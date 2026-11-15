using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using HS2StudioBrowser.Game;
using Studio;
using UnityEngine;

namespace HS2StudioBrowser.Animations
{
    /// <summary>
    /// Studio's Anime panel (Manipulate › character › Anime): its three list columns (group, category,
    /// animation), and the controls under them (speed, pattern, options), which stay visible. Finding the
    /// columns, hiding them, closing the panel, and playing an animation the way the list does, so Animation
    /// Pairing, Timeline and the panel's controls see it as usual.
    /// </summary>
    internal static class AnimePanel
    {
        private static MPCharCtrl _mp;
        private static AnimeGroupList _groups;
        private static AnimeCategoryList _categories;
        private static AnimeList _list;
        private static float _nextFind;

        private static readonly Dictionary<GameObject, CanvasGroup> Hidden = new Dictionary<GameObject, CanvasGroup>();
        private static readonly HashSet<CanvasGroup> Added = new HashSet<CanvasGroup>();

        private static readonly FieldInfo FListSex = AccessTools.Field(typeof(AnimeList), "sex");
        private static readonly FieldInfo FListGroup = AccessTools.Field(typeof(AnimeList), "group");
        private static readonly FieldInfo FListCategory = AccessTools.Field(typeof(AnimeList), "category");
        private static readonly MethodInfo MListSelect = AccessTools.Method(typeof(AnimeList), "OnSelect");
        private static readonly FieldInfo FAnimeControl = AccessTools.Field(typeof(MPCharCtrl), "animeControl");

        /// <summary>Hide Studio's animation box with the lists (the browser draws its own).</summary>
        public static bool HideStudioControls;

        /// <summary>Re-reads Studio's animation box from the character (after the browser changed a value).</summary>
        public static void RefreshStudioControls()
        {
            Find();
            try
            {
                var box = _mp != null ? FAnimeControl?.GetValue(_mp) as AnimeControl : null;
                if (box != null && box.gameObject.activeInHierarchy && box.objectCtrlInfo != null) box.UpdateInfo();
            }
            catch { /* cosmetic */ }
        }

        public const string PairingGuid = "com.hooh.hs2.animpari";
        public const string SearchGuid = "hj.studio.Studio_AnimationSearch";

        private static void Find()
        {
            if (_mp != null && _groups != null && _categories != null && _list != null) return;
            if (Time.unscaledTime < _nextFind) return;
            _nextFind = Time.unscaledTime + 1f;
            if (_mp == null)
            {
                var go = GameObject.Find("StudioScene/Canvas Main Menu/02_Manipulate/00_Chara");
                _mp = go != null ? go.GetComponent<MPCharCtrl>() : UnityEngine.Object.FindObjectOfType<MPCharCtrl>();
            }
            if (_groups == null) _groups = Resources.FindObjectsOfTypeAll<AnimeGroupList>().FirstOrDefault(x => x != null && x.gameObject.scene.IsValid());
            if (_categories == null) _categories = Resources.FindObjectsOfTypeAll<AnimeCategoryList>().FirstOrDefault(x => x != null && x.gameObject.scene.IsValid());
            if (_list == null) _list = Resources.FindObjectsOfTypeAll<AnimeList>().FirstOrDefault(x => x != null && x.gameObject.scene.IsValid());
        }

        /// <summary>True while the Anime panel is open.</summary>
        public static bool Open
        {
            get
            {
                Find();
                return _groups != null && _groups.gameObject.activeInHierarchy;
            }
        }

        /// <summary>The character in the Manipulate panel.</summary>
        public static OCIChar PanelCharacter
        {
            get
            {
                Find();
                try { return _mp != null ? _mp.ociChar : null; }
                catch { return null; }
            }
        }

        /// <summary>Characters selected in the workspace; if none, the one in the Manipulate panel.</summary>
        public static List<OCIChar> Targets()
        {
            var list = StudioTargets.Characters();
            if (list.Count == 0)
            {
                var c = PanelCharacter;
                if (c != null && c.charInfo != null) list.Add(c);
            }
            return list;
        }

        // ------------------------------------------------------------------ hide / close

        public static void SetHidden(bool hide)
        {
            Find();
            var targets = new List<GameObject>();
            if (_groups != null) targets.Add(_groups.gameObject);
            if (_categories != null) targets.Add(_categories.gameObject);
            if (_list != null) targets.Add(_list.gameObject);
            var bar = AnimationTable.SearchBar();
            if (bar != null) targets.Add(bar);
            // Studio's speed / pattern box too, when the browser shows its own copy of it.
            GameObject boxGo = null;
            try { boxGo = (FAnimeControl?.GetValue(_mp) as AnimeControl)?.gameObject; }
            catch { boxGo = null; }
            if (boxGo != null) targets.Add(boxGo);

            foreach (var go in targets)
            {
                var hideThis = hide && (go != boxGo || HideStudioControls);
                if (!Hidden.TryGetValue(go, out var g) || g == null)
                {
                    if (!hideThis) continue;
                    g = go.GetComponent<CanvasGroup>();
                    if (g == null)
                    {
                        g = go.AddComponent<CanvasGroup>();
                        Added.Add(g);
                    }
                    Hidden[go] = g;
                }
                g.alpha = hideThis ? 0f : 1f;
                g.blocksRaycasts = !hideThis;
                g.interactable = !hideThis;
            }
        }

        public static void Restore()
        {
            SetHidden(false);
            foreach (var g in Added) if (g != null) UnityEngine.Object.Destroy(g);
            Added.Clear();
            Hidden.Clear();
        }

        /// <summary>Collapses the character panel's open tab, as clicking it again does.</summary>
        public static void Close()
        {
            Find();
            SetHidden(false);
            try { _mp?.OnClickRoot(-1); }
            catch (Exception ex) { Log.Source.LogDebug("Couldn't close the Anime panel: " + ex.Message); }
        }

        // ------------------------------------------------------------------ play

        /// <summary>
        /// Plays group / category / no on <paramref name="targets"/>. With two or more and Animation Pairing
        /// installed, it goes through Studio's list (Pairing hooks it and gives each character its partner
        /// animation); otherwise each character gets it directly, and the panel's own character through the
        /// panel, so its speed and pattern controls follow. Returns how many characters now play it.
        /// </summary>
        private static string Current(OCIChar c)
        {
            try
            {
                var a = c.oiCharInfo.animeInfo;
                return $"{a.group}/{a.category}/{a.no}";
            }
            catch { return ""; }
        }

        public static int Play(int group, int category, int no, List<OCIChar> targets)
        {
            Find();
            var panel = PanelCharacter;
            var before = targets.ToDictionary(c => c, Current);
            var paired = false;
            using (AnimationTable.Unfiltered())
            {
                if (targets.Count >= 2 && BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(PairingGuid) &&
                    _list != null && _mp != null && panel != null && MListSelect != null && FListGroup != null && FListCategory != null)
                {
                    try
                    {
                        FListSex?.SetValue(_list, panel.oiCharInfo.sex == 0 ? AnimeGroupList.SEX.Male : AnimeGroupList.SEX.Female);
                        FListGroup.SetValue(_list, group);
                        FListCategory.SetValue(_list, category);
                        MListSelect.Invoke(_list, new object[] { no });
                        paired = true;
                    }
                    catch (Exception ex)
                    {
                        Log.Source.LogWarning("Playing through Studio's list (for Animation Pairing) failed, playing directly: " + (ex.InnerException ?? ex).Message);
                    }
                }

                if (!paired)
                {
                    foreach (var c in targets)
                    {
                        try
                        {
                            if (c == panel && _mp != null) _mp.LoadAnime(c.oiCharInfo.sex == 0 ? AnimeGroupList.SEX.Male : AnimeGroupList.SEX.Female, group, category, no);
                            else c.LoadAnime(group, category, no);
                        }
                        catch (Exception ex)
                        {
                            Log.Source.LogWarning($"Playing {group}/{category}/{no} on {StudioTargets.NameOf(c)} failed: {ex.Message}");
                        }
                    }
                }
                else
                {
                    try { (FAnimeControl?.GetValue(_mp) as AnimeControl)?.UpdateInfo(); }
                    catch { /* cosmetic */ }
                }
            }

            // With Pairing, partners get a matching animation from another category: count what changed too.
            var want = $"{group}/{category}/{no}";
            return targets.Count(c => Current(c) == want || (paired && Current(c) != before[c]));
        }
    }
}

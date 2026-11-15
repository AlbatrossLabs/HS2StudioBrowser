using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using HS2StudioBrowser.Files;
using Studio;
using UnityEngine;
using UnityEngine.UI;

namespace HS2StudioBrowser.Characters
{
    /// <summary>
    /// Studio's two character lists (Add > Character > Female / Male, one CharaList each): finding them, telling
    /// which is open, hiding one without closing it, closing it the way the Add menu does, and mirroring file
    /// changes into it (and into Browser Folders' copy, if that's installed) without re-reading every card.
    /// </summary>
    internal static class CharaLists
    {
        private static CharaList[] _lists;
        private static float _nextFind;
        private static readonly FieldInfo FSex = AccessTools.Field(typeof(CharaList), "sex");
        private static readonly FieldInfo FSort = AccessTools.Field(typeof(CharaList), "charaFileSort");
        private static readonly FieldInfo FLoad = AccessTools.Field(typeof(CharaList), "buttonLoad");
        private static readonly FieldInfo FChange = AccessTools.Field(typeof(CharaList), "buttonChange");

        private static readonly Dictionary<CharaList, CanvasGroup> Groups = new Dictionary<CharaList, CanvasGroup>();
        private static readonly HashSet<CanvasGroup> Added = new HashSet<CanvasGroup>();
        private static CharaList _hidden;

        private static CharaList[] Lists()
        {
            if ((_lists == null || _lists.Any(l => l == null)) && Time.unscaledTime >= _nextFind)
            {
                _nextFind = Time.unscaledTime + 2f;
                _lists = Resources.FindObjectsOfTypeAll<CharaList>().Where(l => l != null && l.gameObject.scene.IsValid()).ToArray();
            }
            return _lists ?? new CharaList[0];
        }

        public static int SexOf(CharaList list)
        {
            try { return (int)FSex.GetValue(list); }
            catch { return -1; }
        }

        /// <summary>The character list on screen, if any.</summary>
        public static CharaList Open()
        {
            foreach (var l in Lists())
                if (l != null && l.isActiveAndEnabled)
                    return l;
            return null;
        }

        /// <summary>Hides (or shows) the open list's panel without closing it: invisible and ignoring clicks.</summary>
        public static void SetHidden(CharaList list)
        {
            if (_hidden == list) return;
            if (_hidden != null) Apply(_hidden, false);
            _hidden = list;
            if (list != null) Apply(list, true);
        }

        private static void Apply(CharaList list, bool hide)
        {
            if (list == null) return;
            if (!Groups.TryGetValue(list, out var g) || g == null)
            {
                g = list.GetComponent<CanvasGroup>();
                if (g == null)
                {
                    g = list.gameObject.AddComponent<CanvasGroup>();
                    Added.Add(g);
                }
                Groups[list] = g;
            }
            g.alpha = hide ? 0f : 1f;
            g.blocksRaycasts = !hide;
            g.interactable = !hide;
        }

        public static void Restore()
        {
            SetHidden(null);
            foreach (var g in Added) if (g != null) UnityEngine.Object.Destroy(g);
            Added.Clear();
            Groups.Clear();
        }

        /// <summary>Closes the list the way its Add-menu button does (so the button's highlight follows).</summary>
        public static void Close(CharaList list)
        {
            if (list == null) return;
            try
            {
                foreach (var ctrl in Resources.FindObjectsOfTypeAll<AddButtonCtrl>())
                {
                    if (ctrl == null || !ctrl.gameObject.scene.IsValid()) continue;
                    var infos = AccessTools.Field(typeof(AddButtonCtrl), "commonInfo")?.GetValue(ctrl) as Array;
                    if (infos == null) continue;
                    for (var i = 0; i < infos.Length; i++)
                    {
                        var info = infos.GetValue(i);
                        var obj = AccessTools.Field(info.GetType(), "obj")?.GetValue(info) as GameObject;
                        if (obj == null) continue;
                        if (obj == list.gameObject || list.transform.IsChildOf(obj.transform))
                        {
                            if (obj.activeSelf) ctrl.OnClick(i); // clicking the open one closes it
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug("Couldn't close the character list through the Add menu: " + ex.Message);
            }
            list.gameObject.SetActive(false);
        }

        /// <summary>Mirrors renames, moves, removals and new files into Studio's character lists, in memory.</summary>
        public static void Sync(IList<ListChange> changes)
        {
            if (changes == null || changes.Count == 0) return;
            foreach (var list in Lists())
            {
                if (list == null) continue;
                try { SyncOne(list, changes); }
                catch (Exception ex) { Log.Source.LogDebug("Couldn't update Studio's character list: " + ex.Message); }
            }
        }

        private static void SyncOne(CharaList list, IList<ListChange> changes)
        {
            var sort = FSort?.GetValue(list) as CharaFileSort;
            if (sort == null) return;
            var shown = sort.cfiList;
            var root = Path.Combine(Path.Combine(Path.Combine(BepInEx.Paths.GameRootPath, "UserData"), "chara"), SexOf(list) == 0 ? "male" : "female");
            var bf = BrowserFoldersEntry(list, out var backup);

            // Drop the selection first; indices shift when entries go.
            try { sort.select = -1; } catch { /* stale index */ }
            AccessTools.Field(typeof(CharaFileSort), "m_Select")?.SetValue(sort, -1);
            (FLoad?.GetValue(list) as Button)?.Let(b => b.interactable = false);
            (FChange?.GetValue(list) as Button)?.Let(b => b.interactable = false);

            bool movedOrRemoved = false, removedShown = false;
            foreach (var c in changes)
            {
                if (c.Added)
                {
                    // Only lists of the same sex, and only Browser Folders' copy can take new entries cheaply.
                    if (backup != null && c.NewPath != null && FileOps.IsInside(c.NewPath, root))
                    {
                        backup.Add(new CharaFileInfo(c.NewPath, c.NewName ?? "") { time = File.GetLastWriteTime(c.NewPath) });
                        movedOrRemoved = true;
                    }
                    continue;
                }
                foreach (var l in backup != null ? new[] { shown, backup } : new[] { shown })
                {
                    for (var i = l.Count - 1; i >= 0; i--)
                    {
                        var info = l[i];
                        if (info == null || !FileOps.Same(info.file, c.OldPath)) continue;
                        if (c.Removed)
                        {
                            if (l == shown)
                            {
                                if (info.node != null) UnityEngine.Object.Destroy(info.node.gameObject);
                                removedShown = true;
                            }
                            l.RemoveAt(i);
                            movedOrRemoved = true;
                            continue;
                        }
                        if (c.NewPath != null && !FileOps.Same(c.NewPath, info.file))
                        {
                            info.file = c.NewPath;
                            movedOrRemoved = true;
                        }
                        if (c.NewName != null)
                        {
                            info.name = c.NewName;
                            if (info.node != null) info.node.text = c.NewName;
                        }
                    }
                }
            }

            if (bf != null && movedOrRemoved)
            {
                // Browser Folders rebuilds the shown list from its copy and filters it by folder, without reading cards.
                AccessTools.Field(bf.GetType(), "RefilterInProgress")?.SetValue(bf, true);
                list.InitCharaList(true);
            }
            else if (removedShown)
            {
                for (var i = 0; i < shown.Count; i++) shown[i].index = i;
            }
        }

        /// <summary>Browser Folders' entry for this list and its full (unfiltered) copy, if it has one.</summary>
        private static object BrowserFoldersEntry(CharaList list, out List<CharaFileInfo> backup)
        {
            backup = null;
            try
            {
                var t = AccessTools.TypeByName("BrowserFolders.StudioCharaFolders");
                var dict = t != null ? AccessTools.Field(t, "_CharaListEntries")?.GetValue(null) as System.Collections.IDictionary : null;
                if (dict == null || !dict.Contains(list.name)) return null;
                var entry = dict[list.name];
                backup = AccessTools.Field(entry.GetType(), "_backupFileInfos")?.GetValue(entry) as List<CharaFileInfo>;
                return backup != null ? entry : null;
            }
            catch
            {
                backup = null;
                return null;
            }
        }

        private static void Let<T>(this T obj, Action<T> act) where T : class
        {
            if (obj != null) act(obj);
        }
    }
}

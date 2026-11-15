using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HS2StudioBrowser.Outfits
{
    /// <summary>
    /// "Replace Studio's Costume list": while the Costume panel is open and the browser is showing, Studio's
    /// own list is made invisible (a CanvasGroup at alpha 0 that ignores clicks; it stays active, so Studio and
    /// other plugins keep working with it) and Browser Folders' "Outfit folder" window isn't drawn. Camera
    /// Sweep picks outfits with Studio's list, so while it's picking everything is shown as normal.
    /// </summary>
    internal static class CostumeOverride
    {
        /// <summary>Skip Browser Folders' outfit window this frame.</summary>
        public static bool HideBrowserFolders;

        private static CanvasGroup _group;
        private static bool _addedGroup;
        private static bool _hidden;
        private static float _origAlpha = 1f;
        private static bool _origRaycasts = true, _origInteractable = true;

        public static void Install(Harmony harmony)
        {
            try
            {
                var t = AccessTools.TypeByName("BrowserFolders.StudioOutfitFolders");
                var onGui = t != null ? AccessTools.Method(t, "OnGui") : null;
                if (onGui != null) harmony.Patch(onGui, prefix: new HarmonyMethod(typeof(CostumeOverride), nameof(BrowserFoldersOnGuiPrefix)));
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug("Browser Folders hook skipped: " + ex.Message);
            }
        }

        private static bool BrowserFoldersOnGuiPrefix() => !HideBrowserFolders;

        /// <summary>Shows or hides Studio's Costume list without deactivating it.</summary>
        public static void SetStudioListHidden(bool hide)
        {
            if (hide == _hidden && (!hide || _group != null)) return;
            var root = CostumeList.CostumeRoot();
            if (root == null)
            {
                _hidden = false;
                return;
            }

            if (_group == null || _group.gameObject != root)
            {
                _group = root.GetComponent<CanvasGroup>();
                _addedGroup = _group == null;
                if (_addedGroup) _group = root.AddComponent<CanvasGroup>();
                _origAlpha = _group.alpha;
                _origRaycasts = _group.blocksRaycasts;
                _origInteractable = _group.interactable;
            }

            if (hide)
            {
                _group.alpha = 0f;
                _group.blocksRaycasts = false;
                _group.interactable = false;
            }
            else
            {
                _group.alpha = _origAlpha;
                _group.blocksRaycasts = _origRaycasts;
                _group.interactable = _origInteractable;
            }
            _hidden = hide;
        }

        /// <summary>Puts everything back (plugin unloading).</summary>
        public static void Restore()
        {
            HideBrowserFolders = false;
            if (_group != null)
            {
                _group.alpha = _origAlpha;
                _group.blocksRaycasts = _origRaycasts;
                _group.interactable = _origInteractable;
                if (_addedGroup) UnityEngine.Object.Destroy(_group);
            }
            _group = null;
            _hidden = false;
        }
    }
}

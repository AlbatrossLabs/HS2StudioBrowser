using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Studio;
using UnityEngine;

namespace HS2StudioBrowser.Poses
{
    /// <summary>
    /// Studio's Pose list (character › Kinematics › Pose), which Pose Tools fills with its folders and .png
    /// poses. Finding it, hiding it (a CanvasGroup, so it neither shows nor takes clicks but stays open),
    /// closing it as its own close button does, re-reading it, and pointing Pose Tools' folder (where its
    /// list reads from and new saves go) at a folder.
    /// </summary>
    internal static class PoseList
    {
        private static PauseRegistrationList _list;
        private static float _nextFind;
        private static CanvasGroup _group;
        private static bool _addedGroup;

        private static FieldInfo _fToolsFolder;
        private static bool _toolsResolved;

        public static PauseRegistrationList Find()
        {
            if (_list == null && Time.unscaledTime >= _nextFind)
            {
                _nextFind = Time.unscaledTime + 1f;
                _list = Resources.FindObjectsOfTypeAll<PauseRegistrationList>().FirstOrDefault(l => l != null && l.gameObject.scene.IsValid());
            }
            return _list;
        }

        public static bool Open
        {
            get
            {
                var l = Find();
                return l != null && l.gameObject.activeInHierarchy;
            }
        }

        /// <summary>The character the Pose list belongs to (the one in the Manipulate panel).</summary>
        public static OCIChar Character
        {
            get
            {
                var l = Find();
                try { return l != null ? l.ociChar : null; }
                catch { return null; }
            }
        }

        public static void SetHidden(bool hide)
        {
            var l = Find();
            if (l == null) return;
            if (_group == null)
            {
                if (!hide) return;
                _group = l.GetComponent<CanvasGroup>();
                if (_group == null)
                {
                    _group = l.gameObject.AddComponent<CanvasGroup>();
                    _addedGroup = true;
                }
            }
            _group.alpha = hide ? 0f : 1f;
            _group.blocksRaycasts = !hide;
            _group.interactable = !hide;
        }

        public static void Restore()
        {
            SetHidden(false);
            if (_addedGroup && _group != null) UnityEngine.Object.Destroy(_group);
            _group = null;
            _addedGroup = false;
        }

        /// <summary>Closes the Pose list as its close button does.</summary>
        public static void Close()
        {
            var l = Find();
            if (l == null) return;
            SetHidden(false);
            try { AccessTools.Method(typeof(PauseRegistrationList), "OnClickClose")?.Invoke(l, null); }
            catch (Exception ex) { Log.Source.LogDebug("Couldn't close the Pose list: " + (ex.InnerException ?? ex).Message); }
        }

        /// <summary>Re-reads the Pose list (after files were added, renamed, moved or recycled).</summary>
        public static void Refresh()
        {
            var l = Find();
            if (l == null || !l.gameObject.activeInHierarchy || Character == null) return;
            try { AccessTools.Method(typeof(PauseRegistrationList), "InitList")?.Invoke(l, null); }
            catch (Exception ex) { Log.Source.LogDebug("Couldn't refresh the Pose list: " + (ex.InnerException ?? ex).Message); }
        }

        // ------------------------------------------------------------------ Pose Tools

        private static FieldInfo ToolsFolderField()
        {
            if (_toolsResolved) return _fToolsFolder;
            _toolsResolved = true;
            var t = AccessTools.TypeByName("KK_Plugins.PoseTools.Plugin");
            _fToolsFolder = t != null ? AccessTools.Field(t, "CurrentDirectory") : null;
            if (_fToolsFolder == null) Log.Source.LogWarning("Pose Tools' folder setting wasn't found; poses will save where Pose Tools last was.");
            return _fToolsFolder;
        }

        /// <summary>The folder Pose Tools lists and saves into.</summary>
        public static string ToolsFolder
        {
            get
            {
                try { return (ToolsFolderField()?.GetValue(null) as DirectoryInfo)?.FullName; }
                catch { return null; }
            }
        }

        /// <summary>Points Pose Tools at <paramref name="folder"/> (its list and new saves follow). False if it couldn't.</summary>
        public static bool SetToolsFolder(string folder)
        {
            var f = ToolsFolderField();
            if (f == null || string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;
            try
            {
                var current = f.GetValue(null) as DirectoryInfo;
                if (current != null && Files.FileOps.Same(current.FullName, folder)) return true;
                f.SetValue(null, new DirectoryInfo(folder));
                Refresh();
                return true;
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug("Couldn't set Pose Tools' folder: " + ex.Message);
                return false;
            }
        }
    }
}

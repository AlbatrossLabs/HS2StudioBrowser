using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using HS2StudioBrowser.Game;
using Studio;
using UnityEngine;

namespace HS2StudioBrowser.Outfits
{
    /// <summary>
    /// What the browser needs from Studio: which characters an outfit goes on, whether Studio's own Costume
    /// list is showing, and refreshing that list after files change (so it, and Browser Folders' filtered
    /// view of it, show the new names and locations).
    /// </summary>
    internal static class CostumeList
    {
        private static MPCharCtrl _mpCharCtrl;
        private static object _costumeInfo;
        private static FieldInfo _fCostumeInfo, _fSex, _fObjRoot;
        private static MethodInfo _mInitList;
        private static PropertyInfo _pOciChar;
        private static bool _resolved;

        private static bool Resolve()
        {
            if (_resolved && _mpCharCtrl != null) return _costumeInfo != null;
            _resolved = true;
            try
            {
                var go = GameObject.Find("StudioScene/Canvas Main Menu/02_Manipulate/00_Chara");
                _mpCharCtrl = go != null ? go.GetComponent<MPCharCtrl>() : UnityEngine.Object.FindObjectOfType<MPCharCtrl>();
                if (_mpCharCtrl == null) return false;

                _fCostumeInfo = AccessTools.Field(typeof(MPCharCtrl), "costumeInfo");
                _costumeInfo = _fCostumeInfo?.GetValue(_mpCharCtrl);
                if (_costumeInfo == null) return false;

                var t = _costumeInfo.GetType();
                _fSex = AccessTools.Field(t, "sex");
                _fObjRoot = AccessTools.Field(t, "objRoot");
                _mInitList = AccessTools.Method(t, "InitList", new[] { typeof(int) });
                _pOciChar = AccessTools.Property(t, "ociChar");
                return true;
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug("StudioBridge: " + ex.Message);
                _costumeInfo = null;
                return false;
            }
        }

        /// <summary>True while Studio's Costume list (Manipulate › character › Costume) is on screen.</summary>
        public static bool CostumeListVisible()
        {
            if (!Resolve()) return false;
            try
            {
                var root = _fObjRoot?.GetValue(_costumeInfo) as GameObject;
                return root != null && root.activeInHierarchy;
            }
            catch { return false; }
        }

        /// <summary>The Costume list's root object (Manipulate > character > Costume).</summary>
        public static GameObject CostumeRoot()
        {
            if (!Resolve()) return null;
            try { return _fObjRoot?.GetValue(_costumeInfo) as GameObject; }
            catch { return null; }
        }

        /// <summary>Collapses the character panel's open tab, as clicking the open tab again does.</summary>
        public static void CloseCostumePanel()
        {
            if (!Resolve() || _mpCharCtrl == null) return;
            try { _mpCharCtrl.OnClickRoot(-1); }
            catch (Exception ex) { Log.Source.LogDebug("Couldn't close the Costume panel: " + ex.Message); }
        }

        /// <summary>The character Studio's Manipulate panel is showing, if any.</summary>
        public static OCIChar PanelCharacter()
        {
            if (!Resolve()) return null;
            try { return _pOciChar?.GetValue(_costumeInfo, null) as OCIChar; }
            catch { return null; }
        }

        /// <summary>Characters selected in the workspace; if none, the one in the Manipulate panel.</summary>
        public static List<OCIChar> TargetCharacters()
        {
            var list = StudioTargets.Characters();
            if (list.Count == 0)
            {
                var panel = PanelCharacter();
                if (panel != null && panel.charInfo != null) list.Add(panel);
            }
            return list;
        }

        /// <summary>Re-reads Studio's Costume list from disk (slow with many outfits: Studio parses every card).</summary>
        public static void RefreshCostumeList()
        {
            if (!Resolve() || _fSex == null || _mInitList == null) return;
            try
            {
                var sex = (int)_fSex.GetValue(_costumeInfo);
                if (sex != 0 && sex != 1) return; // never filled; Studio reads it fresh when it opens
                _fSex.SetValue(_costumeInfo, -1);
                _mInitList.Invoke(_costumeInfo, new object[] { sex });
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug("Couldn't refresh Studio's Costume list: " + (ex.InnerException ?? ex).Message);
            }
        }

        /// <summary>
        /// Applies renames, moves, removals and new files to Studio's Costume list in memory, so it doesn't
        /// have to re-read every card. With Browser Folders, its full list is updated too and its folder filter
        /// re-applied (Browser Folders does that without touching the disk). Returns false if a full re-read
        /// is needed to show a change (a new file without Browser Folders).
        /// </summary>
        public static bool SyncCostumeList(IList<ListChange> changes)
        {
            if (changes == null || changes.Count == 0 || !Resolve()) return true;
            try
            {
                var sex = _fSex != null ? (int)_fSex.GetValue(_costumeInfo) : -1;
                if (sex != 0 && sex != 1) return true; // list never filled: Studio reads it fresh when opened

                var fileSort = AccessTools.Field(_costumeInfo.GetType(), "fileSort")?.GetValue(_costumeInfo) as CharaFileSort;
                if (fileSort == null) return false;
                var shown = fileSort.cfiList;
                var bf = BrowserFoldersList(out var bfEntry);

                // Drop the selection first; indices shift when entries go.
                var selectField = AccessTools.Field(typeof(CharaFileSort), "m_Select");
                try { if (fileSort.select >= 0 && fileSort.select < shown.Count) fileSort.select = -1; }
                catch { /* stale index */ }
                selectField?.SetValue(fileSort, -1);
                var loadButton = AccessTools.Field(_costumeInfo.GetType(), "buttonLoad")?.GetValue(_costumeInfo) as UnityEngine.UI.Button;
                if (loadButton != null) loadButton.interactable = false;

                bool removedAny = false, movedAny = false, needFull = false;
                foreach (var c in changes)
                {
                    if (c.Added)
                    {
                        if (bf != null)
                        {
                            bf.Add(new CharaFileInfo(c.NewPath, c.NewName ?? "") { time = System.IO.File.GetLastWriteTime(c.NewPath) });
                            movedAny = true;
                        }
                        else needFull = true;
                        continue;
                    }

                    foreach (var list in bf != null ? new[] { shown, bf } : new[] { shown })
                    {
                        for (var i = list.Count - 1; i >= 0; i--)
                        {
                            var info = list[i];
                            if (info == null || !Files.FileOps.Same(info.file, c.OldPath)) continue;
                            if (c.Removed)
                            {
                                if (list == shown && info.node != null) UnityEngine.Object.Destroy(info.node.gameObject);
                                list.RemoveAt(i);
                                removedAny = true;
                                continue;
                            }
                            if (c.NewPath != null && !Files.FileOps.Same(c.NewPath, info.file))
                            {
                                info.file = c.NewPath;
                                movedAny = true;
                            }
                            if (c.NewName != null)
                            {
                                info.name = c.NewName;
                                if (info.node != null) info.node.text = c.NewName;
                            }
                        }
                    }
                }

                if (bf != null && bfEntry != null && (movedAny || removedAny))
                {
                    // Browser Folders rebuilds the shown list from its full list and filters it by folder.
                    AccessTools.Field(bfEntry.GetType(), "RefilterInProgress")?.SetValue(bfEntry, true);
                    _fSex.SetValue(_costumeInfo, -1);
                    _mInitList.Invoke(_costumeInfo, new object[] { sex });
                }
                else if (removedAny)
                {
                    for (var i = 0; i < shown.Count; i++) shown[i].index = i;
                }
                return !needFull;
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug("Couldn't update Studio's Costume list: " + (ex.InnerException ?? ex).Message);
                return false;
            }
        }

        /// <summary>Browser Folders' full (unfiltered) copy of the Costume list, if it's installed and has one.</summary>
        private static List<CharaFileInfo> BrowserFoldersList(out object entry)
        {
            entry = null;
            try
            {
                var t = AccessTools.TypeByName("BrowserFolders.StudioOutfitFolders");
                if (t == null) return null;
                entry = AccessTools.Field(t, "_costumeInfoEntry")?.GetValue(null);
                if (entry == null) return null;
                return AccessTools.Field(entry.GetType(), "_backupFileInfos")?.GetValue(entry) as List<CharaFileInfo>;
            }
            catch
            {
                entry = null;
                return null;
            }
        }
    }
}

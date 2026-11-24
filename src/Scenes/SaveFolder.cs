using System;
using System.IO;
using HarmonyLib;
using HS2StudioBrowser.Files;
using HS2StudioBrowser.Game;
using HS2StudioBrowser.UI;
using UnityEngine;

namespace HS2StudioBrowser.Scenes
{
    /// <summary>Where System › Save puts a new scene.</summary>
    public enum SaveTarget
    {
        LoadedScene,
        OpenFolder,
        Studio,
    }

    /// <summary>
    /// System › Save: puts the new scene in the folder of the scene you loaded (or the folder open in the
    /// browser), then offers to name it. Studio always saves a new file named by the date into studio\scene
    /// (or Browser Folders' current folder); only saves from Studio's Save button are changed, so autosave
    /// and other plugins that save scenes keep their own paths.
    /// </summary>
    internal static class SaveFolder
    {
        private static SceneBrowserPlugin _plugin;
        private static string _root;
        private static bool _inSave;

        private static string _loadedPath;     // the scene loaded now (null: a new scene)
        private static string _openFolder;     // the folder open in the browser

        // The name prompt after a save.
        private static string _saved;
        private static string _name;
        private static bool _focus;
        private static Rect _rect = new Rect(0, 0, 420, 0);
        private const int WindowId = 0x5B5E;

        public static void Install(Harmony harmony, SceneBrowserPlugin plugin, string root)
        {
            _plugin = plugin;
            _root = root;
            Patch(harmony, AccessTools.Method(typeof(Studio.SystemButtonCtrl), "OnClickSave"), nameof(SavePrefix), nameof(SavePostfix));
            Patch(harmony, AccessTools.Method(typeof(Studio.SceneInfo), "Save", new[] { typeof(string) }), nameof(SceneSavePrefix), nameof(SceneSavePostfix));
            Patch(harmony, AccessTools.Method(typeof(Studio.Studio), "LoadScene", new[] { typeof(string) }), nameof(LoadPrefix), null);
            Patch(harmony, AccessTools.Method(typeof(Studio.Studio), "LoadSceneCoroutine", new[] { typeof(string) }), nameof(LoadPrefix), null);
            Patch(harmony, AccessTools.Method(typeof(Studio.SystemButtonCtrl), "OnSelectInitYes"), null, nameof(InitPostfix));
        }

        private static void Patch(Harmony harmony, System.Reflection.MethodBase m, string prefix, string postfix)
        {
            if (m == null)
            {
                Log.Source.LogWarning("Save folder: a Studio method wasn't found; saving stays as Studio does it.");
                return;
            }
            try
            {
                harmony.Patch(m,
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(SaveFolder), prefix) { priority = Priority.Last },
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(SaveFolder), postfix));
            }
            catch (Exception ex)
            {
                Log.Source.LogWarning($"Save folder: couldn't hook {m.Name}: {ex.Message}");
            }
        }

        /// <summary>The folder open in the browser (for a new scene, and for the Open folder setting).</summary>
        public static void FolderOpened(string folder) => _openFolder = folder;

        private static void LoadPrefix(string __0)
        {
            if (string.IsNullOrEmpty(__0)) return;
            _loadedPath = Path.GetFullPath(__0);
            Loaded(__0);
        }

        private static void InitPostfix()
        {
            _loadedPath = null;
            _lastLoadedName = null;
        }

        private static void SavePrefix() => _inSave = true;
        private static void SavePostfix() => _inSave = false;

        private static void SceneSavePrefix(ref string __0)
        {
            if (!_inSave || string.IsNullOrEmpty(__0)) return;
            try
            {
                var folder = Target();
                if (folder == null) return;
                var path = Path.Combine(folder, Path.GetFileName(__0));
                if (!FileOps.Same(path, __0)) Log.Source.LogDebug($"Saving the scene into {folder}");
                __0 = path;
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug("Save folder: " + ex.Message);
            }
        }

        private static void SceneSavePostfix(string __0, bool __result)
        {
            if (!_inSave || !__result || string.IsNullOrEmpty(__0)) return;
            var path = Path.GetFullPath(__0);
            _loadedPath = path;   // the next save goes to the same folder
            _plugin.FilesChanged();
            if (!_plugin.NameOnSave.Value) return;
            _saved = path;
            _name = SuggestedName();
            _focus = true;
        }

        private static string _lastLoadedName;

        private static string SuggestedName()
        {
            // The loaded scene's name, if it wasn't one Studio named by the date itself.
            var n = _lastLoadedName;
            return string.IsNullOrEmpty(n) || IsDateName(n) ? "" : n;
        }

        private static bool IsDateName(string stem) =>
            System.Text.RegularExpressions.Regex.IsMatch(stem, @"^\d{4}_\d{4}_\d{4}_\d{2}_\d{3}$");

        private static string Target()
        {
            string folder = null;
            switch (_plugin.SaveInto.Value)
            {
                case SaveTarget.LoadedScene:
                    folder = _loadedPath != null ? Path.GetDirectoryName(_loadedPath) : _openFolder;
                    break;
                case SaveTarget.OpenFolder:
                    folder = _openFolder;
                    break;
            }
            if (folder == null || !Directory.Exists(folder)) return null;
            if (!FileOps.Same(folder, _root) && !FileOps.IsInside(folder, _root)) return null;   // only inside studio\scene
            return folder;
        }

        // ---------------------------------------------------------------- name prompt

        public static void Draw()
        {
            if (_saved == null) return;
            if (!File.Exists(_saved))
            {
                _saved = null;
                InputGuard.Set(typeof(SaveFolder), false);
                return;
            }
            InputGuard.Set(typeof(SaveFolder), true);
            GUI.skin = Theme.Skin;
            if (_rect.height < 10) _rect = new Rect((Screen.width - 420) / 2f, Screen.height * 0.3f, 420, 10);
            _rect = GUILayout.Window(WindowId, _rect, DrawPrompt, "Name the saved scene");
            GUI.FocusWindow(WindowId);
        }

        private static void DrawPrompt(int id)
        {
            var e = Event.current;
            var enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
            var esc = e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape;
            if (enter || esc) e.Use();

            var folder = Path.GetDirectoryName(_saved);
            var where = FileOps.Same(folder, _root) ? "studio\\scene" : "studio\\scene\\" + folder.Substring(_root.Length).TrimStart('\\', '/');
            GUILayout.Label($"Saved in {Theme.Hint(where)} as {Theme.Hint(Path.GetFileName(_saved))}", Theme.Rich);
            GUILayout.Space(4);
            GUI.SetNextControlName("sb_save_name");
            _name = GUILayout.TextField(_name ?? "");
            if (_focus && e.type == EventType.Repaint)
            {
                GUI.FocusControl("sb_save_name");
                _focus = false;
            }

            var stem = _name.Trim();
            string note = null;
            var ok = stem.Length > 0 && FileOps.IsValidName(stem, out note);
            if (ok)
            {
                var target = Path.Combine(folder, stem + ".png");
                if (File.Exists(target) && !FileOps.Same(target, _saved))
                    note = $"{stem}.png is taken: it'll be saved as {Path.GetFileName(FileOps.FreePath(folder, stem + ".png"))}";
                else note = null;
            }
            GUILayout.Label(Theme.Hint(note ?? "Enter names it, Esc keeps the date name."), Theme.Rich);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Keep date name", GUILayout.Width(120)) || esc) Close();
            GUI.enabled = ok;
            if ((GUILayout.Button("Name it", Theme.Primary, GUILayout.Width(90)) || enter) && ok) Rename(stem);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUI.DragWindow();
        }

        private static void Rename(string stem)
        {
            try
            {
                var renamed = FileOps.RenameFile(_saved, stem);
                if (FileOps.Same(_loadedPath ?? "", _saved)) _loadedPath = renamed;
                _lastLoadedName = Path.GetFileNameWithoutExtension(renamed);
                _plugin.FilesChanged();
                Log.Source.LogInfo($"Saved scene named {Path.GetFileName(renamed)}");
            }
            catch (Exception ex)
            {
                Log.Source.LogWarning("Couldn't rename the saved scene: " + ex.Message);
            }
            Close();
        }

        private static void Close()
        {
            _saved = null;
            InputGuard.Set(typeof(SaveFolder), false);
        }

        /// <summary>Remembers the loaded scene's name for the prompt (set when a scene loads).</summary>
        public static void Loaded(string path) => _lastLoadedName = string.IsNullOrEmpty(path) ? null : Path.GetFileNameWithoutExtension(path);
    }
}

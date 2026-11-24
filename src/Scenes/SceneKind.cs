using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using HS2StudioBrowser.Files;
using HS2StudioBrowser.Game;
using HS2StudioBrowser.UI;

namespace HS2StudioBrowser.Scenes
{
    /// <summary>Studio scenes in UserData\studio\scene, loaded or imported, standing in for Studio's Load screen.</summary>
    internal sealed class SceneKind : BrowserKind
    {
        private readonly SceneBrowserPlugin _plugin;
        private readonly List<CardAction> _actions;
        private static bool _hideBrowserFolders;
        private bool _loading;

        public SceneKind(SceneBrowserPlugin plugin)
        {
            _plugin = plugin;
            _actions = new List<CardAction>
            {
                new CardAction
                {
                    Label = "Import", MenuLabel = "Import (add to this scene)", Width = 80,
                    Tooltip = "Add this scene's characters and objects to the current scene",
                    CanRun = _ => !_loading,
                    Run = Import,
                },
                new CardAction
                {
                    Label = "Load", Primary = true, Width = 80,
                    Tooltip = "Replace the current scene with this one (double-click or Enter does the same)",
                    CanRun = _ => !_loading,
                    Run = Load,
                },
            };
        }

        public override string DefaultTagSet => TagSets.Scenes;
        public override string[] OlderTagSets => new[] { TagSets.ScenesV1 };

        private static readonly List<TagFacet> Auto = new List<TagFacet>
        {
            AutoFacet.Make("Cast", true, "F", "FF", "FFF", "M", "MM", "MMM", "MF", "FFM", "MMF", "4+", "None"),
        };

        public override IList<TagFacet> AutoFacets => Auto;

        /// <summary>Who is in the scene, counted from the characters stored inside it.</summary>
        public override IEnumerable<string> ReadAutoTags(string path)
        {
            var cast = SceneBytes.CastOf(File.ReadAllBytes(path));
            yield return "Cast:" + SceneBytes.CastLabel(cast.Key, cast.Value);
        }
        public override string Id => "scenes";
        public override string Title => "Scene Browser";
        public override string Noun => "scene";
        public override string Nouns => "scenes";
        public override string UserDataFolder => Path.Combine("studio", "scene");
        public override string StudioListName => "Load screen";
        public override string NameLabel => "Scene name";
        public override float ThumbAspect => 180f / 320f;
        public override bool HasSex => false;
        public override bool HasStoredName => false;
        public override int DefaultThumbWidth => 200;
        public override int MaxThumbWidth => 420;
        public override SortBy DefaultSort => SortBy.Newest;

        public override string ReadName(string path) => SceneFile.ReadName(path);
        public override void WriteName(string path, string newName) { } // scenes store no name; Rename changes the file name only
        public override byte[] ReadPluginData(string path) => SceneFile.ReadPluginData(path);
        public override List<MissingItem> FindMissing(string path) => SceneFile.FindMissing(path);

        public override IList<CardAction> Actions => _actions;

        public override string TargetLine(int sex) =>
            _loading ? Theme.Warn("Loading...") : Theme.Hint("Load replaces the current scene  ·  Import adds its characters and objects to it");

        private ActionResult Load(string path, int sex)
        {
            if (!File.Exists(path)) return ActionResult.Fail("That file is gone.");
            if (_loading) return ActionResult.Fail("Still loading the last scene.");
            _plugin.StartCoroutine(LoadRoutine(path));
            return ActionResult.Ok($"Loading {Path.GetFileNameWithoutExtension(path)}...");
        }

        private IEnumerator LoadRoutine(string path)
        {
            _loading = true;
            var wasStandingIn = LoadScreen.Open;
            try
            {
                yield return Studio.Studio.Instance.LoadSceneCoroutine(path);
            }
            finally
            {
                _loading = false;
            }
            if (_plugin.CloseAfterLoading.Value)
            {
                if (wasStandingIn && LoadScreen.Open) LoadScreen.Close();
                else if (_plugin.WindowVisible) _plugin.HideWindow();
            }
        }

        private ActionResult Import(string path, int sex)
        {
            if (!File.Exists(path)) return ActionResult.Fail("That file is gone.");
            try
            {
                return Studio.Studio.Instance.ImportScene(path)
                    ? ActionResult.Ok($"Imported {Path.GetFileNameWithoutExtension(path)} into the current scene.")
                    : ActionResult.Fail("Studio couldn't import that scene.");
            }
            catch (Exception ex)
            {
                Log.Source.LogWarning($"Importing {Path.GetFileName(path)} failed: {ex}");
                return ActionResult.Fail("Import failed: " + ex.Message);
            }
        }

        public override bool StudioListOpen(out int sex)
        {
            sex = -1;
            return LoadScreen.Open;
        }

        public override void SetStudioListHidden(bool hidden) => LoadScreen.SetHidden(hidden);
        public override void CloseStudioList() => LoadScreen.Close();
        public override void SyncStudioList(IList<ListChange> changes) => LoadScreen.Refresh();

        public override void Install(Harmony harmony)
        {
            SaveFolder.Install(harmony, _plugin, Root(0));
            // Hide Studio's Load screen the moment it's created, so it doesn't show for a few frames first.
            LoadScreen.ShouldHideOnOpen = () => _plugin.ReplaceStudioList.Value && !CameraSweepBridge.Picking();
            try
            {
                var awake = AccessTools.Method(typeof(Studio.SceneLoadScene), "Awake");
                if (awake != null) harmony.Patch(awake, postfix: new HarmonyMethod(typeof(SceneKind), nameof(LoadScreenAwakePostfix)));
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug("Load screen hook skipped: " + ex.Message);
            }

            // Browser Folders' "Scene folder" window draws while its IsVisible() says so.
            try
            {
                var t = AccessTools.TypeByName("BrowserFolders.SceneFolders");
                var isVisible = t != null ? AccessTools.Method(t, "IsVisible") : null;
                if (isVisible != null) harmony.Patch(isVisible, prefix: new HarmonyMethod(typeof(SceneKind), nameof(BrowserFoldersIsVisiblePrefix)));
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug("Browser Folders hook skipped: " + ex.Message);
            }
        }

        private static void LoadScreenAwakePostfix(Studio.SceneLoadScene __instance) => LoadScreen.Opened(__instance);

        private static bool BrowserFoldersIsVisiblePrefix(ref int __result)
        {
            if (!_hideBrowserFolders) return true;
            __result = 0;
            return false;
        }

        /// <summary>
        /// With Browser Folders' "Save scenes to current folder" on, Studio saves into its scene folder: keep that
        /// folder in step with the one open in the browser, so new saves land where you're browsing.
        /// </summary>
        public override void OnFolderChanged(string folder)
        {
            SaveFolder.FolderOpened(folder);
            if (!_plugin.SyncSaveFolder.Value || string.IsNullOrEmpty(folder)) return;
            try
            {
                var t = AccessTools.TypeByName("BrowserFolders.SceneFolders");
                var tree = t != null ? AccessTools.Field(t, "_folderTreeView")?.GetValue(null) : null;
                var prop = tree != null ? AccessTools.Property(tree.GetType(), "CurrentFolder") : null;
                if (prop == null) return;
                var current = prop.GetValue(tree, null) as string;
                if (current != null && Files.FileOps.Same(current, folder)) return;
                prop.SetValue(tree, folder, null);
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug("Couldn't set Browser Folders' scene folder: " + (ex.InnerException ?? ex).Message);
            }
        }

        public override void OnUpdate(bool replacing)
        {
            _hideBrowserFolders = replacing;
            if (!SceneFile.Ready && BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.bepis.bepinex.sideloader"))
                SceneFile.Prepare();
        }

        public override void Restore()
        {
            _hideBrowserFolders = false;
            LoadScreen.Restore();
        }
    }
}

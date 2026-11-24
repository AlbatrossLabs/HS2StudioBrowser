using BepInEx;
using BepInEx.Configuration;
using KKAPI;
using UnityEngine;

namespace HS2StudioBrowser.Scenes
{
    /// <summary>Studio Browser: Scenes. Browse, load, import, rename and organize scenes; stands in for Studio's Load screen.</summary>
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("StudioNEOV2")]
    [BepInDependency(KoikatuAPI.GUID)]
    [BepInDependency("com.bepis.bepinex.sideloader", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("marco.FolderBrowser", BepInDependency.DependencyFlags.SoftDependency)]
    public class SceneBrowserPlugin : StudioBrowserPlugin
    {
        public const string Guid = "com.albatrosslabs.hs2.studiobrowser.scenes";
        public const string PluginName = "Studio Browser: Scenes";
        public const string Version = "0.22.0";

        internal ConfigEntry<bool> CloseAfterLoading;
        internal ConfigEntry<bool> SyncSaveFolder;
        internal ConfigEntry<SaveTarget> SaveInto;
        internal ConfigEntry<bool> NameOnSave;

        protected override StudioListKind CreateKind() => new SceneKind(this);
        protected override Texture2D CreateIcon() => SceneIcon.Create();
        protected override KeyboardShortcut DefaultHotkey => new KeyboardShortcut(KeyCode.L, KeyCode.LeftControl, KeyCode.LeftShift);
        protected override string StudioListWhere => "System > Load";

        protected override void Awake()
        {
            base.Awake();
            CloseAfterLoading = Config.Bind("General", "Close after loading", true,
                "After Load, close the browser (and Studio's Load screen) so you see the scene. Off: stay open to load another. Import always stays open.");
            SyncSaveFolder = Config.Bind("General", "Save into the open folder", true,
                "With Browser Folders' 'Save scenes to current folder' on, keep its folder in step with the one open in this browser.");
            SaveInto = Config.Bind("Saving", "Save new scenes into", SaveTarget.LoadedScene,
                "Where System > Save puts a new scene. LoadedScene: the folder of the scene you loaded (a new scene: the folder open in this browser). OpenFolder: the folder open in this browser. Studio: Studio's own choice (studio\\scene, or Browser Folders' current folder).");
            NameOnSave = Config.Bind("Saving", "Name scenes when saving", true,
                "After System > Save, ask for a file name (Studio names saves by the date). Enter names it, Esc keeps the date name.");
        }

        protected override void OnGUI()
        {
            base.OnGUI();
            if (KKAPI.Studio.StudioAPI.StudioLoaded && !HideWindows) SaveFolder.Draw();
        }
    }
}

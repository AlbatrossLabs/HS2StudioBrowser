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
        public const string Version = "0.16.0";

        internal ConfigEntry<bool> CloseAfterLoading;
        internal ConfigEntry<bool> SyncSaveFolder;

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
                "With Browser Folders' 'Save scenes to current folder' on, keep its folder in step with the one open in this browser, so new saves land where you're browsing.");
        }
    }
}

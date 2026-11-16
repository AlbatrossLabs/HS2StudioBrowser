using System.ComponentModel;
using BepInEx;
using BepInEx.Configuration;
using KKAPI;
using UnityEngine;

namespace HS2StudioBrowser.Poses
{
    /// <summary>Studio Browser: Poses. Browse, load, save, rename and organize poses; stands in for Studio's Pose list.</summary>
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("StudioNEOV2")]
    [BepInDependency(KoikatuAPI.GUID)]
    [BepInDependency(PoseToolsGuid)]
    public class PoseBrowserPlugin : StudioBrowserPlugin
    {
        public const string Guid = "com.albatrosslabs.hs2.studiobrowser.poses";
        public const string PluginName = "Studio Browser: Poses";
        public const string Version = "0.17.2";
        public const string PoseToolsGuid = "com.deathweasel.bepinex.posefolders";

        internal ConfigEntry<bool> ShowDat;
        internal ConfigEntry<bool> SyncSaveFolder;
        internal ConfigEntry<float> NearTolerance;
        internal ConfigEntry<bool> ApplyOnStep;
        internal ConfigEntry<PictureSize> PictureSizeSetting;
        internal ConfigEntry<bool> SharperPictures;

        internal enum PictureSize
        {
            [Description("320 x 180 (Studio's size)")] Small,
            [Description("640 x 360")] Medium,
            [Description("960 x 540")] Large,
            [Description("1280 x 720")] Huge,
        }

        internal static void Dimensions(PictureSize s, out int w, out int h)
        {
            switch (s)
            {
                case PictureSize.Small: w = 320; h = 180; break;
                case PictureSize.Large: w = 960; h = 540; break;
                case PictureSize.Huge: w = 1280; h = 720; break;
                default: w = 640; h = 360; break;
            }
        }

        protected override StudioListKind CreateKind() => new PoseKind(this);
        protected override Texture2D CreateIcon() => PoseIcon.Create();
        protected override KeyboardShortcut DefaultHotkey => new KeyboardShortcut(KeyCode.P, KeyCode.LeftControl, KeyCode.LeftShift);
        protected override string StudioListWhere => "character > Kinematics > Pose";

        protected override void Awake()
        {
            base.Awake();
            ShowDat = Config.Bind("General", "Show dat poses", true,
                "Also show .dat poses (Studio's own format, which has no picture). The .dat switch in the browser's top bar changes this.");
            SyncSaveFolder = Config.Bind("General", "Save into the open folder", true,
                "Keep Pose Tools' folder in step with the one open in this browser, so Studio's Pose list and new saves use the folder you're browsing.");
            ApplyOnStep = Config.Bind("General", "Try poses with the arrow keys", true,
                "Moving through the poses with the arrow keys puts each one on the selected characters as you land on it. Backspace (or Undo) puts back the pose they had before you started. Off: the arrow keys only move the selection.");
            PictureSizeSetting = Config.Bind("Pictures", "Picture size", PictureSize.Medium,
                "Size of the picture Picture takes (16:9, as Studio's pose pictures). Bigger looks sharper in the browser at large sizes; the file grows with it (about 0.1 MB at 320 x 180, 0.4 MB at 640 x 360, 1.5 MB at 1280 x 720). Pose Tools reads any size, and the browser scales big pictures down in memory.");
            SharperPictures = Config.Bind("Pictures", "Smooth edges (render at 2x)", true,
                "Shoot at twice the size and scale down, which smooths jagged edges and thin lines (hair, fingers). Takes a moment longer. Off: one shot at the chosen size.");
            NearTolerance = Config.Bind("Duplicates", "Similar pose tolerance (degrees)", 1f,
                new ConfigDescription("Duplicates also lists poses on the same animation whose every bone is within this many degrees of another's (positions within a hundredth of it). 0: exact copies only.",
                    new AcceptableValueRange<float>(0f, 10f)));
        }
    }
}

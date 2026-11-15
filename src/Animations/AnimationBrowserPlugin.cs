using BepInEx;
using BepInEx.Configuration;
using KKAPI;
using UnityEngine;

namespace HS2StudioBrowser.Animations
{
    /// <summary>Studio Browser: Animations. Search, favorite and step through Studio's animations; stands in for the Anime panel's lists.</summary>
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("StudioNEOV2")]
    [BepInDependency(KoikatuAPI.GUID)]
    [BepInDependency("com.bepis.bepinex.sideloader", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(AnimePanel.PairingGuid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(AnimePanel.SearchGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class AnimationBrowserPlugin : StudioBrowserPlugin
    {
        public const string Guid = "com.albatrosslabs.hs2.studiobrowser.animations";
        public const string PluginName = "Studio Browser: Animations";
        public const string Version = "0.15.0";

        internal ConfigEntry<bool> MeasureLengths;
        internal ConfigEntry<bool> ShowControls;
        internal ConfigEntry<bool> ControlsOpen;

        protected override StudioListKind CreateKind() => new AnimationKind(this);

        protected override void Awake()
        {
            base.Awake();
            MeasureLengths = Config.Bind("General", "Measure animation lengths", true,
                "The first time the browser opens, measure every animation's length in the background (a few milliseconds a frame) and remember it, so the list shows lengths and marks 1-2 frame animations as poses. Animations you play are measured from the character either way.");
            ShowControls = Config.Bind("General", "Show animation controls", true,
                "Show Studio's animation box (speed, pattern, options, props, loop, restart, copy / paste) inside the browser, with a timeline to scrub to any frame, and hide Studio's own box while the browser stands in for the Anime panel. Off: Studio's box shows as usual.");
            ControlsOpen = Config.Bind("Window", "Controls open", true, new ConfigDescription("The controls strip is unfolded.", null, "Advanced"));
        }
        protected override Texture2D CreateIcon() => AnimationIcon.Create();
        protected override KeyboardShortcut DefaultHotkey => new KeyboardShortcut(KeyCode.M, KeyCode.LeftControl, KeyCode.LeftShift);
        protected override string StudioListWhere => "character > Anime";
    }
}

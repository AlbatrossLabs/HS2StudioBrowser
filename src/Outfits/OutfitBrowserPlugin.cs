using BepInEx;
using BepInEx.Configuration;
using KKAPI;
using UnityEngine;

namespace HS2StudioBrowser.Outfits
{
    /// <summary>Studio Browser: Outfits. Browse, load, rename and organize outfit cards; stands in for Studio's Costume list.</summary>
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("StudioNEOV2")]
    [BepInDependency(KoikatuAPI.GUID)]
    [BepInDependency("com.bepis.bepinex.sideloader", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("marco.FolderBrowser", BepInDependency.DependencyFlags.SoftDependency)]
    public class OutfitBrowserPlugin : StudioBrowserPlugin
    {
        public const string Guid = "com.albatrosslabs.hs2.studiobrowser.outfits";
        public const string PluginName = "Studio Browser: Outfits";
        public const string Version = "0.17.2";

        protected override StudioListKind CreateKind() => new OutfitKind();
        protected override Texture2D CreateIcon() => ToolbarIcon.Create();
        protected override KeyboardShortcut DefaultHotkey => new KeyboardShortcut(KeyCode.O, KeyCode.LeftControl, KeyCode.LeftShift);
        protected override string StudioListWhere => "Manipulate > character > Costume";

        // Carried over from the plugin's earlier name, Outfit Browser.
        protected override string LegacyConfigFile => "com.albatrosslabs.hs2.outfitbrowser.cfg";
        protected override string LegacyFavoritesFile => "HS2OutfitBrowser.favorites.txt";
    }
}

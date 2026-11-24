using BepInEx;
using BepInEx.Configuration;
using KKAPI;
using UnityEngine;

namespace HS2StudioBrowser.Items
{
    /// <summary>Studio Browser: Items. Search, favorite and tag Studio's items; stands in for the Add › Item list.</summary>
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("StudioNEOV2")]
    [BepInDependency(KoikatuAPI.GUID)]
    [BepInDependency("com.bepis.bepinex.sideloader", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("AccessoriesToStudioItems", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("HS2_QuickAccessBox", BepInDependency.DependencyFlags.SoftDependency)]
    public class ItemBrowserPlugin : StudioBrowserPlugin
    {
        public const string Guid = "com.albatrosslabs.hs2.studiobrowser.items";
        public const string PluginName = "Studio Browser: Items";
        public const string Version = "0.22.0";

        protected override StudioListKind CreateKind() => new ItemBrowserKind();
        protected override Texture2D CreateIcon() => ItemIcon.Create();
        protected override KeyboardShortcut DefaultHotkey => new KeyboardShortcut(KeyCode.I, KeyCode.LeftControl, KeyCode.LeftShift);
        protected override string StudioListWhere => "Add > Item";
    }
}

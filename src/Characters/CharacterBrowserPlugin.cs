using BepInEx;
using BepInEx.Configuration;
using KKAPI;
using UnityEngine;

namespace HS2StudioBrowser.Characters
{
    /// <summary>Studio Browser: Characters. Browse, add, swap, rename and organize character cards; stands in for Studio's character lists.</summary>
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("StudioNEOV2")]
    [BepInDependency(KoikatuAPI.GUID)]
    [BepInDependency("com.bepis.bepinex.sideloader", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("marco.FolderBrowser", BepInDependency.DependencyFlags.SoftDependency)]
    public class CharacterBrowserPlugin : StudioBrowserPlugin
    {
        public const string Guid = "com.albatrosslabs.hs2.studiobrowser.characters";
        public const string PluginName = "Studio Browser: Characters";
        public const string Version = "0.16.0";

        protected override StudioListKind CreateKind() => new CharacterKind();
        protected override Texture2D CreateIcon() => CharacterIcon.Create();
        protected override KeyboardShortcut DefaultHotkey => new KeyboardShortcut(KeyCode.N, KeyCode.LeftControl, KeyCode.LeftShift);
        protected override string StudioListWhere => "Add > Character > Female / Male";
    }
}

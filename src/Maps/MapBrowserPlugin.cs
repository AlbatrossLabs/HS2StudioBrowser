using System;
using System.Collections;
using BepInEx;
using BepInEx.Configuration;
using KKAPI;
using UnityEngine;

namespace HS2StudioBrowser.Maps
{
    /// <summary>Studio Browser: Maps. Pictures, search, favorites and tags for Studio's maps; stands in for the Add › Map list.</summary>
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("StudioNEOV2")]
    [BepInDependency(KoikatuAPI.GUID)]
    [BepInDependency("com.bepis.bepinex.sideloader", BepInDependency.DependencyFlags.SoftDependency)]
    public class MapBrowserPlugin : StudioBrowserPlugin
    {
        public const string Guid = "com.albatrosslabs.hs2.studiobrowser.maps";
        public const string PluginName = "Studio Browser: Maps";
        public const string Version = "0.22.0";

        internal ConfigEntry<bool> HideCastInPictures;

        protected override StudioListKind CreateKind() => new MapKind(this);
        protected override Texture2D CreateIcon() => MapIcon.Create();
        protected override KeyboardShortcut DefaultHotkey => new KeyboardShortcut(KeyCode.G, KeyCode.LeftControl, KeyCode.LeftShift);
        protected override string StudioListWhere => "Add > Map";

        /// <summary>
        /// Takes a map picture off the screen: next frame, Studio's menus, the browser windows, the move gizmos
        /// (and the characters, if set) are hidden; at the end of that frame the screen is read, and everything
        /// comes back. Calls <paramref name="done"/> with the PNG (null if it failed).
        /// </summary>
        internal void Shoot(Action<byte[]> done) => StartCoroutine(ShootRoutine(done));

        private IEnumerator ShootRoutine(Action<byte[]> done)
        {
            yield return null; // start clean at the next frame, before anything is drawn
            MapShots.Hidden hidden = null;
            byte[] png = null;
            try { hidden = MapShots.HideForShot(HideCastInPictures.Value); }
            catch (Exception ex) { MapShots.Note("hiding for the picture failed: " + ex.Message); }
            yield return new WaitForEndOfFrame();
            try { png = MapShots.ReadScreen(); }
            catch (Exception ex) { MapShots.Note("reading the screen failed: " + ex); }
            finally { hidden?.Restore(); StudioBrowserPlugin.HideWindows = false; }
            done(png);
        }

        protected override void Awake()
        {
            HideCastInPictures = Config.Bind("Pictures", "Leave characters out", true,
                "Hide the scene's characters for the one frame a map picture is taken, so it shows the map (and its items) alone.");
            base.Awake();
        }
    }
}

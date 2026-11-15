using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Studio;
using UnityEngine;

namespace HS2StudioBrowser.Scenes
{
    /// <summary>
    /// Studio's Load screen (System > Load): a separate scene laid over Studio with a page of 12 thumbnails.
    /// Finding it, hiding it (its canvases switched off, so it neither shows nor takes clicks, but stays loaded),
    /// closing it the way its own close button does, and re-reading its list after files change.
    /// </summary>
    internal static class LoadScreen
    {
        private static SceneLoadScene _screen;
        private static float _nextFind;
        private static readonly List<Canvas> Disabled = new List<Canvas>();
        private static SceneLoadScene _hiddenFor;

        /// <summary>Whether the browser will stand in for the Load screen (set by SceneKind).</summary>
        internal static Func<bool> ShouldHideOnOpen;

        /// <summary>
        /// The Load screen just woke up (Harmony postfix on its Awake). Hiding it here, before its first
        /// frame is drawn, keeps it from flashing up before the browser covers it.
        /// </summary>
        internal static void Opened(SceneLoadScene screen)
        {
            _screen = screen;
            try { if (ShouldHideOnOpen != null && ShouldHideOnOpen()) SetHidden(true); }
            catch (Exception ex) { Log.Source.LogDebug("Couldn't hide the Load screen early: " + ex.Message); }
        }

        public static SceneLoadScene Find()
        {
            if (_screen == null && Time.unscaledTime >= _nextFind)
            {
                _nextFind = Time.unscaledTime + 0.25f;
                _screen = UnityEngine.Object.FindObjectOfType<SceneLoadScene>();
            }
            return _screen;
        }

        public static bool Open
        {
            get
            {
                var s = Find();
                return s != null && s.isActiveAndEnabled;
            }
        }

        public static void SetHidden(bool hide)
        {
            var s = Find();
            if (hide && s != null)
            {
                if (_hiddenFor == s) return;
                Restore();
                foreach (var root in s.gameObject.scene.GetRootGameObjects())
                foreach (var c in root.GetComponentsInChildren<Canvas>(true))
                {
                    if (!c.enabled) continue;
                    c.enabled = false;
                    Disabled.Add(c);
                }
                _hiddenFor = s;
            }
            else if (!hide && _hiddenFor != null)
            {
                Restore();
            }
            if (s == null) { Disabled.Clear(); _hiddenFor = null; }
        }

        public static void Restore()
        {
            foreach (var c in Disabled) if (c != null) c.enabled = true;
            Disabled.Clear();
            _hiddenFor = null;
        }

        /// <summary>Closes the Load screen as its close button does.</summary>
        public static void Close()
        {
            var s = Find();
            if (s == null) return;
            Restore();
            try { AccessTools.Method(typeof(SceneLoadScene), "OnClickClose")?.Invoke(s, null); }
            catch (Exception ex) { Log.Source.LogDebug("Couldn't close the Load screen: " + (ex.InnerException ?? ex).Message); }
        }

        /// <summary>Re-reads the Load screen's list (after files were renamed, moved or recycled).</summary>
        public static void Refresh()
        {
            var s = Find();
            if (s == null) return;
            try
            {
                AccessTools.Method(typeof(SceneLoadScene), "OnClickCancel")?.Invoke(s, null);
                AccessTools.Method(typeof(SceneLoadScene), "InitInfo")?.Invoke(s, null);
                AccessTools.Method(typeof(SceneLoadScene), "SetPage")?.Invoke(s, new object[] { SceneLoadScene.page });
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug("Couldn't refresh the Load screen: " + (ex.InnerException ?? ex).Message);
            }
        }
    }
}

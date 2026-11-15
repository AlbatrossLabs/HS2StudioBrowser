using System;
using System.Collections.Generic;
using HarmonyLib;

namespace HS2StudioBrowser.Game
{
    /// <summary>
    /// Studio's keyboard shortcuts (Delete = delete the workspace selection, F2 = shortcut menu, Esc = exit
    /// dialog, F / A / number keys = camera...) are skipped while Studio thinks you're typing in one of its
    /// fields. This makes Studio think so while the pointer is over any browser window or one of its menus or
    /// boxes is open, so the browsers' own keys never reach Studio. Shared by every browser plugin: hooked once.
    /// </summary>
    public static class InputGuard
    {
        private static readonly HashSet<object> Active = new HashSet<object>();
        private static bool _installed;

        /// <summary>Each window says every frame whether it wants the keyboard.</summary>
        public static void Set(object owner, bool active)
        {
            if (active) Active.Add(owner);
            else Active.Remove(owner);
        }

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            var getter = AccessTools.PropertyGetter(typeof(Studio.Studio), nameof(Studio.Studio.isInputNow));
            if (getter == null)
            {
                Log.Source.LogWarning("Studio.isInputNow not found; Studio's shortcut keys will also fire while using the browsers.");
                return;
            }
            try
            {
                new Harmony("com.albatrosslabs.hs2.studiobrowser.core").Patch(getter, postfix: new HarmonyMethod(typeof(InputGuard), nameof(Postfix)));
            }
            catch (Exception ex)
            {
                Log.Source.LogWarning("Couldn't hook Studio.isInputNow: " + ex.Message);
            }
        }

        private static void Postfix(ref bool __result)
        {
            if (Active.Count > 0) __result = true;
        }
    }
}

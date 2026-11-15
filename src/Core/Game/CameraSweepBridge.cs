using System.Reflection;
using HarmonyLib;

namespace HS2StudioBrowser.Game
{
    /// <summary>Camera Sweep picks cards with Studio's own lists; the browsers step aside while it does.</summary>
    public static class CameraSweepBridge
    {
        private static PropertyInfo _active;
        private static bool _resolved;

        public static bool Picking()
        {
            if (!_resolved)
            {
                _resolved = true;
                var t = AccessTools.TypeByName("HS2CameraSweep.UI.PickMode");
                _active = t != null ? AccessTools.Property(t, "Active") : null;
            }
            if (_active == null) return false;
            try { return (bool)_active.GetValue(null, null); }
            catch { return false; }
        }
    }
}

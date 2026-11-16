using System.Collections.Generic;

namespace HS2StudioBrowser.Game
{
    /// <summary>
    /// Puts your names on Studio's own list entries (Info's load info, in memory only) and keeps the
    /// originals, so they can be put back and so a rebuilt catalog still knows the real name.
    /// </summary>
    public static class StudioNames
    {
        private static readonly Dictionary<string, string> _originals = new Dictionary<string, string>();

        /// <summary>The entry's real name: the one it had before it was renamed here, else <paramref name="current"/>.</summary>
        public static string Original(string key, string current) => key != null && _originals.TryGetValue(key, out var o) ? o : current;

        /// <summary>Sets <paramref name="info"/>'s name to <paramref name="name"/> (null: its original).</summary>
        public static void Apply(string key, Studio.Info.LoadCommonInfo info, string name)
        {
            if (info == null || key == null) return;
            if (!_originals.ContainsKey(key)) _originals[key] = info.name;
            info.name = name ?? _originals[key];
        }
    }
}

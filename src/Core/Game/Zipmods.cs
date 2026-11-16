using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace HS2StudioBrowser.Game
{
    /// <summary>
    /// The zipmod files Sideloader loaded, by GUID: where the file is and what's in it (from Sideloader's own
    /// list, read by reflection, so nothing is opened again). Used to tell whether a mod can be removed.
    /// </summary>
    public static class Zipmods
    {
        public sealed class Mod
        {
            public string Guid, Name, Author, File, RelativeFile;
            public long Size;
            public int CharaLists, MapLists;
            public List<string> StudioLists = new List<string>();

            /// <summary>True when the mod holds nothing but lists starting with one of these (e.g. "ItemList", "ItemGroup").</summary>
            public bool OnlyHas(IEnumerable<string> listPrefixes) =>
                CharaLists == 0 && MapLists == 0 && StudioLists.All(l => listPrefixes.Any(p => l.StartsWith(p, StringComparison.OrdinalIgnoreCase)));

            public string OtherContent(IEnumerable<string> listPrefixes)
            {
                var parts = new List<string>();
                if (CharaLists > 0) parts.Add("clothes / accessories / body parts");
                if (MapLists > 0) parts.Add("maps");
                var other = StudioLists.Where(l => !listPrefixes.Any(p => l.StartsWith(p, StringComparison.OrdinalIgnoreCase))).ToList();
                if (other.Any(l => l.StartsWith("Anime", StringComparison.OrdinalIgnoreCase) || l.StartsWith("HAnime", StringComparison.OrdinalIgnoreCase))) parts.Add("animations");
                if (other.Any(l => l.StartsWith("Item", StringComparison.OrdinalIgnoreCase))) parts.Add("studio items");
                if (other.Any(l => !l.StartsWith("Anime", StringComparison.OrdinalIgnoreCase) && !l.StartsWith("HAnime", StringComparison.OrdinalIgnoreCase) && !l.StartsWith("Item", StringComparison.OrdinalIgnoreCase))) parts.Add("other studio lists");
                return string.Join(", ", parts.ToArray());
            }
        }

        private static Dictionary<string, Mod> _byGuid;

        public static Mod Get(string guid)
        {
            if (guid == null) return null;
            if (_byGuid == null) Load();
            return _byGuid.TryGetValue(guid, out var m) ? m : null;
        }

        private static void Load()
        {
            _byGuid = new Dictionary<string, Mod>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var t = AccessTools.TypeByName("Sideloader.Sideloader");
                var dict = t != null ? AccessTools.Field(t, "Zipmods")?.GetValue(null) as IDictionary : null;
                if (dict == null) return;
                foreach (var zi in dict.Values)
                {
                    var zt = zi.GetType();
                    var manifest = AccessTools.Field(zt, "Manifest")?.GetValue(zi);
                    if (manifest == null) continue;
                    var mt = manifest.GetType();
                    var m = new Mod
                    {
                        Guid = AccessTools.Property(mt, "GUID")?.GetValue(manifest, null) as string,
                        Name = AccessTools.Property(mt, "Name")?.GetValue(manifest, null) as string,
                        Author = AccessTools.Property(mt, "Author")?.GetValue(manifest, null) as string,
                        File = AccessTools.Field(zt, "FileName")?.GetValue(zi) as string,
                        RelativeFile = AccessTools.Field(zt, "RelativeFileName")?.GetValue(zi) as string,
                        Size = (long)(AccessTools.Field(zt, "FileSize")?.GetValue(zi) ?? 0L),
                        CharaLists = (AccessTools.Field(zt, "CharaLists")?.GetValue(zi) as ICollection)?.Count ?? 0,
                        MapLists = (AccessTools.Field(zt, "MapLists")?.GetValue(zi) as ICollection)?.Count ?? 0,
                    };
                    if (AccessTools.Field(zt, "StudioLists")?.GetValue(zi) is IEnumerable lists)
                        foreach (var l in lists)
                            if (AccessTools.Property(l.GetType(), "FileNameWithoutExtension")?.GetValue(l, null) is string n) m.StudioLists.Add(n);
                    if (!string.IsNullOrEmpty(m.Guid) && !_byGuid.ContainsKey(m.Guid)) _byGuid[m.Guid] = m;
                }
            }
            catch (Exception ex)
            {
                Log.Source.LogWarning("Couldn't read Sideloader's zipmod list: " + ex.Message);
            }
        }

        /// <summary>
        /// Moves a zipmod out of the mods folder into &lt;game&gt;\mods_to_delete (same subfolders), so it stops
        /// loading after a restart and can still be moved back. Returns the new path; throws on failure.
        /// </summary>
        public static string MoveToDeleteFolder(Mod mod)
        {
            if (mod?.File == null || !System.IO.File.Exists(mod.File)) throw new FileNotFoundException("The zipmod file isn't where Sideloader loaded it from.");
            var rel = !string.IsNullOrEmpty(mod.RelativeFile) ? mod.RelativeFile.TrimStart('\\', '/') : Path.GetFileName(mod.File);
            if (rel.StartsWith("mods" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) rel = rel.Substring(5);
            var target = Path.Combine(Path.Combine(BepInEx.Paths.GameRootPath, "mods_to_delete"), rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            target = Files.FileOps.FreePath(Path.GetDirectoryName(target), Path.GetFileName(target));
            System.IO.File.Move(mod.File, target);
            return target;
        }
    }
}

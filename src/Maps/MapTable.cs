using System;
using System.Collections.Generic;
using System.Linq;
using HS2StudioBrowser.Game;

namespace HS2StudioBrowser.Maps
{
    /// <summary>
    /// Studio's map list (Info.dicMapLoadInfo) as a catalog. Studio has no groups for maps, so they're grouped by
    /// where they come from: the base game, then each mod author. Keys use the zipmod's GUID and its own map
    /// number (Sideloader renumbers mod maps at every start), so favorites, tags and pictures stay put.
    /// </summary>
    internal static class MapTable
    {
        internal sealed class Ids
        {
            public int No;
            public bool Mod;
            public string Guid, Name, Author;
            public int Slot;
        }

        public static List<ItemGroup> Build()
        {
            var baseGroup = new ItemGroup { Key = "base", Name = "Base game" };
            var baseCat = new ItemCategory { Key = "base", Name = "Base game", Group = baseGroup };
            baseGroup.Categories.Add(baseCat);
            var modGroup = new ItemGroup { Key = "mods", Name = "Mods" };
            var byAuthor = new Dictionary<string, ItemCategory>(StringComparer.OrdinalIgnoreCase);
            var sideloader = HarmonyLib.AccessTools.TypeByName("Sideloader.AutoResolver.UniversalAutoResolver") != null;

            var order = 0;
            foreach (var kv in Studio.Info.Instance.dicMapLoadInfo)
            {
                var no = kv.Key;
                var ids = new Ids { No = no };
                if (sideloader) Resolve(ids);
                string key, source;
                ItemCategory cat;
                if (ids.Mod)
                {
                    key = $"m:{ids.Guid}:{ids.Slot}";
                    var author = string.IsNullOrEmpty(ids.Author) ? "Unknown author" : ids.Author;
                    source = string.IsNullOrEmpty(ids.Name) ? ids.Guid : ids.Name + (string.IsNullOrEmpty(ids.Author) ? "" : " · " + ids.Author);
                    if (!byAuthor.TryGetValue(author, out cat))
                    {
                        cat = new ItemCategory { Key = "a:" + author, Name = author, Group = modGroup };
                        byAuthor[author] = cat;
                    }
                }
                else
                {
                    key = $"b:{no}";
                    source = "base game";
                    cat = baseCat;
                }
                cat.Items.Add(new CatalogItem
                {
                    Key = key,
                    Name = StudioNames.Original(key, kv.Value?.name) ?? $"Map {no}",
                    Source = source,
                    ModGuid = ids.Mod ? ids.Guid : null,
                    Category = cat,
                    Tag = ids,
                    Order = order++,
                });
            }

            foreach (var c in byAuthor.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)) modGroup.Categories.Add(c);
            var groups = new List<ItemGroup>();
            if (baseCat.Items.Count > 0) groups.Add(baseGroup);
            if (modGroup.Categories.Count > 0) groups.Add(modGroup);
            return groups;
        }

        /// <summary>The catalog key for a map number (as <see cref="Build"/> makes them), or null if there's no such map.</summary>
        public static string KeyFor(int no, out Ids ids)
        {
            ids = null;
            if (no < 0 || !Studio.Info.Instance.dicMapLoadInfo.ContainsKey(no)) return null;
            ids = new Ids { No = no };
            if (HarmonyLib.AccessTools.TypeByName("Sideloader.AutoResolver.UniversalAutoResolver") != null) Resolve(ids);
            return ids.Mod ? $"m:{ids.Guid}:{ids.Slot}" : $"b:{no}";
        }

        /// <summary>Which zipmod a map comes from: a Studio resolve entry with that local number is a mod map.</summary>
        private static void Resolve(Ids ids)
        {
            try
            {
                // Local numbers come from one counter for all of Sideloader's studio lists, but check the
                // zipmod actually has a map list, in case an item shares the number.
                var all = Sideloader.AutoResolver.UniversalAutoResolver.GetStudioResolveInfos(ids.No, false).ToList();
                var r = all.FirstOrDefault(x => Zipmods.Get(x.GUID)?.StudioLists.Any(l => l.StartsWith("Map", StringComparison.OrdinalIgnoreCase)) == true)
                    ?? all.FirstOrDefault();
                if (r == null) return;
                ids.Mod = true;
                ids.Guid = r.GUID;
                ids.Slot = r.Slot;
                ids.Name = r.Name;
                ids.Author = r.Author;
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug("Map resolve failed: " + ex.Message);
            }
        }
    }
}

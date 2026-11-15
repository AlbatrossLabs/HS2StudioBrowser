using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Studio;

namespace HS2StudioBrowser.Items
{
    /// <summary>Studio's item table (Info.dicItemLoadInfo: group › category › no) read into the browser's catalog.</summary>
    internal static class ItemTable
    {
        public sealed class Ids
        {
            public int Group, Category, No;
            public bool Mod;
            public int Colors;
            public string Guid;   // zipmod GUID (null for base game)
            public int Slot;      // the zipmod's own item number (-1 for base game)
            public string Name;
        }

        public static List<ItemGroup> Build()
        {
            var info = Info.Instance;
            var table = info.dicItemLoadInfo;
            var sideloader = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.bepis.bepinex.sideloader");
            var groups = new List<ItemGroup>();

            foreach (var gkv in info.dicItemGroupCategory.OrderBy(x => x.Value.sort))
            {
                if (!table.TryGetValue(gkv.Key, out var cats)) continue;
                var g = new ItemGroup { Key = "g" + gkv.Key, Name = string.IsNullOrEmpty(gkv.Value.name) ? $"Group {gkv.Key}" : gkv.Value.name };
                foreach (var ckv in gkv.Value.dicCategory.OrderBy(x => x.Value.sort))
                {
                    if (!cats.TryGetValue(ckv.Key, out var items) || items.Count == 0) continue;
                    var c = new ItemCategory { Key = $"g{gkv.Key}c{ckv.Key}", Name = string.IsNullOrEmpty(ckv.Value.name) ? $"Category {ckv.Key}" : ckv.Value.name, Group = g };
                    var order = 0;
                    foreach (var ikv in items)
                    {
                        var key = $"b:{gkv.Key}:{ckv.Key}:{ikv.Key}";
                        var source = "base game";
                        var isMod = false;
                        string guid = null;
                        var slot = -1;
                        if (sideloader)
                        {
                            var mod = ModOf(ikv.Key);
                            if (mod != null)
                            {
                                key = $"m:{mod.Value.Guid}:{mod.Value.Slot}:{gkv.Key}:{ckv.Key}";
                                source = mod.Value.Name;
                                isMod = true;
                                guid = mod.Value.Guid;
                                slot = mod.Value.Slot;
                            }
                        }
                        // Studio colors item names by how many colors they take; say it in words.
                        int colors;
                        try { colors = info.SafeGetItemColorData(gkv.Key, ckv.Key, ikv.Key)?.Count ?? 0; }
                        catch { colors = 0; }
                        if (colors > 0) source += $"  ·  {colors} color{(colors == 1 ? "" : "s")}";

                        c.Items.Add(new CatalogItem
                        {
                            Key = key,
                            Name = ikv.Value?.name ?? $"#{ikv.Key}",
                            Source = source,
                            Tag = new Ids { Group = gkv.Key, Category = ckv.Key, No = ikv.Key, Mod = isMod, Colors = colors, Guid = guid, Slot = slot, Name = ikv.Value?.name ?? "" },
                            Order = order++,
                        });
                    }
                    if (c.Items.Count > 0) g.Categories.Add(c);
                }
                if (g.Categories.Count > 0) groups.Add(g);
            }
            return groups;
        }

        internal struct ModInfo
        {
            public string Guid, Name;
            public int Slot;
        }

        /// <summary>
        /// The zipmod an item comes from. Sideloader renumbers mod items at every start (LocalSlot); the
        /// mod's GUID and its own number (Slot) stay the same, so favorites and tags are stored by those.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ModInfo? ModOf(int localSlot)
        {
            try
            {
                foreach (var r in Sideloader.AutoResolver.UniversalAutoResolver.GetStudioResolveInfos(localSlot, false))
                {
                    if (r == null || string.IsNullOrEmpty(r.GUID)) continue;
                    var name = string.IsNullOrEmpty(r.Name) ? r.GUID : r.Name;
                    if (!string.IsNullOrEmpty(r.Author)) name += " · " + r.Author;
                    return new ModInfo { Guid = r.GUID, Name = name, Slot = r.Slot };
                }
            }
            catch
            {
                // Sideloader missing or changed: treat as base game.
            }
            return null;
        }
    }
}

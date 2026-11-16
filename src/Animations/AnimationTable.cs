using HS2StudioBrowser.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Studio;
using UnityEngine;

namespace HS2StudioBrowser.Animations
{
    /// <summary>
    /// Studio's animation table (Info.dicAnimeLoadInfo: group › category › no) and its group / category names,
    /// read into the browser's catalog. Animation Search swaps the table for a filtered copy while you type in
    /// its box; the browser reads (and plays from) the full one it keeps.
    /// </summary>
    internal static class AnimationTable
    {
        private const string SearchType = "Studio_AnimationSearch.AnimationSearch";
        private static bool _resolved;
        private static Type _searchType;
        private static System.Reflection.FieldInfo _fBase, _fBar;

        private static Type Search()
        {
            if (_resolved) return _searchType;
            _resolved = true;
            try
            {
                _searchType = AccessTools.TypeByName(SearchType);
                if (_searchType != null)
                {
                    _fBase = AccessTools.Field(_searchType, "animeListsBase");
                    _fBar = AccessTools.Field(_searchType, "searchBar");
                }
            }
            catch { _searchType = null; }
            return _searchType;
        }

        /// <summary>The full table: Animation Search's saved copy if it's installed, else Studio's.</summary>
        public static Dictionary<int, Dictionary<int, Dictionary<int, Info.AnimeLoadInfo>>> Full()
        {
            var studio = Info.Instance.dicAnimeLoadInfo;
            try
            {
                var t = Search();
                if (t == null || _fBase == null) return studio;
                var component = UnityEngine.Object.FindObjectOfType(t);
                var saved = component != null ? _fBase.GetValue(component) as Dictionary<int, Dictionary<int, Dictionary<int, Info.AnimeLoadInfo>>> : null;
                return saved ?? studio;
            }
            catch
            {
                return studio;
            }
        }

        /// <summary>Animation Search's box (so it can be hidden with Studio's lists), or null.</summary>
        public static GameObject SearchBar()
        {
            try
            {
                return Search() != null && _fBar != null ? _fBar.GetValue(null) as GameObject : null;
            }
            catch { return null; }
        }

        /// <summary>While in use, Studio's table is the full one (an animation filtered out by Animation Search still loads).</summary>
        public static IDisposable Unfiltered() => new Swap();

        private sealed class Swap : IDisposable
        {
            private readonly Dictionary<int, Dictionary<int, Dictionary<int, Info.AnimeLoadInfo>>> _was;

            public Swap()
            {
                var full = Full();
                if (!ReferenceEquals(full, Info.Instance.dicAnimeLoadInfo))
                {
                    _was = Info.Instance.dicAnimeLoadInfo;
                    Info.Instance.dicAnimeLoadInfo = full;
                }
            }

            public void Dispose()
            {
                if (_was != null) Info.Instance.dicAnimeLoadInfo = _was;
            }
        }

        // ------------------------------------------------------------------ catalog

        public sealed class Ids
        {
            public int Group, Category, No;
            public bool Mod;
        }

        public static List<ItemGroup> Build()
        {
            var table = Full();
            var info = Info.Instance;
            var sideloader = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.bepis.bepinex.sideloader");
            var groups = new List<ItemGroup>();

            // Groups and categories in Studio's order (their sort numbers), as its lists show them.
            foreach (var gkv in info.dicAGroupCategory.OrderBy(x => x.Value.sort))
            {
                if (!table.TryGetValue(gkv.Key, out var cats)) continue;
                var g = new ItemGroup { Key = "g" + gkv.Key, Name = string.IsNullOrEmpty(gkv.Value.name) ? $"Group {gkv.Key}" : gkv.Value.name };
                foreach (var ckv in gkv.Value.dicCategory.OrderBy(x => x.Value.sort))
                {
                    if (!cats.TryGetValue(ckv.Key, out var anims) || anims.Count == 0) continue;
                    var c = new ItemCategory { Key = $"g{gkv.Key}c{ckv.Key}", Name = string.IsNullOrEmpty(ckv.Value.name) ? $"Category {ckv.Key}" : ckv.Value.name, Group = g };
                    var order = 0;
                    foreach (var akv in anims)
                    {
                        var key = $"b:{gkv.Key}:{ckv.Key}:{akv.Key}";
                        var source = "base game";
                        var isMod = false;
                        string guid = null;
                        if (sideloader)
                        {
                            var mod = ModOf(akv.Key, gkv.Key, ckv.Key);
                            if (mod != null)
                            {
                                key = $"m:{mod.Value.Guid}:{mod.Value.Slot}:{gkv.Key}:{ckv.Key}";
                                source = mod.Value.Name;
                                isMod = true;
                                guid = mod.Value.Guid;
                            }
                        }
                        c.Items.Add(new CatalogItem
                        {
                            Key = key,
                            Name = StudioNames.Original(key, akv.Value?.name) ?? $"#{akv.Key}",
                            Source = source,
                            ModGuid = guid,
                            Tag = new Ids { Group = gkv.Key, Category = ckv.Key, No = akv.Key, Mod = isMod },
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
        /// The zipmod an animation comes from. Sideloader gives each mod animation a new number (its
        /// LocalSlot) at startup; the mod's own number (Slot) plus its GUID is what stays the same between
        /// sessions, so favorites are stored by that.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ModInfo? ModOf(int localSlot, int group, int category)
        {
            try
            {
                foreach (var r in Sideloader.AutoResolver.UniversalAutoResolver.GetStudioResolveInfos(localSlot, false))
                {
                    if (r == null || r.Group != group || r.Category != category) continue;
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

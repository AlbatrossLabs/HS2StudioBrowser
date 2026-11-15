using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2StudioBrowser.Files;
using HS2StudioBrowser.Game;
using Sideloader.AutoResolver;

namespace HS2StudioBrowser.Scenes
{
    /// <summary>
    /// Studio scene files as the browser needs them: name check, plugin data and the missing-zipmod check (studio
    /// items, map, embedded characters). Scenes store no name of their own, so the browser shows and renames the
    /// file name. Byte-level parsing is in <see cref="SceneBytes"/>.
    /// </summary>
    internal static class SceneFile
    {
        public static string ReadName(string path) => SceneBytes.ReadName(path);

        public static byte[] ReadPluginData(string path) => SceneBytes.PluginDataIn(File.ReadAllBytes(path));

        // ---------------------------------------------------------------- missing zipmods

        private static HashSet<string> _studioGuids;

        /// <summary>
        /// Call on the main thread before checking: brings Sideloader's studio-item lookups up to date (it builds
        /// them lazily) and notes which zipmods have studio items, so the worker thread only reads.
        /// </summary>
        public static void Prepare()
        {
            if (_studioGuids != null) return;
            try
            {
                UniversalAutoResolver.GetStudioResolveInfos("", 0, true).Any();
                _studioGuids = new HashSet<string>(UniversalAutoResolver.LoadedStudioResolutionInfos.Select(x => x.GUID).Where(g => g != null), StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug("Studio item lookups not ready: " + ex.Message);
            }
        }

        public static bool Ready => _studioGuids != null;

        /// <summary>
        /// What the scene needs that you don't have: studio items and lights, the map, and every embedded
        /// character's clothes and accessories (each character carries its own Sideloader list).
        /// </summary>
        public static List<MissingItem> FindMissing(string path)
        {
            if (_studioGuids == null) return null;
            var b = File.ReadAllBytes(path);
            var list = new List<MissingItem>();

            var dict = ModCheck.ReadDictionary(SceneBytes.PluginDataIn(b));
            var uar = ModCheck.SideloaderData(dict);
            if (uar?.data != null)
            {
                if (uar.data.TryGetValue("itemInfo", out var itemsObj) && itemsObj is object[] items)
                {
                    var seen = new HashSet<string>();
                    foreach (var o in items)
                    {
                        if (!(o is byte[] bytes)) continue;
                        StudioResolveInfo ri;
                        try { ri = MessagePack.MessagePackSerializer.Deserialize<StudioResolveInfo>(bytes); }
                        catch { continue; }
                        if (ri == null || string.IsNullOrEmpty(ri.GUID)) continue;

                        // Entries Sideloader never remaps (bone lists and the like) only need the zipmod present.
                        var ok = ri.ResolveItem
                            ? UniversalAutoResolver.GetStudioResolveInfos(ri.GUID, ri.Slot, true).Any()
                            : _studioGuids.Contains(ri.GUID);
                        if (ok || !seen.Add(ri.GUID + "|" + ri.Slot)) continue;
                        list.Add(new MissingItem
                        {
                            Guid = ri.GUID, Name = ri.Name, Author = ri.Author, Website = ri.Website, Slot = ri.Slot,
                            ModInstalled = Sideloader.Sideloader.GetManifest(ri.GUID) != null,
                            Part = "studio item",
                        });
                    }
                }

                if (uar.data.TryGetValue("mapInfoGUID", out var mapObj) && mapObj is string mapGuid && mapGuid.Length > 0 && !_studioGuids.Contains(mapGuid))
                {
                    uar.data.TryGetValue("mapInfoName", out var mapName);
                    uar.data.TryGetValue("mapInfoAuthor", out var mapAuthor);
                    uar.data.TryGetValue("mapInfoWebsite", out var mapSite);
                    list.Add(new MissingItem
                    {
                        Guid = mapGuid, Name = mapName as string, Author = mapAuthor as string, Website = mapSite as string,
                        ModInstalled = Sideloader.Sideloader.GetManifest(mapGuid) != null,
                        Part = "map",
                    });
                }
            }

            // Characters are stored in the scene exactly like character cards, minus the picture.
            foreach (var cha in SceneBytes.EmbeddedCharacters(b))
                ModCheck.CheckPluginData(ModCheck.ReadDictionary(cha.Value), list, (string.IsNullOrEmpty(cha.Key) ? "character" : cha.Key) + ": ");
            return list;
        }

    }
}

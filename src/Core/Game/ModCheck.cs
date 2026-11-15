using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using AIChara;
using ExtensibleSaveFormat;
using MessagePack;
using Sideloader.AutoResolver;

namespace HS2StudioBrowser.Game
{
    /// <summary>One item in a card that needs a zipmod you don't have (or have in a version without that item).</summary>
    public sealed class MissingItem
    {
        public string Guid;
        public string Name;
        public string Author;
        public string Website;
        public string Property;   // e.g. ChaFileClothes.ClothesTop, ChaFileAccessory.PartsInfo.Acc0...
        public int Slot;
        public bool ModInstalled; // zipmod present, but this item isn't in it (outdated)
        public string Part;       // shown as-is instead of a name made from Property (e.g. "studio item", "Aiko: top")

        public string Label => string.IsNullOrEmpty(Name) ? Guid : Name;
    }

    /// <summary>
    /// Finds outfits that use zipmods you don't have, without loading them. Sideloader stores, in each card, the
    /// zipmod GUID and item slot for every mod item it uses. That list is read straight from the file and each
    /// entry checked against the zipmods Sideloader has loaded, the same test Sideloader runs when an outfit
    /// loads (including its exception for IDs that match a base-game item). Runs on a worker thread; results
    /// are cached by path, date and size for the session (not saved, since your mods change).
    /// </summary>
    public static class ModCheck
    {
        private const string UarId = "com.bepis.sideloader.universalautoresolver";
        private const string UarIdOld = "EC.Core.Sideloader.UniversalAutoResolver";

        /// <summary>The game's item lists. Set on the main thread (the game's Singleton getter isn't thread-safe).</summary>
        public static ChaListControl ListCtrl;

        /// <summary>Missing items in a card (empty if none), or null if it couldn't be read.</summary>
        public static List<MissingItem> Check(string path, Func<string, byte[]> readPluginData)
        {
            byte[] ext;
            try { ext = readPluginData(path); }
            catch { return null; }
            var list = new List<MissingItem>();
            if (ext == null) return list;

            var dict = ReadDictionary(ext);
            if (dict == null) return null;
            CheckPluginData(dict, list, null);
            return list;
        }

        /// <summary>Adds the missing items listed in a card's plugin data (Sideloader's "info" list) to <paramref name="into"/>.</summary>
        public static void CheckPluginData(Dictionary<string, PluginData> dict, List<MissingItem> into, string partPrefix)
        {
            if (dict == null) return;
            PluginData pd;
            if (!dict.TryGetValue(UarIdOld, out pd) || pd == null) dict.TryGetValue(UarId, out pd);
            if (pd?.data == null || !pd.data.TryGetValue("info", out var infoObj) || !(infoObj is object[] info)) return;

            var seen = new HashSet<string>(into.Select(x => x.Guid + "|" + x.Property + "|" + x.Part));
            foreach (var o in info)
            {
                if (!(o is byte[] bytes)) continue;
                ResolveInfo ri;
                try { ri = MessagePackSerializer.Deserialize<ResolveInfo>(bytes); }
                catch { continue; }
                if (ri == null || string.IsNullOrEmpty(ri.GUID) || ri.GUID.Trim().Length == 0) continue;

                if (UniversalAutoResolver.TryGetResolutionInfo(ri.Slot, ri.CategoryNo, ri.GUID) != null) continue;
                if (MatchesBaseGameItem(ri)) continue;

                var part = partPrefix != null ? partPrefix + PartName(ri.Property) : null;
                if (!seen.Add(ri.GUID + "|" + ri.Property + "|" + part)) continue;
                into.Add(new MissingItem
                {
                    Guid = ri.GUID,
                    Name = ri.Name,
                    Author = ri.Author,
                    Website = ri.Website,
                    Property = ri.Property,
                    Slot = ri.Slot,
                    ModInstalled = Sideloader.Sideloader.GetManifest(ri.GUID) != null,
                    Part = part,
                });
            }
        }

        /// <summary>Deserializes an ExtendedSave plugin-data block.</summary>
        public static Dictionary<string, PluginData> ReadDictionary(byte[] ext)
        {
            if (ext == null) return null;
            try { return MessagePackSerializer.Deserialize<Dictionary<string, PluginData>>(ext); }
            catch { return null; }
        }

        /// <summary>Sideloader's plugin data inside an ExtendedSave dictionary, or null.</summary>
        public static PluginData SideloaderData(Dictionary<string, PluginData> dict)
        {
            if (dict == null) return null;
            if (dict.TryGetValue(UarIdOld, out var pd) && pd != null) return pd;
            return dict.TryGetValue(UarId, out pd) ? pd : null;
        }

        /// <summary>"ChaFileClothes.ClothesTopPattern0" → "top pattern 0", "accessory3.ChaFileAccessory..." → "accessory".</summary>
        public static string PartName(string property)
        {
            if (string.IsNullOrEmpty(property)) return "item";
            if (property.IndexOf("Accessory", StringComparison.OrdinalIgnoreCase) >= 0) return "accessory";
            var dot = property.LastIndexOf('.');
            var last = dot >= 0 ? property.Substring(dot + 1) : property;
            foreach (var cut in new[] { "Clothes", "ChaFile" })
                if (last.StartsWith(cut, StringComparison.Ordinal) && last.Length > cut.Length) last = last.Substring(cut.Length);
            var sb = new StringBuilder();
            for (var i = 0; i < last.Length; i++)
            {
                var c = last[i];
                if (i > 0 && (char.IsUpper(c) || (char.IsDigit(c) && !char.IsDigit(last[i - 1])))) sb.Append(' ');
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Sideloader doesn't count an item as missing when its saved ID belongs to a base-game item (it logs
        /// "Missing mod detected but matching ID found" and leaves it). Same rule here.
        /// </summary>
        public static bool MatchesBaseGameItem(ResolveInfo ri)
        {
            try
            {
                var item = ListCtrl?.GetListInfo(ri.CategoryNo, ri.Slot);
                if (item == null) return false;
                if (!item.dictInfo.TryGetValue(15, out var bundle) || bundle == null) return true;
                bundle = bundle.Replace("chara/", "").Replace(".unity3d", "").Replace(ri.CategoryNo + "_", "").Replace("/", "");
                return !int.TryParse(bundle, out _);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Checks cards for missing zipmods in the background and keeps the results for the session.</summary>
    public sealed class MissingModChecker : IDisposable
    {
        private struct Cached
        {
            public DateTime Modified;
            public long Size;
            public List<MissingItem> Missing; // null = couldn't read
        }

        private readonly Dictionary<string, Cached> _cache = new Dictionary<string, Cached>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();
        private readonly List<string> _queue = new List<string>();
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private Thread _thread;
        private volatile bool _stop;
        private int _version;
        private readonly Func<string, List<MissingItem>> _find;

        public MissingModChecker(Func<string, List<MissingItem>> findMissing)
        {
            _find = findMissing;
        }

        /// <summary>Bumps whenever a result arrives.</summary>
        public int Version => _version;

        public int Pending
        {
            get { lock (_lock) return _queue.Count; }
        }

        /// <summary>The missing items for a card, or null while it hasn't been checked yet.</summary>
        public List<MissingItem> Get(string path, DateTime modified)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(path, out var c) && c.Modified == modified) return c.Missing ?? Empty;
            }
            return null;
        }

        public bool HasMissing(string path, DateTime modified)
        {
            var m = Get(path, modified);
            return m != null && m.Count > 0;
        }

        private static readonly List<MissingItem> Empty = new List<MissingItem>();

        /// <summary>Queues every card that isn't checked yet (the ones on screen first).</summary>
        public void Request(IEnumerable<KeyValuePair<string, DateTime>> cards)
        {
            var added = false;
            lock (_lock)
            {
                _queue.Clear();
                foreach (var kv in cards)
                {
                    if (_cache.TryGetValue(kv.Key, out var c) && c.Modified == kv.Value) continue;
                    _queue.Add(kv.Key);
                    added = true;
                }
            }
            if (!added) return;
            if (_thread == null || !_thread.IsAlive)
            {
                _stop = false;
                _thread = new Thread(Work) { IsBackground = true, Name = "OutfitBrowser mod check" };
                _thread.Start();
            }
            _signal.Set();
        }

        public void Forget(string path)
        {
            lock (_lock) _cache.Remove(path);
        }

        public void Dispose()
        {
            _stop = true;
            _signal.Set();
        }

        private void Work()
        {
            while (!_stop)
            {
                string path = null;
                lock (_lock)
                {
                    if (_queue.Count > 0)
                    {
                        path = _queue[0];
                        _queue.RemoveAt(0);
                    }
                }
                if (path == null)
                {
                    _signal.WaitOne(1000);
                    continue;
                }

                try
                {
                    var info = new FileInfo(path);
                    if (!info.Exists) continue;
                    var missing = _find(path);
                    lock (_lock) _cache[path] = new Cached { Modified = info.LastWriteTime, Size = info.Length, Missing = missing };
                    Interlocked.Increment(ref _version);
                }
                catch (Exception ex)
                {
                    Log.Source.LogDebug($"Mod check failed for {Path.GetFileName(path)}: {ex.Message}");
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HS2StudioBrowser.Files
{
    /// <summary>One group of tags in a tag list: "Color" with Black, White, ... ("one": only one value at a time).</summary>
    public sealed class TagFacet
    {
        public string Name;
        public bool Single;
        public readonly List<string> Values = new List<string>();

        public string Tag(string value) => Name + ":" + value;
    }

    /// <summary>
    /// The tags offered in the radial picker, from BepInEx\config\HS2StudioBrowser.&lt;kind&gt;.tagset.txt: one
    /// group per line, "Name: value, value, ...", with "(one)" after the name when only one value can apply.
    /// Written with the kind's defaults the first time; re-read when the file changes.
    /// </summary>
    public sealed class TagSet
    {
        private readonly string _file;
        private readonly string _defaults;
        private readonly TagSet _shared;
        private DateTime _stamp;
        private List<TagFacet> _own = new List<TagFacet>();
        private int _sharedVersion = -1;

        /// <summary>This browser's groups, then the shared ones (Project, Status, Use) it doesn't define itself.</summary>
        public List<TagFacet> Facets { get; private set; } = new List<TagFacet>();
        public int Version { get; private set; }
        public string FilePath => _file;
        public string SharedPath => _shared?.FilePath;

        /// <param name="shared">Groups shown in every browser (HS2StudioBrowser.shared.tagset.txt), or null.</param>
        public TagSet(string file, string defaults, string[] olderDefaults = null, TagSet shared = null)
        {
            _shared = shared;
            _file = file;
            _defaults = defaults ?? "";
            try
            {
                if (!File.Exists(file) && _defaults.Length > 0) File.WriteAllText(file, _defaults, new UTF8Encoding(false));
                else if (File.Exists(file) && olderDefaults != null && _defaults.Length > 0)
                {
                    // Still the starting list of an earlier version, never edited: bring it up to date.
                    var text = File.ReadAllText(file, Encoding.UTF8).Replace("\r\n", "\n");
                    if (olderDefaults.Any(o => o.Replace("\r\n", "\n") == text)) File.WriteAllText(file, _defaults, new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                Log.Source.LogWarning($"Couldn't write {Path.GetFileName(file)}: {ex.Message}");
            }
            Refresh();
        }

        /// <summary>Re-reads the file if it changed since last time.</summary>
        public void Refresh()
        {
            _shared?.Refresh();
            var stamp = DateTime.MinValue;
            try { stamp = File.Exists(_file) ? File.GetLastWriteTimeUtc(_file) : DateTime.MinValue; }
            catch { /* keep the old */ }
            if (stamp == _stamp && Version > 0 && (_shared == null || _shared.Version == _sharedVersion)) return;
            if (stamp != _stamp || Version == 0)
            {
                _stamp = stamp;
                try { _own = Parse(File.Exists(_file) ? File.ReadAllText(_file, Encoding.UTF8) : _defaults); }
                catch (Exception ex)
                {
                    Log.Source.LogWarning($"Couldn't read {Path.GetFileName(_file)}: {ex.Message}");
                    if (_own.Count == 0) _own = Parse(_defaults);
                }
            }
            var all = new List<TagFacet>(_own);
            if (_shared != null)
            {
                _sharedVersion = _shared.Version;
                foreach (var f in _shared.Facets)
                    if (!all.Any(x => string.Equals(x.Name, f.Name, StringComparison.OrdinalIgnoreCase))) all.Add(f);
            }
            Facets = all;
            Version++;
        }

        public TagFacet FacetOf(string tag)
        {
            var i = tag.IndexOf(':');
            if (i <= 0) return null;
            var name = tag.Substring(0, i);
            return Facets.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public static List<TagFacet> Parse(string text)
        {
            var list = new List<TagFacet>();
            foreach (var raw in (text ?? "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var name = line.Substring(0, colon).Trim();
                var single = false;
                var mark = name.IndexOf("(one)", StringComparison.OrdinalIgnoreCase);
                if (mark >= 0)
                {
                    single = true;
                    name = name.Remove(mark, 5).Trim();
                }
                name = name.Replace(":", "").Replace("|", "").Trim();
                if (name.Length == 0) continue;
                var f = new TagFacet { Name = name, Single = single };
                foreach (var v in line.Substring(colon + 1).Split(','))
                {
                    var value = v.Trim().Replace("|", "").Replace(":", "");
                    if (value.Length > 0 && !f.Values.Contains(value, StringComparer.OrdinalIgnoreCase)) f.Values.Add(value);
                }
                if (f.Values.Count > 0) list.Add(f);
            }
            return list;
        }
    }

    /// <summary>
    /// Tags on cards (by path relative to the kind's folder, like favorites) or catalog items (by key), in
    /// BepInEx\config\HS2StudioBrowser.&lt;kind&gt;.tags.txt: "key&lt;tab&gt;Color:Black|Theme:Swimwear" per line.
    /// Renames, moves and Recycle Bin done in the browser keep them in step.
    /// </summary>
    public sealed class TagStore
    {
        private readonly string _root;   // null: keys are used as given
        private readonly string _file;
        private readonly Dictionary<string, HashSet<string>> _tags = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, int> _counts;
        private static readonly HashSet<string> None = new HashSet<string>();

        public int Version { get; private set; }

        public TagStore(string root, string file)
        {
            _root = root != null ? FileOps.Normalize(root) : null;
            _file = file;
            Load();
        }

        public HashSet<string> Get(string pathOrKey)
        {
            var k = Key(pathOrKey);
            return k != null && _tags.TryGetValue(k, out var set) ? set : None;
        }

        public bool Has(string pathOrKey, string tag) => Get(pathOrKey).Contains(tag);

        /// <summary>How many cards or items carry each tag (anywhere, not just on screen).</summary>
        public int Count(string tag)
        {
            if (_counts == null)
            {
                _counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var set in _tags.Values)
                foreach (var t in set)
                    _counts[t] = _counts.TryGetValue(t, out var n) ? n + 1 : 1;
            }
            return _counts.TryGetValue(tag, out var c) ? c : 0;
        }

        /// <summary>Adds or removes <paramref name="tag"/> on each; a one-value group drops its other values when one is added.</summary>
        public void Set(IEnumerable<string> pathsOrKeys, string tag, bool on, TagFacet facet)
        {
            var changed = false;
            foreach (var p in pathsOrKeys)
            {
                var k = Key(p);
                if (k == null) continue;
                if (!_tags.TryGetValue(k, out var set))
                {
                    if (!on) continue;
                    set = _tags[k] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                if (on)
                {
                    if (facet != null && facet.Single)
                        changed |= set.RemoveWhere(t => t != tag && t.StartsWith(facet.Name + ":", StringComparison.OrdinalIgnoreCase)) > 0;
                    changed |= set.Add(tag);
                }
                else changed |= set.Remove(tag);
                if (set.Count == 0) _tags.Remove(k);
            }
            if (changed) Save();
        }

        /// <summary>Removes every value of a group from each.</summary>
        public void ClearFacet(IEnumerable<string> pathsOrKeys, TagFacet facet)
        {
            var changed = false;
            foreach (var p in pathsOrKeys)
            {
                var k = Key(p);
                if (k == null || !_tags.TryGetValue(k, out var set)) continue;
                changed |= set.RemoveWhere(t => t.StartsWith(facet.Name + ":", StringComparison.OrdinalIgnoreCase)) > 0;
                if (set.Count == 0) _tags.Remove(k);
            }
            if (changed) Save();
        }

        public void Moved(IEnumerable<KeyValuePair<string, string>> oldToNew)
        {
            var changed = false;
            foreach (var kv in oldToNew)
            {
                var from = Key(kv.Key);
                var to = Key(kv.Value);
                if (from == null || to == null || string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) continue;
                if (!_tags.TryGetValue(from, out var set)) continue;
                _tags.Remove(from);
                _tags[to] = set;
                changed = true;
            }
            if (changed) Save();
        }

        public void FolderMoved(string oldFolder, string newFolder)
        {
            var from = Key(oldFolder);
            var to = Key(newFolder);
            if (from == null || to == null) return;
            var prefix = from.TrimEnd('/') + "/";
            var hits = _tags.Keys.Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var h in hits)
            {
                var set = _tags[h];
                _tags.Remove(h);
                _tags[to.TrimEnd('/') + "/" + h.Substring(prefix.Length)] = set;
            }
            if (hits.Count > 0) Save();
        }

        public void Removed(IEnumerable<string> paths)
        {
            var changed = false;
            foreach (var p in paths)
            {
                var k = Key(p);
                if (k == null) continue;
                var prefix = k.TrimEnd('/') + "/";
                changed |= _tags.Remove(k);
                foreach (var h in _tags.Keys.Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    _tags.Remove(h);
                    changed = true;
                }
            }
            if (changed) Save();
        }

        private string Key(string pathOrKey)
        {
            if (string.IsNullOrEmpty(pathOrKey)) return null;
            if (_root == null) return pathOrKey;
            string full;
            try { full = FileOps.Normalize(pathOrKey); }
            catch { return null; }
            if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
            return full.Substring(_root.Length + 1).Replace('\\', '/');
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_file)) return;
                foreach (var line in File.ReadAllLines(_file, Encoding.UTF8))
                {
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    var tab = line.IndexOf('\t');
                    if (tab <= 0) continue;
                    var key = line.Substring(0, tab).Trim();
                    var set = new HashSet<string>(line.Substring(tab + 1).Split('|').Select(t => t.Trim()).Where(t => t.IndexOf(':') > 0), StringComparer.OrdinalIgnoreCase);
                    if (set.Count > 0) _tags[key] = set;
                }
            }
            catch (Exception ex)
            {
                Log.Source.LogWarning($"Couldn't read {Path.GetFileName(_file)}: {ex.Message}");
            }
            Version++;
        }

        private void Save()
        {
            Version++;
            _counts = null;
            try
            {
                var lines = new List<string> { "# Studio Browser tags: <card or item><tab>Group:Value|Group:Value" };
                lines.AddRange(_tags.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => kv.Key + "\t" + string.Join("|", kv.Value.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToArray())));
                var tmp = _file + ".tmp";
                File.WriteAllLines(tmp, lines.ToArray(), new UTF8Encoding(false));
                if (File.Exists(_file))
                {
                    try { File.Replace(tmp, _file, null); return; }
                    catch { File.Delete(_file); }
                }
                File.Move(tmp, _file);
            }
            catch (Exception ex)
            {
                Log.Source.LogWarning($"Couldn't save {Path.GetFileName(_file)}: {ex.Message}");
            }
        }
    }

    /// <summary>Tag filter: a card matches when, for every group in the filter, it has one of the picked values.</summary>
    public static class TagFilter
    {
        public static bool Matches(HashSet<string> tags, ICollection<string> filter)
        {
            if (filter == null || filter.Count == 0) return true;
            foreach (var group in filter.GroupBy(t => t.Substring(0, Math.Max(0, t.IndexOf(':'))), StringComparer.OrdinalIgnoreCase))
                if (!group.Any(tags.Contains)) return false;
            return true;
        }

        /// <summary>"Color: Black / Red · Theme: Swimwear".</summary>
        public static string Describe(ICollection<string> filter) =>
            string.Join("  ·  ", filter.GroupBy(t => t.Substring(0, Math.Max(0, t.IndexOf(':'))), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Key + ": " + string.Join(" / ", g.Select(t => t.Substring(t.IndexOf(':') + 1)).ToArray())).ToArray());

        public static string ValueOf(string tag) => tag.Substring(tag.IndexOf(':') + 1);
    }
}

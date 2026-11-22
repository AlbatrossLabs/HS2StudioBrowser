using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HS2StudioBrowser.Files
{
    /// <summary>
    /// Favorite outfits, saved as one path per line (relative to UserData\coordinate, e.g.
    /// "female/Casual/HS2CoordeF_2022.png") in BepInEx\config\HS2StudioBrowser.favorites.txt. Renames, moves
    /// and Recycle Bin done in the browser update the list; a card moved outside the browser loses its star.
    /// </summary>
    public sealed class Favorites
    {
        private readonly string _root;   // UserData\<kind folder>
        private readonly string _rootLabel;
        private readonly string _file;
        private readonly HashSet<string> _set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public int Version { get; private set; }
        public int Count => _set.Count;

        /// <summary>Every starred card, as full paths.</summary>
        public IEnumerable<string> Paths => _set.Select(r => Path.Combine(_root, r)).ToList();

        public Favorites(string coordinateRoot, string file)
        {
            _root = FileOps.Normalize(coordinateRoot);
            _rootLabel = "UserData\\" + Path.GetFileName(_root);
            _file = file;
            Load();
        }

        public bool Contains(string path)
        {
            var rel = Rel(path);
            return rel != null && _set.Contains(rel);
        }

        /// <summary>Stars or un-stars the given cards. Returns how many changed.</summary>
        public int Set(IEnumerable<string> paths, bool favorite)
        {
            var n = 0;
            foreach (var p in paths)
            {
                var rel = Rel(p);
                if (rel == null) continue;
                if (favorite ? _set.Add(rel) : _set.Remove(rel)) n++;
            }
            if (n > 0) Save();
            return n;
        }

        /// <summary>A starred file was renamed or moved.</summary>
        public void Moved(IEnumerable<KeyValuePair<string, string>> oldToNew)
        {
            var changed = false;
            foreach (var kv in oldToNew)
            {
                var from = Rel(kv.Key);
                var to = Rel(kv.Value);
                if (from == null || !_set.Remove(from)) continue;
                if (to != null) _set.Add(to);
                changed = true;
            }
            if (changed) Save();
        }

        /// <summary>A folder was renamed or moved: every star inside it follows.</summary>
        public void FolderMoved(string oldFolder, string newFolder)
        {
            var from = Rel(oldFolder);
            var to = Rel(newFolder);
            if (from == null || to == null) return;
            var prefix = from.TrimEnd('/') + "/";
            var hits = _set.Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            if (hits.Count == 0) return;
            foreach (var h in hits)
            {
                _set.Remove(h);
                _set.Add(to.TrimEnd('/') + "/" + h.Substring(prefix.Length));
            }
            Save();
        }

        /// <summary>Files (or a folder and everything in it) went to the Recycle Bin.</summary>
        public void Removed(IEnumerable<string> paths)
        {
            var changed = false;
            foreach (var p in paths)
            {
                var rel = Rel(p);
                if (rel == null) continue;
                var prefix = rel.TrimEnd('/') + "/";
                changed |= _set.Remove(rel);
                changed |= _set.RemoveWhere(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) > 0;
            }
            if (changed) Save();
        }

        private string Rel(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string full;
            try { full = FileOps.Normalize(path); }
            catch { return null; }
            if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
            return full.Substring(_root.Length + 1).Replace('\\', '/');
        }

        private void Load()
        {
            _set.Clear();
            try
            {
                if (File.Exists(_file))
                    foreach (var line in File.ReadAllLines(_file, Encoding.UTF8))
                    {
                        var l = line.Trim().Replace('\\', '/');
                        if (l.Length > 0 && !l.StartsWith("#")) _set.Add(l);
                    }
            }
            catch (Exception ex)
            {
                Log.Source.LogWarning("Couldn't read favorites: " + ex.Message);
            }
            Version++;
        }

        private void Save()
        {
            Version++;
            try
            {
                var tmp = _file + ".tmp";
                var lines = new List<string> { "# Studio Browser favorites: one card per line, relative to " + _rootLabel };
                lines.AddRange(_set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
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
                Log.Source.LogWarning("Couldn't save favorites: " + ex.Message);
            }
        }
    }
}

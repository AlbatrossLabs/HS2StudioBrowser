using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HS2StudioBrowser.Files
{
    /// <summary>
    /// An ordered list of keys kept in a text file, one per line: favorites (a set) or recently used
    /// (newest first, capped). Saved on every change.
    /// </summary>
    public sealed class KeyList
    {
        private readonly string _file;
        private readonly int _cap;
        private readonly List<string> _list = new List<string>();
        private readonly HashSet<string> _set = new HashSet<string>(StringComparer.Ordinal);

        public int Version { get; private set; }
        public int Count => _list.Count;
        public IList<string> Items => _list;

        /// <param name="cap">0 = no limit (favorites); otherwise the newest <paramref name="cap"/> are kept (recents).</param>
        public KeyList(string file, int cap = 0)
        {
            _file = file;
            _cap = cap;
            try
            {
                if (File.Exists(file))
                    foreach (var line in File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0))
                        if (_set.Add(line)) _list.Add(line);
            }
            catch (Exception ex)
            {
                Log.Source.LogWarning($"Couldn't read {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        public bool Contains(string key) => key != null && _set.Contains(key);

        public void Set(string key, bool on)
        {
            if (key == null || on == _set.Contains(key)) return;
            if (on) { _set.Add(key); _list.Add(key); }
            else { _set.Remove(key); _list.Remove(key); }
            Save();
        }

        /// <summary>Moves (or adds) <paramref name="key"/> to the front, dropping the oldest past the cap.</summary>
        public void Touch(string key)
        {
            if (key == null) return;
            if (_list.Count > 0 && _list[0] == key) return;
            _list.Remove(key);
            _list.Insert(0, key);
            _set.Add(key);
            while (_cap > 0 && _list.Count > _cap)
            {
                _set.Remove(_list[_list.Count - 1]);
                _list.RemoveAt(_list.Count - 1);
            }
            Save();
        }

        public void Clear()
        {
            if (_list.Count == 0) return;
            _list.Clear();
            _set.Clear();
            Save();
        }

        private void Save()
        {
            Version++;
            try { File.WriteAllLines(_file, _list.ToArray()); }
            catch (Exception ex) { Log.Source.LogWarning($"Couldn't save {Path.GetFileName(_file)}: {ex.Message}"); }
        }
    }
}

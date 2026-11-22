using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HS2StudioBrowser.Files
{
    /// <summary>
    /// Your own folders for a list browser (maps, items, animations), in BepInEx\config\HS2StudioBrowser.&lt;kind&gt;.folders.txt,
    /// one name per line in your order. What's in each folder is kept with the tags, as "Folder:&lt;name&gt;", so it follows
    /// the item's key like favorites do. An item can be in several folders.
    /// </summary>
    public sealed class FolderList
    {
        public const string Group = "Folder";

        private readonly string _file;
        private readonly List<string> _names = new List<string>();

        public int Version { get; private set; }
        public IList<string> Names => _names;

        public FolderList(string file)
        {
            _file = file;
            try
            {
                if (File.Exists(file))
                    foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
                    {
                        var n = Clean(line);
                        if (n.Length > 0 && !Contains(n)) _names.Add(n);
                    }
            }
            catch (Exception ex)
            {
                Log.Source.LogWarning($"Couldn't read {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        /// <summary>The tag that puts an item in this folder.</summary>
        public static string Tag(string name) => Group + ":" + name;

        /// <summary>The folders as a tag group (for the details panel and setting membership).</summary>
        public TagFacet Facet
        {
            get
            {
                var f = new TagFacet { Name = Group, Single = false };
                f.Values.AddRange(_names);
                return f;
            }
        }

        public bool Contains(string name) => name != null && _names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

        public static string Clean(string name) => (name ?? "").Replace(":", " ").Replace(",", " ").Replace("\t", " ").Trim();

        /// <summary>Adds a folder; false if the name is empty or taken.</summary>
        public bool Add(string name)
        {
            name = Clean(name);
            if (name.Length == 0 || Contains(name)) return false;
            _names.Add(name);
            Save();
            return true;
        }

        public void Remove(string name)
        {
            if (_names.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) > 0) Save();
        }

        /// <summary>Renames a folder in the list (the caller moves the items' tags); false if the new name is empty or taken.</summary>
        public bool Rename(string from, string to)
        {
            to = Clean(to);
            var i = _names.FindIndex(n => string.Equals(n, from, StringComparison.OrdinalIgnoreCase));
            if (i < 0 || to.Length == 0 || (Contains(to) && !string.Equals(from, to, StringComparison.OrdinalIgnoreCase))) return false;
            _names[i] = to;
            Save();
            return true;
        }

        private void Save()
        {
            Version++;
            try { File.WriteAllLines(_file, _names.ToArray(), new UTF8Encoding(false)); }
            catch (Exception ex) { Log.Source.LogWarning($"Couldn't save {Path.GetFileName(_file)}: {ex.Message}"); }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HS2StudioBrowser.Files
{
    /// <summary>Your own names for catalog items (key → name), in BepInEx\config\HS2StudioBrowser.&lt;kind&gt;.names.txt.</summary>
    public sealed class NameStore
    {
        private readonly string _file;
        private readonly Dictionary<string, string> _names = new Dictionary<string, string>(StringComparer.Ordinal);

        public int Version { get; private set; }
        public IEnumerable<KeyValuePair<string, string>> All => _names;

        public NameStore(string file)
        {
            _file = file;
            try
            {
                if (File.Exists(file))
                    foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
                    {
                        if (line.Length == 0 || line[0] == '#') continue;
                        var tab = line.IndexOf('\t');
                        if (tab > 0 && tab < line.Length - 1) _names[line.Substring(0, tab)] = line.Substring(tab + 1);
                    }
            }
            catch (Exception ex)
            {
                Log.Source.LogWarning($"Couldn't read {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        public string Get(string key) => key != null && _names.TryGetValue(key, out var n) ? n : null;

        /// <summary>Sets (or, with null / empty, clears) the name for a key.</summary>
        public void Set(string key, string name)
        {
            if (key == null) return;
            name = name?.Replace("\t", " ").Replace("\r", "").Replace("\n", " ").Trim();
            if (string.IsNullOrEmpty(name)) _names.Remove(key);
            else _names[key] = name;
            Version++;
            try
            {
                var lines = new List<string> { "# Studio Browser: your own names for items. <key><tab><name>" };
                lines.AddRange(_names.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "\t" + kv.Value));
                File.WriteAllLines(_file, lines.ToArray(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log.Source.LogWarning($"Couldn't save {Path.GetFileName(_file)}: {ex.Message}");
            }
        }
    }
}

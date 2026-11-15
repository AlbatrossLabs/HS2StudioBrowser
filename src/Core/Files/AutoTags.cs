using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace HS2StudioBrowser.Files
{
    /// <summary>
    /// Tags worked out from each card's own data (an outfit's clothing slots, a scene's cast...), read on a
    /// worker thread and cached by path and date in BepInEx\cache\HS2StudioBrowser.&lt;kind&gt;.autotags.txt,
    /// so each card is read once until it changes.
    /// </summary>
    public sealed class AutoTagIndex
    {
        private readonly Func<string, IEnumerable<string>> _read;
        private readonly string _root;
        private readonly string _cacheFile;
        private readonly int _logicVersion;
        private readonly object _lock = new object();
        private readonly Dictionary<string, KeyValuePair<long, string[]>> _cache = new Dictionary<string, KeyValuePair<long, string[]>>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<KeyValuePair<string, DateTime>> _queue = new Queue<KeyValuePair<string, DateTime>>();
        private readonly HashSet<string> _queued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Thread _worker;
        private volatile int _done;
        private int _seenDone;
        private bool _dirty;
        private DateTime _nextSave;

        private static readonly string[] None = new string[0];

        public int Version { get; private set; }
        public int Pending { get { lock (_lock) return _queue.Count; } }

        /// <param name="logicVersion">Bump when the kind reads tags differently, so the cache is read again.</param>
        public AutoTagIndex(Func<string, IEnumerable<string>> read, string root, string cacheFile, int logicVersion)
        {
            _read = read;
            _root = FileOps.Normalize(root);
            _cacheFile = cacheFile;
            _logicVersion = logicVersion;
            Load();
        }

        /// <summary>The card's automatic tags, or none while it hasn't been read yet.</summary>
        public string[] Get(string path, DateTime modified)
        {
            lock (_lock)
                return _cache.TryGetValue(Rel(path), out var kv) && kv.Key == modified.Ticks ? kv.Value : None;
        }

        /// <summary>Queues every card not read yet (or changed since).</summary>
        public void Request(IEnumerable<CardEntry> entries)
        {
            lock (_lock)
            {
                foreach (var e in entries)
                {
                    var rel = Rel(e.Path);
                    if (_cache.TryGetValue(rel, out var kv) && kv.Key == e.Modified.Ticks) continue;
                    if (_queued.Add(e.Path)) _queue.Enqueue(new KeyValuePair<string, DateTime>(e.Path, e.Modified));
                }
                if (_queue.Count > 0 && (_worker == null || !_worker.IsAlive))
                {
                    _worker = new Thread(Work) { IsBackground = true, Name = "Studio Browser auto tags", Priority = ThreadPriority.BelowNormal };
                    _worker.Start();
                }
            }
        }

        /// <summary>Main thread, every frame. True when new results came in (now and then, not per card).</summary>
        public bool Poll()
        {
            var done = _done;
            var changed = false;
            if (done != _seenDone && (Pending == 0 || done - _seenDone >= 200))
            {
                _seenDone = done;
                Version++;
                changed = true;
            }
            if (_dirty && DateTime.UtcNow >= _nextSave && Pending == 0)
            {
                _nextSave = DateTime.UtcNow.AddSeconds(5);
                Save();
            }
            return changed;
        }

        private void Work()
        {
            while (true)
            {
                KeyValuePair<string, DateTime> job;
                lock (_lock)
                {
                    if (_queue.Count == 0) return;
                    job = _queue.Dequeue();
                    _queued.Remove(job.Key);
                }
                string[] tags;
                try { tags = _read(job.Key)?.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? None; }
                catch { tags = None; }
                lock (_lock)
                {
                    _cache[Rel(job.Key)] = new KeyValuePair<long, string[]>(job.Value.Ticks, tags);
                    _dirty = true;
                }
                _done++;
            }
        }

        private string Rel(string path)
        {
            var full = FileOps.Normalize(path);
            return full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(_root.Length + 1).Replace('\\', '/')
                : full;
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_cacheFile)) return;
                var lines = File.ReadAllLines(_cacheFile, Encoding.UTF8);
                if (lines.Length == 0 || lines[0] != Header) return; // older logic: read everything again
                foreach (var line in lines.Skip(1))
                {
                    var p = line.Split('\t');
                    if (p.Length < 2 || !long.TryParse(p[1], out var ticks)) continue;
                    _cache[p[0]] = new KeyValuePair<long, string[]>(ticks, p.Length > 2 && p[2].Length > 0 ? p[2].Split('|') : None);
                }
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug("Couldn't read the auto tag cache: " + ex.Message);
            }
        }

        private string Header => "# Studio Browser automatic tags v" + _logicVersion;

        private void Save()
        {
            List<string> lines;
            lock (_lock)
            {
                _dirty = false;
                lines = new List<string> { Header };
                lines.AddRange(_cache.Select(kv => kv.Key + "\t" + kv.Value.Key + "\t" + string.Join("|", kv.Value.Value)));
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile));
                File.WriteAllLines(_cacheFile, lines.ToArray(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug("Couldn't save the auto tag cache: " + ex.Message);
            }
        }
    }
}

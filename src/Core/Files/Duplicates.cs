using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace HS2StudioBrowser.Files
{
    /// <summary>
    /// What makes two cards the same, from <see cref="BrowserKind.DuplicateKey"/>. Exact: a hash of the
    /// card's data (usually everything after the picture). Bucket + Values: for kinds that also look for
    /// near-identical cards (poses): cards in the same bucket whose values all lie within the tolerance.
    /// </summary>
    public sealed class DupeKey
    {
        public string Exact;
        public string Bucket;
        public float[] Values;
        /// <summary>Which entries of <see cref="Values"/> are angles in degrees (compared around the circle).</summary>
        public bool[] IsAngle;

        /// <summary>Hash of the file's bytes from <paramref name="offset"/> on (to <paramref name="end"/>, or the end).</summary>
        public static string Hash(byte[] bytes, int offset, int end = -1)
        {
            if (end < 0) end = bytes.Length;
            using (var sha = SHA1.Create())
                return Convert.ToBase64String(sha.ComputeHash(bytes, offset, Math.Max(0, end - offset)));
        }

        /// <summary>Everything after the PNG picture (the whole file if it has none).</summary>
        public static DupeKey AfterPicture(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var start = 0;
            using (var ms = new MemoryStream(bytes, false)) start = (int)Math.Max(0, PngLength(ms));
            return new DupeKey { Exact = bytes.Length + ":" + Hash(bytes, start) };
        }

        private static long PngLength(Stream s)
        {
            var sig = new byte[8];
            if (s.Read(sig, 0, 8) != 8 || sig[0] != 137 || sig[1] != 80 || sig[2] != 78 || sig[3] != 71) return 0;
            var head = new byte[8];
            while (s.Read(head, 0, 8) == 8)
            {
                var len = (head[0] << 24) | (head[1] << 16) | (head[2] << 8) | head[3];
                if (len < 0 || s.Position + len + 4 > s.Length) return 0;
                s.Position += len + 4;
                if (head[4] == 'I' && head[5] == 'E' && head[6] == 'N' && head[7] == 'D') return s.Position;
            }
            return 0;
        }
    }

    /// <summary>
    /// Finds sets of duplicate cards on a worker thread: exact matches first, then (where the kind gives
    /// values) near matches within a tolerance. Keys are cached by path and date, so a second run is quick.
    /// </summary>
    public sealed class DuplicateFinder
    {
        private readonly Func<string, DupeKey> _keyOf;
        private readonly object _lock = new object();
        private readonly Dictionary<string, KeyValuePair<DateTime, DupeKey>> _cache = new Dictionary<string, KeyValuePair<DateTime, DupeKey>>(StringComparer.OrdinalIgnoreCase);
        private int _generation;
        private Result _pending;

        public bool Busy { get; private set; }
        public int Version { get; private set; }
        public int Checked { get; private set; }
        public int Total { get; private set; }

        /// <summary>Path → set number (1 = the largest set). Only cards that have a duplicate are in it.</summary>
        public Dictionary<string, int> SetOf { get; private set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public int SetCount { get; private set; }
        /// <summary>Paths whose match is near, not exact (shown as "similar").</summary>
        public HashSet<string> Near { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private sealed class Result
        {
            public int Generation;
            public Dictionary<string, int> SetOf;
            public HashSet<string> Near;
            public int Sets;
        }

        public DuplicateFinder(Func<string, DupeKey> keyOf) { _keyOf = keyOf; }

        public void Run(List<KeyValuePair<string, DateTime>> files, float tolerance)
        {
            var gen = Interlocked.Increment(ref _generation);
            Busy = true;
            Total = files.Count;
            Checked = 0;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Result r;
                try { r = Find(files, tolerance, gen); }
                catch (Exception ex)
                {
                    Log.Source.LogWarning("Duplicate check failed: " + ex.Message);
                    r = new Result { SetOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), Near = new HashSet<string>(StringComparer.OrdinalIgnoreCase) };
                }
                r.Generation = gen;
                lock (_lock) _pending = r;
            });
        }

        /// <summary>Main thread, every frame: takes a finished run. True if the results changed.</summary>
        public bool Poll()
        {
            Result r;
            lock (_lock)
            {
                r = _pending;
                _pending = null;
            }
            if (r == null || r.Generation != _generation) return false;
            SetOf = r.SetOf;
            Near = r.Near;
            SetCount = r.Sets;
            Busy = false;
            Version++;
            return true;
        }

        private Result Find(List<KeyValuePair<string, DateTime>> files, float tolerance, int gen)
        {
            var keys = new List<KeyValuePair<string, DupeKey>>();
            foreach (var f in files)
            {
                if (gen != _generation) break;
                DupeKey k = null;
                lock (_cache)
                    if (_cache.TryGetValue(f.Key, out var c) && c.Key == f.Value) k = c.Value;
                if (k == null)
                {
                    try { k = _keyOf(f.Key); }
                    catch { k = null; }
                    lock (_cache) _cache[f.Key] = new KeyValuePair<DateTime, DupeKey>(f.Value, k);
                }
                if (k != null && k.Exact != null) keys.Add(new KeyValuePair<string, DupeKey>(f.Key, k));
                Checked++;
            }

            // Union-find over the cards.
            var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < keys.Count; i++) index[keys[i].Key] = i;
            var parent = Enumerable.Range(0, keys.Count).ToArray();
            int Root(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            void Join(int a, int b) { a = Root(a); b = Root(b); if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b); }

            // Exact.
            var firstOf = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < keys.Count; i++)
            {
                if (firstOf.TryGetValue(keys[i].Value.Exact, out var j)) Join(i, j);
                else firstOf[keys[i].Value.Exact] = i;
            }

            // Near: one card per exact group, compared within its bucket.
            var near = new HashSet<int>();
            if (tolerance > 0)
            {
                foreach (var bucket in firstOf.Values.Where(i => keys[i].Value.Bucket != null && keys[i].Value.Values != null)
                             .GroupBy(i => keys[i].Value.Bucket, StringComparer.Ordinal))
                {
                    var list = bucket.ToList();
                    if (list.Count < 2 || list.Count > 3000) continue;
                    for (var a = 0; a < list.Count; a++)
                    for (var b = a + 1; b < list.Count; b++)
                    {
                        if (Root(list[a]) == Root(list[b])) continue;
                        if (Close(keys[list[a]].Value, keys[list[b]].Value, tolerance))
                        {
                            Join(list[a], list[b]);
                            near.Add(list[a]);
                            near.Add(list[b]);
                        }
                    }
                    if (gen != _generation) break;
                }
            }

            var groups = Enumerable.Range(0, keys.Count).GroupBy(Root).Where(g => g.Count() > 1)
                .OrderByDescending(g => g.Count()).ThenBy(g => keys[g.First()].Key, StringComparer.OrdinalIgnoreCase).ToList();
            var setOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var nearPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var n = 0;
            foreach (var g in groups)
            {
                n++;
                // A set made only by near matches, or mixing them, marks its near members.
                var exactCounts = g.GroupBy(i => keys[i].Value.Exact).ToList();
                foreach (var i in g)
                {
                    setOf[keys[i].Key] = n;
                    if (exactCounts.Count > 1) nearPaths.Add(keys[i].Key);
                }
            }
            return new Result { SetOf = setOf, Near = nearPaths, Sets = n };
        }

        private static bool Close(DupeKey a, DupeKey b, float tol)
        {
            var va = a.Values;
            var vb = b.Values;
            if (va.Length != vb.Length) return false;
            var plain = tol / 100f; // positions and scales: 1 degree ~ 0.01 units
            for (var i = 0; i < va.Length; i++)
            {
                var d = Math.Abs(va[i] - vb[i]);
                if (a.IsAngle != null && a.IsAngle[i])
                {
                    d %= 360f;
                    if (d > 180f) d = 360f - d;
                    if (d > tol) return false;
                }
                else if (d > plain) return false;
            }
            return true;
        }
    }
}

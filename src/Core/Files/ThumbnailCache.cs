using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace HS2StudioBrowser.Files
{
    /// <summary>
    /// Card thumbnails, loaded only for cards on screen. A worker thread reads the PNG part of each card;
    /// the main thread turns a few per frame into textures. The least recently drawn ones are freed past
    /// the cache size, and everything is freed when the window closes.
    /// </summary>
    public sealed class ThumbnailCache : IDisposable
    {
        private sealed class Item
        {
            public Texture2D Texture;
            public DateTime Modified;
            public int LastUsed;
            public bool Failed;
        }

        private readonly Dictionary<string, Item> _items = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();
        private readonly List<string> _wanted = new List<string>();          // main → worker, newest first
        private readonly Queue<KeyValuePair<string, byte[]>> _done = new Queue<KeyValuePair<string, byte[]>>(); // worker → main
        private readonly HashSet<string> _inFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private Thread _thread;
        private volatile bool _stop;
        private int _frame;
        private readonly HashSet<string> _requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // main thread only

        public int Capacity = 300;
        public int PerFrame = 6;

        /// <summary>The texture for a card, or null while it loads. Asks for it if needed.</summary>
        public Texture2D Get(string path, DateTime modified)
        {
            if (_items.TryGetValue(path, out var item))
            {
                if (item.Modified == modified)
                {
                    item.LastUsed = _frame;
                    return item.Texture;
                }
                Free(path, item);
            }
            if (_requested.Add(path))
            {
                lock (_lock)
                {
                    if (!_inFlight.Contains(path) && !_wanted.Contains(path)) _wanted.Add(path);
                }
            }
            return null;
        }

        public bool Failed(string path) => _items.TryGetValue(path, out var i) && i.Failed;

        /// <summary>
        /// Call once per frame (Update): drops requests for cards that weren't drawn last frame, wakes the
        /// loader for the rest, and turns a few finished reads into textures.
        /// </summary>
        public void Tick(Func<string, DateTime> modifiedOf)
        {
            _frame++;
            EnsureThread();
            bool any;
            lock (_lock)
            {
                _wanted.RemoveAll(w => !_requested.Contains(w));
                any = _wanted.Count > 0;
            }
            _requested.Clear();
            if (any) _signal.Set();

            for (var i = 0; i < PerFrame; i++)
            {
                KeyValuePair<string, byte[]> next;
                lock (_lock)
                {
                    if (_done.Count == 0) break;
                    next = _done.Dequeue();
                    _inFlight.Remove(next.Key);
                }
                var item = new Item { Modified = modifiedOf(next.Key), LastUsed = _frame };
                if (next.Value != null)
                {
                    var tex = new Texture2D(2, 2, TextureFormat.ARGB32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                    if (tex.LoadImage(next.Value, true)) item.Texture = tex;
                    else { UnityEngine.Object.Destroy(tex); item.Failed = true; }
                }
                else item.Failed = true;

                if (_items.TryGetValue(next.Key, out var old)) Free(next.Key, old);
                _items[next.Key] = item;
            }

            Trim();
        }

        /// <summary>Forget one card (renamed, moved, deleted).</summary>
        public void Invalidate(string path)
        {
            if (_items.TryGetValue(path, out var item)) Free(path, item);
        }

        public void Clear()
        {
            foreach (var kv in _items) if (kv.Value.Texture != null) UnityEngine.Object.Destroy(kv.Value.Texture);
            _items.Clear();
            lock (_lock)
            {
                _wanted.Clear();
                _done.Clear();
                _inFlight.Clear();
            }
            _requested.Clear();
        }

        public void Dispose()
        {
            _stop = true;
            _signal.Set();
            Clear();
        }

        private void Free(string path, Item item)
        {
            if (item.Texture != null) UnityEngine.Object.Destroy(item.Texture);
            _items.Remove(path);
        }

        private void Trim()
        {
            if (_items.Count <= Capacity) return;
            var list = new List<KeyValuePair<string, Item>>(_items);
            list.Sort((a, b) => a.Value.LastUsed.CompareTo(b.Value.LastUsed));
            for (var i = 0; i < list.Count - Capacity; i++)
            {
                if (list[i].Value.LastUsed >= _frame - 2) break; // still on screen
                Free(list[i].Key, list[i].Value);
            }
        }

        private void EnsureThread()
        {
            if (_thread != null && _thread.IsAlive) return;
            _stop = false;
            _thread = new Thread(Work) { IsBackground = true, Name = "StudioBrowser thumbnails" };
            _thread.Start();
        }

        private void Work()
        {
            while (!_stop)
            {
                string path = null;
                lock (_lock)
                {
                    foreach (var w in _wanted)
                    {
                        if (_inFlight.Contains(w)) continue;
                        path = w;
                        break;
                    }
                    if (path != null)
                    {
                        _wanted.Remove(path);
                        _inFlight.Add(path);
                    }
                }

                if (path == null)
                {
                    _signal.WaitOne(500);
                    continue;
                }

                byte[] bytes = null;
                try
                {
                    if (File.Exists(path)) bytes = PngFile.LoadPngBytes(path);
                }
                catch
                {
                    bytes = null;
                }
                lock (_lock) _done.Enqueue(new KeyValuePair<string, byte[]>(path, bytes));
            }
        }
    }
}

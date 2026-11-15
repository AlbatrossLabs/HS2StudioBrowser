using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx;
using Studio;
using UnityEngine;

namespace HS2StudioBrowser.Items
{
    /// <summary>
    /// Item thumbnails. First the ones Quick Access Box uses: PNGs in zipmods (thumbnail packs), named by
    /// Quick Access Box's item id, looked up through Sideloader exactly as it does. Then pictures this
    /// browser made before (BepInEx\cache\HS2StudioBrowser.items.thumbs). Only an item with neither is
    /// rendered: its prefab loaded off-screen on a layer of its own, shot from the front-left like Quick
    /// Access Box's own generator, saved, and unloaded. Only items on screen are looked up; at most one is
    /// rendered per frame.
    /// </summary>
    internal sealed class ItemThumbs
    {
        private const int Size = 128;
        private const int MaxTextures = 600;

        private readonly Dictionary<string, Texture2D> _tex = new Dictionary<string, Texture2D>();
        private readonly LinkedList<string> _lru = new LinkedList<string>();
        private readonly HashSet<string> _none = new HashSet<string>();      // nothing to show (no renderers)
        private readonly HashSet<string> _borrowed = new HashSet<string>();  // Quick Access Box's textures: never destroyed here
        private readonly List<KeyValuePair<string, ItemTable.Ids>> _wanted = new List<KeyValuePair<string, ItemTable.Ids>>();
        private readonly HashSet<string> _wantedKeys = new HashSet<string>();
        private Dictionary<string, string> _packPngs;                        // Sideloader png name (no folder/ext) -> path
        private readonly HashSet<string> _bundlesUsed = new HashSet<string>();
        private int _rendersSinceUnload;

        private Camera _cam;
        private Light _key, _fill;
        private RenderTexture _rt;
        private Transform _stage;
        private int _layer = -1;

        public int FromPacks { get; private set; }
        public int FromCache { get; private set; }
        public int Rendered { get; private set; }
        public int Pending => _wanted.Count;
        public bool Enabled = true;

        private static string CacheDir => Path.Combine(Paths.CachePath, "HS2StudioBrowser.items.thumbs");

        /// <summary>The thumbnail if it's ready; otherwise null, and the item is queued (call every frame it's on screen).</summary>
        public Texture2D Get(string key, ItemTable.Ids ids)
        {
            if (_tex.TryGetValue(key, out var t) && t != null)
            {
                Touch(key);
                return t;
            }
            if (Enabled && !_none.Contains(key) && _wantedKeys.Add(key)) _wanted.Add(new KeyValuePair<string, ItemTable.Ids>(key, ids));
            return null;
        }

        /// <summary>True when the item has no picture to show (nothing visible in it).</summary>
        public bool Missing(string key) => _none.Contains(key);

        /// <summary>Main thread, every frame: load what's wanted (packs and cache are quick), render at most one.</summary>
        public void Tick()
        {
            if (_wanted.Count == 0)
            {
                if (_rendersSinceUnload > 0) UnloadBundles();
                return;
            }
            var clock = Stopwatch.StartNew();
            var rendered = false;
            // Newest requests first: what's on screen now.
            for (var i = _wanted.Count - 1; i >= 0 && clock.ElapsedMilliseconds < 6; i--)
            {
                var job = _wanted[i];
                var tex = FromQuickAccessBox(job.Value);
                if (tex != null) _borrowed.Add(job.Key);
                else tex = FromPack(job.Value) ?? FromCacheFile(job.Value);
                if (tex == null)
                {
                    if (rendered) continue;   // one render per frame
                    rendered = true;
                    tex = Render(job.Value);
                }
                _wanted.RemoveAt(i);
                _wantedKeys.Remove(job.Key);
                if (tex != null) Store(job.Key, tex);
                else _none.Add(job.Key);
            }
            // Requests for items scrolled away are dropped; they come back when shown again.
            if (_wanted.Count > 60)
            {
                foreach (var old in _wanted.Take(_wanted.Count - 60)) _wantedKeys.Remove(old.Key);
                _wanted.RemoveRange(0, _wanted.Count - 60);
            }
            if (_rendersSinceUnload >= 25) UnloadBundles();
        }

        /// <summary>Frees every texture (the window closed).</summary>
        public void Clear()
        {
            foreach (var kv in _tex) if (kv.Value != null && !_borrowed.Contains(kv.Key)) UnityEngine.Object.Destroy(kv.Value);
            _tex.Clear();
            _borrowed.Clear();
            _lru.Clear();
            _wanted.Clear();
            _wantedKeys.Clear();
            UnloadBundles();
        }

        private void Store(string key, Texture2D tex)
        {
            _tex[key] = tex;
            Touch(key);
            while (_lru.Count > MaxTextures)
            {
                var oldest = _lru.Last.Value;
                _lru.RemoveLast();
                if (_tex.TryGetValue(oldest, out var t) && t != null && !_borrowed.Contains(oldest)) UnityEngine.Object.Destroy(t);
                _tex.Remove(oldest);
                _borrowed.Remove(oldest);
            }
        }

        private void Touch(string key)
        {
            _lru.Remove(key);
            _lru.AddFirst(key);
        }

        // ------------------------------------------------------------------ ids (as Quick Access Box makes them)

        /// <summary>"GGGGGGGG-CCCCCCCC-SSSSSSSS[-HASH]": group, category, the zipmod's own number (or the item number), the GUID's hash.</summary>
        public static string NewCacheId(int group, int category, int slot, string guid)
        {
            var id = $"{group:D8}-{category:D8}-{slot:D8}";
            if (guid != null) id += $"-{StableHash(guid):X8}";
            return id;
        }

        /// <summary>Quick Access Box's string hash (stable across runs, unlike string.GetHashCode).</summary>
        public static int StableHash(string s)
        {
            unchecked
            {
                var a = 5381;
                var b = a;
                for (var i = 0; i < s.Length && s[i] != 0; i += 2)
                {
                    a = ((a << 5) + a) ^ s[i];
                    if (i == s.Length - 1 || s[i + 1] == '\0') break;
                    b = ((b << 5) + b) ^ s[i + 1];
                }
                return a + b * 1566083941;
            }
        }

        private static readonly char[] Invalid = Path.GetInvalidFileNameChars();

        /// <summary>The old id: group, category and the item's name made file-safe.</summary>
        public static string OldCacheId(int group, int category, string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                if (Array.IndexOf(Invalid, c) < 0) sb.Append(c);
                else if (c == '"') sb.Append('”');
                else if (c == '\'') sb.Append('’');
                else if (c == '/') sb.Append('⁄');
                else sb.Append('_');
            }
            return $"{group:D8}-{category:D8}-{sb}";
        }

        public static IEnumerable<string> CacheIds(ItemTable.Ids ids)
        {
            if (ids.Guid != null)
            {
                yield return NewCacheId(ids.Group, ids.Category, ids.Slot, ids.Guid);
                yield return OldCacheId(ids.Group, ids.Category, ids.Name ?? "");
                yield return NewCacheId(ids.Group, ids.Category, ids.Slot, null);
            }
            else
            {
                yield return NewCacheId(ids.Group, ids.Category, ids.No, null);
                yield return OldCacheId(ids.Group, ids.Category, ids.Name ?? "");
            }
        }

        // ------------------------------------------------------------------ Quick Access Box

        // Quick Access Box knows every picture it can show: the zipmod packs, and pictures other plugins hand
        // it (Accessories to Studio Items gives it the accessories' own icons). Asking it covers all of them.
        private static bool _qabResolved;
        private static System.Reflection.PropertyInfo _qabList, _qabThumb, _qabGroup, _qabCategory, _qabSlot;
        private static System.Reflection.FieldInfo _qabMissing, _qabSound;
        private Dictionary<long, object> _qabIndex;
        private int _qabCount = -1;
        public int FromQab { get; private set; }

        private static void ResolveQab()
        {
            if (_qabResolved) return;
            _qabResolved = true;
            try
            {
                if (!BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("HS2_QuickAccessBox")) return;
                var loader = HarmonyLib.AccessTools.TypeByName("KK_QuickAccessBox.ItemInfoLoader");
                var item = HarmonyLib.AccessTools.TypeByName("KK_QuickAccessBox.ItemInfo");
                var thumbs = HarmonyLib.AccessTools.TypeByName("KK_QuickAccessBox.Thumbs.ThumbnailLoader");
                if (loader == null || item == null || thumbs == null) return;
                _qabList = HarmonyLib.AccessTools.Property(loader, "ItemList");
                _qabThumb = HarmonyLib.AccessTools.Property(item, "Thumbnail");
                _qabGroup = HarmonyLib.AccessTools.Property(item, "GroupNo");
                _qabCategory = HarmonyLib.AccessTools.Property(item, "CategoryNo");
                _qabSlot = HarmonyLib.AccessTools.Property(item, "LocalSlot");
                _qabMissing = HarmonyLib.AccessTools.Field(thumbs, "_thumbMissing");
                _qabSound = HarmonyLib.AccessTools.Field(thumbs, "_thumbSound");
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug("Quick Access Box not usable for thumbnails: " + ex.Message);
                _qabList = null;
            }
        }

        private static long QabKey(int g, int c, int no) => ((long)g << 40) ^ ((long)(c & 0xFFFFF) << 20) ^ (uint)no;

        private Texture2D FromQuickAccessBox(ItemTable.Ids ids)
        {
            ResolveQab();
            if (_qabList == null || _qabThumb == null) return null;
            try
            {
                var list = _qabList.GetValue(null, null) as System.Collections.IEnumerable;
                if (list == null) return null;   // not loaded yet
                if (_qabIndex == null || (list is System.Collections.ICollection col && col.Count != _qabCount))
                {
                    _qabIndex = new Dictionary<long, object>();
                    foreach (var i in list)
                        _qabIndex[QabKey((int)_qabGroup.GetValue(i, null), (int)_qabCategory.GetValue(i, null), (int)_qabSlot.GetValue(i, null))] = i;
                    _qabCount = (list as System.Collections.ICollection)?.Count ?? _qabIndex.Count;
                }
                if (!_qabIndex.TryGetValue(QabKey(ids.Group, ids.Category, ids.No), out var info)) return null;
                var sprite = _qabThumb.GetValue(info, null) as Sprite;
                if (sprite == null || sprite.texture == null) return null;
                if (ReferenceEquals(sprite, _qabMissing?.GetValue(null)) || ReferenceEquals(sprite, _qabSound?.GetValue(null))) return null;
                FromQab++;
                return sprite.texture;
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug("Quick Access Box thumbnail lookup failed: " + ex.Message);
                _qabList = null; // stop asking
                return null;
            }
        }

        // ------------------------------------------------------------------ thumbnail packs (zipmods)

        private Texture2D FromPack(ItemTable.Ids ids)
        {
            if (_packPngs == null) IndexPacks();
            if (_packPngs.Count == 0) return null;
            foreach (var id in CacheIds(ids))
            {
                if (!_packPngs.TryGetValue(id, out var path)) continue;
                var tex = LoadPack(path);
                if (tex != null)
                {
                    FromPacks++;
                    return tex;
                }
            }
            return null;
        }

        private void IndexPacks()
        {
            _packPngs = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.bepis.bepinex.sideloader")) return;
            try { IndexPacksSideloader(); }
            catch (Exception ex) { Log.Source.LogDebug("Couldn't list the thumbnail packs: " + ex.Message); }
            Log.Source.LogInfo($"Item thumbnails: {_packPngs.Count} pictures available in zipmods.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void IndexPacksSideloader()
        {
            var sep = new[] { '/', '\\' };
            foreach (var png in Sideloader.Sideloader.GetPngNames())
            {
                var start = png.LastIndexOfAny(sep) + 1;
                var dot = png.LastIndexOf('.');
                if (dot <= start) continue;
                var key = png.Substring(start, dot - start);
                // Same rule as Quick Access Box: with two of the same name, the longer path wins.
                if (!_packPngs.TryGetValue(key, out var have) || have.Length <= png.Length) _packPngs[key] = png;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Texture2D LoadPack(string path)
        {
            try { return Sideloader.Sideloader.GetPng(path, TextureFormat.ARGB32, false); }
            catch { return null; }
        }

        // ------------------------------------------------------------------ own cache

        private static string CacheFile(ItemTable.Ids ids) => Path.Combine(CacheDir, CacheIds(ids).First() + ".png");

        private Texture2D FromCacheFile(ItemTable.Ids ids)
        {
            var file = CacheFile(ids);
            if (!File.Exists(file)) return null;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                if (!tex.LoadImage(File.ReadAllBytes(file)))
                {
                    UnityEngine.Object.Destroy(tex);
                    return null;
                }
                FromCache++;
                return tex;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------ rendering

        private bool Setup()
        {
            if (_cam != null) return true;
            // A layer nothing else uses, so the shot shows only the item and our lights touch only it.
            for (var l = 31; l >= 8; l--)
            {
                if (!string.IsNullOrEmpty(LayerMask.LayerToName(l))) continue;
                _layer = l;
                break;
            }
            if (_layer < 0) return false;

            var root = new GameObject("StudioBrowser item thumbnails") { hideFlags = HideFlags.HideAndDontSave };
            root.transform.position = new Vector3(0, -20000f, 0);
            _stage = root.transform;

            _cam = new GameObject("camera").AddComponent<Camera>();
            _cam.transform.SetParent(_stage, false);
            _cam.enabled = false;
            _cam.orthographic = true;
            _cam.cullingMask = 1 << _layer;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.16f, 0.17f, 0.19f, 1f);
            _cam.allowHDR = false;
            _cam.allowMSAA = true;
            _rt = new RenderTexture(Size * 2, Size * 2, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            _cam.targetTexture = _rt;

            _key = new GameObject("key").AddComponent<Light>();
            _key.transform.SetParent(_stage, false);
            _key.type = LightType.Directional;
            _key.intensity = 1.0f;
            _key.cullingMask = 1 << _layer;
            _key.transform.rotation = Quaternion.Euler(40f, 30f, 0f);
            _fill = new GameObject("fill").AddComponent<Light>();
            _fill.transform.SetParent(_stage, false);
            _fill.type = LightType.Directional;
            _fill.intensity = 0.45f;
            _fill.cullingMask = 1 << _layer;
            _fill.transform.rotation = Quaternion.Euler(20f, -150f, 0f);
            return true;
        }

        private Texture2D Render(ItemTable.Ids ids)
        {
            Info.ItemLoadInfo info = null;
            try
            {
                if (Info.Instance.dicItemLoadInfo.TryGetValue(ids.Group, out var cats) && cats.TryGetValue(ids.Category, out var items))
                    items.TryGetValue(ids.No, out info);
            }
            catch { info = null; }
            if (info == null || !Setup()) return null;

            GameObject obj = null;
            try
            {
                obj = CommonLib.LoadAsset<GameObject>(info.bundlePath, info.fileName, true, info.manifest);
                _bundlesUsed.Add(info.bundlePath);
                _rendersSinceUnload++;
                if (obj == null) return null;
                obj.transform.SetParent(_stage, false);
                obj.transform.localPosition = Vector3.zero;
                Prepare(obj);

                var bounds = Bounds(obj);
                if (!bounds.HasValue) return null;
                var b = bounds.Value;
                var size = Mathf.Max(0.01f, b.size.magnitude);
                // Front-left and a little above, as Quick Access Box shoots them (items face +Z).
                var dir = (-obj.transform.forward - obj.transform.right + obj.transform.up).normalized;
                _cam.transform.position = b.center + dir * size * 2f;
                _cam.transform.LookAt(b.center);
                _cam.orthographicSize = size * 0.5f;
                _cam.nearClipPlane = 0.01f;
                _cam.farClipPlane = size * 6f;
                _cam.Render();

                var prev = RenderTexture.active;
                var small = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(_rt, small);
                RenderTexture.active = small;
                var tex = new Texture2D(Size, Size, TextureFormat.ARGB32, false);
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(small);

                try
                {
                    Directory.CreateDirectory(CacheDir);
                    File.WriteAllBytes(CacheFile(ids), tex.EncodeToPNG());
                }
                catch (Exception ex)
                {
                    Log.Source.LogDebug("Couldn't save an item thumbnail: " + ex.Message);
                }
                Rendered++;
                return tex;
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug($"Couldn't render {ids.Name}: {ex.Message}");
                return null;
            }
            finally
            {
                if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            }
        }

        /// <summary>Puts the item on the thumbnail layer and keeps its own lights, cameras and sounds from reaching the scene.</summary>
        private void Prepare(GameObject obj)
        {
            foreach (var t in obj.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = _layer;
            foreach (var l in obj.GetComponentsInChildren<Light>(true)) l.cullingMask = 1 << _layer;
            foreach (var c in obj.GetComponentsInChildren<Camera>(true)) c.enabled = false;
            foreach (var a in obj.GetComponentsInChildren<AudioSource>(true)) a.enabled = false;
        }

        private static Bounds? Bounds(GameObject obj)
        {
            Bounds? all = null;
            foreach (var r in obj.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || !r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (all == null) all = r.bounds;
                else
                {
                    var b = all.Value;
                    b.Encapsulate(r.bounds);
                    all = b;
                }
            }
            if (all != null) return all;
            // Effects only: frame the particles.
            foreach (var r in obj.GetComponentsInChildren<ParticleSystemRenderer>(false))
            {
                if (all == null) all = r.bounds;
                else
                {
                    var b = all.Value;
                    b.Encapsulate(r.bounds);
                    all = b;
                }
            }
            return all;
        }

        private void UnloadBundles()
        {
            foreach (var b in _bundlesUsed)
            {
                try { AssetBundleManager.UnloadAssetBundle(b, true); }
                catch { /* already gone */ }
            }
            _bundlesUsed.Clear();
            _rendersSinceUnload = 0;
        }
    }
}

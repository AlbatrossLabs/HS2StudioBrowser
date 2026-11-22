using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace HS2StudioBrowser.Maps
{
    /// <summary>
    /// Map pictures: taken from Studio's camera with the scene's characters and items hidden, saved as PNGs in
    /// BepInEx\cache\HS2StudioBrowser.maps.pictures (by the map's key), and loaded back a few per frame for the
    /// browser. Also remembers what each map has (option objects, lights, water), learned when it loads.
    /// </summary>
    internal sealed class MapShots
    {
        public const int Width = 640, Height = 360;

        private static string Folder => Path.Combine(Paths.CachePath, "HS2StudioBrowser.maps.pictures");
        private static string InfoFile => Path.Combine(Paths.CachePath, "HS2StudioBrowser.maps.info.txt");

        private readonly HashSet<string> _onDisk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Texture2D> _loaded = new Dictionary<string, Texture2D>();
        private readonly Queue<string> _toLoad = new Queue<string>();
        private readonly HashSet<string> _queued = new HashSet<string>();
        private readonly Dictionary<string, string> _info = new Dictionary<string, string>();

        /// <summary>Goes up whenever a picture or a map's details change (filters with automatic tags redo their counts).</summary>
        public int Version { get; private set; }

        public MapShots()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                // A tiny file is a blank picture (0.18.0 saved black ones): treat it as missing so it's taken again.
                foreach (var f in Directory.GetFiles(Folder, "*.png"))
                    if (new FileInfo(f).Length > 4096) _onDisk.Add(Path.GetFileNameWithoutExtension(f));
                if (File.Exists(InfoFile))
                    foreach (var line in File.ReadAllLines(InfoFile, Encoding.UTF8))
                    {
                        var tab = line.IndexOf('\t');
                        if (tab > 0) _info[line.Substring(0, tab)] = line.Substring(tab + 1);
                    }
            }
            catch (Exception ex)
            {
                Log.Source.LogWarning("Couldn't read the map picture folder: " + ex.Message);
            }
        }

        private static string FileName(string key)
        {
            var sb = new StringBuilder(key.Length);
            foreach (var ch in key) sb.Append(char.IsLetterOrDigit(ch) || ch == '.' || ch == '-' ? ch : '_');
            return sb.ToString();
        }

        public bool Has(string key) => key != null && _onDisk.Contains(FileName(key));
        public int Saved => _onDisk.Count;

        public Texture2D Get(string key)
        {
            if (_loaded.TryGetValue(key, out var t) && t != null) return t;
            if (Has(key) && _queued.Add(key)) _toLoad.Enqueue(key);
            return null;
        }

        /// <summary>Loads a few queued pictures (called every frame while the grid shows).</summary>
        public void Tick()
        {
            for (var n = 0; n < 4 && _toLoad.Count > 0; n++)
            {
                var key = _toLoad.Dequeue();
                _queued.Remove(key);
                try
                {
                    var bytes = File.ReadAllBytes(Path.Combine(Folder, FileName(key) + ".png"));
                    var tex = new Texture2D(2, 2, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                    if (tex.LoadImage(bytes, true)) _loaded[key] = tex;
                    else UnityEngine.Object.Destroy(tex);
                }
                catch (Exception ex)
                {
                    Log.Source.LogDebug($"Couldn't load the picture for {key}: {ex.Message}");
                }
            }
        }

        public int Pending => _toLoad.Count;

        /// <summary>Frees every loaded picture (the window closed).</summary>
        public void Clear()
        {
            foreach (var t in _loaded.Values) if (t != null) UnityEngine.Object.Destroy(t);
            _loaded.Clear();
            _toLoad.Clear();
            _queued.Clear();
        }

        public void Save(string key, byte[] png)
        {
            var name = FileName(key);
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), png);
            _onDisk.Add(name);
            if (_loaded.TryGetValue(key, out var old) && old != null) UnityEngine.Object.Destroy(old);
            _loaded.Remove(key);
            Version++;
        }

        // ---------------------------------------------------------------- what a map has

        /// <summary>The map's extras as learned when it loaded: "Options", "Lights", "Water" (empty: none; null: never loaded).</summary>
        public string[] Extras(string key) =>
            key != null && _info.TryGetValue(key, out var s) ? s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries) : null;

        public void Remember(string key, IEnumerable<string> extras)
        {
            if (key == null) return;
            var value = string.Join(",", extras.ToArray());
            if (_info.TryGetValue(key, out var old) && old == value) return;
            _info[key] = value;
            Version++;
            try { File.WriteAllLines(InfoFile, _info.OrderBy(kv => kv.Key).Select(kv => kv.Key + "\t" + kv.Value).ToArray(), new UTF8Encoding(false)); }
            catch (Exception ex) { Log.Source.LogDebug("Couldn't save map details: " + ex.Message); }
        }

        // ---------------------------------------------------------------- taking a picture

        /// <summary>Things hidden for the one frame a picture is read off the screen, and put back after.</summary>
        internal sealed class Hidden
        {
            public readonly List<Canvas> Canvases = new List<Canvas>();
            public readonly List<Renderer> Renderers = new List<Renderer>();
            public Camera Cam;
            public int Mask;

            public void Restore()
            {
                foreach (var c in Canvases) if (c != null) c.enabled = true;
                foreach (var r in Renderers) if (r != null) r.enabled = true;
                if (Cam != null) Cam.cullingMask = Mask;
                Canvases.Clear();
                Renderers.Clear();
                Cam = null;
                StudioBrowserPlugin.HideWindows = false;
            }
        }

        /// <summary>
        /// Clears the screen of Studio's menus, the browser windows, the move gizmos and (optionally) the
        /// characters, for the frame about to be drawn.
        /// </summary>
        public static Hidden HideForShot(bool hideCharacters)
        {
            var h = new Hidden();
            StudioBrowserPlugin.HideWindows = true;
            foreach (var c in UnityEngine.Object.FindObjectsOfType<Canvas>())
                if (c != null && c.enabled && c.isRootCanvas && c.renderMode != RenderMode.WorldSpace)
                {
                    c.enabled = false;
                    h.Canvases.Add(c);
                }
            var cam = Camera.main;
            var gizmos = LayerMask.NameToLayer("Studio/Col");
            if (cam != null && gizmos >= 0)
            {
                h.Cam = cam;
                h.Mask = cam.cullingMask;
                cam.cullingMask &= ~(1 << gizmos);
            }
            if (hideCharacters)
                foreach (var oci in Studio.Studio.Instance.dicObjectCtrl.Values)
                {
                    if (!(oci is Studio.OCIChar)) continue;
                    var root = oci.guideObject != null ? oci.guideObject.transformTarget : null;
                    if (root == null) continue;
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                        if (r != null && r.enabled)
                        {
                            r.enabled = false;
                            h.Renderers.Add(r);
                        }
                }
            return h;
        }

        /// <summary>
        /// Reads the finished frame off the screen (call at the end of the frame), crops it to 16:9 from the
        /// middle and scales it to <see cref="Width"/> x <see cref="Height"/> in halving steps (clean, no shimmer).
        /// This is exactly what was on screen, graphics plugin's exposure and effects included.
        /// </summary>
        public static byte[] ReadScreen()
        {
            int sw = Screen.width, sh = Screen.height;
            var grab = new Texture2D(sw, sh, TextureFormat.RGB24, false);
            var was = RenderTexture.active;
            var temps = new List<RenderTexture>();
            Texture2D outTex = null;
            try
            {
                RenderTexture.active = null;
                grab.ReadPixels(new Rect(0, 0, sw, sh), 0, 0);
                grab.Apply();

                // 16:9 from the middle.
                float cw = sw, ch = sh;
                if (cw / ch > 16f / 9f) cw = ch * 16f / 9f; else ch = cw * 9f / 16f;
                var scale = new Vector2(cw / sw, ch / sh);
                var offset = new Vector2((1f - scale.x) / 2f, (1f - scale.y) / 2f);

                int w = Mathf.RoundToInt(cw), hgt = Mathf.RoundToInt(ch);
                var current = RenderTexture.GetTemporary(w, hgt, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                temps.Add(current);
                Graphics.Blit(grab, current, scale, offset);
                while (w / 2 >= Width && hgt / 2 >= Height)
                {
                    w /= 2;
                    hgt /= 2;
                    var next = RenderTexture.GetTemporary(w, hgt, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    temps.Add(next);
                    current.filterMode = FilterMode.Bilinear;
                    Graphics.Blit(current, next);
                    current = next;
                }
                var final = RenderTexture.GetTemporary(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                temps.Add(final);
                current.filterMode = FilterMode.Bilinear;
                Graphics.Blit(current, final);
                RenderTexture.active = final;
                outTex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                outTex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                outTex.Apply();
                var png = outTex.EncodeToPNG();
                Note($"screen {sw}x{sh} -> {Width}x{Height}, {png.Length} bytes");
                return png;
            }
            finally
            {
                RenderTexture.active = was;
                foreach (var t in temps) RenderTexture.ReleaseTemporary(t);
                UnityEngine.Object.Destroy(grab);
                if (outTex != null) UnityEngine.Object.Destroy(outTex);
            }
        }

        /// <summary>A line in BepInEx\cache\HS2StudioBrowser.maps.log, so picture problems can be traced even with BepInEx's log file off.</summary>
        internal static void Note(string line)
        {
            try
            {
                var file = Path.Combine(Paths.CachePath, "HS2StudioBrowser.maps.log");
                if (File.Exists(file) && new FileInfo(file).Length > 200_000) File.Delete(file);
                File.AppendAllText(file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + Environment.NewLine);
            }
            catch { /* best effort */ }
            Log.Source.LogDebug("Maps: " + line);
        }

    }
}

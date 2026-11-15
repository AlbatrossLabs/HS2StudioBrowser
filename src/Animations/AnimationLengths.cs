using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using Studio;
using UnityEngine;

namespace HS2StudioBrowser.Animations
{
    /// <summary>How long an animation runs: its longest clip's length and frame count, and whether it loops.</summary>
    internal sealed class Measure
    {
        public float Length;   // seconds
        public int Frames;
        public bool Loop;

        /// <summary>
        /// A held pose rather than a motion: one or two frames, or a short clip (half a second or less) that
        /// doesn't loop. Measured on 8,500 animations: about 155 of them, against 12 by frame count alone.
        /// </summary>
        public bool IsPose => Frames <= 2 || Length < 0.07f || (!Loop && Length <= 0.5f);

        public string Bucket => IsPose ? "Pose" : Length < 2f ? "Under 2 s" : Length <= 10f ? "2-10 s" : "Over 10 s";

        public string Text => IsPose ? "pose" : (Length < 10f ? Length.ToString("0.0", CultureInfo.InvariantCulture) : Mathf.RoundToInt(Length).ToString(CultureInfo.InvariantCulture)) + " s · " + Frames + " f";
    }

    /// <summary>
    /// Animation lengths, kept in BepInEx\config\HS2StudioBrowser.animations.lengths.txt by animation key.
    /// Measured in the background the first time the browser opens (each controller loaded once on a hidden
    /// Animator, a few milliseconds per frame), and again from the character whenever one is played.
    /// </summary>
    internal static class AnimationLengths
    {
        private static readonly Dictionary<string, Measure> ByKey = new Dictionary<string, Measure>(StringComparer.Ordinal);
        private static bool _loaded;
        private static bool _dirty;
        private static float _nextSave;

        public static int Version { get; private set; }
        public static bool Measuring { get; private set; }
        public static int Done { get; private set; }
        public static int Total { get; private set; }

        private static string FilePath => Path.Combine(Paths.ConfigPath, "HS2StudioBrowser.animations.lengths.txt");

        public static Measure Get(string key)
        {
            EnsureLoaded();
            return key != null && ByKey.TryGetValue(key, out var m) ? m : null;
        }

        public static void Set(string key, Measure m)
        {
            if (key == null || m == null) return;
            EnsureLoaded();
            ByKey[key] = m;
            _dirty = true;
            Version++;
        }

        /// <summary>Writes changes now and then (call every frame).</summary>
        public static void Tick()
        {
            if (!_dirty || Time.unscaledTime < _nextSave) return;
            _nextSave = Time.unscaledTime + 3f;
            Save();
        }

        /// <summary>Reads what an Animator is playing on layer 0 (after it has updated at least once).</summary>
        public static Measure Read(Animator animator)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return null;
            var clips = animator.GetCurrentAnimatorClipInfo(0);
            if (clips == null || clips.Length == 0) return null;
            AnimationClip longest = null;
            foreach (var c in clips)
                if (c.clip != null && (longest == null || c.clip.length > longest.length)) longest = c.clip;
            if (longest == null) return null;
            var state = animator.GetCurrentAnimatorStateInfo(0);
            return new Measure
            {
                Length = longest.length,
                Frames = Mathf.Max(1, Mathf.RoundToInt(longest.length * Mathf.Max(1f, longest.frameRate))),
                Loop = state.loop || longest.isLooping,
            };
        }

        /// <summary>
        /// Measures every animation in <paramref name="todo"/> (key → its load info) that hasn't been measured,
        /// controller by controller on a hidden Animator, spending at most ~4 ms per frame.
        /// </summary>
        public static IEnumerator MeasureAll(List<KeyValuePair<string, Info.AnimeLoadInfo>> todo)
        {
            EnsureLoaded();
            var left = todo.Where(t => t.Value != null && !ByKey.ContainsKey(t.Key)).ToList();
            if (left.Count == 0 || Measuring) yield break;
            Measuring = true;
            Done = 0;
            Total = left.Count;

            var go = new GameObject("StudioBrowser animation probe") { hideFlags = HideFlags.HideAndDontSave };
            var animator = go.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var clock = Stopwatch.StartNew();

            // One controller (plus its H override, if any) serves many animations: load each once.
            foreach (var group in left.GroupBy(t => ControllerKey(t.Value)))
            {
                var info = group.First().Value;
                RuntimeAnimatorController rac = null;
                try
                {
                    rac = CommonLib.LoadAsset<RuntimeAnimatorController>(info.bundlePath, info.fileName, false, info.manifest);
                    if (rac != null && info is Info.HAnimeLoadInfo h && h.overrideFile != null && h.overrideFile.Check)
                    {
                        var over = CommonLib.LoadAsset<RuntimeAnimatorController>(h.overrideFile.bundlePath, h.overrideFile.fileName);
                        if (over != null) rac = Illusion.Utils.Animator.SetupAnimatorOverrideController(rac, over);
                        AssetBundleManager.UnloadAssetBundle(h.overrideFile.bundlePath, true);
                    }
                }
                catch (Exception ex)
                {
                    Log.Source.LogDebug($"Couldn't load {info.bundlePath}/{info.fileName} to measure it: {ex.Message}");
                }

                if (rac != null)
                {
                    animator.runtimeAnimatorController = rac;
                    foreach (var t in group)
                    {
                        try
                        {
                            animator.Play(t.Value.clip, 0, 0f);
                            animator.Update(0f);
                            var m = Read(animator);
                            if (m != null)
                            {
                                ByKey[t.Key] = m;
                                _dirty = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Source.LogDebug($"Measuring {t.Value.name} failed: {ex.Message}");
                        }
                        Done++;
                    }
                    animator.runtimeAnimatorController = null;
                }
                else Done += group.Count();

                try { AssetBundleManager.UnloadAssetBundle(info.bundlePath, true); }
                catch { /* already gone */ }

                Version++;
                if (clock.ElapsedMilliseconds > 4)
                {
                    yield return null;
                    clock.Reset();
                    clock.Start();
                }
            }

            UnityEngine.Object.Destroy(go);
            Measuring = false;
            Save();
            Version++;
            Log.Source.LogInfo($"Measured {Done} animations.");
        }

        private static string ControllerKey(Info.AnimeLoadInfo i)
        {
            var over = i is Info.HAnimeLoadInfo h && h.overrideFile != null && h.overrideFile.Check ? h.overrideFile.bundlePath + "|" + h.overrideFile.fileName : "";
            return i.bundlePath + "|" + i.fileName + "|" + over;
        }

        // ------------------------------------------------------------------ file

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    var p = line.Split('\t');
                    if (p.Length < 4) continue;
                    if (!float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var len)) continue;
                    if (!int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var frames)) continue;
                    ByKey[p[0]] = new Measure { Length = len, Frames = frames, Loop = p[3] == "1" };
                }
            }
            catch (Exception ex)
            {
                Log.Source.LogWarning("Couldn't read animation lengths: " + ex.Message);
            }
        }

        private static void Save()
        {
            _dirty = false;
            try
            {
                var lines = new List<string> { "# Studio Browser animation lengths: key<tab>seconds<tab>frames<tab>loops (measured; delete the file to measure again)" };
                lines.AddRange(ByKey.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv =>
                    kv.Key + "\t" + kv.Value.Length.ToString("0.###", CultureInfo.InvariantCulture) + "\t" + kv.Value.Frames.ToString(CultureInfo.InvariantCulture) + "\t" + (kv.Value.Loop ? "1" : "0")));
                File.WriteAllLines(FilePath, lines.ToArray(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log.Source.LogWarning("Couldn't save animation lengths: " + ex.Message);
            }
        }
    }
}

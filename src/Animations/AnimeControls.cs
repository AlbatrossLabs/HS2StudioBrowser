using System;
using System.Collections.Generic;
using System.Linq;
using HS2StudioBrowser.UI;
using Studio;
using UnityEngine;

namespace HS2StudioBrowser.Animations
{
    /// <summary>
    /// Studio's animation box (speed, pattern, the two H option sliders, props, loop, Restart / Restart all,
    /// Copy / Paste) drawn inside the browser, plus a timeline to scrub to any frame. Works on the selected
    /// characters (speed and Restart also on selected animated items, as Studio does) with the same undo
    /// steps as Studio's own sliders.
    /// </summary>
    internal sealed class AnimeControls
    {
        private enum Drag { None, Speed, Pattern, Option0, Option1 }

        private Drag _drag;
        private int[] _dragKeys;
        private float[] _dragOld;
        private float _dragValue;
        private OICharInfo.AnimeInfo _copied;
        private GUIStyle _label, _value, _header;

        private static ObjectCtrlInfo[] SelectedAnimated()
        {
            try
            {
                var sel = Studio.Studio.Instance.treeNodeCtrl.selectObjectCtrl.Where(v => v != null && (v.kind == 0 || v.kind == 1)).ToArray();
                if (sel.Length > 0) return sel;
            }
            catch { /* no selection yet */ }
            var c = AnimePanel.PanelCharacter;
            return c != null ? new ObjectCtrlInfo[] { c } : new ObjectCtrlInfo[0];
        }

        private static OCIChar[] Characters() => AnimePanel.Targets().ToArray();

        public void Draw(AnimationBrowserPlugin plugin)
        {
            if (_label == null)
            {
                _label = new GUIStyle(Theme.Skin.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft, richText = true, wordWrap = false };
                _label.normal.textColor = Theme.Col(Theme.MutedHex);
                _value = new GUIStyle(_label) { alignment = TextAnchor.MiddleRight };
                _value.normal.textColor = Theme.Col(Theme.TextHex);
                _header = new GUIStyle(_label) { fontSize = 11 };
                _header.normal.textColor = Theme.Col(Theme.BlueHex);
            }

            var e = Event.current;
            // A slider released: record the change as one undo step, as Studio does.
            if (_drag != Drag.None && (e.rawType == EventType.MouseUp || !Input.GetMouseButton(0))) Commit();

            GUILayout.BeginVertical(Theme.Skin.box);
            GUILayout.BeginHorizontal();
            var open = plugin.ControlsOpen.Value;
            if (GUILayout.Button(new GUIContent(open ? Styles2.Down : Styles2.Right), Styles2.Arrow, GUILayout.Width(18), GUILayout.Height(18)))
                plugin.ControlsOpen.Value = open = !open;
            var chars = Characters();
            var first = AnimePanel.PanelCharacter;
            if (first == null || !chars.Contains(first)) first = chars.FirstOrDefault();
            GUILayout.Label("Animation controls" + (first != null ? Theme.Hint($"   {Describe(chars)}") : Theme.Hint("   select a character")), _header, GUILayout.ExpandWidth(true));
            if (!open && first != null)
                GUILayout.Label($"speed {first.animeSpeed:0.00}  ·  {TimeText(first)}", _value, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();

            if (open)
            {
                var was = GUI.enabled;
                GUI.enabled = was && first != null;
                var h = first != null && first.isHAnime;

                GUILayout.BeginHorizontal();
                Slider("Speed", Drag.Speed, first != null ? first.animeSpeed : 1f, 0f, 3f, true);
                if (GUILayout.Button(new GUIContent("1×", "Normal speed"), GUILayout.Width(30))) SetNow(Drag.Speed, 1f);
                GUILayout.Space(12);
                Slider("Pattern", Drag.Pattern, first != null ? first.animePattern : 0.5f, 0f, 1f, true);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                Slider("Option 1", Drag.Option0, h ? first.animeOptionParam[0] : 0.5f, 0f, 1f, h);
                GUILayout.Space(46);
                Slider("Option 2", Drag.Option1, h ? first.animeOptionParam[1] : 0.5f, 0f, 1f, h);
                GUILayout.EndHorizontal();

                // Timeline: where the animation is now; drag to jump there (at speed 0 it stays on that frame).
                GUILayout.BeginHorizontal();
                GUILayout.Label("Time", _label, GUILayout.Width(52));
                var t = first != null ? Frac(first.charAnimeCtrl.normalizedTime) : 0f;
                var nt = GUILayout.HorizontalSlider(t, 0f, 1f, GUILayout.ExpandWidth(true));
                if (first != null && Mathf.Abs(nt - t) > 0.0005f && GUIUtility.hotControl != 0) Scrub(chars, nt);
                GUILayout.Label(first != null ? TimeText(first) : "-", _value, GUILayout.Width(120));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                var props = first != null && first.optionItemCtrl != null && first.optionItemCtrl.visible;
                var np = GUILayout.Toggle(props, new GUIContent(" Props", "Show the animation's own items (a chair, a toy...)"), GUILayout.ExpandWidth(false));
                if (first != null && np != props) foreach (var c in chars) if (c.optionItemCtrl != null) c.optionItemCtrl.visible = np;
                GUILayout.Space(10);
                var loop = first != null && first.charAnimeCtrl.isForceLoop;
                var nl = GUILayout.Toggle(loop, new GUIContent(" Loop", "Loop animations that would otherwise play once and stop"), GUILayout.ExpandWidth(false));
                if (first != null && nl != loop) foreach (var c in chars) c.charAnimeCtrl.isForceLoop = nl;
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Restart", "Start the selected characters' (and items') animations over"), GUILayout.Width(64))) Restart(SelectedAnimated());
                if (GUILayout.Button(new GUIContent("Restart all", "Start every animation in the scene over, in step"), GUILayout.Width(76)))
                    Restart(Studio.Studio.Instance.dicObjectCtrl.Values.Where(v => v.kind == 0 || v.kind == 1).ToArray());
                GUILayout.Space(8);
                if (GUILayout.Button(new GUIContent("Copy", "Remember this character's animation"), GUILayout.Width(48)) && first != null)
                {
                    _copied = new OICharInfo.AnimeInfo();
                    _copied.Copy(first.oiCharInfo.animeInfo);
                }
                GUI.enabled = was && first != null && _copied != null && _copied.exist;
                if (GUILayout.Button(new GUIContent("Paste", "Play the copied animation on the selected characters"), GUILayout.Width(48)))
                    using (AnimationTable.Unfiltered())
                        foreach (var c in chars) c.LoadAnime(_copied.group, _copied.category, _copied.no);
                GUILayout.EndHorizontal();
                GUI.enabled = was;
                RefreshStudioBox();
            }
            GUILayout.EndVertical();
        }

        private static string Describe(OCIChar[] chars) => chars.Length == 1 ? Game.StudioTargets.NameOf(chars[0]) : $"{chars.Length} characters";

        private static float Frac(float t) => t - Mathf.Floor(t);

        private static string TimeText(OCIChar c)
        {
            var t = Frac(c.charAnimeCtrl.normalizedTime);
            var m = AnimationLengths.Read(c.charAnimeCtrl.animator);
            if (m == null) return $"{t * 100f:0}%";
            if (m.IsPose) return "pose";
            var frame = Mathf.Min(m.Frames, Mathf.FloorToInt(t * m.Frames) + 1);
            return $"frame {frame} / {m.Frames}";
        }

        private void Slider(string label, Drag which, float value, float min, float max, bool enabled)
        {
            var was = GUI.enabled;
            GUI.enabled = was && enabled;
            GUILayout.Label(label, _label, GUILayout.Width(52));
            var shown = _drag == which ? _dragValue : value;
            var nv = GUILayout.HorizontalSlider(shown, min, max, GUILayout.ExpandWidth(true));
            GUILayout.Label(enabled ? shown.ToString("0.00") : "-", _value, GUILayout.Width(34));
            GUI.enabled = was;
            if (enabled && Mathf.Abs(nv - shown) > 0.0001f)
            {
                if (_drag != which)
                {
                    Commit();
                    Begin(which);
                }
                _dragValue = nv;
                Apply(which, nv);
            }
        }

        private void SetNow(Drag which, float value)
        {
            Commit();
            Begin(which);
            _dragValue = value;
            Apply(which, value);
            Commit();
        }

        private void Begin(Drag which)
        {
            var targets = which == Drag.Speed ? SelectedAnimated() : Characters();
            _drag = which;
            _dragKeys = targets.Select(v => v.objectInfo.dicKey).ToArray();
            _dragOld = targets.Select(v => Get(v, which)).ToArray();
        }

        private static float Get(ObjectCtrlInfo v, Drag which)
        {
            var c = v as OCIChar;
            switch (which)
            {
                case Drag.Speed: return v.animeSpeed;
                case Drag.Pattern: return c != null ? c.animePattern : 0f;
                case Drag.Option0: return c != null ? c.animeOptionParam[0] : 0f;
                default: return c != null ? c.animeOptionParam[1] : 0f;
            }
        }

        private void Apply(Drag which, float value)
        {
            foreach (var key in _dragKeys)
            {
                if (!Studio.Studio.Instance.dicObjectCtrl.TryGetValue(key, out var v)) continue;
                var c = v as OCIChar;
                switch (which)
                {
                    case Drag.Speed: v.animeSpeed = value; break;
                    case Drag.Pattern: if (c != null) c.animePattern = value; break;
                    case Drag.Option0: if (c != null) c.animeOptionParam1 = value; break;
                    case Drag.Option1: if (c != null) c.animeOptionParam2 = value; break;
                }
            }
        }

        /// <summary>One undo step for the slider just released (Studio's own commands, so Ctrl+Z works as usual).</summary>
        private void Commit()
        {
            if (_drag == Drag.None) return;
            try
            {
                if (_dragKeys != null && _dragKeys.Length > 0)
                {
                    switch (_drag)
                    {
                        case Drag.Speed: UndoRedoManager.Instance.Do(new AnimeCommand.SpeedCommand(_dragKeys, _dragValue, _dragOld)); break;
                        case Drag.Pattern: UndoRedoManager.Instance.Do(new AnimeCommand.PatternCommand(_dragKeys, _dragValue, _dragOld)); break;
                        case Drag.Option0: UndoRedoManager.Instance.Do(new AnimeCommand.OptionParamCommand(_dragKeys, _dragValue, _dragOld, 0)); break;
                        case Drag.Option1: UndoRedoManager.Instance.Do(new AnimeCommand.OptionParamCommand(_dragKeys, _dragValue, _dragOld, 1)); break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Source.LogDebug("Couldn't record the animation change for undo: " + ex.Message);
            }
            _drag = Drag.None;
            _dragKeys = null;
            _dragOld = null;
        }

        private static void Scrub(IEnumerable<OCIChar> chars, float t)
        {
            foreach (var c in chars)
            {
                try
                {
                    var a = c.charAnimeCtrl.animator;
                    var st = a.GetCurrentAnimatorStateInfo(0);
                    a.Play(st.shortNameHash, 0, t);
                }
                catch { /* no animator */ }
            }
        }

        private static void Restart(ObjectCtrlInfo[] objects)
        {
            foreach (var o in objects)
            {
                try
                {
                    if (o is OCIChar c) c.RestartAnime();
                    else if (o is OCIItem i) i.RestartAnime();
                }
                catch { /* gone */ }
            }
        }

        private float _nextRefresh;

        /// <summary>Keeps Studio's own box (hidden or not) showing the same values.</summary>
        private void RefreshStudioBox()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.5f;
            AnimePanel.RefreshStudioControls();
        }
    }

    /// <summary>Chevrons for the controls header (same look as the folder tree's).</summary>
    internal static class Styles2
    {
        private static GUIStyle _arrow;
        private static Texture2D _down, _right;

        public static Texture2D Down => _down ?? (_down = Chevron(true));
        public static Texture2D Right => _right ?? (_right = Chevron(false));

        public static GUIStyle Arrow => _arrow ?? (_arrow = new GUIStyle(Theme.Skin.label) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(1, 1, 1, 1) });

        private static Texture2D Chevron(bool down)
        {
            const int size = 16;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false) { hideFlags = HideFlags.DontSave };
            var col = Theme.Col(Theme.MutedHex);
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var px = x + 0.5f;
                var py = size - (y + 0.5f);
                float d;
                if (down) d = Mathf.Min(Seg(px, py, 4.5f, 6f, 8f, 10f), Seg(px, py, 8f, 10f, 11.5f, 6f));
                else d = Mathf.Min(Seg(px, py, 6f, 4.5f, 10f, 8f), Seg(px, py, 10f, 8f, 6f, 11.5f));
                var a = Mathf.Clamp01(1.4f - d);
                tex.SetPixel(x, y, new Color(col.r, col.g, col.b, a));
            }
            tex.Apply();
            return tex;
        }

        private static float Seg(float px, float py, float ax, float ay, float bx, float by)
        {
            var p = new Vector2(px - ax, py - ay);
            var ab = new Vector2(bx - ax, by - ay);
            var t = Mathf.Clamp01(Vector2.Dot(p, ab) / ab.sqrMagnitude);
            return (p - ab * t).magnitude;
        }
    }
}

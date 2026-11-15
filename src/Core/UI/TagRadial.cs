using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HS2StudioBrowser.Files;
using HS2StudioBrowser.Game;
using KKAPI.Utilities;
using UnityEngine;

namespace HS2StudioBrowser.UI
{
    /// <summary>
    /// The radial tag picker. A ring of groups (Color, Theme, ...) around the pointer; hovering a group fans
    /// its values out on an outer ring, and clicking a value turns it on or off. It stays open, so several
    /// values can be picked in one go; Esc, the center or a click outside closes it. In Tag mode it changes
    /// the selected cards' tags; in Filter mode it picks which tags the view shows.
    /// </summary>
    internal sealed class TagRadial
    {
        public enum Mode { Tag, Filter }

        private const float R0 = 34f;     // center disc
        private const float ChipH = 24f;
        private const float ValueW = 82f;
        // Ring radii, sized each frame so the group chips never overlap (more groups, wider rings).
        private float R1 = 96f;           // groups
        private float R2 = 178f;          // values, first ring
        private float R3 = 252f;          // values, second ring
        private float Half = 290f;        // window half-size
        private Vector2 _center;          // GUI space

        private readonly int _id;
        private Rect _rect;
        private Mode _mode;
        private List<string> _targets = new List<string>();
        private int _facet = -1;
        private bool _focus;

        private static GUIStyle _chip, _chipHover, _chipOn, _chipSome, _centerLabel, _small;
        private static readonly Dictionary<string, Texture2D> Dots = new Dictionary<string, Texture2D>();

        public TagRadial(int windowId) { _id = windowId; }

        public bool Open { get; private set; }
        public Mode Current => _mode;

        public void ShowTag(Vector2 screen, List<string> targets)
        {
            _mode = Mode.Tag;
            _targets = targets ?? new List<string>();
            Place(screen);
        }

        public void ShowFilter(Vector2 screen)
        {
            _mode = Mode.Filter;
            _targets = new List<string>();
            Place(screen);
        }

        public void Close()
        {
            Open = false;
            InputGuard.Set(this, false);
        }

        private void Place(Vector2 screen)
        {
            _center = GUIUtility.ScreenToGUIPoint(screen);
            _facet = -1;
            _focus = true;
            Open = true;
        }

        /// <summary>Sizes the rings for these groups and keeps the whole wheel on screen.</summary>
        private void Layout(IList<TagFacet> facets)
        {
            EnsureStyles();
            var total = 0f;
            foreach (var f in facets) total += Mathf.Max(70f, _chip.CalcSize(new GUIContent(f.Name)).x + 26) + 10f;
            R1 = Mathf.Max(96f, total / (2f * Mathf.PI));
            R2 = R1 + 82f;
            R3 = R2 + 74f;
            Half = R3 + ChipH + 14f;
            var w = Screen.width / Mathf.Max(0.01f, GUI.matrix.m00);
            var h = Screen.height / Mathf.Max(0.01f, GUI.matrix.m11);
            var cx = Mathf.Clamp(_center.x, Half, Mathf.Max(Half, w - Half));
            var cy = Mathf.Clamp(_center.y, Half, Mathf.Max(Half, h - Half));
            _rect = new Rect(cx - Half, cy - Half, Half * 2, Half * 2);
        }

        /// <summary>
        /// Draw after the browser window. <paramref name="changed"/> runs after any change (tags or filter).
        /// </summary>
        /// <param name="autoFacets">Extra groups offered in Filter mode only (worked out by the kind, not set by hand).</param>
        /// <param name="countOf">How many carry a tag (Filter mode); defaults to the store's count.</param>
        public void Draw(TagSet set, TagStore store, HashSet<string> filter, Action changed, IList<TagFacet> autoFacets = null, Func<string, int> countOf = null)
        {
            if (!Open) return;
            var e = Event.current;
            Layout(_mode == Mode.Filter && autoFacets != null && autoFacets.Count > 0 ? set.Facets.Concat(autoFacets).ToList() : set.Facets);
            if (e.type == EventType.MouseDown && !InRing(e.mousePosition - _rect.position))
            {
                // A click outside closes it (and still goes to whatever is underneath).
                Close();
                return;
            }
            if (e.type == EventType.Repaint) InputGuard.Set(this, true);
            EnsureStyles();
            set.Refresh();
            GUI.Window(_id, _rect, id => DrawRadial(set, store, filter, changed, autoFacets, countOf), GUIContent.none, GUIStyle.none);
            GUI.BringWindowToFront(_id);
            if (_focus)
            {
                GUI.FocusWindow(_id);
                _focus = false;
            }
        }

        private bool InRing(Vector2 local)
        {
            var d = (local - new Vector2(Half, Half)).magnitude;
            return d <= R3 + ChipH + 10f;
        }

        private void DrawRadial(TagSet set, TagStore store, HashSet<string> filter, Action changed, IList<TagFacet> autoFacets, Func<string, int> countOf)
        {
            var e = Event.current;
            var c = new Vector2(Half, Half);
            var facets = _mode == Mode.Filter && autoFacets != null && autoFacets.Count > 0 ? set.Facets.Concat(autoFacets).ToList() : set.Facets;

            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.T))
            {
                Close();
                e.Use();
                return;
            }

            // Faint backdrop behind the rings so the chips read over any scene.
            if (e.type == EventType.Repaint)
            {
                var col = GUI.color;
                GUI.color = new Color(1, 1, 1, 0.55f);
                var r = (_facet >= 0 ? R3 : R1) + ChipH;
                GUI.DrawTexture(new Rect(c.x - r, c.y - r, r * 2, r * 2), Disc(Theme.BgHex), ScaleMode.StretchToFill, true);
                GUI.color = col;
            }

            // Center: what it acts on; click to close.
            var center = new Rect(c.x - R0, c.y - R0, R0 * 2, R0 * 2);
            if (e.type == EventType.Repaint)
            {
                GUI.DrawTexture(center, Disc(center.Contains(e.mousePosition) ? Theme.RaisedHex : Theme.PanelHex), ScaleMode.StretchToFill, true);
                var label = _mode == Mode.Filter ? $"Filter\n<size=10><color={Theme.MutedHex}>{filter.Count} on</color></size>"
                    : $"Tags\n<size=10><color={Theme.MutedHex}>{_targets.Count} selected</color></size>";
                _centerLabel.Draw(center, label, false, false, false, false);
            }
            if (e.type == EventType.MouseDown && center.Contains(e.mousePosition))
            {
                Close();
                e.Use();
                return;
            }

            if (facets.Count == 0)
            {
                _small.Draw(new Rect(c.x - 150, c.y + R0 + 8, 300, 40), "The tag list is empty.\nEdit it to add groups.", false, false, false, false);
            }

            // Groups ring.
            for (var i = 0; i < facets.Count; i++)
            {
                var f = facets[i];
                var a = Angle(i, facets.Count);
                var on = _mode == Mode.Filter ? filter.Count(t => t.StartsWith(f.Name + ":", StringComparison.OrdinalIgnoreCase)) : TargetCountIn(store, f);
                var text = f.Name + (on > 0 ? $"  <color={Theme.NeonHex}>{on}</color>" : "");
                var w = Mathf.Max(70f, _chip.CalcSize(new GUIContent(f.Name)).x + (on > 0 ? 26 : 12));
                var rect = ChipRect(c, a, R1, w);
                var hover = rect.Contains(e.mousePosition);
                if (hover && e.type == EventType.MouseMove || hover && e.type == EventType.Repaint) _facet = i;
                if (e.type == EventType.Repaint)
                    (i == _facet ? _chipOn : hover ? _chipHover : _chip).Draw(rect, text, hover, false, false, false);
                if (hover && e.type == EventType.MouseDown)
                {
                    if (e.button == 1)
                    {
                        // Right-click a group: clear it.
                        if (_mode == Mode.Filter) filter.RemoveWhere(t => t.StartsWith(f.Name + ":", StringComparison.OrdinalIgnoreCase));
                        else store.ClearFacet(_targets, f);
                        changed?.Invoke();
                    }
                    _facet = i;
                    e.Use();
                }
            }

            // Values of the hovered group, fanned out around its direction.
            if (_facet >= 0 && _facet < facets.Count)
            {
                var f = facets[_facet];
                var baseA = Angle(_facet, facets.Count);
                // Chips as wide as this group's longest value ("Reverse cowgirl"), within reason.
                var valueW = ValueW;
                foreach (var v in f.Values) valueW = Mathf.Max(valueW, _chip.CalcSize(new GUIContent(v)).x + (ColorOf(v).HasValue ? 34 : 18));
                valueW = Mathf.Min(valueW, 140f);
                var cap2 = Mathf.Max(1, Mathf.FloorToInt(2 * Mathf.PI * R2 / (valueW + 6)));
                var cap3 = Mathf.Max(1, Mathf.FloorToInt(2 * Mathf.PI * R3 / (valueW + 6)));
                var n2 = Mathf.Min(f.Values.Count, cap2);
                var n3 = Mathf.Min(f.Values.Count - n2, cap3);
                for (var i = 0; i < n2 + n3; i++)
                {
                    var ring = i < n2 ? R2 : R3;
                    var k = i < n2 ? i : i - n2;
                    var count = i < n2 ? n2 : n3;
                    var cap = i < n2 ? cap2 : cap3;
                    var step = 360f / cap;
                    var a = baseA + (k - (count - 1) / 2f) * step;
                    var value = f.Values[i];
                    var tag = f.Tag(value);
                    var rect = ChipRect(c, a, ring, valueW);
                    var hover = rect.Contains(e.mousePosition);

                    int has;
                    string extra = "";
                    if (_mode == Mode.Filter)
                    {
                        has = filter.Contains(tag) ? 2 : 0;
                        var n = countOf != null ? countOf(tag) : store.Count(tag);
                        if (n > 0) extra = $" <size=9><color={Theme.MutedHex}>{n}</color></size>";
                    }
                    else
                    {
                        var with = _targets.Count(t => store.Has(t, tag));
                        has = with == 0 ? 0 : with == _targets.Count ? 2 : 1;
                    }

                    if (e.type == EventType.Repaint)
                    {
                        var style = has == 2 ? _chipOn : has == 1 ? _chipSome : hover ? _chipHover : _chip;
                        var dot = ColorOf(value);
                        var label = (dot.HasValue ? "     " : "") + value + extra;
                        style.Draw(rect, label, hover, false, false, false);
                        if (dot.HasValue)
                            GUI.DrawTexture(new Rect(rect.x + 7, rect.y + (rect.height - 11) / 2, 11, 11), Dot(dot.Value), ScaleMode.StretchToFill, true);
                    }
                    if (hover && e.type == EventType.MouseDown && e.button == 0)
                    {
                        if (_mode == Mode.Filter)
                        {
                            if (!filter.Remove(tag)) filter.Add(tag);
                        }
                        else if (_targets.Count > 0)
                        {
                            store.Set(_targets, tag, has != 2, f);
                        }
                        changed?.Invoke();
                        e.Use();
                    }
                }
                if (f.Values.Count > n2 + n3)
                    _small.Draw(new Rect(c.x - 150, c.y + R3 + 20, 300, 18), $"{f.Values.Count - n2 - n3} more values don't fit; split the group in the tag list", false, false, false, false);
            }

            // Edit the lists (open the text files): this browser's, and the shared one.
            EditLink(new Rect(c.x - 40, c.y + R0 + 4, 80, 16), "edit list", set.FilePath, e);
            if (set.SharedPath != null) EditLink(new Rect(c.x - 40, c.y + R0 + 19, 80, 16), "edit shared", set.SharedPath, e);

            if (e.type == EventType.MouseDown && InRing(e.mousePosition)) e.Use();
            IMGUIUtils.EatInputInRect(new Rect(0, 0, _rect.width, _rect.height));
        }

        private void EditLink(Rect r, string text, string file, Event e)
        {
            if (e.type == EventType.Repaint) _small.Draw(r, r.Contains(e.mousePosition) ? $"<color={Theme.NeonHex}>{text}</color>" : text, false, false, false, false);
            if (e.type == EventType.MouseDown && r.Contains(e.mousePosition))
            {
                try { Process.Start(file); }
                catch (Exception ex) { Log.Source.LogWarning("Couldn't open the tag list: " + ex.Message); }
                e.Use();
            }
        }

        private int TargetCountIn(TagStore store, TagFacet f) =>
            _targets.Count(t => store.Get(t).Any(x => x.StartsWith(f.Name + ":", StringComparison.OrdinalIgnoreCase)));

        /// <summary>Angle in degrees, 0 = up, clockwise.</summary>
        private static float Angle(int i, int n) => n <= 0 ? 0 : i * 360f / n;

        private static Rect ChipRect(Vector2 c, float degrees, float radius, float width)
        {
            var rad = degrees * Mathf.Deg2Rad;
            var p = c + new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad)) * radius;
            return new Rect(p.x - width / 2, p.y - ChipH / 2, width, ChipH);
        }

        // ------------------------------------------------------------------ looks

        private static void EnsureStyles()
        {
            if (_chip != null) return;
            GUIStyle Make(string fill, string edge, string text)
            {
                var s = new GUIStyle(Theme.Skin.label)
                {
                    alignment = TextAnchor.MiddleCenter, richText = true, fontSize = 12, wordWrap = false, clipping = TextClipping.Clip,
                    padding = new RectOffset(6, 6, 2, 2), border = new RectOffset(11, 11, 11, 11),
                };
                s.normal.background = s.hover.background = Theme.Box(fill, edge, 11, 1f, 28);
                s.normal.textColor = s.hover.textColor = Theme.Col(text);
                return s;
            }
            _chip = Make(Theme.PanelHex, Theme.BorderHiHex, Theme.TextHex);
            _chipHover = Make(Theme.RaisedHex, Theme.NeonDimHex, Theme.TextHex);
            _chipOn = Make(Theme.NeonTintHex, Theme.NeonHex, Theme.NeonHex);
            _chipSome = Make(Theme.PanelHex, Theme.NeonDimHex, Theme.NeonHoverHex);
            _centerLabel = new GUIStyle(Theme.Skin.label) { alignment = TextAnchor.MiddleCenter, richText = true, fontSize = 12, wordWrap = true };
            _centerLabel.normal.textColor = Theme.Col(Theme.TextHex);
            _small = new GUIStyle(_centerLabel) { fontSize = 10 };
            _small.normal.textColor = Theme.Col(Theme.MutedHex);
        }

        private static Texture2D Disc(string hex) => Theme.Box(hex, Theme.BorderHex, 64, 1f, 128);

        public static Texture2D Dot(Color c)
        {
            var key = ColorUtility.ToHtmlStringRGBA(c);
            if (Dots.TryGetValue(key, out var t) && t != null) return t;
            const int size = 22;
            t = new Texture2D(size, size, TextureFormat.ARGB32, false) { hideFlags = HideFlags.DontSave };
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = new Vector2(x + 0.5f - size / 2f, y + 0.5f - size / 2f).magnitude;
                var cover = Mathf.Clamp01(size / 2f - d);
                var edge = Mathf.Clamp01(d - (size / 2f - 2.2f));
                var col = Color.Lerp(c, new Color(0.9f, 0.9f, 0.9f, 1f), edge * 0.6f);
                col.a = cover;
                t.SetPixel(x, y, col);
            }
            t.Apply();
            Dots[key] = t;
            return t;
        }

        private static readonly Dictionary<string, string> Named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Black", "#111111" }, { "White", "#F4F4F4" }, { "Gray", "#8C8C8C" }, { "Grey", "#8C8C8C" }, { "Silver", "#C9CED6" },
            { "Red", "#E0383E" }, { "Pink", "#F58FC0" }, { "Orange", "#F28C28" }, { "Yellow", "#F5D547" }, { "Gold", "#D4AF37" },
            { "Green", "#3FAE5A" }, { "Blue", "#3D7BE0" }, { "Navy", "#1F2F6B" }, { "Purple", "#8E5BD6" }, { "Brown", "#7A5232" },
            { "Beige", "#D9C3A0" }, { "Blonde", "#E8CF7A" }, { "Teal", "#2BA6A0" }, { "Cyan", "#4FE3FF" },
        };

        /// <summary>A swatch for values that name a color ("Black", "Pink"), else null.</summary>
        public static Color? ColorOf(string value)
        {
            if (value != null && Named.TryGetValue(value.Trim(), out var hex) && ColorUtility.TryParseHtmlString(hex, out var c)) return c;
            return null;
        }
    }
}

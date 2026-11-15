using System.Collections.Generic;
using UnityEngine;

namespace HS2StudioBrowser.UI
{
    /// <summary>
    /// Outfit Browser's look (same as Camera Sweep), matched to VN Deck: near-black panels with thin grey borders, neon mint for
    /// borders that matter (the window, an open character, selected choices) and the main button, and blue
    /// for titles and the fold-out headers. Everything is drawn from a skin built here, so the window looks
    /// the same whatever skin Studio or other plugins leave active. Change the hex values to retheme.
    /// </summary>
    public static class Theme
    {
        // VN Deck palette.
        public const string BgHex = "#0F1115";        // window
        public const string FieldHex = "#0B0D10";     // text fields
        public const string PanelHex = "#171A21";     // cards, buttons
        public const string RaisedHex = "#1E222B";    // hover
        public const string BorderHex = "#262A33";    // thin borders
        public const string BorderHiHex = "#3A3F4A";  // hovered borders, checkbox edges
        public const string TextHex = "#E6E8EE";
        public const string MutedHex = "#8B91A1";
        public const string NeonHex = "#6EE7B7";      // neon mint: borders, selected, main button
        public const string NeonHoverHex = "#94F0CC";
        public const string NeonDimHex = "#4B957B";
        public const string NeonTintHex = "#1F3A36";  // fill behind a selected choice
        public const string BlueHex = "#58A6FF";      // titles and fold-out headers
        public const string BlueHoverHex = "#82BDFF";
        public const string WarnHex = "#FBBF24";
        public const string ErrorHex = "#F87171";

        // Older names, still used around the window.
        public const string HintHex = MutedHex;
        public const string TokenHex = "#4FE3FF";     // file-name tokens (VN Deck logo cyan)
        public const string ExampleHex = "#FF6FD8";   // file-name examples (VN Deck logo pink)

        public static string Hint(string text) => $"<color={MutedHex}>{text}</color>";
        public static string Warn(string text) => $"<color={WarnHex}>{text}</color>";
        public static string Error(string text) => $"<color={ErrorHex}>{text}</color>";
        public static string Title(string text) => $"<color={BlueHex}><b>{text}</b></color>";

        private static GUISkin _skin;
        private static GUIStyle _rich, _richSingle, _richToggle, _section, _choice, _primary, _progress, _panel, _card, _cardActive;

        /// <summary>The skin the whole window draws with. Set it around all of Outfit Browser's drawing.</summary>
        public static GUISkin Skin
        {
            get
            {
                if (_skin != null) return _skin;
                _skin = Object.Instantiate(GUI.skin);
                _skin.name = "OutfitBrowser";
                _skin.hideFlags = HideFlags.DontSave;
                Build(_skin);
                return _skin;
            }
        }

        public static GUIStyle Rich => _rich ?? (_rich = new GUIStyle(Skin.label) { richText = true, wordWrap = true });

        public static GUIStyle RichSingle => _richSingle ?? (_richSingle = new GUIStyle(Skin.label)
        {
            richText = true, wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft,
        });

        public static GUIStyle RichToggle => _richToggle ?? (_richToggle = new GUIStyle(Skin.toggle) { richText = true, wordWrap = true });

        /// <summary>Fold-out headers: blue title on a panel, neon edge on hover.</summary>
        public static GUIStyle Section
        {
            get
            {
                if (_section != null) return _section;
                _section = new GUIStyle(Skin.button)
                {
                    richText = true, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip,
                    padding = new RectOffset(8, 8, 5, 5),
                };
                Set(_section, Box(PanelHex, BorderHex), BlueHex);
                SetHover(_section, Box(RaisedHex, NeonDimHex), BlueHoverHex);
                SetActive(_section, Box(BorderHex, NeonHex), BlueHoverHex);
                return _section;
            }
        }

        /// <summary>Toolbars and toggle buttons: the selected one gets a neon border and neon text.</summary>
        public static GUIStyle Choice
        {
            get
            {
                if (_choice != null) return _choice;
                _choice = new GUIStyle(Skin.button) { richText = true, clipping = TextClipping.Clip };
                _choice.onNormal.background = _choice.onFocused.background = Box(NeonTintHex, NeonHex);
                _choice.onHover.background = Box(NeonTintHex, NeonHoverHex);
                _choice.onActive.background = Box(PanelHex, NeonHex);
                _choice.onNormal.textColor = _choice.onFocused.textColor = Col(NeonHex);
                _choice.onHover.textColor = _choice.onActive.textColor = Col(NeonHoverHex);
                return _choice;
            }
        }

        /// <summary>The main action (Capture / Record): solid neon, dark text, like VN Deck's "This one".</summary>
        public static GUIStyle Primary
        {
            get
            {
                if (_primary != null) return _primary;
                _primary = new GUIStyle(Skin.button) { richText = true, fontStyle = FontStyle.Bold };
                Set(_primary, Box(NeonHex, NeonHex), BgHex);
                SetHover(_primary, Box(NeonHoverHex, NeonHoverHex), BgHex);
                SetActive(_primary, Box(NeonDimHex, NeonDimHex), BgHex);
                return _primary;
            }
        }

        public static GUIStyle Progress
        {
            get
            {
                if (_progress != null) return _progress;
                _progress = new GUIStyle(Skin.box);
                _progress.normal.background = Box(NeonHex, NeonHex, 3);
                _progress.border = new RectOffset(4, 4, 4, 4);
                return _progress;
            }
        }

        /// <summary>A dark box for reference text (the file-name tokens).</summary>
        public static GUIStyle Panel
        {
            get
            {
                if (_panel != null) return _panel;
                _panel = new GUIStyle(Skin.label) { richText = true, wordWrap = true, padding = new RectOffset(8, 8, 6, 6) };
                _panel.normal.background = Box(FieldHex, BorderHex);
                _panel.normal.textColor = Col(TextHex);
                _panel.border = new RectOffset(6, 6, 6, 6);
                return _panel;
            }
        }

        /// <summary>A character's card; neon-bordered while it's open, like VN Deck's active card.</summary>
        public static GUIStyle Card(bool active)
        {
            if (_card == null)
            {
                _card = new GUIStyle(Skin.box) { padding = new RectOffset(6, 6, 6, 6), margin = new RectOffset(0, 0, 4, 4) };
                _card.normal.background = Box(PanelHex, BorderHex, 6);
                _card.border = new RectOffset(8, 8, 8, 8);
                _cardActive = new GUIStyle(_card);
                _cardActive.normal.background = Box(PanelHex, NeonHex, 6);
            }
            return active ? _cardActive : _card;
        }

        // ---------------------------------------------------------------- skin

        private static void Build(GUISkin s)
        {
            var text = Col(TextHex);

            // Window: dark, neon border, blue title.
            var w = s.window;
            w.normal.background = w.onNormal.background = Box(BgHex, NeonHex, 8, 1.5f);
            w.focused.background = w.onFocused.background = w.normal.background;
            w.hover.background = w.onHover.background = w.normal.background;
            w.normal.textColor = w.onNormal.textColor = w.focused.textColor = w.onFocused.textColor = Col(BlueHex);
            w.hover.textColor = w.onHover.textColor = Col(BlueHex);
            w.fontStyle = FontStyle.Bold;
            w.fontSize = 13;
            w.alignment = TextAnchor.UpperLeft;
            w.padding = new RectOffset(10, 10, 30, 10);
            w.contentOffset = new Vector2(2, -23);
            w.border = new RectOffset(10, 10, 10, 10);

            s.label.normal.textColor = text;
            s.label.hover.textColor = text;

            s.box.normal.background = Box(PanelHex, BorderHex, 6);
            s.box.normal.textColor = text;
            s.box.border = new RectOffset(8, 8, 8, 8);

            var b = s.button;
            Set(b, Box(PanelHex, BorderHex), TextHex);
            SetHover(b, Box(RaisedHex, BorderHiHex), TextHex);
            SetActive(b, Box(BorderHex, NeonHex), TextHex);
            b.onNormal.background = b.onFocused.background = Box(NeonTintHex, NeonHex);
            b.onHover.background = b.onActive.background = Box(NeonTintHex, NeonHoverHex);
            b.onNormal.textColor = b.onHover.textColor = b.onActive.textColor = b.onFocused.textColor = Col(NeonHex);
            b.border = new RectOffset(7, 7, 7, 7);

            foreach (var f in new[] { s.textField, s.textArea })
            {
                Set(f, Box(FieldHex, BorderHex), TextHex);
                f.hover.background = Box(FieldHex, BorderHiHex);
                f.hover.textColor = text;
                f.focused.background = f.onFocused.background = Box(FieldHex, BlueHex);
                f.focused.textColor = f.onFocused.textColor = text;
                f.border = new RectOffset(7, 7, 7, 7);
                f.padding = new RectOffset(5, 5, 3, 3);
            }
            s.settings.cursorColor = Col(NeonHex);
            var sel = Col(BlueHex);
            sel.a = 0.45f;
            s.settings.selectionColor = sel;

            // Checkbox: dark rounded square, neon when ticked.
            var t = s.toggle;
            t.normal.background = Check(false, BorderHiHex);
            t.hover.background = t.active.background = Check(false, NeonDimHex);
            t.onNormal.background = t.onFocused.background = Check(true, NeonHex);
            t.onHover.background = t.onActive.background = Check(true, NeonHoverHex);
            t.focused.background = t.normal.background;
            t.normal.textColor = t.hover.textColor = t.active.textColor = t.focused.textColor = text;
            t.onNormal.textColor = t.onHover.textColor = t.onActive.textColor = t.onFocused.textColor = text;
            t.border = new RectOffset(CheckSize, 0, CheckSize, 0); // draws the box once, at the top left
            t.padding = new RectOffset(CheckSize + 5, 0, 1, 2);
            t.overflow = new RectOffset(0, 0, -1, 0);
            t.fixedHeight = 0;

            // Slider: thin track, neon knob.
            var hs = s.horizontalSlider;
            hs.normal.background = Track();
            hs.border = new RectOffset(3, 3, 0, 0);
            hs.fixedHeight = 12;
            hs.margin = new RectOffset(4, 4, 6, 4);
            var th = s.horizontalSliderThumb;
            th.normal.background = Box(NeonHex, NeonHex, 6, 1, 12);
            th.hover.background = th.active.background = Box(NeonHoverHex, NeonHoverHex, 6, 1, 12);
            th.border = new RectOffset(0, 0, 0, 0);
            th.fixedWidth = 12;
            th.fixedHeight = 12;

            // Scrollbar: slim, no arrows.
            var vs = s.verticalScrollbar;
            vs.normal.background = null;
            vs.fixedWidth = 10;
            vs.border = new RectOffset(0, 0, 0, 0);
            vs.margin = new RectOffset(4, 0, 0, 0);
            var vt = s.verticalScrollbarThumb;
            vt.normal.background = Box(BorderHiHex, BorderHiHex, 3);
            vt.hover.background = vt.active.background = Box(NeonDimHex, NeonDimHex, 3);
            vt.border = new RectOffset(4, 4, 4, 4);
            vt.fixedWidth = 8;
            foreach (var a in new[] { s.verticalScrollbarUpButton, s.verticalScrollbarDownButton })
            {
                a.normal.background = a.hover.background = a.active.background = null;
                a.fixedHeight = 1;
                a.fixedWidth = 10;
            }
        }

        private static void Set(GUIStyle st, Texture2D bg, string textHex)
        {
            st.normal.background = st.focused.background = bg;
            st.normal.textColor = st.focused.textColor = Col(textHex);
        }

        private static void SetHover(GUIStyle st, Texture2D bg, string textHex)
        {
            st.hover.background = bg;
            st.hover.textColor = Col(textHex);
        }

        private static void SetActive(GUIStyle st, Texture2D bg, string textHex)
        {
            st.active.background = bg;
            st.active.textColor = Col(textHex);
        }

        // ---------------------------------------------------------------- textures

        private const int CheckSize = 16;
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        /// <summary>
        /// Rounded rectangle, filled, with an edge line, anti-aliased. Used 9-sliced, so the corners stay
        /// crisp at any size (style.border should be at least radius + 2).
        /// </summary>
        public static Texture2D Box(string fillHex, string edgeHex, int radius = 5, float edgeWidth = 1f, int size = 0)
        {
            if (size <= 0) size = radius * 2 + 6;
            var key = $"box{fillHex}{edgeHex}{radius}{edgeWidth}{size}";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var fill = Col(fillHex);
            var edge = Col(edgeHex);
            var tex = NewTex(size, size);
            var half = size / 2f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = RoundRectDistance(x + 0.5f - half, y + 0.5f - half, half, half, radius);
                var cover = Mathf.Clamp01(0.5f - d);
                var edgeMix = Mathf.Clamp01(d + edgeWidth + 0.5f);
                var c = Color.Lerp(fill, edge, edgeMix);
                c.a *= cover;
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            Cache[key] = tex;
            return tex;
        }

        /// <summary>Checkbox: rounded square; ticked = neon fill with a dark check mark.</summary>
        private static Texture2D Check(bool on, string edgeHex)
        {
            var key = $"check{on}{edgeHex}";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            const int box = 14; // inside a CheckSize texture, top left
            const float half = box / 2f;
            var tex = NewTex(CheckSize, CheckSize);
            var fill = on ? Col(edgeHex) : Col(FieldHex);
            var edge = Col(edgeHex);
            var mark = Col(BgHex);
            for (var y = 0; y < CheckSize; y++)
            for (var x = 0; x < CheckSize; x++)
            {
                // Texture rows run bottom-up; centre the box 1px in from the top-left corner.
                var px = x + 0.5f - (half + 1f);
                var py = y + 0.5f - (CheckSize - half - 1f);
                var d = RoundRectDistance(px, py, half, half, 3f);
                var cover = Mathf.Clamp01(0.5f - d);
                var c = Color.Lerp(fill, edge, Mathf.Clamp01(d + 1.5f));
                if (on)
                {
                    var m = Mathf.Min(Segment(px, py, -3.5f, 0f, -1f, -2.5f), Segment(px, py, -1f, -2.5f, 3.5f, 2.5f));
                    c = Color.Lerp(c, mark, Mathf.Clamp01(1.7f - m));
                }
                c.a *= cover;
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            Cache[key] = tex;
            return tex;
        }

        /// <summary>Slider track: a 4px rounded bar across the middle of a 12px-high texture.</summary>
        private static Texture2D Track()
        {
            const string key = "track";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            const int w = 8, h = 12;
            var tex = NewTex(w, h);
            var c = Col(BorderHiHex);
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var d = RoundRectDistance(x + 0.5f - w / 2f, y + 0.5f - h / 2f, w / 2f, 2f, 2f);
                var p = c;
                p.a *= Mathf.Clamp01(0.5f - d);
                tex.SetPixel(x, y, p);
            }
            tex.Apply();
            Cache[key] = tex;
            return tex;
        }

        private static Texture2D NewTex(int w, int h) =>
            new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave,
            };

        /// <summary>Signed distance to a rounded rectangle centred on 0 (negative inside).</summary>
        private static float RoundRectDistance(float x, float y, float halfW, float halfH, float r)
        {
            var qx = Mathf.Abs(x) - (halfW - r);
            var qy = Mathf.Abs(y) - (halfH - r);
            var outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        private static float Segment(float px, float py, float ax, float ay, float bx, float by)
        {
            var p = new Vector2(px - ax, py - ay);
            var ab = new Vector2(bx - ax, by - ay);
            var t = Mathf.Clamp01(Vector2.Dot(p, ab) / ab.sqrMagnitude);
            return (p - ab * t).magnitude;
        }

        public static Color Col(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.gray;
    }
}

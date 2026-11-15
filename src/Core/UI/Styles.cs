using UnityEngine;

namespace HS2StudioBrowser.UI
{
    /// <summary>Browser-only styles on top of the shared Theme.</summary>
    internal static class Styles
    {
        private static GUIStyle _treeRow, _treeRowOn, _treeArrow, _cardName, _cardFile, _menu, _menuItem, _menuHeader,
            _ghost, _cell, _cellOn, _cellDrop, _placeholder, _status, _dialog, _small;

        private static Texture2D _white;

        public static Texture2D White
        {
            get
            {
                if (_white != null) return _white;
                _white = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
                return _white;
            }
        }

        private static Texture2D _starOn, _starOff, _starHover;

        /// <summary>Filled gold star: a favorite.</summary>
        public static Texture2D StarOn => _starOn ?? (_starOn = Star(Theme.Col(Theme.WarnHex), Theme.Col("#3B2A05"), true));

        /// <summary>Hollow star shown on hover over a card that isn't a favorite.</summary>
        public static Texture2D StarOff => _starOff ?? (_starOff = Star(new Color(1f, 1f, 1f, 0.85f), new Color(0f, 0f, 0f, 0.55f), false));

        /// <summary>Hollow gold star, under the pointer.</summary>
        public static Texture2D StarHover => _starHover ?? (_starHover = Star(Theme.Col(Theme.WarnHex), new Color(0f, 0f, 0f, 0.55f), false));

        /// <summary>A five-point star drawn with a dark outline so it reads on any thumbnail.</summary>
        private static Texture2D Star(Color fill, Color edge, bool filled)
        {
            const int size = 40;
            var pts = new Vector2[10];
            for (var i = 0; i < 10; i++)
            {
                var r = i % 2 == 0 ? 17f : 7.2f;
                var a = Mathf.PI / 2f + i * Mathf.PI / 5f;
                // Texture rows run bottom-up, so +sin puts the first point at the top.
                pts[i] = new Vector2(size / 2f + Mathf.Cos(a) * r, size / 2f - 1.5f + Mathf.Sin(a) * r);
            }

            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var d = float.MaxValue;
                for (var i = 0; i < 10; i++) d = Mathf.Min(d, SegDist(p, pts[i], pts[(i + 1) % 10]));
                var inside = Inside(p, pts);
                var sd = inside ? -d : d; // signed distance to the star's edge

                Color c;
                if (filled)
                {
                    // fill inside, dark rim 2px wide straddling the edge
                    var fillA = Mathf.Clamp01(0.5f - (sd + 1.2f));
                    var rimA = Mathf.Clamp01(0.5f - (sd - 1.3f));
                    c = Color.Lerp(new Color(edge.r, edge.g, edge.b, rimA * edge.a), fill, fillA);
                    c.a = Mathf.Max(rimA * edge.a, fillA);
                }
                else
                {
                    // light ring 1.8px on the edge, dark halo either side
                    var ring = Mathf.Clamp01(1.4f - Mathf.Abs(sd));
                    var halo = Mathf.Clamp01(2.8f - Mathf.Abs(sd));
                    c = Color.Lerp(new Color(edge.r, edge.g, edge.b, halo * edge.a), fill, ring);
                    c.a = Mathf.Max(halo * edge.a, ring * fill.a);
                }
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            return tex;
        }

        private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
        }

        private static bool Inside(Vector2 p, Vector2[] poly)
        {
            var inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            }
            return inside;
        }

        private static Texture2D _chevRight, _chevDown, _chevRightHi, _chevDownHi, _badge, _panelLeft, _panelRight;

        /// <summary>Folder disclosure chevrons: right = closed, down = open; Hi = under the pointer.</summary>
        public static Texture2D ChevronRight => _chevRight ?? (_chevRight = Chevron(0, Theme.Col(Theme.MutedHex)));
        public static Texture2D ChevronDown => _chevDown ?? (_chevDown = Chevron(1, Theme.Col(Theme.MutedHex)));
        public static Texture2D ChevronRightHi => _chevRightHi ?? (_chevRightHi = Chevron(0, Theme.Col(Theme.NeonHex)));
        public static Texture2D ChevronDownHi => _chevDownHi ?? (_chevDownHi = Chevron(1, Theme.Col(Theme.NeonHex)));

        /// <summary>Double chevrons for folding the folder panel away (left) and back (right).</summary>
        public static Texture2D PanelLeft => _panelLeft ?? (_panelLeft = Chevron(2, Theme.Col(Theme.MutedHex)));
        public static Texture2D PanelRight => _panelRight ?? (_panelRight = Chevron(3, Theme.Col(Theme.MutedHex)));

        /// <summary>Red circle with a white "!": the outfit needs zipmods you don't have.</summary>
        public static Texture2D MissingBadge => _badge ?? (_badge = Badge());

        // kind: 0 right, 1 down, 2 double left, 3 double right
        private static Texture2D Chevron(int kind, Color color)
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                // top-down coordinates
                var p = new Vector2(x + 0.5f, size - (y + 0.5f));
                float d;
                switch (kind)
                {
                    case 0: d = Mathf.Min(SegDist(p, new Vector2(12, 8), new Vector2(20, 16)), SegDist(p, new Vector2(20, 16), new Vector2(12, 24))); break;
                    case 1: d = Mathf.Min(SegDist(p, new Vector2(8, 12), new Vector2(16, 20)), SegDist(p, new Vector2(16, 20), new Vector2(24, 12))); break;
                    case 2:
                        d = Mathf.Min(Mathf.Min(SegDist(p, new Vector2(15, 8), new Vector2(8, 16)), SegDist(p, new Vector2(8, 16), new Vector2(15, 24))),
                            Mathf.Min(SegDist(p, new Vector2(24, 8), new Vector2(17, 16)), SegDist(p, new Vector2(17, 16), new Vector2(24, 24))));
                        break;
                    default:
                        d = Mathf.Min(Mathf.Min(SegDist(p, new Vector2(8, 8), new Vector2(15, 16)), SegDist(p, new Vector2(15, 16), new Vector2(8, 24))),
                            Mathf.Min(SegDist(p, new Vector2(17, 8), new Vector2(24, 16)), SegDist(p, new Vector2(24, 16), new Vector2(17, 24))));
                        break;
                }
                var a = Mathf.Clamp01(2.6f - d);
                tex.SetPixel(x, y, new Color(color.r, color.g, color.b, a * color.a));
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D Badge()
        {
            const int size = 40;
            var red = Theme.Col(Theme.ErrorHex);
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            var c0 = new Vector2(size / 2f, size / 2f);
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f, size - (y + 0.5f));
                var r = (p - c0).magnitude;
                var disc = Mathf.Clamp01(17.5f - r);          // red disc
                var rim = Mathf.Clamp01(19.5f - r);           // dark rim around it
                var bar = SegDist(p, new Vector2(20, 9.5f), new Vector2(20, 23f));
                var dot = (p - new Vector2(20, 30f)).magnitude;
                var mark = Mathf.Clamp01(3.2f - Mathf.Min(bar, dot));
                var col = Color.Lerp(new Color(0.12f, 0.02f, 0.02f, 1f), red, disc);
                col = Color.Lerp(col, Color.white, mark * disc);
                col.a = Mathf.Max(rim * 0.85f, disc);
                tex.SetPixel(x, y, col);
            }
            tex.Apply();
            return tex;
        }

        private static GUIStyle _sectionHeader;

        /// <summary>"Favorites" / "Other outfits" labels across the grid.</summary>
        public static GUIStyle SectionHeader => _sectionHeader ?? (_sectionHeader = Make(() =>
        {
            var st = new GUIStyle(Theme.Skin.label) { fontSize = 11, alignment = TextAnchor.LowerLeft, richText = true, padding = new RectOffset(2, 2, 0, 2) };
            st.normal.textColor = Theme.Col(Theme.BlueHex);
            return st;
        }));

        public static GUIStyle TreeRow => _treeRow ?? (_treeRow = Make(() =>
        {
            var s = new GUIStyle(Theme.Skin.label)
            {
                alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip,
                padding = new RectOffset(4, 4, 2, 2), margin = new RectOffset(0, 0, 0, 0), richText = true,
            };
            s.hover.background = Theme.Box(Theme.RaisedHex, Theme.RaisedHex, 3);
            s.hover.textColor = Theme.Col(Theme.TextHex);
            s.border = new RectOffset(4, 4, 4, 4);
            return s;
        }));

        public static GUIStyle TreeRowOn => _treeRowOn ?? (_treeRowOn = Make(() =>
        {
            var s = new GUIStyle(TreeRow);
            s.normal.background = s.hover.background = Theme.Box(Theme.NeonTintHex, Theme.NeonHex, 3);
            s.normal.textColor = s.hover.textColor = Theme.Col(Theme.NeonHex);
            return s;
        }));

        public static GUIStyle TreeArrow => _treeArrow ?? (_treeArrow = Make(() =>
        {
            var s = new GUIStyle(Theme.Skin.label) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0) };
            s.normal.textColor = Theme.Col(Theme.MutedHex);
            s.hover.textColor = Theme.Col(Theme.NeonHex);
            return s;
        }));

        public static GUIStyle CardName => _cardName ?? (_cardName = Make(() =>
        {
            var s = new GUIStyle(Theme.Skin.label) { alignment = TextAnchor.UpperCenter, wordWrap = true, clipping = TextClipping.Clip, fontSize = 12, padding = new RectOffset(2, 2, 0, 0) };
            s.normal.textColor = Theme.Col(Theme.TextHex);
            return s;
        }));

        public static GUIStyle CardFile => _cardFile ?? (_cardFile = Make(() =>
        {
            var s = new GUIStyle(CardName) { wordWrap = false, fontSize = 10 };
            s.normal.textColor = Theme.Col(Theme.MutedHex);
            return s;
        }));

        public static GUIStyle Cell => _cell ?? (_cell = Make(() =>
        {
            var s = new GUIStyle();
            s.normal.background = Theme.Box(Theme.PanelHex, Theme.BorderHex, 5);
            s.border = new RectOffset(7, 7, 7, 7);
            return s;
        }));

        public static GUIStyle CellOn => _cellOn ?? (_cellOn = Make(() =>
        {
            var s = new GUIStyle(Cell);
            s.normal.background = Theme.Box(Theme.NeonTintHex, Theme.NeonHex, 5, 2f);
            return s;
        }));

        public static GUIStyle CellHover => _cellDrop ?? (_cellDrop = Make(() =>
        {
            var s = new GUIStyle(Cell);
            s.normal.background = Theme.Box(Theme.RaisedHex, Theme.BorderHiHex, 5);
            return s;
        }));

        public static GUIStyle Placeholder => _placeholder ?? (_placeholder = Make(() =>
        {
            var s = new GUIStyle(Theme.Skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
            s.normal.textColor = Theme.Col(Theme.BorderHiHex);
            return s;
        }));

        public static GUIStyle Menu => _menu ?? (_menu = Make(() =>
        {
            var s = new GUIStyle(Theme.Skin.window) { padding = new RectOffset(4, 4, 4, 4), contentOffset = Vector2.zero };
            s.normal.background = s.onNormal.background = s.focused.background = s.onFocused.background =
                s.hover.background = s.onHover.background = Theme.Box(Theme.PanelHex, Theme.NeonDimHex, 5);
            s.border = new RectOffset(7, 7, 7, 7);
            return s;
        }));

        public static GUIStyle MenuItem => _menuItem ?? (_menuItem = Make(() =>
        {
            var s = new GUIStyle(TreeRow) { padding = new RectOffset(8, 8, 4, 4), fixedHeight = 0 };
            s.hover.background = Theme.Box(Theme.NeonTintHex, Theme.NeonTintHex, 3);
            s.hover.textColor = Theme.Col(Theme.NeonHoverHex);
            return s;
        }));

        public static GUIStyle MenuHeader => _menuHeader ?? (_menuHeader = Make(() =>
        {
            var s = new GUIStyle(Theme.Skin.label) { fontSize = 11, padding = new RectOffset(8, 8, 3, 3), wordWrap = false, clipping = TextClipping.Clip };
            s.normal.textColor = Theme.Col(Theme.BlueHex);
            return s;
        }));

        public static GUIStyle Ghost => _ghost ?? (_ghost = Make(() =>
        {
            var s = new GUIStyle(Theme.Skin.box) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(8, 8, 4, 4), wordWrap = false };
            s.normal.background = Theme.Box(Theme.NeonTintHex, Theme.NeonHex, 4);
            s.normal.textColor = Theme.Col(Theme.NeonHex);
            return s;
        }));

        public static GUIStyle Status => _status ?? (_status = Make(() =>
            new GUIStyle(Theme.RichSingle) { fontSize = 11 }));

        public static GUIStyle Small => _small ?? (_small = Make(() =>
        {
            var s = new GUIStyle(Theme.Skin.label) { fontSize = 11, wordWrap = false };
            s.normal.textColor = Theme.Col(Theme.MutedHex);
            return s;
        }));

        public static GUIStyle Dialog => _dialog ?? (_dialog = Make(() => new GUIStyle(Theme.Skin.window)));

        private static GUIStyle Make(System.Func<GUIStyle> f) => f();
    }
}

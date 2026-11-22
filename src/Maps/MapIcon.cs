using HS2StudioBrowser.UI;
using UnityEngine;

namespace HS2StudioBrowser.Maps
{
    /// <summary>A folded map with a location pin, drawn in code, with the browsers' neon tab.</summary>
    internal static class MapIcon
    {
        public static Texture2D Create()
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            var line = new Color(0.92f, 0.92f, 0.92f, 1f);
            var accent = Theme.Col(Theme.NeonHex);
            // Three panels of a folded map: zig-zag top and bottom edges.
            var t = new[] { new Vector2(4f, 8f), new Vector2(12f, 5f), new Vector2(20f, 8f), new Vector2(28f, 5f) };
            var b = new[] { new Vector2(4f, 25f), new Vector2(12f, 22f), new Vector2(20f, 25f), new Vector2(28f, 22f) };
            var pin = new Vector2(16f, 12.5f);
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f, size - (y + 0.5f));
                var d = float.MaxValue;
                for (var i = 0; i < 3; i++)
                {
                    d = Mathf.Min(d, Seg(p, t[i], t[i + 1]));
                    d = Mathf.Min(d, Seg(p, b[i], b[i + 1]));
                }
                for (var i = 0; i < 4; i++) d = Mathf.Min(d, Seg(p, t[i], b[i]));
                var cover = Mathf.Clamp01(1.4f - d);
                var c = cover > 0 ? new Color(line.r, line.g, line.b, cover * 0.85f) : new Color(0, 0, 0, 0);
                // Pin: a filled circle over a short point.
                var r = (p - pin).magnitude;
                var pinCover = Mathf.Clamp01(4.2f - r);
                if (p.y > pin.y && p.y < pin.y + 7.5f) pinCover = Mathf.Max(pinCover, Mathf.Clamp01(2.6f - Mathf.Abs(p.x - pin.x) - (p.y - pin.y) * 0.32f));
                if (pinCover > 0) c = Color.Lerp(c, accent, pinCover);
                if (r < 1.6f) c = new Color(0.08f, 0.1f, 0.14f, 1f);
                if (p.x >= 9f && p.x <= 23f && p.y >= 28f && p.y <= 30f) c = accent;
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            return tex;
        }

        private static float Seg(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - a - ab * t).magnitude;
        }
    }
}

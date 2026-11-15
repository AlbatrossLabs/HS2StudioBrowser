using HS2StudioBrowser.UI;
using UnityEngine;

namespace HS2StudioBrowser.Items
{
    /// <summary>A box (cube outline) drawn in code, with the browsers' neon tab.</summary>
    internal static class ItemIcon
    {
        public static Texture2D Create()
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            var line = new Color(0.92f, 0.92f, 0.92f, 1f);
            var accent = Theme.Col(Theme.NeonHex);
            // Cube: top face diamond, two side faces.
            var top = new Vector2(16f, 3.5f);
            var left = new Vector2(5.5f, 9f);
            var right = new Vector2(26.5f, 9f);
            var mid = new Vector2(16f, 14.5f);
            var down = new Vector2(0f, 10.5f);
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f, size - (y + 0.5f));
                var d = Seg(p, top, left);
                d = Mathf.Min(d, Seg(p, top, right));
                d = Mathf.Min(d, Seg(p, left, mid));
                d = Mathf.Min(d, Seg(p, right, mid));
                d = Mathf.Min(d, Seg(p, left, left + down));
                d = Mathf.Min(d, Seg(p, right, right + down));
                d = Mathf.Min(d, Seg(p, mid, mid + down));
                d = Mathf.Min(d, Seg(p, left + down, mid + down));
                d = Mathf.Min(d, Seg(p, right + down, mid + down));
                var cover = Mathf.Clamp01(1.5f - d);
                var c = cover > 0 ? new Color(line.r, line.g, line.b, cover) : new Color(0, 0, 0, 0);
                if (p.x >= 9f && p.x <= 23f && p.y >= 27f && p.y <= 29.5f) c = accent;
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

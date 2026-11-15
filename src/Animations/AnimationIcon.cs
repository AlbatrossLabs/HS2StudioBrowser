using HS2StudioBrowser.UI;
using UnityEngine;

namespace HS2StudioBrowser.Animations
{
    /// <summary>A play triangle in a circle with motion lines, drawn in code, with the browsers' neon tab.</summary>
    internal static class AnimationIcon
    {
        public static Texture2D Create()
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            var line = new Color(0.92f, 0.92f, 0.92f, 1f);
            var accent = Theme.Col(Theme.NeonHex);
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var px = x + 0.5f;
                var py = size - (y + 0.5f); // top-down
                var ring = Mathf.Abs(new Vector2(px - 18f, py - 14f).magnitude - 8.5f);
                var d = ring;
                // Play triangle
                d = Mathf.Min(d, Seg(px, py, 15.5f, 9.5f, 15.5f, 18.5f));
                d = Mathf.Min(d, Seg(px, py, 15.5f, 9.5f, 22.5f, 14f));
                d = Mathf.Min(d, Seg(px, py, 15.5f, 18.5f, 22.5f, 14f));
                // Motion lines on the left
                d = Mathf.Min(d, Seg(px, py, 3f, 10f, 7f, 10f));
                d = Mathf.Min(d, Seg(px, py, 2f, 14f, 7f, 14f));
                d = Mathf.Min(d, Seg(px, py, 3f, 18f, 7f, 18f));
                var cover = Mathf.Clamp01(1.5f - d);
                var c = cover > 0 ? new Color(line.r, line.g, line.b, cover) : new Color(0, 0, 0, 0);
                if (px >= 9f && px <= 23f && py >= 26.5f && py <= 29f) c = accent;
                tex.SetPixel(x, y, c);
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

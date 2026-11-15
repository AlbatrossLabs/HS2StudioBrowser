using HS2StudioBrowser.UI;
using UnityEngine;

namespace HS2StudioBrowser.Poses
{
    /// <summary>A figure with one arm raised, drawn in code, with the browsers' neon tab.</summary>
    internal static class PoseIcon
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
                var head = Mathf.Abs(new Vector2(px - 15f, py - 6.2f).magnitude - 2.4f);
                var d = head;
                d = Mathf.Min(d, Seg(px, py, 15f, 9.5f, 15.5f, 17f));     // body
                d = Mathf.Min(d, Seg(px, py, 15.2f, 11.5f, 21f, 7f));     // raised arm
                d = Mathf.Min(d, Seg(px, py, 21f, 7f, 23f, 3.5f));
                d = Mathf.Min(d, Seg(px, py, 15.2f, 11.5f, 10f, 15.5f));  // lowered arm
                d = Mathf.Min(d, Seg(px, py, 15.5f, 17f, 11.5f, 21f));    // bent leg
                d = Mathf.Min(d, Seg(px, py, 11.5f, 21f, 12.5f, 24.5f));
                d = Mathf.Min(d, Seg(px, py, 15.5f, 17f, 19.5f, 24.5f));  // straight leg
                var cover = Mathf.Clamp01(1.6f - d);
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

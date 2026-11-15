using HS2StudioBrowser.UI;
using UnityEngine;

namespace HS2StudioBrowser.Scenes
{
    /// <summary>A framed landscape (mountains and a sun) drawn in code, with the browsers' neon tab.</summary>
    internal static class SceneIcon
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
                // Frame: rounded rectangle outline 4..28 x 6..24
                var qx = Mathf.Abs(px - 16f) - 10.5f;
                var qy = Mathf.Abs(py - 15f) - 7.5f;
                var outside = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude;
                var frame = Mathf.Abs(outside + Mathf.Min(Mathf.Max(qx, qy), 0) - 1.5f + 1.5f);
                // Mountains: two peaks inside the frame
                var m = Mathf.Min(Seg(px, py, 7f, 21.5f, 13f, 14f), Seg(px, py, 13f, 14f, 17.5f, 19f));
                m = Mathf.Min(m, Mathf.Min(Seg(px, py, 16f, 17.5f, 20f, 13.5f), Seg(px, py, 20f, 13.5f, 25f, 21.5f)));
                // Sun
                var sun = Mathf.Abs(new Vector2(px - 22.2f, py - 11.4f).magnitude - 1.4f);
                var d = Mathf.Min(frame, Mathf.Min(m, sun));
                var cover = Mathf.Clamp01(1.45f - d);
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

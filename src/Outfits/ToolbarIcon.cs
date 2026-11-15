using HS2StudioBrowser.UI;
using UnityEngine;

namespace HS2StudioBrowser.Outfits
{
    /// <summary>A clothes hanger drawn in code, so the plugin ships as a single DLL.</summary>
    internal static class ToolbarIcon
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
                // Texture rows run bottom-up; work in top-down coordinates.
                var px = x + 0.5f;
                var py = size - (y + 0.5f);

                // Hanger: hook, two shoulders, bottom bar.
                var d = Mathf.Min(
                    Mathf.Min(Seg(px, py, 16f, 11f, 3.5f, 22f), Seg(px, py, 16f, 11f, 28.5f, 22f)),
                    Seg(px, py, 3.5f, 22f, 28.5f, 22f));
                // Hook: a small arc above the neck.
                var hook = Mathf.Abs(new Vector2(px - 16f, py - 7f).magnitude - 3f);
                if (py > 7f && px < 16f) hook = Mathf.Min(hook, new Vector2(px - 16f, py - 10f).magnitude); // open left side into the neck
                if (py > 8.5f && px < 16f && px > 12f) hook = 99f;
                d = Mathf.Min(d, hook);

                var c = new Color(0, 0, 0, 0);
                var cover = Mathf.Clamp01(2.1f - d);
                if (cover > 0) c = new Color(line.r, line.g, line.b, cover);

                // A neon "folder" tab under the bar.
                if (px >= 9f && px <= 23f && py >= 25f && py <= 28f) c = accent;

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

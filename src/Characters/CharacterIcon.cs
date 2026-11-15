using HS2StudioBrowser.UI;
using UnityEngine;

namespace HS2StudioBrowser.Characters
{
    /// <summary>A head-and-shoulders silhouette drawn in code, with the same neon tab as the other browsers' icons.</summary>
    internal static class CharacterIcon
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
                // Head: ring
                var head = Mathf.Abs(new Vector2(px - 16f, py - 9.5f).magnitude - 4.6f);
                // Shoulders: upper half of an ellipse outline
                var d = new Vector2((px - 16f) / 10f, (py - 23.5f) / 6.5f);
                var shoulders = py <= 23.5f ? Mathf.Abs(d.magnitude - 1f) * 7f : Mathf.Abs(Mathf.Abs(px - 16f) - 10f) + (py > 23.5f ? 0 : 99);
                if (py > 23.5f) shoulders = 99f;
                var line2 = Mathf.Abs(py - 23.5f) + (Mathf.Abs(px - 16f) <= 10f ? 0f : 99f);
                var dist = Mathf.Min(head, Mathf.Min(shoulders, line2));
                var cover = Mathf.Clamp01(2.1f - dist);
                var c = cover > 0 ? new Color(line.r, line.g, line.b, cover) : new Color(0, 0, 0, 0);
                if (px >= 9f && px <= 23f && py >= 26.5f && py <= 29f) c = accent;
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            return tex;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace BlockPeak.Assets
{
    /// <summary>Draws text with Minecraft's own bitmap font (font/ascii.png), shadow included.</summary>
    public static class McFont
    {
        private static Texture2D atlas;
        private static Color[] atlasPx;
        private static int cell;
        private static readonly int[] widths = new int[256];
        private static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        private static bool Ensure()
        {
            if (atlas != null) return true;
            if (!McAssets.HasTexture("font/ascii.png")) return false;
            atlas = McAssets.Tex("font/ascii.png");
            try { atlasPx = atlas.GetPixels(); }
            catch { atlas = null; return false; }
            cell = atlas.width / 16;
            for (int ch = 0; ch < 256; ch++)
            {
                int gx = (ch % 16) * cell, gyTop = (ch / 16) * cell;
                int w = 0;
                for (int x = cell - 1; x >= 0 && w == 0; x--)
                    for (int y = 0; y < cell; y++)
                        if (Px(gx + x, gyTop + y).a > 0.1f) { w = x + 1; break; }
                widths[ch] = ch == ' ' ? cell / 2 : w;
            }
            return true;
        }

        private static Color Px(int x, int yFromTop) => atlasPx[(atlas.height - 1 - yFromTop) * atlas.width + x];

        public static int Width(string s)
        {
            if (!Ensure()) return s.Length * 6;
            int w = 0;
            foreach (char c in s) w += (c < 256 ? widths[c] : cell / 2) + 1;
            return w + 1;
        }

        /// <summary>A texture with the text in white plus Minecraft's dark drop shadow. Null if the font was not copied.</summary>
        public static Texture2D Render(string s, Color color)
        {
            string key = s + "|" + ColorUtility.ToHtmlStringRGBA(color);
            if (cache.TryGetValue(key, out var t) && t != null) return t;
            if (!Ensure()) return null;
            int w = Width(s) + 1, h = cell + 1;
            var px = new Color[w * h];
            Color shadow = new Color(color.r * 0.25f, color.g * 0.25f, color.b * 0.25f, color.a);
            for (int pass = 0; pass < 2; pass++)
            {
                int ox = pass == 0 ? 1 : 0, oy = pass == 0 ? 1 : 0;
                Color col = pass == 0 ? shadow : color;
                int pen = 0;
                foreach (char ch0 in s)
                {
                    int ch = ch0 < 256 ? ch0 : '?';
                    int gx = (ch % 16) * cell, gyTop = (ch / 16) * cell;
                    for (int x = 0; x < widths[ch]; x++)
                        for (int y = 0; y < cell; y++)
                        {
                            if (Px(gx + x, gyTop + y).a < 0.1f) continue;
                            int dx = pen + x + ox, dyTop = y + oy;
                            if (dx < 0 || dx >= w || dyTop >= h) continue;
                            px[(h - 1 - dyTop) * w + dx] = col;
                        }
                    pen += widths[ch] + 1;
                }
            }
            t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels(px);
            t.Apply();
            if (cache.Count > 200) cache.Clear();
            cache[key] = t;
            return t;
        }
    }
}

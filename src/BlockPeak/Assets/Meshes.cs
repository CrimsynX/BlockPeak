using System.Collections.Generic;
using UnityEngine;

namespace BlockPeak.Assets
{
    /// <summary>
    /// Builds Minecraft-looking geometry at runtime: extruded item sprites, textured cubes,
    /// entity box models (with Minecraft's UV layout) and isometric block icons.
    /// </summary>
    public class MeshBuilder
    {
        private readonly List<Vector3> v = new List<Vector3>();
        private readonly List<Vector2> uv = new List<Vector2>();
        private readonly List<int> tri = new List<int>();

        public enum Face { Right, Left, Top, Bottom, Front, Back }

        /// <summary>Quad given as seen from outside: bottom-left, bottom-right, top-right, top-left.</summary>
        public void Quad(Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl, Vector2 uvBL, Vector2 uvBR, Vector2 uvTR, Vector2 uvTL)
        {
            int i = v.Count;
            v.Add(bl); v.Add(br); v.Add(tr); v.Add(tl);
            uv.Add(uvBL); uv.Add(uvBR); uv.Add(uvTR); uv.Add(uvTL);
            tri.Add(i); tri.Add(i + 3); tri.Add(i + 2);
            tri.Add(i); tri.Add(i + 2); tri.Add(i + 1);
        }

        public void Quad(Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl, Rect r)
        {
            Quad(bl, br, tr, tl, new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax));
        }

        public void FaceQuad(Face f, Vector3 a, Vector3 b, Rect r)
        {
            float x0 = a.x, y0 = a.y, z0 = a.z, x1 = b.x, y1 = b.y, z1 = b.z;
            switch (f)
            {
                case Face.Front: Quad(new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), r); break;
                case Face.Back: Quad(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), r); break;
                case Face.Right: Quad(new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), r); break;
                case Face.Left: Quad(new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), r); break;
                case Face.Top: Quad(new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), r); break;
                case Face.Bottom: Quad(new Vector3(x1, y0, z0), new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), r); break;
            }
        }

        /// <summary>Axis aligned box, one UV rect per face (Right, Left, Top, Bottom, Front, Back).</summary>
        public void Box(Vector3 min, Vector3 max, Rect[] faces)
        {
            for (int i = 0; i < 6; i++) FaceQuad((Face)i, min, max, faces[i]);
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetTriangles(tri, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>UV rect for a pixel rectangle measured from the top-left, like Minecraft does.</summary>
        public static Rect PxRect(float px, float pyTop, float pw, float ph, float texW, float texH, bool mirror = false)
        {
            const float e = 0.01f;
            float u0 = (px + e) / texW, u1 = (px + pw - e) / texW;
            float v1 = 1f - (pyTop + e) / texH, v0 = 1f - (pyTop + ph - e) / texH;
            if (mirror) { float t = u0; u0 = u1; u1 = t; }
            return Rect.MinMaxRect(u0, v0, u1, v1);
        }

        /// <summary>Minecraft's standard cube UV layout for a box of size w,h,d at texture offset u,v.</summary>
        public static Rect[] McBoxUV(int u, int v, float w, float h, float d, float texW, float texH, bool mirror)
        {
            Rect right = PxRect(u, v + d, d, h, texW, texH, mirror);
            Rect front = PxRect(u + d, v + d, w, h, texW, texH, mirror);
            Rect left = PxRect(u + d + w, v + d, d, h, texW, texH, mirror);
            Rect back = PxRect(u + d + w + d, v + d, w, h, texW, texH, mirror);
            Rect top = PxRect(u + d, v, w, d, texW, texH, mirror);
            Rect bottom = PxRect(u + d + w, v, w, d, texW, texH, mirror);
            if (mirror) { var t = right; right = left; left = t; }
            return new[] { right, left, top, bottom, front, back };
        }

        /// <summary>
        /// A Minecraft model box: origin/size in model pixels relative to the part pivot (Minecraft axes, y down).
        /// Converted to Unity (x and z flipped so the model faces +Z, y up).
        /// </summary>
        public void McBox(float ox, float oy, float oz, float w, float h, float d, int u, int v, float texW, float texH, bool mirror = false, float inflate = 0f)
        {
            ox -= inflate; oy -= inflate; oz -= inflate; w += 2 * inflate; h += 2 * inflate; d += 2 * inflate;
            var min = new Vector3(-(ox + w) / 16f, -(oy + h) / 16f, -(oz + d) / 16f);
            var max = new Vector3(-ox / 16f, -oy / 16f, -oz / 16f);
            Box(min, max, McBoxUV(u, v, w - 2 * inflate, h - 2 * inflate, d - 2 * inflate, texW, texH, mirror));
        }
    }

    public static class Meshes
    {
        private static readonly Dictionary<Texture2D, Mesh> spriteCache = new Dictionary<Texture2D, Mesh>();

        /// <summary>
        /// Minecraft's held-item look: every opaque pixel of a 16x16 sprite becomes a little block, 1/16 thick.
        /// Result is 1 unit wide, centred on the origin, lying in the XY plane.
        /// </summary>
        public static Mesh ItemSprite(Texture2D tex)
        {
            if (spriteCache.TryGetValue(tex, out var cached) && cached != null) return cached;
            Color32[] px;
            try { px = tex.GetPixels32(); }
            catch { px = null; }
            int W = tex.width, H = tex.height;
            var mb = new MeshBuilder();
            float s = 1f / Mathf.Max(W, H);
            float zf = -0.5f * s, zb = 0.5f * s;
            bool Solid(int x, int y) => px != null && x >= 0 && y >= 0 && x < W && y < H && px[y * W + x].a > 25;

            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (!Solid(x, y)) continue;
                    var c = new Vector2((x + 0.5f) / W, (y + 0.5f) / H);
                    float x0 = (x - W / 2f) * s, x1 = x0 + s, y0 = (y - H / 2f) * s, y1 = y0 + s;
                    // front (-z) and back (+z)
                    mb.Quad(new Vector3(x0, y0, zf), new Vector3(x1, y0, zf), new Vector3(x1, y1, zf), new Vector3(x0, y1, zf), c, c, c, c);
                    mb.Quad(new Vector3(x1, y0, zb), new Vector3(x0, y0, zb), new Vector3(x0, y1, zb), new Vector3(x1, y1, zb), c, c, c, c);
                    if (!Solid(x - 1, y)) mb.Quad(new Vector3(x0, y0, zb), new Vector3(x0, y0, zf), new Vector3(x0, y1, zf), new Vector3(x0, y1, zb), c, c, c, c);
                    if (!Solid(x + 1, y)) mb.Quad(new Vector3(x1, y0, zf), new Vector3(x1, y0, zb), new Vector3(x1, y1, zb), new Vector3(x1, y1, zf), c, c, c, c);
                    if (!Solid(x, y + 1)) mb.Quad(new Vector3(x0, y1, zf), new Vector3(x1, y1, zf), new Vector3(x1, y1, zb), new Vector3(x0, y1, zb), c, c, c, c);
                    if (!Solid(x, y - 1)) mb.Quad(new Vector3(x1, y0, zf), new Vector3(x0, y0, zf), new Vector3(x0, y0, zb), new Vector3(x1, y0, zb), c, c, c, c);
                }
            if (px == null)
            {
                // Unreadable texture: a flat card is better than nothing.
                mb.Quad(new Vector3(-0.5f, -0.5f, zf), new Vector3(0.5f, -0.5f, zf), new Vector3(0.5f, 0.5f, zf), new Vector3(-0.5f, 0.5f, zf), Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            }
            var m = mb.Build("mc_item_" + tex.name);
            spriteCache[tex] = m;
            return m;
        }

        /// <summary>
        /// Packs side/top/bottom block textures into one strip (side | top | bottom) so a cube needs one material.
        /// </summary>
        public static Texture2D BlockAtlas(Texture2D side, Texture2D top, Texture2D bottom)
        {
            int n = 16;
            var atlas = new Texture2D(n * 3, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var list = new[] { side, top ?? side, bottom ?? top ?? side };
            for (int i = 0; i < 3; i++)
            {
                var t = list[i];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        Color c;
                        try { c = t.GetPixel(x * t.width / n, y * t.width / n); } catch { c = Color.magenta; }
                        c.a = 1f;
                        atlas.SetPixel(i * n + x, y, c);
                    }
            }
            atlas.Apply();
            atlas.name = "mc_atlas_" + side.name;
            return atlas;
        }

        /// <summary>Unit cube (size x size x size), bottom at y=0, textured from a BlockAtlas strip.</summary>
        public static Mesh Cube(float size, string name)
        {
            var mb = new MeshBuilder();
            float h = size / 2f;
            Rect side = Rect.MinMaxRect(0.001f, 0.001f, 1f / 3f - 0.001f, 0.999f);
            Rect top = Rect.MinMaxRect(1f / 3f + 0.001f, 0.001f, 2f / 3f - 0.001f, 0.999f);
            Rect bottom = Rect.MinMaxRect(2f / 3f + 0.001f, 0.001f, 0.999f, 0.999f);
            mb.Box(new Vector3(-h, 0, -h), new Vector3(h, size, h), new[] { side, side, top, bottom, side, side });
            return mb.Build(name);
        }

        /// <summary>Minecraft's torch model: a 2x10x2 pixel stick using the middle of torch.png.</summary>
        public static Mesh Torch(float blockSize)
        {
            var mb = new MeshBuilder();
            float p = blockSize / 16f;
            var min = new Vector3(-p, 0, -p);
            var max = new Vector3(p, 10 * p, p);
            Rect side = MeshBuilder.PxRect(7, 6, 2, 10, 16, 16);
            Rect top = MeshBuilder.PxRect(7, 6, 2, 2, 16, 16);
            Rect bottom = MeshBuilder.PxRect(7, 14, 2, 2, 16, 16);
            mb.Box(min, max, new[] { side, side, top, bottom, side, side });
            return mb.Build("mc_torch");
        }

        /// <summary>Isometric inventory icon of a block, like Minecraft's hotbar shows.</summary>
        public static Texture2D BlockIcon(Texture2D side, Texture2D top)
        {
            const int S = 32;
            var icon = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var clear = new Color[S * S];
            icon.SetPixels(clear);
            for (int py = 0; py < S; py++)
                for (int px = 0; px < S; px++)
                {
                    float sx = px + 0.5f, sy = py + 0.5f; // sy measured from the top
                    float u = (sx - 16f) / 15f;
                    float w = (sy - 1f) / 7.5f;
                    float x = (u + w) / 2f, z = (w - u) / 2f;
                    Color c = Color.clear;
                    if (x >= 0 && x <= 1 && z >= 0 && z <= 1)
                    {
                        c = Sample(top ?? side, x, 1f - z) * 1.0f;
                    }
                    else
                    {
                        // right face (x = 1)
                        float zr = 1f - u;
                        float yr = 1f - (sy - 8.5f - 7.5f * zr) / 16f;
                        if (u >= 0 && zr >= 0 && zr <= 1 && yr >= 0 && yr <= 1) c = Sample(side, 1f - zr, yr) * 0.6f;
                        else
                        {
                            float xl = u + 1f;
                            float yl = 1f - (sy - 8.5f - 7.5f * xl) / 16f;
                            if (u < 0 && xl >= 0 && xl <= 1 && yl >= 0 && yl <= 1) c = Sample(side, xl, yl) * 0.8f;
                        }
                    }
                    if (c.a > 0) { c.a = 1f; icon.SetPixel(px, S - 1 - py, c); }
                }
            icon.Apply();
            icon.name = "mc_icon_" + side.name;
            return icon;
        }

        private static Color Sample(Texture2D t, float h, float vUp)
        {
            int x = Mathf.Clamp((int)(h * 16f), 0, 15), y = Mathf.Clamp((int)(vUp * 16f), 0, 15);
            try { return t.GetPixel(x * t.width / 16, y * t.width / 16); }
            catch { return Color.magenta; }
        }
    }
}

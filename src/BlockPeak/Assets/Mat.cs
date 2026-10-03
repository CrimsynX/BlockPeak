using System.Collections.Generic;
using System.Linq;
using BlockPeak.Core;
using UnityEngine;

namespace BlockPeak.Assets
{
    /// <summary>
    /// Materials for Minecraft textures. PEAK uses its own shaders, so the safest base is a copy of one of
    /// PEAK's own item materials with the texture swapped. Falls back to common Unity shaders.
    /// </summary>
    public static class Mat
    {
        private static readonly string[] TexProps = { "_BaseMap", "_MainTex", "_BaseTexture", "_Albedo", "_AlbedoMap", "_Texture", "_MainTexture", "_BaseColorMap", "_Tex" };
        private static readonly string[] ColorProps = { "_BaseColor", "_Color", "_Tint", "_MainColor" };
        private static Material baseMat;
        private static string texProp;
        private static bool searched;
        private static readonly Dictionary<Texture, Material> cache = new Dictionary<Texture, Material>();
        private static readonly Dictionary<Texture, Material> glowCache = new Dictionary<Texture, Material>();

        public static string Describe => baseMat == null ? "none" : $"{baseMat.shader.name} via {texProp}";

        private static void FindBase()
        {
            if (searched && baseMat != null) return;
            searched = true;

            string over = Cfg.ItemShaderOverride.Value;
            if (!string.IsNullOrWhiteSpace(over))
            {
                var sh = Shader.Find(over.Trim());
                if (sh != null) { Use(new Material(sh)); return; }
                Health.Report("material", "ItemShaderOverride '" + over + "' was not found, using automatic choice.");
            }

            // 1) A PEAK item material that has a main texture slot.
            try
            {
                var db = Game.ItemDb;
                if (db != null)
                {
                    foreach (var item in db.itemLookup.Values.Where(i => i != null))
                    {
                        foreach (var r in item.GetComponentsInChildren<MeshRenderer>(true))
                        {
                            var m = r.sharedMaterial;
                            if (m == null || m.shader == null) continue;
                            string prop = TexProps.FirstOrDefault(p => m.HasProperty(p) && m.GetTexture(p) != null);
                            if (prop == null) continue;
                            baseMat = new Material(m);
                            texProp = prop;
                            Plugin.Log.LogInfo($"Minecraft textures use PEAK material '{m.name}' ({m.shader.name}) from item {item.name}.");
                            return;
                        }
                    }
                }
            }
            catch (System.Exception e) { Health.Report("material", e); }

            // 2) Common shaders.
            foreach (var n in new[] { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Standard", "Universal Render Pipeline/Unlit", "Unlit/Texture", "Sprites/Default" })
            {
                var sh = Shader.Find(n);
                if (sh != null) { Use(new Material(sh)); return; }
            }
            searched = false; // try again later (item database may not be loaded yet)
        }

        private static void Use(Material m)
        {
            baseMat = m;
            texProp = TexProps.FirstOrDefault(m.HasProperty) ?? "_MainTex";
            Plugin.Log.LogInfo($"Minecraft textures use shader {m.shader.name} ({texProp}).");
        }

        public static Material For(Texture tex)
        {
            if (tex != null && cache.TryGetValue(tex, out var m) && m != null) return m;
            FindBase();
            m = baseMat != null ? new Material(baseMat) : new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Hidden/InternalErrorShader"));
            m.name = "BlockPeak_" + (tex != null ? tex.name : "none");
            foreach (var p in TexProps) if (m.HasProperty(p)) m.SetTexture(p, null);
            if (texProp != null && m.HasProperty(texProp)) m.SetTexture(texProp, tex);
            m.mainTexture = tex;
            foreach (var p in ColorProps) if (m.HasProperty(p)) m.SetColor(p, Color.white);
            // Normal / detail maps from the copied PEAK material would look wrong on pixel art.
            foreach (var p in new[] { "_BumpMap", "_NormalMap", "_DetailAlbedoMap", "_DetailNormalMap", "_OcclusionMap", "_MetallicGlossMap", "_EmissionMap" })
                if (m.HasProperty(p)) m.SetTexture(p, null);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.1f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (tex != null) cache[tex] = m;
            return m;
        }

        /// <summary>Same as <see cref="For"/> but glowing (torch flames, magma cubes, lit TNT).</summary>
        public static Material Glowing(Texture tex, Color glow)
        {
            if (tex != null && glowCache.TryGetValue(tex, out var g) && g != null) return g;
            g = new Material(For(tex)) { name = "BlockPeak_glow_" + (tex != null ? tex.name : "") };
            if (g.HasProperty("_EmissionColor"))
            {
                g.EnableKeyword("_EMISSION");
                g.SetColor("_EmissionColor", glow);
                if (g.HasProperty("_EmissionMap")) g.SetTexture("_EmissionMap", tex);
            }
            if (tex != null) glowCache[tex] = g;
            return g;
        }

        /// <summary>Plain coloured material (water splash, particles).</summary>
        public static Material Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.name = "solid_" + ColorUtility.ToHtmlStringRGBA(c);
            return For(t);
        }
    }
}

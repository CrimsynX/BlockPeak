using System.Collections.Generic;
using BlockPeak.Assets;
using BlockPeak.Core;
using UnityEngine;

namespace BlockPeak.Mobs
{
    /// <summary>A built mob: named parts that the animator moves.</summary>
    public class MobModel
    {
        public GameObject Root;
        public Transform Head, Body, ArmR, ArmL, LegR, LegL;
        public readonly List<Transform> SpiderLegsR = new List<Transform>();
        public readonly List<Transform> SpiderLegsL = new List<Transform>();
        public readonly List<Transform> QuadLegs = new List<Transform>(); // creeper: RH, LH, RF, LF
        public readonly List<Renderer> Renderers = new List<Renderer>();
        public readonly Dictionary<Renderer, System.Action<MeshBuilder>> Boxes = new Dictionary<Renderer, System.Action<MeshBuilder>>();
        public float Height = 2f;
        public float Width = 0.6f;
        public string Kind; // humanoid, skeleton, warden, spider, creeper, slime, magma
        public Transform Inner;
        public Transform TendrilR, TendrilL;
        public Material Glow;            // warden: the bioluminescent layer (pulses)
        // Proportions in Minecraft pixels, used to hang the parts on PEAK's skeleton.
        public float LegPx = 12, BodyPx = 12, ShoulderX = 5, ShoulderDrop = 2, HipX = 1.9f;
        public bool FollowsBones => Kind == "humanoid" || Kind == "skeleton" || Kind == "warden";
    }

    /// <summary>
    /// Minecraft's entity models rebuilt from their box definitions (positions and texture offsets are the
    /// vanilla ones), textured with the player's own Minecraft textures.
    /// </summary>
    public static class MobModels
    {
        public static string TextureFor(string type)
        {
            switch (type)
            {
                case "zombie": return "entity/zombie/zombie.png";
                case "husk": return "entity/zombie/husk.png";
                case "drowned": return "entity/zombie/drowned.png";
                case "skeleton": return "entity/skeleton/skeleton.png";
                case "stray": return "entity/skeleton/stray.png";
                case "bogged": return "entity/skeleton/bogged.png";
                case "spider": return "entity/spider/spider.png";
                case "creeper": return "entity/creeper/creeper.png";
                case "slime": return "entity/slime/slime.png";
                case "magma_cube": return "entity/slime/magmacube.png";
                case "warden": return "entity/warden/warden.png";
                default: return "entity/zombie/zombie.png";
            }
        }

        public static MobModel Build(string type, Transform parent)
        {
            var m = new MobModel { Root = new GameObject("mob_" + type) };
            m.Root.transform.SetParent(parent, false);
            var tex = McAssets.Tex(TextureFor(type));
            var mat = type == "magma_cube" ? Mat.Glowing(tex, new Color(0.5f, 0.2f, 0.05f)) : Mat.For(tex);
            float scale = Mathf.Clamp(Balance.F(Balance.Section("mobs"), "scale", 0.9f), 0.3f, 2f);
            var inner = new GameObject("model").transform;
            inner.SetParent(m.Root.transform, false);
            inner.localScale = Vector3.one * scale;
            m.Inner = inner;

            switch (type)
            {
                case "skeleton":
                case "stray":
                case "bogged":
                    m.Kind = "skeleton";
                    Humanoid(m, inner, mat, 64, 32, true);
                    m.HipX = 2f;
                    break;
                case "warden":
                    m.Kind = "warden";
                    Warden(m, inner, mat);
                    m.LegPx = 13; m.BodyPx = 21; m.ShoulderX = 13; m.ShoulderDrop = 0; m.HipX = 5.9f;
                    m.Height = 3.1f * scale; m.Width = 1.1f * scale;
                    break;
                case "spider":
                    m.Kind = "spider";
                    Spider(m, inner, mat);
                    m.Height = 0.9f * scale; m.Width = 1.4f * scale;
                    break;
                case "creeper":
                    m.Kind = "creeper";
                    Creeper(m, inner, mat);
                    m.Height = 1.7f * scale;
                    break;
                case "slime":
                    m.Kind = "slime";
                    Slime(m, inner, mat, 1.6f);
                    m.Height = 1.0f * scale; m.Width = 1f * scale;
                    break;
                case "magma_cube":
                    m.Kind = "magma";
                    Magma(m, inner, mat, 1.6f, tex);
                    m.Height = 1.0f * scale; m.Width = 1f * scale;
                    break;
                default:
                    m.Kind = "humanoid";
                    // Zombies and husks only have the right arm/leg in their texture (the left ones mirror it);
                    // drowned use the player layout with separate left limbs.
                    Humanoid(m, inner, mat, 64, 64, false, type == "drowned");
                    break;
            }
            if (m.Kind == "humanoid" || m.Kind == "skeleton") m.Height = 1.95f * scale;
            return m;
        }

        // ----------------------------------------------------------------- part helpers

        private static Vector3 Pivot(float px, float py, float pz) => new Vector3(-px / 16f, (24f - py) / 16f, -pz / 16f);

        private static Transform Part(MobModel m, Transform parent, string name, Vector3 pivot, Material mat, System.Action<MeshBuilder> boxes)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pivot;
            // Only the opaque texels become geometry, so see-through parts of the texture stay see-through.
            var mb = new MeshBuilder { Cut = mat != null ? mat.mainTexture as Texture2D : null };
            boxes(mb);
            go.AddComponent<MeshFilter>().sharedMesh = mb.Build("mob_" + name);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            m.Renderers.Add(r);
            m.Boxes[r] = boxes;
            return go.transform;
        }

        private static void Humanoid(MobModel m, Transform root, Material mat, float tw, float th, bool thin, bool ownLeftLimbs = false)
        {
            m.Head = Part(m, root, "head", Pivot(0, 0, 0), mat, b => b.McBox(-4, -8, -4, 8, 8, 8, 0, 0, tw, th));
            m.Body = Part(m, root, "body", Pivot(0, 0, 0), mat, b => b.McBox(-4, 0, -2, 8, 12, 4, 16, 16, tw, th));
            if (thin)
            {
                m.ArmR = Part(m, root, "armR", Pivot(-5, 2, 0), mat, b => b.McBox(-1, -2, -1, 2, 12, 2, 40, 16, tw, th));
                m.ArmL = Part(m, root, "armL", Pivot(5, 2, 0), mat, b => b.McBox(-1, -2, -1, 2, 12, 2, 40, 16, tw, th, true));
                m.LegR = Part(m, root, "legR", Pivot(-2, 12, 0), mat, b => b.McBox(-1, 0, -1, 2, 12, 2, 0, 16, tw, th));
                m.LegL = Part(m, root, "legL", Pivot(2, 12, 0), mat, b => b.McBox(-1, 0, -1, 2, 12, 2, 0, 16, tw, th, true));
            }
            else
            {
                m.ArmR = Part(m, root, "armR", Pivot(-5, 2, 0), mat, b => b.McBox(-3, -2, -2, 4, 12, 4, 40, 16, tw, th));
                m.ArmL = Part(m, root, "armL", Pivot(5, 2, 0), mat, b =>
                {
                    if (ownLeftLimbs) b.McBox(-1, -2, -2, 4, 12, 4, 32, 48, tw, th);
                    else b.McBox(-1, -2, -2, 4, 12, 4, 40, 16, tw, th, true);
                });
                m.LegR = Part(m, root, "legR", Pivot(-1.9f, 12, 0), mat, b => b.McBox(-2, 0, -2, 4, 12, 4, 0, 16, tw, th));
                m.LegL = Part(m, root, "legL", Pivot(1.9f, 12, 0), mat, b =>
                {
                    if (ownLeftLimbs) b.McBox(-2, 0, -2, 4, 12, 4, 16, 48, tw, th);
                    else b.McBox(-2, 0, -2, 4, 12, 4, 0, 16, tw, th, true);
                });
            }
        }

        /// <summary>
        /// Minecraft's WardenModel (128x128 texture) with its flat ribcage and head tendrils, plus a second, glowing copy
        /// of every part textured with the bioluminescent layer, heart and spots (pulses with the warden's heartbeat).
        /// </summary>
        private static void Warden(MobModel m, Transform root, Material mat)
        {
            const float tw = 128, th = 128;
            m.Body = Part(m, root, "body", Pivot(0, -10, 0), mat, b =>
            {
                b.McBox(-9, 0, -4, 18, 21, 11, 0, 0, tw, th);
                // ribcage plates on the chest (flat, 0 deep): body pivot is 13px above the ribcage pivots
                b.McBox(-7 - 2 + 0, 13 - 2 - 11, -4 - 0.1f, 9, 21, 0, 90, 11, tw, th);
                b.McBox(7 - 7, 13 - 2 - 11, -4 - 0.1f, 9, 21, 0, 90, 11, tw, th, true);
            });
            m.Head = Part(m, root, "head", Pivot(0, -10, 0), mat, b => b.McBox(-8, -16, -5, 16, 16, 10, 0, 32, tw, th));
            m.TendrilR = Part(m, m.Head, "tendrilR", Local(-8, -12, 0), mat, b => b.McBox(-16, -13, 0, 16, 16, 0, 52, 32, tw, th));
            m.TendrilL = Part(m, m.Head, "tendrilL", Local(8, -12, 0), mat, b => b.McBox(0, -13, 0, 16, 16, 0, 58, 0, tw, th));
            m.ArmR = Part(m, root, "armR", Pivot(-13, -10, 1), mat, b => b.McBox(-4, 0, -4, 8, 28, 8, 44, 50, tw, th));
            m.ArmL = Part(m, root, "armL", Pivot(13, -10, 1), mat, b => b.McBox(-4, 0, -4, 8, 28, 8, 0, 58, tw, th));
            m.LegR = Part(m, root, "legR", Pivot(-5.9f, 11, 0), mat, b => b.McBox(-3.1f, 0, -3, 6, 13, 6, 76, 48, tw, th));
            m.LegL = Part(m, root, "legL", Pivot(5.9f, 11, 0), mat, b => b.McBox(-2.9f, 0, -3, 6, 13, 6, 76, 76, tw, th));

            try
            {
                var glowTex = WardenGlowTexture();
                if (glowTex == null) return;
                var g = new Material(Mat.For(glowTex)) { name = "BlockPeak_warden_glow" };
                if (g.HasProperty("_EmissionColor"))
                {
                    g.EnableKeyword("_EMISSION");
                    g.SetColor("_EmissionColor", new Color(0.2f, 0.9f, 0.9f));
                    if (g.HasProperty("_EmissionMap")) g.SetTexture("_EmissionMap", glowTex);
                }
                Mat.Cutout(g);
                m.Glow = g;
                foreach (var r in m.Renderers.ToArray())
                {
                    if (!m.Boxes.TryGetValue(r, out var boxes)) continue;
                    var gmb = new MeshBuilder { Cut = glowTex };
                    boxes(gmb);
                    var copy = new GameObject(r.name + "_glow");
                    copy.transform.SetParent(r.transform, false);
                    copy.transform.localScale = Vector3.one * 1.004f;
                    copy.AddComponent<MeshFilter>().sharedMesh = gmb.Build("mob_glow_" + r.name);
                    var gr = copy.AddComponent<MeshRenderer>();
                    gr.sharedMaterial = g;
                    gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            catch (System.Exception e) { Health.Report("warden-glow", e); }
        }

        /// <summary>Position of a child part relative to its parent part's pivot (Minecraft pixels, y down).</summary>
        private static Vector3 Local(float px, float py, float pz) => new Vector3(-px / 16f, -py / 16f, -pz / 16f);

        private static Texture2D wardenGlow;

        private static Texture2D WardenGlowTexture()
        {
            if (wardenGlow != null) return wardenGlow;
            var layers = new[]
            {
                "entity/warden/warden_bioluminescent_layer.png", "entity/warden/warden_heart.png",
                "entity/warden/warden_pulsating_spots_1.png", "entity/warden/warden_pulsating_spots_2.png",
            };
            Texture2D outTex = null;
            foreach (var path in layers)
            {
                if (!McAssets.HasTexture(path)) continue;
                var t = McAssets.Tex(path);
                if (outTex == null)
                {
                    outTex = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "warden_glow" };
                    outTex.SetPixels32(new Color32[t.width * t.height]);
                }
                if (t.width != outTex.width || t.height != outTex.height) continue;
                var src = t.GetPixels32();
                var dst = outTex.GetPixels32();
                for (int i = 0; i < src.Length; i++) if (src[i].a > dst[i].a) dst[i] = src[i];
                outTex.SetPixels32(dst);
            }
            if (outTex == null) return null;
            outTex.Apply();
            return wardenGlow = outTex;
        }

        private static void Spider(MobModel m, Transform root, Material mat)
        {
            const float tw = 64, th = 32;
            m.Head = Part(m, root, "head", Pivot(0, 15, -3), mat, b => b.McBox(-4, -4, -8, 8, 8, 8, 32, 4, tw, th));
            Part(m, root, "neck", Pivot(0, 15, 0), mat, b => b.McBox(-3, -3, -3, 6, 6, 6, 0, 0, tw, th));
            m.Body = Part(m, root, "body", Pivot(0, 15, 9), mat, b => b.McBox(-5, -4, -6, 10, 8, 12, 0, 12, tw, th));
            float[] zs = { 2, 1, 0, -1 };
            for (int i = 0; i < 4; i++)
            {
                float z = zs[i];
                m.SpiderLegsR.Add(Part(m, root, "legR" + i, Pivot(-4, 15, z), mat, b => b.McBox(-15, -1, -1, 16, 2, 2, 18, 0, tw, th)));
                m.SpiderLegsL.Add(Part(m, root, "legL" + i, Pivot(4, 15, z), mat, b => b.McBox(-1, -1, -1, 16, 2, 2, 18, 0, tw, th)));
            }
        }

        private static void Creeper(MobModel m, Transform root, Material mat)
        {
            const float tw = 64, th = 32;
            m.Head = Part(m, root, "head", Pivot(0, 6, 0), mat, b => b.McBox(-4, -8, -4, 8, 8, 8, 0, 0, tw, th));
            m.Body = Part(m, root, "body", Pivot(0, 6, 0), mat, b => b.McBox(-4, 0, -2, 8, 12, 4, 16, 16, tw, th));
            m.QuadLegs.Add(Part(m, root, "legRH", Pivot(-2, 18, 4), mat, b => b.McBox(-2, 0, -2, 4, 6, 4, 0, 16, tw, th)));
            m.QuadLegs.Add(Part(m, root, "legLH", Pivot(2, 18, 4), mat, b => b.McBox(-2, 0, -2, 4, 6, 4, 0, 16, tw, th)));
            m.QuadLegs.Add(Part(m, root, "legRF", Pivot(-2, 18, -4), mat, b => b.McBox(-2, 0, -2, 4, 6, 4, 0, 16, tw, th)));
            m.QuadLegs.Add(Part(m, root, "legLF", Pivot(2, 18, -4), mat, b => b.McBox(-2, 0, -2, 4, 6, 4, 0, 16, tw, th)));
        }

        private static void Slime(MobModel m, Transform root, Material mat, float size)
        {
            const float tw = 64, th = 32;
            var holder = new GameObject("slime").transform;
            holder.SetParent(root, false);
            holder.localScale = Vector3.one * size * (8f / 6f); // the inner core grown to the slime's real size
            holder.localPosition = new Vector3(0, -17f / 16f * size * (8f / 6f) + 0f, 0);
            m.Body = Part(m, holder, "core", Pivot(0, 0, 0), mat, b =>
            {
                b.McBox(-3, 17, -3, 6, 6, 6, 0, 16, tw, th);
                b.McBox(-3.25f, 18, -3.5f, 2, 2, 2, 32, 0, tw, th);
                b.McBox(1.25f, 18, -3.5f, 2, 2, 2, 32, 4, tw, th);
                b.McBox(0, 21, -3.5f, 1, 1, 1, 32, 8, tw, th);
            });
            // Pivot(0,0,0) puts y=24 at the feet, so the cube (y 17..23) sits 1px up; shift so it touches the ground.
            holder.localPosition = new Vector3(0, -1f / 16f * holder.localScale.y, 0);
            m.Head = m.Body;
        }

        private static void Magma(MobModel m, Transform root, Material mat, float size, Texture2D tex)
        {
            bool modern = tex != null && tex.height >= 64; // 1.21+ texture is 64x64 with a new layout
            float tw = 64, th = modern ? 64 : 32;
            var holder = new GameObject("magma").transform;
            holder.SetParent(root, false);
            holder.localScale = Vector3.one * size;
            m.Body = Part(m, holder, "body", Pivot(0, 0, 0), mat, b =>
            {
                for (int i = 0; i < 8; i++)
                {
                    int u, v;
                    if (modern) { u = i < 4 ? 0 : 32; v = i < 4 ? 9 * i : 9 * i - 36; }
                    else { u = 0; v = i; if (i == 2) { u = 24; v = 10; } else if (i == 3) { u = 24; v = 19; } }
                    b.McBox(-4, 16 + i, -4, 8, 1, 8, u, v, tw, th);
                }
                if (modern) b.McBox(-2, 18, -2, 4, 4, 4, 24, 40, tw, th);
            });
            m.Head = m.Body;
        }
    }
}

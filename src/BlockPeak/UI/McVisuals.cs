using System.Collections.Generic;
using System.Linq;
using BlockPeak.Assets;
using BlockPeak.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BlockPeak.UI
{
    /// <summary>
    /// Optional looks (switches in the setup program, read when PEAK starts):
    /// - Minecraft particles: PEAK's particle systems get Minecraft's particle textures (smoke, flame, sparks,
    ///   splashes, hearts...), animated frame by frame like Minecraft.
    /// - Minecraft clouds: PEAK's clouds are hidden and Minecraft's blocky cloud layer drifts over the mountain.
    /// </summary>
    public static class McVisuals
    {
        private static bool particles, clouds;
        private static float nextScan;
        private static readonly HashSet<int> done = new HashSet<int>();
        private static readonly Dictionary<string, Material> particleMats = new Dictionary<string, Material>();
        private static readonly Dictionary<string, int> particleFrames = new Dictionary<string, int>();
        private static string scene;

        public static void Init()
        {
            particles = Cfg.McParticles.Value;
            clouds = Cfg.McClouds.Value;
            if (particles || clouds) Plugin.Log.LogInfo($"Minecraft looks: particles {particles}, clouds {clouds}");
        }

        public static void Tick()
        {
            if (!particles && !clouds) return;
            if (McAssets.Status != McAssets.State.Ready) return;
            string s = SceneManager.GetActiveScene().name;
            if (s != scene)
            {
                scene = s;
                done.Clear();
                cloudScanUntil = Time.time + 40f;
                cloudsHidden = 0;
                cloudYSum = 0f;
                cloudHeight = 0f;
                DestroyClouds();
            }
            if (Time.time >= nextScan)
            {
                nextScan = Time.time + 2f;
                if (particles) ScanParticles();
                if (clouds && Time.time < cloudScanUntil) HidePeakClouds();
            }
            if (clouds) TickClouds();
        }

        // ================================================================== particles

        private static void ScanParticles()
        {
            foreach (var r in Object.FindObjectsByType<ParticleSystemRenderer>(FindObjectsSortMode.None))
            {
                if (r == null) continue;
                int id = r.GetInstanceID();
                if (!done.Add(id)) continue;
                try { Swap(r); } catch (System.Exception e) { Health.Verbose("particle swap " + r.name + ": " + e.Message); }
            }
        }

        private static void Swap(ParticleSystemRenderer r)
        {
            if (r.renderMode == ParticleSystemRenderMode.Mesh || r.renderMode == ParticleSystemRenderMode.None) return;
            string n = Describe(r);
            if (n.Contains("blockpeak")) return;
            string kind = Kind(n);
            if (kind == null) return;
            var mat = ParticleMat(kind, out int frames);
            if (mat == null) return;
            r.sharedMaterial = mat;
            var ps = r.GetComponent<ParticleSystem>();
            if (ps == null) return;
            var tsa = ps.textureSheetAnimation;
            if (frames > 1)
            {
                tsa.enabled = true;
                tsa.mode = ParticleSystemAnimationMode.Grid;
                tsa.numTilesX = frames;
                tsa.numTilesY = 1;
                tsa.animation = ParticleSystemAnimationType.WholeSheet;
                tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
                tsa.cycleCount = 1;
            }
            else tsa.enabled = false;
        }

        private static string Describe(ParticleSystemRenderer r)
        {
            var parts = new List<string> { r.name };
            var t = r.transform.parent;
            for (int i = 0; i < 3 && t != null; i++, t = t.parent) parts.Add(t.name);
            var m = r.sharedMaterial;
            if (m != null)
            {
                parts.Add(m.name);
                if (m.mainTexture != null) parts.Add(m.mainTexture.name);
            }
            return string.Join(" ", parts).ToLowerInvariant();
        }

        /// <summary>Which Minecraft particle fits a PEAK effect, by its name.</summary>
        private static string Kind(string n)
        {
            if (n.Contains("cloud") && !n.Contains("fungus")) return clouds ? null : "big_smoke";
            if (n.Contains("fire") || n.Contains("flame") || n.Contains("ember") || n.Contains("torch") || n.Contains("lava") || n.Contains("burn")) return "flame";
            if (n.Contains("explo") || n.Contains("boom") || n.Contains("blast")) return "explosion";
            if (n.Contains("water") || n.Contains("splash") || n.Contains("drip") || n.Contains("rain") || n.Contains("bubble")) return "splash";
            if (n.Contains("spark") || n.Contains("glitter") || n.Contains("star") || n.Contains("shine") || n.Contains("electric") || n.Contains("zap")) return "spark";
            if (n.Contains("heal") || n.Contains("heart")) return "heart";
            if (n.Contains("spore") || n.Contains("poison") || n.Contains("magic") || n.Contains("potion") || n.Contains("buff")) return "effect";
            if (n.Contains("smoke") || n.Contains("steam") || n.Contains("fog") || n.Contains("poof") || n.Contains("puff")) return "big_smoke";
            return "generic"; // dust, snow, debris, everything else
        }

        private static Material ParticleMat(string kind, out int frames)
        {
            if (particleMats.TryGetValue(kind, out var m) && m != null) { frames = particleFrames[kind]; return m; }
            string[] files;
            switch (kind)
            {
                case "flame": files = new[] { "particle/flame.png" }; break;
                case "heart": files = new[] { "particle/heart.png" }; break;
                case "explosion": files = Seq("particle/explosion_", 0, 15, false); break;
                case "splash": files = Seq("particle/splash_", 0, 3, false); break;
                case "spark": files = Seq("particle/spark_", 0, 7, true); break;
                case "effect": files = Seq("particle/effect_", 0, 7, true); break;
                case "big_smoke": files = Seq("particle/big_smoke_", 0, 11, false); break;
                default: files = Seq("particle/generic_", 0, 7, true); break;
            }
            files = files.Where(McAssets.HasTexture).ToArray();
            frames = files.Length;
            if (frames == 0) return null;
            var strip = Strip(files);
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
            if (sh == null) return null;
            m = new Material(sh) { name = "BlockPeak_particle_" + kind };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", strip);
            m.mainTexture = strip;
            Transparent(m);
            particleMats[kind] = m;
            particleFrames[kind] = frames;
            return m;
        }

        private static string[] Seq(string prefix, int a, int b, bool reverse)
        {
            var l = Enumerable.Range(a, b - a + 1).Select(i => prefix + i + ".png").ToList();
            if (reverse) l.Reverse(); // Minecraft plays these frames from the biggest to the smallest
            return l.ToArray();
        }

        private static Texture2D Strip(string[] files)
        {
            var first = McAssets.Tex(files[0]);
            int w = first.width, h = first.height;
            var strip = new Texture2D(w * files.Length, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "mc_strip_" + files[0] };
            for (int i = 0; i < files.Length; i++)
            {
                var t = McAssets.Tex(files[i]);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        strip.SetPixel(i * w + x, y, t.GetPixel(x * t.width / w, y * t.height / h));
            }
            strip.Apply();
            return strip;
        }

        /// <summary>URP particle shader set to alpha blending (keeps PEAK's fade-outs).</summary>
        private static void Transparent(Material m)
        {
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        // ================================================================== clouds

        private static float cloudScanUntil, cloudYSum, cloudHeight, drift;
        private static int cloudsHidden;
        private static Mesh cloudMesh;
        private static Material cloudMat;
        private static readonly List<Transform> cloudTiles = new List<Transform>();
        private const int Grid = 256;

        private static float CellSize => Mathf.Max(1f, Balance.F(Balance.Section("visuals"), "cloudCell", 12f));
        private static float CellHeight => Mathf.Max(0.5f, Balance.F(Balance.Section("visuals"), "cloudThickness", 4f));

        private static void HidePeakClouds()
        {
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r == null || !r.enabled) continue;
                string n = r.name.ToLowerInvariant();
                var m = r.sharedMaterial;
                string mn = m != null ? (m.name + " " + (m.shader != null ? m.shader.name : "")).ToLowerInvariant() : "";
                if (!(n.Contains("cloud") || mn.Contains("cloud")) || n.Contains("fungus") || mn.Contains("fungus") || n.StartsWith("bp_") || n.StartsWith("blockpeak")) continue;
                r.enabled = false;
                cloudsHidden++;
                cloudYSum += r.bounds.center.y;
                Health.Verbose("Hid PEAK cloud renderer " + r.name + " (" + mn + ")");
            }
        }

        private static void TickClouds()
        {
            if (!Game.InRun) { DestroyClouds(); return; }
            var cam = Game.Cam;
            if (cam == null) return;
            if (cloudHeight == 0f)
            {
                if (Cfg.CloudHeight.Value != 0f) cloudHeight = Cfg.CloudHeight.Value;
                else if (cloudsHidden > 0 && Time.time > cloudScanUntil - 30f) cloudHeight = cloudYSum / cloudsHidden;
                else if (Time.time > cloudScanUntil - 30f)
                {
                    float start = SpawnPoint.allSpawnPoints != null && SpawnPoint.allSpawnPoints.Count > 0 && SpawnPoint.allSpawnPoints[0] != null ? SpawnPoint.allSpawnPoints[0].transform.position.y : 0f;
                    cloudHeight = start + Balance.F(Balance.Section("visuals"), "cloudHeightAboveStart", 260f);
                }
                else return;
                Plugin.Log.LogInfo($"Minecraft clouds at height {cloudHeight:0} (PEAK cloud renderers hidden: {cloudsHidden}).");
            }
            if (!EnsureClouds()) return;
            drift += Time.deltaTime * Balance.F(Balance.Section("visuals"), "cloudSpeed", 0.6f);
            float tile = Grid * CellSize;
            Vector3 c = cam.transform.position;
            float ox = Mathf.Floor((c.x - drift - tile * 0.5f) / tile) * tile + drift;
            float oz = Mathf.Floor((c.z - tile * 0.5f) / tile) * tile;
            for (int i = 0; i < cloudTiles.Count; i++)
            {
                var t = cloudTiles[i];
                if (t == null) continue;
                t.position = new Vector3(ox + (i % 2) * tile, cloudHeight, oz + (i / 2) * tile);
            }
        }

        private static bool EnsureClouds()
        {
            if (cloudTiles.Count == 4 && cloudTiles.All(t => t != null)) return true;
            DestroyClouds();
            if (!McAssets.HasTexture("environment/clouds.png")) return false;
            if (cloudMesh == null) cloudMesh = BuildCloudMesh(McAssets.Tex("environment/clouds.png"));
            if (cloudMesh == null) return false;
            if (cloudMat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
                cloudMat = new Material(sh) { name = "BlockPeak_clouds" };
                if (cloudMat.HasProperty("_BaseColor")) cloudMat.SetColor("_BaseColor", Color.white);
                Transparent(cloudMat);
            }
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("BlockPeak.Clouds" + i);
                Object.DontDestroyOnLoad(go);
                go.AddComponent<MeshFilter>().sharedMesh = cloudMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = cloudMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                cloudTiles.Add(go.transform);
            }
            return true;
        }

        private static void DestroyClouds()
        {
            foreach (var t in cloudTiles) if (t != null) Object.Destroy(t.gameObject);
            cloudTiles.Clear();
        }

        /// <summary>Minecraft's "fancy" clouds: each white pixel of clouds.png is a 12 x 4 x 12 box.</summary>
        private static Mesh BuildCloudMesh(Texture2D tex)
        {
            bool[,] on = new bool[Grid, Grid];
            for (int z = 0; z < Grid; z++)
                for (int x = 0; x < Grid; x++)
                {
                    var c = tex.GetPixel(x * tex.width / Grid, z * tex.height / Grid);
                    on[x, z] = c.a > 0.5f && c.grayscale > 0.5f;
                }
            float cs = CellSize, ch = CellHeight;
            var v = new List<Vector3>();
            var col = new List<Color>();
            var tri = new List<int>();
            const float A = 0.8f;
            Color top = new Color(1f, 1f, 1f, A), bottom = new Color(0.7f, 0.7f, 0.7f, A), sideX = new Color(0.9f, 0.9f, 0.9f, A), sideZ = new Color(0.8f, 0.8f, 0.8f, A);
            void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color k)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(cc); v.Add(d);
                col.Add(k); col.Add(k); col.Add(k); col.Add(k);
                tri.Add(i); tri.Add(i + 1); tri.Add(i + 2);
                tri.Add(i); tri.Add(i + 2); tri.Add(i + 3);
            }
            for (int z = 0; z < Grid; z++)
            {
                int x = 0;
                while (x < Grid)
                {
                    if (!on[x, z]) { x++; continue; }
                    int x0 = x;
                    while (x < Grid && on[x, z]) x++;
                    float a = x0 * cs, b = x * cs, z0 = z * cs, z1 = (z + 1) * cs;
                    // top (seen from above) and bottom (seen from below), merged along each row
                    Quad(new Vector3(a, ch, z0), new Vector3(a, ch, z1), new Vector3(b, ch, z1), new Vector3(b, ch, z0), top);
                    Quad(new Vector3(a, 0, z0), new Vector3(b, 0, z0), new Vector3(b, 0, z1), new Vector3(a, 0, z1), bottom);
                }
            }
            for (int z = 0; z < Grid; z++)
                for (int x = 0; x < Grid; x++)
                {
                    if (!on[x, z]) continue;
                    float x0 = x * cs, x1 = (x + 1) * cs, z0 = z * cs, z1 = (z + 1) * cs;
                    if (!on[(x + 1) % Grid, z]) Quad(new Vector3(x1, 0, z0), new Vector3(x1, ch, z0), new Vector3(x1, ch, z1), new Vector3(x1, 0, z1), sideX);
                    if (!on[(x + Grid - 1) % Grid, z]) Quad(new Vector3(x0, 0, z1), new Vector3(x0, ch, z1), new Vector3(x0, ch, z0), new Vector3(x0, 0, z0), sideX);
                    if (!on[x, (z + 1) % Grid]) Quad(new Vector3(x1, 0, z1), new Vector3(x1, ch, z1), new Vector3(x0, ch, z1), new Vector3(x0, 0, z1), sideZ);
                    if (!on[x, (z + Grid - 1) % Grid]) Quad(new Vector3(x0, 0, z0), new Vector3(x0, ch, z0), new Vector3(x1, ch, z0), new Vector3(x1, 0, z0), sideZ);
                }
            if (v.Count == 0) return null;
            var m = new Mesh { name = "mc_clouds", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(v);
            m.SetColors(col);
            m.SetTriangles(tri, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            Plugin.Log.LogInfo($"Minecraft cloud mesh: {v.Count} vertices.");
            return m;
        }
    }
}

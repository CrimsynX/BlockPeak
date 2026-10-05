using System.Collections.Generic;
using BlockPeak.Assets;
using BlockPeak.Core;
using UnityEngine;

namespace BlockPeak.UI
{
    /// <summary>Tiny Minecraft-style particle effects (cubes that fly and fall) and floating markers.</summary>
    public static class Fx
    {
        private class Particle
        {
            public Transform T;
            public Vector3 V;
            public float Age, Life, Size;
            public bool Gravity;
        }

        private class MarkerFx
        {
            public Transform T;
            public int Actor;
            public float Until;
        }

        private class Puff
        {
            public SpriteRenderer R;
            public float Age, Life;
            public Sprite[] Frames;
        }

        private static Sprite[] sonicFrames;

        /// <summary>Minecraft's warden sonic boom: a line of expanding cyan rings from the warden to its target.</summary>
        public static void SonicBoom(Vector3 from, Vector3 to)
        {
            try
            {
                Ensure();
                if (sonicFrames == null)
                {
                    var list = new List<Sprite>();
                    for (int i = 0; i < 16; i++)
                    {
                        string path = "particle/sonic_boom_" + i + ".png";
                        if (!McAssets.HasTexture(path)) continue;
                        var t = McAssets.Tex(path);
                        list.Add(Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), t.width));
                    }
                    sonicFrames = list.ToArray();
                }
                if (sonicFrames.Length == 0) { Burst(Vector3.Lerp(from, to, 0.5f), new[] { new Color(0.2f, 0.9f, 0.9f) }, 20, 2f, 0.6f, false); return; }
                float len = Vector3.Distance(from, to);
                int n = Mathf.Clamp(Mathf.RoundToInt(len / 1.2f), 2, 24);
                for (int i = 1; i <= n && puffs.Count < 80; i++)
                {
                    var go = new GameObject("fx_sonic");
                    go.transform.SetParent(root.transform, false);
                    go.transform.position = Vector3.Lerp(from, to, i / (float)n);
                    go.transform.localScale = Vector3.one * 1.6f;
                    var r = go.AddComponent<SpriteRenderer>();
                    r.sprite = sonicFrames[0];
                    puffs.Add(new Puff { R = r, Life = 0.6f, Age = -i * 0.02f, Frames = sonicFrames });
                }
            }
            catch (System.Exception e) { Health.Report("fx-sonic", e); }
        }

        private static readonly List<Particle> particles = new List<Particle>();
        private static readonly List<Puff> puffs = new List<Puff>();
        private static Sprite[] explosionFrames;
        private static readonly List<MarkerFx> markers = new List<MarkerFx>();
        private static Mesh cube;
        private static GameObject root;
        private static readonly Dictionary<Color, Material> solid = new Dictionary<Color, Material>();
        private const int Max = 400;

        private static void Ensure()
        {
            if (root == null)
            {
                root = new GameObject("BlockPeak.Fx");
                Object.DontDestroyOnLoad(root);
            }
            if (cube == null)
            {
                var mb = new MeshBuilder();
                var r = new Rect(0, 0, 1, 1);
                mb.Box(new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f), new[] { r, r, r, r, r, r });
                cube = mb.Build("fx_cube");
            }
        }

        private static Material ColorMat(Color c)
        {
            c = new Color(Mathf.Round(c.r * 20) / 20, Mathf.Round(c.g * 20) / 20, Mathf.Round(c.b * 20) / 20, 1);
            if (solid.TryGetValue(c, out var m) && m != null) return m;
            m = Mat.Solid(c);
            solid[c] = m;
            return m;
        }

        public static void Burst(Vector3 at, Color[] colors, int count, float speed, float life, bool gravity = true, float size = 0.08f)
        {
            try
            {
                Ensure();
                for (int i = 0; i < count && particles.Count < Max; i++)
                {
                    var go = new GameObject("fx");
                    go.transform.SetParent(root.transform, false);
                    go.transform.position = at + Random.insideUnitSphere * 0.2f;
                    go.AddComponent<MeshFilter>().sharedMesh = cube;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = ColorMat(colors[Random.Range(0, colors.Length)]);
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    float s = size * Random.Range(0.7f, 1.3f);
                    go.transform.localScale = Vector3.one * s;
                    particles.Add(new Particle { T = go.transform, V = Random.onUnitSphere * speed * Random.Range(0.3f, 1f) + Vector3.up * speed * 0.3f, Life = life * Random.Range(0.6f, 1.2f), Size = s, Gravity = gravity });
                }
            }
            catch (System.Exception e) { Health.Report("fx", e); }
        }

        /// <summary>
        /// Minecraft's explosion: a handful of animated grey puffs (particle/explosion_0..15) and a quick flash.
        /// Very cheap on purpose (no physics particles, no PEAK effect prefabs).
        /// </summary>
        public static void Explosion(Vector3 at, float radius, bool light = false)
        {
            try
            {
                Ensure();
                if (explosionFrames == null)
                {
                    var list = new List<Sprite>();
                    for (int i = 0; i < 16; i++)
                    {
                        var t = McAssets.Tex("particle/explosion_" + i + ".png");
                        list.Add(Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), t.width));
                    }
                    explosionFrames = list.ToArray();
                }
                int count = light ? 4 : Mathf.Clamp(Mathf.RoundToInt(radius * 3f), 6, 12);
                for (int i = 0; i < count && puffs.Count < 60; i++)
                {
                    var go = new GameObject("fx_boom");
                    go.transform.SetParent(root.transform, false);
                    go.transform.position = at + Random.insideUnitSphere * radius * 0.6f;
                    go.transform.localScale = Vector3.one * radius * Random.Range(0.5f, 0.9f);
                    var r = go.AddComponent<SpriteRenderer>();
                    r.sprite = explosionFrames[0];
                    r.color = new Color(0.9f, 0.9f, 0.9f, 1f);
                    puffs.Add(new Puff { R = r, Life = Random.Range(0.45f, 0.8f), Age = -Random.Range(0f, 0.15f) });
                }
                if (light) return; // rain TNT: puffs only
                var lightGo = new GameObject("fx_flash");
                lightGo.transform.SetParent(root.transform, false);
                lightGo.transform.position = at;
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = radius * 4f;
                l.intensity = 4f;
                l.color = new Color(1f, 0.9f, 0.7f);
                l.shadows = LightShadows.None;
                Object.Destroy(lightGo, 0.12f);
                Burst(at, new[] { new Color(0.35f, 0.35f, 0.35f), new Color(0.55f, 0.55f, 0.55f) }, 8, radius * 1.5f, 0.9f, true, 0.12f);
            }
            catch (System.Exception e) { Health.Report("fx-explosion", e); }
        }

        public static void WaterSplash(Vector3 at)
        {
            Burst(at + Vector3.up * 0.2f, new[] { new Color(0.25f, 0.4f, 0.95f), new Color(0.45f, 0.6f, 1f), new Color(0.8f, 0.9f, 1f) }, 30, 4f, 1f);
        }

        public static void BlockBreak(Vector3 at, Texture2D tex, float size)
        {
            var cols = new List<Color>();
            try
            {
                for (int i = 0; i < 6; i++)
                {
                    var c = tex.GetPixel(Random.Range(0, tex.width), Random.Range(0, tex.height));
                    if (c.a > 0.3f) cols.Add(c);
                }
            }
            catch { }
            if (cols.Count == 0) cols.Add(Color.gray);
            Burst(at, cols.ToArray(), 16, 2.5f, 0.8f, true, 0.06f * size / 0.5f);
        }

        /// <summary>Float an icon above a player for a while (goat horn).</summary>
        public static void Marker(int actor, Texture2D icon, float seconds)
        {
            try
            {
                Ensure();
                var go = new GameObject("fx_marker");
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = Meshes.ItemSprite(icon);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Mat.Glowing(icon, new Color(0.6f, 0.6f, 0.6f));
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.transform.localScale = Vector3.one * 0.7f;
                markers.Add(new MarkerFx { T = go.transform, Actor = actor, Until = Time.time + seconds });
            }
            catch (System.Exception e) { Health.Report("fx-marker", e); }
        }

        public static void Tick()
        {
            float dt = Time.deltaTime;
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var p = particles[i];
                if (p.T == null) { particles.RemoveAt(i); continue; }
                p.Age += dt;
                if (p.Age >= p.Life) { Object.Destroy(p.T.gameObject); particles.RemoveAt(i); continue; }
                if (p.Gravity) p.V += Physics.gravity * 0.6f * dt;
                p.V *= 1f - 0.8f * dt;
                p.T.position += p.V * dt;
                p.T.localScale = Vector3.one * p.Size * (1f - p.Age / p.Life * 0.7f);
            }
            var camT = Game.Cam != null ? Game.Cam.transform : null;
            for (int i = puffs.Count - 1; i >= 0; i--)
            {
                var p = puffs[i];
                if (p.R == null) { puffs.RemoveAt(i); continue; }
                p.Age += dt;
                if (p.Age >= p.Life) { Object.Destroy(p.R.gameObject); puffs.RemoveAt(i); continue; }
                p.R.enabled = p.Age >= 0f;
                if (p.Age < 0f) continue;
                var frames = p.Frames ?? explosionFrames;
                int frame = Mathf.Clamp((int)(p.Age / p.Life * frames.Length), 0, frames.Length - 1);
                p.R.sprite = frames[frame];
                if (camT != null) p.R.transform.rotation = camT.rotation;
            }
            for (int i = markers.Count - 1; i >= 0; i--)
            {
                var m = markers[i];
                Character who = null;
                foreach (var c in Character.AllCharacters)
                    if (c != null && c.photonView != null && c.photonView.Owner != null && c.photonView.Owner.ActorNumber == m.Actor) { who = c; break; }
                if (m.T == null || Time.time > m.Until || who == null)
                {
                    if (m.T != null) Object.Destroy(m.T.gameObject);
                    markers.RemoveAt(i);
                    continue;
                }
                m.T.position = who.Head + Vector3.up * (1.3f + Mathf.Sin(Time.time * 3f) * 0.1f);
                var cam = Game.Cam;
                if (cam != null) m.T.rotation = Quaternion.LookRotation(m.T.position - cam.transform.position);
            }
        }

        public static void Clear()
        {
            foreach (var p in particles) if (p.T != null) Object.Destroy(p.T.gameObject);
            particles.Clear();
            foreach (var m in markers) if (m.T != null) Object.Destroy(m.T.gameObject);
            markers.Clear();
            foreach (var p in puffs) if (p.R != null) Object.Destroy(p.R.gameObject);
            puffs.Clear();
        }
    }
}

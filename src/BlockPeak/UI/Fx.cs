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

        private static readonly List<Particle> particles = new List<Particle>();
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
        }
    }
}

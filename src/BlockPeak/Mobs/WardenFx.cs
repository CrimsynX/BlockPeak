using System.Linq;
using BlockPeak.Assets;
using BlockPeak.Core;
using BlockPeak.Net;
using BlockPeak.UI;
using UnityEngine;

namespace BlockPeak.Mobs
{
    /// <summary>
    /// What makes the warden scary, on every player's own screen:
    /// - Minecraft's Darkness effect: the screen pulses dark the closer the warden is (always a little during Warden
    ///   Chase, because it is out there somewhere),
    /// - the sculk "nearby" warnings as it closes in (close, closer, closest),
    /// - heavy footsteps that shake the camera, a ground-shaking roar and emergence, and the sonic boom rings.
    /// </summary>
    public static class WardenFx
    {
        private static Texture2D vignette;
        private static float darkness, spike;
        private static int warned;            // 0 none, 1 close, 2 closer, 3 closest
        private static float warnCooldown, stepAt;

        public static void RegisterNet()
        {
            Channel.On(Op.SonicBoom, (a, s) =>
            {
                Vector3 from = Channel.Vec(a[0]), to = Channel.Vec(a[1]);
                Fx.SonicBoom(from, to);
                Shake(to, 1.6f, 0.4f, 25f);
                var me = Game.LocalChar;
                if (me != null && Vector3.Distance(me.Center, to) < 4f) spike = 1f;
            });
        }

        /// <summary>The warden climbs out of the ground (everyone).</summary>
        public static void Emerge(Vector3 at)
        {
            Fx.Burst(at + Vector3.up * 0.3f, new[] { new Color(0.25f, 0.18f, 0.12f), new Color(0.1f, 0.1f, 0.12f), new Color(0.05f, 0.25f, 0.28f) }, 40, 4f, 1.4f);
            Shake(at, 2.5f, 3f, 60f);
            spike = 1f;
        }

        public static void Roar(Vector3 at)
        {
            Shake(at, 2f, 1.8f, 40f);
            var me = Game.LocalChar;
            if (me != null && Vector3.Distance(me.Center, at) < 30f) spike = 1f;
        }

        public static void Shake(Vector3 at, float amount, float seconds, float range)
        {
            try { if (GamefeelHandler.instance != null) GamefeelHandler.instance.AddPerlinShakeProximity(at, amount, seconds, 15f, range); }
            catch { }
        }

        public static void Tick()
        {
            float dt = Time.deltaTime;
            warnCooldown -= dt;
            spike = Mathf.Max(0f, spike - dt * 0.25f);
            BodyMob nearest = null;
            float d = float.MaxValue;
            var cam = Game.Cam;
            if (cam == null) { darkness = Mathf.MoveTowards(darkness, 0f, dt); return; }
            Vector3 eye = cam.transform.position;
            foreach (var m in BodyMobs.All.Values)
            {
                if (m == null || m.Dying || m.Type != "warden") continue;
                float md = Vector3.Distance(m.Position, eye);
                if (md < d) { d = md; nearest = m; }
            }
            bool chase = Modes.GameModes.Active == Modes.ModeKind.WardenChase;
            float target = 0f;
            if (nearest != null)
            {
                float near = Mathf.Clamp01((Balance.F(W, "darknessRange", 40f) - d) / 30f);
                bool angry = (nearest.Flags & 8) != 0;
                target = Mathf.Max(chase ? Balance.F(W, "chaseDarkness", 0.2f) : 0f, near * (angry ? 1f : 0.75f));

                // Footsteps you can feel.
                if (nearest.C != null && nearest.C.data.avarageVelocity.sqrMagnitude > 0.5f && d < 30f && Time.time > stepAt)
                {
                    stepAt = Time.time + (angry ? 0.42f : 0.7f);
                    Sfx.At("mob/warden/step", nearest.Position, 1f, 1f, 32f);
                    Shake(nearest.Position, 0.5f, 0.15f, 14f);
                }

                // Sculk warnings as it closes in (Warden Chase).
                int level = d < 10f ? 3 : d < 20f ? 2 : d < 32f ? 1 : 0;
                if (level > warned && warnCooldown <= 0f)
                {
                    warnCooldown = 4f;
                    warned = level; // only counts as warned once the sound actually played
                    string snd = level == 3 ? "mob/warden/nearby_closest" : level == 2 ? "mob/warden/nearby_closer" : "mob/warden/nearby_close";
                    Sfx.Ui(snd, 0.9f);
                }
                if (level < warned - 1 || level == 0) warned = level;
            }
            else warned = 0;
            target = Mathf.Max(target, spike);
            darkness = Mathf.MoveTowards(darkness, target, dt * 0.8f);
        }

        private static Newtonsoft.Json.Linq.JToken W => Balance.Section("mobs")["warden"] ?? new Newtonsoft.Json.Linq.JObject();

        /// <summary>Minecraft's Darkness effect: a pulsing dark vignette that closes in.</summary>
        public static void Draw()
        {
            if (darkness <= 0.01f || Event.current.type != EventType.Repaint) return;
            if (vignette == null) vignette = MakeVignette();
            // Minecraft's darkness pulses roughly every 2.5 seconds.
            float pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.PI * 2f / 2.5f));
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(darkness * pulse * Balance.F(W, "darknessStrength", 0.92f)));
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), vignette);
            GUI.color = old;
        }

        private static Texture2D MakeVignette()
        {
            const int N = 128;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / 1.2f;
                    float a = Mathf.Lerp(0.72f, 1f, Mathf.SmoothStep(0f, 1f, r));
                    px[y * N + x] = new Color32(0, 0, 0, (byte)(a * 255));
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }
    }
}

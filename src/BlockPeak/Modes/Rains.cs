using System;
using System.Collections.Generic;
using BlockPeak.Assets;
using BlockPeak.Building;
using BlockPeak.Core;
using BlockPeak.Mobs;
using BlockPeak.Net;
using BlockPeak.UI;
using Photon.Pun;
using UnityEngine;

namespace BlockPeak.Modes
{
    /// <summary>
    /// Anvil rain and TNT rain (custom-run check boxes). The host decides when it rains and where each anvil/TNT
    /// starts; every player then drops their own copy with real physics. Each player only takes damage from their own
    /// copies, and the host's copies break blocks and hurt mobs. Everything despawns after a while.
    /// </summary>
    public static class Rains
    {
        private static bool anvilOn, tntOn;
        private static float nextAnvil = -1f, nextTnt = -1f, anvilUntil = -1f, tntUntil = -1f;
        private static float lastStep;
        private static readonly List<Falling> alive = new List<Falling>();
        private static GameObject root;
        private static Mesh anvilMesh, tntMesh;
        private static Material anvilMat, tntMat, flashMat;

        private static Newtonsoft.Json.Linq.JToken R => Balance.Section("modes")["rain"] ?? new Newtonsoft.Json.Linq.JObject();
        private static bool IsHost => !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode;

        public static void RegisterNet()
        {
            Channel.On(Op.RainSpawn, (a, s) =>
            {
                bool tnt = Channel.Bool(a[0]);
                var p = a[1] as float[];
                if (p == null) return;
                for (int i = 0; i + 3 < p.Length; i += 4)
                    Spawn(tnt, new Vector3(p[i], p[i + 1], p[i + 2]), p[i + 3]);
            });
        }

        public static void Reset()
        {
            anvilOn = tntOn = false;
            nextAnvil = nextTnt = anvilUntil = tntUntil = -1f;
            Clear();
        }

        public static void Configure(bool anvil, bool tnt)
        {
            anvilOn = anvil;
            tntOn = tnt;
            float first = Balance.F(R, "firstDelay", 45f);
            if (anvil) nextAnvil = Time.time + first + UnityEngine.Random.Range(0f, 30f);
            if (tnt) nextTnt = Time.time + first + UnityEngine.Random.Range(15f, 60f);
            if (anvil || tnt) Plugin.Log.LogInfo($"Rain: anvils {anvil}, TNT {tnt}");
        }

        // ------------------------------------------------------------------ host: when and where

        public static void TickHost()
        {
            if (!anvilOn && !tntOn) return;
            float now = Time.time;
            float min = Balance.F(R, "everyMin", 60f), max = Balance.F(R, "everyMax", 180f), len = Balance.F(R, "seconds", 12f);

            if (anvilOn && nextAnvil > 0f && now >= nextAnvil)
            {
                anvilUntil = now + len;
                nextAnvil = anvilUntil + UnityEngine.Random.Range(min, max);
                Channel.All(Op.ModeMessage, true, "It's raining anvils!", 3f, "random/anvil_land");
            }
            if (tntOn && nextTnt > 0f && now >= nextTnt)
            {
                tntUntil = now + len;
                nextTnt = tntUntil + UnityEngine.Random.Range(min, max);
                Channel.All(Op.ModeMessage, true, "It's raining TNT!", 3f, "random/fuse");
            }

            const float step = 0.25f;
            if (now - lastStep < step) return;
            lastStep = now;
            if (now < anvilUntil) Drop(false, Balance.F(R, "anvilsPerSecond", 1f) * step);
            if (now < tntUntil) Drop(true, Balance.F(R, "tntPerSecond", 0.7f) * step);
        }

        private static readonly List<float> batch = new List<float>();

        private static void Drop(bool tnt, float chance)
        {
            batch.Clear();
            float spread = Balance.F(R, "radius", 12f), height = Balance.F(R, "height", 22f);
            foreach (var c in BodyMobs.Players)
            {
                // More than one per step when the rate is above 4 per second.
                float n = chance;
                while (n > 0f)
                {
                    if (UnityEngine.Random.value < Mathf.Min(1f, n) && PickSpot(c.Center, spread, height, out var at))
                        batch.AddRange(new[] { at.x, at.y, at.z, UnityEngine.Random.Range(0f, 360f) });
                    n -= 1f;
                }
            }
            if (batch.Count > 0) Channel.All(Op.RainSpawn, true, tnt, batch.ToArray());
        }

        /// <summary>A start point above a player with open sky above it (no anvils inside rock or under overhangs).</summary>
        private static bool PickSpot(Vector3 player, float spread, float height, out Vector3 at)
        {
            Vector2 r = UnityEngine.Random.insideUnitCircle * spread;
            Vector3 ground = new Vector3(player.x + r.x, player.y + 1f, player.z + r.y);
            at = ground + Vector3.up * height;
            if (Physics.Raycast(ground, Vector3.up, out var hit, height, Game.TerrainMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance < 6f) return false;
                at = ground + Vector3.up * (hit.distance - 1f);
            }
            return true;
        }

        // ------------------------------------------------------------------ everyone: the falling things

        private class Falling : MonoBehaviour
        {
            public bool Tnt;
            public float Born, DieAt, ExplodeAt = -1f;
            public bool Landed, HitMe;
            public MeshRenderer Mr;
            private float flashNext;
            private bool flashOn;

            private void OnCollisionEnter(Collision col)
            {
                try
                {
                    float speed = col.relativeVelocity.magnitude;
                    var me = Game.LocalChar;
                    var who = col.collider != null ? col.collider.GetComponentInParent<Character>() : null;

                    if (who != null && who == me && !HitMe && !Tnt && speed > Balance.F(R, "minHitSpeed", 5f) && !me.data.dead)
                    {
                        // An anvil on the head: injury that grows with the speed, and you fall over.
                        HitMe = true;
                        float k = Mathf.Clamp01(speed / 20f);
                        me.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Injury, Balance.F(R, "anvilInjury", 0.25f) * (0.4f + 0.6f * k));
                        me.Fall(Balance.F(R, "anvilFallSeconds", 2f));
                        Sfx.At("random/anvil_land", transform.position, 1f, 1f, 48f);
                    }
                    if (who != null && who != me && who.isBot && IsHost && !Tnt && speed > 6f && !Landed)
                        MobDirector.HostExplosion(transform.position, 0.8f);

                    if (!Landed && speed > 2f)
                    {
                        Landed = true;
                        if (!Tnt) Sfx.At("random/anvil_land", transform.position, 0.9f, UnityEngine.Random.Range(0.9f, 1.1f), 48f);
                        else if (ExplodeAt < 0f)
                        {
                            ExplodeAt = Time.time + UnityEngine.Random.Range(Balance.F(R, "tntFuseMin", 0.5f), Balance.F(R, "tntFuseMax", 2f));
                            Sfx.At("random/fuse", transform.position, 0.6f, 1f, 32f);
                        }
                    }
                }
                catch (Exception e) { Health.Report("rain-hit", e); }
            }

            private void Update()
            {
                float now = Time.time;
                if (Tnt)
                {
                    if (now >= flashNext && Mr != null)
                    {
                        flashOn = !flashOn;
                        flashNext = now + (ExplodeAt > 0f && ExplodeAt - now < 0.6f ? 0.08f : 0.25f);
                        Mr.sharedMaterial = flashOn ? flashMat : tntMat;
                    }
                    // Explodes on the ground (after a short fuse) or in the air after a while anyway.
                    if (ExplodeAt < 0f && now - Born > Balance.F(R, "tntMaxSeconds", 8f)) ExplodeAt = now;
                    if (ExplodeAt > 0f && now >= ExplodeAt) { Explode(); return; }
                }
                if (now >= DieAt || transform.position.y < -2000f) { Remove(this); return; }
                if (!Tnt && DieAt - now < 0.5f) transform.localScale = Vector3.one * BlockWorld.Size * Mathf.Max(0.05f, (DieAt - now) / 0.5f);
            }

            private void Explode()
            {
                Vector3 at = transform.position + Vector3.up * BlockWorld.Size * 0.5f;
                float radius = Balance.F(R, "tntRadius", 2.5f);
                Explosions.OnExplosion(at, radius, Balance.F(R, "tntInjury", 0.25f), true, Balance.F(R, "tntKnockback", 16f));
                if (IsHost) BlockWorld.HostExplosionAftermath(at, radius);
                Remove(this);
            }
        }

        private static void Spawn(bool tnt, Vector3 at, float yaw)
        {
            try
            {
                if (!Game.InRun) return;
                Ensure();
                int cap = Balance.I(R, "maxAlive", 60);
                while (alive.Count >= cap && alive.Count > 0) Remove(alive[0]);

                var go = new GameObject(tnt ? "bp_rain_tnt" : "bp_rain_anvil");
                go.transform.SetParent(root.transform, false);
                go.transform.position = at;
                go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                go.transform.localScale = Vector3.one * BlockWorld.Size;
                go.layer = Game.DefaultLayer;
                go.AddComponent<MeshFilter>().sharedMesh = tnt ? tntMesh : anvilMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = tnt ? tntMat : anvilMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

                var box = go.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.5f, 0f);
                box.size = tnt ? Vector3.one : new Vector3(0.75f, 1f, 1f);
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = tnt ? Balance.F(R, "tntMass", 8f) : Balance.F(R, "anvilMass", 40f);
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.angularDamping = 0.5f;
                rb.linearVelocity = Vector3.down * 4f;
                if (tnt) rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 2f;

                var f = go.AddComponent<Falling>();
                f.Tnt = tnt;
                f.Mr = mr;
                f.Born = Time.time;
                f.DieAt = Time.time + (tnt ? 30f : Balance.F(R, "anvilLifeSeconds", 27f));
                alive.Add(f);
            }
            catch (Exception e) { Health.Report("rain-spawn", e); }
        }

        private static void Remove(Falling f)
        {
            alive.Remove(f);
            if (f != null) UnityEngine.Object.Destroy(f.gameObject);
        }

        public static void Clear()
        {
            foreach (var f in alive) if (f != null) UnityEngine.Object.Destroy(f.gameObject);
            alive.Clear();
        }

        // ------------------------------------------------------------------ models

        private static void Ensure()
        {
            if (root == null)
            {
                root = new GameObject("BlockPeak.Rain");
                UnityEngine.Object.DontDestroyOnLoad(root);
            }
            if (anvilMesh == null)
            {
                anvilMesh = AnvilMesh();
                anvilMat = Mat.For(Meshes.BlockAtlas(McAssets.Tex("block/anvil.png"), McAssets.Tex("block/anvil_top.png"), McAssets.Tex("block/anvil.png")));
            }
            if (tntMesh == null)
            {
                tntMesh = Meshes.Cube(1f, "mc_rain_tnt");
                var atlas = Meshes.BlockAtlas(McAssets.Tex("block/tnt_side.png"), McAssets.Tex("block/tnt_top.png"), McAssets.Tex("block/tnt_bottom.png"));
                tntMat = Mat.For(atlas);
                flashMat = Mat.Glowing(Texture2D.whiteTexture, Color.white);
            }
        }

        /// <summary>Minecraft's anvil model (block/anvil.json): base, waist, neck and the long top, in a 1-unit cell.</summary>
        private static Mesh AnvilMesh()
        {
            var mb = new MeshBuilder();
            // Atlas strip: side texture in the first third, top texture in the second.
            Rect Side(float x0, float y0, float x1, float y1) => Sub(0, x0, y0, x1, y1);
            Rect Top(float x0, float y0, float x1, float y1) => Sub(1, x0, y0, x1, y1);
            void Part(float x0, float y0, float z0, float x1, float y1, float z1, bool topTex)
            {
                var min = new Vector3(x0 / 16f - 0.5f, y0 / 16f, z0 / 16f - 0.5f);
                var max = new Vector3(x1 / 16f - 0.5f, y1 / 16f, z1 / 16f - 0.5f);
                Rect sx = Side(z0, y0, z1, y1);            // faces looking along x
                Rect sz = Side(x0, y0, x1, y1);            // faces looking along z
                Rect up = topTex ? Top(x0, z0, x1, z1) : Side(x0, z0, x1, z1);
                Rect down = Side(x0, z0, x1, z1);
                mb.Box(min, max, new[] { sx, sx, up, down, sz, sz });
            }
            Part(2, 0, 2, 14, 4, 14, false);
            Part(4, 4, 3, 12, 5, 13, false);
            Part(6, 5, 4, 10, 10, 12, false);
            Part(3, 10, 0, 13, 16, 16, true);
            return mb.Build("mc_anvil");
        }

        private static Rect Sub(int third, float x0, float y0, float x1, float y1)
        {
            const float e = 0.001f;
            float u0 = (third + x0 / 16f) / 3f, u1 = (third + x1 / 16f) / 3f;
            return Rect.MinMaxRect(u0 + e, y0 / 16f + e, u1 - e, y1 / 16f - e);
        }
    }
}

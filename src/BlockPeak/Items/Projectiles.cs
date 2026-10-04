using System.Collections.Generic;
using BlockPeak.Assets;
using BlockPeak.Core;
using BlockPeak.Mobs;
using BlockPeak.Net;
using BlockPeak.UI;
using Photon.Pun;
using UnityEngine;

namespace BlockPeak.Items
{
    public enum ProjectileKind : byte { Pearl = 1, WindCharge = 2, Arrow = 3 }

    /// <summary>
    /// Thrown ender pearls, wind charges and skeleton arrows. The thrower's game decides what they hit
    /// (the host for arrows); everyone else just sees the same flight.
    /// </summary>
    public static class Projectiles
    {
        private class Shot
        {
            public ProjectileKind Kind;
            public Vector3 Pos, Vel;
            public float Age;
            public bool Authority;
            public int Owner;          // actor that threw it
            public Transform View;
            public string MobType;     // arrows: who shot it (status effects)
        }

        private static readonly List<Shot> shots = new List<Shot>();
        private static GameObject root;
        private static Mesh arrowMesh;

        public static void RegisterNet()
        {
            Channel.On(Op.Projectile, (a, s) =>
            {
                if (s == (PhotonNetwork.LocalPlayer?.ActorNumber ?? -1)) return;
                Spawn((ProjectileKind)Channel.Byte(a[0]), Channel.Vec(a[1]), Channel.Vec(a[2]), false, s, null);
            });
            Channel.On(Op.Arrow, (a, s) =>
            {
                if (PhotonNetwork.IsMasterClient) return;
                Spawn(ProjectileKind.Arrow, Channel.Vec(a[0]), Channel.Vec(a[1]), false, s, null);
            });
            Channel.On(Op.WindBurst, (a, s) => OnWindBurst(Channel.Vec(a[0]), Channel.Flt(a[1]), Channel.Flt(a[2])));
            Channel.On(Op.Sound, (a, s) =>
            {
                string name = Channel.Str(a[0]);
                Vector3 pos = Channel.Vec(a[1]);
                Sfx.At(name, pos, a.Length > 2 ? Channel.Flt(a[2]) : 1f);
                if (name == "random/chestopen")
                    Fx.Burst(pos + Vector3.up * 0.5f, new[] { new Color(0.55f, 0.36f, 0.18f), new Color(0.9f, 0.75f, 0.3f) }, 18, 2.5f, 0.9f);
            });
        }

        /// <summary>Local player throws something.</summary>
        public static void Throw(ProjectileKind kind, Vector3 from, Vector3 vel)
        {
            int me = PhotonNetwork.LocalPlayer?.ActorNumber ?? 0;
            Spawn(kind, from, vel, true, me, null);
            Channel.Others(Op.Projectile, true, (byte)kind, from, vel);
        }

        /// <summary>Host: a mob fires an arrow.</summary>
        public static void FireArrow(Vector3 from, Vector3 vel, string mobType)
        {
            Spawn(ProjectileKind.Arrow, from, vel, true, 0, mobType);
            Channel.Others(Op.Arrow, false, from, vel);
            Sfx.At("random/bow", from, 0.7f);
        }

        private static void Spawn(ProjectileKind kind, Vector3 from, Vector3 vel, bool authority, int owner, string mobType)
        {
            if (root == null) { root = new GameObject("BlockPeak.Projectiles"); Object.DontDestroyOnLoad(root); }
            var go = new GameObject("mc_" + kind);
            go.transform.SetParent(root.transform, false);
            go.transform.position = from;
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (kind == ProjectileKind.Arrow)
            {
                if (arrowMesh == null)
                {
                    var mb = new MeshBuilder();
                    var r = new Rect(0, 0, 1, 1);
                    mb.Box(new Vector3(-0.025f, -0.025f, -0.35f), new Vector3(0.025f, 0.025f, 0.35f), new[] { r, r, r, r, r, r });
                    arrowMesh = mb.Build("mc_arrow");
                }
                mf.sharedMesh = arrowMesh;
                mr.sharedMaterial = Mat.Solid(new Color(0.45f, 0.32f, 0.2f));
            }
            else
            {
                var tex = McAssets.Tex(kind == ProjectileKind.Pearl ? "item/ender_pearl.png" : "item/wind_charge.png");
                mf.sharedMesh = Meshes.ItemSprite(tex);
                mr.sharedMaterial = Mat.For(tex);
                go.transform.localScale = Vector3.one * 0.3f;
            }
            shots.Add(new Shot { Kind = kind, Pos = from, Vel = vel, Authority = authority, Owner = owner, View = go.transform, MobType = mobType });
        }

        public static void Tick()
        {
            float dt = Time.deltaTime;
            for (int i = shots.Count - 1; i >= 0; i--)
            {
                var s = shots[i];
                s.Age += dt;
                float gravity = s.Kind == ProjectileKind.WindCharge ? 0f : (s.Kind == ProjectileKind.Pearl ? 12f : 16f);
                Vector3 next = s.Pos + s.Vel * dt;
                s.Vel += Vector3.down * gravity * dt;
                s.Vel *= 1f - 0.2f * dt;

                bool done = s.Age > 6f;
                Vector3 hitPoint = next, hitNormal = Vector3.up;
                if (!done && Physics.Linecast(s.Pos, next, out var hit, Game.TerrainMask, QueryTriggerInteraction.Ignore))
                {
                    done = true;
                    hitPoint = hit.point;
                    hitNormal = hit.normal;
                }
                if (!done && s.Authority && s.Kind == ProjectileKind.Arrow && HitCharacter(s.Pos, next, out var victim))
                {
                    done = true;
                    hitPoint = victim.Center;
                    McMobs.HostHurtPlayer(victim, s.MobType, true);
                    Sfx.At("random/bowhit", hitPoint, 0.8f);
                }
                if (!done && s.Authority && s.Kind == ProjectileKind.WindCharge && MobDirector.AnyNear(next, 0.9f))
                    done = true;
                if (!done && s.Authority && s.Kind == ProjectileKind.Pearl && s.Age > 0.15f && OtherCharacterNear(next, s.Owner))
                    done = true;

                if (done)
                {
                    if (s.Authority) Land(s, hitPoint, hitNormal);
                    if (s.View != null) Object.Destroy(s.View.gameObject);
                    shots.RemoveAt(i);
                    continue;
                }
                s.Pos = next;
                if (s.View != null)
                {
                    s.View.position = next;
                    if (s.Kind == ProjectileKind.Arrow) { if (s.Vel.sqrMagnitude > 0.01f) s.View.rotation = Quaternion.LookRotation(s.Vel); }
                    else if (Game.Cam != null) s.View.rotation = Quaternion.LookRotation(next - Game.Cam.transform.position);
                }
            }
        }

        private static bool HitCharacter(Vector3 a, Vector3 b, out Character victim)
        {
            victim = null;
            foreach (var c in Character.AllCharacters)
            {
                if (c == null || c.data.dead || c.isBot) continue;
                foreach (var p in new[] { c.Center, c.Head, c.GetBodypart(BodypartType.Hip).transform.position })
                {
                    if (DistToSegment(p, a, b) < 0.45f) { victim = c; return true; }
                }
            }
            return false;
        }

        private static bool OtherCharacterNear(Vector3 p, int ownerActor)
        {
            foreach (var c in Character.AllCharacters)
            {
                if (c == null || c.data.dead || c.photonView?.Owner?.ActorNumber == ownerActor) continue;
                if ((c.Center - p).sqrMagnitude < 0.6f) return true;
            }
            return false;
        }

        public static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0 : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(p, a + ab * t);
        }

        private static void Land(Shot s, Vector3 at, Vector3 normal)
        {
            switch (s.Kind)
            {
                case ProjectileKind.Pearl:
                {
                    var c = Game.LocalChar;
                    if (c == null || c.data.dead) return;
                    Vector3 from = c.Center;
                    Vector3 to = at + normal * 0.6f + Vector3.up * 0.4f;
                    c.photonView.RPC("WarpPlayerRPC", RpcTarget.All, to, true);
                    var cfg = Balance.ItemCfg("ender_pearl");
                    c.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Injury, Balance.F(cfg, "injury", 0.05f));
                    c.refs.movement.CapFallDamage(0.05f, 2f);
                    Channel.All(Op.Sound, false, "mob/endermen/portal", from, 0.8f);
                    Channel.All(Op.Sound, false, "mob/endermen/portal2", to, 0.8f);
                    Fx.Burst(to, new[] { new Color(0.6f, 0.2f, 0.8f), new Color(0.3f, 0.05f, 0.4f) }, 20, 2f, 1f, false);
                    break;
                }
                case ProjectileKind.WindCharge:
                {
                    var cfg = Balance.ItemCfg("wind_charge");
                    Channel.All(Op.WindBurst, true, at + normal * 0.2f, Balance.F(cfg, "radius", 2.5f), Balance.F(cfg, "launch", 14f));
                    break;
                }
            }
        }

        private static void OnWindBurst(Vector3 at, float radius, float launch)
        {
            Sfx.At("entity/wind_charge/wind_burst", at, 1f);
            Fx.Burst(at, new[] { new Color(0.85f, 0.9f, 1f), new Color(0.7f, 0.75f, 0.95f) }, 24, 5f, 0.6f, false);
            if (PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) MobDirector.HostPush(at, radius * 1.4f, launch);
            var c = Game.LocalChar;
            if (c == null || c.data.dead) return;
            float reach = radius * 1.5f;
            float d = Vector3.Distance(c.Center, at);
            if (d > reach) return;
            float k = 1f - d / reach;
            Vector3 dir = (c.Center - at).normalized;
            dir = (dir + Vector3.up * 1.2f).normalized;
            c.AddForce(dir * (launch * k / Time.fixedDeltaTime), 1f, 1f);
            c.data.sinceGrounded = Mathf.Min(c.data.sinceGrounded, 0.1f);
            c.refs.movement.CapFallDamage(0.1f, 4f);
        }

        public static void Clear()
        {
            foreach (var s in shots) if (s.View != null) Object.Destroy(s.View.gameObject);
            shots.Clear();
        }
    }
}

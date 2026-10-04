using System.Collections.Generic;
using System.Linq;
using BlockPeak.Assets;
using BlockPeak.Core;
using BlockPeak.Net;
using BlockPeak.UI;
using Newtonsoft.Json.Linq;
using Photon.Pun;
using UnityEngine;

namespace BlockPeak.Mobs
{
    /// <summary>
    /// The warden, like Minecraft's: it is blind. It hears vibrations (footsteps, sprinting, jumping, landing,
    /// blocks, explosions...) within ~16 m and smells scouts that come close (sideways distance only; height is
    /// ignored). Every vibration or sniff makes it angrier at that scout. Agitated, it walks to where it heard
    /// something. Angry (80+), it roars and hunts that scout as fast as a scout can run and climb. Crouching makes no
    /// footstep vibrations. In Warden Chase it also always knows roughly which way the scouts are and walks there.
    /// </summary>
    public partial class BodyMob
    {
        // Flags (synced): 4 = agitated, 8 = angry, 16 = attacking
        private const byte FlagAgitated = 4, FlagAngry = 8, FlagAttack = 16;

        private readonly Dictionary<int, float> anger = new Dictionary<int, float>();
        private readonly Dictionary<int, float> stepAcc = new Dictionary<int, float>();
        private readonly Dictionary<int, bool> wasAirborne = new Dictionary<int, bool>();
        private readonly Dictionary<int, float> lastSinceJump = new Dictionary<int, float>();
        private Vector3 investigate;
        private float investigateUntil = -1f, senseTick, sniffCd = 3f, roarUntil = -1f, clickCd, farTimer;
        private Character angryAt;
        private bool speedsAreAngry;
        private float attackAnimUntil = -1f;
        private bool attackFlagSeen;
        private float heartbeatAt;

        private static JToken W => Balance.Section("mobs")["warden"] ?? new JObject();

        private static int ActorOf(Character c) => c != null && c.photonView != null && c.photonView.Owner != null ? c.photonView.Owner.ActorNumber : -1;

        private static float FlatDist(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        /// <summary>Host: something made a vibration (explosion, block placed or broken, horn...).</summary>
        public void HostHear(Vector3 at, float strength, Character source)
        {
            if (Type != "warden" || Dying) return;
            float range = Balance.F(W, "hearRange", 16f) * Mathf.Max(1f, strength * 0.75f);
            if (Vector3.Distance(at, Position) > range) return;
            investigate = at;
            investigateUntil = Time.time + 12f;
            if (source == null) source = BodyMobs.Players.Where(p => Vector3.Distance(p.Center, at) < 6f).OrderBy(p => Vector3.Distance(p.Center, at)).FirstOrDefault();
            if (source != null) AddAnger(source, Balance.F(W, "angerPerVibration", 12f) * strength);
            if (clickCd <= 0f) { clickCd = 1.2f; Channel.All(Op.Sound, false, "mob/warden/tendril_clicks", Position, 0.9f); }
        }

        private void AddAnger(Character p, float amount)
        {
            int a = ActorOf(p);
            if (a < 0) return;
            anger.TryGetValue(a, out float v);
            anger[a] = Mathf.Min(150f, v + amount);
        }

        public void WardenHurt()
        {
            // Hit by a scout: furious at the closest one.
            var p = BodyMobs.Players.OrderBy(x => (x.Center - Position).sqrMagnitude).FirstOrDefault();
            if (p != null) AddAnger(p, 100f);
        }

        private void WardenBrain(float dt)
        {
            clickCd -= dt;
            sniffCd -= dt;

            // ---- senses
            senseTick -= dt;
            if (senseTick <= 0f)
            {
                senseTick = 0.25f;
                float hear = Balance.F(W, "hearRange", 16f);
                foreach (var p in BodyMobs.Players)
                {
                    int a = ActorOf(p);
                    if (a < 0) continue;
                    float d = Vector3.Distance(p.Center, Position);
                    bool airborne = !p.data.isGrounded && !p.data.isClimbing && !p.data.isRopeClimbing;
                    wasAirborne.TryGetValue(a, out bool wasAir);
                    wasAirborne[a] = airborne;
                    lastSinceJump.TryGetValue(a, out float lastJ);
                    lastSinceJump[a] = p.data.sinceJump;
                    if (d > hear) { stepAcc[a] = 0f; continue; }

                    float speed = new Vector2(p.data.avarageVelocity.x, p.data.avarageVelocity.z).magnitude;
                    bool moving = speed > 0.8f || (p.data.isClimbing && p.data.avarageVelocity.sqrMagnitude > 0.3f);
                    if (moving && !p.data.isCrouching)
                    {
                        stepAcc.TryGetValue(a, out float acc);
                        acc += 0.25f * (p.data.isSprinting ? 2f : 1f);
                        if (acc >= 1f) { acc = 0f; HostHear(p.Center, p.data.isSprinting ? 1.6f : 1f, p); }
                        stepAcc[a] = acc;
                    }
                    if (wasAir && !airborne && p.data.isGrounded) HostHear(p.Center, 1.5f, p); // landing
                    if (p.data.sinceJump < 0.3f && lastJ >= 0.3f) HostHear(p.Center, 1.2f, p);  // jump
                }
            }

            // Smell: close by sideways (height ignored).
            if (sniffCd <= 0f)
            {
                sniffCd = Balance.F(W, "sniffSeconds", 5f);
                var near = BodyMobs.Players.Where(p => FlatDist(p.Center, Position) < Balance.F(W, "smellRange", 6f)).OrderBy(p => FlatDist(p.Center, Position)).FirstOrDefault();
                Channel.All(Op.Sound, false, "mob/warden/sniff", Position, 0.9f);
                if (near != null) { AddAnger(near, Balance.F(W, "angerPerSniff", 35f)); investigate = near.Center; investigateUntil = Time.time + 8f; }
            }

            // Anger fades slowly, and only for scouts still around.
            foreach (var k in anger.Keys.ToList())
            {
                float v = anger[k] - Balance.F(W, "angerDecayPerSecond", 1f) * dt;
                if (v <= 0f || !BodyMobs.Players.Any(p => ActorOf(p) == k)) anger.Remove(k); else anger[k] = v;
            }

            // ---- who is it angry at?
            Character mostHated = null;
            float most = 0f;
            foreach (var p in BodyMobs.Players)
            {
                if (p.data.fullyPassedOut) continue;
                if (anger.TryGetValue(ActorOf(p), out float v) && v > most) { most = v; mostHated = p; }
            }
            bool angry = most >= Balance.F(W, "angryAt", 80f);
            bool agitated = !angry && (most >= 40f || Time.time < investigateUntil);

            if (angry && angryAt != mostHated)
            {
                if (angryAt == null) { roarUntil = Time.time + 2.2f; Channel.All(Op.Sound, true, "mob/warden/roar", Position, 1f); }
                angryAt = mostHated;
            }
            if (!angry) angryAt = null;
            byte f = (byte)(Flags & ~(FlagAgitated | FlagAngry));
            if (angry) f |= FlagAngry; else if (agitated) f |= FlagAgitated;
            if (Time.time > attackAnimUntil) f = (byte)(f & ~FlagAttack);
            SetFlags(f);
            SetWardenSpeeds(angry);

            if (Time.time < soundAt) { } else { soundAt = Time.time + UnityEngine.Random.Range(5f, 10f); Channel.All(Op.Sound, false, angry ? "mob/warden/angry" : agitated ? "mob/warden/agitated" : "mob/warden/ambient", Position, 0.8f); }

            // ---- act
            if (Time.time < roarUntil) { if (angryAt != null) Z.LookAt(angryAt.Head); return; }

            float walkGoal = 0f; // how far it still tries to walk (stuck checks only count while walking)
            if (angry && angryAt != null)
            {
                target = angryAt;
                Vector3 to = target.Center - Position;
                float flat = new Vector2(to.x, to.z).magnitude;
                WalkTo(target.Center, flat > 2.5f, true);
                walkGoal = flat;
                if (flat < 2.4f && Mathf.Abs(to.y) < 2f && attackCd <= 0f)
                {
                    attackCd = 1.6f;
                    attackAnimUntil = Time.time + 0.5f;
                    SetFlags((byte)(Flags | FlagAttack));
                    McMobs.HostHurtPlayer(target, Type, false, Position);
                    Channel.All(Op.Sound, false, "mob/warden/attack_impact", Position, 1f);
                }
                // Sonic boom when it can't get to the scout it hates.
                if (sonicCd <= 0f && flat < 20f && (Mathf.Abs(to.y) > 3f || stuckFor > 2f))
                {
                    sonicCd = 5f;
                    Channel.All(Op.Sound, true, "mob/warden/sonic_boom", Position, 1f);
                    Fx.Burst(Vector3.Lerp(Position, target.Center, 0.5f), new[] { new Color(0.2f, 0.9f, 0.9f), new Color(0.1f, 0.5f, 0.6f) }, 20, 2f, 0.6f, false);
                    McMobs.HostHurtPlayer(target, "warden_sonic", true, Position);
                }
            }
            else if (Time.time < investigateUntil)
            {
                target = null;
                if (FlatDist(investigate, Position) > 1.5f) { WalkTo(investigate, false, true); walkGoal = FlatDist(investigate, Position); }
                else Z.LookAt(investigate + UnityEngine.Random.insideUnitSphere);
            }
            else if (Modes.GameModes.Active == Modes.ModeKind.WardenChase)
            {
                // It always has a rough idea which way the scouts went (sideways only) and plods that way.
                var p = BodyMobs.Players.OrderBy(x => FlatDist(x.Center, Position)).FirstOrDefault();
                if (p != null) { WalkTo(p.Center, false, true); walkGoal = FlatDist(p.Center, Position); } else Wander();
            }
            else Wander();

            // Stuck handling: climb, hop, and in Warden Chase dig over to the scouts if it falls far behind.
            float moved = Vector3.Distance(Position, lastPos);
            lastPos = Position;
            if (moved < 0.3f * dt && walkGoal > 3f) stuckFor += dt; else stuckFor = Mathf.Max(0f, stuckFor - dt);
            if (stuckFor > 1.5f && C.data.isGrounded && UnityEngine.Random.value < 0.02f) C.input.jumpWasPressed = true;
            var nearest = BodyMobs.Players.OrderBy(x => FlatDist(x.Center, Position)).FirstOrDefault();
            bool far = nearest != null && FlatDist(nearest.Center, Position) > Balance.F(W, "digWhenFartherThan", 110f);
            farTimer = far || (angry && stuckFor > 10f) ? farTimer + dt : 0f;
            if (Modes.GameModes.Active == Modes.ModeKind.WardenChase && farTimer > 20f && nearest != null)
            {
                farTimer = 0f;
                target = nearest;
                Reemerge();
            }
        }

        private float baseMoveForce = -1f, baseSprintMult, baseClimbSpeed;

        /// <summary>Calm: slow. Angry: exactly as fast as a scout (walk, sprint and climb).</summary>
        private void SetWardenSpeeds(bool angry)
        {
            var mv = C.refs.movement;
            var cl = C.refs.climbing;
            if (baseMoveForce < 0f) { baseMoveForce = mv.movementForce; baseSprintMult = mv.sprintMultiplier; baseClimbSpeed = cl.climbSpeed; }
            if (angry == speedsAreAngry && Time.frameCount % 60 != 0) return;
            speedsAreAngry = angry;
            if (angry)
            {
                var scout = BodyMobs.Players.FirstOrDefault();
                if (scout != null)
                {
                    mv.movementForce = scout.refs.movement.movementForce;
                    mv.sprintMultiplier = scout.refs.movement.sprintMultiplier;
                    cl.climbSpeed = scout.refs.climbing.climbSpeed;
                }
                mv.movementModifier = 1f;
                cl.climbSpeedMod = 1f;
            }
            else
            {
                mv.movementForce = baseMoveForce;
                mv.sprintMultiplier = baseSprintMult;
                cl.climbSpeed = baseClimbSpeed;
                mv.movementModifier = Balance.F(Cfg, "walkSpeed", 0.75f);
                cl.climbSpeedMod = 0.6f;
            }
        }

        /// <summary>Everyone: tendrils wiggle, the glow and heartbeat speed up with its anger, attack swing.</summary>
        private void WardenLooks()
        {
            bool angry = (Flags & FlagAngry) != 0, agitated = (Flags & FlagAgitated) != 0;
            bool attacking = (Flags & FlagAttack) != 0;
            if (attacking && !attackFlagSeen) attackAnimUntil = Time.time + 0.5f;
            attackFlagSeen = attacking;

            float beat = angry ? 2.6f : agitated ? 1.6f : 0.9f; // beats per second
            float phase = Time.time * beat;
            float pulse = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(phase * Mathf.PI * 2f)), 4f);
            if (Model.Glow != null && Model.Glow.HasProperty("_EmissionColor"))
                Model.Glow.SetColor("_EmissionColor", new Color(0.15f, 0.7f, 0.75f) * (0.4f + 1.4f * pulse));
            float wiggle = Mathf.Cos(Time.time * (angry ? 9f : 3f)) * (angry ? 25f : agitated ? 14f : 6f);
            if (Model.TendrilR != null) Model.TendrilR.localRotation = Quaternion.Euler(wiggle, 0f, 0f);
            if (Model.TendrilL != null) Model.TendrilL.localRotation = Quaternion.Euler(-wiggle, 0f, 0f);

            if (Time.time > heartbeatAt)
            {
                heartbeatAt = Time.time + 1f / beat;
                var me = Game.LocalChar;
                if (me != null && Vector3.Distance(me.Center, Position) < 24f) Sfx.At("mob/warden/heartbeat", Position, angry ? 0.9f : 0.5f, 1f, 24f);
            }
        }
    }
}

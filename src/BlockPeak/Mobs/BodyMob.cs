using System;
using System.Collections.Generic;
using System.Linq;
using BlockPeak.Assets;
using BlockPeak.Building;
using BlockPeak.Core;
using BlockPeak.Items;
using BlockPeak.Net;
using BlockPeak.UI;
using Newtonsoft.Json.Linq;
using Photon.Pun;
using UnityEngine;

namespace BlockPeak.Mobs
{
    /// <summary>
    /// One Minecraft mob riding a PEAK zombie body. Everyone: hides the scout look, hangs the Minecraft parts on the
    /// body's bones every frame (so PEAK's walk/run/climb/fall animations drive them), shows hearts when hurt.
    /// Host: the brain (chase, climb, shoot, explode, hop), health and despawning.
    /// </summary>
    public partial class BodyMob : MonoBehaviour
    {
        public string Type;
        public JToken Cfg;
        public Character C;
        public MushroomZombie Z;
        public MobModel Model;
        public bool Dying;
        public byte Flags; // 1 burning, 2 fusing
        public int ViewId;
        public float Radius = 0.6f;

        private Transform holder;
        private HeartsBar hearts;
        private float hp, maxHp;
        private float hideTimer, fireTick;
        private float attackCd, shootCd, hopCd, soundAt, burnTick, vanishAt = -1f;
        private float fleeUntil;
        private Vector3 fleeFrom;
        private float fuse = -1f;
        private Vector3 lastPos;
        private float stuckFor, farFor;
        private Character target;
        private float retarget;
        private float sonicCd;

        public Vector3 Position => C != null ? C.Center : transform.position;
        private static bool IsHost => !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode;

        public void Init(MushroomZombie z, string type)
        {
            Z = z;
            C = z.character != null ? z.character : GetComponent<Character>();
            Type = type;
            Cfg = Balance.Section("mobs")["types"]?[type] ?? new JObject();
            ViewId = GetComponent<PhotonView>().ViewID;
            BodyMobs.All[ViewId] = this;
            maxHp = hp = Balance.F(Cfg, "health", 20f);
            soundAt = Time.time + UnityEngine.Random.Range(2f, 6f);
            var h = new GameObject("BP_Mob_" + type);
            holder = h.transform;
            Model = MobModels.Build(type, holder);
            Radius = Model.Kind == "spider" ? 0.9f : Model.Kind == "warden" ? 0.8f : 0.5f;
            hearts = HeartsBar.Create(holder, maxHp);
            if (C != null)
            {
                C.isZombie = true;
                float speed = Balance.F(Cfg, "walkSpeed", 1f);
                C.refs.movement.movementModifier = speed;
            }
            MobSound("idle");
        }

        private void OnDestroy()
        {
            BodyMobs.All.Remove(ViewId);
            if (holder != null) Destroy(holder.gameObject);
        }

        // ================================================================== looks (everyone)

        private void Update()
        {
            if (C == null) return;
            hideTimer -= Time.deltaTime;
            if (hideTimer <= 0f)
            {
                hideTimer = 0.5f;
                foreach (var r in C.GetComponentsInChildren<Renderer>(true)) if (r.enabled) r.enabled = false;
            }
            if ((Flags & 1) != 0 && Time.time > fireTick)
            {
                fireTick = Time.time + 0.15f;
                Fx.Burst(Position + UnityEngine.Random.insideUnitSphere * 0.4f, new[] { new Color(1f, 0.5f, 0.1f), new Color(1f, 0.85f, 0.2f) }, 2, 1f, 0.5f, false, 0.07f);
            }
        }

        private void LateUpdate()
        {
            if (C == null || Model == null) return;
            try { Pose(); }
            catch (Exception e) { Health.Report("mob-pose", e); }
        }

        private Vector3 Bone(BodypartType t)
        {
            var b = C.GetBodypart(t);
            return b != null ? b.transform.position : C.Center;
        }

        /// <summary>Hang the Minecraft parts on PEAK's skeleton.</summary>
        private void Pose()
        {
            Vector3 hip = Bone(BodypartType.Hip), torso = Bone(BodypartType.Torso);
            Vector3 legL = Bone(BodypartType.Leg_L), legR = Bone(BodypartType.Leg_R);
            Vector3 footL = Bone(BodypartType.Foot_L), footR = Bone(BodypartType.Foot_R);
            Vector3 up = (torso - hip).normalized;
            if (up.sqrMagnitude < 0.01f) up = Vector3.up;
            Vector3 fwd = Vector3.ProjectOnPlane(C.data.lookDirection, up);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.ProjectOnPlane(transform.forward, up);
            fwd.Normalize();
            Vector3 right = Vector3.Cross(up, fwd);
            Quaternion bodyRot = Quaternion.LookRotation(fwd, up);
            float legLen = Mathf.Max(0.3f, (Vector3.Distance(legL, footL) + Vector3.Distance(legR, footR)) * 0.5f + 0.08f);
            float mobScale = Mathf.Clamp(Balance.F(Balance.Section("mobs"), "scale", 0.9f), 0.3f, 2f);
            float s = legLen / Model.LegPx; // world units per Minecraft pixel
            float flash = hearts != null ? hearts.FlashAmount : 0f;
            Tint(flash > 0f ? new Color(1f, 0.35f, 0.35f) : ((Flags & 2) != 0 && Mathf.Sin(Time.time * 20f) > 0.3f ? new Color(1.6f, 1.6f, 1.6f) : Color.white));

            if (Model.FollowsBones)
            {
                // Parts are positioned by hand; the model root stays unscaled at the origin.
                holder.position = Vector3.zero;
                holder.rotation = Quaternion.identity;
                Model.Root.transform.localScale = Vector3.one;
                Model.Inner.localScale = Vector3.one;
                Vector3 hipCenter = (legL + legR) * 0.5f;
                Vector3 neck = hipCenter + up * (Model.BodyPx * s);
                float partScale = s * 16f;
                Place(Model.Body, neck, bodyRot, partScale);
                Vector3 look = C.data.lookDirection.sqrMagnitude > 0.01f ? C.data.lookDirection.normalized : fwd;
                Quaternion headRot = Quaternion.LookRotation(Vector3.Slerp(fwd, look, 0.7f), up);
                Place(Model.Head, neck, headRot, partScale);
                Vector3 shoulderR = neck + right * (Model.ShoulderX * s) - up * (Model.ShoulderDrop * s);
                Vector3 shoulderL = neck - right * (Model.ShoulderX * s) - up * (Model.ShoulderDrop * s);
                if (Model.Kind == "warden")
                {
                    // PEAK's zombie reaches forward while chasing; the warden's long arms hang and swing with its steps.
                    float swing = SignedSwing(legR, footR, right, up) * 0.8f;
                    float attack = Time.time < attackAnimUntil ? Mathf.Sin((attackAnimUntil - Time.time) / 0.5f * Mathf.PI) * 70f : 0f;
                    Vector3 dR = Quaternion.AngleAxis(-swing - attack, right) * -up;
                    Vector3 dL = Quaternion.AngleAxis(swing - attack, right) * -up;
                    // Facing taken from the swing axis, so the arm never flips when it swings past horizontal.
                    Place(Model.ArmR, shoulderR, Quaternion.LookRotation(Vector3.Cross(dR, right), -dR), partScale);
                    Place(Model.ArmL, shoulderL, Quaternion.LookRotation(Vector3.Cross(dL, right), -dL), partScale);
                    WardenLooks();
                }
                else
                {
                    Limb(Model.ArmR, shoulderR, Bone(BodypartType.Arm_R), Bone(BodypartType.Hand_R), fwd, up, partScale);
                    Limb(Model.ArmL, shoulderL, Bone(BodypartType.Arm_L), Bone(BodypartType.Hand_L), fwd, up, partScale);
                }
                Limb(Model.LegR, hipCenter + right * (Model.HipX * s), legR, footR, fwd, up, partScale);
                Limb(Model.LegL, hipCenter - right * (Model.HipX * s), legL, footL, fwd, up, partScale);
                if (hearts != null) hearts.transform.position = neck + up * (Model.Kind == "warden" ? 18f * s : 10f * s);
            }
            else
            {
                // Four-legged / no-legged mobs: the whole model stands on the ground under the body and turns with it.
                float ground = Mathf.Min(footL.y, footR.y) - 0.05f;
                holder.position = new Vector3(hip.x, ground, hip.z);
                Vector3 flatFwd = BodyMobs.Flat(fwd);
                holder.rotation = Quaternion.LookRotation(flatFwd, Vector3.up);
                Model.Root.transform.localScale = Vector3.one * (s * 16f); // one Minecraft pixel = s (parts are built in 1/16 units)
                float swing = SignedSwing(legR, footR, right, up);
                AnimateRootModel(swing);
                if (hearts != null) hearts.transform.position = holder.position + Vector3.up * (Model.Height * s * 16f + 0.3f);
            }
        }

        private static void Place(Transform t, Vector3 pos, Quaternion rot, float scale)
        {
            if (t == null) return;
            t.position = pos;
            t.rotation = rot;
            t.localScale = Vector3.one * scale;
        }

        /// <summary>A limb hangs from its Minecraft joint and points the way PEAK's limb points.</summary>
        private static void Limb(Transform t, Vector3 joint, Vector3 from, Vector3 to, Vector3 fwd, Vector3 up, float scale)
        {
            if (t == null) return;
            Vector3 d = (to - from);
            d = d.sqrMagnitude < 1e-4f ? -up : d.normalized;
            Vector3 f = Vector3.ProjectOnPlane(fwd, d);
            if (f.sqrMagnitude < 1e-4f) f = Vector3.ProjectOnPlane(up, d);
            Place(t, joint, Quaternion.LookRotation(f.normalized, -d), scale);
        }

        private static float SignedSwing(Vector3 hipJoint, Vector3 foot, Vector3 right, Vector3 up)
        {
            Vector3 d = Vector3.ProjectOnPlane(foot - hipJoint, right);
            return Vector3.SignedAngle(-up, d, right);
        }

        private void AnimateRootModel(float swing)
        {
            switch (Model.Kind)
            {
                case "creeper":
                    if (Model.QuadLegs.Count == 4)
                    {
                        Model.QuadLegs[0].localRotation = Quaternion.Euler(swing, 0, 0);
                        Model.QuadLegs[1].localRotation = Quaternion.Euler(-swing, 0, 0);
                        Model.QuadLegs[2].localRotation = Quaternion.Euler(-swing, 0, 0);
                        Model.QuadLegs[3].localRotation = Quaternion.Euler(swing, 0, 0);
                    }
                    float swell = (Flags & 2) != 0 ? 1f + 0.08f * Mathf.Abs(Mathf.Sin(Time.time * 10f)) : 1f;
                    Model.Inner.localScale = new Vector3(swell, 1f + (swell - 1f) * 0.5f, swell) * Mathf.Clamp(Balance.F(Balance.Section("mobs"), "scale", 0.9f), 0.3f, 2f);
                    if (Model.Head != null) Model.Head.localRotation = Quaternion.identity;
                    break;
                case "spider":
                    for (int i = 0; i < Model.SpiderLegsR.Count; i++)
                    {
                        float fan = (i - 1.5f) * 22f;
                        float wiggle = Mathf.Sin(swing * Mathf.Deg2Rad * 3f + i * 1.6f) * 15f;
                        Model.SpiderLegsR[i].localRotation = Quaternion.Euler(0, -fan + wiggle, -35f);
                        Model.SpiderLegsL[i].localRotation = Quaternion.Euler(0, fan - wiggle, 35f);
                    }
                    break;
                case "slime":
                case "magma":
                    float vy = C.data.avarageVelocity.y;
                    float squish = Mathf.Clamp(1f - vy * 0.04f, 0.8f, 1.25f);
                    Model.Inner.localScale = new Vector3(2f - squish, squish, 2f - squish) * Mathf.Clamp(Balance.F(Balance.Section("mobs"), "scale", 0.9f), 0.3f, 2f);
                    break;
            }
        }

        private MaterialPropertyBlock mpb;
        private static readonly string[] ColorProps = { "_BaseColor", "_Color", "_Tint", "_MainColor" };

        private void Tint(Color c)
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
            foreach (var r in Model.Renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                var m = r.sharedMaterial;
                foreach (var p in ColorProps) if (m != null && m.HasProperty(p)) mpb.SetColor(p, c);
                r.SetPropertyBlock(mpb);
            }
        }

        public IEnumerable<Vector3> HitPoints()
        {
            if (C == null) yield break;
            yield return C.Center;
            yield return C.Head;
            yield return Bone(BodypartType.Hip);
        }

        // ================================================================== health & events

        public void OnHurt(float fraction)
        {
            if (hearts != null) hearts.Show(fraction * maxHp, maxHp);
            MobSound("hurt");
        }

        public void OnDied(byte reason)
        {
            Dying = true;
            if (reason == 1) MobSound("death");
            if (hearts != null) hearts.gameObject.SetActive(false);
            if (reason != 2) StartCoroutine(Poof());
        }

        private System.Collections.IEnumerator Poof()
        {
            yield return new WaitForSeconds(0.9f);
            Fx.Burst(Position, new[] { Color.white, new Color(0.8f, 0.8f, 0.8f) }, 14, 1.5f, 0.8f, false, 0.1f);
            Sfx.At("random/pop", Position, 0.3f);
            if (holder != null) holder.gameObject.SetActive(false);
        }

        public void HostDamage(float dmg, Vector3 dir)
        {
            if (!IsHost || Dying) return;
            if (Creative_IsInvulnerableMob()) return;
            hp -= dmg;
            Push(BodyMobs.Flat(dir) * Balance.F(Cfg, "knockbackTaken", 6f) + Vector3.up * 3f);
            Channel.All(Op.BodyHurt, false, ViewId, Mathf.Max(0f, hp) / maxHp);
            if (Type == "warden") WardenHurt();
            if (hp <= 0f) HostDie(1);
        }

        private bool Creative_IsInvulnerableMob() => false;

        public void Push(Vector3 velocityChange)
        {
            if (C == null || !IsHost) return;
            C.AddForce(velocityChange / Time.fixedDeltaTime, 0.9f, 1.1f);
        }

        public void ScareFrom(Vector3 at, float seconds)
        {
            fleeUntil = Time.time + seconds;
            fleeFrom = at;
            fuse = -1f;
        }

        /// <summary>Host: 0 = vanish, 1 = killed, 2 = exploded (creeper).</summary>
        public void HostDie(byte reason)
        {
            if (Dying) return;
            Dying = true;
            if (reason == 1) Drop();
            Channel.All(Op.BodyDied, true, ViewId, reason);
            if (C != null && reason == 1) C.Fall(10f);
            StartCoroutine(DestroyLater(reason == 1 ? 1.2f : 0.1f));
        }

        private System.Collections.IEnumerator DestroyLater(float t)
        {
            yield return new WaitForSeconds(t);
            if (this != null && GetComponent<PhotonView>().IsMine) PhotonNetwork.Destroy(gameObject);
        }

        private void Drop()
        {
            string key = null;
            if ((Type == "zombie" || Type == "husk" || Type == "drowned") && UnityEngine.Random.value < 0.6f) key = "rotten_flesh";
            if (Type == "creeper" && UnityEngine.Random.value < 0.15f) key = "tnt";
            var def = ItemDefs.ByKey(key);
            var t = def != null ? Loot.TemplateFor(def) : null;
            if (t == null || !PhotonNetwork.InRoom) return;
            try
            {
                var go = PhotonNetwork.InstantiateItemRoom(t.name, Position + Vector3.up * 0.5f, Quaternion.identity);
                var item = go.GetComponent<Item>();
                item.GetData<BoolItemData>(Stacks.RolledKey);
                if (def.Stack > 1) Stacks.SetCount(item.data, 1, def.Stack);
                Stacks.MarkRolled(item.data);
                item.photonView.RPC("SetKinematicRPC", RpcTarget.AllBuffered, false, go.transform.position, go.transform.rotation);
            }
            catch (Exception e) { Health.Report("mob-drop", e); }
        }

        private void SetFlags(byte f)
        {
            if (f == Flags) return;
            Flags = f;
            Channel.All(Op.BodyFlags, true, ViewId, f);
        }

        // ================================================================== brain (host)

        public void Brain()
        {
            if (C == null || Dying) return;
            float dt = Time.deltaTime;
            C.input.ResetInput();
            C.data.currentStamina = 1f;
            attackCd -= dt; shootCd -= dt; hopCd -= dt; sonicCd -= dt;
            if (Type == "warden") { WardenBrain(dt); return; }
            bool night = Game.IsNight || BodyMobs.ForceNight || Modes.GameModes.Active != Modes.ModeKind.None;

            // Daylight
            if (!night)
            {
                if (Balance.B(Cfg, "burnsAtDawn", false))
                {
                    SetFlags((byte)(Flags | 1));
                    burnTick += dt;
                    if (burnTick > 1f) { burnTick = 0f; HostDamage(1f, Vector3.zero); if (Dying) return; }
                }
                if (Balance.B(Cfg, "vanishAtDawn", false))
                {
                    if (vanishAt < 0f) vanishAt = Time.time + UnityEngine.Random.Range(2f, 6f);
                    if (Time.time > vanishAt) { HostDie(0); return; }
                }
            }

            // Target
            retarget -= dt;
            bool smells = Modes.GameModes.MobsAlwaysKnow;
            bool neutral = !night && Balance.B(Cfg, "neutralAtDawn", false);
            if (retarget <= 0f || target == null || target.data.dead)
            {
                retarget = 1f;
                target = null;
                if (!neutral)
                {
                    float best = smells ? float.MaxValue : 40f * 40f;
                    foreach (var p in BodyMobs.Players)
                    {
                        if (p.data.fullyPassedOut) continue;
                        float d = (p.Center - Position).sqrMagnitude;
                        if (d < best) { best = d; target = p; }
                    }
                }
            }

            // Despawn when nobody is around (game-mode mobs never give up)
            float nearest = BodyMobs.Players.Select(p => Vector3.Distance(p.Center, Position)).DefaultIfEmpty(999f).Min();
            if (!smells)
            {
                if (nearest > Balance.F(Balance.Section("mobs"), "despawnDistance", 60f)) farFor += dt; else farFor = 0f;
                if (farFor > Balance.F(Balance.Section("mobs"), "despawnAfterSeconds", 20f) || Position.y < -500f) { HostDie(0); return; }
            }

            if (Time.time > soundAt) { soundAt = Time.time + UnityEngine.Random.Range(6f, 14f); MobSoundAll("idle"); }

            if (Time.time < fleeUntil) { WalkTo(Position + BodyMobs.Flat(Position - fleeFrom) * 10f, true, true); return; }
            if (target == null) { Wander(); return; }

            Vector3 to = target.Center - Position;
            float flat = new Vector2(to.x, to.z).magnitude;
            bool ranged = Balance.B(Cfg, "ranged", false);
            bool sprints = Balance.B(Cfg, "sprints", Type != "warden");

            if (Balance.B(Cfg, "explodes", false))
            {
                if (CreeperLogic(flat, Mathf.Abs(to.y))) return;
                if (fuse >= 0f) { Z.LookAt(target.Head); return; }
            }

            if (ranged && flat < 16f && flat > 5f && CanSee(target))
            {
                Z.LookAt(target.Head);
                if (shootCd <= 0f) { shootCd = UnityEngine.Random.Range(2.2f, 3.2f); Shoot(); }
            }
            else if (ranged && flat <= 4f)
            {
                // Skeletons back off to keep their distance.
                Z.LookAt(target.Head);
                C.input.movementInput = new Vector2(0f, -1f);
            }
            else
            {
                WalkTo(target.Center, sprints && flat > 5f && flat < 25f, Balance.B(Cfg, "climbs", true));
            }

            // Slimes hop
            if ((Type == "slime" || Type == "magma_cube") && hopCd <= 0f && C.data.isGrounded)
            {
                hopCd = UnityEngine.Random.Range(0.8f, 1.6f);
                C.input.jumpWasPressed = true;
                MobSoundAll("jump");
            }

            // Melee
            float reach = Type == "warden" ? 2.4f : Type == "slime" || Type == "magma_cube" ? 1.4f : 1.7f;
            if (!ranged || flat < 2.5f)
            {
                if (flat < reach && Mathf.Abs(to.y) < 2f && attackCd <= 0f && !Balance.B(Cfg, "explodes", false))
                {
                    attackCd = Type == "warden" ? 1.8f : 1.2f;
                    McMobs.HostHurtPlayer(target, Type, false, Position);
                    MobSoundAll("attack");
                }
            }

            // Warden: sonic boom when it can't reach you
            if (Type == "warden" && sonicCd <= 0f && flat < 20f && (Mathf.Abs(to.y) > 3f || stuckFor > 2f) && CanSee(target))
            {
                sonicCd = 5f;
                Channel.All(Op.Sound, true, "mob/warden/sonic_boom", Position, 1f);
                Fx.Burst(Vector3.Lerp(Position, target.Center, 0.5f), new[] { new Color(0.2f, 0.9f, 0.9f), new Color(0.1f, 0.5f, 0.6f) }, 20, 2f, 0.6f, false);
                McMobs.HostHurtPlayer(target, "warden_sonic", true, Position);
            }

            // Stuck handling
            float moved = Vector3.Distance(Position, lastPos);
            lastPos = Position;
            if (moved < 0.3f * dt && flat > 3f) stuckFor += dt; else stuckFor = Mathf.Max(0f, stuckFor - dt);
            if (stuckFor > 1.5f && C.data.isGrounded && UnityEngine.Random.value < 0.02f) C.input.jumpWasPressed = true;
            if (stuckFor > 10f || (smells && flat > 90f))
            {
                if (smells) Reemerge();
                else { HostDie(0); }
            }
        }

        private void Wander()
        {
            if (UnityEngine.Random.value < 0.01f) Z.LookAt(Position + UnityEngine.Random.insideUnitSphere * 5f);
            C.input.movementInput = new Vector2(0f, 0.4f);
        }

        private void WalkTo(Vector3 pos, bool sprint, bool climb)
        {
            Z.LookAt(pos);
            if (C.data.isClimbing)
            {
                Z.ClimbTowards(pos, 1f);
                if (pos.y < C.Center.y - 1f) C.refs.climbing.StopClimbing();
                return;
            }
            C.input.movementInput = new Vector2(0f, 1f);
            Z.SetSprint(sprint);
            if (climb && (pos.y > C.Center.y + 1.2f || stuckFor > 0.5f)) C.refs.climbing.TryClimb();
            // Jump over small gaps like PEAK's zombies.
            if (HelperFunctions.LineCheck(C.Center + BodyMobs.Flat(pos - C.Center) * 1f, C.Center + BodyMobs.Flat(pos - C.Center) * 1f + Vector3.down * 3f, HelperFunctions.LayerType.TerrainMap).transform == null)
                C.input.jumpWasPressed = true;
        }

        private bool CanSee(Character t) =>
            HelperFunctions.LineCheck(C.Head, t.Center, HelperFunctions.LayerType.TerrainMap).transform == null;

        private bool CreeperLogic(float flat, float dy)
        {
            if (fuse < 0f && flat < 2.4f && dy < 2f)
            {
                fuse = 1.5f;
                SetFlags((byte)(Flags | 2));
                Channel.All(Op.Sound, false, "random/fuse", Position, 1f);
            }
            if (fuse < 0f) return false;
            if (flat > 6f) { fuse = -1f; SetFlags((byte)(Flags & ~2)); return false; }
            fuse -= Time.deltaTime;
            if (fuse > 0f) return false;
            float radius = 3f;
            float injury = Balance.F(Cfg, "amount", 0.2f) * 1.8f;
            Vector3 at = Position;
            HostDie(2);
            Channel.All(Op.Explosion, true, at, radius, injury);
            BlockWorld.HostExplosionAftermath(at, radius);
            return true;
        }

        private void Shoot()
        {
            Vector3 from = C.Head + BodyMobs.Flat(target.Center - C.Head) * 0.5f;
            Vector3 aim = target.Center + UnityEngine.Random.insideUnitSphere * 0.6f;
            Vector3 d = aim - from;
            float speed = 22f;
            float t = new Vector2(d.x, d.z).magnitude / speed;
            Vector3 v = BodyMobs.Flat(d) * speed;
            v.y = d.y / Mathf.Max(0.05f, t) + 0.5f * 16f * t;
            Projectiles.FireArrow(from, v, Type);
        }

        /// <summary>Warden: dig down and come back up behind its target.</summary>
        private void Reemerge()
        {
            stuckFor = 0f;
            if (target == null) return;
            Vector3 behind = target.Center - BodyMobs.Flat(target.data.lookDirection) * 25f;
            if (!Physics.Raycast(behind + Vector3.up * 30f, Vector3.down, out var hit, 80f, Game.TerrainMask, QueryTriggerInteraction.Ignore)) return;
            Channel.All(Op.Sound, true, "mob/warden/dig", Position, 1f);
            C.photonView.RPC("WarpPlayerRPC", RpcTarget.All, hit.point + Vector3.up * 0.5f, false);
            Channel.All(Op.Sound, true, "mob/warden/emerge", hit.point, 1f);
        }

        private void MobSoundAll(string kind) => Channel.All(Op.Sound, false, SoundName(kind), Position, kind == "idle" ? 0.6f : 0.9f);

        private void MobSound(string kind)
        {
            var name = SoundName(kind);
            if (name != null) Sfx.At(name, Position, kind == "idle" ? 0.6f : 0.9f);
        }

        private string SoundName(string kind)
        {
            string t = Type == "magma_cube" ? "magmacube" : Type;
            string b = "mob/" + t + "/";
            string[] options;
            switch (kind)
            {
                case "idle": options = new[] { b + "say", b + "idle", b + "ambient", b + "big" }; break;
                case "hurt": options = new[] { b + "hurt", b + "small", b + "say", b + "big" }; break;
                case "death": options = new[] { b + "death", b + "big" }; break;
                case "attack": options = new[] { b + "attack_impact", b + "attack", b + "say" }; break;
                case "jump": options = new[] { b + "jump", b + "small" }; break;
                default: options = new string[0]; break;
            }
            foreach (var o in options) if (McAssets.Sound(o) != null) return o;
            return null;
        }
    }

    /// <summary>Minecraft hearts over a mob's head. Only shown for a few seconds after the mob gets hurt.</summary>
    public class HeartsBar : MonoBehaviour
    {
        private SpriteRenderer sr;
        private float showUntil, flashUntil;
        private float lastHp = -1f;

        public float FlashAmount => Time.time < flashUntil ? 1f : 0f;

        public static HeartsBar Create(Transform parent, float maxHp)
        {
            var go = new GameObject("hearts");
            go.transform.SetParent(parent, false);
            var h = go.AddComponent<HeartsBar>();
            h.sr = go.AddComponent<SpriteRenderer>();
            h.sr.enabled = false;
            return h;
        }

        public void Show(float hp, float maxHp)
        {
            showUntil = Time.time + Balance.F(Balance.Section("mobs"), "heartsSeconds", 4f);
            flashUntil = Time.time + 0.3f;
            if (Mathf.Abs(hp - lastHp) > 0.01f)
            {
                lastHp = hp;
                var tex = Render(hp, maxHp);
                sr.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0f), 9f / 0.12f);
            }
        }

        private void LateUpdate()
        {
            bool on = Time.time < showUntil;
            sr.enabled = on;
            if (!on) return;
            var cam = Game.Cam;
            if (cam != null) transform.rotation = cam.transform.rotation;
        }

        /// <summary>Up to 10 hearts in a row (each heart = a tenth of the health if the mob has a lot).</summary>
        private static Texture2D Render(float hp, float maxHp)
        {
            var container = McAssets.Tex("gui/sprites/hud/heart/container.png");
            var full = McAssets.Tex("gui/sprites/hud/heart/full.png");
            var half = McAssets.Tex("gui/sprites/hud/heart/half.png");
            int hearts = Mathf.Clamp(Mathf.CeilToInt(maxHp / 2f), 1, 10);
            float perHeart = maxHp / hearts;
            int w = hearts * 8 + 1, h = 9;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels(new Color[w * h]);
            for (int i = 0; i < hearts; i++)
            {
                float v = Mathf.Clamp((hp - i * perHeart) / perHeart, 0f, 1f);
                Blit(tex, container, i * 8);
                if (v >= 0.75f) Blit(tex, full, i * 8);
                else if (v > 0.05f) Blit(tex, half, i * 8);
            }
            tex.Apply();
            return tex;
        }

        private static void Blit(Texture2D dst, Texture2D src, int x0)
        {
            try
            {
                for (int y = 0; y < Mathf.Min(9, src.height); y++)
                    for (int x = 0; x < Mathf.Min(9, src.width); x++)
                    {
                        var c = src.GetPixel(x, y);
                        if (c.a > 0.05f && x0 + x < dst.width) dst.SetPixel(x0 + x, y, c);
                    }
            }
            catch { }
        }
    }
}

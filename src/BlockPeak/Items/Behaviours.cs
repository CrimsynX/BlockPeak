using System;
using System.Collections.Generic;
using BlockPeak.Assets;
using BlockPeak.Building;
using BlockPeak.Core;
using BlockPeak.Mobs;
using BlockPeak.Net;
using Peak.Afflictions;
using Photon.Pun;
using UnityEngine;

namespace BlockPeak.Items
{
    /// <summary>Adds the right Minecraft behaviour components to an item template.</summary>
    public static class Behaviours
    {
        public static void Attach(McItemDef def, GameObject go)
        {
            switch (def.Kind)
            {
                case McKind.Food:
                {
                    var reduce = go.GetComponent<Action_ReduceUses>() ?? go.AddComponent<Action_ReduceUses>();
                    reduce.OnCastFinished = true;
                    reduce.consumeOnFullyUsed = true;
                    go.AddComponent<McFood>().OnCastFinished = true;
                    break;
                }
                case McKind.Block:
                case McKind.Torch:
                case McKind.RedstoneTorch:
                case McKind.Ladder:
                case McKind.Tnt:
                    go.AddComponent<McPlace>().OnPressed = true;
                    break;
                case McKind.EnderPearl:
                case McKind.WindCharge:
                    go.AddComponent<McThrow>().OnPressed = true;
                    break;
                case McKind.GoatHorn:
                    go.AddComponent<McHorn>().OnPressed = true;
                    break;
                case McKind.WaterBucket:
                    go.AddComponent<McWater>().OnPressed = true;
                    break;
                case McKind.Sword:
                    go.AddComponent<McSword>().OnPressed = true;
                    break;
                case McKind.Elytra:
                    go.AddComponent<McElytra>();
                    break;
                case McKind.Boat:
                    go.AddComponent<McBoat>();
                    break;
            }
        }

        /// <summary>Use up one from the stack (or the whole item if it does not stack).</summary>
        public static void UseOne(Item item)
        {
            var uses = item.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
            if (!uses.HasData || uses.Value <= 1)
            {
                if (uses.HasData) uses.Value = 0;
                item.StartCoroutine(item.ConsumeDelayed(true));
                return;
            }
            item.photonView.RPC("ReduceUsesRPC", RpcTarget.All);
        }

        public static McItemDef Def(Item item) => ItemDefs.Of(item);
    }

    /// <summary>Blocks, torches, ladders and TNT: click to place.</summary>
    public class McPlace : ItemAction
    {
        public override void RunAction()
        {
            var c = character;
            if (c == null || !c.IsLocal) return;
            var def = Behaviours.Def(item);
            if (def == null) return;
            if (BlockWorld.TryPlaceFromView(def)) Behaviours.UseOne(item);
        }
    }

    /// <summary>Minecraft foods: eat over a short time, then the effects below.</summary>
    public class McFood : ItemAction
    {
        public override void RunAction()
        {
            var c = character;
            if (c == null || !c.IsLocal) return;
            var def = Behaviours.Def(item);
            if (def == null) return;
            var cfg = def.Cfg;
            var aff = c.refs.afflictions;
            float hunger = Balance.F(cfg, "hunger", 0f);
            if (hunger > 0) aff.SubtractStatus(CharacterAfflictions.STATUSTYPE.Hunger, hunger);
            float heal = Balance.F(cfg, "heal", 0f);
            if (heal > 0) aff.SubtractStatus(CharacterAfflictions.STATUSTYPE.Injury, heal);
            float bonus = Balance.F(cfg, "bonusStamina", 0f);
            if (bonus > 0) c.AddExtraStamina(bonus);
            float heatImmune = Balance.F(cfg, "heatImmuneSeconds", 0f);
            if (heatImmune > 0)
            {
                aff.AddAffliction(new Affliction_Sunscreen(heatImmune));
                LocalEffects.HeatImmuneUntil = Mathf.Max(LocalEffects.HeatImmuneUntil, Time.time + heatImmune);
            }
            float half = Balance.F(cfg, "halfInjurySeconds", 0f);
            if (half > 0) LocalEffects.HalfInjuryUntil = Mathf.Max(LocalEffects.HalfInjuryUntil, Time.time + half);
            float poisonChance = Balance.F(cfg, "poisonChance", 0f);
            if (poisonChance > 0 && UnityEngine.Random.value < poisonChance)
                aff.AddStatus(CharacterAfflictions.STATUSTYPE.Poison, Balance.F(cfg, "poison", 0.1f));
            Sfx.At("random/burp", c.Head, 0.6f);
        }
    }

    /// <summary>Ender pearls and wind charges: click to throw.</summary>
    public class McThrow : ItemAction
    {
        private static float lastThrow;

        public override void RunAction()
        {
            var c = character;
            if (c == null || !c.IsLocal || Time.time - lastThrow < 0.5f) return;
            var def = Behaviours.Def(item);
            if (def == null) return;
            lastThrow = Time.time;
            Vector3 dir = Game.CamForward;
            Vector3 from = Game.CamPos + dir * 0.8f;
            float speed = def.Kind == McKind.EnderPearl ? 24f * Mathf.Max(0.5f, Balance.F(def.Cfg, "throwBoost", 1.8f)) / 1.8f : 22f;
            Projectiles.Throw(def.Kind == McKind.EnderPearl ? ProjectileKind.Pearl : ProjectileKind.WindCharge, from, dir * speed + c.data.avarageVelocity * 0.5f);
            Sfx.At("random/bow", from, 0.5f, 0.6f);
            Behaviours.UseOne(item);
        }
    }

    /// <summary>Goat horn: a loud call everyone hears, a marker over you, and nearby mobs run away.</summary>
    public class McHorn : ItemAction
    {
        private static float last = -100f;

        public override void RunAction()
        {
            var c = character;
            if (c == null || !c.IsLocal) return;
            var def = Behaviours.Def(item);
            float cd = Balance.F(def?.Cfg, "cooldown", 7f);
            if (Time.time - last < cd) return;
            last = Time.time;
            int variant = item.data != null ? Mathf.Abs(item.data.guid.GetHashCode()) % 8 : 0;
            Channel.All(Op.Horn, true, c.Head, PhotonNetwork.LocalPlayer?.ActorNumber ?? 0, variant);
        }

        public static float CooldownLeft(McItemDef def) => Mathf.Max(0, Balance.F(def?.Cfg, "cooldown", 7f) - (Time.time - last));
    }

    /// <summary>Water bucket: pour it just before you land to cancel fall damage (Minecraft's "MLG"), or cool down.</summary>
    public class McWater : ItemAction
    {
        public override void RunAction()
        {
            var c = character;
            if (c == null || !c.IsLocal) return;
            var def = Behaviours.Def(item);
            var cfg = def?.Cfg;
            Vector3 hip = c.Center;
            if (!c.data.isGrounded)
            {
                if (Physics.Raycast(hip, Vector3.down, out var hit, 10f, Game.TerrainMask, QueryTriggerInteraction.Ignore))
                {
                    c.refs.movement.CapFallDamage(0f, Balance.F(cfg, "fallSaveWindow", 1.5f) + hit.distance / 10f);
                    Channel.All(Op.Splash, true, hit.point);
                    Behaviours.UseOne(item);
                }
                return;
            }
            var aff = c.refs.afflictions;
            aff.SubtractStatus(CharacterAfflictions.STATUSTYPE.Hot, 0.3f);
            aff.SubtractStatus(CharacterAfflictions.STATUSTYPE.Spores, 0.1f);
            Vector3 at = Physics.Raycast(hip, Vector3.down, out var h2, 3f, Game.TerrainMask, QueryTriggerInteraction.Ignore) ? h2.point : c.data.groundPos;
            Channel.All(Op.Splash, true, at);
            Behaviours.UseOne(item);
        }
    }

    /// <summary>Stone sword: hit mobs in front of you.</summary>
    public class McSword : ItemAction
    {
        private float last;

        public override void RunAction()
        {
            var c = character;
            if (c == null || !c.IsLocal || Time.time - last < 0.6f) return;
            last = Time.time;
            var def = Behaviours.Def(item);
            float dmg = Balance.F(def?.Cfg, "damage", 5f);
            float reach = Balance.F(def?.Cfg, "reach", 3f);
            bool hit = McMobs.LocalMelee(Game.CamPos, Game.CamForward, reach, dmg);
            Sfx.At(hit ? "entity/player/attack/strong" : "entity/player/attack/sweep", c.Head, 0.7f);
            var vis = item.transform.Find("BP_Visual");
            if (vis != null) StartCoroutine(Swing(vis));
        }

        private System.Collections.IEnumerator Swing(Transform vis)
        {
            Quaternion start = vis.localRotation;
            for (float t = 0; t < 0.25f; t += Time.deltaTime)
            {
                float a = Mathf.Sin(t / 0.25f * Mathf.PI) * 60f;
                vis.localRotation = start * Quaternion.Euler(a, 0, 0);
                yield return null;
            }
            vis.localRotation = start;
        }
    }

    /// <summary>
    /// Elytra (12% durability, no fireworks): hold the use button while falling to glide. Look down to dive
    /// faster, look up to slow down. Flying into a wall at speed hurts.
    /// </summary>
    public class McElytra : MonoBehaviour
    {
        private Item item;
        private bool gliding;
        private float lastSpeed;
        private AudioSource loop;
        private float brokenNoticeAt;

        private void Awake() => item = GetComponent<Item>();

        private void FixedUpdate()
        {
            var c = item.holderCharacter;
            bool want = c != null && c.IsLocal && item.itemState == ItemState.Held && item.isUsingPrimary
                        && !c.data.isGrounded && !c.data.isClimbingAnything && c.data.fullyConscious && c.data.sinceGrounded > 0.3f;
            var def = ItemDefs.Of(item);
            float dur = Stacks.Durability(item.data, 0.12f);
            if (want && dur <= 0f)
            {
                want = false;
                if (Time.time > brokenNoticeAt) { brokenNoticeAt = Time.time + 3f; Sfx.At("random/break", c.Head, 0.6f); }
            }
            if (!want)
            {
                if (gliding) StopGlide();
                return;
            }
            if (!gliding) { gliding = true; loop = Sfx.Loop("item/elytra/elytra_loop", transform, 0.5f); }

            var cfg = def?.Cfg;
            float total = Mathf.Max(1f, Balance.F(cfg, "glideSeconds", 52f));
            Stacks.SetDurability(item.data, dur - Time.fixedDeltaTime * Balance.F(cfg, "startDurability", 0.12f) / total);

            float down = Mathf.Clamp01(-Game.CamForward.y);
            float up = Mathf.Clamp01(Game.CamForward.y);
            float yDrag = Mathf.Lerp(0.9f, 0.99f, down) - 0.05f * up;
            float forward = 12f + 26f * down - 8f * up;
            c.refs.movement.ApplyGlider(yDrag, 0.995f, Mathf.Max(0f, forward));
            c.data.sinceGrounded = Mathf.Min(c.data.sinceGrounded, 0.6f); // no fall damage for gliding itself

            float speed = c.data.avarageVelocity.magnitude;
            if (lastSpeed > 14f && speed < lastSpeed * 0.4f)
            {
                float k = Mathf.InverseLerp(14f, 30f, lastSpeed);
                c.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Injury,
                    Mathf.Lerp(Balance.F(cfg, "crashInjuryMin", 0.1f), Balance.F(cfg, "crashInjuryMax", 0.4f), k));
                Sfx.At("damage/hit", c.Head, 0.8f);
            }
            lastSpeed = speed;
        }

        private void StopGlide()
        {
            gliding = false;
            lastSpeed = 0f;
            if (loop != null) Destroy(loop);
        }

        private void OnDisable() => StopGlide();
    }

    /// <summary>Boat: hold the use button to ride. Fast on water, faster on snow and ice, slow on land.</summary>
    public class McBoat : MonoBehaviour
    {
        private Item item;
        private float nextPaddle;

        private void Awake() => item = GetComponent<Item>();

        private void FixedUpdate()
        {
            var c = item.holderCharacter;
            if (c == null || !c.IsLocal || item.itemState != ItemState.Held) return;
            bool riding = item.isUsingPrimary && c.data.fullyConscious && !c.data.isClimbingAnything;
            item.blocksSprint = riding;
            if (!riding) return;
            var cfg = ItemDefs.Of(item)?.Cfg;
            Vector3 fwd = Game.CamForward;
            fwd.y = 0;
            if (fwd.sqrMagnitude < 0.01f) return;
            fwd.Normalize();

            float accel;
            string sound = "entity/boat/paddle_land";
            if (c.data.isInWater) { accel = Balance.F(cfg, "waterSpeed", 9f); sound = "entity/boat/paddle_water"; }
            else if (!c.data.isGrounded) return;
            else if (OnSnowOrIce(c)) accel = Balance.F(cfg, "snowSpeed", 7f);
            else accel = 1.5f;

            // Slopes help going down.
            Vector3 n = c.data.groundNormal.sqrMagnitude > 0.1f ? c.data.groundNormal : Vector3.up;
            Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, n);
            Vector3 push = fwd * accel + downhill * accel * 0.5f;
            foreach (var part in c.refs.ragdoll.partList) part.AddForce(push, ForceMode.Acceleration);
            if (Time.time > nextPaddle)
            {
                nextPaddle = Time.time + 0.9f;
                Sfx.At(sound, c.Center, 0.4f);
            }
        }

        private static bool OnSnowOrIce(Character c)
        {
            if (Physics.Raycast(c.Center, Vector3.down, out var hit, 2.5f, Game.TerrainMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<PlacedBlock>() is PlacedBlock pb && pb.Def != null && pb.Def.Key == "packed_ice") return true;
                var r = hit.collider.GetComponent<Renderer>();
                string m = r != null && r.sharedMaterial != null ? r.sharedMaterial.name.ToLowerInvariant() : "";
                if (m.Contains("snow") || m.Contains("ice")) return true;
            }
            return Game.CurrentBiome == Biome.BiomeType.Alpine;
        }
    }
}

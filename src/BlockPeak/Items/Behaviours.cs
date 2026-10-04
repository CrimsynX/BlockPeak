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
                case McKind.Potion:
                    go.AddComponent<McPotion>().OnCastFinished = true;
                    break;
                case McKind.Block:
                case McKind.Torch:
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
                case McKind.Sword:
                    go.AddComponent<McSword>().OnPressed = true;
                    break;
                case McKind.Boat:
                    go.AddComponent<McBoatPlace>().OnPressed = true;
                    break;
                case McKind.Bow:
                    go.AddComponent<McBow>().OnPressed = true;
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

    /// <summary>
    /// Minecraft foods: eat over a short time, then the effects. Golden apples use PEAK's own effects:
    /// PEAK's med-kit healing, PEAK's bonus stamina and (enchanted) PEAK's milk invincibility, just longer.
    /// </summary>
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
            if (heal > 0) aff.AddAffliction(new Affliction_HealAll { maxHealing = heal, totalTime = 0.1f });
            float bonus = Balance.F(cfg, "bonusStamina", 0f);
            if (bonus > 0) c.AddExtraStamina(bonus);
            float milkMult = Balance.F(cfg, "milkInvincibilityMultiplier", 0f);
            if (milkMult > 0)
                aff.AddAffliction(new Affliction_Invincibility { totalTime = PeakEffects.MilkSeconds * milkMult, isFromMilk = true });
            float poisonChance = Balance.F(cfg, "poisonChance", 0f);
            if (poisonChance > 0 && UnityEngine.Random.value < poisonChance)
                aff.AddStatus(CharacterAfflictions.STATUSTYPE.Poison, Balance.F(cfg, "poison", 0.1f));
            Sfx.At("random/burp", c.Head, 0.6f);
        }
    }

    /// <summary>Potions of swiftness and leaping (PEAK's own speed boost; a jump boost on PEAK's jump).</summary>
    public class McPotion : ItemAction
    {
        public override void RunAction()
        {
            var c = character;
            if (c == null || !c.IsLocal) return;
            var def = Behaviours.Def(item);
            if (def == null) return;
            float seconds = Balance.F(def.Cfg, "seconds", 25f);
            if (def.Effect == "speed")
            {
                c.refs.afflictions.AddAffliction(new Affliction_FasterBoi
                {
                    totalTime = seconds,
                    moveSpeedMod = Balance.F(def.Cfg, "moveSpeed", 0.35f),
                    climbSpeedMod = Balance.F(def.Cfg, "climbSpeed", 0.25f),
                });
                LocalEffects.StartSpeed(seconds);
            }
            else if (def.Effect == "jump")
            {
                LocalEffects.StartJumpBoost(c, Balance.F(def.Cfg, "jumpExtraVelocity", 4f), seconds);
            }
            Sfx.At("random/drink", c.Head, 0.7f);
            item.StartCoroutine(item.ConsumeDelayed(true));
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
            bool hit = MobDirector.LocalMelee(Game.CamPos, Game.CamForward, reach, dmg);
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

    /// <summary>Boat item: click the ground or water to put the boat down (then hold Interact on it to get in).</summary>
    public class McBoatPlace : ItemAction
    {
        public override void RunAction()
        {
            var c = character;
            if (c == null || !c.IsLocal) return;
            if (Building.Boats.TryPlaceFromView()) Behaviours.UseOne(item);
        }
    }
}

using System;
using BlockPeak.Assets;
using BlockPeak.Core;
using BlockPeak.Net;
using BlockPeak.UI;
using HarmonyLib;
using Peak.Afflictions;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace BlockPeak.Items
{
    /// <summary>Timed effects from Minecraft food and the totem, plus the shared visual/sound events.</summary>
    public static class LocalEffects
    {
        public static float HalfInjuryUntil;
        public static float HeatImmuneUntil;

        public static void Reset()
        {
            HalfInjuryUntil = HeatImmuneUntil = 0f;
        }

        public static void RegisterNet()
        {
            Channel.On(Op.TotemPop, (a, s) =>
            {
                Vector3 pos = Channel.Vec(a[0]);
                Sfx.At("item/totem/use_totem", pos, 1f);
                Fx.Burst(pos, new[] { new Color(1f, 0.85f, 0.2f), new Color(0.3f, 0.9f, 0.3f), new Color(1f, 1f, 0.6f) }, 40, 4f, 1.6f);
            });
            Channel.On(Op.Splash, (a, s) =>
            {
                Vector3 pos = Channel.Vec(a[0]);
                Sfx.At("item/bucket/empty", pos, 0.8f);
                Fx.WaterSplash(pos);
            });
            Channel.On(Op.Horn, (a, s) =>
            {
                Vector3 pos = Channel.Vec(a[0]);
                int actor = Channel.Int(a[1]);
                int variant = Channel.Int(a[2]);
                Sfx.At("item/goat_horn/call" + variant, pos, 1f, 1f, 96f);
                var def = ItemDefs.ByKey("goat_horn");
                Fx.Marker(actor, ItemRegistry.IconFor(def), Balance.F(def.Cfg, "markerSeconds", 10f));
                if (PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom)
                    Mobs.McMobs.Scare(pos, 10f, Balance.F(def.Cfg, "scareSeconds", 10f));
            });
        }

        /// <summary>Called when we are about to pass out: a totem anywhere in the hotbar saves us once.</summary>
        public static bool TryTotem(Character c)
        {
            var totem = ItemDefs.ByKey("totem_of_undying");
            if (totem == null || c.player == null) return false;
            ItemSlot found = null;
            foreach (var s in c.player.itemSlots)
                if (!s.IsEmpty() && s.prefab.itemID == totem.Id) { found = s; break; }
            if (found == null && !c.player.tempFullSlot.IsEmpty() && c.player.tempFullSlot.prefab.itemID == totem.Id) found = c.player.tempFullSlot;
            if (found == null) return false;

            var items = c.refs.items;
            if (items.currentSelectedSlot.IsSome && items.currentSelectedSlot.Value == found.itemSlotID && c.data.currentItem != null)
                c.data.currentItem.StartCoroutine(c.data.currentItem.ConsumeDelayed(true));
            else
                c.player.EmptySlot(Optionable<byte>.Some(found.itemSlotID));

            var aff = c.refs.afflictions;
            foreach (var t in new[] { CharacterAfflictions.STATUSTYPE.Poison, CharacterAfflictions.STATUSTYPE.Cold, CharacterAfflictions.STATUSTYPE.Hot, CharacterAfflictions.STATUSTYPE.Drowsy, CharacterAfflictions.STATUSTYPE.Spores })
                aff.SetStatus(t, 0f);
            aff.SubtractStatus(CharacterAfflictions.STATUSTYPE.Injury, 0.45f);
            aff.SubtractStatus(CharacterAfflictions.STATUSTYPE.Hunger, 0.15f);
            var cfg = totem.Cfg;
            aff.AddAffliction(new Affliction_Invincibility { totalTime = Balance.F(cfg, "invincibleSeconds", 5f) });
            float heat = Balance.F(cfg, "heatImmuneSeconds", 40f);
            aff.AddAffliction(new Affliction_Sunscreen(heat));
            HeatImmuneUntil = Mathf.Max(HeatImmuneUntil, Time.time + heat);
            c.data.passOutValue = 0f;
            Channel.All(Op.TotemPop, true, c.Center);
            Plugin.Log.LogInfo("Totem of Undying used.");
            return true;
        }
    }

    [HarmonyPatch(typeof(Character), "HandleLife")]
    internal static class Character_HandleLife_Patch
    {
        private static float lastTry;

        private static bool Prefix(Character __instance)
        {
            var c = __instance;
            if (!c.IsLocal || c.data.isSkeleton || c.data.dead || c.data.passedOut) return true;
            if (!c.refs.afflictions.shouldPassOut) return true;
            if (Time.time - lastTry < 0.5f) return true;
            lastTry = Time.time;
            try { return !LocalEffects.TryTotem(c); }
            catch (Exception e) { Health.Report("totem", e); return true; }
        }
    }

    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddStatus))]
    internal static class CharacterAfflictions_AddStatus_Patch
    {
        private static bool Prefix(CharacterAfflictions __instance, CharacterAfflictions.STATUSTYPE statusType, ref float amount, ref bool __result)
        {
            if (__instance.character == null || !__instance.character.IsLocal) return true;
            if (statusType == CharacterAfflictions.STATUSTYPE.Injury && Time.time < LocalEffects.HalfInjuryUntil) amount *= 0.5f;
            if (statusType == CharacterAfflictions.STATUSTYPE.Hot && Time.time < LocalEffects.HeatImmuneUntil)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}

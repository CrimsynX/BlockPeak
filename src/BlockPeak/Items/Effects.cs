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
            EndJumpBoost();
        }

        // ---- jump boost (potion of leaping): an extra upward push right after PEAK's own jump.
        //      (Scaling PEAK's jump impulse did nothing noticeable, so the boost is added on top.)
        private static float jumpUntil, jumpExtra;
        private static float lastSinceJump = 10f, pushAt = -1f;
        public static float SpeedUntil, SpeedSeconds = 1f, JumpSeconds = 1f;

        public static float JumpBoostLeft => Mathf.Max(0f, jumpUntil - Time.time);

        public static void StartJumpBoost(Character c, float extraVelocity, float seconds)
        {
            jumpExtra = extraVelocity;
            jumpUntil = Time.time + seconds;
            JumpSeconds = seconds;
        }

        public static void StartSpeed(float seconds)
        {
            SpeedUntil = Time.time + seconds;
            SpeedSeconds = seconds;
        }

        public static void Tick()
        {
            var c = Game.LocalChar;
            if (c == null) { pushAt = -1f; return; }
            // A real jump resets sinceJump (JumpRpc; the jetpack doesn't). PEAK pushes 0.1 s later, ours right after.
            float sj = c.data.sinceJump;
            if (sj < 0.05f && lastSinceJump > 0.2f && Time.time < jumpUntil) pushAt = Time.time + 0.12f;
            lastSinceJump = sj;
            if (pushAt > 0f && Time.time >= pushAt)
            {
                pushAt = -1f;
                if (!c.data.isClimbing && !c.data.isRopeClimbing) c.AddForce(Vector3.up * (jumpExtra / Time.fixedDeltaTime), 1f, 1f);
            }
        }

        private static void EndJumpBoost()
        {
            jumpUntil = 0f;
            SpeedUntil = 0f;
        }

        // ---- Minecraft's effect icons (top right) while a potion is active
        private static Texture2D effectBg;

        public static void Draw()
        {
            if (Game.LocalChar == null) return;
            int scale = Mathf.Max(2, Screen.height / 360);
            float x = Screen.width - 8 * scale;
            float y = 8 * scale;
            DrawEffect(ref x, y, scale, "mob_effect/speed.png", SpeedUntil - Time.time, SpeedSeconds);
            DrawEffect(ref x, y, scale, "mob_effect/jump_boost.png", jumpUntil - Time.time, JumpSeconds);
        }

        private static void DrawEffect(ref float x, float y, int scale, string icon, float left, float total)
        {
            if (left <= 0f) return;
            if (effectBg == null) effectBg = McAssets.Tex("gui/sprites/hud/effect_background.png");
            x -= 24 * scale;
            // Minecraft fades the icon in and out during the last 10 seconds.
            float alpha = left > 10f ? 1f : 0.5f + 0.5f * Mathf.Abs(Mathf.Cos(left * Mathf.PI * 0.5f));
            var old = GUI.color;
            GUI.DrawTexture(new Rect(x, y, 24 * scale, 24 * scale), effectBg);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(x + 3 * scale, y + 3 * scale, 18 * scale, 18 * scale), McAssets.Tex(icon));
            GUI.color = old;
            int secs = Mathf.CeilToInt(left);
            var txt = McFont.Render(secs / 60 + ":" + (secs % 60).ToString("00"), Color.white);
            if (txt != null) GUI.DrawTexture(new Rect(x + 12 * scale - txt.width * scale / 4f, y + 25 * scale, txt.width * scale / 2f, txt.height * scale / 2f), txt);
            x -= 2 * scale;
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
                    Mobs.MobDirector.Scare(pos, 10f, Balance.F(def.Cfg, "scareSeconds", 10f));
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
            if (UI.Creative.BlocksStatus(statusType)) { __result = false; return false; }
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

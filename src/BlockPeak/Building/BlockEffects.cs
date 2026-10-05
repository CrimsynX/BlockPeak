using BlockPeak.Assets;
using BlockPeak.Core;
using UnityEngine;

namespace BlockPeak.Building
{
    /// <summary>
    /// Special placed blocks, felt by the local scout:
    /// - slime block: landing on it bounces you back up (no fall damage); crouch to land without bouncing,
    /// - magma block: standing on it burns (heat) unless you crouch, like Minecraft.
    /// </summary>
    public static class BlockEffects
    {
        /// <summary>
        /// Until this time the local scout takes no fall damage AND is not knocked over by a hard landing.
        /// (PEAK's CapFallDamage only caps the injury; the knock-over happens before it.)
        /// </summary>
        public static float NoFallUntil;
        public static void GuardFall(float seconds) => NoFallUntil = Mathf.Max(NoFallUntil, Time.time + seconds);

        private static bool armed;
        private static float armedVy, magmaTick;

        public static void Tick()
        {
            var c = Game.LocalChar;
            if (c == null || c.data.dead) { armed = false; return; }
            float vy = c.data.avarageVelocity.y;
            Vector3 feet = Feet(c);

            // Slime: about to land on one -> no fall damage, then bounce.
            if (vy < -4f && !c.data.isCrouching && Under(feet, Mathf.Max(1.5f, -vy * 0.25f)) == "slime")
            {
                c.refs.movement.CapFallDamage(0f, 0.6f);
                GuardFall(0.6f);
                armed = true;
                armedVy = Mathf.Min(armedVy, vy);
            }
            if (armed && (c.data.isGrounded || vy > -0.5f))
            {
                if (Under(feet, 0.6f) == "slime" && !c.data.isCrouching)
                {
                    float up = Mathf.Min(-armedVy * Balance.F(Balance.ItemCfg("blocks"), "slimeBounce", 0.8f), 25f);
                    if (up > 3f)
                    {
                        c.AddForce(Vector3.up * (up / Time.fixedDeltaTime), 1f, 1f);
                        c.data.sinceGrounded = Mathf.Min(c.data.sinceGrounded, 0.1f);
                        c.refs.movement.CapFallDamage(0f, 1.5f);
                        GuardFall(1.5f);
                        Sfx.At("mob/slime/big", feet, 0.8f);
                    }
                }
                armed = false;
                armedVy = 0f;
            }

            // Magma: hot to stand on (crouching is safe, like Minecraft).
            magmaTick -= Time.deltaTime;
            if (magmaTick <= 0f && c.data.isGrounded && !c.data.isCrouching && Under(feet, 0.5f) == "magma")
            {
                magmaTick = 1f;
                if (!UI.Creative.BlocksStatus(CharacterAfflictions.STATUSTYPE.Hot))
                    c.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Hot, Balance.F(Balance.ItemCfg("blocks"), "magmaHeat", 0.03f));
                Sfx.At("random/fizz", feet, 0.4f);
            }
        }

        private static Vector3 Feet(Character c)
        {
            var l = c.GetBodypart(BodypartType.Foot_L);
            var r = c.GetBodypart(BodypartType.Foot_R);
            if (l != null && r != null) return (l.transform.position + r.transform.position) * 0.5f;
            return c.Center - Vector3.up * 0.9f;
        }

        /// <summary>The special kind of the placed block right under this point, or null.</summary>
        private static string Under(Vector3 feet, float dist)
        {
            if (!Physics.Raycast(feet + Vector3.up * 0.3f, Vector3.down, out var hit, dist + 0.3f, Game.TerrainMask, QueryTriggerInteraction.Ignore)) return null;
            var pb = hit.collider != null ? hit.collider.GetComponent<PlacedBlock>() : null;
            return pb != null && pb.Def != null ? pb.Def.Special : null;
        }
    }

    /// <summary>Skips PEAK's landing check (injury and knock-over) while <see cref="BlockEffects.NoFallUntil"/> is active.</summary>
    [HarmonyLib.HarmonyPatch(typeof(CharacterMovement), "CheckFallDamage")]
    internal static class CheckFallDamage_Guard_Patch
    {
        private static bool Prefix(CharacterMovement __instance)
        {
            var c = __instance.character;
            return !(c != null && c.IsLocal && Time.time < BlockEffects.NoFallUntil);
        }
    }
}

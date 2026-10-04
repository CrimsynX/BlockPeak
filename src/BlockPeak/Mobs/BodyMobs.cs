using System;
using System.Collections.Generic;
using System.Linq;
using BlockPeak.Building;
using BlockPeak.Core;
using BlockPeak.Items;
using BlockPeak.Net;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Photon.Pun;
using UnityEngine;

namespace BlockPeak.Mobs
{
    /// <summary>
    /// Night mobs and the warden live on PEAK's own zombie body: the same physics, walking/running animations,
    /// climbing, collisions and network sync as PEAK's mushroom zombies. Their PEAK looks are hidden and the
    /// Minecraft model is hung on the body's bones (see <see cref="BodyMob"/>). The host runs their brains.
    /// </summary>
    public static class BodyMobs
    {
        public const string Tag = "bpmob";
        public static readonly Dictionary<int, BodyMob> All = new Dictionary<int, BodyMob>();
        private static string prefabName;
        private static bool prefabSearched;
        private static float nextSpawn;
        private static bool wasNight;
        public static bool ForceNight;

        private static JToken M => Balance.Section("mobs");
        public static int Count => All.Values.Count(m => m != null && !m.Dying);
        private static bool IsHost => !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode;

        public static void RegisterNet()
        {
            Channel.On(Op.BodyHit, (a, s) =>
            {
                if (!IsHost) return;
                if (All.TryGetValue(Channel.Int(a[0]), out var m) && m != null)
                {
                    m.HostDamage(Channel.Flt(a[1]), Channel.Vec(a[2]));
                    float down = a.Length > 3 ? Channel.Flt(a[3]) : 0f;
                    if (down > 0f && !m.Dying && m.C != null) m.C.Fall(down);
                }
            });
            Channel.On(Op.BodyHurt, (a, s) =>
            {
                if (All.TryGetValue(Channel.Int(a[0]), out var m) && m != null) m.OnHurt(Channel.Flt(a[1]));
            });
            Channel.On(Op.BodyDied, (a, s) =>
            {
                if (All.TryGetValue(Channel.Int(a[0]), out var m) && m != null) m.OnDied(Channel.Byte(a[1]));
            });
            Channel.On(Op.BodyFlags, (a, s) =>
            {
                if (All.TryGetValue(Channel.Int(a[0]), out var m) && m != null) m.Flags = Channel.Byte(a[1]);
            });
        }

        /// <summary>PEAK's zombie prefab (in Resources, so Photon can spawn it).</summary>
        public static string PrefabName
        {
            get
            {
                if (prefabName != null || prefabSearched) return prefabName;
                prefabSearched = true;
                foreach (var n in new[] { "MushroomZombie", "MushroomZombie_NPC", "Zombie", "MushroomZombie_Player" })
                {
                    try
                    {
                        var go = Resources.Load<GameObject>(n);
                        if (go != null && go.GetComponent<MushroomZombie>() != null && go.GetComponent<Character>() != null)
                        {
                            prefabName = n;
                            Plugin.Log.LogInfo("Mobs use PEAK's zombie body '" + n + "'.");
                            break;
                        }
                    }
                    catch { }
                }
                if (prefabName == null) Health.Report("mobs", "PEAK's zombie body was not found; mobs are disabled.");
                return prefabName;
            }
        }

        public static IEnumerable<Character> Players =>
            Character.AllCharacters.Where(c => c != null && c.IsPlayerControlled && !c.data.dead);

        /// <summary>Host: spawn a mob body at a spot.</summary>
        public static BodyMob HostSpawn(string type, Vector3 pos, float yaw)
        {
            if (!IsHost || PrefabName == null || !PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode) return null;
            try
            {
                var go = PhotonNetwork.InstantiateRoomObject(PrefabName, pos + Vector3.up * 0.2f, Quaternion.Euler(0, yaw, 0), 0, new object[] { Tag, type });
                return go != null ? go.GetComponent<BodyMob>() : null;
            }
            catch (Exception e) { Health.Report("mob-spawn", e); return null; }
        }

        public static BodyMob HostSpawnNear(Character c, string type, float distance = 6f)
        {
            if (c == null) return null;
            Vector3 f = c.data.lookDirection; f.y = 0;
            f = f.sqrMagnitude < 0.01f ? Vector3.forward : f.normalized;
            Vector3 probe = c.Center + f * distance;
            Vector3 pos = probe;
            if (Physics.Raycast(probe + Vector3.up * 10f, Vector3.down, out var hit, 40f, Game.TerrainMask, QueryTriggerInteraction.Ignore)) pos = hit.point;
            float yaw = Quaternion.LookRotation(-f).eulerAngles.y;
            return HostSpawn(type, pos, yaw);
        }

        public static int HostKillAll()
        {
            int n = 0;
            foreach (var m in All.Values.ToList()) if (m != null && !m.Dying) { m.HostDie(0); n++; }
            return n;
        }

        // ------------------------------------------------------------------ night spawning (host)

        public static void Tick()
        {
            if (!IsHost || !Game.InRun) return;
            bool night = Game.IsNight || ForceNight;
            if (night != wasNight) wasNight = night;
            if (Modes.GameModes.Active != Modes.ModeKind.None) return; // game modes bring their own mobs
            if (!Balance.B(M, "enabled", true) || !night || Time.time < nextSpawn) return;
            nextSpawn = Time.time + Mathf.Max(3f, Balance.F(M, "spawnIntervalSeconds", 20f));
            TrySpawnRound();
        }

        private static void TrySpawnRound()
        {
            float chance = Balance.F(M, "spawnChance", 0.35f);
            int maxPer = Balance.I(M, "maxPerPlayer", 2);
            var players = Players.Where(c => !c.data.passedOut).ToList();
            if (players.Count == 0 || Count >= maxPer * players.Count) return;
            var types = (M["biomes"]?[Game.CurrentBiome.ToString()] as JArray)?.Select(t => (string)t).ToList();
            if (types == null || types.Count == 0) return;
            foreach (var p in players)
            {
                if (Count >= maxPer * players.Count) break;
                int near = All.Values.Count(m => m != null && (m.Position - p.Center).sqrMagnitude < 40f * 40f);
                if (near >= maxPer || UnityEngine.Random.value > chance) continue;
                string type = PickType(types);
                if (type == null || !FindSpawnPoint(p, out var pos)) continue;
                HostSpawn(type, pos, Quaternion.LookRotation(Flat(p.Center - pos)).eulerAngles.y);
            }
        }

        private static string PickType(List<string> types)
        {
            var options = new List<string>();
            string creepers = Balance.S(M, "creepers", "Rare");
            foreach (var t in types)
            {
                if (t == "creeper")
                {
                    if (creepers == "Off") continue;
                    if (creepers == "Rare" && UnityEngine.Random.value > Balance.F(M["types"]?[t], "rare", 0.25f)) continue;
                }
                options.Add(t);
            }
            return options.Count == 0 ? null : options[UnityEngine.Random.Range(0, options.Count)];
        }

        public static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude < 1e-4f ? Vector3.forward : v.normalized; }

        public static bool FindSpawnPoint(Character p, out Vector3 pos, float min = -1f, float max = -1f)
        {
            if (min < 0) min = Balance.F(M, "minDistance", 16f);
            if (max < 0) max = Balance.F(M, "maxDistance", 32f);
            float vert = Balance.F(M, "noSpawnVerticalFromClimber", 8f);
            float noLight = Balance.F(M, "noSpawnNearLightRadius", 12f);
            for (int i = 0; i < 12; i++)
            {
                float ang = UnityEngine.Random.Range(0f, Mathf.PI * 2f), dist = UnityEngine.Random.Range(min, max);
                Vector3 probe = p.Center + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * dist;
                if (!Physics.Raycast(probe + Vector3.up * 25f, Vector3.down, out var hit, 60f, Game.TerrainMask, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y < 0.6f) continue;
                if (Mathf.Abs(hit.point.y - p.Center.y) > vert) continue;
                if (BlockWorld.LightNear(hit.point, noLight)) continue;
                if (NearCampfire(hit.point, noLight)) continue;
                if (Players.Any(c => (c.Center - hit.point).sqrMagnitude < min * min * 0.8f)) continue;
                pos = hit.point;
                return true;
            }
            pos = default;
            return false;
        }

        private static bool NearCampfire(Vector3 p, float r)
        {
            try
            {
                foreach (var c in Campfire.ALL_CAMPFIRES)
                    if (c != null && (c.transform.position - p).sqrMagnitude < r * r) return true;
            }
            catch { }
            return false;
        }

        // ------------------------------------------------------------------ combat helpers

        /// <summary>Local player swings: hit the body mob in front. True if one was in reach.</summary>
        public static bool LocalMelee(Vector3 origin, Vector3 dir, float reach, float damage, float knockDown = 0f)
        {
            BodyMob best = null;
            float bestD = reach + 0.01f;
            foreach (var m in All.Values)
            {
                if (m == null || m.Dying) continue;
                foreach (var p in m.HitPoints())
                {
                    Vector3 to = p - origin;
                    float along = Vector3.Dot(to, dir);
                    if (along < 0 || along > reach + m.Radius) continue;
                    float off = Vector3.Distance(origin + dir * along, p);
                    if (off > m.Radius + 0.25f) continue;
                    if (along < bestD) { bestD = along; best = m; }
                }
            }
            if (best == null) return false;
            Channel.Host(Op.BodyHit, best.ViewId, damage, dir, knockDown);
            return true;
        }

        public static void HostExplosion(Vector3 at, float radius)
        {
            if (!IsHost) return;
            foreach (var m in All.Values.ToList())
            {
                if (m == null || m.Dying) continue;
                float d = Vector3.Distance(m.Position, at);
                if (d > radius * 1.6f) continue;
                m.HostDamage(20f * (1f - d / (radius * 1.6f)), (m.Position - at).normalized * 1.5f);
            }
        }

        /// <summary>Host: a vibration the warden can hear (explosions, blocks, horns, landings...).</summary>
        public static void HostVibration(Vector3 at, float strength)
        {
            if (!IsHost) return;
            foreach (var m in All.Values)
                if (m != null && !m.Dying && m.Type == "warden") m.HostHear(at, strength, null);
        }

        public static void HostPush(Vector3 at, float radius, float launch)
        {
            if (!IsHost) return;
            foreach (var m in All.Values)
                if (m != null && !m.Dying && Vector3.Distance(m.Position, at) < radius) m.Push((m.Position - at).normalized * launch + Vector3.up * launch * 0.6f);
        }

        public static void Scare(Vector3 at, float radius, float seconds)
        {
            foreach (var m in All.Values)
                if (m != null && (m.Position - at).sqrMagnitude < radius * radius && m.Type != "warden") m.ScareFrom(at, seconds);
        }

        public static bool AnyNear(Vector3 p, float r)
        {
            foreach (var m in All.Values)
                if (m != null && !m.Dying && m.HitPoints().Any(h => (h - p).sqrMagnitude < (r + m.Radius) * (r + m.Radius))) return true;
            return false;
        }

        public static void Clear() => All.Clear();
    }

    // ====================================================================== PEAK zombie patches

    [HarmonyPatch(typeof(MushroomZombie), "Awake")]
    internal static class MushroomZombie_Awake_Patch
    {
        private static void Postfix(MushroomZombie __instance)
        {
            try
            {
                var data = __instance.GetComponent<PhotonView>()?.InstantiationData;
                if (data == null || data.Length < 2 || !(data[0] is string tag) || tag != BodyMobs.Tag) return;
                __instance.gameObject.AddComponent<BodyMob>().Init(__instance, data[1] as string ?? "zombie");
            }
            catch (Exception e) { Health.Report("mob-body", e); }
        }
    }

    [HarmonyPatch(typeof(MushroomZombie), "Start")]
    internal static class MushroomZombie_Start_Patch
    {
        private static bool Prefix(MushroomZombie __instance)
        {
            var m = __instance.GetComponent<BodyMob>();
            if (m == null) return true;
            __instance.character.isZombie = true;
            return false; // no sleeping, no PEAK grunts, no mushroom reveal
        }
    }

    [HarmonyPatch(typeof(MushroomZombie), "Update")]
    internal static class MushroomZombie_Update_Patch
    {
        private static bool Prefix(MushroomZombie __instance)
        {
            var m = __instance.GetComponent<BodyMob>();
            if (m == null) return true;
            if (__instance.photonView.IsMine) m.Brain();
            return false;
        }
    }

    [HarmonyPatch(typeof(MushroomZombie), "RPC_PlaySFX")]
    internal static class MushroomZombie_Sfx_Patch
    {
        private static bool Prefix(MushroomZombie __instance) => __instance.GetComponent<BodyMob>() == null;
    }

    [HarmonyPatch(typeof(MushroomZombie), nameof(MushroomZombie.ReadyToDisable))]
    internal static class MushroomZombie_ReadyToDisable_Patch
    {
        private static bool Prefix(MushroomZombie __instance, ref bool __result)
        {
            if (__instance.GetComponent<BodyMob>() == null) return true;
            __result = false; // BlockPeak removes its own mobs
            return false;
        }
    }

    [HarmonyPatch(typeof(MushroomZombie), "OnBitCharacter")]
    internal static class MushroomZombie_Bite_Patch
    {
        private static bool Prefix(MushroomZombie __instance) => __instance.GetComponent<BodyMob>() == null;
    }
}

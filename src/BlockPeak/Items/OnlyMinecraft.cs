using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BlockPeak.Core;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Photon.Pun;
using UnityEngine;

namespace BlockPeak.Items
{
    /// <summary>
    /// "Only Minecraft items": every item PEAK puts on the mountain becomes a Minecraft item. PEAK spawns world items
    /// from many places (luggage, berry bushes and vines, items lying on the ground, single item spots, breakables),
    /// so instead of patching each list, BlockPeak marks "a world spawner is running" around all of them and swaps the
    /// item right where PEAK creates it. Items players drop or throw are never touched (no spawner is running then).
    /// Kept as they are: the start area around the plane, and anything on the keep list (gems, flares, passport...).
    /// </summary>
    public static class OnlyMinecraft
    {
        [ThreadStatic] private static int depth;
        [ThreadStatic] private static object currentSpawner;
        private static List<string> keep;
        public static int Swapped { get; private set; }

        public static void Enter(object spawner) { depth++; if (spawner != null) currentSpawner = spawner; }
        public static void Exit() { depth = Mathf.Max(0, depth - 1); if (depth == 0) currentSpawner = null; }

        public static void ResetRun() { Swapped = 0; keep = null; }

        /// <summary>The Minecraft item that replaces this PEAK item, or null to keep it.</summary>
        public static string Replace(string prefabName, Vector3 position)
        {
            if (depth <= 0 || !ItemRegistry.Ready || !PhotonNetwork.IsMasterClient) return null;
            if (!Modes.CustomOptions.OnlyMinecraftItems) return null;
            if (string.IsNullOrEmpty(prefabName)) return null;
            string bare = prefabName.StartsWith("0_Items/") ? prefabName.Substring(8) : prefabName;
            if (bare.StartsWith("BlockPeak_")) return null;
            var M = Balance.Section("modes")["minecraftItemsOnly"] ?? new JObject();
            if (keep == null) keep = (M["keep"] as JArray)?.Select(t => ((string)t).ToLowerInvariant()).ToList() ?? new List<string>();
            string lower = bare.ToLowerInvariant();
            if (keep.Any(k => lower.Contains(k))) return null;
            if (currentSpawner is RespawnChest) return null;
            float startRadius = Balance.F(M, "startAreaRadius", 40f);
            if (SpawnPoint.allSpawnPoints != null && SpawnPoint.allSpawnPoints.Any(s => s != null && Vector3.Distance(s.transform.position, position) < startRadius)) return null;

            string pool = currentSpawner is Luggage l ? Loot.PoolName(l.GetSpawnPool()) : null;
            if (pool == null || !Loot.IsBiomePool(pool) && !Loot.IsRarePool(pool)) pool = Spawner_GetObjectsToSpawn_Patch.PoolForBiome(Game.CurrentBiome);
            var def = Loot.Roll(pool, Loot.IsRarePool(pool)) ?? Loot.Roll(pool, false);
            var t = Loot.TemplateFor(def);
            if (t == null) return null;
            Swapped++;
            return prefabName.StartsWith("0_Items/") ? "0_Items/" + t.name : t.name;
        }
    }

    /// <summary>Marks every PEAK world-item spawner while it runs.</summary>
    [HarmonyPatch]
    internal static class WorldSpawnScope_Patch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var list = new List<MethodBase>
            {
                AccessTools.Method(typeof(Spawner), nameof(Spawner.SpawnItems)),
                AccessTools.Method(typeof(BerryBush), nameof(BerryBush.SpawnItems)),
                AccessTools.Method(typeof(BerryVine), nameof(BerryVine.SpawnItems)),
                AccessTools.Method(typeof(GroundPlaceSpawner), nameof(GroundPlaceSpawner.SpawnItems)),
                AccessTools.Method(typeof(SingleItemSpawner), nameof(SingleItemSpawner.TrySpawnItems)),
            };
            return list.Where(m => m != null).Distinct();
        }

        private static void Prefix(object __instance) => OnlyMinecraft.Enter(__instance);
        private static Exception Finalizer(Exception __exception) { OnlyMinecraft.Exit(); return __exception; }
    }

    [HarmonyPatch(typeof(PhotonNetwork), nameof(PhotonNetwork.InstantiateItemRoom))]
    internal static class InstantiateItemRoom_OnlyMinecraft_Patch
    {
        private static void Prefix(ref string prefabName, Vector3 position)
        {
            try { var r = OnlyMinecraft.Replace(prefabName, position); if (r != null) prefabName = r; }
            catch (Exception e) { Health.Report("only-minecraft", e); }
        }
    }

    [HarmonyPatch(typeof(PhotonNetwork), nameof(PhotonNetwork.Instantiate))]
    internal static class Instantiate_OnlyMinecraft_Patch
    {
        private static void Prefix(ref string prefabName, Vector3 position)
        {
            try
            {
                if (prefabName == null || !prefabName.StartsWith("0_Items/")) return;
                var r = OnlyMinecraft.Replace(prefabName, position);
                if (r != null) prefabName = r;
            }
            catch (Exception e) { Health.Report("only-minecraft", e); }
        }
    }
}

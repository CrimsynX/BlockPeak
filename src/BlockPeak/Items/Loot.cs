using System;
using System.Collections.Generic;
using System.Linq;
using BlockPeak.Assets;
using BlockPeak.Core;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Photon.Pun;
using UnityEngine;

namespace BlockPeak.Items
{
    /// <summary>
    /// Puts Minecraft items into PEAK's luggage. The host decides (PEAK only spawns loot on the host).
    /// A share of each luggage's rolls becomes Minecraft items; now and then a whole luggage is a "Minecraft chest".
    /// </summary>
    public static class Loot
    {
        private static readonly Dictionary<string, int> runCounts = new Dictionary<string, int>();
        public static int SpawnedThisRun { get; private set; }

        public static void ResetRun()
        {
            runCounts.Clear();
            SpawnedThisRun = 0;
        }

        private static JToken L => Balance.Section("loot");

        public static string PoolName(SpawnPool pool)
        {
            foreach (SpawnPool v in Enum.GetValues(typeof(SpawnPool)))
                if (v != SpawnPool.None && v != SpawnPool.All && pool.HasFlag(v) && v.ToString().StartsWith("Luggage")) return v.ToString();
            return pool.ToString();
        }

        private static bool InList(string key, string pool) => (L[key] as JArray)?.Any(t => (string)t == pool) == true;

        public static bool IsBiomePool(string pool) => InList("biomePools", pool);
        public static bool IsRarePool(string pool) => InList("rarePoolsOnly", pool);

        /// <summary>One random Minecraft item suited to this luggage, honouring the per-run caps. Null if none fits.</summary>
        public static McItemDef Roll(string pool, bool rareOnly)
        {
            var weights = L["weights"] as JObject;
            if (weights == null) return null;
            var mult = L["multipliers"]?[pool] as JObject;
            var rare = new HashSet<string>((L["rareItems"] as JArray)?.Select(t => (string)t) ?? Enumerable.Empty<string>());
            var caps = L["perRunCaps"] as JObject;
            var options = new List<(McItemDef def, float w)>();
            foreach (var p in weights.Properties())
            {
                string key = p.Name;
                if (rareOnly && !rare.Contains(key) && key != "golden_apple") continue;
                float w = p.Value.Value<float>();
                if (mult != null && mult[key] != null) w *= mult[key].Value<float>();
                if (w <= 0) continue;
                if (caps?[key] != null && runCounts.TryGetValue(key, out int used) && used >= caps[key].Value<int>()) continue;
                McItemDef def = key == "blocks" ? BlockFor(pool) : ItemDefs.ByKey(key);
                if (def == null || !ItemRegistry.Templates.ContainsKey(def.Id)) continue;
                options.Add((def, w));
            }
            if (options.Count == 0) return null;
            float total = options.Sum(o => o.w), r = UnityEngine.Random.value * total;
            foreach (var o in options)
            {
                r -= o.w;
                if (r <= 0) return Count(o.def);
            }
            return Count(options[options.Count - 1].def);
        }

        private static McItemDef Count(McItemDef def)
        {
            runCounts.TryGetValue(def.Key, out int n);
            runCounts[def.Key] = n + 1;
            SpawnedThisRun++;
            return def;
        }

        public static McItemDef BlockFor(string pool)
        {
            string key = L["blockForPool"]?[pool]?.ToString();
            var def = ItemDefs.ByKey(key);
            if (def != null) return def;
            var blocks = ItemDefs.Blocks.ToList();
            return blocks[UnityEngine.Random.Range(0, blocks.Count)];
        }

        public static GameObject TemplateFor(McItemDef def) =>
            def != null && ItemRegistry.Templates.TryGetValue(def.Id, out var t) && t != null ? t.gameObject : null;
    }

    [HarmonyPatch(typeof(Spawner), "GetObjectsToSpawn")]
    internal static class Spawner_GetObjectsToSpawn_Patch
    {
        private static void Postfix(Spawner __instance, ref List<GameObject> __result)
        {
            try
            {
                if (__result == null || !ItemRegistry.Ready) return;
                if (!PhotonNetwork.IsMasterClient) return;
                if (Modes.CustomOptions.On(Modes.CustomOptions.McItemsOnly)) { OnlyMinecraft(__instance, __result); return; }
                if (!(__instance is Luggage)) return;
                string pool = Loot.PoolName(__instance.GetSpawnPool());
                bool biome = Loot.IsBiomePool(pool), rare = Loot.IsRarePool(pool);
                if (!biome && !rare) return;
                var L = Balance.Section("loot");
                float share = rare ? Balance.F(L, "ancientShare", 0.2f) : Balance.F(L, "minecraftShare", 0.15f);

                if (biome && UnityEngine.Random.value < Balance.F(L, "chestChance", 0.1f))
                {
                    // A Minecraft chest: only Minecraft items.
                    int min = Balance.I(L, "chestItemsMin", 1), max = Balance.I(L, "chestItemsMax", 3);
                    int n = Mathf.Clamp(UnityEngine.Random.Range(min, max + 1), 1, __result.Count);
                    for (int i = 0; i < __result.Count; i++)
                        __result[i] = i < n ? Loot.TemplateFor(Loot.Roll(pool, false)) ?? __result[i] : null;
                    McChests.Mark(__instance as Luggage);
                    Health.Verbose($"Minecraft chest in {pool} with {n} items");
                    return;
                }
                for (int i = 0; i < __result.Count; i++)
                {
                    if (UnityEngine.Random.value >= share) continue;
                    var t = Loot.TemplateFor(Loot.Roll(pool, rare));
                    if (t != null) __result[i] = t;
                }
            }
            catch (Exception e) { Health.Report("loot", e); }
        }

        /// <summary>
        /// "Only Minecraft items" custom run: every item a spawner makes becomes a Minecraft item, except the start
        /// area (BingBong, passport...), the respawn chests and gems/crystals (keep list in balance.json).
        /// </summary>
        private static void OnlyMinecraft(Spawner sp, List<GameObject> result)
        {
            if (sp is RespawnChest) return;
            var M = Balance.Section("modes")["minecraftItemsOnly"] ?? new JObject();
            float startRadius = Balance.F(M, "startAreaRadius", 40f);
            Vector3 pos = sp.transform.position;
            if (SpawnPoint.allSpawnPoints != null)
                foreach (var s in SpawnPoint.allSpawnPoints)
                    if (s != null && Vector3.Distance(s.transform.position, pos) < startRadius) return;
            var keep = (M["keep"] as JArray)?.Select(t => ((string)t).ToLowerInvariant()).ToList() ?? new List<string>();

            string pool = sp is Luggage ? Loot.PoolName(sp.GetSpawnPool()) : PoolForBiome(Game.CurrentBiome);
            if (!Loot.IsBiomePool(pool) && !Loot.IsRarePool(pool)) pool = PoolForBiome(Game.CurrentBiome);
            bool rare = Loot.IsRarePool(pool);
            for (int i = 0; i < result.Count; i++)
            {
                var go = result[i];
                if (go == null) continue;
                if (go.GetComponent<Item>() == null || go.GetComponent<McItem>() != null) continue;
                string n = go.name.ToLowerInvariant();
                if (keep.Any(k => n.Contains(k))) continue;
                var t = Loot.TemplateFor(Loot.Roll(pool, rare)) ?? Loot.TemplateFor(Loot.Roll(pool, false));
                if (t != null) result[i] = t;
            }
        }

        private static string PoolForBiome(Biome.BiomeType b)
        {
            string s = b.ToString().ToLowerInvariant();
            if (s.Contains("tropic") || s.Contains("jungle")) return "LuggageJungle";
            if (s.Contains("root")) return "LuggageRoots";
            if (s.Contains("alpine") || s.Contains("tundra") || s.Contains("snow")) return "LuggageTundra";
            if (s.Contains("mesa") || s.Contains("desert")) return "LuggageMesa";
            if (s.Contains("volcano") || s.Contains("caldera")) return "LuggageCaldera";
            if (s.Contains("peak") || s.Contains("kiln")) return "LuggageClimber";
            return "LuggageBeach";
        }
    }

    /// <summary>Luggage that rolled as a Minecraft chest plays the chest sound when opened.</summary>
    public static class McChests
    {
        private static readonly HashSet<int> marked = new HashSet<int>();

        public static void Mark(Luggage l)
        {
            if (l == null) return;
            marked.Add(l.GetInstanceID());
            Net.Channel.All(Net.Op.Sound, true, "random/chestopen", l.transform.position, 0.8f);
        }

        public static void Reset() => marked.Clear();
    }
}

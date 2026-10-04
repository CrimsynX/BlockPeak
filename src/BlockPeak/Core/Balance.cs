using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Photon.Pun;

namespace BlockPeak.Core
{
    /// <summary>
    /// All gameplay numbers. Loaded from BepInEx/config/BlockPeak/balance.json (created from the built-in default on first run).
    /// In a lobby the host's numbers win: the host publishes them as a room property and everyone else uses that copy.
    /// </summary>
    public static class Balance
    {
        public const string RoomKey = "bp_bal";

        private static JObject local;
        private static JObject active;
        public static bool UsingHostCopy { get; private set; }
        private static string lastAdopted;

        public static JObject Root => active ?? local ?? new JObject();

        public static string LocalPath => Path.Combine(Plugin.DataDir, "balance.json");

        public static void LoadLocal()
        {
            try
            {
                string defaultText = ReadEmbeddedDefault();
                if (File.Exists(LocalPath))
                {
                    // A balance file from an older BlockPeak would hide this version's new numbers: keep a copy, start fresh.
                    try
                    {
                        int have = JObject.Parse(File.ReadAllText(LocalPath)).Value<int?>("version") ?? 0;
                        int want = JObject.Parse(defaultText).Value<int?>("version") ?? 0;
                        if (have < want)
                        {
                            string backup = Path.Combine(Plugin.DataDir, $"balance.v{have}.backup.json");
                            File.Copy(LocalPath, backup, true);
                            File.Delete(LocalPath);
                            Plugin.Log.LogInfo($"balance.json was from an older BlockPeak; saved it as {Path.GetFileName(backup)} and wrote the new default.");
                        }
                    }
                    catch { /* a broken file is reported below */ }
                }
                if (!File.Exists(LocalPath))
                {
                    File.WriteAllText(LocalPath, defaultText);
                    Plugin.Log.LogInfo("Wrote default balance.json to " + LocalPath);
                }
                var def = JObject.Parse(defaultText);
                JObject user;
                try { user = JObject.Parse(File.ReadAllText(LocalPath)); }
                catch (Exception e)
                {
                    Health.Report("balance", "balance.json has a typo, using defaults: " + e.Message);
                    user = new JObject();
                }
                // Defaults fill anything missing from the user's file (so new versions add new keys).
                def.Merge(user, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace, MergeNullValueHandling = MergeNullValueHandling.Ignore });
                local = def;
                ApplyConfigKnobs(local);
            }
            catch (Exception e)
            {
                Health.Report("balance", e);
                local = new JObject();
            }
        }

        private static string ReadEmbeddedDefault()
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("BlockPeak.balance.json"))
            using (var r = new StreamReader(s))
                return r.ReadToEnd();
        }

        /// <summary>The simple knobs in the .cfg file override the JSON equivalents.</summary>
        private static void ApplyConfigKnobs(JObject o)
        {
            o["loot"]["minecraftShare"] = Cfg.McLootShare.Value;
            o["loot"]["chestChance"] = Cfg.McChestChance.Value;
            o["building"]["placedBlockLimit"] = Cfg.PlacedBlockLimit.Value;
            o["mobs"]["enabled"] = Cfg.MobsEnabled.Value;
            o["mobs"]["maxPerPlayer"] = Cfg.MaxMobsPerPlayer.Value;
            o["mobs"]["spawnChance"] = (float)o["mobs"].Value<float>("spawnChance") * Cfg.MobDensity.Value;
            o["mobs"]["creepers"] = Cfg.Creepers.Value;
        }

        /// <summary>Host: publish our numbers to the room. Client: adopt the host's numbers.</summary>
        public static void SyncWithRoom()
        {
            try
            {
                if (!PhotonNetwork.InRoom) { active = null; UsingHostCopy = false; return; }
                if (PhotonNetwork.IsMasterClient)
                {
                    active = null;
                    UsingHostCopy = false;
                    string json = local.ToString(Newtonsoft.Json.Formatting.None);
                    PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(RoomKey, out object cur);
                    if ((cur as string) != json)
                    {
                        var h = new ExitGames.Client.Photon.Hashtable { [RoomKey] = json };
                        PhotonNetwork.CurrentRoom.SetCustomProperties(h);
                    }
                }
                else if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(RoomKey, out object v) && v is string s)
                {
                    if (s != lastAdopted)
                    {
                        lastAdopted = s;
                        active = JObject.Parse(s);
                        UsingHostCopy = true;
                        Plugin.Log.LogInfo("Using the host's balance numbers.");
                    }
                }
            }
            catch (Exception e) { Health.Report("balance-sync", e); }
        }

        // ---- typed helpers ----
        public static JToken Section(string name) => Root[name] ?? new JObject();
        public static JToken ItemCfg(string key) => Root["items"]?[key] ?? new JObject();

        public static float F(JToken t, string key, float fallback)
        {
            var v = t?[key];
            if (v == null || v.Type == JTokenType.Null) return fallback;
            try { return v.Value<float>(); } catch { return fallback; }
        }

        public static int I(JToken t, string key, int fallback)
        {
            var v = t?[key];
            if (v == null || v.Type == JTokenType.Null) return fallback;
            try { return v.Value<int>(); } catch { return fallback; }
        }

        public static bool B(JToken t, string key, bool fallback)
        {
            var v = t?[key];
            if (v == null || v.Type == JTokenType.Null) return fallback;
            try { return v.Value<bool>(); } catch { return fallback; }
        }

        public static string S(JToken t, string key, string fallback)
        {
            var v = t?[key];
            if (v == null || v.Type == JTokenType.Null) return fallback;
            return v.ToString();
        }

        public static (int min, int max) Range(JToken t, string key, int a, int b)
        {
            var arr = t?[key] as JArray;
            if (arr == null || arr.Count < 2) return (a, b);
            int lo = arr[0].Value<int>(), hi = arr[1].Value<int>();
            return lo <= hi ? (lo, hi) : (hi, lo);
        }
    }
}

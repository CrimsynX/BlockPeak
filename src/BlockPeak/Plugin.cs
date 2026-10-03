using System;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using BlockPeak.Core;
using HarmonyLib;
using UnityEngine;

namespace BlockPeak
{
    /// <summary>
    /// BlockPeak: Minecraft hotbar, items and night mobs inside PEAK.
    /// Entry point. Everything else hangs off <see cref="Runner"/>.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.blockpeak.mod";
        public const string Name = "BlockPeak";
        public const string Version = "0.1.1";

        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        /// <summary>Folder the DLL lives in (BepInEx/plugins/BlockPeak).</summary>
        public static string PluginDir { get; private set; }

        /// <summary>BepInEx/config/BlockPeak (balance.json, logs written by the mod).</summary>
        public static string DataDir { get; private set; }

        public static Harmony Harmony { get; private set; }

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            PluginDir = Path.GetDirectoryName(Info.Location);
            DataDir = Path.Combine(Paths.ConfigPath, "BlockPeak");
            Directory.CreateDirectory(DataDir);

            Log.LogInfo($"{Name} {Version} starting (PEAK {Application.version}, Unity {Application.unityVersion})");

            Cfg.Bind(Config);
            if (Cfg.SafeMode.Value)
            {
                Log.LogWarning("SafeMode is ON in the config: BlockPeak does nothing this session.");
                return;
            }

            Balance.LoadLocal();

            var go = new GameObject("BlockPeak.Runner");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<Runner>();

            Harmony = new Harmony(Guid);
            ApplyPatches();
        }

        /// <summary>
        /// Patch each class separately so one broken patch (after a PEAK update) does not take the whole mod down.
        /// </summary>
        private static void ApplyPatches()
        {
            int ok = 0, failed = 0;
            foreach (var type in typeof(Plugin).Assembly.GetTypes().Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0))
            {
                try
                {
                    Harmony.CreateClassProcessor(type).Patch();
                    ok++;
                }
                catch (Exception e)
                {
                    failed++;
                    Log.LogError($"Patch group {type.Name} failed and is disabled: {e.GetBaseException().Message}");
                    Health.Report("patch:" + type.Name, e);
                }
            }
            Log.LogInfo($"Harmony: {ok} patch groups applied, {failed} failed.");
        }
    }
}

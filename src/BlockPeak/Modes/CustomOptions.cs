using System;
using System.Collections.Generic;
using System.Linq;
using BlockPeak.Core;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPeak.Modes
{
    /// <summary>
    /// BlockPeak's own check boxes in PEAK's "Custom run" window. They only take effect in custom runs, and only the
    /// host's choices count (the host runs the modes).
    /// </summary>
    public static class CustomOptions
    {
        public class Option
        {
            public string Key, Label, Help;
            public bool Value
            {
                get
                {
                    try { return PlayerPrefs.GetInt("BlockPeak.opt." + Key, 0) == 1; }
                    catch { return false; }
                }
                set
                {
                    try { PlayerPrefs.SetInt("BlockPeak.opt." + Key, value ? 1 : 0); PlayerPrefs.Save(); }
                    catch { }
                }
            }
        }

        public static readonly Option WardenChase = new Option { Key = "warden_chase", Label = "Minecraft: Warden Chase", Help = "10 s after the first scout moves, the warden comes. It can smell you anywhere." };
        public static readonly Option ZombieChase = new Option { Key = "zombie_chase", Label = "Minecraft: Zombie Chase", Help = "10 s after the first scout moves, a huge zombie horde comes for you." };
        public static readonly Option McItemsOnly = new Option { Key = "mc_items_only", Label = "Minecraft: Only Minecraft items", Help = "Every item found is a Minecraft item (the start area and gems stay as they are)." };
        public static readonly Option AnvilRain = new Option { Key = "anvil_rain", Label = "Minecraft: Anvil rain", Help = "Now and then it rains anvils. Look up!" };
        public static readonly Option TntRain = new Option { Key = "tnt_rain", Label = "Minecraft: TNT rain", Help = "Now and then it rains lit TNT." };
        public static readonly Option StarterKit = new Option { Key = "starter_kit", Label = "Minecraft: Starter kit", Help = "Every scout starts with blocks, torches, cookies and an ender pearl." };

        public static readonly Option ChestsRare = new Option { Key = "chests_rare", Label = "Minecraft: Chests (rare)", Help = "Minecraft items only come from Minecraft chests scattered over the map. Few chests." };
        public static readonly Option ChestsNormal = new Option { Key = "chests_normal", Label = "Minecraft: Chests (normal)", Help = "Minecraft items only come from Minecraft chests scattered over the map." };
        public static readonly Option ChestsCommon = new Option { Key = "chests_common", Label = "Minecraft: Chests (common)", Help = "Minecraft items only come from Minecraft chests scattered over the map. Lots of chests." };

        public static readonly List<Option> All = new List<Option> { WardenChase, ZombieChase, McItemsOnly, ChestsRare, ChestsNormal, ChestsCommon, AnvilRain, TntRain, StarterKit };

        private static readonly Option[] ChestOptions = { ChestsRare, ChestsNormal, ChestsCommon };

        /// <summary>Minecraft chests in this run: null = off, else "rare" / "normal" / "common".</summary>
        public static string ChestFrequency =>
            On(ChestsCommon) ? "common" : On(ChestsNormal) ? "normal" : On(ChestsRare) ? "rare" : null;

        /// <summary>Is this option in effect for the current run (custom run + ticked)?</summary>
        public static bool On(Option o) => o.Value && (RunSettings.IsCustomRun || Cfg.Debug && Balance.B(Balance.Section("modes"), "debugIgnoresCustomRun", false));

        public static void Set(Option o, bool v)
        {
            o.Value = v;
            // The two chase modes are exclusive.
            if (v && o == WardenChase) ZombieChase.Value = false;
            if (v && o == ZombieChase) WardenChase.Value = false;
            // One chest amount at a time, and chests and "only Minecraft items" exclude each other.
            if (v && ChestOptions.Contains(o)) { foreach (var c in ChestOptions) if (c != o) c.Value = false; McItemsOnly.Value = false; }
            if (v && o == McItemsOnly) foreach (var c in ChestOptions) c.Value = false;
            foreach (var t in toggles) if (t.Value != null) t.Value.SetIsOnWithoutNotify(t.Key.Value);
        }

        private static readonly List<KeyValuePair<Option, Toggle>> toggles = new List<KeyValuePair<Option, Toggle>>();
        private static bool uiFailed;
        private static CustomOptionsWindow window;

        /// <summary>Copy one of PEAK's own check-box rows for each BlockPeak option.</summary>
        public static void Inject(CustomOptionsWindow w)
        {
            window = w;
            toggles.Clear();
            try
            {
                var template = w.GetComponentsInChildren<CustomOptionToggle>(true).FirstOrDefault();
                if (template == null) { uiFailed = true; return; }
                var parent = template.transform.parent;
                int index = 0;
                foreach (var o in All)
                {
                    var go = UnityEngine.Object.Instantiate(template.gameObject, parent);
                    go.name = "BlockPeak_" + o.Key;
                    foreach (var lt in go.GetComponentsInChildren<LocalizedText>(true)) UnityEngine.Object.DestroyImmediate(lt);
                    foreach (var tip in go.GetComponentsInChildren<CustomOptionTooltip>(true)) UnityEngine.Object.DestroyImmediate(tip);
                    var old = go.GetComponent<CustomOptionToggle>();
                    var toggle = old != null ? old.toggle : go.GetComponentInChildren<Toggle>(true);
                    if (old != null) UnityEngine.Object.DestroyImmediate(old);
                    var text = go.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault();
                    if (text != null) text.text = o.Label;
                    foreach (var b in go.GetComponentsInChildren<Button>(true)) b.onClick = new Button.ButtonClickedEvent();
                    if (toggle != null)
                    {
                        toggle.onValueChanged = new Toggle.ToggleEvent();
                        toggle.SetIsOnWithoutNotify(o.Value);
                        var opt = o;
                        toggle.onValueChanged.AddListener(v => Set(opt, v));
                        toggles.Add(new KeyValuePair<Option, Toggle>(o, toggle));
                        foreach (var b in go.GetComponentsInChildren<Button>(true)) b.onClick.AddListener(() => toggle.isOn = !toggle.isOn);
                    }
                    go.transform.SetSiblingIndex(index++);
                }
                Plugin.Log.LogInfo("Added BlockPeak options to PEAK's custom run window.");
            }
            catch (Exception e)
            {
                uiFailed = true;
                Health.Report("custom-options-ui", e);
            }
        }

        /// <summary>If copying PEAK's check boxes failed, draw simple ones next to the window instead.</summary>
        public static void Draw()
        {
            if (!uiFailed || window == null || !window.gameObject.activeInHierarchy) return;
            var r = new Rect(20, Screen.height * 0.2f, 330, 30 + All.Count * 28);
            GUI.Box(r, "BlockPeak (custom runs)");
            float y = r.y + 26;
            foreach (var o in All)
            {
                bool v = GUI.Toggle(new Rect(r.x + 10, y, 310, 24), o.Value, " " + o.Label.Replace("Minecraft: ", ""));
                if (v != o.Value) Set(o, v);
                y += 28;
            }
        }
    }

    [HarmonyPatch(typeof(CustomOptionsWindow), "Initialize")]
    internal static class CustomOptionsWindow_Initialize_Patch
    {
        private static void Postfix(CustomOptionsWindow __instance) => CustomOptions.Inject(__instance);
    }
}

using System;
using System.Linq;
using BlockPeak.Assets;
using BlockPeak.Core;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPeak.Modes
{
    /// <summary>
    /// BlockPeak's custom-run settings. PEAK's Custom run window gets ONE extra row ("MINECRAFT  OPTIONS"); its button
    /// opens a Minecraft-style options screen with buttons that cycle through their values (like Minecraft's own
    /// option buttons). Settings only take effect in custom runs, and only the host's count.
    /// </summary>
    public static class CustomOptions
    {
        // ---- stored values (PlayerPrefs, per player; the host's are the ones used)
        public static readonly string[] ChaseNames = { "Off", "Warden", "Zombie horde" };
        public static readonly string[] ItemNames = { "Mixed", "Only Minecraft", "Chests (few)", "Chests", "Chests (many)" };
        public static readonly string[] RainNames = { "Off", "Anvils", "TNT", "Anvils + TNT" };

        public static int Chase { get => Get("chase", 0, ChaseNames.Length); set => Put("chase", value); }
        public static int Items { get => Get("items", 0, ItemNames.Length); set => Put("items", value); }
        public static int Rain { get => Get("rain", 0, RainNames.Length); set => Put("rain", value); }
        public static bool Kit { get => Get("kit", 0, 2) == 1; set => Put("kit", value ? 1 : 0); }

        private static int Get(string k, int def, int count)
        {
            try { return Mathf.Clamp(PlayerPrefs.GetInt("BlockPeak.v3." + k, def), 0, count - 1); }
            catch { return def; }
        }

        private static void Put(string k, int v)
        {
            try { PlayerPrefs.SetInt("BlockPeak.v3." + k, v); PlayerPrefs.Save(); } catch { }
            RefreshRow();
        }

        /// <summary>Are BlockPeak's custom-run settings in effect for this run?</summary>
        public static bool InEffect => RunSettings.IsCustomRun || Cfg.Debug && Balance.B(Balance.Section("modes"), "debugIgnoresCustomRun", false);

        public static bool WardenChase => InEffect && Chase == 1;
        public static bool ZombieChase => InEffect && Chase == 2;
        public static bool OnlyMinecraftItems => InEffect && Items == 1;
        /// <summary>Minecraft chests in this run: null = off, else "rare" / "normal" / "common".</summary>
        public static string ChestFrequency => !InEffect ? null : Items == 2 ? "rare" : Items == 3 ? "normal" : Items == 4 ? "common" : null;
        public static bool AnvilRain => InEffect && (Rain == 1 || Rain == 3);
        public static bool TntRain => InEffect && (Rain == 2 || Rain == 3);
        public static bool StarterKit => InEffect && Kit;

        public static string Summary
        {
            get
            {
                int n = (Chase > 0 ? 1 : 0) + (Items > 0 ? 1 : 0) + (Rain > 0 ? 1 : 0) + (Kit ? 1 : 0);
                return n == 0 ? "OFF" : n + " ON";
            }
        }

        // ================================================================== the row in PEAK's window

        private static CustomOptionsWindow window;
        private static TMP_Text rowValue;
        private static bool rowFailed;

        public static void Inject(CustomOptionsWindow w)
        {
            window = w;
            try
            {
                // Copy PEAK's own "Expedition type" row (a label + a button) and turn it into "MINECRAFT [OPTIONS]".
                var template = w.GetComponentsInChildren<CustomOptionMulti>(true).FirstOrDefault(m => m.gameObject.name != "BlockPeak_Row");
                if (template == null) { rowFailed = true; return; }
                foreach (Transform t in template.transform.parent)
                    if (t.name == "BlockPeak_Row") UnityEngine.Object.Destroy(t.gameObject);
                var go = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent);
                go.name = "BlockPeak_Row";
                var multi = go.GetComponent<CustomOptionMulti>();
                var button = multi != null ? multi.button : go.GetComponentInChildren<Button>(true);
                var labelText = multi != null && multi.label != null ? multi.label.GetComponent<TMP_Text>() : null;
                var valueText = multi != null && multi.buttonText != null ? multi.buttonText.GetComponent<TMP_Text>() : null;
                var buttonImage = multi != null ? multi.buttonImage : null;
                var adjusted = multi != null ? multi.adjustedColor : Color.green;
                foreach (var lt in go.GetComponentsInChildren<LocalizedText>(true)) UnityEngine.Object.DestroyImmediate(lt);
                foreach (var tip in go.GetComponentsInChildren<CustomOptionTooltip>(true)) UnityEngine.Object.DestroyImmediate(tip);
                if (multi != null) UnityEngine.Object.DestroyImmediate(multi);
                var texts = go.GetComponentsInChildren<TMP_Text>(true);
                if (labelText == null) labelText = texts.FirstOrDefault();
                if (valueText == null) valueText = texts.LastOrDefault();
                if (labelText != null) labelText.text = "MINECRAFT";
                rowValue = valueText;
                if (buttonImage != null) buttonImage.color = adjusted;
                if (button != null)
                {
                    button.onClick = new Button.ButtonClickedEvent();
                    button.onClick.AddListener(() => OptionsScreen.Open(w));
                }
                go.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
                RefreshRow();
                Plugin.Log.LogInfo("Added the MINECRAFT row to PEAK's custom run window.");
            }
            catch (Exception e)
            {
                rowFailed = true;
                Health.Report("custom-options-ui", e);
            }
        }

        private static void RefreshRow()
        {
            if (rowValue != null) rowValue.text = "OPTIONS: " + Summary;
        }

        /// <summary>Draws the options screen, and a fallback button if PEAK's window could not be extended.</summary>
        public static void Draw()
        {
            OptionsScreen.Draw();
            if (!rowFailed || window == null || !window.gameObject.activeInHierarchy || OptionsScreen.IsOpen) return;
            if (GUI.Button(new Rect(20, Screen.height * 0.2f, 240, 32), "Minecraft options: " + Summary)) OptionsScreen.Open(window);
        }
    }

    /// <summary>
    /// Minecraft-style options screen (dimmed background, Minecraft buttons and font) drawn over PEAK's window.
    /// PEAK's own buttons are switched off while it is open.
    /// </summary>
    public static class OptionsScreen
    {
        public static bool IsOpen { get; private set; }
        private static GraphicRaycaster[] blocked = new GraphicRaycaster[0];
        private static CustomOptionsWindow owner;
        private static Texture2D dim;

        public static void Open(CustomOptionsWindow w)
        {
            IsOpen = true;
            owner = w;
            try
            {
                blocked = w != null ? w.GetComponentsInParent<Canvas>(true).Select(c => c.GetComponent<GraphicRaycaster>()).Where(g => g != null && g.enabled).ToArray() : new GraphicRaycaster[0];
                foreach (var g in blocked) g.enabled = false;
            }
            catch { blocked = new GraphicRaycaster[0]; }
            Sfx.Ui("random/click", 1f);
        }

        public static void Close()
        {
            IsOpen = false;
            foreach (var g in blocked) if (g != null) g.enabled = true;
            blocked = new GraphicRaycaster[0];
        }

        public static void Draw()
        {
            if (!IsOpen) return;
            if (owner == null || !owner.gameObject.activeInHierarchy) { Close(); return; }
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { Close(); e.Use(); return; }
            if (dim == null) { dim = new Texture2D(1, 1); dim.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.78f)); dim.Apply(); }
            GUI.depth = -1000;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), dim);
            int s = Mathf.Max(2, Mathf.Min(Screen.height / 250, Screen.width / 420));
            float cx = Screen.width / 2f;
            float y = Screen.height * 0.14f;
            Label("BlockPeak - Minecraft options", cx, y, s, Color.white);
            y += 13 * s;
            Label("Custom runs only. Only the host's choices count.", cx, y, s * 0.5f, new Color(0.65f, 0.65f, 0.65f));
            y += 18 * s;

            if (Button("Chase: " + CustomOptions.ChaseNames[CustomOptions.Chase], cx, y, s)) CustomOptions.Chase = (CustomOptions.Chase + 1) % CustomOptions.ChaseNames.Length;
            Hint(ChaseHint(CustomOptions.Chase), cx, y + 22 * s, s);
            y += 38 * s;
            if (Button("Items: " + CustomOptions.ItemNames[CustomOptions.Items], cx, y, s)) CustomOptions.Items = (CustomOptions.Items + 1) % CustomOptions.ItemNames.Length;
            Hint(ItemsHint(CustomOptions.Items), cx, y + 22 * s, s);
            y += 38 * s;
            if (Button("Rain: " + CustomOptions.RainNames[CustomOptions.Rain], cx, y, s)) CustomOptions.Rain = (CustomOptions.Rain + 1) % CustomOptions.RainNames.Length;
            Hint(CustomOptions.Rain == 0 ? "Nothing falls from the sky." : "Every few minutes it rains " + CustomOptions.RainNames[CustomOptions.Rain].ToLowerInvariant() + " around every scout.", cx, y + 22 * s, s);
            y += 38 * s;
            if (Button("Starter kit: " + (CustomOptions.Kit ? "ON" : "OFF"), cx, y, s)) CustomOptions.Kit = !CustomOptions.Kit;
            Hint("16 blocks, 4 torches, 4 cookies and an ender pearl for everyone.", cx, y + 22 * s, s);
            y += 44 * s;
            if (Button("Done", cx, y, s)) Close();
        }

        private static string ChaseHint(int v) =>
            v == 1 ? "The warden rises 10 s after the first scout leaves the start. It is blind: crouch!" :
            v == 2 ? "A huge zombie horde comes 10 s after the first scout leaves the start." :
            "No chase.";

        private static string ItemsHint(int v) =>
            v == 1 ? "Every item on the mountain is a Minecraft item (start area, gems and flares stay)." :
            v >= 2 ? "Minecraft items are only in chests next to PEAK's luggage. Copper chests hold the best loot." :
            "Some luggage holds Minecraft items, the rest is normal PEAK loot.";

        // ---- drawing helpers (Minecraft widgets + font)

        private static bool Button(string text, float cx, float y, int s)
        {
            var r = new Rect(cx - 100 * s, y, 200 * s, 20 * s);
            bool hover = r.Contains(Event.current.mousePosition);
            string path = hover ? "gui/sprites/widget/button_highlighted.png" : "gui/sprites/widget/button.png";
            if (McAssets.HasTexture(path)) GUI.DrawTexture(r, McAssets.Tex(path));
            else GUI.Box(r, GUIContent.none);
            Label(text, cx, y + 6 * s, s, hover ? new Color(1f, 1f, 0.63f) : new Color(0.88f, 0.88f, 0.88f));
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && hover)
            {
                Event.current.Use();
                Sfx.Ui("random/click", 1f);
                return true;
            }
            return false;
        }

        private static void Hint(string text, float cx, float y, int s) => Label(text, cx, y, s * 0.5f, new Color(0.7f, 0.7f, 0.7f));

        private static GUIStyle fallback;

        private static void Label(string text, float cx, float y, float scale, Color c)
        {
            var tex = McFont.Render(text, c);
            if (tex != null)
            {
                float w = tex.width * scale, h = tex.height * scale;
                GUI.DrawTexture(new Rect(cx - w / 2f, y, w, h), tex);
                return;
            }
            if (fallback == null) fallback = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter };
            fallback.fontSize = Mathf.RoundToInt(scale * 8);
            fallback.normal.textColor = c;
            GUI.Label(new Rect(cx - 400, y, 800, scale * 12), text, fallback);
        }
    }

    [HarmonyPatch(typeof(CustomOptionsWindow), "Initialize")]
    internal static class CustomOptionsWindow_Initialize_Patch
    {
        private static void Postfix(CustomOptionsWindow __instance) => CustomOptions.Inject(__instance);
    }

    /// <summary>While the Minecraft options screen is open, Escape / Back only closes it, not PEAK's window too.</summary>
    [HarmonyPatch(typeof(MenuWindow), "TestCloseViaInput")]
    internal static class MenuWindow_TestCloseViaInput_Patch
    {
        private static bool Prefix(MenuWindow __instance)
        {
            if (!OptionsScreen.IsOpen || !(__instance is CustomOptionsWindow)) return true;
            try
            {
                var input = Zorro.Core.Singleton<UIInputHandler>.Instance;
                if (input != null && input.cancelWasPressed) { input.cancelWasPressed = false; OptionsScreen.Close(); }
            }
            catch { }
            return false;
        }
    }

    /// <summary>Close the options screen when PEAK closes its window.</summary>
    [HarmonyPatch(typeof(CustomOptionsWindow), "OnClose")]
    internal static class CustomOptionsWindow_OnClose_Patch
    {
        private static void Postfix() { if (OptionsScreen.IsOpen) OptionsScreen.Close(); }
    }
}

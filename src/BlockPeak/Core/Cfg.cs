using BepInEx.Configuration;
using UnityEngine;

namespace BlockPeak.Core
{
    /// <summary>Player-facing settings (BepInEx/config/com.blockpeak.mod.cfg, editable in Gale or a text editor).</summary>
    public static class Cfg
    {
        public static ConfigEntry<bool> SafeMode;
        public static ConfigEntry<bool> ShowWelcome;
        public static ConfigEntry<bool> VerboseLog;

        public static ConfigEntry<int> HotbarSlots;
        public static ConfigEntry<int> HotbarScale;
        public static ConfigEntry<int> HotbarOffsetY;
        public static ConfigEntry<bool> RawNumberKeys;
        public static ConfigEntry<KeyCode> BackpackKey;
        public static ConfigEntry<bool> MouseWheelSwitchesSlots;

        public static ConfigEntry<KeyCode> QuickPlaceKey;
        public static ConfigEntry<KeyCode> FireworkKey;

        public static ConfigEntry<bool> MobsEnabled;
        public static ConfigEntry<float> MobDensity;
        public static ConfigEntry<int> MaxMobsPerPlayer;
        public static ConfigEntry<string> Creepers;

        public static ConfigEntry<float> McLootShare;
        public static ConfigEntry<float> McChestChance;
        public static ConfigEntry<int> PlacedBlockLimit;

        public static ConfigEntry<string> MinecraftPath;
        public static ConfigEntry<string> PreferMinecraftVersion;
        public static ConfigEntry<float> McSoundVolume;
        public static ConfigEntry<string> ItemShaderOverride;
        public static ConfigEntry<bool> TestMode;

        /// <summary>Test/debug mode as it was when PEAK started (changing the .cfg mid-game does not unlock it).</summary>
        public static bool Debug { get; private set; }
        public static ConfigEntry<KeyCode> TestMenuKey;

        public static void Bind(ConfigFile c)
        {
            SafeMode = c.Bind("General", "SafeMode", false, "Turn the whole mod off without uninstalling it.");
            ShowWelcome = c.Bind("General", "ShowWelcome", true, "Show the short 'BlockPeak loaded' message in the Airport.");
            VerboseLog = c.Bind("General", "VerboseLog", false, "Write extra detail to BepInEx/LogOutput.log (useful when reporting a bug).");

            HotbarSlots = c.Bind("Hotbar", "HotbarSlots", 9, new ConfigDescription("Number of hotbar slots. 3 = vanilla PEAK. Everyone in the lobby must use the same number.", new AcceptableValueRange<int>(3, 9)));
            HotbarScale = c.Bind("Hotbar", "HotbarScale", 0, new ConfigDescription("Size of the Minecraft hotbar. 0 = automatic, 1-6 = Minecraft GUI scale.", new AcceptableValueRange<int>(0, 6)));
            HotbarOffsetY = c.Bind("Hotbar", "HotbarOffsetY", 0, "Move the hotbar up (positive) or down (negative), in screen pixels.");
            RawNumberKeys = c.Bind("Hotbar", "RawNumberKeys", true, "Also read the keyboard number keys 1-9 directly, even if PEAK's own Hotbar5-9 bindings are empty.");
            BackpackKey = c.Bind("Hotbar", "BackpackKey", KeyCode.Alpha0, "Key that selects the backpack (shown as the offhand slot). PEAK's own 'Select backpack' binding also works.");
            MouseWheelSwitchesSlots = c.Bind("Hotbar", "MouseWheelSwitchesSlots", true, "Mouse wheel scrolls through the hotbar like Minecraft. Hold Alt to send the wheel to the held item instead (rope, etc.).");

            QuickPlaceKey = c.Bind("Building", "QuickPlaceKey", KeyCode.F, "While climbing: place a block from your hotbar into the wall under your feet.");
            FireworkKey = c.Bind("Building", "FireworkKey", KeyCode.R, "While gliding with the elytra: use its one firework boost (the Use button works too).");
            PlacedBlockLimit = c.Bind("Building", "PlacedBlockLimit", 200, "Most blocks that can be placed in one run (host decides).");

            MobsEnabled = c.Bind("Mobs", "Enabled", true, "Spawn Minecraft mobs at night (host decides).");
            MobDensity = c.Bind("Mobs", "Density", 1.0f, new ConfigDescription("Multiplies the night spawn chance (host decides).", new AcceptableValueRange<float>(0f, 3f)));
            MaxMobsPerPlayer = c.Bind("Mobs", "MaxPerPlayer", 2, new ConfigDescription("Mobs alive at once, per player (host decides).", new AcceptableValueRange<int>(0, 6)));
            Creepers = c.Bind("Mobs", "Creepers", "Rare", new ConfigDescription("Creepers in the Tropics (host decides).", new AcceptableValueList<string>("Off", "Rare", "Normal")));

            McLootShare = c.Bind("Loot", "MinecraftShare", 0.15f, new ConfigDescription("Share of luggage item rolls that become Minecraft items (host decides).", new AcceptableValueRange<float>(0f, 0.9f)));
            McChestChance = c.Bind("Loot", "MinecraftChestChance", 0.10f, new ConfigDescription("Chance that a regular luggage is a Minecraft chest full of Minecraft items (host decides).", new AcceptableValueRange<float>(0f, 1f)));

            MinecraftPath = c.Bind("Minecraft", "MinecraftPath", "", "Leave empty to find Minecraft automatically (Modrinth App, official launcher, Prism, CurseForge). Otherwise: the folder that contains 'versions' and 'assets', e.g. C:\\Users\\you\\AppData\\Roaming\\ModrinthApp\\meta");
            PreferMinecraftVersion = c.Bind("Minecraft", "PreferVersion", "26.3", "Which installed Minecraft version to take textures and sounds from. Falls back to the newest one found.");
            McSoundVolume = c.Bind("Audio", "MinecraftSoundVolume", 0.8f, new ConfigDescription("Volume of Minecraft sounds.", new AcceptableValueRange<float>(0f, 1f)));

            TestMode = c.Bind("Testing", "TestMode", false, "Debug mode, read when PEAK starts: the / command chat (/time set, /gamemode, /summon, /kill @e, /heal, /help) and the F6 item/mob menu. Switch it with the setup program before starting PEAK. In multiplayer the HOST must have it on too.");
            TestMenuKey = c.Bind("Testing", "TestMenuKey", KeyCode.F6, "Opens and closes the test menu (when TestMode is on).");
            Debug = TestMode.Value;
            ItemShaderOverride = c.Bind("Debug", "ItemShaderOverride", "", "Advanced: name of a shader to draw Minecraft items with if they look wrong (e.g. 'Universal Render Pipeline/Lit'). Empty = copy PEAK's item material.");
        }

        public static int ExtraSlotCount => Mathf.Clamp(HotbarSlots?.Value ?? 9, 3, 9) - 3;
    }
}

# Troubleshooting

First stop: **`PEAK\BepInEx\LogOutput.log`** (setup → *Open game log*). BlockPeak's lines are tagged
`:BlockPeak]`. When a feature fails, BlockPeak switches just that feature off, keeps the rest running, writes the
reason to the log, and the Airport shows "N BlockPeak feature(s) had problems".

For a bug report, set `VerboseLog = true` in `BepInEx\config\com.blockpeak.mod.cfg`, play until it happens, and
send `LogOutput.log` plus `BepInEx\config\BlockPeak\peak-items.txt`.

| Problem | Try this |
|---|---|
| PEAK crashes or closes at start after installing | Make sure PEAK is up to date in Steam. In Steam → PEAK → Properties → Launch options add `-dx12`. Still crashing: setup → **Mods OFF**, check that PEAK starts vanilla, then report the log. |
| Nothing changed in game | Did the game start through Steam with mods ON? The log should say `BlockPeak 0.2.0 starting`. If there is no `BepInEx\LogOutput.log` at all, BepInEx is not running: press Install / Update again. |
| Grey/purple checker textures instead of Minecraft ones | Minecraft 26.3 was not found. Start Minecraft 26.3 once in the Modrinth App, then setup → **Copy Minecraft textures**. If Minecraft is somewhere unusual, set `MinecraftPath` in the .cfg to the folder that contains `versions` and `assets` (for the Modrinth App: `%AppData%\ModrinthApp\meta`). |
| No Minecraft sounds | Same as above; the launcher must have downloaded the sounds (start the game once). |
| Minecraft items are invisible, black or pink | Set `ItemShaderOverride` in the .cfg (Debug section), e.g. `Universal Render Pipeline/Lit`, and restart. |
| Items show `LOC: NAME_BP_...` | Harmless naming hiccup after changing language; restart PEAK. |
| Keys 5–9 do nothing | Keep `RawNumberKeys = true` (default). If PEAK's own key bindings use those keys for something else, rebind them in PEAK. |
| Mouse wheel skips two slots | Set `MouseWheelSwitchesSlots = false` (PEAK's own next/previous item binding still works). |
| Hotbar is too big/small or covers something | `HotbarScale` (1–6, 0 = automatic) and `HotbarOffsetY` in the .cfg. |
| "Inventory from the host has N slots" | Everyone must use the same `HotbarSlots` value. |
| Red warning in the Airport about a friend | They need BlockPeak too, and the same version. Send them the same download. |
| I want to play in a normal (non-modded) lobby | setup → **Mods OFF**. **Mods ON** to come back. |
| Too many / too few mobs | `Density`, `MaxPerPlayer`, `Creepers` in the .cfg (host decides). `Enabled = false` turns mobs off. |
| Too much / too little Minecraft loot | `MinecraftShare` and `MinecraftChestChance` in the .cfg (host decides). |
| F6 or / does nothing | Debug mode is read when PEAK starts: setup → **Debug mode ON**, then restart PEAK. In multiplayer the host needs it too. |
| My balance.json changes are gone after updating | Version 0.2.0 has new defaults, so your old file was saved as `balance.v1.backup.json` next to it. Copy your changes across. |
| The BlockPeak check boxes are not in the Custom run window | They may show as a plain list on the left of the screen instead. They only work in a **Custom run**, and only the host's count. |
| Low FPS in Zombie Chase | Lower `modes.zombieChase.count` or `drawDistance` in balance.json (host). |
| Something else is weird after a PEAK update | Set `SafeMode = true` in the .cfg to switch BlockPeak off without uninstalling, and check for a BlockPeak update. |

## Mod managers

The setup installs BepInEx directly into the PEAK folder, which is what Steam launches. Mod managers such as Gale
or r2modman start PEAK with their own profile folder instead; if you use one, either launch from Steam, or copy
`dist\BlockPeak` into the manager profile's `BepInEx\plugins` folder and copy the `mc-assets` folder from
`PEAK\BepInEx\config\BlockPeak\` into the profile's `BepInEx\config\BlockPeak\`.

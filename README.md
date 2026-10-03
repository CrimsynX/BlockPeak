# BlockPeak

**Minecraft's hotbar, items and night mobs inside PEAK** — co-op, for you and your friends.

- A real **9-slot Minecraft hotbar** replaces PEAK's 3 pockets (your PEAK backpack stays, shown in the off-hand slot).
- **Minecraft items turn up in PEAK's luggage**: blocks, torches, ladders, TNT, ender pearls, an elytra at 12% durability (no fireworks), goat horns, totems of undying, golden apples, cookies, steak and more. Now and then a whole suitcase is a "Minecraft chest".
- **Build**: place blocks to make ledges and bridges, stand and climb on them, light TNT, put up torches and ladders.
- **Night mobs** that fit each PEAK biome — drowned on the shore, zombies, spiders and (rare) creepers in the tropics, strays in the snow, husks in the mesa, magma cubes near the volcano, zombies and skeletons in the gloomy swamp. They are rare and basic, and they burn, wander off or vanish at dawn.
- You stay a **PEAK scout** — no Minecraft arms or Steve. The scout's hands hold Minecraft items in the right place.
- Works in **multiplayer**: the host's settings are used for everyone.

> **Status: 0.1.0, first build.** It compiles against PEAK 2.6.a (Unity 6000.3, BepInEx 5) but has not been
> play-tested yet. Every feature is wrapped so that if one part breaks after a PEAK update, the rest keeps working
> and the problem is written to the log. See [Troubleshooting](docs/TROUBLESHOOTING.md).

---

## Install (for you and every friend)

You need: **PEAK** on Steam, and **Minecraft 26.3** in the **Modrinth App** (any 26.3 profile, e.g. Fabulously
Optimized) that you have **started at least once**. BlockPeak copies a few textures and sounds from *your own*
Minecraft — nothing from Minecraft is in this repository.

1. On this GitHub page press **Code → Download ZIP** and unzip it anywhere (or `git clone` it).
2. Double-click **`Setup.bat`**. (If Windows says "Windows protected your PC", click *More info → Run anyway*.)
3. Press **Install / Update**, wait for "All done!", then press **Play**.

That's it. The setup:

- finds PEAK in your Steam libraries,
- installs **BepInEx** for PEAK (BepInExPack_PEAK from Thunderstore, with BepInEx from GitHub as a fallback),
- installs the **BlockPeak** plugin,
- copies the Minecraft textures/sounds BlockPeak needs from your Modrinth App (or the official launcher, Prism or
  CurseForge — it looks everywhere and prefers 26.3).

Other buttons: **Mods OFF (vanilla)** lets you join normal lobbies, **Mods ON** switches back, **Uninstall** removes
BlockPeak (and BepInEx too, if this setup installed it), **Open game log** helps with bug reports.

To update: download the new version and press **Install / Update** again (your settings are kept).

### Playing together

- **Everyone** in the lobby needs BlockPeak installed, the **same version**, and the same hotbar size.
  The Airport shows a red warning if someone is missing it or has a different version.
- The **host's** loot, mob and building numbers are used for everybody.
- Launch PEAK from **Steam** (or the setup's Play button). If you use a mod manager like Gale, its profiles are
  separate — install into the game folder with this setup instead.

---

## Controls

| Key | What it does |
|---|---|
| **1 – 9** | Pick a hotbar slot (press the same number again for empty hands, like PEAK) |
| **Mouse wheel** | Cycle through the hotbar, then the backpack. Hold **Alt** to send the wheel to the item instead (rope etc.) |
| **0** | Backpack (shown in the off-hand slot). PEAK's own backpack button also works |
| **Use** (left mouse) | Place a block / eat / throw a pearl or wind charge / toot the horn / pour water / swing the sword / punch a mob with empty hands |
| **Hold Use** | Elytra: glide while falling (look down to dive, up to slow). Boat: ride |
| **Hold Interact (E)** on a placed block | Break it (it drops as an item). On TNT: light the fuse |
| **F** while climbing | Quick-place a block from your hotbar into the wall under your feet (a foothold) |

Dropping and throwing items uses PEAK's normal keys.

## Test mode (try everything without a run)

In the setup press **Test mode ON**, start PEAK and press **F6** (in the Airport or on the mountain). A menu opens:

- click any Minecraft item to get a full stack (it drops in front of you if your hotbar is full),
- spawn any mob about 6 m in front of you,
- "Mobs act like it's night", remove all mobs, remove all blocks.

Blocks, TNT, torches and ladders can be placed right in the Airport. In multiplayer the **host** needs test mode on.
Press **Test mode OFF** before normal play. (Same switch: `TestMode` under `[Testing]` in the .cfg.)

## Items

| Item | Found | Stack | What it does |
|---|---|---|---|
| Blocks (sand, oak planks, moss, packed ice, terracotta, basalt, deepslate, stone bricks) | Everywhere, the block matches the biome | up to 12 found, stacks to 24 | Build ledges, bridges and walls. Climbable. 200 per run |
| Torch | Common, more in the snow | 2–6 | Light, keeps mobs away (12 m), slowly warms you when you stand right next to it |
| Redstone torch | Uncommon | 2–4 | Dim red light; lights TNT placed next to it |
| Ladder | Common | 3–8 | Place on a wall; climbing it costs half the stamina |
| TNT | Uncommon, more in the mesa | 1–3 | Place, then hold Interact to light. 4 s fuse, breaks blocks, hurts and knocks back |
| Ender pearl | Rare | 1 | Throw it, you teleport where it lands (a little injury) |
| Elytra | Very rare, max 1 per run | 1 | 12% durability ≈ 52 seconds of gliding. No fireworks. Crashing into walls hurts |
| Goat horn | Uncommon | 1 | Loud call everyone hears, a marker over your head for 10 s, scares mobs away. 7 s cooldown |
| Totem of undying | Very rare | 1 | Keep it anywhere in your hotbar: the first time you would pass out, it saves you |
| Golden apple | Uncommon | 1 | Food + heals + bonus stamina |
| Enchanted golden apple | Very rare, max 1 per run | 1 | Big heal, big bonus stamina, 3 min heat immunity and half damage |
| Cookie | Common | 2–5 | Small snack, quick to eat |
| Steak | Common | 1–2 | Big meal |
| Rotten flesh | Dropped by zombies | 1 | Food, but usually poisons you |
| Oak boat | Rare, more near water and snow | 1 | Hold Use to ride: fast on water, faster on snow and ice |
| Water bucket | Uncommon, more in the mesa, never in the caldera | 1 | Pour just before you land to cancel fall damage, or cool off when it's hot |
| Wind charge | Uncommon | 1–3 | Throw it at your feet to launch yourself, or at mobs to push them |
| Stone sword | Uncommon | 1 | Fight mobs |

## Night mobs

They only spawn at night, never near torches or campfires, at most 2 per scout.

| Biome | Mobs |
|---|---|
| Shore | Drowned |
| Tropics | Spider, zombie, creeper (rare) |
| Roots | Bogged (poison arrows), slime |
| Alpine | Stray (freezing arrows) |
| Mesa | Husk (makes you hungry) |
| Caldera / Kiln | Magma cube (hot) |
| Gloom (swamp) | Zombie, skeleton |

Hits use PEAK's own status bar (injury, poison, cold, heat, hunger). Zombies and skeletons burn at dawn, spiders
calm down, slimes and magma cubes vanish. Fight back with the stone sword, your fists, TNT or wind charges.

## Settings

Two files in `PEAK\BepInEx\config\` (open them with the setup's **Open settings folder** button):

- `com.blockpeak.mod.cfg` — simple options: hotbar size (3–9), hotbar scale, keys, mobs on/off, creepers
  (Off/Rare/Normal), mob density, how much loot is Minecraft, block limit, Minecraft folder, sound volume.
- `BlockPeak\balance.json` — every number (spawn weights per biome, stack sizes, food values, TNT radius, mob
  health and damage...). See [docs/BALANCE.md](docs/BALANCE.md). In multiplayer the host's file counts.

## Building from source

The repo already contains a built `dist\BlockPeak\BlockPeak.dll`; you only need this to change the code.
Install the [.NET SDK 8+](https://dotnet.microsoft.com/download), run `Setup.bat` once (so BepInEx is in PEAK),
then:

```powershell
powershell -ExecutionPolicy Bypass -File tools\build.ps1 -Install
```

It copies PEAK's DLLs into `lib\` (git-ignored, never committed), makes "publicized" copies so the mod can reach
PEAK's internals, builds, updates `dist\`, and (`-Install`) copies the result into PEAK. How the mod works inside:
[docs/HOW-IT-WORKS.md](docs/HOW-IT-WORKS.md).

## Repository layout

```
Setup.bat                 double-click installer (runs setup\BlockPeakSetup.ps1)
setup\                    the installer (PowerShell, no extra downloads needed besides BepInEx)
dist\BlockPeak\           the built plugin that Setup installs
config\                   default balance.json and the list of Minecraft files to copy
src\BlockPeak\            the mod's C# source (BepInEx 5 plugin + Harmony patches)
tools\                    build.ps1, package.ps1, Publicizer
docs\                     how it works, balance reference, troubleshooting
```

## Legal

NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.
Not affiliated with PEAK's developers (Aggro Crab and Landfall).

This repository contains no Minecraft or PEAK files. Minecraft textures and sounds are copied on each player's own
computer from that player's own Minecraft installation, and are only used there. BlockPeak's own code is MIT
licensed (see [LICENSE](LICENSE)).

# BlockPeak

**Minecraft's hotbar, items and night mobs inside PEAK** — co-op, for you and your friends.

- A real **9-slot Minecraft hotbar** replaces PEAK's 3 pockets (your PEAK backpack stays, shown in the off-hand slot).
- **Minecraft items turn up in PEAK's luggage**: blocks, torches, TNT, ender pearls, a bow with 16 arrows, an elytra (with one firework boost), goat horns, totems of undying, golden apples, potions of swiftness and leaping, cookies, steak, a boat and more. Now and then a whole suitcase is a "Minecraft chest".
- **Build**: an outline shows where the block will go. Place blocks to make ledges and bridges, stand and climb on them, light TNT and put up torches.
- **Night mobs** that fit each PEAK biome — drowned on the shore, zombies, spiders and (rare) creepers in the tropics, strays in the snow, husks in the mesa, magma cubes near the volcano, zombies and skeletons in the gloomy swamp. They use PEAK's own zombie body, so they climb and don't walk through walls, and show Minecraft hearts for a moment when you hit them.
- **Custom-run modes**: Warden Chase, Zombie Chase (100+ zombies), Only Minecraft items, Minecraft chests (normal, large and copper), Anvil rain, TNT rain and a Starter kit — check boxes in PEAK's *Custom run* window.
- **Optional Minecraft looks**: Minecraft particles and Minecraft clouds (switches in the setup).
- You stay a **PEAK scout** — no Minecraft arms or Steve. The scout's hands hold Minecraft items in the right place.
- Works in **multiplayer**: the host's settings are used for everyone.

> **Status: 0.3.0.** It compiles against PEAK 2.6.a (Unity 6000.3, BepInEx 5) but has not been
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

Other buttons: **Uninstall** removes BlockPeak (and BepInEx too, if this setup installed it), **Open game log** helps
with bug reports. Below them are on/off switches:

- **Mods** — off lets you join normal (non-modded) lobbies; on switches BlockPeak back on.
- **Debug mode** — the F6 test menu and the / command chat. Set it *before* starting PEAK.
- **Minecraft particles and effects** — PEAK's smoke, fire, sparks, splashes and dust use Minecraft's particle textures.
- **Minecraft clouds** — PEAK's clouds are replaced with Minecraft's blocky clouds.

The two looks switches take effect the next time PEAK starts. They are personal: each player picks their own.

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
| **Use** (left mouse) | Place a block / eat or drink / throw a pearl or wind charge / toot the horn / swing the sword / place the boat / punch a mob with empty hands |
| **Hold Use, let go** with the bow | Draw (1 second for full power) and shoot |
| **Elytra slot selected** | You wear the elytra. **Jump in mid-air** to open the wings and glide (look down to dive, up to slow) |
| **R** (or Use) while gliding | The elytra's one firework boost |
| **Hold E** on a placed boat | Get in. **WASD** steers, **Jump** or **Crouch** gets out. **Crouch + hold E** picks it up again |
| **Hold Interact (E)** on a placed block | Break it (it drops as an item). On TNT: light the fuse |
| **Hold Interact (E)** on a Minecraft chest | Open it (its items pop out) |
| **F** while climbing | Quick-place a block from your hotbar into the wall under your feet (a foothold) |

Dropping and throwing items uses PEAK's normal keys.

## Debug mode (try everything without a run)

In the setup press **Debug mode ON** *before* you start PEAK (it is read once when PEAK starts; restart PEAK after
switching it). Then, in the Airport or on the mountain:

- **F6** opens the test menu: click any Minecraft item to get a full stack, spawn any mob 6 m in front of you,
  "Mobs act like it's night", remove all mobs, remove all blocks.
- **/** opens a Minecraft-style command chat (only you see it). **Enter** runs the command, **Esc** closes,
  **Up/Down** goes through earlier commands, **Tab** completes.

| Command | What it does |
|---|---|
| `/help` | Lists the commands |
| `/time set <day\|night\|noon\|midnight\|ticks>` | Changes the time of day for everyone |
| `/gamemode <creative\|survival>` | Creative: no damage, endless stamina, double-tap Jump to fly |
| `/summon <mob> [count]` | Spawns mobs near you (zombie, husk, drowned, skeleton, stray, bogged, spider, creeper, slime, magma_cube, warden) |
| `/kill @e` | Removes every mob |
| `/heal` | Heals you fully |

In multiplayer the **host** needs debug mode on too. Press **Debug mode OFF** before normal play.
(Same switch: `TestMode` under `[Testing]` in the .cfg.)

## Custom-run modes

Start a **Custom run** in PEAK (the host does this). Under PEAK's own options you'll find BlockPeak's check boxes.
Only the host's boxes count, and they only work in custom runs.

| Mode | What happens |
|---|---|
| **Warden Chase** | 10 seconds after the first scout walks away from the start, the warden climbs out of the ground. Like in Minecraft it is **blind**: it hears footsteps, jumps, landings, blocks and explosions within about 16 m, and smells scouts that come within about 6 m sideways (height doesn't matter). Each sound or sniff makes it angrier; once angry it roars and hunts that scout as fast as a scout can run and climb. **Crouching makes no footstep sounds.** It always knows roughly which way the scouts went and slowly follows. |
| **Zombie Chase** | Same countdown, then a horde of 120 zombies that always know where you are. Built to be light on FPS. |
| **Only Minecraft items** | Every item you find is a Minecraft item. The start area (BingBong, passport...), respawn chests and gems stay as they are. |
| **Minecraft chests (rare / normal / common)** | Minecraft items are only found in Minecraft chests placed next to PEAK's luggage all over the mountain — pick how many. Chests and large chests hold everyday items (blocks, torches, food, TNT, potions, bow...). **Copper chests** are the rare ones with the powerful items (elytra, totem, enchanted golden apple, ender pearls). Hold E to open. |
| **Anvil rain** | Every few minutes (at random) it rains anvils around each scout for 15–30 seconds. Real physics; a hit hurts and knocks you over. They disappear after a while. |
| **TNT rain** | Same, with lit TNT (never at the same time as anvil rain) that explodes when it lands (or a moment later). Small, cheap explosion effects. |
| **Starter kit** | Every scout starts with 16 blocks, 4 torches, 4 cookies and an ender pearl. |

Warden Chase and Zombie Chase can't be on at the same time, and neither can the chests and "Only Minecraft items".
The warden can also be summoned with `/summon warden` in debug mode; it behaves the same way (blind). Night mobs don't spawn during the chase modes.

## Items

| Item | Found | Stack | What it does |
|---|---|---|---|
| Blocks (sand, oak planks, moss, packed ice, terracotta, basalt, deepslate, stone bricks) | Everywhere, the block matches the biome | up to 12 found, stacks to 24 | Build ledges, bridges and walls. Climbable. 1000 per run. Moss and planks are light, stone bricks, basalt and deepslate are heavier |
| Torch | Common, more in the snow | 2–6 | Light, keeps mobs away (12 m), slowly warms you when you stand right next to it |
| TNT | Uncommon, more in the mesa | 1–3 | Place, then hold Interact to light. 4 s fuse, breaks blocks, hurts and knocks you back hard |
| Ender pearl | Rare | 1 | Throw it, you teleport where it lands (a little injury) |
| Elytra | Very rare, max 1 per run | 1 | Worn while its slot is selected (wings on your back, a chest-slot icon next to the hotbar). Found at full durability, which lasts about 45 seconds of gliding, then it breaks. One firework boost. Crashing into walls hurts |
| Bow | Uncommon | 1 | Comes with 16 arrows (the count shows on the slot). Anyone you hit — scout or mob — is knocked over and lets go of the wall |
| Goat horn | Uncommon | 1 | Loud call everyone hears, a marker over your head for 10 s, scares mobs away. 7 s cooldown |
| Totem of undying | Very rare | 1 | Keep it anywhere in your hotbar: the first time you would pass out, it saves you |
| Golden apple | Uncommon | 1 | Heals and gives some extra stamina |
| Enchanted golden apple | Very rare, max 1 per run | 1 | Heals everything (like a med kit), lots of extra stamina, and a longer version of PEAK's milk invincibility |
| Potion of swiftness | Uncommon, more in the mesa | 1 | 25 s of faster running and climbing |
| Potion of leaping | Uncommon, more in the snow | 1 | 25 s of higher jumps |

Active potions show Minecraft's effect icons with the time left in the top-right corner.
| Cookie | Common | 2–5 | Small snack, quick to eat |
| Steak | Common | 1–2 | Big meal |
| Rotten flesh | Dropped by zombies | 1 | Food, but usually poisons you |
| Oak boat | Rare, more near water and snow | 1 | Place it, then hold E to get in: fast on water, snow and ice, slow on land. No fall damage while you sit in it |
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

- `com.blockpeak.mod.cfg` — simple options: hotbar size (3–9), hotbar scale, keys (quick-place, firework), mobs on/off, creepers
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

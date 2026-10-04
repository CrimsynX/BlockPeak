# Changelog

## 0.3.0

New
- **Minecraft chests** custom-run option (rare / normal / common): Minecraft items only come from chests placed
  next to PEAK's luggage. Chest, large chest and copper chest; items have rarity tiers and copper chests hold the
  powerful ones.
- **Bow** with 16 arrows: hold Use to draw, let go to shoot. A hit knocks any scout or mob over (scouts let go of
  the wall).
- Setup: on/off **pill switches** for Mods, Debug mode, **Minecraft particles and effects** and **Minecraft clouds**.
- Potion effect icons (Minecraft's) with time left, top right.

Changed
- **Warden** works like Minecraft's: blind, hears vibrations (crouch to sneak), smells nearby scouts (sideways
  distance only), gets angrier, roars, then hunts as fast as a scout runs and climbs. Arms hang and swing instead of a
  T-pose. Head tendrils, glowing heart and spots that pulse faster when it's angry.
- Elytra is found at full durability and lasts about 45 s of gliding; both durability bars match.
- Sword is held in the right hand only, blade angled like Minecraft.
- Boat rebuilt from Minecraft 26.x's boat model, with paddles that row while you steer.
- Anvil and TNT rain: longer (15–30 s), random timing and strength, never both at once.
- Block limit raised from 200 to 1000 (new setting `MaxPlacedBlocks`).

Fixed
- Black parts on mobs, the boat and other models (transparent texture pixels are now see-through; zombies and husks
  use the right arm/leg texture).
- Potion of leaping now really makes you jump higher.

Removed
- Ladder.

Not yet play-tested.

## 0.2.0

New
- Custom-run check boxes: **Warden Chase**, **Zombie Chase** (120-zombie horde), **Only Minecraft items**,
  **Anvil rain**, **TNT rain**, **Starter kit**.
- The warden (only in Warden Chase or via /summon): smells you anywhere, climbs, never outruns a running scout.
- Potions of swiftness and leaping (25 s).
- Debug chat (Minecraft style, private): /help, /time set, /gamemode creative|survival, /summon, /kill @e, /heal.
  Only when debug mode was switched on in the setup before starting PEAK.
- Block placement outline. Boats are placed and ridden (hold E), no fall damage while riding.
- Elytra rework: worn on your back while its slot is selected, second HUD slot with durability, Minecraft gliding,
  one firework boost (R), breaks when durability runs out.

Changed
- Night mobs now use PEAK's zombie body: they climb, don't walk through walls, and animate with PEAK's rig. More
  health. Minecraft hearts appear for a few seconds when they are hit.
- Ladders climb like PEAK's ropes. The sword is held properly.
- Golden apples use PEAK's effects (healing, extra stamina; the enchanted one also gives milk invincibility).
- Blocks have different weights (moss and planks light, stone bricks/basalt/deepslate heavier).
- TNT: cheap Minecraft explosion effect instead of PEAK's (no FPS spike), bigger knockback.
- Fixed the PEAK overlay on Minecraft block textures.
- Setup: "Test mode" buttons are now "Debug mode".
- An old balance.json is backed up and replaced so the new numbers take effect.

Removed
- Water bucket and redstone torch.

Not yet play-tested.

## 0.1.1

- Test mode: F6 menu to get any Minecraft item, spawn mobs, force night, clear mobs/blocks. Setup buttons to switch it on/off.

## 0.1.0 (first build)

- 9-slot Minecraft hotbar (3–9 configurable) with Minecraft's own HUD sprites and font; backpack in the off-hand slot.
- 25 Minecraft items in PEAK luggage, biome-aware, with per-run caps and "Minecraft chests".
- Building: blocks, torches, redstone torches, ladders, TNT; host-owned, synced, climbable; quick-place while climbing.
- Night mobs per biome: zombie, husk, drowned, skeleton, stray, bogged, spider, creeper, slime, magma cube.
- One-click setup: finds PEAK, installs BepInEx and BlockPeak, copies Minecraft textures/sounds from the player's own install.
- Not yet play-tested.

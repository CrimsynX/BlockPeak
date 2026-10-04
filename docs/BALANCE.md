# balance.json reference

`PEAK\BepInEx\config\BlockPeak\balance.json` is created on first start from `config/balance.json` in this repo.
Edit, save, restart PEAK. Missing keys fall back to the defaults, so it is safe to delete lines. When a new
BlockPeak version changes the defaults (the `version` number at the top), your old file is saved as
`balance.v<old number>.backup.json` and the new default is written. In multiplayer the
**host's** file is used by everyone (the host shares it through the lobby).

Units are PEAK's: status amounts go from 0 to 1 where 1.0 is the whole stamina bar and 0.025 is one tick.
Weight is in PEAK weight units (1 unit = one 2.5% tick on the bar).

The simple options in `com.blockpeak.mod.cfg` (MinecraftShare, MinecraftChestChance, PlacedBlockLimit, mob
Enabled/Density/MaxPerPlayer/Creepers) override the matching numbers here.

## loot

| Key | Meaning |
|---|---|
| `minecraftShare` | Chance for each item in a normal suitcase to be a Minecraft item instead |
| `ancientShare` | Same for ancient luggage, which only rolls the rare items (and golden apples) |
| `chestChance` | Chance that a suitcase is a "Minecraft chest" holding only Minecraft items |
| `chestItemsMin/Max` | How many items a Minecraft chest holds |
| `perRunCaps` | At most this many of an item per run (elytra 1, enchanted golden apple 1, ...) |
| `blockForPool` | Which block type each luggage pool gives (sand on the beach, packed ice in the tundra...) |
| `biomePools` | Luggage pools that can contain Minecraft items |
| `rarePoolsOnly` | Pools that only get the rare items |
| `weights` | Relative chance of each item (`blocks` = the biome's block) |
| `multipliers` | Per pool weight multipliers (e.g. more torches and boats in the tundra, no water in the caldera) |

## items

Every item has `find: [min, max]` (how many you find in one stack) and `stack` (maximum per hotbar slot).
Weight: `weightPer: N` = one weight unit per N items (rounded up), `weightEach: N` = N units per item.

| Item | Extra keys |
|---|---|
| blocks | (shared by all block types) |
| blocks | `blockWeights`: blocks per weight unit for each block type (higher = lighter) |
| tnt | `fuseSeconds`, `radius`, `injury` (at the centre), `knockback` |
| ender_pearl | `injury`, `throwBoost` |
| bow | `arrows`, `drawSeconds`, `arrowSpeed`, `damage` (mob health points at full draw), `playerInjury`, `knockDownSeconds`, `knockback`, `mobKnockDownSeconds` |
| elytra | `startDurability` (1 = full), `glideSeconds` (flight time from full durability), `crashInjuryMin/Max`, `fireworks` (boosts per elytra), `fireworkSeconds` |
| boat | `waterSpeed`, `snowSpeed`, `landSpeed` |
| potion_swiftness | `seconds`, `moveSpeed`, `climbSpeed` (0.35 = 35% faster) |
| potion_leaping | `seconds`, `jumpExtraVelocity` (extra upward speed per jump, m/s) |
| wind_charge | `launch` (speed you get thrown at), `radius` |
| goat_horn | `cooldown`, `markerSeconds`, `scareSeconds` |
| totem_of_undying | `invincibleSeconds`, `heatImmuneSeconds` |
| foods | `hunger` (how much hunger it removes), `heal` (healing like PEAK's med kit), `bonusStamina`, `eatSeconds`, `milkInvincibilityMultiplier` (enchanted golden apple: PEAK milk's invincibility, this many times as long), `poisonChance`, `poison` |
| any item | `hold: { rotation: [x,y,z], offset: [x,y,z], scale: n }` changes how the scout holds it |
| stone_sword | `damage` (mob health points), `reach` |

## building

`reach`, `blockSize` (1.0 = one PEAK unit), `placedBlockLimit`, `breakSeconds`, `quickPlaceStamina`,
`quickPlaceCooldown`, `ladderStaminaMultiplier`, `torchLightRange`, `torchWarmRadius`, `torchColdRemovedPerSecond`.

## mobs

| Key | Meaning |
|---|---|
| `enabled` | Night mobs on/off |
| `spawnIntervalSeconds`, `spawnChance` | Every interval each scout has this chance to get a mob nearby |
| `maxPerPlayer` | Mobs alive at once per scout |
| `minDistance/maxDistance` | Spawn ring around a scout |
| `noSpawnNearLightRadius` | No spawns this close to torches or campfires |
| `noSpawnVerticalFromClimber` | Spawns must be within this height of the scout |
| `despawnDistance/despawnAfterSeconds` | Mobs far from everyone disappear |
| `punchDamage` | Damage of an empty-handed punch |
| `creepers` | Off / Rare / Normal |
| `scale` | Mob size (0.9 = slightly smaller than in Minecraft so they fit PEAK's scouts) |
| `biomes` | Which mobs each PEAK biome uses (Shore, Tropics, Roots, Alpine, Mesa, Volcano, Swamp) |
| `heartsSeconds` | How long the hearts stay over a mob after it is hurt |
| `types` | Per mob: `health`, `walkSpeed` (1 = a walking scout), `sprints`, `status` + `amount` (what a hit does), optional `extraStatus` + `extraAmount`, `ranged`, `climbs`, `explodes`, `rare`, `knockback`, `burnsAtDawn`, `neutralAtDawn`, `vanishAtDawn`. `warden`, `warden_sonic` and `horde_zombie` are used by the chase modes |

## modes

| Key | Meaning |
|---|---|
| `debugIgnoresCustomRun` | With debug mode on, let the check boxes work in normal runs too (for testing) |
| `countdownSeconds`, `moveToStart` | Chase countdown, and how far the first scout must walk to start it |
| `zombieChase` | `count`, `zombieHealth`, `zombieSpeed`, `spawnMinDistance/MaxDistance`, `drawDistance` |
| `starterKit` | Item key → how many (`blocks` = the beach block) |
| `minecraftItemsOnly` | `startAreaRadius`, `keep` (PEAK items whose names contain these words are never swapped) |
| `chests` | `frequency` (chance per luggage for rare/normal/common), `kinds` (single/double/copper weights), `items` (how many per chest), `tiers` (which items are common/uncommon/rare), `tierWeights` (which tiers each chest kind rolls) |
| `rain` | `everyMin/Max` (seconds between rains), `secondsMin/Max` (rain length), `gapBetweenRains`, `radius`, `height`, `maxAlive`, `anvilsPerSecond`, `anvilInjury`, `anvilLifeSeconds`, `tntPerSecond`, `tntFuseMin/Max`, `tntRadius`, `tntInjury`, `tntKnockback` (all per scout) |

### mobs.warden

`hearRange` (m), `smellRange` (m, sideways), `sniffSeconds`, `angerPerVibration`, `angerPerSniff`, `angryAt`
(anger needed to hunt, Minecraft uses 80), `angerDecayPerSecond`, `digWhenFartherThan` (Warden Chase: digs over to
the scouts when they are this far away for 20 s).

## visuals

Minecraft clouds: `cloudCell` (size of one cloud pixel, m), `cloudThickness`, `cloudSpeed`, `cloudHeightAboveStart`
(used when PEAK's own clouds can't be found; `MinecraftCloudHeight` in the .cfg overrides it).

## templates (advanced)

Minecraft items are built from a plain PEAK item so the scout's hands line up. Leave empty for automatic, or put
item names from `peak-items.txt` (written next to balance.json on start) into `generic` / `food`.

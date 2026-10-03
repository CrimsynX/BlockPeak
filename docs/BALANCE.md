# balance.json reference

`PEAK\BepInEx\config\BlockPeak\balance.json` is created on first start from `config/balance.json` in this repo.
Edit, save, restart PEAK. Missing keys fall back to the defaults, so it is safe to delete lines. In multiplayer the
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
| tnt | `fuseSeconds`, `radius`, `injury` (at the centre) |
| ender_pearl | `injury`, `throwBoost` |
| elytra | `startDurability` (0.12 = 12%), `glideSeconds` (flight time at that durability), `crashInjuryMin/Max` |
| boat | `waterSpeed`, `snowSpeed` |
| water_bucket | `fallSaveWindow` (seconds the fall-damage cancel lasts) |
| wind_charge | `launch` (speed you get thrown at), `radius` |
| goat_horn | `cooldown`, `markerSeconds`, `scareSeconds` |
| totem_of_undying | `invincibleSeconds`, `heatImmuneSeconds` |
| foods | `hunger` (how much hunger it removes), `heal` (injury removed), `bonusStamina`, `eatSeconds`, `heatImmuneSeconds`, `halfInjurySeconds`, `poisonChance`, `poison` |
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
| `types` | Per mob: `health`, `speed`, `status` + `amount` (what a hit does), optional `extraStatus` + `extraAmount`, `ranged`, `climbs`, `explodes`, `rare`, `knockback`, `burnsAtDawn`, `neutralAtDawn`, `vanishAtDawn` |

## templates (advanced)

Minecraft items are built from a plain PEAK item so the scout's hands line up. Leave empty for automatic, or put
item names from `peak-items.txt` (written next to balance.json on start) into `generic` / `food`.

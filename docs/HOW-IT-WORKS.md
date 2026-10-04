# How BlockPeak works

A BepInEx 5 plugin (`src/BlockPeak`, C# netstandard2.1) with Harmony patches. It is built against "publicized"
copies of PEAK's assemblies (everything made public) so it can use PEAK's internals; Mono does not check access at
runtime. No PEAK, BepInEx or Minecraft files are in the repo.

```
Plugin.cs            entry point: config, balance, Harmony (each patch class applied separately)
Core/                Cfg (the .cfg), Balance (balance.json + host sync), Game (helpers), Health (fail-safe
                     error reporting), Runner (per-frame driver), Diagnostics (peak-items.txt)
Assets/              AssetList (config/minecraft-assets.txt), Extractor (copies files from the player's Minecraft),
                     McAssets (textures/sounds), McFont (Minecraft bitmap font), Meshes (item sprites, cubes,
                     entity boxes, block icons), Mat (materials), Sfx
Net/                 Channel (Photon RaiseEvent code 173), Handshake (version check via player properties)
Hotbar/              HotbarPatches (extra slots, switching, stacking, weight), HotbarHud (Minecraft HUD)
Items/               ItemDefs, ItemRegistry (builds the items), McItem, Behaviours (what each item does),
                     Effects (totem, horn, jump boost), PeakEffects (PEAK afflictions), Elytra, Loot,
                     Projectiles, Stacks
Building/            BlockWorld (host-owned placed blocks), PlacedBlock (looks, breaking, TNT), Explosions,
                     PlacementPreview (outline), Boats
Mobs/                BodyMobs + BodyMob + WardenBrain (night mobs and the warden on PEAK zombie bodies), McMobs (the
                     lightweight zombie horde), MobModels (Minecraft box models), MobDirector (routes to both)
Modes/               CustomOptions (check boxes in the Custom run window), GameModes (chases, starter kit),
                     Rains (anvil/TNT rain), Chests (Minecraft chests)
UI/                  Banner, Fx (particles, Minecraft explosion puffs), ChatBox + Commands (debug chat),
                     TestMenu (F6), McVisuals (Minecraft particles and clouds)
```

## Hotbar

PEAK's `Player.itemSlots` has 3 entries with slot ids 0–2; id 3 is the backpack and 250 the "hands full" slot.
BlockPeak grows the array to 3 + N in `Player.Awake` and gives the new slots ids **4–9**, so no id clashes with
PEAK's. `Player.GetItemSlot` maps 4–9 to the new entries. Everything that loops over `itemSlots` (pickup, weight,
inventory sync, reconnect data) picks the extra slots up automatically. `CharacterItems.DoSwitching` is replaced
(only when the hotbar has more than 3 slots) with Minecraft-style switching. `DropAllItems` also drops the new
slots. PEAK's own slot UI keeps running but invisible; `HotbarHud` draws Minecraft's sprites on top.

Every player must use the same slot count, because `SyncInventoryRPC` sends a fixed-size array. A guard handles a
mismatch without errors and the Airport banner warns about it.

## Items

Each Minecraft item is a clone of a plain PEAK item (chosen automatically, see `peak-items.txt`): PEAK's scripts,
models and colliders are stripped, a Minecraft model is added where the original model sat (so PEAK's `Hand_L` /
`Hand_R` grip points still fit), and BlockPeak's behaviour components are attached. The clone is kept inactive,
registered in `ItemDatabase.itemLookup` under a fixed id (47000 + index) and in Photon's `DefaultPool` as
`0_Items/BlockPeak_<key>`, so PEAK spawns, picks up, holds, drops, throws, backpacks and syncs it like any of its
own items. Foods are cloned from a PEAK food so the eating animation and sounds stay.

Stack size is PEAK's own "uses" counter (`DataEntryKey.ItemUses`), so it travels with the item everywhere.
Picking up a Minecraft item tops up an existing stack first (`Player.AddItem` prefix, host side).

## Loot

`Spawner.GetObjectsToSpawn` (host only) swaps a share of each luggage's items for Minecraft items rolled from the
weights in balance.json, with per-run caps. The stack size is rolled when the item first appears on the host.
With the "Only Minecraft items" custom-run option every spawner's items are swapped, except respawn chests,
spawners within 40 m of the start (`SpawnPoint.allSpawnPoints`) and items whose names are on the keep list.

## Blocks

Blocks live on a world grid. A player asks the host to place/break (`Op.BlockPlaceReq`), the host checks the limit
and tells everyone (`Op.BlockPlaced`). A player who joins late gets a snapshot. Blocks use PEAK's `Map` layer so
scouts stand on and climb them. Breaking uses PEAK's hold-to-interact system (`IInteractibleConstant`). Explosions use
Minecraft's own explosion sprites (a handful of billboards, no PEAK effect prefab) and a strong knockback; damage
and knockback are applied by each player's own game. `PlacementPreview` draws a wireframe outline of where the
held block would go (red when it can't).

## Mobs

Night mobs and the warden are PEAK's own mushroom-zombie body (`MushroomZombie`, a full bot `Character`),
spawned by the host with `PhotonNetwork.InstantiateRoomObject` and tagged through `InstantiationData`. That gives
them PEAK's physics, collisions, climbing and network sync. BlockPeak hides the zombie's renderers, hangs the
Minecraft box model on its bones (so PEAK's walk/run/climb/fall animations move the Minecraft limbs), and replaces
its brain (`MushroomZombie.Update` prefix) with each mob's Minecraft behaviour: chasing, climbing, skeletons keeping
their distance and shooting, creeper fuses, slime hops, dawn rules, the warden's smell and sonic boom.
Minecraft hearts float over a mob for a few seconds after it is hurt.

The Zombie Chase horde is too big for full characters, so it is a separate light system (`McMobs`): one ground
raycast batch per frame (`RaycastCommand`), staggered thinking, a spatial grid, 10 bytes per zombie 8 times a
second, and GPU-instanced drawing with a few pre-built pose meshes.

Hits on players go to the victim's game (`Op.HurtPlayer`), which applies PEAK statuses itself (PEAK only lets the
owner change its statuses). Players' hits on mobs go to the host.

## Elytra, boats, debug chat

The elytra is worn while its hotbar slot is selected: an `EquipSlot` prefix keeps it out of the hands, `WingsView`
draws Minecraft's wings on the torso, and gliding sets the ragdoll's velocities with Minecraft's fall-flying maths.
Durability and the one firework are stored in the item's data and synced to the host.

A boat is placed in the world (host-owned list). The rider drives it and streams its position; while riding, the
movement keys steer (`CharacterInput.Sample` postfix) and fall damage is capped to zero.

The debug chat only exists when `TestMode` was on when PEAK started. Commands that change the world (`/time`,
`/summon`, `/kill`) run on the host (`Op.DebugCmd`); the others are local.

## Textures

All Minecraft materials use alpha cut-out, so transparent pixels (skeleton ribs, chest and boat gaps) are not drawn.
Models with rotated parts (boat, warden tendrils) use Minecraft's own part pivots and rotations: Minecraft's model
space maps to Unity's by flipping x, y and z, which leaves rotation matrices unchanged.

## Custom-run modes

`CustomOptionsWindow.Initialize` (postfix) clones one of PEAK's own option rows for each BlockPeak option (an IMGUI
list is the fallback if that fails). Choices are saved in PlayerPrefs. The host's `GameModes` waits for the first
scout to walk 2.5 m from the start, broadcasts a countdown, then spawns the warden or the horde. `Rains` has the
host pick drop points with open sky above the scouts; every game simulates its own copies with Unity physics and
only the host's copies break blocks or hurt mobs.

## Networking summary

All messages go through one Photon event code (173) with a 1-byte op. The host shares balance.json through a room
property (`bp_bal`). Each player publishes `bp_ver` = version | hotbar size | item list hash.

## Minecraft files

`config/minecraft-assets.txt` lists the textures and sounds to copy. Both the setup and the mod read it: textures
come out of the client jar (`assets/minecraft/textures/...`), sounds from the launcher's asset index + objects
folder. They go to `BepInEx/config/BlockPeak/mc-assets/` with a `source.json` stamp. If something is missing, the
mod uses generated stand-ins.

## Patches

| Patch | Why |
|---|---|
| `Player.Awake` (postfix) | add hotbar slots 4–9 |
| `Player.GetItemSlot` (prefix) | resolve ids 4–9 |
| `Player.SyncInventoryRPC` (prefix, mismatch only) | tolerate a different slot count |
| `Player.AddItem` (prefix, Minecraft items only) | stack merging |
| `CharacterItems.DoSwitching` (replaced when > 3 slots) | number keys 1–9, wheel, backpack key |
| `CharacterItems.DropAllItems` (postfix) | drop slots 4–9 too |
| `CharacterAfflictions.UpdateWeight` (postfix) | stacks weigh by count |
| `CharacterAfflictions.AddStatus` (prefix, only while an effect is active) | half damage / heat immunity |
| `Character.HandleLife` (prefix, only when about to pass out) | totem of undying |
| `Spawner.GetObjectsToSpawn` (postfix, host) | Minecraft loot, "Only Minecraft items" |
| `CharacterItems.EquipSlot` (prefix, elytra slot only) | wear the elytra instead of holding it |
| `CharacterInput.Sample` (prefix/postfix) | block input while the chat/menu is open, steer the boat |
| `MushroomZombie.Awake/Start/Update/RPC_PlaySFX/ReadyToDisable/OnBitCharacter` (BlockPeak mobs only) | Minecraft mobs on PEAK zombie bodies |
| `CustomOptionsWindow.Initialize` (postfix) | BlockPeak's custom-run check boxes |

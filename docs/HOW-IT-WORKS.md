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
                     Effects (food/totem), Loot, Projectiles, Stacks
Building/            BlockWorld (host-owned placed blocks), PlacedBlock (looks, breaking, TNT), Explosions
Mobs/                McMobs (spawning, AI, combat, sync), MobModels (Minecraft box models), MobView (animation)
UI/                  Banner (Airport text, toasts), Fx (particles, markers)
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

## Blocks

Blocks live on a world grid. A player asks the host to place/break (`Op.BlockPlaceReq`), the host checks the limit
and tells everyone (`Op.BlockPlaced`). A player who joins late gets a snapshot. Blocks use PEAK's `Map` layer so
scouts stand on and climb them; ladders add PEAK's `ClimbModifierSurface`. Breaking uses PEAK's hold-to-interact
system (`IInteractibleConstant`). TNT reuses PEAK's dynamite explosion effect without its damage; damage and
knockback are applied by each player's own game.

## Mobs

Mobs are not Photon objects. The host runs spawning and AI and sends 10 compact updates per second
(`Op.MobStates`); other players interpolate. Hits on players go to the victim's game (`Op.HurtPlayer`) which
applies PEAK statuses itself (PEAK only lets the owner change its statuses). Players' hits on mobs go to the host
(`Op.MobHitReq`).

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
| `Spawner.GetObjectsToSpawn` (postfix, luggage, host) | Minecraft loot |

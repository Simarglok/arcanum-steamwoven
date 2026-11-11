# Placed trap devices (ARC-WORLD-124)

Status: **Implemented bounded component**, work items ARC-WORLD-124 and ARC-WORLD-125 (slices E6-b and E6-c of the
[technology audit](../research/technology-audit-res-125.md)). ARC-WORLD-124 places the devices whose slot 1 holds a spell: the two timed bombs, item 15154
(record 176) and item 15193 (record 220). ARC-WORLD-125 arms the script trap devices (items 15100 and 15120, slot 1 empty: a script written onto a portal, a container or a tile marker, the refusals of `mes\trap.mes`
lines 2 and 3; [below](#script-trap-devices-arc-world-125)). Requirements:
[FR-ITEM-016](../requirements/items-economy.md#fr-item-016) (ledger [TP1–TP8](../research/ledger/technology.md#trap-devices-arc-technology-010),
[IA13](../research/ledger/items.md#item-aiming-and-use-with-a-target-arc-res-113), awareness research section 7).

## Rules (`Arcanum.Simulation.Traps.TrapDeviceRules`)

- `Plan(slotSpell, onTile)` reads slot 1 (field 0x65), the only slot the device branches read. 10000 is empty (a script trap, `Script`); a number outside the 223 records is `Invalid` (the
  executable would read past its table; no installed device holds one); anything else is `Timed`.
- A timed plan carries the spell, the marker prototype of the **tile form** (4047 for record 176, 4049 for record 220, none for another record and none for the object form), the
  alarm (record 176 only) and the fuse, 88,000 ms of game time (the timer's delay 88 times 1000, 0x4BC690).
- A target of object type 0 (a wall) takes the tile form (IA13); the viewer decides it from the cast host's object facts.

## Session (`GameSessionOwner.PlaceTrapDevice`, event kind `trap.device`)

- One revision does what the executable does in order: the device is destroyed out of the hero's pack with **no check of the target** (any object, a critter included; the cursor's mask only
  allowed an object or a tile), the marker is made on a tile (a scenery object of the [created objects](game-session-runtime.md); the hero's own spotted mark is its being drawn)
  and the fuse is scheduled. Refused before any effect: a stale version, a device the hero does not carry unworn or that is no generic item, a cell or map out of range, the created-object
  or fuse budget (`MaxTrapDevices` 256), the revision bound, a full durability budget.
- The event `trap.device` is the original's Traps event (kind 28), game time, **saved**, scoped to the map of its entity, any number. Entity: the marker, or for an object target the
  destroyed device item (it only names the map; the handler checks its own entities). Values: set at, delay (the fuse, or the retry's 60,000 ms), the record, the cell x and y, 1 for a
  marker entity. A restore refuses an event whose entity or map is invalid, whose values are out of range, set after the saved clock, or whose deadline is not its setting plus its delay.
  The registry version stays 1 (the kind is added to it as the earlier saved kinds were) and the snapshot schema is unchanged.
- When due (0x4BC780) the handler destroys the marker and reports the firing in `SessionEventPassResult.TrapDevices`. It does **not** start the spell run itself (the run rules refuse a host
  port called from inside a pass): a pass without a cast host (`ISessionTrapDeviceWorld`, implemented by the cast host's pass world) schedules the fuse again a minute on and reports it
  `Postponed`, keeping the marker.

## Cast host (`SessionSpellCaster`)

- `UseTrapDevice(item, tile, onTile)` is the hero's use: the plan, `PlaceTrapDevice`, then the runs that had the device as their source are cleared. It returns `Placed`, `Script` (nothing changed),
  `Invalid`, `Unknown` or `Refused` with the session's failure; the device stays in every case but `Placed`.
- After an event pass (`ProcessEvents`) each fuse that ran is cast by `CastAtTileWithoutCaster` (0x455A20 with no source, then 0x455AC0): an invocation with no parent and no source, the tile as the
  target, so the run has no casting goal and its BEGIN comes as an event; the results are read by `TakeTrapDeviceCasts`. What the run does is the spell host's (ledger TP7: the radius-2 area at the
  tile, Damage with no attacker, the Summon of prototype 4050, the eye candy; knockback and Destroy with Tgt_Self do nothing with no caster).

## AI host and viewer

- `AreaAiHost.TrapDeviceAlarm` is 0x4AA0D0, given when a device of **record 176** is used (not when it goes off): for every NPC in the square of half-size 10 around the hero except a dead,
  gone or mind-controlled one and one the hero leads, one that sees the hero or hears it (silent) is attacked by the hero through the attack event (silent noise of radius 2).
- The AREA viewer's item use (the inventory's Use icon, the item cursor's goals 39 and 41) places the device through `TerrainViewerApp.PlaceTrapDeviceUse`; the marker is drawn as the created
  scenery of its prototype. Smoke: the stage `item-aim` ends with the bomb placed on a tile, its fuse run, the marker gone and record 176 cast at the tile.

## Limits

- The marker does **not** carry `OF_TRAP_PC` or any trap state (TP2 writes both on the original's marker): it is a plain created scenery. A fuse the session cannot run (the run budget refuses the cast, or the revision bound is reached when it falls due) is consumed with no effect and no retry; `TakeTrapDeviceCasts` reports the refused cast.
- The placement's spotted mark is quiet in the original (0x4BC480 passes the quiet argument 1 to 0x4BC090), so no line of `mes\trap.mes` is shown when a device is placed; the viewer's status line only names the outcome.
  The timed marker is a plain created scenery: a trap object's disarm and spot state (ARC-WORLD-076) does not know it (a spell-device marker has no script, so nothing could trigger it in the original either).
- An NPC's use of a trap device stays NOT READY (no installed AI item entry is a trap device, IA15).
- The marker is a scenery of the shown map; a fuse on another map waits for it (late on return), as the other map-scoped events do.

## Script trap devices (ARC-WORLD-125)

The devices whose slot 1 is empty (items 15100 and 15120, generic flag 0x4) copy their own use script (attachment point 1: number, 32 local flags, four counters) onto the target, as the
executable's object form (0x4BC2E0) and tile form (0x4BC480) do ([ledger IA13](../research/ledger/items.md#item-aiming-and-use-with-a-target-arc-res-113), TP2). The installed items 15100 and
15120 carry native scripts (15100 the script 30001); no device holds a number outside the catalogue.

### Rules (`TrapDeviceRules.ScriptOnObject`, `ScriptOnTile`, `ScriptMarkerPrototype`)

- Object form: a **portal** (type 1) or a **container** (type 2) whose use slot holds no script takes the trap. Any other object, or one that already holds a script, is refused: the device stays and the player gets
  line 2 of `mes\trap.mes`. A wall (type 0) takes the tile form.
- Tile form: a tile on which a trap object (type 17) stands already (0x4BCAB0) refuses it with line 3 and the device stays. Otherwise a marker is made at the tile.
- The marker prototype follows the script: for the native scripts 30000..30007 the table at 0x5B5F60 (the decompiler's `0x598AA0 + number * 4`) holds 26000..26007 in order; any other script takes
  26000 when its file header says magical (0x4BBE00 answers 2) and 26001 otherwise.

### Session (`GameSessionOwner.PlaceScriptTrapDevice`)

- One revision: the device's use slot is written onto the carrier as the map changes `use.script`, `use.lf` and `use.counters` of its entity (the keys the use-script host reads, so an object armed this way meets a
  use through the trap gate of [trap state](trap-state-runtime.md) at once); the carrier gets the PC-placed and spotted marks (`trap.state` bits 4 and 1, which also clear an earlier removal: a
  re-armed object is trapped again); the device is destroyed out of the pack. The carrier is the target object (nothing is made) or a **marker**, a created scenery of the prototype above made at the tile.
- Refused before any effect with the device kept: a stale version, a form that names both or neither of a target and a marker, a script number outside the catalogue or 0 (the original would copy an empty slot and
  spend the device; no installed device does), a device the hero does not carry unworn or that is no generic item, a map or cell out of range, the created-object, durability or map-change budget, the revision bound.
- `QueryScriptTrapMarkers(map, cell)` lists the markers standing on a tile; `QueryPlacedTrapScript` and `QueryMarkerTrapBaseline` read a placed slot back (the baseline of a marker is a trap object with
  don't-draw, which the spotted mark clears). The PC-placed bit makes a free NPC not know the trap and the player's critical disarm give the device prototype 6047 (or 15101), as TT2 and TT6 say.
- The save needs no schema step: the keys are the ones of the use slot and `trap.state`, and a saved value with bit 4 is read back.

### Cast host and viewer

- `SessionSpellCaster.UseScriptTrapDevice(item, script, class, tile, target, trapObjectOnTile)` is the hero's use after the plan found slot 1 empty (`UseTrapDevice` answers `Script`); it asks the rules, then the
  session, and clears the runs that had the device as their source. The outcomes are `Armed`, `TargetRefused` (line 2), `TileRefused` (line 3), `Refused` (the session's failure), `Invalid` and `Unknown`.
- The AREA viewer's item use (`TerrainViewerApp.PlaceScriptTrapUse`) reads the device's slot (the session's own for a made item, else the carried map item's data through `AreaUseScriptHost.ItemUseSlot`), its trap
  class from the script file header (`TrapHeaderFlags`), the target's type from the cast host and its use script as the trap state reads it (`UseScriptOf`), and whether the shown map's data holds a trap
  object on the tile. A map object with no use slot in its data still takes the trap: the host reads a written use script as an empty slot under it. Smoke: the stage `item-aim` arms the
  device on a tile (marker 26001 for the script 30001), refuses a second one on the same tile with line 3 and an NPC target with line 2, the device staying.

### Limits

- A **marker does not fire**: the walk and the step of the hero meet the shown map's trap objects only (`AreaTraps`), so the hero stepping on a marker, spotting it by a roll, disarming it, or a free NPC meeting
  it is a later slice; the session already holds its state (placed, spotted, script, counters), so the slice needs no new state. A marker is drawn as the created scenery of its prototype,
  whatever its spotted state. An object armed by a device does meet a use (a container's open, a portal's use), an attack that breaks it and the disarm rules.
- The player gets lines 2 and 3 of `mes\trap.mes` in the viewer's status text, not in a message window, and no sound is read (IA13).
- The tile check counts the shown map's trap objects (the rows of its data) and the session's markers; a trap object of another map is not asked (the hero uses a device where the hero stands).
- The spotted eye candy a native script plays on its object (TT4) and the object's drawing are not part of the placement.
- An NPC's use of a device stays NOT READY (no installed AI item entry is a trap device, IA15).

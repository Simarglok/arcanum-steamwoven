# Placed trap devices (ARC-WORLD-124)

Status: **Implemented bounded component**, work item ARC-WORLD-124 (slice E6-b of the
[technology audit](../research/technology-audit-res-125.md)), for the devices whose slot 1 holds a spell: the two timed bombs, item 15154
(record 176) and item 15193 (record 220). The script trap devices (items 15100 and 15120, slot 1 empty: a script written onto a portal, a container or a tile, the refusals of `mes\trap.mes`
lines 2 and 3) are ARC-WORLD-125; their use still answers NOT READY and the device stays. Requirements:
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
- The spotted line (`mes\trap.mes` line 0, 0x4BC090) is shown in the status text, not in a message window, and the marker is a plain created scenery: a trap object's disarm and spot
  state (ARC-WORLD-076) does not know it (a spell-device marker has no script, so nothing could trigger it in the original either).
- An NPC's use of a trap device stays NOT READY (no installed AI item entry is a trap device, IA15).
- The marker is a scenery of the shown map; a fuse on another map waits for it (late on return), as the other map-scoped events do.

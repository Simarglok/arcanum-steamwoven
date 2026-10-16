# Map catalogue and transition graph

Status: **Research**, work item ARC-RES-035 (whole-world plan, section R2,
2026-10-16). This note catalogues every map of the `Arcanum` module and every
way the data moves the player between maps or across one map: jump points,
teleport actions in scripts and the objects, tiles and sectors that run them,
with their conditions, and which of them are unconditional. It adds one
read-only probe mode and this note. It changes no production API, viewer,
session or launcher file.

## Vocabulary and boundary

- **Source fact**: read from the pinned CE specification
  [`a7ff41b300ef712f0e7d088183a3d08957110cdd`](https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd)
  (Sustainable Use License v1.0, not MIT); line ranges below are what was read. It
  is a specification lineage, not an original-executable observation.
- **Local observation**: measured by the probe on the owner's unchanged
  installation through the [ARC-DATA-011 content stack](content-precedence.md)
  (winner per virtual path). The probe prints only numbers, booleans and SHA-256
  digests; no map, script, object or dialog names, text or payload bytes.
- **Project policy**: a classification this project chooses where the source
  leaves the question open (what counts as "unconditional", below).

`tools/research/Arcanum.CrashSiteProbe/WorldTransitionProbe.cs` was written
independently from the rules stated here and does not translate CE code. It
decodes, it does not execute: no script VM, dialog, movement or map lifecycle
runs. Original executable parity remains **NOT_VERIFIED**.

Map numbers: the **catalogue** number is the `rules/maplist.mes` key
(5000..5080); the **runtime** number is the row's position plus one
(catalogue − 4999). Scripts name destinations by catalogue number, jump points
by runtime number.

## How to run

```bash
dotnet run --project tools/research/Arcanum.CrashSiteProbe -- --world-transitions --game-root <game folder>
dotnet run --project tools/research/Arcanum.CrashSiteProbe -- --world-transitions-self-test
```

The report is deterministic JSON. On the owner's installation two runs gave the
same SHA-256 `ba843273a29afdc61c0356edb4db4aba553c97c794dbb8d71e74a89485db8121`
in about 3 s. Inputs: the seven archives of the stack (fingerprinted before and
after the run) plus the `data/` and module folders. The report sections are
`maps`, `jump_points`, `teleport_sites`, `edges` (one row per attachment and
reachable teleport), `orphan_sites`, `graph` and `gaps`.

## Answer

- **81 catalogue rows, 79 real maps.** The catalogue is an unbroken run
  5000..5080. Rows 5021 and 5025 have no map folder (CE refuses to open a map
  without one); one more map, 5013 (one sector), has no transition in or out.
  Two map folders in the module (18 sector files, 21 mobiles) are not in the
  catalogue and can never be opened. 5000 is the start map (2000×2000 sectors,
  621 hand-made sector files); the other 78 maps are interiors and dungeons of
  1×1 to 64×64 sectors.
- **45 jump points on 17 maps, and only 7 of them leave their map.** A jump
  point fires when the player enters its cell, with no condition. 38 of them
  move the player inside the same map; 6 cells of 5011 lead to 5000 and one cell
  of 5071 leads to 5007.
- **Scripts carry the rest: 457 teleport actions in 357 scripts**, 428 plain
  teleports and 29 fade-and-teleports. Every destination (map, x, y) is a
  literal number; none is computed. 554 attachments on placed objects, tiles and
  sectors reach one of them; together with dialog resumption they give 873
  attachment-to-teleport paths.
- **255 of the 873 paths are unconditional** (policy below): a use click on a
  visible object, or a tile or sector entry, running a teleport of the player on
  a path without any non-trivial condition. 156 of them change the map.
- **158 directed map pairs, 110 with an unconditional path.** From the start map
  48 maps (5000 included) are reachable through jump points and unconditional
  paths only, and every one of them has an unconditional way back to 5000.
  30 more maps are reachable only through conditional paths; 3 catalogue rows
  are unreachable (5013, 5021, 5025).
- **What gates the conditional paths:** mostly a global flag compared with a
  number (quest progress), a held item (17) or a wielded item (21), dialog
  choices, an NPC's death or a broken object. The details are in
  [Conditions](#conditions).
- **40 teleport actions in 33 scripts are not attached** to any placed object,
  tile or sector and are not reached by a literal call from an attached script
  (see [Gaps](#gaps-and-not_verified)).

## Findings

| ID | Claim | Source fact | Local observation |
| --- | --- | --- | --- |
| W1 | Map catalogue and map identity | [map.c 2003–2060](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L2003-L2060), [792–847](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L792-L847), [880–943](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L880-L943) | Rows from 5000 while the key exists: name, start x, start y, then optional `Type:` (`START_MAP`), `WorldMap:` and `Area:`. 81 rows, no key after the run. One `START_MAP` row (5000). `WorldMap:` absent on 10 rows, 0 on 71. 32 distinct `Area:` values; 0 (or absent) on 8 rows. |
| W2 | Map folder files | [map.c 579–790](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L579-L790), [1599–1640](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L1599-L1640) | `map.prp` is 24 bytes: base terrain type, 4 padding bytes, width and height in cells (64-bit). All 79 present; sizes in the table below; terrain types 2 (73 maps), 7 (5), 4 (1). `startloc.txt` (two decimal lines) on 79 maps, 71 of them with the same editor value; the new game uses the catalogue start instead (W8). `mapinfo.txt` on 79: one `LightScheme` each (2 on 68 maps), one `SoundScheme` (music, ambient) on 72. `terrain.tdf` on 79. |
| W3 | Jump point format and trigger | [jumppoint.h 6–16](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/jumppoint.h#L6-L16), [jumppoint.c 187–224, 392–405, 607–630](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/jumppoint.c#L607-L630), [map.c 1002–1018](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L1002-L1018), [object.c 5240–5251](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L5240-L5251), [teleport.c 190–194](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L190-L194) | `map.jmp` is a count and 32-byte records: flags, padding, source location, runtime destination map, padding, destination location (x low, y high 32 bits). Entering the cell as a PC teleports that PC (nesting below 9), flags 0, no condition. Destination map −1 means the current map. 79 files: 62 empty, 17 with 45 records; all flags 0. |
| W4 | Script teleport actions | [script.h 151–293, 299–325](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.h#L299-L325), [script.c 1875–1889, 2405–2441](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L2405-L2441) | Teleport (44): focus object, map, x, y. Fade-and-teleport (102): seconds of time advance, sound, movie, focus, map, x, y. The map is a catalogue number (−4999 at run time). 2,399 script numbers, all framed; 39 winners shadow an older copy. 457 teleport actions in 357 scripts, all with literal map, x and y. Focus: triggerer 440, player 9, attachee 8. |
| W5 | Script control flow | [script.c 278–388, 586–592, 1507–1535, 1622–1665](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L278-L388) | A script runs from an entry line; each line evaluates its condition and runs the true or the false action; the next line follows unless the action returns, starts a dialog (which also returns) or jumps (`goto` to a literal line). Condition type 0 is always true. Loops repeat over the objects their focus resolves to. From line 0, 265 teleport actions lie on the forced path, 84 behind a condition, 108 are not reached (most only by dialog resumption, W7). |
| W6 | Who runs a script | [script.h 7–44](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.h#L7-L44), [tile_script.c 360–389](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tile_script.c#L360-L389), [tile_script_list.h 8–13](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tile_script_list.h#L8-L13), [sector_object_list.c 680–730](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_object_list.c#L680-L730), [object.c 2536–2672](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L2536-L2672), [portal.c 30–38](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/portal.c#L30-L38) | An object's script slot index is its attachment point: 1 use, 2 destroy, 9 dialog, 12 dying, 19 heartbeat, 30 bust (the object is broken). A tile script (24-byte node: flags, tile index, script flags, counters, script number) runs at line 0 for any critter entering the tile; a sector script runs at line 0 when the PC enters the sector; both as a use. 9,854 attachments in 1,662 sector files and 12,702 mobiles; 554 run a script that holds or calls a teleport, 530 of them reach one: 303 scenery and 6 portals in sectors, 170 tiles, 46 NPCs and 5 containers among the mobiles, no sector script. |
| W7 | Dialog resumption | [script.c 1513–1535](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1513-L1535), [dialog_ui.c 295–306, 400–406](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/dialog_ui.c#L400-L406), [dialog.c 2408, 2538–2549](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L2538-L2549) | A dialog action starts the conversation of the DLG with the same number and returns. A player response of 0 ends it and resumes the NPC's dialog script on the line after the dialog action; a response of −N resumes it on line N. 501 negative responses in the DLGs of the dialog-attached teleport scripts; 121 paths start this way. |
| W8 | Other transfers (not catalogued as edges) | [wmap_ui.c 1760–1773](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/wmap_ui.c#L1760-L1773), [magictech.c 2700–2747](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L2700-L2747), [mainmenu_ui.c 1651–1679](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/mainmenu_ui.c#L1651-L1679), [ai.c 2596–2663](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2596-L2663) | World-map travel always lands on the start map. The teleportation spell takes the player to an area location on the start map (from another map only through that map's `Area:`), unless the place blocks teleports. A new game places the PC on the start map at the catalogue start. NPC waypoints and standpoints teleport NPCs between maps. None of these needs map-pair data. |
| W9 | What a teleport does | [teleport.c 114–139, 164–271](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L164-L271) | As the [cave research](cave-transition-evidence.md#t4--what-the-teleport-does): one pending PC teleport at a time, followers that do not wait travel along, the destination map opens with its saved differences, no passability check at the arrival. All 873 arrivals lie inside their map's bounds; 872 land in a sector that has a file, one lands on a generated sector of the start map. |

## Unconditional (project policy)

A path from an attachment to a teleport action counts as **unconditional** when
all of these hold; otherwise the report lists every reason that fails:

1. The trigger is a use click on an object (slot 1), or a tile or sector entry.
   Dialog, dying, destroy, heartbeat and bust triggers are reported as `sap_N`.
2. The object is not off or destroyed (`OF_OFF`, `OF_DESTROYED`) and, for a use
   click, can be picked (not don't-draw, invisible or click-through;
   [obj_flags.h 11–22](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj_flags.h#L11-L22)).
3. The attached script has no trap file flag (a trap check runs first).
4. Every line from line 0 to the teleport, through each literal call, lies on
   the forced path: only always-true conditions, no loop, no call that is
   delayed (call-in/at) or replaces a script.
5. The destination is literal and the triggerer or the player travels.

Jump points are unconditional by their source rule (W3). The policy does not
look at what a condition reads at T0: a quest flag that is already set at a new
game still counts as a condition. The same location reached by an
unconditional and a conditional path counts as unconditional for the pair.

| Trigger | Paths | Unconditional | Change the map | Same map |
| --- | ---: | ---: | ---: | ---: |
| Use click (slot 1) | 286 | 246 | 178 | 108 |
| Tile entry | 421 | 9 | 267 | 154 |
| Dialog (slot 9) | 121 | 0 | 5 | 116 |
| Bust (slot 30) | 31 | 0 | 2 | 29 |
| Dying (slot 12) | 12 | 0 | 12 | 0 |
| Destroy (2), heartbeat (19) | 2 | 0 | 1 | 1 |
| **All** | **873** | **255** | **465** | **408** |

The unconditional cross-map paths are mostly use clicks on scenery (144 paths),
then tiles (5), containers (5) and portals (2). The Crash Site cave (5000 ↔ 5078,
scripts 2793 and 1532) is one of them, as the
[cave research](cave-transition-evidence.md) found.

## Conditions

A guard is a condition met on the way to the teleport, on either branch. Each
of the 618 conditional paths and each of the 48 pairs without an unconditional
path counts once per guard kind, so a row can share paths with another.
Condition and value numbers follow
[script.h 73–81, 95–149](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.h#L95-L149).

| Guard | Meaning | Conditional paths | Pairs without an unconditional path |
| --- | --- | ---: | ---: |
| 21 | the triggerer wields a given item | 328 | 17 |
| 4, global flag vs number | a global flag equals a value (quest progress) | 305 | 43 |
| 4, local variable vs number | a variable of the attachment equals a value | 148 | 3 |
| dialog re-entry | reached only by resuming a conversation (W7) | 121 | 4 |
| 17 | the triggerer holds a given item | 73 | 18 |
| deferred call | a call-in (24), a script replacement (3) | 27 | 1 |
| 20 | the object is a given named object | 26 | 1 |
| loop | inside a loop over a focus group | 17 | 4 |
| 18 | the object follows the PC | 14 | 1 |
| 33 | the object has a reputation | 8 | 0 |
| 4, counter or global variable vs number | a script counter or a global variable equals a value | 10 | 2 |
| 6 | PC quest state | 5 | 2 |
| 5 | less or equal | 5 | 0 |
| 13, 28, 29, 52, 40 | shrunk, can see, can hear, waits for leader, rumor known | 2, 1, 1, 1, 1 | 1 each |

The 48 pairs with no unconditional path:

- **16 arenas behind 5031 and back.** Tiles in 5031 send the player to 5042..5057
  according to the item wielded (21); each arena returns to 5031 by a tile that
  checks a held item (17) and a global flag.
- **5000 to 5006, 5009, 5018, 5040, 5064, 5080, 5007 to 5072 and 5018 to 5019**
  by use clicks behind a global flag (5018 also by dialog, 5019 also behind a
  known rumor); **5000 to 5016 and 5077** by use clicks in a loop behind
  comparisons and named, seeing, hearing or shrunk checks.
- **5000 to 5069** by a tile behind a held item and a local variable; **5069
  back to 5000** by a use click behind a wielded or held item and a global flag.
- **5000 to 5007** by dialog or an NPC's death (quest state), as is **5058 to
  5007**; **5000 to 5010** by dialog behind a follower check; **5010 back to
  5000** by dialog, a death or a destroy behind flags and a delayed call.

## Map catalogue

Sectors are the `map.prp` size (W×H in sectors); files are sector files with a
hand-made layout and mobile files. Area and light are the catalogue `Area:` and
the `mapinfo.txt` light scheme. Jump points count all records, with the same-map
ones in brackets. "Same-map script paths" counts attachment paths that stay on
the map. Exits list destination catalogue numbers: **J** jump point, **U** an
unconditional script path, **C** conditional paths only. Reach from start: the
class of the best route from 5000.

| Catalogue | Runtime | Sectors (W×H) | Files: sec / mob | Area | Light | Jump points (same-map) | Same-map script paths | In | Exits | Reach from start |
| ---: | ---: | --- | --- | ---: | ---: | --- | ---: | ---: | --- | --- |
| 5000 | 1 | 2000×2000 | 621 / 7399 | 0 | 1 | 0 | 140 | 50 | 5001U, 5002U, 5005U, 5006C, 5007C, 5008U, 5009C, 5010C, 5011U, 5014U, 5015U, 5016C, 5017U, 5018C, 5020U, 5022U, 5023U, 5024U, 5026U, 5027U, 5028U, 5029U, 5030U, 5031U, 5032U, 5033U, 5036U, 5037U, 5038U, 5039U, 5040C, 5041U, 5058U, 5060U, 5061U, 5062U, 5063U, 5064C, 5065U, 5066U, 5067U, 5068U, 5069C, 5070U, 5073U, 5075U, 5076U, 5077C, 5078U, 5079U, 5080C | start |
| 5001 | 2 | 3×3 | 9 / 37 | 1 | 2 | 0 | 0 | 1 | 5000U | unconditional |
| 5002 | 3 | 3×3 | 9 / 17 | 21 | 2 | 0 | 0 | 2 | 5000U, 5003U | unconditional |
| 5003 | 4 | 3×3 | 9 / 27 | 21 | 2 | 0 | 0 | 2 | 5002U, 5004U | unconditional |
| 5004 | 5 | 3×3 | 9 / 30 | 21 | 2 | 0 | 0 | 1 | 5003U | unconditional |
| 5005 | 6 | 12×12 | 43 / 591 | 7 | 2 | 0 | 10 | 1 | 5000U | unconditional |
| 5006 | 7 | 12×12 | 24 / 456 | 9 | 2 | 0 | 23 | 1 | 5000U | conditional |
| 5007 | 8 | 33×33 | 54 / 106 | 0 | 7 | 0 | 132 | 5 | 5071U, 5072C, 5074U | conditional |
| 5008 | 9 | 6×6 | 36 / 209 | 4 | 2 | 0 | 0 | 2 | 5000U, 5059U | unconditional |
| 5009 | 10 | 5×3 | 12 / 0 | 21 | 2 | 0 | 0 | 2 | 5000U, 5011U | unconditional |
| 5010 | 11 | 3×3 | 1 / 13 | 19 | 8 | 0 | 0 | 1 | 5000C | conditional |
| 5011 | 12 | 3×3 | 9 / 39 | 21 | 2 | 6 (0) | 0 | 3 | 5000J, 5009U, 5012U | unconditional |
| 5012 | 13 | 3×3 | 9 / 53 | 21 | 2 | 0 | 0 | 1 | 5011U | unconditional |
| 5013 | 14 | 1×1 | 1 / 41 | 0 | 1 | 0 | 0 | 0 | — | none |
| 5014 | 15 | 3×3 | 5 / 90 | 27 | 2 | 0 | 0 | 1 | 5000U | unconditional |
| 5015 | 16 | 4×4 | 16 / 52 | 21 | 2 | 0 | 0 | 1 | 5000U | unconditional |
| 5016 | 17 | 3×3 | 9 / 34 | 30 | 3 | 0 | 0 | 1 | 5000U | conditional |
| 5017 | 18 | 3×3 | 1 / 22 | 1 | 2 | 0 | 0 | 1 | 5000U | unconditional |
| 5018 | 19 | 64×64 | 12 / 166 | 6 | 1 | 0 | 0 | 2 | 5000U, 5019C | conditional |
| 5019 | 20 | 3×3 | 9 / 1 | 6 | 2 | 0 | 0 | 1 | 5018U | conditional |
| 5020 | 21 | 3×3 | 9 / 43 | 68 | 2 | 0 | 0 | 1 | 5000U | unconditional |
| 5021 | 22 | no folder | 0 / 0 | 0 | — | 0 | 0 | 0 | — | none |
| 5022 | 23 | 3×3 | 9 / 19 | 21 | 2 | 0 | 0 | 1 | 5000U | unconditional |
| 5023 | 24 | 3×3 | 9 / 19 | 4 | 2 | 0 | 0 | 1 | 5000U | unconditional |
| 5024 | 25 | 9×9 | 79 / 207 | 4 | 2 | 0 | 22 | 1 | 5000U | unconditional |
| 5025 | 26 | no folder | 0 / 0 | 0 | — | 0 | 0 | 0 | — | none |
| 5026 | 27 | 10×10 | 9 / 86 | 48 | 2 | 0 | 2 | 1 | 5000U | unconditional |
| 5027 | 28 | 64×64 | 4 / 111 | 4 | 2 | 0 | 0 | 1 | 5000U | unconditional |
| 5028 | 29 | 8×8 | 36 / 0 | 4 | 2 | 0 | 1 | 1 | 5000U | unconditional |
| 5029 | 30 | 4×4 | 16 / 25 | 4 | 2 | 0 | 0 | 1 | 5000U | unconditional |
| 5030 | 31 | 10×10 | 9 / 38 | 4 | 2 | 0 | 0 | 1 | 5000U | unconditional |
| 5031 | 32 | 9×9 | 42 / 160 | 11 | 2 | 17 (17) | 2 | 17 | 5000U, 5042C, 5043C, 5044C, 5045C, 5046C, 5047C, 5048C, 5049C, 5050C, 5051C, 5052C, 5053C, 5054C, 5055C, 5056C, 5057C | unconditional |
| 5032 | 33 | 64×64 | 53 / 202 | 22 | 2 | 0 | 16 | 1 | 5000U | unconditional |
| 5033 | 34 | 3×3 | 9 / 8 | 8 | 2 | 0 | 0 | 2 | 5000U, 5034U | unconditional |
| 5034 | 35 | 3×3 | 9 / 29 | 8 | 2 | 0 | 0 | 2 | 5033U, 5035U | unconditional |
| 5035 | 36 | 3×3 | 9 / 13 | 8 | 2 | 0 | 0 | 1 | 5034U | unconditional |
| 5036 | 37 | 64×64 | 1 / 1 | 10 | 1 | 0 | 0 | 1 | 5000U | unconditional |
| 5037 | 38 | 4×4 | 4 / 16 | 10 | 2 | 0 | 0 | 1 | 5000U | unconditional |
| 5038 | 39 | 3×3 | 9 / 4 | 8 | 2 | 0 | 0 | 1 | 5000U | unconditional |
| 5039 | 40 | 7×3 | 21 / 225 | 8 | 2 | 0 | 6 | 1 | 5000U | unconditional |
| 5040 | 41 | 64×64 | 14 / 77 | 16 | 4 | 0 | 2 | 1 | 5000U | conditional |
| 5041 | 42 | 12×12 | 18 / 68 | 37 | 2 | 0 | 2 | 1 | 5000U | unconditional |
| 5042 | 43 | 5×5 | 1 / 4 | 11 | 2 | 3 (3) | 0 | 1 | 5031C | conditional |
| 5043 | 44 | 3×3 | 9 / 6 | 11 | 2 | 0 | 0 | 1 | 5031C | conditional |
| 5044 | 45 | 3×3 | 6 / 47 | 11 | 2 | 1 (1) | 0 | 1 | 5031C | conditional |
| 5045 | 46 | 3×3 | 2 / 71 | 11 | 2 | 2 (2) | 0 | 1 | 5031C | conditional |
| 5046 | 47 | 4×4 | 9 / 6 | 11 | 2 | 1 (1) | 0 | 1 | 5031C | conditional |
| 5047 | 48 | 3×3 | 1 / 142 | 11 | 2 | 1 (1) | 0 | 1 | 5031C | conditional |
| 5048 | 49 | 3×3 | 1 / 33 | 11 | 2 | 2 (2) | 0 | 1 | 5031C | conditional |
| 5049 | 50 | 3×3 | 2 / 25 | 11 | 2 | 1 (1) | 0 | 1 | 5031C | conditional |
| 5050 | 51 | 3×3 | 1 / 8 | 11 | 2 | 1 (1) | 0 | 1 | 5031C | conditional |
| 5051 | 52 | 3×3 | 1 / 4 | 11 | 2 | 1 (1) | 1 | 1 | 5031C | conditional |
| 5052 | 53 | 3×3 | 1 / 47 | 11 | 2 | 1 (1) | 0 | 1 | 5031C | conditional |
| 5053 | 54 | 3×3 | 1 / 80 | 11 | 2 | 1 (1) | 0 | 1 | 5031C | conditional |
| 5054 | 55 | 3×3 | 1 / 42 | 11 | 2 | 1 (1) | 0 | 1 | 5031C | conditional |
| 5055 | 56 | 3×3 | 1 / 11 | 11 | 2 | 0 | 0 | 1 | 5031C | conditional |
| 5056 | 57 | 3×3 | 3 / 68 | 11 | 2 | 0 | 0 | 1 | 5031C | conditional |
| 5057 | 58 | 3×3 | 1 / 19 | 11 | 2 | 1 (1) | 0 | 1 | 5031C | conditional |
| 5058 | 59 | 3×3 | 9 / 68 | 4 | 2 | 0 | 4 | 2 | 5000U, 5007C, 5059U | unconditional |
| 5059 | 60 | 3×3 | 1 / 43 | 4 | 2 | 0 | 0 | 2 | 5008U, 5058U | unconditional |
| 5060 | 61 | 4×4 | 14 / 37 | 44 | 2 | 0 | 0 | 1 | 5000U | unconditional |
| 5061 | 62 | 9×9 | 9 / 15 | 40 | 2 | 0 | 0 | 1 | 5000U | unconditional |
| 5062 | 63 | 5×5 | 9 / 177 | 13 | 2 | 0 | 0 | 1 | 5000U | unconditional |
| 5063 | 64 | 8×8 | 3 / 93 | 53 | 2 | 0 | 4 | 1 | 5000U | unconditional |
| 5064 | 65 | 4×4 | 8 / 10 | 57 | 2 | 0 | 2 | 1 | 5000U | conditional |
| 5065 | 66 | 12×12 | 16 / 123 | 56 | 2 | 0 | 0 | 1 | 5000U | unconditional |
| 5066 | 67 | 12×12 | 3 / 38 | 55 | 2 | 0 | 2 | 1 | 5000U | unconditional |
| 5067 | 68 | 25×25 | 77 / 171 | 58 | 1 | 0 | 27 | 1 | 5000U | unconditional |
| 5068 | 69 | 12×12 | 11 / 58 | 5 | 2 | 0 | 0 | 1 | 5000U | unconditional |
| 5069 | 70 | 20×20 | 40 / 9 | 11 | 2 | 0 | 8 | 1 | 5000C | conditional |
| 5070 | 71 | 7×7 | 1 / 48 | 5 | 2 | 0 | 0 | 1 | 5000U | unconditional |
| 5071 | 72 | 12×12 | 9 / 6 | 0 | 2 | 1 (0) | 0 | 1 | 5007J | conditional |
| 5072 | 73 | 12×12 | 30 / 83 | 0 | 2 | 4 (4) | 2 | 1 | 5007U | conditional |
| 5073 | 74 | 5×5 | 9 / 14 | 47 | 2 | 0 | 0 | 1 | 5000U | unconditional |
| 5074 | 75 | 12×12 | 20 / 27 | 0 | 2 | 0 | 0 | 1 | 5007U | conditional |
| 5075 | 76 | 8×8 | 64 / 269 | 21 | 2 | 0 | 0 | 1 | 5000U | unconditional |
| 5076 | 77 | 4×4 | 1 / 5 | 61 | 1 | 0 | 0 | 1 | 5000U | unconditional |
| 5077 | 78 | 4×4 | 1 / 13 | 62 | 2 | 0 | 0 | 1 | 5000U | conditional |
| 5078 | 79 | 64×64 | 1 / 39 | 2 | 2 | 0 | 0 | 1 | 5000U | unconditional |
| 5079 | 80 | 4×4 | 9 / 6 | 72 | 2 | 0 | 0 | 1 | 5000U | unconditional |
| 5080 | 81 | 4×4 | 9 / 3 | 13 | 1 | 0 | 0 | 1 | 5000U | conditional |

## Jump points

Cells are map locations (x, y). All have flags 0.

| Map | Cells | Destination map | Arrival |
| --- | --- | --- | --- |
| 5011 | (109..110, 92..94), 6 cells | 5000 | (61976, 65664) |
| 5031 | (436..437, 214..216), 5 cells | same | (304, 219) |
| 5031 | (307..309, 219..222), 12 cells | same | (438, 219) |
| 5042 | (183,150); (183,142); (146,145) | same | (183,159); (150,145); (163,165) |
| 5044 | (68,95) | same | (121,68) |
| 5045 | (73,79); (69,99) | same | (120,100); (116,119) |
| 5046 | (72,184) | same | (118,134) |
| 5047 | (69,71) | same | (118,100) |
| 5048 | (86,73); (112,88) | same | (72,73); (121,105) |
| 5049 | (82,76) | same | (122,98) |
| 5050 | (69,79) | same | (118,116) |
| 5051 | (106,75) | same | (121,99) |
| 5052 | (81,79) | same | (120,110) |
| 5053 | (85,79) | same | (116,107) |
| 5054 | (69,79) | same | (121,95) |
| 5057 | (77,79) | same | (122,112) |
| 5071 | (351,307) | 5007 | (1077, 875) |
| 5072 | (653,93); (565,96); (439,71); (553,106) | same | (564,96); (654,93); (552,105); (439,72) |

Some same-map records store the map's own runtime number, others −1; both keep
the current map (W3).

## Consequences for the whole-world plan

- **M3 transitions can start with one mechanism.** Use-click teleports on
  scenery carry 144 of the unconditional cross-map paths and reach all 48
  unconditional maps; the cave (ARC-WORLD-009) already implements exactly this
  shape. Jump points add only 5011 → 5000 and 5071 → 5007 across maps, plus
  same-map shortcuts on 15 maps.
- **"Unconditional teleports only"** (pending owner decision) would admit 48
  maps, all with an unconditional way back to the start map. The other 30 need
  the global-flag, item and dialog conditions of
  [Conditions](#conditions), i.e. the script VM and session state.
- Tile triggers fire for any critter, not only the player, and most of them are
  gated; an implementation must not treat a tile teleport as a player-only door.

## Gaps and NOT_VERIFIED

- **40 teleport actions in 33 scripts have no attachment path**: 31 are in
  scripts that nothing placed attaches and nothing calls literally, 9 are called
  only from such scripts, 1 sits in an attached use script on a line that no
  analysed entry reaches. Their destinations are 5000
  (19), 5007 (18), 5008 (2) and 5006 (1). They may run from objects created at
  run time, item prototypes, call-ex or computed calls; the probe does not follow
  those.
- **Not followed:** call-ex (an object's own attachment), computed calls and
  gotos (none occur on teleport paths), objects created by scripts, scripts
  attached to prototypes of objects that are not placed, triggers on items in
  inventories other than through their placed holders, the two folders outside
  the catalogue.
- **What a condition reads at T0 is not evaluated**: a guarded path may be open
  at a new game. Script counters, the editor `startloc.txt`, the jump point
  flags word (always 0) and the script file flag 0x100 (defined, never read by
  CE) are recorded only.
- **Bust triggers** (31 paths, slot 30) are on scenery and portals with the
  respawnable or bustable state; whether the original fires them for these
  objects is NOT_VERIFIED.
- The normal game reads mobiles from per-map aggregates rebuilt from the module's
  `.mob` files; the probe reads the `.mob` files (the
  [census](data-census.md) frames all aggregates against them).

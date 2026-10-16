# Where Virgil's scheme-101 event is attached

## Status, scope and evidence discipline

Status: **Research**, work item ARC-RES-037 (2026-10-16). It answers the open
questions **U6**, **U7** and **U9** of [ARC-RES-032](virgil-level-replay.md#unknowns-u):
which installed objects run script 2023 (the event that sets Virgil's scheme
101) and scripts 2498 and 2648 (which set global flag 2593), whether any DLG,
including the 38 the strict reader refuses, writes flag 2593, and what the
call-ex actions reachable from his scripts run. It adds one read-only probe
mode and this note. It changes no production API, viewer, conversation
runtime, `Arcanum.Session` or launcher file.

Categories as in [ARC-RES-032](virgil-level-replay.md): **Source (S)** is
`alexbatalov/arcanum-ce` at
[`a7ff41b300ef712f0e7d088183a3d08957110cdd`](https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd),
read for the meaning of numbers only; **Data (D)** is the installed 1.0.7.4
`Arcanum` module read through the [content stack](content-precedence.md) by
the probe, so every map, sector, mobile, aggregate, terrain template,
prototype, script and DLG is the winner of its path; **Unknown (U)** as before.
The report holds numbers, map folder and file names, and booleans; no MES or
DLG text. Original executable parity remains **NOT_VERIFIED**.

## Answer

- **U6, script 2023 (D + S).** It is a **tile script on the world map**
  (catalogue 5000) on four tiles in one row, (96390..96393, 18903), in sector
  (1506, 295). A tile script runs when any critter steps onto its tile, with
  that critter as triggerer, so it fires when Virgil (name 6409) walks over one
  of those tiles while flag 2593 is set and the story state is above 14. It
  then sets his scheme to 101 and sends him to catalogue map 5023
  (`Caladon-Mafia Underground`), tile (92, 95), with standpoints there.
- **U6, flag 2593 (D + S).** Two writers, both found:
  - **Script 2498**, a tile script on eight tiles just south of the 2023 row
    (96389..96391, 18908..18910, centre tile empty) in the same sector. It sets
    flag 2593 for any critter that steps on it, until flag 2547 is set. Then,
    for a triggerer named 6409 and (unless flag 2744 is set) a story state
    above 14, it picks his dialog script: 1842 with flags 2444 and 2547 when
    the player has quest 1025 in state 4 or his alignment is −699 or lower,
    otherwise 2019, which it calls at once. Either way it removes itself. For a
    triggerer named 6614 it may start that NPC's dialog instead.
  - **Script 2648**, the dying script (attachment point 12) of one NPC on the
    world map, present both as a mobile file and in the game-generated
    aggregate. It uses **Virgil's prototype 17102** but has its own name 6400 and
    description 30419, and stands at tile (20678, 56022). When it dies it sets
    flags 2591 and 2593.
  - No prototype, terrain template, sector script or other object attaches any
    of the three scripts.
- **U7 (D).** A lenient scan of all 709 DLG files, including the 38 the strict
  reader refuses, finds no `GF 2593` anywhere. So only the two scripts above
  set the flag.
- **U9 (D + S).** All 15 call-ex actions reachable from his six scripts run the
  **dialog** attachment point (9) of an object: 13 of "the current looped
  object", 2 of the attachee (Virgil himself). They start conversations; none
  is a new path to a level change. As ARC-RES-031 showed, no installed script
  adjusts the level stat at all.

## Inputs and reproduction

```sh
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --script-attachments-self-test
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --script-attachments \
  --game-root "$GAME" --scripts 2023,2498,2648 --flags 2593 --roots 1324,1801,1997,1100,1568,1848 > "$PRIVATE/attach.json"
```

The run takes about two seconds. It scanned 81 maps, 1,680 sector files, 12,723
mobile files, 81 aggregates, 413 terrain template sectors, 1,423 prototypes
(every object's prototype resolved) and 709 DLGs, with no refusal. The map
catalogue (`rules/MapList.mes`) has 81 entries from 5000.

## 1. How the attachments run (S)

| ID | Claim | Source |
| --- | --- | --- |
| A1 | A sector holds a list of tile scripts: each 24-byte node is flags, the tile within the sector (x + 64·y), then a script attachment (flags, counters, number). | [tile_script_list.h 8–13][tile-node] |
| A2 | When any critter moves onto a tile, that tile's script runs with the critter as triggerer, no attachee, attachment point "use". | [object.c 5248–5250][move], [tile_script.c 360–389][tile-exec] |
| A3 | A sector file name is the sector key: x in the low 26 bits, y above. | as in the [cave research](cave-transition-evidence.md) |
| A4 | Catalogue maps are the `MapList.mes` entries read as an unbroken run from 5000; a teleport or standpoint map operand is a catalogue number. | [map.c 2003–2060][maplist], [script.c 1875–1889][teleport] |
| A5 | Call-ex runs the script that its first object operand has on the attachment point given by the second operand, from the line in the third, with the fourth operand as triggerer. | [script.c 2246–2248][call-ex-case], [script.c 2880–2887][call-ex], [object.c 3866][script-exec] |
| A6 | The conditions and actions read below: quest state (6), named (20), compare (4, 5); goto (3), remove this script (5), call script (7), assign (10), get stat (33), change script (99), set scheme (139), standpoints (140, 141), teleport (44). | [script.c 631–668][conditions], [script.c 904–925][named], [script.c 1511–1575][actions], [script.c 1535–1543][call-script], [script.h 95–294][enums] |

## 2. The attachments (D)

| Script | Where | Objects or tiles |
| --- | --- | --- |
| 2023 | world map 5000, sector (1506, 295), tile scripts | 4 tiles: (96390..96393, 18903) |
| 2498 | world map 5000, same sector, tile scripts | 8 tiles: x 96389..96391, y 18908..18910 without (96390, 18909) |
| 2648 | world map 5000, NPC mobile and its aggregate record | 1 NPC: prototype 17102, name 6400, description 30419, tile (20678, 56022), dying script |

Virgil at the Crash Site is the same prototype with name 6409 and description
31073 ([ARC-RES-031](virgil-auto-level.md)), so the 2648 NPC is a second object
built from his prototype, not Virgil himself.

**Script 2498, line by line.** 0: flag 2547 set → end. 1: flag 2593 = 1, for
any critter that steps on the tile. 2: flag 2744 set → skip the story check.
3–4: story state ≤ 14 → end. 5: triggerer named 6409, else go to 18. 6: the
player has quest 1025 in state 4 → 13. 7–8: the triggerer's alignment ≤ −699 →
13. 9–11: dialog script 2019, call script 2019, remove this script. 13–16:
dialog script 1842, flag 2444 = 1, flag 2547 = 1, remove this script. 18–20: a
triggerer named 6614, with flag 2476 clear, gets a call-ex of its own dialog
script at line 20.

**Script 2648.** 0: flag 2591 = 1. 1: flag 2593 = 1. 2: return and run the
default.

**Script 2023** is as decoded in [ARC-RES-032](virgil-level-replay.md#4-scripts-that-act-on-him-d-read-with-s);
its teleport and both standpoints name catalogue map 5023, tile (92, 95).

## 3. DLG and call-ex scans (D)

- **DLG.** The lenient scan looks for the letters `GF` (any case, not after
  another letter), optional spaces and the flag number anywhere in a DLG file.
  It over-reports rather than misses. All 709 files were scanned, 38 of them
  refused by the strict DLG reader; 0 hits for flag 2593.
- **Call-ex.** The 20 scripts reached from his six roots hold 15 call-ex
  actions: scripts 1046 (2), 1100, 1324, 1650, 1651, 1712 (2), 1715 (5), 1828
  and 3094. Every one runs attachment point 9 (dialog); the object is "current
  looped object" (13) or the attachee (1100, 3094); the triggerer is the
  player or the triggerer. One literal edge target (1800) is not installed.

## Proposal (P)

1. A future story slice that moves Virgil can key on these tile triggers and
   flags rather than on map scripts; the tiles and conditions above are its
   real-data fixture.
2. Treat flag 2593 as "the event is armed" (two writers), and flag 2449 as
   "the event has run" (set by 2023, read by his enter-combat script).

## Unknowns (U)

- **U10** The story state that unlocks 2023 and 2498 (above 14) and quest 1025
  were not traced to their writers.
- **U11** Which objects the call-ex "current looped object" iterates over was
  not resolved (it depends on each script's loop action).
- **U12** Original-executable behaviour of tile scripts on NPC movement is
  NOT_VERIFIED.

## Method

The read-only probe `--script-attachments` (self-test
`--script-attachments-self-test` covers the lenient GF scan and the sector
key), source reading at the pinned commit for the meaning of numbers, and the
line decoder of [ARC-RES-032](virgil-level-replay.md). No CE code or
structure was copied.

[tile-node]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tile_script_list.h#L8-L13
[move]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L5248-L5250
[tile-exec]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tile_script.c#L360-L389
[maplist]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L2003-L2060
[teleport]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1875-L1889
[call-ex-case]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L2246-L2248
[call-ex]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L2880-L2887
[script-exec]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L3866
[conditions]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L631-L668
[named]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L904-L925
[actions]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1511-L1575
[call-script]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1535-L1543
[enums]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.h#L95-L294

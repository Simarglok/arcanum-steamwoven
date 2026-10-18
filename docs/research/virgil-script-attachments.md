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
read from the executable's functions at the addresses given, level F0 (not
checked in the executable), for the meaning of numbers only; **Data (D)** is the installed 1.0.7.4
`Arcanum` module read through the [content stack](content-precedence.md) by
the probe, so every map, sector, mobile, aggregate, terrain template,
prototype, script and DLG is the winner of its path; **Unknown (U)** as before.
The report holds numbers, map folder, catalogue and file names, and booleans;
no other MES text and no DLG text. Original executable parity remains **NOT_VERIFIED**.

## Answer

- **U6, script 2023 (D + S).** It is a **tile script on the world map**
  (catalogue 5000) on four tiles in one row, (96390..96393, 18903), in sector
  (1506, 295). A tile script runs when any critter steps onto its tile, with
  that critter as triggerer, so it fires when Virgil (name 6409) walks over one
  of those tiles while flag 2593 is set and the story state is above 14. It
  then sets his scheme to 101 and sends him to catalogue map 5023
  (`Caladon-Mafia Underground`), tile (92, 95), with standpoints there. That
  map already holds an NPC with Virgil's name 6409 and description 31073
  (prototype 17090) at exactly (92, 95), with the dialog script 1712 that 2023
  gives him.
- **U6, flag 2593 (D + S).** Two writers, both found:
  - **Script 2498**, a tile script on eight tiles 5 tiles further along y from the 2023 row
    (96389..96391, 18908..18910, centre tile empty) in the same sector. It sets
    flag 2593 for any critter that steps on it, until flag 2547 is set. Then,
    for a triggerer named 6409 and (unless flag 2744 is set) a story state
    above 14, it picks his dialog script: 1842 with flags 2444 and 2547 when
    the player has quest 1025 completed (state 4) or his alignment is −699 or lower,
    otherwise 2019, which it calls at once. Either way it removes itself from
    the tile that fired; the other seven tiles keep it, and on the 2019 branch
    flag 2547 stays clear, so they can fire again. For a
    triggerer named 6614 it may start that NPC's dialog instead.
  - **Script 2648**, the dying script (attachment point 12) of one NPC on the
    world map, present both as a mobile file and in the game-generated
    aggregate. It is built from the generic critter prototype 17102 (about 50
    mobiles use it, Virgil at the Crash Site among them), keeps the prototype's
    name 6400, overrides the description to 30419, and stands at tile
    (20678, 56022). When it dies it sets
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
(every object that inherits its scripts found its prototype) and 709 DLGs, with no refusal. The map
catalogue (`rules/MapList.mes`) has 81 entries from 5000.

## 1. How the attachments run (S)

| ID | Claim | Source |
| --- | --- | --- |
| A1 | A sector holds a list of tile scripts: each 24-byte node is flags, the tile within the sector (x + 64·y), then a script attachment (flags, counters, number). | tile script list functions (0x4F6310–0x4F6750) and their node record |
| A2 | When any critter moves onto a tile, that tile's script runs with the critter as triggerer, no attachee, attachment point "use". | object 0x444270, tile script 0x4C0980 |
| A3 | A sector file name is the sector key: x in the low 26 bits, y above. | the sector key definition |
| A4 | Catalogue maps are the `MapList.mes` entries read as an unbroken run from 5000; a teleport or standpoint map operand is a catalogue number. | map 0x4125C0, script 0x447220 |
| A5 | Call-ex runs the script that its first object operand has on the attachment point given by the second operand, from the line in the third, with the fourth operand as triggerer. | script 0x447220, 0x44B170, object 0x441980 |
| A6 | The conditions and actions read below: quest state (6), named (20), compare (4, 5); goto (3), remove this script (5), call script (7), assign (10), get stat (33), get story state (42), change script (99), set scheme (139), standpoints (140, 141), teleport (44); focus 0 triggerer, 1 attachee, 10 current looped object, 21 player. | script 0x4451B0, 0x447220 and the condition, action and focus enumerations |

## 2. The attachments (D)

| Script | Where | Objects or tiles |
| --- | --- | --- |
| 2023 | world map 5000, sector (1506, 295), tile scripts | 4 tiles: (96390..96393, 18903) |
| 2498 | world map 5000, same sector, tile scripts | 8 tiles: x 96389..96391, y 18908..18910 without (96390, 18909) |
| 2648 | world map 5000, NPC mobile and its aggregate record | 1 NPC: prototype 17102 (inherited name 6400), description 30419, tile (20678, 56022), dying script |
| 1712 (for comparison) | catalogue map 5023, NPC mobile and aggregate record | 1 NPC: prototype 17090, name 6409, description 31073, tile (92, 95), dialog script |

Virgil at the Crash Site overrides the name to 6409 and the description to
31073 ([NPC dialog evidence](crash-site-npc-dialog-evidence.md),
[item inventory](crash-site-item-inventory.md)); the 2648 NPC has neither, so
it is another NPC sharing a generic prototype, not Virgil.

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
  It could miss a signed operand (`GF +2593`) or a GF glued to another code,
  so the probe also reports how many DLG files contain the number 2593 at
  all (not inside a longer number): none. All 709 files were scanned, 38 of them refused by the strict DLG
  reader; 0 hits.
- **Call-ex.** The 20 scripts reached from his six T0 roots hold 15 call-ex
  actions: scripts 1046 (2), 1100, 1324, 1650, 1651, 1712 (2), 1715 (5), 1828
  and 3094. Every one runs attachment point 9 (dialog). In 13 the object is the
  current looped object (focus 10), in 2 the attachee (1100, 3094); the
  triggerer is the player, the triggerer, or (1100) the current looped object.
  One literal edge target (1800) is not installed. With the scripts the event
  hands him added as roots (2019, 3092, 2023, 2498), 25 scripts and 17 call-ex
  actions are reached, still all attachment point 9.

## 4. The numbers checked against editor sources (X)

Every number this note and ARC-RES-031/032 read through the source
enumerations was checked against two independent sources, both kept in the
project's local toolkit archive (ignored by git; nothing from them is copied
here beyond names):

- **ScriptEd 1.50** ([TimoP80/ScriptEd_v1_50](https://github.com/TimoP80/ScriptEd_v1_50),
  commit `da7e6d2`, GPL), a community script editor whose data files tell its
  maintainers to keep them in sync with the enums of a `Script.h`.
- **Troika's editor documents** in `editdocs.zip` from
  [Terra-Arcanum](https://terra-arcanum.com/downloads/arcanum/resources/editdocs.zip)
  (SHA-256 `a237d7f6…b1137b`): the event-script guide and the reference sheet.

| Table | Source entries | ScriptEd entries | Checked indices | Result |
| --- | ---: | ---: | --- | --- |
| Actions | 142 | 142 | 3, 5, 7, 10, 12, 33, 40, 42, 44, 68, 81, 99, 100, 101, 137, 139, 140, 141 | all name the same action (goto, remove this script, call script, assign, subtract, store stat, transform to basic prototype, store story state, teleport, destroy, call script attached to object at point, change script at point, set/clear global flag, adjust stat, change autolevel scheme, day/night standpoint on map) |
| Conditions | 53 (0 = always) | 52 (1..52) | 4, 5, 6, 20 | same: equals, less or equal, PC has quest in state, object is named |
| Focus objects | 24 | 24 | 0, 1, 10, 21 | same: triggerer, attachee, current looped object, player |
| Value kinds | 7 | 7 | 2, 3, 4 | same: local variable, number, global flag |
| Attachment points | 36 | 36 | 9, 10, 12, 13, 19, 27, 31 | same: dialog, first heartbeat, dying, enter combat, heartbeat, new sector, dialog override |

ScriptEd's descriptions also give the operand order this research assumed:
call script takes script, line, triggerer, attachee; "call script attached
to" takes object, point, line, triggerer. The Troika reference sheet numbers
stat 17 as level, 19 as alignment, and quest state 4 as completed, so script
2498's test is "the player has completed quest 1025". These are community and
design documents, not the executable, so they raise confidence in the
numbering without proving runtime behaviour.

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
  NOT_VERIFIED. As read, a tile script fires only on a tile a critter steps
  onto or arrives at; a teleport catch-up hits only its arrival tile.
- **U13** How the Virgil already standing on map 5023 relates to the one 2023
  teleports there (duplicates, a replacement, or one hidden until the event)
  was not read.

## Method

The read-only probe `--script-attachments` (self-test
`--script-attachments-self-test` covers the lenient GF scan, the number search
and the sector key), the editor sources of section 4, source reading of the executable's functions for the meaning of numbers, and the
line decoder of [ARC-RES-032](virgil-level-replay.md). Earlier provenance is
in git history.


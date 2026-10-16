# Crash Site cave transition

Status: **Research**, work item ARC-WORLD-009 (owner top-5 item 5, 2026-10-16).
This document answers how the Crash Site cave entrance and exit fire and where
they lead, and fixes the facts the cave loader, wall rendering, transition and
return need. It changes no production API, viewer or movement permission.
Requirements: [FR-WORLD-006..009/011/012](../requirements/world.md#fr-world-006)
and [FR-SCENE-007](../requirements/scene.md#fr-scene-007). The earlier
[area inventory](crash-site-area-inventory.md#bounded-cave-endpoints-and-composition)
found the two literal script actions; this closes the three gaps it listed
(normal-game cave counterpart, the two activations, the destination tiles).

## Vocabulary and boundary

- **Source fact**: read from the pinned CE specification
  [`a7ff41b300ef712f0e7d088183a3d08957110cdd`](https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd)
  (Sustainable Use License v1.0); line ranges below are what was read. It is a
  specification lineage, not an original-executable observation.
- **Local observation**: measured by the probe below on the owner's unchanged
  installation. Only numbers and SHA-256 digests leave the probe; no original
  text, names or payload bytes are recorded here.
- **Project policy**: a choice this project makes where the source leaves a
  runtime or product decision open.

`tools/research/Arcanum.CrashSiteProbe/CaveTransitionProbe.cs` was written
independently from the rules stated here and does not translate CE code. It
decodes, it does not execute: no script VM, movement or map lifecycle runs.
Original executable parity remains **NOT_VERIFIED**.

## Answer

The cave is a **separate map** reached by a **use action** on a visible
scenery object, not by stepping on a cell. There are exactly two directed,
literal endpoints, and the way back is an explicit second record, not a
return stack:

| Direction | Trigger object | Script | Destination (catalogue / runtime map, cell) |
| --- | --- | ---: | --- |
| Exterior → cave | sector 86570436011 index 7, prototype 4047, cell (92910,82598) | 2793 | 5078 / 79, (2087,2018) |
| Cave → exterior | cave sector 2080374816 index 99, prototype 4047, cell (2093,2019) | 1532 | 5000 / 1, (92913,82598) |

Both arrival cells are terrain-passable and both lie inside the single
destination sector that the loader has to provide. The return lands three
cells east of the exterior trigger, not on the trigger itself.

## Findings

| ID | Claim | Source fact | Local observation |
| --- | --- | --- | --- |
| T1 | Script file and line format | [script.h 299–325](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.h#L299-L325), [script.c 3532–3555](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L3532-L3555) | Both scripts are 328 bytes: a 64-byte header and two 132-byte lines; file flags 0; each occurs once across the six pinned archives (resource2 only). |
| T2 | What the two lines do | [script.h 46–81, 95–96, 151–196](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.h#L151-L196), [script.c 586–592, 1507–1510, 1875–1889, 2992–3003](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1875-L1889) | Line 0: always-true condition → teleport of the **triggerer** to three literal numbers. Line 1: always-true → return, run default (2793) / return, skip default (1532). False branches do nothing. |
| T3 | What triggers the script | [script.h 7–44](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.h#L7-L44), [intgame.c 3444–3446](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/intgame.c#L3444-L3446), [anim.c 1240–1256, 6500–6531, 6534–6630, 4767–4795](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L6534-L6630), [object.c 3866–3906](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L3866-L3906) | Each trigger is a scenery object whose only attachment is slot 1 (use). It is drawn and clickable: no off, don't-draw, invisible or click-through flag. |
| T4 | Teleport lifecycle | [teleport.c 95–139, 164–271, 369–445](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L164-L271), [map.c 792–872](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L792-L872) | The script passes flags 0: no fade, sound, movie or time advance. Source and destination maps differ, so both directions take the cross-map branch. |
| T5 | Map identity | [script.c 1882](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1875-L1889), [map.c 801–836](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L792-L847) | Catalogue rows 5000 (start (92958,82592), start-map tag) and 5078 (start (2087,2018), area tag 2). The cave's catalogue start equals the teleport arrival. |
| T6 | Arrival cells | [teleport.c 369–445](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L369-L445), F1 of [passability closure](area-passability-closure.md#f1--tile-name-flags) | Cave (2087,2018): mask clear, class-0 tile not blocking, one wall on the tile (edge 7). Exterior (92913,82598): mask clear, facade tile not blocking, no object. |
| T7 | Walls are edge blockers | [object.c 2962–3200](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L2962-L3200), [location.c 369–416](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/location.c#L369-L416), [tig art.c 44–51, 1647–1680](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/tig/src/art.c#L1647-L1680) | 125 walls, one prototype (1020), one wall number (11); edges 1/3/5/7 = 33/31/28/33; 0 window/opening pieces; 8 tiles carry two walls (corners). |
| T8 | Normal-game cave counterpart | [gamelib.c 690–700](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L690-L700), [map.c 1056–1135, 1400–1460, 1668–1690](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L1668-L1690) | The cave aggregate (6,698 bytes) frames to exactly 39 records, a semantic bijection with the 39 loose cave mobiles (22 byte-identical). Both aggregate selectors match the catalogue names. |
| T9 | Nothing else transfers | as T2 | The only other scripted cave scenery returns skip-default; the cave's ten tile attachments call two flag-gated story scripts with no teleport. Neither is in scope. |

### T2 — the two lines

The probe admits exactly one shape and refuses anything else (a computed
operand, another focus object, a non-trivial condition or an unknown action).
The teleport action carries four operands: a focus object and three numbers.
Focus kind 0 is the triggerer, so the object that used the trigger travels;
kinds 1..3 are literal numbers. The map number is the catalogue number; the
source subtracts 4999 to get the runtime map (5078 → 79, 5000 → 1). Operand
slots past the fourth, and the focus slot's value, hold editor memory and
mean nothing. After the teleport is queued, line 1 returns: 2793 lets the
default use behaviour run, 1532 skips it. For scenery with no other use
behaviour this has no visible effect in either direction.

### T3 — the trigger is a use action

Outside combat a left click on scenery queues the use-object goal for the
player. The goal sets a near range of 2 for scenery (1 for containers and
critters), walks the player there, then checks one step from the player
towards the object: if the object's tile is terrain-blocking or that step is
blocked (the object itself temporarily ignored), the use fails. Otherwise the
object's slot-1 script runs with the player as triggerer. A use on a trapped
object first tries to spot the trap; these objects are scenery, not traps.

This is not the jump-point path (cell entry through `map_process_jumppoint`):
both maps' jump lists are empty (inventory). FR-WORLD-008's cell-entry
trigger therefore does not apply to this cave; its "separate explicit trigger
policy" case does.

### T4 — what the teleport does

The script only queues the request; the game loop processes it on its next
ping, and a second player teleport is refused while one is pending. Processing
then: collects followers (none at T0), marks every transferred object, and,
because the maps differ, saves the current map's changes, appends the
transferred objects at the destination location to the destination map's
save-folder object file, marks the source copies destroyed and extinct, opens
the destination map (its base files plus that map's saved differences), sets
the view origin to the arrival cell and pre-loads the 3×3 sectors around it.
The arrival is written without any passability check. Leaving the cave
therefore restores whatever the exterior's saved differences hold; that
persistence is FR-WORLD-013 and belongs to the session-state work item.

### T6 — arrival cells

The cave arrival tile holds wall index 90 (piece 1, edge 7). By T7 that wall
blocks only the step across the tile's y−1 edge; the steps towards x−1, y+1
and x+1 have no wall on either side. The player lands on the inner side of
the corridor's north wall, six cells west and one north of the exit object.
The exterior arrival is a facade tile whose bit 0 is set (not blocking) with
no object on it, three cells east of the entrance object; it lies in core
sector (1451,1290).

### T7 — walls

A wall's art id carries, from bit 0 up: variation (bits 8–9), rotation
(bits 11–13), piece (bits 14–19), wall number (bits 20–27) and art class 1
(bits 28–31). Even rotations are rounded up to the next odd one, so every
wall sits on one of the four tile edges 1, 3, 5, 7 (towards x−1, y+1, x+1,
y−1). A cardinal step from cell A in edge direction r is blocked by a solid
wall on A with edge r, or by one on the neighbour with the opposite edge. A
diagonal step is checked as the two cardinal steps around it. Pieces 10, 13,
14, 17–19, 22, 25, 26 and 29–32 are window/opening pieces with their own
rules; the cave has none of them. The tile under a wall stays walkable.

Rendering is a separate question. The normal game view draws walls through
the general object pass; the dedicated wall-geometry routine is used only for
the editor's top-down view (FR-SCENE-007). Wall art is located through a
structure table and a wall-name table in resource2, keyed by wall number,
piece, damage and variation; this research does not resolve those names.

### T8 — the cave's normal-game objects

At module load the game writes, for every map folder, one game-generated file
that holds the module GUID followed by that map's loose mobile objects. Its
name is the catalogue name with each ASCII letter shifted by 13 using the
source's short wrap, then reversed; the installed file system keeps it in
lower case. For the cave this file frames to the same 39 objects as the
module's loose cave mobiles (4 containers, 15 generic items, 4 food, 6
weapons, 2 ammo, 1 gold, 7 NPCs). The earlier status "editor_loose_observed,
game aggregate not verified" is now **verified at T0**; the loader can use the
game file exactly as it uses the exterior one.

## Project policy for the implementation

The following steps are separate PRs under ARC-WORLD-009, in this order:

1. **Cave loader** (Data). A second pinned area profile for catalogue 5078:
   one sector, 4,096 class-0 terrain cells, 125 walls, 13 scenery, the 39
   aggregate objects, 4 lights (data only, no lighting) and no roofs,
   portals or jump points. It publishes both endpoints as data.
2. **Walls** (Data + viewer). Resolve wall art (number 11 only) and draw the
   walls in the area view; add edge blocking (T7) to the step model for the
   cave. Corners where two walls share a tile are drawn and checked
   independently.
3. **Transition** (Simulation + viewer). One active map at a time. A use of
   a trigger object is accepted when the actor is within Chebyshev distance 2
   of it and the one-step check towards it passes (T3). The request is queued
   and processed once at the next simulation tick; a second request while one
   is pending is refused, and repeated frames never dispatch twice
   (FR-WORLD-008). Processing unloads the source map, opens the destination,
   places the actor exactly on the authored cell and centres the camera on it
   (FR-WORLD-009/011). Followers, inventory transfer, fade and sound are out
   of scope.
4. **Return with state**. The way back is only the authored reverse record
   1532; if it is absent, the exit is reported unavailable, never inferred
   (FR-WORLD-012). Restoring the exterior's changed state on return uses the
   session-state owner from ARC-WORLD-008 and waits for it to merge.

Out of scope: cave NPC behaviour and combat, the story tile scripts (T9),
cave lighting and sound, and the jump-point path.

## Reproduction

Use the pinned SDK from the [toolchain](../workflow/toolchain.md). Output is
private; keep it in ignored task-owned evidence, never in Git.

```sh
"$DOTNET" build tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --no-restore
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --cave-transition-self-test
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --cave-transition \
  --prototype-root "$GAME/data/proto" --mobile "$AREA_MOBILE" --cave-mobile "$CAVE_MOBILE" \
  --module "$GAME/modules/Arcanum.dat" --resource1 "$GAME/arcanum1.dat" --resource2 "$GAME/arcanum2.dat" \
  --resource3 "$GAME/arcanum3.dat" --resource4 "$GAME/arcanum4.dat" --patch0 "$GAME/modules/Arcanum.PATCH0" \
  > "$PRIVATE_CAVE_REPORT"
```

`$AREA_MOBILE` is the `areaMobile` role of the [launcher](../workflow/local-development.md#project-launcher);
`$CAVE_MOBILE` is the cave aggregate next to it (6,698 bytes, SHA-256
`aa29827de32c5cb6bf7aa03ea049f9f01d7698b003097172cd26ab80c9658318`). Every
archive and both aggregates are fingerprinted before and after the run and
must match the pinned identities; the probe fails closed with a coded reason.

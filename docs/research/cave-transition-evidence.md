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
  installation at T0 (new game, nothing executed; as defined in the
  [passability closure](area-passability-closure.md)). The probe prints only
  numbers, booleans and SHA-256 digests; no original text, names or payload
  bytes are recorded here.
- **Project policy**: a choice this project makes where the source leaves a
  runtime or product decision open.

`tools/research/Arcanum.CrashSiteProbe/CaveTransitionProbe.cs` was written
independently from the rules stated here and does not translate CE code. It
decodes, it does not execute: no script VM, movement or map lifecycle runs.
Original executable parity remains **NOT_VERIFIED**.

Directions: x−1 is called west, x+1 east, y−1 north and y+1 south. These are
cell-grid words only, not screen directions.

## Answer

The cave is a **separate map** reached by a **use action** on a visible
scenery object, not by stepping on a cell. There are exactly two directed,
literal endpoints, and the way back is an explicit second record, not a
return stack:

| Direction | Trigger object | Script | Destination (catalogue / runtime map, cell) |
| --- | --- | ---: | --- |
| Exterior → cave | sector 86570436011 index 7, prototype 4047, cell (92910,82598) | 2793 | 5078 / 79, (2087,2018) |
| Cave → exterior | cave sector 2080374816 index 99, prototype 4047, cell (2093,2019) | 1532 | 5000 / 1, (92913,82598) |

Both trigger cells and both arrival cells are terrain-passable, and each lies
inside the single sector that its map has to provide. The return lands three
cells east of the exterior trigger, not on the trigger itself.

## Findings

| ID | Claim | Source fact | Local observation |
| --- | --- | --- | --- |
| T1 | Script file and line format | [script.c 3447, 3524–3555](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L3524-L3555), [script.h 299–325](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.h#L299-L325), [script_name.c 38–82](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script_name.c#L38-L82), [gamelib.c 1826–1895](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L1826-L1895) | Both scripts are 328 bytes: a 64-byte header and two 132-byte lines; file flags 0. Each occurs once across the six pinned archives (resource2 only); no loose script, catalogue or tile-name file exists in the data or module folder. |
| T2 | What the two lines do | [script.h 46–81, 95–96, 151–196](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.h#L151-L196), [script.c 586–592, 1507–1510, 1875–1889, 2992–3003, 3217–3230](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1875-L1889) | Line 0: always-true condition → teleport of the **triggerer** to three literal numbers. Line 1: always-true → return, run default (2793) / return, skip default (1532). False branches do nothing. |
| T3 | What triggers the script | [script.h 7–44](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.h#L7-L44), [intgame.c 3444–3446](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/intgame.c#L3444-L3446), [anim.c 1240–1256, 4503–4540, 4767–4795, 6500–6531, 6534–6630](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L6534-L6630), [location.c 535–556](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/location.c#L535-L556), [object.c 3866–3906](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L3866-L3906), [trap.c 114–147](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/trap.c#L114-L147), [obj_flags.h 11–22](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj_flags.h#L11-L22), [tig art.h 76–100](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/tig/include/tig/art.h#L76-L100) | Each trigger is scenery whose only attachment is slot 1 (use) and whose art is of the teleport-facade scenery type (20), not a bed (22). It is drawn and clickable: no off, don't-draw, invisible or click-through flag. Both trigger cells are terrain-passable. |
| T4 | Teleport lifecycle | [teleport.c 95–139, 164–271, 369–445](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L164-L271), [map.c 792–872](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L792-L872) | The script passes flags 0: no fade, sound, movie or time advance. Source and destination maps differ, so both directions take the cross-map branch. |
| T5 | Map identity | [map.c 2003–2060](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L2003-L2060), [script.c 1880–1883](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1875-L1889), [map.c 801–836](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L792-L847) | The catalogue has 81 unbroken rows from 5000. Row 5000: start (92958,82592), start-map flag. Row 5078: start (2087,2018), area 2. The cave's catalogue start equals the teleport arrival. |
| T6 | Arrival cells | [teleport.c 412–441](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L412-L441), F1 of [passability closure](area-passability-closure.md#f1--tile-name-flags) | Cave (2087,2018): mask clear, class-0 tile not blocking, one wall on the tile (edge 7), no aggregate object. Exterior (92913,82598): mask clear, facade tile not blocking, no sector or aggregate object. |
| T7 | Walls are edge blockers | [object.c 2962–3200](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L2962-L3200), [location.c 369–416](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/location.c#L369-L416), [tig art.c 18, 44–51, 1620–1680](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/tig/src/art.c#L1620-L1680) | 125 walls, one prototype (1020), one wall number (11); edges 1/3/5/7 = 33/31/28/33; 0 window/opening pieces; 8 tiles carry two walls (corners). |
| T8 | Normal-game cave counterpart | [gamelib.c 690–700](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L690-L700), [map.c 1056–1135, 1400–1460, 1668–1690](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L1668-L1690) | The cave aggregate (6,698 bytes) frames to exactly 39 records, a semantic bijection with the 39 loose cave mobiles (22 byte-identical). Both aggregate selectors match the catalogue names. |
| T9 | Nothing else transfers | as T2; [object.c 3877–3881](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L3866-L3906) | Six other scripts are attached in the trigger sector or the cave (objects, tiles, aggregate), plus one literal call target already among them. None has a teleport or fade-and-teleport action on either branch; one makes one computed object-focus call that is not followed. |

### T2 — the two lines

The probe admits exactly one shape and refuses anything else (a computed
operand, another focus object, non-zero file flags, an acting false branch, a
non-trivial condition or an unknown action). The teleport action carries four
operands: a focus object and three numbers. Focus kind 0 is the triggerer, so
the object that used the trigger travels. Operands 1..3 each have value kind 3,
which the value reader returns as the literal itself. The map number is a
catalogue number; the source subtracts 4999 to get the runtime map (5078 → 79,
5000 → 1), which equals the row's position because the catalogue is read as an
unbroken run from 5000 (T5). Operand slots past the fourth, and the focus
slot's value, hold editor memory and mean nothing. After the teleport is
queued, line 1 returns: 2793 lets the default use behaviour run, 1532 skips
it. For this scenery the default use path has nothing more to do after the
script, so neither return has a visible effect.

### T3 — the trigger is a use action

Outside combat a left click on scenery queues the use-object goal for the
player. The goal sets a near range of 2 for scenery (1 for containers and
critters) and walks the player until the Chebyshev distance to the object is
at most that range. The use then fails if the object's tile is
terrain-blocking, or if one step from the player towards the object is
blocked with the object itself temporarily ignored. For scenery whose art is
of the bed type the use opens the sleep interface instead of the script.
Otherwise the object's slot-1 script runs with the player as triggerer. A use
first tries to spot a trap only when the slot-1 script's file flags mark a
trap; both scripts have file flags 0 (T1), so there is no trap step.

This is not the jump-point path (cell entry through `map_process_jumppoint`):
both maps' jump lists are empty (inventory). FR-WORLD-008's cell-entry
trigger therefore does not apply to this cave; its "separate explicit trigger
policy" case does.

### T4 — what the teleport does

The script only queues the request; the game loop processes it on its next
ping. While a player teleport is pending, every further teleport request is
refused. Processing, in order: collects followers (none at T0) and marks
every transferred object; because the maps differ, appends the transferred
objects, placed on the arrival cell, to the destination map's save-folder
object file and marks the source copies destroyed and extinct; then opens the
destination map, which first flushes the current map's changes to the save
folder and then loads the destination's base files plus its saved
differences; finally sets the view origin to the arrival cell and pre-loads
the 3×3 sectors around it. The arrival is written without any passability
check. Leaving the cave therefore restores whatever the exterior's saved
differences hold; that persistence is FR-WORLD-013 and belongs to the session
state component (ARC-WORLD-008, [contract](../world/game-session-runtime.md)).

### T6 — arrival cells

The cave arrival tile holds wall index 90 (piece 1, edge 7). By T7 that wall
blocks only the step across the tile's north edge; the west, south and east
edges have no wall on either side. The player lands on the inner side of the
corridor's north wall, six cells west and one north of the exit object. The
exterior arrival is a facade tile whose bit 0 is set (not blocking) with no
object on it, three cells east of the entrance object; it lies in core sector
(1451,1290). Both cells were also checked against every aggregate object
location of their map.

### T7 — walls

A wall's art id carries variation (bits 8–9), rotation (bits 11–13), piece
(bits 14–19), wall number (bits 20–27) and art class 1 (bits 28–31). Even
rotations are rounded up to the next odd one, so every wall sits on one of the
four tile edges 1, 3, 5, 7 (west, south, east, north). A cardinal step from
cell A in edge direction r is blocked by a solid wall on A with edge r, or by
one on the neighbour with the opposite edge. A diagonal step is checked along
both L-shaped paths around it: the counter-clockwise leg from A then the
clockwise leg from that neighbour, and the clockwise leg from A then the
counter-clockwise leg from that neighbour. That is four cardinal legs, and any
blocked leg blocks the step. Pieces 10, 13, 14, 17–19, 22, 25, 26 and 29–32
are window/opening pieces with their own rules; the cave has none of them.
The tile under a wall stays walkable.

Rendering is a separate question. The normal game view draws walls through
the general object pass; the dedicated wall routine is called only from the
editor's draw path ([gamelib.c 1700–1745](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L1700-L1745)).
Wall art is located through a structure table and a wall-name table in
resource2 ([a_name.c 1402–1490, 1494–1660](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/a_name.c#L1494-L1660)),
keyed by wall number, rotation half, piece, damage and variation. This
research does not resolve those names; the walls step does.

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

### T9 — other scripts

The probe collects every script attached to the trigger sector's objects and
tiles and to the cave's objects, tiles and aggregate objects, follows one hop
of literal calls, and decodes each one's action types on both branches. It
refuses a teleport or fade-and-teleport anywhere among them and reports call
targets. The exterior trigger sector has no other attachment. In the cave: the
other scripted scenery only returns skip-default; the tile attachments call a
14-line script gated on global flags and a 2-line script that sets one flag;
the aggregate's NPCs and one item carry three more. These are story and NPC
behaviour, out of scope here. The rest of the exterior map was covered by the
earlier [topology search](crash-site-area-inventory.md#bounded-cave-endpoints-and-composition),
which found no other literal teleport.

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
   of it, the object's tile is not terrain-blocking and the one-step check
   towards it passes (T3). The request is queued and processed once at the
   next simulation tick; any further request while one is pending is refused,
   and repeated frames never dispatch twice (FR-WORLD-008). Processing
   unloads the source map, opens the destination, places the actor exactly on
   the authored cell and centres the camera on it (FR-WORLD-009/011).
   Followers, inventory transfer, fade and sound are out of scope.
4. **Return with state**. The way back is only the authored reverse record
   1532; if it is absent, the exit is reported unavailable, never inferred
   (FR-WORLD-012). Restoring the exterior's changed state on return uses the
   session state owner from ARC-WORLD-008.

Out of scope: cave NPC behaviour and combat, the story tile scripts (T9),
cave lighting and sound, and the jump-point path.

## Reproduction

Use the pinned SDK from the [toolchain](../workflow/toolchain.md). Output is
private; keep it in ignored task-owned evidence, never in Git.

```sh
"$DOTNET" build tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --no-restore
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --cave-transition-self-test
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --cave-transition \
  --game-root "$GAME" --prototype-root "$GAME/data/proto" --mobile "$AREA_MOBILE" --cave-mobile "$CAVE_MOBILE" \
  --module "$GAME/modules/Arcanum.dat" --resource1 "$GAME/arcanum1.dat" --resource2 "$GAME/arcanum2.dat" \
  --resource3 "$GAME/arcanum3.dat" --resource4 "$GAME/arcanum4.dat" --patch0 "$GAME/modules/Arcanum.PATCH0" \
  > "$PRIVATE_CAVE_REPORT"
```

`$AREA_MOBILE` is the `areaMobile` role of the [launcher](../workflow/local-development.md#project-launcher);
`$CAVE_MOBILE` is the cave aggregate next to it (6,698 bytes, SHA-256
`aa29827de32c5cb6bf7aa03ea049f9f01d7698b003097172cd26ab80c9658318`). The six
archives and both aggregates must match pinned identities. Every prototype
file the probe reads is fingerprinted when read and again at the end (34
files at T0); the report gives their count and one combined digest. The game
root is only used to look for loose overrides in its data and module folders.
Any mismatch or change fails closed with a coded reason.

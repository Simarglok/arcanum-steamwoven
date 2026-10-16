# First building: doors, windows and roofs

## Status, scope and evidence discipline

ARC-RES-029 picks the first building the player can reach from the Crash Site and
describes how its walls, doors, windows and roof are stored and how the pinned
source makes them behave. It answers FR-SCENE-008, 010, 011, 014 and 015 for that
one building. It is **research, not a runtime**: no `src/**`, viewer,
`Arcanum.Session` or launcher file changes. It builds on the cave wall facts of
[ARC-WORLD-009](cave-transition-evidence.md) and the wall fade port of ARC-RENDER-011
([viewer note](../rendering/terrain-viewer.md#actor-anchor-and-wall-fade-arc-render-011)).

Categories, as in the [item research](crash-site-item-inventory.md):

- **Source (S):** read branches of `alexbatalov/arcanum-ce` at
  `a7ff41b300ef712f0e7d088183a3d08957110cdd` (Sustainable Use License 1.0, not MIT),
  not original-executable parity. No CE code or table is copied; the probe restates
  rules in its own form (for example roof pieces as edge bands, below).
- **Data (D):** the six pinned archives, the normal-game exterior aggregate and the
  prototype directory, read by the probe below. T0 is the serialized state before any
  tick, script or player action.
- **Proposal (P):** project recommendations for a later implementation.
- **Unknown (U):** a named gap with its next check.

No original text, pixels or private paths are published. Objects are identified by
world tile, prototype and script numbers. The town name in the selection section
was checked locally in the module's town map catalogue and is not quoted from it.

## Inputs and reproduction

Inputs are the six archives of the [cave research](cave-transition-evidence.md)
(module, resource1..4, PATCH0), the exterior aggregate (`a548f611…de6a5`,
1,823,280 bytes) and the prototype directory. Every archive and the aggregate are
fingerprinted before and after the run, as are the 17 prototype files read
(aggregate `317a2e02…dcaba7`). Mapping and ART files must resolve in exactly one of
the six archives, no archive but the module may carry the exterior map folder; loose
`art/portal`, `art/roof` and attached-script overrides in
`data/` and the module folder must be absent.

```sh
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --building-self-test
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --building-research \
  --game-root "$GAME" --prototype-root "$GAME/data/proto" --mobile "$GAME/modules/Arcanum/maps/<exterior aggregate>" \
  --module "$GAME/modules/Arcanum.dat" --resource1 "$GAME/arcanum1.dat" --resource2 "$GAME/arcanum2.dat" \
  --resource3 "$GAME/arcanum3.dat" --resource4 "$GAME/arcanum4.dat" --patch0 "$GAME/modules/Arcanum.PATCH0" \
  > "$PRIVATE/building.json"
```

The report holds numbers, booleans and SHA-256 digests only; two runs produced the
same report digest `15e5ef24…09e0881d`. The self-test covers the art field decoders,
open frames, roof slots, all 13 band masks, coverage, the fill flood, door
admission with the day boundaries, indoor tiles and edges on synthetic values.

| Resource | Archive | Bytes | SHA-256 |
| --- | --- | ---: | --- |
| Selected sector, module ordinal 492 | module | 58,854 decoded | `afe98530…829038` |
| `art/portal/portal.mes` | resource2 | — | `e8bb531d…f73d0b` |
| door ART (portal number 55), ordinal 2142 | resource2 | 15,808 | `a1ccf42b…49cfe2` |
| window ARTs (numbers 109, 111, 114), ordinals 2150/2154/2158 | resource2 | 2,855 / 3,134 / 3,150 | `56a79d3d…a5b79` / `ba14faa9…138fb` / `fb8eea2b…57fc43` |
| `art/roof/roofname.mes` (16 contiguous entries) | resource2 | — | `ac8387c9…e5ade2` |
| roof ART number 7, ordinal 2748 | resource2 | 221,251 | `59dd285c…34f128b` |
| `art/roof/roofshade.mes` | resource2 | — | `c7b968be…2e51c0` |
| exterior `map.jmp` | module | 4 | `df3f6198…81119` |

## Selection: which building comes first

**Policy (P):** survey all 621 sectors of the start map (catalogue 5000) and take the
nearest sector, by Chebyshev distance in sectors from the start sector
`(1452,1290)`, that holds at least one portal and one non-empty roof slot. The probe
refuses a tie or a changed result.

**Data (D):** across the map, 161 sectors hold walls, 102 portals and 132 roofs
(28,064 walls, 1,588 portals, 6,483 roof slots). The exterior aggregate holds no
walls or portals; they all live in sector files. The nearest results are:

| Distance | Sector | Town map | Walls / portals / roof slots | What it is |
| ---: | --- | ---: | --- | --- |
| 12 | `(1451,1302)`, id 87375742379 | 0 (none) | 36 / 5 / 9 | **Selected:** one isolated house |
| 33 | `(1450,1323)` | 0 | 20 / 1 / 4 | a second lone building |
| 41..45 | eight sectors in `x 1407..1411, y 1313..1315` (rows in the report) | 3 | 728 / 91 / 181 in total | the first town, Shrouded Hills |

The house is self-contained: none of its eight neighbour sectors exists in the map
directory. Its nearest interior tile is 746 tiles (Chebyshev) from the start point
`(92958,82592)`, almost all of it along +y (screen lower right) from the Crash Site core. That the player passes it on the way
to the town is **not** verified (U1).

## The house

**Data (D):** the sector holds 36 walls, 5 portals, 395 scenery objects, no lights
and a non-empty roof list. The interior is one room of 9×9 = 81 indoor tiles,
`x 92890..92898, y 83338..83346`, found as a four-neighbour component of indoor
tiles that does not cross a wall or portal edge. Indoor means tile type 0, read from
bit 8 of a tile id or bit 25 of a facade id, the same type CE uses for roof coverage
and for indoor-to-outdoor door checks.

Inside are one NPC (type 16, prototype 17156; dialog script 2786, dying script 2788,
heartbeat script 2787) at `(92894,83341)` and one armor item (type 7, prototype 8114)
on the floor at `(92894,83339)`. Two outside scenery objects carry the examine
script 2933. All four scripts exist once (resource2) and contain no teleport action
(one hop of literal calls checked; none present).

Directions below follow the CE rotation table (1 = −x, 3 = +y, 5 = +x, 7 = −y). On
screen, +x points to the lower left and +y to the lower right, so the door wall
(+y side) faces the viewer's lower right. Roof piece names follow this view: west
pieces lie at the building's largest x, north pieces at its smallest y.

## Walls and openings

**Data (D):** all 36 walls use prototype 1021 and wall ART number 7, wall flags 0,
no damage, no spell flags, no scripts. Each sits on a perimeter interior tile and
faces outward: nine per side, corners carry two. Edge rotations are only odd
(9 × 1, 3, 5, 7), as in the cave.

| Side | Openings |
| --- | --- |
| +x (edge 5) | windows (piece 10) at y 83340 and 83344 |
| +y (edge 3) | door frame pieces 21/22/23 at x 92891..92893 (only 22 is an opening piece), windows (pieces 13, 14) at x 92895, 92896 |
| −x, −y | solid; pieces 34/35/36 occur on both and are solid for traversal |

**Source (S):** a wall or portal on tile T with ART rotation r blocks the edge in
direction `r|1` from T, and the neighbour's objects facing back block the same edge
([object traversal, current tile](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L3029-L3112),
[destination tile](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L3152-L3256)).
Wall pieces 10, 13, 14, 17, 18, 19 are windows: on a cardinal step without the
window-blocks flag they mark a window instead of blocking; a diagonal step is
blocked. Pieces 22, 25, 26, 29..32 are doorway pieces and never block by themselves;
the portal on the same edge decides. Every other piece blocks unless the wall object itself
carries the pass-wall spell flag (none here). Six wall objects (the four window pieces and door frame pieces 22 and
23) also carry `NO_BLOCK | SEE_THROUGH | SHOOT_THROUGH`; the wall branch does not
read `NO_BLOCK`, so those flags matter only for sight and projectiles.

## Portals: doors and windows (FR-SCENE-008)

**Source (S):** a portal ART id is class 3 with number in bits 19..27, frame 14..18,
rotation 11..13, type bit 10 (0 window, 1 door), busted bit 9 and palette 4..5; the
low nibble is a flag field whose bit 0 mirrors the image
([portal id](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/tig/src/art.c#L2034-L2066),
[flags](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/tig/src/art.c#L2628-L2639)).
The file name is the first word of `portal.mes` entry `number` (`number + 1001` for a
window; loading cuts every entry at its first space); a busted id swaps one letter for `D`
([load](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/a_name.c#L1187-L1205),
[name](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/a_name.c#L1214-L1223),
[entry](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/a_name.c#L1330-L1342)).
A portal is open when its frame is not 0
([`portal_is_open`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/portal.c#L93-L96)).
Opening a door animates frames 1..3 when its cardinal rotation is 2 or 4, and 4..6
when it is 0 or 6, and sets `SEE_THROUGH | SHOOT_THROUGH | NO_BLOCK`; closing runs
back and clears them on frame 0. A window has only frames 0 and 1. An ART with one
frame cannot open at all
([open](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/portal.c#L127-L205),
[close](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/portal.c#L215-L292)).
Portal fields follow the 36 common slots: flags (slot 36), lock difficulty (37),
key id (38), notify NPC (39); flag bits are locked 0x1, jammed 0x2, magically held
0x4, never locked 0x8, always locked 0x10, locked by day 0x20, locked by night 0x40,
busted 0x80, sticky 0x100
([field order](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj.h#L11-L60),
[`obj_flags.h`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj_flags.h#L94-L107)).

**Data (D):**

| Portal | Tile, edge | ART | State and fields | Wall on the same edge |
| --- | --- | --- | --- | --- |
| Door, prototype 2028 | `(92892,83346)`, 3 (to `(92892,83347)` outdoor) | number 55, rotation 3, mirrored, 7 frames in one group, frame 0 = closed | flags 0x41 (locked + locked by night), difficulty 25, key 0, no scripts, `PROVIDES_COVER` only | doorway piece 22 |
| Window ×2, prototype 2036 | `(92898,83340)`, `(92898,83344)`, 5 | number 109, not mirrored, 1 frame | flags 0x2 (jammed), key 0, `SEE_THROUGH` | window piece 10 |
| Window, prototype 2036 | `(92895,83346)`, 3 | number 111, mirrored, 1 frame | jammed, `SEE_THROUGH` | window piece 13 |
| Window, prototype 2036 | `(92896,83346)`, 3 | number 114, mirrored, 1 frame | jammed, `SEE_THROUGH` | window piece 14 |

All five store their current ART directly, start closed and carry notify NPC 6402
(meaning U3). Every portal ART has one physical image group. The three edge-3 portals
(door, windows 111 and 114) set flag bit 0 with palette 1; the edge-5 windows (109)
do not. Bit 0 mirrors the image at blit time, and the frame data then moves the hot
spot: a mirrored wall or portal uses `width − hot_x − 2`, and a wall or portal with
rotation below 2 or above 5 is shifted by (−40, +20) first
([hot spot](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/tig/src/art.c#L1236-L1254),
[blit](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/tig/src/art.c#L3221-L3228)).
The door's stored closed frame is 35×94 with hot spot (33,78), so mirrored it is drawn
with hot spot (0,78); rotation 3 needs no shift. Door rotation 3 is cardinal 2, so it
opens through frames 1..3. The windows have one frame, so they can never open; they
only matter for window jumping (FR-SCENE-016, `later`).

## Door admission (FR-SCENE-014)

**Source (S):** a closed, unbusted portal on the edge blocks a step unless the walker
is a critter whose open attempt succeeds
([`ai_attempt_open_portal`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3745-L3790)).
The attempt order is: busted passes; the unlock script attachment (SAP 3) may refuse;
jammed and magically held refuse; unless always locked, leaving a building passes,
and so does any door for an NPC with no player leader; otherwise a locked portal
needs its key in the party. "Locked" ignores never-locked
portals, the by-day bit outside hours 7..21 and the by-night bit inside them
([`object_locked_get`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L4024-L4057)).
"Leaving" means the portal's own tile is indoor and the tile across its edge is not
([`ai_is_indoor_to_outdoor_transition`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3793-L3827));
for a walker coming from across the edge the source swaps the two tiles through a
self-assignment, so that side always compares one tile with itself and never counts
as leaving (U4). The leaving check steps by the raw ART rotation, not `r|1`; for this
door both are 3. When a walk reaches a closed door the move goal pushes an open-door
goal that animates it open
([move node 8](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L418),
[open-door goals](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L565-L611));
using a portal toggles it
([use node 6](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L1253)).
An unlock relocks the portal after one game hour and closes it
([lock timer](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L4060-L4115)).

**Data + restated rule (D/S):** the probe evaluates the restated rule for the player
character, without a key and with no unlock script attached:

| Door | Entering by day | Entering by night | Leaving, day or night |
| --- | --- | --- | --- |
| `(92892,83346)` | opens | **locked** (key 0 matches no key; difficulty 25 for [lock picking](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/skill.c#L1969-L2005), or [bust](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/portal.c#L19-L50)) | opens (leaving a building) |

The windows refuse as jammed from both sides. A new game starts at hour 15
([clock policy](../world/game-clock-policy.md)), inside the unlocked 7..21 window, so
the door is unlocked (still closed, opening when walked into) if the player reaches
it before 22:00 on the first day; the travel
time to the house is not modelled (U2).

## Roofs (FR-SCENE-010)

**Source (S):** a sector stores 256 roof ART ids after its tiles when its `empty`
word is 0 ([sector roof list](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_roof_list.c#L145-L165)).
Slot `x/4 + 16·(y/4)` of the sector-local tile is one 4×4 block; invalid id
`0xFFFFFFFF` means no roof ([slot](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/roof.c#L383-L405)).
A roof id is class 10 with number in bits 19..27, frame 14..18, fill bit 13, fade
bit 12, palette 4..5 and flag bit 0; pieces 0..8 are frames 0..8, pieces 9..12 are
frames 0..3 with bit 0 set (and palette 1)
([roof id](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/tig/src/art.c#L2344-L2470)).
The thirteen pieces are north-west outside, west, north, north-west inside,
south-west outside, south-west inside, north-east inside, north-east outside,
centre, south-east outside, south, east and south-east inside. The file is
`art/roof/<entry>.art`, where the roof number indexes `roofname.mes` entries in
order ([names](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/a_name.c#L1906-L1964)).
`roof_draw` walks visible blocks, skips invalid and filled ones, anchors each at the
block's tile `(x0+2, y0+2)` shifted by (−120, −200) screen pixels minus the frame hot
spot (a mirrored roof frame uses hot spot x 0, see the portal hot spot link above), and with the fade bit set blends corner alphas per piece from the three
`roofshade.mes` values (full opacity, partial opacity, full transparency)
([init](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/roof.c#L144-L188),
[draw](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/roof.c#L216-L374),
[rect](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/roof.c#L727-L742)).

**Data (D):** nine slots form one 3×3 roof over the 12×12 tiles
`x 92888..92899, y 83336..83347`, all roof number 7, fill and fade clear at T0:

| | x 92888 (east edge) | x 92892 | x 92896 (west edge) |
| --- | --- | --- | --- |
| y 83336 (north) | north-east outside, frame 7 | north, frame 2 | north-west outside, frame 0 |
| y 83340 | east (frame 2 mirrored) | centre, frame 8 | west, frame 1 |
| y 83344 (south) | south-east outside (frame 0 mirrored) | south (frame 1 mirrored) | south-west outside, frame 4 |

The roof ART has 9 frames in one group; frame sizes run from 224×143 to 318×160,
with stored hot spots (−95..−1, 0); the three mirrored pieces are drawn with hot spot
x 0. `roofshade.mes` holds 255, 200 and 80. The roof
reaches two tiles beyond the walls on the −x and −y sides and one tile on the +x
and +y sides.

## Roof coverage, hide and fade (FR-SCENE-011)

The probe restates each piece as **edge bands** inside its 4×4 block (row = y mod 4,
column = x mod 4): a west edge keeps columns 0..2, east 2..3, north rows 2..3, south
rows 0..2; outside corners are the intersection of their two bands, inside corners
the union, the centre all 16 tiles. The self-test pins all 13 pieces as literal
masks written from this description; the independent review of this research
checked by hand that they equal the source's per-piece masks and its hide conditions
for every piece.

- **Coverage (S):** an indoor tile is covered when the block holding `(x+3, y+3)`
  exists, is not filled and its band holds `((y+3) mod 4, (x+3) mod 4)`
  ([`roof_is_covered_loc`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/roof.c#L668-L709)).
  Covered tiles and the objects on them are not drawn. Tiles ask without counting a
  faded roof, so they are drawn again under a faded roof; objects count a faded roof
  as covering and stay hidden
  ([tiles](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tile.c#L699),
  [objects](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L552-L571)),
  and a click on a drawn roof pixel picks no object
  ([hit test](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/roof.c#L465-L501),
  [caller](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L1824)).
- **Coverage (D):** with the roof intact 36 of 81 indoor tiles are covered (the 6×6
  block `x ≤ 92895, y ≤ 83343`), including the tiles of the NPC and the armor; the
  other 45 are drawn. Whether the drawn roof image hides them on screen is not
  checked (U5).
- **Hide, "fill" (S):** each time the local player character changes tile, the
  block under that tile (no +3 offset) is set filled when its band holds
  `(y mod 4, x mod 4)` and unfilled otherwise; a change floods to the four
  neighbouring blocks, except that a flood arriving at an edge piece whose outer side
  faces it stops, which keeps the flood inside one roof
  ([`roof_recalc`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/roof.c#L504-L603),
  [flood](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/roof.c#L745-L804),
  [caller](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L5241),
  [deferred caller](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/wallcheck.c#L700-L709)).
  A teleport unfills the roof at the arrival tile
  ([teleport](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L455-L461)).
- **Hide (D):** the interior lines up exactly with the bands: standing on any of the
  81 interior tiles fills all nine blocks (one outcome for every tile), so the whole
  roof disappears. None of the 63 outside tiles under the roof hides it, and from a fully hidden roof
  each of them restores all nine blocks, so the first step out of the door (tile
  `(92892,83347)`, row 3 outside the south band) shows the roof again.
- **Fade (S):** wall fading (ARC-RENDER-011) also fades the roof block at each faded
  wall's own tile, reference-counted per block, and clears it when the wall is
  released
  ([fade on](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/wallcheck.c#L999-L1015),
  [fade off](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/wallcheck.c#L1109-L1125)).
  A faded but unfilled roof still counts as covering when objects are drawn; a wall
  with a transparent side under a faded roof is skipped unless its rotation check
  passes. The roof list's DIF/save bodies are empty, so fill and fade are not
  saved through DIF; whether anything recomputes them on load, before the player's next tile
  change, is not shown by the cited source (U7)
  ([stubs](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_roof_list.c#L87-L123)).

## Same-map interior (FR-SCENE-015)

**Data (D):** the interior is part of the same exterior map and sector; it is not a
separate map. The map's jump list is empty (count 0), the sector has no tile
scripts and sector script 0, and none of the four attached scripts teleports. The
only way in is the door edge `(92892,83346)↔(92892,83347)`; walls and jammed windows
close every other edge. **Source (S):** entering is an ordinary step across that
edge once the door admits the walker (above); the roof then hides by the fill rule.
No map change happens at the door.

## Answers per requirement

| Requirement | Answer for this building | Status |
| --- | --- | --- |
| FR-SCENE-008 | One door and four windows are type-1 sector objects with their own ART (portal number, type, frame, rotation, mirror flag) and fields; state is the ART frame; the door has 7 frames, windows 1 | source + data facts; no portal draw path |
| FR-SCENE-010 | One 3×3 roof of roof number 7 with all nine piece kinds of a plain rectangle, stored as 9 of 256 slots; piece = frame plus mirror; fill/fade clear at T0 | source + data facts; no roof model or renderer |
| FR-SCENE-011 | Coverage by +3 offset and bands (36/81 tiles); whole-roof hide while inside; restore on the first outside step; fade tied to wall fade | source rule + data prediction; no runtime |
| FR-SCENE-014 | Closed door blocks its edge; walking in opens it by day, is locked by night, always opens when leaving; windows jammed | restated rule + data; no door action |
| FR-SCENE-015 | Interior on the same map and sector; no jump point or teleport; entry only through the door edge | data facts |

## Proposed implementation slices (P)

1. **Data:** a bounded first-building profile, the selected sector only, exposing
   walls, portals (art fields, flags, lock, key) and roof slots as typed records,
   validated against this probe's numbers. Godot-independent.
2. **Traversal:** extend the Area step model with portal edges and wall opening
   pieces (doorway pieces open, window pieces window), closed portals blocking,
   using the cave edge rule.
3. **Door state:** a door owner with open/close frames and the admission rule above
   (day/night lock, leaving rule, jammed, key), feeding the step model; key and
   lock picking stay out until an inventory owner exists.
4. **Roof:** roof blocks drawn from slots with piece frames and mirroring, the fill
   rule on player tile change with the one-roof flood, coverage for tiles and
   objects, then roof fade tied to the existing wall fade.

Each slice needs its own synthetic fixtures; none may reuse real record bytes.

## Unknowns

**Original check (ARC-RES-048):** U4 is answered: the original swaps the portal and far tiles (no self-assignment), so the door-admission rule below that a walker from across never counts as leaving holds only for CE; the house door's result does not change. The verdicts in the [verification ledger](original-verification-ledger.md#world-movement-and-data-arc-res-047-arc-res-029-arc-res-034-arc-data-021-clock) supersede the CE readings below where they differ.

- **U1** Whether the house lies on or near the route a new player takes towards the
  town; next check: a route survey from the start point to townmap 3.
- **U2** The arrival hour at the house: the start hour is 15, but travel time is not
  modelled; next check: walking time once world travel exists.
- **U3** What notify NPC 6402 on all five portals means; next check: the CE readers
  of `OBJ_F_PORTAL_NOTIFY_NPC`.
- **U4** Whether the original executable has the same self-assignment in the
  leaving check, or swaps the tiles; next check: original-runtime observation. It
  cannot change this door: its own tile is indoor and the tile across is outdoor.
- **U5** How roof, walls and the 45 uncovered indoor tiles look together on screen,
  and the roof fade alphas in practice; next check: a rendered comparison once a roof
  renderer exists.
- **U6** The NPC inside (prototype 17156, dialog 2786): who it is and whether it opens
  the door or reacts to entry; next check: its dialog and scripts.
- **U7** Whether roof fill and fade are recomputed after loading a save; next check:
  the CE load path for the player's location.

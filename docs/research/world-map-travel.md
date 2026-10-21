# World-map travel: areas, routes, time and arrival (ARC-RES-068)

Status: **Research** (whole-world plan M4, step R7; the travel slice W3 builds on it, rules since
ARC-WORLD-056). The
behaviour below is read in the retail 1.0.7.4 `Arcanum.exe` (SHA-256 `9bd80511…8d52`).
Two readers checked it, and the rows are in
[ledger WT1–WT13](original-verification-ledger.md#world-map-travel-arc-res-068). Data facts are
from the owner's installed module (GOG 1.0.7.4). The interface itself (buttons, zoom, notes,
town maps) and random encounters are summarized only as far as travel needs them. Their
functions keep level F0 until their own slices.

## What the player sees

The world map shows the whole start map (`Arcanum1-024-fixed`, runtime map 1). One map pixel
is one sector (64 × 64 tiles). The player:

1. Marks up to 30 waypoints, which become one route of at most 5,000 sector steps.
2. Starts the journey. The PC then moves one sector per 62 real milliseconds, and the game
   clock advances one hour per sector.
3. Discovers areas passed within their radius on the way.
4. Lands on the start map at the route's end, snapped to an area's exact location when one
   is near. Followers come along.

Random encounters interrupt a journey through the game clock's event queue, not through the
travel step.

## Data

| File | Installed facts | Read by |
| --- | --- | --- |
| `mes/gamearea.mes` (module) | 82 entries, 0..81 without gaps. An entry is `x, y, name x-offset, name y-offset /Name /Description` with an optional `/Radius:<n>` (43 entries use it: −1, 1, 3, 5, 10, 15, 20, 25). Entry 0 is "A Place Unimportant", the unknown area. x and y are a tile location of the start map. | area module init (WT1) |
| `worldmap/worldmap.mes` (module) | Entry 50: `8, 8, SmallMapChunks, ZoomedName: Map_Zoomed, MapKeyedTo: 1`, an 8 × 8 grid of map tiles (the file's own note: 250 × 250 each, a 2000 × 2000 picture). The world map belongs to runtime map 1. Entries 501.. name the town maps. | world-map interface init (F0) |
| `rules/wmap_rnd.mes` | The random encounter tables (the module's patch0 copy differs, [content precedence](content-precedence.md)). | encounter module (F0, not part of W3) |

## Areas (WT1–WT4)

- **Init.** Each entry sets an area's location (the tile pair) and its name and description
  (split at `/`).
  - Its discovery radius is 320 tiles (5 sectors) by default, and `Radius:<n>` makes it
    n × 64 tiles. The searches below start at area 1, so area 0 is never discovered or snapped to.
  - A negative radius (the 16 `-1` areas, such as the Black Mountain Mines and the Wheel
    Clan) is never the first area in range when passing near (WT3).
- **Known.** Known areas are one bit per area for the local PC, set by discovery and never
  cleared by travel.
  - Setting the bit also records the area as the PC's "last discovered"; the travel step
    clears that record again at once (WT3).
  - It also calls the interface callback 0x460790(3, 1), which is not read (F0).
- **Discovery.** Each travel step discovers an area whose radius covers the PC's new location
  by Chebyshev tile distance (WT3). The first area in range is the candidate; a later one
  replaces it when it is nearer and within the *candidate's* radius. The result must then be
  within the radius loaded in the last comparison: its own radius, unless it became the
  candidate in that last comparison (the file's last area). A replacement outside its own
  radius therefore makes the search find nothing, even though the first candidate covered the
  location (A1 below).
  - The arrival snap (WT12) uses the same search with a radius of 64 tiles for an area of
    radius 0 or below.
  - Areas are numbered by their position in the file, not by entry number (the installed
    file has no gaps, so both agree).
- **Snap targets.** Up to 320 tiles away from a waypoint, the nearest *known* area takes the
  waypoint's place, at the area's location (WT4).

## Route (WT5–WT8)

- **Waypoints.** Waypoints are set only while the world map is walkable: the shown map is the
  map the world map is keyed to, and the PC's sector has no town map. There are at most 30. Each segment starts at the end of the previous one, or
  at the PC's location for the first. All segments share one direction buffer of 5,000 steps.
  A segment that cannot be routed, or that starts and ends in one sector, is refused with
  message 601 of `WorldMap.mes`; the waypoint is not added.
- **Coordinates.** Map pixel (px, py) is the tile (64 × (W − px) + 32, 64 × py + 32): the
  sector's centre, with x mirrored. W is the map's width in tiles / 64 when the world map
  opens on its keyed map, else 2000. Tile x maps back to pixel W − x/64, and tile y to y/64.
- **Steps.** The route is a list of the 8 directions between sector centres. From the
  current sector the next step points straight at the target (the sector direction 0x4CFCD0:
  the signs of the deltas, the minor axis dropped when 2 |minor| < |major|, computed from
  doubled deltas).
  That sector is taken when it passes:
  - a sector the scripts blocked (the blocked-sector list, 0x4D0EE0) never passes;
  - a sector with its own sector file passes;
  - otherwise a terrain-blocked sector (`/b` base or second type, or outside the map;
    [generated wilderness](generated-wilderness-evidence.md#7-what-b-blocks)) does not.
- **Detour.** When the straight step fails, the route looks along the straight line for the
  first sector that passes. If that sector is fewer than 8 sectors away on both axes, a search over a
  16 × 16 sector window joins the two:
  - each step costs 10, plus 1 when it turns from the step before;
  - the estimate is 10 × the Chebyshev distance;
  - a node whose (cost + estimate) / 10 exceeds the steps left in the 5,000 buffer is
    dropped.

  Otherwise, or when the search finds nothing, the segment fails, and so does a blocked
  destination. After a detour, one direction byte is skipped and never written (A2 below).

## Travel (WT9–WT13)

- **Start.** Entering the travel state calls 0x424070 with the PC at level 3 (the animation
  interrupt that companion orders also use, [OL5](original-verification-ledger.md#companion-orders-and-loyalty-arc-res-064))
  and then 0x457450 (demaintain, [magic](magic-core-evidence.md)); neither is read here (F0).
  It hides the PC and its followers and starts the travel music: every sound stops and
  `sound\music\arcanum.mp3` loops, with fade 25 ([SN30](audio-evidence.md#dialogue-voice-event-sounds-scripts-and-interface)). It then schedules the
  first WorldMap event: real time, 62 ms, kind 14, [event queue](game-event-queue.md#3-event-kinds-and-consumers-s).
- **Each event.**
  1. The PC's map position moves one direction step to the next sector centre. The segment's
     last step goes to the waypoint itself.
  2. The step discovers an area (WT3). A dead branch also tests the snap for an unknown area:
     the search returns only known ones, so it never applies.
  3. When the segment ends, the waypoint is removed. With more waypoints the journey goes on,
     unless 0x565140 refuses (then every waypoint is dropped and the PC lands where it is).
     With none left it lands (WT12).
  4. Unless the step ended a segment, the game and animation clocks jump by
     ⌊Chebyshev tile distance between the two positions / 64⌋ hours: one hour for a whole
     sector step, diagonal or not (0x45C200, 3,600,000 ms per sector); a step that stays in the
     sector adds 1 ms. The step that ends a segment adds no time (A3 below).
  5. Unless the PC landed, another WorldMap event is scheduled 62 ms later.
- **Arrival.** The PC lands at the location of the map position. When an area's radius covers
  it (64 tiles for a radius of 0 or below), the PC lands at that area's location instead.
  - The landing is a teleport with flag 0x20 to the map the map list flags as the start map
    (0x40FF50(1), runtime map 1): the teleport lifecycle of [world transitions W9](world-transitions.md), so
    followers that do not wait go along. On success the map position takes the landed
    location.
  - Leaving the travel state stops the music, shows the PC and its followers again, removes
    the WorldMap events (kind 14), then requests a plain teleport to the map position's
    location and only after it the snapped landing. A request while another is pending is
    refused: after an arrival the arrival's own landing stands, and a plain cancel lands on
    the sector centre without the area snap (A4 below).
  - Before the teleport, the cached sectors are saved and unloaded.
- **Time and encounters.** The clock jumps directly, so game-time events that came due fire
  at the queue's next pass. That includes the Random Encounter kind (300..700 game minutes,
  scheduled by the encounter module, 0x559640, F0), which is how a journey is interrupted.

## Anomalies (switch candidates)

Each one is the original's behaviour; whether the Steamwoven setting changes it is an owner
decision for the W3 task (rule modes, [D8](npc-combat-ai-plan.md#owner-decisions-2026-10-16)). The owner
decided on 2026-10-21 that Steamwoven fixes all four; Original keeps them (ARC-WORLD-056).

| ID | What the original does | Effect |
| --- | --- | --- |
| A1 | Discovery compares a later area with the current best's radius, then checks the result against the radius loaded last (WT3). | Where two areas' circles overlap, a nearer area outside its own radius makes the step discover nothing; only the file's last area can be discovered outside its own radius. |
| A2 | A route's step count after a detour also takes the loop's own increment, so one direction byte is never written (WT7). | One stray step of the previous route's byte, or toward x−1, y−1 on a fresh buffer. |
| A3 | The step that ends a segment adds no game time (WT11). | A journey of n sectors over k waypoints takes n − k hours. |
| A4 | Cancelling travel lands on the sector centre without the area snap (WT12). | A cancelled journey lands differently from an arrival at the same place. |

## Steamwoven: slice W3 (done)

The Data, Simulation and Session parts are implemented as ARC-WORLD-056
([world-map travel rules](../world/world-map-travel-runtime.md)); the viewer panel is ARC-UI-048
([world map in map mode](../rendering/area-map-mode.md#world-map-arc-ui-048)).

**ARC-WORLD-05x world-map travel**, Godot-free rules plus a viewer path. It needs no new
original data reader beyond `mes/gamearea.mes`. Its parts:

- **Data.** An area table reader (`Arcanum.Data.Maps`, the module's content stack, phase
  of the area module's init).
- **Simulation.** The route rules (WT5–WT8: straight step, pass test, detour search) as pure
  functions over a sector pass predicate. The travel step rules (WT9–WT13: one sector per
  step, hours per step, discovery, snap, arrival) as a stepper.
- **Session.** The PC's known areas and last discovered area (cleared again by each travel
  step, WT3), and the travel state.
  Encounters are not scheduled (W3 boundary).
- **Viewer.** A diagnostic world-map panel in map mode on the start map. It picks a known
  area or a sector, shows the route, travels and lands through the existing map transfer.
  The PC lands on the main-map window around the arrival, ARC-RENDER-024.

The interface of the original (zoomed tiles, notes, town maps, buttons) is not part of W3.

## Open (F0)

- The waypoint list in town-map mode and the town-map route (0x44EB40).
- 0x424070 and 0x565140 refusing travel.
- The discovery notice 0x460790.
- The encounter module (0x5581E0..0x559640).
- The world-map interface's own load and save of positions and notes.
- Whether the interface draws the route from the same buffer. By inference it does, since
  one buffer serves both.

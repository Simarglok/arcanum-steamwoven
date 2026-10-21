# World-map travel rules (ARC-WORLD-056)

Status: **Implemented** (Godot-free rules and the session's known areas; slice W3 of the whole-world plan M4). No
caller yet: the viewer's world-map panel is a later task. The rules follow the retail 1.0.7.4 executable as read in
[world-map travel](../research/world-map-travel.md) and
[ledger WT1–WT13](../research/original-verification-ledger.md#world-map-travel-arc-res-068).

## Data (`Arcanum.Data.Maps`, WorldAreas.cs)

- **`WorldAreaTable`** reads `mes/gamearea.mes` from the content stack's winner at the module-loaded phase, as the
  area module's init does (0x4CA9B0). Areas are numbered by their position in the file; area 0 is the unknown area.
  - An entry is `x, y, name x-offset, name y-offset /Name /Description`, with an optional `/Radius:<n>`.
  - The radius is 320 tiles by default and n × 64 with `Radius:` (C white space before the key is skipped); area 0
    gets 0 from a `Radius:` key. Searches start at area 1, so area 0's radius is never read.
  - An entry without a `/` name or description keeps what was read, with radius 0 and `Complete` false.
  - A missing file is no failure (no areas). A file the MES grammar does not admit is refused (project policy).
  - Installed: 82 areas; 16 have radius −64 (`Radius: -1`).
- **`WorldTravelSectors`** answers whether a route may enter a sector of the start map (WT7):
  - a sector in the script-blocked list (`map.sbf`, filled by scripts at runtime) never passes; the installed
    module ships none, so a new game starts with the list empty;
  - a sector with its own sector file passes;
  - any other sector passes unless it is terrain-blocked (0x4E8E00): outside the grid, word 0xFFFF, or a base or
    second type marked `/b` (water and the five mountain types in the installed `terrain.mes`).

## Rules (`Arcanum.Simulation.Travel`)

`WorldTravelGeometry` holds the geometry and the area searches:

| Method | Original | What it does |
| --- | --- | --- |
| `Location(pixel, width)` | 0x561800 | A pixel's sector centre: (64 (W − px) + 32, 64 py + 32). |
| `Pixel(location, width)` | 0x561490 | (W − x / 64, y / 64). |
| `Distance(a, b)` | 0x4B96F0 | The Chebyshev tile distance. |
| `Direction(from, to)` | 0x4CFCD0 | The signs of the deltas; the minor axis drops when twice it is below the major. The same sector gives 5. |
| `AreaInRadius(areas, location, arrival, rules)` | 0x4CB4D0 | The area whose radius covers the location, from area 1, 0 for none. At arrival a radius of 0 or below counts as 64. |
| `NearestKnownArea(areas, known, location, distance)` | 0x4CB2A0 | The nearest known area closer than the distance, from area 1. |

The direction codes are 0 (−1,−1), 1 (−1,0), 2 (−1,+1), 3 (0,+1), 4 (+1,+1), 5 (+1,0), 6 (+1,−1), 7 (0,−1).

`WorldRoute` plans segments into one shared buffer of 5,000 direction bytes (0x4207D0):

- It takes straight steps toward the target while the next sector passes.
- When one does not, the detour (0x420900) finds the first passing sector along the straight line. A window search
  (0x4209C0) joins the two when they are fewer than 8 sectors apart on both axes. The search uses a 16 × 16 window,
  costs 10 per step plus 1 per turn, and estimates 10 × the Chebyshev distance.
- The same sector, a blocked destination, a detour too far or no path refuses the segment.
- The buffer outlives a route, as the original's does. Under the original's detour step (A2) the stale byte can walk
  the map position into a blocked sector for one step; the segment's last step still ends on its waypoint.
- A segment whose step count would pass the buffer's end is refused (project policy; only the original's extra
  detour step can cause it).

`WorldJourney` is one journey over the route:

- **`AddWaypoint`** (0x5643E0, 0x564970) adds at most 30 waypoints. A segment starts where the last one ends (the map
  position for the first) and ends at the clicked pixel's sector centre. A known area within 320 tiles of that
  centre takes its place. A refused segment adds no waypoint (`NoRoute`, the original's message 601).
- **`Step`** (0x564C20) is one WorldMap event; the host calls it every 62 real milliseconds (`StepRealMs`):
  1. The map position moves one direction; the segment's last step goes to its end.
  2. The new position discovers an area (`Discovered`, 0 for none or a known one).
  3. A finished segment drops its waypoint. With none left the journey lands (`Landing`) and the map position takes
     the landing (WT12); with more it goes on
     unless the host's `continueJourney` refuses (0x565140), which drops every waypoint and lands.
  4. The step adds ⌊distance / 64⌋ game hours (`GameMs`), 1 ms for a step inside one sector.
- **`Landing`** (0x5649F0) snaps the location to an area whose arrival radius covers it, else keeps it.
- **`Cancel`** drops every waypoint and lands where the map position stands; the map position takes the landing.

The host applies a step: it advances the session clock by `GameMs`, calls `DiscoverArea` and then
`ClearLastDiscoveredArea`, and on a landing teleports the party with flag 0x20 to runtime map 1.

## Rule settings (`TravelRuleProfile`, `GameRules.Travel`)

The four anomalies of the research are switches; Steamwoven has them off and Original on (registry rows in
[rule modes](rule-modes.md)). The Steamwoven values are proposed and the owner's decision is pending.

| Switch | Ledger | Original | Steamwoven |
| --- | --- | --- | --- |
| `DiscoveryUsesBestRadius` | WT3 (A1) | A later area is compared with the best's radius and the result with the radius loaded last. | The nearest area within its own radius. |
| `DetourKeepsStaleStep` | WT7 (A2) | A detour counts one byte more than it wrote. | Only the written steps. |
| `SegmentEndAddsNoTime` | WT11 (A3) | A segment's last step adds no time. | Every step adds its hours. |
| `CancelSkipsAreaSnap` | WT12 (A4) | A cancel lands on the sector centre. | A cancel snaps as an arrival does. |

## Session (`GameSessionOwner`, GameSessionAreas.cs)

- **Known areas.** The story keeps the PC's known areas and last discovered area (schema 28: `knownAreas`, ascending,
  and `lastArea`). Areas are 1..4,095 (`SessionStoryLimits.LastArea`, project policy).
  - `DiscoverArea` (0x4CAFD0) makes an unknown area known and the last discovered one; a known area is NoChange.
  - `ClearLastDiscoveredArea` (0x4CB160) clears the record, as each travel step does after its discovery.
  - A restore refuses a last area the PC does not know.
- **Not in the session.** The journey itself (waypoints, map position, route buffer) is not saved. The original keeps
  it in the interface while the world map is open; whether a save during travel keeps it is not read (F0).

## Real data

On the installed start map, one-waypoint routes from the Crash Site reach only the near areas: Shrouded Hills
(area 1, 43 sectors over forest) and two others. The Crash Site lies in mountains, and a far route across mountains
or water needs waypoints. Every planned journey lands on its area under both settings
(`WorldTravelOriginalTests`, `DAT_ROOT`).

## Limits (NOT READY)

- **No viewer.** There is no world-map panel, no clock pump at 62 ms and no landing through the map transfer yet.
- **Not modelled.** Random encounters (the encounter module, F0), the script-blocked list's writers, town maps,
  notes and the original's world-map interface. Travel music and hiding the party are the host's.

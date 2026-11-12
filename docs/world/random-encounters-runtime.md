# Random encounters runtime

Status: **Implemented bounded component (Simulation rules, Session event and state, World host, viewer hook).** Work item ARC-WORLD-1159 (issue #1159, plan E2-c), 2026-11-12. While the game clock runs, a Random Encounters event comes due every 300 to 700 game minutes. If the hero is on the outdoor start map and in no named area, it rolls for an encounter at the place of the party: the hero's tile, or the world-map marker while the party travels with the map open. A fired roll makes created critters on two rings next to that place, closes the open world map (a journey lands where the marker stood) or the open rest window, and keeps the world map's opener closed for four real seconds. Saved state: the use count of each table row that fired (schema 92, section `encounters`); the pending event is in the `events` section.

Requirements: [FR-TRAVEL-003](../requirements/world-travel-runtime.md#fr-travel-003) (a random encounter depends on the route, area and time), [FR-TRAVEL-004](../requirements/world-travel-runtime.md#fr-travel-004) (sleep, the rest window's chance).

Every rule was read in the retail 1.0.7.4 `Arcanum.exe` ([ledger PO13–PO19](../research/ledger/npc-ai.md#random-encounters-built-arc-world-1159), level F1); the built rows are EN1–EN5 in the same section. No original data, text or art is stored: the tables are read from the player's `rules/wmap_rnd.mes` at run time (the module copy wins over the base one), and the tests use invented tables.

## Layers

| Layer | Holds |
| --- | --- |
| `Arcanum.Simulation.Travel` | `EncounterTables.Parse` (the three charts and the tables, the loader's quirks), `EncounterRules` (region lookup, terrain value, table index, eligibility, `Roll` over an injected draw, `Spot`) |
| `Arcanum.Session` | The event kind `encounter.random`, `GameSessionOwner.FireRandomEncounter` (re-arm, roll, create critters, count the row), `SessionEncounterState` (schema 92), notices for the host (`TakeEncounterNotices`) |
| `Arcanum.World` | `AreaEncounterHost` (tables and the place of the roll, asked at each firing), `AreaWorldMap.ReadEncounterTables`, `AreaStartMapAreas.InTownMapSector`, `AreaEventWorld` forwarding |
| `Arcanum.Data` | `WorldAreas.TerrainWordAt`: the terrain word of a sector of the start map's grid |
| Viewer | `TerrainViewerAreaEncounters` (the place, the notices, closing the windows, the lock), stage `encounters` |

## The roll, in the original's order of draws

1. The event re-arms itself (a draw of 300..700 minutes) before anything else.
2. The host names the place or none. None (not the start map, or the hero stands in a named area) ends the firing.
3. With tables loaded: the chance is the frequency chart's value at the place (default 5), times 5 while the rest window is open; a draw of 1..100 above it ends the roll.
4. The power chart's value (default 2; 0 ends the roll). A place in a sector with a town map ends it. The terrain value of the sector's terrain word (both types must have a value) ends it when 0.
5. The table: the override chart's value when it is not -1, else night + 2 × (power + 3 × terrain value) − 8 (night is the game hour before 6 or from 18). Outside the loaded tables ends the roll.
6. Eligible rows (hero level in range, global flag set, use count below the cap): none ends the roll. One draw of 1..sum of weights picks a row; its use count rises by one.
7. Group sizes (a draw only when a group's minimum differs from its maximum); a total of 0 ends the roll.
8. A draw of 1..100 below 51 puts the groups west of the place, else north of it; each creature stands on its ring spot (the first of a group one tile on, later ones in rings of three). A blocked spot is searched again from the hero's tile with six steps; a creature still blocked or on a critter's tile is destroyed. The first creature of every group after the first stands on the first group's first tile, so it is lost (the original's quirk, kept).

The creatures are the session's created critters (`oid:critter:n`) with no encounter flag, hosted by the AI host and given their goods by the viewer's creation path. The AI makes them hostile or not as their prototypes say.

## Closing the windows (viewer)

For a fired roll: an open world map closes; during a journey `LandWorldMap` ends it where the marker stood (the critters stand round the marker) and its opener (`W`, the bar button) is refused with message 602 for 4,000 real milliseconds; else an open rest window closes (0x57B450). The tick that ends a journey stops at once, as the landing replaced the map's lighting driver.

## Limits (NOT READY)

- The arming is at the first event pass of a host with tables, not at a new game's start; a save from before holds no event and arms at its first pass.
- The `-norandom` switch is not read.
- The creatures do not face the roll's place. A tile outside the shown window is not tested for blocking.
- A sector's town map is read from the shown sectors, else the area table; no other source is read.
- A roll on another map, or with the hero in a named area, only re-arms.

## Verification

`EncounterTablesTests`, `EncounterRulesTests` (Simulation), `EncounterStateTests` (Session: the chain, the roll, the critters, the counts, the save and the schema step), `AreaWorldMapTests` (the town-map sector test, the tables on the data), a census of the installed tables under `RealData` (`AreaEncounterRealDataTests`), and the native stage `encounters` (`./arcanum smoke pr --stages encounters`): a forced roll makes critters that the AI host holds, closes the rest window, closes the world map and locks its opener for 4 s, and ends a journey in a landing at the marker.

# Limits census

## Scope

ARC-DATA-029. Every limit of the universal path (whole-map plans, the area owner, lighting, the
viewer's map mode and the game session) is either derived from the plan and its data, or a
documented policy cap that keeps at least 1.5 times the largest value of all 81 installed maps.
No limit of that path is sized to Crash Site. Two opt-in tests measure the installed original
(`DAT_ROOT`, the game folder) and fail when a cap loses that headroom:
`LimitsCensusRealDataTests` (Data: plans, owner budgets, lighting, map patch) and
`SessionLimitsCensusRealDataTests` (Session); the viewer's map-mode caps are checked in
`AreaMapLoadOriginalTests.Every_interior_the_strict_plan_reads_completely_loads_in_the_viewer`.
Only counts and sizes are recorded, no content.

**Not covered:** the main world map (`arcanum1-024-fixed`, 621 sector files) is reported but not
planned: its sectors are generated from the terrain grid and it is meant to be streamed, so it
exceeds `AreaMapPlans.MaxSectorFiles` by design and that cap is not raised. The pinned Crash Site
exterior and cave profiles keep their own pinned limits (below).

## Limits and headroom

Largest installed value against each cap; "interiors" are the 80 maps a whole-map plan loads.

| Limit | Cap | Largest installed | Map | Headroom |
| --- | ---: | ---: | --- | ---: |
| `AreaMapPlans.MaxSectorFiles` (interiors) | 256 | 79 | caladon-castle main floor | 3.2× |
| `AreaMapPlans.MaxLooseResources` | 1,024 | 0 | — | — |
| `AreaMapPlans.MaxMemberBytes` (mobile aggregate) | 64 MiB | 1,823,280 | arcanum1-024-fixed (interiors: 102,016, bmc) | 36.8× |
| Owner budgets (`AreaLoaderOptions`, scaled by the plan up to 16×) | per plan | retained bytes at their peak (frame holds and memos included): 61.8 % of the budget | qintarra | 1.6× |
| — the other owner budgets (largest use) | per plan | build phase at its peak 17.1 %, stored 19.2 %, decoded 18.9 %, managed 8.2 %, frame requests 5.8 %, encoded 5.2 %, RGBA 4.4 %, read 0 % | void, qintarra | ≥ 5.2× |
| `AreaLightingReader.MaxSectors` | 256 | 79 | caladon-castle main floor | 3.2× |
| `AreaLightingReader.MaxSchemes` | 32 | 3 | k'na tha | 10.7× |
| `StationaryLightReader.MaxLights` (was 256) | 512 | 207 | tarant sewers-02 | 2.5× |
| `StationaryLightReader.MaxFrames` | 16 | 9 | tulla interior | 1.8× |
| `MapPatchFile.MaxBytes` (`Rules\map.patch`) | 1 MiB | 5,779 | — | 181× |
| `AreaMapLoadOperation.MaxChartSectors` | 32 | 14 | k'na tha | 2.3× |
| `AreaRepositoryPolicy.MaxMapFrames` (CPU, and GPU visible since ARC-DATA-029) | 16,384 | 4,557 drawn frames (4,562 owner frame requests) | void | 3.6× |
| `AreaRepositoryPolicy.MaxMapCpuBytes` (CPU, and GPU visible) | 160 MiB | 58,892,780 (56.2 MiB) | void | 2.8× |
| `GameSessionLimits.PolicyMaxMaps` (was 64) | 200 | 81 maps | — | 2.5× |
| `GameSessionLimits.PolicyMaxChangesPerMap` (was 4,096) | 262,144 | 158,385 objects | arcanum1-024-fixed | 1.7× |
| `GameSessionLimits.PolicyMaxTotalChanges` (was 16,384) | 524,288 | 276,625 objects | all maps | 1.9× |
| `GameSessionLimits.PolicyMaxLifeRecords`, `…ReactionRecords`, `…ExperienceRecords` (were 4,096) | 8,192 | 3,792 NPCs | all maps | 2.2× |
| `GameSessionSnapshotCodec.MaxEncodedCharacters` (save file, was 8 MiB) | 96 MiB | about 43 MB estimated | all maps | 2.3× |

Notes on the policy caps:

- **Maps.** The original keeps its map list in a table of 200 entries, and the loader stops
  adding maps when it is full (checked in the 1.0.7.4 executable:
  [ledger](../research/original-verification-ledger.md#limits-census-arc-data-029)). A session
  can therefore never have changes on more maps than that.
- **Changes.** A whole playthrough is sized as a change of every object of the largest map, and
  of every object of every installed map, once. A change is a keyed property value
  ([game session](game-session-runtime.md)); most objects never change, and an object with
  several changed properties uses several. The census counts sector objects and mobile objects
  of each map's own files (`map.patch` and generated objects are not counted).
- **NPC records.** Life, reaction and experience records are kept for NPCs the session changed;
  each cap holds a record for every NPC of every installed map. The session's event queue is
  derived from the life records (four timers each, [event queue](game-event-queue-runtime.md)):
  33,859 events.
- **Snapshot size.** Raising the count caps needs a larger text cap: 262,144 sector changes
  alone take about 16.8 MB. The estimate of a whole playthrough is a change of every installed
  object under the viewer's 68-character object ids (`oid:` and a SHA-256), about 141 characters
  each with a 12-character property and a 7-digit value (276,625 × 141 ≈ 39.0 MB), plus a life,
  reaction and experience record and four timers for every NPC (3,792 NPCs, about 4.4 MB):
  about 43.4 MB, so 96 MiB keeps 2.3 times. It is an estimate, not a measured save. A snapshot
  that reaches the count caps with the longest keys (128-character entities, 64-character
  properties) can exceed the cap; saving it is then the typed `TooLarge`, as before. A save file
  is read into a buffer of its own size, not of the cap. Raising the caps does not change the
  snapshot format, so the schema stays.
- **HD.** The HD visible-set ceiling (`HdDeviceBudget.LegacyAreaVisibleBytes`, `N²` × 81,853,096
  bytes, ARC-RENDER-013) is still the exterior's figure: it holds the largest interior's whole
  repository (58,892,780 bytes) 1.39 times, and the visible set is a part of it. It is an HD
  device ceiling, an exceeded frame draws Legacy (no failure), and changing it is an HD budget
  decision, so it is left as it is and reported.
- **Not sized by the census.** `PolicyMaxCompanions` (64) and `PolicyMaxPcItems` (512) bound
  what the party and the PC hold at once, not anything that grows with the maps; they are
  unchanged.

The pinned Crash Site profiles keep `AreaRepositoryPolicy.MaxFrames` (2,444) and `MaxCpuBytes`
(81,853,096): since ARC-DATA-029 they are defined as the pinned plan's own limits
(`AreaLoaderOptions.PinnedVisuals` and `PinnedFrameRgbaBytes`, the exterior's totals, which its
original profile reconciles), so they derive from the only plans that use them; the cave
(smaller) shares them. Only the pinned profiles'
`CopyFrames` path reads them; until ARC-DATA-029 the GPU's visible-frame check also used them,
so a view of more than 2,444 frames at once could be refused on the two interiors that draw
more (t'sen-ang, 2,622 frames, and void, 4,557). Their removal is left to the fixture task that
replaces the pinned profiles.

## Census by map

Sectors are sector files; objects are sector plus mobile objects; NPCs are objects of type 16;
prototypes are distinct prototype numbers; lights, frames and RGBA are from the area owner and
the stationary-light reader (— where the map has no plan or no light source). Bytes are the
mobile aggregate and the largest sector file.

Six interiors have no stationary lights for a data reason, never a cap (the census test asserts
that no lighting read stops at `LimitExceeded`): two are not in the map list, two have sectors
without the metadata section, one uses an animated light ART (void) and one an unsupported
light flag (bates mansion lev 1).

| Map | Sectors | Objects | NPCs | Prototypes | Lights | Frames | RGBA MiB | Aggregate bytes | Largest sector bytes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| arcanum1-024-fixed | 621 | 158,385 | 1,556 | 708 | — | — | — | 1,823,280 | 127,177 |
| ashbury haunted castle | 21 | 2,878 | 117 | 49 | 2 | 307 | 5.8 | 46,942 | 116,887 |
| ashbury-scienticians underground | 9 | 107 | 1 | 11 | 1 | 47 | 1.1 | 623 | 27,755 |
| bates mansion lev 1 | 9 | 407 | 8 | 21 | — | 181 | 3.6 | 10,460 | 51,112 |
| bates mansion lev 2 | 9 | 396 | 6 | 31 | 4 | 216 | 5.1 | 11,858 | 48,601 |
| bates tunnel-fixed | 12 | 570 | 0 | 9 | — | 44 | 0.8 | 16 | 38,590 |
| bessietoonesmine-fixed | 9 | 722 | 16 | 28 | — | 126 | 2.5 | 6,291 | 76,481 |
| bmc | 43 | 5,596 | 162 | 129 | 165 | 682 | 13.5 | 102,016 | 123,544 |
| caladon guardhouse basement | 9 | 316 | 3 | 24 | 0 | 93 | 1.5 | 6,382 | 42,739 |
| caladon high security prison | 4 | 1,223 | 14 | 41 | 1 | 138 | 2.3 | 23,420 | 58,268 |
| caladon sanders basement | 16 | 108 | 0 | 24 | 2 | 68 | 1.2 | 3,890 | 24,438 |
| caladon trap disarm dungeon | 36 | 1,968 | 0 | 13 | 21 | 71 | 1.5 | 16 | 56,033 |
| caladon-castle main floor | 79 | 3,190 | 56 | 88 | 93 | 292 | 5.2 | 57,643 | 87,809 |
| caladon-mafia underground | 9 | 137 | 7 | 21 | 2 | 79 | 2.8 | 6,246 | 28,175 |
| caladon-panarrii temple | 9 | 903 | 15 | 42 | 15 | 217 | 4.0 | 19,043 | 60,274 |
| caladon-panarrii temple-basement | 1 | 840 | 20 | 13 | 0 | 81 | 1.8 | 7,456 | 87,575 |
| caladons sewers | 36 | 3,013 | 159 | 46 | 119 | 163 | 3.3 | 30,443 | 77,005 |
| castle s'nel n'fa | 9 | 1,930 | 9 | 27 | 1 | 125 | 2.3 | 3,834 | 183,750 |
| cave of the bangellian scourge | 5 | 821 | 31 | 53 | 8 | 239 | 4.2 | 17,372 | 47,103 |
| crash site cave | 1 | 177 | 7 | 30 | 4 | 80 | 1.7 | 6,698 | 29,320 |
| crematorium-lev 1 | 9 | 78 | 8 | 8 | 0 | 41 | 0.8 | 1,552 | 23,084 |
| crematorium-lev 2 | 9 | 749 | 29 | 8 | 0 | 121 | 2.6 | 4,135 | 76,369 |
| crematorium-lev 3 | 9 | 225 | 9 | 12 | 8 | 92 | 2.0 | 2,096 | 33,242 |
| dernholm pits | 11 | 1,520 | 28 | 33 | 3 | 153 | 2.8 | 13,380 | 61,594 |
| dernholm-castle | 1 | 960 | 11 | 33 | 8 | 207 | 3.2 | 12,803 | 96,441 |
| drogs cave | 4 | 205 | 2 | 19 | 2 | 92 | 2.0 | 2,423 | 22,011 |
| dungeon of the dragon pool | 3 | 950 | 26 | 21 | 1 | 144 | 4.2 | 7,702 | 47,974 |
| elven ruins | 9 | 406 | 17 | 27 | 3 | 253 | 4.0 | 7,926 | 43,835 |
| fal'kins maze | 40 | 496 | 0 | 14 | 31 | 118 | 2.8 | 1,774 | 23,138 |
| iron clan - music box | 9 | 448 | 5 | 10 | 18 | 114 | 2.7 | 1,195 | 45,398 |
| iron clan hq | 8 | 80 | 4 | 9 | 16 | 763 | 9.5 | 1,843 | 23,339 |
| k'na tha | 77 | 43,219 | 49 | 54 | 75 | 477 | 10.1 | 37,383 | 92,201 |
| mannox's hole | 9 | 43 | 0 | 8 | 1 | 47 | 0.9 | 497 | 20,437 |
| pit of fires | 14 | 856 | 21 | 25 | 14 | 83 | 2.5 | 7,788 | 56,823 |
| pschuylers lev 1-fixed | 9 | 148 | 8 | 15 | 0 | 85 | 1.7 | 2,304 | 29,812 |
| pschuylers lev 2-fixed | 9 | 316 | 16 | 21 | 0 | 108 | 2.2 | 4,028 | 42,332 |
| pschuylers lev 3-fixed | 9 | 251 | 10 | 28 | 9 | 105 | 1.8 | 6,648 | 37,779 |
| qintarra | 12 | 324 | 18 | 62 | 18 | 2,135 | 28.2 | 39,043 | 22,064 |
| qintarra-queens chambers | 9 | 8 | 1 | 4 | 4 | 226 | 2.7 | 344 | 17,922 |
| roseborough inn2 | 9 | 1,071 | 15 | 72 | 40 | 380 | 6.2 | 35,801 | 100,221 |
| ruins of szabo | 3 | 631 | 21 | 25 | 9 | 142 | 3.4 | 25,383 | 44,679 |
| scienticians underground lev 1 | 9 | 330 | 4 | 10 | — | 58 | 1.2 | 900 | 42,701 |
| scienticians underground lev 2 | 9 | 168 | 7 | 9 | — | 60 | 1.1 | 1,575 | 30,617 |
| shopmap | 1 | 41 | 1 | 35 | 0 | 34 | 0.2 | 7,518 | 16,952 |
| shrouded hills-panarii temple lev 1 | 1 | 222 | 9 | 22 | 8 | 231 | 3.6 | 4,512 | 35,373 |
| stillwater giants cave | 1 | 206 | 1 | 4 | 1 | 61 | 1.7 | 256 | 34,580 |
| stonecutter clan | 18 | 1,196 | 36 | 43 | 40 | 361 | 8.1 | 16,767 | 58,708 |
| t'sen-ang | 14 | 3,887 | 21 | 34 | 22 | 2,622 | 34.1 | 24,028 | 109,102 |
| tarant sewers-01 | 16 | 1,046 | 25 | 32 | 7 | 144 | 2.9 | 9,563 | 67,911 |
| tarant sewers-02 | 64 | 4,801 | 232 | 44 | 207 | 197 | 4.2 | 37,395 | 71,098 |
| tarant-city hall downstairs | 9 | 257 | 5 | 17 | 6 | 133 | 2.5 | 4,611 | 39,079 |
| temple of the derian ka | 16 | 949 | 45 | 55 | 12 | 225 | 4.9 | 22,004 | 75,311 |
| the lair of bellerogrim | 9 | 1,115 | 49 | 20 | 31 | 242 | 4.8 | 10,476 | 55,128 |
| the pit-fixed | 1 | 46 | 7 | 15 | 6 | 491 | 5.8 | 5,774 | 20,479 |
| thieves cave | 1 | 62 | 4 | 10 | 0 | 55 | 1.5 | 3,780 | 21,180 |
| tulla interior | 42 | 2,331 | 34 | 75 | 72 | 1,623 | 25.6 | 43,241 | 84,325 |
| tulla interior - battle - air | 6 | 883 | 11 | 14 | 12 | 75 | 3.6 | 11,277 | 81,354 |
| tulla interior - battle - conveyance | 1 | 84 | 4 | 6 | 7 | 49 | 0.7 | 1,444 | 26,808 |
| tulla interior - battle - divination | 9 | 477 | 6 | 4 | 11 | 188 | 2.8 | 784 | 57,129 |
| tulla interior - battle - earth | 2 | 724 | 25 | 14 | 14 | 83 | 3.7 | 13,383 | 74,919 |
| tulla interior - battle - fire | 9 | 565 | 6 | 5 | 21 | 37 | 0.9 | 816 | 67,237 |
| tulla interior - battle - force | 1 | 556 | 33 | 7 | 22 | 39 | 1.1 | 4,194 | 62,005 |
| tulla interior - battle - mental | 2 | 521 | 7 | 9 | 24 | 44 | 1.1 | 3,326 | 59,836 |
| tulla interior - battle - meta | 1 | 394 | 4 | 10 | 14 | 41 | 1.0 | 1,681 | 50,262 |
| tulla interior - battle - morph | 1 | 298 | 3 | 6 | 10 | 39 | 0.9 | 611 | 42,291 |
| tulla interior - battle - nature | 1 | 425 | 45 | 7 | 16 | 38 | 0.9 | 5,986 | 49,362 |
| tulla interior - battle - necro evil | 1 | 434 | 23 | 10 | 11 | 41 | 1.0 | 14,894 | 47,210 |
| tulla interior - battle - necro good | 1 | 615 | 42 | 8 | 19 | 44 | 1.1 | 5,474 | 66,015 |
| tulla interior - battle - phantasm | 1 | 252 | 11 | 5 | 9 | 36 | 1.0 | 1,402 | 37,703 |
| tulla interior - battle - summoning | 3 | 325 | 24 | 8 | 13 | 39 | 0.9 | 12,471 | 39,247 |
| tulla interior - battle - temporal | 1 | 397 | 19 | 4 | 16 | 35 | 0.9 | 2,434 | 49,386 |
| tulla interior - battle - water | 1 | 548 | 80 | 8 | 24 | 39 | 0.9 | 23,636 | 52,338 |
| uncharted cave | 1 | 83 | 1 | 6 | 2 | 57 | 1.7 | 722 | 23,575 |
| vendigroth underground | 53 | 3,750 | 77 | 48 | 100 | 1,353 | 23.2 | 32,800 | 76,722 |
| void | 54 | 928 | 102 | 25 | — | 4,562 | 56.2 | 17,138 | 34,739 |
| void - arronax's interior | 9 | 269 | 4 | 9 | 17 | 58 | 1.4 | 2,015 | 35,267 |
| void - cave of kriggyrd's falchion | 20 | 636 | 25 | 12 | 14 | 95 | 3.3 | 4,699 | 69,080 |
| void-kerghans castle | 30 | 1,902 | 76 | 26 | 76 | 1,224 | 16.2 | 20,704 | 106,892 |
| vollingers basement | 9 | 91 | 3 | 18 | 1 | 54 | 0.9 | 3,573 | 23,700 |
| wheel clan | 24 | 7,848 | 178 | 157 | 171 | 1,653 | 26.3 | 95,995 | 107,924 |
| wolf-cave | 9 | 292 | 18 | 30 | 2 | 92 | 2.0 | 11,871 | 36,416 |

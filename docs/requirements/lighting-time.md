# World time and lighting — functional requirements

**Current status:** the status of each requirement in this document, the implemented scope, the remainder and the documentation are in the [coverage matrix](coverage/lighting-time.md) (re-checked 2026-10-20, ARC-DEV-027). The "Steamwoven" fields below and the summary coverage section give details as of the date of their last edit (original audit 2026-10-09, merged main [`a8a553b7`](https://github.com/Simarglok/arcanum-steamwoven/tree/a8a553b7c406ace77ea019bf2b13251f00ecddb6), PR #54); where they differ, the matrix is right.

**Research cut:** 2026-10-02. **Scope:** game time, day/night, global and local light, and its link to the matching checks. This is a bounded specification, not an audit of all game systems. Weather, the full job/AI scheduler and the engine beyond the directly confirmed dependencies are out of scope.

**Claim levels.** "Original observation" — reading of original functions at the listed addresses, level F0 (not checked in the exe) unless the row is in the [verification ledger](../research/original-verification-ledger.md); the named ledger rows are checked in the exe. "Project proposal" — a functional policy for an independent implementation. "Unknown" — behaviour that cannot be pinned down without additional data checks or the original runtime. The requirements and scenarios below are proposed and not implemented by this document.

**Steamwoven snapshot:** the actual `main` at `b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae` (PR #36) was checked. The [project status](../project/status.md) catalogue still points to the base `0bc9962f90aed467b09ca977206ce456e31a4616`, through PR #30; so the coverage below is checked against the code of the current cut, not derived from the stale catalogue row. Links to Steamwoven code in the report are pinned to these SHAs.

## Requirements

**ARC-RES-018 — RESEARCH (2026-10-07):** the [canonical clock/light contract](../world/time-lighting-contract.md)
traces the raw clock, relative hour scheduling, numeric map/sector schemes,
indoor/outdoor channels and stationary/primary/overlay provenance; the bounded inspector
and the independent synthetic/original-data checks are kept apart from production coverage.
The selected exterior has no stationary records but has non-invalid object-light
descriptions; this does not prove they are active. Calendar quirks, gaps/reload,
coordinate/mask readiness and layer-specific tint are not replaced by guessed defaults.
FR-LIGHT stay proposed: the production clock/scheduler/renderer and original
executable/gameplay/visual parity are **not implemented / NOT_VERIFIED** by this research.

<a id="fr-light-001"></a>

### FR-LIGHT-001 — One game clock and the day/night boundary

**Proposed phase: Pre-alpha.** The simulation must have one game date and time. Systems that need the clock or a day flag read them from this state and do not compute their own timer. The player must be able to see the current game time and the "day/night" state in the interface the project provides.

**Original observation:** the clock update (0x45B370) keeps the game and animation clocks separate from real time; the current hour, minute and day (0x45AAA0, 0x45AAC0, 0x45AB00) read the game clock (the getters of the current day and month are swapped and never called, row [Clock current day/month](../research/ledger/world.md#world-movement-and-data-arc-res-047-arc-res-029-arc-res-034-arc-data-021-clock); the date-time getters from the pair of days and milliseconds are checked, row [LB13](../research/ledger/ui-audio.md#logbook-arc-ui-008)), and the day flag (0x45ACF0) treats the interval `[06:00, 18:00)` as daytime (F0; the same interval in the sector light walk is checked, row [SS8](../research/ledger/world.md#main-map-streaming-arc-data-030)). The interface callback updates the game dial and schedules the next update. Clock and calendar accessors (0x45A840, 0x45A870, 0x45A890, 0x45A8B0, 0x45A8D0, 0x45A8E0, 0x45A900, 0x45A920, 0x45A950, 0x45A970, 0x45A9B0, 0x45A9E0, 0x45AA10, 0x45AA50, 0x45AAA0, 0x45AAC0, 0x45AAE0, 0x45AB00, 0x45AB20, 0x45AB40), day predicate (0x45ACF0), clock UI callback (0x5526F0). Reading the functions is not an exhaustive check of the original runtime.

**Dependencies:** serializable game clock state; a compatibility profile for the calendar and thresholds; one interface for reading the time for the simulation and for display. **Proposed scenarios:** check the boundaries 05:59/06:00 and 17:59/18:00 on the original profile; advance the clock across midnight and confirm that the interface, lighting and consumers of the day flag see the same state.

**Steamwoven:** partial, [ARC-WORLD-010](../rendering/area-lighting.md): one session clock ([WORLD-004](../world/game-clock-runtime.md) inside [WORLD-008](../world/game-session-runtime.md)) runs by itself in the `area` viewer from host time (original function reading: 5/250 ms, 8×, level F0, NOT_VERIFIED), stays paused on Menu pause while the palette or a dialog is open without catch-up, and moves by an explicit "+1 hour" wait even while paused; lighting, the HUD and the snapshot read this clock. There is no sleep, turn-based combat or game event queue.

<a id="fr-light-002"></a>

### FR-LIGHT-002 — Time flow, stop and explicit wait

**Proposed phase: Pre-alpha.** The game clock advances by the rules of the selected time profile. When the profile pauses the simulation, the game clock stands still and does not by itself bring a future game-time deadline closer; an explicit wait/sleep action advances time by the given duration and updates the lighting state before the world is shown to the player again.

**Original observation:** the clock update (0x45B370) adds `8 × delta` to the game clock outside turn-based combat and with the internal gate open (F0); during turn-based combat the game clock does not change although the animation clock does (checked in the exe, row [TB15](../research/ledger/combat.md#turn-based-round-arc-combat-003)). `delta` itself is skipped below 5 ms and capped at 250 ms, so the factor 8 cannot be read as an unconditional speed relative to real time. Sleep adds a game hour explicitly; the ambient update is switched off for the duration of the fade-out and resumes on waking. Tick and advance conditions (0x45B370), sleep step (0x57B7F0), fade-out and update on waking (0x57B9E0, 0x57BA70). The internal gate, with an uninformative name, does not reveal the full set of stop reasons.

**Queue boundaries.** After the clock update the original walks the real-time, game-time and animation queues separately (queue loop (0x45B370); the queue order is checked in the exe, rows [TE3, TE4–TE7](../research/ledger/scripts.md#scripts-event-queue-and-randomness-arc-res-042-arc-res-045-arc-script-007-arc-res-046-arc-world-019)). When the game clock is stopped, a game-time callback that is already due can still be processed; stopping the clock does not prove a global block of callbacks. The proposed acceptance distinguishes a future deadline, an already due event, a real-time event and an animation event under gate/TB. If the project pauses all callbacks, that is a separate project policy, not a conclusion from the original.

**Dependencies:** the time-speed profile, turn-based combat and other stop states, the game event queue and wait operations. **Proposed scenarios:** a render-frame pause does not change game time; a profile pause blocks the clock transition; a one-hour wait advances by exactly one hour under the selected policy and lifts the fade-out only after the light is synchronized. The number `8×` should be accepted only after a decision on the target profile and a check of the original runtime.

**Steamwoven:** partial, [ARC-WORLD-010](../rendering/area-lighting.md): one session clock ([WORLD-004](../world/game-clock-runtime.md) inside [WORLD-008](../world/game-session-runtime.md)) runs by itself in the `area` viewer from host time (original function reading: 5/250 ms, 8×, level F0, NOT_VERIFIED), stays paused on Menu pause while the palette or a dialog is open without catch-up, and moves by an explicit "+1 hour" wait even while paused; lighting, the HUD and the snapshot read this clock. There is no sleep, turn-based combat or game event queue.

<a id="fr-light-003"></a>

### FR-LIGHT-003 — Hourly ambient color scheme

**Proposed phase: Pre-alpha.** The active scheme holds separate ambient RGB colors for indoor and outdoor surfaces for each hour of the day. The colors come from versioned game data and are addressed by a scheme identifier. Numbers from the source resources must not be embedded in code as a universal palette.

**Original observation:** loading schemes at module load (0x4A7C90) requires the index catalogue of lighting schemes; scheme selection (0x4A7DD0) resolves the scheme id to the matching data file (the load phase of both files is checked in the exe, rows [LP3, LP4](../research/ledger/data.md#load-phases-arc-data-027)), and scheme parsing (0x4A8060) reads 24 hourly records of the form outdoor RGB + indoor RGB (F0). Scheme load and selection (0x4A7C90), id-to-data resolution (0x4A7DD0), hourly record format (0x4A8060).

**Dependencies:** scheme table identifiers and content; a six-channel record per hour; safe checking of input ranges and completeness. **Proposed scenarios:** a synthetic scheme with different indoor/outdoor components yields 24 independent pairs and does not change the source resource data; a missing file or an invalid record gives an explicit diagnostic error instead of a silent plausible palette.

**Steamwoven:** partial, [ARC-RENDER-010](../rendering/area-lighting.md): a bounded implementation for the `area` viewer (Data `Arcanum.Data.Lighting`, WORLD-004 clock, per-channel shader multiplier). Boundaries, checks and NOT_VERIFIED are in the [component description](../rendering/area-lighting.md); research is [RES-018](../world/time-lighting-contract.md).

<a id="fr-light-004"></a>

### FR-LIGHT-004 — Empty hours and verifiable interpolation of scheme data

**Proposed phase: Pre-alpha.** If the profile format allows empty hourly records, scheme normalization must determine their color before the scheme is applied and document the rules for the initial and final empty ranges. This step fills a temporary table; by itself it does not mean a smooth transition of the image between minutes.

**Original observation:** in scheme parsing (0x4A8060, F0) a non-empty record sets the next anchor, empty records mark a gap, and on meeting a later non-empty anchor the intermediate channels are filled with integer steps. The code does not close the range after the last anchor and does not set up cyclic wrap-around; the array is allocated zeroed before parsing. So the edge behaviour needs confirmation on valid source data and is not generalized as cyclic interpolation. Parser and gap fill (0x4A8060), zero initialization of the table (0x4A7C60).

**Dependencies:** a confirmed scheme grammar and the rules for its edge slots. **Proposed scenarios:** on synthetic anchors confirm the intermediate value of each channel and the rounding; separately fix the expected behaviour of empty slots before the first and after the last anchor; reject or safely diagnose a table without enough anchors.

**Steamwoven:** partial, [ARC-RENDER-010](../rendering/area-lighting.md): a bounded implementation for the `area` viewer (Data `Arcanum.Data.Lighting`, WORLD-004 clock, per-channel shader multiplier). Boundaries, checks and NOT_VERIFIED are in the [component description](../rendering/area-lighting.md); research is [RES-018](../world/time-lighting-contract.md).

<a id="fr-light-005"></a>

### FR-LIGHT-005 — Hourly scheme application

**Proposed phase: Pre-alpha.** When the game hour changes, the renderer receives the hourly indoor/outdoor pair of the current scheme and updates the dependent surfaces. In the original-compatibility profile this is a discrete application on the hourly event. Interpolation of configuration gaps from FR-LIGHT-004 must not be passed off as a continuous runtime transition between hours.

**Original observation and chain:** the ambient callback (0x57D660) reads the current hour (0x45AAA0), calls the scheme hour application (0x4A7EB0), then schedules the next event after 3 600 000 game milliseconds. The hour application calls the color update (0x4D8560), which updates the ambient palettes and invalidates drawing. Hourly callback and schedule (0x57D660, 0x57D6C0), hour application (0x4A7EB0), color update and invalidation (0x4D8560). The value `3 600 000` here is a delay on the game-time scale, not 60 minutes of real time.

**Dependencies:** the clock and scheme from FR-LIGHT-001/003; game-time events; invalidation of palette/light buffers. **Proposed scenarios:** in a synthetic scheme the colors are unchanged between two game hour boundaries, change exactly at the hour transition and match the current time immediately after resuming from pause.

**Steamwoven:** partial, [ARC-RENDER-010](../rendering/area-lighting.md): a bounded implementation for the `area` viewer (Data `Arcanum.Data.Lighting`, WORLD-004 clock, per-channel shader multiplier). Boundaries, checks and NOT_VERIFIED are in the [component description](../rendering/area-lighting.md); research is [RES-018](../world/time-lighting-contract.md).

<a id="fr-light-006"></a>

### FR-LIGHT-006 — Indoor/outdoor color on the matching surfaces

**Proposed phase: Pre-alpha.** Drawing selects the ambient color by the verified surface type and the layer rules. Outdoor and indoor surfaces use their own channels; roofs, roof hiding and other layers apply separately established rules. This does not reduce to a single screen color grade over the whole scene.

**Original observation:** the terrain draw takes the indoor or outdoor color by the ART tile class and with local light may blend color values (F0); the roof draw passes the outdoor ambient color (with hardware drawing, checked in the exe, row [RF2](../research/ledger/world.md#roofs-arc-render-021)). Color selection and tile blit, roof color (0x439140). Classification and composition must be confirmed separately for all target layers.

**Dependencies:** terrain/roof classification, the ambient pair, tile/facade/roof pass order, source palettes and renderer blend behavior. **Proposed scenarios:** a contrasting synthetic scheme visibly distinguishes indoor/outdoor terrain; changing one channel affects only the matching class; a roof does not get the indoor channel by default.

**Steamwoven:** partial, [ARC-RENDER-010](../rendering/area-lighting.md): a bounded implementation for the `area` viewer (Data `Arcanum.Data.Lighting`, WORLD-004 clock, per-channel shader multiplier). [ARC-RENDER-012](../rendering/area-lighting.md#cave-ambient-and-stationary-sector-lights-arc-render-012) adds object color by original function reading (F0) for bodies, walls and the actor (the tile class under the cell plus local light) and the cave indoor scheme. Boundaries, checks and NOT_VERIFIED are in the [component description](../rendering/area-lighting.md); research is [RES-018](../world/time-lighting-contract.md).

<a id="fr-light-007"></a>

### FR-LIGHT-007 — Map-default and per-sector scheme selection

**Proposed phase: Pre-alpha.** When a map loads, its default scheme is applied; a sector may set its own scheme. When a sector boundary is crossed, the ambient pair is selected for the active sector at the current game hour. An undefined or missing id must have an explicit profile rule.

**Original observation and chain:** map expansion loading (0x4115D0) reads the map default from the map metadata (the `mapinfo.txt` read phase is checked in the exe, row [LP4](../research/ledger/data.md#load-phases-arc-data-027)); the sector stores a lighting scheme field (the order of sector sections is checked, row [SS5](../research/ledger/world.md#main-map-streaming-arc-data-030)); the sector change callback resolves the special id "map default" and applies the selected scheme at the current hour. Map default (0x4115D0), sector field, application at sector change (0x4F2330), reading the value from the sector (0x4D1A30).

**Dependencies:** the map, the sector selector, mapinfo, the sector light scheme field and the table of scheme ids. **Proposed scenarios:** two adjacent synthetic sectors with different schemes switch at the boundary; a sector with the map-default id follows the current map default; switching keeps the same game hour.

**Steamwoven:** partial, [ARC-RENDER-010](../rendering/area-lighting.md): a bounded implementation for the `area` viewer (Data `Arcanum.Data.Lighting`, WORLD-004 clock, per-channel shader multiplier). Boundaries, checks and NOT_VERIFIED are in the [component description](../rendering/area-lighting.md); research is [RES-018](../world/time-lighting-contract.md).

<a id="fr-light-008"></a>

### FR-LIGHT-008 — Sector stationary light sources

**Proposed phase: Pre-alpha.** The sector light list creates and removes local sources in world coordinates, taking into account offset, activity, type (light/darkening), ART profile and tint. Light affects the matching local area, may be animated and is handled correctly at the borders of neighboring sectors.

**Original observation and chain:** sector loading reads the count and then each light record (the transfer of template-sector sources by the object rule is checked in the exe, rows [Wilderness A1](../research/ledger/world.md#world-movement-and-data-arc-res-047-arc-res-029-arc-res-034-arc-data-021-clock) and [SS6](../research/ledger/world.md#main-map-streaming-arc-data-030)); the serialized structure has size `0x30` (48 bytes); adding places the source in the list and starts the animation if needed; the renderer walks the lists of visible sectors and applies the active light ART and tint. The 48-byte record and its read, read light (0x4DDD20), sector list load/add (0x4F71C0, 0x4F7200, 0x4F7250, 0x4F72C0), sector load caller (0x4D1A30), visible light renderer (0x4DE900).

**Dependencies:** sector records, light records, save/DIF override, light ART/palette, map coordinates and neighboring sectors. **Proposed scenarios:** synthetic stationary light affects only intersecting tiles; OFF suppresses it; DARK subtracts rather than adds; an overlapping emitter in a neighboring sector remains visible without duplicate contribution.

**Steamwoven:** partial, [ARC-RENDER-012](../rendering/area-lighting.md#cave-ambient-and-stationary-sector-lights-arc-render-012): stationary sources of the core sectors are read from the strict snapshot and drawn in the `area` viewer (a 40×20 grid for terrain, object color for bodies, a gradient for walls). On the original there are 4 sources in cave 5078 and none outdoors. Animated light, DIF/save overrides and sources of neighboring maps are not implemented. [Research frontier](../world/time-lighting-contract.md), [Area loader](../world/area-loader.md).

<a id="fr-light-009"></a>

### FR-LIGHT-009 — Object, overlay and carried sources

**Proposed phase: Pre-alpha.** Light tied to a world object follows its position/offset and lifecycle; overlay lights belong to the same object. Worn/equipped gear may update the size/color of the character's local light according to confirmed item rules. An item in the inventory must not itself remain a separate world emitter unless the profile policy requires it.

**Original observation and chain:** the sector light walk (0x4F7940) drops inventory objects from the ordinary sector walk and adds a primary and up to four overlay light handles (F0; row [SS8](../research/ledger/world.md#main-map-streaming-arc-data-030) checks only the "not in inventory" condition for the night switching of scenery); equipping and unequipping call the item light recalculation (0x467E80), which recalculates the character flags, gets the creature light profile (0x45FA70) and calls the object light binding (0x43EBD0) (F0). Sector adding of object/overlay lights (0x4F7940), recalc at unequip (0x467CB0), equipped flags → object light (0x467E80), critter light profile (0x45FA70), object light binding (0x43EBD0).

**Dependencies:** object/prototype and item flags, equipment slots/effectiveness, actor state, object movement, primary/overlay light ART, sector membership. **Proposed scenarios:** equipping/unequipping a synthetic light item changes only the expected actor emitter; movement carries the source along; removing the owner removes the linked sources; an inventory item is not added as a second source next to the character.

**Steamwoven:** there is no production runtime for this requirement at the audit baseline. The area/terrain reader keeps light-section framing/counts and raw fields, without light records/handles/activity semantics or emitters. [Research frontier](../world/time-lighting-contract.md), [Area loader](../world/area-loader.md).

<a id="fr-light-010"></a>

### FR-LIGHT-010 — Switching on time-sensitive sources

**Proposed phase: Pre-alpha.** If an object/source has a confirmed tie to the time of day, the change of day and night changes its activity and visual state on the same time boundary as the profile. The switching criteria must belong to the object's data/rules; they must not be derived only from the word "night" or a resource name.

**Original observation:** at sector load the sector light walk (0x4F7940) classifies the hour as day `[06:00,18:00)` and may switch the matching night scenery off/on by the base tile type (checked in the exe, row [SS8](../research/ledger/world.md#main-map-streaming-arc-data-030)). When ambient colors change, the color update (0x4D8560) walks the loaded sectors and calls an update; the function 0x4DDD90 synchronizes the object flags and its light handles (F0). Fold at load and temporary switching on (0x4F7940), color update → sector enumeration (0x4D8560), hourly handling of scenery and lights (0x4DDD90). Reading the functions does not establish the full set of original object types.

**Dependencies:** the game clock, the scheme, scenery flags, terrain tile class, object state and sector loading. **Proposed scenarios:** a synthetic time-sensitive emitter is off by day and on by night per the profile; sector load computes the correct flag from the current hour at once; an hour change does not add a light handle twice.

**Steamwoven:** partial, [ARC-RENDER-012](../rendering/area-lighting.md#cave-ambient-and-stationary-sector-lights-arc-render-012): by original function reading, stationary sector light does not depend on the hour (only OFF is checked), so on an hour change only the blend with the new ambient is rebuilt; sources with INDOOR/OUTDOOR take the color of the current hour (synthetic, the cave sources have flags 0). Night switching of scenery objects and their light belong to FR-LIGHT-009 and are not implemented. [Research frontier](../world/time-lighting-contract.md).

<a id="fr-light-011"></a>

### FR-LIGHT-011 — Luminance for explicitly light-aware checks

**Proposed phase: Pre-alpha.** Mechanics whose dependence on light is confirmed use a common luminance query for an object or coordinate. The check must take into account the base indoor/outdoor ambient, the active local lights, their add/darken direction, and only explicitly confirmed source/target modifiers. This requirement does not claim that all visibility, AI search or line-of-sight depend on such an estimate.

**Original observation and chain:** the skill application difficulty (0x4C8430) for light and sight check flags reads the grey luminance of the target, source or coordinate through the functions 0x4DCE10/0x4D9240; the latter calls 0x4D89E0, where the indoor/outdoor baseline and the active local light masks are summed. The calculation then applies the creature's light emission and dark sight coefficients and adds a concrete skill penalty. Light-aware caller and calculation (0x4C8430), positional sampling and light contribution (0x4D89E0, 0x4D9240), object luminance accessor (0x4DCE10).

**Dependencies:** a single light sampling API; the skill invocation context; emitted-light flags and dark-sight capabilities; object/coordinate luminance. **Proposed scenarios:** synthetic light changes only an explicitly light-aware result; identical object and coordinate parameters give a consistent sample; dark-sight/emission modifiers are checked independently; actions unrelated to light do not change.

**Steamwoven:** there is no production runtime for this requirement at the audit baseline. See the [component catalogue](../project/status.md); the research contract does not close gameplay/save/Remastered integration.

<a id="fr-light-012"></a>

### FR-LIGHT-012 — Saving time and the applied scheme

**Proposed phase: Pre-alpha.** Save/load keeps the authoritative game clock and enough lighting state that after loading the active scheme, the current hour and the scheduled updates match. Loading must not first show a stale ambient layer over an already updated world.

**Original observation:** clock save and load (0x45AED0, 0x45B120) keep the real/game/animation date-time and the serializable events (the saved event kinds are checked in the exe, rows [OQ2, OQ3](../research/ledger/scripts.md#scripts-event-queue-and-randomness-arc-res-042-arc-res-045-arc-script-007-arc-res-046-arc-world-019)); a separate lighting scheme component keeps the current scheme id and hour, and the loader calls scheme selection (0x4A7DD0). Clock save (0x45AED0), clock load (0x45B120), scheme save/load (0x4A7D00, 0x4A7D40), game library component registration.

**Dependencies:** save schema/versioning, world clock, active scheme, sector state and event queue. **Proposed scenarios:** save a synthetic world before an hour transition, load it after several wall-clock minutes and get the same clock/scheme as before; save while the world is paused and check exact resumption without skipping or repeating the hourly event.

**Steamwoven:** partial, [ARC-WORLD-010](../world/game-session-runtime.md#snapshot-schema-62): the session snapshot of schema 3 carries the clock (time, pause) and the applied scheme (id and hour); the viewer writes the scheme after each hour or sector change, restore rejects a scheme of another hour. Quick save F7/F8 (ARC-WORLD-011) writes and loads this clock and scheme, the day/night driver re-binds to the loaded session; there is no save list or event queue.

## Later visual ideas and evidence boundaries

<a id="fr-light-013"></a>

### FR-LIGHT-013 — A separate Remastered visual profile

**Proposed phase: Later / Remastered.** Soft shadows and normal-map treatment may be proposed as separate visual options after Legacy compatibility. They do not change the game clock, the scheme map, luminance, a skill outcome or the world state and must not be declared restored features of the original.

**Original observation:** the functions read contain local sources, a dark-light branch, a legacy shadow API/settings and light drawing, but there is no confirmation here of a modern soft-shadow profile or normal-map lighting specifically. This does not prove the absence of any form of shadows in the original; these two specific enhancements remain a project idea. **Dependencies:** a separate presentation backend and an explicit visual-mode switch that keeps the simulation state according to the accepted [mode direction](../architecture/decisions.md#architecture-direction). **Proposed scenario:** switching Legacy/Remastered changes only the picture; the synthetic clock, luminance gameplay values and saved state stay identical.

**Steamwoven:** there is no production runtime for this requirement at the audit baseline. See the [component catalogue](../project/status.md); the research contract does not close gameplay/save/Remastered integration.

## Coverage and uncertainties up to 2026-10-20

[RES-018 time/light](../world/time-lighting-contract.md) and [RES-020 clock policy](../world/game-clock-runtime.md#original-clock-evidence-carried-from-the-res-020-policy) are merged as research. Their bounded source/raw-data/table checks are not tests of the production clock or lighting. At the audit baseline there are no public clock/scheduler/light-scheme/emitters/rendering/save systems. WORLD004 exists on the board but did not enter this merged cut. Exact movement time and ART playback clocks remain separate domains.

The Area Data owner keeps section boundaries/counts and raw fields; decoding of sources/schemes, their meaning and activity need their own implementations. The research state and production coverage are separated in the matrix.

## Steamwoven coverage up to 2026-10-20

The coverage table of this section has moved to the [coverage matrix](coverage/lighting-time.md): there each requirement has its current status, scope, remainder and documentation (2026-10-20). The paragraphs below described the earlier table of this document (original audit 2026-10-09 with later edits) and are kept as history; the words "table", "rows" and "columns" in them refer to it.

This matrix refers to the checked Steamwoven base, not to the presence of features in the original game. The framing section and the selected zero count do not prove the absence of light data in other sectors or game runtimes.

## Inventory of sources reviewed and coverage

**Original — relevant functions and related callers were read:**

- Game clock and events: the event queue and clock (0x45A7C0–0x45CC10, 80 functions) — date-time accessors, day predicate, tick, explicit time increase, save/load and event types.
- Ambient schemes and local lights: lighting schemes (0x4A7C60–0x4A8060, 15 functions) and light (0x4D7F30–0x4DF310, 79 functions) — scheme load/parse/selection, color update, light-record size and read, positional sampling, drawing and sector invalidation.
- Sector/object sources: sector light lists (0x4F7110–0x4F7B50), sector (0x4CEF70–0x4D31C0), map (0x40EA90–0x412C90), sector object lists (0x4F1150–0x4F2330) — load/add/fold, the scheme field, map default and sector transition. The DIF loader was read only for the call; the full policy of saved differences was not reconstructed.
- Time of day, items, luminance and surfaces: the ambient callback (0x57D240–0x57D6C0), the sleep window (0x57B080–0x57BAC0), relevant branches of the world map window (0x55F8D0–0x566D10), items (0x460E70–0x4681A0), creatures (0x45CF30–0x45FC70), objects (0x43A330–0x444690), skills (0x4C5BD0–0x4C91F0), cells (0x4D6840–0x4D7E90), roofs (0x438F90–0x43A1A0), the game library (0x4020F0–0x405070).

**Related original functions found during the search but not fully analysed:** the remaining callers of the light/visibility check; object/prototype flag definitions and all equip/update paths; the full save lifecycle; tile/object/roof render ordering and the lower TIG palette/blit backend; other UI/settings paths for selecting a lighting scheme. Weather, the full AI/quest scheduler and game systems not directly dependent on light are outside this cut.

**Steamwoven — directly reviewed:** `src/Arcanum.Data/Terrain/TerrainLoader.cs` (sector framing and skipping the light section), `TerrainModels.cs` (framing model), `tools/terrain-viewer/Godot/TerrainCanvas.cs` (draw path), as well as `docs/formats/maps.md`, `docs/rendering/terrain-viewer.md`, `docs/project/status.md`, `docs/architecture/decisions.md` and `docs/research/method.md`. A search for lighting/time terms was run over the current `src`, `tools/terrain-viewer` and `tests`; the absence of functionality is stated only for the checked Data/Simulation/Presentation/viewer paths.

**Not checked:** the contents of the original resource payloads and lighting scheme tables, the game executable/runtime capture, the build and behaviour outside the stated area. So palettes from the original data, full agreement of the functions read with the original runtime and the target time multiplier are not confirmed here.

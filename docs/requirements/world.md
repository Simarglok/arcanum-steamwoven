# Functional requirements: maps, sectors and transitions

**Current status:** the status of each requirement in this document, the implemented scope, the remainder and the documentation are in the [coverage matrix](coverage/world.md) (rechecked 2026-10-20, ARC-DEV-027). The "Steamwoven" fields below and the summary coverage section give the detail as of the date of their last edit (initial audit 2026-10-09, merged main [`a8a553b7`](https://github.com/Simarglok/arcanum-steamwoven/tree/a8a553b7c406ace77ea019bf2b13251f00ecddb6), PR #54); where they differ, the matrix is correct.

ARC-RES-017: [whole-exterior traversal research](../world/area-traversal-contract.md)
for FR-WORLD-002/003/004: coordinates, neighbor binding and edge evidence;
Research/Proposed, NOT_READY, without streaming/eviction or crossing movement runtime.

**Research cutoff:** 2026-10-02. Checked checkout: `arcanum-steamwoven` commit `b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae`; original behavior is taken from the functions of the 1.0.7.4 executable at the stated addresses. This is the initial source-research baseline; the current implementation coverage is stated above. The requirements cover this domain only; this is not a full audit of the game and not an approved backlog.

## How to read the findings

**ARC-RES-018 — RESEARCH:** the [clock/light contract](../world/time-lighting-contract.md)
ties the numeric map default/sector sentinel to the active hour scheme, without declaring
a separate ambient policy for each visible neighbor. The bounded original selection
and the synthetic override/coverage checks do not implement world streaming/transfer;
stationary coordinates and masks remain NOT_READY. Legacy/HD shared state is a
proposal; the production clock and original-runtime parity are not confirmed.

ARC-DATA-009 adds a [bounded complete-area Data API](../world/area-loader.md):
a survey of 621 → 9 core sectors and an explicit context, immutable world/source keys, 36,864
core terrain, 426 static and 88 mobile bodies, reconcile 7,399 aggregate records.
This is partial data-layer coverage of FR-WORLD-001/002/003/005/007 (sources, addresses,
snapshot and numeric endpoint), not a streaming/eviction runtime, crossing movement,
cave transition, DIF/PATCH or original-runtime parity. The [coverage matrix](coverage/world.md) counts this merged scope.

The later bounded research ARC-RES-015 (the [Crash Site area census](../research/area-inventory.md) and the [proposed multi-sector/mobile contract](../world/area-contract.md)) refines the sources, the boundary and the potential cave endpoints. This is research, not an implementation of FR-WORLD-001..007 and not original-runtime parity; the current production coverage is stated below, separately from the research.

- **Original** — reading the original's functions at the stated addresses, level F0 (not checked in the exe) unless the row is in the [verification ledger](../research/original-verification-ledger.md); the named ledger rows are checked in the exe. This is not a check of behavior on original data.
- **Target policy** and the acceptance scenarios below are proposed Steamwoven behavior. They rest on the original's behavior but are not an already accepted product policy.
- **Unknown** means there is not enough code, specific linkage in the source data or verification to draw a conclusion.
- Steamwoven coverage is rechecked against the current audit baseline: the Area Data API, production traversal and WORLD003 have different scopes. A full exterior snapshot does not close the live-registry/HP/state frontiers, streaming or the transition into a cave. See the [component catalog](../project/status.md).

## Proposal by stages

The **pre-alpha** and **later** labels are a preliminary estimate for the requirements of this world domain only. The minimum pre-alpha candidates here are map/sector identity, sector boundaries, one verifiable connector, the exact arrival place and keeping the changed state across leaving/returning. This is not the complete definition of pre-alpha, not a requirement to load the whole world and not a ranking of other game mechanics. The labels are a proposal for agreeing the early scope; they are not deadlines, assignments or authority to create tasks. [Decisions on the architectural direction](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/architecture/decisions.md), [roadmap and the task authority boundary](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/roadmap.md#L1-L4).

## Requirements

<a id="fr-world-001"></a>

### FR-WORLD-001 — Map catalog and regional attributes

**Stage:** pre-alpha. **Observable behavior:** by a stable ID the system selects a map, its start coordinate and its size; the map's regional properties (`world map`, `area`, start type) stay bound to the same ID during play.

**Boundaries:** catalog metadata, resolving the name/ID and checking that the target map exists. The selection order between DAT/PATCH/loose files is not included here.

**Original:** loading the map list (0x4125C0) reads the name, X/Y, map type, worldmap and area; getters return them by map number. The list is a single table of 200 entries, and reading stops at the first missing number (checked in the exe, row [LC1](../research/ledger/data.md#limits-census-arc-data-029)). The map load chain resolves the list entry → in-game map open (0x40FC70) builds the paths → map open (0x40F4E0) sets the current map. List load (0x4125C0), getters (0x40FEE0, 0x40FF40, 0x40FF50, 0x40FF70, 0x40FFE0, 0x410020), in-game open → map open (0x40FC70).

**Dependencies:** a versioned map catalog, a map coordinate system and a source-resource resolver. **Acceptance:** two synthetic map IDs with different area/start entries resolve independently; an invalid ID leaves the current world unchanged. **Steamwoven:** Partial: the [Area Data owner](../world/area-loader.md) authenticates the fixed map5000/runtime1/townmap2 and a finite sector profile. There is no production catalog of all maps, no campaign bootstrap and no general map selection. [PR #46](https://github.com/Simarglok/arcanum-steamwoven/pull/46).

<a id="fr-world-002"></a>

### FR-WORLD-002 — Global coordinates, boundaries and sector address

**Stage:** pre-alpha. **Observable behavior:** a local map point resolves unambiguously to a sector and a local cell; the map's dimensions define the permitted area, and an attempt to go beyond it is rejected.

**Boundaries:** the coordinate codec, dividing the map into cells/sectors and the bounds check. Projection to the screen and collision policy are not defined by this requirement.

**Original:** the sector address (0x4CFC50) computes the sector X/Y by shifting the coordinates by 6 bits, the inverse conversion (0x4CFC90) restores the sector origin; map open (0x40F4E0) sets the location/sector limits from `map.prp`, and sector acquire (0x4D0540) checks these limits (checked in the exe, row [SS2](../research/ledger/world.md#main-map-streaming-arc-data-030)). Caller path: map open → bounds → location → sector lookup. sector coordinate conversion (0x4CFC50, 0x4CFC90), map dimensions and limits (0x40F4E0), map limits set (0x40F4E0), sector bounds (0x4D0540).

**Dependencies:** the original format profile, a verified signedness/overflow policy and unified world coordinates for Simulation and Data. **Acceptance:** synthetic bounds check both sides of each sector edge, including the last permitted and the first non-permitted cell; pack/unpack preserves the ID on the boundary. **Steamwoven:** Partial: Area snapshots have checked sector pack/unpack, origins and distinct world/source/occurrence keys for several sectors; local serialized static and direct mobile coordinates differ. [Area API](../world/area-loader.md). Movement/viewer remain in the bounded rectangle x4..8/y0..3, not a general map runtime.

<a id="fr-world-003"></a>

### FR-WORLD-003 — Loading and eviction of neighboring sectors

**Stage:** pre-alpha. **Observable behavior:** when an actor/camera or an active world query reaches a sector, the needed sectors are available; the cache keeps the records in use, frees the safe unused ones and writes dirty state before eviction.

**Boundaries:** loading by sector ID, refcount/pinning, a bounded cache, prefetch and unload. The exact cache size, the LRU policy or background multithreading are not specified.

**Original** (checked in the exe, rows [SS1–SS5](../research/ledger/world.md#main-map-streaming-arc-data-030)): the precache (0x40FE00) locks/unlocks a 3×3 area around the target point. Sector acquire (0x4D0540) first looks in the 16-sector cache, checks the bounds, then calls the loader; when the cache is full it saves and evicts the least recently used sector without references, and if all have references it refuses. Chain: teleport → 3×3 precache → sector acquire → sector load (0x4D1A30); ordinary consumers also use lock/unlock. prefetch caller (0x40FE00), cache/load/evict (0x4D0540), game loader selection (0x4CEF70).

**Dependencies:** a sector provider, async-safe asset/runtime ownership, a bounded cache and a dirty-save path. **Acceptance:** a synthetic route crosses three sectors; a locked sector is not evicted, an unlocked dirty sector is restored after eviction/reload. **Steamwoven:** Partial: the Area owner synchronously loads nine core and eleven present context sectors, explicitly keeps five absent, owns the sources and revocable selected-frame leases. There is no live refcount/prefetch/eviction, no persistent frame cache and no sector streaming during movement. [API/lifetime](../world/area-loader.md).

<a id="fr-world-004"></a>

### FR-WORLD-004 — World continuity when crossing a sector edge

**Stage:** pre-alpha. **Observable behavior:** crossing a sector boundary continues the same actor movement and world position. An edge between technical sectors does not by itself become a wall, a teleport or a separate map; the visual seam gives no second cell or double event.

**Boundaries:** a single chart/world identity and consistency of actor/tile/object queries near the seam. The rules of passability, the diagonal corner and dynamic obstacles remain separate evidence gates.

**Original:** a single packed map location addresses a sector by `>>6`; the location projection (0x4B8680) takes the same location and projects it, and the PC location update updates the sector-dependent objects. Chain: object position update (0x444270) → sector address (0x4CFC50) → sector-change notification. location projector (0x4B8680), PC location update caller (0x444270), sector address/bounds (0x4CFC50, 0x4CFC90).

**Dependencies:** FR-WORLD-002/003, movement, terrain/object query and future collision admission. **Acceptance:** synthetic adjacent sectors A/B have verifiable markers on the shared edge; the actor passes through coordinates 63→64 without a chart change, a skipped/duplicated cell or a state reset. **Steamwoven:** Partial: the world keys and terrain of neighboring Area sectors are consistent in the Data loader. WORLD003 moves the actor only within one narrow stretch; crossing a sector edge is not integrated. [Area traversal research](../world/area-traversal-contract.md) keeps NOT_READY because of the loaded registry/HP/state, and does not cancel the bounded production profile.

<a id="fr-world-005"></a>

### FR-WORLD-005 — Base sector data and its changes

**Stage:** pre-alpha. **Observable behavior:** the loader assembles a sector from versioned base data and the applicable local changes, without losing the base when a change is absent; dirty terrain/roof/light/script/object components are saved separately for this sector.

**Boundaries:** reading the installed version of `.sec` and the matching `.dif`, the bounds of permitted overlays and their application. Universal archive precedence and the whole object/script runtime are not included here.

**Original:** sector load (0x4D1A30) opens the base and the sector-specific diff; flags select the corresponding change sections; then come the light, tile, roof, script/metadata/sound/object sections (the sources and the section order are checked in the exe, row [SS5](../research/ledger/world.md#main-map-streaming-arc-data-030)). Sector save (0x4D2750) serializes only the changed components (F0). Chain: sector acquire → loader → base `.sec` + optional `.dif`; eviction/flush → saver. base/diff and tile load (0x4D1A30), remaining conditional sections (0x4D1A30), diff save (0x4D2750).

**Dependencies:** a parser for each component, a fixed resource profile, a typed diff policy and an independent version of the source. **Acceptance:** a synthetic base without a diff matches the base; a diff that sets one component changes only it; reopening gives the same aggregate state. **Steamwoven:** Partial: the FILE-effective Area reader reads the sector/static/mobile/prototype/default data with reconciliation; `DIF/PATCH/loose` overlays are not applied and map state is not changed. [Loader boundary](../world/area-loader.md).

<a id="fr-world-006"></a>

### FR-WORLD-006 — Interior and cave topology

**Stage:** pre-alpha infrastructure; several original interiors — later. **Observable behavior:** the authoritative model can represent an entrance as a local transition within the same map identity or as a transition to a different map identity. The "building/cave" content type does not by itself dictate which variant is used.

**Boundaries:** separate sector traversal, same-map local passage and cross-map transfer. The requirement does not declare that a cave is always a separate map, and does not define the artistic/script logic of the entrance.

**Original:** the map field in the teleport request plus the map ID allow requesting a destination; map −1 resolves as the current one (checked in the exe, row [TR4](../research/ledger/world.md#map-transitions-arc-data-025)), and the in-game map open (0x40FC70) is called only for a differing map ID (F0). A jump point record stores the concrete destination map and cell. Chain: endpoint lookup → teleport queue (0x4D3380) → teleport processing (0x4D3460) → the same map or open destination. Teleport request, same-map resolution and conditional map open (0x4D3460), conditional map load (0x4D3460), endpoint fields.

**Unknown:** for the Crash Site cave, the linkage to the original data has been researched ([cave transition](../research/cave-transition-evidence.md)); for the other cave/building entrances it has not been researched, and which original interiors use same-map passage and which a separate map is not determined. **Dependencies:** map identity, a connector model and a format profile. **Acceptance:** two synthetic cases of the same "cave entrance" type — one same-map, the other cross-map — must keep the expected topology without a name-based heuristic. **Steamwoven:** the [RES-015 inventory](../research/area-inventory.md) researched a separate cave map and its records, and the potential endpoint bindings are described in the [area contract](../world/area-contract.md). Data loads the cave under its own pinned profile; the area viewer (ARC-WORLD-009) draws it and switches the scene Crash Site ↔ cave through `AreaTransitionCoordinator` in the viewer Core ([viewer](../rendering/terrain-viewer.md#crash-site-cave-and-return-arc-world-009)). There is no general production topology runtime and no other interiors. The ARC-WORLD-009 research ([cave transition](../research/cave-transition-evidence.md)) showed that the Crash Site cave is a separate map (cross-map), and the entrance and exit are given by two directed literal records in the use-action scripts.

<a id="fr-world-007"></a>

### FR-WORLD-007 — Explicit entrance/exit endpoints and direction

**Stage:** pre-alpha for one connector; a wide network — later. **Observable behavior:** an endpoint sets the source location and the exact destination map/location. A reverse exit exists only if it is explicitly represented as a reverse record or an equivalent two-way contract.

**Boundaries:** the identifier and direction of the link, destination validation, optional flags; it does not implement door animation, dialogue, quest gating or general interaction.

**Original:** the serializable jump point record stores the source cell, the map and the destination cell; the lookup (0x4E3450) looks for an exact match of the source location, with several records on a cell the first wins, flags are not checked (checked in the exe, row [TR5](../research/ledger/world.md#map-transitions-arc-data-025)). Map jump point processing (0x4101D0) passes the endpoint to the teleport. Chain: PC update → map processor → source-cell lookup → teleport destination. Jump point record, lookup (0x4E3450), map processor (0x4101D0).

**Unknown:** the reversibility of the records of specific original doors and cave links without reading the linked data entry; individual endpoints can be one-way. **Dependencies:** content association, stable map IDs, the world coordinate chart and destination validation. **Acceptance:** for a synthetic entrance the exact exit endpoint can be verified; removing the reverse link must make the exit explicitly unavailable, not invent a return. **Steamwoven:** Partial: the Area snapshot publishes both endpoints of the Crash Site cave (entrance 2793 and the explicit reverse record 1532) with the arrival map and cell; the viewer Core checks that the target map is loaded and the arrival cell is admitted, otherwise the endpoint is unavailable ([viewer](../rendering/terrain-viewer.md#crash-site-cave-and-return-arc-world-009)). There is no general connector runtime and no original activation parity; the other pairs remain in the [area contract](../world/area-contract.md).

<a id="fr-world-008"></a>

### FR-WORLD-008 — An endpoint fires by world state, not by the screen

**Stage:** pre-alpha. **Observable behavior:** an endpoint has an explicitly set trigger condition — for example, the actor entering its source cell; the decision is made by simulation coordinates and does not depend on scaling/pixel hit. If the target decision requires a separate use action, it remains a separate explicitly set trigger policy.

**Boundaries:** endpoint detection and exactly one dispatch per permitted entry event; a repeated animation/render update does not duplicate the transition.

**Original:** in the object position update (0x444270), after the PC location update, the chain goes through map jump point processing (0x4101D0); it looks up the exact cell and calls the teleport queue (0x4D3380). The path is checked in the exe (row [TR3](../research/ledger/world.md#map-transitions-arc-data-025): for any PC, while the nested jump point transitions number fewer than 9, flags 0); this is the dispatch path, not a claim about the association of a specific cave. actor update caller (0x444270) → map dispatch (0x4101D0) → exact source-location lookup (0x4E3450).

**Dependencies:** a committed actor world position, a trigger crossing event and a transition coordinator. **Acceptance:** a synthetic trigger on a coordinate fires once on entry at two different render scales; a stationary actor and a repeated frame do not start a second transfer. **Steamwoven:** Partial, for the Crash Site cave only: a use is accepted by the actor's committed cell and the object's cell (Chebyshev ≤ 2, the step toward the object is not blocked); the sprite pixel only selects the object. An accepted use is queued and applied once on a settled step; a repeated request while pending is rejected ([viewer](../rendering/terrain-viewer.md#crash-site-cave-and-return-arc-world-009)). The check at different render scales was not performed; there are no cell-entry triggers. For the Crash Site cave the trigger is the player's use action on the entrance scenery object, not entering a cell ([cave transition](../research/cave-transition-evidence.md#t3--the-trigger-is-a-use-action)), that is, a separate trigger policy. [headless model scope](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/world/grid-movement-runtime.md#L18-L33), [world association boundary](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L13).

<a id="fr-world-009"></a>

### FR-WORLD-009 — Transition between maps as a single lifecycle

**Stage:** pre-alpha for one transition. **Observable behavior:** with a cross-map connector the game closes the old map runtime, opens the target map ID and sets the given destination location; a same-map connector does not destroy and recreate the whole map context.

**Boundaries:** the sequence flush → resolve/open destination → destination actor placement → runtime rebind. The full global campaign save/load is out of scope here.

**Original:** the teleport queue (0x4D3380) puts a request in the queue (a request while the local PC's teleport is pending is rejected, others are replaced; row [TR4](../research/ledger/world.md#map-transitions-arc-data-025)); the update (0x4D3330) calls teleport processing (0x4D3460), which distinguishes the current/destination map and calls the in-game map open (0x40FC70) only when the ID changes; on success it then centers the view on the destination location (F0). The in-game map open flushes the map and builds the base/save paths by map. Chain: trigger → queued teleport → optional map flush/open → landing. queue and ping caller (0x4D3330, 0x4D3380), teleport map branch (0x4D3460), map flush and open paths (0x40FC70).

**Dependencies:** FR-WORLD-001/006/007, state ownership, map load/unload and actor rebinding. **Acceptance:** synthetic A→B→A records map IDs and coordinates; after the transition only the target map is active, and a same-map route keeps the current map-owned references. **Steamwoven:** Partial, only Crash Site ↔ cave in the area viewer: one map is active; the transition changes the scene, the step data, the chart and the movement session, and the ARC-WORLD-008 session state records the actor's map and cell. A→B→A is verified on original data and natively ([viewer](../rendering/terrain-viewer.md#crash-site-cave-and-return-arc-world-009)). Deviation from the original: both maps are loaded in advance and stay in memory (no unload/open on transition); there is no general map lifecycle.

<a id="fr-world-010"></a>

### FR-WORLD-010 — Portable actor links survive a cross-map transfer

**Stage:** later; until then pre-alpha may limit the vertical slice to one actor. **Observable behavior:** transferring the PC keeps the identity and the explicit links of the followers included in the transfer; actor/container-owned payload is not lost and does not create duplicates on a map change.

**Boundaries:** only serialization of the existing transferable entities and relinking. The inventory system, follower AI, combat, quest state and the full object model are not specified here.

**Original** (checked in the exe by two readers, except FM2 and FM9 (F1), rows [FM1–FM14](../research/ledger/social.md#followers-across-maps-arc-res-076), [research](../research/follower-map-transfer.md)): the teleport collects the object, its inventory and the non-waiting companions recursively, onto a single cell; on a map change each object as a whole, with its unchanged ID, is appended to the destination map's `mobile.mdy` as a dynamic one, while the source copy is marked destroyed and its ID goes into the source map's `mobile.des`. The transitional combat state (focus, last attacker, enemy list, flight, combat mode, AI events) is cleared; time events where all objects move are carried over. The only link of an NPC to a map is field 243 (teleport map): a former companion on a foreign map goes home on a forced AI pass. Chain: teleport processing (0x4D3460) → recursive object preparation (0x4D3760) → cross-map serializer (0x4D39A0).

**Dependencies:** stable entity identity, a destination object store, ownership/follower references and atomic transfer. **Acceptance:** a synthetic group passes A→B→A with the same entity IDs, with ownership and inventory references in exactly one instance each. **Steamwoven:** partial (ARC-WORLD-017): in the AREA viewer, Virgil following the PC passes Crash Site → cave → Crash Site with the same entity id, in exactly one instance; his position is map-local session state, recorded together with the PC in one atomic step ([contract](../world/companion-follow-runtime.md#map-change-arc-world-017)). There is no transfer of the inventory payload, of followers of followers, or of other maps. The companion's base record (combat fields, the AI line, the inventory holder, the body) is looked up by consumers among the objects of the displayed map, so on a foreign map he is a "guest" without combat data; since ARC-DEV-072 (MIG1) the single resolver [finds the record by identity](../world/record-resolver.md) (the home map is read on demand), and consumers are being moved over in slices MIG2–MIG5; the systemic gap and the plan of slices V1–V6 are in [ARC-RES-076](../research/follower-map-transfer.md#fix-plan).

<a id="fr-world-011"></a>

### FR-WORLD-011 — Exact arrival and readiness of neighboring sectors

**Stage:** pre-alpha. **Observable behavior:** the actor appears at the given destination coordinate; the origin/follow-camera is centered on it, and the necessary surroundings are ready for the first frame/simulation step.

**Boundaries:** destination location, camera origin and bounded neighboring-sector prefetch. This does not fix the original game's renderer or visual screen match.

**Original:** after the cross-map map open (0x40FC70), teleport processing (0x4D3460) sets the origin to the destination; for the local PC after any map change the precache (0x40FE00, via 0x4EF1E0) is started, which loads 3×3 around the addressed sector (checked in the exe, row [SS4](../research/ledger/world.md#main-map-streaming-arc-data-030)). transition landing and prefetch (0x4D3460), 3×3 prefetch (0x40FE00).

**Dependencies:** FR-WORLD-002/003/009, camera follow policy, destination bounds and sector readiness. **Acceptance:** link arrival puts actor exactly at declared local cell, queries correct destination sector before rendering, and camera follows without snapping to the old map. **Steamwoven:** Partial: the exact movement runtime and WORLD003 separate the committed cell, the continuous anchor and the settled arrival; the camera/sprite do not determine the cell commit. For the Crash Site cave the actor after the transition stands exactly on the authored arrival cell in the new session, and the camera is centered on it before the scene is published ([viewer](../rendering/terrain-viewer.md#crash-site-cave-and-return-arc-world-009)). There is no sector prefetch: the single cave sector is loaded in advance. [Viewer policy](../rendering/terrain-viewer.md#integrated-world-diagnostic-policy).

<a id="fr-world-012"></a>

### FR-WORLD-012 — Explicit return to the exit point

**Stage:** pre-alpha for a verified reverse endpoint; broad return UX — later. **Observable behavior:** the reverse connector returns the actor to the exact source/arrival location defined by the link data; the system does not guess the entrance from geometry and does not reset the arrival location on reopening the map.

**Boundaries:** the reverse link and location continuity. An automatic stack of recent maps is a separate optional policy and is not required here.

**Original:** the jump point stores the destination, the lookup is directed by the source location, and the teleport passes the declared map/location; no automatic return stack was found in the path read. directed endpoint fields, source lookup (0x4E3450), destination passed to teleport (0x4101D0).

**Dependencies:** verified endpoint association and FR-WORLD-007/009/011. **Acceptance:** enter synthetic interior, return through its explicit reverse endpoint and compare map ID/cell/camera context with the authored return cell. **Steamwoven:** Partial, Crash Site cave only: the return goes only by the explicit reverse record 1532 onto the cell (92913,82598); without it the exit object is not a trigger, and the return path is not inferred. The real-data A→B→A compares the map and the cell ([viewer](../rendering/terrain-viewer.md#crash-site-cave-and-return-arc-world-009)); the camera context is not checked.

<a id="fr-world-013"></a>

### FR-WORLD-013 — Changed map state survives leaving and returning

**Stage:** pre-alpha for bounded map-local state; a full saved campaign — later. **Observable behavior:** changes to the sector and map-local entities are available after unloading/reloading the map and are not reset because of a transition into an interior or a neighboring region.

**Boundaries:** dirty sector overlays, dynamic map entities, endpoint edits and a map-specific save namespace. The full saved party/game snapshot, quests, inventory policy and crash-safe save format remain outside the requirement.

**Original:** the in-game map open (0x40FC70) calls the flush of the current map (0x410070); the flush saves the sector, jump points, terrain and town map. Sector save (0x4D2750) writes a diff of only the changed components, and the map loader reads the dynamic mobile state. map flush on transition (0x40FC70), flush component chain (0x410070), sector diff save (0x4D2750), dynamic map entity load (0x411450).

**Dependencies:** FR-WORLD-005/009, stable map identity, typed dirty records and persistence boundary. **Acceptance:** modify one synthetic sector and one map-local entity, travel away and return; both remain changed while untouched neighbor sector is byte/semantic-equivalent to baseline. **Steamwoven:** absent as world state persistence; status marks persistence/save-load future work. Existing immutable probe outputs and local diagnostic exports do not persist gameplay changes. [not implemented boundary](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L23-L25), [research outputs are bounded](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L20-L21).

<a id="fr-world-014"></a>

### FR-WORLD-014 — Transition from the world map to the local world

**Stage:** later. **Observable behavior:** the selected overland destination is converted to a coordinate of the needed map identity; completing the travel returns the actor to the local world context with this coordinate, and does not treat a world map pixel as a sector location.

**Boundaries:** region/area mapping and the travel completion handoff. Full route AI, encounters, weather and the original world map and its interface are not set here.

**Original:** the map catalog carries worldmap/area attributes. The world map window converts the selected coordinates into a map location, and the world map teleport (0x561860) directs the PC to the start map; the state transition passes the selected position into this path. worldmap/area metadata (0x4125C0), world map moving-state handoff (0x5615D0), coordinate conversion and teleport to start map (0x561800, 0x561860).

**Dependencies:** FR-WORLD-001/009/011, world chart, map areas, travel clock and encounter policy. **Acceptance:** from a synthetic area map choose location `(x,y)`; final local position equals the versioned area transform and activates start map, with no screen-scale dependency. **Steamwoven:** partial data-only observation: one selected map catalog/properties route was read, but no worldmap runtime, area binding or travel transition. [single-map data boundary](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/formats/maps.md#L9-L18), [world transitions absent](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L23-L25).

<a id="fr-world-015"></a>

### FR-WORLD-015 — Optional effects and transition time

**Stage:** later. **Observable behavior:** a transition record can set fade, sound/movie, game-time delta and follower policy independently of map topology; a skipped effect does not block the state transfer.

**Boundaries:** only orchestration hooks/flags and ordering. The implementation of audio, movies, rest and the scheduler is outside this domain requirement.

**Original:** the teleport request exposes fade/movie/sound/time and follower flags (the script fade-and-teleport action sets them, checked in the executable, ledger row [TR2](../research/ledger/world.md#map-transitions-arc-data-025)); chain teleport queue (0x4D3380) → ping (0x4D3330) → process (0x4D3460) applies requested effects before map branch and fade-in after arrival (F0). queue and ping caller (0x4D3330, 0x4D3380), transition effect flags, pre-transition effect ordering (0x4D3460), arrival fade-in (0x4D3460).

**Dependencies:** transition coordinator, audio/video services and world clock. **Acceptance:** synthetic transition with each effect flag absent/present preserves identical destination state; only requested effects and declared elapsed time are emitted. **Steamwoven:** absent; display/input labs and dialog runtime do not implement world map transition effects. [project component limits](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L11-L16), [world transitions future](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L23-L25).

<a id="fr-world-016"></a>

### FR-WORLD-016 — Safe transition error boundary

**Stage:** pre-alpha. **Observable behavior:** an unknown map, a missing link, an invalid coordinate or a load error rejects the transfer with the pre-transfer actor/map state available for recovery; the error has a typed reason and does not leave the actor duplicated/lost.

**Boundaries:** validation/commit/rollback for a single transition. This is not a general resilience policy for all loaders or a full transaction system.

**Original:** reading the functions (F0) shows failure points, but not the required rollback: the in-game map open (0x40FC70) first flushes the current map, the map open (0x40F4E0) closes it before checking the catalog/properties; the cross-map teleport serializes the transferable objects before trying to open the target map, and then returns false when the open fails. No explicit rollback was found in this path. flush before destination open (0x40FC70), close before path/properties validation (0x40F4E0), cross-map entity write (0x4D39A0), map-open failure return (0x4D3460).

**Target policy:** check the target/link/location before commit; if the open nevertheless ends in an error, restore the initial authoritative actor/map state or leave it in a deterministic recovery state. Do not turn the original's order into a norm. **Acceptance:** a synthetic loader fails after validation and at each commit stage; no case loses or duplicates the actor, returns a stable failure receipt and keeps the ability to continue from the initial or the declared recovery state. **Steamwoven:** Partial: the [Area owner](../world/area-loader.md) does not replace the publication on a failed load; [WORLD003](../rendering/terrain-viewer.md#integrated-world-diagnostic-policy) rejects Blocked/Unknown/Unsupported atomically. There is no full transfer transaction/rollback.

## Steamwoven coverage up to 2026-10-20

The coverage table of this section has been moved to the [coverage matrix](coverage/world.md): there each requirement has its current status, scope, remainder and documentation (2026-10-20). The paragraphs below described the former table of this document (initial audit 2026-10-09 with later edits) and are kept as history; the words "table", "rows" and "columns" in them refer to it.

The statuses show coverage on the pinned cutoff only. `No` means the component is absent in the checked branch, not a refutation of the original game's behavior. `Partial` is limited by the explanation in the cell. Existing bounded tests count only for the stated local contract. The only completed map transition is Crash Site ↔ cave in the area viewer (ARC-WORLD-009); other transitions and original-data association were not run. Reading the original's functions does not by itself count as a runtime check.

The checks of Data coordinates/neighbor selection and of bounded movement belong to different layers. Area fixtures/load confirm multisector metadata, while the WORLD003 checks confirm movement/rejection only within x4..8/y0..3. Cross-sector actor, connectors and map-transfer rollback are not checked. There are no new runtime runs in the audit; historical candidate-specific receipts are in the git history.

## Source inventory and unchecked branches

**Original — the stated functions were read directly** (not the whole group): map (0x40EA90–0x412C90: map save/load, open, sector prefetch, jump-point dispatch, flush, selected diff/dynamic load, map-list metadata; the addresses are given in the requirements), sector (0x4CEF70–0x4D31C0: init/cache, coordinates/bounds, selected load/save, diff path), location (0x4B8680, location projection), object (0x444270, actor-location caller), teleport (0x4D32D0–0x4D3F30: queue/process, map branch, recursive transfer, effects), jump points (0x4E2FB0–0x4E3890: exact lookup, selected read/write), world map window (0x55F8D0–0x566D10: selected map positioning/state handoff only). All functional claims are tied to the function addresses next to them.

**Steamwoven — read directly:** the checkout-root `AGENTS.md`, `docs/architecture/decisions.md`, `docs/research/method.md`, `docs/project/status.md`, `docs/formats/maps.md`, `docs/world/grid-movement-runtime.md`, `docs/research/cardinal-traversal-evidence.md`. Catalog-status snapshot itself dates through PR #30; repo HEAD also includes PR #35/#36 documents and bounded implementation. In the movement tests only `GeometryTests.cs` 19–64 and `ValidationTests.cs` 58–91 were read directly; the production movement source was found by symbols/path, but its body was not audited here.

**Identified, but the bodies/full caller paths were not researched:** the area functions (0x4CA940–0x4CB6A0), the full travel/route/time-event path of the world map window (0x55F8D0–0x566D10), the teleport op callers in script execution (0x4446E0–0x44C820; the teleport actions themselves are checked in the exe, rows [TR1, TR2](../research/ledger/world.md#map-transitions-arc-data-025)), alternate teleport callers in AI (0x4A8320–0x4AFC70), the event queue (0x45A7C0–0x45CC10), the game library lifecycle (0x4020F0–0x405070) and game initialization (0x4B9B90–0x4B9DB0), plus sector component implementations in the functions of the sector lists and neighboring groups (0x4F1150–0x4F83D0), cells (0x4D6840–0x4D7E90) and terrain (0x4E7B00–0x4E9680). The original map-list/jump-point/cave entries and other extracted game data were not read. Therefore the requirements do not define the concrete cave/interior association, the full overland route policy or all sector component semantics.

**Checks:** source/document review only. No game launch, data extraction, runtime smoke checks or acceptance scenarios were performed; they are proposed as future target checks.

## Notes on the functions read and reliability boundaries

The notes found in the researched chain do not become the target contract:

- Map save (0x40EE50): by reading (F0), a module save error stops the loop, but the function still returns success; this is an open risk to save reliability.
- In-game map open (0x40FC70): by reading (F0), after an object system check error (0x405280) a quit is requested, but execution continues.
- Function 0x40FED0 is empty; its purpose is unknown, so no world behavior can be inferred from its presence.
- Sector save (0x4D2750): by reading (F0), when opening the diff fails, `fclose(NULL)` is called, and the cross-map serializer (0x4D39A0) does not close the file on a write error. This confirms the need for a separate fail-safe policy.

## Open questions and what exactly is confirmed

1. The original data linking specific cave/interior entrances and exits to a map ID, a source cell and a destination cell were not checked. The FR-WORLD-006 requirement sets the model's capability, not an answer about each original cave.
2. In the exe only the ledger rows [TR1–TR5](../research/ledger/world.md#map-transitions-arc-data-025), [SS1–SS8](../research/ledger/world.md#main-map-streaming-arc-data-030) and [LC1](../research/ledger/data.md#limits-census-arc-data-029) are checked; the rest is reading the functions (F0). The presence of functions or arguments does not confirm the actual runtime path on original data.
3. The actual original-data precedence between the base map, PATCH, loose files and sector diffs remains outside the scope of this sample. The research of the selected Steamwoven sector does not directly universalize format/precedence.
4. The map-specific worldmap/area associations and the travel-time/encounter policy for the whole catalog are not resolved.
5. The exact failure/rollback semantics of the original are not reliable enough and must not be copied without a separate decision.

The research was read-only: the game was not launched, data/bytes/assets were not extracted for the report and were not included in it. [Steamwoven research method](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/research/method.md#L1-L5).

# Requirements for the static scene and building entry

**Current status:** the status of each requirement in this document, the implemented scope, the remainder and the documentation are in the [coverage matrix](coverage/scene.md) (rechecked 2026-10-20, ARC-DEV-027). The "Steamwoven" fields below and the summary coverage section give the details as of their last edit (initial audit 2026-10-09, merged main [`a8a553b7`](https://github.com/Simarglok/arcanum-steamwoven/tree/a8a553b7c406ace77ea019bf2b13251f00ecddb6), PR #54); where they disagree, the matrix is right.

ARC-RES-017: [whole-exterior traversal research](../world/area-traversal-contract.md)
for FR-SCENE-012/013/014/016 keeps the physical/visual, state and edge frontiers;
Research/Proposed, NOT_READY, without scene collision or door/window runtime.

Date: 2026-10-02. Initial Steamwoven research baseline: `b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae` (`docs/functional-requirements-20261002`). The original's behaviour is taken from the functions of the 1.0.7.4 executable at the listed addresses. The baseline working tree was clean at the time of the research.

Scope: the scene surface, static objects and trees, walls/buildings, door portals, windows, roofs, their drawing and the movement rules that affect them. Combat conditions, inventory, NPC/AI and map transitions/`jumppoint` are not specified. Physical passage through a door does not mean the building is a separate map: if the data defines a map transition, it belongs to a separate transition contract.

Evidence terms: **Original** — a reading of the original's functions at the listed addresses, level F0 (not checked in the exe), unless the line is in the [verification ledger](../research/original-verification-ledger.md); the named ledger lines are checked in the exe. **Inference/product policy** marks required behaviour inferred for the project but not measured in the original runtime. For all the listed requirements, original visual and gameplay parity remains **not established**. The `prealpha-core` and `later` stages are proposed groupings of requirements, not a task queue and not a readiness estimate.

## Requirements

**ARC-RES-018 — RESEARCH:** the [clock/light contract](../world/time-lighting-contract.md)
separates tile/facade/roof palette paths, emitter tint sources and object/overlay
descriptions from the active light and submitted pixels. A zero stationary count does not
mean there are no object lights; original masks/coordinate/state binding remain
NOT_READY. This is not an implementation of FR-SCENE, a lighting renderer or a cave transition.

ARC-DATA-009: the [complete exterior data owner](../world/area-loader.md)
provides separate terrain/sector/object metadata, all 426 core statics and 88 mobile
body references with explicit dispositions, presence/default provenance and
identity-bound group/frame/palette/placement inputs. This is data-layer coverage of
FR-SCENE-001/002, not submitted draw commands, ordering/visibility, roof/wall
rendering or passability. Active unsupported required composite layers fail
readiness; source-frame decoding does not declare FR-SCENE-001..009 implemented.

Later research ARC-RES-015: the [area inventory](../research/area-inventory.md) accounts for 426 static and 88 core mobile records, separate resource/placement gaps and cave sources; the [area contract](../world/area-contract.md) keeps the flags/order/cache boundaries. This does not extend the production five-only profile and does not declare FR-SCENE-001..009 implemented.

<a id="fr-scene-001"></a>

### FR-SCENE-001 — The scene accounts for each source type separately (`prealpha-core`)

**Behaviour and acceptance.** For a loaded sector, the scene must account separately for the surface, facade/surface drops, ordinary static objects, walls, portals and the roof. For each record the result must be a visible element or an explicit status "not applicable", "not supported" or "not allowed"; the record count by itself is not the count of drawn or passability-checked elements. Acceptance compares the independent counts of records, resolved resources, computed placements, derived commands and passability contributions; an unknown resource is not turned into a skip without a status.

**Original and data.** The original loads roofs and objects as separate sections (sector section loading (0x4D1530, editor variant), game sector loading (0x4D1A30); the section sources and order are checked in the exe, line [SS5](../research/ledger/world.md#main-map-streaming-arc-data-030)); the game draw pipeline calls surfaces, objects and roofs through separate paths (game draw (0x4046F0)); roofs are drawn after all objects (line [RF2](../research/ledger/world.md#roofs-arc-render-021)). Data: sectional tile/roof/object records, the object type, prototypes, resource mapping and the ART frame. The priority of DIF/PATCH/loose files is not established by this reading.

**Steamwoven now.** [DATA009](../world/area-loader.md) keeps core/context terrain, static/mobile occurrences, dispositions and visual chains separately: 36,864 core cells, 426 statics, 88 core mobiles. An inventory-insertion refusal does not automatically allow drawing the body. The viewer still uses one sector/five statics; the full-area renderer is in progress, not on the audit main. [PR #46](https://github.com/Simarglok/arcanum-steamwoven/pull/46).

<a id="fr-scene-002"></a>

### FR-SCENE-002 — Each static object gets a verifiable chain to the frame (`prealpha-core`)

**Behaviour and acceptance.** For each ordinary static object in the selected area, the scene must link the instance to the current ART, mapping, resource identity, group/frame and palette. An error or ambiguity must be a visible diagnostic with the source index, not a fallback picture. Acceptance for a sector with all its static objects shows what share of the chains is resolved and does not declare the scene complete while records are uncovered.

**Original and data.** Object drawing (0x43B390) reads `OBJ_F_CURRENT_AID`, then prepares the ART blit (0x443170; the blend choice is checked in the exe, line [BL1](../research/ledger/world.md#rendering-body-blends-and-wading-arc-render-018)); the sector loader puts objects into its lists (0x4D1A30). Required: the instance/prototype current ART, the selected mapping and a resource archive with a valid group/frame/palette.

**Steamwoven now.** The Area API resolves all 2,444 required selected terrain/object chains of the declared original profile, actual defaults, the physical frame selection and identity-bound leases. The five-only `StaticSceneryScenePlan` remains a separate frozen regression; general original resource precedence is not implemented. [API](../world/area-loader.md).

<a id="fr-scene-003"></a>

### FR-SCENE-003 — Trees and large vegetation are not lost because of a bounded profile (`prealpha-core`)

**Behaviour and acceptance.** If a sector contains trees or other vegetation as static records, each record must go through the same full resource-resolution and visible-placement path as furniture and small objects. Acceptance includes a real selected vegetation object only after its identity is confirmed in local data; the result must show the trunk/crown with the matching ART frames or name the specific unresolved resource. A crown overhanging several cells must not automatically become a physical obstacle of the same width.

**Original and data.** The original draws the sector's typed records through the common object drawing and computes the frame/rectangle from the instance and ART (object drawing (0x43B390), frame geometry (0x443620)). The conclusion that vegetation should be covered by the common static pipeline and that the visible crown does not define the collision footprint is **product policy**; a separate classification of trees and its runtime behaviour are not confirmed here.

**Steamwoven now.** Vegetation/static chains are already part of the full core Area snapshot and are reconciled against the independent original inventory. This is data coverage of the exterior area, without a general tree class rule or display of all vegetation in the viewer. The remaining static records should no longer be considered unstudied merely because of the five-only scene API. [Area inventory](../research/area-inventory.md), [Data loader](../world/area-loader.md).

<a id="fr-scene-004"></a>

### FR-SCENE-004 — Static images are tied to the coordinate and the frame hotspot (`prealpha-core`)

**Behaviour and acceptance.** The frame position must account for the instance coordinate, the direct instance offsets and the hotspot/size of the selected frame; ART offsets must remain separate data and be applied only by a proven rule. Acceptance uses synthetic large and small sprites with different hotspots and offsets: the same cell with different instance offsets gives different anchors, and a missing required offset gives an explicit refusal.

**Original and data.** In object drawing (0x43B390), the screen projection of the location is supplemented by `OBJ_F_OFFSET_X/Y` and `+40,+20`; function 0x443620 receives the frame hotspot/size and builds the rectangle (coordinate base (0x43B390), frame geometry (0x443620)). These functions are called from the object drawing path. The retail interpretation of all formats/types remains unchecked.

**Steamwoven now.** Area visual descriptors keep signed hotspots/ART/object offsets, dimensions, scale and mirroring. The diagnostic projection/hotspot rule does not prove the general original placement. The viewer places five statics and HumanFixed, with bounded historical metadata/raster checks. [Area API](../world/area-loader.md), [viewer](../rendering/terrain-viewer.md).

<a id="fr-scene-005"></a>

### FR-SCENE-005 — The surface stays under objects in the proven layer (`prealpha-core`)

**Behaviour and acceptance.** The base ground and its visual drops must stay under objects; their footprint and screen projection do not depend on the camera and viewport size. Acceptance on a synthetic cell field checks the joint of a cell, a facade edge and a large static sprite under pan/zoom without changing world coordinates.

**Original and data.** In the game draw pipeline, cell drawing (0x4D6950) is called before object drawing (0x43B390) (game draw, order (0x4046F0)). Facade drawing (0x4C9E70) appears in the editor draw path (0x404740), not in the shown normal-game call; therefore the exact normal-runtime facade behaviour is not confirmed from this call site.

**Steamwoven now.** Area Data covers tile/facade of the selected core/context sectors; the viewer keeps the bounded single-sector layer policy. Full-area rendering, normal runtime facade/visibility and pixel parity do not follow from the full Data snapshot. [Area API](../world/area-loader.md).

<a id="fr-scene-006"></a>

### FR-SCENE-006 — Overlap uses a consistent depth order (`prealpha-core`)

**Behaviour and acceptance.** The surface, static objects, actors, walls and roofs must be drawn in a consistent order so that a large object can overlap and be overlapped by neighboring geometry according to its anchor/layer. Acceptance uses a synthetic reproducible scene with two overlapping trees, an actor and a wall; this is not a claim that trees are among the five confirmed original-data objects; the result keeps the order on a repeated frame and on camera movement.

**Original and data.** Object drawing (0x43B390) distributes blits over the underlay/flat/shadow/non-flat/overlay order ranges, and the deferred blit flush (0x443560) sorts the queue; the roof is drawn after all objects (checked in the exe, line [RF2](../research/ledger/world.md#roofs-arc-render-021)) (object groups (0x43B390), enqueue/sort/flush (0x443440, 0x443560, 0x443600), common draw pipeline (0x4046F0)). The source rectangle of a scaled sprite is rounded to the nearest (line [BL4](../research/ledger/world.md#rendering-body-blends-and-wading-arc-render-018)); possible dirty-rectangle artifacts for scaled sprites were not checked in the exe.

**Steamwoven now.** The viewer has a finite diagnostic terrain/facade/statics/actor ordering, and Area issues descriptors for a future renderer. Since ARC-RENDER-011 the actor in `area` stands at the centre of the diamond of its cell, like all bodies (it used to be drawn 40 px to the left), and cave walls take part in the common upright order and become semi-transparent in front of the player by the original wall check. General depth/occlusion, the roof and the normal original z-order are not implemented. [Viewer policy](../rendering/terrain-viewer.md#integrated-world-diagnostic-policy), [actor anchor and wall fade](../rendering/terrain-viewer.md#actor-anchor-and-wall-fade-arc-render-011), [Data API](../world/area-loader.md).

<a id="fr-scene-007"></a>

### FR-SCENE-007 — Walls and building parts form connected geometry (`prealpha-core`)

**Behaviour and acceptance.** Adjacent wall segments must assemble into the right edges, corners, joints and openings according to their orientation and piece data; a single zoom change must not turn a wall into independent gaps. Acceptance covers straight walls, a corner, a junction and a door/window in each supported direction, and an unsupported combination gets an explicit status.

**Original and data.** In the wall functions (0x4DF390–0x4E2C50), the joint geometry changes the p-piece by neighboring objects (0x4E2C50); the rotation computation in the exe is not checked (F0), and the editor branches (0x4DF500, 0x4E1490) were not fully read. Besides, wall drawing (0x4DF500) is called only for the top-down view, whereas the ordinary game scene calls object drawing (wall piece/rotation (0x4E2C50), game draw (0x4046F0)). Required: wall object fields, the current wall ART selector, rotation and neighboring wall instances.

**Steamwoven now.** Data loads the Crash Site cave's wall objects and selects their wall ART ([cave profile](../world/area-loader.md#crash-site-cave-profile)); the scene plan places a wall by hotspot and flip, the step blocks the cell edge; the viewer draws the cave walls, and walls in front of the player fade by the original wall check (ARC-RENDER-011, [description](../rendering/terrain-viewer.md#actor-anchor-and-wall-fade-arc-render-011)). There is no recomputation of piece joints (p-piece) and no connectivity checks. The static object loader is limited to the ordinary scenery subsystem and does not replace wall/portal records. [Static loader boundary](../world/static-object-loader.md#scope).

<a id="fr-scene-008"></a>

### FR-SCENE-008 — A door/portal is drawn from its own data and state (`prealpha-core`)

**Behaviour and acceptance.** A door must visually occupy the corresponding opening, show the selected state/frame and be tied to the same physical object that movement checks. Acceptance switches closed and open synthetic doors and verifies that the visible frame belongs to the portal's current state, not to a scenery fallback.

**Original and data.** The portal openness check (0x4F08C0) extracts the state from the ART frame; the portal toggle (0x4F08F0) uses open/close animations. Object drawing (0x43B390) draws the object's current ART in the common game pass. The data include separate portal fields, the current ART/frame and the opening coordinate.

**Steamwoven now.** Separate portal records and a state-aware draw pipeline are not loaded; the five ART resource chains belong to the finite static-scene diagnostic profile. Door parity is not claimed.

**Research.** [ARC-RES-029](../research/first-building-evidence.md): the first house has one door (ART number 55, 7 frames, open at frame ≠ 0) and four windows with one frame; the portal fields, mirroring and the path to ART are described from a reading of the original's functions and from data. There is no implementation.

<a id="fr-scene-009"></a>

### FR-SCENE-009 — A window is displayed in the context of a wall opening (`later`)

**Behaviour and acceptance.** A window represents a separate wall-piece context and a consistent visible opening geometry. Pixel transparency does not mean physical passability. Acceptance uses a synthetic wall opening with a window and checks its anchor/layer/overlap. Capability-dependent crossing and edge assessment belong to FR-SCENE-016 and FR-NAV-003; their acceptance is not repeated here.

**Original and data.** The passage cost calculation (0x43FE00) recognizes a subset of wall p-pieces as windows and reports a window flag (the window parts and who can jump are checked in the exe, line [W1, W2, W7](../research/ledger/world.md#world-movement-and-data-arc-res-047-arc-res-029-arc-res-034-arc-data-021-clock)); traversal flags and the actor type affect blocking (wall/window branch (0x43FE00), capability flags in the caller (0x425760, 0x4257E0)). A separate general window pixel/alpha model does not follow from these functions. Acceptance here is a proposed synthetic fixture; the selected original window piece and the ART/resource association are not yet established. It is not a confirmed render of the original window.

**Steamwoven now.** There is no wall/window data path and no actor-capability traversal for windows. The static obstruction contract explicitly excludes the wall/window/portal branches and does not treat unrecognized records as passable. [Component boundaries](../world/static-obstruction-components.md).

<a id="fr-scene-010"></a>

### FR-SCENE-010 — The roof is read, drawn and keeps its piece topology (`prealpha-core`)

**Behaviour and acceptance.** The roof must be built from sector roof data with its piece, fill/fade state and frames, not from a static object with similar pixels. Acceptance covers an assembled roof section with inner and outer corners, filled and unfilled parts; an unknown roof is not replaced by an invented sprite.

**Original and data.** The original loads roofs as a separate sector section; roof drawing (0x439140) selects records on a 4-cell grid and blits the roof ART, including the piece-specific fade alpha (roof load (0x4D1A30), draw and piece fade (0x439140); the grid, drawing and fade are checked in the exe, lines [RF1, RF2](../research/ledger/world.md#roofs-arc-render-021)).

**Steamwoven now.** For the selected section only the roof section framing and an empty discriminator are recorded; there is no roof data model, ART-resolution chain, roof renderer or synthetic roof piece matrix. [Scenery placement](../research/scenery-placement.md), [loader boundary](../world/static-object-loader.md#scope).

**Research.** [ARC-RES-029](../research/first-building-evidence.md): the first house's roof is 9 of 256 slots (a 3×3 block of 4×4 cells, ART number 7, all nine kinds of rectangle pieces), a piece = a frame + a mirror flag; fill/fade are reset in the data. There is no implementation.

**Persistence limit.** The Roof DIF API is present, but by a reading of the functions (F0) the load/save bodies are empty and the changed flag returns false (0x4F7FC0, 0x4F7FD0, 0x4F7FE0). Piece-topology rendering does not prove support for a changed roof in DIF/save; the original behaviour remains unknown.

<a id="fr-scene-011"></a>

### FR-SCENE-011 — The roof hides/fades over the interior area without losing the hit/occlusion context (`prealpha-core`)

**Behaviour and acceptance.** When an actor is under a covering roof, the scene shows the interior enough that the actor and relevant objects stay visible and selectable; after the exit the roof returns to its original state. Acceptance walks an actor across the roof coverage boundary in both directions, checking the disappearance/fade without a gap through neighboring exterior segments and without the disappearance of an interior wall/door.

**Original and data.** The roof coverage check (0x43A030) accounts for the tile type, roof piece, fill and fade (line [RF4](../research/ledger/world.md#roofs-arc-render-021)); roof drawing (0x439140) applies the piece-specific alpha (RF2), and object drawing (0x43B390) checks roof coverage/fade before outputting objects (coverage (0x439FF0, 0x43A030), objects and roof checks (0x43B390)). The wall check turns the roof fade on at the first count of its cell and turns it off when the last is released (count (0x438720), clear (0x438A50); line [RF9](../research/ledger/world.md#roofs-arc-render-021)). The exact visual result was not checked.

**Steamwoven now.** There is no roof coverage, fade, click-through or linking of roof state to an actor; the existing renderer draws only a finite diagnostic scene. Neither the roof nor occlusion was tested on the original runtime.

**Research.** [ARC-RES-029](../research/first-building-evidence.md): the coverage rule with a +3 shift covers 36 of the 81 interior cells; on any interior cell the roof hides entirely, and the first step outside returns it; the fade is tied to the wall fade. This is a retelling of a reading of the original's functions (the coverage and fill rules were later checked in the exe, lines [RF3–RF5](../research/ledger/world.md#roofs-arc-render-021)) and a computation from data, not runtime.

<a id="fr-scene-012"></a>

### FR-SCENE-012 — The visual area does not substitute for a physical blocker (`prealpha-core`)

**Behaviour and acceptance.** Sprite geometry, cell membership and the contribution to movement must remain separate quantities. A large crown may cover neighboring pixels, while the scenery blocker must be determined by a recognized object/terrain/wall/portal rule. Acceptance checks the picture, the destination cell and the directed step separately; a `NoBlock` of one component is not turned into a general `Open`.

**Original and data.** The original separates the visual frame rectangle (object drawing, 0x43B390) and traversal: the cell blocking check (0x4D7110) separately uses the block mask and tile-art properties, and the passage cost calculation (0x43FE00) handles the scenery, wall and portal branches (tile blocking (0x4D7110), object traversal (0x43FD70, 0x43FDC0, 0x43FE00)). Required: tile masks/names/facade, direct/prototype object flags, destination/edge context, wall/portal state and actor capability.

**Steamwoven now.** The owned [Data assessment](../world/cardinal-traversal-assessment.md) and WORLD003 use physical contributors, not the sprite extent. In x4..8/y0..3 and the isolated context, the cardinal and opt-in four-object-leg diagonal assessments are covered. This is a bounded production admission, not a general IsWalkable, a live world registry or a full-area permission. [PR #38](https://github.com/Simarglok/arcanum-steamwoven/pull/38), [PR #44](https://github.com/Simarglok/arcanum-steamwoven/pull/44).

<a id="fr-scene-013"></a>

### FR-SCENE-013 — Scene obstacles are connected to the directed-step assessment (`prealpha-core`)

**Behaviour and acceptance.** The displayed terrain, scenery and wall/portal objects pass the recognized obstacle data to the directed-step consumer. With the same world state, the visible obstacle and the assessment refer to the same entity/edge; the camera and sprite bounds do not change the verdict. Acceptance checks this link on synthetic blockers/non-blockers and a wall, including a changed state. The full route, bypass and step execution belong to FR-NAV-002/004; this is a requirement for scene-to-navigation integration, not a second pathfinding contract.

**Original and data.** Pathfinding (0x41F3C0) checks the blocking tile and the scenery/critters in the target cell; the animation step caller separately checks the cell blocking (0x4D7110) and the object passage blocking (0x43FD70) (path check (0x41F3C0), step check (0x425760)). This is a reading of the functions (F0; the diagonal step of four cardinal parts is checked in the exe, line [LV3](../research/ledger/world.md#leaving-a-building-through-a-locked-portal-arc-res-049)); the full movement/navigation algorithm is not specified here.

**Steamwoven now.** WORLD003 links the owned Data assessment with the exact MovementRuntime and the viewer; a permitted step starts only with a matching Supported/Open. The final terrain and the four role-labelled object legs are accounted for at a map diagonal; a blocked shoulder refuses. The viewer no longer uses the free/collision-not-evaluated integrator. Scope x4..8/y0..3; dynamic doors/registry/HP/full-area traversal are not covered. [Viewer](../rendering/terrain-viewer.md#integrated-world-diagnostic-policy), [whole-area frontier](../world/area-traversal-contract.md).

<a id="fr-scene-014"></a>

### FR-SCENE-014 — Opening and closing a door are consistent with passability (`prealpha-core`)

**Behaviour and acceptance.** For an ordinary accessible portal, the closed state obstructs the step; a permitted action opens the door, the state/frame changes, and the next step through the opening accounts for this state. Closing changes traversal again. A blockage for which there is no permitted opening keeps its blocker status. Acceptance checks the closed passage, the opening, the passage through the opening and the re-closing separately from the visual animation.

**Original and data.** The openness check (0x4F08C0) and the portal toggle (0x4F08F0) implement the state/frame transition; wall/portal traversal lets an open door through and checks whether an attempt to open a closed one is possible (the order of the open-attempt checks and leaving a building through a locked portal are checked in the exe, lines [LV2–LV5](../research/ledger/world.md#leaving-a-building-through-a-locked-portal-arc-res-049)) (portal state (0x4F08C0, 0x4F08F0), traversal caller (0x43FE00)). Required inputs: portal flags/state, current ART frame, orientation, actor capability, and directed step.

**Steamwoven now.** There are no portal records, open/close animation, capability-aware door action or passability integration. The static `Blocker/NoBlock` component explicitly excludes portals and cannot answer this step. [Static contract](../world/static-obstruction-components.md).

**Research.** [ARC-RES-029](../research/first-building-evidence.md): a closed door blocks its edge; the player enters by day, at night the door is locked (no key, difficulty 25), the exit is always open; the windows are jammed. There is no implementation.

<a id="fr-scene-015"></a>

### FR-SCENE-015 — Entering a building does not require an implicit map transition (`prealpha-core`)

**Behaviour and acceptance.** After a permitted opening of a physical door, an actor can step from the exterior cell to the interior side of the wall opening within the currently loaded scene. Roof coverage/fade and visibility are updated by the interior position. A map change happens only when there is a separate, explicitly permitted transition in the data; the building geometry itself does not create a `jumppoint`.

**Original and data.** The original links the portal state with traversal on the adjacent directed step (passage cost calculation (0x43FE00), animation step check (0x425760); checked in the exe, lines [LV3, LV4](../research/ledger/world.md#leaving-a-building-through-a-locked-portal-arc-res-049)). This local coupling does not prove that a specific interior is on the same map; the map and its separate transition must be defined by map/sector data.

**Steamwoven now.** There are no walls, doors, interior scene, roof visibility or actor collision integration; the current WORLD diagnostic mode is limited to one 64×64 sector and direct movement. Map/jumppoint transitions are not covered here.

**Research.** [ARC-RES-029](../research/first-building-evidence.md): the first house's interior lies on the same map and in the same sector, there are no jump points or teleports, and entry is only through the door edge.

<a id="fr-scene-016"></a>

### FR-SCENE-016 — Window crossing is a separate variant of wall traversal (`later`)

**Behaviour and acceptance.** If the project supports passage through a window, it must differ from ordinary passage through a door and depend on explicitly declared window state and actor capabilities. An ordinary actor does not pass through a wall merely because its texture is transparent. Acceptance compares the same window edge under different actor capabilities and separately checks an ordinary doorway.

**Original and data.** The original checks a limited set of wall p-pieces and sets a window flag (line [W1, W2, W7](../research/ledger/world.md#world-movement-and-data-arc-res-047-arc-res-029-arc-res-034-arc-data-021-clock)); traversal flags/actor capability change the outcome (window in the wall branch (0x43FE00), caller flags (0x4257E0)). This does not define a visual alpha rule, destructibility or the exact retail policy.

**Steamwoven now.** ARC-WORLD-021: on whole-map evidence, a cardinal step through a window part of a wall is open as `WINDOW_JUMP` if the window portal and the rest do not decide otherwise; a diagonal is blocked; a route pays +50; the viewer gives the step a time and a jump animation (vault). Only for a human actor (PC); the ability of other actors to jump is not checked; visible in map mode (two interiors with windows). [Gait runtime](../world/gait-runtime.md#window-jump-arc-world-021).

## Key coverage gaps up to 2026-10-20

- The complete exterior Data owner resolves terrain/static/mobile body chains, but the viewer still draws a finite sector/five statics. The full-area renderer is a separate delivery; Data-ready does not equal draw/visibility-ready.
- WORLD003 already connects the bounded original-data step assessment to the runtime/actor. Its 20-cell rectangle and isolated context do not close the whole-exterior registry, HP/state, cross-sector permission or route search.
- Wall/portal/window/roof gameplay and the normal original depth/occlusion are not implemented. The absence of such active composite references in the selected original Area profile does not prove support for their formats.
- Function branches not fully read and original-runtime parity remain limitations of the initial research; the historical viewer/readback checks below do not lift them.

## Steamwoven coverage up to 2026-10-20

The coverage table of this section was moved to the [coverage matrix](coverage/scene.md): there each requirement has its current status, scope, remainder and documentation (2026-10-20). The paragraphs below described the former table of this document (the initial audit of 2026-10-09 with later edits) and are kept as history; the words "table", "rows" and "columns" in them refer to it.

The statuses refer only to the pinned Steamwoven source snapshot. Checking source data, synthetic fixtures and reviewing code are not equal to checking the original runtime. "Integration" means connection to the scene/simulation, not the presence of a separate diagnostic API.

For detailed boundaries and evidence see the corresponding FRs above: [loader](../world/static-object-loader.md), [scenery placement](../research/scenery-placement.md), [static obstruction evidence](../research/static-obstruction-evidence.md), [four directed cardinal observations](../research/cardinal-traversal-evidence.md) and the [movement catalogue](../project/status.md). The status of one component is not added to another: scene data, resource completeness, the blocker and the full runtime verdict remain different dimensions.

The research does not store game bytes, texts, images or local paths. For the provenance approach and the verification levels see the [research method](../research/method.md); for the project's accepted direction see the [architecture decisions](../architecture/decisions.md).

## Boundary of the original function review

Below are the functions and branches actually reviewed for this report. This is a reading of the relevant fragments, not a full audit of each group of functions.

| Reviewed functions and branches | What was checked |
| --- | --- |
| Game (0x4046F0) and editor (0x404740) drawing | The tile/object/roof call order and the difference of the editor draw path. |
| Object drawing (0x43B390); rectangle/order helpers (0x443440, 0x443560, 0x443600, 0x443620); traversal (0x43FD70, 0x43FDC0, 0x43FE00) | The object render path, anchor/hotspot/order and the start of wall/portal traversal up to the window branch. |
| Sector section loading (0x4D1530, editor variant); game loading with roof DIF (0x4D1A30) | Separate loading of tile/roof/object sections and the roof DIF branch. |
| Cell drawing (0x4D6950); cell blocking (0x4D7110) | The draw path choice and the separate terrain/block-mask check. |
| Roof drawing (0x439140); hit/coverage/fade (0x439890, 0x439FA0, 0x439FF0, 0x43A030) | The visible roof draw/hit/coverage/fade functions; checked in the exe, lines [RF1–RF10](../research/ledger/world.md#roofs-arc-render-021). |
| Wall drawing (0x4DF500), function 0x4E1490; pieces/neighbors (0x4E2C50), wall-piece helpers | The editor/top-down drawing boundary, helpers not fully read, neighboring pieces and the unresolved rotation question. |
| Portals: breaking (0x4F06E0), removal (0x4F0790), openness check (0x4F08C0), toggle (0x4F08F0) | The busted/open frame state and the portal open/close calls. |
| Animation: step (0x425760), capability flags (0x4257E0); door toggles (0x42B640, 0x42B6F0) | The directed step gate, portal/window capability flags and door animation operations. |
| Pathfinding (0x41F3C0) | The early destination tile/scenery checks in the ordinary path creation branch. |
| Wall check: roof count (0x438720), reset (0x438A50) | The refcounted roof fade on/off calls. |

The inventory includes the changed-flag, load and save functions with the difference of the sector roof list (0x4F7FC0, 0x4F7FD0, 0x4F7FE0); the rest of the roof section serializer was not audited.

The implementations not studied in this domain include the facade renderer, object sector indexing, the tile section serializer and the functions of the TIG ART library for low-level frame/blend behaviour. Also, the remaining part of the passage cost calculation (0x43FE00) after the window branch was not reviewed, including the opposing/additional object branches (its portal parts were later checked in the exe, line [LV3](../research/ledger/world.md#leaving-a-building-through-a-locked-portal-arc-res-049)). Therefore the report does not define the full format of these subsystems, all rotation/window/destroyed-state cases, the resource priority or the retail runtime behaviour.

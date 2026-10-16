# Whole-core T0 step assessment

Status: **Implemented Data component**, stage 1 of the
[closure research](../research/area-passability-closure.md#proposed-implementation-not-authorized).
Stage 2 connects it to the `crash-site-area` viewer through `AreaGridSession`
([viewer](../rendering/crash-site-area-viewer.md#one-chart-separate-visual-and-motion-domains)). The
[production cardinal assessment](cardinal-traversal-assessment.md) and its
x4..8/y0..3 profile are unchanged. Original executable parity remains
**NOT_VERIFIED**.

## Scope and owner decisions

- **Admitted area**: the nine Crash Site core sectors 1451..1453 × 1289..1291
  (36,864 cells), or for the cave profile its one sector (32,31) (4,096 cells).
  The exterior boundary is an owner decision of 2026-10-15. A step whose
  source, destination or diagonal shoulder leaves the admitted sectors is
  `Unsupported`, like a map edge. The context ring and the walkable exit south
  of it are not admitted.
- **State**: the new-game initial state T0 of the research: objects and critter
  life as loaded, before any time event, AI tick, script or save. Treating NPC
  positions and life as fixed at T0 is project policy until this project has an
  NPC runtime.
- **Actor**: the same declared diagnostic context as the cardinal assessment
  (`ISOLATED_PC_CRITTER_ART_NONPOLYMORPH`, kind 15, ART class 2, ordinary ground,
  no polymorph, editor or supplemental registry). `PATH_FLAG_0x1000` returns
  `Unknown` and never falls back.

## API

```csharp
AreaStepSnapshot AreaStepInspector.ValidateOriginalData(AreaSnapshot area,
    string modulePath, string tileNameResourcePath, string effectResourcePath,
    string patchArchivePath, string mobilePath, long evidenceEpoch);
AreaStepSnapshot AreaStepInspector.Inspect(AreaSnapshot area, DatArchive module,
    DatArchive tileNameArchive, DatArchive effectArchive, DatArchive? patchArchive,
    ReadOnlyMemory<byte> mobileFile, long evidenceEpoch);
AreaStepRequest AreaStepSnapshot.Request(AreaStepCell from, AreaStepCell to, CardinalTraversalDirection direction);
AreaStepAssessment AreaStepSnapshot.Assess(AreaStepRequest request);
```

Types live in `Arcanum.Data.Traversal`. Cells are map coordinates
(`AreaStepCell(long X, long Y)`). Coverage, disposition, direction and context
reuse the cardinal enums and context class. Both factories require a
snapshot that its owner published as strict-complete (`LoadStrict`); an
inspected snapshot with issues is refused. The original factory also requires
the original profile (`arc-data-009-complete-file-effective-source-v1`) with
exactly the nine core sectors, or the cave profile
(`arc-world-009-cave-file-effective-source-v1`, ARC-WORLD-009) with exactly its
one sector (32,31) and the cave's game-generated object file as the mobile file.
It reads these resources from the named
archives and requires each to match its pinned payload identity:

| Resource | Archive (original installation) | Bytes | SHA-256 |
| --- | --- | --- | --- |
| `art/tile/tilename.mes` | resource2 (`arcanum2.dat`) | 2,999 | `1edd402a3d7b786c01e0500f1534c76525cae1cd83fc67df274afd036d6a9627` |
| `rules/effect.mes` | resource3 (`arcanum3.dat`) | 17,497 | `8ac43304c1e3ff2b351124acb7403c5de79165963f6ea0a83fa4c4d3bdb0b6b2` |
| `rules/gameeffect.mes` | module (`modules/Arcanum.dat`) | 417 | `f8c921b72da9b004aedf9a34c8a1d4f6bbaa86abd1168d234cd811b66ead0a85` |
| `rules/map.patch` | module patch (`modules/Arcanum.PATCH0`) | 5,779 | `2bed0dfca7dc6620c84a1f5aea286a392e98adf6de9f7b6296fcc2d5a3cadf28` |

Every sector payload is re-hashed against the Area snapshot, and every core
mobile record is re-hashed in the supplied mobile file. Archive, PATCH and
loose-file precedence is not inferred: for this pinned profile the research
showed each resource occurs once. The structural factory accepts synthetic
archives; its patch archive is optional, and `rules/gameeffect.mes` is optional
in the module as in the source.

## Evidence and composition

Construction evaluates every admitted cell and every registered object located
in it, then owns the results (no source retention, no query-time I/O). The
evidence digest covers every Area input identity, the mobile file and rule
payload identities, admitted sectors, every terrain verdict and every object
verdict. A request must carry the snapshot's profile,
digest and epoch.

- **Destination terrain** (research F1, F2): a set serialized mask bit blocks;
  a sector of version below `0xAA0004` has a zeroed mask; a facade blocks when
  raw bit 0 is clear; a class-0 tile uses the tile-name flags (first `/`, flags
  to the first space, `b`/`f` block). Mask/version disagreement, an invalid
  tile-name table, an unresolved material index and other art classes are
  `Unknown` gaps.
- **Objects** (F4, F5, F6): inventory, destroyed/off and item types are retained
  as `NotApplicable`. Containers, scenery, projectiles and traps block unless
  `OF_NO_BLOCK` is set. An NPC blocks while alive; life uses the exact or
  interval HP rule, and effect grammar outside add/percent/min/max, an
  out-of-range effect index, a max-HP effect or an interval straddling zero are
  `Unknown`. Portals, PC records, unresolved flags or stats and any
  mobile named by `map.patch` (in either GUID byte order, or when the patch has
  an unsupported line) are `Unknown`.
- **Walls** ([cave research](../research/cave-transition-evidence.md) T7): a
  wall is retained as `NotApplicable` (`WALL_EDGE_COMPONENT`) and registered on
  one tile edge, its art rotation rounded up to odd (1 x−1, 3 y+1, 5 x+1, 7 y−1).
  It never blocks its own tile. A destroyed/off wall is not listed, nor is a wall
  whose own spell flags (slot 19) carry pass-wall (0x100000), which the source
  traversal skips; unresolved spell flags are `Unknown` (`WALL_SPELL_FLAGS_UNRESOLVED`)
  and a wall whose art is not class 1 is `Unknown` (`WALL_ART_UNRESOLVED`). An
  Unknown wall or portal on the source tile is a gap for every step leaving it.
- **Step** (F7): destination terrain first; then wall edges; then destination
  objects; for a map diagonal also the objects on both shoulders. A cardinal step
  crosses one edge: it is blocked by a wall on the source tile on that edge or on
  the destination tile on the opposite edge. A diagonal step checks both
  L-shaped paths (counter-clockwise leg then clockwise leg, and clockwise then
  counter-clockwise), four legs, and any blocked leg blocks; `WallBlocker` names
  the first wall found. Window/opening pieces are never guessed: crossing one is
  an `Unknown` gap (`WALL_OPENING_PIECE_UNSUPPORTED`). Shoulder terrain is never
  consulted. A blocker returns `Supported/Blocked` and keeps unrelated gaps.
  Any remaining gap returns `Unknown/null`. Only complete non-blocking evidence
  returns `Supported/Open`.

Shoulder object roles are named `CounterClockwiseShoulder` and
`ClockwiseShoulder` after the source's rotation order; they correspond to the
cardinal API's object-only shoulder legs.

Terrain evidence codes: `SERIALIZED_BLOCK_MASK`, `FACADE_WALKABILITY_BIT`,
`TILE_NAME_FLAGS` (supported); `MASK_VERSION_MISMATCH`,
`TILE_NAME_TABLE_UNSUPPORTED`, `TILE_NAME_MAPPING_UNRESOLVED`,
`TILE_ART_CLASS_UNSUPPORTED` (Unknown). Object evidence codes: `OBJECT_BLOCKS`,
`NO_BLOCK_FLAG`, `CRITTER_ALIVE`, `CRITTER_DEAD` (applicable);
`INVENTORY_NOT_LISTED`, `DESTROYED_OR_OFF_NOT_LISTED`, `ITEM_TYPE_NOT_QUERIED`,
`WALL_EDGE_COMPONENT`, `WALL_PASSWALLED_NOT_LISTED` (not applicable); `MAP_PATCH_TARGETS_OBJECT`, `MAP_PATCH_LINE_UNSUPPORTED`,
`WALL_OR_PORTAL_UNSUPPORTED` (portals), `WALL_SPELL_FLAGS_UNRESOLVED`, `WALL_ART_UNRESOLVED`, `FLAGS_UNRESOLVED`, `PC_RECORD_UNSUPPORTED`,
`OBJECT_TYPE_UNSUPPORTED`, `CRITTER_STATS_UNRESOLVED`,
`CRITTER_EFFECT_INDEX_UNSUPPORTED`, `CRITTER_EFFECT_UNSUPPORTED`,
`CRITTER_MAX_HP_EFFECT_UNSUPPORTED`, `CRITTER_LIFE_INTERVAL_UNRESOLVED`
(Unknown). A gap repeats the Unknown component's code with its cell, object and
role.

Assessment codes: `DESTINATION_TERRAIN_BLOCKED`, `WALL_EDGE_BLOCKED`, `DESTINATION_OBJECT_BLOCKER`,
`SHOULDER_OBJECT_BLOCKER`, `REQUIRED_EVIDENCE_UNAVAILABLE`,
`COMPLETE_NONBLOCKING_EVIDENCE`, `WINDOW_JUMP` (an Open cardinal step across a window, whole maps only, ARC-WORLD-021), `PATH_BRANCH_UNAVAILABLE`, and the unsupported
codes `PROFILE_UNSUPPORTED`, `EVIDENCE_IDENTITY_MISMATCH`, `EPOCH_MISMATCH`,
`DIRECTION_UNSUPPORTED`, `SAME_CELL_UNSUPPORTED`, `NONADJACENT_UNSUPPORTED`,
`DIRECTION_MISMATCH`, `CONTEXT_UNSUPPORTED`, `SOURCE_OUTSIDE_ADMITTED_AREA`,
`DESTINATION_OUTSIDE_ADMITTED_AREA` and `SHOULDER_OUTSIDE_ADMITTED_AREA`.
Construction failures throw `AreaStepInvocationException` with a code and stage
only; they never become evidence.

## Verification

Synthetic tests (`AreaStepTests`) cover the tile-name, effect, HP, patch and MES
rules; object evaluation through the documented field slots (flags 18, HP 26..28,
stat base 38, effects 46) and every type/flag filter; every step composition
branch, gap retention and unsupported request, including non-rectangular
admission and extreme coordinates; and structural inspection through the Area
owner fixture: version-3 (absent) and version-4 (serialized) masks, a forged
version mismatch, tile names, object registration and skipping, patch targets,
strict-publication and identity failures.

The opt-in original check needs the Area variables plus `ARC_AREA_EFFECTS`
(`arcanum3.dat`) and `ARC_AREA_PATCH` (`modules/Arcanum.PATCH0`):

```sh
"$DOTNET" test tests/Arcanum.Data.Tests/Arcanum.Data.Tests.csproj --no-build \
  --filter 'FullyQualifiedName~AreaStepOriginalDataTests&TestCategory=RealData'
```

On the owner's installation it observed 36,864 admitted cells, 29,063 blocked
tiles, 115 object-blocker cells, zero Unknown cells or objects, all seven spawn
observations of the frozen profile (including the reverse step back to spawn), deterministic repeat construction, and
**7,150** cells reachable from spawn `(92934,82561)` inside the core, equal to
the research's core share. The only refusals at the reachable boundary are
`DESTINATION_OUTSIDE_ADMITTED_AREA`. Its evidence digest does not change with
wall support: wall edges enter the digest only when a snapshot has any.

The cave check (same filter, plus `ARC_CAVE_MOBILE`) observed 4,096 admitted
cells, 3,576 blocked tiles, 13 object-blocker cells, 125 wall edges and zero
Unknown cells or objects. From the arrival (2087,2018) the north step is
`WALL_EDGE_BLOCKED` by the arrival tile's own wall and the west, south and east
steps are open; **506** cells are reachable, every step with complete evidence,
all inside the cave walls, and one of them lies within use range 2 of the exit
object (2093,2019).

## Whole maps (ARC-WORLD-018)

`AreaStepInspector.InspectWholeMap(area, stack, aggregatePath, epoch)` builds
the same evidence for a strictly published whole-map snapshot
([whole-map plans](whole-map-plans.md)): the module archive and the rule
resources (tile names, effects, the module's game effects, `map.patch`) come
from the archives that win them in the content stack (each must be an archive
member), the mobiles from the map's aggregate. Only for such snapshots, walls
and portals state their edge rule (pinned evidence keeps the earlier rule and
digest; the rules are appended to the digest only when present):

- a doorway wall piece (22, 25, 26, 29–32) never blocks: the portal on its edge
  decides; a window piece (10, 13, 14, 17–19) blocks a diagonal step, and a
  cardinal step across it is a window jump for the original's critter walker
  (since ARC-WORLD-021: Open with reason `WINDOW_JUMP` and the window in
  `AreaStepAssessment.WindowJump` when nothing else decides it; before it, an
  Unknown `WINDOW_JUMP_UNMODELLED` step; `WindowJump` names the window on every
  result but a blocking wall, so a host overlay that opens the step keeps it a jump);
  every other piece blocks;
- a portal sits on edge `rotation | 1` like a wall and decides for the player
  at T0 as the original's open attempt does (pinned CE `ai_attempt_open_portal`,
  `object_locked_get`;
  [door rules](../research/first-building-evidence.md#portals-doors-and-windows-fr-scene-008)):
  open or busted passes; an unlock script (SAP 3) is Unknown; jammed or
  magically held blocks; a portal without the LOCKED bit, or never-locked,
  passes (walking opens it); a locked one with a by-day or by-night bit is
  Unknown because the evidence has no clock; otherwise it needs a key, none is
  held at T0, and only leaving a building passes: always-locked (which disables
  that shortcut) blocks, a plain lock is Unknown (the shortcut is directional).
  Doors and windows follow the same attempt; a window's wall piece then makes
  the step a jump. Mobile portals targeted by `map.patch` are Unknown, as other
  mobiles are.

The evidence digest identifies the inputs and stored edge rules, not how `Assess` reads them: ARC-WORLD-021 changed
the cardinal window step from Unknown to Open without changing the digest (no step result is persisted).

`AreaMapPlan.StartTile` is the map-list arrival tile. On the installed original
(opt-in `AreaWholeMapStepTests`) every one of the 54 complete interiors builds
evidence with no Unknown terrain; the generic Crash Site cave reaches the same
506 tiles from its start as the pinned cave profile; at least 48 interiors are
walkable from their start (the others list no start tile, or one a walker
cannot leave). In a one-off run the remaining Unknown steps came from critter
effects the rules do not model and from the lock cases above.

## Not delivered

No route search, state after T0, context-ring or southern admission, other
actors or original executable comparison. The native synthetic AREA grid and
camera probes use structural whole-core evidence, and the source `area-original`
gate loads the original profile; the exported ARC-RENDER-007 gates have not yet
been re-run on it.

# Whole-core T0 step assessment

Status: **Implemented Data component**, stage 1 of the
[closure research](../research/area-passability-closure.md#proposed-implementation-not-authorized).
Stage 2 connects it to the `area` viewer through `AreaGridSession`
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
  interval HP rule. Since ARC-DATA-035 effects are read with the shared effect grammar (`EffectRules`, the original's
  parser 0x4E9C20, ledger LM9; its attribute table holds the original's stat, skill, resistance, discipline and special
  names), and only the changes the HP rule reads count: strength, willpower, level, poison level and maximum hit points;
  any other attribute (speed, a skill…) is ignored. An effect the grammar refuses, a multiply or divide or a −1 bound
  on a counted attribute, an out-of-range effect index, a max-HP effect or an interval straddling zero are `Unknown`. Portals, PC records, unresolved flags or stats and any
  mobile named by `map.patch` (in either GUID byte order, or when the patch has
  an unsupported line) are `Unknown` in the pinned profiles; a whole map reads the
  object as the owner patched it ([map patch](map-patch.md), ARC-DATA-028), and only a
  change or line the patch model does not cover is `Unknown`
  (`MAP_PATCH_CHANGE_UNMODELLED`, `MAP_PATCH_LINE_UNSUPPORTED`).
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
([whole-map plans](whole-map-plans.md)): the sectors from the module archive,
the rule resources through the content stack in the phase the original reads
them (ARC-DATA-027, exe-checked: tile names and effects at game start, so the
first base source and never a module copy; the module's game effects at module
load and `map.patch` when a map opens, so the stack winners; a loose file is
read like an archive member), the mobiles from the map's aggregate. The rule
MES files (tile names, effects, game effects) are read in the original grammar
(`MesFile`; a signed or spaced key is refused); `map.patch` is not MES and is
still read as Latin-1 lines. Only for such snapshots, walls
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
  at T0 as the original's open attempt does (the AI portal open attempt 0x4AEB70
  and the lock state getter 0x441D40;
  [door rules](../research/first-building-evidence.md#portals-doors-and-windows-fr-scene-008)):
  open or busted passes; an unlock script (SAP 3) is Unknown; jammed or
  magically held blocks; a portal without the LOCKED bit, or never-locked,
  passes (walking opens it); a locked one with a by-day or by-night bit is
  Unknown because the evidence has no clock, unless the step leaves a building
  (below); otherwise it needs a key, none is
  held at T0, and only leaving a building passes: always-locked (which disables
  that shortcut) blocks, or is Unknown when an hour bit is set. Doors and windows
  follow the same attempt; a window's wall piece then makes the step a jump.
  Mobile portals targeted by `map.patch` are Unknown in the pinned profiles, as other
  mobiles are; a whole map reads the patched portal flags (ARC-DATA-028);
- since ARC-RES-049 the leaving shortcut is decided per step, as the original
  1.0.7.4 executable does it
  ([ledger LV1–LV5](../research/ledger/world.md#leaving-a-building-through-a-locked-portal-arc-res-049)):
  a locked portal that is not always-locked has the edge rule `Leaving` and keeps
  its raw ART rotation r. With P its tile and Q the tile one step along r (the tile
  across for an odd r, the diagonal tile for an even one), a leg leaves when both
  open attempts say so: the traversal attempt (direction of the leg) and the door
  or window goal (direction from the step's source to P, 5 on P itself). Each
  attempt leaves when P is indoor and Q is not, except that it tests Q indoor and
  P not when its direction is r + 3, r + 4 or r + 5; indoor is tile art with bit 8
  clear or facade art with bit 25 clear. So a step out of P passes for r = 3..7
  when P is indoor and Q is not and never for r = 0..2; a step into P from across
  passes when Q is indoor and P is not. Any other step is blocked by a plain lock
  (`PORTAL_LOCKED_LEAVING_ONLY`) and Unknown by an hour lock
  (`PORTAL_LOCK_DEPENDS_ON_HOUR`); a P or Q outside the evidence, or not tile or
  facade art, is an Unknown `PORTAL_LEAVING_TILE_UNRESOLVED` gap. The goal is
  applied to every such portal the step crosses; for a diagonal step across two
  closed doors the original repeats it for the first door only (not modelled). A
  leaving portal adds its raw rotation to the stated edge rules of the digest.

The evidence digest identifies the inputs and stored edge rules, not how `Assess` reads them: ARC-WORLD-021 changed
the cardinal window step from Unknown to Open without changing the digest (no step result is persisted).

`AreaMapPlan.StartTile` is the map-list arrival tile. On the installed original
(opt-in `AreaWholeMapStepTests`) every one of the 54 complete interiors builds
evidence with no Unknown terrain; the generic Crash Site cave reaches the same
506 tiles from its start as the pinned cave profile; at least 48 interiors are
walkable from their start (the others list no start tile, or one a walker
cannot leave). In a one-off run the remaining Unknown steps came from critter
effects the rules do not model and from the lock cases above. ARC-RES-049 left
these counts (54 complete, at least 48 walkable, 22 window-jump steps in 2
interiors) unchanged.

**Runtime portals (ARC-WORLD-051).** `Assess(request, portals)` takes, for any
door or window key, an `AreaPortalPassage` that replaces its T0 edge rule:
`Open` passes, `LeavingOnly` passes only a step leaving a building (the
ARC-RES-049 test), `Blocked` blocks, and `Unresolved` keeps the T0 rule. A
portal whose T0 evaluation is Unknown has no edge and stays a gap. Every whole-map
portal keeps its raw rotation for that test; the evidence digest still writes it
only for a Leaving rule, so digests are unchanged. `AreaStepAssessment.Portals`
lists the portals on the step's legs in leg order, which the viewer opens when
the step passes ([doors and windows](../rendering/area-map-mode.md#doors-and-windows-arc-world-051)).

## Windows (ARC-DATA-034)

`InspectWholeMap` also takes a window snapshot ([window plans](whole-map-plans.md#window-plans-arc-data-032),
profile `arc-data-032-window-original-rules-v1`) and applies the whole-map rules to it. Its own core sectors are read
from the module archive as for a whole map. A generated core sector is read again from the archive that holds its
template in the stack (the window names each template's virtual path, `AreaWindowSector.TemplatePath`; `AreaSnapshot.Window`
exposes the window), bound to the snapshot's payload digest (`FingerprintMismatch` otherwise), and inspected at the real
sector's coordinates; since ARC-DATA-035 a template the stack holds as a loose file is read through the stack the same
way (entry 0 of its source). The template inputs are among the
evidence's sources (`area-terraintemplate-N`).

Installed original (opt-in `AreaWindowStepRealDataTests`):

- **Crash Site (1452, 1290).** The window's evidence equals the pinned exterior profile's (`ValidateOriginalData`) on
  every one of the 36,864 core cells and for every object evaluation, with no Unknown cell or object and no wall edge;
  7,150 cells are reachable from the spawn in both, the pinned profile's frozen count.
- **Town (1409, 1314), elven forest wilderness (1384, 239, all nine core sectors generated) and a generated centre
  (1450, 1290, five core sectors generated).** Evidence builds for all 36,864 core cells with no Unknown terrain or
  object (before ARC-DATA-035 one town critter was `CRITTER_EFFECT_UNSUPPORTED`: its speed effect, which the HP rule does
  not read, is now ignored). The wilderness has 3,539 wall edges from its templates' walls, the town 717, the generated
  centre none.

## Not delivered

No route search, state after T0, context-ring or southern admission, other
actors or original executable comparison. The native synthetic AREA grid and
camera probes use structural whole-core evidence, and the source `area-original`
gate loads the original profile; the exported ARC-RENDER-007 gates have not yet
been re-run on it.

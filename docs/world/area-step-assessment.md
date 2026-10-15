# Whole-core T0 step assessment

Status: **Implemented Data component**, stage 1 of the
[closure research](../research/area-passability-closure.md#proposed-implementation-not-authorized).
It is not connected to Simulation, the viewer or any input yet. The
[production cardinal assessment](cardinal-traversal-assessment.md) and its
x4..8/y0..3 profile are unchanged. Original executable parity remains
**NOT_VERIFIED**.

## Scope and owner decisions

- **Admitted area**: the nine Crash Site core sectors 1451..1453 × 1289..1291
  (36,864 cells). This boundary is an owner decision of 2026-10-15. A step whose
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
reuse the cardinal enums and context class. The original factory requires the
strict original Area snapshot (`arc-data-009-complete-file-effective-source-v1`)
with exactly the nine core sectors. It reads these resources from the named
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
evidence digest covers source identities, admitted sectors, every terrain
verdict and every object verdict. A request must carry the snapshot's profile,
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
  `Unknown`. Walls, portals, PC records, unresolved flags or stats and any
  mobile named by `map.patch` (in either GUID byte order, or when the patch has
  an unsupported line) are `Unknown`.
- **Step** (F7): destination terrain first; then destination objects; for a map
  diagonal also the objects on both shoulders. Shoulder terrain is never
  consulted. A blocker returns `Supported/Blocked` and keeps unrelated gaps.
  Any remaining gap returns `Unknown/null`. Only complete non-blocking evidence
  returns `Supported/Open`.

Reason codes: `DESTINATION_TERRAIN_BLOCKED`, `DESTINATION_OBJECT_BLOCKER`,
`SHOULDER_OBJECT_BLOCKER`, `REQUIRED_EVIDENCE_UNAVAILABLE`,
`COMPLETE_NONBLOCKING_EVIDENCE`, `PATH_BRANCH_UNAVAILABLE`, and the unsupported
codes `PROFILE_UNSUPPORTED`, `EVIDENCE_IDENTITY_MISMATCH`, `EPOCH_MISMATCH`,
`DIRECTION_UNSUPPORTED`, `SAME_CELL_UNSUPPORTED`, `NONADJACENT_UNSUPPORTED`,
`DIRECTION_MISMATCH`, `CONTEXT_UNSUPPORTED`, `SOURCE_OUTSIDE_ADMITTED_AREA`,
`DESTINATION_OUTSIDE_ADMITTED_AREA` and `SHOULDER_OUTSIDE_ADMITTED_AREA`.
Construction failures throw `AreaStepInvocationException` with a code and stage
only; they never become evidence.

## Verification

Synthetic tests (`AreaStepTests`) cover the tile-name, effect, HP, patch and MES
rules, every step composition branch, gap retention, unsupported requests, and
structural inspection through the Area owner fixture (masks, tile names, object
registration, patch targets and identity failures).

The opt-in original check needs the Area variables plus `ARC_AREA_EFFECTS`
(`arcanum3.dat`) and `ARC_AREA_PATCH` (`modules/Arcanum.PATCH0`):

```sh
"$DOTNET" test tests/Arcanum.Data.Tests/Arcanum.Data.Tests.csproj --no-build \
  --filter 'FullyQualifiedName~AreaStepOriginalDataTests&TestCategory=RealData'
```

On the owner's installation it observed 36,864 admitted cells, 29,063 blocked
tiles, 115 object-blocker cells, zero Unknown cells or objects, all six spawn
observations of the frozen profile, deterministic repeat construction, and
**7,150** cells reachable from spawn `(92934,82561)` inside the core, equal to
the research's core share. The only refusals at the reachable boundary are
`DESTINATION_OUTSIDE_ADMITTED_AREA`.

## Not delivered

No Simulation binding, viewer movement, input, route search, state after T0,
context-ring or southern admission, other actors or original executable
comparison. The viewer integration is the next separately reviewed stage.

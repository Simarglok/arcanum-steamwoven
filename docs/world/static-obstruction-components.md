# Bounded static-obstruction component

## Scope and provenance

`Arcanum.Data.StaticObjects.StaticObstructionInspector` implements the ordinary-static-scenery contract specified on this page,
not a combined collision grid, `IsWalkable`, navigation rule or actor step verdict. The profile
is the immutable Crash Site base sector `(1452,1290)`, packed ID `86570436012`,
origin `(92928,82560)`, extent 64×64. Only v119 type-3 ordinary scenery in a normal
non-editor ordinary-ground-critter, adjacent-cardinal, zero-traversal-flags
context is admitted. `NoBlock` means no blocker from this component alone.

The specification is a reading of the original's functions, evidence level F0
(not checked in the executable), and the corroborating archived WorldEd report.
The exact source trace and limits remain in
[RES-012 evidence](../research/static-obstruction-evidence.md). This component
is independently authored.
Source support and fingerprint-bound serialized-data checks do not establish
original executable behavior or independent source corroboration.

## API and ownership

`InspectSector(source, prototypes, moduleSourceId, prototypeSourceId, selector,
options)` takes existing `StaticObjectSource`, `StaticObjectPrototypeSource`,
`TerrainSectorSelector` and `StaticObjectLoaderOptions`. Archives remain
caller-owned. Sources are explicit; no discovery, fallback resource, precedence
or neighbor loading is performed. Source IDs are at most 128 ASCII letters,
digits, underscore, hyphen or dot: no paths or control characters. The selector
must declare the fixed numeric sector profile and match the source's exact entry
path; its optional `TerrainEntryExpectation` is checked using the existing
selection primitive.

This operation is **structural**: the caller asserts the sector profile; synthetic
inputs can exercise the same production seam, but do not become authenticated
original data. `InputIdentity` is null on this path. Call
`ValidateOriginalData(modulePath, resourcePath, prototypeDirectory, options)` for
read-only authenticated original validation. It reuses
`StaticObjectLoader.ValidateOriginalData` and its fixed module/resource/six-prototype
pre-read, production-read and post-read fingerprint gates, then assesses the
owned numeric inspection. The existing five selected ART chains are checked by
that authentication operation; ART outcomes do not determine obstruction.

The owned immutable `StaticObstructionSectorReport` retains:

- numeric `Sector` identity (including DAT entry ordinal), with no selector path;
- source-ordered `Records`: typed `StaticObstructionRecordAssessment` with source
  index/type/prototype A-ID, nullable effective flags, independent flags source
  and location status, optional validated destination and component `Result`;
- separate `UnknownLocationRecords` and `LocationCoverageComplete`;
- optional `InputIdentity`: module/resource length and SHA-256 plus immutable
  per-prototype fingerprints and the existing sorted prototype aggregate.

The report retains no archive, stream, prototype path, ART pixels or caller
buffer. Arrays are copied and public lists are read-only wrappers; later input
mutation, source file changes and archive disposal cannot change the snapshot.
Reports describe the input read, not mutable runtime state.

`GetCell(x,y,context)` queries one exact local destination. `GetDestinationForStep(
fromX,fromY,toX,toY,context)` validates the request boundary then queries only the
destination; it neither assesses the source nor decides traversal. Both return a
`StaticObstructionCellAssessment`: destination coordinates, source-ordered subset
contributors, the separate coverage warning and a component result. Out-of-sector
coordinates, including extreme integers, return an explicit Unsupported result;
no arithmetic wraparound or neighbor query occurs.

## Dimensions and rule order

Each `StaticObstructionResult` independently retains:

```text
Supportedness = Supported | Unknown | Unsupported
Applicability = Applicable | NotApplicable | Unknown
Disposition   = Blocker | NoBlock | null
ReasonCode    = stable uppercase ASCII token
Provenance    = immutable identifier-only entries
```

Disposition exists only with Supported + Applicable. Unknown, Unsupported and
NotApplicable always carry null, never an implicit NoBlock. Provenance retains
serialized source label/entry ordinal/record index, the pinned specification
identifier, and prototype source label/A-ID only when flags inherit. Authenticated
reports additionally retain original-data identity evidence. A cell's result has
sector/specification provenance; its contributor records retain individual flag
provenance.

Per-record evaluation order is intentional:

1. Other delimited types are `TYPE_UNSUPPORTED` (Unsupported + Unknown).
2. Effective flags use **direct zero-based slot 18**, otherwise exactly one
   matching canonical type-A prototype. Missing, duplicate, unlocatable ID,
   unavailable directory, type mismatch or ID mismatch produces
   `FLAGS_UNRESOLVED` (Unknown + Unknown). A direct value of zero is present and
   wins over an opposite default. Slot 19 is ignored.
3. `0x2000` gives `DYNAMIC_EXCLUDED` (Unsupported + Unknown), a subset selector.
4. A direct location is required. Packed signed 32-bit world X/Y are decoded and
   the fixed origin is subtracted with checked arithmetic. Only local `0..63`
   in each dimension is admitted. **Do not use the loader's low-six-bit
   `LocalX/LocalY` convenience properties to validate input.** Missing, overflow
   and out-of-range yield `DIRECT_LOCATION_ABSENT`, `DIRECT_LOCATION_OVERFLOW`
   and `DIRECT_LOCATION_OUT_OF_RANGE` (Unknown + Unknown). Prototype location,
   masked coordinates and zero fallback are never used.
5. With valid location, inventory `0x1000`, destroyed `0x1`, then off `0x2` yield
   `INVENTORY_EXCLUDED`, `DESTROYED_EXCLUDED`, `OFF_EXCLUDED`
   (Supported + NotApplicable), not NoBlock shortcuts.
6. Remaining `0x400` set yields `OBJECT_NO_BLOCK`; clear yields `OBJECT_BLOCKER`
   (Supported + Applicable).

Current ART/field-status/resource outcomes, offsets, hotspot, dimensions, alpha,
shoot-through and sight/cover bits are not obstruction inputs. Flags resolve
independently of ART. The implementation shares the existing loader's wire
parser and prototype/default resolution; its internal flag-only assessment
policy catches only unresolved-reference prototype failures per record.
**Public `StaticObjectLoader.Inspect` and selected-resource policies remain
strict.** Invalid wire/presence/masks/counts/version, truncated prototype/record,
array/string corruption and limits still throw sanitized `StaticObjectException`
with the existing stage/index/offset diagnostics. No report or guessed
continuation is created after malformed input. These parser failures are distinct
from an absent valid location or unresolved flag reference.

## Aggregation and unknown coverage

Only validated exact destinations register; no visual footprint, source cell,
neighbor or edge is inferred. Cell contributors include Applicable and unresolved
subset candidates; explicitly Unsupported and NotApplicable records remain
visible in `Records` but are outside this named admitted subset.

- Any admitted Blocker wins (`DESTINATION_BLOCKER`), including mixed cells with
  unresolved contributors or separate unknown-location candidates.
- Without a Blocker, a known-cell unresolved candidate gives
  `DESTINATION_UNRESOLVED` (Unknown).
- Without a Blocker, any unresolved candidate that cannot be placed gives
  `LOCATION_COVERAGE_UNCERTAIN` (Unknown), even for an apparently empty cell.
- Otherwise all admitted contributors are NoBlock, including the fully surveyed
  empty subset: `ADMITTED_SUBSET_NO_BLOCK`.

A winning Blocker does **not** erase `LocationCoverageComplete=false`; unknown
locations remain separately listed rather than becoming `(0,0)` or evidence of
absence. This is component-only blocker dominance, never a combined Open verdict.
Indexing allocates exactly 4096 destination lists plus at most the bounded terminal
record count, rather than duplicating every record across every cell.

All non-normal contexts (editor, projectile, sound, skip-objects, ignore-critters,
wall/portal/window/critter, traps/spells/combat, mutable state, non-ground actor,
other traversal flags or an unrecognized enum value) return `CONTEXT_UNSUPPORTED`.
Step-query exclusions are `SAME_CELL_UNSUPPORTED`, `NONADJACENT_UNSUPPORTED`,
`DIAGONAL_UNSUPPORTED` and `CROSS_SECTOR_UNSUPPORTED`. Disposition stays null.
There is no Godot integration, navigation, combined passability, dynamic
state/door behavior, resource precedence or original movement parity.
Unsupported means a silent NoBlock is never assumed: the excluded requests
(projectile, sound, skip-objects, ignore-critters, wall, portal, window, critter,
traps, spells, combat, mutable state, diagonal, same-cell, non-adjacent,
cross-sector) keep null disposition.

## Composition boundary

A consumer must keep this component's result next to the serialized block-mask,
facade, tile-name, dynamic/state, actor and edge components and may never derive
a combined `Supported + Open` (walkable) answer from it; the full selected-sector
result stays Unknown until those independent dependencies and the precedence
policy are admitted. The component selects no archive, DIF, PATCH or loose-file
precedence, loads no neighbor sector and establishes no original executable
behavior. The retained gaps and the Open/Blocked composition rule are in the
[traversal research contract](area-traversal-contract.md). A normal direct
slot-18 result carries `OBSERVED_SERIALIZED_INSTANCE` and `SOURCE_READING`
provenance, an inherited one also `PROTOTYPE_DEFAULT`; the archived WorldEd
report is corroboration, not original-runtime provenance. The self-test holds a
distinct slot-19 sentinel against accidental substitution for slot 18.

## Reproduction and independent oracles

Use the [pinned toolchain](../workflow/toolchain.md) and its root locked restore,
build, synthetic test filter and DAT self-test. Focused synthetic production-seam
checks are in `StaticObstructionInspectorTests`; they share only the existing
**test-local independently encoded** v119/DAT/prototype fixture, not implementation
constants or assessment logic. Literal flags, opposite override/default values,
raw coordinates, expected outcomes and malformed envelopes are authored oracles.

```sh
dotnet test tests/Arcanum.Data.Tests/Arcanum.Data.Tests.csproj --no-build \
  --filter 'FullyQualifiedName~StaticObstructionInspectorTests&TestCategory!=RealData'
```

The test class's opt-in original-data method is the minimal inspection probe:
it calls the production authenticated API and checks literal aggregate and numeric
representative oracles from RES-012. Configure only the unchanged local original
inputs via `ARCANUM_STATIC_MODULE`, `ARCANUM_STATIC_RESOURCE` and
`ARCANUM_STATIC_PROTOTYPES` (never put actual paths or payloads in Git), then run:

```sh
dotnet test tests/Arcanum.Data.Tests/Arcanum.Data.Tests.csproj --no-build \
  --filter 'FullyQualifiedName~StaticObjects&TestCategory=RealData' \
  --logger 'console;verbosity=detailed'
```

Missing inputs fail, not skip. The probe emits fingerprints, safe counts and six
numeric rows, not an asset dump or full grid. Expected fixed-source values are
317 records, 31 direct / 286 prototype flags, 317 in-range, 58 Blocker / 259
NoBlock, zero unresolved, 293 occupied cells, 22 multi-contributor groups, max 4.
Rows `(index,x,y,flags source,disposition,count)` are `(1,5,1,Direct,Blocker,1)`,
`(7,38,2,Prototype,Blocker,1)`, `(166,19,34,Direct,NoBlock,1)`,
`(0,50,0,Prototype,NoBlock,1)`, `(15,59,4,Mixed,Blocker,2)` and
`(2,6,1,Prototype,NoBlock,2)`. These are test oracle values, not universal runtime
rules or an original executable comparison. The existing Crash Site research
`--self-test` / `--passability` checks remain separate historical research paths;
they are not substitutes for this production component test.

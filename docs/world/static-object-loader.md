# Bounded static-object loader

## Scope

`Arcanum.Data.StaticObjects.StaticObjectLoader` is a Godot-independent,
fail-closed reader for the reviewed selected Crash Site sector. It accepts an
explicit module archive and entry, explicit prototype directory, and explicit
resource archive. It does not discover archives, resolve PATCH/dif precedence,
load other maps or versions, decode pixels, choose a scene order, or make
visibility, collision, interaction, occlusion, or runtime-parity claims.

The loader reuses `TerrainLoader.ExtractObjectWindow` for the fixed sector
framing and `DatArchive`/`ArtDecoder` for archive and ART metadata handling.
Caller-owned archives remain caller-owned; the result owns copies of parsed
values only. `DatArchive.ReadEntry` serializes its seek/read/decompression
operation, so concurrent callers cannot corrupt a shared seek-based archive.

ARC-RES-012 consumes the existing `Inspect` records in its bounded research
probe to assess one ordinary-static-scenery destination-cell component. That
consumer does not widen this loader, add collision/navigation API surface, or
turn `IsStatic` into a general collision verdict. See the separate
[static-obstruction contract](static-obstruction-contract.md) and
[evidence](../research/static-obstruction-evidence.md).

## API

The separate [complete area owner](crash-site-area-loader.md) shares the internal
v119 FILE wire reader while supplying its own authenticated multisector/mobile
profile. This frozen reader's 317-record and five-only resource scopes remain
unchanged; full-area readiness is not a claim made by `InspectSelectedResources`.

```csharp
using var module = DatArchive.Open(modulePath);
using var resource = DatArchive.Open(resourcePath);
var source = new StaticObjectSource(module, selectedEntryPath, resource);
var prototypes = new StaticObjectPrototypeSource(prototypeDirectory);
StaticObjectInspection report = StaticObjectLoader.Inspect(source, prototypes);
```

The same public API is exercised by a complete, executable synthetic fixture:
`StaticObjectLoaderTests.Selected_resource_stage_resolves_five_synthetic_records_without_claiming_original_identity`
in `tests/Arcanum.Data.Tests/StaticObjects/StaticObjectLoaderTests.cs`. The one
test-local source file constructs five unrelated direct-field v119 records, the
sector window, both 1TAD archives, a mapping, and minimal ART metadata before
calling `InspectSelectedResources`; it asserts every returned metadata field.
Run that exact synthetic example with:

```text
dotnet test tests/Arcanum.Data.Tests/Arcanum.Data.Tests.csproj --filter "FullyQualifiedName~Selected_resource_stage_resolves_five_synthetic_records_without_claiming_original_identity" --no-restore
```

The fixture is intentionally test-local: production exposes no fixture encoder,
test profile, golden record, original path, or original payload.

The loader neither disposes the caller-owned streams nor mutates their buffers;
its inspection result is an immutable copy of the admitted numeric fields.
`InspectSelectedResources` is likewise caller-owned and structural: it resolves
only source indexes 0 through 4 and fails closed on those five resource chains.
It is useful for synthetic tests but does not claim an authenticated Crash Site
identity or return validation evidence.

For an opt-in original-data check, `ValidateOriginalData` owns bounded temporary
archive handles, fingerprints the module/resource and each canonical selected
prototype before its production read, compares the production-read fingerprint
to that pre-read evidence, and checks all inputs again after inspection. Its
result returns identifier-only module/resource and per-prototype size/hash
evidence plus an aggregate; it does not retain supplied paths. The operation
uses private fixed selectors and documented module/resource hashes and
six-prototype hash set before it reads the fixed five-record profile. Callers
cannot supply labels, selectors, expected records, or fingerprints to that API.

`StaticObjectSource` requires a single exact module entry and caller-owned
archives. `StaticObjectPrototypeSource` receives one caller-selected directory;
it has no archive or loose-file precedence behavior. Limits are
configured with `StaticObjectLoaderOptions` and cap path-owned archive/index/
entry totals, sector, record, field, array, string, prototype, resource,
mapping entries/values, and ART metadata sizes before allocation.

`Inspect` performs census/default resolution only. It deliberately leaves every
resource outcome `NotEvaluated`. `InspectSelectedResources` is the separate,
untrusted structural resource-stage operation. It admits source indexes 0
through 4 only when each is resolved type 3, static, and has the required direct
instance fields; it fails the entire operation on a selected mapping/ART/group/
frame/palette failure. The authenticated `ValidateOriginalData` operation alone
uses the private Crash Site selector and immutable input identities. For its
317-record input it marks exactly the other 312 records `OutsideResourceScope`.

`StaticObjectInspection` deliberately has separate dimensions:

| Field | Meaning |
| --- | --- |
| `TerminalRecordCount`, `Records`, `FieldResolvedCount` | v119 census and current-ART/object-flag field resolution |
| `ResourceResolvedCount` | selected ART metadata chains completed |
| `ResourceNotEvaluatedCount` | census-only records whose resource stage was not requested |
| `ResourceOutsideScopeCount` | selected-resource records deliberately excluded from the five admitted source indexes |

For the reviewed fixed input the expected reconciliation is 317 serialized
records, 288 direct and 29 prototype current ART values, 31 direct and 286
prototype object flags, five selected resource metadata outcomes, and 312
`OutsideResourceScope` resource outcomes. Census/default success is not a claim that
all sector resources are resolved.

## Admitted fields and precedence

The parser accepts object version 119 and safely delimited type-3 records.
It preserves the source index, type-A prototype ID when present, direct
current ART, optional local location, direct instance offsets, and direct
object flags. Absent values stay absent.

Only these inherited values are resolved:

```text
current ART = direct slot 0, else exactly one matching type-A prototype
object flags = direct zero-based slot 18, else exactly one matching type-A prototype
static = type 3 && (effective flags & 0x00002000) == 0
```

Slot 19 is not substituted for object flags. Direct instance offsets remain
distinct from ART frame offsets/hotspots and the API does not project or
rasterize either value.

For source indexes 0 through 4 only, the selected-resource operation resolves
the packed ART type,
scenery mapping, unique normalized resource entry, physical group/frame, and
palette metadata. A missing/ambiguous mapping or resource, unsafe mapping
filename, non-scenery ART type, invalid group/frame, or unavailable palette
fails closed. All later records are explicitly `OutsideResourceScope` rather
than invalid or resource-complete.

## Errors and statuses

| Condition | Result |
| --- | --- |
| malformed selected sector or terminal mismatch | `StaticObjectException` with `InvalidFraming` or `InvalidRecord`; parsing stops at the safe record/offset diagnostic |
| unknown version, type, mask bit, count mismatch, optional/array/string envelope | `StaticObjectException`; no synthetic continuation is created |
| missing/ambiguous/mismatched required prototype | `UnresolvedReference` exception or record field status |
| missing/ambiguous/unsafe selected ART chain | `UnresolvedResource` / `InvalidArtSelector` |
| five selected metadata chains | `ResourceStatus.Resolved` |
| all other selected-resource census records | `OutsideResourceScope` |

Diagnostics expose a typed `Stage` plus nullable source index/offset, and never
return original paths, archive names, raw bytes, strings, or pixels.

## Verification

Synthetic fixtures independently encode v119 records and a minimal DAT/ART
chain. The opt-in real-data test has no input paths or payloads in Git and
requires all three environment variables:

```text
ARCANUM_STATIC_MODULE=<explicit module DAT>
ARCANUM_STATIC_RESOURCE=<explicit resource DAT>
ARCANUM_STATIC_PROTOTYPES=<explicit prototype directory>
dotnet test tests/Arcanum.Data.Tests/Arcanum.Data.Tests.csproj --filter "FullyQualifiedName~StaticObjectLoaderTests&TestCategory=RealData"
```

See [Crash Site objects](../research/crash-site-objects.md) for the frozen
input identities, provenance, and evidence boundaries, and
[the static-object contract](static-object-loader-contract.md) for the
unimplemented placement/renderer boundaries. The cited source readings remain
specification evidence only (F0 unless the verification ledger lists them); this
implementation is independently written.

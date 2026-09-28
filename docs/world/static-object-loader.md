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
values only.

## API

```csharp
using var module = DatArchive.Open(modulePath);
using var resource = DatArchive.Open(resourcePath);
var source = new StaticObjectSource(module, selectedEntryPath, resource, "selected-resource");
var prototypes = new StaticObjectPrototypeSource(prototypeDirectory, "selected-prototypes");
StaticObjectInspection report = StaticObjectLoader.Inspect(source, prototypes);
```

For an opt-in original-data check, `ValidateOriginalData` owns its temporary
archive handles, fingerprints the module/resource and selected prototype set,
checks them again after inspection, and returns identifier-only size/hash
evidence with the inspection. It does not retain the supplied paths.

`StaticObjectSource` requires a single exact module entry and a non-path
resource label. `StaticObjectPrototypeSource` receives one caller-selected
directory; it has no archive or loose-file precedence behavior. Limits are
configured with `StaticObjectLoaderOptions` and cap sector, record, field,
array, string, prototype, resource, and ART metadata sizes before allocation.

`StaticObjectInspection` deliberately has separate dimensions:

| Field | Meaning |
| --- | --- |
| `TerminalRecordCount`, `Records`, `FieldResolvedCount` | v119 census and current-ART/object-flag field resolution |
| `ResourceResolvedCount` | selected ART metadata chains completed |
| `ResourceNotEvaluatedCount` | valid census records deliberately outside selected resource scope |

For the reviewed fixed input the expected reconciliation is 317 serialized
records, 288 direct and 29 prototype current ART values, 31 direct and 286
prototype object flags, five selected resource metadata outcomes, and 312
`NotEvaluated` resource outcomes. Census/default success is not a claim that
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

For source indexes 0 through 4 only, the loader resolves the packed ART type,
scenery mapping, unique normalized resource entry, physical group/frame, and
palette metadata. A missing/ambiguous mapping or resource, unsafe mapping
filename, non-scenery ART type, invalid group/frame, or unavailable palette
fails closed. All later records are explicitly `NotEvaluated` rather than
invalid or resource-complete.

## Errors and statuses

| Condition | Result |
| --- | --- |
| malformed selected sector or terminal mismatch | `StaticObjectException` with `InvalidFraming` or `InvalidRecord`; parsing stops at the safe record/offset diagnostic |
| unknown version, type, mask bit, count mismatch, optional/array/string envelope | `StaticObjectException`; no synthetic continuation is created |
| missing/ambiguous/mismatched required prototype | `UnresolvedReference` exception or record field status |
| missing/ambiguous/unsafe selected ART chain | `UnresolvedResource` / `InvalidArtSelector` |
| five selected metadata chains | `ResourceStatus.Resolved` |
| all other census records | `ResourceStatus.NotEvaluated` |

Diagnostics intentionally contain stage/index information only and never
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
unimplemented placement/renderer boundaries. The cited CE material remains
attributed specification evidence only; this implementation is independently
written and does not copy or translate CE code.

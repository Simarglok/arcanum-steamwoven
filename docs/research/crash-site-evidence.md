# Crash Site evidence ledger

## Scope and provenance

This ledger records the ARC-RES-004 correction to the one-sector research
result. The user-owned
original data was read only. The repository contains no extracted game bytes,
asset names, previews, installation paths, credentials, or third-party source
copy.

| Source | Identifier | Use | Status / limit |
| --- | --- | --- | --- |
| Observed module archive | `Arcanum.dat`, 355,938,305 bytes, SHA-256 `8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f` | exact map and sector selectors | experimentally verified local input; not a retail-canonical or precedence claim |
| Project reader | `src/Arcanum.Data/DatArchive.cs` | bounded DAT opening and in-memory entry decode | independently authored project implementation |
| Project ART decoder | `src/Arcanum.Data/ArtDecoder.cs` | bounded selected-frame metadata/decode for representative terrain paths | no scene-parity or general precedence claim |
| Pinned CE source | `a7ff41b300ef712f0e7d088183a3d08957110cdd` | sector sequence, coordinate packing, isometric relation, ART-ID context | one external lineage, not independent corroboration |
| Community catalogue | preserved toolkit metadata | Crash Site context | reported only; not byte-level map proof |

The CE revision was resolved by its full immutable commit ID before inspecting
these focused paths: `src/game/sector.c`, its light/tile/roof/script/sound/block
and object-list modules, `src/game/location.c`, `src/game/name.c`,
`src/game/a_name.c`, and `first_party/tig/src/art.c`.

## Reproduction commands

Use the canonical project SDK and pass a local, user-owned input explicitly.
The probe emits aggregate metadata only and does not write next to its input.

```text
dotnet restore --locked-mode
dotnet build --no-restore
dotnet restore tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --locked-mode
dotnet build tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --no-restore

dotnet run \
  --project tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj \
  --no-build -- --self-test

dotnet run \
  --project tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj \
  --no-build -- --module <explicit-module-path>

dotnet restore tools/research/Arcanum.TerrainProbe/Arcanum.TerrainProbe.csproj --locked-mode
dotnet build tools/research/Arcanum.TerrainProbe/Arcanum.TerrainProbe.csproj --no-restore
dotnet run --project tools/research/Arcanum.TerrainProbe/Arcanum.TerrainProbe.csproj --no-build -- --self-test
dotnet run --project tools/research/Arcanum.TerrainProbe/Arcanum.TerrainProbe.csproj --no-build -- \
  --module <explicit-module-path> --resource <explicit-one-source-resource-archive>
```

Use the project's configured .NET 8 SDK. The module path is supplied locally
and must not be committed or reported.

## Executed results

| Check | Result |
| --- | --- |
| Crash Site probe synthetic suite | PASS: framing branches, checked count/length bounds, roof and placeholder discriminators, and the bounded v119 delimiter are exercised without retaining real payloads. |
| Read-only selected-sector framing run | PASS: size/SHA-256 matched before and after; ordinal 463 decoded to 47,291 bytes; all contiguous sections led to object window `17140..47287`; 317 v119 records reached the terminal count. The word at `16388` was correctly treated as roof `empty=1`, not an object version. |
| Terrain probe synthetic suite | PASS: type-specific palette selection, sorted facade MES ordinal lookup, resolver branches, and projection arithmetic. |
| Read-only terrain run with explicit `arcanum2.dat` source | PASS with completeness failure retained: 3,033 tile and 1,063 facade cells; 2,576 tiles and all facades resolved; 457 tile cells unresolved; all observed branches reported, with six bounded identifier-to-ART/frame/metadata/anchor chains. Source fingerprints matched before and after. |
| Existing DAT synthetic tests | PASS: canonical SDK 8.0.425 locked run with `DAT_ROOT` set to user-owned inputs: Arcanum.Data.Tests 25/25 and Arcanum.Presentation.Tests 14/14. |
| Existing Python DAT probe synthetic suite | PASS: `python3 tools/research/dat_probe.py --self-test`. |

The commands record aggregate facts and bounded representative chains only; no
original bytes, MES text, asset corpus, or decoded pixels are retained.

## Corrected boundary and remaining probe limits

`Arcanum.CrashSiteProbe` reuses `DatArchive`, caps the selected entry at 1 MiB,
and validates arithmetic before each count-dependent region. On this frozen
input it frames zero lights, the 4,096-word table, the roof and all enabled
intermediate sections, and then the object window. The corrected window is
`17140..47287`, with terminal count at `47287..47291`; the observed `1` at
`16388` is a roof discriminator. Its bounded v119 delimiter is framing
evidence, not a full object model or scenery classifier.

`Arcanum.TerrainProbe` supports only the observed tile/facade classes and an
explicit one-archive source set. It applies source-attributed MES callback
rules, requires one exact raw-path candidate, and uses `DatArchive` plus
`ArtDecoder` for a selected bounded entry/frame. Unsupported classes, missing
or duplicate candidates, invalid selectors, and unresolved lookups fail
closed; no fallback path or generic archive precedence is inferred.

## Known limitations and resulting readiness

- The bounded one-source terrain resolver leaves 457 tile cells unresolved and
  establishes neither a general resource-precedence policy nor a complete
  sector.
- Framing and the v119 delimiter establish no selected-version object field
  semantics, static-scenery subset, current-ART/prototype chain, baseline, or
  occlusion rule. The word `1` at offset `16388` is not an object version.
- No C# destination-rectangle/baseline transform, terrain overlap order,
  lighting/palette behavior, or scene comparison was measured.
- The CE location formula is source-derived and numeric synthetic checks verify
  only its relative arithmetic, not visual parity.

Accordingly, the C# loader and static Godot scene are both **blocked**, not
ready. Their bounded research gates are stated in
`docs/world/static-scene-contract.md`.

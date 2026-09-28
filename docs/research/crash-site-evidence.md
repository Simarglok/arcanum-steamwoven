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
| Observed resource archive | `arcanum2.dat`, 147,991,122 bytes, SHA-256 `4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d` | one explicit terrain lookup source | experimentally verified local input; not a retail-canonical or precedence claim |
| Bounded installed-source inventory | `arcanum1.dat` `6dd9d10ef0932e02fc88f0eb5367b1bc37275db51d60bd68ac9bd7ef2d478576`; `arcanum3.dat` `cbadd76bb3df798fa25dcc05871af1ea751c1164fb1426dccc520cd7d9edc499`; `Arcanum4.dat` `975d43854fca8b7dd74e2b12ef2ff3fe6d3c0f99dc541151d33001469f89710c`; `tig.dat` `c9b48eb6396c514eba3805fb4847df67db3c67ef9f13a79ea21bbf7dd329a09b`; `Vormantown.dat` `163a9560ad7d684f486167bf91ad6da0fdef8bd7fd1082fc054084611da04345`; `Arcanum.PATCH0` `d9a3313f71d78115dcac489296bf3d8a5801dd55b20267bd6017b7a898364bce` | competing-source/duplicate check for the 220 corrected and 86 historical pre-normalization paths | all eight fingerprints matched before/after; corrected set: 220 entries only in `arcanum2.dat`; historical set: zero entries in every archive; no precedence policy inferred |
| Bounded loose-ART check | three explicit relevant installation ART roots: root, `data`, and `modules` | direct generated-path check for the corrected and historical sets | all three roots existed, but none contained a generated corrected or historical candidate file; zero candidates for both sets; no recursive scan or loose-file precedence policy inferred |
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
  --no-build -- --module <explicit-module-path> \
  --prototype-root <explicit-prototype-directory> --resource <explicit-resource-path>

dotnet restore tools/research/Arcanum.TerrainProbe/Arcanum.TerrainProbe.csproj --locked-mode
dotnet build tools/research/Arcanum.TerrainProbe/Arcanum.TerrainProbe.csproj --no-restore
dotnet run --project tools/research/Arcanum.TerrainProbe/Arcanum.TerrainProbe.csproj --no-build -- --self-test
dotnet run --project tools/research/Arcanum.TerrainProbe/Arcanum.TerrainProbe.csproj --no-build -- \
  --module <explicit-module-path> --resource <explicit-one-source-resource-archive> \
  [--inventory <read-only-installed-archive> ...] [--loose-root <read-only-art-root> ...]
```

Use the project's configured .NET 8 SDK. The module path is supplied locally
and must not be committed or reported.

## Executed results

| Check | Result |
| --- | --- |
| Crash Site probe synthetic suite | PASS: framing branches, checked count/length bounds, roof and placeholder discriminators, and the bounded v119 delimiter are exercised without retaining real payloads; direct and inherited object-flag cases independently write distinct sentinels to literal raw serialized slots 18 and 19 before parsing. |
| Read-only selected-sector object run | PASS: module and explicit resource SHA-256 values matched before and after; ordinal 463 decoded to 47,291 bytes; all contiguous sections led to object window `17140..47287`; 317 v119 type-3 records reached the terminal count. Current ART resolved directly for 288 and through six validated prototype files for 29; object flags resolved directly for 31 and through those six files for 286. All passed the bounded static predicate using serialized common slot 18 (CE `OBJ_F_FLAGS` enum ID 19), not adjacent slot 19 spell flags. Five selected numeric source examples completed exact resource/mapping/ART metadata chains. The word at `16388` was correctly treated as roof `empty=1`, not an object version. |
| Terrain probe synthetic suite | PASS: fail-closed MES initialization (malformed, missing, duplicate, non-zero facade start, each shifted/missing tile anchor, range/capacity cases), no name trimming, type-specific palette selection, unsupported-class/invalid-selector rejection, every resolver branch through exact raw-path archive lookup, separate corrected/historical candidate inventory through the production archive reader, projection arithmetic, and successful no-draw results for empty footprint/viewport and source/frame intersections. |
| Read-only terrain run with explicit `arcanum2.dat` source | PASS: module `355,938,305` bytes / `8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f` and resource `147,991,122` bytes / `4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d` matched before and after; all 3,033 tile and 1,063 facade cells resolved uniquely; zero missing/ambiguous/bad-selector/unsupported outcomes; all 4,096 selected frames inspected; seven bounded identifier-to-ART/frame/metadata/anchor chains include one normalization recovery. The explicit non-Windows/macOS CE pathname profile reported 220 corrected paths only in `arcanum2.dat`, zero historical paths in all eight archives, and zero candidates under three existing relevant loose ART roots, none of which contained a generated candidate file. |
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
explicit one-archive source set. It pins and reports both source fingerprints,
then verifies the module and resource fingerprints again after the read-only
run. It applies source-attributed MES callback rules, including the tile
callback normalization that clears palette and low flag bits before filename
construction; it rejects malformed MES,
requires tile keys `0`, `100`, `200`, `300`, and `400`, rejects facade tables
outside 1–511 entries or without exact key `0`, requires one exact raw-path
candidate, and uses `DatArchive` plus
`ArtDecoder` for a selected bounded entry/frame. Unsupported classes, missing
or duplicate candidates, invalid selectors, and unresolved lookups fail
closed; no fallback path or generic archive precedence is inferred.

The installed-source inventory used separate normalized candidate sets against
eight bounded DAT files, recomputing each fingerprint before and after. Only
the selected `arcanum2.dat` contained the 220 corrected candidate entries; the
86 historical pre-normalization paths had zero entries in every archive. A
direct, non-recursive check of those two sets at the three relevant loose ART
roots found all three roots existed but neither generated candidate set had a
candidate file below any of them. The probe's pathname
comparison is explicitly the CE non-Windows/macOS branch: lowercasing plus
backslash-to-slash conversion; Windows behavior was not claimed. No precedence
rule was inferred from these observations.

## Known limitations and resulting readiness

- The bounded one-source terrain resolver covers all 4,096 records only for
  the recorded explicit source. It establishes neither a general
  resource-precedence policy nor a complete rendered sector.
- ARC-RES-007 establishes the selected-v119 record envelope, type-3/static
  subset, current-ART/prototype chain, and five ART-metadata examples only.
  Baseline, placement, occlusion, ordering, resource precedence, and scene
  parity remain unmeasured. The word `1` at offset `16388` is not an object
  version.
- R2 source tracing establishes diagnostic tile/editor-facade destination
  footprints, source/destination crop, raw-tile flip, and a mode-qualified
  order. Lighting/palette behavior, camera/dirty-rectangle policy, normal-game
  facade rendering, and scene comparison remain unmeasured.
- The CE location formula is source-derived and numeric synthetic checks verify
  only its relative arithmetic, not visual parity.

The three deliverables have separate readiness verdicts:

- **Fixed-source terrain-only loader contract: READY.** For this frozen sector,
  a caller supplies the immutable module and the explicit one-source resource
  archive; the tile/facade-only contract resolves all 4,096 cells and carries
  selected-frame plus diagnostic placement evidence. Its opaque object suffix
  remains outside the returned terrain records.
- **Terrain scene: NOT READY.** No production/Godot scene or renderer
  integration exists, and lighting/palette treatment, camera/dirty-rectangle
  policy, normal-runtime facade intent, and measured presentation comparison
  remain open.
- **Static-object diagnostic: READY; static scenery scene: NOT READY.** The
  framed suffix now has a bounded object/current-ART result, but baseline,
  placement, occlusion, and static-scene ordering remain unestablished.

These verdicts do not imply a general source-precedence policy or a production
loader. The broader terrain-scene and scenery gates are stated in
`docs/world/static-scene-contract.md`.

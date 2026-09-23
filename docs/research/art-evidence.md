# ART research evidence record

## Purpose and scope

This record supports the bounded ART specification in
[formats/art.md](../formats/art.md). It uses the user-owned installation only
through read-only `Arcanum.Data` access and an independently authored research
probe. It does not publish or retain original ART bytes, decoded pixels,
original entry names, payload hashes, previews, or installation paths.

No CE application, legacy executable, Windows tool, game runtime, renderer, or
community binary was run. ArtConverter, ArcNET, and archived ArtView material
were read only for provenance and source claims; no external decoder code was
copied into this repository.

## Source matrix and lineage

| Source | URL / stable reference | Saved version and integrity | Availability | Provenance / license status | Claims considered |
| --- | --- | --- | --- | --- | --- |
| ArtConverter source | https://github.com/AxelStrem/ArtConverter/tree/0a7572480066c7d15a88cf51c075c250b9e49ee6 | commit `0a7572480066c7d15a88cf51c075c250b9e49ee6`; archived bundle SHA-256 `430aacd4e23247455348914ddae1efbe75bc67ad3c1f89d02278e078a4c026b5` | Local bare mirror and verified bundle available in ignored `toolkit/` | Author Alexey Stremov; no `LICENSE` path at pinned tree. Reuse permission is not established. | 132-byte header sequence, present palette tables, 28-byte frame headers, raw/RLE hypothesis, and its names for geometry fields. Source claim only until the bounded byte test below. |
| ArcNET source | https://github.com/Bia10/ArcNET/tree/baa7cb5a7b8d1498344cdb5e3170bce8f191fd3c | commit `baa7cb5a7b8d1498344cdb5e3170bce8f191fd3c`; archived bundle SHA-256 `398b0daa72fe69eb526bf4ad3a4fb6c33091bc6b233ab065c9cd1f698098dba6` | Local bare mirror and verified bundle available in ignored `toolkit/` | MIT repository, but its README identifies `arcanum-ce` as a primary format-research route and credits ArtConverter as ART reference. It is CE-derived/dependent corroboration, not an independent confirmation or reuse basis. | A matching 132-byte/header-table/RLE implementation and labels such as frame rate, action frame, palette index 0 transparency, and geometry names. Labels were not adopted as facts. |
| Arkanum ART wiki | https://github.com/vleon1/Arkanum/wiki/art | wiki commit `8248418aef9fb5bcbb13d387ff318dfd4bd31249`; archived bundle SHA-256 `7370bf7058c107e1eb844be5edd8d3b857ddc58d76cc9ce48fe1590f8b8ded44` | Local mirror/bundle available in ignored `toolkit/` | No reuse license established for this route. It is a community observation note, not a binary-layout reference. | Category/type observations used only to choose local `item` and `critter` groups; it provides no header, codec, timing, or renderer proof. |
| ArtView.Fixed documentation | https://terra-arcanum.com/downloads/arcanum/tools/ArtView.Fixed.zip | package SHA-256 `9acbffee54790144e2b8ced8dd6edce4728c1b76c37e3290a7342a3df2debb2b`; manifest records ZIP CRC pass | Preserved package/documentation in ignored toolkit; executable/source not run | Original by eml78; changes attributed to Drog Black Tooth. License not reviewed. | Publication history and an `-X`/`-Y` bug-fix statement only; not a format oracle. |
| Terra-Arcanum / Arcanum Club ART-tool catalogues | https://terra-arcanum.com/downloads.handler/index.php?section=modding and https://arcanumclub.org/workshop/tools | URLs, acquisition times, hashes, and package availability in `toolkit/manifest.json` and `toolkit/SHA256SUMS` | Local preserved packages available, unexecuted | Individual package licenses not reviewed. | Historical availability of ART viewers/converters only; catalogue listings do not prove layout or runtime semantics. |
| Local installation candidates | user-owned data, read-only, parameterized as `DAT_ROOT` | Seven supplied DAT files matched the prior R1 size/SHA-256 values before and after the ART run | Available locally during this run | User-owned 1.0.7.4 candidates; provenance remains bounded as explained in [DAT evidence](dat-evidence.md#installation-provenance-and-confidence-boundary) | Experimentally verified table/payload/RLE boundaries for the selected corpus. |

The ArtConverter and ArcNET implementations share a direct declared ART source
route. They are therefore not counted as two independent confirmations.
ArtConverter has no pinned-tree license, and ArcNET's MIT label does not erase
its stated CE ancestry. Neither was copied, executed, or treated as a runtime
oracle.

## Toolkit integrity checks

The read-only check recomputed these archived SHA-256 values and ran
`git fsck --no-dangling` on the ArtConverter and ArcNET mirrors:

- ArtConverter.bundle: `430aacd4e23247455348914ddae1efbe75bc67ad3c1f89d02278e078a4c026b5`;
- ArcNET.bundle: `398b0daa72fe69eb526bf4ad3a4fb6c33091bc6b233ab065c9cd1f698098dba6`;
- ArtView.Fixed.zip: `9acbffee54790144e2b8ced8dd6edce4728c1b76c37e3290a7342a3df2debb2b`.

These hashes establish only the locally preserved bytes, not original-tool
correctness, source authenticity, or reuse permission.

## Read-only corpus and selection

The harness is `tools/research/Arcanum.ArtProbe`. It opens each DAT through the
merged `Arcanum.Data` `DatArchive` reader; no new DAT parser is introduced. It
reads index metadata to aggregate `.art` entries, then reads at most 48 eligible
payloads of at most 1 MiB. The completed run needed only 12 payload reads.

A payload is eligible only when its raw entry name ends in `.art`, its DAT flag
is exactly plain (`0x001`) or zlib (`0x002`), and both stored and decoded sizes
are positive and at most 1 MiB. The probe has caps of 1,024 total frames, 4,096
per dimension, and 4 MiB decoded pixels per frame. It validates all arithmetic
before slicing or decoding a payload.

The selector is deterministic: archives are supplied in the command order;
entries retain DAT index order; it takes the lowest valid six `item` category
entries and the lowest valid six `critter` category entries. It checks that the
observed header-bit group count agrees with the local class expectation; a
mismatch is not selected. The selector prints only `archive-label#index-ordinal`
and aggregate category names, never an original entry name.

### Aggregate index inventory

| Archive label | DAT entries | `.art` entries | Eligible bounded `.art` entries |
| --- | ---: | ---: | ---: |
| base1 | 5,856 | 5,754 | 5,739 |
| base2 | 12,365 | 8,221 | 8,220 |
| base3 | 1,097 | 0 | 0 |
| base4 | 134 | 1 | 1 |
| tig | 28 | 17 | 17 |
| module | 75,591 | 0 | 0 |
| vormantown | 12,812 | 0 | 0 |

The aggregate category inventory includes 1,279 `item`, 3,524 `critter`, 514
`monster`, 563 `unique_npc`, 1,478 `scenery`, 1,255 `tile`, 3,104 `wall`, 815
`interface`, and 605 `portal` entries. Other observed category aggregates are
reported by the local harness but were not needed for the corpus. Category
labels are local selector aids, not a runtime type mapping.

The expected `modules/Arcanum.PATCH0` candidate was unavailable at this run, so
it was not silently substituted and no PATCH ART coverage is claimed.

### Selected corpus

| Selector | Category / class | Present palettes | Frames | Raw / RLE frames | Decoded indexed pixels | Header words 17–24 equal per-group payload totals |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| `base2#825` | item / static | 1 | 1 | 0 / 1 | 17,750 | no |
| `base2#826` | item / static | 1 | 1 | 0 / 1 | 13,054 | no |
| `base2#827` | item / static | 1 | 1 | 0 / 1 | 7,068 | no |
| `base2#828` | item / static | 1 | 1 | 0 / 1 | 682 | no |
| `base2#829` | item / static | 1 | 1 | 0 / 1 | 12,852 | no |
| `base2#830` | item / static | 1 | 1 | 0 / 1 | 2,852 | no |
| `base1#4` | critter / directional | 4 | 80 | 0 / 80 | 154,049 | yes |
| `base1#5` | critter / directional | 4 | 8 | 0 / 8 | 14,745 | yes |
| `base1#6` | critter / directional | 4 | 40 | 0 / 40 | 59,533 | yes |
| `base1#7` | critter / directional | 4 | 72 | 0 / 72 | 122,452 | yes |
| `base1#8` | critter / directional | 4 | 56 | 0 / 56 | 93,529 | yes |
| `base1#9` | critter / directional | 4 | 32 | 0 / 32 | 53,757 | yes |

All selected entries had exact header/table/payload consumption. The 12
samples contain 306 RLE frames and no real-data raw frames. The six item
samples experimentally disprove treating header words 17–24 as universal
per-group payload totals; the six selected directional samples alone happened
to match. This is preserved as a contradiction, not normalized into a field
name.

## Reproduction commands

The following commands are parameterized so no installation path is committed.
They require the repository-local SDK selected by `global.json` and a lawful,
unchanged local `DAT_ROOT`:

```sh
export DOTNET_ROOT="$PWD/local/dotnet"
export PATH="$DOTNET_ROOT:$PATH"

dotnet restore --locked-mode
dotnet build --no-restore
dotnet restore tools/research/Arcanum.ArtProbe/Arcanum.ArtProbe.csproj
dotnet build tools/research/Arcanum.ArtProbe/Arcanum.ArtProbe.csproj --no-restore
dotnet run --project tools/research/Arcanum.ArtProbe/Arcanum.ArtProbe.csproj --no-build -- --self-test

dotnet run --project tools/research/Arcanum.ArtProbe/Arcanum.ArtProbe.csproj --no-build -- \
  --archive base1:"$DAT_ROOT/arcanum1.dat" \
  --archive base2:"$DAT_ROOT/arcanum2.dat" \
  --archive base3:"$DAT_ROOT/arcanum3.dat" \
  --archive base4:"$DAT_ROOT/Arcanum4.dat" \
  --archive tig:"$DAT_ROOT/tig.dat" \
  --archive module:"$DAT_ROOT/modules/Arcanum.dat" \
  --archive vormantown:"$DAT_ROOT/modules/Vormantown.dat"
```

The synthetic self-test drives the same ART validation path with valid raw and
RLE entries, then asserts rejection of a truncated header, an RLE output
overrun, and oversized dimensions. The real-data run reports whether a
candidate corpus is incomplete instead of treating a missing class as a pass.

## Input immutability and observed result

Before and after the real-data run, SHA-256 checks for the seven supplied DAT
files were identical to the R1 values recorded in
[DAT evidence](dat-evidence.md#observed-inventory):

- `arcanum1.dat`: `6dd9d10ef0932e02fc88f0eb5367b1bc37275db51d60bd68ac9bd7ef2d478576`;
- `arcanum2.dat`: `4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d`;
- `arcanum3.dat`: `cbadd76bb3df798fa25dcc05871af1ea751c1164fb1426dccc520cd7d9edc499`;
- `Arcanum4.dat`: `975d43854fca8b7dd74e2b12ef2ff3fe6d3c0f99dc541151d33001469f89710c`;
- `tig.dat`: `c9b48eb6396c514eba3805fb4847df67db3c67ef9f13a79ea21bbf7dd329a09b`;
- `modules/Arcanum.dat`: `8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f`;
- `modules/Vormantown.dat`: `163a9560ad7d684f486167bf91ad6da0fdef8bd7fd1082fc054084611da04345`.

The probe opens sources through `Arcanum.Data` read-only streams and commits no
input, output, or preview. It never writes next to a game archive.

## Limitations and next evidence

- The DAT candidates are user-owned 1.0.7.4 candidates, not proof of canonical
  retail bytes; see DAT evidence for the provenance boundary.
- The selected categories and twelve entries do not establish every ART family,
  raw-pixel ART, version, patch, or runtime resource resolution.
- No independent macOS-safe ART comparator with known non-CE, non-ArtConverter
  lineage was found or executed. Structural and synthetic tests are therefore
  additional safeguards, not an independent decoder comparison.
- Palette channel order, transparency, direction order, row orientation,
  anchors/offsets, action frame, and timing remain unknown. A visually plausible
  local preview would not by itself settle those semantics.
- A later C# decoder task must start from the admitted subset and this
  contradiction ledger; it must not broaden the format or start renderer work
  implicitly.

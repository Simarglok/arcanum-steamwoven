# DAT R1 evidence record

## Purpose and boundary

This is the reproducibility record for the read-only R1 DAT-format pilot. It
records observations of the locally available, user-owned installation
candidates, not canonical/vanilla bytes and not a behavioural oracle. No CE
application, legacy executable, Windows installation, game runtime, writer,
packer, or extraction tool was run. No game bytes, original entry names, or
personal absolute paths are committed.

The probe is [`tools/research/dat_probe.py`](../../tools/research/dat_probe.py).
It uses only Python 3 standard-library modules (`struct`, `pathlib`,
`hashlib`, `zlib`) because that is convenient for a bounded macOS experiment;
it does not select a production technology stack.

## Installation provenance and confidence boundary

`Version.txt` in the investigated installation says `Activision`, `Arcanum`,
and `VERSION:1.0.7.4`. Its `readme.txt` calls itself a 1.0.7.4 readme and
contains a GOG technical-support section. An adjacent macOS application
identifies itself as `com.alexbatalov.arcanum-ce`, version `1.0.0`.

Consequently, the eight files below are **user-owned 1.0.7.4 candidates**.
The version text is direct installation evidence, but it does not prove the
files are an unmodified canonical or vanilla release. The adjacent CE app was
not executed and is not a behavioural reference for this work.

## Observed inventory

Classification is solely by location and filename; an extension or `PATCH0`
name is not evidence of container format or runtime precedence. All fields
below were generated through a read-only open and are aggregate metadata.

| Relative candidate | Location class | Bytes | SHA-256 | Entries | Flags: plain / compressed / directory | Index start / end | Confidence |
| --- | --- | ---: | --- | ---: | ---: | --- | --- |
| `arcanum1.dat` | base candidate | 447,470,816 | `6dd9d10ef0932e02fc88f0eb5367b1bc37275db51d60bd68ac9bd7ef2d478576` | 5,856 | 0 / 5,759 / 97 | 447,153,650 / 447,470,788 | observed candidate, bounded provenance |
| `arcanum2.dat` | base candidate | 147,991,122 | `4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d` | 12,365 | 44 / 12,274 / 47 | 147,357,213 / 147,991,094 | observed candidate, bounded provenance |
| `arcanum3.dat` | base candidate | 9,605,011 | `cbadd76bb3df798fa25dcc05871af1ea751c1164fb1426dccc520cd7d9edc499` | 1,097 | 18 / 1,066 / 13 | 9,545,563 / 9,604,983 | observed candidate, bounded provenance |
| `Arcanum4.dat` | base candidate | 447,614 | `975d43854fca8b7dd74e2b12ef2ff3fe6d3c0f99dc541151d33001469f89710c` | 134 | 1 / 120 / 13 | 440,915 / 447,586 | observed candidate, bounded provenance |
| `tig.dat` | base candidate | 57,149 | `c9b48eb6396c514eba3805fb4847df67db3c67ef9f13a79ea21bbf7dd329a09b` | 28 | 0 / 26 / 2 | 56,032 / 57,121 | observed candidate, bounded provenance |
| `modules/Arcanum.dat` | module candidate | 355,938,305 | `8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f` | 75,591 | 2,907 / 72,378 / 306 | 350,531,338 / 355,938,277 | observed candidate, bounded provenance |
| `modules/Vormantown.dat` | module candidate | 16,060,074 | `163a9560ad7d684f486167bf91ad6da0fdef8bd7fd1082fc054084611da04345` | 12,812 | 15 / 12,770 / 27 | 15,059,316 / 16,060,046 | observed candidate, bounded provenance |
| `modules/Arcanum.PATCH0` | PATCH-name candidate | 18,804 | `d9a3313f71d78115dcac489296bf3d8a5801dd55b20267bd6017b7a898364bce` | 6 | 0 / 4 / 2 | 18,545 / 18,776 | observed candidate, bounded provenance |

For every row the footer marker was `1TAD`, the computed index ended exactly
at the start of the 28-byte footer, and all non-directory payload ranges ended
at or before the index. The measured `name_bytes_hint / tail_region_bytes`
pairs are, in table order:

```text
176590 / 317166; 337117 / 633909; 33088 / 59448; 3451 / 6699;
413 / 1117; 3592751 / 5406967; 693238 / 1000758; 83 / 259
```

### Raw-name and flag aggregates

All 107,889 entry records were NUL-terminated and ASCII-decodable before any
normalization. All had zero forward slashes, zero absolute-like names, and
zero traversal components under the probe's conservative byte tests. The
backslash counts were, in table order: 5,856; 12,365; 1,097; 125; 26; 75,582;
12,800; 4. The entries without a backslash are not interpreted here.

Only `0x00000001`, `0x00000002`, and `0x00000400` occurred. The probe's
experimentally observed mask is exactly those three bits. It reports every bit
outside that mask as unobserved, separately identifying source-reported but
unobserved `0x00000100`, `0x00000200`, and `0x00000800`; all other unobserved
bits remain unknown. The synthetic production-path test asserts that this
separation is retained for a `0x120` fixture. This does not establish a
complete flag vocabulary or reject other versions.

## Bounded payload checks

The deterministic selection rule is: lowest index ordinal among non-empty,
non-directory entries for each requested storage class with stored input at
most 1 MiB and declared output at most 1 MiB. It first prefers entries whose
declared output is at most 64 KiB, then falls back deterministically through
the 1 MiB output cap when no preferred entry exists. At most one plain and one
compressed sample per class were allowed. `arcanum1.dat`, `modules/Arcanum.dat`, and
`modules/Arcanum.PATCH0` were the primary base/module/PATCH choices; a plain
base fallback was taken from `arcanum2.dat` because the primary base has no
observed plain record. No PATCH plain record was observed.

The reproduced combined inventory/comparison command reads exactly five
production-probe payload ranges once each, all in memory, and reuses each
bounded result for reporting and comparison. It retains at most the two
selected outputs for the current archive, not every selected output across the
whole invocation. Arkanum independently accesses five comparator payloads,
once per selected ordinal. The probe rejects a stored input over 1 MiB before
reading it and rejects a declared output over 1 MiB. Compressed samples
additionally had an exact bounded zlib decode, EOF, no unused/trailing or
unconsumed data, and exact declared output length. Excess output, malformed or
incomplete streams, trailing/unconsumed data, and length mismatches are
deterministic `DatProbeError` failures. This confirms only these samples, not
every record.

| Candidate | Ordinal | Storage | Stored bytes | Declared output bytes | Result |
| --- | ---: | --- | ---: | ---: | --- |
| `arcanum1.dat` | 0 | compressed | 53 | 1,186 | all bounded zlib checks passed |
| `arcanum2.dat` | 4,247 | plain | 68 | 68 | exact-size check passed |
| `modules/Arcanum.dat` | 8,022 | plain | 4 | 4 | exact-size check passed |
| `modules/Arcanum.dat` | 3 | compressed | 4,248 | 20,156 | all bounded zlib checks passed |
| `modules/Arcanum.PATCH0` | 1 | compressed | 6,438 | 16,270 | all bounded zlib checks passed |

## Source IDs, provenance, and lineage

| Source | Identifier and integrity evidence | What was used | Lineage / limitation |
| --- | --- | --- | --- |
| AMTUT DBMaker synopsis | [Terra-Arcanum tutorial](https://terra-arcanum.com/amtut/amtut/undat_tutorial.htm) | Reported build/view/unpack modes and compression levels. | Operational documentation; it does not prove this footer/index layout or runtime precedence. |
| DBMaker package | Toolkit `downloads/terra-arcanum/tools/dbmaker.zip`, SHA-256 `efc4e2324591565e119cea1bf541374a9be4087c8f3444c96bf7c798139795f9` | Provenance reviewed as a preserved, unexecuted package. | License/source status not reviewed; no executable was run and it was not a structural comparator. |
| MatuX UnDAT package | Toolkit `downloads/terra-arcanum/tools/UnDAT_MatuX_TMS.zip`, SHA-256 `35c1cf64059e7df38c8959e50bfe39e2787d252797ff2bbcbd1234d85a6bd420` | Read only `undat.cpp`, `undat.h`, `dat2.1.txt`, and readme from the archive; no code was transferred. | Package license is not reviewed; documentation describes partial research and its tool was not run. It is not retail/canonical proof. |
| Arkanum | [`vleon1/Arkanum`](https://github.com/vleon1/Arkanum/tree/ad58733e9f3e7c95055ae34f8c6d997e3b2d8d29), commit `ad58733e9f3e7c95055ae34f8c6d997e3b2d8d29`; local bundle SHA-256 `67c18d146120c49f1a2249214efcda593513ff5767478c5b473b5011e94ea19b` | Inspected `formats/dat.py`; its reader was run unchanged from a temporary local checkout for the comparison below. | No `LICENSE` path exists at the pinned tree. Shared decoder lineage is not established; it is a comparison, not reusable code. |
| ArcNET | [`Bia10/ArcNET`](https://github.com/Bia10/ArcNET/tree/baa7cb5a7b8d1498344cdb5e3170bce8f191fd3c), commit `baa7cb5a7b8d1498344cdb5e3170bce8f191fd3c`; local bundle SHA-256 `398b0daa72fe69eb526bf4ad3a4fb6c33091bc6b233ab065c9cd1f698098dba6` | Inspected DAT directory/entry/flag sources and README; its MIT license was observed. | README credits `arcanum-ce` as the primary archive-format source. It is CE-derived corroboration, not an independent oracle; it was not run. |

Toolkit package hashes above were recomputed from the local files and matched
`toolkit/manifest.json` and `toolkit/SHA256SUMS`; both Git bundles also passed
`git bundle verify`. Preservation and a matching hash establish the saved
bytes, not source authenticity or reuse permission.

## Comparison result

The pinned Arkanum `formats/dat.py` ran on macOS without source changes or
Windows tooling. It matched the R1 probe on all eight entry counts and every
entry's ASCII raw path, compressed/directory classification, declared size,
stored size, and absolute payload offset (107,889 entries total). It also
produced byte-equal output for all five bounded sample ordinals. This is
useful corroboration, but not independent confirmation of the format because
reader lineage is unproven and the pinned project has no license at this
commit.

ArcNET was intentionally not used as an independent comparator: its own README
attributes archive-format knowledge to CE.

## Reproduction commands

The commands are parameterized to avoid committing a local installation or
toolkit path. Supply paths to a lawfully accessible, unchanged installation
and local toolkit archive. `DAT_ROOT` must contain the listed relative files;
`ARKANUM_SOURCE` is a temporary checkout of the pinned Arkanum commit.

```sh
python3 tools/research/dat_probe.py --self-test

python3 tools/research/dat_probe.py \
  "$DAT_ROOT/arcanum1.dat" "$DAT_ROOT/arcanum2.dat" \
  "$DAT_ROOT/arcanum3.dat" "$DAT_ROOT/Arcanum4.dat" "$DAT_ROOT/tig.dat" \
  "$DAT_ROOT/modules/Arcanum.dat" "$DAT_ROOT/modules/Vormantown.dat" \
  "$DAT_ROOT/modules/Arcanum.PATCH0" \
  --sample "$DAT_ROOT/arcanum1.dat" \
  --plain-sample "$DAT_ROOT/arcanum2.dat" \
  --sample "$DAT_ROOT/modules/Arcanum.dat" \
  --sample "$DAT_ROOT/modules/Arcanum.PATCH0" \
  --compare-arkanum "$ARKANUM_SOURCE"

shasum -a 256 \
  "$DAT_ROOT/arcanum1.dat" "$DAT_ROOT/arcanum2.dat" \
  "$DAT_ROOT/arcanum3.dat" "$DAT_ROOT/Arcanum4.dat" "$DAT_ROOT/tig.dat" \
  "$DAT_ROOT/modules/Arcanum.dat" "$DAT_ROOT/modules/Vormantown.dat" \
  "$DAT_ROOT/modules/Arcanum.PATCH0"
```

The synthetic self-test creates only temporary files and exercises the
production `parse_archive()` and `read_bounded_entry()` paths. It covers valid
plain and zlib entries; truncated footer/index/name; directly unreachable
count; out-of-range and overflow-style payload bounds; observed versus
source-reported/unobserved versus unknown flags; declared-length mismatch and
excess output; trailing, incomplete, and malformed zlib streams; stored-input
and output hard-cap rejection; and the deterministic `>64 KiB` fallback below
the 1 MiB output cap. Its combined-orchestration regression wraps the real
production reader and asserts one read for each of two selected ordinals, two
independent comparator accesses, and zero payload accesses when no sample is
requested. It does not require or create a game-data fixture.

## Input immutability

The full eight-row `size | SHA-256` manifest was collected before the first
real-data probe and again after the final real-data comparison. All eight
byte sizes and SHA-256 values matched exactly; the table above is both the
pre- and post-run manifest. The probe opens inputs only with `"rb"`, writes
no extracted content, and writes no output adjacent to an input.

## Known gaps and excluded work

- The installation candidates are not proven canonical/vanilla bytes.
- The `1TAD` observation does not cover ` DAT` or other historical variants.
- The opaque footer bytes and reserved entry bytes remain unknown.
- No conclusion is made about `PATCH0` semantics, archive ordering, loose
  files, module precedence, collision behaviour, case sensitivity, or Unicode
  paths.
- MatuX/DBMaker provenance and licensing do not authorize source reuse; their
  executables were not run.
- Arkanum provides a runnable comparison but has unproven lineage and no
  pinned-tree license; ArcNET is expressly CE-derived.
- No production reader, C# implementation, writer, packer, CLI, engine, ART
  support, Windows/VM setup, CE execution, or runtime experiment was
  implemented.

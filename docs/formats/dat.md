# DAT container: evidence-bounded R1 note

## Scope and status

This document records a **read-only R1 observation** of eight user-owned
Arcanum 1.0.7.4 candidates, not a universal DAT specification and not a
production reader contract. The probes, installation boundary, source
lineage, commands, and exact inventory are in
[the evidence record](../research/dat-evidence.md).

The observed data are sufficient to describe a structural subset. They do
**not** establish archive load order, patch precedence, loose-file
precedence, case semantics, Unicode semantics, all historical DAT variants,
or write/pack behaviour.

## Claim ledger

| Subject | Status | Evidence and boundary |
| --- | --- | --- |
| The observed candidate tail has a `1TAD` marker encoded as four bytes. | experimentally verified | All eight inventories in [`dat-evidence.md`](../research/dat-evidence.md#observed-inventory) passed the bounded footer/index walk. |
| The observed tail is a 28-byte sequence: 16 opaque bytes, `1TAD`, then two little-endian `uint32` fields. | experimentally verified | The R1 probe's `struct.Struct("<16s4sII")` consumes exactly the terminal 28 bytes of every candidate. This **disputes** the earlier 32-byte footer wording in pre-R1 research notes. The leading 16 bytes remain unnamed. |
| First post-marker integer is a name-byte allocation hint; second is an index/tail-region size. | reported / corroborated | Names come from the field names in Arkanum `formats/dat.py` at pinned commit `ad58733e9f3e7c95055ae34f8c6d997e3b2d8d29`; it was not independently semantically established. The measured formula `index_start = file_size - tail_region_bytes` parsed all eight candidates exactly to the pre-footer boundary. |
| Count and entries begin at that computed index start. | experimentally verified | Each index had a reachable count and consumed exactly to `file_size - 28`; every non-directory payload ends at or before index start. |
| Entry layout is `uint32 name_length`, raw NUL-terminated name bytes, four skipped/reserved bytes, then four little-endian `uint32`: flags, declared output size, stored size, offset. | experimentally verified for the observed eight | The R1 probe validated the entire index under this layout; its field-by-field comparison with the runnable pinned Arkanum reader matched all 107,889 entries. The meaning of the skipped four bytes is unknown. |
| Raw name bytes are ASCII-decodable and NUL-terminated. | experimentally verified for the observed eight | All 107,889 observed entries were NUL-terminated and ASCII-decodable. This is an observation, not a Unicode or filename-policy rule. |
| Backslash is the observed separator. | experimentally verified for the observed eight | Forward slash count was zero; backslash counts and the few directory records are listed in the evidence record. No normalization was performed. |
| Flag `0x002` denotes a zlib-compressed stored stream and `0x400` denotes a directory-like record. | experimentally verified only for sampled compression; corroborated for record classification | The `0x002` samples passed bounded zlib checks; all `0x400` records were excluded from payload bounds. MatuX/Arkanum report these interpretations. No claim is made about every historical flag value. |
| Flag `0x001` means plain storage. | experimentally verified on two bounded plain samples; otherwise corroborated | The samples' stored and declared sizes were equal. It occurs in five of the eight candidates. |
| The observed flag mask is exactly `0x001 | 0x002 | 0x400`. | experimentally verified for the eight candidates | The probe reports every bit outside this mask as unobserved. It separately labels `0x100`, `0x200`, and `0x800` as source-reported/unobserved; any remaining unobserved bits remain unknown. Neither category is silently accepted as observed. |
| DAT/DAT1 marker variants share a layout. | unknown | All R1 candidates used `1TAD`; no ` DAT` variant was observed. |

Status vocabulary follows [the research method](../research/method.md):
reported, corroborated, experimentally verified, disputed, and unknown.

## Observed container subset

The following is a description of the subset admitted by
`tools/research/dat_probe.py`:

```text
file
  payload area [0, index_start)
  index area   [index_start, file_size - 28)
    uint32_le entry_count
    repeated entry_count times:
      uint32_le name_length
      byte[name_length] raw_name  # must end in NUL in the observed subset
      byte[4] reserved_or_unknown
      uint32_le flags
      uint32_le declared_output_size
      uint32_le stored_size
      uint32_le payload_offset
  terminal 28-byte footer
    byte[16] opaque
    byte[4]  "1TAD"
    uint32_le name_bytes_hint
    uint32_le tail_region_bytes
```

The validated arithmetic is deliberately strict:

1. The file must be at least 28 bytes.
2. `tail_region_bytes` must be between 28 and the file size.
3. `index_start = file_size - tail_region_bytes`; the index must end exactly
   before the terminal footer.
4. Entry count must be physically reachable before iterating it.
5. A name length must be nonzero, within the remaining index, and include its
   terminal NUL.
6. Each fixed entry tail must remain within the index.
7. For each non-directory record, checked `offset + stored_size` must be no
   later than `index_start`.

These are safety invariants for the observed subset, not assertions about
what a future runtime should silently repair or accept.

## Compression and bounded samples

The probe retains at most one selected payload result per requested storage
class (therefore at most two results, each at most 1 MiB, for the current
archive) and never writes extracted content. A combined inventory and Arkanum
comparison reads each selected production-probe payload once, then reuses that
bounded result for both consumers; Arkanum independently accesses its own
comparator payload once per selected ordinal. The probe rejects a stored input
larger than 1 MiB **before** reading its payload and rejects a declared output
larger than 1 MiB. Selection prefers the lowest eligible ordinal with declared
output at most 64 KiB, but deterministically falls back to the lowest eligible
ordinal up to the distinct 1 MiB output cap; the 64 KiB threshold is not a read
rejection. The R1 selected samples were all at most 20,156 bytes.

A compressed sample passes only if zlib reaches EOF, has neither trailing nor
unconsumed input, and produces exactly its declared output length. Excess
output, malformed or incomplete streams, trailing/unconsumed data, and length
mismatches are rejected as probe errors. A plain sample passes only when stored
and declared lengths agree.

The reproduced combined command made five bounded production-probe reads, once
per selected ordinal (the contract permits at most six), plus five independent
Arkanum comparator accesses:

| Candidate class | Selection role | Entry ordinal | Storage | Stored / declared output bytes | Result |
| --- | --- | ---: | --- | ---:| --- |
| base (`arcanum1.dat`) | first eligible compressed | 0 | compressed | 53 / 1,186 | bounded zlib checks passed |
| base fallback (`arcanum2.dat`) | first eligible plain | 4,247 | plain | 68 / 68 | exact-size check passed |
| module (`modules/Arcanum.dat`) | first eligible plain | 8,022 | plain | 4 / 4 | exact-size check passed |
| module (`modules/Arcanum.dat`) | first eligible compressed | 3 | compressed | 4,248 / 20,156 | bounded zlib checks passed |
| PATCH candidate (`modules/Arcanum.PATCH0`) | first eligible compressed | 1 | compressed | 6,438 / 16,270 | bounded zlib checks passed |

There was no plain entry in `arcanum1.dat` or the PATCH candidate; the plain
base fallback was therefore selected from `arcanum2.dat`. Sample success is
not evidence that all entries or variants are decodable.

## Variants, flags, and unknowns

Every R1 candidate had `1TAD`, passed the same bounded walk, and had no flag
bits outside the experimentally observed mask (`0x001`, `0x002`, `0x400`).
The source-reported but unobserved bits `0x100`, `0x200`, and `0x800` are kept
separate from genuinely unknown bits in probe output; no broader flag vocabulary
is claimed. Counts by candidate, including the small number of records without
backslashes, are intentionally aggregate-only in the evidence record. No
original paths, extracted bytes, or payload digests are committed.

Unresolved questions include:

- semantic meaning and stability of the 16 opaque footer bytes;
- semantic meanings of the two footer integers beyond the measured index
  formula and exact name-byte correspondence;
- meaning of the four pre-flag entry bytes;
- whether `0x400` directory records can have payload semantics in another
  variant;
- values and semantics of unobserved flag bits;
- ` DAT` marker layout, other version layouts, offset base variants, duplicate
  offsets, and overlap policy;
- pathname encoding beyond ASCII decodability, case folding, separator
  normalization, and traversal handling;
- archive/module/PATCH resolution order and conflict precedence.

## Requirements for a later reader task

A later, separately authorized reader should retain checked arithmetic,
explicit variant recognition, raw-path handling before any normalization,
configurable decompression caps, exact compressed-stream completion checks,
and an explicit rejection policy for unobserved flags and malformed bounds.
It must not infer resource precedence from this container research. Writer,
packer, extraction CLI, runtime integration, and precedence behaviour are
not implemented by R1.

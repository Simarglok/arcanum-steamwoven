# Crash Site evidence ledger

## Scope and provenance

This ledger records ARC-RES-003's one-sector research result. The user-owned
original data was read only. The repository contains no extracted game bytes,
asset names, previews, installation paths, credentials, or third-party source
copy.

| Source | Identifier | Use | Status / limit |
| --- | --- | --- | --- |
| Observed module archive | `Arcanum.dat`, 355,938,305 bytes, SHA-256 `8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f` | exact map and sector selectors | experimentally verified local input; not a retail-canonical or precedence claim |
| Project reader | `src/Arcanum.Data/DatArchive.cs` | bounded DAT opening and in-memory entry decode | independently authored project implementation |
| Project ART decoder | `src/Arcanum.Data/ArtDecoder.cs` | designated future ART metadata/frame decoder | no selected Crash Site ART path was established here |
| Pinned CE source | `a7ff41b300ef712f0e7d088183a3d08957110cdd` | sector sequence, coordinate packing, isometric relation, ART-ID context | one external lineage, not independent corroboration |
| Community catalogue | preserved toolkit metadata | Crash Site context | reported only; not byte-level map proof |

The CE revision was resolved by its full immutable commit ID before inspecting
these focused paths: `src/game/sector.c`, `src/game/sector_light_list.c`,
`src/game/sector_tile_list.c`, `src/game/sector_object_list.c`,
`src/game/location.c`, and `first_party/tig/src/art.c`.

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
```

Use the project's configured .NET 8 SDK. The module path is supplied locally
and must not be committed or reported.

## Executed results

| Check | Result |
| --- | --- |
| Crash Site probe synthetic suite | PASS: a synthetic in-memory DAT drives `DatArchive.Open`/metadata enumeration through referenced-prototype selection, `ReadEntry`/decompression, `ParsePrototype`, and `ResolveCurrentAids`; it preserves sector-boundary cases; ObjectID forms 0/1/2/3 and invalid/runtime/prototype forms; all supported type layouts plus rejected 18/19; masks/counts/presence; string/array/bitset bounds, sizes, NUL, population and truncation; canonical lookup grammar plus missing/duplicate/malformed/own-ID/type/non-A/range failures; direct/inherited concrete ART values; late variable fields; and numeric projection examples. The archive tracking fixtures prove unrelated and malformed-name payload ranges are not read. |
| Read-only selected-sector run | FAIL CLOSED (expected): input size/SHA were unchanged before/after; sector aggregation still resolves ordinal 463, 47,291 decoded bytes, a 30,899-byte / 317-record window; its first LE word is unsupported version `1`, so no object bytes, IDs, names, or prototype candidates are emitted or interpreted |
| Existing DAT synthetic tests | PASS: canonical SDK 8.0.425 locked run of Arcanum.Data.Tests with DAT_ROOT set to the user-owned inputs passed 25/25 (0 failed, 0 skipped). |
| Existing Python DAT probe synthetic suite | PASS: `python3 tools/research/dat_probe.py --self-test` |

The real-data aggregate art-type counts are recorded in `docs/formats/maps.md`.
They intentionally do not enumerate raw IDs or asset names.

## Probe boundary

`Arcanum.CrashSiteProbe` reuses `DatArchive`; it adds a narrowly bounded
sector-prefix inspection plus a supported-v119 synthetic object/prototype
delimiter. It caps the decoded sector and each decoded selected prototype at
1 MiB and validates all arithmetic before a count-dependent region is read.
The selected real run rejects before record 0, so it does not parse its object
records or any PRO entry. The supported synthetic path retains only direct or
inherited current-AID values and the optional selected location needed to
validate resolution; it writes or exports no decoded bytes, names, or IDs.

The strict synthetic mode rejects an unexpected non-tile high-nibble class.
The real probe records those classes rather than assuming the whole table has
one resource semantics; this is why it is evidence collection, not a loader.

## Verified fail-closed object delimiter

The real sector does establish the exact byte window that needs an object
schema: with zero lights, the 4,096-word tile table occupies decoded offsets
`4..16387`, the terminal count occupies `47287..47290`, and the intervening
`30,899` bytes contain the 317 serialized objects. These values are aggregate
measurements only; no object bytes, names, or identifiers are retained.

Pinned CE's `objlist_load` is a source-derived explanation of this boundary:
after the fixed tile list it first reads the final count, returns to the object
window, calls its object reader that many times, then reads the final count
again. Its `obj_read` first consumes a version and object identifier, selects
prototype versus instance layout from that identifier, and the instance layout
then consumes prototype/object identifiers, a validated type, a field-count,
a type-dependent override-bitset, and the payload for every set field. Some
field payloads are not fixed-width. This is one CE lineage, not independent
format confirmation.

The bounded probe now implements the complete delimiter required by that
loader order for the pinned CE version-119 form. It validates version 119, both persisted ObjectIDs, concrete
type, exact mask-word count, valid mask bits and popcount, then skips a
specified payload for **every** set field in order. This includes all later
variable-width values after common current ART ID and location; those early
fields are not record boundaries. It accepts only canonical optional presence
bytes, bounded strings, and bounded sparse arrays with validated element size
and bitset population. The probe would parse exactly 317 records from offsets `16388..47287` or
reject the module without retaining field payloads.

If current ART ID is not directly overridden, the probe's synthetic in-memory
DAT path opens the production archive and selects only a referenced exact
canonical-A candidate before reading any candidate payload. It verifies the
canonical raw-name grammar, uniqueness before decode, candidate own-A-ID
equality, and complete versioned entry; tracking confirms same-prefix/non-`.pro`
and unrelated entries have zero payload reads. The inherited ART ID is its
first common I32 field. Missing, duplicate, malformed, unlocatable, or
mismatched candidates are a fail-closed result. The full wire contract and
pinned function-level lineage are in `docs/formats/maps.md`; the source list
now includes `sector_object_list.c`, `obj.c`, `obj.h`, `obj_private.c`,
`obj_id.h`, `sa.c`, `proto.c`, and `obj_pool.c` at the immutable CE revision.

Terrain is independently blocked at the equally small lookup boundary: the
fixed table contains both the CE-reported tile and facade high-nibble classes,
but this research has not established the type-specific ART-ID
number/variation decomposition and `ART ID -> selected resource path` rule for
either class. `DatArchive` can then locate a raw exact-name entry and
`ArtDecoder.Inspect` can inspect a bounded entry, but neither can supply that
missing resolver. No fallback path, archive winner, frame, baseline, or order
is inferred.

The selected read-only module fails this exact contract at the first window
word: it is LE signed `1`, not version `119`. Therefore the probe does not
weaken its supported form or attempt an offset heuristic, and no record or
prototype claim is made for that input. This is a precise fail-closed blocker:
the pinned CE form is sufficient as a delimiter design but does not establish
the selected retail window's versioned object envelope. Terrain lookup and
ART/frame/baseline/order evidence remain separate research gates.

## Known limitations and resulting readiness

- The exact mapping from selected tile words to lookup table and ART path is
  unknown.
- The selected retail input is rejected before record 0 because its observed
  first word is unsupported version 1; it has no delimiter/prototype claim.
- No source precedence, frame/palette selection,
  baseline, occlusion, or scene comparison was measured.
- The CE location formula is source-derived and numeric synthetic checks verify
  only its relative arithmetic, not visual parity.

Accordingly, the C# loader and static Godot scene are both **blocked**, not
ready. Their bounded research gates are stated in
`docs/world/static-scene-contract.md`.

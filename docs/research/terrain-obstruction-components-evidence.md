# Terrain obstruction component evidence (ARC-DATA-006)

## Boundary and provenance

This is a bounded production-data record for three independent terrain obstruction components in one selected 64×64 base sector. It does not establish combined occupancy, actor traversal, navigation, collision, runtime state, source precedence, or original-runtime parity. The underlying serialized-mask, facade-bit, and tile-name claims are read from the executable's functions listed in [selected-sector passability evidence](sector-passability-evidence.md#source-trace-and-exact-mapping), level F0 (not checked in the executable); earlier provenance is in git history.

The precise fixed selector and immutable input fingerprints remain in [selected-sector passability evidence](sector-passability-evidence.md). The public component API and admitted grammar are in [terrain obstruction components](../world/terrain-obstruction-components.md). Original files were read only; this record intentionally contains no local paths, original names, MES values, decoded bytes, masks, grids, or visual assets.

## Fixed-profile aggregate check

The opt-in Crash Site probe called `TerrainObstructionInspector` directly, emitted its parser-derived aggregate, then repeated module and explicit-resource length/SHA-256 fingerprint checks before declaring integrity. It also verified selector ordinal `463`, compressed flag `0x00000002`, decoded length `47,291`, and the existing six-prototype manifest. The aggregate was:

| Independent component/domain | Blocked | NoBlock | Unknown |
| --- | ---: | ---: | ---: |
| Serialized 512-byte mask / 4,096 cells | 0 | 4,096 | 0 |
| Class-11 facade raw bit / 1,063 cells | 976 | 87 | 0 |
| Class-0 tile-name rule / 3,033 cells | 0 | 2,942 | 91 |

The tile-name row is a component aggregate, not a passability result. Its 91 strict-subset unknown values remained null dispositions. The three tile-name counts sum to the independently measured 3,033 class-0 cells. The zero serialized-mask set-bit count is observed only for this selected input and does not imply the clear mask is walkable.

## Verification matrix

Synthetic API tests cover direct 511/512/513-byte mask admission and owned input lifetime; set and clear little-endian word/row/final boundaries through both the snapshot and report; X/Y/ordinal rejection and sanitized world-coordinate overflow; absent prior framing; class-11 raw-bit polarity with unrelated bits; every tile-name category's sorted ordinal endpoints and unresolved mappings; admitted duplicate `b`/`f` and separate non-block flags, conservative no-slash values, and complete sound-token syntax including a long syntax-only decimal; brace and other unsupported values remain Unknown/null. They also cover malformed MES structure and decoded/record/value caps as typed sanitized failures; missing or ambiguous name entries; component disagreements and NotApplicable/Unknown/Unsupported null-disposition invariants; safe nonzero selected numeric-entry provenance; printable non-path source IDs; and report snapshot lifetime after mutating actual DAT tile-word and serialized-mask bytes plus MES backing bytes before archive disposal. Existing terrain framing tests separately cover malformed/truncated/overlapping sector sections and configured limits. The Crash Site probe self-test remains a regression sanity check; it is separate from the identity-bound local run.

Full passability remains **NOT READY** because static object spatial contribution, doors and dynamic state, actor/context capability, diagonal/corner/edge behavior, cross-sector state, PATCH/dif/loose precedence, and original-runtime comparison have not been admitted. See [the retained passability contract](../world/passability-contract.md) for those explicit gaps.

# Terrain obstruction component evidence (ARC-DATA-006)

## Boundary and lineage

This is a bounded production-data record for three independent terrain obstruction components in one selected 64×64 base sector. It does not establish combined occupancy, actor traversal, navigation, collision, runtime state, source precedence, or original-runtime parity. The underlying serialized-mask, facade-bit, and tile-name claims are attributed to [`alexbatalov/arcanum-ce` commit `a7ff41b300ef712f0e7d088183a3d08957110cdd`](https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd) as specification lineage. No CE implementation was copied or mechanically translated.

The precise fixed selector and immutable input fingerprints remain in [selected-sector passability evidence](sector-passability-evidence.md). The public component API and admitted grammar are in [terrain obstruction components](../world/terrain-obstruction-components.md). Original files were read only; this record intentionally contains no local paths, original names, MES values, decoded bytes, masks, grids, or visual assets.

## Fixed-profile aggregate check

The opt-in Crash Site probe called `TerrainObstructionInspector` directly after before/after fingerprint verification of the selected module and explicit resource. It also verified selector ordinal `463`, compressed flag `0x00000002`, decoded length `47,291`, and the existing six-prototype manifest. The parser-derived aggregate was:

| Independent component/domain | Blocked | NoBlock | Unknown |
| --- | ---: | ---: | ---: |
| Serialized 512-byte mask / 4,096 cells | 0 | 4,096 | 0 |
| Class-11 facade raw bit / 1,063 cells | 976 | 87 | 0 |
| Class-0 tile-name rule / 3,033 cells | 0 | 2,942 | 91 |

The tile-name row is a component aggregate, not a passability result. Its 91 strict-subset unknown values remained null dispositions. The three tile-name counts sum to the independently measured 3,033 class-0 cells. The zero serialized-mask set-bit count is observed only for this selected input and does not imply the clear mask is walkable.

## Verification matrix

Synthetic API tests cover exact 512-byte mask interpretation, little-endian word/row/final boundaries, absent prior framing, checked coordinate rejection, class-11 raw-bit polarity, all four tile-name category selections, `b`/`f` and non-block/suppressed values, unsupported packed classes, missing name source, strict grammar unknown behavior, independent result values, and report lifetime after archive disposal. Existing terrain framing tests cover malformed/truncated/overlapping sections and configured limits. The Crash Site probe self-test remains a regression sanity check; it is separate from the identity-bound local run.

Full passability remains **NOT READY** because static object spatial contribution, doors and dynamic state, actor/context capability, diagonal/corner/edge behavior, cross-sector state, PATCH/dif/loose precedence, and original-runtime comparison have not been admitted. See [the retained passability contract](../world/passability-contract.md) for those explicit gaps.

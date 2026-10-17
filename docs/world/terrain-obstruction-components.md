# Bounded terrain obstruction components

## Scope

`Arcanum.Data.Terrain.TerrainObstructionInspector` is a Godot-independent, fixed-64-by-64 snapshot/query API. `TerrainSerializedBlockMask.Parse(ReadOnlySpan<byte>)` is the separately usable exact-size, owned snapshot for a raw serialized mask: it accepts exactly 512 bytes, interprets 128 little-endian words, and exposes checked coordinate or ordinal component queries. It reports three independent source components for one selected local cell:

- `SerializedBlockMask`, decoded only from the framed 512-byte / 128 little-endian `uint32` section. Ordinal is `x + 64*y`; a set bit is `Blocked` and a clear bit is `NoBlock` for this component.
- `FacadeWalkabilityBit`, applicable only to packed class 11. Raw bit 0 set is `NoBlock`; clear is `Blocked` for this component.
- `TileNameRule`, applicable only to packed class 0. It projects the first material number through the explicitly supplied tile-name MES source.

The API has no `IsWalkable`, `IsPassable`, combined cell value, step value, implicit OR, navigation rule, or actor verdict. `NoBlock` means only that this one admitted component contributes no blocker. Static spatial data, dynamic doors/state, actor capability, diagonal/corner/edge behavior, cross-sector handling, and source precedence remain outside this API and the [passability contract is still NOT READY](passability-contract.md).

## Input, ownership, and results

Call `TerrainObstructionInspector.InspectSector(moduleArchive, moduleSourceId, selector, tileNameSource, options)`. Both archives are explicitly selected and caller-owned; the API neither searches an alternative archive nor disposes or retains either archive. `moduleSourceId` and `TerrainTileNameSource.SourceId` are non-path ASCII labels. The tile source supplies one exact MES entry path and one admitted path profile. Passing no tile source preserves mask/facade inspection and reports tile-name `Unknown`.

The returned `TerrainObstructionSectorReport` owns copies of sector words, the optional block mask, and accepted MES projections. It remains usable after caller data mutate or caller archives are disposed. Its `Sector` exposes only safe numeric entry/coordinate dimensions, never the caller selector's source path or label. `GetCell(localX, localY, TileNameRuleMode)` rejects coordinates outside `0..63`; checked world coordinates derive from the selected sector origin.

Every result independently includes `Supportedness` (`Supported`, `Unknown`, `Unsupported`), `Applicability` (`Applicable`, `NotApplicable`, `Unknown`), nullable `Disposition` (`Blocked`, `NoBlock`), an uppercase stable reason token, and safe provenance. Successful serialized-sector, facade, and MES provenance retains the selected numeric DAT entry ordinal; missing or ambiguous MES selection deliberately has no invented ordinal. A disposition exists only for `Supported + Applicable`. Unknown, unsupported, and not-applicable results always have null disposition; unknown never becomes `NoBlock`.

## Tile-name subset

The optional tile source is the exact caller-selected MES resource. The parser is the original MES grammar (`MesFile`, ARC-DATA-027) with decoded-byte, record, and value caps: text outside braces is ignored, and the shapes the original truncates or stops on (a NUL, a lone CR, a 0xFF or 0x1A byte, a brace inside a field, a number that is not 1..10 digits within int32, a duplicate key, an unterminated field, a text over 1,999 bytes) or an empty table fail closed with a sanitized `TerrainException`; missing or ambiguous selected MES makes only the tile-name component `Unknown`.

For a class-0 tile, `num1 = (rawId >> 22) & 63` selects an ordinal in one of four sorted MES key ranges: outdoor/flippable `0..99`, outdoor/fixed `100..199`, indoor/flippable `200..299`, indoor/fixed `300..399`, using `(rawId >> 8) & 1` and `(rawId >> 7) & 1`. Missing category ordinal or an unresolved `num1` mapping is `Unknown`; the implementation never assumes a key is a range base plus material number.

A selected value is admitted only when it is printable ASCII with a nonempty prefix and either:

1. no slash and exactly a conservative three-character token; or
2. one slash followed by zero or more of `s`, `b`, `f`, `i`, `n`, `p`, then optional ASCII spaces and an optional complete signed or unsigned decimal sound token.

Within that subset, `b` or `f` means `Blocked`; recognized non-block flags or no flags mean `NoBlock`. Duplicate recognized flags are idempotent. Extra slash, unknown flag, control/NUL/brace, malformed numeric tail, trailing text, or other relevant syntax returns `Unknown + null`, never an open result. `TileNameRuleMode.SuppressedByCaller` produces `Supported + NotApplicable + null` only for the tile-name component.

## Provenance and boundary

The serialized-mask, raw facade-bit, tile category, and `b`/`f` facts are attributed specification lineage from [arcanum-ce commit `a7ff41b300ef712f0e7d088183a3d08957110cdd`](https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd). No CE code was copied or mechanically translated; this is not original-runtime validation or a clean-room proof. The related selected-sector source record is [sector passability evidence](../research/sector-passability-evidence.md).

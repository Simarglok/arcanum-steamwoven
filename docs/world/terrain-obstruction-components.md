# Bounded terrain obstruction components

## Scope

`Arcanum.Data.Terrain.TerrainObstructionInspector` is a Godot-independent, fixed-64-by-64 snapshot/query API. `TerrainSerializedBlockMask.Parse(ReadOnlySpan<byte>)` is the separately usable exact-size, owned snapshot for a raw serialized mask: it accepts exactly 512 bytes, interprets 128 little-endian words, and exposes checked coordinate or ordinal component queries. It reports three independent source components for one selected local cell:

- `SerializedBlockMask`, decoded only from the framed 512-byte / 128 little-endian `uint32` section. Ordinal is `x + 64*y`; a set bit is `Blocked` and a clear bit is `NoBlock` for this component.
- `FacadeWalkabilityBit`, applicable only to packed class 11. Raw bit 0 set is `NoBlock`; clear is `Blocked` for this component.
- `TileNameRule`, applicable only to packed class 0. It projects the first material number through the explicitly supplied tile-name MES source.

The API has no `IsWalkable`, `IsPassable`, combined cell value, step value, implicit OR, navigation rule, or actor verdict. `NoBlock` means only that this one admitted component contributes no blocker. Static spatial data, dynamic doors/state, actor capability, diagonal/corner/edge behavior, cross-sector handling, and source precedence remain outside this API and full passability is still NOT READY (see the [traversal research contract](area-traversal-contract.md)).

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

The serialized-mask, raw facade-bit, tile category, and `b`/`f` facts are a reading of the original's functions, evidence level F0 (not checked in the executable); this is not original-runtime validation or a clean-room proof. The related selected-sector source record is [sector passability evidence](../research/sector-passability-evidence.md).

## Fixed-profile aggregate check and tests (ARC-DATA-006)

The opt-in Crash Site probe called `TerrainObstructionInspector` on the fixed selector (ordinal `463`, compressed flag `0x00000002`, decoded length `47,291`, six-prototype manifest), emitted the parser-derived aggregate, then repeated the module and explicit-resource length/SHA-256 fingerprint checks. Original files were read only; nothing local or original is recorded. The fixed selector and fingerprints stay in [selected-sector passability evidence](../research/sector-passability-evidence.md).

| Component / domain | Blocked | NoBlock | Unknown |
| --- | ---: | ---: | ---: |
| Serialized 512-byte mask / 4,096 cells | 0 | 4,096 | 0 |
| Class-11 facade raw bit / 1,063 cells | 976 | 87 | 0 |
| Class-0 tile-name rule / 3,033 cells | 0 | 2,942 | 91 |

These are component aggregates, not passability results: the 91 strict-subset tile-name unknowns keep null dispositions, and the zero set-bit count of the mask holds only for this input and does not make the clear mask walkable.

Synthetic API tests cover 511/512/513-byte mask admission and owned input lifetime; set and clear word/row/final boundaries through the snapshot and the report; X/Y/ordinal rejection and sanitized world-coordinate overflow; class-11 raw-bit polarity with unrelated bits; the sorted ordinal endpoints of every tile-name category and unresolved mappings; the admitted `b`/`f`, non-block and sound-token syntax and the Unknown/null rest; malformed MES structure and the decoded/record/value caps as typed sanitized failures; missing or ambiguous name entries; the null-disposition invariants; and report lifetime after mutating the DAT tile words, mask and MES bytes before archive disposal. Terrain framing tests cover malformed sector sections and limits. The Crash Site probe self-test is a separate regression check.

# Bounded map and sector notes

## Status

The separately scoped [complete exterior data owner](../world/crash-site-area-loader.md)
now applies the bounded framing to the selected multisector area and reconciles
its normal-game aggregate. It does not make the historical one-sector observations
below a universal sector/map schema or establish runtime scene parity.

`terrain.tdf` and the template sectors that stand in for missing sector files are
researched separately in
[generated-wilderness-evidence.md](../research/generated-wilderness-evidence.md)
(ARC-RES-034).

This note specifies only the inspected Crash Site sector described in
[crash-site-slice.md](../research/crash-site-slice.md). The claims below are
not a universal map-file format specification.

## Verified container boundary

`DatArchive` reads the observed module's supported `1TAD` container subset,
keeps raw entry names unnormalised, bounds entry payloads, and requires exact
zlib output length for compressed entries. For the selected input it resolves
the map list, map properties, and one sector by exact raw-name equality.

The selected sector entry is compressed (`0x00000002`) and decodes to 47,291
bytes. The probe applies a 1 MiB stored/decoded cap before reading it. It does
not extract it to disk.

## Sector framing for the frozen input

The following byte order is **corroborated from the source loader order and
experimentally bounded on this one input**. The complete section ledger and
the read-only probe limits are in
[crash-site-framing.md](../research/crash-site-framing.md); this is not a
universal sector specification.

| Offset rule | Interpretation in this probe | Validation |
| --- | --- | --- |
| `0` | signed LE light count | non-negative and at most 1,024 |
| `4 + count * 48` | start of 4,096 LE 32-bit ART-ID words | checked arithmetic and complete in-buffer range |
| `16388` on this input | roof `empty` discriminator | LE `i32`; `1` selects no 256-word roof array |
| `16392..17140` on this input | placeholder-controlled script, metadata/sound, and block sections | `0xAA0004` selects all listed sections; each count and length is bounded |
| `17140..47287` on this input | object-record window | terminal count controls a bounded delimiter |
| `decoded_length - 4` | signed LE terminal object count | non-negative and at most 4,096; observed `317` |

Read as source (F0, not checked in the executable), the original's sector load
reads a light list, a fixed 4,096-element tile list, roof and conditional intermediate
sections, then an object list whose count is at the stream end. The actual
input has `light_count = 0`, `tile_count = 4096`, an object window beginning at
`17140`, and terminal count `317`. The bounded delimiter reaches `47287` after
317 version-119 records. The separately bounded ARC-RES-007 result classifies
the selected records with its narrow type-3/static predicate and resolves
current ART; this framing observation alone establishes neither that chain nor
placement or draw order.

## Coordinates and sector address

The original's sector-from-location function (0x4CFC50) maps location components with `>> 6`; its sector
macro packs X in bits `0..25` and Y in bits `26..51`. With the frozen start
location this yields:

```text
sector_x = 92958 >> 6 = 1452
sector_y = 82592 >> 6 = 1290
sector_id = sector_x | (sector_y << 26) = 86570436012
local_origin = (1452 << 6, 1290 << 6) = (92928, 82560)
```

This is source-derived and verified against the selected entry selector, but is not
a promise for every map source. The probe's synthetic test rejects an impossible
light count, a truncated computed tile table, a negative terminal count, and a
non-tile identifier when strict type checking is requested.

## Terrain ART-ID status

A reading of the original's ART identifier code (F0) identifies a type field in
the high four bits. The selected words occupy two classes, and the bounded
companion probe now handles only those tile and facade branches. The real run reported counts
by high nibble:

```text
3033, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1063, 0, 0, 0, 0
```

Under the source enum, these are tile and facade classes respectively. The
type-specific MES callback lookup, exact one-source ART selection, frame
selection, and coverage failures are recorded in
[crash-site-terrain-resolution.md](../research/crash-site-terrain-resolution.md).
that investigation resolves all 4,096 records in its explicit one-source run.
Its R2 addition defines source-derived diagnostic tile/editor-facade footprints,
crop and raw-tile-flip behavior, but not source precedence, lighting, a normal
game facade pass, or a complete terrain scene.

## Object-window status

For this exact base sector, the corrected object cursor is `17140`, not
`16388`, and the bounded version-119 delimiter consumes 317 records through
`47287`, immediately before the terminal count. The LE word `1` at `16388` is
the roof `empty` discriminator; it is not a retail object-version claim.

The framing note establishes a framed, bounded record window only. The separate
ARC-RES-007 result records the selected-version envelope, type-3/static
predicate, current-ART/prototype resolution, and five ART metadata chains in
`crash-site-objects.md`. Neither result authorizes a full object parser or
establishes an object baseline or ordering rule; those omissions remain
deliberate static-scenery blockers rather than holes a future loader may fill
by default.

## Required follow-up before static-scenery resolution

A future, separately authorized placement investigation must retain the
ARC-RES-007 selected-version envelope and selected current-ART/prototype
chains, then prove each admitted object's visual placement/overlap chain. It
must use `DatArchive` and `ArtDecoder.Inspect`/`Decode` for a bounded selected
ART entry rather than reimplementing either decoder. Patch and loose-file
precedence, scenery baseline, and occlusion ordering remain unknown for this
slice.

## Sources and lineage

- Project implementation: `src/Arcanum.Data/DatArchive.cs`.
- A source reading (evidence level F0, not checked in the executable) of the
  original's sector, sector light/tile/object list, object, object id, sparse
  array, prototype, object pool, script, quest and ART identifier code.

Source-derived readings are one lineage and are not independent confirmation.
This independently authored note records constrained behavior.

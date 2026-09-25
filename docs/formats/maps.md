# Bounded map and sector notes

## Status

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

## Sector prefix subset

The following byte order is **corroborated from pinned CE loader order and
experimentally bounded on this input**, with medium confidence:

| Offset rule | Interpretation in this probe | Validation |
| --- | --- | --- |
| `0` | signed LE light count | non-negative and at most 1,024 |
| `4 + count * 48` | start of 4,096 LE 32-bit ART-ID words | checked arithmetic and complete in-buffer range |
| `decoded_length - 4` | signed LE terminal object count | non-negative and at most 4,096 |

Pinned CE revision `a7ff41b300ef712f0e7d088183a3d08957110cdd` loads a light
list, a fixed 4,096-element tile list, then later reads an object list whose
count is at end of the stream. The project probe deliberately does not walk the
opaque light records or object records.

The actual selected input has `light_count = 0`, `tile_count = 4096`, and a
terminal count of `317`. This does not establish that all 317 records are
static scenery or that the bytes between the tile table and terminal count are
self-delimiting without the broader object schema.

## Coordinates and sector address

Pinned CE `sector_id_from_loc` maps location components with `>> 6`; its sector
macro packs X in bits `0..25` and Y in bits `26..51`. With the frozen start
location this yields:

```text
sector_x = 92958 >> 6 = 1452
sector_y = 82592 >> 6 = 1290
sector_id = sector_x | (sector_y << 26) = 86570436012
local_origin = (1452 << 6, 1290 << 6) = (92928, 82560)
```

This is CE-derived and verified against the selected entry selector, but is not
a promise for every map source. The probe's synthetic test rejects an impossible
light count, a truncated computed tile table, a negative terminal count, and a
non-tile identifier when strict type checking is requested.

## ART-ID caution

Pinned CE's ART source identifies its tile ART encoding with a type field in
the high four bits, but the real selected tile-table words span more than that
one class. The probe therefore reports only aggregate type counts and does not
interpret every word as a tile filename. The initial real run reported counts
by high nibble:

```text
3033, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1063, 0, 0, 0, 0
```

Under the pinned CE enum, these are tile and facade classes respectively. The
observed mixture still does not establish a universal direct `tile word -> ART
path` shortcut or the required MES resolver. It is not an asset inventory.

## Required follow-up before resource resolution

A future, separately authorized parser must bound and identify the relevant
MES lookup source, distinguish invalid/sentinel and non-tile values, and prove
the path selected for a small fixed set of actual terrain or scenery records.
It must then use `ArtDecoder.Inspect`/`Decode` for a selected bounded ART entry
rather than reimplement ART decoding. Patch and loose-file precedence remain
unknown for this slice.

## Sources and lineage

- Project implementation: `src/Arcanum.Data/DatArchive.cs`.
- Pinned CE revision `a7ff41b300ef712f0e7d088183a3d08957110cdd`:
  `src/game/sector.c`, `src/game/sector_light_list.c`,
  `src/game/sector_tile_list.c`, `src/game/sector_object_list.c`, and
  `first_party/tig/src/art.c`.

CE and CE-derived sources remain one lineage and are not independent
confirmation. This independently authored note records constrained behavior;
it does not copy CE implementation structure or license terms.

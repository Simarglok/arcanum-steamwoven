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

## Canonical bounded object delimiter (Route B)

The fixed sector framing and the following **fail-closed, span-only** object
delimiter are now specified for this one input. This is not an object model or
a reusable game loader. It is independently authored from the pinned CE
lineage, which establishes loader order and field categories at revision
`a7ff41b300ef712f0e7d088183a3d08957110cdd` in
`src/game/sector_object_list.c::objlist_load`, `src/game/obj.c::obj_read`,
`obj_inst_read_file`, `object_inst_enumerate_overridden_fields`,
`sub_40A8A0`, and `sub_40C030`, `src/game/obj.h::ObjectField,ObjectType`, and
`src/game/obj_private.c::obj_data_read_file_fast,bitset_read_file`.

For this exact sector, the object cursor starts at `16388`, reads exactly 317
records, and must finish at `47287`, immediately before the terminal signed
count. The probe rejects a cursor mismatch, any record version other than LE
signed `119`, a first ObjectID with the prototype (`-1`) or runtime (`-2`)
discriminator, and types outside concrete values `0..17` (including pseudo
types 18 and 19). It accepts persisted ObjectID forms null (0), A (1), GUID
(2), and P (3): each is 24 bytes, comprising its signed LE discriminator, six
uninterpreted header bytes, and a 16-byte union. Equality uses only the form's
meaningful union fields; the header bytes are neither interpreted nor emitted.

An instance has two ObjectIDs, LE signed type, LE signed 16-bit field count,
and then exactly this many LE `u32` mask words: wall, portal, container,
scenery, projectile, and trap use 3; item-derived types 5..14 use 4; PC and
NPC use 5. The common group is 36 fields in words 0..1; a direct group begins
at word 2, item-derived types use item then subtype words, and critters use
32+1 critter bits followed by their subtype word. Only valid bits may be set;
the signed field count must equal their popcount and is capped at 94.

Every set bit is consumed in group/ordinal order, including bits after current
ART ID and location. The compact probe descriptor covers common (36), wall
(5), portal (8), container (11), scenery (6), projectile (8), item (23),
weapon (27), ammo (7), armor (12), gold (6), food/scroll/key/generic/trap (5
each), key-ring (6), written (8), critter (33), PC (25), and NPC (25) fields.
Its only wire kinds are fixed I32; optional I64; optional ObjectID; optional
NUL-terminated string; and sparse arrays of I32/U32, I64/U64, 12-byte script,
16-byte quest, or 24-byte ObjectID items. This descriptor deliberately stores
no field model or game values.

Optional values use only presence bytes 0 or 1. Present I64 values consume 8
bytes; present ObjectIDs consume a canonical persisted ObjectID; present
strings consume signed length plus exactly `length + 1` bytes and require the
final NUL. Present arrays consume signed element size, count, and legacy slot,
then `size * count` data bytes, followed by signed bitset-word count and that
many `u32` words. Expected element sizes are 4, 8, 12, 16, and 24 respectively.
Counts are non-negative, multiplication is checked, bitsets have at least two
words, and bitset population must equal element count. No allocation follows a
serialized count; every skip remains inside the selected 1 MiB entry cap and
the remaining record window.

When common bit 0 is set, the probe retains that I32 as the direct current ART
ID; it also retains common bit 1's optional location only while validating the
supported record. Otherwise it resolves the first ObjectID against exactly one
referenced canonical prototype entry in the same immutable module, without
printing its name or identifier. The raw-byte grammar is exactly
`[Pp][Rr][Oo][Tt][Oo]\\DDDDDD - LABEL.[Pp][Rr][Oo]\\0`: ASCII letter folding
applies only to `proto` and `pro`; the backslash, six ASCII digits, spaces,
hyphen, period, and terminal NUL are literal. `LABEL` is non-empty opaque
material with no NUL, `/`, or `\\`, and no bytes may follow the terminal NUL.
The supported A range is `0..999999`; non-A or out-of-range referenced IDs
fail before payload reads. The probe enumerates metadata, groups only wanted
canonical candidates, and requires exactly one candidate per wanted A value
before decoding any payload. Same-prefix malformed, non-`.pro`, and unrelated
canonical names are ignored without reads. A decoded prototype is independently
validated as version 119, first blocked ObjectID marker, own ObjectID, concrete
type, exact type mask count, then every valid common/base/subtype field (not
merely marked fields); its own A ID must equal both the filename and requested
value. Its first common I32 is returned as the concrete inherited current ART
ID, and type agreement remains a resolver check. Missing, duplicate, malformed,
or mismatched candidates fail closed. This follows CE `proto.c::proto_init`,
`obj.c::obj_field_fetch`, `obj_get_prototype_handle`, and
`obj_pool.c::obj_pool_perm_lookup`; no default ART ID or recursive inheritance
is invented.

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
  `src/game/sector_tile_list.c`, `src/game/sector_object_list.c`, `src/game/obj.c`,
  `src/game/obj.h`, `src/game/obj_private.c`, `src/game/obj_id.h`, `src/game/sa.c`,
  `src/game/sa.h`, `src/game/proto.c`, `src/game/obj_pool.c`, `src/game/script.h`,
  `src/game/quest.h`, and `first_party/tig/src/art.c`.

CE and CE-derived sources remain one lineage and are not independent
confirmation. This independently authored note records constrained behavior;
it does not copy CE implementation structure or license terms.

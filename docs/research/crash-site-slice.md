# Crash Site: frozen initial sector

## Scope

ARC-RES-003 is limited to one reproducible Crash Site research candidate. It
is neither a production map loader nor evidence of gameplay, asset precedence,
scene parity, or rendering behavior.

The selected map is the observed module's only `START_MAP` record, not an
inference from a directory name. The selected sector is the sector containing
that record's start coordinate. No neighbouring sector is included.

## Identity chain

| Link | Observation | Status / confidence |
| --- | --- | --- |
| Community context | The preserved Terra-Arcanum catalogue records a historical Crash Site page and image URL. | reported; context only |
| Map selector | Raw DAT entry `Rules\MapList.mes` is ordinal 14,935, compressed with `0x00000002`, and decodes to 5,454 bytes. Record 5,000 is the observed module's sole `START_MAP`; it names `Arcanum1-024-fixed` and gives `(92958, 82592)`. | experimentally verified locally; high |
| Map properties | Raw entry `maps\Arcanum1-024-fixed\map.prp` is ordinal 8,023; its 24 decoded bytes begin with observed words `2`, `1`, and a little-endian pair `(128000, 128000)`. No semantic labels are assigned. | experimentally verified byte observation; high |
| Sector selector | Pinned CE's `sector_id_from_loc` shifts both location coordinates by six and packs the results. Applied to the start coordinate, that gives `(1452, 1290)` and `86570436012`. The matching sector entry is ordinal 463 and decodes to 47,291 bytes. | entry and size experimentally verified; coordinate interpretation CE-derived; medium |
| Object lead | The final signed little-endian word in the selected decoded bytes is `317`. Pinned CE's `objlist_load` treats that terminal value as the object-list count. | bytes verified; CE-derived interpretation; medium |

CE is a single source lineage, not independent corroboration. Its Sustainable
Use License remains applicable to that source; this document does not change
it or assert a legal clean-room conclusion.

## Frozen input

| Field | Value |
| --- | --- |
| Map name | `Arcanum1-024-fixed` |
| Start location | `(92958, 82592)` |
| Sector coordinate | `(1452, 1290)` |
| Packed sector identifier | `86570436012` |
| Local origin | `(92928, 82560)` |
| Extent | 64 by 64 positions, inclusive ranges `92928..92991` and `82560..82623` |
| Start offset inside sector | `(30, 32)` |
| Sector entry | module DAT ordinal 463, flag `0x00000002`, decoded length 47,291 |
| Terminal object count | 317; not a count of static scenery |

The one-sector extent follows the CE source's sector shift, not a claim about
all formats or maps.

## Bounded selector and safety limits

The read-only probe accepts one explicit module path and requires the observed
module size `355938305` and SHA-256
`8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f`.
It opens the archive through `Arcanum.Data.DatArchive`, raw-name matches exactly
one selected `.sec` entry, limits both stored and decoded payloads to 1 MiB,
and fingerprints the input before and after its in-memory read.

It reads only this small CE-correlated prefix subset:

1. signed little-endian light count, capped at 1,024;
2. `count * 48` bytes of opaque serialized lights, skipped only after checked
   arithmetic and bounds validation;
3. 4,096 four-byte ART identifiers; and
4. the final signed little-endian object count, capped at 4,096.

The selected real input had zero serialized lights. The 4,096 values contain
1,041 distinct raw identifiers and a mix of high-nibble classes, so this probe
does **not** claim that every word is a directly resolvable terrain ART ID.
It records no raw IDs or game payloads in the repository.

## Bounded record and unresolved resource schemas

The selected sector contains a now-measured object window rather than an
undifferentiated future work item. With its observed zero light records, the
tile table ends at decoded offset 16,388, the final count begins at 47,287, and
the 30,899-byte intervening window has 317 records according to the terminal
count. The probe records only those aggregates.

Pinned CE's object-list and object-reader paths state that the count controls a
sequence of versioned object records; each record branches through an object
identifier and a type-dependent property mask before field payloads. Route B
implements the complete fail-closed delimiter for pinned CE version 119,
including every later variable-width field, direct current-AID/selected-location
retention, and non-overridden concrete current-ART prototype resolution. On
the selected retail window, however, the first LE
word at offset 16,388 is observed `1`, not that supported version. The probe
therefore rejects before record 0 rather than guessing another form or fixed
offset. The compact supported contract/caps and CE lineage are in
`docs/formats/maps.md`; this is a precise form mismatch, not a claim that any
record is static scenery.

The tile table exposes two observed high-nibble classes. A terrain chain is
blocked only at their type-specific ART-ID decomposition and resource-path
lookup rule. It is not acceptable to promote a raw word to an ART filename;
once a path is proven, the existing `DatArchive` and `ArtDecoder.Inspect`
provide the prescribed bounded archive and metadata steps.

## Explicitly not established

- an ART/resource interpretation or static-scenery subset: the delimiter reads
  only enough to validate record boundaries and current-ART inheritance;
- the two observed tile-class lookup rules, tile/scenery filename resolution,
  or a selected ART frame for this sector;
- patch, loose-file, or archive conflict precedence;
- object baselines, visual offsets, mirroring, height, occlusion, or measured
  scene parity;
- lighting, walls, roofs, gameplay entities, or a playable scene.

Those gaps prevent declaring either a Godot-independent loader or a static
Godot scene ready. They are research requirements, not defaults a later
implementation may silently guess.

## Sources

- User-owned module archive: local, read-only evidence above.
- Existing project reader: `src/Arcanum.Data/DatArchive.cs`.
- Pinned `arcanum-ce` revision
  `a7ff41b300ef712f0e7d088183a3d08957110cdd`: `src/game/sector.c`,
  `src/game/sector_tile_list.c`, `src/game/sector_object_list.c`,
  `src/game/location.c`, and `first_party/tig/src/art.c`.
- Community catalogue: toolkit metadata record; contextual only.

No original bytes, extracted assets, name inventory, or user installation path
is committed.

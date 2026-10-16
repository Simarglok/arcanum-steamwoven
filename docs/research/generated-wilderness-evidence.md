# Generated wilderness: terrain.tdf and terrain templates (ARC-RES-034)

Status: **research + read-only probe**, work item ARC-RES-034 (whole-world plan,
section R1), 2026-10-16. No `src/`, viewer or session change. This document answers
how the original builds a sector that has no hand-made `.sec` file: how
`terrain.tdf` is stored, how a sector's terrain word selects a template sector in
`arcanum2.dat`, how the template's objects and lights are moved to the real sector,
what happens when no template exists, and what the `/b` terrain types block. It is
the input for the later loader slices listed at the end.

## Vocabulary and boundary

- **Source fact**: read from the pinned CE specification
  [`a7ff41b300ef712f0e7d088183a3d08957110cdd`](https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd)
  (Sustainable Use License v1.0). Line ranges below are what was read. It is one
  lineage, not an observation of the original executable.
- **Local observation**: measured by `tools/research/terrain_template_probe.py` on
  the owner's unchanged installation. The probe prints counts, map and template
  folder names (DAT member paths), terrain type names and SHA-256 digests only,
  never sector payloads, object records or MES colours.
- **Inference**: a reading of observations that the data supports but does not
  prove. Each is marked as such.

The probe was written from the rules stated here and does not translate CE code.
Its v119 record walker uses this project's own wire descriptors
(`src/Arcanum.Data/StaticObjects/StaticObjectLoader.cs`).

## Reproduction and inputs

```text
python3 -B tools/research/terrain_template_probe.py --self-test
python3 -B tools/research/terrain_template_probe.py --game-root <installed game folder> [--map Arcanum1-024-fixed]
```

The real run opens the content stack in the documented mount order
([content precedence](content-precedence.md): module folder, `Arcanum.PATCH0`,
module `.dat`, `data/`, `arcanum4..1.dat`, game folder, `tig.dat`), hashes every
archive before and after (refusing if one changed), and runs in about 8 seconds.
It refuses an installation with archives it does not model (`arcanum5+.dat`,
`Arcanum.patch1..9`). Report digest of the run below: `6740b9b9eaf79c80cb3f03b84b5c71322d3b0f89dd6e65770b3e66baef74a221`
(two runs, identical).

| Input | Bytes | SHA-256 |
| --- | ---: | --- |
| `modules/Arcanum.dat` | 355,938,305 | `8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f` |
| `arcanum2.dat` | 147,991,122 | `4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d` |
| `arcanum1.dat` | 447,470,816 | `6dd9d10ef0932e02fc88f0eb5367b1bc37275db51d60bd68ac9bd7ef2d478576` |
| `arcanum3.dat` | 9,605,011 | `cbadd76bb3df798fa25dcc05871af1ea751c1164fb1426dccc520cd7d9edc499` |
| `Arcanum4.dat` | 447,614 | `975d43854fca8b7dd74e2b12ef2ff3fe6d3c0f99dc541151d33001469f89710c` |
| `modules/Arcanum.PATCH0` | 18,804 | `d9a3313f71d78115dcac489296bf3d8a5801dd55b20267bd6017b7a898364bce` |
| `tig.dat` | 57,149 | `c9b48eb6396c514eba3805fb4847df67db3c67ef9f13a79ea21bbf7dd329a09b` |
| `terrain/terrain.mes` (winner: `arcanum2.dat`) | | `d7d8ef7b7286416f3b1a0bc23f58255672becd58022b122bc545bf3f88bbb77e` |
| `maps/Arcanum1-024-fixed/terrain.tdf` (winner: module `.dat`) | 1,771,118 | `c7b65274883513e4d5363865a6cdf31ab58b1a5b6a5c24b94ea89126fd67656b` |

## 1. Where a sector comes from

For every sector the game loads (source: [`sector_load_game`, `sector.c` 1453–1491](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L1453-L1491) for the choice,
[1501–1530](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L1501-L1530) and [1694–1702](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L1694-L1702) for the `.dif` and the real id passed to the light and object loaders):

1. `maps/<map>/<sector id>.sec` through the content stack, where
   `sector id = x | y << 26` in decimal. If it exists, it is used as is.
2. Otherwise the **template path** computed from the map's terrain word for that
   sector (section 3). The template is read like any sector file, but the real
   sector id is passed to the light and object loaders so they move their records
   (section 5). A `.dif` for the generated sector is still looked up under the real
   id in the save folder.
3. Otherwise **fill** (section 6): all 4,096 tiles from one tile family with a
   random variation per tile, no lights, roofs, scripts, blocks or objects, and no
   `.dif` lookup.

A demo-limits branch skips step 1 for sectors outside configured bounds. The limits
come from a special `134217730001.sec` in the map folder
([`sector_load_demo_limits`, `sector.c` 2158+](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L2158)); the main
map has no such file (report: `own_files_outside_grid` is 0), so it does not apply.

`sector_exists` (the "hand-made?" test used elsewhere, [`sector.c` 836–841](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L836-L841)) checks step 1 only.

Observed on the main map `Arcanum1-024-fixed` (2000 × 2000 = 4,000,000 sectors):

| Resolution | Sectors |
| --- | ---: |
| Own sector file | 621 |
| Plain template | 3,954,569 |
| Transition template | 44,810 |
| Fill (no template) | **0** |

Every generated sector finds an existing template, all of them in `arcanum2.dat`;
no higher source shadows a template. 208 distinct template files are used by the
main map, 212 by all maps together, of 413 template `.sec` files present.

## 2. terrain.tdf

Source: header [`terrain.c` 14–21](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/terrain.c#L14-L21),
[`terrain_open` 198–271](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/terrain.c#L198-L271),
[row read 469–499](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/terrain.c#L469-L499),
[row cache 886–946](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/terrain.c#L886-L946),
[editor decompress 949–987](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/terrain.c#L949-L987).

| Offset | Field | Rule |
| ---: | --- | --- |
| 0 | `float32 version` | must equal `1.2` exactly, else the map fails to open |
| 4 | `u32 flags` | bit 0 = rows compressed; no other bit observed |
| 8 | `i64 width` | sectors per row |
| 16 | `i64 height` | rows |
| 24 | `i32 base_terrain_type` | default type for a new map; not used when reading |
| 28 | `i32 padding` | 0 |
| 32 | body | flat: `width × height` LE `u16` words, row-major (`x + y × width`). Compressed: per row a LE `u32` size, then one zlib stream that inflates to exactly `2 × width` bytes |

Reading in the game (not the editor) keeps the compressed body in memory and
inflates rows on demand into a four-row round-robin cache; a missing row is found
by walking the size prefixes from row 0. A sector outside `width × height` reads
as `0xFFFF`. Any `terrain_open` failure (missing file, wrong version, short read)
makes `map_open` try `terrain_map_new` from `map.prp`
([`map.c` 701–713](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L701-L713)), but that file is written only in the
editor ([`terrain_flush` 434–466](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/terrain.c#L434-L466)); in the game the reopen
fails and the map does not open. A loader should therefore refuse a missing or bad
grid rather than synthesise one.

Observed (report `map_tdf_layout`): all 81 maps of the module carry a `terrain.tdf`
(all from the module `.dat`), all version 1.2; 9 are row-compressed (including the main map), 72 flat.
The main map's 2,000 rows each inflate to exactly 4,000 bytes and the last row ends
at the last byte. Header base type 2, padding 0. The probe refuses a wrong version,
unknown flag bit, a row that inflates to another length, trailing bytes, or a flat
body of the wrong size.

## 3. Terrain word and template path

### Fields of the 16-bit word

Source: [accessors `terrain.c` 605–626](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/terrain.c#L605-L626).

| Bits | Field |
| --- | --- |
| 11–15 | base type (`tid >> 11 & 31`) |
| 6–10 | second type (`tid >> 6 & 31`) |
| 2–5 | transition code (`tid >> 2 & 15`) |
| 0–1 | variant (`tid & 3`) |

Observed consistency on the main map: base = second exactly when the code is 0
(3,954,883 sectors both ways; zero mixed cases); code 15 never occurs. The
editor-side encoder puts the second type at bit 4, not 6 (anomaly A2); the data
agree with the readers, not the encoder.

### Terrain types (`terrain/terrain.mes`)

Source: [`terrain_init_types` 658–683](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/terrain.c#L658-L683),
[variant count 686–725](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/terrain.c#L686-L725),
[transitions 728–807](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/terrain.c#L728-L807).
Messages 0.. (consecutive, at most 32) name the types. A `/b` in the text marks the
type blocked and is cut off together with the whitespace before it; the remaining
text is the folder name. Messages 100+ are editor colours. Messages 200 onward
must read `<name> to <name>`, else terrain initialisation fails; they feed only an
editor helper table. The original matches each name against every message of the
file; the probe is stricter and accepts only type names. The runtime counts how
many plain variants exist per type and never reads that count back, but the same
loop fails initialisation if type 0 has no `0.sec` and fixes the type count that
A6 depends on (terrain.c 713–722).

| Type | Folder name | `/b` | Plain variants present | Main-map base count |
| ---: | --- | :---: | :---: | ---: |
| 0 | green grasslands | | 4 | 406,569 |
| 1 | swamps | | 4 | 11,889 |
| 2 | water | yes | 4 | 2,264,557 |
| 3 | plains | | 4 | 180,818 |
| 4 | forest | | 4 | 672,344 |
| 5 | desert island | | 4 | 106,290 |
| 6 | void plains | | 0 | 0 |
| 7 | broad leaf forest | | 4 | 0 |
| 8 | desert | | 4 | 0 |
| 9 | mountains | yes | 4 | 153,077 |
| 10 | elven forest | | 4 | 107,243 |
| 11 | deforested | | 4 | 29,843 |
| 12 | snowy plains | | 4 | 10,483 |
| 13 | tropical jungle | | 4 | 56,887 |
| 14 | void mountains | yes | 0 | 0 |
| 15 | scorched earth | | 0 | 0 |
| 16 | desert mountains | yes | 4 | 0 |
| 17 | snowy mountains | yes | 4 | 0 |
| 18 | tropical mountains | yes | 4 | 0 |

19 types, 18 transition messages. The main map uses 11 types; 57% of it is water.

### Path rule

Source: [`terrain_sector_path` 367–418](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/terrain.c#L367-L418),
[slot table 50–67](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/terrain.c#L50-L67),
[variant slot 650–655](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/terrain.c#L650-L655).
A template folder is a small map of its own; its file names are sector ids
`x | y << 26` of a **slot** inside that folder.

- **Plain** (base = second, or code 0): `terrain\<base>\<slot>.sec` with slot
  `(variant % 2, variant / 2)`, i.e. files `0`, `1`, `67108864`, `67108865` for
  variants 0..3.
- **Transition** (base ≠ second and code ≠ 0): `terrain\<from> to <to>\<slot>.sec`,
  slot from the table below. For codes with a slot, from = base and to = second.
  For the other codes the pair is swapped (from = second, to = base) and the slot
  of code `15 − c` is used. The variant is ignored.

| Code | Bits (3..0) | Folder | Slot (x, y) | File id | Main map |
| ---: | --- | --- | --- | ---: | ---: |
| 1 | 0001 | base to second | (1, 0) | 1 | 2,330 |
| 2 | 0010 | base to second | (1, 2) | 134217729 | 2,172 |
| 3 | 0011 | base to second | (1, 1) | 67108865 | 5,939 |
| 4 | 0100 | base to second | (3, 2) | 134217731 | 2,321 |
| 5 | 0101 | base to second | (0, 0) | 0 | 13 |
| 6 | 0110 | base to second | (2, 2) | 134217730 | 4,351 |
| 7 | 0111 | second to base | (3, 0) as code 8 | 3 | 2,812 |
| 8 | 1000 | base to second | (3, 0) | 3 | 2,603 |
| 9 | 1001 | base to second | (2, 0) | 2 | 6,614 |
| 10 | 1010 | second to base | (0, 0) as code 5 | 0 | 16 |
| 11 | 1011 | second to base | (3, 2) as code 4 | 134217731 | 3,250 |
| 12 | 1100 | base to second | (3, 1) | 67108867 | 6,212 |
| 13 | 1101 | second to base | (1, 2) as code 2 | 134217729 | 3,243 |
| 14 | 1110 | second to base | (1, 0) as code 1 | 1 | 3,241 |
| 15 | 1111 | none | none | `-1` (anomaly A3) | 0 |

Counts include the 307 own-file sectors that also carry a transition word. Folder
names compare case-insensitively (`Desert Island to Water` is found as
`desert island to water`). Slots (0, 1), (2, 1) and (0, 2) of a transition folder
are never referenced; 87 such files exist and are never loaded. Slot use on all
maps: slots (0,0), (1,0), (0,1), (1,1) about 1.0 M each (plain variants; (0,0),
(1,0) and (1,1) also take some transitions, (0,1) is plain-only), the
transition-only slots 4,323..6,581 each.

### Transition direction (inference)

Reading the four bits as sector corners fits the one- and two-bit codes: bit 0 the
(−x, −y) corner, bit 1 (−x, +y), bit 2 (+x, +y), bit 3 (+x, −y), a set bit meaning
the second type reaches that corner (sector x grows with column, y with row of the
grid). The probe measures, for each code, how often each of the eight neighbours is
a pure sector of the second type:

| Codes | Pure second-type neighbours (share) |
| --- | --- |
| 0001 | (−1,−1) 0.70, (0,−1) 0.73, (−1,0) 0.73 |
| 0010 | (−1,0) 0.76, (−1,+1) 0.72, (0,+1) 0.76 |
| 0100 | (+1,0) 0.73, (+1,+1) 0.69, (0,+1) 0.73 |
| 1000 | (0,−1) 0.81, (+1,−1) 0.78, (+1,0) 0.81 |
| 0011 / 0110 / 1100 / 1001 | the −x / +y / +x / −y side (0.52..0.74) |
| 0111 / 1011 / 1101 / 1110 | only the diagonal opposite the clear corner (0.66..0.76) |
| 0101 / 1010 | saddles, 13 and 16 sectors |

The three-bit codes do not show pure neighbours on the two sides next to the clear
corner, so the corner reading is not a full geometric model. The loader does not
need it: the slot table alone decides the file.

## 4. Template content (all 413 files, 212 used)

Framed with the sector order of [crash-site-framing.md](crash-site-framing.md) and
the v119 record walker; every file frames completely and every object window ends
exactly at its terminal count.

| Measure | All 413 | Used 212 |
| --- | ---: | ---: |
| Objects | 53,815 | 29,561 |
| — type 0 (wall) / type 3 (scenery) | 4,676 / 49,139 | 4,388 / 25,173 |
| — other types | 0 | 0 |
| — own object ID kind 0 (null) | all | all |
| — location inside the file's own slot | all | all |
| Files without objects | 248 | 78 |
| Lights (in 8 files; per folder: elven forest 79, tropical jungle 49) | 128 | 128 |
| — location inside the file's own slot / owner handle set | 128 / 0 | 128 / 0 |
| Roof arrays (elven forest to forest 9, elven forest 4, tropical jungle 4) | 17 | 16 |
| Tile scripts | 0 | 0 |
| Sector script attached (script 30285, `30285sectorplantgen.scr`, in the plain forest, deforested, elven forest, plains and tropical jungle variants) | 20 | 20 |
| Sound record set | 217 | 184 |
| Block-mask tiles set | 1,225 | 1,225 |
| Townmap / aptitude / light scheme set | 0 | 0 |
| Placeholder `0xAA0000` / `0xAA0003` / `0xAA0004` | 157 / 14 / 242 | 7 / 13 / 192 |

So the template objects are stored at the template's own coordinates (a variant-3
plain file at slot (1, 1) holds objects at x, y 64..127), carry no permanent object
ID (the original registers a permanent ID only for a non-null kind,
[`obj.c` 3080–3082](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj.c#L3080-L3082)),
and are walls and scenery only. One template can therefore be instantiated at many
real sectors without identity collisions.

The `terrain.tdf` inside each template folder is editor scratch and plays no part
in selection (report `template_folder_tdf`): 15 are version 1.2 2 × 2, 30 are 1.2
4 × 3, all with every code 0, and one is version 1.1 with an empty body.

Per-folder counts in the table come from the report's `per_folder` and
`sector_script_files` entries.

## 5. Moving template records to the real sector

Source: [`objlist_load`, `sector_object_list.c` 115–186](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_object_list.c#L115-L186),
[`sector_light_list_load`, `sector_light_list.c` 111–154](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_light_list.c#L111-L154).

- **Objects:** new location = `(real x << 6) + (old x & 63), (real y << 6) + (old y & 63)`;
  the object's modified flags are cleared and it gets the next temporary id of the
  sector. Offsets and every other field are unchanged.
- **Lights:** CE's loader, marked "TODO: Check" at the pin, adds the *unshifted*
  sector x/y to the tile index `x & 63 | (y & 63) << 6` (anomaly A1). Taken
  literally it would put all 128 template lights far from their sector (for sector
  (1451, 1302): x ≈ 1451..5546, y = 1302). Since the lights are stored in their own
  slot exactly like the objects, the object rule is the intended reading
  (inference). Proposed project policy: relocate lights with the object rule.
- **Not moved, sector-local by construction:** the 4,096 tile words, roof array,
  block mask (tile bits), tile scripts (keyed by local tile; none in templates),
  sector script, sound and light-scheme words.
- After loading, objects and lights are folded under the real sector id like a
  hand-made sector ([`sector.c` 1717–1719](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L1717-L1719)).

## 6. Fill fallback

Source: [`terrain_fill` 421–431](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/terrain.c#L421-L431).
When the template path does not exist, every tile gets a tile ART of one family
(tile numbers 7/7, edge 15) whose variation the tile helper picks at random per
tile ([`tile.c` 289–296](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tile.c#L289-L296)); the sector has no lights, roofs,
scripts, blocks or objects and no `.dif` is applied. A deterministic loader needs a
policy for that variation. Retail data never reaches it (0 sectors), but a module
or mod that names a missing type or slot would.

## 7. What `/b` blocks

Source: [`terrain_is_blocked` 629–647](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/terrain.c#L629-L647),
[world-map path `path.c` 781–842, 953](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/path.c#L781-L842),
encounter terrain [`wmap_rnd.c` 163–183](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/wmap_rnd.c#L163-L183) and [708–726](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/wmap_rnd.c#L708-L726).

A sector is terrain-blocked when its word is `0xFFFF` (outside the map) or its base
or second type carries `/b`. The only caller is world-map travel routing
(path.c 801, 838, 953), which treats a blocked sector as impassable **only if it has
no own sector file**. Random encounters ignore `/b`: they read the terrain word and
use their own per-type table, which marks water, all five mountain types and
scorched earth (no `/b`) invalid. Walking on the local tile grid never consults it; there the template's
own tiles, block mask and objects decide. Main map: 2,430,691 generated sectors are
world-map blocked (water and mountains).

## 8. Other maps

56 of the 81 maps contain generated sectors: interiors are padded with plain
templates around their hand-made sectors, for example `Crash Site Cave` (64 × 64,
1 own file, 4,095 generated water sectors) and `Caladon High Security Prison`
(64 × 64, 4 own files). Their generated sectors by base type (report
`other_maps_generated_base_types`): water 23,481, broad leaf forest 4,094, forest
548, mountains 120; all resolve, none fill.

This answers the open question U1 of the [data census](data-census.md)
(ARC-RES-036): absent sectors inside a larger interior grid are generated from
templates, not void. The census's independent counts (81 TDF files, 9
row-compressed; 413 templates with 53,815 walls and scenery) agree with this probe.

## 9. Template folders versus terrain.mes

46 folders, 413 sector files. Folders named by `terrain.mes` (a type, or either
direction of a transition message) but absent: the three
void/scorched types and their transitions (`void plains`, `void mountains`,
`scorched earth`, `void plains to water` and five more), and
`desert mountains to desert`. The folder that exists instead is spelled
`desert moutains to desert` and can never be found (anomaly A4).
`tropical jungle to tropical mountains` holds slots (0,0), (0,1), (2,1) and
(0,2): only the saddle slot (0,0) of the nine referenced slots; the other eight are
missing (anomaly A5). No retail sector needs any of these.

## Anomalies

| Id | Where | Observation | Consequence |
| --- | --- | --- | --- |
| A1 | CE `sector_light_list_load` | Adds unshifted sector x/y to a local tile index | Literal reading misplaces all 128 template lights; use the object rule (policy above) |
| A2 | CE `terrain.c` 587–596 (editor encoder) | Second type packed at bit 4, readers use bit 6 | Editor-only; data follow the readers |
| A3 | CE path rule | Code 15 with base ≠ second has no slot either way and builds `-1.sec` | Never in data; would fall to fill |
| A4 | `arcanum2.dat` | `desert moutains to desert` misspelled | Folder unreachable; unused by retail maps |
| A5 | `arcanum2.dat` | `tropical jungle to tropical mountains` has only the referenced slot (0,0) of nine | Unused by retail maps; would fall to fill |
| A6 | CE path rule | A base or second type ≥ type count has no name (CE prints an error only for an invalid base; either case continues with a null name, undefined behaviour) | Never in data; a loader should refuse or fill, not crash |

## Proposed slices (for the whole-world plan)

1. **Data: terrain grid reader** — `terrain.tdf` header, flat and row-zlib bodies,
   typed refusals, a row-offset index instead of a linear walk.
2. **Data: template resolver** — type table from `terrain.mes` (with `/b`), the
   path rule above against the content stack, explicit `Own`/`Plain`/`Transition`/
   `Fill`/`Refused` results.
3. **Data: generated sector** — frame the template with the existing sector reader,
   apply the object rule to objects and lights, keep tiles/roofs/blocks/scripts
   local; fill as a typed result.
4. **Viewer/session**: stream generated sectors next to hand-made ones (needs the
   world-plan streaming slices).

## Limits and NOT READY

- No loader, renderer or session code; nothing in the viewer changes.
- The original executable was not run or disassembled. A1's correct rule is an
  inference from CE and the data; the fill's random tile variation is from CE only.
- The v119 record walker met only object types 0 and 3 on real data; other types
  are covered by the synthetic self-test only.
- The probe models the mounts of the measured installation (`patch0`,
  `arcanum1..4`, folders, `tig.dat`) and refuses others; an ambiguous loose name is
  treated as absent.
- Script 30285 (sector plant generator) in 20 plain templates may change vegetation
  at run time; what it does is not researched here.
- `.dif` files for generated sectors (save state) are not examined.
- Only the owner's GOG + CE installation and its one module were measured;
  `Vormantown` and other modules are out of scope.

## Sources and lineage

- Pinned CE `a7ff41b300ef712f0e7d088183a3d08957110cdd`: `src/game/terrain.c`,
  `src/game/sector.c`, `src/game/sector_object_list.c`,
  `src/game/sector_light_list.c`, `src/game/light.c`, `src/game/obj.c`,
  `src/game/tile.c`, `src/game/path.c`, `src/game/map.c`, `src/ui/wmap_rnd.c`.
- Project: `tools/research/terrain_template_probe.py`,
  `tools/research/dat_probe.py`, the v119 descriptors in
  `src/Arcanum.Data/StaticObjects/StaticObjectLoader.cs`, and
  [content-precedence.md](content-precedence.md), [crash-site-framing.md](crash-site-framing.md).

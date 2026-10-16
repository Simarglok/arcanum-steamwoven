# Universal map catalogue

## Scope

`Arcanum.Data.Maps` (work item ARC-DATA-015) is the first piece of the
owner-chosen universal loader: it reads any map of any module through the
module's [content stack](../research/content-precedence.md), so a patch, a loose
override, a modification or another module is read exactly like the original.
It replaces nothing yet. The pinned Crash Site and cave profiles
([area loader](crash-site-area-loader.md)) stay as they are and remain the
test oracles; no viewer, session or traversal code consumes this catalogue yet.

The formats and counts it relies on are measured in the
[data census](../research/data-census.md) (ARC-RES-036). It is Godot-free and
read-only, and it never prints or returns MES/DLG text.

## API

| Call | Reads | Result |
| --- | --- | --- |
| `WorldCatalog.Open(stack, options)` | every `maps/<name>/` folder with a `map.prp`; `terrain.tdf` header, `startloc.txt`, `mapinfo.txt`, `map.jmp`; the file list; `proto/NNNNNN - *.pro` names | `Maps` in name order, catalogue `Issues` |
| `ReadSector(map, coordinate)` | `maps/<name>/<id>.sec` | all five sector versions: lights, 4,096 tile words, roof words, tile scripts, sector script, metadata/sound and block mask as raw words, and v119 objects of types 0..17 |
| `ReadMobiles(map)` | the game-generated aggregate `maps/<obfuscated name>` when present, else the editor `.mob` files | objects plus the source used |
| `ReadTerrain(map)` | the whole `terrain.tdf` | one 16-bit word per sector, raw or one zlib row per grid row |
| `ReadPrototype(number)` | `proto/NNNNNN - *.pro` | the prototype record |

`WorldSectorCoordinate` packs a sector as `X | Y << 26`;
`WorldLocation.FromPacked` unpacks a tile location as `X | Y << 32`;
`WorldCatalog.AggregateName` is the original's mobile-file name rule (rotate
letters by 13 with its one-short wrap, then reverse). `ContentStack.Keys(prefix)`
is the new enumeration the catalogue is built on.

## Refusal unit

A failure refuses the smallest unit that can be skipped, never the catalogue:

- A map folder without a readable 24-byte `map.prp` is left out and listed in
  `WorldCatalog.Issues`.
- A malformed small file (`terrain.tdf` header, `startloc.txt`, `mapinfo.txt`,
  `map.jmp`) leaves its value null or empty and adds a map issue. Unknown files
  in a map folder are listed as `UnrecognisedFile`.
- A sector that does not frame is `Refused` with its reason; other sectors read.
- Object records are sequential, so a refused record ends the readable part of
  its window: the objects before it are kept and `ObjectIssue` names the index.
  The same holds for the aggregate. An editor `.mob` file is refused alone.

Limits are explicit in `WorldLoaderOptions` (sector, wire, aggregate, terrain,
jump-point and small-file bounds) and default above the largest observed values.

## Choices

- **Mobiles.** In a normal game the original reads only the aggregate (pinned CE
  `map_load_mobile`); the editor reads the `.mob` files. The catalogue prefers the
  aggregate and falls back to the editor files, reporting `WorldMobileSource`,
  because a module may ship a map without an aggregate (the `Vormantown` shop
  map does).
- **Start location.** `startloc.txt` is the map's own start; a new game starts
  where the map catalogue says, which is separate (ARC-RES-035).
- **Raw sections.** Lights, roofs, tile and sector scripts and metadata stay raw
  words: their meaning belongs to the components that render or run them.

## Verification

- Synthetic tests (`tests/Arcanum.Data.Tests/Maps/WorldCatalogTests.cs`):
  loose-file installations covering every refusal path, terrain decoding,
  aggregate and editor mobiles, prototype lookup and stack shadowing.
- Opt-in `RealData` tests with `DAT_ROOT`: all 81 maps of `Arcanum` (1,680
  sectors, 263,902 objects, 12,723 aggregate mobiles, 1,423 prototypes, 45 jump
  points, the 2,000 × 2,000 grid) read without refusals in about one second;
  the `Vormantown` module reads too, with one map on editor mobiles.

## Not yet

Generated sectors from the terrain grid (ARC-RES-034), map transitions and the
map catalogue numbers (ARC-RES-035), ART trailing-byte policy, the content
stack fingerprint, sector streaming and every consumer (viewer, traversal,
session) are later steps of the [whole-world plan](../research/data-census.md#proposal-for-arc-data-015-p).

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
| `ReadPrototype(number)` | `proto/NNNNNN - *.pro` from the base sources ([prototypes](#prototypes)) | the prototype record |
| `Prototypes` | the same files | the shared `ContentPrototypes` index: numbers, the source each is read from, `Read(number)` as a `PrototypeFile` |

`WorldSectorCoordinate` packs a sector as `X | Y << 26`;
`WorldLocation.FromPacked` unpacks a tile location as `X | Y << 32`;
`WorldCatalog.AggregateName` is the original's mobile-file name rule (rotate
letters by 13 with its one-short wrap, then reverse). `ContentStack.Keys(prefix)`
is the new enumeration the catalogue is built on.

## Refusal unit

A failure refuses the smallest unit that can be skipped, never the catalogue:

- A map folder without a readable 24-byte `map.prp` is left out and listed in
  `WorldCatalog.Issues` with the reason (missing, too large, malformed).
- A `proto/*.pro` file whose name is not `NNNNNN - label.pro` is listed as
  `UnrecognisedFile` (the original loads it anyway, so it must not vanish
  silently); two files with one number are each listed as `Malformed`, and that
  number reads from neither (`ReadPrototype` gives a `Malformed` issue). A copy
  in the module archive, a module patch or the module folder is listed as
  `IgnoredModuleCopy` ([prototypes](#prototypes)).
- A malformed small file (`terrain.tdf` header, `startloc.txt`, `mapinfo.txt`,
  `map.jmp`) leaves its value null or empty and adds a map issue. Unknown files
  in a map folder, including any in a subfolder, are listed as `UnrecognisedFile`.
- A sector that does not frame is `Refused` with its reason; other sectors read.
- Object records are sequential, so a refused record ends the readable part of
  its window: the objects before it are kept and `ObjectIssue` names the index.
  The same holds for the aggregate. An editor `.mob` file is refused alone.

Limits are explicit in `WorldLoaderOptions` (sector, wire, aggregate, terrain,
jump-point and small-file bounds) and default above the largest observed values;
options that would let a derived size leave an array or Int32 offset are refused.

## Prototypes

ARC-DATA-026. `Arcanum.Data.Content.ContentPrototypes` is the one prototype
source of the universal readers: this catalogue (and the
[map transitions](map-transitions.md) read through it), [whole-map plans](whole-map-plans.md),
the [pre-made hero](starting-hero-runtime.md) and the
[combat fields](../formats/combat-fields.md) (`ContentPrototypes.Read` is the
`Func<int, PrototypeFile>` lookup those readers take). The pinned Crash Site
exterior and cave profiles (`AreaOriginalValidator`, `StaticObjectLoader`, the
viewer's `--prototype` folder) still read one loose folder; moving them is a
later task. The original lists and
reads every `proto\*.pro` once while the game initialises, before it mounts the
module, and keeps them for the process: the map subsystem's Proto entry has an
init function and no module-load function (checked in the 1.0.7.4 executable,
[ledger](../research/original-verification-ledger.md#prototype-phase-arc-data-026)
row "Prototype phase"). So a prototype is read from the base sources only —
`data/`, `arcanum*.dat`, the game folder and `tig.dat`, the first of them in
search order — and a module's copy is never read, also not when no base copy
exists. A file larger than 64 KiB is `TooLarge`. `PremadeHeroReader.DirectoryPrototypes`
(one loose folder) remains for synthetic fixtures only. On the installed
original all 1,423 prototypes are loose in `data/proto` and the module holds
none, so every read is unchanged (opt-in `ContentPrototypesRealDataTests`
compares every file, every combat-field read and all 13 PC prototypes with the
one-folder lookup).

## Leniency matches the original

Where the original reader is lenient, the catalogue is too, so a file the game
accepts is not refused here (pinned CE `map.c`, `terrain.c`):

- `startloc.txt`: the first two lines, blank ones included, read like `strtoll`
  (leading digits, else 0); only a missing line is an issue.
- `mapinfo.txt`: each `LightScheme` / `SoundScheme` line is applied as read, so
  a later line wins; signed values are accepted; other lines are counted.
- `terrain.tdf`: header flag bits other than row compression are ignored; a
  compressed row must inflate to at least one row, and anything after that row
  is not examined.

Opening the catalogue reads each `terrain.tdf` in full to get its 32-byte
header (1.8 MB for the main map); this costs time, not correctness.

## Choices

- **Mobiles.** In a normal game the original reads only the aggregate (pinned CE
  `map_load_mobile`); the editor reads the `.mob` files. The catalogue prefers the
  aggregate and falls back to the editor files, reporting `WorldMobileSource`,
  because a module may ship a map without an aggregate (the `Vormantown` shop
  map does).
- **Start location.** `startloc.txt` is read as the original reads it, but every
  installed map holds the same pair (477, 178), which is not a tile of most of
  them; arrival tiles come from `rules/maplist.mes` instead
  (`AreaMapPlan.StartTile`, ARC-WORLD-018; transitions are ARC-RES-035).
- **Raw sections.** Lights, roofs, tile and sector scripts and metadata stay raw
  words: their meaning belongs to the components that render or run them.

## Verification

- Synthetic tests (`tests/Arcanum.Data.Tests/Maps/WorldCatalogTests.cs`):
  loose-file installations covering missing and malformed map files, refused
  sectors and object records, trailing object bytes, the map and option limits,
  prototype names, the lenient small-file rules, terrain decoding, aggregate and
  editor mobiles, prototype lookup and stack shadowing; base-source order,
  ignored module copies, duplicates, names and sizes of prototypes
  (`tests/Arcanum.Data.Tests/Content/ContentPrototypesTests.cs`).
- Opt-in `RealData` tests with `DAT_ROOT`: all 81 maps of `Arcanum` (1,680
  sectors, 263,902 objects, 12,723 aggregate mobiles, 1,423 prototypes, 45 jump
  points, the 2,000 × 2,000 grid) read without refusals in about one second;
  the `Vormantown` module reads too, with one map on editor mobiles.

## Not yet

Generated sectors from the terrain grid (ARC-RES-034), map transitions and the
map catalogue numbers (ARC-RES-035), ART trailing-byte policy, the content
stack fingerprint, sector streaming and every consumer (viewer, traversal,
session) are later steps of the [whole-world plan](../research/data-census.md#proposal-for-arc-data-015-p).

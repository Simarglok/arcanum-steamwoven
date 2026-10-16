# Census of original data for a universal loader

## Status, scope and evidence discipline

Status: **Research**, work item ARC-RES-036 (2026-10-16). The owner chose one
universal loader for every original data type over the module content stack,
instead of per-location pinned profiles, so that modifications and other
modules that reuse the same resources load too. This note answers the first
question of that plan: what the installed data contains, and which of it the
readers already in this repository can read. It adds one read-only probe mode
and this note. It changes no production API, viewer, session or launcher file.

Categories:

- **Data (D):** the installed original read through the content stack of
  [ARC-DATA-011](content-precedence.md) (game folder, `tig.dat`,
  `arcanum1..4.dat`, `data/`, module `.dat`, `.patch0..9`, module folder),
  winner per virtual path only.
- **Source (S):** `alexbatalov/arcanum-ce` at
  [`a7ff41b300ef712f0e7d088183a3d08957110cdd`](https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd)
  (Sustainable Use License 1.0, not MIT), read for intent; no code or structure
  is copied.
- **Proposal (P):** a project recommendation for ARC-DATA-015, not yet accepted.
- **Unknown (U):** a named gap with its next check.

The report holds counts, byte totals, type numbers and refusal reasons. It never
prints payload bytes, MES/DLG text or object names. Original executable parity
remains **NOT_VERIFIED**.

## How to run

```bash
dotnet run --project tools/research/Arcanum.CrashSiteProbe -- --data-census --game-root <game folder> [--module <name>]
dotnet run --project tools/research/Arcanum.CrashSiteProbe -- --data-census-self-test
```

The report is deterministic JSON (fixed field order, tallies sorted by key, maps in name order). A member the stack cannot read becomes a refusal bucket for its role; the run does not stop. On the owner's
installation two runs of the `Arcanum` module gave the same SHA-256
`eae72084a51df92d2838634da2e042a6b02e3730324d397a838e49035cfd3134` in about
7 s. The stack does not expose its index, so the probe gathers names from every
mounted source and resolves each again; only names the stack indexed survive.

Readers used: the production `ArtDecoder.Inspect`, `MesFile.Parse` and
`DlgReader.Read`; the probe's own sector framing and v119 object reader (the
same layout as `TerrainLoader` framing and `ObjectWireReader`, which are
internal); a framing check for SCR (64-byte header, 132-byte lines); and a new
TDF check (header version 1.2, raw words or length-prefixed zlib rows).

## Answer

- **Every map, sector, object and mobile in both modules reads (D).** In the
  `Arcanum` module all 1,680 sector files of 81 maps frame, all 263,902 sector
  objects parse, all 12,723 `.mob` files parse, all 81 game-generated mobile
  aggregates frame and each belongs to exactly one map by the name selector of
  [cave research](cave-transition-evidence.md). The second module `Vormantown`
  (3 maps, 157 sectors, 25,113 objects, 5,806 mobiles) reads the same way. The
  object layout is v119 with types 0..17 everywhere; no unknown version or type
  occurs.
- **The world is mostly generated, and its templates read too (D).** All 413
  terrain template sectors (53,815 walls and scenery) frame and parse. The main
  map's `terrain.tdf` describes 2,000 × 2,000 sectors; only 621 have files.
  Several interiors also have a larger TDF grid than files (for example 625
  grid sectors and 77 files); that absent interior sectors are generated too
  is inferred from the grid size, not measured (U1).
  The generation rule is [ARC-RES-034](#unknowns-u).
- **Refusals are few and confined to presentation data (D).** 437 of 13,993 ART
  files (3.1%), 38 of 709 DLG files and 1 of 239 MES files are refused by the
  current strict readers. No prototype, sector, mobile, aggregate or TDF file
  is refused, and every SCR file frames (header and line size only).
- **A universal loader is feasible without per-map pins (P).** Format-level
  checks with explicit limits already accept the whole installation. Per-file
  hashes add nothing a structural check does not, and they would refuse a mod.

## What the installation holds

Winners per virtual path in the `Arcanum` module: 97,629 paths. The game folder
is itself a mounted source, so files under it also appear under their
`data/`-relative names (for example 1,423 prototypes as both `proto/` and
`data/proto/`); the table counts the stack's own paths.

| Data | Files | Bytes | Reader in repo | Result |
| --- | ---: | ---: | --- | --- |
| Map sectors `maps/<map>/*.sec` | 1,680 | 53.0 MB | framing + v119 objects | all read |
| Editor mobiles `maps/<map>/*.mob` | 12,723 | 2.9 MB | v119 object | all read |
| Mobile aggregates `maps/<selector>` | 81 | 2.9 MB | aggregate framing | all read, 1:1 with maps |
| `terrain.tdf` per map | 81 | 1.8 MB | new TDF check | all read (9 row-compressed) |
| `map.prp`, `map.jmp` | 81 each | small | size checks | all framed; 45 jump points of 32 bytes on 17 maps |
| `mapinfo.txt`, `startloc.txt`, one `.sbf`, one `.log` | 81 / 81 / 1 / 1 | small | none | counted only |
| Terrain templates `terrain/<type>/*.sec` | 413 | 12.0 MB | framing + v119 objects | all read |
| Prototypes `proto/*.pro` | 1,423 | 0.9 MB | v119 prototype | all read |
| All `.art` (mostly under `art/`) | 13,993 | 720 MB | `ArtDecoder.Inspect` | 13,556 read, 437 refused |
| All `.mes` (mostly `mes/`, `rules/`, `oemes/`, `semes/`, `art/`, `sound/`, module template) | 239 | 3.0 MB | `MesFile.Parse` | 238 read, 1 duplicate number |
| All `.dlg` | 709 | 5.9 MB | `DlgReader.Read` | 671 read, 38 refused |
| All `.scr` (2,399 under `scr/`) | 2,402 | 3.3 MB | SCR framing | all framed, 23,878 lines |
| Townmap tiles `townmap/**.bmp`, `.tmi` | 57,522 / 114 | 180 MB | none | not needed for the world |
| World map `worldmap/*.bmp` | 65 | 4.2 MB | none | world-map stage only |
| Sound `.mp3` / `.wav` | 2,704 / 706 | 325 MB | audio lab (ARC-DATA-010) | out of scope here |
| Movies `.bik`, slides, portraits | 18 / 42 / 126 | 351 MB | none | out of scope here |

## Maps and sectors

| Measure | `Arcanum` module |
| --- | ---: |
| Maps / with sector files | 81 / 81 |
| Sectors, main map / interiors | 621 / 1,059 |
| Sector format discriminator 0 / 1 / 2 / 3 / 4 | 18 / 2 / 6 / 169 / 1,485 |
| Sectors with roofs / with non-tile ART in the tile table | 184 / 329 |
| Serialized lights (largest in one sector) | 2,797 (47) |
| Tile scripts | 3,012 |
| Objects (largest in one sector) | 263,902 (1,915) |
| Objects by type: wall 0 / portal 1 / scenery 3 / trap 17 | 89,013 / 2,107 / 170,719 / 2,063 |
| Maps with roofs / lights / jump points | 15 / 72 / 17 |
| Map sizes (`map.prp`, tiles per side) | 64 … 4,096; main map 128,000 |

All five sector format versions occur, so a loader that reads only the newest
one (block mask present) would refuse 195 sectors. The existing framing in
`TerrainLoader` and in the probe already handles all five.

Sector objects hold only walls, portals, scenery and traps. Everything else
(containers 915, NPCs 3,792, weapons, armour, gold, food, keys, written items,
generic items) comes from mobiles, and the type counts of the 12,723 editor
`.mob` files equal those of the 81 aggregates exactly. The aggregate is the
normal-game form the original writes; the editor files are the shipped form.
In `Vormantown` the shop map has editor mobiles but no aggregate, so a loader
cannot assume an aggregate exists (P: read the aggregate when present, else the
editor files; [ARC-DATA-009](../world/crash-site-area-loader.md) already
reconciled the two for the main map).

## Refusals

| Reader | Refused | Where | Reason |
| --- | ---: | --- | --- |
| ART | 437 | interface 326, eye candy 35, wall 33, item 18, scenery 16, portal 8, monster 1 | frame payloads end before the file does |
| DLG | 38 | dialog files | 32 non-adjacent record fields, 4 tokens outside records, 2 non-decimal numbers |
| MES | 1 | one table | duplicate message number |

The ART reason is checked after every frame payload has been bounds-checked, so
these files carry extra bytes after the last frame rather than missing ones
(inferred from the decoder's check order, not measured byte by byte). The
Crash Site area uses none of them. Walls, portals, scenery and eye candy are
drawn in the world, so 92 refused files matter for rendering; interface ART
matters only for the game UI.

## Proposal for ARC-DATA-015 (P)

1. **Source.** Every input comes from `ContentStack` by virtual path; the
   loader takes a module name and never a file path. A module, patch or loose
   override then works without code changes.
2. **Map catalogue.** A map is a folder under `maps/` with `map.prp`,
   `terrain.tdf`, sector files, editor mobiles and an optional aggregate. Read
   the catalogue and properties eagerly; sectors on demand.
3. **Checks.** Keep strict structural checks with explicit limits (largest
   observed: 1,915 objects and 47 lights per sector; sector files stay under the 1 MiB cap), all
   five sector versions, v119 objects of types 0..17.
4. **Refusal unit.** Refuse one record, not the map: a sector that does not
   frame becomes an explicit unavailable sector, an object or ART file that does
   not read becomes N/A with its reason, and a counter reports both.
5. **ART trailing bytes.** Accept a file whose frames end before its end and
   record the count of trailing bytes, instead of refusing it (needs the exact
   trailing sizes first; see U2).
6. **Identity.** Record a fingerprint of the content stack (sources and their
   hashes) in reports and session snapshots, not as an admission gate. Keep
   the Crash Site and cave pinned profiles as test oracles.

## Unknowns (U)

- **U1 Generated sectors:** template choice, transition tables, relocation of
  template objects and lights, and whether absent interior sectors inside a
  larger TDF grid are generated or void. Next: ARC-RES-034.
- **U2 ART trailing bytes:** exact trailing sizes of the 437 files and which of
  the 92 world ART files are referenced by placed objects. Next: ARC-DATA-015
  measures both before choosing the policy.
- **U3 DLG refusals:** whether the 38 files are valid for the original reader.
  Next: when a world conversation needs one of them.
- **U4 Map transitions:** jump points and scripted teleports. Next: ARC-RES-035.
- **U5 Modifications:** no mod was examined. A mod can add object or sector
  variants this installation does not contain; the record-level refusal above is
  what keeps such a mod loadable. Next: a real mod chosen by the owner.

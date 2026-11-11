# Original verification ledger: census triage of 0x4D0000–0x4FFFFF

Task ARC-RES-111, V1 wave 1, slice F of the [function audit plan](../function-audit-plan.md#5-recommendation). The band holds the
time-event, effect, map and lock code of the retail 1.0.7.4 `Arcanum.exe` (SHA-256 `9bd80511…3678d52`): 922 function starts of the
[census](../../verification/census.csv), 67 of them already rows of the [registry](../../verification/functions.csv). Every one of the 922
addresses now has one state, kept per address in [`triage/band-4d-4f.csv`](../../verification/triage/band-4d-4f.csv); the rows of the
registry grew by 633 (3 marked, 167 read and not implemented, 463 not yet read). The 222 not relevant functions have no registry row.

## Method

- **Source of the list:** the census rows of the band (the 391 cited and not registered addresses first, 253 reached from a game-loop
  root or a registry function, then the `address_taken` rows, then the rest).
- **Evidence of a reading:** an address counts as read only when a ledger or architecture row names it as the function it is about (the
  function cell of the row, not a mention in a reading); the level is the level the row states (ledger area files: two readers, F2;
  architecture rows: the row's own F1 or F2). Documents under `docs/research` and `docs/requirements` that only list an address, and
  rows that only mention it, give no reading and leave it at F0.
- **What this task read itself** (llvm-objdump of the player's file, nothing stored): 17 small functions, listed under
  [readings of this task](#readings-of-this-task), and the structure of the rest: callees, the data globals and strings they use, their
  callers and reach. The structure sorted the library, debug, platform, editor and network code (the samples under
  [sample checks](#sample-checks) back each class).
- **Not done:** no Ghidra run, no oracle run, no function-by-function read of the 463 not yet read ones.

## States

| State | Meaning | Where it is kept |
| --- | --- | --- |
| registered | a row of `functions.csv` existed before this task | the registry |
| implemented | read in this task, and a method carries its mark (row added, level F1) | the registry, the code |
| not implemented | read (the row or reading named in the registry reason) and no method carries its mark; the reason says what covers the area | the registry, F1 or F2 |
| not yet read | named by the census only, or only listed in a document | the registry, F0 (a `not implemented` row; the name is the function family) |
| not relevant | never needed by a single-player reimplementation, with a reason class | the triage CSV only |

"Not implemented" here means that **no method claims the function**. Where the project has a component for the area (the sector cache,
the lights of a sector, the object directory, the terrain templates, the message readers) the component may cover it on its own design;
it has not been compared function by function, and the registry reason names the component. The V4 gate of the plan checks it.

Reason classes of not relevant: `editor` (level-editor or tool code), `multiplayer` (network layer: packet writers and receivers, the
wrappers that apply a raw rule and send it, the online matchmaker), `debug` (validation, assertions, log output), `library` (zlib 1.1.2
and the TIG memory manager), `platform` (window and mouse glue of the original engine, replaced by Godot input), `renderer` (drawing
of the original's 2D pipeline that the viewer replaces by its own).

## Counts

| Domain | Registered | Implemented (marked now) | Read, not implemented | Not yet read | Not relevant |
| --- | --- | --- | --- | --- | --- |
| audio | 7 | 0 | 0 | 0 | 0 |
| debug | 0 | 0 | 0 | 0 | 13 |
| dialogue | 2 | 0 | 0 | 9 | 0 |
| effects | 8 | 0 | 15 | 6 | 0 |
| fate | 5 | 0 | 0 | 1 | 0 |
| goals | 1 | 0 | 0 | 0 | 0 |
| input | 0 | 0 | 3 | 3 | 15 |
| items | 1 | 0 | 0 | 0 | 0 |
| library | 0 | 0 | 0 | 0 | 68 |
| lighting | 2 | 0 | 29 | 54 | 6 |
| locks | 4 | 0 | 1 | 0 | 0 |
| magic | 8 | 0 | 7 | 6 | 0 |
| maps | 9 | 0 | 20 | 109 | 1 |
| multiplayer | 0 | 0 | 0 | 0 | 22 |
| names | 0 | 0 | 15 | 46 | 0 |
| objects | 0 | 0 | 38 | 95 | 3 |
| persistence | 0 | 0 | 4 | 7 | 3 |
| rendering | 1 | 0 | 0 | 0 | 0 |
| residency | 7 | 1 | 6 | 19 | 0 |
| scene | 0 | 0 | 3 | 14 | 0 |
| scripts | 0 | 0 | 6 | 21 | 91 |
| technology | 1 | 0 | 0 | 0 | 0 |
| text | 0 | 2 | 3 | 31 | 0 |
| travel | 1 | 0 | 0 | 0 | 0 |
| ui | 7 | 0 | 10 | 34 | 0 |
| world | 3 | 0 | 7 | 8 | 0 |
| **Total** | **67** | **3** | **167** | **463** | **222** |

Of the 463 not yet read functions, 258 are reached by direct calls from a game-loop root or a registry function (the census reach is
not `none`) and 95 of those are cited in a document. The read, not implemented rows are mostly the lights (LT1–LT15), the object model
(OM1–OM6, RL3–RL12), the sector files (PS21–PS25) and the effect accessors.

## Not relevant

### editor: 4
- 0x4D1530–0x4D1530 editor sector loader (1)
- 0x4D22E0–0x4D22E0 editor sector callback (1)
- 0x4D2460–0x4D2460 editor sector writer (1)
- 0x4E9580–0x4E9580 terrain editor decompress (1)
### debug: 16
- 0x4D2C00–0x4D2C00 object system validation (1)
- 0x4D2C30–0x4D2C30 object system validation failure (1)
- 0x4F1D80–0x4F1D80 object list consistency check (1)
- 0x4FEB70–0x4FEBC0 debug log write (2)
- 0x4FEC00–0x4FEF00 debug output (11)
### renderer: 10
- 0x4DA360–0x4DA360 light draw routine (1)
- 0x4DDF20–0x4DE060 light shadow setup (3)
- 0x4DE200–0x4DE250 light blit helper (2)
- 0x4FFB40–0x4FFD60 cursor drawing (3)
- 0x4FFEA0–0x4FFEA0 cursor helper (1)
### multiplayer: 113
- 0x4ED510–0x4ED780 network wrapper of a rule (5)
- 0x4ED9E0–0x4EDEE0 network wrapper of a rule (12)
- 0x4EDFF0–0x4F05F0 network wrapper of a rule (72)
- 0x4F0640–0x4F0640 handle to id converter (1)
- 0x4F0690–0x4F0690 id to handle converter (1)
- 0x4F5820–0x4F60E0 online matchmaker call (22)
### library: 68
- 0x4F8400–0x4FDFF0 compression library (57)
- 0x4FE380–0x4FEB10 memory manager (10)
- 0x4FEB60–0x4FEB60 memory manager helper (1)
### platform: 11
- 0x4FF020–0x4FF350 mouse library helper (7)
- 0x4FF9B0–0x4FF9B0 mouse library helper (1)
- 0x4FFA30–0x4FFAB0 mouse library helper (3)

The multiplayer ones are the 91 starts of the block 0x4ED510..0x4F06E0 that are not registry rows and not 0x4EDF20 (SA25 counts the
block at 92–93 starts with about 63 network ones), plus the 22 of the matchmaker. Each wrapper applies a raw rule that has its own row and
then sends or receives the packet, or converts a handle and an id for a packet (0x4F0640, 0x4F0690); the single-player path is the raw
call. The move wrapper 0x4EDF20 is the exception found by the review: for the hero it first centres the view (0x4B8CE0), then
relocates (0x43E770); it is a read, not implemented row, not a not relevant one.

## Readings of this task

Three functions are now marked and have rows at F1:

| Function | Reading | Difference |
| --- | --- | --- |
| 0x4D04E0 sector loaded test | binary search of the sorted slot index (0x4D2CA0, below) by the 64-bit sector id; found and the slot flag word (+0x10, bit 0x80000000) clear gives 1, else 0 | the flag is not kept: the project loads a sector inside the lock, so a slot in the index counts as loaded (`SectorCacheState.IsLoaded`) |
| 0x4D4210 object description text | the number is refused below 0 and above the last loaded number; below 30000 the base table (0x6018D4) serves, from there the module table (0x6018D0, refused when absent); the text is the entry the message search (0x4D45E0) finds, else none | a number below 1 gives none here (`LogbookTexts.DescriptionName`), where the original also looks up 0 |
| 0x4D4260 key name text | the entry the message search finds in the key table (0x6018CC) for the key number, else none; no bound test | a missing table is none here (`LogbookTexts.KeyName`); the original passes the table as it is |

The functions below were read once by this task and have rows at F1 (state not implemented: no method claims them):

| Function | Name | Reading |
| --- | --- | --- |
| 0x4D2CA0 | sector index search | binary search of the sorted slot index by the 64-bit sector id; returns found and the position, no side effect |
| 0x4D7180 | tile soundproof test at a location | tile art at the location (0x4D70B0), then the tile name flag 0x20 (0x4EBBC0) |
| 0x4D71A0 | tile flag 2 test at a location | tile art at the location, then the tile name flag 0x2 (0x4EBB30); called by the blocked test 0x4D7110 when its flag argument is non-zero and the tile blocks; that caller drops the result and returns 0 |
| 0x4D71C0 | tile flag 8 test at a location | tile art at the location, then the tile name flag 0x8 (0x4EBB80) |
| 0x4EA9C0 | effect adjustment of table 0x603ac4 | the same call over the effect table at 0x603AC4 |
| 0x4EA9F0 | effect adjustment of table 0x603acc | the same call over the effect table at 0x603ACC |
| 0x4EAA20 | special effect adjustment 0 | the same call over the special effect table (0x603AD8), kind 0 |
| 0x4EAA50 | special effect adjustment 1 | the special effect table, kind 1 |
| 0x4EAB00 | special effect adjustment 3 | the special effect table, kind 3 |
| 0x4EAB30 | special effect adjustment 4 | the special effect table, kind 4 |
| 0x4EAB60 | special effect adjustment 5 | the special effect table, kind 5 |
| 0x4EAB90 | special effect adjustment 6 | the special effect table, kind 6 (the critical miss chance of CR11) |
| 0x4EABC0 | special effect adjustment 9 | the special effect table, kind 9 |
| 0x4EBB30 | tile name flag 2 test | -1 gives 0, else the flag word (0x4EBAD0) and 0x2 |
| 0x4EBB80 | tile name flag 8 test | -1 gives 0, else the flag word and 0x8 |
| 0x4EBBA0 | tile name flag 10 test | -1 gives 0, else the flag word and 0x10 |
| 0x4EBBC0 | tile name flag 20 test | -1 gives 0, else the flag word and 0x20 (the soundproof flag of 0x4D7180) |

Naming correction: 0x4D70B0 reads the sector of the location through the sector cache (lock, tile index 0x4D7090, unlock) and returns the
**tile art word** of the location, or −1 when the sector does not load; the blocked test 0x4D7110 and the flag tests above treat the
result as a tile art. The oracle's stub label `SECTOR_OF` (`tools/original-oracle/oracle_functions.py`) and the ledger text of LR6 call it the
sector.

### Sample checks

- **Network wrappers:** 0x4EDC00 builds packet 0x57 from two field reads and sends it (0x5295F0); 0x4EFA70 builds packet 0x78 and sends
  it; 0x4ED630 packs a player record into a packet buffer. All three are packet code with no game rule.
- **Wrapper pattern:** the callee lists of the 92 wrappers of the block (0x4EDF20 included) hold the send call 0x5295F0 (63), the object id getter 0x407EF0 (35) or the handle lookup 0x4E50E0 (19) plus
  one raw rule, as SA25 describes.
- **Libraries:** the strings of 0x4F8400..0x4FDFF0 are zlib's error messages and version (1.1.2); 0x4FE380..0x4FEB10 are the TIG
  guard-byte allocator; 0x4FEB70..0x4FEF00 write a debug text file and the registry key of the debug output.
- **Editor:** 0x4D1530, 0x4D22E0 and 0x4D2460 are the editor's sector callbacks (PS23); 0x4E9580 is called an editor decompression by a research note (F0), unproven: its caller tests a flag read from the file header. The
  review took 0x4E8CE0, which a research note calls an editor encoder, out of this class: it packs the terrain word the game reads (a not
  yet read row), and 0x4F8350..0x4F83A0 (the node pool of the sector light list) and 0x4DA310 (a free-list push of light records) out of
  the library and renderer classes.
- **Matchmaker:** the 22 starts of 0x4F5820..0x4F60E0 are the online matchmaker (strings `matchmaker_*`, NT1).

## Not yet read

463 rows of the registry carry the level F0 and the reason `not read yet`, with the census reach, the direct callers and, where it
exists, the document that names the address. The name is the family the function sits in (a range of the census with one global data
cluster), not a reading. The families with the most unread functions are the map files and tile code (108), the object index, pool,
id and sparse-array code (95), the lights (50), the name loaders (46) and the message file reader (31).

To continue: read the 95 cited and reached ones first, by family, then the other reached ones; each reading moves its row to F1 and
names the function.

## Limits

- The triage by structure puts functions in the not relevant classes without reading every body; the samples above show each class, and
  the points below are the ones to confirm.
- The census misses a function behind another one that ends with no padding: 0x4EBB50 (a tile name flag test: bit 0, then bit 2, default
  1) follows 0x4EBB30 and is not a start of the census.

## Points to confirm

No behaviour changed in this task (three marks and registry rows only), so it adds no step to the
[owner re-check list](../../project/owner-recheck-list.md). These are classification points for the next slices and the V4 gate:

1. **Network wrappers (91 and two converters, not relevant).** Each one applies a raw rule with its own row and sends the packet; a wrapper that does more
   in single player than call the raw rule would belong in the audit. 0x4EDF20 was such a case and is a read row now; the item use
   wrappers 0x4EF6F0 and 0x4EF830 call 0x462CC0 and 0x4445A0 (outside this band), and the receiver 0x4EDFF0 has the shape of the move
   wrapper but runs only in a networked game.
2. **Renderer (10, not relevant).** The light blits, the shadow setup and the cursor drawing were sorted by their callees (draw
   primitives only), not read in full.
3. **Read, not implemented (167).** Where the project has a component for the area (sector cache, lights at sector load, object
   directory, terrain templates, message readers) the function may be implemented unmarked; the V4 gate of the plan finds those. Two are
   already named in code: the tile blocked test 0x4D7110 and the soundproof test 0x4D7180 (the path request, the drop map and the
   awareness rules cite them) carry no mark.
4. **0x4D4080 item effect text.** The viewer's `AreaItemIcons.EffectsText` implements it, in an assembly the registry cannot mark (the
   marks must lie in `Arcanum.Data`, `Simulation`, `Scripting`, `Session` or `Presentation`).
5. **Census gap.** A function behind a function that ends with no padding is not a start of the census (0x4EBB50); the scan could add a
   pass for the instruction after an unconditional return that no branch of the same function targets.
6. **Oracle label.** `SECTOR_OF` (0x4D70B0) holds the tile art word of a location, as read above; the oracle's stub models what the
   blocked test needs, so the label only misleads.

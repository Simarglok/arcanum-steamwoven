# Whole-exterior passability closure

Status: **Research**, direct owner request of 2026-10-15. This document closes,
at research level, the terrain, registry, HP and neighbour frontiers that the
[whole-exterior step contract](../world/crash-site-area-traversal-contract.md)
left NOT_READY. It changes no production API, no viewer and no movement
permission. The production [cardinal assessment](../world/cardinal-traversal-assessment.md)
still admits only local x4..8/y0..3. A separately authorized implementation
is described in [Proposed implementation](#proposed-implementation-not-authorized).

## Vocabulary and boundary

- **Source fact**: a claim from an earlier source reading (provenance in git history), cited by the executable address of its function; level F0 (not checked in the executable) unless a ledger row is cited. It is not an observation of the running
  original.
- **Local observation**: an aggregate measured by the research probe on the
  owner's unchanged installation. No original text, names or bytes are
  recorded here or printed by the probe.
- **Project policy**: a choice this project makes where the source leaves a
  runtime or product decision open.

The code in `tools/research/Arcanum.CrashSiteProbe/AreaPassabilityClosureProbe.cs`
was written independently from the rules stated in this document. Original executable parity remains **NOT_VERIFIED**.

**Initial state T0** means: a new game has emptied the current save folder,
the map is open, its mobile file and `map.patch` are applied, sectors are
loaded and folded. No time event, AI tick, script, dialog or combat has run.
Every closure below holds at T0 only. What changes after T0 is listed in
[Dynamic boundary](#dynamic-boundary-after-t0).

## Selected rule

The normal-ground step check `sub_425760`
(0x425760)
is unchanged from the step contract. It checks the **destination** tile with
`tile_is_blocking` once, then calls the object traversal routine. The source
tile and diagonal shoulder tiles are never checked for terrain.
The actor context remains the declared diagnostic human: no polymorph, portal
or window flags are relevant because the area has no walls or portals (F5).

## Findings

| ID | Claim | Source fact | Local observation |
| --- | --- | --- | --- |
| F1 | Tile-name flags | tile name counting and loading (0x4EB270, 0x4EB2E0), name flags (0x4EBAB0, 0x4EBAD0), `mes_search` (0x4D45E0), `mes_merge` (0x4D46F0) | All 38,678 cells that the conservative production component reports Unknown resolve; 0 remain unresolved. |
| F2 | Absent block mask | `sector_cache_init` (0x4CF810), `sector_load_game` (0x4D1A30), sector block list (0x4F81E0, 0x4F8200, 0x4F8230, 0x4F8250), `tileblock_is_tile_blocked` (0x4BB410) | The three mask-less sectors are context sectors with version `0xAA0003`. All nine core sectors are `0xAA0004` and have a mask. |
| F3 | Resource precedence | `map_load_mobile` (0x410D10), `map_open` (0x40F4E0), `gamelib_reset` and `gamelib_exit` (0x402380, 0x4024D0) | The four consumed rule/name resources and every present sector file occur exactly once across all seven archives. No loose override, sector or save difference file exists under the module and data folders. |
| F4 | Patch file | `map_load_mobile` (0x410D10), object ID text conversion (0x4E6570) and GUID parser (0x4E67A0) | The PATCH archive's `map.patch` names 50 objects; none of the 98 core/context mobile records contains any of those IDs in either byte order. |
| F5 | Exact-tile registry | `obj_inst_read_file` (0x409AA0), `objlist_load` (0x4F1350), `objlist_fold` (0x4F1CD0), `objlist_insert_internal` (0x4F2020), `object_init` (0x43A330), `object_list_location` (0x4407C0) | 98 mobile records re-verified in place; 0 walls or portals; 123 destination-blocker cells. |
| F6 | Critter life | `critter_is_dead` (0x45D8D0), object hit point functions (0x43D5A0, 0x43D600, 0x43D630, 0x43D690), stat tables, `stat_level_get` (0x4B0490), `stat_base_get` (0x4B0740), stat level minimum and maximum (0x4B0FC0, 0x4B0FD0), effect loading and `effect_parse` (0x4E99F0, 0x4E9C20), `effect_adjust_func` (0x4EA6C0), `background_get` (0x4C2690) | 42 visible critters: 38 alive (minimum current HP 10), 4 dead (maximum current HP −31,952). None is Unknown. |
| F7 | Step composition | object traversal (0x43FD70, 0x43FDC0, 0x43FE00), `location_in_dir` (0x4B8FF0), `tile_art_id_at` and `tile_is_blocking` (0x4D70B0, 0x4D7110) | All seven frozen production observations at spawn are reproduced. |
| F8 | Reachability | Composition of F1–F7 | 8,138 cells reachable from spawn; the only exits lead into an unsurveyed sector south of the context ring. |

### F1 — tile-name flags

`tile_is_blocking` returns blocked when the serialized mask bit is set. For a
facade (art class 11) it returns the inverse of raw bit 0. Otherwise it uses
the tile-name flags of the class-0 tile. The flags are built once from
`art\tile\tilename.mes`:

- The MES keys are sorted. Keys 0..99, 100..199, 200..299 and 300..399 fill
  four category arrays in ascending order. The array index restarts only at
  exactly 100, 200 and 300, so rank-within-category is the material index
  **only when keys 0, 100, 200 and 300 all exist**. The probe requires them.
  A bare value shorter than three characters anywhere in 0..399 aborts the
  whole table; the probe refuses such a table.
- A value with a `/` has its flags in the characters after the **first** `/`,
  up to the first space or the end. `b` and `f` set the blocking flag. Every
  other character, **including a later `/`**, is ignored. A sound number may
  follow the space.
- A value without `/` carries no flags. It must have at least three characters
  (see above).
- The category is selected by tile bit 8 (outdoor) and bit 7 (flippable); the
  material index is bits 22..27.

The production component is deliberately stricter. It admits one `/` and a
fixed flag alphabet, and treats everything else as Unknown. On this input
every Unknown name value contains a second `/` inside its flag run. One
blocking value covers 37,364 cells, mostly the all-blocked border sectors.
Three non-blocking values cover 175 core cells. This includes the historical
91 unresolved cells of the base sector: they all resolve to non-blocking. The
tile name table has 88 records; 52 of them fall in the four categories (31, 6,
2 and 13 entries) and the rest are edge records at 400 and above. The other
1,139 conservative Unknown cells are cells of the three mask-less sectors
(F2) whose name or facade component was already supported.

### F2 — absent block mask

A sector file stores a version word (`0xAA0000`..`0xAA0004`). The block list
is read only for `0xAA0004`. The sector cache initializes the block list to
zero when the cache entry is created, before loading. A sector of version `0xAA0003` therefore has **no blocked
mask bits**: the mask component is `NoBlock`, not Unknown. The probe fails if
a `0xAA0004` sector lacks the mask or an older sector reports one.

### F3 — resource precedence and new-game state

The probe searches module, resource1..4, tig and the PATCH archive. Each of
`art/tile/tilename.mes` (resource2), `rules/effect.mes` (resource3),
`rules/gameeffect.mes` (module) and `rules/map.patch` (PATCH archive) occurs
exactly once, and so does every present sector file (module). The required
loose roots (the module folder and the data folder) contain no file of those
names and no `.sec`, `.dif` or `.mdy` file. Choosing between duplicates is
therefore unnecessary for this input. The mobile file and prototypes are the
Area owner's authenticated selection; their shadowing is not re-checked here.
No general archive, PATCH or loose-file precedence is inferred.

Game initialization and module load empty `Save\Current`. A new game
therefore has no sector `.dif`, `mobile.md`, `mobile.des` or `mobile.mdy`. (The
map open path itself empties the map save folder only after reading those
files, so the conclusion rests on the `Save\Current` emptying.) The map's
mobile file is read in full and `map.patch` is applied before any sector is
loaded.

### F4 — patch file

`map.patch` lines name an object ID followed by attribute changes. Every line
of this input is a `G_` GUID line; any other non-comment line fails the probe.
The earlier reading of the string parser stored the 32 hex digits in text order,
but the observed mobile file stores GUIDs in the mixed-endian Windows layout: 32 of
the 50 targets occur in the file in that layout and none in text order. This
source/observation discrepancy is recorded, not resolved here; ledger MP3 later
found that the original parser stores the Windows layout. The probe therefore
searches each core and context mobile record's raw bytes for **both** byte
orders, which is a superset of an own-ID match. There are 0 hits in either
order. Statics are not yet loaded when the patch is applied, so the patch
cannot reach them. Resolved by ARC-DATA-028: the original executable stores a parsed
GUID in the Windows layout, as the object files do
([ledger MP3](ledger/data.md#map-patch-arc-data-028)), so its lines do
find their objects; whole maps now apply them ([map patch](../world/map-patch.md)).

### F5 — exact-tile registry at T0

- Every object read from the mobile file is added to the location registry.
- When a sector is loaded, its static objects are read from the sector file
  with the low-six-bit location rebase. Fold then inserts every registered
  non-inventory, non-static object located in that sector into its tile list.
- The destination query uses the type mask `0x3801F`: walls, portals,
  containers, scenery, projectiles, PCs, NPCs and traps. Items (types 5..14)
  are excluded by type. Outside the editor, objects with `OF_DESTROYED` or
  `OF_OFF` are excluded. Because the mask includes scenery, the query always
  locks the destination sector. A neighbour sector's list is therefore the
  same sector-local list, with no cross-sector special case.
- A non-critter blocks unless `OF_NO_BLOCK` (`0x400`) is set. A critter blocks
  while it is alive, regardless of `OF_NO_BLOCK`.

Local observation: 98 mobile records (88 core, 10 context) were re-hashed at
their recorded offsets. There are 426 static scenery records, 0 walls and 0
portals. Excluded: 16 inventory, 1 `OF_OFF` context NPC, and 37 items.
343 scenery records have `OF_NO_BLOCK`. The 123 blocker cells are 83 scenery,
2 containers and 38 live critters.

### F6 — critter life

`critter_is_dead` is `current HP <= 0`. For an NPC:

```text
current = maxhps-effects(WP + 2 × (ST + Level) + 4 + HpAdj + 4 × HpPts) − HpDamage
```

- WP, ST and Level are `stat_level_get` values. Each starts from the serialized
  stat base. ST first loses `min(3, poison / 100)`. Effects are applied, then
  the result is clamped to `[1, race maximum]` for ST and WP and `[0, 51]` for
  Level. An NPC has no background, so no background adjustment applies.
- Effects come from the critter's effect array, indexing `rules\effect.mes`
  (50..399) and `rules\gameeffect.mes` (0..49). Tokens are separated by
  spaces and commas. Changes stack as percent, multiply, divide, add,
  strongest minimum, strongest maximum. The probe admits only add, percent and
  min/max: an earlier reading had the parser read a `*` or `/` factor as zero
  (ledger LM9: the original reads the number after the operator), and `-1` is its
  "unset" bound, so those forms fail closed.
- Every race maximum is at least 20. The probe therefore computes an exact
  ST or WP inside `[1, 20]` and an interval above it. Level's bound does not
  depend on race and is exact. A `maxhps` effect, an effect
  outside the admitted grammar or vocabulary, a stat array that is not exactly
  28 entries, or an interval that straddles zero all fail closed.

Local observation: no referenced effect changes `maxhps`. Every value is
exact. 30 core NPCs are alive with current HP 10..50; 4 core NPCs carrying
32,000 damage are dead; 8 visible context NPCs are alive with HP 10..34. The
smallest alive margin is 10 HP and the largest dead value is −31,952.
Dead critters do not block, and their `OF_NO_BLOCK` flag is irrelevant.

### F7 — step composition and neighbours

- **Cardinal step**: the destination tile must not block, then no destination
  blocker may exist at the destination cell.
- **Diagonal step**: the destination tile must not block. The object routine
  then checks four object-only legs: source to the counter-clockwise shoulder,
  that shoulder to the destination, source to the clockwise shoulder, and that
  shoulder to the destination. With no walls or portals, this means no
  blocker may sit on either shoulder or on the destination. Shoulder terrain
  is not consulted.
- `location_in_dir` fails only at the map limits. Crossing a sector boundary
  needs no other rule. The neighbour's terrain and object list are the loaded
  data of that sector.

Local observation: from spawn `(92934,82561)` the probe reproduces all seven
frozen observations of the production assessment: X−1 blocked by an object,
X+1 open, both west diagonals blocked by an object, both east diagonals open,
and the reverse step back to spawn open.

### F8 — reachability

Breadth-first search over F7 from spawn, at T0:

| Region | Reachable cells |
| --- | --- |
| Core 1451,1289 / 1452,1289 | 167 / 822 |
| Core 1451,1290 / 1452,1290 / 1453,1290 | 693 / 3,048 / 766 |
| Core 1452,1291 / 1453,1291 | 1,591 / 63 |
| Context 1451,1292 / 1452,1292 | 291 / 697 |
| **Total** | **8,138** (7,150 core, 988 context) |

No reachable step touches one of the five absent sectors of the surveyed ring.
The only exits are 16 cells, x 92912..92927 at y 82752, in sector (1451,1293).
That sector exists in the module archive but lies **outside the surveyed 5×5
area**. The walkable region continues south beyond the Crash Site survey. All
open cells form 82 components; the two largest have 8,138 and 1,540 cells.

## Dynamic boundary after T0

These facts are **not** closed and need separate work:

- Fold schedules an immediate AI time event for every non-destroyed NPC
  (0x441FC0, 0x4AD790).
  NPC positions can change from the first tick.
- Scripts (heartbeat, sector, tile, dialog) can change flags, HP and
  positions. For example, the `OF_OFF` context NPC can be switched on.
- Start-location placement of the PC and party, combat, death and saved games
  (`.dif`, `mobile.md`) are outside T0.
- Absent sectors would be generated from `terrain.tdf` by `terrain_fill`
  (`sector_load_game`, 0x4D1A30).
  This is not researched, but no reachable step reaches such a sector.
- `PATH_FLAG_0x1000` remains an unavailable caller branch, and other actor
  contexts remain undeclared.

## Proposed implementation (not authorized)

Nothing below is delivered by this research. Each stage needs its own owner
approval. The owner chose option (a), core only, with T0-fixed NPCs; stage 1 is
now the [whole-core step assessment](../world/area-step-assessment.md).

1. **Data**: an immutable area-wide step assessment built from the Area owner,
   using the F1/F2 terrain rule, the F5 registry and the F6 life verdicts at
   T0, with the same coverage, disposition, gap and identity binding style as
   the cardinal assessment. It reuses the frozen regressions. Every fail-closed
   case above becomes a typed gap, not an exception.
2. **Viewer**: the `area` mode moves the HumanFixed actor with that
   assessment instead of the x4..8/y0..3 profile.
3. **Docs**: amend the step and passability contracts, the component
   catalogue and the requirements coverage.

Owner decisions needed before stage 1:

- **Admitted boundary**: (a) the nine core sectors only, with the core edge as
  a refused boundary; (b) core plus the present context ring (adds 988
  reachable cells), with the south exit refused; or (c) first extend the Area
  survey south. The exit at y 82752 must be refused or researched in every
  option.
- **NPC state policy**: treat NPC positions and life as fixed at T0 (no AI in
  this project yet), recorded as project policy.

## Reproduce

Build with the pinned [toolchain](../workflow/toolchain.md). Inputs are
explicit and read-only; the oracle is the area oracle of the
[area loader](../world/crash-site-area-loader.md) with SHA-256
`bfe9770d75a1c1922ca8817fd0c239e589c3de48020022f7de6bd450ef2d2b60`.
Input and resource SHA-256 values are printed by the probe; private paths,
command logs and original text stay local.

```sh
"$DOTNET" restore tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --locked-mode
"$DOTNET" build tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --no-restore
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --area-passability-closure-self-test
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --area-passability-closure \
  --module MODULE --resource1 RESOURCE1 --resource2 RESOURCE2 --resource3 RESOURCE3 \
  --resource4 RESOURCE4 --tig TIG --patch MODULE_PATCH0 --mobile MOBILE \
  --prototype-root PROTOTYPES --oracle ORACLE --loose-root MODULE_DIR --loose-root DATA_DIR
```

The self-test makes 45 synthetic assertions over the F1, F6, F7 and F8 rules,
the MES and `map.patch` structure, including both GUID byte orders. The original run exits 0 only when every
frozen cross-check matches. It fails closed on any unresolved tile name,
mask/version mismatch, wall or portal, duplicated resource or sector, loose
override, unsupported or area-hitting `map.patch` line, changed input, mobile
record mismatch or unknown critter life.

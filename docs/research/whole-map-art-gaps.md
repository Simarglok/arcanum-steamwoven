# Whole-map art gaps and the original's missing-art fallback

Status: **Research**, work item ARC-RES-050 (2026-10-17). Census measured on the
owner's installed original (GOG 1.0.7.4, `DAT_ROOT`); name rules from an earlier
source reading (provenance in git history), cited by the executable address of
each function, and checked in the retail
`Arcanum.exe` (SHA-256 `9bd80511…8d52`) by the
[original executable check](original-executable-check.md) where the ledger says so, rows MA1–MA8 of the
[verification ledger](original-verification-ledger.md#art-names-and-missing-art-arc-res-050).
No code changes; the slices below implement it.

## Scope

After ARC-DATA-020, the whole-map read budgets (PR #164) and ARC-RENDER-018, 54 of the 80 interiors publish a
complete [whole-map snapshot](../world/whole-map-plans.md) and load in the viewer's
[map mode](../rendering/area-map-mode.md). This study says what stops each of the
other 26, how the original game names and falls back for the art involved, and
which universal rules close the gap. It also corrects the earlier cause table:
no eye-candy overlay is active at T0 in any interior.

## Method

A scratch runner (not committed) opened every interior with
`AreaMapPlans.Create` → `Open` → `Inspect` on `origin/main` at `1ec13f5`, grouped
the issues by code and stage, and matched each object issue to the one object
record of the same source index that is not schema-ready. For each failed object it
printed the type, prototype, effective art id (field 0), flags (field 18) and the
three overlay arrays (fields 5–7), and for every snapshot (complete or not) it
counted active overlay entries. Archive entry names, `art/missing.dat`,
`art/badart.art` and the relevant ART headers were read with the
`dat_probe.py` archive reader. The whole pass takes about two minutes.

## What stops the 26 maps

Object issues outside K'na Tha: 245. Every one is the body art of a single object;
terrain, sectors, prototypes and mappings of all 26 maps are complete.

| Map | Issues | Cause |
| --- | ---: | --- |
| ashbury haunted castle | 28 | concealed monsters |
| bmc | 7 | concealed monsters |
| caladon-panarrii temple | 1 | scenery object with tile art |
| castle s'nel n'fa | 8 | concealed monsters |
| cave of the bangellian scourge | 1 | unique NPC without standing art |
| crematorium-lev 2 | 24 | concealed monsters |
| crematorium-lev 3 | 9 | concealed monsters |
| dernholm pits | 1 | damaged wall |
| dungeon of the dragon pool | 21 | unique NPC without standing art |
| k'na tha | 15,374 | frame budget (15,367) and 7 issues it hides |
| pit of fires | 6 | concealed monsters |
| pschuylers lev 1-fixed | 8 | concealed monsters |
| pschuylers lev 2-fixed | 16 | concealed monsters |
| scienticians underground lev 1 | 4 | concealed monsters |
| scienticians underground lev 2 | 4 | concealed monsters |
| tarant sewers-01 | 3 | concealed monsters |
| temple of the derian ka | 9 | concealed monsters |
| the lair of bellerogrim | 49 | unique NPC without standing art |
| tulla interior | 1 | monster without standing art |
| tulla interior - battle - force | 2 | unique NPC without standing art |
| tulla interior - battle - mental | 5 | unique NPC without standing art |
| tulla interior - battle - necro evil | 2 | unique NPC without standing art |
| tulla interior - battle - necro good | 16 | unique NPC without standing art |
| tulla interior - battle - phantasm | 11 | unique NPC without standing art |
| void | 1 | unique NPC without standing art |
| wheel clan | 8 | 6 concealed monsters, 2 unique NPC without standing art |

### Concealed monsters (132 objects, 13 maps)

Monster critters (art class 12) whose art id holds animation 5, the original's
"conceal fidget" (letter `f`), with no weapon (weapon 0, letter `A`), on the last frame of the animation: the
lurking pose. Species 3 (`ert`), 5 (`ls1`), 6 (`lz1`), 7 (`lm1`) and 10 (`msp`)
occur; each has `…uwxaf.art` in `arcanum1.dat`, and every frame index used (4, 5,
10, 11, 12) is that file's last frame (5, 6, 11, 12 and 13 frames). The owner
admits only animations 0 (stand) and 7 (fall down), so these are refused as an
unsupported state although their art exists.

### Critters without standing art (111 objects, 11 maps)

110 objects of unique NPC 20 (`dgnuw` in `art/unique_npc/unique_npc.mes`) and one
monster of species 28 (`AIR`) stand with no weapon (animation 0, weapon 0, letter `A`). Neither has a
`…xaa.art`: `dgnuw` ships only `xab`, `xah`, `xai`, `xba` and `xbu`. The owner
requires the standing no-weapon file, so the object fails with a missing source. The
original never asks for that file: its missing-art table marks it absent and the
name rule switches the weapon to 1, "unarmed" (letter `B`), giving `dgnuwxba.art` and
`airuwxba.art`, which exist (14 and 16 frames; frame 0 is drawn).

### Damaged wall (1 object, dernholm pits)

A wall (class 1) with damage bit `0x400` at rotation 7. The owner refuses both
damage bits (`0x480`). The original names damaged walls with the letters `L`/`R`
instead of `U` (rule N6 below); here the rotation swaps the bit to `0x80` and the
piece is 0 (`bse`), giving the `R` file.

### Tile art on a scenery object (1 object, caladon-panarrii temple)

A scenery object (prototype 4038) whose art id is a tile id (class 0: tile 16/16,
indoor, both flip bits). The original draws any art class through the same object
path; the name is the tile's file name (rule N7).

### K'na Tha

77 sectors, 43,219 objects, 315,390 terrain cells, but only **260 distinct art ids**
among the objects. The owner decodes and hashes a frame for every object visual
(terrain selections are already shared), so after 28,091 frame requests (shared terrain selections and object visuals) the
cumulative encoded-frame budget (150,138,368 bytes, the 16× cap) is spent and 15,367 objects
fail with `BudgetExceeded`. At that point the owner had reserved 3.6 GB of managed
memory, 1.19 GB retained and read 960 MB of entries. The seven other issues (four
unsupported states, three missing sources) cannot be attributed while the budget
failures share their source indices; two of them are terrain cells (315,390 of
77 × 4,096 = 315,392 cells are published), and the failed objects include a damaged
wall and walls and scenery that may need the missing-art fallback. Streaming is not
needed first: decoding each distinct selection once per map, as terrain already
does, removes the repeated work (slice 2). After ARC-DATA-023 and ARC-DATA-024 K'na
Tha publishes a complete snapshot with all 315,392 cells from 477 frame decodes,
without bad art, so none of the seven issues remains.

### Eye-candy overlays: none

No object of the 80 interiors has an active overlay or underlay at T0 (fields 5–7
gated by flags `0x8000`/`0x10000`). The "eye-candy overlays" share of the earlier
cause table was a guess from the admission check; overlays and underlays become a
question only for animated effects at run time (spells, `animfx`).

## How the original names critter art and falls back

These rules come from an earlier source reading of the name functions
(0x41CA40–0x41F240), the art name functions (0x4EAC80–0x4ED440) and the ART cache; N1–N3, N6 and the
load fallback of N4 are checked in the retail executable, N5, N7 and N4's frame
lookup are earlier readings, level F0 (not checked in the executable) (ledger rows in
brackets).

- **N1 Normalize before naming** (MA2, MA3). `name_resolve_path` (0x41DAE0)
  normalizes every art id (`name_normalize_aid`, 0x41D510) before building the file
  name, and the art cache keys on that name. Weapon 0 is "no weapon" (letter `A`),
  weapon 1 "unarmed" (letter `B`). For critters (class 2), monsters (12)
  and unique NPCs (13) the normalized id has frame, rotation and palette 0 (they
  select inside the file) and is built in this order:
  1. critters only: an elf female is drawn with the human body; plate armour (4 or
     6) forces the male gender and draws an elf as human and a halfling as dwarf;
  2. the missing-art repair N2 on (animation, weapon);
  3. animation 24 (explode) clears armour, shield and weapon (critters also set
     the male gender, where the reconstruction wrote female; the explode name uses `XX` and
     gender `X` either way, so nothing visible depends on it, MA2);
     animation 23 (stunned) sets weapon 1 and clears the shield;
     otherwise weapon 1 with animation 3 (stealth walk) or 5 (conceal fidget)
     becomes weapon 0; animations 6–19 (run, fall down, get up, 9–16, the three
     bloody deaths) clear weapon and shield; animation 1 (walk) clears the shield.
- **N2 Missing-art repair** (MA1). `fix_missing_art` (0x41DA40) reads one 19-bit
  mask per critter body/gender (10), monster species and unique NPC. Each bit
  stands for one checked (animation, weapon) pair. The pairs are tried in table
  order with the animation as replaced so far; when the animation equals a pair's
  animation whose bit is set (the weapon must match too only for animation 21, low
  attack), the animation is replaced: walk and run → walk, stunned and stand →
  stand, the ten low attacks → attack (20), the four special deaths → fall down
  (7), stealth walk → walk. A replacement by walk also sets weapon 0 when walk
  unarmed is missing; a replacement by stand with weapon 0 sets weapon 1 (unarmed)
  when stand without a weapon is missing. Animations that are not checked (conceal
  fidget among them) are never repaired.
- **N3 The mask file** (MA4). The masks live in `art/missing.dat`: the number of
  critter, monster and unique NPC ART files under `art/critter|monster|unique_npc/*/`,
  then 10 + monster-count + unique-count little-endian words (counts from the two
  name lists). The game uses the file only when its header equals the current file
  count; otherwise it rebuilds the masks by checking each pair's file and writes the
  file again. The installed file (332 bytes, `arcanum1.dat`) has header 4,601,
  which equals the current count, so it is current. The original compares the unique
  NPC read with the unique count (an earlier reading compared it with the monster
  count: reconstruction slip, no effect on this data).
- **N4 Bad art** (MA5). When the cache cannot load the resolved file, the original
  loads `art\badart.art` in its place under the same art id and draws it; only if
  that fails too is nothing drawn. `badart.art` is one rotation, one frame. The
  frame lookup that follows does not check the frame number, so a frame other than
  0 would read past the table (earlier reading, level F0, not checked in the
  executable); only frame 0 is defined.
- **N5 Single-rotation files** (MA6). A one-rotation ART serves every rotation
  (already the whole-map rule since ARC-DATA-016).
- **N6 Damaged walls** (MA7). The wall's damage bits are `0x400` and `0x80`. At
  rotations 2, 3, 6 and 7 the two bits swap. Then `0x400` names the `L` file and
  turns piece 7 into the base piece; `0x80` turns piece 8 into the base piece and
  names `L` for pieces 1–6 and `R` otherwise. An earlier reading had pieces 2–6:
  piece 1 (`lfc`) differs (reconstruction slip).
- **N7 Tile art on an object** (MA8). The object draw (`object_draw`, 0x43B390)
  and the name resolver handle every art class alike; a tile id names the tile's
  file (`a_name_tile_aid_to_fname`, 0x4EAE90), rotation and frame are 0 and the
  palette comes from bits 4–5, as for a terrain cell.

With N1–N2 the 132 concealed monsters and the 111 critters without standing art
resolve to existing files; N6 and N7 cover the wall and the tile scenery. N4 is
not needed for the 245 counted above; it may be for some of K'na Tha's walls and
scenery and for any other module's missing files.

## Slices

Slice 1 is implemented by ARC-DATA-023 (79 of 80 interiors complete), slice 2 by
ARC-DATA-024 (all 80); ARC-RENDER-019 made the viewer load K'na Tha in about 17 s
instead of about 3 minutes.

1. **ARC-DATA-023 — original art names on whole maps.** In whole-map plans that
   read ART as the original does (`AllSectors` with `AllowArtTrailingBytes` then; in the
   code `_plan.Original` meant the pinned profiles instead; since ARC-DATA-036 every plan
   follows these rules and the flag is gone): the full N1 normalization for classes 2, 12 and 13 (every
   animation, the weapon/shield rules), N2 from `art/missing.dat` with N3's header
   check (the pinned profiles then read it, as a fixed 332-byte file with header
   4,601 that refuses bit 8, until ARC-DATA-036; whole-map plans already bind the mapping) (masks rebuilt from the content index when the header does not match),
   N6 damaged walls, N7 tile-art bodies, N4 `badart.art` frame 0 for a resolved
   name no archive holds, recorded on the visual as a fallback policy. Pinned
   profiles unchanged. Expected: the 25 maps other than K'na Tha publish complete
   snapshots (all 80 with K'na Tha after slice 2), checked by the opt-in
   real-data test and a viewer map-mode smoke of a concealed-monster map and a
   `dgnuw` map.
2. **ARC-DATA-024 — shared object frames (K'na Tha).** Decode, hash and charge
   each distinct object frame selection (entry, group, frame, palette) once per
   build, keeping one visual record per object; then measure K'na Tha and resolve
   its seven hidden issues. A viewer change (ARC-RENDER-019) follows only if map
   mode's own frame and texture budgets refuse the result (they did not; the viewer's
   scene plan was slow on 43,219 objects instead, fixed by ARC-RENDER-019).

## Limits

- Static reading: what each function does, not how often a branch runs in play.
  Whether the game ever draws `badart.art` for installed data is not observed.
- The census is a one-off measurement at `1ec13f5`; the opt-in test of each slice
  pins the result.
- Not checked: the animation timing of concealed critters (they sit on the last
  frame at T0; the run-time conceal fidget is animation work), run-time eye candy.
- Parity of the drawn result with the running game is NOT_VERIFIED.

# Template sector scripts: the plant generator (ARC-RES-073)

Status: **research, docs only**, work item ARC-RES-073, 2026-10-27; **implemented by ARC-WORLD-081** (2026-10-28,
[template sector plants runtime](../world/template-sector-plants-runtime.md), ledger PG11–PG13 added). No `src/`, viewer, session or probe
change by this study. This document answers what the one sector script found in terrain templates does, when the original runs it, what
it creates and where, and how many sectors carry it. It is the input for ARC-WORLD-081 (running the scripts of generated
sectors and the create-object action).

## Where generated sectors stand

Generated sectors of the main map already draw, walk and light in map mode: a window plan builds its sectors from the
terrain templates ([window plans](../world/whole-map-plans.md#window-plans-arc-data-032), ARC-DATA-032; step evidence
ARC-DATA-034; lighting ARC-DATA-035), and the viewer streams the window after the actor (ARC-RENDER-024). On main at
888880ce the map smoke at a generated elven forest sector (`ARC_AREA_MAP_SMOKE_ONLY=map`, `--area-map-sector 1384,239`,
the exported ARC-DEV-015 build) passed with 9 core sectors, 4,401 bodies, 3,539 walls, lighting on and an 8-step walk
over 36,864 admitted cells (2026-10-27).

What generated sectors still lack is their **sector script**: the viewer runs the sector scripts of a map's own sector
files only ([tile and sector scripts](../rendering/area-map-mode.md#tile-and-sector-scripts-arc-script-017)). Of the
template's sector-local sections ([ledger SS5](ledger/world.md#main-map-streaming-arc-data-030): tiles, roofs, block mask, sounds, light scheme, scripts), the script is the
one with gameplay effect.

## Sources and boundary

- **Executable**: the retail 1.0.7.4 `Arcanum.exe` (SHA-256 `9bd80511…8d52`), read with `llvm-objdump` per the
  [original executable check](original-executable-check.md) by this task and, independently, by a fresh reader that saw
  only the questions and addresses (step 6). The verdicts are in the
  [ledger](ledger/scripts.md#template-sector-scripts-arc-res-073), rows PG1–PG10.
- **Data**: the owner's unchanged installation through the content stack in the documented mount order, read with the
  public functions of `tools/research/terrain_template_probe.py` (stack, sector framing, map survey) in a session script
  that is not committed; the counts below are its output. Script lines were read with the project's SCR layout
  (`ScrProgram`, 64-byte header, 132-byte lines) and type names from the [SCR census](scr-census.md). Prototype names come
  from `mes/description.mes` (a prototype's description number is its basic number). No payload, record or listing is
  stored.

## The script

Script 30285 (`scr/30285sectorplantgen.scr`, in `arcanum2.dat` only) has 20 lines. Read with the editor signatures and
the executable's operand readers (PG1–PG7), it does this when it runs:

1. Makes the player (the run's triggerer) the run's attachee (assign object, [OV2](ledger/scripts.md#object-variables-and-calls-arc-script-018)).
2. Takes two prototype numbers from the sector's own counters: **A** = 10000 + counter 2 and **B** = 10000 + counter 0.
   The second add names counter index 2,012,780,696 (editor bytes in the operand); the counter reader shifts by eight
   times the index and the shift keeps only five bits, so it reads counter 0 (PG6).
3. Counts the set bits 0..31 of the sector script's own flags word (local flag, PG4) into **R**; with none set it stops
   and runs the default.
4. Loops over every scenery object in the vicinity of the attachee, now the player (PG3). For each one whose name field
   is 1673 (obj_is_named, [ledger SG3](ledger/scripts.md#guard-conditions-arc-script-014)) it makes R draws: two random numbers 1..100 (PG5); when the first is at most
   counter 3 it creates A, and when the second is at most counter 1 it creates B, each next to that scenery (PG1, PG2).
   Other scenery is skipped.

The script never removes itself and changes none of its own flags or counters, so nothing is stored back (X3 sector) and
it runs again on every entry.

## When it runs

A sector script runs when the local player's move enters another sector, with the player as triggerer and no attachee
([ledger TS2, TS3](ledger/scripts.md#tile-and-sector-scripts-arc-script-017)). The sector notification locks the entered sector and copies its script record; it makes no difference
between an own sector file and a template, and a template sector reads its script section from the template ([SS5](ledger/world.md#main-map-streaming-arc-data-030)). So
every crossing into one of the sectors below runs the generator, and crossing back and forth runs it each time.

## What it creates and where

- **Items, not scenery.** Every prototype the counters name is an item of type 9 ("food" in the prototype file names):
  10061 Ginka Root, 10062 Kadura Stem, 10063 Coca Leaves, 10064 Tobacco Leaves, 10080 Witchbane, 10081 Poppy Flowers,
  10091 Mushrooms and 10125 Passion Root. All but Passion Root are components in `rules/schematic.mes` (Herbology). The
  script's number is the basic prototype number; the executable adds 20 to find the prototype (PG8), the file numbering
  the project already uses (`ContentPrototypes`).
- **The marker.** Each of the 20 templates holds exactly one scenery object whose instance name field is 1673 (its
  prototype's own name class is 1600): a big tree, a tree, a plant or a "wooden object" (prototypes 4026, 4027, 4030,
  4038). No other scenery in any template has an instance name.
- **The tile.** The create action tries up to ten times to find a tile near the marker: a random direction (nine values,
  the ninth meaning "stay") and a random distance of 1..3 tiles; the tile is accepted through a short path check from the
  marker, which gives the marker's own tile when no short path exists. After ten failures it uses the marker's own tile
  (PG1, PG2). The created object is an ordinary object at that location (object create, single-player path); creation
  schedules no Item Decay event ([ledger ID9](ledger/items.md#item-decay-arc-world-062)), so the items stay until
  picked up.
- **Which markers.** The vicinity is a window-sized area centred on the player (PG3), so only markers on screen when the
  player crosses the border are used, and a marker of a neighbouring sector counts as well as the entered sector's. The
  flags and counters are always the entered sector's. At most 100 scenery objects enter the loop list (PG3, PG7); the
  templates hold 72..702 scenery each (forest 433..574 per 4,096 tiles), so a crowded view can leave the marker out.

## Recount

The 20 templates with script 30285 are the four plain variants of deforested, elven forest, forest, plains and tropical
jungle. Their flags, counters and products:

| Template folder (variant file) | R | A (chance) | B (chance) | Expected items per marker and entry |
| --- | --- | --- | --- | --- |
| deforested (all four) | 2 | Mushrooms (10%) | Mushrooms (10%) | 0.4 |
| elven forest 0 | 2 | Witchbane (10%) | Coca Leaves (10%) | 0.4 |
| elven forest 1 | 2 | Witchbane (10%) | Kadura Stem (10%) | 0.4 |
| elven forest 67108864 | 1 | Passion Root (100%) | Witchbane (30%) | 1.3 |
| elven forest 67108865 | 2 | Witchbane (20%) | Coca Leaves (10%) | 0.6 |
| forest 0 | 2 | Kadura Stem (30%) | Ginka Root (30%) | 1.2 |
| forest 1 | 2 | Kadura Stem (30%) | Coca Leaves (20%) | 1.0 |
| forest 67108864 | 2 | Ginka Root (20%) | Coca Leaves (10%) | 0.6 |
| forest 67108865 | 2 | Ginka Root (30%) | Kadura Stem (20%) | 1.0 |
| plains 0 | 2 | Ginka Root (10%) | Poppy Flowers (20%) | 0.6 |
| plains 1 | 3 | Poppy Flowers (20%) | Tobacco Leaves (10%) | 0.9 |
| plains 67108864 | 2 | Ginka Root (10%) | Poppy Flowers (30%) | 0.8 |
| plains 67108865 | 3 | Tobacco Leaves (10%) | Poppy Flowers (20%) | 0.9 |
| tropical jungle 0, 1 | 2 | Coca Leaves (10%) | Coca Leaves (10%) | 0.4 |
| tropical jungle 67108864 | 2 | Coca Leaves (10%) | Coca Leaves (50%) | 1.2 |
| tropical jungle 67108865 | 1 | Coca Leaves (10%) | Coca Leaves (10%) | 0.2 |

Sectors that carry it:

- **Main map** (`arcanum1-024-fixed`): 1,029,964 generated sectors use one of the 20 templates (26% of its 3,954,569
  plain sectors): forest 661,274, elven forest 104,965, plains 179,313, deforested 28,565, tropical jungle 55,847. Twelve
  of its own sector files also attach 30285 with template-like flags and counters (for example (1505, 295) with the elven
  forest 67108864 values, and the cluster around (506, 975) with plains values).
- **K'na Tha**: 548 generated sectors and three own sector files.
- No other map uses these templates or the script.

The 15 own-file copies are among the 19 sector scripts ARC-SCRIPT-017 refuses: the viewer reads them but refuses the
run, because the admission sets lack random, the loops, the vicinity focus kinds, a counter index above 3 and create
object (52).

## What ARC-WORLD-081 needs

ARC-WORLD-081 did all of this ([runtime](../world/template-sector-plants-runtime.md)): the window lists its template sectors' scripts, set
S15 admits the commands, the session keeps the created items (snapshot schema 47), and the owner settled both questions (a fixed reach of
10 tiles, the original's own play view, in both settings; a limit of 6 created items within 3 tiles under Steamwoven; Original keeps the
first 100 objects of the list and no limit).

- Read the sector script record of a generated sector from its template (the catalogue already keeps the section
  sector-local) and run it on entry like an own sector's, with its slot on the sector (the same session keys).
- Admit what the script uses beyond today's sets (which already hold true, equal, less-or-equal, local flag, add,
  goto, return and run default, is named and assign object): random (37), loop for and loop end (18, 19) with the
  focus kinds "every scenery in vicinity" (15, centred on the assigned attachee) and "current looped object" (10), the
  100-object cap, a counter operand with an index above 3 (PG6), and create object (52) with the tile choice above.
- A store for created objects on generated and own sectors that survives window moves, F7/F8 and leaving the map (the
  original keeps them in the sector's saved differences, which the project does not model).
- Decisions to put to the owner: the vicinity size (the original's depends on the game window; the project draws a
  zoomable view), and whether repeated crossings may pile up items without bound as in the original (no self-removal,
  no limit, no decay) or Steamwoven caps them.

## Limits and NOT READY

- Static reading only (F2 where both readers agree); how often a marker is on screen at a crossing in play is not
  observed.
- Not read: the order of the vicinity list (which 100 objects are kept; the window size was read by ARC-WORLD-081: the fixed 800 × 400 play view, PG13), the path
  search's own rules, and how created objects are saved and reloaded for a template sector beyond [SS2 and SS5](ledger/world.md#main-map-streaming-arc-data-030).
- Since ARC-WORLD-081 the viewer runs template sector scripts and creates their items; this study's own text above is unchanged.

# Main-map streaming

Status: **research**, work item ARC-DATA-030 (audit item U5, step 1), 2026-10-18. No
code changes. This document answers how the original 1.0.7.4 executable keeps the
sectors of a map in memory while the game runs. That covers which sectors are
resident around the PC, when a sector is loaded and dropped, how a sector without its
own file is built, and how mobile objects, `map.patch`, lighting and a saved game's
differences reach a sector. It is the input for the sector-windowed plan of the Data
layer (ARC-DATA-031, ARC-DATA-032).

Earlier facts reused here: [generated wilderness](generated-wilderness-evidence.md)
(ARC-RES-034: `terrain.tdf`, the template path rule, template content),
[map patch](../world/map-patch.md) (ARC-DATA-028), [whole-map plans](../world/whole-map-plans.md),
[map catalogue](../world/map-catalog.md).

## Method

Behaviour comes from the retail executable (`Arcanum.exe` 1.0.7.4, SHA-256
`9bd80511…78d52`), read statically with `llvm-objdump` by a first reader and,
independently, by a second reader that saw only the questions
([method step 6](original-executable-check.md)). The rows and verdicts are in the
[ledger](original-verification-ledger.md#main-map-streaming-arc-data-030). Local observations
come from the read-only `tools/research/terrain_template_probe.py` helpers on the owner's
installation and are counts and coordinates only.

## How the game keeps sectors

The original has no fixed "window" of sectors. Every system that needs a sector locks it
by id; the lock loads the sector on demand into a small cache and keeps it until the
cache needs the room.

- **Cache (SS1).** Sector initialisation asks for a cache of 16 sectors; the cache
  clamps any request to 8..128, and nothing else resizes it.
- **Lock (SS2).** A lock fails while another lock is being taken, and fails (with a
  log line) when no map is open. It refuses a sector outside the map: x and y must lie in
  `0 .. size / 64`, where the size is the map's tile width and height from `map.prp`,
  set when the map opens (`terrain.tdf` is not consulted for the bound). A hit counts a
  reference and refreshes the entry's age. On a miss with the cache full, the oldest
  entry without references is saved (the game's sector save, which writes the
  sector's differences) and evicted; when every entry is referenced the lock fails with
  a warning. Locks are short (lock, read, unlock), so the resident set is the 16 most
  recently used sectors.
- **History (SS3).** An evicted sector's id and the game time are kept in a history of
  twice the cache size (32 entries, oldest dropped first). A sector found there when it
  is loaded again carries that time, which the object fold uses (SS7).
- **Pre-cache (SS4).** A pre-cache locks and unlocks the 3×3 sectors around a location:
  the sector holding it and its eight neighbours (a neighbour outside the map is
  refused by the lock). A teleport of the local PC runs it at the target, after opening
  the target map when the map changes; in a network game it also runs for each player and
  on a message naming the local PC. Opening a map has a pre-cache flag of its own, but
  both of its callers pass "no", so arriving on a map otherwise pre-caches nothing.
  Rendering and the other systems then lock what they touch.

## How a sector is built

- **Sources (SS5).** The game sector loader tries, in order:
  1. the map's own `<map folder>\<sector id>.sec`;
  2. otherwise the terrain template path of the sector's terrain word
     ([path rule](generated-wilderness-evidence.md#3-terrain-word-and-template-path));
  3. otherwise fill: one tile family, no other content.
  
  Outside the optional demo limits the own file is not tried. A template sector is read
  like an own file, but the loaders of lights and objects get the real sector id so that
  they move their records; an own sector passes "no id".
- **Saved differences (SS5).** For an own or a template sector, the loader looks for
  `<save folder>\<sector id>.dif` under the **real** id and applies it section by
  section. A filled sector reads nothing.
- **Order (SS5).** Lights, tiles, roofs, the placeholder word, tile scripts, sector
  scripts, town map, aptitude adjustment, light scheme, sounds, block mask, objects;
  each section takes its `.dif` part when the flag word names it.
- **Relocation (SS6).** A template object's location becomes the real sector's origin
  plus the object's tile inside its own template slot. Lights move by the same rule
  (ledger row Wilderness A1, ARC-RES-048).
- **Fold (SS7).** After loading, every mobile object standing in the sector that is not
  in an inventory and not static joins the sector's object list. An NPC among them (not
  destroyed) gets an immediate AI time event whose flag says whether at least 3,000 ms
  of game time passed since the sector was last dropped (also true for a sector not in
  the history). The sector's lights are then
  folded (SS8), and a sector with town-map data pre-caches its art.
- **Lights (SS8).** The light fold handles nocturnal scenery (scenery flag 0x4, not in an
  inventory) by the tile it stands on: on a tile whose art type bit is clear it is
  switched on; otherwise it is switched off by day (hour 6..17) and on by night. Every
  other object, and nocturnal scenery left on, has its lights added to the sector.

## Where the other map data comes from

| Data | When | Scope |
| --- | --- | --- |
| Mobile objects (critters, items, portals, containers…) | map open, from the map's mobile file and the save | whole map; a sector only lists those standing in it (SS7) |
| `Rules\map.patch` | map open, after the mobiles ([MP1](original-verification-ledger.md#map-patch-arc-data-028)) | whole map, mobile objects only |
| Sector lights, light scheme word | sector load | per sector, moved for templates |
| Map default light scheme | map open (`mapinfo.txt`) | whole map |
| Saved differences | sector load (`.dif`) and save at eviction | per sector, own and template |

## Local observation: Crash Site and the main map

The main map `Arcanum1-024-fixed` has 621 own sector files in clusters. The largest
8-connected clusters measure 64, 59, 37, 31, 26, 22, 21 and 19 sectors; the third holds
the Crash Site (x 1450..1454, y 1288..1297).

Around the Crash Site centre (1452, 1290), the 3×3 is all own files, and so is the pinned
exterior profile's core. Of the 16 sectors of the ring at distance 2, 11 are own files.
The other five are generated: (1452, 1288) and (1454, 1288) from the plain mountains
template variant 1, (1453, 1288) from variant 3, (1450, 1290) from variant 2, and
(1450, 1291) from the forest-to-mountains transition (slot file 3). These are exactly the
five context sectors the pinned profile records as absent.

## What this means for the Data layer

- A **window** of 3×3 sectors around a centre sector is the set the original loads around
  the PC when it teleports (SS4), and the pinned Crash Site profile's core. A ring around
  it matches the pinned profile's context. The plan's limits follow the window (at most
  25 sectors), not the map's 621 files.
- Each window sector resolves own file → template → fill. Retail data never reaches
  fill. Fill's random tile variation needs a policy, so a plan should refuse it as a
  typed result rather than invent tiles.
- Mobile objects are read from the map-wide aggregate and kept when they stand in a
  window sector. Templates hold only walls and scenery without object ids, so a
  generated sector adds no mobile.
- `map.patch` stays map-wide (as in whole-map plans). Lighting reads each window
  sector's own light list and scheme word, moved for templates.
- Saved differences are not modelled (no saved game), the same boundary as whole-map
  plans.
- The NPC AI event on load (SS7) and the eviction save are run-time behaviour of a
  session host, not Data.

## Not checked

- The rectangle of sectors the renderer locks each frame (bounded by three per axis by
  its structure), and the sectors other systems lock (lights by radius, objects,
  scripts).
- How the `.dif` sections are written, and the sector blocking file read at map open.
- The fill's tile variation.

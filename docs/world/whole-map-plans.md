# Whole-map plans for the area owner

## Scope

ARC-DATA-019, step 2 of the "any interior" stage of the whole-world plan. It
lets the existing `AreaDataOwner` (the source of the viewer, the step model and
the lighting) load any map of the [universal catalogue](map-catalog.md) under a
structural plan built at run time, instead of only the two pinned profiles. The
pinned Crash Site exterior and cave profiles are unchanged and still pass their
original-data oracles. No viewer, launcher or session file changes here.

## API

`AreaMapPlans.Create(stack, catalog, mapName)` returns an `AreaMapPlan` or a
typed `AreaMapPlanStatus` (unknown map, no sectors, more than 256 sector files,
missing or unsupported source, unreadable input; I/O and archive errors are
reported, not thrown): the structural `AreaSourcePlan`, the input files and
`AreaLoaderOptions` sized for the map. `AreaMapPlan.Open()` opens the inputs and
the owner; the owner authenticates every input against the plan as before. The
snapshot's `Profile` is `arc-data-019-whole-map-original-rules-v1` (or
`arc-data-019-whole-map-v1` without the ART rules).

- **Inputs:** the module archive, `arcanum1.dat`, `arcanum2.dat`, the map's
  mobile aggregate and every prototype the map's objects name (from the
  catalogue's own reads), each hashed when the plan is made. No hash is pinned.
  A sector file overridden outside the module archive is refused, because the
  owner reads sectors from the module archive.
- **Mappings:** the ten MES/policy files the pinned profiles bind, located by
  name in the two base archives.
- **Map numbers:** from `rules/maplist.mes`, read from 5000 up to the first
  missing number (at most 200), as the original does; runtime = catalogue − 4999;
  0/0 for an unlisted map or a list that does not parse.
- **Start:** `AreaMapPlan.StartTile` is the map-list arrival tile (the entry's
  second and third values); the plan's start sector is its sector when that has
  a file, else the first sector file. (`startloc.txt` holds the same pair for
  every installed map and is not used.)
- **Limits:** count limits follow the map exactly; memory budgets are the
  exterior profile's (20 present sectors, 524 objects) scaled by the map's
  sectors or objects, whichever grows more, at most 16×, and frame budgets are
  that 16× cap outright (interiors read larger ART per visual than the
  exterior), so an oversized map fails with a budget error instead of exhausting
  memory. A whole map with the original's ART rules reads archive entries
  (sectors, mappings, ART) up to 16 MiB (1 MiB for pinned profiles), with a
  32 MiB frame-operation budget. Other caps stay as they are: 1 MiB per object
  record and per prototype, and the strict ART frame limits (4 Mi pixels,
  16 MiB RGBA, 256 KiB frame table).

## Rules of a whole-map plan

`AreaSourcePlan` gains two init options; pinned profiles ignore both.

- `Selection = AreaSectorSelection.AllSectors`: every sector file under the
  map's prefix is core and there is no context ring (sectors without a file are
  the map's edge). The default `TownmapComponent` keeps the exterior rule.
- `AllowArtTrailingBytes = true`: ART is read as the original reads it. Bytes
  after the last frame are accepted ([ARC-DATA-016](../formats/art.md)), and a
  single-rotation ART is drawn from its only group whatever rotation the object
  asks for (pinned CE `first_party/tig/src/art.c`, `TIG_ART_0x01`).

A whole-map plan also:

- reads tile names as the original does: a name ends at `/`, otherwise it is the
  first three characters of the value and the rest is a sound number (pinned CE
  `a_name.c` `load_tile_names`); `TileNames.Parse(…, originalNames: true)`;
- looks a resource up in whichever of the two base archives holds it (one
  holder required), not only in the archive of its mapping;
- keeps a sector's roof list in its record (`RoofDiscriminator`) without
  failing the map: roofs are not drawn yet;
- draws doors and windows (ARC-DATA-020): with the `Portal` mapping
  (`art/portal/portal.mes`) a type-1 object's ART is the entry's first word for
  its number (+1001 for a window), the busted bit turns the name's
  sixth-from-last letter into `D`, the only group is drawn (the ART must hold
  a single rotation) and flag bit 0 mirrors it; flag bits 1–3 and the unused
  bits 6–8 are refused
  ([portal facts](../research/first-building-evidence.md#portals-doors-and-windows-fr-scene-008));
- draws scrolls, keys and key rings (types 10–12) like every other item;
- resolves no body, whatever the object's type, when its serialized state hides
  it at T0 (destroyed 0x1, off 0x2 or don't-draw 0x100; every installed trap is
  don't-draw, and about a hundred other objects, mostly NPCs, are hidden too),
  so such an object needs no ART; the viewer draws no such body anyway. The
  original still draws a don't-draw object's overlays, so their admission check
  still applies to it; a destroyed or off object is not checked;
- names art as the original does (ARC-DATA-023, rules N1–N7 of the
  [art gaps study](../research/whole-map-art-gaps.md)) when it reads ART with the
  original's rules (`AllowArtTrailingBytes`):
  - critter, monster and unique NPC art (classes 2, 12, 13) in any of the 25
    animations, with the original's body rules (an elf female drawn as human; plate
    armour male only, elf as human, halfling as dwarf), the missing-art repair from
    `art/missing.dat` applied first (each checked animation/weapon pair whose file
    is marked missing becomes walk, stand, attack or fall down; a stand with no
    weapon becomes unarmed), then the weapon and shield rules (explode clears
    armour, shield and weapon and is named `XX`; stunned is unarmed without a
    shield; stealth walk and conceal fidget drop an unarmed weapon; run to the
    bloody deaths drop weapon and shield; walk drops the shield). The table is used
    when its header equals the number of critter, monster and unique NPC ART files
    in the two resource archives and it holds all 10 + monster + unique NPC masks;
    otherwise the masks are rebuilt by checking each pair's file, as the original
    rebuilds a stale table. Animations above 24, critter armour above 8, weapons
    above 14 and critter bodies above 4 stay refused (no installed object has them;
    the original would draw bad art for some of them);
  - damaged walls (bits `0x400`/`0x80`, swapped at rotations 2, 3, 6, 7): `0x400`
    names the `L` file (piece 7 as the base piece), `0x80` names `L` for pieces 1–6
    and `R` otherwise (piece 8 as the base piece);
  - a tile id on an object names the tile file as a terrain cell does (no rotation
    or frame, palette from bits 4–5, mirrored when flippable with flag bit 0);
  - an object's resolved name that neither resource archive holds is drawn from
    `art/badart.art`, frame 0 and its only rotation, as the original's art cache
    does; the visual's policy is `original-badart-v1`. A name the mapping cannot
    build (no list entry) and an ambiguous name stay refused. Terrain and facade
    cells keep their strict resolution (the original's cache would draw bad art for
    them too; no cell of the 79 complete interiors needs it).

## Result on the installed original

Opt-in `AreaMapPlansRealDataTests` (`DAT_ROOT`): all 80 interiors get a plan,
open and inspect without an exception; 79 publish a complete snapshot and are
pinned by the test (a map that starts publishing joins the list): all but K'na Tha
since ARC-DATA-023 (54 before), and none of them draws bad art. The Crash Site
cave under the generic plan reaches the pinned profile's counts (4,096 terrain
cells, 138 statics, 39 mobiles, 194 visuals) without its per-object exceptions.

What stopped the other 26 is measured map by map in the
[whole-map art gaps study](../research/whole-map-art-gaps.md) (ARC-RES-050): 132
concealed monsters, 111 critters without standing art, a damaged wall and a
tile-art scenery object, all drawn since ARC-DATA-023, and K'na Tha. K'na Tha still
stops at the cumulative encoded-frame budget after 28,091 frame requests (15,374
objects refused; 260 distinct art ids among its 43,219 objects); decoding each
distinct frame once is ARC-DATA-024. No eye-candy overlay is active at T0 in any
interior.

Before doors, windows, small items and hidden objects were admitted (ARC-DATA-020)
19 maps were complete; those added 2,568 objects of the types the owner did not
draw.

## Limits

The viewer's [map mode](../rendering/area-map-mode.md) (ARC-RENDER-017) draws these
maps and places a door or window on the wall hot-spot rule (mirror and rotation
shift, see the portal facts); five complete maps still stop at blend states the
scene plan does not draw yet.

Resources resolve only in `arcanum1.dat` and `arcanum2.dat` (their overrides in
`arcanum3/4.dat`, patches and loose folders are not seen), and the aggregate and
prototypes must be loose files; ARC-DATA-018 moves resolution to stack order.
The main map is not covered (its sectors are generated from the terrain grid,
ARC-RES-034). Original-executable parity is NOT_VERIFIED.

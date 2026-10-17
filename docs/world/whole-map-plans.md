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
  still applies to it; a destroyed or off object is not checked.

## Result on the installed original

Opt-in `AreaMapPlansRealDataTests` (`DAT_ROOT`): all 80 interiors get a plan,
open and inspect without an exception; 54 publish a complete snapshot and are
pinned by the test (a map that starts publishing joins the list). The Crash Site
cave under the generic plan reaches the pinned profile's counts (4,096 terrain
cells, 138 statics, 39 mobiles, 194 visuals) without its per-object exceptions.

What stops the other 26 is measured map by map in the
[whole-map art gaps study](../research/whole-map-art-gaps.md) (ARC-RES-050, census at
`1ec13f5`, not asserted by tests): 245 object bodies outside K'na Tha and the frame
budget of K'na Tha. No eye-candy overlay is active at T0 in any interior.

| Cause | Objects | Next |
| --- | ---: | --- |
| Concealed monsters (animation 5, art present) | 132 | the original's name rules (ARC-DATA-023) |
| Unique NPC `dgnuw` and the `AIR` monster standing with no weapon without that file | 111 | the original's missing-art repair (ARC-DATA-023) |
| A damaged wall; a scenery object with tile art | 2 | damaged wall names, tile-art bodies (ARC-DATA-023) |
| K'na Tha: encoded-frame budget after 28,091 frame requests (260 distinct art ids among 43,219 objects) | 15,367 + 7 | decode each distinct frame once (ARC-DATA-024) |

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

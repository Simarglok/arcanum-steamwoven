# Whole-map plans for the area owner

## Scope

ARC-DATA-019, step 2 of the "any interior" stage of the whole-world plan. It
lets the existing `AreaDataOwner` (the source of the viewer, the step model and
the lighting) load any map of the [universal catalogue](map-catalog.md) under a
structural plan built at run time, instead of only the two pinned profiles. The
pinned Crash Site exterior and cave profiles are unchanged and still pass their
original-data oracles. No viewer, launcher or session file changes here.

## API

`AreaMapPlans.Create(stack, catalog, mapName)` returns an `AreaMapPlan` (or a
typed `AreaMapPlanStatus`): the structural `AreaSourcePlan`, the input files and
`AreaLoaderOptions` sized for the map. `AreaMapPlan.Open()` opens the inputs and
the owner; the owner authenticates every input against the plan as before.

- **Inputs:** the module archive, `arcanum1.dat`, `arcanum2.dat`, the map's
  mobile aggregate and every prototype the map's objects name (from the
  catalogue's own reads), each hashed when the plan is made. No hash is pinned.
- **Mappings:** the ten MES/policy files the pinned profiles bind, located by
  name in the two base archives.
- **Map numbers:** from `rules/maplist.mes` (catalogue number from 5000,
  runtime = catalogue − 4999); 0/0 for an unlisted map.
- **Start:** the sector of `startloc.txt` when it has a file, else the first one.
- **Limits:** the exterior profile's limits scaled by the map's size (sectors,
  objects, prototypes, aggregate).

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
  failing the map: roofs are not drawn yet.

## Result on the installed original

Opt-in `AreaMapPlansRealDataTests` (`DAT_ROOT`): all 80 interiors get a plan,
open and inspect without an exception; 19 publish a complete snapshot and are
pinned by the test (a map that starts publishing joins the list). The Crash Site
cave under the generic plan reaches the pinned profile's counts (4,096 terrain
cells, 138 statics, 39 mobiles, 194 visuals) without its per-object exceptions.

What stops the other 61, measured once (counts of issues, not maps):

| Cause | Issues | Next |
| --- | ---: | --- |
| Object types the owner does not draw yet (portals, keys, keyrings, scrolls, traps, PC) | 2,568 | generalised object model |
| ART path of an object not found in `arcanum1/2.dat` | 115 | stack-order resources (ARC-DATA-018) |
| Unsupported art states (eye-candy overlays, critter poses beyond the admitted set) | 138 | generalised object model |
| An entry over the 1 MiB read cap | 31 | raise the cap for whole maps |

## Limits

Resources resolve only in `arcanum1.dat` and `arcanum2.dat` (their overrides in
`arcanum3/4.dat`, patches and loose folders are not seen), and the aggregate and
prototypes must be loose files; ARC-DATA-018 moves resolution to stack order.
The main map is not covered (its sectors are generated from the terrain grid,
ARC-RES-034). Original-executable parity is NOT_VERIFIED.

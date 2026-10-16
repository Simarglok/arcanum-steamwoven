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
- **Start:** the sector of `startloc.txt` when it has a file, else the first one.
- **Limits:** count limits follow the map exactly; byte budgets are the exterior
  profile's (20 present sectors, 524 objects) scaled by the map's sectors or
  objects, whichever grows more, at most 16×, so an oversized map fails with a
  budget error instead of exhausting memory.

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
  sixth-from-last letter into `D`, the only group is drawn and flag bit 0
  mirrors it; other flag bits are refused
  ([portal facts](../research/first-building-evidence.md#portals-doors-and-windows-fr-scene-008));
- draws scrolls, keys and key rings (types 10–12) like every other item;
- resolves no body for an object its serialized state hides at T0 (off,
  destroyed, invisible), so such an object needs no ART; every trap is one.
  The viewer draws no such body anyway.

## Result on the installed original

Opt-in `AreaMapPlansRealDataTests` (`DAT_ROOT`): all 80 interiors get a plan,
open and inspect without an exception; 52 publish a complete snapshot and are
pinned by the test (a map that starts publishing joins the list). The Crash Site
cave under the generic plan reaches the pinned profile's counts (4,096 terrain
cells, 138 statics, 39 mobiles, 194 visuals) without its per-object exceptions.

What stops the other 28 (counts of issues, not maps; a one-off measurement
with a scratch runner over `AreaMapPlans` on the installed original at the PR
head, not asserted by tests):

| Cause | Issues | Next |
| --- | ---: | --- |
| Unsupported art states (eye-candy overlays, critter poses beyond the admitted set) | 138 | overlays and poses |
| ART path of an object not found in `arcanum1/2.dat` | 114 | stack-order resources (ARC-DATA-018) |
| Read budget: entries over the 1 MiB cap and the capped byte budgets of the largest maps (K'na Tha, Void) | 16,588 | raise the entry cap; stream large maps |

Before doors, windows, small items and hidden objects were admitted (ARC-DATA-020)
19 maps were complete; those added 2,568 objects of the types the owner did not
draw.

## Limits

The viewer still places a door or window with the hot spot of an ordinary body;
the wall rule (mirror and rotation shift, see the portal facts) applies to
portals too and lands with the viewer map mode (ARC-RENDER-017).

Resources resolve only in `arcanum1.dat` and `arcanum2.dat` (their overrides in
`arcanum3/4.dat`, patches and loose folders are not seen), and the aggregate and
prototypes must be loose files; ARC-DATA-018 moves resolution to stack order.
The main map is not covered (its sectors are generated from the terrain grid,
ARC-RES-034). Original-executable parity is NOT_VERIFIED.

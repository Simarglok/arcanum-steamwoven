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
snapshot's `Profile` is `arc-data-019-whole-map-original-rules-v1`.

- **Inputs:** the module archive, every resource source of the content stack
  (see [stack order](#resources-in-content-stack-order) below), the map's mobile
  aggregate and every prototype the map's objects name (from the catalogue's own
  reads), each hashed when the plan is made. No hash is pinned. The aggregate is
  read from whichever stack source wins it, a prototype from the base source the
  original reads it from (`WorldCatalog.Prototypes`, ARC-DATA-026: a module copy
  is never read, a prototype only a module holds is `MissingSource`): a loose file is
  opened as itself, an archive member is read into memory when the plan opens
  (`AreaMapPlanInput.Member` is its entry ordinal; at most 1 MiB for a prototype,
  64 MiB for the aggregate) and the owner authenticates those bytes against the
  plan. A sector or editor mobile (`.mob`) file overridden outside the module
  archive is refused, because the owner reads both from the module archive.
- **Map patch:** the stack's winner of `rules/map.patch` (read when a map opens)
  is an input (`AreaSourceRole.MapPatch`), with the headers of the scripts its
  lines attach; the owner applies its lines to the map's mobile objects before
  building them ([map patch](map-patch.md), ARC-DATA-028).
- **Mappings:** the ten MES/policy files the pinned profiles bind. The original
  reads all of them in Name's init, before the module is mounted (ARC-DATA-027,
  exe-checked), so each is the first base source's copy: a module copy is never
  read, and a base copy that is not an input of the plan (a loose base file a
  module copy shadows, or an archive outside the resource order) is
  `UnsupportedSource`. The owner parses them in the original MES grammar
  (`MesFile`). `art/missing.dat` is optional: without it the
  owner rebuilds the masks, as the original does (N3).
- **Roof tables** (ARC-RENDER-021): `art/roof/roofname.mes` (read with the
  names) and `art/roof/roofshade.mes` (read by the roofs' own init), optional,
  resolved in the same game-start phase; that phase is not checked in the
  executable for these two tables.
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
  memory. A plan reads archive entries (sectors, mappings, ART) up to 16 MiB (every
  plan since ARC-DATA-036, pinned profiles too), and a whole map has a
  32 MiB frame-operation budget. Other caps stay as they are: 1 MiB per object
  record and per prototype, and the strict ART frame limits (4 Mi pixels,
  16 MiB RGBA, 256 KiB frame table).

## Rules of every plan

`Selection = AreaSectorSelection.AllSectors` makes every sector file under the
map's prefix core, with no context ring (sectors without a file are the map's
edge). The default `TownmapComponent` keeps the exterior rule. The selection
chooses sectors only: since ARC-DATA-036 every plan, the pinned profiles too, is
built by the rules below ([one rule set](#one-rule-set-arc-data-036)).

Every plan:

- reads ART as the original reads it: bytes after the last frame are no part of
  the ART ([ARC-DATA-016](../formats/art.md)), so the frame is read from the ART's
  own extent (the visual's `ViewLength` is the entry's decoded bytes less the
  trailing bytes; policy `original-art-extent-v1`), and a single-rotation ART is
  drawn from its only group whatever rotation the object asks for (the original's
  TIG ART reader, ART header flag `0x01`; policy `original-single-group-rotation-v1`).
  Before ARC-DATA-036 a whole map read such an ART whole (`ViewLength` the full
  entry); the pixels are the same, but an HD pack built from map-mode frames before
  it is keyed by the old view and misses those few frames, which draw Legacy
  ([HD pack key](../formats/hd-pack.md#sprite-entries-the-portable-frame-key));

- reads tile names as the original does: a name ends at `/`, otherwise it is the
  first three characters of the value and the rest is a sound number (the tile name
  load, 0x4EB2E0, F0); `TileNames.Parse(…, originalNames: true)`;
- looks a resource up in content-stack order (ARC-DATA-018, below), not in the
  archive of its mapping;
- keeps a sector's roof list in its record (`RoofDiscriminator`) without
  failing the map, and since ARC-RENDER-021 reads the roof blocks of every core
  sector, with the `Roof` (`art/roof/roofname.mes`) and `RoofShade`
  (`art/roof/roofshade.mes`) mappings, into `AreaSnapshot.Roofs` and roof
  visuals ([roofs](../rendering/roofs.md));
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
  [art gaps study](../research/whole-map-art-gaps.md)):
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
    the content stack holds and it holds all 10 + monster + unique NPC masks;
    otherwise the masks are rebuilt by checking each pair's file, as the original
    rebuilds a stale table. Animations above 24, critter armour above 8, weapons
    above 14 and critter bodies above 4 stay refused (no installed object has them;
    the original would draw bad art for some of them);
  - damaged walls (bits `0x400`/`0x80`, swapped at rotations 2, 3, 6, 7): `0x400`
    names the `L` file (piece 7 as the base piece), `0x80` names `L` for pieces 1–6
    and `R` otherwise (piece 8 as the base piece);
  - a tile id on an object names the tile file as a terrain cell does (no rotation
    or frame, palette from bits 4–5, mirrored when flippable with flag bit 0);
  - an object's resolved name that no resource source holds is drawn from
    `art/badart.art`, frame 0 and its only rotation, as the original's art cache
    does; the visual's policy is `original-badart-v1`. A name the mapping cannot
    build (no list entry) and an ambiguous name stay refused. Terrain and facade
    cells keep their strict resolution (the original's cache would draw bad art for
    them too; no installed cell needs it);
- decodes each distinct frame selection (entry, group, frame, palette, fallback policy
  and whether the art class is a wall or portal) once
  per build and copies its frame facts (size, hot spot, offsets, payload and pixel
  hashes, policy) into every later visual with that selection, each keeping its own
  key, raw and lookup art, facing, mirror flag, offsets and scale (ARC-DATA-024).
  Budget admissions and decodes are therefore per distinct selection, as terrain
  selections already were.

## Resources in content-stack order

ARC-DATA-018. A whole-map plan reads every resource as the original's file
system opens it: from the first source in [search order](../research/content-precedence.md)
that holds the name (module folder, module patches, module archive, `data/`,
`Arcanum4.dat`, `arcanum3.dat`, `arcanum2.dat`, `arcanum1.dat`, game folder,
`tig.dat`), so an override in a later archive, a patch or a loose folder is
seen, and a name two base archives hold is no longer ambiguous. A pinned profile
lists its own two base archives in that order (`arcanum2.dat`, then `arcanum1.dat`;
ARC-DATA-036); a caller-authored plan without an order reads a name from the one of
the two base archives that holds it and refuses a name both hold.

- `AreaSourcePlan.ResourceOrder` lists the resource sources top first
  (`AreaResourceSource`: an input key, plus the virtual path for a loose file).
  The plan lists each archive that wins at least one name under `art/` and each
  loose file a folder wins there; every name's winner is then a listed source,
  so the first listed holder is the stack's winner and the count of critter ART
  files (the `art/missing.dat` header check) is the stack's count.
- Input keys: the module archive stays `Module` and the two base archives keep
  `Resource1` (`arcanum1.dat`) and `Resource2` (`arcanum2.dat`), which the viewer
  also reads outside the owner; any other archive is `Archive` numbered by its
  search-order position, and each loose file is `Loose` numbered from 1. A
  source that wins nothing under `art/` is no input. The key names an input;
  resolution follows the order alone.
- Within an archive, a directory record (`0x400`) or an ignored member
  (`0x800`) holds no file, as in the content stack; an archive holding a name
  twice is refused as ambiguous (the content stack refuses such an index).
- A loose input is read whole and authenticated like every other input. Its
  entry identity has ordinal, flags and offset 0, its length as stored and
  decoded size, and the SHA-256 of its virtual path as the name digest. At most
  1,024 loose files under `art/` (`AreaMapPlans.MaxLooseResources`), each at most
  16 MiB, else the plan is `TooLarge`; `AreaLoaderOptions.MaxResourceInputs`
  bounds the `Archive` and `Loose` inputs an owner admits (0 by default, so no
  caller plan gains inputs it did not size).
- The owner refuses a plan whose order omits an `Archive` or `Loose` input. Since
  ARC-DATA-036 a pinned profile has an order too (its two base archives). A plan
  without an order keeps the earlier rule (one holder among the two base archives).
- Each extra archive adds its index to the owner's retained budget, which
  `Limits` does not scale for it; an installation with large patch archives can
  fail with a typed `BudgetExceeded`. The snapshot's `Profile` is the same with
  or without an order; its `Inputs` list shows which sources were read.
- An archive member input (a prototype or aggregate) that cannot be read again
  when the plan opens is a typed `AreaException` (`MalformedRecord` for a
  damaged archive, `MissingSource` for an I/O failure); one that changed is
  `InputIdentityMismatch`. A loose name under `art/` that is no valid virtual
  path (a backslash in a file name) cannot be resolved and is left out.
- On the installed game the order is `Arcanum4.dat`, `arcanum2.dat`,
  `arcanum1.dat`, `tig.dat` (the others hold nothing under `art/`; no loose
  file is there), and all 80 interiors publish the same visuals, objects and
  counts as before.

## Result on the installed original

Opt-in `AreaMapPlansRealDataTests` (`DAT_ROOT`): all 80 interiors get a plan,
open and inspect without an exception and all 80 publish a complete snapshot,
pinned by the test: 54 until ARC-DATA-023, 79 (all but K'na Tha) until ARC-DATA-024.
None of them draws bad art. The Crash Site
cave under the generic plan reaches the pinned profile's counts (4,096 terrain
cells, 138 statics, 39 mobiles, 194 visuals) without its per-object exceptions.

What stopped the other 26 is measured map by map in the
[whole-map art gaps study](../research/whole-map-art-gaps.md) (ARC-RES-050): 132
concealed monsters, 111 critters without standing art, a damaged wall and a
tile-art scenery object, all drawn since ARC-DATA-023, and K'na Tha, which stopped
at the cumulative encoded-frame budget after 28,091 frame requests (15,374 objects
refused; 260 distinct art ids among its 43,219 objects). With shared frame
selections (ARC-DATA-024) K'na Tha needs 477 frame decodes (488 since ARC-WORLD-051 decodes every frame of its door ART) and inspects in about
8 s (Release, scratch runner). No eye-candy overlay is active at T0 in any
interior.

Before doors, windows, small items and hidden objects were admitted (ARC-DATA-020)
19 maps were complete; those added 2,568 objects of the types the owner did not
draw.

## Window plans (ARC-DATA-032)

`AreaMapPlans.CreateWindow(stack, catalog, map, centre, radius = 1)` plans the sectors around one sector of any map, the
main world map included, through the same area owner. It is how the main map loads through the universal path.

- **Selection.** The core is the `(2·radius + 1)²` sectors around the centre (radius 1, the default, is the 3×3 the original
  loads around the PC when it teleports, [ledger SS4](../research/original-verification-ledger.md#main-map-streaming-arc-data-030)),
  and one ring of context sectors surrounds it; radius at most `MaxWindowRadius` (2). Sectors outside the map's `map.prp`
  size are left out (the original never loads them); a centre outside it is `OutsideMap`.
- **Sources.** Each sector is resolved by the map catalogue as the original takes it
  ([generated sectors](map-catalog.md#generated-sectors-arc-data-031), ledger SS5): the map's own file is read from the
  module archive, as for a whole map; a sector without one is generated from its terrain template, which becomes an
  authenticated `AreaSourceRole.TerrainTemplate` input (one per distinct template, read from the stack's winner). A
  sector the original would fill, or cannot resolve, refuses the plan (`UnsupportedSource`); retail data has none.
- **Owner.** `AreaSourcePlan.Selection = Window` with `AreaSourcePlan.Window` listing each sector, its scope and its
  template (input and virtual path, ARC-DATA-034; a template without its path, a path without a template, or a path
  that is not a lower-case virtual path under `terrain/` is `InvalidSelection`). The owner surveys only the window's own sector files (all must be in the module archive, else
  `MissingSource` at selection), reads a generated sector from its template input like a sector file, places its
  objects at the sector's origin plus their tile (ledger SS6) and moves its lights (words 2 and 3, Wilderness A1).
  `AreaSectorRecord.Template` names the template input of a generated sector, which has no module ordinal or entry.
  It refuses an inconsistent window (no core sector, a repeated sector, a template that is no input or an input no
  sector uses, a window on another selection or a pinned profile, a missing sector list or entry) with
  `InvalidSelection`, and more window sectors than `AreaLoaderOptions.MaxPresentSectors` or more templates than
  `MaxTerrainTemplates` with `BudgetExceeded`, before building anything. Two module entries of one window sector are
  `DuplicateIdentity`. Profile `arc-data-032-window-original-rules-v1`. A generated object keeps the template's
  serialized location; only its world position moves. Retail templates hold no object ids; a template object with one,
  serving two sectors, would be refused as a duplicate identity (temporary ids are not modelled).
- **Rules.** A window follows the rules of every plan (original ART names and leniency, portals, small items, hidden
  objects, roofs read; ARC-DATA-036).
- **Start.** The plan's `Start` is the window's centre; `AreaMapPlan.StartTile` stays the map list's start tile, which is
  usually outside the window. The reconciliation's survey count is the window's own sector files.
- **Objects.** The owner reads the map-wide mobile file (the original loads mobiles map-wide when the map opens, ledger
  SS7) and keeps the objects standing in the window's sectors; `map.patch`, prototypes, name tables and resources are
  bound as for a whole map.
- **Limits.** Derived from the window, not the map: survey and present sectors are the window's (25 for radius 1),
  objects and prototypes those of its sectors and the mobiles standing in them, one template input per distinct
  template; the aggregate limits follow the whole mobile file, which the owner reads.

Installed original (opt-in `AreaWindowRealDataTests`):

| Window | Present sectors | Generated | Objects | Core mobiles | Visuals |
| --- | ---: | ---: | ---: | ---: | ---: |
| Crash Site (1452, 1290) | 25 | 5 | 524 | 88 | 2,444 |
| Town, the largest own cluster (1409, 1314) | 25 | 0 | 4,660 | 387 | 5,092 |
| Elven forest wilderness (1384, 239) | 25 | 25 | 10,532 | 0 | 5,414 |
| Generated centre beside the Crash Site (1450, 1290) | 25 | 13 | 2,173 | 8 | 1,950 |

The Crash Site window reproduces the pinned exterior profile's core: the same nine core sectors and payloads, every one of
the 36,864 core terrain cells, the 426 core statics and 88 core mobiles by record and place, the eleven context sectors it
has files for, the same 524 objects and the same 2,444 visuals by RGBA digest. The pinned profile records its five other
context sectors as absent; the window generates them (four mountain templates, one forest-to-mountains transition), and
they add terrain only.

Step evidence (ARC-DATA-034, [windows](area-step-assessment.md#windows-arc-data-034)): `AreaStepInspector.InspectWholeMap`
takes a window snapshot and reads its generated core sectors from their template archive; the Crash Site window's evidence
equals the pinned exterior's on every core cell and object (7,150 cells reachable from the spawn in both).

Lighting (ARC-DATA-035): `AreaLightingReader.Read(stack, snapshot)` reads a generated core sector's light scheme word
from its template (metadata + 8, read through the stack like the sector file the original reads it as, ledger SS5; 0 is
the map default, as for an own sector); the stationary-light reader reads the moved lights. Every installed test window
lights, and a template held as a loose file is read for lighting and step evidence alike.

**NOT READY:** the viewer does not stream windows; saved differences are not modelled.

## One rule set (ARC-DATA-036)

The owner builds every plan, a catalogue plan or a pinned Crash Site profile, by the same rules: the rules
[above](#rules-of-every-plan), the stack order and the window rules. Nothing in the owner depends on which
profile made the plan except the profile's own expected-output check.

- **Profiles.** `OriginalProfile` (exterior) and `CaveProfile` hold only identities and expected outputs: their input
  files, mapping entries and prototypes by length and SHA-256, the resource order of their two base archives, the
  reconciliation counts, the core sectors and object readiness (`Check`), and the expected endpoint. The per-object
  ART exceptions they once pinned are the general rules now: on the exterior four mobiles whose ground ART (entry 919)
  ends with one NUL and one mobile asking its single-rotation ART for rotation 5, in the cave four items whose ART ends
  with `00` or `02 00` and one container asking for rotation 1. The trailing-byte view and the single-group rotation
  give the same entries, views and pixels. The pinned exterior still equals the independent full-area oracle (2,444 visuals by entry, frame
  and pixels) and the cave its pinned counts.
- **Endpoints.** `AreaSourcePlan.Endpoints` are the use-click transitions a plan expects out of its core. The owner
  publishes them only after checking each against the built objects (`AreaEndpoints.Check`: a present static core
  object with that id and prototype whose own use script, word 2 of the use element of its effective script field, is
  the endpoint's script); a profile's expected endpoint that the data do not carry fails the build
  (`InputIdentityMismatch`). `AreaEndpoints.FromTransitions` derives endpoints by the general rule: the unconditional
  use sites of the map's transition data ([map transitions](map-transitions.md), ARC-DATA-025) whose trigger is a
  static object of the given core sectors. Catalogue plans name no endpoints (map mode reads the map's transitions
  itself, ARC-WORLD-031); the pinned viewer path has no content stack, so the profiles name theirs.
- **Proof (opt-in, installed original).** The general rules reproduce the profiles: the exterior's full-area oracle
  and the Crash Site window (ARC-DATA-032/034: same core sectors, terrain, objects and visuals, step evidence equal to
  the pinned validator, reach 7,150); the cave profile equals the cave's whole-map plan in every core sector, terrain
  cell, object and visual (frame, view, policy and pixels, 194 visuals); and each profile's endpoint is exactly the one
  use transition the transition data give in its core (exterior: script 2793 to the cave, arriving at (2087, 2018);
  cave: script 1532 to the main map, arriving at (92913, 82598)). Tests: `AreaRuleSetRealDataTests`,
  `AreaOriginalTests`, `AreaCaveOriginalTests`, `AreaWindowRealDataTests`, `AreaWindowStepRealDataTests`.
- **Unchanged.** The pinned profiles keep their selection (the exterior's townmap component with its eleven present
  and five absent ring sectors; a window generates those five from templates) and take no map patch or window; the
  viewer's pinned exterior mode and the launcher's pinned regression modes stay (retiring them waits for the map-mode
  work). The pinned AREA export gate compares the oracle's two per-object policy labels as the general labels
  (`tools/local_dev/area_validation.py`, `ORACLE_POLICIES`).

## Limits

Every limit of a whole-map plan, its owner and its lighting is derived from the plan or a
policy cap with at least 1.5 times the largest installed value; the census of all 81 maps and
the headroom of each cap are in the [limits census](limits-census.md) (ARC-DATA-029).

The viewer's [map mode](../rendering/area-map-mode.md) (ARC-RENDER-017) draws these
maps and places a door or window on the wall hot-spot rule (mirror and rotation
shift, see the portal facts); five complete maps still stop at blend states the
scene plan does not draw yet.

Sector and editor mobile files must be the module archive's own (an override
elsewhere is refused, not read; lifting it needs per-file sector and `.mob`
inputs in the owner, which reads both from the module archive's index, so
ARC-DATA-026 leaves it), and the viewer still reads its actor and light
ART from `arcanum1.dat` and `arcanum2.dat` outside the owner, so it refuses a
plan in which either wins nothing under `art/` (`AREA_MAP_SOURCES_MISMATCH`). The main map loads only as windows
([window plans](#window-plans-arc-data-032)); a whole-map plan of it is `TooLarge` by design. Original-executable parity is
NOT_VERIFIED.

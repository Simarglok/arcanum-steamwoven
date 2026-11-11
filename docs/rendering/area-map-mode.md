# Viewer map mode (ARC-RENDER-017)

## Scope

`area` gains a second source: one whole interior of the installed
module, chosen by its `maps/` folder name, drawn and walked in the same viewer.
It is the first consumer of the universal loader: the
[whole-map plan](../world/whole-map-plans.md) (ARC-DATA-019/020) loads the map
from the module's content stack and the
[whole-map step evidence](../world/area-step-assessment.md#whole-maps-arc-world-018)
(ARC-WORLD-018) decides every step. The Crash Site exterior path, its pinned
profiles and every exterior smoke stay as they were.

    ./arcanum map "elven ruins"            # needs ./arcanum setup (any installation) and an ARC-DEV-015 or ARC-RENDER-008 build with map mode
    ./arcanum map Arcanum1-024-fixed [--sector X,Y]   # the main world map, streamed by window (ARC-RENDER-024); a build exported after it
    viewer: --mode area --module … --resource … --source-id … --actor-resource … --game-root GAME --area-map "elven ruins" [--area-map-sector X,Y]

Since ARC-RENDER-024 the main world map opens here too, as a window that moves with the actor
([the main world map](#the-main-world-map-arc-render-024)).

## What it does

- **Inputs.** `--game-root` is the installed game folder; its content stack is
  discovered as the data layer does. The viewer's `--module`, `--resource` and
  `--actor-resource` must be that stack's module archive, `arcanum2.dat` and
  `arcanum1.dat`, spelled as the stack spells them (full paths compared
  ordinally, no case folding or symlink resolution; refused with
  `AREA_MAP_SOURCES_MISMATCH` otherwise). The `--expect-*-sha256` pins are
  checked against the snapshot's identities, and the three archives, which the
  actor reads outside the data owner, are re-verified against them before
  publication, as on the exterior. Lighting and stationary lights are read
  through the content stack in the phase the original reads each file
  (ARC-DATA-027, [sources](area-lighting.md#sources-and-load-phases-arc-data-027)).
  Prototypes come from the stack, so `--prototypes` is refused, as are the Crash
  Site inputs (`--area-mobile`, `--area-effects`, `--area-patch`,
  `--area-cave-mobile`). A plan the data layer refuses stops the load with
  `AREA_MAP_PLAN_<STATUS>` (unknown map, too large, unreadable…).
- **Budgets.** A whole map may retain up to 16,384 frames and 160 MiB of CPU
  frames (`AreaRepositoryPolicy.MaxMapFrames`/`MaxMapCpuBytes`; the exterior
  keeps 2,444 frames and 81,853,096 bytes); the CPU frames plus the actor stay
  within `TerrainCachePolicy.MaxRgbaBytes` (256 MiB). Since ARC-DATA-029 the
  frames visible at once on the GPU are bounded by the same map caps (two interiors
  draw more than the exterior's 2,444 frames: 2,622 and 4,557); terrain chunks and
  the frame LRU are the exterior's. Census of all interiors:
  [limits census](../world/limits-census.md).
- **Chart.** The map's chart starts at its first core sector row and column and
  covers every core sector (`AreaChart.MinimumCell = 0`,
  `MaximumCell = 64 × span − 1`, up to 896 cells for the 14-sector K'na Tha;
  a span over 32 sectors is refused with `AREA_MAP_CHART_TOO_LARGE`).
  Every place that used the exterior's fixed nine-sector window (session start,
  actor placement, approach cells, transfers, quick-save placement, companions)
  now asks the chart; the exterior and cave keep −64…127.
- **Start.** The actor stands on the map-list arrival tile (`rules/maplist.mes`,
  ARC-WORLD-018) when it can stand there; otherwise on the first core tile (by
  row, then column) where it can. A tile qualifies with open terrain and a
  cardinal neighbour it can step to and back from, so no body blocks it.
- **Steps.** The T0 whole-map evidence of the same strict publication: walls and
  window pieces, doors by the original's open attempt for the player, and the
  live actor overlay. Click-to-move, the stick and the passability overlay work
  as on the exterior.
- **Portals drawn on the wall rule.** Doors and windows (art class 3) take the
  wall hot spot: rotations 0, 1, 6 and 7 shift it 40 left and 20 down, then
  the flip bit mirrors it (the original's ART hot-spot code, source reading, F0). The pinned Crash
  Site profiles draw no portal, so their plans are unchanged.
- **Lighting and session.** The map's own ambient scheme and stationary lights
  when its sources admit them (the lighting reader now reads up to 256 core
  sectors; otherwise the map is drawn unlit and the HUD names the lighting failure). Since ARC-WORLD-057 an unlit map
  still has its game session: its clock starts from the module's `rules/gameinit.mes` alone
  (`AreaLightingReader.ReadStartup`), with the same clock profile as a lit map of the module, so a session carries
  between them (native smoke on `bessietoonesmine-fixed`, drawn unlit for sectors without the metadata section: the map
  battery with `--hero`, the transfer to the main map and F7/F8 pass). A map whose start clock cannot be read has none.
  Since ARC-DEV-124 (MIG12 S5) an unlit map has its own frame source, `AreaFrameDriver` (the same `FrameStep` as a lit map's ambient driver, with the same
  Menu pause for the Esc menu and no lighting to follow), so its session clock flows, its event passes and the AI host's goal runs go on, as in the
  original, whose clocks and AI do not depend on the lighting; the palette's wait (a time jump through the ambient driver) is still unavailable there.
  The session places the actor on the map's runtime number.
- **Inventory.** With a session, ground pickup, looting and the inventory window
  work on the map as on the exterior (the map's T0 items seed the session).
- **Transitions.** See [map transitions](#map-transitions-arc-world-031) below.
- **Dialogs.** See [dialogs in map mode](#dialogs-arc-world-032) below.
- **Quick save.** See [quick save in map mode](#quick-save-arc-world-038) below.
- **Companions.** See [companions in map mode](#companions-arc-world-037) below.

## Coverage on the installed original

Opt-in `RealData` tests (`tests/Arcanum.TerrainViewer.Tests/AreaMapLoadOriginalTests.cs`,
`DAT_ROOT` = the game folder): four interiors load with a drawable plan, step
evidence and a session the walker can leave (the cave spawns on the pinned
cave's arrival 2087,2018); all interiors whose strict snapshot is
complete load in the viewer: all 80 since ARC-DATA-024 (K'na Tha's shared frame
selections), 79 since ARC-DATA-023 (the original's
[art names and bad art](../world/whole-map-plans.md#rules-of-every-plan)),
54 before it and 49 before ARC-RENDER-018, which draws the
[body blends and wading](#body-blends-and-wading-arc-render-018) of five of them.

Native smoke (`--native-smoke`, macOS, Godot 4.7.2 .NET, 1280×800): the map
draws, a left click through the production ground-click seam sets a route goal
up to 8 open steps away, frames walk the actor there on the map's chart, and the
resource sweep and overlapped reload pass (`ARC-RENDER-017 native-smoke map
PASS`, `ARC-RENDER-007 resources reload PASS`). Since ARC-WORLD-021 a map with a reachable window also
steps the actor across it with the vault (`ARC-WORLD-021 native-smoke vault PASS`; otherwise `NO-WINDOW`). Since ARC-COMBAT-005, with `--hero` and a lit map, the hero attacks the living NPC with the fewest hit points until it dies ([hero attack](../world/hero-attack-runtime.md#viewer-area); `ARC-COMBAT-005 native-smoke attack PASS`, otherwise `NO-TARGET`; observed on `crash site cave`: a rat of 6 hit points, walk to reach, 12 attacks, fallen pose read, 192 experience). Since ARC-COMBAT-008, a stage Alt-clicks the attackable door, window, container or scenery with the fewest hit points and attacks it until it breaks or 40 attacks ran ([object damage](../world/object-damage-runtime.md#viewer-arc-combat-008-part-2); `ARC-COMBAT-008 native-smoke object-attack PASS`, otherwise `NONE`). Since ARC-COMBAT-009 the last stage arms the hero's critical-hit fate (a granted point) and attacks the living NPC with the most hit points through the session, so the original's critical-effect tables run over that NPC's real data ([critical effects](../world/hero-attack-runtime.md#critical-effects-arc-combat-009); `ARC-COMBAT-009 native-smoke critical PASS` with the body type, the effects and the item steps, otherwise `NONE`). Observed on `elven ruins`
(9 sectors), `bates mansion lev 1` (9), `crash site cave` (1) and
`caladon-castle main floor` (79 sectors, 323,584 terrain cells, 2,967 bodies,
load about 14 s). Since ARC-DATA-023 also on `crematorium-lev 2` (concealed
monsters), `the lair of bellerogrim` (unique NPCs repaired to their unarmed
standing art), `dernholm pits` (damaged wall), `caladon-panarrii temple` (tile-art
scenery) and `tulla interior` (42 sectors, repaired `AIR` monster, vault PASS); the
first of four `crematorium-lev 2` runs, right after a build, stopped at the
resource sweep's coverage check (`AREA_RESOURCES_SWEEP_COVERAGE`, a command not
seen in any view) and the three repeats passed; ARC-RENDER-023 found the likely
cause (below; not reproduced on this map).
Since ARC-DATA-024 also on `k'na tha` (77 sectors, 315,392 terrain cells, 43,087
drawn bodies, 416 frames, 8.6 MB of frames; wading blend, vault, sweep of 2,728
views and reload PASS). Its load took about 187 s until
ARC-RENDER-019, almost all in the scene plan, which looked up each body's visual by
scanning every visual, and in the wall check, which did the same per wall; both
now use a lookup by key, and K'na Tha loads in about 17 s in a source run (the
strict snapshot about 10 s of it). A small map may fit every GPU cache, so map mode does not
require evictions in the sweep; every per-view budget and the ownership balance
still hold. Eviction itself is therefore shown only by the exterior sweep. The
reload's managed-heap bound (half the CPU repository) has an 8 MiB floor in map
mode, because a small map's repository (the cave's is 1.5 MB) is below the
heap's ordinary noise, and since ARC-RENDER-018 a second floor of 4 % of the heap
before the reload, because a large map's heap (about 540 MB live for the 64-sector
`tarant sewers-02`) varied between runs from −63 to +26 MB in a single post-GC
sample. The baseline and the retained size are each the least of three post-GC samples
a frame apart (exterior and maps); the retired generation must still be collected.

Since ARC-RENDER-025 the last collection of each sample compacts the large-object
heap. On `tarant sewers-02` the earlier flake did not come from growth. In five
runs the reload left the live heap (the generations' size less their free space)
about 10 MB *smaller*: 543.4 MB before, 533.0 MB after. In those runs the
large-object heap held 96 to 208 MB of free space, and the uncompacted samples
differed from the live bytes by up to tens of megabytes, so before and after
sometimes crossed the 4 % floor. This is an observation; the runtime's own
estimate is meant to exclude free space, and the mechanism is not established.
With compaction, in four runs, the samples equal the live bytes to within about
10 KB (543.4 MB before, 533.0 MB after each time).

Since ARC-DEV-060 a growth over the bound gets one more reload before the check
fails. After the whole map chain on `elven ruins` (about 400 MB live heap, a 16 MB
bound) the reload grew the heap by 21 to 24 MB in three of four runs (the launch
queue's run logs on the owner's Mac, outside git); with two extra reloads in one run
the growth from the baseline was 14.2 MB after the overlapped pair, 20.8 MB after the
next reload and 20.8 MB after the one after it: the first reloads fill content once,
then the heap stays flat. A retention that every replaced generation adds grows the
heap again by as much, so the check takes the smaller of the two growths, and the
extra reload must publish and the generation it replaces must be collected too. The
bound then holds one generation's retention rather than the overlapped pair's: a
retention between half the bound and the bound per generation, which the pair alone
showed, now passes.

## Body blends and wading (ARC-RENDER-018)

The scene plan draws three more serialized body states, by rules checked in the
retail executable
([ledger BL1–BL5](../research/ledger/world.md#rendering-body-blends-and-wading-arc-render-018)).
They are general rules of every body, not of these maps.

- **Blend choice.** Blit additive `0x10` wins over multiply `0x40`, which wins
  over constant alpha `0x100`. The serialized blit alpha (0–255) is used only by
  constant alpha; any other blit alpha than 255, any other blit bit (subtract,
  the alpha and palette modes) and a non-white blit colour stay
  `AREA_UNSUPPORTED_BODY_STATE`, as does a frozen body (`OF_FROZEN`, drawn
  additive with a constant colour before any blit blend); `OF_TRANSLUCENT` stays
  `AREA_UNSUPPORTED_TRANSLUCENCY`.
- **Multiply.** Each opaque source pixel multiplies the colour beneath it;
  transparent pixels leave it unchanged (`AreaDrawCommand.Multiply`, a
  `blend_mul` shader whose output alpha is 1).
- **Constant alpha.** The body is drawn at its blit alpha (`Alpha`, the source's
  weight; the shader divides by 255 where the original divides by 256, at most
  1/255 apart).
- **Unlit.** Additive (since ARC-RENDER-026, ledger BL2), multiply and
  constant-alpha bodies are drawn with the base palette,
  without ambient or stationary light (`Unlit`, the `Unlit` ambient channel), and
  a wall among them never fades.
- **Wading.** A body with `OF_WADING` and without `OF_WATER_WALKING`, as
  serialized at T0, draws its lowest 15 destination rows (after scale) at alpha
  92, which replaces every other alpha of those rows; a flat wading body is
  drawn whole at alpha 92 (`WadeRows`, relative to the destination, so a moved
  command keeps its water line). Light still applies. An additive or multiply
  body keeps its blend over the whole frame: the wading pass leaves those modes
  set beside the wading alpha, and the original's ART blitter tests additive and
  multiply before constant alpha. That dispatch is a source reading, not checked in
  the executable (F0), and no installed body has the combination.

On the installed original (opt-in test): additive bodies, drawn unlit, on most
interiors (115 in `bmc`, 86 in K'na Tha, 76 in `ashbury haunted castle`); 20 wading bodies in `caladons sewers`
and 102 in `tarant sewers-02` (rats, lake scenery, a container), 12 and 42
multiply scenery bodies in `void - arronax's interior` and
`void-kerghans castle`, and one constant-alpha critter (alpha 95) in
`bessietoonesmine-fixed`.

The map native smoke observes each kind the map has (`ARC-RENDER-018
native-smoke blend PASS`, or `NONE`), after the walk and its screenshot: for one
opaque, uncovered pixel of an unscaled body that is not a wall, where the result
differs from both inputs, the colour beneath (the body left out) and the drawn
colour must match the original's formula within 3/255: multiply `s·d/255`,
constant alpha and wading rows `d + (s − d)·a/255`; an upright wading body's rows
above the water line must show the source itself where it differs from the
colour beneath. The tolerance does not tell alpha 92 from 95. Lighting is off
during the map smoke. Observed on all
five maps (multiply on both void maps, alpha 95 in the mine, wading in both
sewers).

Not covered: wading is the T0 flag only (the original recomputes it from the tile when
an object moves, a source reading not checked in the executable, F0; the walking
actor never wades); shadows (the original also darkens a wading body's shadow,
source reading, F0); frozen, stoned, `OF_DONTLIGHT` and
animated-dead tints.

## Map transitions (ARC-WORLD-031)

Map mode moves the player between maps by the map's own data: the
[map transitions](../world/map-transitions.md) (ARC-DATA-025) read from the
content stack when the map loads, so every map works the same way and nothing
is bound to a location. Unconditional transitions run as below; a use site whose
teleport stands behind guards runs its use script since ARC-SCRIPT-015
([guarded use transitions](#guarded-use-transitions-arc-script-015)), and every tile
and sector script the player enters runs since ARC-SCRIPT-017
([tile and sector scripts](#tile-and-sector-scripts-arc-script-017)).

- **Triggers.** A left click on an object with an unconditional use teleport
  (scenery or a portal) is a use: accepted within Chebyshev distance 2 by the
  ARC-WORLD-009 use rule (`AreaUseRule`, now shared with the Crash Site pair),
  otherwise the click route first walks to the nearest cell from which the rule
  accepts the use, and the use is asked on arrival; an exit to a map that map
  mode cannot open is refused at once, without the walk. Every tile the actor
  steps into is checked in step order, also when one frame completes several
  steps: a jump point or an unconditional tile script there fires, the jump
  point first (ledger TR3), and stops the click route (the actor may stand a
  step further when the frame had already started it); since ARC-SCRIPT-017 the
  tile's script runs instead of its unconditional teleport (which still fires when
  the run is refused before it starts), and a sector script runs when a step
  enters its sector. One transfer
  is pending at a time; the stick, clicks and hover actions wait while it is,
  and a reset or reload drops it; a reset puts the actor on its start without
  firing the start tile, as a load does.
- **Destinations.** A transfer applies once the actor is settled. On the same
  map the actor moves to the authored arrival. Another map must be one map mode
  can open: listed in the map list with a sector file (every installed map folder
  of the list since ARC-RENDER-024: a map with more than 256 sector files, the
  2000 × 2000 start map, opens as a window around the arrival; before it every exit
  to the start map was refused with `DESTINATION_NOT_OPENED_IN_MAP_MODE 5000`). The destination is loaded through the ordinary load path
  with the authored arrival (`AreaMapArrival`, never a command-line option): the
  actor starts exactly there when the step evidence admits the tile (the
  original places the player without a passability check, W9), otherwise at the
  map-list arrival rule. A palette reload afterwards reloads the arrival map.
- **Session.** A transfer leaves combat mode, as the original's teleport does
  (ARC-COMBAT-004). Since ARC-DEV-113 (MIG11 S2) a settled transfer is the
  session's [change of place](../architecture/change-of-place.md#3-what-the-session-does-s2):
  the hero's request (a jump point with flags 0, a script's teleport with the
  flags of its action) is run by the session in the same frame, which advances
  the clock for a fade-and-teleport and writes the hero, the companions and the
  move list in one revision; the fades, sound and movie are not drawn. The game
  session is carried over: when the new map is published, the change writes its
  actor placement on the new map and arrival in one session step, and the new map's T0 items are attached beside those of the maps
  already visited (the session holds the seeds of at most 200 maps, and two maps
  listing one object id refuse the second seed; then the inventory is
  unavailable on that map). A map without lighting keeps the carried session. If
  the destination's load fails before the scene is published, the origin map is
  loaded again with the actor where the transfer started and the same session,
  and the HUD names the failure (this fallback is not observed by the smoke); a
  failure after publication (a visible upload) is shown like any load failure.
  Reading a map's transitions adds a second pass over its sector files to each
  load: about 0.3 s for K'na Tha, under 0.1 s for the other large maps, all of
  it transient.
- **Not covered.** A teleport into another jump point does not chain (the
  original nests up to 9). Tile scripts fire for the player only (the original
  runs them for any critter). No fade, sound, movie or time advance of a
  fade-and-teleport. Followers travel with the player since ARC-WORLD-037
  ([companions](#companions-arc-world-037)).

Opt-in tests (`tests/Arcanum.TerrainViewer.Tests/AreaMapTransfersOriginalTests.cs`):
`crematorium-lev 1` has its two exits; the way down is accepted and loads
`crematorium-lev 2` at its arrival, which leads back up and further down; the
way out to the start map is refused; the 17 jump-point tiles of `tulla
interior` move the player on the same map. Native smoke (map mode, after the
resource stages): `ARC-WORLD-031 native-smoke transfer PASS` (a use through the
production click seam, the destination load, the arrival and the carried
session; observed `crematorium-lev 1` → `crematorium-lev 2`, script 2369,
arrival 116,112) and `ARC-WORLD-031 native-smoke jump PASS` (a ground click
walks onto a jump point; observed in `tulla interior`, 437,214 → 304,219), or
`NO-EXIT`/`NONE` when the map has no such trigger reachable from its start.

## Guarded use transitions (ARC-SCRIPT-015)

A use site whose teleport the [transition reader](../world/map-transitions.md) finds only behind a condition or with a
computed destination (guards `ConditionalPath` and `ComputedDestination` only, on scenery or a portal) is a use trigger
in map mode too, and its use script decides (plan slice S4, viewer part). Any other guard (a trap script, a container, an
object of another kind, an object off or not clickable at T0, a teleport of another object) keeps the site out; an object
with an unconditional exit stays that exit.

- **Run.** The click goes through the same use rule and walk-to-use as an unconditional exit. Within range,
  `AreaUseScriptHost` runs the object's use script once, with the PC as the triggerer, under the set "S4-area-use-reading-objects": S4
  without the story-state actions (the session has no story-state number) and without the dialog action, plus the reading actions
  64, 65 and 114 (ARC-UI-049: a PC viewer's request opens the [reading window](../world/reading-window-runtime.md)). The use slot is
  the row's script attachment, overridden by the session's map changes `use.lf`, `use.counters` and `use.script` of the
  object (a mobile by its own id; a sector object, whose id is null, by its sector and window ordinal,
  `AreaUseScriptHost.EntityId`), and a changed slot is written back there (since ARC-DEV-096 the slot parts are fields of the object, a mobile's one record by identity and a sector object's per map, saved in `objects.state`; a value equal to the loaded one clears its key), so a door that
  removes its script or keeps a flag stays so, also across F7/F8 and map changes.
- **Guards** (ledger SG1–SG6) are answered from the session: the PC's quest log (botched reads 6) and, for any other
  object, the quest's global state; the hero's kit and carried items and their worn slots; a holder's current list and
  its items' inventory locations; any object's name number and spell flags from its row (the PC has no row: name 0, no
  spell flags); the PC's reputations from the session (ARC-WORLD-044, [reputation runtime](../world/reputation-runtime.md); only a PC has them).
- **Effects**, applied as the run goes, as for a dialog script: global flags, sound-scheme requests, deferred calls to the
  session's queue, the use slot; a step the session refuses leaves the earlier ones applied and the use is reported
  refused. Printed lines show on the status line; effect sounds are not played. The first teleport of the player is the
  transfer: its map (−1 keeps this map, ledger TR4) and tile replace the site's, and it queues through the ordinary
  transfer path. A teleport map mode cannot take (an arrival the step evidence does not admit)
  is checked before anything is applied: the use is refused, the door stays as it was and its printed lines are not shown. A teleport of another object
  is not modelled: the run's effects are dropped and the use is refused. A run outside the set, without script content or
  after a script file changed is refused and changes nothing.
- **Installed original** (opt-in `AreaUseScriptsOriginalTests`, `DAT_ROOT`): 21 guarded use sites on 6 of the 79 maps map
  mode opens (none on the start map). With a new game's state (no flag, quest or item) 9 run and their guards hold the teleport back, 12 are
  refused because they need conditions or actions outside the set (immediate script calls, loops, other conditions). In
  `tulla interior` the door with script 2353 prints its line while global flag 2376 is clear and, once it is set,
  teleports the player to (168,218) on the same map.
- **Not covered.** Immediate script calls, loops and focus groups; a teleport behind a deferred call (the call later runs under the dialog host's set, which has
  no teleport, and is refused); the PC's name number; a fade-and-teleport's fade, sound and movie.

## Tile and sector scripts (ARC-SCRIPT-017)

Every tile script and sector script of the shown map runs when the player enters it, teleport or not (plan slice S4,
viewer part), through the same host and set as a guarded use. The [transition reader](../world/map-transitions.md) lists
them as `MapTransitionSet.Placed`: 3,012 tile scripts and 20 sector scripts in the installed maps (2,439 tile scripts on
the start map), one per tile.

- **When.** For each step the actor completes, in step order (ledger TS2, read in the executable): the tile's jump point
  first, then the tile's script, then, when the step enters another sector, that sector's script. Since ARC-SCRIPT-046
  a companion's step runs the tile's script too, with the companion as the triggerer (below). A load, a reset, a transfer arrival and an F8
  restore fire nothing; the original places an object on the map with the same notifications (TS2), which arrival path
  a teleport takes is not read.
- **Run** (ledger TS1, TS3): from line 0 as a use, the player the triggerer, no attachee, under "S4-area-placed-dialog-actor" (the use set
  "S4-area-use-reading-objects-story-world" with the dialog action since ARC-SCRIPT-046, and call-ex, destroy and stop attacking since
  ARC-SCRIPT-052). The slot
  is the sector file's attachment overridden by the session's map changes on the sector: `tile.N.script`, `tile.N.lf`,
  `tile.N.counters` for tile index N, `sector.script`, `sector.lf`, `sector.counters`. A changed slot is written back
  there; under the Original rules a tile script's change is discarded (ledger X3, the original stores the unchanged
  record), a sector script's is kept in both modes.
- **Effects and teleports** as for a guarded use: flags, schemes, deferred calls and the slot apply as the run goes; the
  first teleport of the player is the transfer, checked before anything applies. A teleport asked while another transfer
  is pending (the tile's jump point fired) is dropped and the run's other effects stay (TR4). A tile run refused before it
  starts (an action or condition outside the set, no script content, no session) leaves the tile's unconditional
  teleport of ARC-WORLD-031 to fire as before: the four same-map tile teleports of `void` keep working that way. An
  entry run checks only the files it reads for changes (a changed script refuses it; a changed dialog file skips that
  printed line, and later runs are refused), not every file seen so far as a click does.
- **Status and log.** The status line shows a transfer, an exit map mode cannot take and a run that printed lines;
  refused and silent runs are only logged (`ARC-WORLD-031 enter`), and nothing replaces an accepted transfer while it
  is pending. One host serves every run of the shown map and session.
- **Installed original** (opt-in `AreaUseScriptsOriginalTests`, `DAT_ROOT`, a new game's state for each script):
  6 tile scripts teleport (the exit tiles of `iron clan hq`, script 2203, to the start map at (96948,75722), and since
  ARC-SCRIPT-018 a tile of `void-kerghans castle`, script 3070), 1,180 tile scripts and 16 sector scripts run without one,
  1,696 tile and 4 sector scripts are refused because they need actions or conditions outside the set, and since ARC-WORLD-081
  the 130 tiles of script 1629 on the start map run to a create object of a critter prototype (28464, eight next to the PC), which the
  census's host (no world) cannot apply, so their effects are refused there; the viewer creates the critters since ARC-DEV-371 (before ARC-DEV-157's get stat and vicinity loops: 850 and 2,026; before ARC-DEV-100's store operands: 823 and 2,053; before ARC-SCRIPT-018: 5, 249 and 2,758; before ARC-SCRIPT-019: 6, 638 and
  2,368; before ARC-WORLD-081: 678 and 1 run, 2,328 and 19 refused; before ARC-SCRIPT-052's call-ex, destroy and stop attacking: 678
  and 2,198).
- **Generated sectors** (ARC-WORLD-081, [template sector plants](../world/template-sector-plants-runtime.md)): a main-map window
  also lists the sector scripts of its template sectors (the template's script section, ledger PG9), and they run on entry like an own
  sector's. The only one, the plant generator 30285, creates herbs next to the template's marker when the marker stands within 10 tiles
  of the player (set S15: random, loops over the vicinity's scenery, create object); they lie on the ground as created placed items,
  drawn from their prototype's art and picked up like any placed item, at most 6 within 3 tiles of a marker under Steamwoven (no limit
  under Original). The log line `ARC-WORLD-081 created` names each creation. Native smoke (the stage `plants`, run only when named:
  `ARC_AREA_MAP_SMOKE_ONLY=plants` on the main map with `--area-map-sector 1384,239`): with the PC reported on a core marker, the
  marker's sector script runs 40 times through map mode's own runner; on 2026-10-28 it listed 19 template scripts in the window and
  laid 6 herbs (Passion Root and Witchbane, the elven forest variant 67108864 of sector (1385, 238)) next to the marker, all drawn and
  picked as ground items: `ARC-WORLD-081 native-smoke plants PASS`.
- **Companions and conversations** (ARC-SCRIPT-046, ledger TR3 and X11, read in the executable): the original runs a tile's script
  after every move of a PC or an NPC, with the mover as the triggerer; the jump point only for a PC, a sector script only for the
  player. Every step a following companion of the shown map starts into a scripted tile runs its script with the companion as the
  triggerer (`AreaMapTransfers.EnterByCritter`, `AreaUseScriptHost.RunPlaced` with a triggerer); its teleport is not modelled and
  leaves that run unapplied. A dialog action of a tile or sector run, or of a script it calls, opens that conversation of the PC with
  the action's attachee once the run's effects are applied (`AreaScriptedUse.Dialog`; the conversation host's `BeginScripted`, no
  dialog script run): combat mode is left first, as for a talk, and none opens while another conversation or a modal window is
  open (logged); the player's steps still queued wait until it closes, a companion's remaining steps of that drive run nothing.
  A dialog action with no NPC of the shown map to talk to (a tile script's own, which has no attachee), or with a teleport, applies
  nothing and counts as a refused run (so a tile's unconditional teleport still fires); a stored use slot of an NPC with no loaded
  use element leaves the run unapplied. Such a conversation's "return to script" does not resume a script (not modelled; the
  speaker's dialog script is not the one that started it), and a DLG with an LF operation is refused (which local flags it reads
  for such a start is not read in the executable). Log lines: `ARC-SCRIPT-046 companion-tile` (a companion's run that did something),
  `ARC-SCRIPT-046 scripted conversation`. Installed original: at the plane wreck of the Crash Site, script 1695 on a row of tiles
  waits for Virgil (name 6409) to step on one, then calls 1696, whose dialog asks for node 1 of DLG 1696 (his remark on the
  flying machine; its last replies set flag 2304, which ends 1695 at once from then on); the player stepping there runs it to
  its return with nothing (the bug report of 2026-10-29). Opt-in check `AreaUseScriptsOriginalTests` (`DAT_ROOT`); native smoke
  the stage `wreck`, run only when named (`ARC_AREA_MAP_SMOKE_ONLY=wreck` on the main map at the Crash Site): Virgil joins, stands
  one cell before a wreck tile with the PC four cells past it, follows, and his first step opens the conversation
  (`ARC-SCRIPT-046 wreck smoke PASS`; on 2026-10-29 at tile (92912,82552), in 1 frame, DLG 1696 node 1). A conversation
  left before its last reply leaves flag 2304 unset, so his next step onto a wreck tile opens it again, as the script reads.
  The other native smoke stages hold a scripted conversation (ARC-WORLD-118): the run stands, the conversation does not open
  (`ARC-WORLD-118 scripted conversation held by the smoke`), so a following companion stepping onto a wreck tile cannot
  quarantine the world and stop a stage's own walk (the map battery's far click, with a hero whose companion Virgil joined in the
  talk stage, stopped so on 2026-10-29); only the `wreck` stage lets them open. A smoke stage that observes a scripted
  conversation of its own sets `_smokeTakesScriptedConversations` for its run.
- **Not covered.** Tile scripts for NPCs other than following companions (moving NPCs of the AI host, visiting companions, a
  companion's jump onto the PC); the actions and conditions outside the set (most tile scripts); the arrival tile of a transfer; a
  fade-and-teleport's fade, sound and movie.

## Dialogs (ARC-WORLD-032)

The dialog NPCs of a map are those of its own snapshot (a core critter whose
effective script slot names a dialog script), so every map talks the same way
and nothing is bound to a location. With a session (a lit map, or one reached
by a transfer), the left click, hover action, walk to talk, modal panel,
dispatcher run and DLG reading are those of the exterior
([AREA viewer integration](../scripting/dialog-conversation-runtime.md#area-viewer-integration-project-diagnostic)),
with the game's script content found from the module archive. The consequences
(GF, quests, rumors, an NPC's local flags, reaction and dialog slot) are
session state on the map's runtime number, so they go with the session to the
next map and come back with it. Since ARC-WORLD-037 the party commands (`JO`,
`WA`, `UW`) of a speaker with follow facts go through the session party
([companions](#companions-arc-world-037)). A map the map
list does not name (`scienticians underground lev 1` and `lev 2`) has no runtime number,
hence no session (no dialogs, saves or sound; since ARC-RENDER-027 again: an unlit map's session clock, ARC-WORLD-057, had made the
viewer ask for a session the session owner refuses for runtime map 0, `AREA_SESSION_InvalidActor`)).

Opt-in census (`AreaMapConversationOriginalTests`, `DAT_ROOT`): 34 listed
interiors have 182 dialog NPCs. Each one gets a typed start: 50 start a
conversation, 1 ends with skip default, and 131 stop at an SCR action,
condition, goto or value kind the dispatcher does not admit yet (most often
action types 23 and 22, and condition type 21). Of the 50 conversations, 35 stop at a generated NPC
line, which is not loaded yet, 6 at an unsupported DLG operation, and 9 offer
replies at once; 7 end when the first choosable reply is always taken (since ARC-WORLD-043 the `ME` condition is
admitted: before, 8 stopped at an unsupported operation, 7 offered replies and 6 ended). Native smoke (`ARC-WORLD-032 native-smoke talk PASS`, after
the roof smoke): from 4–6 cells away the NPC hovers as a walk to talk, a click
through the production seam walks within reach and opens the panel (a walk
that stops short is tried again from within reach, where the click must open
it), the first choosable replies run until the dialog ends or stops, and Close
ends the modal panel; a start refused for missing script content, an entry
error or a changed source fails the smoke. `FRONTIER` means every NPC tried (up
to eight) stopped before offering a reply, `NOT-STARTED` that none started,
`NO-REACHABLE-SPEAKER` that none could be clicked, and `NONE` that no NPC is
drawn. Since ARC-WORLD-043 Close must also end the NPC's reaction conversation (the host holds no open slot and a
started talk leaves the NPC remembering the PC, `MetBefore` in the record). Observed: `caladon-panarrii temple`, script
2969, 3 turns, the dialog ended, 1 effect (3 session revisions with the reaction slot's open and close; 2026-10-21), the NPC
remembers the PC; `tulla interior` reports `FRONTIER` (generated
greetings); in `caladon-mafia underground` both NPCs are undrawn. That the
consequences travel with the session to another map and back is the host's
synthetic test (`AreaConversationTests`); the native smoke does not observe it.

## Quick save (ARC-WORLD-038)

With a session, F7 and F8 work on any map as on the exterior
([quick save](terrain-viewer.md#quick-save-and-quick-load-arc-world-011): keys on
release, the same guards, atomic slot, every check before anything changes),
with their own slot, `user://saves/map/quicksave.json`, so the Crash Site slot
is never touched (an automated run keeps both slots in its own folder,
[ARC-DEV-032](terrain-viewer.md#automated-runs-and-the-players-settings-arc-dev-032)). A save records the actor on the map's runtime number. While
another map loads there is no session, so F7/F8 do nothing; a load drops a
queued transfer.

- **Same map.** A save whose actor stands on the map shown is adopted in place,
  as on the exterior: the shown map's inventory seeds and dialog state are
  checked against it, then it replaces the running session. ARC-WORLD-120: on a
  map shown as a window of sectors (the main map, ARC-RENDER-024), a save whose
  cell lies outside the window's core sectors loads as a save on another map
  does (below), the window opening around the saved cell; it was refused as "not
  admitted for movement" before, which broke F8 and the death's load box for a
  save made on the main map away from where the hero stands (bug report triage
  D1). Native smoke: the `debug-travel` stage saves before its teleport and loads
  that save from the far window: `ARC-WORLD-120 native-smoke far load PASS`.
- **Another map.** A save whose actor stands on another map that map mode can
  open (listed with a sector file; the main map as a window around the saved cell, ARC-RENDER-024) passes the same checks of the slot, the
  restore and the shown map, then loads that map through the ordinary load
  path with the actor on the saved cell, as a transfer does; the shown scene is
  cleared as the load starts, as for any load. The loaded session keeps its
  saved sound state and is adopted when the map is published; the running
  session is kept until then and released once the map is published with the
  loaded one. When the map does not load, or publishing it refuses the loaded
  session (for example its dialog state there), the shown map is loaded again
  with the running session, the actor where it stood and its facing. Checked
  only once the map is loaded: the saved cell (when that map does not admit it,
  the actor stands on the map's start and the HUD says so) and the map's own
  inventory seed (attached as after a transfer, so a disagreement leaves its
  inventory unavailable instead of refusing the load). A save on a map map mode
  cannot open is refused.
- **Same map, host state.** A load on the map shown also starts the wall check
  and the roofs afresh and drops the tiles the replaced actor had stepped into.

Native smoke (in the transfer smoke, with a session): F7 next to the exit, F8
a step away brings the actor back to the saved cell under a new session; after
the transfer to another map, F8 loads the saved map again with the actor on
the saved cell under the loaded session, after the destination's jump smoke
(`ARC-WORLD-038 native-smoke quickload PASS`; `SAME-MAP-ONLY` when the exit
moves the actor on the same map). Observed
`crematorium-lev 1` → `crematorium-lev 2` → F8 → `crematorium-lev 1` at
92,94. The fallback after a failed load is not observed natively.

## Camera held during smoke samples (ARC-RENDER-023)

Until ARC-RENDER-023 K'na Tha's native smoke failed in most runs, on `origin/main`
before ARC-RENDER-019 as well: the blend check of its first wading body reported
`AREA_BLEND_PIXEL_MISMATCH`, a run stopped once at the resource sweep's coverage
check, and one run that passed the blend check stopped at the reload's managed-heap
bound (+64 MB over a 4 % floor of about 33 MB). The two reads of the blend check
showed different places: the second frame was centred on the actor. A sampler sets
the camera with a reset and reads the frames after it, but whenever the viewer
window had the real focus the world input gate was open, and the camera follow then
returned the view to the actor a frame or two later. Whether the window was focused
depended on the desktop, so the failure looked random and was not caused by other
sessions' windows (it also failed with no other viewer running).

The blend samples and both resource sweeps now run with the window focus simulated
off (the gate stays closed, so the follow does not move the camera), and each
refuses a view that moved after its reset: `AREA_BLEND_CAMERA_MOVED` for a blend
pair, `AREA_RESOURCES_CAMERA_MOVED` for a sweep step. Observed after the change:
eight `k'na tha` runs with the blend check PASS, and the four runs after the sweep
was held too complete (map, sweep and reload PASS; managed heap after the reload
between 0.7 MB smaller and 10 MB larger), `crematorium-lev 2` and `tarant sewers-02` complete PASS. The +64 MB
reload was not seen again; its cause is not known. The resource sweep is shared with
the exterior AREA smoke, so its two sweeps are held there too.

ARC-RENDER-029 found the same cause behind the exterior Crash Site smoke's stop at its
first class sample (`AREA_CLASS_PIXEL_NOT_OBSERVED`, failure stage `Upload`): on
`origin/main` the smoke passed twice with the window visible but not focused, and
stopped there in the one run with the focus simulated (a scratch build that keeps the
window's focus state on, since a background process cannot take the real focus on
macOS). Every sample read a view on the actor instead of the one just set,
so all 64 terrain candidates of the first group were off screen. The class samples,
the exterior lighting samples, the cave lighting samples (already on test time, so
the hold only guards them) and the HD identity views (shared by the exterior and
WORLD smokes; WORLD runs with processing off, so there the hold only guards) now hold
the camera the same way, each refusing a view that moved after its
reset: `AREA_CLASS_CAMERA_MOVED`, `AREA_LIGHTING_CAMERA_MOVED`,
`CAVE_LIGHTING_CAMERA_MOVED`, `HD_SMOKE_CAMERA_MOVED`. Observed after the change: four
exterior runs with the focus state held on complete (class samples, HD, lighting,
transition with cave lighting, sweep and reload PASS); in the first, before the HD
views held the camera, the widest-body view counted 46 HD draws (the actor view's)
instead of 15, and the three after it count 15. Five runs without it complete, the
`crash site cave` map smoke passes and the WORLD smoke passes. A window that really holds the focus was not observed: neither
`osascript` nor Godot's own move to the foreground could take it from a background
run.

## Companions (ARC-WORLD-037)

Any dialog NPC of a map can be a companion: once in the party it follows and
travels with the player. Nothing is pinned to Virgil or to a location.

- **Facts from the data.** The follow check's NPC side comes from the NPC's own
  fields: the AI row its AI data field names in the
  [AI parameter table](../world/ai-parameters.md) (fields 5..8), and its level
  and alignment as the original's stat getter reads them. That means the stored
  stat, then every effect of its effect list that changes it (the effect tables
  from the content stack, the original's aggregation), then the getter's clamp of
  0..51 and −1000..1000 ([ledger AP5](../research/ledger/npc-ai.md#ai-parameters-and-follow-consent-arc-data-033));
  an effect number without a definition changes nothing, as in the original.
  `AreaCompanionData` opens the tables once per module. The PC side is the
  session hero with `--hero` (ARC-WORLD-067, [party runtime](../world/party-runtime.md#the-session-hero-as-the-pc-side-arc-world-067)):
  its charisma, level, alignment, Persuasion and max followers; without one the
  labelled AREA diagnostic profile (charisma 8, level 1), with which a dialog `JO`
  meets the level gate of most NPCs (most rows have a gate of 2 and about 170 of
  the NPCs are level 3 or above). On
  the installed original all 185 interior dialog NPCs read, on ten distinct
  follow rows (`AreaMapCompanionOriginalTests`); Virgil's facts read this way
  equal the measured ones.
- **Joining.** The conversation host passes every speaker with facts through the
  session party port, so a `JO` runs the follow check. A speaker the party has
  touched (following, waiting, or recorded away from its T0 cell) becomes a
  moving companion of the map with its T0 body; every other speaker stays its
  T0 body, and an NPC dead at T0 stays its T0 corpse, never a moving
  companion (ARC-WORLD-049). The group is rebuilt when a conversation ends
  (the follow smoke closes a conversation after the join and checks the
  rebuilt companion).
- **Following.** These are the companion follow rules of the exterior
  ([companion follow runtime](../world/companion-follow-runtime.md)): start
  beyond 4 cells, stop within 2, and the 30-cell catch-up.
- **Travel.** Before the player leaves, the companions' cells are recorded. On
  a transfer to another map, every follower arrives on the arrival cell in the
  player's own session step; its home map names the map it is on. On the same
  map a follower moves to the arrival with the player. Its T0 body (commands,
  visuals and frames from its home map) is kept for the rest of the run, so it is
  drawn, lit and picked on any map. Its frames join the shown map's frame
  repository as visiting frames. Waiting companions stay where they stand.
- **Saves.** F7 records the companions' cells. F8 brings them back from the
  session.

Native smoke (`ARC-WORLD-037 native-smoke follow PASS`, before the transfer
smoke; `NONE` when the map has no living drawn speaker with follow facts, as
`crash site cave`, whose only speaker is dead at T0). A drawn, living speaker
with facts joins through the session's forced join, as a script follow does; the dialog's `JO` uses the same session party, and
dialogs that offer a `JO` from their start are rare (see the dialog census). It
follows the player placed 6 cells away (on a cell it can walk to) to within 2
and is drawn there,
unless a roof over its cell hides it (the player outside the building).
The transfer smoke then carries it (`ARC-WORLD-037 native-smoke carry PASS`:
it stands with the player on the destination and is drawn there, or hidden by a
roof). A companion is covered by a roof at the cell it stands on, not at its T0
tile. The jump-point smoke checks the same on a same-map jump. Observed on
`bates mansion lev 2` → `bates mansion lev 1` (script 1160, level 35, then F8
back to lev 2 with the companion) and on a `tulla interior` jump point
(437,214 → 304,219, script 1982).

- **Talking to a visitor** (ARC-WORLD-048). A companion standing on a map it
  is not a speaker of (from another map, or on the main map from another
  window) is talked to where it stands: a click, the hover action and the
  walk-to-talk reach it as any speaker, and the conversation runs through the
  shown map's one host (ARC-DEV-081, MIG5: the host lists the companion as one
  more speaker, `AreaConversationHost.AdmitVisitor`; a speaker goes only to the
  host that lists it, since its index means something only there; the viewer
  self-test routes its stand-in row itself) over its home map's dialog
  state: its dialog slot, local flags and follow keys are its home map's, its
  party commands (`JO`, `WA`, `UW`, `LV`) go to the session party with its
  facts, so the talk is the same wherever it stands and one identity keeps one
  state, as the original keeps one object
  ([OL31–OL33](../research/companion-orders-loyalty-evidence.md#7-followers-across-maps-q7-companion-o7)).
  After the conversation the companions are recorded, then the group is
  rebuilt, so it stays where it stands. A negative `IN` goes to the session by
  the companion's entity: it moves the item when its seed holder is on the hero's
  map and is refused (a rejected command, `UnknownHolder`) when it is on another. Its dialog script runs on its home-map
  slot, its quest awards and script sounds are taken from its host like the
  shown map's, and a deferred call whose attachee it is runs through the same
  host. A home-map dialog state the session cannot read (a damaged save) makes
  it not talkable, with a notice, never a crash. The native smoke talks to the
  carried companion on the destination
  (`ARC-WORLD-048 native-smoke visitor-talk PASS`, after the carry; observed on
  `bates mansion lev 1` and on the main map, script 1160 from lev 2, whose
  dialog script line 0 is not admitted yet, `EntryNotAdmitted`, as at home).
  Since ARC-DEV-087 (MIG6e) the talk is followed (with `--hero`) by `ARC-DEV-087 native-smoke visitor-give`: the dialog's negative `IN`
  takes an item from the carried companion's list on its home map's seed, the hero's list holds it with the home map as origin, and the
  hero gives it back.
  Since ARC-DEV-065 a fight there with the AI on (with `--hero`) follows the
  talk: the AI host keeps the visitor out of its rules
  (`ARC-DEV-065 native-smoke visitor-fight PASS`, [AI host](../world/ai-host.md)).

Since ARC-DEV-079 (MIG4) a companion's body is read from its home map's own data by identity ([bodies and window
homes](../world/record-resolver.md#bodies-and-window-homes-arc-dev-079-mig4)): after a quick load or a fresh run on a
foreign map, a companion recorded on a loaded cell of the shown map is drawn, picked and talked to there, and follows;
its body and frames are read from the home map (a window of the main map around the stored cell the transfer recorded
as `npc.home`), and the viewer keeps no body of its own. A body that cannot be read (a companion that left before the
stored cell was recorded, with a window home; an unreadable map) is named on the HUD with the reason, never guessed
(`ARC-DEV-079 native-smoke visitor-body PASS` rebuilds the directory after the carry and checks body, frames, following
and a fresh resolver's record). Not covered: a follower that is not a companion of
the shown map (no body, or its cell not admitted) is not carried and stays
recorded where it was. There are
no followers of followers, unconscious followers, carried inventory, orders,
loyalty or wait timer. The exterior keeps its measured roster (Virgil).

## Same-tile draw order (ARC-DEV-173)

The owner's report (an automated map smoke at Shrouded Hills, zoom 2): a bathtub inside a building was drawn over the building's near wall, the
wall of the sides a viewer looks at. The tub and the wall stand on **one tile**, and the plan orders the bodies of one tile by their same-cell
ranks, which came from each body's own offsets: a wall has none, so it ranked (20, -20) and a tub with a 2-pixel offset (19, -19) came after it.

The original ranks a wall (type 0) or a portal (type 1) from a fixed offset instead, whatever offsets it has: (0, 19) for the ART rotations 2 to 5,
(0, -20) for every other rotation (ledger [SO2, SO3](../research/ledger/world.md#same-tile-draw-order-arc-dev-173)). So the walls of a building's
near sides rank (38, -2) and are drawn after everything on their tile, and the walls of its far sides rank (0, -40) and are drawn before it. The plan
takes the ranks from `AreaScenePlan.BodyRanks` (the art rotation of the original's 0x502880 is `AreaScenePlan.ArtRotation`); every other body keeps
`SameCellRanks` of its offsets. Walls and portals of the Crash Site cave, which has no object offsets, keep their order unless a rotation says
otherwise.

Evidence: `AreaScenePlanTests` (the ranks by rotation and type; a scenery with a 2-pixel offset on a wall's tile comes after a far-side wall and
before a near-side one, whatever the file order); real-data `AreaMapLoadOriginalTests.The_tub_on_the_near_wall_tile_of_the_shrouded_hills_building_is_drawn_before_that_wall`
(opt-in, the town sector (1409, 1314): the building's tub is drawn before the near-side wall on its tile, whose rank is (38, -2)); the map smoke
stages that draw the town draw the same order, and the owner's view is the one shown by the wall check capture. NOT VERIFIED: the original's
explicit wall-before-portal rule of one tile (the plan orders them by rank, then by file order), and any pixel comparison with the original's own picture.

## Doors and windows (ARC-WORLD-051)

Doors and windows keep their state in the session's lock owner
([lock state](../world/lock-state-runtime.md), ARC-WORLD-050). Their rules follow
the executable as read in
[ledger LK1–LK13](../research/ledger/world.md#doors-containers-and-locks-arc-res-065).

- **Frames.** The area owner decodes every frame of a portal ART with more than
  one frame as `PortalFrame` visuals, and the plan copies them into the map's
  repository (`AreaScenePlan.PortalFrames`). A door whose even rotation is 0 or
  6 opens through frames 4..6; one of 2 or 4 opens through frames 1..3. A window
  uses frames 0 and 1. A portal off its T0 frame is drawn like a moved body,
  from its current frame placed by the frame's own hot spot
  (`AreaScenePlan.PortalFrame`).
- **Steps.** The actor's steps and the click route use the portal's current
  state instead of its T0 edge rule (`AreaStepSnapshot.Assess` with
  `AreaPortalPassage`):
  - an open or busted portal passes;
  - otherwise the player's open attempt decides at the session hour: an
    unlocked one passes, a locked one passes only a step leaving a building
    (ledger LV5), and jammed, magically held and always-locked ones block (the
    a lock opens when the hero's key list holds its key, ARC-WORLD-123);
  - an unlock script keeps the T0 rule.
- **Opening by a step.** When a started step crosses a closed portal it may
  pass, the portal gets its own goal run (ARC-DEV-138, MIG13 S7: goal 70 on the
  session's table, `AreaDoorHost`): the first open step comes 5 animation
  milliseconds later and the session's frame follows each step, so the portal is
  passable from the first step; the frames play at 1000 / fps ms each, with the
  portal's open sound. The actor does not wait for the animation. A diagonal step
  opens every passable closed portal on its four legs, the legs the step's
  evidence checks (parity NOT_VERIFIED).
- **Closing by itself (ARC-DEV-138).** After its last open frame the door waits
  1.5 s (goal 72), then tests its own tile: in a turn-based round, or with a living
  player character or NPC standing on it, it waits another 1.5 s and tests
  again; otherwise it waits 1.5 s more and the closing (goal 71) steps it down
  frame by frame with the close sound, blocking again at frame 0. A portal whose
  flag 0x100 (field 46) is set stays open. The runs step on the animation clock,
  so the Esc menu stops them. They are not saved (an open door stays open after a
  load) and NPCs do not open doors
  ([goal table](../architecture/goal-table.md#35-engine-goals-doors-projectiles-eye-candy-gr26-s6s9),
  [the S7 read](../architecture/goal-table.md#the-s7-read-of-the-executable)).
- **Use.** A left press on a door or window uses it, walking within the use
  range (2 cells) first; a press while the actor is mid-step uses it once the step
  has settled, and any other click replaces a pending door use. An open portal closes. A closed one opens when the
  player's open attempt succeeds. A locked, jammed or magically held one plays
  the locked sound and stays closed. One with an unlock script is not opened (its
  script is not run).
- **Pick a lock (ARC-SKILLS-006).** The inventory window's *Pick a lock with:* button on lockpicks closes it and arms
  them; the next left press on a door or window picks its lock ([pick locks](../world/pick-locks-runtime.md)), walking within
  range 1 first; a press anywhere else, a key press, focus loss, a follower walk or attack order, a map change or a reload cancels it. The HUD names the outcome (the lock opens, locked it, the lock jams, the pick
  failed); nothing opens by itself. Full light and no turn-based round are passed.
- **Pick pocket (ARC-SKILLS-010).** The palette's *Pick pocket (then press an NPC)* arms the skill; the next left press on an
  NPC opens the inventory window in steal mode at sight distance, without a walk (*Steal:* and *Plant:* buttons, one attempt each, which
  walks the hero within reach 1 first; no Take all), floats the refusal of a critter that cannot be robbed, or loots a dead one ([pick pocket](../world/pick-pocket-runtime.md));
  a caught thief's window closes after the victim's catching-thief script, the leader test and the AI's attack event. It needs the
  map's AI host; the native smoke `ARC_AREA_MAP_SMOKE_ONLY=pickpocket` runs it with `--hero` (`ARC-SKILLS-010 native-smoke pickpocket PASS`).
- **Saves.** F7/F8 and map changes carry the state, because it is part of the
  session's map changes. A Lock event that comes due locks, unjams and closes
  the portal while its map is shown, and waits while another map is.
- **Not covered.** Maps without a runtime number (not in the map list) keep their
  portals at T0.

Native smoke `door-close` (`ARC-DEV-138 native-smoke door-close PASS`, SM10; in the map battery after the portal stage and alone with `--stages door-close`):
a use opens the first closed door the player can open that keeps no flag 0x100, frame by frame; a second later it still waits to close; five seconds of Esc menu change
nothing; after it the door closes by itself at the original's interval (two waits of 1.5 s plus the closing steps after its last open frame, the pause excluded).
The frame clock is driven with literal host stamps.

Native smoke `npc-door` (`ARC-DEV-209 native-smoke npc-door PASS`, named only, hero, AI on, `--sector 1409,1314`): a free NPC is brought next to a closed openable door and ordered across it; the stage requires the opener goals (11 or 12) on its stack, a run of the door itself, the door open or opening when the NPC enters its cell, the NPC arriving on the far side and the door ending open.

Native smoke (`ARC-WORLD-051 native-smoke portal PASS`, after the talk smoke):
1. The PC stands on a closed, unlocked door's tile and steps across its edge.
   The session then records the door open, and after the animation it is drawn
   on its open frame.
2. A press on the door closes it, and it is drawn on its T0 frame again.
   Bodies drawn in front can hide an open frame at every pixel, so the door cannot be pressed (ashbury haunted castle:
   a tall wall two tiles south of a door). Since ARC-WORLD-061 the smoke closes such a door again by a use, lists it under
   `Hidden` and tries the next door; it fails when all of the first eight are hidden.
3. When the map has a door the player cannot open, a press next to it is
   refused and that door stays closed.
4. Since ARC-SKILLS-006, when the PC holds lockpicks (the halfling-male hero's kit), the inventory's *Pick a lock* button arms
   them and a press on that door picks its lock; the session's lock state follows the outcome
   (`ARC-SKILLS-006 native-smoke pick PASS`; `NONE` without lockpicks or when the refused door is jammed or held but not
   locked).

The smoke prints `NONE` on a map without a door that opens. An opt-in test
(`AreaPortalsOriginalTests`) checks every installed interior: the frames of
every multi-frame door and window are in the map's repository, and a step from
the tile of every closed, unlocked door opens it.

## Animated scenery (ARC-RENDER-035)

Scenery whose ART has several frames (fires, torches, braziers, magic effects) plays them as the original's scenery
animation loop does ([ledger AS1–AS7](../research/ledger/world.md#animated-scenery-arc-render-035), read in the executable).
It is fixed in both rule modes: the frame that stood still was a technical bug of the viewer (the owner's report of
2026-10-29, the fire on the Crash Site wreck at night).

- **Frames.** On a whole map or a main-map window, the area owner decodes every other frame of a scenery ART with more
  than one frame as `SceneryFrame` visuals, unless the scenery has scenery flag 0x1 (no auto-animation). The plan copies
  them into the map's repository (`AreaScenePlan.SceneryFrames`). The pinned Crash Site exterior profile keeps its
  oracle-checked visual set and does not animate.
- **Start.** When the map is shown, each scenery that is not destroyed or off, has no scenery flag 0x1, is not stoned
  (spell flag 0x10000) and has more than one frame starts its loop at its T0 frame (`SceneryAnimationRules.Starts`, AS1–AS3).
  Fires (scenery flag 0x10) loop the same way and burn: since ARC-DEV-144 (MIG13 S9c) goal 77's fire damage (3 fire a tick on the critters on the scenery's tile, once per turn-based round and doubled in a round, `AreaFireBurner`) is modelled; lying items are not.
- **Loop.** Since ARC-DEV-142 (MIG13 S9a) each loop is a run of the session's goal table (goal 1, or 77 for scenery flag 0x10; `AreaSceneryHost`, [the S9 read](../architecture/goal-table.md#the-s9-read-of-the-executable) GT75–GT77): the run steps on the animation clock inside the frame step, so the loops stop behind the Esc menu. One step every 1000 / fps ms, rounded up to the frame tick that finds it due (the session's rule for every run: about 133 ms at 60 frames a second for 125 ms); after the last frame comes
  frame 0. The loop steps only while the PC is within 30 tiles (largest coordinate difference). The distance is measured
  again on every third check, and a loop away from the PC checks again every 800 ms (AS4, AS5, the nodes of `ExecutorSceneryGoals`). A frame's
  non-zero delta would move the object's offsets (AS6); no installed animated scenery has one.
- **Drawing.** A scenery off its T0 frame is drawn like a moved body, from its current frame placed by the frame's own
  hot spot (`AreaScenePlan.SceneryFrame`); a busted scenery's ground body wins. A frame keeps the body's draw-order key, so
  it takes its T0 place in the upright order without an insertion (`AreaUprightOrder.With`). A window move or a map change
  ends the runs of the old loops and restarts the new plan's at their T0 frames.
- **Census.** 1,063 animated scenery on 60 of the 80 interiors, 10 to 30 frames at 8 to 15 frames per second; the Crash
  Site window holds 4 of them (76 more frames). Void needs 4,678 frame requests (116 more), within the repository's 16,384.
- **Not modelled.** The loop's ambient sound, restarting a loop when a scenery's ART or off flag
  changes at runtime (`AreaSceneryHost.Restart` is the rule of 0x43F710, GT78; the viewer changes neither, so no site calls it), the HD pack's frames (an HD pack draws the frames it holds). A table-exhaustion sweep (216 slots, every run at level 3) ends the loops for good, as in the original: they start again only when the window is shown again (GT98).

Native smoke (`ARC-RENDER-035 native-smoke scenery animation PASS`, after the light animation stage): the stage takes the shown map's loops with their runs, takes the animated scenery nearest the PC within 30 tiles that the canvas draws (of the four nearest) and ticks the frame clock (50 ms host stamps) until its run has stepped four more times; after each
step the canvas must draw one of that scenery's frames, three distinct frames in all. It prints `SKIP` on a map without
animated scenery drawn near the PC.

## The main world map (ARC-RENDER-024)

The 2000 × 2000-sector start map (`Arcanum1-024-fixed`, catalogue 5000, runtime 1; 621 sector files, the rest
generated from terrain templates, ARC-DATA-031) is too large to load whole, so map mode loads it as a
[window](../world/whole-map-plans.md#window-plans-arc-data-032): 3×3 core sectors around a centre and their context
ring (25 sectors), with the [window step evidence](../world/area-step-assessment.md#windows-arc-data-034), window
lighting and stationary lights (ARC-DATA-035), its transitions and its dialog NPCs, through the same load path as any
map.

- **Where it opens.** A map with more than `AreaMapPlans.MaxSectorFiles` (256) sector files opens as a window around
  the sector of a transfer's or quick load's arrival, else around `--area-map-sector X,Y` (launcher
  `./arcanum map NAME --sector X,Y`), else around the sector of its map-list start tile. A transfer or quick load to
  another map drops the opening sector, so the destination opens at its own arrival. That start tile is the new-game start: row 5000 of
  `rules/maplist.mes` puts it at (92958, 82592), the Crash Site sector (1452, 1290); `gameinit.mes` names no start map
  or location (both entries are empty and marked unused). The actor stands on the arrival when admitted; with a sector
  asked, on the open core tile nearest that sector's middle (a sector without open ground of its own, such as the
  generated mountains at (1450, 1290), gives a tile of a neighbour); otherwise on the start tile when it can stand
  there, else on the first open core tile. With a sector asked on
  a map that loads whole, the actor stands near that sector the same way.
- **Streaming.** When the actor stands 16 cells (`StreamMargin`) beyond the window's centre sector, the window around
  the sector it stands in loads in the background (about 5 s on the owner's Mac) while the shown window stays drawn and
  playable. Once loaded it replaces the shown window in one publication, but not while the shown scene holds an intent a
  new scene would drop or a state that must not move: a pending transfer (it applies and supersedes the move), a walk
  to use, talk or pick up (it runs to its end first), combat mode, or an open dialog, inventory, sheet, logbook or palette;
  no move starts in any of these either. The publication: the game session is carried as on a transfer (`MapTransferHandoff`, kind `Stream`), the
  companions' cells are written to the session first and they are bound again where they stand, the actor stands on
  the cell it reached meanwhile, and a walk keeps its goal when the new window holds it; when it does not (the walk bent
away from the goal and the window followed, ARC-WORLD-042), the walk heads for the end of a partial
[corridor](../world/click-route-runtime.md#corridors-beyond-the-local-reach-arc-world-042) toward the goal over the new
window (its cell nearest the goal) and the goal is handed on to each later move until a window holds it; a new click
replaces it. A click beyond the route follower's 32-cell reach is walked along a corridor over the window. Music and ambient sound go on
  (no map change). The margin keeps a walk along a border, or a path that bends back over it, from reloading the
  window at every step; the actor always has at least 48 loaded cells ahead. The original keeps no fixed window: its
  sector cache holds the 16 most recently used sectors and a teleport pre-caches the 3×3 around its target
  ([main-map streaming](../research/main-map-streaming.md), SS1–SS4); the 3×3 core is that pre-cache kept around the
  actor. A window that cannot load leaves the shown one in place; it is tried again once the actor has stood in the
  centre sector again (another sector is tried at once). A move whose new window no longer holds the actor when it can
  publish (a hold let it walk off that core, or a same-map jump moved it) is given up, and the next frame starts the
  window around where the actor stands. In a native smoke the window moves only in its own stage. A transfer or reload during a window load supersedes
  it (the move gives up its session to the scene, which carries or ends it); F7/F8 wait while a move is pending, loading
  or held ("another map is loading"). A publication that fails publishes the failure as any load does, and the game
  session ends with the scene (no fallback to the shown window).
- **Transitions.** Exits from the window's objects and tiles work as on any map, into interiors and back: the Crash
  Site cave entrance leads to the cave, whose exit leads back onto the main map, which opens around the arrival
  (92913, 82598). Every listed map is a destination.
- **Native smoke** (`--native-smoke` on the main map; `ARC_AREA_MAP_SMOKE_ONLY=map,resources,transfer,roundtrip,stream,worldmap,walk,census,cast,turn-based,ranged,junk,plants,life`
  narrows a run to those stages; without it all but `walk`, `census`, `plants` (ARC-WORLD-081) and `life` (ARC-UI-020's [health and fatigue bars](terrain-viewer.md#health-and-fatigue-bars-arc-ui-020)) run (`cast`, ARC-MAGIC-015's [first cast](../world/spell-cast-host.md#the-viewer-arc-magic-015), and `turn-based`, ARC-COMBAT-020's [round host](../world/turn-based-round-runtime.md#viewer-host-arc-combat-020), are part of the map battery and alone run only themselves; `ranged` alone runs the battery's last combat stages, object attack, critical, line of fire and ARC-COMBAT-015's [ranged shot](../world/hero-attack-runtime.md#ranged-hero-attack-arc-combat-015); `junk` alone runs ARC-WORLD-063's [junk pile](../world/inventory-runtime.md#junk-piles-arc-world-063) stage, the battery's last) ([map census](#map-census-arc-render-027)); `worldmap` is [the world map](#world-map-arc-ui-048), after the stream), in this order: the map battery at the new-game start (the Crash Site window: walk,
  logbook, sound, dialog with Virgil, who then follows); the resource sweep and reload; the companion follow smoke and
  the map transfer smoke (exit to another map, jump point, F7/F8); `ARC-RENDER-024 native-smoke round-trip PASS`: into
  the cave and back onto the main map by the two exits (`round-trip NONE` where the window holds no usable exit to
  another map, as at the generated wilderness sector (1384, 239)); and last `ARC-RENDER-024 native-smoke stream PASS`: walks by the
  production route follower into sectors the actor has not stood in until the window has moved three times, each move
  with the session carried (and Virgil following), the actor inside the new core and the walk's goal reached, the first
  one met mid-walk. With `ARC_AREA_STREAM_AI=1` (ARC-DEV-119, MIG9 S5c) the stream run also keeps the NPCs' AI on, starting them calm,
  and requires the one AI host built for the map to be the shown host after the moves, rebound once per move (the `Ai` object of the PASS line: builds, rebinds, NPCs known). The map battery and the stream run with `--area-map-sector` at a town (1409, 1314) and a generated
  wilderness sector (1384, 239). The smoke counts only the plan's own commands in the resource sweep (a companion
  visiting from another window is drawn from its kept body, a junk pile or placed item under its own key) and picks, for the follow smoke, a free NPC that can walk
  6 cells.
- **Walk smoke** (ARC-WORLD-042; the stage `walk`, run only when named: `ARC_AREA_MAP_SMOKE_ONLY=walk`, on the main
  map from the new-game start): plans the walk to the middle of the Shrouded Hills town sector (90208, 84128) over the
  map's windows in the background ([window route](../world/click-route-runtime.md#world-routes-across-windows-arc-world-042)),
  then walks it with the production route follower, which it gives the farthest route cell of the shown window each time
  the follower arrives or the window moves, while the window streams. A smoke frame is one second of walk; while a window
  loads, one frame a second of wall time. It requires no seam: the follower never stops short (a denied step, no route
  or a partial corridor fails the smoke at once), the actor's cell changes by no more than a second's run a frame
  (6 steps), every window move publishes the actor on the cell it stood on, inside the new core and drawn, with the
  session carried, no window fails to load and no move is given up (a counter of abandoned or superseded moves stays
  0). Named on another map, the stage fails. It reports its slowest frame (the walk, its route plans with corridors and
  the steering on the main thread). `ARC-WORLD-042 native-smoke walk PASS` on the owner's Mac (2026-10-21, on main with
  #345): route planned in 225 s (3,106 cells, 88 sectors, 51 window loads, 143,401 expansions), walked in 2,330 frames
  with at most 2 steps a frame and a slowest frame of 188 ms, 72 window moves (loads up to 8.8 s), each resumed with
  its goal held by the new window (the far-goal hand-over was not needed on this walk; unit tests cover it), at least
  179 visible bodies after each move. Managed heap after a full collection: 265 MB at the start and 413 MB at the end
  in the Shrouded Hills window (observed values, not a growth measure; the stream smoke's windows take 368 to 435 MB).
  **Sample by default (ARC-DEV-395):** without `--all` the stage plans and walks only to a cell of that route (route cell 700 of
  3,106, measured at 10 window moves against 71 for the whole route, so the "72" above counts one more; planned in 11 s, 477 frames, 63 s of slot instead of 363 s on an idle Mac, 525 s in the speed diagnosis); `./arcanum smoke pr
  --stages walk --all` is the full walk above and logs which route cell the sample goal is. What the sample no longer proves:
  the tail of the walk (the window moves after the tenth, the arrival at the town) and the planner on the whole route.
- **Inventory of a window (ARC-COMBAT-024).** A window's inventory seed lists only the window's objects and is marked a
  window seed ([inventory](../world/inventory-runtime.md#model)): an item the hero took where the new window does not reach
  no longer refuses the seed. Before, such a move (the owner's report: armour taken at the Crash Site, the window moved two
  sectors south, published as the hero died) left the scene without its inventory and combat hosts, so killed NPCs stood up,
  the hero was drawn without its armour and nothing could be looted or attacked; a refused attach now prints
  `ARC-UI-004 inventory-attach refused`. Native check (stage `dead-window`, only when named, from the map's opening window,
  with `--hero`; it leaves the hero dead): the hero takes an item one sector behind the centre, the window moves twice the
  other way, the second time while the hero is dead and without the item's sector; the map keeps its inventory and combat,
  an NPC killed before is drawn fallen and the hero keeps its armoured body: `ARC-COMBAT-024 native-smoke dead-window PASS`
  (half-ogre and human male, 2026-10-29: windows (1452,1290) → (1452,1291) → (1452,1292), the item from (1452,1289)).
- **Former follower goes home (ARC-DEV-116, MIG11 S5).** Only when named (`--stages follower-home`, with a hero): the transfer stage's carry of the
  joined companion (Virgil) to the Crash Site cave, then the hero releases it there; the forced waypoint or standpoint pass the cave's sector fold would
  run (driven here by the AI host's own steps) finds a leaderless NPC whose teleport map (the map the carry took it from, FM6) is another map and asks for its
  change of place on that map; the frame's ping carries it out: its placement is its home map's again, the cave's group no longer holds it, the hero
  stays and the request slot is empty: `ARC-DEV-116 native-smoke follower-home PASS` ([change of place](../architecture/change-of-place.md#6-what-the-session-and-viewer-do-for-the-homecoming-of-former-followers-s5)).
  ARC-DEV-351 (TG9): the stage also checks the teleported mark (the original's OF_TELEPORTED) at both ends: the carried companion's mark was taken off by the cave's open and the open notified it on the arrival cell, and the one sent home
  left its mark on its home map; then (`RunFormerFollowerReturnSmokeAsync`) the hero walks to a cave exit that leads there and the open of the home map takes the mark off and notifies the former follower on its own cell
  (`ARC-DEV-351 teleported ...`, the hero's arrival cell is notified too: `ARC-DEV-351 hero arrival tile ...`): `ARC-DEV-351 native-smoke follower-return PASS`. The quick-load tail of the transfer stage is skipped after this return trip (the hero is back on the origin map).
- **Follower across a window move and a quick load (ARC-DEV-170, B05, SM01).** Only when named (`--stages follower-far`, with a hero and `--debug-mode`): the follow smoke's join of the
  start map's speaker, the debug travel's Teleport to the farthest area (the session carried), then F7, F8: after each the companion is in the party, in the shown map's group, drawn
  and within the follow distance of the hero after 100 frames: `ARC-DEV-170 native-smoke follower-far PASS`.
- **Items the hero took and made across a window move and a quick load (ARC-DEV-305, B01, B36, SM02).** Only when named (`--stages inv-carry`, with a hero and `--debug-mode`, at the Crash Site): the hero takes up to six items from the first dead body that holds any, and an item the session makes (a copy of the kit's first wearable laid on the ground next to the hero and picked up: a `made:` item with no map row) is worn through the window's own wear step, which needs the host to read its wear facts from its prototype (the stage fails with "the made item has wear facts (B36)" without that). The multiset of (prototype, units, place) of everything the hero holds is recorded, then the debug travel's Teleport to the farthest area, then F7, F8: after each step it is equal, the worn slot count is unchanged, the inventory window opens and lists every held item, and the made item is still held and still has its wear facts: `ARC-DEV-305 native-smoke inv-carry PASS`. ARC-DEV-314 added the plan's step 5, the report's own map change: the window moves to the Shrouded Hills town, an exit object there is used into the Panarii temple map (`shrouded hills-panarii temple lev 1`), F7 and F8 are done inside it, and the exit back is used; the same checks run after each, and the window's text of every held item (its name and weight) is compared as well. This reproduced B01: after a window move the window listed "[unknown object]" for the items the hero took in the old window, because a window of the main map is runtime map 1 each time, so the next inventory host held the new window's rows only and the old window's rows (which name an item and give its weight) were lost with the old host. Fixed in the source: the host hands the rows and weights of the items the hero holds to the next host (`AreaInventoryHost.HeldRows`, `Retain`, `AreaHeldRow`), wired in `TerrainViewerAreaInventory.cs`; unit tests `AreaInventoryHostHeldRowsTests`.
- **A running animal killed by a spell (ARC-DEV-248, B13, SM11).** Only when named (`--stages critter-fall`, with a hero, AI on): the hero learns a damaging college spell (the cast smoke's learning, now shared), the weakest animal that can be
  seen from seven to nine cells is cut to one hit point and told of the hero's attack, and while it is running (its mover stepping) the hero casts the spell at its drawn body through the pane, the cursor and the click. From the first frame the session
  says it is dead the stage samples the animal's mover and drawn body for three seconds of game time: the animal's mover was in the middle of a step when the session said it was dead, the drawn cell is the mover's cell in every sample (the mover is where the drawn body, the session and the pool of blood take their cell from, so the weight of the check is that the mover does not go on), the body lay on at most the cell it was entering and the one it stood on, it settled within 0.4 s
  and stayed on one cell, the session's place is that cell and the pool of blood the dying animation leaves (a new pool of that animal, not an earlier stage's) is on it: `ARC-DEV-248 native-smoke critter-fall PASS`. ARC-DEV-293: every animal starts calm and is calmed again at the end, because an earlier stage of the run, such as `ai-cast`, leaves its victim in a fight with the hero and an animal already fighting runs before the cast; and the click waits for the start of the animal's next step and one frame after it, because the AI's step rounds keep the phase of the game time the run has reached and the spell kills a fixed 150 ms after the click, so without the wait the death could fall between two steps.
  B13's report (the body flies toward the hero and the pool stays halfway) is not seen on `main`. A spell that is resisted fails the stage (the animal has one hit point left, so any damage kills it).
- **Buttons on a joined companion (ARC-DEV-263, B33, SM24).** Only when named (`--stages companion-click --hero human-male`, AI off, the start map): the first free speaker of the map joins (Virgil) and comes up to the hero, and the stage judges the mouse by the original's mapping (executable read, ledger OL34): a right press alone on his portrait opens no menu, the right release opens the order menu and a second right click closes it; with the menu open a left click on the portrait closes it and starts no talk; with it closed the left click opens no menu and talks to him (the hero's talk goal opens the conversation with him); a left press on his body talks to him; a right click on his body opens no menu and starts no talk (a guard: the world's right press has no action of its own, as in the original, where the right release only turns the hero); a right release in the world, a right click on a menu item and a left click on an item that is not modelled close the open menu. The presses on the portrait go through the viewer's GUI (a pushed mouse event), those on the body through the world's input. The stage prints the 13 facts, then fails if any differs from the original's: `ARC-DEV-263 native-smoke companion-click PASS`. It failed on the portrait code before ARC-DEV-280 (the menu opened on the left button).
- **A real grenade thrown from the pack (ARC-DEV-249, B14, SM12).** Only when named (`--stages grenade --map "crash site cave" --hero human-male`, with a hero; not ALONE): the stage takes a map item whose spell carries the attacks-a-location trigger (0x800000), the damaging one before a summoning one, picks it up from its container, lifts it onto the game cursor and throws it at the tile of a living NPC that has another within two cells, from a cell three to six cells away with a clear line. At the flight's end the item's event must start the spell (a run), the spell's damage must land on (or be resisted by) one of the two NPCs near the aimed tile (the hero does not count), and the grenade must be gone (not placed, not in the pack, destroyed): `ARC-DEV-249 native-smoke grenade PASS`. The landing tile, the aimed tile and the number of creatures hurt are printed, not judged (a run landed three cells beyond the aimed tile and hurt one NPC).
- **An aggressive spell cast from afar (ARC-DEV-264, B08, SM06).** Only when named (`--stages ai-react --hero human-male`, with a hero, AI on, the Crash Site; ALONE): the data's weakest aggressive single-target damage spell with a missile (spell 16 in the installed data) is lent to the kit weapon and cast through the weapon's pane, the cursor and the click at a calm animal from the longest distance the spell's `Range:`, the screen and a standable cell allow (16, 14, ... down to 4 cells off along an axis or a diagonal; for each distance the first of the twelve animals with the most hit points that the hero can stand that far from and see). The stage runs on the map it is given (the Crash Site is the plan's). A resisted spell or a spell that leaves the animal not active prints `NONE`. At the hit the animal must be in COMBAT_FOCUS on the hero (the notice of an aggressive spell runs the attack event before the damage, whatever the distance), and within ten seconds of game time (the original's bound is five between ticks and ten after a fold's wake-up; the stage allows ten as a margin and prints when the animal moved) it must come two or more cells toward the hero or attack: `ARC-DEV-264 native-smoke ai-react PASS`. Beyond 30 tiles of the player the original's tick does nothing and the animal stands still; the screen shows no animal that far, so that case is not tested ([hostility 7.7](../research/npc-ai-hostility.md#77-a-spell-hit-arc-dev-264-read-on-2026-11-08-bug-b08)).
- **A critter's spell runs end with it (ARC-DEV-301, triage T1).** Only when named (`--stages spell-end --hero human-male`, with a hero, AI on, the Crash Site; ALONE): the highest-level NPC with magick levels is told of the hero's attack and casts, through the AI host's perform, a timed spell whose BEGIN adds an eye candy on the target (the entangle, spell 51, first); the stage waits for the run to begin and for the eye candy entries on the hero. Then (1) a lethal packet through the session's life step kills the NPC: the run it is the parent of must end (the kill 0x45DA20 calls 0x459740) and every eye candy entry the run added must be removed from the hero; (2) the NPC is raised and casts again, a fatigue packet knocks it out (the fatigue setter 0x45D5A0 calls the demaintain 0x457450), and the run it is the source of must end with its eye candy removed again: `ARC-DEV-301 native-smoke spell-end PASS`. The entangle's hold flag is not written yet (MT13), so the hold is not tested. The NPC and the hero are restored for the later stages.
- **The hold spells hold the hero (ARC-DEV-304, B11, SM09).** Only when named (`--stages hold-hero --hero human-male`, with a hero, the Crash Site; ALONE): with the hero walking to a cell at least eight cells away, a script with no caster (a cast with the hero as parent would play a casting goal that replaces the walk and fails the willpower check, RN5) casts stone (spell 127), paralysis (113) and entangle (140) in turn, and the stage reads the hold flags, the walk run and the hero's cell. Stone interrupts nothing: the walk run stays, the hero stops after the step it is on, a press is dropped, and the walk resumes to its old target after the END (GT146, GT150). Paralysis ends the walk run at once, holds the hero the same way and a press after the END walks again. Entangle is run twice, once per rule mode: the mode is switched after the hold has begun (the cast path reads no switch; only the step start does), and the hero can still act and the click walk (a goal-3 run) is ended at once in both (GT153), so the hero stands within a step and a new walk fails at its plan (GT149); the D8 difference (Original keeps the steps of a goal 4, 55 or 56 run) needs such a run, which this stage does not produce, so it is not exercised. The pass line is `ARC-DEV-304 native-smoke hold-hero PASS`. The hero and the runs are restored for the later stages.
- **Spirit of the dead (ARC-DEV-304, B24, SM26).** Only when named (`--stages spirit-talk --hero human-male`, with a hero, AI on, the Crash Site; ALONE): the stage kills the first dialog NPC that has a standable cell two to four cells from it (the lethal packet through the session's life step), shows that the click loots the body (goal 81) and the talk click does nothing, casts spell 56 at it by a script with no caster, and requires the spoken-with-dead spell flag (0x2000000) on the body, the body counted as a present speaker, the click as a talk (goal 21) and the dialog starting with the corpse: the dialog start must be `Started` (the window opens) for an NPC whose script file has the death-speech flag 0x8 (the stage prefers one) and `GenericGreeting` (run default, SU15) for one without, which opens **no window** and floats the corpse's dead line, an NPC line 1500..1599, over it (ARC-DEV-324, GT220–GT222; the stage requires the queued line). It then ends the run: the flag is off, the body is no speaker, the click loots again (GT216–GT218). The pass line is `ARC-DEV-304 native-smoke spirit-talk PASS`. The NPC and the hero are restored for the later stages.
- **Save and resume (ARC-DEV-325, B10, SM30).** Only when named (`--stages save-resume --hero human-male`, with a hero, AI on, the Crash Site; ALONE): the hero holds a maintained spell; the weakest animal of the wreck (a wolf) is told of the hero's attack and runs at him from seven or more cells; F7 saves in the middle of one of its steps after at least two steps; the live run goes on twelve frames; F8 loads. After the load the spell holds its slot of the maintain bar with its tick queued, the animal stands on its saved cell, its goal run is in the table under its handle, it is still in combat focus and runs on to the hero (nearer than at the save, down to the cell next to him). Parity with the original runtime is not verified.
- **The map's NPC rows against the AI host (ARC-DEV-308, B07, SM07).** Only when named (`--stages placement-census --hero human-male`, with a hero, AI on, the Crash Site; ALONE; read-only, it changes nothing in the session): the session's residency (every mobile row of the open map, MIG9) gives the NPC rows within ten sectors of the hero's sector, minus the off and destroyed ones (RL9); the AI host's NPCs are read before the AI's first tick (only the few frames that place the hero have run). Pass: rows exist near the hero and the host holds some, every NPC the host holds stands on its row's cell, none lacks a row (a host cannot hold one NPC twice: it refuses duplicates when built) (an off or destroyed row the host holds is printed as `HELD-OFF`, not judged), and every row in the hero's sector and the eight around it (the loaded core) is held by the host; the rows farther out are printed by distance in sectors (`byDistance=D:held/rows`) and are held only as far as the window loads. Then F7 and F8 on the same map: the same census must hold on the session the load published (no tick ran, so the places are the rows' places). Counts and entity prefixes only are printed. First run on `main` (2026-11-08): 45 rows, 42 held on their row cells (the three not held are three sectors out), one off row held, the same after the load.
- **An NPC archer's arrow (ARC-DEV-282, PF1–PF3).** Only when named (`--stages npc-shot --sector 1409,1314`, the town, with a hero; not ALONE): the stage takes a free NPC of the map whose worn weapon has a projectile art and reaches beyond one tile (a real archer of the installed data, no lent weapon), stands the hero four to six cells from it in sight, tells the NPC of the hero's attack and runs the frames until the archer shoots. It requires the launch of the arrow as a projectile of its own, the end of the flight with nothing left in flight (the session holds no shot), and the damage: a shot decided as a hit that reached the hero hurts it at the strike, a miss does not hurt the hero. NONE on a map without such an NPC (the main map's Crash Site area, the wild sector and the elven ruins have none). The hero's life is put back at the end.
- **A damage spell on several animals (ARC-DEV-250, B12, SM08).** Only when named (`--stages cast-multi --map "elven ruins" --hero elf-female`, with a hero, AI on; the Crash Site has no three animals within two cells of one another, and the stage prints `NONE` there): the data's lowest-rank damage spell with a missile and an object-and-radius area (spell 22, radius 2) is lent to the kit weapon (the pre-made hero cannot learn it) and cast through the weapon's pane, the cursor and the click at one of three animals that stand within that radius of it. The AI moves the animals, and the stage requires that at least one has moved when the missile lands; if fewer than two of the three stand within the radius of the click's tile at the hit it prints `NONE` (not a pass). The stage records their cells at the click and at the first Damaged or Resisted event and then reads the cast host's log: no animal is hit twice, every animal that is hit has an eye-candy effect added on it (the effect is added on the object, so it follows the animal's cell; the stage reads the cast host's log, not a drawn frame) and one that is not hit has none, the target is hit once, and at least two of the three are hit: `ARC-DEV-250 native-smoke cast-multi PASS`.
  The area is gathered around the **tile of the click**, with the animals' cells at the hit deciding who stands in it, so an animal that left the radius between the click and the hit is not hit (in the run, the third animal went from two cells to three). That is the original's rule, read in the executable (ARC-DEV-327, [ledger RC1–RC5](../research/ledger/spell-runs.md#centre-of-a-radius-cast-arc-dev-327)): the cursor measures a radius spell's reach from the caster, so a body farther away than the radius (the stage places the hero three or four cells from it, the radius is two) is cast at as its own tile and the centre stays at that tile until the missile lands; a body within the radius of the caster stays the target and the centre follows it to where it stands at the hit (pinned by a unit test, not by this stage).
- **Barrel routes (ARC-DEV-247, B43, B44, SM17).** Only when named (`--stages route-barrel`, with a hero; the plan's map is `--map "crash site cave"`): the click on each of up to three barrels of the map
  (a container whose shown name has "barrel" in it) from up to eight start cells spread over the ground the arrival cell reaches over Open steps. The stage places the hero, hovers and presses the barrel and watches every
  committed cell of the walk at fifty frames a second (the stock frame, a tenth, spans two cells): each cell is one the map admits, each step between two committed cells is Open over the route source of that session
  at the frame that committed it, the loot window opens, the hero ends within the container reach of the barrel, and the walk took at most the shortest Open route to a cell next to the barrel plus two steps:
  `ARC-DEV-247 native-smoke route-barrel PASS`. B43: reproduction not seen on `main` (24 walks over the three barrels of the cave in the first run). The check uses the route's own step source as the evidence, so it catches routes, approach cells and walks the source rejects, not a step the source wrongly calls Open. B44 (the room beyond the cave's walls at the exit) is a
  question of the original's map and stays open: the stage prints the ground reachable from the arrival cell (its count and extent) as the state to compare with the original.
- **The inventory window's arrange button (ARC-DEV-339, B54).** Only when named (`--stages inv-arrange`, with a hero): one of the hero's grid items is moved to the
  last free cell, the own window in the game's art is opened and its arrange button (art 340, 28 × 27, at (763, 197)) is pressed and released three times with
  the window's own pointer path: each press plays 3000 and its release 3001, the first lays the grid out row by row (the tallest, widest item at cell 0, the same
  items, every item inside the grid, no shared cell, one session revision), the second column by column, the third gives the first layout again:
  `ARC-DEV-339 native-smoke inv-arrange PASS`. NONE without a hero, with fewer than two grid items or when the window's art did not read.
- **Loot of a dead NPC's worn items (ARC-DEV-202, B03, SM03).** Only when named (`--stages loot-worn`, with a hero): an NPC of the map that wears an item (a dead one, else
  the first living one in entity order, killed through the session's life domain) is looted item by item through the host, as Take all does: the worn items move to the hero's list,
  nothing the hero wears changes, and the body's list loses them (gold, keys and ammunition are other rules): `ARC-DEV-202 native-smoke loot-worn PASS`. NONE on a map where no NPC wears an item.
- **Take all without room (ARC-DEV-333, B48, triage F2).** Only when named (`--stages loot-room`, with a hero): a dead NPC of the map with at least two plain items (no script, no gold,
  key or ammunition; else the first living one that is no follower, killed through the session's life domain) is looted with the window's Take all. The session's art hook sizes the items
  as 10-wide frames as high as the hero's free rows allow (so some but not all fit; the viewer's own hook is attached again at the end). The take moves the items that fit and leaves the
  rest in the body with the status "no free room in the grid", no item of the hero is held outside the grid, a second Take all changes nothing, and once the hook gives the items one cell
  each the next Take all takes more: `ARC-DEV-333 native-smoke loot-room PASS`. NONE without a body of two plain items or when the hero's free rows cannot split them.
- **Route NPCs walk (ARC-DEV-170, B06, SM05).** Only when named (`--stages npc-resident`, with a hero, AI on, a town: `--sector 1409,1314`): after 30 s of game time (the fold's forced
  placement) the route NPCs within 24 cells of the hero are recorded; after 90 s at least one stands 3 or more cells from its first cell through the walks the AI host started (unforced
  waypoint and standpoint steps, goal 7); F7, 20 s, F8 leave every recorded NPC on its saved cell: `ARC-DEV-170 native-smoke npc-resident PASS`.
- **Tile scripts for AI-host walkers (ARC-DEV-206, B25, GR12, SM28).** Only when named (`--stages npc-tile`, with a hero, AI on, the altar's window: `--sector 1452,1291`): every cell an
  NPC the AI host moves is placed on (a walk step, a knockback slide, a route relocation) reaches the viewer as an `Entered` notice, and the shown map's tile script for that cell runs with
  the NPC as the triggerer, as a following companion's does (`RunCompanionTileScripts`). The stage takes the NPC named 6627 (the Molochean agent) of the window; the tiles of script 1724 (global flag 2323 set, the triggerer named 6627, then destroy) are its exit. It orders it a walk onto another such tile with the flag clear (the script runs and leaves it) and with the flag set (the
  script runs and destroys it): `ARC-DEV-206 native-smoke npc-tile PASS`. Notices wait while a conversation is open and run after it; an NPC gone or dead when its notice is read runs nothing. A follower is not told (the companion drive runs the scripts of a companion of the shown map; a companion carried from another map has none yet); visitors from another map, jumps, teleport
  arrivals and created critters do not notify yet, nor does the wading test or a new sector's script and point 27 (GR12 stays open for them). ARC-DEV-298: a companion the AI host pushes (knockback, `PushSlide`) sends the same notice; its catch-up to the leader and route relocations stay the companion drive's.
- **Tile script float lines (ARC-DEV-298, B20, SM16).** Only when named (`./arcanum smoke map "crash site cave" --stages tile-ghost --hero human-male`, AI on): the hero is put on the first tile of script 2768 and the tile's entry is run as a step does. With the story flag 2672 clear the script returns at line 3 and casts nothing (control); with it set the run goes loop for, cast spell on (line 8), call-ex (line 9) into script 2789, whose float line (23) is read from its DLG and queued for the floater field (`ARC-DEV-298 float`): `ARC-DEV-298 native-smoke tile-ghost PASS`. ARC-DEV-338 (B53, F7) extends the stage: spell 216 has no caster, so its run has no casting goal and begins by the magic-tech event 2 ms after the cast (0x455C30, [GT224](../architecture/goal-table.md)); the stage ticks the area clock until the run's Begin has logged the spoken-with-dead flag and the two eye candies (entries 1298 and 1299, the ghost's picture), then requires the viewer to have posted the cast log, the eye candy to play on the ghost and to be among the moving bodies (`ARC-DEV-338 tile-ghost eye candy shown`).
- **Container clicks (ARC-DEV-299, B23, SM16).** Only when named (`./arcanum smoke pr --stages container-click --sector 1409,1314`, with a hero): each standing container of the window is clicked as the player does (the hero stood beside it, `HoverPointAsync`, `AreaSmokeClick`, frames until the loot window opens). An admitted container that is pickable must open its loot window, every admitted rubbish bin or can (prototype 3075) must open, a locked one must answer `Locked` with no window, a container under a drawn roof pixel is recorded as `ROOF` (RF10: the original picks nothing there) and one with no standable cell within reach as `UNREACHABLE`; neither fails. On the town window: 27 containers, 13 opened, 3 locked refused, 8 roofed, 1 unreachable, rubbish 4/4: `ARC-DEV-299 native-smoke container-click PASS`. **Sample by default (ARC-DEV-395):** without `--all` the stage clicks at most 8 containers (one per group of prototype and admitted outcome, the admitted rubbish bins' group and a locked one first, so at least one bin and not all four; `ARC-DEV-395 container-click SAMPLE n of N` names them) and the figures above are those of `--all` (600 s of slot on an idle Mac, 675 s in the speed diagnosis; the sample takes 129 s). What the sample no longer proves: the click path of the other containers, and the "every rubbish bin opens" rule over the whole window. See [the B23 re-check](../research/open-bugs.md).
- **World time on the frame clocks (ARC-DEV-125, MIG12 S6).** Part of the map stage (after the far click, with a hero and a lighting driver): the
  viewer's world time, which the hero's walk, attack and cast, projectiles, spell missiles, eye candy, the dying animations, scenery and pools read,
  follows the session's frame clocks when the check drives them by literal host stamps. Ten 16 ms frames are 160 world milliseconds, frames under
  5 ms add nothing and then add up, the hero's click route walks on that clock and arrives, a 1920 ms Esc pause stops the world time and the hero without a
  refused observation (the next frame credits 16 ms, not the pause) and the walk resumes, and a time jump of five minutes moves game time and not the world
  time: `ARC-DEV-125 native-smoke frame clock PASS` ([clocks and the frame](../architecture/clocks-frame.md)). The other smokes keep driving their own
  test time; `tools/local_dev/test_world_clock_sync.py` keeps the rule files free of host time.
- **Terrain overview (ARC-RENDER-034).** Beyond the window's 3×3 core the viewer draws a terrain overview: every sector within
  6 of the window's centre (`AreaTerrainOverview.Radius`; 160 sectors at most) is one flat diamond (5,120 × 2,560 px) in the
  colour of its base terrain type, read from the map's terrain grid (`terrain.tdf`) and the type's editor colour in
  `terrain\terrain.mes` (message 100 + type, `R,G,B`, `TerrainEditorColours`; a type without one is grey). It is drawn behind
  everything (`AreaOverviewLayer`, one polygon per sector, redrawn only when a window publishes, unbound when a scene is
  cleared), at full colour whatever the hour (the map's light is not applied to it). Objects, critters and tiles appear only
  inside the loaded window. The overview's bounds become the camera's, and the window's zoom floor is the smallest power of two
  (at least the ordinary 1/8) at which a view centred anywhere in the core stays inside the diamond of the core and the overview
  (`AreaTerrainOverview.Floor`): 1/8 up to 2560 × 1440 (a 20,480 × 11,520 px view reaches past the window onto the overview),
  1/4 at 3840 × 2160. A manual pan can still move the view into the corners of the overview's bounding box outside that diamond,
  which stay empty, as do the parts of the overview cut by the map's edge. The editor colours are data the original's
  terrain initialisation does not read; the overview is a Steamwoven viewer feature (the original has no zoom), so no parity
  applies. A window whose terrain grid or types cannot be read has no overview and keeps the floor below (the load log says
  why: `ARC-RENDER-034 overview unavailable`). Load cost on the owner's Mac (2026-10-21): the grid and colours read in 40 ms
  per window load; the farthest frame at 2560 × 1440 took 15 ms with 269 level-3 chunks and 37,363 visible commands. Native
  check in the map battery: `ARC-RENDER-034 native-smoke overview PASS` (a window must have its overview, whole away from the
  map's edge; at the smoke's window size and at 2560 × 1440 the zoom reaches its floor, the view stays inside the overview's
  bounds, past the window on the large display, every corner and edge midpoint of the view falls on the core or an overview
  sector, a pixel near the view's lower left corner shows its sector's colour, the overview is drawn, and the far frame keeps
  within the census frame budget). Tests: `AreaTerrainOverviewTests` (sectors, colours and the fallback, diamonds, bounds, the
  floor), `TerrainEditorColourTests` (the colour messages).
- **Zoom floor (ARC-RENDER-032).** Without a terrain overview (ARC-RENDER-034, above), on a map loaded as a window the smallest zoom is not the viewer's ordinary 1/8 but
  the smallest power of two (1/2^k), at least 1/8, at which the view is no wider and no taller than the window's drawn
  bounds (`WorldZoomPolicy.WindowFloor`: the viewport over the bounds of the 3×3 core on the tighter axis, rounded up).
  The camera keeps the view inside those bounds, so the view never reaches past what the window loads and draws; at 1/8
  the screen would otherwise cover 25 to 40 sectors on a large display while the window draws 9. A power of two keeps the
  terrain in its exact reduced chunks ([chunks](area-viewer.md), which only such zooms have); at the exact
  floor of a 2560 × 1440 view (0.1875) it would fall back to one item per tile. The floor applies to every way the AREA
  viewer zooms: LT/RT, the palette's 100/200/300 presets and numeric editor, Fit, a window resize, and every scene reset or
  publication (the wheel and `+`/`-` do not zoom there); the zoom preference other maps open at keeps the value asked, and
  a trigger held at the floor leaves it unchanged. A whole map keeps 1/8. The HUD's window status shows the floor. Zoom is
  a Steamwoven viewer feature, the original has none, so no parity applies. Native check in the map battery:
  `ARC-RENDER-032 native-smoke zoom floor PASS` (in the smoke's window and in a 2560 × 1440 one: a window zoomed out to
  1/8 is raised to its floor by the resize, a zoom asked below the floor stops at it, the view lies inside the window's
  bounds with the terrain chunked, and the window size is restored; a whole map reaches 1/8). The JSON reports the texture
  memory, the resident index rows and the first far frame; driver memory per row texture is not measured otherwise. On
  the owner's Mac (2026-10-21) at the Crash Site: 1280 × 800 floor 1/8 (view 10,240 × 6,400, 228 level-3 chunks, 326
  rows, first far frame 13 ms); 2560 × 1440 floor 1/4 (view 10,240 × 5,760, 214 level-2 chunks, 503 rows, 15 ms).
  Between a floor and the next power of two a continuous trigger zoom can still leave the chunks for one item per tile;
  on a large view that needs more nearest-index rows than the renderer allowed (8,192) and made the viewer drop the scene
  (`AREA_NEAREST_MAPPING_BUDGET`), so the row cap is now 65,536, more than a window's commands
  ([nearest sampling](area-nearest-sampling.md)).
- **Memory.** One window holds 1,300 to 4,900 visuals and 10 to 26 MB of CPU frames; after a full collection the
  managed heap of a run that moved the window seven times followed the window's contents (368 to 435 MB), with no
  growth from move to move; the resource stage's reload after the map battery leaves the heap 5.5 MB smaller (since
  ARC-UI-045 released the closed conversation's voice start, which had kept 25 to 30 MB after a dialog).

**NOT READY:** the window is a fixed 3×3 core, not the original's 16-sector cache; a move costs a background load of
the whole window (no sector is reused) and one publication, which rebuilds the scene (a frame hitch at the swap; a step
in progress ends on its committed cell); saved sector differences (`.dif`) are not modelled; the world map's own travel
(the original's world-map screen: see the original look under [World map](#world-map-arc-ui-048)) is not implemented (the diagnostic [world map](#world-map-arc-ui-048) travels by its rules since ARC-UI-048); a companion more than one sector from the actor when the window
moves is bound only if the new window holds its cell; on the same map, an F8 save or a jump point or teleport whose cell
lies outside the shown window is refused (the cell is not admitted in this window; another map's save or exit opens the
window around it); a click is a goal inside the shown window (a goal farther away is reached only by a host that plans
it, as the walk smoke does with the window route); far out, the view shows the terrain overview beyond the window
(ARC-RENDER-034): flat colours by terrain type, sector by sector, no tile detail, objects or critters, and nothing beyond
6 sectors from the window's centre (the camera keeps the view inside). Original parity NOT_VERIFIED.

## World map (ARC-UI-048)

On the start map (runtime map 1, `arcanum1-024-fixed` installed, the map the world map is keyed to) the original key
**W** opens a diagnostic world-map panel over the travel rules of ARC-WORLD-056
([world-map travel rules](../world/world-map-travel-runtime.md)).

- **Opening.** W acts on its logical key down under the guards of C, L and T: not with the palette, a dialog or another
  window open, not under UI focus. The panel is modal for the hero's input: the gate closes and a walk stops, but the clock runs behind it (ARC-DEV-126, MIG12 S7: the original's world map is no pause).
  It refuses on another map ("only on the start map"; in [debug mode](#debug-mode-arc-dev-050) it opens there too), in
  combat mode, while the actor is between cells, and until the module's world-map data have loaded (in the background,
  once per module).
- **What it shows.** It shows the project's own 500 × 500 picture of the start map. The picture is drawn from its terrain:
  one pixel for 4 × 4 sectors, coloured by terrain type, darker where travel is blocked, and brighter where a blocked type
  has its own sector file. On top are the PC, the areas (known ones in yellow), the waypoints and the planned route.
  - A click on the picture adds a waypoint at that pixel's sector. A known area within 320 tiles takes its place.
  - Only in debug mode a list holds every area by its data name; a double-click there adds a waypoint at that area (a
    diagnostic shortcut for clicking it) and Teleport moves the PC there at once. Without debug mode there is no list.
  - A refused segment shows "no route there (the original's message 601)". Clear drops the waypoints.
- **Original look (ARC-UI-073).** Without debug mode, with the game's art and all 64 map tiles read (in the background, beside the data), the
  panel is the original's window ([world map](../ui/original-windows.md#world-map), ledger WM1–WM125): art 138 behind the 501 × 365 view of the
  2000 × 2000 tile picture, opened centred on the party, scrolled by the arrow keys, the compass and Home, with the buttons at their places;
  a press adds a waypoint at the picture pixel under the pointer, or selects the waypoint under it (since ARC-UI-085), Travel (the button or Enter) starts the journey, a right release
  or Delete removes the selected waypoint or the last. The party, the known areas, the waypoints and the route are drawn over the picture. The window sits between the bars at the largest whole factor
  that fits. Debug mode and a run without a game folder keep the diagnostic panel described above. Since ARC-UI-083 the known areas carry their names in the
  original's font, the two boxes read the sector under the pointer, a marker under it writes its description into the message window, the zoom and
  whole-map buttons and Z switch to the continent view (`Map_Zoomed` with a dot for every marker; a click in it zooms in), and art 217 covers the view's
  bottom edge. Since ARC-UI-084 a PC in a sector with a town map (the sector's metadata word, from the sectors the map loaded) is off the walkable world and the window
  opens on the town map (any map, not in debug mode): the tiles of `townmap/<name>/` at the scroll offset, centred on the party marker, fogged by the town's reveal
  (a whole map or a script's reveal shows all; since ARC-WORLD-122 the hero's walk reveals the tile under it in the session, schema 75), the zoom button and Z returning to it. Since ARC-UI-085 (ledger WM95–WM109) the waypoint list follows the original: an armed press on a waypoint (a box of half the 205 art's size plus 5 px,
  the last first) selects it and otherwise adds one, both selecting it and starting a drag; the drag scrolls the view 50 px within 23 px of its edge (also on the 250 ms repeat)
  and never moves the waypoint; the left release re-arms and snaps the waypoint to a known area within 320 tiles; a right release (anywhere in the map view) or Delete removes the selected waypoint,
  or the last; during a journey the last goes and the journey ends. The route is drawn as straight lines from the party through each waypoint's icon, (160, 50, 50), and
  (50, 50, 110) for the segment that ends at the selected one. A middle waypoint deleted leaves its steps in the original's route (WM97): under Original the journey
  still walks them, under Steamwoven the later segments are planned again (the deletion is refused with a note when one has no route; `AreaWorldMap.RemoveWaypoint`,
  `WorldJourney.RemoveWaypoint`, `TravelRuleProfile.DeletedWaypointKeepsSteps`). Since ARC-UI-092 the town map has its waypoints (ledger WM110–WM125; `AreaTownWaypoints`, `AreaTownMapLayout.ToLocation`): an armed press on a waypoint selects it, else adds one at the clicked tile; at most 6 (message 611), a path test from the PC's tile (the first) or the previous waypoint (message 610: the start tile, 32 or more tiles away on an axis, or no path in the PC's own walk plan); there is no drag; the first press of the window works (the original's flaw WM86 is fixed); Delete and the right release remove the selected waypoint or the last; the route is drawn as in the map view; Travel closes the window, keeps the list (until another town, map or session) and walks the PC to the first waypoint as a ground click does, the others stacked as goal-3 walks in the same run of the PC's goal table (ARC-DEV-311, ARC-DEV-322, in both rule modes with no switch): a ground click or the stick overwrites only the last waypoint, a second Travel doubles the route, and any failure or other interrupting action ends the whole run. Native smoke: the
  `worldmap` stage checks the original look opens centred, scrolls by steps, clamps at both corners and centres on Home
  (`ARC-UI-073 native-smoke worldmap-art PASS`), then a marker for each known area (one is found for the check), the hover description and readouts of a
  pointer over one and Z both ways (`ARC-UI-083 native-smoke worldmap-names PASS`), the waypoint list (`ARC-UI-085 native-smoke worldmap-waypoints PASS`: press selects and disarms, a second press adds nothing, the west edge scrolls 50 px, the drag moves nothing, the release re-arms, a right release removes), and the town map's waypoints (`ARC-UI-092 native-smoke worldmap-town-waypoints PASS`: adds through the view's mouse path, selection, the 7th refused with 611, Delete, Travel closes the window and builds one run holding a goal-3 walk per waypoint) and the town map (`ARC-UI-084 native-smoke worldmap-town PASS`, which also checks that the hero's tile is revealed in the session); `ARC_WORLDMAP_SMOKE_SHOT=PNG` (an `--env` of the smoke runner) saves the frame for a local look,
  never for git.
- **Travel.** Travel, Enter or confirm starts the journey. Since ARC-DEV-127 (MIG12 S8) it is a chain of the session's
  real-time events (`travel.step`): the first step falls due 50 ms after the start, each next one 62 ms of real time after
  the step before it (TL28), run by the frame step, so the steps fall due while the map is open, which is no pause: game time flows at 8x behind it as well as in the steps' jumps
  (ARC-DEV-126, MIG12 S7). Escape does not end the journey: on the open map it opens the in-play menu (window 4, the original's, since ARC-DEV-111), and the
  steps go on behind that menu's pause, as the original's real list does (TL24); the journey ends by its landing, W or a controller cancel.
  A frame runs as many steps as fit its admitted real time (at most 250 ms: up to four). A map without lighting has the frame driver of ARC-DEV-124 (`AreaFrameDriver`, the same frame step), so the chain runs there too; a scene
  with no session has no clock to run it and refuses the journey ("no game clock on this map").
  - Each step's game time is a jump of game and animation time together (TL26). The events it made due fire in the same
    frame, right after the step.
  - An area the step discovers becomes known in the session, and the last discovered record is cleared again.
- **Landing.** At the end the panel closes and the start map loads around the landing location, as a transfer does:
  the session is carried and following companions arrive with the PC. W, Escape or cancel during the journey lands
  where it stands, by the setting's cancel rule (A4). Since ARC-DEV-115 (MIG11 S4) the landing is the session's
  [change of place](../architecture/change-of-place.md#5-what-the-session-and-viewer-do-for-the-world-map-landing-and-the-new-game-s4)
  (caller `WorldMapLanding`, flag 0x20): the session builds the move list and writes the hero on the landing, then the window
  loads around it and stands the actor on the cell it admits; log line `ARC-DEV-115 landing request`.
- **Native smoke** (stage `worldmap`, last; skipped off the start map). It starts wherever the earlier stages left the
  actor; the figures below are those of `ARC_AREA_MAP_SMOKE_ONLY=worldmap` at the new-game start:
  1. W opens the panel.
  2. The farthest area refuses its one-waypoint route.
  3. The nearest unknown area that travel can discover and whose route plans is chosen; from the Crash Site that is
     Shrouded Hills, 43 steps.
  4. With a hero, a maintained spell of the hero is started first (ARC-MAGIC-059): Enter's start check interrupts the hero's actions and
     ends it (`ARC-MAGIC-059 native-smoke worldmap-demaintain PASS`; `NONE` without a hero).
  5. Enter travels. The run ticks the session clock with its own frame stamps (an automated run has no live flow) and
     checks that the real clock gained at least 50 ms + 42 × 62 ms (43 steps) while the open map holds the Menu pause,
     no step is left pending, the game clock gained exactly 43 h, Crash Site and Shrouded Hills
     known, the start map loaded around the landing with the session carried, and the actor on the area's location:
     `ARC-UI-048 native-smoke worldmap PASS`.

### Debug mode (ARC-DEV-050)

A development aid, never gameplay, off by default. It is on for a run started with the lone user argument `--debug-mode`
(after Godot's `--`; `./arcanum area|map --debug-mode`, or the packaged app's executable with `-- --debug-mode`) or when the
launcher settings store it (`debugMode` in `launcher_v1.json`, the launcher screen's "Debug mode" box; it stays on until it is
turned off there or by Reset settings). `ViewerDebugMode` holds the rules; the [player instruction](../player/alpha-build.md#7-debug-mode)
says how to turn it on.

- **World map.** W opens on any map in map mode, under the same guards as above (not in combat mode, not between cells). The
  header says "DEBUG MODE". The area list and Teleport are shown; every area is open to Teleport, known or not (the session's
  known areas are not changed, so the yellow marks stay the true ones). Off the start map the panel has no position: no
  waypoint, no journey, teleport only.
- **Teleport.** The selected area's location on the start map becomes the landing, as a journey's landing: the panel closes
  and the start map loads around it (from another map, as a transfer to the start map), the session carried and following
  companions arriving with the PC. It opens at the area's sector without an arrival tile (an area's location may be a
  blocked tile, and the map load takes an arrival whenever its sector is loaded), so the PC stands on the walkable tile
  nearest to the sector's middle. No game time passes and no area is discovered. Refused during a
  journey and for an area without a location (0,0).
- **Marks.** A save made in debug mode has `[DEBUG] ` before its location, so the save list names it; the build line of the
  main menu ends in "DEBUG MODE"; the status line says "W world map (DEBUG MODE: teleport)". The session's rules and its
  snapshot are not changed by the mode: a debug save loads in a run without it as any save does.
- **Actor guides** (ARC-DEV-064). The controlled actor's magenta frame and yellow anchor cross are drawn only in debug mode
  (AREA and WORLD modes); in play they are not drawn. Self-test raster oracles that check them turn them on for their own
  canvas. The `report` smoke stage checks that the canvas guide switch follows the mode.
- **Native smoke** (stage `debug-travel`, only when named, with `--debug-mode`): W shows the list and Teleport; Teleport on the
  farthest placed area loads the start map around it with the session carried, the actor within 64 tiles of the area, no
  area discovered and less than one game hour gained; a save location is marked: `ARC-DEV-050 native-smoke debug-travel PASS`.
  The ordinary `worldmap` stage checks that the list and Teleport are hidden without the mode.

**NOT READY:**
- The original's world-map screen is not drawn: no map art, zoom, notes or town maps.
- The original keeps the world map unwalkable in a sector with a town map; since ARC-UI-084 the original look makes that check (debug mode and the diagnostic panel do not).
- Travel music, hiding the party, random encounters and the script-blocked list's writers are missing.
- The journey is not saved. When the landing load fails, the start map reloads where the PC stood, with the journey's
  hours and discoveries kept.
- Original parity NOT_VERIFIED.

## Map census (ARC-RENDER-027)

The native census opens every map of the game folder's catalogue that map mode opens, in turn, through the ordinary map load
(the main world map as a window around its map-list start). A map with a sector file opens; a window also needs its
map-list start. The two scienticians underground levels, which have no map-list row (no transfer leads to them), open too,
without a session. The census is the map-smoke stage `census`, run only when named (`ARC_AREA_MAP_SMOKE_ONLY=census` on a
map-mode start; named elsewhere it fails, and it ends on the last map). Per map:

- the load publishes the map with no failure, also none after the publication (a session refused while the scene binds
  fails the map); a load that fails is recorded for its map and the census goes on;
- a frame is drawn with visible draw commands;
- the walker stands on a tile it can step from and back (the spawn rule); a window opens around its map-list start;
- the step evidence leaves no terrain cell, no object evaluation and no edge Unknown (an unresolved portal's flags, a
  leaving-only lock whose tiles cannot be read, an unsupported opening piece). Edges are Unknown by contract when an
  unlock script or an hour lock decides them; they are reported per map, and any other Unknown edge reason fails the map;
- the load time, the slowest frame period of the seven after the publication's first (which carries the upload), the
  managed heap after a full collection and the process working set stay within the census budgets: 60 s, 750 ms, 2 GB and
  3 GB. They are about two to three times the largest values observed. The frame budget bounds stalls (it needs a visible
  window; frames are paced at the display rate and other processes move them), not a frame rate.

`AreaMapCensus.Maps` lists the maps; `AreaMapWindowOriginalTests` pins the list on the installed original.

`ARC-RENDER-027 native-smoke census PASS` on the owner's Mac (2026-10-21, Debug source run from the Crash Site cave):

- 81 catalogued maps, all published and passed, the main world map among them.
- Two edges Unknown by contract (`PORTAL_LOCK_DEPENDS_ON_HOUR`).
- Median load about 4 s and median heap about 200 MB.
- The largest values all come from K'na Tha: 21 s, a 1.03 GB heap and a 1.48 GB working set.
- The slowest frame period was 169 ms. Earlier runs saw 127 ms and 301 ms (wheel clan), which is why the frame budget is
  750 ms.
- Observed, not checked: the heap returns to about 200 MB on the maps after the largest ones.

The census found that the two unlisted maps failed to open in map mode (`AREA_SESSION_InvalidActor`): see
[dialogs](#dialogs-arc-world-032). Original parity does not apply (a viewer census).

## The Crash Site through map mode (ARC-RENDER-028)

`./arcanum area` is map mode on the main world map: `--area-map Arcanum1-024-fixed`, a window around the map-list start (the
Crash Site), with `--hero NAME` and `--sector X,Y` passed on as for `./arcanum map`. It needs the same universal ARC-DEV-015
build (else ARC-RENDER-008, exported after ARC-RENDER-024 so it opens the main map by window) and the same four inputs as map
mode, on any installation. The pinned Crash Site exterior profile
(`area` without `--area-map`, its pinned mobile, effects and patch inputs) is no launcher mode any more: the viewer
keeps it for its regression smokes, which start it with explicit arguments. `./arcanum status` reports `area` with map mode's
inputs. Tests: `tools/local_dev/test_launcher_sync.py` (`area` is map mode on the main map, the arguments it passes) and
`test_universal_dev.py` (status).

## Not yet

- Roofs are drawn, hidden and faded since ARC-RENDER-022 ([roofs](roofs.md)).
  Wall fade uses the ARC-RENDER-011 wall check; inside buildings an actor north
  of a wall can still be hidden by it (parity with the original not verified; the order of the objects of one tile follows the original since ARC-DEV-173, [same-tile draw order](#same-tile-draw-order-arc-dev-173)).
- Animated scenery plays its frames since ARC-RENDER-035 (above), without its sound (fire scenery burns the critters on its tile, ARC-DEV-144).
- Doors and windows open, close and animate since ARC-WORLD-051 (above). Keys are
  not held, NPCs do not open doors, an unlock script is not run, and a door the
  original destroys when busted has no destroyed state.
- Guarded tile and sector transitions and use scripts outside the set; the dialog
  gaps above (SCR admission, generated lines). On the main world map: the limits listed in
  [its section](#the-main-world-map-arc-render-024).
- Original-executable parity is NOT_VERIFIED.

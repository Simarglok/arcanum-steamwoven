# Viewer map mode (ARC-RENDER-017)

## Scope

`crash-site-area` gains a second source: one whole interior of the installed
module, chosen by its `maps/` folder name, drawn and walked in the same viewer.
It is the first consumer of the universal loader: the
[whole-map plan](../world/whole-map-plans.md) (ARC-DATA-019/020) loads the map
from the module's content stack and the
[whole-map step evidence](../world/area-step-assessment.md#whole-maps-arc-world-018)
(ARC-WORLD-018) decides every step. The Crash Site exterior path, its pinned
profiles and every exterior smoke stay as they were.

    ./arcanum map "elven ruins"            # needs ./arcanum setup (any installation) and an ARC-DEV-015 or ARC-RENDER-008 build with map mode
    ./arcanum map Arcanum1-024-fixed [--sector X,Y]   # the main world map, streamed by window (ARC-RENDER-024); a build exported after it
    viewer: --mode crash-site-area --module … --resource … --source-id … --actor-resource … --game-root GAME --area-map "elven ruins" [--area-map-sector X,Y]

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
  On an unlit map the session clock stands still: with no ambient driver it does not flow, holds no modal pause and runs
  no event passes, and the palette's wait is unavailable; time and the pending timers go on on the next lit map.
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
  (ARC-COMBAT-004). The game session is carried over: when the new map is
  published, its actor placement moves to the new map and arrival in one
  session step, and the new map's T0 items are attached beside those of the maps
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
  `AreaUseScriptHost.EntityId`), and a changed slot is written back there (a value equal to the loaded one clears its key), so a door that
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
  first, then the tile's script, then, when the step enters another sector, that sector's script (only the player's
  steps; the original also runs a tile script for an NPC entering the tile). A load, a reset, a transfer arrival and an F8
  restore fire nothing; the original places an object on the map with the same notifications (TS2), which arrival path
  a teleport takes is not read.
- **Run** (ledger TS1, TS3): from line 0 as a use, the player the triggerer, no attachee, under "S4-area-use-reading-objects". The slot
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
  ARC-SCRIPT-018 a tile of `void-kerghans castle`, script 3070), 678 tile scripts and 1 sector script run without one,
  2,328 tile and 19 sector scripts are refused because they need actions or conditions outside the set (before
  ARC-SCRIPT-018: 5, 249 and 2,758; before ARC-SCRIPT-019: 6, 638 and 2,368).
- **Not covered.** Tile scripts for NPCs; the actions and conditions outside the set (most tile scripts); tile and
  sector scripts of generated main-map sectors (only the map's own sector files are read; the one such script, the
  plant generator 30285, is described in [template sector scripts](../research/template-sector-plants.md),
  ARC-RES-073); the arrival tile of a
  transfer; a fade-and-teleport's fade, sound and movie.

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
  checked against it, then it replaces the running session.
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
  walk-to-talk reach it as any speaker, and the conversation runs through its
  own host (`AreaConversationHost.ForVisitor`) over its home map's dialog
  state (`AreaTalkRouting.HostOf`: a speaker goes only to the host that lists
  it, since its index means something only there; the viewer self-test routes
  its stand-in row itself): its dialog slot, local flags and follow keys are its home map's, its
  party commands (`JO`, `WA`, `UW`, `LV`) go to the session party with its
  facts, so the talk is the same wherever it stands and one identity keeps one
  state, as the original keeps one object
  ([OL31–OL33](../research/companion-orders-loyalty-evidence.md#7-followers-across-maps-q7-companion-o7)).
  After the conversation the companions are recorded, then the group is
  rebuilt, so it stays where it stands. A negative `IN` moves the item through
  its home map's inventory seed when the viewer has it attached and is refused
  (a rejected command) when it has not. Its dialog script runs on its home-map
  slot, its quest awards and script sounds are taken from its host like the
  shown map's, and a deferred call whose attachee it is runs through the same
  host. A home-map dialog state the session cannot read (a damaged save) makes
  it not talkable, with a notice, never a crash. The native smoke talks to the
  carried companion on the destination
  (`ARC-WORLD-048 native-smoke visitor-talk PASS`, after the carry; observed on
  `bates mansion lev 1` and on the main map, script 1160 from lev 2, whose
  dialog script line 0 is not admitted yet, `EntryNotAdmitted`, as at home).

Not covered: after a quick load, a companion whose home map was not loaded in
this run, or loaded without the party ever touching it, has no body and is
skipped: when it is recorded on a loaded cell of the shown map, the HUD names it
as not talkable here (`AreaVisitors.Unbodied`), nothing is guessed; a follower that is not a companion of
the shown map (no body, or its cell not admitted) is not carried and stays
recorded where it was. There are
no followers of followers, unconscious followers, carried inventory, orders,
loyalty or wait timer. The exterior keeps its measured roster (Virgil).

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
    session holds no keys yet);
  - an unlock script keeps the T0 rule.
- **Opening by a step.** When a started step crosses a closed portal it may
  pass, the session records the portal open (the last frame of its range) at
  once. The frames then play at 1000 / fps ms each, with the portal's open
  sound. The actor does not wait for the animation (the original's door goal
  sequencing is not modelled). A diagonal step opens every passable closed portal
  on its four legs, the legs the step's evidence checks (parity NOT_VERIFIED).
- **Use.** A left press on a door or window uses it, walking within the use
  range (2 cells) first; a press while the actor is mid-step uses it once the step
  has settled, and any other click replaces a pending door use. An open portal closes. A closed one opens when the
  player's open attempt succeeds. A locked, jammed or magically held one plays
  the locked sound and stays closed. One with an unlock script is not opened (its
  script is not run).
- **Pick a lock (ARC-SKILLS-006).** The inventory window's *Pick a lock with:* button on lockpicks closes it and arms
  them; the next left press on a door or window picks its lock ([pick locks](../world/pick-locks-runtime.md)), walking within
  range 1 first; a press anywhere else, a key press, focus loss, a follower walk order, a map change or a reload cancels it. The HUD names the outcome (the lock opens, locked it, the lock jams, the pick
  failed); nothing opens by itself. Full light and no turn-based round are passed.
- **Saves.** F7/F8 and map changes carry the state, because it is part of the
  session's map changes. A Lock event that comes due locks, unjams and closes
  the portal while its map is shown, and waits while another map is.
- **Not covered.** Maps without a runtime number (not in the map list) keep their
  portals at T0.

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
- **Native smoke** (`--native-smoke` on the main map; `ARC_AREA_MAP_SMOKE_ONLY=map,resources,transfer,roundtrip,stream,worldmap,walk,census,cast,turn-based`
  narrows a run to those stages; without it all but `walk` and `census` run (`cast`, ARC-MAGIC-015's [first cast](../world/spell-cast-host.md#the-viewer-arc-magic-015), and `turn-based`, ARC-COMBAT-020's [round host](../world/turn-based-round-runtime.md#viewer-host-arc-combat-020), are part of the map battery and alone run only themselves) ([map census](#map-census-arc-render-027)); `worldmap` is [the world map](#world-map-arc-ui-048), after the stream), in this order: the map battery at the new-game start (the Crash Site window: walk,
  logbook, sound, dialog with Virgil, who then follows); the resource sweep and reload; the companion follow smoke and
  the map transfer smoke (exit to another map, jump point, F7/F8); `ARC-RENDER-024 native-smoke round-trip PASS`: into
  the cave and back onto the main map by the two exits; and last `ARC-RENDER-024 native-smoke stream PASS`: walks by the
  production route follower into sectors the actor has not stood in until the window has moved three times, each move
  with the session carried (and Virgil following), the actor inside the new core and the walk's goal reached, the first
  one met mid-walk. The map battery and the stream run with `--area-map-sector` at a town (1409, 1314) and a generated
  wilderness sector (1384, 239). The smoke counts only the plan's own commands in the resource sweep (a companion
  visiting from another window is drawn from its kept body) and picks, for the follow smoke, a free NPC that can walk
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
  terrain in its exact reduced chunks ([chunks](crash-site-area-draw-calls.md), which only such zooms have); at the exact
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
(the original's world-map screen) is not implemented (the diagnostic [world map](#world-map-arc-ui-048) travels by its rules since ARC-UI-048); a companion more than one sector from the actor when the window
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
  window open, not under UI focus. The panel is modal: the gate closes, the clock pauses for the frames and a walk stops.
  It refuses on another map ("only on the start map"), in combat mode, while the actor is between cells, and until the
  module's world-map data have loaded (in the background, once per module).
- **What it shows.** It shows the project's own 500 × 500 picture of the start map. The picture is drawn from its terrain:
  one pixel for 4 × 4 sectors, coloured by terrain type, darker where travel is blocked, and brighter where a blocked type
  has its own sector file. On top are the PC, the areas (known ones in yellow), the waypoints and the planned route. A list
  holds every area by its data name.
  - A click on the picture adds a waypoint at that pixel's sector. A known area within 320 tiles takes its place.
  - A double-click in the list adds a waypoint at that area (a diagnostic shortcut for clicking it).
  - A refused segment shows "no route there (the original's message 601)". Clear drops the waypoints.
- **Travel.** Travel, Enter or confirm starts the journey. It runs one travel step per 62 real ms, at most four per frame.
  - Each step's game time jumps the session clock through the event pump, as an explicit wait does. Events that came due
    fire once, late. On a map without lighting the clock jumps without event passes.
  - An area the step discovers becomes known in the session, and the last discovered record is cleared again.
- **Landing.** At the end the panel closes and the start map loads around the landing location, as a transfer does:
  the session is carried and following companions arrive with the PC. W, Escape or cancel during the journey lands
  where it stands, by the setting's cancel rule (A4).
- **Native smoke** (stage `worldmap`, last; skipped off the start map). It starts wherever the earlier stages left the
  actor; the figures below are those of `ARC_AREA_MAP_SMOKE_ONLY=worldmap` at the new-game start:
  1. W opens the panel.
  2. The farthest area refuses its one-waypoint route.
  3. The nearest unknown area that travel can discover and whose route plans is chosen; from the Crash Site that is
     Shrouded Hills, 43 steps.
  4. Enter travels. The run checks one step per 62 ms, the clock gained exactly 43 h, Crash Site and Shrouded Hills
     known, the start map loaded around the landing with the session carried, and the actor on the area's location:
     `ARC-UI-048 native-smoke worldmap PASS`.

**NOT READY:**
- The original's world-map screen is not drawn: no map art, zoom, notes or town maps.
- The original keeps the world map unwalkable in a sector with a town map; that check is not made.
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
(`crash-site-area` without `--area-map`, its pinned mobile, effects and patch inputs) is no launcher mode any more: the viewer
keeps it for its regression smokes, which start it with explicit arguments. `./arcanum status` reports `area` with map mode's
inputs. Tests: `tools/local_dev/test_launcher_sync.py` (`area` is map mode on the main map, the arguments it passes) and
`test_universal_dev.py` (status).

## Not yet

- Roofs are drawn, hidden and faded since ARC-RENDER-022 ([roofs](roofs.md)).
  Wall fade uses the ARC-RENDER-011 wall check; inside buildings an actor north
  of a wall can still be hidden by it (parity with the original not verified).
- Doors and windows open, close and animate since ARC-WORLD-051 (above). Keys are
  not held, NPCs do not open doors, an unlock script is not run, and a door the
  original destroys when busted has no destroyed state.
- Guarded tile and sector transitions and use scripts outside the set; the dialog
  gaps above (SCR admission, generated lines). On the main world map: the limits listed in
  [its section](#the-main-world-map-arc-render-024).
- Original-executable parity is NOT_VERIFIED.

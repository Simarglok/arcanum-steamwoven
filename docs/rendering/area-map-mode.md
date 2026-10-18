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

    ./arcanum map "elven ruins"            # needs ./arcanum setup (game root role) and an ARC-RENDER-008 build with map mode
    viewer: --mode crash-site-area --module … --resource … --source-id … --actor-resource … --game-root GAME --area-map "elven ruins"

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
  sectors; otherwise the map is drawn unlit, has no session clock and the HUD
  names the lighting failure). The session places the actor on the map's
  runtime number.
- **Inventory.** With a session, ground pickup, looting and the inventory window
  work on the map as on the exterior (the map's T0 items seed the session).
- **Transitions.** See [map transitions](#map-transitions-arc-world-031) below.
- **Dialogs.** See [dialogs in map mode](#dialogs-arc-world-032) below.
- **Quick save.** See [quick save in map mode](#quick-save-arc-world-038) below.
- **Not in map mode.** Companions; the HUD says so.

## Coverage on the installed original

Opt-in `RealData` tests (`tests/Arcanum.TerrainViewer.Tests/AreaMapLoadOriginalTests.cs`,
`DAT_ROOT` = the game folder): four interiors load with a drawable plan, step
evidence and a session the walker can leave (the cave spawns on the pinned
cave's arrival 2087,2018); all interiors whose strict snapshot is
complete load in the viewer: all 80 since ARC-DATA-024 (K'na Tha's shared frame
selections), 79 since ARC-DATA-023 (the original's
[art names and bad art](../world/whole-map-plans.md#rules-of-a-whole-map-plan)),
54 before it and 49 before ARC-RENDER-018, which draws the
[body blends and wading](#body-blends-and-wading-arc-render-018) of five of them.

Native smoke (`--native-smoke`, macOS, Godot 4.7.2 .NET, 1280×800): the map
draws, a left click through the production ground-click seam sets a route goal
up to 8 open steps away, frames walk the actor there on the map's chart, and the
resource sweep and overlapped reload pass (`ARC-RENDER-017 native-smoke map
PASS`, `ARC-RENDER-007 resources reload PASS`). Since ARC-WORLD-021 a map with a reachable window also
steps the actor across it with the vault (`ARC-WORLD-021 native-smoke vault PASS`; otherwise `NO-WINDOW`). Since ARC-COMBAT-005, with `--hero` and a lit map, the hero attacks the living NPC with the fewest hit points until it dies ([hero attack](../world/hero-attack-runtime.md#viewer-area); `ARC-COMBAT-005 native-smoke attack PASS`, otherwise `NO-TARGET`; observed on `crash site cave`: a rat of 6 hit points, walk to reach, 12 attacks, fallen pose read, 192 experience). Observed on `elven ruins`
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
before the reload, because a large map's heap (about 450 MB for the 64-sector
`tarant sewers-02`) varied between runs from −63 to +26 MB in a single post-GC
sample. The baseline and the retained size are each the least of three post-GC samples
a frame apart (exterior and maps); the retired generation must still be collected.
**Known flake:** on `tarant sewers-02` the heap after the reload stays 8–24 MB
larger in a stable way (samples within one measurement differ by under 1 MB),
so about one run in four still exceeds the 4 % floor; what grows (a one-time
cache or a retention) is not yet known and is left to its own investigation.

## Body blends and wading (ARC-RENDER-018)

The scene plan draws three more serialized body states, by rules checked in the
retail executable
([ledger BL1–BL5](../research/original-verification-ledger.md#rendering-body-blends-and-wading-arc-render-018)).
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
- **Unlit.** Multiply and constant-alpha bodies are drawn with the base palette,
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

On the installed original (opt-in test): 20 wading bodies in `caladons sewers`
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
animated-dead tints. Additive bodies keep the earlier lit rule, although the
original draws them unlit too (BL2); changing it touches the pinned Crash Site
lighting and is left to its own task.

## Map transitions (ARC-WORLD-031)

Map mode moves the player between maps by the map's own data: the
[map transitions](../world/map-transitions.md) (ARC-DATA-025) read from the
content stack when the map loads, so every map works the same way and nothing
is bound to a location. Only unconditional transitions run; a guarded one (a
quest flag, a held item, a dialog) is not a trigger here (SCRIPT slice S4).

- **Triggers.** A left click on an object with an unconditional use teleport
  (scenery or a portal) is a use: accepted within Chebyshev distance 2 by the
  ARC-WORLD-009 use rule (`AreaUseRule`, now shared with the Crash Site pair),
  otherwise the click route first walks to the nearest cell from which the rule
  accepts the use, and the use is asked on arrival; an exit to a map that map
  mode cannot open is refused at once, without the walk. Every tile the actor
  steps into is checked in step order, also when one frame completes several
  steps: a jump point or an unconditional tile script there fires, the jump
  point first (ledger TR3), and stops the click route (the actor may stand a
  step further when the frame had already started it). Sector scripts are read
  but none of the installed ones teleports, so they are not wired. One transfer
  is pending at a time; the stick, clicks and hover actions wait while it is,
  and a reset or reload drops it; a reset puts the actor on its start without
  firing the start tile, as a load does.
- **Destinations.** A transfer applies once the actor is settled. On the same
  map the actor moves to the authored arrival. Another map must be one map mode
  can open: listed in the map list with 1 to 256 sector files (78 of the 79
  installed map folders; the 2000 × 2000 start map is the one it cannot open, so
  every exit to it is refused with `DESTINATION_NOT_OPENED_IN_MAP_MODE 5000` and
  the HUD says so). The destination is loaded through the ordinary load path
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
  fade-and-teleport. Companions do not travel in map mode (ARC-WORLD-037).

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
next map and come back with it. The party commands keep the diagnostic follow
key: no NPC follows the player in map mode yet (ARC-WORLD-037). A map the map
list does not name (`scienticians underground lev 2`) has no runtime number,
hence no session and no dialogs.

Opt-in census (`AreaMapConversationOriginalTests`, `DAT_ROOT`): 34 listed
interiors have 182 dialog NPCs. Each one gets a typed start: 50 start a
conversation, 1 ends with skip default, and 131 stop at an SCR action,
condition, goto or value kind the dispatcher does not admit yet (most often
action types 23 and 22, and condition type 21). Of the 50 conversations, 35 stop at a generated NPC
line, which is not loaded yet, 8 at an unsupported DLG operation, and 7 offer
replies at once; 6 end when the first choosable reply is always taken. Native smoke (`ARC-WORLD-032 native-smoke talk PASS`, after
the roof smoke): from 4–6 cells away the NPC hovers as a walk to talk, a click
through the production seam walks within reach and opens the panel (a walk
that stops short is tried again from within reach, where the click must open
it), the first choosable replies run until the dialog ends or stops, and Close
ends the modal panel; a start refused for missing script content, an entry
error or a changed source fails the smoke. `FRONTIER` means every NPC tried (up
to eight) stopped before offering a reply, `NOT-STARTED` that none started,
`NO-REACHABLE-SPEAKER` that none could be clicked, and `NONE` that no NPC is
drawn. Observed: `caladon-panarrii temple`, script 2969, 3 turns, the dialog
ended, 1 effect in the session; `tulla interior` reports `FRONTIER` (generated
greetings); in `caladon-mafia underground` both NPCs are undrawn. That the
consequences travel with the session to another map and back is the host's
synthetic test (`AreaConversationTests`); the native smoke does not observe it.

## Quick save (ARC-WORLD-038)

With a session, F7 and F8 work on any map as on the exterior
([quick save](terrain-viewer.md#quick-save-and-quick-load-arc-world-011): keys on
release, the same guards, atomic slot, every check before anything changes),
with their own slot, `user://saves/map/quicksave.json`, so the Crash Site slot
is never touched. A save records the actor on the map's runtime number. While
another map loads there is no session, so F7/F8 do nothing; a load drops a
queued transfer.

- **Same map.** A save whose actor stands on the map shown is adopted in place,
  as on the exterior: the shown map's inventory seeds and dialog state are
  checked against it, then it replaces the running session.
- **Another map.** A save whose actor stands on another map that map mode can
  open (listed, 1 to 256 sector files) passes the same checks of the slot, the
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
the exterior AREA smoke, so its two sweeps are held there too; the exterior smoke was
not run for this change (it stops earlier at a known pixel-sample failure). Other
samplers that reset the camera and then read a frame live (the exterior lighting
and class samples, cave lighting) are not changed and may flake the same way.

## Not yet

- Roofs are drawn, hidden and faded since ARC-RENDER-022 ([roofs](roofs.md)).
  Wall fade uses the ARC-RENDER-011 wall check; inside buildings an actor north
  of a wall can still be hidden by it (parity with the original not verified).
- Door passability follows the step evidence: a locked door the player has no key
  for opens only for a step leaving a building, as the original executable decides
  it (ARC-RES-049); doors are not opened or animated.
- The main world map (ARC-RES-034) and therefore every exit to it, conditional
  transitions, door runtime and companions on these maps; the dialog gaps
  above (SCR admission, generated lines).
- Original-executable parity is NOT_VERIFIED.

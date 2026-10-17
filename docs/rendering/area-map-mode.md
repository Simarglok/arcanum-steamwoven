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
  actor, lighting and stationary lights read outside the data owner, are
  re-verified against them before publication, as on the exterior.
  Prototypes come from the stack, so `--prototypes` is refused, as are the Crash
  Site inputs (`--area-mobile`, `--area-effects`, `--area-patch`,
  `--area-cave-mobile`). A plan the data layer refuses stops the load with
  `AREA_MAP_PLAN_<STATUS>` (unknown map, too large, unreadable…).
- **Budgets.** A whole map may retain up to 16,384 frames and 160 MiB of CPU
  frames (`AreaRepositoryPolicy.MaxMapFrames`/`MaxMapCpuBytes`; the exterior
  keeps 2,444 frames and 81,853,096 bytes); the CPU frames plus the actor stay
  within `TerrainCachePolicy.MaxRgbaBytes` (256 MiB). GPU residency stays within
  the exterior's visible-frame caps; terrain chunks and the frame LRU are the
  exterior's.
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
  the flip bit mirrors it (pinned `tig art.c` 1235–1253). The pinned Crash
  Site profiles draw no portal, so their plans are unchanged.
- **Lighting and session.** The map's own ambient scheme and stationary lights
  when its sources admit them (the lighting reader now reads up to 256 core
  sectors; otherwise the map is drawn unlit, has no session clock and the HUD
  names the lighting failure). The session places the actor on the map's
  runtime number.
- **Inventory.** With a session, ground pickup, looting and the inventory window
  work on the map as on the exterior (the map's T0 items seed the session).
- **Not in map mode.** Map transitions, NPC dialogs, companions and F7/F8 quick
  save (the Crash Site slot is never touched); the HUD says so.

## Coverage on the installed original

Opt-in `RealData` tests (`tests/Arcanum.TerrainViewer.Tests/AreaMapLoadOriginalTests.cs`,
`DAT_ROOT` = the game folder): four interiors load with a drawable plan, step
evidence and a session the walker can leave (the cave spawns on the pinned
cave's arrival 2087,2018); all 54 interiors whose strict snapshot is
complete load in the viewer (49 before ARC-RENDER-018, which draws the
[body blends and wading](#body-blends-and-wading-arc-render-018) of the other five).

Native smoke (`--native-smoke`, macOS, Godot 4.7.2 .NET, 1280×800): the map
draws, a left click through the production ground-click seam sets a route goal
up to 8 open steps away, frames walk the actor there on the map's chart, and the
resource sweep and overlapped reload pass (`ARC-RENDER-017 native-smoke map
PASS`, `ARC-RENDER-007 resources reload PASS`). Since ARC-WORLD-021 a map with a reachable window also
steps the actor across it with the vault (`ARC-WORLD-021 native-smoke vault PASS`; otherwise `NO-WINDOW`). Observed on `elven ruins`
(9 sectors), `bates mansion lev 1` (9), `crash site cave` (1) and
`caladon-castle main floor` (79 sectors, 323,584 terrain cells, 2,967 bodies,
load about 14 s). A small map may fit every GPU cache, so map mode does not
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

The scene plan draws three more serialized body states, by rules read in CE and
checked in the retail executable
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
  set beside the wading alpha, and CE's blitter (`tig art.c`) tests additive and
  multiply before constant alpha. That dispatch is a CE reading, not checked in
  the executable, and no installed body has the combination.

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

Not covered: wading is the T0 flag only (CE recomputes it from the tile when
an object moves, not checked in the executable; the walking actor never wades);
shadows (CE also darkens a wading body's shadow); frozen, stoned, `OF_DONTLIGHT` and
animated-dead tints. Additive bodies keep the earlier lit rule, although the
original draws them unlit too (BL2); changing it touches the pinned Crash Site
lighting and is left to its own task.

## Not yet

- Eye-candy overlays and the remaining critter poses; the
  original's missing-art fallback; K'na Tha's frame budget (streaming).
- Roofs are not drawn. Wall fade uses the ARC-RENDER-011 wall check; inside
  buildings an actor north of a wall can still be hidden by it (parity with the
  original not verified).
- Door passability follows the step evidence: a locked door the player has no key
  for opens only for a step leaving a building, as the original executable decides
  it (ARC-RES-049); doors are not opened or animated.
- Map transitions (ARC-RES-035), the main world map (ARC-RES-034), door
  runtime, dialogs and companions on these maps.
- Original-executable parity is NOT_VERIFIED.

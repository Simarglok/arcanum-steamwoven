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
cave's arrival 2087,2018); of the 54 interiors whose strict snapshot is
complete, 49 load in the viewer. The other five stop at
`AREA_UNSUPPORTED_BODY_STATE`, because the scene plan does not yet draw these
original blend states (follow-up ARC-RENDER-018):

| Map | Bodies | State |
| --- | ---: | --- |
| `caladons sewers`, `tarant sewers-02` | 122 | wading (`OF_WADING`): the lower 15 px drawn at alpha 92 |
| `void - arronax's interior`, `void-kerghans castle` | 54 | multiply blend (blit `0x40`) |
| `bessietoonesmine-fixed` | 1 | constant alpha (blit `0x100`, alpha 95) |

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
heap's ordinary noise; the retired generation must still be collected.

## Not yet

- The five maps above; eye-candy overlays and the remaining critter poses; the
  original's missing-art fallback; K'na Tha's frame budget (streaming).
- Roofs are not drawn. Wall fade uses the ARC-RENDER-011 wall check; inside
  buildings an actor north of a wall can still be hidden by it (parity with the
  original not verified).
- The door and window step rule of ARC-WORLD-018 is being recounted against the
  original executable (its inside-to-outside exit check swaps the cells), so
  door passability in map mode is not final.
- Map transitions (ARC-RES-035), the main world map (ARC-RES-034), door
  runtime, dialogs and companions on these maps.
- Original-executable parity is NOT_VERIFIED.

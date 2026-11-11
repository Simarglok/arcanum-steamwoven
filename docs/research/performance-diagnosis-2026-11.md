# Load time and window responsiveness diagnosis (ARC-RES-102, 2026-11-11)

Measured diagnosis of the viewer: how long start-up, map loads, save loads and the opening of each window take, where
the time goes and what to change first. The base commit at measurement time was `ace7c7b81` of `origin/main`
(measurement builds carried a local, uncommitted probe on top of it). The owner's observation that the game feels much
less responsive than the original and than the open source port of it is what started it; the earlier, Crash-Site-only passes are
[area viewer performance](area-viewer-performance.md) and [area performance review](area-performance-review.md).
It changes no code, is not a budget and not a regression gate. Every optimization below is a candidate that becomes
its own task through the coordinator; gains marked "estimate" are projections from the numbers, not measurements.

## Scope and method

- **Machine and builds.** One Apple M4 Pro Mac (14 cores, macOS, Godot 4.7.2 .NET, `gl_compatibility`), 1280×800 window,
  original data of the GOG 1.0.7.4 installation. Two kinds of run: the packaged build as a player runs it (the
  `macOS package` export preset, `ExportRelease`, optimized assemblies; "packaged" below) and a Debug source run through
  `./arcanum smoke` ("Debug source").
- **Profiles.** Packaged runs used the owner's own settings (visual profile HD, HD interface on, the installed interface
  pack of 1,142 sprites at 4×); "Legacy" is the same packaged build with both off (one run).
- **Probe.** A temporary probe (not committed; the standing proposal is O10) printed monotonic timestamps, wall time,
  managed allocation and GC counts around the funnel methods: `StartLoadAsync`, `PublishAreaReady` (with laps inside
  it and inside `StartAreaTransitions` and the openers), `OpenWindowMode`, `LoadGame`, `StartNewGame`, `OpenMainMenu`,
  `OpenSaveList`, `ShowSpellPane`, the window-move publication, and stage marks on the load worker
  (`AreaMapLoadOperation`, `AreaLoadOperation`). A temporary smoke stage `perf` pressed each window key through the
  production key route, six times each, timing the call (`open-sync`) and the call up to the end of the next rendered
  frame (`to-first-frame`); iteration 0 is the cold open, iterations 1–5 are warm. The probe went through four
  revisions; the first one (no HD pack found, no laps) is not used for the tables.
- **Noise.** Other threads' builds and smokes ran on the same Mac (observed with `uptime`: load average 4–18 on 14
  cores); identical runs differed by up to 2×. Window tables give the median and the range over seven packaged HD
  runs, loaded runs included; the load figures say which runs they use. They rank causes; they are not contracts.
  Runs went through the launch queue, so only one Godot window ran at a time.
- **Units.** Allocation is printed by the probe in KiB and given here in MiB.
- **Not measured.** The original and its open source port (no timing was taken from them; the comparison below is an unverified
  expectation), gamepad and hover latency, combat, character creation screens in the packaged build, the Esc-menu
  Options window (the `O` key did nothing in the stage), a save with a large session (the quick loads below are small
  ones), Windows, memory use.
- **Run status.** Some smoke runs ended with a stage failure or a texture-leak `ERROR` line at exit (`menu,options,scene`
  in Debug source, and an exit code on two packaged flow runs); the timing lines used here came from stages that had
  reached the measured point. Two packaged flow runs crashed before the probe fix and were discarded.

## Result in one table

| What the player does | Measured | Where it goes |
| --- | --- | --- |
| Start to the main menu | 12–15 s: process start 0.7 s, map decode on the worker 7.7 s, main-thread publication 3.2–3.6 s (best runs), menu 0.6–1.0 s | the menu opens only after the whole start map is published |
| New game from the menu (Debug source) | 41–118 ms | fine |
| Load a save on the same map | 59–138 ms (small session) | fine |
| Load a save on another map, or walk out of a cave | 6.7–8.2 s on the worker, then 0.4–0.7 s publication | the whole main map is read again |
| Enter the Crash Site cave | 3.6–5.0 s worker, 0.1 s publication | a whole map read |
| Walk across a sector window edge | 6.3–8.8 s in the background, then a 0.09–0.7 s stall and once a 0.48 s frame | the window move repeats a full load |
| First open of inventory / sheet / logbook (HD) | 0.7 s / 1.1 s / 1.0 s (medians, up to 1.1 / 2.2 / 2.2 s) | art read and HD decode on the main thread |
| First open of world map (HD) | 0.25 s | HD decode |
| Same windows, Legacy | 0.27 / 0.41 / 0.36 s, world map 0.06 s | art read |
| First open of the saves list (Debug source) | 0.7 s | listing and art |
| Any window again, spell pane, schematics | 8–17 ms to the next frame (frames are 5–9 ms) | fine |

So the feeling of slowness has two sources. At start-up the main thread is blocked for 3–4 s while the map is
published; after that, map changes and window moves load 6–9 s in the background and stall the game for 0.1–0.7 s when
they publish. Separately, the first time each window opens in a session it costs 0.25–1.1 s (up to 2.2 s under load).
Steady frame time and warm window opens are good.

## Loading

**Worker thread**, the whole main map (`Arcanum1-024-fixed`). Seven packaged runs: median 7.7 s, range 6.4–14.9 s
(the 14.9 s run was loaded); Debug source runs 8.0–11.1 s. Stage times of one clean packaged run (the other clean runs
agree within about ±30%):

| Stage | Time | Note |
| --- | ---: | --- |
| content stack open, catalogue | 0.3 s | |
| map plan | 1.0 s | |
| map transitions | 1.6 s | map-invariant (from code reading), recomputed on every load |
| archive authentication (`mapPlan.Open`) | 1.1 s | whole-archive SHA-256 passes (from code reading) |
| strict snapshot | 1.3 s | |
| step inspection and scene plan | 0.3 s | |
| frame copy (1,994 frames, 25 MiB) | 1.2 s | |
| lighting, `VerifyFiles` | 0.5 s | more SHA-256 passes (from code reading) |
| whole mobile objects, actor source | 0.3 s | |

**Main thread**, `PublishAreaReady`: median 4.2 s, range 3.2–8.7 s over seven packaged runs, 3.5 s in the clean run
below, with about 640 MiB of managed allocation and about 200 gen0, 80 gen1 and 5–20 gen2 collections per publication.
Laps of that clean run (packaged, HD):

| Step | Time | Note |
| --- | ---: | --- |
| `SyncHeroBody` | 1.84 s | opens the actor archive (`arcanum1.dat`, 447 MB) and decodes the hero body ART on the main thread (from code reading); 1.6–1.8 s in the other clean runs |
| `StartAreaTransitions` | 1.30 s | see below |
| `BindAreaCompanions` | 0.20 s | 89 MiB allocated |
| everything else | 0.23 s | |

After the publication the first rendered frame takes a median of 0.35 s (range 0.28–1.13 s, texture upload); it is
outside the 3.5 s.

`StartAreaTransitions` in a Debug source run (2.0 s, 469 MiB allocated): companion templates 0.23 s, reputations
0.22 s, session creation 0.10 s, inventory seeds 0.41 s, item art sizes 0.47 s, sound catalogue 0.59 s. Five of the six
allocate about 90 MiB each (89, 88, 90, 89, 112 MiB), which looks like the same large archive structures read again
by every consumer; the cause is not confirmed (O4). In the packaged build the whole step is 1.1–1.3 s.

The window move runs the same worker and the same publication; its publication is cheaper (0.09–0.7 s, 55–145 MiB)
because the hero body and several caches are warm.

**Long frames not attributed.** In the packaged `save-resume,transfer` run frames of 1.6–1.9 s followed a same-map
quick load and a 3.8 s frame came at the start of the cave-return load; in Debug source, frames of 0.6–0.9 s around
new game and load, and one of 5.6 s. They may be work of the smoke stages themselves (waiting, synchronous test code)
rather than of the game; the probe cannot tell. This needs its own probe before any save-load claim is made (O11).

## Windows

First open, `open-sync` / `to-first-frame` in ms, packaged build. HD: median [min–max] over seven runs; Legacy: one run
(loaded). Warm opens are the median of iterations 1–5.

| Window | HD cold | Legacy cold | Warm (to-first-frame) |
| --- | --- | --- | --- |
| Inventory (`I`) | 434 [205–790] / 735 [440–1130] | 218 / 266 | 8–11 |
| Character sheet (`C`) | 567 [384–1607] / 1125 [999–2239] | 380 / 407 | 8–9 |
| Logbook (`L`) | 651 [341–1826] / 983 [705–2189] | 345 / 364 | 8–10 |
| Schematics (`T`) | 12 [12–18] / 25 [24–37] | 13 / 28 | 8–9 |
| World map (`W`) | 35 [34–50] / 253 [248–367] | 38 / 58 | 16–17 |
| Spell pane (`M`) | 14 [13–16] / 20 [18–22] | 13 / 21 | 6–7 |

Anatomy of the cold open (laps inside the openers, packaged HD; ranges are over the runs that had laps): inventory
art look 170–735 ms (99 MiB allocated), then 225–340 ms more at first draw; character sheet art attach 200–870 ms and
portrait 180–375 ms (88 MiB), then 560–615 ms more at first draw; logbook art look 330–1810 ms (188 MiB). The "more at
first draw" part is the HD resolution in `HdInterfaceTexture.Active()` (from code reading): on first draw each HD
interface frame is resolved against the pack (file read, SHA-256 of the Legacy pixels, managed PNG decode of a 4×
frame, alpha-mask check), gets mipmaps and is uploaded, synchronously inside the draw. The first open is also when the
Legacy art is read and decoded for the first time (`InventoryArtLook`, `AttachSheetArt`, `LogbookArtLook`; the main
menu's art 0.23–0.7 s and the browser art 0.17 s at the menu's first open). All of it is created once and cached, which
is why the second open is cheap.

## Other findings

- Runtime settings tested on the packaged build, no gain: `DOTNET_GCgen0size` 256 MB (publication 4.9 s and 7.8 s
  against 3.2–4.2 s without it), `DOTNET_TieredPGO=0` (within noise).
- The load path is not slower in Debug source than in the packaged build within the noise (worker 8.0–11.1 s against
  6.4–14.9 s, publication 3.6–8.0 s against 3.2–8.7 s): the cost is in work done, not in JIT mode.
- GC pressure is a symptom of the publication's allocations, not a lever of its own.
- Steady state is fine: frames settle at 5–9 ms after the first publication and warm windows open invisibly.

## Expectation against the original and its open source port (not measured)

The expectation, from the architecture of the original and of its open source port and not from a run of either
(level F0; a read of the executable would settle it), is that the interface art is loaded when the interface
initializes and windows draw from it, that the menu shows before any map exists, and that a map is read sector by
sector as needed, without whole-archive authentication. The viewer differs in four ways that the numbers above
reflect: it needs the whole start map before it shows the menu (measured), it creates window art at first use
(measured), it re-reads and re-authenticates map archives for every map change and window move (the re-read is
measured; the authentication is from code reading), and it decodes 4× PNGs on the main thread at first draw (from code
reading, consistent with the HD/Legacy difference measured). The comparison with the other two programs itself is not
measured.

## Ranked optimizations

Gain is what the numbers above say it can remove (estimates unless stated); cost is relative. Each becomes its own task.

| # | Optimization | Gain (estimate) | Cost |
| --- | --- | --- | --- |
| O10 | A standing, env-gated timing probe (`ARC_PERF_PROBE`) and a `perf` smoke stage that prints these tables, as the way to verify every item below | needed to prove any other item | small |
| O3 | Load the hero body ART (`SyncHeroBody`) on the worker in the load stage and only bind it on the main thread | −1.6–1.8 s of the 3.5 s main-thread freeze at start and at a cave return (little at a window move, where the body is already cached) | small–medium |
| O5b | Compute and cache the map's transitions once per map and runtime (not on every load) | −1.0–1.6 s on every load, including each window move | small |
| O2 | Warm every window's art off the main thread after the publication: decode ART and HD PNGs on workers into bytes, create textures on the main thread in small per-frame slices (or draw each window once off screen) | the cold hitches (0.25–1.1 s each, 8 windows) become warm opens | medium |
| O4 | One shared reader of the module and resource archives for the session binders (companions, reputations, inventory seeds, item art sizes, sound), parsed on the worker; confirm the 90 MiB-per-consumer cause with an allocation profile first | up to −1.1–1.3 s of publication and −400 MiB allocation (packaged transitions lap 1.1–1.3 s) | medium |
| O1 | Open the main menu as soon as the menu art is read and load the start map behind it (a new game then waits only for what is left) | start to menu from 12–15 s to about 2 s | medium, touches the start contract |
| O5a | Skip repeated whole-archive SHA-256 passes inside one process when the file identity (size, modification time, first verification) is unchanged | −1.6–2.2 s of the worker (about 25%) on every load | medium, touches the authentication rule of the load contract (owner decision) |
| O8 | Saves list: list asynchronously, decode thumbnails lazily | −0.7 s first open (Debug source) | small |
| O5c | Keep the decoded frame repository and strict snapshot across loads of the same map for window moves, cave return and a cross-map quick load | 6–9 s to about 2 s per map change; fewer hitches while walking | large, touches the residency design |
| O6 | Split the publication into per-frame slices (after O3 and O4 it is mostly the first-frame upload) | removes the 0.1–0.7 s stall and the 0.48 s frame at a window move | medium |
| O9 | HD: ship mipmapped or VRAM-compressed textures in the pack and skip the SHA-256 of the Legacy pixels on a warm pack | −0.2–0.6 s per window at first draw | large, pack format change |
| O11 | Attribute the long frames around quick load and map change (probe in the game, not in a smoke stage) | unknown; decides whether save load has a real stall | small |

**Done (ARC-DEV-368).** O5b: a map's transitions and the map list's destinations are read once per process for the same content stack
(`AreaMapTransitionsCache`, keyed by map and the stack's mounted sources, archive sizes and modification times); measured on the main map in a
Debug source run the stage went from 0.95–1.6 s to 0–1 ms on every later load. O3, in part: the hero's wanted body is read and decoded on a worker
(`PrefetchHeroBody`, started once the session and inventory exist) while the publication binds its other hosts; in the same run the main thread waited 0 ms
for a dressed dwarf body that took 0.5 s on it before (the worker read it in 134 ms). The first `PublishAreaReady` was still 4.0 s against 4.2 s
before: the rest of it, the first publication of the scene to the canvas (inside the body swap before, in the final publication now), is O6. A duplicate
publication the swap and the quarantine make was tried and dropped: later ones cost 0.1–0.3 s and removing them showed no gain. Original behaviour is unchanged.

Recommended order: O10, then O3 and O5b together, then O2, then O4 and O1; O5a, O5c and O9 need the owner's decision on
their contracts first; O11 can run beside any of them. Done in that order the main-thread freeze at start should fall
from about 3.5 s to under 1 s (estimate) and the first window opens to warm opens, without touching original behaviour.

## Reproducing

The probe and the stage were local patches, not part of the repository; rerunning needs equivalent instrumentation
(O10 is the proposal to keep it). The shape of a run: build a Release export of a commit with the probe into a scratch
folder (`tools/local_dev/package.py mac --ref COMMIT --no-fetch --no-smoke --output FOLDER` inside a queued `export`
run, never into `artifacts/packages`), run it with `-- --native-smoke` and the viewer's map arguments through
`./arcanum run --kind window`, with the owner's `visual_profile_v1.json` and `game_settings_v1.json` copied to isolated
`*_TEST_PATH` files and `ARC_HD_PACK` set for HD (an automated run does not discover packs by itself), and read the
timestamped lines from the log.

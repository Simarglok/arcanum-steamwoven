# Documentation index

Pages are grouped below by kind: project (status, plans, decisions), development (workflow and tooling), formats and world contracts, rendering, input and scripting, research evidence, and player-facing pages. Start with [current status](project/status.md), [vision](project/vision.md), the [missing systems plan](project/missing-systems-plan.md) and [toolchain](workflow/toolchain.md). Research documents retain historical claims and evidence boundaries; linked implementation pages describe current APIs. Active tasks come from the owner ([task workflow](workflow/tasks.md)).

## Project

- [Project status and component catalogue](project/status.md)
- [Project vision](project/vision.md)
- [Missing systems: map and work plan](project/missing-systems-plan.md)
- [Open bugs register (B01-B54)](research/open-bugs.md)
- [Function audit against the original: state and plan](research/function-audit-plan.md)
- [Full-game functional requirements and bounded implementation coverage (draft)](requirements/README.md)
- [Architecture direction](architecture/decisions.md)
- [Arcanum.Data: API, limits and commands](project/components/Arcanum.Data.md)
- [Arcanum.Executable: the x86 interpreter of the player's Arcanum.exe](project/components/Arcanum.Executable.md)
- [Arcanum.Godot viewer: shared-toolchain commands and known limits](project/components/Arcanum.Godot.md)

## Development

- [Toolchain and verification entry points](workflow/toolchain.md)
- [Private local diagnostic launch and build retention](workflow/local-development.md); [which builds may be deleted](workflow/build-retention.md)
- [Packaged builds: prerequisites, commands, daily build](workflow/packaging.md)
- [Task workflow](workflow/tasks.md) and [task naming](workflow/task-naming.md)
- [Original executable check](research/original-executable-check.md): how original behaviour and bugs of the original game are classified, and the function oracle (emulating single functions of the executable)
- [Original oracle and reconstruction removal (ARC-RES-051)](research/original-oracle-research.md): trial results, tools and plan for checking Steamwoven against the executable and removing the source reconstruction
- [Prototype extraction (ARC-RES-085)](research/prototype-extraction.md): how the original builds its prototype files at its first start, and how Steamwoven builds them from the player's own executable on an installation the original never ran
- [Arcanum.Data](project/components/Arcanum.Data.md)
- [Arcanum.Godot viewer](project/components/Arcanum.Godot.md)

## Formats and world

- [ART: evidence-bounded one-frame decoder subset](formats/art.md)
- [DAT container: evidence-bounded R1 note](formats/dat.md)
- [Bounded DLG reader](formats/dlg-reader.md)
- [Bounded map and sector notes](formats/maps.md)
- [HD pack and recipe format](formats/hd-pack.md)
- [Bounded static-object loader](world/static-object-loader.md)
- [Terrain loader](world/terrain-loader.md)
- [Bounded terrain obstruction components](world/terrain-obstruction-components.md)
- [Bounded static-obstruction components](world/static-obstruction-components.md)
- [Area traversal contract: coverage, disposition, readiness gate](world/area-traversal-contract.md)
- [Grid movement runtime and policies P1–P8](world/grid-movement-runtime.md)
- [Bounded production cardinal traversal assessment](world/cardinal-traversal-assessment.md)
- [Click-to-move route (ARC-WORLD-007: core and LMB in area)](world/click-route-runtime.md)

## Rendering, input and scripting

- [Terrain, static and bounded actor diagnostic viewer (ARC-RENDER-003/004/005)](rendering/terrain-viewer.md)
- [Bounded actor visual contract — original-runtime parity NOT READY](rendering/actor-visual-contract.md)
- [ARC-INPUT-001 diagnostic palette and WORLD keyboard/mouse route](input/diagnostic-palette.md)
- [Crash Site day/night ambient and cave lighting (ARC-RENDER-010, ARC-RENDER-012)](rendering/area-lighting.md)
- [Actor anchor and original wall fade (ARC-RENDER-011)](rendering/terrain-viewer.md#actor-anchor-and-wall-fade-arc-render-011)
- [ARC-RENDER-008 passability overlay](rendering/passability-overlay.md)
- [HD presentation contract: scaled crop, Legacy anchors, per-frame fallback, toggle (ARC-RENDER-013) — no HD runtime yet](rendering/hd-presentation-contract.md)
- [HD interface art: U1/U2 methods and rules](rendering/hd-interface-art.md)
- [HD upscale methods at 2×: AI vs pixel-art scalers, alpha, palette slots, terrain seams, licences (ARC-RES-033) — research only](rendering/hd-upscale-research.md)
- [Original look of the inventory, loot, character, logbook and world-map windows (ARC-UI-051: plan; inventory done; ARC-UI-052: loot done; ARC-UI-065: steal done)](ui/original-windows.md)
- [ARC-UI-001 display/UI validation](input/display-ui-validation.md)
- [Gamepad prototype evidence](input/gamepad-prototype.md)
- [Synthetic movement study](input/gamepad-movement-study.md)
- [Bounded local-pathfinding study](input/arcanum-pathfinding-study.md)
- [Bounded dialog transition runtime](scripting/dialog-transition-runtime.md)

## Research evidence

- [ART decoder specification record](research/art-decoder-spec.md)
- [ART research evidence record](research/art-evidence.md)
- [Actor visual evidence and anchor readiness gap (ARC-RES-009)](research/actor-visual-evidence.md)
- [Actor world-anchor placement evidence (ARC-RES-010)](research/actor-placement-evidence.md)
- [Crash Site sector framing: R3 correction](research/crash-site-framing.md)
- [Crash Site selected-sector objects](research/crash-site-objects.md)
- [Crash Site terrain identifier resolution (ARC-RES-004)](research/crash-site-terrain-resolution.md)
- [Selected Crash Site sector passability evidence (ARC-RES-011)](research/sector-passability-evidence.md)
- [Static-obstruction evidence (ARC-RES-012)](research/static-obstruction-evidence.md)
- [DAT R1 evidence record](research/dat-evidence.md)
- [ARC-RES-005 dialogue evidence record](research/dialog-evidence.md)
- [Research method and R1 boundary](research/method.md)
- [Crash Site scenery placement and overlap (ARC-RES-008)](research/scenery-placement.md)

## Player

- [Alpha install and first start](player/alpha-build.md)
- [Hotkeys](player/hotkeys.md)

## Standalone labs

- [Display UI lab](../experiments/display-ui-lab/README.md)
- [Gamepad lab](../experiments/gamepad-lab/README.md)
- [Contributor guidance](../CONTRIBUTING.md)

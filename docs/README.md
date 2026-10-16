# Documentation index

Start with [current status](project/status.md), [vision](project/vision.md), [dependency roadmap](project/roadmap.md) and [toolchain](workflow/toolchain.md). Research documents retain historical claims and evidence boundaries; linked implementation pages describe current APIs. The Hermes board supplies active tasks.

## Project

- [Project status and component catalogue](project/status.md)
- [Project vision](project/vision.md)
- [High-level dependency roadmap](project/roadmap.md)
- [Full-game functional requirements and bounded implementation coverage (draft)](requirements/README.md)
- [Architecture direction](architecture/decisions.md)

## Development

- [Toolchain and verification entry points](workflow/toolchain.md)
- [Private local diagnostic launch and build retention](workflow/local-development.md)
- [Hermes project workflow](workflow/hermes.md)
- [Arcanum.Data](Arcanum.Data.md)
- [Arcanum.Godot viewer](Arcanum.Godot.md)

## Formats and world

- [ART: evidence-bounded one-frame decoder subset](formats/art.md)
- [DAT container: evidence-bounded R1 note](formats/dat.md)
- [Bounded DLG reader](formats/dlg-reader.md)
- [DLG: bounded seven-field research subset](formats/dlg.md)
- [Bounded map and sector notes](formats/maps.md)
- [Static-object loader contract: bounded Crash Site candidate](world/static-object-loader-contract.md)
- [Bounded static-object loader](world/static-object-loader.md)
- [Static-scene contract: bounded Crash Site candidate](world/static-scene-contract.md)
- [Terrain loader](world/terrain-loader.md)
- [Bounded terrain obstruction components](world/terrain-obstruction-components.md)
- [Bounded production cardinal traversal assessment](world/cardinal-traversal-assessment.md)
- [Click-to-move route (ARC-WORLD-007: core and LMB in crash-site-area)](world/click-route-runtime.md)
- [Ordinary static-scenery obstruction contract — bounded component only](world/static-obstruction-contract.md)
- [Proposed selected-sector passability contract — NOT READY](world/passability-contract.md)

## Rendering, input and scripting

- [Static-scenery diagnostic contract (ARC-RES-008)](rendering/static-scenery-contract.md)
- [Terrain, static and bounded actor diagnostic viewer (ARC-RENDER-003/004/005)](rendering/terrain-viewer.md)
- [Bounded actor visual contract — original-runtime parity NOT READY](rendering/actor-visual-contract.md)
- [ARC-INPUT-001 diagnostic palette and WORLD keyboard/mouse route](input/diagnostic-palette.md)
- [Crash Site day/night ambient lighting (ARC-RENDER-010)](rendering/area-lighting.md)
- [ARC-RENDER-008 passability overlay](rendering/passability-overlay.md)
- [ARC-UI-001 display/UI validation](input/display-ui-validation.md)
- [Gamepad prototype evidence](input/gamepad-prototype.md)
- [Synthetic movement study](input/gamepad-movement-study.md)
- [Bounded CE local-pathfinding study](input/arcanum-pathfinding-study.md)
- [Minimal dialog contract (proposal only)](scripting/dialog-minimal-contract.md)
- [Bounded dialog transition runtime](scripting/dialog-transition-runtime.md)

## Research evidence

- [ART decoder CE-source-derived specification record](research/art-ce-spec.md)
- [ART research evidence record](research/art-evidence.md)
- [Actor visual evidence and anchor readiness gap (ARC-RES-009)](research/actor-visual-evidence.md)
- [Actor world-anchor placement evidence (ARC-RES-010)](research/actor-placement-evidence.md)
- [Crash Site evidence ledger](research/crash-site-evidence.md)
- [Crash Site sector framing: R3 correction](research/crash-site-framing.md)
- [Crash Site selected-sector objects](research/crash-site-objects.md)
- [Crash Site: frozen initial sector](research/crash-site-slice.md)
- [Crash Site terrain identifier resolution (ARC-RES-004)](research/crash-site-terrain-resolution.md)
- [Selected Crash Site sector passability evidence (ARC-RES-011)](research/sector-passability-evidence.md)
- [Static-obstruction evidence (ARC-RES-012)](research/static-obstruction-evidence.md)
- [Terrain obstruction component evidence (ARC-DATA-006)](research/terrain-obstruction-components-evidence.md)
- [DAT R1 evidence record](research/dat-evidence.md)
- [ARC-RES-005 dialogue evidence record](research/dialog-evidence.md)
- [Research method and R1 boundary](research/method.md)
- [Crash Site scenery placement and overlap (ARC-RES-008)](research/scenery-placement.md)

## Standalone labs

- [Display UI lab](../experiments/display-ui-lab/README.md)
- [Gamepad lab](../experiments/gamepad-lab/README.md)
- [Contributor guidance](../CONTRIBUTING.md)

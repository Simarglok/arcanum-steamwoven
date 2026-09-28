# Architecture direction

These are accepted product directions, not claims of implementation. Detailed implementation ADRs follow evidence from research.

| Decision | Direction |
|---|---|
| Independent runtime | No CE fork or CE runtime dependency; MIT for independently authored work. |
| Technology | C# core/data; Godot 4 presentation. Current pins and existing assemblies are recorded in [toolchain](../workflow/toolchain.md) and [status](../project/status.md); future boundaries still require evidence. |
| Original data | Read the player's original formats. Preserve identifiers and distinguish raw data from domain mapping. |
| Simulation boundary | Authoritative world/game state independent of rendering, UI and physical input. |
| Visual modes | Shared simulation for Legacy and later Remastered visuals; intended switching preserves world/action state. |
| Early graphics | Legacy first. Later upscale existing assets one-to-one; no new directions/frames or 3D required. |
| Display | Modern resolution, high-DPI and scalable UI are baseline, independent of HD assets. Viewport must not define simulation rules. |
| Controls | Keyboard/mouse and first-class gamepad. Accepted Xbox Series Bluetooth direction: left stick moves actor; right stick moves camera, both camera axes inverted by default and configurable. Manual camera cancels follow; after camera and actor stop, actor-only movement resumes recentering. This direction is not implemented in the merged ARC-GPAD-001 lab; see [vision](../project/vision.md). |
| Platform sequence | Develop/research on macOS now; validate Windows/Linux/handheld support later. |
| Compatibility | Original game is the principal behavioral reference; mods/patches need explicit versioned profiles. |

Proposed logical boundaries: Data, Simulation, Scripting, Navigation, Persistence, Presentation, Platform and Tools. Do not automatically create an assembly for every label.

Open decisions: exact compatibility baseline, archive precedence, time/RNG/action timeline, persistence format and presentation contracts. Unknowns are not implementation permission.

## Implementation status

This page records direction. The [component catalogue](../project/status.md) separates existing Data, Presentation, Scripting, viewer and lab assemblies from future simulation, navigation and persistence. Bounded diagnostic rendering and dialog transitions do not establish original-runtime parity.

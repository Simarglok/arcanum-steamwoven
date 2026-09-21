# Architecture direction

These are accepted product directions, not claims of implementation. Detailed implementation ADRs follow evidence from research.

| Decision | Direction |
|---|---|
| Independent runtime | No CE fork or CE runtime dependency; MIT for independently authored work. |
| Technology | C# core/data; Godot 4 presentation. Versions and assembly layout remain open. |
| Original data | Read the player's original formats. Preserve identifiers and distinguish raw data from domain mapping. |
| Simulation boundary | Authoritative world/game state independent of rendering, UI and physical input. |
| Visual modes | Shared simulation for Legacy and later Remastered visuals; intended switching preserves world/action state. |
| Early graphics | Legacy first. Later upscale existing assets one-to-one; no new directions/frames or 3D required. |
| Display | Modern resolution, high-DPI and scalable UI are baseline, independent of HD assets. Viewport must not define simulation rules. |
| Controls | Keyboard/mouse and first-class gamepad; semantic targeting and camera-centered interaction are hypotheses to test. |
| Platform sequence | Develop/research on macOS now; validate Windows/Linux/handheld support later. |
| Compatibility | Original game is the principal behavioral reference; mods/patches need explicit versioned profiles. |

Proposed logical boundaries: Data, Simulation, Scripting, Navigation, Persistence, Presentation, Platform and Tools. Do not automatically create an assembly for every label.

Open decisions: exact compatibility baseline, archive precedence, time/RNG/action timeline, SDK and Godot versions, persistence format and presentation contracts. Unknowns are not implementation permission.

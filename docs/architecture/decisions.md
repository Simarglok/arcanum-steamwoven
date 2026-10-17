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
| HD pack (2026-10-16) | Upscale of existing frames, characters and objects first. First set at 2×; after the measured plain upscales were rejected the same day (the edge kept its steps), the owner chose "1 + 3": a 4× soft-edge pack as the base (2× stays supported) and a generative experiment (ARC-RES-038); the pack format stays method-agnostic. A pack is built on a device from a recipe or imported from another of the owner's devices (e.g. Mac to a 16 GB Steam Deck class handheld), with per-device HD budgets. The owner builds the pack locally from his own installation; it stays outside the repository and is not distributed (pack, frames, screenshots and recordings never enter git). Git holds only code, the manifest format and the build recipe. Contract: [HD presentation](../rendering/hd-presentation-contract.md). |
| Display | Modern resolution, high-DPI and scalable UI are baseline, independent of HD assets. Viewport must not define simulation rules. |
| Controls | Keyboard/mouse retain original Arcanum actions and UI semantics; WASD actor movement is not a product binding. Custom controls and interface belong to gamepad mode. The [original-input requirements](../requirements/original-keyboard-mouse.md) define evidence and diagnostic-key boundaries. Keyboard/mouse and first-class gamepad. Xbox Series Bluetooth target: left stick moves actor; right stick moves camera. Historical ARC-GPAD-002/003 lab defaults invert both camera axes; current integrated WORLD defaults are false/false, independently configurable and separately persisted. Manual camera cancels follow; after camera and actor stop, actor-only movement resumes recentering. WORLD is diagnostic integration, not original-runtime parity or physical Bluetooth validation. ARC-GPAD-004 adds WORLD-only LT-out/RT-in camera zoom; actor, assets and UI are not scaled by it. See [vision](../project/vision.md). |
| Platform sequence | Develop/research on macOS now; validate Windows/Linux/handheld support later. |
| Compatibility | Original game is the principal behavioral reference; mods/patches need explicit versioned profiles. |
| Rule modes (2026-10-17) | Two game-wide rule modes (owner decision D8, extended to every domain): **Steamwoven**, the original with the recorded fixes (default), and **Original**, the retail 1.0.7.4 behaviour including the bugs of the original game confirmed in its executable. A CE reconstruction slip is never a mode difference, and the Original mode never reproduces a crash, hang or out-of-bounds access (owner decision 2026-10-17). The mode is one player setting kept outside saves; the settings menu switches it during play without a restart. Every domain owns a stateless profile of switches, the session composes them (`GameRules`) and each domain reads the active one at the moment it decides; no state depends on the mode, so any save loads under either. Contract and registry: [rule modes](../world/rule-modes.md); verdicts: [verification ledger](../research/original-verification-ledger.md). |

Proposed logical boundaries: Data, Simulation, Scripting, Navigation, Persistence, Presentation, Platform and Tools. Do not automatically create an assembly for every label.

Open decisions: exact compatibility baseline, archive precedence, time/RNG/action timeline, persistence format and presentation contracts. Unknowns are not implementation permission.

## Implementation status

This page records direction. The [component catalogue](../project/status.md) separates existing Data, Presentation, Scripting, viewer and lab assemblies from future simulation, navigation and persistence. Bounded diagnostic rendering and dialog transitions do not establish original-runtime parity.

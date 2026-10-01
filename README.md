# Arcanum Steamwoven

Research-first, independent reimplementation of **Arcanum: Of Steamworks and Magick Obscura**, using the player's original game data.

**Status (merged main `8cdb624`, 2026-09-29, through PR #24):** bounded DAT/ART/DLG readers, a headless dialog transition runtime, fixed-source terrain and static-object loaders, and ART/display diagnostics exist. The terrain viewer can combine terrain with five admitted static objects. The synthetic gamepad lab supports dual-stick movement, configurable camera inversion and Follow/Manual camera behavior. One actor’s idle/walk visual chain is researched, but its world-anchor placement remains unresolved. There is no integrated playable scene, full SCR VM, original-world navigation/collision, combat, or save/load, and original-runtime parity is unverified. See the [component catalogue](docs/project/status.md) for implemented scope, evidence and source links.

## Direction

- C# data/compatibility and simulation code independent of Godot.
- Godot 4.7.2 .NET for the bounded viewer presentation boundary; `Arcanum.Data` remains Godot-independent. See [Arcanum.Godot viewer](docs/Arcanum.Godot.md) for the shared-toolchain commands and known limits.
- Preserve original gameplay; distinguish original behavior, bug fixes and optional mod profiles.
- Modern resolutions, scalable UI and high-DPI from the early Legacy frontend.
- Keyboard/mouse and first-class gamepad interaction.
- Later visual update: upscale existing sprites one-to-one, preserving directions, frames, anchors and timing. Additional frames/directions and 3D are future options, not early commitments.

Players provide their own legally acquired copy. Original game assets, third-party binaries and extracted content are not distributed here. MIT applies to independently authored project code and documentation, not to Arcanum or archived third-party material. This unofficial fan project is not affiliated with the game's rights holders.

## Start here

- [Documentation index](docs/README.md)
- [Project vision](docs/project/vision.md) and [dependency roadmap](docs/project/roadmap.md)
- [Toolchain and checks](docs/workflow/toolchain.md)
- [Private local diagnostic launch and build retention](docs/workflow/local-development.md)
- [Architecture decisions](docs/architecture/decisions.md)
- [Research method and R1 boundary](docs/research/method.md)
- [DAT format boundary](docs/formats/dat.md)
- [ART format boundary](docs/formats/art.md)
- [Arcanum.Data API, limits and commands](docs/Arcanum.Data.md)
- [Arcanum.Godot viewer](docs/Arcanum.Godot.md)
- [Hermes delivery workflow](docs/workflow/hermes.md)
- [Contributor guidance](CONTRIBUTING.md)

**The Hermes Kanban board `arcanum-steamwoven` is authoritative for board-managed tasks and acceptance contracts.** Explicit user instructions may authorize bounded direct Codex work. Obsidian stores research and discussions; it is not a task queue. Repository documents describe product and engineering policy, not runnable tickets.

The local `toolkit/` archive is intentionally ignored. No task starts merely because a roadmap or research note exists.

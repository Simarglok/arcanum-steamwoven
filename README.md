# Arcanum Steamwoven

Research-first, independent reimplementation of **Arcanum: Of Steamworks and Magick Obscura**, using the player's original game data.

**Status:** `Arcanum.Data` implements the observed R1 `1TAD` DAT subset, bounded 8-bpp ART-to-RGBA decode, and read-only ART metadata inspection. `Arcanum.Godot` is a bounded macOS developer animation inspector with source/override FPS controls, one physical group timeline, and raw-hotspot diagnostic placement. It is not a game animation runtime: no gameplay events, action-frame behavior, simulation, writer, extractor, or archive-precedence policy has been implemented. Current development targets macOS; Windows, Linux and handheld PCs remain product goals.

## Direction

- C# data/compatibility and simulation code independent of Godot.
- Godot 4.7.2 .NET for the bounded viewer presentation boundary; `Arcanum.Data` remains Godot-independent. See [Arcanum.Godot viewer](docs/Arcanum.Godot.md) for the shared-toolchain commands and known limits.
- Preserve original gameplay; distinguish original behavior, bug fixes and optional mod profiles.
- Modern resolutions, scalable UI and high-DPI from the early Legacy frontend.
- Keyboard/mouse and first-class gamepad interaction.
- Later visual update: upscale existing sprites one-to-one, preserving directions, frames, anchors and timing. Additional frames/directions and 3D are future options, not early commitments.

Players provide their own legally acquired copy. Original game assets, third-party binaries and extracted content are not distributed here. MIT applies to independently authored project code and documentation, not to Arcanum or archived third-party material. This unofficial fan project is not affiliated with the game's rights holders.

## Start here

- [Architecture decisions](docs/architecture/decisions.md)
- [Research method and R1 boundary](docs/research/method.md)
- [DAT format boundary](docs/formats/dat.md)
- [ART format boundary](docs/formats/art.md)
- [Arcanum.Data API, limits and commands](docs/Arcanum.Data.md)
- [Arcanum.Godot viewer](docs/Arcanum.Godot.md)
- [Hermes delivery workflow](docs/workflow/hermes.md)
- [Contributor guidance](CONTRIBUTING.md)

**Tasks and acceptance contracts live in the Hermes Kanban board `arcanum-steamwoven`.** Obsidian stores research and discussions; it is not a task queue. Repository documents describe product and engineering policy, not runnable tickets.

The local `toolkit/` archive is intentionally ignored. No task starts merely because a roadmap or research note exists.

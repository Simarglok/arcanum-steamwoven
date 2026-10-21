# Arcanum Steamwoven

Research-first, independent reimplementation of **Arcanum: Of Steamworks and Magick Obscura**, using the player's original game data.

**Status (merged main `894b944`, 2026-10-16, through PR #157).** Not a playable game yet: every component is deliberately bounded and original-runtime parity is unverified. The [component catalogue](docs/project/status.md) lists implemented scope, evidence and source links.

- **Original data.** DAT and resource precedence over the original mount order, MES text and object names, ART, DLG (all 709 installed files), a universal map catalogue of all 81 maps, whole-map plans that open every interior through the area owner (52 of 80 publish a complete snapshot, with T0 step evidence), T0 inventory and the stored combat fields of weapons, ammunition, armour and critters.
- **Crash Site area viewer** (`./arcanum area`, since ARC-RENDER-028 the main world map's map mode at the Crash Site). The Crash Site exterior and the cave with return: click-to-move, hover names and action cursor, inventory window with pickup and loot, multi-turn NPC conversation, Virgil joining the party and following the PC, day/night and cave lighting on the session clock, one quick-save slot (F7/F8), the diagnostic palette (Ctrl+Shift+F9 or gamepad Start) and an optional local HD pack for characters and objects.
- **Headless rules** (Godot-free). One game-session owner (clock, global flags, story state, party, inventory, starting hero, seeded randomness, life state, snapshot schema), character stats, the starting hero from a pre-made PC, a pure attack resolver for one attack and the life rules (hit points, fatigue, poison, injuries, knockout, death).
- **Research** (merged, not runtime). Combat core, magic, skills, NPC reaction, the NPC combat AI plan, the SCR event dispatcher and opcode census, character creation, map transitions and HD upscale methods.

Not yet there: a viewer mode for other maps and map transitions beyond the cave, combat in the viewer, NPC AI, a full SCR runtime, quests and journal, levels and XP, save list, main menu, overland travel and Windows/Linux/handheld validation.

## Direction

- C# data/compatibility and simulation code independent of Godot.
- Godot 4.7.2 .NET for the bounded viewer presentation boundary; `Arcanum.Data` remains Godot-independent. See [Arcanum.Godot viewer](docs/Arcanum.Godot.md) for the shared-toolchain commands and known limits.
- Preserve original gameplay; distinguish original behavior, bug fixes and optional mod profiles.
- Modern resolutions, scalable UI and high-DPI from the early Legacy frontend.
- Keyboard/mouse and first-class gamepad interaction.
- Legacy graphics first. HD is an optional local pack: existing frames of characters and objects upscaled (4× soft-edge by default, 2× supported) with the same directions, frames, anchors and timing. Each player builds the pack from their own installation or imports it from another of their devices; it stays outside the repository and is never distributed ([HD pack format](docs/formats/hd-pack.md), [HD presentation contract](docs/rendering/hd-presentation-contract.md)). Additional frames/directions and 3D are future options, not commitments.

Players provide their own legally acquired copy. Original game assets, third-party binaries and extracted content are not distributed here. MIT applies to independently authored project code and documentation, not to Arcanum or archived third-party material. This unofficial fan project is not affiliated with the game's rights holders.

## Run the viewer

With the game installed at `/Applications/Arcanum` and an exported build present:

```sh
./arcanum setup    # find the installed game's inputs and configure the launcher
./arcanum map "crash site cave"   # one whole map, on any installation
./arcanum map Arcanum1-024-fixed  # the main world map, streamed by window from the new-game start (--sector X,Y for another place)
./arcanum area     # the Crash Site on the main world map (map mode; --hero NAME, --sector X,Y)
./arcanum world    # the WORLD diagnostic
./arcanum status   # which modes have every input configured
./arcanum clean    # which builds are protected and which may go (dry run)
```

Never delete `artifacts/` or a build in it by hand: the launcher and the export need the current builds. See
[which builds may be deleted](docs/workflow/build-retention.md).

The launcher runs the newest exported build of each mode; the features above need a build exported from current `main`. See [the project launcher](docs/workflow/local-development.md#project-launcher).

## Start here

- [Documentation index](docs/README.md)
- [Project vision](docs/project/vision.md) and [dependency roadmap](docs/project/roadmap.md)
- [Full-game functional requirements and bounded implementation coverage (draft)](docs/requirements/README.md)
- [Toolchain and checks](docs/workflow/toolchain.md)
- [Private local diagnostic launch and build retention](docs/workflow/local-development.md)
- [Architecture decisions](docs/architecture/decisions.md)
- [Research method and R1 boundary](docs/research/method.md)
- [DAT format boundary](docs/formats/dat.md)
- [ART format boundary](docs/formats/art.md)
- [Arcanum.Data API, limits and commands](docs/Arcanum.Data.md)
- [Arcanum.Godot viewer](docs/Arcanum.Godot.md)
- [Task workflow](docs/workflow/tasks.md) and [task naming](docs/workflow/task-naming.md)
- [Agent rules](AGENTS.md) and [the Claude session guide](CLAUDE.md)
- [Contributor guidance](CONTRIBUTING.md)

**Tasks and acceptance contracts come from the owner's explicit requests ([task workflow](docs/workflow/tasks.md)):** a direct instruction, a Claude project thread or a named `ARC-TYPE-NNN` work item. Each task is one PR, reviewed independently and merged by the agent that did it unless the owner asked to review it. Obsidian stores research and discussions; it is not a task queue. Repository documents describe product and engineering policy, not runnable tickets.

The local `toolkit/` archive is intentionally ignored. No task starts merely because a roadmap or research note exists.

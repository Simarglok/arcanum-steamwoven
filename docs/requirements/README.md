# Arcanum Functional Requirements — Register Draft

This register answers the question "which functions are needed and how much of each is already implemented". It supplements the [component catalogue](../project/status.md); it does not replace it. Tasks, assignees, priorities and permission to work are set by the owner ([work order](../workflow/tasks.md)). `FR-*` identifiers describe requirements and are not `ARC-*` tasks.

A convenient way to start a review is the [short overview](player-review.md), then open the [coverage matrix](coverage.md): each requirement there has a status, what is implemented, what is missing and links to documentation (full re-check 2026-11-11, snapshot at main `c254e84` — [re-check log](coverage.md#recheck-log)). The topic documents hold the requirements themselves, the original's behavior, acceptance and research; their "Steamwoven" field gives details as of the last edit, and the matrix keeps the current status. A task is checked against the register when it is created and updates the matrix in its PR ([rule](../workflow/tasks.md#functional-requirements)).

| Area | Requirement groups |
| --- | --- |
| World and transitions | 16 |
| Objects, buildings and roofs | 16 |
| Lighting and time | 13 |
| Character and skills | 26 |
| Combat, magic and technology | 25 |
| Items and economy | 19 |
| Dialogs, story, social systems and AI | 21 |
| Navigation, travel and runtime | 16 |
| UI, media and lifecycle | 16 |
| Data, compatibility and modern goals | 15 |
| Network play | 8 |
| Total | 191 |

This is the number of function groups, not the number of future tasks, an effort estimate or a percentage of a finished game.

## Research boundaries

The register covers the game systems that can be studied through the functions of the original `Arcanum.exe` 1.0.7.4, and the project's modern goals. **The technical descriptions are checked against the cited sources; the completeness of the catalogue and the proposed product details await owner approval.** The full catalogue and the scope of the nearest pre-alpha are determined separately.

Domain sections:

- [World and transitions](world.md): maps, sectors, the outdoor area, interiors/caves, entry and return, world state across a map change.
- [Objects and buildings](scene.md): scenery, trees, walls, physical doors and windows, roofs, draw order and overlap of the character.
- [Lighting and time](lighting-time.md): game clocks, time-of-day change, indoor/outdoor background colors and local light sources.
- [Character and skills](characters-skills.md): creation, attributes, development, skills and training, aptitude and special states.
- [Combat, magic and technology](combat-magic-tech.md): game combat modes, actions, damage/status, spells and the interaction of magic with technology.
- [Items and economy](items-economy.md): inventory, equipment, trade, crafting, use and state of items.
- [Dialogs, story, social systems and AI](story-social-ai.md): dialogs, SCR, quests, companions, reaction/reputation and NPC behavior.
- [Navigation, travel and world runtime](world-travel-runtime.md): movement and pathfinding, the global map, encounters, rest and related world mechanics.
- [UI, media and lifecycle](ui-media-lifecycle.md): game screens, controls, video/sound, startup, load/save and settings.
- [Data, compatibility and modern goals](content-product.md): the resource system and separately accepted project extensions.
- [Network play](network.md): creating and joining a network game, host authority, network clocks and limits, network modules, characters, server options, player parties, lobby and chat (added 2026-10-27, ARC-DEV-044; not alpha).

The detailed specification of the [original keyboard and mouse controls](original-keyboard-mouse.md) develops FR-UI-002, FR-NAV-001 and FR-PRODUCT-002. Its local KM items refine existing groups and do not increase the number 191 in the table above. Accepted direction: the original scheme and UI for keyboard/mouse, custom presets and UI for gamepad. The options for a diagnostic layer are not yet a chosen layout.

How completely each function group of the original has been checked, and the remaining gaps, are recorded in the [function-group census](source-coverage.md). A group's presence in the census does not mean that all its branches are checked. A missing binding or an empty function in the source that was read does not prove that the corresponding mechanic is absent from the original game.

This is a register of functions and of the limits of knowledge, not a specification of every formula, every spell and every quest branch. It does not establish the readiness of the whole Crash Site, does not determine the number of future tasks and does not promise a pre-alpha date. The full catalogue and the scope of the nearest milestone are separate decisions. Multiplayer, an editor, extra sprite directions and 3D may be recorded as researched/possible areas, keeping the accepted early non-goals.

A room under a roof in the current map and a separate interior map are different cases. The physical opening of a door and a `jumppoint` with a transfer to another map are also different functions. For the Crash Site cave there is already a [data/source inventory](../research/area-inventory.md) and a numeric exterior endpoint in the Area snapshot. Runtime activation, destination loading, return and parity are not yet confirmed; the name of a location does not prove the transition mechanism.

## Baseline and provenance

Initial research: **2026-10-02/03**. Implementation audit: **2026-10-09**, merged main [`a8a553b7c406ace77ea019bf2b13251f00ecddb6`](https://github.com/Simarglok/arcanum-steamwoven/tree/a8a553b7c406ace77ea019bf2b13251f00ecddb6), via **PR #54**. The number/IDs of the 183 groups, the behavior descriptions and the proposed acceptance scenarios are kept; coverage was changed, not the acceptance of new requirements.

Status re-check: **2026-10-20**, task ARC-DEV-027, merged main `e9a1356` (last merged PR #268): each row of the [matrix](coverage.md) received a status on the matrix scale, the implemented scope, the remainder and links to documentation; no runs were made. Then ARC-RES-107 re-checked the rows (2026-11-11, main `52f66db`), and ARC-RES-128 brought the snapshot up to main `c254e84` (PR #1048) and gathered what is done, working and remaining into the matrix section [“What is done, in progress and left”](coverage.md#what-is-done-in-progress-and-left). The paragraphs below describe the initial audit of 2026-10-09.

The audit maps all 54 merged PRs and their merge ancestry, the sources/tests/canonical API contracts and a read-only snapshot of the Hermes board `arcanum-steamwoven` of **2026-10-09 18:11:36 UTC**. The [component catalogue](../project/status.md) lists the delivered parts, research, specific historical checks and work not yet included in this baseline. A board root Done may mean completed decomposition/coordination; the product is counted by merged code and the declared scope. The mapping of old cards without FR fields is an audit conclusion, not a change of the card contract. This PR does not edit the board.

There are no new builds, game runs, or GUI/export/device/original-data experiments here. The PASS/counts cited are historical receipts of specific PRs/heads; they do not prove joint runtime validation of all subsystems on the audit main. The initial census of sources and its unchecked branches are kept as dated research. Later merges/board movements need a new snapshot; closed coordination cards, research self-tests and published/unmerged PRs do not close an FR.

The only source of the original's behavior is the retail `Arcanum.exe` 1.0.7.4; the verified facts are recorded in the [verification ledger](../research/original-verification-ledger.md). This report contains functional descriptions and references to function addresses; the original's code and listings are not part of the repository. See the [architecture direction](../architecture/decisions.md), the [research method](../research/method.md) and the [check in the original executable](../research/original-executable-check.md).

## Confirmation status

The topic descriptions and the stated limits are checked against the cited functions of the original (level F0 if there is no row in the verification ledger). Unknown bindings of the original data, unchecked branches and empty functions are marked in the relevant documents. The uniqueness of FR IDs, the match between the domain and the overall coverage tables, function addresses, local links and anchors have been checked. This is a check of documentation and sources; it does not confirm the completeness of formulas, the running of all game scenarios or parity with the original executable.

## How to read and update coverage

A requirement must have a stable ID, observable behavior, limits, a source, an acceptance scenario, the currently implemented scope and the remainder. IDs should be tied to tasks in the task contract or its PR; closing a task does not by itself close a requirement.

Since 2026-10-20 the matrix gives each requirement one status on the [scale](coverage.md#status-scale) (Implemented, Partial — in the viewer, Partial — no host, Researched, Not started), the implemented scope, the remainder and links to documentation. The scope is described along the four dimensions below; a dimension that is absent is named in the remainder:

| Dimension | What it confirms |
| --- | --- |
| Data | The needed original records are read and have an established meaning. Skipping a byte section is not enough. |
| Behavior | An independent runtime executes the required rules within the declared range. A diagnostic or synthetic policy must be named. |
| Integration | The behavior is connected to the shared scene/world and to the needed consumers, and does not exist only as an isolated API or lab. |
| Verification | There are specific checks and their conditions: synthetic, original-data, export, human visual, physical device or original-runtime comparison. These kinds of evidence are not interchangeable. |

Before 2026-10-20 the matrix rated each dimension in a separate column: `No`, `Partial`, `Implemented in the stated scope`, `Not researched` or `Not applicable` with a reason. "Partial" is always accompanied by a list of the covered cases and the missing branches. The full scope cannot be inferred from the presence of a parser, the number of functions of the original, green tests or the number of closed cards.

The state of knowledge about behavior is kept separately: **source-confirmed** (reading the original's functions at the cited addresses, level F0 if there is no row in the verification ledger), **project policy**, **inference**, **unknown**. Even a call verified in the exe does not mean **original-runtime-verified** (level F4) without observing the running game. An empty or unfinished function in the source that was read is a limit of the reading, not grounds to exclude a function of the original game.

When coverage changes, state the new merge commit/PR, the exactly covered sub-cases, the implementation path and checks, and the remaining limits. For data, state the area/versions/object types; for rules, the context and unsupported branches. The source of truth for coverage is the corresponding row and the detailed requirement in the domain document. Do not count an overall readiness percentage by the number of rows: a roof, a transition and reading a field have incomparable effort.

## Relation to the first playable slice

The "proposed for pre-alpha" marks in the domain documents are the authors' preliminary priorities, not an agreed minimum scope. They do not automatically include all capabilities of the corresponding domain in the first prototype and do not approve an implementation contract. A coherent demonstration of an outdoor area and one interior space also needs the adjacent functions already researched:

| Adjacent function | Current scope (2026-11-11, main `c254e84`) | Next boundary |
| --- | --- | --- |
| Reading data and maps | A universal catalogue of 81 maps through the content stack, full snapshots of Crash Site and of all 80 interiors, the main map as a window around the actor ([catalogue](../world/map-catalog.md), [plans](../world/whole-map-plans.md), [map mode](../rendering/area-map-mode.md)). | Objects and creatures outside the main map window. |
| Actor and movement | Click route, stick, walking, running and jumping on any viewer map with world state (doors, objects, traps), companions follow the hero ([route](../world/click-route-runtime.md), [gait](../world/gait-runtime.md), [components](../project/status/world.md)). | A spawner of creature generators and random encounters on the road. |
| Interaction and dialog | Conversation with an NPC on any map through the SCR dispatcher, consequences in the session, pickup and search, the barter window, a journal with tabs, the schematics window ([conversation](../scripting/dialog-conversation-runtime.md), [catalogue](../project/status.md)). | The wager and the newspaper queue, SCR actions and conditions that still refuse, training with trainers. |
| Character and combat | A ready hero, sheet, experience and levels, equipment, real-time combat and the turn-based combat round, NPC AI in the first fight, ranged combat, the spell host (colleges and technology entries) and item assembly by schematics ([attack](../world/hero-attack-runtime.md), [magic](../project/status/magic.md), [technology](../project/status/technology.md)). | The remaining spell components (states E4-a2, nested Cast, creature summoning), the fate window and the skills panel. |
| Light, time, sound | Session clocks, day and night, sector light, music (including the menu), ambience, positional sounds and voice ([lighting](../rendering/area-lighting.md), [sound](../audio/viewer-audio.md)). | NPC sleep and daily routine, a clock in the HUD, cell illumination for the darkness penalty. |
| Transitions and saving | Map transitions from data, the world map window and travel, companions cross with the hero, save and load windows, F7 autosave and F8 quick load ([transitions](../rendering/area-map-mode.md#map-transitions-arc-world-031)). | Random encounters, spell endings during rest and travel, saving the runs of inactive maps. |
| Interface and launch | Main menu, settings window, game panel and hero windows, death screen, the `./arcanum` launcher, the player package `./arcanum package` ([launcher](../workflow/local-development.md#project-launcher), [packaging](../workflow/packaging.md)). | Cutscenes and credits, adaptive window layout, package check on Windows. |

The first slice should show a coherent scenario: load a declared area, see its objects, move across permitted cells, interact with one NPC, go to an interior area and return with the saved state; then confirm consistent lighting by day/night and across transitions. The specific spaces, character, dialog and mandatory branches must be chosen from the original data. This is a proposal for further discussion, not permission to create the whole backlog automatically.

Legacy and future HD sprites must keep the world size and anchor; switching the visual mode keeps the simulation, the actor's position and the camera view. This [accepted product direction](../project/vision.md) is not extracted from the original and is not yet implemented. New directions/frames and 3D remain future options.

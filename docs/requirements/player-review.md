# Owner review of the register as a player

**Status: draft list, awaiting approval.** The register holds 191 groups of functions in 11 areas. The [coverage matrix](coverage.md) shows how much of each is already done, to what extent and where it is described (re-checked 2026-11-11, snapshot at main `c254e84`). Checking the sources helps find a system and its limits; familiarity with the original game is needed to check completeness and meaning. This document does not assign cards and does not approve the whole list as the pre-alpha scope. Technical confirmations, limits and the current implementation are in the [topic documents](README.md) and the [coverage matrix](coverage.md).

## What is proposed for review

| Area | Game functions in the register |
| --- | --- |
| World and scene | Maps and sectors, surfaces, trees and other objects, walls, doors, windows, roofs and character occlusion; entrances/exits, caves, party transfer and the changed state of visited areas. |
| Light and time | Game clock, day/night, stopping and waiting, outdoor/indoor lighting, local and carried light sources, the effect of lighting on the declared checks. |
| Character | Creation and backgrounds, race/gender/portrait, attributes and derived values, experience and development points, skills and training, aptitude, special states and fate. |
| Combat | Real time, turn-based and fast turn-based mode, queue, AP, target selection, hit/dodge/critical outcomes, weapons and ammunition, damage and resistances, fatigue/knockout/death, the sequence of game actions and animations; kill and injury log. |
| Magic and technology | Schools and prerequisites, choice and cost of actions, supported effects and their ending, summoning, special movement, interaction of magic/technology and item effects. |
| Items and economy | Inventory and containers, weight, quantities, equipment, keys, use/charges, identification, written items, damage/repair, trade, stock, gambling, schematics and assembly. |
| Story and social systems | Dialogs and reply conditions, events/scripts/flags, quests and journal, rumors/newspapers, reaction and reputation, followers and NPC behavior. |
| Navigation and travel | Local movement and path, obstacles and special restrictions, slippery and sinkable surfaces, detection, traps and lock picking, world map, travel/random encounters, rest and waiting. |
| Interface and media | HUD and game screens, quick actions, settings, video and ending sequences, music/sounds/voice, launch and exit, save/load. |
| Network play | Creating and joining a network game, host authority, network clocks and limits, network modules, characters, server options, player parties, lobby and chat (added 2026-10-27; not alpha). |
| Data and modern goals | Original resources and their priorities, modules/patches/mods, text, identity and randomness; modern resolutions/UI, gamepad, platforms and the future Legacy/HD toggle. |

This is a short table of contents, not a replacement for the detailed FRs. Similar actions may have different rules: a physical door and a map transition; skipping an animation and skipping game time; an ordinary save and importing an old save; an item and its visual representation.

## Where your review is especially useful

1. **Missing mechanics.** Is there an important game system that is absent from the table of contents or the detailed FRs? An example of an action and its visible result is especially useful, even without an explanation of the formula.
2. **Wrong groupings.** Are different capabilities merged under one row: turn-based combat modes, healing/resurrection, NPC attitude/reputation, ordinary and special travel?
3. **Door and building behavior.** Check the difference between passing within the same map and a real transition, roof hiding, the use of windows and obstacles. The numeric endpoint/source bindings for caves are already researched; activation, transition/return and runtime parity still need checking.
4. **Social and special character cases.** Reputation, reaction, followers, backgrounds, dialog and action particulars at different attributes are easily lost in a short list. Corrections from play experience are needed.
5. **Improvements and compatibility.** The list distinguishes restored functions from accepted modern goals. The new grid-release/gamepad rules are not declared an exact reproduction of the original. Specific pathfinding/gameplay changes need a separate decision.
6. **Early scope.** The early-candidate labels in individual reports are provisional. Accepting the full register does not mean a requirement to implement all races, spells, quests, trade and character creation before the first playable slice.

## Boundaries already accepted in the project

The founding principle is to preserve the game and to implement the engine separately in C#/Godot with MIT code of independent origin. The user supplies the data. The early graphics scope keeps the existing sprites; a quality upscale follows later, without mandatory new frames, directions or 3D. World size and camera framing are kept across a future Legacy/HD switch.

Multiplayer, a full editor, import of original saves and a full HD library do not become early commitments just because the original has a matching group of functions. Our own reliable save/load and the original dialogs/quests remain functions of the future game. The exact version of the original data and the patch profile are not established by this research.

## Implemented scope at the current snapshot

Snapshot of **2026-11-11**, main `c254e84` (last merged PR #1048); this is a text update from the [matrix](coverage.md), not a new game run. Of 191 requirements, 4 are implemented, 168 partly work in the viewer, 5 are merged without a host, 11 are researched and 3 are not started ([summary](coverage.md#сводка)); what is done, working and remaining per line of work is collected in the [snapshot](coverage.md#что-сделано-что-в-работе-что-осталось).

The game as a whole does not exist yet, but in the viewer (`./arcanum area`, `./arcanum map NAME`) the following already works: reading and showing any of the 81 maps (the main map as a window), walking, running and jumping, doors, traps, light and time of day, music, ambient sound and voice; talking to NPCs through the original's scripts, barter, the hero's journal, sheet and inventory, the schematics window and item assembly; real-time combat, the turn-based combat circle and NPC AI in the first fight; the spell host (some of the 80 college spells work fully, the rest await their components, see the [magic audit](../research/magic-audit-res-103.md)) and technology records (at least 74 of 83 by the census test, see the [technology audit](../research/technology-audit-res-125.md)); companions, transitions between maps, the main menu, the settings window, the save and load windows, the hero death screen. The player package is built with the `./arcanum package` command.

Not working or unfinished: cutscenes and credits, sleep and the NPC day, random encounters, the creature generator spawner, betting and the newspaper window, some SCR actions and conditions, the fate window and the skills panel, the remaining spell components, network play (deferred in the project), checking on Windows. The detailed list with reasons is in the rows of the [matrix](coverage.md) and in the [missing systems plan](../project/missing-systems-plan.md). Modern resolutions and HiDPI were checked in labs and partly wired into the windows; there is no on-device legibility check.

## How to submit corrections

To change an existing item, it is enough to name the `FR-*` and describe the expected behavior. For a missing mechanic, give a name and an example of the player's action and its result. After approval the register is saved with a mark of the accepted version; later findings are added with new IDs, and current IDs are not renumbered. The owner sets the implementation queue separately.

Original formulas, the content of each quest branch and full parity with the original executable are not confirmed by this approval. Such claims need their own research and checks.

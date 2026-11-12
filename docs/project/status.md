# Project status and component catalogue

The catalogue was seeded by a source/delivery audit of merged main through PR #54 (2026-10-09) and has been kept current by the PRs that change a component since; a component's row is the place to look for what is merged, its evidence and its remaining boundary. Root-card `done` alone is not product delivery.

This is a source/delivery catalogue, **not a new build, game, original-data, graphical, export or device run**. Neither green tests nor a merge establishes original executable parity, and the tests of separate components do not certify every subsystem together. The [191-group requirements register](../requirements/README.md) gives each requirement one status with what is implemented, what is missing and documentation links in its [coverage matrix](../requirements/coverage.md); no whole-game readiness percentage is inferred.

## Implemented components

One line per component; the full row (merged scope, implementation and contract links, remaining boundary) is in its area file: [Data and formats](status/data.md), [World, maps and movement](status/world.md), [Viewer, rendering, HD and input](status/viewer.md), [Audio](status/audio.md), [Game session and character](status/session.md), [Combat](status/combat.md), [Magic](status/magic.md), [Technology](status/technology.md), [Skills](status/skills.md), [Items and inventory](status/items.md), [Dialog and scripting](status/scripting.md), [NPC AI, reaction and companions](status/npc.md), [Verification and tools](status/tools.md). Tasks are the first task codes the row names.

| Component | Area | Tasks |
| --- | --- | --- |
| DAT | [Data and formats](status/data.md) | — |
| Resource precedence | [Data and formats](status/data.md) | ARC-DATA-026, ARC-DATA-011 |
| Launcher settings and game folder | [Data and formats](status/data.md) | ARC-DEV-046 |
| Settings file (arcanum.cfg) | [Data and formats](status/data.md) | ARC-UI-1130 |
| Object names and MES text | [Data and formats](status/data.md) | ARC-DATA-042, ARC-DATA-013, ARC-UI-005 |
| Universal map catalogue | [Data and formats](status/data.md) | ARC-DATA-026, ARC-DATA-015, ARC-DATA-025 |
| Whole-map plans | [Data and formats](status/data.md) | ARC-RENDER-021, ARC-RENDER-022, ARC-DATA-020 |
| Whole-map steps | [World, maps and movement](status/world.md) | ARC-DATA-034, ARC-DATA-027, ARC-DATA-028 |
| Map list and transitions | [Data and formats](status/data.md) | ARC-RES-035, ARC-SCRIPT-017, ARC-DATA-025 |
| Roofs (data and rules) | [World, maps and movement](status/world.md) | ARC-RENDER-021, ARC-RENDER-022 |
| Viewer map mode | [Viewer, rendering, HD and input](status/viewer.md) | ARC-RENDER-028, ARC-RENDER-034, ARC-RENDER-018 |
| Area T0 inventory view | [Items and inventory](status/items.md) | ARC-RES-023, ARC-DATA-012 |
| Viewer interface sounds and volumes | [Audio](status/audio.md) | ARC-UI-044, ARC-UI-046 |
| Viewer dialogue voice | [Audio](status/audio.md) | ARC-UI-043 |
| Viewer positional sounds | [Audio](status/audio.md) | ARC-UI-042 |
| Viewer music and ambient | [Audio](status/audio.md) | ARC-UI-041, ARC-UI-042, ARC-UI-043 |
| Sound cues of world events | [Audio](status/audio.md) | ARC-WORLD-041, ARC-UI-042 |
| Session music and ambient state | [Audio](status/audio.md) | ARC-WORLD-040, ARC-UI-041 |
| Viewer sound output | [Audio](status/audio.md) | ARC-UI-040, ARC-UI-041, ARC-UI-042 |
| Sound catalogue | [Audio](status/audio.md) | ARC-DATA-040, ARC-DATA-041 |
| Combat fields of items and critters | [Combat](status/combat.md) | ARC-DATA-026, ARC-DATA-021, ARC-WORLD-028 |
| Spell records | [Magic](status/magic.md) | ARC-MAGIC-001, ARC-MAGIC-002 |
| Spell cost and payment | [Magic](status/magic.md) | ARC-MAGIC-003, ARC-TECHNOLOGY-007 |
| Charge cells | [Technology](status/technology.md) | ARC-TECHNOLOGY-007, ARC-TECHNOLOGY-009, ARC-WORLD-066 |
| Tech devices | [Technology](status/technology.md) | ARC-TECHNOLOGY-008, ARC-MAGIC-009, ARC-TECHNOLOGY-007 |
| Spell book | [Magic](status/magic.md) | ARC-MAGIC-004 |
| Spell resistance | [Magic](status/magic.md) | ARC-MAGIC-005 |
| Spell invocation check | [Magic](status/magic.md) | ARC-MAGIC-006 |
| Spell target areas | [Magic](status/magic.md) | ARC-MAGIC-007 |
| Spell run rules | [Magic](status/magic.md) | ARC-MAGIC-009, ARC-MAGIC-010 |
| Spell per-target stage | [Magic](status/magic.md) | ARC-MAGIC-011, ARC-MAGIC-013 |
| Spell damage and heal | [Magic](status/magic.md) | ARC-MAGIC-012 |
| Spell cast host and viewer casting | [Magic](status/magic.md) | ARC-MAGIC-014, ARC-MAGIC-015 |
| Spell eye candy | [Magic](status/magic.md) | ARC-MAGIC-017 |
| Item spell triggers | [Magic](status/magic.md) | ARC-MAGIC-020 |
| Aptitude inputs | [Magic](status/magic.md) | ARC-MAGIC-028 |
| Weapon spell pane | [Magic](status/magic.md) | ARC-UI-016 |
| Spell pane in the bottom strip | [Magic](status/magic.md) | ARC-UI-053 |
| Spell missile and caster facing | [Magic](status/magic.md) | ARC-MAGIC-018 |
| Schematic data | [Technology](status/technology.md) | ARC-TECHNOLOGY-001 |
| ART and playback | [Data and formats](status/data.md) | ARC-DATA-016 |
| HD pack format | [Viewer, rendering, HD and input](status/viewer.md) | ARC-DATA-014, ARC-RENDER-016, ARC-DEV-003 |
| HD render path | [Viewer, rendering, HD and input](status/viewer.md) | ARC-RENDER-014, ARC-RENDER-016, ARC-UI-006 |
| HD before/after review | [Viewer, rendering, HD and input](status/viewer.md) | ARC-RENDER-015, ARC-DEV-003, ARC-RES-038 |
| Terrain/static regression profile | [Data and formats](status/data.md) | — |
| Complete exterior Data | [Data and formats](status/data.md) | — |
| Area day/night ambient | [World, maps and movement](status/world.md) | ARC-WORLD-010, ARC-RENDER-010 |
| Area cave and stationary lights | [World, maps and movement](status/world.md) | ARC-DATA-027, ARC-DATA-039, ARC-RENDER-030 |
| Whole-core step assessment | [World, maps and movement](status/world.md) | — |
| Directed step assessment | [World, maps and movement](status/world.md) | — |
| Exact movement | [World, maps and movement](status/world.md) | — |
| Click-to-move route | [World, maps and movement](status/world.md) | ARC-WORLD-073, ARC-WORLD-042, ARC-WORLD-060 |
| Hover tooltip and action cursor | [Viewer, rendering, HD and input](status/viewer.md) | ARC-UI-005, ARC-UI-056, ARC-UI-057 |
| Health and fatigue bars | [Viewer, rendering, HD and input](status/viewer.md) | ARC-UI-020, ARC-UI-024 |
| Floating text | [Viewer, rendering, HD and input](status/viewer.md) | ARC-UI-021 |
| Inventory window, pickup and loot | [Viewer, rendering, HD and input](status/viewer.md) | ARC-WORLD-014, ARC-SCRIPT-013, ARC-UI-004 |
| Item cursor | [Viewer, rendering, HD and input](status/viewer.md) | ARC-DEV-365 |
| Character sheet | [Viewer, rendering, HD and input](status/viewer.md) | ARC-UI-009, ARC-MAGIC-008, ARC-UI-007 |
| Logbook | [Viewer, rendering, HD and input](status/viewer.md) | ARC-UI-008, ARC-UI-074 |
| Window art warm-up | [Viewer, rendering, HD and input](status/viewer.md) | ARC-DEV-369 |
| Save list | [Viewer, rendering, HD and input](status/viewer.md) | ARC-UI-010 |
| Main menu | [Viewer, rendering, HD and input](status/viewer.md) | ARC-UI-011, ARC-UI-012 |
| New-character window | [Viewer, rendering, HD and input](status/viewer.md) | ARC-UI-030, ARC-UI-033 |
| Scene flow | [Viewer, rendering, HD and input](status/viewer.md) | ARC-UI-012 |
| Launcher screen | [Viewer, rendering, HD and input](status/viewer.md) | ARC-UI-014, ARC-DEV-074, ARC-DEV-069, ARC-DEV-082 |
| Debug mode | [Viewer, rendering, HD and input](status/viewer.md) | ARC-DEV-050 |
| Interface bars | [Viewer, rendering, HD and input](status/viewer.md) | ARC-UI-018, ARC-UI-023, ARC-UI-1111 |
| WORLD diagnostic integration | [Viewer, rendering, HD and input](status/viewer.md) | ARC-INPUT-001 |
| Human presentation | [Viewer, rendering, HD and input](status/viewer.md) | — |
| Input/camera/zoom | [Viewer, rendering, HD and input](status/viewer.md) | ARC-DEV-032, ARC-INPUT-003 |
| Display UI lab | [Viewer, rendering, HD and input](status/viewer.md) | — |
| DLG and Direct transition | [Dialog and scripting](status/scripting.md) | ARC-DATA-017 |
| NPC dialog entry | [Dialog and scripting](status/scripting.md) | — |
| Global flags | [Dialog and scripting](status/scripting.md) | — |
| Game session state | [Game session and character](status/session.md) | ARC-SCRIPT-006, ARC-UI-008, ARC-WORLD-010 |
| Item durability | [Items and inventory](status/items.md) | ARC-WORLD-053, ARC-COMBAT-007, ARC-SKILLS-007 |
| Lock state of portals and containers | [World, maps and movement](status/world.md) | ARC-WORLD-050, ARC-WORLD-051, ARC-SKILLS-006 |
| World-map travel rules | [World, maps and movement](status/world.md) | ARC-WORLD-056, ARC-UI-048 |
| Character stats | [Game session and character](status/session.md) | ARC-WORLD-012, ARC-WORLD-015 |
| Rule modes | [Game session and character](status/session.md) | ARC-WORLD-023, ARC-WORLD-024, ARC-WORLD-026 |
| Attack resolver | [Combat](status/combat.md) | ARC-COMBAT-001, ARC-SKILLS-003, ARC-COMBAT-007 |
| Life state | [Combat](status/combat.md) | ARC-COMBAT-002, ARC-WORLD-024 |
| NPC reaction | [NPC AI, reaction and companions](status/npc.md) | ARC-WORLD-026, ARC-RES-028, ARC-WORLD-043 |
| Turn-based round | [Combat](status/combat.md) | ARC-COMBAT-003, ARC-RES-030 |
| Real-time combat | [Combat](status/combat.md) | ARC-COMBAT-004, ARC-RES-030, ARC-COMBAT-005 |
| Hero attack | [Combat](status/combat.md) | ARC-COMBAT-005, ARC-COMBAT-007, ARC-COMBAT-012 |
| Object damage | [Combat](status/combat.md) | ARC-COMBAT-008, ARC-WORLD-065, ARC-COMBAT-011 |
| Critical effects | [Combat](status/combat.md) | ARC-COMBAT-009, ARC-WORLD-059, ARC-COMBAT-010 |
| Placed items | [Items and inventory](status/items.md) | ARC-WORLD-059, ARC-COMBAT-010, ARC-WORLD-070 |
| Junk piles | [Items and inventory](status/items.md) | ARC-WORLD-063 |
| Equipment | [Items and inventory](status/items.md) | ARC-WORLD-028, ARC-WORLD-034, ARC-WORLD-066 |
| Durable effects | [Game session and character](status/session.md) | ARC-WORLD-029, ARC-WORLD-035 |
| Experience and levels | [Game session and character](status/session.md) | ARC-WORLD-030, ARC-WORLD-033, ARC-UI-046 |
| Auto-level schemes | [Game session and character](status/session.md) | ARC-WORLD-036, ARC-SCRIPT-016, ARC-WORLD-047 |
| Game event queue core | [Game session and character](status/session.md) | ARC-WORLD-022, ARC-RES-046, ARC-WORLD-024 |
| Skill rules | [Skills](status/skills.md) | ARC-SKILLS-001, ARC-SKILLS-002, ARC-SKILLS-003 |
| Hero skills | [Skills](status/skills.md) | ARC-SKILLS-002, ARC-SKILLS-004, ARC-WORLD-045 |
| Character point pool | [Game session and character](status/session.md) | ARC-WORLD-052, ARC-UI-009, ARC-MAGIC-004 |
| Tech degrees | [Technology](status/technology.md) | ARC-TECHNOLOGY-002, ARC-TECHNOLOGY-005, ARC-TECHNOLOGY-006 |
| Schematic knowledge | [Technology](status/technology.md) | ARC-TECHNOLOGY-003, ARC-UI-049, ARC-TECHNOLOGY-005 |
| Schematic assembly | [Technology](status/technology.md) | ARC-TECHNOLOGY-004, ARC-WORLD-070, ARC-TECHNOLOGY-006 |
| Schematic window | [Technology](status/technology.md) | ARC-TECHNOLOGY-005, ARC-RES-062, ARC-TECHNOLOGY-006 |
| Powered shield | [Technology](status/technology.md) | ARC-MAGIC-041, ARC-RES-112 |
| Reading window | [Viewer, rendering, HD and input](status/viewer.md) | ARC-UI-049, ARC-SCRIPT-020, ARC-TECHNOLOGY-009 |
| Sleep and wait window | [Viewer, rendering, HD and input](status/viewer.md) | ARC-WORLD-1160 |
| NPC inventory changes | [Items and inventory](status/items.md) | ARC-WORLD-055, ARC-TECHNOLOGY-006 |
| Ammunition | [Items and inventory](status/items.md) | ARC-WORLD-066, ARC-TECHNOLOGY-009, ARC-TECHNOLOGY-007 |
| NPC gold and transfers | [Items and inventory](status/items.md) | ARC-WORLD-071 |
| Weight and encumbrance | [Items and inventory](status/items.md) | ARC-WORLD-085 |
| Hero keys | [Items and inventory](status/items.md) | ARC-WORLD-123 |
| Barter window | [Items and inventory](status/items.md) | ARC-UI-050 |
| Backgrounds | [Game session and character](status/session.md) | ARC-WORLD-069, ARC-WORLD-097, ARC-UI-030 |
| Background table | [Game session and character](status/session.md) | ARC-DATA-044 |
| Portrait catalogue | [Game session and character](status/session.md) | ARC-DATA-045 |
| Starting hero | [Game session and character](status/session.md) | ARC-RES-027, ARC-WORLD-027, ARC-DATA-026 |
| Skill check | [Skills](status/skills.md) | ARC-SKILLS-003, ARC-SKILLS-005, ARC-SKILLS-006 |
| Fate points | [Skills](status/skills.md) | ARC-WORLD-054 |
| Heal | [Skills](status/skills.md) | ARC-SKILLS-005 |
| Pick locks | [Skills](status/skills.md) | ARC-SKILLS-006, ARC-WORLD-050 |
| Repair | [Skills](status/skills.md) | ARC-SKILLS-007, ARC-WORLD-053 |
| Stealth | [Skills](status/skills.md) | ARC-SKILLS-008 |
| Pick pocket | [Skills](status/skills.md) | ARC-SKILLS-010 |
| NPC conversation | [Dialog and scripting](status/scripting.md) | ARC-SCRIPT-005, ARC-WORLD-043, ARC-WORLD-044 |
| Walking and running | [World, maps and movement](status/world.md) | ARC-WORLD-020, ARC-WORLD-021 |
| Window jump | [World, maps and movement](status/world.md) | ARC-WORLD-021, ARC-RES-049 |
| Companion follow | [NPC AI, reaction and companions](status/npc.md) | ARC-WORLD-016, ARC-WORLD-074, ARC-WORLD-020, ARC-WORLD-075 |
| Wait timer and loyalty | [NPC AI, reaction and companions](status/npc.md) | ARC-WORLD-045, ARC-WORLD-046 |
| Companion orders | [NPC AI, reaction and companions](status/npc.md) | ARC-WORLD-046, ARC-WORLD-045 |
| Companion map change | [NPC AI, reaction and companions](status/npc.md) | ARC-WORLD-017 |
| Record resolver | [World, maps and movement](status/world.md) | ARC-DEV-072 |
| World hosts (MIG15) | [World, maps and movement](status/world.md) | ARC-DEV-217..226, 256, 257 |
| Change-of-place request | [World, maps and movement](status/world.md) | ARC-DEV-112, ARC-DEV-113, ARC-DEV-114, ARC-DEV-115, ARC-DEV-204 |
| Goal rules (run table) | [World, maps and movement](status/world.md) | ARC-DEV-131, ARC-DEV-132, ARC-DEV-134, ARC-DEV-135 |
| Interface mode stack | [World, maps and movement](status/world.md) | ARC-DEV-108, ARC-DEV-109, ARC-DEV-110 |
| Sector cache model | [World, maps and movement](status/world.md) | ARC-DEV-102 |
| Object directory | [World, maps and movement](status/world.md) | ARC-DEV-084, ARC-DEV-086 |
| Residents of the open map | [World, maps and movement](status/world.md) | ARC-DEV-103 |
| Sector fold | [World, maps and movement](status/world.md) | ARC-DEV-104 |
| NPC waypoints and standpoints | [World, maps and movement](status/world.md) | ARC-DEV-117 |
| Heartbeat gating | [World, maps and movement](status/world.md) | ARC-DEV-105 |
| Sector terrain and the main map's residency | [World, maps and movement](status/world.md) | ARC-DEV-118 |
| NPC danger state | [NPC AI, reaction and companions](status/npc.md) | ARC-WORLD-079, ARC-COMBAT-004, ARC-WORLD-026 |
| NPC hostility and targets | [NPC AI, reaction and companions](status/npc.md) | ARC-WORLD-080, ARC-WORLD-079, ARC-WORLD-078 |
| NPC morale | [NPC AI, reaction and companions](status/npc.md) | ARC-WORLD-082, ARC-WORLD-080, ARC-WORLD-079 |
| NPC AI scheduler | [NPC AI, reaction and companions](status/npc.md) | ARC-WORLD-084, ARC-WORLD-079, ARC-WORLD-024 |
| NPC AI executor | [NPC AI, reaction and companions](status/npc.md) | ARC-WORLD-086, ARC-WORLD-084, ARC-WORLD-082 |
| NPC AI support and magic | [NPC AI, reaction and companions](status/npc.md) | ARC-WORLD-088, ARC-WORLD-086, ARC-MAGIC-014 |
| NPC AI action goals | [NPC AI, reaction and companions](status/npc.md) | ARC-WORLD-089, ARC-WORLD-088, ARC-WORLD-086 |
| NPC AI followers in combat | [NPC AI, reaction and companions](status/npc.md) | ARC-WORLD-091, ARC-WORLD-080, ARC-WORLD-046 |
| NPC AI viewer host | [NPC AI, reaction and companions](status/npc.md) | ARC-WORLD-092, ARC-WORLD-086, ARC-WORLD-084 |
| NPC awareness rules | [NPC AI, reaction and companions](status/npc.md) | ARC-WORLD-078, ARC-COMBAT-012, ARC-SKILLS-008 |
| Function oracle | [Verification and tools](status/tools.md) | ARC-DEV-007, ARC-WORLD-033, ARC-SKILLS-001 |
| Executable interpreter | [Verification and tools](status/tools.md) | ARC-DEV-067 |
| Prototype builder | [Verification and tools](status/tools.md) | ARC-DEV-068 |
| Map aggregate builder | [Verification and tools](status/tools.md) | ARC-DEV-082 |
| Function registry | [Verification and tools](status/tools.md) | ARC-DEV-008, ARC-DEV-023, ARC-DEV-009 |
| Local workflow/research tools | [Verification and tools](status/tools.md) | ARC-DEV-026, ARC-DEV-028, ARC-DEV-033, ARC-DEV-041, ARC-DEV-043, ARC-DEV-055, ARC-DEV-059 |
| Packaged alpha build (macOS, Windows) | [Verification and tools](status/tools.md) | ARC-DEV-047, ARC-DEV-048, ARC-DEV-049, ARC-DEV-062 |
| In-game bug report | [Verification and tools](status/tools.md) | ARC-DEV-051 |

## Merged research, not production runtime

One line per subject; the full row (canonical result, what remains) is in its area file.

| Subject | Area | Tasks |
| --- | --- | --- |
| Area/cave and connectors | [World, maps and movement](status/world.md) | ARC-WORLD-009, ARC-RENDER-011, ARC-RENDER-012 |
| Whole-exterior traversal | [World, maps and movement](status/world.md) | — |
| Time and lighting | [World, maps and movement](status/world.md) | — |
| Character creation and starting hero | [Game session and character](status/session.md) | ARC-RES-025, ARC-RES-027 |
| Dialog state and replies | [Dialog and scripting](status/scripting.md) | — |
| Negative inventory transfer | [Items and inventory](status/items.md) | — |
| AI parameter table | [NPC AI, reaction and companions](status/npc.md) | ARC-DATA-033, ARC-WORLD-037, ARC-WORLD-077 |
| Companion follow (Virgil) | [NPC AI, reaction and companions](status/npc.md) | ARC-RES-024, ARC-WORLD-013, ARC-WORLD-016 |
| Virgil's follow consent | [NPC AI, reaction and companions](status/npc.md) | ARC-RES-026 |
| Virgil's auto-level and level gate | [NPC AI, reaction and companions](status/npc.md) | ARC-RES-031, ARC-WORLD-036 |
| Original data census | [Data and formats](status/data.md) | ARC-RES-036, ARC-DATA-015, ARC-RES-034 |
| Map catalogue and transitions | [Data and formats](status/data.md) | ARC-RES-035, ARC-DATA-015, ARC-DATA-025 |
| Virgil's level-up replay | [NPC AI, reaction and companions](status/npc.md) | ARC-RES-032, ARC-WORLD-036 |
| Virgil's scheme-101 event | [NPC AI, reaction and companions](status/npc.md) | ARC-RES-037 |
| Crash Site items and inventory | [Items and inventory](status/items.md) | ARC-RES-023, ARC-DATA-012 |
| First building: doors, windows, roofs | [World, maps and movement](status/world.md) | ARC-RES-029, ARC-RES-065 |
| World-map travel | [World, maps and movement](status/world.md) | ARC-RES-068, ARC-WORLD-056 |
| Lights on all maps | [World, maps and movement](status/world.md) | ARC-RES-069 |
| Generated wilderness | [World, maps and movement](status/world.md) | ARC-RES-034, ARC-DATA-030, ARC-DATA-031 |
| NPC reaction and reputation | [NPC AI, reaction and companions](status/npc.md) | ARC-RES-028, ARC-WORLD-026, ARC-WORLD-044 |
| Combat core | [Combat](status/combat.md) | ARC-RES-030, ARC-COMBAT-002, ARC-COMBAT-001 |
| NPC combat AI plan | [NPC AI, reaction and companions](status/npc.md) | ARC-RES-039 |
| SCR event dispatcher | [Dialog and scripting](status/scripting.md) | ARC-RES-042, ARC-RES-039, ARC-RES-045 |
| Game data fixes | [Data and formats](status/data.md) | ARC-DATA-022, ARC-DATA-037 |
| Script and dialog content | [Dialog and scripting](status/scripting.md) | ARC-SCRIPT-011, ARC-SCRIPT-012, ARC-DATA-022 |
| SCR set auto-level scheme | [Dialog and scripting](status/scripting.md) | ARC-SCRIPT-016, ARC-TECHNOLOGY-005, ARC-UI-049 |
| SCR story state and story actions | [Dialog and scripting](status/scripting.md) | ARC-SCRIPT-019 |
| SCR story and time commands | [Dialog and scripting](status/scripting.md) | ARC-SCRIPT-1165 |
| SCR object variables and calls | [Dialog and scripting](status/scripting.md) | ARC-SCRIPT-018 |
| SCR teleports and guards | [Dialog and scripting](status/scripting.md) | ARC-SCRIPT-014, ARC-SCRIPT-015, ARC-UI-049 |
| SCR deferred calls | [Dialog and scripting](status/scripting.md) | ARC-SCRIPT-010, ARC-WORLD-039 |
| SCR print line and item insert scripts | [Dialog and scripting](status/scripting.md) | ARC-SCRIPT-013 |
| SCR wield events and item effects | [Dialog and scripting](status/scripting.md) | ARC-SCRIPT-042 |
| SCR dialogue through the dispatcher | [Dialog and scripting](status/scripting.md) | ARC-SCRIPT-009 |
| SCR dispatcher core | [Dialog and scripting](status/scripting.md) | ARC-SCRIPT-008, ARC-RES-048, ARC-SCRIPT-040 |
| SCR program and admission | [Dialog and scripting](status/scripting.md) | ARC-SCRIPT-007, ARC-RES-045, ARC-SCRIPT-008 |
| SCR opcode and attachment census | [Dialog and scripting](status/scripting.md) | ARC-RES-045 |
| Game event queue | [Game session and character](status/session.md) | ARC-RES-046, ARC-WORLD-022, ARC-WORLD-024 |
| Multiplayer of the original | [Game session and character](status/session.md) | ARC-RES-072 |
| Launcher and settings | [Game session and character](status/session.md) | ARC-RES-074 |
| Combat-domain load map | [Combat](status/combat.md) | ARC-RES-040, ARC-RES-041 |
| Combat-domain data census | [Combat](status/combat.md) | ARC-RES-041 |
| NPC AI core loop | [NPC AI, reaction and companions](status/npc.md) | ARC-RES-054, ARC-DATA-033 |
| NPC AI script and dialog surface | [NPC AI, reaction and companions](status/npc.md) | ARC-RES-070 |
| NPC AI followers in combat | [NPC AI, reaction and companions](status/npc.md) | ARC-RES-067, ARC-WORLD-046, ARC-RES-064 |
| NPC AI action execution | [NPC AI, reaction and companions](status/npc.md) | ARC-RES-066, ARC-COMBAT-007 |
| NPC AI support and magic | [NPC AI, reaction and companions](status/npc.md) | ARC-RES-059, ARC-RES-043 |
| NPC AI awareness | [NPC AI, reaction and companions](status/npc.md) | ARC-RES-056, ARC-SKILLS-003 |
| NPC AI morale | [NPC AI, reaction and companions](status/npc.md) | ARC-RES-058 |
| NPC AI hostility and targeting | [NPC AI, reaction and companions](status/npc.md) | ARC-RES-057 |
| Magic core | [Magic](status/magic.md) | ARC-RES-043, ARC-RES-030, ARC-RES-060 |
| Magic implementation plan | [Magic](status/magic.md) | ARC-RES-060, ARC-MAGIC-001, ARC-MAGIC-002 |
| Skills | [Skills](status/skills.md) | ARC-RES-044, ARC-SKILLS-001, ARC-SKILLS-003 |
| Skills plan | [Skills](status/skills.md) | ARC-RES-061, ARC-RES-044 |
| Technology | [Technology](status/technology.md) | ARC-RES-062, ARC-RES-043, ARC-TECHNOLOGY-001 |
| HD upscale methods | [Viewer, rendering, HD and input](status/viewer.md) | ARC-RES-033, ARC-RENDER-015 |
| Original verification ledger | [Verification and tools](status/tools.md) | ARC-RES-048, ARC-RES-051, ARC-DEV-007 |
| Running and window jumps | [World, maps and movement](status/world.md) | ARC-RES-047, ARC-WORLD-020, ARC-WORLD-021 |
| Whole-map art gaps | [Data and formats](status/data.md) | ARC-RES-050, ARC-DATA-023, ARC-DATA-024 |
| Audio study and plan | [Audio](status/audio.md) | ARC-RES-052, ARC-DATA-040, ARC-UI-040 |
| Generative HD re-detailing | [Viewer, rendering, HD and input](status/viewer.md) | ARC-RES-038, ARC-RES-053 |
| HD alternatives | [Viewer, rendering, HD and input](status/viewer.md) | ARC-RES-053 |
| Original input and navigation | [World, maps and movement](status/world.md) | — |
| Screen size and UI layout | [Viewer, rendering, HD and input](status/viewer.md) | ARC-RES-093 |

## Still missing for a playable game

State of 2026-11-11 (main `4dc5162`), checked against the code by ARC-RES-107 and again by ARC-DEV-1110, and the [missing systems plan](missing-systems-plan.md#3-map-of-the-systems) (section 3.1 names each system with its file and symbol). This is a list of what the player cannot do yet, not of what exists: the viewer plays the opening map and the world map in the original's window, and the components in the tables above are integrated into it.

What is integrated and working in the viewer (details in the [coverage matrix](../requirements/coverage.md)): whole-map rendering with passability and the sector cache, game lighting and the clock, cave entry and return, dialogue and barter, the inventory and equipment, the character sheet and creation, the hero's combat, ranged attacks and thrown items, NPC awareness and combat, spells from the hotkey bar and from items (81 of the 83 technology records run; of the 80 college spells the creature Summons and a few others are still open, [magic audit](../research/magic-audit-res-103.md#answer)), the technology schematics and the assembled products, traps on maps and placed trap devices, the logbook, the world map screen with travel and discovery, save and load, and the HD graphics and interface switches.

Not built or only stubbed (each has an epic in the plan):

- Objects made at run time: items, scenery, eye-candy objects and critters by prototype can be created (E1, ARC-DEV-347, -359, -370, -371, -386; script action 52 makes critters; a created pet becomes a companion, ARC-MAGIC-050), and the Summon component makes scenery (Fireflash, Wall of Fire). Not built: the Summon of creatures (17 of the 42 installed Summon components, with AIRedirect, E1-e) and the AI's summoning (E1-f).
- Random encounters on the world map (E2-c); the generators and the critter body decay are built (E2-a, E2-b).
- The NPC hourly day (beds, walking between day and night standpoints; E3). The hero's sleep and wait window (ARC-WORLD-1160) and the HUD clock (ARC-UI-1111) are built.
- 6 of the 25 spell component types have no handler (AIRedirect, EnvFlag, Interrupt, Recharge, TraitIdx, Trait64; 19 run), item recharge, the viewer draw of hidden, shrunk and translucent critters, the permanent polymorph's ObjFlag part (technology record 222), Tempus Fugit and the raise script hook (E4; [technology audit](../research/technology-audit-res-125.md)).
- 114 of 142 script actions and 29 of 53 conditions are admitted by the widest admission set (`ScrAdmissionSet.S37`, S36 plus the ten world-object actions and three conditions of E5-b, ARC-SCRIPT-1164, six actions and the three conditions applied by the area hosts; S36 is S35 plus the nine NPC state actions of E5-a, ARC-SCRIPT-1163, eight of them applied by the area hosts; S35 is S34 plus fade 103, ARC-SCRIPT-1241; the survey of the missing systems plan found 81 and 20 under S33, E5-c added 13 and 6 in S34); dialogue markers `c r t z` and positive `IN` (E5).
- The native trap script 30002, the follower who disarms instead of the hero and the hero's step onto a placed trap marker (E6; the placing of devices is built, ARC-WORLD-124, -125; the hero's spot trap roll is built, ARC-MAGIC-057; the disarm action from the hotkey bar and the skill pane is built, ARC-SKILLS-1156, ARC-UI-1113).
- Movies, credits, intro and endings (E7).
- Fate window and Fate Points, companions screen, gambling, identify window, several sound callers and the hidden options (E8).
- Gamepad in the game UI, UI scale, and Windows, Linux and handheld validation (E9; the Windows package exists and was never run on Windows); the three experimental controller schemes are planned in the epic [#1371](https://github.com/Simarglok/arcanum-steamwoven/issues/1371), [concept](../input/controller-schemes.md).
- The network game, a second module and mods (E10, deferred by the owner).

This is not yet a playable game. The [missing systems plan](missing-systems-plan.md) describes the order of the unbuilt systems; the [vision](vision.md) describes accepted direction. Requirements/product completeness and milestone scope need their own decisions.

## Maintaining this catalogue

The full rows live in the area files under [`status/`](status/); this file keeps one line per component ([area files rule](../workflow/tasks.md#area-files-of-the-catalogue-matrix-and-ledger)). A changed component edits its row in the area file; a new component adds its row there and one line to the index above. After a merged product change, update the row of each affected requirement in the [coverage matrix](../requirements/coverage.md), in the section's file under [`coverage/`](../requirements/coverage/) with the section's counts in the summary (status, what is implemented, what is missing, documentation links; [rule](../workflow/tasks.md#functional-requirements)) and this catalogue's concrete bounded scope, implementation/test links and verification conditions. Keep research-only work and unmerged candidates separate. Re-snapshot main and open PRs when planning; do not reuse this dated observation as live authority or infer delivery from Done/decomposition.

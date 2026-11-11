# Census of the original's function groups and research boundaries

**Implementation coverage update 2026-10-09:** the [audit of PRs/main and the board](README.md#baseline-and-provenance) re-checks Steamwoven, not all function groups. The census and the link counts below belong to the initial audit of 2026-10-02/03; a new full census is not claimed here. Later function and data research is linked from the relevant FRs and the [component catalogue](../project/status.md).

**Subject: the functions of `Arcanum.exe` 1.0.7.4; research started 2026-10-02.** The functions are split into 140 groups by address range (some ranges overlap); four groups have no functions in the exe. The groups are a navigation aid for finding links in the domain reports, not the structure of the original and not a claim of a line-by-line audit of the whole game. Every statement about a function has level F0 (read, not verified in the exe; [levels](../research/original-executable-check.md#evidence-levels-for-a-function-f0f4)) unless the function is listed in the [verification ledger](../research/original-verification-ledger.md).

## How to read the census

- "Has ranges" means that a domain report contains links to the addresses of this group's functions. It does not mean that the whole group or all calling functions were verified.
- Bodies/sections that were read and paths found only by search are distinguished in each domain report's source list. A link alone does not prove the semantics of a requirement.
- "No range link" keeps an explicit tracing gap. The group's functions may have been found by search or partly reviewed; that does not close research into their behavior.
- The initial audit did not check behavior in the exe or game payloads. An empty or unfinished function in a source that was read does not rule out the corresponding function of the original game.
- The group description is derived from its functions and the domain reports; it names the topic, not a full list of what the functions do.

## All function groups

| Group: addresses, function count, topic | Ranges in the register | Domain links |
| --- | --- | --- |
| 0x4020F0–0x405070 (56 functions) — game library: subsystem table, init/reset/load/save, module and patch attachment | Has ranges: 19 | [content-product](content-product.md), [lighting-time](lighting-time.md), [scene](scene.md), [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x405110–0x40D670 (144 functions) — object storage: fields, prototypes, reading and writing | Has ranges: 2 | [content-product](content-product.md) |
| 0x40D6C0–0x40DB70 (14 functions) — player character: creation and view binding | Has ranges: 2 | [characters-skills](characters-skills.md), [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x40DE20–0x40DEE0 (4 functions) — video clip launch | Has ranges: 1 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x40DF50–0x40EA50 (18 functions) — view scrolling | Has ranges: 2 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x40EA90–0x412C90 (50 functions) — map: opening, loading, saving, map patch | Has ranges: 17 | [lighting-time](lighting-time.md), [world](world.md) |
| 0x412D40–0x41A8C0 (107 functions) — dialogs: DLG loading, record parsing, conditions and actions | Has ranges: 9 | [story-social-ai](story-social-ai.md) |
| 0x41A940–0x41C9D0 (54 functions) — game sound: music, hourly ambient, object sounds | Has ranges: 6 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x41CA40–0x41F240 (15 functions) — ART name normalization and missing images | No range link | Open gap; see the caveat above. |
| 0x41F3C0–0x421AE0 (17 functions) — pathfinding | Has ranges: 8 | [scene](scene.md), [world-travel-runtime](world-travel-runtime.md) |
| 0x421B00–0x437D00 (327 functions) — animation goals: walk, run, attack, use, follow, steps | Has ranges: 23 | [combat-magic-tech](combat-magic-tech.md), [scene](scene.md), [world-travel-runtime](world-travel-runtime.md) |
| 0x437D90–0x438A50 (15 functions) — wall fade in front of the actor | Has ranges: 2 | [scene](scene.md) |
| 0x438AE0–0x438EF0 (13 functions) — settings: registration, reading and writing values | Has ranges: 2 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x438F90–0x43A1A0 (25 functions) — roofs: drawing, fading, hit testing | Has ranges: 5 | [lighting-time](lighting-time.md), [scene](scene.md) |
| 0x43A330–0x444690 (140 functions) — world objects: creation, movement, drawing, inspection, hit points, selection under the cursor | Has ranges: 30 | [items-economy](items-economy.md), [lighting-time](lighting-time.md), [scene](scene.md), [story-social-ai](story-social-ai.md), [ui-media-lifecycle](ui-media-lifecycle.md), [world](world.md), [world-travel-runtime](world-travel-runtime.md) |
| 0x4446E0–0x44C820 (55 functions) — SCR scripts: execution of conditions and actions | Has ranges: 8 | [story-social-ai](story-social-ai.md) |
| 0x44C840–0x44EEE0 (49 functions) — internal nodes of animation goals, their saving and loading | Has ranges: 3 | [world-travel-runtime](world-travel-runtime.md) |
| 0x44EF50–0x45A760 (181 functions) — magick and technology: spells, components, maintenance, resistance | Has ranges: 19 | [combat-magic-tech](combat-magic-tech.md), [world-travel-runtime](world-travel-runtime.md) |
| 0x45A7C0–0x45CC10 (80 functions) — time event queue and date-time | Has ranges: 7 | [lighting-time](lighting-time.md), [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x45CF30–0x45FC70 (85 functions) — critters: faction, fatigue, sleep, death, experience, states | Has ranges: 13 | [characters-skills](characters-skills.md), [combat-magic-tech](combat-magic-tech.md), [lighting-time](lighting-time.md), [story-social-ai](story-social-ai.md) |
| 0x45FD40–0x460DE0 (87 functions) — game callbacks into the interface: inventory, health and fatigue bars | No range link | Open gap; see the caveat above. |
| 0x460E70–0x4681A0 (140 functions) — items: wearing, weight, computed properties, insertion and removal, light, use | Has ranges: 36 | [characters-skills](characters-skills.md), [items-economy](items-economy.md), [lighting-time](lighting-time.md), [world-travel-runtime](world-travel-runtime.md) |
| 0x4681B0–0x49C610 (27 functions) — object prototypes | Has ranges: 1 | [world-travel-runtime](world-travel-runtime.md) |
| 0x49C670–0x4A6560 (102 functions) — multiplayer | Has ranges: 1 | [ui-media-lifecycle](ui-media-lifecycle.md), [network play](network.md) (FR-NET-001…008), [original multiplayer](../research/multiplayer-evidence.md) (ARC-RES-072: function roles; the scan found about 140 entry points, the discrepancy is not resolved) |
| 0x4A6620–0x4A7B90 (21 functions) — levels: experience thresholds, level-up, auto-level schemes | Has ranges: 7 | [characters-skills](characters-skills.md) |
| 0x4A7C60–0x4A8060 (15 functions) — hourly lighting schemes | Has ranges: 7 | [lighting-time](lighting-time.md) |
| 0x4A8320–0x4AFC70 (119 functions) — NPC AI: decisions, attack, fleeing, following, healing, skill use | Has ranges: 16 | [story-social-ai](story-social-ai.md), [world-travel-runtime](world-travel-runtime.md) |
| 0x4AFCD0–0x4B0320 (17 functions) — technology disciplines: degrees and requirements | Has ranges: 3 | [combat-magic-tech](combat-magic-tech.md), [items-economy](items-economy.md) |
| 0x4B0340–0x4B1350 (18 functions) — stats: base and effective values, limits, cost | Has ranges: 13 | [characters-skills](characters-skills.md), [world-travel-runtime](world-travel-runtime.md) |
| 0x4B1440–0x4B1CF0 (29 functions) — spell table: names, cost, requirements | Has ranges: 2 | [combat-magic-tech](combat-magic-tech.md) |
| 0x4B1D50–0x4B83E0 (90 functions) — combat: attack, damage, combat mode, turn-based round, action points | Has ranges: 23 | [combat-magic-tech](combat-magic-tech.md) |
| 0x4B8440–0x4B99C0 (23 functions) — coordinates: screen transforms, direction, distance | Has ranges: 1 | [world](world.md) |
| 0x4B9B90–0x4B9DB0 (5 functions) — module parameters: starting date and time, new-game reset | Has ranges: 2 | [content-product](content-product.md) |
| 0x4B9E10–0x4BA320 (14 functions) — party: composition and update | Has ranges: 2 | [story-social-ai](story-social-ai.md) |
| 0x4BA380–0x4BABF0 (18 functions) — monster generators | Has ranges: 3 | [story-social-ai](story-social-ai.md) |
| 0x4BAC10–0x4BAF50 (12 functions) — magick and technology node pool, its saving | No range link | Open gap; see the caveat above. |
| 0x4BAFF0–0x4BB590 (11 functions) — manual tile blocking and its drawing | No range link | Open gap; see the caveat above. |
| 0x4BB720–0x4BB980 (8 functions) — service indicator over the view: initialization and redraw | No range link | Open gap; see the caveat above. |
| 0x4BB9A0–0x4BBCE0 (9 functions) — service indicator over the view: size, update, redraw | No range link | Open gap; see the caveat above. |
| 0x4BBD00–0x4BD950 (21 functions) — traps: detection, triggering, disarming | Has ranges: 5 | [items-economy](items-economy.md), [world-travel-runtime](world-travel-runtime.md) |
| 0x4BD980–0x4BDD30 (10 functions) — teleportation bans between maps and areas | Has ranges: 2 | [world-travel-runtime](world-travel-runtime.md) |
| 0x4BDE70–0x4BE050 (5 functions) — screen fade-out and fade-in | No range link | Open gap; see the caveat above. |
| 0x4BE0B0–0x4BE0F0 (3 functions) — brightness (gamma) | Has ranges: 1 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x4BE1B0–0x4BEFB0 (22 functions) — town maps | Has ranges: 1 | [world-travel-runtime](world-travel-runtime.md) |
| 0x4BF060–0x4BF330 (11 functions) — newspapers: issues and order | Has ranges: 2 | [story-social-ai](story-social-ai.md) |
| 0x4BF390–0x4BFCA0 (7 functions) — inventory sources (`InvenSource.mes`) and set generation | Has ranges: 3 | [items-economy](items-economy.md) |
| 0x4BFCE0–0x4C0370 (12 functions) — drawing of NPC waypoint markers | Has ranges: 2 | [content-product](content-product.md) |
| 0x4C0410–0x4C0490 (5 functions) — sector script | No range link | Open gap; see the caveat above. |
| 0x4C0530–0x4C0A40 (14 functions) — tile scripts: setting, execution, drawing | Has ranges: 2 | [story-social-ai](story-social-ai.md) |
| 0x4C0BD0–0x4C15A0 (22 functions) — NPC reaction | Has ranges: 6 | [story-social-ai](story-social-ai.md) |
| 0x4C15D0–0x4C21E0 (14 functions) — reputation | Has ranges: 5 | [story-social-ai](story-social-ai.md) |
| 0x4C2270–0x4C2B50 (19 functions) — backgrounds: choice, eligibility, effects, starting items | Has ranges: 5 | [characters-skills](characters-skills.md) |
| 0x4C2C10–0x4C3BE0 (9 functions) — orders to companions shouted by the player | Has ranges: 2 | [story-social-ai](story-social-ai.md) |
| 0x4C3C60–0x4C3FA0 (10 functions) — curses: adding, removal, effect | Has ranges: 1 | [characters-skills](characters-skills.md) |
| 0x4C4110–0x4C4450 (10 functions) — blessings: adding, removal, effect | Has ranges: 1 | [characters-skills](characters-skills.md) |
| 0x4C45C0–0x4C5400 (20 functions) — quests: states, quest experience, journal list | Has ranges: 3 | [story-social-ai](story-social-ai.md) |
| 0x4C5500–0x4C5B10 (19 functions) — rumors: knowledge, suppression, journal list | Has ranges: 4 | [story-social-ai](story-social-ai.md) |
| 0x4C5BD0–0x4C91F0 (58 functions) — skills: levels, training, use | Has ranges: 20 | [characters-skills](characters-skills.md), [items-economy](items-economy.md), [lighting-time](lighting-time.md), [world-travel-runtime](world-travel-runtime.md) |
| 0x4C9280–0x4C9BE0 (12 functions) — dialog reply options window | Has ranges: 1 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x4C9DA0–0x4CA7C0 (10 functions) — facades (large images over the map): drawing | No range link | Open gap; see the caveat above. |
| 0x4CA940–0x4CB6A0 (21 functions) — world map areas: opening and looking up known areas | Has ranges: 1 | [world-travel-runtime](world-travel-runtime.md) |
| 0x4CB720–0x4CC160 (22 functions) — magic items: effects and triggers | Has ranges: 6 | [combat-magic-tech](combat-magic-tech.md), [items-economy](items-economy.md) |
| 0x4CC1F0–0x4CCC40 (30 functions) — AI choice of spells and items | No range link | Open gap; see the caveat above. |
| 0x4CCCF0–0x4CE300 (20 functions) — visual effects attached to an actor or place | Has ranges: 1 | [combat-magic-tech](combat-magic-tech.md) |
| 0x4CE350–0x4CEB80 (13 functions) — portraits: catalogue and choice | Has ranges: 3 | [characters-skills](characters-skills.md) |
| 0x4CEC10–0x4CEE60 (6 functions) — script file names | Has ranges: 3 | [story-social-ai](story-social-ai.md) |
| 0x4CEEB0–0x4CEF50 (7 functions) — random number generator | Has ranges: 1 | [content-product](content-product.md) |
| 0x4CEF70–0x4D31C0 (66 functions) — sectors: loading, cache, eviction | Has ranges: 14 | [lighting-time](lighting-time.md), [scene](scene.md), [world](world.md) |
| 0x4D32D0–0x4D3F30 (17 functions) — teleportation: queue, object transfer, map change | Has ranges: 11 | [world](world.md) |
| 0x4D3F60–0x4D4080 (5 functions) — item effect texts | Has ranges: 1 | [combat-magic-tech](combat-magic-tech.md), [items-economy](items-economy.md) |
| 0x4D40D0–0x4D4260 (6 functions) — descriptions of objects and keys from MES | No range link | Open gap; see the caveat above. |
| 0x4D4290–0x4D4DD0 (22 functions) — reading MES files | Has ranges: 2 | [content-product](content-product.md) |
| 0x4D4E20–0x4D5DC0 (29 functions) — floating text over objects | Has ranges: 2 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x4D5B80–0x4D67F0 (18 functions) — text bubbles over objects | Has ranges: 2 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x4D6840–0x4D7E90 (26 functions) — tiles: drawing, properties (blocking, sound, water) | Has ranges: 3 | [lighting-time](lighting-time.md), [scene](scene.md), [world-travel-runtime](world-travel-runtime.md) |
| 0x4D7F30–0x4DF310 (79 functions) — light: sources, colors, shadows, drawing | Has ranges: 7 | [lighting-time](lighting-time.md) |
| 0x4DF390–0x4E2C50 (13 functions) — walls: drawing and removal | Has ranges: 3 | [scene](scene.md) |
| 0x4E2FB0–0x4E3890 (21 functions) — transition points between maps | Has ranges: 1 | [world](world.md) |
| 0x4E3900–0x4E3F40 (16 functions) — object index by location | No range link | Open gap; see the caveat above. |
| 0x4E3F80–0x4E6240 (39 functions) — internal storage and serialization of object fields | No range link | Open gap; see the caveat above. |
| 0x4E4CD0–0x4E59A0 (26 functions) — object pool and persistent identifiers | No range link | Open gap; see the caveat above. |
| 0x4E62A0–0x4E6B00 (13 functions) — object identifiers (GUID, persistent identity) | Has ranges: 1 | [content-product](content-product.md) |
| 0x4E6B60–0x4E71B0 (8 functions) — object files: reading a single object | Has ranges: 1 | [content-product](content-product.md) |
| 0x4E72B0–0x4E7400 (6 functions) — internal object names | Has ranges: 1 | [items-economy](items-economy.md) |
| 0x4E7430–0x4E7AB0 (22 functions) — sparse field arrays and their serialization | No range link | Open gap; see the caveat above. |
| 0x4E7B00–0x4E9680 (39 functions) — world map terrain: sector templates, filling | No range link | Open gap; see the caveat above. |
| 0x4E9920–0x4E99C0 (6 functions) — object list node pool | No range link | Open gap; see the caveat above. |
| 0x4E99F0–0x4EABF0 (28 functions) — lasting effects: parsing, adding, removal, stat modifiers | Has ranges: 3 | [characters-skills](characters-skills.md) |
| 0x4EAC80–0x4ED440 (66 functions) — ART resource names: image file paths, tile flags | Has ranges: 1 | [characters-skills](characters-skills.md) |
| 0x4ED6C0–0x4F0690 (33 functions) — network action variants for multiplayer | No range link | Open gap; see the caveat above. |
| 0x4F06E0–0x4F0A90 (6 functions) — doors and windows: state and toggling | Has ranges: 2 | [scene](scene.md) |
| 0x4F0BF0–0x4F1050 (5 functions) — sounds of items, critters, containers and doors | Has ranges: 1 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x4F1150–0x4F2330 (20 functions) — sector object lists | Has ranges: 3 | [lighting-time](lighting-time.md), [story-social-ai](story-social-ai.md), [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x4F24F0–0x4F4E40 (22 functions) — target selection under the cursor | Has ranges: 3 | [combat-magic-tech](combat-magic-tech.md) |
| 0x4F5090–0x4F5320 (6 functions) — Fate Points: activation and use | Has ranges: 1 | [characters-skills](characters-skills.md) |
| 0x4F5330–0x4F57C0 (5 functions) — kill and injury accounting for the journal | Has ranges: 2 | [combat-magic-tech](combat-magic-tech.md) |
| 0x4F6170–0x4F62E0 (11 functions) — sector script lists | Has ranges: 2 | [story-social-ai](story-social-ai.md) |
| 0x4F6310–0x4F6750 (17 functions) — tile script lists | Has ranges: 1 | [story-social-ai](story-social-ai.md) |
| 0x4F67D0–0x4F6BB0 (9 functions) — text filters | Has ranges: 2 | [story-social-ai](story-social-ai.md) |
| 0x4F6CF0–0x4F7020 (6 functions) — walkability mask files | No range link | Open gap; see the caveat above. |
| 0x4F7110–0x4F7B50 (14 functions) — sector light source lists | Has ranges: 2 | [lighting-time](lighting-time.md) |
| 0x4F7BA0–0x4F7DF0 (12 functions) — sector tile lists | No range link | Open gap; see the caveat above. |
| 0x4F7F20–0x4F8090 (11 functions) — sector roof lists | Has ranges: 1 | [scene](scene.md) |
| 0x4F80E0–0x4F81D0 (9 functions) — sector sound lists | No range link | Open gap; see the caveat above. |
| 0x4F81E0–0x4F83D0 (13 functions) — sector blocked-tile lists | No range link | Open gap; see the caveat above. |
| 0x53E600–0x53EB00 (11 functions) — interface library: init/reset/save, transition to the main menu | Has ranges: 1 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x53EB10–0x5407F0 (27 functions) — journal window: tabs and entries | Has ranges: 7 | [combat-magic-tech](combat-magic-tech.md), [story-social-ai](story-social-ai.md), [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x540930–0x549A80 (138 functions) — main menu: new game, hero choice, loading and saving | Has ranges: 19 | [characters-skills](characters-skills.md), [ui-media-lifecycle](ui-media-lifecycle.md), [world-travel-runtime](world-travel-runtime.md) |
| 0x549B70–0x557CF0 (160 functions) — game interface: buttons, panels, input handling in the world | Has ranges: 9 | [combat-magic-tech](combat-magic-tech.md), [items-economy](items-economy.md), [lighting-time](lighting-time.md), [story-social-ai](story-social-ai.md), [ui-media-lifecycle](ui-media-lifecycle.md), [world-travel-runtime](world-travel-runtime.md) |
| 0x557DF0–0x5581A0 (9 functions) — isometric view: redraw and invalidation | Has ranges: 1 | [world-travel-runtime](world-travel-runtime.md) |
| 0x5581B0–0x559640 (32 functions) — random encounters on the world map | Has ranges: 4 | [world-travel-runtime](world-travel-runtime.md) |
| 0x559690–0x55F340 (52 functions) — character sheet: stats, skills, point allocation | Has ranges: 10 | [characters-skills](characters-skills.md), [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x55F8D0–0x566D10 (113 functions) — world map and town map: route, notes, movement | Has ranges: 8 | [ui-media-lifecycle](ui-media-lifecycle.md), [world](world.md), [world-travel-runtime](world-travel-runtime.md) |
| 0x566E20–0x567320 (11 functions) — text input field | No range link | Open gap; see the caveat above. |
| 0x567330–0x5689B0 (22 functions) — dialog window | Has ranges: 7 | [story-social-ai](story-social-ai.md), [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x5689D0–0x5695A0 (19 functions) — compact panel: message window, health and fatigue bars | Has ranges: 3 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x5695B0–0x569AA0 (11 functions) — slideshow and credits | Has ranges: 4 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x56A020–0x56A300 (3 functions) — numeric counter drawing | No range link | Open gap; see the caveat above. |
| 0x56A4A0–0x56B970 (21 functions) — companion panel and orders menu | Has ranges: 3 | [story-social-ai](story-social-ai.md), [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x56BAA0–0x56CCA0 (17 functions) — books, notes and newspapers: parsing and drawing | Has ranges: 3 | [items-economy](items-economy.md), [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x56CD60–0x56EA30 (20 functions) — schematics window (technology recipes) | Has ranges: 5 | [items-economy](items-economy.md), [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x56EA90–0x56F9B0 (16 functions) — combat interface: action points, mode buttons | Has ranges: 2 | [combat-magic-tech](combat-magic-tech.md) |
| 0x56FAE0–0x56FF40 (9 functions) — Fate Points window | Has ranges: 3 | [characters-skills](characters-skills.md) |
| 0x5717F0–0x571990 (7 functions) — shout input (order to companions) | Has ranges: 1 | [story-social-ai](story-social-ai.md) |
| 0x5719D0–0x571CB0 (6 functions) — using an item on a target | Has ranges: 2 | [items-economy](items-economy.md), [world-travel-runtime](world-travel-runtime.md) |
| 0x572060–0x579E30 (65 functions) — inventory, barter and loot window | Has ranges: 12 | [items-economy](items-economy.md), [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x579E80–0x57AEB0 (31 functions) — skill use and training from the interface | Has ranges: 4 | [characters-skills](characters-skills.md) |
| 0x57B080–0x57BAC0 (13 functions) — sleep and wait window | Has ranges: 5 | [lighting-time](lighting-time.md), [world-travel-runtime](world-travel-runtime.md) |
| 0x57BB30–0x57C670 (22 functions) — spell interface | Has ranges: 5 | [combat-magic-tech](combat-magic-tech.md) |
| 0x57C6E0–0x57D150 (27 functions) — turn-based mode interface | Has ranges: 3 | [characters-skills](characters-skills.md), [combat-magic-tech](combat-magic-tech.md), [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x57D240–0x57D6C0 (15 functions) — deferred interface events and hourly ambient light update | Has ranges: 1 | [lighting-time](lighting-time.md) |
| 0x57D700–0x57F3C0 (35 functions) — hotkeys and quick slots | Has ranges: 2 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x57F4D0–0x5803D0 (24 functions) — cycling controls (list choice with arrows) | No range link | Open gap; see the caveat above. |
| 0x580410–0x5816F0 (29 functions) — scroll bars | No range link | Open gap; see the caveat above. |
| 0x589280–0x589A00 (35 functions) — settings window | Has ranges: 4 | [combat-magic-tech](combat-magic-tech.md), [ui-media-lifecycle](ui-media-lifecycle.md) |
| 0x589A20–0x589B90 (3 functions) — raising and lowering technology degrees | Has ranges: 1 | [combat-magic-tech](combat-magic-tech.md) |
| no addresses (0 functions) — high-resolution settings; the original has none | Has ranges: 1 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| no addresses (0 functions) — high-resolution support; the original has none | Has ranges: 1 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| no addresses (0 functions) — table of object flag and field names (data only) | No range link | Open gap; see the caveat above. |
| no addresses (0 functions) — resistance tables (data only) | Has ranges: 2 | [characters-skills](characters-skills.md), [combat-magic-tech](combat-magic-tech.md) |

The domain documents have range links for **113 of 140** groups; for **27** there are none. These are source tracing figures, not behavior completeness.

## Outside the census

The census does not cover the engine library functions (DAT files and archives, graphics, sound, video, network), platform paths and data tables without functions of their own. Links to them in [content-product](content-product.md), [ui-media-lifecycle](ui-media-lifecycle.md), [combat-magic-tech](combat-magic-tech.md), [items-economy](items-economy.md), [story-social-ai](story-social-ai.md), [lighting-time](lighting-time.md) and [world](world.md) do not mean they were researched. The main loop with mouse and keyboard scrolling (0x401000–0x402010, 4 functions) is also outside the 140 groups; [ui-media-lifecycle](ui-media-lifecycle.md) and [world-travel-runtime](world-travel-runtime.md) refer to it.

## Object infrastructure

The following researched sections concern identity, lifetime and serialization. They do not define separate player actions or an obligation to reproduce the original's memory layout:

- object list node pool, creation and release (0x4E9920, 0x4E9930, 0x4E9940, 0x4E9970, 0x4E9990, 0x4E99C0).
- magick and technology node pool and initial fields (0x4BAC10, 0x4BAC20, 0x4BAC30, 0x4BAC80, 0x4BACA0).
- object handle allocation (0x4E4E60): the runtime handle and the persistent identity are distinguished. The full lookup/restore/invalidation path is not researched.
- sparse field array serialization (0x4E7590, 0x4E75E0) and typed object field reset (0x4E3FA0): storage structures, not a standalone inventory/quest mechanic.
- table of flag group and field names: registry metadata, not proof that all flag values are executed.

These functions describe the original's object storage infrastructure and by themselves do not confirm Steamwoven coverage. The table above counts links in the topic documents; the selected infrastructure ranges are listed here separately.

## Next research and unverified scope

A full format/API/formula audit has not been done. For a chosen task, check the specific caller paths and the values of the original data, the supported profile and the results of an independent runtime. Specific gaps: the exact inputs/outputs of Crash Site, RNG formulas and draw order, resource priorities of a full installation, unsupported object/art variants, all script opcodes/quest branches and platform-specific media/save paths. This is a list of knowledge gaps, not permission to start new tasks.

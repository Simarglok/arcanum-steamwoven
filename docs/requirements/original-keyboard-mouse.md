# Original keyboard and mouse controls

**Date:** 2026-10-04. **Purpose:** requirements for an independent implementation of keyboard/mouse input, its interface, and the separation of diagnostics from game actions. This document extends FR-UI-002, FR-NAV-001 and FR-PRODUCT-002. The KM items refine existing requirement groups and do not create tasks or new groups in the shared FR counter.

**Accepted by the owner:** keyboard/mouse keep the original Arcanum scheme and interface actions. WASD do not control the character. Custom presets, direct stick control, contextual prompts and other interaction UI belong to the gamepad. Modern resolution, HiDPI and scaling of the original interface remain accepted goals; this is not permission to replace the mouse layout with a gamepad layout.

**Research result:** the input functions of the original executable were read at the addresses at level F0 (the ledger lines LB1, CS1, RT4, FA1–FA4 were checked against the exe), and the original manual and the Quick Reference Card were checked. No item claims a run or full parity with the original executable. This research did not change the control code, game data, active cards or the schedule. The diagnostic layer options below are proposed for selection, not declared an active mapping.

## Sources and the order of resolving discrepancies

| Label | Source and locator |
| --- | --- |
| M | The GOG manual.pdf provided by the owner, Arcanum Players Guide, 187 PDF pages. SHA-256: daf0e1851e95725c3f0572bc4923df8d33873fcfad6d855a81e9bafe5566c892. Appendix A-10 Quick Keys: PDF 181–182, printed 188–189. |
| Q | The Arcanum_Quick_Reference_Card.pdf provided by the owner, 2 PDF pages; Quick Keys is on PDF 2. SHA-256: 5cf8414c918f233295f51b9aaa1caa8f6db4984e42ea9838240ebcb2094977fb. This is a scan; the table was checked visually. |
| EXE | Retail `Arcanum.exe` 1.0.7.4. The codes K/P/U/T below point to functions of the original at the addresses in parentheses ([locator codes](#locator-codes)); the basis is a read of the original's functions at the stated addresses, level F0 (not checked in the exe) unless the line is in the verification ledger. |
| SW | The Steamwoven source slice for the list of current discrepancies: db8bcd0ff48983b4e90a4ff30a66a9ad48d79e7c, without unmerged candidates. This is a code inventory, not a re-check of builds or a real GUI test. |

In the manual, some pages share one content stream of a spread and have different cropboxes. A text dump of one PDF sheet can include the neighboring printed page. Therefore the locators give the PDF number and the printed numbers separately; the relevant spreads and both Appendix sheets were checked visually. The PDFs and their full text dumps are not included in the MIT repository. What is kept here are functional descriptions, identifiers and the provenance of the sources, without game images and extracted game lines.

Knowledge statuses:

- **Original-documented:** the action is described in M/Q; this is a documented contract, not a runtime observation.
- **F0:** a read of the original's functions at the stated addresses, level F0 (not checked in the exe) unless the line is in the verification ledger; the specific call and conditions are described, but this is not proof of a full match with retail.
- **Exe-verified:** the behavior is confirmed by a line of the [verification ledger](../research/original-verification-ledger.md) (for example LB1, RT4, FA1–FA4).
- **Owner-policy:** the accepted Steamwoven direction, including the separation of control methods.
- **Open:** data, deeper verification or an observation of the original's runtime is required.
- **Proposal:** a proposed new diagnostic layer; the layout is not chosen and not implemented.

When an F0 observation differs from M/Q, the documented behavior of the original remains the initial requirement, and the discrepancy is recorded separately. A bug, stub, cheat or convenience feature outside the documented original must not be silently carried into original compatibility. If an observation of the original version differs from the manual, a versioned compatibility baseline must be chosen explicitly and the relevant item updated. M and Q partly repeat one table; they cannot be counted as two independent runtime checks.

## KM-01 — Two adapters, one shared game simulation

Keyboard/mouse and gamepad pass semantic commands to the shared simulation, but keep different ways of choosing actions and targets. For keyboard/mouse, W/A/S/D must not be converted to a movement direction, I to Inspect, C to a universal Context, Space to a universal Confirm, or F1 to Settings. These keys have game assignments.

Direct left-stick control, the right camera, LT/RT zoom, gamepad presets and RB highlighting remain a separate gamepad contract. The right-stick inversion setting must not invert the original arrows/mouse. The original mouse hover reports information in the Message Window; controller contextual labels do not replace this interface in keyboard/mouse mode. Changing the input method preserves the actor/world/action state; accidental pointer movement or switching prompts does not duplicate a command. An unresearched or not yet implemented game screen does not release its key to WASD/diagnostics: the action stays unavailable with an explicit status.

Acceptance: W/A/S/D do not create MoveCharacter/step/goal; W/S/A perform only their permitted interface actions, D has no normal actor binding. Gamepad lab results do not count as original keyboard/mouse parity.

## KM-02 — Documented game layout

In all rows of this table, M means Appendix A-10 (PDF 181–182 / printed 188–189), Q means PDF 2. The documents give the action; the down/up event type and part of the restrictions below are established only at level F0 and need checking in the exe/runtime to claim exact parity.

| Input | Required action of the original | Observation at the addresses and boundaries |
| --- | --- | --- |
| Escape | Main game menu; in a nested main menu, return to the previous window. | Up: return to MAIN from some modes or the in-play/locked menu. Locked applies during dialog, the world map or another actor's TB turn. K1 (0x401560). In the main menu windows (Single Player, New Game, hero browser) Escape goes back; in the root window it does nothing at first opening and quits the game at the second; the button letter (the caption message + 10 in `MainMenu.mes`) selects it ([MM6](../research/ledger/ui-audio.md#main-menu-arc-ui-011), ARC-UI-011). |
| F1 | Order all Followers to go to the selected point. | Up: broadcast index 0, the location under the pointer. M additionally PDF 74–75 / printed 74–75. K2 (0x54DE50), K12 (0x4C3B90, 0x4C3BE0) |
| F2 | Order all Followers to attack the selected target. | Up: broadcast index 1; the same source/target context. K2 (0x54DE50) |
| F3 | Followers follow closer. | Up: broadcast index 2. This is not a free overlay hotkey. K2 (0x54DE50) |
| F4 | Followers follow at a greater distance. | Up: broadcast index 3. K2 (0x54DE50) |
| F5 | Followers stop fighting their target. | Up: broadcast index 4. K2 (0x54DE50) |
| F7 | Quick save to the dedicated auto-save slot. | Up: a living PC, a permitted own turn; if the map is open, it is closed first, then SlotAuto is saved. K1 (0x401560) |
| F8 | Quick load of the newest save. | Up: the helper takes the save with the latest file modification time (the auto-save or any numbered slot), not SlotAuto explicitly; verified by two reads of the exe ([SL2](../research/ledger/ui-audio.md#save-list-arc-ui-010)), in the viewer with ARC-UI-010. K1 (0x401560), K8 (0x403850) |
| F12 | Screenshot. | A separate TIG callback is configured on the start of a detected hold; the ordinary key-up handler of the game interface is not used. File paths/format are not set by this document. K7 (0x401000), T3 |
| NumLock | Toggle the constant-run mode. | The original 1.0.7.4 reads the Num Lock state (toggled by each press) and runs when the Always Run option is off; the key does not write the option ([ARC-RES-047 L2](../research/locomotion-evidence.md)). Implemented in `area` (ARC-WORLD-020). P3 (0x4304C0) |
| Enter | Go to entering a broadcast/chat message; a second Enter sends the entered text. | Down: opens broadcast; in the quantity window it confirms the quantity instead. M PDF 72–73 / printed 72–73; K3 (0x54B5D0), U2 (0x566F00) |
| Space | Closing game interfaces and switching Turn-Based/Real-Time according to the original's context. | Down: the TB switch is called; the key turns it on only for the active PC ([RT4](../research/ledger/combat.md#real-time-combat-arc-combat-004)); the direct call contains no generic close UI. In Fate, Space closes that screen. Do not turn it into a generic Confirm. On another actor's TB turn, Space down also toggles TB ([CT6](../research/ledger/combat.md#turn-based-host-arc-combat-020)). In the AREA viewer the TB switch is implemented (ARC-COMBAT-020), closing interfaces is not. K3 (0x54B5D0), U12 (0x56FE70), U13 (0x4B6D20) |
| A | Launch the last Active action near the rotating window. | Logical key down activates the first recent-action slot if there is no text focus/main menu. This is not movement left. K4 (0x57DC60) |
| C | Toggle the sheet/editor of the current character. | Logical key down calls the character UI. K3 (0x54B5D0) |
| F | Toggle the Fate Point Window. | Logical key down calls the Fate UI; not Fit Camera. K3 (0x54B5D0) |
| I | Toggle the inventory. | Logical key down calls the inventory; not Inspect. K3 (0x54B5D0) |
| L | Toggle the Logbook. | Logical key down: in the original exe the characters `l`/`L` call the logbook toggle for the local PC (line [LB1](../research/ledger/ui-audio.md#logbook-arc-ui-008)); the AREA viewer does the same (ARC-UI-008). |
| O | Open Options. | Physical scancode up; not executed at text focus. In options, O can close the window. K1 (0x401560), U3 (0x546EE0) |
| R | Ready/Unready, Attack/Talk mode. | Logical key down toggles combat readiness; this is not TB/RT and not Reset Scene. K3 (0x54B5D0) |
| S | Sleep/Wait UI. | Logical key down calls the sleep UI. This is not movement down. M PDF 52–53 / printed 52–53. K3 (0x54B5D0) |
| T | Toggle the technology schematics. | Logical key down calls the schematic UI: the characters `t`/`T` open the schematics window for the local PC as crafter and viewer (line [SW1](../research/ledger/technology.md#schematic-window-arc-technology-005)); the AREA viewer does the same (ARC-TECHNOLOGY-005). K3 (0x54B5D0) |
| V | Show version information. | By F0 it is handled only at cheat level >= 1. For the documented original action the cheat flag must not be required. K1 (0x401560) |
| W | Toggle the World Map UI. | Logical key down calls the world map. This is not movement up. The AREA viewer in map mode does the same on the start map with ARC-UI-048 (diagnostic world map, [world map](../rendering/area-map-mode.md#world-map-arc-ui-048)). K3 (0x54B5D0) |
| Hold comma while attacking | Aimed attack at the head. | Scratch value 1 is selected ([FA4](../research/ledger/combat.md#hero-attack-arc-combat-005)); cursor/art and combat semantics are checked separately. M PDF 60–61 / printed 60–61; K5 (0x54ED30) |
| Hold period while attacking | Aimed attack at the arms. | Scratch value 2 is selected (arm, [FA4](../research/ledger/combat.md#hero-attack-arc-combat-005)). Which cursor art K13 selects for value 2 is not checked in the exe; do not derive a combat rule from it. K5 (0x54ED30), K13 (0x553990) |
| Hold slash while attacking | Aimed attack at the legs. | Scratch value 3 is selected (leg, [FA4](../research/ledger/combat.md#hero-attack-arc-combat-005)); the cursor art K13 for value 3 is not checked. K5 (0x54ED30), K13 (0x553990) |

Additional binds documented in the main text or found only at the addresses (F0):

| Input and context | Action | Basis |
| --- | --- | --- |
| 1–9 and 0 of the top row; A | Activate the corresponding one of the ten quick slots; 0 is the tenth; A is the last action. A slot is assigned by drag-and-drop; if needed, a separate target choice starts. | Original-documented: M PDF 40–41 / printed 41, Hot Key Bank. F2 (ARC-UI-022, [HK8](../research/ledger/spell-runs.md#hotkey-bar-arc-ui-022)): press (scancode 2..11), not on another actor's turn of turn-based combat, an empty entry is skipped; the character A/a is the last of the two last actions. Implemented in the AREA viewer (ARC-UI-022); assignment is `Ctrl`+digit (last action), clearing is `Ctrl`+`Shift`+digit. K10 (0x54DE50), K11 (0x57F1D0) |
| Arrows in the game world | Pan the isometric view, not move the character. | Original-documented: M PDF 174 / printed 174. F0: held scancode polling, diagonals, permitted scrolling modes. P1 (0x401560, 0x401F50, 0x402010) |
| Home in the game world | Center on the PC; by the addresses K2/K6, when the PC is already centered in TB, on the current participant of the turn. | F0; the original's runtime semantics of the additional TB case are not checked. K2 (0x54DE50), K6 (0x54EB60) |
| K / M | Skills / Spells pane toggle on scancode up, if the main menu is not open. | F1 (ARC-MAGIC-015, [SU14](../research/ledger/spell-runs.md#spell-ui-arc-magic-015)): releasing M (0x32) switches the interface panel to spells (0x54DBF0), K (0x25) to skills. M in the AREA viewer shows and hides the spells panel (ARC-MAGIC-015); K is not implemented. K3 (0x54B5D0) |
| E in the TB combat UI | Advance the local PC's sub-turn when the animation permits and without text focus. | Key-up (and the end-of-turn button): on the local PC's turn without animation or with only a goal-2 animation, the next sub-turn at once ([CT5](../research/ledger/combat.md#turn-based-host-arc-combat-020)); not Next Target. Implemented in the AREA viewer for an idle hero (ARC-COMBAT-020). U5 (0x56F9B0) |
| Z / Home / arrows in the map UI | Z up toggles the map level; Home centers the map; arrows scroll. | F0. The Home/arrows filter handles them without a pressed guard; separate held polling also exists. This is a different context than the world camera. U1 (0x5618B0) |
| F6 | By the address K2, the sixth broadcast index, caption from mes ID 505. | F0; M/Q document F1–F5, the meaning of F6 is not established. Do not invent a sixth command. K2 (0x54DE50) |
| F9 | In the researched functions and M A-10/Q no binding was found. | The negative result is limited to these sources; the original runtime and OS delivery are not checked. A candidate diagnostic prefix, not a provably free key. |
| F10 | Compact/interface toggle on key-up, with a fullscreen condition in the called helper. | F0, absent from Quick Keys; must not be declared an ordinary fullscreen hotkey or a new game binding without choosing a baseline. K1 (0x401560), K9 (0x5578E0) |
| Tab | In text edit, an optional on_tab callback. | F0; a global gameplay Tab mapping was not found. Do not declare it absent in all original contexts. U2 (0x566F00) |

## KM-03 — The mouse sets a route target, not a direction for every frame

A click on the ground sets the destination cell; the character walks along a game route to that point. It does not teleport and does not move by WASD or by following a camera drag. The route planner uses the original world/grid/traversal contract and the shared movement executor; it does not substitute sprite bounds or automatically derived Godot navigation for passability.

Original-documented: M PDF 54–55 / printed 54–55. F0: MAIN LMB-down after the world-window/picker guard selects a move/run goal; LMB-up only resets the local action guard and does not cancel the route. Reassigning the same cell keeps the current ground goal; a different cell updates the goal and marks the path for recalculation. P2 (0x54DE50), P4 (0x433640)

The gamepad rule for completing/rolling back a step when the stick is released does not become the rule for releasing the mouse. The click continues to act after the button is released. The path budget/segmentation, object goal interrupts and the specific route result with a moving obstacle are studied separately; the current source study does not confirm the whole pathfinder or full Crash Site traversal. See [world-travel-runtime](world-travel-runtime.md#fr-nav-001) and the [pathfinding study](../input/arcanum-pathfinding-study.md).

Acceptance: one click-down/up starts reaching the given cell; release does not stop the route; a repeated click replaces the remaining goal without a jump; the same target does not create a duplicate queue. Blocked/Unknown/outside the admitted zone do not turn into Open. The bounded diagnostic zone and the absence of a full router are stated honestly.

## KM-04 — Object, mode and modifier determine the action

| Context / input | Required result | Source |
| --- | --- | --- |
| Mouse hover | Original information in the Message Window: name/suitable properties of an NPC, item, scenery, portal/container. Not all scenery must have text. | M PDF 52–55 / printed 53–55; documented mouse-hover behavior; the details of feedback/picking at the addresses are not declared fully verified. |
| LMB outside Combat Mode | Door: open/use; living NPC: talk; item: pick up; chest/corpse: loot; ground: walk. The action takes into account the state and availability of the target. | M PDF 54–55 / printed 54; goal selection by type P5 (0x54ED30). |
| LMB in Combat Mode | For a permitted living non-follower target, attack; the state/faction/party and object state determine permissibility. | M PDF 54–57 / printed 55–57; P5 (0x54ED30). |
| Ctrl+LMB on the ground | Run instead of ordinary walking in the documented case. Running in Combat Mode spends fatigue under separate game rules. | M PDF 54–55 / printed 55, M188/Q; P2 (0x54DE50), P3 (0x4304C0). |
| Shift+LMB on a target | Attack/act standing in place; do not automatically approach. A ranged attack is used if it is available and required. On the ground the ordinary move branch is suppressed (F0). | M55, M188/Q; P2 (0x54DE50), P5 (0x54ED30); on a living target in Combat Mode Shift gives an attempt-attack goal ([FA1](../research/ledger/combat.md#hero-attack-arc-combat-005)). The full meaning of the animation flag is not established from one number. |
| Alt+LMB in Combat Mode | Forced attack, including suitable scenery/doors/chests/Followers. | M55, M188/Q. P5 has different branches by type and Alt+Shift; this is not a single universal object action. A click attacks one's own Follower only with the left Alt ([FA2](../research/ledger/combat.md#hero-attack-arc-combat-005)); a dead target with Alt is searched ([FA3](../research/ledger/combat.md#hero-attack-arc-combat-005)). P5 (0x54ED30) |
| Alt+LMB on an item/corpse outside combat | Move a neighboring item/corpse to the character's position; the character must stand directly adjacent. This is not a camera drag and not a promise of a mouse drag-and-drop gesture. | M PDF 54–57 / printed 55–56, Q; called by the object helper P5 (0x54ED30), F0. |
| Selecting a Spell / Item / Skill | If a target is needed, go to the suitable targeting mode; a permitted click applies the action, RMB cancels the corresponding targeting. If no target is needed, the action can run at once. | M PDF 50–51, 62–63, 76–77, 143 / printed 50–51, 62–63, 76–77, 142–143; P6 (0x54DE50), U6 (0x57BC70). |
| Shift when selecting a spell / applicable item | Immediate application to self, only if the action permits self as a target. | M PDF 76–77 / printed 76; U6 (0x57BC70), U7 (0x571AA0, 0x571C80, 0x571CB0). |
| Ctrl with Pick Lock / Disarm Trap | Suppress the automatic choice of a more skilled Follower; use one's own character. | M PDF 70–71 / printed 71; U7 (0x571AA0, 0x571C80, 0x571CB0), U15 (0x57A320, 0x57A5E0, 0x57A620). Not a general modifier for diagnostics. |
| RMB on a Follower portrait | Its command menu; the Walk/Attack commands go to target selection; RMB cancels the menu/targeting. Unavailable actions are not activated. | M PDF 74–75 / printed 75; U8 (0x56A9D0, 0x56AFD0, 0x56B0F0, 0x56B110, 0x56B180). |
| Town Map LMB / RMB | Add / remove a waypoint; a separate button starts the route through the waypoints. | M PDF 48–49 / printed 48. Do not carry this rule over to the ordinary world. U1 (0x5618B0) |

RMB is not a universal Cancel for the whole world. In MAIN, on RMB-up a separate, conditionally permitted logic of interrupts, combat/concealment and orientation toward the pointer runs; other modes have other handlers. All these actions must not be replaced by a single path cancel. P2 (0x54DE50), P6 (0x54DE50). Read in the exe by two readers ([RR1–RR5](../research/ledger/combat.md#right-release-arc-combat-022)): for an active PC that is not lying down and has no animation, RMB-up in combat mode turns it off (through the exit check, a refusal gives message 24), outside combat mode it removes stealth; combat mode is not turned on by this; then animations are interrupted and the PC turns toward the pointer. In the viewer (ARC-COMBAT-022) there is leaving combat mode and removing stealth; there are no interrupts and no turn.

In TARGETING mode for Spell/Skill/Item/Follower the action is often executed on LMB-up, while ordinary walking/interaction is on down. TIG generates a repeated down after an interval over 250 ms and a repeat flag; the main world handler does not check repeat. There is no separate double-click message in the listed set. This is F0 data (part of TIG without matched addresses), not proof of retail timing; do not add new game semantics to a double click without a separate requirement. P7, T4

## KM-05 — The original camera does not use LMB for dragging

Arrows pan the game view; the mouse selects movement and objects. At the addresses P1 (F0) there is also edge-scroll with diagonals in the corners, which stops when the application is inactive. The 8 px threshold is an F0 observation, not a proven modern-HiDPI zone size. Its real boundaries/speed and the original baseline need a separate check. P1 (0x401560, 0x401F50, 0x402010) Reading ARC-UI-099 (F2): the 8 px figure is wrong, only the exact border pixel scrolls (x 0 or 799, y 0 or 599 on the 800×600 screen), corners diagonal; built in the viewer, see [interface modes](../architecture/interface-modes.md#scroll-and-right-press-arc-dev-200-arc-dev-201).

In the researched world handler MMB camera drag and wheel world zoom are not assigned. The wheel is used by UI scrollbars; middle-up can have actions in other windows/slides. Therefore the earlier proposed MMB drag is not accepted as an original control. Ordinary wheel/+/- world zoom and left camera drag from the diagnostic Viewer must not automatically pass into keyboard/mouse compatibility. Future additional camera actions are permissible through a separately chosen diagnostic surface or a separate new product decision. Gamepad LT/RT remain the accepted separate zoom contract. U9 (0x569770), U14 (0x580B10), T4

Acceptance: LMB on the world does not drag the camera; arrows/Home keep the permitted context and do not acquire gamepad inversion; map UI and the world camera are distinguished; mouse button handling is not replaced by an unconditional Confirm/Cancel.

## KM-06 — The context UI owns the event

The route at the addresses (F0): TIG mouse-button handling and window filters → scrollbar/hotkey/interface filter → dialog/turn/text guards → the mode world handler (0x54DE50) → the remaining global keys of the main loop (0x401560). An event absorbed by a button or a window filter does not reach the main loop; but an early return from the game interface's event handler (0x54DE50) alone does not forbid the subsequent global actions of the main loop. This is a behavioral specification, not an instruction to copy the original's message queue. U10, T5, T6 (0x54B5D0), K1 (0x401560)

| Context | Rule at the addresses (F0) | Requirement boundary |
| --- | --- | --- |
| Text field | Home/arrows/Insert/Delete/Backspace/Tab/Return are handled on key-down; characters arrive as a separate text event. An unknown key can return false. | Do not turn typing into an actor intent. Do not describe focus as absolute absorption of all keys. U2 (0x566F00) |
| Save/Load menu | Key-up arrows select an entry, Delete/Backspace delete, Return/KP Enter confirm, Escape closes. | These are save UI actions, not Cancel/Confirm/WorldMovement of the general lab layout. The exact original/default semantics are confirmed separately. U3 (0x546EE0) |
| Main menu | Logical key-down can activate a visible button through its accelerator; Enter has separate paths. Escape in the separate menu loop is handled on release. | Do not invent letter accelerators without chosen resources/localization. The routing of the hidden game interface while the menu is open is not checked in the exe. U3 (0x546EE0) |
| Dialog | In the checked tc handler the reply is chosen by LMB/position; direct choice by digit/letter was not found there. | Do not introduce 1–0 as a reply choice only because another RPG or a gamepad mock does so. The research does not exclude other unknown original branches. U4 (0x4C9A10) |
| Logbook/character/inventory | Filters use buttons/mouse and the common big-window lock; this is not a blanket lock of all input. U16 (0x5572D0, 0x557330) | L from M/Q is confirmed in the exe ([LB1](../research/ledger/ui-audio.md#logbook-arc-ui-008)). Keep the original mouse inventory/equipment/use/drop/throw/quantity flows. U11 (0x53F0E0, 0x53F2F0, 0x53F350), U7 (0x571AA0, 0x571C80, 0x571CB0) |
| Another actor's TB turn | The world handler ignores commands; the ISO filter permits Space down. A recent action is filtered earlier, and some of the main globals have their own guards. | This must not be replaced by the generalization "all keys are disabled". Check each route/context and the availability of the action. K1 (0x401560), T6 (0x54B5D0), K4 (0x57DC60) |
| Slides | Key-up ends a slide; Escape can stop the sequence. Mouse releases also take part. | This is a separate media context. Do not carry it into gameplay. U9 (0x569770) |

The global Escape/F7/F8/F10/F11 of the main loop have no common text-focus guard (F0); O and the following cheat shortcuts have one. In FOLLOWER mode F1–F6/Home are under a positive text-focus condition, in MAIN/SPELL/SKILL under a negative one. This is a noted F0 anomaly that must not be silently normalized or considered an already verified original rule. K1 (0x401560), K2 (0x54DE50)

## KM-07 — Portability of key identity and modifiers

Input at the addresses (F0) stores the logical key, the physical scancode and pressed at once; text input goes separately. W/S/R/T/C/F/I/A are checked as logical keys (the letters `c`/`C`, `l`/`L` and `t`/`T` arrive as character messages, [CS1](../research/ledger/character.md#character-sheet-arc-ui-007), [LB1](../research/ledger/ui-audio.md#logbook-arc-ui-008), [SW1](../research/ledger/technology.md#schematic-window-arc-technology-005)); the digit row, K/M, global F-keys and held arrows are by scancode. The letters A, C, F, I, L, R, S, T, W arrive only from WM_CHAR: Shift gives the capital (the same action), Ctrl a control code outside the table, Alt a WM_SYSCHAR that the game does not read; therefore with Ctrl/Alt (and Command on Mac) these keys do nothing ([WK1–WK4](../research/ledger/ui-audio.md#window-key-modifiers-arc-input-004), ARC-INPUT-004). Modifiers are read as the state of held keys, not from message fields: a click on a target in combat accepts the left or right Shift/Alt ([FA1–FA3](../research/ledger/combat.md#hero-attack-arc-combat-005)). T1

An independent adapter preserves the meaning of the chosen compatibility baseline and does not mix a physical code with a localized text character. The document does not confirm the behavior of all Russian/English/non-standard layouts of retail. Ctrl is not automatically replaced with Command. Check real native delivery on macOS, the NumLock function on different keyboards, the function row/Fn, both sides of Ctrl/Shift/Alt, text input, focus loss and the absence of sticking. For actions where the original's functions check LeftCtrl/LeftAlt separately (the check of one's own Follower reads only the left Alt, [FA2](../research/ledger/combat.md#hero-attack-arc-combat-005)), keep this asymmetry; unverified cases remain F0 until checked in the exe. Platform convenience must not silently change the combat or follower override.

## KM-08 — Discrepancies and open questions

| Question | Evidence | Requirement for the next implementation/check |
| --- | --- | --- |
| L in the original exe | M188/Q give Logbook; the character table of the key handler in the exe calls the logbook on `l`/`L` ([LB1](../research/ledger/ui-audio.md#logbook-arc-ui-008)). | Resolved by ARC-UI-008: L toggles the logbook; the absence of a branch in the F0 read is not to be taken as proof that the key is absent in the original. |
| V is under a cheat gate | M189/Q give an ordinary version info; the main loop K1 by F0 requires cheat >= 1; the initial value of the cheat level T2 is not checked in the exe. K1 (0x401560), T2 (0x401000) | The ordinary version action does not depend on cheat mode; the version string of the new runtime is its own information, not a copied version string of the original. |
| Space and closing interfaces | M188/Q explicitly describe close UI + TB/RT; the called function U13 changes the TB setting without a generic close UI call ([RT4](../research/ledger/combat.md#real-time-combat-arc-combat-004)). K3 (0x54B5D0), U13 (0x4B6D20) | Check Space separately in inventory/character/Fate/other screens and in the world; do not infer completeness from one toggle call. |
| F8: fixed auto-slot versus the save-list helper | Resolved by reading the exe (ARC-UI-010, [SL2](../research/ledger/ui-audio.md#save-list-arc-ui-010), two readers): the helper sorts by modification time without pinning the auto-save, F8 loads the newest save; M188/Q ("auto-slot") is correct only when the auto-save is the newest. K8 (0x403850) | No observation in a running game (F4) was made. |
| Called-shot period/slash | M189/Q: arms/legs; the exe stores 2 (arm) and 3 (leg) ([FA4](../research/ledger/combat.md#hero-attack-arc-combat-005)); the choice of cursor art K13 is not checked. Scratch values 2/3 do not establish the final damage semantics. K5 (0x54ED30), K13 (0x553990) | Study the combat effect/ART mapping separately; do not rearrange the initial layout on the basis of unverified cursor art. |
| F6/undocumented K/M/E/Home/F10 | There are handlers at the addresses (F0), but not all actions are in the original Quick Keys. | Do not lose the source inventory and do not declare complete retail verification. The F6 label remains unknown. |
| NumLock and Always Run | The original docs give effective constant running; the functions at the addresses have a menu option, a Num modifier and a Ctrl override. P3 (0x4304C0) | The truth table and pace are established from the original ([ARC-RES-047](../research/locomotion-evidence.md) L1–L8): option on: run, Ctrl: step; off: run with Ctrl or Num Lock; a click on an object is always a run. Implemented in `area` ([ARC-WORLD-020](../world/gait-runtime.md)); Shift and deferred goals are not studied. |
| The full stop/interrupt/path lifecycle | The ground-goal update and LMB-up are known; a general interrupt requires state/priority and animation branches. P2 (0x54DE50), P4 (0x433640) | Do not promise a universal RMB cancel/a perfect router; specific cases separately. |
| Conveniences outside the original and cheats | The LeftAlt highlight and the menu by clock are not confirmed in the exe. Cheat/profile/lighting/gamma keys are not an ordinary binding. K1 (0x401560), K17 (0x54B5D0), T2 (0x401000) | Do not include automatically in the original mapping. RB-held highlighting remains gamepad policy. |
| Modifier/text-focus source anomalies | FOLLOWER focus inversion; global handlers have no single focus guard; menu keyboard routing is not checked in the exe. K1 (0x401560), K2 (0x54DE50), U3 (0x546EE0) | A separate baseline decision/runtime case, not a mechanical carry-over of suspicious conditions. |

Cheat-only inventory, so that it is not taken for game input: D/Y/Shift+4 at level >=2; H/N/grave/P/F11 at >=3; V/E/X/U/brackets/apostrophe/backslash/G, Ctrl+G, Shift+G and Ctrl+KP7/8/9 at >=1. Ctrl+LeftAlt when opening the Logbook activates a separate hidden branch. These modes do not become mandatory production functionality of the new runtime. K1 (0x401560), U11 (0x53F0E0, 0x53F2F0, 0x53F350)

## KM-09 — The diagnostic layer: options after verification

**Owner-policy:** diagnostic commands require an additional layer with modifiers. The modern additional Viewer actions are diagnostic extensions, not restored original shortcuts.

**Choice of 2026-10-04:** the owner chose option B (palette, entry Ctrl+Shift+F9) for a bounded implementation. **Binding status:** implemented in Terrain Viewer WORLD by the task ARC-INPUT-001, the contract and evidence are in [diagnostic-palette.md](../input/diagnostic-palette.md). Software checks (synthetic Godot events) do not confirm native macOS delivery and a physical keyboard; this was confirmed by the owner's manual check ARC-INPUT-002 (PASS 2026-10-17, [result](../input/diagnostic-palette.md#result-2026-10-17)). The table of options below is kept as research.

A modifier does not make any occupied key free: the F1–F6 handlers at the addresses K2 (F0) often do not check Ctrl/Shift. Therefore Ctrl+Shift+F3 can still call the original broadcast on key-up if the diagnostic handler intercepts only down. Alt is especially inconvenient as a general prefix: it already affects the original force-attack/drag/targeting, and the left Alt also permits an attack on one's own Follower ([FA2](../research/ledger/combat.md#hero-attack-arc-combat-005)). Command/Option are not considered a replacement for the original Ctrl/Alt; platform variants require their own check.

| Option | How it works | Pros / limitations |
| --- | --- | --- |
| A. Direct combinations | In an explicitly enabled diagnostic host, Ctrl+Shift+F3 is the overlay; Ctrl+Shift+R is reset; Ctrl+Shift+F is fit; Ctrl+Shift+L is reload; Ctrl+Shift+wheel is diagnostic zoom over the world. | Fast and similar to the current names. These base keys are already taken; the chord is an intentional diagnostic override, not a found free original combination. The original handling must be fully suppressed for the whole lifetime, and the UI wheel/text typing must not be intercepted. More collision cases. |
| B. Diagnostic palette | The candidate Ctrl+Shift+F9 opens a separate palette/menu; there are the passability checkbox, fit/reset/reload, zoom presets 1.00/2.00 and brief diagnostics. | The recommended initial option: one entry into the layer, the functions are visible, the ordinary F1–F5/R/F/L are preserved. F9 was not found in the researched map, but retail/OS delivery is still to be checked. An extra action for the toggle, but fewer reserved chords. |
| C. A temporary held layer | Ctrl+Shift+F9 is held as a prefix; 1 is the overlay, 2 is fit, 3 is reset, 4 is reload, 5 is details; releasing the prefix returns the original keyboard route. | Fast repeated operations after entry, an explicitly visible active layer. Several keys must be held, more state/focus/release cases; the function 1–0 must not activate an original quick slot after leaving the layer. Chord availability on a real keyboard needs checking. |

Recommendation B preserves the ordinary original layout and reduces the number of overrides. It does not choose the final key, does not create a shortcut in the code and does not change the existing task contracts. The palette is available as a diagnostic tool; the production keyboard/mouse UI remains original. Opening the palette obeys the modal/pause policy of the explicitly chosen host, and introduces no implicit change of game time. The passability overlay itself remains read-only and does not change the actor/route/camera/time.

Mandatory rules of any implementation of the layer:

1. A bare modifier does not launch a diagnostic command; the original Ctrl/Shift/Alt+mouse are preserved outside the active diagnostic context.
2. The chord is recognized before the original mapping; the event owner is kept until the main key is released, even if the modifier is released first. Key-up must not leak into a broadcast, a quick slot or a UI toggle.
3. Only one action per full chord; echo/repeated down does not do several Reset/Reload/Toggle.
4. During text entry, a gameplay modal or an invalid focus, the layer does not claim text typing/a game action. A separate modal context is defined for the palette itself.
5. Losing focus, opening/closing the layer and changing the input method clear/isolate the held state; returning does not create a deferred command. While the buttons are not released, they must not be treated as a new world intent.
6. All diagnostic actions are available from the palette/settings without needing a full F-keyboard. The gamepad enters through its own UI surface, without new mandatory game buttons.
7. The layer mapping is configured separately from the original KBM and gamepad presets. A shared physical shortcut must not silently override a gameplay or system shortcut.
8. Collision is checked by combination, context, down/up, live modifiers and repeat, not only by the key name. Checking on macOS and a real keyboard remains a separate gate.

## KM-10 — Current Viewer discrepancies and the boundaries of the work

On the SW slice [GodotInputAdapter](https://github.com/Simarglok/arcanum-steamwoven/blob/db8bcd0ff48983b4e90a4ff30a66a9ad48d79e7c/src/Arcanum.Input.Godot/GodotInputAdapter.cs) creates a WASD actor vector, I=Inspect, C=Context, Enter/Space=Confirm, F1=Settings and W/S=menu navigation. [TerrainViewerWorld](https://github.com/Simarglok/arcanum-steamwoven/blob/db8bcd0ff48983b4e90a4ff30a66a9ad48d79e7c/tools/terrain-viewer/Godot/TerrainViewerWorld.cs) uses LMB drag, R reset, F fit, L reload and wheel/+/- zoom. This is a diagnostic scheme; it is not the original controls and must not become the production default. A new candidate must be checked separately: this inventory does not claim the live state of all branches/builds.

**After ARC-INPUT-001:** in the Viewer's WORLD modes the keyboard no longer creates an actor vector, F1/R/F/L, I/C/Space and wheel/+/-/LMB drag carry no diagnostic actions; the original keys are reserved as not yet implemented, arrows pan with a fixed direction, and Fit/Reset scene/Reload/exact zoom were moved to the palette ([diagnostic-palette.md](../input/diagnostic-palette.md)). The lab layout of GodotInputAdapter remained only for the Gamepad Lab and standalone modes. Follower commands, quick slots and game screens are still not implemented.

**After ARC-WORLD-007:** in `area` an LMB press on the ground (not absorbed by the HUD/palette) sets the route target cell ([click route](../world/click-route-runtime.md#viewer-binding-stage-2)); release cancels nothing, repeating the same cell keeps the target, the stick takes over movement at once (KM-03). The cursor and the path preview are not implemented; the physical mouse was checked only by the owner's manual check ARC-INPUT-002 (2026-10-17: LMB on the ground sets the target, [result](../input/diagnostic-palette.md#result-2026-10-17)); running (Ctrl+LMB, Num Lock, the Always Run option) was added in ARC-WORLD-020 ([gait runtime](../world/gait-runtime.md)).

Separate implementations are needed for the original KBM adapter/context routing, click-to-move goal + bounded route planning, the available original UI actions and the chosen diagnostic layer. They use the shared simulation and the existing gamepad contracts, and do not require creating a second game simulation. The shared lab keyboard for synthetic controller tests does not confirm the original keyboard mapping. Tests that hard-require WASD actor movement cannot be used as a future compatibility gate.

This document does not authorize changing task contracts, starting development, cleaning up other people's worktrees, release or merge. The statuses of existing tasks and their earlier acceptance evidence are not rewritten by the research result. Tasks are set by the owner.

## KM-11 — Acceptance scenarios for future tasks

| Group | Required check |
| --- | --- |
| Layout | W is map, S is sleep/wait, A is the active recent action, R is ready/unready; no WASD key changes the actor position. C/F/I/L/O/T/V and the corresponding toggles have the initial actions. |
| Quick actions | 1–0 activate exactly their own slot; a target-required action enters targeting; A repeats the designated active action. Drag assigns/removes a slot; the controller template does not shorten the original bank. |
| Follower | F1–F5 match M/Q, the target is chosen by the original pointer context. An ordinary F3 works even if a debug overlay exists. F6 gets no invented command. |
| Movement | Click-down/up sets the path and continues it; a new cell replaces the target; Shift-ground does not start ordinary walking; Blocked/Unknown are not ignored. A mouse target is not canceled by the neutral (no-direction) state of a keyboard acting as a gamepad-style surrogate stick. |
| Mouse actions | Door/NPC/item/container/corpse, readiness and modifiers select exactly the documented actions; Alt+LMB moving an adjacent corpse to the character needs no mouse-camera drag. A UI click is not duplicated in the world. |
| Camera | Arrows pan the view; a click sets the actor goal; Home/map context and edge-scroll are checked separately. Changing the gamepad inversion does not change the arrow direction. |
| Combat | Space in the world changes TB/RT, R is readiness; Space in interfaces is checked separately. Called shots: comma/head, period/arms, slash/legs, without trusting conflicting cursor comments. |
| Saves | F7 writes the chosen auto-slot; F8 loads the newest save (SL2, ARC-UI-010). The ban on saving on another actor's turn/with a dead PC is checked against the baseline; do not claim original save compatibility. |
| Input ownership | Text entry, modal UI, follower targeting, another actor's turn, lose/refocus and the KBM/gamepad switch do not cause a double action. The original global guards are recorded separately, without blanket bans. |
| Diagnostics | The chosen layer has checks of modifier-first/key-first release, echo, losing focus, modal/text entry, the original F3 broadcast and slot 1 after leaving the layer. Toggling the overlay does not change the simulation state. |
| Platform | A real keyboard/mouse on macOS, the function row, the available modifier sides/NumLock and the chosen layouts. Synthetic InputEvent tests do not prove physical delivery/convenience or retail parity. |

Acceptance is divided into (1) source/document validation; (2) independent synthetic command/context tests; (3) native Godot UI/export and real devices; (4) comparison with the original runtime for disputed cases. This document closes only the research and the source reconciliation, not items 2–4. Each implementation card sets the supported subset, the source references and the specific checks; unavailable parts are not declared restored.

## Research coverage and the remainder

Reviewed: the central keyboard/mouse routes of the main loop (0x401000–0x402010, 4 functions) and of the game interface (0x549B70–0x557CF0, 160 functions), TIG message/kb/mouse/window/button (addresses not matched), the selected ground goals/interrupt/path entry, broadcast and contextual consumers hotkey, dialog/tc, follower, combat, Fate, world map, textedit, mainmenu, logbook, character, inventory, item, skill, spell, schematic, written, sleep, slide and compact UI. The search for scancodes/keycodes/modifiers covered the interface and game logic functions; this is not a claim that every function of these groups or multiplayer/editor was fully studied.

What remains: the original executable's timing/auto-repeat/full path; localized broadcast/rules and menus; F6; exact called-shot combat/ART association; Space across all windows; the F8 baseline; the Shift branch of running and deferred goals (the NumLock/AlwaysRun/Ctrl table is established in ARC-RES-047); focus anomalies; platform delivery of input. The absence of a branch in the F0 read does not prove the absence of an original function. New source/runtime findings update this single thematic document; separate history/review/recheck files in requirements are not needed.

## Locator codes

K, P, U and T are location codes in the retail `Arcanum.exe` 1.0.7.4; the function address is given in parentheses next to the code. Codes without an address (P7, T1, T3, T4, T5, U10) belong to the TIG library inside the exe (message queue, keyboard, mouse, windows); their addresses are not yet matched. The basis of all codes is a read of the original's functions at the stated addresses, level F0 (not checked in the exe) unless the line is in the verification ledger; confirmed places are named by ledger lines (LB1, CS1, RT4, FA1–FA4).


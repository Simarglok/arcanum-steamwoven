# Interface mode stack (K16, MIG16)

Status: **slices 1 and 2 implemented (ARC-DEV-108, ARC-DEV-109, 2026-11-05): the stack and its tables in `Arcanum.World`; the viewer owns one stack, its windows open and close through it, and the cursor and the window part of the modal test read it. Target modes, input gating and Esc are S3 and S4.**
Concept document K16 of the [architecture audit](../research/architecture-audit-plan.md) and design of MIG16 ([target model](target-model.md#migration-order),
element TM13, gap GR35). Behaviour from the executable stays the original's rows (SA36–SA55 of the
[scripts and authority reading](original-scripts-authority.md#5-the-interface-mode-machine)); this page fixes how the viewer will use them and what is built so far.

## The gap (GR35)

The gap as it was before slice 2 (the viewer now owns the stack, see [What slice 2 builds](#what-slice-2-builds)):

The original keeps one stack of interface modes (0x64C634, depth 0x64C6B8) changed by one function (0x551A80: 0 pops, anything else pushes). The mode picks
the cursor, says who owns the mouse (the game view acts only in modes 0, 1, 2, 3, 16 and 18) and runs a leave and an enter case that open and close the windows.
Steamwoven has no stack: `AreaCursorMode` (`Game`, `SpellTarget`, `SkillTarget`, `Window`; `tools/terrain-viewer/Core/AreaCursor.cs`) is derived in `TerrainViewerCursor.cs` from the spell-targeting state and `WorldModal` only (`SkillTarget` is never derived), `WorldModal` is an or of twelve
`…Open` flags and the hero-death cover, and `WorldUiFocus` asks Godot which control has focus. Each window is its own panel with its own flag and its own input route. Cases where the two differ:
a target mode opened over the inventory (the original pops twice when it is left), window-to-window moves, the mouse owner in a dialog (the game view still acts in mode 3), and Esc
in a target mode (SA51: Esc is not handled there; the main menu opens).

## Target design (TM13)

A Godot-free `InterfaceModeStack` in the input layer of the world runtime (`src/Arcanum.World`, GQ6 A), not saved (the original does not save it); the Godot panels open and close from
it. Principles:

- **One change function.** `Push(mode)` and `Pop()` are the original's two calls; both run the old mode's leave case and the new mode's enter case as *steps* and apply the
  state first. An observer carries the steps out (open or close a panel, end or suspend a dialog, put a held item back). While a change runs, a nested change does nothing
  (the original's lock, SA37).
- **Openers are policies over it.** `OpenWindow` toggles a window that is on top, refuses for a dead hero (and, for some, an unconscious one, SA52), else pops and pushes. `OpenTarget`
  pushes a target mode over the inventory (when the target mask has the inventory bit) or replaces the top. `OpenOverlay` pushes the quantity picker, the matchmaker and the follower order.
- **Leaving a spell or skill target pops once more** (SA38), so the inventory below goes too: [0, inventory, target] → [0].
- **Tables, not flags.** Cursor family (SA41), who owns the mouse and the scroll (SA44), which windows bring the hidden bar back, which leave cases close a window or put an item back are
  pure functions of the mode (`InterfaceModes`).
- **Rule modes (D8, D9).** Where the original behaviour is a technical accident a player never meets (a stack depth overflow) Steamwoven keeps a typed refusal; where it is a rule (Esc in a
  target mode, window-to-window moves) the question goes to the owner as a card in slice S4, with the original as the Original setting (owner decision D8 on rule-mode switches). Nothing in S1 differs between the modes.

## What slice 1 builds

| Part | Where | What |
| --- | --- | --- |
| `InterfaceMode` | `InterfaceModes.cs` | The original's 21 numbers (0 game … 20 inventory kind 6; 12 unused). |
| `InterfaceModes` | `InterfaceModes.cs` | The tables of SA37–SA45 and SA52: window, target and overlay kinds, cursor, game view acts, scroll, follower keys, bar restore, leave closes window, held item put back, leave pops again, opener refusal. |
| `InterfaceModeStack` | `InterfaceModeStack.cs` | The stack (base 0, 33 slots as the original, `InterfaceModeLimits.MaxDepth`), `Push`, `Pop`, `Reset` (no cases), `OpenWindow`, `OpenTarget`, `OpenOverlay`, the lock, the observer. |
| Results | `InterfaceModeStack.cs` | `InterfaceModeChange` (status `Applied`, `Locked`, `Refused`, `DepthExceeded`, `InvalidMode`; the steps in the original's order), `InterfaceModeStep` (leave or enter, mode, other, popped). |
| Tests | `tests/Arcanum.World.Tests/InterfaceModeStackTests.cs` | Synthetic: push, pop at the base, window replace and toggle, openers and refusals, target modes over the inventory popping twice, the item target's held item, the quantity picker, dialog end versus suspend, the lock, reset, the limit, the tables. |

The observer receives the steps after every pass of a change has run, with the stack already in its final state (the original runs leave and enter pass by pass); a host reads the step, not `Current`. `Reset` ignores the lock, as the original's does. An opener of a window that is on the stack closes it, popping through any overlay above it (inference).

Deliberate deviations, all invisible in play: a push over the limit returns `DepthExceeded` where the original would overwrite its depth word (depth in play is three); mode 0 cannot be pushed (the
original's argument 0 is the pop). Inferences kept from the reading and marked in the code: a push over mode 1 or 2 is popped again by the repeat (SA38); target modes started without the inventory bit
replace the top; the toggle of a window under an overlay pops through the overlay; loot, barter and steal windows close on every destination the rows do not name; only mode 8 stands for the inventory opener's dead-hero refusal.

## What slice 2 builds

The viewer owns one stack and the windows that are modes open, close and are asked about through it. No change a player can see on the paths the viewer's guards allow: the openers keep their guards, their order of steps and their sounds. One difference: an open asked while another window mode is on the stack is refused and logged (`ARC-DEV-109 … not opened`), where the old code would have shown both panels; every viewer path already closes the other window first or waits (listed below), and the two that did not (the hotkey bar's Repair slot, which now answers "ignored while another window is open", and the container loot, which now refuses before its session part runs) are the only visible corner.

| Part | Where | What |
| --- | --- | --- |
| `AreaInterfaceWindows` | `tools/terrain-viewer/Core/AreaInterfaceWindows.cs` | The viewer's one `InterfaceModeStack` (it is the stack's observer) and the hosts of the window modes on it. `Open(mode, show, hide)` registers the panel's show and hide and asks the stack's window opener; the enter step of the pushed mode runs `show`, the leave step that closes the window runs `hide`. `Close(mode)` is the opener's toggle of a window that is on the stack. `IsOpen`, `AnyOpen`. Result `AreaWindowOpen`: `Opened`, `AlreadyOpen`, `AnotherWindowOpen`, `Refused`. |
| The viewer partial | `tools/terrain-viewer/Godot/TerrainViewerInterfaceModes.cs` | `_interfaceWindows`, `WindowOpen(mode)`, `AnyWindowOpen`, `WindowCanOpen(mode)` (an early test of an opener, before it changes anything: false when the window is open, or, logged, when another window is), `OpenWindowMode`, `CloseWindowMode`. |
| Windows | `TerrainViewerConversation`, `…AreaBarter`, `…AreaWorldMap`, `…AreaLogbook`, `…AreaInventory`, `…AreaSheet`, `…AreaSchematics`, `…AreaReading` | The `…Open` flag of each window is `WindowOpen(its mode)` (the inventory's is any of its three modes); its opener builds the view, asks `OpenWindowMode(mode, panel.Open, panel.CloseView)` where it used to call `panel.Open`; its End routine calls `CloseWindowMode(mode)` where it used to call `panel.CloseView`. The routines keep their steps before and after (sounds, the editor and junk-pile closes, quarantine, status). |
| Modal test | `TerrainViewerWorld.cs` | `WorldModal` is `PaletteOpen`, `AnyWindowOpen`, the save list, the main menu, the hero-death cover and the bug report; the eight window flags are gone from the or. |
| Cursor | `TerrainViewerCursor.cs`, `AreaCursor.cs` | `CursorMode` is the spell cursor while spell targeting runs (S3), else `AreaCursorArt.Mode(stack.Cursor)` (SA41, the gauntlet in every window mode), else the gauntlet under a panel outside the stack. |
| Tests | `tests/Arcanum.TerrainViewer.Tests/AreaInterfaceWindowsTests.cs` | Synthetic: every window mode shows once and hides once with the stack already in its final state, the gauntlet cursor, open and another window change nothing, non-window modes are refused, the dialog is ended by its pop, the lock keeps a stack change out of a show or hide, the cursor family to `AreaCursorMode`. The viewer self-test and the per-PR smokes below run the panels, and the self-tests that open a window also require `WindowPanelsMatchStack` (each panel is shown exactly when its mode is on the stack). |

Decisions kept small, all invisible in play:

- **The inventory panel is three modes**, as the original's inventory opener has them: the hero's own list is mode 8, a body, a container or a junk pile is the loot window (10), a pocket being picked is the steal window (11). All three are window modes with the gauntlet; `InventoryOpen` is any of the three. The table of S1 names loot and steal windows as inference-closing (`LeaveClosesWindow`); nothing leaves them for another mode in S2.
- **Show and hide are the panel's own Open and CloseView.** An observer callback must not ask for another change (the lock turns it into `Locked`), and an End routine can open the next window (the dialog's resumed script, the barter after `b:`), so each End routine runs its steps around the stack call, not inside it. A stack change that closes a window for another reason (S4's window-to-window moves) therefore has to go through the window's End routine too; the bar button does so today (`BarWindow` closes the other window, then opens its own).
- **One window at a time, as before.** `AreaInterfaceWindows.Open` changes nothing when another window mode is on the stack (`AnotherWindowOpen`); the viewer's openers already close the other window first (the bar button, the reading from the inventory) or wait while any window is open (a dialog's barter, a script's reading). The stack's own replace is S4's window-to-window move, decided by the owner (D9).
- **The hero's refusals (SA52) stay with the viewer's own checks.** The viewers' openers keep the dead-hero refusals they have (the conversation, sheet, logbook, schematic, barter and reading builds); the stack's opener is not given `heroDead` and `heroUnconscious`, so no refusal the viewer did not make is added in this slice (the original's unconscious-hero refusals of the dialog and the world map, and the table's dead-hero refusal of the world map, are S4's to settle with the owner as rule modes, D9).
- **The dialog is ended by a pop.** The mode table suspends a dialog under a push and ends it on a pop (row 3); the viewer pushes nothing over a dialog (a barter or a schematic window from `b:` or `su` opens after the talk ended and does not resume it, BT7, NC2), so the dialog's leave hides its panel.
- **Panels outside the stack** (the palette, the main menu, the save list, the bug report, the hero-death cover) keep their flags and stay in `WorldModal` and in the cursor test. The opening shop's barter window opens on the stack while the main menu is open (the menu is no mode).

## Slices

| Slice | Result | Touches | Smoke | State |
| --- | --- | --- | --- | --- |
| S1 (ARC-DEV-108) | Concept K16, the stack and its tables with tests | `src/Arcanum.World`, docs | none (nothing in the viewer changes) | **Done** |
| S2 (ARC-DEV-109) | The viewer owns one stack; the windows that are modes (conversation 3, barter 4, world map 5, logbook 7, inventory 8 with its loot 10 and steal 11 kinds, sheet 9, schematic 14, reading 15) open and close through it; `AreaCursorMode` and the window part of `WorldModal` are read from it; the panels outside the stack (main menu, save list, palette, hero-death cover, bug report) keep their flags | `TerrainViewerWorld`, `TerrainViewerCursor`, the panel partials, `AreaInterfaceWindows` | the window stages (world map, barter, container, junk, pickpocket, hover, report, dead-window, the Crash Site dialogs, menu, scene, bar, death) and the self-test | **Done** |
| S3 (ARC-DEV-110) | The target modes on the stack: spell targeting (1) and item use (16) over the inventory pop twice; skill target (2), quantity picker (13) and follower order (18) as the viewer gains them; held item put back on leave | spell targeting, cursor item, inventory | spell, inventory, cursor stages | planned |
| S4 (ARC-DEV-111) | Game view and scroll gating by mode (SA44), Esc and window-to-window moves as rule-mode switches decided by the owner (D9), coverage rows | input routes | SM23 of the [open bugs plan](../research/open-bugs-migration-smoke-plan.md) | planned |

MIG16 touches no residency, sector, heartbeat or save code, and no snapshot schema (the stack is not saved), so it runs beside MIG9 and needs no schema number.

## Open questions (from the reading)

Esc in target modes (Q7: whether the main menu closes one is read only for the inventory close); inventory kinds 19 and 20 (their callers are not read); the dialog's callback ordering; the toggle for
a window that sits under an overlay, and the next-mode dependence of the bar hiding in modes 19 and 20. Each is read in the executable when its slice needs it (owner decision 2026-10-20), not guessed.

# Interface mode stack (K16, MIG16)

Status: **slice 1 implemented (ARC-DEV-108, 2026-11-05): the stack and its tables in `Arcanum.World`, not yet used by the viewer.**
Concept document K16 of the [architecture audit](../research/architecture-audit-plan.md) and design of MIG16 ([target model](target-model.md#migration-order),
element TM13, gap GR35). Behaviour from the executable stays the original's rows (SA36–SA55 of the
[scripts and authority reading](original-scripts-authority.md#5-the-interface-mode-machine)); this page fixes how the viewer will use them and what is built so far.

## The gap (GR35)

The original keeps one stack of interface modes (0x64C634, depth 0x64C6B8) changed by one function (0x551A80: 0 pops, anything else pushes). The mode picks
the cursor, says who owns the mouse (the game view acts only in modes 0, 1, 2, 3, 16 and 18) and runs a leave and an enter case that open and close the windows.
Steamwoven has no stack: `AreaCursorMode` (`Game`, `SpellTarget`, `SkillTarget`, `Window`) is derived from which panel is open, `WorldModal` is an or of thirteen
`…Open` flags, and `WorldUiFocus` asks Godot which control has focus. Each window is its own panel with its own flag and its own input route. Cases where the two differ:
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
- **Rule modes (D9).** Where the original behaviour is a bug a player never wants (a lost held item, a stack depth overflow) Steamwoven keeps the typed refusal; where it is a rule (Esc in a
  target mode, window-to-window moves) the open question goes to the owner as a card in slice S4, with the original as the Original setting. Nothing in S1 differs between the modes.

## What slice 1 builds

| Part | Where | What |
| --- | --- | --- |
| `InterfaceMode` | `InterfaceModes.cs` | The original's 21 numbers (0 game … 20 inventory kind 6; 12 unused). |
| `InterfaceModes` | `InterfaceModes.cs` | The tables of SA37–SA45 and SA52: window, target and overlay kinds, cursor, game view acts, scroll, follower keys, bar restore, leave closes window, held item put back, leave pops again, opener refusal. |
| `InterfaceModeStack` | `InterfaceModeStack.cs` | The stack (base 0, 33 slots as the original, `InterfaceModeLimits.MaxDepth`), `Push`, `Pop`, `Reset` (no cases), `OpenWindow`, `OpenTarget`, `OpenOverlay`, the lock, the observer. |
| Results | `InterfaceModeStack.cs` | `InterfaceModeChange` (status `Applied`, `Locked`, `Refused`, `DepthExceeded`, `InvalidMode`; the steps in the original's order), `InterfaceModeStep` (leave or enter, mode, other, popped). |
| Tests | `tests/Arcanum.World.Tests/InterfaceModeStackTests.cs` | Synthetic: push, pop at the base, window replace and toggle, openers and refusals, target modes over the inventory popping twice, the item target's held item, the quantity picker, dialog end versus suspend, the lock, reset, the limit, the tables. |

Deliberate deviations, all invisible in play: a push over the limit returns `DepthExceeded` where the original would overwrite its depth word (depth in play is three); mode 0 cannot be pushed (the
original's argument 0 is the pop). Inferences kept from the reading and marked in the code: a push over mode 1 or 2 is popped again by the repeat (SA38); target modes started without the inventory bit
replace the top; a window under an overlay is replaced, not toggled.

## Slices

| Slice | Result | Touches | Smoke | State |
| --- | --- | --- | --- | --- |
| S1 (ARC-DEV-108) | Concept K16, the stack and its tables with tests | `src/Arcanum.World`, docs | none (nothing in the viewer changes) | **Done** |
| S2 | The viewer owns one stack; the windows that are modes (conversation 3, barter 4, world map 5, logbook 7, inventory 8, sheet 9, schematic 14, reading 15) open and close through it; `AreaCursorMode` and the window part of `WorldModal` are read from it; the panels outside the stack (main menu, save list, palette, hero-death cover, bug report) keep their flags | `TerrainViewerWorld`, `TerrainViewerCursor`, the panel partials | window stages, hover, game bar | planned |
| S3 | The target modes on the stack: spell targeting (1) and item use (16) over the inventory pop twice; skill target (2), quantity picker (13) and follower order (18) as the viewer gains them; held item put back on leave | spell targeting, cursor item, inventory | spell, inventory, cursor stages | planned |
| S4 | Game view and scroll gating by mode (SA44), Esc and window-to-window moves as rule-mode switches decided by the owner (D9), coverage rows | input routes | SM23 of the [open bugs plan](../research/open-bugs-migration-smoke-plan.md) | planned |

MIG16 touches no residency, sector, heartbeat or save code, and no snapshot schema (the stack is not saved), so it runs beside MIG9 and needs no schema number.

## Open questions (from the reading)

Esc in target modes (Q7: whether the main menu closes one is read only for the inventory close); inventory kinds 19 and 20 (their callers are not read); the dialog's callback ordering; a toggle for
a window that sits under an overlay. Each is read in the executable when its slice needs it (owner decision 2026-10-20), not guessed.

# Hotkeys

Every keyboard and mouse control of the game, grouped by purpose; [the alpha instruction](alpha-build.md) names only the keys of its own steps. These are the controls of the packaged alpha and of the map mode (`./arcanum area`, `./arcanum map`), as the
code on `main` routes them (ARC-DEV-053). Keys follow the original game where it has them; the rows marked *Steamwoven*
are this project's own.

- **Mac:** on a keyboard whose F keys control brightness or volume, hold **fn** with every F key (**fn+F7**,
  **fn+Ctrl+Shift+F10**). **Ctrl** is the Control key on a Mac too, never Command.
- A, C, F, I, L, R, S, T and W with Ctrl, Alt or Command held do nothing in play, as in the original.
- An open window, conversation, menu or the diagnostic palette takes the keys first: the *In play* keys act only while
  none is open.

**In play**

| Key | Action |
| --- | --- |
| Escape | In-play menu (save, load, options, quit) |
| F7 / F8 | Quick save / quick load (one slot) |
| I | Inventory |
| C | Character sheet |
| L | Logbook |
| T | Technological schematics |
| S | Sleep or wait window (the bar's Sleep button too) |
| M | Spell pane, shown or hidden (on the key's release, while the hero can cast) |
| K | Skill pane (Prowling, Pick Pocket, Disarm Traps, Repair), shown or hidden in the bottom bar's message window (on the key's release; the same as the Skills button; needs the interface bars) |
| W | World map; in the [debug mode](alpha-build.md#7-debug-mode) it also lists every area with **Teleport** |
| Num Lock | Run by default on or off (the original's Num Lock lock; on a keyboard that has the key) |
| Arrow keys | Pan the camera |
| H | Legacy or HD graphics (needs a local [HD pack](../formats/hd-pack.md)); *Steamwoven* |
| F11 | Fullscreen on or off; *Steamwoven* |

**Combat**

| Key | Action |
| --- | --- |
| R | Combat mode (ready / unready) |
| Space | Turn-based on or off (also the default combat mode of the options) |
| E | End the hero's turn in a turn-based round (on the key's release) |
| `,` `.` `/` held while clicking a foe | Called shot at the head, the arms, the legs |
| Alt + left click (combat mode) | Attack an object (a door, a window, a container, scenery) or loot a corpse |

**Quick bar**

| Key | Action |
| --- | --- |
| 1 … 9, 0 | Use quick slot 1 … 10 |
| A | Repeat the last action |
| Ctrl+1 … Ctrl+0 | Put the last action on that slot |
| Ctrl+Shift+1 … Ctrl+Shift+0 | Clear that slot |
| 1 … 0 with an item held in the inventory | Put the held item on that slot |

**Orders to companions** (on the key's release, to every follower within 10 tiles)

| Key | Order |
| --- | --- |
| F1 | Walk to the tile under the pointer |
| F2 | Attack |
| F3 | Stay close |
| F4 | Spread out |
| F5 | Back off (stop attacking) |
| F6 | Follow in the *Steamwoven* rules; nothing in the *Original* rules (the original's preset names nobody) |

**Mouse**

| Input | Action |
| --- | --- |
| Left click | Walk there, or use, talk to, pick up or attack (combat mode) what is under the pointer |
| Ctrl + left click on the ground | Run there; walk there instead while the Always run setting is on. On a Mac, Ctrl+click works the same |
| Shift while using an item or casting a spell | Use it on the hero, cast it at the hero |
| Alt held at a spell's target cursor | Let an aggressive spell pick the hero's party too |
| Right click | Cancel a spell's target cursor |
| Left click on a quick bar slot or recent-action button | Use it (with an item held in the inventory: put the item on the slot) |
| Left press on a college's button in the spell pane; left click on a spell's icon there | Show that college's known spells; use the spell (as the hotkey does) |
| Left click on a button in the skill pane | Use the skill: Prowling toggles, Pick Pocket and Disarm Traps ask for a target (left click on it, right click cancels), Repair opens the inventory |
| Right press and drag from a quick bar slot, a spell of the spell pane, a button of the skill pane or a slot of the weapon pane | Move it on the bar, put it on a slot, or drag it off the bar to empty the slot |
| Click on a follower's portrait | That follower's order menu (a walk order takes the next left click as its tile) |
| Left click on a spell icon in the five top slots | End that maintained spell (hovering it shows the spell) |

**Inside windows** (the open window takes every key)

| Window | Keys |
| --- | --- |
| Inventory | I or Escape closes; 1 … 0 puts the held item on a quick slot |
| Quantity box | keypad digits type (the main-row digits do not), Backspace or Delete erases, Enter accepts, Escape or I cancels |
| Character sheet | C or Escape closes |
| Logbook | L or Escape closes; Tab switches the tab |
| Technological schematics | T or Escape closes; Tab assembles |
| World map | W or Escape closes (or ends the journey); Enter travels |
| Book or note | Escape closes; Left or Page Up, Right or Page Down turns a page |
| Barter | Escape closes |
| Conversation | click a reply; keys do nothing |
| Save and load windows | Up / Down select; Enter saves or loads; Delete or Backspace deletes (while no description is typed); Escape closes; typing writes the save's description |
| Main menu and in-play menu | the letter of a button presses it; Escape goes back; Enter answers yes in the quit box and accepts the character editor and the opening shop |
| Options | Up / Down move the focus, Left / Right change the focused control; O closes the menu opened in play |
| Death screen | a key or a click ends a slide, Escape skips the rest; then, in the *Steamwoven* rules with a save there, Enter or Y loads the last save and Escape or N declines (the *Original* rules return to the main menu) |

**Tools** (*Steamwoven*, not part of the original game)

| Key | Action |
| --- | --- |
| Ctrl+B | [Bug report](alpha-build.md#9-reporting-a-bug-ctrlb) over anything: Ctrl+Enter (or Command+Enter) sends, Escape cancels |
| Ctrl+Shift+F9 | Diagnostic palette |
| Ctrl+Shift+F10 | [Diagnostic panel](alpha-build.md#8-showing-and-hiding-the-diagnostic-panel) shown or hidden |
| Shift held while the packaged game starts | Opens the launcher (debug mode, settings) |

**Not there yet.** These original keys do nothing yet (the diagnostic panel, when shown, names them
`Reserved: … (NOT IMPLEMENTED)`): F (fate points), O (options; Escape opens them),
V (version), Home (centre on the hero), Enter (message), F10 (compact interface), F12 (screenshot). Attacking a follower
(left Alt + click in combat mode) is refused as not ready yet. `,` `.` `/` pressed alone also show the reserved notice;
held during an attack click they aim as listed under *Combat*.

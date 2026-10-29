# Original look of the inventory, loot, character, logbook and world-map windows (ARC-UI-051, ARC-UI-052)

The owner's playtest of the packaged alpha (main `72c7b932`, 2026-10-28) found that the inventory (`I`),
character sheet (`C`), logbook (`L`) and world map (`W`) open diagnostic panels without the original graphics.
This plan reads, in the retail 1.0.7.4 `Arcanum.exe` (SHA-256 `9bd80511…8d52`, read with `llvm-objdump` on
2026-10-28 by this task, one reader), where each window takes its art and layout, and splits the work into one
task per window. Behaviour stays as it is: each slice replaces only the presentation, keeps every action the
diagnostic window offers (the diagnostic view stays one click away) and keeps the synthetic self-tests, which
have no game folder, on the diagnostic look.

Art numbers are messages of `art\interface\interface.mes`; an interface art is built by 0x504330 (number,
frame, flag, palette) and blitted by 0x51DA80 into the window. Every window below is 800 × 400 at (0, 41) of the
800 × 600 screen, between the top strip (art 185) and the bottom strip (art 184) of the
[interface bars](../research/ledger/ui-audio.md#interface-bars-arc-ui-018). Sizes are the installed arts' frame sizes.
Facts marked F0 carry their address and are re-read by the slice that implements them (owner decision 2026-10-20).

## Order

1. **Inventory** (this task, ARC-UI-051): the most used window and the one whose frame (doll rectangles, grid,
   boxes) the viewer already follows (IW14). Done here; see below.
2. **Character sheet**: one background art plus the existing page logic (skills, spells, stats) that already
   follows the exe's tables (CS1–CS7, SP1–SP8, the spells page).
3. **Logbook**: two background arts and a page art per tab; the tab logic exists (LB1–LB13).
4. **World map**: the background and the two zoom buttons; the map itself (tiles, town maps) is the largest
   slice and may split again (see below). The debug "travel anywhere" mode is a separate task, not part of this plan.

## Inventory window (done: ARC-UI-051)

Read in the redraw 0x576520 and the open 0x572340; rows [IW20–IW25](../research/ledger/ui-audio.md#inventory-window-art-arc-ui-051).

| Part | Art | Place in the window | Notes |
| --- | --- | --- | --- |
| Paper doll background | 223 `PDoll` 358 × 400 | (0, 0) | modes 0, 5, 6 (0x5782D0); mode 1 (barter) 235 `Barter` (825 `Barter_Follower` when 0x681100 is set, F0), modes 2–4 234 `Loot` |
| Pack background | 221 `Inventor` 442 × 400 | (358, 0) | grid 10 × 12 cells of 32 from (368, 8) (IW14) |
| Empty wear slot | 241–249 `CVR_*` | the slot's top-left (table 0x5CAC58) | table 0x5CAF58; a held worn item's slot also shows its cover |
| Worn item | item art | centred in the slot | weapons and armour draw their paper-doll art (fields 0x71, 0x97), others the inventory art (0x5D) |
| Grid item | inventory art (0x5D) | centred in its cells | cell size of an item from 0x461780 (its art's size) |
| Drop box | 224 off / 225 on, 348 refused | (703, 344), hit area = art 80 × 43 | "on" while a held item is over it |
| Use box | 342 off / 343 on, 347 refused | (703, 290), hit area = art 80 × 43 | "refused" only in modes 1–3 |
| Lines | font 0x682C74 | (145, 29), (145, 44), (145, 59), 185 × 15 | `inven_ui.mes` 9 + 6, 10 + `critter.mes` 18 + level, 11 + speed |
| Counts | font 0x68345C | (729, 70 + 23 × i), 58 × 20 | gold, arrows, bullets, charges, fuel |

Done in the viewer: backgrounds, covers, item arts (inventory art everywhere, scaled down when larger than the
item's place), boxes with their hover art, lines and counts in the project's font, the window at its place
between the bars, the "Diagnostic view" switch below it. Steal, barter and the other modes keep the diagnostic window;
the loot window is done by ARC-UI-052 (below).

Left for the next inventory slices (each its own task):

- ~~**Item sizes**~~: done by ARC-UI-055 (an item's grid size is its inventory art's frame in cells, 0x461780, ledger JP3;
  [hero grid](../world/inventory-runtime.md#hero-inventory-grid-arc-ui-019)).
- **Paper-doll art** of worn weapons and armour (fields 0x71, 0x97, art place 2 of 0x4EC290, F0).
- **Fonts**: the original's art fonts (0x682C74, 0x68345C; font arts 226–230) for the lines and counts.
- **Other modes**: loot (done by ARC-UI-052 without the doll views, below), barter (235 `Barter`, 337 `Barter_PD`, gamble box 344–346 at
  (703, 236)), steal; the doll toggle buttons (338, 339 at 0x5738A9/0x5738F9; toggle 0x68150C draws
  341 `PD_Cover4Inven` at (361, 0) and moves the doll's rectangles 348 pixels to the right (table 0x5CACE8), F0).
- The doll art's empty box at its top-left (about 18, 14 to 160, 157): the mode-0 redraw draws nothing there (only
  modes 1–6 draw a critter image, 0x553D10, at their own places); whether another path fills it is not read (F0).
- The two numbers at (80, 358) and (261, 358) (0x464630, 0x464700, F0) and the selected-item fill
  (0x681458, colour from the palette, F0).

## Loot window (done: ARC-UI-052)

The owner's bug report of 2026-10-29 (build `169ef64`): the window for searching a body drew placeholder rectangles. Read in the
open 0x5727B0, the redraw 0x576520, the hit test 0x575CB0 and the image choice 0x553D10; rows
[IW26–IW32](../research/ledger/ui-audio.md#loot-window-art-arc-ui-052). The loot window is interface mode 2 (a dead critter or a
container); the frame is the inventory window's.

| Part | Art | Place in the window | Notes |
| --- | --- | --- | --- |
| Loot background | 234 `Loot` 358 × 400 | (0, 0) | the hero's doll and weight lines are not drawn (doll toggle clear at every open) |
| Pack background, hero's grid, counts, boxes | as in the inventory window | as there | the boxes show 348 / 347 (refused) under the body's own item |
| Body's image | race icons 375–382, 634; 383–385, 434 by critter flags; containers 832–839, 435 | (183, 33) | 0x553D10; a follower's portrait is not built |
| Body's grid | item inventory art | 10 × 8 cells of 32 from (8, 136), scrolled by rows | worn items (1000..1008) only in the doll view; scroll maximum 4 (critter) or 88 (container) |
| Take all | 808 `TakeAllButt` | (301, 47) | mode 2 only |

Done in the viewer: the backgrounds, the body's image, the body's items at their inventory locations with their art (cut to the
eight shown rows; a junk pile's items at their pile slots; PROJECT: an item the session keeps at no location, -1, at the first free
place), the hero's grid, counts and boxes, the Take all button; a press on a body's item and its release on the hero's
grid takes it (a gold pile or ammunition stack asks how many), on a box or elsewhere it goes back; the mouse wheel scrolls the
body's grid (PROJECT: the original's scroll bar at (330, 136) is not drawn); the "Diagnostic view" switch shows the diagnostic
window with its Take buttons. PROJECT: a taken item goes to the hero's first free cells, not the cell it was released on.

Left for the next loot slices (each its own task): the doll views (the body's worn items on 349 `Loot_PD` at two thirds, toggles 338 /
339 at (23, 108), (74, 108); the hero's doll toggle at (713, 37), (751, 37); IW29, IW30), the scroll bar's art, the move of a hero's
item into the body, the steal window (mode 3, same frame), the button 340 at (763, 197) and the hot spots of IW30, a follower's
portrait as the image.

The round view at the window's top-left (done: ARC-UI-054, rows
[IW33–IW34](../research/ledger/ui-audio.md#round-view-arc-ui-054)): the open centres the game view on the PC and the window's
89 × 89 rectangle, (11, 9) in the own window and (16, 17) in the loot window, shows the game view's square of 178 × 178 around the
PC at half size with art 233 or 232 over it; a left release in it closes the window. The viewer copies the screen there with a
shader under the window; the barter window's view (art 231) is not built.

## Character sheet

Entry: open 0x5597C0 (`C`, CS1). The open reads art 22 `char_maint` (800 × 400, at 0x55987C) and creates its
window with callback 0x55A5C0 (window helper 0x5572D0, handle 0x64CA64); arts 22 are drawn again at 0x55EDD2 and
0x55EF2B (redraws). The skills page draws 29 `Skills_Window` (235 × 325, 0x55BAC2, 0x55BD9A, 0x55BF31) and the
spells page 31 `char_spells` (235 × 325, 0x55C8A4, 0x55CBD1). The slice reads the button and text tables of the
window (stat rows, the `+`/`−` buttons SP_Plus 258 / SP_Minus 257, the alignment art 254, the portrait) from
these functions, places the existing values at the original rectangles and keeps the existing skill and spell
logic (UI-009, MAGIC-008). F0 for every coordinate.

## Logbook

Entry: open 0x53F020 (`L`, LB1). The redraw draws 264 `LogBooks_Side` (165 × 400) at (0, 0) (0x53F191) and 260
`Book_Main` (635 × 400) at (165, 0) (0x53F4F4, x 0xA5), then the page art at (172, 23) (0x53F576, 561 × 355 pages:
259 `Book_Karma`, 261 `Book_Quests`, 262 `Book_Rumors`); the side tabs come from the table at 0x5C33F8 (9 entries).
Arts 263 `Book_Side`, 477/500 `BookBackground` (668 × 400) belong to other books (written items, ARC-UI-049) and
are checked there. The slice places the existing tab logic (rumors, quests, LB1–LB13) on these arts. F0.

## World map

Entry: open 0x560760 (`W`). The background 138 `mapmain` (800 × 400) is read at 0x561164 and the window is
created with callback 0x5618B0 and the table at 0x5C9B00 (0x5572D0). Buttons 811 `MapZoomBut` (111 × 56) and 812
`MapWholeBut` (106 × 56), 2 frames each. The world map's own picture: the `wm_tile_*` names 149–164 of
`interface.mes` are not under `art\interface` in the installed game (the slice finds where the module keeps the
world-map tiles, F0); town maps are 668 × 400 arts (626 `TarantMap`, 627 `Saltys_Map`, 687, 749, 763, 764, 806).
Suggested split: (a) background, buttons and the existing travel panel's controls at their places; (b) the
world-map picture and the party marker; (c) town maps.

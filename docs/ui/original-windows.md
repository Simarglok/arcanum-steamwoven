# Original look of the inventory, loot, character, logbook, world-map and menu windows (ARC-UI-051, ARC-UI-052, ARC-UI-058)

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
3. **Logbook** (done: ARC-UI-074, the other tabs ARC-UI-079..082, the lens and the tab hint ARC-UI-098): two background arts and a page art; the tab logic exists (LB1–LB13).
4. **World map**: the background, the buttons, the picture of 64 tiles, the scroll (ARC-UI-073), the names, readouts and the continent view
   (ARC-UI-083) and the town map (ARC-UI-084) are done; the walked cells and the original's waypoint handling follow (see below). The debug "travel anywhere" mode is a separate task, not part of this plan.

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
between the bars, the "Diagnostic view" switch below it. Barter and the other modes keep the diagnostic window;
the loot window is done by ARC-UI-052 and the steal window by ARC-UI-065 (below).

Left for the next inventory slices (each its own task):

- ~~**Item sizes**~~: done by ARC-UI-055 (an item's grid size is its inventory art's frame in cells, 0x461780, ledger JP3;
  [hero grid](../world/inventory-runtime.md#hero-inventory-grid-arc-ui-019)).
- **Paper-doll art** of worn weapons and armour (fields 0x71, 0x97, art place 2 of 0x4EC290, F0).
- **Fonts**: the original's art fonts (0x682C74, 0x68345C; font arts 226–230) for the lines and counts.
- **Other modes**: loot (done by ARC-UI-052 without the doll views, below), barter (done by ARC-UI-064, its merchant's doll view added by ARC-UI-1917; without the hero's doll view and the
  gamble, below), steal (done by ARC-UI-065, below); the doll toggle buttons (338, 339 at 0x5738A9/0x5738F9; toggle 0x68150C draws
  341 `PD_Cover4Inven` at (361, 0) and moves the doll's rectangles 348 pixels to the right (table 0x5CACE8), F0).
- The doll art's empty box at its top-left (about 18, 14 to 160, 157): the mode-0 redraw draws nothing there (only
  modes 1–6 draw a critter image, 0x553D10, at their own places); whether another path fills it is not read (F0).
- The two numbers at (80, 358) and (261, 358) of the hero's own window (0x464630, 0x464700; read by ARC-UI-1121 for the body's doll and by ARC-UI-1267 for the hero's own window, [IW41](../research/ledger/ui-audio.md#doll-values-and-weapon-overlay-arc-ui-1121)) and the selected-item fill
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

ARC-UI-072 built the body's doll view and its toggles (the loot and steal windows of a critter: the worn items on 349 `Loot_PD` at (0, 134), toggles 338 / 339 at
(23, 108), (74, 108); IW29, IW30). ARC-UI-1121 added the doll's two values and the weapon slot's fill ([IW41–IW43](../research/ledger/ui-audio.md#doll-values-and-weapon-overlay-arc-ui-1121)). ARC-UI-1267 added the same two values and the weapon fill to the hero's own window. Left for the next loot slices (each its own task): the hero's doll toggle at (713, 37), (751, 37), the scroll bar's art, the move of a hero's
item into the body, the button 340 at (763, 197) and the hot spots of IW30, a follower's portrait as the image, the right release's
move of a hero's item into the body (IW36), a move within the body's grid.

The steal window (done: ARC-UI-065, rows [IW35–IW36](../research/ledger/ui-audio.md#steal-window-arc-ui-065)): mode 3 draws the loot
window's frame, image and grids without the Take all button (made in mode 2 alone, IW30). In the original look of the viewer the
victim's item pressed and released on the hero's grid is one theft attempt and the hero's item released on the victim's grid one plant
attempt (0x574EB8 → 0x575180 / 0x575100; no quantity window, PP10); a right release on an item with nothing held makes the same
attempt (0x574262..0x574430: in modes 1–3 the right release moves the item to the other side). The boxes refuse the victim's items
(348 / 347) and drop or use the hero's as in mode 0. PROJECT: a stolen or planted item goes to the receiving side's first free place;
the original's room test before a right-release move (0x466510, its reason printed by 0x4673F0) is left to the session's insert check.

The round view at the window's top-left (done: ARC-UI-054, rows
[IW33–IW34](../research/ledger/ui-audio.md#round-view-arc-ui-054)): the open centres the game view on the PC and the window's
89 × 89 rectangle, (11, 9) in the own window and (16, 17) in the loot window, shows the game view's square of 178 × 178 around the
PC at half size with art 233 or 232 over it; a left release in it closes the window. The viewer copies the screen there with a
shader under the window. ARC-UI-060: the rectangle, its art and the release by the window's mode are one shared table
(`AreaInventoryHole`): the own window (11, 9) and 233, the barter window (16, 17) and 231, the loot and steal windows (16, 17) and
232; the barter window has its round view since ARC-UI-064 and the steal window since ARC-UI-065. ARC-UI-063: in the own window a held item
released in it goes onto the game cursor and the window closes (IW18); a left press in the game view throws it, and the cursor shows
the held item's art ([terrain viewer](../rendering/terrain-viewer.md#inventory-throw-from-the-game-cursor-arc-ui-063)).

## Barter window (done: ARC-UI-064)

Read in the open 0x5727B0, the background 0x5782D0, the redraw 0x576520, the hit test 0x575CB0, the move handler 0x573D74, the release
0x574248 and the hover quote 0x578330; rows [BW1–BW5](../research/ledger/ui-audio.md#barter-window-art-arc-ui-064). The barter window is
interface mode 1 (a `b:` reply, BT7); the frame is the inventory window's.

| Part | Art | Place in the window | Notes |
| --- | --- | --- | --- |
| Barter background | 235 `Barter` 358 × 400; 825 `Barter_Follower` | (0, 0) | 825 while the follower's cycle buttons exist: a follower's window when the PC leads another follower (BW1) |
| Pack background, hero's grid, counts, drop and use boxes | as in the inventory window | as there | the hero's doll and weight lines are not drawn (doll toggle clear at every open) |
| Merchant's image | as the loot window's (0x553D10) | (183, 33) | the merchant's, not its substitute container's; a follower's portrait is not built |
| Merchant's name | font 0x682418 | centred on x 215, line (y 102, 200 × 20) | 0x441B60 at the open (BW2) |
| Quote line | font 0x682C74 | (89, 128), 253 × 30 | the hover quote 0x578330 and the trade messages; a follower's window prints the follower's weight lines there (BW2, BW5) |
| Shown grid | item inventory art | 10 × 7 cells of 32 from (8, 168), scrolled by rows | the substitute container's items when there is one, else the merchant's; scroll maximum 5 or 89 (BW4) |
| Gamble box | 345 off, 344 on, 346 refused | (703, 236), hit area = art | on / refused by the gamble check 0x579840 while an item is held over it (BW3) |
| Round view | 231 | (16, 17) | IW33–IW34 |

Done in the viewer: the backgrounds, the image, the name and quote line in the project's font, the shown grid with its items' art and the
wheel as its scroll (PROJECT: the scroll bar at (330, 168) is not drawn), the hero's grid, counts and boxes, the gamble box in its off art,
the round view; a merchant's item released on the hero's grid is bought and a hero's item released on the merchant's grid sold through the
session (PROJECT: into the hero's first free cells, a stack whole), and a right release on an item moves it to the other side the same
way (IW36); the quote line shows a PROJECT quote text (`Buy NAME: N`) on a resting
pointer and the trade messages; the "Diagnostic view" switch shows the diagnostic window. Since ARC-UI-069 the opening shop shows this
look too, docked at the screen's (0, 41) over window 15's strips, without the round view and its release (the main menu is up, IW33,
IW34; [menu windows plan](#menu-windows-plan-arc-ui-058) item 5).

The follower's cycle buttons 757 / 758 at (155, 55), (253, 55) are built (ARC-UI-1120, BW6). Left for the next barter slices (each its own task): the gamble (check 0x579840, gamble 0x579B60, the box's on and refused arts), the
generated quote phrases (0x413490 and its neighbours, `gd_*` lines), the count dialog for stacks, the hero's grid moves and drop and use
boxes inside the barter window, the follower's weight lines, the scroll bar's art.

ARC-UI-1917 built the merchant's doll view and its two toggles in the barter window (IW29, IW30): the buttons 338 and 339 at (8, 123) and (46, 123), the toggle clear at every open;
with it set the window draws 337 `Barter_PD` at (0, 157) with the merchant's worn items in the nine rectangles of table 0x5CAD78 (the same rectangles as the loot windows' doll),
a cover on each empty slot, the doll's two values and the weapon slot's fill (where the combat host covers the map; a body the session's lists do not show shows covers only), and hides the shown grid. Left for the next barter slices (each its own task): buying a worn item
by dragging it from the doll (issue #1926), the two hot spots of IW30, the generated look of 337 and the doll cover 341 ([#1750](https://github.com/Simarglok/arcanum-steamwoven/issues/1750)).

## Character sheet

Entry: open 0x5597C0 (`C`, CS1). The open reads art 22 `char_maint` (800 × 400, at 0x55987C) and creates its
window with callback 0x55A5C0 (window helper 0x5572D0, handle 0x64CA64); arts 22 are drawn again at 0x55EDD2 and
0x55EF2B (redraws). The skills page draws 29 `Skills_Window` (235 × 325, 0x55BAC2, 0x55BD9A, 0x55BF31) and the
spells page 31 `char_spells` (235 × 325, 0x55C8A4, 0x55CBD1). The slice reads the button and text tables of the
window (stat rows, the `+`/`−` buttons SP_Plus 258 / SP_Minus 257, the alignment art 254, the portrait) from
these functions, places the existing values at the original rectangles and keeps the existing skill and spell
logic (UI-009, MAGIC-008). F0 for every coordinate.

**Main page done by ARC-UI-075** (ledger [CHW1–CHW9](../research/ledger/character.md#character-sheet-original-look-arc-ui-075)): the reading
changed the plan: the stats, the derived stats, the resistances, the gauges and the portrait on art 22 are always shown, the four tabs
(skills 34, tech 35, spells 36, auto-level schemes 568) change only the right-hand panel at window (503, 63), and there is no Close button
(`C`, or a mouse-up in (12, 10, 89, 89)). The skills page (art 29, four groups, gauge, buttons) and the tech page (art 30, disciplines, degrees, icons) are done by ARC-UI-076
(ledger CHW10–CHW16); the spells page (art 31, sixteen college tabs, five spells with greyed next and later icons, the + and − over the
icons; no header, no points line) is done by ARC-UI-077 (ledger CHW17–CHW22); ARC-UI-078 did the refusals' texts (CHW23); the NPC sheet of the dialog (`ce`) is done by ARC-UI-088 (ledger CHW24).

## Logbook (all seven tabs, the PC lens and the tab hint done: ARC-UI-074, ARC-UI-079..082, ARC-UI-098)

Entry: open 0x53F020 (`L`, LB1). Read in the create 0x53F0E0, the panel draw 0x53F490, the spread layout 0x53F8F0 and the
window table 0x5C33F8; rows [LB14–LB17](../research/ledger/ui-audio.md#logbook-arc-ui-008). The window is the game windows' 800 × 400 at
the screen's (0, 41); the table's rectangles are 16 bytes each (the window's, two text rectangles, the lens, the two page buttons, seven tab
buttons, two columns, two titles, two page numbers).

| Part | Art | Place in the window | Notes |
| --- | --- | --- | --- |
| Side panel | 264 `LogBooks_Side` 165 × 400 | (0, 0) | the PC lens (art 198 over (25, 24, 89, 89), LB23) is registered over it: the game view's round view of the hero (IW33) |
| Book | 260 `Book_Main` 635 × 400 | (165, 0) | |
| Page art | 261 561 × 355 | (172, 23) | the same for every tab (259 and 262 are not called) |
| Tab buttons | 265, 267, 271, 266, 270, 268, 269 | x 696 (695 for the fourth), y 83 + 46 × i | rumors, quests, reputation, blessings and curses, kills and injuries, background, keys; the shown tab's button is pressed |
| Page buttons | 272 previous, 273 next | (213, 77), (675, 77) | hidden at the first / last spread |
| Columns | font 229 | (222, 72, 215, 270), (468, 72, 215, 270) | entries in list order, a line between; LB15 |
| Title | font 27, "-title-" | (236, 43, 187, 19), (482, 43, 187, 19) | `logbk_ui.mes` message = tab index |
| Page numbers | font 229, "-N-" | (222, 346, 215, 15), (468, 346, 215, 15) | 2k + 1 and 2k + 2 |

Done in the viewer (`AreaLogbookArtLayout`, `AreaLogbookArtView`): the backgrounds, the seven tab buttons with their frames, the page buttons,
the titles and page numbers, the two columns with the original's spread layout and the entries in the original's colours and strike-through,
the month names, state labels and titles from the player's `logbk_ui.mes`; the "Diagnostic view" switch shows the earlier list.
Rumors and quests (ARC-UI-074), the reputation tab (the PC's reputations in stored order, each a date line over the name from
`gamereplog.mes`) and the background tab (the hero's description text cut into column-sized pieces, no date line; ARC-UI-079, ledger LB18) are
built, and so is the blessings and curses tab (the two lists merged oldest first, a blessing before a curse of the same time, blue and red, a date line over the name
from `gamebless.mes` / `gamecurse.mes`; ARC-UI-080, ledger LB19); and so is the keys tab (the PC's key numbers in stored order, each only the key's name from `gamekey.mes`; ARC-UI-081, ledger LB20) and the kills and injuries tab
(nine fixed lines, the PC's injuries struck through unless current; ARC-UI-082, ledger LB21): all seven tabs are built. ARC-UI-098 (ledger LB23): the PC lens is the inventory's round view
(`AreaRoundView`, IW33): the open centres the game view on the hero and the game view's square of 178 game pixels around the hero is shown in the lens's
rectangle at half size, the side art leaving the rectangle out and art 198 drawn over it; a left release in the rectangle (a square test) closes the window
with the close sound, a press does nothing; the pointer entering a tab button writes that tab's message of `logbk_ui.mes` (the title's text) to the message
window, only when 3 s passed since the last stored message (the world map's button path, WM61), and leaving it takes the text away. PROJECT: closing the
window with the pointer on a tab clears the hint too. Left: the developer quotes page and the logbook button's highlight.

## World map

Entry: open 0x560760 (`W`), mode 5; rows [WM1–WM125](../research/ledger/ui-audio.md#world-map-window-arc-ui-073). The window is 800 × 400 at the
screen's (0, 41) (table 0x5C9B00, callback 0x5618B0); art 138 `mapmain` is drawn at its (0, 0) and has a transparent hole, x 150..650 and
y 11..375 of the window, which is the map view (the executable's (150, 52, 501, 365) of the screen). In window coordinates the buttons are
811 `MapZoomBut` (111 × 56) at (20, 165), 812 `MapWholeBut` (106 × 56) at (19, 267), 813 `MapTravelBut` (57 × 57) at (698, 312) and the
compass 139 `navbutton` (45 × 45) at (375, 347), 2 frames each (idle, pressed); the markers are 204 `MM_Cross` (party), 205 `MM_Loc` (area),
206 `MM_WayP` (waypoint) and 814 `MM_LocNew` (new area), drawn centred on their pixel. The map picture is not under `art\interface`: the module
keeps it as `worldmap/smallmapchunks001..064.bmp` (8 × 8 tiles of 250 × 250, 8 bpp; entry 50 of `worldmap/worldmap.mes` names them) and
`worldmap/map_zoomed.bmp` (365 × 365, the continent view). A town map is not an interface art: it is a tiled picture per entry of `rules/townmap.mes`
(WM65–WM69, below). The 668 × 400 arts (626 `TarantMap`, 627, 687, 749, 763, 764, 806) take no part in the window (WM68).

Done (ARC-UI-073, `AreaWorldMapArtLayout`, `AreaWorldMapArtContent`, `AreaWorldMapArtView`): without debug mode and with the art and all 64 tiles
read, the world map shows the original window: the picture at the scroll offset (opened centred on the party, WM12, with the centring clamped to
the scroll maximum, which the original does not do, WM13), the buttons with their frames, the press and release sounds 3000/3001, the party,
the known areas, the waypoints and the route over it. Scroll: the arrow keys, 20 px per axis every 10 ms at most, left beating right and up
beating down (WM18); the compass, 40 px a step, repeating every 250 ms outside its dead zone, and a release without a drag centres on the party
(WM31); Home centres (WM22); the view stops at [0, 2000 − view] on each axis (WM19). A left press in the view selects the waypoint under it or else adds one (WM27, ARC-UI-085: the hit box is half the 205 art's size plus 5 px,
the last waypoint first, WM102; a press when the armed flag is clear does nothing, WM101); the drag that follows only scrolls the view (50 px, within 23 px of its edge, repeating
every 250 ms) and never moves the waypoint (WM99); the left release re-arms and snaps the selected waypoint onto a known area within 320 tiles (WM100). A right release (in the map view) or Delete
removes the selected waypoint, or the last (WM98); during a journey the last goes and the journey ends. The route is a straight line from the party through each waypoint's icon,
(160, 50, 50) and, for the segment that ends at the selected waypoint, (50, 50, 110) (WM80, WM104). Deleting a waypoint that is not the last is the original's flaw WM97: its
steps stay in the route, so the journey still walks them (rule switch `DeletedWaypointKeepsSteps`, Original); Steamwoven plans the later segments again. Travel starts
the journey (WM33); the view follows the party while it travels (PROJECT: not read, F0). Debug mode and a run without the game folder keep the diagnostic panel; there the window opens as before.

Done (ARC-UI-083, slice 2): the zoom and whole-map buttons (811 map view, 812 continent view) and Z switch the view kind, not during a journey (WM62; the
original's buttons then end the journey, WM63, not reproduced); each kind keeps its own offset. The continent view is `Map_Zoomed` (365 × 365) at window
(218, 11) in a black view with a 2 × 2 red dot for every marker (WM17's flaw, only the first eight showing, is fixed), the last discovered area as a 5 × 5
yellow square and the party as a 3 × 3 green one; a click in it zooms in on that point (WM34). The compass and the Travel button are hidden there. In both
views art 217 `Nav_Cvr` (207 × 35) covers the view's bottom edge at window (294, 341). In the map view a known area's marker carries its name in art 840
`NewTimes16Font`: the colour of `WorldMap.mes` 655 (254, 248, 236) over a (10, 10, 10) shadow, wrapped at 200 px, at most the lines that fit in 50 px, from
the marker's pixel plus the area's `gamearea.mes` name offsets plus (12, -10) (WM45–WM49). The two black boxes of the frame show the sector under the
pointer, `%d W` and `%d S`, white with the shadow, at window (707, 159) and (707, 191) (WM51, WM52). A marker under the pointer (the newest of the overlapping
ones, ±9 picture pixels in the map view and ±20 in the continent view) writes the area's description into the message window at once, without storing it,
and clears it on leaving (WM58–WM60); the buttons' hints ("Action", a new line and message 501, 502, 510 or 512 of `WorldMap.mes`) show only when 3 s have
passed since the last stored message and are dropped otherwise (WM61).

Done (ARC-UI-084, slice 3): the town map, kind 2 (`AreaTownMapLayout`, `AreaTownMapSource`, `AreaWorldMapArtView`). A PC whose sector has a town map (the
sector's metadata word, WM65; 0 = none) is not on the walkable world, and the window opens on it on any map (WM5, WM6); a list entry `rules/townmap.mes`
names the town, cut at `[w:` (WM66), and `townmap/<name>/<name>.tmi` (WM69; version 2, 48 bytes: the picture's isometric size, the tile columns and rows, the
origin location and the scale 0.25) with `<name>NNNNNN.bmp` tiles (64 × 32, 8 bpp, index = row × columns + column; a missing file stands in as tile 000000, WM74)
make its picture, from 1280 × 640 up to several thousand pixels, scrolled like the map view by the arrow keys and the compass, centred on the party pixel computed at
the open (WM70, WM72; Home and a compass release re-centre). The tiles are read as they are first drawn and the textures kept (the pictures run to 19 600 tiles).
Fog (WM77): a town revealed whole (`[w:1]` in the list, e.g. the crash site and the towns) shows every tile; in another, a tile shows when its bit is set in
the town's reveal state and its file exists, an unrevealed tile with at least one revealed neighbour among its eight fades into black (the tile blended with
white on the corners beside revealed neighbours, black on the others), and any other tile is black; the party marker 204 is drawn on top, and `Nav_Cvr` last.
The zoom button (811) and Z reach it from the other views in a town-map sector (WM22, WM32), Z cycles map or town, continent, town; a click in the continent view
zooms in only outside a town-map sector (WM34); the readouts, the labels and the marker hover are absent there (WM52, WM59). The Travel button is shown (WM62)
and does nothing yet. Done (ARC-WORLD-122): the tile under the PC is revealed in the session whenever the PC's cell changes in a sector with a town map (WM78: one tile, no radius,
index (y × rows / height) × columns + x × columns / width of the full pixel; `GameSessionOwner.RevealTownMapCell`, schema 75), and the window's fog reads those
bits, so a town that is not whole opens as the player walks it. Done since (ARC-UI-092): the town waypoints (WM85–WM88, WM105, WM110–WM125): an armed press on a waypoint selects it, else adds one at the clicked pixel's tile (`AreaTownMapLayout.ToLocation`, WM110) if the PC's own walk plan finds a path from the PC (first) or the previous waypoint, at most 6 (message 611) and not farther than 31 tiles on an axis or on the start tile (message 610); there is no drag; Delete and the right release remove the selected waypoint or the last; the route is drawn as in the map view; Travel closes the window and walks the PC to the first waypoint as a ground click does, the others stacked as goal-3 walks in the same run of the PC's goal table (ARC-DEV-311, ARC-DEV-322: the original's one-run route in both rule modes, no switch). Done since (ARC-UI-093): the PC's followers drawn on the town map at their current pixels with the party art (WM81; one outside the picture is left out, a corner glitch not reproduced). Done since (ARC-UI-103): the notes of a town map (WM79, WM127, WM128): hovering a scenery with the town-map flag (bit 0x8 of its scenery flags) in the PC's town adds a question-art note with the object's name at its pixel unless one lies within 9 px (at most 199), the notes are drawn under the route with art 143 and a hovered note shows its text in the message window (`AreaTownNotes`); they are kept in the session and saved with it (`story.townNotes`, schema 81, ARC-UI-104; a damaged list is refused by the strict decode, which also fixes WM91's unchecked text length). ARC-UI-106 (Steamwoven rules; the quill is unreachable in the retail executable, WM126, so the control is the project's): `Q` enters or leaves a note quill in the town map (cursor art 21, the original's quill cursor, WM30; the marker hover is off and the waypoint selection cleared meanwhile, WM59, WM103); a left press on a note opens its text, elsewhere starts a new question note at the pixel, in a one-line entry over the view's bottom edge (the original uses the message window, WM92); Enter keeps the text (white space alone counts as empty), an empty text deletes the edited note, Escape cancels the entry and opens no menu, the right button, Delete, the Travel button, a kind switch and closing the window leave the quill (WM98, WM89, WM103); at 199 notes a press on empty ground adds none; the list goes to the session at once (schema 81, no new field). ARC-UI-110 (Steamwoven rules): the same quill works in the map view and the continent view (not while a journey runs); each view keeps its own notes in the session (`story.mapNotes`, schema 82), at pixels of the world-map picture, drawn with art 143 and read on hover where no area marker lies under the pointer (hit boxes ±9 px in the map view, in the continent view at least ±20 px and the drawn art's half size); a press in the quill state adds no waypoint, starts no drag and does not zoom in from the continent view; entering clears the waypoint selection and keeps the waypoints (WM132).
Without the art, in debug mode or when the town's files do not read, the window keeps the map view and the earlier rules (the walkable gate is the map alone).
A town map with a tile 000000 that does not decode is refused (WM73's fatal path is dead in the original and unreachable here).

Split: (a) done, the window, the picture, the scroll, the markers (ARC-UI-073); (b) done, the names and hover texts of the areas, the sector readout boxes, the
continent view and the zoom and whole buttons (ARC-UI-083); (c) done, the town map window (ARC-UI-084) and the walked cells (ARC-WORLD-122); (d) done, the map view's waypoints: selecting, the drag, the release snap, Delete and the right release removing one (ARC-UI-085); (e) done, town waypoints and walking (ARC-UI-092); (f) town-map notes and the note quill (ARC-UI-093); the right release in the continent view is not read (WM98, F0).

## Schematic window (done: ARC-UI-142)

Entry: open 0x56D130 (`T`, the interface button, the dialog's `su`, script action 129; ledger [SW1–SW8](../research/ledger/technology.md#schematic-window-arc-technology-005)).
Read in the build 0x56D310, the page draw 0x56DDC0, the slot draw 0x56E190, the handler 0x56D530 and the tables 0x5CA820..0x5CA97C (rows SW9–SW14). The
window is 800 × 400 on art 365 at the screen's (0, 41): the page buttons (arts 372, 373, gone on the first and last page), the assemble button (art 357,
its face 358 to 360 by state) and the two list tabs (art 6), the eight discipline tabs as one radio group (arts 355, 356, 361..364, 368, 369), the record's
art, name (font 371), glyph (font 370) and description (font 229), two component slots with the found or missing overlay (831, 830), the item's picture
and the required expertise (fonts 300 and 229), and the round view's lens (89 × 89 at (50, 26), art 231) whose release closes the window. The viewer
([layout](../../src/Arcanum.World/AreaSchematicWindowLayout.cs), [view](../../tools/terrain-viewer/Godot/AreaSchematicWindowView.cs)) draws it when the arts read
and keeps the diagnostic panel as the fallback; every action, sound and key of the diagnostic window is unchanged.

## Reading window (done: ARC-UI-1877)

Entry: the reading start 0x56BB60 and open 0x56BC00 (an item's use, script actions 64, 65 and 114; ledger [WR1–WR16](../research/ledger/ui-audio.md#reading-window-arc-ui-049)).
Read in the create 0x56BCC0, the redraw 0x56C050, the callback 0x56BFC0, the text draws 0x56C630, 0x56C750, 0x56C800 and 0x535570 and the tables 0x5CA478..0x5CA777 (rows
WR17–WR23). The window is 800 × 399 at the screen's (0, 41): the frame art 494 at (0, 0), the background art of the kind (477 book, 489 note, 487 newspaper, 491 telegram, 633 plaque,
817 for the alternate newspaper prototype, a picture's own art) at (132, 0), the round view's lens (art 231, 89 × 89 at (25, 24)) whose left release closes the window, a book's two page
buttons (arts 495 and 496 at (213, 77) and (675, 77)) and the text in the original's fonts: a book's two pages of 200 × 290 at (227, 57) and (476, 57) paged by pixel height, a note in
(349, 49, 215 × 300), a telegram's eleven form lines and body, a newspaper's masthead, headline and four columns, a plaque (white text) in (269, 40, 428 × 327). The viewer
([layout and text flow](../../tools/terrain-viewer/Core/AreaReadingArt.cs), [view](../../tools/terrain-viewer/Godot/AreaReadingArtView.cs)) draws it when the arts and fonts read and keeps the
diagnostic panel as the fallback; every action, sound and key of the diagnostic window is unchanged.

## Main menu (done: ARC-UI-058)

The owner's bug report of 2026-10-29 (build `6e793c18`): the main menu and its windows (options, save and load, character creation)
drew the project's panel, "PROJECT window over the original's menu windows". Read in the window table 0x5C3A40, the window build
0x546340, the label draw 0x546B40 and the menu's fonts; rows [MW1–MW6](../research/ledger/ui-audio.md#menu-window-art-arc-ui-058).
The menu's windows are the 800 × 600 screen, not the 800 × 400 window of the game windows above.

| Part | Art | Place on the screen | Notes |
| --- | --- | --- | --- |
| Background | 329 `MainMenuBack` 800 × 600 (windows 2, 3, 4, 11, 26), 331 `SinglePlayerBack` (window 5) | (0, 0) | an art 600 rows high fills the screen (MW1) |
| Labels | font 327 `Morph30Font` (225 glyphs, 33 rows) | i-th centred on (410, 143 + 50 × i) | text-only buttons, hit area = the label's size (MW2, MW4) |
| Label colours | black, brown (97, 61, 42), face dark red (100, 0, 0) | the passes at (−1, −1), (+1, +1), (0, 0) | the face bright red (240, 15, 15) under the pointer (MW3, MW5, MW6) |

Done in the viewer (`AreaMenuArtLayout`, `AreaArtFont`, `AreaMenuArtView`): with the game's art the main, single-player, new-game and
in-play windows show the background over the whole screen at the bars' whole factor (black around it) and their labels in the art
font with the three passes; the pointer over a label lights it and plays 3026, a press plays 3027 and a release over the label
chooses it; the letters, Escape and the controller work as before (the labels show no letters, as the original's; the focused label is lit at
the window's opening and after a focus move, PROJECT). PROJECT: the
buttons Steamwoven hides (MultiPlayer, Credits, View Intro, a refused new game's) leave no gap, and a line at the top of the
window names them and the window's notice (for example "no save to continue from"). The editor window and every window below it keep the
diagnostic panel (the quit question is the original's dialog box since ARC-DEV-334, item 6); the synthetic self-tests, which have no game folder, keep it everywhere. `AreaArtFont` is the first art font of the
project: the next slices draw their texts with it.

## Menu windows plan (ARC-UI-058)

The remaining menu windows, each its own task and thread, in this order (read in the window table and the build; MW1). Every window
keeps its existing behaviour and its diagnostic look for the self-tests, as above; coordinates are the screen's; facts not in MW1–MW6
are F0 and are read by the slice.

1. **Options** (window 6, `O` in play): background 556 `OptionsMenuBack` (800 × 600, full screen); its four page buttons (Game, Video,
   Sound, Done; messages 4000–4003) are text buttons with flags 0xD like the main menu's, but at x 130, y 221 + 40 × i. The page's
   controls (sliders, value arrows, the checkbox look) come from the options window's own open 0x541C70 and close 0x541D70 and its
   control table (OP1–OP8, [options window](../research/ledger/ui-audio.md#options-window-arc-ui-013)); the slice reads their art and
   places the existing controls. Done by ARC-UI-059: the background and the page buttons (`AreaMenuArtView`, MW7) and the slot
   points, x 261 / 531 and y 122 + 75 × row (MW8). Done by ARC-UI-061: each control in its own art at its slot (control build
   0x57FAD0, MW9–MW10): background 688 (slider) or 671 (list), the label in `morph15font`, the arrows 667 and 668 as push buttons,
   a slider's bar 623, a list's value in `Flare12Font`; the shown controls take the first slots. The window is done.
2. **Save and load** (windows 8 save, 7 load; the palette's and F-keys' windows too): background 745 `SaveLoadBackground` (800 × 400)
   at (0, 41) with the strips 336 at the top and 335 at the bottom (MW1); art buttons 321 `NextBut` (61 × 61, 3 frames) at (675, 55)
   (Save / Load, hint 7021 / 7020), 323 `BackBut` at (69, 55) (hint 7002) and 32 `cncl_big` (38 × 37) at (115, 396) (hint 7022).
   The list, the slot thumbnails and the description field come from the opens 0x543380 (save) and 0x541F20 (load), their redraws
   and the save info 0x404270 (SL1–SL8, [save list](../research/ledger/ui-audio.md#save-list-arc-ui-010)). May split: (a) frame,
   buttons and the list's text; (b) the save's thumbnail. Done by ARC-UI-062 (a), ledger MW11–MW16: the strips and the background,
   Save / Load and Previous Screen on the bottom strip at the screen's (675, 496) and (69, 496) (table flag 1: y from the strip's
   top), Delete at (115, 396), with their frames and sounds (`AreaSaveListArtView`); the list's eleven rows at (42, 161 + 20 × k),
   141 wide, in art 27 `morph15font`, near-white and the selected row gold, a wide description ending in "...", the new-save row "New"
   (5055) or the typed description, the scroll top following Up/Down. PROJECT: the stored preview is drawn into the preview hole
   (281, 96, 468 × 300), the details, notices and the Yes/No question stand in the bottom strip's middle box, and the wheel scrolls
   the list. Slice (b) done by ARC-UI-067, ledger MW22–MW25: the save's picture under the cover 748 `SaveShotCover`, the info texts and
   the save's portrait over it (details schema 2), the toggle 747 at (499, 82) and the lens's art 746 at the screen's (84, 51). Left:
   what the lens shows (0x4B8CE0 with the hero's location and 0x550DA0, not a portrait), the scroll bar's art (0x5804E0) and the
   buttons' hints (7002, 7020–7022, 7025; where the menu shows hints is not read).
3. **Pre-made character browser** (window 13): background 766 `PreGenCharacterBase` (800 × 400) at (0, 41) with the strips; buttons
   321 at (675, 55) (start, hint 7003), 323 at (69, 55) (back), 283 `Big_Grn_L` / 284 `Big_Grn_R` (34 × 33) at (43, 118) and
   (244, 118) (previous, next). The hero's portrait, name and stats are drawn by the window's own callbacks (descriptor +0x54, +0x58);
   the slice reads them and draws the browsed hero with the character sheet's parts where they match. Done by ARC-UI-066, ledger
   MW17–MW21, MW26 (`AreaPremadeBrowserArtView`): the strips, the background and the four art buttons with their frames and sounds; the
   hero's large portrait at (95, 69) (128 × 128); its player name in `Cloister18Font` red at (46, 224, 228 × 19); its gender, race and
   background name in `morph15font` red, centred in their fields at y 284, 334 and 384; its story (`gameback.mes` 1039..1050, the
   background description field 0x102) in the field (350, 196, 395 × 212); "Initial Stats" and fifteen stats "ST: 8" (`stat.mes`
   500 + stat, the start's effective values) in four columns from (346, 87); each field filled black first. The story's lines are
   the original's (MW26): every installed story is longer than its field (14–18 lines) and, as in the original, its first 11 show
   (an anomaly for the owner: a "Steamwoven" setting could show the rest). PROJECT: the hints are not shown. A hero that does not
   read keeps the diagnostic window, and a portrait that does not read leaves its field black. The window is done but for these
   points.
4. **New character** (window 12): background 765 `CreateCharacterBase` (800 × 400) at (0, 41) with the strips; buttons 321 / 323 as in
   the browser, 283 / 284 at (43, 118) / (244, 118), 757 `MPCycleLeftButton` / 758 `MPCycleRightButton` (23 × 23) at (42, 331),
   (255, 332), (42, 381), (255, 382) (the gender, race, background and portrait arrows, F0 which is which), the name field and the
   portrait from the window's callbacks (NW1–NW6). Done by ARC-UI-068, ledger MW27–MW29 (`AreaNewCharacterArtView`): the strips, the
   background and ten art buttons from the table 0x5C4C48 (the portrait's 283 / 284; the race's 757 / 758 at (42, 331) / (255, 332),
   the background's at (42, 381) / (255, 382) and the gender's at (42, 281) / (255, 282)), the character draw shared with the browser
   (its stats under the chosen background's effect too), the pointer's gender and race bands choosing the description field's text
   (hover flag 0x64C458), and the name field: an art-less button at (45, 224, 230 × 20) and the typed name with a closing "|"
   (0x544100). PROJECT: the hints are not shown; the controller's focus row lights its buttons until the pointer moves. The window is done but for these
   points.
5. **Character editor and opening shop** (windows 14 and 15): no background of their own (-1, MW1: the strips only); buttons 321 at
   (675, 55) and 323 at (69, 55) (back, target -2). The editor is the creation-mode character sheet over 22 `char_maint`: done with
   the character sheet's slice (above) rather than here; the shop is the barter window's look (ARC-UI-038). Shop done by ARC-UI-069,
   ledger MW30 (`AreaShopArtView`): the strips and the two art buttons Accept and Back in the bottom strip (table 0x5C5408, flag 1,
   the screen's (675, 496) and (69, 496)), the barter window's original look docked at the screen's (0, 41) on the shop's session,
   without the round view (the main menu is up, IW33, IW34). PROJECT: the hints are not shown; the controller's focus row lights its
   button until the pointer moves. Editor done by ARC-UI-147 (window 14, the same strips and buttons, table 0x5C5310; `AreaShopArtView`
   with the character sheet's original look between them, `AreaCharacterSheetArtView.MenuScreen`): the sheet's window at the menu screen's
   (0, 41), the draft's values and portrait, the three pages' `−` and `+` and the tabs by the pointer; the portrait's close rectangle does not
   close it (modes 1 and 2 only, CM3). PROJECT: the hints are not shown and a refused press's `charedit.mes` text goes to the menu's note line.
6. **Quit box** (in play, Quit): the yes/no box of 0x5416A0 with message 5100 (SF3) in the game's common dialog box, read by
   ARC-WORLD-120 (DB1–DB4: `DialogueBox` 325 × 136 at (237, 232), `Flare12Font` text, `Lil_Accept` and `Lil_Cancel`) and built
   there as `AreaDialogBoxView` for the death load box. ARC-DEV-334: the in-play menu's quit question draws on the same view over the
   window it was asked in (PROJECT placement, the original's view behind the box is not read; the 800 × 600 plate of the menu's art, a catcher under it keeps the clicks off the window; Return, Y, N and
   Escape stay the app's keys, a release over a button answers); without the game's art the diagnostic panel keeps the question. The
   main menu's Exit Game exits at once in the original (MM2, MM6), so it asks nothing. Left: the other yes/no questions on that view.

Not planned here: Credits (25), View Intro (10) and the network windows (16–24), which Steamwoven does not build (FR-MEDIA-001,
FR-MEDIA-002, network play).


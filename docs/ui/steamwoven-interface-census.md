# Census of the Steamwoven interface: windows, art, kit kinds, reuse and backdrop families (ARC-RES-2451)

Status: **census, 2026-11-16** (work item ARC-RES-2451, [#2451](https://github.com/Simarglok/arcanum-steamwoven/issues/2451),
epic [#2446](https://github.com/Simarglok/arcanum-steamwoven/issues/2446)). It supplies the facts that the
[TRD](steamwoven-interface-trd.md) leaves to the census: the families of [SWI-BDR-4](steamwoven-interface-trd.md#menu-backdrops), the
parts for the kit manifest (SWI-KIT-1), the text that fonts must draw (SWI-LAY-15) and the production batches. Requirements touched
(status unchanged): [FR-UI-002](../requirements/ui-media-lifecycle.md#fr-ui-002), [FR-UI-004](../requirements/ui-media-lifecycle.md#fr-ui-004),
[FR-UI-007](../requirements/ui-media-lifecycle.md#fr-ui-007).

The machine-readable table of every interface art, with its kit kind, batch, windows, reuse count and baked-text finding, is
[`steamwoven-interface-census-arts.tsv`](steamwoven-interface-census-arts.tsv) (identifiers, sizes and verdicts only: no pixels,
no frames). Screenshots stayed local ([below](#screenshots)).

## Method and sources

- **Windows**: the window table A of [ARC-RES-093](../research/screen-size-and-ui-layout.md#a-window-layout-census) and
  [original windows](original-windows.md) (the retail 1.0.7.4 `Arcanum.exe`, read by earlier tasks), 53 rows in three groups: `H` the
  play screen and its overlays, `G` the game windows, `M` the menus.
- **Art**: the player's installed `art\interface` (815 ART files, 824 rows: 799 numbered messages of `interface.mes` that have a file, and 25 files with no message; frame counts and sizes read
  with the project's ART reader, metadata only; 25 files have no message and are named by file). Which window draws which art comes from the ledger
  rows and the viewer's layout classes that already draw the Original look (cited per row in the local working files, not repeated here).
- **Baked text**: every non-font art was looked at on contact sheets (decoded locally with the project's ART decoder, 2026-11-16).
- **Kit kind**: one of the [asset kit](steamwoven-interface-trd.md#asset-kit) kinds, assigned by role, name and size; judgement, to be
  confirmed when the manifest is built.
- Nothing here changes behaviour; the Original look is untouched.

## Summary

| Measure | Count |
| --- | ---: |
| Windows and HUD states (rows below) | 53 |
| Interface arts (files) | 815 |
| Rows of the TSV (799 numbered messages with a file, some of them naming one file twice, plus 25 files with no message) | 824 |
| Numbered arts in `interface.mes` without a file under `art\interface` (portraits, world map tiles, masks) | 47 |
| Bitmap font arts (not kit parts, drawn text: [#2462](https://github.com/Simarglok/arcanum-steamwoven/issues/2462)) | 33 |
| Arts with a window row (drawn by a window) | 497 |
| Arts drawn as members of a family (schematic record pictures, written-object pictures, hotkey slots) | 155 |
| Arts with no window row, kind proposed by name and size | 107 |
| Network play arts (out of scope) | 24 |
| Unused extra tab arts | 8 |
| Arts assigned a kit kind (the rows above except fonts and network play) | 767 |


## Windows

Anchor and family follow [ARC-RES-093 table A](../research/screen-size-and-ui-layout.md#a-window-layout-census); `arts` counts the distinct non-font arts the window draws (shared arts count in each window); `shots` is the number of local Original-look screenshots. `BOARD` is the shared panel board; `-` means the window has no backdrop of its own (it sits on the world, on the board or on its parent).

| Id | Window | Anchor | Family | Backdrop or board art | Arts | Shots |
| --- | --- | --- | --- | --- | ---: | ---: |
| H01 | Top strip (185 IntTop) and its buttons | top strip 800x41 | board | 185 IntTop | 32 | 6 |
| H02 | Bottom strip (184 IntBotom), its buttons and panel frames | bottom strip 800x159 | board | 184 IntBotom | 40 | 5 |
| H03 | Vials, fatigue and health gauges (17-20) | bottom strip | board | - | 4 | 6 |
| H04 | Message window and hovered-object panel | bottom strip | board | - | 26 | 2 |
| H05 | Quantity window (bar mode) | bottom strip (replaces message window) | board | - | 6 | 3 |
| H06 | Combat lamp panel and combat UI (end turn, bars) | bottom strip / over the view | board | - | 5 | 5 |
| H07 | Follower portraits and frames (503-505) | play view, left edge | - | - | 10 | 6 |
| H08 | Follower command window | play view, left edge (runtime table) | - | - | 1 | 5 |
| H09 | Skills, spells and weapon panes in the bottom strip | bottom strip (replaces message window) | board | - | 117 | 13 |
| H10 | Load indicator and other play-view overlays (601) | play view, top right | - | - | 1 | 0 |
| H11 | Floaters | follows the object (text only) | - | - | 0 | 6 |
| H12 | Text bubbles | follows the object (text only) | - | - | 0 | 5 |
| H13 | Cursors and hovered-object highlight (467, 468) | follows the cursor / object | - | - | 12 | 2 |
| H14 | Hidden-interface compact UI (-fullscreen) | hidden-interface mode (-fullscreen only) | - | - | 0 | 0 |
| H15 | Fate window (292) | pop-up, top left of the play view | - | - | 2 | 3 |
| H16 | Sleep window (565) | pop-up, top right of the play view | - | - | 2 | 4 |
| H17 | Dialog (talk) window and option area | play view (darkened view, text only) | - | - | 0 | 3 |
| H18 | Dialog box yes/no (822-824) | centred pop-up | - | - | 3 | 1 |
| H19 | Message log | bottom strip (message window) | board | - | 8 | 5 |
| H20 | Death window / death cover | full screen bitmap | - | - | 3 | 1 |
| H21 | Hover (object) window | follows the object | - | - | 95 | 7 |
| H22 | Round view / lens (231-233, 198) | lens inside a window | - | - | 4 | 1 |
| G01 | Inventory (own, mode 0) | play-view panel 800x400, two parts | BD-A | 221 Inventor, 223 PDoll | 19 | 9 |
| G02 | Loot window and body's doll (mode 2) | play-view panel 800x400, two parts | BD-A | 221 Inventor, 234 Loot, 341 PD_Cover4Inven, 349 Loot_PD | 52 | 4 |
| G03 | Barter window and merchant's doll (mode 1) | play-view panel 800x400, two parts | BD-A | 221 Inventor, 235 Barter, 337 Barter_PD, 341 PD_Cover4Inven, 825 Barter_Follower | 31 | 6 |
| G04 | Steal window (mode 3) and See Contents (mode 4) | play-view panel 800x400, two parts | BD-A | 221 Inventor, 234 Loot, 341 PD_Cover4Inven, 349 Loot_PD | 25 | 1 |
| G05 | Character sheet, main page (22) | play-view panel 800x400 | BD-B | 22 char_maint | 24 | 2 |
| G06 | Character sheet: skills page (29) | panel 235x325 over G05 | BD-B | - | 8 | 1 |
| G07 | Character sheet: tech page (30) | panel 235x325 over G05 | BD-B | - | 19 | 1 |
| G08 | Character sheet: spells page (31) | panel 235x325 over G05 | BD-B | - | 19 | 1 |
| G09 | Character sheet: auto-level schemes page | panel 235x325 over G05 | BD-B | - | 7 | 1 |
| G10 | NPC character sheet of the dialog (ce) | play-view panel 800x400 | BD-B | 22 char_maint | 10 | 0 |
| G11 | Logbook (seven tabs) | play-view panel 800x400, two parts | BD-C | 260 Book_Main, 264 LogBooks_Side | 13 | 9 |
| G12 | World map (map view) | play-view panel 800x400 with a hole | BD-E | 138 mapmain | 16 | 5 |
| G13 | World map continent view | play-view panel 800x400 with a hole | BD-E | 138 mapmain | 6 | 1 |
| G14 | Town map | play-view panel 800x400 with a hole | BD-E | 138 mapmain | 11 | 2 |
| G15 | Schematic window | play-view panel 800x400 | BD-F | 365 Schematic_Base | 153 | 3 |
| G16 | Reading window (book, note, newspaper, telegram, plaque, picture) | play-view panel 800x399, two parts | BD-D | 494 WritenSideBar | 22 | 10 |
| G17 | Identify and repair windows (modes 5, 6) and stat hot spots | play-view panel 800x400, two parts | BD-A | 221 Inventor, 223 PDoll | 15 | 8 |
| M01 | Main menu (329 MainMenuBack) | full screen 800x600 | BD-H | 329 MainMenuBack | 1 | 2 |
| M02 | Single-player menu (331) | full screen 800x600 | BD-H | 331 SinglePlayerBack | 1 | 0 |
| M03 | New game menu | full screen 800x600 | BD-H | 329 MainMenuBack | 1 | 0 |
| M04 | Options (556), game / video / sound pages | full screen 800x600 | BD-H | 556 OptionsMenuBack, 669 OptionsBase | 8 | 4 |
| M05 | Save window (745) | two strips + 800x400 | BD-G | 335 CharCreateBottomBar, 336 CharCreateTopBar, 745 SaveLoadBackground | 9 | 7 |
| M06 | Load window (745) | two strips + 800x400 | BD-G | 335 CharCreateBottomBar, 336 CharCreateTopBar, 745 SaveLoadBackground | 9 | 8 |
| M07 | Pre-made character browser (766) | two strips + 800x400 | BD-G | 335 CharCreateBottomBar, 336 CharCreateTopBar, 766 PreGenCharacterBase | 7 | 1 |
| M08 | New character (765) | two strips + 800x400 | BD-G | 335 CharCreateBottomBar, 336 CharCreateTopBar, 765 CreateCharacterBase | 9 | 1 |
| M09 | Character editor (creation mode sheet) | two strips + character sheet | BD-B | 22 char_maint, 335 CharCreateBottomBar, 336 CharCreateTopBar | 36 | 3 |
| M10 | Opening shop | two strips + barter window | BD-A | 221 Inventor, 235 Barter, 335 CharCreateBottomBar, 336 CharCreateTopBar, 337 Barter_PD, 341 PD_Cover4Inven, 825 Barter_Follower | 18 | 1 |
| M11 | Quit box and other yes/no boxes | centred pop-up over the window | - | 329 MainMenuBack | 4 | 0 |
| M12 | Slide show, ending and movie screens | full screen bitmaps | - | - | 0 | 8 |
| M13 | Credits and View Intro (not built) | full screen (not built) | - | 2 Black | 1 | 0 |
| M14 | Network lobby windows (out of scope) | full screen (out of scope) | - | 330 MultiPlayerBack | 1 | 0 |

## Kit kinds

Every art that a window draws, or that the family rows below name, is assigned one kind of the TRD asset kit. Counts (all assigned arts, including those without a window row):

| Kind | Arts | What the original arts of this kind are |
| --- | ---: | --- |
| board | 4 | the four strips (top and bottom of the HUD, top and bottom of the two-strip menus) |
| backdrop | 28 | window and menu backgrounds, doll and cover layers of the inventory family, pop-up bases that are full panels |
| sheet | 28 | reading-window papers, book pages, the iron right panels of the character sheet, script-given pictures (maps, murals) |
| hardware | 69 | slot and socket housings, lens rings, pop-up and pane frames, option control plates, covers |
| plate | 157 | button bodies with their frames (idle, hover or pressed, disabled): the kit splits each into plate, ring, glyph and states (SWI-KIT-5) |
| glyph | 17 | map markers and the silhouettes that cover empty worn slots |
| state | 12 | lit, on and refused overlays of buttons and boxes |
| gauge | 56 | vials, bars, the clock, pips, scroll thumbs, counters |
| picture | 384 | icons and pictures on transparent backgrounds: items, spells, skills, critters, status, schematic records, hover rings |
| cursor | 12 | the cursor set |

No original art is assigned the kinds `material`, `grid` or `decor`: those are new parts the kit makes (the material sheet of [#2456](https://github.com/Simarglok/arcanum-steamwoven/issues/2456); the schematic grid is painted into art 365 today and the inventory grids into the pack arts, so each becomes its own `grid` layer when the backdrop is redrawn; the corner and rivet decor has no original art).
Fonts are listed with kind `font` and belong to [#2462](https://github.com/Simarglok/arcanum-steamwoven/issues/2462), not to the kit.


## Reuse groups

One art drawn in two or more windows is one kit part at one master size ([SWI-KIT-3](steamwoven-interface-trd.md#asset-kit)).
Groups below are arts with the same set of windows; 146 arts are shared, in 43 groups, largest first. Single art families that a
window row cannot list (item inventory art, portraits) are one family per window and are not shared in this sense.

| Windows | Arts | Examples |
| --- | ---: | --- |
| G08, H09 | 15 | 38 s_air, 44 s_divination, 50 s_fire, 56 s_forc … |
| G02, G05, H21 | 13 | 375 RaceHumanIcon, 376 RaceElfIcon, 377 RaceHElfIcon, 378 RaceDwarfIcon … |
| G02, H21 | 12 | 435 ContainerIcon, 436 Door_Icon, 437 LevelUpIcon, 786 IconWindow … |
| G01, G02, G03, G04, G17 | 11 | 242 CVR_Boot, 243 CVR_Gauntlet, 244 CVR_Armor, 245 CVR_Medalion … |
| G06, M09 | 7 | 29 Skills_Window, 302 Combat_But, 303 Thieving_But, 304 Social_But … |
| H04, H19 | 7 | 354 DialogueWindow, 438 LevelUpIcon, 439 Poisoned_Icon, 441 BlessIcon … |
| G09, M09 | 6 | 238 ScrllSlideT, 240 ScrllSlideB, 317 ScrllUP, 318 ScrllDWN … |
| G12, G13, G14 | 5 | 138 mapmain, 143 mm_ques, 217 Nav_Cvr, 811 MapZoomBut … |
| H02, H09 | 5 | 278 SKL_Repair, 279 SKL_Conceal, 280 SKL_PickPocket, 282 SKL_Traps … |
| G03, M10 | 4 | 235 Barter, 337 Barter_PD, 345 Gamble_Box_OFF, 825 Barter_Follower |
| G05, G10, M09 | 4 | 22 char_maint, 34 char_Common_Skills, 254 align, 255 MT_Apt |
| G12, G14 | 4 | 139 navbutton, 204 MM_Cross, 206 MM_WayP, 813 MapTravelBut |
| M05, M06 | 4 | 745 SaveLoadBackground, 746 SaveLoadPCLens, 747 TextToggle, 748 SaveShotCover |
| M05, M06, M07, M08, M09, M10 | 4 | 321 NextBut, 323 BackBut, 335 CharCreateBottomBar, 336 CharCreateTopBar |
| G02, G03, G04, M10 | 3 | 338 PD_lil_butt, 339 Inven_lil_butt, 341 PD_Cover4Inven |
| G07, M09 | 3 | 30 Disciplines_Window, 306 Anatomical_But, 649 TTI_Chem |
| H18, H20, M11 | 3 | 822 DialogueBox, 823 Lil_Accept, 824 Lil_Cancel |
| G01, G02, G03, G04 | 2 | 225 Drop_ON, 343 Use_Box_ON |
| G01, G02, G03, G04, G17, M10 | 2 | 221 Inventor, 241 CVR_Helmet |
| G01, G02, G03, G04, M10 | 2 | 224 Drop_OFF, 342 Use_Box_OFF |
| G02, G03, G04 | 2 | 347 Use_Box_ILL, 348 Drop_ILL |
| G02, G04 | 2 | 234 Loot, 349 Loot_PD |
| G03, M08, M10 | 2 | 757 MPCycleLeftButton, 758 MPCycleRightButton |
| G05, G10, H09, M09 | 2 | 35 char_Tech_Skills, 36 char_Spells_Skills |
| G05, M09 | 2 | 23 char_minus, 24 char_plus |
| G07, G08, M09 | 2 | 647 SpellTech_Add, 648 SpellTech_Minus |
| M07, M08 | 2 | 283 Big_Grn_L, 284 Big_Grn_R |
| G01, G17 | 1 | 223 PDoll |
| G01, G17, H22 | 1 | 233 Lns_Papr |
| G02, G04, H22 | 1 | 232 Lns_Loot |
| G02, G05, H21, M09 | 1 | 383 MonsterIcon |
| G03, G15, G16, H22, M10 | 1 | 231 Lns_Bart |
| G05, G09, G10, M09 | 1 | 568 char_Schem_Skills |
| G05, G10, G11, G12, G13, G14, H22 | 1 | 198 PCWinCvr |
| G06, M04, M09 | 1 | 623 SkilGauge |
| G08, H09, M09 | 1 | 11 s_conveyance |
| G08, M09 | 1 | 31 char_spells |
| G12, G14, H13 | 1 | 21 spellcur |
| H04, H19, H21 | 1 | 440 HexedIcon |
| H05, M05, M06 | 1 | 32 cncl_big |
| H09, M09 | 1 | 12 s_conveyance1 |
| H15, H16 | 1 | 293 FateBut |
| M01, M03, M11 | 1 | 329 MainMenuBack |

Notes: several original numbers are one file (432, 437, 438, 444 and 445 are all `LevelUpIcon`; `done_big` is 33 and 203, `cncl_big` is 32 and 202; nine rows in all repeat a file): the manifest keys parts by file, with the numbers as aliases. The shared lens covers (198, 231, 232, 233) and the slot covers (241 to 249) are drawn over the game view or over the doll, so they stay separate layers (SWI-LAY-12).


## Backdrop families

A family is a group of windows whose backgrounds share one material and so one backdrop master set ([SWI-BDR-4](steamwoven-interface-trd.md#menu-backdrops)).
Materials are read from the decoded arts (judgement, 2026-11-16); the material sheet ([#2455](https://github.com/Simarglok/arcanum-steamwoven/issues/2455),
[#2456](https://github.com/Simarglok/arcanum-steamwoven/issues/2456)) fixes the tones. The panel board is the same dark wood in the original strips
and in the backdrops, so one wood family underlies A, B (main page), C, D (side), E, G and the board; the families differ in what is laid on the wood.

| Family | Name | Windows | Material | Structure | Arts |
| --- | --- | --- | --- | --- | ---: |
| BD-A | Inventory family | G01, G02, G03, G04, G17, M10 (the opening shop shows the barter window) | dark streaked wood with gold trim and black grid wells; the left and right halves are different backgrounds (the right half is the same for every mode) | two-part: left 358 units (paper doll, loot, barter), right 442 units (pack); the doll and cover layers 337, 341, 349 lie over the grid areas | 8 |
| BD-B | Character sheet | G05, G06, G07, G08, G09, G10, M09 | main page 22: dark wood with brass; right panels 29, 30, 31, 567: pitted iron plate with brass rows | the main page is the backdrop; the 235 x 325 panels are sheets laid over it at (503, 63) | 7 |
| BD-C | Logbook | G11 | dark streaked wood side (264) beside an open book on a desk (260); page art 261 over the book | two-part: side 165 units, book 635 units | 6 |
| BD-D | Reading desk and written-object pictures | G16 | desk wood (finer grain, lighter than BD-A) under book, newspaper, note, telegram and plaque sheets; side bar 494 is the BD-A wood | two-part: side bar 132 units + a 668 x 400 desk-and-sheet background; the maps and murals are script-given pictures of the same size | 20 |
| BD-E | World map frame | G12, G13, G14 | wood and brass frame with a transparent hole for the map picture | one background; the map picture is a bitmap, not an art | 1 |
| BD-F | Schematic parchment | G15 | aged parchment with a printed grid, brass buttons at the left | one 800 x 400 background; the grid is painted in | 1 |
| BD-G | Two-strip menus | M05, M06 (745), M07 (766), M08 (765) | wood with brass trim, black fields | one 800 x 400 background between strips 336 and 335 (the board) | 5 |
| BD-H | Full-screen menus | M01, M02, M03 (329, 331 painted scene), M04 (556 options wood), M13, M14 (not built) | a painted still life (lamp, skull, pocket watch) and an options plate of wood | full-screen 800 x 600; no strips | 8 |
| BOARD | Panel board | H01, H02, H03, H04, H05, H06, H09, H19, M05-M10 | dark wood with brass | the four strips 184, 185, 335, 336; hardware over them is [B-HARDWARE](#production-batches) | 4 |

### Two-part windows and full-screen menus

- **Two-part** (a left and a right master with their own material; the split of [SWI-BDR-5](steamwoven-interface-trd.md#menu-backdrops)): the inventory family (left 358, right 442 units), the logbook (165 + 635), the reading window (132 + 668). The character sheet is one backdrop with a panel laid over it.
- **Full-screen menus** (800 x 600 original, a 3840 x 2400 master that covers the panels too, [SWI-BDR-6](steamwoven-interface-trd.md#menu-backdrops)): the main menu and new game menu (329), the single-player menu (331), options (556); credits and View Intro have only the black fill (art 2) and the movie screen is bitmaps (M12, no interface art).
- **Two-strip menus** (an 800 x 400 backdrop between the strips 336 and 335): save and load (745), the pre-made browser (766), new character (765); the character editor and opening shop reuse the strips and the character sheet or barter backdrop.
- **Windows without a backdrop of their own**: the pop-ups (fate 292, sleep 565, dialog box 822), the bottom-strip panes (frames 354, 274, 564, 291, 200, 298) and the lens rings. They sit on the world or the board; the kit draws them as hardware.


## Text baked into art

All non-font arts that a window draws (and the extra arts without a window row) were looked at in the decoded frames (2026-11-16); the first four frames of long animations only. Text that the executable draws over an art is not baked (titles, stat numbers, labels from `.mes` files, the main menu labels in `Morph30Font`, the newspaper masthead): fonts draw it ([#2462](https://github.com/Simarglok/arcanum-steamwoven/issues/2462)). The finding is `baked_text` in the TSV.

**Drawn by a window, text in the picture:**

| Art | Windows | Text in the picture | Consequence |
| --- | --- | --- | --- |
| 22 `char_maint` | G05, G10, M09 | the eight stat abbreviations (ST CN DX BE IN WP PE CH), the gauge caption `E&G`, the alignment scale digits (75 50 25 0 25 50 75) | redrawn without letters and digits; fonts draw them (SWI-LAY-15) from `stat.mes`, with the scale numbers as text; the earlier ledger row CHW1 also lists Health and Fatigue as baked: the art has heart and cross symbols there, no words |
| 138 `mapmain` | G12, G13, G14 | the compass letters N E S W | drawn by font on the redrawn frame |
| 566, 626, 627, 687, 749, 763, 764, 806 (written-object map pictures) | G16 | place names (regions, islands and towns of the world), numerals on 764 | these are the player's maps: kept as pictures in the original language or redrawn with text removed and labels drawn; decided in batch [BD-D](#production-batches) |
| 816 `BloodStone`, 788 `TullaMural` | G16 | in-world script, unreadable (816); rune-like decoration (788, unclear) | picture only |
| 429 `Note_Icon`, 430 `Telegram_Icon`, 462 `Schematic_Icon`, 586 `Pen_MSR` | H21 | unclear: line pattern or a tiny label at icon size | redrawn as pictures; no text to move |

**Not drawn by any window** (no kit part needed unless a window row is found): 132 `empty` ("Empty"), 133 to 136 `intcedit`, `intinven`, `intlog`, `intmap` (old interface button labels CeD, Inven, Log, Map), 196 `Cmps_Bse` and 197 `CompassN` (N E S W), 672 `wp_iso` (digits), `tempicon` ("TEMP"), `splash2` (the ARCANUM logo).

All other arts (the strips, the inventory family, buttons, tabs, icons, the schematic records and their title arts, every sheet except the map pictures) hold no letters or digits. Art 365 (schematic) paints pictures on its list buttons and printed lines to the component boxes, not words. The reading newspaper (487, 817) and telegram (491) arts hold rules and ornaments, not words.

## Screenshots

188 Original-look screenshots of 45 windows are in `local/steamwoven-ui/census/shots/` with the index `shots/index.tsv` (window, file, size, how captured, notes); nothing is committed. They were taken with the existing smoke captures (`./arcanum smoke pr --stages ... --env ARC_AREA_*_CAPTURE=...`, HD option off, `ARC_AREA_WINDOW_ART=legacy`), mostly 1280 x 800, bars and strips as crops (800 x 41, 800 x 159). The smoke's diagnostic text overlay and, in the stages that run without bars, a "Diagnostic view" strip are visible in some frames; the index notes it per row. Windows with no screenshot and why:

| Window | Why |
| --- | --- |
| H10 load indicator | no capture hook |
| H13 cursors | the framebuffer holds no cursors; only the hover ring is captured |
| H14 hidden-interface compact UI | no capture hook (`-fullscreen` only; text only) |
| G04 See Contents (mode 4) | no capture of mode 4; the steal window (mode 3) is captured |
| G10 NPC character sheet | no capture hook |
| M02 single-player menu, M03 new game menu | no capture hook (M01, M07, M08 are captured) |
| M11 quit box | no capture hook (the box art is in H18, H20) |
| M13 credits and View Intro | not built; credits need the slide table, View Intro plays only in the game build |
| M14 network lobby | out of scope |

The gaps are filed as [#2489](https://github.com/Simarglok/arcanum-steamwoven/issues/2489).


## Production batches

One issue per batch, created by this census ([FR-UI-007](../requirements/ui-media-lifecycle.md#fr-ui-007)); every issue is blocked by the fit tool [#2459](https://github.com/Simarglok/arcanum-steamwoven/issues/2459), the bake tool [#2460](https://github.com/Simarglok/arcanum-steamwoven/issues/2460) and the approval of the material sheet [#2456](https://github.com/Simarglok/arcanum-steamwoven/issues/2456); the board and backdrop batches also by the surface method [#2458](https://github.com/Simarglok/arcanum-steamwoven/issues/2458). The order follows SWI-KIT-7: board and backdrops first, then hardware, buttons and glyphs, sheets, the schematic and gauges, then icons, pictures and cursors. Each batch ends with the owner's look check in its thread. Arts per batch (kit kinds assigned in the TSV column `batch`):

| Batch | Issue | Covers | Arts |
| --- | --- | --- | ---: |
| B-BOARD | [#2472](https://github.com/Simarglok/arcanum-steamwoven/issues/2472) | panel board | 4 |
| BD-A | [#2473](https://github.com/Simarglok/arcanum-steamwoven/issues/2473) | inventory family backdrop | 8 |
| BD-B | [#2474](https://github.com/Simarglok/arcanum-steamwoven/issues/2474) | character sheet backdrop and right panels | 7 |
| BD-C | [#2475](https://github.com/Simarglok/arcanum-steamwoven/issues/2475) | logbook backdrop | 6 |
| BD-D | [#2476](https://github.com/Simarglok/arcanum-steamwoven/issues/2476) | reading desk and written-object pictures | 20 |
| BD-E | [#2477](https://github.com/Simarglok/arcanum-steamwoven/issues/2477) | world map frame | 1 |
| BD-F | [#2478](https://github.com/Simarglok/arcanum-steamwoven/issues/2478) | schematic parchment backdrop | 1 |
| BD-G | [#2479](https://github.com/Simarglok/arcanum-steamwoven/issues/2479) | two-strip menu backdrops | 5 |
| BD-H | [#2480](https://github.com/Simarglok/arcanum-steamwoven/issues/2480) | full-screen menu backdrops | 8 |
| B-HARDWARE | [#2481](https://github.com/Simarglok/arcanum-steamwoven/issues/2481) | hardware: frames, slots, lens rings, pop-up panels | 69 |
| B-BUTTONS | [#2482](https://github.com/Simarglok/arcanum-steamwoven/issues/2482) | button plates, glyphs, state overlays | 178 |
| B-GAUGES | [#2483](https://github.com/Simarglok/arcanum-steamwoven/issues/2483) | gauges and readouts | 56 |
| B-ICONS-SPELL | [#2484](https://github.com/Simarglok/arcanum-steamwoven/issues/2484) | spell and skill icons | 88 |
| B-ICONS | [#2485](https://github.com/Simarglok/arcanum-steamwoven/issues/2485) | item, critter, status and other icons | 162 |
| B-SCHEMATIC | [#2486](https://github.com/Simarglok/arcanum-steamwoven/issues/2486) | schematic record pictures | 134 |
| B-CURSORS | [#2487](https://github.com/Simarglok/arcanum-steamwoven/issues/2487) | cursors | 12 |

The game window itself (the world, 800 x 400 at (0, 41)) is not a window of the kit, and the hidden-interface message line and combat bar of ARC-RES-093 table A exist only under `-fullscreen` (H14, text only). Fonts (33 bitmap font arts) are the scope of [#2462](https://github.com/Simarglok/arcanum-steamwoven/issues/2462); arts of network play (24) and eight unused tab arts have no batch.

## Findings and open points

- The strips and every wood backdrop show one material, a dark streaked wood; the families differ in what is laid on it (grids, doll wells, brass, iron panels, paper). That favours one material sheet for the wood, brass, iron, paper and parchment, and one wood backdrop method for A, B, C, D, E and G ([#2457](https://github.com/Simarglok/arcanum-steamwoven/issues/2457)).
- The TRD names two-part windows of the inventory family only; the census finds two more (logbook 165 + 635 units, reading window 132 + 668 units) and the two-strip menus (save, load, pre-made, new character) whose strips 335 and 336 are not the HUD strips. TRD SWI-BDR-5 and SWI-BDR-4 point here for the list.
- The schematic window carries 134 record pictures of 295 x 225 (the largest batch by count) and the written-object pictures 12 of 668 x 400; both are script- or record-given pictures, not chrome.
- 47 numbered messages of `interface.mes` (world map tile and mask arts 149 to 164, the portrait sets 570 to 581 and 602 on, a few single arts) have no file under `art\interface` in this installation, so they have no row; they are bitmaps or live elsewhere and need their own reading ([#2490](https://github.com/Simarglok/arcanum-steamwoven/issues/2490)).
- Open facts (marked `?` in the local working files, none changes a family or a kind): the scroll-bar chain art ids of the inventory family (236, 237, 316, 319 are candidates), which frames of the lit button arts are drawn, the y of the load indicator in the combat UI, the idle, hover and pressed meaning of most button frames, the unit of the schematic slot overlays 830 and 831, the follower portrait art, the weapon-pane slot icons by spell. They are read with the slices that need them ([#2490](https://github.com/Simarglok/arcanum-steamwoven/issues/2490)).
- Source conflicts to correct when touched: art 293 has 2 frames in the file, the ledger says 3; the to-hit icon widths of ledger row OH19 do not match the file order; CHW1 lists Health and Fatigue as baked into art 22 (see above).


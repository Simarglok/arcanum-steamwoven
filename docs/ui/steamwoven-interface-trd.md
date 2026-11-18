# TRD: Steamwoven interface — layout, kit, pipeline and work breakdown

Status: **requirements for implementation, 2026-11-16** (work item ARC-UI-2447). The decision and its reasons are in
the [ADR](steamwoven-interface-adr.md); this page says what to build and how it is checked. Epic:
[#2446](https://github.com/Simarglok/arcanum-steamwoven/issues/2446). Every requirement has an id `SWI-<area>-<n>`;
issues and PRs cite the ids. Values marked *provisional* are fixed by the named owner decision. Nothing here is
implemented yet.

## Terms

| Term | Meaning |
| --- | --- |
| Master | A part as authored for the 3840 × 2160 reference screen, in master pixels (px). |
| Step | One of the four baked sets: ×1, ×2/3, ×1/2, ×1/3 of the masters. |
| Frame | The rectangle of the window the interface is laid out in (the window minus any black bars). |
| `f` | Device pixels per master pixel for the current frame. |
| UI unit | One pixel of the original 800 × 600 layout. In the master, 1 unit = 3.6 px. |
| Band | Top panel, middle band (world or menu backdrop), bottom panel. |
| Centre block | The 2880 px (800 units) wide part of a band that keeps the original layout. |
| Sides | The parts of a band left and right of the centre block. |
| Family | A group of menus whose backgrounds share one material and so one backdrop. |
| Original look / Steamwoven look | The interface with the HD option off / on ([ADR](steamwoven-interface-adr.md#context)). |

## Screen frame and scale

- **SWI-LAY-1** The reference screen is 3840 × 2160. For a window of W × H device pixels,
  `f = min(W / 3840, H / 2160)`.
- **SWI-LAY-2** An aspect from 16:10 to 16:9 inclusive is laid out natively: the frame is the whole window.
- **SWI-LAY-3** A narrower window (4:3, 5:4) gets a frame of W × round(2400 · f), centred, black bars at the top and
  bottom. A wider window (21:9, 32:9) gets a frame of round(3840 · f) × H, centred, black bars at the sides. A bar
  under 4 px per side is dropped: the frame is the whole window and the sides take the difference.
- **SWI-LAY-4** The baked step is the smallest of ×1, ×2/3, ×1/2, ×1/3 that is ≥ `f`. Above ×1 (5K screens) the ×1
  set is scaled up (a recorded limit, see [open questions](#open-questions)). Below ×1/3 the ×1/3 set is scaled down
  to `f` = 0.25; below 0.25 the Steamwoven interface is not supported and the Original look is used.
- **SWI-LAY-5** The run-time scale is `f / step`. `f`, the step choice and the test for an exact step are computed
  exactly (integers or rationals, for example `3 · W ≥ 2 · 3840`), never with floating-point equality. When it is exactly 1 the parts are drawn at whole device pixels with
  no filtering; otherwise with linear filtering from the baked set.
- **SWI-LAY-6** W × H are device (backing) pixels, including the macOS backing scale.
- **SWI-LAY-7** Every master length and offset is a multiple of 6 px, so it is a whole number of pixels at every step
  (6 px = 4, 3 and 2 px). A device position is `round(master position · f)`, computed per edge (not per size), so
  neighbouring parts share their edges; halves round up. The two panel heights are the exception: each is `round(height · f)`
  from the frame's own top or bottom edge, as the reference table below shows (1680 × 945: bottom panel 200, where rounding the bottom panel's top edge would give 199).

Reference table (band heights at the default values of SWI-LAY-10; "bars" are per side, horizontal / vertical). The layout
model's unit tests use these numbers ([#2450](https://github.com/Simarglok/arcanum-steamwoven/issues/2450)):

| Window | Frame | `f` | Step | Run-time scale | Top | Middle | Bottom | Centre | Side | Bars |
| --- | --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 3840 × 2160 | 3840 × 2160 | 1.0000 | 1 | 1.000 | 120 | 1584 | 456 | 2880 | 480 | 0 / 0 |
| 2560 × 1440 | 2560 × 1440 | 0.6667 | 2/3 | 1.000 | 80 | 1056 | 304 | 1920 | 320 | 0 / 0 |
| 1920 × 1080 | 1920 × 1080 | 0.5000 | 1/2 | 1.000 | 60 | 792 | 228 | 1440 | 240 | 0 / 0 |
| 1280 × 720 | 1280 × 720 | 0.3333 | 1/3 | 1.000 | 40 | 528 | 152 | 960 | 160 | 0 / 0 |
| 3840 × 2400 | 3840 × 2400 | 1.0000 | 1 | 1.000 | 120 | 1824 | 456 | 2880 | 480 | 0 / 0 |
| 2560 × 1600 | 2560 × 1600 | 0.6667 | 2/3 | 1.000 | 80 | 1216 | 304 | 1920 | 320 | 0 / 0 |
| 1920 × 1200 | 1920 × 1200 | 0.5000 | 1/2 | 1.000 | 60 | 912 | 228 | 1440 | 240 | 0 / 0 |
| 1280 × 800 | 1280 × 800 | 0.3333 | 1/3 | 1.000 | 40 | 608 | 152 | 960 | 160 | 0 / 0 |
| 3024 × 1964 | 3024 × 1890 | 0.7875 | 1 | 0.787 | 95 | 1436 | 359 | 2268 | 378 | 0 / 37 |
| 3456 × 2234 | 3456 × 2160 | 0.9000 | 1 | 0.900 | 108 | 1642 | 410 | 2592 | 432 | 0 / 37 |
| 2880 × 1800 | 2880 × 1800 | 0.7500 | 1 | 0.750 | 90 | 1368 | 342 | 2160 | 360 | 0 / 0 |
| 1366 × 768 | 1366 × 768 | 0.3556 | 1/2 | 0.711 | 43 | 563 | 162 | 1024 | 171 | 0 / 0 |
| 1600 × 900 | 1600 × 900 | 0.4167 | 1/2 | 0.833 | 50 | 660 | 190 | 1200 | 200 | 0 / 0 |
| 1680 × 945 | 1680 × 945 | 0.4375 | 1/2 | 0.875 | 53 | 692 | 200 | 1260 | 210 | 0 / 0 |
| 3440 × 1440 | 2560 × 1440 | 0.6667 | 2/3 | 1.000 | 80 | 1056 | 304 | 1920 | 320 | 440 / 0 |
| 5120 × 1440 | 2560 × 1440 | 0.6667 | 2/3 | 1.000 | 80 | 1056 | 304 | 1920 | 320 | 1280 / 0 |
| 1024 × 768 | 1024 × 640 | 0.2667 | 1/3 | 0.800 | 32 | 486 | 122 | 768 | 128 | 0 / 64 |
| 5120 × 2880 | 5120 × 2880 | 1.3333 | 1 | 1.333 | 160 | 2112 | 608 | 3840 | 640 | 0 / 0 |

The two MacBook rows (3024 × 1964, 3456 × 2234) are narrower than 16:10 and get bars of 37 px; in macOS full screen
the area beside the camera notch is usually left out, which may make the window 16:10 already (to check on the
device, not a requirement).

Why this range (Steam Hardware Survey, primary display, October 2026, read 2026-11-16 from
[store.steampowered.com/hwsurvey](https://store.steampowered.com/hwsurvey/Steam-Hardware-Software-Survey-Welcome-to-Steam)):
1920 × 1080 50.23 %, 2560 × 1440 21.82 %, 2560 × 1600 5.46 %, 3840 × 2160 5.05 %, 3440 × 1440 3.23 %,
1920 × 1200 2.97 %, 1366 × 768 2.25 %; 16:10 resolutions together about 11 %; 2560 × 1080, 3440 × 1440 and
5120 × 1440 together about 4.3 %; no 4:3 resolution is listed (all below the 3.30 % "Other").

## Play screen layout

- **SWI-LAY-10** Three bands: top panel 120 px, bottom panel 456 px (master; **owner decision 2026-11-17**,
  [#2453](https://github.com/Simarglok/arcanum-steamwoven/issues/2453), after the [prototype](#prototype-of-the-play-screen-arc-ui-2452)'s comparison of this pair with the original strips' own 150 and 570: the original's
  41 and 159 units × 3.6 = 147.6 and 572.4, rounded to the grid), and the middle band = frame height − top − bottom. Both heights are multiples of 6 and together at most 720, so the window zone of SWI-BDR-2 always fits
  a 16:9 middle band; at 120 and 456 the middle band is 1584 px at 16:9 and 1824 px at 16:10, and the zone has 72 px above and below it at 16:9. The layout model's options hold the pair in one place.
- **SWI-LAY-11** Each panel spans the whole frame width. The centre block (2880 px, the width of the window zone of SWI-BDR-2) is centred; the sides take the
  rest. **Owner decision 2026-11-18 (reverses the choice of 2026-11-17 in [#2526](https://github.com/Simarglok/arcanum-steamwoven/issues/2526); ARC-UI-2608 reverted): variant A, the whole HUD scales proportionally, one centre scale k = 0.8 with a 2304 px block, the sides take the rest, nothing is squashed. The owner took 0.8 for a proportional reduction; variant B, tried in the viewer (PR #2642), looked squashed. The text below describes variant B, which the layout model (`SteamwovenControls`) still computes and the viewer does not use; it is not the rule in force:** in B the centre block keeps its
  width, so every horizontal offset and width of a control is the original's × 3.6 snapped to the 6 px grid, and the HUD text (message window, clock, counters) has the scale `f · 3.6` of the windows' text. Only the vertical
  placement fits the band, because the bands cannot hold the original strips (147.6 and 572.4 px at that scale): the kit's hardware is drawn for them. The rule per band (G = the 6 px grid; centred = `floor_G((band − height) / 2)`,
  a shift of at most 3 px; the numbers are for 120 and 456; the layout model, `SteamwovenControls`, holds the same rule for any pair):
  - top panel T: a button (Character, Logbook, Map, Inventory) is a square of `min(120, T − 24)` = 96 (12 px above and below), Fate Points and Sleep `min(84, T − 48)` = 72, the clock window 462 wide and `min(108, T − 36)` = 84 high, all
    centred; the experience strip hangs over the band's lower edge, 1380 × 48 with its top `T − 12`;
  - bottom panel B: the three right-hand buttons (Skills, Spells, Schematics) are squares of `min(138, floor_G((B − 96) / 3))` = 120 at tops 18, 18 + side + 30 and 18 + 2 · (side + 30) (the original's stagger of x kept); the Combat button is the same square, centred; the two
    vials (102 wide, `min(318, floor_G(B − 36))` = 318 high) and the message window (1380 wide, `min(294, floor_G(B − 36))` = 294 high) are centred, the hover image is `min(228, message height − 48)` = 228 square at (24, 24) in the message window.
  The rectangles at 120 / 456, in master px from the centre block's left edge and the band's top edge (x, y, width × height):
  top: Character 12, 12, 96; Logbook 150, 12, 96; Map 282, 12, 96; Inventory 414, 12, 96; Fate Points 564, 24, 72; Sleep 2178, 24, 72; clock 2334, 18, 462 × 84; experience strip 762, 108, 1380 × 48;
  bottom: Combat 312, 168, 120; Skills 2496, 18, 120; Spells 2334, 168, 120; Schematics 2496, 318, 120; health vial 48, 66, 102 × 318; fatigue vial 2712, 66, 102 × 318; message window 762, 78, 1380 × 294; hover image 786, 102, 228.
  A control's device rectangle takes each edge from its own exact master position, from the centre block's left edge and the band's top edge, halves up (SWI-LAY-7). The original positions the model reads are the bar layout's
  (the viewer's test compares them).
- **SWI-LAY-12** A panel is the panel board (one continuous material for both panels) plus hardware layers (sockets,
  rings, vial housings, the message window frame, rails, buttons). The sides show the same board: same material, tone
  and texture, no visible join at the centre block's edges (measured by SWI-VER-5). At this stage the sides have no controls and no decor.
- **SWI-LAY-13** Hit areas are the rectangles (or circles) of the drawn parts from the layout, not the original's
  pixel masks.
- **SWI-LAY-14** With no menu open the middle band is the world view. The out-of-sight rules follow the owner decision
  of 2026-11-07 ([decisions](../architecture/decisions.md), "Out-of-sight rules and the view": Steamwoven tests the
  drawn view).
- **SWI-LAY-15** No text is baked into any part; all text is drawn with fonts
  ([#2462](https://github.com/Simarglok/arcanum-steamwoven/issues/2462)).
  **Note body (ARC-UI-2765, [#2765](https://github.com/Simarglok/arcanum-steamwoven/issues/2765)).** The owner could not read a letter in the original's
  handwriting face (Pepper20, art 497) at the window's scale, so the generated interface draws a note's body in the first of the player's fonts 840 (NewTimes16,
  a plain serif) and 229 (Flare12) that holds the whole text in the note's rectangle, and keeps 497 for a note neither holds (`AreaReadingArtLayout.NoteFontsFor`,
  `AreaReadingFlow.Single`); the Original look and every other written kind keep their fonts. Local capture: `--env ARC_AREA_READING_NOTE_FONTS=301,...` with the
  Steamwoven `bar` smoke (`ARC_AREA_BAR_STEAMWOVEN=1`, `ARC_AREA_BAR_CAPTURE=PREFIX`) saves the note as drawn, in 497 and in each listed face, as `PREFIX-hud-steamwoven-*-note-*.png`
  (never committed). The telegram, newspaper and plaque faces are not reworked; they stay the original's until the owner asks.

## Prototype of the play screen (ARC-UI-2452)

Slice [#2452](https://github.com/Simarglok/arcanum-steamwoven/issues/2452): the viewer lays the play screen out by the layout model with the **generated chrome** as the placeholder, to judge sizes and proportions before any
Codex art. It is on while the HD option and the generated interface are both on (`AreaGameBarPanel.SteamwovenOn`); with the HD option off, or with the generated interface off, the whole-factor layout is unchanged (the strips and the
bars' captures are pixel-identical to `origin/main`; only the changing text of the diagnostic palette differs).

- **Scale.** The two strips keep the original's 800-unit layout and are drawn at one fractional scale, `f · 3.6 · k` (`SteamwovenHud.Plan`); `k` is 1 for the original strips' own heights (150 and 570 master px) and the smaller of
  `top / 150` and `bottom / 570` for another pair (0.8 for 120 and 456, the prototype's stand-in), so thinner panels give a smaller centre block (2304 px) and wider sides, not a squashed strip: the prototype's stand-in for art that will be drawn to fit the panel (the owner's choice of #2526 is SWI-LAY-11's 2880 px centre block with controls placed by the layout model; the prototype's strips keep this stand-in scale until the runtime composition, [#2461](https://github.com/Simarglok/arcanum-steamwoven/issues/2461), or the viewer's own HUD issue lays the controls by it). The top strip stands on the lower edge of the top band and the bottom strip hangs
  from the upper edge of the bottom band, so each strip's rim meets the board's rim. At k = 1 a 41-unit strip is 147.6 · f px in a 150 · f band (the board shows above it) and a 159-unit strip is 572.4 · f px in a 570 · f band (its last
  2.4 · f px lie beyond the frame's bottom edge); at k = 0.8 they are 118.1 · f in 120 · f and 457.9 · f in 456 · f.
- **Boards.** The band beside each strip is a generated board of the strip's own wood (colours tuned from the strip frame, grain continued from the strip's position, the rim on the edge facing the world), painted off
  the main thread (`InterfaceChromeRenderer.RenderBoard`, `SteamwovenBoardView`). The world is still drawn behind the panels over the whole frame, as in the original; the bars of a letterboxed or pillarboxed
  window are black rectangles over it.
- **Menus.** `AreaGameBarPanel.TopInset` and `BottomInset` are now the middle band's, so every window that docks between the strips docks in it (the windows stand in the window zone, see below). While a menu window is open the generated backdrop of its family fills the whole middle band (layer 3, under the windows; ARC-UI-2576, below).
- **Windows in the window zone (ARC-UI-2575).** Every menu window of the middle band takes its place from one hook, `AreaGameBarPanel.ZoneWindow` / `ZoneScreen` (`SteamwovenWindowZone`, Godot-free, unit-tested): the window is drawn at
  the layout's unit scale `f · 3.6` (1.8 at 1920 × 1080, 2.4 at 2560 × 1440, 3.6 at 3840 × 2160) and its 800 × 400 body is the window zone of SWI-BDR-2, centred in the middle band, so the free width is the frame and a window never lies under a
  pillarbox bar. It covers the inventory, loot, barter (docked and undocked), character sheet (also the creation editor's screen), logbook, world map, schematics, reading, save/load and the shop strips; the pre-made browser and the
  new-character screen take the same hook (the screen views place the 800 × 600 screen so its body lies in the zone). The hook reads the window size alone, so the windows that hide the bars (loot, barter, reading, save/load) follow the same zone. A
  screen (an 800 × 600 original screen with its own two strips) is kept inside the frame: at 16:9 it is as high as the frame, so its body lies a little above the zone's place; at 16:10 it lies exactly in it. With the HD option or the
  generated interface off the windows keep their own whole or fitted scale (pixel-identical). The smoke `bar --env ARC_AREA_BAR_STEAMWOVEN=1` opens the inventory, sheet, logbook, map, schematics, the save list, the loot window of a container (See Contents), the barter window of the first NPC that can trade,
  a readable book, and the main menu's pre-made and new-character screens (ARC-UI-2614) at each size and checks each drawn body against the hook, saving `…-hud-steamwoven-VARIANT-WxH-WINDOW.png`.
- **Backdrops per family (ARC-UI-2576).** The backdrop is chosen by the window on top of the interface mode stack (`SteamwovenBackdrops.FamilyOf`: an overlay such as the quantity picker keeps the window's own backdrop): BD-A for the inventory, loot, steal, barter, identify
  and repair windows, BD-B the character sheet, BD-C the logbook, BD-D the reading window, BD-E the world map, BD-F the schematics; the dialog and sleep views have none. A piece is generated wood sampled at the window's unit scale `f · 3.6` (the census finds one dark streaked wood under
  A to E, the families differ in what is laid on it, so each window's own wood is continued outwards), its grain starting at the window art it continues: the window's top left for a one-piece family, and for a two-part family
  (SWI-BDR-5) each half starts at its own left edge, so the right half's grain starts at the split. The inventory family splits at the layout model's `Split` (1290 px from the centre block's left edge at 3840 × 2160); the logbook (165 + 635 units) and the reading window
  (132 + 668 units) at the zone's left edge plus their left part at the unit scale. The parts tile the middle band (the frame's width) and meet at the split. Each piece's wood is tuned to the tone of the window art it continues (ARC-UI-2643, `SteamwovenBackdrops.ToneArt`: the paper doll 223 and inventory 221, the sheet 22, the logbook's side 264 and book 260, the reading side bar 494, the map frame 138, the schematic base 365; read with the bars' arts, the bottom strip's tone where one is missing), the way a board takes its strip's;
  the schematic window's parchment page and the logbook's book lie on the wood, so BD-F is wood at the zone's edges. The two-strip menus (BD-G) and the full-screen menus (BD-H) hide the bars and are laid out over the whole frame by [#2577](https://github.com/Simarglok/arcanum-steamwoven/issues/2577). The smoke
  (`SteamwovenBackdrops`, `CheckBackdrop`) checks the family, the pieces' rectangles and texels, and the seam ratio R of SWI-VER-5 (`SteamwovenSeam`: the difference of neighbouring pixels in a ±8 px band at the join over the one in a band beside it on the backdrop, inside the
  middle band; each taken at the cleanest twentieth of the edge's rows or columns, because parts of the window that lie at its edge, such as the character sheet's alignment scale, only raise it and a real seam shows on every line) at the edges of the window zone (left, right and top gated, R ≤ 1.5; the bottom edge is printed only, because the windows' own look switch, a wide bordered button, lies just below it): the highest of the nine SWI-VER-6 sizes is 1.20. The split of a two-part window is a designed join of two arts
  (each half's grain starts at its own left edge) and is exempt from R; the smoke prints it above the zone (0.5 to 1.7). In the Steamwoven layout the barter, identify and repair and reading windows keep the bars (`GameBarActive`; the original hides them), so that every menu window of the middle band has its backdrop; the main menu, the save list and the palette still hide them. The round-view lens of a window (the ring at the top left of the logbook, schematics, reading, inventory and barter windows, ARC-UI-2645) copies the game view on layer 2, under the backdrop (layer 3), which leaves the lens's square out (`SteamwovenBoardView.SetHoles`, `AreaRoundView.VisibleRects`; the smoke checks the count). The character sheet has the live lens too ([#2694](https://github.com/Simarglok/arcanum-steamwoven/issues/2694): the sheet's open turns the view-window mode 1 on with art 198, CHW9, so the medallion of art 22 is under it and the square (12, 10, 89) is cut out for the round view and the cover art 198 drawn over it), and the world map's window frame draws no ring of its own outside the lens circle (the cover art's ring inside its square is the only one). The smoke clicks the lens of the sheet and the map (a mouse-up in it closes the window) and prints each lens's place in units from the zone corner. **One lens place (owner decision 2026-11-18, [#2714](https://github.com/Simarglok/arcanum-steamwoven/issues/2714)):** the original's arts put the 89-unit lens at five places (inventory 11, 9; sheet 12, 10; loot and barter 16, 17; logbook, map and reading 25, 24; schematics 50, 26), which made it jump when one window replaced another. With the generated interface every window's lens stands at **11, 9** (`AreaLensPlace`, set with the interface setting in `InterfaceHd.Configure`; the Original look keeps each window's own place). The point is the inventory window's own, because the inventory's item grid starts at y 107 right under the lens (a lens any lower, the median 16, 17 among them, would cut into the grid or move the grid and the doll down); every other lens only moves toward the corner, into wood that holds nothing (sheet 1, 1; loot and barter 5, 8; logbook, map and reading 14, 15; schematics 39, 17), so no window's content moves. The generated art of each window carries the hole there: a recipe's `ChromeLensFrom` names the Legacy frame's place and `InterfaceChromeRenderer.Render` moves that box (the lens's 89 units and 5 around them, with the HD picture) to the common place before painting and closes the old place with the frame's own wood; the views (`AreaLogbookArtLayout.Lens` and the like, `AreaInventoryHole.Corner`, `AreaSchematicWindowLayout.RoundView`, the sheet's close rectangle) read the place through `AreaLensPlace.Corner`. The reading window stands in the window zone like the others (its top at the zone body's top; it stood on the 800 × 600 screen's body, up to 12.5 units above the zone at 16:9), so its lens is at the same place to the pixel. The smoke requires every lens at 11, 9 within a device pixel (`TerrainViewerSteamwovenSmoke`, the `lens` print). The old backdrop was `WoodDark`; the family backdrops are the wood of their window's own tone, so an open menu is lighter than before and its seam to the window is not a step.
- The combat list and the spell pane ([#2604](https://github.com/Simarglok/arcanum-steamwoven/issues/2604)) take `AreaGameBarPanel.FreeRect` too: the spell pane starts 12 px right of the frame's left edge (`FreeLeft`) and the combat list ends 8 px left of the frame's right edge, not under a pillarbox bar; without the layout `FreeRect` is the whole window and they stand as before. The text bubbles' placement clamp ([#2603](https://github.com/Simarglok/arcanum-steamwoven/issues/2603)) takes `AreaGameBarPanel.FreeRect` (the frame's width from `FreeLeft`, between the strips; the whole window without the layout) as the view `TextBubbleRules.ChooseSlot` clamps against. Done by [#2510](https://github.com/Simarglok/arcanum-steamwoven/issues/2510): the follower portraits hang from the top strip and the floaters (lines and text bubbles) are laid
  out at the strips' scale (`AreaGameBarPanel.TopScreen`, the screen where the strips put it); the sleep and fate views keep their own whole scale, placed on that screen, and their textures claim it from the moment the art is handed over
  (the strips' fractional scale is for the bar textures only); the free width of `AreaGameBarPanel.FreeArea` is the frame's, from `FreeLeft`, for the windows that still take their place from it. The strips are not snapped to the 6 px grid and are sampled
  Linear; the boards are painted at the logical scale (the prototype assumes a content scale of 1). The backdrop is for the menu windows, not for the dialog or sleep.
- **Full-screen menus** ([#2577](https://github.com/Simarglok/arcanum-steamwoven/issues/2577), SWI-BDR-6; `SteamwovenFullScreen`, `AreaMenuArtView`, `AreaMainMenuPanel`). With the same switch (the HD option and the generated interface on) the main, single-player, new-game, in-play and options windows
  (the census' BD-H: the windows of `AreaMenuArtView`) are laid out by the model's full-screen zone: the 800 × 600 plate stands in the 2880 × 2160 · f zone centred in the frame at `f · 3.6` (sampled Linear; the pack's 16:9 plate is not used), a generated dark-wood backdrop
  (`SteamwovenBoardView`, painted off the main thread) covers the whole frame under it, and the window outside the frame is black. Hit testing is the control's own transform, so it follows the drawn plate (`SteamwovenFullScreenPlan.HitTest` is the same mapping as plain numbers, unit-tested). The backdrop is a placeholder: the wood is not tuned to the
  painted still life of art 329/331, so the join shows (the real backdrops are SWI-BDR-7, batch BD-H). Credits and View Intro (a black fill) and the pre-made, new-character, editor and shop windows (two-strip menus, [#2575](https://github.com/Simarglok/arcanum-steamwoven/issues/2575)) are not part of it. With the HD option or the generated interface off the whole-factor and 16:9-plate look is unchanged.
  Smoke: `./arcanum smoke pr --stages menu --main-menu --env ARC_AREA_MENU_STEAMWOVEN=1 [--env ARC_AREA_MENU_CAPTURE=PATH.png]` checks, at the SWI-VER-6 sizes (`ARC_STEAMWOVEN_SIZES`), for the main and options windows, the plate's scale and place, the backdrop over the frame, the black bars and the pointer over each label, and saves
  `PATH-steamwoven-WINDOW-WxH.png` and a `-before` capture (local, never in git). With `ARC_AREA_MENU_CAPTURE` it also saves ([#2672](https://github.com/Simarglok/arcanum-steamwoven/issues/2672), the `legacy` variant of the bar smoke below) the same windows in the Original look (the HD option off, the whole-factor plate, no plan) as `PATH-legacy-WINDOW-WxH.png`, with no layout checks.
- **Smoke.** `./arcanum smoke pr --stages bar --env ARC_AREA_BAR_STEAMWOVEN=1 --env ARC_HD_PACK=<pack> --env ARC_AREA_BAR_CAPTURE=<prefix>` checks the plan, the strips' places, the insets, the boards, the bars and the open-menu
  backdrop at the SWI-VER-6 sizes (`ARC_STEAMWOVEN_SIZES=WxH,...` to choose others) for the variants of `ARC_STEAMWOVEN_VARIANTS` (`thin` = the default 120 and 456, `original` = 150 and 570, `before` = the old layout, `legacy` = the Original look for comparison ([#2672](https://github.com/Simarglok/arcanum-steamwoven/issues/2672): the HD option off for that pass, Legacy art, no plan, the whole factor, the same sizes, inventory and window probes saved as `<prefix>-hud-steamwoven-legacy-WxH[-menu|-WINDOW].png` with no layout-plan checks, only that the Original look stands (no plan, no boards, the strips at the whole factor with Legacy art); the settings are restored after it), `TOP-BOTTOM` master px), the root
  viewport takes the size as its logical render target and the window is given the size's width (ARC-UI-2696: the content scale mode Viewport draws the target at the window's size, so a narrower window, the default 1280 wide for a 2560 target, has a stretch of 0.5 and every generated texture, the strips' and the windows' wood, is painted at that stretch, clamped to one pixel per unit, and drawn up again, softer than the boards, which are painted at the target's scale: the captures showed two woods; the height is the operating system's to limit and does not matter, the paint reads the stretch across; the smoke checks that the strips are painted at the scale they are drawn at; `ARC_STEAMWOVEN_FIT_WINDOW=0` keeps the old window for a comparison, the check then only prints), and saves `<prefix>-hud-steamwoven-VARIANT-WxH[-menu].png` (local, never in git). The older generated-interface smokes run with the layout
  suspended (`SteamwovenHud.Suspended`): they measure the whole-factor geometry.

## Menu backdrops

- **SWI-BDR-1** An open menu window fills the whole middle band (full width and height) with its backdrop.
- **SWI-BDR-2** The window zone, 2880 × 1440 px (the original 800 × 400 units), is centred in the middle band
  (vertical placement: [open questions](#open-questions)); window content (paper doll, grids, pages, buttons) sits in
  it at the original offsets × 3.6, snapped to the grid. The zone scale is `f · 3.6`; the HUD is at 0.8 of it (SWI-LAY-11, variant A of #2526), so its centre block (2304 px) is narrower than the zone.
- **SWI-BDR-3** A backdrop master is 3840 × 1824 px (the middle band of a 16:10 frame at ×1: 2400 − 120 − 456). A shorter middle band
  shows its centred crop (16:9: 1584 px). A middle band a few pixels taller than 16:10 allows (a bar under 4 px dropped by
  SWI-LAY-3) gets the backdrop scaled uniformly to cover it, centred.
- **SWI-BDR-4** Menus whose original backgrounds share a material share one backdrop (a family). The families are
  fixed by the census ([#2451](https://github.com/Simarglok/arcanum-steamwoven/issues/2451)): eight families (BD-A to BD-H) and the
  panel board, listed in the [census](steamwoven-interface-census.md#backdrop-families).
- **SWI-BDR-5** A two-part window (inventory, loot, barter: left 358 units, right 442 units; the census finds two more, the logbook 165 + 635 and the reading window 132 + 668, [census](steamwoven-interface-census.md#two-part-windows-and-full-screen-menus)) splits at 1290 px from the
  centre block's left edge (358 × 3.6 = 1288.8, snapped). The left material runs from the frame's left edge to the
  split, the right material from the split to the frame's right edge.
- **SWI-BDR-6** A full-screen menu (an original 800 × 600 background: main menu, options and the others the census
  finds) has a 3840 × 2400 master that covers the whole frame, panels included; a shorter frame shows the centred
  crop, a slightly taller one (SWI-LAY-3) the master scaled to cover. Its 800 × 600 zone is 2880 × 2160 px, centred.
- **SWI-BDR-7** Each backdrop is made from its own original background: the centre after the original, the sides
  continued outwards from the centre's edges in the same material, with no visible join (measured by SWI-VER-5).

## Asset kit

Part kinds:

| Kind | What | Examples |
| --- | --- | --- |
| material | a surface, seamless where it must be | wood, dark wood, brass, iron, paper, parchment |
| board | the panel board, one continuous image for both panels | wood of the top and bottom panels |
| backdrop | the full middle band (or full frame) of a family | inventory left/right, logbook, schematic paper |
| hardware | fixed metal or frame pieces over a board or backdrop | brass rings, sockets, vial housings, rails, frames |
| plate | a button's body | round and square plates by size |
| glyph | a symbol on a transparent background | button symbols |
| state | an overlay for a button state | hover, pressed, disabled |
| sheet | a document laid over a backdrop | newspaper, letter, book page, telegram, plaque |
| grid | a grid as its own layer | the schematic grid, inventory cells |
| picture | an image on a transparent background | items on the schematic, item and spell icons, portraits |
| gauge | layered gauge parts; the fill comes from the value | vial glass, housing, liquid |
| decor | ornaments that are not hardware | corner pieces, rivets |
| cursor | pointer art | the cursor set |

- **SWI-KIT-1** Every part is a row of the kit manifest `tools/steamwoven_ui/kit.json`
  ([#2454](https://github.com/Simarglok/arcanum-steamwoven/issues/2454)): id, kind, master size, anchor, layers,
  states, used-by windows, source original art ids (identifiers only), family, batch, provenance record id, status.
  The manifest holds no pixels.
- **SWI-KIT-2** A master is a PNG, RGBA 8 bit, sRGB, straight alpha, with sides that are multiples of 6 px.
- **SWI-KIT-3** A part used in several windows (a reuse group of the census) is one part at one master size.
- **SWI-KIT-4** A part with transparency has a clean edge: no halo of the generator's background colour (checked by
  the fit tool, SWI-VER-3).
- **SWI-KIT-5** A button is plate + ring + glyph + state overlays. The states of one button size (idle, hover, pressed,
  disabled) are generated together on one sheet, so they match.
- **SWI-KIT-6** A gauge is glass, housing and liquid as layers; the fill level is drawn from the value, not from a frame
  sequence.
- **SWI-KIT-7** Order of production: board and backdrops, then buttons and glyphs, sheets, the schematic, gauges and
  decor; icons, portraits and cursors last (the owner: all interface art is redrawn).

## Generation pipeline

| Step | Who | Output | Where it lives |
| --- | --- | --- | --- |
| 1. Census | agent ([#2451](https://github.com/Simarglok/arcanum-steamwoven/issues/2451)) | [census note](steamwoven-interface-census.md), screenshots | note in git, screenshots in `local/steamwoven-ui/census/` |
| 2. Kit manifest | agent ([#2454](https://github.com/Simarglok/arcanum-steamwoven/issues/2454)) | `kit.json`, validator | git |
| 3. Context images | owner in Codex ([#2455](https://github.com/Simarglok/arcanum-steamwoven/issues/2455)) | whole-screen redraws of key screens | local |
| 4. Material sheet | owner in Codex, owner approves ([#2456](https://github.com/Simarglok/arcanum-steamwoven/issues/2456)) | wood, brass, paper, … | local |
| 5. Parts | owner in Codex, per batch | raw generations | `local/steamwoven-ui/outbox/` |
| 6. Fit | agent tool ([#2459](https://github.com/Simarglok/arcanum-steamwoven/issues/2459)) | masters | `local/steamwoven-ui/masters/` |
| 7. Look check | owner | per-batch composite | thread only |
| 8. Bake | agent tool ([#2460](https://github.com/Simarglok/arcanum-steamwoven/issues/2460)) | four step sets | per SWI-PRV |
| 9. Runtime | agent ([#2461](https://github.com/Simarglok/arcanum-steamwoven/issues/2461)) | composed interface | code in git |

- **SWI-GEN-1** Context images come first: whole-screen redraws of the key screens (HUD, inventory, character,
  logbook, schematic, one reading window, main menu) at the frame size, so Codex sees what each part is for. Parts are
  then generated with a crop of the context image as reference.
- **SWI-GEN-2** The approved material sheet freezes the tones and textures; every later part is generated against it
  and colour-locked to it.
- **SWI-GEN-3** A part's references are the context crop and the material sheet, plus the original art of the part
  (crop, frame or screenshot): original art is given to Codex as input (owner decision 2026-11-16,
  [#2448](https://github.com/Simarglok/arcanum-steamwoven/issues/2448)). Its prompt
  comes from the prompt library `tools/steamwoven_ui/prompts/` (text only).
- **SWI-GEN-4** A generation becomes a master only through the fit tool (size, grid, alpha clean-up, low-frequency
  colour lock, provenance record).
- **SWI-GEN-5** The board and the backdrops are made by the method the owner picks
  ([#2458](https://github.com/Simarglok/arcanum-steamwoven/issues/2458)) from the comparison
  ([#2457](https://github.com/Simarglok/arcanum-steamwoven/issues/2457)).
- **SWI-GEN-6** Every generated image has a provenance record: generator and version, date, prompt id and the hash of
  its text, reference ids, who ran it.
- **SWI-GEN-7** The owner runs Codex image generation (owner decision 2026-11-16, [#2449](https://github.com/Simarglok/arcanum-steamwoven/issues/2449)); agents never
  send anything to Codex. Handoff: the agent puts a batch's prompts, references and a short list of what to generate into
  `local/steamwoven-ui/inbox/<batch>/`, asks the owner in the thread, and the owner puts the results into
  `local/steamwoven-ui/outbox/<batch>/`; the agent then writes the provenance records (SWI-GEN-6) and runs the fit tool.

## Bake and runtime

- **SWI-RUN-1** The bake makes the four step sets from the masters, resampled in linear light with premultiplied
  alpha; every baked length is exactly the master length times the step; two runs give identical bytes; each set has
  a manifest with sizes and hashes.
- **SWI-RUN-2** The runtime takes the step from SWI-LAY-4 and composes the layers of each part (board, hardware,
  plates, glyphs, states).
- **SWI-RUN-3** A missing part falls back to the generated chrome
  ([ARC-RES-122](../rendering/hd-interface-density.md)), so the HD option always draws a complete interface.
- **SWI-RUN-4** With the HD option off the output is byte-identical to today.
- **SWI-RUN-5** Only the backdrop of the open window is resident (a ×1 backdrop is about 25.8 MB as raw RGBA); the
  board and the HUD hardware stay loaded.

## Provenance and storage

- **SWI-PRV-1** Owner decision 2026-11-16 ([#2448](https://github.com/Simarglok/arcanum-steamwoven/issues/2448)): original art is given to
  Codex as input, so every generated image, master and baked set is derived from the original: it stays under the ignored
  `local/steamwoven-ui/`, is never committed and never packaged, and follows the
  [HD pack workflow](../workflow/hd-pack.md) for backup. In git: tools, the manifest (identifiers only), the prompt
  library (text) and notes with numbers.
- **SWI-PRV-2** The kit therefore exists only on the owner's devices (and those of a player who builds it from their own
  installation), like the HD pack; it is not shipped with the game. Reopening this needs a new owner decision.
- **SWI-PRV-3** Composites for the owner are posted in the thread and never committed (owner rule 2026-11-11).

## Verification

Agent-checkable (acceptance criteria of the slices):

- **SWI-VER-1** Layout model unit tests reproduce the [reference table](#screen-frame-and-scale) exactly.
- **SWI-VER-2** The manifest validator passes on the real manifest and fails each synthetic bad manifest.
- **SWI-VER-3** Fit tool tests on synthetic fixtures: exact output size; no halo (in the semi-transparent edge band the
  mean colour difference to the nearest opaque interior is within the bound the tool documents); the colour lock
  leaves the high-frequency energy within 10 % of the input.
- **SWI-VER-4** Bake: exact sizes and identical bytes on a second run.
- **SWI-VER-5** Surfaces: seam ratio R ≤ 1.5 at every join (the measure of
  [ARC-RES-094 U4](../rendering/hd-interface-art.md#9-slice-side-pieces-u4)); side-versus-centre colour and texture
  statistics within the bounds that [#2457](https://github.com/Simarglok/arcanum-steamwoven/issues/2457) fixes; until
  then only the seam ratio is a gate.
- **SWI-VER-6** Smoke captures (local only) at 3840 × 2160, 2560 × 1440, 1920 × 1080, 2560 × 1600, 1920 × 1200,
  1280 × 800, 1366 × 768, 3440 × 1440 and 1024 × 768.
- **SWI-VER-7** Byte-identical output with the HD option off (SWI-RUN-4).

Owner gates (separate issues, never acceptance criteria): proportions
[#2453](https://github.com/Simarglok/arcanum-steamwoven/issues/2453), material sheet
[#2456](https://github.com/Simarglok/arcanum-steamwoven/issues/2456), surface method
[#2458](https://github.com/Simarglok/arcanum-steamwoven/issues/2458), and the look of each production batch (named by
its issue).

## Work breakdown

One issue is one work item ([Definition of Ready](../workflow/tasks.md#definition-of-ready)); the issues carry the
acceptance criteria. Start any issue that is `status:ready`; the order below is the dependency order.

| Phase | Issue | Result | Blocked by |
| --- | --- | --- | --- |
| 0. Decisions | [#2447](https://github.com/Simarglok/arcanum-steamwoven/issues/2447) | this TRD and the ADR | — |
| 0. Decisions | [#2448](https://github.com/Simarglok/arcanum-steamwoven/issues/2448) | owner: provenance and distribution — decided: originals are Codex inputs, kit local only | — |
| 0. Decisions | [#2449](https://github.com/Simarglok/arcanum-steamwoven/issues/2449) | Codex runner — decided: the owner runs Codex (closed, not planned) | — |
| 1. Geometry | [#2450](https://github.com/Simarglok/arcanum-steamwoven/issues/2450) | layout model (SWI-LAY, SWI-BDR) | — |
| 1. Geometry | [#2452](https://github.com/Simarglok/arcanum-steamwoven/issues/2452) | prototype with generated chrome ([done](#prototype-of-the-play-screen-arc-ui-2452)) | #2450 |
| 1. Geometry | [#2453](https://github.com/Simarglok/arcanum-steamwoven/issues/2453) | owner: panel proportions — decided 2026-11-17: 120 / 456 | #2452 |
| 2. Census | [#2451](https://github.com/Simarglok/arcanum-steamwoven/issues/2451) | census, families, production issues | — |
| 2. Census | [#2454](https://github.com/Simarglok/arcanum-steamwoven/issues/2454) | kit manifest and validator | #2451 |
| 3. Style | [#2455](https://github.com/Simarglok/arcanum-steamwoven/issues/2455) | context images, material sheet, prompts | #2451 |
| 3. Style | [#2456](https://github.com/Simarglok/arcanum-steamwoven/issues/2456) | owner: material sheet | #2455 |
| 3. Style | [#2457](https://github.com/Simarglok/arcanum-steamwoven/issues/2457) | 4K surface method comparison | #2456 |
| 3. Style | [#2458](https://github.com/Simarglok/arcanum-steamwoven/issues/2458) | owner: surface method | #2457 |
| 4. Tools | [#2459](https://github.com/Simarglok/arcanum-steamwoven/issues/2459) | fit tool | #2454 |
| 4. Tools | [#2460](https://github.com/Simarglok/arcanum-steamwoven/issues/2460) | bake tool and storage | #2454 |
| 5. Runtime | [#2461](https://github.com/Simarglok/arcanum-steamwoven/issues/2461) | runtime composition, window integration issues | #2450, #2460 |
| 5. Runtime | [#2462](https://github.com/Simarglok/arcanum-steamwoven/issues/2462) | fonts at the four steps | #2450, #2451 |
| 6. Production | [#2472](https://github.com/Simarglok/arcanum-steamwoven/issues/2472) to [#2487](https://github.com/Simarglok/arcanum-steamwoven/issues/2487) (created by #2451, [batch table](steamwoven-interface-census.md#production-batches)) | one batch per family or part group | fit, bake, material sheet (backdrops also the surface method) |
| 6. Integration | created by #2461 | one issue per window | runtime, its batch |

Ready now: #2450; the census (#2451) is done and unblocks #2454 and #2455 (#2462 still waits for #2450), and follow-ups #2489 (capture hooks) and #2490 (open facts).

## Open questions

- The player's `uiScale` setting (50–300 %) in the Steamwoven look: ignored (the interface is proportional by design)
  or a multiplier on `f` with the run-time scale absorbing it.
- Screens above ×1 (5K): scale the ×1 set up (current requirement) or add a ×4/3 set.
- Vertical place of the window zone in a taller (16:10) middle band: centred (current requirement) or right under the
  top panel as in the original.

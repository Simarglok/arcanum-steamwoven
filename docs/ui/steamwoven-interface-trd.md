# TRD: Steamwoven interface — layout, kit, pipeline and work breakdown

Status: **requirements, rewritten 2026-11-19** (work item ARC-DOC-2893, [#2893](https://github.com/Simarglok/arcanum-steamwoven/issues/2893)) after the owner's verdict on the column check ([#2892](https://github.com/Simarglok/arcanum-steamwoven/issues/2892)); first written 2026-11-16 (ARC-UI-2447). The decision and its reasons are in
the [ADR](steamwoven-interface-adr.md); this page says what to build and how it is checked. Epic:
[#2446](https://github.com/Simarglok/arcanum-steamwoven/issues/2446). Every requirement has an id `SWI-<area>-<n>`;
issues and PRs cite the ids. Values marked *provisional* are fixed by the named owner decision. **State at 2026-11-19:** the column model of the layout is merged ([#2889](https://github.com/Simarglok/arcanum-steamwoven/issues/2889)); the viewer draws the column in
[#2890](https://github.com/Simarglok/arcanum-steamwoven/issues/2890) (play screen and windows; by owner decision it already carries the 4:3 scale rule of SWI-LAY-1..3, and its PR lists which points of [#2925](https://github.com/Simarglok/arcanum-steamwoven/issues/2925) it covers) and [#2891](https://github.com/Simarglok/arcanum-steamwoven/issues/2891) (full-screen menus); #2925 completes the rule (the follower group at the window's edge and the points #2890 leaves);
[#2936](https://github.com/Simarglok/arcanum-steamwoven/issues/2936) puts the Original look in the same composition; [#2926](https://github.com/Simarglok/arcanum-steamwoven/issues/2926) removes the full-width layout. The kit, its tools and the runtime composition are not built. The rules below are the target; the [superseded rules](#superseded-rules-of-2026-11-16) are what the 2026-11-16 text said.

## Terms

| Term | Meaning |
| --- | --- |
| Composition | The one centred 4:3 block of the Steamwoven interface: the original 800 × 600 units, top strip 41, window zone (or the world) 400, bottom strip 159. |
| Master | A part as authored for the master composition of 2880 × 2160 px, which is the 3840 × 2160 reference screen without its 480 px side areas (the sides show the world or black). Measured in master pixels (px). |
| Step | One of the four baked sets: ×1, ×2/3, ×1/2, ×1/3 of the masters. |
| `s` | Device pixels per UI unit for the current window (fractional). |
| `f` | Device pixels per master pixel for the current window: `f = s / 3.6`. |
| UI unit | One pixel of the original 800 × 600 layout. In the master, 1 unit = 3.6 px. |
| Strip | The top strip (800 × 41 units) or the bottom strip (800 × 159 units) of the composition. |
| Window zone | The 800 × 400 units between the strips: the world on the play screen, a menu window when one is open. |
| Sides | The parts of the window left and right of the composition (above and below it when the window is narrower than 4:3). They show the world or black, never wood. |
| Family | A group of menus whose backgrounds share one material and so one window-background set. |
| Original look / Steamwoven look | The interface with the HD option off / on ([ADR](steamwoven-interface-adr.md#context)). Both use the one composition ([Original look in the composition](#original-look-in-the-composition)); they differ in art, in what the sides show and in the world's scale. |

## Screen frame and scale

- **SWI-LAY-1** The Steamwoven layout is the composition scaled to the window. For a window of W × H device pixels,
  `s = min(W / 800, H / 600)` (fractional) and `f = s / 3.6 = min(W / 2880, H / 2160)`. At the 3840 × 2160 reference `s` = 3.6, `f` = 1 and the composition is the master composition, 2880 × 2160 px.
- **SWI-LAY-2** Every aspect is supported by scaling the one centred composition; there is no other layout. 4:3 is native: the composition fills the window and there are no sides. A wider window (16:10, 16:9, 21:9, 32:9)
  is height-limited: the composition has the window's full height and the sides are left and right of it. A narrower window (5:4 and below) is width-limited: the composition has the window's full width, centred, with black above and below.
- **SWI-LAY-3** The composition is `800 · s` × `600 · s` px, centred; its left, right, top and bottom edges are each the `round` of their own exact position (halves up), so its device size is the difference of two rounded edges (2560 × 1600: 213 and 2347, a width of 2134) and the sides are `round` of their own exact edge too. The sides show **the world** on the play screen and in windows opened over it (the world is drawn to the window's edges, not
  cropped to the composition, and is dimmed and desaturated while a window is open), and **black** beside full-screen menus and two-strip menus; the strip above and below a width-limited composition is always black. Nothing is drawn as wood in a gap: there are no gap rectangles, no frame bars at 16:10 and wider, and no
  pillarbox at 21:9 and 32:9.
- **SWI-LAY-4** The baked step is the smallest of ×1, ×2/3, ×1/2, ×1/3 that is ≥ `f`. Above ×1 (`f` > 1: 5K screens, 3840 × 2400) the ×1
  set is scaled up (a recorded limit, see [open questions](#open-questions)). Below ×1/3 the ×1/3 set is scaled down
  to `f` = 0.25; below 0.25 (a window narrower than 720 px at 4:3) the Steamwoven interface is not offered and the Original look, which has its own composition scale (SWI-ORG-1), is used.
- **SWI-LAY-5** The run-time scale is `f / step`. `f`, the step choice and the test for an exact step are computed
  exactly (integers or rationals; for a width-limited window `f` is exactly the step 2/3 when `3 · W = 2 · 2880` and is at most 2/3 when `3 · W ≤ 2 · 2880`), never with floating-point equality. When it is exactly 1 the parts are drawn at whole device pixels with
  no filtering; otherwise with linear filtering from the baked set.
- **SWI-LAY-6** W × H are device (backing) pixels, including the macOS backing scale.
- **SWI-LAY-7** Every kit master length and offset is a multiple of 6 px, so it is a whole number of pixels at every step
  (6 px = 4, 3 and 2 px); the strip heights are the open exception ([open questions](#open-questions)). A device position is `round(master position · f)`, computed per edge (not per size), so
  neighbouring parts share their edges; halves round up. The two strips are the exception: each is `round(units · s)` high from the composition's own top or bottom edge (41 and 159 units) and the window zone is what is left
  between them, so a strip never loses a pixel to a neighbour's rounding.

Reference table of the target model (exact numbers; "sides" and "black" are the widths left / right and above / below the composition). The layout model's unit tests use these numbers ([#2925](https://github.com/Simarglok/arcanum-steamwoven/issues/2925), SWI-VER-1):

| Window | `s` | `f` | Step | Run-time scale | Composition | Top strip | Window zone | Bottom strip | Sides | Black |
| --- | ---: | ---: | --- | ---: | --- | ---: | ---: | ---: | --- | --- |
| 3840 × 2160 | 3.6000 | 1.0000 | 1 | 1.000 | 2880 × 2160 | 148 | 1440 | 572 | 480 / 480 | 0 / 0 |
| 2560 × 1440 | 2.4000 | 0.6667 | 2/3 | 1.000 | 1920 × 1440 | 98 | 960 | 382 | 320 / 320 | 0 / 0 |
| 1920 × 1080 | 1.8000 | 0.5000 | 1/2 | 1.000 | 1440 × 1080 | 74 | 720 | 286 | 240 / 240 | 0 / 0 |
| 1280 × 720 | 1.2000 | 0.3333 | 1/3 | 1.000 | 960 × 720 | 49 | 480 | 191 | 160 / 160 | 0 / 0 |
| 3840 × 2400 | 4.0000 | 1.1111 | 1 | 1.111 | 3200 × 2400 | 164 | 1600 | 636 | 320 / 320 | 0 / 0 |
| 2560 × 1600 | 2.6667 | 0.7407 | 1 | 0.741 | 2134 × 1600 | 109 | 1067 | 424 | 213 / 213 | 0 / 0 |
| 1920 × 1200 | 2.0000 | 0.5556 | 2/3 | 0.833 | 1600 × 1200 | 82 | 800 | 318 | 160 / 160 | 0 / 0 |
| 1280 × 800 | 1.3333 | 0.3704 | 1/2 | 0.741 | 1066 × 800 | 55 | 533 | 212 | 107 / 107 | 0 / 0 |
| 3024 × 1964 | 3.2733 | 0.9093 | 1 | 0.909 | 2618 × 1964 | 134 | 1310 | 520 | 203 / 203 | 0 / 0 |
| 3456 × 2234 | 3.7233 | 1.0343 | 1 | 1.034 | 2978 × 2234 | 153 | 1489 | 592 | 239 / 239 | 0 / 0 |
| 2880 × 1800 | 3.0000 | 0.8333 | 1 | 0.833 | 2400 × 1800 | 123 | 1200 | 477 | 240 / 240 | 0 / 0 |
| 1366 × 768 | 1.2800 | 0.3556 | 1/2 | 0.711 | 1024 × 768 | 52 | 512 | 204 | 171 / 171 | 0 / 0 |
| 1600 × 900 | 1.5000 | 0.4167 | 1/2 | 0.833 | 1200 × 900 | 62 | 599 | 239 | 200 / 200 | 0 / 0 |
| 1680 × 945 | 1.5750 | 0.4375 | 1/2 | 0.875 | 1260 × 945 | 65 | 630 | 250 | 210 / 210 | 0 / 0 |
| 3440 × 1440 | 2.4000 | 0.6667 | 2/3 | 1.000 | 1920 × 1440 | 98 | 960 | 382 | 760 / 760 | 0 / 0 |
| 5120 × 1440 | 2.4000 | 0.6667 | 2/3 | 1.000 | 1920 × 1440 | 98 | 960 | 382 | 1600 / 1600 | 0 / 0 |
| 1024 × 768 | 1.2800 | 0.3556 | 1/2 | 0.711 | 1024 × 768 | 52 | 512 | 204 | 0 / 0 | 0 / 0 |
| 1280 × 1024 | 1.6000 | 0.4444 | 1/2 | 0.889 | 1280 × 960 | 66 | 640 | 254 | 0 / 0 | 32 / 32 |
| 5120 × 2880 | 4.8000 | 1.3333 | 1 | 1.333 | 3840 × 2880 | 197 | 1920 | 763 | 640 / 640 | 0 / 0 |

The composition's edges are rounded one by one (SWI-LAY-3), so the sides of an odd leftover are equal (2560 × 1600: 213 / 213, a 2134 px composition). The strip columns are the strips' device heights by SWI-LAY-7 (`round(41 · s)`, `round(159 · s)`); the window zone is the rest, so it is 400 · `s` to within a pixel (1600 × 900: 599). The two MacBook rows (3024 × 1964, 3456 × 2234) are 16:10-like and scale like any other
height-limited window; in macOS full screen the area beside the camera notch is usually left out, which may change the window's size (to check on the device, not a requirement).

Why every aspect: in the Steam Hardware Survey of October 2026 (primary display, read 2026-11-16 from
[store.steampowered.com/hwsurvey](https://store.steampowered.com/hwsurvey/Steam-Hardware-Software-Survey-Welcome-to-Steam)) 1920 × 1080 is 50.23 %, 2560 × 1440 21.82 %, 2560 × 1600 5.46 %, 3840 × 2160 5.05 %, 3440 × 1440 3.23 %,
1920 × 1200 2.97 %, 1366 × 768 2.25 %; 16:10 resolutions together about 11 %; 2560 × 1080, 3440 × 1440 and
5120 × 1440 together about 4.3 %; no 4:3 resolution is listed (all below the 3.30 % "Other"). Scaling one composition costs nothing per aspect, so the 2026-11-16 rule of a frame with bars for narrower and wider screens is dropped (owner verdict 2026-11-19).

## Play screen layout

- **SWI-LAY-10** The composition's three bands are the original's: top strip 41 units, window zone 400, bottom strip 159 (master 147.6, 1440 and 572.4 px, which are off the 6 px grid: [open question](#open-questions); device heights by SWI-LAY-7). Only this pair (k = 1) exists: the thin pair of
  120 / 456 master px (owner decision 2026-11-17, [#2453](https://github.com/Simarglok/arcanum-steamwoven/issues/2453)) and `k` = 0.8 are retired, because the strips and an open window together are exactly 4:3 at the original proportions and the composition fills the height.
- **SWI-LAY-11** The HUD is the original layout at `s`: every control sits at its original unit position and size × `s`, the HUD text (message window, clock, counters) is drawn at the same scale, and nothing is re-laid by a layout
  model. The strips are composition-wide (800 units). Elements that stand at an edge over the world (the spell pane, the combat list, text bubbles and floaters) are placed against the window's edges, not against the composition; the follower group has its own rule, SWI-LAY-16.
- **SWI-LAY-12** A strip is one image (the strip art) plus hardware layers over it (sockets, rings, vial housings, the message window frame, rails, buttons; hardware is a separate layer, not baked into the strip). There is **no board and no sides**:
  beside the composition the world shows. Each strip's outer ends carry an **end cap** (a brass edge and a soft shadow on the world, kit kind `cap`), so the strip does not end in a bare cut.
- **SWI-LAY-13** Hit areas are the rectangles (or circles) of the drawn parts from the layout, not the original's
  pixel masks.
- **SWI-LAY-14** With no menu open the window zone is the world view, and the world is visible at the sides to the window's edges (at the scale of SWI-LAY-17). The out-of-sight rules follow the owner decision
  of 2026-11-07 ([decisions](../architecture/decisions.md), "Out-of-sight rules and the view": Steamwoven tests the
  drawn view); how wide the drawn world may be on an ultrawide window is an [open question](#open-questions).
- **SWI-LAY-15** No text is baked into any part; all text is drawn with fonts
  ([#2462](https://github.com/Simarglok/arcanum-steamwoven/issues/2462)): the original's fonts (HD frames in HD), sizes per step in the
  [font note](steamwoven-interface-fonts.md).
- **SWI-LAY-16** The follower group moves as one: the portraits, the scroll arrows and the show/hide button (FP1: the button 67 × 13 units at (0, 428), the arrows at (0, 415) and (33, 415), the portraits at x 11, y 50 + 62 per slot) keep their original heights at the composition's scale `s`
  and stand at the **window's** left edge, over the world, instead of the composition's. The button stands on a small brass backing of its own (a kit part of the strips batch). At 4:3 the group is identical to the original ([#2925](https://github.com/Simarglok/arcanum-steamwoven/issues/2925)).

- **SWI-LAY-17** **World zoom (owner decision 2026-11-19, [#2937](https://github.com/Simarglok/arcanum-steamwoven/issues/2937)).** The world zoom stays a **player setting for now**: the existing in-game zoom option is kept and not changed by this layout. The **working reference look is the composition's scale `s`** (option B of the issue; the default is not fixed, see below): the world scales with the interface, the play view inside the window zone shows the same area as the original (800 × 400 units), the sides add only what lies beyond it, and sprites match the interface's size. Not chosen as the reference: world pixels at 1× (A, much more world on a large screen, small sprites). The HD sprites question stays closed (world frames 1×, decision 2026-11-15). The Original look draws its world at `s` only inside the composition (SWI-ORG-3). **To revisit:** by the owner's report (to verify in the viewer) the game runs today with a 200 % world zoom at 2560 × 1440 in full screen and the owner is fully happy with it; the composition's `s` there is 2.4 (240 %). The final default (the setting's default and its steps relative to `s`) is decided with the owner later.

## Superseded rules of 2026-11-16

Owner verdict 2026-11-19 ([#2892](https://github.com/Simarglok/arcanum-steamwoven/issues/2892)): the full-width layout read as a letterboxed film and the column's wood in vertical gaps "looks poor", so the layout above replaces these rules. Their ids are reused by the rules above or by [Window backgrounds and plates](#window-backgrounds-and-plates); code comments that cite the old meanings are removed with the full-width code ([#2926](https://github.com/Simarglok/arcanum-steamwoven/issues/2926)).

| Old rule (2026-11-16 / 2026-11-17 / 2026-11-18) | Now |
| --- | --- |
| SWI-LAY-1..3: `f = min(W / 3840, H / 2160)`, a frame of 16:10 to 16:9, a 16:10 frame with bars for narrower windows, a 16:9 frame with bars (pillarbox) for 21:9 and 32:9, bars under 4 px dropped | SWI-LAY-1..3: `s = min(W / 800, H / 600)`, one composition at every aspect, world or black at the sides |
| the reference table of the 16:10 / 16:9 frames (bands 120 / 456, centre block 2880, sides, bars) | the reference table above |
| SWI-LAY-10: thin strips 120 and 456 master px (and the prototype's `k` = 0.8 centre block) | SWI-LAY-10: the original 41 / 400 / 159 units, `k` = 1 |
| SWI-LAY-11: panels span the whole frame, the sides continue the board, variant A (`k` = 0.8, 2304 px block) or variant B (controls placed by the layout model in the bands) | SWI-LAY-11: the original layout at `s`; no layout-model placement of controls |
| SWI-LAY-12: one continuous board for both panels, the sides show the same board without a join | SWI-LAY-12: strip art, no board, no sides, end caps |
| SWI-BDR-1: a menu's backdrop fills the whole middle band; SWI-BDR-3: a 3840 × 1824 backdrop master with crops and cover scaling; SWI-BDR-5: halves continue outwards to the frame's edges; SWI-BDR-6: a 3840 × 2400 full-screen master covering the panels; SWI-BDR-7: sides continued outwards without a join | SWI-BDR-1..8 below: window backgrounds of the zone's size, full-screen plates of the composition's size, nothing outward |
| kit kinds: a full-width `board` (one image for both panels); a `backdrop` that continues outwards | kit kinds below: `board` is the strip art, `backdrop` a window background or a plate, `cap` is new |
| the open question of the window zone's vertical place on 16:10 | closed: the composition is exactly 4:3, the zone is directly under the top strip |

## Prototype of the play screen (ARC-UI-2452)

> **Historical record of the full-width prototype (2026-11-16 to 2026-11-18), superseded 2026-11-19.** The strips' boards and the 120 / 456 panel pair, the `k` = 0.8 stand-in scale, the side boards, the per-family backdrop pieces over the whole middle band and the full-screen menu backdrop over the whole frame
> are replaced by the [composition](#screen-frame-and-scale) and [Window backgrounds and plates](#window-backgrounds-and-plates); the code goes in [#2926](https://github.com/Simarglok/arcanum-steamwoven/issues/2926), which also trims this section. What stays valid: windows stand in the window zone at the unit scale (now `s`, SWI-BDR-1..2),
> the one wood of a family and its tone rules (ARC-UI-2763), the one lens place (#2714), the seam gate of SWI-VER-5, the strips' painted-at-scale check and the generated chrome as the placeholder and fallback (SWI-RUN-3).


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
  (132 + 668 units) at the zone's left edge plus their left part at the unit scale. The parts tile the middle band (the frame's width) and meet at the split. Each piece's wood is tuned to the tone of the window art it continues (ARC-UI-2643, `SteamwovenBackdrops.ToneArt`: the inventory 221 for both halves of the inventory family, the sheet 22, the logbook's side 264 and book 260, the reading side bar 494, the map frame 138, the schematic base 365; read with the bars' arts, the bottom strip's tone where one is missing), the way a board takes its strip's. **The inventory family is one wood (ARC-UI-2763, owner report 2026-11-18):** the original's arts of BD-A have woods of slightly different tones (the doll 223 darker than the pack 221, the loot doll lighter), which showed as a tone seam at the split and a differently toned box around the worn items. Every window art of the family (`inventor.art`, `pdoll.art`, `loot.art`, `barter.art`, `barter_follower.art`, `loot_pd.art`, `barter_pd.art`, `pd_cover4inven.art` and the lenses 231 to 233, whose wood corners lie in these windows (231 is the reading window's lens too); recipe flag `FamilyWood`) takes one wood, `InterfaceChromeRenderer.FamilyWoodLow/High` (the pack art's, the middle one of the family), both backdrop pieces are tuned to the same tone, the pack art's grain starts at the split's 358 units (`InterfaceChromeRenderer.SteamwovenFamilySplitUnits`) and the backdrop's right piece continues the left piece's grain from the zone's left edge; the logbook's and the reading window's halves keep their own grain;
  the schematic window's parchment page and the logbook's book lie on the wood, so BD-F is wood at the zone's edges. The two-strip menus (BD-G) and the full-screen menus (BD-H) hide the bars and are laid out over the whole frame by [#2577](https://github.com/Simarglok/arcanum-steamwoven/issues/2577). The smoke
  (`SteamwovenBackdrops`, `CheckBackdrop`) checks the family, the pieces' rectangles and texels, and the seam ratio R of SWI-VER-5 (`SteamwovenSeam`: the difference of neighbouring pixels in a ±8 px band at the join over the one in a band beside it on the backdrop, inside the
  middle band; each taken at the cleanest twentieth of the edge's rows or columns, because parts of the window that lie at its edge, such as the character sheet's alignment scale, only raise it and a real seam shows on every line) at the edges of the window zone (left, right and top gated, R ≤ 1.5; the bottom edge is printed only, because the windows' own look switch, a wide bordered button, lies just below it): the highest of the nine SWI-VER-6 sizes is 1.20. The split of a two-part window is a designed join of two arts
  (the logbook's and reading window's halves start their grain at their own left edge; the inventory family's grain and wood are one, ARC-UI-2763) and is exempt from R; the smoke prints it above the zone (0.5 to 1.7). In the Steamwoven layout the barter, identify and repair and reading windows keep the bars (`GameBarActive`; the original hides them), so that every menu window of the middle band has its backdrop; the main menu, the save list and the palette still hide them. The round-view lens of a window (the ring at the top left of the logbook, schematics, reading, inventory and barter windows, ARC-UI-2645) copies the game view on layer 2, under the backdrop (layer 3), which leaves the lens's square out (`SteamwovenBoardView.SetHoles`, `AreaRoundView.VisibleRects`; the smoke checks the count). The character sheet has the live lens too ([#2694](https://github.com/Simarglok/arcanum-steamwoven/issues/2694): the sheet's open turns the view-window mode 1 on with art 198, CHW9, so the medallion of art 22 is under it and the square (12, 10, 89) is cut out for the round view and the cover art 198 drawn over it), and the world map's window frame draws no ring of its own outside the lens circle (the cover art's ring inside its square is the only one). The smoke clicks the lens of the sheet and the map (a mouse-up in it closes the window) and prints each lens's place in units from the zone corner. **One lens place (owner decision 2026-11-18, [#2714](https://github.com/Simarglok/arcanum-steamwoven/issues/2714)):** the original's arts put the 89-unit lens at five places (inventory 11, 9; sheet 12, 10; loot and barter 16, 17; logbook, map and reading 25, 24; schematics 50, 26), which made it jump when one window replaced another. With the generated interface every window's lens stands at **11, 9** (`AreaLensPlace`, set with the interface setting in `InterfaceHd.Configure`; the Original look keeps each window's own place). The point is the inventory window's own, because the inventory's item grid starts at y 107 right under the lens (a lens any lower, the median 16, 17 among them, would cut into the grid or move the grid and the doll down); every other lens only moves toward the corner, into wood that holds nothing (sheet 1, 1; loot and barter 5, 8; logbook, map and reading 14, 15; schematics 39, 17), so no window's content moves. The generated art of each window carries the hole there: a recipe's `ChromeLensFrom` names the Legacy frame's place and `InterfaceChromeRenderer.Render` moves that box (the lens's 89 units and 5 around them, with the HD picture) to the common place before painting and closes the old place with the frame's own wood; the views (`AreaLogbookArtLayout.Lens` and the like, `AreaInventoryHole.Corner`, `AreaSchematicWindowLayout.RoundView`, the sheet's close rectangle) read the place through `AreaLensPlace.Corner`. The reading window stands in the window zone like the others (its top at the zone body's top; it stood on the 800 × 600 screen's body, up to 12.5 units above the zone at 16:9), so its lens is at the same place to the pixel. The smoke requires every lens at 11, 9 within a device pixel (`TerrainViewerSteamwovenSmoke`, the `lens` print). The old backdrop was `WoodDark`; the family backdrops are the wood of their window's own tone, so an open menu is lighter than before and its seam to the window is not a step.
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
  viewport takes the size as its logical render target and the window is given the size's width (ARC-UI-2696: the content scale mode Viewport draws the target at the window's size, so a narrower window, the default 1280 wide for a 2560 target, has a stretch of 0.5 and every generated texture, the strips' and the windows' wood, is painted at that stretch, clamped to one pixel per unit, and drawn up again, softer than the boards, which are painted at the target's scale: the captures showed two woods; the height is the operating system's to limit and does not matter, the paint reads the stretch across; the smoke checks that the strips are painted at the scale they are drawn at; it also checks ([#2767](https://github.com/Simarglok/arcanum-steamwoven/issues/2767)) that the Fate Points and Sleep balls stand in the middle of the brass ring the strip draws round their hole (the Legacy hole's centre, `AreaBarRingFit`; the original's places were a pixel off it); `ARC_STEAMWOVEN_FIT_WINDOW=0` keeps the old window for a comparison, the check then only prints), and saves `<prefix>-hud-steamwoven-VARIANT-WxH[-menu].png` (local, never in git). The older generated-interface smokes run with the layout
  suspended (`SteamwovenHud.Suspended`): they measure the whole-factor geometry.

## Original look in the composition

Owner decision 2026-11-19 ([#2936](https://github.com/Simarglok/arcanum-steamwoven/issues/2936)): the Original look (HD option off, Legacy art) uses the same layout as the Steamwoven look, instead of the whole-factor strips and the fitted windows of today (inconsistent in the 2026-11-18 comparison: tiny strips at 1920 × 1080, large windows). This supersedes "Original is untouched" of the 2026-11-16 ADR.

- **SWI-ORG-1** The original 800 × 600 screen is one centred composition scaled with `s = min(W / 800, H / 600)`, as SWI-LAY-1..2: strips, windows, full-screen menus and the follower group at their original places inside it.
- **SWI-ORG-2** Everything outside the composition is **black** at every aspect, never the world.
- **SWI-ORG-3** The world is drawn at the composition's scale, so the play view shows exactly the original 800 × 400 area, and the out-of-sight rules keep the fixed 800 × 400 view (decision 2026-11-07): a wider drawn world would show spawns at the edges.
- **SWI-ORG-4** Legacy art is scaled with sharp pixel scaling at fractional `s`: nearest to the next whole factor, then a smooth downscale to `s` (or an equivalent shader), so pixels stay crisp and even; at whole `s` the output equals nearest scaling.
- **SWI-ORG-5** Hit testing follows the drawn composition; the Steamwoven look is unchanged by this requirement. The smoke `legacy` variant ([#2672](https://github.com/Simarglok/arcanum-steamwoven/issues/2672)) captures it.

### The Original look in the viewer (ARC-UI-2936)

Issue [#2936](https://github.com/Simarglok/arcanum-steamwoven/issues/2936). The Godot-free part is `OriginalComposition` (`tools/terrain-viewer/Core`): the scale `s`, the whole factor `n = ceil(s)`, the virtual screen `800 n × 600 n`, the composition's rectangle (the rounding of `SteamwovenColumnMenu.Fit`), the black rectangles, the pointer mapping, the camera's zoom factor and the fixed out-of-sight window; `OriginalCompositionTests` carry them (16:9, 16:10, 21:9, 4:3, 5:4, narrower than 4:3).

- **Mechanism (SWI-ORG-1, SWI-ORG-4).** While the HD option is off, no launcher screen is open and the run is not an automated one, the viewer's window gets the content scale `Viewport`, aspect `Keep`, size `800 n × 600 n` (`TerrainViewerApp.SyncOriginalComposition`, each frame, so a resized window follows). The whole interface and the world are then laid out in that virtual screen by the existing whole-factor rules at `n` (the strips at `n`, the windows and menus fitted to a virtual screen of exactly 800 × 600 units at `n`, the follower group and bubbles at their places), Godot draws the picture centred, reduces it smoothly (bilinear) to `s` and leaves the rest black (SWI-ORG-2), and maps the pointer back (SWI-ORG-5). At a whole `s` (`n = s`) there is no reduction and the picture is the nearest-pixel one. The launcher, the Steamwoven look (HD option on) and automated runs keep the window's own pixels.
- **World (SWI-ORG-3).** The camera's zoom is the player's setting times `n / 2` (`TerrainCameraController.ZoomScale`): the setting's default 2 shows exactly 800 × 400 world pixels between the strips at every `s`, and the in-game zoom option keeps working from there ([#2937](https://github.com/Simarglok/arcanum-steamwoven/issues/2937): the setting stays). In the Original look the generators' and the restock's out-of-sight test and `WindowSize` are the fixed 800 × 400 centred on the view's centre (`AreaGameWindow`).
- **Smoke.** The `legacy` pass of the bar smoke (`./arcanum smoke pr --stages bar --env ARC_AREA_BAR_STEAMWOVEN=1 --env ARC_HD_PACK=<pack> --env ARC_STEAMWOVEN_VARIANTS=legacy [--env ARC_STEAMWOVEN_LEGACY_SIZES=WxH,...] [--env ARC_STEAMWOVEN_LEGACY_BEFORE=1] --env ARC_AREA_BAR_CAPTURE=<prefix>`) lays the viewer out for each size as above (the window cannot always be the size of a 4K target, so the composition takes the target as its size) and checks the HD option off, the whole factor, the strips at `n` with the original's insets, `400 n` of play view between them, the camera's zoom and the 800 × 400 out-of-sight window, and at a whole `s` that the Legacy strips are drawn with nearest pixels (every `n × n` block uniform). The captures are the virtual screen reduced to `s` on black, composed in software (what the window shows); `ARC_STEAMWOVEN_LEGACY_BEFORE=1` also saves the old layout as `-hud-steamwoven-legacy-before-WxH` for the owner's before / after composite. Default sizes: 3840 × 2160, 2560 × 1440, 1920 × 1080, 2560 × 1600, 1280 × 800, 3440 × 1440, 1024 × 768, 1280 × 1024, and the whole scales 1600 × 1200 and 3200 × 1800 for the pixel check.
- **Play view and map edge.** The camera's focus stands at the middle of the 400-unit play view (`ViewShift`, the camera's offset), not at the virtual screen's middle, and the map-edge clamp takes the play view's height (`PlayHeight`), so the hero can reach the map's edge inside the play view. A press on the black sides (outside the virtual screen) is dropped before it reaches the world (SWI-ORG-5); a release is not. **Open:** that Godot's final scaling of the virtual screen to the window is smooth (not nearest) at a fractional `s` is assumed from the Viewport stretch mode and is not read from real window pixels (a capture of the real window is unavailable to automated runs); the captures are composed in software.

## Window backgrounds and plates

Rewritten 2026-11-19 (it was "Menu backdrops": a backdrop filling the whole middle band, continued outwards). Nothing is drawn beyond the composition except the world (play screen and in-play windows) or black.

- **SWI-BDR-1** A menu window of the middle band stands in the window zone of the composition; the world beside it (the sides) is dimmed and desaturated while it is open (presentation only; the values are in the layout options, [#2890](https://github.com/Simarglok/arcanum-steamwoven/issues/2890)). The dim takes no pointer: input under an open window is as it is today (modal). No backdrop is drawn outside the zone.
- **SWI-BDR-2** The window zone is 800 × 400 units (2880 × 1440 px master) directly under the top strip. Window content (paper doll, grids, pages, buttons) sits in it at the original offsets × `s`; in a master the offsets are × 3.6, snapped to the 6 px grid. The window art fills the zone exactly, so
  there is no gap and no gap wood at any aspect (the composition is 4:3 by construction, which also closes the former question of the zone's vertical place on a 16:10 band).
- **SWI-BDR-3** A window background master is 2880 × 1440 px, the size of the zone: no crop, no cover scaling, no part that lies outside the composition. A two-strip menu (save, load, pre-made characters, new character) has the same 2880 × 1440 background between its own two strips (art 335, 336).
- **SWI-BDR-4** Menus whose original backgrounds share a material share one family. The families are
  fixed by the census ([#2451](https://github.com/Simarglok/arcanum-steamwoven/issues/2451)): eight families (BD-A to BD-H) and the
  strips, listed in the [census](steamwoven-interface-census.md#backdrop-families). Windows of one family keep one wood (the tone rules of the inventory family, ARC-UI-2763, and the one lens place, [#2714](https://github.com/Simarglok/arcanum-steamwoven/issues/2714), stay).
- **SWI-BDR-5** A two-part window (inventory, loot, barter: left 358 units, right 442 units; the logbook 165 + 635 and the reading window 132 + 668, [census](steamwoven-interface-census.md#two-part-windows-and-full-screen-menus)) is two masters inside the zone, split at 1290 px from the zone's left edge
  (358 × 3.6 = 1288.8, snapped). Each half keeps its own material; neither continues outwards.
- **SWI-BDR-6** A full-screen menu (an original 800 × 600 background: main menu, options and the others the census finds) is a **plate**, a 2880 × 2160 master that is the composition itself with the strips hidden. The sides beside it are black.
- **SWI-BDR-7** Each background and plate is made from its own original art. The only joins inside the composition are the two-part split (a designed join of two arts, exempt from the seam gate) and the zone's edges against the strips, which are measured by SWI-VER-5.
- **SWI-BDR-8** The kit has no full-width board, no outward backdrops and no gap wood: they are not kit parts and no batch makes them.

## Layout model and its state

The layout model is `SteamwovenLayout` in `Arcanum.Presentation`, with the column plan `SteamwovenColumnPlan` ([#2889](https://github.com/Simarglok/arcanum-steamwoven/issues/2889), merged 2026-11-19). The viewer draws it in
[#2890](https://github.com/Simarglok/arcanum-steamwoven/issues/2890) and [#2891](https://github.com/Simarglok/arcanum-steamwoven/issues/2891), still under their contract of before the verdict.

- **As merged (#2889):** the column is an option of the model (`SteamwovenLayoutOptions.Column`; presets `ColumnOriginal`, `k` = 1, and `ColumnThin`, `k` = 0.8; `WithWorldToEdges()`); it computes the strip scale `s = 3.6 · k · f`, a column `2880 · k · f` wide inside the 16:10 to 16:9 frame (the frame, bars, step and run-time scale of the 2026-11-16 SWI-LAY-1..7), the window zone and
  full-screen zone centred in the column, `GapAbove` / `GapBelow` rectangles for the window's wood, and `WorldLeft` / `WorldRight`. The full-width members (bands, centre block, split, backdrops, controls) are still the model's default.
- **Target (the scale rule is carried by [#2890](https://github.com/Simarglok/arcanum-steamwoven/issues/2890) for the viewer's `column` choice; [#2925](https://github.com/Simarglok/arcanum-steamwoven/issues/2925) completes it):** the column plan takes SWI-LAY-1..7 and SWI-LAY-16: `s = min(W / 800, H / 600)`, `f = s / 3.6`, the step and run-time scale from `f`, no frame bars, no gap rectangles and no gap wood, the world to the window's edges (dimmed under an open window, SWI-BDR-1), black sides for full-screen and two-strip menus, black above and below a window narrower than 4:3, and the
  `column-thin` preset removed. The unit tests (`SteamwovenColumnTests`) carry the reference table above and 1280 × 1024 (SWI-VER-1).
- **Removal ([#2926](https://github.com/Simarglok/arcanum-steamwoven/issues/2926)):** the full-width plan and its presets, the side boards and the per-family full-middle backdrops of the viewer, the layout option's `full-width` and `column-thin` choices, their smoke variants and checks; the `legacy` capture variant ([#2672](https://github.com/Simarglok/arcanum-steamwoven/issues/2672)) stays. The
  [prototype section](#prototype-of-the-play-screen-arc-ui-2452) above is trimmed to what remains in the same PR.

### Menus in the column (ARC-UI-2891)

Issue [#2891](https://github.com/Simarglok/arcanum-steamwoven/issues/2891). **Owner decision 2026-11-19 (after the first composite of the column plan's full-screen zone):** in column mode every **full-screen menu** (main, single player, new game, in-play, options: `AreaMenuArtView`, `AreaMainMenuPanel`)
and every **two-strip screen** (pre-made, new character, shop and the save and load lists: their 800 × 600 screen, whose 800 × 400 body lies between its own two strips) is **scaled up to fill the window's height from edge to edge**, centred, and everything beside it is **black**.
The column plan's full-screen zone (a plate at `3.6 · k · f` with wood gaps at 16:10 and k = 0.8) is not used by the menus; k makes no difference to them.

- **Plate.** `SteamwovenColumnMenu.Fit(width, height)`: scale `min(width / 800, height / 600)` device px per unit (the whole height from 4:3 and wider; a window narrower than 4:3 is limited by its width and has black above and below), the rectangle centred (the top left rounded from its exact position, halves up; the far edges rounded up, so the black starts after the drawn plate): 1920 × 1080 s = 1.8, the plate 1440 × 1080 at x = 240; 2560 × 1600 s = 8/3, 2134 × 1600 at x = 213; 1920 × 1200 s = 2, 1600 × 1200 at x = 160. The plan is `SteamwovenFullScreen.Plan(w, h, options)` (the plate fitted, the rest of the layout unchanged); the two-strip views take their place from
  `AreaGameBarPanel.TwoStripScreen`, which is `SteamwovenColumnMenu.Place` with the column option on and the window zone's place with it off, as before. The fractional scale is sampled Linear, as in the full-width layout.
- **Black, nothing else.** No wood sides, no vignette, no full-width backdrop, no wood gap (the plate leaves none). `SteamwovenColumnMenu.BlackRects` are the rectangles around the plate (`SteamwovenColumnSidesView`, which also takes the pointer over the whole window: the menu is modal).
- **Hit testing** is the control's own transform, so it follows the drawn plate; `SteamwovenFullScreenPlan.HitTest` is the same mapping as plain numbers (unit-tested; a pointer on the black hits nothing).
- Unchanged: the full-width layout (the option off), HD off (the whole-factor plate and the pack's 16:9 plate), the Credits and View Intro black fills.

Unit tests: `SteamwovenColumnMenuTests` (the plate and the black at the AC5 sizes, 4:3 and a narrower window, tiling of the window by the black and the plate, hit tests, the full-width plan unchanged). Smoke: `./arcanum smoke pr --stages menu --main-menu --env ARC_AREA_MENU_COLUMN=1 [--env ARC_STEAMWOVEN_SIZES=WxH,...] [--env ARC_AREA_MENU_CAPTURE=PATH.png]`
(default sizes 2560 × 1440, 1920 × 1080, 2560 × 1600, 1920 × 1200, 3440 × 1440, 1280 × 800) checks, for the main and options windows and the pre-made and new-character screens, the plate's scale and place against `Fit`, its full height, the black rectangles, no full-width backdrop, the labels' hit tests and the pixels of the black, and saves
`PATH-column-WINDOW-WxH.png`; with the column option off, and with it on but the HD option off, nothing of it shows. The same pass runs on the save list inside the save window's smoke (`SaveListColumnForSmoke`, saved with `ARC_AREA_SAVE_CAPTURE`).

### The fitted column (owner decision 2026-11-19, ARC-UI-2890)

After the first captures the owner liked the column at 16:9 and rejected the wood fill in the gaps at 16:10. The choice `column` is now the **fitted column** (`SteamwovenColumnMode.FitHeight`, `SteamwovenLayoutOptions.ColumnFitted`): the strips and the open window are one 4:3 composition
(41 + 400 + 159 = 600 units of the original's 800 × 600 screen) scaled to the frame's whole height, `u = frame height / 600` device px per unit, a column `800 · u` wide centred in the frame, the world (or black) at the sides. At 16:9 it is the k = 1 column within a pixel or two (`u = 3.6 · f`; the strips are exactly 41 and 159 units, without the sliver and overhang of the 150 and 570 px bands); at 16:10 the
composition is scaled up (`4 · f`), so the window zone fills the middle band exactly and there are **no gaps and no wood fill**. Any aspect ratio is supported by the same rule: the frame is the whole window, or, where the window is taller than 4:3, a 4:3 band of the window's width centred
vertically (black bars above and below, however thin); a wider window shows the world to its edges. The top strip is `round(41 u)` high, the bottom strip ends at the frame's bottom (`159 u`), so strip art and bands coincide (no sliver, no overhang). The HD art is to be generated for 3840 × 2160 and reduced
for the other sizes. `column-thin` (k = 0.8, wood in the gaps) and the unfitted k = 1 column (`column-wood` in the smoke) stay for comparison; the wood pieces of `SteamwovenBackdrops` are drawn only for them. Unit tests: `SteamwovenColumnTests` (the fitted table at eleven sizes).

### The column in the viewer (ARC-UI-2890)

The viewer draws the column for the play screen and the middle-band windows of the #2575 hook; the full-screen menus (BD-H) and the two-strip screens' backdrops are [#2891](https://github.com/Simarglok/arcanum-steamwoven/issues/2891).
The Godot-free parts are `SteamwovenHudPlan.Column` / `WindowZone`, `SteamwovenWindowZone`, `SteamwovenBackdrops.Pieces` and `SteamwovenColumnEdges`; the drawing is `AreaGameBarPanel` with `SteamwovenEdgeView`.

- **Option (AC1).** The game setting `interface layout` (`AreaGameSettings.InterfaceLayout`, the settings line of the same name, a launcher row after the generated interface): `column` (the default while the owner tests the hypothesis), `column-thin` or `full-width`
  (`SteamwovenLayoutChoices`). This run's `ARC_STEAMWOVEN_LAYOUT` (the same three words, for the launcher's `--env`) overrides it. It is read only while the generated interface is on; with the HD option or the generated interface off the whole-factor layout is unchanged.
- **Play screen (AC2).** Both strips stand in the column at `Column.TopStrip` / `BottomStrip` (the top strip on the band's lower edge, as before); no side boards, no bottom board; the only board is the sliver of the strip's wood above the top strip where its 41 units
  are a little lower than the band (as wide as the column). The world is drawn over the whole frame. **End caps** (placeholder art drawn from numbers, `SteamwovenEdgeView`): a brass edge of about one unit (a dark line, a highlight and mid brass, at least 2 px) over the outermost pixels of each strip's
  end and, outside it, a shadow on the world, seven units wide, falling off with the square of the distance, clipped to the frame. While a menu window is open the middle band's ends are capped too.
- **Windows (AC3).** `SteamwovenWindowZone.Body` / `Screen` use `Column.Window.Zone` and the column's unit scale `3.6 · k · f`, so every window of the hook is as wide as the column. The family's wood fills `GapAbove` and `GapBelow` (`SteamwovenBackdrops.Pieces`: one piece per gap, two for a
  two-part family split at the zone's left edge plus the left part's units; none where the zone fills the middle band, 16:9 at k = 1); its grain continues the window art's from the zone's top left. The world left and right of the column (`Column.WorldLeft`, `WorldRight`) is dimmed while a menu window
  is open: a screen-texture shader darkens and desaturates it (`SteamwovenWorldDim`, default 0.45 and 0.55, in the column options, `SteamwovenColumnMode.WorldDim`); presentation only, the dimming takes no pointer, so a click on the world is what it is under an open window today.
- **Edge elements (AC4).** The follower portraits hang from the frame's left edge (`AreaGameBarPanel.EdgeScreen`, the strips' screen moved to the frame's left); the spell pane stands 12 px right of the frame's left edge and 12 px above its bottom (`EdgeRect`, `EdgeBottom`); the combat list 8 px
  left of the frame's right edge and 8 px under its top (`EdgeTop`); the text bubbles' and floaters' view is the frame's width between the strips (`FreeRect`). What takes the strips' screen (the sleep and fate views, the windows' free width `FreeArea`, `FreeLeft`) is the column's.
  A pane wider than the side (1280 × 800: 160 px each side) lies partly under the strip, which draws over it: a recorded limit.
- **Smoke (AC5).** `ARC_STEAMWOVEN_VARIANTS` takes `column` and `column-thin` (the default list now ends with them; `legacy` and `thin` are the Original look and the full-width layout of the owner's comparison). For each size it checks the strips' places and width, the free area, the boards, the caps,
  the edge elements, the brass edge read back, the dimming read back (the world's mean brightness falls under an open menu), then opens the inventory and every window of the hook (the zone place, the pieces of the wood, the seam ratio at the zone's top and bottom edges) and saves
  `PREFIX-hud-steamwoven-column[-thin]-WxH[-menu|-WINDOW].png` (local, never in git).

## Asset kit

**Master scale (ARC-RES-2949).** The kit stays at 3.6 px per unit (composition 2880 × 2160): the HD pack audit compared it with the pack's 4 px per unit
(3200 × 2400) and recommends 3.6, because its steps draw the 16:9 and 21:9 sizes (720p, 1080p, 1440p, 2160p) without resampling and the kit, its tools
and the layout already use it; a pack frame reused in the kit is area-resampled by 0.9 ([audit](../rendering/hd-pack-4x3-audit.md#kit-master-scale-36-or-4-px-per-unit)).

Part kinds:

| Kind | What | Examples |
| --- | --- | --- |
| material | a surface, seamless where it must be | wood, dark wood, brass, iron, paper, parchment |
| board | a strip: the art of the top or of the bottom strip, 2880 px wide (the kind's id is kept from the census; it names the strip art, not a full-width board) | the HUD strips, the two-strip menu strips |
| backdrop | a window background (2880 × 1440 px, the window zone) or a full-screen plate (2880 × 2160 px, the composition) of a family; never wider than the composition | inventory left/right, logbook, schematic paper, main menu |
| cap | the end cap of a strip: a brass edge and the soft shadow on the world (a new part, no original art) | the strips' outer ends |
| hardware | fixed metal or frame pieces over a strip or a window background | brass rings, sockets, vial housings, rails, frames |
| plate | a button's body | round and square plates by size |
| glyph | a symbol on a transparent background | button symbols |
| state | an overlay for a button state | hover, pressed (the original has no disabled frame; disabled is a Steamwoven option) |
| sheet | a document laid over a backdrop | newspaper, letter, book page, telegram, plaque |
| grid | a grid as its own layer | the schematic grid, inventory cells |
| picture | an image on a transparent background | items on the schematic, item and spell icons, portraits |
| gauge | layered gauge parts; the fill comes from the value | vial glass, housing, liquid |
| decor | ornaments that are not hardware | corner pieces, rivets |
| cursor | pointer art | the cursor set |

- **SWI-KIT-1** Every part is a row of the kit manifest `tools/steamwoven_ui/kit.json`
  ([#2454](https://github.com/Simarglok/arcanum-steamwoven/issues/2454)): id, kind, master size, anchor, layers,
  states, used-by windows, source original art ids (identifiers only), family, batch, provenance record id, status.
  The manifest holds no pixels. **As built (ARC-DEV-2454):** `tools/steamwoven_ui/build_kit.py` makes it from the [census](steamwoven-interface-census.md) (one part per original art file; census rows with the same name and size are taken as one file and are one part with all their numbers in `source_arts`; master = original units × 3.6 snapped to the 6 px grid, halves up; the strips come out at 150 / 570 px, marked `provisional` until [#2927](https://github.com/Simarglok/arcanum-steamwoven/issues/2927) fixes the open question), and `tools/steamwoven_ui/kit_check.py` validates it (unique ids, known kinds including `cap`, sizes on the grid and inside the composition, layers naming existing parts without a cycle, known windows, every census art a part or excluded, each part agreeing with its census rows in kind, batch, size, windows and frames, every window that draws art used, and the caps agreeing with their strip). Two census arts are kept with a note: art 2 `Black` (the census says no kit part is needed) and art 330 (network lobby, out of scope); a batch or the owner may drop them. The parts no original art feeds are rows of their own with an open size (`size_basis` `open`, fixed by their batch): the four end caps, the follower button's backing, the six materials, the inventory and schematic grid layers and the corner and rivet decor. The 65 census rows that are not kit parts (33 bitmap fonts, 24 network play, 8 unused tabs) are listed under `excluded`. The button's ring, glyph and state layers are not rows yet: B-BUTTONS ([#2482](https://github.com/Simarglok/arcanum-steamwoven/issues/2482)) splits each plate when it produces them.
- **SWI-KIT-2** A master is a PNG, RGBA 8 bit, sRGB, straight alpha, with sides that are multiples of 6 px.
- **SWI-KIT-3** A part used in several windows (a reuse group of the census) is one part at one master size.
- **SWI-KIT-4** A part with transparency has a clean edge: no halo of the generator's background colour (checked by
  the fit tool, SWI-VER-3).
- **SWI-KIT-5** A button is plate + ring + glyph + state overlays. The states of one button size (idle, hover, pressed,
  disabled) are generated together on one sheet, so they match. The original has no disabled frame (idle 0, pressed 1, hover 2: [states note](steamwoven-interface-states.md#the-originals-state-rules)); a disabled overlay is a Steamwoven addition and an open question there.
- **SWI-KIT-6** A gauge is glass, housing and liquid as layers; the fill level is drawn from the value, not from a frame
  sequence.
- **SWI-KIT-7** Order of production: the strips, end caps and window backgrounds, then buttons and glyphs, sheets, the schematic, gauges and
  decor; icons, portraits and cursors last (the owner: all interface art is redrawn).

## Generation pipeline

| Step | Who | Output | Where it lives |
| --- | --- | --- | --- |
| 1. Census | agent ([#2451](https://github.com/Simarglok/arcanum-steamwoven/issues/2451), states and animations [#2971](https://github.com/Simarglok/arcanum-steamwoven/issues/2971)) | [census note](steamwoven-interface-census.md), [states and animations note](steamwoven-interface-states.md), screenshots | note in git, screenshots in `local/steamwoven-ui/census/` |
| 2. Kit manifest | agent ([#2454](https://github.com/Simarglok/arcanum-steamwoven/issues/2454)) | `kit.json`, validator | git |
| 3. Context images | owner in Codex ([#2455](https://github.com/Simarglok/arcanum-steamwoven/issues/2455)) | whole-screen redraws of key screens | local |
| 4. Material sheet | owner in Codex, owner approves ([#2456](https://github.com/Simarglok/arcanum-steamwoven/issues/2456)) | wood, brass, paper, … | local |
| 5. Parts | owner in Codex, per batch | raw generations | `local/steamwoven-ui/outbox/` |
| 6. Fit | agent tool ([#2459](https://github.com/Simarglok/arcanum-steamwoven/issues/2459)) | masters | `local/steamwoven-ui/masters/` |
| 6b. Assemble | agent tool ([#2972](https://github.com/Simarglok/arcanum-steamwoven/issues/2972), [page](steamwoven-kit-assembly.md)) | clean base plus placed details baked into the HD frame of the original ART, one frame per original frame with its state | local |
| 7. Look check | owner | per-batch composite | thread only |
| 8. Bake | agent tool ([#2460](https://github.com/Simarglok/arcanum-steamwoven/issues/2460), `tools/steamwoven_ui/bake.py`) | four step sets | `local/steamwoven-ui/baked/<step>/`, local only (SWI-PRV-4) |
| 9. Runtime | agent ([#2461](https://github.com/Simarglok/arcanum-steamwoven/issues/2461)) | composed interface | code in git |

- **SWI-GEN-1** Context images come first: whole-screen redraws of the key screens (HUD, inventory, character,
  logbook, schematic, one reading window, main menu) at the master composition size (2880 × 2160), so Codex sees what each part is for. Parts are
  then generated with a crop of the context image as reference.
- **SWI-GEN-2** The approved material sheet freezes the tones and textures; every later part is generated against it
  and colour-locked to it.
- **SWI-GEN-3** A part's references are the context crop and the material sheet, plus the original art of the part
  (crop, frame or screenshot): original art is given to Codex as input (owner decision 2026-11-16,
  [#2448](https://github.com/Simarglok/arcanum-steamwoven/issues/2448)). Its prompt
  comes from the prompt library `tools/steamwoven_ui/prompts/` (text only).
- **SWI-GEN-4** A generation becomes a master only through the fit tool (size, grid, alpha clean-up, low-frequency
  colour lock, provenance record).
- **SWI-GEN-5** The strips and the window backgrounds are made by the method the owner picks
  ([#2458](https://github.com/Simarglok/arcanum-steamwoven/issues/2458)) from the comparison
  ([#2457](https://github.com/Simarglok/arcanum-steamwoven/issues/2457)).
- **SWI-GEN-6** Every generated image has a provenance record: generator and version, date, prompt id and the hash of
  its text, reference ids and hashes, who ran it. The batch runner (SWI-GEN-7) also records the Codex thread id, the pixel size, the
  outcome and the refusal text.
- **SWI-GEN-7** Codex image generation runs through the owner's subscription Codex CLI on the Mac (owner decision 2026-11-16,
  [#2449](https://github.com/Simarglok/arcanum-steamwoven/issues/2449), changed 2026-11-19, [#3001](https://github.com/Simarglok/arcanum-steamwoven/issues/3001):
  agents may run it). No API and no key: the built-in `image_gen` tool of the logged-in CLI only. Handoff: the agent builds a batch inbox
  (`local/steamwoven-ui/inbox/<batch>/`: prompts, references, `batch.json`, `README.txt`), runs `tools/steamwoven_ui/codex_batch.py`, which writes
  `local/steamwoven-ui/outbox/<batch>/<id>.png` with a provenance record per image, reports the files, sizes and refusals to the owner, and then runs the fit tool.
  The owner can still fill the outbox by hand and run `prompt_library.py provenance`.
- **SWI-GEN-8** The result of an item is accepted only as a PNG at the asked aspect (within 1 %); the generator keeps a budget of about 1.57 MP per
  image, so a master size above it is reached by the fit tool, never by an upscale inside the generation session. A refusal or a failure is
  logged with Codex's own text; the prompt is never reworded automatically.

### Codex batch runner (ARC-DEV-3001)

[#3001](https://github.com/Simarglok/arcanum-steamwoven/issues/3001). `tools/steamwoven_ui/codex_batch.py` (standard library only; tests `python3 -B -m unittest tools.steamwoven_ui.test_codex_batch`, also in the batched `run_tests.py`)
runs one inbox batch and writes the outbox. It never writes into the inbox and never touches git. Its work folder is `local/steamwoven-ui/codex-work/<batch>/<run>/` (prompts sent, JSONL events, results and stderr per item).

- **`batch.json`** (written by `prompt_library.py inbox`, or by hand): `{"batch": NAME, "items": [...]}`; an item has `id` (plain name), `prompt_file` and `prompt_sha256` (required; the text is checked against it; files stay inside the inbox),
  `size` `[w, h]` or null and optional `aspect` `[w, h]` (the asked aspect is `aspect`, else `size`), `references` `[{"slot", "file", "sha256"}]` (files relative to the inbox, inside it, hash required and checked); `after` and other keys are ignored and `outbox_file` = `outbox/<batch>/<id>.png`.
- **Pre-check**: `codex --version`, `codex features list` shows `image_generation` on, `codex login status` is a subscription login (an API-key login stops the run). Any failure stops with the reason (exit 2) before anything is sent.
- **One session per batch**: the first item is `codex exec --skip-git-repo-check --sandbox workspace-write --enable image_generation --json -C <workdir> -o <result> [--image=REF ...] -` (prompt on stdin, with the session rules); the `thread_id` comes from the `thread.started` event and every later item is `codex exec resume <thread_id> ...` run from the same directory, strictly in order. Never `--last`, never `--ephemeral`, an argument list with no shell, `OPENAI_*` variables removed from the environment.
- **Per item**: exit code, the final JSON `{"ok", "path", "error"}`, the file `<id>.png` in the work folder, a PNG, the aspect within 1 %. Outcomes: `ok`, `refused` (ok false, with Codex's text), `failed`, `timeout` (`--timeout`, default 900 s, the process group is killed), `bad-result`, `missing-file`, `not-png`, `wrong-aspect`. The batch goes on after a failed item. Only an `ok` image is copied to the outbox, and an existing outbox file is kept unless `--overwrite`.
- **Provenance** `outbox/<batch>/<id>.provenance.json`: generator `codex-cli <version> image_gen`, date, batch, id, `prompt_sha256`, each reference with its sha256, `thread_id`, pixel size, asked size and aspect, outcome, refusal text, image sha256.
- **Commands**: `codex_batch.py check`; `codex_batch.py run BATCH --dry-run [--only ID,ID]` prints the commands and sends nothing; `codex_batch.py run BATCH [--only ID,ID] [--timeout S] [--overwrite] [--model M]`. Exit 0 all ok, 1 an item not ok, 2 pre-check or batch file failed.
- First real run (2026-11-19): a two-item synthetic text batch, both items `ok` in one resumed session; the tool returned 1448 × 1086 px (1.57 MP) for a 4:3 ask. Reference attachment (repeated `--image=PATH` before the `-` prompt, on `exec` and on `exec resume`) was checked once with two tiny synthetic images against the real CLI.
- The agent skill that drives it, `.claude/skills/steamwoven-codex-images/SKILL.md`, is narrow: only running ready interface image batches.

### Prompt library and handoff (ARC-RES-2455)

[#2455](https://github.com/Simarglok/arcanum-steamwoven/issues/2455). `tools/steamwoven_ui/prompts/` is text only: `common.txt` (the rules of every prompt: layout truth is the original, one 4:3 composition, no text, colour lock to the sheet, painted look, transparent parts with a clean edge), one prompt per context screen (`ctx-hud`, `ctx-inventory`, `ctx-character`, `ctx-logbook`, `ctx-schematic`, `ctx-reading`, `ctx-main-menu`, each 2880 × 2160), the material sheet (`mat-sheet`, twelve swatches sampled from the approved contexts, 3840 × 2160) and one prompt per kit kind (`kind-*`, with the three backdrop variants: window background, half of a two-part window, full-screen plate), listed in `library.json` (id, group, kind, size, reference slots, batch). `references.json` holds the crop geometry of the 800 × 600 layout references built from the census screenshots (local only).

- The tool `tools/steamwoven_ui/prompt_library.py` checks the library (unique ids, a prompt for every TRD kind and every context screen, declared sizes on the 6 px grid (a part's own size is checked when its prompt is composed and, with `provenance`, the aspect of each result, because the generator may return a smaller size; the strip heights stay an open question), plain ASCII, known placeholders and reference slots, the kinds against the asset kit table above), composes a prompt (common rules, body, reference slots) with its sha256, builds the layout references (`refs`, needs ImageMagick), builds a batch inbox (`inbox`: composed prompts, references, `batch.json`, `README.txt`) and, after the owner has put the results into the outbox, writes one provenance record per image (`provenance`: generator and version, date, prompt id and hash, reference hashes, who ran it, SWI-GEN-6). The inbox and outbox are `local/steamwoven-ui/inbox|outbox/<batch>/` of the main checkout.
- Batches: `style-anchors-contexts` (the seven context images), then `style-anchors-sheet` (the material sheet, made from the approved contexts); the owner's approval of the sheet is [#2456](https://github.com/Simarglok/arcanum-steamwoven/issues/2456). Part prompts take the part id, a note and the size from the production batch that uses them.
- Checks: `python3 -B tools/steamwoven_ui/prompt_library.py check`, `... self-test`, `python3 -B -m unittest tools.steamwoven_ui.test_prompt_library` (task checks, not part of `run_tests.py`).

## Bake and runtime

- **SWI-RUN-1** The bake makes the four step sets from the masters, resampled in linear light with premultiplied
  alpha; every baked length is exactly the master length times the step; two runs give identical bytes; each set has
  a manifest with sizes and hashes. Tool and commands: [toolchain](../workflow/toolchain.md#steamwoven-kit-bake-arc-dev-2460) (`bake.py`, ARC-DEV-2460).
- **SWI-RUN-2** The runtime takes the step from SWI-LAY-4 and composes the layers of each part (strips with their end caps, hardware,
  plates, glyphs, states) inside the composition.
- **SWI-RUN-3** A missing part falls back to the generated chrome
  ([ARC-RES-122](../rendering/hd-interface-density.md)), so the HD option always draws a complete interface.
- **SWI-RUN-4** With the HD option off the Legacy art, its fonts and the original rules are unchanged; only the layout is the composition of SWI-ORG-1..5 ([#2936](https://github.com/Simarglok/arcanum-steamwoven/issues/2936)). The 2026-11-16 requirement that the output stays byte-identical is superseded (owner decision 2026-11-19).
- **SWI-RUN-5** Only the background of the open window is resident (a ×1 window background is about 16.6 MB as raw RGBA, a full-screen plate about 24.9 MB); the
  strips, their end caps and the HUD hardware stay loaded.

## Provenance and storage

- **SWI-PRV-1** Owner decision 2026-11-16 ([#2448](https://github.com/Simarglok/arcanum-steamwoven/issues/2448)): original art is given to
  Codex as input, so every generated image, master and baked set is derived from the original: it stays under the ignored
  `local/steamwoven-ui/`, is never committed and never packaged, and follows the
  [HD pack workflow](../workflow/hd-pack.md) for backup. In git: tools, the manifest (identifiers only), the prompt
  library (text) and notes with numbers.
- **SWI-PRV-2** The kit therefore exists only on the owner's devices (and those of a player who builds it from their own
  installation), like the HD pack; it is not shipped with the game. Reopening this needs a new owner decision.
- **SWI-PRV-3** Composites for the owner are posted in the thread and never committed (owner rule 2026-11-11).
- **SWI-PRV-4** Storage of the baked sets (ARC-DEV-2460): `local/steamwoven-ui/baked/<step>/` of the main checkout, written only by `bake.py`; not committed, not packaged and
  not backed up (reproducible from the masters, whose backup convention is in the [toolchain section](../workflow/toolchain.md#steamwoven-kit-bake-arc-dev-2460)).

## Verification

Agent-checkable (acceptance criteria of the slices):

- **SWI-VER-1** Layout model unit tests reproduce the [reference table](#screen-frame-and-scale) exactly (the target model of [#2925](https://github.com/Simarglok/arcanum-steamwoven/issues/2925)).
- **SWI-VER-2** The manifest validator passes on the real manifest and fails each synthetic bad manifest.
- **SWI-VER-3** Fit tool tests on synthetic fixtures: exact output size; no halo (in the semi-transparent edge band the
  mean colour difference to the nearest opaque interior is within the bound the tool documents); the colour lock
  leaves the high-frequency energy within 10 % of the input.
- **SWI-VER-4** Bake: exact sizes and identical bytes on a second run.
- **SWI-VER-5** Surfaces: seam ratio R ≤ 1.5 at every join inside the composition (a window zone's edge against the strips, the parts of one strip; the measure of
  [ARC-RES-094 U4](../rendering/hd-interface-art.md#9-slice-side-pieces-u4); the designed split of a two-part window is exempt, SWI-BDR-5); there are no sides to match, so the former side-versus-centre statistics are dropped. [#2457](https://github.com/Simarglok/arcanum-steamwoven/issues/2457) may add
  repetition and sharpness bounds for the surfaces it compares; until then only the seam ratio is a gate.
- **SWI-VER-6** Smoke captures (local only) at 3840 × 2160, 2560 × 1440, 1920 × 1080, 2560 × 1600, 1920 × 1200,
  1280 × 800, 1366 × 768, 3440 × 1440, 5120 × 1440 (32:9), 1024 × 768 (4:3) and 1280 × 1024 (5:4); the owner's composite shows 16:9, 16:10, 21:9, 4:3 and 5:4.
- **SWI-VER-7** With the HD option off the Legacy art is drawn unchanged at whole `s` (pixel check against nearest scaling) and the out-of-sight rules are the original's (SWI-RUN-4, SWI-ORG-3).

Owner gates (separate issues, never acceptance criteria): proportions
[#2453](https://github.com/Simarglok/arcanum-steamwoven/issues/2453), material sheet
[#2456](https://github.com/Simarglok/arcanum-steamwoven/issues/2456), surface method
[#2458](https://github.com/Simarglok/arcanum-steamwoven/issues/2458), and the look of each production batch (named by
its issue).

## Work breakdown

One issue is one work item ([Definition of Ready](../workflow/tasks.md#definition-of-ready)); the issues carry the
acceptance criteria. Start any issue that is `status:ready`; the order below is the dependency order. State at 2026-11-19.

| Phase | Issue | Result | Blocked by |
| --- | --- | --- | --- |
| 0. Decisions | [#2447](https://github.com/Simarglok/arcanum-steamwoven/issues/2447) | the first ADR and TRD (2026-11-16, done) | — |
| 0. Decisions | [#2448](https://github.com/Simarglok/arcanum-steamwoven/issues/2448) | owner: provenance and distribution — decided: originals are Codex inputs, kit local only | — |
| 0. Decisions | [#2449](https://github.com/Simarglok/arcanum-steamwoven/issues/2449) | Codex runner — decided 2026-11-16: the owner runs Codex (closed, not planned); changed 2026-11-19: agents run the subscription CLI ([#3001](https://github.com/Simarglok/arcanum-steamwoven/issues/3001)) | — |
| 0. Decisions | [#2893](https://github.com/Simarglok/arcanum-steamwoven/issues/2893) | this rewrite: the ADR amendment, the TRD, the replacement production issues | #2892 |
| 1. Geometry | [#2450](https://github.com/Simarglok/arcanum-steamwoven/issues/2450) | layout model of the full-width frame (done; its model is superseded) | — |
| 1. Geometry | [#2452](https://github.com/Simarglok/arcanum-steamwoven/issues/2452) | full-width prototype with generated chrome (done, [historical](#prototype-of-the-play-screen-arc-ui-2452)) | #2450 |
| 1. Geometry | [#2453](https://github.com/Simarglok/arcanum-steamwoven/issues/2453) | owner: panel proportions — decided 2026-11-17: 120 / 456 (retired 2026-11-19, SWI-LAY-10) | #2452 |
| 1. Geometry | [#2889](https://github.com/Simarglok/arcanum-steamwoven/issues/2889) | column model in the layout model (done) | — |
| 1. Geometry | [#2890](https://github.com/Simarglok/arcanum-steamwoven/issues/2890), [#2891](https://github.com/Simarglok/arcanum-steamwoven/issues/2891), [#2924](https://github.com/Simarglok/arcanum-steamwoven/issues/2924) | the column in the viewer: play screen and windows, full-screen and two-strip menus, creation editor and shop (finish as contracted) | #2889 |
| 1. Geometry | [#2892](https://github.com/Simarglok/arcanum-steamwoven/issues/2892) | owner's column check — verdict 2026-11-19 (closed) | #2890, #2891 |
| 1. Geometry | [#2925](https://github.com/Simarglok/arcanum-steamwoven/issues/2925) | complete the 4:3 rule: the follower group at the window's edge (SWI-LAY-16) and the points #2890 leaves (SWI-LAY-1..3) | #2890 |
| 1. Geometry | [#2936](https://github.com/Simarglok/arcanum-steamwoven/issues/2936) | the Original look in the composition (SWI-ORG) | #2890 |
| 1. Geometry | [#2937](https://github.com/Simarglok/arcanum-steamwoven/issues/2937) | owner: world zoom in the Steamwoven look — decided 2026-11-19: stays a player setting, composition scale is the reference (SWI-LAY-17); the default is to be revisited | — |
| 1. Geometry | [#2926](https://github.com/Simarglok/arcanum-steamwoven/issues/2926) | remove the full-width layout | #2925, #2893 |
| 2. Census | [#2451](https://github.com/Simarglok/arcanum-steamwoven/issues/2451) | census, families, production batches (done) | — |
| 2. Census | [#2454](https://github.com/Simarglok/arcanum-steamwoven/issues/2454) | kit manifest and validator | #2451 |
| 3. Style | [#2455](https://github.com/Simarglok/arcanum-steamwoven/issues/2455) | context images at the composition size, material sheet, prompts | #2451 |
| 3. Style | [#2456](https://github.com/Simarglok/arcanum-steamwoven/issues/2456) | owner: material sheet | #2455 |
| 3. Style | [#2457](https://github.com/Simarglok/arcanum-steamwoven/issues/2457) | surface method comparison on a strip and a window background | #2456 |
| 3. Style | [#2458](https://github.com/Simarglok/arcanum-steamwoven/issues/2458) | owner: surface method | #2457 |
| 4. Tools | [#2459](https://github.com/Simarglok/arcanum-steamwoven/issues/2459) | fit tool | #2454 |
| 4. Tools | [#2460](https://github.com/Simarglok/arcanum-steamwoven/issues/2460) | bake tool and storage | #2454 |
| 4. Tools | [#2972](https://github.com/Simarglok/arcanum-steamwoven/issues/2972) | kit assembly tool: base plus details at the original coordinates, baked per original frame (`tools/steamwoven_ui/assemble.py`, [page](steamwoven-kit-assembly.md); done) | #2893, #2971 |
| 5. Runtime | [#2461](https://github.com/Simarglok/arcanum-steamwoven/issues/2461) | runtime composition of the strips, caps, window backgrounds and plates, window integration issues | #2925, #2460 |
| 5. Runtime | [#2462](https://github.com/Simarglok/arcanum-steamwoven/issues/2462) | fonts at the four steps | #2451 |
| 6. Production | [#2927](https://github.com/Simarglok/arcanum-steamwoven/issues/2927) to [#2935](https://github.com/Simarglok/arcanum-steamwoven/issues/2935) (replace the full-width #2472 to #2480, [batch table](steamwoven-interface-census.md#production-batches)) | strips with caps (B-BOARD), window backgrounds (BD-A to BD-G), full-screen plates (BD-H) | fit, bake, material sheet, surface method |
| 6. Production | [#2481](https://github.com/Simarglok/arcanum-steamwoven/issues/2481) to [#2487](https://github.com/Simarglok/arcanum-steamwoven/issues/2487) (unchanged) | hardware, buttons, gauges, icons, schematic pictures, cursors | fit, bake, material sheet |
| 6. Integration | created by #2461 | one issue per window | runtime, its batch |

Ready now: [#2462](https://github.com/Simarglok/arcanum-steamwoven/issues/2462) (fonts), [#2490](https://github.com/Simarglok/arcanum-steamwoven/issues/2490) (open facts), and the window-zone defects [#2615](https://github.com/Simarglok/arcanum-steamwoven/issues/2615) and
[#2641](https://github.com/Simarglok/arcanum-steamwoven/issues/2641) (re-check them against the column after #2925). The kit track (#2454, #2455, #2457, #2461, production) was waiting for this rewrite. Replaced and closed as not planned on 2026-11-19: #2472 to #2480, #2578, #2638, #2643.

**No batch makes gap wood** (the former idea of the 16:10 and thin-panel gaps): the composition is exactly 4:3 and has no gaps, SWI-BDR-8.

## Open questions

- The player's `uiScale` setting (50–300 %) in the Steamwoven look: ignored (the interface is proportional by design)
  or a multiplier on `f` with the run-time scale absorbing it.
- Screens above ×1 (`f` > 1: 5K, 3840 × 2400): scale the ×1 set up (current requirement) or add a ×4/3 set.
- **Maximum world width on ultrawide windows (new, 2026-11-19).** The world is drawn to the window's edges beside the composition (SWI-LAY-3) at the composition's scale (SWI-LAY-17), and Steamwoven tests the drawn view for the out-of-sight rules (decision 2026-11-07), so a 32:9 window (5120 × 1440: 1600 px = 667 units of world each side of an 800-unit composition) sees
  far more than the original's 800-unit view and than a 16:9 window does. Open: no limit (the drawn view decides, current requirement), a maximum drawn world width with black beyond it, or the rules keeping the 16:9 view. The owner decides after #2925's composite at 21:9; listed as open in the [decisions](../architecture/decisions.md) row of 2026-11-19.
- **Strip master heights.** The strips are 41 and 159 units = 147.6 and 572.4 master px, which are not multiples of the 6 px grid (SWI-LAY-7): author them at 150 and 570 (the grid; the model's former band heights, drawn at the exact unit height) or at 148 and 572 (the rounded units of the reference table). Fixed with the first batch, [#2927](https://github.com/Simarglok/arcanum-steamwoven/issues/2927).

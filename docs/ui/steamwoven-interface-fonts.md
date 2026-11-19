# Fonts of the Steamwoven interface at the four kit steps (ARC-RES-2462)

Slice [#2462](https://github.com/Simarglok/arcanum-steamwoven/issues/2462) of the Steamwoven interface epic
[#2446](https://github.com/Simarglok/arcanum-steamwoven/issues/2446). It answers TRD requirement SWI-LAY-15 (no text is baked into a kit
part; every text is drawn with a font, [TRD](steamwoven-interface-trd.md)): which font draws each text role, at what size and line height
at each kit step, and under which licence. Nothing here changes behaviour; the viewer is untouched.

## Decision: the original's fonts, nothing else

Owner rule (2026-11-19, project instructions): **original fonts everywhere; in HD the HD version of that same font, else the original; no
Godot default font and no other substitute.** So the issue's options (free vector look-alikes, or both) are closed: no look-alike font is
chosen, no font file enters git, and the font of every text role is the original game's bitmap font (an interface ART of 225 frames,
frame *n* is character *n* + 31, Latin-1 only), drawn from the HD glyph frames of the player's local pack when the pack has them and from
the original's frames when it does not. Two consequences are recorded as limits, not fixed here:

- **Latin-1 only.** The fonts hold characters 31..255; a character above 255 has no glyph and is skipped (`AreaArtFont.Glyph`). Under the rule
  there is no substitute for other alphabets.
- **Non-original text exists in the viewer today** (the Godot fallback font in a few player-facing places, [list below](#non-original-text-in-the-viewer-today)).
  Each one is outside this note's scope and has its own issue.

## Scale arithmetic

The kit master is 3840 x 2160 and the 4:3 composition is 2880 x 2160 master px for the original's 800 x 600 layout units, so one original
pixel is **3.6 master px** ([epic plan 2026-11-19](https://github.com/Simarglok/arcanum-steamwoven/issues/2446)). A text is laid out in the
original's units (glyph advance = the frame header's centre x, line = the tallest glyph; the viewer's `AreaArtFont.LineHeight` takes the space glyph's height and `AreaTextFontLayout.LineHeight` the tallest, and on the player's installation
the two are equal for all 33 fonts, read locally on 2026-11-19, so either rule gives the `Line` column) and drawn at `3.6 x step`:

| Step | Master x | Device line px per original px | HD glyph frame (4x art) drawn at |
| --- | ---: | ---: | ---: |
| x1 (3840 x 2160) | 1 | 3.6 | 0.9 |
| x2/3 (2560 x 1440) | 2/3 | 2.4 | 0.6 |
| x1/2 (1920 x 1080) | 1/2 | 1.8 | 0.45 |
| x1/3 (1280 x 720) | 1/3 | 1.2 | 0.3 |

No step draws an HD glyph larger than its art (the 4x frame is always minified) and no step is a whole number of original pixels
(3.6, 2.4, 1.8, 1.2), so every drawn glyph is resampled. The layout model owns where text goes ([#2890](https://github.com/Simarglok/arcanum-steamwoven/issues/2890),
[#2891](https://github.com/Simarglok/arcanum-steamwoven/issues/2891), [#2925](https://github.com/Simarglok/arcanum-steamwoven/issues/2925)); this
note fixes only the font, the size and the line height per step. A window the layout scales by its own factor draws its text at that factor times the
values below.

## Text roles, fonts and sizes

Windows use the ids of the [census](steamwoven-interface-census.md). **Where this table and the census differ the table wins**: the census lists a
font only for the arts a window draws and misses the text the code draws (font 229 is drawn in far more windows than the census says; 171, 486 and 27 in
more places; font 483 is listed for G07 and M09 but never drawn, found by reading the viewer on 2026-11-19). `HD` is where the HD glyph frames of the installed pack
came from: **A** Codex-generated (ARC-UI-1202, ARC-UI-2329), **B** traced locally from the original by [`tools/hd_font`](../workflow/hd-font.md) (ARC-DEV-361, ARC-UI-2309),
**C** present in the installed pack (checked 2026-11-19), source not recorded in the workflow notes; a local delivery folder named ARC-UI-2845 holds these four, which is an unverified lead, and `hd-font.md` still says 300 and 370 stay Legacy (stale, the pack has them). `Line` is the original line height in
original px; the four right-hand columns are the line height in device px at the four steps (`Line x 3.6 x step`, not rounded: rounding belongs to the layout).

| ART | Font | Text role | Windows | HD | Line | x1 | x2/3 | x1/2 | x1/3 |
| ---: | --- | --- | --- | :-: | ---: | ---: | ---: | ---: | ---: |
| 25 | `rollerfont` | Quantity count digits | H05 | C | 20 | 72.0 | 48.0 | 36.0 | 24.0 |
| 27 | `morph15font` | Titles, stat and label names, option labels, hotkey-bar slot count, character names | H02/H09 (hotkeys), H04, H19, G05, G06, G08, G10, M04, M07, M08, M09 | A | 19 | 68.4 | 45.6 | 34.2 | 22.8 |
| 28 | `pork12font` | Spell minimum-level text on the spells page | G08, M09 | A | 18 | 64.8 | 43.2 | 32.4 | 21.6 |
| 37 | `casablanca16font` | Written-text markup only (books, plaques) | G16 | B | 15 | 54.0 | 36.0 | 27.0 | 18.0 |
| 171 | `cloister18font` | Numbers: bottom-strip health, fatigue and bar counters, sheet alignment and aptitudes, inventory counts | H02, H03, G01, G05, G10, M09 | A | 18 | 64.8 | 43.2 | 32.4 | 21.6 |
| 229 | `flare12font` | General text: messages, tooltips, floaters, bubbles, dialog lines, list rows, option values, log, hover, descriptions | H03, H04, H08, H11, H12, H15-H19, H21, G01-G05, G07, G09-G12, G15, M04-M09, M11 | A | 15 | 54.0 | 36.0 | 27.0 | 18.0 |
| 230 | `flare14font` | Hit-chance percentage over the bullseye of the combat panel | H04 (combat panel) | A | 16 | 57.6 | 38.4 | 28.8 | 19.2 |
| 300 | `icons17font` | Discipline glyph in the tech page header and schematic slots | G07, G15 | C | 17 | 61.2 | 40.8 | 30.6 | 20.4 |
| 301 | `nick16font` | Tech page header text, degree and schematic names | G07, M09 | A | 21 | 75.6 | 50.4 | 37.8 | 25.2 |
| 327 | `morph30font` | Menu button labels | M01-M04 | A | 33 | 118.8 | 79.2 | 59.4 | 39.6 |
| 370 | `icons32font` | Large discipline glyph of the selected schematic | G15 | C | 32 | 115.2 | 76.8 | 57.6 | 38.4 |
| 371 | `casablancaantique30font` | Selected schematic's name | G15 | A | 38 | 136.8 | 91.2 | 68.4 | 45.6 |
| 478 | `bookmanoldbold18font` | Written-text markup only (books) | G16 | B | 20 | 72.0 | 48.0 | 36.0 | 24.0 |
| 479 | `clarendonblk18font` | Telegram name lines; book markup | G16 | B | 21 | 75.6 | 50.4 | 37.8 | 25.2 |
| 480 | `courier10font` | Telegram body and last form line | G16 | B | 12 | 43.2 | 28.8 | 21.6 | 14.4 |
| 481 | `garmond6font` | Telegram form lines (nine of eleven) | G16 | A | 7 | 25.2 | 16.8 | 12.6 | 8.4 |
| 482 | `garmond8font` | Newspaper masthead's three small lines | G16 | A | 10 | 36.0 | 24.0 | 18.0 | 12.0 |
| 483 | `garmond9font` | Loaded for the tech page but not drawn; newspaper text by markup | G07 (not drawn), G16 | A | 10 | 36.0 | 24.0 | 18.0 | 12.0 |
| 484 | `georgia30font` | Newspaper masthead's middle title | G16 | B | 33 | 118.8 | 79.2 | 59.4 | 39.6 |
| 485 | `zurich20font` | Newspaper masthead's side titles | G16 | B | 24 | 86.4 | 57.6 | 43.2 | 28.8 |
| 486 | `latinxcn30font` | Damage and armour ratings under the doll | G01, hero doll in G02/G03 | C | 35 | 126.0 | 84.0 | 63.0 | 42.0 |
| 490 | `swiss921font` | Newspaper text by markup | G16 | B | 37 | 133.2 | 88.8 | 66.6 | 44.4 |
| 492 | `zurich16font` | Newspaper text by markup | G16 | B | 17 | 61.2 | 40.8 | 30.6 | 20.4 |
| 497 | `pepper20font` | Note body | G16 | B | 19 | 68.4 | 45.6 | 34.2 | 22.8 |
| 815 | `euph30font` | Alternate masthead middle title | G16 | B | 33 | 118.8 | 79.2 | 59.4 | 39.6 |
| 840 | `newtimes16font` | World map labels and coordinate readouts | G12, G13 | B | 21 | 75.6 | 50.4 | 37.8 | 25.2 |

Fonts with no HD frames: none that the viewer draws. Seven font ARTs are never drawn by the viewer and need no step: 26 `arial10font`, 226 `ArialB12Font`, 227
`Comic12Font`, 228 `Elga12Font`, 488 `NewsIconsFont`, 744 `BookImagesFont` and 841 `MPIconsFont` (network play); the pack holds at most a handful of frames of 488
and 744 and none of the others. If a text role ever needs one of them it draws the original frames at the factors above (no substitute). Fonts 37, 478, 490
and 492 are chosen by the written text's own markup, so they are drawn only when an installed book or newspaper names them.

The HD glyph frames exist for 28 font ARTs in the installed pack (index checked 2026-11-19: 223 frames per font; 221 for 27 and 222 for 497; the control-character
placeholders, vanished traces and the coloured frame 0 draw their original frame, [format](../formats/hd-pack.md)).

## Rules for drawing the fonts at the four steps

1. **Size = the original's size times the step factor.** A text role has no point size of its own: one original line is `Line` original px and the device
   size follows from the table. There is no per-step size choice to make, and none is left for the layout to guess.
2. **Layout in original units, drawing in device px.** Advances and wrapping are computed in the original's integers (so line breaks and clipping are the same at
   every step and in Original mode); each glyph is then placed at `x x 3.6 x step`.
3. **Minify the 4x frames with an area filter, not a bare bilinear sample.** The viewer samples a glyph bilinear **without mipmaps** (ARC-UI-145, chosen by the owner at
   window scales of 2.5 to 3.5, that is 0.6 to 0.9 of the 4x frame). At 0.45 and 0.3 each device pixel then reads only the four texels nearest its centre and skips
   the rest of the 2.2 to 3.3 texels under it, which breaks thin strokes up (the right column of the readability composite below). The area-filtered column is the target:
   bake the glyph frames per step with the kit bake ([#2460](https://github.com/Simarglok/arcanum-steamwoven/issues/2460)) or give the font textures mipmaps at the steps below 0.6.
   This is for the runtime composition slice ([#2461](https://github.com/Simarglok/arcanum-steamwoven/issues/2461)) and is its own issue, [#2939](https://github.com/Simarglok/arcanum-steamwoven/issues/2939).
4. **Floor.** The handheld rule of the old UI (`AreaUiScale.MinimumTextPx` = 9 px, ARC-GPAD-1377; [handheld scale](../input/handheld-ui-scale.md)) is a rule about that layout, not about
   these steps. For Steamwoven a line under about 10 device px has strokes of one pixel or less. At x1/3 that is only font 481 `Garmond6` (8.4 px, the telegram's form lines). Everything
   else is 12 px or more at x1/3. Garmond6 is kept (it is the original's font and the line is a printed form; at 8.4 px it is below that 9 px handheld minimum); the owner judges it in the composite.
5. **No text is baked into a kit part** (SWI-LAY-15): labels, numbers and names come from the `.mes` files and the game state and are drawn with the fonts above.

## Readability sample at x1/3

A local composite for the owner (never committed; the game's glyphs are in it): `Strength 12  Dexterity 9  Gold 1,204` in seven of the fonts above, at the device scale of
x1/3 (1.2 original px, 0.3 of the HD frame), shown at 3x nearest zoom so single device pixels are visible. Columns: the original bitmap font at 1.2x, the HD frames
area-filtered, and the HD frames with the viewer's current plain bilinear sampling. It is a **model** (Pillow resampling over the same glyph metrics), not a capture of the viewer: the
viewer cannot draw x1/3 text until the layout slices land. The composite was posted to the owner in the task thread (AC3). The composite is not in git and no script for it is kept; what follows is the author's local reading of it, not a measurement. Reading it: Flare12, Morph15, Cloister18 (18 to 23 px lines) read well in the first two columns; Garmond9 and Garmond8
(12 px) read, with thin serifs; Courier10 (14 px) reads; **Garmond6 (8.4 px) is marginal in every column, and plain bilinear sampling breaks every font up, worst the smallest**.

## Licences

No font file, glyph frame or outline is in git, and none is added.

| Font group | What it is | Licence and storage |
| --- | --- | --- |
| The 33 original bitmap fonts (26 are drawn by the viewer, the table above) | Rasterised glyph frames in the game's `interface.mes` ARTs, read from the player's own installation | The game's own assets: no licence from this project, never copied into git, a package or the HD pack source; the typefaces behind them look like commercial designs (names such as Garamond, Georgia, Zurich, Courier, Bookman, Clarendon; no review done), so the shapes stay local |
| HD glyph frames, group A (Codex) | Codex redraws of the original glyphs, owner-run, made from the original frames as input | Derived from the player's own game data (owner decision 2026-11-16, [#2448](https://github.com/Simarglok/arcanum-steamwoven/issues/2448): the HD content is derived, local only, not shipped); stays in the player's local HD pack |
| HD glyph frames, group B (traced) | 4x coverage frames traced from the original frames by `tools/hd_font` | Derived the same way, local only. The tool depends on **potracer** (GPLv2+, a port of Potrace) in a private venv, never vendored and never linked; that the traced output is not covered by that licence is a judgement, not a legal opinion ([hd-font](../workflow/hd-font.md)) |
| Group C (25, 300, 370, 486) | Frames in the installed pack | Local, derived from the player's data like A and B; **the origin is not recorded** (unverified: see the HD note above), to be written down where the owner next touches the pack |
| Godot's fallback font, system fonts, free look-alikes | **Not used for the Steamwoven interface** (owner rule) | Where the viewer still uses the Godot fallback font it is a defect against the rule, tracked below |

## Non-original text in the viewer today

Found by reading the viewer on 2026-11-19; each draws player-facing text with Godot's default font, against the owner rule. Not fixed here (layout and viewer code are
other slices' ground). Sites: the "Round N" turn-order list of the combat HUD (`AreaCombatHud`, 13 px `Label`s); the hotkey bar's slot names where a slot has no icon (`AreaHotkeyBarView`, 9 px
`Label`s); the follower portraits' initials and fallback words (`AreaFollowerPortraits`, 9 px `Label`s); and the fallback paths of the bar numbers, floaters and text bubbles and the
message window and log that run when the original font did not load (`AreaBarNumber.DrawStandIn`, `AreaFloaterLayer`, `AreaMessageLogView`). Other `Label` sites in the panels
(about 150 in the Godot folder, many developer or launcher panels) were not audited. Issue: [#2940](https://github.com/Simarglok/arcanum-steamwoven/issues/2940).

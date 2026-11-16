# UI scale on the handheld screen (ARC-GPAD-1377)

Issue [#1377](https://github.com/Simarglok/arcanum-steamwoven/issues/1377), epic [#1371](https://github.com/Simarglok/arcanum-steamwoven/issues/1371),
concept [controller schemes §0.9](controller-schemes.md), requirements
[FR-UI-007](../requirements/ui-media-lifecycle.md#fr-ui-007) and FR-UI-006.

## Decision (owner, 2026-11-12)

The original's windows are drawn at a **whole-number** scale only, and no text a player reads is smaller than **9 px** on the Steam Deck's 7 inch
1280 × 800 screen (Valve's Deck Verified rule: 9 px at least, 12 px recommended). Hit areas may be larger than the art; the art is not enlarged
to make room for them. The owner starts the physical tests on the ROG Xbox Ally X (1920 × 1080), so that size is measured together with 1280 × 800.

## Result

The original composes every window inside one 800 × 600 screen, so the viewer draws all of them at one whole factor, the largest that keeps that
screen inside the window (`AreaUiScale.ScreenFactor`, the former `AreaGameBarPanel.ScreenFactor`, unchanged in value). It is **1** on 1280 × 800
and on 1920 × 1080: the next step needs 1600 × 1200. So the chosen scale for both handhelds is 1, every original window fits, and no other
resolution changed (`AreaUiScaleTests` pins the factor for the common sizes).

At factor 1 the text is the original's own font size. The size is the number in the font's file name (`Flare12Font` = 12; line height = its tallest
glyph, the viewer's own "text height"; capital height = ink rows of "H", shown for information):

| Font (art) | Windows | Size | Line | Capital |
| --- | --- | ---: | ---: | ---: |
| Flare12 (229) | options, save and load list, character sheet, message window, hover names, floaters, dialog box, schematic | 12 | 15 | 9 |
| Garmond9 (483) | character sheet skills and tech pages | **9** | 10 | 7 |
| every other font a window draws | all the rest | 12 to 32 | 16 to 38 | 11 to 23 |

The only original-font text under the 12 px recommendation is Garmond9 on the character sheet's skills and tech pages, which sits exactly on the 9 px minimum.
Its capital letters are 7 rows high; if the owner reads "9 px" as a capital height instead of the font size, that text and Flare12's 9-row capitals
are the margin to judge on the device (human gate below).

The Godot drawing (the fallback without the player's fonts and the viewer-only labels) had one size under the rule: the hotkey bar's stand-in
names at 8 px. They are 9 px now (`AreaUiScale.MinimumTextPx`). The scan reads 16 sizes, from 9 (life bars, follower initials, the logbook button,
the hotkey stand-ins) to 14 (the floaters), including the inventory and barter drawing's own sizes (11 and 12 at factor 1; its floors of 6 and 8 apply
only below factor 1); the save list's transparent keyboard field keeps size 1 on purpose and carries a marker comment the scan honours.

## Measuring

```sh
python3 -B tools/research/ui_text_scale.py                  # 1280x800 and 1920x1080, installation from ARCANUM_GAME_DIR
python3 -B tools/research/ui_text_scale.py --size 1280x800 --game-dir PATH
python3 -B tools/research/ui_text_scale.py --self-test      # synthetic fonts, window table checked against the code
```

It reads the player's fonts from the installation (nothing is copied or written), prints the three heights per window and factor, scans the
literal Godot-label sizes, and exits 1 on any text under 9 px or a screen that does not fit. The window table lists the fonts each window draws;
`--self-test` checks that every cited source file still names them. A window added later (or a font changed) must be added to the table.

## Reading windows: the text-only factor (ARC-UI-1428, issue #1428)

The original draws the reading windows with these fonts (art number, size): telegram 480 Courier10 (10), note 497 Pepper20 (20), newspaper masthead 485 Zurich20
(20), 484 Georgia30 (30) and **482 Garmond8 (8)**, the alternate masthead 815 Euph30 (30), newspaper text 483 Garmond9 (9), 492 Zurich16 (16) and 490 Swiss921,
book and plaque markup 478, 479, 28 (Pork12), 37 (Casablanca16), 171 and 229. Two are under the 9 px minimum at the screen factor 1 of both handhelds:
Garmond8 (the masthead's three small lines, 8 px) and **Garmond6 (481, 6 px)**; the installed texts use no markup that names Garmond6, so it is covered
for a mod's or another language's data. Garmond9 and Courier10 are at or above the minimum.

**Rule (owner's whole-number policy kept):** a reading-window line whose original font is under 9 px at the screen factor is drawn with a **text-only whole
factor**, the smallest that reaches 9 px: `AreaUiScale.TextOnlyFactor` (Garmond8 × 2 = 16 px, Garmond6 × 2 = 12 px at factor 1; a font already at 9 px or
above, or at screen factor 2 and up where 8 × 2 = 16, is not multiplied). Only the glyphs are multiplied; the window art keeps the screen factor, hit areas are
not changed. The alternative, the viewer's fallback font at 9 px or more, is what the diagnostic panel draws now (`AreaReadingPanel`: every text has an explicit
size, a header line's size is `AreaReadingText.HeaderLinePx(font art, screen factor)` = the original size × screen factor × text-only factor, 9 to 32 px at factor 1,
16 px for a font whose file name carries no size; the body 16, the panel's other texts 12 to 14); a later drawing with the original fonts applies the same factor to the
glyphs (16-px-high text in a masthead built for 8 px glyphs is a layout matter of that drawing: it must re-break the masthead's lines, NOT_VERIFIED).
`tools/research/ui_text_scale.py` lists the reading windows (a row flagged `text_factor`) and prints `pass (text x2 = 16 px)` for such a line; the gate still
fails on any other window or Godot label under 9 px.

## Not covered, and open

- The **reading windows** (telegram, newspaper, notes, books, plaques) are not drawn with the original fonts yet (`AreaReadingPanel`: "fonts are not drawn"),
  but their text rule is decided, see the next section.
- The scan covers `AddThemeFontSizeOverride` sizes, `FontSize` constants of the files that draw with `DrawString`, and the inventory and barter
  `Math.Max(floor, N * _scale)` sizes; a label left at the theme's default size or a computed `DrawString` size elsewhere is not seen (a size it cannot
  resolve fails, it is never skipped).
- `AreaUiScale` holds the factor (used by every window through `AreaGameBarPanel.ScreenFactor`) and the two text limits (used by the hotkey bar); the script
  mirrors the factor, `AreaUiScaleTests` and `--self-test` keep both in step.
- Hit areas larger than the art (touch, controller focus) belong to the schemes' own slices of the epic.
- **Human gate:** the owner picks the final scale on a real 7 inch screen (the Deck) and on the 1080p handheld; physical readability is NOT VERIFIED here.

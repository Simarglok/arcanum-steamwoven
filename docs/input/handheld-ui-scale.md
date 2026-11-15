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
| Flare12 (229) | options, character sheet, message window, hover names, floaters, dialog box, schematic | 12 | 15 | 9 |
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

## Reading windows: the text-only factor and Garmond9 (ARC-UI-1428, ARC-UI-1462)

The original draws the reading windows with these fonts (art number, size): telegram 480 Courier10 (10), note 497 Pepper20 (20), newspaper masthead 485 Zurich20
(20), 484 Georgia30 (30) and **482 Garmond8 (8)**, the alternate masthead 815 Euph30 (30), newspaper text 483 Garmond9 (9), 492 Zurich16 (16) and 490 Swiss921,
book and plaque markup 478, 479, 28 (Pork12), 37 (Casablanca16), 171 and 229. Two are under the 9 px minimum at the screen factor 1 of both handhelds:
Garmond8 (the masthead's three small lines, 8 px) and **Garmond6 (481, 6 px)**: the telegram's form draws nine of its eleven legend lines in Garmond6 (0x5CA538; ledger WR20)
and any markup may name it. Garmond9 and Courier10 are at or above the minimum.

**Rule for the fallback panel (ARC-UI-1428, owner's whole-number policy kept):** a reading-window line whose original font is under 9 px at the screen factor is drawn with a **text-only whole
factor**, the smallest that reaches 9 px: `AreaUiScale.TextOnlyFactor` (Garmond8 × 2 = 16 px, Garmond6 × 2 = 12 px at factor 1; a font already at 9 px or
above, or at screen factor 2 and up where 8 × 2 = 16, is not multiplied). Only the glyphs are multiplied; the window art keeps the screen factor, hit areas are
not changed. The diagnostic panel (`AreaReadingPanel`, the fallback when the art or a font did not read) draws with the viewer's fallback font at that size: every text has an explicit
size, a header line's size is `AreaReadingText.HeaderLinePx(font art, screen factor)` = the original size × screen factor × text-only factor, 9 to 32 px at factor 1,
16 px for a font whose file name carries no size; the body 16, the panel's other texts 12 to 14.

**Rule for the original-look window (ARC-UI-1462, issue #1462; owner decision pending, recommended by the thread):** the window drawn with the original fonts and art
(`AreaReadingArtView`) draws a line of Garmond8 or Garmond6 that would be under 9 px with **Garmond9** (art 483, the newspaper text's own font, one size up in the same
family) instead of multiplying the glyphs: `AreaReadingText.ShownFont(font, screen factor)`; at screen factor 2 and up nothing changes, and when Garmond9 did not read the line keeps its own
glyphs. The text-only factor was built and tried first on the real art at 1280 × 800 (screen factor 1): the printed masthead band between the two rules is 13 px high, so
glyphs doubled to 16 px strike through the rules, the centre tagline (about 400 px wide) needs two rows and its second row falls on the headline and the third column; the telegram's printed boxes
(about 46 px wide) are sized for 6 px glyphs, and at 12 px the labels fill and cross them. Garmond9 sits in the masthead band in one row between the "Price" lines (no
re-break is needed); in the telegram's boxes the 9 px legend is legible but wider than its printed boxes (the labels "CLASS OF SERVICE" and "NR-No Response" cross the box edge), which the art
cannot avoid at 9 px (alternative the owner may choose: keep Garmond6 there as an exception for the printed legend, not for the telegram's text, which is Courier10).
`tools/research/ui_text_scale.py` lists the reading windows (a row flagged `text_factor` and `substitute=483`) and prints `pass (substitute Garmond9Font.ART, 9 px)` for such a line; the gate still
fails on any other window or Godot label under 9 px.

## Not covered, and open

- The **reading windows** (telegram, newspaper, notes, books, plaques) draw with the original fonts and art since ARC-UI-1877; their text rule is the section above. Physical
  readability of the 9 px Garmond9 on a 7 inch screen is NOT VERIFIED.
- The scan covers `AddThemeFontSizeOverride` sizes, `FontSize` constants of the files that draw with `DrawString`, and the inventory and barter
  `Math.Max(floor, N * _scale)` sizes; a label left at the theme's default size or a computed `DrawString` size elsewhere is not seen (a size it cannot
  resolve fails, it is never skipped).
- `AreaUiScale` holds the factor (used by every window through `AreaGameBarPanel.ScreenFactor`) and the two text limits (used by the hotkey bar); the script
  mirrors the factor, `AreaUiScaleTests` and `--self-test` keep both in step.
- Hit areas larger than the art (touch, controller focus) belong to the schemes' own slices of the epic.
- **Human gate:** the owner picks the final scale on a real 7 inch screen (the Deck) and on the 1080p handheld; physical readability is NOT VERIFIED here.

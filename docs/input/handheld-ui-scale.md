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

The only text under the 12 px recommendation is Garmond9 on the character sheet's skills and tech pages, which sits exactly on the 9 px minimum.
Its capital letters are 7 rows high; if the owner reads "9 px" as a capital height instead of the font size, that text and Flare12's 9-row capitals
are the margin to judge on the device (human gate below).

The Godot-label drawing (the fallback without the player's fonts and a few viewer-only labels) had one size under the rule: the hotkey bar's
stand-in names at 8 px. They are 9 px now (`AreaUiScale.MinimumTextPx`). The other literal sizes are 9 to 13; the save list's invisible keyboard
field keeps size 1 on purpose and is exempt.

## Measuring

```sh
python3 -B tools/research/ui_text_scale.py                  # 1280x800 and 1920x1080, installation from ARCANUM_GAME_DIR
python3 -B tools/research/ui_text_scale.py --size 1280x800 --game-dir PATH
python3 -B tools/research/ui_text_scale.py --self-test      # synthetic fonts, window table checked against the code
```

It reads the player's fonts from the installation (nothing is copied or written), prints the three heights per window and factor, scans the
literal Godot-label sizes, and exits 1 on any text under 9 px or a screen that does not fit. The window table lists the fonts each window draws;
`--self-test` checks that every cited source file still names them. A window added later (or a font changed) must be added to the table.

## Not covered, and open

- The **reading windows** (telegram, newspaper, notes) are not drawn with the original fonts yet (`AreaReadingPanel`: "fonts are not drawn"). The original's
  newspaper masthead uses Garmond8 (size 8, below the rule) and Garmond6 would be 6; when that window is built the text needs a decision
  (a text-only factor 2 for those lines, or the viewer's fallback font at 9 px or more) before it ships on the handheld.
- The inventory, barter, logbook and reading windows draw their text with Godot labels, not the original fonts; their sizes are in the scan above.
- Hit areas larger than the art (touch, controller focus) belong to the schemes' own slices of the epic.
- **Human gate:** the owner picks the final scale on a real 7 inch screen (the Deck) and on the 1080p handheld; physical readability is NOT VERIFIED here.

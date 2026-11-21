# Surface method study: strips and window background at the master size (ARC-RES-2457)

Status: **study done, method A picked by the owner** ([#2458](https://github.com/Simarglok/arcanum-steamwoven/issues/2458)), 2026-11-21
([#2457](https://github.com/Simarglok/arcanum-steamwoven/issues/2457), epic [#2446](https://github.com/Simarglok/arcanum-steamwoven/issues/2446)).
Requirement touched: [FR-UI-007](../requirements/ui-media-lifecycle.md#fr-ui-007) (status unchanged). Linked from the TRD
([SWI-GEN-5](steamwoven-interface-trd.md), SWI-VER-5). Every image of the study is local (`local/steamwoven-ui/compare/surface-study/`, never in git);
this page holds the method and the numbers only.

## Inputs

The upscaled ORIGINAL of the inventory screen (800 x 600 units, 3.6 px per unit, 2880 x 2160 px) is the image Codex is given to edit, in three zones that
tile the composition exactly (the owner's one-centred-4:3 rule, no outward backdrop):

| zone | units | master px | aspect |
|---|---|---|---|
| top strip | 800 x 41 | 2880 x 148 | 19.5:1 |
| window | 800 x 400 | 2880 x 1440 | 2:1 |
| bottom strip | 800 x 159 | 2880 x 572 | 5.0:1 |

The three aspects are also the owner's extreme-aspect test of the generator (2:1, 5:1, 20:1). Codex is pinned to `gpt-6.1-sol` (SWI-GEN); prompts are the
library's common rules plus a zone or tile text (`tools/steamwoven_ui/surface_study.py`); the rules say: same palette, brightness and contrast as the ORIGINAL,
no ornament, no shine, dull brass, no text.

## The three methods

- **A. Generate at the generator's size, then upscale.** One Codex item per zone (`surface_study.py zones-inbox`, batch `surface-aspect`), the result resized with
  Lanczos to the zone. Window: 1774 x 887 px returned (2.0:1, 1.57 MP), upscaled x1.62.
- **B. Upscale, regenerate overlapping tiles, blend.** The window zone cut into four 4:3 tiles of 1560 x 1080 px with 240 px (horizontal) and 720 px (vertical)
  overlaps (`tiles-inbox`, batch `surface-tiles`); each returned 1507 x 1044 px (1.57 MP), was resized to its rectangle and laid over the original in painter's
  order with a linear 0 to 1 ramp over the overlap (`surface_study.py blend`; identical tiles reproduced the original exactly when checked by hand, there is no unit test for it). B covers the window only.
- **C. Seamless material plus macro image.** The wood tile (Codex attempt 2 of #2457, 1254 px, reduced to 432 px per repeat) repeated over the picture; where the
  ORIGINAL is wood (the tone check's wood class, as solid regions) the result is the original's low pass (Gaussian sigma 6 px: plank layout, tone variation) plus
  the high pass of the tiled texture (`tools/steamwoven_ui/method_c.py`, needs ImageMagick).

All three ran on the same input. Method A on the two strips needs a note, below.

## Codex at the extreme aspects

| asked | returned | outcome |
|---|---|---|
| 2880 x 1440 (2:1) | 1774 x 887 | ok, exact aspect |
| 2880 x 572 (5:1) | 2103 x 748 with black bars above and below | `wrong-aspect`: the batch tool refused it, the art band inside is 2103 x 417 (5.04:1) |
| 2880 x 148 (19.5:1) | 2103 x 748 with white bars above and below | `wrong-aspect`: the art band inside is 2103 x 107 after a 4 px shave (19.65:1) |

The generator does not return a canvas with an extreme aspect; it keeps a roughly 2.8:1 canvas and **draws the piece at the right proportion inside it**, padded.
Cropping the band (a 4 % fuzz trim plus a shave of 2 to 4 px for the white anti-aliasing line) and resizing it to the zone gives a usable strip (aspect error
at most 1 %); without the shave the band keeps a light line at its edge and the seam at the join rises to 12.8. The strips are thus upscaled by x1.37 (2103 to 2880 px) and the window by x1.62 (1774 to 2880 px). The two
refused items are not accepted outputs of the tool (no provenance record); their files stay in the work folder.

## Metrics

`tools/steamwoven_ui/surface_metrics.py` (standard library; tests in `test_surface_metrics.py`):

- **Seam ratio at a join** (a strip against the window zone, y = 148 and y = 1588; SWI-VER-5): the mean colour step across the join over the mean step between
  neighbouring lines in 24 lines on each side; 1.0 is as smooth as the inside, the gate is 1.5. The band is 24 lines, wider than the 8 px band of the U4 measure that SWI-VER-5 cites
  ([hd-interface-art](../rendering/hd-interface-art.md#9-slice-side-pieces-u4)), so the gate of 1.5 is carried over, not identical. The designed split of a two-part window is exempt.
- **Repetition**: peak of the normalised autocorrelation of the high-passed luma (reduced x8, lags beyond the texture's own width, up to half the axis), along x
  and y; a tiled texture shows a peak near 1 at its period.
- **Sharpness** at the master step (x1) and after a x1/3 reduction: the mean gradient magnitude of the luma. An upscaled original has little at x1 and more at
  x1/3 (ratio above 1); a picture with real detail at the master size keeps both equal.

Repetition and sharpness are read **only on the wood** (`method_c.py --mask-only`: the wood class mask made into solid regions, 17.7 % of the composition), so
black fields and metal do not decide them; the seams are whole-line. Tone is the project's `tone_check.py compare` against the ORIGINAL (all four PASS).

| | sharp. x1 | sharp. x1/3 | seam y=148 | seam y=1588 | repetition x / y | tone dL* | contrast | dE mean / p95 |
|---|---|---|---|---|---|---|---|---|
| Original, upscaled | 3.96 | 7.16 | 1.82 | 1.73 | 0.016 / 0.107 | 0 | 0 | 0 |
| A, zones as single images | 7.95 | 8.53 | 3.61 | 3.57 | 0.023 / 0.099 | -1.48 | -15.1 % | 3.16 / 9.85 |
| B, four overlapping window tiles | 5.86 | 6.98 | 1.31 | 5.39 | 0.030 / 0.147 | -0.99 | -2.5 % | 1.33 / 4.91 |
| C, wood tile plus macro | 5.74 | 5.58 | 1.05 | 1.24 | 0.032 / 0.081 | +0.04 | +0.8 % | 0.22 / 0.96 |

(A's contrast change is under the tone check's absolute floor of the spread, so it passes; the numbers are the tool's output.)

## Reading

- **A** has the most real detail at the master size (x1 sharpness doubles, x1 and x1/3 close) and no repeat, and it covers all three zones. Its weak points: the
  whole picture is darker in contrast (about -15 %, still inside the gate), the joins between independently generated zones do not match (seam 3.6, over the
  gate of 1.5; the original's own 1.8 is also over it because the strips meet the window at a designed edge), and the generator redraws the zones and so drops the
  text and counters the ORIGINAL has (expected: the no-text rule).
- **B** stays closest to the ORIGINAL (tone and contrast within 3 %) but adds the least detail, covers the window only (the seam to the bottom strip is 5.4
  because the strip is not regenerated), and four tiles of one 4:3 prompt each is the cost of one window. No tile seam is visible in the overlaps on a 1000 x 1000 px crop.
- **C** has the best tone and joins (seam 1.05 and 1.24) and adds detail, but the tile used here fails its own gate (#2457 comment: attempt 2 dL* -3.91, seam 2.27),
  the 432 px repeat gives a repeat peak of only 0.03 on the wood (low because the original's plank variation, kept by the macro, hides it), and the macro low
  pass **washes out the carved squares and edges inside the wood class** (the top strip's two bevelled squares lose their shading), which is visible in the
  composite. It would need a mask of its own that keeps those parts.
- The x1/3 readings of A and B stay above C's, so at the third step C is the softest.

## Owner pick and conditions (2026-11-21)

**Method A for all three zones** (strips and window); B and C are dropped. A gives the **wooden surface with its relief and nothing else**; the conditions
for the production surfaces, which the composite below already meets with the original's parts standing in for the kit:

1. Text, counters, digits and fields are not part of the generated surface; the game draws them over it. (The generated picture covered them; that is no loss
   of the method.)
2. Zones filled in play (the portrait circle, text plates, counter windows, grids) are taken by the ORIGINAL's mask, never from the generation (Codex had drawn
   its own scene in the portrait circle).
3. Metal parts (the infinity sign, the symbols on the plates, the buttons) come from the kit as in the pilot [#3058](https://github.com/Simarglok/arcanum-steamwoven/issues/3058),
   not redrawn by Codex in every zone.
4. Seams at the strip joins: not worse than the original's (1.82 and 1.73); the target is at most 1.8 (A alone: 3.6).
5. Wood tone: mean L* and contrast (and chroma) fitted to the ORIGINAL by the wood mask; random dark specks removed, the original's rows of dots kept.

### A as the player sees it

`tools/steamwoven_ui/surface_compose.py compose` builds the prototype of that composition (local, 2880 x 2160): the Codex wood replaces the ORIGINAL only where
the original is wood (the wood class as solid regions, a 6 px margin and a 3 px soft edge); everything else, text and fields and metal parts, is the original's
pixels, the stand-in for the kit parts until the pilot chain delivers them. In the wood: dark specks (more than 12 levels below the 9 px horizontal median) are
replaced by that median, so the grain running along x survives; the original's rows of dots (faint dark dots, six or more in a row, found at 6 levels) are put
back; L*, contrast and chroma are fitted by the wood mask the tone check uses; about 3 px each side of both strip joins stay fully the original's (a 12 px band, blended by a 2 px soft edge). On the inventory screen 98 650 speck pixels were replaced and 388 dots of the original's rows kept.

| | wood L* | wood contrast | wood chroma | seam y=148 | seam y=1588 | sharp. x1 / x1/3 | repetition x / y | tone_check dL* / contrast / dE mean, p95 |
|---|---|---|---|---|---|---|---|---|
| Original | 20.13 | 3.94 | 9.54 | 1.82 | 1.73 | 3.96 / 7.16 | 0.016 / 0.107 | |
| A as generated, assembled | 17.64 (after the speck clean) | 3.49 | 9.89 | 3.61 | 3.57 | 7.95 / 8.53 | 0.023 / 0.099 | -1.48 / -15.1 % / 3.16, 9.85 |
| A as the player sees it | 20.04 | 3.82 | 9.44 | 0.96 | 1.11 | 8.67 / 9.60 | 0.072 / 0.111 | +0.06 / -0.3 % / 0.97, 5.02 |

The remaining gap to the production state: the kit parts replace the original's metal stand-ins (pilot #3058, [#2459](https://github.com/Simarglok/arcanum-steamwoven/issues/2459));
the game draws text and counters over the surface; the extreme-aspect strips are cropped by a tool step ([#3083](https://github.com/Simarglok/arcanum-steamwoven/issues/3083));
the composition is a script today, not a pipeline step. Repetition and sharpness bounds are not added to SWI-VER-5 here; the seam ratio stays the only gate.

## Reproduction

```sh
python3 -B tools/steamwoven_ui/surface_study.py zones-inbox --original ORIG_2880x2160.png
python3 -B tools/steamwoven_ui/surface_study.py tiles-inbox --original ORIG_2880x2160.png
python3 -B tools/steamwoven_ui/codex_batch.py run surface-aspect
python3 -B tools/steamwoven_ui/codex_batch.py run surface-tiles
python3 -B tools/steamwoven_ui/surface_study.py blend --original ORIG_2880x2160.png --out B.png --work WORKDIR
python3 -B tools/steamwoven_ui/method_c.py --original ORIG_2880x2160.png --tile TILE.png --out C.png
python3 -B tools/steamwoven_ui/method_c.py --original ORIG_2880x2160.png --out MASK.png --mask-only
python3 -B tools/steamwoven_ui/surface_compose.py compose --original ORIG_2880x2160.png --a A.png --out A_PLAYER.png --work WORKDIR
python3 -B tools/steamwoven_ui/surface_metrics.py --mask MASK.png --join y=148 --join y=1588 RESULT.png
python3 -B tools/steamwoven_ui/tone_check.py compare ORIG_2880x2160.png RESULT.png
```

The three strip results of A are made from the work folder files by a fuzz trim, a shave and a Lanczos resize with ImageMagick (this page's "Codex at the
extreme aspects"); that step is not yet a tool command.

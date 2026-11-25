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
pixels, the stand-in for the kit parts until the pilot chain delivers them. In the wood: single dark specks (a small compact component, at most 8 pixels in a 4 x 4 box, of pixels more than 12 levels below the 9 px horizontal median) are
removed pointwise, each pixel and its 1 px halo taking the mean of its row neighbours outside the speck, and only where it is darker than both of them (a pixel beside a
bright line is left), so the grain running along x survives and grain strokes are not touched (the first version replaced every such pixel by the median, which
left even corduroy-like hatching on narrow wood strips; ARC-UI-3104); the original's rows of dots (faint dark dots, six or more in a row, found at 6 levels) are put
back; L*, contrast and chroma are fitted by the wood mask the tone check uses; about 3 px each side of both strip joins stay fully the original's (a 12 px band, blended by a 2 px soft edge). On the inventory screen 388 dots of the original's rows were kept (the first version replaced 98 650 speck pixels; the pointwise version removes 10 551 specks).

Owner review of 2026-11-21 (ARC-UI-3104, issue #3104) added three fixes to the top strip, all in `surface_compose.py` and local-only like the rest:

- **Blotches.** The dark sharp blotches on the wood were the original's darkest stains, which the wood class does not take: holes in the wood weight where the
  original's dark smear showed against the sharp Codex wood. The wood weight is closed (a 10 px disk) before the joins are kept, so a non-wood notch too narrow to hold the disk is wood.
- **Square bevel.** The original's squares have a light right and bottom bevel; the wood weight (blurred, eroded 6 px) swallowed the 5 px right line and the Codex
  wood redrew it thinner. The light vertical lines of the top strip (rows 0 to 148, at least 40 px tall, at most 10 px wide, found by an opening with a 1 x 30 line so
  the bottom bevel is not taken) are put back from the original, by default under the Codex line (`--bevel lighten`: the lighter of the two) or alone (`--bevel original`).
- **Chip.** The white rim at the right plate's tip (a box of the inventory master, `CHIP_BOXES`) is filled with the plate's darkest metal to its right. Other windows need their own boxes. The light glint at that plate tip is part of the kit's metal (production condition 3), not of the wood; filling it with dark metal is temporary, until the kit parts replace the original's stand-ins.



| | wood L* | wood contrast | wood chroma | seam y=148 | seam y=1588 | sharp. x1 / x1/3 | repetition x / y | tone_check dL* / contrast / dE mean, p95 |
|---|---|---|---|---|---|---|---|---|
| Original | 20.13 | 3.94 | 9.54 | 1.82 | 1.73 | 3.96 / 7.16 | 0.016 / 0.107 | |
| A as generated, assembled | 17.64 (after the speck clean) | 3.49 | 9.89 | 3.61 | 3.57 | 7.95 / 8.53 | 0.023 / 0.099 | -1.48 / -15.1 % / 3.16, 9.85 |
| A as the player sees it | 20.04 | 3.82 | 9.44 | 0.96 | 1.11 | 8.67 / 9.60 | 0.072 / 0.111 | +0.06 / -0.3 % / 0.97, 5.02 |

The remaining gap to the production state: the kit parts replace the original's metal stand-ins (pilot #3058, [#2459](https://github.com/Simarglok/arcanum-steamwoven/issues/2459));
the game draws text and counters over the surface; the extreme-aspect strips are cropped by a tool step (`codex_batch.py crop`, [#3083](https://github.com/Simarglok/arcanum-steamwoven/issues/3083));
the composition was a script then, now a pipeline step ([below](#production-pipeline-step-arc-ui-3100)). Repetition and sharpness bounds are not added to SWI-VER-5 here; the seam ratio stays the only gate.

## Production pipeline step (ARC-UI-3100)

Issue [#3100](https://github.com/Simarglok/arcanum-steamwoven/issues/3100), parent of the production batches (strips [#2927](https://github.com/Simarglok/arcanum-steamwoven/issues/2927), windows). The prototype's composition is now a tool step,
`surface_compose.py build` ([toolchain](../workflow/toolchain.md#steamwoven-surface-pipeline-arc-ui-3100)); the conditions of the owner pick map onto it like this:

| Condition | In the step |
|---|---|
| 1. Text, counters, digits, fields drawn by the game | `erase_boxes` of the zone spec: a box is filled with the per-channel median of the 2 px ring outside it, so a baked text or counter is not part of the surface and the game draws it. The boxes are per window (the census column `baked_text`, [#3037 table](steamwoven-interface-trd.md#elements-the-old-arts-held-arc-ui-3037)); none is set for the inventory, whose original art has no baked text. |
| 2. Zones filled in play by the original's mask | unchanged: only the original's wood class is taken from the Codex result, everything else is the original's pixels (the portrait circle, plates, grids, counter windows). The mask is the tone check's wood class of the zone's screen (`screen` of the spec, `prompts/tone_masks.json`), so each window family has its own. |
| 3. Metal parts from the kit | `kit_parts` of the spec: a kit master laid over the result at master pixels (straight alpha, over), replacing the original's stand-in; its file hash is in the record. Until a part exists (the hardware batch [#2481](https://github.com/Simarglok/arcanum-steamwoven/issues/2481); the pilot [#3058](https://github.com/Simarglok/arcanum-steamwoven/issues/3058) made the round bottom-strip buttons, which reach the game inside the pack's frames) the original's pixels stand. |
| 4. Seams | measured at every join of the spec (`joins`, default the two strip joins), gate: at most 1.8; SWI-VER-5's 1.5 is recorded beside it (`seams_within_swi_ver_5`). |
| 5. Wood tone | as the prototype (mean L*, contrast, chroma fitted by the wood mask, single dark specks removed pointwise, the original's rows of dots kept); the numbers of the original, the Codex wood and the result are in the record, and the tone check of the whole screen (`tone_check.compare_screen`) is the second gate. |

The record `provenance/surface/<zone id>.json` (schema `steamwoven-surface/1`, local, no pixels): the tool and the date, the zone spec, the file names and SHA-256 of the ORIGINAL, of method A's assembled image and of the result, the counts (specks, dots, bevel lines, chip and erased
pixels), the kit parts laid (id, hash, size, place), the wood numbers, the seams, the tone regions and the gates. The record is written whatever the gates say; exit 1 tells the caller a gate failed (a join list that is empty, or a spec of the wrong shape, is an error, exit 2: no join measured is no seam gate passed). The zone id and the batch name are plain file names. Codex's own provenance (SWI-GEN-6) stays with the zone image in the outbox; its hash is the method A input's.

`surface_study.py zones-inbox --batch B --zones window --screen-name NAME` makes the inbox of any screen's zones and `assemble` puts the three zone results into the 2880 x 2160 composition (the strips' results of one screen are reused for every family: the strips of the character, logbook, reading and schematic contexts differ from the inventory's by at most 0.2 % RMSE, and the compose step takes the Codex wood only where the screen's own wood mask says wood).

**Run, 2026-11-24:** the inventory screen through `build` with the prototype's inputs: seams 0.95 and 1.10, tone PASS, wood L\* 20.08 against 20.13, contrast 3.84 against 3.94 (the same as the prototype within the changes of ARC-UI-3104). The other families' window zones (character sheet, logbook, reading window, schematics) were made into Codex batches (`surface-3100-character`, `-logbook`, `-reading`, `-schematic`) but the four runs failed at once with the subscription's usage limit (Codex text: usage limit reached, back at 2026-11-25 02:17 local); nothing was generated, no prompt was changed, and the runs were done the next day (see the 2026-11-25 run below). The spec path (another screen, an erase box, a kit part) was exercised on the character screen with the inventory's wood as a stand-in for the missing generation (a code-path check, not a result): 6000 px erased, one kit part laid, and the gates fail as they should (seam 2.24 at the bottom join, tone FAIL on the wood, because the inventory's wood is not this window's).

**Run, 2026-11-25 (ARC-UI-3164, issue [#3164](https://github.com/Simarglok/arcanum-steamwoven/issues/3164)):** the four window zone batches ran through the owner's Codex subscription (`gpt-6.1-sol`, effort medium), one `window` item each, all ok at 1774 x 887 px (1.57 MP, 76 to 93 s each), no refusal.
Each family's A was assembled with the inventory screen's two strip results (`surface_study.py assemble`) and composed with `surface_compose.py build` (spec: the screen `ctx-<family>`, bevel band 0 to 148, joins 148 and 1588, `island_min` 4000, `dot_bands` the two strips, no erase box, no chip box, no kit part; the character screen also has `fill_boxes`; the baked text of each window is left to [#3165](https://github.com/Simarglok/arcanum-steamwoven/issues/3165)):

| Screen | seam y=148 | seam y=1588 | wood L\* (original / result) | wood contrast (original / result) | tone | wood dE mean / p95 |
|---|---|---|---|---|---|---|
| character | 0.67 | 1.47 | 19.63 / 19.69 | 3.77 / 3.67 | PASS | 3.02 / 6.85 |
| logbook | 1.00 | 1.49 | 19.72 / 19.68 | 4.76 / 4.58 | PASS | 2.82 / 6.10 |
| reading | 1.16 | 1.26 | 19.75 / 19.80 | 4.96 / 4.95 | PASS | 2.09 / 4.57 |
| schematic | 1.03 | 1.08 | 20.01 / 20.02 | 3.87 / 3.75 | PASS | 3.08 / 6.71 |

All four gates hold (seams at most 1.49, so the target 1.8 and SWI-VER-5's 1.5 are both met; tone PASS). `surface_study.py assemble` wrote a 16-bit PNG (the Q16 canvas `xc:black`), which `surface_compose.py` refuses; it now sets `-depth 8`.
Records: `provenance/surface/ctx-<family>.json`; the composites are local, under `local/steamwoven-ui/composites/ARC-UI-3164/`.

**Owner look check, 2026-11-25:** the backgrounds were accepted with three fixes, all in `surface_compose.py` and shown as 1:1 crops (original, before, after):

- **Character: patch around the stat labels and fields.** The tone mask excludes the label and field boxes whole, so the original's lighter panel wood stood there as a patch with a sharp edge against the Codex wood. A spec's `fill_boxes` (master pixels, the tone mask's boxes grown by 24 px) put the Codex wood between the items of each box: an item is an original pixel brighter than luma 105 or darker than 14 (letters, brass rims, black fields; the colour class cannot say it, the panel wood is no wood class), grown by 4 px, closed by 4 px and filled inside (a knob in a rim); the wood weight is the max of the old weight and the box's wood.
- **Schematics: leaks of the wood class into the gears, and specks in the wood.** `island_min`: the wood weight is eroded by 6 px, white islands under that many pixels are dropped, and the weight is grown back inside the original: a leak inside a gear or a bay joined to the wood by a few pixels is the original's metal again (a plain island count missed the bay above the hat, its channel was 5 px); only the dropped islands are taken out of the soft weight, so its feathered edges stay). The dark specks on the wood were the original's "rows of dots" restored (`original_dots`): in the window the faint grain specks chain into false rows, so `dot_bands` keep the dots only in the strips (rows 0 to 148 and 1588 to 2160); the Codex specks are removed pointwise as in [ARC-UI-3104](https://github.com/Simarglok/arcanum-steamwoven/issues/3104). The dark halo around the gears was left to [#3101](https://github.com/Simarglok/arcanum-steamwoven/issues/3101) (below).
- **Logbook, reading: page edges.** The same leak: the book's page-edge strip lies outside the tone mask's book zone, and blobs of it came out as wood; `island_min` drops them. The reading window's edges and both windows' right edges were clean.

The spec fields default to the old behaviour (`island_min` 0, no `fill_boxes`, no `halo_boxes`, `dot_bands` null), so the inventory result is unchanged.

**Schematics: the dark halo round the gears (ARC-UI-3101, 2026-11-25).** The wood weight is the tone mask's wood class closed to a solid blob; round each gear the blob's edge lies outside the metal, so a ring of the old art's wood fringe and shadow (darker and blurrier than the window's wood, 20 to 40 px wide) stayed as original pixels: a dark ring on the new wood. `halo_boxes` (a box round each gear and, for the lower one, its scroll) set the wood weight to 1 everywhere inside the box except the part's own shape: the shape is the original's pixels lighter than luma 70 (`HALO_BRIGHT`; the metal, the paper, the brass tassel), grown by 2 px, closed by 10 px (the dark cap and the dark gear centre) and filled inside (holes), through the same routine as `fill_boxes` with no dark items (`fill_between_items`, dark cut off; the weight is feathered like every wood weight). The gaps between the teeth and the ring round the gear are the Codex wood now; the part's pixels are the original's, its outermost 2 to 3 px feathered into the wood by the weight's 3 px blur. No new image is made by Codex: the wood already generated for the window is used, so nothing is added to the art and no stain is drawn. The gates hold unchanged (seams 1.03 and 1.08, tone PASS, wood L\* 20.02 against the original's 20.01, wood dE mean 3.09, p95 6.71). Left as they are, and not part of the halo: the dark brown stains inside the lower gear (the original art's own pixels on the metal) and the round button's own shadow (a kit part, [#3098](https://github.com/Simarglok/arcanum-steamwoven/issues/3098)).

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

The two strip results of A are made from the work folder files by the tool step `codex_batch.py crop IMAGE --size WxH --out OUT.png` (fuzz trim, shave of 2 to 4 px, band aspect check
within 1 %, Lanczos resize; [TRD](steamwoven-interface-trd.md#codex-batch-runner-arc-dev-3001), "Band crop"), and `codex_batch.py run BATCH --crop-band` does it inside a batch; the shave is now chosen per canvas to
bring the band closest to the asked aspect (7 px for the 19.5:1 canvas, 4 px for the 5:1 one) instead of a fixed 4 px.

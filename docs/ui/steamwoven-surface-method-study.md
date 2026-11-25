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
| 1. Text, counters, digits, fields drawn by the game | `erase_boxes` of the zone spec: a box is filled with the per-channel median of the 2 px ring outside it, so a baked text or counter is not part of the surface and the game draws it. The boxes are per window (the census column `baked_text`, [#3037 table](steamwoven-interface-trd.md#elements-the-old-arts-held-arc-ui-3037)). Since ARC-UI-3165 the spec also has `text_boxes`, `paper_boxes` and `wood_boxes` for text on a ground that is no flat colour, and the five families' specs are committed ([below](#baked-text-and-counters-per-window-family-arc-ui-3165)). |
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

The spec fields default to the old behaviour (`island_min` 0, no `fill_boxes`, no `halo_boxes`, no `disc_boxes`, `dot_bands` null), so the inventory result is unchanged.

**Schematics: the dark halo round the gears (ARC-UI-3101, 2026-11-25).** The wood weight is the tone mask's wood class closed to a solid blob; round each gear the blob's edge lies outside the metal, so a ring of the old art's wood fringe and shadow (darker and blurrier than the window's wood, 20 to 40 px wide) stayed as original pixels: a dark ring on the new wood. `halo_boxes` (a box round each gear and, for the lower one, its scroll) set the wood weight to 1 everywhere inside the box except the part's own shape: the shape is the original's pixels lighter than luma 70 (`HALO_BRIGHT`; the metal, the paper, the brass tassel), grown by 2 px, closed by 10 px (the dark cap and the dark gear centre) and filled inside (holes), through the same routine as `fill_boxes` with no dark items (`fill_between_items`, dark cut off; the weight is feathered like every wood weight). The gaps between the teeth and the ring round the gear are the Codex wood now; the part's pixels are the original's, its outermost 2 to 3 px feathered into the wood by the weight's 3 px blur. No new image is made by Codex: the wood already generated for the window is used, so nothing is added to the art and no stain is drawn. The gates hold unchanged (seams 1.03 and 1.08, tone PASS, wood L\* 20.02 against the original's 20.01, wood dE mean 3.09, p95 6.71). Left as they are, and not part of the halo: the dark brown stains inside the lower gear (the original art's own pixels on the metal) and the round button's own shadow (a kit part, [#3098](https://github.com/Simarglok/arcanum-steamwoven/issues/3098)).

**Round parts: lenses, oval and round buttons (ARC-UI-3101, 2026-11-25).** The same old-shadow problem stands round every round part of the five surfaces (the lens of the inventory family, the character sheet, the logbook, the reading window and the schematic window; the barter's hand and arrow ovals; the three round buttons and the selected-page frame of the character sheet; the two round sockets of the schematics): the colour class leaves a square or a rectangle of the old art's wood and shadow round the part as original pixels, and a leak of the class sits inside the oval's parchment. A light shape cannot say where such a part ends: the lens's rim has dark stretches (its lower right is luma 70 to 90, the old wood beside it is 60 to 85), so the closed shape left the interior open and the lens was repainted by the generator. The part is round, so the shape is exact: `disc_boxes` entries (x0, y0, x1, y1, dx0, dy0, dx1, dy1), a box and inside it the bounding box of the part's rim; the wood weight is 1 in the box outside the ellipse inscribed in the rim box and 0 inside it (feathered like every weight), so the interior is the original's (whole but for the weight's 3 px feather at the rim, which blends the rim's outermost pixels into the wood: the lens shows the view, the oval its parchment and glyph) and everything outside the rim in the box is the window's new wood. The rim box is measured on the original, not guessed: the bounding box of the gold pixels (red at least 70 and red minus blue at least 50; the old wood is red minus blue 25 to 30), at the 0.3 and 99.7 percentiles, grown by 1 px. The box (the wood reach) is cut by hand so that it takes in the old square and stops short of any neighbour (the name plate, the doll frame, the gears). `halo_boxes` stay the rule for a part that is no ellipse (the gears and the selected-page frame; a light shape may hold old light wood, so no weight is forced to 0 inside it). The old shadow went with the old wood, so the round parts lay flat; owner request 2026-11-25: each disc gets one generated soft shadow down and to the right (light from the top left), `cast_disc_shadows`: the disc's ellipse shifted by 13 px in x and y, blurred by sigma 16 px, scaled by 0.7, multiplies the picture where the wood weight is 1 (never the part itself or a neighbour that keeps its pixels). The numbers are those of the shadows the generator painted under the schematic's round sockets (the wood is 0.35 to 0.4 of its luma at the rim, back to 1 about 40 px out), the same generator that painted the gears' shadow of #3222. The gates hold on all five surfaces (seams 0.67 to 1.49, tone PASS, wood L\* within 0.15 of the original). Looked at and left out on purpose: the inventory's dark round socket beside the ovals and the schematic's wide round button with its two arrow wells (the old lighter wood round them is the surface's own patch of the counter column and the channel; a box there makes a rectangular edge against it), and the name plate's rounded square (its own corner wood). Over a kit window the parts that the viewer draws as *separate arts* (the barter portrait and ware frame, the tech frame and the spell rings of the character sheet's pages, the skill category buttons on the stone panel) are not in these surfaces and still carry their old background in their edges: [#3101](https://github.com/Simarglok/arcanum-steamwoven/issues/3101) takes them in the next paragraph.

**Round parts drawn as separate arts (ARC-UI-3101, 2026-11-25).** The round buttons of the character sheet's pages are cut and shadowed at run time, not in a surface, because the viewer draws them over the kit's sheet from the original's (or the pack's) frame: the sixteen college rings of the spells page, the four skill group buttons (the hat is 304) and the eight discipline buttons of the technology page (their edge pixels carry the old shadow and the dark anti-aliasing stair as opaque pixels). Each is a circle, so the rule is the surface rule's: `AreaKitRoundParts` (`tools/terrain-viewer/Core`) holds, per art, the circle of the ring's gold measured on the original's frames (a least-squares circle through the outermost gold pixels, red at least 70 and red minus blue at least 50, every frame of the art, the median; the spread is 0.3 unit), the cut radius is that radius plus 0.4 unit (the ring's own dark outline), and `AreaKitRoundPartsLayer` (`tools/terrain-viewer/Godot`) draws the part through a canvas shader that leaves out everything outside the circle (one device pixel of edge in the art's own units, so the cut is exact for the Legacy frame and for a pack frame at any scale) and, under it, the generated shadow of the surfaces' round parts down and to the right (offset 13 / 3.6 units, sigma 16 / 3.6 units, strength 0.7, drawn only outside the disc). The shadows of a view's parts are drawn under all of its parts. Only over the kit's sheet (`AreaKitSheets`); a page on its own art draws its buttons as before, and so does a texture whose size is not the table's art size. The lens rings (arts 231, 232, 233 and 198) are not cut: the surfaces already carry the ring (the original's pixels inside `disc_boxes`), so the window draws its lens in a following step without the ring art and with the game view at the surface's lens, clipped to the circle. That step is the next paragraph. The schematic window's eight discipline tabs (arts 355 to 369) are parchment banners, not round.

**Lens: one ring, the game view in the ring's circle (ARC-UI-3101, #3276, 2026-11-25).** The surfaces keep the original's ring pixels where each ring was measured, but the window put its lens at the kit's common place (11, 9) and drew the ring art (231, 232, 233, 198) over it, so the logbook and the schematic window showed two rings, offset by 14 and 40 units, and the square copy of the game view left the old square's corners open over the wood. Now a surface that carries its ring (`AreaKitLens`, `tools/terrain-viewer/Core`: the inventory, the character sheet, the logbook and the schematic window) sets `AreaLensPlace.Surface`; the lens is then the circle inside the surface's ring and the windows draw no ring art (one ring). The circle is measured on the built surfaces: the inner edge of the ring's gold (red at least 70, red minus blue at least 50; a least-squares circle through the first gold pixel of 360 rays, outliers beyond 5 master pixels dropped) is 46.3 units in radius on all five, centred at (55.87, 51.62) in the inventory, (56.71, 52.61) in the sheet, (69.83, 66.80) in the logbook and (94.79, 68.78) in the schematic window (the reading window keeps its own place and ring art; a unit is 3.6 master pixels, the master's top 150 rows are the bevel band); the lens is the 46-unit circle round the centre, its square 92 units at the corners (10, 6), (11, 7), (24, 21) and (49, 23), within half a unit of the measurement, and the inventory's lens bottom (y 98) stays clear of the item grid (y 107). The game view's copy (`AreaRoundView`) and the background's hole (`SteamwovenKitPieceView`) are the circle inscribed in that square, not the square (#3276: the corners are the surface's own wood), also for the loot, barter and map backgrounds, whose ring art stays. The copy keeps the original's scale (a 3 percent larger view over the 92 units). When the kit's lens ring part replaces the baked ring ([#3296](https://github.com/Simarglok/arcanum-steamwoven/issues/3296)), the surfaces lose their ring and the part is drawn at this same circle; the windows already draw none.

**Recessed plate of the inventory ovals (ARC-UI-3275, 2026-11-25; the geometry of ARC-UI-3290; replaced by ARC-UI-3205).** The `disc_boxes` cut of #3249 treated one non-wood detail as old wood: the hand and arrow ovals of the inventory stand in a dark rectangular recessed plate whose four corners show as dark triangles; the cut removed them. Two interim fixes (#3287 L brackets by a pixel threshold, then analytic triangles laid on the surface by `plate_boxes`, #3294) are retired: the ovals are kit parts now (`plate.use_box_off`, `plate.drop_off`: the old HD pack's frame with an analytic alpha, the ring's ellipse united with the four corner triangles of the same geometry, 160 x 98 against the ring's 141 x 78 on the 2880 px master, see the [toolchain](../workflow/toolchain.md#steamwoven-parchment-boxes-batch-b-buttons-slice-5-arc-ui-3205)), laid by `kit_parts`, so the corners come with the part and the surface carries no plate of its own. The soft shadow stays the surface's.

**Parts that are no ellipse: the name plate, the inventory's counter column and the schematic's wide button (ARC-UI-3248, 2026-11-25).** The three leftovers that #3101 looked at and left out: (1) the inventory family's name plate had its left scroll cut at the edge of the wood weight (a rounded stub of the old wood and the scroll's tip replaced by the Codex wood); (2) the counter column of the inventory (the five icons and counters, the glyph slots, the arrow, the grid and the dark socket), x 2490 to 2866 beside the frame line, y 383 to 961, is excluded from the wood class as a whole, so the old, lighter wood between its items stood as a rectangular patch with the dark round socket in it, and a `disc_boxes` entry alone would have left the rectangle's edge; (3) the schematic's wide round button (the centre button with its ring, the brass bar and the two arrow wells, x 138 to 589, y 1304 to 1509) had the Codex wood and its own brass painted into the gaps of the wood weight (the ring's upper left, the bar between ring and right well, the ring's lower right; at one try the olive centre), because the weight leaks into the part's brass and the old wood round it is as light as the brass (luma 50 to 90), so no luma threshold tells them apart. The column is `fill_boxes` (box 2490, 375 to 2872, 972: the Codex wood between the items, the rule of #3164) plus one `disc_boxes` entry for the socket (rim box 2747, 855 to 2844, 947, as measured for the lenses), so the socket gets the window's wood and the same soft shadow as the other round parts. The plate and the button are the new `part_boxes` (x0, y0, x1, y1 and an optional closing radius, default `HALO_CLOSE` 10): a part is its **gold**, the pixels whose red and blue differ by at least `PART_GOLD` 43 in the original (brass rims, scrolls, the olive centre and the arrows; old wood is mostly 25 to 38, shadow less, but about 2 % of its pixels pass, as scattered specks, so connected groups under `PART_SPECK` 400 px are dropped first: the review of the first version found the part mask 83 000 px wide, a patch of old wood round the button), grown by 2 px, closed by the radius and filled inside (the black field of the plate, the wells), as `halo_boxes` do; the box's wood weight is then 1 outside that shape and 0 inside it (the `disc_boxes` rule for a part that is no ellipse), so the part is the original's whole and everything round it is the window's new wood. The boxes: the plate 405, 222 to 1285, 452 (the whole plate, so its black field is closed inside the box and the old plate's own pixels stay; default closing 10) and the button 120, 1285 to 610, 1530 with closing 36 (the dark shadow between the ring and the right well is no gold and a smaller closing leaves it as a notch the Codex wood is painted into; clear of the lower gear's halo box). A box must not overlap another kind's box (the passes combine by Lighten and the disc boxes run last); the closing radius is at most 60. No image is generated and nothing is added: the wood already generated for the window is used, no shadow is drawn under the plate or the button (the gears have none either), the old shadow is replaced by the new wood. The gates hold on both surfaces (inventory seams 0.94 and 1.10, tone PASS, wood L\* 19.91 against 20.13; schematic seams 1.03 and 1.08, tone PASS, wood L\* 19.97 against 20.01). Left as they are: the faint ghost of the baked counter text in the first counter field (the erase of #3165) and the old shadow inside the button's own dark outline.

## Baked text and counters per window family (ARC-UI-3165)

Issue [#3165](https://github.com/Simarglok/arcanum-steamwoven/issues/3165), owner condition 1 of method A: the surface carries no text, digit or counter the game draws itself. The census (`baked_text`, [census](steamwoven-interface-census.md)) lists the words baked into the original *arts*: art 22 (the character sheet's stat names, the dial's E and G, the ruler's divisions) and art 138 (the world map's N E S W); the arts of the inventory, logbook, schematic and save window carry none, and the character sheet has no "Health" and "Fatigue" words (the census row CHW1 was wrong: the art has a heart and a cross there). But the surfaces are composed from the **context screenshots** (`ctx-<screen>-master`), which also hold the text the game wrote on them that day: a name, stat numbers, the logbook's entries, a reading window's page, the inventory's weights and counts. Both kinds are baked into the surface and are taken out here, so the game's own text is the only text in a window. The boxes are in master pixels (2880 x 2160; a window unit is 3.6 px, and the window zone starts at y 148).

| Family (spec) | Baked element | Master box(es) | Taken out by | Drawn by the game |
|---|---|---|---|---|
| Character (`ctx-character`) | stat names ST CN DX BE, IN WP PE CH (art 22), on the wood | 8 boxes, x 452..598 and 928..1098, rows y 552..642, 702..800, 848..934, 996..1082 (left), 556..655, 704..790, 848..934, 994..1082 (right) | `wood_boxes`: the Codex wood shows there | `AreaKitStandIns.SheetLabels` over the kit's background, `AreaCharacterSheetArtView`; the kit smoke reads the 8 names back |
| | dial letters E and G (art 22), the old needle above the hub | 156, 686, 250, 732; 194, 662, 214, 748 | `text_boxes` (parchment) | `SheetLabels` (E, G); the needle is art 254, the sheet's |
| | ruler divisions 75 50 25 0 25 50 75 (art 22), parchment | 7 boxes, x 2750..2802 (2750..2790 for the 0), y 589..613, 697..733, 834..858, 935..969, 1057..1082, 1165..1200, 1295..1318 | `text_boxes` | `SheetLabels` (the 7 divisions, at the places of the kit smoke's list) |
| | "Health" and "Fatigue" words | none (symbols only) | not baked | the heart and the cross are fixtures (`fixture.health_disc`, `fixture.fatigue_disc`) |
| | screenshot text: name box (name, level, points, XP, race, gender, age), the eight stat numbers, the health and fatigue numbers, the dial's number, the ruler's two end numbers, the derived-stats and resistance text | 756, 208, 1556, 482; 8 x 44 x 46 at x 740 and 1243; 1582, 684, 1636, 722; 1578, 938, 1640, 982; 172, 872, 244, 918; 2735, 398, 2795, 442; 2745, 1474, 2790, 1519; 165, 1165, 1080, 1522; 1130, 1165, 1600, 1522 | `text_boxes` | the sheet's own text (`AreaCharacterSheetArtView` and its panel) |
| Inventory (`ctx-inventory`) | none in arts 221 and 223; screenshot text: the name plate's weight, encumbrance and speed, the five count numbers, the weapon and shield armour numbers | 512, 252, 1075, 418; 2622, 416 + 83k, 2712, 452 + 83k (k = 0..4); 336, 1434, 392, 1489; 930, 1434, 1002, 1489 | `text_boxes` | the inventory window's own text and counters |
| Logbook (`ctx-logbook`) | none in arts 260 and 264; screenshot text: the page titles, the entries, the page numbers, on the parchment | 790, 300, 1570, 1446; 1676, 300, 2420, 1446; 2420, 540, 2468, 1446 | `paper_boxes` | the logbook's own text layer |
| Reading window (`ctx-reading`) | none in the book arts; screenshot text: the title and both pages of a book | 800, 340, 1560, 1388; 1700, 340, 2420, 1388 | `paper_boxes` | the reading window's own text |
| Schematic (`ctx-schematic`) | none in art 365; the title, the description and the slot counts stand on the blueprint page (201, 13, 506, 367 units) and on the two slot boxes, which are fixtures (`fixture.schematic_page`, its ruling and `fixture.schematic_slot`) | none | not touched: the fixtures replace the page and the boxes whole, and the ruling and the page's grid would be cut by any fill | the window's own text over the fixtures |
| World map (art 138, [#3036](https://github.com/Simarglok/arcanum-steamwoven/issues/3036), [#3112](https://github.com/Simarglok/arcanum-steamwoven/issues/3112)) | compass letters N E S W | the rose, with its kit part | `AreaWorldMapArtLayout.CompassLetters` already written by the window; the rose and the legend are #3112 | the window (dark red font), read back by the kit smoke |

**Erased caption, and who draws it** (owner condition before the merge, 2026-11-25: the constant captions are erased together with the dynamic text, and a text-free background shows only where the window's label layer is there, all or nothing). Positions are window units (800 x 400) of the original's own layout; the "who" is the code that writes it today:

| Erased caption | Who draws it, with which font and where |
|---|---|
| Stat names ST CN DX BE, IN WP PE CH (art 22) | `AreaKitStandIns.SheetLabels`, written by `AreaCharacterSheetArtView` in the sheet's Cloister font (`AreaSheetFont.Cloister`, the original's art 171 face), gold, at (131, 115 + 40 r) and (263, 115 + 40 r), the places measured in art 22 (#3037); drawn exactly when the kit's character background shows |
| Dial letters E and G (art 22) | `SheetLabels`, Cloister, dark ink, at (38, 146) and (66, 146); drawn with the stat names |
| Ruler divisions 75 50 25 0 25 50 75 (art 22) | `SheetLabels`, Cloister, dark ink, at (766, 120 + 32 i); drawn with the stat names |
| "Derived Stats" and "Resistance" (screenshot text) | `AreaCharacterSheetArt` text list, the Label font, white, centred at (178, 282) and (383, 282) from the exe's sheet draw (0x55A240, ledger CHW); drawn with every background, art 22 holds neither word |
| Name, "Level:", "Points:", "XP Next:", "Race:", "Gender:", "Age:" lines, the eight stat numbers, the health and fatigue numbers, the dial's number, the ruler's two end numbers, the derived and resistance rows | the same text list: Label, Flare (rows) and Cloister (dial, ruler) fonts at the exe's places (212 / 337, 15..72; 211 / 350, 115 + 40 r; 447, 143 and 213; 57, 198; 767, 66 and 365; 49 / 178, 308 + 18 r; 322, 301 + 16 r) |
| Logbook "-Quests-" (the tab's title, both pages) | `AreaLogbookArtView`: the title font, centred in the two rectangles of `AreaLogbookArtLayout.Titles` ((236, 43, 187, 19) and (482, 43, 187, 19), exe 0x53F6E0), any background |
| Logbook page numbers "-1-", "-2-" | `AreaLogbookArtView`: the text font, centred in `AreaLogbookArtLayout.PageNumbers`, the same exe draw |
| Logbook entries (dates, states, texts, the struck-through finished quest) | `AreaLogbookArtView`: glyph runs of the text font in the two columns of `AreaLogbookArtLayout.Columns`, colour and strike by entry state |
| Reading window title and both pages | `AreaReadingArtLayout` / `AreaReadingFlow`: the book's own text laid out into the two pages |
| Inventory name plate lines, the five counts, the weapon and shield numbers | the inventory window's own text and counters |

**All or nothing.** The character sheet's three baked captions are drawn only over the kit's background: `AreaCharacterSheetArtView.KitBackgroundShown` switches the stand-ins, the 17 labels and the missing art 22 together, so a text-free character surface never shows without its labels (the kit smoke reads the 17 labels back, and none with the art's own background: ARC-UI-3165). The sheet's other captions and the logbook's, the reading window's and the inventory's text are not part of any art and are drawn over any background. None of the erased surfaces is installed as a kit part by this change (the kit's window pieces are still planned: `kit.json`); the look of the real window text over the character surface is captured in the viewer (`ARC_STEAMWOVEN_KIT_LOOK=DIR`, the kit smoke's look hook).

How the three fills work (`surface_compose.py`, constants `TEXT_*`):

- **`text_boxes`** (a black field, a disc, a pit with a gradient, a flat parchment): the ground is the median luma of the 3 px ring outside the box; a pixel is text when it differs from it by more than 35 (or three standard deviations of the ring, whichever is more), the mask is grown by 8 px, narrowed step by step down to 2 px when the box would keep under 20 % ground (the resampled art's ringing is darker than the field and shows as a ghost otherwise), and each text pixel takes the mean of the ground pixels around it (the narrowest window of 8, 16, 32 or 64 px that holds enough of them), smoothed once, plus the ground's own fine texture from a fixed offset (nothing is painted that was not there). The fill is the surroundings, not a flat colour: a gradient continues through the box.
- **`paper_boxes`** (dark ink on a shaded page): the page's light changes by tens of levels from the spine to the edge, so "differs from the box's median" would take half the page. The text is what a grey-scale closing of the red and the green channel (21 px, 28 levels) removes; red and green because every ink is dark there (blue ink is bright in blue). The mask is grown by 3 px and filled as above.
- **`wood_boxes`**: the wood weight is 1 in the box (feathered like the rest), so letters baked into the wood are not kept as items and the Codex wood shows in their place, with its own grain. A flat fill of the original's wood would be a patch.

A box is refused when more than 80 % of it differs from its ground (it is a picture, not text) or lies outside the image; the provenance record counts the replaced pixels (`counts.text_pixels`). What is **not** text and stays, as condition 2 and 3 say: the portrait, the items, the coins, the round view and its debug overlay, the sheet's skills page and gauges, the needle, the schematic's picture; the fixtures ([#3098](https://github.com/Simarglok/arcanum-steamwoven/issues/3098)) and the production kit parts replace the plates, pits and discs they stand on.

The tone gate of a spec with text or paper boxes excludes those boxes (so the paper gate of the logbook and the reading window, whose page boxes cover most of each page, says little about the page tone; their other gates, wood and brass, are unchanged) (`tone_masks_for`, in 800 x 600 layout units): the text the game draws is taken out on purpose, and a page with its text erased would read as a brighter paper (the reading window's first build failed the paper gate by 3.5 L\* for that reason).

**Run, 2026-11-25:** the five specs through `build` with the inputs of ARC-UI-3164 (the inventory's of ARC-UI-3100); all gates hold. The text pixels replaced, per surface: character 348 956, inventory 73 638, logbook 550 426, reading 1 038 140, schematic 0. Other numbers as before (the text the game draws does not touch the wood).

| Screen | seam y=148 | seam y=1588 | wood L\* (original / result) | tone |
|---|---|---|---|---|
| character | 0.67 | 1.47 | 19.63 / 19.69 | PASS |
| inventory | 0.95 | 1.10 | 20.13 / 20.08 | PASS |
| logbook | 1.00 | 1.49 | 19.72 / 19.68 | PASS |
| reading | 1.16 | 1.26 | 19.75 / 19.80 | PASS |
| schematic | 1.03 | 1.08 | 20.01 / 20.02 | PASS |

Look notes for the owner: the alignment dial keeps a slightly paler patch where the needle's upper part and the caption were taken out (the dial is a fixture and the kit part covers it); the schematic page's title, description and slot counts stay in the surface because the page and the slot boxes are fixtures (a fill would cut the page's grid), the owner may ask for an erase there as well.

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

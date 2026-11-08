# HD interface art: methods for panels, frames, buttons and small art (ARC-RES-094)

Status: **research only, 2026-11-07; U3 (redraw + fit) run on the owner's 12 redraws, strips and icons not usable in round 1; round 2 (refined tiles) is a candidate, not gated.** Work item ARC-RES-094, docs-only. Requirements:
[FR-UI-007](../requirements/ui-media-lifecycle.md#fr-ui-007) (adaptive layout, status unchanged),
[FR-UI-002](../requirements/ui-media-lifecycle.md#fr-ui-002) (HUD).

Goal: an HD interface for any screen resolution. [ARC-RES-093](../research/screen-size-and-ui-layout.md) settled the
layout (UI in 800 × 600 "UI units" at UI scale `s`, blocks anchored to screen edges, strips centred with tiled sides).
This note asks how interface art gets more detail than the 1× original without moving a pixel of its geometry. It
follows [HD alternatives](hd-alternatives.md) (ARC-RES-053), [generative re-detailing](hd-generative-redetail.md)
(ARC-RES-038) and [upscale research](hd-upscale-research.md) (ARC-RES-033), which did not cover interface art.

No original or derived image, crop, sheet, weight or harness file is in this repository or its PR. Everything stayed
under the ignored `local/` of the owner's Mac (the sheets are in `local/res094/sheets/` of the main checkout). The
owner's look decides; the numbers prove artefacts, not "better". Evidence vocabulary as in ARC-RES-033: **local
observation** (a bounded run on the owner's unchanged installation), **source fact** (a cited licence or text),
**judgement**.

## Summary

- **Census (local observation).** The installation holds 815 interface ART files (`art\interface`), 8,734 frames and
  31.75 Mpx. 33 of the files are bitmap fonts (7,425 frames, listed only). The rest, 782 files, hold 1,309 frames and
  29.7 Mpx. Nearly all of that is two kinds: 87 panels of at least 300 px (18.25 Mpx, 57 % of all pixels) and 151
  mid-size panels (9.14 Mpx). A full 4× RGBA of the 782 non-font files would be about 1.9 GB raw, so the pack's PNG
  size (not the count) is the cost.
- **U1 and U2 keep the geometry.** Both are anchored to the original by the shading lock: rectangle shifts are at most
  0.30 UI px (U1) and 0.14 (U2) over 230 measured ledger rectangles (the gate is 1 UI px). Silhouette IoU is at least 0.98 for strips,
  panels, buttons, gauges and icons.
- **The pack's soft alpha is the weak point, not the pixels.** The smooth alpha of U1/U2 (the ARC-RES-038 rule) fails
  the hole gate (IoU ≥ 0.990) on 5 of the 7 sampled frames that have a hole, both strips among them, and fails the
  silhouette gate (≥ 0.937) on both cursors (0.904). It erases single-pixel holes (all five holes, 5 source pixels, of the
  `SaveShotCover` 748 cover). Interface art needs the recipe's `legacy` alpha mode (exact parent-texel alpha): then the alpha,
  hole and colour-key gates hold by construction. The exact alpha leaves 4-pixel steps on diagonal edges at 4×; ARC-UI-111 adds the
  `legacySmooth` mode (interior, holes and colour keys exact, only the edge anti-aliased), which the importer now writes.
- **Recommendation.** Owner decision (2026-11-07): **U3** (the owner's redraws, registered and locked, with Legacy alpha) is the
  base of the HD interface; U2 was judged too soft ("мыльно") and is not the default. Cursors stay Legacy nearest. The formal
  gates are not yet passed for U3: round 1 failed the strips and the dialog box; round 2 (refined strip tiles, one redrawn
  frame per button) is measured but not gated, and the top bar has 75 % of blocks within 1 UI px against the 80 % the note
  uses as a pass. U2 stays only as the geometric reference and the fallback for arts without a redraw. See the
  [U3 results](#u3-results-the-owners-redraws) and [U3 round 2](#u3-round-2-refined-redraws).

## Inputs and harness

Local observation. The installed original (`/Applications/Arcanum`, resource precedence as the game's), module
`Arcanum`. ART frames decoded by this repository's `ArtDecoder` at palette slot 0 (every interface ART has one
palette), through a throwaway C# exporter in the ignored `local/`; **326 of the 815 files (exporter run) carry bytes after the last
frame and need `AllowTrailingBytes`** (the documented option; the strict form rejects them). Art names are
`interface.mes`'s (846 messages); 25 ART names have no exact message match (name differences, not investigated).
Python 3.14 venv with NumPy, Pillow, SciPy and scikit-image for the methods and metrics. Machine: Apple M4 Pro,
24 GiB, macOS 27.

Sample: 35 arts, 87 frames. Strips `IntTop` 185 and `IntBotom` 184; `charcreatetopbar`, `charcreatebottombar`; panels
`Inventor` 221, `PDoll` 223, `MainMenuBack` 329, `Loot`, `SaveLoadBackground` (hole 468 × 300), `SaveShotCover` 748,
`intrface` (six holes); dialog `DialogueBox` 822 with `Lil_Accept` 823 and `Lil_Cancel` 824; bar buttons
(`Combat_Button` 470, `Skills_Button` 472, `Spells_Button` 473, `Schematics_Button` 471, `Char_But`, `Log_But`,
`Invn_But`, `TMap_But`, `lilgrnbut` 137) and `NextBut` 321 (all three frames each); vials 17–20 (`bluvial` and
`redvial` all 15 frames); `FollowerFrame` 503; `OptionSlider` 688; cursors `cursor` and `battlecur` 353; icons
`coinsicon`, `magicmeleeicon`, `skl_heal` (frames 0 and 1). The metrics table groups by the census rule: `FollowerFrame`
counts as an icon, `skl_heal` as a button and `OptionSlider` as a panel.

## Census (3.0)

Kinds are assigned by name and geometry (a classification rule of this note, judgement): fonts by the `font` suffix;
cursors by the cursor names; strips are the four full-width bars; gauges by vial/bar/gauge names; panels by side
(≥ 300 px, or a single frame 100–299 px with the short side ≥ 30); buttons are 2–4 frames of one size up to 120 px;
animations are non-font arts of more than 4 frames; icons are single frames up to 128 px.

| Kind | Files | Frames | Mpx | Share of pixels | Files with interior transparent holes |
| --- | ---: | ---: | ---: | ---: | ---: |
| panels ≥ 300 px a side | 87 | 93 | 18.25 | 57.5 % | 26 |
| panels 100–299 px | 151 | 151 | 9.14 | 28.8 % | 3 |
| icons (≤ 128 px) | 232 | 232 | 0.75 | 2.4 % | 11 |
| buttons (2–4 frames) | 237 | 570 | 0.64 | 2.0 % | 4 |
| animations and overlays (> 4 frames) | 10 | 159 | 0.40 | 1.3 % | 8 |
| strips (top and bottom bars) | 4 | 4 | 0.32 | 1.0 % | 2 |
| gauges (vials, bars, pips) | 35 | 63 | 0.15 | 0.5 % | 0 |
| cursors | 21 | 27 | 0.02 | 0.1 % | 14 |
| other (button/marker oddities) | 5 | 10 | 0.03 | 0.1 % | 0 |
| bitmap fonts (listed only) | 33 | 7,425 | 2.04 | 6.4 % | all (glyph gaps) |
| **Total** | **815** | **8,734** | **31.75** | | |

Marks:
- **No semi-transparent pixel exists.** Every decoded interface frame has alpha 0 or 255 only (0 frames). The colour
  key of the original is the transparent palette entry; "colour-key pixels unchanged" means the transparent set stays
  exactly the same.
- **Holes (transparent regions not connected to the frame border)** are in 26 of the 87 big panels, both bar strips of
  the HUD (`IntTop` 12 holes, `IntBotom` 4: the button sockets), 3 mid panels, 4 button files and 14 cursors.
- Multi-frame buttons are 2 frames (147 files), 3 frames (84) and 4 frames (6); frame sizes within one button do not
  vary. The animations are the compass, hover overlays, load indicator and waypoint arts; the vials 18 and 19 have 15
  frames but only frame 0 is drawn by the original (PA1).
- Every art has one palette slot; the buttons ask for slot 0 (GB2).

## Methods (3.2)

Same inputs for every method: the decoded RGBA at slot 0.

| Id | What | Alpha | State |
| --- | --- | --- | --- |
| U0 | Legacy, integer nearest ×4 (the reference) | Legacy | run |
| U1 | colour bleed (12 px, then the mean colour) → `realesrgan-x4plus` at native 4× → shading lock (4 luma back-projection iterations) | smooth alpha (ARC-RES-038: nearest ×4, Gaussian σ 3.2, ramp) | run |
| U2 | U1 with edge decontamination (the bleed starts one pixel inside the silhouette: the ring is replaced from the interior; the frame border counts as interior) and a coarse colour lock (the chroma error blurred over one source pixel, two iterations) | smooth alpha | run |
| U3 | redraw + fit (external redraw, registration, luma and coarse colour lock, Legacy alpha and holes, repeated-part step) | Legacy | **run** on the owner's 12 redraws (OpenAI's built-in image generator, in the owner's own account; no local generator, ARC-RES-053); the repeated-part step and a local warp not run; the lock is U2's (U3) or coarse (U3c); [results below](#u3-results-the-owners-redraws) |
| U4 | tileable side pieces of the 9-slice, cut from U1/U2 | n/a | run on the strips (below) |

The Legacy-alpha variant of U1/U2 is the same RGB with the Legacy alpha (the pack's `legacy` mode); it is not a fifth
method. Its alpha, hole and key results are exact by construction. Its colour metrics were re-measured on the whole
sample: ΔE, luma MAE and sharpness equal the table's within 0.03 on every kind except the cursors (ΔE 8.40 / 9.23
against 8.71 / 9.72, luma MAE 1.33 / 2.09 against 2.08 / 2.95), so the table's colour columns stand for it. The sheets and the HUD
mock-ups were rendered with the smooth alpha.

## Metrics (3.3)

Per frame against the Legacy frame. IoU is on the alpha ≥ 128 mask against the nearest ×4 Legacy mask; the hole IoU is
the worst hole (the best-overlapping transparent component of the HD result against the nearest ×4 hole); "key px" is
source pixels whose 4 × 4 majority vote differs from the Legacy alpha; ΔE is CIE76 on the alpha-weighted 4 × 4 box
downsample against the source over opaque pixels; luma MAE is on the same downsample; "detail" is the mean luma
deviation of an HD pixel from its own block mean (0 for nearest); "sharpness" is the 99th percentile of the luma
gradient against U0's. All numbers are for the smooth alpha unless marked.

| Kind (U1 / U2) | Silhouette IoU min | Hole IoU min | Key px (frames) | ΔE mean | Luma MAE | Detail | Sharpness |
| --- | --- | --- | --- | --- | --- | --- | --- |
| panels (9 files) | 0.994 | **0.000** | 60 | 2.58 / 1.83 | 0.22 / 0.20 | 4.3 | 1.18 / 1.17 |
| strips (4) | 0.998 | **0.975** | 42 | 2.93 / 2.19 | 0.23 / 0.22 | 3.7 / 3.8 | 1.03 / 1.00 |
| buttons (13 files, 37 frames) | 0.980 | – | 64 | 5.59 / 3.94 | 0.90 | 7.7 / 7.4 | 1.10 / 1.03 |
| gauges (4 files, 32 frames) | 0.997 | – | 68 | 3.20 / 1.06 | 0.53 / 0.21 | 2.5 | 0.77 |
| icons (3) | 0.998 | 0.998 | 4 | 3.35 / 2.32 | 0.35 / 0.36 | 8.7 / 8.3 | 1.15 / 1.25 |
| cursors (2) | **0.904** | – | 8 | 8.71 / 9.72 | 2.08 / 2.95 | 15.8 | 0.91 / 0.83 |

Gates (the brief's, plus judgement where marked):
- **Silhouette ≥ 937 ‰.** Fails only for both cursors (0.905 and 0.904). A 24 × 27 px frame has too few pixels for
  the smooth alpha.
- **Hole IoU ≥ 990 ‰.** Fails with the smooth alpha on 5 of the 7 sampled frames with holes: `IntTop` 0.975, `intrface` 0.905
  (U1 and U2 alike), `SaveShotCover` 0.000 (all five small holes disappear), `IntBotom` and `SaveLoadBackground`
  between 0.984 and 0.987. `Loot` (1.000) and `FollowerFrame` (0.998) pass. With Legacy alpha every hole is exact.
- **Colour key unchanged.** The smooth alpha changes 4 to 68 source pixels per kind (42 to 68 on strips, panels, buttons and gauges; single-pixel and edge-ring
  pixels, the 15-frame vials 34 per file); Legacy alpha changes none.
- **Hotspot rectangles.** 230 rectangles per method (every frame box, the nine wear slots, 120 grid cells, the use and
  drop boxes of `Inventor`/`PDoll` (IW14, IW19, IW23), the strips' buttons, vial gauges (GB2, CH7), the dialog box's
  text and buttons (DB2, DB3), the save preview (MW16)) were registered against Legacy by phase correlation of the
  grey-composited luma (a test shift of (6, −3) HD px read back 1.63 UI px against 1.68). Largest shift 0.30 UI px
  (U1, an `Inventor` grid cell) and 0.13 (U2, the `cursor` frame), mean 0.063 (U1) and 0.023 (U2); six rectangles are entirely transparent (the strips'
  sockets, where the button art stands), not measured. This is expected: the shading lock pins U1/U2 to the original.
  It is **not** a prediction for U3, whose redraw moves edges: see [U3 results](#u3-results-the-owners-redraws).
- **Repeated parts.** Identical 8 × 8 source blocks (≥ 6 colours): 8 to 2,700 pairs per art; the copies' pixel MAE
  at 4× is 0.7 to 2.6 per art (mean 1.1 on strips, 2.6 on `SaveShotCover`), against 0 for U0. U1/U2 do not keep identical parts identical;
  the repeated-part step of ARC-RES-053 (a build step that copies one master part) is needed for grids, rivets and
  bands. Not run here.
- **Low-frequency colour.** ΔE is 1–2 on panels and strips, 1–3 on gauges after the colour lock (gauges 3.2 for U1
  against 1.1 for U2, so the colour lock matters on saturated flat vials), 4–6 on buttons and 9–10 on cursors (judgement:
  the soft edge ring of small shapes carries most of it). Luma MAE at 1× is below 1 except buttons' edge rings and
  cursors.
- **Text-like engraving** (U2 crispness): mean sharpness is 1.00–1.18 on panels and strips and 1.15–1.25 on icons, i.e. edges
  about as sharp as nearest on average, with single arts softer (`IntTop` U2 0.73 against 0.86 for U1); gauges (0.77) and cursors (0.83–0.91) are softer. Bitmap fonts are out of scope.
- **Detail.** None of U1/U2 adds invented detail; the intra-block deviation is 2.5 (gauges) to 8.7 (icons) luma
  levels (15.8 on the cursors), mostly the network's smoothing of the original's dither (judgement). Judgement: this is the "soft 4× base", not the
  owner's ChatGPT look, which is a redraw.

### 9-slice side pieces (U4)

ARC-RES-093 recommends the 800-unit strip centred and the remaining width filled by tiling "the strip's edge
columns". Local observation on U1/U2 (the seam ratio R is the mean adjacent-column difference in a ±8 HD px band at
the tile join over the tile's interior; a seam as smooth as the interior is 1.0; judgement: R ≤ 1.5 passes):

| Strip | Free columns (opaque, UI px) | Tile | R (U2) | Note |
| --- | --- | --- | --- | --- |
| `IntTop` | left edge 0–3 only (the Character socket starts at 4); 149–157, 179–280, 313–330, … | plain wood 32 px from x = 186 | 0.58 (16 px: 0.56; 24 px: 0.67; 48 px: 0.67) | cut from the wood run between button groups; U0 gives 0.59–0.84 |
| `IntBotom` | 0–85, 125–648, 732–799 | the left and right 8 columns | 1.19 / 1.44 | wood rail with the brass strip; widths 12 and 16 reach 2.0–2.3 (the vial housing starts near x = 10) |
| `charcreatetopbar`, `charcreatebottombar` | the whole width | edge 16 columns | 1.06–1.17 (bottom bar), 1.13–1.22 (top bar) | no holes |

Findings:
- Fully opaque columns are necessary but not sufficient: the bottom strip's free runs contain the vial housings and the
  message window, so the side piece must be a narrow wood rail (8 columns) or a redrawn piece (U3/U4 redraw).
- U1/U2 seams are mostly smoother than U0's nearest blocks (bottom strip, 8 columns: U0 1.77 / 1.86, U1 1.18 / 1.44).
- Whether there is "no visible repeat at 21:9" cannot be proven by a number (a tile is exactly periodic): the 21:9
  mock-up at 200 % (2800 × 1200, 300 UI px of tiled fill per side) is the evidence for the owner's look.

## U3 results (the owner's redraws)

Source (local observation). The owner redrew 12 sheets of source art (`local/res094/u3_redraw/source/`, prompts in
`PROMPTS.md` plus per-file prompts) in the owner's own account with OpenAI's built-in image generator (model name not exposed;
the owner's `results/SOURCE.txt` records the prompt and attempt of each file). Several files took more attempts
(`Inventor`: several versions and edits; the doll, `NextBut`, `SaveLoadBackground`, the vials and the icons: two each).
The final `Inventor` (`v3-local`) is not a single generation: it is a deterministic masked ImageMagick composition of
three generations (`v3` as the base, `v4` as the coin donor, `v3-corrected` as the arrow donor). The owner's own QA notes
list: icons of the inventory reinterpreted (`Inventor`, icons sheet), a gradient instead of a flat fill in
`SaveLoadBackground`, white margins and a 3:1 canvas on both strips. Redraw sources are judged here only by the numbers
below; the content (which icon is which) is the owner's look.

Fit (local, a harness of this task): white margins cropped (both strips only), a per-axis scale and offset per sheet by
the best normalised cross-correlation of blurred luma, a per-frame refinement of scale and offset (±2 %), then a
per-frame translation refinement to remove the residual whole-frame shift (the fit is global per axis, with no local
warp); bicubic sampling into the frame's 4× grid; Legacy
alpha and holes. Variants: **U3raw** (no lock), **U3** (the luma lock of U1 and the coarse colour lock of U2),
**U3c** (both locks coarse, over 3 source px, so the redraw keeps its own tone). The repeated-part step is not run. Fit quality (sheet NCC before the translation refinement): 0.45–0.52 on the three
button sheets, 0.60 on `IntTop`, 0.82–0.96 elsewhere; low values come from the redrawn wood and glow, so the geometry of
the buttons rests on a low-correlation fit and is read from the block shifts below, not from the NCC. Blocks whose
Legacy luma deviation is under 4 are skipped in all block shares.

Findings:
- **Resolution of the redraws.** Redraw pixels per UI px: panels 1.8–3.2 (`MainMenuBack` 1.77, `SaveLoadBackground` 2.15,
  strips 2.66, `Inventor` 2.88, `PDoll` 3.17), the dialog box 5.5, vials 8.6, icons 8.4, buttons 9.5–13.5. The big
  panels (57 % of the pixels) were redrawn at less than the pack's 4×, so a 4× entry from them is an upsample.
- **Geometry after the rigid fit, before any lock (U3raw).** Block shifts are phase-correlation shifts of 32 × 32 UI px
  blocks (16 × 16 for frames under 64 px) against Legacy; the share is of blocks within 1 UI px. Rectangle checks are
  the ledger rectangles of the gates above.

| Art | Median shift (UI px) | Blocks ≤ 1 UI px | Rectangles ≤ 1 UI px |
| --- | --- | --- | --- |
| `IntTop` (01) | 0.61 | 57 % | 3 of 3; the left button group is misplaced (90th percentile 3.7, worst 8.2) |
| `IntBotom` (02) | 0.32 | 86 % | 6 of 8 (worst 4.4) |
| `Inventor` (03, the owner's final `v3-local`) | 0.46 | 94 % | 123 of 123 (worst 0.83) |
| `PDoll` (04) | 0.66 | 87 % | 10 of 10 (worst 0.90) |
| `MainMenuBack` (05) | 0.53 | 75 % | – |
| `DialogueBox` (06) | 0.79 | 81 % | 3 of 4 (worst 3.6) |
| `SaveLoadBackground` (07) | 0.62 | 71 % | – (90th percentile 4.1) |
| `NextBut`, `Combat_Button`, `Skills_Button` (08–10) | 0.08–0.42 | 89–100 % | all within 1 |
| vials (11) | 0.07–0.84 | 91–100 % | all within 1 |
| icons (12) | 0.12–0.28 | 75–100 % | all within 1 |

  By kind, blocks within 1 UI px: buttons 97 %, vials 98 %, icons 88 %, panels 80 % (139 rectangles, 99 % within 1),
  strips 62 % (11 rectangles, 82 % within 1); kinds follow the census rule (`skl_heal` counts as a button). Rectangle
  counts include the whole-frame box; the four left sockets of `IntTop` are transparent and not measured.
- **Wood grain is not a misfit.** Most blocks beyond 1 UI px lie on plain wood, where the redrawn grain legitimately
  differs from the original's. Counting only structured blocks (Legacy luma deviation of at least 30 in the block), the
  share within 1 UI px is: `IntTop` 74 %, `IntBotom` 96 %, `Inventor` 100 %, `PDoll` 79 %, `MainMenuBack` 98 %,
  `DialogueBox` 83 %, `SaveLoadBackground` 93 %, `NextBut`, `Combat_Button`, `Skills_Button` and the vials 100 %
  (96–100 % per file), icons 88 %. The earlier `PDoll` version (before the owner's wood edit) scores 97 % there: the wood
  edit moved details, which is a case for toning wood by colour grading instead of regeneration. The strip and
  dialog numbers are real structural misfits (the left socket group of `IntTop` is redrawn at another place).
- **The locks hide the misfit.** After the U3 lock every rectangle is within 0.3 UI px, because the lock pins the luma
  at one source pixel to the original; the raw numbers above are the geometry result.
- **Colour.** The raw redraw is far from the original in tone, which is probably the owner's wood harmonisation (judgement): ΔE 5.0 panels,
  7.7 buttons, 7.8 vials, 11.2 strips, 11.6 icons. After the U3 lock ΔE is 1.0–3.3 (panels 1.7, strips 2.7, buttons
  3.3, vials 1.0, icons 2.5), comparable to U2 on the same frames (strips 2.7 against 2.2, panels 1.7 against 1.6, icons
  2.5 against 1.9, buttons 3.3 against 4.7); U3c keeps ΔE 3.3–6.7 and luma MAE 4–11.
- **Detail and sharpness (U3).** Detail 3.7 panels, 6.4 strips, 8.5 buttons, 2.7 vials, 9.7 icons (U2 on the same frames: 3.2, 4.5, 6.3,
  3.0, 8.1); sharpness 0.63 on vials (U3c 0.33: the redrawn vials are soft), 0.88 strips, 1.1–1.2 elsewhere.
- **Repeated parts.** Copies of identical source blocks differ by 4.8 (panels: mean of four arts) and 6.9 (strips: `IntBotom`
  only) at 4×, against 0.7–1.5 for U2 on the same arts and 0 for U0: the repeated-part step is needed before any panel is packed.
- **Frame consistency of buttons (the owner's observation on `NextBut`).** The three frames of a sheet were redrawn
  independently and differ in execution (glass dome, arrow glow, wood grain direction). The mean absolute RGB
  difference over all frame pixels (0–255) of frames 0–2 is 8.8 against 2.9 in the original (`Combat_Button` 17.1 against 9.4, `Skills_Button` 14.1
  against 8.5). A fix tested without a new redraw: use only the redrawn frame 0 and derive frames 1 and 2 by adding the
  Legacy frame difference (bicubic): the differences are within 0.9 of the original for frame pairs 0–1 and 0–2 and lower for 1–2 (`NextBut` 7.4 / 3.0 / 7.2
  against 8.3 / 2.9 / 10.7; the coarse-lock variant `U3dc`, build script `lib/make_u3d.py`; the full-lock variant is worse,
  `Combat_Button` 0–1 17.1 against 14.5). This is a construction check, not evidence of visual consistency (frames 1 and
  2 are built from the Legacy difference); the look is the owner's. Judgement: redraw one frame per button, not a sheet.
- **Not measurable by the gates.** Content (which icon is which, invented ornaments in transparent areas, the gradient
  fill) and the silhouettes of the redraw itself (the redraws are opaque; alpha and holes are the Legacy's).

Judgement. The geometry gate is met by the buttons and vials (and by the inventory), not by
the strips and the doll's final version, and the dialog box (structured blocks 74–83 %); in `SaveLoadBackground` and the
main menu most of the 25–29 % of blocks beyond 1 UI px are plain wood. The icons fail on content by the owner's QA notes and the
vials on softness. So U3 is a candidate for buttons (one redrawn frame plus the original's frame differences) and for
large background panels as a texture source, behind U2's geometry (a local warp or a masked blend of the redraw's
texture into U2's geometry is the next step and was not tried). By the numbers U2 stays the safe default; by the
owner's look (2026-11-07) U2 is too soft ("мыльно") and the redraw (U3) is the direction. The refinement plan is: per art
the version with the best structured-block fit as the base (the doll: the version before the wood edit), wood tone by
colour grading, buttons redrawn as one frame with the other frames derived from the original's differences, strips
redrawn in tiles, icons redrawn once as separate motifs and composited, and refinement prompts that take the aligned
redraw as the edit target.

## U3 round 2 (refined redraws)

Source (local observation). After the first round the owner returned refined redraws made with `CHATGPT_BRIEF.md`
(the style lock, one request per file): the top bar in five overlapping tiles (176 UI wide, 20 overlap), the bottom
panel in four (224 wide, 32 overlap), one frame per button, the dialog box corners, the vials, the small ammunition
icons (sheet 13) and the large motifs (sheet 14). The first run is **set A** (top bar 5, bottom panel 4, three buttons, dialog corners, vials, sheets 13 and 14); a second run of
the same brief with a relaxed size rule is **set B** (the same files again, 16 in all). Unless a line says set B, the files used below are set A. Both sets stay local; no result
is in git. The generator returns its own canvas sizes (top bar 2172 × 724 with white margins, bottom 1489 × 1056,
buttons 1254 × 1254), so each tile is registered to its source before use.

Fit (local, `lib/fit_refine.py`): the content box is found (white margins cut for the top bar), a per-axis scale and
offset maximise the normalised correlation of blurred luma, and 6 × 2 blocks give the residual shift in UI px (the
fit is global per tile, no local warp). Per-axis scale ratios are within 2 % of square.

| Art | Result px per UI px | Tile NCC | Blocks: median / worst shift (UI px) |
| --- | --- | --- | --- |
| top bar, 5 tiles (set A) | 12.2–12.5 | 0.79, 0.81, 0.92, 0.82, 0.82 | 0.50–0.66 / 1.3, 2.9, 1.4, 1.4, 1.1 |
| bottom panel, 4 tiles (set A) | 6.65 | 0.95, 0.96, 0.96, 0.93 | 0.11–0.56 / 0.74, 0.37, 0.93, 2.9 |

Medians and worst values are magnitudes of the per-block shift. Blocks within 1 UI px: top bar 75 % of 60, bottom panel
97 % of 36 (two bottom tiles have only 6 valid blocks each; blocks of plain wood are included, so the share is not
comparable with the structured-block shares above). The top bar's 75 % is below the 80 % the note uses elsewhere for a pass.

- **Stitching.** Adjacent tiles redrawn separately do not agree on texture in the overlaps (top bar: luma correlation
  0.17–0.61, luma MAE 12.6–22.9 on a mean luma of 27–42), so a linear blend hides grain but not edges. One seam
  (tiles 3 and 4 of the top bar) showed a double socket frame, a shift of about 1.1 UI px; moving that seam into the
  plain wood between two sockets (a 30-working-pixel blend) removed it. By eye (developer) the other seams were clean. The
  stitched strips are 4800 and 4000 working px wide (6 and 5 per UI px; 800 UI).
- **Against Legacy at 1 UI px (box downsample, not the gates above).** Top bar: luma MAE 12.3 (8 × 8 low-frequency 6.1),
  mean luma 26.7 against 27.2. Bottom panel: luma MAE 10.0 (low-frequency 8.1), mean luma 24.3 against 31.3, so the redraw is
  darker than the original wood (judgement: the style lock's measured palette is the likely cause; the hue shift was only
  seen by eye, not measured); colour grading is not applied. A hole check at luma below 22 gives IoU 0.83 (top bar) and
  0.88 (bottom panel), far under the 990 ‰ gate (judgement, not tested: the original's dark wood and navy windows sit near
  that threshold and the redraw's black windows are darker); alpha and holes of an interface strip would still come from Legacy. The rectangle ledger gates were not run on the stitched strips.
- **Buttons (set A).** The refined single frames fit the original frame 0 with low NCC (`NextBut` 0.39, `Combat_Button` 0.47,
  `Skills_Button` 0.36, dithered symbols); by eye (developer) the arrow, shield and key sit on the original's. Frames 1 and 2 were derived as in U3dc (redrawn frame 0 plus the
  Legacy frame difference, coarse lock); the three frames of each button now share one execution. Numbers for the new
  frames were not computed.
- **Vials (set B).** By eye (developer) set B is crisper (narrow bright highlights) than set A and the earlier version, with the
  same shapes. Fit NCC 0.68–0.82 per frame; no block shifts were computed.
- **Motifs.** Sheet 13 (set A) fits the original within 0.8 UI px (block shift median 0.14–0.53, worst 0.78).
  Sheet 14 (set A) fits with block shifts up to 0.65 UI px (`coinsicon`) but 1.9 for `arrows_icon`, and frame NCC 0.92 for
  the coin and 0.42 for the quiver, a weak fit. Set B of sheet 14 sits on flat grey, which is easier to cut out, and was
  not fitted (the fit script looks for white margins). In both sets the gold coin has a woman's profile, not the original's.
- **Dialog box.** By eye (developer) the set A aligned base has the original's corner outline; set B keeps a small round mark in
  one upper corner and a grainier brass. No dialog numbers were computed.

Judgement. By eye (developer's look, owner's verdict pending on the sheets) the refined tiles remove the softness the
owner named in U2 and keep the sockets, bands and rivets in place on the seams that were checked (one seam had a double frame,
fixed); the residual shifts are under 1 UI px for 75 % (top bar) and 97 % (bottom panel) of blocks, with a few up to 2.9.
A build would need a local warp or a per-tile check of the worst tiles (top bar tile 2, bottom panel tile 4) first. The formal gates for the stitched strips, the repeated-part step and a colour grade to the palette were
not run.

## Sheets (3.4, local only)

Under `local/res094/sheets/` of the main checkout; each shows U0, U1, U2, U3 and U3c ("not redrawn" where the owner made no redraw). The texture is
sampled nearest from the 4× image at `s` device px per UI px, as the HD contract draws it:
- `sheet_200pct.png`, `sheet_300pct.png`: 15 rows, among them a lamp and an ornament of the main menu, the inventory
  corner with its grid, wood and bars, the doll plate and slot, the bottom strip's vial, the top strip's buttons, the
  dialog box, the three frames of `NextBut` and `Combat_Button`, the accept and cancel buttons, three vial frames,
  cursors and icons.
- `hud_21x9_200pct_U0|U1|U2.png`: a full HUD (both strips with tiled sides, all bar buttons at their GB2 offsets, the
  two vials at CH7 positions) on a 2800 × 1200 (21:9) screen at `s` = 2; `hud_4x3_300pct_*.png`: the same on 800 × 600
  at `s` = 3; `hud_compare_*_join.png`: the strip joins of the three methods.
- Judgement (developer's look, not the gate): U1 and U2 are visibly smoother than U0 at 300 % and keep every
  silhouette; they add no detail; U2's edge rings are cleaner; on the HUD the three methods are consistent with each
  other. The tiled top fill shows the strip's chain ornament repeating (a wood piece without the ornament would avoid
  it).

## Recommendation per kind

The owner chose U3 as the base on 2026-11-07 (U2 judged too soft). The rows name U3 where a redraw has been fitted and U2 where
it is still the only measured method; no row has passed the formal gates for U3 yet.

| Kind | Method | Alpha | Why | What is open |
| --- | --- | --- | --- | --- |
| panels, mid panels, dialog boxes | **U3** (owner's choice; round 1: 80 % of blocks within 1 UI px, local warp needed; the dialog box failed a rectangle); U2 is the fallback | `legacy` | (U2 numbers) geometry exact; ΔE ~1.8; holes exact only with Legacy alpha | repeated-part step for grids and bands; local warp of U3 |
| strips | **U3 round 2** stitched refined tiles (owner's choice; formal gates not passed: 75 % top bar / 97 % bottom panel of blocks within 1 UI px, worst 2.9; round 1 U3 failed: 62 %); U2 with U4 side pieces is the fallback | `legacy` | (U2 numbers) holes (button sockets) exact; seams pass with a wood run (top) or an 8-column rail (bottom) | gates, colour grade and 9-slice side pieces for the U3 strips do not exist; the owner's look |
| buttons (2–4 frames) | **U3 round 2** (owner's choice: one redrawn frame plus the original's frame differences; no gate numbers for the new frames yet); U2 is the fallback | `legacy` | (U2 numbers) silhouette IoU 0.98; ΔE ~4 and 64 key px change with smooth alpha | the frame difference numbers and gates for the round 2 frames; a redraw per frame breaks the set (above) |
| gauges (vials, bars) | **U3** (owner's choice; vials: set B is the crisper redraw; round 1: 98 % of blocks within 1 UI px); U2 is the fallback | `legacy` | (U2 numbers) colour lock matters (ΔE 3.2 → 1.1) | the 15 vial frames: only frame 0 is drawn (PA1); flicker across frames not measured |
| icons | **U3** (owner's choice; small ammunition icons from sheet 13, fit within 0.8 UI px); the round 1 icons failed on content; U2 is the fallback | `legacy` | (U2 numbers) IoU 0.998 | U3 icon gates; the full icon set beyond sheet 13 is not redrawn |
| cursors | **U0**, not in the pack, or a hand-drawn redraw | `legacy` | 24 × 27 px; smooth alpha IoU 0.904; ΔE 9–10 | hot points are integer UI px (CU4) |
| fonts | out of scope | – | – | – |

U1 alone is not recommended anywhere: U2 improves ΔE on every kind (cursors excepted, where neither is recommended)
but trades a little luma MAE and sharpness on a few arts (`IntTop`: luma MAE 0.49 against 0.32, sharpness 0.73 against
0.86), and both pass or fail the same gates.

## What the pack recipe needs (proposal, no format change)

[HD pack format](../formats/hd-pack.md) already accepts any ART and has the `legacy` alpha mode, so U1 without its
locks is expressible today as `network` (`realesrgan-x4plus`, 4×, `rgb.edgeExtend`) plus `alpha.mode: "legacy"`; U2
(shading lock, colour lock, edge decontamination) is not, and the 0.30 UI px rectangle result belongs to the locked
variants. The gaps, as proposals:
- **A slice kind for interface art.** `slices[].kinds` has no `interface` value (the list is `player`, `critter`, `npc`,
  `scenery`, `container`, `item`, `wall`, `facade`, `tile`, `terrainChunk`).
- **Alpha mode per slice.** The alpha mode is one recipe field. A pack holding sprites with `soft` and interface art
  with `legacy` needs it per slice, or the interface art in a pack of its own (only one pack is active).
- **A hole check in the `soft` rule.** It checks the one-texel growth bound and the silhouette IoU, and neither sees a filled hole, so a `soft` recipe
  at 900 ‰ would accept the cursors (0.904) and `SaveShotCover` with all its holes gone.
- **Post steps beyond the network.** The shading and colour locks, the edge decontamination and the 9-slice side pieces
  are algorithm steps not covered by `rgb` (bleed only). A recipe kind `redraw+fit` (an `external` method whose
  results are registered and locked at build time, with the redraw source recorded) is needed if U3 is adopted (the fit, the frame-difference step and the locks are the build);
  otherwise `network` plus new `parameters` labels (`lock`, `decontaminate`) would carry U2.
- **Side pieces** are extra pack entries (a tile per strip and side), not new ART frames.

## Licences (source facts, checked 2026-11-07)

| Component | Licence | Use |
| --- | --- | --- |
| Real-ESRGAN `realesrgan-ncnn-vulkan` 20220424 (zip `e0ad0558…cc333a`, binary `c1c35d92…91b7cc`, the same hashes as ARC-RES-033; downloaded from the project's v0.2.5.0 release and verified) with `realesrgan-x4plus` | MIT (tool), BSD-3-Clause (model) per ARC-RES-033; ncnn BSD-3-Clause | soft 4× base, external program |
| NumPy 2.5.4 | BSD-3-Clause | local tooling |
| SciPy 1.18.1, scikit-image 0.26.0 | BSD-3-Clause | local tooling |
| Pillow 13.0.0 | MIT-CMU (HPND) | local tooling |
| `Arcanum.Data` decoder, a throwaway exporter | this repository (MIT) | frame export |

No model was trained; no weight is added to the repository. Generated frames are derivative works of the original art
(ARC-RES-033 legal note) and stay local.

## Not run and not verified

- **U3 limits.** The redraws are the owner's attempts with one generator; the fit is global per axis (no local warp), the repeated-part
  step is not run, the HUD mock-ups have no U3 and the U3 sheet columns show only what was redrawn. Round 2 stitched strips have no
  gate run (rectangle ledger, seam ratio, key pixels), no colour grade and no 9-slice side pieces; the round 2 buttons and vials have no frame numbers; the second-set sheet 14 was not fitted; the owner accepted the
  darker wood tone and the coin with a woman's profile as they are (2026-11-07). A redrawn tileable wood piece for the 9-slice and the
  redrawn cursors were not made.
- The filtered HD minification and the staircase of the Legacy edge at 200–300 % were not
  tested; the Legacy alpha keeps the original's one-texel stairs (4 HD px) at the silhouette.
- Frame-to-frame flicker of the 15-frame vials was not measured (the original draws frame 0 only).
- The 25 arts without an exact `interface.mes` name were not mapped; the wider census rests on file names and
  geometry, not on the window code (judgement).
- The sample is 35 arts of 782; the per-kind rows are means over that sample, not the whole census. Mid panels
  (151 files, 29 % of the pixels) were sampled by `OptionSlider` only (the schematic, map and book arts were not).
- The ΔE and sharpness values depend on the Real-ESRGAN output of one GPU (M4 Pro); cross-GPU bit-exactness is not
  established (ARC-RES-033).
- Seams are measured on the Legacy frames' columns; no 21:9 repeat metric exists beyond the mock-up.
- Fonts (the bitmap arts 27, 229, 171, 327), a scaling and anchoring layer, pack format changes, renderer changes,
  the build tool ARC-DEV-003 and the second Mac are outside this task.

Provenance: frames decoded from the owner's installation with this repository's own decoder; tools used as external
programs and libraries; no source reconstruction used.

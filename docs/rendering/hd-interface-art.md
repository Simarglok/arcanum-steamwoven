# HD interface art: methods for panels, frames, buttons and small art (ARC-RES-094)

Status: **research only, 2026-11-07; U3 (redraw + fit) not run.** Work item ARC-RES-094, docs-only. Requirements:
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
  0.10 UI px over 230 measured ledger rectangles (the gate is 1 UI px). Silhouette IoU is at least 0.98 for strips,
  panels, buttons, gauges and icons.
- **The pack's soft alpha is the weak point, not the pixels.** The smooth alpha of U1/U2 (the ARC-RES-038 rule) fails
  the hole gate (IoU ≥ 0.990) on 5 of the 7 sampled frames that have a hole, both strips among them, and fails the
  silhouette gate (≥ 0.937) on both cursors (0.904). It erases single-pixel holes (all five holes, 5 source pixels, of the
  `SaveShotCover` 748 cover). Interface art needs the recipe's `legacy` alpha mode (exact parent-texel alpha): then the alpha,
  hole and colour-key gates hold by construction.
- **Recommendation.** U2 with Legacy alpha for panels, strips, buttons, gauges and icons; cursors stay Legacy nearest;
  U3 is the candidate for big panels and buttons and is the owner's call (not run). Details per kind below.

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
| U3 | redraw + fit (external redraw, registration, luma and coarse colour lock, Legacy alpha and holes, repeated-part step) | Legacy | **not run**: the redraw is made in the owner's own account; no local generator is used (ARC-RES-053). Which source made each redraw is to be recorded when it runs |
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
  grey-composited luma (a test shift of (6, −3) HD px read back 1.63 UI px against 1.68). Largest shift 0.095 UI px
  (the `PDoll` medallion), mean 0.025 (U1) and 0.012 (U2); six rectangles are entirely transparent (the strips'
  sockets, where the button art stands), not measured. This is expected: the shading lock pins U1/U2 to the original.
  It is **not** a prediction for U3, whose redraw moves edges (silhouette IoU about 0.91 on the chest in ARC-RES-053)
  and must pass this gate.
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

## Sheets (3.4, local only)

Under `local/res094/sheets/` of the main checkout; each shows U0/U1/U2 and an empty U3 column (the nearest sampling of
the 4× texture at `s` device px per UI px, as the HD contract draws it):
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

| Kind | Method | Alpha | Why | What is open |
| --- | --- | --- | --- | --- |
| panels, mid panels, dialog boxes | **U2** (U3 candidate) | `legacy` | geometry exact; ΔE ~1.8; holes exact only with Legacy alpha | repeated-part step for grids and bands; U3 not run |
| strips | **U2** with U4 side pieces (U3 candidate) | `legacy` | holes (button sockets) exact; seams pass with a wood run (top) or an 8-column rail (bottom) | a redrawn wood piece without the chain ornament; the owner's look |
| buttons (2–4 frames) | **U2** (U3 candidate, one sheet per button) | `legacy` | silhouette IoU 0.98; ΔE ~4 and 64 key px change with smooth alpha | the three frames of one button must stay a set |
| gauges (vials, bars) | **U2** | `legacy` | colour lock matters (ΔE 3.2 → 1.1) | the 15 vial frames: only frame 0 is drawn (PA1); flicker across frames not measured |
| icons | **U2** | `legacy` | IoU 0.998 | none |
| cursors | **U0**, not in the pack, or a hand-drawn redraw | `legacy` | 24 × 27 px; smooth alpha IoU 0.904; ΔE 9–10 | hot points are integer UI px (CU4) |
| fonts | out of scope | – | – | – |

U1 alone is not recommended anywhere: U2 improves ΔE on every kind (cursors excepted, where neither is recommended)
but trades a little luma MAE and sharpness on a few arts (`IntTop`: luma MAE 0.49 against 0.32, sharpness 0.73 against
0.86), and both pass or fail the same gates.

## What the pack recipe needs (proposal, no format change)

[HD pack format](../formats/hd-pack.md) already accepts any ART and has the `legacy` alpha mode, so U1 without its
locks is expressible today as `network` (`realesrgan-x4plus`, 4×, `rgb.edgeExtend`) plus `alpha.mode: "legacy"`; U2
(shading lock, colour lock, edge decontamination) is not, and the 0.10 UI px rectangle result belongs to the locked
variants. The gaps, as proposals:
- **A slice kind for interface art.** `slices[].kinds` has no `interface` value (the list is `player`, `critter`, `npc`,
  `scenery`, `container`, `item`, `wall`, `facade`, `tile`, `terrainChunk`).
- **Alpha mode per slice.** The alpha mode is one recipe field. A pack holding sprites with `soft` and interface art
  with `legacy` needs it per slice, or the interface art in a pack of its own (only one pack is active).
- **A hole check in the `soft` rule.** It checks the one-texel growth bound and the silhouette IoU, and neither sees a filled hole, so a `soft` recipe
  at 900 ‰ would accept the cursors (0.904) and `SaveShotCover` with all its holes gone.
- **Post steps beyond the network.** The shading and colour locks, the edge decontamination and the 9-slice side pieces
  are algorithm steps not covered by `rgb` (bleed only). A recipe kind `redraw+fit` (an `external` method whose
  results are registered and locked at build time, with the redraw source recorded) is only needed if U3 is adopted;
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

- **U3 (redraw + fit) not run.** The redraw belongs to the owner's own account; none was supplied. Its checks (the
  silhouette, hole and rectangle gates on a redraw) are open and are the first questions for it. Same for the redrawn
  3-frame button sheets and for a redrawn tileable wood piece.
- The repeated-part step, the filtered HD minification and the staircase of the Legacy edge at 200–300 % were not
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

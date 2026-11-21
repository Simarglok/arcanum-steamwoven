# HD world map and town maps (ARC-RES-2992)

Status: **research, 2026-11-20** (work item ARC-RES-2992, [#2992](https://github.com/Simarglok/arcanum-steamwoven/issues/2992)). Docs only; every
probe, capture and composite stayed in the ignored `local/` of the owner's Mac, and no pixel, hash or image of the game is in this note: only
kinds, counts, sizes and measured numbers. Requirements touched (status unchanged):
[FR-UI-004](../requirements/ui-media-lifecycle.md#fr-ui-004), [FR-UI-007](../requirements/ui-media-lifecycle.md#fr-ui-007),
[FR-PRODUCT-001](../requirements/content-product.md#fr-product-001).

Context: the [gap census](hd-pack-gap-census.md#world-map-and-town-map-screen-travel-arc-res-2983) found that the world-map window draws bitmaps that
the HD pack has no entry kind for (the 64 map tiles of 250 × 250, `Map_Zoomed` 365 × 365, and 57,522 + 6,677 town-map tiles of 64 × 32). The owner
decided on 2026-11-19 that none of them is redrawn by a generator (a redraw moves coasts, rivers and roads that the route and the markers depend
on). Two ideas were tested: render the town maps with our own engine, and upscale the world map as one picture with a non-generative model. The
entry kind for these bitmaps is the question of [#2952](https://github.com/Simarglok/arcanum-steamwoven/issues/2952); this note hands it its inputs
(the last section).

## Verdicts

| Part | Verdict |
| --- | --- |
| Town maps (AC1) | **Pure render.** The original tiles are the town's area drawn by the engine at 1:1 and decimated to 1/4 (nearest neighbour). No hand-painted additions found in the three towns tested (two small differences stay unclassified, below). Rendering with our engine is feasible and reproduces the original at scale 0.25 to a mean error of about 1.8 of 255 (on the compared pixels of the stitched town). |
| Town maps at a larger scale (AC2) | Feasible: same renderer, larger sampling step. Proposal and cost below (extrapolated, not measured at the larger scale). |
| World map (AC3) | Feasible, with a caveat: a super-resolution x4 upscale (Real-ESRGAN family, not text-prompted) is sharper but moves outlines more than plain resampling. Best of the three models tried: `realesr-animevideov3` (edge F1 0.918 against 0.964 for plain Lanczos). `Map_Zoomed` is derived from the same big picture. |

## Town maps: original tiles against our render (AC1)

**Method.** The `.tmi` record (version, map number, picture W × H in isometric screen pixels, columns, rows, origin location, scale 0.25; the
layout is in [ledger row WM69](../research/ledger/ui-audio.md#world-map-window-arc-ui-073) and `AreaTownMapInfo.Parse`) gives each tile's rectangle
in the town's picture. A throwaway local seam of the AREA viewer (a camera job: output size, zoom 1, shot offsets from the town origin; **not committed**,
it hooks the `door-close` smoke stage) took 1:1 captures of 3 × 3 sector windows around the town sectors, the captures were stitched into the town
picture, decimated to 1/4 and registered against the mosaic of the original tiles. Towns: Ashbury, Shrouded Hills, Tarant (the three of the issue).

| Town | Compared pixels (both non-black) | Mean abs. error (RGB, 0–255) | Pixels with difference < 5 | < 10 |
| --- | ---: | ---: | ---: | ---: |
| Ashbury | 5.9 M | 1.81 | 97.4 % | 99.4 % |
| Shrouded Hills | 7.0 M | 1.87 | 97.3 % | 99.3 % |
| Tarant | 12.6 M | 1.94 | 97.6 % | 99.3 % |

**Findings.**

- **Registration.** Picture pixel = full 1:1 render pixel; the origin location sits at the picture centre `(W/2, H/2 + 20)` (the 20 is the cell centre
  against the viewer's foot anchor; no horizontal offset). On a test region, shift (0,0) gives an error of 1.1 against about 13 at ±1 pixel, so the alignment is exact (the table's 1.8–1.9 is over the whole town and includes the unrendered objects below).
- **Filter.** Nearest neighbour: our pixel `(4i + 2, 4j + 2)` of the 1:1 render matches (error 2.1 on a test region of about 1,000 compared pixels per tile); the next-best phase gives 11.3,
  a box filter 9.5, a 2 × 2 centre mean 8.0. The original's high-frequency energy matches nearest (14.7 against 14.8; box gives 6.6).
- **Palette.** Every tile has its own 256-colour adaptive palette (268 distinct palettes among the first 300 tiles). Quantising our pixels to a tile's
  own palette lowers the error from 1.84 to 0.66 (300 Ashbury tiles): the quantisation is most of the texture-level residual. The original's choice of
  index is not plain nearest-in-RGB (48 % exact index match; the rest unexplained).
- **Lighting.** Consistent with the area's own lighting, not a hand-set scheme: the RGB ratio of our render to the original is 1.03 / 1.03 / 0.96 on average
  (up to 4 % off; the viewer ran at its default clock, hour 15). Which hour the original tiles were rendered at is **not** determined, nor were other hours tested.
- **Drawn:** static objects (buildings, fences, lamps, plants, ships) and roofs, closed. **Not drawn:** NPCs and critters (three NPCs and one ship-deck
  NPC of our render are absent in the original). **No text or labels** anywhere in the difference image.
- **Differences found.** The hero's own house shows its roof hidden in ours (about 6,800 pixels in Ashbury) but closed in the original: a render path must
  force roofs on. One Tarant door differs (open against closed). One Ashbury cluster of about 390 pixels (a row of small features and a rock in ours) is
  not classified. A thin line at window boundaries is visible in the Tarant difference (a capture seam of the probe, not of the original).
- **Black border.** The stair-stepped border follows the **tile** grid, not the sector diamond. A full sector is exactly 200 tiles. The rule "any pixel
  of the tile lies in a rendered sector" reproduces 2,757 of the 2,990 non-black Ashbury tiles with no false positive; the remaining 233 tiles
  (about one tile of fringe) are not explained. Inside a rendered sector nothing is black. Ashbury's rendered sectors: x 503–505 by y 974–977 plus
  (502,976).

Not tested: other towns, interiors, Vormantown, other clock hours (and so the hour of the original render), the cause of the two unclassified differences.

## Town maps at a larger scale (AC2)

**Scale.** A tile covers 256 × 128 screen pixels of the town picture at every scale, so fog (one bit per tile, `WM78`), the tile under the PC marker
and the layout (columns, rows, origin) are **unchanged**; only the pixel size of a tile changes, and the viewer derives it from the tile
file (tile size = size of tile 000000, scale from the `.tmi` record). The loader reads tile size from an **uncompressed 8-bit BMP only, at most 512 pixels a side and 64 KB**
(`AreaTownMapLayout.TryTileSize`, `MaxTileBytes`): an 8-bit 256 × 128 tile (about 33 KB) fits, but 24-bit or PNG tiles need a loader change. A tile at scale 0.5 is 128 × 64, at scale 1 it is 256 × 128. The PC marker position uses
`ToPixel`, which multiplies by the scale of the `.tmi` (fog `CellOf` and click-to-tile `ToLocation` use the same scale); so a pack entry must also carry the
scale (or the loader overrides it), otherwise the marker is off by the tile-size ratio.

**Proposal: 0.5 as the default, 1.0 as an option.** The Steamwoven composition is scaled by `s = min(W / 800, H / 600)`, which is 3.6 at 3840 × 2160 and
1.8 at 1920 × 1080 (window pixels are drawn at `s` device pixels each). A scale-0.25 picture is therefore enlarged 3.6 times at 4K; 0.5 reduces that
to 1.8 and 1.0 gives 0.9, which is native at 4K. 0.5 is enough up to about 1080p–1440p and is half the linear size of 1.0 (a quarter of the bytes);
1.0 is the sharp one for 4K.

**Cost (the 0.25 figures are measured, the rest is extrapolated).**

| Item | Measured / extrapolated |
| --- | --- |
| Viewer start per window run | about 18 s (measured) |
| One shot at zoom 1 (viewer window 5120 × 1296, the height clamps at the screen) | about 0.9 s (measured) |
| Ashbury (6 windows of 3 × 3 sectors) | about 4 min (measured; shots per window were fewer than Tarant's 48) |
| Tarant (9 windows) | about 10–12 min with all 48 shots per window (measured); about 8 min if each window renders only its own sectors (not tested) |
| Stitching, Tarant | about 2:50 (measured, throwaway Python; a production writer would write tiles directly) |
| Tile size at 0.25, 8-bit BMP with its own palette | about 4 KB on disk (Tarant 24.7 MB, Ashbury 12 MB, measured on the shipped tiles; the file itself is about 3.1 KB, the rest is file-system blocks) |
| All 64,199 tiles at 0.25 | about 0.25 GB (extrapolated) |
| All 64,199 tiles at 0.5, 8-bit with own palette | about 9.3 KB per tile (file size), about 0.6 GB (extrapolated) |
| All 64,199 tiles at 1.0, 8-bit with own palette | about 33.8 KB per tile (file size), about 2.1 GB (extrapolated) |
| Total render time for 125 towns | order of hours on the Mac (extrapolated from two towns; most towns are far smaller than Tarant and Ashbury) |

Larger tiles at 8 bits would force a 256-colour quantisation of a 1:1 render, which loses colour at 1.0; a pack entry could store 24-bit PNG tiles
instead, which needs the loader change above (a format decision for #2952). A render at 0.5 or 1.0 is not a decimation of the 1:1 capture by 4 but by 2 or 1 (nearest at phase 1 or 0, or the
1:1 capture itself): that choice is not measured here.

**What the render path needs (smallest design).** A viewer run per 3 × 3 sector window at zoom 1 with: the area's own light (fixed hour), roofs forced
on, no hero, no NPCs or critters, no UI, an exact camera seam (the probe's seam shows it is a small piece of code); then cutting tiles by the `.tmi`
layout, sampling at the scale chosen, writing the pack entries and the scale. The tile-inclusion rule at the border (233 of 2,990 in Ashbury) has to be
either pinned down or replaced by a rule of our own (include a tile when any of its pixels is in a rendered sector, then accept the one-tile fringe).
Nothing of this exists in the viewer today. Follow-ups: the render path [#3063](https://github.com/Simarglok/arcanum-steamwoven/issues/3063), the open details of the border rule, palette choice and two differences [#3064](https://github.com/Simarglok/arcanum-steamwoven/issues/3064), and staging the upscaled world map [#3065](https://github.com/Simarglok/arcanum-steamwoven/issues/3065).

## World map: one picture, non-generative upscale (AC3)

**Method.** The 64 tiles of 250 × 250 (row-major 8 × 8) are joined into one 2000 × 2000 picture and upscaled x4 (8000 × 8000) with
`realesrgan-ncnn-vulkan` (already the project's tool, [HD upscale research](hd-upscale-research.md)), three models, plus a Lanczos x4 baseline. The
result is downscaled back to 2000 × 2000 (box) and compared with the original: mean absolute error, SSIM on luma, and a Canny edge check of the
outlines (coast, river, road, mountain line, text) with precision, recall and F1 at 1 and 2 pixels of tolerance (the Lanczos row is the practical
ceiling of a pure resampler, so a model is judged by how close it gets to it).

| Model (x4) | Time for 8000 × 8000 | MAE | SSIM | Edge precision / recall (1 px) | Edge F1 (1 px / 2 px) |
| --- | ---: | ---: | ---: | --- | --- |
| Lanczos (baseline, not a model) | seconds | 4.16 | 0.974 | 0.987 / 0.941 | 0.964 / 0.973 |
| `realesr-animevideov3` | 24 s | 7.26 | 0.952 | 0.905 / 0.931 | **0.918 / 0.943** |
| `realesrgan-x4plus` | 96 s | 10.46 | 0.832 | 0.957 / 0.690 | 0.801 / 0.833 |
| `realesrgan-x4plus-anime` | 58 s | 13.00 | 0.688 | 0.973 / 0.685 | 0.804 / 0.831 |

**Reading, by eye on the crops (coast with trees, the Grey Mountains ridge, the "Stonewall Range" label; the owner has the composite).**

- `animevideov3` keeps the paper tone and the ink colours, makes lettering and line work crisp, and keeps shapes where they were. It makes the canvas
  grain of the parchment somewhat more visible.
- `x4plus` turns the canvas grain into a regular grid pattern and loses part of the fine strokes (recall 0.69).
- `x4plus-anime` washes the colours out and draws horizontal streaks over the ground; it is not usable.
- Lanczos is the faithful but soft baseline: it only reproduces the original softness at x4.

The honest reading is that this model **sharpens line work and lettering and is the best of the models tried, but its outline fidelity (F1 0.92) is below
that of plain resampling (0.96)**: about 7 % of the original's edge pixels have no result edge within one pixel, and about 9 % of the result's edge pixels have no
original edge nearby. All three models are Real-ESRGAN networks (GAN-trained super-resolution, no text prompt); no model from another family was run. Whether this matters is the
owner's look at the crops, not a number; a route or marker overlay is drawn by code on the map, so the outline movement only shows against the
picture itself. A low-strength re-detail pass was **not** tested (the owner ruled out generators for the base picture).

**`Map_Zoomed` (365 × 365, continent view).** The original `Map_Zoomed` agrees with a plain downscale of the 2000 × 2000 map (mean error 6.9 with a box
filter, 5.2 with Lanczos). The same downscale of the upscaled picture gives 7.1 (box), so deriving it from the big picture keeps the two views
consistent as well as the original does. The original scales a `Map_Zoomed` of another size to its 365 rectangle (ledger row WM53), but our viewer draws the texture at its
own size and keeps 365 constants for the dots and the hover box (`AreaWorldMapArt.cs`, `AreaWorldMapArtView.cs`), so a larger `Map_Zoomed` (x4 = 1460 × 1460) needs a
draw-to-rectangle change ([#3079](https://github.com/Simarglok/arcanum-steamwoven/issues/3079)); the black margins of 68 pixels are added by the window.

**Cut back.** The 8000 × 8000 picture was cut into 64 tiles of 1000 × 1000 (about 112 MB as PNG for the whole set; the single 8000 × 8000 picture is 164 MB as PNG); the cut took
6 s. A single large picture is equally possible; the tile split only follows the original's file layout.

**Recipe chosen by the owner (2026-11-21, ARC-RENDER-3065): "Blend A", an edge-masked blend of `realesr-animevideov3` and Lanczos.** The plain network
turns the canvas weave of the parchment into a grid and the plain Lanczos picture is soft, so the picture takes the network only on strong ink lines and
lettering and Lanczos everywhere else (still non-generative: a super-resolution network and a resampler, no text prompt). Plain `animevideov3` and the wider
mask ("Blend B") are dropped; plain Lanczos is the fallback. The recipe is the script [`tools/hd_pack/world_map_upscale.py`](../../tools/hd_pack/world_map_upscale.py)
(`blend` and `outline` commands; needs numpy, opencv-python-headless and Pillow; tests `python3 -B -m unittest tools.hd_pack.test_world_map_upscale`):

1. The 2000 x 2000 map is upscaled x4 with `realesrgan-ncnn-vulkan -n realesr-animevideov3 -s 4 -t 250` (the network picture) and with Pillow's Lanczos (the baseline).
2. The mask is made on the original: Gaussian blur sigma 1.0, Canny 100/200, edge components under 30 pixels dropped (paper grain), dilation by an ellipse of radius 2
   original pixels, Gaussian feather sigma 1.2 original pixels, then cubic resize to 8000 x 8000. About 27 % of the picture is covered.
3. Result = mask x network + (1 - mask) x Lanczos, per channel, rounded.
4. Outline check (rebuilt, the script of ARC-RES-2992 was lost, [#3080](https://github.com/Simarglok/arcanum-steamwoven/issues/3080)): the candidate is box-downscaled back to
   2000 x 2000 and compared with the original; Canny 40/100 (chosen so that Lanczos gives F1 0.965, close to 0.964 above), tolerance 1 and 2 pixels.

| Picture (x4) | MAE | Edge precision / recall (1 px) | Edge F1 (1 px / 2 px) |
| --- | ---: | --- | --- |
| Lanczos | 4.16 | 0.984 / 0.946 | 0.965 / 0.991 |
| `animevideov3` plain | 7.17 | 0.971 / 0.921 | 0.945 / 0.982 |
| **Blend A (chosen)** | 5.07 | 0.982 / 0.940 | **0.960 / 0.989** |
| Blend B (wider mask, dropped) | 5.37 | 0.982 / 0.933 | 0.957 / 0.987 |

The thresholds differ from the lost script, so the absolute numbers differ a little from the table above; the comparison inside this table is consistent. Seams: the
fine canvas weave has the same strength in the blend's transition band as in plain Lanczos (ratio of the weave to the far field 0.93 against 0.92 for Lanczos), so no
step in the texture was measured at the mask border; Blend A and Lanczos were also compared at the display scales 0.9 (4K) and 0.6 (1440p) of the x4 texture.

Not tested: a further free model (only the three bundled with the project's `realesrgan-ncnn-vulkan` were run; the project has no other
super-resolution tool installed), x2 or x3 variants, test-time augmentation, a comparison on the owner's 4K screen against the stopgap of
[#2993](https://github.com/Simarglok/arcanum-steamwoven/issues/2993) (a 1:1 draw of the original tiles).

## Recommendation (AC4)

1. **Town maps: render with our engine.** The tiles are a pure render, so a larger one is a render at a larger scale, not a redraw; no generator is
   involved. Start with 0.5 and decide 1.0 after the owner's look at one town; keep the `.tmi` layout and the fog and marker logic unchanged and carry
   the scale in the pack entry. Ship it as a follow-up task after the owner decides #2952.
2. **World map: Blend A (the edge-masked blend above), the tile split and the derived `Map_Zoomed`**, picked by the owner on the crops (2026-11-21); plain Lanczos is the
   fallback, the original pixels stay the stopgap of #2993. Do not use the `x4plus` models. A re-detail pass stays an option only if
   the outline check still passes. The pack loader for the kinds `worldmap` and `map-zoomed` and the draw of `Map_Zoomed` into its rectangle: [#3079](https://github.com/Simarglok/arcanum-steamwoven/issues/3079).
3. **Both** are local derived bitmaps of the player's own installation: never in git or distributed ([hard rules](../../CLAUDE.md)); the pack holds
   them.

## Questions handed to #2952 (the entry kind for bitmaps)

- A kind for one-per-file bitmaps `worldmap` (64 tiles named as in the original, any size multiple of 250 and an integer ratio recorded) and
  `map-zoomed` (one picture), so the loader draws them at their own size.
- A kind for town-map tiles keyed by `(module, town, tile index)` with a recorded scale (0.5 or 1.0), the `.tmi` layout staying with the original;
  24-bit PNG tiles are allowed (8-bit with a palette is not needed).
- A pack may be built by the owner's machine only (the existing rule); the render of the town maps is a build step like the HD font.

## Local evidence (not in git)

`local/res2992/world/` holds the three upscales, the Lanczos baseline, the 64 cut tiles, the crops and the outline script; `local/res2992/town/` holds the
stitched 0.25 renders, the capture seam source, the registration and metric scripts and the composites. The probe seam and its hook were removed from
the working tree before this commit.

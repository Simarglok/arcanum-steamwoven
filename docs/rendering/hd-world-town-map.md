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
| Town maps (AC1) | **Pure render.** The original tiles are the town's area drawn by the engine at 1:1 and decimated to 1/4 (nearest neighbour). No hand-painted additions found in the three towns tested (the small differences (two found first, two more later) are classified in part, see "Executable check and the open details"). Rendering with our engine is feasible and reproduces the original at scale 0.25 to a mean error of about 1.8 of 255 (on the compared pixels of the stitched town). |
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
  force roofs on. One Tarant spot differs (read as a door in the first pass; see below). One Ashbury cluster of about 390 pixels (a row of small features and a rock in ours) is
  classified in part below (ARC-RES-3064). A thin line at window boundaries is visible in the Tarant difference (a capture seam of the probe, not of the original).
- **Black border.** The stair-stepped border follows the **tile** grid, not the sector diamond. A full sector is exactly 200 tiles. The rule "any pixel
  of the tile lies in a rendered sector" reproduces 2,757 of the 2,990 non-black Ashbury tiles with no false positive; the remaining 233 tiles
  (about one tile of fringe) are not explained by that rule (an empirical fit of ours; the true rule belongs to the offline tool, see below). Inside a rendered sector nothing is black. Ashbury's rendered sectors: x 503–505 by y 974–977 plus
  (502,976).

Not tested: other towns, interiors, Vormantown, other clock hours (and so the hour of the original render).

### Executable check and the open details (ARC-RES-3064)

Read 2026-11-21 in the exports (strings, imports, the two tile functions; one reader). **The executable only reads town-map tiles.** The tile file name
`townmap\<name>\<name>%06d.bmp` is referenced by exactly two functions, the loader 0x563000 (builds the name; falls back to tile 0 when the file is
missing; loads the picture through the shared bitmap loader 0x5630F0) and the existence test 0x4BEDD0 (tile index against the town's reveal bitmap, then a
file check). It imports `CreateFileA` and `WriteFile`, but the only BMP writers with a live caller are the savegame preview (`save\%s.bmp`) and the screenshot key
(`screen%04d.bmp`), plus a debug art-frame dumper (0x5360F0, 640 × 480, no caller in the export); none touches town maps, none chooses palette indexes for town tiles, none walks sectors. So the tiles were produced **before shipping by a tool
outside the game** (not in the executable, not in the player's data); its rules cannot be read from the player's installation.

- **Border rule (AC1): not needed for an HD render.** The game does not decide the border; it draws a tile when its reveal bit is set and its file exists, fades or fills the rest black
  (ledger WM77). Our render decides its own extent. Recommendation: draw a tile when any of its pixels lies in a rendered sector (reproduces 92 % of
  the original's non-black Ashbury tiles with no false positive) and **accept one tile of fringe** beyond that, or keep the original tile set's own
  extent where a pack must match it exactly. The 233 tiles of fringe stay unexplained at the tool's level, as the tool is not available.
- **Palette choice (AC2): out of scope for 24-bit HD tiles.** The game loads each BMP with its own palette and expands it to the display format; the
  quantiser that chose the indexes lived in the offline tool. A 24-bit HD tile has no palette. The 48 % exact-index measure is therefore only a
  description of the shipped tiles, not a rule to reproduce.
- **Differences (AC3), classified as far as the evidence reaches** (re-read from the saved 0.25 renders against the original mosaics and the object
  lists of the areas, no probe re-run, the capture folders were gone):
  - *Ashbury rock* (about 127 strong pixels, cell (32364, 62531), sector (505, 977)): a **mobile record (critter class)**, drawn as a boulder by ours,
    absent in the original, consistent with "the original tiles hold no NPCs or critters" (**original content state, not a bug of ours**; confidence
    about 85 %; the prototype was not looked up).
  - *Ashbury tuft row* (about 265 strong pixels, cells X 32353–32362 near Y 62527, on the sector 976/977 boundary): the **original is the brighter side**;
    ours is missing pale tufts. All 217 objects of the box (static grass tuft, one prototype) have draw commands in our plan and none is off or
    invisible, so it is **not an object our loader skips**. **Unresolved**: a viewer render of window (505, 976) or a capture-window or stitch artefact
    near the boundary; low confidence in any cause.
  - *Tarant ship deck* (a difference the first pass did not list): the original shows a tall cage-like structure, ours a low stump. Two static scenery
    objects at (62201, 65665) and (62205, 65666) carry the **off** flag; our loader suppresses them, so the offline
    tool either ignores the off flag or the objects are switched on by a script. **Content state (original quirk or script-set state)**, confidence about 70 %; whether an off-flag
    object is drawn in the original in game is not checked.
  - *Tarant door (the one of the first pass, open against closed)*: the nearest candidate is a band of wall cells (X 61930–61934, Y 65630–65639,
    sector (967, 1025)) that is continuous in the original and has a gap in ours; **no door or portal object lies in the gap** and all its objects are
    drawn by our plan, so it is probably **not a door state**; the gap is more likely a capture-window effect of the probe. **Unresolved**.
- **What is left** ([#3086](https://github.com/Simarglok/arcanum-steamwoven/issues/3086)): re-render the two places with the committed town render path (#3063) and compare window by window; count
  off-flag scenery in the three towns against the original tiles.

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
draw-to-rectangle change ([#3065](https://github.com/Simarglok/arcanum-steamwoven/issues/3065)); the black margins of 68 pixels are added by the window.

**Cut back.** The 8000 × 8000 picture was cut into 64 tiles of 1000 × 1000 (about 112 MB as PNG for the whole set; the single 8000 × 8000 picture is 164 MB as PNG); the cut took
6 s. A single large picture is equally possible; the tile split only follows the original's file layout.

Not tested: a further free model (only the three bundled with the project's `realesrgan-ncnn-vulkan` were run; the project has no other
super-resolution tool installed), x2 or x3 variants, test-time augmentation, a comparison on the owner's 4K screen against the stopgap of
[#2993](https://github.com/Simarglok/arcanum-steamwoven/issues/2993) (a 1:1 draw of the original tiles).

## Recommendation (AC4)

1. **Town maps: render with our engine.** The tiles are a pure render, so a larger one is a render at a larger scale, not a redraw; no generator is
   involved. Start with 0.5 and decide 1.0 after the owner's look at one town; keep the `.tmi` layout and the fog and marker logic unchanged and carry
   the scale in the pack entry. Ship it as a follow-up task after the owner decides #2952.
2. **World map: take the `animevideov3` x4 picture, the tile split and the derived `Map_Zoomed` as the proposal**, judged by the owner on the crops;
   the alternative is to keep the original pixels (the stopgap of #2993). Do not use the `x4plus` models. A re-detail pass stays an option only if
   the outline check still passes.
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

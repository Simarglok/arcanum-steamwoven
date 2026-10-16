# HD upscale methods at 2× (ARC-RES-032)

Status: **research only.** This note compares upscaling methods for the future local
HD pack of the [HD presentation contract](hd-presentation-contract.md)
(ARC-RENDER-013) and recommends a default. It adds no pack format, build tool,
loader or renderer path (ARC-DATA-014, ARC-DEV-003, ARC-RENDER-014/016). No upscaled
frame, crop, screenshot, montage or model weight is in this repository, its PRs or
their attachments: every output of this study stayed under the ignored `local/`
directory on the owner's Mac. Visual quality is judged here only by the numeric
checks below plus one developer look at local outputs; the human before/after review
is ARC-RENDER-015 and is never a merge gate.

Owner decisions this note works under (2026-10-16, [decisions](../architecture/decisions.md)):
scale 2× only; the owner builds the pack on their own machine; the pack is a separate
local directory outside the repository and is not distributed. The legal position
(upscales are derivative works of the original art; no MIT licence can cover them)
is the owner's decision record, not repeated here.

Evidence vocabulary: **local observation** = a bounded run on the owner's unchanged
installation (identities below); **source fact** = a cited tool/licence text;
**judgement** = an inference or a developer's look, stated as such.

## Inputs and harness

Local observation. Installed original: the `GAME_ROLES` identities of
[`tools/local_dev/launch.py`](../../tools/local_dev/launch.py) (module
`8bddf7ae…c76f`, resource1 `6dd9d10e…8475`, resource2 `4078653c…e24d`, exterior mobile
`a548f611…e6a5`, cave mobile `aa29827d…8318`). Frames were taken through the
production strict Area load (`AreaDataOwner.LoadStrict`, `AreaScenePlan`, the
frame copy of `AreaLoadOperation.ReadArea`) and `ArtDecoder`, so they are exactly
the RGBA the AREA viewer draws (index 0 → `(0,0,0,0)`, every other index opaque).
Terrain samples are `AreaTerrainChunks.Compose` level-0 chunks (512×512 world
pixels) of the exterior core.

**Sample set (16 frames, 18 extra palette-slot variants, 3 terrain chunks):**

| Kind | ART (frame) | Size | Colours | Palettes in ART |
| --- | --- | --- | ---: | ---: |
| Container | `Chest_Fancy-1` (g0 f0) | 64×47 | 191 | 1 |
| Scenery | `tree`, `T_young_pine_2`, `T_young_pine_1` (g0 f0) | 249×253, 215×260, 204×267 | 251, 198, 202 | 1 |
| Critter / unique NPC | `hgmlaxah` (g6 f10 p2), `hgmv1xaa` (g5 f0 p0), `hmfc2xah` (g7 f10 p3), `hmmcdxah` (g5 f10 p1) | 107×52, 40×89, 76×46, 73×39 | 173–188 | 4 |
| Item | `G_Kedurastem` (g0 f0) | 31×44 | 251 | 1 |
| Facade | `Black` (f144), `Mountain_04`, `Mountain_06` (f0) | 78×40 | 1, 122, 139 | 1 |
| Cave wall | `Cv1lfcU0`, `RCvlfcU0` (g0 f0) | 75×152, 51×148 | 113, 199 | 1 |
| Player body | `hmfuwxaa`, `hmmuwxaa` (g1 f0, slots 0–3) | 25×78, 26×79 | 155, 194 | 4 |
| Terrain | chunks (4,1), (−1,1), (2,3): the three most colourful exterior chunks (a chunk with >1/16 black pixels is skipped) | 512×512 each, plus the 1536×1536 3×3 neighbourhood | 617–808 | — |

The exterior core has no wall objects (its "walls" are facades and the hull), so
the two wall samples come from the Crash Site cave (`AreaOriginalValidator.OpenCaveFiles`).

**Harness (local, not committed).** A throwaway C# exporter referencing
`Arcanum.TerrainViewer.Core`, a stdlib-only Python runner and ImageMagick raw-RGBA
I/O live in `local/hd-research/` on the owner's Mac. Every method produces exactly
`2w × 2h` RGBA; checks compare it with the nearest 2× replica of the source
(the contract's identity frame):

- **Soft alpha %**: output pixels with `0 < α < 255` (the source has none).
- **Silhouette IoU**: intersection/union of `α ≥ 128` with the nearest mask.
- **In-palette %**: opaque output pixels whose RGB occurs in the source frame.
- **MAE**: mean absolute RGB difference from the nearest replica over pixels opaque
  in both (0–255 scale; a measure of change, not of quality).
- **Dark halo %**: silhouette pixels (nearest-opaque next to nearest-transparent)
  that are more than 40 luma darker than the nearest replica, i.e. transparent black
  bled into the edge.
- **Recolour exact %**: upscale slot 0, recolour by the per-pixel slot-0→slot-k
  colour map of the source, compare with the upscale of slot k.
- **Terrain**: each method upscales (a) the 512 chunk alone, (b) the chunk with 32
  source pixels of neighbour context, cropped back, (c) every tile/facade frame
  separately, recomposed at 2× in plan order (`α ≥ 128` wins), and (d) the whole
  1536 neighbourhood, whose centre is the reference. MAE vs (d) is split into the
  outer 16-pixel band of the 1024 output and the interior; "dark %" counts pixels
  more than 40 luma darker than (d); "holes %" counts pixels no tile covered.

Tools (source facts; SHA-256 of the local copies):

| Tool | Version | SHA-256 |
| --- | --- | --- |
| ImageMagick (Homebrew) | 7.1.2-29 Q16-HDRI arm64 | binary `c8e0be14…250b` |
| realesrgan-ncnn-vulkan (xinntao release v0.2.5.0, `realesrgan-ncnn-vulkan-20220424-macos.zip`) | 20220424, universal arm64/x86_64 | zip `e0ad0558…333a`, binary `c1c35d92…c7cc` |
| Model `realesr-animevideov3-x2` | same zip | bin `548a36f9…ab2d`, param `b88ff4f0…0a04` |
| Model `realesrgan-x4plus` | same zip | bin `713ee713…dcbd`, param `35330ece…0a86` |
| Model `realesrgan-x4plus-anime` | same zip | bin `fe01c269…bacc`, param `2b8fb6e0…21b4` |

Machine: Apple M4 Pro (20-core GPU, Metal 4 via MoltenVK inside the binary), 24 GiB
unified memory, macOS 27.

## What the source art implies

Local observation unless stated.

1. **Alpha is binary.** Index 0 is the only transparency; every decoded pixel is
   `α = 0` or `α = 255`. Any soft alpha in an output is invented.
2. **Palette slots.** Of the 2,444 exterior visual chains, 2,430 draw requested and
   effective slot 0; the rest are `1→1` (5), `2→2` (5), `3→3` (2) and `2→0` (2, the
   requested slot is absent and falls back). The 1,975 distinct exterior frames are
   1,663 facade, 253 tile, 32 scenery, 19 critter/NPC, 6 item and 2 container frames;
   every frame's ART has one palette table except critters (18 of 19 distinct critter
   frames come from four-table ARTs) and 2 scenery frames.
3. **Recolouring an upscale is not exact even without an upscaler.** For the nearest
   replica, a slot-0→slot-k colour map reproduces 75.9–100 % of the pixels: two
   indices that share a colour in slot 0 can differ in slot k (`hgmlaxah`: 614 such
   source pixels). For upscalers it is worse (table below). **An HD frame must be
   built from the RGBA of its own effective slot**, which matches the contract's
   frame key (entry, effective palette, group, frame). For the Crash Site exterior
   that is 14 extra frames, not four times the pack.
4. **Size of the job (whole game).** A census of every `.art` name in the module,
   `Arcanum4.dat`, `arcanum3/2/1.dat` and `tig.dat` (first archive in that order wins
   a name; this is a counting order, not the [resource precedence](../research/content-precedence.md)):
   13,556 ART, 337,513 frames, **1.31 G source pixels with one palette per ART, 4.16 G
   with every present palette** (437 ARTs refused by the strict decoder are not
   counted). Critters, unique NPCs and monsters are 81 % of the pixels; tiles 0.3 %,
   facades 7.8 %. At 2× the output is 4× that: ~21 GB of raw RGBA for one palette per
   ART before PNG compression.
5. **Crash Site at 2×.** The 1,975 distinct exterior frames hold 6.48 M pixels
   (24.7 MiB RGBA); at 2× 98.9 MiB, inside the contract's AREA HD limit
   (`4 · 81,853,096` B). Terrain chunks at 2× are 4 MiB each, so the 192 MiB chunk
   cache would hold 48 resident 1024² chunks instead of 192 — an ARC-RENDER-016 budget
   question.

## Methods compared

| Id | Method | Kind | Native scale | Run as |
| --- | --- | --- | --- | --- |
| nearest | pixel replication | identity baseline | any | `magick -sample 200%` |
| scale2X | Scale2x/EPX | pixel-art, copies source colours | 2× | `magick -define magnify:method=scale2X -magnify` |
| eagle2X | Eagle | pixel-art, copies source colours | 2× | same, `eagle2X` |
| hq2X | hq2x | pixel-art, blends | 2× | same, `hq2X` |
| xbr2X | xBR (Hyllian), not Zenju's xBRZ | pixel-art, blends | 2× | same, `xbr2X` |
| av3 | Real-ESRGAN `realesr-animevideov3-x2` | AI (small VGG-style net) | 2× | `realesrgan-ncnn-vulkan -s 2` |
| x4plus | Real-ESRGAN `realesrgan-x4plus` (RRDB) | AI, general images | 4× → Box 2× | `-s 4`, then `magick -filter Box -resize 50%` |
| x4anime | Real-ESRGAN `realesrgan-x4plus-anime` | AI, illustrations | 4× → Box 2× | as x4plus |
| …+bleed | the AI method on RGB with opaque colours bled 8 px into the transparent area (rest = mean colour), alpha from the same AI upscale of the binary mask, thresholded at 128 | alpha handling variant | | two runs |
| …+legacymask | as +bleed, but alpha = the nearest 2× Legacy mask (the contract's default HD alpha) | alpha handling variant | | one run |

Not run, and why: **xBRZ** (Zenju, GPL-3.0 C++) and **ScaleFX** (Sp00kyFox, MIT
libretro shader) have no CLI on this machine; ScaleFX is natively **3×** (its last
pass is `scale5 = 3.0` in `scalefx.slangp`), so a 2× result needs a resample that
breaks its "colours only from the original" property. Both are pixel-art scalers of
the same family as xBR/hq2x, and the measurements below show that family adds almost
no detail to this pre-rendered, dithered art (judgement). `RealESRGAN_x2plus` and
`realesr-general-x4v3` exist only as PyTorch weights in the Real-ESRGAN model zoo and
were not run (no PyTorch here).

## Results

### Sprites (16 frames, mean)

Local observation. Seconds are the sum over the 16 frames and 18 slot variants, one
process per file (start-up dominates the pixel-art rows).

| Method | Soft alpha % | Silhouette IoU | In-palette % | Output colours (mean) | MAE | Dark halo % | Seconds (34 files) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| nearest | 0.0 | 1.0 | 100.0 | 171 | 0.0 | 0.0 | 0.3 |
| scale2X | 0.0 | 0.982 | 100.0 | 171 | 0.39 | 0.0 | 0.3 |
| eagle2X | 0.0 | 0.975 | 100.0 | 170 | 0.09 | 0.0 | 0.3 |
| hq2X | 1.12 | 0.996 | 93.5 | 554 | 0.21 | 0.0 | 0.3 |
| xbr2X | 1.92 | 0.962 | 87.2 | 683 | 1.0 | 0.0 | 0.4 |
| av3 | 13.8 | 0.999 | 9.3 | 11888 | 9.19 | 7.3 | 4.4 |
| av3+bleed | 0.0 | 0.972 | 8.8 | 10788 | 6.34 | 0.17 | 12.1 |
| av3+legacymask | 0.0 | 1.0 | 8.8 | 10776 | 6.3 | 0.15 | 6.7 |
| x4plus | 14.28 | 0.999 | 10.1 | 6371 | 11.9 | 6.69 | 17.9 |
| x4plus+bleed | 0.0 | 0.973 | 9.5 | 7377 | 9.05 | 0.7 | 43.2 |
| x4plus+legacymask | 0.0 | 1.0 | 9.5 | 7400 | 8.99 | 0.63 | 19.2 |
| x4anime | 14.28 | 0.999 | 8.9 | 5262 | 13.24 | 11.41 | 9.6 |

- **Pixel-art scalers barely change this art.** Scale2x/Eagle copy source colours
  (100 % in palette) and differ from the nearest replica on well under 1 % of the
  value range; hq2x and xBR blend a little (87–94 % in palette). They were designed
  for flat-colour sprites; Arcanum's sprites are pre-rendered and dithered, with
  ~170 colours per frame, so the detectors find few clean edges. Judgement from the
  local outputs: at 2× they look like the nearest replica with slightly smoothed
  outlines; Scale2x/Eagle also erode thin silhouette details (IoU 0.98).
- **AI scalers repaint every pixel** (only ~9–10 % of output pixels keep a source
  colour; ~6,000–12,000 colours per frame) and invent soft alpha on ~14 % of the
  pixels when fed RGBA directly. Because transparent pixels are `(0,0,0,0)`, the
  network sees black around the sprite: 6.7–11.4 % of silhouette pixels come out
  more than 40 luma darker (a dark halo).
- **Alpha handling fixes the halo.** Bleeding opaque colours into the transparent
  area before the run and taking alpha separately removes the soft alpha and cuts the
  halo to 0.15 % (av3) / 0.63 % (x4plus). With the contract's default alpha (the
  nearest Legacy mask) the silhouette is exactly the Legacy one (IoU 1.0) at no extra
  network run; the AI-upscaled, thresholded mask (+bleed) is smoother but moves 3 % of
  the silhouette, which the contract allows only as an opt-in pack option.
- Judgement from the local outputs: `x4plus` keeps the most texture (cloth, rock,
  bark) and its 4×→2× Box step keeps edges crisp; `av3` is smoother and a little
  "plastic"; `x4anime` flattens texture into painted areas and destroys the terrain's
  pebble detail. This is one developer's look, not the ARC-RENDER-015 review.

### Palette slots (18 slot variants)

Recolour exact % on the 7 variants with no ambiguous source colours (minimum / mean):
nearest 100 / 100, eagle2X 99.6 / 99.9, scale2X 98.8 / 99.7, hq2X 90.9 / 92.6,
xbr2X 79.7 / 83.4, every AI variant 0.0 / ≤ 0.3. Only pure colour-copying scalers
come close, and none is exact; AI output must be built per slot.

### Terrain seams (3 chunks, mean)

Local observation. MAE against the centre of the 1536 neighbourhood bake; band = the
outer 16 output pixels of the 1024 chunk.

| Method | Chunk alone: band / interior MAE | Padded 32: band / interior MAE | Per tile: MAE, dark %, holes % | Seconds: chunk / padded / 3×3 region / tiles |
| --- | --- | --- | --- | --- |
| nearest | 0.0 / 0.0 | 0.0 / 0.0 | 0.0, 0.0, 0.0 | 0.13 / 0.16 / 1.03 / 1.02 |
| scale2X | 0.05 / 0.0 | 0.0 / 0.0 | 0.54, 0.749, 1.04 | 0.15 / 0.19 / 1.19 / 1.03 |
| eagle2X | 0.0 / 0.0 | 0.0 / 0.0 | 0.55, 0.791, 1.1 | 0.13 / 0.16 / 1.06 / 0.99 |
| hq2X | 0.01 / 0.0 | 0.0 / 0.0 | 0.11, 0.054, 0.07 | 0.16 / 0.2 / 1.33 / 0.99 |
| xbr2X | 0.12 / 0.0 | 0.0 / 0.0 | 0.92, 0.998, 1.38 | 0.21 / 0.26 / 1.7 / 1.02 |
| av3 | 1.63 / 0.06 | 0.04 / 0.05 | 3.63, 0.909, 0.0 | 0.33 / 0.38 / 1.77 / 14.69 |
| av3+bleed | 1.63 / 0.06 | 0.04 / 0.05 | 2.11, 0.019, 0.0 | 0.34 / 0.39 / 1.86 / 38.78 |
| av3+legacymask | 1.63 / 0.06 | 0.04 / 0.05 | 2.07, 0.013, 0.0 | 0.34 / 0.39 / 1.84 / 20.42 |
| x4plus | 3.36 / 1.26 | 1.28 / 1.14 | 6.12, 0.385, 0.0 | 3.73 / 4.55 / 29.38 / 52.47 |
| x4plus+bleed | 3.36 / 1.26 | 1.28 / 1.14 | 5.69, 0.029, 0.0 | 3.68 / 4.52 / 29.67 / 111.33 |
| x4plus+legacymask | 3.36 / 1.26 | 1.28 / 1.14 | 5.69, 0.025, 0.0 | 3.74 / 4.52 / 29.23 / 55.89 |
| x4anime | 2.77 / 0.66 | 0.54 / 0.59 | 4.02, 0.749, 0.0 | 2.13 / 2.54 / 16.85 / 30.13 |

- **Per-tile upscaling is the worst option for every non-identity method.** Tiles
  and facades are 78×40 diamonds with transparent corners; upscaled one by one, the
  diamond edges get dark seams (AI: 0.4–0.9 % of the chunk, the alpha variants remove
  most of it) and the colour-copying scalers erode the diamond edges so that 1.0–1.4 %
  of the chunk is left uncovered (holes the Legacy terrain does not have). Per-tile is
  also the slowest (one process per frame).
- **A 512 chunk alone gets an edge band.** AI output differs from the neighbourhood
  bake mainly in the outer 16 pixels (band MAE 1.6–3.4 vs interior 0.06–1.3), i.e. a
  visible seam where two independently upscaled chunks meet.
- **32 source pixels of neighbour context remove the band.** With padding the band
  error equals the interior error for every method (av3 0.04/0.05, x4plus 1.28/1.14),
  so chunk edges no longer stand out. The interior residual of x4plus (and x4anime)
  is not a seam: the ncnn binary splits images into tiles internally, and the tile
  grid of a 576² input differs from that of a 1536² input (see Determinism).

### Time and memory

Local observation, directory mode (one process for all 292 tile/facade frames of the
three chunks, 0.91 M source pixels) and one 1536² region; footprint is macOS "peak
memory footprint" of the process (`/usr/bin/time -l`, unified memory, includes GPU
allocations the process owns).

| Method | 292 tiles, batch | 1536² region, auto tile | 1536² region, `-t 64` |
| --- | --- | --- | --- |
| av3 | 0.64 s (~1.4 M px/s), 156 MiB | 1.9 s, 333 MiB | 2.7 s, 295 MiB |
| x4plus | 13.5 s (~67 k px/s), 665 MiB | 24.7 s (~96 k px/s), 1,833 MiB | 32.3 s, 926 MiB |
| x4anime | 4.0 s (~228 k px/s), 320 MiB | 10.8 s, 1,586 MiB | 13.4 s, 664 MiB |
| hq2X / xbr2X (ImageMagick) | — | 1.5 s / 2.1 s, 246 MiB | — |

The x4 models run at 4×, so they do 4× the network work of a native 2× model and
need the extra Box step. A smaller tile caps memory (x4plus 1.8 GiB → 0.9 GiB) at
~30 % more time.

Extrapolation (inference from the rates above, not a measurement): one pass over the
whole game's 1.31 G source pixels takes roughly 4–6 h with x4plus, about 1.6 h
with x4anime and under half an hour with av3 on this M4 Pro; every present palette (4.16 G)
about 3.2× that. The Crash Site exterior (6.48 M pixels) is about 1–2 minutes of
network time with x4plus. Batch (directory) mode matters: one process per frame adds
~0.2–0.5 s start-up per file, which for 337,513 frames would exceed the network time.

### Determinism

Local observation on the one available GPU (M4 Pro), three inputs (tree, critter,
chunk), two models; percent = differing output bytes vs the default run.

| Change | av3 | x4plus |
| --- | --- | --- |
| Same command twice more | identical | identical |
| Thread split `-j 1:1:1` | identical | identical |
| Tile size `-t 400` (vs auto) | 0–0.9 % (max Δ 74) | 0–19 % (max Δ 122) |
| Tile size `-t 64` | 1.3–4.3 % | 26–57 % |
| Tile size `-t 32` | 4.2–6.3 % (max Δ 116) | 33–65 % (max Δ 175) |
| TTA `-x` | 33–59 % | 35–63 % |
| CPU (`-g -1`) | refused: "invalid gpu device" | refused |

- On one machine with fixed arguments the output is bit-for-bit repeatable.
- The **tile size changes the result**, by a lot for x4plus. With `-t 0` (auto) the
  binary chooses the tile size from the GPU heap budget (upstream `src/main.cpp`:
  200 above 1,900 MB, then 100, 64, 32; on this Mac presumably 200), so two GPUs with
  different memory get different tilings and therefore different pixels. Frames
  smaller than the tile are one tile and are unaffected (the critter at `-t 400`).
  The macOS build also
  ran with fp16 storage and arithmetic (`fp16-p/s/a=1/1/1` in its log); a GPU without
  fp16 support computes in fp32 and rounds differently (inference from ncnn's device
  capability switch; not measured, no second GPU or CPU path was available).
- Consequence: an HD pack is **built once and identified by the hashes of its
  outputs**, never re-derived on another machine and expected to match. The build
  record must pin the tool and model hashes, scale, model, an explicit tile size (not
  auto), TTA off, the GPU name and the alpha-handling and padding parameters.

## Recommendation

**Default method for the first pack (characters and objects): Real-ESRGAN
`realesrgan-x4plus` through `realesrgan-ncnn-vulkan`, 4× then Box downsample to
exactly 2×, on colour-bled RGB, with the alpha of the Legacy frame (nearest mask),
one HD frame per frame key (effective palette slot), explicit tile size, TTA off,
batch mode.** Reasons: it is the AI option that keeps the most texture in the local
look, it introduces no soft alpha and no silhouette change (contract default alpha),
the halo is down to 0.63 % of edge pixels, the Crash Site exterior costs minutes, and
its code, binary and weights carry permissive licences.

- **Fast preview / second candidate:** `realesr-animevideov3-x2` with the same alpha
  handling: native 2×, 13–21× faster, smoother look, smallest change from the original
  (MAE 6.3). Worth keeping as the A/B alternative for ARC-RENDER-015.
- **Not recommended:** `realesrgan-x4plus-anime` (flattens texture; strongest halo
  when fed RGBA) and any AI run on raw RGBA (soft alpha, dark halo).
- **No-AI fallback:** hq2x — the only pixel-art scaler here with no holes or halo in
  any test and low per-tile error — but expect little visible gain over Legacy; the
  pixel-art family (Scale2x, Eagle, xBR, and by extension xBRZ/ScaleFX) is not a good
  match for pre-rendered art.
- **Terrain (ARC-RENDER-016):** upscale composed 512 chunks with at least 32 source
  pixels of neighbour context, cropped back; never tile by tile. The ARC-RENDER-013
  contract keeps terrain Legacy until then, and an HD chunk is not a per-frame
  replacement, so this needs its own contract amendment there.
- The final choice between x4plus and av3 is a human visual decision
  (ARC-RENDER-015). The contract's consequence 3 applies to both: HD detail reaches
  the screen only above magnification 1, so the comparison must be made at zoom 2.

## Licences

Source facts from each project's licence file or header (checked 2026-10-16), plus
judgement where marked. None of these tools or weights is added to the repository;
the owner runs them locally. Not legal advice.

| Component | Licence | Use in this project |
| --- | --- | --- |
| ImageMagick (incl. its Scale2x, Eagle, hq2x, xBR `magnify` methods) | ImageMagick License (Apache-2.0-style) | External tool; fine for local builds. |
| realesrgan-ncnn-vulkan (`xinntao/Real-ESRGAN-ncnn-vulkan`) | MIT (© 2021 Xintao Wang); derived from `nihui/realsr-ncnn-vulkan`, MIT | External tool. |
| ncnn (Tencent), statically inside the binary | BSD-3-Clause, third-party parts under their own terms | Through the tool only. |
| MoltenVK, glslang/SPIRV-Tools, statically inside the macOS binary (LunarG SDK build) | Apache-2.0 (MoltenVK); glslang/SPIRV-Tools permissive (not re-read) | Through the tool only. |
| Real-ESRGAN code and the three weights above (`xinntao/Real-ESRGAN`) | Repository BSD-3-Clause; the release weights carry no separate licence | Judgement: the repository licence is the only stated grant for the weights. The networks were trained on public research datasets whose own terms are not re-checked here; whether such terms bind a model's outputs is unsettled. Local, non-distributed use only. |
| xBRZ (Zenju) | GPL-3.0 | May be run as a separate local build tool; never copied or linked into this MIT code. |
| ScaleFX (libretro `slang-shaders`) | MIT (shader header, © 2016 Sp00kyFox) | Shader; would need a runner. |
| Upscayl (GUI around the same ncnn binaries) | AGPL-3.0; its extra community models have their own, mixed licences | Not used; check each model's licence before use. |
| **Pack frames themselves** | Derivative works of the original Arcanum art; no MIT or other licence of this project covers them | Owner's decision: local pack outside git, not distributed. |

## Consequences for the next tasks

- **ARC-DATA-014 (pack format):** key every HD frame by the contract frame key
  including the effective palette slot; record per pack the tool/model hashes, scale,
  tile size, TTA, alpha mode (`legacy` default, `soft` opt-in), bleed radius, padding
  and GPU; store output hashes. Rebuilding on another machine is a new pack identity.
- **ARC-DEV-003 (build tool):** run in directory batches; enumerate frame keys from
  the strict Area load (as this study's exporter did) so palette fallbacks such as
  `2→0` are built under their effective slot; refuse `-t 0`.
- **ARC-RENDER-014 (budgets):** the Crash Site exterior is 98.9 MiB at 2× for all
  distinct frames, inside the contract's AREA HD limit. Real usage still has to be
  measured there.
- **ARC-RENDER-016 (terrain):** padded chunk bake; at 2× a 1024² chunk is 4 MiB, so the
  current 192 MiB chunk cache would hold 48 chunks instead of 192.
- **Storage (whole game):** ~21 GB raw RGBA for one palette per ART at 2× before PNG
  compression; building only the frame keys a map actually uses keeps early packs
  small.

## Not verified / open

- Cross-GPU and CPU output differences were not measured (one GPU; the binary has
  no CPU path). Only same-machine repeatability is shown.
- Visual quality is a developer's look plus numeric proxies; the numbers measure
  change and artefacts, not "better". Human review is ARC-RENDER-015.
- xBRZ, ScaleFX, `RealESRGAN_x2plus`, `realesr-general-x4v3`, waifu2x, Real-CUGAN and
  community models were not run.
- The sample is 16 frames of the Crash Site exterior and cave plus three terrain
  chunks; interface art, roofs, eye candy, lights, translucent or animated effects,
  and other maps are not covered. Animation-to-animation flicker between successive
  upscaled frames was not measured.
- The whole-game census counts names by a fixed archive order, not the resource
  precedence, and skips the 437 ARTs the strict decoder refuses.
- Time and memory are for this M4 Pro with 24 GiB; the footprint figure is the
  process's own and is not a VRAM measurement on discrete GPUs.

Provenance: measurements by this repository's own decoder and Area code on the
owner's installation; tools are used as external programs; no CE code or structure is
used.

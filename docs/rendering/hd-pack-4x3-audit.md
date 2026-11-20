# HD pack audit against the 4:3 composition (ARC-RES-2949)

Status: **audit, 2026-11-19** (work item ARC-RES-2949, [#2949](https://github.com/Simarglok/arcanum-steamwoven/issues/2949)). Docs only; the
installed pack was read, never written. Requirements touched (status unchanged):
[FR-UI-007](../requirements/ui-media-lifecycle.md#fr-ui-007), [FR-PRODUCT-001](../requirements/content-product.md#fr-product-001).

The question: does the installed local HD pack `interface-redraw-4x` still fit the interface format of the owner's decisions of 2026-11-19
(epic [#2446](https://github.com/Simarglok/arcanum-steamwoven/issues/2446)): one centred 4:3 composition scaled by `s = min(W / 800, H / 600)`
(fractional), the sides world or black, no full-width parts, Steamwoven kit masters for 3840 × 2160. The inventory itself (what the pack holds and
lacks) is the [gap census](hd-pack-gap-census.md) and is not repeated. No pixel, hash or image of the game is in this note: only kinds, counts,
sizes and measured numbers. The captures and composites stay on the owner's device.

## Summary

- **Geometry works.** Every pack entry is a 4× frame of one original art, registered to the Legacy geometry; the column draws the original layout
  at `s`, so nothing in the pack depended on the full-width layout. The full-width pieces (side tiles, side boards, per-family full-middle backdrops)
  are generated at run time, not pack entries; they were removed by [#2926](https://github.com/Simarglok/arcanum-steamwoven/issues/2926).
- **The column does not sample the pack with nearest anywhere.** The issue's premise is out of date: since ARC-UI-112 a direct pack frame is drawn
  trilinear with mipmaps, the strips and window frames are generated chrome whose pack pictures are area-averaged to the paint scale by
  `InterfaceChromeRenderer.ResamplePicture` (ARC-UI-141 in the code; the fractional own scale is ARC-UI-148),
  and the font glyphs are bilinear without mipmaps (ARC-UI-145). The presentation contract has no nearest rule for the interface.
- **Two sampling changes are needed:** direct frames should be area-resampled once per scale instead of trilinear
  ([#3002](https://github.com/Simarglok/arcanum-steamwoven/issues/3002)); glyphs need the area filter below `s` = 2, where bilinear without mipmaps
  aliases (equal to nearest at 1280 × 800) ([#2939](https://github.com/Simarglok/arcanum-steamwoven/issues/2939), widened).
- **The widescreen main-menu plate** was dropped by the owner (option C, [#3003](https://github.com/Simarglok/arcanum-steamwoven/issues/3003), 2026-11-20): the game ignores the file.
- **Kit master scale: keep 3.6 px per unit** (the TRD's). See [Kit master scale](#kit-master-scale-36-or-4-px-per-unit).

## Entry kinds and verdicts (AC1)

From the pack's `index.tsv` and `manifest.json` (7,075 entries: 6,949 sprites and 126 portraits). Every entry is 4× its Legacy frame in both
directions (no 2× entry is installed). "Opaque" is the plain alpha of the frame (no `masked` mode); slices are the pack's build slices.

| Kind | Frames | Arts | Alpha | How the column draws it | Verdict |
| --- | ---: | ---: | --- | --- | --- |
| panels | 315 | 221 | opaque (300 `interface`, 15 `character-hd114`) | 35 frames of 27 recipe arts (strips, window frames) as generated chrome with the pack picture area-averaged to the device scale; the other 280 directly, trilinear | works for recipe arts; the direct ones need [#3002](https://github.com/Simarglok/arcanum-steamwoven/issues/3002) |
| buttons | 243 | 109 | 227 opaque, 16 `masked` | 23 frames of 12 recipe arts generated; the rest directly, trilinear | as panels |
| gauges | 29 | 29 | opaque | 5 recipe arts generated; the rest directly | as panels |
| icons | 108 | 108 | opaque | directly, trilinear | needs [#3002](https://github.com/Simarglok/arcanum-steamwoven/issues/3002) |
| spells | 218 | 106 | opaque (193 `character-hd114`) | directly, trilinear | needs [#3002](https://github.com/Simarglok/arcanum-steamwoven/issues/3002) |
| tabs | 46 | 23 | opaque | directly, trilinear | needs [#3002](https://github.com/Simarglok/arcanum-steamwoven/issues/3002) |
| indicators | 60 | 9 | opaque | directly, trilinear | needs [#3002](https://github.com/Simarglok/arcanum-steamwoven/issues/3002) |
| schematics (record pictures) | 63 | 63 | opaque | directly, trilinear, inside the schematic window | needs [#3002](https://github.com/Simarglok/arcanum-steamwoven/issues/3002) |
| portraits (heroes 96, NPCs 30) | 126 | 126 | opaque | directly, trilinear; the follower group stays in the column on its original places ([#2925](https://github.com/Simarglok/arcanum-steamwoven/issues/2925) | works; sharper with [#3002](https://github.com/Simarglok/arcanum-steamwoven/issues/3002) |
| fonts (glyph frames) | 5,807 | 28 | `masked` | per glyph, bilinear without mipmaps, at the window's scale | works at `s` ≥ 2.4; needs the area filter below 2 ([#2939](https://github.com/Simarglok/arcanum-steamwoven/issues/2939)) |
| cursors | 60 | 4 | opaque | Legacy by design (gap census) | works (not drawn from the pack) |
| network-play arts (part of the kinds above) | 41 | 23 | opaque | not drawn (network play is out of scope) | obsolete; dropped at the next pack build ([#2362](https://github.com/Simarglok/arcanum-steamwoven/issues/2362)) |
| `main-menu-16x9.png` (pack root) | 1 | | opaque, 1664 × 936 | not drawn under any Steamwoven plan (the column included); only with the HD option on and the generated interface off | dropped (owner, [#3003](https://github.com/Simarglok/arcanum-steamwoven/issues/3003)): no longer read |

The recipe arts are the 45 names of `InterfaceChromeRecipes` (`tools/terrain-viewer/Core/InterfaceChrome.cs`); 44 have pack frames (63 frames).

**Scale range.** At 4 px per unit the pack covers the composition up to `s` = 4. The sizes of the TRD run from `s` 1.2 (1280 × 720) through 1.33
(1280 × 800), 1.8 (1920 × 1080), 2.4 (2560 × 1440), 2.67 (2560 × 1600), 3.6 (3840 × 2160) to 4.8 (5120 × 2880): the pack is minified everywhere
except above `s` = 4 (only 5120 × 2880), where it is magnified by at most 1.2. No kind needs a larger scale for the TRD's sizes.

## Sampling (AC2)

**Captures.** The play screen in the column (`./arcanum smoke pr --stages bar --env ARC_AREA_BAR_STEAMWOVEN=1 --env ARC_HD_PACK=<pack>
--env ARC_STEAMWOVEN_VARIANTS=column --env ARC_STEAMWOVEN_SIZES=1920x1080,2560x1440,2560x1600,1280x800,3840x2160 --env ARC_AREA_BAR_CAPTURE=<prefix>`,
PASS, 2026-11-19) was captured at the five sizes with the play screen, every window of the hook and an open menu (local only). They show that the
strips and the window frames in the column are the **generated** chrome (generated wood with the pack's pictures; the Steamwoven layout is on only
with the generated interface), placed at the original offsets: the pack's bottom strip correlates with the capture best at offset (0, 0) at
1280 × 800, 1920 × 1080 and 3840 × 2160 (normalised cross-correlation 0.88 to 0.89; the rest is the generated wood and the HUD drawn over it).

**Filters in the code** (origin/main, 2026-11-19): a direct pack frame is a `CanvasTexture` with `LinearWithMipmaps` and generated mipmaps
(`InterfaceHd.cs`, ARC-UI-112; the canvas texture's filter wins over the parent node's); a generated frame is painted at its draw scale (ARC-UI-141, fractional
since ARC-UI-148) with its pack picture averaged by `InterfaceChromeRenderer.ResamplePicture` (area filter); a glyph is a `CanvasTexture` with `Linear`, no
mipmaps (`AreaArtTextDraw.cs`, ARC-UI-145, the owner's choice at scales 2.5 to 3.5). Nothing draws a pack frame with nearest at a fractional `s`.

**Measurement.** The GPU filters were reproduced on the pack's own frames (nearest: the texel under each pixel centre; bilinear without mipmaps;
trilinear over a 2× box mip chain) and compared with the area-averaged result at the same device size (premultiplied alpha). PSNR in dB, higher is
closer to the area result:

| Frame | 1280 × 800 (`s` 1.33) | 1920 × 1080 (1.8) | 2560 × 1440 (2.4) | 2560 × 1600 (2.67) | 3840 × 2160 (3.6) |
| --- | --- | --- | --- | --- | --- |
| inventory window art (442 × 400) N / B / T | 32.7 / 32.7 / 34.9 | 34.8 / 37.8 / 37.2 | 37.0 / 43.0 / 39.9 | 38.2 / 41.3 / 39.3 | 43.4 / 41.3 / 40.8 |
| options background (800 × 600) | 40.4 / 40.4 / 37.8 | 37.9 / 44.1 / 40.7 | 38.7 / 48.2 / 43.4 | 39.7 / 42.6 / 41.3 | 46.2 / 43.4 / 43.0 |
| dialogue window (410 × 107) | 28.4 / 28.4 / 31.3 | 28.6 / 31.4 / 32.0 | 30.9 / 35.2 / 35.3 | 34.4 / 36.3 / 33.7 | 40.8 / 37.5 / 36.8 |
| icon (64 × 64) | 29.0 / 29.0 / 30.4 | 32.6 / 35.6 / 34.4 | 31.9 / 33.4 / 33.3 | 34.5 / 37.4 / 36.0 | 33.9 / 34.8 / 34.7 |
| hero portrait | 29.7 / 29.7 / 30.3 | 32.2 / 35.8 / 34.8 | 32.1 / 34.1 / 33.7 | 34.6 / 38.4 / 36.3 | 33.7 / 34.7 / 34.6 |

N nearest, B bilinear without mipmaps, T trilinear (the viewer's filter for direct frames today).

For the glyphs the useful number is how much ink a glyph keeps against the area result (mean and worst deviation over all glyphs of a font):

| Font | 1280 × 800 | 1920 × 1080 | 2560 × 1440 | 2560 × 1600 | 3840 × 2160 |
| --- | --- | --- | --- | --- | --- |
| Garmond9 (223 glyphs), B (today) | ±12.2 %, worst 66 % | ±6.2 %, 29 % | ±3.3 %, 15 % | ±1.9 %, 9 % | ±5.4 %, 23 % |
| Garmond9, T | ±17.6 %, 63 % | ±7.0 %, 26 % | ±4.2 %, 31 % | ±3.6 %, 19 % | ±5.9 %, 25 % |
| Nick16 (223), B (today) | ±7.4 %, 45 % | ±4.5 %, 28 % | ±3.0 %, 25 % | ±1.3 %, 9 % | ±2.3 %, 19 % |
| Morph15 (221), B (today) | ±7.1 %, 44 % | ±4.5 %, 51 % | ±3.1 %, 24 % | ±1.2 %, 8 % | ±3.7 %, 92 % |

Findings:

1. At 1280 × 800 the scale is exactly 1/3 of the pack, so bilinear sampling lands on texel centres and equals nearest: thin strokes break
   (a Garmond9 glyph keeps 12 % more or less ink than it should on average, up to 66 %). Trilinear blurs instead (it is the worst for text).
   The glyphs need the area filter below `s` = 2 ([#2939](https://github.com/Simarglok/arcanum-steamwoven/issues/2939), which planned it for the
   kit's steps; the column already draws at fractional `s`, so the need is not limited to the steps).
2. Between `s` 1.8 and 2.67 bilinear beats trilinear for the direct frames by up to 5 dB: the mip blend over-blurs. The area result is the
   reference for both, and the viewer already produces it for the generated chrome's pictures, so the direct frames should take the same path,
   resampled once per scale and drawn 1:1 ([#3002](https://github.com/Simarglok/arcanum-steamwoven/issues/3002)).
3. At whole device pixels there is no temporal shimmer: the column places every frame at a rounded device position, so a frame's sampling
   phase does not change while the scale stands; the faults above are static.

**Where the rules change.** `hd-presentation-contract.md` needs no change (it has no interface filter rule). The interface rule lives in
[`hd-interface-art.md`](hd-interface-art.md) (the ARC-UI-112 note: trilinear below 4×) and in the fonts' rule 3 of
[`steamwoven-interface-fonts.md`](../ui/steamwoven-interface-fonts.md); [#3002](https://github.com/Simarglok/arcanum-steamwoven/issues/3002)
and [#2939](https://github.com/Simarglok/arcanum-steamwoven/issues/2939) update them with the code. The TRD's SWI-LAY-5 ("linear filtering from the
baked set" for the kit) stays.

The local composite for the owner (portrait, icon and an ammunition panel, and Garmond9 glyphs, at 1280 × 800 and 1920 × 1080; viewer filter,
nearest, bilinear and the area result side by side) was posted in the thread, not committed.

## Geometry (AC3)

The column draws the original layout at `s` with the window zone and the strips at the original offsets (ARC-UI-2890). Pack frames are
registered to their Legacy frames (U3 fit, [`hd-interface-art.md`](hd-interface-art.md)), so they fit the column exactly as they fitted the
whole-factor layout; the captures confirm the bottom strip's place at three sizes. **No pack entry is obsolete because of the layout:** the parts
that belonged to the full-width layout (the strip side tiles, the 9-slice side pieces, the side boards, the per-family full-middle backdrops)
were generated or painted at run time and were removed with the layout by [#2926](https://github.com/Simarglok/arcanum-steamwoven/issues/2926).

Obsolete entries: the 41 frames of 23 network-play arts (not drawn, out of scope); noted for the next pack build
([#2362](https://github.com/Simarglok/arcanum-steamwoven/issues/2362)). The widescreen plate below.

## Widescreen main-menu plate (AC4)

`main-menu-16x9.png` is an opaque 1664 × 936 plate whose middle 75 % registers with the 800 × 600 menu frame: 1.56 px per unit, well below the
pack's own 4× table scene (`singleplayerback`, 3200 × 2400). It backs the table-scene menus only with the HD option on and the generated interface
off; under any Steamwoven plan, the column included, the viewer does not draw it and the 4× art stands at `s` with black sides. Drawn in the column,
its middle would be magnified at every size from 1920 × 1080 up (× 1.15 at 1920 × 1080 up to × 2.31 at 3840 × 2160). Keep the painted sides as an exception, crop to
4:3, or drop: **the owner chose to drop it (option C, 2026-11-20, [#3003](https://github.com/Simarglok/arcanum-steamwoven/issues/3003))**; the support code is removed (ARC-UI-3003) and the whole-factor layout (generated interface off) draws the original's plate.

## Fonts, portraits, illustrations

- **Fonts:** see [Sampling](#sampling-ac2). Readable from `s` 2.4 up with today's filter; at 1280 × 800 and to a lesser degree at 1920 × 1080 the
  strokes are uneven until [#2939](https://github.com/Simarglok/arcanum-steamwoven/issues/2939).
- **Portraits:** all 126 are in the pack; drawn trilinear, they lose 1 to 4 dB against the area result (table above). Their place in the follower
  group at the window's left edge is [#2925](https://github.com/Simarglok/arcanum-steamwoven/issues/2925).
- **Illustrations:** the 63 schematic record pictures are direct frames inside the schematic window (as panels). Slides, world-map bitmaps and the
  splash have no pack entry kind ([#2952](https://github.com/Simarglok/arcanum-steamwoven/issues/2952)).

## Kit master scale: 3.6 or 4 px per unit

Input for the TRD (#2893, merged without it). The TRD authors the kit for 3840 × 2160: the composition 2880 × 2160, **3.6 px per unit**, baked to
the steps ×1, ×2/3, ×1/2 and ×1/3. The alternative is the pack's layout, **4 px per unit** (composition 3200 × 2400), so that kit and pack share
the format and tools.

Where each choice draws without filtering (the step equals `s`) and with which run-time factor otherwise (the smallest step not below `s`):

| Screen | `s` | 3.6: step, factor | 4: step, factor |
| --- | ---: | --- | --- |
| 1280 × 720 | 1.200 | 1.2, **exact** | 1.333, 0.900 |
| 1280 × 800 | 1.333 | 1.8, 0.741 | 1.333, **exact** |
| 1440 × 900, 1600 × 900 | 1.500 | 1.8, 0.833 | 2.0, 0.750 |
| 1920 × 1080, 2560 × 1080 | 1.800 | 1.8, **exact** | 2.0, 0.900 |
| 1920 × 1200 | 2.000 | 2.4, 0.833 | 2.0, **exact** |
| 2560 × 1440, 3440 × 1440 | 2.400 | 2.4, **exact** | 2.667, 0.900 |
| 2560 × 1600 | 2.667 | 3.6, 0.741 | 2.667, **exact** |
| 2880 × 1800 | 3.000 | 3.6, 0.833 | 4.0, 0.750 |
| 3024 × 1964 | 3.273 | 3.6, 0.909 | 4.0, 0.818 |
| 3456 × 2234 | 3.723 | 3.6, 1.034 (magnified) | 4.0, 0.931 |
| 3840 × 2160 | 3.600 | 3.6, **exact** | 4.0, 0.900 |
| 5120 × 2880 | 4.800 | 3.6, 1.333 (magnified) | 4.0, 1.200 (magnified) |

| | 3.6 px per unit (TRD) | 4 px per unit (pack) |
| --- | --- | --- |
| Exact screens | the 16:9 and 21:9 sizes: 720p, 1080p, 1440p, 2160p, 2560 × 1080, 3440 × 1440 | the 16:10 sizes 1280 × 800, 1920 × 1200, 2560 × 1600 |
| Unit grid | 1 unit = 3.6 px; lengths snap to the 6 px grid (≤ 3 master px, under one unit); the strip heights 147.6 / 572.4 are off the grid ([#2927](https://github.com/Simarglok/arcanum-steamwoven/issues/2927)'s open question) | 1 unit = 4 px, every original coordinate whole at ×1 and ×1/2; at ×2/3 and ×1/3 (8/3 and 4/3 px per unit) lengths snap to a 3-unit grid, coarser than the 6 px grid |
| Generation | 2880 × 2160 context screens | 3200 × 2400, 23 % more pixels per generation |
| Reusing a pack frame in the kit | one area resample by 0.9 (the same filter as `ResamplePicture`), offsets × 0.9 then snapped | the frame as it is |
| Above ×1 (5K) | magnified 1.333 (TRD open question) | magnified 1.2 |
| Cost of the choice now | none (the TRD, `kit.json`, the kit tools, the prompts and the Presentation layout already use 3.6) | rework: the TRD's tables and SWI-LAY rules, `kit.json` (765 parts), `build_kit.py`, `kit_check.py`, `assemble.py`, `prompt_library.py` and the seven context prompts, and the Presentation layout's `f = s / 3.6` (`SteamwovenColumn`, `SteamwovenLayout`, `SteamwovenGrid`, `SteamwovenControls`) |

**Recommendation: keep 3.6 px per unit.** It is the owner's stated target (art for 3840 × 2160), it draws the most common desktop sizes (16:9 and
21:9) without any resampling, it is already built into the kit, its tools and the layout, and it costs smaller generations. The pack does not need the
kit's scale to be useful: a pack frame reused as a kit reference or underlay is one area resample by 0.9, the filter the viewer already uses for the
generated chrome. The 4 px option's real gains (whole units at ×1, no strip-height question, 16:10 exact, more headroom at 5K) are smaller than its
rework and its filtering of every 16:9 screen. If the owner prefers the 16:10 sizes (MacBook panels at their native 2560 × 1600), 4 is the
alternative, with the rework listed above.

## Follow-ups

- [#3002](https://github.com/Simarglok/arcanum-steamwoven/issues/3002) direct pack frames: area resample once per scale.
- [#2939](https://github.com/Simarglok/arcanum-steamwoven/issues/2939) glyphs: area filter below `s` = 2 (measurements of this note added there).
- [#3003](https://github.com/Simarglok/arcanum-steamwoven/issues/3003) the widescreen main-menu plate: dropped (owner decision, ARC-UI-3003).
- [#2362](https://github.com/Simarglok/arcanum-steamwoven/issues/2362) next pack build: leave out the network-play arts.

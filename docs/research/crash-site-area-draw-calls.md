# Crash Site area draw calls (research, ARC-RENDER-009)

Research for cause F3 of the
[performance diagnosis](crash-site-area-viewer-performance.md): every visible
area sprite is its own Godot canvas item and its own draw call. It records the
measured cost, the data preconditions checked on the authenticated Crash Site
profile, and the options with a recommendation. It changes no code and is not a
renderer contract; the [nearest-sampling rule](../rendering/area-nearest-sampling.md)
stays the authority for pixels.

## Measured cost

Debug source run on the development Mac (M4 Pro, `gl_compatibility`), after the
F1/F2 fixes, scripted camera, isolated preferences:

| View | Visible sprites | Draw calls | Frame |
| --- | ---: | ---: | ---: |
| Idle, zoom 2 | 191 | 200 | 2.0 ms |
| Pan, zoom 1 | 757 | 763 | 11.0 ms |
| Pan, zoom 0.5 | 2,472 | 2,213 | 19.6 ms |
| Idle, zoom 0.25 | 10,941 | 10,958 | 33.2 ms |
| Fit (zoom 0.125) | about 32,000 | not measured | about 100 ms (extrapolated at 3 µs per call) |

Sprites are averaged over area recomputes and draw calls over all frames, so the
panning rows are not exact one-to-one matches. With no area update at all, zoom
0.25 costs about 3 µs per draw call (whole frame divided by draw calls; the
diagnosis's 4.6 µs was measured before the F1/F2 fixes). Each
`AreaDrawItem` has its own `ShaderMaterial`, index-row texture and uniforms
(`screen_start`, `screen_size`, `body_alpha`), so Godot cannot batch adjacent
items, and terrain is most of the scene: 36,864 of all 37,363 commands.

## Terrain preconditions (checked on original data)

A probe over the strict Crash Site snapshot and its 1,975 retained frames found:

- every terrain command (10,347 tiles, 26,517 facades) is drawn 1:1: its
  destination size equals its source crop size (a data finding);
- terrain frames have only fully transparent or fully opaque pixels (0 of
  5,977,920 terrain pixels have partial alpha; a data finding); no additive blend
  and alpha 255 hold by construction;
- 1,664 terrain commands are mirrored in X, none in Y;
- terrain phases 0 and 1 precede every body phase (2: 3, 3: 42, 4: 454), so
  terrain never interleaves with bodies;
- the terrain envelope is 15,358 × 7,680 world pixels. Pre-composited RGBA chunks
  that contain any terrain need 252 MiB (1,008 chunks of 256 px), 278 MiB (278 of
  512 px) or 312 MiB (78 of 1,024 px).

Only the 1:1 size and binary alpha are properties of this profile's data, so a
chunk builder must check all of them (1:1, binary source alpha, non-additive,
alpha 255, phase 0/1) for every terrain command and fail closed otherwise.

## Why pre-composited terrain is exact

For a 1:1 item at integer destination `d` and zoom `z`, the sampling rule selects
source index `floor((s + 1/2 - o) / z) - d` for screen pixel `s`, so every 1:1
item samples the same world texel `W = floor((s + 1/2 - o) / z)`, and the item owns
the pixel exactly when `W` lies in its destination. Mirroring is applied after that
selection. With only binary alpha and painter order, the screen pixel therefore
shows the last terrain command whose texel at `W` is opaque. A chunk texture
composed on the CPU in the same order, at 1:1, mirrored within each command's
source crop and replacing a chunk texel only where the source texel's alpha is
255, holds exactly that texel at `W`, and drawn as one 1:1 item it selects `W` by
the same rule. Adjacent chunks partition world texels, so chunk seams cannot add or drop a
pixel. Bodies keep their own items above the terrain.

At zoom `2^-k` (k ≥ 1) with an integer (pixel-snapped) origin, `W ≡ 2^(k-1)
(mod 2^k)` for every pixel, independent of the pan. A decimated chunk that keeps
only those texels, drawn as an item whose destination is `2^k` times its source
size, therefore selects exactly them at that zoom. Other zooms (the zoom is continuous) need the
full-resolution chunk.

## Options

| Option | Draw calls at zoom 0.25 | Memory | Exactness | Size of change |
| --- | --- | --- | --- | --- |
| A. Terrain chunks, 512 px, composed on demand for visible chunks, GPU LRU | up to about 90 terrain chunks at 1280×800 + bodies | 1 MiB per visible chunk at full resolution (1,024 px chunks would exceed the 8,192 px row cap at zoom 8) | exact by the argument above; seams and mirroring tested against per-item rendering | medium: chunk compositor, LRU, Fit path; bodies unchanged |
| B. A + decimated chunks for dyadic zooms ≤ 0.5 | same | 1/4 to 1/64 of A at 0.5 to 0.125 | exact only at dyadic zooms with integer origin | small on top of A |
| C. Batch all sprites (frame atlas, one material, per-instance data through MultiMesh, index rows in a row atlas) | a few | atlas of 24.7 MiB frames + rows | must re-prove the rule with per-instance integer data | large: rewrites the compositor and its native probes |
| D. Clamp the minimum zoom or limit Fit | unchanged per sprite | none | n/a | trivial, but changes the agreed camera policy |

## Recommendation

Implement A next, as its own PR. Then decide on B from measured Fit frames. A
replaces 36,864 terrain items with tens of chunk items wherever terrain dominates.
It keeps bodies, ordering, the actor and the shared index rows unchanged. It also
reuses the existing 1:1 sampling path for chunks, and its exactness can be tested
pixel for pixel against the current per-item renderer. C is only worth considering
if bodies ever dominate. D is a policy change for the owner.

Also in scope of F3/F6 and cheap once A exists:

- keep a bounded LRU of GPU frame textures instead of evicting every texture
  that leaves the view, which re-uploaded 900+ textures per 8 s pan at zoom 0.25
  in the same runs,
  keeping the resource smoke's eviction check by bounding the LRU;
- cap the idle frame rate (the same runs rendered about 500 FPS when idle).

## Implemented

Option A with the decimated levels of option B now draws the terrain
([viewer contract](../rendering/crash-site-area-viewer.md)). A CPU raster test
compares chunk drawing with per-command drawing under the exact rule, with zero and
non-zero source crops: full-resolution chunks at zooms 1, 2, 1.2651956 and 0.8 and at
fractional origins at zooms 1 and 0.5, decimated chunks at 0.5, 0.25 and 0.125 with
integer origins. The native owner fixture compares whole chunked and per-command
readbacks at 15 views (levels 0 to 3) and checks frozen pixels in both modes; the
seam fixture and the original-data smoke run in chunk mode, and the resource smoke
checks that the chunk cache stays within its caps and evicts during the sweep. The
measured result is in the [performance diagnosis](crash-site-area-viewer-performance.md#f3-result-terrain-chunks).

## Not established

- Fit frame time was not measured, only extrapolated.
- No chunk prototype was built and no GPU memory was measured.
- Original-runtime appearance remains NOT_VERIFIED.

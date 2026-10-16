# Generative HD re-detailing at 4× (ARC-RES-038)

Status: **research only.** This note tests diffusion re-detailing of original
sprites at 4× for the local HD pack of the [HD presentation contract](hd-presentation-contract.md)
and recommends a pipeline. It builds on the 2× upscaler comparison in
[hd-upscale-research.md](hd-upscale-research.md) (ARC-RES-033) and maps its result onto
the existing [HD pack and recipe format](../formats/hd-pack.md) (ARC-DATA-014); it adds
no format change, build tool, loader or renderer path. No generated, upscaled or original frame, crop,
sheet, GIF, training image, LoRA or model weight is in this repository or its PR:
everything stayed under the ignored `local/` directory on the owner's Mac. The owner
reviewed same-size comparison sheets in the project discussion; that review is not a
merge gate, and the human before/after review remains ARC-RENDER-015.

Owner decisions this note works under ([decisions](../architecture/decisions.md), HD pack;
project discussion, 2026-10-16):

- The owner rejected the ARC-RES-033 outputs (2× restoration upscalers and pixel-art
  scalers keep staircase edges and add no detail) and chose a **soft-edge 4× base**
  (Real-ESRGAN `realesrgan-x4plus` at native 4× on colour-bled RGB, alpha from the
  same network on the binary mask; the format's example
  [`hd-recipe-x4plus-4x-soft.json`](../formats/examples/hd-recipe-x4plus-4x-soft.json))
  as the provisional base, with
  this generative experiment as the main bet.
- **Acceptance: shading and form must survive.** On the soft 4× base the owner saw
  the heroine's back view lose the hip and garment shading that the original carries
  in dithered shadows. The control frame `hmfuwxaa` g1 f0 p0 is on every sheet.
- A **style LoRA is trained locally on original sprites**; training data and weights
  never leave the Mac and are never sent to an external service.
- Silhouette, anchor and frame size are locked: every output is exactly `4w × 4h`
  of its source frame, so the source hotspot times four stays the anchor.
- The legal footing of ARC-RES-033 applies unchanged: outputs and the LoRA are
  derivative of the original art, kept local and not distributed.

Evidence vocabulary as in ARC-RES-033: **local observation** = a bounded run on the
owner's unchanged installation; **source fact** = a cited licence or tool text;
**judgement** = an inference or a developer's look, stated as such.

## Inputs and harness

Local observation. Installed original as in ARC-RES-033 (`GAME_ROLES` identities of
[`tools/local_dev/launch.py`](../../tools/local_dev/launch.py)); machine Apple M4 Pro,
24 GiB unified memory, macOS 27, PyTorch 2.14.1 on the MPS backend.

- **Statics and control frame:** the ARC-RES-033 exports (strict Area load, exactly
  the RGBA the AREA viewer draws): `Chest_Fancy-1` g0 f0 (64×47), `tree` g0 f0
  (249×253), cave walls `RCvlfcU0` (51×148) and `Cv1lfcU0` (75×152), and the
  control frame, female underwear idle `hmfuwxaa` g1 f0 p0 (25×78).
- **Animated actor:** the same body's walk, `art\critter\hmf\hmfuwxab.ART` (8
  physical groups × 10 frames, raw 17 FPS; the WORLD actor profile's Walk), decoded
  with `ArtDecoder` at palette slot 0. The actor contract draws facings 0..7 from
  groups `[0,7,6,5,4,5,6,7]` with facings 1..3 mirrored, so only the five drawn
  groups 0, 4, 5, 6 and 7 (50 frames) were generated; a mirrored facing is an exact
  horizontal flip of its 4× frames.
- **LoRA training set:** 300 frames drawn by a fixed-seed sample over every ART name
  in the module, `Arcanum4.dat`, `arcanum3/2/1.dat` and `tig.dat` (first archive wins
  a name; a counting order, not the resource precedence): critters 70, monsters 30,
  unique NPCs 20, scenery 60, containers 20, walls 30, items 30, portals 15,
  interface 25; one random group/frame per ART at slot 0; frames under 32 px
  (interface: 128 px) or less than one sixth opaque were skipped. Every evaluation
  ART name above, and every `hmfuw*` / `hmmuw*` body ART, was **held out**. The
  holdout is by name only: the set contains other female and male bodies in other
  outfits (for example `hmflasfu`, `hmfv1xhu`) and other trees, so the LoRA's effect
  on the control frame and the tree is not a test on unseen content.
- **Harness (local, not committed):** a throwaway C# exporter referencing
  `Arcanum.Data`, and Python scripts (NumPy, Pillow, diffusers, peft) under
  `local/hd-gen/` of the worktree. The Real-ESRGAN binary and models are the
  ARC-RES-033 copies (hashes there).

## Methods

**4× bases** (all exactly `4w × 4h`):

| Id | RGB | Alpha |
| --- | --- | --- |
| nearest | pixel replication (Legacy identity) | Legacy nearest mask |
| lanczos | Lanczos of the colour-bled source | Lanczos of the binary mask, cut below 24 and above 232 |
| soft 4× base | `realesrgan-x4plus` at native 4× on the bled RGB | the same network on the binary mask, cut below 24 and above 232 (the owner's provisional base) |
| x4hard | as soft 4× | Legacy nearest mask (a less smoothing edge) |

Colour bleed = opaque colours spread 12 px into the transparent area by repeated
8-neighbour averaging, rest filled with the mean opaque colour.

**Shading lock** (new; this note's "luminance-detail transfer"). Iterative
back-projection on luminance: downsample the 4× result to source size by an
alpha-weighted 4×4 box, take the per-pixel Rec. 601 luma error against the
original on opaque source pixels, upsample that error bicubically (after bleeding it
into the transparent area) and add it to all three channels; four iterations. It
keeps the 4× result's own high-frequency detail and colour but forces its
low-frequency light and shadow back to the original's. It takes at most 0.3 s per
frame on the CPU (0.02 s for the control frame, 0.3 s for the tree).

**Smooth alpha** (new). The binary Legacy mask, nearest 4×, Gaussian blur with
σ = 3.2 output px, then a linear ramp `clip((b − 0.5) · 3 + 0.5)`. It removes the
4-px staircase of the nearest mask and of the x4plus mask and keeps the silhouette's
position: the opaque area changes by 0 to +2.1 % (tree), 63 isolated source pixels of
the tree fall below α 128, and no pixel with α > 0 lies outside one Legacy texel of
the Legacy silhouette (checked on the five statics at strength 0.5 and all 50 walk
frames). Its IoU against the Legacy
mask is 0.948–0.996 on the statics and 0.938–0.98 on the walk frames (16 of 50 below
0.95, minimum 0.938 at g4 f1). Every generative output uses it. An experiment that also
re-coloured the original's dark outline ring (pre-render anti-aliasing against
black: 26 % of the control frame's opaque pixels) with interior colours made thin
limbs visibly fatter and was dropped: on 25-px-wide characters the dark ring carries
the form (local observation, judgement).

**Generative re-detailing.** SDXL base 1.0 (fp16, `madebyollin/sdxl-vae-fp16-fix`
VAE) img2img with a ControlNet, DPM++ 2M Karras, 30 steps scaled by the strength,
CFG 5, seed 38, prompt `arcanum style, isometric pre-rendered game sprite, <subject>,
highly detailed, sharp focus, plain grey background` plus a fixed negative prompt.
The 4× init is enlarged with Lanczos so its long side is at least 704 px (at most
1280), centred on a 64-multiple canvas whose transparent area is colour-bled (no grey
fringe at soft edges), generated, cropped, reduced to exactly `4w × 4h` with Lanczos
and given the smooth alpha. Variants (`<init>-<control><strength>`):

| Id | Init | Control | Strength |
| --- | --- | --- | --- |
| soft-t35 / t50 / t65 / t80 | soft 4× base + shading lock | ControlNet Tile (xinsir) on the init, scale 0.9 | 0.35 / 0.50 / 0.65 / 0.80 |
| lanczos-t50 | Lanczos of the original | Tile on the init | 0.50 |
| nearest-t60 | nearest of the original | Tile on the Lanczos image | 0.60 |
| soft-u50-depth | soft 4× base + lock | ControlNet Union promax (xinsir): tile + depth, depth = blurred, contrast-stretched original luminance at 4× ("depth from luma") | 0.50 |

Each variant ran without and with the style LoRA (adapter weight 1.0), and each output was
also shading-locked (`+lock`).

**Style LoRA.** A minimal trainer (diffusers + peft) on MPS: rank 16, alpha 16,
attention projections `to_q/to_k/to_v/to_out.0` of the SDXL UNet (23.2 M
parameters), frozen UNet in bf16 with fp32 LoRA weights, gradient checkpointing,
AdamW lr 1e-4, weight decay 0.01, gradient clipping 1.0, 50 warm-up steps and a cosine
decay planned for 1,500 steps, min-SNR γ = 5, batch 1, seed 38, text
embeddings and VAE latents precomputed once. Training images are the soft 4× base
plus shading lock of each sampled frame, enlarged with Lanczos to a 700-px long side
(interface art: up to 1024) and composited on grey `#808080`, 64-multiple canvases
from 512 to 1024 px; captions name the kind (`arcanum style, isometric pre-rendered
game sprite, <human woman character | scenery object | wall segment | …>, plain grey
background`). Training on shading-locked bases is deliberate: the LoRA should learn
the original's light and materials, not the soft base's flattening.

**SUPIR** (`SUPIR-v0Q`, fp16, through ComfyUI and `kijai/ComfyUI-SUPIR` on the same
canvases) was installed but did not run: see "Not verified / open".

## Metrics

Computed on every output against the source frame:

- **IoU**: `α ≥ 128` of the output against the nearest Legacy mask.
- **Shading fidelity:** the output is downsampled to source size by the
  alpha-weighted 4×4 box. **Luma MAE** = mean absolute Rec. 601 luma difference from
  the original over opaque source pixels (0–255). **LC ratio** = mean 3×3 local
  luma standard deviation of the downsample over that of the original, on pixels
  one step inside the silhouette (1.0 = same local contrast; below 1 = flattened, the
  owner's complaint). **LC corr** = Pearson correlation of the two local-contrast
  maps (where the contrast is, not only how much).
- **Detail 4×**: RMS of the 4× luma minus the bicubic re-enlargement of its own
  downsample, inside the silhouette: high-frequency content above the source's
  Nyquist. Nearest scores high here because its 4-px blocks are high frequency;
  read it as "texture or blocks", not as quality.
- **Flicker** (animation): frames are placed on one canvas by their hotspots
  (`4 · hot` at 4×). **Flicker 1×** = mean over consecutive frame pairs (cyclic) of
  `|Δ luma of the downsampled output − Δ luma of the original|` on pixels opaque in
  both frames. **Flicker 4×** = the same at 4× against the Lanczos 4× replica of the
  original, which also sees high-frequency boiling that the 1× check averages away.
- **Time and memory:** wall time of the diffusion call per image, MPS driver
  allocation after the call (the Python process's own RSS stayed at 0.9–1.1 GiB; the
  weights live in MPS memory).

The shading lock drives luma MAE to ~0.1 and LC ratio to ~1.0 **by construction**,
so those two numbers prove only that the lock works; the pre-lock numbers are the
evidence about each generator, and the owner's look decides whether the locked
result is better.

## Results

### Statics and control frame (5 frames, mean)

Local observation. `+lora` = with the style LoRA; `+lock` = after the shading lock.

| Variant | IoU | Luma MAE | LC ratio | LC corr | Detail 4× | Control frame LC ratio | s / image |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| nearest (Legacy) | 1.000 | 0.00 | 1.000 | 1.000 | 7.5 | 1.000 | — |
| lanczos | 0.994 | 2.31 | 0.876 | 0.992 | 3.5 | 0.933 | — |
| soft 4× base | 0.987 | 7.52 | 0.841 | 0.958 | 9.1 | 0.953 | — |
| x4hard (soft 4× RGB, Legacy mask) | 1.000 | 7.52 | 0.841 | 0.958 | 9.1 | 0.954 | — |
| soft 4× base + lock | 0.987 | 0.15 | 0.997 | 1.000 | 9.1 | 0.999 | ≤ 0.3 (lock) |
| soft-t35 | 0.975 | 2.61 | 0.948 | 0.981 | 8.5 | 0.970 | 29 |
| soft-t35+lora | 0.975 | 2.73 | 0.979 | 0.978 | 8.8 | 0.980 | 24 |
| soft-t50 | 0.975 | 3.07 | 0.951 | 0.976 | 8.6 | 0.964 | 36 |
| soft-t50+lora | 0.975 | 3.19 | 1.028 | 0.970 | 9.3 | 0.981 | 41 |
| soft-t50+lora+lock | 0.975 | 0.10 | 0.998 | 1.000 | 9.3 | 0.999 | — |
| soft-t65+lora | 0.975 | 3.54 | 1.067 | 0.964 | 9.8 | 0.979 | 46 |
| soft-t80 | 0.975 | 3.84 | 0.938 | 0.965 | 9.3 | 0.938 | 58 |
| soft-t80+lora | 0.975 | 3.87 | 1.077 | 0.958 | 10.0 | 0.965 | 62 |
| soft-t80+lora+lock | 0.975 | 0.12 | 0.998 | 1.000 | 9.9 | 0.999 | — |
| lanczos-t50+lora | 0.975 | 3.54 | 0.934 | 0.972 | 4.1 | 0.928 | 42 |
| nearest-t60+lora | 0.975 | 5.47 | 1.023 | 0.924 | 8.5 | 0.970 | 47 |
| soft-u50-depth+lora | 0.975 | 9.51 | 1.289 | 0.911 | 8.8 | 1.012 | 41 |

The mean seconds include the tree (1088 × 1088 canvas, 110 s at strength 0.5, 19.0 GiB
MPS); the 512 × 768 and 768 × 576 canvases took 22–27 s at 11.9–12.0 GiB.

Findings:

1. **The soft 4× base flattens and shifts the shading.** On the four statics it
   loses 14–22 % of the local contrast (LC ratio 0.78–0.86; 0.841 over all five
   frames) and it shifts luminance most of the bases (MAE 7.5). The less smoothing
   edge (x4hard) changes only the silhouette, not these numbers: the flattening is in
   the network's RGB, not in the alpha. On the control frame
   the change is mostly a tone shift (MAE 8.6) with a 5 % contrast loss (0.953), on
   the walk frames 3.5 % (0.965): the hip shading the owner missed is lost more in
   tone than in contrast at source scale.
2. **The style LoRA counteracts flattening.** At every strength the LoRA raises the
   pre-lock LC ratio (t50: 0.951 → 1.028; t80: 0.938 → 1.077) and detail 4×, with similar
   luma MAE. Without it SDXL smooths toward a generic look (judgement from the
   sheets). On one cave wall, `RCvlfcU0`, it overshoots (LC ratio 1.14 at 0.5, 1.26
   at 0.65, 1.36 at 0.8: invented rock texture); the other wall, `Cv1lfcU0`, stays at
   1.05–1.07.
3. **Init matters more than control.** From Lanczos the result stays blurred (detail
   4.1); from nearest the 4-px blocks survive into the texture (judgement from the
   sheets) and form correlation drops (0.924). The soft base with shading lock is the
   best init.
4. **Depth-from-luma is the wrong constraint.** Union tile + depth moves luminance
   the most (MAE 9.5) and over-sharpens (LC ratio 1.29): treating brightness as
   depth relights the sprite.
5. **Shading lock + generation is the combination that meets the acceptance
   criterion.** Generation adds texture the base lacks (bark, rock, wood grain,
   leaf shapes; judgement from the sheets) and may drift in tone (the tree turns
   brighter and greener: MAE 6.2–6.8); the lock restores the original's light and
   shadow (MAE ≈ 0.1) without removing that texture (detail 4× unchanged by the lock).
   On the control frame the hip and garment shading of the original is visible again
   (owner's sheets).
6. **Silhouette.** IoU 0.975 against the Legacy mask is the smooth alpha's contour
   smoothing (thin limbs lose their stair corners); generation never changes alpha.
   The source's dark outline ring stays and still reads as a darker, partly stepped
   fringe on the control frame (judgement); the generator does not remove it at
   strength ≤ 0.8 with Tile control.

### Animated actor: walk, 5 drawn groups × 10 frames

Local observation. Strength 0.5, Tile control, style LoRA, same seed for every
frame. **Per frame** = one diffusion run per frame (512 × 768 canvas). **Sheet** = all
ten frames of a direction laid out 5 × 2 on one canvas and generated in one run, so
the frames share one denoising pass; at the default 704-px canvas (sheet A) each
frame gets only about half the working resolution of a per-frame run, so sheet B
enlarges the canvas to a 1280-px minimum long side (1280–1344 px, comparable
resolution per frame). Means over the five drawn groups:

| Variant | IoU | Luma MAE | LC ratio | Flicker 1× | Flicker 4× | s / frame |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| soft 4× base | 0.976 | 8.32 | 0.965 | 7.60 | 14.54 | — |
| soft 4× base + lock | 0.976 | 0.17 | 0.999 | 0.25 | 11.97 | — |
| per frame | 0.954 | 2.72 | 0.988 | 3.07 | 12.86 | 26.0 |
| per frame + lock | 0.954 | 0.06 | 1.000 | 0.09 | 12.55 | — |
| sheet A (704 px) | 0.954 | 6.24 | 1.000 | 8.44 | 14.06 | 4.0 |
| sheet A + lock | 0.954 | 0.17 | 0.999 | 0.26 | 11.00 | — |
| sheet B (1280 px) | 0.954 | 3.28 | 0.974 | 4.06 | 12.66 | 14.6 |
| sheet B + lock | 0.954 | 0.09 | 1.000 | 0.13 | 12.11 | — |

Findings:

1. **Generation does not add visible boiling over the base.** Flicker 4× of the
   locked generative walks (12.1–12.6) is within about 0.6 of the locked soft base
   (12.0), and below the unlocked soft base (14.5). Judgement from the frame strips:
   no texture change between consecutive frames beyond the pose; the owner's GIFs
   (original, soft base, soft base + lock, per frame, sheet B side by side) are the
   check that counts.
2. **The soft base itself flickers in tone** (flicker 1× 7.6: its luminance error
   changes from frame to frame); the shading lock removes that for every method
   (flicker 1× ≤ 0.26), because each frame is pinned to its own original.
3. **Sheet vs per frame.** Sheet B is 1.8× faster per frame (14.6 s vs 26.0 s) with
   slightly lower 4× flicker (12.11 vs 12.55) but needs 21.0–23.7 GiB of MPS memory,
   at the edge of this machine. Sheet A's low flicker comes with half the working
   resolution per frame and visibly less detail (judgement from the strips; its
   pre-lock tone error is also the largest, MAE 6.2), so it is not a fair winner. Flicker 4× rises with added detail, so it ranks methods with similar
   detail only.
4. **The original walk is itself uneven:** in group 6 the garment renders dark in
   frames f1–f4 (0-based) and white in the others (the source frames, not an artefact); every
   method keeps that.
5. Mirrored facings 1–3 are flips of groups 7–5, so the walk needs 5 × 10 = 50
   generated frames, not 80.

### LoRA training

Local observation. 300 images, 17 distinct captions; precompute (text embeddings and
VAE latents) 122 s; 2.4–3.1 s per step; MPS driver allocation 12.9 GiB rising to
14.2 GiB. The planned 1,500 steps were stopped at step 1,175 when the machine
started swapping (12 GiB of swap in use with the owner's other applications open,
step time above 10 s; MPS allocation 9.2 GiB at the start, 12.9 GiB by step 200); the **step-1,000 checkpoint** (93 MB of safetensors, ~50 min of
training) is the LoRA used everywhere above; because the cosine schedule was planned
for 1,500 steps, its learning rate had only decayed to about 3.3e-5 there. Loss stayed flat around 0.05–0.08
(min-SNR weighted), as usual for a style LoRA. For a pack build: cap canvases at
768 px and free the MPS cache between steps, or train with the owner's other
applications closed.

## Time and memory (M4 Pro, 24 GiB)

Local observation; one image at a time, fp16 SDXL, models already loaded.

| Step | Canvas | Time | Memory |
| --- | --- | ---: | --- |
| Soft 4× base (x4plus RGB + mask, two ncnn runs) | — | 1.5 s (control frame), 2.6 s (tree), one uncached run each | as ARC-RES-033 |
| Shading lock (4 iterations, CPU) | — | 0.02 s (control frame), 0.3 s (tree) | negligible |
| SDXL + Tile, strength 0.5, 30 × 0.5 steps | 512 × 768 / 768 × 576 | 22–27 s | 11.9–12.0 GiB MPS |
| same | 1088 × 1088 (tree) | 110 s | 19.0 GiB MPS |
| same, walk sheet of 10 frames | 1280–1344 × 960–1344 | 125–158 s (12.5–15.8 s per frame) | 21.0–23.7 GiB MPS |
| Strength 0.8 | as above | ~1.6× the 0.5 time | same |
| LoRA training, rank 16, 1,000 steps | 512–1024 | ~50 min + 2 min precompute | 12.9–14.2 GiB MPS |

Same-machine repeatability (one check, not kept as a result file): re-running the
control frame at strength 0.5 with the LoRA in a fresh process (34 s) gave a
byte-identical 4× result (seeded CPU generator, fixed
canvas). Other machines, PyTorch versions and the CUDA path were not tried.

**Scale (judgement from these rates).** The first pack's 59 distinct Crash Site
character and object frames are about 25–40 min. The whole game's 337,513 frames
(one palette per ART, ARC-RES-033 census) at 15–26 s per frame are roughly
1,400–2,400 hours, two to three months of continuous running on this Mac; generating
only the frame keys maps actually use, lower step counts, or a CUDA GPU would be
needed for a full pack.

## Recommendation

**Pipeline for characters and objects at 4× (replaces the plain soft 4× base as
the recommended HD method; base choice stays provisional until ARC-RENDER-015):**

1. Soft 4× base: `realesrgan-x4plus` on colour-bled RGB (as the owner chose).
2. **Shading lock** against the original (four back-projection iterations). This
   alone fixes the measured flattening and tone shift of the base (statics LC ratio
   0.84 → 1.00, characters 0.95–0.97 → 1.00, luma MAE 7.5 → 0.15) in at most 0.3 s
   per frame, so it should be part of the base even if generation is not used.
3. SDXL base 1.0 + ControlNet Tile img2img from that image, with the **local
   Arcanum style LoRA** at adapter weight 1.0, denoising strength **0.5** for
   characters and animated actors, 0.5–0.65 for objects and vegetation, at most 0.5
   for rock walls (one of the two walls overshot above that; a one-frame basis),
   long side ≥ 704 px, fixed seed and prompt per ART.
4. Lanczos to exactly `4w × 4h`, **shading lock again**, **smooth alpha** from the
   Legacy mask. Anchor = 4 × source hotspot; frame size and silhouette position
   unchanged.
5. Animated actors: one run per direction sheet when memory allows (sheet B), else
   per frame with one seed; generate only drawn groups and mirror the rest.

Not recommended: init from nearest or Lanczos, depth-from-luma control, SDXL
without the style LoRA, any result without the second shading lock, SUPIR
(non-commercial licence and not running on MPS here).

**Mapping onto the existing pack format** ([hd-pack.md](../formats/hd-pack.md),
ARC-DATA-014; judgement):

- `method.kind: "generative"` holds step 3: `model` and `modelFiles` = the SDXL base
  UNet and text encoders, the fp16-fix VAE and ControlNet Tile (hashes in the licence
  table); `adapters` = the local style LoRA `{name, sha256, weight: "1.0"}`; `seed`,
  `prompt`, `negativePrompt`; `parameters` labels such as `sampler`
  (`dpmpp2mKarras`), `steps` (`30`), `denoise` (`0.5`), `controlStrength` (`0.9`),
  `cfg` (`5`), `workLongSide` (`704`); `rgb: {edgeExtend: 12, fill: "mean"}`.
- What the format cannot say yet: the **two-stage** pipeline (a network base, then
  generation), the **shading lock** (iterations, luminance only) and the **smooth
  alpha** (σ, ramp). Today they could only be `parameters` labels, with the x4plus
  model file joining `modelFiles`; a later format task should decide whether a
  pipeline is one method with labels or a list of stages.
- `alpha: {mode: "soft"}` fits: no pixel of any generated frame lies outside one
  Legacy texel of the silhouette. With this smooth alpha `minIoUPermille` must be at
  most **935** (walk minimum 938‰); the format's generative example uses 950, which 16
  of the 50 walk frames and the tree (948‰) would fail with `AlphaMismatch`. A smaller σ (2.0 output px
  kept the control frame at 980‰ and the chest at 992‰) trades some staircase back
  for margin.
- **ARC-RENDER-015** (the existing ART-viewer pack review) is where Legacy, soft
  4× + lock and generative + lock should be compared in place, at magnification ≥ 2,
  including the walk.
- **Open defect to solve before a pack:** the original's dark outline ring still
  shows as a darker, partly stepped fringe around thin characters (see below).

## Licences

Source facts from each project's licence file or model card (checked 2026-10-16),
plus judgement where marked. None of these tools or weights is added to the
repository; the owner runs them locally. Not legal advice. SHA-256 values are the
Hugging Face blob ids of the files used.

| Component | Version / file | Licence | Use in this project |
| --- | --- | --- | --- |
| SDXL base 1.0 (`stabilityai/stable-diffusion-xl-base-1.0`) | commit `46216598`; UNet `unet/diffusion_pytorch_model.fp16.safetensors` `83e012a8…e139`; text encoders `660c6f5b…6cdd`, `ec310df2…b1f4` | CreativeML Open RAIL++-M (use-based restrictions, attachment A; outputs are the user's subject to them) | Local generation. Permits commercial use; the use restrictions travel with the weights and derivatives (such as a LoRA). |
| SDXL VAE fp16 fix (`madebyollin/sdxl-vae-fp16-fix`) | commit `207b116d`, `1b909373…ace2c` | MIT | Local decoding/encoding. |
| ControlNet Tile SDXL (`xinsir/controlnet-tile-sdxl-1.0`) | commit `1ae8d952`, `9f23ba7b…e011` | Apache-2.0 | Local control. |
| ControlNet Union SDXL promax (`xinsir/controlnet-union-sdxl-1.0`) | commit `801a4a3f`, `9fae2e50…7cdc` | Apache-2.0 | Local control (depth-from-luma experiment). |
| SUPIR-v0Q fp16 (`Kijai/SUPIR_pruned`, pruned from XPixel's SUPIR) | commit `eaabd8ec`, `3eef33ec…13ce` | SUPIR licence: **non-commercial use only**, commercial use needs written permission | Research only; not usable for a pack that might ever be commercial. |
| ComfyUI | commit `e087d8f6` | GPL-3.0 | Separate local program (SUPIR host); never linked into this MIT code. |
| ComfyUI-SUPIR (kijai) | commit `99d49e91` | SUPIR non-commercial licence (repository `LICENSE`) | As SUPIR. Two local patches (module name, conditioning device) stay in the ignored clone. |
| PyTorch 2.14.1, diffusers 0.41.0, transformers 5.20.1, peft 0.21.2, accelerate 1.15.0, huggingface_hub 1.33.0, safetensors 0.8.0, NumPy 2.4.6, Pillow 12.3.0 and their dependencies | pip wheels | permissive: PyTorch BSD-style with bundled permissive parts; diffusers, transformers, peft, accelerate, huggingface_hub Apache-2.0; NumPy BSD-3-Clause; Pillow MIT-CMU; safetensors Apache-2.0 (not re-read) | Local tooling. |
| Real-ESRGAN `realesrgan-x4plus` + ncnn-vulkan binary | as ARC-RES-033 | BSD-3-Clause / MIT | Soft 4× base. |
| **Style LoRA** (`local/hd-gen/out/lora/r16/step1000`) | trained here | Judgement: a derivative of the original art (trained only on it) and of SDXL (Open RAIL++-M restrictions apply); no project licence covers it | Local only, never distributed or uploaded, like the pack. |
| **Generated frames** | — | Derivative works of the original art (ARC-RES-033 legal note) | Local pack only. |

## Not verified / open

- **SUPIR did not run.** `SUPIR-v0Q` loaded in ComfyUI (after a local fix for the
  custom node's module name and pinning transformers below 5 in that separate
  environment), but its sampler failed twice with the same error on MPS (a module
  still on the CPU in fp16: `mixed dtype (CPU)`), also after moving the conditioning
  to the GPU. Per the owner's rule the attempt stopped there; whether to retry in
  fp32 (~16 GiB) or through the original CUDA-oriented repository is the owner's
  decision. Its licence excludes commercial use either way.
- **Flux was not tried** (owner-approved scope): FLUX.1-schnell is gated behind a
  Hugging Face licence acceptance under the owner's account, FLUX.1-dev and its
  ControlNet upscaler are non-commercial, and (judgement, not tried) training a Flux
  LoRA would not fit 24 GiB of unified memory.
- The dark outline ring of the original frames (anti-aliasing against black in the
  pre-render, 26 % of the control frame's opaque pixels) is kept; re-colouring it
  with interior colours made limbs fatter. A better treatment (thin dark rim at 4×,
  matting against black) is open.
- Shading fidelity is measured at source resolution; the shading lock satisfies it
  by construction, so the visual judgement of the 4× detail is the owner's.
- The style LoRA was trained once (rank 16, 1,000 steps, one caption scheme, seed
  38) on 300 frames; rank, steps, captions and training images (soft base vs
  others) were not swept. Its effect is measured only through LC ratio and detail 4×
  plus sheets.
- Sample: four statics, one control frame and one walk (one body, slot 0) of the
  Crash Site; terrain, facades, effects, translucent frames, other palettes and
  other critters were not run. Determinism was checked once on one machine.
- Time and memory are this M4 Pro's with the owner's usual applications open; the
  1,280-px sheets and the LoRA training reached the machine's memory limit.
- All raw outputs, sheets, GIFs, training images, the LoRA and the harness are only
  in the owner's ignored `local/hd-gen/`.

Provenance: frames from this repository's own decoder and Area code on the owner's
installation; models and tools used as external programs and libraries; no CE code
or structure is used.

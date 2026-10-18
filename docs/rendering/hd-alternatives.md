# HD alternatives for characters, objects and terrain (ARC-RES-053)

Status: **research only.** This note follows the [generative re-detailing](hd-generative-redetail.md)
(ARC-RES-038) and the owner's review of its Crash Site pack. It tests three things. The first is an edge fix
for sprites without diffusion. The second is stronger open image-editing models run as "redraw + fit". The
third is a terrain method that avoids per-tile generation. It then proposes a method per category.

It adds no pack format change, build tool, loader or renderer path. No original, generated or derived frame,
crop, sheet, GIF, model weight or harness file is in this repository or its PR. Everything stayed under the
ignored `local/` directories of the owner's two Macs. The owner judged the sheets and animations in the
project discussion. That review is not a merge gate; the human before/after review stays
[ARC-RENDER-015](terrain-viewer.md).

Evidence vocabulary as in ARC-RES-033/038:
- **local observation**: a bounded run on the owner's unchanged installation;
- **source fact**: a cited licence, model card or tool text;
- **judgement**: an inference or a developer's look, stated as such.

The owner's look decides; the numbers below prove artefacts, not "better".

## Owner decisions and verdicts this note works under

From the project discussion, 2026-10-17..18:

- **Target look.** The owner fed sheets to ChatGPT's image model. Its redraws (chest, tree, heroine) are the
  target look, not the method. They are redraws, not upscales: the chest silhouette moved (IoU about 0.91), the
  heroine gained invented content (a hood, then long hair), and the tree became a different tree (IoU about 0.61).
- **Upscalers are not enough.** ARC-RES-033 scalers and the ARC-RES-038 soft 4× base add no detail that is not
  in the original.
- **Terrain.** Per-tile SDXL + LoRA terrain is rejected: blurred, with hard diamond edges and fern-sized grass.
  The whole-map mosaic is rejected as well (about 10 h for Crash Site, large swap).
- **Z-Image-Turbo is rejected** ("do not try again").
- **FLUX.2 klein-4B** lightens skin, drops the swimsuit's back seams, invents lid planks and distorts the chest's far
  rim. Even so, "klein + soft lock" is the best local result so far, and the owner asked to keep tuning it.
- **Three fixes after the 8× review.** Fix 1, edge decontamination, is kept: the halo is gone. The model's own
  silhouette is preferred over the 8-px stairs of the Legacy mask at 8×. Fix 3 must be coarse tone matching
  only. Fix 2, de-matting the ring, must not touch dark materials.
- **Repeated parts.** Identical man-made parts (the chest's three bronze bands) must stay identical. This led to
  the repeated-part step below.
- **Machines.** Generation may also run on a second Mac (M1 Max, 32 GiB) set up by another thread. The brief
  had left it out of scope.

## Machines, inputs and harness

Local observation.

- **Main Mac:** Apple M4 Pro, 24 GiB unified memory, macOS 27.
- **Second Mac:** M1 Max, 32 GiB, its own installation of the game.
- **Software:** PyTorch 2.14.1 on MPS (bf16), diffusers 0.41.0, transformers 5.20.1, gguf 0.19.0.
- **Frames:** the ARC-RES-033 exports (strict Area load, palette slot 0) of `Chest_Fancy-1` g0 f0 (64×47), `tree`
  g0 f0 (249×253), cave walls `RCvlfcU0` (51×148) and `Cv1lfcU0`, and the heroine control frame `hmfuwxaa` g1 f0 p0
  (25×78).
- **Walk:** the body's walk `hmfuwxab`, the five drawn groups 0, 4, 5, 6, 7 × 10 frames. Facings 1..3 are
  mirrors, as in ARC-RES-038.
- **Reproduction on the second Mac.** It exported the frames from its own installation and got byte-identical
  PNGs for all five evaluation frames and the 80 walk frames (SHA-256 compared). It reproduced the klein chest
  to the reported precision: IoU 0.979, mean contour deviation 0.33 source px, repeated-part residual
  47.2 → 9.8, HD offset (57, 27).
- **Harness:** Python scripts under `local/hd-gen/lib`, plus throwaway C# exporters over `Arcanum.Data` and the
  terrain viewer core. Not committed.

## What the viewer zoom actually shows

Local observation from the owner's screenshots, plus the existing rules.

The viewer draws in physical pixels:
- a 1280×800 window fills 1280 pixels of a macOS screenshot, and macOS screenshots are saved at physical
  resolution (an Activity Monitor capture is 1624 px wide);
- the camera zoom therefore maps one original pixel to 2 device pixels at 200 % and 3 at 300 %. This is why the
  owner finds 100 % too small on a Retina screen.
- not verified: the viewer itself was not instrumented.

Consequences:

- **Target and scale.** The owner plays at 200–300 %, so that is the target view, and every sheet in this study
  shows rows at 2, 3 and 4 device px per original px. A **4× frame is already denser** than these views need. 8×
  pays off only at 400 % and above. An earlier sheet labelled "4 px = 200 % on Retina" was wrong and was
  corrected in the discussion.
- **The staircase is a sampling effect.** The "pixelization" the owner saw at 200–300 % comes from sampling, not
  from the HD size. [Area nearest sampling](area-nearest-sampling.md) and the
  [HD contract](hd-presentation-contract.md) require exact nearest sampling of the 4× texture, with no filtering,
  mipmaps or box downsample. At 200 % each screen pixel takes one of 2×2 HD texels; at 300 % it takes one of
  about 1.33² texels. The same frames box-filtered to the screen size lose the staircase on the comparison
  sheets (judgement). Removing it in the game needs a filtered HD minification rule: a contract and renderer
  change, proposed as a follow-up.

## A. Edge fix without diffusion

Local observation. Variants on the ARC-RES-038 soft 4× base (`realesrgan-x4plus` on colour-bled RGB, shading
lock, smooth alpha):

- **interior bleed**: colours spread only from pixels one step inside the Legacy mask;
- **de-matte**: ring colour treated as interior colour × α over black; α from the luma ratio, never above the
  Legacy-derived alpha;
- **half de-matte + 1.25-px contour**;
- a **low-frequency colour lock** (chroma error blurred over one source px) next to the luma lock.

Metrics:
- **dark-fringe share**: silhouette-band pixels more than 40 luma below the nearest interior colour, as a share
  of the band; **fringe width** in HD px;
- **opaque-area change** against Legacy;
- **low-frequency colour error**: mean |RGB| of the blurred 4×4-box downsample against the blurred original.

| Sample | Variant | IoU | Fringe share | Fringe width | Opaque area | LF colour err. | Detail 4× |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Heroine | base (ARC-RES-038) | 0.957 | 0.71 | 4.6 | +0.8 % | 3.0 | 13.0 |
| Heroine | interior bleed | 0.957 | 0.50 | 3.3 | +0.8 % | 5.5 | 11.6 |
| Heroine | de-matte + colour lock | 0.822 | 0.01 | 1.5 | −14.5 % | 14.8 | 10.8 |
| Heroine | half de-matte + lock + contour | 0.951 | 0.06 | 1.3 | −0.4 % | 14.0 | 10.8 |
| Chest | base | 0.984 | 0.04 | 3.0 | +0.1 % | 1.5 | 15.3 |
| Chest | de-matte + colour lock | 0.978 | 0.00 | 1.0 | −0.7 % | 0.9 | 14.1 |
| Tree | base | 0.948 | 0.08 | 3.8 | +2.1 % | 2.8 | 8.9 |
| Tree | de-matte + colour lock | 0.938 | 0.00 | 1.2 | +0.6 % | 2.3 | 6.1 |
| Cave wall | interior bleed + colour lock | 0.996 | 0.01 | 1.9 | 0.0 % | 0.1 | 3.3 |

Walk, five groups (per group, edge = interior bleed + colour lock):
- **frame-to-frame flicker** (the ARC-RES-038 measure; 1× = at source scale): 0.42–0.51 at 1× against 0.28–0.38
  for the base, and 11.5–14.3 at 4×;
- minimum IoU 0.938–0.952;
- fringe share 0.41–0.51 against 0.61–0.73 for the base.

Findings:
- **The double contour.** ARC-RES-038's dark double contour comes from the bled dark ring (hypothesis
  confirmed). Interior bleed narrows it without changing the area, which is right for characters. Full de-matte
  removes it on objects at no area cost. On the thin heroine it eats 14.5 % of the silhouette.
- **Detail.** None of the A variants adds detail. The owner judged all of them short of the target look.

## B. Stronger open models: redraw + fit

Method (local observation), as the brief's "redraw + fit".

**Redraw.**
- The edge-fixed 4× base is placed on mid grey (128) on a canvas whose long side is 1024 px, a multiple of 16.
- The edit model gets it with a per-object text hint, for example "a slim young woman with tanned skin seen from
  behind, long straight black hair falling below the shoulders, white strapless one-piece swimsuit with seams on
  the back, bare arms and legs, barefoot", and "keep the exact pose, proportions, silhouette, colours and lighting;
  change nothing else".

**Fit.**
- The output is registered back to the frame by a scale and offset search. The search maximises the IoU of the
  redraw's own foreground (difference from its background) against the Legacy mask, plus 0.25 × the normalised
  luma correlation.
- **IoU before mask** is that foreground against the Legacy mask, before any alpha is imposed.

**Finish (current, "v3", 4× output).**
- the redraw's **own silhouette**, limited to one source pixel outside the Legacy mask, smoothed over 0.75 HD px;
- **edge decontamination**: colours outside the redraw's eroded foreground are pulled in from its interior;
- **coarse tone match**: luma and chroma error blurred over 3 source px, weighted by interior pixels, towards the
  original with its dark ring de-matted, so ring pixels are excluded from the target;
- the **deviation** from the Legacy contour in source px: mean and maximum distance of mismatched pixels, lost and
  added area.

Earlier finishes (superseded, kept for the record):
- **Per-pixel luma + colour lock (ARC-RES-038 style).** It imports the original's dither and dark ring back into
  the redraw. On the owner's 200 % comparison it was the main reason local results looked muddy next to
  ChatGPT; the raw klein redraw was close to the ChatGPT look before the lock (judgement).
- **Soft lock**: RGB error blurred over 2.5 source px.
- **8× output.** Withdrawn after the zoom finding above.

### Models

Licence facts are under [Licences](#licences).

| Model | Run here | Result |
| --- | --- | --- |
| FLUX.2 klein-4B | 4 steps (distilled), bf16, 1024 canvas | best local result; see below |
| Z-Image-Turbo | img2img strength 0.5, 9 steps, guidance 0, prompt embeddings computed first (two-phase load) | rejected |
| Qwen-Image-Edit-2511 | transformer GGUF Q4_0 (unsloth), text encoder bf16 in a separate phase, 20 steps, true CFG 4 | most faithful; very slow here |

**Z-Image-Turbo** (local observation, owner verdict).
- **Detail fell:** chest 14.1 → 7.4, heroine 11.6 → 9.1.
- **Heroine:** flat and cartoon-like, the hair a black blob.
- **Chest:** blurred, with a different ornament.
- **Tree:** IoU 0.81; another tree.
- **Cave wall:** registration failed (IoU 0.18).
- 50–172 s per image. Rejected; not run on the walk.

**FLUX.2 klein-4B** (local observation).

| Sample | IoU before mask | Detail 4× (A base → klein) | Time | MPS peak |
| --- | --- | --- | --- | --- |
| Chest | 0.978 | 14.1 → 26.8 | 99–170 s | 22–23 GiB |
| Tree | 0.909 | 6.1 → 21.3 | 190 s | 26 GiB |
| Heroine (back) | 0.903–0.917 | 11.6 → 12–13 (v3) | 56–92 s | 18 GiB |
| Cave wall | 0.70 (drew masonry) | 3.2 → 15.0 | 104 s | 19 GiB |

Parameter sweep on the heroine and chest: canvas 1024 vs 1536, steps 4 vs 8, soft lock 1.5 vs 2.5.
- **Canvas 1536** drifts proportions (heroine IoU 0.877) and costs 1.4–3× the time.
- **8 steps** add nothing (detail 13.6 against 13.4).
- **Canvas 512 at 4× output:** heroine worse (hair, a zip on the back), chest equal; 36 s against 56 s.
- **v3 finish:**
  - halo band luma deviation 42 → 5 on the heroine and 46 → 6 on the chest;
  - soft band width 5.1 → 1.2 HD px (8×) on the heroine;
  - heroine contour deviation mean 0.37 source px, maximum 2.4 (inwards), 6.5 % of the area lost and 2.9 %
    added (IoU 0.909);
  - chest deviation 0.33, IoU 0.979.
- **De-matting the ring before generation**, restricted to light interior materials:
  - **heroine:** it still lightened and thinned the hair tips over the shoulder, because their inner neighbour is
    skin;
  - **chest:** it gave a smoother lid.
  - Kept for man-made objects only.
- **Semantic errors** (owner): lighter skin, missing back seams, invented lid planks, the chest's far rim
  distorted. With the corrected hint the long hair is right; the first hint said "short hair" and was wrong.

**Qwen-Image-Edit-2511** (local observation; chest only so far).
- **Fidelity:** IoU 0.975, contour deviation 0.39 source px.
- **Bands:** more uniform than klein's before any repeat step (residual 30.1 against 47.3).
- **Details it kept:** the original's front gold swags became chains, and the lock and proportions stayed.
- **Drawbacks:** softer than klein (detail 18.3 against 27.7), and it still adds plank lines on the lid.
- **Cost:**
  - 57 min for one image: 20 steps at about 170 s, MPS about 28 GiB with swap on the 24-GiB Mac;
  - the text encoder phase took 82–90 s per image at 16.5 GiB;
  - too slow on this Mac for any volume.
- A few-step LoRA exists (below) but was not run.

### Animation

Local observation; the owner calls animation the part that really matters. Per-frame redraws do not survive
motion.

| Run | Flicker 1× | Flicker 4× | IoU | Time per frame |
| --- | --- | --- | --- | --- |
| A edge fix (no generation) | 0.42–0.51 | 11.5–14.3 | ≥ 0.938 | — |
| klein, per frame, per-pixel lock (group 6) | 1.5 | 17.3 | ≥ 0.83 before mask | 123 s |
| klein, one 5×2 sheet per group (group 6) | 1.1 | 10.0 | ≥ 0.76 before mask | 20 s |
| klein, per frame, soft lock (group 6) | 25.3 | 31.9 | ≥ 0.83 before mask | — |
| klein, per frame, 8× fixes 1–3 (5 groups) | 18.2–22.8 | 21.2–26.9 | 0.83–0.86 (8×) | 62 s |
| klein v3, per frame vs with frame 0 as reference (5 groups) | pending | pending | pending | ~29 s / ~55 s |

What changes between frames:
- face, hair and swimsuit highlights change from frame to frame;
- group 4 (front) changes most.

The sheet mode holds better, but it loses detail and IoU. The per-pixel lock hides the flicker only by
re-imposing the original's look.

## Repeated-part consistency (man-made objects)

Local observation on the chest (owner's method).

1. **Repeats in the source.**
   - Translation offsets are searched where the sprite matches itself inside a bronze mask (colour rule).
   - The shortest offset whose double also matches is (14, 7) source px, exactly the 2:1 isometric axis. The
     double (28, 14) matches equally (21.6–21.7 mean |RGB| against 28.3 for random bronze pairs). This is
     consistent with the assumed orthographic camera: the bands are translated copies.
   - The match is noisy because the original's ornament itself is dithered.
2. **HD alignment and transfer.**
   - The HD offset is refined to (57, 27) HD px, which is 14.25 × 6.75 source px: a sub-pixel period that 1×
     cannot show.
   - Instances are indexed along the repeat direction. Where the source says it is the same part (blurred colours
     within 22), the middle, fully visible band is copied onto the others with a 2-HD-px feathered weight. The end
     rim, handles and lock keep the generation.
   - A per-pixel median of the three bands was tried first. It averages three different ornaments into noise and
     was dropped.
3. **Metric:** the residual between aligned instances, mean |RGB| over 4,596 HD pixel pairs.

| Run | Residual before → after | Transferred share of bronze |
| --- | --- | --- |
| klein v3 chest | 47.3 → 9.8 | 31 % |
| Qwen chest | 30.1 → 7.4 | 31 % |

What is left:
- the left edge of the outer band, where the middle band has no counterpart, keeps the model's own drawing;
- the bronze mask is a colour rule tuned on this chest;
- planks, rivets, windows, grilles, wheels and facades need their own material masks (not run).

## D. Terrain without per-tile generation

Local observation on the 3×3 exterior chunks around the start.

**Material detail by world coordinates.**
- Tile names encode the materials: `XXXbse` is the base of material XXX, `AAABBB` a transition from A to B (grs,
  drt, dg1, rck, sw1–sw3, swr, tdt, cf1, blk).
- The x4plus base gets a low-frequency light and colour lock (σ 1.5 source px).
- One seamless detail texture per material: SDXL Tile + LoRA with circular padding, strength 0.6, from a mosaic of
  the material's own base tiles, then high-passed.
- The detail is added in world coordinates through soft masks. On transition pixels A and B are split by colour
  distance to each material's mean base colour. Facades (mountains, wreck) get no detail.
- Cost: about 73 s per 9 chunks, plus a one-time texture per material.

Metrics:
- **seam ratio**: mean |∇luma| in a ±8 HD px band around tile and chunk borders, divided by the mean elsewhere;
- **border line ratio**: the same on the 2–3 HD px border line itself;
- **hard edge**: p99 of |∇| of the σ = 2 HD px blurred luma inside transition tiles, relative to Legacy (Lanczos
  4×);
- **texture scale**: spectral centroid of the high-pass luma on base tiles, relative to Legacy at 1×; below 1
  means a coarser texture.

| Variant | Seam ratio | Border line ratio | Hard edge | Texture scale | Detail 4× |
| --- | --- | --- | --- | --- | --- |
| Legacy (Lanczos 4×) | 1.040 | 1.069 | 1.00 | — | 6.0 |
| x4plus + lock (pack today) | 1.037 | 1.065 | 1.17 | 1.24 | 9.4 |
| Per-tile SDXL + LoRA (rejected) | 0.934 | 1.083 | 0.87 | 0.85 | 5.7 |
| Material detail, gain 1 | 1.012 | 1.022 | 0.76 | 1.02 | 6.3 |
| Material detail, gain 2.5 | 0.998 | 0.998 | 0.83 | 1.23 | 8.1 |

Per-tile generation:
- blurs inside tiles (seam ratio below 1);
- keeps a sharper line exactly on the tile border (1.083);
- is coarser than the original (0.85), the owner's "fern leaves".

Material detail at gain 2.5:
- shows no border signature;
- keeps soft transitions;
- adds fine-scale texture.

The context fallback (each tile generated in its 8-neighbour context) was not run.

**Large painted backdrops (Quintarra and similar facades):** note only. They need a separate task: reassemble
the backdrop, run tiled diffusion with overlap, cut it back into frames.

## C. Method per category (recommendation)

Judgement from the numbers and the owner's verdicts; the owner's look decides.

- **Characters and animation.**
  - **No generative method is ready.** Every redraw tested flickers when each frame is drawn alone: 1× flicker of
    18–25 against 0.4–0.5 for the A edge fix, once the per-pixel lock is gone.
  - Until a consistent method exists, the pack keeps the A edge fix (interior bleed + colour lock) for animated
    bodies.
  - Next candidates:
    - klein with frame 0 as a reference image (being measured on the second Mac);
    - Qwen-Edit with a reference;
    - a reference-guided edit from an owner-approved HD exemplar per body.
- **Hard-surface, man-made objects** (chests, furniture, machines).
  - Method: klein v3 redraw, de-matted ring on light materials, then the repeated-part step for any repeats.
  - Qwen-Edit is more faithful where speed allows: second Mac, few-step LoRA.
  - Invented plank lines remain a semantic error; such frames should carry both versions (generation and A) in
    the pack for the ARC-RENDER-015 review.
- **Natural objects** (trees, bushes, rocks): klein v3. The tree gains the most (detail 6.1 → 21.3, IoU 0.909),
  but its leaves and branches are the model's. Keep both versions for review.
- **Walls and cave rock.**
  - A or the ARC-RES-038 generation. klein drew masonry on the cave wall (IoU 0.70), so do not use edit models
    there without a material-specific hint test.
- **Terrain:** material detail by world coordinates (gain about 2.5) over the x4plus + lock base; no per-tile or
  whole-map generation.
- **Viewing:** propose filtered HD minification for zooms below 4× (contract and renderer task) before judging
  HD at 200–300 % in the game; 4× stays the pack scale.

## Time and memory

Local observation.

**Main Mac (M4 Pro, 24 GiB).**
- klein draws one 1024 canvas in 56–190 s at 18–26 GiB MPS. Release the MPS cache after every image:
  `gc.collect()` + `torch.mps.empty_cache()`. Without it a 50-frame walk grew to 30 GiB and swap reached 24 GiB.
- Qwen-Edit Q4_0 takes 57 min per image with swap; Z-Image-Turbo 50–172 s.
- The A edge fix and all finishes run in seconds on the CPU.

**Second Mac (M1 Max, 32 GiB).**
- klein chest 81 s, 22.9 GiB, no swap reported;
- model load 20 s, finish 17 s.

## Licences

Source facts from the model cards' licence fields on the Hugging Face Hub (checked 2026-10-18) and the tools'
licence files; judgement where marked. None of these tools or weights is added to the repository; the owner runs
them locally. Not legal advice.

| Component | Revision / file | Licence | Use here |
| --- | --- | --- | --- |
| FLUX.2 klein-4B (`black-forest-labs/FLUX.2-klein-4B`) | `e7b7dc27`, diffusers parts | Apache-2.0 (card) | Local redraw. FLUX.2 dev and klein-9B are non-commercial and were **excluded**. |
| Z-Image-Turbo (`Tongyi-MAI/Z-Image-Turbo`) | `f332072a` | Apache-2.0 (card) | Tried, rejected. |
| Qwen-Image-Edit-2511 (`Qwen/Qwen-Image-Edit-2511`) | `6f3ccc0b`, text encoder, VAE, configs | Apache-2.0 (card) | Local redraw (chest). |
| Qwen-Image-Edit-2511 GGUF (`unsloth/Qwen-Image-Edit-2511-GGUF`) | `0d33d969`, `qwen-image-edit-2511-Q4_0.gguf` | Apache-2.0 (card) | 4-bit transformer. |
| Qwen-Image-Edit-2511 Lightning (`lightx2v/Qwen-Image-Edit-2511-Lightning`) | `107d238d` | Apache-2.0 (card field; card text not read) | Not run; candidate for speed. |
| SDXL base 1.0, SDXL VAE fix, ControlNet Tile, style LoRA | as ARC-RES-038 | as ARC-RES-038 | Terrain material textures (D). |
| Real-ESRGAN `realesrgan-x4plus` + ncnn-vulkan | as ARC-RES-033 | BSD-3-Clause / MIT | 4× base. Copied to the second Mac with the owner's approval. |
| PyTorch, diffusers, transformers, accelerate, peft, huggingface_hub, gguf 0.19.0, SciPy 1.17.1, NumPy, Pillow | pip wheels | permissive (gguf MIT, SciPy BSD-3-Clause, others as ARC-RES-038) | Local tooling. |
| Generated frames, textures, sheets | — | Derivative works of the original art (ARC-RES-033 legal note) | Local only, never distributed. |

## Not verified / open

- **Animation consistency is unsolved.** The klein reference-frame walk and the Qwen heroine run on the second
  Mac; their numbers are not in this note yet.
- **The zoom-to-physical-pixel mapping** is inferred from screenshots, not measured in the viewer.
- **LoRA training for klein-4B or Qwen-Edit was not attempted.** Judgement: a klein-4B LoRA with the text encoder
  precomputed might fit 24 GiB; Qwen-Edit (20B) will not train on either Mac.
- The Qwen few-step LoRA, Qwen on the heroine and the walk, and Qwen with a reference were not run here.
- **The repeated-part step is shown on one object** with one colour-rule material mask. Its generalisation to
  planks, rivets, windows, grilles, wheels and facades is a proposal.
- **The own-silhouette alpha fails the pack's soft alpha check for characters.** The heroine's IoU is 0.90–0.92
  against `minIoUPermille` 937 (the ARC-RES-038 walk minimum was 937). Packing such frames needs a lower
  threshold for redraws or a model that keeps the silhouette.
- The terrain context fallback was not run. Terrain metrics are proxies on 9 chunks of one map.
- Seeds are fixed. MPS output is not bit-exact across M1 and M4; the second Mac matched the chest to the
  reported precision only.
- All outputs, sheets, GIFs, weights and the harness are only in the owners' ignored `local/` directories.

Provenance: frames from this repository's own decoder and Area code on the owner's installations; models and
tools used as external programs and libraries; no CE source used.

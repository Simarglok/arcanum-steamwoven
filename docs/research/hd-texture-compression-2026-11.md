# HD interface textures: VRAM compression study (ARC-DEV-382, 2026-11-11)

Question (item O9 of the [performance diagnosis](performance-diagnosis-2026-11.md)): should the optimized texture cache
([optimized textures](../workflow/hd-pack.md#optimized-textures-arc-dev-376)) hold VRAM-compressed textures instead of the lossless RGBA8
mip chain, to save the memory, the file read and the upload of a 4× interface frame?

**Owner decision (2026-11-11): keep the lossless chain.** The study is recorded here so it is not repeated and so the methods are at hand if the
question comes back (for example when memory, not time, becomes the limit). Nothing in the game uses compressed textures.

## Method

A throwaway standalone Godot 4.7 project (GDScript, `gl_compatibility` like the viewer, run through `./arcanum run --kind window`; not in git) read real
PNGs of the owner's installed interface pack and, for each frame:

1. built the mip chain with `Image.GenerateMipmaps` (what the cache stores today) and uploaded it as an `ImageTexture` (3 uploads averaged);
2. compressed a copy of the chain with `Image.Compress` as S3TC (BC3) and as BPTC (BC7), timed the encode, uploaded it, and asked the texture for its
   image to see whether the renderer kept it compressed (`Image.IsCompressed`);
3. compressed a mip-less copy, decompressed it, and compared level 0 with the source on every 7th byte: PSNR over RGBA and the largest per-channel and
   alpha differences (the alpha rule of the pack, `HdAlphaCheck`, is strict, so alpha error matters most);
4. cut the 190 × 150 texel window with the most edge energy out of each variant and drew them side by side, labelled, at about 2× nearest into one PNG
   (posted in the project thread; images are never committed).

Frames: a 940 × 1300 character-sheet panel, the 3200 × 636 bottom HUD bar, a 360 × 224 inventory icon, a 44 × 76 bitmap-font glyph (coverage, `masked`)
and a 168 × 168 indicator. Machine: Apple M4 Pro, Metal 4.1 (GL), shared and loaded, so absolute times are only a guide.

## Results

| Frame | Lossless chain | BC3 size / PSNR / max alpha diff / encode | BC7 size / PSNR / max alpha diff / encode |
|---|---|---|---|
| panel 940 × 1300 | 6363 KiB | 1595 KiB / 40.3 dB / 12 / 0.11 s | 1595 KiB / 48.3 dB / 2 / 1.8 s |
| HUD bar 3200 × 636 | 10597 KiB | 2654 KiB / 39.7 dB / 12 / 0.01 s | 2654 KiB / 47.4 dB / 2 / 3.1 s |
| icon 360 × 224 | 419 KiB | 52 KiB / 38.3 dB / 0 / 0.01 s | 105 KiB / 46.1 dB / 0 / 0.14 s |
| font glyph 44 × 76 | 17 KiB | 4 KiB / 33.7 dB / 0 / 0.006 s | 4 KiB / 41.0 dB / 0 / 0.002 s |
| indicator 168 × 168 | 146 KiB | 37 KiB / 45.0 dB / 12 / 0.006 s | 37 KiB / 51.7 dB / 2 / 0.03 s |

- Both formats are accepted by the renderer as compressed textures (`gl_compatibility` on Metal). ASTC is not: its encode/decode with a mip chain failed
  and the GL upload path refused the image, so it was not studied further. ETC2 is not meant for this desktop GL path.
- Memory, VRAM and the cache file shrink to about a quarter (BC3 on a frame with a plain alpha channel to about an eighth).
- **Upload time does not improve.** A lossless chain uploads in 0.2–3 ms per frame on this machine, a BC7 chain in 0.3–16 ms. The win would be memory and
  file read, not the first-draw hitch. The first-draw cost that was measurable was the alpha pass, which ARC-DEV-382 removes without any loss
  ([remembered alpha passes](../workflow/hd-pack.md#optimized-textures-arc-dev-376)).
- BC3 is visibly softer on edges and on small glyphs/icons (PSNR 34–45 dB, alpha off by up to 12). BC7 is close to the source (41–52 dB, alpha within 2).
  BC7 encodes slowly (seconds for a 3200-wide frame), fine for a background conversion that runs once per PNG, not for a draw path.

## If this comes back: methods that fit

- **Where:** only in the device cache (`HdOptimizedCache`, a new entry format next to format 1), never in the pack, so the pack stays the PNG source and
  portable. The compressed chain is made by `Image.Compress(CompressMode.Bptc)` after `GenerateMipmaps`, in the existing two-thread below-normal converter.
- **The alpha rule cannot run on compressed pixels** (a lossy alpha would fail a rule meant for exact edges). Run it on the lossless PNG once, record the
  pass in the remembered-alpha list (ARC-DEV-382), and bind the compressed texture only for a frame with a recorded pass; a frame without one takes the
  lossless path and records it.
- **Which frames:** large panels and backdrops first (the quarter-size saving is in megabytes there); keep small frames, bitmap-font coverage glyphs and
  indicators lossless (the saving is kilobytes and the error is most visible on thin edges).
- **Format choice:** BC7, not BC3. Fall back to lossless when the renderer does not report the format compressed after upload.
- **Acceptance:** the labelled before/after composite above reproduced on the real pack with the viewer's own conversion, an owner look, and the
  bar/HUD smokes with the pack. A change that alters what is drawn needs the owner's go before it is enabled (owner rule 2026-11-11).

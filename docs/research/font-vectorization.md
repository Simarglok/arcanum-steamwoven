# Vectorizing the original bitmap fonts for the HD pack

Status: **research, evidence level "local observation" plus "judgement"** (2026-11-10), work item ARC-RES-100. Docs-only: no source
change, nothing derived from the game in git, no repository checks run (the measurements below were scratch runs on the owner's
Mac). It authorizes nothing: the slices in section 7 are proposals and take their codes from the coordinator.

Requirement groups touched (reference only, no coverage row changes): FR-PRODUCT-004, FR-PRODUCT-007 (Legacy and HD show one world;
later picture improvements have their own bounds), FR-UI-005 (messages, tooltips, floating text), FR-UI-007 (adaptive composition).

The question: can the interface's bitmap fonts (ART 27 `morph15font`, 229 `Flare12Font`, 171 `Cloister18Font`, 327 `Morph30Font`, and
the others) be turned into vector outlines so that text stays sharp in the HD interface, what tools do it, and what of this needs an
image generator (the Codex route used for the interface redraws, ARC-RES-095) and what does not.

## 1. Answer

- **Feasible, and it does not need an image generator.** The glyphs are 4-bit anti-aliased coverage masks, so the job is a deterministic
  trace (upsample, back-project, threshold, fit curves), not a redraw. It ran in pure Python in about 70 seconds for all 28 text fonts
  (section 4). Claude can build, run and test it on the Mac end to end; Codex is not needed.
- **Quality is bounded by the source, not by the tool.** A glyph is 7 to 38 rows high with 16 grey levels. On synthetic ground truth
  (section 5, one test font family at 18 and 30 px) the traced outline matches the true outline with IoU 0.87 to 0.94 for an unhinted
  source (0.79 to 0.86 if the source was grid-fitted) and 0.72 to 0.82 at 10 to 16 px. Edge error at 4× is two to four times smaller
  than with nearest or bicubic upscaling (1.3 to 1.9 times for a hinted source). Fonts of 6 to 9 px and 1-pixel-stroke fonts (Garamond
  6/8/9, Courier 10, Pork 12) recover poorly; they are cheap to leave bitmap, but several reading windows use them (section 7).
- **Recommended path: render the outlines offline into 4× glyph frames of the HD pack** (same frame numbers, same integer advances, so
  layout is the original's), not a runtime font. It needs a small pack/viewer step (section 6), and the owner's eyes on three windows.
- **At exactly 4× a vector is not sharper than a back-projected, thresholded 8× raster.** Both give the same edge error (section 5). The
  vector's gain is size and scale freedom (2,000 to 10,000 curve nodes per font; any scale later; an OTF if a runtime font is ever wanted), not extra
  fidelity at 4×.
- **Everything stays local.** The outlines are derived from the player's own game data, so they follow the HD pack rule (local, never in
  git, never distributed). The original typefaces look like commercial designs; a licence review of derived outlines was not done.

## 2. What the fonts are

Facts from this repository's decoder, the ledger rows MW3, MW4, MW10, MW14, MW31 and a census of the extracted frames (local
observation, 33 fonts, 7,425 frames):

- A font is an ART file of 225 frames; character *c* (31..255) is frame *c* − 31 ([MW4](ledger/ui-audio.md)). A text's advance is the
  frame header's centre x, so layout is integer pixels and kerning-free.
- The draw reads the glyph pixel's **red level as coverage** `a` and mixes `screen + (colour − screen) × a / 256` (MW31; palette index 0
  is skipped). Only fonts with flag 0x80 or 0x100 (the Morph30 menu's brown pass) use the glyph's colour, so every font drawn by the
  viewer's windows is a grey mask.
- The masks have **16 levels** (0, 16, 32, 48, 68, 85, … 255): 15 to 17 distinct red values in 24 of the 28 glyph fonts (`casablanca16font`,
  `euph30font` and `zurich16font` show 22 to 60 values in the census, from pixels not examined). `newtimes16font` (ART 840, the world map's font)
  has only 3 (70, 113, 255).
- Each text font has one coloured frame (frame 0, character 31, in the five fonts checked) and a few control-character frames that are
  one pixel wide with a single faint pixel (seven in Cloister). They carry no glyph.
- Five of the 33 fonts are **not text**: `bookimagesfont` (186 rows), `newsiconsfont` (81 rows), `icons17font`, `icons32font` and
  `mpiconsfont` hold coloured pictures (190 to 243 distinct levels, or icons). They belong to the picture pipeline of ARC-RES-095, not to
  vectorization.
- The viewer draws every glyph (`AreaArtTextDraw`) as a texture in the glyph's Legacy-size rectangle at integer positions, on canvases
  whose texture filter is set to nearest (the windows' views set `TextureFilter = Nearest`); at a UI scale above 1 the text is blocky.
  The HD interface study ([hd-interface-art](../rendering/hd-interface-art.md), ARC-RES-094; its slice kind is ARC-DEV-242 in
  [hd-pack](../formats/hd-pack.md#interface-art-arc-dev-242)) lists fonts as out of scope, and the RES-095 groups left them as
  `Z3_fonts-not-redrawn` at 1×.

Sizes the viewer actually uses (constants in `tools/terrain-viewer/Core`): ART 27 (19 rows), 229 (15), 171 (18), 327 (33), 840 (21).
The document fonts the reading windows pick by markup (ledger WR5..WR11: 480, 482, 484, 485, 497 and others) are among the other 23 text fonts.

## 3. Method of the measurements

All scratch work, nothing kept in git. Tools run as external programs and libraries: Python 3.14 in a throw-away venv with numpy,
scipy, scikit-image, Pillow, fontTools, potracer. Licences: numpy, scipy, scikit-image BSD; Pillow MIT-CMU; fontTools MIT; **potracer
GPLv2+** (a port of Potrace). Run here as an offline tool; that its output is not covered by the licence is a judgement, not a legal
opinion, and a repository tool depending on it (slice 1) would pull a GPL library into an MIT repository, as a pinned external
dependency like Unicorn in [toolchain](../workflow/toolchain.md) (that one is LGPL), never vendored. No source reconstruction
used; frames were read from the extracted 1× PNGs of the owner's installation (RES-095 `Z3_fonts-not-redrawn`).

Pipeline per glyph (`T` = tracing helper, `ibp` = back-projection):

1. Coverage `c = red / 255` (0 where index 0), padded by 3 px.
2. Cubic upsample to 8× (pixel-centre aligned), then 6 rounds of **iterative back-projection**: `f ← f + up(c − down(f))`, so the 8×
   field reproduces the 1× masks when box-reduced.
3. Binarize. A plain 0.5 level is the best fixed level (a sweep of 0.40 to 0.55 on arts 27, 229, 171 has its minimum at 0.50: the
   masks are linear area coverage, not gamma-shifted). Thin strokes never reach 0.5, so the recommended form picks, per glyph, the level
   ≤ 0.5 at which the traced area equals the mask's total coverage (**mass-preserving threshold**).
4. Potrace-style curve fit (`turdsize` 24 at 8×, `alphamax` 1.0, optimizing curves, tolerance 0.2); even-odd fill.
5. Rasterize at 16× supersampling and box-reduce to 1× or 4×.

Metrics: *round trip* compares the outline's 1× coverage with the original mask over the pixels either one touches; *ground truth* (section
5) rasterizes a known TrueType outline at 16× (the truth), box-reduces it to a 16-level 1× mask (the "game bitmap"), traces that, and
compares at 4×.

## 4. Real fonts: round trip and cost

Round trip on the extracted game frames, plain 0.5 level (mean absolute coverage error `MAE`, 1.0 = full coverage; `>2lv` = share of ink-touching
pixels whose error exceeds two grey levels; one grey level is 0.0625, so a perfect fit still lands near 0.016 by quantization alone):

| Font (ART) | Rows | Nodes per glyph | MAE | >2lv |
| --- | --- | --- | --- | --- |
| `morph15font` (27) | 19 | 25 | 0.082 | 26 % |
| `flare12font` (229) | 15 | 21 | 0.071 | 20 % |
| `cloister18font` (171) | 18 | 32 | 0.082 | 26 % |
| `morph30font` (327) | 33 | 45 | 0.057 | 15 % |
| `newtimes16font` (840) | 21 | 32 | 0.077 | 23 % |
| `swiss921font` (best) / `garmond6font` (worst) | 37 / 7 | 32 / 10 | 0.034 / 0.129 | 10 % / 50 % |

- **Cost.** The 28 text fonts (6,200 glyphs) took about 70 seconds in total (67 s); the slowest fonts (Georgia 30, Morph30) 5 to 6 seconds each. A font is 2,000 to 10,000
  curve nodes. The five key fonts rendered to 4× PNG glyph frames came to 100 to 220 KB each, so all 28 fonts are about 4 MB (local
  estimate from the five; not a pack-size measurement).
- **Ink conservation.** The median traced/original ink ratio is 0.95 to 1.02 for 24 of the 28 fonts and 0.84 to 0.95 for Garamond 6/8/9
  and Pork 12. With a plain 0.5 level (the table above), small and thin-stroke fonts lose ink: 117 of 223 glyphs in Garamond 6 and 99 in
  Pork 12 fall below 85 % of the original ink, and a few glyphs vanish (dots, commas: scratch run on six fonts, 3 in Garamond 6 and Pork
  12, 1 in Garamond 8 and Elga 12; the per-font table also shows an ink minimum of 0.0 for Casablanca 16 and Pepper 20, not examined).
  The mass-preserving level fixes it on the six fonts it was run on (Garamond 6: 117 → 6 glyphs below 85 %, none vanish; Elga 12: 34 →
  0; Morph15: 16 → 0). It did not raise the error on any of the six (Pork 12 MAE 0.120 → 0.111; Garamond 6 0.129 → 0.120; Morph15 unchanged, Cloister
  within 0.001); the other 22 fonts were not run with it.
- **A per-component level over-inks** (median ratio 1.07 to 1.10 on the same six, scratch run), so it is not recommended.
- The seven "zero-ink" frames left in Cloister are the placeholders (characters 127, 129, 141, 143, 144, 157, 160), not glyphs.
- **Seen by eye** (arts 27, 229 and 171, sample line "The Quick Brown Fox…" at 4×, nearest against bicubic against traced): the traced
  text is clearly crisper than both, with straight stems and round bowls and no visible change of weight. The demo placed glyphs with
  width + 1 instead of the header's advances, so the few overlaps in Cloister are the demo's, not the trace's.

## 5. Ground truth: what a trace can and cannot recover

Macintosh system TrueType fonts (Georgia, Arial, Times New Roman, Courier New: designs close to those the game's names suggest) were
rasterized to 16-level 1× masks as test input only; no font file or glyph from them is kept or used in the product. "Unhinted" is a
box-reduced large render; "hinted" is FreeType's 1× grid-fitted render (the game's bitmaps are closer to unhinted: stems are not
snapped to whole pixels: a stem of about 1.7 px appears in `flare12font`; judgement from one glyph). Mean over 64 glyphs (A–Z, a–z,
0–9, & ?). IoU of the shape at 0.5 coverage, at 4×, against the true outline; coverage MAE over the canvas:

| Test font and size | Input | IoU nearest | IoU bicubic | IoU trace | MAE nearest / bicubic | MAE trace |
| --- | --- | --- | --- | --- | --- | --- |
| Georgia 12 | unhinted | 0.59 | 0.74 | 0.74 | 0.0145 / 0.0147 | 0.0075 |
| Georgia 18 | unhinted | 0.68 | 0.86 | 0.87 | 0.0139 / 0.0131 | 0.0044 |
| Georgia 30 | unhinted | 0.80 | 0.94 | 0.94 | 0.0093 / 0.0090 | 0.0022 |
| Georgia 12 | hinted | 0.48 | 0.57 | 0.57 | 0.0199 / 0.0207 | 0.0151 |
| Georgia 18 | hinted | 0.66 | 0.79 | 0.79 | 0.0146 / 0.0144 | 0.0077 |
| Georgia 30 | hinted | 0.77 | 0.85 | 0.86 | 0.0102 / 0.0103 | 0.0054 |
| Arial 12 | unhinted | 0.66 | 0.82 | 0.81 | 0.0134 / 0.0137 | 0.0053 |
| Arial 12 | hinted | 0.62 | 0.73 | 0.73 | 0.0147 / 0.0154 | 0.0084 |
| Arial 10 | unhinted | 0.58 | 0.72 | 0.72 | 0.0154 / 0.0155 | 0.0078 |
| Times New Roman 16 | unhinted | 0.63 | 0.79 | 0.80 | 0.0123 / 0.0119 | 0.0050 |
| Times New Roman 16 | hinted | 0.59 | 0.69 | 0.68 | 0.0142 / 0.0145 | 0.0096 |
| Courier New 10 | unhinted | 0.19 | 0.14 | 0.29 | 0.0115 / 0.0126 | 0.0095 |

(Trace = back-projected, potrace-style. Arial 10 hinted and Courier 10 hinted are in the scratch run and behave like their unhinted rows.
A thresholded 8× raster **with** the same back-projection reached the same coverage MAE within 0.0006 in all 14 conditions and an IoU within 0.025 of the trace's in all of them but Courier 10
unhinted, where the raster is better (0.32 against 0.29). The same raster **without** back-projection
had the same MAE but lost the thin strokes (Courier 10 unhinted IoU 0.15).)

- **The trace is as good as the source allows, and no better.** In the Georgia rows at 18 and 30 px the shape matches the true outline
  to IoU 0.87 to 0.94 (unhinted); no other family was run at those sizes. At 10 to 16 px a pixel of source is a tenth of the glyph
  height and a fifth to a quarter of the shape is simply not in the data (IoU 0.72 to 0.82).
- **The gain over a bicubic upscale is crispness, not geometry**: IoU is equal, but edge coverage error falls two to four times
  (unhinted; 1.3 to 1.9 times hinted). Over nearest the geometry also improves (IoU +0.14 to +0.19 unhinted, +0.09 to +0.13 hinted).
- **Hinting hurts.** A grid-fitted source loses 0.07 to 0.17 IoU against the same font unhinted (Georgia 12: 0.74 → 0.57; the other
  rows lose 0.07 to 0.12; snapped stems are not where the outline was). If the game's bitmaps were hinted, the trace faithfully reproduces the hinted shape at 4×: the glyph is the original's, not
  the typeface designer's.
- **One-pixel strokes (Courier 10) fail**: a stroke of about 0.8 px peaks below 0.5 coverage; only back-projection keeps part of it. The
  small fonts need a stroke-aware step (offset the outline to the measured width) or stay bitmap.

## 6. Options

| Option | What it gives | Cost and risk | Verdict |
| --- | --- | --- | --- |
| **A. Offline trace to 4× glyph frames in the HD pack** | Sharp text at the pack's 4× with the original layout; no renderer change beyond reading the frames; scale limited to about 4× like the other HD art | The HD glyph frame must hold red = green = blue = coverage with alpha 255 on ink pixels and 0 elsewhere (alpha below 255 at an edge would scale the coverage twice on the premultiplied path, ARC-UI-114). The interface slice's `legacy`/`legacySmooth` modes pin the alpha to the Legacy silhouette ([hd-pack](../formats/hd-pack.md#interface-art-arc-dev-242)), but the entry-level `"alpha": "masked"` (ARC-UI-113) allows any HD alpha within one Legacy texel of an opaque one, which may already fit a traced glyph; to be checked, together with an importer plan kind or `lock: false` item, before a pack format change is claimed. The viewer's font face must read HD frames. Drawing a 4× glyph into its Legacy-size rectangle at a UI scale below 4 samples the texture down, and the text views set a nearest filter: without mipmaps or per-scale frames the text may look worse than the 1× text; check scale 1 too | **Recommended** |
| B. Outlines to OTF, drawn by Godot `FontFile` (MSDF or a rasterized size) | Any UI scale, sub-pixel positions, fonts in one file | Layout must be re-done: advances, the tallest-glyph line height, the three-pass menu blend (MW5), floater fades, premultiplied draw; unhinted result differs from every ledger row that uses glyph metrics | Later, only if arbitrary scale is wanted |
| C. Back-projected, thresholded 8× raster instead of curves | Same 4× quality as A (section 5) with a tenth of the code | No scale freedom, no OTF; frames only; needs the back-projection step for thin strokes | Acceptable fallback for A |
| D. Neural upscale (Real-ESRGAN class, ARC-RES-033) | None needed | Blurs masks, mixes grey levels, cannot guarantee coverage = the mask | Reject |
| E. Free lookalike fonts (Gelasio for Georgia, Arimo for Arial, Cousine for Courier, EB Garamond…) | Committable, any scale, extra glyphs | Not the original look; breaks "original behaviour, no switches" for text | Only as a localisation fallback, never as the default |
| F. The same outlines from the player's own system fonts | Exact shapes | Licence, and the game's typefaces are not guaranteed to be those | Reject |

## 7. Proposed slices

Each is a proposal; none starts until the owner agrees, and codes come from the coordinator.

1. **Font tracing tool and its test** (type DEV or RENDER, Claude, Mac): `tools/hd_font` producing 4× coverage frames from the player's
   installation (it reads the DAT, not extracted PNGs; the extraction used here was a convenience), the mass-preserving level, a pinned
   requirements lock with hashes, a synthetic-fixture test (a font built from drawn shapes in the test, not a game font), `--dry-run`. First
   output: art 27, 229, 171, 327, 840 (they are the viewer's fonts). Art 840 has only 3 grey levels, so the 16-level results of section 5
   do not cover it; judge its trace separately.
2. **Pack and viewer support for coverage frames** (type RENDER/UI, Claude, Mac): first try the existing `masked` alpha and an importer
   item with `lock: false`; only if that fails, a new alpha mode or a `font` flag on the interface slice (the owner's call, a pack format
   change). `AreaArtFontFace` reading HD glyphs with the Coverage blend and a sampling choice for scales below 4 (mipmaps or per-scale
   frames); `docs/formats/hd-pack.md` and `docs/rendering/hd-interface-art.md` updated.
3. **Owner check in the game** (the owner): main menu (327), options (27, 229), character sheet (27, 229, 171), dialog box and floaters
   (229), world map (840), save/load (27), pre-made browser (27, 171); compare with the 1× text at UI scales 1, 1.5, 2, 3, 4.
4. **The remaining 23 document and small fonts** only if the owner accepts the first look. The 6 to 9 px fonts (Garamond 6/8/9, Courier
   10, Pork 12) stay bitmap or get the stroke-aware step first; note that the reading windows use some of them (ledger WR6..WR8: 480
   Courier 10 for the telegram, 482 Garamond 8 three times in the newspaper masthead, 497 Pepper 20 for the note), so telegram and
   newspaper text would stay at 1× until then.
5. **Optional later: runtime outlines (option B)** if arbitrary scale is wanted; a separate design task.

Not covered by these slices and needing outside help: new glyphs for other alphabets (the fonts hold Latin-1 only), hand-tuning outlines
in a vector editor (none is installed), and any legal opinion on derived typeface outlines.

## 8. Limits of this research

- Round-trip numbers use the extracted 1× PNGs, not a fresh decode; the decoder's red level is what the PNGs hold.
- Ground truth is synthetic (system fonts), a stand-in for the game's unknown rasterizer. The game's fonts may be hinted differently.
- Visual judgement covered three fonts at one sample line; no in-game screenshot at a real UI scale was made, and no pack was built.
- Pack size is estimated from five fonts. Cross-machine reproducibility of the trace was not tested (the algorithm is deterministic;
  library versions were not pinned for this run).
- Advance widths were not compared; the layout argument rests on the ledger (MW4), not on a measurement.

Provenance: frames decoded from the owner's installation with this repository's own decoder; tools used as external programs and
libraries; no source reconstruction used; no original or derived pixel, outline or font file is in git.

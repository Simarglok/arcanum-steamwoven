# HD font tracing: the player's interface fonts as 4× coverage frames (ARC-DEV-355)

Slice 1 of the font vectorization research (ARC-RES-100 (deleted; its findings are at the end of "Method and limits"), section 7). The tool
[`tools/hd_font`](../../tools/hd_font/hd_font.py) reads the interface bitmap fonts from the **player's own installation**, traces every
glyph's coverage mask into an outline and renders it back as a 4× coverage frame. It writes **local files only**; it does not build
or touch an HD pack. Slice 2 (ARC-DEV-361) puts the frames into the pack and the viewer draws text from them: [HD pack workflow](hd-pack.md#traced-fonts-in-the-pack-arc-dev-361). It needed no pack format change (the existing `masked` interface entries take the frames).

## Hard rules

1. **No original or derived data in git.** The frames, `font.json` and `receipt.json` hold glyph shapes and metrics of the player's fonts.
   The tool accepts an `--out` only under `<checkout>/local/hd-font/` (a checkout holds `.git`, `local/` is ignored by git), after
   resolving links, so the frames cannot land in git, an HD pack, its staging or backup folders, `artifacts/` or the game folder. The
   tests draw their own font; no game byte and no system font is in the repository.
2. **Read-only on the installation.** Nothing writes into the game folder, the installed HD pack or the iCloud copy.
3. **Own folders only.** A font folder is written only when it is empty, absent, or holds this tool's `font.json`, `receipt.json` only when absent or the tool's own, and a symbolic link in the way is refused. A venv is replaced only when it carries this tool's marker.
4. **`--dry-run` first.** It reads the sources and fonts, prints what would be written and writes nothing; it needs no dependencies.

## Commands

All from the checkout root. A real run installs the locked dependencies into `local/hd-font/venv` on first use (`ARCANUM_HD_FONT_VENV`
overrides it); a worktree has its own empty `local/`, so give `--out` the main checkout's `local/hd-font/...` path to keep the frames there.

```sh
python3 -B tools/hd_font/hd_font.py trace --out local/hd-font/out --dry-run     # sources, fonts, planned files
python3 -B tools/hd_font/hd_font.py trace --out local/hd-font/out               # arts 27, 229, 171, 327, 840 (under a minute)
python3 -B tools/hd_font/hd_font.py trace --out local/hd-font/out --art 27,229  # other interface ART numbers: any font in interface.mes
python3 -B tools/hd_font/hd_font.py trace --out local/hd-font/out-x --alpha-max 0.3 --level-cap 0.45   # a variant to compare (ARC-UI-145), below
python3 -B tools/hd_font/hd_font.py self-test                                   # synthetic-fixture tests, inside the locked venv
```

The game folder is `$ARCANUM_GAME_DIR` or `/Applications/Arcanum` (`--game-dir`); `--module` names the module whose archive and patches are
mounted (default `Arcanum`). The sources are searched in the original's order ([content precedence](../research/content-precedence.md)):
module folder, patches, module archive, `data/`, `arcanum*.dat` last to first, game folder. An interface ART number becomes a file
through `art/interface/interface.mes`, as the viewer does.

## What is written

`<out>/<art>-<name>/frame-NNN.png` (one per original frame, frame *n* is character *n* + 31, size = 4 × the original frame), `font.json`
(per frame: the original width, height, centre x/y, delta x/y, whether the frame is grey, nodes, threshold level, ink and traced ink,
round-trip error; the art file's SHA-256 and a hash over all PNGs) and `<out>/receipt.json` (tool version, lock hash, parameters).
The PNG rule of the research (option A): red = green = blue = coverage, alpha 255 on every ink pixel and 0 elsewhere, no premultiplication.
The advances stay the original's integers in `font.json`; layout is not changed. A traced outline may reach a little past the frame
rectangle; the part outside is clipped. Frames without ink (the control-character placeholders) are written transparent, so the frame
count and numbering stay the original's. Frame 0 (character 31) is coloured in the fonts checked; it carries no glyph and is traced from
its red level like the rest.

## Method and limits

The method is the research's section 3: pad, cubic upsample to 8×, 6 rounds of iterative back-projection, the **mass-preserving
level** (the level at or below the cap, **0.42**, at which the binary area equals the mask's total coverage), Potrace curve fit (`turdsize` 24,
`alphamax` **0.6**, optimizing curves, tolerance 0.2), even-odd fill, rasterized at 16× and box-reduced to 4×. It is deterministic.

**Sharper defaults (ARC-UI-145, tool version 2).** The first traces (ARC-DEV-355) used `alphamax` 1.0 and a cap of 0.5. The owner reported the HD text as too
smooth (2026-11-11) and chose, from a labelled on-screen comparison of seven variants (character sheet and tooltip at 2560 × 1315: current, `alphamax` 0.6 and 0.3,
`alphamax` 0.6 with cap 0.42, bilinear and nearest sampling without mipmaps, and the original), the trace `alphamax` 0.6 with cap 0.42 **and** bilinear sampling
without mipmaps in the viewer ([HD pack workflow](hd-pack.md#traced-fonts-in-the-pack-arc-dev-361)). On the screen the sampling filter made most of the difference
(the share of soft edge pixels in the text fell from 0.375 to 0.30), the `alphamax` change alone almost none (0.379), the cap a little (0.343). `--alpha-max`
(0 to 1.3334), `--level-cap` (0.05 to 0.95) and `--opt-tolerance` set the parameters for a comparison run; the receipt records them. The cap is not
free: below the mass-preserving level the glyphs get bolder, about 11 % more ink in area over the five fonts (median traced ink 1.107 of the mask's, against 0.998
at cap 0.5) and a larger round-trip error (0.079 against 0.059 mean), a local observation of 2026-11-11. A cap of 0.5 gives back the first behaviour.

- Dependencies: numpy, scipy, Pillow and **potracer** (GPLv2+, a port of Potrace), pinned with wheel hashes in
  [`requirements.lock`](../../tools/hd_font/requirements.lock) (macOS wheels, CPython 3.12 to 3.14). potracer is an external tool
  dependency installed into a private venv, never vendored and never linked by a C# assembly, like Unicorn in
  [toolchain](toolchain.md#function-oracle). That the traced output of the player's own fonts is not covered by its licence is a
  judgement, not a legal opinion.
- Quality is bounded by the source: fonts of 6 to 9 px and one-pixel strokes recover poorly (research, section 5), and the tool does not
  yet have the stroke-aware step; art 840 has only three grey levels, so its trace is judged on its own. Frames whose ink is under a
  tenth of a pixel (placeholders such as Cloister's seven) may trace to nothing; `font.json` lists `vanished` frames above that mass.
- `interface.mes` is read in its simple `{number}{text}` form (first message of a number wins).
- Pack and viewer support for the coverage frames is ARC-DEV-361 (`hd_pack.py fonts-plan`, importer `coverage` items, `AreaArtFontFace`); the owner's look at the result is slice 3.
- Not done here (later slices): runtime outlines. Nine more fonts (28, 230, 301, 371, 483 new, 27, 229, 171, 327 replaced) were generated by Codex and installed by ARC-UI-1202 ([pack workflow](hd-pack.md#traced-fonts-in-the-pack-arc-dev-361)); fonts 300, 370 (icons) and 48 (spell picture) stay Legacy; the other fonts of the 23 are not reviewed yet.
- **Research findings (ARC-RES-100, the research note is deleted).** The job is a deterministic trace, not a redraw: all 28 text fonts trace in about 70 s
  of pure Python. On synthetic ground truth the traced outline matches the true one with IoU 0.87-0.94 for an unhinted source (0.79-0.86 if grid-fitted)
  and 0.72-0.82 at 10-16 px; edge error at 4x is two to four times below nearest or bicubic upscaling. At exactly 4x a vector is not sharper than a
  back-projected, thresholded 8x raster; its gain is size and scale freedom. A stroke under one pixel is quantized to whole 8x rows before the curve
  fit. The original typefaces look like commercial designs and a licence review of derived outlines was not
  done, so the outlines stay local like the rest of the pack. Not covered: other alphabets (the fonts hold Latin-1 only); runtime outlines (option B) are an optional later design.
- **Sharper text (ARC-UI-145, the task note is deleted).** The owner found the HD text of ARC-DEV-361 too smooth. Seven variants were drawn in the real viewer
  (local readbacks of the character sheet and a tooltip at 2560 x 1315): the share of soft edge pixels (luma 0.15 to 0.85 of the ink pixels, lower is crisper) was
  0.375 for the installed fonts, 0.379 and 0.373 for trace `alphamax` 0.6 and 0.3 (no effect), 0.343 with a level cap of 0.42, 0.301 bilinear without mipmaps and
  0.168 nearest (stair-stepped). The filter, not the trace, made the text soft. The owner chose `alphamax` 0.6 with cap 0.42 plus bilinear sampling without mipmaps
  (0.279 in the viewer). Cost: the cap makes glyphs about 11 % bolder in area (median traced ink 1.107 of the mask's against 0.998) and raises the round-trip error
  (0.079 against 0.059); without mipmaps a window smaller than about 2x may shimmer when text moves (not checked). `outline.py` holds `ALPHA_MAX` and `LEVEL_CAP`,
  `hd_font.py` takes `--alpha-max`, `--level-cap` and `--opt-tolerance`, recorded in the receipt.
- **Owner check (slice 3):** compare the traced text with the 1x text at UI scales 1, 1.5, 2, 3 and 4 on the main menu (327), options (27, 229), the
  character sheet (27, 229, 171), the dialog box and floaters (229), the world map (840), save/load (27) and the pre-made browser (27, 171). The 6 to
  9 px fonts (Garamond 6/8/9, Courier 10, Pork 12) stay bitmap or get the stroke-aware step first; the reading windows use some of them (ledger WR6-WR8:
  480 Courier 10 for the telegram, 482 Garamond 8 in the newspaper masthead, 497 Pepper 20 for the note), so that text stays at 1x until then.

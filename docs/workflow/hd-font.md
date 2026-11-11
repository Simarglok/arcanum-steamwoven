# HD font tracing: the player's interface fonts as 4× coverage frames (ARC-DEV-355)

Slice 1 of the font vectorization research ([ARC-RES-100](../research/font-vectorization.md), section 7). The tool
[`tools/hd_font`](../../tools/hd_font/hd_font.py) reads the interface bitmap fonts from the **player's own installation**, traces every
glyph's coverage mask into an outline and renders it back as a 4× coverage frame. It writes **local files only**; it does not build
or touch an HD pack (that is slice 2, which needs the owner's call on a pack format change), and the viewer does not read the frames yet.

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
level** (the level at or below 0.5 at which the binary area equals the mask's total coverage), Potrace curve fit (`turdsize` 24,
`alphamax` 1.0, optimizing curves, tolerance 0.2), even-odd fill, rasterized at 16× and box-reduced to 4×. It is deterministic.

- Dependencies: numpy, scipy, Pillow and **potracer** (GPLv2+, a port of Potrace), pinned with wheel hashes in
  [`requirements.lock`](../../tools/hd_font/requirements.lock) (macOS wheels, CPython 3.12 to 3.14). potracer is an external tool
  dependency installed into a private venv, never vendored and never linked by a C# assembly, like Unicorn in
  [toolchain](toolchain.md#function-oracle). That the traced output of the player's own fonts is not covered by its licence is a
  judgement, not a legal opinion.
- Quality is bounded by the source: fonts of 6 to 9 px and one-pixel strokes recover poorly (research, section 5), and the tool does not
  yet have the stroke-aware step; art 840 has only three grey levels, so its trace is judged on its own. Frames whose ink is under a
  tenth of a pixel (placeholders such as Cloister's seven) may trace to nothing; `font.json` lists `vanished` frames above that mass.
- `interface.mes` is read in its simple `{number}{text}` form (first message of a number wins).
- Not done here (later slices): pack and viewer support for coverage frames, the remaining 23 fonts' review, runtime outlines.

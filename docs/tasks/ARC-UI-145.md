# ARC-UI-145 — HD interface fonts: sharper text

Work-Item: ARC-UI-145
Result: The HD bitmap-font text (character sheet, tooltips, dialogs) is drawn bilinear without mipmaps from glyph frames traced with sharper parameters, as the owner chose from an on-screen comparison.
Components: `tools/hd_font` (trace defaults), `AreaArtFontFace` (viewer sampling), local HD pack font entries
Human-Gates: the real pack `apply` and the in-game look are the owner's
Requirements: FR-UI-001, FR-UI-008

Finding (owner's bug report 2026-11-11, character sheet): the HD text of ARC-DEV-361 looked too smooth. Seven variants were drawn in the real viewer (bar smoke
readback of the sheet and of the Willpower tooltip at 2560 × 1315, local, never in git) from clones of the installed pack with replaced font frames: the current
one, trace `alphamax` 0.6 and 0.3, `alphamax` 0.6 with level cap 0.42, bilinear and nearest sampling without mipmaps, and the original Legacy bitmap font. Share of
soft edge pixels in the text (luma 0.15 to 0.85 of all ink pixels, lower is crisper): 0.375 current, 0.379 and 0.373 for `alphamax` 0.6 and 0.3 (no effect), 0.343
with cap 0.42, 0.301 bilinear without mipmaps, 0.168 nearest (stair-stepped edges). The filter, not the trace, made the text soft. The chosen combination, drawn afterwards in the viewer with the new defaults: 0.279 (installed fonts 0.375). In a 1280 × 800 window (sheet scale 1.5) the text is crisper and bolder than before with no visible stair-stepping in a still readback; shimmer in motion was not looked at.

Choice (owner): trace `alphamax` 0.6 with cap 0.42, and bilinear sampling without mipmaps. Cost: the cap makes glyphs about 11 % bolder in area (median traced ink 1.107 of
the mask's against 0.998, local figures of the player's five fonts) and raises the round-trip error (0.079 against 0.059); without mipmaps a window smaller than about 2× may
shimmer when text moves (not checked).

Change: `outline.py` constants (`ALPHA_MAX` 0.6, `LEVEL_CAP` 0.42), `hd_font.py` (`--alpha-max`, `--level-cap`, `--opt-tolerance` with range checks, restored after a run, recorded
in the receipt; tool version 2), tests (defaults equal the tracer's constants, a custom run is recorded and undone, a cap of 0.5 keeps the old ink bounds, the shape tests take
the cap's extra ink), `AreaArtFontFace` texture filter, docs ([hd-font](../workflow/hd-font.md), [HD pack workflow](../workflow/hd-pack.md#traced-fonts-in-the-pack-arc-dev-361),
[terrain viewer](../rendering/terrain-viewer.md), [font research](../research/font-vectorization.md)). No pack format change. No original or derived data in git.

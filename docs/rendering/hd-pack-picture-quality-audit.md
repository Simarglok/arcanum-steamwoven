# HD pack quality audit of icons and pictures, per category (ARC-RES-3144)

Status: **audit, 2026-11-24** (work item ARC-RES-3144, [#3144](https://github.com/Simarglok/arcanum-steamwoven/issues/3144)). Python-only,
read-only: the installed pack and the player's own art frames were read, nothing was written to them. Requirements touched (status unchanged):
[FR-UI-007](../requirements/ui-media-lifecycle.md#fr-ui-007), [FR-PRODUCT-001](../requirements/content-product.md#fr-product-001).

Owner decision (2026-11-24): pictures and icons come from the HD pack only if its quality suits. This page records the check and a recommended source
per category; **the owner picks**, and the batches [#2484](https://github.com/Simarglok/arcanum-steamwoven/issues/2484) to
[#2487](https://github.com/Simarglok/arcanum-steamwoven/issues/2487) are rewritten or closed after that. No image, hash or pixel of the game is in this
note: art names and numbers only. The composites live in `local/steamwoven-ui/composites/ARC-RES-3144/` (never in git).

## Method

For every art of the four batches that has a pack frame (all frames), and for the 126 portraits, the pack frame is box-averaged down to the original size and
compared with the original frame. Numbers:

- **Contour recall / precision** (the shape check that matters; alpha is the Legacy mask by construction, so the silhouette cannot differ): the strongest
  15 % gradients of the smoothed luminance on both sides; *recall* is the share of the original's contours that have a pack contour within 2 original
  pixels, *precision* the reverse. A redrawn shape that differs from the original (the class of [#3105](https://github.com/Simarglok/arcanum-steamwoven/issues/3105))
  shows as low recall. Texture the redraw adds lowers precision only.
- **Colour**: mean CIE76 distance dE, mean lightness shift dL (pack minus original), chroma ratio.
- **Upscale character**: mean |pack minus plain nearest upscale| and the share of flat 4x4 blocks (staircase left); **ghost** share (pack structure on a black
  background where the original has none, the bleed of a redraw sheet's neighbours); **halo** (fringe against interior lightness, the 17 arts with alpha).
- **In-game size**: the composites show real pixels at 1920x1080 (UI x1.8) and 2560x1440 (x2.4): Legacy nearest, pack Lanczos-minified.

The numbers are a proxy; every category was also looked at in the composites (sample of 6 to 16 per category plus contact sheets of every art).

## Results

| Category | Arts | Pack frames | Contour recall (median, arts below 0.85) | Colour (dE median, dL median) | Verdict |
| --- | ---: | ---: | --- | --- | --- |
| Spell and skill icons (#2484) | 88 | 88 | 0.97, 6 | 9.7, -1.8 | faithful, richer texture, no dither, no halo |
| Item-type, critter, status, other icons (#2485) | 162 | 151 | 0.97, 7 (+ 5 of 12 indicator arts) | 9.3, -1.0 | faithful except named exceptions |
| Schematic record pictures (#2486) | 134 | 72 | 0.88, 20 | 17.8, -10.3 | shapes kept, tone and paper restyled; 62 without frame |
| Cursors (#2487) | 12 | 0 | n/a | n/a | Legacy by design, nothing to compare |
| Portraits (outside the kit) | 126 | 126 | 0.99, 0 | 7.0, -3.0 | faithful |

### Spell and skill icons

Contour recall median 0.97 (43 arts at 0.95 or more, 39 at 0.85 to 0.95, 6 below: `s_nature1`, `s_earth1`, `s_fire2`, `s_evilnecro1`, `s_evilnecro2`,
`s_earth3`, where a glyph detail is reinterpreted but the symbol is the same). The pip count of every art matches. Colour is richer, same hue (chroma
x1.08), slightly darker (dL -1.8). No Legacy dither is left, no ghost, the tiles are opaque. Recommendation: **pack as is** (the six above are a
fix list if the owner wants it); batch #2484 is not needed.

### Item-type, critter, status and other icons

135 art icons with pack frames: recall median 0.97, 87 arts at 0.95 or more, 41 at 0.85 to 0.95, 7 below (`AP_Red`, `GeneralRingIcon`, `GeneralMedalionIcon`,
`AP_Orange`, `LevelUpIcon`, `RobeGeneralIcon`, `CombineIndicator1`; ring and medallion are redrawn at a slightly different angle, the 11x12 AP lights are
tiny). Defects found: **ghost bleed** on black backgrounds (faint outlines of neighbouring icons, visible at x4; `Ammo_Icon_Arrows`, `Ammo_Icon_Gold`,
`Ammo_Icon_Fuel`, `Ammo_Icon_Bullets`, `TechSkillIcon`, `E_EarthIcon`, `SocialIcon`, `CombatIcon`, `Cont_Junk`, `racedarkelficon`; 0.3 to 4 % of the
background); the 37x24 ammo icons are soft. **Indicators** (turn-based, jump, waypoint, scroll lines): `tb_td` redraws the square grid dots as round dots with a
blurred rim (recall 0.26), `ts_iso`, `jmp_td`, `wp_iso` thin lines are re-rendered softer (recall 0.33 to 0.60 on the line art; shapes read the same); there is
no gain in HD for flat line art. 11 arts have no pack frame (8 scroll cursors, `scroll_not`, `BlockedShot`, `hoverover`). Halo: none (fringe lighter than
interior at most +11 on 17 alpha arts; the dark fringe of the `Pen_*` arts is the original's own outline). Recommendation: **pack as is for the 135 arts, with
fixes** (clear the ghost background of the 10 arts above; leave the indicators and the 11 frameless arts **Legacy**, or **kit by rule** from the original's mask if the owner
wants crisp lines); batch #2485 shrinks to those 21 arts or is closed.

### Schematic record pictures

72 of 134 arts have a pack frame (the other 62 draw Legacy, see the gap census). Contour recall median 0.88 (3 at 0.95 or more, 48 at 0.85 to 0.95, 20 below;
`S_Miners_Helmet` 0.50 and a grey bar down its left edge). The objects keep their shapes but the redraw is a **restyle**: 66 of 72 are darker than the original by
more than 8 lightness units (median dL -10.3, dE 17.8), the original's red and orange paper grid is replaced by a fine grey grid, detail is added. It looks
good by itself and reads as a different paper. A pack where 72 pictures are restyled and 62 stay Legacy would look mixed on one screen. Recommendation:
**pack with fixes** if the owner likes the restyle (re-tone to the original paper and grid, fix `S_Miners_Helmet`) **and** the same route for the other 62
(kit by Codex as the batch is written, in the same style); otherwise **Legacy** for all 134. Not recommended: kit by rule (these are drawings, not glyphs).

### Cursors

The pack holds no frame for any of the 12 cursors (Legacy by design, 16x22 to 32x37, hot spot at the tip). Nothing to audit against the pack. Recommendation:
**Legacy** (a soft-edge upscale of the original's mask is the only rule-based option and gains little at these sizes; the hot spot must not move). Batch #2487
closes as written.

### Portraits and the pictures outside the kit

126 portrait frames: contour recall median 0.99 (123 at 0.95 or more, none below 0.93), dE 7.0, dL -3.0 (a little darker), no ghost except three `_b` frames
(`portrait-elf1_b`, `portrait-gnm1_b`, `portrait-elf3_b`, 0.5 to 3 %). Recommendation: **pack as is**. Inventory item pictures and critter body pictures are
world art (`art/item`, critter sprites): the pack holds none by the owner decision of 2026-11-15, so there is nothing to audit; they stay Legacy.

## Limits

Contour agreement is a proxy and cannot tell a better redraw from a different one; the composites are the evidence the owner decides on. The sample composites
show frame 0; the numbers cover every frame. Art counts are by art name (five arts share the name `LevelUpIcon`, so 151 art entries show as 147 names). Opaque frames carry no alpha check (the alpha is the Legacy mask). Tooling: throwaway Python (numpy, scipy, Pillow in a scratch
venv), kept under `local/steamwoven-ui/composites/ARC-RES-3144/tools/`, not committed (it reads original frames).

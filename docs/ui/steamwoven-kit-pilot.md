# Steamwoven kit pilot: bottom-strip round buttons through the whole chain (ARC-UI-3058)

Status: **pilot run, 2026-11-20/21, second round 2026-11-21 (v2, not installed), third round 2026-11-21 (v3: glyphs by rule, whole plate in the game)** ([#3058](https://github.com/Simarglok/arcanum-steamwoven/issues/3058), epic [#2446](https://github.com/Simarglok/arcanum-steamwoven/issues/2446)).
Requirements touched (status unchanged): [FR-UI-002](../requirements/ui-media-lifecycle.md#fr-ui-002), [FR-UI-007](../requirements/ui-media-lifecycle.md#fr-ui-007).
Every image named here is local under `local/steamwoven-ui/` (never in git); this page holds only numbers, names and findings.

## What ran

One round plate in two looks, four glyphs in two looks and the buttons of arts 470, 471, 472 and 473 (Combat shield, Schematics gear, Skills key, Spells arrows; 39 x 39 units, three frames each: idle, pressed, hover), so all four round buttons of the bottom strip come from the kit,
through `prompt library -> codex_batch -> fit -> assemble -> stage/plan/apply -> in-game captures`.

| Step | Tool | Result |
| --- | --- | --- |
| Batch | `local/steamwoven-ui/inbox/kit-pilot-round-button/` (hand script, finding F3) | round 1, 4 items: `plate-round-button` (2 cells: idle, pressed), `glyph-key`, `glyph-gear`, `glyph-arrows` (2 cells each: idle, lit). References per item: the upscaled ORIGINAL frames (Lanczos, 1560 x 780 or 1740 x 870) as the image to edit, the approved sheet cut-out of the round button, for the plate also its `.context.png`. Prompts: `kind-plate`, `kind-glyph` of the library (extended in this PR so a part note can ask for states on one sheet) |
| Codex | `codex_batch.py run`, `-m gpt-6.1-sol`, effort medium | 4 of 4 `ok`, each 1774 x 887 px (1.57 MP, the budget; asked 2:1), real alpha, 55 to 88 s each; no refusal, no failure; provenance per image in `outbox/kit-pilot-round-button/<id>.provenance.json` |
| Batch 2 | `inbox/kit-pilot-glyphs-relief/` | the four glyphs again (key, gear, arrows, shield), 2 cells each (dull, lit), with the relief wording of `kind-glyph` (below); plate untouched |
| Fit | `fit.py` against the approved sheet (sha256 `4d5fb9c0...`) | 10 masters: `plate.round_button`, `plate.round_button_pressed` (138 x 138), `glyph.{key,gear,arrows,shield}` and `_lit` (108 x 108); records `provenance/fit/<part>.json` |
| Assemble | `assemble.py`, alpha `masked`, 3.6 to 4 px per unit | arts 470, 471, 472, 473: 3 frames each at 156 x 156, every gate passed |
| Pack | `hd_pack.py stage`, `plan`, `apply`, `verify --deep` on a **candidate clone** of the installed pack | round 2: 12 entries replaced (Combat 470 added to the three of round 1), 0 added, `verify --deep` PASS (7076 entries, candidate manifest `c846a6eacad6`); the installed pack is untouched (manifest `386d49c59b83`) |
| Captures | `./arcanum smoke pr --stages bar` with `ARC_STEAMWOVEN_BUTTON_STATES=1` (new, this PR) | 1920 x 1080 and 2560 x 1440, Original (Legacy art), pack before, pilot, four buttons, idle, hover, pressed (the idle capture parks the pointer off the button, hover moves onto it, pressed holds the mouse down; a toggle is clicked off afterwards): `local/steamwoven-ui/pilot/3058/shots/` |

## Numbers

- **Fit (gate: halo mean dE 3 / p95 6 as of SWI-VER-3; the pilot ran the fit with the limits relaxed to 9 / 20 in a throw-away wrapper, the repo tool unchanged, and the records show those limits).**

  | Part | Halo mean / p95 dE (before the safety net) | Colour lock to the sheet |
  | --- | --- | --- |
  | `plate.round_button` | 7.5 / 16.2 | brass, strength 0.3: shift dE 2.9 (full strength 9.6 failed the high-frequency gate) |
  | `plate.round_button_pressed` | 4.4 / 8.7 | brass, strength 0.3: shift dE 3.9 |
  | `glyph.key` / `_lit` | 3.7 / 8.3, 3.9 / 8.3 | none (a glyph is no sheet material) |
  | `glyph.gear` / `_lit` | 6.7 / 19.9, 8.1 / 19.8 | none |
  | `glyph.arrows` / `_lit` | 1.1 / 2.1, 2.4 / 5.6 | none |

  After the final safety net every part measured p95 2.2 to 2.8 dE.
- **Plate shape.** The generated plate is slightly oval (796 x 815 px in a cell of 887): the masters take the 815 px square; `masked` alpha keeps the plate's own outline (the original's silhouette is the full 39-unit circle, so `legacySmooth` refused 1716 uncovered pixels).
- **Pack.** The art key is the content hash of the ART file: Schematics_Button (471) has the same content as Char_Tech_Skills, so its frames are the entries of `char_tech_skills`, and the update replaced those three; Skills_Button (472) and Spells_Button (473) replaced their own. Art 471 therefore was never missing at run time (the "pack before" capture draws its HD gear), see F6.

- **Manifest sizes.** The ten new parts are rows with an open size (a part with no source art has one until its batch fixes it, `kit_check`); the masters are 138 x 138 and 108 x 108 px as the fit records say, and the rows' notes name them.

## Second round (owner review 2026-11-21)

1. **Stair-steps at 1080p and 1440p: found, in the viewer's draw, not in the pack.** The assembled frames are smooth. In the Generated-interface mode `InterfaceChromeRenderer.PaintButton` paints its own rim and face and keeps the pack picture only where `GlyphMask` says "glyph": a lookup on the Legacy 39 x 39 grid (colour threshold 60, grown one Legacy pixel), so every glyph edge was cut along that grid and a dull glyph (the idle key) fell below the threshold and was replaced by the generated face. Fix for the four bottom-strip buttons (`combat_button`, `skills_button`, `spells_button`, `schematics_button`): the recipes carry `KeepFace` (v2; superseded in round 3 by `WholePicture`, below), the art's own picture stays inside the generated rim. The general mechanism for every other button recipe is [#3087](https://github.com/Simarglok/arcanum-steamwoven/issues/3087). The generated rim bands still show 1-px jaggies at the two sizes (generated chrome, not the 39 x 39 steps).
2. **Tone per state, by numbers** (`tools/steamwoven_ui/glyph_tone.py`, tests `test_glyph_tone.py`). The original glyph is dithered (about half its pixels are the dark ground), so the target is the brighter half's CIE L* a* b* mean, and the position is relative to the plate: the plate dome of the idle frame is the reference (the hover frame's dome is lit by the glow, the kit's plate is not). The glyph's L* above the dome, original target (`measure`, with `--dome-frame` the idle frame) against the assembled frame (`report`, plain mean; the lit result is compared with the bright-half target):

   | Glyph | Idle target / result | Lit (hover, pressed) target / result |
   | --- | --- | --- |
   | shield (470) | 10.2 / 9.8 | 34.4 / 33.7 |
   | gear (471) | 12.8 / 12.9 | 42.5 / 41.0 |
   | key (472) | 10.0 / 8.7 | 34.7 / 33.5 |
   | arrows (473) | 9.9 / 10.1 | 40.5 / 40.8 |

   Every glyph lands within 1.5 L* of its target; the master shifts the fit applied are in `provenance/fit/glyph-tone-3058.json` (local). The plate is untouched.
3. **Relief.** `kind-glyph` now asks for an engraved or inlaid motif in low relief, lit from the upper left like the rest of the kit: a thin highlight along the upper-left edge, a thin darker shadow along the lower-right edge, a thin bevel, soft shading, matte, no gloss, no glow. The relief glyphs measured halo mean / p95 dE 9.95 / 19.5 (key lit) and 9.2 / 17.8 (arrows lit) before the safety net, run with the limits relaxed to 11 / 24 (#3073).
4. **Combat shield (470).** One more glyph pair (`glyph.shield`, `glyph.shield_lit`) on the same plate, the same batch, the same chain.

## Third round (v3, owner decision 2026-11-21): glyphs by rule, whole plate in the game

The owner rejected v2: the checker on the original's glyphs (one original pixel wide, weak when idle, strong on hover and pressed) is **part of the picture**, not a dither to smooth away; the glyph sat differently inside each button; the generated rim in the game was jagged where the original has a brass setting with a soft shadow. v3 draws the glyphs **without Codex**, by a rule from the original's own frame, and the viewer draws the kit plate whole.

1. **Silhouette and position from the original's glyph mask** (`tools/steamwoven_ui/glyph_cells.py mask|cells`, tests `test_glyph_cells.py`). The glyph's cells are the checker's bright cells (lighter than their four neighbours by an Otsu-split threshold inside the dome), the cells whose colour is far from the dome's local colour (the shield's bar, a dim idle arrow), the dark cells of the checker between them, closed by one cell, specks dropped. The mask is read from the **lit frame** for all three states (the glyph is one shape; the idle frame's checker is too weak to give a clean mask). The 39 x 39 mask becomes a smooth outline at 4 x (a Gaussian of 0.6 cell, a soft threshold), so no edge is a stair of original pixels. The canvas is the whole 39-unit frame (156 x 156 px, 4 px per unit) placed by the layout at 0,0, so the glyph lies at the original's coordinates, not at the centre of a picture.
2. **Cells by a deterministic rule.** The cell is the original pixel, aligned to its grid; its parity is the original's (the phase is read from the frame); a cell's tone is the mean L\*a\*b\* of the cells of that parity in a 3 x 3 window of the original (grown when it holds none), so the contrast of the cells in each state, and the shading (the shield's bar, the key's hole, the hover glow), are the original's. The tone is then shifted as a whole (`glyph_tone.py`, `--stat mean`, against the kit plate's dome, the idle frame's dome as the reference) so that the glyph's brightness over its plate equals the original's over its own. Two looks, same rule: `flat` (crisp square cells, a soft-edged silhouette) and `bevel` (the same with a thin bevel; dropped by the owner in round four).
3. **Plate with a soft shadow in its alpha** (`glyph_cells.py plate`): the kit plate master is brought to 36.5 units across, centred on the 39-unit canvas, with a soft dark shadow (alpha 0.55, blur 0.45 unit, moved 0.2 / 0.3 unit down and right: its tail is gone before the original silhouette's edge, whose 1-unit steps would otherwise cut it into a jagged dark outline) under it; `assemble.py` (alpha `masked`, master scale 4, exact position) lays glyph over plate and the pack gate passes (the shadow stays within one original texel of the silhouette).
4. **In the game, the plate whole.** A recipe with `WholePicture` (the four bottom-strip buttons) draws the pack's picture as it is, colour and alpha, instead of a generated rim (`InterfaceChromeRenderer.PaintWholePicture`; the generated path resamples such a picture with `ResampleStraight`, which keeps the alpha), so the kit plate's brass rim and its soft shadow are drawn whole at every size. Without a pack picture (HD option off, no art in the pack) the button is drawn as before. The generated rim's 1-px jaggies remain for every other button: [#3087](https://github.com/Simarglok/arcanum-steamwoven/issues/3087). This replaces v2's `KeepFace` on these four recipes.

Glyph brightness over the dome (L\*), original / kit result (mean of the glyph's cells), flat look (bevel within 0.3):

| Glyph | idle | pressed | hover |
| --- | --- | --- | --- |
| shield (470) | 4.1 / 3.7 | 20.3 / 18.8 | 20.3 / 18.5 |
| gear (471) | 7.3 / 6.7 | 23.8 / 22.8 | 23.9 / 22.6 |
| key (472) | 15.7 / 15.0 | 41.6 / 40.1 | 41.6 / 40.1 |
| arrows (473) | 8.1 / 8.1 | 26.2 / 26.0 | 26.2 / 26.1 |

Pack: two candidate packs on clones of the v2 candidate (12 entries replaced each: `flat` manifest `592ba4963e73`, `bevel` `e85daefafe4c`, `verify --deep` PASS 7076 entries); the installed pack (manifest `386d49c59b83`) is untouched. The kit manifest rows of the glyphs now say they are drawn by rule (`rule:glyph_cells:<id>`, no fit record); the Codex glyph batches of rounds 1 and 2 are no longer used.

## Fourth round (v4, owner decision 2026-11-24): masks from the union of states, pressed plate, plate fills the socket

The owner chose the flat look (no bevel; the bevel and the 36.5-unit plate with a shadow of round three are superseded) and four fixes. Nothing is installed in the real pack.

1. **Masks from the union of all states** (`glyph_cells.py cells --mask-frame PATH[:RADIUS]`, repeatable). The lit frames and the pressed frame give the mask; the pressed frame's rim is dark, so its mask may reach radius 18.5 units instead of the dome's 15.5, which brings back what the dome circle cut: the key's beards and teeth to the rim and its head eye, the arrows' tips. The eye (a small dark hole) stays because the fill rule and the closing only fill cells that are not darker than the dome reference; enclosed specks of 2 cells or fewer are filled; the dome reference is read from the quiet cells only (the shield's body no longer pollutes it).
2. **Gear hole a true circle** (`--round-holes`): an enclosed hole of at least 10 cells is replaced by the circle of equal area at its centroid, soft by about one pixel, after the mask is taken; the small eye of the key is not regularized.
3. **Pressed plate as in the original** (`glyph_cells.py pressed IDLE_CANVAS OUT`): the whole plate darkens and an inner shadow lies on the upper left, by a rule fitted to the original's L\* bands (dome ring at 12-15 units, upper-left x0.65 to x0.43; rim 15-17 units x0.42; rim 17-19.5 units x0.29; lower right x0.94 / x0.81). Pressed is no longer the hover plate.
4. **Plate fills the 39-unit socket** (`plate --plate-units 38.6 --shadow 0 --edge-dark 0.4`): the plate is 38.6 units across with a darker last unit instead of the soft shadow of round three, so no dark outline stands outside it. In the game the four sockets of the bottom strip's picture (`intbotom.art`) are bare holes with the board colour (`ChromeBoardHole`), not the Ink of an ordinary hole, so the black stair-stepped ring around the plate (the Ink fringe of the hole's transparent texels) is gone; the faint dark 1-px edge of the strip's hole itself that is left is [#3109](https://github.com/Simarglok/arcanum-steamwoven/issues/3109), a separate task (owner decision 2026-11-24). The original's brass setting around the four sockets (in the strip's own art) is not drawn: without it the buttons read smaller than the original; the kit must supply it for these sockets (noted on #3042 and #2481).

Glyph brightness over the dome (L\*), original / kit, flat (`glyph_tone.py report --stat mean --dome-frame idle`):

| Glyph | idle | pressed | hover |
| --- | --- | --- | --- |
| shield (470) | 4.5 / 4.1 | 20.7 / 19.9 | 20.7 / 19.1 |
| gear (471) | 7.5 / 7.0 | 23.6 / 23.2 | 23.6 / 22.5 |
| key (472) | 15.7 / 14.9 | 41.1 / 40.0 | 41.2 / 39.5 |
| arrows (473) | 7.9 / 7.9 | 24.9 / 25.0 | 25.0 / 24.8 |

Pack: one candidate (`flat`, manifest `c3bc53185579`, 12 entries replaced, `verify --deep` PASS); the installed pack (manifest `386d49c59b83`) is untouched.

## Chain findings (each an issue)

| # | Finding | Issue |
| --- | --- | --- |
| F1 | The fit tool's halo gate contradicts the prompts: bevelled rims and the light edge of an engraved glyph fail it while the colour lock and the safety net would pass them (5 of 8 parts failed at the strict limits) | [#3073](https://github.com/Simarglok/arcanum-steamwoven/issues/3073) |
| F2 | `fit --batch B --prompt ID` looks for `provenance/<batch>/<id>.json`; `codex_batch` writes `outbox/<batch>/<id>.provenance.json` | [#3072](https://github.com/Simarglok/arcanum-steamwoven/issues/3072) |
| F3 | `prompt_library.py inbox` builds only the library batches; a part batch (references cut from original frames, states on one sheet) needed a script | [#3075](https://github.com/Simarglok/arcanum-steamwoven/issues/3075) |
| F4 | The colour lock has one material per part: a plate (brass rim, olive dome) shifts as a whole and a full-strength lock fails the high-frequency gate | [#3074](https://github.com/Simarglok/arcanum-steamwoven/issues/3074) |
| F5 | Nothing turns assembled frames into an importer plan (items, grey alpha masks) | [#3077](https://github.com/Simarglok/arcanum-steamwoven/issues/3077) |
| F6 | The HD gap census reports 471 as missing; the pack resolves it through the content key | [#3076](https://github.com/Simarglok/arcanum-steamwoven/issues/3076) |
| F7 | Fixed in this PR: the manifest could not hold a part with no original art of kind `plate` or `glyph` (`NEW_PART_KINDS`), so the shared plate and the glyphs had no row for `fit` | this PR |

Further observations (no issue, judged by the owner): the plate's rim is the copper-gold of the approved sheet's round-button cut-outs, the dome is darker than the sheet's; in the game the strip's own ring hardware and its hover overlay stand over the button art at every size, so the pilot buttons read softer than the pack's dithered redraws, the dim idle key is the weakest of the three; the generated glyphs are clean (no dither), the Codex session kept one consistent look over four items.

## Not done

The frames are **not** in the installed pack and the PR is not opened: the owner's go is needed (workflow: install after the owner allows it, game closed). The candidate pack (`local/steamwoven-ui/pilot/3058/v4/candidate/`) and its backup of the 12 replaced files are what the captures show; installing is `plan`, `apply --dry-run`, `apply`, `verify --deep` on the installed pack with the staging folder `local/res095/pack-out/kit-pilot-3058-v4`, base `--base` the installed pack itself.

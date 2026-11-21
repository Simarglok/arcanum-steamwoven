# Steamwoven kit pilot: bottom-strip round buttons through the whole chain (ARC-UI-3058)

Status: **pilot run, 2026-11-20/21, second round 2026-11-21 (owner review: edge steps, tone per state, relief, Combat shield)** ([#3058](https://github.com/Simarglok/arcanum-steamwoven/issues/3058), epic [#2446](https://github.com/Simarglok/arcanum-steamwoven/issues/2446)).
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

1. **Stair-steps at 1080p and 1440p: found, in the viewer's draw, not in the pack.** The assembled frames are smooth. In the Generated-interface mode `InterfaceChromeRenderer.PaintButton` paints its own rim and face and keeps the pack picture only where `GlyphMask` says "glyph": a lookup on the Legacy 39 x 39 grid (colour threshold 60, grown one Legacy pixel), so every glyph edge was cut along that grid and a dull glyph (the idle key) fell below the threshold and was replaced by the generated face. Fix for the four bottom-strip buttons (`combat_button`, `skills_button`, `spells_button`, `schematics_button`): the recipes carry `KeepFace` (with `KeepFaceWithPictureOnly`: only when a pack picture is given, the Legacy pixels alone are drawn as before), the art's own picture stays inside the generated rim. The general mechanism for every other button recipe is [#3087](https://github.com/Simarglok/arcanum-steamwoven/issues/3087). The generated rim bands still show 1-px jaggies at the two sizes (generated chrome, not the 39 x 39 steps).
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

The frames are **not** in the installed pack and the PR is not opened: the owner's go is needed (workflow: install after the owner allows it, game closed). The candidate pack (`local/steamwoven-ui/pilot/3058/candidate/`) and its backup of the 12 replaced files are what the captures show; installing is `plan`, `apply --dry-run`, `apply`, `verify --deep` on the installed pack with the staging folder `local/res095/pack-out/kit-pilot-3058`, base `--base` the installed pack itself.

# ADR: Steamwoven interface — an authored 4K kit on a centred 4:3 composition

Status: **accepted by the owner on 2026-11-16** (owner thread, work item ARC-UI-2447,
[#2447](https://github.com/Simarglok/arcanum-steamwoven/issues/2447)) and **amended on 2026-11-19** (owner verdict of the column check [#2892](https://github.com/Simarglok/arcanum-steamwoven/issues/2892); work item ARC-DOC-2893,
[#2893](https://github.com/Simarglok/arcanum-steamwoven/issues/2893)): decisions 1, 2, 3, 5 and 10 below are replaced by the [amendment](#amendment-of-2026-11-19), the rest stands; the open owner decisions are listed under
[Open decisions](#open-decisions) and tracked as issues. Epic:
[#2446](https://github.com/Simarglok/arcanum-steamwoven/issues/2446). Requirements and the work breakdown:
[TRD](steamwoven-interface-trd.md). Requirements touched (status unchanged):
[FR-UI-007](../requirements/ui-media-lifecycle.md#fr-ui-007),
[FR-PRODUCT-001](../requirements/content-product.md#fr-product-001),
[FR-PRODUCT-004](../requirements/content-product.md#fr-product-004).

## Context

The game has two interface looks, which the owner calls **Original** (Legacy) and **Steamwoven**:

- **Original** draws the interface as the original does (800 × 600 layout, Legacy art), with small improvements at
  most. It is the look with the HD option off. The 2026-11-16 text left it untouched; the 2026-11-19 amendment gives it the same 4:3 composition (art and rules unchanged).
- **Steamwoven** is the interface of the HD option ([decisions](../architecture/decisions.md), "HD scope and relit
  look", 2026-11-15: the HD option is the HD interface plus the relit lighting). How to make it was open.

Three earlier routes:

1. **Per-file redraw fitted to the original** ([ARC-RES-094](../rendering/hd-interface-art.md), U3): every
   original interface frame redrawn (the owner's Codex/ChatGPT redraws), registered and locked to the original pixels,
   Legacy alpha. It keeps the 800 × 600 geometry, so the interface cannot grow to the full width, and the files are
   redrawn one by one, so tone and texture drift between them (the wood of the strips was the visible case).
2. **Strips centred with tiled sides** ([ARC-RES-093 C](../research/screen-size-and-ui-layout.md#c-recommendation)):
   the 800-unit strip centred, the rest of the width filled by repeating its edge columns (9-slice). The tile repeats
   and the free columns of the bottom strip are narrow ([U4](../rendering/hd-interface-art.md#9-slice-side-pieces-u4)).
3. **Generated chrome on a density scale** ([ARC-RES-122](../rendering/hd-interface-density.md)): chrome drawn by
   code at the screen's density, the interface keeping a physical size. The owner found the look wrong (owner thread 2026-11-16), but it
   was useful for understanding the process, the layout and the parts.

The owner's Codex redraw experiments showed promise; the open problem is keeping the style consistent (tone and
texture of the wood backgrounds).

## Decision

1. ~~**Full-width layout.**~~ **Superseded 2026-11-19.** Was: the top and bottom panels span the whole width of the frame and the sides continue the same material, tone and texture as the centre. Now: the strips are the original 800 units wide and
   the sides show the world or black ([amendment](#amendment-of-2026-11-19)).
2. ~~**An open menu fills the whole play area.**~~ **Superseded 2026-11-19.** Was: a menu window's backdrop fills the whole middle band and each half of a two-part window continues outwards in its own material. Now: the window stands in the 800 × 400 unit zone, the
   world at the sides is dimmed, and nothing is continued outwards.
3. ~~**Three bands of one material.**~~ **Superseded 2026-11-19.** Was: one continuous board for the top and bottom panels. Now: two strips of art with end caps, no board; hardware stays a separate layer over the strip, not baked into it.
4. **Authored for 3840 × 2160, baked down.** Every part is authored as a master for the 3840 × 2160 reference screen, of which the 4:3 composition is 2880 × 2160 px, and baked to four steps:
   ×1, ×2/3, ×1/2, ×1/3 (composition widths 2880, 1920, 1440, 960). The step is the smallest one at or above `f = min(W / 2880, H / 2160)`; the run-time scale between steps is fractional. The interface
   size is proportional to the screen (not a physical size).
5. ~~**Aspect range.**~~ **Superseded 2026-11-19.** Was: 16:10 to 16:9 native, a 16:9 frame with black bars for wider screens and a 16:10 frame for narrower ones, no 4:3 or 21:9 layout. Now: every aspect is supported by scaling the one 4:3 composition
   (`s = min(W / 800, H / 600)`); 4:3 is native, and the old survey argument (no 4:3 share, 16:10 about 11 %, ultrawide about 4 %) only shows that the composition must look right at 16:9 first.
6. **A kit of parts.** The interface is composed of parts: materials (wood board, paper, parchment, …), backdrops,
   hardware (brass rings, sockets, frames), button plates, glyphs on transparent backgrounds, button state overlays,
   sheets (newspaper, letter, book page), the schematic grid as its own layer, item pictures on transparent backgrounds
   for the schematic, gauges and decor. A part used in several windows is one part at one size and style. Text is never
   baked into art; it is drawn with fonts.
7. **All art is redrawn by Codex image generation.** The style is held by: a census of the original interface
   (screenshots, element extraction, reuse); whole-screen context images first (as in the current HD pack) so Codex sees
   each part's purpose; a material sheet the owner approves; prompts per part kind; a fit tool that locks every part to
   the approved materials. Original art is given to Codex as input, and the owner runs Codex; agents prepare the
   inputs and process the outputs (owner decision 2026-11-16). So the kit is derived content: it stays local like the HD
   pack and is not shipped.
8. **Large surfaces are a research item.** How to get 2880-wide strips and 2880 × 1440 window backgrounds from a generator with a smaller
   output (upscale, tiled regeneration, material texture plus macro image) is decided by comparison
   ([#2457](https://github.com/Simarglok/arcanum-steamwoven/issues/2457)). Regenerating tiles separately is expected to
   leave visible differences at the joins, so it must prove itself.
9. **Proportions are checked first.** Before any Codex art, the existing generated interface is reworked to the new
   layout to judge the sizes of panels and elements
   ([#2452](https://github.com/Simarglok/arcanum-steamwoven/issues/2452), done; the check of the column, [#2892](https://github.com/Simarglok/arcanum-steamwoven/issues/2892), set the 2026-11-19 verdict).
10. ~~**Original is untouched.**~~ **Superseded 2026-11-19** (point 7 of the [amendment](#amendment-of-2026-11-19)): the Original look keeps its art and rules but uses the composition. Was: with the HD option off the output stays byte-identical. Still valid: until the Steamwoven kit covers
    a window, that window keeps today's HD-option drawing as the fallback (TRD SWI-RUN-3).

## Amendment of 2026-11-19

Owner verdict on the column check ([#2892](https://github.com/Simarglok/arcanum-steamwoven/issues/2892), epic [#2446](https://github.com/Simarglok/arcanum-steamwoven/issues/2446)). The full-width layout of decisions 1 to 3 read as a letterboxed film, so the **column** was tested (the original-width strips centred, the world at the sides, [#2889](https://github.com/Simarglok/arcanum-steamwoven/issues/2889) to [#2891](https://github.com/Simarglok/arcanum-steamwoven/issues/2891)). The owner accepted it for 16:9 with the original strips
(k = 1) and refused wood in the vertical gaps ("looks poor"). The decisions that replace 1, 2, 3 and 5:

1. **One centred 4:3 composition.** The Steamwoven interface is the original 800 × 600 units: top strip 41, window zone (or the world) 400, bottom strip 159, centred and scaled to the screen with `s = min(W / 800, H / 600)` (fractional). Strips and an open window together are exactly 4:3 and fill the screen's height.
2. **The sides show the world or black, never wood.** On the play screen and in windows opened over it the world is drawn to the window's edges (dimmed and desaturated under an open window); beside full-screen and two-strip menus the sides are black. There are no gap rectangles, no gap wood, no full-width panels, no full-width backdrops and no
   outward continuation of a window's material.
3. **Every aspect by scaling.** 16:9 and 16:10 natively, 21:9 and 32:9 with the world to the window's edges (no pillarbox bars), 4:3 natively full screen, narrower than 4:3 width-limited with black above and below. The 16:10 frame with bars, the 16:9 pillarbox and the thin 120 / 456 strips are retired.
4. **Master composition 2880 × 2160** inside the 3840 × 2160 reference; the baked steps ×1, ×2/3, ×1/2, ×1/3 and the run-time scale between them are unchanged (`f = s / 3.6`).
5. **Kit** at the composition's size: strips (with end caps), window backgrounds (2880 × 1440), full-screen plates (2880 × 2160), hardware and the rest; no full-width board, no outward backdrop, no gap wood. The follower group (portraits, scroll arrows, show/hide button) moves as one to the window's left edge at its original heights; the button gets a small brass backing.
6. **Open question recorded, one decided:** the world is now drawn to the window's edges while Steamwoven tests the drawn view for the out-of-sight rules (decision of 2026-11-07), so an ultrawide window sees a much wider world; whether a maximum world width is needed is open ([TRD open questions](steamwoven-interface-trd.md#open-questions)). **Decided the same day ([#2937](https://github.com/Simarglok/arcanum-steamwoven/issues/2937)):** the world zoom stays a player setting for now (the existing in-game zoom option is kept), and the composition's scale is the working reference look (the world scales with the interface and the play view in the window zone shows the same area as the original; TRD SWI-LAY-17). By the owner's report (to verify) the game runs today at a 200 % zoom at 2560 × 1440 in full screen and the owner is fully happy with it, where `s` = 2.4; the final default is to be set with the owner.
7. **The Original look uses the same composition** ([#2936](https://github.com/Simarglok/arcanum-steamwoven/issues/2936)): the HD-off interface is the original 800 × 600 screen as one centred 4:3 composition scaled with `s = min(W / 800, H / 600)`, Legacy art with sharp pixel scaling, **black** sides at every aspect, the world drawn at the composition's scale so the play view shows the original 800 × 400 area and the out-of-sight rules keep the fixed 800 × 400 view (decision 2026-11-07). This supersedes decision 10 ("Original is untouched", byte-identical output with the HD option off); art, fonts and rules of the Original look are unchanged ([TRD](steamwoven-interface-trd.md#original-look-in-the-composition)).

Work: [#2890](https://github.com/Simarglok/arcanum-steamwoven/issues/2890) already carries the scale rule for the viewer's `column` choice and [#2925](https://github.com/Simarglok/arcanum-steamwoven/issues/2925) completes it (the follower group at the window's edge and the points #2890 leaves), [#2926](https://github.com/Simarglok/arcanum-steamwoven/issues/2926) removes the full-width layout, the TRD is rewritten by [#2893](https://github.com/Simarglok/arcanum-steamwoven/issues/2893), and the full-width production batches #2472 to #2480
(closed, not planned) are replaced by #2927 to #2935; the Original look's composition is [#2936](https://github.com/Simarglok/arcanum-steamwoven/issues/2936).

## Alternatives considered

| Alternative | Outcome | Why |
| --- | --- | --- |
| Per-file redraw fitted to the original pixels (ARC-RES-094 U3) | superseded for the Steamwoven interface | geometry bound to 800 × 600; no full width; files redrawn one by one drift in tone; the pixel-fit gates measure the wrong thing once the layout is ours |
| Strip edge columns tiled at the sides (ARC-RES-093 C, ARC-RES-094 U4) | rejected | visible repetition (now moot: the sides show the world or black, 2026-11-19) |
| Generated chrome as the final look (ARC-RES-122) | rejected as the look, kept as the placeholder and fallback | the look; it stays useful for the proportions prototype and for parts the kit does not yet have |
| Density (physical-size) scale (ARC-RES-122 concept 1) | not used by the Steamwoven interface | art is authored for four fixed steps and the interface is proportional to the screen; the player's `uiScale` is an open question |
| A native 4:3 layout | **adopted 2026-11-19** as the composition itself (was: rejected, no measurable share) | the one 4:3 composition scaled to the screen is the layout at every aspect |
| A native 21:9 layout | not needed (was: rejected for now) | the composition is scaled and the world runs to the window's edges; whether the drawn world needs a maximum width is an open question |
| Original look left as it is (decision 10 of 2026-11-16) | rejected 2026-11-19 | its whole-factor strips and fitted windows were inconsistent on the 2026-11-18 comparison; it takes the same composition with sharp pixel scaling and black sides |
| Full-width panels with continued wood sides and backdrops (the 2026-11-16 decision) | rejected 2026-11-19 | it read as a letterboxed film; the owner's column check preferred the original-width strips with the world at the sides |
| Wood fill in the vertical gaps of the column (16:10, thin panels) | rejected 2026-11-19 | "looks poor"; growing the composition until it fills the height removes the gaps |
| Thin strips of 120 / 456 master px (2026-11-17) | retired 2026-11-19 | the strips keep the original 41 / 159 units so strips and a window are exactly 4:3 |

## Consequences

- **Superseded** for the Steamwoven interface by the 2026-11-16 decision: the ARC-RES-094 recommendation (U3 as the base, its geometry gates and
  the U4 side pieces), the strip tiling and the full-screen pillar-box of ARC-RES-093 section C, and the density scale
  of ARC-RES-122. Each of those notes carries a dated pointer to this ADR.
- **Still valid:** the ARC-RES-093 window census and rule readers (table A, table B), the owner decision of 2026-11-07
  that Steamwoven tests the drawn view for the out-of-sight rules (and the Original look keeps the fixed view), the Original look's art and rules, the HD pack workflow for the
  current fallback, the relit lighting, and the generated chrome code as the placeholder.
- **New work** (all in the [TRD work breakdown](steamwoven-interface-trd.md#work-breakdown)): a layout model, a
  proportions prototype, a census, a kit manifest, style anchors, a surface-method comparison, fit and bake tools, the
  runtime composition, fonts, and production batches per window-background family (replaced on 2026-11-19, see the amendment).
- **Costs and risks:** many window backgrounds at 2880 × 1440 (one per background family); generator output smaller than the master
  size; style drift between generations; text baked into original art must become drawn text; the kit is derived from original art, so it
  stays local and is not shipped (decided 2026-11-16, #2448).

## Open decisions

| Question | Issue |
| --- | --- |
| Provenance — **decided 2026-11-16**: original art is a Codex input; the kit is local only, like the HD pack | [#2448](https://github.com/Simarglok/arcanum-steamwoven/issues/2448) |
| Who runs Codex — **decided 2026-11-16**: the owner, through the inbox/outbox handoff | [#2449](https://github.com/Simarglok/arcanum-steamwoven/issues/2449) |
| Panel heights and proportions after the prototype — decided 2026-11-17: top 120 and bottom 456 master px; **retired 2026-11-19**: the original strips (41 / 159 units) | [#2453](https://github.com/Simarglok/arcanum-steamwoven/issues/2453) |
| Approval of the material sheet | [#2456](https://github.com/Simarglok/arcanum-steamwoven/issues/2456) |
| Method for the strips and window backgrounds | [#2458](https://github.com/Simarglok/arcanum-steamwoven/issues/2458) |
| World zoom in the Steamwoven look — **decided 2026-11-19**: stays a player setting; working reference: the composition's scale; the default is to be set with the owner | [#2937](https://github.com/Simarglok/arcanum-steamwoven/issues/2937) |
| The player's `uiScale` in the Steamwoven interface; behaviour above ×1 (5K screens); a maximum drawn world width on ultrawide windows; the strip master heights (150 / 570 or 148 / 572 px) | [TRD open questions](steamwoven-interface-trd.md#open-questions) |

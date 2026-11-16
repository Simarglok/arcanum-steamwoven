# ADR: Steamwoven interface — an authored 4K kit on a full-width layout

Status: **accepted by the owner on 2026-11-16** (owner thread, work item ARC-UI-2447,
[#2447](https://github.com/Simarglok/arcanum-steamwoven/issues/2447)); the open owner decisions are listed under
[Open decisions](#open-decisions) and tracked as issues. Epic:
[#2446](https://github.com/Simarglok/arcanum-steamwoven/issues/2446). Requirements and the work breakdown:
[TRD](steamwoven-interface-trd.md). Requirements touched (status unchanged):
[FR-UI-007](../requirements/ui-media-lifecycle.md#fr-ui-007),
[FR-PRODUCT-001](../requirements/content-product.md#fr-product-001),
[FR-PRODUCT-004](../requirements/content-product.md#fr-product-004).

## Context

The game has two interface looks, which the owner calls **Original** (Legacy) and **Steamwoven**:

- **Original** draws the interface as the original does (800 × 600 layout, Legacy art), with small improvements at
  most. It is the look with the HD option off and is not changed by this ADR.
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
   code at the screen's density, the interface keeping a physical size. The look did not work ("looks silly"), but it
   was useful for understanding the process, the layout and the parts.

The owner's Codex redraw experiments showed promise; the open problem is keeping the style consistent (tone and
texture of the wood backgrounds).

## Decision

1. **Full-width layout.** The top and bottom panels span the whole width of the frame. The centre block keeps the
   original 800-unit layout; the sides continue **the same material, tone and texture** as the centre (wood in most
   places). At this stage the sides carry no controls and no decor, only the material.
2. **An open menu fills the whole play area.** A menu window's backdrop fills the whole middle band (full width and
   height between the panels). Where menus have different background materials, more full-size backdrops are made,
   each from its own original background. A window built from two backgrounds (inventory, loot, barter) continues each
   half outwards with its own material.
3. **Three bands of one material.** The panel material is one continuous board for the top and bottom panels; the
   middle band holds the world or a menu backdrop. Panel hardware (sockets, rings, vial housings, the message window
   frame, rails) is a separate layer over the board, not baked into it.
4. **Authored at 3840 × 2160, baked down.** Every part is authored as a master for 3840 × 2160 and baked to four steps:
   ×1 (3840), ×2/3 (2560), ×1/2 (1920), ×1/3 (1280). 16:10 screens of the same width use the same step; the extra
   height goes to the middle band. Other sizes take the nearest larger step and scale down at run time. The interface
   size is proportional to the screen (not a physical size).
5. **Aspect range.** 16:10 to 16:9 is laid out natively. Wider screens (21:9, 32:9) get a 16:9 frame with black bars at
   the sides. Narrower screens (4:3, 5:4) get a 16:10 frame with black bars at the top and bottom; a player who wants
   the native 4:3 look has the Original look. Rationale: in the Steam survey of October 2026 no 4:3 resolution has a row
   of its own, 16:10 together is about 11 %, and the ultrawide resolutions together about 4 % (TRD
   [Screen frame and scale](steamwoven-interface-trd.md#screen-frame-and-scale)).
6. **A kit of parts.** The interface is composed of parts: materials (wood board, paper, parchment, …), backdrops,
   hardware (brass rings, sockets, frames), button plates, glyphs on transparent backgrounds, button state overlays,
   sheets (newspaper, letter, book page), the schematic grid as its own layer, item pictures on transparent backgrounds
   for the schematic, gauges and decor. A part used in several windows is one part at one size and style. Text is never
   baked into art; it is drawn with fonts.
7. **All art is redrawn by Codex image generation.** The style is held by: a census of the original interface
   (screenshots, element extraction, reuse); whole-screen context images first (as in the current HD pack) so Codex sees
   each part's purpose; a material sheet the owner approves; prompts per part kind; a fit tool that locks every part to
   the approved materials.
8. **Large surfaces are a research item.** How to get 3840-wide boards and backdrops from a generator with a smaller
   output (upscale, tiled regeneration, material texture plus macro image) is decided by comparison
   ([#2457](https://github.com/Simarglok/arcanum-steamwoven/issues/2457)). Regenerating tiles separately is expected to
   leave visible differences at the joins, so it must prove itself.
9. **Proportions are checked first.** Before any Codex art, the existing generated interface is reworked to the new
   layout to judge the sizes of panels and elements
   ([#2452](https://github.com/Simarglok/arcanum-steamwoven/issues/2452)).
10. **Original is untouched.** With the HD option off the output stays byte-identical. Until the Steamwoven kit covers
    a window, that window keeps today's HD-option drawing as the fallback.

## Alternatives considered

| Alternative | Outcome | Why |
| --- | --- | --- |
| Per-file redraw fitted to the original pixels (ARC-RES-094 U3) | superseded for the Steamwoven interface | geometry bound to 800 × 600; no full width; files redrawn one by one drift in tone; the pixel-fit gates measure the wrong thing once the layout is ours |
| Strip edge columns tiled at the sides (ARC-RES-093 C, ARC-RES-094 U4) | rejected | visible repetition; the sides must be the same painted material, not a repeated slice |
| Generated chrome as the final look (ARC-RES-122) | rejected as the look, kept as the placeholder and fallback | the look; it stays useful for the proportions prototype and for parts the kit does not yet have |
| Density (physical-size) scale (ARC-RES-122 concept 1) | not used by the Steamwoven interface | art is authored for four fixed steps and the interface is proportional to the screen; the player's `uiScale` is an open question |
| A native 4:3 layout | rejected | no measurable share; Original covers the 4:3 look |
| A native 21:9 layout | rejected for now | about 4 % share; black bars are cheap; can be reopened |

## Consequences

- **Superseded** for the Steamwoven interface: the ARC-RES-094 recommendation (U3 as the base, its geometry gates and
  the U4 side pieces), the strip tiling and the full-screen pillar-box of ARC-RES-093 section C, and the density scale
  of ARC-RES-122. Each of those notes carries a dated pointer to this ADR.
- **Still valid:** the ARC-RES-093 window census and rule readers (table A, table B), the owner decision of 2026-11-07
  that Steamwoven tests the drawn view for the out-of-sight rules, the Original look, the HD pack workflow for the
  current fallback, the relit lighting, and the generated chrome code as the placeholder.
- **New work** (all in the [TRD work breakdown](steamwoven-interface-trd.md#work-breakdown)): a layout model, a
  proportions prototype, a census, a kit manifest, style anchors, a surface-method comparison, fit and bake tools, the
  runtime composition, fonts, and production batches per backdrop family.
- **Costs and risks:** many full-size backdrops (one per background family); generator output smaller than the master
  size; style drift between generations; text baked into original art must become drawn text; provenance of
  Codex outputs decides whether the kit can ship (below).

## Open decisions

| Question | Issue |
| --- | --- |
| Provenance: may Codex receive original art as input (kit local only, like the HD pack) or only our own references (kit can ship)? | [#2448](https://github.com/Simarglok/arcanum-steamwoven/issues/2448) |
| Panel heights and proportions after the prototype | [#2453](https://github.com/Simarglok/arcanum-steamwoven/issues/2453) |
| Approval of the material sheet | [#2456](https://github.com/Simarglok/arcanum-steamwoven/issues/2456) |
| Method for the panel board and backdrops | [#2458](https://github.com/Simarglok/arcanum-steamwoven/issues/2458) |
| The player's `uiScale` in the Steamwoven interface; behaviour above ×1 (5K screens); vertical place of the window zone on 16:10 | [TRD open questions](steamwoven-interface-trd.md#open-questions) |

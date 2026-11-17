# TRD: Steamwoven interface — layout, kit, pipeline and work breakdown

Status: **requirements for implementation, 2026-11-16** (work item ARC-UI-2447). The decision and its reasons are in
the [ADR](steamwoven-interface-adr.md); this page says what to build and how it is checked. Epic:
[#2446](https://github.com/Simarglok/arcanum-steamwoven/issues/2446). Every requirement has an id `SWI-<area>-<n>`;
issues and PRs cite the ids. Values marked *provisional* are fixed by the named owner decision. Nothing here is
implemented yet.

## Terms

| Term | Meaning |
| --- | --- |
| Master | A part as authored for the 3840 × 2160 reference screen, in master pixels (px). |
| Step | One of the four baked sets: ×1, ×2/3, ×1/2, ×1/3 of the masters. |
| Frame | The rectangle of the window the interface is laid out in (the window minus any black bars). |
| `f` | Device pixels per master pixel for the current frame. |
| UI unit | One pixel of the original 800 × 600 layout. In the master, 1 unit = 3.6 px. |
| Band | Top panel, middle band (world or menu backdrop), bottom panel. |
| Centre block | The 2880 px (800 units) wide part of a band that keeps the original layout. |
| Sides | The parts of a band left and right of the centre block. |
| Family | A group of menus whose backgrounds share one material and so one backdrop. |
| Original look / Steamwoven look | The interface with the HD option off / on ([ADR](steamwoven-interface-adr.md#context)). |

## Screen frame and scale

- **SWI-LAY-1** The reference screen is 3840 × 2160. For a window of W × H device pixels,
  `f = min(W / 3840, H / 2160)`.
- **SWI-LAY-2** An aspect from 16:10 to 16:9 inclusive is laid out natively: the frame is the whole window.
- **SWI-LAY-3** A narrower window (4:3, 5:4) gets a frame of W × round(2400 · f), centred, black bars at the top and
  bottom. A wider window (21:9, 32:9) gets a frame of round(3840 · f) × H, centred, black bars at the sides. A bar
  under 4 px per side is dropped: the frame is the whole window and the sides take the difference.
- **SWI-LAY-4** The baked step is the smallest of ×1, ×2/3, ×1/2, ×1/3 that is ≥ `f`. Above ×1 (5K screens) the ×1
  set is scaled up (a recorded limit, see [open questions](#open-questions)). Below ×1/3 the ×1/3 set is scaled down
  to `f` = 0.25; below 0.25 the Steamwoven interface is not supported and the Original look is used.
- **SWI-LAY-5** The run-time scale is `f / step`. `f`, the step choice and the test for an exact step are computed
  exactly (integers or rationals, for example `3 · W ≥ 2 · 3840`), never with floating-point equality. When it is exactly 1 the parts are drawn at whole device pixels with
  no filtering; otherwise with linear filtering from the baked set.
- **SWI-LAY-6** W × H are device (backing) pixels, including the macOS backing scale.
- **SWI-LAY-7** Every master length and offset is a multiple of 6 px, so it is a whole number of pixels at every step
  (6 px = 4, 3 and 2 px). A device position is `round(master position · f)`, computed per edge (not per size), so
  neighbouring parts share their edges; halves round up. The two panel heights are the exception: each is `round(height · f)`
  from the frame's own top or bottom edge, as the reference table below shows (1600 × 900: bottom panel 238, middle 599).

Reference table (band heights at the default values of SWI-LAY-10; "bars" are per side, horizontal / vertical). The layout
model's unit tests use these numbers ([#2450](https://github.com/Simarglok/arcanum-steamwoven/issues/2450)):

| Window | Frame | `f` | Step | Run-time scale | Top | Middle | Bottom | Centre | Side | Bars |
| --- | --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 3840 × 2160 | 3840 × 2160 | 1.0000 | 1 | 1.000 | 120 | 1584 | 456 | 2880 | 480 | 0 / 0 |
| 2560 × 1440 | 2560 × 1440 | 0.6667 | 2/3 | 1.000 | 80 | 1056 | 304 | 1920 | 320 | 0 / 0 |
| 1920 × 1080 | 1920 × 1080 | 0.5000 | 1/2 | 1.000 | 60 | 792 | 228 | 1440 | 240 | 0 / 0 |
| 1280 × 720 | 1280 × 720 | 0.3333 | 1/3 | 1.000 | 40 | 528 | 152 | 960 | 160 | 0 / 0 |
| 3840 × 2400 | 3840 × 2400 | 1.0000 | 1 | 1.000 | 120 | 1824 | 456 | 2880 | 480 | 0 / 0 |
| 2560 × 1600 | 2560 × 1600 | 0.6667 | 2/3 | 1.000 | 80 | 1216 | 304 | 1920 | 320 | 0 / 0 |
| 1920 × 1200 | 1920 × 1200 | 0.5000 | 1/2 | 1.000 | 60 | 912 | 228 | 1440 | 240 | 0 / 0 |
| 1280 × 800 | 1280 × 800 | 0.3333 | 1/3 | 1.000 | 40 | 608 | 152 | 960 | 160 | 0 / 0 |
| 3024 × 1964 | 3024 × 1890 | 0.7875 | 1 | 0.787 | 95 | 1436 | 359 | 2268 | 378 | 0 / 37 |
| 3456 × 2234 | 3456 × 2160 | 0.9000 | 1 | 0.900 | 108 | 1642 | 410 | 2592 | 432 | 0 / 37 |
| 2880 × 1800 | 2880 × 1800 | 0.7500 | 1 | 0.750 | 90 | 1368 | 342 | 2160 | 360 | 0 / 0 |
| 1366 × 768 | 1366 × 768 | 0.3556 | 1/2 | 0.711 | 43 | 563 | 162 | 1024 | 171 | 0 / 0 |
| 1600 × 900 | 1600 × 900 | 0.4167 | 1/2 | 0.833 | 50 | 660 | 190 | 1200 | 200 | 0 / 0 |
| 3440 × 1440 | 2560 × 1440 | 0.6667 | 2/3 | 1.000 | 80 | 1056 | 304 | 1920 | 320 | 440 / 0 |
| 5120 × 1440 | 2560 × 1440 | 0.6667 | 2/3 | 1.000 | 80 | 1056 | 304 | 1920 | 320 | 1280 / 0 |
| 1024 × 768 | 1024 × 640 | 0.2667 | 1/3 | 0.800 | 32 | 486 | 122 | 768 | 128 | 0 / 64 |
| 5120 × 2880 | 5120 × 2880 | 1.3333 | 1 | 1.333 | 160 | 2112 | 608 | 3840 | 640 | 0 / 0 |

The two MacBook rows (3024 × 1964, 3456 × 2234) are narrower than 16:10 and get bars of 37 px; in macOS full screen
the area beside the camera notch is usually left out, which may make the window 16:10 already (to check on the
device, not a requirement).

Why this range (Steam Hardware Survey, primary display, October 2026, read 2026-11-16 from
[store.steampowered.com/hwsurvey](https://store.steampowered.com/hwsurvey/Steam-Hardware-Software-Survey-Welcome-to-Steam)):
1920 × 1080 50.23 %, 2560 × 1440 21.82 %, 2560 × 1600 5.46 %, 3840 × 2160 5.05 %, 3440 × 1440 3.23 %,
1920 × 1200 2.97 %, 1366 × 768 2.25 %; 16:10 resolutions together about 11 %; 2560 × 1080, 3440 × 1440 and
5120 × 1440 together about 4.3 %; no 4:3 resolution is listed (all below the 3.30 % "Other").

## Play screen layout

- **SWI-LAY-10** Three bands: top panel 120 px, bottom panel 456 px (master; **owner decision 2026-11-17**,
  [#2453](https://github.com/Simarglok/arcanum-steamwoven/issues/2453), after the [prototype](#prototype-of-the-play-screen-arc-ui-2452)'s comparison of this pair with the original strips' own 150 and 570: the original's
  41 and 159 units × 3.6 = 147.6 and 572.4, rounded to the grid), and the middle band = frame height − top − bottom. Both heights are multiples of 6 and together at most 720, so the window zone of SWI-BDR-2 always fits
  a 16:9 middle band; at 120 and 456 the middle band is 1584 px at 16:9 and 1824 px at 16:10, and the zone has 72 px above and below it at 16:9. The layout model's options hold the pair in one place.
- **SWI-LAY-11** Each panel spans the whole frame width. The centre block (2880 px) is centred; the sides take the
  rest. Controls in the centre block sit at their original offsets × 3.6, snapped to the 6 px grid (shift at most
  3 px, under one unit).
- **SWI-LAY-12** A panel is the panel board (one continuous material for both panels) plus hardware layers (sockets,
  rings, vial housings, the message window frame, rails, buttons). The sides show the same board: same material, tone
  and texture, no visible join at the centre block's edges (measured by SWI-VER-5). At this stage the sides have no controls and no decor.
- **SWI-LAY-13** Hit areas are the rectangles (or circles) of the drawn parts from the layout, not the original's
  pixel masks.
- **SWI-LAY-14** With no menu open the middle band is the world view. The out-of-sight rules follow the owner decision
  of 2026-11-07 ([decisions](../architecture/decisions.md), "Out-of-sight rules and the view": Steamwoven tests the
  drawn view).
- **SWI-LAY-15** No text is baked into any part; all text is drawn with fonts
  ([#2462](https://github.com/Simarglok/arcanum-steamwoven/issues/2462)).

## Prototype of the play screen (ARC-UI-2452)

Slice [#2452](https://github.com/Simarglok/arcanum-steamwoven/issues/2452): the viewer lays the play screen out by the layout model with the **generated chrome** as the placeholder, to judge sizes and proportions before any
Codex art. It is on while the HD option and the generated interface are both on (`AreaGameBarPanel.SteamwovenOn`); with the HD option off, or with the generated interface off, the whole-factor layout is unchanged (the strips and the
bars' captures are pixel-identical to `origin/main`; only the changing text of the diagnostic palette differs).

- **Scale.** The two strips keep the original's 800-unit layout and are drawn at one fractional scale, `f · 3.6 · k` (`SteamwovenHud.Plan`); `k` is 1 for the original strips' own heights (150 and 570 master px) and the smaller of
  `top / 150` and `bottom / 570` for another pair (0.8 for 120 and 456, the owner's choice), so thinner panels give a smaller centre block (2304 px) and wider sides, not a squashed strip: the prototype's stand-in for art that will be drawn to fit the panel. The top strip stands on the lower edge of the top band and the bottom strip hangs
  from the upper edge of the bottom band, so each strip's rim meets the board's rim. At k = 1 a 41-unit strip is 147.6 · f px in a 150 · f band (the board shows above it) and a 159-unit strip is 572.4 · f px in a 570 · f band (its last
  2.4 · f px lie beyond the frame's bottom edge); at k = 0.8 they are 118.1 · f in 120 · f and 457.9 · f in 456 · f.
- **Boards.** The band beside each strip is a generated board of the strip's own wood (colours tuned from the strip frame, grain continued from the strip's position, the rim on the edge facing the world), painted off
  the main thread (`InterfaceChromeRenderer.RenderBoard`, `SteamwovenBoardView`). The world is still drawn behind the panels over the whole frame, as in the original; the bars of a letterboxed or pillarboxed
  window are black rectangles over it.
- **Menus.** `AreaGameBarPanel.TopInset` and `BottomInset` are now the middle band's, so every window that docks between the strips docks in it (windows keep their own whole or fitted scale until
  [#2461](https://github.com/Simarglok/arcanum-steamwoven/issues/2461) integrates each). While a menu window is open a generated dark-wood backdrop fills the whole middle band (layer 3, under the windows).
- **Not yet laid out by the model** (follow-ups of the per-window integration): the full-screen menus (SWI-BDR-6), the two-part split backdrops (SWI-BDR-5), the barter and loot windows, which hide the bars, the 800 × 600
  windows' own scale (the inventory stays at its fitted scale on the generated backdrop), the follower portraits and the floaters (still placed on the whole-factor screen), and the sleep and fate views (their textures take the
  strips' fractional scale while the bars show). The windows' free width is still the whole window (a window wider than the 16:9 frame can lie under a pillarbox bar). The strips are not snapped to the 6 px grid and are sampled
  Linear; the boards are painted at the logical scale (the prototype assumes a content scale of 1). The backdrop is for the menu windows, not for the dialog or sleep.
- **Smoke.** `./arcanum smoke pr --stages bar --env ARC_AREA_BAR_STEAMWOVEN=1 --env ARC_HD_PACK=<pack> --env ARC_AREA_BAR_CAPTURE=<prefix>` checks the plan, the strips' places, the insets, the boards, the bars and the open-menu
  backdrop at the SWI-VER-6 sizes (`ARC_STEAMWOVEN_SIZES=WxH,...` to choose others) for the variants of `ARC_STEAMWOVEN_VARIANTS` (`thin` = the default 120 and 456, `original` = 150 and 570, `before` = the old layout, `TOP-BOTTOM` master px), without
  resizing the window (the root viewport takes the size as its logical render target), and saves `<prefix>-hud-steamwoven-VARIANT-WxH[-menu].png` (local, never in git). The older generated-interface smokes run with the layout
  suspended (`SteamwovenHud.Suspended`): they measure the whole-factor geometry.

## Menu backdrops

- **SWI-BDR-1** An open menu window fills the whole middle band (full width and height) with its backdrop.
- **SWI-BDR-2** The window zone, 2880 × 1440 px (the original 800 × 400 units), is centred in the middle band
  (vertical placement: [open questions](#open-questions)); window content (paper doll, grids, pages, buttons) sits in
  it at the original offsets × 3.6, snapped to the grid.
- **SWI-BDR-3** A backdrop master is 3840 × 1824 px (the middle band of a 16:10 frame at ×1: 2400 − 120 − 456). A shorter middle band
  shows its centred crop (16:9: 1584 px). A middle band a few pixels taller than 16:10 allows (a bar under 4 px dropped by
  SWI-LAY-3) gets the backdrop scaled uniformly to cover it, centred.
- **SWI-BDR-4** Menus whose original backgrounds share a material share one backdrop (a family). The families are
  fixed by the census ([#2451](https://github.com/Simarglok/arcanum-steamwoven/issues/2451)): eight families (BD-A to BD-H) and the
  panel board, listed in the [census](steamwoven-interface-census.md#backdrop-families).
- **SWI-BDR-5** A two-part window (inventory, loot, barter: left 358 units, right 442 units; the census finds two more, the logbook 165 + 635 and the reading window 132 + 668, [census](steamwoven-interface-census.md#two-part-windows-and-full-screen-menus)) splits at 1290 px from the
  centre block's left edge (358 × 3.6 = 1288.8, snapped). The left material runs from the frame's left edge to the
  split, the right material from the split to the frame's right edge.
- **SWI-BDR-6** A full-screen menu (an original 800 × 600 background: main menu, options and the others the census
  finds) has a 3840 × 2400 master that covers the whole frame, panels included; a shorter frame shows the centred
  crop, a slightly taller one (SWI-LAY-3) the master scaled to cover. Its 800 × 600 zone is 2880 × 2160 px, centred.
- **SWI-BDR-7** Each backdrop is made from its own original background: the centre after the original, the sides
  continued outwards from the centre's edges in the same material, with no visible join (measured by SWI-VER-5).

## Asset kit

Part kinds:

| Kind | What | Examples |
| --- | --- | --- |
| material | a surface, seamless where it must be | wood, dark wood, brass, iron, paper, parchment |
| board | the panel board, one continuous image for both panels | wood of the top and bottom panels |
| backdrop | the full middle band (or full frame) of a family | inventory left/right, logbook, schematic paper |
| hardware | fixed metal or frame pieces over a board or backdrop | brass rings, sockets, vial housings, rails, frames |
| plate | a button's body | round and square plates by size |
| glyph | a symbol on a transparent background | button symbols |
| state | an overlay for a button state | hover, pressed, disabled |
| sheet | a document laid over a backdrop | newspaper, letter, book page, telegram, plaque |
| grid | a grid as its own layer | the schematic grid, inventory cells |
| picture | an image on a transparent background | items on the schematic, item and spell icons, portraits |
| gauge | layered gauge parts; the fill comes from the value | vial glass, housing, liquid |
| decor | ornaments that are not hardware | corner pieces, rivets |
| cursor | pointer art | the cursor set |

- **SWI-KIT-1** Every part is a row of the kit manifest `tools/steamwoven_ui/kit.json`
  ([#2454](https://github.com/Simarglok/arcanum-steamwoven/issues/2454)): id, kind, master size, anchor, layers,
  states, used-by windows, source original art ids (identifiers only), family, batch, provenance record id, status.
  The manifest holds no pixels.
- **SWI-KIT-2** A master is a PNG, RGBA 8 bit, sRGB, straight alpha, with sides that are multiples of 6 px.
- **SWI-KIT-3** A part used in several windows (a reuse group of the census) is one part at one master size.
- **SWI-KIT-4** A part with transparency has a clean edge: no halo of the generator's background colour (checked by
  the fit tool, SWI-VER-3).
- **SWI-KIT-5** A button is plate + ring + glyph + state overlays. The states of one button size (idle, hover, pressed,
  disabled) are generated together on one sheet, so they match.
- **SWI-KIT-6** A gauge is glass, housing and liquid as layers; the fill level is drawn from the value, not from a frame
  sequence.
- **SWI-KIT-7** Order of production: board and backdrops, then buttons and glyphs, sheets, the schematic, gauges and
  decor; icons, portraits and cursors last (the owner: all interface art is redrawn).

## Generation pipeline

| Step | Who | Output | Where it lives |
| --- | --- | --- | --- |
| 1. Census | agent ([#2451](https://github.com/Simarglok/arcanum-steamwoven/issues/2451)) | [census note](steamwoven-interface-census.md), screenshots | note in git, screenshots in `local/steamwoven-ui/census/` |
| 2. Kit manifest | agent ([#2454](https://github.com/Simarglok/arcanum-steamwoven/issues/2454)) | `kit.json`, validator | git |
| 3. Context images | owner in Codex ([#2455](https://github.com/Simarglok/arcanum-steamwoven/issues/2455)) | whole-screen redraws of key screens | local |
| 4. Material sheet | owner in Codex, owner approves ([#2456](https://github.com/Simarglok/arcanum-steamwoven/issues/2456)) | wood, brass, paper, … | local |
| 5. Parts | owner in Codex, per batch | raw generations | `local/steamwoven-ui/outbox/` |
| 6. Fit | agent tool ([#2459](https://github.com/Simarglok/arcanum-steamwoven/issues/2459)) | masters | `local/steamwoven-ui/masters/` |
| 7. Look check | owner | per-batch composite | thread only |
| 8. Bake | agent tool ([#2460](https://github.com/Simarglok/arcanum-steamwoven/issues/2460)) | four step sets | per SWI-PRV |
| 9. Runtime | agent ([#2461](https://github.com/Simarglok/arcanum-steamwoven/issues/2461)) | composed interface | code in git |

- **SWI-GEN-1** Context images come first: whole-screen redraws of the key screens (HUD, inventory, character,
  logbook, schematic, one reading window, main menu) at the frame size, so Codex sees what each part is for. Parts are
  then generated with a crop of the context image as reference.
- **SWI-GEN-2** The approved material sheet freezes the tones and textures; every later part is generated against it
  and colour-locked to it.
- **SWI-GEN-3** A part's references are the context crop and the material sheet, plus the original art of the part
  (crop, frame or screenshot): original art is given to Codex as input (owner decision 2026-11-16,
  [#2448](https://github.com/Simarglok/arcanum-steamwoven/issues/2448)). Its prompt
  comes from the prompt library `tools/steamwoven_ui/prompts/` (text only).
- **SWI-GEN-4** A generation becomes a master only through the fit tool (size, grid, alpha clean-up, low-frequency
  colour lock, provenance record).
- **SWI-GEN-5** The board and the backdrops are made by the method the owner picks
  ([#2458](https://github.com/Simarglok/arcanum-steamwoven/issues/2458)) from the comparison
  ([#2457](https://github.com/Simarglok/arcanum-steamwoven/issues/2457)).
- **SWI-GEN-6** Every generated image has a provenance record: generator and version, date, prompt id and the hash of
  its text, reference ids, who ran it.
- **SWI-GEN-7** The owner runs Codex image generation (owner decision 2026-11-16, [#2449](https://github.com/Simarglok/arcanum-steamwoven/issues/2449)); agents never
  send anything to Codex. Handoff: the agent puts a batch's prompts, references and a short list of what to generate into
  `local/steamwoven-ui/inbox/<batch>/`, asks the owner in the thread, and the owner puts the results into
  `local/steamwoven-ui/outbox/<batch>/`; the agent then writes the provenance records (SWI-GEN-6) and runs the fit tool.

## Bake and runtime

- **SWI-RUN-1** The bake makes the four step sets from the masters, resampled in linear light with premultiplied
  alpha; every baked length is exactly the master length times the step; two runs give identical bytes; each set has
  a manifest with sizes and hashes.
- **SWI-RUN-2** The runtime takes the step from SWI-LAY-4 and composes the layers of each part (board, hardware,
  plates, glyphs, states).
- **SWI-RUN-3** A missing part falls back to the generated chrome
  ([ARC-RES-122](../rendering/hd-interface-density.md)), so the HD option always draws a complete interface.
- **SWI-RUN-4** With the HD option off the output is byte-identical to today.
- **SWI-RUN-5** Only the backdrop of the open window is resident (a ×1 backdrop is about 25.8 MB as raw RGBA); the
  board and the HUD hardware stay loaded.

## Provenance and storage

- **SWI-PRV-1** Owner decision 2026-11-16 ([#2448](https://github.com/Simarglok/arcanum-steamwoven/issues/2448)): original art is given to
  Codex as input, so every generated image, master and baked set is derived from the original: it stays under the ignored
  `local/steamwoven-ui/`, is never committed and never packaged, and follows the
  [HD pack workflow](../workflow/hd-pack.md) for backup. In git: tools, the manifest (identifiers only), the prompt
  library (text) and notes with numbers.
- **SWI-PRV-2** The kit therefore exists only on the owner's devices (and those of a player who builds it from their own
  installation), like the HD pack; it is not shipped with the game. Reopening this needs a new owner decision.
- **SWI-PRV-3** Composites for the owner are posted in the thread and never committed (owner rule 2026-11-11).

## Verification

Agent-checkable (acceptance criteria of the slices):

- **SWI-VER-1** Layout model unit tests reproduce the [reference table](#screen-frame-and-scale) exactly.
- **SWI-VER-2** The manifest validator passes on the real manifest and fails each synthetic bad manifest.
- **SWI-VER-3** Fit tool tests on synthetic fixtures: exact output size; no halo (in the semi-transparent edge band the
  mean colour difference to the nearest opaque interior is within the bound the tool documents); the colour lock
  leaves the high-frequency energy within 10 % of the input.
- **SWI-VER-4** Bake: exact sizes and identical bytes on a second run.
- **SWI-VER-5** Surfaces: seam ratio R ≤ 1.5 at every join (the measure of
  [ARC-RES-094 U4](../rendering/hd-interface-art.md#9-slice-side-pieces-u4)); side-versus-centre colour and texture
  statistics within the bounds that [#2457](https://github.com/Simarglok/arcanum-steamwoven/issues/2457) fixes; until
  then only the seam ratio is a gate.
- **SWI-VER-6** Smoke captures (local only) at 3840 × 2160, 2560 × 1440, 1920 × 1080, 2560 × 1600, 1920 × 1200,
  1280 × 800, 1366 × 768, 3440 × 1440 and 1024 × 768.
- **SWI-VER-7** Byte-identical output with the HD option off (SWI-RUN-4).

Owner gates (separate issues, never acceptance criteria): proportions
[#2453](https://github.com/Simarglok/arcanum-steamwoven/issues/2453), material sheet
[#2456](https://github.com/Simarglok/arcanum-steamwoven/issues/2456), surface method
[#2458](https://github.com/Simarglok/arcanum-steamwoven/issues/2458), and the look of each production batch (named by
its issue).

## Work breakdown

One issue is one work item ([Definition of Ready](../workflow/tasks.md#definition-of-ready)); the issues carry the
acceptance criteria. Start any issue that is `status:ready`; the order below is the dependency order.

| Phase | Issue | Result | Blocked by |
| --- | --- | --- | --- |
| 0. Decisions | [#2447](https://github.com/Simarglok/arcanum-steamwoven/issues/2447) | this TRD and the ADR | — |
| 0. Decisions | [#2448](https://github.com/Simarglok/arcanum-steamwoven/issues/2448) | owner: provenance and distribution — decided: originals are Codex inputs, kit local only | — |
| 0. Decisions | [#2449](https://github.com/Simarglok/arcanum-steamwoven/issues/2449) | Codex runner — decided: the owner runs Codex (closed, not planned) | — |
| 1. Geometry | [#2450](https://github.com/Simarglok/arcanum-steamwoven/issues/2450) | layout model (SWI-LAY, SWI-BDR) | — |
| 1. Geometry | [#2452](https://github.com/Simarglok/arcanum-steamwoven/issues/2452) | prototype with generated chrome ([done](#prototype-of-the-play-screen-arc-ui-2452)) | #2450 |
| 1. Geometry | [#2453](https://github.com/Simarglok/arcanum-steamwoven/issues/2453) | owner: panel proportions — decided 2026-11-17: 120 / 456 | #2452 |
| 2. Census | [#2451](https://github.com/Simarglok/arcanum-steamwoven/issues/2451) | census, families, production issues | — |
| 2. Census | [#2454](https://github.com/Simarglok/arcanum-steamwoven/issues/2454) | kit manifest and validator | #2451 |
| 3. Style | [#2455](https://github.com/Simarglok/arcanum-steamwoven/issues/2455) | context images, material sheet, prompts | #2451 |
| 3. Style | [#2456](https://github.com/Simarglok/arcanum-steamwoven/issues/2456) | owner: material sheet | #2455 |
| 3. Style | [#2457](https://github.com/Simarglok/arcanum-steamwoven/issues/2457) | 4K surface method comparison | #2456 |
| 3. Style | [#2458](https://github.com/Simarglok/arcanum-steamwoven/issues/2458) | owner: surface method | #2457 |
| 4. Tools | [#2459](https://github.com/Simarglok/arcanum-steamwoven/issues/2459) | fit tool | #2454 |
| 4. Tools | [#2460](https://github.com/Simarglok/arcanum-steamwoven/issues/2460) | bake tool and storage | #2454 |
| 5. Runtime | [#2461](https://github.com/Simarglok/arcanum-steamwoven/issues/2461) | runtime composition, window integration issues | #2450, #2460 |
| 5. Runtime | [#2462](https://github.com/Simarglok/arcanum-steamwoven/issues/2462) | fonts at the four steps | #2450, #2451 |
| 6. Production | [#2472](https://github.com/Simarglok/arcanum-steamwoven/issues/2472) to [#2487](https://github.com/Simarglok/arcanum-steamwoven/issues/2487) (created by #2451, [batch table](steamwoven-interface-census.md#production-batches)) | one batch per family or part group | fit, bake, material sheet (backdrops also the surface method) |
| 6. Integration | created by #2461 | one issue per window | runtime, its batch |

Ready now: #2450; the census (#2451) is done and unblocks #2454 and #2455 (#2462 still waits for #2450), and follow-ups #2489 (capture hooks) and #2490 (open facts).

## Open questions

- How the controls of the centre block fit the thinner panels: SWI-LAY-11 puts them at the original offsets × 3.6, which the 120 and 456 px bands cannot hold (the original strips are 147.6 and 572.4 px high at that scale),
  so the kit's hardware is drawn for these bands (vertical offsets compressed) or the centre block is scaled by `k` as in the prototype (2304 px); to settle in the kit design ([#2454](https://github.com/Simarglok/arcanum-steamwoven/issues/2454), [#2461](https://github.com/Simarglok/arcanum-steamwoven/issues/2461)).
- The player's `uiScale` setting (50–300 %) in the Steamwoven look: ignored (the interface is proportional by design)
  or a multiplier on `f` with the run-time scale absorbing it.
- Screens above ×1 (5K): scale the ×1 set up (current requirement) or add a ×4/3 set.
- Vertical place of the window zone in a taller (16:10) middle band: centred (current requirement) or right under the
  top panel as in the original.

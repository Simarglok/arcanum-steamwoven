# Census of interface states and animations for the Steamwoven kit (ARC-UI-2971)

Status: **census, 2026-11-19** ([#2971](https://github.com/Simarglok/arcanum-steamwoven/issues/2971), epic
[#2446](https://github.com/Simarglok/arcanum-steamwoven/issues/2446); the 4:3 kit plan
[#2893](https://github.com/Simarglok/arcanum-steamwoven/issues/2893)). Docs-only; nothing in the viewer changes. It lists every
interface art that has more than one frame or changes with input, a value or time, so that the kit
([TRD asset kit](steamwoven-interface-trd.md#asset-kit)) reproduces it with the **same frames, frame order and timing**. The kit
principle (owner, 2026-11-19): a repeated element is generated once; the states of one element are generated together on one
sheet, or derived from one generated base by a fixed rule, never as unrelated generations. Requirements touched (status
unchanged): [FR-UI-002](../requirements/ui-media-lifecycle.md#fr-ui-002), [FR-UI-004](../requirements/ui-media-lifecycle.md#fr-ui-004),
[FR-UI-007](../requirements/ui-media-lifecycle.md#fr-ui-007).

The per-art table is [`steamwoven-interface-states.tsv`](steamwoven-interface-states.tsv) (248 arts: identifiers, sizes, frame
meanings, triggers, timing, viewer difference, ledger rows, kit proposal; no pixels). This note holds the rules, the classes, the
arts that change without extra frames, the differences from the original and the kit proposal.

## Method and sources

- **Frames and sizes**: the project's ART reader over the player's installed `art\interface` (metadata only; the table of
  [`steamwoven-interface-census-arts.tsv`](steamwoven-interface-census-arts.tsv) and the local `local/steamwoven-ui/census/arts.tsv`
  for the frame counts, per-frame sizes and the ART header's fps).
- **What a frame is and what switches it**: the [verification ledger](../research/ledger/ui-audio.md) of the retail 1.0.7.4
  `Arcanum.exe` (rows cited by id in the table: OF9 and OF10 give the button rule, CU5 the cursor timer, OH4 and OH21 the hover
  rings, PA1 to PA3 and CH7 the vials, CHW5 and CHW20 the sheet, GB8 to GB10 the clock). Where the ledger has no row the table says
  **F0** (no reader found) and the frame meaning is the one read from the frames themselves.
- **Frames looked at** on local contact sheets (never committed): 2026-11-19, by eye and by pixel statistics. One new executable
  read, **F0, one reader** (a text search of the decompiled export for the art builder's immediate arguments, then
  `0x4BFCE0`, `0x4BFE00`, `0x4C0140`; position helper `0x4C0280`): the waypoint overlay of art 672 (below).
- **Left out**: the 33 bitmap font arts (225 frames each are glyphs by character code, not states; slice
  [#2462](https://github.com/Simarglok/arcanum-steamwoven/issues/2462)) and the network play arts (out of scope,
  [census](steamwoven-interface-census.md)).

## Summary

| Class | Arts | What it is |
| --- | ---: | --- |
| `BTN3` | 64 | button plate, 3 frames: idle, pressed, hover |
| `BTN2` | 49 | button plate, 2 frames: idle, pressed (no hover look); includes 2 arts the ledger shows unused and 8 extra logbook tabs |
| `BTN4` | 6 | character sheet plus/minus, 4 frames, frame 3 never drawn |
| `BTN2T` | 1 | toggle (`TextToggle`): up, down (down = info hidden) |
| `LIT` | 4 | the lit face of a top strip button: replaces the idle art |
| `TAB3` | 16 | the college tabs of the spells page, 3 frames |
| `ICON2` | 90 | 80 spell icons, 8 skill icons and 2 small skill icons, 2 frames (raised, pressed bevel) |
| `MARK2` | 4 | world map markers, 2 frames, frame 0 only drawn |
| `VIAL` | 2 | health and fatigue vials, 15 frames, frame 0 only drawn |
| `VALUE` | 1 | alignment needle, 21 frames, frame set by the value |
| `ANIM` | 4 | cursor and hover rings animated by time |
| `WAYPOINT` | 1 | waypoint diamond (`wp_iso`) |
| `UNREAD` | 6 | multi-frame arts no code or ledger row draws |
| **Total** | **248** | the table's rows |

Besides these, [table B](#arts-that-change-without-extra-frames) lists the arts that change by being swapped, cropped or
repeated (clock, experience strip, bars, action point lamps, schematic slots, follower panel).

## The original's state rules

All from the ledger; one row each.

1. **One button rule for every button** (OF9, GB4): three states, `frame = f(state, frame count)`. Up draws frame 0; pressed
   (state 0) one step, frame 1; hover (state 2) two steps, frame 2; a step that reaches the frame count wraps to 0. So of the
   TRD's "idle, hover, pressed, disabled" the original has **idle 0, pressed 1, hover 2 and no disabled frame**; a button that
   cannot act is hidden (flag 8: not drawn and not hit) or, for text, drawn grey. With **two frames** the pointer shows frame 0
   (no hover look). With **one frame** every state shows frame 0. With **four frames** (the character sheet's plus and minus)
   frame 3 is never drawn.
2. **Press and release** (GB3): a push button goes down on the press and acts on the release over it; a toggle flips on each
   press and stays on frame 1 even under the pointer. Sound 3000 on the press and 3001 on the release for the bars' buttons (GB6);
   the text-only menu buttons play 3027 / 3026 and have no art (GB7).
3. **Pixel-exact hit test** (OF9): a transparent pixel of the stored frame is not a hit; the kit's plates must keep that shape.
4. **Lit buttons** (OF10, GB15 to GB17): a lit art (558 Map town face, 195 Map world face, 559 Inventory, 560 Logbook, 561
   Character) **replaces** the button's idle art; nothing is overlaid, there is no blink and no timer. The Map button's art
   itself swaps between 193 and 194 with the sector (GB17).
5. **Pressed frames keep the geometry** (measured 2026-11-19 on `Char_But`, `Combat_Button`, `NextBut`, `cncl_big` and
   `Skills_Button`: the best alignment of frame 1 on frame 0 is at offset 0, so the pressed look is a change of colour and light,
   not a shift). The direction differs per art: the pressed frame is darker for `Char_But`, `TMap_But` and the end turn button, lighter
   for several plates (`Combat_Button`, `Skills_Button`, `NextBut`, `cncl_big`, `ScrllUP`). **There is no one derivation sign**, so
   the kit takes the differences per plate size from the original's frames, not from a global rule.
6. **Timers** are in the art's header (`fps`): `spellcur` 10 fps, the hover rings and the load indicator 15 fps, everything else
   8 fps. The period is `1000 / fps` in whole milliseconds (CU5 and OH4: 100 ms for `spellcur`, 66 ms for the rings), the next frame
   is taken by the mouse ping or the object redraw once that much time has passed.

## Classes (what each frame is, what switches it, timing)

The per-art rows (frame meaning, trigger, timing, windows, ledger rows) are in the TSV; this table is the class summary.

| Class | Frames | What each frame is | What switches it | Timing | Examples (art id) | Ledger |
| --- | --- | --- | --- | --- | --- | --- |
| `BTN3` | 3 | 0 idle, 1 pressed, 2 hover | pointer press and over; the action runs on the release | none | top strip 169, 186, 187, 193, 194; bottom strip 470 to 473, 563; 32, 33, 202, 203, 823, 824; 321, 323; 297 end turn; 317, 318 scroll arrows; 302 to 313 sheet buttons; 676, 677 | OF9, GB3, GB4 |
| `BTN2` | 2 | 0 idle, 1 pressed | pointer press | none | 137 (fate and sleep), 139 (compass button), 265 to 271 (logbook tabs), 340 `tiny_butt`, 811 to 813 (map buttons), 506, 508 (follower arrows), 355 to 369 (discipline tabs), 357 | OF9, GB4 |
| `BTN4` | 4 | 0 idle, 1 pressed, 2 hover, 3 never drawn | pointer; the editor makes the four from frame 0 | none | 23, 24, 770, 771, 804, 805 | OF9 |
| `BTN2T` | 2 | 0 up (info display on), 1 down (info display off) | a press flips it (sub-event 0 when it goes down sets the info display off, 1 when it comes up sets it on) | none | 747 `TextToggle` | MW25, GB3 |
| `LIT` | 2 | 0 idle, 1 pressed (a lit art shows frame 0 under the pointer too) | the game lights it, a window open puts it out | none | 195, 558, 559, 560 (561 has one frame) | OF10, GB15 |
| `TAB3` | 3 | 0 idle, 1 pressed, 2 hover; which frame the pressed (selected) college tab shows is F0 | pointer; the selection | none | 11, 38, 44, 50, 56, 62, 68, 74, 80, 86, 92, 98, 104, 110, 116, 122 | OF9, CHW F0 |
| `ICON2` | 2 | 0 raised bevel, 1 pressed bevel, same picture inside | the pressed look on a press; whether the panes drive frame 1 is F0 | none | 12 to 127 (80 spell icons), 275 to 282 (skill icons), 129, 130 | OF9, CHW20 |
| `MARK2` | 2 | 0 the marker, 1 the same with a yellow outline (a highlight) | nothing read draws frame 1 | none | 204 to 206, 814 | WM15, WM129 |
| `VIAL` | 15 | the liquid (frame 0 drawn; 1 to 14 never asked for) | health (poison swaps in 17) and fatigue values set the **fill height**, not the frame | none | 18 `redvial`, 19 `bluvial` | PA1 to PA3, CH7 |
| `VALUE` | 21 | one needle angle per alignment step (-100 to +100 in tens) | frame = trunc(stat 19 / 10) / 10 + 10 | none | 254 `align` | CHW5 |
| `ANIM` | 7 / 15 / 14 | one looping animation | `spellcur` while the target cursor is up; the rings while a creature is under the pointer | 100 ms a frame (10 fps); 66 ms a frame (15 fps) | 21, 467, 468, 555 | CU1, CU5, OH2, OH4, OH21 |
| `WAYPOINT` | 16 | a diamond with the waypoint's index 0 to 15 | frame = the waypoint's index in its object list (capped at the last frame) | none | 672 `wp_iso` | export read F0, below |
| `UNREAD` | 9 / 7 / 30 / 16 / 15 / 8 | art no code read draws | none known | none | 256, 334, 601, 197, `hovercompass`, 673 | OF11, WM64, UA2 |

### What is not drawn by anything

`UNREAD` and the unused rows are listed so the kit does **not** produce them (K7):

- **256 `Item_Dam`** (9 frames, black to red, 31 × 31), **334 `CharGears`** (7 near-identical frames of a 71 × 379 chain),
  **673 `wp_td`** (8 frames of a top-down direction marker): no ledger row, and none of the 115 distinct hexadecimal immediate arguments of the
  art builder 0x504330 in the decompiled export is 256, 334 or 673 (the builder is the only way to name an art by number, UA1; 93 of
  the 316 calls pass a computed value, so a table or base-plus-index use is not ruled out, UA2).
- **601 `loadindicator`** (30 frames, 15 fps): built by the indicator modules and by the combat interface init, but nothing draws
  it (OF11, CH6); no reader that draws it was found.
- **197 `CompassN`** (16 frames): loaded by the world map init with `Cmps_Bse` (WM64), use not read (F0).
- **`hovercompass`** (15 frames, 114 × 78): a file with no `interface.mes` number, so the builder cannot name it (OF1).
- **236, 237** (`Scrll_UP`, `Scrll_DWN`): unused (OF7). **The 8 extra logbook tabs** (`tab_*`, no number) and the old labels 133
  to 136 (`intcedit`, `intinven`, `intlog`, `intmap`), 257 and 258 (`SP_Minus`, `SP_Plus`), 289 `Trap_Button`, 751 and 752
  (`BookmarkButt`, `UnBookmarkButt`), 9 and 10 (`m_dnbut`, `m_upbut`) have no window row in the census and no reader in the
  ledger (F0).
- **672 `wp_iso`** (new read, F0, one reader): the module at 0x4BFCE0 (init), 0x4BFE00 (draw dispatch), 0x4C0140 (draw), 0x4C0280 (position helper)
  and 0x4C00D0 (a walker that calls a callback) walks the object list 0x11F of the sector and draws one diamond at each waypoint, frame = its index capped at 15,
  unless the module flag at 0x5FC640 is 1. **The init sets that flag to 0, so the overlay is on by default** and is off only if 0x4BFDB0 is
  called with 1; who calls it, and whether retail sectors carry waypoint lists in that object list, is not read, so "never drawn in play" is not shown. It looks like a waypoint overlay for the editor or debugging. `wp_td` (673) sits next to it in the same art range and a
  base-plus-1 use by the same module is plausible; both go to [#2977](https://github.com/Simarglok/arcanum-steamwoven/issues/2977) before the kit ignores them ([#2977](https://github.com/Simarglok/arcanum-steamwoven/issues/2977)).

## Arts that change without extra frames

Table B: a one-frame art that the code swaps, crops, repeats or fills from a value. The kit builds these as parts of the part's
**layer set**, not as frame sequences.

| What | Arts | What changes it | Timing | Ledger | Kit part |
| --- | --- | --- | --- | --- | --- |
| Health and fatigue vial fill | 17 `yelovial` (poisoned), 18, 19, 20 `clrvial` (empty) | fill = height × percent / 100 of the 88 px; the empty vial is drawn over the top part; the fill is a crop of frame 0 of 18 or 19, 17 while poisoned | redrawn with the interface bar's gauge refresh (the ledger does not give the exact triggers) | PA1 to PA3, CH7 | gauge layers: glass, housing, liquid (K6) |
| Clock | 207 `Clk_timestrip`, 208 to 215 moons, 216 `TimePnts` | the moon art is `208 + index` of the first table entry not below the phase (phase = days mod 28, the day turns at noon); the sky strip 207 scrolls by the hour and the needle 216 is fixed | refresh once an hour of game time (3,600,000 game ms after the last refresh, not on the hour) | GB8 to GB10 | 8 moon pictures plus a strip (K6) |
| Experience strip | 772 `XP_GaugeMag`, 773 to 782 `XP_Pip1` to `XP_Pip10` | one whole piece per full hundred of progress × 11 / 10, the eleventh row cut to the remainder's width | on the bar refresh and when the sheet is open (GB14) | GB13, GB14 | gauge pieces (K6) |
| Action point lamps | 295 green, 296 orange, 315 red | A - P lamps of 295, min(A, P) of 296, P - A of 315, at most 20, four groups of five | on every cost preview and charge; **another's turn: three running lamps (positions i, i - 7, i + 7 of 20) advance one lamp every 100 ms (a 2 s lap)** | CH2 to CH5 | one lamp lit and one lamp off per colour (K4) |
| Hovered critter's bars | 463 to 466 (health, fatigue), 665, 666 (poisoned) | the full art is cropped to width × percent / 100 on the empty art | on hover | CH8 | gauge layers (K6) |
| Follower portraits | 503 frame, 505 / 616 health (616 poisoned), 504 fatigue bars | bars cropped to the percent; below 20 % health a red fill is added over the 32 × 32 portrait | on each hit point or fatigue change | CH9, FP4 | frame hardware plus two bar layers (K6) |
| Follower panel buttons | 501, 502 (show/hide), 506/507, 508/509 (scroll arrows and their end looks) | 501 while shown, 502 while hidden; 507 / 509 show at the ends of the list; the arrows exist only with the panel shown and at least seven followers | on the show flag and the scroll offset | FP5 | state pairs by art (K1) |
| Schematic component slot | 830 `Schem_NO_Item`, 831 `Schem_Have_Item` (99 × 67) | 831 when the hero carries one of the slot's three acceptable components and the expertise reaches it, else 830; per slot | on window refresh | OF8 | two hardware overlays (K4) |
| Scroll bar | 238 / 787 / 240 thumb caps and middle (frame 0 of each), the 11 px thumb stretched to the bar's 17 px width | thumb length by the list; no track art (the track is the window background) | on scrolling | OF5, OF6 | three parts, middle stretched (K4) |
| Top strip buttons | 193 / 194 Map town and world face; 169, 186, 187 and the Map button's lit arts 561, 559, 560, 558, 195 | the sector's town map number picks the Map face; game events light | on sector entry and on game events | GB15 to GB17, OF10 | idle plate and lit plate (K3) |
| Option control | 688 `OptionSlider`, 671 `OptionMultiChoice`; arrow buttons 667, 668 only when enabled | a disabled control has no arrows and a grey label | on the control's state | MW9, OP4 | background hardware, arrow state pair (K1) |
| Skill slider fill | 623 `SkilGauge` (120 × 27) | cropped to value × 120 / top | on the value | MW9 | gauge layers (K6) |
| Aptitude bar | 255 `MT_Apt` (14 × 6, frame 0) | its place moves: top-left at (775, 221 − trunc(aptitude × 128 / 100)) | on the sheet refresh | CHW5 | one marker part (K4) |
| Alignment needle place | 254 (frame, above) | drawn with the frame's own hot point on the window's (56, 181) | on the sheet refresh | CHW5 | the pivot is that hot point |
| Main menu list thumb | 316 `ScrllSLD_OFF` (13 × 600) | a slice of the strip the height of the thumb, source y 300 less half the thumb plus the list position | on the list position | OF7 | one strip part (K4) |
| Messages | 438 to 444 icons by entry type (and the hover panel images) | entry type | on each message | ML5 | glyph set (K4) |

Floaters, hover text and the clock's digits are drawn text (fonts), not art; they are not kit parts.

## Where the viewer draws differently from the original (AC2)

The original look is the original's frames and rules; the Steamwoven look is the kit's. The differences that matter for the kit:

| Art | Original | Viewer today | Note |
| --- | --- | --- | --- |
| 21 `spellcur` (and 0 `cursor`, 352 `skillcur`, 353 `battlecur`, called shot cursors) | 7 frames at 100 ms, hot point from the frame header | **Legacy only**: Godot OS cursors from the Legacy RGBA, never an HD frame (owner 2026-11-07); `spellcur` steps its 7 frames every 100 ms | cursors stay out of the kit ([#2893](https://github.com/Simarglok/arcanum-steamwoven/issues/2893) keeps them last) |
| 467 `hoverunder`, 468 `hoverover`, 555 `hoverattack` | additive rings, tinted by the reaction category, 15 fps | **Legacy only**: the rings are drawn additive and tinted by `AreaRing`, HD does not apply (ARC-UI-1117); the frame stamps start at the hover | the tint is code; the frames are the three loops |
| 18 `redvial`, 19 `bluvial` | frame 0 only, the fill is a crop of frame 0 | the same (frame 0 only, 14 HD frames unused) | not a difference; flagged because the HD pack holds 28 frames nothing draws |
| 204 to 206, 814 markers | frame 0 only | the same (the second frame is not drawn) | not a difference |
| 23, 24, 770, 771, 804, 805 | frame 3 never drawn | the same | not a difference |
| 12 to 127 spell icons | grey for a not yet learned spell by a copied grey palette (CHW20, weights F0) | the usual luminance (PROJECT) | the grey is not a frame (the census TSV note is corrected accordingly) |
| 772 to 782 experience strip | repainted on the bar refresh, or when the sheet is open (GB14) | drawn on every frame | no visual difference |
| 18, 19 vial overflow | a fill above 88 (current above the maximum) draws from a lower art row; a dead PC draws the empty art over more rows (PA3) | the viewer clamps the overflow | technical drawing quirk, not kit |
| combat interface | action point lamps only (CH6: no turn order list) | the viewer adds a list of the round's participants (Steamwoven rule mode) | not an art; no kit part |
| Buttons with a hover look | three frames as the original | the three frames, drawn as the original (GB4) | disabled states: **none in the original** |
| 601, 197, 256, 334, 672, 673, `hovercompass` | not drawn (or not read) | not drawn | no kit part |

## Proposal for the kit (AC3)

Kinds of production, from the cheapest to the most special. Every rule keeps the frame count, order and timing of the original.

- **K1 State sheet** (`BTN3`, `BTN2`, `BTN2T`, `TAB3`, the state pairs of table B). The states of **one plate size** are generated on
  one sheet: idle, pressed and hover together, one execution, so the glass, ring and grain match (the TRD's SWI-KIT-5 and the owner's
  observation on `NextBut`, [HD interface art](../rendering/hd-interface-art.md)). The census has few distinct sizes (34 × 34, 38 × 37,
  39 × 39, 28 × 27, 23 × 23, 19 × 19, 24 × 24, 32 × 32, 46 × 41 …), and **ten buttons of one size are one sheet**; the glyph is a
  separate transparent picture over it, so a glyph that appears in two windows is one file. Only the states the original has are made:
  **no disabled overlay**, because the original has no disabled frame (a button is hidden instead; the TRD's "disabled" is dropped
  or optional, [open question 1](#open-questions-and-issues-filed)).
- **Choosing K1 or K2 for a `BTN3` size** ([open question 8](#open-questions-and-issues-filed)): use **K1** when the size has few arts (up to about four: the sheet is cheap and each look is crisp) and **K2** when many arts share the size (the five 34 × 34 top strip buttons, the 16 college tabs of 24 × 24: the frame difference is then one fixed rule). A pressed frame stays on the plate's geometry in both.
- **K2 Base plus rule** (`BTN3` of a size with many arts, `ICON2`, `BTN4`). One generated frame 0 plus the **original's measured frame
  difference** per size: frame 1 and frame 2 = frame 0 plus the Legacy difference (bicubic-enlarged), the U3d derivation that the HD
  work already tested (`AddLegacyDifference`, differences within 0.9 of the original on `NextBut`; [HD interface art](../rendering/hd-interface-art.md)).
  For the 80 spell icons and 8 skill icons: **one raised/pressed bevel pair per size (32 × 32, 16 × 16) as hardware, and the
  picture on top**: the 88 pictures are generated once each and the pressed frame is the pressed bevel plus the same picture, never a
  second generation. The 16 college tabs are one round plate with three states; only the college glyph differs.
- **K3 Lit variant** (`LIT`). The lit face of a top strip button is the idle plate with a lit glyph, made in the same sheet as the
  idle plate (the same size, the same geometry, frames idle and pressed), so lighting does not change the button's outline or its hit
  test; the game replaces the art, so the kit ships two plates per button, not an overlay.
- **K4 Glyph and overlay** (markers, lamps, slot overlays, message icons, scroll parts). Each is a small separate part generated once
  from its own crop; a lamp is one lit and one dim part per colour, the 20 lamps are one part placed 20 times, the schematic slot
  states 830 and 831 are two hardware overlays at the original coordinates (579, 181) and (579, 277), the marker pair is frame 0 only
  (frame 1 is added only if a hover highlight is ever wanted).
- **K5 Animation sheet** (`ANIM`). One sheet of the frames in order, generated together, played by the same timer:
  the hover rings (467 and 468 together, plus 555 so the three loops match) at **66 ms a frame**, 15 frames, 14 for `hoverattack`.
  The tint stays in code. The rings and the cursors are Legacy today (AC2); the sheet is only needed if the owner moves them to the kit.
- **K6 Value layer** (`VIAL`, `VALUE`, table B gauges). The value drives the layer, not a frame sequence (SWI-KIT-6): the vial is
  glass, housing and liquid, the fill is cropped from the value exactly as the original crops frame 0 (PA3, CH7), the empty vial is drawn
  over the top part, and **the poisoned yellow is its own liquid layer**; the alignment needle is **one needle rotated by the value
  in code** (21 steps) or one 21-frame sheet generated together; the clock is a strip plus eight moon pictures, the experience strip
  eleven pieces. The 14 unused vial frames are not made.
- **K7 Hold** (`UNREAD`, `WAYPOINT`, unused arts): no part is made until a reader is found. The kit manifest lists them as
  `status: unused` so a later reading cannot lose them.

Ordering for production (SWI-KIT-7): the buttons and glyphs come before the icons and cursors; this census adds that **the sheet of a
plate size is made once before any glyph of that size**, and that the 16 college tabs and the 88 spell and skill icons are the last
and the largest batch (their bevel pair is two parts).

## Open questions and issues filed

1. **Disabled state.** The TRD's SWI-KIT-5 lists "disabled"; the original has none (OF9). Keep a disabled overlay as a Steamwoven
   addition, or drop it. Owner decision (Steamwoven only; the original look has none).
2. **Hover on two-frame buttons.** 49 plates have no hover look in the original (frame 0 under the pointer). The kit makes the same
   two frames; adding a third for Steamwoven is a design choice, not an original fact.
3. **Spell and skill icons frame 1.** How the spells and skills panes drive the second frame is not read (F0): are they library
   buttons? Needed before the 88 icons are produced; [#2978](https://github.com/Simarglok/arcanum-steamwoven/issues/2978).
4. **College tabs: the selected frame.** CHW F0: the viewer uses the pressed frame for the selected tab; read by [#2978](https://github.com/Simarglok/arcanum-steamwoven/issues/2978) too.
5. **Do the rings and cursors move to the kit?** Today Legacy by owner decision (2026-11-07); one decision for K5 and the cursor set.
6. **`wp_iso` and the unread arts** (256, 334, 673, 197, `hovercompass`, 601): one exe read to decide whether any is ever drawn in
   retail play. Filed as [#2977](https://github.com/Simarglok/arcanum-steamwoven/issues/2977); nothing blocks the kit on it.
7. **Frame differences per plate size** are measured from the original's frames when the kit manifest is built
   ([#2454](https://github.com/Simarglok/arcanum-steamwoven/issues/2454)); this note only states the rule (no global sign).
8. **K1 or K2 per plate size.** The threshold above (about four arts) is a proposal; the owner confirms it or asks for K1 everywhere.

Issues filed with this note: [#2977](https://github.com/Simarglok/arcanum-steamwoven/issues/2977) (read the unread arts) and [#2978](https://github.com/Simarglok/arcanum-steamwoven/issues/2978) (the icon and tab frames). The kit assembly tool ([#2972](https://github.com/Simarglok/arcanum-steamwoven/issues/2972)) and the manifest rework ([#2454](https://github.com/Simarglok/arcanum-steamwoven/issues/2454)) are separate and consume this table.

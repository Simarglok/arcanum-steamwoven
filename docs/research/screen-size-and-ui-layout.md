# Screen size in the original: UI layout and play-view dependencies (ARC-RES-093)

Status: **Research**, work item ARC-RES-093, 2026-11-07. Docs only; no code changes.
Requirements: [FR-UI-007](../requirements/ui-media-lifecycle.md#fr-ui-007) (adaptive layout),
[FR-UI-002](../requirements/ui-media-lifecycle.md#fr-ui-002) (HUD). Owner decision (Display,
[decisions](../architecture/decisions.md)): "Viewport must not define simulation rules".
Evidence: the ledger section [Screen size and layout](ledger/ui-audio.md#screen-size-and-layout-arc-res-093),
rows SZ1–SZ14 (F1: one reading of the retail 1.0.7.4 `Arcanum.exe`, per the
[original executable check](original-executable-check.md); behaviour in prose only).

This note answers two questions for the planned scalable HD interface (UI laid out in the
original's 800 × 600 "UI units", drawn at a UI scale `s`, split into blocks anchored to the
screen edges, the world view filling the rest; the scaling layer itself is a later ARC-UI task):

1. **Layout:** where every interface window sits and what it is attached to (table A).
2. **Logic:** which game rules read the screen or play-view size (table B), and what
   Steamwoven must fix so that a wider view does not change them (section C).

## Inputs

- The owner's GOG 1.0.7.4 install on the Mac: `Version.txt` 1.0.7.4, `Arcanum.exe` SHA-256 prefix
  `9bd80511f8663a41` (matches the reference in the executable check). Read in place, never copied.
- Disassembly: Capstone (BSD-3-Clause) through Python 3 and `pefile` (MIT), run in the Linux VM
  on the owner's Mac; data words read from the image's `.data`. No listing is kept in git.
- The GOG launcher entries (`goggame-1207658829.info`): the normal entry passes no argument; "Safe
  Mode" passes `-no3d -doublebuffer`. Neither passes `-fullscreen`.
- Earlier ledger rows (GB1, CH1, CH7–CH9, MW1, PA4, PA6, IW33, PD6, DB2, QW3, ML4, SL8, PG13, TG4,
  OL29, TT12, BT10, NT12, RT2, FL3, SU15–SU16, RL11, SN7) are reused and cited, not re-read.

## Answer in short

- There are **two size sources**. (1) The **game window** (0, 41, 800 × 400), created once by
  0x557810 and never moved or resized; with the command-line switch `-fullscreen` it is created
  (0, 0, 800 × 600) instead (SZ1). (2) The **view rectangle** that the game library and every module
  copy from it at start and receive again through the resize fan-out (SZ2, SZ3). The only caller
  of the resize is the interface hide toggle, which exists only under `-fullscreen` (PA4): it swaps
  the view between (0, 41, 800 × 400) and the whole 800 × 600 window.
- So in the **default launch** the play view is always 800 × 400 and nothing ever resizes. Under
  `-fullscreen` the play view is 800 × 600 while the bars are hidden and 800 × 400 while they are
  shown, but the game window stays 800 × 600 throughout.
- **Rule readers** (table B): the screen-sized vicinity (0x440FF0, eleven callers: script focus
  lists, follower level step, experience split, key sharing, restock, combat exit, AI redirect,
  float line, trap shooter, spoken orders, target pick prologue) reads the **game window** live;
  the monster generators (0x4BA790, 0x4BA910) and the container restock (0x463C60) test "on
  screen" against the **view rectangle** (the object's side is the rectangle 0x43DFD0 returns,
  not read in detail); the fidget event (0x4300D0) uses a **constant**
  800 × 400 rectangle; what is drawn loads its sectors (RL11). Positional sound takes constant
  parameters set at sound init, not a screen read (SZ10).
- In the default launch every one of them sees 800 × 400, as expected. **Two contradictions** for
  the owner (section C): the original does grow these rules to 800 × 600 under `-fullscreen`;
  and the generator and restock tests exist to keep spawning and restocking **out of the
  player's sight**, so fixing them to 800 × 400 while the drawn view grows lets the player watch
  monsters appear in the extra margin. Owner decision 2026-11-07: Steamwoven mode tests the drawn
  view, Original mode keeps 800 × 400 (question 1, option (b)).

## A. Window layout census

Coordinates are the 800 × 600 screen's unless a parent is named (button and child offsets are
relative to their window, GB1). Anchor classes: `top-strip`, `bottom-strip`, `play-view` (inside
(0, 41, 800 × 400)), `full-screen`, `follows-object`, `follows-cursor`; "computed" marks a
rectangle derived from another window or the screen rather than fixed. Rows without a ledger ID
in the source column were read by this task (SZ rows); F0 marks what was not read.

| Window or element | Rectangle (800 × 600) | Parent / computed | Art | Anchor class | Source |
| --- | --- | --- | --- | --- | --- |
| Top strip | 0, 0, 800 × 41 | screen | 185 `IntTop` | top-strip | GB1, GB2 (buttons) |
| Bottom strip | 0, 441, 800 × 159 | screen | 184 `IntBotom` | bottom-strip | GB1, GB2 |
| PC health / fatigue gauges | 14, 472 and 754, 473 of the screen, 28 × 88 | bottom strip | 17–20 | bottom-strip | CH7, PA1 |
| Message window and hovered-object panel | text rects from 290, 504; image at 217, 510 | bottom strip | — | bottom-strip | ML4, ML6, CH8, PA6 |
| Quantity window (bar mode) | buttons 364..545, 506..559 | bottom strip | 299, 804, 805, 32, 33 | bottom-strip | QW3 |
| Combat lamp panel (bars shown) | 222, 412 | screen (overlaps the view's lowest 29 rows) | 294, 297 | bottom-strip | CH1 |
| Load indicator (combat UI) | x = window right − art width − 2, y from the window top | computed from the game window | 601 | play-view (top-right) | SZ4 |
| Follower portraits | 40 × 49 at 11, 50 + 62 × slot (six) | screen, left edge of the view | 503–505 | play-view (left) | CH9 (SZ11: Follower-UI swaps a position table by the view's height; which elements, F0) |
| Game window (the world) | 0, 41, 800 × 400 (`-fullscreen`: 0, 0, 800 × 600) | screen | — | play-view | SZ1 |
| Shared panel window: inventory, loot, barter, steal, character sheet, logbook, world map, schematic | 0, 41, 800 × 400 | screen; one at a time | own backgrounds (e.g. 221–235) | play-view | SZ12; IW20–IW36, BW1–BW5 |
| Round view inside the inventory | 89 × 89 copy of the view square (311, 96, 178 × 178) | the shared panel; the square is computed from the view | 231–233 | play-view | IW33; SZ3 (square moves to 311, 196 when bars hidden) |
| Reading window (written objects) | 0, 41, 800 × 399 | screen | own | play-view | SZ13; WR5, WR12 |
| Fate window | 0, 41, art size | screen | 292 | play-view (top-left) | SZ13 |
| Sleep window | 573, 42, art size | screen | 565 | play-view (top-right) | SZ13 |
| Follower command window | per-slot rectangle from a runtime table (0x67BB88) | follower portrait | — | play-view (left) | SZ13, F0 for the values |
| Character sheet sub-windows | at 503, 104 (size at run time) | F0 which parent | — | play-view | SZ13, F0 |
| Dialog option area | the view rectangle (TC module) | computed from the view | — | play-view | SZ4; placement F0 |
| Dialog box (yes/no) | 325 × 136 at the request's point; the quit question centred | screen | 822–824 | full-screen (centred) | DB1–DB3 |
| Menu windows with a 600-row background | 0, 0, 800 × 600 | screen | 329, 330, 331, 556 | full-screen | MW1, MW7 |
| Menu windows without one (load, save, character creation) | 0, 41, 800 × 399 plus strips 336 (0, 0) and 335 in (200, 441, 400 × 47), (0, 441, 200 × 159), (600, 441, 200 × 159) | screen | 335, 336, 745, 765, 766 | play-view + own strips | MW1, MW11, MW17 |
| Slide show | 0, 0, 800 × 600 | screen | slides | full-screen | PD6 |
| Network lobby (out of scope) | 0, 0, 800 × 600; player window at 566, 41 | screen | own | full-screen / play-view | SZ13 |
| Floaters | 200 px wide box centred over the object's screen point | follows the object | font 229 | follows-object | CH10, CH11 |
| Text bubbles | over the object | follows the object | — | follows-object | SZ4 (rect only); placement F0 |
| Hovered-object highlight | the object itself | follows the object | 467, 468 | follows-object | OH2, OH3 |
| Cursor, held item | hot point on the pointer | follows the cursor | cursor arts, item art | follows-cursor | CU4, CU7 |
| Hidden-interface (`-fullscreen`) compact UI | container 210, 479, 385 × 84; life panel 751, 498, 49 × 102 | screen, over the view | — | bottom-strip / right edge | PA4, PA5 |
| Hidden-interface message line | 196, 563, 411 × 37 | screen, over the view | — | bottom-strip | SZ11 |
| Hidden-interface combat bar | 124 × 14 at 617, 577 | screen | — | bottom-strip | CH1 |

Every rectangle read above is a constant except the load indicator, the round view square and the
dialog option area, which take the window's or the view's rectangle; the follower command window
and the character-sheet sub-windows take run-time values not read (F0), and the hidden-interface
layout picks other constants by the view's height (SZ11). No window is placed from a configured
resolution. There is no tooltip window for objects: the name goes to the message window (PA6).

## B. Readers of the screen and play-view size

Source "window" = the game window's rectangle (0x51CEB0 on 0x5E2F24 or 0x64C52C): 800 × 400
default, 800 × 600 under `-fullscreen` for the whole run. Source "view" = the view rectangle a
module stores at init and at resize: 800 × 400 default; under `-fullscreen` 800 × 600 with the
bars hidden, 800 × 400 with them shown.

| Reader | Domain | Source | What it computes | Class | Rows |
| --- | --- | --- | --- | --- | --- |
| Vicinity 0x440FC0 / 0x440FF0 | objects | window, read live | a window-sized rectangle centred on a location; objects listed by type mask | **rule** (11 callers below) | SZ5; PG13 |
| Script focus "everyone/anyone in vicinity" 0x44B4E0 | scripts | vicinity | the focus list | **rule** | TG4 |
| Float line 0x44B1F0 | scripts | vicinity | the dialog's PC near the triggerer | **rule** | FL3 |
| Experience split 0x45F110 | multiplayer | vicinity | party PCs that share the award | **rule** (network) | NT12 |
| Key sharing 0x463370 | multiplayer | vicinity | "party member on screen" | **rule** (network) | NT10, NT11 |
| Restock 0x463C60 | items | view (0x5E8808) and vicinity | container on screen → rescheduled; off screen → nearby NPC substitute | **rule** | SZ7; BT10 |
| Follower level step 0x4A69C0 | character | vicinity | waiting followers that level with the PC | **rule** | OL29 |
| AI redirect 0x4A9560 | AI | vicinity | NPCs around the spell target that turn on it | **rule** | HS17, P3 |
| Combat exit 0x4B3D90 | combat | vicinity | fighting NPCs on screen that block leaving combat | **rule** | RT2 |
| Trap shooter 0x4BD150 | technology | vicinity | the trap's shooter near the triggerer | **rule** | TT12 |
| Spoken orders 0x4C3770 | multiplayer | vicinity | PCs that hear an order | **rule** (network) | NT11 |
| Target pick prologue 0x4F28A0 | input | vicinity | dead critters made click-through | presentation / input | SU15 |
| Monster generators 0x4BA790, 0x4BA910 | AI / world | view (0x5FC358) | a generator with flag 0x4 spawns only while off screen, and only at spots off screen (ten tries, then the generator's own spot) | **rule** | SZ6 |
| Fidget event 0x4300D0 | animation | **constant** (0, 0, 800 × 400) | the critters that may fidget, every 4 s | **rule**, size fixed | SZ8 |
| Map draw 0x402E50 | residency | view | sectors under the view are locked and loaded | residency (rule-adjacent) | SZ9; RL11 |
| Sound init 0x41AA30 | audio | **constants** | positional sound parameters (400, 0, 300, 0 and 150, 800, 150, 400; their meaning not read) | presentation (constants) | SZ10 |
| Sound listener 0x40E630 | audio | view | the listener is the view's centre tile | presentation | SN7 |
| Location module 0x4B8440 / 0x4B8540 | coordinates | view | view size and centre (for centring and the screen ↔ location rectangle) | presentation | SZ4 |
| Scroll, Object, Light, Tile, Roof, Wall, JumpPoint, Iso, text bubbles, floaters, dialog options, indicators | drawing | view | redraw, culling and clipping rectangles; object margin −256 / +512 px | presentation | SZ4 |
| Target module 0x4F24F0 / 0x4F2580 | input | view | picks only inside the view | presentation / input | SZ4; SU16 |
| Game library 0x4020F0 | save | window (init only) | the save preview is window width / 4 × height / 4 (200 × 100 default) | presentation | SZ2; SL8 |
| Interface, hotkeys, combat UI, followers | UI | view height (400 or not) | the hidden-interface layout | presentation | SZ11 |

**Does the original ever run with another play view?** Only under `-fullscreen` (SZ1, SZ3): then
the window is 800 × 600 for the whole run (vicinity rules use 800 × 600 even with the bars
shown) and the view toggles 800 × 400 ↔ 800 × 600 (generator, restock and drawing use the
current one). Nothing reads a configured resolution; the library screen is always 800 × 600 at
16 bits (SZ2).

## C. Recommendation

### Layout per anchor class

Let the display be `W × H` pixels and `s` the UI scale (integer with Legacy art, fractional with
the 4× HD pack). One UI unit is `s` pixels.

| Class | 16:9, 16:10, 21:9 (wider than 4:3 at `s`) | 4:3 |
| --- | --- | --- |
| top-strip | attached to the top edge; the 800-unit strip centred, its buttons at their strip offsets; the remaining width filled by tiling the strip's edge columns (9-slice with fixed button area) | full width at `s = W / 800` |
| bottom-strip | attached to the bottom edge, same rule; the gauges, message window and buttons keep their strip offsets inside the centred 800 units | as top-strip |
| play-view (full panels: inventory, sheet, logbook, map, schematic, reading, load/save) | the 800 × 400 panel centred in the view area at `s`; the world can stay visible around it (the original covers the whole view with these panels but keeps drawing the world beneath them, TL10 view mode 1) | the panel fills the view |
| play-view (edge elements: follower portraits, fate, sleep, load indicator) | attached to the view's nearest edge (portraits and fate left, sleep and indicator right), offsets in units from that edge | as original |
| full-screen (menus, slides, dialog box) | 800 × 600 units centred, pillar-boxed (black or a tiled background) | fills the screen |
| follows-object / follows-cursor | at the object's or pointer's pixel position; sizes in units (`s`) | same |

### Rule readers: fixed rectangles

Every rule reader in table B must use a fixed rectangle in **world pixels**, independent of the
display and of `s`, centred where the original's is:

- vicinity: 800 × 400 centred on the given location (the default launch's window);
- fidget: 800 × 400 at the view origin as the original (its constant), or centred on the camera
  centre; the default launch gives the same set because its view is 800 × 400;
- generator and restock "on screen" (owner decision 2026-11-07, option (b) of question 1): in
  **Original** mode 800 × 400 centred on the camera centre (the view's centre; the original
  centres the PC's location 15 px above the view's middle, IW33); in **Steamwoven** mode the
  rectangle actually drawn, so nothing spawns or restocks in the player's sight on any display;
- sector residency: Steamwoven's own residency rules (not the drawn view), as already decided for
  the world runtime; the original's "what is drawn loads" is an engine effect, not a game rule.

D8: no Original/Steamwoven difference is needed for the vicinity, fidget and sector readers: both
modes keep 800 × 400 and only the drawn view grows. The generator and restock tests are the one
D8 difference (owner decision, question 1).

### Questions (for the owner and the follow-up ARC-UI task)

1. **Out-of-sight rules with a wider view.** Generators and restock exist to act where the player
   cannot see. With a fixed 800 × 400 test and a wider drawn view, monsters can appear and
   containers restock in plain sight; with the drawn view as the test, a wider screen spawns less
   near the PC. Options: (a) fixed 800 × 400 in both modes (rule numbers kept, visible pop-in);
   (b) the drawn view in Steamwoven mode, 800 × 400 in Original (a D8 difference); (c) fixed
   800 × 400 for the rule plus a presentation-only fade-in for anything spawned in view.
   **Decided 2026-11-07 by the owner: (b)** (see [decisions](../architecture/decisions.md)).
2. **`-fullscreen` variant.** The original grows every vicinity rule to 800 × 600 under that
   switch. Recommended: model only the default launch (800 × 400) and record `-fullscreen` as an
   unsupported original variant.
3. Bottom and top strips: centred 800 units with tiled sides (recommended, keeps every original
   offset) or a true full-width re-layout.
4. Maximum view width and height in world pixels (to bound culling, residency and the sight
   question above), and whether the view grows or `s` grows first on large displays.
5. Whether Steamwoven offers the original's hidden-interface mode (compact UI, PA4) as a
   presentation option.

## Not verified

- Every row is F1 (one reading); no second independent reading and no oracle run.
- Rectangles marked F0 in table A (follower command window values, character-sheet sub-window
  parent, dialog option and bubble placement).
- The Object module's tile spans (0x5E2F50, 0x5E2E60) have no reader found outside the module
  (SZ4); the 0x43DFD0 object rectangle was not read in detail.
- Callers of the screen ↔ location helpers (0x4B8680, 0x4B8730) in AI line-of-fire and hearing
  were taken to be size-independent conversions (they read the view origin, not its size).

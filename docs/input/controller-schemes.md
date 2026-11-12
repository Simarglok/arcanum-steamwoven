# Controller schemes experiment: A Direct, B Pointer, C Touch+pad (2026-11-12)

Status: **concept accepted for building as an experiment** (owner request 2026-11-12), epic
[#1371](https://github.com/Simarglok/arcanum-steamwoven/issues/1371). It defines three schemes that sit
**next to** the original mouse + keyboard (which keeps the original scheme, owner-accepted; KM-01 in
[the original's controls](../requirements/original-keyboard-mouse.md): two adapters over one shared simulation), so they can be built into the
prototype (`./arcanum area` / packaged build) and compared by a person. The existing lab and its
presets ([gamepad presets](gamepad-presets.md)) are a reference only: owner decision 2026-11-12, nothing already built for the
gamepad has to be kept and the schemes may be rewritten from scratch where that is better. Nothing here is implemented yet; the
slices and their status are in the [issue map](#issue-map).

Target segments:
- **H — handhelds**: Steam Deck class (7" 1280×800 touch screen, built-in pad, two trackpads, gyro,
  back grips); also Windows handhelds (ROG Ally, Legion Go) with touch.
- **P — PC/Mac + a connected pad** (Xbox / PlayStation / Switch Pro), no touch.

| Scheme | Idea | Segments | Closest references |
| --- | --- | --- | --- |
| **A. Direct** | No cursor in the world: the stick walks the hero, targets are picked by proximity, highlight and cycling; abilities on a radial; screens by focus. | P, H | BG3 default mode, Owlcat real-time, PoE/Deadfire console |
| **B. Pointer** | The original point-and-click kept as is, driven by an assisted virtual cursor (stick with magnetism; trackpad/gyro on the Deck as precision pointers). | P, H | BG3 Cursor Mode, Owlcat turn-based cursor, Destiny cursor assists, Steam Deck trackpad templates |
| **C. Touch + pad** | Touch for the world and windows (tap, long press, drag), pad for walking, hotkeys and camera; a magnifier and a confirm button for precision. | H only | DOS2 iPad, Deadfire Deck support |

Why these three: they span the real design space — *replace the cursor* (A), *assist the cursor*
(B), *replace the cursor with the finger* (C) — and differ in cost: B reuses the original windows
almost unchanged, A needs focus navigation for every window, C needs hit-area and gesture work.
The test should tell which one is primary per segment and which stays an option.

## 0. Shared foundation (build once, all schemes use it)

Every scheme is an **input adapter** that emits the same semantic commands to the session (KM-01).
The foundation is a precondition for any fair comparison:

1. **Scheme selector** in Options → Controls: `Original (mouse+keyboard)` always active;
   `Pad scheme: A Direct / B Pointer`; `Touch: Off / C Touch+pad`. Switchable at run time without
   restart, persisted in user settings. Defaults for the test: none — the test script chooses.
2. **Active device detection** ("last meaningful input wins"; both devices always live):
   - pad becomes active on a button or a stick/trigger beyond the deadzone (drift ignored);
   - mouse becomes active on a click, a key, or accumulated motion > ~12 px within 100 ms;
   - touch becomes active on a touch event;
   - **pointer-from-pad exception**: mouse motion with no keyboard/click while the pad was used in
     the last 2 s (Deck trackpad or gyro mapped to mouse by Steam Input) keeps pad prompts and
     only shows the cursor (otherwise Deck Verified's "no M+KB glyphs when not active" breaks);
   - 250 ms hysteresis before the HUD re-skins; open windows and focus are never reset.
3. **Glyph families**: Xbox, PlayStation, Nintendo, Steam Deck, keyboard/mouse; detected from the
   pad name/GUID (`Input.GetJoyName/GetJoyGuid`, the lab already reads the name), with a manual
   override `Prompt style: Auto/Xbox/PlayStation/Nintendo/Deck`. Bindings must apply to **any
   device**, not device 0 (a known Godot-on-Deck failure: the Deck's built-in pad is not always
   index 0).
4. **Prompt strip**: a line at the bottom of every modal and a small prompt near the world target
   ("A Talk · X Examine"), reflecting remaps.
5. **Focus layer over the original windows**: every original window gets a declared list of focus
   targets (its existing hit regions) with up/down/left/right neighbours, a focus ring, LB/RB for
   tabs/pages, B back. Needed by A always, by B/C optionally (D-pad shortcut in menus).
6. **Real-time pause rules** (setting, default on for the test): opening a radial, a target pick
   or a window pauses real time; closing resumes.
7. **Accessibility switches** (XAG 107): hold ↔ toggle for every hold; no chord required for any
   action; per-stick deadzone and sensitivity ±50 %; prompts follow remaps.
8. **Test evidence log**: a local, opt-in session log of semantic commands, cancels, refused
   actions, wrong-target corrections and timings (no personal paths, never committed) — the
   metrics source for §5.
9. **UI scale on 1280×800**: the original windows at a whole scale that keeps the smallest text
   ≥ 9 px (Deck Verified minimum, 12 px recommended); hit areas may be larger than the art.

Platform prerequisites (owner decisions, see §6): the Deck runs our **Windows build through
Proton** today (packages are mac|win only). Under Proton the Deck touch screen most likely
reaches the game as **emulated mouse clicks** (Steam's "Touchscreen mode"), single-point, no
hover; real multi-touch needs a native Linux build with "Touch API pass-through" (reported for
other engines, unverified for Godot 4.7.2). Scheme C is therefore designed so its core works with
single-point emulated touch.

## 1. Scheme A — Direct (cursorless)

Takes the ideas of the ARC-GPAD-005 lab (Radial preset) into the game as a reference, not as a base. Precision moments (ground target,
area spell, turn-based step) use a **target reticle**, not a free cursor.

| Input | Exploration (real time) | Combat (real time) | Turn-based | Windows |
| --- | --- | --- | --- | --- |
| LS | Walk the hero (stick → short movement goals on the original pathing; lab "Nearby Goal") | Walk | Move the grid reticle; path + AP cost preview | Focus move |
| RS | Camera pan (inversion setting) | Camera pan | Camera pan | Scroll / item details |
| A | Default action on the highlighted target (talk, open, pick up, attack) | Attack / use on target | Confirm step / action | Activate |
| B | Cancel / back | Cancel targeting | Cancel preview | Back / close |
| X | Examine target (original examine text) | Examine | Examine | Secondary (drop, split) |
| Y | Hotkey radial (10 slots: spells, tech, items) | same, pauses RT | same | Compare / equip toggle |
| LB / RB | Hold: reveal all interactables with labels (toggle option) / cycle targets | Cycle enemies | Cycle targets in range | Previous / next tab |
| D-pad ←/→ | Previous / next target | same | same | Focus |
| D-pad ↑ | Party radial (F1–F5 follower orders) | same | same | — |
| D-pad ↓ | Repeat last action (original `A`) | same | same | — |
| LT / RT | Zoom out / in | same | same | Previous / next character page |
| R3 | Lock / unlock target | same | same | — |
| L3 | Recenter camera on the hero | same | same | — |
| View | Hub radial: inventory, character, logbook, world map, fate, sleep, quick save/load | same | End turn (hold, toggle option) | Close all |
| Menu | In-play window (original Escape) | same | same | — |
| — | RT ↔ TB toggle (original Space): in the hub radial and as a remappable binding | | | |

Targeting rules: the best target is scored by distance and facing within the original
interaction range; the highlight is **sticky** until the player moves away, cycles or cancels;
A never fires on a target the player cannot see highlighted. Spells/items that need a target
open a reticle that starts on the auto-target, D-pad/shoulders cycle, LS moves it freely for
ground/area targets, A confirms, B cancels — no effect before confirmation.

Windows: all through the focus layer (§0.5). Inventory: A picks an item into a "held" state,
LS moves the shaped footprint on the grid (snapping, invalid cells red), A places, B returns it.
Dialogue: vertical focus list, numbers still shown, A answers.

Expected strengths: comfortable long walks, couch play, no aiming fatigue. Risks: crowded scenes
(target cycling cost), area spells, inventory with many items, every window needs a focus map.

## 2. Scheme B — Pointer (assisted virtual cursor)

The original interaction model stays 1:1: a cursor and the original left/secondary actions.
The cursor is drawn by the game (the original action cursors, ARC-UI-005/056/057), moved by:

- **Stick** (LS by default, swappable to RS): response curve with a slow precision zone and
  acceleration after ~0.4 s of full deflection; **magnetism** (pull toward interactables and UI
  buttons inside a radius), **friction** (slower over targets), **press stabilisation** (cursor
  frozen for ~80 ms around an A press — stick jitter on confirm causes a large share of
  cursor misselections).
- **D-pad snap**: jumps the cursor to the next interactable / UI button in that direction
  (focus-like, no focus maps needed — uses screen positions of hit regions).
- **Deck right trackpad / gyro**: through Steam Input as mouse (absolute-ish precision); handled
  by the pointer-from-pad rule (§0.2). In-engine gyro is optional: motion-sensor support appears
  in Godot's development docs; check it for 4.7.2 before relying on it.

| Input | Action |
| --- | --- |
| LS | Move cursor (with assists) |
| RS | Camera pan; the cursor at the screen edge also pans (original edge scroll) |
| A | Original left click (move / default action / pick up / place / button) |
| X | Original secondary action (examine and the right-button uses in the KM table) |
| B | Cancel / close window (original Escape for windows) |
| Y | Hotkey radial (10 slots), selection puts the original item/spell cursor in place |
| LB / RB | Hold: reveal all interactables (toggle option) / snap cursor to the next target |
| D-pad | Snap cursor to the nearest target or button in that direction |
| LT / RT | Zoom out / in (hold RT + A = force walk, if the original has it) |
| L3 / R3 | Recenter camera / center cursor on the hero |
| View / Menu | Hub radial / in-play window (same as A) |

Long walks: hold A on the ground = continuous walk toward the cursor (the original's held-click
behaviour where it exists); the cursor follows the hero while held. Inventory: original drag,
done as A pick → move → A drop (no hold needed, XAG). The original windows are used without a
redesign; only the hit areas grow to the minimum size and the cursor gets magnetism to buttons.

Expected strengths: lowest build cost, full parity with original rules (every original action is
reachable), precise area targeting, good with Deck trackpad. Risks: slow stick cursor over long
distances, fatigue, small original buttons on 7".

## 3. Scheme C — Touch + pad (handheld hybrid)

The finger replaces the cursor for **pointing**; the pad keeps **walking, hotkeys and camera**.
Touch has no hover, so everything hover gives in the original (object names, action cursor
preview, tooltips) moves to long press or to the selection state.

| Gesture / input | Action |
| --- | --- |
| Tap on ground | Walk there (original left click) |
| Tap on object/NPC | **Select** it: highlight + name + action prompts ("A Talk · X Examine"); second tap on the same object or A = default action |
| Long press (~400 ms) | Examine / tooltip (original examine text, item description) |
| Tap in windows | Press the button / pick the item |
| Drag in inventory | Move an item (pick up after small movement, no long hold — DOS2 iPad rejected hold-to-drag) |
| Drag on world (one finger) | Camera pan *(only if it doesn't conflict with tap-to-walk in the test; otherwise RS only)* |
| Pinch / two-finger pan | Zoom / pan **only with native multi-touch** (Linux build or Windows handheld) |
| Magnifier button / hold LB | Magnified loupe under the finger for small targets (DOS2 iPad "magnifying glass") |
| Targeted spell / item | Tap target shows the preview; an on-screen **Confirm** button (or A) casts — no double-tap |
| LS / RS | Walk the hero (as A) / camera |
| Y / View / Menu / D-pad / LT-RT | As scheme A (radial, hub, in-play, cycling, zoom) |

Hit areas: on the Deck (≈216 ppi) 1 mm ≈ 8.5 px, so a 7 mm finger target ≈ 60 px at 1280×800.
Original buttons are smaller: extend their hit regions invisibly (not the art), resolve overlaps
to the nearest centre, and show a touch ripple on the hit. No action fires on touch-down: fire
on release, cancel by sliding off (XAG pointer rule).

Single-point fallback (Proton, emulated mouse): tap, long press (press duration), drag and the
loupe still work; pinch and two-finger pan are hidden. The device detector treats a click with no
preceding motion at a new spot as touch.

Expected strengths: the fastest way to point on a handheld, natural for inventory and windows.
Risks: hands move between grip and screen, fingers hide small sprites, Proton touch limits,
tap-to-walk vs drag-to-pan conflict.

## 4. What is shared, what differs (build map)

| Piece | A | B | C |
| --- | --- | --- | --- |
| Foundation §0 (detection, glyphs, prompts, selector, log, scale) | yes | yes | yes |
| Focus maps for every original window | **required** | optional (D-pad) | optional |
| Proximity targeting + sticky highlight + cycling (lab) | **required** | snap only | selection state |
| Virtual cursor with assists | reticle only | **required** | — |
| Hotkey radial and hub radial (lab) | yes | yes | yes |
| Touch hit areas, gestures, loupe, Confirm | — | — | **required** |
| Inventory grid move | focus + footprint | original drag | touch drag |

Suggested build order: foundation → B (cheapest, proves the foundation on original windows) → A
(reuses lab, adds focus maps) → C (needs the Deck run decided, §6).

## Issue map

All work items are GitHub issues under the epic [#1371](https://github.com/Simarglok/arcanum-steamwoven/issues/1371)
(task code `ARC-GPAD-<number>` unless the issue's label says another type). Build order: foundation, B, A, C.

| Piece | Issues |
| --- | --- |
| This document and coverage rows | [#1372](https://github.com/Simarglok/arcanum-steamwoven/issues/1372) |
| Foundation §0: core, viewer wiring (done), pause and accessibility, evidence log | [#1373](https://github.com/Simarglok/arcanum-steamwoven/issues/1373), [#1374](https://github.com/Simarglok/arcanum-steamwoven/issues/1374), [#1375](https://github.com/Simarglok/arcanum-steamwoven/issues/1375), [#1376](https://github.com/Simarglok/arcanum-steamwoven/issues/1376) |
| UI scale on 1280×800 | [#1377](https://github.com/Simarglok/arcanum-steamwoven/issues/1377) ([result](handheld-ui-scale.md)) |
| Focus layer; radials | [#1378](https://github.com/Simarglok/arcanum-steamwoven/issues/1378), [#1379](https://github.com/Simarglok/arcanum-steamwoven/issues/1379) |
| Scheme B Pointer | [#1380](https://github.com/Simarglok/arcanum-steamwoven/issues/1380), [#1381](https://github.com/Simarglok/arcanum-steamwoven/issues/1381) |
| Scheme A Direct | [#1382](https://github.com/Simarglok/arcanum-steamwoven/issues/1382), [#1383](https://github.com/Simarglok/arcanum-steamwoven/issues/1383), [#1384](https://github.com/Simarglok/arcanum-steamwoven/issues/1384), [#1385](https://github.com/Simarglok/arcanum-steamwoven/issues/1385) |
| Scheme C Touch+pad | [#1389](https://github.com/Simarglok/arcanum-steamwoven/issues/1389), [#1390](https://github.com/Simarglok/arcanum-steamwoven/issues/1390) |
| Deck build path; physical check of the lab (cancelled) | [#1386](https://github.com/Simarglok/arcanum-steamwoven/issues/1386), [#1388](https://github.com/Simarglok/arcanum-steamwoven/issues/1388) |
| Human test and decision | [#1391](https://github.com/Simarglok/arcanum-steamwoven/issues/1391) |

Requirements covered: FR-PRODUCT-002 (keyboard/mouse and semantic gamepad control), FR-UI-002,
FR-UI-003, FR-UI-004, FR-UI-005, FR-UI-006, FR-UI-007, FR-UI-008 (see the
[coverage matrix](../requirements/coverage.md)). The earlier issue
[#1173](https://github.com/Simarglok/arcanum-steamwoven/issues/1173) (gamepad in the game UI) is covered by
the focus-layer and window slices.

## Foundation core (ARC-GPAD-1373, merged parts)

The Godot-free parts of §0 live in `src/Arcanum.Input` and are tested in `tests/Arcanum.TerrainViewer.Tests`:

- `ControlSchemeSettings` and its codec (§0.1): `Pad` Off/Direct/Pointer, `Touch` Off/TouchPad, `Prompts`
  Auto/Xbox/PlayStation/Nintendo/SteamDeck; defaults are Off/Off/Auto; the lines live in the game's Options window (owner decision 2026-11-12, as the removed HD lines did: one `AreaOption` per line, saved in the game settings; [#1374](https://github.com/Simarglok/arcanum-steamwoven/issues/1374)); `OriginalActive` is always true, so no
  value of the record can switch the original controls off. A corrupt, oversized or foreign-version text gives the
  defaults; an unknown value of one field gives that field's default and keeps the others. The host owns storage.
- `ActiveDeviceTracker` (§0.2): "last meaningful input wins" over `DeviceSignal`s with the host's own time. Pad:
  a button, or an axis beyond the deadzone (0.3 by default; drift is ignored). Mouse: a click, a key, or motion over 12 px
  within 100 ms. Touch: a touch event. Pointer-from-pad: mouse motion alone within 2 s of pad use keeps the pad
  prompts and only reports `CursorVisible` (for 3 s). A click or key still switches. With emulated touch on, a click that
  jumps more than 24 px from the last mouse position with no motion before it counts as touch (the Deck touch
  screen under Proton). A click or motion within 0.5 s of a touch is the engine's mouse echo of that touch and is ignored. A change shows after 250 ms and is dropped if the active device is used again within that time.
  The tracker holds no window or focus state, so its consumer must not reset open windows on `Changed` (checked in
  the viewer slice, [#1374](https://github.com/Simarglok/arcanum-steamwoven/issues/1374)). The pad id of the last pad signal is
  kept (`LastPadDeviceId`), never assumed to be 0.
- `GlyphFamilyDetector` and `ControllerGlyphs` (§0.3): family from the override, then a Deck name (or the Valve vendor id on a Deck host), then the vendor id in the GUID
  (Sony, Nintendo), then name words; an unknown pad is Xbox. A pad behind Steam Input reports a virtual Xbox pad, so
  on a Deck host (`SteamDeck` environment variable, read by the host) an Xbox-looking pad is the Deck. This is a
  heuristic and needs a check on a real Deck. Labels are text per physical position (Nintendo's bottom button is B).

## Foundation in the viewer (ARC-GPAD-1374, merged parts)

The viewer hosts the core (`tools/terrain-viewer/Godot/TerrainViewerControllers.cs`, the Godot-free `ControllerFoundation` and
`PromptStrip` in `src/Arcanum.Input`):

- **Scheme selector.** Three project lines at the end of the Game page of the Options window, in the main menu and in play, the
  pattern of the removed HD lines: `Pad scheme` (Off, A Direct, B Pointer), `Touch` (Off, C Touch+pad) and `Prompt style` (Auto, Xbox,
  PlayStation, Nintendo, Steam Deck). One `AreaOption` each (`AreaGameOptions.cs`), saved with the game settings as the lines
  `pad scheme`, `touch scheme` and `prompt style` of the one settings file (`ViewerSettingsConfig`; a missing or unknown word is that
  line's default, Off, Off, Auto), applied at once with no restart; the original mouse and keyboard are not a value. The Game page now
  holds all eight slots of the original's art.
- **Device in hand.** Every input event of any pad (its device id, never assumed 0), the mouse, the keyboard and the touch screen
  goes to the `ActiveDeviceTracker` from `_Input` (`ObserveControllerInput`); `_Process` applies a change after the 250 ms
  hysteresis. The foundation holds no window or focus state, so a change of device or of a line resets no open window (checked in the
  options self-test and the options smoke). The glyph family is resolved each time from the name and GUID of the pad of the latest pad
  signal (`Input.GetJoyName/GetJoyGuid`), the `SteamDeck` environment variable and the Prompt style line.
- **Prompt strip.** One line at the bottom of the screen (layer 90, ignores the mouse), empty while the mouse or keyboard is the active
  device, so the original is unchanged (KM-01; no keyboard or mouse glyph is ever in a pad or touch line). With a pad in hand it names the
  pad family and the scheme ("PlayStation pad - A Direct", "Xbox pad - pad scheme Off (Options > Pad scheme)"); with touch the same
  ("Touch - C Touch+pad"). Once a scheme has prompts (`PromptBindings.ForPad/ForTouch`, none built yet: a prompt must never name an input
  that does nothing) the line lists them per context (`Modal` is the bottom line of every window, `Target` the near-target prompt) with the
  glyphs of the pad's family, rebound by the user's remaps (`PromptBindings.Remapped`, by prompt id).
- **Open, filed as issues:** the near-target prompt next to a highlighted world target needs the scheme's target highlight (with
  [#1380](https://github.com/Simarglok/arcanum-steamwoven/issues/1380) and [#1382](https://github.com/Simarglok/arcanum-steamwoven/issues/1382));
  the rest of the foundation: hold/toggle, pause rules and stick settings ([#1375](https://github.com/Simarglok/arcanum-steamwoven/issues/1375)),
  the evidence log ([#1376](https://github.com/Simarglok/arcanum-steamwoven/issues/1376)). The strip on a real Deck and with a real pad
  of each family is checked by a person (human gate of #1374).

## 5. Test protocol (human test)

**Devices.** P: Mac + Xbox Series pad (owner's) — schemes A, B. H: Steam Deck — schemes A, B, C
(Windows build via Proton unless a Linux build exists). Original M+KB is the baseline on the Mac.

**Content.** Only what the prototype already has: Crash Site exterior/cave, hero, interface bars,
inventory and loot, item cursor, character sheet, logbook, world map, save/load, combat with
health/fatigue and turn-based AP.

**Tasks** (each timed from the log; same order inside a scheme):
1. Walk from the crash site to a named point (long walk).
2. Find and examine three objects in a cluttered spot (targeting precision).
3. Loot a container, move two items into the inventory, equip a weapon (grid + drag).
4. Open character sheet → logbook → world map → back to play (window navigation).
5. Real-time fight: attack one enemy, use an item from a hotkey slot on it.
6. Switch to turn-based, move with AP preview, use an item on a target, end turn.
7. Quick save, quick load.
8. Handheld only: read a written item / examine text at the Deck's default scale (legibility).

**Order.** Rotate scheme order between sessions (Latin square: ABC, BCA, CAB); a 5-minute free
warm-up per scheme; same save file at start.

**Metrics.**
- Objective (from the evidence log): task time, wrong target / misclick count, cancels, refused
  actions, number of inputs, pauses.
- Subjective per task: Single Ease Question 1–7; per scheme: short raw NASA-TLX (mental, physical,
  frustration) and "what annoyed you" note; final ranking per device.
- Device checks: prompts always match the device in hand; no M+KB glyphs while on pad/touch; no
  ghost actions on pad connect/disconnect or window focus loss.

**Decision rule** (proposal): per segment, the primary scheme is the one with the best median
task time without a worse error count, unless the owner's ranking strongly disagrees; the
runner-up stays as a selectable option; a scheme that loses on both segments is retired, its
reusable parts (e.g. reticle, loupe) kept.

## 6. Open points for the owner

1. **Deck build path** ([#1386](https://github.com/Simarglok/arcanum-steamwoven/issues/1386)): test the Windows build under Proton (no native multi-touch) or add a
   Linux export to the package flow for real touch? Scheme C's full form depends on it.
2. **Followers** (related [#1352](https://github.com/Simarglok/arcanum-steamwoven/issues/1352)): party radial needs F1–F5 orders implemented in the session; until then it is
   a stub in all schemes.
3. **Text legibility on 7"** ([#1377](https://github.com/Simarglok/arcanum-steamwoven/issues/1377)): which UI scale of the original windows is acceptable on the Deck
   (relates to ARC-RES-093/094).
4. ~~Physical check of the existing lab~~ ([#1388](https://github.com/Simarglok/arcanum-steamwoven/issues/1388), cancelled 2026-11-12: the lab is no longer the base).
5. Whether the test may record the evidence log on the owner's devices (local, not committed, [#1376](https://github.com/Simarglok/arcanum-steamwoven/issues/1376)).

**Owner decisions 2026-11-12:** (1) the Deck runs the Windows package under Proton first, scheme C is built on
single-point emulated touch, a Linux export is not planned now; (2) the UI scale of the original windows is a whole
number with the smallest text at least 9 px; (3) the physical check of the old lab is cancelled, the schemes are
checked on the new builds, and the lab is a reference only; (4) the followers' party radial item stays a stub; (5) the
evidence log may be recorded on the owner's devices (local, opt-in, never committed).

## Sources

- Larian/Elverils — Adapting DOS2 DE for iOS (touch decisions: no hover, long press info,
  magnifier, Confirm instead of double tap, two-finger scroll, no hold-to-drag):
  https://www.gamedeveloper.com/game-platforms/in-depth-adapting-divinity-original-sin-2-definitive-edition-for-ios
- Game UX: the cursor that wasn't supposed to be there (magnetism, smoothing, when a cursor fits):
  https://uxdesign.cc/game-ux-the-cursor-that-wasnt-supposed-to-be-there-f9369dafc451
- PlayStation Blog — BG3 direct and Cursor Mode, radials:
  https://blog.playstation.com/2023/09/05/how-baldurs-gate-3-adapts-its-expansive-rpg-gameplay-for-your-dualsense-controller/
- Wccftech — Pathfinder: Kingmaker console (no cursor in real time, cursor in turn-based):
  https://wccftech.com/pathfinder-kingmaker-console-preview/
- GamingOnLinux — PoE/Deadfire controller beta, Deck and on-screen keyboard (2026-11):
  https://www.gamingonlinux.com/2026/11/pillars-of-eternity-and-pillars-of-eternity-ii-deadfire-get-major-updates-with-controller-support-in-beta/
- Steamworks — Steam Deck compatibility review (glyphs, text size, text input):
  https://partner.steamgames.com/doc/steamdeck/compat
- Unity forum — Steam Deck touch: emulated click vs Touch API pass-through, Proton limits:
  https://discussions.unity.com/t/touchscreen-on-steamdeck-not-working/896903
- Godot forum — Linux build input on Deck broken by Device0 bindings:
  https://forum.godotengine.org/t/linux-build-inputs-break-on-steam-deck-but-windows-builds-work-on-steam-deck/139250
- Microsoft — Xbox Accessibility Guideline 107 (holds, chords, pointer up-event, sensitivity):
  https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/107
- Repo: [the original's controls](../requirements/original-keyboard-mouse.md) (KM-01, KM-02), [gamepad presets](gamepad-presets.md),
  [viewer status](../project/status/viewer.md) (current windows and HUD).

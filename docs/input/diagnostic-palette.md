# Diagnostic palette and the WORLD keyboard/mouse route (ARC-INPUT-001)

**Status:** delivered by ARC-INPUT-001 in the Terrain Viewer WORLD modes
(`world-diagnostic` and `crash-site-area`). Diagnostic tool, **not** original UI and
not a game control scheme. Physical keyboard/controller verdict: owner validation
ARC-INPUT-002 (checklist below). Requirements: [original keyboard and mouse](../requirements/original-keyboard-mouse.md)
KM-01, KM-02, KM-05, KM-06, KM-07, KM-09 (variant B, chosen 2026-10-04), KM-10, KM-11.

## What changed in WORLD

| Before | Now |
| --- | --- |
| F1 / HUD button / gamepad Start opened "input settings" | **Ctrl+Shift+F9**, the HUD button "Diagnostic palette (Ctrl+Shift+F9 / Start)" or gamepad **Start** opens the diagnostic palette |
| `F` Fit, `R` reset scene, `L` reload | palette buttons **Fit**, **Reset scene**, **Reload current source** |
| wheel and `+`/`-` world zoom | palette exact zoom: **100% (1.00)**, **200% (2.00)**, 300% (3.00) and the numeric editor (current zoom) |
| W/A/S/D moved the actor by screen sectors | keyboard has **no actor route**; the gamepad left stick keeps the eight screen sectors |
| left drag panned the camera; LMB could act as Confirm | LMB in the world does nothing yet (click-to-move is **PENDING**) |
| arrows panned with the camera inversion setting | arrows pan with **one fixed direction** (KM-05); inversion applies to the right stick only |
| Escape left fullscreen | Escape is the reserved original game-menu key; F11 still toggles fullscreen as a host window control |

Gamepad behaviour is unchanged: left stick movement, right stick camera, camera and
actor inversion settings, neutral gate/Follow/recenter, LT out / RT in zoom, Start.
The standalone (non-WORLD) viewer modes and the Gamepad Lab keyboard scaffolding
(`GodotInputAdapter` with `WorldRoute = false`) keep their historical lab keys; they
are not an original-controls gate.

## Palette

One bounded modal surface (`DiagnosticInputSettingsPanel`), opened and closed by any
of: Ctrl+Shift+F9, the HUD button, gamepad Start; inside it also Escape (Cancel) or
its Close button. Contents:

- **Fit (session view only)**, **100% / 200% / 300%** and the numeric zoom editor:
  camera ratio only (not world size, simulation, UI scale or physical 1:1). Startup /
  preferred zoom, persistence and Fit semantics are those of ARC-GPAD-004.
- **Reset scene**: actor and Follow return to the diagnostic spawn after the accepted
  step settles; preferences are kept.
- **Reload current source**: the generation-aware reload of the loaded source after the
  accepted step settles; a failed reload is shown as a failure and the old scene is not
  republished. Without a loaded source the palette says "Reload unavailable".
- **Show passability (read-only)**: the [passability overlay](../rendering/passability-overlay.md)
  checkbox (ARC-RENDER-008), OFF at launch, navigable while a movement session exists;
  without one it reads "unavailable" and is not navigable. The host binds it through
  `DiagnosticInputSettingsPanel.BindOverlay(available, on, text)` and receives one
  `OverlayToggled` event per user toggle.
- The status line reports how a palette action ended: "Reset scene done", "Reload
  done: generation N published", or "Scene FAILED: stage=… code=…" (the old scene is not
  republished).
- Input preferences (deadzones, sensitivities, camera image X/Y and actor screen X/Y
  inversion) and **Reset preferences** (input defaults and zoom 2.00; the scene is
  not reset).
- A short diagnostic summary (area kind, committed cell, phase, camera mode, last step
  assessment) and a status line for the last palette action.

Modal policy is the existing WORLD one: while the palette is open, new actor/camera
intent is gated, an accepted step finishes by its lifecycle, then the neutral gate
needs two complete neutral frames. Opening or closing the palette is not a reset or
reload and does not change grid authority, evidence, facing, camera, Follow or zoom.
No new pause/time rule exists: opening the palette mid-step gives the same movement
trace as releasing the stick at that time (self-test "equal modal trace").

## Keyboard routing

Engine-free seam: `src/Arcanum.Input/OriginalWorldKeyboard.cs`
(`WorldKey`, `WorldKeyRole`, `OriginalWorldKeyboard`, `DiagnosticPaletteChord`);
Godot boundary: `GodotInputAdapter.WorldRoute`/`PaletteOpen`/`ToWorldKey` and the
WORLD host (`TerrainViewerWorld.cs`).

1. **Chord first.** Every key event first meets `DiagnosticPaletteChord`. Only
   Ctrl+Shift+F9 with no Alt/Meta toggles the palette, once per fresh press. The
   consumed F9 owns its whole down/up pair: echo/auto-repeat and the release are
   consumed even when a modifier is released first, so no late F9, quick-slot or
   other action can leak. A press whose release is lost to a focus change never blocks
   the next chord. The chord opens only when the window is focused and no text entry or
   other UI owns focus; an open palette may always be closed by its chord (also from its
   numeric editor). Both Ctrl/Shift sides are accepted (Godot reports the modifier state
   on the F9 event).
2. **World route (palette closed).** Arrows: camera pan, fixed direction (the
   false/false image sign). Original keys from M/Q and KM-02 are **reserved, not
   implemented**: Escape, F1–F5, F7, F8, F12, NumLock, Enter, Space, Home, 1–0, A, C, F,
   I, L, O, R, S, T, V, W, and the CE-source-only F6, F10, E, K, M, plus held `,` `.` `/`.
   A reserved key has no debug action and is not redirected; the HUD names the last
   one ("F1 = Followers: go to point (original; NOT IMPLEMENTED)"). Every other key is
   unmapped. Reserved/unmapped keys are not keyboard input-method events either.
3. **Palette route (palette open).** Up/Down navigate, Enter/Space activate,
   Escape/Backspace cancel (close); the numeric editor keeps its native digits,
   Backspace, Enter and Tab. W/S/Q/E/F1 are not palette keys.
4. **Enforcement.** The adapter route and a host-side filter
   (`WorldKeyboardRouting`) both remove any keyboard actor vector and lab pulses, so raw
   events, held-level sampling and the reducer cannot bypass the route. Bare modifiers
   and Ctrl/Shift/Alt+mouse outside the palette are not consumed.

Mouse: LMB press/drag/release in the world creates no intent; the wheel and `+`/`-`
do not zoom; palette/HUD clicks belong to native GUI dispatch and never become world
intent.

## Evidence

Software only (synthetic Godot InputEvents and simulated controller levels):

- Unit tests `tests/Arcanum.TerrainViewer.Tests/DiagnosticPaletteRoutingTests.cs`:
  chord ownership, exact modifiers, text/unfocused gating, lost release, reserved
  table, palette keys, bounded trace, fixed-sign arrows vs stick inversion.
- Godot self-test (`--self-test`, headless and graphical): `ARC-INPUT-001 palette chord
  matrix PASS` (order, modifier-first release, echo, re-press, both sides, partial/extra
  modifiers, held modifiers + original keys, text entry, palette text, focus loss,
  Start/keyboard, equal modal trace; leak check through the routed trace) and
  `ARC-INPUT-001 palette UI PASS` at **1280x800** and **2560x1440** (chord open,
  containment, field focus, overlay row unavailable, native clicks 1.00/2.00/Fit/Close,
  surface click, inert world LMB, opaque pixels when graphical). The WORLD lifecycle,
  modal, settings, zoom and grid matrices run on the new route; movement scripts use the
  selected controller's stick.
- Original-data regressions keep their reports; the `grid-original` report now carries
  `keyboardRoute: original-world`, a 96-case stick matrix and 24 `keyboardInert` cases
  (all eight former W/A/S/D sector chords at three viewports create no movement event).
- Export: literal task `ARC-INPUT-001` in the closed terrain endpoint, with every
  ARC-RENDER-007 gate (see [toolchain](../workflow/toolchain.md) and
  [local development](../workflow/local-development.md)); the synthetic gates require the
  ARC-INPUT-001 markers. Its ordinary mode is `crash-site-area`; `./arcanum area`
  prefers the current ARC-INPUT-001 build over ARC-RENDER-007.

**NOT VERIFIED:** native macOS OS delivery of Ctrl+Shift+F9, real keyboards/layouts/Fn,
physical controller comfort — `OWNER_KEYBOARD_VALIDATION_PENDING (ARC-INPUT-002)`.
Synthetic injection is not a physical press. No original gameplay key is implemented:
click-to-move, Followers, quick slots, game screens and called shots remain future work.

## Owner checklist (ARC-INPUT-002)

Run the exported ARC-INPUT-001 app (`./arcanum area`) on an ordinary Mac keyboard and
controller, then record the verdict on the ARC-INPUT-002 card.

1. **Chord:** Ctrl+Shift+F9 opens and closes the palette exactly once per press, with
   left and right Ctrl/Shift where the keyboard has them (Fn may be needed for F9).
2. **Release:** releasing keys in any order causes no second or late action; plain F9,
   F1–F5 and 1–0 do nothing in the world.
3. **Text field:** typing in the zoom editor stays in the field; keys do not reach the
   world, and the chord still closes the palette.
4. **Fit/zoom:** Fit, 100% (1.00) and 200% (2.00) give the expected view.
5. **Controller:** Start opens the palette; left stick movement, right stick camera,
   inversion and LT/RT zoom work as before.

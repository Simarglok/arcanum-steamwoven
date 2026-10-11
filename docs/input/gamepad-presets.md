# Gamepad Lab presets and comparison guide

ARC-GPAD-005 adds three selectable templates to the **same synthetic Gamepad Lab**, one Godot-free binding validator/reducer and one production scene/router. Contextual is the initial template, not a selected usability winner. All actions, objects, resource counters, AP and screens here are **DIAGNOSTIC / SYNTHETIC**. This is not original-game inventory, combat, spells, skills, party AI, quests, time, save state or a Terrain Viewer integration. Keyboard/mouse retain the lab fallback; diagnostic WASD is not the [original product keyboard mapping](../requirements/original-keyboard-mouse.md).

## Built-in bindings

Face order is the spatial diamond **A / X / Y / B**, not A / B / X / Y.

| Template / held shoulders | A | X | Y | B |
| --- | --- | --- | --- | --- |
| Contextual, none or RB only | Interact | Inspect | Full ten slots / catalog | Back |
| Contextual, LB | 1 | 2 | 3 | 4 |
| Contextual, LB + RB | 5 | 6 | 7 | 8 |
| QuickSlots, none | 1 | 2 | 3 | 4 |
| QuickSlots, LB or LB + RB | 5 | 6 | 7 | 8 |
| QuickSlots, RB only | Interact | Inspect | Full ten slots / catalog | Back |
| Radial, wheel closed | Interact | Inspect | Full ten slots / catalog | Back |

QuickSlots deliberately prioritizes the second bank over RB context when both shoulders are held. RB still highlights in either bank; holding it does not activate a slot. Modifiers are sampled with the selecting edge; changing shoulders while a face stays held does not reroute or repeat that activation.

Radial: a fresh LB edge opens the ten-sector wheel. Physical RS direction selects a sector, with 1 at up then clockwise through 10; exact halfway ties choose clockwise. Engage/release magnitudes are 0.65/0.45. A chooses the latched slot; RS returning to neutral retains that choice. A without a direction produces NoSelection, not a random slot. B or LB release cancels without applying; LB release plus A/B in the same observation also cancels. A selection never confirms a preview that it just opened. The wheel cannot reopen until a fresh LB edge. Camera inversion does not reverse sectors.

| Shared default | Behavior |
| --- | --- |
| LS | Synthetic actor movement; modal navigation or turn-grid preview in those contexts |
| RS | Camera pan; owned by the wheel while selecting and until genuine selected-device RS neutral after close |
| LT / RT | Camera-only zoom out / in, bounded 0.5–2.0, initial 1.0; full drive halves/doubles in one second |
| RB held / released | Contours and adjacent display-provider labels / hide them |
| D-pad Left / Right | Previous / next eligible target; not shoulder target cycling |
| D-pad Up / Down | Synthetic battle / follower command surface |
| L3 | Camera-only recenter; actor position unchanged |
| R3 | Target lock / unlock |
| View | Synthetic inventory / character / journal / maps hub and catalog |
| Menu / Start | Settings safe boundary |
| Guide / system button | Not intercepted |

Both sticks work concurrently in free world, including LS and RB while the wheel is open. The wheel is not a gameplay pause. It suppresses RS pan and fabricated-neutral Manual/recenter transitions, preserving camera mode/center; genuine RS neutral hands camera ownership back without same-frame pan. The established Follow/Manual policy, independent X/Y inversion, deadzones, sensitivities and Direct Intent/Nearby Goal comparison remain. Fresh lab inversion is true/true; WORLD remains false/false with its own files and defaults. F2 is the separate diagnostic **actor and camera** reset, not L3.

## Context priorities and action safety

Highest to lowest: untrusted/lifecycle quarantine; Settings/hub/catalog/battle/follower modal; pending action or grid preview; Radial wheel; free-world preset and continuous domains. Modal and pending A/B always confirm/cancel the current layer, even under QuickSlots. Simultaneous higher-layer cancel wins; entry to a higher layer consumes remaining lower-layer edges. Otherwise independent face edges use A/X/Y/B order. Held frames do not repeat one-shot actions; menu/grid navigation alone has bounded repeat.

Preset changes, copy apply/reset, modal transitions, focus and device lifecycle clear pending/chord/wheel/grid state. World input resumes only after **two consecutive genuine complete selected-device frame-neutral observations**, then a fresh edge. Other devices, cached callbacks, incomplete/invalid polls, cleared adapter zeros, opposing controls and individually held triggers cannot manufacture neutral. No mouse/OS-cursor synthesis is used.

The HUD displays preset/template versus user copy, context, physical bindings, bank/slot/action, target/lock, confirmation and a bounded journal. The full-ten surface opens with Y (Contextual/Radial) or RB+Y (QuickSlots): choose `slot-01` through `slot-10`, then **Use / Assign / Empty / Close**. All ten slots can be assigned; 9/10 are not placeholders. Assign selects an item/skill/spell or catalog entry; Empty produces an explicit EmptySlot, never a neighboring slot.

Initial assignments: 1/5/9 self item; 2/6 enemy skill; 3/7 ally spell; 4/8/10 ground spell. Supported catalog IDs are `synthetic.item.self`, `synthetic.skill.enemy`, `synthetic.spell.ally`, `synthetic.spell.ground`; sneak/rest/turn-end/follower entries emit mock intents. Crafting/fate/graphics-mode explicitly emit Unsupported, not production effects.

A self or valid exact actor-target action records one MockActivated/resource event. Ground or missing-target selection opens a preview; a later fresh A confirms, B cancels with no resource use. Confirmation rechecks exact target ID, revision, kind and context epoch; removed, hidden, ineligible or stale locked targets do not silently become another nearby object. Explicit cycling/reselection or unlock is required. Highlight eligibility is separate from action eligibility; hidden/scenery fixtures are not revealed. Names come from an injected synthetic display-label provider, not ART filenames/prototype IDs or invented original names. Missing labels are diagnostic. Contours/labels follow actual world projection, are clipped/culling-aware, and use deterministic adjacent/scrolling-overflow packing; UI scale changes text independently of camera zoom. Presentation bounds do not decide collision or target eligibility.

In **D-pad Up → Turn-based comparison**, LS previews a bounded 0–9 grid destination; fresh A records MockMoveAP, B cancels. The literal initial example is (4,4) → right preview (5,4) → one AP event on confirm. Diagonals use dominant-axis/X-tie policy. This is not production pathfinding, passability or combat.

## Settings, user copy and reset

Open **Menu/Start** or **F1**. Navigate with D-pad/LS or arrows; A/Enter chooses, B/Escape closes. Choose `Preset Contextual`, `Preset QuickSlots` or `Preset Radial`; positions, camera mode and zoom are preserved. Release every held control and allow genuine neutral before resuming.

1. `Create user copy` creates/replaces the single copy from the active template's base. It does **not** activate it.
2. `Use user copy` activates that saved copy. `Edit bindings` edits one world selecting-button row at a time. Selecting an occupied button rejects; the explicit `Swap with …` command exchanges occupied rows atomically for that modifier/context. The HUD identifies actual bindings.
3. `Swap shoulder roles` exchanges LB/RB roles. Modifier tokens remain semantic roles; Radial wheel-opening selecting rows move with their role. Non-wheel custom selecting buttons stay physical. Full validation still runs before save/apply.
4. `Reset user copy` restores only that copy's base template. To leave the copy, choose a built-in preset.
5. `Reset defaults` restores all lab preferences, Contextual and default slot assignments; actor/camera positions, mode and zoom stay preserved. `Recenter camera`/L3 is separate; F2 deliberately resets actor and camera.

Menu→Settings, modal A/B, fixed LS/RS domains and system-button exclusion are not editable. Unknown/malformed/conflicting/duplicate/unreachable/reserved bindings reject before apply; failed save retains previous committed preferences. The GUI is a scoped digital editor, not an engine-wide binding editor or Steam Input SDK; there is no ordinary file-import widget.

## Storage and reusable contract

[Bindings](../../src/Arcanum.Input/GamepadBindings.cs), [codec](../../src/Arcanum.Input/GamepadBindingCodec.cs) and [one reducer](../../src/Arcanum.Input/GamepadPresetReducer.cs) are Godot-free. Explicit opt-in physical-button fields preserve legacy positional constructors/deconstruction/type forwarders and historical semantic consumers. [The shared adapter](../../src/Arcanum.Input.Godot/GodotInputAdapter.cs) retains device/provenance filtering and common event/poll edge ownership. [LabPresetRouting](../../experiments/gamepad-lab/Scripts/LabPresetRouting.cs) is the single scene dispatch path.

`arcanum-gamepad-bindings-v1`, version 1, stores `id`, `basePresetId`, `shoulderRoles` and complete rows `{context, button, modifiers, actionId, slotId}`. Stable presets are `contextual`, `quick-slots`, `radial`, `user-copy`; slots are `slot-01`…`slot-10`. Buttons are `a/b/x/y/lb/rb/dpad-up/dpad-down/dpad-left/dpad-right/l3/r3/view/menu`. Contexts are `world`/`modal`; modifier tokens `lb`/`rb` denote roles, not Godot indices. Profiles are defensively immutable, with at most 128 rows, 64 KiB JSON and depth 16; duplicate/unknown fields reject. The validator enumerates exact finite button/modifier/context predicates and required reachability. Built-ins cannot be mutated.

Lab-only `arcanum-gamepad-lab-settings-v2`, version 2, stores all explicit axis preferences, string movement mode, active preset, nullable single copy, exactly ten assignments and UI 100/150/200%. Normal path is `user://gamepad_lab_settings_v2.json`. When v2 is missing, valid v1 is migrated without modifying/deleting v1. An existing corrupt/unknown v2 falls back to valid inverted lab defaults with diagnostics, **not** stale v1; corruption is not overwritten until an explicit save/reset. Atomic sibling-temp/rename saves retain old data on failure. This is preference persistence, not game save/load.

Explicit verification uses `GAMEPAD_LAB_SETTINGS_TEST_PATH`; its migration sibling is that path plus `.legacy-v1`, never the real user v1. Ordinary launches must remove test overrides. Common InputSettings remains version 1 and WORLD preferences/defaults are unchanged.

## Verification and ordinary artifact

Use [the lab checks](../../experiments/gamepad-lab/README.md#checks) and [pinned toolchain](../workflow/toolchain.md). Core literal traces are independent of generated binding tables. Actual source and exported Main.tscn GUI matrices cover all three presets × 1280×800/2560×1440/3440×1440 × UI 100/150/200% × zoom 0.5/2.0, including widgets, target/resource events, glyphs/Cyrillic, labels/projection/packing/edges, scale/resize return and lifetime plateau. Each origin has a native graphical baseline. Large tuples use a real non-headless SubViewport of the **same** scene: this proves logical scene/layout/raster, not physical monitor size, owner readability or comfort. Reports bind actual complete source/build/runtime bytes and invocation/exit; old dirty reports cannot be rebound to a later stable head.

The closed `ARCANUM_GAMEPAD_EXPORT_TASK=ARC-GPAD-005` uses the existing leased endpoint, a separate `gpad005` root and owned local home; 003 remains the default endpoint, readers preserve 001/002/003, and 004 stays terrain. See [local workflow](../workflow/local-development.md#gamepad-presets-artifact-arc-gpad-005). The export is local ad-hoc signed, not notarized distribution. Registration is not review/acceptance. The delivery receipt supplies the **actual full head, app path and owned home**; do not substitute an older 003 payload.

After registration, from the candidate repository and with that owned home:

```sh
export ARCANUM_LOCAL_HOME=OWNED_HOME
unset GAMEPAD_LAB_SETTINGS_TEST_PATH ARCANUM_GAMEPAD_CANDIDATE
tools/arcanum-local configure select-launch --tool gamepad --candidate gpad005:ARC-GPAD-005:FULL_HEAD
tools/arcanum-local launch gamepad --dry-run
tools/arcanum-local launch gamepad
```

`OWNED_HOME`/`FULL_HEAD` are receipt values, not literal commands to execute unchanged. Ordinary launch has no test/capture/fake-input/persistence arguments or preference overrides, and is separate from automated GUI checks. Keep runtime logs outside the immutable package; check manifest/evidence/payload digests and deep/strict codesign again afterward. **OWNER_PHYSICAL_VALIDATION_PENDING** remains true until the owner's actual session; technical synthetic checks do not choose a final layout.

## Owner comparison checklist

Repeat the same route with each built-in, then optionally its user copy. Record controller/device, preset and any explicit axis/UI choices locally; no telemetry upload or personal data is needed.

- Connect Xbox Series X|S Bluetooth; confirm selected-device HUD, all buttons/physical glyphs, drift and independent inversion toggles. Test focus loss, disconnect/reconnect, held sticks/shoulders/faces/triggers and safe modal resume; check no ghost cast/zoom/pan.
- Walk with LS while panning RS; check Follow → Manual → both-neutral → new actor-only recenter, independent LT-out/RT-in zoom and L3 camera-only recenter. Compare Direct Intent/Nearby Goal and wall/corner/narrow-passage stops; F2 reset is separate.
- Hold/release RB: visible interactive objects get readable adjacent Cyrillic/long labels/contours, hidden/scenery do not; camera zoom does not scale text. Check overlapping objects and viewport edges at UI 100/150/200%.
- D-pad cycle eligible near targets, R3 lock/unlock, then Interact/Inspect; check stale/removed/ineligible target refusal and keyboard/mouse fallback. LS alone must not interact.
- Use slots **1/4/5/8/9/10** and assign every slot through the full-ten surface. Check first/second banks, both shoulder orders, simultaneous shoulders, face-before-modifier, release and held nonrepeat. Empty must not choose another slot.
- Exercise **self/ally/enemy/ground**, missing-target preview, selecting-edge non-confirmation, fresh A confirmation and B cancel with no accidental resource effect. Highlighting alone must not apply an action.
- For Radial, test no selection, sectors 1/9/10, latch while RS becomes neutral, A choose, B/LB-release cancel, no immediate reopen and RS-neutral camera handoff while LS/RB remain live.
- Open battle/party commands, turn-based destination preview/confirm/cancel, **turn end**, View hub/catalog and modal/dialog-style choice/resume. Distinguish MockIntent/Unsupported from production screens/effects.
- Switch all three presets in Settings, edit/save/restart/reset a user copy, swap shoulder roles, return to template and Reset defaults; confirm positions preserved and fresh-process preferences match. Verify invalid edits leave old valid bindings intact.

| Compare for each preset | Record locally |
| --- | --- |
| Time-to-action | Same task and starting state |
| Wrong slot / wrong target | What was intended versus observed |
| Accidental application | Selecting/confirming/cancelling and resource event |
| Required stop of movement | Where a hand/action forced a stop |
| Hand comfort | Reach, grip and fatigue; owner's judgment |

Physical Bluetooth timing/drift/reconnect, comfort, comparative usability and final layout remain **PENDING / NOT_VERIFIED** until the owner records them; they are separate from agent technical pre-merge gates.

## Sources and provenance

Implementation is independently authored using synthetic fixtures. Reference concepts, not copied bindings/code or parity claims: [Blizzard D2R controller abilities/modifiers](https://news.blizzard.com/en-us/article/23722094/bringing-the-original-diablo-ii-experience-to-console), [Valve radial menu behavior](https://partner.steamgames.com/doc/features/steam_controller/radial_menus), [Godot 4.7 controller API](https://docs.godotengine.org/en/4.7/tutorials/inputs/controllers_gamepads_joysticks.html). The [movement study](gamepad-movement-study.md) retains the narrower movement evidence. No CE-derived implementation or original assets/names are packaged.

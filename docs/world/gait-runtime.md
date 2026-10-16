# Walking and running (ARC-WORLD-020)

Status: delivered in the Terrain Viewer `crash-site-area` mode. Rules come from the
[locomotion research](../research/locomotion-evidence.md) (ARC-RES-047); this note
states what the project implements and what it leaves out.

## Model

- A step is a walk or a run. The gait is a property of the step, chosen by the host
  when the step is admitted; the [grid movement contract](grid-movement-contract.md)
  does not change (its `MovementAction` stays Idle/Walk and every step keeps one frozen
  duration).
- `GaitTiming` (viewer Core, `ActorGait.cs`) gives the exact duration of one step per
  gait and direction: frames per cycle × frame time ÷ tiles per cycle, where the frame
  rate follows Speed (research L6; Speed 8 gives the middle value as in the original,
  anomaly LA1) and the tiles per cycle are the ART's frame offsets projected on the step
  (L7). The fixed diagnostic human at Speed 8: walk 29/53 s (547 ms) per axis step and
  58/75 s (773 ms) per diagonal step, run 25/149 s (168 ms) and 4/17 s (235 ms). This is
  a per-tile average, not per-frame parity (research U1).
- `AreaGridSession` takes `Timing` and a `Gait` callback. With timing, each permit uses
  the gait's duration and names it in its provenance (`GAIT_RUN` / `GAIT_WALK`);
  without timing every step keeps the fixed duration as before.
  `DiagnosticGridActor.Configure` gives a session its timing only when the caller passes
  no explicit duration, so tests with literal durations keep them.
- The presentation draws the run ART for a step whose permit says `GAIT_RUN`
  (`ActorDiagnosticAction.Run`), the walk ART otherwise; the action clock restarts when
  the drawn action changes and stays continuous otherwise. Run reuses the walk facing
  table (groups `[0,7,6,5,4,5,6,7]`), not independently verified. An HD pack without run
  frames draws those frames Legacy (the HD fallback).
- The AREA view loads `ActorDiagnosticProfile.HumanFixedRun`: the HumanFixed idle and
  walk resources plus the run ART (animation 6, resource1 ordinal 2671, 8 × 10 frames,
  20 fps; numeric metadata commitment `39f2a93d…`), 105 keys, 889,656 RGBA bytes; the
  AREA publication refuses a profile without the run resource. The stride constants are
  checked against the installed ART's frame offsets (opt-in test). The WORLD diagnostic
  and its closed RENDER-006 human validation keep the 55-key `HumanFixed`.
- Companions: a follower whose follow decision is a run (farther than range + 2,
  research L11) steps at the run duration; Virgil uses the human timing (his own ART
  stride is not read).

## Controls (research L1–L4)

| Input | Effect |
| --- | --- |
| Palette **Always Run on/off** | The original's Always Run option; saved in `user://always_run_v1.json` (`ALWAYS_RUN_TEST_PATH` isolates tests; automated runs save nothing). |
| **Num Lock** (fresh press) | Flips the always-run lock used while the option is off (the original reads the Num Lock state; the viewer starts it off). |
| LMB on the ground | Walks, or runs when always-run is in effect (option on, or option off and Num Lock on). |
| **Ctrl**+LMB on the ground | Runs once with the option off; walks once with the option on. On macOS a physical Ctrl+click arrives as a right press with Ctrl held and is taken as Ctrl+LMB. |
| LMB on an object (talk, use, pick up, loot) | The approach always runs (the original's behaviour, LA3). |
| Gamepad stick | Walks, or runs when always-run is in effect (no Ctrl). |

The gait is set when a route or approach starts; clicking the same goal again with
another modifier changes it for the next steps (the original also updates the running
goal). The controls act only in `crash-site-area`: in `world-diagnostic` Num Lock stays a
reserved key and the palette row says it is unavailable. The HUD and the palette show
the option, the Num Lock state, what the next click does and the current gait. Mac
keyboards have no Num Lock key; the palette option works everywhere.

## Not implemented

Fatigue and encumbrance are not modelled, so running is never refused (L5) and costs
nothing; turn-based AP and the combat-mode fatigue counter (L9) wait for the combat
round. No stealth walk, no Speed from the session hero (the profile is fixed at Speed
8), no Shift-click flag, no window jump (ARC-WORLD-021). Parity NOT_VERIFIED.

## Checks

- `ActorGaitTests` (frame rate by Speed, exact durations, run resource only in the AREA
  profile, run permits and the drawn run action, fixed duration without timing) and
  `AreaCompanionTests.AFarCompanionRunsAndANearOneWalksWithGaitTiming`.
- Native self-test (`ARC_AREA_GRID_PROBE=1`): route case `gait` (plain click walks,
  Ctrl runs, Num Lock and the option, Ctrl walks with the option on, the macOS right-press
  form, object approach).
- `ActorGaitTests.StridesAreTheFrameOffsetSumsOfTheInstalledArt` (RealData, opt-in
  `ARC_AREA_ORIGINAL=1`, `ARC_AREA_RESOURCE1`).
- Native smoke on the installed original: `ARC-WORLD-020 native-smoke run PASS` (Ctrl
  click runs 9 steps in 1.578 s with the run ART drawn, a plain click walks back in
  5.151 s, Num Lock makes a plain click run; `ARC_AREA_RUN_CAPTURE=PATH.png`).

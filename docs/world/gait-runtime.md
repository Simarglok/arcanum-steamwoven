# Walking, running and window jumps (ARC-WORLD-020, ARC-WORLD-021)

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
- The AREA view loads `ActorDiagnosticProfile.HumanFixedArea`: the HumanFixed idle and
  walk resources plus the run ART (animation 6, resource1 ordinal 2671, 8 × 10 frames,
  20 fps; numeric metadata commitment `39f2a93d…`), 105 keys, 889,656 RGBA bytes (150 keys with the
  ARC-WORLD-021 vault, below); the
  AREA publication refuses a profile without the run or vault resource. The stride constants are
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

## Window jump (ARC-WORLD-021)

- Data: on whole-map step evidence ([ARC-WORLD-018](area-step-assessment.md#whole-maps-arc-world-018))
  a cardinal step across a window wall piece (10, 13, 14, 17–19) is Open with reason
  `WINDOW_JUMP` and `AreaStepAssessment.WindowJump` naming the window, unless the
  window portal on that edge or anything else decides it (research W1–W3). Diagonal
  steps across a window stay blocked. A window portal that blocks (jammed, held,
  always locked) or is Unknown still decides; `WindowJump` names the window on those
  results too, so the companion overlay, which can open a step whose only gap is a
  moved record, keeps it a jump. Pinned profiles (the Crash Site exterior and cave)
  keep the Legacy rule: an opening piece stays Unknown.
- Route: `AreaRouteSource.ExtraCost` charges 50 for a window step; the straight attempt
  never takes it (research W7).
- Viewer: a window step takes the vault time, 9 frames × 125 ms = 9/8 s (the ART rate,
  no Speed scaling), whatever the gait, and its permit says `GAIT_VAULT`; the actor
  draws the vault ART (animation 13, resource1 ordinal 2677, 8 × 9 frames, 8 fps,
  commitment `e4f5ddec…`), played once over the step. `HumanFixedArea` (renamed from
  `HumanFixedArea`) now has 150 keys, 1,259,440 RGBA bytes. The anchor moves linearly
  across the step and the cell commits at its half, not at the vault's action frame
  (research W4); the next step resumes the gait; two vaults in a row each play from the
  first frame. A follower's window step takes the vault time too, but Virgil is drawn
  with his T0 frame, and whether his (unique-NPC) ART may jump at all (research W2) is
  NOT VERIFIED: his session reuses the PC's walker context.
- Where to see it: map mode ([ARC-RENDER-017](../rendering/area-map-mode.md)), for
  example `./arcanum map "fal'kins maze"` or `"vendigroth underground"`: the two complete
  interiors with window-jump steps reachable from their starts (22 directed steps). The
  Crash Site exterior and cave have none (the windows near the start are jammed,
  research D3–D4). The jump's
  own leaving test (research LA4) is not modelled: the evidence uses the portal's door
  rule, where a plain lock stays Unknown. The original-executable check found that the
  original really swaps the two tiles in that test (CE does not), so LA4 and the
  start-map count are being redone in a separate task.

## Not implemented

Fatigue and encumbrance are not modelled, so running is never refused (L5) and costs
nothing; turn-based AP and the combat-mode fatigue counter (L9) wait for the combat
round. No stealth walk, no Speed from the session hero (the profile is fixed at Speed
8), no Shift-click flag. Parity NOT_VERIFIED.

## Checks

- `ActorGaitTests.AWindowStepIsAVaultWithItsOwnTimeArtAndRouteCost` (synthetic
  whole-map window: vault permit and drawn action, the next step's gait, route cost and
  the blocked diagonal); `RoutePlannerTests.AWindowStepEndsTheStraightAttemptAndCostsFiftyInTheSearch`;
  `AreaWholeMapStepTests` (Data rule, window with a portal or blocked destination on
  the same step; opt-in census: 22 directed window-jump steps in 2 interiors);
  `ActorGaitTests.TheCompanionOverlayKeepsTheWindowJump`, `AVaultRightAfterAVaultPlaysFromItsFirstFrame`.
- Map-mode native smoke: `ARC-WORLD-021 native-smoke vault PASS` after the map stage (the
  first Open window step reachable from the spawn; the stick steps across, the permit is
  the vault, the vault ART is drawn mid-jump, the actor lands on the far side;
  `ARC_AREA_VAULT_CAPTURE=PATH.png`); observed on `fal'kins maze` (735,474)→(735,473) and
  `vendigroth underground` (2074,2021)→(2073,2021). A map without one prints
  `native-smoke vault NO-WINDOW`.
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

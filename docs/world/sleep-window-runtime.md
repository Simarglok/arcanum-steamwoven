# Sleep window runtime

Status: **Implemented bounded component (World rules, viewer host), bed and random encounter not modelled.** Work item ARC-WORLD-1160
(issue #1160, plan E3-a), 2026-11-12. The hero's rest: the key `S` and the game bar's Sleep button open the original's interface mode 6, a
window of lengths; a press runs a chain of one-hour steps that jump the game clock, heal the hero and his followers and end by the chosen
length. Universal: any map of the viewer's area mode with a session hero. No saved state is added (the original does not save the chain
either: a load closes the window).

Requirements: [FR-TRAVEL-004](../requirements/world-travel-runtime.md#fr-travel-004) (sleep and waiting have different conditions and
effects), [FR-LIGHT-002](../requirements/lighting-time.md#fr-light-002) (time flow and explicit wait).

Every rule was read in the retail 1.0.7.4 `Arcanum.exe` ([ledger SWB1–SWB13](../research/ledger/world.md#sleep-wait-and-beds-arc-res-1102),
[time loop TL27](../architecture/original-time-loop.md), ARC-RES-1102; the exit gate is
[RT2](../research/ledger/combat.md#real-time-combat-arc-combat-004)).

## What the window does

- **Open decision** ([`AreaRestRules.Decide`](../../src/Arcanum.World/AreaRest.cs), SWB2–SWB4): a dead hero does nothing; an open window is
  closed (the key toggles); an unconscious hero gets message 11; a town-map sector decides the kind (no town map: the **sleep window**, eight
  buttons, heals; a town map whose `townmap.mes` entry carries the wait marker: the **wait window**, seven buttons, no heal; a town map without
  the marker: message 20, refused); a hero in combat mode is asked the exit gate (an active NPC in the combat-focus state that has the hero or a
  follower as focus, or an attack being attempted, refuses with message 9) and otherwise leaves combat mode at once. A hero **not** in combat
  mode is never asked: enemies near do not stop the window. Other windows stay open when the decision refuses and are closed once it opens.
- **Lengths** (SWB7): 1, 2, 4, 8 and 24 hours, until 07:00, until 20:00 and (sleep window only) until healed. The labels are the player's
  `mes\sleepUI.mes` (entries 0..7, the wait window's 12..18, the messages 9, 11 and 20; the project's English text when the file does not read,
  [`AreaRestTexts`](../../src/Arcanum.World/AreaRest.cs)).
- **Press** (SWB8, [`AreaRestRun.Press`](../../src/Arcanum.World/AreaRest.cs)): the pending step is removed and the first is due 50 ms of real time
  later; the buttons are hidden; once per rest the screen fades to black (2.0 s in 48 steps, `AreaRestFade`) and the hourly light apply is held
  until the stop; script event 29 (leader sleeping) runs on the hero's followers
  ([`AreaUseScriptHost.RunLeaderSleeping`](../../src/Arcanum.World/AreaUseScripts.cs), the heartbeat admission set, its answer decides nothing).
- **Step** (SWB9, TL27): the game clock jumps an hour (due events fire once and late, as in a world-map step), the hero and every follower the
  AI host runs are healed ([`AreaRestParty.Step`](../../src/Arcanum.World/AreaRest.cs), `LifeRules.RestHeal`: hit-point damage drops by the
  healing rate, fatigue damage by three times that; nothing in the wait window), then the end tests
  ([`AreaRestRules.AfterStep`](../../src/Arcanum.World/AreaRest.cs)): the hero's death ends the rest; a length counts down to its last hour;
  until 07:00 and until 20:00 stop at the first step whose hour matches; until healed stops when hit-point damage is gone (fatigue damage is
  not looked at, and the unconscious state is not tested). A step that goes on is due 200 ms later.
- **Stop** (SWB11, SWB12): the key, another window's key, a changed session or the end of the rest removes the pending step, applies the current
  hour's light at once, fades the screen in and closes the window. Only the Esc menu pauses; the steps go on behind it. Nothing in the window
  reads damage or hostile arrivals (SWB13).

## Boundaries (NOT READY)

- **Bed** (SWB1, SWB5, SWB14–SWB16): no seating of the hero in a bed object, no bed use script point 1 test (message 8), no occupied-bed test
  (message 10), no kind-2 heal. Open data question: which installed beds (scenery of art type 4, sub-type 0x16) carry a use script, since a bed
  with no script on point 1 refuses the hero in the original (SWB5).
- **Random encounter** (SWB13): the encounter roll, which closes the window with five times the chance, belongs to the world journey (E2-c).
- **The sleeper's maintained spells** end at the press in the original (UE6, issue #1161); here they do not.
- **Followers of followers**, and followers the AI host does not run, are not healed or told of event 29.
- The exit gate's NPC scan uses an approximated view rectangle (`AreaAiHost.CombatThreats`); the original's is the game view's size.
- A town-map sector whose town-map files are not read yet (or cannot be) gets a notice, not message 20; the viewer cannot decide it (SWB3).
- No step runs under the death cover (the frame is frozen); the pending step waits. The animated-light push is also held during a rest.
- The chain is the window's own deadline (50 ms, then 200 ms), not an entry of the session's event queue, which has no real-time kind.
- The window is a diagnostic panel (no original art, 0x235 and 0x23D, no fonts); the fade covers the game view under the windows and the bars,
  its extent in the original is not checked.

Code: [`AreaRest`](../../src/Arcanum.World/AreaRest.cs) (rules, run, fade, texts, party heal),
[`AreaAiHost.CombatThreats`](../../src/Arcanum.World/AreaAiHost.cs) and `FollowerLifeTargets`,
[`AreaSleepPanel`](../../tools/terrain-viewer/Godot/AreaSleepPanel.cs) and
[`TerrainViewerAreaSleep`](../../tools/terrain-viewer/Godot/TerrainViewerAreaSleep.cs) (opening, keys, steps, the light hold, the bar button).
Tests: [`AreaRestTests`](../../tests/Arcanum.World.Tests/AreaRestTests.cs); the native smoke `ARC-WORLD-1160 native-smoke sleep PASS` (stage
`sleep`, [`TerrainViewerAreaSleepSmoke`](../../tools/terrain-viewer/Godot/TerrainViewerAreaSleepSmoke.cs)): in the wilderness sector 1384,239 the
sleep window and its eight game-text labels, the exact hour jump and heal of a one-hour rest, until healed 200 ms apart, until morning at hour 7, the
toggle and the cover; at the Crash Site start (a town-map sector with the marker) the wait window with no heal.

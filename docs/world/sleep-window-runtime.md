# Sleep window runtime

Status: **Implemented bounded component (World rules, viewer host); the hero's bed (ARC-WORLD-1162, slice 1 of E3-d: script, occupancy, kind-2 heal) and the NPCs' beds (slice 2: seat at the standpoint, wake, the latch) are built; the bed's occupied frame, the NPC sleeper's flags and the nudge (slice 3) are built but not yet compared with the original's picture ([owner re-check list](../project/owner-recheck-list.md)), the random encounter is not.** Work items ARC-WORLD-1160
(issue #1160, plan E3-a) and ARC-WORLD-1162 (issue #1162, E3-d, the hero's bed), 2026-11-12. The hero's rest: the key `S` and the game bar's Sleep button open the original's interface mode 6, a
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

## The bed (ARC-WORLD-1162, ledger SWB1, SWB3, SWB5, SWB14–SWB16)

A **bed** is scenery (object type 3) whose art id is of art type 4 with sub-type 0x16 (bits 6 to 10): the executable has no bed flag, list or type
([`AreaBeds`](../../src/Arcanum.World/AreaBeds.cs)). A left click on one is a use click (the hero's use goal 38, the same approach and range test as an
exit object, [`AreaMapTransfers.Beds`](../../src/Arcanum.World/AreaMapTransfers.cs)); the act node hands it to the rest hook, which here is
`TerrainViewerApp.OpenSleep(bed)` (the key S and the bar button call it with no bed). The same open decision runs with the bed, in the original's order:

1. The gates of the ground rest: a dead hero does nothing, an open window is closed, an unconscious hero is refused (message 11), a hero in combat mode
   is asked the exit gate (message 9) and otherwise leaves combat mode at once. **A call with a bed skips the town-map test** (SWB3): a bed is usable in a
   town whatever its `townmap.mes` entry says, and it is never the wait window (eight buttons, until healed included).
2. **The bed's use script** (attachment point 1, the hero as triggerer, [`AreaUseScriptHost.RunBed`](../../src/Arcanum.World/AreaUseScripts.cs), the trap
   meeting and effects as for any use): the answer **1** refuses the hero with message 8 and nothing changes. The answer 1 is what the script entry point
   gives for *run default* and **for an object with no script on the point** (SA10), so a bed with no use script refuses the hero (owner decision
   2026-11-12 after the census below). Any other answer (*skip default*, a normal end, a trap that ended the call) goes on. The hero has already left combat
   mode when a bed refuses him (SWB5). A run the host cannot apply (a script it refuses, a teleport or a conversation asked from a bed) answers nothing:
   the status line says so and the bed is not used.
3. **The seat** ([`AreaBedOccupancy.Seat`](../../src/Arcanum.World/AreaBeds.cs), 0x45F2D0): an occupied bed answers no and the hero is refused with message
   10; otherwise he is the bed's occupant. A mode change that is refused takes him out again (SWB6).
4. The sleep window opens as on the ground (eight lengths) and its steps heal with **kind 2** (`AreaRestRules.BedHealKind`: hit-point damage drops by twice the
   healing rate, fatigue damage by three times that, SWB9, SWB10). The close (the end of the rest, the toggle, a replaced session) takes the hero out
   (`Unseat`, 0x45F3A0) and empties the bed.

**What the installed data holds** ([`BedCensusRealDataTests`](../../tests/Arcanum.World.Tests/BedCensusRealDataTests.cs), opt-in `DAT_ROOT`, numbers only;
2026-11-12, the 80 maps of the catalogue that a snapshot holds; the main map is not read): **191 placed beds**, all of one prototype (basic number 4045, eight art numbers for the styles). **19 carry a script on point 1 and 172 carry none**, so in
the original 172 of them refuse the hero (message 8). The scripted ones are in four maps: Bates mansion level 1 (5 beds) and level 2 (6), script 3023 (a
conditional script of three lines that ends *skip default* on one branch and *run default* on the other); Roseborough inn2 (4 of its 15) and Wheel clan (3 of
its 29), script 1990 (32 lines with both ends, so the answer depends on state); the Crash Site cave (1 bed), script 1893 (a single line that ends *skip default*:
the hero may lie down). The bed prototype itself holds no use script; the scripts are set on the placed beds. The main world map (621 sector files, more than a
snapshot holds) is **not** read by the census; the 80 maps are every other map of the catalogue. The smoke stage `bed` pins the cave's bed.

## The bed's picture (ARC-WORLD-1162 slice 3, ledger SWB15)

A bed that holds a sleeper is drawn at **the next frame of its ART** (0x43F7F0 steps the art id's frame number, bits 14 to 18, to the next frame; the frame after the last wraps to 0 as the scenery loop's does). The area builder
decodes the other frames of a bed's ART although a bed carries scenery flag 0x1 ([`AreaBedArt`](../../src/Arcanum.Data/Area/AreaBedArt.cs); no loop is made of them), and the viewer draws an occupied bed with the plan's
next-frame command (`TerrainViewerApp.AddOccupiedBeds`, the same draw path as the scenery loops' frames), redrawing the beds when the occupancy's revision changes. The sleeper itself is not drawn (object flag 0x100,
the body styles' don't-draw rule). The hero is not drawn differently: his window fades the screen and the bed is empty again at the close. Only the frame **numbers** are checked ([owner re-check list](../project/owner-recheck-list.md#arc-world-1162--beds-sleeper-flags-occupied-bed-frame-and-nudge-slice-3-2026-11-12)); what the frame looks like, and the offset of the occupied art against the empty one, have not been compared with the original.

## NPC beds (ARC-WORLD-1162 slice 2, ledger SWB16–SWB21)

An NPC lies in a bed through the standpoint step, as the original's: the step ([`NpcRouteRules`](../../src/Arcanum.Simulation/Ai/NpcRouteRules.cs), forced at a wake-up and unforced in the routine) puts an NPC within its
radius (1 tile, 4 for a wanderer) of the current standpoint **at** it; the AI host ([`AreaAiHostBeds`](../../src/Arcanum.World/AreaAiHostBeds.cs)) then does what 0x4ACDB0 does there:

- **The latch (NPC flag 0x200)** is set once: while it is clear the step looks for a bed on the standpoint tile (0x4AD140: the finder tests neither the hour nor the NPC) and seats the NPC (0x45F2D0). The seat's answer is
  not read, so an NPC whose bed is taken (by the hero, by another NPC) keeps the latch, stands there and is not tried again until it leaves its radius (the far branch clears the latch). The latch is host state and is
  not saved; the first bed question of a host (below) settles the latch, the occupancy and the saved flags.
- **A seated NPC** is the bed's occupant in the occupancy the hero's rest uses, so a bed an NPC sleeps in refuses the hero with message 10, and its critter flag 0x1000 and object flag 0x100 (not drawn) are session writes (the saved `objectFlags`, no schema
  change); the host's sleeping readers see it asleep (`AreaAiHost.Sleeping`: the danger update, the item search and the heal skip a sleeper; the AI gate only lets a sleeper past its hidden test, so a sleeping NPC still reaches the route steps, which is how it is woken). An NPC that stands on the bed's own tile is moved to the neighbour tile in direction 4 (x + 1, y + 1) when the shown map holds that tile (the same placement test as every other relocation of the host: the tile is admitted and the hero is not on it; the original tests only that the tile exists), with no animation (0x45F2D0 ends with 0x43E770); any other NPC stays where it is. A seated NPC answers 1 on every later step and
  does not move.
- **The wakers** (SWB18), each through the re-seat (0x4AD0B0: the bed at the night standpoint, then at the day standpoint; a sleeper that lies in neither is not woken, SWB17): the waypoint step of a sleeper with a list for the
  period, the standpoint step of a sleeper farther than its radius from the current standpoint (the hour switch at 06:00 or 21:00: the NPC day test is hours 6 to 20, SWB20, and the step's 1-in-1000 roll at hours 6 and 21 spreads the
  town's waking), the attack event's victim and a sleeping witness of an attack. The unseat clears the flag 0x1000 and the latch; the waking step answers 1 and moves nothing, the next step walks the NPC.
- **After a load or a host change** the occupancy (shared, not saved) and the session's saved sleeping flags can disagree, so the first bed question of a host (a route step or a wake) settles them: an NPC of the host that holds a bed
  without sleeping, or is dead or gone, is taken out; then every NPC the session saved asleep is seated again in the free bed of its nearer standpoint, all of them before any step seats a new one. A killed NPC leaves its bed at
  once (a project choice: the original's handling of a dead occupant is not read). A sleeper that is saved asleep but whose bed was lost anyway (taken by the hero, say) is woken by its next re-seat instead of sleeping for ever;
  the original cannot reach that state, its occupant field being saved.

**What the installed data holds** ([`BedCensusRealDataTests`](../../tests/Arcanum.World.Tests/BedCensusRealDataTests.cs), numbers only): of the **2236** NPC rows of the 80 read maps with a readable route, **41** have their
night standpoint on a placed bed's tile, **21** their day standpoint and **51** either, so the step seats them.

## Boundaries (NOT READY)

- **Bed, what is left** (SWB15, SWB16): the hero gets neither the sleeping flag nor the don't-draw flag nor the nudge (his window fades the screen over him and the bed is emptied at the close; a flag written
  into a saved session could outlive the window); the occupied frame's pixel offset adjustment of 0x43F7F0 is only the frame's own hotspot difference; the bed's picture is not compared with the original's yet.
- **NPC beds, what is left** (SWB18d, SWB19–SWB21): an attack aimed at the bed (SC16: the occupant is taken out and becomes the target) does not wake its occupant; the near branch's wander start is not modelled (a seated
  wanderer would start one in the same step); the latch is not saved; the attack resolver's and the turn-based round's sleeping tests (+hit chance, round skip) read the stored critter flag only, so a sleeping NPC the
  step seated is skipped by the danger update, the item search and the heal but not by those two; NPCs of the main map's windows are seated only for the beds the window holds; an NPC a script moves or destroys while seated is not taken out (only a kill and the first bed question of a host do); an NPC still walking to its standpoint when it comes within the radius is seated without an interrupt of its walk, so it may finish the walk onto the bed's tile (hidden, not nudged: the nudge is the seat's own test; not read in the original); an NPC whose map row already has the sleeping flag is never seated (it already counts as asleep, so its step is the sleeper branch).
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

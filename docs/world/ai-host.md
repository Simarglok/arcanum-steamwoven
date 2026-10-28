# NPC AI viewer host: the first fight (ARC-WORLD-092)

Status: **Implemented bounded component**, ARC-WORLD-092, first part of NPC combat AI plan slice
[I10](../research/npc-combat-ai-plan.md#implementation-slices-proposals-after-the-matching-research) ("first fight"): in the AREA
viewer, NPCs of the shown map fight the hero in real time with their melee attack. It is the first host of the scheduler
([AI scheduler](ai-scheduler.md), I6), the executor ([AI executor](ai-executor.md), I7), the danger, hostility and morale rules
([danger state](ai-danger-state.md), [hostility](ai-hostility.md), [morale](ai-morale.md), I3–I5), and it adds the session's NPC
hit step. Exe facts read by this task: ledger [AH1–AH4](../research/ledger/npc-ai.md#npc-ai-viewer-host-arc-world-092) (one reader,
F1). It covers the first NPC fights of [FR-AI-001](../requirements/story-social-ai.md#fr-ai-001) and
[FR-AI-002](../requirements/story-social-ai.md#fr-ai-002). ARC-WORLD-095 (the next part of I10) lets the NPCs fight in their own
subturns of a turn-based round ([below](#turn-based-round-arc-world-095); exe rows
[TH1–TH6](../research/ledger/npc-ai.md#npc-ai-turn-based-host-arc-world-095), one reader, F1). ARC-WORLD-099 (the first part of
I10's third step) adds the round's tail: the end-combat script at an NPC's subturn end, the completion check of a turn set's
interrupt and the fidget after a done run (exe rows [TH7–TH10](../research/ledger/npc-ai.md#npc-ai-turn-based-host-tail-arc-world-099),
one reader, F1).

## Session: the NPC hit step (`GameSessionOwner.AttackByNpc`)

| Operation | Original | What it does |
| --- | --- | --- |
| `SessionCritterCombat.AttackValues(status, state, profile, wornNow)` | 0x465F70 (AH1) | What an NPC strikes with: its wielded weapon (the map data's worn items the host says it still wears: a weapon a critical dropped or destroyed no longer strikes), else a monstrous critter's (0x45F730) own damage table (NPC field 0x12F, minimum at 2 × type, maximum at 2 × type + 1), else bare hands with the gauntlets' unarmed bonus (field 0x9E of the item at location 1007, read for melee only); the weapon's skill with the stored basic skill's level and training (melee 20 and the tiers for a monstrous critter; a levelled follower's sheet); the weapon speed, attack cost, Speed and reach. Supported: melee. |
| `AttackByNpc(expected, SessionNpcAttack)` | hit step 0x42A630 → 0x4B3BB0 → resolution 0x4B3170 | The NPC (attacker) on the hero (`Target` null) or another NPC: the resolver under the active rules and difficulty (`combat.*` rolls); the hero's armed "critical miss" fate turns it into a critical miss and is used up in a revision of its own (FT6); the damage to the target, or to the attacker on a critical self-hit; on an NPC target's hit-point damage the pool share, which the pool loses although only a PC attacker gets it (XP10), and on its death the kill award to the attacker's PC leader (a follower or a waiting follower of the hero), else nothing and the worth stays (XP9); then the critical and wear steps with the NPC as attacker (drops at the NPCs' cells, the hero's among its placed items). Refused before any roll: no hero, values that do not hold, an attack on itself, wear items that do not hold, a distance beyond the reach, a drop cell outside the packing, an attack other than melee (`AttackUnsupported`), the version, the revision. A resolver refusal is NoChange. |

No new state: the snapshot schema stays as it is.

## Viewer host (`Arcanum.TerrainViewer.Core.AreaAiHost`)

One host per shown map, built when the session, the map or the loaded area changes (a load or a map change builds it again: goal
runs and NPC positions are not saved, so every NPC stands on its map data's cell again). It answers in world cells; the planners
take chart cells.

| Part | What it does |
| --- | --- |
| Ticks | Every NPC of the map that is not a companion (not in the companion group, not led by the hero) and not dead gets a delayed AI tick (0x4AD6E0, the loaded NPC's start) unless one is pending. The viewer's event world (`AreaEventWorld`) forwards the tick's host questions to the host of the current session, also through the cast host's pass world (`SessionSpellCaster`); without one an NPC counts as gone and its sector as not loaded, so the tick schedules no next one. The host is built again when a follower joins or leaves; an NPC that joined since counts as gone. |
| Scheduler host (`IAiHost`) | Distance to the hero; no SCR at the AI points (every point continues), no waypoints, standpoints, spawner or encounter creatures; generator, sleeping and hidden from the NPC's flags; the animation priority of the goal its run is at (the goal table's header word 1, AH3), 0 without a run; an animation runs while it has a run. Heal and item search choose nothing yet. |
| Danger stage | `HostilityRules.DangerUpdate` and its plan through the session: the flee path's flee-spell step (no flee action is chosen), the unlock, and the setter (`SetDangerState`) with the NPC's setter facts (invulnerable, NO_FLEE: the stored flag ORed with the session bit, the leader's extraordinary charisma) and, for a fight on the hero, its reaction facts and AI parameter 10. No spell or item choice yet. |
| Perform stage | `AiExecutorRules.Perform` with the danger source as the pass's target and no choice; a started run replaces the NPC's run (the old one's end nodes run when its goal differs). |
| Runs | One `AiGoalRun` per NPC, stepped by `AiExecutorRules.Step` when due, then after its delay (0: the next frame); a killed run, the node limit and an overflow run the end nodes through `EndRun`, as a replaced run does. The runs go by the viewer's world clock (the clock of the hero's walk and attack): the milliseconds the host answers (`BeginStep`, `BeginAttack`) and the executor's delays are that clock's, which the executor's contract calls game milliseconds; the ticks go by the session's game clock, which runs 8 times faster. |
| Walker | The attack's, the follower's and the tile planners are the hero's route planner (`RoutePlanner.PlanApproach`, `Plan`) over the map's step evidence with a live overlay (the hero, the companions, the other walked NPCs; the target's own cell does not block); a flight's escape line is the straight line. A step is blocked by a living critter on the next cell (wait) or a refused step (cannot start). `BeginStep` puts the NPC on its next cell at once and slides its T0 body there over the step's time (the gait timing of the hero's grid). |
| Reach and lines | The line of fire of the shown map with the walked NPCs and the companions on their cells; reach is the weapon's (1 bare-handed) with a clear shot. |
| Attack | The attempt-attack goal's node 5 (0x42B9C0, AH2) puts the attacker into combat mode, and a PC target, or an NPC target's living PC leader, too; the animation's timing is the NPC's attack ART played at its Speed and weapon speed (`RealTimeCombatRules`; the fixed human attack ART, 9 frames, action frame 6, 15 fps, stands in: NPC attack ART is not read yet); at the action frame the hit step is `AttackByNpc`, then the attack event. |
| Attack event | `HostilityRules.AttackEvent` for the hero's resolved attacks on an NPC (the viewer's attack hook) and the NPCs' hits: a PC victim enters combat mode (0x4A9AD0, AH4); the noise's witnesses (`NoiseWitnesses`, `Witness`) react; the session's who-hit-me-last and team writes (`ApplyAttackEvent`); the victim turns; a deliberate hit lowers its reaction toward the hero (`ReactToPcHit`); its reaction (`MoraleRules.ReactToAttack`) asks the setter. |
| Hostility and morale world | `Find` from the NPC's fields, life, danger record and combat mode; parameters from the AI table row (out of the table: 0); sight and hearing (`AwarenessRules`) over the line of fire, no tile soundproof; faction-same as far as this host knows (the hero and its followers, one leader, one non-zero faction; the PC's reputations granting a faction are not read); kill-on-sight scripts allow. |
| Viewer | Per frame after the hero's attack: the runs step, the hits reach the status line (" | AI: …" with each fighting NPC's state, target, goal and last reason), a stun's end, a killed NPC's fall, and a hero knocked down or out stops its walk and attack; the walked NPCs are drawn where they stand (a fallen body too), block the hero's walk, stand in the line of fire, the drop map and the round's scan, and the hero's attack, loot and criticals find them on their cells. Automated runs keep the AI off unless their stage turns it on (`ARC_AREA_MAP_SMOKE_ONLY=ai`). |

## Turn-based round (ARC-WORLD-095)

The viewer's round host (`AreaTurnBased.Step` with the map's AI host, [turn-based round](turn-based-round-runtime.md#viewer-host-arc-combat-020))
runs an NPC's subturn through its AI; without a host (automated runs keep the AI off unless their stage turns it on) an NPC's
subturn still ends at once.

| Part | Original | What it does |
| --- | --- | --- |
| Subturn start | turn set 0x4B6E70, hook 0x4B7010 (TH1, TH3) | The session grants the action points (`max(5, Speed)`); the round host then runs the session's subturn hook (`RunAiSubturnHook`): the heartbeat (no SCR yet: it continues) and one AI pass (gate, danger update, perform). A pass that starts no run ends the subturn (`EndCombatTurn`); a started run holds it. |
| The run | stepper 0x423560 | The run steps on the world clock as in real time; each step and the attack check and pay the round's action points through the session (`ConsumeActionPoints`): a walk 2, a run 1 with fatigue, the attack its weapon's cost (EX6, EX21). A step it cannot pay kills the run. |
| The run's end | stepper's end 0x423B8D, completion check 0x4B7080 (TH4, TH5) | The NPC's next frame without a run runs the hook again (its next action), then, when no other run goes on and the hero is idle, the completion check (`CompleteCombatActions`, with its 2 ms turn event as one step): 0 action points pass the turn on, an actor checked twice with unchanged action points spends the rest. So an NPC whose start gate lets it begin an attack its points do not cover (fatigue above 35 %) passes on after at most two tries. |
| Interrupts | round start 0x4B71E0, turn set 0x4B6E70 (TH1, TH6, TH10) | `ObserveRound` (before each pump step, after the last, and on each subturn of a round frame) compares the session's subturn starts (`QueryCombatSubturnStarts`) with the last seen: a round that began interrupts every run, a new subturn the runs of the previous turn set's critter when it is an NPC, at priority 3 (walks, attacks and flights end with their end nodes; an attempted cast goes on). An attack that puts the hero into combat mode with the turn-based setting on starts a round and so ends its own swing. ARC-WORLD-099: a turn set's interrupt that frees the last run while the hero runs no animation (the scene's `HeroIdle`) runs the completion check (`CompleteCombatActions`, counted in `CompletionChecks`, a refusal in `CheckFailure`) for the new participant before its hook, as the free's callback does; a round's start runs none (its interrupts precede the callback's registration and the round flag), while a next round's first subturn is a turn set like any other (TH10). A check that passes the turn on is observed as the next turn set before the round host hooks anyone. |
| Subturn end | subturn end 0x4B7630 (TH7) | ARC-WORLD-099: every NPC `SubturnEnded` among the events of a round call (the executor's consumes and subturn ends, the host's check, the round host's hook, check and end calls) asks the NPC's end-combat script (point 16, the NPC as attachee and triggerer: `SubturnsEnded`, counted in the trace's `EndCombatScripts`); this host runs no SCR at the AI points, so the point continues. The PC's subturn end is the interface's. |
| Fidget | stepper end 0x423B95–0x423BA0, 0x4B4320, 0x435BD0 (TH8, TH9) | ARC-WORLD-099: after a done run (finished, end step, killed in its step; not the node limit or an overflow), after the owner's subturn hook (the round actor's: the round host's `FidgetAfterHook` right after the hook it runs next; anyone else's at once), `AiExecutorRules.FidgetAfterRun` decides the fidget: an NPC in combat mode outside a round or with critter flags 2 bit 0x2 (auto-animates), passing the start gate with cost 0, without a run; in a round only while the current participant has action points (in practice never: see TH9). The host reports it (`AreaAiNoticeKind.Fidget`, the viewer's log line `ARC-WORLD-099 ai-fidget`); its art test allows it and its goal-2 run (priority 1) is not held, since NPC ART is not read and nothing the host runs reads a priority-1 run. |
| Pending cost | 0x4B75D0, check 0x4B7790 (TH2) | The host's `PendingCost` reads 0 once a subturn started after it was written, whichever call started it (the subturn start's check of cost 0). EX6's end-step charge reads it. |
| Ticks | 0x4AD420 | In a round the AI ticks only reschedule (CL7: the near test needs no round), so NPCs think only in their own subturns; an attack still makes its victim react (the setter), not act. |
| Viewer | | The round log and the status line take the charges and subturn ends of the runs (`DrainRoundEvents`). Smoke stage `ai-turn-based` (only when named, map mode with `--hero`, `crash site cave`): the AI and the setting on, the NPC told of the hero's attack four cells away, R begins round 1; in its own subturns its run walks it up paying action points and bites, it never moves on the hero's subturn, which E ends; marker `ARC-WORLD-095 native-smoke ai-turn-based PASS`. |

A host built during a round (a load, a map change, a follower joining) asks the NPC whose subturn it is again (its run was not
kept). A9's repeated first subturn (Original setting) runs the hook once: the second start would interrupt the first start's run and
ask again, so the NPC acts once either way.

## Settings

No new switch. EX6, EX9, EX12, CL1–CL5, HS9, AW16, SP16, FO7, FO8 and the morale switches act through the rules they belong to.

## Not ready

- **Turn-based rounds.** The end-combat script at an NPC's subturn end is asked, but no SCR runs at the AI points (below). A run
  killed from outside its step gets the hook on the NPC's next frame too (the original runs none; nothing in this host does that to
  the actor's run); followers stand in their subturns (I9's host part). The completion check of a free runs after the actor's own run
  ended (after the hook, where the original runs it at the free, before the hook: the action points are the same, the pass charges
  nothing) and at a turn set's interrupt of the last run; a run killed in its step by a refused charge is freed by the original after
  the new participant's hook and checked then, which this host does not do (the check then only records the stalled-actor
  observation). The turn set's check runs after the session granted the new participant's action points (the original's before them):
  in the Original setting the stalled-actor observation keeps the previous value either way (A11); in Steamwoven it records the new
  participant's own points, as A11's fix does for every check. A round that ends and another that begins between two observations (one frame) skips the round start's interrupt
  of every run. The fidget's animation is not played and its run not held (NPC ART is not read).
- **Action goals.** Casting, item use, skills, throws and pickups (I8b's goals), grenades, NPC queued casts and spell missiles and
  goal 40: the host's calls refuse or do nothing; heal and item search choose nothing.
- **Followers in combat.** Companions keep their follow goal; the follower rule, the attack order's target and pass, the comment
  lines and the world-map travel's stop and allow calls are I9's host part.
- **NPC ranged attacks** (bow, throw, firearm, missile) are refused by the hit step (`AttackUnsupported`).
- **Scripts and routines.** No SCR runs at the AI points (heartbeat, start and end combat), no waypoints, standpoints or day
  routines; the danger hooks are not run.
- **Back-off.** The away path (0x41F3C0) is not walked: a back-off ends at once.
- **Drawing.** NPC walk and attack ART are not drawn (the T0 frame slides); attack sounds of NPCs are not played; the hero's fall
  and death after an NPC's hit are not drawn.
- **Positions.** Walked NPCs are not saved; the companions' own overlay does not see them; a talk approach goes to a walked NPC's
  map data's cell.

## Tests

`NpcAttackStateTests` (Session): the monster table, bare hands and gauntlets, a wielded weapon, a bow refused; the hero bitten and
paying nothing; an NPC victim's pool and the kill award for a PC-led killer only; the hero's critical-miss fate; the refusals.
`AreaAiHostTests` (viewer Core): the movers' steps and slide; on the installed original (`ARC_AREA_CONVERSATION_GAME_ROOT`,
`ARC_CAVE_MOBILE`) an animal of the Crash Site cave that the hero hits reacts, focuses the hero through its tick's danger update,
starts its attack run (adjacent, and from 4 tiles after walking up) and bites through the session; in a turn-based round it walks up
and bites in its own subturns, its run holding its subturn and paying action points, never moving on another participant's subturn,
and the pending cost reads 0 after a subturn start (ARC-WORLD-095); the done attack run's fidget in real time, the end-combat script
asked at its subturn ends and no fidget in a round, and a turn set's interrupt of its last run running the completion check once
(ARC-WORLD-099). `ExecutorRulesTests` (Simulation): `FidgetAfterRun`'s combat mode, auto-animate flag, running animation, art test,
PC, and in a round the action points and the gate. `CombatRoundTests` (Session): the subturn starts are counted
whichever call began them, A9's twice, and not saved. Native smokes: `ARC_AREA_MAP_SMOKE_ONLY=ai` on `crash site cave` with
`--hero`, marker `ARC-WORLD-092 native-smoke ai PASS` (since ARC-WORLD-099 also the fidget after the bite's run, `Fidgets` in the
marker); `ai-turn-based`, marker `ARC-WORLD-095 native-smoke ai-turn-based PASS` (since ARC-WORLD-099 also the end-combat script
asked at the NPC's subturn end, `EndCombatScripts`).

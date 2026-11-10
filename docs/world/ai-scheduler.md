# NPC AI scheduler: the tick, the pass and the subturn hook (ARC-WORLD-084)

Status: **Implemented bounded component**, ARC-WORLD-084, NPC combat AI plan slice
[I6](../research/npc-combat-ai-plan.md#implementation-slices-proposals-after-the-matching-research).
The scheduling half of the retail 1.0.7.4 executable's AI core loop ([core loop](../research/npc-ai-core-loop.md), stage A1;
ledger [CL1–CL13](../research/ledger/npc-ai.md#npc-ai-core-loop-arc-res-054), two readers, re-read by this task): when an NPC
thinks, what one AI pass checks before it acts, and how a turn-based round hands an NPC its subturn. The rules are drivers in
`Arcanum.Simulation.Ai.AiSchedulerRules` over an `IAiSchedulerWorld` and an `IAiRollSource`; the session (`GameSessionAi.cs`)
schedules the tick on its event queue, answers and writes the state it owns, and asks the host (`ISessionAiWorld`) for the rest.
The AREA viewer's AI host implements `ISessionAiWorld` ([AI viewer host](ai-host.md), slice I10): its ticks since ARC-WORLD-092, the subturn hook in a turn-based round since ARC-WORLD-095.

## Rules (`AiSchedulerRules`)

World members are called in the original's order (the far path asks whether the sector is loaded before the catch-up's facts,
as the original computes it first), so a host answers from the state the previous calls left.
Ids are the host's critter ids; `AiSchedulerRules.Pc` (`"pc"`) names the PC as leader, triggerer and round actor.

| Rule | Original | What it decides |
| --- | --- | --- |
| `TickDistance` | 0x4AD610 | The distance the delay counts: the distance to the PC, 30 when it is negative or larger. |
| `TickDelayMs` | 0x4AD5D0 | `250 + 4750 × d / 30` game ms, truncating: 250 next to the PC, 408 at 1, 1041 at 5, 1833 at 10, 3416 at 20, 5000 from 30 on. |
| `NextTickDelayMs` | (0x4AD200) | The delay a tick schedules: `TickDelayMs`, plus 0..5000 ms (`ai.TickWakeUpJitter`) when the tick carried the wake-up marker. |
| `IsNear` | 0x4AD420 | Alive, no round and at most 30 tiles from the PC; a negative distance is near (unlike the delay). |
| `CatchesUp` | 0x4AD4D0 | Alive, no round, a PC leader more than 30 tiles away (a negative distance is not), not waiting, not paralysed and active. |
| `Tick` | 0x4AD200 | `AiTickResult`: steps, next (`Delay`, `Spawner`, `None`) and delay. Wake-up of a living NPC: "check wield" set, a leaderless NPC outside combat mode runs its waypoints and, when they did nothing, its standpoints, then the first heartbeat (point 10); anything but "continue" takes the far path. Near: the heartbeat (point 19); on "continue" a generator asks the spawner (its delay is the next tick's; a refusal destroys an encounter creature outside a round and schedules nothing), any other NPC gets one `Pass`; the next tick follows whatever the heartbeat answered. Far: outside a round a living NPC's animations are interrupted; a follower that `CatchesUp` is moved to its leader; moved or on a loaded sector the next tick follows, otherwise an encounter creature is destroyed outside a round and no tick follows. |
| `Pass` | 0x4A84F0 | `AiPassResult`: none while the map clears its objects; the danger state and source are read, then the `Gate`; when it passes, heal, else the item search, else the danger update, then perform. |
| `Gate` | 0x4A8570 | `AiGateResult` (`AiGateStop` reason and `AiGateSteps`), in the original's order: dead; hidden unless sleeping; the PC hidden; stoned; a conversation partner (with the PC as partner, the dialog callback set and the state COMBAT_FOCUS or FLEE, the conversation is ended); in combat mode the end-combat script (point 16, outside a round) and the start-combat script (point 15), the danger source as triggerer (P5); another critter's turn; an animation of priority above 2; blindness cleared on 1 in 160 (`ai.GateBlindRecovery`); every crippled condition cleared on 1 in 8000 (`ai.GateCrippleRecovery`); stunned ends the pass, clearing the stun first when its animation no longer runs (CL1); unconscious; a generator; "check wield" re-wields every slot and clears "check wield" and "check weapon" (CL2), else "check weapon" re-wields the weapon slot (1004); DEMAINTAIN_SPELLS with the leader missing, dead or out of combat mode is cleared and the maintained spells end. |
| `SubturnHook` | 0x4B7010 | `AiSubturnHookResult`: for the round's actor, when it is not the PC and no game loads, the heartbeat (the NPC as triggerer) and on "continue" one `Pass`; then, when no animation of it runs, its subturn ends. |

The blind, crippled and stun tests read the critter flags as they were before the conversation test (`AiCritterFlags`: the
conditions, FLEEING, SURRENDERED, SPELL_FLEE, the combat-mode bit and concealment), in both settings, so both draw the same
rolls. Their writes differ (CL5): the Original setting writes that copy back with the recovered bits cleared, undoing what the
pass's combat scripts changed in it; Steamwoven clears only the recovered bits of the current flags.

## Session (`GameSessionOwner`)

| Operation | Original | What it does |
| --- | --- | --- |
| `ScheduleAiTick(request)` | 0x4AD6E0, 0x4AD790 | One revision: the NPC's tick on the actor's map, replacing its pending one (never the PC's). `WakeUp` (a sector fold) is due 1 ms from now with the marker; `Delayed` (a placed, created or resurrected NPC) after `TickDelayMs(request.DistanceToPc)`. Refused inside an event pass and when the queue has no room. |
| `QueryAiTick(entity)` | | The NPC's pending tick: deadline and marker. |
| `ProcessEvents(world)` | (0x4AD200) | A due `ai.tick` runs `Tick` when the pass's host is an `ISessionAiWorld` and the tick is on the actor's map, then schedules the tick it names (one revision; a tick that does not fit the queue, or whose spawner delay exceeds the bound, is lost and reported as `EventBudgetExceeded`, and as `RevisionExhausted` at the revision limit); `SessionEventPassResult.AiTicks` lists each firing. Another script host consumes it without running it (`Tick` null), and nothing follows. |
| `RunAiPass(entity, world)` | (0x4A84F0) | One `Pass` of an NPC, as an attack, a follower order or the host asks for it. |
| `RunAiSubturnHook(critter, world)` | (0x4B7010) | The hook for a round participant; the subturn's end is `EndCombatTurn`, whose result (`SessionAiResult.Round`: the next subturn's start or the round's end, for the host to run their hooks) is returned and whose refusal is the `WriteFailure`. |
| `AiRolls` | | Adds the labels `ai.TickWakeUpJitter`, `ai.GateBlindRecovery` and `ai.GateCrippleRecovery`. |

The session answers death, unconsciousness and stoning from the NPC's life record over the host's loaded state
(`ISessionAiWorld.AiLifeTarget`; a gone NPC counts as dead), the round and its actor, combat mode, the danger record and its
source, the critter-flags parts it holds, and a recorded companion's waiting. Each gate write is its own revision: the danger
bits through the danger records, the critter flags (life conditions, the danger bits of the word, combat mode, concealment) in
one write; a write a budget refuses is reported as `WriteFailure` and the gate goes on (as the original's writes cannot fail), so
the actions before it stay done: a re-wield whose bit clear is refused runs again on the next pass. Concealment is held only for the hero's followers, so a
stale write that would conceal another NPC stores nothing.

**State.** `ai.tick` (game time, active map only, one per NPC; entity: the NPC; values: the game time it was scheduled at, its
delay of 1 ms up to the deferred call's bound, the wake-up marker) is saved with the queue (registry version 1 unchanged). A
map change removes every tick, the followers' too: the host's fold wakes the new map's NPCs, so the original's carry-over of a
follower's tick (P3 of the event queue study) does not happen. It also drops the danger records that hold only "check wield"
and "check weapon": the fold's wake-up sets "check wield" again and its full re-wield covers the weapon, so no NPC acts
differently, and the danger budget does not fill with the NPCs woken on earlier maps. A tick never names the PC. A restore refuses a tick off the saved actor's map or with a
deadline its delay does not give. Under the Original setting (OQ2, `GameEventQueueProfile.RebuildOnLoad`) the restore rebuilds
each saved tick as a wake-up tick 1 ms after the restored clock, as the original's load folds the sectors in; Steamwoven keeps
the saved timing. The new danger bit CHECK_WEAPON (`DangerFlags.CheckWeapon`, NPC flag 0x80) moves the snapshot to schema 42
(flag name `CheckWeapon`; schemas 1..41 are refused, not migrated).

## Settings

| ID | Original 1.0.7.4 (switch on) | Steamwoven | Switch |
| --- | --- | --- | --- |
| CL5 | The gate's blindness, crippling and stun recoveries write back the critter flags read before the pass's combat scripts, undoing a change those scripts made in the same pass (latent). | Each write clears only its own bits of the current flags. | `CombatAnomalyProfile.GateRecoveryWritesStaleFlags` |
| OQ2 | AI ticks are not saved; a load wakes every NPC again. | Saved ticks restore with their timing. | `GameEventQueueProfile.RebuildOnLoad` |

CL1 (a stunned NPC never acts, also on the pass that clears the stun) and CL2 (the full re-wield clears "check weapon" too)
follow the original in both settings; P5 (the combat scripts on every pass) is kept.

## Not ready

- **Host.** The AREA viewer's AI host ([AI viewer host](ai-host.md), ARC-WORLD-092) implements `ISessionAiWorld` and schedules the
  delayed tick of every NPC of the shown map when it is built; it schedules none on a sector fold, runs no heartbeat or combat
  script through the gate; in a turn-based round the viewer's round host runs the subturn hook with it (`AreaTurnBased.Step`,
  ARC-WORLD-095).
- **Pipeline stages.** Heal and the item search (rules of slice I8, `AiSupportRules.Heal` and `AiGroundRules.SearchItems`, [AI support](ai-support.md)), the danger update's writes (the decisions of slices I4 and I5) and
  perform are the host's stages; perform's rules are the executor's `AiExecutorRules.Perform` (ARC-WORLD-086,
  [AI executor](ai-executor.md)); the viewer's host runs the danger update and perform (ARC-WORLD-092), not heal and the item search yet.
- **Other callers of the pass.** The attack misses and the damage without death (0x4B30F5, 0x4B3948, 0x4B52FC) call it through the
  AREA host's `AreaAiHost.Struck` (ARC-DEV-292; the hero's and NPCs' hits and misses that resolve on the spot or at the strike of a shot, a thrown item's hit, spell and script damage, burns, traps, knockback
  bumps). NOT READY: the pass of an NPC attacker whose critical miss turned on itself (a damage step on the attacker, 0x4B52FC), and the miss path of a shot that
  flies and strikes nobody (whether 0x4B30F5 runs at the shot or at its end was not read); the follower orders (I9) call it through the session's order step; the animation end of a round (call at 0x423B8D) does not call the hook.
- **Hit effect (ARC-DEV-303).** The NPCs' hits (melee, shot, landed shot, thrown item) also call `AreaAiHost.HitEffect` before the victim's pass: the hit effect 0x459A30 asks an entangled victim's save again
  (`SessionSpellCaster.CritterHit`, [spell cast host](spell-cast-host.md#effect-component-and-the-entangle-re-resistance-arc-dev-303)); a miss never does.
- **Routine.** Waypoints, standpoints, day and night positions, sleeping and wandering (FR-AI-001) are the host's; the spawner
  (0x4BA790, FR-AI-004) is not implemented; the follower catch-up's move is the host's.
- **Network.** The network client tests (CL3) are not modelled: Steamwoven is single player.

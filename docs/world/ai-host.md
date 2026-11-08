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
I10's third step) adds the round's tail: the end-combat script at an NPC's subturn end, the callback of a turn set's
interrupt and the fidget after a done run (exe rows [TH7–TH10](../research/ledger/npc-ai.md#npc-ai-turn-based-host-tail-arc-world-099),
one reader, F1). ARC-WORLD-102 (the next part of I10's third step) is the casting host: an NPC that knows a spell chooses it at its
danger update and casts it through the map's cast host ([below](#casting-arc-world-102); exe rows
[AC1–AC4](../research/ledger/npc-ai.md#npc-ai-casting-host-arc-world-102), one reader, F1); it covers the NPC's casts of
[FR-AI-002](../requirements/story-social-ai.md#fr-ai-002) and [FR-MAGIC-004](../requirements/combat-magic-tech.md#fr-magic-004).
ARC-WORLD-104 (I10 part 3c) queues a cast while the NPC's casting goal plays, as the hero's ([below](#queued-cast-arc-world-104);
exe rows [AC5–AC7](../research/ledger/npc-ai.md#npc-ai-queued-cast-arc-world-104), one reader, F1). ARC-WORLD-109 (I10 part 3f)
hosts the skill goals 33, 34, 86 and 67: the turn, the skill animation and the attempt, whose heal is the session's NPC heal
([below](#skill-goals-arc-world-109); exe rows [AK1–AK3](../research/ledger/npc-ai.md#npc-ai-skill-goals-host-arc-world-109), one
reader, F1); it covers the NPC's skill use of [FR-AI-002](../requirements/story-social-ai.md#fr-ai-002) and
[FR-CHAR-019](../requirements/characters-skills.md#fr-char-019). ARC-WORLD-113 (I10 part 3g, first slice) hosts the throw goal 37 of a
"default throws" weapon: the attack starter's throw, the throw animation and the session's NPC throw, decided and landed at once
([below](#throw-arc-world-113); exe rows [TA1–TA2](../research/ledger/npc-ai.md#npc-ai-throw-host-arc-world-113), one reader, F1); it
covers the NPC's thrown weapons of [FR-AI-002](../requirements/story-social-ai.md#fr-ai-002).
ARC-WORLD-108 (I10 part 3e) gives the NPCs their items: the heal step, the item lists, perform's cast from an item and goal 39's item
use ([below](#item-use-arc-world-108); exe rows [NU1–NU4](../research/ledger/npc-ai.md#npc-ai-item-use-arc-world-108), one reader,
F1).

## Session: the NPC hit step (`GameSessionOwner.AttackByNpc`)

| Operation | Original | What it does |
| --- | --- | --- |
| `SessionCritterCombat.AttackValues(status, state, profile, wornNow)` | 0x465F70 (AH1) | What an NPC strikes with: its wielded weapon (since ARC-WORLD-106 the items the session's NPC wears, `AreaCombatHost.Worn`: a regeneration's best-items record, items given to or made for it, never a taken, dropped, destroyed or broken one), else a monstrous critter's (0x45F730) own damage table (NPC field 0x12F, minimum at 2 × type, maximum at 2 × type + 1), else bare hands with the gauntlets' unarmed bonus (field 0x9E of the item at location 1007, read for melee only); the weapon's skill with the stored basic skill's level and training (melee 20 and the tiers for a monstrous critter; a levelled follower's sheet; since ARC-WORLD-106 firearms from the stored tech skill, ledger NF1); the weapon speed, attack cost, Speed and reach. Supported: melee. |
| `AttackByNpc(expected, SessionNpcAttack)` | hit step 0x42A630 → 0x4B3BB0 → resolution 0x4B3170 | The NPC (attacker) on the hero (`Target` null) or another NPC: the resolver under the active rules and difficulty (`combat.*` rolls); the hero's armed "critical miss" fate turns it into a critical miss and is used up in a revision of its own (FT6); the damage to the target, or to the attacker on a critical self-hit; on an NPC target's hit-point damage the pool share, which the pool loses although only a PC attacker gets it (XP10), and on its death the kill award to the attacker's PC leader (a follower or a waiting follower of the hero), else nothing and the worth stays (XP9); then the critical and wear steps with the NPC as attacker (drops at the NPCs' cells, the hero's among its placed items). (ARC-DEV-273: also a bow or firearms attack, reading `SessionNpcAttack.LineOfFire`.) Refused before any roll: no hero, values that do not hold, an attack on itself, wear items that do not hold, a distance beyond the reach, a drop cell outside the packing, an attack other than melee (`AttackUnsupported`), the version, the revision. A resolver refusal is NoChange. |

No new state: the snapshot schema stays as it is.

## Viewer host (`Arcanum.World.AreaAiHost`)

One host per shown map, built when the session, the map or the party changes (a load or a map change builds it again; since ARC-DEV-119 a window move of the main map does not: the host is rebound to the new window's scene, `AreaAiHost.Rebind`, and keeps its runs, movers and ticks, and the NPCs of the windows it has seen; a whole map's area still builds it again). Goal
runs are not saved, but where an NPC stands is, ARC-DEV-094, so the host restores each NPC on the place the session recorded and an NPC with none stands on its map data's cell. It answers in world cells; the planners
take chart cells.

| Part | What it does |
| --- | --- |
| Ticks | Every NPC of the map that is not a companion (not in the companion group, not led by the hero) and not dead gets a delayed AI tick (0x4AD6E0, the loaded NPC's start) unless one is pending; since ARC-WORLD-115 so does every companion the hero leads (following or waiting, see [followers in combat](#followers-in-combat-arc-world-115)). The viewer's event world (`AreaEventWorld`) forwards the tick's host questions to the host of the current session, also through the cast host's pass world (`SessionSpellCaster`); without one an NPC counts as gone and its sector as not loaded, so the tick schedules no next one. The host is built again when a follower joins or leaves; an NPC that joined since counts as gone. |
| Scheduler host (`IAiHost`) | Distance to the hero; no SCR at the AI points (every point continues), no spawner or encounter creatures; the waypoint and standpoint steps of a fold-marked tick are `NpcRouteRules` run through `AreaAiHost.RunWaypoints`/`RunStandpoints` (ARC-DEV-117, [ledger WS1–WS12](../research/ledger/npc-ai.md#npc-waypoints-and-standpoints-arc-dev-117)); generator, sleeping and hidden from the NPC's flags; the animation priority of the goal its run is at (the goal table's header word 1, AH3), 0 without a run; an animation runs while it has a run. Heal and item search choose nothing yet. |
| Danger stage | `HostilityRules.DangerUpdate` and its plan through the session: the flee path's flee-spell step (no flee action is chosen), the unlock, and the setter (`SetDangerState`) with the NPC's setter facts (invulnerable, NO_FLEE: the stored flag ORed with the session bit, the leader's extraordinary charisma) and, for a fight on the hero, its reaction facts and AI parameter 10. No spell or item choice yet. |
| Perform stage | `AiExecutorRules.Perform` with the danger source as the pass's target and no choice; a started run replaces the NPC's run (the old one's end nodes run when its goal differs). |
| Runs | One `AiGoalRun` per NPC, stepped by `AiExecutorRules.Step` when due, then after its delay (0: the next frame); a killed run, the node limit and an overflow run the end nodes through `EndRun`, as a replaced run does. The runs go by the frame step's world milliseconds (ARC-DEV-124, MIG12 S5: `FrameClocks.WorldMs`, admitted animation time ÷ 8, so it equals real time unpaused, stops with the pause and is not moved by a jump; the hero's walk and attack still play on the viewer's Godot time until S6): the milliseconds the host answers (`BeginStep`, `BeginAttack`) and the executor's delays are that clock's, which the executor's contract calls game milliseconds; the ticks go by the session's game clock, which runs 8 times faster. The host is a participant of the frame step (`AreaAiFrameParticipant`, registered with each ambient driver): once per frame after the event passes it touches the view's sectors and pumps the runs; a paused frame (the Esc menu, and until S7 any window) pumps nothing, a frozen frame calls nothing (`FrameStep.Run` hands the participant the host's `frozen`), no pump runs while a map transfer is in flight, a round pumps (TL38); the viewer draws the movers and attack plays at `AreaAiHost.NowMs`, the clock of the last pump. |
| Walker | The attack's, the follower's and the tile planners are the hero's route planner (`RoutePlanner.PlanApproach`, `Plan`) over the map's step evidence with a live overlay (the hero, the companions, the other walked NPCs; the target's own cell does not block); a flight's escape line is the straight line. A step is blocked by a living critter on the next cell (wait) or a refused step (cannot start). `BeginStep` puts the NPC on its next cell at once and slides its T0 body there over the step's time (the gait timing of the hero's grid). |
| Reach and lines | The line of fire of the shown map with the walked NPCs and the companions on their cells; reach is the weapon's (1 bare-handed) with a clear shot. |
| Attack | The attempt-attack goal's node 5 (0x42B9C0, AH2) puts the attacker into combat mode, and a PC target, or an NPC target's living PC leader, too; the animation's timing is the NPC's attack ART played at its Speed and weapon speed (`RealTimeCombatRules`). Since ARC-RENDER-038 (ledger WA1, WA4, WA5) the host asks the viewer (`AreaAiScene.AttackArt`) for the ART of `AreaAiAttackArtRules.Request`: the animation of `WeaponArtRules.Animation` (the throw 14 for a returning weapon, the low attack 21 against a shrunk target (its stored object flags), a lying one (unconscious or dead, else its stored art's animation 7..11) or a short one (its art's height), else the attack 20) and, in combat mode, the weapon part and shield bit of `WeaponArtRules.WeaponPart`/`ShieldPart` from the items it wears (`AreaCombatHost.Worn`; bare hands 1 without a weapon). The ART read gives frames, action frame and own rate; the rate is its weapon slot's for 20 and 21 (by the wanted animation: a low attack drawn with the attack's file keeps the low-attack rates, as the art id keeps 21 and only the name resolver repairs, WA9) and its own for the throw (the rate's default, WA9). An ART that is not read plays with the fixed human attack ART's timing (9 frames, action frame 6, 15 fps) and is not drawn. The play (`AreaAiAttackPlay`: start, request, ART, `RealTimeCombatRules.Schedule`) is kept for the drawing. At the action frame the hit step is `AttackByNpc`, then the attack event. |
| Attack event | `HostilityRules.AttackEvent` for the hero's resolved attacks on an NPC (the viewer's attack hook) and the NPCs' hits: a PC victim ends its dialog (the callback 0x5F848C, ARC-DEV-271, GT140) and enters combat mode (0x4A9AD0, AH4); the noise's witnesses (`NoiseWitnesses`, `Witness`) react; the session's who-hit-me-last and team writes (`ApplyAttackEvent`); the victim turns; a deliberate hit lowers its reaction toward the hero (`ReactToPcHit`); its reaction (`MoraleRules.ReactToAttack`) asks the setter. |
| Hostility and morale world | `Find` from the NPC's fields, life, danger record and combat mode; parameters from the AI table row (out of the table: 0); sight and hearing (`AwarenessRules`) over the line of fire, no tile soundproof; faction-same as far as this host knows (the hero and its followers, one leader, one non-zero faction; the PC's reputations granting a faction are not read); kill-on-sight scripts allow. |
| Viewer | Per frame after the hero's attack: the runs step, the hits reach the status line (" | AI: …" with each fighting NPC's state, target, goal and last reason), a stun's end, a killed NPC's fall, and a hero knocked down or out stops its walk and attack; the walked NPCs are drawn where they stand (a fallen body too), block the hero's walk, stand in the line of fire, the drop map and the round's scan, and the hero's attack, loot and criticals find them on their cells. Automated runs keep the AI off unless their stage turns it on (`ARC_AREA_MAP_SMOKE_ONLY=ai`). |

## Followers in combat (ARC-WORLD-115)

I9's host part: the hero's companions on the shown map (the group's companions the session says the hero leads, following or
waiting; `AreaAiScene.Followers`, `IAreaAiFollowers`) are run by the host like its NPCs, over the follower rules of
[AI followers](ai-followers.md) (FO1–FO20). Since ARC-WORLD-119 the free ones (released or cursed) are run too, as any leaderless
NPC: the removal leaves no mark but the null leader, which the AI's follower branches read (FO20). They are the group's companions standing here,
whatever their standing, so a group companion's join or leave no longer rebuilds the host (other NPCs' runs go on); a leave ends
the leaver's run (`ObserveLeaves`: the removal's interrupt at priority 5 ends every run of this host, whose goals are of priority 4
or less; seen by the pump, by perform, and right after an order), a join keeps it (C5). An order's attack event (the curse's,
FO2) runs through `OrderAttack` after the order's leave and before the former follower's pass.

Since ARC-DEV-075 (MIG2, TM3) a companion carried here from another home map is run like any NPC of the shown map: the host asks its
fields, combat values, worn and held items, AI parameter row and sheet **by identity** (`AreaCombatHost.Fields(entity)`, `Build(session,
entity)`, `Worn`, `WornItems`, `NpcItems`, `HeldItemFields`, `NpcHolder`, `NpcItemFields`), which the record resolver of MIG1 answers from
the companion's home map (held, or read on demand), and the scene's `Companions` give each companion's entity beside its T0 key, so a
visitor's row (a row of its home map) is never looked up by the shown map's key. Virgil carried from the main map into the Crash Site cave
is therefore told of attack events, stands in the scan's and noise's squares (`Square`), counts in the hero's party a flee test counts
(`Followers`, which still leaves out a follower on another map), strikes and is struck. Since ARC-DEV-065 an object the rules cannot
describe must never reach them (the rules refuse an object `Find` does not know, and the refusal once stopped the whole game with failure
stage `ActorPlan`): the one case left is a companion with no record at all (its home is a main-map window and the session recorded no stored cell
for it, `npc.home`: a companion that left before ARC-DEV-079; since that slice the window is read on demand around the recorded cell, so Virgil after
a quick load or a fresh run on a foreign map resolves, and a second window of one map is not resolved: MIG9), which `Unresolved` keeps out
of the rules as before; its walk still follows the hero through the group.

| Part | Original | What it does |
| --- | --- | --- |
| Tell followers | 0x4A9B80, 0x4A9C00 (FO1–FO19) | Every attack event of the host (the hero's hits, the NPCs' hits and throws) tells the attacker's direct followers right after its early exits, then, after a PC victim's combat-mode entry, the victim's, before the noise. Only the hero leads here: its followers in the party's array order that follow it (not the waiting ones) and stand on this map. The follower rule's case 5 is the follower's reaction to an attack by the other side (`MoraleRules.ReactToAttack` through the setter, as a victim's: fight or flee); a lock, a focus on an object and a complaint (−5 toward the hero) go through `ApplyFollowerRule`. The host's attacks are deliberate (never accidental) and never ordered. The upset line is not floated. |
| Ticks and passes | 0x4AD6E0 | The companions get their AI ticks and passes (gate, heal step, danger update, perform) as the NPCs do; their spells, items and attacks go the same ways. In a turn-based round their subturns run through the host too, and only a held companion's walk is driven (the steps the host gives it on its subturn; the others stand, as before); no test or smoke covers a companion in a round yet. |
| Follow | 0x4346A0, A6 | A follower follows only in state NONE (the non-combat perform): the follow goal is the companion's own walk's ([companion follow](companion-follow-runtime.md)), so a follow that perform starts for a companion is not run by the host (trace "Follow by its own walk") and a run left from the fight (a flight, a back-off) ends as a replaced one. |
| Hold | — | `AreaAiHost.Holds`: while a companion has a goal run of the host or its danger state is not NONE, its walk makes no follow decision (`AreaCompanionGroup.HeldByAi`) and drives only the host's steps. |
| Steps and turns | — | The host's step of a companion (`BeginStep`) orders its own walk one step onto the next cell (`AreaCompanionGroup.StepTo`, the walk-here order's ordered goal `CompanionFollower.WalkTo`; a refused one is traced); its cell is that walk's, so the host reads it from the scene's companions. A turn stands its settled grid again with the new facing (`FaceTo`). Its body is the companion group's (the T0 frame with the walk or run cycle). The companions' walks see the AI's moved NPCs (`AreaCompanionGroup.Extra`: their T0 records away, their live cells held), as the hero's does. |
| First-step test | 0x425130 (AP1) | The arrival test's step toward the target reads the walker's overlay toward it (`Occupancy`): the target is made no-block (its T0 record and every moved record away), the other critters' live cells block. Before, it read the bare map data, so an attacker next to an NPC standing on its own record's cell met that record as an object in its way and found no first step. Next to the target past a corner object there is no first step and, as in the original, no route (AP2), so the attack ends (AP4). |
| Orders | 0x4C31A0, 0x43C570, call 0x4C389D (FO1, FO3, FO5) | Since ARC-WORLD-117 the viewer's follower orders go through the host (`AreaCompanionOrders.Send`): each follower's combat facts are the host's (`OrderFacts`: the setter's facts, the reaction facts toward the hero, the combat-mode facts), the host is the ordered attack event's world and each reached follower's AI pass runs through it right after its order. The attack order's target is the highlighted object as its session entity (`AreaCompanionOrders.Target`): for F2 the object under the pointer at the key's release, for the portrait menu's attack the object under the next left press (its target cursor; not while the hero is dead or unconscious, OL1); none, or a press on no object, is no target (the order does nothing but the pass). A back off's stop interrupts the follower's run at priority 3 (`ApplyStop`, through the order's `Ordered` hook) right after its order and before its pass, as the original's stop attacking interrupts inside the order, so the pass finds it idle. This host's hostility world knows critters only, so an attack order on a container or a ground item stops at its attack event (Missing) and the follower ignores it; a door or scenery has no session entity, so no target. Without the host the attack order goes without a target. Only the attack and back-off orders carry the combat facts. The walk-here goal and the spread step start through the same hook, before the pass, as the original sets them inside 0x4C31A0 before 0x4C389D. |
| Step test | — | A step's terrain test reads the map with the T0 records the planner moves too (the walked NPCs', the companions', the removed ones) and no live cells (the destination's critter is the living-critter test's): a route through a companion's home cell or a walked NPC's origin is no longer refused step by step while the planner keeps planning it. |

## Turn-based round (ARC-WORLD-095)

The viewer's round host (`AreaTurnBased.Step` with the map's AI host, [turn-based round](turn-based-round-runtime.md#viewer-host-arc-combat-020))
runs an NPC's subturn through its AI; without a host (automated runs keep the AI off unless their stage turns it on) an NPC's
subturn still ends at once.

| Part | Original | What it does |
| --- | --- | --- |
| Subturn start | turn set 0x4B6E70, hook 0x4B7010 (TH1, TH3) | The session grants the action points (`max(5, Speed)`); the round host then runs the session's subturn hook (`RunAiSubturnHook`): the heartbeat (no SCR yet: it continues) and one AI pass (gate, danger update, perform). A pass that starts no run ends the subturn (`EndCombatTurn`); a started run holds it. |
| The run | stepper 0x423560 | The run steps on the world clock as in real time; each step and the attack check and pay the round's action points through the session (`ConsumeActionPoints`): a walk 2, a run 1 with fatigue, the attack its weapon's cost (EX6, EX21). A step it cannot pay kills the run. |
| The run's end | stepper's end 0x423B8D, completion check 0x4B7080 (TH4, TH5) | The NPC's next frame without a run runs the hook again (its next action). The run's free called the round's callback when no significant goal was left on the map (ARC-DEV-136: `GameSessionOwner.AnimationsDone`, which feeds the stall detector and asks for the 2 ms turn event); the event's pass (`AreaTurnBased.Step`, the handler `CompleteCombatActions`) passes the turn at 0 action points and spends the rest of an actor the callbacks saw twice with unchanged action points, once the hero is idle. So an NPC whose start gate lets it begin an attack its points do not cover (fatigue above 35 %) passes on after at most two tries. |
| Interrupts | round start 0x4B71E0, turn set 0x4B6E70 (TH1, TH6, TH10) | `ObserveRound` (before each pump step, after the last, and on each subturn of a round frame) compares the session's subturn starts (`QueryCombatSubturnStarts`) with the last seen: a round that began interrupts every run, a new subturn the runs of the previous turn set's critter when it is an NPC, at priority 3 (walks, attacks and flights end with their end nodes; an attempted cast goes on). An attack that puts the hero into combat mode with the turn-based setting on starts a round and so ends its own swing. ARC-WORLD-099 and ARC-DEV-136: a turn set's interrupt that frees the last significant goal calls the round's callback (the table does it, `GameSessionOwner.AnimationsDone`, counted in `AnimationCallbacks`) for the new participant before its hook; a round's start calls none (the host holds the callback with `SuppressRoundCallback` while its interrupts run, TH10), while a next round's first subturn is a turn set like any other. A pass that passes the turn on is observed as the next turn set before the round host hooks anyone. |
| Subturn end | subturn end 0x4B7630 (TH7) | ARC-WORLD-099: every NPC `SubturnEnded` among the events of a round call (the executor's consumes and subturn ends, the host's check, the round host's hook, check and end calls) asks the NPC's end-combat script (point 16, the NPC as attachee and triggerer: `SubturnsEnded`, counted in the trace's `EndCombatScripts`); this host runs no SCR at the AI points, so the point continues. The PC's subturn end is the interface's. |
| Fidget | stepper end 0x423B95–0x423BA0, 0x4B4320, 0x435BD0 (TH8, TH9) | ARC-WORLD-099: after a done run (finished, end step, killed in its step; not the node limit or an overflow), after the owner's subturn hook (the round actor's: the round host's `FidgetAfterHook` right after the hook it runs next; anyone else's at once), `AiExecutorRules.FidgetAfterRun` decides the fidget: an NPC or the hero (ARC-DEV-272, TH9: the hero's done runs and an NPC's finished knockback push call `AreaAiHost.IdleRestart`; the hero's critter flags give no auto-animate) in combat mode outside a round, or an NPC with critter flags 2 bit 0x2 (auto-animates) (auto-animates), passing the start gate with cost 0, without a run; in a round only while the current participant has action points (in practice never: see TH9). The host reports it (`AreaAiNoticeKind.Fidget`, the viewer's log line `ARC-WORLD-099 ai-fidget`); its art test allows it and its goal-2 run (priority 1) is not held, since NPC ART is not read and nothing the host runs reads a priority-1 run. |
| Pending cost | 0x4B75D0, check 0x4B7790 (TH2) | The host's `PendingCost` reads 0 once a subturn started after it was written, whichever call started it (the subturn start's check of cost 0). EX6's end-step charge reads it. |
| Ticks | 0x4AD420 | In a round the AI ticks only reschedule (CL7: the near test needs no round), so NPCs think only in their own subturns; an attack still makes its victim react (the setter), not act. |
| Viewer | | The round log and the status line take the charges and subturn ends of the runs (`DrainRoundEvents`). Smoke stage `ai-turn-based` (only when named, map mode with `--hero`, `crash site cave`): the AI and the setting on, the NPC told of the hero's attack four cells away, R begins round 1; in its own subturns its run walks it up paying action points and bites, it never moves on the hero's subturn, which E ends; marker `ARC-WORLD-095 native-smoke ai-turn-based PASS`. |

A host built during a round (a load, a map change, a follower joining) asks the NPC whose subturn it is again (its run was not
kept). A9's repeated first subturn (Original setting) runs the hook once: the second start would interrupt the first start's run and
ask again, so the NPC acts once either way.

## Casting (ARC-WORLD-102)

The support rules' spell half ([support and magic](ai-support.md), slice I8) and the casting goals ([action goals](ai-action-goals.md),
slice I8b) get their host: the AI host answers `IAiSupportWorld`, its spell questions through the shown map's cast host
(`SessionSpellCaster`, the hero's casts' host too, the scene's `Spells`), and the casting goals' calls go to the cast host's goal.

| Part | Original | What it does |
| --- | --- | --- |
| Spell choice | danger update 0x4A92D0 → 0x4ABC20 (HS1, SP4, SP16, FO8) | For a fight request the danger stage runs `AiSupportRules.ChooseSpell` for the found target before the setter; the setter's target is `HostilityRules.FocusTarget` of the pass record's (the choice's target, else the found one, SP16); the choice stays in the pass record for this pass's perform unless the setter's NO_ATTACK or "NPC fighting" rule refused the fight in the Steamwoven setting (`KeepsChosenAction`, FO8). Without a fight, or with nothing chosen, perform acts by the danger state as before. |
| The spell half of the support world | 0x4B1950, 0x4B1AB0, 0x4B1660, 0x450B40, 0x459380, 0x453B20, 0x4AB430, 0x4ADE00 | `Support`: MECHANICAL (critter flag 0x20000000), CAST_HIGHEST (NPC flag 0x40000, AC2), the fatigue percent (AC3: 0 for a maximum of 0), current fatigue, poison, the effective willpower and the magick points (an NPC's session record, else its stat). The ten global lists from the records' AI values; the critter's list through the host's list cache (SP11, not saved). The cast host answers the book (`Book`: the hero's, an NPC's session record, else its fields' `SpellBookRules.FromFields`), knows and the college rank, the record facts (`AiSpell`: maintained, the minimum intelligence 5 and willpower, item triggers), the cast cost with the caster's mastered college (an NPC's fields' mastery now counts in every cast cost), the free maintenance count, under the influence (`SpellRunRules.UnderInfluence`, AC1) and the fail-chance predictor with its own d20 (the fate-maximised flag of the last processed action is not kept: it reads unset). The line of fire is the attack's, with the blocking row's entity. Parameters 13, 14 and 15 come from the AI table row. |
| Perform's cast | 0x4AC250 (EX2, AG14, IV12) | `IAiPerformWorld.Cast` runs `SessionSpellCaster.CastByAi`: the invocation with the NPC as source and parent and the choice's target object (in a round with the round's actor), its check (a refusal starts nothing), then the run, whose activation checks it again. The started run's casting goal is 82 when the record's casting animation is not −1, else 26, and replaces the NPC's run. |
| Goal 26/82 | 0x429B50, 0x429BC0, 0x42C850 (AG3, SM7) | `StartCast` is the cast host's goal start (`StartGoal`: in a round the 4 action points, whose events are the round's; the pre-animation check; a target destroyed or off); `FaceCast` applies the facing turn it computed (a stoned, stunned or paralysed caster cannot turn: the goal ends, freeing the run per MG39). |
| Goal 27/83 | 0x429F00, 0x42CAA0, 0x429C40, 0x429C80, 0x429CD0 (AG4) | `BeginCast`: the target re-check, then the cast eye candy (6 · spell) on the NPC (the viewer's `CastEyeCandy`, timing only): its release frame is the action frame and its end the rest (`CastEyeCandyAnimation` played through); a failed re-check, goal 83's casting animation (NPC ART is not read) or no eye candy releases at once. `CastActionFrame` releases (a play that never reaches its action frame releases nothing, EC7); `ReleaseCast` is the cast host's release (the re-check and BEGIN; MG41 in both settings as the cast host has it); `EndCast` its goal end (before the release the run is freed charged, MG39). |
| Dropped casting goal | 26/82's end node 0x42B090 (AC4) | A run the host drops (an interrupt, a replacement, a kill) whose casting goal 26 or 82 had not reached its attempt frees the NPC's magic run that still waits for its BEGIN, unpaid, in both settings (the original keeps the slot, as MG43's queued cast). |
| Viewer | | The cast map sees the NPCs the AI walked on their cells (range, line of fire, a target's tile; the hero's casts too; on a tile they are listed after its own rows, as a companion is, which the cursor's pick order follows). A host dropped or replaced for the same session (a follower joining or leaving, a map change, the AI turned off) first ends its runs (`Retire`), so a casting goal's run is freed as an interrupted one's is; a load drops the replaced session's runs with it. A play logs `ARC-WORLD-102 ai-cast`, a release `ai-cast-release` with the cast host's log (the status line, the message window, the eye-candy components on their targets), the bodies and the inventory; the goal's end `ai-cast-end`. Smoke stage `ai-cast` (only when named, map mode with `--hero`, `Arcanum1-024-fixed`'s Crash Site window): the highest-level NPC whose fields store a college level (27359, college 10 at rank 2) is told of the hero's attack 3..5 cells away and casts at the hero; marker `ARC-WORLD-102 native-smoke ai-cast PASS`. |

No new state: the pass record's choice, the list cache and the casting goals' play are the host's, not saved (a load builds the
host again); a magic run itself is the session's, as the hero's.

## Queued cast (ARC-WORLD-104)

Perform's cast reaches the hero's run start (0x4AC250 → 0x455AC0 → 0x455C30, AC5), so a cast while the NPC's casting goal plays
is queued as the hero's is ([queued cast](spell-cast-host.md), ARC-MAGIC-019, QC1–QC6). The queue lives in the NPC's goal run.

| Part | Original | What it does |
| --- | --- | --- |
| Queue | 0x44E830, 0x4373A0, 0x4B6D70, 0x44DBE0 (AC5, QC1, QC2) | `Cast` runs `CastByAi` first (the run exists from the cast); when the NPC's run has goal 26 or 27 at its bottom (82 and 83 are not found) `SessionSpellCaster.Queue` takes it with the run's current index as the depth: the new goal (26, or 82 for a record with a casting animation) is inserted at the bottom of the run, so the playing goal stays current and the queued goals play in the order they were cast; a depth of 4 or more or a turn-based round refuses and frees the run, silently. Either way no run starts: perform keeps the NPC's run (`AiCastStart.Queued`). Without a casting run the cast starts its own run, replacing the NPC's, as before; with goal 82 or 83 at the bottom (not found) the original starts a second run beside it, which this host, holding one run per NPC, does not: nothing starts (not ready; no college spell has a casting animation). |
| Play | goal 27's pop (QC3), the stepper | The attempt goal's pop restarts the goal beneath at its first node in the same step: the queued goal's start, turn, play and release are the NPC's casting goal's (above). A queued goal whose run is no longer the NPC's (cancelled before it, QC6, its slot perhaps another caster's) starts without one and ends (QC4). An older queued goal whose freed slot the new cast reuses is dropped when the new one is inserted (the original's stale goal would start the new run early or, without it, take the end step and drop the casts beneath), so the new cast plays in its turn. |
| Dropped queue | end step, kill (QC5, AC7; MG43) | A run that ends through the end step (a queued goal refused at its start, a play with no eye candy) or is dropped (an interrupt, a replacement, a kill, `Retire`) leaves the casts queued beneath unplayed: the host frees their runs, still waiting for their BEGIN, unpaid, in both settings (the owner's MG43 decision). |
| Reach | gate 0x4A8570, 0x4233D0 (AC6) | Perform is called only from the pass after the gate, whose busy test reads the bottom goal of the NPC's run: a casting run's (26, 27, 82, 83: priority 3 or 4) holds the NPC busy, so its own AI pass never meets a queue; the branch takes an NPC's cast from another starter of a spell run (the script commands', not run by this host). Since ARC-WORLD-105 the host's busy test reads the bottom goal too (`AnimationPriority`; it read the top goal, which let an attack run's pass go on mid-approach: fixed in both settings as a technical slip). |

No new state: the queue is the NPC's goal run, the host's and not saved; the queued runs are the session's spell runs.

## Spell missile (ARC-WORLD-105)

An NPC's casting goal launches its spell's missile as the hero's does ([spell missile](spell-cast-host.md), ARC-MAGIC-018, SM1–SM9):
the launch node has no player test (AC8). The flight is the viewer's, shared with the hero's missiles.

| Part | Original | What it does |
| --- | --- | --- |
| Launch | 0x429C40, 0x42CAA0, 0x4246E0 (AC8, SM1) | At the play's action frame a spell whose projectile eye candy has art (the scene's `MissileArt`) launches instead of releasing: `CastActionFrame` runs `SessionSpellCaster.LaunchMissile` (the release gate marks the frame handled, no re-check or BEGIN now) and posts a `Missile` notice with the spell, the run, its target object and the cast host. The goal restarts one pace after the launch (`PaceAfterRelease` in `BeginCast`'s timing), so the rest is one pace longer than a release's. A run released or begun before launches nothing and the goal ends. |
| Goal's end | 0x429CD0 | The attempt goal's end after the launch frees nothing (`EndGoal` sees the handled frame): the run waits for its missile, whatever becomes of the NPC's goal run afterwards (an end, an interrupt, a replacement, `Retire`). The host keeps the cast host that launched the run until the goal's end and ends the goal there, so a cast host rebuilt meanwhile (a map change) does not free the run charged. |
| Flight | goals 28 and 78, 0x4248A0, 0x424D90, 0x429C80, 0x429E70 (SM2–SM9, MG44) | The viewer flies the missile from the NPC's live tile to its target's (a tile for a tile target): the secondary cast eye candy plays on the NPC, the missile is drawn and steps, follows a moved target on a new leg, and arrives (`ArriveMissile`: the re-check and BEGIN, whose damage reaches the bodies, the inventory, the status line and a downed hero's walk and attack as a release's does) or is lost (`LoseMissile`: the run freed unpaid, MG44 in both settings). A missile that cannot fly from the NPC (no cell, no art, a run gone, another session) is lost at once. Logs: `ARC-MAGIC-018 missile-launch` with `caster=`, `missile-end`. |

No new state: the missile is the viewer's, not saved (a quick save is refused while any missile flies, as for the hero's); its run
is the session's.

## Skill goals (ARC-WORLD-109)

The host answers the skill goals' world calls ([action goals](ai-action-goals.md), AG6–AG9, AG12), so a skill run that perform's
skill starter (0x4350F0, 0x436220) begins plays out: goal 33 walks within range 1, then the attempt goal 34 (any skill but two), 86
(pickpocket) or 67 (pick locks) replaces it.

| Part | Original | What it does |
| --- | --- | --- |
| Priority | goal table (AK1) | Goals 33, 34, 67 and 86 have priority 3: a skill run holds its NPC busy at the gate, as an attack run does. |
| Turn | 0x42C0F0, 0x42C390 | `FaceForAction` turns a walked NPC to the target's cell or to the tile; refused for an object the host does not walk, a target without a cell, object flag 0x10000 or a stunned or paralysed critter (critter flags 0x60, or the session's stun). |
| Animation | 0x42BD40, 0x42CB10, 0x42D300, 0x436220 | `BeginAction` plays the skill animation (12) to its action frame, then its rest; goal 67's pick-locks loops run before its attempt, as many as the starter counts from the art's frames (frames ÷ max(frames − 2, 1) + 1, halved for an apprentice or better in pick locks). NPC ART is not read: the fixed human attack ART's frames (9, action frame 6, 15 per second) stand in, played at their raw rate, so the attempt comes 400 ms after the start and the goal pops 200 ms later (two loops, 1200 ms, for pick locks); `HasSkillArt` answers yes for every walked NPC. An object flag 0x10000 or a stunned or paralysed NPC cannot start; as the turn before it refuses the same NPC, such a run ends at the turn (the end step, no charge). |
| Attempt | 0x42A2A0 → 0x4C7090, 0x4C7160 (AK2, AK3) | After the attempt's 4 action points (the rules' consume), `AttemptSkill` runs the invocation. The heal skill (10) is the session's `HealByNpc`: the NPC's tool item must be a healing item it holds now (`AreaCombatHost.HeldItemFields`, the session's view of its list), its skills come from its map data's sheet and effects (the scene's `NpcSheet`, the session's saved sheet for a levelled follower); without such an item the heal is impossible and nothing changes. Its answer, the run's flag 0x40000, is the check's success (AK2). A `Skill` notice tells the viewer, which refreshes the bodies, the inventory and the status line after a heal and logs `ARC-WORLD-109 ai-skill`. |
| Session heal | heal case of 0x4C7160 (AK3, HL1–HL5) | `GameSessionOwner.HealByNpc(SessionNpcHeal)`: the hero's heal check, outcome and item uses with the healer's own heal level and training, extraordinary intelligence and blindness and crippled arms, no local-PC difficulty term, no fate and no action points (the goal paid them). Refused before any roll: a hero, a dead or invalid healer, an item that is no healing item, has a use or destroy script, was destroyed or is the hero's, an invalid target, the version. |

What chooses a skill: only the heal ladder's heal-skill rung (`AiSupportRules.Heal`, out of danger, below 90 % HP, with a healing
item), which ARC-WORLD-116 wires with the NPC's items (below); `AreaAiHost.PerformSkill` (a test and smoke seam) starts a skill run as
that choice would. No new state, no new switch.

## Heal skill rung (ARC-WORLD-116)

The heal ladder's heal-skill rung gets the NPC's healing item, so an NPC heals itself, and a companion its PC leader (the hero) or a
fellow follower, with a healing item through the skill goals above (ledger HI1, HI2).

| Part | Original | What it does |
| --- | --- | --- |
| Tool | 0x4C91F0 → 0x462760 (HI1) | `HealingItem` is the first item of the NPC's list as the session shows it (`AreaCombatHost.NpcItems`, worn items included, in the list's order) that is a generic item (type 14) with the healing flag (generic flags 0x8); none for the hero. |
| Check | 0x4AE570 (SP3, HI2) | `HealSkillAccepts` takes the item's fields (an item an NPC of the map holds), the target the hero or a critter of the map, and a healer that is neither undead (critter flag 0x4) nor mechanical (0x20000000), from its stored critter flags as the host reads them elsewhere. The target's flags are not read (the heal helper's mechanical test, P4, comes first; a dead target takes the resurrect rung instead). |
| Choice | 0x4A8AA0 (SP2) | Out of danger with the target below 90 % HP the rung writes the pass action 3 with skill 10, the tool and the target; perform starts goal 33, the attempt goal 34 heals through `HealByNpc` (AK3) and spends a use of the item. A companion's skill run holds its own walk (`Holds`) until the goal pops. |

No new state, no new switch, no schema change: the item's uses and its deletion are the session's durability records, which the
snapshot already carries.

## Throw (ARC-WORLD-113)

The attack starter of an NPC that wields a "default throws" weapon (TW1, the throwing dagger 6072 in the installed game) starts the
throw goal 37 at its target's tile instead of an attack ([action goals](ai-action-goals.md), AG10, AG12, AG13), and the host plays it.

| Part | Original | What it does |
| --- | --- | --- |
| Throwable weapon | 0x434B00 (TA2, TW1) | `ThrowableWeapon` answers the item the NPC wears in its weapon slot as the session shows it (`AreaCombatHost.Worn`) when it is a weapon with weapon flag 0x200 and no no-drop flag (the remove check 0x466DA0); not ready: one with scripts or one the session added to the NPC is not answered, as the session's throw would refuse it. |
| Unwield | 0x466E00 (TA1) | The original takes the weapon out of the NPC's inventory here, before the goal runs, and loses it when the goal never reaches its throw (a refused start, an interrupt, a replacement, the NPC's death). A lost object (owner rule on technical bugs): in both rule modes `Unwield` changes nothing and the weapon stays wielded until the throw removes it. |
| Animation | 0x42BD40, 0x42C390, 0x42CB10 (AG10) | `FaceForAction` turns the NPC to the tile; `BeginAction` plays the throw animation (14) with the skill animation's stand-in (the fixed human attack ART's 9 frames, action frame 6, 15 per second: the throw 400 ms after the start, the end 200 ms later; NPC ART is not read). Goal 37 has priority 3. |
| Throw | 0x42AB90 → 0x4B3C00, 0x4B3170 (TW2–TW6) | At the action frame `ThrowItem` needs an item the NPC still holds (the session's view; else the throw fails, AG10), weighs it as the thrower holds it (0x461370 with the thrower: its stored weight and its magic adjustment under the thrower's aptitude, TW5) and names the struck critter (TW4): the first object the line of fire to the tile stops at, when it is a living walked NPC or companion, else the living critter on the tile (the hero, a walked NPC, a companion). Then the session's `ThrowByNpc` (below) and, for a resolved attack, the attack event on the struck critter (the projectile's end alerts it, PF8). A `Throw` notice tells the viewer, which logs `ARC-WORLD-113 ai-throw` and treats a struck critter's attack as an NPC hit (status line, floaters, stun, fall, the bodies and the inventory). Goal 37 then charges 2 and takes the end step on every path (AG10, EX6). |
| Session throw | 0x4B3C00 (TW3, TW4, RH2, RH4, PS3, TW6) | `GameSessionOwner.ThrowByNpc(SessionNpcThrow)`: with a struck critter, `AttackByNpc`'s attack with the thrower's throwing level and training (skill 3; `SessionCritterCombat.SkillValues`), the item's thrown range (`ThrowRules.Range`: the check's range, impossible beyond it) and the line's cover, without the reach test; a hit lands the item on the struck critter's cell, a miss scatters it from the tile (`(20 − throwing level) / 5 + 1` tiles at most the distance, a rotation drawn after the check). The item leaves the NPC (worn no more) and lies on the landing cell as its seed item's ground map change, in a revision of its own before the damage, moved there without the drop's placement: no junk pile, no decay (TW6, ID9). The damage, experience, critical and wear steps follow with the thrower's hand empty. Without a struck critter nothing is rolled and the item lands on the tile. Refused before any roll: no hero, cells outside the packing, a weight outside 0..1,000,000, a struck critter without its cell, an item not in the NPC's seed list on the hero's map (or dropped, thrown or destroyed), an item with any script point (the throw script 6 and the removal's scripts are not run), an item that is not the wielded melee weapon, a no-drop item, then `AttackByNpc`'s refusals. A resolver refusal throws nothing. |

No new state: the thrown item's ground cell is a map change (`item.dropped`, as a critical's drop), which the snapshot already
carries; the schema stays 57.

## Item use (ARC-WORLD-108)

The support rules' item half and the item-use goal get their host: an NPC lists the items it holds, heals with them at the pass's heal
step, casts from them and uses them, through the map's cast host (whose item lookup knows the NPCs' items since ARC-UI-031).

| Part | Original | What it does |
| --- | --- | --- |
| Items in the lists | 0x4CC810, 0x4CC930, 0x4CCA30, 0x456A10, 0x453CC0 (SP10) | `Inventory` is the NPC's list as the session shows it, worn items included (`AreaCombatHost.NpcItems`: seed items still held, items given or made for it, never the taken, dropped or destroyed ones; an item without fields is left out); `HexedWeapon` the wielded weapon (slot 1004 of the session's wear) with item flag 0x40; `Item` reads its fields (spell slots, 10000 as −1; identified flag 0x1 or the session's identification; food or scroll exempt; valid-AI-action flag 0x200000 and AI action field 0x6A) with the spell charges as the cast host gives them (the session's spent charges); `CanUseItem` and `ItemFailChance` are the cast host's (`AiCanUseItem`, `AiItemFailChance`). In the Original setting's list cache (SP11) an item the NPC used or cast from that is then destroyed or out of charges drops the NPC's slot at its next list request (the force-removal 0x467860 → 0x4CCC00, the exhausted item 0x450420 → 0x4CCC40); an item given to the NPC (the insertion's drop, 0x466640) does not yet. |
| Heal step | 0x4A8940, 0x4A8AA0 | The pass's first step runs `AiSupportRules.Heal`: a choice becomes the pass record's for this pass's perform (the pass then skips the item search and the danger update); an AI-action item is used at once. The heal skill's rung finds the NPC's healing item (ARC-WORLD-116, above). |
| Use at once | 0x4ABF10 → 0x462CC0 | An AI-action item entry the chooser meets (at the heal step or the danger update's spell choice) is used at once through the item use below, unchecked, with the entry's target; the pass record keeps no choice. |
| Cast from an item | 0x4AC250 (NU1, NU4; MG47) | A choice with an item and pass action 1 (a scroll, a wand: an item whose first spell has no item triggers) is `SessionSpellCaster.CastByAi` with the item as source and the NPC as parent (the power it draws from the item); after a passed check the item's runs' sources are cleared and a food or scroll is destroyed out of the NPC's list (`RemoveItemFromNpc`), whether a run started or not. The Original setting clears the sources for any item, so a wand's run pays no charge (MG47); Steamwoven only for the food or scroll it destroys. The casting goal is the NPC's own (above). |
| Goal 39 | 0x435450, 0x428CD0, 0x462CC0 (AG15, NU2) | A choice with pass action 2 (an item whose first spell has item triggers, an empty first slot or the valid-AI-action flag: a potion) starts goal 39 at the choice's target; on the target's tile or next to it, with the item still the NPC's (`ItemHolder`), the item use runs with the NPC's world: `UsedItem` from the item's fields and the cast host's charges (a scroll needs intelligence 5), the magic branch through `SessionItemTriggers.UseItem` (for an NPC holder the slot-0 activation casts nothing, NU2; the item used executor's spells start at the target and play at once; a food or scroll is spent out of the NPC's list), 4 action points in a turn-based round through the round's consume. Each use logs `ARC-WORLD-108 ai-item-use` in the viewer and refreshes the inventory. |

No new state: the pass record's choice is the host's; the spent item and the spells' runs are the session's (the deletion and the
spent charges are saved as the hero's are).

## Heartbeat scripts (ARC-WORLD-114)

The scheduler's first-heartbeat (point 10) and heartbeat (point 19) script calls (CL7) run the NPC's script now: the host's
`RunScript` asks the scene's `Heartbeat` port for these two points (the other AI points still run no script and continue). The viewer
answers it with the shown map's use-script host (`AreaUseScriptHost.RunHeartbeat`): the NPC is the attachee and the triggerer, its
slot is its row's attachment under the session's map changes `scr10.*` / `scr19.*`, and the run's outcome is the tick's ("run
default" goes on to the pass; "skip default" stops it, also a skip default by the dispatcher's rule: a missing script file, a run
that falls off the end or uses up its line budget; each logs `ARC-WORLD-114 heartbeat-skip` once per NPC, point and stop). A run that
is refused (a script outside the set below, the content unavailable or changed) or whose effects the viewer cannot apply goes on as
without a script, and logs `ARC-WORLD-114 heartbeat-refused` once per NPC, point and reason.

| Part | Original | What it does |
| --- | --- | --- |
| Set | `AreaUseScriptHost.HeartbeatAdmission` | S2 (control flow, local and global flags, counters, arithmetic, story state, remove and change this script) with the single-object focus kinds of S3, plus toggle state (56) and kill (58) of [set S16](../scripting/scr-dispatcher-runtime.md#object-state-actions-arc-world-114-set-s16), and since ARC-SCRIPT-047 loop for, loop end, get object type, call-ex (81) and dialog with the looped object and the critter vicinity kinds 8 and 9 of [set S17](../scripting/scr-dispatcher-runtime.md#critter-vicinity-and-the-dialog-point-in-call-ex-arc-script-047-set-s17), and since ARC-SCRIPT-049 the conditions following (18), is named (20), is dead (22), is switched off (27) and has a reputation (33) and the actions call script in (54, the session's queue), distance between (71) and set faction (134) of [set S18](../scripting/scr-dispatcher-runtime.md#heartbeat-world-questions-arc-script-049-set-s18). A heartbeat runs every few seconds for every NPC within 30 tiles, so the set stays this narrow. |
| Talk | 0x44B170, 0x4449B0 (TG5, TG6) | A heartbeat that calls its NPC's dialog point (Virgil's 1100 for the PC in his vicinity) returns a dialog start; the viewer opens that talk after the event pump (`OpenHeartbeatConversation`, the first one asked for). The run is applied already (the heartbeat removed itself), so a talk that cannot open yet (a conversation or a window is open, the hero is under attack) stays pending until it can (a project choice; the original's behaviour there is not read); one for another map, or left when the AI host retires, is dropped. It logs `ARC-SCRIPT-047 heartbeat-talk`. The callee's slot is the NPC's dialog slot (map changes `dialog.*`), which the talk resumes. The vicinity answers NPC rows at their live cells (AI-moved, companions) and the PC; every other place the use host reads for an NPC (a creation's spot, the scenery vicinity's centre) is that live cell too. |
| Kill | 0x45D900 (PM3) | The world's kill: `GameSessionOwner.Kill` on the NPC's life target (no killer: no experience, no kill notification), drawn with the plain fall; the NPC is then a dead NPC to loot. Only an NPC of the shown map whose combat values read (`AreaScriptWorld.CanKill`, checked before anything applies); anything else leaves the run unapplied. A session refusal of the kill itself, after the run's earlier effects applied, stops there as the other hosts' steps do (applied steps stay). |
| Toggle state | 0x4487CE (PM2) | Flips the map change `obj.off` of an NPC of the shown map (1: switched off; absent: as its map data has it). A switched-off NPC is in the map's gone set (`AreaObjectTargets.Gone`): it is not drawn, not picked and blocks no step, the gate stops its passes (`Hidden`, object flags 0x102, now also for an NPC its map data has off), and the hostility facts read it as destroyed or off, as the original's object flag test does. An NPC its map data has off is never switched on (its T0 body is not drawn at all): that leaves the run unapplied. |
| World questions | 0x45D8D0, 0x441AE0, 0x45D0C0 (HW1–HW4) | Dead: the PC's life, an NPC's as the session holds it (map data or a kill); another object is not answered and stops the run. Switched off: the row's object flag 0x2 or, for an NPC of the shown map, `obj.off`; the PC never. Distance: the places of the vicinity (live cells of the PC and AI-moved NPCs, else the row's cell). Set faction: the map change `npc.faction` of an NPC of the shown map (absent while it equals its map data's); the reaction, greeting, barter and AI (one faction, the reaction's reputation share) read it; any other object is left as it is, an NPC of another map leaves the run unapplied. |
| Slot | 0x441980 | The changed slot is written back as map changes (a removed script reads 0, so the next tick runs nothing). |

At the Crash Site this is what makes the eleven unchosen pre-made heroes corpses and takes the chosen hero's double away
([character creation evidence](../research/character-creation-evidence.md#unchosen-pre-made-heroes-arc-world-114)); it is the
original's own behaviour, the same in both rule modes. A heartbeat runs only while the AI host runs: with a hero, and in automated
runs only in an AI smoke stage. The far heroes die when the PC comes within 30 tiles, as in the original.

No new snapshot field: the kill is the life domain's record, the off flag and the slot are map changes; the schema stays 57.

## Settings

One new switch, MG47 (`MagicAnomalyProfile.AiItemCastClearsAnySource`, [rule modes](rule-modes.md)): above. EX6, EX9, EX12,
CL1–CL5, HS9, AW16, SP16, FO7, FO8 and the morale switches act through the rules they belong to.

## Not ready

- **Turn-based rounds.** The end-combat script at an NPC's subturn end is asked, but no SCR runs at the AI points (below). A run
  killed from outside its step gets the hook on the NPC's next frame too (the original runs none; nothing in this host does that to
  the actor's run); followers stand in their subturns (I9's host part). The round's callback of a free is the table's (ARC-DEV-136): it runs at the free, before the hook, as the original's does, and at a turn set's interrupt of the last run; a run killed in its step by a refused charge is freed by the original after
  the new participant's hook and checked then, which this host does not do (the check then only records the stalled-actor
  observation). The turn set's check runs after the session granted the new participant's action points (the original's before them):
  in the Original setting the stalled-actor observation keeps the previous value either way (A11); in Steamwoven it records the new
  participant's own points, as A11's fix does for every check. A round that ends and another that begins between two observations (one frame) skips the round start's interrupt
  of every run. The fidget's animation is not played and its run not held (NPC ART is not read).
- **Skill goals.** An NPC's pickpocket (goal 86) and pick locks (goal 67) reach their attempts, which do nothing (the session's pick
  pocket and lock picking are the hero's); any skill but heal does nothing either. The skill animation is the fixed stand-in (NPC ART
  is not read) and is not drawn; the record's flag 0x2 from the goal frame's flag 0x1 is not read (AK2, F0).
- **Throws.** The thrown item does not fly (ARC-WORLD-113 lands it at once): no projectile is drawn, a miss strikes no critter on
  the tiles it passes (PF6), no item event fires at its end (PF10) and the item is never hidden while it flies; a first blocker on the line that
  is a scenery, a wall, a dead or unknown critter is not struck, nor is the hero standing on the line to another target (the
  line's map holds no hero): the item then lands on the tile without a roll; only the wielded "default throws" weapon is thrown (no
  generic items or grenades: the grenade step answers no grenade), only a seed item of the NPC's list without scripts
  (`ThrowableWeapon` answers no weapon for one the session added to the NPC or one with scripts, so the NPC attacks with it); the
  throw and remove-item scripts are not run and the item spells' unwear (0x4CBFC0) is not run.
- **Action goals.** Pickups (I8b's goals), grenades and goal 40 (the heal skill or pick locks with an item,
  0x4352C0): the host's calls refuse or do nothing; the item search chooses nothing. ARC-WORLD-116: the heal skill's rung does not
  search the substitute container of an NPC without a leader (HI1; a companion never has one); an NPC's item use runs no use
  script (an item with one goes on as if the script let it, which the trace notes), and a trap device's use, reading a written item
  and a lockpick do nothing; the cancel after an NPC's item use, which in the original returns the player's interface to its normal
  mode and closes an open targeting cursor (NU3), does not touch the viewer's cursor;
  an NPC's spell missile still in flight does not hold its subturn in a turn-based round (the NPC's run ends one pace after the
  launch; the hero's missile is the same): whether the round's "a run runs" test counts the missile's goal 28 (0x423300) is not
  read (F0); goal 83's casting animation is not played (NPC ART is not read: the release comes at once); the NPC's cast eye candy times
  its goal but is not drawn; the fail-chance predictor reads the fate-maximised flag unset. A cast host rebuilt between a release
  and its goal's end (a map change) forgets the release; no synthetic test covers the pass record's choice under the setter's refusal (FO8).
- **Followers in combat** (ARC-WORLD-115 runs the hero's companions and tells them of attack events; since ARC-WORLD-117 the
  viewer's orders carry the attack target and run the AI pass through the host; since ARC-WORLD-119 free companions are run as any
  leaderless NPC and the curse's attack event runs). Not ready: a curse from the viewer (the typed order text is not read), the
  curse's insult line, a leave outside an order seen only at the host's next pump or perform (an AI pass in between finds the old
  run; a leave and a join between two looks, as a second join's leave-first, C5, is not seen at all: the run is kept), an attack order on a non-critter object (the host's world knows critters only), the
  menu's target cursor drawn as the original's, the comment lines (taunts, near-death, the complaint's upset
  line), the world-map travel's stop and allow calls, the companion's loyalty step still on its own tick beside
  the AI tick (one tick in the original), a visiting companion whose home is the main map's window (no record: it neither fights nor is
  fought here, is told of no attack and is left out of the flee test's party count, ARC-DEV-065/075; MIG9), a script's faction change of a visitor (a change on the shown map), a companion's attack pose (its swing is not drawn), the skill animation (the fixed stand-in
  art plays for a companion's heal too). Since ARC-WORLD-116 a companion heals the hero with a healing item through the heal skill's
  rung, and one that knows a healing spell through the spell rungs.
- **NPC ranged attacks.** Since ARC-DEV-273 a bow or firearms attack of an NPC is decided and landed at once by the hit step (`SessionNpcAttack.LineOfFire`, RA4: a blocked line fails the check, else its cost is the cover; the NPC spends no ammunition, RA3: an NPC whose leader chain reaches no PC never spends, and a companion's spending is not modelled); NOT READY: the arrow's flight (the projectile of PF2 with the strike at its arrival; the next slice), the line of fire's redirect to another blocker (RA4) and a missile weapon or a boomerang's throw (still refused, `AttackUnsupported`). A "default
  throws" weapon is thrown through goal 37 (ARC-WORLD-113).
- **Scripts and routines.** Of the AI points only the first heartbeat and the heartbeat run their scripts, and only within the
  heartbeat set above (ARC-WORLD-114, ARC-SCRIPT-047); start and end combat run none; the turn-based subturn heartbeat (0x4B704A)
  runs no script ([script trigger audit](../research/script-trigger-audit.md)). A heartbeat's kill runs no dying script (point 12) and fires no
  item events of the death; a script that switches off or kills another object than an NPC of the shown map is not applied. No
  day routines (the fold's waypoint and standpoint steps exist since ARC-DEV-117: the bed, sleep, wander and the teleport request of another map do not); the danger hooks are not run.
- **Back-off.** The away path (0x41F3C0) is not walked: a back-off ends at once.
- **Drawing.** Since ARC-RENDER-038 an NPC the host moved (a step or a turn) plays its attack ART frame by frame while its play runs and in combat mode stands on frame 0 of its armed stand (the weapon part and shield it shows, WA1), read by `AreaBodyMoveArt.LoadArmed` with the original's names (MA2, MA3) and the missing-art repair (a missing low attack is the attack for bare hands and weapons 2..8, 10 and 13, MA1, WA10), then, as the hero's (the session's wear check does not test the art, WA8), without the shield, then bare-handed; an NPC never moved or turned keeps its T0 frame. Not ready: an attack begun out of combat mode is played (the original's node fails without an art there, WA5), a missing attack ART still attacks with the fixed timing (the original's node fails, WA5), a target mid-fall, getting up or knocked down (the hero after a knockdown critical too) is not read as lying, an attack animation ends only with a dropped run (a stun, a hit out of reach or leaving combat mode do not end it), the hero's shrink is not read, and the leaving of combat mode turns no playing attack into the stand (WA3); the smoke's fighter is an animal, so an armed human NPC is covered by the synthetic tests only. The fidget, skill, throw and casting animations of NPCs are not drawn (since ARC-WORLD-111 a step draws the walk or run cycle, see [companion follow](companion-follow-runtime.md#drawing-the-walk-arc-world-111)); attack sounds of NPCs are not played; the hero's fall
  and death after an NPC's hit are not drawn.
- **Positions.** ARC-DEV-094 (MIG7c): the cell and facing of a map NPC the host walked or turned are its object state (`SessionNpcPlacement`, `GameSessionOwner.RecordNpcPlace`), written at `BeginStep` and the turn and restored by a
  host built for the map; the companions' own overlay does not see them; a talk approach goes to a walked NPC's
  map data's cell. Not saved with the place: the goal run, the animation and the AI tick.

## Tests

`AreaAiFollowerTests` (viewer Core, synthetic rows, ARC-WORLD-115): the hero's hit tells its companion, which focuses the victim,
is held, starts its attack run, walks up through its own walk and strikes through the session in combat mode; an NPC's hit on the hero
tells the victim's follower; a waiting companion is not told; a peaceful companion's follow is its own walk's; without followers in
the scene nothing is run; next to the enemy past a corner object there is no first step and no route (AP1, AP2), while the enemy's
own T0 record on its cell does not block the first step; after the fight (state NONE, no run) the companion is released to its walk;
its step onto its own home record is not refused; since ARC-DEV-075 a visiting companion (a row of another home map, given or read on demand) is told of both attack events, is in the enemy's
square, counts in the flee test, strikes and is struck, while one with no record (ARC-DEV-065's case) is told of neither, is not in the square and the flee test runs without it; out of danger a companion holding a healing item heals the hurt hero through the
heal step's heal-skill rung, goal 33 and the session (ARC-WORLD-116); since ARC-WORLD-117 the attack order's target is the highlighted
object's entity (none, an object without one, another map's: none), an attack order through the host locks the companion on its target
and its AI pass starts the attack, which walks up and strikes, an attack order without a target or on the party does nothing but the
pass, without the host it lifts the stop and fails with no pass, back off through the host stops the fight (peace, interrupt,
NO_ATTACK) before its pass, whose gate finds the companion idle, and releases it to its walk, and the stop's interrupt ends the attack
run only when asked. `NpcAttackStateTests` (Session): the monster table, bare hands and gauntlets, a wielded weapon, a bow refused; the hero bitten and
paying nothing; an NPC victim's pool and the kill award for a PC-led killer only; the hero's critical-miss fate; the refusals.
`AreaAiHostTests` (viewer Core): the movers' steps and slide; on the installed original (`ARC_AREA_CONVERSATION_GAME_ROOT`,
`ARC_CAVE_MOBILE`) an animal of the Crash Site cave that the hero hits reacts, focuses the hero through its tick's danger update,
starts its attack run (adjacent, and from 4 tiles after walking up) and bites through the session; in a turn-based round it walks up
and bites in its own subturns, its run holding its subturn and paying action points, never moving on another participant's subturn,
and the pending cost reads 0 after a subturn start (ARC-WORLD-095); the done attack run's fidget in real time, the end-combat script
asked at its subturn ends and no fidget in a round, and a turn set's interrupt of its last run calling the round's callback once
(ARC-WORLD-099). `AreaAiCastTests` (viewer Core, opt-in `ARC_AREA_CONVERSATION_GAME_ROOT`, `ARC_AREA_MOBILE`): on the Crash Site
exterior the college NPC told of the hero's attack chooses a spell at its danger update, casts it at the hero through the cast host,
the release begins the run and its goal's end frees nothing; in a turn-based round it casts in its own subturn and the goal's start
pays 4 action points; an invented cast eye candy times the release frame and the rest, a host retired mid-play frees the run, and a
play that never reaches its action frame releases nothing and its end frees the run (ARC-WORLD-102); a cast while the casting goal
plays is queued beneath it and the queue plays in the order cast, the queue refuses at depth 4 and in a turn-based round, freeing the
run, a retired host frees the queue, and a queued goal that ends through the end step frees the run of the cast beneath it
(ARC-WORLD-104); with every spell given a missile, the action frame launches it (no release, the rest one pace longer), the goal's
end frees nothing, the arrival after it begins the run, a run dropped mid-rest leaves the missile's run, whose loss frees it, and a cast host rebuilt before the
goal's end neither frees nor charges it (ARC-WORLD-105). `AreaAiHostTests` also: an animal walking up to bite is busy at its attack goal's priority 3 (the bottom goal,
ARC-WORLD-105). `AreaAiSkillTests` (viewer Core, synthetic rows): an NPC's heal skill on itself walks goal 33 into 34 at priority
3 and at the action frame heals through the session, spending a use of its held item; a heal without a held healing item changes
nothing; since ARC-WORLD-116 the heal step finds the held healing item and the check's refusals (no healing item, no critter), and a
hurt NPC's heal step chooses the heal skill on itself and heals through goal 33, while an undead healer is refused and chooses nothing; pickpocket and pick locks reach goals 86 and 67, whose attempts are not ready; the stand-in animation's timing (since ARC-WORLD-113 the throw's
too) and the turn (ARC-WORLD-109). `AreaAiThrowTests` (viewer
Core, synthetic rows, ARC-WORLD-113): the attack starter of an NPC wielding a "default throws" dagger starts goal 37 at the hero's tile
without changing the session, the throw at the action frame strikes the hero and the dagger lands on its cell or its scattered tile,
leaving the NPC; a weapon without the flag attacks; an item the NPC does not hold fails; a throw at an empty tile lands the dagger
there without a roll. `NpcThrowStateTests` (Session, ARC-WORLD-113): a throw at the hero checks with the throwing skill and lands the
dagger on the hero (a hit) or within the scatter (a miss), takes it out of the NPC's list, schedules no decay and cannot be thrown
again; a throw at an empty location rolls nothing; a throw at another NPC; the refusals before any roll. `HealStateTests` (Session): an NPC master heals the hero with its own item, charging
nothing and leaving the hero's heal fate; an NPC heals itself through its life record, its crippled arm making the check harder, and
its last use destroys its item; the refusals before any roll (ARC-WORLD-109). `ActionGoalsTests` (Simulation): a queued goal 26 at the bottom plays once the goals above it have popped, and one
refused at its start takes the end step (ARC-WORLD-104). `SessionSpellCasterAiTests` (Session): the NPC's book from its fields with its mastery halving
the cost, the record facts, the global lists, the free maintenance count, perform's cast and under the influence, a refused check,
the fail-chance predictor (ARC-WORLD-102). `AreaAiItemTests` (viewer Core, the opt-in data of `AreaAiCastTests`): the college NPC
given an installed healing potion and hurt to 60 % lists it on its light-healing list, its heal step chooses it, goal 39 uses it on
itself (the `Item_Used` spell heals it) and the potion leaves its list, in both settings, the Original setting's cached list then no
longer listing it (ARC-WORLD-108). `NpcItemUseTests` (Session): an NPC's potion
fires `Item_Used` and is spent out of its list, its wand casts nothing from slot 0 (NU2), the AI's cast from a scroll destroys it and
clears the run's source, from a wand pays its charge in Steamwoven and none in the Original setting (MG47), the refusals and the item
answers. `ExecutorRulesTests` (Simulation): `FidgetAfterRun`'s combat mode, auto-animate flag, running animation, art test,
PC, and in a round the action points and the gate. `CombatRoundTests` (Session): the subturn starts are counted
whichever call began them, A9's twice, and not saved. Native smokes: `ARC_AREA_MAP_SMOKE_ONLY=ai` on `crash site cave` with
`--hero`, marker `ARC-WORLD-092 native-smoke ai PASS` (since ARC-WORLD-099 also the fidget after the bite's run, `Fidgets` in the
marker; since ARC-WORLD-109 a living animal's pickpocket run on itself, started through `PerformSkill`, walks goal 33 into 86 and its
not-ready attempt reaches the viewer, `Skills` in the marker); `ai-turn-based`, marker `ARC-WORLD-095 native-smoke ai-turn-based PASS` (since ARC-WORLD-099 also the end-combat script
asked at the NPC's subturn end, `EndCombatScripts`); `ai-cast` on `Arcanum1-024-fixed` with `--hero`, marker
`ARC-WORLD-102 native-smoke ai-cast PASS` (since ARC-WORLD-105 the NPC then casts the first listed missile spell whose run starts at
the hero, as its pass record's choice: the missile flies from it, is drawn and arrives; `MissileSpell`, `MissileLegs` and
`MissileSteps` in the marker); `premade` on `Arcanum1-024-fixed` with `--hero` (ARC-WORLD-114): the hero stands by the north and then the
south group of the twelve pre-made heroes' NPCs until their heartbeats have run; the chosen hero's NPC is switched off and not drawn,
the eleven others are killed, drawn fallen and lootable as dead NPCs, the map data's own dead NPCs stay drawn and lootable; marker
`ARC-WORLD-114 native-smoke premade PASS` (`ARC_PREMADE_SMOKE_SHOT` names an optional PNG of the view); `ai-follower` on
`Arcanum1-024-fixed` with `--hero` (ARC-WORLD-115): a free speaker (Virgil) joins, the hero stands a few cells from the nearest living
NPC that is not a pre-made hero's and attacks it; the follower is told, held, walks up through its own walk and strikes; marker
`ARC-WORLD-115 native-smoke ai-follower PASS` (since ARC-WORLD-116 first, where the hero stands, the follower given an installed healing
item heals the hero hurt to half through the heal skill's rung and goal 33; marker `ARC-WORLD-116 native-smoke ai-heal-item PASS`;
since ARC-WORLD-117, right after the blow, the viewer's back-off order stops the fight with its AI pass and an attack order on the NPC
as the highlighted object locks the follower on it, its pass starts the attack and the follower strikes again; marker
`ARC-WORLD-117 native-smoke ai-order PASS`; since ARC-WORLD-119 a curse order then frees the follower, the hero's attack event on it
runs and asks its reaction before its pass, and the same host runs it on as a free NPC for 200 frames; marker
`ARC-WORLD-119 native-smoke ai-free PASS`, before the stage's own; the stage restores life and combat mode but leaves the session's
danger records, who-hit-me-last, team and the follower's lowered reaction toward the hero, so a later stage in the same run meets a
cursed Virgil); since ARC-DEV-065 the `transfer` stage with `--hero` (Virgil carried into the Crash Site cave as a visitor, after
his visitor talk) turns the AI on, the hero strikes the cave's living monstrous NPC with the fewest hit points and that NPC bites
back while the visitor stands there; since ARC-DEV-075 Virgil, resolved by identity, is also told of the attack, stands in the NPC's
square and strikes it (`Resolved=true`, `VisitorHit` in the marker; an unresolved visitor keeps the ARC-DEV-065 checks): marker `ARC-DEV-065 native-smoke visitor-fight PASS` (without a hero it prints `NONE`);
`opening` on `Arcanum1-024-fixed` with `--hero` (ARC-SCRIPT-047): the AI runs with the hero where the new game puts it, without any
input; Virgil's heartbeat 1100 opens his talk (DLG 1324, a talk at the dialog point), removes itself, and after the talk is closed
no second talk opens; marker `ARC-SCRIPT-047 native-smoke opening PASS`.

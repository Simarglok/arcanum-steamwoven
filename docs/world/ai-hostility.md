# NPC hostility and targets: validity, kill-on-sight, the scan, attacks and peace (ARC-WORLD-080)

Status: **Implemented bounded component**, ARC-WORLD-080, NPC combat AI plan slice
[I4](../research/npc-combat-ai-plan.md#implementation-slices-proposals-after-the-matching-research).
The hostility and targeting rules of the retail 1.0.7.4 executable ([hostility study](../research/npc-ai-hostility.md), stage A3;
[noise and witnesses](../research/npc-ai-awareness.md#6-noise-and-witnesses-s9), stage A2; ledger
[HS1–HS22](../research/ledger/npc-ai.md#npc-ai-hostility-and-targeting-arc-res-057), two readers, re-read by this task) as pure
rules in `Arcanum.Simulation.Ai.HostilityRules` over an `IHostilityWorld` and an `IAiRollSource`, and the writes they decide in
`Arcanum.Session` (`GameSessionHostility.cs`) on the danger records of slice I3 ([danger state](ai-danger-state.md)). No AI pass,
combat path or host calls them yet: the scheduler (I6, [AI scheduler](ai-scheduler.md)) runs the danger update as a host stage, and the host behind it (I10) and the executor (I7) are their first callers.

## Rules (`HostilityRules`)

The world (`IHostilityWorld`) answers by object id: the object (`HostilityCritter`: type, the object, critter, spell and NPC
flag bits the rules test, perception, intelligence, level, alignment, hit-point percent, origin, direct and PC leader), the tile
distance, faction-same (0x45D110, the reputation owner's `FactionSame`), the AI reaction variant toward a PC (0x4C0CE0), the
will-kos script's verdict (point 22), the AI parameters 4, 10, 11 and 12, sight and hearing (`AwarenessRules`, slice I2), an
NPC's danger record, its mind controller (0x459040) and the scan's rectangle query in the map's order. A stored handle the
world no longer knows (a focus, source or enemy that left the map) counts as none.

| Rule | Original | What it decides |
| --- | --- | --- |
| `Validate` | validity 0x4AB990 | `TargetValidity`: the first failed test of 15 (missing; destroyed, off, not drawn or invulnerable; itself; for a critter: dead, unconscious, its leader, a fellow follower, its follower, stoned, invisible (`CanTargetInvisible`, 0x45A580), the decoy for a smart NPC; a wall or a busted object; beyond 20 tiles of it or of its leader; an unseen concealed master prowler). The unconscious test runs the scan outside a scan and never inside one (re-entrancy, HS21). |
| `KillOnSight` | 0x4AE120 | `KillOnSight` reason: NO (invalid, script veto, surrendered to it, enshrouded), FACTION, REACTION (parameter 10), ALIGNMENT (parameter 11), DECOY, GUARD. |
| `Protect` | 0x4AE3A0 | `Protection`: SELF, GROUP, NO (mind-controlled), FACTION, ORIGIN (guards), NO. |
| `Upset` | 0x4ADCC0 | `UpsetReason`: NONE for an obeying mind-controlled NPC or a non-critter target, PARTY, GOOD (parameter 12), ORIGIN, FACTION. HS9 switch. |
| `FindTarget` | 0x4AB460, avenge 0x4AE450 | `ScanResult`: refused (invulnerable, nigh-invulnerable, the decoy) or the square of half-size `clamp(Perception, 1, 10)`, its first 50 critters sorted by key (0 for a decoy for the NPC, else the distance, +1000 unconscious; stable); per candidate, perceived (heard at Silent when concealed, else Normal, then seen): a kill-on-sight candidate, or an ally's protected-against danger source (ally help); perceived or not, a dead protected ally's focus (avenge). Nothing found: the concealed PC to turn toward. |
| `ChooseTarget` | 0x4AB0B0 | Current or newcomer by rows 1–10, else the d100 `ChooseTargetScore` (1..50 `level − distance`, else nearness; a tie keeps the current). |
| `PickEnemy` | 0x4AFA90 | `start := random(0, n − 1)` (`EnemyListStart`; an empty list draws −1..0, one entry draws nothing), the first valid entry from there, wrapping. |
| `DangerUpdate` | 0x4A92D0, 0x4ABBC0 | `DangerUpdatePlan`: asleep; NONE: the scan's target or nothing; COMBAT_FOCUS: the valid focus, else unlock and rescan, else who-hit-me-last, else the pick, else NONE; FLEE: the fleeing helper (`MoraleRules.FleeSpell`, [morale](ai-morale.md)); SURRENDER: at 80 % hit points or more, `random(1, 500) = 1` (`SurrenderCalm`) asks for NONE. |
| `FocusTarget` | (danger update) | The setter's target after the spell and item choice (I8): the found target; SP16 switch for the enemy-list path. |
| `KeepsChosenAction` | (danger update) | Whether the chosen spell or item stands after the setter call: FO8 switch when rule 4 refused the fight. |
| `ReactToAttack` | 0x4AA620, weigh 0x4AB2A0, `FleesFar` 0x4AB030 | `AttackReaction`: dead → nothing; NONE → weigh; COMBAT_FOCUS or SURRENDER → weigh when hit by its source or choose target prefers the attacker, else keep the focus or a flee goal; FLEE → keep fleeing the attacker, else weigh when mind-controlled or beyond parameter 4. Weighing asks the caller's should-flee (`MoraleRules.ShouldFlee`; `MoraleRules.ReactToAttack` passes it, [morale](ai-morale.md)) for FLEE or COMBAT_FOCUS on the attacker; every living NPC joins the round. |
| `AttackEvent` | 0x4A9650 | `AttackEventPlan`: where the event stops (`AttackEventStop`) and each step for the caller, flags accidental, no fan-out and ordered (`AttackEventFlags`). |
| `NoiseWitnesses` | 0x4A9E10 | The witnesses in order: the attacker's square when under 30 tiles apart, then the victim's when over 1 tile apart. AW16 switch. |
| `Witness` | 0x4A9F10 | `WitnessOutcome`: nothing for the two sides and their followers; woken when asleep; react to the attacker (protects the victim and perceives it), react to the victim (protects the attacker and perceives it), or flee from the attacker (a bystander that is no guard, may flee, is calm and perceives the victim). Sight before hearing. |

## Session (`GameSessionOwner`)

| Operation | Original | What it does |
| --- | --- | --- |
| `MakePeace(request)` | peace 0x4AA300 | One revision: head := the other's PC leader (the hero for its followers) or the other; the NPC's focus and who-hit-me-last cleared, the head off its list; for the hero, the standard reaction below 50 raised to 50 through the PC's effects (`SessionPeaceFacts.Reaction` needed); the setter asked for NONE (exit hook); each NPC of the hero's follower array forgets the NPC and leaves its list. One-sided: the head's own record keeps the NPC. |
| `StopAttacking(request)` | 0x4AA4A0 | One revision, for an NPC or each of the hero's followers: with a focus, peace with it, the animation interrupt and the combat-mode exit (`SessionPeaceFacts.CombatMode` needed when in combat mode); a follower of the hero gets NO_ATTACK; the lock is cleared. Returns one `SessionPeaceStep` per NPC. A peace with the hero's side always needs its reaction facts, so a stop of the hero's party whose follower fights the hero or a fellow follower needs that follower's `Reaction` entry. |
| `ApplyAttackEvent(request)` | 0x4A9650 steps 10–11 | Who-hit-me-last and the team adds both ways as the plan decided; a stopped plan writes nothing. |
| `PickDangerEnemy(entity, valid)` | pick 0x4AFA90 | The pick over the NPC's list with the host's validity, drawing `ai.EnemyListStart` from the session generator; no revision. |
| `AiRolls` | | The session generator as `IAiRollSource`, labels `ai.ChooseTargetScore`, `ai.EnemyListStart`, `ai.SurrenderCalm`. |

The reaction queries and changes (`QueryReaction`, `AdjustReaction`, `SetReaction`, `ReactToPcHit`, the wait expiry) now take
"the PC is on the NPC's enemy list" from the NPC's danger record, not from the caller (reaction R1). A follower that leaves the
hero (leave, the leader check's leave, the expired wait) loses NO_ATTACK (FO7). No new state: the snapshot stays schema 38.

## Settings

| ID | Original 1.0.7.4 (switch on) | Steamwoven | Switch |
| --- | --- | --- | --- |
| HS9 | Upset counts a leaderless target as the party of a leaderless NPC. | PARTY needs a non-null leader: leaderless townspeople help allies against the PC and guards turn on leaderless aggressors. | `CombatAnomalyProfile.UpsetPartyMatchesMissingLeader` |
| AW16 | An NPC in both alert squares is handled twice (its reaction and target roll repeat). | Handled once, at its first place. | `CombatAnomalyProfile.WitnessHandledTwice` |
| SP16 | The enemy-list path passes the pass record's target, which a summon or self-aimed spell made the NPC itself. | The pick is passed. | `CombatAnomalyProfile.EnemyListFocusTakesSpellTarget` |
| FO7 | No leave clears NO_ATTACK. | Leaving the hero clears it. | `CombatAnomalyProfile.NoAttackSurvivesLeaving` |
| FO8 | The action chosen for the target stands when setter rule 4 refuses the fight. | It is dropped. | `CombatAnomalyProfile.NoAttackKeepsChosenAction` |

## Not ready

- **Callers.** The AI pass (I6) runs the danger update only as a host stage that no host implements yet, no combat path builds an attack event, no host runs the witness rule,
  peace or stop attacking (the companion "stop" order is I9's; the script and dialog attacks and stop attacking are the
  dispatcher's S-slices; pick pocket's detection is ARC-SKILLS-010's). The hosts implement no `IHostilityWorld` yet.
- **Steps returned, not run.** The follower rule (0x4A9C00, I9), the PC victim's response (0x4A9AD0), the turn, the goal and
  animation interrupts, the fleeing line, the flee goal, the round entry and the float lines are the caller's; should-flee is
  slice I5's ([morale](ai-morale.md)).
- **Nested scan's turn.** Validity's unconscious test outside a scan runs a scan whose turn toward a concealed PC is not
  returned (a rotation only).
- **Exit gate.** The combat-mode exit gate's threats (`SessionCombatThreat`) still come from the host; the danger records could
  supply them later.
- **Redirect** (0x4A9560, P3) is slice I8's.

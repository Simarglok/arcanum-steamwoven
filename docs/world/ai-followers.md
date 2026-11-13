# NPC AI followers in combat: the follower rule, combat orders and comment lines (ARC-WORLD-091)

Status: **Implemented bounded component**, ARC-WORLD-091, NPC combat AI plan slice
[I9](../research/npc-combat-ai-plan.md#phase-1-faithful-reconstruction).
The followers-in-combat rules of the retail 1.0.7.4 executable ([followers study](../research/npc-ai-followers.md), stage A7;
ledger [FO1–FO20](../research/ledger/npc-ai.md#npc-ai-followers-in-combat-arc-res-067); FO18 and FO19 read by this task, FO20 by
ARC-WORLD-119, one reader) as pure rules in `Arcanum.Simulation.Ai.FollowerRules` over the hostility slice's `IHostilityWorld` and an
`IAiRollSource`, the follower rule's writes in `Arcanum.Session` (`GameSessionFollowers.cs`) and the combat orders attack, back
off and curse in the companions thread's single order entry `GameSessionOwner.IssueCompanionOrder` (ARC-WORLD-046,
[companion orders](companion-orders-runtime.md)). It covers the combat half of
[FR-SOCIAL-004](../requirements/story-social-ai.md#fr-social-004) and the follower side of
[FR-AI-002](../requirements/story-social-ai.md#fr-ai-002). Upset attacking, faction-same and origin-same are the hostility and
reputation owners' (`HostilityRules.Upset`, `ReputationRules.OriginSame`, the world's `FactionSame`); nothing is copied.

## Rules (`FollowerRules`)

| Rule | Original | What it decides |
| --- | --- | --- |
| `Rule` | follower rule 0x4A9C00 | `FollowerRuleOutcome` for follower F of leader L against target T, first match: L is T → `LeaderIsTarget`; T no PC or NPC → `AccidentalObject`, `Lock` (ordered) or `Focus`; F is T or dead → `SelfOrDead`; upset (F, T, L) not NONE → `UpsetIgnored` on the victim's side, for T in combat mode or an accidental hit, else `Complain` with the reason and whether F is active (it then floats the line; the −5 applies either way, FO10); else `Accidental`, `Lock` (ordered) or `ReactToAttack` (the caller's `HostilityRules.ReactToAttack` with its should-flee). |
| `TellFollowers` | 0x4A9B80 | Nothing when L is the other side; otherwise `Rule` (never ordered) for each of the caller's list of L's direct followers, in its array order. The attack event calls it for the attacker's followers (after its early exits) and the victim's (`AttackEventPlan.TellsAttackerFollowers`, `TellsVictimFollowers`). |
| `AttackOrderRefused` | broadcast type 10 | Outside the PC's combat mode a target whose PC leader is the PC is refused; a PC target is not. |
| `UpsetLines` | 0x414370 | GOOD 4900–4999, ORIGIN 5000–5099, FACTION 5100–5199, PARTY 4800–4899 (FO18). |
| `TauntKindOf`, `Taunt` | 0x4B6B90, 0x4AE9E0 | With the "combat taunts" setting, a PC attacker, a struck object outside the PC's party and a hit: kind 0 (critical on the aimed object, 2400–2499) or kind 1 (another object under record flag 0x8, 2500–2599; the flag's one writer is the critical-miss turn 0x4B6410, so it is the hero's self-hit when the attack's end calls the taunt: not for an ammunition shot or a held throw; ARC-AI-1639, `HeroAttackTauntFacts`). Then `random(1, 2)` (`ai.TauntSpeak`) must not be 1, a follower is drawn by `random(0, n − 1)` (`ai.TauntFollower`, no draw for one) and must be active. |
| `NearDeath` | 0x4AEAB0 | A hit with hit-point damage on a critter with a PC leader taking it from at least 20 % to below: 5500–5599 toward its PC leader (asked by `AreaAiHost.Hurt`, ARC-AI-1527). |
| `Active` | 0x45D790 | Not destroyed or off, dead, unconscious, stunned or paralysed (`HostilityCritter.Stunned`, critter flags 0x20, 0x40), or stoned. |

## Session (`GameSessionOwner`)

| Operation | Original | What it does |
| --- | --- | --- |
| `ApplyFollowerRule(request)` | 0x4A9C00's writes | One revision: `Lock` runs the target lock (0x4AFBD0: unlock, the setter for COMBAT_FOCUS without sound, the lock when it then reads that focus); `Focus` asks the setter for COMBAT_FOCUS on the target without sound (FO18); `Complain` changes the follower's reaction toward a hero leader by −5 through the reaction change rules (R2; nothing for an NPC leader; a drop arms the follower's leader check, OL17). Facts: the setter's, the hostility reaction for a fight on the hero and the follower's reaction facts for a complaint. |
| `IssueCompanionOrder`, attack | broadcast type 10 (FO3, FO4, FO9) | No target (`CompanionOrderRequest.Target`) → `NoTarget`; the party filter → `PartyTarget`; otherwise in one revision NO_ATTACK cleared, then the ordered attack event (`HostilityRules.AttackEvent` over `CompanionOrderRequest.World`): an early stop → `AttackIgnored` with the stop; else the follower rule with the follower's direct leader → `AttackLocked` (`Locked`, the setter step's hook), `Complained` (rule, line, reaction) or `AttackIgnored`. The cleared stop stays in every case past the filter (FO9). No line. A step the session cannot make (no world, missing reaction facts for a complaint) is `Failed` but still clears the stop. |
| `IssueCompanionOrder`, back off and stop | broadcast type 12 (FO5) | The acknowledgement (2100–2199) and `StopAttacking` for the follower (`Stopped`, the peace step for the host's interrupts). |
| `IssueCompanionOrder`, curse | broadcast type 7 (FO2) | Mind controlled → `MindControlled`; an unforced leave (the hero's charisma, else the consent facts' `PcCharisma`) → `LeaveRefused` for an extraordinary leader; else `Cursed`: the follower is free (FO7 clears NO_ATTACK in Steamwoven), `Insult` (the race insult, FO19: base 1000 of the race-line tables, chosen by the host) and `CurseAttack`, the hero's attack event on it at loudness 1 with no flags, which the host runs in full (fan-out, noise, its reaction to the attack). |
| `IssueCompanionOrder`, the AI pass | call 0x4C389D (FO1) | With `CompanionOrderRequest.Ai`, each reached follower gets one `RunAiPass` right after its own order and before the next follower's, whatever the order did (`CompanionOrderOutcome.Pass`). Since ARC-WORLD-117 `CompanionOrderRequest.Ordered` is the host's part of each order, called with its outcome before that pass (a back off's animation interrupt, which the original makes inside stop attacking). |

Per follower the order's combat facts are `CompanionOrderFacts.Combat` (`SessionPeaceFacts`: the setter's facts, the reaction facts
of a complaint and of a stop's peace with the hero's side, the combat-mode facts of a stop); invalid ones, or an invalid target
handle, refuse the whole order. No new saved state: the snapshot stays schema 48.

## Cleanup after a fight

The cleanup rows of the study ([section 7](../research/npc-ai-followers.md#7-after-the-fight)) are already in place: the setter's
exit branch (FIGHTING, BACKING_OFF, DEMAINTAIN_SPELLS, the exit-combat hook) and the FO14 lock clear in slice I3
([danger state](ai-danger-state.md)), the gate's demaintain step in slice I6 ([AI scheduler](ai-scheduler.md)), peace, stop
attacking, allow attacking on the hero's combat-mode entry, FO7 and FO8 in slice I4 ([hostility](ai-hostility.md)), the target
unlock on a combat-mode exit in ARC-COMBAT-004. This slice adds no switch: FO9, FO10, FO13 follow the original in both settings and
FO16, FO17 are fixed in both.

## Settings

| ID | Original 1.0.7.4 | Steamwoven | Switch |
| --- | --- | --- | --- |
| FO16 | The near-death line floats an empty line for a follower killed through the 20 % mark. | Nothing. | none (both fixed) |
| FO17 | The taunt passes the drawn array entry to the activity test without a null test. | A null entry says nothing. | none (both fixed) |

FO7, FO8 and FO14, the stage's switches, are read where the leave, the danger update and the setter run (slices I3 and I4).

## Not ready

- **Host.** Since ARC-WORLD-115 the AREA viewer's AI host runs the hero's companions and its attack events tell the followers
  ([AI host](ai-host.md#followers-in-combat-arc-world-115)). Since ARC-WORLD-117 the viewer's orders go through that host: the
  attack order's target is the highlighted object (F2: the one under the pointer at the release; the portrait menu: the object of
  the next press, its target cursor; U2 stays an inference), the followers' combat facts and the attack event's world are the
  host's, each reached follower's AI pass runs through it, and a back off's interrupt is the host's. Since ARC-WORLD-119 the host
  runs free companions (released or cursed) as any leaderless NPC (FO20: the removal leaves no mark but the null leader, which the AI's follower branches read),
  a leave ends the leaver's run (the removal's interrupt at priority 5), and the curse's attack event runs through the host right
  after the order, before the former follower's pass. The host's world knows critters only (an order on a container or a ground
  item is ignored at its attack event; a door or scenery has no session entity, so no target). The viewer sends no curse: the
  typed order text (the Enter chat and `broadcast.mes`) is not read, so only tests reach it.
- **The attack event in full.** The AI host's attack events run the fan-out (ARC-WORLD-115) and, since ARC-WORLD-119, the curse's;
  its race insult is not floated. The comment lines (ARC-AI-1527) are asked by the host: `AreaAiHost.AttackEnded` (the taunt kind
  and draw, 0x4B6B90 → 0x4AE9E0) at the end of the hero's hit, `AreaAiHost.Hurt` (the near-death line, 0x4AEAB0) after each critter
  hit the viewer resolves (the attack damage path of 0x4B4390) and after a script's damage, and the complaint's upset line in
  `TellFollowers`. Each names the speaker and the message range (`AreaAiHost.FollowerSpeaks`); the viewer asks the can-speak head
  (0x4AD800, `CanSpeakRules.Head` and the unconscious test) and `AreaConversationHost.SayFollowerLine` reads a random line of the
  range (`DialogFollowerComment.Say`: the speaker's own override line 11999 + range / 100 first, else the `gd_npc_*` file of the
  speaker's and the PC's genders, the names expanded) and floats it over the follower as a white script line. The "combat taunts"
  setting is on the options window's Video page (default on, the settings file's `combat taunts` line). The line texts are the
  player's and are never stored; no voice is played. NOT READY: the taunt of a hit on a door or scenery, the strike-time self-hit of an ammunition shot
  ([#1683](https://github.com/Simarglok/arcanum-steamwoven/issues/1683)), the race-line selection. The near-death line is asked for every critter damage as the original's damage function does (ARC-AI-1638): the attack path, a script's answer and the spell, trap and burn damage steps (`TickCasterDamage`; a script's step is marked `FromScript` so it asks once).
- **World-map travel.** Its window's stop attacking for the hero on opening and allow attacking on return (FO5, FO6) are the
  travel host's calls of `StopAttacking` and `AllowAttacking` with the hero; the W panel does not make them yet.
- **Mind control.** The order reads `CompanionOrderFacts.MindControlled` and the curse's leave the same fact; a controlling spell
  that is not the PC's (0x459040) is the host's answer.

## Tests

`FollowerRulesTests` (Simulation, invented critters): the rule's case order, non-critter targets, complaints and their sides,
the four line ranges, tell followers, the party filter, the taunt kind and its two draws, FO17, the near-death crossing and FO16,
the activity test. `CompanionCombatOrderTests` (Session): the attack's lock and lifted stop in one revision per follower, no
target and the party filter, FO9 with an ignored event, a complaint with −5 in combat mode, back off, curse in both settings
(FO7) and an extraordinary leader, one AI pass per reached follower and before the next follower's order, the host's part of each
order before its pass (ARC-WORLD-117), an origin complaint,
a target in combat mode and a door through the order, a failed step that still lifts the stop, refused facts, and
`ApplyFollowerRule`'s writes. `AreaAiFollowerTests` (TerrainViewer, ARC-WORLD-119): a free companion is run as any leaderless NPC
(the routine, not told of the hero's fight, a victim's reaction, an attack through its own walk), the curse through the host ends
the follower's run, runs the attack event before its pass and is seen once, a leave outside an order ends only the leaver's run at
the next pump, and a join keeps it.

```sh
"$DOTNET" test tests/Arcanum.Simulation.Tests --no-build --filter "FullyQualifiedName~FollowerRules"
"$DOTNET" test tests/Arcanum.Session.Tests --no-build --filter "FullyQualifiedName~CompanionCombatOrder|FullyQualifiedName~CompanionOrderState"
```

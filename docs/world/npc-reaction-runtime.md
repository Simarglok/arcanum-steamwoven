# NPC reaction runtime

Status: **Implemented bounded component**, ARC-WORLD-026 (2026-10-17). Slices 1 and 2 of the
[reaction research](../research/npc-reaction-reputation-evidence.md#proposed-implementation-slices-project-policy-not-accepted)
(ARC-RES-028): the reaction rules as pure functions in `Arcanum.Simulation.Reaction`, and the NPCs' reaction memory
toward the PC as a domain of the [game session](game-session-runtime.md) (snapshot schema 10). The AREA viewer's dialog
reaction now lives there instead of the per-speaker `reaction.pc` map change. Slice 3, ARC-WORLD-043 (2026-10-21), binds
it to the dialog: the `ME` condition, the insult and the conversation slot opened and closed by every AREA talk
([Dialog binding](#dialog-binding-arc-world-043)). Requirements:
[FR-SOCIAL-001](../requirements/story-social-ai.md#fr-social-001), with
[FR-DIALOG-001, FR-DIALOG-002 and FR-DIALOG-004](../requirements/story-social-ai.md#fr-dialog-001) for the binding.
Reputations (FR-SOCIAL-002), fate points and the AI consumers are not part of it ([Not ready](#not-ready)).

## Evidence

- The original's functions, read first as in ARC-RES-028: the reaction and effect functions, the hit branch of the AI
  attack handler (0x4A9650) and the internal quest state setter (0x4C4E60).
- The retail 1.0.7.4 `Arcanum.exe` (SHA-256 `9bd80511…78d52`), read statically by two independent readers per the
  [original executable check](../research/original-executable-check.md); verdicts in the
  [verification ledger](../research/original-verification-ledger.md#reaction-runtime-arc-world-026). Every function below
  behaves as that first reading describes except one: the effective reaction passes the **PC**, not the NPC, to the
  `reaction` effect lookup (the reaction getter 0x4C0D00 → 0x4EAA80, ledger R1 effects), so both rule modes use the
  PC's effects. The original has no switch that lets charm bypass the PC's adjustment effects (A4): the delta always
  goes through them (ledger R2).
- The race table (0x5B684C) holds 88 values, read from the executable (ledger R1 term).
- Network-only preludes in the original (the reaction adjustment 0x4C0DE0 and the quest setter return early on a network client) do not
  apply to single player and are not modelled.

## Rules (`Arcanum.Simulation.Reaction`)

| API | Behaviour |
| --- | --- |
| `ReactionRules.Effective(stored, npc, pc, query)` | 0 when the PC is on the NPC's enemy list; otherwise `stored` + race term (`RaceReaction`, row NPC race, column PC race) + the PC's reaction modifier (both skipped for an aloof or monstrous NPC; in `ReactionQuery.Hostility` the two together are floored at −49) + the reputation sum, then the PC's `reaction` effects, then at least 50 when this PC mind-controls the NPC. No clamp. |
| `ReactionRules.Category(value)` | ≤ 0 hatred, ≤ 20 dislike, ≤ 40 suspicious, ≤ 60 neutral, ≤ 80 courteous, ≤ 100 amiable, above love; enum order love … hatred as in the source. |
| `ReactionRules.Adjust(delta, npc, pc, ledByPc)` | Skips a delta of 0, an NPC this PC mind-controls, and a negative delta while the PC has extraordinary charisma and leads the NPC (`ReactionSkip`). Otherwise the delta passes through the PC's bad (negative) or good (positive) adjustment effects; the result is added even when it is 0. `LeaderCheck` marks a negative change of a follower of this PC. |
| `ReactionRules.DeltaTo(target, effective)` | "Set to *v*" is the delta `v − effective`, then `Adjust`. |
| `ReactionRules.ConversationStart(stored, force)` | 100 when the stored value is below 100 and the "force good reaction" fate point is spent. |
| `ReactionRules.PcHit(stored, npc, pc, ai, ledByPc, deliberate, profile)` | Follower of the attacker: deliberate hit ± AI parameter 9 (A2, below), accidental nothing; the follow check runs either way. Any other NPC: accidental −10; deliberate lowers it to parameter 10 when its hostility-variant reaction is above it. |
| `ReactionRules.ApplyEffects(value, effects)` | Percent, multiply, divide, add, minimum, maximum, as the character stats owner aggregates stat effects. The source uses −1 as "no minimum/maximum", so a written `min -1` or `max -1` is ignored there; Steamwoven applies it, like the stats owner (latent: no reaction effect of the data is known to use it). |
| `ReactionRuleProfile` | Rule-mode profile: `Steamwoven`, `Original`, `All`, `FromName`; switch `FollowerHitRaisesReaction`. |

Inputs are explicit records. `ReactionNpcFacts`: reaction base, race, aloof, monstrous, PC on the enemy list, mind
controlled by the PC. `ReactionPcFacts`: race (one of the eight PC races), reaction modifier, reputation sum,
`reaction`, good and bad adjustment effects, extraordinary charisma. `ReactionHitFacts`: AI parameters 9 and 10.
Magnitudes are bounded to ±1,000,000 and effect lists to 64 entries (project policy); a divisor of 0 and a PC race
outside the table (LM12) are invalid in both modes, as the original would fault or read out of bounds.

## Session domain (`Arcanum.Session`)

Per NPC entity, `SessionReaction(Entity, Remembered, Conversation)`: the remembered slot (present exactly when the NPC
has met the PC) and the conversation slot (present exactly while a conversation is open). The stored value is the
conversation slot, else the remembered slot, else the NPC's base from its facts. An NPC without a record has neither.
Single PC: the source's ten-slot memory with eviction of the slot closest to 50 (R3) is not needed and not built.

| API | Behaviour |
| --- | --- |
| `QueryHeroReactionFacts()` | The hero's race, effective reaction modifier and extraordinary charisma (≥ 20); reputation 0 (no reputation owner); the reaction, good and bad reaction effects of its [durable effects](durable-effects-runtime.md) (ARC-WORLD-029; its innate effects keep only modelled stats, so the gnome's bad-reaction adjustment is still missing). `null` without a hero. |
| `QueryReaction(entity, npc, pc, query)` | `SessionReactionView`: effective value, category, stored value, met before (remembered slot only, so false during the first conversation), in conversation, recorded. |
| `QueryReactionRecord(entity)` / `QueryReactions()` | The record or `null`; a detached copy of the domain. |
| `AdjustReaction(version, entity, npc, pc, delta)` | `Adjust` with "led by the PC" taken from the session party (following or waiting). The new value goes to the conversation slot while one is open, else to the remembered slot. |
| `SetReaction(version, entity, npc, pc, target)` | `DeltaTo` against the current effective value, then as `AdjustReaction`. |
| `ReactToPcHit(version, entity, npc, pc, ai, deliberate)` | `PcHit` under `Rules.Reaction`, read at the call; `FollowCheck` asks the caller to re-run the follow check (leaving the party is its next step). |
| `BeginReactionConversation(version, entity, npc)` / `EndReactionConversation(version, entity, npc)` | Copy the stored value into the conversation slot (raised to 100 below 100 when the hero's "force good reaction" fate is armed, which it then uses; ARC-WORLD-054); move it to the remembered slot and clear the conversation slot. As in the source, an end without a begin also remembers the PC. |
| `ForgetReaction(version, entity)` | Clears the remembered slot; an open conversation keeps its slot. |

Every mutation takes the expected version and refuses in this order: entity (`InvalidReactionTarget`), facts
(`InvalidReactionFacts`, also AI parameters of a hit beyond the bounds), version (`StaleVersion`), the requested delta or
target and the new stored value (`InvalidReactionAmount` beyond ±1,000,000), then NoChange (a skip or an unchanged record,
with the `Adjustment` still reported), the record budget (`GameSessionLimits.MaxReactionRecords`, policy 8192 since ARC-DATA-029, `BudgetExceeded`) and the revision
capacity. One revision per changed record; a record back at the baseline is removed. A hit's `FollowCheck` is reported
whenever the hit was decided, also when its reaction write is then refused, because the source runs the follow check
whatever the write does.

## Rule modes

| Ledger ID | Original | Steamwoven | Switch |
| --- | --- | --- | --- |
| Reputation A2 | A deliberate hit on a follower adds parameter 9 | lowers it by parameter 9 (owner decision 2026-10-17) | `ReactionRuleProfile.FollowerHitRaisesReaction` |

The Steamwoven drop is an ordinary negative change, so the negative rules of `Adjust` apply to it as if the original had
subtracted: a leader with extraordinary charisma is immune (no change at all), any other follower gets the "check
leader" mark, which the Original rise never sets; the follow check runs in both modes.
| R1 effect owner | the PC's `reaction` effects | the same (the original, ledger R1 effects; no switch) | none |
| LM12 race table | unchecked index | refused in both (no out-of-bounds) | none |

The state has one schema for both modes, so `SelectRules` changes only the next hit decision.

## Snapshot schema 10

`GameSessionSnapshot.Reactions` (`SessionReactionState`): the records in ordinal entity order. JSON, between `life` and
`events`:

```json
"reactions":{"records":[{"entity":"oid:a","remembered":null,"conversation":-10},
                        {"entity":"oid:b","remembered":65,"conversation":null}]}
```

The codec refuses unsorted or repeated entities and a record with both slots `null` (`NotCanonical`), a slot beyond
±1,000,000 (`OutOfRange`) and more than 8192 records (`BudgetExceeded`); `Restore` applies the caller's budget.
**Migration decision: none.** Schema 9 and older are refused. The viewer's `reaction.pc` map changes of schema 9 saves
are therefore never read; a quick save of schema 9 loads as "written by an incompatible version".

## Dialog binding (ARC-WORLD-043)

Read in the retail executable by two readers ([ledger DR1–DR7](../research/original-verification-ledger.md#dialog-reaction-binding-arc-world-043)):

| Original | Steamwoven |
| --- | --- |
| The dialog start (0x412FD0) opens the NPC's reaction conversation before the first NPC line and action; every exit of the dialog (exit, return to a script line, a final line, an insult that ends it, a failed window, leaving dialog mode) ends it (0x413280 → 0x4C10A0), before the caller's script continues. | `AreaConversationHost` calls `BeginReactionConversation` (which reads the hero's armed fate itself, ARC-WORLD-054) right before `DialogConversation.Start` of every started talk, also one a resumed script opens, and `EndReaction()` closes it; the viewer calls it first on every close of the panel (the player's close, a reply that ends the talk, a frontier or failure, teardown, reset and map changes), before a resumed script runs. A slot a missed end left open is closed before the next talk begins. A refused begin is the start status `ReactionRejected`; a refused end is `ReactionFailure` and a notice. A rebound host (quick load) starts with no slot open: the panel takes every key during a talk. |
| `ME 0` / `ME 1` (0x4150D0) ask "met before" (0x4C0C40): the remembered slots only, so false during the first talk and true after it ends; any other operand passes untested. | The conversation's optional `IDialogMetBeforePort`; `AreaConversationState` answers it from the session record (`SessionReaction.MetBefore`). Operands other than 0 and 1 stop the conversation at an `UnsupportedOperation` frontier (never reinterpreted). |
| A Player reply of exactly `i:` (any case) is an insult: after the reply's action, the reaction changes by −10 through the PC's adjustment effects (0x4C0DE0), then at an effective reaction of 20 or below the NPC ends the talk with a generated line, without a script continuation (0x4191E0); above 20 the reply's own transition follows. An action that returns early (read for FL, which moves the dialog to its final line) skips the insult. | The conversation offers it as a generated `i:` option with its own transition; picking it runs the action, then a `ReactionCommand` of −10 through the world port (the viewer's `AdjustReaction`, into the open conversation slot), then reads the effective reaction; at most 20 it ends as `DialogEndKind.Insulted`. The phrase and the NPC's generated reply are not loaded. An insult whose action ends with JO, UW, FL or ET is shown blocked (`UnsupportedInteraction`): FL as read, JO, UW and ET as a precaution (their return was not read). The viewer's world port reports a change the PC's skips reduce to nothing (mind control, an extraordinary-charisma leader) as applied, so the insult's receipt then says `Applied` with no change. |
| "Force good reaction" is fate 1: spending a fate point arms it, undo refunds it while unused, and the next talk whose stored value is below 100 consumes it and starts at 100 (O4, DR6). | ARC-WORLD-054: `BeginReactionConversation` reads the hero's armed fate and uses it in the same revision ([fate points](fate-points-runtime.md)); no fate window arms it yet. |

The AI's checks of the dialog start and pick (the NPC refuses to speak, or turns hostile and ends the talk with a
generated line) are not modelled.

## AREA viewer

`AreaConversationState` reads and writes the speaker's reaction through the session domain: `RE t` gates read
`Reaction(speaker)` (the effective value), unsigned `RE v` is `SetReaction`, `RE ±d` is `AdjustReaction`, an accepted
quest raises the reaction to at least 41 with `SetReaction` and a completed one adds 10 with `AdjustReaction`; the
follow consent of the session party reads the same effective value. The facts are diagnostic: each speaker is a human,
not aloof NPC whose base is the profile's initial reaction (50; Virgil's measured base 60 of ARC-RES-026 is still not
used), and the PC is the session hero when there is one, else a human with reaction modifier 0. Since ARC-WORLD-043 each
talk opens the speaker's conversation slot and its end moves it to the remembered slot, so changes during a talk land in
the conversation slot, `ME` reads the remembered slot and the insult goes through `AdjustReaction`
([Dialog binding](#dialog-binding-arc-world-043)).

## Tests

- `Arcanum.Simulation.Tests/Reaction/ReactionRulesTests`: the race table (cells and the sum of all 88), each term of the
  effective value varied alone, aloof and monstrous, enemy list, mind-control floor after the PC's effects, the
  hostility floor, category boundaries, every skip of `Adjust` and the effect sign, set-to and the fate point, the hit
  rules in both profiles, profile names, invalid facts, effect order with several minimums, maximums and percents.
- `Arcanum.Session.Tests/ReactionStateTests`: base without a record, remembered after a change (also a change reduced to
  0), set-to as a delta under PC effects and from the standard (not hostility) value, the conversation slot through begin,
  a second begin, change, forget and end, the fate point, the party's leader rules (following and waiting), a follower hit
  under both modes switched between calls, the Steamwoven drop under an extraordinary-charisma leader and at the bound
  (follow check kept), a hit on another NPC, the hero's facts, typed refusals in their order and stale versions, the record
  budget, canonical text and restore, decode and restore refusals. `RuleModeTests` checks the profile in `GameRules`.
- `Arcanum.TerrainViewer.Tests/AreaConversationTests`: the dialog reaction is a session reaction record, not a map change.
- `Arcanum.Scripting.Tests/Dialog/DialogReactionBindingTests` (ARC-WORLD-043): `ME` through its port in condition order, the
  missing port, refused operands and a throwing port; the insult as a generated option with its transition, after the
  action, ending at 20 and continuing at 21, reading the value the owner's effects left, unknown reaction, a refused change,
  no reaction port, a transfer tail, a prefixed marker and the effect budget.
- `Arcanum.TerrainViewer.Tests/AreaReactionConversationTests` (ARC-WORLD-043): through the host with synthetic SCR and DLG
  content, `ME 0` holds in the first talk and `ME 1` in the second, the slot is open during a talk and remembered after
  `EndReaction`; insults lower the slot 50 → 40 → 30 → 20 and the last ends the talk; a missed end is closed before the next
  talk.

## Not ready

- Reputations (slice 4): the PC's list, `rules\gamerep.mes`, the reaction sum, faction membership, greetings. The
  sum is an input, 0 from the hero.
- The fate window that arms "force good reaction" (the fate itself is ARC-WORLD-054), the AI's speak refusal and hostile end at the dialog start and pick, the
  insult phrase and the NPC's generated replies (generated text), `ME` operands other than 0 and 1.
- The PC's `reaction`, good and bad adjustment effects of real data (the hero keeps none), enemy lists,
  mind control, charm (A4, decision D2 open), barter prices, gifts, hover colour, AI consumers (KOS at parameter 10, the
  follower loyalty pass that reads the leader check and the follow check after a hit).
- The source's ten-slot memory for several PCs.

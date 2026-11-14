# Party state and follow consent runtime

Status: **Implemented bounded component**, ARC-WORLD-013 (slices 1 and 2 of the
[companion research](../research/companion-follow-evidence.md#proposed-implementation-slices-project-policy-not-accepted),
ARC-RES-024). Godot-free C# in `Arcanum.Simulation` (the follow check) and `Arcanum.Session`
(party state, snapshot schema 4, the dialog party port), plus `WA` / `UW 0 node` admission in
the [conversation runtime](../scripting/dialog-conversation-runtime.md). It covers the state
and consent part of [FR-SOCIAL-003](../requirements/story-social-ai.md#fr-social-003) and
[FR-SOCIAL-005](../requirements/story-social-ai.md#fr-social-005).

**NOT READY:** since ARC-WORLD-016 the AREA viewer passes `SessionDialogPartyPort` for Virgil and he
follows the PC ([companion follow runtime](companion-follow-runtime.md)); other speakers keep the
diagnostic `follow.pc` key. Since ARC-WORLD-045 the session has the original's wait timer and follower loyalty
([below](#wait-timer-and-loyalty-arc-world-045)) and since ARC-WORLD-046 the out-of-combat orders
([companion orders](companion-orders-runtime.md)), whose wait order schedules the timer; the dialog `WA` action is not admitted,
NPC leaders other than the PC exist since ARC-WORLD-1840 ([below](#npc-leaders-arc-world-1840)), and there is no generated line text (lines are named by their message range). Since ARC-DATA-033 the follow check and the max-followers stat
are checked in the retail executable ([ledger AP3, AP4](../research/ledger/npc-ai.md#ai-parameters-and-follow-consent-arc-data-033));
the rest of the party state's numbers and order are earlier source claims, **NOT_VERIFIED** in the executable.

## Follow check (`Arcanum.Simulation.Party`)

`FollowConsentRules.Evaluate(FollowConsentInput)` is a pure function over explicit, effective
inputs. The first matching step wins (the executable's 0x4AD950, ledger AP3):

| Step | Input | Result |
| --- | --- | --- |
| 1 | `MindControlledByPc` (the caller resolves the source's controller lookup) | `Accepted` |
| 2 | `PcPersuasion == Master` | go to step 8 (steps 3–7 are skipped; earlier readings had an unconditional `Accepted`) |
| 3 | `OtherLeaderCharisma` (another leader, PC or NPC) ≥ `PcCharisma` | `AlreadyInGroup` |
| 4 | `ForcedFollower` | `Accepted` |
| 5 | `Reaction` ≤ AI field 5 | `Dislike` |
| 6 | PC alignment above the NPC's: PC − NPC > field 6; otherwise NPC − PC > field 7 | `TooGood` / `TooBad` |
| 7 | NPC level ≥ PC level + field 8 | `LowLevel` |
| 8 | unless `IgnoreCharismaLimit`: `MaxFollowers` = 0 / `NonForcedFollowers` ≥ `MaxFollowers` | `NotAllowed` / `LimitReached` |

`FollowThresholds` carries AI fields 5..8. Differences and the level sum are computed in 32 bits
and wrap, as in the executable. `FollowConsentRules.MaxFollowers(charisma, persuasion)` is
the effective charisma / 4, +1 at Expert or above, clamped to 1..7 (ledger AP4);
a caller with stat effects passes its own effective value. With a hero the session supplies the PC's persuasion training, effective charisma, level and
max followers itself, the hero's effects on that stat applied before the clamp (ARC-SKILLS-002, [hero skills](hero-skills-runtime.md));
the caller's `PartyConsentFacts` values stand only without a hero (the alignment always stays the caller's). With a clamped value `NotAllowed`
is unreachable, as ARC-RES-026 found; the evaluator keeps it for an explicit 0 (a negative
value, as in the executable, falls through to `LimitReached`).
`MayLeaveUnforced(leaderCharisma, mindControlled)` is the C6 leave guard (refused at charisma
≥ 20 or under mind control). Invalid enum values, a negative follower count or a null input
throw (programming errors).

## Party state (`GameSessionOwner`)

The session owns the PC's party: the ordered follower array and one record per NPC that is not
at the baseline (free, no flags). An NPC is named by its session entity id (the same
1..128-character code as map-change entities; the viewer uses `oid:<object id SHA-256>`).

`SessionCompanion(Entity, Standing, Forced, SpreadOut, Jilted, Loyalty)`: `Standing` is `Free`,
`Following` (in the array) or `Waiting` (outside the array, PC kept as leader). `LedByPc` is
true while following **or** waiting, as the source's `FO` / SCR `FOLLOWING` read only the
leader. `Forced` exists only while following, `Jilted` only while free; `SpreadOut` is kept in
every standing, and so are the loyalty marks (`Loyalty`, ARC-WORLD-045).

| Entry | Contract (refusal order: input, version, standing, consent, NoChange, budget, revision; `LeaveParty` reports a free NPC as NoChange before its leave guard) |
| --- | --- |
| `QueryCompanion(entity)` / `QueryParty()` | One record (an unrecorded NPC reads as free with no flags; an invalid id throws) / detached `SessionPartyState`: `Followers` in array order, `Others` (waiting, or free with a flag) in ordinal entity order, and `NonForcedFollowers`. |
| `JoinParty(expected, entity, facts, force)` | Dialog `JO force node` ([C1](../research/companion-follow-evidence.md#findings), [C5](../research/companion-follow-evidence.md#findings)): the follow check with the session's own inputs added (the NPC's forced flag, the PC's non-forced follower count, `IgnoreCharismaLimit = force`). A refusal is `ConsentRefused` with the reason in `PartyMutationResult.Consent`. Success leaves any standing and appends the NPC at the end of the array (never a duplicate), forced when `force`, jilted cleared. The last follower joining again unchanged is `NoChange`. As read in the source, an NPC already in the array counts toward the limit itself, and an unforced join makes a forced follower unforced. |
| `ForceJoinParty(expected, entity)` | Script follow, mind control, the follow spell ([C17](../research/companion-follow-evidence.md#findings)): no check, always forced; also brings a waiting NPC back. |
| `UnwaitParty(expected, entity, facts, force)` | Dialog `UW force node` ([C11](../research/companion-follow-evidence.md#findings)): only an NPC with the WAIT bit (`NotWaiting` otherwise; a free NPC with the wait flag is `NoChange`, it has no leader, CF9); the check as for join (a waiting NPC is never forced and is outside the array). A refusal keeps it waiting; success re-appends it and clears the bit (also for a follower that carries the flag). |
| `WaitParty(expected, entity, timer?)` | The source's dialog `WA` action, wait order, script wait: a follower leaves the array, forced cleared (wait is a forced leave), PC kept. An NPC whose WAIT bit is set (waiting, or the wait flag) is `NoChange`. A free NPC (ARC-SCRIPT-1136, CF8) gets the wait flag and stays free and outside the array; its timer is the one of its wait index with no leader read ([below](#wait-timer-and-loyalty-arc-world-045)); no room for a new record is `BudgetExceeded`. With `PartyWaitFacts` the wait timer is scheduled ([below](#wait-timer-and-loyalty-arc-world-045)). |
| `LeaveParty(expected, entity, PartyLeave)` | [C6](../research/companion-follow-evidence.md#findings): a follower or waiting NPC becomes free, forced cleared, `SpreadOut` kept, `Jilted` when `Jilt`. An unforced leave under an extraordinary-charisma leader or mind control is `LeaveRefused`; a free NPC is `NoChange` (with the wait flag too). A waiting NPC's wait timer is removed; a follower that carries the wait flag loses it. |
| `SetSpreadOut(expected, entity, value)` | Dialog `SO` / `SC` in any standing. |

Facts naming another leader (`OtherLeaderCharisma`) for an NPC the PC already leads contradict the
session and are `InvalidConsentFacts`; a null facts or leave argument throws. Every change adds one
session revision; refusals change nothing. `GameSessionLimits.MaxCompanions`
(policy maximum 64) bounds the records; a new record over it is `BudgetExceeded`, while an
existing record may still change. New failures: `InvalidCompanion`, `InvalidConsentFacts`,
`ConsentRefused`, `LeaveRefused`, `NotInParty`, `NotWaiting`.

Not stored yet: death and destruction (the original's kill notification and object destruction end the wait; here a dead or
destroyed waiting NPC keeps its `Waiting` record, and its companion budget slot, with no timer: death removes the timer, a pass drops
the timer of an NPC the host no longer reports),
and the NPC flags an area record may carry at T0 (a new session starts with no records; Virgil's bits are clear, ARC-RES-026).

## NPC leaders (ARC-WORLD-1840)

Owner decision 2026-11-13 (#1322): an NPC leads followers as in the original, where every critter has a leader handle (field 0x119,
read by 0x45DE00) and a follower array (field 0xF1), the join 0x45DE90 and the leave 0x45DFC0 / 0x45E040 work under any leader and
0x45DDA0 walks the leaders up to a player character. The session keeps the hero's party as above and beside it the followers of
leaders that are not the hero (`SessionNpcFollower`: entity, leader, forced; saved in `party.npcLed`, schema 102, grouped by leader in
ordinal order, each leader's array in order, at most `MaxNpcFollowers` = 256). A critter has one leader: its hero record stands free
while an NPC leads it.

- `QueryLeader(critter)` (0x45DE00): the hero for a follower or waiting NPC of the hero, the NPC leader of a follower of an NPC, else
  null; the hero has none. `QueryPcLeader(critter)` (0x45DDA0): the hero when the chain of leaders reaches it, else null (it starts at
  the leader, so the hero itself answers null). `QueryFollowers(leader)`: the hero's array or an NPC leader's.
- `ForceJoinLeader(entity, leader)` (the forced join 0x45DE90 with force 1): the hero's is `ForceJoinParty`. Under an NPC the joiner
  first leaves the leader it has (a follower or waiting NPC of the hero leaves the party with the leave's side effects: concealment,
  the no-attack order, the Tempus Fugit flag, a wait timer), then it is appended, forced, jilted cleared, the leader's concealment
  taken (0x45EE30) and the leader's Tempus Fugit flag copied (TF3; an NPC leader's from the session's written flags only). Every join
  of the hero's party leaves an NPC leader first. The hero follows no one, a critter does not lead itself, and a join that would close
  a cycle of leaders is refused (`InvalidCompanion`): the original's chain query would not end on one. The unforced joins the session
  models are the hero's (dialog, unwait), so every NPC-led follower is forced (the script follow, 0x447220, and mind control join with
  force 1).
- `LeaveParty` leaves an NPC leader too: out of its array, the Tempus Fugit flag cleared, the concealment ended; an unforced leave
  reads `PartyLeave.LeaderCharisma` and `MindControlled` (the session does not hold an NPC's charisma). No jilted mark is written.
- A destroyed critter (a created one) leaves its NPC leader (0x45E180), and the followers it led lose their leader without a leave of
  their own (they keep their flags). The original leaves their handle naming the destroyed object (OM17); that the handle then
  answers no leader is the session's reading, not read in the executable.
- The AI host answers `Leader` and `PcLeader` from these and `Followers` for any critter; its call sites read the getter the
  original's function reads (the trap alarm 0x4AA0D0, peace 0x4AA300 and its interrupt 0x44E050, the trap
  avoidance of a drop 0x4BBFE0 and the trap observer read 0x45DDA0; the routine, the follow goal and the healer's free services read
  0x45DE00), and a leave of an NPC leader (also the first half of a join under another leader) ends the follower's run as the hero's
  leave does (`ObserveLeaves`). The catch-up (0x4AD4D0) reads the chain too (ARC-WORLD-2116): an NPC of the map led through another
  leader, more than 30 tiles from the hero, is moved onto the hero's cell (the chain's PC, never its immediate leader, which is not
  tested) by the host's mover placement, as a companion is by the follower source ([NLD1, NLD2](../research/ledger/npc-ai.md#npc-leaders-in-the-ai-host-built-arc-world-2116)).
- A follower of an NPC is never concealed: 0x45EE30 gives the joiner its leader's concealment, but the session's concealment is the
  hero party's (toggle, end on an attack, save), so a companion's summon does not sneak with it.
- The leave of an NPC leader clears the follower's WAIT bit and removes its timer (the forced unwait 0x4AA8C0 inside 0x45DFC0).
- Scripts (ARC-SCRIPT-1842): make follow (21, 0x45DE90 force 1) joins under any leader in the heartbeat and dying sets
  (`AreaUseScriptHost.ApplyMakeFollow`: the follower an NPC standing on the shown map, the leader the PC or such an NPC; the PC as the
  follower does nothing, the original's NPC test) and in the thrown item's script (the leader the PC or another object of the shown
  map); disband (22, 0x45DFC0 force 1) leaves an NPC leader through `LeaveParty`. The scripts' leader query (condition 18, 0x45DE00)
  answers the session's leader in every script host: the PC, or an NPC leader named on the critter's map. A join refused as a cycle
  leaves the run going on. A free Create Undead whose caster is a looped NPC (the dying scripts 30118 and 30127) reaches the cast host
  with that caster, whose join makes the risen critter its follower (ARC-MAGIC-1841).
  Condition 11 (bad associates, 0x45F460) walks the NPC leader's follower array as it walks the hero's party; the schematic
  window of a talk takes the first PC up the speaker's chain (0x45DDA0) as its viewer, so an NPC led by an NPC alone opens none.
  A script destroy (68) of an NPC that leads or follows an NPC is refused whole: it writes a map change only and never reaches the
  session's unlink.

- The session's "PC leader" sites read the chain (ARC-WORLD-1864, each read in the executable): the danger enemy add 0x4AF930 (the same-side skip: the entry is a PC or its chain's PC equals the owner's), the team add 0x4AF8C0 (head := the
  offender's chain PC or the offender; for the PC its follower tree 0x4413E0 → 0x441260: the direct followers in array order, then each follower's own followers), peace 0x4AA300 (head := the other's chain PC, else the other itself; the head's
  follower tree forgets the NPC, a PC or NPC head alike), the turn-based order's PC block 0x4B7EB0, and the kill award 0x45DA20 and the kill log 0x4F5330 that follows it (the credited PC is the killer or its chain's PC). The hit-damage award in 0x4B4390
  pays only a PC attacker (no leader read), as before. `GameSessionOwner.PcLeads` is the chain test, `LedByPc` the direct one.

- A waiting follower of an NPC (ARC-WORLD-2116, 0x4AA7A0, NLD3–NLD6): the wait keeps the leader and takes the follower out of the leader's
  array. The session writes it as the follower's NPC link kept with the wait flag on its free record (no schema step; saved as such, the
  timer with it): `QueryLeader` and `QueryPcLeader` still answer the leader and the chain, `QueryFollowers` leaves it out, the AI host's tell
  followers and catch-up gate (the waiting NPC stays put) read it so. The timer is the one of the wait index alone, as a leaderless NPC's (the original reads the leader's charisma and
  Persuasion; every caller's wait facts are the hero's, and an NPC leader's own stats are #2117) and runs out as the hero's does, but the reaction change is written
  only toward a player character: the follower is freed, jilted and leaderless, no reaction change. A forced unwait (the death's) joins the
  leader again, appended at the end of its array, forced, the leader's Tempus Fugit flag copied; an unforced unwait needs the leader's stats
  and changes nothing. A join under an NPC leader of a free NPC that carries the wait flag clears the flag (the original keeps it, in the
  array; the flag is this encoding's wait), and the hero's join of a follower waiting under an NPC clears it (the join's leave runs the forced unwait). A save written before ARC-WORLD-2116 that
  holds an NPC link on a record with the wait flag (such a join kept the flag) now reads as a wait under that leader: the follower is out of the array and its leaderless timer jilts it and frees it.
- The AI host tells followers of any leader (0x4A9B80, ARC-WORLD-2116, NLD7): the leader's array the host runs, each through the follower
  rule; an NPC leader's follower complains in words alone (the −5 reaction is written only toward a player character).

**NOT READY:** the concealment of NPC-led followers; an NPC leader's own charisma
for the danger setter's extraordinary-charisma test (0x4AABE0 reads the leader's; the host reads only the hero's) and for its
followers' unforced leave; the followers of a hero's companion travelling with the hero (the move list's followers of followers, GR14:
the viewer carries only the hero's companions); the dying follower's leave (0x45DA20 leaves the leader and keeps the handle; the
session keeps the hero's dead followers in the array as before, and an NPC's too). An unforced unwait of a follower waiting under an NPC leader, and the dialog's wait facts for one (the leader's stats). Tails: #2117 (charisma), #2118 (travel, concealment), #2119 (join check, barter, the remaining direct sites).

## Wait timer and loyalty (ARC-WORLD-045)

The original's wait timer and follower loyalty, read in the retail executable
([companion orders and loyalty](../research/companion-orders-loyalty-evidence.md#2-loyalty-while-following-q2-companion-c13-o4),
rows OL17–OL26; registry rows 0x4AA7A0, 0x4AA990, 0x4AAA30, 0x4AC380, 0x4ADB50 at F2). Universal: nothing names Virgil.
Pure rules in `Arcanum.Simulation.Party.CompanionLoyaltyRules`; state and entry points in `GameSessionOwner`
(`GameSessionLoyalty.cs`).

**Wait flag of an NPC with no leader (ARC-SCRIPT-1136, ledger CF8 to CF10).** The original's `wa` (0x4AA7A0) tests only the NPC's own guards, so an NPC nobody leads still gets the WAIT bit (0x8): `SessionCompanion.WaitFlag`, saved with the party record
(schema 96; the record of a free NPC with the flag is listed in `Others`). `WaitBit` (a waiting standing or the flag) is what the dialog `wa` condition and the AI's waiting test read. The timer of such an NPC
is queued by its wait index alone (the leader's charisma and Persuasion are not read; the mind-control test stands) and does nothing when it runs out (0x4AA990 acts only for a non-null leader). A join
keeps the flag (0x45DE90 never writes the bit): the follower then stands in the array with it set, its loyalty step returns early (0x4AC380), and only an unwait (`UW`, the forced unwait of a death)
or a leave clears it. A save holds a timer only for a waiting NPC or a free one with the flag. Not modelled and the open point: [CF10](../research/ledger/scripts.md#dialog-companion-state-and-actions-arc-dev-195).

**Wait timer.** `WaitParty(expected, entity, PartyWaitFacts(WaitIndex, LeaderPersuasion, LeaderCharisma, MindControlled))`
schedules the event `party.wait` ([event queue](game-event-queue-runtime.md#session-wiring-arc-world-024)) on the actor's map, naming the NPC,
when the leader's charisma is below 20 and the NPC is not mind controlled. The duration is the wait field's (NPC field 300) entry
of 0, 15 min, 2 h, 1 day, 7 days, 30 days for 0..5; any Persuasion training of the leader adds one step before the cut (index 0
becomes 15 min, index 5 none); 6 or more (Virgil's 6) and a negative index schedule none. A duration of 0 is stored as 1 ms: the
NPC is jilted at the next pass. With a hero its training and effective charisma replace the caller's (here and when the timer
fires). No room for the timer (`SessionEventKinds.MaxWaitTimers` = 64, or the queue) is `EventBudgetExceeded` and changes nothing.

**Expiry** (OL24). In one revision: the reaction change `2 × (charisma − 20)` (the hero's effective charisma, else the one saved
in the event: without a hero the leader is not re-read, a documented deviation from OL23) through the ordinary reaction rules while the PC still leads the NPC, so a negative change arms its marks (OL22); then
the NPC is free and jilted, forced cleared, its marks kept. The reaction facts are the host's: a pass host that implements
`ISessionPartyEventWorld` gives the NPC's (and without a hero the PC's) `Reaction…Facts`; without them, or when the stored
value would leave its bounds or no reaction record is left, the NPC is jilted without the change and `SessionWaitFiring.ReactionFailure`
says why. No line, animation or script. A pass result lists the firings in `SessionEventPassResult.Waits`.

**Cancel** (OL25). Every way out of waiting removes the NPC's timer wherever it is parked: `UnwaitParty`, `JoinParty`,
`ForceJoinParty`, `LeaveParty` (forced or not) and an NPC's death (`Life` with `Died`). A refused unwait or leave keeps it. The death is a forced unwait (ARC-AI-1312, kill notification 0x4AA1B0 → 0x4AA8C0): a waiting companion that dies also joins its leader again as a forced follower, appended at the end of the array, JILTED cleared, the leader's Tempus Fugit flag copied; a follower or free NPC that dies keeps its record.
Destruction is the host's: a pass drops an event whose NPC the host no longer reports (the record stays `Waiting`, above).

**Maps** (OL26). The event stays with the waiting NPC's map (only followers' events travel with the actor), so it is parked while
the PC is away and fires, overdue, at the first pass after the PC returns.

**Loyalty marks** (OL17, OL22). `CompanionLoyalty` holds check-leader and three test bits. A reaction change that asks for the leader
check (`ReactionAdjustment.LeaderCheck`: a negative change that applies while the PC leads the NPC, waiting included) arms
check-leader and the reaction bit in the same revision, also when the PC's bad-reaction effects reduce it to 0.
`RecordPcAlignmentChange(expected, previous, current)` (the caller owns the alignment; the AREA viewer's `AL`) arms each follower
in the array, not a waiting one, with check-leader and the too-good bit (rose) or the too-bad bit (fell). The marks stay in every
standing until a loyalty step clears them.

**Loyalty step** (OL18–OL21). `RunLoyaltyStep(expected, entity, PartyConsentFacts, actionGoalRunning)` is the loyalty part of a
follower's non-combat AI pass. A waiting or free NPC gets no step (`Step` null, `NoChange`). While `actionGoalRunning` (the
host's answer: a goal of the action class runs, a follow goal included; decision D5, both settings as the original) the step is
`GoalRunning`. Otherwise the follow check runs with the charisma limit ignored; a refusal makes the follower leave unforced
(free, jilted, marks kept) unless the leave is refused (charisma 20+ or mind control, the hero's charisma with a hero), and the
host shows the refusal line range of its reason (`LoyaltyStepResult.RefusalLines`: too good 1600, too evil 1700, dislike 1800,
already grouped 2600, limit 2700, not allowed 2800, low level 2900, 100 lines each). A passed check or a refused leave goes on:
an armed follower clears all its marks and runs the check-leader test (no warning under mind control by the leader, Persuasion
Master or for a forced follower; else reaction bit and reaction ≤ AI field 5 + 20 → low reaction; else by which alignment is
higher, too-good bit and gap > field 6 − 100 or too-bad bit and gap > field 7 − 100), whose warning range the host shows
(`WarningLines`: 5200, 5300, 5400). No reaction change; one revision when the record changed.

**Snapshot schema 27.** Every party record carries `"loyalty"`, the marks' bit sum 0..15 (check-leader 1, reaction 2, too good 4,
too bad 8); decode refuses another value (`OutOfRange`) or a missing one (`MissingField`), restore an undefined one
(`InvalidCompanion`). A free record with only marks is kept (OL22). A pending `party.wait` event is admitted only for an NPC the
saved party records waiting, set no later than the saved clock, with a duration of the table exactly before its deadline, one per
NPC (`InvalidEvents`). Schema 26 is refused, not migrated.

Not modelled: the network-client branch of the expiry; leading NPCs other than the PC; the loyalty step's host part (leaving combat
mode, the follow request, the item search) and the lines' text and speech.

## Snapshot schema 4

`GameSessionSnapshot.Party` is encoded between `story` and `lighting`:

```json
"party":{"followers":[{"entity":"golem","forced":true,"spreadOut":true,"loyalty":0},
                      {"entity":"ari","forced":false,"spreadOut":false,"loyalty":3}],
         "others":[{"entity":"sogg","standing":"Waiting","spreadOut":false,"jilted":false,"loyalty":0},
                   {"entity":"virgil","standing":"Free","spreadOut":false,"jilted":true,"loyalty":0}]}
```

(`loyalty` since schema 27, ARC-WORLD-045; `waitFlag`, a boolean at the end of every record, since schema 96, ARC-SCRIPT-1136: the WAIT bit of a free or following NPC; never set with the `Waiting` standing, which is that bit itself; the schema 95 upgrade adds `false`.)

Decoding keeps the follower order and refuses an entity listed twice anywhere or unsorted
`others` (`NotCanonical`), a standing other than `Waiting`/`Free`, a waiting record marked
jilted or flagged `waitFlag`, or a free record without a flag (`OutOfRange`), and more than 64 records
(`BudgetExceeded`). `Restore` re-checks the same rules (`InvalidCompanion`, `InvalidSnapshot`)
and the caller's lower limit (`BudgetExceeded`). **Migration decision: none.** Schemas 1..3 are
`UnsupportedSchema`; a schema-3 quick save loads as "written by an incompatible version" with
the running session kept, and the next F7 overwrites it. The viewer's only party trace in
schema 3 was the diagnostic `follow.pc` key, which schema 4 still carries unchanged.

## Dialog seam

`IDialogPartyPort` (Scripting) answers `WA` (`IsWaiting`), `JO` and `UW` (`DialogJoinResult`).
When a host passes it in `DialogConversationPorts.Party`, the conversation admits `WA 0/1`
conditions and a trailing `UW 0 node`, and sends `JO` there instead of the world port; without
it nothing changes (see the [conversation grammar](../scripting/dialog-conversation-runtime.md#conversation-grammar-v1)).

`SessionDialogPartyPort` (Session) implements it over a `GameSessionOwner`: the host maps each
NPC participant to its entity id and supplies `PartyConsentFacts` (the PC's and NPC's effective
values and the NPC's AI fields). `JO 0` / `UW 0` run `JoinParty` / `UnwaitParty` unforced; a
refusal returns `Refused` (the conversation's `JoinRefused` frontier) and keeps the reason in
`LastRefusal` for the host's refusal line. An unmapped NPC, another PC or missing facts is
`Rejected` with no effect. The port reads the session at every call; a host that replaces the
owner (quick load) builds a new port.

For Virgil the inputs are in [ARC-RES-026](../research/virgil-follow-consent.md#2-virgil-at-t0-d):
AI row 5 (−1000, 2001, 2001, 30), level 1, alignment 1, reaction base 60. With the diagnostic PC
(charisma 8, level 1, max followers 2) and no other leader, only the follower limit can refuse him; with `--hero` the session
uses the hero's persuasion, charisma, level, alignment and max followers instead (ARC-SKILLS-002, ARC-WORLD-067, below).

## The session hero as the PC side (ARC-WORLD-067)

With a session hero (`--hero`) every party rule reads the hero as the PC side, in every viewer path (the AREA exterior and cave,
map mode, the carried companions of ARC-WORLD-048 that the one host lists since ARC-DEV-081) and for every NPC: `GameSessionOwner.QueryHeroPartySide` gives its Persuasion
training (the hero's skills, ARC-SKILLS-002), its effective charisma (the stat with its effects), its level, its stored alignment
and the max followers they give (ledger AP4: charisma / 4, +1 from expert Persuasion, then the hero's effects on that stat, clamped
to 1..7). The session itself uses it in the follow check (join, unwait, the orders' follow, the loyalty step), the leave guard
(an unforced leave is refused for a hero of charisma 20 or more), the wait timer's Persuasion step and charisma cut, and the
timeout's reaction change, whatever the caller's facts say; the viewer builds its facts from it too
(`AreaCompanionRoster.Facts`/`WaitFacts` with the party side), so the logs and the HUD name what the rules read. A dialog's `AL`
writes the hero's stored alignment (`ChangeHeroAlignment`: clamped to −1000..1000, the followers armed as OL17, one revision; the
hero's alignment is already saved, no schema change), so the character sheet follows it too. Without a hero the labelled
diagnostic profile stands in (charisma 8, level 1, no Persuasion, the conversation host's alignment, starting at 0). The HUD's
companion line names the side: `PC side: hero (charisma …, level …, alignment …, persuasion …, max followers …)` or
`PC side: diagnostic profile (…)`.

## Tests

- `tests/Arcanum.Simulation.Tests/Party/FollowConsentRulesTests.cs`: every refusal at its
  boundary on a made-up AI row, step order, the ignored limit, overflow, Virgil's row, the
  max-followers clamp, the leave guard and invalid input.
- `tests/Arcanum.Session.Tests/PartyStateTests.cs`: join order and rejoin without a duplicate,
  refusal reasons, forced joins, waiting and unwait, leave refusal and jilting, spread out,
  invalid/stale/over-budget requests, the literal schema-4 party text and restore, and decode
  refusals. `SessionDialogPartyPortTests.cs` drives a synthetic DLG through the real
  conversation runtime with the session port.
- `tests/Arcanum.Scripting.Tests/Dialog/DialogPartyTests.cs`: `WA` gating, `UW` continuation,
  refusal and rejection, `JO` through the party port, no admission without the port,
  unadmitted forms and a throwing port.
- `tests/Arcanum.Simulation.Tests/Party/CompanionLoyaltyRulesTests.cs` and
  `tests/Arcanum.Session.Tests/CompanionLoyaltyStateTests.cs` (ARC-WORLD-045): every duration and the Persuasion step, no timer at
  charisma 20, mind control or index 6+, expiry effects and order, every cancel path, a timer parked across maps, arming by reaction
  and alignment, the step's refusal and jilt, a refused leave, the warning, the goal gate, the snapshot round trip and the decode and
  restore refusals; `tests/Arcanum.TerrainViewer.Tests/AreaCompanionLoyaltyTests.cs` the viewer's AI tick, follow-goal gate, wait
  expiry through the event pump and `AL` arming.
- `tests/Arcanum.TerrainViewer.Tests/VirgilPartyTests.cs` (opt-in `RealData`,
  `ARC_AREA_CONVERSATION_GAME_ROOT`): Virgil's original DLG with the session party port. The
  first talk walks the same 13 nodes and joins him through the session; an `AL` +5 after the
  join arms his check-leader and "too good" loyalty marks (OL17), which stay through waiting
  and save/load; waiting, a save/load round trip and node 385 then offer only the `WA 1` / `UW 0 518` replies, which bring him back;
  following, node 385 offers its `WA 0` replies. A PC already leading two followers is refused
  at node 210 with `LimitReached`.
  `Original_virgil_joins_the_session_hero_as_the_pc_side` (ARC-WORLD-067): with the installed pre-made `human-male` hero the
  party side is the hero's (effective charisma, level, alignment), the conversation reads its alignment, the original first talk
  joins Virgil and its `AL` raises the hero's alignment.
- `tests/Arcanum.Session.Tests/HeroPartySideTests.cs` and `tests/Arcanum.TerrainViewer.Tests/AreaHeroPartySideTests.cs`
  (ARC-WORLD-067): the hero's party side, the follow limit from charisma and expert Persuasion, the hero's level and alignment in
  the check, the leave guard at charisma 20, the wait timer's Persuasion step and charisma cut, the alignment change, the viewer's
  facts and label with and without a hero.

```sh
"$DOTNET" test tests/Arcanum.Simulation.Tests --no-build --filter "FullyQualifiedName~FollowConsent"
"$DOTNET" test tests/Arcanum.Session.Tests --no-build
ARC_AREA_CONVERSATION_GAME_ROOT=<installation> "$DOTNET" test tests/Arcanum.TerrainViewer.Tests --no-build --filter "FullyQualifiedName~VirgilParty"
```

## Next

The viewer wiring and follower movement (slice 3) are ARC-WORLD-016
([companion follow runtime](companion-follow-runtime.md)); the cross-map group (slice 4) is ARC-WORLD-017
([map change](companion-follow-runtime.md#map-change-arc-world-017)); the wait timer and loyalty are ARC-WORLD-045 (above); the orders are ARC-WORLD-046
([companion orders](companion-orders-runtime.md)). Next: the dialog `WA` action and the combat orders (NPC AI stage A7).

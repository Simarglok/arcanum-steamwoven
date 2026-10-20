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
diagnostic `follow.pc` key. There is no map-change group, orders, loyalty test, wait timer or death
handling, and no generated refusal line text. Since ARC-DATA-033 the follow check and the max-followers stat
are checked in the retail executable ([ledger AP3, AP4](../research/original-verification-ledger.md#ai-parameters-and-follow-consent-arc-data-033));
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
max followers itself (ARC-SKILLS-002, [hero skills](hero-skills-runtime.md)); the caller's `PartyConsentFacts` values stand
only without a hero. With a clamped value `NotAllowed`
is unreachable, as ARC-RES-026 found; the evaluator keeps it for an explicit 0 (a negative
value, as in the executable, falls through to `LimitReached`).
`MayLeaveUnforced(leaderCharisma, mindControlled)` is the C6 leave guard (refused at charisma
≥ 20 or under mind control). Invalid enum values, a negative follower count or a null input
throw (programming errors).

## Party state (`GameSessionOwner`)

The session owns the PC's party: the ordered follower array and one record per NPC that is not
at the baseline (free, no flags). An NPC is named by its session entity id (the same
1..128-character code as map-change entities; the viewer uses `oid:<object id SHA-256>`).

`SessionCompanion(Entity, Standing, Forced, SpreadOut, Jilted)`: `Standing` is `Free`,
`Following` (in the array) or `Waiting` (outside the array, PC kept as leader). `LedByPc` is
true while following **or** waiting, as the source's `FO` / SCR `FOLLOWING` read only the
leader. `Forced` exists only while following, `Jilted` only while free; `SpreadOut` is kept in
every standing.

| Entry | Contract (refusal order: input, version, standing, consent, NoChange, budget, revision; `LeaveParty` reports a free NPC as NoChange before its leave guard) |
| --- | --- |
| `QueryCompanion(entity)` / `QueryParty()` | One record (an unrecorded NPC reads as free with no flags; an invalid id throws) / detached `SessionPartyState`: `Followers` in array order, `Others` (waiting, or free with a flag) in ordinal entity order, and `NonForcedFollowers`. |
| `JoinParty(expected, entity, facts, force)` | Dialog `JO force node` ([C1](../research/companion-follow-evidence.md#findings), [C5](../research/companion-follow-evidence.md#findings)): the follow check with the session's own inputs added (the NPC's forced flag, the PC's non-forced follower count, `IgnoreCharismaLimit = force`). A refusal is `ConsentRefused` with the reason in `PartyMutationResult.Consent`. Success leaves any standing and appends the NPC at the end of the array (never a duplicate), forced when `force`, jilted cleared. The last follower joining again unchanged is `NoChange`. As read in the source, an NPC already in the array counts toward the limit itself, and an unforced join makes a forced follower unforced. |
| `ForceJoinParty(expected, entity)` | Script follow, mind control, the follow spell ([C17](../research/companion-follow-evidence.md#findings)): no check, always forced; also brings a waiting NPC back. |
| `UnwaitParty(expected, entity, facts, force)` | Dialog `UW force node` ([C11](../research/companion-follow-evidence.md#findings)): only a waiting NPC (`NotWaiting` otherwise); the check as for join (a waiting NPC is never forced and is outside the array). A refusal keeps it waiting; success re-appends it. |
| `WaitParty(expected, entity)` | The source's dialog `WA` action (not admitted by the conversation runtime yet), wait order, script wait: a follower leaves the array, forced cleared (wait is a forced leave), PC kept. Already waiting is `NoChange`; a free NPC is `NotInParty`. |
| `LeaveParty(expected, entity, PartyLeave)` | [C6](../research/companion-follow-evidence.md#findings): a follower or waiting NPC becomes free, forced cleared, `SpreadOut` kept, `Jilted` when `Jilt`. An unforced leave under an extraordinary-charisma leader or mind control is `LeaveRefused`; a free NPC is `NoChange`. |
| `SetSpreadOut(expected, entity, value)` | Dialog `SO` / `SC` in any standing. |

Facts naming another leader (`OtherLeaderCharisma`) for an NPC the PC already leads contradict the
session and are `InvalidConsentFacts`; a null facts or leave argument throws. Every change adds one
session revision; refusals change nothing. `GameSessionLimits.MaxCompanions`
(policy maximum 64) bounds the records; a new record over it is `BudgetExceeded`, while an
existing record may still change. New failures: `InvalidCompanion`, `InvalidConsentFacts`,
`ConsentRefused`, `LeaveRefused`, `NotInParty`, `NotWaiting`.

Not stored yet (later slices, after O3/O4 of the research): the check-leader flag, the three
loyalty-test bits, the wait timer, death, NPC leaders other than the PC, and the NPC flags an
area record may carry at T0 (a new session starts with no records; Virgil's bits are clear, ARC-RES-026).

## Snapshot schema 4

`GameSessionSnapshot.Party` is encoded between `story` and `lighting`:

```json
"party":{"followers":[{"entity":"golem","forced":true,"spreadOut":true},
                      {"entity":"ari","forced":false,"spreadOut":false}],
         "others":[{"entity":"sogg","standing":"Waiting","spreadOut":false,"jilted":false},
                   {"entity":"virgil","standing":"Free","spreadOut":false,"jilted":true}]}
```

Decoding keeps the follower order and refuses an entity listed twice anywhere or unsorted
`others` (`NotCanonical`), a standing other than `Waiting`/`Free`, a waiting record marked
jilted or a free record without a flag (`OutOfRange`), and more than 64 records
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
(charisma 8, level 1, max followers 2) and no other leader, only the follower limit can refuse him.

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
- `tests/Arcanum.TerrainViewer.Tests/VirgilPartyTests.cs` (opt-in `RealData`,
  `ARC_AREA_CONVERSATION_GAME_ROOT`): Virgil's original DLG with the session party port. The
  first talk walks the same 13 nodes and joins him through the session; waiting, a save/load
  round trip and node 385 then offer only the `WA 1` / `UW 0 518` replies, which bring him back;
  following, node 385 offers its `WA 0` replies. A PC already leading two followers is refused
  at node 210 with `LimitReached`.

```sh
"$DOTNET" test tests/Arcanum.Simulation.Tests --no-build --filter "FullyQualifiedName~FollowConsent"
"$DOTNET" test tests/Arcanum.Session.Tests --no-build
ARC_AREA_CONVERSATION_GAME_ROOT=<installation> "$DOTNET" test tests/Arcanum.TerrainViewer.Tests --no-build --filter "FullyQualifiedName~VirgilParty"
```

## Next

The viewer wiring and follower movement (slice 3) are ARC-WORLD-016
([companion follow runtime](companion-follow-runtime.md)); the cross-map group (slice 4) is ARC-WORLD-017
([map change](companion-follow-runtime.md#map-change-arc-world-017)). Next: orders, loyalty and the wait timer (slice 5).

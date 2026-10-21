# PC reputation runtime

Status: **Implemented bounded component**, ARC-WORLD-044 (2026-10-21). Slice 4 of the
[reaction research](../research/npc-reaction-reputation-evidence.md#proposed-implementation-slices-project-policy-not-accepted)
(ARC-RES-028): the module's reputation table (Data), the reputation rules (Simulation), the PC's reputation list in the
[game session](game-session-runtime.md) (snapshot schema 30), the dialog `RP` condition and action, the script condition
"has reputation" and the reputation term of the [NPC reaction](npc-reaction-runtime.md). Requirements:
[FR-SOCIAL-002](../requirements/story-social-ai.md#fr-social-002) and
[FR-SOCIAL-001](../requirements/story-social-ai.md#fr-social-001). The reputation greeting is ARC-WORLD-058
([dialog greeting](../scripting/dialog-greeting.md)); the logbook's reputation tab and the AI consumers are not part of it
([Not ready](#not-ready)).

## Evidence

The retail 1.0.7.4 `Arcanum.exe`, read statically by two independent readers that saw only the questions
([ledger "Reputations"](../research/original-verification-ledger.md#reputations-arc-world-044), RP1–RP8, RM1, A9); they agree
on every row. Local data: the opt-in census below (counts only).

## Table (`Arcanum.Data.Reputations.ReputationTable`)

`ReputationTable.Load(ContentStack)` reads, from each file's winner at module load:

1. `rules\gamerep.mes`, entries 1000..1999 by number (an absent entry is skipped; other numbers are never read). Each
   entry is split on commas, empty pieces skipped. The first piece holds three faction numbers, each further piece one
   effect: adjustment, origin, faction (0 = any), up to five; more are ignored. The numbers are scanned as the original does:
   `atoi`, then before each further number skip spaces (the space character only) and a run of digits, `+` and `-`; so a
   trailing space reads a last 0 and a tab stalls the scan (a number is read again). An effect with fewer than three numbers
   keeps 0 for the rest and counts. An entry whose first piece ends before its third faction number stops the table there:
   it is kept with the numbers already read (`Complete` false) and `Aborted` names it. Every effect keeps the origin and
   faction it is written with, signed or not (decision D1).
2. `mes\gamereplog.mes`, the logbook names (`Name(number)`, numbers ≥ 1000), only when the table did not stop.
3. `mes\game_rp_npc_{m2m,m2f,f2f,f2m}.mes`, the greeting files, only when the names file exists; a missing one is skipped.
   The [dialog greeting](../scripting/dialog-greeting.md) reads them (ARC-WORLD-058).

No `gamerep.mes` gives an empty table without names and greetings. A file that cannot be read or parsed as MES refuses the
whole load (`ReadFailed`, `ParseFailed`), and so does a number beyond 32 bits (`OutOfRange`; the original would wrap):
project policy. The original never clears its 1000 records between module loads; one table here is one load.

Installed original (`DAT_ROOT`, opt-in `ReputationTableTests` RealData): 76 reputations, 93 effects, none stopped, 5 with a
membership faction, all 76 named, all four greeting files (ARC-RES-028 L1, L2).

## Rules (`Arcanum.Simulation.Reputation`)

| API | Behaviour |
| --- | --- |
| `ReputationCatalog.Create(definitions)` | The session's view of a table; null for a number outside 1000..1999, a repeat, more than three factions or five effects, or an adjustment beyond ±1,000,000 (project bounds). |
| `ReputationRules.ReactionSum(held, catalog, origin, faction)` | 0x4C1AC0: every effect of every held reputation whose origin is 0 or the NPC's and whose faction is 0 or the NPC's adds its adjustment; all stack. A number without a record adds nothing. |
| `ReputationRules.GrantsFaction(held, catalog, faction)` | 0x4C21E0: never for faction 0; else when one of a held reputation's three factions is it. |
| `ReputationRules.FactionSame(a, b, pcReputations, catalog)` | 0x45D110 over `FactionMember(Id, IsPc, Faction, Leader, PcLeader)`: two PCs never; a PC and a non-PC when the non-PC's PC leader is that PC or its non-zero faction is granted by the PC's reputations; two non-PCs when they share a non-null PC leader, one leads the other, they share a non-null leader, or they have the same non-zero faction. Single PC: the PC side reads the session PC's list. The AI slices consume this one function. |
| `ReputationRules.OriginSame(a, b)` | 0x45D320: equal and non-zero. |
| `ReputationRules.GreetingCandidates(held, catalog, origin, faction, category)` | 0x4C1F80 (ARC-WORLD-058): in held order, each held reputation with an effect in the NPC's scope (origin and faction 0 or the NPC's) whose adjustment is negative for a category above neutral or positive for one below; once each. The [greeting](../scripting/dialog-greeting.md) picks one uniformly. |

## Session domain (`Arcanum.Session`)

The story domain holds the PC's reputations in the order gained, each with the game time it was first gained
(`SessionReputation`), as the original's two PC fields do. The table is data: `GameSessionDescriptor.Reputations` and
`GameSessionRestoreOptions.Reputations` (never saved; without one a held reputation has no effect).

| API | Behaviour |
| --- | --- |
| `HeroHasReputation(n)` | 0x4C1CB0: whether the PC holds `n`; a number outside 1000..1999 is never held. |
| `AddReputation(version, n)` | 0x4C1D20: appends `n` at the current game time; held already: NoChange (the first time stays). |
| `RemoveReputation(version, n)` | 0x4C1E10: removes `n`, the later entries move up with their times; not held: NoChange. |
| `QueryReputations()` | 0x4C1C30: the list in order with times (the logbook's order). |
| `QueryReputationReaction(origin, faction)`, `HeroGrantedFaction(faction)`, `HeldReputations()` | The rules above over the session's list and table. |
| `QueryHeroReactionFacts(origin, faction)` | The hero's reaction facts with the reputation sum for that NPC; the parameterless form is origin 0, faction 0. A sum beyond ±1,000,000 makes every reaction query and change refuse the facts. |

Refusals in order: the number (`InvalidReputation` outside 1000..1999), the version (`StaleVersion`), then NoChange, then
the revision capacity. One revision per change. No record budget beyond the range: the list holds at most 1000 numbers.

## Rule modes and anomalies

| Ledger ID | Original | Steamwoven | Switch |
| --- | --- | --- | --- |
| A5 | any number is stored (and read outside the table) | only 1000..1999 are admitted; a dialog `RP` action outside is skipped (`ReputationRangePolicy.Skip`, recorded as `Skipped`, the dialog goes on), a condition on it is "not held" | none: owner decision 2026-10-21 (option b), skip in both settings; `ReputationRangePolicy.Refuse` remains a host option, unused |
| RM1 | removal copies entry 2·i into i (original bug) | a correct shift in both settings | none: owner decision 2026-10-21 (option a) |
| A9 | the greeting pick returns the reputation's own number | the same (ARC-WORLD-058, ledger RG8) | reconstruction slip of the earlier reading |

## Snapshot schema 30

`story.reputations`: `[{"reputation":1001,"timestampMs":54000000}, …]` in the order gained. The codec refuses a number
outside 1000..1999 (`OutOfRange`), a repeated number or a time below the previous entry's (`NotCanonical`), more than 1000
entries; restore refuses a time after the saved clock (`InvalidSnapshot`). **Migration decision: none.** Schemas 1..29 are
refused, so a quick save of schema 29 loads as written by an incompatible version.

## Dialog and scripts

- `IDialogReputationPort` (Scripting) admits `RP n` as a Player condition and as an action of either side (without the port
  `RP` stays unadmitted): the condition holds n for n > 0 and lacks −n for n ≤ 0; the action adds n or removes −n (0x4150D0,
  0x415BA0). `SessionDialogReputationPort` binds it to the session; a reputation already held or not held is applied with no
  change, and the receipt of a skipped number is `DialogCommandAttemptOutcome.Skipped`.
- The AREA viewer passes the port in every conversation; the SCR condition 33 (`AreaUseScriptHost.HasReputation`) is
  `IsPc(obj) && HeroHasReputation(n)` (SG5).
- The viewer's dialog reaction reads the PC's facts per speaker with the speaker's origin and faction (its NPC fields
  0x123 and 0x124, slots 80 and 81; `AreaConversationHost.Scope`), so a held reputation's effects reach the right NPCs; the
  diagnostic PC without a hero reads the same sum. The viewer loads the module's table when it creates the area session
  (`AreaReputations.Load`, `ARC-WORLD-044 reputations status=… detail=…` in the log); a quick load keeps it.

## Tests

- `Arcanum.Data.Tests/Reputations/ReputationTableTests`: entries, signed scopes, the number scan (empty pieces, trailing
  space, tab stall, non-numeric separator), the stop and its skips, names and greetings gating, module precedence, parse
  refusals, the 32-bit bound; opt-in census.
- `Arcanum.Simulation.Tests/Reputation/ReputationRulesTests`: the sum by origin and faction, stacking, grants, every row of
  faction-same in both orders, origin-same, catalogue bounds.
- `Arcanum.Session.Tests/ReputationStateTests`: add, remove and has with times, refusals, the hero's facts with the sum,
  snapshot round trip, decode and restore refusals, the dialog port's policies, the revision limit.
- `Arcanum.Scripting.Tests/Dialog/DialogReputationTests`: `RP` conditions and actions, skips, refusals, the missing port.
- `Arcanum.TerrainViewer.Tests/AreaReputationTests` and `AreaUseScriptHostTests`: the speaker scope and the script condition.

## Not ready

- The logbook's reputation tab (FR-SOCIAL-008; a later logbook task, owner decision 2026-10-21), the logbook button flash and
  network messages. (Greetings: ARC-WORLD-058.)
- The AI consumers of `FactionSame`, `GrantsFaction` and `OriginSame` (kill-on-sight, protect, upset, last hitter): their
  slices call these functions.
- NPC origins set by the dialog `OR` action and factions by scripts or summons (the viewer reads the loaded fields only).
- Script reputation actions (add, remove) are not admitted by the SCR dispatcher.

# NPC dialog greeting

Status: **Implemented bounded component**, ARC-WORLD-058 (2026-10-21). R12 of the
[reaction study](../research/npc-reaction-reputation-evidence.md) (ARC-RES-028): an NPC node whose text is `g:` now says the
original's generated greeting in the [conversation runtime](dialog-conversation-runtime.md). It covers the order of the greeting
kinds and their slot overrides, the class, race and NPC-line generators over the
[generated dialog files](../formats/generated-dialog-files.md) (ARC-DATA-042), the NPC's own override lines, the reputation
greeting and the reaction line by category and met before. Universal: any module, any NPC.
Requirements: [FR-SOCIAL-002](../requirements/story-social-ai.md#fr-social-002) (the reputation greeting) and
[FR-DIALOG-003](../requirements/story-social-ai.md#fr-dialog-003) (generated NPC lines). The NPC markers `i:`, `m:` and `r:`
stay frontiers ([Not ready](#not-ready)).

## Evidence

The retail 1.0.7.4 `Arcanum.exe`, read statically in two rounds by two independent readers each
([ledger "Reputation greetings"](../research/ledger/social.md#reputation-greetings-arc-world-058), RG1–RG12,
anomaly GD-A1; the files: [GD1–GD5](../research/ledger/data.md#generated-dialog-files-arc-data-042)). Local data:
the opt-in census below (counts only).

## Components

| Assembly | API | Original |
| --- | --- | --- |
| Arcanum.Simulation | `ReputationRules.GreetingCandidates(held, catalog, origin, faction, category)` | 0x4C1F80: the reputations a greeting may name (the pick is the caller's) |
| Arcanum.Scripting | `IDialogGreetingPort` (`DialogConversationPorts.Greeting`), `DialogGreetingFacts`, `DialogGreetingStates`, `DialogGreetingSources`, `DialogLineOrigin` (`DialogNpcLine.Origin`), `DialogRuleProfile` | 0x416840, 0x419260, 0x418480, 0x4185F0, 0x418780, 0x418870, 0x4C2100 |
| Arcanum.Session | `GameRules.Dialog` (the dialog profile of the mode, composed as every domain profile; no state, no schema) | — |
| AREA viewer Core | `AreaGreeting` (the port over the session and one speaker), `AreaGreetingTexts` (the texts of the module) | — |

## Behaviour

A `g:` node (the marker is case-insensitive, read from the record text whatever the PC's gender) runs its actions first,
then greets (RG1). Without a greeting port the node stays an `UnsupportedGeneratedText` frontier, as before.

**Slots** (RG2). The text after `g:` sets up to ten slots: `N 0` switches slot N off, `N M` continues at node M when slot N's
state holds, `N fl M` says node M as a floating line and ends. The parse is the original's (`DialogGreeting.ParseSlots`),
including its quirks: only a lowercase `fl` floats, an `f` without `l` ends the parse, an index without a value takes the
next entry's index as its value. An index of 10 or more is ignored (the original writes past its arrays; no-crash rule, both
settings). The nodes the slots name are planned with the `g:` node, before its actions: a missing node or a reply fails it
(`MissingTarget`, `WrongTargetRole`), a node that is itself a greeting or cannot run here stops it at a frontier, even when
its slot would never fire (the original follows a slot only when it fires; project policy, no installed slot text is
affected). The step's turn and effect budgets reserve one more turn and the largest slot node's action.

**Order** (RG4). The first kind whose state holds speaks:

| Slot | State (`DialogGreetingStates`) | Line |
| --- | --- | --- |
| 0 | `NpcDown`: the NPC's hit points are 0 or below | NPC lines 1500..1599 |
| — | `NpcSurrendered` (no slot can change it) | NPC lines 3000..3099, override 12029 |
| 1 | `BadAssociates`: a summoned party member that is no familiar or animal | class 18000, ends the talk unless the NPC's social class is 2 |
| 2 | `PcInvisible` | class 22000, ends as slot 1 |
| 3 | `PcElementalBody` | class 19000, ends as slot 1 |
| 4 | `PcPolymorphed` | class 20000, ends as slot 1 |
| 6 | `PcShrunk` | race 6000 |
| 7 | `PcArtCodeZero`: the code of the PC's art is 0 | class 16000 |
| 8 | `PcArtCodeSeven`: that code is 7 | class 17000 |
| 9 | always | off: no line (the line keeps this conversation's previous one, RG11); a node: RG3 |
| — | the reputation greeting (below) | |
| — | the reaction line | love 11000 met / 8000 not met; amiable, courteous 12000 / 9000; neutral 13000 / 10000 (class groups); suspicious, dislike 4000 / 2000; hatred 5000 / 3000 (race groups) |

Slot 5 is never read. A slot whose state holds and that names a node continues there as an ordinary node (its actions, its
line, its replies) or, floating, runs that node's actions, says its line and ends the talk (`DialogEndKind.NpcFinalLine`). A
line that does not end the talk keeps the `g:` node's replies.

**Generators** (RG6). A class line reads `gd_cls_*` from the group plus 50 × the NPC's social class, a dumb NPC
`gd_cls_dumb_*` from the group; a race line `gd_rce_*` from the group plus 50 × k (k = 0 for the PC's own race, else the PC's
race + 1), a dumb NPC `gd_rce_dumb_*`; an NPC line `gd_npc_*` in its range. The file is the NPC's gender to the PC's
(`GeneratedDialogFiles.NpcLineFile`). Each first tries the NPC's **override line** (RG7: 9999 + group / 1000, 10999 + group / 1000,
11999 + first / 100) in the DLG of the script at the NPC's point 31; an override line is raw (no name substitution) and has
its record's voice. A generated line has `@pcname@` and `@npcname@` substituted and any other `@…@` token dropped, and no voice.

**Line pick** (GD5, GD-A1; `DialogRuleProfile.GeneratedLinePickSpansGaps`, D8 switch, owner decision 2026-10-21, option a):

| Setting | Pick |
| --- | --- |
| Steamwoven (default) | uniformly among the entries stored in the range; none gives no line (`DialogLineOriginKind.Empty`) |
| Original | the range's first number plus a uniform roll from 0 to the run length less one (−1 or 0 without a first entry); a number the file lacks shows the project's text `[missing generated line N in mes\<file>.mes]` (`MissingLine`); the original shows its own static error message with the same number and path (RG9) |

A file the module lacks says nothing in both settings (the original exits the program).

**Reputation greeting** (RG8). A roll of 1..100, made even without reputations; at most 20, the candidates
(`ReputationRules.GreetingCandidates` over the PC's reputations, the NPC's origin and faction and its reaction category) and,
when there is one, a uniform pick among them. The line is the reputation's entry of `mes/game_rp_npc_{m2m,m2f,f2f,f2m}.mes` by
the NPC's gender then the PC's, raw and without voice. No candidate, a missing or an empty entry gives the reaction line.

**Rolls.** The port's rolls (`DialogGreetingRoll.Line`, `ReputationChance`, `ReputationPick`); a value outside its bounds or
a refused roll fails the conversation (`InvalidQueryResult`). The viewer rolls the session's labelled generator
(`dialog.greeting.line`, `dialog.greeting.reputation`, `dialog.greeting.pick`), so a restore replays the same greetings.

## AREA viewer

`AreaConversationHost` passes an `AreaGreeting` in every conversation once the module's texts load
(`AreaGreetingTexts.Load` over the script content's stack, at the host's first conversation); otherwise `g:` stays a frontier.

| Fact | Source |
| --- | --- |
| NPC gender, race, dumb | the NPC's stored base stats 26, 27 and 4 (`CombatFieldReader`); the original reads the effective stats (RG6), which differ only under an effect on them |
| NPC social class | its NPC field 0x128 |
| PC gender and race | the conversation's PC (the session hero, else the diagnostic profile) |
| category, met before | the speaker's effective reaction (`ReactionRules.Category`) and the session's met before |
| `NpcDown` | the session holds a death time for the NPC |
| other states | not modelled: they read as "not that state" (surrender, summons, the PC's spell flags and art code have no owner yet) |
| origin, faction | the NPC's fields 0x123 and 0x124 (`AreaConversationHost.Scope`) |
| override dialog | the DLG of the script at the NPC's point 31 (`ScriptContent.Dialog`, with the session's data-fix rules) |

The voice of an override line is its record's (`AreaConversationStart.VoiceIdentity`); generated and reputation lines are
silent.

## Census

Installed original (`DAT_ROOT`, opt-in `DialogGreetingRealDataTests`, counts only):

- 246 `g:` nodes in 215 DLGs; 13 carry slot texts, none switches slot 9 off, none has an index of 10 or more, no FL reply targets a `g:` node.
- 503 override-range records in 21 DLGs; none holds `@`.
- The four reputation greeting files hold the same 70 entries, each a reputation of the table; 27, 28, 28 and 27 are not empty.
- Every reaction-line block (social classes 0..10, PC races 0..7, the dumb files) holds an entry in all four gender pairs.
- Every installed `g:` node started with neutral, unmet facts: 57 say a generated reaction line and one continues at its
  slot-9 node in both settings; the rest stop at their own actions or replies under the census's minimal ports, none at the greeting.

AREA map census (`AreaMapConversationOriginalTests`, `DAT_ROOT`): the 35 conversations of the 34 interiors that stopped at a
`g:` greeting now greet; 19 offer replies (9 before) and 17 end (7 before); the other 25 stop later at an operation not admitted yet.

## Tests

- `Arcanum.Simulation.Tests/Reputation/ReputationRulesTests`: the candidates by scope, sign and category, held order, once each.
- `Arcanum.Scripting.Tests/Dialog/DialogGreetingTests`: the slot parse, every reaction cell with the offsets, the files by
  gender and dumbness, the gap pick in both settings, override lines, names, the order of the states with the end rule,
  jump and floating slots (slot 9 too), slot 9 off, the reputation greeting, the order of action and greeting, the slot
  nodes' turn and effect reservation, replies whose target is a greeting, failures and frontiers.
- `Arcanum.Scripting.Tests/Dialog/DialogGreetingRealDataTests`: the census above.
- `Arcanum.TerrainViewer.Tests/AreaGreetingTests`: the facts from the fields, the override script, candidates, rolls, rules,
  the text load and the voice of override and generated lines (the host's lazy load and its rebind are covered by the map census); `AreaMapConversationOriginalTests`: the map census.
- `Arcanum.Session.Tests/RuleModeTests`: the dialog profile of each mode.

## Not ready

- The NPC markers `i:` (the hostile refusal line), `m:` (money request) and `r:` (rumor) stay `UnsupportedGeneratedText` frontiers.
- The dialog start's hostile refusal (G_rce 1000 and end, 0x412FD0) and the floating greeting outside a conversation
  (0x4132A0, the viewer's `GenericGreeting`) are not built.
- The states without an owner (surrender, summoned associates, the PC's spell flags, the art code) read as not that state.
- Floating lines are shown in the conversation panel with the conversation's end, not as speech over the NPC.
- The PC option files (`gd_pc*`, `gd_cls_pc*`), the story files (`gd_sto_*`) and the broadcast lines (0x4133B0) have no consumer.
- The logbook's reputation tab (FR-SOCIAL-008) is a later logbook task (owner decision 2026-10-21: greetings first).

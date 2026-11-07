# Dialog condition codes outside the subset: plan (ARC-RES-091)

Status: **plan; C0 (census, ARC-RES-092), C1 (gold `$$`, ARC-DEV-179), C2 (quest log, ARC-DEV-180), C3 (combat `co`, ARC-DEV-190), C4 (story and area, ARC-DEV-191), C5 (PC stats, ARC-DEV-192), the SU10 slice (hostile check after a reply, ARC-DEV-193), C6a (item conditions `in ni`, ARC-DEV-194) and C6b (the non-negative `in` action, ARC-DEV-199) and C7a (`fo wt`, `wa lv sc so`, ARC-DEV-195) and C8 (`lc`, ARC-DEV-196) done 2026-11-07, C7b (`pa`, `nk`, ARC-DEV-227) and C9 to C10 not built yet.** Record written 2026-11-07 at the owner's request, after ARC-DEV-171 (`ru`, `rq`, bug B17)
showed that many installed dialogs still stop on a condition code the conversation runtime does not parse. The owner's decision: **the
remaining codes are to be implemented**, in slices, each checked against the retail 1.0.7.4 `Arcanum.exe`.

## What is known

- The original's condition interpreter (0x4150D0) matches each operation against a table of **37 condition codes** (0x5A06BC; ledger
  [DS1](ledger/skills.md), [DG1](ledger/scripts.md#dialog-script-stores-arc-dev-152)); the action interpreter (0x415BA0) against **35 action
  codes** (0x5A0750). An unknown code fails a condition and ends an action string (DS1).
- The conversation runtime
  ([grammar](../scripting/dialog-conversation-runtime.md#conversation-grammar-v1)) parses **30 condition codes**: `LF GF RE RA ME RU RQ` (the last two
  need the rumor port to run) and, behind an optional port, `RP` (reputation), `WA` (party), `PS HA SK TR` (skills), `GV PV PF` (script
  stores), `$$` (gold, C1), `QU QA QB` (quest log, C2) and `SS AR IA` (story and area, C4) and `CH PE LE AL MA TA NA` (PC stats, C5), and the actions `CO` (C3) and `SS` (C4). Any other
  condition code makes the field `UnsupportedOperation`: the reply is offered blocked, or, when a whole reply group is needed, the step stops
  at a frontier (the block kind `TargetUnsupported` when the target node's reply group holds one).
- The names the owner met in play are `ch`, `in`, `ia`, `ni`, `qa`, `qb`, `le`, `lc` and the money code `$$`. C0 reads them all
  ([DCC1–DCC11, DAC1–DAC12](ledger/scripts.md#dialog-condition-and-action-codes-arc-res-092)): `ch`/`le` are the PC's charisma and level, `in`/`ni` item holdings, `ia`
  the area of the PC's map, `qa`/`qb` the PC's quest state at least/at most a value, `lc` the NPC's dialog counter and `$$` the gold of the PC and its
  followers.
- The census before any slice
  ([skill gates](../scripting/dialog-conversation-runtime.md#skill-gates-arc-skills-004), first observation 2026-10-21, 709 DLG files):
  of 1,582 fields with a skill operation, 364 stopped at a code outside the subset (money `$$` 121, rumors 84, quests 47, and others).
  That counts only fields that also hold a skill operation, and the rumor share was closed by ARC-DEV-171 (RU1–RU4: 223 `ru`/`rq` fields in
  21 files, 20 still stop on damaged data). There is **no census of the other codes over all installed dialogs**.

## Slices

Each slice is its own task code, branch and PR. A slice adds codes to the grammar behind a typed port (as `RP`, `WA`, `GV`, `RU` were
added), records the original's behaviour in the ledger from the executable (prose only, no bytes or listings), updates the requirement
row and the table below, and keeps the unsupported rest a typed frontier. Original behaviour is followed in both rule modes unless the
original is a bug of the game (then the bug rule applies).

| Slice | Content | Needs |
| --- | --- | --- |
| **C0 census** | **Done (ARC-RES-092).** The 37 condition codes and 35 action codes are read in the executable ([ledger DCC1–DAC12](ledger/scripts.md#dialog-condition-and-action-codes-arc-res-092)); the opt-in `RealData` test `DialogCodeCensusRealDataTests` counts them over the 709 installed DLG files; the table below holds the result. | The owner's Mac and original data; Sonnet |
| **C1 gold** | **Done (ARC-DEV-179).** `$$` condition and action (2,857 fields stopped on it): the gold of the PC plus its direct followers (signed rule), and the payment `$$ −n` (the PC pays, followers pay the rest). Read in the executable ([ledger GM1–GM6](ledger/scripts.md#dialog-money-code-arc-dev-179)): the payment of the original has a bug (a payer holding less than the whole amount owed pays nothing yet is counted as having paid all it held), kept in the Original setting and fixed in Steamwoven (`DialogRuleProfile.ShortPayerPaysNothing`, approved by the owner on 2026-11-07). Owner: the session inventories (`SessionDialogGoldPort`). After it 7,574 fields (of 10,431) still hold a code outside the subset. | C0; ledger DCC5, DAC2 |
| **C2 quest log** | **Done (ARC-DEV-180).** `qu` (equals), `qa` (at least), `qb` (at most) conditions (1,653 fields freed): the PC's state of the quest (the getter 0x4C4CB0: the PC's record, 6 when it is marked botched, 0 for a quest never changed) against the second operand with signed compares, read in the executable ([ledger QL1–QL5](ledger/scripts.md#dialog-quest-log-conditions-arc-dev-180)); no difference between the rule settings. Owner: the PC quest log that the `QU` action writes (`SessionDialogQuestPort`, `IDialogQuestPort`). After it 5,921 fields (of 10,431) still hold a code outside the subset. | C0; ledger DCC4 |
| **C3 combat start** | **Done (ARC-DEV-190).** `co` action (1,325 fields freed): a flag only; after the whole action string the attack event runs once with the PC as attacker on the NPC (loudness 1, no flags), after a final line of the same string, and an unknown code, an empty string or a lone trailing character skips it. Read in the executable ([ledger CA1–CA5](ledger/scripts.md#dialog-combat-action-arc-dev-190)); no difference between the rule settings. Owner: the viewer's AI host (`IDialogCombatPort`, `AreaConversationHost.HeroAttack` over `AreaAiHost.OrderAttack`). Only a `co` may follow an `fl`; the reply step's hostile re-check after a string (SU10, CA3; 76 installed fields are a bare `co`) is the extra slice ARC-DEV-193 below. After it 4,596 fields (of 10,431) still hold a code outside the subset. | C0; ledger SU13, DAC1 |
| **C4 story and area** | **Done (ARC-DEV-191).** `ss` condition and action, `ar`, `ia` conditions (1,051 fields freed): the signed rule on the game's story state and the setter that keeps the larger value; the PC knows area n (n > 0) or does not know area −n; the PC's map is in area n (n > 0) or not in −n. Read in the executable ([ledger SV1–SV6](ledger/scripts.md#dialog-story-state-and-area-codes-arc-dev-191)); no difference between the rule settings. Owner: the session story state and known areas (`SessionDialogStoryPort`, `SessionDialogAreaPort`) and the shown map's area (`IDialogMapAreaPort`, map mode); the dialog `mm` action now writes the session's known areas. After it 3,545 fields (of 10,431) still hold a code outside the subset. | C0; ledger DCC7, DAC7 |
| **C5 PC stats** | **Done (ARC-DEV-192).** `ch pe le al ma ta na` conditions (1,026 fields freed): the signed rule on the PC's charisma, perception, level, alignment and aptitude, `ta` on the negated aptitude, `na` the alignment against the negated operand (the installed `na 799` / `na −800` pairs show the form is designed). Read in the executable ([ledger PQ1–PQ5](ledger/scripts.md#dialog-pc-stat-conditions-arc-dev-192)); no difference between the rule settings. Owner: the session hero's stats (`SessionDialogStatPort` over `QueryHeroScriptStat`); the diagnostic PC without a hero has none, so the codes stay unadmitted there; the getter's other effects are partial (ledger row 0x4B0490). After it 2,519 fields (of 10,431) still hold a code outside the subset. | C0; ledger DCC2, DCC3 |
| **SU10 hostile check** (extra, owner-approved 2026-11-07) | **Done (ARC-DEV-193).** The reply step (0x413130) checks, after every action string that returns 1 (no `fl jo uw et ce su ii ri` in it), whether the NPC is alive and kill-on-sight toward the PC; if so the NPC speaks a generated line (group 1000) and the talk ends before the insult and the transition (state 4). Read in the executable ([ledger HR1–HR4](ledger/scripts.md#dialog-hostile-check-after-a-reply-arc-dev-193)). Owner: the viewer's AI host (`IDialogHostilePort`, `AreaConversationHost.NpcHostile` over `AreaAiHost.HostileToHero`). Not built: the same check at the dialog start (DR2), the can-speak refusal of the pick, the text of the generated line. No code freed. | C3; ledger SU10, CA3 |
| **C6 items** | **C6a done (ARC-DEV-194):** `in`, `ni` conditions (1,056 fields freed): the PC or a follower holds an item by name for n ≥ 0, the NPC or its substitute container for n < 0, `ni` the negation; the item search counts the whole direct list, worn items included. Read in the executable ([ledger IH1–IH4](ledger/scripts.md#dialog-item-conditions-arc-dev-194)); no difference between the rule settings. Owner: `IDialogItemHoldPort` over the session's lists (`SessionDialogItemHoldPort`, `QueryHolderItems`) and the viewer's substitute container (`AreaInventoryHost.SubstituteContainer`). After it 1,463 fields (of 10,431) still hold a code outside the subset. **C6b done (ARC-DEV-199):** the non-negative `in` action (328 Player and 42 NPC action fields now parse whole): the PC (failing that, the first follower holding it) hands item n to the NPC (transfer 0x4617F0, then the holder is re-read; a transfer that did not end with the NPC puts it at the NPC's location for an NPC with a leader and destroys it otherwise, not built). Read again in the executable ([ledger TK1–TK4](ledger/scripts.md#dialog-item-hand-over-arc-dev-199)); no difference between the rule settings. Owner: `IDialogItemTakePort` over the hero's lists (`SessionDialogItemTakePort`, `GameSessionOwner.GiveItemToNpc`); an item only a follower holds is Rejected (the session has no move between NPC lists). | C0; ledger DCC6, DAC5 |
| **C7 party and NPC flags** | **C7a done (ARC-DEV-195):** `fo`, `wt` conditions and `wa`, `lv`, `sc`, `so` actions (226 fields freed; 1,237 of 10,431 still hold a code outside the subset): the PC leads the NPC, the NPC is jilted; the NPC waits, is forced out of the party, its spread-out flag is set or cleared. Read again in the executable ([ledger CF1–CF7](ledger/scripts.md#dialog-companion-state-and-actions-arc-dev-195)); no difference between the rule settings. Owner: `IDialogCompanionPort` over the session party (`SessionDialogCompanionPort`); `so` sets the flag but does not yet start the walk-away goal (CF4). **C7b open:** the condition `pa` (366 fields, 326 of them stop on it first: some follower whose spell flags lack the mind-control bit has the name field n; it needs the NPCs' name field, which the viewer does not carry) and the action `nk` (9 fields: the NPC is killed, 0x45D900). | C0; ledger DCC8, DAC4, DAC8, DAC9 |
| **C8 NPC counter** | **Done (ARC-DEV-196):** `lc` condition and action (486 fields freed; 751 of 10,431 still hold a code outside the subset): counter i (0..3) of the NPC's dialog attachment equals v, or takes the low eight bits of v. Read in the executable ([ledger KC1–KC4](ledger/scripts.md#dialog-npc-counters-arc-dev-196)); no difference between the rule settings. Owner: the viewer conversation state (`IDialogCounterPort`, the counters beside the local flags). 22 fields stay unparsed: the index 4, which the source wraps onto counter 0, and one missing operand. | C0; ledger DCC9, DAC6 |
| **C9 spell colleges** | `sc` condition (158 more). Owner: the PC college ranks. | C0; ledger DCC10 |
| **C10 tail** | `ce ii ri` (132), `xp` (38), `np` (21), `or` (4) actions. `ce ii ri` first need the reply UI's windows read (DAC11); `np` the queue rule (DAC10). | C0; ledger DAC9–DAC12 |
| **A-codes** | The census covers the action codes too and shows installed dialogs that stop on them; they are in C1, C3, C4, C6 to C10 above. Actions the runtime already shows blocked (barter, training, and so on) stay with their own tasks. | C0 |
| **Close** | When the census (re-run after each slice; its pinned totals move with the slice) shows no installed dialog field stopping on a code outside the subset, `FR-DIALOG-002` drops "other codes outside the subset" from its row. | All slices |

A slice that touches the save schema, the session core or threading is reviewed on Opus; the rest on Sonnet
([review model](../workflow/tasks.md#review-model)). Per PR, run only the smoke stages the change touches: talking to the Crash Site
NPCs is the usual one.

## Table of codes (filled by C0)

First observation 2026-11-07, retail 1.0.7.4 (GOG), 709 DLG files, original mode; counts only, from the opt-in test `DialogCodeCensusRealDataTests`
(`DAT_ROOT`; `ARC_DIALOG_CENSUS_OUT` writes the lines to a local file). A **field** is one condition or action string of a reply or NPC line; the scan is the
original's (DS1: two characters after skipping everything that is not a letter or `$`). **First stop** is the code at which `DialogConversationGrammar` stops a
field (every port admitted): *outside the subset* = the code itself is not admitted; *at a form* = any other stop at the code (the census records the code, not the failure kind); by the grammar these are an admitted code in a form it leaves out
(for example the non-negative `in` action, a `jo`/`uw` with a non-zero first operand, `re`/`al` with `>` or `<`, `ru` 4180 in dialog 1934). A field is
counted once, at its first stop, so a code behind another unsupported code shows 0 here; the next table shows what a whole group frees.

**Fields.** Player conditions 15,512 non-empty: 8,487 parse whole, 7,025 stop (6,989 outside the subset, 30 at a form, 6 at a character that is not a
code). Player actions 16,644: 13,210 whole, 3,434 stop (3,047 outside, 383 at a form, 4 non-code). NPC-line actions 5,211: 4,825 whole, 386 stop
(348 outside, 38 at a form). The 2,716 NPC-record condition fields are not operation strings (2,715 numbers, one text field), so NPC conditions are not a code
census. Together 10,384 fields stop first on a code outside the subset (of 10,845 stopped); 10,431 hold such a code somewhere in the field, the rest of them
stopping first at a form or a non-code.

### Condition codes (37, table 0x5A06BC; Player reply conditions)

| # | Code | Tests | Owner of the state | Parsed | Fields | Files | First stop outside the subset (fields / files) | First stop at a form |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | `PS` | persuasion skill (11), recorded DS2 | PC skills | yes | 274 | 65 | 0 | 0 |
| 1 | `CH` | charisma, signed rule | PC stats | yes (C5) | 478 | 115 | 0 (425 / 114 before C5) | 0 |
| 2 | `PE` | perception, signed rule | PC stats | yes (C5) | 11 | 6 | 0 (11 / 6 before C5) | 0 |
| 3 | `AL` | alignment, signed rule | PC stats | yes (C5) | 31 | 7 | 0 (31 / 7 before C5) | 0 |
| 4 | `MA` | magick aptitude, signed rule | PC stats | yes (C5) | 418 | 40 | 0 (329 / 40 before C5) | 0 |
| 5 | `TA` | technology aptitude, signed rule | PC stats | yes (C5) | 88 | 14 | 0 (80 / 13 before C5) | 0 |
| 6 | `GV` | global variable equals, DG1 | script stores | yes | 624 | 14 | 0 | 0 |
| 7 | `GF` | global flag equals, DG1 | script stores | yes | 2,838 | 198 | 0 | 4 |
| 8 | `QU` | quest state equals | PC quest log | no | 1,303 | 139 | 1,221 / 137 | 0 |
| 9 | `RE` | NPC reaction, signed rule | reaction | yes | 1,683 | 179 | 0 | 0 |
| 10 | `$$` | gold of the PC plus its followers, signed rule | inventories (gold) | yes (C1) | 1,576 | 81 | 0 (1,563 / 80 before C1) | 0 |
| 11 | `IN` | PC or a follower holds item n; negative: the NPC or its container holds item −n | inventories (items) | no | 671 | 112 | 529 / 94 | 0 |
| 12 | `HA` | haggle skill (9), DS2 | PC skills | yes | 290 | 35 | 0 | 0 |
| 13 | `LF` | NPC dialog attachment flag equals | NPC script attachment | yes | 5,806 | 261 | 0 | 6 |
| 14 | `LC` | NPC dialog attachment counter equals | NPC script attachment | no | 315 | 18 | 307 / 18 | 0 |
| 15 | `TR` | skill training, DS2 | PC skills | yes | 595 | 59 | 0 | 0 |
| 16 | `SK` | skill level, DS2 | PC skills | yes | 424 | 62 | 0 | 0 |
| 17 | `RU` | PC knows rumor, RU1 | rumors | yes | 221 | 21 | 0 | 19 |
| 18 | `RQ` | rumor quelled, RU2 | rumors | yes | 61 | 4 | 0 | 1 |
| 19 | `FO` | NPC follows the PC | party link | no | 2 | 1 | 2 / 1 | 0 |
| 20 | `LE` | character level, signed rule | PC stats | yes (C5) | 44 | 2 | 0 (40 / 2 before C5) | 0 |
| 21 | `QB` | quest state at most | PC quest log | no | 468 | 84 | 250 / 57 | 0 |
| 22 | `ME` | NPC has met the PC, DR1 | reaction | yes | 741 | 73 | 0 | 0 |
| 23 | `NI` | neither the PC nor a follower holds item n (negation of IN) | inventories (items) | no | 410 | 87 | 234 / 65 | 0 |
| 24 | `QA` | quest state at least | PC quest log | no | 528 | 96 | 475 / 91 | 0 |
| 25 | `RA` | PC race, RA | PC stats | yes | 703 | 66 | 0 | 0 |
| 26 | `PA` | a follower with name n, not mind-controlled | party and spell flags | no | 366 | 35 | 311 / 34 | 0 |
| 27 | `SS` | story state, signed rule | script story state | yes (C4) | 703 | 40 | 0 (676 / 38 before C4) | 2 |
| 28 | `WA` | NPC waits, SU14 | NPC flags | yes | 259 | 32 | 0 | 0 |
| 29 | `WT` | NPC flag JILTED | NPC flags | no | 0 | 0 | 0 | 0 |
| 30 | `PV` | PC variable equals, DG1 | script stores | yes | 0 | 0 | 0 | 0 |
| 31 | `PF` | PC flag equals, DG1 | script stores | yes | 0 | 0 | 0 | 0 |
| 32 | `NA` | alignment against the negated operand (n ≥ 0 passes at ≥ −n, n < 0 at ≤ n; an odd form, DCC3) | PC stats | yes (C5) | 69 | 9 | 0 (69 / 9 before C5) | 0 |
| 33 | `AR` | area n known to the PC | known areas | yes (C4) | 37 | 10 | 0 (34 / 9 before C4) | 0 |
| 34 | `RP` | PC holds reputation, RP8 | reputation | yes | 134 | 12 | 0 | 0 |
| 35 | `IA` | the PC's map is in area n | map area | yes (C4) | 238 | 19 | 0 (233 / 19 before C4) | 0 |
| 36 | `SC` | spell college rank, signed rule | PC college ranks | no | 158 | 16 | 158 / 16 | 0 |

Codes outside the table in conditions (damaged data, one file each): `$2`, `$5`, `BE`, `L9`, `MM`, `ST`; the original fails a condition on an unknown code (DS1).
Unused in every installed dialog: `wt`, `pv`, `pf`.

### Action codes (35, table 0x5A0750; Player replies and NPC lines together)

| # | Code | Does | Owner of the state | Parsed | Fields | Files | First stop outside the subset (fields / files) | First stop at a form |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | `$$` | PC gets n gold, or n < 0: the PC pays −n to the NPC (followers pay the rest) | inventories (gold) | yes (C1) | 1,388 | 139 | 0 (1,353 / 137 before C1) | 3 |
| 1 | `RE` | NPC reaction: set, add, raise to or lower to | reaction | yes (`>`/`<` no) | 2,593 | 262 | 0 | 15 |
| 2 | `QU` | quest state set (with the NPC for the experience) | PC quest log | yes | 1,221 | 152 | 0 | 2 |
| 3 | `FL` | speak node n as the final line, pending code 4 | dialog | yes (last op) | 2,510 | 297 | 0 | 3 |
| 4 | `CO` | start combat: the attack event once at the end of the string | combat | yes (C3) | 1,329 | 203 | 0 (1,318 / 201 before C3) | 1 |
| 5 | `GV` | global variable set, DG3 | script stores | yes | 602 | 29 | 0 | 0 |
| 6 | `GF` | global flag set, DG3 | script stores | yes | 1,732 | 230 | 0 | 1 |
| 7 | `MM` | mark area n known to the PC | known areas | yes | 140 | 54 | 0 | 0 |
| 8 | `AL` | alignment: set, add, raise to or lower to | PC stats | yes (`>`/`<` no) | 129 | 36 | 0 | 0 |
| 9 | `IN` | n ≥ 0: the PC or a follower hands item n to the NPC; n < 0: the NPC gives item −n | inventories (items) | negative form only | 606 | 162 | 0 | 326 |
| 10 | `LF` | NPC dialog attachment flag set | NPC script attachment | yes | 4,043 | 350 | 0 | 28 |
| 11 | `LC` | NPC dialog attachment counter set | NPC script attachment | no | 213 | 18 | 213 / 18 | 0 |
| 12 | `TR` | skill training set, DS4 | PC skills | yes | 115 | 43 | 0 | 0 |
| 13 | `RU` | PC learns rumor, LB12 | rumors | yes | 343 | 98 | 0 | 0 |
| 14 | `RQ` | rumor quelled, LB12 | rumors | yes | 98 | 31 | 0 | 0 |
| 15 | `JO` | NPC joins the party, go to node | party | yes (first operand 0) | 379 | 62 | 0 | 34 |
| 16 | `WA` | NPC waits | NPC flags | no | 49 | 33 | 47 / 32 | 0 |
| 17 | `LV` | NPC leaves the party (forced) | party | no | 83 | 38 | 79 / 38 | 0 |
| 18 | `SS` | story state set | script story state | yes (C4) | 102 | 23 | 0 (102 / 23 before C4) | 0 |
| 19 | `SC` | NPC spread-out flag cleared | NPC flags | no | 49 | 33 | 49 / 33 | 0 |
| 20 | `SO` | NPC spread-out flag set | NPC flags | no | 47 | 32 | 47 / 32 | 0 |
| 21 | `UW` | NPC stops waiting, go to node | party | yes (first operand 0) | 76 | 28 | 0 | 12 |
| 22 | `PV` | PC variable set, DG3 | script stores | yes | 0 | 0 | 0 | 0 |
| 23 | `PF` | PC flag set, DG3 | script stores | yes | 0 | 0 | 0 | 0 |
| 24 | `XP` | PC gets the experience of quest level n | experience | no | 38 | 13 | 13 / 10 | 0 |
| 25 | `NK` | NPC is killed | combat | no | 9 | 4 | 9 / 4 | 0 |
| 26 | `RP` | reputation added (n > 0) or removed | reputation | yes | 102 | 29 | 0 | 0 |
| 27 | `NP` | queue newspaper issue n | newspaper queue | no | 21 | 6 | 20 / 6 | 0 |
| 28 | `ce` | generated line, pending code 5 | dialog windows | no | 47 | 27 | 47 / 27 | 0 |
| 29 | `FP` | one fate point, FT8 | fate | yes | 44 | 19 | 0 | 0 |
| 30 | `SU` | schematic window, pending code 7, NC1 | dialog windows | yes | 8 | 5 | 0 | 0 |
| 31 | `OR` | NPC origin set | NPC fields | no | 4 | 1 | 4 / 1 | 0 |
| 32 | `II` | generated line, pending code 8 | dialog windows | no | 26 | 2 | 26 / 2 | 0 |
| 33 | `RI` | generated line, pending code 9 | dialog windows | no | 61 | 8 | 59 / 7 | 0 |
| 34 | `ET` | expert teacher line, DS5 | PC skills | yes | 118 | 20 | 0 | 0 |

Codes outside the table in actions (damaged data, one file each): `AW AY LK MA PT TA` in player replies, `AD BL ES NG SI` and a stray `D ` in NPC lines;
the original ends an action string on an unknown code (DS1). Unused in every installed dialog: `pv`, `pf`.

### Groups by state owner, and the order that frees the most fields

Each stopped field is blocked by the set of codes outside the subset it holds (not only the first). A greedy order by fields freed (a field is freed when
all its blocking codes are admitted; forms of admitted codes are not counted):

| Step | Group (codes) | Fields freed | Cumulative |
| --- | --- | --- | --- |
| 1 | gold (`$$` condition and action) | 2,857 | 2,857 |
| 2 | quest log (`qu qa qb`) | 1,653 | 4,510 |
| 3 | combat start (`co` action) | 1,325 | 5,835 |
| 4 | story and area (`ss` both, `ar`, `ia`) | 1,051 | 6,886 |
| 5 | PC stats (`ch pe le al ma ta na`) | 1,026 | 7,912 |
| 6 | items (`in`, `ni`) | 1,056 | 8,968 |
| 7 | party and NPC flags (`fo pa wt`; `wa lv sc so nk`) | 559 | 9,527 |
| 8 | NPC counter (`lc` both) | 528 | 10,055 |
| 9 | spell colleges (`sc` condition) | 158 | 10,213 |
| 10 | generated-line windows (`ce ii ri`) | 132 | 10,345 |
| 11–13 | experience `xp` 38, newspaper `np` 21, origin `or` 4 | 63 | 10,408 |
| — | never freed by a group (codes outside the table) | 23 | 10,431 |

After C1 (ARC-DEV-179, 7,574 fields still blocked by a code outside the subset; the census prints these lines):

| Step | Group (codes) | Fields freed | Cumulative |
| --- | --- | --- | --- |
| 1 | quest log (`qu qa qb`) | 1,653 | 1,653 |
| 2 | combat start (`co` action) | 1,325 | 2,978 |
| 3 | story and area (`ss` both, `ar`, `ia`) | 1,051 | 4,029 |
| 4 | PC stats (`ch pe le al ma ta na`) | 1,026 | 5,055 |
| 5 | items (`in`, `ni`) | 1,056 | 6,111 |
| 6 | party and NPC flags (`fo pa wt`; `wa lv sc so nk`) | 559 | 6,670 |
| 7 | NPC counter (`lc` both) | 528 | 7,198 |
| 8 | spell colleges (`sc` condition) | 158 | 7,356 |
| 9 | generated-line windows (`ce ii ri`) | 132 | 7,488 |
| 10–12 | experience `xp` 38, newspaper `np` 21, origin `or` 4 | 63 | 7,551 |
| — | never freed by a group (codes outside the table) | 23 | 7,574 |

After C3 (ARC-DEV-190, 4,596 fields still blocked by a code outside the subset):

| Step | Group (codes) | Fields freed | Cumulative |
| --- | --- | --- | --- |
| 1 | story and area (`ss` both, `ar`, `ia`) | 1,051 | 1,051 |
| 2 | PC stats (`ch pe le al ma ta na`) | 1,026 | 2,077 |
| 3 | items (`in`, `ni`) | 1,056 | 3,133 |
| 4 | party and NPC flags (`fo pa wt`; `wa lv sc so nk`) | 559 | 3,692 |
| 5 | NPC counter (`lc` both) | 528 | 4,220 |
| 6 | spell colleges (`sc` condition) | 158 | 4,378 |
| 7 | generated-line windows (`ce ii ri`) | 132 | 4,510 |
| 8–10 | experience `xp` 38, newspaper `np` 21, origin `or` 4 | 63 | 4,573 |
| — | never freed by a group (codes outside the table) | 23 | 4,596 |

After C4 (ARC-DEV-191, 3,545 fields still blocked by a code outside the subset):

| Step | Group (codes) | Fields freed | Cumulative |
| --- | --- | --- | --- |
| 1 | PC stats (`ch pe le al ma ta na`) | 1,026 | 1,026 |
| 2 | items (`in`, `ni`) | 1,056 | 2,082 |
| 3 | party and NPC flags (`fo pa wt`; `wa lv sc so nk`) | 559 | 2,641 |
| 4 | NPC counter (`lc` both) | 528 | 3,169 |
| 5 | spell colleges (`sc` condition) | 158 | 3,327 |
| 6 | generated-line windows (`ce ii ri`) | 132 | 3,459 |
| 7–9 | experience `xp` 38, newspaper `np` 21, origin `or` 4 | 63 | 3,522 |
| — | never freed by a group (codes outside the table) | 23 | 3,545 |

After C5 (ARC-DEV-192, 2,519 fields still blocked by a code outside the subset):

| Step | Group (codes) | Fields freed | Cumulative |
| --- | --- | --- | --- |
| 1 | items (`in`, `ni`) | 1,056 | 1,056 |
| 2 | party and NPC flags (`fo pa wt`; `wa lv sc so nk`) | 559 | 1,615 |
| 3 | NPC counter (`lc` both) | 528 | 2,143 |
| 4 | spell colleges (`sc` condition) | 158 | 2,301 |
| 5 | generated-line windows (`ce ii ri`) | 132 | 2,433 |
| 6–8 | experience `xp` 38, newspaper `np` 21, origin `or` 4 | 63 | 2,496 |
| — | never freed by a group (codes outside the table) | 23 | 2,519 |

The slices C1 to C10 above follow this order. A step's gain is not always smaller than the one before it, because a field blocked by several groups is freed only by the last of them; the order is a
proposal, to be re-run after each slice (the census prints the greedy lines). The non-negative `in` action (326 fields stop at its form, by the grammar) goes with the
items slice, and `ru` 4180 / `jo` / `uw` / `re >` forms stay separate small items.

## Where this is linked

- [`FR-DIALOG-002` coverage row](../requirements/coverage/story-social-ai.md): "other codes" point here.
- [Conversation runtime, status and scope](../scripting/dialog-conversation-runtime.md#condition-codes-outside-the-subset-arc-res-091).

# Spell book runtime (ARC-MAGIC-004)

Status: **Implemented bounded component, not wired.** Work item ARC-MAGIC-004 (2026-10-21), slice MT3 "Spell book" of the
[magic implementation plan](../research/magic-implementation-plan.md). One spell-book rule that auto-level, the character
editor's point pool and the scripts share: learn rank r, unlearn the highest rank, mastery once with all five ranks, and the
re-validation of known ranks when intelligence or willpower falls. Godot-free: pure rules in `Arcanum.Simulation.Magic`, the
mastered college on the hero's and NPCs' advancement sheets in `Arcanum.Session` (snapshot schema 25).

Requirements: [FR-MAGIC-002](../requirements/combat-magic-tech.md#fr-magic-002) (spell book and college order),
[FR-CHAR-015](../requirements/characters-skills.md#fr-char-015) (points spent and refunded, the stat-decrease guard).

Every rule below was read in the retail 1.0.7.4 `Arcanum.exe` by two independent readers
([ledger "Spell book"](../research/original-verification-ledger.md#spell-book-arc-magic-004), rows SB1–SB11), and the learn
(0x4B1790) was compared with the original in the function oracle on 10,000 seeded inputs (F3, receipt
[`4B1790.json`](../verification/receipts/4B1790.json); re-run with `./arcanum oracle --function 0x4B1790`, which the
`SpellBookOriginalExeTests` compare with four planted mutants). Code: [`SpellBookRules`](../../src/Arcanum.Simulation/Magic/SpellBookRules.cs),
[`SpellRankLane`](../../src/Arcanum.Simulation/Magic/SpellRankLane.cs),
[`GameSessionSpellBook`](../../src/Arcanum.Session/GameSessionSpellBook.cs); tests:
[`SpellBookRulesTests`](../../tests/Arcanum.Simulation.Tests/Magic/SpellBookRulesTests.cs),
[`SpellBookOriginalExeTests`](../../tests/Arcanum.Simulation.Tests/Magic/SpellBookOriginalExeTests.cs),
[`SpellBookStateTests`](../../tests/Arcanum.Session.Tests/SpellBookStateTests.cs).

## The book

A critter's book (`SpellBook`) is the known rank of each of the sixteen colleges (0..5) and the mastered college (0..15, or
−1). The original keeps both in the critter's spell-and-tech field (indices 0..15 and 16); an empty book is all ranks 0 and
no mastery (0x4B1530). A rank r of college c is spell `5c + r − 1`; spells 0..79 are the colleges' (SB1).

`SpellBookRules.FromFields` reads a critter's book from the 25 stored spell-and-tech values of the critter field reader, for
any critter on any map; a rank outside 0..5 or a mastery outside −1..15 gives no book. The AREA viewer's follower loader
(`AreaAdvancement.Npc`) takes the mastery from it, and an NPC whose book is out of range has no admissible advancement sheet.

## Rules

| Rule | Original | Function |
| --- | --- | --- |
| `Learn` unforced (auto-level, the editor) | The rank must be the college's next (known + 1); then the character level ≥ 1, 1, 5, 10, 15 by rank; the effective intelligence ≥ 5; the effective willpower ≥ the spell's minimum (its record's `IQ:`, or 6, 9, 12, 15, 18 by rank); the rank is written and one magick point added (`RankPointCost`). Failures change nothing. | 0x4B1790 (oracle F3), 0x4B1770, 0x4B1750, 0x4B1760; SB2, SB4 |
| `Learn` forced (prototypes, the cheat key) | No check: a rank above the known one is written and the difference added in magick points; a rank already known succeeds without change | 0x4B1790; SB3 |
| `IsKnown` | 0..79 and the spell's rank at most its college's | 0x4B1950; SB5 |
| `Unlearn` | Only the highest known rank; one magick point taken; the mastered college is kept; nothing re-validated | 0x4B19B0; SB6 |
| `CanMaster`, `Master` | No college mastered yet and all five ranks known; then the college is mastered, for life | 0x4B1C70, 0x4B1CF0; SB7 |
| `RanksHold` | The stat setter's re-validation of a lower base intelligence (each known rank needs 5) or willpower (each known rank's minimum), against the new effective value | 0x4B1B90, 0x4B1C00; SB11, CP8 |

The magick points move by the change's amount and the caller's stat setter clamps them to 0..210, as the original's does.
Auto-level's spell purchase (`AutoLevelRules`, AL6) learns through the same unforced `Learn`, with the sheet's own mastery
kept.

## Character points

Spell ranks are the `AdvancementTrack.College` lane of the shared [character point pool](character-point-pool.md):
`SpellRankLane` costs `RankPointCost` (1) for the college's next rank, which the pool tests against the unspent points
**before** the learn's checks (0x57C540: "not enough points" first), learns it unforced with the sheet's level and its
effective intelligence and willpower (stat effects and poison), and unlearns the highest rank for the same refund (0x57C670).
The pool keeps the editor's floor: only ranks learned since the editor opened can be unlearned, and once it closes never
(owner rule 2026-10-20). Refusals: `NotEnoughPoints`, `LevelTooLow`, `IntelligenceTooLow` (as a tech degree's), `StatTooLow` (willpower named),
`AtMaximum` (five ranks), `AtMinimum` (no rank), `AtFloor`. Lowering intelligence or willpower in the editor is refused while a
known rank needs it (`SpellNeedsStat`).

## Session API

| Entry | Contract |
| --- | --- |
| `SpendHeroPoint(expected, College, college)`, `RefundHeroPoint(…)` | Learn and unlearn through the pool (see above); one revision each. |
| `QueryHeroSpellBook()` | The hero's ranks and mastery, or null without a hero. |
| `QueryNpcSpellBook(entity)` | An NPC's book from its saved advancement record, or null while the session has none (the host then reads its fields with `FromFields`). |
| `MasterHeroCollege(expected, college)` | The script action's mastery (MT21 calls it): refused without a hero (`SpellBookUnavailable`), for a college outside 0..15 (`InvalidSpellBook`), a stale version, or by the rule (`SpellBookRefused` with `AlreadyMastered` or `RanksMissing`). |
| `MasterNpcCollege(expected, loaded, college)` | The same on an NPC's advancement record (created from `loaded` when the session has none, within the record budget). |

## Snapshot schema 25

The hero's advancement (`advancement.mastery`) and each NPC advancement record (`mastery`) save the mastered college
(−1 none), right after `colleges`. A new hero's mastery is −1 unless its definition says otherwise; a mastery outside −1..15 is
`InvalidHero` (the hero) or `InvalidAdvancement` (an NPC record). Schema 24 is refused, not migrated
([game session](game-session-runtime.md)).

## Differences from the original

Owner decision 2026-10-20: every difference is documented. This slice adds no rule-mode switch and needs no owner choice.

| Original 1.0.7.4 | Steamwoven | Mode |
| --- | --- | --- |
| Learn and unlearn take any spell number: one outside 0..79 addresses the college index 16 and above of the spell-and-tech field, which is the mastery and the tech degrees (SB9; no caller passes one). | Refused (`Invalid`), by the no-out-of-bounds rule. | both |
| The level minimum (0x4B1770) has no range test: a negative or large spell reads before or past its table (SB4). | `MinimumLevel` throws outside 0..79; no caller passes such a number. | both |
| The mastery test and set take any college index (SB7). | A college outside 0..15 is refused (`Invalid`). | both |
| Each function reads only the one college it needs; a rank above 5 or a mastery outside −1..15 elsewhere in the field is not noticed. | The rules take a whole book and refuse one outside its ranges (`Invalid`); the session and the critter-field reader never hold such a book. | both |
| The editor reports a failed learn by testing the level, then intelligence, then names willpower for anything else, so a rank-order failure is reported as a level, intelligence or willpower failure (SB8). | The typed status names the failing check in the learn's order; the messages are the sheet's (MT19). | — |
| The level check reads the effective level (stat 17 through the effect system). | The sheet's stored level; no effect changes the level in the modelled effects. | both |
| A network client sends learns, unlearns and rank writes to the host. | Single player only. | — |
| The learn refreshes the player's interface; the unlearn clears the spell from the spell bar (0x57F340). | The host's. | — |
| The forced learn is called by the prototype loader and the cheat key. | The rule takes the flag; no session path calls it yet (a critter's book comes from its fields). | — |

## Not ready

- No caller in the viewer: the character sheet's spell page and its learn and unlearn buttons are MT19; the script actions
  (`knows_spell`, give mastery) are MT21; the AI's use of the book is slice I8.
- The spell bar and the maintained spells of an unlearned rank are MT20.

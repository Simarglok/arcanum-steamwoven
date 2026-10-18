# Experience and levels runtime (ARC-WORLD-030)

Status: **Implemented bounded component**, ARC-WORLD-030 (2026-10-17), top-20 item 16. The hero's experience, level and
unspent points in the [game session](game-session-runtime.md), how the source gives experience (quests, the dialog and script
actions, hits and kills) and how a level is reached, and the experience worth and pool of the NPCs the session changed.
Experience and the NPC records are saved (snapshot schema 14). Requirements:
[FR-CHAR-013](../requirements/characters-skills.md#fr-char-013) (experience and level thresholds),
[FR-CHAR-014](../requirements/characters-skills.md#fr-char-014) (points for a level), the experience part of
[FR-CHAR-026](../requirements/characters-skills.md#fr-char-026) (a dead PC gets none) and the experience part of
[FR-SCRIPT-004](../requirements/story-social-ai.md#fr-script-004) (a completed quest pays). In the viewer only the
experience data and a dialog's quest completion are wired (ARC-UI-007, [Not ready](#not-ready)).

## Evidence

- CE at the pinned commit `a7ff41b300ef712f0e7d088183a3d08957110cdd`: `level.c` (the table, to-next, progress, points,
  the recalculation, the followers' update), `critter.c` (the award, an NPC's worth, the kill award), `combat.c` (the pool
  share of a hit), `quest.c` (quest experience), `stat.c` (the stat ranges and the experience setter), `dialog.c` and
  `script.c` (the two actions). No code or structure was copied.
- The retail 1.0.7.4 `Arcanum.exe` (SHA-256 `9bd80511…78d52`), read statically per the
  [original executable check](../research/original-executable-check.md) by two independent readers; rows XP1–XP12 in the
  [verification ledger](../research/original-verification-ledger.md#experience-and-levels-arc-world-030). The original
  differs from CE in two places, both **CE reconstruction slips**, which need no rule-mode switch: both modes follow the
  original. A third difference, found later by the oracle trial of
  [ARC-RES-051](../research/original-oracle-research.md), is a bug of the original: progress computes `1000 × remaining`
  in 32 bits and wraps above 2,147,483 remaining (XP3), never reached with the installed table. Owner decision
  2026-10-17: a rule-mode switch, `ExperienceRuleProfile.ProgressProductWraps` (ARC-WORLD-033): Original wraps,
  Steamwoven keeps the exact value; `ExperienceRules.Progress` takes the profile and `QueryHeroExperience` passes the
  session's current rules.
  - **Hard difficulty takes a quarter of an award away**, not half (XP1).
  - **At the last level the original writes the level-50 threshold** back as the experience (CE: level 51's, which
    re-enters the recalculation without end). The PC never passes level 50 and the game never hangs (XP5, as the earlier
    ledger rows LM11 and A1 found).
- Installed original (opt-in, below): the four tables read and are admitted; the winning `xp_level.mes` (the `Arcanum4.dat`
  copy) is strictly increasing.

## Source rules

| Step | Original |
| --- | --- |
| Table (`level_init`, XP6) | `rules\xp_level.mes` message `n` is the experience of level `n`, 1..51; a missing one is 0, level 1 is always 0. |
| Award (`critter_give_xp`, XP1) | Only a PC. A zero award does nothing. Easy adds half, hard takes a quarter away, both truncated toward zero. A dead PC gets nothing. The `xpgain` effects then change the award (the effect aggregation of [durable effects](durable-effects-runtime.md)); it is added to the experience, which is clamped to 0..2,000,000,000 (XP2), and the level is recalculated. |
| To next level, progress (XP3) | The next level's threshold minus the experience (999,999 at 51); progress in thousandths `1000 − 1000 × remaining / step`, 0 at 51, 999 for level 0 or a reached next level. |
| Points (XP4) | One per level, two for every fifth level. |
| Recalculation (`level_recalc`, XP5) | Nothing at level 51 or while the next level is not reached. Otherwise each reached level is written with its points (unspent points capped at 56: more are lost, XP2) and its message, or the PC's auto-level scheme spends them. When the next level would be 51, the experience becomes level 50's threshold and nothing else changes. Followers and nearby waiting followers then follow the PC's level step, (old, 51) in the last-level case. |
| Followers (`update_follower_level`, XP7) | A follower at or above the PC's new level stays; one at or below the PC's old level keeps its gap, one between moves to the PC's new level; never above 50; the levels' points, then the NPC's auto-level scheme. |
| Quests (XP8, XP11) | A request for Completed that passes the quest rules gives `xp_quest.mes`[the quest's level] before the state is stored; the dialog `XP` action and the script action that gives quest experience give `xp_quest.mes`[value]. A quest's level is the first number of its `rules\gamequest.mes` entry, 1 without one. |
| Hits (`combat_dmg`, XP10) | A hit with an attacker on an NPC not turned to stone, while its pool is positive: `worth × (100 × damage / max HP) / 100`, at most the pool, leaves the pool; only a PC attacker gets it. |
| Kills (`critter_notify_killed`, XP9) | A kill by the PC or a critter whose leader is the PC gives the PC `worth × 20 / 100`; then the worth is 0. Other kills change nothing. |

## Data (Data `ExperienceTables`)

`ReadLevels` (`rules\xp_level.mes`: 51 thresholds, the source's missing-is-0 and level-1-is-0), `ReadValues`
(`rules\xp_quest.mes` and `rules\xp_critter.mes`: every entry by number) and `ReadQuestLevels` (`rules\gamequest.mes`: the
first number of quests 1000..1999). Each entry it reads must be one number (optional spaces and sign, 1..10 digits in the int32 range),
or the table is refused with the entry's number (`InvalidNumber`; a quest entry with no first number is `MissingField`):
the source's `atoi` would read a prefix or 0. `xp_critter.mes` is for the host that places new NPCs (their worth and pool
by level, `critter_xp_worth`); the session never reads it.

## Rules (Simulation `Character.ExperienceRules`)

`ExperienceTable.Create` admits 51 thresholds that start at 0, rise strictly and stay within 2,000,000,000 (project
admission: an equal step would divide by zero in the progress formula; the installed table qualifies). `ToNextLevel`,
`Progress`, `BonusPoints`, `Advance` (the recalculation for a PC without an auto-level scheme and without level effects:
level, experience, unspent points, levels and points gained, and the step for the followers or null when it stopped early;
an optional callback spends each level's points as it is written, ARC-WORLD-036),
`FollowerLevel`, `AdjustForDifficulty`, `KillAward` and `DamageShare`. `DamageShare` computes without the original's
32-bit wrap of worth × percentage, which installed worths (a few thousand) reach only with a hit thousands of times larger
than the maximum hit points.

## Session (`GameSessionOwner`)

| Member | Behaviour |
| --- | --- |
| `GameSessionDescriptor.Experience`, `GameSessionRestoreOptions.Experience` | `SessionExperienceRules.Create(table, quest experience, quest levels)`: data, never saved. Without it the session awards nothing (`ExperienceUnavailable`) and a completed quest gives no experience. Quest values are bounded by `SessionExperienceLimits.MaxAward` (100,000,000) and quest levels must be quests 1000..1999. |
| `Difficulty`, `SelectDifficulty`, descriptor and restore `Difficulty` | The game difficulty (`GameDifficulty`, the player's setting): Normal by default, switched at once without a revision, not saved, like the rule mode. An undefined value is `InvalidDescriptor` or `Rejected`. |
| `SessionHeroDefinition.Experience`, `SessionHeroState.Experience` | The stored experience (0..2,000,000,000, else `InvalidHero`); a pre-made hero has 0. |
| `QueryHeroExperience` | Level, experience, unspent points, and with data the remaining experience and the progress. |
| `AwardHeroExperience(expected, amount)` | XP1 and XP5 for the hero: amount within ±100,000,000 (`InvalidExperience`); a zero amount and a dead hero are `NoChange` with no award. The result's `SessionExperienceAward` has the award after difficulty and effects, the levels and points gained and the followers' level step. One revision. An award that leaves the hero unchanged (a negative one at 0, or the last-level branch from level 50's threshold) is `NoChange` with its award set, and its follower step still applies, as the source runs the followers' update every time. |
| `AwardQuestExperience(expected, level)` | The dialog `XP` action and the script quest-experience action: `xp_quest.mes`[level], 0 (no change) without an entry. |
| `SetQuestState` | A Completed request that passes the quest rules gives the quest's experience first, in the same revision; the overload with `out SessionExperienceAward?` returns it. |
| `RecordNpcDamage(expected, loaded, damage, maxHitPoints, byHero)` | XP10: the NPC's record (or its loaded worth and pool while it has none) loses the hit's share; the hero gets it when `byHero`. Damage 0..1,000,000 and maximum 1..1,000,000. The host calls it only for an NPC not turned to stone, with an attacker. |
| `RecordNpcKill(expected, loaded, creditsHero)` | XP9: with `creditsHero` the hero gets a fifth of the worth and the worth becomes 0, so a second kill pays nothing; without it nothing changes. The host calls it after the dying script, for an NPC. |
| `QueryNpcExperience(entity)` | The NPC's saved record, null while it has none. |

Refusals come in the order named in the API comments; every one leaves the session unchanged. An NPC's worth and pool are
0..100,000,000 (`InvalidExperienceTarget`); new records stop at `GameSessionLimits.MaxExperienceRecords` (4096,
`BudgetExceeded`). A level raises the hero's maximum hit points and fatigue through the [life rules](life-state-runtime.md)
formulas (level is one of their inputs), keeping the damage, so the current values rise with them.

## Snapshot (schema 14)

The hero gains `"experience"` after `"level"`; the root gains `"experience": {"npcs": [{"entity", "worth", "pool"}]}` after
`"effects"`, in ordinal entity order. Restore checks the values' ranges, the order and the budget; it does not check that the
hero's experience agrees with its level (the source recalculates only when experience is written). Schemas 1..13 are
refused, not migrated.

## Tests

- `tests/Arcanum.Simulation.Tests/Character/ExperienceRulesTests.cs`: table admission, to-next and progress at and around
  thresholds, the points schedule, single and multi-level advances, the unspent cap, the last-level branch, followers,
  difficulty, kill and pool shares.
- `tests/Arcanum.Data.Tests/Characters/ExperienceTablesTests.cs`: the four readers on synthetic MES text.
- `tests/Arcanum.Session.Tests/ExperienceStateTests.cs`: awards, levels and points, maximum hit points, clamps, the last
  level, difficulty, the `xpgain` effect, a dead hero, quest experience through `SetQuestState`, pool and kill records,
  the budget, the revision limit, the snapshot and its refusals.
- Opt-in `tests/Arcanum.Session.Tests/ExperienceRealDataTests.cs` (`DAT_ROOT`): reads the four installed tables, admits
  them, and takes the fixture hero from level 1 to 50 with quest experience; it stays at 50. Only counts and levels are
  printed.

## Not ready

- Viewer host, partial (ARC-UI-007): with `--hero` the AREA session gets the three tables (`AreaStartingHero.Experience`,
  kept over F8), so a dialog's quest completion gives quest experience and the
  [character sheet](../rendering/terrain-viewer.md#character-sheet-arc-ui-007) shows the experience to the next level. Not
  wired: the dialog `XP` action and a level-up message; hits and kills give experience since ARC-COMBAT-005 and the
  points are spent by the hero's scheme since ARC-WORLD-036.
- Auto-level schemes are [ARC-WORLD-036](advancement-runtime.md): the hero's scheme spends its points at creation and at
  each level, and the followers' level step is applied to their saved sheets with their schemes. Point purchases by the
  player and the Educator background are not modelled.
- Effects on the level stat (none in the installed rules) are not modelled; the recalculation reads the stored level.
- Quest alignment and the quest reaction effects belong to their own owners.

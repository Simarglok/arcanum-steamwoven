# Auto-level schemes runtime (ARC-WORLD-036)

Status: **Implemented bounded component**, ARC-WORLD-036 (2026-10-17), the follow-up of
[experience and levels](experience-runtime.md) (top-20 item 16). The source's auto-level schemes, which spend a critter's
unspent points by a rule of steps, run for the hero at its creation and at each level it reaches, and for the hero's
followers after their level update (Virgil among them). The hero's purchasable fields and the levelled NPCs' sheets are
saved (snapshot schema 20). Requirements: [FR-CHAR-021](../requirements/characters-skills.md#fr-char-021) (auto-level
schemes), the follower part of [FR-CHAR-014](../requirements/characters-skills.md#fr-char-014) (points for a level). The
AREA viewer runs them with `--hero` ([Viewer](#viewer-host)).

## Evidence

- The retail 1.0.7.4 `Arcanum.exe` (SHA-256 `9bd80511…78d52`), read statically per the
  [original executable check](../research/original-executable-check.md) by two independent readers; rows AL1–AL14 in the
  [verification ledger](../research/original-verification-ledger.md#auto-level-schemes-arc-world-036). One difference from
  the reading before (ARC-RES-032), which needs no rule-mode switch: in the original a stat purchase continues from the
  stat's base value and pays from the base unspent points (AL4). Two **bugs of the original**, a college
  target above 5 and a degree target above 7 (AL7, AL8), are reached by no installed rule; both modes refuse such a rule
  when the data is read, so no switch exists ([rule modes](rule-modes.md)).
- Installed original (opt-in, below): all 58 rules parse; every pre-made hero's scheme spends its five starting points;
  Virgil's fields and effects read from the Crash Site object file, and his scheme runs from level 2 to 50.
- Earlier research: [Virgil's auto-level](../research/virgil-auto-level.md) (ARC-RES-031) and
  [level-up replay](../research/virgil-level-replay.md) (ARC-RES-032).

## Source rules

| Step | Original |
| --- | --- |
| Scheme lookup (AL2) | A critter's scheme number names a rule: 1..49 in `rules\userlevel.mes`, 50..199 in `rules\gamelevel.mes`; 0, other numbers and missing entries name none. |
| Run (AL1) | No rule: nothing. Otherwise the rule; an NPC whose rule completed then runs the standard rule 70. A PC never does. |
| Rule (AL3) | Steps `<token> <target>,` in order; the target is the C library's `atoi` of the text up to the comma. A step that runs out of points stops the rule, one that fails otherwise goes on; an unknown token stops it. Completed means every step ran. |
| Stats (AL4, AL10) | Each unit from the base value costs 0 to reach 1 and 1 above, paid from the base unspent points; the race maximum clamps a write, which fails the step. |
| Skills (AL5) | One point per point while the level is below the target. A point above the key stat's cap is refused and the key stat is raised by one (AL4) instead. Tech points move with tech skill points. |
| Spells (AL6) | The college's next rank while below the target, one point each; the rank needs character level 1, 1, 5, 10, 15, intelligence 5 and the spell's willpower (6, 9, 12, 15, 18 by rank unless `spelllist.mes` names one). A rank the level refuses fails the step; another refusal raises intelligence (when below 5) or willpower by one. Each rank adds one magick point. |
| Degrees (AL8) | The discipline's next degree while below the target, one point each; degree `n` needs intelligence 0, 5, 8, 11, 13, 15, 17, 19 and adds one tech point; a refused degree raises intelligence by one. |
| Hit and fatigue points (AL9) | While the maximum is below the target and the base unspent points are at least 1: one bought point (4 more before effects); a maximum that did not rise fails the step with the point kept unpaid, else 1 point is paid. |
| When (AL11, AL12) | At a new PC's creation after its level-1 points; for a PC after each level written with its points; for a follower after its level rose. |

## Data

- Data `AutoLevelTables` (`src/Arcanum.Data/Characters/AutoLevelTables.cs`): `Rule(game, user, scheme)` (AL2) and
  `ReadSpellWillpower(spells)`: each spell's willpower from `rules\spelllist.mes` entry 1001 + 50 × spell when its text starts
  with `IQ:` and a number (a malformed number refuses the table, `InvalidNumber`), else the default by rank.
- Data `CombatFieldReader` reads a critter's effect list (fields 46 and 47, number and cause) beside its other fields
  (`CombatCritterFields.Effects`); keys must run 0..n−1 in both lists, else the object is `ArrayMalformed`.

## Rules (Simulation `Character.AutoLevelRules`)

`Parse` reads a rule as AL3 does; it refuses (typed `AutoLevelParseStatus`) a byte outside printable ASCII, a token with no
target (A3), a number of more than 9 digits, a college target above 5 and a degree target above 7, and counts the targets
whose text after the number `atoi` ignores (`IgnoredTargetText`). `Apply(sheet, modifiers, scheme, standard, npc,
spellWillpower)` is AL1 over an `AdvancementSheet` (race, monstrous flag, stats, magick and tech points, level, unspent
points, skill fields, colleges, degrees, pools and adjustments) and `AdvancementModifiers` (the effects' stat, skill and
maxima changes, poison); the result is the new sheet, whether the last rule completed and every purchase in order.
`IsValid` admits sheets and modifiers; `StatCost`, `SkillCap`, `SpellMinimumLevel`, `DefaultSpellMinimumWillpower`,
`DegreeMinimumIntelligence` and the effective stat and skill levels are public. Maximum hit points and fatigue come from the
[life rules](life-state-runtime.md). `ExperienceRules.Advance` takes an optional `spend(level, unspent)` callback that runs
after each level written and returns the unspent points left.

## Session (`GameSessionOwner`)

| Member | Behaviour |
| --- | --- |
| `GameSessionDescriptor.AutoLevel`, `GameSessionRestoreOptions.AutoLevel` | `SessionAutoLevelRules.Create(schemes, spell willpower)`: the parsed rules by number (1..199) and the 80 willpower minima; data, never saved. Without it no scheme runs. |
| `SessionHeroDefinition.Advancement`, `SessionHeroState.Advancement` | The hero's skill fields, colleges, degrees, bought hit and fatigue points (0..100) and scheme (0..199); a pre-made hero starts with none bought and its prototype's scheme. |
| `Create` | With scheme data, a new hero's scheme runs once over its starting points (AL11) with its innate effects only. |
| Pool bound | The hero holds at most `SessionAdvancementLimits.MaxHeroPoolPoints` (100) bought hit and fatigue points each, so the effect bounds of its maxima hold; a scheme run (at creation or for one level) that would go past it buys nothing and leaves that level's points unspent. A project guard: a pre-made hero buys at most 64 points in all. |
| Experience awards (`AwardHeroExperience`, `AwardQuestExperience`, `SetQuestState`, hits and kills) | The hero's scheme spends each level's points as the level is written (AL12), with its effects' changes and poison; the award's `Purchases` lists what it bought (null when nothing). A level raises the maxima by the bought points too. |
| `LevelFollower(expected, loaded, modifiers, step)` | For one follower or waiting follower after an award's `Followers` step: its level and points ([experience](experience-runtime.md), XP7), then its scheme and rule 70 (AL1). `loaded` is the NPC's map data, read only while the session has no record of it. A level that does not rise is `NoChange`. Refused, in order: no scheme data (`AdvancementUnavailable`), an invalid entity, sheet, scheme, modifiers or step (`InvalidAdvancement`), the version, a new record over `GameSessionLimits.MaxAdvancementRecords` (256, `BudgetExceeded`), the revision. One revision. |
| `SetAutoLevelScheme(expected, target, scheme)` | SCR action 139 (ARC-SCRIPT-016, ledger OL30, 0x4A6D90): the target (`SessionSchemeTarget.Hero` or `ForNpc(loaded)`) gets the scheme; nothing is bought now, the next level step runs it (the hero's next level written, an NPC's next rise), and a number without a rule then buys nothing (the hero's points stay unspent). An NPC without a record gets one from its loaded sheet. The original stores any number; the session keeps 0..199, so a number outside is refused (a documented deviation no installed script reaches). Refused, in order: no hero for a hero target (`AdvancementUnavailable`), a number outside 0..199 or an invalid loaded record (`InvalidAdvancement`), the version, a new record over the budget (`BudgetExceeded`), the revision; the same scheme is `NoChange`. One revision. No snapshot change: the scheme fields already exist. |
| `QueryNpcAdvancement(entity)`, `AutoLevel` | The NPC's saved record (null until levelled) and the session's scheme data. |

## Snapshot (schema 20)

Since ARC-TECHNOLOGY-002 the editor buys and refunds `techDegrees` through the point pool and the session reads the
expertise they give ([tech degrees](tech-degrees-runtime.md)).

The hero gains `"advancement":{"basicSkills","techSkills","colleges","techDegrees","hitPointPoints","fatiguePoints","scheme"}`
after `"alignment"`; the root gains `"advancement":{"npcs":[...]}` after `"experience"`, each record with its entity, race,
monstrous flag, stats, magick and tech points, level, unspent points, skills, colleges, degrees, pools, adjustments and
scheme, in ordinal entity order. Restore checks every range, the order and the budget. Schemas 1..19 are refused, not
migrated. Since schema 25 (ARC-MAGIC-004) both the hero's advancement and each NPC record also carry `mastery` (the mastered
college, −1 none) after `colleges`, and the spell purchases learn through the [spell book](spell-book-runtime.md)'s one rule.

## Viewer host

With `--hero` the AREA viewer reads `gamelevel.mes`, `userlevel.mes` and `spelllist.mes` with the hero's other start tables
(`AreaAdvancement.Rules`) and the module's `gameeffect.mes` effect rules, so the hero's scheme spends its starting points and
each level's. After every award (a dialog's quest completion, a hit or a kill) the followers and the waiting NPCs on the
current map follow the hero's level step (`AreaAdvancement.LevelFollowers`) from their stored fields
(`CombatFieldReader`, from the map whose objects hold the NPC; a waiting NPC counts while the session places it on the
current map) and effects; the conversation reads a levelled follower's level from the session. The smoke log prints
`ARC-WORLD-036 follower-level`.

## Tests

- `tests/Arcanum.Simulation.Tests/Character/AutoLevelRulesTests.cs`: parsing and its refusals, each purchase kind, caps
  and key-stat raises, spell and degree minima, pools, rule 70, a PC never running it.
- `tests/Arcanum.Data.Tests/Characters/AutoLevelTablesTests.cs`: rule lookup and spell willpower on synthetic MES text.
- `tests/Arcanum.Session.Tests/AdvancementStateTests.cs`: the creation scheme, purchases per level, the pool bound, follower
  levels, refusals, the budget, the snapshot and its refusals.
- `tests/Arcanum.TerrainViewer.Tests/AreaAdvancementTests.cs`: the viewer's tables, a follower's sheet and effects from
  its fields, every effect operator, the award's follower step, which NPCs are nearby.
- Opt-in `tests/Arcanum.TerrainViewer.Tests/AreaAdvancementOriginalTests.cs` (`DAT_ROOT`): the twelve pre-made heroes spend
  their starting points and level to 50 with their schemes; Virgil's sheet levels from 1 to 50 with scheme 91. Only counts
  and levels are printed.

The hero's purchases read the skill and maxima changes of its durable effects and, since ARC-SKILLS-002, the race and
gender effects' skill changes ([hero skills](hero-skills-runtime.md)), so a race bonus lowers the points a scheme buys for a
skill target.

## Not ready

- The scheme choice (the character screen's auto-level selection) and point purchases by the player; the Educator
  background; the level-up message.
- Waiting followers "nearby" are the ones on the current map; a waiting follower on another map does not follow the level
  step (the source's distance test is not modelled; it is a screen-sized rectangle around the PC,
  [ARC-RES-064](../research/companion-orders-loyalty-evidence.md#5-waiting-followers-and-the-level-step-q5-xp5-xp7) OL29). An NPC whose home map the viewer has not loaded (map mode covers one
  map) is skipped, even when the session already holds its record.
- Viewer: an award reached while the viewer has no combat host or effect rules (no `--hero`) levels no follower.
- Script changes of a scheme run in the viewer's dialog scripts since ARC-SCRIPT-016 ([set S8](../scripting/scr-dispatcher-runtime.md#set-auto-level-scheme-arc-script-016-set-s8));
  Virgil's script 2023, the one installed action, is not run yet (its other lines use actions no set admits), and an NPC's
  scheme change needs its map data (`--hero`).
- Multiplayer and network branches (AL13) are out of scope.

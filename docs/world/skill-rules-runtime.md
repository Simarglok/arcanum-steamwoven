# Skill rules runtime (ARC-SKILLS-001)

Status: **Implemented bounded component**, ARC-SKILLS-001 (2026-10-20), task S1 of the
skills plan (ARC-RES-061, slice K1 of the [skills research](../research/skills-evidence.md)).
One public, pure, Godot-free owner of the state rules of all sixteen skills: `SkillRules` in
[`Arcanum.Simulation.Skills`](../../src/Arcanum.Simulation/Skills). The attack's `CombatSkillRules` and the auto-level
purchases (`AutoLevelRules`) call it instead of keeping their own copies. No session, snapshot, viewer or dialog change.
Requirements: [FR-CHAR-016](../requirements/characters-skills.md#fr-char-016) (ranks),
[FR-CHAR-017](../requirements/characters-skills.md#fr-char-017) (tech ranks),
[FR-CHAR-018](../requirements/characters-skills.md#fr-char-018) (the training ladder),
[FR-CHAR-020](../requirements/characters-skills.md#fr-char-020) (the tech points side of the aptitude).

## Evidence

- The retail 1.0.7.4 `Arcanum.exe` (SHA-256 `9bd80511…78d52`), read statically per the
  [original executable check](../research/original-executable-check.md) by two independent readers: rows FA7–FA9 and AL5
  (already at F2) and SR1–SR9 of the [verification ledger](../research/ledger/skills.md#skill-rules-arc-skills-001).
  Both readers agree on every row; no anomaly, no rule-mode switch.
- The function oracle ([ARC-DEV-007](../../tools/original-oracle/README.md)) runs the basic level (0x4C5EB0) and the basic
  training setter (0x4C6170) on 10,000 seeded inputs each: no difference, four planted mutants caught per function (F3,
  receipts in [`docs/verification/receipts`](../verification/receipts)).
- Installed original (opt-in, [below](#real-data-census)): the skill fields of every critter prototype and of every critter
  placed on any map read through the rules.

## The skills

`Skill` numbers the twelve basic skills by the source's basic index (bow 0 .. persuasion 11) and the four tech skills as
12 + the tech index (repair 12 .. disarm traps 15). `SkillRules.Basic(i)`, `Tech(i)` and `FamilyIndex` convert.

| Skill | Key stat | Skill | Key stat |
| --- | --- | --- | --- |
| Bow, dodge, melee, throwing, backstab, pick pocket | dexterity | gambling, heal | intelligence |
| Prowling, spot trap | perception | haggle | willpower |
| Persuasion | charisma | repair | intelligence |
| Firearms, disarm traps | perception | pick locks | dexterity |

## Rules

| Rule | Behaviour (original) | API |
| --- | --- | --- |
| Stored value (FA7, SR2, SR4) | Points in bits 0..5, training in bits 6..7, higher bits kept. | `Points`, `StoredTraining`, `BaseLevel` (4 × points) |
| Key-stat cap (FA7) | 3, 7, 11, 15, 19, 20 for a key stat up to 5, 8, 11, 14, 17, 20; the stat is clamped to 1..20 first. | `KeyStatCap` |
| Level (FA7, AL5) | 4 × points, or 20 for the melee of a monstrous critter (mechanical, monster, animal or undead; never a tech skill), then the skill's effect changes, clamped to 0..20, then capped by the effective key stat. | `Level(skill, stored, keyStat, monstrous, effects)` |
| Training (FA8, SR2) | The stored tier; a monstrous critter's melee takes the highest tier whose melee level (1, 9, 18) and character level (10, 20, 30) it reaches in order. | `Training` |
| Minimum level of a tier (SR3) | None 0, apprentice 1, expert 9, master 18. | `MinimumLevel` |
| Points set (AL5, SR9) | Negative points: refused (the source returns 0). Four times the points above the cap of the effective key stat: refused (the source returns the stored points); so no skill holds more than five bought points. Otherwise the point bits are replaced. A tech skill's change also moves the stored tech points by new − old points (the caller adds it, through its own stat clamp). | `SetPoints` → `SkillPointsChange` |
| Training set (SR1) | A tier above the stored one must be the next one; a lower or the same tier skips that rule. Then the skill's effective level (effects and the monstrous melee included) must reach the tier's minimum, so none always succeeds and a master whose level fell to 5 cannot be set to expert. A change replaces the training bits; the source then runs the Educator background update, also when the same tier is written again. | `SetTraining` → `SkillTrainingChange` |
| Cost and refund (SR5) | One character point per skill point, one back per point removed. | `PointCost`, `PointRefund` |
| The next point's key stat (SR7) | The lowest key stat whose cap reaches a level; the sheet's hint asks it for the base level + 4. 0 above level 20. | `MinimumKeyStat` |
| Stat-lowering guard (SR8) | A key-stat value is refused when a skill keyed on it has a base level (four per point; no effects, no monstrous melee) above the value's cap. The stat setter asks only when it lowers the stored stat, with the effective value the new base would give, and keeps the stored stat on a refusal. | `CheckKeyStat`, `CheckBaseChange` → `SkillKeyStatCheck` |

Changes answer typed results: `SkillChangeStatus` `Changed`, `NegativePoints`, `KeyStatTooLow`, `SkipsTier`,
`LevelTooLow` or `Invalid` (an undefined skill or tier, a value outside ±1,000,000, a level outside 0..20, a skill list of
the wrong length), with the stored value after the call and the value the source returns. For points beyond ±1,000,000
the source answers too high for the key stat, and from 2^29 its 32-bit product wraps and writes them (ledger SR9). The reads (`Level`, `Points`,
`Training`, ...) validate their inputs and throw `ArgumentException` outside the domain, as `CombatSkillRules` did.

## Boundaries

- The callers own the state: the session's hero and follower fields (ARC-WORLD-036, schema 20) are not touched here. The hero's
  skills in play are [hero skills](hero-skills-runtime.md) (S2), the [shared skill check](skill-check-runtime.md) S3, the dialog gates S4, buying points with the accepted floor S5 ([character point pool](character-point-pool.md), ARC-WORLD-052).
- Not modelled: the Educator background update (S16), the network message of the training setters, and the spell and
  degree checks the stat setter runs after the skill guard when it lowers intelligence or willpower (magic and technology).
- The stat setter's own clamp and write stay with `CharacterStatsOwner.SetBase`, which does not call the guard yet (the
  point transaction of S5 does).

## Real-data census

`SkillRulesRealDataTests` (category `RealData`; `ARC_AREA_PROTOTYPES` = the installation's `data/proto`, `DAT_ROOT` = the
game folder), first observation 2026-10-20, effects not applied:

- **Prototypes:** 510 PC and NPC prototypes store no skill value at all; the monstrous melee of 280 of them gives the only
  levels (and the derived tiers: 82 apprentice, 69 expert, 25 master).
- **Placed critters:** 3,792 PCs and NPCs on 76 maps (each map's mobile objects, effective fields), 1,049 with skill points,
  every skill used (melee 926, dodge 835, bow 382 at the top; disarm traps 17 at the bottom); stored tiers 229 apprentice,
  193 expert, 54 master. The stored values go beyond what the purchase rules allow: up to 16 points, 6 values above five
  points, 779 with more points than the critter's base key stat allows, 5 tiers without the level they need. The reads keep
  them as the original does, and the level's clamp and cap still hold; only the setters refuse such values.

## Checks

`tests/Arcanum.Simulation.Tests/Skills/SkillRulesTests.cs` (synthetic), `SkillOriginalExeTests` (category `OriginalExe`,
`./arcanum oracle --function 0x4C5EB0` and `--function 0x4C6170`), the existing combat-skill and auto-level tests, and
`tests/Arcanum.Session.Tests/SkillRulesRealDataTests.cs` (opt-in).

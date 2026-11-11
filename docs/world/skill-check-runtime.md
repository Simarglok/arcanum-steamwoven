# Skill check runtime (ARC-SKILLS-003)

Status: **Implemented bounded component**, ARC-SKILLS-003 (2026-10-21), task S3 of the
skills plan (ARC-RES-061, slice K2 of the [skills research](../research/skills-evidence.md#5-the-skill-check)).
One public, pure, Godot-free owner of the check every skill uses: `SkillCheckRules` in
[`Arcanum.Simulation.Skills`](../../src/Arcanum.Simulation/Skills). The [attack resolver](attack-resolver-runtime.md) runs its
attack and dodge rolls through it, and `AttackRules`' effectiveness, critical chance and light helpers call it instead of
keeping their own copies. No session, snapshot, viewer or dialog change; no other skill calls it yet (the uses are skills plan
S7–S15 and the AI's prowling margin, slice I2).
Requirements: [FR-CHAR-019](../requirements/characters-skills.md#fr-char-019) (the check),
[FR-COMBAT-005](../requirements/combat-magic-tech.md#fr-combat-005) (the attack and dodge checks),
[FR-COMBAT-009](../requirements/combat-magic-tech.md#fr-combat-009) (the critical chances).

## Evidence

- The retail 1.0.7.4 `Arcanum.exe` (SHA-256 `9bd80511…78d52`), read statically per the
  [original executable check](../research/original-executable-check.md) by two independent readers: rows SC1–SC17 of the
  [verification ledger](../research/ledger/skills.md#skill-check-arc-skills-003). They agree on every row.
  One earlier reading was wrong and is fixed in both rule settings: a small light of the source keeps three quarters of the
  darkness penalty, not two thirds (SC9). The attack's own invocation and the dodge (SC16, SC17), F0 facts of the resolver
  until now, were re-read in the same pass (owner decision 2026-10-20).
- The function oracle ([ARC-DEV-007](../../tools/original-oracle/README.md)) runs the basic effectiveness (0x4C62E0), the
  opposed effectiveness (0x4C6410) and the tech effectiveness (0x4C69F0) on 10,000 seeded inputs each (a source and an
  optional target, their levels, the target's perception, intelligence and Magnetic Inversion, extraordinary intelligence,
  the local PC and the difficulty setting): no difference, three to five planted mutants caught per function (F3, receipts in
  [`docs/verification/receipts`](../verification/receipts)). The difficulty (0x4C8430), the run (0x4C7160) and the critical
  chances (0x4C81A0, 0x4C82E0) read the world (light, lines of fire, inventories, effects) through many service functions and
  stay at F2.

## The check

| Step | Behaviour (original) | API |
| --- | --- | --- |
| Skill flags (SC1) | Per skill: hit location, distance, light, awareness, the opposed kind (perception, intelligence, the same skill), eyes, arms, legs, combat (the critical divisors) and the 95 cap. Opposed: pick pocket and prowling against perception, persuasion against intelligence, gambling against the same skill. Combat: bow, dodge, melee, throwing, firearms. | `Opponent`, `IsCombat` |
| Opposed effectiveness (SC4) | 100 without a target; the own level minus the opponent's value: 100 from +10, 0 from −10, between them a quarter of the source's table (13 at −9 .. 47 at 0 .. 81 at +9). | `Opposed(level, opponentValue)` |
| Effectiveness (SC2, SC3) | The opposed value for the four opposed skills, else bow and melee 5L + 25, dodge 2L, throwing 7L + 25, spot trap 4L, haggle 4L + 10, heal 5L, repair 4L, firearms 5L + 25 (−20 against a target in Magnetic Inversion), pick locks 6L, disarm traps 4L + 20 (0 at level 0), backstab 0 (SK6). Then +10 for extraordinary intelligence; for the local PC only, easy + half and hard − a quarter (truncated toward zero); the 95 cap of the capped skills (all but bow, melee, throwing, firearms and prowling); at least 0. | `Effectiveness(skill, level, SkillEffectivenessFacts)` |
| Difficulty (SC5–SC11) | The source's terms in its order, each read only when the skill's flag selects it: the modifier; a weapon's to-hit bonus (minus) and 5 per point of missing strength; against a critter −50 paralysed, −50 unconscious, −30 falling or stunned, −50 asleep, else −30 when it cannot notice the source (not for prowling); against any target an aimed location's penalty (two thirds for an expert in firearms) and the armour class share `effectiveness × (AC / 2) / 100` unless a trained backstabber's true backstab; impossible beyond an item's range or with a blocked line of fire, +5 per cell beyond PE / 2 except for a master, plus the cover; darkness; blinded +30, both arms +50 or one +20, both legs +30 for the skills that use them; then the skill's own term (below). "Impossible" is +1,000,000. | `Difficulty(skill, training, effectiveness, SkillDifficultyFacts)` → `SkillDifficulty` |
| Darkness (SC9) | None for a source with critter flag 0x04 (undead) or a skill without the light flag (except in a seeing check), for an expert in melee or pick locks, and for a trained source in spot trap, disarm traps or a seeing check. Otherwise `30 × (255 − light) / 255`, divided by the source's own light (extra large ÷4, large ÷3, medium ÷2, small × ¾), replaced by 30 − it with dark sight (in a seeing check: when the observer has none); 0 or less adds nothing. | `LightPenalty` |
| Own terms (SC11) | Pick pocket: a worn slot's penalty (helmet 50, rings and medallion 30, weapon and shield 70, armour, gauntlets and boots 1000) and `5 × width × height`, halved for an expert. Prowling in a seeing check: a hidden prowler's difficulty is −100 in all, else minus the cover, +50 without cover below expert; in a hearing check minus each worn armour's silent-move adjustment, halved from apprentice. Heal: impossible without a healing item, else minus its bonus. Pick locks: impossible on anything but a portal or container, minus the lockpick's bonus, plus the lock's difficulty (a positive one halved for a master). Disarm traps: plus a trap's difficulty. | (in `Difficulty`) |
| Critical chances (SC13) | Success: effectiveness / 20 for a combat skill, / 2 otherwise; an aimed location's penalty / −5; the weapon's magic critical chance; the backstab term; then the effect changes (no minimum). Failure: (100 − effectiveness) / 7 or / 2; an item below its maximum hit points adds `100 × (max − hp) / (max × divisor)`; the weapon's magic critical miss chance; the effect changes; at least 2. | `CriticalSuccessChance`, `CriticalFailureChance` (`SkillCriticalFacts`) |
| Run (SC12, SC14) | Effectiveness, difficulty, the total with the modifier counted again under A2; the check roll (gambling uses the stored roll and keeps the one it draws for the next check); success when `roll + total ≤ effectiveness` or forced; a second d100 always, against the success or failure chance (a master of melee never fails critically); then, for a PC whose fate point of the skill's kind is armed (gambling, heal, pick pocket, repair, pick locks, disarm traps), a critical success. | `Run(SkillCheckRequest, ISkillCheckRolls, modifierCountedTwice)` → `SkillCheckResult` |

The facts records name what the caller measures in the world: the light at the source or target, distances, lines of fire
and cover, sight and hearing, items, locks and traps (`SkillDifficultyFacts`), the opponent's value, Magnetic Inversion, the
local PC and the game difficulty (`SkillEffectivenessFacts`), the item's magic and hit points, the backstab term and the
effect changes (`SkillCriticalFacts`). The rules validate them (magnitudes up to ±1,000,000, light 0..255, at most 9 worn
armour values and 64 effect changes, defined flags and enums) and throw `ArgumentException` outside the domain; a roll
outside 1..100 throws `InvalidOperationException`. The result carries the shared `SkillCheck` numbers (effectiveness, total,
roll, success, critical chance and roll, critical), the difficulty terms, whether a fate point made it, and gambling's next
stored roll.

## The attack and dodge through the check

`AttackResolver` builds the attack's request from its `AttackRequest` (the bed's −30 as the modifier, the weapon, the
target's conditions and armour class, aim, distance, cover, the attacker's light and injuries, the item's magic and wear,
the true-backstab term, the effect adjustments as one additive change) and the target's dodge request (its dodge level,
training, blindness and crippled legs; no item, no target), with the A2 switch of its `CombatAnomalyProfile` and the same
roll purposes (`AttackCheck`, `AttackCritical`, `DodgeCheck`, `DodgeCritical`) in the same order. A dodge of a critter that
cannot move still rolls and then fails without a critical (SC17). Every attack result is unchanged, except the darkness
term of an attacker with a small light (22 instead of 20 at full darkness, SC9), in both rule settings.

## Boundaries

- Not modelled here: the source's network gate (a client in multiplayer runs no check), the per-skill handlers after the roll
  (what a success opens, heals, repairs, steals or disarms: S7–S15), the fate point's state (the caller passes it armed and
  clears it), gambling's stored roll (the caller keeps it; the session does not yet, S13), the follower substitution and the
  animation goals that start a check.
- An undefined distance (the source's distance function fails) is impossible at once in the original; the caller measures
  the distance and handles that case.
- The awareness inputs (`TargetUnaware`: not in combat mode and cannot see or hear the source, and not a target with critter
  flag 0x40000), the light, the cover and the item fields are the caller's measurements.

## Checks

`tests/Arcanum.Simulation.Tests/Skills/SkillCheckRulesTests.cs` (flags, the opposed table, every formula and modifier, each
difficulty term and its order, darkness, the own terms, the critical chances, the run with forced, master, fate, A2 and
gambling, refusals) and `SkillCheckOriginalExeTests.cs` (the oracle, opt-in: `./arcanum oracle`). The attack resolver's
tests (`AttackResolverTests`, `CombatAnomalyProfileTests`) pass unchanged; `AttackRulesTests` has the new small-light value.

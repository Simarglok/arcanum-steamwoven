# Hero skills runtime (ARC-SKILLS-002)

Status: **Implemented bounded component**, ARC-SKILLS-002 (2026-10-20), task S2 of the
[skills plan](../research/skills-plan.md). The session answers the hero's sixteen skill levels and training, and the levelled
followers' from their saved sheets, through the [skill rules](skill-rules-runtime.md) (ARC-SKILLS-001), and every reader of
a hero's skill uses them: its attack, dodge and backstab, the weapon speed, the best-items choice and the follow check. The
race and gender effects' skill changes of a pre-made hero (ledger SH-09) are kept as the hero's innate skill changes and
applied whenever a level is read, never written into the stored points. Snapshot schema 21. Requirements:
[FR-CHAR-016](../requirements/characters-skills.md#fr-char-016), [FR-CHAR-017](../requirements/characters-skills.md#fr-char-017),
[FR-CHAR-009](../requirements/characters-skills.md#fr-char-009) (race effects on skills),
[FR-CHAR-022](../requirements/characters-skills.md#fr-char-022) (effects on skills), and the skill inputs of
[FR-COMBAT-005](../requirements/combat-magic-tech.md#fr-combat-005) and [FR-COMBAT-012](../requirements/combat-magic-tech.md#fr-combat-012).

## Evidence

- The rules read are at F2 or F3 already (levels, training, points: [skill rules](skill-rules-runtime.md); weapon speed
  0x465CF0, weapon skill 0x465D80, best items 0x465170; the follow check and max followers, ledger AP3, AP4). The persuasion
  facts the task makes live were read by two readers ([ledger HK1, HK2](../research/original-verification-ledger.md#hero-skills-arc-skills-002)):
  a master persuader passes the leader check at once, and any persuasion training of the leader lengthens a follower's
  wait by one step; neither has a runtime yet. No rule-mode switch.
- Installed original (opt-in, [below](#checks)): all twelve pre-made heroes start with the skill levels their scheme and their
  race and gender effects give; 28 innate skill changes over ten heroes; eight attack bare-handed
  with a melee level above 0 (first observation 2026-10-20).

## Session API

| Entry | Contract |
| --- | --- |
| `SessionHeroDefinition.InnateSkills` | The race and gender effects' skill changes (`SessionInnateSkillChange`: cause `Race` or `Gender`, skill, operator, value in level units); null: none. At most seven per effect, the race's before the gender's (the effects' order, which the aggregation of bounds reads), a magnitude within 1000, no divide by 0; anything else is `InvalidHero`. |
| `SessionHeroState.InnateSkills` | The same list, saved. |
| `QueryHeroSkills()` | `SessionSkills` (sixteen `SessionSkill`: stored value, points, effective level, training) or null without a hero. The level reads the hero's stored points, its effective key stat (stat effects and poison included), the innate skill changes and the skill changes of its durable effects; training is the stored tier (a PC is never monstrous). |
| `QueryNpcSkills(entity, modifiers)` | The same from an NPC's saved advancement sheet (a levelled follower, [auto-level schemes](advancement-runtime.md)) under the caller's modifiers (its effects now, as `LevelFollower` takes them), with the monstrous melee tiers; null while the session holds no record. |
| `QueryHeroAttack()` | The weapon skill's level and training (melee bare-handed, firearms a tech skill), dodge and backstab level and training in the `Combatant`, and the weapon speed with that training (an apprentice +5, 0x465CF0). |
| `QueryHeroEquipmentCombat(training?)` | Null training reads the hero's training in the wielded weapon's skill; an explicit value still overrides. |
| `SetHeroSkillTraining(version, skill, tier)` | ARC-SKILLS-004 (the dialog action `TR`): the hero's training through `SkillRules.SetTraining` with the skill's effective level now; a tier the rules refuse or the same tier is `NoChange` with the rules' answer; no Educator update (no background is modelled). Refused: no hero (`SkillsUnavailable`), an undefined skill or tier (`InvalidSkill`), the version, the revision. Saved with the hero's stored skills (schema 21, no new field). |
| Best items | The chooser scores melee, bow, throwing and firearms with the hero's levels. |
| Follow check | With a hero the session supplies the PC's persuasion training, effective charisma, level and max followers (charisma / 4, +1 from expert persuasion, then the hero's effects on the stat, clamped to 1..7, ledger AP4) in place of the caller's `PartyConsentFacts`; without a hero the caller's values stand. The alignment stays the caller's (the conversation host tracks what its dialog actions change). |
| Attack on a levelled follower | `AttackCritter` reads the defender's dodge and backstab (level and training, under the sheet's own stats, as `QueryNpcSkills` reads them without effects) and its level from the session's advancement record when it has one, its map data otherwise. |

The hero's auto-level purchases (creation and level-up) read the innate skill changes too, so a race bonus makes a scheme buy
fewer points for the same target, as in the original, where the race effect is in the critter's effect list.

## Snapshot (schema 21)

The hero gains `"innateSkills":[{"cause","skill","operator","value"}]` (cause `Race` or `Gender`, the `Skill` name, the effect
operator name); the decoder checks the shape and at most 14 entries, the import every value. Schema 20 is refused like the
earlier ones, not migrated ([game session](game-session-runtime.md#snapshot-schema-25)).

## Viewer host

`AreaStartingHero.Definition` maps a pre-made hero's race and gender effect changes on basic and tech skills into
`InnateSkills` instead of dropping them; `DroppedChanges` now counts only the attributes the session does not model
(resistances, tech disciplines, special values, age …).

## Not ready

- No skill page or point buying (tasks S5, S6) and no trainers (S16); the dialog gates and `tr` are ARC-SKILLS-004 ([skill gates](../scripting/dialog-conversation-runtime.md#skill-gates-arc-skills-004)); the shared skill check is S3.
- The leader check (HK1) and the wait timer (HK2) have no runtime, so the persuasion training reaches only the follow check.
- A levelled follower defends with its sheet read without effects (its current stat effects and poison do not change its
  dodge); the map path reads poison.
- The character sheet's max followers is the character-stats value (charisma / 4 with effects), without the expert
  persuasion bonus the follow check adds (AP4); the sheet does not read the session's value yet.
- Backstab and ranged attacks of the hero are not modelled (the hero attacks in melee only), so their skills are read but
  unused; NPCs read map skills except a levelled follower's.

## Checks

- `tests/Arcanum.Session.Tests/HeroSkillsTests.cs`: levels, training and innate changes; attack, dodge and weapon speed; the
  best-items levels; the follow check with persuasion master and the expert follower bonus; the snapshot round trip and its
  refusals and the innate order; the creation scheme with an innate change; a durable effect's skill change; the hero's
  effects on max followers; a levelled follower's skills and the dodge it defends with in the session's attack.
- Opt-in `tests/Arcanum.TerrainViewer.Tests/AreaAdvancementOriginalTests.cs` (`DAT_ROOT`): every pre-made hero's skills (only
  levels and counts printed), and `StartingHeroTests` (`ARC_AREA_CONVERSATION_GAME_ROOT`): the half-elf's four tech skill
  changes are kept, only age is dropped.

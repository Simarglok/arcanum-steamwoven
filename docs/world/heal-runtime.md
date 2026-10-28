# Heal runtime (ARC-SKILLS-005)

Status: **Implemented bounded component**, ARC-SKILLS-005 (2026-10-21), task S8 of the [skills plan](../research/skills-plan.md),
the first use of the [shared skill check](skill-check-runtime.md). The hero uses a healing item on a critter (itself included):
the heal check, the healed amount, the master's and the critical success's cure, the item's uses and its destruction, and the
4 action points in a turn-based round. Pure rules: `HealRules` in [`Arcanum.Simulation.Skills`](../../src/Arcanum.Simulation/Skills/HealRules.cs)
and `LifeRules.SkillHeal`; session: `HealWithItem` in [`GameSessionHeal.cs`](../../src/Arcanum.Session/GameSessionHeal.cs);
viewer: "Use on self" in the AREA inventory window. Snapshot schema 31 (the items' remaining uses).
Requirements: [FR-CHAR-019](../requirements/characters-skills.md#fr-char-019) (a skill used),
[FR-ITEM-009](../requirements/items-economy.md#fr-item-009) (item use and charges),
[FR-CHAR-026](../requirements/characters-skills.md#fr-char-026) (healing the life state).

## Evidence

- The retail 1.0.7.4 `Arcanum.exe` (SHA-256 `9bd80511…78d52`), read statically per the
  [original executable check](../research/original-executable-check.md) by two independent readers and checked against a
  first reading: rows HL1–HL5 of the [verification ledger](../research/ledger/skills.md#heal-arc-skills-005).
  They agree on every row; no anomaly, no rule-mode switch. HL4 re-reads the record heal applier (0x4B58C0) that
  `LifeRules.Heal` (ARC-COMBAT-002) modelled at F0: it agrees, now F2.
- The skill check, its difficulty (a healing item lowers it by its bonus, none makes it impossible) and the heal fate are
  [SC1–SC17](../research/ledger/skills.md#skill-check-arc-skills-003) and
  [FT6](../research/ledger/skills.md#fate-points-arc-world-054).
- Installed original (opt-in, [below](#real-data-census)).

## Rules

| Rule | Behaviour (original) | API |
| --- | --- | --- |
| Amount (HL1) | The healer's heal level; with any training × 3 / 2 (toward zero); at least 1. | `HealRules.MaximumAmount` |
| Success (HL1) | A master or a critical success heals the maximum and cures; otherwise `random_between(1, maximum)` (the only extra draw), no cure. | `HealRules.Resolve` |
| Cure (HL1) | One scar (one effect of cause 7, the critical injury) and the first of blinded, both arms, one arm and both legs crippled; done by the handler itself, so also on a dead critter. | `LifeRules.SkillHeal` |
| Failure (HL1) | Nothing healed; from expert a critical failure is a plain one. | `HealRules.Resolve` |
| Item (HL1) | A generic item with the healing flag loses five uses on a critical failure, one otherwise; at 0 or less it is destroyed. | `HealOutcome.UsesSpent`, `HealWithItem` |
| Applying (HL4) | Only the hit-point damage falls by the amount (never below 0); a dead critter is not healed (no raise without its flags). | `LifeRules.SkillHeal` → `LifeRules.Heal` |
| Use (HL2, HL3) | The inventory passes the user as the target; nothing refuses a target at full hit points, an undead or mechanical one, or the user itself. The item must be the user's. 4 action points are charged at the use in a turn-based round, and the heal goes on whatever the charge answers. | `HealWithItem` |

`HealWithItem(version, target, item, world?)` takes a `SessionHealingItem` (the host's fields: flags, usage bonus, uses, condition,
script points; `AreaHealingItems.From` builds it from ARC-DATA-021 fields). It refuses before any roll or charge: no hero or a dead one
(`HealUnavailable`, as every hero action; an unconscious hero is not refused here, as the session has no action gate for it), not a healing item or values outside the bounds (`InvalidHealingItem`), a use or destroy script
(`ItemScriptUnsupported`, as the session runs none), an item the hero does not hold or the session deleted (`ItemNotAvailable`), an
invalid target, the version. With a world and the hero's subturn it first checks the heal's worst-case budgets (a new NPC life
record, a new durability record) and two free revisions, then charges 4 action points (`ConsumeActionPoints`, its own revision;
overexertion as there); only an exhausted generator can then refuse the heal, and the charge stays. Without a world nothing is
charged, even in the hero's subturn (the caller's choice). A destroyed item counts against the durability budget unless it
already had a record. The check before the charge is the worst case: in a round, a full life-record table refuses a heal of an
unrecorded NPC even when the heal would leave it unchanged (a failed check, full hit points), which the same call outside a round
would allow. An NPC's life write follows the session's life rule (ARC-WORLD-024): hit-point damage left positive and changed
schedules its resting timer on the actor's map, and its pending timers on another map move there; before the charge an NPC heal
needs room for two events (its resting timer and the hero's fatigue timer an overexerting charge can add), and is refused during
an event pass when its timers are on another map; both worst-case checks also refuse when the heal would need neither. Then the check, the life record, the fate and the item are one revision; the rolls (check,
critical, amount) come from the session generator. The answer carries the check, the outcome, the life outcome, the uses left or
the destruction, and whether the heal fate was used.

## Snapshot schema 31

The `durability` section gains `uses`: the remaining uses of every item whose uses the session spent (entity and count 1..1,000,000,
ordinal order; a used-up item is in `destroyed`). The decoder refuses a count outside the range (`OutOfRange`), an out-of-order or
repeated entity (`NotCanonical`) and a missing or unknown field; Restore refuses a uses record of a destroyed item (`InvalidDurability`). Schema 30 is refused, not migrated ([game session](game-session-runtime.md#snapshot-schema-51)).

## Viewer

The AREA inventory window lists a **Use on self** button for each healing item in the PC's list (the kit's from the hero load's
prototype fields, a taken item's from its object row) and runs `HealWithItem` on the hero; in a turn-based round (ARC-COMBAT-020) it passes the round's world, so the use charges its 4
action points on the hero's subturn and goes on whatever the charge answers. A made item (ARC-TECHNOLOGY-004, ARC-WORLD-055) gets no button: the viewer has no object row and so no healing
fields for it, although `HealWithItem` accepts one the hero holds. The window reports the amount, the cure, the uses left or the item used up.

## Boundaries

- Not modelled: the use and destroy scripts (refused), the best-follower substitution and its line, the dialog skill service
  (SK8), the AI's heal decision, healing another critter from the viewer (no target choice), the animation goal's walk and its
  range checks, the network messages.
- The cure removes a scar from the session's scar count; other injury effects of cause 7 on an NPC are not session state.

## Real-data census

- `HealingItemRealDataTests` (Data, `DAT_ROOT`), first observation 2026-10-21: of 1,423 prototypes (115 unread), 148 generic
  items, two healing items (prototypes 15199 and 15226, no bonus, 10 and 60 uses, no scripts); on all 80 interiors two placed
  healing items (15199), on two maps, without scripts. 15199 is the new character's heal kit item (SR13's basic prototype 15179).
- `AreaHealRealDataTests` (viewer, `ARC_AREA_CONVERSATION_GAME_ROOT`): no pre-made hero's kit and nothing on the Crash Site
  exterior is a healing item, so the viewer's button is checked by the synthetic self-test.

## Checks

`tests/Arcanum.Simulation.Tests/Skills/HealRulesTests.cs`, `tests/Arcanum.Session.Tests/HealStateTests.cs` (master, failure and
destruction, a critical failure's five uses, fate, NPC target, the turn-based charge, budgets before the charge, a dead hero,
refusals, the snapshot and its uses list), the viewer self-test (the use button) and the two real-data tests.

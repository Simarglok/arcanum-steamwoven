# Spell invocation runtime (ARC-MAGIC-006)

Status: **Implemented bounded component, not wired.** Work item ARC-MAGIC-006 (2026-10-21), slice MT7 "Invocation check" of
the [magic implementation plan](../research/magic-implementation-plan.md). The check the original runs before any run slot or
payment, with a typed refusal per step, and the item-trigger gate, as the original has them. Godot-free pure rules in
`Arcanum.Simulation.Magic`, for any caster, item, target and map; what needs the world (the line of fire, the BEGIN-area
tests) comes through a port. No rule-mode switch, no saved state.

Requirements: [FR-MAGIC-003](../requirements/combat-magic-tech.md#fr-magic-003) (the target is checked by the definition's
parameters: range, line of fire, forbidden flags, the NPC fallback), [FR-MAGIC-009](../requirements/combat-magic-tech.md#fr-magic-009)
(the anti-teleport refusal of the check), [FR-MAGIC-010](../requirements/combat-magic-tech.md#fr-magic-010) (magic against
technology: the technologist's powerless magic item, the technologist target, the item gate).

Every rule below was read in the retail 1.0.7.4 `Arcanum.exe` by two independent readers
([ledger "Spell invocation"](../research/original-verification-ledger.md#spell-invocation-arc-magic-006), rows IV1–IV12), and the
range (0x454700), the forbidden flags (0x456430) and the item gate (0x456A10) were compared with the original in the function
oracle on 10,000 seeded inputs each (F3, receipts [`454700.json`](../verification/receipts/454700.json),
[`456430.json`](../verification/receipts/456430.json), [`456A10.json`](../verification/receipts/456A10.json); re-run with
`./arcanum oracle --function 0x454700`, `0x456430` and `0x456A10`): Steamwoven matches on every input. The check (0x4564E0) and
the line of fire (0x456BC0) are F2. Code: [`SpellInvocationRules`](../../src/Arcanum.Simulation/Magic/SpellInvocationRules.cs);
tests: [`SpellInvocationRulesTests`](../../tests/Arcanum.Simulation.Tests/Magic/SpellInvocationRulesTests.cs),
[`SpellInvocationOriginalExeTests`](../../tests/Arcanum.Simulation.Tests/Magic/SpellInvocationOriginalExeTests.cs).

## Inputs

- `SpellInvocation`: the spell; the **parent**, the caster (null for a sourceless cast; an item's holder when the item is in an
  inventory); the **source**, the object whose spell it is (the parent itself for its own spell, else the item); the target
  object and the target tile (either may be none); the power the parent draws from a source item (0x4614A0,
  [`EquipmentRules.EffectivePower`](../../src/Arcanum.Simulation/Equipment/EquipmentRules.cs); required when a technologist
  parent uses an item's spell: the check throws rather than guess it); whether the parent may be teleported where it stands
  (0x4BDD30, `Rules\AntiTeleport.mes`); whether the parent is the local player; whether turn-based combat runs and the critter
  whose turn it is (none matches no critter parent, as in the original).
- `SpellObjectFacts` for each object: an id (identity), the object type (15 PC, 16 NPC, 5..14 items), the location (x, y;
  `SpellLocation`, (0, 0) is none), the object flags (0x1000 in an inventory, 0x20 shoot-through), spell flags (0x1000 Bonds of
  Magick), critter flags, the effective magick/tech aptitude and intelligence.
- `SpellInvocationRecord`: technology (`Is_Tech:`, every record from 140), `Range:`, `Disallowed_SF:`, `Disallowed_TSF:`,
  `Disallowed_TCF:` (the [spell record](../formats/spell-records.md)'s values) and the minimum intelligence 5.
- `ISpellInvocationWorld`: the line of fire from the parent to a tile (score and first blocker, 0x4ADE00) — filled by the NPC
  AI awareness stage I2, which owns the line of fire, or a later line-of-sight slice; and the NPC fallback's questions — does the
  target match the record's BEGIN area, does that area take tiles, and the first matching tile in a radius around the target
  object — filled by the target functions (MT8). Tests use a double.
- `ISpellRollSource`: the MG5 d100 (purpose `InvocationAptitudeD100`; the session's `SpellRolls` labels it
  `magic.InvocationAptitudeD100`).

## The check (`Check`, 0x4564E0)

In the original's order; the first failing step answers its refusal:

1. **Turn** (`NotTheirTurn`): in turn-based combat a PC or NPC parent that is not the critter whose turn it is. An item or
   other parent passes.
2. **Spell number** (`InvalidSpell`): outside 0..222 (the original logs "not a spell").
3. **Range** (`OutOfRange`, with a parent; `InRange`, 0x454700): no parent location is in range; a target object in an
   inventory is in range, otherwise its location replaces the tile; no location is out of range; otherwise the tile distance
   (`SpellLocation.Distance`, 0x4B96F0: the larger coordinate difference, in 64 bits) must be at most `Range:`.
4. **Line of fire** (`NoLineOfFire`; `HasLineOfFire`, 0x456BC0): a self-cast or a parentless cast passes; the target tile,
   else the target object's location, is the point (an item in an inventory passes); no point passes; a score of 100 or more
   refuses; a blocker that is neither shoot-through nor the target refuses.
5. **Forbidden flags** (`ForbiddenFlags`; `FlagsAllow`, 0x456430): with a parent, its spell flags against `Disallowed_SF:`;
   with a target object, its spell flags against `Disallowed_TSF:` and, for a PC or NPC, its critter flags against
   `Disallowed_TCF:`.
6. **Bonds of Magick** (`BondsOfMagick`): a parent under it casting its own spell; an item's spell still works.
7. **NPC fallback** (`NoValidTarget`): for a parent that is not a PC (or none), a target that fails the record's BEGIN area is
   replaced, when there is a target object and the area takes tiles, by the first matching tile in **radius 2** around it; the
   target object is then cleared and the result names the tile (`ReplacedTile`). Otherwise refused.
8. **Aptitude**, magic records only:
   - `ItemAptitude`: a parent with negative aptitude using an item's spell draws no power from it (0x4614A0 at most 0); the
     "resisted" effect plays (`PlaysResisted`).
   - `TargetAptitude` (MG5): for a target with negative aptitude T the chance is `(100 − A) × T / 100` (toward zero) for a
     parent aptitude A ≥ 0, else T; a d100 at most the chance refuses. For aptitudes in −100..100 the chance is never positive, so it never refuses, but
     the d100 is rolled whenever T is negative, as in the original (both modes).
9. **Intelligence** (`Intelligence`): a PC or NPC casting its own spell with intelligence below 5, for technology records too.
10. **Anti-teleport** (`AntiTeleport`): Teleportation (spell 4) where the parent may not be teleported; the local player is
    shown the message (`ShowsTeleportMessage`, line 10000 of the magic message file), any other parent nothing.

## Item gate (`HolderCanUseItem`, 0x456A10)

An item's triggered spells (0x4CC930) fire only when the gate allows; the trigger passes its event's first object (usually the
item's holder) as the holder. No holder, an item without magic-tech complexity, or a holder whose aptitude is not negative,
may; a technologist may only when it draws power from the item (asked only then).

## Differences from the original

Owner decisions 2026-10-20: every difference is documented.

| Original 1.0.7.4 | Steamwoven | Mode |
| --- | --- | --- |
| The check reads the objects' fields, the turn, the anti-teleport zones and the local player itself, and runs the line of fire and the target functions. | The caller passes the facts; the line of fire and the BEGIN-area tests come through `ISpellInvocationWorld`. | — |
| With neither a target tile nor a target object the line of fire reads an uninitialised stack slot as its point; called from the check, that slot holds the zero words the range call left, so the case passes (IV4). | The case passes directly (no point). | both |
| The record's `Range:` getter answers 1 before the spell table loads. | Records are loaded before any cast; the caller passes `Range:`. | — |
| The not-a-spell log, the "resisted" effect (0x283) and the anti-teleport message are shown by the check. | The result names them (`InvalidSpell`, `PlaysResisted`, `ShowsTeleportMessage`); the caller shows them. | — |
| The item gate reads a second object's aptitude and ignores it. | Not read. | — |

MG5 (the technologist target never refuses, its d100 still drawn) follows the original in both modes, as decided. No new
anomaly needs an owner choice.

## Not ready

- No caller: the run lifecycle (MT9a) runs the check before a slot; the AI's cast (0x4AC250, slice I8) runs it too, and the
  run's activation runs it a second time.
- The line of fire itself (0x4ADE00) is the AI awareness stage I2's; until then a caller has no implementation of the port.
- The BEGIN-area tests and the target collection (0x4F2D20, 0x4F40B0) are MT8's.
- The anti-teleport zones (0x4BDD30, `Rules\AntiTeleport.mes`) and the movement components are MT16's (FR-TRAVEL-005).
- The spell UI's own line-of-fire call (0x57C235) is MT19's.

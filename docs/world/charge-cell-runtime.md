# Charge-cell runtime

Status: **Bounded rules (Simulation) and a session port; no host writes the plan yet.** Work item ARC-TECHNOLOGY-007,
2026-10-21, the cell part of slice TECH-6 of the [technology research](../research/technology-evidence.md#proposed-implementation-slices-project-policy-not-accepted).
The charge-cell source behind the spell payment's port (`ISpellCellSource`, [spell cost runtime](spell-cost-runtime.md)): a
record with `ChargesFromCells:` (147, 168, 187) pays charge cells instead of fatigue, mana stores or item charges. Since
ARC-WORLD-066 the session holds ammunition ([ammunition](inventory-runtime.md#ammunition-arc-world-066)) and its
`HeroCellSource` spends the hero's cells for an applied payment; the rules and the host-stack port below write nothing
themselves.

Requirements: [FR-MAGIC-010](../requirements/combat-magic-tech.md#fr-magic-010) (magick against technology: the item-source
need, TC1), [FR-MAGIC-011](../requirements/combat-magic-tech.md#fr-magic-011) (tech devices; the device list and the maintain test
are ARC-TECHNOLOGY-008).

Every rule was read in the retail 1.0.7.4 `Arcanum.exe` ([ledger "Charge cells"](../research/original-verification-ledger.md#charge-cells-arc-technology-007),
CC1–CC8, anomalies CCA1–CCA4; switch [TC1](rule-modes.md)). Code: [`ChargeCellRules`](../../src/Arcanum.Simulation/Technology/ChargeCellRules.cs)
(need, plan, `ChargeCellSource`), [`TechnologyRuleProfile.ItemCellsFavourMagick`](../../src/Arcanum.Simulation/Technology/TechnologyRuleProfile.cs)
and [`GameSessionCells`](../../src/Arcanum.Session/GameSessionCells.cs); tests
[`ChargeCellRulesTests`](../../tests/Arcanum.Simulation.Tests/Technology/ChargeCellRulesTests.cs) and
[`CellSourceStateTests`](../../tests/Arcanum.Session.Tests/CellSourceStateTests.cs).

## Who pays and from which cells (CC1–CC3)

- The payment (0x450420) asks the cell source after its zero-cost and no-payer exits and before every other branch, with the
  whole cost: a fatigue-limiting critter's halving never applies, and no fatigue, mana store or item charge is touched.
- A critter (or any payer that is not an item) pays from itself; an item (types 5..14) from its parent (`CellOwnerKind`): a
  critter parent, a container, no parent, or another object.
- Cells are ammunition of type 2 (prototype 7060). A critter has one power-cell stack (the original merges ammunition into
  one stack per type); a container's cells would be the first item of prototype 7060 in its inventory, but the original's
  lookup passes the raw prototype number where a handle is compared and never finds it (ledger [AM6](../research/original-verification-ledger.md#ammunition-arc-world-066),
  ARC-WORLD-066), so a container has none in both settings until the owner decides; no parent or another object
  has none. The host finds the stack and gives it as `CellStack(entity, units)`.

## The need (CC4, TC1)

| Payer | Cells needed |
| --- | --- |
| a critter or another object | the cost |
| a neutral item (complexity 0) | the cost |
| a magickal item (complexity c > 0) | `2 × cost − trunc(cost × p / c)`: a mage holder (p = c) the cost, a technologist (p = 0) twice |
| a technological item (c < 0), **Original** | `cost + trunc(cost × p / c)`: a technologist (p = c) twice, a mage (p = 0) the cost |
| a technological item, **Steamwoven** (TC1) | `2 × cost − trunc(cost × p / c)`: a technologist the cost, a mage twice |

p is the item's power for a critter parent (`trunc((aptitude + c) / 2)` clamped to the item's side, `EquipmentRules.EffectivePower`)
and c itself for any other parent. TC1 is `TechnologyRuleProfile.ItemCellsFavourMagick` (Original true), owner decision
2026-10-20. The installed carriers are technological: record 147 on one weapon (complexity −90), 168 on three armours (−80);
187 on no item.

## The plan (CC5, CC6)

`ChargeCellRules.Plan(payer, cost, apply, profile)` returns a `CellPlan`: paid or refused (`TooFewCells`, `OutOfRange`), the
need, the stack, its units after and whether it is removed (its last units taken, as the original destroys the stack). Too few
cells take nothing. `ChargeCellSource` is the engine's port over one payer: each `Pay` returns `SpellCellPayment(paid, need)`
and keeps the plan (`LastPlan`).

The original takes the cells even for an affordability check that does not apply the payment (CCA1: the interface's check before
a cast and the animation goal's check). Both settings answer such a request exactly as the original does and write nothing
(`CellPlan.Applied` false, `Writes` false).

Crash class, fixed in both settings (CCA2): a need or a product `power × cost` that does not fit 32 bits (the original wraps
and can add cells or fault on a division), and a complexity or aptitude out of range, are refused (`OutOfRange`). An item
without a parent (CCA3, the original reads an undefined object) or another owner counts no cells (`TooFewCells`). A payment whose
payer kind differs from the source's payer is a host wiring error (`PayerMismatch`).

## Session

`GameSessionOwner.QueryHeroCellPayer(cells)` (the hero pays from its stack) and `QueryHeroItemCellPayer(complexity, cells)` (an
item the hero holds, with the hero's magick/tech aptitude) give the payer facts, null without a hero; `CellSource(payer)` gives
the port under the session's technology rules. None of them changes the session. ARC-WORLD-066 adds `HeroCellSource(itemComplexity?)`:
the port over the hero's own power-cell stack, which spends the cells of an applied, paid plan through `SpendAmmo` (type 2) and
writes nothing for an unapplied request ([ammunition](inventory-runtime.md#ammunition-arc-world-066)).

## Not implemented

- An NPC payer's cells written in session state (the host can spend them with `SpendAmmo` for the NPC); the critical misses'
  "destroy" and "lose ammunition" outcomes (the combat lane, through `SpendAmmo`).
- An NPC payer's aptitude and stack (the host can build a `CellPayer` itself); the multiplayer client forward.
- Which tech records reach the engine and the maintain test: done by ARC-TECHNOLOGY-008 ([tech devices](tech-devices-runtime.md)).

# Item durability runtime

Status: **Implemented bounded component**, work item ARC-WORLD-053 (2026-10-20), the item durability slice of the
[technology research](../research/technology-evidence.md#proposed-implementation-slices-project-policy-not-accepted).
Godot-free durability rules in `Arcanum.Simulation.Items` and the session's item condition state in `Arcanum.Session`
(snapshot schema 24). No viewer, combat or trade caller uses them yet.

Requirements: [FR-ITEM-011](../requirements/items-economy.md#fr-item-011) (durability, break and the item side of repair).
The Repair skill rule itself is ARC-SKILLS-011 (skills plan), which calls the item side below.

Every rule was read in the retail 1.0.7.4 `Arcanum.exe` by two independent readers: the first reading of ARC-RES-062
([item durability](../research/technology-evidence.md#item-durability-fr-item-011)) and a second reading for this slice
([ledger "Item durability"](../research/original-verification-ledger.md#item-durability-arc-world-053), rows DU1–DU8). The
registry rows ([functions](../verification/functions.csv)) are at F2.

Code: [`ItemDurabilityRules`](../../src/Arcanum.Simulation/Items/ItemDurabilityRules.cs),
[`ItemRuleProfile`](../../src/Arcanum.Simulation/Items/ItemRuleProfile.cs),
[`GameSessionDurability`](../../src/Arcanum.Session/GameSessionDurability.cs); tests
[`ItemDurabilityRulesTests`](../../tests/Arcanum.Simulation.Tests/Items/ItemDurabilityRulesTests.cs),
[`ItemDurabilityStateTests`](../../tests/Arcanum.Session.Tests/ItemDurabilityStateTests.cs),
[`ItemDurabilityRealDataTests`](../../tests/Arcanum.Session.Tests/ItemDurabilityRealDataTests.cs).

## Condition

`ItemCondition(Points, Adjustment, Damage, Broken, InventoryArtBroken)` holds the saved object fields hp_pts (27), hp_adj
(28), hp_damage (29) and the "destroyed" bit (0x400) of the current art (`Broken`, which every broken test reads) and of
the inventory art (`InventoryArtBroken`, which only the inventory icon shows). The host builds the baseline from an item's
data with `SessionItemConditions.FromCombatFields` (a prototype, or an instance's fields over its prototype,
ARC-DATA-021).

- **Maximum** = hp_pts + hp_adj; an item has no effects and no clamp (0x43D5A0; its critter branch is
  `LifeRules.MaxHitPoints`).
- **Current** = maximum − damage, which may be 0 or less (0x43D600).
- **Damage setter**: a negative value is stored as 0; the setter never breaks the item (0x43D530).
- **Damage step** (`AddDamage`): the damage grows by the amount; at 0 hit points or less the item breaks.
- **Break** (0x43F1C0, item types 5..14): the first break sets the destroyed bit on the current and the inventory art and
  resets the damage to 0 (a broken item reads as healthy); the holder takes it off its wear slot (dropped when it cannot go
  back to the inventory), the bust sound plays and the BUST script (point 30) runs with the breaker as triggerer. A second
  break of an item whose current art carries the bit deletes it, with the destroy sound and no script.

## What broken blocks

| Rule | Broken item |
| --- | --- |
| Wear (ledger EQ1, `EquipmentRules.CheckWear`) | refused; the session passes its record of the condition to the check |
| Sale (0x462170) | refused, price 0 (also for a worth of 0) |
| Schematic component search | skipped (`BlocksSchematicUse`, for TECH-4) |
| Use and item spells | **not** blocked (TC9, the original's behaviour in both modes) |

## Price and repair quote

- **Sale price** (0x461F80): only when the PC sells and the item lost hit points does the price the trade owner computed
  (worth, the buyer's multiplier, the haggle share) become `current × price / maximum`, truncated; then, in that branch, a
  negative price is 0 and a price of exactly 1 is 2. Buying from an NPC ignores damage (its own bounds are the trade owner's).
- **Repair quote** (0x578760), from the undamaged PC sale price the trade owner passes: a broken item is quoted only by a
  master of Repair, with the damage taken as the maximum (so the quote is `P / 2`), else refused; damage 0 is "not
  needed"; otherwise `trunc(P / 2) × damage / maximum`, at least 2.

  | Mode | P |
  | --- | --- |
  | Original | the damage-scaled sale price: damage counts twice, so a badly damaged item is nearly free and the dearest repair is half damaged (TC7) |
  | Steamwoven | the undamaged sale price: the cost grows with the damage (owner decision 2026-10-20) |

  The switch is `ItemRuleProfile.RepairQuoteCountsDamageTwice` in `GameRules.Items`
  ([rule modes](rule-modes.md#registry)).

## Repair, item side

`ItemDurabilityRules.Repair(condition, amount, adjustmentPercent, repairer)` (0x4C8E60, the repair case of the skill run):

- nothing for an item without damage unless it is broken and the repairer a master (`NothingToRepair`);
- otherwise the damage falls by `amount` (to 0 at least) and hp_adj by `trunc(maximum × percent / 100)` of the maximum
  before the repair; a master's repair clears the destroyed bit of the **current** art only. A broken item that took damage
  again is repaired by any repairer and stays broken unless the repairer is a master: only the quote turns a broken item away
  from a repairer who is not a master.
- `RepairAmount`: the whole maximum with the PC's fate full repair, else `maximum × effectiveness / 100`, at least 1 (the
  service forces success). `RepairAdjustmentPercent`: the repairer's training none 10, apprentice 5, expert 1, master 0,
  and 5 for a broken item. The payment, made whatever the skill gives, is the trade owner's.

## Session

The session tracks every item it knows: the hero's kit, the items the PC took, and the items of every attached map seed
(any map, any holder or the ground). An item keeps its host's baseline until the session changes it:

- `QueryItemCondition(entity, baseline)`: the record, or the baseline; null for a deleted item. `IsItemDestroyed`,
  `QueryDurability`.
- `DamageItem(expected, entity, baseline, amount)`, `BreakItem`, `DestroyItem` (the "destroy weapon" critical: deleted at
  once without a break), `RepairItem(expected, entity, baseline, amount, percent, repairer)`. Each is one revision;
  refusals are typed (`InvalidItem`, `StaleVersion`, `ItemNotAvailable` for an item the session does not track or deleted,
  `BudgetExceeded` past `GameSessionLimits.MaxDurabilityRecords`, 8,192 by default). The result (`ItemDurabilityResult`) names the condition, the break step and, for
  an item the hero wore, the slot it left.
- A worn item's values follow its record; a first break takes it off the hero's slot into the inventory, where it already
  counts. For an item an NPC wears, taking it off is the host's.
- A deleted item leaves its wear slot and the PC's lists (a deleted kit item leaves its `kit:n` number out), is hidden
  from every seed view and from pickup and loot, and keeps no record.
- `Equip` and `EquipBest` read the record's broken bit and hit points over the host's data.

### Snapshot (schema 24)

`"durability":{"items":[{"entity","points","adjustment","damage","broken","inventoryArtBroken"}],"destroyed":[entity]}`,
both in strictly ascending ordinal order. Schemas 22 and 23 are left to the slices the coordinator reserved them for;
schemas 1..23 are refused, not migrated. The decoder refuses a list out of order or with a repeated entity
(`NotCanonical`), a value outside the bounds (points and adjustment within ±100,000, damage 0..200,000: `OutOfRange`) and a
missing or unknown field. Restore refuses (`InvalidDurability`) a deleted item that has a record and a worn item whose record
is broken or disagrees with its worn values, more records than the limit (`BudgetExceeded`), and a kit or PC item that is
deleted or a kit numbering that only an unlisted deletion would explain (`InvalidSnapshot`).

## Callers

The attack resolver produces no armour or weapon wear yet (combat core C20, C21), and the session's hero attack does
not damage items, so no combat caller exists; the damage entry point is exposed for the combat item-damage work. Trade
(sale price, quote and payment), the Repair skill (ARC-SKILLS-011) and the schematic gate (TECH-4) call the rules when they
exist.

## Differences from the original

| Original 1.0.7.4 | Steamwoven (both modes unless the switch says) |
| --- | --- |
| The repair quote counts damage twice (TC7). | A switch: Steamwoven quotes from the undamaged price. |
| The price product `current × price` is 32-bit and could wrap for a price beyond any installed item. | Exact (prices are bounded to 10⁹). |
| hp_adj is lowered by a repair without a clamp. | Kept within ±100,000, the rules' bounds. |
| A worn item that cannot go back to the holder's grid is dropped on a break. | The hero's broken item goes to the inventory, where it already counts; dropping is not modelled. |

## Not ready

- No combat damage on items, armour or weapon wear, area damage or script action 60 reach the session yet.
- No trade, price function, repair window or payment; no Repair skill run (ARC-SKILLS-011).
- The bust and destroy sounds and the BUST script are reported to the host, not played or run here.
- Breaking walls, portals, containers, scenery and traps (other branches of 0x43F1C0) is not implemented.
- No viewer shows item condition or broken art.

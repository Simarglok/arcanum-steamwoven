# Item durability runtime

Status: **Implemented bounded component**, work item ARC-WORLD-053 (2026-10-20), the item durability slice of the
[technology research](../research/technology-evidence.md#proposed-implementation-slices-project-policy-not-accepted).
Godot-free durability rules in `Arcanum.Simulation.Items` and the session's item condition state in `Arcanum.Session`
(snapshot schema 22). Combat calls them since ARC-COMBAT-007 (attack wear) and the hero's Repair skill since ARC-SKILLS-007, which
the AREA viewer's inventory window runs with the kit's and the object rows' baselines; no trade caller uses them yet.

Requirements: [FR-ITEM-011](../requirements/items-economy.md#fr-item-011) (durability, break and the item side of repair).
The Repair skill's own run is ARC-SKILLS-007 ([repair](repair-runtime.md)), which calls the item side below.

Every rule was read in the retail 1.0.7.4 `Arcanum.exe` by two independent readers: the first reading of ARC-RES-062
([item durability](../research/technology-evidence.md#item-durability-fr-item-011)) and a second reading for this slice
([ledger "Item durability"](../research/ledger/items.md#item-durability-arc-world-053), rows DU1–DU8). The
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
  service forces success; its repairer is an NPC, so no fate applies there, ledger RP3). `RepairAdjustmentPercent`: the repairer's training none 10, apprentice 5, expert 1, master 0,
  and 5 when a master mends a broken item (ledger RP2: a repairer who is not a master keeps the table). The payment, made
  whatever the skill gives, is the trade owner's. The skill's own run is not forced: `SkillRepairAmount` restores nothing at an
  effectiveness of 0, and `Repair` then still takes the share (ARC-SKILLS-007, [repair](repair-runtime.md)). Ledger RP2 places
  this item part in the run's own repair case, which the service reaches through a forced run.

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

### Snapshot (schema 22)

`"durability":{"items":[{"entity","points","adjustment","damage","broken","inventoryArtBroken"}],"destroyed":[entity],"uses":[{"entity","uses"}]}`,
each in strictly ascending ordinal order. Schemas 1..21 are refused, not migrated. Since schema 31 (ARC-SKILLS-005) `uses` holds the
remaining uses of every item whose uses the session spent (1..1,000,000; a used-up item is deleted), counted in the same record
budget ([heal runtime](heal-runtime.md)). The decoder refuses a list out of order or with a repeated entity
(`NotCanonical`), a value outside the bounds (points and adjustment within ±100,000, damage 0..200,000: `OutOfRange`) and a
missing or unknown field. Restore refuses (`InvalidDurability`) a deleted item that has a record and a worn item whose record
is broken or disagrees with its worn values, more records than the limit (`BudgetExceeded`), and a kit or PC item that is
deleted or a kit numbering that only an unlisted deletion would explain (`InvalidSnapshot`).

## Callers

Combat (ARC-COMBAT-007): `GameSessionOwner.ApplyAttackWear`, which the hero's attack (`AttackCritter`) runs and NPC AI
will run, calls `DamageItem` for armour wear (+ 10 at the hit location), the critical "damage weapon" (+ 10) and the
attacker's weapon wear by the struck material, and `DestroyItem` for the "destroy/explode weapon" critical
([attack wear](attack-resolver-runtime.md#wear-arc-combat-007), ledger CW1–CW10). The hero's attack on an item on the ground
(`AttackObject`, ARC-COMBAT-008, [object damage](object-damage-runtime.md)) damages it through `DamageItem`. Trade
(sale price, quote and payment) and the schematic gate (TECH-4) call the rules when they exist; the Repair skill's run calls
`SkillRepairAmount`, `SkillRepairAdjustmentPercent` and `Repair` since ARC-SKILLS-007 ([repair](repair-runtime.md)).

## Differences from the original

| Original 1.0.7.4 | Steamwoven (both modes unless the switch says) |
| --- | --- |
| The repair quote counts damage twice (TC7). | A switch: Steamwoven quotes from the undamaged price. |
| The price product `current × price` is 32-bit and could wrap for a price beyond any installed item. | Exact (prices are bounded to 10⁹). |
| hp_adj is lowered by a repair without a clamp. | Kept within ±100,000, the rules' bounds. |
| A worn item that cannot go back to the holder's grid is dropped on a break. | The hero's broken item goes to the inventory, where it already counts; dropping is not modelled. |

## Not ready

- Area damage and script action 60 do not reach the session yet; armour and weapon wear (ARC-COMBAT-007) and the hero's hit on
  an item on the ground (ARC-COMBAT-008) do, but the viewer passes no item baselines yet.
- No trade, price function, repair window or payment; the Repair skill's run is ARC-SKILLS-007.
- The bust and destroy sounds and the BUST script are reported to the host, not played or run here.
- Breaking walls (a branch of 0x43F1C0 no attack reaches) is not implemented; portals, containers, scenery and traps break in
  the [object damage](object-damage-runtime.md) domain (ARC-COMBAT-008).
- No viewer shows item condition or broken art.

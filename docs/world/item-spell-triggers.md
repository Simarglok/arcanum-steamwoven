# Item spell triggers (ARC-MAGIC-020)

Status: **Implemented bounded component; no viewer caller yet.** Work item ARC-MAGIC-020 (2026-10-28), the first part of the shared
item trigger slice of [FR-MAGIC-011](../requirements/combat-magic-tech.md#fr-magic-011) ("Outside this plan" of the
[magic implementation plan](../research/magic-implementation-plan.md#outside-this-plan)). Behaviour is the 1.0.7.4 executable's,
read twice ([ledger IT1–IT9](../research/ledger/spell-runs.md#item-spell-triggers-arc-magic-020)); the item spell fields and the
technology side are the [tech devices](tech-devices-runtime.md) contract's (DV1–DV3).

This slice runs an item's spells on its **wear and unwear** through the [cast host](spell-cast-host.md). The other events are later
slices (see [Not ready](#not-ready)).

## Rules (`ItemSpellTriggerRules`, Simulation)

- **Slots.** An item has five spell fields (0x64..0x68; 10000 none) and a charge count (0x69; negative unlimited, 0 none). The walk
  takes the slots in order and stops at the first 10000, so an empty slot 0 fires nothing (IT1).
- **Match.** A slot fires when its record's `ItemTriggers:` mask, read for the slot's whole value, shares a bit with the event's
  mask; the run's spell is the value's low byte (LM7).
- **Chance** (`Fire` only). A mask with a chance bit rolls d100 (label `magic.ItemTriggerChance`) and fires at or below Rare 10,
  Uncommon 20, Common 40 or Frequent 66, the first bit in that order (IT2).
- **Aim.** The other object (`Target_Attacker`, so `Item_Used`), its weapon or armour slot, nothing for the melee bit alone, the tile
  for `Parent_Atks_Location`, else the holder (IT3). No target object and no tile start no run.
- **Fire** (0x4CB830): each firing becomes a run without the casting goal (goal 73), with the event's mask as the run's triggers, which
  gate the record's components (IT4).
- **Withdraw** (0x4CBFF0, unwear and drop): no chance; per match the first run whose parent is the holder, whose target is the holder
  and whose spell is the slot's is interrupted (the source is not compared, IT6), then the spell runs again at the holder with the
  event's mask and the casting goal (IT5).
- Event masks for every wrapper are constants of the rules (IT7); only wear and unwear have callers here.

## Session (`SessionSpellCaster`, `SessionItemTriggers`)

- **Items as objects.** A cast host built with an item lookup (`Func<string, SessionItemSpells?>`: entity, holder, type, slots,
  charges, complexity; `SessionItemSpells.FromCombatFields` for original data) answers an item its holder carries as an object at
  the holder's location with the inventory flag, owner the holder. Its run object is not a critter; it pays as an item (charges, the
  payment's item branch); a record with `ChargesFromCells:` takes the cells of the hero's stack for an item the hero holds (TC1 need
  from the item's complexity).
- **`StartItemSpells`** fires an event for one item and holder and starts the runs: source the item, parent the holder, the item power
  `EquipmentRules.EffectivePower` for the invocation check, `SkipCastGoal` and the event mask as triggers. Outcomes: started, no
  target, not ready (an attacker's worn weapon or armour), refused (the creation's answer kept).
- **`WithdrawItemSpells`** interrupts the matching run (`SessionSpellRunActionKind.Interrupt`) and starts the recast with the casting
  goal.
- **`SessionItemTriggers`** (`ISessionItemTriggers`) joins a host and its combat world for the session's moves: `Supports` (the host
  knows the item and its charges need no saved state: unlimited or none), `Fire`, `Withdraw`. It plays each started run's goal at
  once: the goal's start (`StartGoal`, 4 action points in a turn-based round), then the release (`Release`, its BEGIN). No eye-candy
  art paces it. A withdrawal's recast ends its goal (`EndGoal`). A fired run's goal 73 has no end node: when its start refuses, the
  original leaves the run in its slot, never begun; Steamwoven frees it unpaid under MG39's switch
  (`MagicAnomalyProfile.GoalRefusalChargesBegin`, owner decision 2026-10-27 for refused casting goals).
- **Equipment.** `Equip` and `Unequip` take an optional `ISessionItemTriggers`. Without one, as before ARC-MAGIC-020, an item with
  item spells is refused (`ItemEffectUnsupported`), and so is a move of an item that is the source of a run. With one, an item with
  spells moves when the host supports it; after the move (and its item event scripts) come, in order, the unwear of the item when it
  left another slot, the unwear of the occupant it displaced, and the item's wear (`EquipmentMutationResult.ItemSpells`); `Unequip`
  runs the unwear. A move that rolled back fires nothing.

## Installed data

The census (`ItemSpellCensusRealDataTests`, opt-in `DAT_ROOT`) finds four wear items, all armour with unlimited charges and no move
scripts: 8168, 8169 and 8170 carry record 168, a maintained technology heal that pays cells (its MAINTAIN components are gated by
`Wear`), and 8207 carries record 182, whose BEGIN sets object flags on the wear and clears them on the unwear. Nothing fires on pickup
or drop. Of the components, Heal runs; NoOp, TestNBranch, ChargeNBranch, ObjFlag are not executed yet (MT13–MT18: logged as not
ready), so record 168 heals without paying cells and record 182 changes no flag. The unwear's recast of record 168 is maintained too;
its MAINTAIN aims at the run's target (the holder) and its components are gated by `Wear`, so its first tick applies nothing and ends
it, as in the original (a MAINTAIN with an empty target list would count as applied and keep the run).

## Differences from the original

| Original | Steamwoven | Switch |
| --- | --- | --- |
| The wear and unwear executors run inside the wield and unwield (0x4677B0, 0x467CB0), before the light recalculation and script points 24/25 (IT8). | They run after the move and its scripts. No installed wear item has a move script. | — (order of the session's move) |
| A fired run whose goal 73 refuses at its start stays in its slot, never begun. | Freed unpaid. | MG39 (`GoalRefusalChargesBegin`) |
| The casting goal's release waits for the caster's eye candy (EC7). | The goals of item spells are released at once. | — (presentation, not ready) |

## Not ready

- Pickup, drop and item use (`Item_Used`, 42 installed slots, charges 1 mostly): the session's inventory moves and the use need the
  executor wired and a saved charge count; an item with a positive charge count is not supported here.
- The combat and life events (parent hit, stunned, going unconscious, dying, attacks, damages, target hit) and the global attacker.
- The item spell pane and hotkeys (0x57C040, 0x57C080, IT9), and spells aimed at an inventory item.
- The best-items start (`EquipBest`) skips items with spells, as before: it runs before any cast host exists.
- The viewer has no equip action, so nothing calls these executors there yet; the session's runs are not saved (MT9b), so a loaded
  game wears its items without their runs.

# Item spell triggers (ARC-MAGIC-020)

Status: **Implemented bounded component; the viewer passes the triggers on wear and take-off (ARC-UI-015) but its cast host has no item lookup yet.** Work items ARC-MAGIC-020, ARC-MAGIC-022 and ARC-MAGIC-023 (2026-10-28), the first parts of the shared
item trigger slice of [FR-MAGIC-011](../requirements/combat-magic-tech.md#fr-magic-011) ("Outside this plan" of the
[magic implementation plan](../research/magic-implementation-plan.md#outside-this-plan)). Behaviour is the 1.0.7.4 executable's,
read twice ([ledger IT1–IT10](../research/ledger/spell-runs.md#item-spell-triggers-arc-magic-020), [IC1–IC6](../research/ledger/spell-runs.md#combat-and-life-item-triggers-arc-magic-023)); the item spell fields and the
technology side are the [tech devices](tech-devices-runtime.md) contract's (DV1–DV3).

This slice runs an item's spells on its **wear and unwear** and (ARC-MAGIC-022) its **use, pickup and drop** and (ARC-MAGIC-023) the **damage step of the hero's attack on an NPC** through the [cast host](spell-cast-host.md),
and saves the charges the spells spend. The other events are later slices (see [Not ready](#not-ready)); the use is read in ledger
[IU1–IU10](../research/ledger/spell-runs.md#item-use-and-moves-arc-magic-022).

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
- Event masks for every wrapper are constants of the rules (IT7); the wear, unwear, pickup, drop, item used and the damage step's
  events have callers here.

## Session (`SessionSpellCaster`, `SessionItemTriggers`)

- **Items as objects.** A cast host built with an item lookup (`Func<string, SessionItemSpells?>`: entity, holder, type, slots,
  charges, complexity and, since ARC-MAGIC-022, the item flags; `SessionItemSpells.FromCombatFields` for original data) answers an item its holder carries as an object at
  the holder's location with the inventory flag, owner the holder. Its run object is not a critter; it pays as an item (charges, the
  payment's item branch); a record with `ChargesFromCells:` takes the cells of the hero's stack for an item the hero holds (TC1 need
  from the item's complexity).
- **`StartItemSpells`** fires an event for one item and holder and starts the runs: source the item, parent the holder, the item power
  `EquipmentRules.EffectivePower` for the invocation check, `SkipCastGoal` and the event mask as triggers. Outcomes: started, no
  target, not ready (an attacker's worn weapon or armour), refused (the creation's answer kept).
- **`WithdrawItemSpells`** interrupts the matching run (`SessionSpellRunActionKind.Interrupt`) and starts the recast with the casting
  goal.
- **`SessionItemTriggers`** (`ISessionItemTriggers`) joins a host and its combat world for the session's moves: `Supports` (the host
  knows the item as the hero's; its charges are saved, see [Saved state](#saved-state-arc-magic-022)), `Fire`, `Withdraw`. It plays each started run's goal at
  once: the goal's start (`StartGoal`, 4 action points in a turn-based round), then the release (`Release`, its BEGIN). No eye-candy
  art paces it. A withdrawal's recast ends its goal (`EndGoal`). A fired run's goal 73 has no end node: when its start refuses, the
  original leaves the run in its slot, never begun (IT10); both rule modes free it unpaid (owner decision 2026-10-28: a slot leak,
  fixed without a switch).
- **Equipment.** `Equip` and `Unequip` take an optional `ISessionItemTriggers` of the same session. Without one, as before
  ARC-MAGIC-020, an item with item spells is refused (`ItemEffectUnsupported`), and so is a move of an item that is the source of a
  run in the table or (ARC-MAGIC-022) that carries the item spells mark. With one, an item with spells moves when the host supports it; after the move (and its item event scripts) come, in order, the unwear of the item when it
  left another slot, the unwear of the occupant it displaced, and the item's wear (`EquipmentMutationResult.ItemSpells`); `Unequip`
  runs the unwear. A move that rolled back fires nothing.

## Saved state (ARC-MAGIC-022)

- **Charges.** The payment's item branch spends one of a positive charge count when it commits and sets the item's magical flag
  (0x4) with it (0x450420, ledger DV1). The cast host saves that spend: `SpendItemCharge` keeps the charges left by entity
  (`QueryItemCharges`; the item's own field is the baseline until the session holds a count), and the host's item lookup reads the
  saved count and, for an item with a row, the magical flag (`SessionItemSpells.Magical`). A row is only made by a spend, so it also
  stands for the flag; it goes with the item when the session deletes it. The original also sets the flag when the payment is only
  asked (a cost check) for an item with charges; that is not visible, because an item can only reach 0 charges by a spend that
  sets the flag, and the flag is read only at 0 charges (the use's spell path, the AI's fail-chance predictor RS6).
- **Mark.** A worn item that carries item spells and was worn through a trigger host keeps the mark `SessionWornItem.ItemSpells`;
  without a host `Equip` and `Unequip` refuse a move of a marked item or occupant (`ItemEffectUnsupported`), so a loaded game (whose
  runs are not saved, MT9b) still takes the item off through a host and runs its unwear. The other ways a worn item leaves run no
  executor yet and refuse a marked item: giving it to an NPC and the throw (`ItemEffectUnsupported`), the best-items start's take-off
  and wield (it never marks, skipping items with spells) and a critical drop (step `ItemSpellsUnsupported`). A first break still
  takes it off without its unwear (not ready).
- **Batches.** The notes of an event pass are applied after the pass, so two charge payments of one pass are both made against the
  count before it; one that then finds no charge left spends nothing, where the original would refuse it. A spend the session
  refuses (a full record budget) is logged as `ChargeNotSaved`.
- Both are in snapshot schema 49 ([game session](game-session-runtime.md#snapshot-schema-51)): `durability.charges` (`entity`,
  `charges` 0..1,000,000, ordinal order, within the durability budget) and each worn item's `itemSpells`.

## Item use (ARC-MAGIC-022)

`SessionItemTriggers.UseItem(item, target)` is the spell branch of the hero's item use (0x462CC0, IU1). The host runs the use's
earlier steps first, as the original does: the scroll test (`SessionSpellCaster.ScrollTooHard`: a scroll's spell needs intelligence
5, 0x4B1750, and a PC is told message 13), the targeting cursor for an item that needs a target (item flag 0x100) and has none, a trap
device's own use, and the use script, which may stop the use. Outcomes: the host does not know the item as the hero's (`Unknown`);
no charges and no magical flag (`NoMagic`: the reading, lockpick, healing kit and spending branches stay the host's, with their 4
action points); else `Used`, in this order (the use charges no action points; each casting goal charges its own 4 in a turn-based
round, so a goal refused there frees its run while a food or scroll is still spent):

1. **Slot 0** (0x4605E0 → the callback 0x57CE30, IU2–IU3): nothing for an empty slot (`NoSpell`; the original reads outside the
   record table, IU6) or a record with item triggers (`Triggered`). Otherwise the activation (`SpellUiRules.Activate`, 0x57BC70, IU4)
   with the holder as caster and no Shift or Alt (the interface mode change it makes is the host's): `Nothing` when the hero cannot act or the spell is disabled; a cast at once at the
   holder's tile (`AtCaster`, the use's target unread); otherwise, whether the cursor would open or the action points refuse, the cast
   at the use's target (`AtTarget`). Both go through `SessionSpellCaster.CastAt` with the item as source (IU5: the holder is the
   parent, the power it draws from the item; an item source skips the cost check), and the casting goal plays at once with its end,
   so the BEGIN's payment spends the item's charge (saved, see below). A spell aimed at an inventory item (`InventoryTarget`) is not
   ready: nothing is cast, where the original opens the inventory and casts at the use's target.
2. **Item used** (0x4CBF70, IU7): the executor with `ItemUsed` (0x1000) and the use's target as the other object; its runs play as
   the other executors' (goal 73).
3. **Spend**, food and scrolls only (`SessionSpellCaster.SpendUsedItem`): the session deletes the item (`GameSessionOwner.DestroyUsedItem`,
   refused for an item the hero does not hold or wears, that is not food or a scroll, or past the durability budget), then the source
   of every run whose parent or source is the item is cleared (0x4574D0, IU8; the original clears first, and nothing reads the runs
   in between); a refused deletion clears nothing. Its destroy script is the host's. A scroll is spent whatever its slot-0 cast did.

The installed uses ([census](#installed-data)): 84 scrolls (69 cast at a picked target, 13 at once, 1 at an inventory item, 1 through
`Item_Used`), 43 food items (41 `Item_Used` potions, 2 at a picked target), 3 weapons with 20 charges and one unlimited (at a picked
target), and 22 items with unlimited charges whose slot 0 has item triggers (wear items and `Item_Used` devices).

## Pickup and drop (ARC-MAGIC-022)

The hero's inventory moves take an optional `ISessionItemTriggers` like `Equip` (results in `InventoryMutationResult.ItemSpells`):

- **Pickup** (0x4CBAA0 from the insertion 0x466640, IU10): after `PickUpItem`, `LootItem`, `GiveItemToPc` and `TakeItemFromNpc` moved
  an item into the hero's list, the executor fires with `ItemSpellTriggerRules.Pickup` and the hero as holder (after the move and its
  scripts; in the original after the item is listed, before its insert-item script). An ammunition item merged into a stack is
  deleted and fires nothing, as in the original.
- **Removal** (0x467860, IU9): after `GiveItemToNpc` moved an item out, `ISessionItemTriggers.Remove` (`SessionSpellCaster.RemoveItemSpells`)
  ends by event the runs the item is the source of, unless technological or with item triggers and no maintenance period (0x457450),
  then runs the drop's withdrawal (0x4CC130) with the hero as holder: the item counts as the hero's while it runs, as the original
  passes the holder and leaves the item's parent field (also while the recast's goal plays). Without a host a give of an item that is
  the source of a run is refused (`ItemEffectUnsupported`), and so is a barter sale of one (`BarterSell` gives without a host). An
  ammunition item merged into the NPC's stack is deleted before these steps (the original removes it first); no installed ammunition
  carries item spells.
- Without a host the moves run no executor; no installed record fires on a pickup or a drop (census), so only the removal's ending
  of runs is visible in the installed game. No viewer or dialog caller passes a host yet (the viewer's pickup and loot, the dialog
  `IN` port).

## Combat events (ARC-MAGIC-023)

The combat and life wrappers each walk their parent's worn items (slots 1000..1008 in order) and call the executor with the event's
other object (IC1). `ItemSpellTriggerRules` (Simulation) holds them as rules:

- **`DamageStep`** lists the wrapper calls of one damage step (0x4B4390, IC2) on a critter, in its order: nothing for an invulnerable or
  already dead critter; **stunned** (the target's items, other the attacker) for a stun effect on a critter not yet stunned without a
  knockdown or knockout, before the stun's own start, so also when that start fails; for a knockdown or knockout **going unconscious**
  (the target's, other the attacker) and **target going unconscious** (the attacker's, other the target); **going unconscious** with no
  other object when the step takes a conscious critter to 0 fatigue (0x45D5A0, IC4: it reads the fatigue only, so also when the hit
  kills); **dying** with the kill's damage source (IC5) when the critter dies; then, with an attacker, **parent hit** (the target's,
  other the attacker) and **damages opponent** (the attacker's, other the target).
- **`WornCalls`** is a wrapper's walk: one call per worn item with the event's mask; for damages opponent each item takes
  `Parent_Dmgs_Opponent` then `Target_Hit`, and the attack's weapon also `Parent_Dmgs_Opponent_W_Item` (0x4CBE70).
- **`MeleeHit`**: the parent hit carries `Target_Attacker_Weapon_Melee` for an attack entry's context (never a spell's damage) whose
  weapon is none or has a reach of at most 1 (0x4CBAD0). Since a slot fires on any shared bit, the bit changes only which records with
  that bit alone fire, and those start no run (IT3).

The session fires them for the hero's attack on an NPC (`AttackCritter`, `LandShot`) through `SessionAttack.ItemTriggers`, a trigger
host of the same session (`ISessionItemTriggers.Fire` now takes the event's other object, the aim of `Target_Attacker`); the executor
calls and their results are `SessionAttackResult.ItemSpells` (`SessionItemTriggerFiring`). In the step's order, they fire after the
life and experience steps: the stun's and the knockdown's walks (and a going unconscious the knockout caused) before the attack's
critical drops and wear, so over the NPC's items as the host passed them (`SessionAttack.TargetWorn`), the others after them, over the
NPC's items without those this attack's drops or breaks took off (IC3); the hero's items walk as it wore them before the attack's
weapon wear. The host plays each run's goal at once, so a run begins (and its BEGIN acts) before the next executor call. The
context's weapon is the wielded weapon, or a shot's item (kept with the shot: the weapon wielded at the shot, or the thrown item,
which the hero no longer wears); the melee bit reads the reach of the weapon the swing or the shot carries (a throw keeps the
weapon's reach). An NPC's items fire only when the host's item lookup knows them, with their holder the NPC; their spent charges are
saved by entity as the hero's are.

Not fired here: a critical miss that hurts the hero (its damage step's context is not read), an attack whose experience or item step
the session refused (from that step on), the damage step without a host or (for the NPC's items) without the host's item data.

## Installed data

The census (`ItemSpellCensusRealDataTests`, opt-in `DAT_ROOT`) finds four wear items, all armour with unlimited charges and no move
scripts: 8168, 8169 and 8170 carry record 168, a maintained technology heal that pays cells (its MAINTAIN components are gated by
`Wear`), and 8207 carries record 182, whose BEGIN sets object flags on the wear and clears them on the unwear. Nothing fires on pickup
or drop. Of the combat events (ARC-MAGIC-023 census) only the damage step's are used, all with unlimited charges and no chance bit:
weapon 6130 (record 147, `Target_Hit|Target_Attacker`: a damage that pays cells through ChargeNBranch), weapons 6175 and 6179 (records
203 and 204, `Parent_Dmgs_Opponent_W_Item|Target_Attacker`: a Cast component, not executed yet, and a damage) and armour 8215
(record 87, `Parent_Hit|Target_Attacker|Target_Attacker_Weapon_Melee`: a damage at the attacker). The 14 `Parent_Atks_Location`
records are thrown items' (food 10105, 13 generic items). Of the components, Heal runs; NoOp, TestNBranch, ChargeNBranch, ObjFlag are not executed yet (MT13–MT18: logged as not
ready), so record 168 heals without paying cells (its maintenance cost is 0, so the stage pays nothing; its cells are the unexecuted
ChargeNBranch's) and record 182 changes no flag. The unwear's recast of record 168 is maintained too;
its MAINTAIN aims at the run's target (the holder) and its components are gated by `Wear`, so its first tick applies nothing and ends
it, as in the original (a MAINTAIN with an empty target list would count as applied and keep the run).

## Differences from the original

| Original | Steamwoven | Switch |
| --- | --- | --- |
| The wear and unwear executors run inside the wield and unwield (0x4677B0, 0x467CB0), before the light recalculation and script points 24/25 (IT8). | They run after the move and its scripts. No installed wear item has a move script. | — (order of the session's move) |
| A fired run whose goal 73 refuses at its start stays in its slot, never begun (IT10). | Freed unpaid in both modes. | none (owner decision 2026-10-28: fixed in both modes) |
| The casting goal's release waits for the caster's eye candy (EC7). | The goals of item spells are released at once. | — (presentation, not ready) |
| The damage step's executor calls run inside the step, and their goal 73 runs begin after it (IC2). | The stun's and knockdown's calls run before the attack's drops and wear, the others after them, each walk over the items as they were at its point (IC3); each run begins at once, before the next call. | — (order of the session's attack and the goals played at once) |
| The attacker's damages opponent walk comes before its weapon wear, so a weapon this attack's wear breaks still fires (IC2). | The hero's walk names the weapon, but the host no longer knows it as held once the wear broke it, so it fires nothing. | — (not visible: no installed weapon carrying combat triggers breaks in one hit) |
| The kill's dying event waits for the victim's dying script (point 12), which may stop it (IC5). | It always fires: the session runs no dying script. | — (scripts are the host's, not ready) |
| An item use's empty slot 0 on the spell branch reads a record outside the table (IU6). | Nothing is cast. | none (technical bug, fixed in both modes; owner rule 2026-10-28) |
| A used scroll whose slot-0 spell targets an inventory item opens the inventory and is cast at the use's target (IU4). | Nothing is cast and the scroll is still spent (one installed scroll). | — (not ready: inventory targets) |
| A used food or scroll has its runs' sources cleared before their goals play, so their BEGIN finds no source and charges nothing. | The goals play at once, so the item pays its charge before it is spent and deleted. | — (not visible: the item is deleted; no installed food or scroll reaches the spell branch with no charge left) |

## Not ready

- The other inventory moves run no pickup or removal executor yet: barter (a sale of an item that sources a run is refused), the
  throw, a critical drop, items placed or created on the ground by the session, the NPCs' own inventories and the items scripts move
  (their pickup and drop fire nothing in the installed game; the removal's ending of runs is missing there). Picking such an item up
  again through `PickUpItem` or `LootItem` fires its pickup.
- The item use's other branches (reading, lockpick, healing kit, food and scroll spending without magic) are the host's own calls
  (`UseWrittenItem`, `PickLock…`, `HealWithItem`); no viewer action calls `UseItem` yet. Spells aimed at an inventory item through a
  use, and an NPC's use (the activation refuses a holder that is not a player's PC, SU1) are not ready.
- The combat and life events outside the hero's damage step on an NPC: going unconscious from fatigue elsewhere (casting, running,
  overexertion, IC4), the dying animation's end with no other object (IC5), a spell's damage step (its attacker is the run's parent,
  DH1), the attack start's attacks an opponent and the projectile's attacks a location (IC6), an item-type attacker's own target hit
  (0x4CBE70), and NPC attacks (the session has no NPC attack path yet). The global attacker (IC6) needs the run's other object, which
  the invocation does not carry (MT7). The aims at the other's worn weapon or armour stay not ready (no installed record uses them).
- Hotkeys (0x57C080, IP7–IP9) and spells aimed at an inventory item; the weapon's spell pane (0x57C040) is in the viewer since
  ARC-UI-016 ([spell cast host](spell-cast-host.md#the-weapons-spell-pane-arc-ui-016)).
- The best-items start (`EquipBest`) skips items with spells, as before: it runs before any cast host exists.
- A first break of a marked worn item takes it off without its unwear (0x4CBFC0), and its runs keep the item as source.
- The viewer's inventory window (ARC-UI-015) passes `SessionItemTriggers` over the shown map's cast host to `Equip` and `Unequip`,
  but that cast host is built without an item lookup (the spell charges field is not read for map or kit items), so it supports no
  item and an item with spells stays refused there; the session's runs are not saved (MT9b), so a loaded game wears its items without
  their runs.

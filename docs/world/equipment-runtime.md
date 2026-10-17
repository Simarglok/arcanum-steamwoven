# Equipment runtime (ARC-WORLD-028)

Status: **Implemented bounded component**, ARC-WORLD-028 (2026-10-17). The hero's wear slots in the
[game session](game-session-runtime.md): which item may take which slot, wielding and wearing kit items and items taken
from the world, and what the worn items give the [attack resolver](attack-resolver-runtime.md) (ARC-COMBAT-001) and the
[turn-based round](turn-based-round-runtime.md) (ARC-COMBAT-003). The rules are pure functions in
`Arcanum.Simulation.Equipment.EquipmentRules`; the session holds the worn items (snapshot schema 12). Requirements:
[FR-ITEM-005](../requirements/items-economy.md#fr-item-005) (slot legality, wield and remove),
[FR-ITEM-006](../requirements/items-economy.md#fr-item-006) (weapon and armour item values into combat) and the light part
of [FR-ITEM-007](../requirements/items-economy.md#fr-item-007). Not wired into the viewer ([Not ready](#not-ready)).

## Evidence

- CE at the pinned commit `a7ff41b300ef712f0e7d088183a3d08957110cdd`: `item.c` (wear check, wield, unwield, remove check,
  slot of an item, armour class adjustment, race size table, effective power and magic adjustment, weapon speed, range,
  minimum strength, unarmed damage, light), `object.c` (armour class and resistance), `inven_ui.c` (the paperdoll's own
  checks), `first_party/tig/src/art.c` (item art bits). No code or structure was copied.
- The retail 1.0.7.4 `Arcanum.exe` (SHA-256 `9bd80511…78d52`), read statically by two independent readers at the same
  functions per the [original executable check](../research/original-executable-check.md); rows EQ1–EQ14 in the
  [verification ledger](../research/original-verification-ledger.md#equipment-arc-world-028). The original does what CE
  reads except one CE slip that changes a rule (EQ1: a shield is refused with one crippled arm too) and one that changes only
  stored flags (EQ12: the critter keeps only its largest light flag). EQ5 is a bug of the original without a session effect
  (the medallion slot recalculates the animation speed of the item, not the wearer), so no rule-mode switch.
- Installed original (opt-in, below): all 150 weapon, 251 armour and 148 generic prototypes become wear values.

## Rules (`EquipmentRules`)

| Rule | Value |
| --- | --- |
| Slots | helmet 1000, ring 1001 and 1002, medallion 1003, weapon 1004, shield 1005, body armour 1006, gauntlets 1007, boots 1008 |
| Slot of an item | a weapon the weapon slot; armour by the coverage of its inventory art (torso, shield, helmet, gauntlets, boots, ring, medallion); a generic item with the torch/shield flag the shield slot; anything else none |
| Wear check, in order | a polymorphed or "body of" wearer: not wieldable; a magickal item (complexity > 0) and Magick Allergy; broken (destroyed art bit); not the item's slot (either ring slot takes a ring); weapon: a two-handed weapon with a fixed hand count is refused next to a shield and with any crippled arm, any other weapon only with both arms crippled; shield: refused next to such a weapon and with any crippled arm (EQ1); body armour: the race's size (dwarf, gnome, halfling small; half-ogre, ogre large; else medium), male-only before female-only, unique NPC art; then the wearer's art for a weapon, an armour shield or body armour (the host's answer). Minimum strength is never checked. |
| Leaving a place | an item with the no-drop flag never leaves (wielding it is refused too); a worn item may always be taken off (the original's hexed-item check is a stub, EQ2) |
| Effective power | `(complexity + aptitude) / 2` truncated toward zero, clamped to `0 .. complexity` on the item's side |
| Magic adjustment | magickal `value × power / complexity`, technological `value − value × power / complexity`, neutral unchanged |
| Worn armour class | each worn armour item's AC adjustment + its magic AC adjustment; the caller adds base AC, effects and the AcAdjustment stat (`AttackRules.ArmorClass`) |
| Worn resistances | per type the adjustment + the magic adjustment of each worn armour item; the caller adds the base, the poison bonus of CN, effects and the 0..95 clamp |
| Light | each worn item's light sizes shrink by 1, 2 or 3 at an effective power ratio ≤ 75, 50, 25 %; the largest remaining size |
| Weapon | bare hands: `CombatWeapon.Unarmed(gauntlet unarmed bonus)`; else the stored damage, to-hit, charts, flags and condition, with range, minimum strength and every magic adjustment for the wielder |
| Minimum strength | stored + magic adjustment; unless the hand count is fixed, two-handed +4 next to a shield, one-handed −2 without one |
| Weapon speed | speed factor + magic speed adjustment + 5 with apprentice training, 10 bare-handed; attack cost from `TurnBasedRules.AttackCost` |

`EquipmentRules.Evaluate(worn, aptitude, weaponTraining)` returns all of it as `EquipmentCombat`. `IsValid` bounds an
item (numbers within ±1000, speeds within ±495, condition 0..1 000 000, the blocks of its kind).

## Session domain (`Arcanum.Session`)

| Entry | Contract |
| --- | --- |
| `QueryEquipment()` | The worn items in slot order: slot, the PC item entity (`kit:n` or a taken item), its saved values. |
| `QueryHeroEquipmentCombat(training)` | `EquipmentRules.Evaluate` for the hero's effective magick/tech aptitude; null without a hero. Training is the caller's (skills are not session state). |
| `Equip(expected, entity, slot, item, artCheck, insertCheck)` | The source's `item_wield_set`. Refusals, in order: input, version, no hero (`EquipmentUnavailable`), an item the hero does not hold (`ItemNotAvailable`) or another kind than the saved one (`InventoryMismatch`); NoChange when already in that slot; a `SAP_INSERT_ITEM`, `SAP_REMOVE_ITEM`, `SAP_WIELD_ON` or `SAP_WIELD_OFF` script (`ItemScriptUnsupported`) or item spells (`ItemEffectUnsupported`); the wear check with the hero's race, gender, aptitude and crippled arms from the life domain, then `artCheck` (`WearRefused` with the `EquipmentRefusal`); the no-drop flag of the item, then of the slot's occupant (`WearRefused`, `NotDroppable`); the insert check of the occupant going back to the grid (`InsertRefused`). A ring in the other ring slot moves. One revision. |
| `Unequip(expected, slot, insertCheck)` | The source's unwield to the grid: NoChange for an empty slot; the no-drop flag, then the insert check. One revision. |

`SessionWearableItem` is what the host offers: the saved kind, script points, whether the item has item spells, and its
`EquipmentItem` values. `SessionEquipmentFacts.FromCombatFields` builds the values from the stored combat fields
([ARC-DATA-021](../formats/combat-fields.md), which now also reads the current art, inventory art and use-art fragment).
The host's callbacks must not call back into the owner: a call that moves the revision makes the step `StaleVersion`.

**Combat effects.** The hero's life facts take the worn armour's normal resistance (clamped 0..95; a pre-made PC's base
resistance and resistance effects are not session state), so a worn item now reduces the round's overexertion fatigue
and the poison tick. The resolver and the round's attack cost read `QueryHeroEquipmentCombat`; no host calls them yet.

A worn item stays in its PC list (`QueryHeroKit`, `QueryPcInventory`): the session does not reorder the lists as the
source's remove-and-insert does, because no rule reads the order of the PC's own list.

## Snapshot schema 12

`GameSessionSnapshot.Equipment`, JSON between `inventory` and `random`, slots strictly ascending, entities unique:

```json
"equipment":[{"slot":"Weapon","entity":"kit:1","item":{"kind":"Weapon","complexity":0,"noDrop":false,"broken":false,
  "uber":false,"light":0,"usesShieldLocation":false,"hp":20,"maxHp":20,"weapon":{…},"armor":null}}]
```

(The synthetic test's values; the weapon block holds its flags, skill, five damage ranges and magic damage, to-hit,
speed, range and minimum strength with their magic adjustments, ammunition and the critical charts and chances; the
armour block coverage, sizes, gender flags, AC, five resistances and their magic adjustments and the unarmed bonus.)
The codec checks shapes, names and ranges; `Restore` refuses (`InvalidEquipment`) a worn item without a hero, an entity the
hero does not hold or of another type, an item that does not belong in its slot or is invalid, an entity in two slots,
and a fixed two-handed weapon beside a shield. **Migration decision: none.** Schema 11 and older are refused; a quick save
of schema 11 loads as "written by an incompatible version" and the next F7 overwrites it (the viewer wears nothing).

## Tests

- `Arcanum.Simulation.Tests/Equipment/EquipmentRulesTests`: slot mapping, race sizes, the wear check's order and every
  refusal (the shield with one crippled arm), the art and no-drop checks, effective power and adjustment with truncation,
  worn AC and resistances, light tiers, minimum strength, weapon speed and cost, bare hands with gauntlets, a wielded
  weapon's values, validation.
- `Arcanum.Session.Tests/EquipmentStateTests`: wielding a kit weapon, armour against overexertion, every refusal in order,
  the art check's place, the occupant's insert check and swap, rings between slots, unequipping, no-drop, crippled arms
  from the life domain, race and gender, a taken item, the canonical text and restore, restore and decode refusals, the
  bridge from stored fields.
- `Arcanum.Data.Tests/Combat/CombatFieldReaderTests`: the art fields and their bits.
- Real data (opt-in `RealData`, `ARC_AREA_PROTOTYPES`): `EquipmentRealDataTests`. All 549 weapon, armour and generic
  prototypes convert (slots: 150 weapon, 165 body armour, 13 shields and 2 torches, 19 helmets, 15 gauntlets, 10 boots,
  19 rings, 10 medallions, 146 generic items worn nowhere); Virgil's armour 8114 is body armour with AC +3. Offered to a
  half-elf man, 290 of 403 are worn, 96 body armour items are another size, 5 are for women and 12 carry item spells.

## Not ready

- **No host.** The viewer's inventory window has no paperdoll; nothing calls `Equip`, the resolver or the round's attack
  cost with the hero's equipment yet. The pre-made hero's start does not wield its kit (the original's new game runs
  `item_wield_best_all`, which needs skill effectiveness, LM1/LM2).
- **NPC equipment.** NPC slots stay the host's (seed inventory locations); `EquipmentRules.Evaluate` serves them too, and
  looting a worn item is still `WornItemUnsupported`.
- **Not modelled:** item spells on wear and unwear (such items are refused), scripts on wield (refused), art and
  appearance changes, the animation speed recalculation (EQ5), the paperdoll UI's own path (`inven_ui.c`), identification
  (combat reads every magic adjustment), weapon wear, hexed items, ammunition pools, drop, barter, weight effects,
  base AC and resistances of the PC and its effects, skills and training.

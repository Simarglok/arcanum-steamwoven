# Equipment runtime (ARC-WORLD-028)

Status: **Implemented bounded component**, ARC-WORLD-028 (2026-10-17); the best items of a slot and the pre-made start's wield
ARC-WORLD-034 (2026-10-18, [Best items](#best-items-arc-world-034)). The hero's wear slots in the
[game session](game-session-runtime.md): which item may take which slot, wielding and wearing kit items and items taken
from the world, and what the worn items give the [attack resolver](attack-resolver-runtime.md) (ARC-COMBAT-001) and the
[turn-based round](turn-based-round-runtime.md) (ARC-COMBAT-003). The rules are pure functions in
`Arcanum.Simulation.Equipment.EquipmentRules`; the session holds the worn items (snapshot schema 12). Requirements:
[FR-ITEM-005](../requirements/items-economy.md#fr-item-005) (slot legality, wield and remove),
[FR-ITEM-006](../requirements/items-economy.md#fr-item-006) (weapon and armour item values into combat) and the light part
of [FR-ITEM-007](../requirements/items-economy.md#fr-item-007). The viewer uses it only for the pre-made start
([Not ready](#not-ready)).

## Evidence

- The original's functions, read first: the item functions (wear check, wield, unwield, remove check, slot of an item,
  armour class adjustment, race size table, effective power and magic adjustment, weapon speed, range, minimum strength,
  unarmed damage, light), the object armour class and resistance, the paperdoll UI's own checks and the TIG item art bits.
- The retail 1.0.7.4 `Arcanum.exe` (SHA-256 `9bd80511…78d52`), read statically by two independent readers at the same
  functions per the [original executable check](../research/original-executable-check.md); rows EQ1–EQ14 in the
  [verification ledger](../research/original-verification-ledger.md#equipment-arc-world-028). The original does what
  that first reading describes except in two rows: one changes a rule (EQ1: the original refuses a shield with one crippled arm
  too) and one changes only stored flags (EQ12: the original keeps only the critter's largest light flag). EQ5 is a bug of the original without a session effect
  (the medallion slot recalculates the animation speed of the item, not the wearer), so no rule-mode switch.
- Installed original (opt-in, below): all 150 weapon, 251 armour and 148 generic prototypes become wear values.

## Rules (`EquipmentRules`)

| Rule | Value |
| --- | --- |
| Slots | helmet 1000, ring 1001 and 1002, medallion 1003, weapon 1004, shield 1005, body armour 1006, gauntlets 1007, boots 1008 |
| Slot of an item | a weapon the weapon slot; armour by the coverage of its inventory art (torso, shield, helmet, gauntlets, boots, ring, medallion); a generic item with the torch/shield flag the shield slot; anything else none |
| Wear check, in order | a polymorphed or "body of" wearer: not wieldable; a magickal item (complexity > 0) and Magick Allergy; broken (destroyed art bit; since ARC-WORLD-053 the session's [durability](item-durability-runtime.md) record of the item wins over the host's data, also in `EquipBest`, and a first break takes a worn item off); not the item's slot (either ring slot takes a ring); weapon: a two-handed weapon with a fixed hand count is refused next to a shield and with any crippled arm, any other weapon only with both arms crippled; shield: refused next to such a weapon and with any crippled arm (EQ1); body armour: the race's size (dwarf, gnome, halfling small; half-ogre, ogre large; else medium), male-only before female-only, unique NPC art; then the wearer's art for a weapon, an armour shield or body armour (the host's answer). Minimum strength is never checked. |
| Leaving a place | an item with the no-drop flag never leaves (wielding it is refused too); a worn item may always be taken off (the original's hexed-item check is a stub, EQ2) |
| Effective power | `(complexity + aptitude) / 2` truncated toward zero, clamped to `0 .. complexity` on the item's side |
| Magic adjustment | magickal `value × power / complexity`, technological `value − value × power / complexity`, neutral unchanged |
| Worn armour class | each worn armour item's AC adjustment + its magic AC adjustment; the caller adds base AC, effects and the AcAdjustment stat (`AttackRules.ArmorClass`) |
| Worn resistances | per type the adjustment + the magic adjustment of each worn armour item; the caller adds the base, the poison bonus of CN, effects and the 0..95 clamp |
| Light | each worn item's light sizes shrink by 1, 2 or 3 at an effective power ratio ≤ 75, 50, 25 %; the largest remaining size |
| Weapon | bare hands: `CombatWeapon.Unarmed(gauntlet unarmed bonus)`; else the stored damage, to-hit, charts, flags and condition, with range (never below 0), minimum strength and every magic adjustment for the wielder |
| Minimum strength | stored + magic adjustment; unless the hand count is fixed, two-handed +4 next to a shield, one-handed −2 without one |
| Weapon speed | speed factor + magic speed adjustment + 5 with apprentice training, 10 bare-handed; attack cost from `TurnBasedRules.AttackCost` |

`EquipmentRules.Evaluate(worn, aptitude, weaponTraining)` returns all of it as `EquipmentCombat`. `IsValid` bounds an
item to what the attack resolver accepts (numbers within ±1000, speeds within ±495, condition 0..100 000, weapon damage
and unarmed bonus not negative, the blocks of its kind).

## Session domain (`Arcanum.Session`)

| Entry | Contract |
| --- | --- |
| `QueryEquipment()` | The worn items in slot order: slot, the PC item entity (`kit:n` or a taken item), its saved values. |
| `QueryHeroEquipmentCombat(training)` | `EquipmentRules.Evaluate` for the hero's effective magick/tech aptitude; null without a hero. Without a training argument the hero's training in the wielded weapon's skill (ARC-SKILLS-002, [hero skills](hero-skills-runtime.md)); an explicit one overrides. |
| `Equip(expected, entity, slot, item, artCheck, insertCheck)` | The original's wield (0x464AE0). Refusals, in order: input, version, no hero (`EquipmentUnavailable`), an item the hero does not hold (`ItemNotAvailable`) or another kind than the saved one (`InventoryMismatch`); NoChange when already in that slot; a `SAP_INSERT_ITEM`, `SAP_REMOVE_ITEM`, `SAP_WIELD_ON` or `SAP_WIELD_OFF` script (`ItemScriptUnsupported`) or item spells (`ItemEffectUnsupported`); the wear check with the hero's race, gender, aptitude and crippled arms from the life domain, then `artCheck` (`WearRefused` with the `EquipmentRefusal`); the no-drop flag of the item, then of the slot's occupant (`WearRefused`, `NotDroppable`); the insert check of the occupant going back to the grid (`InsertRefused`). A ring in the other ring slot moves. One revision. |
| `Unequip(expected, slot, insertCheck)` | The original's unwield to the grid: NoChange for an empty slot; the no-drop flag, then the insert check. One revision. |

`SessionWearableItem` is what the host offers: the saved kind, script points, whether the item has item spells, and its
`EquipmentItem` values. `SessionEquipmentFacts.FromCombatFields` builds the values from the stored combat fields
([ARC-DATA-021](../formats/combat-fields.md), which now also reads the current art, inventory art and use-art fragment).
The host's callbacks must not call back into the owner: a call that moves the revision makes the step `StaleVersion`.
The insert check is asked as the source asks it: on a swap with both the new item and the occupant already out of the grid
(the room the new item leaves counts); on unequipping with the item still worn (the source counts its weight twice near
the carry limit). The facts it gets carry only the saved kind; weight and grid size are the host's to look up.

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

## Best items (ARC-WORLD-034)

The original asks for a critter's best items in every slot (ledger rows WB1–WB8): a new game does it for the pre-made hero
before the game starts, and the AI does it for NPCs. `EquipmentChoice` holds the pure rules, the session runs the steps.

| Rule | Value |
| --- | --- |
| Worth (`Worth`, `Identified`) | an unidentified magickal item (complexity > 0 without the identified flag) 300; else the stored worth, at least 2 |
| Thrown damage (`ThrownDamage`) | a throwable weapon its own ranges; anything else normal `(weight + 99) / 100 .. (weight + 49) / 50` from the caller's weight |
| Weapon score (`WeaponScore`) | null without the ammunition for one use; else the sum over the five types of the average of the wielder's damage range (`AttackRules.WeaponDamageRange`: melee DamageBonus, magic adjustments, caps), times the attack effectiveness of the weapon's skill / 100; a skill at level 0 other than melee, or an effectiveness of 0, counts −1 |
| Choice (`ChooseBest`) | weapon slot: the eligible weapon with the highest score, a tie to the later candidate (the original compares object handles, which the session has not; candidates come in creation order); with a target the best weapon that reaches it wins; other slots: the eligible item worn nowhere with a strictly higher worth than the kept item and every earlier one |

`GameSessionOwner.EquipBest(expected, items, artCheck, insertCheck)` runs the slots 1000..1008 in order on a plan and
commits once (one revision; NoChange when the worn items end as they were): a slot's item that still passes the wear check
stays as the worth to beat, except in the weapon slot; any other comes off (no-drop or a refused insert check keeps it on);
then the choice over the hero's items in creation order (the kit, then the items taken from the world), wielded as by
`Equip`. The host gives every held item's facts (`SessionWearableLookup`; `SessionEquipmentFacts.Wearable` builds them from
the stored combat fields, which now also read the script points) and answers the art and insert checks. The hero's weapon
skill levels (melee, bow, throwing, firearms) are its session skills since ARC-SKILLS-002 ([hero skills](hero-skills-runtime.md)), and since
ARC-WORLD-066 its ammunition is the units of its stack of each type ([ammunition](inventory-runtime.md#ammunition-arc-world-066)). `SessionEquipBestStep` lists what happened per slot. Refused before any effect: version, no hero, invalid or
mismatched facts, a callback that moved the session. **Project policy:** a chosen item with a move script or item spells
is not wielded (the session runs neither; the original wears it and runs them): its step names it, the slot keeps what it
held when the call began (a weapon taken off for the choice goes back on) and the call goes on.

The AREA viewer's pre-made start (`--hero`, ARC-WORLD-027) runs it after creating the session, with the kit's facts read
from the hero's content prototypes and yes to the art and insert checks (one diagnostic body art, no grid); the HUD shows
`worn N`. Installed original (opt-in, `ARC_AREA_CONVERSATION_GAME_ROOT`): every pre-made hero starts with a weapon, body
armour and boots on, except the human woman, whose body armour 8220 carries wield-on and wield-off scripts and stays in the
pack.

## Tests

- `Arcanum.Simulation.Tests/Equipment/EquipmentRulesTests`: slot mapping, race sizes, the wear check's order and every
  refusal (the shield with one crippled arm), the art and no-drop checks, effective power and adjustment with truncation,
  worn AC and resistances, light tiers, minimum strength, weapon speed and cost, bare hands with gauntlets, a wielded
  weapon's values, validation.
- `Arcanum.Session.Tests/EquipmentStateTests`: wielding a kit weapon, armour against overexertion, every refusal in order,
  the art check's place, a callback that changes the session (stale), the occupant's insert check and swap, a fixed two-handed weapon against a shield (also on restore), rings between slots, unequipping, no-drop, crippled arms
  from the life domain, race and gender, a taken item, the canonical text and restore, restore and decode refusals, the
  bridge from stored fields.
- `Arcanum.Simulation.Tests/Equipment/EquipmentChoiceTests` (ARC-WORLD-034): worth and identification, thrown damage, the
  weapon score (every type summed, DamageBonus, difficulty, untrained skills, ammunition), the choice with its tie rule, a
  target, and the other slots' strict worth.
- `Arcanum.Session.Tests/EquipBestTests` (ARC-WORLD-034): the start in one revision and a repeated call, a more valuable
  item replacing a kept one, ineligible items and a refused swap, a no-drop weapon that stays on and a shield that crippled
  arms take off, the tie to the later item, refusals and a meddling callback, unsupported items left in the pack.
  `Arcanum.TerrainViewer.Tests/AreaStartingHeroTests`: the viewer start wears the kit; mismatched facts and an unreadable
  kit prototype fail it. `EquipBestTests` also keeps the knife on when the better weapon is unsupported; the session's bridge
  test reads the wearable facts and the Data reader test the script points.
- `Arcanum.Data.Tests/Combat/CombatFieldReaderTests`: the art fields and their bits.
- Real data (opt-in `RealData`, `ARC_AREA_PROTOTYPES`): `EquipmentRealDataTests`. All 549 weapon, armour and generic
  prototypes convert (slots: 150 weapon, 165 body armour, 13 shields and 2 torches, 19 helmets, 15 gauntlets, 10 boots,
  19 rings, 10 medallions, 146 generic items worn nowhere); Virgil's armour 8114 is body armour with AC +3. Offered to a
  half-elf man, 290 of 403 are worn, 96 body armour items are another size, 5 are for women and 12 carry item spells.
  `StartingHeroTests.Every_premade_hero_starts_wearing_its_best_kit_items` (`ARC_AREA_CONVERSATION_GAME_ROOT`): the worn
  slots and prototypes of all twelve pre-made heroes (ARC-WORLD-034).

## Not ready

- **No paperdoll.** The viewer's inventory window has no paperdoll and no host calls `Equip`; the pre-made start wields the
  kit (ARC-WORLD-034). The best-items choice reads the hero's skill levels (ARC-SKILLS-002) and its ammunition stacks (ARC-WORLD-066); a custom
  character's start (weapon and armour only) and the AI's call with a target are not wired.
- **NPC equipment.** NPC slots stay the host's (seed inventory locations); `EquipmentRules.Evaluate` serves them too, and
  looting a worn item is still `WornItemUnsupported`.
- **Not tied to the round.** `Equip` and `Unequip` neither charge action points nor refuse during a turn-based round; the
  whether the original charges for changing equipment in a round is not researched here.
- **Not modelled:** item spells on wear and unwear (such items are refused), scripts on wield (refused), art and
  appearance changes, the animation speed recalculation (EQ5), the paperdoll UI's own path, identification
  (combat reads every magic adjustment), weapon wear, hexed items, ammunition pools, drop, barter, weight effects,
  base AC and resistances of the PC and its effects, skills and training.

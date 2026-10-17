# Combat fields of items and critters

Status: **Implemented, bounded**, work item ARC-DATA-021 (2026-10-16). A
Godot-free reader in `Arcanum.Data` of the stored combat-relevant fields of
weapons, ammunition, armour, other items and critters, so the attack resolver
(ARC-COMBAT-001) can move from synthetic to original Crash Site inputs. It
answers the data half of ARC-RES-030 open question O6
([combat core](../research/combat-core-evidence.md)) and implements the data
side of [FR-ITEM-006](../requirements/items-economy.md#fr-item-006); the field
list is [section 5 of the combat-domain load map](../research/combat-domain-load-map.md#5-prototypes-and-object-fields)
(ARC-RES-040). It is not the combat data census (ARC-RES-041), changes no
session, snapshot or viewer, and applies no combat rule.

Code: [`src/Arcanum.Data/Combat`](../../src/Arcanum.Data/Combat); tests:
[`tests/Arcanum.Data.Tests/Combat`](../../tests/Arcanum.Data.Tests/Combat).

## API

- `CombatFieldReader.ReadPrototype(lookup, prototype)` reads one loose
  prototype file by its six-digit number (basic prototype + 20) through the
  lookup the pre-made hero reader takes (`PremadeHeroReader.DirectoryPrototypes`
  over the installation's `data/proto`). Origin `Prototype`.
- `CombatFieldReader.Read(AreaObjectRecord)` reads one Area object by its
  effective fields: an instance field replaces the prototype's whole field
  (an array is never merged key by key), as the source's lazy inheritance does.
  Origin `AreaObject`.
- `CombatFieldReader.ReadArea(AreaSnapshot)` reads every mobile item and
  critter of a strictly published snapshot in source-index order; the first
  refusal stops it. An item or critter from a sector file instead of the mobile
  aggregate is refused (`StaticObjectUnsupported`), as the inventory view does.
- `CombatFieldReader.WeaponSkill(flags, complexity, range, ammoType)` is the
  source's `item_weapon_skill` over stored fields; `SkillPoints` and
  `SkillTraining` split a stored skill value.

Every result is a `CombatFieldRead` or `CombatAreaRead`: `Failure` `None` with
fields, or a typed refusal (`PrototypeMissing`, `PrototypeAmbiguous`,
`PrototypeInvalid`, `TypeUnsupported`, `SnapshotNotStrict`, `StaticObjectUnsupported`, `FieldUnresolved`,
`ArrayMalformed`) with the slot, prototype number or object type in `Detail`.
No exception crosses the boundary except for null arguments and cancellation.

## What is read

`CombatFields` holds the blocks that exist for the object's type: `Object`
always, `Item` for types 5..14 with `Weapon` (5), `Ammo` (6), `Armor` (7) or `Generic` (14),
`Critter` for PCs and NPCs (15, 16) with `Npc` for NPCs. Slots are the v119
layout of the StaticObjects loader, in the source's field order
([`obj.h`][obj-h]).

| Block | Slots | Fields |
| --- | --- | --- |
| Object | 0, 18..30 | current art (slot 0, ARC-WORLD-028), flags, spell flags, name class, description, AC, HP points, adjustment and damage, material, five resistances |
| Item | 36..55 | item flags, weight, worth, mana store, inventory art (42), inventory location, use-art fragment (44), magick/tech complexity, discipline, five spells, spell mana store, AI action |
| Weapon | 59..81 | weapon flags, to-hit bonus and magic adjustment, damage lower and upper (five types), magic damage adjustment, speed factor, range, minimum strength (each with its magic adjustment), ammunition type and consumption, critical hit and miss charts with magic chance and effect; derived skill |
| Ammunition | 59..61 | flags, quantity, ammunition type |
| Armour | 59..67 | armour flags, AC adjustment and magic adjustment, resistance adjustments and magic adjustments (five each), silent-move adjustment and magic adjustment, unarmed bonus damage |
| Generic | 59..61 | generic flags (lockpick, trap device, healing item, grenade and others), usage bonus, remaining usage count |
| Critter | 36..63 | critter flags and flags 2, 28 base stats, 12 basic and 4 tech skills, 25 spell/tech values, fatigue points, adjustment and damage, critical-hit body type, inventory source, auto-level scheme |
| NPC | 69..92 | NPC flags, AI parameter row, experience worth and pool, faction, reaction base, social class, monster damage (five min/max pairs) |

Index orders differ and are kept as stored: damage arrays by
`CombatDamageIndex` (normal, poison, electrical, fire, fatigue), resistance
arrays by `CombatResistanceIndex` (normal, fire, electrical, poison, magic)
([damage][damage-h], [resistance][resistance-h]). NPC monster damage stores the
minimum at 2 × type and the maximum at 2 × type + 1 ([generator][proto-damage]).
A weapon without ammunition stores 10000 (`NoAmmo`); ammunition types are
arrow 0, bullet 1, power cell 2, fuel 3 ([ammo][item-ammo]).

Sparse arrays read like the source ([`sa_get`][sa-get]): a missing key, a null
or an empty array is 0. A key outside the array's range, which the source would
simply never ask for, is refused as a deliberate fail-closed choice; it and an
element that is not one 32-bit word are `ArrayMalformed`; unsigned words are reinterpreted as
signed, so the stored "no mastery college" is −1. A scalar that is absent, null
or not a number is `FieldUnresolved`.

The weapon skill follows [`item_weapon_skill`][weapon-skill]: a boomerang
throws; a technological weapon (complexity below 0) with range at least 3 and
bullets, power cells or fuel is a firearm; an arrow weapon is a bow; anything
else is melee.

**Item art (ARC-WORLD-028).** `CombatItemArt` decodes the three art ids the wear rules need, as the source's tig art
helpers do (checked in the retail executable, ledger EQ1 and EQ6): only an item art id (type 6 in bits 28..31) has item
bits; the inventory art's bits 14..16 are the armour coverage (torso, shield, helmet, gauntlets, boots, ring, medallion),
the use-art fragment's bits 6..9 the art subtype a wearer's art takes, and bit 10 of the current art marks a destroyed
(broken) item. The [equipment runtime](../world/equipment-runtime.md) turns these fields into wear values
(`SessionEquipmentFacts.FromCombatFields`).

## Boundaries

Values are stored values. Nothing here applies stats, effects, aptitude scaling
of magic adjustments, difficulty, item condition, wielder or wielded-slot rules;
those belong to the attack resolver and its future data bridge. HP points of
critters are the stored base (0 on the Crash Site creatures); maximum HP is a
rule over stats and level.

NOT READY: portals, containers, walls and scenery (types 0..4) and traps (17)
are refused as `TypeUnsupported`, although portals and containers can be
attacked; critter ammunition pools (four object handles) and inventory gear
(use the [Area inventory view](../world/area-inventory-view.md) and
`InventoryLocation` 1004 wielded, 1006 worn); prototypes inside archives
(loose `data/proto` only, like the pre-made hero reader); the spell, AI
parameter and critical tables the stored numbers point to; generated
prototypes for missing files.

## Installed original

Opt-in tests (`TestCategory("RealData")`, inputs of the inventory real-data
tests: `ARC_AREA_MODULE`, `ARC_AREA_RESOURCE1/2`, `ARC_AREA_MOBILE`,
`ARC_AREA_PROTOTYPES`, `ARC_CAVE_MOBILE`), first observation 2026-10-16:

- **Crash Site exterior.** Every mobile item and critter reads. 25 creatures
  (NPCs with a monster damage table, normal damage only, stored base HP 0) of
  five prototypes: 28359 (10 core + 3 context, 1–3), 27356 (3, 3–8), 28447
  (3, 1–4), 27359 (1, 5–10, the only creature holding anything: weapon 6198
  wielded, melee 2–6, speed 6)
  and 28339 (5 context, 3–7). Virgil carries weapon 6057 unwielded (normal 1–4,
  fatigue 2–9) and wears armour 8114 (AC +3). Two weapons, four armour items,
  no ammunition.
- **Crash Site cave.** Six creatures of prototype 28373 (1–3, AI row 19), one
  firearm 6102 (bullets, one per shot, range 5, to-hit −10, 1–5), five melee
  weapons 6067 (1–2), arrows and bullets in stacks of 15.
- **All loose prototypes.** All 1,308 item and critter prototypes read
  (150 weapons: 89 melee, 12 bow, 8 throwing, 41 firearms; 4 ammunition,
  one per type, stack 10; 251 armour; 13 PCs; 497 NPCs); the other 115 are
  refused as `TypeUnsupported`. 148 generic items carry the generic block.

These are reader acceptance checks, not the combat census: per-field
distributions, AI rows, flags and first-fight candidates are in the
[combat-domain data census](../research/combat-data-census.md) (ARC-RES-041).

[obj-h]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj.h#L11-L324
[damage-h]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/damage_type.h#L6-L13
[resistance-h]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/resistance.h#L4-L11
[proto-damage]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/proto.c#L16269-L16288
[item-ammo]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L64-L77
[sa-get]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sa.c#L190-L207
[weapon-skill]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L3308-L3340

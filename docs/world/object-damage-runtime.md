# Object damage runtime (ARC-COMBAT-008)

Status: **Implemented bounded component** (Godot-free rules and session owner), ARC-COMBAT-008 (2026-10-21). The hero
attacks a non-critter object on any map: a portal, a container, a scenery, a trap or an item on the ground. The hit damages
it and, at 0 hit points, breaks it as the retail 1.0.7.4 executable does. No viewer input reaches it yet.

Requirements: [FR-COMBAT-004](../requirements/combat-magic-tech.md#fr-combat-004) (attackable targets: objects),
[FR-COMBAT-008](../requirements/combat-magic-tech.md#fr-combat-008) (damage to objects, material reduction),
[FR-ITEM-011](../requirements/items-economy.md#fr-item-011) (items on the ground break),
[FR-ITEM-001](../requirements/items-economy.md#fr-item-001) (a broken container's items lie on the ground) and
[FR-WORLD-013](../requirements/world.md#fr-world-013) (destroyed and busted objects stay so across leaving and saving); the
drawing side of [FR-SCENE-008](../requirements/scene.md#fr-scene-008) (a destroyed door, a busted window) is the viewer's, not
ready.

Every rule was read in the executable by two independent readers
([ledger "Object damage"](../research/original-verification-ledger.md#object-damage-arc-combat-008), rows OD1–OD12, F2).

Code: [`ObjectDamageRules`](../../src/Arcanum.Simulation/Combat/ObjectDamageRules.cs),
[`GameSessionObjects`](../../src/Arcanum.Session/GameSessionObjects.cs); tests
[`ObjectDamageRulesTests`](../../tests/Arcanum.Simulation.Tests/Combat/ObjectDamageRulesTests.cs),
[`ObjectAttackStateTests`](../../tests/Arcanum.Session.Tests/ObjectAttackStateTests.cs).

## The hit (the object branch of 0x4B4390)

The [attack resolver](attack-resolver-runtime.md) resolves the attack with the object as a portal, a container or another
object:

- there is no skill roll, dodge or critical procedure;
- the object's stored resistances are clamped 0..95;
- a PC's hit points are scaled by the game difficulty;
- a portal or container subtracts its material reduction: stone, brick, metal and force 10, wood 5, others 0;
- an invulnerable or destroyed object takes nothing;
- a wall never takes damage.

The resolver's hit-point amount (normal + electrical + fire, at least 0) is added to the object's hit-point damage. When the
object's current hit points (hp_pts + hp_adj − damage) are 0 or less, two things happen in order:

1. When the object's use script is a trap, the trap fires with the attacker as triggerer (no triggerer for a ranged attack).
2. The object breaks.

When the object stays whole, the guards of a portal or container hear of it and the attacker's weapon or hands wear by the
object's material ([wear](attack-resolver-runtime.md#wear-arc-combat-007), ledger CW9). Fatigue and poison do not reach objects.

## The break (0x43F1C0, ObjectDamageRules.Break)

| Object | Break |
| --- | --- |
| Portal (0x4F06E0, ledger LK12) | A door has no busted ART: it is destroyed (removed). A window gets its busted ART (bit 0x200, frame 0) and the busted bit (0x80), and its BUST script runs. Its damage is kept, so every later hit busts it again (ODA3). No sound: a portal has no misc sound. |
| Container | A junk pile (prototype 3023) has its damage reset to 0 and nothing breaks. Any other container drops its inventory at its location (generated first from its inventory source when it was never opened) and is destroyed; an eye candy (1203) plays on the first container or item lying there. |
| Scenery, respawning (scenery flag 0x20) | Its BUST script runs. It gets scenery flag 0x400, is switched off (object flag 0x2) with damage 0, and a respawn event switches it on again later. The delay comes from its field 71: positive values count seconds at the game rate (× 8,000 ms), negative values count game minutes, and 0 means one day. |
| Scenery | Already busted: destroyed. Otherwise it takes its destroyed ART (field 25, when set) and its damage becomes its maximum. It is marked busted (scenery flag 0x2) and made invulnerable (object flag 0x400000), the bust sound plays and its BUST script runs. |
| Trap | Without a destroyed ART, or already busted: destroyed. Otherwise it takes the ART with damage 0, is marked busted, the bust sound plays and its BUST script runs. |
| Item (types 5..14) | The [item durability](item-durability-runtime.md) break: first broken, then deleted. |
| Projectile, critter | Nothing. |

The function first returns for a destroyed object (object flag 0x1) and stops the object's animations.

## Session (`GameSessionOwner`, GameSessionObjects.cs)

The host passes a portal, container, scenery or trap as a `SessionObjectTarget`: its map, its entity id and its
`SessionObjectBaseline`. The baseline holds the object's kind, its stored hit points, material and resistances, and the
invulnerable flag. It also holds the facts its break reads:

- whether a portal is a window;
- whether a container is a junk pile, and the container's cell;
- whether a scenery is respawning, and its respawn field;
- whether a scenery or trap has a destroyed ART and is busted;
- whether its use script is a trap;
- for a portal or container, its lock baseline (required for a portal).

An item on the ground is passed as a `SessionWearItem` with its durability baseline, plus its material and resistances
(`ItemFacts`).

| Entry | Contract |
| --- | --- |
| `QueryObject(target)` | The damage, current hit points and condition (`Whole`, `Destroyed`, `Busted`, `Off`): recorded, or the baseline's (a busted baseline reads `Busted`). |
| `AttackObject(expected, attack)` | The hero's attack. Refused before any roll, in order: no hero (`AttackUnavailable`); an invalid target or item, or both or neither given (`InvalidObject`); a destroyed or switched-off object, or an item no longer on the ground (`ObjectUnavailable`); hero items that do not hold (`InvalidWear`); an aim at the torso or a distance outside the reach (`InvalidAttack`); a weapon that is not melee (`AttackUnsupported`); the version; the revision; for a respawning scenery, a full respawn budget (`EventBudgetExceeded`). Then the resolver on the session generator, the fate it used in a revision of its own, and the hit, which writes the object's damage and condition in one revision or damages the item through `DamageItem`. A critical self-hit hurts the hero instead and wears its own armour. The hero's weapon wear follows while the object stays whole. The result names what the host does (`SessionObjectHostSteps`): the trap, the BUST script, the bust or destroy sound, the spill eye candy, and the guards. |

State is kept in map changes of the object's entity, which the snapshot already carries, so there is no schema change:

- `object.damage` holds the hit-point damage;
- `object.condition` holds the condition, with a spilled container's cell above its low byte.

A busted window also takes the lock domain's busted bit on frame 0 in the same change (as `SetOpenableBusted`, ledger LK12).
Its busted ART is the busted bit's for the host.

A spilled container lists no items in `QueryMapInventory`: they lie on the ground at its cell, after the seed's ground items,
and `PickUpItem` takes them.

A respawning scenery schedules the saved game event `object.respawn`:

- it is scoped to the object's map;
- its values are the time it was set and the delay;
- at most 1,024 can be pending, a project policy (the original has no bound);
- when it fires, it switches the scenery on again;
- a restore refuses one whose scenery is not recorded off.

The registry version stays 1, as when the Lock event was added.

## Differences from the original

| Original 1.0.7.4 | Steamwoven |
| --- | --- |
| The respawn delay multiplies in 32 bits (it wraps beyond 268,435 seconds or 35,791 minutes). | Exact. |
| A respawning scenery that is off can still be damaged (ODA1). | The hero's attack on it is refused (`ObjectUnavailable`): the player cannot click an object that is off. |
| A container never opened first generates its inventory from its inventory source, then spills it. | The session holds no inventory sources: only the T0 items spill. |

## Not ready

- **Viewer.** It has no input to attack an object, and does not hide a destroyed door, draw a busted window's ART, show
  spilled items or switch a respawning scenery off.
- **Host steps.** The trap and BUST scripts, sounds, eye candy, the guards' reaction and the taking-damage script are reported
  to the host, not run here.
- **Others.** NPC attacks on objects (AI), area damage (spells, explosions) and script action 60 on objects, and the decay
  of dropped items.

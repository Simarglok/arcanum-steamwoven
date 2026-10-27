# Object damage runtime (ARC-COMBAT-008)

Status: **Implemented bounded component** (Godot-free rules and session owner), ARC-COMBAT-008 (2026-10-21). The hero
attacks a non-critter object on any map: a portal, a container, a scenery, a trap or an item on the ground. The hit damages
it and, at 0 hit points, breaks it as the retail 1.0.7.4 executable does. Since part 2 the AREA viewer attacks doors, windows,
containers and scenery with an Alt-click in combat mode ([viewer](#viewer-arc-combat-008-part-2)).

Requirements: [FR-COMBAT-004](../requirements/combat-magic-tech.md#fr-combat-004) (attackable targets: objects),
[FR-COMBAT-008](../requirements/combat-magic-tech.md#fr-combat-008) (damage to objects, material reduction),
[FR-ITEM-011](../requirements/items-economy.md#fr-item-011) (items on the ground break),
[FR-ITEM-001](../requirements/items-economy.md#fr-item-001) (a broken container's items lie on the ground) and
[FR-WORLD-013](../requirements/world.md#fr-world-013) (destroyed and busted objects stay so across leaving and saving); the
drawing side of [FR-SCENE-008](../requirements/scene.md#fr-scene-008) (a destroyed door, a busted window) is the viewer's, not
ready.

Every rule was read in the executable by two independent readers
([ledger "Object damage"](../research/ledger/combat.md#object-damage-arc-combat-008), rows OD1–OD12, F2).

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
3. The witnesses of the attacker's PC hear of the broken object (0x4AEE50 with 0: possible hostility).

When the object stays whole, the witnesses hear of the attack (0x4AEE50 with 1: NPCs within 10 tiles guarding the object's
faction lower their reaction by 20, then may turn hostile), and the attacker's weapon or hands wear by the object's material ([wear](attack-resolver-runtime.md#wear-arc-combat-007), ledger CW9). Fatigue and poison do not reach objects.

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
| `AttackObject(expected, attack)` | The hero's attack. Refused before any roll, in order: no hero (`AttackUnavailable`); an invalid target or item, or both or neither given (`InvalidObject`); a destroyed or switched-off object, or an item no longer on the ground (`ObjectUnavailable`); hero items that do not hold (`InvalidWear`); an aim at the torso or a distance outside the reach (`InvalidAttack`); a weapon that is not melee (`AttackUnsupported`); the version; the revision; for a respawning scenery, a full respawn budget (`EventBudgetExceeded`). Then the resolver on the session generator, the fate it used in a revision of its own, and the hit, which writes the object's damage and condition in one revision or damages the item through `DamageItem`. A critical self-hit hurts the hero instead, rolls the critical-effect tables on it and applies them as `AttackCritter` does (ARC-COMBAT-009, `SessionObjectAttackResult.Criticals`; [hero attack](hero-attack-runtime.md#critical-effects-arc-combat-009)) and wears its own armour. The hero's weapon wear follows while the object stays whole. The result names what the host does (`SessionObjectHostSteps`): the trap, the BUST script, the bust or destroy sound, the spill eye candy, and the witnesses (`WitnessesHearAttack` while the object stays whole, `WitnessesHearBreak` when it breaks). The resolution's draws are spent once it ran, also when nothing changes. |

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

## Viewer (ARC-COMBAT-008 part 2)

- **Data.** `CombatFieldReader.ReadBreakable(row)` reads a portal, container, scenery or trap of a map: the common block and the
  break fields (`CombatBreakableFields`: the type block's flags, a scenery's respawn delay, the destroyed ART). The session's
  `SessionObjectFacts.FromCombatFields` turns them into a `SessionObjectBaseline`:
  - a window is a portal whose ART has bit 10 clear;
  - a junk pile is prototype file 3043;
  - respawning (0x20) and busted (0x2) come from the type flags.

  `SessionObjectFacts.ItemFacts` gives an item's facts, and `SessionWearItem.FromCombatFields` its durability values.
- **Targets.** `AreaObjectTargets` names the attackable objects of the shown map:
  - the drawn doors and windows, with the lock domain's entity and baseline;
  - the containers, with their inventory holder's entity, so a spill reaches the holder's items;
  - scenery and traps, with `AreaUseScriptHost.EntityId`;
  - the items on the ground.

  Whether an object's use script is a trap is the caller's answer (`Target(session, key, trapped)`); the viewer passes
  `AreaUseScriptHost.IsTrap` (ARC-COMBAT-011, 0x4BBDD0, ledger OD6): the object's point-1 script as the session holds it (a
  script a use wrote back replaces the loaded one), when it is not 0 and its file's header flags carry 0x1 (non-magical) or 0x2
  (magical). A script the content does not hold or cannot read is no trap, as the original's header read fails (a stored number
  outside 0..99999 included); without script content the answer is unknown and the viewer passes no trap. The flag is read again
  at each hit.

  It also names the objects the session destroyed or switched off. The view no longer draws or picks those, and a destroyed
  door's passage is open. Since ARC-WORLD-065 they block no step either: the live overlay moves their T0 records for the PC
  (its steps and click routes) and for each companion (`AreaCompanionGroup.Removed`), as the original's object lists skip
  destroyed and switched-off objects in the step test (ledger FC1–FC3); a respawning scenery blocks again once it is back.
  The same holds for destroyed doors, windows and traps: their T0 records leave no gap on a step either.
  The tile's own blocking (sector mask, facade, tile name) does not depend on objects and stays. The object-attack smoke
  reports the steps onto the struck object's tile that its T0 record alone blocked and how many are open afterwards
  (`BlockedSteps`, `FreedSteps`; all of them when the object was removed, none while it stands). The next stage,
  `ARC-WORLD-065 native-smoke freed-cell`, takes the map's first container or scenery that blocks steps by itself, marks it
  destroyed in the session, checks those steps are open for the PC's overlay and its click routes, returns its condition to
  what it was and checks they block again. The use reach of a trigger, the hover cursor's route and the main-map corridor still
  read T0 steps (a removed crate on such a step is a detour or a refusal there).

  It works on every map the viewer shows. The exterior and the cave have no session portals, so there only containers and
  scenery are targets. A static container's entity is its inventory holder's `oid:` id, not the `sector:` id of
  `AreaUseScriptHost.EntityId`; a later container lock or use host must use the holder id too.
- **Input.** In combat mode, an Alt-click on a door, window, container or scenery walks within reach, plays the attack and
  runs `AttackObject` with the hero's worn items. The attack repeats while the object stays whole and stops when it breaks.
  Stopping at a break is the viewer's own choice, not read in the executable: a busted window or a spared junk pile would break
  again on every further hit (ODA3).
  This is the original's click (ledger OD14). Without Alt, the object is used as out of combat. An item on the ground is
  picked up and a trap walked to, as the original does, so neither is attacked by a click. The status line names the hit and
  the break (and a trap that would fire, ARC-COMBAT-011), and the log prints `ARC-COMBAT-008 object-attack-hit`. The object-attack smoke
  stage first logs `ARC-COMBAT-011 native-smoke trap-flags`, a census (it checks nothing): the count of the shown map's attackable
  objects whose use script is a trap (NONE without script content; the Crash Site cave has 17 objects and no trap).
- **Wear in play.** The hero's attack on an NPC (`AttackCritter`) now passes both sides' worn items with their durability
  values, so armour and weapon wear happen in play:
  - the hero's kit, from `AreaHeroLoad.KitDurability`;
  - the NPC's items in wear slots, from `AreaCombatHost.WornItems`. An item the session broke counts as taken off, as the
    original takes it off at its first break.
- **Native smoke.** With `--hero`, as the last stage of map mode (the map keeps what the hits did), the hero Alt-clicks the
  drawn door, window, container or scenery with the fewest hit points it can damage. It attacks until the object breaks or
  40 attacks have run. Each hit goes through the session, and a destroyed or switched-off object is no longer drawn. The
  stage prints `ARC-COMBAT-008 native-smoke object-attack PASS`, or `NONE` without such an object. Since ARC-COMBAT-014 the
  candidates are only the objects the player's click can pick: a click-through scenery (OF_CLICK_THROUGH) is never picked, as in
  the original, and a roof-covered body is not drawn, so neither can be Alt-clicked. A candidate is skipped for the next (at
  most eight tried, `Skipped` in the PASS line) when it has no cell within the hero's reach to stand on, when no start a few
  cells away has a planner route to the cell the attack will walk to, or when the pointer finds no point of it; with every
  candidate skipped the stage is `NONE` with the reasons. On the main map the earlier stage chose a click-through scenery
  ("target not visible") and then a start with no route to the approach cell. Known limit: the attack walks to the standable
  cell nearest the object and then nearest the actor (`ApproachCell`), which does not ask whether a route leads there (since ARC-WORLD-073 the attack takes the original's approach instead, a cell a route reaches by construction, [click route](click-route-runtime.md#attack-approach-arc-world-073)); the
  smoke starts where it does, so another reachable cell beside the object is not tried by the attack (the original's move-near
  is not read).

## Differences from the original

| Original 1.0.7.4 | Steamwoven |
| --- | --- |
| The respawn delay multiplies in 32 bits (it wraps beyond 268,435 seconds or 35,791 minutes). | Exact. |
| Each spilled item goes through the drop function (0x466E50, ledger OD13): a second item landing where items lie gathers them all into a new junk pile (a container, prototype 3023), and each dropped item gets an item-decay event two days later; the container drops its last item first. | The items lie loose on the cell in the holder's order, without a pile; since ARC-WORLD-062 each decays two game days later ([item decay](inventory-runtime.md#item-decay-arc-world-062)). |
| A respawning scenery that is off can still be damaged (ODA1). | The hero's attack on it is refused (`ObjectUnavailable`): the player cannot click an object that is off. |
| A container never opened first generates its inventory from its inventory source, then spills it. | The session holds no inventory sources: only the T0 items spill. |

## Not ready

- **Viewer.** It draws a busted window on frame 0 of its T0 ART (the busted ART is not loaded). Since ARC-RENDER-033 a busted
  scenery or trap is drawn from its destroyed ART (field 24) at its T0 anchor and spilled items at the container's cell
  ([ground items](inventory-runtime.md#ground-items-in-the-viewer-arc-render-033)).
  The viewer passes the trap flag (ARC-COMBAT-011) but runs no trap: the trap script with its built-in spells and attacks (30000..30006) is the SCR lane's.
- **Host steps.** The trap and BUST scripts, sounds, eye candy, the witnesses' reaction and the taking-damage script are reported
  to the host, not run here.
- **Others.** NPC attacks on objects (AI), area damage (spells, explosions) and script action 60 on objects (the decay
  of dropped items is in since ARC-WORLD-062).

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
| `AttackObject(expected, attack)` | The hero's attack. Refused before any roll, in order: no hero (`AttackUnavailable`); an invalid target or item, or not exactly one of the target, the item and the wall (`SessionObjectAttack.Wall`, ARC-COMBAT-015) given (`InvalidObject`); a destroyed or switched-off object, or an item no longer on the ground (`ObjectUnavailable`); hero items that do not hold (`InvalidWear`); an aim at the torso or a distance outside the reach (`InvalidAttack`); a wielded throwing weapon (`AttackUnsupported`); the version; the revision; for a respawning scenery, a full respawn budget (`EventBudgetExceeded`). Then the resolver on the session generator, the fate it used in a revision of its own, and the hit, which writes the object's damage and condition in one revision or damages the item through `DamageItem`. A critical self-hit hurts the hero instead, rolls the critical-effect tables on it and applies them as `AttackCritter` does (ARC-COMBAT-009, `SessionObjectAttackResult.Criticals`; [hero attack](hero-attack-runtime.md#critical-effects-arc-combat-009)) and wears its own armour. The hero's weapon wear follows while the object stays whole. The result names what the host does (`SessionObjectHostSteps`): the trap, the BUST script, the bust or destroy sound, the spill eye candy, and the witnesses (`WitnessesHearAttack` while the object stays whole, `WitnessesHearBreak` when it breaks). The resolution's draws are spent once it ran, also when nothing changes. A wall (ARC-COMBAT-015, ledger RH5: the line of fire struck it) is hit without a skill roll, its damage rolled and prevented (`DamagePrevention.Wall`); nothing else changes but the shot's ammunition ([ranged hero attack](hero-attack-runtime.md#ranged-hero-attack-arc-combat-015)). |

State is kept in map changes of the object's entity, which the snapshot already carries, so there is no schema change:

- `object.damage` holds the hit-point damage;
- `object.condition` holds the condition, with a spilled container's cell above its low byte.

A busted window also takes the lock domain's busted bit on frame 0 in the same change (as `SetOpenableBusted`, ledger LK12).
Its busted ART is the busted bit's for the host.

A spilled container lists no items in `QueryMapInventory`: they lie on the ground at its cell, after the seed's ground items,
and `PickUpItem` takes them. Since ARC-WORLD-1306 (ledger VPG7) the spill walks the container's whole list, the items a barter window sold
into it (ARC-WORLD-090, the end of the list) first: a made item becomes a script-created ground item (as a body's dropped made item), an
ammunition stack or an item the hero gave (kit or taken) a placed item of the hero's own, which its pickup or loot returns to its list; both
only with a hero (a save restores a placed hero item only with one), else, and for gold, a key or a kind no ground source admits, the item is
destroyed with the container (a recorded limit, as a body's). Before any change the break checks the created-item bound, the hero's item
bound, the deleted-item records and, when an item is placed, that the container's cell fits the ground record (`DropTileUnfit`); a break that cannot take every item is refused
whole (`DamageObject` and `AttackObject` report `BudgetExceeded` or `DropTileUnfit` as they report a full map-change budget) and the
container stays whole with its sold items (every blow on it is refused too, as the damage and the break are one write, until the hero makes room). A made item the hero sold counts against the created-item bound on the ground, so picking it up again needs room in the hero's list. Saves made before the fix keep the sold items of an already spilled container hidden in its list (lost, as before); only a break after the fix spills them. `CloseMapJunkPile` still leaves a pile that holds a sold item untouched.

A respawning scenery schedules the saved game event `object.respawn`:

- it is scoped to the object's map;
- its values are the time it was set and the delay;
- at most 1,024 can be pending, a project policy (the original has no bound);
- when it fires, it switches the scenery on again;
- a restore refuses one whose scenery is not recorded off.

The registry version stays 1, as when the Lock event was added.

## Damage that is no hero attack (ARC-DEV-255)

A script's damage (60, 138), a spell's damage component, a burn (goals 75/76/77) and a trap's damage reach a portal, container, scenery or
trap through the same state an attack writes, with no new state and no schema step (`object.damage`, `object.condition`). It is the original's
damage apply (0x4B4390) with no attacker (GT194/GT195, ledger OD3, OD4, OD7–OD11):

- `GameSessionOwner.QueryObjectCombat(target)` gives the damage step its defender: the stored material and resistances (clamped 0..95), invulnerable
  at the baseline's flag or for a scenery the session busted; kind `Portal`, `Container` or `OtherObject`. The damage step
  (`AttackResolver.DamageStep`) then takes the **material reduction of a portal or container** (stone, brick, metal, force 10, wood 5, others 0; none
  for scenery and traps) and the stored resistance (skipped by the unresistable damage 138 / flag), never armour, difficulty or a critical.
- `GameSessionOwner.DamageObject(expected, target, amount)` adds the step's hit-point total to the object's damage (`ObjectDamageRules.Hit`) and, at
  current hit points 0 or less, breaks it (`ObjectDamageRules.Break`: a door is destroyed, a window busts, a container spills and is destroyed, scenery busts
  or respawns, a trap busts or is destroyed). An amount of 0 or less changes nothing; a destroyed or switched-off object is refused
  (`ObjectUnavailable`). `SessionObjectDamageResult.Host` names what the host owns: `TrapFires` (set together with the break of a trapped object; the trap
  runs **before** the break in the original, OD4), the bust script and sound, the destroy sound and the spill eye candy, and (ARC-SCRIPT-2620) `ObjectDestroyed` for a scenery, trap or container the break destroyed: the viewer then ends the object's goal runs and tells its spell runs, as a script's destroy does (a door or window, an item and a busting first break carry none), and (ARC-SCRIPT-2646, OD7) `ObjectBroken` for every break of a container, scenery or trap: the interrupt of the object's goal runs at level 5, which ends a scenery's loop before the BUST script and the destroy. There are no witnesses (no attacker).
- Viewer: `AreaSpellCastMap` answers `ObjectCombat` and `DamageObject` for the rows the object-attack host names now (`AreaObjectTargets.Target`: doors and
  windows, containers, scenery and traps, not yet destroyed or off), so a spell, a burn and a trap damage reach them through the cast host unchanged. A
  script's damage (`AreaUseScriptHost`) is applied to such an object of the shown map as well (`DamageByScript` maps the script's entity to the cast id
  through `AreaObjectTargets.KeyOfEntity` and `AreaSpellCastMap.IdOf`); `TakeObjectHits` reports each hit for the log (`ARC-DEV-255 object-damage`) and
  the view redraws from the session's version as it does after an attack. A static row of a sector now has the cast id `sector:S:N` (the entity a script
  names it by); `row:Static:N` repeated from sector to sector, so two statics shared one id. A save made before holds such an id in a saved spell run that names a static object: the run loses that object on load (the old id was ambiguous, so no mapping exists; Original and Steamwoven alike, a technical fix).
- Since ARC-DEV-260 the viewer runs what `Host` names, for this path and the hero's attack alike (see [the break's trap and BUST script](#the-breaks-trap-and-bust-script-arc-dev-260)).
- Since ARC-AI-2124 a spell's hit with an attacker (its caster) is taken by the viewer the moment the damage step queues it (`AreaSpellCastMap.ObjectHitMade`: damage line, break steps, guard witnesses), as the original's damage end does it inside each object's step; damage with no attacker (script, burn, trap) still waits for the cast log's flush. The viewer's own "damaged" cast-log line (a status and message window entry, not an original step) is still posted at the flush, so it follows the hit's floater, break messages and witness reactions.
- NOT READY: the object's script
  at point 0x17 (asked since ARC-DEV-254 and ARC-DEV-261 for a script's, a spell's, a burn's, a trap's and the hero's hit damage, with the session entity of the object, and since ARC-COMBAT-1141 for the script kill, the poison tick and each object of an acid chain (a worn item's own script is not run: it is no row of the shown map, so its ask answers run default); GT210–GT212); the witnesses of a PC's spell hit; objects of another map than the shown one; the damage floaters. Read in the executable once (GT195, F1), the object branch twice (OD1–OD12, F2); this slice read nothing new.

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
  runs `AttackObject` with the hero's worn items. Since ARC-COMBAT-015 "within reach" is the attack goal's reach test (ledger RH1):
  also a clear line of fire from the hero's tile, for every weapon, so an object next to the hero across a wall edge or behind
  cover worth 26 or more has no approach and ends the goal at once (AP4); a farther one is approached. The attack repeats while the object stays whole and stops when it breaks.
  Stopping at a break is the viewer's own choice, not read in the executable: a busted window or a spared junk pile would break
  again on every further hit (ODA3).
  This is the original's click (ledger OD14). Without Alt, the object is used as out of combat. An item on the ground is
  picked up and a trap walked to, as the original does, so neither is attacked by a click. The status line names the hit and
  the break (and a trap that goes off, ARC-COMBAT-011), and the log prints `ARC-COMBAT-008 object-attack-hit`. The object-attack smoke
  stage first logs `ARC-COMBAT-011 native-smoke trap-flags`, a census (it checks nothing): the count of the shown map's attackable
  objects whose use script is a trap (NONE without script content; the Crash Site cave has 17 objects and no trap).
- **Wear in play.** The hero's attack on an NPC (`AttackCritter`) now passes both sides' worn items with their durability
  values, so armour and weapon wear happen in play:
  - the hero's kit, from `AreaHeroLoad.KitDurability`;
  - the NPC's items in wear slots, from `AreaCombatHost.WornItems`. An item the session broke counts as taken off, as the
    original takes it off at its first break.
- **Native smoke.** With `--hero`, as a late stage of map mode (the map keeps what the hits did), the hero Alt-clicks the
  drawn door, window, container or scenery with the fewest hit points it can damage. It attacks until the object breaks or
  40 attacks have run. Each hit goes through the session, and a destroyed or switched-off object is no longer drawn. The
  stage prints `ARC-COMBAT-008 native-smoke object-attack PASS`, or `NONE` without such an object. Since ARC-COMBAT-014 the
  candidates are only the objects the player's click can pick: a click-through scenery (OF_CLICK_THROUGH) is never picked, as in
  the original, and a roof-covered body is not drawn, so neither can be Alt-clicked. A candidate is skipped for the next (at
  most eight tried, `Skipped` in the PASS line) when it has no cell within the hero's reach to stand on, when no start a few
  cells away has a planner route to the cell the attack will walk to with a clear shot from that cell (ARC-BUG-1536: a wall on the target's own
  tile blocks the line from a diagonal approach, the original's rule, so a start whose approach cell has none is not used; a ranged
  weapon's approach cell is not tested, its walk stops at the first clear cell), or when the pointer finds no point of it; with every
  candidate skipped the stage is `NONE` with the reasons. On the main map the earlier stage chose a click-through scenery
  ("target not visible") and then a start with no route to the approach cell. Known limit: the attack walks to the standable
  cell nearest the object and then nearest the actor (`ApproachCell`), which does not ask whether a route leads there (since ARC-WORLD-073 the attack takes the original's approach instead, a cell a route reaches by construction, [click route](click-route-runtime.md#attack-approach-arc-world-073)); the
  smoke starts where it does, so another reachable cell beside the object is not tried by the attack (the original's move-near
  is not read).

## Differences from the original

| Original 1.0.7.4 | Steamwoven |
| --- | --- |
| The respawn delay multiplies in 32 bits (it wraps beyond 268,435 seconds or 35,791 minutes). | Exact. |
| Each spilled item goes through the drop function (0x466E50, ledger OD13): a second item landing where items lie gathers them all into a new junk pile (a container of description 3023, prototype 3043), and each dropped item gets an item-decay event two days later; the container drops its last item first. | Exact since ARC-WORLD-063 ([junk piles](inventory-runtime.md#junk-piles-arc-world-063)): the items drop last first through the session's placement and decay two game days later (ARC-WORLD-062); the session's limits of the placement (scripts not run, no stack merge) apply. |
| The destroy point (2) of the break's destroy branches runs after the trap and after the break wrote the damage and dropped the items (0x43F1C0 → 0x43CCA0). | The point runs inside the session's break write, before the trap (the host runs the trap after the session call) and before the break's writes, so it sees the object as it was before the hit (ARC-SCRIPT-2644); the answer, the kept object, the drop and the sound are as the original's. A point that changes its own object (damages, spills or destroys it) ends the outer break's writes (its nested break is the state). A run that did not apply, or no script content, keeps nothing on a guess: a failed run keeps the object, a missing content lets it go. The point's printed message lines and dialog are not shown; its awards, floats and creations are taken by the viewer's break step. |
| A respawning scenery that is off can still be damaged (ODA1). | The hero's attack on it is refused (`ObjectUnavailable`): the player cannot click an object that is off. |
| The spill walks the container's whole list, last first (0x463730, mode 1), so the items a barter window sold into the container (the end of its list) drop first. | Exact since ARC-WORLD-1306: the sold items are placed on the ground and drop first, then the seed's items; the limits above (sold items destroyed when the hero or a ground source is missing) are recorded. |
| A container never opened (flag 0x200) first generates its inventory from its inventory source (the clear keeps the persistent items, RK5), then spills the whole list last first (0x463730, RK13). | Exact since ARC-WORLD-1301 when the host gives the generation's facts (`GameSessionOwner.GenerateOnceWorld`, or the event pass's world): the break runs the same seeded generation as the first loot open (tile x, the hero's race, gender, background and player name), records the container as generated, and the spill's walk drops the kept items and the goods through the placement with their decay. The generation is one revision of its own: a break the walk's budgets refuse leaves the container generated and whole, as a first open would. Limits: without the host's facts (no world, not ready, no seed facts, a set the session does not admit, a transfer script, a full budget) the break spills the stored items and the container stays ungenerated; the generated gold piles are destroyed with the spill (the ground holds none, as for a sold pile). |

## The break's destroy point (ARC-SCRIPT-2644)

The destroy branches of the break (ledger OD8–OD11: a door, a container after its drop, a trap with no destroyed ART, a scenery already busted) call the object
destroy (0x43CCA0), which runs the object's destroy point (2) first; a skip default keeps the object (TD2). The session calls the gate the host attached
(`GameSessionOwner.AttachObjectDestroyGate`; the viewer attaches `AreaUseScriptHost.BreakMayDestroy` with the prototype scripts) once per such break, after the
container's generation and the spill walk's budgets and before the break's own writes. A refusal (the point's skip default, or a run that was not applied) writes
the hit's damage and a container's spill as before, leaves the object's condition as it was (a kept container is no spilled one: its seed items lie loose
through the dropped-item change), leaves `SessionObjectHostSteps.ObjectDestroyed` out of the host steps and keeps the sound or the spill eye candy. Without a
gate every such break destroys. The same gate serves every break path (the hero's and an NPC's melee and landed shots, a spell, the burn, a script's and a trap's damage).

## Damage to items that is no hero attack (ARC-COMBAT-025)

The same damage apply (0x4B4390) with no attacker also reaches an **item** (types 5..14): the gates (OD1), the stored resistance of the item (OD2) and
**no material reduction** (OD3: only portals and containers have one), then the item's own durability instead of the object state (OD12, DU1–DU4).
A script's damage (60, 138), a spell's damage component, the burn of a burning tile and a trap's damage reach it through the cast map; an acid's steps
(DH9: every item in the wear slots 1000..1008 becomes the damage step's target) reach the items a critter wears. There is no new state and no schema
step: the item's damage and its two-step break are the durability records (`DamageItem`, schema 22).

- `GameSessionOwner.QueryItemCombat(item, facts)` gives the damage step its defender: the item's stored material and resistances (clamped 0..95) and its
  invulnerable flag (the item's or the facts'); kind `OtherObject`. Null for an invalid item or facts (anything but an item's).
- `GameSessionOwner.DamageItemUnattacked(expected, item, facts, amount, onGround)` adds the step's hit-point total (a negative amount counts as 0) through
  `DamageItem`: at current hit points 0 or less the item breaks (first broken: both arts take the destroyed bit, the damage resets to 0, the hero's worn item leaves
  its slot and goes to the pack, an NPC's drops out of the host's worn list (`AreaCombatHost.Worn` skips a broken item); broken again: deleted). `SessionItemDamageResult.Host` names what the host owns: `TrapFires` (a trapped item that
  broke), the bust script and sound, the destroy sound. No witnesses (no attacker). An item lying on the ground (`onGround`) must still be loose in the seed
  (not taken, deleted, in a container or in a junk pile: `ObjectUnavailable`); a carried item is the caller's to name, the session refusing one it does not track
  (`ItemNotAvailable`).
- Viewer: `AreaSpellCastMap` answers `ObjectCombat` and `DamageObject` for an item row the object-attack host names as an item lying on the ground now
  (`AreaObjectAttackTarget.Item`) and for the items a worn-items query listed (`WornItems`, asked by the acid's plan before its steps): an NPC's from the session's worn
  list (its stored fields), the hero's starting kit from its prototypes (`AreaCombatHost.KitItemFacts`; any other worn item of the hero leaves the list empty, as the
  attack's wear does). An item taken off since the query is no target. A worn item whose stored material does not read is left out of the acid's list (the original lists every item in the wear slots; real data has none). `TakeItemHits` reports each hit for the log (`ARC-COMBAT-025 item-damage`).
  The burn of a burning tile lists the loose items of the tile after its critters (a project choice; the victims are independent, so only the log order differs from the original's single object list) (`AreaSpellCastMap.GroundItemsAt`, `AreaFireBurner`): they count as victims for
  the round gate.
- Read in the executable: nothing new. The object branch (OD1–OD12), the acid's steps (DH9) and the burn's victim types 5..16 (GT94–GT97) were read twice or
  more; this slice applies them to the item type. The item's taking-damage script (point 0x17) is asked where ARC-DEV-254 and ARC-DEV-261's hook asks it (by the item's cast id); the witnesses are not modelled.
- The viewer runs an item's trap and BUST script as it runs any broken object's (ARC-DEV-260, `RunObjectBreakSteps`) for an item lying on the map; a worn item has no map row, so its BUST script is not run.
- The viewer floats the damage line of a hit on an item lying on the map (`HT N`, the packet's amount, ARC-COMBAT-1138, `AreaItemHit.Amount`); a worn item has no cell to float over. Not done: a hit whose amount the material takes all of (OD3 floats `HT 0` over the object) queues no object hit, so it floats nothing on a portal or container; an item's zero hit does float `HT 0`.
- NOT READY: an item in a pack, a container or a junk pile is never reached (areas skip them); a
  non-kit item the hero wears; items on another map than the shown one; an NPC's item broken mid-pass is dropped by the host's build, not by the session.

## Not ready

- **Viewer.** It draws a busted window on frame 0 of its T0 ART (the busted ART is not loaded). Since ARC-RENDER-033 a busted
  scenery or trap is drawn from its destroyed ART (field 24) at its T0 anchor and spilled items at the container's cell
  ([ground items](inventory-runtime.md#ground-items-in-the-viewer-arc-render-033)).
  The viewer passes the trap flag (ARC-COMBAT-011); since ARC-DEV-260 it runs the trap and the BUST script of a broken object
  ([below](#the-breaks-trap-and-bust-script-arc-dev-260)).
- **Host steps.** Sounds, eye candy and the witnesses' reaction are reported to the host, not run here; the trap and BUST scripts are the host's since ARC-DEV-260.
- **Others.** An NPC's ranged attack reaches objects since ARC-COMBAT-1140 (`GameSessionOwner.AttackObjectByNpc` for the object that stops its line of fire, `LandNpcShotOnObject` for the arrow's strike; the damage step of this section with the NPC as the attacker, no witnesses, the trap and BUST script run with no triggerer or breaker, see tail T4e of the goal table); an NPC's melee attack on objects is not modelled; area damage reaches objects since [ARC-DEV-255](#damage-that-is-no-hero-attack-arc-dev-255) and items since
  [ARC-COMBAT-025](#damage-to-items-that-is-no-hero-attack-arc-combat-025) (the decay of dropped items is in since ARC-WORLD-062).

## The break's trap and BUST script (ARC-DEV-260)

Ledger [OD4 and OD15](../research/ledger/combat.md#object-damage-arc-combat-008). A hit that brings a door, window, container, scenery, trap or
item to 0 hit points reports `SessionObjectHostSteps.TrapFires` (the object's use script is a trap) and `BustScript` (its break dispatches
point 30) in `SessionObjectAttackResult.Host` and `SessionObjectDamageResult.Host`. The viewer runs them in `AreaObjectBreakHost.Run`
(`TerrainViewerTraps.RunObjectBreakSteps`), after the hero's attack (`FinishObjectHit`, also for a projectile that lands) and for each object hit
that damage with no attacker dealt (`LogObjectHits`):

- **Trap (`AreaTraps.Attack`, cause `SessionTrapCause.Attack`).** No spot attempt, no write-back, so an auto-removing trap stays. A native run
  (30000..30006 except the 30002 shot) applies its effect through the cast host; the hero is its triggerer for a melee attack of the hero, none for a
  ranged attack (a projectile that lands, ODA4) and for damage with no attacker (`AreaTrapEffects.ApplyWithoutTriggerer`). An ordinary trap script (no native run)
  runs as the use point (`AreaUseScriptHost.RunTrapScript`) with the same triggerer. A native trap with no cast host is not met (not consumed).
- **BUST script (`AreaUseScriptHost.RunBust`, `ScrPoint.Bust` = 30).** The object is the attachee, the breaker the triggerer: the hero for any attack of the
  hero, ranged or not; none for damage with no attacker. It runs under the heartbeat admission set; its answer is ignored (as the original's break
  ignores it); its slot is kept as map changes (`scr30.*`). A door, a container and every destroyed-instead branch (a scenery or trap already busted or
  with no destroyed ART, an item deleted) run none.
- **Differences from the original.** The session applies the break in one revision, so the trap and the BUST script run after it, not as the original
  orders them (trap, then the break, whose BUST script follows the object's own change; a respawning scenery's BUST script runs before it is switched
  off). Only a script that reads the object's own state (switched off, busted, its art) sees the difference. The trap's own damage to objects queues
  further hits: while a trap or script runs, `LogObjectHits` stands off and the outer loop takes the hits in at most 8 rounds (a window breaks again on every hit, ODA3, so its BUST script can chain). A ranged weapon with no missile art is still ranged (no trap triggerer). The hits' sounds and the witnesses stay unmodelled; each hit's damage line floats (ARC-COMBAT-1138, `LogObjectHits` -> `AreaCombatFloaters.Of(AreaObjectHit)`, the amount the hit added to the object's damage, "text floaters" 2 only). A BUST script whose effects the viewer cannot apply is left unapplied, as for any script run here.

# Hero attack runtime

Status: **Implemented bounded component**, ARC-COMBAT-005 (2026-10-17), top-20 item 10. The hero's first attack in the AREA
viewer, in real time and without AI: in combat mode a left click on a living NPC walks within reach, plays the hero's attack
animation at the rate of its Speed and weapon, resolves the hit on the animation's action frame through the
[attack resolver](attack-resolver-runtime.md), applies the damage to the [life state](life-state-runtime.md), pays the
[experience](experience-runtime.md) of the hit and the kill, repeats with the auto-attack setting while the target lives, and
draws a killed or knocked-out NPC in its fallen pose; a killed NPC can then be looted. It works on every map the viewer loads
(the pinned Crash Site exterior and cave and every whole map of `--area-map`), because NPC combat values come from each map's own
effective object fields. No NPC fights back. Requirements:
[FR-COMBAT-004](../requirements/combat-magic-tech.md#fr-combat-004) (the target and the reach),
[FR-COMBAT-006](../requirements/combat-magic-tech.md#fr-combat-006) (the attack animation's hit, partly),
[FR-COMBAT-011](../requirements/combat-magic-tech.md#fr-combat-011) (the attack click, cursor and feedback, partly) and
[FR-CHAR-016](../requirements/characters-skills.md#fr-char-016) (combat skill levels). Since ARC-COMBAT-007 the attack also
wears armour and weapons ([FR-COMBAT-008](../requirements/combat-magic-tech.md#fr-combat-008),
[FR-ITEM-011](../requirements/items-economy.md#fr-item-011); [attack wear](attack-resolver-runtime.md#wear-arc-combat-007)), and
since ARC-COMBAT-009 a critical hit rolls the original's critical-effect tables and applies them
([FR-COMBAT-009](../requirements/combat-magic-tech.md#fr-combat-009), [below](#critical-effects-arc-combat-009)).

## Evidence

- The original's functions, read first: the in-game click on a target (0x54ED30), the skill functions (skill base,
  level, key-stat cap, training), the monstrous test (0x45F730), the object resistance, combat damage (0x4B4390, the
  dying animation) and the animation functions (the attack and attempt-attack goals, the death and fall-down
  animations).
- The retail 1.0.7.4 `Arcanum.exe`, read statically by two independent readers per the
  [original executable check](../research/original-executable-check.md); rows FA1–FA10 of the
  [verification ledger](../research/ledger/combat.md#hero-attack-arc-combat-005). Three rows differ from that first
  reading: a click on a follower of the hero does nothing unless left Alt is held (FA2), a corpse click without Alt does
  nothing (FA3), and the click does nothing when the PC is not active, is stoned, or is stunned or paralysed (FA6); none
  is a bug of the original, so there is no rule-mode switch.
- [Function registry](../verification/README.md) rows (F2, two readings): skill points, base level, level, key-stat cap and
  training (`CombatSkillRules`, a view of [`SkillRules`](skill-rules-runtime.md) since ARC-SKILLS-001; 0x4C5FA0, 0x4C5E50, 0x4C5EB0, 0x4C5F70, 0x4C60C0) and the critter resistance
  (`CombatantRules.Resistances`, 0x43D6D0); no function oracle covers them yet.
- The installed data: the twelve pre-made PC prototypes store no skills (the pre-made hero reader refuses one that does); since
  ARC-WORLD-036 their schemes buy skills at the start and since ARC-SKILLS-002 the attack reads them ([hero skills](hero-skills-runtime.md)). The
  Crash Site creatures are animals and monsters with stored hit-point adjustments of −3 to −6.

## Rules

| Rule | Value |
| --- | --- |
| Click in combat mode (FA1–FA6) | Nothing while the hero cannot act; a living NPC or PC: attack, Shift (left or right) the attempt-attack goal (here run as the same attack); a follower of the hero only with left Alt held; a corpse only with Alt (left or right), which loots it, else nothing; held `,` `.` `/` aim at the head, an arm, a leg. Out of combat mode the click talks or loots as before. |
| Skill level (`CombatSkillRules`, FA7) | 4 per stored point (bits 0..5), or 20 for the melee of a monstrous critter, then the skill's effect changes, clamped 0..20, then capped by the dexterity table `3,3,3,3,3,7,7,7,11,11,11,15,15,15,19,19,19,20,20,20` (dexterity clamped to 1..20); all five combat skills (bow, dodge, melee, throwing, backstab) key on dexterity. |
| Training (FA8) | Stored bits 6..7; a monstrous critter's melee takes the highest tier whose melee level and character level reach 1/10, 9/20, 18/30 in order. |
| Monstrous (FA9) | Mechanical, monster, animal or undead critter flag. |
| Resistance (`CombatantRules.Resistances`) | Stored + worn armour (with its magic, scaled for the wearer) + `5 × (CN − 4)` for poison when positive, clamped 0..95 unless a monstrous NPC (the resistance getter 0x43D6D0, ledger EQ8); resistance effects are not applied. |
| Reach | The weapon's range, 1 bare-handed; farther away the click route walks (running, as every object approach) (since ARC-WORLD-073 the original's approach: the cell one step short of the target on the shared request's route into its tile, [click route](click-route-runtime.md#attack-approach-arc-world-073)) first, the attack goal's move-near step. |
| Animation | The hero's bare-handed attack ART (animation 20, weapon slot 1: 9 frames, 15 fps, action frame 6) played by [`RealTimeCombatRules`](real-time-combat-runtime.md#rules-realtimecombatrules) at the rate of the hero's Speed and the pause of its weapon speed; one attack sound roll (1..4) per attack start; facing the target. |
| Hit | The update that shows the action frame runs the hit once when the target is still within reach (else the attack ends without one): `GameSessionOwner.AttackCritter`. |
| Repeat | Auto attack (on, the original's default) starts the attack again 5 ms after it ends while the hero is in combat mode, no round runs and the target is alive. A click elsewhere, the stick, a dialog or window, a quick load, the hero leaving the map or combat mode (refused while an animation plays: the exit gate's attempt-attack test) or losing the ability to act stop it; a refused hit ends it; the same target clicked again keeps its goal. |
| Dying pose (FA10) | A death-flag hit or a death of at most 20 hit points falls (animation 7); otherwise a head hit decapitates (17), a leg hit severs the leg (19), anything else blows out a chunk (18); a knockout falls (7). The body keeps the last frame of that animation in its facing (group 0 for a single-rotation pose ART); a bloody death without its file falls (the original's missing-art repair). |

## Session API (`GameSessionOwner`)

| Entry | Contract |
| --- | --- |
| `SessionCritterCombat.From(entity, fields, carried)` | One NPC's combat values from its ARC-DATA-021 fields and the fields of the items it carries (those in a wear slot count): life facts (pools, level, effective ST under each poison penalty, CN, WP, HealRate, PoisonRecovery, normal resistance, traits from the critter, object and spell flags), the loaded life state (hit-point and fatigue damage, poison, stun and injury flags), the experience worth and pool, and `Combatant(status, state, profile)` with its effective stats under its poison and crippled legs, armour class, resistances, dodge and backstab, light and conditions. Effective stats are the character owner's for the NPC's race and gender over its stored base stats, each clamped to the race's range first (monsters store values beyond it); its stored effects are not applied. Typed `SessionCritterCombatFailure`, never an exception for data. |
| `QueryHeroAttack()` | The hero's `Combatant`, its weapon (bare hands without one), the weapon's skill with its level and training (`QueryHeroSkills`, ARC-SKILLS-002; dodge and backstab too), weapon speed with that training, Speed, range and whether the attack is supported (melee only). Null without a hero. |
| `AttackCritter(expected, attack)` | Refused before any roll: no hero (`AttackUnavailable`), an invalid target, an aim at the torso or an undefined location, a distance outside 0..1 (`InvalidAttack`), a weapon that is not melee (`AttackUnsupported`), the version, an exhausted revision (`RevisionExhausted`). Then the resolver on a trial copy of the session generator (`RandomExhausted` when it runs out), adopted once it ran; a resolver refusal (the hero cannot act, the target is dead) is NoChange with the resolution. A fate the resolver used (the hero's armed critical hit, ARC-WORLD-054, [fate points](fate-points-runtime.md)) is cleared in a revision of its own, so such an attack is Changed even without damage. Then `ApplyDamage` on the target or, on a critical self-hit, the hero; then, with experience data, `RecordNpcDamage` for an NPC that is not stoned and `RecordNpcKill` on a death; last the attack's wear (`ApplyAttackWear` with the hero as attacker and the NPC as target, `SessionAttackResult.Wear`). Each step commits its own revision; a later refusal is `StepFailure` and leaves the earlier steps. `SessionAttack.HeroWorn` and `TargetWorn` carry the items in the two sides' wear slots with their durability baselines (the hero's exactly its worn items, else `InvalidWear` before any roll); null skips that side's items (the bare-handed hero's own wear still applies). |
| Critical effects | ARC-COMBAT-009, [below](#critical-effects-arc-combat-009): the resolver runs with `OriginalCriticalEffects`; `SessionAttack.TargetCell` is where the NPC's dropped items go; `SessionAttackResult.Criticals` reports the item steps. |
| `ApplyAttackWear(expected, wear)` | ARC-COMBAT-007, for any attacker and recipient (the NPC AI will call it for NPC attacks): in the original's order, the armour at the hit location of whoever took the damage (`DamageItem` + 10), that critter's wielded weapon on the critical "damage weapon" (+ 10) and "destroy/explode weapon" (`DestroyItem`) unless invulnerable, and the attacker's weapon (`DamageItem`) or, bare-handed, its own hit points (never the last one). Each step is reported (`SessionWearStep`: kind, slot, item, amount, skip reason, durability result or life outcome); a first break of the hero's item takes it off its slot (item durability), of an NPC's item is reported for the host. `SessionWearItem` carries an item's entity, durability baseline, invulnerable flag and weapon art subtype, all host data passed at each attack (`SessionWearItem.FromCombatFields`); the hero's attack takes its weapon's subtype from it. Refused before any change (`InvalidWear`): no attacker critter, the same critter on both sides, a hero side that is not exactly the hero's worn items, an NPC side naming one of the hero's items, items on an object side, an invalid entity, baseline or subtype, an NPC item a critical dropped (ARC-COMBAT-009), an item the session does not track or deleted (a host leaves out items an earlier attack deleted, `IsItemDestroyed`; the `ItemDeleted` skip is only for an item a step of the same wear deleted), armour or weapon steps on an object; then the version. A refused step ends the wear: `StepFailure` after changes, Rejected when nothing changed. |
| `AttackObject(expected, attack)` | ARC-COMBAT-008: the hero's attack on a portal, container, scenery, trap or an item on the ground ([object damage](object-damage-runtime.md)). |
| `QueryNpcKilled(entity)` | Whether the session's record of the NPC holds the pinned death damage. |
| Loot | `LootItem` and `QueryMapInventory` count an NPC the session killed as dead, besides those its map data shows dead. |

No snapshot change: deaths, damage and experience are already saved by the life and experience domains.

## Critical effects (ARC-COMBAT-009)

`AttackCritter` and the critical self-hit of `AttackObject` pass `OriginalCriticalEffects` to the resolver, so a critical rolls
the original's tables ([attack resolver](attack-resolver-runtime.md#critical-effects-arc-combat-009), ledger CR1–CR15) from the
session generator, in the original's order. The recipient's `Combatant` carries what the tables read: its body type (an NPC's
critter field 227; every pre-made PC has 0, so the hero's is 0), effective constitution and its race's maximum, whether it is
scarred, and its worn helmet, weapon and armour pieces (the hero's from its equipment; an NPC's from `SessionAttack.TargetWorn`
when the host passes it, else its map data).

The effects reach whoever took the damage, the target or, on a critical self-hit, the hero. The item steps keep the original's
relative order (0x4B4390, CW8, CR12–CR13) but follow the whole life step, where the original applies them before the hit-point
damage and death; no state or draw depends on that difference today:

| Step | Contract |
| --- | --- |
| Life | `ApplyDamage`: stun (only on a critter that can act), knockdown, knockout (fatigue damage = maximum + 10..20, drawn), scar, blinding, crippling (the second arm at d100 ≤ 50, drawn). |
| Arm-hit drops | After a crippling arm hit (`LifeEvents.ArmHitDropsItems`, also when nothing new was crippled): the wielded weapon, then the shield, each dropped when the wear check (`EquipmentRules.CheckWear`, 0x464D20) refuses it under the injuries now. An NPC's check reads its map data's item (an item it lists none for is `Unknown`). |
| Wear | `ApplyAttackWear` without the items that left: the armour's damage, the weapon's critical damage and destruction, the attacker's side. |
| Helmet drop | `DropHelmet` (an item a step of this attack deleted or dropped is `NoItem`). |
| Ammunition | ARC-COMBAT-013 (CR15, AM4): unless the weapon was destroyed (CW3), `DestroyAmmo` (0x40000) spends every unit of the hero's stack of its wielded weapon's ammunition type and `LoseAmmo` (0x20000) five times the weapon's consumption, all or nothing, through `SpendAmmo` (a stack spent to 0 is deleted): `AmmunitionSpent` with the stack and `Units`, or `NoAmmunition` when the hero holds none of the type, too few, or the count is 0 (a consumption of 0); a weapon without an ammunition type never rolls them, and a type outside 0..3 names no stack (AMA3). The weapon is the attack's, taken before a step of the same attack broke or dropped it. An NPC's are `AmmunitionNotReady` (no NPC attacks; an NPC without a PC leader would lose nothing). A refused spend (a budget, the revision) is the attack's `StepFailure`. |
| Weapon drop | `DropWeapon` (`NoItem` as above). |

A drop (`SessionCriticalStep`, `SessionCriticalStepOutcome`): an NPC's item that its map seed lists in the NPC's list goes to the
ground as a map change (`ItemDroppedProperty` = `item.dropped`, the cell packed with 24 bits per
coordinate, + 1), so no schema changes; it then lies on the ground in `QueryMapInventory` (after the spilled containers' items),
leaves the NPC's list (loot, negative `IN`, the NPC crafter and the NPC inventory store no longer see it) and `PickUpItem` takes
it. The hero's item goes to the ground among its placed items (ARC-WORLD-059,
[placed items](inventory-runtime.md#placed-items-arc-world-059)): it leaves its slot and list until it is picked up. The tile is
the [drop cell](inventory-runtime.md#drop-cell-arc-combat-010)'s (ARC-COMBAT-010): with the host's `SessionAttack.DropMap` the
session draws the distance 2..4 before the drop's refusals and one direction per try after them, and the item lands where the
original's tries lead from its holder's cell (the hero's, or `SessionAttack.TargetCell` for an NPC); without a map it lands on the
holder's cell and nothing is drawn (`HeroCellUnfit` when the landing cell does not fit the packing). Not applied: an item the session added to the NPC (`NotSeedItem`), an item with a drop script (`ScriptUnsupported`), no cell or no item data
(`Unknown`); an item with the no-drop flag is `Refused`, as in the original (`SessionWearItem.NoDrop`). A dropped item is no
longer worn: a `TargetWorn` that still lists it is refused (`InvalidWear`), and `SessionAttack.TargetCell` outside 0 .. 2^24 − 1
is `InvalidAttack`, both before any roll.

**Not ready**: the NPC search budget per real second of the drop's path (DCA2). Also not modelled: the NPC's look-for-weapon/armour flags after a drop, the PC's injury record (0x4F5690), the worn armour's
critical scripts (points 34, 35), the drop script, the stun animation and its duration (goal 57) and an NPC's ammunition. An NPC's
armour class, resistances and light still come from its map data's worn items, so a dropped or destroyed piece keeps counting
there (the critical tables read the host's `TargetWorn`).

## Reach and line of fire (ARC-COMBAT-012)

Requirements: [FR-COMBAT-007](../requirements/combat-magic-tech.md#fr-combat-007) (ammunition per shot, partly) and
[FR-COMBAT-004](../requirements/combat-magic-tech.md#fr-combat-004) (the line of fire chooses the struck object). Read in the
retail 1.0.7.4 executable by two readers ([ledger RA1–RA10](../research/ledger/combat.md#reach-and-line-of-fire-arc-combat-012)).

| Rule | Value |
| --- | --- |
| Ranged path (RA1) | A weapon whose range is above 1 takes the original's ranged path even when its skill is melee; the installed data have two such weapons (prototypes 6063 and 6072, range 15, the throwing dagger among them; opt-in census `WeaponReachCensusTests`). The hit still wears the weapon (CW10 closed). |
| Range terms (RA2) | A melee-skill hit at reach has no range limit, range penalty or line-of-fire difficulty: those belong to the bow, throwing and firearms skills (the shared check's distance flag). |
| Ammunition per shot (RA3) | A weapon with an ammunition type spends its per-shot count from the hero's stack of that type after the hit-location roll and before every other roll, all or nothing: too few units refuse the attack (`AttackRefusal.OutOfAmmunition`, no later roll); a miss has spent its units. The refusal's other effects in the original (sound 3, flag 0x400, the PC's auto-switch re-wield) are not ready. |
| Line of fire (RA4–RA10) | `LineOfFireRules.Trace` (0x4ADE00): the pixel line between the two tiles' screen centres in 10-pixel samples from the attacker's tile and pixel offset; each tile change priced by `StepCost` (0x43FE00 with the projectile flags) over an injected `ILineOfFireMap`: walls by their piece (a window piece passes an edge step and blocks a corner step, a doorway piece passes, any other blocks, a pass-walled one takes the cover test), doors and windows open or busted pass and closed ones take the cover test, dead critters pass, other objects take the cover test (not shoot-through stops the line, shoot-through and opaque +50, see-through cover +20); the target tile's own objects are skipped. More than 1,000 pixels: cost 100, no blocker. |
| Corner step (AW13, AW14) | `CombatAnomalyProfile.CornerStepCountsBothRoutes` (Original): both routes around the corner count the corner tile's objects and the last step skips the side tiles' objects; Steamwoven counts the corner tile's objects on the first route only and skips only the target tile's objects. |
| Redirect (RA4) | A blocker other than the target becomes the struck object: an NPC takes the normal attack, a door, window, container, scenery or trap the object attack (no skill roll). |

Session API: `SessionAttackResult.Ammunition` and `SessionObjectAttackResult.Ammunition` report the spend (`SessionAmmoUse`, the
[ammunition owner](inventory-runtime.md#ammunition-arc-world-066)); a spend the owner rejects after the resolution ran is
`StepFailure`. `AttackRequest.AmmunitionShort` asks the resolver for the refusal. The session's hero is the PC, so the
original's "an NPC without a PC leader spends nothing" has no caller yet (no NPC attacks).

Viewer: `AreaLineOfFire` is the shown map as the line of fire reads it (built like `AreaDropMap`: the step evidence's tiles, the
walls' pieces through `AreaWallPieces`, the session's doors and windows, the map's rows of the original's projectile mask less
what is destroyed or switched off, the companions on their live cells). When the hero's reach is above 1, the hit traces the line
from the hero's tile to the target's; a blocker is struck instead (`ARC-COMBAT-012 line-of-fire strikes …`), and a wall, a
companion or an NPC without combat values as blocker ends the goal without a hit (not ready). Native smoke
`ARC-COMBAT-012 native-smoke line-of-fire PASS` (map mode, last stage): the hero stands next to an NPC or attackable object that
stops the line to another NPC, attacks the farther NPC with a lent reach (no kit holds a reach weapon) and the hit goes to the
nearer one; on `arcanum1-024-fixed` a scenery five cells short of the target took it.

Limits:

- The redirect is decided before the session's attack runs, so the hit-location and technology-failure draws the original makes
  before its line of fire (RA4) are made in the struck one's attack: the same draws in the same order (every attack rolls its
  location unless aimed), now belonging to the struck one; an aim is kept, as the original keeps the location.
- Within one tile the host gives walls and portals before the other objects; the original takes the tile's one list in its order
  (it matters only for which of two blockers on the same tile is reported).
- The host reads a wall's pass-wall bit from its map data; the session does not track the pass-wall spell, so a wall it marks in
  play keeps its T0 kind.
- The projectile of a weapon with a projectile art is not drawn and does not fly: the hit resolves on the action frame.
- Only the normal (isometric) view's geometry; the top-down view option is not read.

### Proposed split of true ranged combat (Not ready)

Not part of ARC-COMBAT-012 (agreed scope); proposed tasks (the codes were free on 2026-10-21 and are confirmed when a task is
taken), each to be checked against the requirements and read in the executable when taken:

| Task | Scope |
| --- | --- |
| ARC-COMBAT-015 Ranged hero attack | Bow, throwing and firearms skills in `QueryHeroAttack` and `AttackCritter`/`AttackObject`: the range limit and penalty and the line-of-fire difficulty (RA2) in the attack check, the ammunition spend of RA3 in play, the attack goal's clear-shot test (0x425590, 0x429960: cost below 26 and no other blocker, AW8) before shooting. |
| ARC-COMBAT-016 Projectiles in flight | The projectile object of 0x4B39B0 (field 127): its flight, its timing of the hit on arrival, its drawing in the viewer, and what a miss does with it. |
| ARC-COMBAT-017 Thrown weapons | The thrown item leaving the hand (the throwable and default-throws flags), its landing cell through the drop placement, picking it up again, and the auto-switch re-wield. |
| ARC-COMBAT-018 Second projectile and scatter | A second projectile and the scatter of a missed shot, as the original's ranged path has them (not read yet), and their ammunition. |
| ARC-COMBAT-019 Returning weapons | Weapons that come back to the thrower (boomerangs, the throwing skill's 0x40 flag) and their catch. |

## Viewer (AREA)

- In combat mode (R) the cursor over a living NPC is the closed hand (`attack` / `walk to attack`), over a corpse nothing
  unless Alt is held (`loot`). The status line shows the last attack: outcome, damage, the target's hit points, a knockout or
  kill and the experience it paid.
- Combat targets are the NPC holders of the map's inventory seed; their combat values are read once per NPC. A map without an
  inventory seed (a whole map without a session: since ARC-WORLD-057 only one whose start clock cannot be read) has no
  targets.
- The hero is drawn with its attack ART while it attacks in place (`DiagnosticGridActor.AttackPose`); the human diagnostic profile
  `HumanFixedArea` now loads idle, walk, run, vault and attack (195 keys, 1,705,144 checked RGBA bytes; actor key cap 195).
- A killed or unconscious NPC is drawn in its fallen pose (`AreaFallenPoses`): the pose's ART is read on demand from the archive
  the body's art came from (`AreaLoadResult.ArtSources`), after checking that its entry is the one the published snapshot read
  (ordinal, flags, sizes, offset, name digest); the frame joins the map's frame repository (`AreaFrameOverlay`) and the body is
  drawn, lit and picked as a moved body (`AreaScenePlan.Repose`). A pose that cannot be read leaves the standing body and is
  logged. Reading a pose opens that archive's index on the main thread (a few milliseconds on the installed archives).
- The status line names a critical's injuries and drops; the log line `ARC-COMBAT-005 attack-hit` adds the table's effects and
  each critical item step (`crit=`). The viewer plays no stun animation, so a stun of the hero or the NPC ends at once
  (`ClearStun`, the contract for an animation that could not start; `ARC-COMBAT-009 stun-ended`); a critical knockout is drawn in
  the fallen pose like any knockout. `TargetCell` is the NPC's map-data cell (NPCs do not move in the viewer); the drop's map is
  `AreaDropMap` over the shown map (ARC-COMBAT-010), and each hit first places the session's actor on the walking hero's committed
  cell (`PlaceActor`, as F7 does), so the hero's drops start from where it stands. The critical stage also logs
  `ARC-COMBAT-010 native-smoke drop-paths PASS`: the drop's path from the NPC's tile to the tile two steps away for each direction
  draw 0..8 (result/partial steps), and it checks that each item the NPC dropped lies on the ground at the reported cell. A dropped item is hidden from the NPC but not drawn on the ground (as a
  spilled container's items).
- Native smoke `ARC-COMBAT-009 native-smoke critical PASS` (map mode, last stage): a granted fate point arms the hero's critical
  hit, the hero attacks the living NPC with the most hit points through the session as the viewer does (worn items, cell), and
  the stage checks the critical hit and that the tables drew; in the Crash Site cave a rat (body type 1) took a doubled torso hit.
- Native smoke: `ARC-COMBAT-005 native-smoke attack PASS` (with `--hero`, after the combat-mode stage, on the exterior and in map
  mode): the living NPC with the fewest hit points is attacked from a few cells away until it dies; the stage checks the attack
  ART, the rolls, the death, the experience, the corpse being lootable, the fallen pose, the status line and that a corpse click
  without Alt does nothing. A map without a living NPC with combat values prints `NO-TARGET`.

## Not ready

- AI: no NPC fights back, flees, joins combat mode or changes its reaction when attacked; the attacked NPC's AI pass, combat
  focus and taunts; the exit gate's threats are still empty.
- Ranged and thrown attacks (the [proposed split](#proposed-split-of-true-ranged-combat-not-ready); a melee-skill weapon with reach,
  the ammunition per shot and the line of fire are in since ARC-COMBAT-012), the "default throws" weapon, backstab (needs facing and
  perception), aimed-attack critical bonuses beyond the resolver's, the target's awareness (an NPC always sees the hero) and the
  darkness penalty (every position is lit), the critical effects listed above as not ready, the taking-damage
  and dying scripts, blood splotches, the corpse's decay timer, alignment and the kill log.
- The attack ART of a wielded weapon (the diagnostic human is drawn bare-handed; the viewer has no equip action, so the hero
  fights bare-handed), the low attack, the hero's own knockdown and death animations, the dying animation itself (the body jumps
  to its last frame), the get-up after a knockout (the viewer runs no event pump, so an unconscious NPC stays down), and HD frames
  of the fallen poses (Legacy per frame).
- A killed NPC still blocks its cell: step evidence is T0 map data.
- Companions (Virgil while he stands as a companion actor) are not attackable: their combat goal would need their live cell;
  the click says so.
- The pose ART is read from the archive the body's standing art came from, not through the whole content stack, so an override
  of the pose in another archive is not seen; the hero's facing returns to its movement facing after an attack; the cursor's
  attack shape is an OS cursor, not the original's.
- The turn-based round in the viewer (an attack there costs AP and passes turns; the session supports the round but the viewer
  has no turn-based setting).
- NPC stored effects (race, items, spells) in its effective stats; skill and resistance effects of the hero.
- Since ARC-COMBAT-008 part 2 the viewer passes the hero's kit and the NPC's worn items with their durability values, so wear
  happens in play; it shows no item condition. Attacking objects is in the [object damage runtime](object-damage-runtime.md#viewer-arc-combat-008-part-2).
- Original-runtime parity: NOT_VERIFIED (static reading only).

## Tests

`tests/Arcanum.Simulation.Tests/Combat/CombatSkillRulesTests.cs` (cap table, levels, monstrous melee, training tiers,
resistances, conditions), `tests/Arcanum.Session.Tests/AttackStateTests.cs` (NPC values from fields, failures, the hero's attack
until the kill with the experience, loot of a session kill, refusals before any roll, the aimed attack, the ranged refusal,
experience-free sessions), `tests/Arcanum.Simulation.Tests/Combat/LineOfFireRulesTests.cs` and
`tests/Arcanum.TerrainViewer.Tests/AreaLineOfFireTests.cs` (ARC-COMBAT-012: the geometry, samples, cover, walls, portals, the
corner switch), `tests/Arcanum.Session.Tests/CriticalEffectsStateTests.cs` (ARC-COMBAT-009 and the ammunition per shot of ARC-COMBAT-012: a critical hit disarms
an NPC and the weapon lies at its cell after a restore, items without a cell stay, the hero's dropped weapon lies at its cell until it is picked up (ARC-WORLD-059), a crippled
arm drops a torch it can no longer hold), `tests/Arcanum.Session.Tests/AttackWearStateTests.cs` (armour wear to the second break, the critical
weapon effects and invulnerability, the hero's weapon to its break, bare hands never killing, an NPC attacker, object targets,
missing item data, a refused step, refusals, and the wear of the hero's attack on a stone golem), `tests/Arcanum.TerrainViewer.Tests/AreaCombatTests.cs` (facing, the attack player, the status text,
`Repose`, the frame overlay, the dying animation and the pose read from a synthetic archive with its refusals) and the native
smoke above; synthetic only in git.

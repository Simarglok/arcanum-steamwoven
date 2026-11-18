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
([FR-COMBAT-009](../requirements/combat-magic-tech.md#fr-combat-009), [below](#critical-effects-arc-combat-009)). Since
ARC-COMBAT-021 the hero falls when it goes unconscious, lies while it stays so and gets up when it comes round
([FR-COMBAT-010](../requirements/combat-magic-tech.md#fr-combat-010), [below](#hero-knockdown-arc-combat-021)).

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
| Click in combat mode (FA1–FA6) | Nothing while the hero cannot act; a living NPC or PC: attack, Shift (left or right) the attempt-attack goal 19 with run flag 0x100 (ARC-DEV-270: the hero swings from where it stands, walks no approach and does not run; out of reach the attack ends); a follower of the hero, a companion that moves with its own actor included (ARC-COMBAT-1945), only with left Alt held; a corpse only with Alt (left or right), which loots it, else nothing; held `,` `.` `/` aim at the head, an arm, a leg. Out of combat mode the click talks or loots as before. |
| Skill level (`CombatSkillRules`, FA7) | 4 per stored point (bits 0..5), or 20 for the melee of a monstrous critter, then the skill's effect changes, clamped 0..20, then capped by the dexterity table `3,3,3,3,3,7,7,7,11,11,11,15,15,15,19,19,19,20,20,20` (dexterity clamped to 1..20); all five combat skills (bow, dodge, melee, throwing, backstab) key on dexterity. |
| Training (FA8) | Stored bits 6..7; a monstrous critter's melee takes the highest tier whose melee level and character level reach 1/10, 9/20, 18/30 in order. |
| Monstrous (FA9) | Mechanical, monster, animal or undead critter flag. |
| Resistance (`CombatantRules.Resistances`) | Stored + worn armour (with its magic, scaled for the wearer) + `5 × (CN − 4)` for poison when positive, clamped 0..95 unless a monstrous NPC (the resistance getter 0x43D6D0, ledger EQ8); resistance effects are not applied. |
| Reach | The weapon's range, 1 bare-handed, and since ARC-COMBAT-015 a clear line of fire to the target for every weapon (the goal's reach test, ledger RH1, [below](#ranged-hero-attack-arc-combat-015): a target farther away without a clear shot is approached; a target next to the hero across a wall edge or cover worth 26 or more has no approach, so the goal ends at once, as the original's empty plan does, AP4); farther away the click route walks (running, as every object approach) (since ARC-WORLD-073 the original's approach: the cell one step short of the target on the shared request's route into its tile, [click route](click-route-runtime.md#attack-approach-arc-world-073)) first, the attack goal's move-near step. |
| Animation | The hero's attack ART (animation 20) with the weapon its body shows in combat mode (since ARC-RENDER-037, [below](#weapon-art-arc-render-037): the wielded weapon's art slot, bare hands slot 1: 9 frames, 15 fps, action frame 6; the dagger's 5 frames, action frame 3) played by [`RealTimeCombatRules`](real-time-combat-runtime.md#rules-realtimecombatrules) at the rate of the hero's Speed and that weapon slot and the pause of its weapon speed; one attack sound roll (1..4) per attack start; facing the target. |
| Hit | The update that shows the action frame runs the hit once when the target is still within reach (else the attack ends without one): `GameSessionOwner.AttackCritter`. |
| Repeat | Auto attack (the options window's setting, ARC-UI-013; on by default, as the original's) starts the attack again 5 ms after it ends while the hero is in combat mode, no round runs and the target is alive. A click elsewhere, the stick, a dialog or window, a quick load, the hero leaving the map or combat mode (refused while an animation plays: the exit gate's attempt-attack test) or losing the ability to act stop it; a refused hit ends it; the same target clicked again keeps its goal. |
| Dying pose (FA10) | A death-flag hit or a death of at most 20 hit points falls (animation 7); otherwise a head hit decapitates (17), a leg hit severs the leg (19), anything else blows out a chunk (18); a knockout falls (7). The body plays that animation in its facing (group 0 for a single-rotation pose ART) and keeps its last frame (since ARC-RENDER-040, [below](#dying-animation-arc-render-040)); a bloody death without its file falls (the original's missing-art repair). |

## Session API (`GameSessionOwner`)

| Entry | Contract |
| --- | --- |
| `SessionCritterCombat.From(entity, fields, carried)` | One NPC's combat values from its ARC-DATA-021 fields and the fields of the items it carries (those in a wear slot count): life facts (pools, level, effective ST under each poison penalty, CN, WP, HealRate, PoisonRecovery, normal resistance, traits from the critter, object and spell flags), the loaded life state (hit-point and fatigue damage, poison, stun and injury flags), the experience worth and pool, and `Combatant(status, state, profile)` with its effective stats under its poison and crippled legs, armour class, resistances, dodge and backstab, light and conditions. Effective stats are the character owner's for the NPC's race and gender over its stored base stats, each clamped to the race's range first (monsters store values beyond it); its stored effects are not applied. Typed `SessionCritterCombatFailure`, never an exception for data. |
| `QueryHeroAttack()` | The hero's `Combatant`, its weapon (bare hands without one), the weapon's skill with its level and training (`QueryHeroSkills`, ARC-SKILLS-002; dodge and backstab too), weapon speed with that training, Speed, range and whether the attack is supported (melee; since ARC-COMBAT-015 bow and firearms too, [below](#ranged-hero-attack-arc-combat-015)). Null without a hero. |
| `AttackCritter(expected, attack)` | Refused before any roll: no hero (`AttackUnavailable`), an invalid target, an aim at the torso or an undefined location, a distance outside 0..reach (a dead target critter refuses every attack but a throw, ARC-COMBAT-2392), for a bow or firearm 0..100,000, or a line-of-fire cost outside 0..100,000 (`InvalidAttack`), a throw that does not fly or hold (`InvalidAttack`, ARC-COMBAT-017), a throwing-skill weapon (a boomerang, `AttackUnsupported`), the version, an exhausted revision (`RevisionExhausted`). Then the resolver on a trial copy of the session generator (`RandomExhausted` when it runs out), adopted once it ran; a resolver refusal (the hero cannot act, the target is dead) is NoChange with the resolution. A fate the resolver used (the hero's armed critical hit, ARC-WORLD-054, [fate points](fate-points-runtime.md)) is cleared in a revision of its own, so such an attack is Changed even without damage. Then `ApplyDamage` on the target or, on a critical self-hit, the hero; then, with experience data, `RecordNpcDamage` for an NPC that is not stoned and `RecordNpcKill` on a death; last the attack's wear (`ApplyAttackWear` with the hero as attacker and the NPC as target, `SessionAttackResult.Wear`). Each step commits its own revision; a later refusal is `StepFailure` and leaves the earlier steps. `SessionAttack.HeroWorn` and `TargetWorn` carry the items in the two sides' wear slots with their durability baselines (the hero's exactly its worn items, else `InvalidWear` before any roll); null skips that side's items (the bare-handed hero's own wear still applies). |
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
| Ammunition | ARC-COMBAT-013 (CR15, AM4): unless the weapon was destroyed (CW3), `DestroyAmmo` (0x40000) spends every unit of the hero's stack of its wielded weapon's ammunition type and `LoseAmmo` (0x20000) five times the weapon's consumption, all or nothing, through `SpendAmmo` (a stack spent to 0 is deleted): `AmmunitionSpent` with the stack and `Units`, or `NoAmmunition` when the hero holds none of the type, too few, or the count is 0 (a consumption of 0); a weapon without an ammunition type never rolls them, and a type outside 0..3 names no stack (AMA3). The weapon is the attack's, taken before a step of the same attack broke or dropped it. ARC-DEV-283: an NPC led by a PC spends the same way from its own stack (holder = the NPC itself); an NPC without a PC leader loses nothing (`NoAmmunition`, CR15, RA3). A refused spend (a budget, the revision) is the attack's `StepFailure`. |
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

**Not ready**: the NPC search budget per real second of the drop's path (DCA2). Also not modelled: the NPC's look-for-weapon/armour flags after a drop, the PC's injury record of a spell, burning, trap or script hit (0x4F5690; the attack steps write it since ARC-UI-097, ledger LB22), the worn armour's
critical scripts (points 34, 35), the drop script and an NPC's ammunition (the stun's animation and duration are done: ARC-COMBAT-1741). Since
ARC-WORLD-106 an NPC's armour class, resistances and light come from the items the session's NPC wears
([NPC wear in combat](equipment-runtime.md#npc-wear-in-combat-arc-world-106)), so a dropped, destroyed or broken piece no longer
counts.

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
from the hero's tile to the target's; a blocker is struck instead (`ARC-COMBAT-012 line-of-fire strikes …`); since ARC-COMBAT-015
a wall takes the shot ([below](#ranged-hero-attack-arc-combat-015)), and a companion or an NPC without combat values as blocker
ends the goal without a hit (not ready). Native smoke
`ARC-COMBAT-012 native-smoke line-of-fire PASS` (map mode): the hero stands next to an NPC or attackable object that
stops the line to another NPC, attacks the farther NPC with a lent reach (no kit holds a reach weapon) and the hit goes to the
nearer one (since ARC-COMBAT-015 the stage starts the animation itself: the goal's reach test admits only a clear line); on `arcanum1-024-fixed` a scenery five cells short of the target took it.

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
taken), each to be checked against the requirements and read in the executable when taken. ARC-COMBAT-015 is done
([below](#ranged-hero-attack-arc-combat-015)); ledger RH1–RH7 already read parts of the later ones:

| Task | Scope |
| --- | --- |
| ARC-COMBAT-016 Projectiles in flight | Done ([below](#projectiles-in-flight-arc-combat-016)): the projectile of 0x4B39B0 (field 127, −1 none), its flight, the hit on arrival (0x4B2870), its drawing in the viewer and a miss's stray strike (ledger PF1–PF14). |
| ARC-COMBAT-017 Thrown weapons | Done ([below](#thrown-weapons-arc-combat-017)): the "default throws" weapon (the throwing dagger 6072) leaves the hand at the attack, is checked with the throwing skill and its thrown range, flies hidden and lands where its projectile ends (ledger TW1–TW7); the original wields nothing again afterwards (TW3). |
| ARC-COMBAT-018 Second projectile and scatter | Done ([below](#second-projectile-and-scatter-arc-combat-018)): a bow shot from expert training flies as two projectiles for one arrow, and a missed throw scatters from its target location (ledger PS1–PS6). |
| ARC-COMBAT-019 Returning weapons | Done ([below](#boomerangs-arc-combat-019)): a boomerang stays in the hand, is checked with the thrown range of its own weight, keeps the hero from attacking while it is away, turns back at its first end, flies back striking nothing and is back where the hero stood at the turn; then the hero attacks again with auto attack (ledger BR1–BR7). |
| ARC-COMBAT-023 Inventory throw | Done in the session ([below](#inventory-throw-arc-combat-023)): any held item thrown at a location as the inventory throws it, with the damage of its weight, its thrown range, the line of fire's struck critter, the scatter and the landing (ledger TT1–TT5); the viewer's cursor and press: ARC-UI-063, done ([terrain viewer](../rendering/terrain-viewer.md#inventory-throw-from-the-game-cursor-arc-ui-063), TT6, TT7, CU7); since ARC-DEV-182 the press is the hero's throw goal 37 of the [goal table](../architecture/goal-table.md). |

## Ranged hero attack (ARC-COMBAT-015)

Requirements: [FR-COMBAT-007](../requirements/combat-magic-tech.md#fr-combat-007) (the ranged action's range, blocker and ammunition,
partly), [FR-COMBAT-004](../requirements/combat-magic-tech.md#fr-combat-004) (the attack goal's reach) and
[FR-COMBAT-005](../requirements/combat-magic-tech.md#fr-combat-005) (the distance terms of the attack check). Read in the retail
1.0.7.4 executable by two readers ([ledger RH1–RH7](../research/ledger/combat.md#ranged-hero-attack-arc-combat-015)); the check's
distance terms are SC8 and RA2, the shot's ammunition RA3.

| Rule | Value |
| --- | --- |
| Supported weapons | Melee, bow and firearms (`SessionHeroAttack.Supported`); a throwing-skill weapon (a boomerang) is `AttackUnsupported` until ARC-COMBAT-019 (its return is not modelled); a "default throws" weapon is thrown since ARC-COMBAT-017 (`SessionAttack.Throw`). `RangedSkill` (bow, throwing, firearms: the check's distance terms) and `RangedPath` (the throwing skill or a range above 1: the line of fire may strike another object, RA1) tell the host which applies. |
| Distance terms (SC8, RA2) | A bow or firearm: impossible (+1,000,000) beyond the weapon's range; 5 per tile beyond half the Perception below master; impossible when `SessionAttack.LineOfFire` is blocked, else its cost as cover. `SessionAttack.Distance` is the tile distance (0x4B96F0) and may lie beyond the range (refused only above 100,000): the original's goal shoots within range, but the check, not the session, refuses a target that moved since; such a shot misses and its ammunition is spent. A melee weapon must be within its reach (`InvalidAttack`) and ignores the line. |
| Line of fire input | `SessionLineOfFire(Cost, Blocked)`: the host traces the line from the hero's tile to the struck object's (`LineOfFireRules.Trace`, 0x4ADE00) and passes its cost (0..100,000, else `InvalidAttack`) and whether any object stops it; null is an open line without cover. After a redirect the check measures the distance and traces the line to the struck object (RH7, SC5), so the host traces again and passes the distance to it. |
| Ammunition (RA3) | A bow's or firearm's shot spends its per-shot count from the hero's stack of the weapon's type (`SessionAttackResult.Ammunition`), a hit or a miss; too few units refuse the shot after the hit location's roll. |
| Reach test (RH1, `LineOfFireRules.InReach`, 0x429960) | The attack goal starts the attack when the tile distance is at most the wielded weapon's range (at least 1) and the line from the attacker to the target costs below 26 with no blocker but the target, for **every** weapon (a melee one next to its target too); otherwise it approaches. The line (in the viewer also the redirect's and the check's) is `LineOfFireRules.ReachTrace`: for a target next to the attacker it is read as the Original setting reads it in both rule modes (owner decision 2026-10-28), so the Steamwoven fix of AW13/AW14 does not let an object on a side tile of a corner step into the target (a companion, an NPC, scenery) refuse or redirect an attack the original allows; longer lines keep the fix. The screen-left diagonal (x + 1, y − 1) is walked over the side tile (x + 1, y) as two edge steps in both settings, so an object there still stops it, as in the original. |
| Approach (EX9) | `CombatAnomalyProfile.RangedApproachSkipsClearShot` (Original): the walk goes on to the approach's tile next to the target, as the original's callback (0x425590) does while the attack is possible, and the reach is tested there. Steamwoven stops the walk at the first cell the hero commits to from which the reach test passes (the started step ends at its cell centre). Melee walks to the approach's tile in both settings. |
| Wall (RH5) | A wall the line of fire struck takes the shot: `SessionObjectAttack.Wall` resolves it without a skill roll (the damage is rolled and prevented, `DamagePrevention.Wall`), spends the shot's ammunition and changes nothing else; no wear. Since ARC-COMBAT-1973 the weapon's hit scripts (7, 34) with the wall as the extra object and the wall's taking-damage script (23) are offered as for any struck object (a thrown item's own hit script ends its flight); the hit sound is not played. |
| Follower (RH6) | The original attacks a follower on the line like any critter. Since ARC-COMBAT-2568 a companion that stops the line of fire (`StrikeBlocker`) or that a flying shot meets (`Strike`) is struck like any critter: the combat target by its identity (`AreaTargetOf`, the companion's live cell), the shot's own rolls; a companion with no combat values still ends the goal without a hit. The smoke check `RequireCompanionOnLineOfFire` in the companion smoke (stage `transfer`, or `follower-home`) hands a far NPC's attack to the strike of the line's blocker with the companion as the blocker. |

Viewer (AREA; since ARC-DEV-181 the hero's attack is goals 18 and 19 of the [goal table](../architecture/goal-table.md), the viewer holding only the attack's data, and the hit, the line of fire and the animation below are the ports the goals call): the goal tests its reach with `InAttackReach` (the line from the hero's committed cell, the shown map's line-of-fire
view), walks when it fails (`walking to attack: no clear shot` when within range), and under Steamwoven stops the walk at a clear
shot (`ARC-COMBAT-015 clear-shot stop`). The hit passes the line to the struck NPC as `LineOfFire`; a wall blocker is struck
(`ARC-COMBAT-015 line-of-fire strikes wall=…`, `wall-hit`). The hero is drawn with the attack ART of its wielded weapon at that weapon slot's
rate (since [ARC-RENDER-037](#weapon-art-arc-render-037)). Native smoke `ARC-COMBAT-015 native-smoke ranged PASS` (map mode after the
line-of-fire stage, or alone with `ARC_AREA_MAP_SMOKE_ONLY=ranged`): no kit holds a bow, so the stage lends the wielded kit weapon a
bow's values (range 10, no ammunition) and restores it; from a cell 3..6 tiles from an NPC with a clear shot the attack starts
without a walk and the check carries the range penalty and the line's cost as cover; from a cell 12..16 tiles away under
Steamwoven the walk stops at a clear shot (`clear-shot stop`) and the hit runs from 2..10 tiles. The stage `ranged` also runs the
object-attack, critical and line-of-fire stages that follow the cast stage in the map battery. A wall struck on the line has no
native observation: the goal's reach test admits only a clear line, so the viewer meets a wall blocker only when the line changed
after it; the session tests cover the wall's shot.

Not ready: boomerangs (RH3, RH4,
ARC-COMBAT-019), the out-of-ammunition refusal's sound, flag and auto-switch (RA3), the firearms' and
bows' repeat shot (RT rows), and the NPCs' ranged attacks (no AI).

## Projectiles in flight (ARC-COMBAT-016)

Requirements: [FR-COMBAT-007](../requirements/combat-magic-tech.md#fr-combat-007) (the projectile of a ranged shot, partly) and
[FR-COMBAT-006](../requirements/combat-magic-tech.md#fr-combat-006) (the hit's timing, partly). Read in the retail 1.0.7.4
executable by two readers ([ledger PF1–PF14](../research/ledger/combat.md#projectiles-in-flight-arc-combat-016)); the weapon's
projectile art is field 127 (`CombatWeaponFields.MissileArt`, slot 74, [combat fields](../formats/combat-fields.md)).

| Rule | Value |
| --- | --- |
| Which shots fly (PF1, PF2) | A shot on the ranged path (`RangedPath`) whose weapon has a projectile art (not −1). Every installed bow, 5 of the 41 firearms, the throwing weapons and the throwing dagger have one, all scenery arts (combat census). Without one the hit runs on the action frame as before. |
| Decided at the shot (PF1) | `SessionAttack.Flight` / `SessionObjectAttack.Flight`: the attack runs as without it up to the outcome (refusals, the hit location, the ammunition, the check, the dodge, the fate, the concealment) and a critical miss turns on the hero at once; a hit or critical hit carries no damage yet (`AttackRequest.DeferHit`). The result names the shot in flight (`SessionAttackResult.Shot`), a hit or a miss; at most `MaxShotsInFlight` (16) fly. |
| Strike (PF5, PF6, PF8) | `LandShot` (an NPC) and `LandShotOnObject` (a portal, container, scenery, trap or wall): the shot's own target when it hit, with the carried location, aim and critical (`AttackResolver.LandHit` rolls only the damage, on the target as it is now; a target dead by then is refused); any other object as a forced hit without a check (`AttackResolver.ForcedHit`: torso, no critical). A miss never strikes its own target (walls excepted: they share one identity and take nothing). Every step after the resolution follows as for an attack at once; the shot's fate, concealment and ammunition are not spent again. The damage uses the shot's weapon and skill (field 0x50); the attacker's values and the wear of the hero's items are those of the strike (a weapon wielded since then takes the wear). |
| End (PF9) | `EndShot`: the shot flew beyond its range, off the map or its map left the screen; a carried hit is lost. |
| Saved (ARC-DEV-288) | A shot in flight is saved with its projectile, as the original saves its projectile objects with their fields 77–80 (SV5, SV6; the `flights` section, schema 80, [saved runs](../architecture/saved-runs.md#35-projectiles-missiles-and-spell-runs)): the weapon, skill and reach it was shot with, the decided outcome (the check's flags, not its rolls), the target, the thrown item and the boomerang's state; after a load the projectile flies on and strikes with that outcome. A shot no saved projectile carries is not written. |
| Flight (PF3, PF4) | `ProjectileRules.Launch` and `Step`: the pixel line from the hero's tile centre to the destination tile's (the goal target's tile at the shot, also after a line-of-fire redirect, PF2), in 10-pixel steps, replayed when they run out; four steps a 35 ms tick (`TickMs`, `StepsIn`; the weapon's eye-candy speed column is not read). A tile change is a `ProjectileCrossing` of the tile left; a point off the map ends the flight. |
| Crossing (PF5–PF7, PF9) | `ProjectileRules.Cross` (0x4B2870): a hit strikes its target where the target stands on the tile left; leaving the destination turns the shot into a free miss; a miss meets, flying free, the object stopping its next step, else each living critter on the tile left other than its target and the hero, struck when the session's `combat.StrayStrike` roll of 1..3 is 1; with nothing struck beyond the range from the start the flight ends. The obstruction is the line of fire's step cost (flags 0x0C): the original's flags 0x0D stop at a closed door or window before its cover test (PF7), which is not ready. |
| Facing (PF13, PF14) | `ProjectileRules.Direction` (32 screen directions, 0 up, clockwise) and `FacedArt` (a scenery art: rotation direction mod 8, number + direction / 8). |

Viewer (AREA): with the wielded kit weapon's projectile art (`AreaStartingHero.KitMissileArt`) the action frame's hit, the line of
fire's NPC or object blocker and the wall shot fly (`ARC-COMBAT-016 projectile-launch`); each frame the projectile takes the steps
its ticks owe (`ProcessAreaProjectiles`), the tiles it leaves are crossed over the shown map (the struck target where it stands, the
hero, the companions on their live cells and the map's living NPCs in row order, the line-of-fire view's step for an obstruction),
and a strike resolves through the session with the same sounds, status line, death pose and drawn bodies as a hit at once
(`projectile-end strike=… struck=…`). The projectile is drawn from its faced art at its tile, moved by its pixel offset along the
line, its frame advanced once per step; each projectile keeps the session that holds its shot, and a map change, a reload (F8 or the load window) or the smoke's end gives the flights up (a strike the session refuses ends its shot there too). A companion the shot meets on the
way is struck like any critter (RH6, ARC-COMBAT-2568). Native smoke `ARC-COMBAT-016 native-smoke projectile PASS` (in the ranged stage,
with the lent bow and scenery art 0 as its projectile art): from the first cell each shot flies, a hit deals nothing at the shot and
strikes its target on arrival, a miss never strikes its target, the arrow is drawn while it flies and the session keeps no shot
afterwards, until a hit has landed and a miss has flown (at most 60 shots; the miss is reported, not required; ARC-COMBAT-2573: the hit chance is a seeded roll, 25 % at the hero's bow level, so the number of shots a hit takes depends on how many draws the earlier stages of the chain made, and twelve misses in a row appeared after unrelated changes; a critical miss that takes the bow ends the leg with `NONE`).

Not ready: (the scale arc of a thrown non-weapon item, PF11, is the viewer's since ARC-UI-063),
boomerangs (ARC-COMBAT-019; thrown weapons since [ARC-COMBAT-017](#thrown-weapons-arc-combat-017)), the AI alert, taunt and attack end at the strike (no AI), the weapon's miss scripts and the item event
0x800000 (PF9, PF10), a closed portal stopping a free flight (PF7), the eye-candy speed column, the additive draw of weapon flag 0x20,
and a projectile across a save.

## Thrown weapons (ARC-COMBAT-017)

Requirements: [FR-COMBAT-007](../requirements/combat-magic-tech.md#fr-combat-007) (a thrown item is lost by its type: it leaves the
hand and lands, partly) and [FR-COMBAT-005](../requirements/combat-magic-tech.md#fr-combat-005) (the throw's check, partly). Read in
the retail 1.0.7.4 executable ([ledger TW1–TW7](../research/ledger/combat.md#thrown-weapons-arc-combat-017), with RH2, RH4 and
PF12). Of the installed weapons only the throwing dagger (6072) carries the "default throws" flag; the eight throwing-skill weapons
are boomerangs (ARC-COMBAT-019).

| Rule | Value |
| --- | --- |
| Throw request (TW1) | `SessionAttack.Throw` / `SessionObjectAttack.Throw` = `SessionThrow(Weight)`: the host says the wielded weapon carries weapon flag 0x200 and passes its effective weight (0x461370; the session keeps no item weights, ARC-WORLD-085 owns the inventory weight, and the viewer passes the stored weight without its magic adjustment). A throw must fly (`Flight`), its weight lie in 0..1,000,000, a melee-skill weapon be wielded (the only installed one, the dagger; a bow's ammunition or a boomerang's return would follow other rules) and the target lie within the weapon's reach, else `InvalidAttack`. A weapon the remove check refuses (no drop) would stay in the hand and attack as without the flag; the session never wields one. |
| Check (TW4, TW5) | The throwing skill's level and training replace the weapon's skill (a melee-skill dagger too); the weapon's range becomes the thrown range `ThrowRules.Range` (0x4622A0: `max(1, (50 × ST [+ half from expert throwing]) / max(1, W / 10) / 5)`), so the check is impossible beyond it and the range penalty applies as for any ranged skill. The reach test before the attack stays the weapon's range (RH1). The technology-failure and magick-cancel rolls are drawn and cannot succeed; the line of fire and its redirect are those of any ranged shot. No weapon wear (CW5: the throwing skill). |
| Leaving the hand (TW1, TW3) | Unless the attack is refused, the weapon leaves its slot and the hero's list in a revision of its own and lies hidden on the hero's cell (`QueryItemInFlight`: not drawn, `PickUpItem` refuses it as `ItemNotAvailable`) without a decay event (0x4415C0); the attack's later steps find the hand empty (a critical miss's weapon effects act on no weapon). `QueryThrownShot(shot)` names the item and its thrown range. Nothing wields another weapon (TW3). |
| Landing (TW6, PF9) | A thrown shot that struck (`LandShot`, `LandShotOnObject`) is not gone: it waits for `LandThrownItem(expected, shot, cell)`, which moves the item to the projectile's tile (the struck object's, or the last one beyond the thrown range or off the map), shows it again and ends the shot, one revision, with no decay event (0x4EDF20, ID9). The original lands it just before the hit (PF8); nothing of the hit reads it. `EndShot` gives the flight up (a map change, a reload): the item stays on the hero's cell. A snapshot taken during the flight keeps the shot and its projectile (ARC-DEV-288): the item stays hidden there until the restored flight lands it. |
| Throw script (TW3, TT1, TT8) | ARC-COMBAT-1955: with `SessionHeroThrow.Scripts` (the viewer's item script host) the removal's wield-off script (25, a worn item) and remove-item script (28, not for a split stack: the stack path runs none, QW5), then the item's throw script (point 6) run before anything of the throw is decided (`RunHeroThrowScripts`): the hero is the triggerer, the item the attachee, no other object (the press throws at a location, TT2), under `SessionItemScripts.Admission`, each run reading what the earlier ones did; the original removes the item at the lift (0x466E00 → 0x467860) and runs the throw script at the throw (0x4B3C00 → 0x441980), Steamwoven takes the item out at the throw so all three run there (the item-leaves-at-the-throw difference above). A run the session cannot apply refuses the throw before any effect (`ItemScriptUnsupported`); the runs' changes are one revision of their own and the throw goes on from the new version. A throw script that answers skip default throws nothing (`SessionHeroThrowResult.ScriptDeclined`): the changes of the runs stay, a worn item is worn no more and lies in the hero's list again (the original puts it on the hero's tile, 0x4415C0, and inserts it back, 0x4617F0), the removal's spell steps (`RemovalSpells`) are made and no roll is drawn. ARC-COMBAT-2183 (0x4617F0 → 0x461810 → 0x466640): the give-back's insertion runs the item's insert-item script (21) after the throw script, the hero as the triggerer and the item (the worn one too) as the attachee, its answer ignored, in the same runs and revision (a run the session cannot apply refuses the throw before any effect); a throw that goes on runs none. The give-back's own get script (point 16, the transfer's gate) and a split stack's insert-item script stay NOT READY (see ARC-COMBAT-2183's leftovers). The printed lines (`Messages`) and eye candy (`EyeCandies`) of the runs are the host's to show. ARC-COMBAT-2182 (TW3, QW5): the throw script of a split ammunition stack or gold pile runs for the stack the split would make: the session plans the made item's entity before the scripts (`_pendingThrowMade` makes it answer as the made item, so the script reads its prototype's array), runs the throw script with the hero as the triggerer and no removal script (the stack path runs none), and splits only afterwards; an answer of skip default throws nothing and the units go back to the stack they came from (the original's insertion merges them, 0x466640), the run's flag, store and message changes kept and the slots it wrote for the never-made item dropped. ARC-COMBAT-2413 (exe read 0x578EA0, 0x4658E0, 0x465A40, 0x4617F0): a throw of the whole ammunition stack is different, because the lift destroys the source stack (exactly the count) and makes the new object, which the insertion finds no other stack to merge into: the source and its own slots are gone, and the made stack, with the slots and calls the runs left on it, is appended to the hero's list and takes the first grid cell it fits (0x4664C0 → 0x4675A0), in both rule modes (`RemakeWholeAmmoStack`). ARC-COMBAT-2424 (exe read 0x464830, 0x464970, 0x4617F0, 0x466640): a whole gold pile that is declined is the same: the lift destroyed the pile and made a new gold object that merges into no pile, which the insertion appends to the list at the first cell it fits, so the pile takes the first free cell (`RefitWholeGoldPile`; a part of the pile merges back into what remains and changes no cell). Steamwoven difference, decided here: the pile keeps its one fixed entity (the coins are the session's counter, the owner's ARC-UI-090 model), where the original's list gold is the new object. Modelling a changing gold entity would give the counter a per-pile identity (script slots, scheduled calls, save schema, every gold path of the grid, drop and barter) and nothing reachable reads it: a pile carries no script slots in the counter model and no installed gold carries a throw script. The slots a declined throw's runs wrote for the never-made object are dropped (`ForgetNeverMadeThrowItem`). Refused as before, NOT READY: any of these scripts without a host, and a split stack's (ammunition, gold) throw script together with an insert-item script (the give-back would merge it back and run it). ARC-COMBAT-2176: with a struck critter the refusals of the attack on it (`StruckThrowPreflight`: the critter's record, both sides' worn items, the distance, the cell's light, the flight budget, the revision, the defender's combat values and the roll sequence, the resolver and a missed throw's scatter run on a copy) are checked before any script runs, so such a throw is refused with nothing applied. ARC-COMBAT-2392 (ledger TW8): a struck critter that is dead is thrown at like a living one (a hit or miss roll against the corpse, 50 easier, no dodge; a hit does no damage and still runs the hit scripts), before and after the scripts; this replaces ARC-COMBAT-2390's throw to the location for a dead critter (the viewer's pick never names a corpse, NOT READY, #2416). ARC-COMBAT-2390 (ledger TW8): a struck critter that refuses after the scripts ran (any other refusal of the attack that only their runs or a host callback could bring about: the hero's values they changed, the random sequence) is thrown at as a location by the session itself, so the scripts do not run twice and their changes stay; the location throw makes its own checks (worn items, an inactive hero, the budget, the version, the revision), which refuse what binds a throw of any kind. In the original no refusal follows the throw script (its attack check refuses only for the ammunition of a thrown weapon, 0x4B65D0) and the item flies to the critter with the normal hit or miss roll, so the throw at the location is an approximation for those Steamwoven-only refusals (NOT READY: the hit roll on the critter after such a refusal, the flight's landing forcing the hit on a critter in its way and, with a random sequence run out, refused again at the landing; a thrown weapon's ammunition refusal, which leaves the item switched off on the hero's tile, #2415). An item script can change the hero's effects only: another object, a random draw and a critter are not admitted, so the random sequence is reached by a host callback alone. No installed thrown weapon carries a throw script (TW7). |
| Ammunition of a thrown weapon (TW8) | ARC-COMBAT-2415: a pack weapon thrown with `ThrowHeroItem` that has an ammunition type is checked before any roll but the hit location (the dispatcher's ammunition check): the hero's stack of that type against the weapon's consumption. Enough units are spent at the throw (`SessionAttackResult.Ammunition` of the throw's attack, a hit or a miss alike) and the throw goes on; fewer refuse it (`SessionHeroThrowResult.AmmunitionShort`): the item leaves the list and lies on the hero's tile switched off (`QueryItemOff`), nothing flies, nothing is given back, nothing else is rolled. NOT READY: the weapon auto-switch after the refusal and message code 3 as a floater (the viewer shows a notice). |

Viewer (AREA): a wielded kit weapon with the flag (`AreaStartingHero.KitThrowWeight`, its stored weight) is thrown by the action
frame's hit, the line of fire's blocker and the wall shot (`ARC-COMBAT-016 projectile-launch … thrown=kit:N range=R`), and the attack goal ends there (the throw is the
original's throw goal, which nothing repeats, TW1); the projectile
flies to the thrown range, the ground does not draw the item while it flies, and at the strike or the flight's end the item lands
on the projectile's tile (`ARC-COMBAT-017 thrown-landing`) and is drawn there; a flight given up leaves it on the hero's cell. It is
picked up as any placed kit item and wielded again by hand. Native smoke `ARC-COMBAT-017 native-smoke throw PASS` (in the ranged
stage, after the projectile check): the lent bow becomes a throwing dagger (melee skill, weight 15) with the same projectile art;
each throw leaves the hand, lies hidden on the hero's cell while it flies, lands on the tile its projectile ended on, drawn and
without decay, and is picked up and wielded again; at most 12 throws until one hits (a hit is reported, not required: the pre-made
heroes barely train throwing, and the session's tests strike with one).

Not ready: a flagged weapon without a projectile art (the original flies the item's own art, PF2; every installed one has an art, so
the viewer throws only with one), the inventory's
throw of other items (their scale arc, PF11, is the viewer's since ARC-UI-063), the throw script (none installed), NPC throws (no AI), the struck object's alert; a missed throw's scatter is in since
[ARC-COMBAT-018](#second-projectile-and-scatter-arc-combat-018), boomerangs since [ARC-COMBAT-019](#boomerangs-arc-combat-019).

## Second projectile and scatter (ARC-COMBAT-018)

Requirements: [FR-COMBAT-007](../requirements/combat-magic-tech.md#fr-combat-007) (projectiles and throws resolved by weapon type,
partly). Read in the retail 1.0.7.4 executable by two readers ([ledger PS1–PS6](../research/ledger/combat.md#second-projectile-and-scatter-arc-combat-018),
refining PF2, PF5 and RH2). No rule-mode switch; no snapshot change (the shots are saved since ARC-DEV-288, schema 80).

| Rule | Value |
| --- | --- |
| Projectiles of a shot (PS1, PS2) | `ProjectileRules.Projectiles(skill, training)` (0x4B39B0): 2 when the attack's skill is the bow and the hero's bow training is expert or master, else 1 (firearms, a throw, a lower bow). A shot that flies (`Flight`) is launched as that many session shots (`SessionAttackResult.Shots`, `SessionObjectAttackResult.Shots`, launch order; `Shot` stays the first): each carries the same decided check (hit, location, critical, weapon) and strikes on its own through `LandShot` or `LandShotOnObject`, which rolls its own damage, wear, experience and criticals. The ammunition goes once, at the shot. Both fly the same line (the original passes offsets 0 and 10 that nothing reads, PS2; no fix is recorded, so both rule modes draw them on one line). |
| Second arrow on a corpse | A hit's first arrow may kill the target; the second still strikes its tile (the original tests no death on the way, PF5), and the session answers NoChange with `AttackRefusal.TargetDead`: a corpse takes no damage (the damage step leaves for a dead critter, CW8), and that shot is gone. |
| Shot limit | `MaxShotsInFlight` (16) must hold every projectile of the shot: an expert bow shot needs two free places, else `BudgetExceeded` before any roll. |
| Scatter (PS3, PS4) | A throw (`SessionThrow`) that does not hit (a miss, dodge or critical miss) draws a rotation 0..7 from the session's sequence (`CombatRollPurpose.ThrowScatter`, right after the resolver's draws, so a critical miss's later drop draws follow it where the original draws them first; the generator is the session's, not the original's) and walks `ThrowRules.ScatterTiles(level, distance)` = `min((20 − throwing level) / 5 + 1, distance)` tiles (toward zero; 1..5 for levels 20..0) from the throw's target location in it (`ThrowRules.Scatter`, 0x4B90D0): the step is `SessionAttack.DropMap.Step` (the shown map; a step off it stops the walk), or without a map any cell in 0 .. 2^24 − 1. The distance is the hero's cell to the target location. `SessionAttackResult.Scatter` names the tile; the shot keeps no target, so a stray strike may hit the critter it missed. |
| Target location | `SessionThrow.Target`: the throw goal's location (the clicked target's tile, also after a line-of-fire redirect, TW1, PF2); without it the attack's `TargetCell`. A throw at a critter with neither, or with one outside 0 .. 2^24 − 1, is refused (`InvalidAttack`) before any roll. A throw at an object or a wall never misses (RH5), so it never scatters. |
| Flight without a target (PS5) | `ProjectileRules.Cross` with no target: the shot flies free from the start (obstructions and the 1-in-3 stray strike), and leaving its destination ends it (`ProjectileStrikeKind.DestinationEnd`) before that tile's tests and before its range end, so the thrown item lands on the scattered tile unless it struck on the way or flew beyond its thrown range first. |

Viewer (AREA): every shot of `Shots` is launched as its own projectile (`ARC-COMBAT-016 projectile-launch`, the same line and
destination), each ending on its own; a missed throw is launched to its scattered tile without a target, free from the start
(`… scattered`), and its end at the destination lands the dagger there (`projectile-end … strike=DestinationEnd`, notice "the throw
missed and fell aside"). A scatter back onto the hero's own cell has no line to fly: the dagger lands there at once. The throw passes
the goal target's tile as `SessionThrow.Target`. Native smoke in the ranged stage: `ARC-COMBAT-018 native-smoke scatter PASS` (every
missed throw of the dagger check scatters and lands on its scattered tile unless it strikes first) and `ARC-COMBAT-018 native-smoke
expert-bow` (the hero's bow trained to expert, its level raised with unspent points in the character editor when it must, then
restored: each shot flies as two projectiles on one line, both strike a hit's target and neither strikes a miss's); the pre-made
heroes have a bow level of 4 and no unspent points, below expert's 9, so that check reports NONE and the two projectiles rest on
the session's tests.

Since ARC-DEV-140 the end of a scattered throw fires the thrown item's attacks-a-location event on the landing tile (PS5, PF10), so a missed grenade
explodes where it falls; since ARC-COMBAT-1223 its item script runs at point 7 (the destination's hit point, ledger PS5), not 8 (PF9's range end). A scatter
back onto the thrower's own tile is the one miss that fires no event: the projectile already stands on its destination, the flight goal pops at its node 3 (0x424D90, GT53)
and ends without a context, and the item lies on that tile unexploded (Steamwoven lands it there at once, as the original). Not ready: the critical weapon's script 34 at that end.

## Boomerangs (ARC-COMBAT-019)

Requirements: [FR-COMBAT-007](../requirements/combat-magic-tech.md#fr-combat-007) (projectiles and throws resolved by weapon type,
partly). Read in the retail 1.0.7.4 executable by this task ([ledger BR1–BR7](../research/ledger/combat.md#boomerangs-arc-combat-019),
refining RH3, RT7, PF9, PF12 and PS5). No rule-mode switch; no snapshot change (shots and the mark are not saved).

| Rule | Value |
| --- | --- |
| Weapon | A wielded throwing-skill weapon (weapon flag 0x40, 0x465D80; the installed boomerangs 6184–6191, TW7) is now `SessionHeroAttack.Supported`. It stays in the hand: nothing leaves it, nothing lands, and it never scatters (PS3). |
| Check range (BR7) | The reach before the attack is the weapon's range (RH1, `SessionHeroAttack.Range`); the check measures the thrown range `ThrowRules.Range(strength, throwing training, weight)` (RH4), the weight 0x461370 of the wielded item from the session's weight row (ARC-WORLD-085, `CarryRules.ItemWeight` with the hero as holder; none weighs 0). The outward flight's range end is the weapon's range (PF9). |
| Away (BR1, BR2) | A boomerang's shot in flight marks the hero (`QueryBoomerangInFlight`, the original's field 219 bit 0x4): `AttackCritter` and `AttackObject` refuse any new attack with `GameSessionFailure.WeaponInFlight` before any roll, and the viewer's real-time repeat (RT7's `UsingBoomerang`) does not repeat. `QueryShotReturns(shot)`: the shot will still turn back. |
| Turn (BR3, BR4) | At its first end the shot turns back instead of ending: a strike (`LandShot`, `LandShotOnObject`) turns it itself; at the end of its range or the destination of a shot without a target the host calls `ReturnShot(shot)`. `ProjectileRules.TurnBack(flight, shot, thrower)` gives the new line from the tile being left to the hero's tile now, the shot `Returning` (flag 0x2000) with its outcome, range and facing kept; `ProjectileRules.TurnEndAt` (ARC-COMBAT-1971, ledger PF9) decides the hero's own tile: on the tile left the boomerang ends at once with the second end's hit script and item event, on the tile entered it ends bare, otherwise it flies back. |
| Return (BR5) | `ProjectileRules.Cross` with a returning shot strikes nothing, meets no obstruction and has no range end; leaving its destination ends it (`DestinationEnd`). A returning shot refuses a strike (`InvalidAttack`). |
| Back (BR3, BR6) | `EndShot` is the second end: the shot is gone and so is the mark; a flight given up (a map change, a reload) is back in the hand too, as the original's flight goal end passes no turn. |

Viewer (AREA): a boomerang's projectile (`projectile-launch … boomerang range=8`) turns at its first end (`ARC-COMBAT-019
boomerang-turn shot=… from=… to=…`; the strike or range end is logged as `projectile-end … turned back` first) and flies back to
the hero's tile at the turn; its end there is `projectile-end … strike=DestinationEnd … back in the hero's hand` (notice "the
boomerang is back"). Then (0x4A9AD0, `boomerang-back … reattack=…`) the hero takes up combat mode if it left it and, in real time
with the auto-attack setting and no attack goal running, starts a new attack goal on what the shot was fired at (the clicked NPC or object, or the line of fire's blocker; not a critter struck on
the way), not aimed. A
click while the boomerang is away says "the boomerang is still in flight". Native smoke in the ranged stage: `ARC-COMBAT-019
native-smoke boomerang PASS` (the lent kit weapon with a boomerang's values and the bow's projectile art: it stays wielded, a second
attack is refused while it flies, it turns at its first end, flies back to the hero's tile and ends there, the mark clears and the
auto re-attack starts).

Not ready: the return's hit script and item event (BR5, as PS5), the turn-based round's join and the hook of BR6 and its goal-slot
tests, the re-attack after a flight given up, NPC boomerangs (no AI), the struck object's alert, and a boomerang across a save (the
session keeps neither the shot nor the mark: a restored hero has it in the hand).

## Inventory throw (ARC-COMBAT-023)

Requirements: [FR-COMBAT-007](../requirements/combat-magic-tech.md#fr-combat-007) (throws resolved by item type, partly) and
[FR-COMBAT-005](../requirements/combat-magic-tech.md#fr-combat-005) (the throw's check, partly). Read in the retail 1.0.7.4
executable ([ledger TT1–TT5](../research/ledger/combat.md#inventory-throw-arc-combat-023), with TW1–TW7 and WB8). Session and
rules only: the viewer's game cursor holding the item and its press in the game view are ARC-UI-063
([terrain viewer](../rendering/terrain-viewer.md#inventory-throw-from-the-game-cursor-arc-ui-063)).

| Rule | Value |
| --- | --- |
| Request (TT1, TT2) | `GameSessionOwner.ThrowHeroItem(expected, SessionHeroThrow(Item, Facts, Target))`: a held item, listed or worn, its kind, no-drop flag and script points (`SessionGridMoveItem`), the pressed tile; a listed weapon's `Combat` facts and weapon flag 0x10 (`Throwable`); the critter the line of fire to the tile names (`Struck`, `StruckCell`, `LineOfFire`, TW4 as corrected by TW10 and TW11, ARC-COMBAT-2425: the line's blocker, else the critter an object press names (TT6), else none; the critter on a pressed tile is never named) or none. |
| Leaving the hero (TT1, TW3) | The original takes the item out when it is lifted onto the game cursor (remove check 0x466DA0, removal 0x466E00); a PC's throw (0x4B3C00) leaves it there. The session takes it out at the throw: the no-drop flag refuses (`ItemNotDroppable`), the item leaves its list or slot in a revision of its own and lies hidden on the hero's cell without decay until `LandThrownItem` or `EndShot`, as a thrown weapon (TW6). A weapon the hero wields stays in the hand. |
| Check (TT3, TW4, TW5) | With a struck critter: `AttackCritter`'s flight with the throwing skill's level and training, the thrown range `ThrowRules.Range` of the item's weight (the session's weight row, the hero as holder; `QueryHeroThrowRange`), the struck critter's distance and the line's cover, no reach test, no ammunition of the wielded weapon and no boomerang test (the throw starter 0x434F80 has none). A miss scatters from the tile (RH2). Without one (ARC-COMBAT-2435, ledger TW11) the throw rolls as a throw at a location (`ThrowAtLocation`, `AttackRequest.NoDefender`: the hit and critical with only the distance, the line's cost, the tile's light (`SessionHeroThrow.TargetLight`, `LineOfFire`) and the hero's flags, no dodge; a miss scatters from the tile, a critical miss turns on the hero) and the shot flies to `Destination` (the tile, or the scattered tile); a random stream that cannot give the rolls flies it unrolled. |
| Damage (TT5, WB8) | `ThrowRules.ThrownWeapon`: a throwable weapon (flag 0x10) its own damage, any other weapon `ThrowRules.ItemDamage` of its weight with its magic damage adjustment, anything else a `WeaponKind.ThrownItem` with `ItemDamage`: normal `(W + 99) / 100 .. (W + 49) / 50`. No strength bonus (melee only). |
| Action points (TT4) | `TurnBasedRules.ThrowActionCost` / `QueryHeroThrowCost`: the throw goal charges 2 to every thrower but the local PC (0x433270), so the original PC throws for nothing in a round; Original switch `PcThrowCostsNothing`, Steamwoven charges the hero 2. The turn-based host charges it. |

Steamwoven differences: the item leaves at the throw, so a worn item's own values still count for the range and check, and a critical
miss's weapon and ammunition steps read the weapon the hero wields (CW3). A dead struck critter is thrown at like a living one (ARC-COMBAT-2392; the viewer's struck-critter choice never names a corpse, as the original's line of fire never does, ARC-COMBAT-2416, TW9; the original's click-through of corpses in combat mode is not reproduced): the host throws
again without one. A wielded boomerang away on its shot is refused (`WeaponInFlight`). Since ARC-UI-063 the viewer charges
`ThrowActionCost` for both throws in a round, the inventory's and a "default throws" weapon's.

ARC-UI-086: an ammunition stack throws the units its lift asked for (`SessionHeroThrow.Count`, `SplitKind`): they are split off into a new
made stack as the item leaves (the stack path of QW5, no remove check, no remove-item event, so the no-drop flag and that script do not
refuse it), and that stack is the thrown item with the weight, range and damage of its units. Not ready: gold (the hero's coins are a counter,
`AttackUnsupported`), a split stack's throw script with an insert-item script (refused, `ItemScriptUnsupported`; the item's own throw script with one runs it at the give-back since ARC-COMBAT-2183; the item's own throw and removal scripts run since ARC-COMBAT-1955, a split stack's throw script since ARC-COMBAT-2182), the target-less check (TT3), a non-critter blocker, the struck
object's alert and the landing's item event (PF10), the throw arc of an item's flight (PF11), and the hotkey bar's held item (ARC-UI-087: read in TT1; the viewer
puts it on the game cursor and the session throws it as any listed item). The original's lost item when the throw start fails after the cursor was
cleared (TT2) does not arise: the session refuses before the item leaves.

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
  each critical item step (`crit=`). Since ARC-COMBAT-1741 the viewer plays stun animation 23 for the hero and the NPC (goal 57 over the stunned ART, the hero's body and the NPC's art); only a body with no such ART ends the stun at once
  (`ClearStun`, the contract for an animation that could not start; `ARC-COMBAT-009 stun-ended`); a blow on the stunned hero does not cut its stun short (ARC-COMBAT-2172: the viewer's hero-down route stop leaves the stun goal's run alone); a critical knockout is drawn in
  the fallen pose like any knockout. `TargetCell` is the NPC's map-data cell (NPCs do not move in the viewer); the drop's map is
  `AreaDropMap` over the shown map (ARC-COMBAT-010), and each hit first places the session's actor on the walking hero's committed
  cell (`PlaceActor`, as F7 does), so the hero's drops start from where it stands. The critical stage also logs
  `ARC-COMBAT-010 native-smoke drop-paths PASS`: the drop's path from the NPC's tile to the tile two steps away for each direction
  draw 0..8 (result/partial steps), and it checks that each item the NPC dropped lies on the ground at the reported cell. A dropped item is hidden from the NPC but not drawn on the ground (as a
  spilled container's items).
- Native smoke `ARC-COMBAT-009 native-smoke critical PASS` (map mode, after the far-click stage): a granted fate point arms the hero's critical
  hit, the hero attacks the living NPC with the most hit points through the session as the viewer does (worn items, cell), and
  the stage checks the critical hit and that the tables drew; in the Crash Site cave a rat (body type 1) took a doubled torso hit.
- Native smoke: `ARC-COMBAT-005 native-smoke attack PASS` (with `--hero`, after the combat-mode stage, on the exterior and in map
  mode): the living NPC with the fewest hit points is attacked from a few cells away until it dies; the stage checks the attack
  ART, the rolls, the death, the experience, the corpse being lootable, the fallen pose, the status line and that a corpse click
  without Alt does nothing. A map without a living NPC with combat values prints `NO-TARGET`.

## Hero knockdown (ARC-COMBAT-021)

The AREA hero (exterior, cave and map mode; real time and turn-based; both rule modes) falls when the session makes it
unconscious and gets up when the fatigue recovery brings it round, as the original draws a critter (exe rows
[UC1–UC5](../research/ledger/combat.md#hero-knockdown-arc-combat-021), one reader, F1). Owner report of 2026-10-29: the hero at
fatigue −3 kept standing and acting.

- **Session.** Unchanged: `LifeStatus.IsUnconscious` (fatigue at most 0, never for an undead, 0x45D800) is the state; the going
  unconscious of the fatigue setter (IC4) already ends the hero's maintained runs and its casts refuse, and the fatigue recovery
  event (0x45E610) on the session queue, pumped by the viewer's clock, brings it round. No snapshot change.
- **Body.** `HeroBodyArt` also reads the body's fall-down (animation 7, `…XAh.art`) and get-up (animation 8, `…XAi.art`) ARTs
  into the profile (`ActorDiagnosticProfile.FallDown`, `GetUp`, both or neither; actions `FallDown`, `GetUp`); every creatable
  body has them, 11 frames at 11 fps and 19 at 14 fps (UC5; 345 keys a body); a body whose fall or get-up ART is missing, is not one
  entry, does not read in its palette or would pass the key cap loads without both. A human or elf woman, until now drawn with the
  fixed human (which has neither ART), is read as her own body when the session has a hero; without a hero, or when the body
  does not read, the fixed human stays and the hero lies down without a drawn fall.
- **Stages** (`AreaHeroKnockdown`, Godot-free): standing → falling (the fall played once from frame 0 at the ART rate on the
  world clock, starting once the hero stands on its cell) → down (the fall's last frame held while unconscious or dead) →
  getting up (once conscious, UC2–UC3) → standing. A hero that goes down again while getting up falls from the start. A new
  session or map (a load, a transfer, a window move) rebases: a hero down already lies, none falls. The drawn pose is
  `DiagnosticGridActor.DownPose` (action, facing octant at the fall, frame), which wins over the attack pose on a settled actor.
- **Getting up** needs an active hero (UC3): a conscious hero that is stunned, paralysed or stoned stays down until it is active.
- **Turn-based round.** The get-up waits for the hero's own subturn and pays `TurnBasedRules.GetUpCost` (5) through the hero's
  charge (A3, CT3, UC3); a refused charge keeps the hero down and passes the subturn as any refused charge. An unconscious hero
  is skipped by the round (ARC-COMBAT-020).
- **Input.** Going down stops the walk, the attack, the talk, use, inventory and door approaches and closes the spell's target
  cursor. While the hero lies (falling, down, getting up) or is dead, a world press does nothing (`ignored (the hero is down)`,
  UC4: the original's world press returns for a PC that is not active) and the walk keys and stick start no step. Windows and
  keys that check the hero themselves keep their own refusals (combat mode, turn-based, casts, hot keys).
- **Death.** A dead hero plays the dying animation its dying goal (31) names (ARC-COMBAT-1793): the goal's frame, made at the kill
  from the killing blow's choice (`AiExecutorRules.DyingAnimationOf`: 7 plain, 17 head hit, 18 blown-out chunk, 19 severed leg), with
  the Violence Filter forcing 7 (`DyingAnimationRules.Chosen`). The hero body's profile carries the three bloody ARTs as optional
  actions (`ActorDiagnosticProfile.Decapitation`, `BlownOutChunk`, `SeveredLeg`, read by `HeroBodyArt` with the weapon part cleared like the
  fall, each within the key cap and only on a body that has the fall); `DyingArt` answers the ART or, when this body lacks it, the plain fall
  (a typed fallback). The viewer reads the frame while the goal shows (a goal with count 1, made for a hero that was unconscious or knocked down, and a death first seen
  while the hero already lay or fell, keep the plain fall, as an NPC's does, 0x45D730; a goal a frame late, after a kill inside a spell run, is
  read until the plain fall has ended) and the knockdown machine plays that ART instead of the fall; the dying goal waits for it to end, so the
  death cover's 5 s count from the end of the bloody animation. A window move or transfer keeps the animation of the dead hero (its corpse keeps
  the bloody last frame); a new session (a load) reads the saved goal if there is one, else the hero lies on the plain fall. A monster body (hero in a monster body, ARC-MAGIC-052) loads the species' own optional 17, 18 and 19 ARTs the same way (ARC-COMBAT-1865: the file is named by N1 with weapon part 0, a file over the archive entry limit counts as missing) and falls plain for the animation the species lacks; the same bodies read the stunned ART (23, weapon part 1).
- Log lines `ARC-COMBAT-021 hero knockdown stage=… fatigue=…`; the status line adds ` | hero: falling`, `unconscious`, `dead` or
  `getting up`.
- Native smoke `ARC-COMBAT-021 native-smoke knockdown PASS` (with `--hero`, after the audio stage on the Crash Site exterior and in
  the map battery, as its recovery moves the game clock on; alone as `ARC_AREA_MAP_SMOKE_ONLY=knockdown`): a fatigue packet leaves the hero at fatigue −3; it falls
  over drawn frames to the fall's last frame (uploaded), the status line names it, a ground press and the stick do nothing;
  game-time jumps of 10 s fire the recovery, the hero gets up over drawn frames, stands idle and takes a press again; the fatigue
  is healed back.
- Not ready: a stunned or paralysed hero (inactive in the original too, UC4) still takes presses; the knockdown goal's test
  0x424070; a recovered NPC stands at once without its get-up animation; HD frames of the fall and get-up (Legacy per frame).

## Weapon art (ARC-RENDER-037)

A playtest of the packaged build (bug report of 2026-10-29) showed the hero punching with a wielded dagger: the hero's body had one
fixed bare-handed attack ART. The original shows a critter's weapon only in combat mode and attacks with its weapon's ART; read in
the retail 1.0.7.4 executable ([ledger WA1–WA10](../research/ledger/items.md#weapon-art-arc-render-037)). A technical bug of ours,
fixed in both rule modes.

| Rule | Behaviour |
| --- | --- |
| Weapon art (WA1–WA3) | `WeaponArtRules.WieldedArt` (0x465020): out of combat mode the art id's weapon part (bits 0..3) and shield bit (19) are 0; in combat mode the weapon part is the use-art subtype of the weapon slot's item (`WeaponPart`; bare hands 1 without an item or for a subtype of 0), the shield bit 1 for an armour item in the shield slot (`ShieldPart`). The combat-mode toggle and the wield-on and wield-off hooks set it. `WithWeapon`/`WithShield` (0x504100, 0x504180) keep the parts in range. |
| Files (MA1–MA3) | The stand shows weapon and shield, the walk the weapon only, the attack and low attack both, the animations 6..19 (run, vault, fall, get up) neither; a two-handed sword with a shield is written as the sword. A missing armed walk is the walk without the weapon: no installed creatable body has an armed walk, so the armed hero walks without its weapon and stands and attacks with it (`HeroBody.Selector`, `Candidates`, `ArtPath`). |
| Attack animation (WA4–WA6) | `WeaponArtRules.Animation` (the attempt-attack goal's node 0x42B9C0): the throw (14) for a weapon with flag 0x40 (a boomerang), the low attack (21) against a shrunk target (object flag 0x80), one playing animation 7..11 (`AnimationOf`, 0x503E20: 0 for an art that is not a critter's, monster's or unique NPC's) or a short one (`Height`, 0x503F60: every critter is middle, a monster by its species table), else the attack (20). Since ARC-RENDER-039 the viewer plays all three (below). |
| Rate and repair (WA9, WA10) | The rate (0x437990) reads the art id's animation: the attack and low attack take their Speed tables (`RealTimeCombatRules.AttackFramesPerSecond`, `AttackAnimation.Attack`/`AttackLow`), the throw its ART's own rate (`AttackAnimation.Throw`). A missing low attack of bare hands or weapons 2..8, 10 and 13 is the attack's file (MA1); the art id keeps 21, so it plays the attack ART at the low attack's rate. A missing throw, or a low attack of another weapon, fails the goal (WA5). |
| Wear check (WA7, WA8) | The original refuses to wield a weapon or shield whose attack art (combat mode, animation 20) the body lacks. Not ready: the session's wear check leaves the art test to the host (as BA6). |

Viewer (AREA): the hero's body (`HeroBody`, ARC-UI-039) carries `Weapon` and `Shield`; `TerrainViewerHeroBody` builds it from the
session's combat mode, the weapon slot's use art (the inventory host's wear facts, as the body armour's; one not read there is drawn
bare-handed, NOT READY) and the shield slot's item kind (`HeroBody.Armed`), so entering or leaving combat mode or changing the weapon
swaps the body at the next frame, after a playing attack (its ARTs read once a run on the main thread, as a worn armour's: the first
entry into combat mode with a new weapon decodes its nine ARTs in one frame; each weapon and shield variant keeps its own copy of
every frame for the run). An armed body whose ARTs do not read is drawn without the shield, then unarmed, logged. The attack clip takes the drawn attack ART's frames, action frame and weapon slot, so the rate follows the weapon
(`RealTimeCombatRules.AttackFramesPerSecond`); the log line `ARC-COMBAT-005 attack-start` names `weapon=`, `animation=`, `drawn=` and `art=`.

Low attack and throw (ARC-RENDER-039): the hero body also reads its low attack (21, with weapon and shield) and throw (14, without
them; MA2) when they read (`HeroBodyArt`, `ActorDiagnosticProfile.LowAttack`/`Throw`; a low attack the repair turns into the attack is
not read again). The installed creatable bodies have the throw and the low attack of bare hands (with and without a shield) and the bow
(`v` files of weapons `B` and `I`): in underwear the low attack is 7 frames at 13 fps (action frame 5), the throw 11 at 12 fps (action
frame 4), 435 keys a body; every other weapon's low attack is the attack's ART. At each attack start `HeroAttackArt.Animation` picks the
animation from the session's weapon skill (the throwing skill is the returning weapon) and the target's placed art and object flags,
with the fall (7) or dying animation of an NPC the session knocked out or killed; `HeroAttackArt.Clip` gives the ART, the drawn action
(`DiagnosticGridActor.AttackPose` carries it) and the rate table. A hero body without the chosen ART (none installed) does not
attack, and the refusal comes before the turn-based action-point charge (whether the original charges first is not read, F0); the
fixed human stand-in drawn when the body does not load plays its attack ART at the chosen rate. Not ready: no session shrink state (only the placed object flag
makes a target shrunk; the shrink spell does not set it), and the NPC's own animation while the AI walks it is its placed art's.

Native smoke: the `attack` stage (`ARC-COMBAT-005 native-smoke attack PASS`, now with `Weapon`) checks that in combat mode the
attack and stand ARTs show the wielded weapon's part (above 1 for a weapon whose use art reads, bare hands 1 without one) and that
out of combat mode the stand shows none (the human man's kit staff: part 13, 8 frames, action frame 4).
Opt-in real data (`HeroBodyArtTests.EveryPremadeHerosBodyArmourLoadsFromTheInstalledArt`): each pre-made hero's kit weapons and
bare hands load an armed body (the dagger's attack 5 frames at 10 fps, action frame 3; the staff's 8 frames, action frame 4), and a
dagger with a shield loads the shield's stand and attack.

Native smoke (ARC-RENDER-039): the `attack` check knocks the NPC out after raising it and requires the next attack to be the low
attack (`LowAttack` in its PASS line: the drawn action, the attack ART for a repaired weapon); the `ranged` stage's boomerang check
requires each throw to play the throw ART.

Not ready: the NPCs' attack ART (ARC-RENDER-038: the AI host still times NPC attacks with the human stand-in), the wear check's art
test (WA8), the auto-switch re-wield.

## Pool of blood (ARC-RENDER-041)

A critter that dies leaves a pool of blood, as the original does (read in the executable by two readers, ledger
[BP1–BP9](../research/ledger/combat.md#pool-of-blood-arc-render-041)):

- **Rules** (`Arcanum.Simulation.Combat.BloodPoolRules`): `Leaves` refuses on a blocking tile (the dying goal's node 0x42FED0),
  then `Refusal` (the creation 0x432D90) for the violence filter, an undead or mechanical critter (critter flags 0x20000004), an
  elemental (flags 2, 0x400) and a stoned one (spell flags 0x10000). The flag "no blood splotches" (flags 2, 0x100) does not stop
  it: it gates only the hit's blood eye candy, and no installed critter prototype has it. `Frame` gives the frame the pool shows:
  frame 1 at once, one more every 1000 / the ART's rate ms, the last kept. `DecayDelay` and `Decayed`: the Decay Dead Bodies
  event deletes the pool two game days after its creation.
- **Session** (`GameSessionOwner.LeaveBloodPool`, `QueryBloodPools`; snapshot schema 58, `inventory.bloodPools`): the host leaves
  a pool for an NPC with a recorded death or for the dead hero (`pc`), at a map cell with the critter's pixel offsets; one pool
  per recorded death while its pool is kept (a repeated call is NoChange; the host asks once per death), a refusal is NoChange with its reason. A pool is `pool:n`, keeps the death time
  and its creation time, and is not returned from two game days on (the session keeps no queued event: a decayed pool is dropped
  when the next one is left). At most 4096 pools (project bound). A schema 57 save loads with no pools.
- **Viewer (AREA)**: when a recorded death on the shown map that it did not see when the map and session were first shown has
  played its dying animation (ARC-RENDER-040: its dying goal has ended; at once for an NPC whose pose cannot be drawn or who lay in
  its fall; the hero's dying is its fall, ARC-COMBAT-021), it leaves the pool: an NPC at the cell its body is drawn on (a companion's or AI walker's cell with no offset, else its
  map cell and offsets) with its map data's flags, the hero at its committed cell; the session lays the flags it wrote on the critter (stoned, undead, elemental, ARC-COMBAT-1928) over them, hero included. The tile blocks when the step evidence settles it
  as not open. Each pool is drawn flat from prototype 4048's art (`art\scenery\blood.ART`, 4 frames at 8 a second), offset by
  its critter's pixels, never hovered or picked; a pool this view left plays its frames, any other shows its last frame. The
  log says `ARC-RENDER-041 blood-pool critter=… disposition=… refusal=…` for each death and `blood-pool-art 0x…` once.
- **Not ready**: the violence filter (no such
  setting); the weapon's own effect of the projectile speed (0x4377C0 kind 2) and the hit's second add (the hit's own effect and its veto of the blood are built, ARC-RENDER-1990; the begin and the swing effects of the weapon, ARC-RENDER-2011); the dead critter's own one-day decay; a visiting companion from another
  map's seed (such as Virgil in the cave) that dies on the shown map leaves no pool (the view reads the shown map's seed holders
  only); the multiplayer and editor cases.

## Dying animation (ARC-RENDER-040)

A killed NPC plays its dying animation and a knocked-out one its fall, as the original does (read in the executable by two readers,
ledger [DA1–DA6](../research/ledger/combat.md#dying-animation-arc-render-040)):

- **Rules** (`Arcanum.Simulation.Combat.DyingAnimationRules`): `LiesFallen` (the dying goal's test 0x45D730): a critter whose art
  shows the fall (7, any frame) keeps it and its dying goal ends at once. `Start` (0x42D570) and `Delay` (also the knockdown's start
  0x42E4B0): the delay 1000 / the ART's frame rate (integer division; 100 ms when the header does not read). `Frame`: frame 1 at
  once, one more per delay, the last kept (the dying step 0x42D6F0, the knockdown's animate step 0x42CC80 alike). `EndMs`: the dying
  goal ends (frames − 1) delays after its start, when the step finds the last frame; its pool node and its end run then.
- **Viewer (AREA)**: the pose read (`AreaFallenPoses`) decodes every frame of the animation for the body's facing (kept per NPC and
  animation for the session, so a fallen body costs its frame count times one frame's pixels). A death or
  knockout the view sees plays from that moment on the world clock and keeps its last frame; a body already lying when the map and
  session are first shown (a load, a map change) holds its last frame. An NPC killed while lying in its fall keeps the fall (after a
  load too). The hero's pool waits until the dead hero lies down (its fall, ARC-COMBAT-021). A
  recovered or raised NPC stands at once. The pool of blood comes when the dying goal ends ([above](#pool-of-blood-arc-render-041)).
  The log says `ARC-RENDER-040 lying entity=… wanted=… shows=… frames=… delay=… plays|held|kept ends-in=…` once per change.
- **Smoke** (stage `attack`): the killed NPC's dying animation draws its frames on the world clock, no pool lies before its dying
  goal ends, the last frame stays (`ARC-RENDER-040 smoke dying npc=… drawn=…`); the knockout plays the fall. When the stage's attacks knock the target out
  before the killing blow, the death is not played (it keeps the fall, above), so the stage checks that rule instead (`FallKept` of the lying show): the dying goal ends, no pool lies before, and the fall's last frame stays (`ARC-RENDER-040 smoke dying npc=… fall-kept`, ARC-DEV-083).
- **Not ready**: the dying sounds (DA3), the dying goal's finish when
  interrupted (DA6: the viewer holds the last frame after a map change or load), an NPC's get-up animation, and HD frames of the poses.

## Not ready

- AI: no NPC fights back, flees, joins combat mode or changes its reaction when attacked; the attacked NPC's AI pass, combat
  focus and taunts; the exit gate's threats come from the AI host (ARC-COMBAT-1935).
- The [proposed split](#proposed-split-of-true-ranged-combat-not-ready) is done: a
  melee-skill weapon with reach, the ammunition per shot and the line of fire are in since ARC-COMBAT-012, bow and firearm shots
  since ARC-COMBAT-015, projectiles in flight since ARC-COMBAT-016, the "default throws" weapon since ARC-COMBAT-017, a bow's second projectile and a
  missed throw's scatter since ARC-COMBAT-018, boomerangs since ARC-COMBAT-019. Backstab since ARC-COMBAT-1929 (the hero's melee strike with a wielded weapon of art subtype 2, or 3, 4 and 7 above apprentice, on a target not facing it: the backstab level added to the normal damage, five times it when the target neither sees nor hears; `SessionAttack.Backstab` carries the host's facing and awareness facts, none means no backstab; a throw or a shot is never one, as in the original, which clears the context's backstab flags in 0x4B3C00 and 0x4B2870, ARC-COMBAT-1968). Still not ready: aimed-attack critical bonuses beyond the resolver's, the target's awareness (an NPC always sees the hero) and the
  darkness penalty (every position is lit), the critical effects listed above as not ready, the taking-damage
  and dying scripts, the hit's blood eye candy (the pool of blood is in since ARC-RENDER-041), the corpse's decay timer, alignment
  and the kill log.
- The low attack (21) and the boomerang's throw animation (14): the hero always plays the attack (20; ARC-RENDER-039), an NPC's get-up animation
  after a knockout (a recovered NPC stands at once), and HD frames of the fallen poses (Legacy per frame).
- A killed NPC still blocks its cell: step evidence is T0 map data.
- Companions (Virgil while he stands as a companion actor) are not attackable: their combat goal would need their live cell;
  the click says so.
- The pose ART is read from the archive the body's standing art came from, not through the whole content stack, so an override
  of the pose in another archive is not seen; the hero's facing returns to its movement facing after an attack; the cursor's
  attack shape is an OS cursor, not the original's.
- The turn-based round in the viewer is [ARC-COMBAT-020](turn-based-round-runtime.md) (Space and the options window's
  Default Combat Mode set it).
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
corner switch), `tests/Arcanum.Simulation.Tests/Combat/ProjectileRulesTests.cs` and `tests/Arcanum.Session.Tests/ProjectileStateTests.cs`
(ARC-COMBAT-016: the direction and faced art, the flight along the line of fire's tiles and past it, the strike, stray, obstruction
and range end; the shot decided at once and struck on arrival, the forced hit, the end, the limit, no shot after a restore; since
ARC-COMBAT-018 the bow's projectile count, the end of a shot without a target, an expert bow's two shots for one arrow, the second on
a corpse and the limit for two), `tests/Arcanum.Simulation.Tests/Combat/ThrowRulesTests.cs` and
`tests/Arcanum.Session.Tests/ThrowStateTests.cs` (ARC-COMBAT-017 and ARC-COMBAT-018's scatter tiles, walk, map edge and target
location), `tests/Arcanum.Session.Tests/HeroItemThrowStateTests.cs` and the item damage cases of `ThrowRulesTests.cs` (ARC-COMBAT-023:
a pack or worn item thrown at a critter or an empty tile, its damage, scatter, landing and refusals), `tests/Arcanum.Session.Tests/BoomerangStateTests.cs` and the boomerang cases of `ProjectileRulesTests.cs`
(ARC-COMBAT-019: the boomerang kept in the hand and checked with its thrown range, the mark and the refused attack, the turn at a
strike or a range end, the harmless return, its end and no mark after a restore), `tests/Arcanum.Session.Tests/CriticalEffectsStateTests.cs` (ARC-COMBAT-009 and the ammunition per shot of ARC-COMBAT-012: a critical hit disarms
an NPC and the weapon lies at its cell after a restore, items without a cell stay, the hero's dropped weapon lies at its cell until it is picked up (ARC-WORLD-059), a crippled
arm drops a torch it can no longer hold), `tests/Arcanum.Session.Tests/AttackWearStateTests.cs` (armour wear to the second break, the critical
weapon effects and invulnerability, the hero's weapon to its break, bare hands never killing, an NPC attacker, object targets,
missing item data, a refused step, refusals, and the wear of the hero's attack on a stone golem), `tests/Arcanum.TerrainViewer.Tests/AreaCombatTests.cs` (facing, the attack player, the status text,
`Repose`, the frame overlay, the dying animation and the pose read from a synthetic archive with its refusals),
`tests/Arcanum.TerrainViewer.Tests/AreaHeroKnockdownTests.cs` (ARC-COMBAT-021: the stages, the refused get-up, a fall while
getting up, the rebase, a body without the ARTs, the profile's fall and get-up, a synthetic body read with both, one or an unreadable
one, and the down pose; the opt-in real-data
`HeroBodyArtTests` count every body's fall and get-up frames) and the native smokes above; synthetic only in git.

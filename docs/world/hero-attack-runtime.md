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
[FR-CHAR-016](../requirements/characters-skills.md#fr-char-016) (combat skill levels).

## Evidence

- CE at the pinned commit `a7ff41b300ef712f0e7d088183a3d08957110cdd`: `ui/intgame.c` (`sub_54ED30`, the click on a target),
  `skill.c` (skill base, level, key-stat cap, training), `critter.c` (`critter_is_monstrous`), `object.c` (resistance),
  `combat.c` (`combat_dmg`, the dying animation), `anim.c` (the attack and attempt-attack goals, the death and fall-down
  animations).
- The retail 1.0.7.4 `Arcanum.exe`, read statically by two independent readers per the
  [original executable check](../research/original-executable-check.md); rows FA1–FA10 of the
  [verification ledger](../research/original-verification-ledger.md#hero-attack-arc-combat-005). The original differs from CE
  in three CE slips (a follower click, a corpse click without Alt, the inactive-PC test of the click); none is a bug of the
  original, so there is no rule-mode switch.
- The installed data: the twelve pre-made PC prototypes store no skills (the pre-made hero reader refuses one that does), the
  Crash Site creatures are animals and monsters with stored hit-point adjustments of −3 to −6.

## Rules

| Rule | Value |
| --- | --- |
| Click in combat mode (FA1–FA6) | Nothing while the hero cannot act; a living NPC or PC: attack, Shift (left or right) the attempt-attack goal (here run as the same attack); a follower of the hero only with left Alt held; a corpse only with Alt (left or right), which loots it, else nothing; held `,` `.` `/` aim at the head, an arm, a leg. Out of combat mode the click talks or loots as before. |
| Skill level (`CombatSkillRules`, FA7) | 4 per stored point (bits 0..5), or 20 for the melee of a monstrous critter, then the skill's effect changes, clamped 0..20, then capped by the dexterity table `3,3,3,3,3,7,7,7,11,11,11,15,15,15,19,19,19,20,20,20` (dexterity clamped to 1..20); all five combat skills (bow, dodge, melee, throwing, backstab) key on dexterity. |
| Training (FA8) | Stored bits 6..7; a monstrous critter's melee takes the highest tier whose melee level and character level reach 1/10, 9/20, 18/30 in order. |
| Monstrous (FA9) | Mechanical, monster, animal or undead critter flag. |
| Resistance (`CombatantRules.Resistances`) | Stored + worn armour (with its magic, scaled for the wearer) + `5 × (CN − 4)` for poison when positive, clamped 0..95 unless a monstrous NPC (CE `object_get_resistance`); resistance effects are not applied. |
| Reach | The weapon's range, 1 bare-handed; farther away the click route walks (running, as every object approach) to the nearest standable cell within reach first, the attack goal's move-near step. |
| Animation | The hero's bare-handed attack ART (animation 20, weapon slot 1: 9 frames, 15 fps, action frame 6) played by [`RealTimeCombatRules`](real-time-combat-runtime.md#rules-realtimecombatrules) at the rate of the hero's Speed and the pause of its weapon speed; one attack sound roll (1..4) per attack start; facing the target. |
| Hit | The update that shows the action frame runs the hit once when the target is still within reach (else the attack ends without one): `GameSessionOwner.AttackCritter`. |
| Repeat | Auto attack (on, the original's default) starts the attack again 5 ms after it ends while the hero is in combat mode, no round runs and the target is alive. A click elsewhere, the stick, a dialog or window, a quick load, the hero leaving the map or combat mode (refused while an animation plays: the exit gate's attempt-attack test) or losing the ability to act stop it; a refused hit ends it; the same target clicked again keeps its goal. |
| Dying pose (FA10) | A death-flag hit or a death of at most 20 hit points falls (animation 7); otherwise a head hit decapitates (17), a leg hit severs the leg (19), anything else blows out a chunk (18); a knockout falls (7). The body keeps the last frame of that animation in its facing (group 0 for a single-rotation pose ART); a bloody death without its file falls (the original's missing-art repair). |

## Session API (`GameSessionOwner`)

| Entry | Contract |
| --- | --- |
| `SessionCritterCombat.From(entity, fields, carried)` | One NPC's combat values from its ARC-DATA-021 fields and the fields of the items it carries (those in a wear slot count): life facts (pools, level, effective ST under each poison penalty, CN, WP, HealRate, PoisonRecovery, normal resistance, traits from the critter, object and spell flags), the loaded life state (hit-point and fatigue damage, poison, stun and injury flags), the experience worth and pool, and `Combatant(status, state, profile)` with its effective stats under its poison and crippled legs, armour class, resistances, dodge and backstab, light and conditions. Effective stats are the character owner's for the NPC's race and gender over its stored base stats, each clamped to the race's range first (monsters store values beyond it); its stored effects are not applied. Typed `SessionCritterCombatFailure`, never an exception for data. |
| `QueryHeroAttack()` | The hero's `Combatant`, its weapon (bare hands without one), the weapon's skill with its level and training (all 0: the session keeps no skill state and a pre-made hero has none), weapon speed, Speed, range and whether the attack is supported (melee only). Null without a hero. |
| `AttackCritter(expected, attack)` | Refused before any roll: no hero (`AttackUnavailable`), an invalid target, an aim at the torso or an undefined location, a distance outside 0..1 (`InvalidAttack`), a weapon that is not melee (`AttackUnsupported`), the version. Then the resolver on a trial copy of the session generator (`RandomExhausted` when it runs out), adopted once it ran; a resolver refusal (the hero cannot act, the target is dead) is NoChange with the resolution. Then `ApplyDamage` on the target or, on a critical self-hit, the hero; then, with experience data, `RecordNpcDamage` for an NPC that is not stoned and `RecordNpcKill` on a death. Each step commits its own revision; a later refusal is `StepFailure` and leaves the earlier steps. |
| `QueryNpcKilled(entity)` | Whether the session's record of the NPC holds the pinned death damage. |
| Loot | `LootItem` and `QueryMapInventory` count an NPC the session killed as dead, besides those its map data shows dead. |

No snapshot change: deaths, damage and experience are already saved by the life and experience domains.

## Viewer (AREA)

- In combat mode (R) the cursor over a living NPC is the closed hand (`attack` / `walk to attack`), over a corpse nothing
  unless Alt is held (`loot`). The status line shows the last attack: outcome, damage, the target's hit points, a knockout or
  kill and the experience it paid.
- Combat targets are the NPC holders of the map's inventory seed; their combat values are read once per NPC. A map without an
  inventory seed (an unlit whole map, which has no session) has no targets.
- The hero is drawn with its attack ART while it attacks in place (`DiagnosticGridActor.AttackPose`); the human diagnostic profile
  `HumanFixedArea` now loads idle, walk, run, vault and attack (195 keys, 1,705,144 checked RGBA bytes; actor key cap 195).
- A killed or unconscious NPC is drawn in its fallen pose (`AreaFallenPoses`): the pose's ART is read on demand from the archive
  the body's art came from (`AreaLoadResult.ArtSources`), after checking that its entry is the one the published snapshot read
  (ordinal, flags, sizes, offset, name digest); the frame joins the map's frame repository (`AreaFrameOverlay`) and the body is
  drawn, lit and picked as a moved body (`AreaScenePlan.Repose`). A pose that cannot be read leaves the standing body and is
  logged. Reading a pose opens that archive's index on the main thread (a few milliseconds on the installed archives).
- Native smoke: `ARC-COMBAT-005 native-smoke attack PASS` (with `--hero`, after the combat-mode stage, on the exterior and in map
  mode): the living NPC with the fewest hit points is attacked from a few cells away until it dies; the stage checks the attack
  ART, the rolls, the death, the experience, the corpse being lootable, the fallen pose, the status line and that a corpse click
  without Alt does nothing. A map without a living NPC with combat values prints `NO-TARGET`.

## Not ready

- AI: no NPC fights back, flees, joins combat mode or changes its reaction when attacked; the attacked NPC's AI pass, combat
  focus and taunts; the exit gate's threats are still empty.
- Ranged and thrown attacks (ammunition, projectiles, line of fire), the "default throws" weapon, backstab (needs facing and
  perception), aimed-attack critical bonuses beyond the resolver's, the target's awareness (an NPC always sees the hero) and the
  darkness penalty (every position is lit), critical-effect tables (FR-COMBAT-009), weapon and armour wear, the taking-damage
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
- Original-runtime parity: NOT_VERIFIED (static reading only).

## Tests

`tests/Arcanum.Simulation.Tests/Combat/CombatSkillRulesTests.cs` (cap table, levels, monstrous melee, training tiers,
resistances, conditions), `tests/Arcanum.Session.Tests/AttackStateTests.cs` (NPC values from fields, failures, the hero's attack
until the kill with the experience, loot of a session kill, refusals before any roll, the aimed attack, the ranged refusal,
experience-free sessions), `tests/Arcanum.TerrainViewer.Tests/AreaCombatTests.cs` (facing, the attack player, the status text,
`Repose`, the frame overlay, the dying animation and the pose read from a synthetic archive with its refusals) and the native
smoke above; synthetic only in git.

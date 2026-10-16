# Combat core: real time and turn-based, AP, hit, damage, knockout and death

Status: **Research**, work item ARC-RES-030 (2026-10-16). This document fixes
how the pinned source specifies the combat core: entering and leaving combat,
the real-time cadence and the turn-based round, the action-point budget, how
an attack resolves to hit or miss, how damage is computed and applied, and how
fatigue, knockout and death stay separate outcomes. It prepares a later
combat implementation and changes no production API, viewer, session schema or
dialog port. Requirements:
[FR-COMBAT-001](../requirements/combat-magic-tech.md#fr-combat-001),
[FR-COMBAT-002](../requirements/combat-magic-tech.md#fr-combat-002),
[FR-COMBAT-003](../requirements/combat-magic-tech.md#fr-combat-003),
[FR-COMBAT-004](../requirements/combat-magic-tech.md#fr-combat-004),
[FR-COMBAT-005](../requirements/combat-magic-tech.md#fr-combat-005),
[FR-COMBAT-008](../requirements/combat-magic-tech.md#fr-combat-008) and
[FR-COMBAT-010](../requirements/combat-magic-tech.md#fr-combat-010).
Critical-effect tables (FR-COMBAT-009), projectiles and ammunition
(FR-COMBAT-007), attack animation events (FR-COMBAT-006) and the combat UI
(FR-COMBAT-011) are touched only where the core calls them.

## Vocabulary and boundary

- **Source fact**: read from the pinned CE specification
  [`a7ff41b300ef712f0e7d088183a3d08957110cdd`](https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd)
  (Sustainable Use License v1.0); the line ranges linked below are what was
  read. CE is a reconstruction lineage, not an original-executable
  observation, and many functions keep decompiler names (`sub_…`), so a
  reading of their intent is marked as such.
- **CE anomaly**: a branch that looks inconsistent (a likely decompilation or
  reconstruction slip). It is recorded, never adopted silently; the retail
  behaviour is unknown.
- **Project policy**: a choice this project makes where the source leaves a
  runtime or product decision open. Every policy below is a **proposal**.

No original file, probe or game data was read for this work item; every number
is a source constant or formula, not a retail observation. Original executable
parity remains **NOT_VERIFIED**. "Roll d100" means the source's inclusive
`random_between(1, 100)` ([random]); integer arithmetic is C division,
truncating toward zero. Distances are Chebyshev cell distances, as in the
[companion research](companion-follow-evidence.md).

## Answer

- **Two modes, one rule set.** Combat mode is a per-critter flag. The global
  `turn_based` setting decides whether the local PC entering combat mode starts
  a turn-based round; otherwise everyone acts in real time with no action
  points. Hit, damage and outcome rules are identical in both modes.
- **Turn-based round.** Participants are every PC and NPC in a square of
  radius `max(10, PE/2 + 5)` around the PC. NPCs keep the scan order, PCs move
  to the tail; there is **no initiative roll**. Each subturn grants
  `max(5, Speed)` AP. Inactive critters are skipped. After the last subturn the
  game clock advances by one second and the list is rebuilt.
- **AP costs.** Walk step 2, run step 1; attack `1` if weapon speed > 24, `2`
  if > 20, else `max(1, 8 − speed/3)` (unarmed speed 10 → 5 AP); use object 2,
  pick up 1, cast or use a skill or item 4. The local PC may start an action it
  cannot fully pay while it has AP left and fatigue > 1: the AP drop to 0 and
  the PC takes 2 fatigue damage.
- **Hit.** The attack is a skill check: hit when `difficulty + d100 ≤
  effectiveness`, with melee/bow `5 × level + 25`, throwing `7 × level + 25`.
  Difficulty adds the target's armour class as a share of effectiveness, aimed
  location penalties, range, light, blindness and crippled arms, and subtracts
  weapon to-hit and helpless-target bonuses. A hit can still be dodged
  (`5 × dodge level`, capped at 95). Critical hit and miss come from a second
  d100 against `effectiveness/20` and `(100 − effectiveness)/7` (minimum 2).
- **Damage.** Each of five damage types (normal, poison, electrical, fire,
  fatigue) is rolled uniformly in the weapon's range; melee adds the damage
  bonus. Resistance removes a percentage (fatigue uses ¾ of normal
  resistance), easy/hard difficulty scales a PC's damage ×1.5/×0.75, and a
  critical may multiply it by 3, 2 or 1.5 (never when an NPC hits a PC, see
  C20). Normal + electrical + fire reduce hit
  points, fatigue damage reduces fatigue, poison raises the poison level.
- **Outcomes.** Dead means current HP ≤ 0. Unconscious means current fatigue
  ≤ 0 and the critter is not undead. A critical can stun, knock down or (for
  NPCs only) knock out by setting fatigue damage to max + 10..20. Fatigue
  recovers by `HealRate × elapsed/80 s` on an 80-game-second timer and the
  critter gets up once fatigue is positive. A dead PC ends the game after a
  5-second delay and a death slide.

## State model (source facts)

| State | Where | Notes |
| --- | --- | --- |
| Combat mode | critter flag `OCF_COMBAT_MODE_ACTIVE` | Per critter, toggled by [`combat_critter_toggle_combat_mode`][toggle-mode]. |
| Turn-based setting | `turn_based`, `fast_turn_based`, `auto_attack`, `combat_taunts` settings | Registered in [`combat_init`][init]; `turn_based` changes go through [`sub_4B6C90`][tb-switch]. |
| Active round | active flag, turn counter, participant list, current node, AP left, required AP, last actor | Saved and restored by [`combat_save` / `combat_load`][save]. |
| Hit points | `hp_pts`, `hp_adj`, `hp_damage` | Max = `4 × hp_pts + hp_adj + WP + 2 × (ST + level) + 4`, then effects; current = max − damage ([HP][hp]). |
| Fatigue | `fatigue_pts`, `fatigue_adj`, `fatigue_damage` | Max = `4 × fatigue_pts + fatigue_adj + 2 × (level + CN) + WP + 4`, then effects ([fatigue max][fatigue-max]). |
| Poison | stat `POISON_LEVEL` | Raised by poison damage, lowered by healing ([damage apply][dmg-vitals], [heal][heal]). |
| Injuries | critter flags stunned, blinded, crippled one/both arms, crippled legs, scarring effect | Set by critical effects, cleared by healing ([damage][dmg], [heal][heal]). |
| Fate | PC fate bits `CRIT_HIT`, `CRIT_MISS` | One-shot: [`fate_resolve`][fate] clears the bit when it fires. |

## Findings

### Entering, leaving and switching mode (FR-COMBAT-001)

- **C1** The PC enters or leaves combat mode with the combat toggle
  ([input][pc-toggle]). Leaving is refused, with a message, while
  [`combat_can_exit_combat_mode`][exit-mode] is false: the PC is in an
  attempt-attack goal, or an active, fighting NPC in the vicinity has the PC or
  one of its followers as combat focus. Starting a dialog, sleeping and opening
  the world map first leave combat mode and are refused under the same test
  ([dialog][dialog-exit], [sleep][sleep-exit], [world map][wmap-exit]).
- **C2** Combat mode also turns on without a key: the attack goal's approach
  step activates the attacker and, if the target is a PC or a living PC's
  follower, that PC ([attack approach][mode-on-attack]); the AI's attack
  setup also activates its attacker ([AI attack][ai-attacked]). Combat mode
  cannot be entered while stoned, paralysed or otherwise inactive, nor left
  while stoned, paralysed or stunned ([toggle][toggle-mode]).
- **C3** With `turn_based` on, the local PC entering combat mode starts a round
  and takes the first turn; leaving combat mode ends the round
  ([toggle][toggle-mode]). Switching `turn_based` while the PC is in combat
  mode starts or ends the round at once ([switch][tb-switch]). Turning it on
  requires an active PC ([toggle setting][tb-switch]).
- **C4** In real time nothing is budgeted: every `combat_check_*` and
  `combat_consume_action_points` returns success when no round is active
  ([AP checks][ap-check], [consume][ap-consume]). The attack cadence is the
  attack animation itself: each frame waits the Speed-scaled frame time
  (`1000 / rate`, 30..800 ms) minus `10 × (weapon speed − 10)` ms, clamped
  again to 30..800 ms
  ([attack begin/update][attack-begin]). Damage is applied once, by the hit
  step of the attack goal ([goal][attack-goal], [hit step][hit-step]).
- **C5** Real-time repetition: the PC's attack goal restarts on the same
  target while combat mode and the `auto_attack` setting are on, the target is
  alive and not busted, no round is active, the PC is not flagged non-lethal
  and no boomerang is in flight ([auto attack][auto-repeat],
  [setting][auto-attack-get]). In real time pistols and rifles (two frames
  after the action frame) and bows (at the action frame) rewind three frames
  and fire again while ammunition and a valid target remain
  ([attack update][attack-begin]).

### Turn order (FR-COMBAT-002)

- **C6** A round starts by interrupting all animation goals and collecting
  every PC and NPC in the square `PC ± R` with `R = max(10, PE/2 + 5)`
  ([round start][tb-start]). [`sort_combat_list`][sort] moves PCs to the tail
  and otherwise keeps the scan order of `object_list_rect`; it recomputes the
  reaction marker but sorts by nothing else.
- **C7** Each round ([round][tb-round]) increments the turn counter, rebuilds
  the list in the same way, and starts at the first participant that is active
  and drawn. Inactive means destroyed or off, dead, unconscious, stunned,
  paralysed or stoned ([states][states]); a not-drawn critter that is not
  sleeping is also skipped ([skip][tb-skip]). If nobody qualifies but the PC
  is active, the PC acts alone; otherwise the round ends.
- **C8** A subturn sets AP to `max(5, Speed)` ([whose turn][whos-turn]).
  For an NPC (not during a load) it runs the heartbeat script and, when that
  returns 1, the AI; if no animation was started its AP become 0 (reading of
  `sub_423300`). After every animation completes a 2 ms timer
  checks the budget: the PC keeps its turn while AP > 0; an NPC whose AP did not
  change since the previous check is forced to spend the rest, and AP ≤ 0
  advances to the next participant ([anim completion][tb-anims]).
- **C9** [`combat_turn_based_next_subturn`][tb-next] skips inactive entries;
  past the last entry the clock advances **1000 ms** and a new round begins
  (see anomaly A9).
  An NPC that ends its subturn runs its end-combat script. Fast turn-based mode
  (and an NPC that is not in combat mode, attacking or backing off) moves the
  current critter several path steps in one goal while AP remain
  ([fast turn][fast-tb], [TB movement][tb-move]).
- **C10** Critters joining later are inserted by the AI and the list is
  re-sorted ([AI insert][ai-tb-add], [insert][tb-add]). When the AI's attack
  setup puts an attacker into combat mode during a round, the current turn
  passes to that attack's target ([AI attack][ai-attacked]); the intent of
  this decompiled branch is unclear.

### Action points (FR-COMBAT-003)

- **C11** A goal is refused before it starts when its actor is inactive, or in
  a round when it is not the actor's turn, or an NPC lacks the AP while its
  current fatigue is at most 35 % of maximum ([goal start][goal-start]). An
  NPC above 35 % may start anyway; the later charge then fails and ends its
  subturn (C13). Any non-NPC passes this gate.
- **C12** Costs. `combat_check_*` is the cost the UI previews
  ([costs][ap-costs]); the animation goals charge it
  ([attack][attack-begin], [step][step-ap], [path length][path-len],
  [walk or run][run-walk], [item use][item-use]):

  | Action | AP |
  | --- | --- |
  | Walk one step | 2 |
  | Run one step (needs fatigue > 0) | 1 |
  | Estimate for a move | 2 per path step, 1 for a PC with always-run |
  | Attack | weapon speed > 24 → 1; > 20 → 2; else `max(1, 8 − speed/3)` |
  | Melee attack on a target farther than 1 | attack + adjacent-path estimate |
  | Use object | 2 + path estimate when not on the same cell |
  | Pick up item | 1 + path estimate when not on the same cell |
  | Cast spell, use skill | 4 |
  | Use an item on an object or location | 4 (charged by the item use itself) |

  Weapon speed is the weapon's speed factor plus its magic speed adjustment,
  **+5** when the wielder has at least apprentice training in the weapon's
  skill; unarmed speed is a flat 10 ([weapon speed][weapon-speed]).
- **C13** Overexertion: when the cost exceeds the AP left, the local PC with
  AP > 0 and current fatigue > 1 still acts; it takes 2 fatigue damage through
  the normal damage path (so fatigue resistance applies) and its AP become 0.
  Anyone else, or a PC with no AP or fatigue ≤ 1, ends the subturn and the
  action fails ([consume][ap-consume]).
- **C14** A critter that falls unconscious ends its own subturn at once
  ([fatigue set][fatigue-set]).

### Validating the attack target (FR-COMBAT-004)

- **C15** An attack on oneself is refused; a weapon flagged "default throws"
  turns the attack into a throw ([goal start][goal-start]). An attacker in
  Body of Air (not elemental) cannot attack.
- **C16** The attack goal approaches until the target is within the weapon
  range (1 when unarmed or negative) and the line of fire is not blocked by a
  third object nor rated 26 or more by the line-of-fire test
  ([in range][in-range]). The target must not be destroyed or off, and a
  critter target must not be dead ([repeat test][attack-valid]). Corpses take
  no further damage: [`combat_dmg`][dmg] returns for a dead critter after the
  critical branch.
- **C17** A scenery target occupied by a sleeper (a bed) is replaced by the
  sleeper, who leaves the bed; the attack then gets a `−30` difficulty modifier
  ([resolution][resolve]). Non-critter targets (portals, containers, scenery)
  are hit automatically once the technology and magick rolls (steps 3–4
  below) have passed.

### Hit, dodge and critical (FR-COMBAT-005)

Order of resolution in [`sub_4B3170`][resolve]:

1. **Hit location.** Torso means "not aimed": the location is rolled
   (d100 ≤ 70 torso, ≤ 85 leg, ≤ 95 arm, else head) ([roll][hitloc]). Any
   other location marks an aimed attack with penalty head −50, arm −30,
   leg −30 ([penalties][hitloc-pen]).
2. **Ammunition.** Only a PC or a PC's follower consumes ammunition; on
   shortage the attack ends with an out-of-ammo sound and the weapon switch
   ([ammo][ammo]).
3. **Technology failure.** A technological weapon in the hands of a magickal
   user fails critically with chance `−complexity × aptitude / 100`
   ([aptitude][aptitude]); this becomes a critical miss.
4. **Magick against technology.** Otherwise a ranged attack whose line of
   fire is blocked is first retargeted to the blocking object. Then a d100
   against the target's opposing aptitude, scaled by the attacker's own
   aptitude (a technological target against a magickal weapon, a magickal
   target against a technological one), may cancel the weapon: a
   magickal melee weapon still swings without its magical bonuses, any other
   attack simply misses ([aptitude][aptitude], [resolution][resolve]).
5. **Skill roll** ([run][skill-run]): `success = difficulty + d100 ≤
   effectiveness`. Effectiveness ([effectiveness][effectiveness],
   [firearms][firearms-eff]) is melee and bow `5L + 25`, throwing `7L + 25`,
   firearms `5L + 25` (−20 against Magnetic Inversion), +10 when IN is
   extraordinary, ×1.5 on easy and ×0.75 on hard for the PC. Combat skills are
   **not** capped at 95. `L` is the skill level 0..20 owned by the character
   rules (4 per allocated point, capped by the key stat;
   [skill level][skill-level]).
6. **Difficulty** ([difficulty][difficulty]), summed:
   - the invocation modifier (the bed `−30`; see anomaly A2);
   - minus the weapon's to-hit bonus (plus magic adjustment), plus
     `5 × (min ST − ST)` when the wielder is too weak;
   - against a critter: −50 paralysed, unconscious or sleeping, −30 falling or
     stunned, −30 when the target is not in combat mode and can neither see
     nor hear the attacker;
   - aimed: minus the location penalty (two thirds of it for an expert
     firearms user);
   - armour: `effectiveness × (AC/2) / 100`, skipped for a true backstab
     (see below). AC = base AC + worn items + `AcAdjustment` and
     effects, clamped 0..95 ([AC][ac]);
   - bow, throwing, firearms only: out of range or a blocking object makes the
     hit impossible; each cell beyond `PE/2` adds 5 (not for masters); cover
     adds the line-of-fire rating;
   - light (not undead; melee experts exempt): `30 × (255 − light)/255`,
     reduced by the attacker's own light (÷4, ÷3, ÷2, ×⅔) and inverted by dark
     sight;
   - blinded +30; crippled arms +20 one, +50 both.
7. **Critical.** A second d100 decides the critical ([crit chance][crit-chance]):
   on success critical when `roll ≤ effectiveness/20` + aimed bonus
   (head 10, arm or leg 6) + weapon magic + backstab terms + effects; on failure
   critical when `roll ≤ (100 − effectiveness)/7` + item wear + weapon magic +
   effects, at least 2. A melee master never fails critically.
8. **Dodge.** A hit critter rolls Dodge (`5L`, capped 95, auto-fail when
   unconscious, stoned, paralysed or stunned; difficulty from blindness and
   crippled legs only). A successful dodge turns the hit into a miss; a critical
   dodge makes it a critical miss with chance 0/10/50/100 % by dodge training
   ([resolution][resolve]).
9. **Fate.** A PC attacker with an armed critical-hit fate always hits
   critically; when the attacker is not a PC, a PC target with an armed
   critical-miss fate makes the attack a critical miss ([resolution][resolve], [fate][fate]).
10. A critical miss turns the attack on the attacker ([crit miss][crit-miss]):
    it always strikes itself with a normal damage roll, and with chance 50 %
    the self-hit is also critical, adding chances to destroy or explode the
    weapon and lose ammunition plus a critical-hit roll on itself (anomaly
    A7). The effect tables belong to FR-COMBAT-009.
11. Ranged attacks continue as a projectile; melee applies damage now
    ([melee][melee], [ranged][ranged]).

Backstab ([eligibility][backstab], [context][context]): a trained backstabber
behind a target that is not facing it, with a dagger (an expert also with a
sword, axe or two-handed sword). It is a true backstab when the target can
neither see nor hear the attacker; then the critical chance gains
`2 × backstab level − target level` (+20 for a master).

### Damage (FR-COMBAT-008)

- **C18** Rolls ([calc][calc], [weapon damage][weapon-dmg]): per damage type,
  uniform in `[min, max]` from the weapon, a monster's own table, or unarmed
  (normal and fatigue only: `max(1, gauntlet bonus − 25) .. gauntlet bonus +
  5`). Melee adds `DamageBonus` to normal and fatigue minimum and maximum;
  magic damage adjustment follows; results are clamped to `0 .. 3 ×` the base
  maximum (2× unarmed).
  A backstab adds `backstab level` normal damage, ×5 for a true backstab.
  Fatigue ×4 against a fatigue-draining target, ÷4 against a
  fatigue-limiting one. Poison is 0 against undead, mechanical or stoned
  targets. Unarmed Body of Earth/Fire/Water and Hardened Hands add fixed
  damage.
- **C19** Application ([damage][dmg]): the target's taking-damage script may
  veto; destroyed or invulnerable targets and walls take nothing; a
  nigh-invulnerable critter takes nothing from a non-uber weapon; a stoned
  critter's rolled damage is zeroed, though critical effects and full or death
  damage still apply. Then critical effects, then resistance
  ([resistance][resist]): each type loses `resistance %`, fatigue against
  `¾` of normal resistance, unless ignored by flag. Resistance = base + worn
  armour + armour magic + `5 × (CN − 4)` for poison when positive + effects,
  clamped 0..95 except for monstrous NPCs ([object resistance][resistance]).
  There is no separate armour-subtraction step: armour acts through AC (hit)
  and resistance (damage).
- **C20** HP damage is `normal + electrical + fire` (the full current HP for a
  death or full-damage flag). A PC attacker's HP damage is ×1.5 on easy and
  ×0.75 on hard. A critical hit multiplies HP damage by 3 when
  `d100 ≤ chance + 10`, else by 2 when a new `d100 ≤ chance + 30`, else by
  1.5 when a third `d100 ≤ chance + 60`; these rolls are skipped when an NPC
  hits a PC, and ×1.5 is also the fallback when the critical produced no
  other effect at all ([crit hit][crit-hit]). A critical can therefore carry
  no multiplier. Fatigue damage
  is added unless the target is fatigue-immune; poison raises the poison level.
  Fire damage or a damage-armour weapon also wears the armour at the hit
  location by 10 HP ([armour slot][armor-slot]).
- **C21** Side effects ([vitals][dmg-vitals]): any attacker drains
  `worth × damage% / 100` from the NPC's experience pool, and only a PC
  attacker is paid it; a PC follower dropping below 20 %
  HP triggers its near-death reaction; a melee hit wears the weapon by the
  target's material, or hurts an unarmed attacker but never kills it
  ([wear][wear]). Portals and containers subtract a material reduction (stone,
  brick, metal, force 10; wood 5) and are busted, triggering any trap, at 0 HP
  ([damage][dmg]).

### Fatigue, knockout and death (FR-COMBAT-010)

- **C22** Dead is `current HP ≤ 0`; unconscious is `current fatigue ≤ 0` for a
  non-undead critter; active excludes both and stunned, paralysed, stoned
  ([states][states]). The fatigue and HP pools are independent: no damage
  converts between them.
- **C23** Going unconscious (a non-zero fatigue-damage write that brings
  current fatigue to ≤ 0, i.e. damage reaching the maximum) demaintains the
  critter's spells, notifies its items, plays the knockdown and ends its
  subturn ([fatigue set][fatigue-set]).
- **C24** Critical outcomes ([crit hit][crit-hit]): stun when
  `d100 ≤ chance + 5` (+10 crushing or electrical, +10 head without helmet)
  and the target fails a CN check at −5; for an NPC that is not fatigue-immune
  a second roll (`d100 < chance + 5 …`, strict) with another failed CN check
  turns it into a **knockout**, which sets fatigue damage to
  `max fatigue + 10..20` ([damage][dmg]). Undead and amorphous targets are
  never stunned or knocked out, and a PC is never knocked out by a critical.
  Bipeds can be knocked down (`chance + 5`, +5 crushing, +10 leg). `chance`
  is the weapon's magic critical-effect value (0 unarmed) plus effects, +10
  when aimed; with a weapon whose magic was cancelled it is undefined
  (anomaly A8).
- **C25** Recovery ([fatigue event][fatigue-event], [time][time]): any non-zero
  fatigue damage schedules one recovery event per critter 80 000 ms of game
  time ahead. It removes `HealRate × max(1, elapsed seconds/80)` fatigue damage;
  when fatigue becomes positive again the critter gets up. Resting heals
  `HealRate × hours` HP and three times that fatigue ([rest][rest]). In a round,
  getting up is gated by the round (anomaly A3).
- **C26** Death ([kill][kill]): the dying script may veto; otherwise combat
  mode ends, the death time is stored, an NPC records its killer as combat
  focus, the PC killer (or the killer's PC leader) gains 20 % of the NPC's
  experience worth, alignment and the kill log are updated, followers are told,
  the NPC leaves its leader's follower list but keeps the leader handle, the
  dying animation plays, the body is scheduled to decay
  after one game day unless flagged otherwise, and HP damage is pinned at
  32 000. The dying animation is the plain fall for death-flag damage or
  ≤ 20 damage, otherwise a head, leg or body variant ([vitals][dmg-vitals]).
- **C27** When the dying animation of the local PC ends, the game waits 5 s,
  shows the death slide, resets and returns to the main menu
  ([dying][dying], [end death][end-death], [reset][death-reset]).
- **C28** Healing ([heal][heal]) lowers HP, fatigue and poison damage; a full
  heal also clears injuries. Resurrection of a dead critter is gated by its
  resurrect script and the unrevivifiable/unresurrectable flags, resets
  fatigue damage to 10 and rejoins its leader when the follow check passes.
- **C29** The `non-lethal combat` flag stops automatic attack repetition (C5)
  and makes the AI reject an unconscious target ([AI target][ai-nonlethal]);
  no reviewed damage branch converts lethal damage to knockout
  ([auto attack][auto-repeat], [damage][dmg]).

### Persistence (FR-COMBAT-001)

- **C30** A save stores the turn-based setting, the active flag and turn
  counter; when a round is active also the AP left, the current participant,
  the required AP and two further actor handles. Loading
  restarts the round (rebuilding the list), then restores AP and the current
  participant; required AP are reset to 0 ([save/load][save]).

## CE anomalies (recorded, not adopted)

- **A1** `sort_combat_list` tests "not a PC" twice, so only PCs move to the
  tail and PC followers keep their scan position ([sort][sort]).
- **A2** `skill_invocation_run` adds the invocation modifier to a difficulty
  that already starts from the same modifier, so the bed modifier counts
  twice (−60) ([run][skill-run], [difficulty][difficulty]).
- **A3** Getting up in a round is refused on the critter's own subturn and
  free on any other subturn; the 5 AP charge is unreachable
  ([get up][getup]).
- **A4** The every-fifth-run-step fatigue charge in combat mode (every 20th
  for fatigue-limiting, every 1–2nd for fatigue-draining critters) writes back
  the unchanged fatigue damage, so running never costs fatigue; and because
  the write returns the stored value, a critter with no fatigue damage fails
  that step without paying AP. What the failed step does next was not traced
  ([step][step-ap], [fatigue set][fatigue-set]).
- **A5** A crippled-arm critical drops the **weapon** handle when only the
  shield qualifies; partial healing of crippled legs clears blindness instead
  ([damage][dmg], [heal][heal]).
- **A6** `object_get_ac` applies AC-adjustment effects to the whole AC and
  then adds the effective `AcAdjustment` stat again ([AC][ac]).
- **A7** On a critical self-hit the critical-hit effects are rolled twice: once
  inside the critical-miss branch and again when the damage is applied
  ([crit miss][crit-miss], [damage][dmg]).
- **A8** The critical-hit `chance` is never assigned when the weapon's magic
  was cancelled (step 4), while the critical-miss path sets it to 0
  ([crit hit][crit-hit], [crit miss][crit-miss]).
- **A9** When the skip loop of `combat_turn_based_next_subturn` runs past the
  last entry, the new round already starts its first subturn and the function
  then starts it a second time, so an NPC acting first runs its heartbeat
  script and AI twice ([next subturn][tb-next], [round][tb-round]).

## Open questions

- **O1** Retail parity of every constant and formula above (NOT_VERIFIED).
  An original-runtime oracle would need scripted fights with a fixed seed.
- **O2** The scan order of `object_list_rect`, which is the actual NPC order in
  a round, was not read.
- **O3** The Speed-scaled animation rate (`sub_437990`), so the real-time
  cadence as a function of Speed, was not read.
- **O4** The line-of-fire test `sub_4ADE00` (the 26 threshold and the cover
  rating) belongs to FR-COMBAT-007 and was not read.
- **O5** How AI chooses attacks, flees or ends its subturn early
  (`ai_process`), and what an NPC does in real time between attacks, belong to
  the AI section.
- **O6** Weapon, armour and critter prototype fields (damage ranges, speed
  factor, range, to-hit, minimum strength, crit charts, AC, resistances) were
  not read from data; the Crash Site combatants are unknown.
- **O7** Whether the recovery event re-arms itself while it runs (duplicate
  check against the event being processed) was not traced.

## Steamwoven inputs that already exist

[`CharacterStatRules`](../../src/Arcanum.Simulation/Character/CharacterStatRules.cs)
already derives `Speed`, `DamageBonus`, `AcAdjustment` and `HealRate` with the
same source formulas ([contract](../world/character-stats-runtime.md)); the
session clock provides game time for the 80-second recovery and the
one-second round advance; exact movement and the click-route planner provide
steps. Skills, training, hit points, fatigue, resistances, AC, items and
equipment do not exist yet.

## Proposed implementation slices (project policy, not accepted)

Each slice is a separate PR; all keep Data/Simulation/Session free of Godot and
take randomness from an injected, seedable source so tests can pin every roll.

1. **Vital state** in Simulation: HP and fatigue pools with the formulas above,
   poison level, injury flags, `IsDead`/`IsUnconscious`/`IsActive`, damage and
   heal transitions as typed results, the 80-second recovery tick on game
   time. Synthetic tests: knockout without death, death at exactly 0 HP,
   undead never unconscious, recovery and get-up.
2. **Attack resolver**: a pure function over explicit combatant and weapon
   records (skill level and training, AC, resistances, flags, light, distance)
   returning hit location, hit/miss/dodge, critical and the per-type damage
   with the difficulty and resistance steps (C17–C21), with the critical-effect
   table left as an injected FR-COMBAT-009 hook. Anomalies A1–A9 are settled
   by an explicit owner decision, not copied.
3. **Turn-based round**: participants, skip rules, AP grant and costs,
   overexertion, end of round +1 s and save/restore of round state as a new
   snapshot schema version (C6–C14, C30). Initiative stays "scan order, PCs
   last" unless the owner chooses otherwise after O2.
4. **Real-time cadence and combat mode**: the per-critter mode flag, the exit
   rule (C1), attack timing from animation rate and weapon speed (C4) and the
   auto-attack setting (C5).
5. **Data**: read weapon, armour and critter combat fields from prototypes
   (O6) on the Mac, then a first Crash Site fight in the AREA viewer.

## Method

Source reading only, at the pinned commit above, through the call chains named
in each row: `combat.c` in full, and the called parts of `skill.c`, `item.c`,
`object.c`, `critter.c`, `anim.c`, `ai.c`, `fate.c`, `random.c`,
`timeevent.c` and the UI callers. No CE code or structure was copied; names in
this document are descriptive. The open questions are the parts this reading
could not settle.

[init]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L198-L227
[save]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L249-L347
[context]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L350-L404
[resolve]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L787-L996
[melee]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L999-L1054
[ranged]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L1057-L1116
[exit-mode]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L1173-L1224
[toggle-mode]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L1243-L1375
[dmg]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L1390-L1944
[dmg-vitals]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L1768-L1879
[armor-slot]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L1947-L1959
[wear]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L1962-L2077
[heal]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L2104-L2291
[hitloc-pen]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L76-L82
[crit-hit]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L2327-L2500
[crit-miss]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L2503-L2561
[hitloc]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L2564-L2571
[ammo]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L2574-L2604
[calc]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L2607-L2679
[resist]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L2682-L2710
[tb-switch]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L2857-L2902
[whos-turn]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L2953-L3038
[tb-anims]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3041-L3103
[tb-start]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3106-L3180
[tb-round]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3183-L3256
[tb-next]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3272-L3330
[ap-check]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3339-L3369
[ap-costs]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3372-L3526
[ap-consume]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3529-L3581
[tb-add]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3594-L3631
[sort]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3634-L3731
[fast-tb]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3734-L3765
[auto-attack-get]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3843-L3850
[skill-level]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/skill.c#L486-L582
[effectiveness]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/skill.c#L771-L853
[firearms-eff]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/skill.c#L1276-L1344
[skill-run]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/skill.c#L1518-L1630
[crit-chance]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/skill.c#L2066-L2176
[difficulty]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/skill.c#L2210-L2481
[aptitude]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L482-L573
[weapon-speed]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L3279-L3353
[weapon-dmg]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L3399-L3504
[hp]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L1632-L1677
[resistance]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L1680-L1730
[ac]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L1733-L1763
[fatigue-set]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L462-L495
[fatigue-max]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L502-L534
[states]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L569-L615
[kill]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L622-L757
[fatigue-event]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L1304-L1405
[rest]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L1421-L1453
[backstab]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L2281-L2319
[attack-goal]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L752-L776
[path-len]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L5051-L5085
[attack-valid]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L6938-L6990
[auto-repeat]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L7015-L7077
[in-range]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L7107-L7154
[hit-step]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L7686-L7714
[mode-on-attack]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L8465-L8500
[getup]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L10204-L10245
[tb-move]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L10415-L10449
[run-walk]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L10504-L10556
[dying]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L11108-L11128
[step-ap]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L11671-L11741
[attack-begin]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L12729-L12926
[goal-start]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L13874-L14023
[ai-attacked]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1054-L1068
[ai-tb-add]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1409-L1453
[fate]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/fate.c#L132-L155
[random]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/random.c#L64-L73
[time]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L222-L290
[pc-toggle]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/intgame.c#L3195-L3218
[end-death]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/tb_ui.c#L289-L292
[death-reset]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/anim_ui.c#L159-L180
[dialog-exit]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/dialog_ui.c#L136-L145
[sleep-exit]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/sleep_ui.c#L297-L305
[wmap-exit]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/wmap_ui.c#L1182-L1190
[tb-skip]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3584-L3591
[item-use]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L1415-L1560
[ai-nonlethal]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2066-L2076

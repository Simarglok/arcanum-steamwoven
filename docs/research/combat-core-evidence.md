# Combat core: real time and turn-based, AP, hit, damage, knockout and death

Status: **Research**, work item ARC-RES-030 (2026-10-16). This document fixes
how the game specifies the combat core: entering and leaving combat,
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

- **Source fact**: read from the executable's functions at the addresses
  given, level F0 (not checked in the executable) unless a row of the
  [original verification ledger](original-verification-ledger.md) is cited.
  Many functions keep decompiler names (`sub_…`), so a reading of their
  intent is marked as such. Earlier provenance is in git history.
- **Anomaly**: a branch that looks inconsistent (a likely decompilation or
  reconstruction slip). It is recorded, never adopted silently; the retail
  behaviour is unknown except for A1–A10, which were checked against the
  original executable ([original check](#original-executable-check-arc-combat-001)).
- **Project policy**: a choice this project makes where the source leaves a
  runtime or product decision open. Every policy below is a **proposal**.

No original file, probe or game data was read for ARC-RES-030; every number
is a source constant or formula, not a retail observation. Original executable
parity remains **NOT_VERIFIED**, except for the ten anomalies of the later
[original check](#original-executable-check-arc-combat-001). "Roll d100" means the source's inclusive
`random_between(1, 100)` (random (0x4CEF00)); integer arithmetic is C division,
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
  (`5 × dodge level` in the earlier reading, capped at 95; the original uses `2 × dodge level`,
  [ARC-RES-048](original-verification-ledger.md)). Critical hit and miss come from a second
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
| Combat mode | critter flag `OCF_COMBAT_MODE_ACTIVE` | Per critter, toggled by `combat_critter_toggle_combat_mode` (0x4B3F80). |
| Turn-based setting | `turn_based`, `fast_turn_based`, `auto_attack`, `combat_taunts` settings | Registered in `combat_init` (0x4B1D50); `turn_based` changes go through `sub_4B6C90` (0x4B6C90, 0x4B6D20). |
| Active round | active flag, turn counter, participant list, current node, AP left, required AP, last actor | Saved and restored by `combat_save` / `combat_load` (0x4B1EA0, 0x4B2020). |
| Hit points | `hp_pts`, `hp_adj`, `hp_damage` | Max = `4 × hp_pts + hp_adj + WP + 2 × (ST + level) + 4`, then effects; current = max − damage (HP (0x43D5A0, 0x43D600, 0x43D630, 0x43D690)). |
| Fatigue | `fatigue_pts`, `fatigue_adj`, `fatigue_damage` | Max = `4 × fatigue_pts + fatigue_adj + 2 × (level + CN) + WP + 4`, then effects (fatigue max (0x45D670, 0x45D700)). |
| Poison | stat `POISON_LEVEL` | Raised by poison damage, lowered by healing (damage apply (0x4B4390), heal (0x4B58C0)). |
| Injuries | critter flags stunned, blinded, crippled one/both arms, crippled legs, scarring effect | Set by critical effects, cleared by healing (damage (0x4B4390), heal (0x4B58C0)). |
| Fate | PC fate bits `CRIT_HIT`, `CRIT_MISS` | One-shot: `fate_resolve` (0x4F5270) clears the bit when it fires. |

## Findings

### Entering, leaving and switching mode (FR-COMBAT-001)

- **C1** The PC enters or leaves combat mode with the combat toggle
  (input (0x54EBF0)). Leaving is refused, with a message, while
  `combat_can_exit_combat_mode` (0x4B3D90) is false: the PC is in an
  attempt-attack goal, or an active, fighting NPC in the vicinity has the PC or
  one of its followers as combat focus. Starting a dialog, sleeping and opening
  the world map first leave combat mode and are refused under the same test
  (dialog (0x567460), sleep (0x57B180), world map (0x5607E0)).
- **C2** Combat mode also turns on without a key: the attack goal's approach
  step activates the attacker and, if the target is a PC or a living PC's
  follower, that PC (attack approach (0x42B9C0)); the AI's attack
  setup also activates its attacker (AI attack (0x4A9AD0)). Combat mode
  cannot be entered while stoned, paralysed or otherwise inactive, nor left
  while stoned, paralysed or stunned (toggle (0x4B3F80)).
- **C3** With `turn_based` on, the local PC entering combat mode starts a round
  and takes the first turn; leaving combat mode ends the round
  (toggle (0x4B3F80)). Switching `turn_based` while the PC is in combat
  mode starts or ends the round at once (switch (0x4B6C90, 0x4B6D20)). Turning it on
  requires an active PC (toggle setting (0x4B6C90, 0x4B6D20)).
- **C4** In real time nothing is budgeted: every `combat_check_*` and
  `combat_consume_action_points` returns success when no round is active
  (AP checks (0x4B7790), consume (0x4B7C90, 0x4B7CD0)). The attack cadence is the
  attack animation itself: each frame waits the Speed-scaled frame time
  (`1000 / rate`, 30..800 ms) minus `10 × (weapon speed − 10)` ms, clamped
  again to 30..800 ms
  (attack begin/update (0x432700, 0x432990)). Damage is applied once, by the hit
  step of the attack goal (goal, hit step (0x42A630)).
- **C5** Real-time repetition: the PC's attack goal restarts on the same
  target while combat mode and the `auto_attack` setting are on, the target is
  alive and not busted, no round is active, the PC is not flagged non-lethal
  and no boomerang is in flight (auto attack (0x429760),
  setting (0x4B8230)). In real time pistols and rifles (two frames
  after the action frame) and bows (at the action frame) rewind three frames
  and fire again while ammunition and a valid target remain
  (attack update (0x432700, 0x432990)).

### Turn order (FR-COMBAT-002)

- **C6** A round starts by interrupting all animation goals and collecting
  every PC and NPC in the square `PC ± R` with `R = max(10, PE/2 + 5)`
  (round start (0x4B71E0, 0x4B7300, 0x4B7330)). `sort_combat_list` (0x4B7EB0) moves PCs to the tail
  and otherwise keeps the scan order of `object_list_rect`; it recomputes the
  reaction marker but sorts by nothing else.
- **C7** Each round (round (0x4B73A0, 0x4B7580)) increments the turn counter, rescans
  the square and merges the result into the list (participants of earlier
  rounds stay, new ones are put in front, the last found first; confirmed in
  the original by ARC-COMBAT-003), sorts it, and starts at the first
  participant that is active and drawn. Inactive means destroyed or off, dead, unconscious, stunned,
  paralysed or stoned (states (0x45D790, 0x45D800, 0x45D870, 0x45D8D0)); a not-drawn critter that is not
  sleeping is also skipped (skip (0x4B7DC0)). If nobody qualifies but the PC
  is active, the PC acts alone; otherwise the round ends.
- **C8** A subturn sets AP to `max(5, Speed)` (whose turn (0x4B6E70, 0x4B7010)).
  For an NPC (not during a load) it runs the heartbeat script and, when that
  returns 1, the AI; if no animation was started its AP become 0 (reading of
  `sub_423300`). After every animation completes a 2 ms timer
  checks the budget: the PC keeps its turn while AP > 0; an NPC whose AP did not
  change since the previous check is forced to spend the rest, and AP ≤ 0
  advances to the next participant (anim completion (0x4B7080, 0x4B7150)).
- **C9** `combat_turn_based_next_subturn` (0x4B7630, 0x4B7690, 0x4B7740) skips inactive entries;
  past the last entry the clock advances **1000 ms** and a new round begins
  (see anomaly A9).
  An NPC that ends its subturn runs its end-combat script. Fast turn-based mode
  (and an NPC that is not in combat mode, attacking or backing off) moves the
  current critter several path steps in one goal while AP remain
  (fast turn (0x4B8040), TB movement (0x42E9B0)).
- **C10** Critters joining later are inserted by the AI and the list is
  re-sorted (AI insert (0x4AA620), insert (0x4B7E00)). When the AI's attack
  setup puts an attacker into combat mode during a round, the current turn
  passes to that attack's target (AI attack (0x4A9AD0)); the intent of
  this decompiled branch is unclear.

### Action points (FR-COMBAT-003)

- **C11** A goal is refused before it starts when its actor is inactive, or in
  a round when it is not the actor's turn, or an NPC lacks the AP while its
  current fatigue is at most 35 % of maximum (goal start (0x4348E0, 0x434980, 0x434AE0, 0x434B00)). An
  NPC above 35 % may start anyway; the later charge then fails and ends its
  subturn (C13). Any non-NPC passes this gate.
- **C12** Costs. `combat_check_*` is the cost the UI previews
  (costs (0x4B7830, 0x4B78D0, 0x4B79A0, 0x4B7A20, 0x4B7AA0, 0x4B7AE0, 0x4B7BA0, 0x4B7C20, 0x4B7C30)); the animation goals charge it
  (attack (0x432700, 0x432990), step (0x430FC0), path length (0x4261E0, 0x426250),
  walk or run (0x42EDC0, 0x42EE90), item use (0x462CC0, 0x462FC0)):

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
  skill; unarmed speed is a flat 10 (weapon speed (0x465CF0, 0x465D80, 0x465E30)).
- **C13** Overexertion: when the cost exceeds the AP left, the local PC with
  AP > 0 and current fatigue > 1 still acts; it takes 2 fatigue damage through
  the normal damage path (so fatigue resistance applies) and its AP become 0.
  Anyone else, or a PC with no AP or fatigue ≤ 1, ends the subturn and the
  action fails (consume (0x4B7C90, 0x4B7CD0)).
- **C14** A critter that falls unconscious ends its own subturn at once
  (fatigue set (0x45D5A0)).

### Validating the attack target (FR-COMBAT-004)

- **C15** An attack on oneself is refused; a weapon flagged "default throws"
  turns the attack into a throw (goal start (0x4348E0, 0x434980, 0x434AE0, 0x434B00)). An attacker in
  Body of Air (not elemental) cannot attack.
- **C16** The attack goal approaches until the target is within the weapon
  range (1 when unarmed or negative) and the line of fire is not blocked by a
  third object nor rated 26 or more by the line-of-fire test
  (in range (0x429960)). The target must not be destroyed or off, and a
  critter target must not be dead (repeat test (0x4294F0)). Corpses take
  no further damage: `combat_dmg` (0x4B4390) returns for a dead critter after the
  critical branch.
- **C17** A scenery target occupied by a sleeper (a bed) is replaced by the
  sleeper, who leaves the bed; the attack then gets a `−30` difficulty modifier
  (resolution (0x4B3170)). Non-critter targets (portals, containers, scenery)
  are hit automatically once the technology and magick rolls (steps 3–4
  below) have passed.

### Hit, dodge and critical (FR-COMBAT-005)

Order of resolution in `sub_4B3170` (0x4B3170):

1. **Hit location.** Torso means "not aimed": the location is rolled
   (d100 ≤ 70 torso, ≤ 85 leg, ≤ 95 arm, else head) (roll (0x4B65A0)). Any
   other location marks an aimed attack with penalty head −50, arm −30,
   leg −30 (penalties).
2. **Ammunition.** Only a PC or a PC's follower consumes ammunition; on
   shortage the attack ends with an out-of-ammo sound and the weapon switch
   (ammo (0x4B65D0)).
3. **Technology failure.** A technological weapon in the hands of a magickal
   user fails critically with chance `−complexity × aptitude / 100`
   (aptitude (0x461590, 0x461620, 0x461700)); this becomes a critical miss.
4. **Magick against technology.** Otherwise a ranged attack whose line of
   fire is blocked is first retargeted to the blocking object. Then a d100
   against the target's opposing aptitude, scaled by the attacker's own
   aptitude (a technological target against a magickal weapon, a magickal
   target against a technological one), may cancel the weapon: a
   magickal melee weapon still swings without its magical bonuses, any other
   attack simply misses (aptitude (0x461590, 0x461620, 0x461700), resolution (0x4B3170)).
5. **Skill roll** (run (0x4C7160)): `success = difficulty + d100 ≤
   effectiveness`. Effectiveness (effectiveness (0x4C62E0),
   firearms (0x4C69F0)) is melee and bow `5L + 25`, throwing `7L + 25`,
   firearms `5L + 25` (−20 against Magnetic Inversion), +10 when IN is
   extraordinary, ×1.5 on easy and ×0.75 on hard for the PC. Combat skills are
   **not** capped at 95. `L` is the skill level 0..20 owned by the character
   rules (4 per allocated point, capped by the key stat;
   skill level (0x4C5E50, 0x4C5EB0, 0x4C5F70, 0x4C5FA0)).
6. **Difficulty** (difficulty (0x4C8430)), summed:
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
     effects, clamped 0..95 (AC (0x43D880));
   - bow, throwing, firearms only: out of range or a blocking object makes the
     hit impossible; each cell beyond `PE/2` adds 5 (not for masters); cover
     adds the line-of-fire rating;
   - light (not undead; melee experts exempt): `30 × (255 − light)/255`,
     reduced by the attacker's own light (÷4, ÷3, ÷2, ×⅔) and inverted by dark
     sight;
   - blinded +30; crippled arms +20 one, +50 both.
7. **Critical.** A second d100 decides the critical (crit chance (0x4C81A0, 0x4C82E0)):
   on success critical when `roll ≤ effectiveness/20` + aimed bonus
   (head 10, arm or leg 6) + weapon magic + backstab terms + effects; on failure
   critical when `roll ≤ (100 − effectiveness)/7` + item wear + weapon magic +
   effects, at least 2. A melee master never fails critically.
8. **Dodge.** A hit critter rolls Dodge (`5L` in the earlier reading, `2L` in the original
   per [ARC-RES-048](original-verification-ledger.md); capped 95, auto-fail when
   unconscious, stoned, paralysed or stunned; difficulty from blindness and
   crippled legs only). A successful dodge turns the hit into a miss; a critical
   dodge makes it a critical miss with chance 0/10/50/100 % by dodge training
   (resolution (0x4B3170)).
9. **Fate.** A PC attacker with an armed critical-hit fate always hits
   critically; when the attacker is not a PC, a PC target with an armed
   critical-miss fate makes the attack a critical miss (resolution (0x4B3170), fate (0x4F5270)).
10. A critical miss turns the attack on the attacker (crit miss (0x4B6410)):
    it always strikes itself with a normal damage roll, and with chance 50 %
    the self-hit is also critical, adding chances to destroy or explode the
    weapon and lose ammunition plus a critical-hit roll on itself (anomaly
    A7). The effect tables belong to FR-COMBAT-009.
11. Ranged attacks continue as a projectile; melee applies damage now
    (melee (0x4B3770), ranged (0x4B39B0)).

Backstab (eligibility (0x45F9D0), context (0x4B2210)): a trained backstabber
behind a target that is not facing it, with a dagger (an expert also with a
sword, axe or two-handed sword). It is a true backstab when the target can
neither see nor hear the attacker; then the critical chance gains
`2 × backstab level − target level` (+20 for a master).

### Damage (FR-COMBAT-008)

- **C18** Rolls (calc (0x4B6680), weapon damage (0x465F70)): per damage type,
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
- **C19** Application (damage (0x4B4390)): the target's taking-damage script may
  veto; destroyed or invulnerable targets and walls take nothing; a
  nigh-invulnerable critter takes nothing from a non-uber weapon; a stoned
  critter's rolled damage is zeroed, though critical effects and full or death
  damage still apply. Then critical effects, then resistance
  (resistance (0x4B6860)): each type loses `resistance %`, fatigue against
  `¾` of normal resistance, unless ignored by flag. Resistance = base + worn
  armour + armour magic + `5 × (CN − 4)` for poison when positive + effects,
  clamped 0..95 except for monstrous NPCs (object resistance (0x43D6D0)).
  There is no separate armour-subtraction step: armour acts through AC (hit)
  and resistance (damage).
- **C20** HP damage is `normal + electrical + fire` (the full current HP for a
  death or full-damage flag). A PC attacker's HP damage is ×1.5 on easy and
  ×0.75 on hard. A critical hit multiplies HP damage by 3 when
  `d100 ≤ chance + 10`, else by 2 when a new `d100 ≤ chance + 30`, else by
  1.5 when a third `d100 ≤ chance + 60`; these rolls are skipped when an NPC
  hits a PC, and ×1.5 is also the fallback when the critical produced no
  other effect at all (crit hit (0x4B5F40)). A critical can therefore carry
  no multiplier. Fatigue damage
  is added unless the target is fatigue-immune; poison raises the poison level.
  Fire damage or a damage-armour weapon also wears the armour at the hit
  location by 10 HP (armour slot (0x4B54B0)).
- **C21** Side effects (vitals (0x4B4390)): any attacker drains
  `worth × damage% / 100` from the NPC's experience pool, and only a PC
  attacker is paid it; a PC follower dropping below 20 %
  HP triggers its near-death reaction; a melee hit wears the weapon by the
  target's material, or hurts an unarmed attacker but never kills it
  (wear (0x4B5580)). Portals and containers subtract a material reduction (stone,
  brick, metal, force 10; wood 5) and are busted, triggering any trap, at 0 HP
  (damage (0x4B4390)).

### Fatigue, knockout and death (FR-COMBAT-010)

- **C22** Dead is `current HP ≤ 0`; unconscious is `current fatigue ≤ 0` for a
  non-undead critter; active excludes both and stunned, paralysed, stoned
  (states (0x45D790, 0x45D800, 0x45D870, 0x45D8D0)). The fatigue and HP pools are independent: no damage
  converts between them.
- **C23** Going unconscious (a non-zero fatigue-damage write that brings
  current fatigue to ≤ 0, i.e. damage reaching the maximum) demaintains the
  critter's spells, notifies its items, plays the knockdown and ends its
  subturn (fatigue set (0x45D5A0)).
- **C24** Critical outcomes (crit hit (0x4B5F40)): stun when
  `d100 ≤ chance + 5` (+10 crushing or electrical, +10 head without helmet)
  and the target fails a CN check at −5; for an NPC that is not fatigue-immune
  a second roll (`d100 < chance + 5 …`, strict) with another failed CN check
  turns it into a **knockout**, which sets fatigue damage to
  `max fatigue + 10..20` (damage (0x4B4390)). Undead and amorphous targets are
  never stunned or knocked out, and a PC is never knocked out by a critical.
  Bipeds can be knocked down (`chance + 5`, +5 crushing, +10 leg). `chance`
  is the weapon's magic critical-effect value (0 unarmed) plus effects, +10
  when aimed; with a weapon whose magic was cancelled it is undefined
  (anomaly A8).
- **C25** Recovery (fatigue event (0x45E610, 0x45E820), time (0x45A7F0, 0x45A820, 0x45A840, 0x45A870, 0x45A890, 0x45A8B0, 0x45A8D0, 0x45A8E0, 0x45A900, 0x45A920, 0x45A950)): any non-zero
  fatigue damage schedules one recovery event per critter 80 000 ms of game
  time ahead. It removes `HealRate × max(1, elapsed seconds/80)` fatigue damage;
  when fatigue becomes positive again the critter gets up. Resting heals
  `HealRate × hours` HP and three times that fatigue (rest (0x45E910)). In a round,
  getting up is gated by the round (anomaly A3).
- **C26** Death (kill (0x45D900, 0x45DA20)): the dying script may veto; otherwise combat
  mode ends, the death time is stored, an NPC records its killer as combat
  focus, the PC killer (or the killer's PC leader) gains 20 % of the NPC's
  experience worth, alignment and the kill log are updated, followers are told,
  the NPC leaves its leader's follower list but keeps the leader handle, the
  dying animation plays, the body is scheduled to decay
  after one game day unless flagged otherwise, and HP damage is pinned at
  32 000. The dying animation is the plain fall for death-flag damage or
  ≤ 20 damage, otherwise a head, leg or body variant (vitals (0x4B4390)).
- **C27** When the dying animation of the local PC ends, the game waits 5 s,
  shows the death slide, resets and returns to the main menu
  (dying (0x42FF40), end death (0x57CDC0), reset (0x57D410)).
- **C28** Healing (heal (0x4B58C0)) lowers HP, fatigue and poison damage; a full
  heal also clears injuries. Resurrection of a dead critter is gated by its
  resurrect script and the unrevivifiable/unresurrectable flags, resets
  fatigue damage to 10 and rejoins its leader when the follow check passes.
- **C29** The `non-lethal combat` flag stops automatic attack repetition (C5)
  and makes the AI reject an unconscious target (AI target (0x4AB990));
  no reviewed damage branch converts lethal damage to knockout
  (auto attack (0x429760), damage (0x4B4390)).

### Persistence (FR-COMBAT-001)

- **C30** A save stores the turn-based setting, the active flag and turn
  counter; when a round is active also the AP left, the current participant,
  the required AP and two further actor handles. Loading
  restarts the round (rebuilding the list), then restores AP and the current
  participant; required AP are reset to 0 (save/load (0x4B1EA0, 0x4B2020)).

## Anomalies (recorded, not adopted)

- **A1** `sort_combat_list` tests "not a PC" twice, so only PCs move to the
  tail and PC followers keep their scan position (sort (0x4B7EB0)).
- **A2** `skill_invocation_run` adds the invocation modifier to a difficulty
  that already starts from the same modifier, so the bed modifier counts
  twice (−60) (run (0x4C7160), difficulty (0x4C8430)).
- **A3** Getting up in a round is refused on the critter's own subturn and
  free on any other subturn; the 5 AP charge is unreachable
  (get up (0x42E590)).
- **A4** The every-fifth-run-step fatigue charge in combat mode (period ×4 for
  fatigue-limiting, then ÷4, at least 1, for fatigue-draining critters) writes back
  the unchanged fatigue damage, so running never costs fatigue; and because
  the write returns the stored value, a critter with no fatigue damage fails
  that step without paying AP. What the failed step does next was not traced
  (step (0x430FC0), fatigue set (0x45D5A0)).
- **A5** A crippled-arm critical drops the **weapon** handle when only the
  shield qualifies; partial healing of crippled legs clears blindness instead
  (damage (0x4B4390), heal (0x4B58C0)).
- **A6** `object_get_ac` applies AC-adjustment effects to the whole AC and
  then adds the effective `AcAdjustment` stat again (AC (0x43D880)).
- **A7** On a critical self-hit the critical-hit effects are rolled twice: once
  inside the critical-miss branch and again when the damage is applied
  (crit miss (0x4B6410), damage (0x4B4390)).
- **A8** The critical-hit `chance` is never assigned when the weapon's magic
  was cancelled (step 4), while the critical-miss path sets it to 0
  (crit hit (0x4B5F40), crit miss (0x4B6410)).
- **A9** When the skip loop of `combat_turn_based_next_subturn` runs past the
  last entry, the new round already starts its first subturn and the function
  then starts it a second time, so an NPC acting first runs its heartbeat
  script and AI twice (next subturn (0x4B7630, 0x4B7690, 0x4B7740), round (0x4B73A0, 0x4B7580)).

- **A10** (found by ARC-COMBAT-001) `combat_calc_dmg` keeps the backstab
  flags after a critical miss retargets the attack to the attacker, so the
  self-hit gets the backstab damage bonus (calc (0x4B6680), crit miss (0x4B6410)).

- **A11** (found by ARC-COMBAT-003) The stalled-actor check of the
  animations-complete step records a new actor without its action points, so
  the new actor's second check compares its action points with the previous
  actor's last value (0 after a round start) and may force it to stop
  although it spent (anim completion (0x4B7080, 0x4B7150)).
- **A12** (found by ARC-COMBAT-003) An overexertion that knocks the PC out
  ends its subturn inside the damage step; the charge then sets the action
  points, by now the next participant's, to 0 (consume (0x4B7C90, 0x4B7CD0),
  fatigue set (0x45D5A0)).

The decisions on A1–A12 (owner decision D2) are recorded in the [NPC combat
AI plan](npc-combat-ai-plan.md#anomaly-decisions); A11 and A12 were checked in
the original by ARC-COMBAT-003 ([ledger](original-verification-ledger.md#turn-based-round-arc-combat-003)).

## Original executable check (ARC-COMBAT-001)

On the owner's request the ten anomalies were checked against the original
game with what became the project's
[original executable check](original-executable-check.md): the retail **1.0.7.4** `Arcanum.exe` of the owner's local
GOG install (SHA-256 `9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52`;
GOG adds a `ddraw.dll` import section, and no checked function branches into it) was disassembled read-only with
`llvm-objdump` at the addresses given for each function, which match the
binary's layout. No bytes, listings or game content are stored in the
repository; the table records only the observed behaviour.

| Anomaly | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| A1 | `sort_combat_list` (0x4B7EB0) | The second test reads the **type of the PC leader** that `critter_pc_leader_get` returned: PCs and the PCs' followers move to the tail. | Reconstruction slip |
| A2 | `skill_invocation_run` (0x4C7160), `skill_invocation_difficulty` (0x4C8430) | The difficulty starts from the invocation modifier and the run adds it again: the bed's −30 counts −60. | In the original |
| A3 | `AGbeginGetUpAnim` (0x42E590) | In a round: refused unless it is the critter's own turn, then refused unless 5 AP are paid. | Reconstruction slip |
| A4 | run step AP (0x430FC0) | In combat mode the charge period is every 5th run step, ×4 when fatigue-limiting, then ÷4 (at least 1) when fatigue-draining; each charge writes **fatigue damage + 1**. | Reconstruction slip |
| A5 | `combat_dmg` (0x4B4390), `combat_heal` (0x4B58C0) | The crippled-arm branch reuses one handle, so the **shield** is dropped; partial healing of a leg injury clears **crippled legs**. | Reconstruction slips |
| A6 | `object_get_ac` (0x43D880) | AC-adjustment effects are applied to the whole AC, then the AcAdjustment stat is added. | In the original |
| A7 | `combat_process_crit_miss` (0x4B6410), `combat_dmg` | The critical-miss branch calls the critical-hit procedure and `combat_dmg` calls it again. | In the original |
| A8 | `combat_process_crit_hit` (0x4B5F40) | With the weapon's magic cancelled the critical effect chance is **0** before effects. | Reconstruction slip |
| A9 | `combat_turn_based_next_subturn` (0x4B7690) | After the skip loop ends the round, the begin-turn path starts the first subturn and the function starts it again. | In the original |
| A10 | `combat_process_crit_miss`, `combat_calc_dmg` (0x4B6680) | The backstab flags survive the retarget to the attacker and the damage step adds the backstab bonus. | In the original |

## Open questions

- **O1** Retail parity of every constant and formula above (NOT_VERIFIED; only
  the anomalies A1–A10 were checked in the original executable).
  An original-runtime oracle would need scripted fights with a fixed seed.
- **O2** The scan order of `object_list_rect`, which is the actual NPC order in
  a round, was not read. *Answered by ARC-COMBAT-003:* sector rows from the
  top, sectors from the left, and inside a sector the critter that entered it
  last first ([ledger TB13](original-verification-ledger.md#turn-based-round-arc-combat-003)).
- **O3** The Speed-scaled animation rate (`sub_437990`), so the real-time
  cadence as a function of Speed, was not read. *Answered by ARC-COMBAT-004:* the attack rate per weapon slot and
  Speed, the frame pause and the goal timeline, read in the original
  ([ledger RT8–RT11](original-verification-ledger.md#real-time-combat-arc-combat-004)).
- **O4** The line-of-fire test `sub_4ADE00` (the 26 threshold and the cover
  rating) belongs to FR-COMBAT-007 and was not read.
- **O5** How AI chooses attacks, flees or ends its subturn early
  (`ai_process`), and what an NPC does in real time between attacks, belong to
  the AI section.
- **O6** Weapon, armour and critter prototype fields (damage ranges, speed
  factor, range, to-hit, minimum strength, crit charts, AC, resistances) were
  not read from data; the Crash Site combatants are unknown. *Update:* the
  reader is [ARC-DATA-021](../formats/combat-fields.md), which lists the
  Crash Site exterior and cave combatants and their gear.
- **O7** Whether the recovery event re-arms itself while it runs (duplicate
  check against the event being processed) was not traced.

## Steamwoven inputs that already exist

[`CharacterStatRules`](../../src/Arcanum.Simulation/Character/CharacterStatRules.cs)
already derives `Speed`, `DamageBonus`, `AcAdjustment` and `HealRate` with the
same source formulas ([contract](../world/character-stats-runtime.md)); the
session clock provides game time for the 80-second recovery and the
one-second round advance; exact movement and the click-route planner provide
steps. Skills, training, resistances, AC, items and equipment do not exist yet.
*Update:* slice 3 is the [turn-based round](../world/turn-based-round-runtime.md)
(ARC-COMBAT-003, snapshot schema 11); slice 2 is the [attack resolver](../world/attack-resolver-runtime.md)
(ARC-COMBAT-001) and slice 1 the [life state](../world/life-state-runtime.md)
(ARC-COMBAT-002: hit points, fatigue, poison, injuries, knockout, death,
healing and the recovery steps in the session, snapshot schema 8).

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
   table left as an injected FR-COMBAT-009 hook. Anomalies A1–A10 are settled
   by an explicit owner decision, not copied: owner decision D2 of 2026-10-16
   ([NPC combat AI plan](npc-combat-ai-plan.md#owner-decisions-2026-10-16))
   fixes a confirmed slip in the default and logs it there.
3. **Turn-based round**: participants, skip rules, AP grant and costs,
   overexertion, end of round +1 s and save/restore of round state as a new
   snapshot schema version (C6–C14, C30). Initiative stays "scan order, PCs
   last" unless the owner chooses otherwise after O2. *Implemented by
   ARC-COMBAT-003* ([turn-based round runtime](../world/turn-based-round-runtime.md),
   snapshot schema 11).
4. **Real-time cadence and combat mode**: the per-critter mode flag, the exit
   rule (C1), attack timing from animation rate and weapon speed (C4) and the
   auto-attack setting (C5).
5. **Data**: read weapon, armour and critter combat fields from prototypes
   (O6) on the Mac, then a first Crash Site fight in the AREA viewer.

## Method

Reading of the executable's functions only, through the call chains named in
each row: the combat functions (0x4B1D50–0x4B83E0) in full, and the called
parts of the skill, item, object, critter, animation, AI, fate, random and
time-event functions and the UI callers. Evidence level F0 unless a ledger row
is cited; earlier provenance is in git history. Names in this document are
descriptive.
 The open questions are the parts this reading
could not settle.


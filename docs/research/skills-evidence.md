# Skills: ranks, training, checks and technical aptitude

Status: **Research**, work item ARC-RES-044 (2026-10-16), item 13 of the
owner's top-20 task list (project planning, outside the repository). This document fixes how the game stores the
sixteen skills, how ranks are bought and capped, how training tiers are gained,
how a skill check is rolled, what each skill does where it is used, and how
technical ranks feed magick/tech aptitude. It ends with anomalies, policy
proposals and the `SKILLS` implementation slices. It builds on the
[combat-domain load map](combat-domain-load-map.md) (ARC-RES-040: what is
loaded and saved) and the [combat core research](combat-core-evidence.md)
(ARC-RES-030: the attack itself). It changes no production API, reader, viewer,
session schema or dialog port.

Requirements: [FR-CHAR-016](../requirements/characters-skills.md#fr-char-016)
(ranks), [FR-CHAR-017](../requirements/characters-skills.md#fr-char-017)
(technical ranks), [FR-CHAR-018](../requirements/characters-skills.md#fr-char-018)
(training), [FR-CHAR-019](../requirements/characters-skills.md#fr-char-019)
(skill check), [FR-CHAR-020](../requirements/characters-skills.md#fr-char-020)
(aptitude); the weapon-skill inputs of
[FR-COMBAT-001](../requirements/combat-magic-tech.md#fr-combat-001).

## Vocabulary and boundary

- **Source fact**: an earlier source reading (provenance in git history),
  cited by the executable address of its function; level F0 (not checked in
  the executable) unless a row of the
  [original verification ledger](original-verification-ledger.md) is cited.
  Unnamed functions keep decompiler names (`sub_…`); a reading of their intent
  is marked **inference**.
- **Anomaly**: a branch that looks like a slip, numbered
  SK1..SK8 in [section 8](#8-anomalies-found-by-this-reading). Under owner
  decision [D2](npc-combat-ai-plan.md#owner-decisions-2026-10-16) the slice
  that implements the branch fixes a confirmed slip in the default behaviour
  and logs it; nothing is adopted silently.
- **Project policy**: every item of [section 9](#9-consequences-for-steamwoven-project-policy-proposals)
  is a **proposal**.
- **Level** is the effective skill level 0..20 that every rule reads;
  **points** are the stored ranks; **training** is the tier None, Apprentice,
  Expert or Master.

**Original check (ARC-SKILLS-001, 2026-10-20).** The state facts of sections 1 to 4 are now read in the executable by
two readers and stand at F2 or F3: storage, levels, caps, key stats, training get and set with its minima, points
setters, cost and refund, the next point's key stat, the stat-lowering guard and its caller, the tech points side effect,
the skill effect collections, the Educator update, the skill module (names, the "follower skills" setting, the saved
gambling roll), the new-character kit and the aptitude formula ([ledger](ledger/skills.md#skill-rules-arc-skills-001)
FA7–FA9, AL5, SR1–SR14; since ARC-SKILLS-004 also the dialog skill conditions and the `tr` and `et` actions, [DS1–DS5](ledger/skills.md#dialog-skill-gates-arc-skills-004)). They agree with the reading below. The editor's + and − buttons, its floor, snapshot and
hints (0x57ACD0, 0x57AEB0, 0x55D3A0, 0x55D6F0, 0x5597C0, 0x55AE70) stand at F2 since ARC-WORLD-052
([ledger CP1–CP12](ledger/character.md#character-point-pool-arc-world-052)), with one correction to section 2: the
floor does not hold through a − click whose level an effect pins (CP6). Still F0, apart from the − loop (SK2) and the training
dispatch (SK3) already read by ARC-RES-048: the sheet's skill page (0x55BD10; task S6), the training sources (0x418CA0,
0x418DE0, 0x418F30, 0x411940; S16), and everything of sections 5 and 6
(the check and the uses; S3 and later).

No original file, probe or game data was read for the research work item ARC-RES-044 itself. Original
executable parity remains **NOT_VERIFIED** except where a ledger row is cited (above and in section 8). Values that depend on the shipped
data (NPC skill points, effects on skills, lock and trap difficulties, dialog
use of skill conditions) are listed for the census in
[section 11](#11-census-items-for-the-mac).

## Answer

1. **Sixteen skills, two families, one byte each.** Twelve basic skills (bow,
   dodge, melee, throwing, backstab, pick pocket, prowling, spot trap,
   gambling, haggle, heal, persuasion) and four technical skills (repair,
   firearms, pick locks, disarm traps). Each critter stores one 32-bit value
   per skill: bits 0..5 are points, bits 6..7 the training tier
   (points (0x4C5FA0, 0x4C6000), training (0x4C60C0, 0x4C6170)).
2. **A point is four levels, and the key stat caps the level.** Level =
   4 × points, then effects, then clamp 0..20, then the cap of the key stat:
   stat 1..5 → 3, 6..8 → 7, 9..11 → 11, 12..14 → 15, 15..17 → 19,
   18..20 → 20 (level (0x4C5E50, 0x4C5EB0, 0x4C5F70)). Buying point
   *n* needs `4n ≤ cap`, so the key stat must be at least 6, 9, 12, 15 and 18
   for points 1..5. Every point costs one character point and refunds one
   (cost (0x4C64B0, 0x4C64C0, 0x4C64D0, 0x4C64E0)).
3. **Monsters are special only in melee.** A mechanical, monster, animal or
   undead critter has base melee level 20 regardless of points, and its melee
   training is derived from melee level and character level (≥1 and ≥10 →
   Apprentice, ≥9 and ≥20 → Expert, ≥18 and ≥30 → Master)
   (level (0x4C5E50, 0x4C5EB0, 0x4C5F70), training (0x4C60C0, 0x4C6170), monstrous (0x45F730)).
4. **Training is a ladder with level gates and per-skill perks.** A tier can
   only rise one step at a time and needs level 0, 1, 9 or 18; a lower or equal
   tier skips the one-step rule but still needs its minimum level, so None
   always succeeds (setter (0x4C60C0, 0x4C6170)). Apprentice training is sold by
   dialog trainers for 100 gold; any tier can come from the dialog action `tr`,
   `Rules\map.patch` and the Educator background ([section 3](#3-training)). The tiers mostly switch rules on and off
   rather than add numbers ([section 6](#6-what-each-skill-does)).
5. **A technical point is also a tech point.** Changing technical points adds
   the difference to the stored `TECH_POINTS` stat; aptitude is
   `(50 × magick points − 55 × tech points) / 10` plus the sector adjustment,
   clamped −100..100 (tech points (0x4C66E0), aptitude (0x4B0740)).
   Steamwoven's [character stats owner](../world/character-stats-runtime.md)
   already computes the aptitude; it lacks the skill side.
6. **One roll for every active skill.** `success = difficulty + d100 ≤
   effectiveness` (or a forced success), then a second d100 for a critical
   success or failure, then fate for the PC, then the skill's own handler
   (run (0x4C7160)). Effectiveness is a per-skill formula of the level, an
   opposed table for four skills, +10 for extraordinary intelligence, the game
   difficulty for the PC and a 95 % cap for every skill but bow, melee,
   throwing, firearms and prowling ([SC1](ledger/skills.md#skill-check-arc-skills-003))
   (effectiveness (0x4C62E0), opposed (0x4C6410), tech (0x4C69F0)).
7. **Persuasion and haggle in dialog are thresholds, not rolls.** Dialog
   conditions `ps`, `ha`, `sk` and `tr` compare the PC's effective level or
   training with a number (conditions (0x4150D0)). Persuasion otherwise acts
   through training: +1 follower at Expert, always accepted as leader at
   Master, a longer "wait here" at Apprentice with extraordinary charisma.
8. **Prowling is not rolled either.** Concealment lowers the observer's
   perception by the prowler's margin, in percent, in the AI's sight and
   hearing checks (sight (0x4AF260), hear (0x4AF470)).
9. **Skill state lives in the critter; only the gambling roll is global.**
   Points and training are object fields saved with the object; the Skill
   module saves only the pre-drawn next gambling roll, which defeats
   save-and-retry (load/save (0x4C5BD0, 0x4C5D50, 0x4C5DB0, 0x4C5DC0, 0x4C5DE0)).
10. **Seven new anomalies (SK1, SK2, SK4..SK8)** and SK3, which confirms
    an existing note; the worst is SK1, a script command
    that reads a skill and then falls through into casting a spell.

## 1. The sixteen skills

Key stat (for basic skills table 0x5B6F04, ledger FA7), effectiveness at level L
(basic (0x4C62E0), tech (0x4C69F0)), whether it is capped at 95 %, and how
the game reaches it. "Opposed" uses the table of
[section 5.2](#52-effectiveness).

| Skill | Key stat | Effectiveness | ≤ 95 | Reached by |
| --- | --- | --- | --- | --- |
| Bow | DX | 5L + 25 | no | attack with an arrow weapon |
| Dodge | DX | 5L in the earlier reading; **2L** in the original ([ARC-RES-048](original-verification-ledger.md)) | yes | defender roll after every hit |
| Melee | DX | 5L + 25 | no | attack with a melee weapon or bare hands |
| Throwing | DX | 7L + 25 | no | attack with a non-weapon item or a boomerang |
| Backstab | DX | none (0) | yes | modifies a melee attack, never rolled |
| Pick pocket | DX | opposed: target PE | yes | skill bar, steal/plant inventory |
| Prowling | PE | opposed: observer PE (dead formula 5L + 25, 0 at L 0) | no | skill bar toggle, AI sight and hearing |
| Spot trap | PE | 4L | yes | every path step and every use of a trapped object |
| Gambling | IN | opposed: target gambling | yes | trade window gamble |
| Haggle | WP | 4L + 10 | yes | every price; dialog `ha` threshold |
| Heal | IN | 5L | yes | bandages, NPC service, NPC AI |
| Persuasion | CH | opposed: target IN | yes | dialog `ps` threshold; training perks |
| Repair | IN | 4L | yes | skill bar, NPC repair service |
| Firearms | PE | 5L + 25, −20 against Magnetic Inversion | no | attack with a technological ranged weapon |
| Pick locks | DX | 6L | yes | lockpicks, followers |
| Disarm traps | PE | 4L + 20, 0 at L 0 | yes | skill bar |

Combat skills (bow, dodge, melee, throwing, firearms) also use the combat
critical divisors of [section 5.4](#54-criticals-fate-and-forced-success).
Each skill carries fixed flags that select which difficulty terms apply
(awareness, hit location, distance, light, eyes, arms, legs).
The prowling formula `5L + 25` exists in the code but
is unreachable, because prowling is opposed (effectiveness (0x4C62E0)).

Names, descriptions and the four training names come from `mes\skill.mes`
entries 0..35, loaded once at init from the base archives
(init (0x4C5BD0, 0x4C5D50, 0x4C5DB0, 0x4C5DC0, 0x4C5DE0); [load map](combat-domain-load-map.md#3-data-file-catalogue)).

## 2. Ranks

**Storage and level** (basic (0x4C5E50, 0x4C5EB0, 0x4C5F70), tech (0x4C6580, 0x4C65E0, 0x4C6680)). Points are
read as the low six bits, so the field can hold 0..63, but a purchase can only
reach five (4 × 5 = 20). Effects adjust the level in level units through the
effect system's basic-skill and tech-skill collections
(effects (0x4EA960, 0x4EA990)); the 0..20 clamp comes before the key-stat cap, so an
effect cannot lift a skill above what the key stat allows.

**Purchase and refund** (setters (0x4C5FA0, 0x4C6000), tech setter (0x4C66E0),
UI (0x57ACD0, 0x57AEB0)). The editor's + button checks one unspent point, sets
points + 1 (refused when `4 × points` exceeds the cap), then spends the point;
the − button does the reverse and refunds. The character editor refuses a −
that would go below the value accepted when the editor opened
(floor (0x55D3A0, 0x55D6F0), snapshot (0x5597C0)). A refused + reports
"skill at maximum" when the level is already 20 and "stat too low" otherwise;
the stat hint shows the key stat needed for the next point
(hint (0x55AE70)). The basic − path loops until the visible level
changes; see SK2. The floor is tested only before the loop, so when an effect
holds the level the click passes it (ledger CP6, an original bug; Steamwoven
stops at the floor in both modes, [character point pool](../world/character-point-pool.md)).

**Auto-level** (auto level (0x4A76B0, 0x4A77A0)). A scheme step "skill N" buys points
until the level reaches N; when a point is refused it raises the key stat by
one instead and tries again, failing when the character points run out or the key stat cannot be raised.
[ARC-RES-032](virgil-level-replay.md) replays these steps for Virgil.

**Lowering a key stat** (guard (0x4C6B50), caller (0x4B0980)).
A stat decrease is refused when any skill keyed on that stat would have a base
level (4 × points, without effects) above the new stat's cap.

**Starting kit** (kit (0x460FF0)). A new character with any pick-locks point
receives crude lockpicks, and with any heal point bandages
([ARC-RES-027](starting-hero-evidence.md) records the kit).

## 3. Training

**Rules** (basic (0x4C60C0, 0x4C6170), tech (0x4C67F0, 0x4C6850),
minimum (0x4C69C0)). Minimum level: None 0, Apprentice 1, Expert 9,
Master 18. Raising skips no tier; a lower or equal tier skips the one-step
rule but still needs its minimum level (a Master whose level fell to 5 cannot
be set to Expert; None always succeeds). The check reads the effective level,
effects included. Every
successful set runs the Educator background update.

**Sources.**

| Source | What it grants | Gate |
| --- | --- | --- |
| Dialog option `t:` with a skill list | Apprentice for 100 gold (offer, price, perform (0x418CA0, 0x418DE0, 0x418F30)) | already trained → refusal 4000; not allowed by the setter (level < 1) → refusal 5000; probed by setting Apprentice and resetting None |
| Dialog action `tr skill tier` | any tier through the setter (action (0x415BA0)) | setter rules |
| Dialog action `et skill line` | none; continues to the line only when level ≥ 9 and training ≥ Apprentice, otherwise a "won't teach" reply (action (0x415BA0)) | the expert-teacher gate |
| `Rules\map.patch` key `training` | any tier on a named object through the setter (patch (0x411940)) | setter rules; whether the shipped patch uses it is a census item |
| Educator background | followers' tiers raised to one below the PC's, for skills where the PC is Expert or Master (educator (0x4C2950)) | runs on every training change and every level-up of that PC (level-up (0x4A69C0)); follower setters may refuse |
| Monstrous melee | derived, not stored (answer 3) | — |

The character editor shows training but has no control to change it
(display (0x55BD10)). The earlier reading had `skill_ui_set_training` swap the
basic and tech setters; the original routes them correctly except skill 12
(repair), which goes to the basic setter and is refused, and this is harmless
in single player (ledger SK3: called only from the multiplayer handler).

## 4. Technical skills and aptitude

Technical ranks follow the basic rules (key stats IN, PE, DX, PE). A changed
point count adds the difference to the stored tech points
(tech setter (0x4C66E0)); tech degrees add their degree cost
(tech degree (0x4AFEC0)) and spell levels add to magick points
(spell (0x4B1790)). Changing either stored total re-applies the effects of
wielded items (side effect (0x4B0980)). Aptitude
(formula (0x4B0740)) is `(50 × MP − 55 × TP) / 10` plus the sector's
adjustment, clamped −100..100; the Day, Night, Sky and Nature Mage backgrounds
move MP by ±4 by time of day, indoor or outdoor, or natural ground
(mage backgrounds (0x4B0490)). Consumers: technological items failing in
magickal hands and magick cancelling technology in combat
([combat core](combat-core-evidence.md)), the spot-trap shield bonus of
[section 6](#spot-trap-and-disarm-traps), and item effects scaled by aptitude.
The sector adjustment table and the magick side belong to the magic research.

## 5. The skill check

### 5.1 Invocation

An invocation carries source, target, target location, item, skill, a
modifier, a hit location and flags (aim, backstab, no magic adjustment,
forced, hearing or seeing check, steal or plant mode)
(init (0x4C6F90, 0x4C6FD0, 0x4C7010, 0x4C7050, 0x4C7090, 0x4C7120)). Starters:

- **Animation goals.** `skill_use` and the steal and plant calls queue a
  "use skill on" goal that walks to the target; pick locks queues a variant
  of it whose working time is halved for an apprentice (goal (0x4350F0),
  pick locks (0x436220)). The goal charges 4 action points when it fires
  (charge (0x42A2A0)); the timed pick-lock goal does not (its check node 0x42A4E0 has no charge), and it
  belongs to the AI, scripts and dialog services, not to the player (ledger PL3). Using bandages or lockpicks from the inventory
  queues "use item with skill", charging 4 points at use
  (item use (0x462CC0), goal (0x4352C0), run (0x428E10)).
- **Immediate.** Repair and disarm from the skill bar run at once, without an
  animation goal; disarm charges 4 points, repair charges none
  (skill bar (0x57A320, 0x57A5E0)). Gambling runs from the trade window
  (gamble (0x579840, 0x579B60)); trap spotting from movement and use (section 6).
- **Skill bar.** Four slots: prowling (toggle concealment on self and all followers,
  conceal (0x45EE30)), pick
  pocket, disarm traps, repair (preprocess (0x57A0A0)).
- **Others.** The AI (`ai_action_perform_skill`, AI (0x4AC350)), dialog
  skill services (service (0x419B50, 0x419C30, 0x419CB0)) and script command "use item with
  skill" with a modifier (script (0x447220)).

**Follower substitution** (best follower (0x4C8FA0, 0x4C8FF0, 0x4C9050)). When the
"follower skills" option is on (default on, setting (0x4C5BD0, 0x4C5D50, 0x4C5DB0, 0x4C5DC0, 0x4C5DE0)), disarm from
the skill bar and lockpicks from the inventory let the follower with the best
`effectiveness − difficulty` act, with a "let me handle it" line
(lockpicks (0x571CB0), disarm (0x57A620)). Holding Ctrl
skips it.

### 5.2 Effectiveness

Per-skill formulas are in [section 1](#1-the-sixteen-skills). Then
(basic (0x4C62E0), tech (0x4C69F0)):

1. +10 when the source's effective intelligence is extraordinary (≥ 20);
2. for the local PC only, +50 % on easy and −25 % on hard (integer halves and
   quarters);
3. the 95 % cap for the skills marked in section 1;
4. never below 0.

**Opposed table** (vs (0x4C6410)). With no target the result is 100. Else
Δ = own level − the opponent's value (perception, intelligence or the same
skill's level); Δ ≥ 10 → 100, Δ ≤ −10 → 0, otherwise a table indexed by Δ, its
values divided by four: −9…+9 → 13, 16, 19, 22, 26, 30, 34, 38, 42, 47, 52,
56, 61, 65, 68, 72, 75, 78, 81.

### 5.3 Difficulty

**Original check (ARC-SKILLS-003):** sections 5.2–5.4 were read in the retail 1.0.7.4 executable by two readers, rows
[SC1–SC17](ledger/skills.md#skill-check-arc-skills-003). They agree with this reading except that a small own
light keeps three quarters of the darkness penalty (SC9), and they complete it: heal without a healing item is impossible,
and each worn armour's silent-move adjustment is halved on its own (SC11).

Summed, in this order (difficulty (0x4C8430)):

1. the invocation modifier;
2. with a weapon item: minus its to-hit bonus (plus the magic adjustment unless
   disabled), plus `5 × (min ST − ST)` when too weak;
3. **awareness** (bow, melee, throwing, firearms, pick pocket, prowling)
   against a critter: −50 paralysed, −50 unconscious, −30 falling or stunned,
   −50 sleeping, else −30 when it is not in combat mode and can neither see
   nor hear the source (this last term not for prowling);
4. **hit location** (bow, melee, throwing, firearms): an aimed attack adds the
   location penalty (two thirds of it for an expert in firearms); the target's
   AC share `effectiveness × (AC/2) / 100` unless this is a true backstab;
5. **distance** (bow, throwing, firearms): out of range or a blocking object →
   impossible; +5 per cell beyond PE/2 except for a master of the skill;
   a positive line-of-fire value is added as cover (inference: the helper's
   purpose is unclear and only the flag name says cover);
6. **light** (not for an undead source) for bow, melee, throwing, firearms,
   spot trap, pick locks, disarm traps and seeing checks: up to +30 in full
   darkness, divided by the source's own light (4, 3, 2 or ×3/4 by size; SC9),
   replaced by 30 − penalty when the source has dark sight (for a seeing
   check: unless the observer has dark sight); skipped by an expert in melee or pick locks; spot,
   disarm and seeing checks apply it only while untrained;
7. **injuries**: blinded +30 for skills that use the eyes; both arms crippled
   +50, one arm +20 for skills that use the arms; both legs +30 for skills that
   use the legs (dodge, prowling);
8. **per skill**: pick pocket +30..1000 for worn slots (helmet 50, rings and
   medallion 30, weapon and shield 70, armour, gauntlets and boots 1000) and
   `5 × width × height` for size, halved for an expert; prowling (section 6);
   heal minus the healing item's usage bonus (impossible without one, SC11); pick locks minus the lockpick
   bonus plus the lock's difficulty (halved for a master), impossible on
   anything but a portal or container; disarm plus the trap's difficulty.

"Impossible" is +1,000,000. A2 of the [combat core research](combat-core-evidence.md#anomalies-recorded-not-adopted)
applies to every skill: the run adds the modifier to a difficulty that already
starts from it, so every non-zero modifier counts twice (the bed −30, the
spot-trap shield bonus, a script's modifier).

### 5.4 Criticals, fate and forced success

(run (0x4C7160), hit (0x4C81A0), miss (0x4C82E0))

- **Critical success** on a success: d100 ≤ effectiveness / 20 for combat
  skills, / 2 for the rest; aimed: + penalty / −5; the weapon's magic critical
  chance; a backstab adds 2 × backstab level − target level, +20 for a
  master; effects adjust it.
- **Critical failure** on a failure: d100 ≤ (100 − effectiveness) / 7 for
  combat skills, / 2 for the rest; a damaged item adds its damaged share by the
  same divisor; the weapon's magic critical-miss chance; effects; at least 2.
  A master of melee never fails critically.
- **Fate** (PC only): a fate point for gambling, heal, pick pocket, repair,
  pick locks or disarm turns the result into a critical success; a fated repair
  restores full HP.
- **Forced** success skips the roll comparison; the trade-window repair
  service uses it (repair (0x4C8E60)). Dialog skill services ask for it,
  but the goal they queue drops the flag, so they roll (SK8).
- **Gambling** uses the saved pre-drawn roll instead of a fresh d100.

## 6. What each skill does

### Weapon skills, dodge and backstab (inputs of the attack resolver)

The weapon decides the skill (weapon skill (0x465D80)): no weapon →
melee; a non-weapon item or a boomerang → throwing; a technological weapon
(negative complexity) with range ≥ 3 using bullets, fuel or charges →
firearms; arrows → bow; else melee. The attack roll is the generic check
(attack (0x4B3170)). What the [combat core research](combat-core-evidence.md)
records as formulas, the skills side owns as inputs:

| Rule | Source |
| --- | --- |
| Apprentice in the weapon's skill: weapon speed +5 | speed (0x465CF0) |
| Expert melee or pick locks: no light penalty; master melee: no critical miss | difficulty (0x4C8430), run (0x4C7160) |
| Masters of bow, throwing, firearms: no distance penalty; expert firearms: aimed penalty ×2/3 | difficulty (0x4C8430) |
| Expert bow: two projectiles per shot | ranged (0x4B39B0) |
| Throwing: a miss lands up to `(20 − L) / 5 + 1` cells off; expert: range ×1.5 | ranged (0x4B39B0), distance (0x4622A0) |
| Dodge: after a hit the defender rolls dodge (auto-fail when unconscious, stoned, paralysed or stunned); success turns the hit into a miss; a critical dodge makes it a critical miss with chance 0/10/50/100 % by dodge training | dodge (0x4B3170), table |
| Backstab: needs Apprentice, the target not facing the attacker and a dagger (Expert also swords, axes, two-handed swords) | eligibility (0x45F9D0) |
| True backstab (target can neither see nor hear): ignores AC, damage + 5 × backstab level; seen backstab + level; critical bonus as in 5.4 | flags (0x4B2210), damage (0x4B6680) |
| NPC weapon choice scores average damage × effectiveness, non-melee skills at level 0, and any effectiveness of 0, score −1 | choice (0x465170), LM1/LM2 of [ARC-RES-040](combat-domain-load-map.md#8-anomalies-found-by-this-reading) |
| Melee damage range scaling by a skill helper that returns 100 | LM14 |

### Pick locks

On a locked portal or container (handler (0x4C7160)): success unlocks
and failure does not, guards are notified either way, and a critical failure
jams the lock. On an unlocked
one success locks it. Lockpicks lower the difficulty by their usage bonus; the
lock's difficulty is added (difficulty (0x4C8430)). Lockpicks are optional:
a follower without them still tries (lockpicks (0x571CB0)).

**Original check (ARC-SKILLS-006):** read in the retail executable by two readers, rows
[PL1–PL7](ledger/skills.md#pick-locks-arc-skills-006). They add that the case reads only the lock query (not the
jam, hold or always-locked bits), that the witnesses are called only for a locked target, that lockpicks are never used up, and
that the player's only start is the inventory use (4 action points, no animation); PL7 confirms the ledger's SK4.

### Heal

**Original check (ARC-SKILLS-005):** read in the retail executable by two readers, rows
[HL1–HL5](ledger/skills.md#heal-arc-skills-005); they agree with this reading and add that the cure also
applies to a dead target, that nothing refuses a full-health, undead or self target, and that the item use charges 4 action points.

(handler (0x4C7160)) Healing = heal level, ×1.5 when trained, at least 1. A
critical success, or any success by a master, heals the maximum and cures one
injury effect and the first of blinded, both arms, one arm, both legs crippled;
otherwise it heals 1..maximum. An expert turns a critical failure into a plain
failure. A healing item loses one charge per use, five on a critical failure,
and is destroyed at zero. NPCs heal themselves and allies below 90 % HP when out
of danger and holding a healing item (AI heal (0x4A8AA0),
check (0x4AE570)); healers sell it in dialog (service (0x419B50, 0x419C30, 0x419CB0)).

### Repair

(handler (0x4C7160)) On an item with HP damage, or a broken item for a
master: repair = effectiveness % of max HP (full for a fated success), at least
1 when effectiveness > 0 or forced. Every repair costs max HP: 10 %, 5 %, 1 %
for None, Apprentice, Expert; a master loses none except 1 % on a critical
failure, and fixes a broken item at a 5 % cost as an automatic success. The
amount is applied whatever the roll (SK7). NPC repair in the trade window is a
forced repair for gold (service (0x4C8E60)).

**Original check (ARC-SKILLS-007):** read in the retail executable by two readers, rows
[RP1–RP5](ledger/skills.md#repair-arc-skills-007). They confirm SK7 (the case sets success whatever the roll), add
that the repair fate is used before the case also when there is nothing to repair, that the player's skill-bar repair makes the
PC the repairer of any item it holds without action points, and that the master's 1 % with the critical flag is reachable only when
the run restores nothing (an effectiveness of 0, which clears the case's success), on an item that is not broken.

### Spot trap and disarm traps

Spotting (spot (0x4BBE40)) is tried for every unspotted trap on the next
cell of a path and on every use of an object (step (0x427110),
use (0x441980)); an active Detect Traps effect skips the roll. An item with
name 2866 in the shield slot, while the PC carries charges, adds the negative part of the aptitude as the
modifier. A master gets a second roll; magical traps need Expert
(handler (0x4C7160)). Disarm (handler (0x4C7160), trap (0x4BC7B0))
needs a spotted trap; a master gets a second roll; success removes the trap,
and an expert's critical success also yields the trap's disarm item; a critical
failure sets the trap off.

### Pick pocket

From the skill bar the PC opens the target's inventory in steal mode
(activate (0x57A320, 0x57A5E0)); dead targets are looted, and critters flagged
"no pickpocket" or mechanical refuse. Steal or plant one item
(steal/plant (0x575100, 0x575180)): gold moves at most 100, ammunition at most 20.
A theft marks the victim robbed and adjusts alignment. A failure is noticed on
a critical failure, or when the thief is untrained and the victim passes a
perception check; then the victim's and thief's scripts run and, unless
a script vetoes it or the victim is the thief's follower, the theft counts as
an attack by the thief on the victim (AI hostility). A master is never caught planting
(handler (0x4C7160)).

### Prowling

The skill bar toggles concealment (preprocess (0x57A0A0)). Sight
(0x4AF260) and hearing (0x4AF470) reduce the observer's perception
by `prowling effectiveness − difficulty`, clamped 0..100, in percent. In sight
checks a blocking object makes it 100 %; without cover a non-expert gets +50
difficulty; light counts only while untrained. In hearing checks worn armour's
silent-move adjustment counts, halved from Apprentice (difficulty (0x4C8430)).
Movement: an untrained concealed critter paths with an extra restriction flag,
below Expert it uses the stealth walk, and only a master can run concealed
(path (0x425BF0), walk (0x42EDC0)). A concealed master the NPC cannot see
is not a valid AI target (target (0x4AB990)).

**Original check (ARC-SKILLS-008):** read in the retail executable by two readers, rows
[ST1–ST6](ledger/skills.md#stealth-arc-skills-008). They add that the toggle costs nothing and forces the hero's
new state on every follower, that a new follower copies its leader's concealment and a leaving one loses it, that a dialog's
start and an attack on the party end it while being seen, a map change or a load do not, that an expert walks (only the
stealth walk is below expert), and that the margin is computed per observer at each check and stored nowhere.

### Gambling

In the trade window (gamble (0x579840, 0x579B60)) the NPC must have gambling points, the
item's price must not exceed the PC's limit (a table by gambling level, 0..5000,
doubled from Apprentice, limit (0x4C6520, 0x4C6560)) or the PC's gold; worn items
need Expert and "won't sell" items Master (but see SK5). The roll is opposed
against the NPC's gambling level and uses the saved pre-drawn d100.

### Haggle

Selling to an NPC pays `worth × (H / 2 + 50) %`, where H is haggle effectiveness,, scaled by the item's HP;
buying costs `worth + worth × multiplier × (100 − H) / 10000` before the
reaction adjustment (price (0x461F80)). An expert can sell items the NPC
would not buy; a master can buy "won't sell" and worn items
(sell/buy (0x462170, 0x462230)). Dialog `ha` compares the effective level.

### Persuasion

Dialog `ps` compares the PC's effective level with a threshold (positive: at
least, negative: at most) (conditions (0x4150D0)). Expert +1 maximum
followers (followers (0x4B0490)); Master: every follow and leader check
succeeds (follow (0x4AD950), leader (0x4ADB50)); any persuasion training of
the leader lengthens a follower's "wait here" by one step, unless its charisma is
extraordinary, when no wait ends (wait (0x4AA7A0); this summary first said
"apprentice with extraordinary charisma", which the executable does not do,
[ledger HK2](ledger/skills.md#hero-skills-arc-skills-002)).
Steamwoven models the follow rules ([follow consent](../../src/Arcanum.Simulation/Party/FollowConsentRules.cs)),
and since ARC-SKILLS-002 the session reads the hero's persuasion training itself
([hero skills](../world/hero-skills-runtime.md)).

### Dialog and scripts

Conditions `sk skill level` and `tr skill tier` compare the effective level or
training (positive: at least, negative: at most); `ps` and `ha` are the
persuasion and haggle shortcuts (conditions (0x4150D0)). Option `u:skill`
offers an NPC's skill as a service: free for its leader, otherwise priced
`(training + 1) × (level + 2)`; it requests a forced success that is lost on
the way (SK8) (option (0x416C10), price (0x4C62D0),
service (0x419B50, 0x419C30, 0x419CB0)). The NPC must pass the AI's
use-skill check (SK4). Scripts read a skill level (SK1) and run "use item with
skill" with a modifier (script (0x447220)).

## 7. Persistence and data

| State | Owner | Saved |
| --- | --- | --- |
| Points and training per skill | the critter's two skill array fields | with the object |
| Tech points, magick points | the critter's stored stats | with the object |
| Effects on skills | the critter's effect list | with the object |
| Next gambling roll | Skill module | module blob (save (0x4C5BD0, 0x4C5D50, 0x4C5DB0, 0x4C5DC0, 0x4C5DE0)) |
| "Follower skills" option | settings | settings file |
| Trap known/spotted marks | trap object flags `OF_TRAP_SPOTTED`, `OF_TRAP_PC` (spotted (0x4BBFE0, 0x4BC090)) | with the object |

Data the skills read: `mes\skill.mes` (names), prototypes (points, training,
monstrous flags), `rules\effect.mes` and module effects (skill adjustments),
backgrounds, `Rules\map.patch`, dialog files (`ps`, `ha`, `sk`, `tr`, `et`,
`t:`, `u:`), lock and trap difficulty fields, item usage bonuses and charges.
Everything else in this document is code: the key stats, caps, training
minimums, formulas, opposed table, gambling limits, pick-pocket slot penalties
and monstrous thresholds.

## 8. Anomalies found by this reading

**Original check (ARC-RES-048):** these rows were checked in the retail 1.0.7.4 executable; the verdicts in the [verification ledger](ledger/character.md#skills-levels-and-character-arc-res-044-arc-res-031-arc-res-027) supersede the earlier readings below where they differ.

Recorded, not adopted. "Inference" marks a reading of intent.

| ID | Where | Source behaviour | Likely intent (inference) |
| --- | --- | --- | --- |
| SK1 | script command "get skill" (script (0x447220)) | the case has no `return`, so after storing the level it falls into "cast spell" and casts with its own operands reinterpreted as caster, spell and target | return after storing; reachability depends on shipped scripts (census) |
| SK2 | basic skill − button (dec (0x57ACD0, 0x57AEB0)) | the loop "until the level changes" re-sets the same `points − 1` and refunds a point on every pass; when one point does not change the level (an effect pins it at 0, at 20 or at the key-stat cap) it never ends and keeps refunding; reachable only after buying a point in the same editor session, since − stops at the accepted value | update the point count each pass, or refund once |
| SK3 | `skill_ui_set_training` (dispatch (0x57AC90)) | sends a tech skill to the basic setter and a basic skill to the tech setter; `ui_set_skill_training` has no single-player caller (ui (0x4603E0)) | swapped labels; harmless in single player (ledger SK3: called only from the multiplayer handler, where the repair off-by-one is an original bug) (confirms the FR-CHAR-018 note; not new) |
| SK4 | `ai_check_use_skill` (check (0x4AE570)) | **Settled by ledger SK4, confirmed by PL7 (ARC-SKILLS-006):** the case is pick locks (skill 14), not disarm traps: an NPC with a PC leader may pick without lockpicks, any other picker needs them, and the target must be lockable; disarm traps has no case | no anomaly: the earlier reading numbered the skills one off |
| SK5 | gambling gates (gamble (0x579840, 0x579B60)) | worn (Expert) and "won't sell" (Master) gates read the **NPC's** gambling training, while the price limit reads the PC's; haggle's matching gates read the PC's (sell/buy (0x462170, 0x462230)) | the PC's training |
| SK6 | skill effectiveness (effectiveness (0x4C62E0)) | the prowling formula is unreachable (prowling is opposed); backstab has no formula (0) | dead branches; prowling's opposed path is the live rule |
| SK7 | repair (handler (0x4C7160)) | HP is restored and durability lost whatever the roll; success only changes the message (and fate gives full HP) | unknown: may be the original design ("repair a percentage"); retail check |
| SK8 | dialog skill service (service (0x419B50, 0x419C30, 0x419CB0), goal (0x42A2A0)) | `dialog_use_skill` asks for a forced success for a non-follower, but the use-skill goal maps only the plant flag and pick locks drops all flags, so the paid service rolls like any use | forced, as the trade-window repair service is |

Already recorded elsewhere and not renumbered: A2 (double modifier, now known
to apply to every skill), LM1, LM2 and LM14. SK3 is numbered here only to confirm the FR-CHAR-018 swap note.

## 9. Consequences for Steamwoven (project policy proposals)

1. **Skills join the character owner.** The ranks, training and skill effects
   go into the existing `CharacterStatsOwner` (or a sibling built and saved
   with it), because the key-stat cap, the stat-lowering guard and the
   tech-points side effect cross skills and stats in one transaction.
2. **Store points 0..5, refuse more.** The six-bit field allows 63; the
   strict owner accepts only reachable values and reports a typed refusal,
   as `SetBase` does for stats. The census tells whether prototypes ever
   store more; if they do, the reader keeps them and the level clamp still
   holds.
3. **Effects target skills in level units.** The effect model gains basic and
   tech skill targets ([EffectRules](../../src/Arcanum.Data/Characters/EffectRules.cs)
   already parses them); the pre-made heroes then keep their race skill
   changes (SH-09) instead of dropping them.
4. **One pure skill check, shared with combat.** Effectiveness, difficulty,
   roll and criticals form one Godot-free function over explicit inputs (levels,
   training, the facts of 5.3, a labelled roller from the session RNG, fate as
   an input). The attack resolver of ARC-COMBAT-001 consumes the same function
   for its attack and dodge rolls instead of owning a second copy.
5. **A2 and SK1..SK8 under D2.** Steamwoven setting (D8; "Default" before): A2 counts the modifier once, SK1
   returns, SK2 refunds one point per click, SK4 checks pick locks, SK5 reads
   the PC, SK8 forces the paid service; SK3 and SK6 need no behaviour; SK7 follows the
   original (ledger SK7: HP restored and durability lost whatever the roll). The Original setting ("Source" before D8) reproduces each where harmless (SK1 and SK2
   never: a spell cast from a read, a hang).
6. **Dialog thresholds before rolls.** `ps`, `ha`, `sk`, `tr` and `et` are
   pure reads of the hero's skills and unlock real dialog branches without any
   random roll; they are the cheapest visible win.

## 10. Proposed SKILLS slices

Codes are allocated on creation (D6). All code slices need a machine with the
pinned .NET SDK and the original data. Session slices change the snapshot
schema and therefore go one at a time with other schema changes. "Top-20
#n" names an item of the owner's planning list outside the repository.
The [skills plan](skills-plan.md) (ARC-RES-061, 2026-10-20) refines these
slices against later main into tasks S1..S17.

| Slice | Content | Requirements | Depends on | Lane |
| --- | --- | --- | --- | --- |
| K1 Skill ranks | Owner state for 16 skills: points, training, key stats, caps, effects in level units, monstrous melee, purchase/refund with an explicit point budget and an accepted floor, training ladder, tech-points side effect, stat-lowering guard; synthetic tests. | FR-CHAR-016, 017, 018 (rules), 020 (tech points) | WORLD-012 (exists) | Mac, Simulation only, no schema |
| K2 Skill check | The pure check of section 5: effectiveness, opposed table, difficulty from a facts record, roll, criticals, fate and forced inputs, per-skill post-rules as typed outcomes; A2 decision; the attack resolver switches to it. | FR-CHAR-019; FR-COMBAT-001 inputs | K1; top-20 #1 RNG (or an injected roller until then); ARC-COMBAT-001 merged | Mac, Simulation only |
| K3 Hero skills in the session | The hero's skills in session state and snapshot; pre-made race and gender skill effects kept; a skills port for dialog `ps`, `ha`, `sk`, `tr` conditions and `tr`, `et` actions (today unknown dialog opcodes are refused as `UnsupportedOperation`); the party consent's persuasion fact read from the hero. | FR-CHAR-016..018 in play; FR-SCRIPT dialog gates | K1; session lane | Mac, schema |
| K4 Skills on the world | One sub-slice per use, each when its world object exists: pick locks on doors and containers (world thread objects), heal with bandages (top-20 #2 life state), spot and disarm traps (world thread traps), repair (item HP). Each uses K2, charges 4 AP in combat and offers follower substitution. | FR-CHAR-019 handlers | K2, K3 and the named world owner | Mac |
| K5 Stealth | Concealment toggle, prowling's perception reduction in sight and hearing, stealth walk and run rules. | prowling | K2; AI plan I2 perception | Mac |
| K6 Trade and social | Haggle prices, gambling with the saved roll, pick pocket steal and plant with detection. | haggle, gambling, pick pocket | K2, K3; a trade window | Mac |
| K7 Training and sheet | Dialog trainers (`t:`, 100 gold), Educator, skill page with + and − on the character sheet (top-20 #11). | FR-CHAR-016, 018 UI | K3; top-20 #11 | Mac, viewer |

The first visible result is K1 → K3: dialog branches gated by skills open for
a hero whose race skill effects are now real (which Crash Site lines use the
gates is a census item). K2 can run in parallel with
K3 once ARC-COMBAT-001 is merged.

## 11. Census items for the Mac

For the original-data census (the Mac combat census task or its follow-up;
local aggregates only):

- prototypes: points and training per skill (any point count above 5, any
  training without the level for it), monstrous flags and their character
  levels;
- effects (`effect.mes`, module `gameeffect.mes`, backgrounds) touching skills:
  which skills, operators and largest magnitude (SK2 needs an effect that pins a level at 0, 20 or the key-stat cap);
- `Rules\map.patch`: any `training` key;
- dialog files: count of `ps`, `ha`, `sk`, `tr`, `et`, `t:` and `u:`, and
  which Crash Site and cave lines use them;
- scripts: any use of the "get skill" command (SK1) and of "use item with
  skill" with a non-zero modifier (A2);
- Crash Site objects: lock difficulties, jammed or locked flags, trap
  difficulties, lockpick and bandage usage bonuses and charges.

## Method

An earlier source reading (provenance in git history), covering, by executable
address, the skill functions (0x4C5BD0–0x4C91F0) as a whole
and every caller of their public functions (combat, item, animation, AI, trap, stat, script, map,
critter, background, level, dialog, effect, tech, spell and object functions,
and the skill, character editor, inventory, item, turn-based and general UI
functions), found with a search for every exported skill function.
Multiplayer branches were skipped. The rules above are restated in prose and
tables; evidence level F0 unless a ledger row is cited.



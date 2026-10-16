# Magic core: spells, colleges, cost, maintenance, resistance and effects

Status: **Research**, work item ARC-RES-043 (2026-10-16). This document fixes
how the pinned source runs magick: the spell table and the 16 colleges, how a
spell is learned and mastered, how a cast is checked, paid and resolved, how
a target resists, how durations and maintained spells live and end, and what
the 25 effect component types do. It is the magic counterpart of the
[combat core research](combat-core-evidence.md) (ARC-RES-030) and builds on
the [combat-domain load map](combat-domain-load-map.md) (ARC-RES-040), which
already records **what** loads (files, record layout, parse rules, save
blobs); this document records **how it works**. It changes no production API,
reader, viewer, session schema or dialog port. Requirements:
[FR-MAGIC-001](../requirements/combat-magic-tech.md#fr-magic-001) to
[FR-MAGIC-007](../requirements/combat-magic-tech.md#fr-magic-007); summons
(FR-MAGIC-008), teleport (FR-MAGIC-009), magic against technology
(FR-MAGIC-010) and item triggers (FR-MAGIC-011) are touched only where the
core calls them.

## Vocabulary and boundary

- **Source fact**: read from the pinned CE specification
  [`a7ff41b300ef712f0e7d088183a3d08957110cdd`][ce] (Sustainable Use License
  v1.0) at the line ranges linked below. CE is a reconstruction lineage, not an
  original-executable observation, and keeps decompiler names (`sub_…`); a
  reading of their intent is marked as such.
- **CE anomaly**: a branch that looks like a reconstruction slip. It is
  recorded under [CE anomalies](#ce-anomalies-recorded-not-adopted) as
  MG1..MG20, never adopted silently; under owner decision D2 the slice that
  uses it decides the fix and logs it in the plan's
  [anomaly decisions](npc-combat-ai-plan.md#anomaly-decisions).
- **Project policy**: a choice this project makes where the source leaves a
  runtime decision open. Every policy below is a **proposal**.

No original file, probe or game data was read for this work item; every number
below is a source constant or formula, not a retail observation. Original
executable parity remains **NOT_VERIFIED**. "Roll d100" and "roll d20" mean the
source's inclusive `random_between(1, 100)` and `random_between(1, 20)`;
integer arithmetic is C division, truncating toward zero. Distances are
Chebyshev cell distances ([distance][dist]). "Real seconds" means the unit of
the data fields; the session clock turns them into game time at the CE rate of
8× ([clock rate][te-rate], already modelled by
[`GameTimeFlow`](../world/game-clock-runtime.md)).

Roles in a cast, as the source names them: the **source** is what casts (a
critter, or an item, trap or nothing); the **parent** is the critter behind it
(the holder of an item, or the critter itself); the **target** is an object or
a tile. "Magic" means a spell record without the technology flag.

## Answer

1. **One table of 223 records serves magic and technology.** Records 0..79 are
   the 80 college spells: 16 colleges × 5 ranks, college = `n / 5`, rank =
   `n % 5 + 1` ([spell header][spell-h]). Records 80..139 are magic records
   outside any college (their willpower minimum is forced to 0; what they hold
   is a census question). Records 140..222 are technology (forced tech flag,
   no willpower minimum) ([load][mt-load]).
2. **Learning is strictly sequential.** Each rank costs one character point,
   needs the previous rank of its college, character level {1, 1, 5, 10, 15}
   for ranks 1..5, willpower at least the record's `IQ:` value (default
   {6, 9, 12, 15, 18}, ARC-RES-040 LM6) and intelligence at least 5 (a
   constant, LM5). Each learned rank adds one magick point, so about +5 magick/tech
   aptitude (integer division can make a step 4). Only the highest known rank of a college can be unlearned, which
   refunds the point ([add/remove][spell-add], [UI][ui-learn]).
3. **Mastery is one college for life, given only by a script.** It needs all
   five ranks and no earlier mastery; then that college's spells cost half
   (rounded up to even at run time) and last and maintain twice as long
   ([mastery][spell-mastery], [script][script-mastery], [time][mt-time-scale]).
4. **A cast is a pipeline.** Invocation check (turn, range, line of fire,
   forbidden flags, Bonds of Magick, aptitude, intelligence, anti-teleport)
   → a run slot (512) → the casting animation (4 AP in turn-based mode and
   an affordability check) → at the release frame the BEGIN action: caster
   alive and conscious, intelligence and willpower, **payment**, no-stack and
   mutual cancels → per target: anti-magic shell, reflection, resistance and
   saving throw → effect components → scheduling of maintenance, duration or
   END ([check][mt-check], [run][mt-invoke], [animation][anim-cast],
   [stage][mt-stage], [process][mt-process], [schedule][mt-schedule]).
5. **Cost is fatigue, paid once at BEGIN.** `Cost:` with mastery (halved,
   then +1 when odd), ×2 for dwarves, halved again for critters flagged
   fatigue-limiting; worn items with a mana store pay first and recharge
   later; the rest is fatigue damage, refused only when fatigue would fall to
   −15 or lower, so a caster can faint from casting. A spell cast from an item
   spends one charge of the item's spell store instead. Script and dialog
   "free" casts pay nothing ([pay][mt-pay], [cost][mt-cost]).
6. **Resistance is magic-only and two-step.** Against a target other than the
   caster, a d100 below the target's magic resistance (raised for tech-aligned
   targets) gives a resistance margin; the record's `Resist:(stat@value)` then
   allows a d20 saving throw that adds to the margin. Damage is reduced by the
   margin in percent (at least 50 % after a successful save); a non-damaging,
   non-maintained spell is lost entirely on a save, or, without a save, on the
   d100 alone; a maintained spell still lands but costs the margin in percent
   more to maintain. Extraordinary willpower makes a target immune to
   willpower-resisted spells ([resist][mt-resist]).
7. **Durations and maintenance run on game time.** `Duration:(seconds@stat)`
   plus `DurationStatInfo:` schedules the END (or `DurationTriggerCount:`
   repeated MAINTAIN ticks first); `Maintain:(cost@seconds)` charges each
   period for as long as the caster pays, with at most `INT / 4` maintained
   spells per caster. An unresistable run (`No_Resist:` or a script's
   "unresistable" cast) gets no duration or maintenance: its END follows the
   BEGIN at once (MG19). Unconsciousness, rest, world-map travel, a follower's
   fight ending, death or destruction of caster, target or summon, a dispel
   and an explicit stop all end runs ([schedule][mt-schedule], [cap][mt-cap],
   [interrupt][mt-interrupt], [death][mt-death]).
8. **Effects are 25 component types per action** (BEGIN, MAINTAIN, END,
   CALLBACK, END_CALLBACK), each with its own area filter. Damage and heal go
   through the combat damage and heal steps of ARC-RES-030; effects add or
   remove numbered entries of the character effect list; object flags carry
   the visible states (invisible, stoned, charmed, mind-controlled, …) with
   reference counting ([process][mt-process], [components](#effect-components-fr-magic-007)).
9. **This reading adds 20 CE anomalies (MG1..MG20)**, among them a cost check
   that indexes the spell table by run number (MG1), three disagreeing cost
   formulas (MG2), a double maintenance charge (MG3), two dead aptitude
   branches (MG5, MG6), a dead interrupt component (MG7) and a summon faction
   switch read from the wrong field (MG8), an unresistable flag that also
   means "END pending", so unresistable runs never last (MG19) and a refused cast that still
   drains worn mana stores (MG20).

## State model (source facts)

| State | Where | Notes |
| --- | --- | --- |
| Known spells | critter array `OBJ_F_CRITTER_SPELL_TECH_IDX` 0..15: rank known per college (0..5); 16: mastered college or −1 | defaults all 0 and −1 ([defaults][spell-add]); 17..24 are tech degrees (TECHNOLOGY) |
| Magick points | stat `MAGICK_POINTS` (0..210), +1 per learned rank, −1 per unlearned | day, night, sky and nature mage backgrounds add or remove 4 at read time ([backgrounds][stat-mage-bg]) |
| Aptitude | derived stat `(50 × MP − 55 × TP) / 10` + sector adjustment, −100..100, then effects | already in [`CharacterStatRules`](../world/character-stats-runtime.md) ([stat][stat-aptitude], [sector][mt-aptitude-adj]) |
| Magic resistance | object resistance 4 (normal, fire, electrical, poison, magic) + worn armour adjustments (magic part scaled by item power) + effects, clamped 0..95 (monstrous NPCs unclamped) | [resistance][resist] |
| Item mana | `OBJ_F_ITEM_MANA_STORE` (worn items: a pool that pays casting costs), `OBJ_F_ITEM_SPELL_MANA_STORE` (charges of the item's own spells; negative means unlimited) | [pay][mt-pay] |
| Spell flags | `OBJ_F_SPELL_FLAGS`, 32 states (invisible, body of air/earth/fire/water, shielded, anti-magic shell, bonds of magick, full reflection, summoned, stoned, charmed, entangled, mind controlled, …) | set and cleared by object-flag components |
| Active spells | 512 run slots: spell, action, source, parent, target (object or tile), attacker, object list, summoned list, flags, resistance margin, trigger, next tick time, remaining tick count | saved in the MagicTech-Post blob ([load map](combat-domain-load-map.md#6-runtime-owners-and-persistence)) |
| Timers | game-time MagicTech event (maintain or END tick, delayed interrupt, deferred BEGIN) and "recharge magic item" event, both saved | [timer table][te-table] |

## Findings

### Definitions (FR-MAGIC-001)

Every record starts from defaults and is then overwritten by the keys present
([defaults][mt-defaults], [line +1][mt-parse-1], [line +3][mt-parse-3], [AI
line][mt-parse-ai], [components][mt-parse-comp]):

| Field | Default | Meaning at run time |
| --- | --- | --- |
| `IQ:` | {6, 9, 12, 15, 18}[n mod 5]; 0 for n ≥ 80 | minimum **willpower** to learn, cast and maintain (LM6) |
| `Cost:` | 0 | fatigue (or charge) at BEGIN |
| `Maintain:(cost@period)` | 0, 0 | period > 0 makes the spell maintained; period in real seconds |
| `Duration:(period@stat)`, `DurationStatInfo:(level@modifier)` | 0, −1, 0, 1 | seconds until END; with a stat, `+ max(0, level − caster stat) × modifier` seconds |
| `DurationTriggerCount:` | 0 | MAINTAIN ticks of a timed spell before END; also the tick budget of a maintained spell whose source is gone (8 when 0) |
| `Resist:(stat@value)` | none | saving throw stat and modifier (usually negative per CE's comment) |
| `Range:` | 99 | Chebyshev cells from the parent to the target |
| `ChargesFromCells:` | no | cost is paid in charge-cell ammunition instead of fatigue |
| `ChargeBeginCost: 0` | (charged) | skip the BEGIN payment |
| `Info:` | neutral | friendly, aggressive or both; aggressive spells notify the AI of an attack on each target |
| `Disabled:` | enabled | refuses only a PC's activation from the spell UI ([UI][ui-enabled]); AI, script, item and dialog casts do not test it |
| `No_Stack:`, `Cancels_SF:`, `Cancels_EnvSF:` | none | stacking and mutual cancellation |
| `Disallowed_SF:`, `Disallowed_TSF:`, `Disallowed_TCF:` | none | spell flags forbidden on the caster or target, critter flags forbidden on the target |
| `ItemTriggers:` | none | which item events the record (and each component) answers |
| `No_Resist:`, `No_Reflect:` | absent | by presence: never resisted, never reflected; `No_Resist:` also marks the run unresistable, which ends it right after BEGIN (MG19, [flags][mt-run-flags]) |
| `AI_*` | −1 | priorities for the ten AI lists (ARC-RES-039) |
| Components | none | per action: area (`AoE:` over 65 target names, spell-flag filters, `Radius:`, `Count:`), then one component per line |

A record "has damage" when any action holds a Damage component; that flag
drives the resistance rules below.

### Learning, colleges and mastery (FR-MAGIC-002)

- **Order.** Learning rank r of a college requires knowing exactly r − 1
  ranks, character level ≥ {1, 1, 5, 10, 15}[r − 1], intelligence ≥ 5 and
  willpower ≥ the record's `IQ:` ([add][spell-add], [levels][spell-levels]).
  The UI first requires one unspent character point (`spell_cost` is always
  1) and names the failing check ([UI][ui-learn]).
- **Effect of learning.** College level becomes r and `MAGICK_POINTS` rises
  by 1, so aptitude rises by about 5 (the truncating division in
  `(50·MP − 55·TP) / 10` can make a step 4) ([add][spell-add],
  [aptitude][stat-aptitude]). The forced variant, used by prototypes
  ([proto][proto-spell]) and the F11 cheat key ([cheat][main-cheat]), skips
  the checks, sets the rank directly and adds `r − current` magick points
  without spending character points; auto-level uses the checked variant and
  spends unspent points itself ([level][level-auto]).
- **Unlearning.** Only the highest known rank, refunding one point and one
  magick point ([remove][spell-add], [UI][ui-learn]); a stat decrease is
  re-validated against every known rank ([checks][spell-check]).
- **Mastery.** Allowed once, with all five ranks of the college
  ([mastery][spell-mastery]); only the script action "give spell mastery in
  college" calls it ([script][script-mastery]). Effects: BEGIN cost halved
  (rounded up to even, MG2), maintenance period and duration doubled
  ([time][mt-time-scale]); maintenance **cost** is not reduced.
- **Services.** Paid spellcasting in trade and dialog (identify, healing,
  other casts) costs a flat 100 coins ([money][spell-cost]).

### Choosing the target (FR-MAGIC-003)

The invocation check runs before any slot or payment ([check][mt-check]):

1. In turn-based mode only the critter whose turn it is may cast.
2. The spell number must be 0..222.
3. **Range**: Chebyshev distance from the parent to the target object or tile
   ≤ `Range:`; an object inside an inventory is always in range
   ([range][mt-range]).
4. **Line of fire**: the parent's line-of-fire test to the target tile must be
   below 100 and any blocker must be shoot-through or the target itself; an
   item inside a container is always visible; self-casts skip it
   ([LOS][mt-los]). The line-of-fire test itself is combat open question O4.
5. **Forbidden flags**: caster spell flags against `Disallowed_SF:`, target
   spell flags against `Disallowed_TSF:`, target critter flags against
   `Disallowed_TCF:` ([flags][mt-flags-check]).
6. **Bonds of Magick**: a critter under it cannot cast its own spells (items
   still work).
7. **NPC fallback**: for a non-PC parent, the target is evaluated against the
   BEGIN area with radius 2; if it fails and the spell takes tiles, the first
   valid tile in radius 2 around the target object replaces it.
8. **Aptitude** (magic only): a tech-aligned holder cannot use a magic item
   whose effective power is ≤ 0; a tech-aligned target was meant to nullify
   by chance (dead in CE, MG5).
9. **Intelligence** ≥ 5 for a critter casting its own spell, and Teleportation
   is refused in anti-teleport areas.

Area selection at each action builds a target list from the action's area:
self, source, the object, objects or tiles in a square radius, a wall line,
the "cone" (a square around the origin in CE's reading), party and non-party
critters, the parent or attacker, and **the run's own object list**, which
lets MAINTAIN and END reach exactly the objects BEGIN reached
([targets][target-build]). Each component then filters the current target
by its own area and may re-aim at self, the source, the last summoned object
or the target tile ([apply area][mt-apply-aoe]). CE flags doubts in the cone
and count code ([targets][target-build]); this reading did not audit the
target module beyond those paths.

### Paying (FR-MAGIC-004)

**When.** The casting goal spends 4 AP in turn-based mode and checks that the
cast is affordable before the animation (MG1); the BEGIN action pays when the
spell is magic, the parent is a critter, the run is not free and the record
does not set `ChargeBeginCost: 0` ([animation][anim-cast],
[stage][mt-stage]). A free run (script "cast free spell", dialog cast by an
NPC without a PC leader) pays nothing ([script][script-cast],
[dialog][dialog-spell]). A cast interrupted before its BEGIN ran is charged
in full and dropped ([interrupt][mt-interrupt]).

**How much** at BEGIN ([cost][mt-cost]):

```
cost = Cost
if spell < 80 and caster mastered its college: cost = cost / 2; if cost is odd: cost + 1
if cheat mode: cost = 1
if caster is a dwarf PC or NPC: cost × 2
```

**From where** ([pay][mt-pay]):

1. Records with `ChargesFromCells:` take charge-cell ammunition from the
   holder: `2 × cost − magic-adjusted cost` for an item source, plain `cost`
   for a critter; refused when the holder has fewer.
2. A critter flagged fatigue-limiting pays half (at least 1).
3. A critter's worn items with a mana store pay first, slot by slot; each
   drained amount schedules a recharge (below), and an emptied item notifies
   the AI. The stores are written **before** the fatigue test of step 4, so a
   refused cast still drains them, with no refund (MG20).
4. The rest is fatigue damage. Refused if `current fatigue − rest ≤ −15`;
   otherwise applied. When the record is maintained or has no duration and
   fatigue is now below 0, the cast fails **after** paying; a timed,
   non-maintained spell still lands. Fatigue at 0 or below knocks out a
   caster that is not undead, so at exactly 0 the cast lands and the caster
   faints; that ends every non-tech run the caster is the source of, timed
   ones included, except item-triggered runs that are not maintained
   ([unconscious][unconscious], [demaintain][mt-demaintain]).
5. An item source (scroll, wand, device) spends one charge of its spell mana
   store; 0 refuses; a negative store never runs out.

**Recharge of worn mana items.** A drain schedules a game-time event 60 real
seconds later that restores `2 × max(1, elapsed game seconds / 120)` points
up to the amount drained and reschedules the rest; drains of the same item
merge into one event ([recharge][mt-recharge]).

**Worked example.** `Cost: 6`, mastered college, dwarf caster: run time
charges `6 / 2 = 3 → 4`, then `× 2 = 8` fatigue; the UI and AI formula
`spell_cast_cost` gives `6 × 2 / 2 = 6` (MG2).

### Resistance, saving throw and aptitude (FR-MAGIC-005, FR-MAGIC-010)

Applied at BEGIN to each target, for magic records only, and skipped for
self-targets, `No_Resist:` records and unresistable runs (script "cast
unresistable spell") ([resist][mt-resist]):

```
margin = 0
if spell is Disintegrate and target has "no disintegrate": target skipped
if target != caster:
    if target is a PC and its armed fate "save against magick" fires: margin = 10   (MG13)
    else:
        R = target magic resistance (0..95)
        if target aptitude A < 0 and the cast is not fate-maximised:
            R = 100 − (100 − R) × (A + 100) / 100
        roll d100; if roll < R: margin = R − roll
        if R > 0 and the spell has no damage and is not maintained:
            with a saving throw: penalty = margin / 10            (MG4)
            without one: if roll < R: target skipped
if target is the caster, the spell has no saving throw or the target is no critter: apply with margin
if Resist stat is Willpower and the target's willpower is extraordinary: target skipped (an aggressive spell still notifies the AI)
S = Resist value + target stat − penalty
if S > 0 and d20 ≤ S:
    margin += S − d20
    damaging spell: margin = max(margin, 50)
    else, not maintained: target skipped
apply with margin
```

What the margin does: Damage components remove `margin %` of the rolled
damage (at least 1), except that with a margin a Damage component carrying
the death flag does nothing at all on that target; Movement and Destroy do nothing on
a resisted target (Destroy still removes summoned objects); a maintained
spell's MAINTAIN ticks charge `cost + max(1, margin % of cost)` on top of the
normal charge (MG3). The margin is rolled per target but stored once per run,
so the last target's margin is the one later ticks use. A skipped target
plays the "resisted" effect.

**Worked example.** Magic resistance 30, target aptitude −40: `R = 100 −
70 × 60 / 100 = 58`; a d100 of 20 gives margin 38. A fireball-like damaging
spell then deals 62 % of its roll before the target's fire resistance step;
if the target also makes its save, the margin is at least 50.

**Predictor for UI and AI.** `magictech_cast_spell_fail_chance` returns a
percentage the AI compares with 38 ([fail][mt-fail], [AI][ai-cast-check]).
It follows the same shape but differs (MG14): it uses `R / 10` instead of the
rolled margin, treats `R > 40` without a save as 100 % for non-damaging,
non-maintained spells, and **rolls a d20 itself**, so asking the question
moves the random stream.

**Aptitude elsewhere.** Item power is `(complexity + holder aptitude) / 2`,
clamped to the item's side and its complexity; magic armour adjustments scale
by it ([item power][item-power]). Lock-breaking spells fail when the caster's
aptitude is below the lock difficulty, unless fate-maximised, and alert
guards either way ([lock][mt-lock]). Scaled damage, heal and effect counts
use the parent's aptitude captured at the cast (below). The sector aptitude
adjustment enters aptitude itself ([sector][mt-aptitude-adj]).

**Fate.** An armed "spell at maximum" fate on a PC source sets the run's
source and parent aptitude to 100 and disables the tech-aligned resistance
bonus and the lock check for that action ([process][mt-process]); "save
against magick" on a PC target sets margin 10 (MG13). Both are consumed when
they fire ([fate][fate]).

**Magic against technology.** Records 140..222 skip resistance, the BEGIN
payment and the aptitude gates; the anti-magic shell and reflection checks
below do not test the technology flag (open question O8).

### Shells, reflection, stacking and cancels

- **Anti-magic shell** on the source: its own spells fail ("you lose your
  concentration"); on a target: the effect is nullified for that target. The
  run that grants the shell is exempt ([source][mt-shell-source],
  [target][mt-reflect]).
- **Full reflection** on a target (not for `No_Reflect:`): the run is marked
  reflected, the caster of the reflection pays the reflected cost (MG15),
  and source and target swap for the components; if the original caster also
  reflects, the spell fizzles ([reflect][mt-reflect]).
- **No stacking**: with `No_Stack:`, BEGIN is refused when the same spell
  already runs on the same target (or, for a self spell, from the same
  source); the refusal comes after payment (MG16) ([no stack][mt-nostack]).
- **Cancels**: a new run interrupts the other runs from the same **source**
  that share any `Cancels_SF:` bit (the body spells); for `Cancels_EnvSF:`
  the same happens only when the local PC already carries all of those spell
  flags (or, for detect-invisible alone, has extraordinary perception)
  ([cancel][mt-nostack], [env][mt-env-sf]).

### Duration, maintenance and ending (FR-MAGIC-006)

After each action the scheduler decides what comes next
([schedule][mt-schedule], [timers][mt-timers], [time][mt-time-scale]):

- **Unresistable run** (`No_Resist:` or a script "unresistable" cast): END
  at once, unless a BEGIN callback is pending, before any maintenance or
  duration branch (MG19). The same flag later marks a timed run whose final
  END is scheduled.
- **Nothing was applied** (targets existed but every one was filtered out,
  shielded or resisted): END at once, unless a callback is pending. An empty
  target list or an action without components counts as applied.
- **Maintained** (`Maintain:` period > 0) with a source: check the cap, then
  schedule MAINTAIN after `period` seconds (×2 with mastery) and put the run
  in the PC's maintain bar. Each MAINTAIN re-checks intelligence and
  willpower and pays the maintenance cost (dwarves ×2, no mastery discount);
  failing either ends the spell with "maintain terminated". The **cap** counts
  the parent's active magic maintained runs: more than `INT / 4` ends the new
  one. The PC bar shows `INT / 4` slots, at most 5 ([UI][ui-maintain]).
- **Maintained without a source** (the item or caster detached): runs
  `DurationTriggerCount:` ticks (8 when 0), then END.
- **Timed** (`Duration:` set): schedule the next step after
  `period + max(0, level − parent stat) × modifier` seconds (×2 with
  mastery); with `DurationTriggerCount:` n, n MAINTAIN ticks precede the END,
  otherwise the next step is END. The END of a timed run is unresistable
  (the flag of MG19).
- **Neither**: END at once.
- **Callbacks**: an eye-candy component with a callback flag defers the
  decision to the animation; its completion runs the CALLBACK or
  END_CALLBACK action ([interrupt][mt-interrupt]).

All waits are game-time events: `seconds × 1000 × 8` game milliseconds.

**Ending.** `magictech_interrupt` moves a run to END (or to a callback action
first when the record has one) and cancels its pending tick
([interrupt][mt-interrupt]). It is reached from: the maintain bar click
([UI][ui-maintain]); the script "stop spell" ([script][script-stop]);
dispel; a failed payment or check; the caster going unconscious, resting,
travelling on the world map, or (for an NPC follower) its leader leaving
combat ([unconscious][unconscious], [follower][ai-demaintain]); and the
death or destruction of an involved object: the parent's runs end, a source
item is detached, runs on the target end, and a summoned object ends the run
that summoned it ([death][mt-death]). A cast killed before its BEGIN is
charged and freed. Entangle is re-resisted each time the entangled critter
is hit ([entangle][mt-entangle]).

### Effect components (FR-MAGIC-007)

Components run in order inside an action, on each target the area filter
accepts, unless the record and component item triggers exclude the current
trigger ([process][mt-process]). Branch components can jump forward or end
the list.

| Type | What it does |
| --- | --- |
| Noop | nothing |
| AGoal | knock back 4 cells away from the parent, knock down, make the target follow the parent, or push another animation goal aimed at the parent ([goal][mt-agoal]) |
| AGoalTerminate | cancel the target's animation goals of one type |
| AIRedirect | AI redirect with critter flags and `MinIQ:` (AI section) |
| Cast | the parent casts another spell at the current target or tile as a friendly cast, through the full invocation check ([cast][mt-cast]) |
| ChargeNBranch | pay `Cost:` from the source; on failure jump to `Branch:` or end the list |
| Damage | `DmgType:` normal, poison, electrical, fire, "magic" (index 4, the fatigue slot) or acid; `Dmg:a-b` uniform; `Scaled` makes it `min + aptitude % × (max − min)`, no roll (aptitude ≤ 0: min); `Full` fatigue knocks out (current fatigue + 10); other `Dmg_Flags:` (death, stun, scars, dropped weapon, crippling, …) pass to the combat damage step; margin reduces it ([damage][mt-damage]) |
| Destroy | destroy the target unless resisted; summoned objects always ([destroy][mt-destroy-dispel]) |
| Dispel | interrupt every other magic run sourced by, aimed at, summoned as or listing the target; clear "magically held", "stoned", perma-polymorph; destroy a summoned target ([dispel][mt-destroy-dispel]) |
| Effect | add or remove `Count:` entries of effect `N` with `Cause:`; `Scaled:` adds `scaled × aptitude / 100` more ([effect][mt-effect]) |
| EnvFlag | stub: logs an error, changes nothing ([stub][mt-envflag]) |
| EyeCandy | add or remove a visual on the target; callback flags drive the CALLBACK actions ([fx][mt-eyecandy]) |
| Heal | heal HP, poison or fatigue by a range (scaled as Damage); `Full` heals everything; `Resurrect`/`Reanimate` raise the dead unless flagged unresurrectable or vetoed by the resurrect script; `Scars` remove injuries ([heal][mt-heal], [combat heal][combat-heal]) |
| Identify | identify a critter, container contents or item |
| Interrupt | meant to interrupt another spell on the target; a no-op in CE (MG7) ([interrupt][mt-interrupt-comp]) |
| Movement | move the target to the target tile, a random tile in a radius, a world-map teleport (anti-teleport checked) or the nearest known area; skipped when resisted ([movement][mt-movement]) |
| ObjFlag | set or clear one flag of a named flag field, with side effects (below) ([flag][mt-objflag]) |
| Recharge | add to an item's spell charges up to `Max:` (9999 by default) ([recharge][mt-recharge-comp]) |
| Summon | create a prototype or a list pick at the target tile (below) ([summon][mt-summon-comp]) |
| Terminate | end the component list |
| TestNBranch | compare a target field (art, teleport tile, fleeing-from, HP damage) with `TestVal:`; on failure jump to `Branch:` or end the list ([branch][mt-branch]) |
| Trait | polymorph: drop items the new art cannot wield, set monster art and palette, elemental flags (MG10); `-1` restores ([trait][mt-trait]) |
| TraitIdx | set a stat or resistance to `value × mul / div + add` |
| Trait64 | store the target tile and map, or the parent handle, in a field |
| Use | run the target's use script |

**Object-flag side effects** ([flag][mt-objflag]). Setting a spell flag on an
object that already has it is skipped while another active run provides it;
clearing keeps it while more than one run provides it (reference counting by
the records' object-flag components). Invisible adds the invisible and
translucent render flags; shrunk, water walking and stoned set their object
flags; ending water walking on a blocking tile kills (full damage);
entangled and paralysed stop movement goals; mind control makes an NPC
follow the parent with no flee (−50 reaction if a PC did it to a
non-summoned NPC) and disbands it at the end; charm gives +30 reaction toward
a PC and a kill-on-sight override (animals go through a separate path);
undead sets "animated dead"; fleeing and blinded call the AI and combat;
"don't draw" also paralyses; unlocking checks aptitude against the lock and
alerts guards.

**Summons** ([summon][mt-summon], [list][mt-summon-comp],
[tables][mt-summon-tables]). The summoned object is flagged summoned, with
the record's palette, no pickpocketing, no experience, concealed until it
appears, alignment of the parent and faction of an NPC caster, or faction 0
when the faction switch is on (MG8). Lists 0..2 are weighted (forest animals;
farm animals; a patriarch wolf with a 1 % vorpal bunny), lists 3..5 pick by
the parent's aptitude (familiars at 25/50/75/higher; forest animals at
20/40/55/70/95/higher; undead at 20/60/85/higher). Summoned objects are
tracked in the run, so ending the run removes them, and a dispel destroys
them.

## CE anomalies (recorded, not adopted)

**Original check (ARC-RES-048):** these rows were checked in the retail 1.0.7.4 executable; the verdicts in the [verification ledger](original-verification-ledger.md#magic-arc-res-043-load-map-arc-res-040) supersede the CE readings below where they differ.

"Inference" marks a reading of intent. MG numbers continue ARC-RES-040's
LM1..LM20 (LM5 and LM6 also concern magic). CE's own deliberate changes are
listed after the table.

| ID | Where | Source behaviour | Likely intent (inference) |
| --- | --- | --- | --- |
| MG1 | casting goal ([anim][anim-cast], [check][mt-anim-charge]) | the affordability check before the animation reads `Cost:` (or the maintenance cost) of the spell whose **number equals the run slot**, not of the spell being cast, and applies no mastery; the table has only 223 entries, so run slots 223..511 read past its end (undefined behaviour) | the cast spell's own cost |
| MG2 | cost ([cost][mt-cost], [UI/AI][spell-cost], [UI check][mt-can-charge]) | three formulas: run time halves for mastery and adds 1 when the half is odd, then doubles for dwarves; `spell_cast_cost` (UI, AI) doubles then halves with truncation; the UI affordability check ignores mastery | one formula; likely `ceil(cost / 2)` for mastery |
| MG3 | maintenance tick ([stage][mt-stage], [pay][mt-maint-pay]) | with a resistance margin a tick charges the plain cost **and** the cost plus the margin share | charge only the surcharged cost |
| MG4 | saving throw ([resist][mt-resist], [fail][mt-fail]) | the resistance-derived term is **subtracted** from the save, while CE's own comment quotes the manual: "+1 bonus on the save for each 10 % of resistance" | add it |
| MG5 | aptitude gate ([check][mt-check]) | a tech-aligned target "nullifies" when `d100 ≤ target aptitude`, which is negative, so it never happens | use the magnitude, reduced by a magick-aligned caster's aptitude |
| MG6 | time scaling ([time][mt-time-scale]) | the aptitude difference is an absolute value, then tested for `< 0`, so the duration/maintenance scaling by aptitude never applies | an unknown signed scaling |
| MG7 | Interrupt component ([interrupt][mt-interrupt-comp], [stop][mt-interrupt]) | builds an invocation without source or parent, then searches only when the source is set and matches the (empty) parent: never interrupts anything | interrupt the named spell on the target |
| MG8 | summon ([list][mt-summon-comp], [summon][mt-summon]) | `Clear_Faction:` is parsed but the faction switch is fed `List:`: a fixed prototype (list −1) or lists 1..5 get faction 0 and no alignment copy; list 0 keeps the caster's | use `Clear_Faction:` |
| MG9 | summon lists 0..2 ([list][mt-summon-comp]) | the weight roll is 0..100 inclusive over tables summing to 100 (lists 0, 2) or 120 (list 1): a roll of 100 falls off the end to the first entry with an error log; the cow of list 1 needs exactly 100 | roll 0..99 over consistent weights |
| MG10 | polymorph to an elemental ([trait][mt-trait]) | critter flags 2 and blit flags are **assigned** a single flag, wiping the others (summoned "no pickpocket" among them); the restore clears only that bit | OR the flags in |
| MG11 | maintained-slot predicate ([slots][mt-slots], [AI][ai-cast-check]) | the counter of maintained spells is an empty loop returning 0, so the AI's "no free maintenance slot" refusal fires only below intelligence 4; the real cap is applied later, after BEGIN | count the caster's maintained runs |
| MG12 | maintain bar ([UI][ui-maintain]) | with every slot taken the "no candidate" value −1 passes the `≥ count` test and writes slot −1; `INT / 4` above 5 indexes past five slots; latent while the run-time cap equals `INT / 4` | refuse when no slot is free; cap at 5 |
| MG13 | fate "save against magick" ([resist][mt-resist]) | sets a margin of 10 (and a display value of 100); the target still rolls its save | a full save (margin 100); low confidence |
| MG14 | fail-chance predictor ([fail][mt-fail]) | differs from the resolver (R / 10, 100 % above R 40, no margin) and consumes a d20 | a pure estimate that matches the resolver |
| MG15 | reflection cost ([reflect][mt-reflect]) | the reflection caster pays "the last cost charged" from a global, stale for free or item casts | the reflected spell's cost |
| MG16 | no-stack refusal ([stage][mt-stage]) | the BEGIN cost is paid before the no-stack test, so a refused recast is paid | test before paying; low confidence that it is a slip |
| MG17 | fate maximisation ([process][mt-process]) | evaluated on every action with components, so an END can be maximised when its BEGIN was not and then removes more scaled effect entries than were added | capture once at BEGIN |
| MG18 | target names ([names][mt-tgt]) | `Tgt_Obj_No_ST_Critter_Dead` lacks the critter type bit that its siblings carry (CE's FIXME) | add the bit |
| MG19 | unresistable runs ([flags][mt-run-flags], [schedule][mt-schedule]) | one run flag means both "unresistable" (`No_Resist:`, script unresistable casts) and "final END scheduled"; the scheduler tests it before maintenance and duration, so an unresistable spell ends right after BEGIN whatever its `Duration:` or `Maintain:` | separate flags; an unresistable spell keeps its duration (the census sizes which records combine them) |
| MG20 | mana stores ([pay][mt-pay]) | worn mana stores are drained and their recharge scheduled before the fatigue refusal (≤ −15), so a refused cast still costs the stored mana | test fatigue before draining; low confidence that it is a slip |

**CE's deliberate changes in these paths** (CE marks them `FIX:` or `CE:`;
the original may differ here, see O10): the shell and reflection check moves
the self-target test below the anti-magic shell handling (so an
interrupted spell that never began runs no END) and lets runs dispelled by the
shell, or cast before a reflection shield appeared, finish their END ([reflect][mt-reflect]); the
charm reaction bonus (+30, removed on END) bypasses the effect list so it can
be reversed ([charm][mt-charm]); a summoned monster's own palette becomes its
base art so transformations can be undone ([summon][mt-summon]).

Not a slip but a risk for the effects owner: the Effect component removes
the **first** entry with that effect number, whatever its cause, so an END can
remove an identical effect that an item or background added
([effect][effect-add]).

## Open questions

- **O1** Retail parity of every constant and formula above (NOT_VERIFIED).
- **O2** What records 80..139 are, and which records use `Scaled`, `Full`,
  `Resurrect`, `DurationStatInfo:`, `DurationTriggerCount:`, `Resist:` stats,
  `ChargesFromCells:`, `ChargeBeginCost:` and `Disabled:`: a data census
  question (below).
- **O3** The target module was read only along the magic paths; cone, count
  and radius doubts flagged in CE stay open for FR-MAGIC-003 acceptance.
- **O4** How spell timers behave in turn-based mode (the game clock advances
  one second per round per ARC-RES-030) was not traced.
- **O5** Technology records: charge-cell use, tech degrees and the
  `ChargeNBranch` pattern belong to a TECHNOLOGY research item.
- **O6** Item triggers (`mt_item.c`, FR-MAGIC-011) and the spell visual
  catalogue (`SpellEyeCandy.mes`, the missile and casting art) were not read.
- **O7** The effect list semantics (`effect.c`) belong to the durable-effects
  owner (top-20 item 17); this reading used only add and remove.
- **O8** Whether the anti-magic shell and reflection are meant to stop
  technology too (the code does not test the flag).
- **O9** CE's comment says the original omits equipment magic resistance
  adjustments in these calls, yet the called resistance function adds worn
  armour adjustments; which one the original does is unverified.
- **O10** What the original does where CE deliberately changed behaviour
  (the `FIX:`/`CE:` list after the anomaly table).

## What AI slice I8 needs from this domain

Slice I8 of the [NPC combat AI plan](npc-combat-ai-plan.md#phase-1-faithful-reconstruction)
(support and magic, after stage A5) calls these magic functions
([eligibility][ai-cast-check], [cast][ai-cast]):

| AI step | Magic input |
| --- | --- |
| Ten action lists | records with `AI_*` values (slice MAGIC-1) |
| Eligibility codes 1..8 | `spell_is_known` and college level ("cast highest only"), cost against current fatigue with `spell_cast_cost` (MG2), intelligence ≥ 5 and willpower ≥ `IQ:` (LM5, LM6), maintained-slot predicate (MG11), item spell charges, fail-chance predictor ≥ 38 (MG14, with its d20), line of fire |
| Skip recasting | "is under the influence of" over active runs |
| Perform | invocation check, then run; for items the source is detached afterwards |
| Heal ladder | Heal components (HP, poison, fatigue, resurrect) on the vital state of combat slice 1 |
| Summon list | Summon component and tables |
| Defensive buffs | Effect and ObjFlag components, maintenance and the cap |
| After the fight | demaintain on a follower's "demaintain spells" flag |
| Items | worn mana stores, spell charges, the holder-can-use-item gate (`sub_456A10`) and item power (`item_effective_power`), the item-exhausted notification |

So I8 needs MAGIC slices 1 to 5 below, and slice 6 for summoning and
resurrection if the first fights use them. Under D2 each anomaly I8 meets
(MG1, MG2, MG11, MG14, MG20 at least) needs its decision row before I8 merges, and
the predictor's d20 must come from the injected, labelled random source.

## Steamwoven inputs that already exist

[`CharacterStatRules`](../../src/Arcanum.Simulation/Character/CharacterStatRules.cs)
already holds magick and tech points (0..210) and derives aptitude with the
sector adjustment ([contract](../world/character-stats-runtime.md));
[`EffectRules`](../../src/Arcanum.Data/Characters/EffectRules.cs) parses
the `effect.mes` rules, and the
[`EffectCause`](../../src/Arcanum.Simulation/Character/CharacterStatTypes.cs)
enum of applied effects already has a Spell cause;
[`MesFile`](../../src/Arcanum.Data/Messages/MesFile.cs) reads message files;
the session clock already turns host time into game time at 8×
([clock](../world/game-clock-runtime.md)). Spells, colleges, mana stores,
spell flags, active runs, vital state (HP and fatigue) and resistances do not
exist yet.

## Proposed implementation slices (project policy, not accepted)

Type `MAGIC` per the [naming policy](../workflow/task-naming.md); codes are
allocated when each task is created (D6). Each slice is a separate PR; all
keep Data/Simulation/Session free of Godot, take randomness from an injected,
seedable source with a purpose label per roll, and settle each anomaly they
meet by an owner decision logged in the plan (D2). Under D8 only a bug of the
original game gets a switch of the Original setting; for a CE slip both
settings follow the original ([verification ledger](original-verification-ledger.md) says which is which).

1. **Spell definitions** (Data, FR-MAGIC-001): read `spelllist.mes` passes A
   and B, `magictech.mes` component names and `spell.mes` names into
   immutable records with every field of the definitions table; unknown keys,
   tokens and component types become typed refusals in strict mode and are
   reproduced where harmless in the "like original" mode of ARC-RES-040 §9.
   Synthetic fixtures; a `RealData` test counts 223 records on the Mac.
2. **Spell book** (Simulation, Session; FR-MAGIC-002): college ranks and
   mastery on the critter, learn and unlearn rules with typed refusals,
   magick points feeding the existing aptitude formula, snapshot schema
   version bump.
3. **Cast rules** (Simulation, pure; FR-MAGIC-003/004/005/010): one cost
   calculator (MG2 decided), a payment plan over worn mana stores (MG20), fatigue,
   item charges and charge cells, the invocation check with a line-of-fire
   hook, the resistance and save resolver returning the margin (MG4, MG5,
   MG13 decided) and a predictor that matches it (MG14).
4. **Active spells** (Session; FR-MAGIC-006): runs with BEGIN, MAINTAIN, END
   and callback actions on the session clock, maintenance payment and the
   `INT / 4` cap, duration and tick counts, interrupt and every ending
   trigger, persistence in the snapshot, the timer recharge of mana items;
   MG3, MG11 and MG19 decided.
5. **Core components** (FR-MAGIC-007): Damage and Heal over combat slice 1,
   Effect over the durable-effects owner (top-20 item 17), ObjFlag for spell
   flags with reference counting, Dispel, the branch components; EyeCandy as
   a presentation event only.
6. **World components** (FR-MAGIC-008/009): Summon with its tables (MG8,
   MG9), Movement and teleport through the world transitions, Trait
   polymorph (MG10), AGoal, AIRedirect, Cast, Identify, Recharge, Destroy,
   Use.
7. **Casting in the viewer** (UI lane): spell list and targeting, the
   maintain bar (MG12), feedback texts 600..605 of `spell.mes`.
8. **Item spells** (FR-MAGIC-011) with the items domain.

Dependencies: session RNG (top-20 item 1), combat slice 1 vital state for
slices 3 and 5, durable effects (item 17) for slice 5, equipment (item 14)
for worn mana stores.

## Data census additions

For the Mac census that follows ARC-RES-040 (stage A9), in addition to the
`spelllist.mes` counts listed there: per record `Cost:`, `Maintain:`,
`Duration:`, `DurationStatInfo:`, `DurationTriggerCount:`, `Resist:` stat and
value, `Range:`, `Info:`, `No_Stack:`, the cancel and disallow sets,
component types per action, `Dmg_Flags:` in use (scaled, full, resurrect),
summon `Proto:`, `List:` and `Clear_Faction:` (to size MG8), records that
combine `No_Resist:` with `Maintain:` or `Duration:` (to size MG19), and what records
80..139 contain; per Crash Site and cave critter its college ranks and
mastery; per item its mana store, spell charges and spells.

## Method

Source reading only, at the pinned commit above: `magictech.c` (tables,
getters, payment, process, all 25 component functions, stage checks,
resistance and predictor, scheduling, run slots, invocation, check, line of
fire, interrupts, parse of record lines +1, +3, +4 and components, influence
queries, timers, recharge, death hooks), `magictech.h`, `spell.c`,
`spell.h`, `ui/spell_ui.c` (apply, maintain bar, learning), and the called
parts of `stat.c`, `object.c`, `item.c`, `critter.c`, `combat.c`, `ai.c`,
`anim.c`, `script.c`, `dialog.c`, `fate.c`, `effect.c`, `target.c`,
`location.c` and `timeevent.c`. Steamwoven inputs are the requirements and
the research and runtime documents linked above. No CE code or structure was
copied; names in this document are descriptive.

[ce]: https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd
[mt-tgt]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L150-L216
[mt-summon-tables]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L477-L536
[mt-load]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L1301-L1435
[ui-enabled]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/spell_ui.c#L155-L163
[proto-spell]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/proto.c#L16235
[main-cheat]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/main.c#L485-L500
[level-auto]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L1095-L1105
[mt-run-flags]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L4770-L4786
[mt-demaintain]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L5469-L5488
[mt-env-sf]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L6139-L6158
[mt-charm]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L4085-L4180
[mt-pay]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L1536-L1690
[mt-cost]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L1698-L1727
[mt-can-charge]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L1729-L1752
[mt-anim-charge]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L1754-L1787
[mt-slots]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L1810-L1848
[mt-summon]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L1856-L1963
[mt-process]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L1986-L2103
[mt-agoal]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L2105-L2175
[mt-cast]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L2177-L2218
[mt-damage]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L2220-L2296
[mt-destroy-dispel]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L2298-L2453
[mt-effect]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L2455-L2487
[mt-eyecandy]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L2489-L2553
[mt-heal]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L2555-L2590
[mt-interrupt-comp]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L2606-L2617
[mt-movement]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L2652-L2750
[mt-summon-comp]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L2760-L2815
[mt-branch]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L2817-L2872
[mt-trait]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L2874-L3032
[mt-envflag]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L3039-L3043
[mt-stage]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L3045-L3174
[mt-maint-pay]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L3176-L3217
[mt-nostack]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L3219-L3288
[mt-shell-source]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L3309-L3336
[mt-resist]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L3338-L3441
[mt-fail]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L3447-L3572
[mt-apply-aoe]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L3574-L3638
[mt-schedule]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L3643-L3866
[mt-cap]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L3868-L3905
[mt-range]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L3907-L3927
[mt-timers]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L3929-L3985
[mt-recharge-comp]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L3987-L4027
[mt-objflag]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L4029-L4277
[mt-lock]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L4279-L4304
[mt-time-scale]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L4306-L4349
[mt-reflect]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L4412-L4495
[mt-invoke]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L4646-L4903
[mt-flags-check]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L4905-L4926
[mt-check]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L4928-L5070
[mt-los]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L5148-L5183
[mt-interrupt]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L5285-L5518
[mt-defaults]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L5520-L5567
[mt-parse-1]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L5649-L5715
[mt-parse-3]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L5780-L5816
[mt-parse-ai]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L5818-L5875
[mt-parse-comp]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L5877-L6137
[mt-recharge]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L6584-L6665
[mt-death]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L6667-L6778
[mt-entangle]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L6780-L6912
[mt-aptitude-adj]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L6988-L7001
[spell-h]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/spell.h#L1-L146
[spell-levels]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/spell.c#L25-L37
[spell-cost]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/spell.c#L347-L422
[spell-add]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/spell.c#L262-L583
[spell-check]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/spell.c#L674-L733
[spell-mastery]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/spell.c#L735-L789
[ui-learn]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/spell_ui.c#L490-L542
[ui-maintain]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/spell_ui.c#L332-L480
[stat-aptitude]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L604-L607
[stat-mage-bg]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L464-L510
[resist]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L1680-L1730
[item-power]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L418-L502
[unconscious]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L462-L495
[combat-heal]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L2104-L2291
[anim-cast]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L7190-L7290
[ai-demaintain]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L405-L417
[ai-cast]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2355-L2401
[ai-cast-check]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3606-L3681
[script-mastery]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L2563-L2568
[script-cast]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L2605-L2655
[script-stop]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L2720-L2727
[dialog-spell]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L3926-L3940
[fate]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/fate.c#L132-L153
[effect-add]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/effect.c#L488-L562
[target-build]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/target.c#L1148-L1560
[dist]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/location.c#L535-L556
[te-table]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L93-L118
[te-rate]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L820-L842

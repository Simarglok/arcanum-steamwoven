# Magic core: spells, colleges, cost, maintenance, resistance and effects

Status: **Research**, work item ARC-RES-043 (2026-10-16). This document fixes
how the game runs magick: the spell table and the 16 colleges, how a
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

- **Source fact**: an earlier source reading (provenance in git history),
  cited by the executable address of its function; level F0 (not checked in
  the executable) unless a row of the
  [original verification ledger](original-verification-ledger.md) is cited.
  Unnamed functions keep decompiler names (`sub_…`); a reading of their intent
  is marked as such.
- **Anomaly**: a branch that looks like a slip. It is
  recorded under [Anomalies](#anomalies-recorded-not-adopted) as
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
Chebyshev cell distances (distance (0x4B96F0)). "Real seconds" means the unit of
the data fields; the session clock turns them into game time at the game's rate of
8× (clock rate (0x45B370), already modelled by
[`GameTimeFlow`](../world/game-clock-runtime.md)).

Roles in a cast, as the source names them: the **source** is what casts (a
critter, or an item, trap or nothing); the **parent** is the critter behind it
(the holder of an item, or the critter itself); the **target** is an object or
a tile. "Magic" means a spell record without the technology flag.

## Answer

1. **One table of 223 records serves magic and technology.** Records 0..79 are
   the 80 college spells: 16 colleges × 5 ranks, college = `n / 5`, rank =
   `n % 5 + 1`. Records 80..139 are magic records
   outside any college (their willpower minimum is forced to 0; what they hold
   is a census question). Records 140..222 are technology (forced tech flag,
   no willpower minimum) (load (0x44FE30, 0x44FFA0, 0x450090, 0x4501D0)).
2. **Learning is strictly sequential.** Each rank costs one character point,
   needs the previous rank of its college, character level {1, 1, 5, 10, 15}
   for ranks 1..5, willpower at least the record's `IQ:` value (default
   {6, 9, 12, 15, 18}, ARC-RES-040 LM6) and intelligence at least 5 (a
   constant, LM5). Each learned rank adds one magick point, so about +5 magick/tech
   aptitude (integer division can make a step 4). Only the highest known rank of a college can be unlearned, which
   refunds the point (add/remove (0x4B1530, 0x4B1570, 0x4B15A0, 0x4B1600, 0x4B1620, 0x4B1650, 0x4B1660, 0x4B16C0, 0x4B1740, 0x4B1750, 0x4B1760, 0x4B1770, 0x4B1790, 0x4B1950, 0x4B19B0, 0x4B1A40), UI (0x57C540, 0x57C670)).
3. **Mastery is one college for life, given only by a script.** It needs all
   five ranks and no earlier mastery; then that college's spells cost half
   (rounded up to even at run time) and last and maintain twice as long
   (mastery (0x4B1C70, 0x4B1CB0, 0x4B1CF0), script (0x447220), time (0x455250)).
4. **A cast is a pipeline.** Invocation check (turn, range, line of fire,
   forbidden flags, Bonds of Magick, aptitude, intelligence, anti-teleport)
   → a run slot (512) → the casting animation (4 AP in turn-based mode and
   an affordability check) → at the release frame the BEGIN action: caster
   alive and conscious, intelligence and willpower, **payment**, no-stack and
   mutual cancels → per target: anti-magic shell, reflection, resistance and
   saving throw → effect components → scheduling of maintenance, duration or
   END (check (0x4564E0), run (0x455A20, 0x455AC0, 0x455C30), animation (0x429B50, 0x429BB0, 0x429BC0, 0x429C40, 0x429C80, 0x429CD0),
   stage (0x452F20), process (0x4510F0), schedule (0x453FA0)).
5. **Cost is fatigue, paid once at BEGIN.** `Cost:` with mastery (halved,
   then +1 when odd), ×2 for dwarves, halved again for critters flagged
   fatigue-limiting; worn items with a mana store pay first and recharge
   later; the rest is fatigue damage, refused only when fatigue would fall to
   −15 or lower, so a caster can faint from casting. A spell cast from an item
   spends one charge of the item's spell store instead. Script and dialog
   "free" casts pay nothing (pay (0x450420), cost (0x4507D0)).
6. **Resistance is magic-only and two-step.** Against a target other than the
   caster, a d100 below the target's magic resistance (raised for tech-aligned
   targets) gives a resistance margin; the record's `Resist:(stat@value)` then
   allows a d20 saving throw that adds to the margin. Damage is reduced by the
   margin in percent (at least 50 % after a successful save); a non-damaging,
   non-maintained spell is lost entirely on a save, or, without a save, on the
   d100 alone; a maintained spell still lands but costs the margin in percent
   more to maintain. Extraordinary willpower makes a target immune to
   willpower-resisted spells (resist (0x4537B0)).
7. **Durations and maintenance run on game time.** `Duration:(seconds@stat)`
   plus `DurationStatInfo:` schedules the END (or `DurationTriggerCount:`
   repeated MAINTAIN ticks first); `Maintain:(cost@seconds)` charges each
   period for as long as the caster pays, with at most `INT / 4` maintained
   spells per caster. An unresistable run (`No_Resist:` or a script's
   "unresistable" cast) gets no duration or maintenance: its END follows the
   BEGIN at once (MG19). Unconsciousness, rest, world-map travel, a follower's
   fight ending, death or destruction of caster, target or summon, a dispel
   and an explicit stop all end runs (schedule (0x453FA0), cap (0x4545E0),
   interrupt (0x456F70, 0x456FA0, 0x457000, 0x457030, 0x457060, 0x4570E0, 0x457100, 0x457110, 0x4571E0, 0x457270, 0x4573D0, 0x457450, 0x4574D0, 0x457530), death (0x459740, 0x4598D0, 0x459A20)).
8. **Effects are 25 component types per action** (BEGIN, MAINTAIN, END,
   CALLBACK, END_CALLBACK), each with its own area filter. Damage and heal go
   through the combat damage and heal steps of ARC-RES-030; effects add or
   remove numbered entries of the character effect list; object flags carry
   the visible states (invisible, stoned, charmed, mind-controlled, …) with
   reference counting (process (0x4510F0), [components](#effect-components-fr-magic-007)).
9. **This reading adds 20 anomalies (MG1..MG20)**, among them a cost check
   that indexes the spell table by run number (MG1), three disagreeing cost
   formulas (MG2), a double maintenance charge (MG3), two dead aptitude
   branches (MG5, MG6), a dead interrupt component (MG7) and a summon faction
   switch read from the wrong field (MG8), an unresistable flag that also
   means "END pending", so unresistable runs never last (MG19) and a refused cast that still
   drains worn mana stores (MG20).

## State model (source facts)

| State | Where | Notes |
| --- | --- | --- |
| Known spells | critter array `OBJ_F_CRITTER_SPELL_TECH_IDX` 0..15: rank known per college (0..5); 16: mastered college or −1 | defaults all 0 and −1 (defaults (0x4B1530, 0x4B1570, 0x4B15A0, 0x4B1600, 0x4B1620, 0x4B1650, 0x4B1660, 0x4B16C0, 0x4B1740, 0x4B1750, 0x4B1760, 0x4B1770, 0x4B1790, 0x4B1950, 0x4B19B0, 0x4B1A40)); 17..24 are tech degrees (TECHNOLOGY) |
| Magick points | stat `MAGICK_POINTS` (0..210), +1 per learned rank, −1 per unlearned | day, night, sky and nature mage backgrounds add or remove 4 at read time (backgrounds (0x4B0490)) |
| Aptitude | derived stat `(50 × MP − 55 × TP) / 10` + sector adjustment, −100..100, then effects | already in [`CharacterStatRules`](../world/character-stats-runtime.md) (stat (0x4B0740), sector (0x45A060)) |
| Magic resistance | object resistance 4 (normal, fire, electrical, poison, magic) + worn armour adjustments (magic part scaled by item power) + effects, clamped 0..95 (monstrous NPCs unclamped) | resistance (0x43D6D0) |
| Item mana | `OBJ_F_ITEM_MANA_STORE` (worn items: a pool that pays casting costs), `OBJ_F_ITEM_SPELL_MANA_STORE` (charges of the item's own spells; negative means unlimited) | pay (0x450420) |
| Spell flags | `OBJ_F_SPELL_FLAGS`, 32 states (invisible, body of air/earth/fire/water, shielded, anti-magic shell, bonds of magick, full reflection, summoned, stoned, charmed, entangled, mind controlled, …) | set and cleared by object-flag components |
| Active spells | 512 run slots: spell, action, source, parent, target (object or tile), attacker, object list, summoned list, flags, resistance margin, trigger, next tick time, remaining tick count | saved in the MagicTech-Post blob ([load map](combat-domain-load-map.md#6-runtime-owners-and-persistence)) |
| Timers | game-time MagicTech event (maintain or END tick, delayed interrupt, deferred BEGIN) and "recharge magic item" event, both saved | event type table 0x5B2188 (address from ledger OQ2; saveability of these kinds F0) |

## Findings

### Definitions (FR-MAGIC-001)

Every record starts from defaults and is then overwritten by the keys present
(defaults (0x457580), line +1 (0x4578F0), line +3 (0x457D00), AI
line (0x457E70), components (0x458060)):

| Field | Default | Meaning at run time |
| --- | --- | --- |
| `IQ:` | {6, 9, 12, 15, 18}[n mod 5]; 0 for n ≥ 80 | minimum **willpower** to learn, cast and maintain (LM6) |
| `Cost:` | 0 | fatigue (or charge) at BEGIN |
| `Maintain:(cost@period)` | 0, 0 | period > 0 makes the spell maintained; period in real seconds |
| `Duration:(period@stat)`, `DurationStatInfo:(level@modifier)` | 0, −1, 0, 1 | seconds until END; with a stat, `+ max(0, level − caster stat) × modifier` seconds |
| `DurationTriggerCount:` | 0 | MAINTAIN ticks of a timed spell before END; also the tick budget of a maintained spell whose source is gone (8 when 0) |
| `Resist:(stat@value)` | none | saving throw stat and modifier (usually negative per a comment of the earlier reading) |
| `Range:` | 99 | Chebyshev cells from the parent to the target |
| `ChargesFromCells:` | no | cost is paid in charge-cell ammunition instead of fatigue |
| `ChargeBeginCost: 0` | (charged) | skip the BEGIN payment |
| `Info:` | neutral | friendly, aggressive or both; aggressive spells notify the AI of an attack on each target |
| `Disabled:` | enabled | refuses only a PC's activation from the spell UI (UI (0x57BC70)); AI, script, item and dialog casts do not test it |
| `No_Stack:`, `Cancels_SF:`, `Cancels_EnvSF:` | none | stacking and mutual cancellation |
| `Disallowed_SF:`, `Disallowed_TSF:`, `Disallowed_TCF:` | none | spell flags forbidden on the caster or target, critter flags forbidden on the target |
| `ItemTriggers:` | none | which item events the record (and each component) answers |
| `No_Resist:`, `No_Reflect:` | absent | by presence: never resisted, never reflected; `No_Resist:` also marks the run unresistable, which ends it right after BEGIN (MG19, flags (0x455C30)) |
| `AI_*` | −1 | priorities for the ten AI lists (ARC-RES-039) |
| Components | none | per action: area (`AoE:` over 65 target names, spell-flag filters, `Radius:`, `Count:`), then one component per line |

A record "has damage" when any action holds a Damage component; that flag
drives the resistance rules below.

### Learning, colleges and mastery (FR-MAGIC-002)

- **Order.** Learning rank r of a college requires knowing exactly r − 1
  ranks, character level ≥ {1, 1, 5, 10, 15}[r − 1], intelligence ≥ 5 and
  willpower ≥ the record's `IQ:` (add (0x4B1530, 0x4B1570, 0x4B15A0, 0x4B1600, 0x4B1620, 0x4B1650, 0x4B1660, 0x4B16C0, 0x4B1740, 0x4B1750, 0x4B1760, 0x4B1770, 0x4B1790, 0x4B1950, 0x4B19B0, 0x4B1A40)).
  The UI first requires one unspent character point (`spell_cost` is always
  1) and names the failing check (UI (0x57C540, 0x57C670)).
- **Effect of learning.** College level becomes r and `MAGICK_POINTS` rises
  by 1, so aptitude rises by about 5 (the truncating division in
  `(50·MP − 55·TP) / 10` can make a step 4) (add (0x4B1530, 0x4B1570, 0x4B15A0, 0x4B1600, 0x4B1620, 0x4B1650, 0x4B1660, 0x4B16C0, 0x4B1740, 0x4B1750, 0x4B1760, 0x4B1770, 0x4B1790, 0x4B1950, 0x4B19B0, 0x4B1A40),
  aptitude (0x4B0740)). The forced variant, used by prototypes
  (proto (0x49B5A0)) and the F11 cheat key (cheat (0x401560)), skips
  the checks, sets the rank directly and adds `r − current` magick points
  without spending character points; auto-level uses the checked variant and
  spends unspent points itself (level (0x4A7890)).
- **Unlearning.** Only the highest known rank, refunding one point and one
  magick point (remove (0x4B1530, 0x4B1570, 0x4B15A0, 0x4B1600, 0x4B1620, 0x4B1650, 0x4B1660, 0x4B16C0, 0x4B1740, 0x4B1750, 0x4B1760, 0x4B1770, 0x4B1790, 0x4B1950, 0x4B19B0, 0x4B1A40), UI (0x57C540, 0x57C670)); a stat decrease is
  re-validated against every known rank (checks (0x4B1B90, 0x4B1C00)).
- **Mastery.** Allowed once, with all five ranks of the college
  (mastery (0x4B1C70, 0x4B1CB0, 0x4B1CF0)); only the script action "give spell mastery in
  college" calls it (script (0x447220)). Effects: BEGIN cost halved
  (rounded up to even, MG2), maintenance period and duration doubled
  (time (0x455250)); maintenance **cost** is not reduced.
- **Services.** Paid spellcasting in trade and dialog (identify, healing,
  other casts) costs a flat 100 coins (money (0x4B1650, 0x4B1660, 0x4B16C0, 0x4B1740)).

### Choosing the target (FR-MAGIC-003)

The invocation check runs before any slot or payment (check (0x4564E0)):

1. In turn-based mode only the critter whose turn it is may cast.
2. The spell number must be 0..222.
3. **Range**: Chebyshev distance from the parent to the target object or tile
   ≤ `Range:`; an object inside an inventory is always in range
   (range (0x454700)).
4. **Line of fire**: the parent's line-of-fire test to the target tile must be
   below 100 and any blocker must be shoot-through or the target itself; an
   item inside a container is always visible; self-casts skip it
   (LOS (0x456BC0)). The line-of-fire test itself is combat open question O4.
5. **Forbidden flags**: caster spell flags against `Disallowed_SF:`, target
   spell flags against `Disallowed_TSF:`, target critter flags against
   `Disallowed_TCF:` (flags (0x456430)).
6. **Bonds of Magick**: a critter under it cannot cast its own spells (items
   still work).
7. **NPC fallback**: for a non-PC parent, the target is evaluated against the
   BEGIN area with radius 2; if it fails and the spell takes tiles, the first
   valid tile in radius 2 around the target object replaces it.
8. **Aptitude** (magic only): a tech-aligned holder cannot use a magic item
   whose effective power is ≤ 0; a tech-aligned target was meant to nullify
   by chance (it never fires, also in the original: ledger MG5).
9. **Intelligence** ≥ 5 for a critter casting its own spell, and Teleportation
   is refused in anti-teleport areas.

Area selection at each action builds a target list from the action's area:
self, source, the object, objects or tiles in a square radius, a wall line,
the "cone" (a square around the origin in the earlier reading), party and non-party
critters, the parent or attacker, and **the run's own object list**, which
lets MAINTAIN and END reach exactly the objects BEGIN reached
(targets (0x4F40B0)). Each component then filters the current target
by its own area and may re-aim at self, the source, the last summoned object
or the target tile (apply area (0x453D40, 0x453EE0, 0x453F20)). The earlier reading flagged doubts in the cone
and count code (targets (0x4F40B0)); this reading did not audit the
target module beyond those paths.

### Paying (FR-MAGIC-004)

**When.** The casting goal spends 4 AP in turn-based mode and checks that the
cast is affordable before the animation (MG1); the BEGIN action pays when the
spell is magic, the parent is a critter, the run is not free and the record
does not set `ChargeBeginCost: 0` (animation (0x429B50, 0x429BB0, 0x429BC0, 0x429C40, 0x429C80, 0x429CD0),
stage (0x452F20)). A free run (script "cast free spell", dialog cast by an
NPC without a PC leader) pays nothing (script (0x447220),
dialog (0x419D50)). A cast interrupted before its BEGIN ran is charged
in full and dropped (interrupt (0x456F70, 0x456FA0, 0x457000, 0x457030, 0x457060, 0x4570E0, 0x457100, 0x457110, 0x4571E0, 0x457270, 0x4573D0, 0x457450, 0x4574D0, 0x457530)).

**How much** at BEGIN (cost (0x4507D0)):

```
cost = Cost
if spell < 80 and caster mastered its college: cost = cost / 2; if cost is odd: cost + 1
if cheat mode: cost = 1
if caster is a dwarf PC or NPC: cost × 2
```

**From where** (pay (0x450420)):

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
   (unconscious (0x45D5A0), demaintain (0x457450)).
5. An item source (scroll, wand, device) spends one charge of its spell mana
   store; 0 refuses; a negative store never runs out.

**Recharge of worn mana items.** A drain schedules a game-time event 60 real
seconds later that restores `2 × max(1, elapsed game seconds / 120)` points
up to the amount drained and reschedules the rest; drains of the same item
merge into one event (recharge (0x459590, 0x459640, 0x459680)).

**Worked example.** `Cost: 6`, mastered college, dwarf caster: run time
charges `6 / 2 = 3 → 4`, then `× 2 = 8` fatigue; the UI and AI formula
`spell_cast_cost` gives `6 × 2 / 2 = 6` (MG2).

### Resistance, saving throw and aptitude (FR-MAGIC-005, FR-MAGIC-010)

Applied at BEGIN to each target, for magic records only, and skipped for
self-targets, `No_Resist:` records and unresistable runs (script "cast
unresistable spell") (resist (0x4537B0)):

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
percentage the AI compares with 38 (fail (0x453B20, 0x453CC0), AI (0x4AE720)).
It follows the same shape but differs (MG14): it uses `R / 10` instead of the
rolled margin, treats `R > 40` without a save as 100 % for non-damaging,
non-maintained spells, and **rolls a d20 itself**, so asking the question
moves the random stream.

**Aptitude elsewhere.** Item power is `(complexity + holder aptitude) / 2`,
clamped to the item's side and its complexity; magic armour adjustments scale
by it (item power (0x4614A0, 0x461520, 0x461540, 0x461590)). Lock-breaking spells fail when the caster's
aptitude is below the lock difficulty, unless fate-maximised, and alert
guards either way (lock (0x4551C0)). Scaled damage, heal and effect counts
use the parent's aptitude captured at the cast (below). The sector aptitude
adjustment enters aptitude itself (sector (0x45A060)).

**Fate.** An armed "spell at maximum" fate on a PC source sets the run's
source and parent aptitude to 100 and disables the tech-aligned resistance
bonus and the lock check for that action (process (0x4510F0)); "save
against magick" on a PC target sets margin 10 (MG13). Both are consumed when
they fire (fate (0x4F5270)).

**Magic against technology.** Records 140..222 skip resistance, the BEGIN
payment and the aptitude gates; the anti-magic shell and reflection checks
below do not test the technology flag (open question O8).

### Shells, reflection, stacking and cancels

- **Anti-magic shell** on the source: its own spells fail ("you lose your
  concentration"); on a target: the effect is nullified for that target. The
  run that grants the shell is exempt (source (0x453710),
  target (0x455550)).
- **Full reflection** on a target (not for `No_Reflect:`): the run is marked
  reflected, the caster of the reflection pays the reflected cost (MG15),
  and source and target swap for the components; if the original caster also
  reflects, the spell fizzles (reflect (0x455550)).
- **No stacking**: with `No_Stack:`, BEGIN is refused when the same spell
  already runs on the same target (or, for a self spell, from the same
  source); the refusal comes after payment (MG16) (no stack (0x453410, 0x4534E0)).
- **Cancels**: a new run interrupts the other runs from the same **source**
  that share any `Cancels_SF:` bit (the body spells); for `Cancels_EnvSF:`
  the same happens only when the local PC already carries all of those spell
  flags (or, for detect-invisible alone, has extraordinary perception)
  (cancel (0x453410, 0x4534E0), env (0x458A80)).

### Duration, maintenance and ending (FR-MAGIC-006)

After each action the scheduler decides what comes next
(schedule (0x453FA0), timers (0x454790, 0x4547F0, 0x4548D0), time (0x455250)):

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
  one. The PC bar shows `INT / 4` slots, at most 5 (UI (0x57C290, 0x57C320, 0x57C370, 0x57C3F0, 0x57C470, 0x57C4B0)).
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
  END_CALLBACK action (interrupt (0x456F70, 0x456FA0, 0x457000, 0x457030, 0x457060, 0x4570E0, 0x457100, 0x457110, 0x4571E0, 0x457270, 0x4573D0, 0x457450, 0x4574D0, 0x457530)).

All waits are game-time events: `seconds × 1000 × 8` game milliseconds.

**Ending.** `magictech_interrupt` moves a run to END (or to a callback action
first when the record has one) and cancels its pending tick
(interrupt (0x456F70, 0x456FA0, 0x457000, 0x457030, 0x457060, 0x4570E0, 0x457100, 0x457110, 0x4571E0, 0x457270, 0x4573D0, 0x457450, 0x4574D0, 0x457530)). It is reached from: the maintain bar click
(UI (0x57C290, 0x57C320, 0x57C370, 0x57C3F0, 0x57C470, 0x57C4B0)); the script "stop spell" (script (0x447220));
dispel; a failed payment or check; the caster going unconscious, resting,
travelling on the world map, or (for an NPC follower) its leader leaving
combat (unconscious (0x45D5A0), follower (0x4A8570)); and the
death or destruction of an involved object: the parent's runs end, a source
item is detached, runs on the target end, and a summoned object ends the run
that summoned it (death (0x459740, 0x4598D0, 0x459A20)). A cast killed before its BEGIN is
charged and freed. Entangle is re-resisted each time the entangled critter
is hit (entangle (0x459A30, 0x459C10)).

### Effect components (FR-MAGIC-007)

Components run in order inside an action, on each target the area filter
accepts, unless the record and component item triggers exclude the current
trigger (process (0x4510F0)). Branch components can jump forward or end
the list.

| Type | What it does |
| --- | --- |
| Noop | nothing |
| AGoal | knock back 4 cells away from the parent, knock down, make the target follow the parent, or push another animation goal aimed at the parent (goal (0x4514E0, 0x4516D0, 0x451700)) |
| AGoalTerminate | cancel the target's animation goals of one type |
| AIRedirect | AI redirect with critter flags and `MinIQ:` (AI section) |
| Cast | the parent casts another spell at the current target or tile as a friendly cast, through the full invocation check (cast (0x451740, 0x451850)) |
| ChargeNBranch | pay `Cost:` from the source; on failure jump to `Branch:` or end the list |
| Damage | `DmgType:` normal, poison, electrical, fire, "magic" (index 4, the fatigue slot) or acid; `Dmg:a-b` uniform; `Scaled` makes it `min + aptitude % × (max − min)`, no roll (aptitude ≤ 0: min); `Full` fatigue knocks out (current fatigue + 10); other `Dmg_Flags:` (death, stun, scars, dropped weapon, crippling, …) pass to the combat damage step; margin reduces it (damage (0x4518D0)) |
| Destroy | destroy the target unless resisted; summoned objects always (destroy (0x451AF0, 0x451B90, 0x451BB0, 0x451C40)) |
| Dispel | interrupt every other magic run sourced by, aimed at, summoned as or listing the target; clear "magically held", "stoned", perma-polymorph; destroy a summoned target (dispel (0x451AF0, 0x451B90, 0x451BB0, 0x451C40)) |
| Effect | add or remove `Count:` entries of effect `N` with `Cause:`; `Scaled:` adds `scaled × aptitude / 100` more (effect (0x451F20)) |
| EnvFlag | stub: logs an error, changes nothing (stub (0x452F10)) |
| EyeCandy | add or remove a visual on the target; callback flags drive the CALLBACK actions (fx (0x451FE0)) |
| Heal | heal HP, poison or fatigue by a range (scaled as Damage); `Full` heals everything; `Resurrect`/`Reanimate` raise the dead unless flagged unresurrectable or vetoed by the resurrect script; `Scars` remove injuries (heal (0x4521A0), combat heal (0x4B58C0)) |
| Identify | identify a critter, container contents or item |
| Interrupt | meant to interrupt another spell on the target; a no-op, also in the original (ledger MG7) (interrupt (0x452300)) |
| Movement | move the target to the target tile, a random tile in a radius, a world-map teleport (anti-teleport checked) or the nearest known area; skipped when resisted (movement (0x4524C0, 0x452650)) |
| ObjFlag | set or clear one flag of a named flag field, with side effects (below) (flag (0x454A10, 0x455100)) |
| Recharge | add to an item's spell charges up to `Max:` (9999 by default) (recharge (0x454920)) |
| Summon | create a prototype or a list pick at the target tile (below) (summon (0x452830, 0x452900)) |
| Terminate | end the component list |
| TestNBranch | compare a target field (art, teleport tile, fleeing-from, HP damage) with `TestVal:`; on failure jump to `Branch:` or end the list (branch (0x4529D0, 0x4529F0)) |
| Trait | polymorph: drop items the new art cannot wield, set monster art and palette, elemental flags (MG10); `-1` restores (trait (0x452AD0, 0x452B00, 0x452CD0, 0x452D80, 0x452E40, 0x452ED0)) |
| TraitIdx | set a stat or resistance to `value × mul / div + add` |
| Trait64 | store the target tile and map, or the parent handle, in a field |
| Use | run the target's use script |

**Object-flag side effects** (flag (0x454A10, 0x455100)). Setting a spell flag on an
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

**Summons** (summon (0x450C40), list (0x452830, 0x452900)).
The summoned object is flagged summoned, with
the record's palette, no pickpocketing, no experience, concealed until it
appears, alignment of the parent and faction of an NPC caster, or faction 0
when the faction switch is on (MG8). Lists 0..2 are weighted (forest animals;
farm animals; a patriarch wolf with a 1 % vorpal bunny), lists 3..5 pick by
the parent's aptitude (familiars at 25/50/75/higher; forest animals at
20/40/55/70/95/higher; undead at 20/60/85/higher). Summoned objects are
tracked in the run, so ending the run removes them, and a dispel destroys
them.

## Anomalies (recorded, not adopted)

**Original check (ARC-RES-048):** these rows were checked in the retail 1.0.7.4 executable; the verdicts in the [verification ledger](original-verification-ledger.md#magic-arc-res-043-load-map-arc-res-040) supersede the earlier readings below where they differ.

"Inference" marks a reading of intent. MG numbers continue ARC-RES-040's
LM1..LM20 (LM5 and LM6 also concern magic). The reconstruction's own
deliberate changes are listed after the table.

| ID | Where | Source behaviour | Likely intent (inference) |
| --- | --- | --- | --- |
| MG1 | casting goal (anim (0x429B50, 0x429BB0, 0x429BC0, 0x429C40, 0x429C80, 0x429CD0), check (0x450940)) | the affordability check before the animation reads `Cost:` (or the maintenance cost) of the spell whose **number equals the run slot**, not of the spell being cast, and applies no mastery; the table has only 223 entries, so run slots 223..511 read past its end (undefined behaviour) | the cast spell's own cost |
| MG2 | cost (cost (0x4507D0), UI/AI (0x4B1650, 0x4B1660, 0x4B16C0, 0x4B1740), UI check (0x4508A0)) | three formulas: run time halves for mastery and adds 1 when the half is odd, then doubles for dwarves; `spell_cast_cost` (UI, AI) doubles then halves with truncation; the UI affordability check ignores mastery | one formula; likely `ceil(cost / 2)` for mastery |
| MG3 | maintenance tick (stage (0x452F20), pay (0x4532F0, 0x453370)) | with a resistance margin a tick charges the plain cost **and** the cost plus the margin share | charge only the surcharged cost |
| MG4 | saving throw (resist (0x4537B0), fail (0x453B20, 0x453CC0)) | the resistance-derived term is **subtracted** from the save, against the manual's "+1 bonus on the save for each 10 % of resistance" | add it |
| MG5 | aptitude gate (check (0x4564E0)) | a tech-aligned target "nullifies" when `d100 ≤ target aptitude`, which is negative, so it never happens | use the magnitude, reduced by a magick-aligned caster's aptitude |
| MG6 | time scaling (time (0x455250)) | the aptitude difference is an absolute value, then tested for `< 0`, so the duration/maintenance scaling by aptitude never applies | an unknown signed scaling |
| MG7 | Interrupt component (interrupt (0x452300), stop (0x456F70, 0x456FA0, 0x457000, 0x457030, 0x457060, 0x4570E0, 0x457100, 0x457110, 0x4571E0, 0x457270, 0x4573D0, 0x457450, 0x4574D0, 0x457530)) | builds an invocation without source or parent, then searches only when the source is set and matches the (empty) parent: never interrupts anything | interrupt the named spell on the target |
| MG8 | summon (list (0x452830, 0x452900), summon (0x450C40)) | `Clear_Faction:` is parsed but the faction switch is fed `List:`: a fixed prototype (list −1) or lists 1..5 get faction 0 and no alignment copy; list 0 keeps the caster's | use `Clear_Faction:` |
| MG9 | summon lists 0..2 (list (0x452830, 0x452900)) | the weight roll is 0..100 inclusive over tables summing to 100 (lists 0, 2) or 120 (list 1): a roll of 100 falls off the end to the first entry with an error log; the cow of list 1 needs exactly 100 | roll 0..99 over consistent weights |
| MG10 | polymorph to an elemental (trait (0x452AD0, 0x452B00, 0x452CD0, 0x452D80, 0x452E40, 0x452ED0)) | critter flags 2 and blit flags are **assigned** a single flag, wiping the others (summoned "no pickpocket" among them); the restore clears only that bit | OR the flags in |
| MG11 | maintained-slot predicate (slots (0x450AC0, 0x450B40, 0x450B90), AI (0x4AE720)) | the counter of maintained spells is an empty loop returning 0, so the AI's "no free maintenance slot" refusal fires only below intelligence 4; the real cap is applied later, after BEGIN | count the caster's maintained runs |
| MG12 | maintain bar (UI (0x57C290, 0x57C320, 0x57C370, 0x57C3F0, 0x57C470, 0x57C4B0)) | with every slot taken the "no candidate" value −1 passes the `≥ count` test and writes slot −1; `INT / 4` above 5 indexes past five slots; latent while the run-time cap equals `INT / 4` | refuse when no slot is free; cap at 5 |
| MG13 | fate "save against magick" (resist (0x4537B0)) | sets a margin of 10 (and a display value of 100); the target still rolls its save | a full save (margin 100); low confidence |
| MG14 | fail-chance predictor (fail (0x453B20, 0x453CC0)) | differs from the resolver (R / 10, 100 % above R 40, no margin) and consumes a d20 | a pure estimate that matches the resolver |
| MG15 | reflection cost (reflect (0x455550)) | the reflection caster pays "the last cost charged" from a global, stale for free or item casts | the reflected spell's cost |
| MG16 | no-stack refusal (stage (0x452F20)) | the BEGIN cost is paid before the no-stack test, so a refused recast is paid | test before paying; low confidence that it is a slip |
| MG17 | fate maximisation (process (0x4510F0)) | evaluated on every action with components, so an END can be maximised when its BEGIN was not and then removes more scaled effect entries than were added | capture once at BEGIN |
| MG18 | target names (target table 0x596140) | `Tgt_Obj_No_ST_Critter_Dead` lacks the critter type bit that its siblings carry (flagged in the earlier reading) | add the bit |
| MG19 | unresistable runs (flags (0x455C30), schedule (0x453FA0)) | one run flag means both "unresistable" (`No_Resist:`, script unresistable casts) and "final END scheduled"; the scheduler tests it before maintenance and duration, so an unresistable spell ends right after BEGIN whatever its `Duration:` or `Maintain:` | separate flags; an unresistable spell keeps its duration (the census sizes which records combine them) |
| MG20 | mana stores (pay (0x450420)) | worn mana stores are drained and their recharge scheduled before the fatigue refusal (≤ −15), so a refused cast still costs the stored mana | test fatigue before draining; low confidence that it is a slip |

**Reconstruction changes in these paths** (marked as deliberate in the
earlier reading; the original may differ here, see O10): the shell and reflection check moves
the self-target test below the anti-magic shell handling (so an
interrupted spell that never began runs no END) and lets runs dispelled by the
shell, or cast before a reflection shield appeared, finish their END (reflect (0x455550)); the
charm reaction bonus (+30, removed on END) bypasses the effect list so it can
be reversed (charm (0x454A10)); a summoned monster's own palette becomes its
base art so transformations can be undone (summon (0x450C40)).

Not a slip but a risk for the effects owner: the Effect component removes
the **first** entry with that effect number, whatever its cause, so an END can
remove an identical effect that an item or background added
(effect (0x4E9F70, 0x4EA100)).

## Open questions

- **O1** Retail parity of every constant and formula above (NOT_VERIFIED).
- **O2** What records 80..139 are, and which records use `Scaled`, `Full`,
  `Resurrect`, `DurationStatInfo:`, `DurationTriggerCount:`, `Resist:` stats,
  `ChargesFromCells:`, `ChargeBeginCost:` and `Disabled:`: a data census
  question (below).
- **O3** The target module was read only along the magic paths; cone, count
  and radius doubts flagged in the earlier reading stay open for FR-MAGIC-003 acceptance.
- **O4** How spell timers behave in turn-based mode (the game clock advances
  one second per round per ARC-RES-030) was not traced.
- **O5** Technology records: charge-cell use, tech degrees and the
  `ChargeNBranch` pattern belong to a TECHNOLOGY research item.
- **O6** Item triggers (the item spell functions 0x4CB720–0x4CC160, FR-MAGIC-011) and the spell visual
  catalogue (`SpellEyeCandy.mes`, the missile and casting art) were not read.
- **O7** The effect list semantics (the effect functions 0x4E99F0–0x4EABF0) belong to the durable-effects
  owner (top-20 item 17); this reading used only add and remove. Answered by
  ARC-WORLD-029 ([durable effects](../world/durable-effects-runtime.md), ledger rows
  EF1–EF10): the original matches the earlier reading.
- **O8** Whether the anti-magic shell and reflection are meant to stop
  technology too (the code does not test the flag).
- **O9** A comment of the earlier reading says the original omits equipment magic resistance
  adjustments in these calls, yet the called resistance function adds worn
  armour adjustments; which one the original does is unverified.
- **O10** What the original does where the reconstruction deliberately changed
  behaviour (the list of reconstruction changes after the anomaly table).

## What AI slice I8 needs from this domain

Slice I8 of the [NPC combat AI plan](npc-combat-ai-plan.md#phase-1-faithful-reconstruction)
(support and magic, after stage A5) calls these magic functions
(eligibility (0x4AE720), cast (0x4AC250, 0x4AC320, 0x4AC350)):

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
original game gets a switch of the Original setting; for a reconstruction slip both
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

An earlier source reading (provenance in git history), covering, by executable
address, the magick/tech functions
(0x44EF50–0x45A760: tables, getters, payment, process, all 25 component
functions, stage checks, resistance and predictor, scheduling, run slots,
invocation, check, line of fire, interrupts, parse of record lines +1, +3, +4
and components, influence queries, timers, recharge, death hooks), the spell
functions (0x4B1440–0x4B1CF0), the spell UI functions (0x57BB30–0x57C670:
apply, maintain bar, learning), and the called parts of the stat, object,
item, critter, combat, AI, animation, script, dialog, fate, effect, target,
location and time-event functions. Steamwoven inputs are the requirements and
the research and runtime documents linked above. Evidence level F0 unless a
ledger row is cited. Names in this document are descriptive.



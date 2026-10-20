# NPC AI support and magic

Status: **Research**, work item ARC-RES-059 (2026-10-20), stage A5 of the
[NPC combat AI plan](npc-combat-ai-plan.md#research-stages-source-reading-one-document-each)
(source units S16 heal and support, S17 spell and item choice, S18 items,
weapons and grenades). It fixes, for one NPC, whom it heals and with what, how
its ten AI action lists of spells and magick items are built, sorted and cached,
how an entry is chosen and every reason an entry is refused, when it looks for
ammunition, weapons and armour on the ground, and when it throws a grenade. It
settles the plan's anomalies P1, P4 and P7. It changes no code; the rules are
inputs for slice I8 (support and magic) of the plan.

Earlier facts reused here, not re-derived: the [core loop](npc-ai-core-loop.md)
(ARC-RES-054: the pass record, the gate and the pipeline, the danger states, the
[parameter dictionary](npc-ai-core-loop.md#4-parameter-dictionary), whose slots
13, 14 and 15 are read here), the [line of fire](npc-ai-awareness.md#5-line-of-fire-s8-combat-o4)
and the perception distance of the [awareness](npc-ai-awareness.md) study
(ARC-RES-056), the [protect rule](npc-ai-hostility.md#42-protect) and the danger
update of the [hostility](npc-ai-hostility.md) study (ARC-RES-057), the flee
spell step of the [morale](npc-ai-morale.md#1-the-flee-decision) study
(ARC-RES-058), the [best items](../world/equipment-runtime.md#best-items-arc-world-034)
of the equipment runtime (ARC-WORLD-034, ledger WB1–WB8), the installed `AI_*`
values and item AI actions of the [data census](combat-data-census.md) (ARC-RES-041,
sections [2](combat-data-census.md#2-ai-parameter-rows),
[3](combat-data-census.md#3-spell-records) and [5](combat-data-census.md#5-prototypes))
and the magic predicates listed for slice I8 by the
[magic core](magic-core-evidence.md#what-ai-slice-i8-needs-from-this-domain)
(ARC-RES-043, ledger MG11).

## Method

Behaviour comes from the retail executable (`Arcanum.exe` 1.0.7.4, SHA-256
`9bd80511…78d52`, re-hashed by both readers), read statically with `llvm-objdump`
by a first reader and, independently, by a second reader that saw only the
questions ([method step 6](original-executable-check.md)). Object field numbers
were named from the executable's own field table (0x59BEA8); flag bits, object
types and type masks, stats, skills, colleges, prototype numbers and struct
offsets come from the project's records of the executable's constants. The rows
and verdicts are in the [ledger](original-verification-ledger.md#npc-ai-support-and-magic-arc-res-059).
Everything below is read in the executable unless marked **(inference)**; the
functions of other stages and domains that these call (perform, the animation
goals, the magic predicates) are named, not re-read.

Conventions: the pass record holds the NPC, a target (at the start of the pass
the danger source), the danger state, the action type (0 none, 1 cast, 2 use
item, 3 use skill), the spell, the skill and the item
([core loop section 3](npc-ai-core-loop.md#3-one-pass)). HP% and fatigue% are
`current × 100 / maximum`, signed 32-bit, truncated, with no zero guard (0x4AB400,
0x4AB430; fatigue% is high when the critter is rested). "Leader" is the NPC-leader
field (0x45DE00), "PC leader" the PC at the top of the leader chain (0x45DDA0),
"followers" the follower list with followers of followers (0x441260). "In
combat mode" is the critter flag bit (0x4B3D50); "in danger" means a danger state
other than NONE. Distances are in tiles (0x441AE0). Parameter numbers are the
slots of the dictionary.

## Answer

1. **Heal runs first and heals the party, nearest in rank first (S16).** A
   sleeping or fleeing NPC heals nobody. Otherwise it tries itself, then its
   leader (its PC leader, else its NPC leader), then every follower of that
   leader (its own followers when it has no leader), and stops at the first
   target it acts on. In combat mode a target above 30 % HP is considered only
   with parameter 14 % chance per pass ([section 1](#1-the-heal-ladder)).
2. **The ladder per target.** Mechanical critters are never healed (P4, the
   original's behaviour). A dead target gets the resurrect list. Out of danger:
   below 90 % HP the heal skill with a healing item; then heavy healing below
   40 %, medium below 55 %, cure poison when poisoned, light healing below 90 %.
   In danger: the fatigue-recovery list while the **healer's** fatigue is below
   20 %, then heavy below 40 %, medium below 55 %, cure poison, light below 70 %.
3. **Spells in a fight (S17).** When the danger update finds a target, a
   non-invulnerable NPC (with the global "NPC fighting" switch on) rolls a d100
   against a chance of 25 % (50 % when parameter 15 > 1), which is 0 while it has
   a leader, both are above 50 % HP and the target is not clearly stronger. On a
   hit it tries the summon list when it has no followers, then the defensive
   list when parameter 13 < d100 (a `100 − parameter 13` % chance, P7, the
   original's behaviour), then the offensive list ([section 2](#2-spell-and-item-choice-in-a-fight)).
4. **Ten lists, items or spells (S17).** A list holds the critter's known spells
   with a value for that action, highest value first, or the spells of its magick
   items and its items whose AI action is that list, in inventory order. In
   combat mode at or below 50 % fatigue it takes items, and spells only when no
   item qualifies; otherwise spells, and items only when no spell qualifies. A
   per-critter cache of 40 slots keeps only the **first** list a critter builds,
   until its inventory changes ([section 4](#4-the-ten-action-lists)).
5. **The chooser and the refusal codes.** With 79 % chance the chooser starts at
   the first (highest) entry, otherwise at a random one, and walks the list
   circularly. An item entry without a spell is used at once, unchecked. Other
   entries are skipped when the target is already under that spell or the
   eligibility test refuses: 1 unknown spell, 2 too tired or no charge, 3 too
   little intelligence, 4 not the highest spell of its college (cast-highest
   NPCs), 5 no line of fire, 6 no maintenance slot, 7 too little willpower, 8 a
   fail chance of 38 % or more ([sections 3](#3-the-chooser) and
   [5](#5-eligibility)).
6. **Items on the ground (S18).** When its look-for flags are set (out of
   ammunition sets ammunition and weapon; an NPC that drops a weapon or armour
   looks for another), a non-monstrous NPC that is awake, not blind and not
   blocked by the interface's callback (**(inference)** a window holding an
   object) picks up the **nearest** item of the kind within
   `clamp(Perception − 5, 1, 10)` tiles (P1: the earlier reading's "farthest" is a
   reconstruction slip), else the first such item from a junk pile in range; the
   new item makes it re-wield its best items on the next pass
   ([section 6](#6-items-on-the-ground)).
7. **Grenades (S18).** Once at the start of each fight and after each back-off,
   an NPC with any throwing skill and a grenade throws it at its target when the
   target is beyond the blast radius, nothing blocks the line, and no living NPC
   it protects stands in the blast; PCs are not checked ([section 7](#7-grenades)).
8. **Anomalies.** P1 is a reconstruction slip; P4 and P7 are the original's
   behaviour. Four more things look unintended in the original: the cache keeps
   the first list stale and rebuilds (and leaks) every other (SP11); the grenade's
   friendly-fire scan skips PCs, so a follower may blow up its PC leader (SP15);
   the enemy-list path of the danger update hands the spell's target, possibly
   the NPC itself, to the setter as the new focus (SP16); the fatigue rung tests
   the healer's fatigue for every target (SP17). A latent out-of-bounds read in
   the chooser (an item AI-action entry in the defensive list reads before the
   spell table, SP18) is fixed in both settings ([section 8](#8-anomalies-and-decisions)).

## 1. The heal ladder

**Heal (0x4A8940)**, the pipeline's first step after the gate. It returns
nothing chosen when the NPC is sleeping or its danger state is FLEE (SURRENDER
and COMBAT_FOCUS heal). The **combat argument** starts at 1; when the NPC is in
combat mode it becomes 0 if `random(1, 100) > parameter 14`. Targets, each
handed to the helper below with that argument, stopping at the first success:

1. the NPC itself;
2. its PC leader, or, without one, its NPC leader (the pass record's leader);
3. the followers of that leader (followers of followers included), the NPC
   itself skipped; when the NPC has no leader at all, its own followers.

**Heal helper (0x4A8AA0)**, for one target T. The lists are always the
**healer's** lists (its spells and items, [section 4](#4-the-ten-action-lists)),
and the chooser is called in mode 8 with T as the target
([section 3](#3-the-chooser)).

| # | Test | Effect |
| ---: | --- | --- |
| 1 | T has the critter flag MECHANICAL (0x20000000) | nothing (P4) |
| 2 | T is dead | resurrect list (9); whatever it gives, the helper ends |
| 3 | T's HP% > 30 and the combat argument is 0 | nothing |
| 4 | the NPC's danger state is NONE and T's HP% < 90 | the heal skill (10) with the first healing item of the NPC (skill supplementary item 0x4C91F0), when the skill check (0x4AE570) accepts: the item is a generic item with the healing flag, T is a PC or NPC and neither undead nor mechanical. The pass records action 3 (skill 10), the item and T, and the helper ends |
| 5 | the NPC is in danger (not NONE) and **the NPC's own** fatigue% < 20 | fatigue-recovery list (8) on T (SP17) |
| 6 | T's HP% < 40 | heavy healing list (6) |
| 7 | T's HP% < 55 | medium healing list (5) |
| 8 | T's poison level (stat 24) > 0 | cure-poison list (7) |
| 9 | T's HP% < 70, or the danger state is NONE and T's HP% < 90 | light healing list (4) |

A list step that finds an entry ends the helper with success; otherwise the next
step runs. So, with the combat argument, parameter 14 is the per-pass chance (in
combat mode) of also caring for targets above 30 % HP; installed values
([census](combat-data-census.md#2-ai-parameter-rows)): 100 or 2001 in 12 rows
(always), 0 in 8 rows (only critically hurt targets), 10 to 50 in the rest. The
argument does not filter the resurrect step, which comes before it.

Worked example: an NPC in COMBAT_FOCUS at 60 % HP, rested, not poisoned, with
parameter 14 = 20: with 80 % chance per pass the argument is 0 but 60 > 30, so
it does nothing for itself; with 20 % chance it reaches step 9 and, since 60 <
70, tries the light healing list. When that self attempt finds no entry (always
in the 80 % case), its leader at 25 % HP is tried next and reaches the heavy
healing list.

The heal skill step writes the action without testing the NPC's heal skill
level; the skill's own rules run when perform executes it (stage A6). The fatigue
list in danger is useful mostly to PC followers: the eligibility test refuses
every spell of an NPC without a PC leader below 20 % fatigue (refusal 2,
[section 5](#5-eligibility)), so such an NPC restores fatigue only with items.

## 2. Spell and item choice in a fight

**Spell choice (0x4ABC20)** runs at each of the four places where the danger
update has found a target for an NPC in NONE or COMBAT_FOCUS (scan, kept focus,
last attacker, enemy-list pick; [hostility section 1](npc-ai-hostility.md#1-the-danger-update)),
with that target in the pass record. An NPC with the object flag INVULNERABLE or
the critter flag NIGH_INVULNERABLE chooses nothing; when nothing is chosen the
pass's action type is set to 0.

**Chance (0x4ABE20).** 0 when the NPC has a leader, its HP% and the leader's are
both above 50 and the target is not stronger (0x4ABEB0 below is false); otherwise
50 when parameter 15 > 1, else 25. **Stronger (0x4ABEB0)**: the target is
stronger than the NPC when the target's level is at least the NPC's level + 10
(target below level 20), + 5 (target level 20 to 39) or + 0 (40 and above).

**Order (0x4ABC70).** Nothing while the global "NPC fighting" switch is off.
`random(1, 100) > chance` → nothing. Then, each step ending the choice when the
chooser finds an entry:

| # | Condition | List | Chooser mode (target) |
| ---: | --- | --- | --- |
| 1 | the NPC has no followers (0x45E3F0) | summon (1) | 1 (the NPC) |
| 2 | parameter 13 < `random(1, 100)` | defensive (2) | 4 (per spell: the pass target or the NPC) |
| 3 | always | offensive (3) | 2 (the pass target) |

So parameter 13 is a threshold to **skip** the defensive list: installed values
0 (12 rows, always tried), 50 (12 rows), 75 (2 rows, 25 %) and others between
([census](combat-data-census.md#2-ai-parameter-rows)) (P7). Parameter 15 is 4
or 5 in 15 rows (50 % spell chance) and 0 or 1 in 26 (25 %).

The flee list (0) is asked only by the flee spell step in state FLEE, in mode 1
([morale section 1](npc-ai-morale.md#1-the-flee-decision)).

## 3. The chooser

**Chooser (0x4ABF10)**, given the pass record and a request (mode, list,
target). An empty list chooses nothing. The target comes from the mode: 1 the
NPC, 2 the pass target, 8 the request's target; mode 4 decides per entry: the
pass target when the spell's second defensive value (spell record +0x154, the
value after `@` of `AI_Defensive:`, read by 0x4CC2A0) is not 0, else the NPC.
That read is made for every visited entry, before the spell −1 test of step 1,
and indexes the spell table by the entry's spell: for an item AI-action entry
(spell −1) it reads the 4 bytes just before the table, and that value decides
the target of the item use (SP18). Only an item whose AI action is the
defensive list (2) can reach it; the installed data has none
([census](combat-data-census.md#5-prototypes)), so the read is latent.

Start: `random(1, 100) < 80` → index 0; otherwise `random(0, n − 1)`. So the
first (highest) entry is the start with chance `0.79 + 0.21 / n`. Then each index
`(start + i) mod n` for `i = 0 .. n − 1`:

1. an entry whose spell is −1 (an item AI action, [section 4](#4-the-ten-action-lists))
   is used at once (item use on object 0x462CC0, the NPC, the item, the target;
   in mode 4 the target picked from the out-of-bounds read above, SP18) and the
   chooser reports success without writing the pass record: no eligibility, no
   line of fire, no influence test;
2. the target already under the influence of that spell (0x459380) → next entry;
3. the eligibility test ([section 5](#5-eligibility)) refuses → next entry;
4. otherwise the pass records the spell, the item (or none) and the target, with
   action type 2 (use item) when there is an item that passes 0x4CC160, else 1
   (cast, also for an item that fails 0x4CC160); success.

0x4CC160 passes an item with a non-empty mana store whose first spell slot is
empty, or whose first spell has item triggers, or that has the valid-AI-action
item flag. The item's type is read and discarded on the cast-with-item path.
Because a step-1 item use writes nothing, the pass's action stays as it was
(none), so perform then acts by the danger state in the same pass
([core loop section 3](npc-ai-core-loop.md#3-one-pass)); whether the item use
itself costs time or action points is stage A6's question (U2).

## 4. The ten action lists

**Spell values (0x457E70).** Line +4 of a spell record carries `AI_Flee:`,
`AI_Summon:`, `AI_Defensive:`, `AI_Offensive:`, `AI_HealingLight:`,
`AI_HealingMedium:`, `AI_HealingHeavy:`, `AI_CurePoison:`, `AI_FatigueRecover:` and
`AI_Resurrect:` for lists 0..9 (an absent key leaves −1, [census source fact](combat-data-census.md#3-spell-records)).
`AI_Defensive:` is a pair `(value@second)`; without `@` the second value is the
same as the first (0x531B70). The census reads ten defensive spells with second
value 1 and the rest with 0, so the installed data writes the pair in full
(SP9).

**Global lists (0x4CC1F0, 0x4CC240, 0x4CC350, 0x4CC380).** At init, for each
list, every spell 0..222 with a value other than −1 is entered (spell, no item)
and the entries are sorted by value, **highest first** (comparator 0x4CC2D0, the
runtime's sort 0x58A548). The order of equal values is whatever that sort makes
of the input order (U1). The installed sets are in the
[census](combat-data-census.md#3-spell-records): 7 flee, 16 summon, 25 defensive,
32 offensive, 7/3/1 light/medium/heavy healing, 3 cure poison, 1 fatigue
recovery, 2 resurrect.

**Building a critter's list (0x4CCA90 → 0x4CC580).** Only for a PC or NPC.

1. **Items or spells.** In combat mode with fatigue% ≤ 50: items first, spells
   only when no item entry results. Otherwise spells first, items only when no
   spell entry results. So each list is all items or all spells.
2. **Spells (0x4CC8A0).** Only with magick points (stat 22) above 0: the global
   list's spells that the critter knows, in the global order.
3. **Items (0x4CC810, 0x4CC930).** When the wielded weapon is hexed, only that
   weapon; otherwise every inventory item in inventory order. For one item:
   - when its mana store is not empty and it passes the identification rule
     (0x4CCA30: identified, or the critter has no PC leader, or the item is food
     or a scroll), each of its five spell slots with a spell that has a value for
     this list and that the critter may use (0x456A10: a critter with negative
     aptitude only while the item's effective power for it is above 0) gives an
     entry (spell, item), in slot order;
   - independently, an item with the valid-AI-action flag whose AI action is this
     list gives an entry (−1, item).
   Items are not sorted. The installed data has seven AI-action items (healing
   light, two heavy, cure poison, two fatigue recovery, one offensive) and 155
   items with spells ([census](combat-data-census.md#5-prototypes)).
4. **Limit.** At most 200 entries; more are dropped.

**Cache (0x4CC6F0, 0x4CC770, 0x4CCB90, 0x4CCC00, 0x4CCC40).** Forty slots keyed
by critter. A request finds the critter's slot and copies its lists:

| Case | What happens |
| --- | --- |
| no slot | a free slot, else the next slot in round-robin order (its lists freed), is taken for the critter; the requested list is built **into the slot** and copied out |
| slot, requested list not empty | the slot's list is used as it is, however old |
| slot, requested list empty | the list is built into the caller's copy only; the slot keeps it empty, so the next request builds it again |

So a slot ever holds only the list that was requested when it was created, and
that list is frozen (items or spells, the fatigue and combat mode at that time,
the spells known then, the PC leader for the identification rule) until the
slot is cleared: when an item is inserted into or force-removed from the critter
(0x4CCC00 from item insert 0x466640 and item force-remove 0x467860, call at
0x467BF1), when one of the slot's listed items runs out of mana (0x4CCC40 from
0x450420, call at 0x45062B), at a reset, or when round-robin reuse takes
the slot. The lists built on the third path are never freed (the release
function 0x4CCBF0 does nothing) (SP11). Which list is first depends on the
pass: in a fight it is often the summon or offensive list of the danger update,
or a heal list.

## 5. Eligibility

**Eligibility (0x4AE720)**, given the critter, the item (or none), the target
(or none) and the spell. It returns 0 (accepted) or the first refusal below, in
this order. The college is `spell / 5`; college 12 is white necromancy (the
healing college).

Without an item (a spell of the critter):

| # | Condition | Code |
| ---: | --- | ---: |
| 1 | the critter does not know the spell | 1 |
| 2 | an NPC (not a PC) without a PC leader whose fatigue% < 20 | 2 |
| 3 | an NPC, the spell is maintained (maintenance period above 0, 0x4503A0) and the free maintenance count is 0 (0x450B40: intelligence / 4 minus the running maintained spells, a count that is always 0 in this build, MG11; so only intelligence 0..3) | 6 |
| 4 | the spell's cast cost (0x4B1660) ≥ the critter's current fatigue | 2 |
| 5 | intelligence < the spell's minimum intelligence | 3 |
| 6 | willpower < the spell's minimum willpower | 7 |
| 7 | the critter has the NPC flag CAST_HIGHEST and the spell is not `5 × college + level in that college − 1` | 4 |
| 8 | no target | accepted |
| 9 | unless college 12 and the target's HP% ≤ 30: the cast fail chance (0x453B20) ≥ 38 | 8 |
| 10 | the target is not the critter and the [line of fire](npc-ai-awareness.md#5-line-of-fire-s8-combat-o4) to its tile costs ≥ 100 or reports a blocker other than the target | 5 |

With an item (a spell of a magick item):

| # | Condition | Code |
| ---: | --- | ---: |
| 1 | an NPC, the spell is maintained and the free maintenance count is 0 | 6 |
| 2 | the item's mana store is empty | 2 |
| 3 | no target | accepted |
| 4 | unless college 12 and (the target's HP% ≤ 30 or the critter's fatigue% ≥ 80): the item use fail chance (0x453CC0) ≥ 38 | 8 (SP8) |
| 5 | the line-of-fire test as above | 5 |

The item path skips the known-spell, fatigue, cost, intelligence, willpower and
cast-highest tests. Only a zero maintenance count refuses; a negative one would
pass (the magic domain's own test 0x450BB0 is the same). An unreferenced entry at
0x4AE6F0 calls this test without an item; nothing in the executable calls it.

Reason codes for slice I8: `CAST_REFUSED_UNKNOWN` (1), `CAST_REFUSED_TIRED` (2,
also no charge), `CAST_REFUSED_INTELLIGENCE` (3), `CAST_REFUSED_NOT_HIGHEST` (4),
`CAST_REFUSED_LINE_OF_FIRE` (5), `CAST_REFUSED_MAINTENANCE` (6),
`CAST_REFUSED_WILLPOWER` (7), `CAST_REFUSED_FAIL_CHANCE` (8), plus the chooser's
`CAST_SKIPPED_UNDER_INFLUENCE`.

## 6. Items on the ground

**Look for item (0x4A8E70)**, the pipeline step after heal. Nothing when the NPC
sleeps, is blinded, or the interface's callback (slot 0x12C of the interface
callback table, 0x460C20) reports an object (**(inference)** a window of the
interface holding an object). Then, in this order, each flag cleared just before
its search whatever the result, the first search that starts a pickup ending
the step (later flags stay set for later passes):

| NPC flag | Search for |
| --- | --- |
| LOOK_FOR_AMMO | ammunition (any type) |
| LOOK_FOR_WEAPON | weapons |
| LOOK_FOR_ARMOR | armour |

**Search (0x4A8F90).** Nothing for a monstrous critter (undead, animal, monster
or mechanical). Range `r = clamp(Perception − 5, 1, 10)` (the
[perception distance](npc-ai-awareness.md#1-perception-distance-s6)), a square
of half-size `r` around the NPC.

1. Loose items of the kind in the square, skipping those with the item flags
   NO_DISPLAY or NO_NPC_PICKUP, worth 0, or that the NPC cannot take
   (0x466510). The **nearest** wins; on equal distance the first in query order
   stays (P1). A pickup goal (0x4356C0) on it ends the search.
2. Otherwise junk piles (containers of description 3023) in the square, in query
   order: the first item of the kind in each pile (only that one), with the same
   filters plus the object flag OFF; the first pile whose item gets a pickup goal
   ends the search.

Ammunition is any ammunition, weapons any weapon, armour any armour: the search
does not match a weapon's ammunition type or a slot.

**Who sets the flags.** A ranged attack by an NPC that cannot consume ammunition
sets LOOK_FOR_AMMO, LOOK_FOR_WEAPON and CHECK_WEAPON (0x4ADFF0, from 0x4B3170 at
0x4B3284, after the out-of-ammunition sound). An NPC that drops a weapon gets
LOOK_FOR_WEAPON, one that drops armour LOOK_FOR_ARMOR (item drop 0x461AB0).

**Wield best.** Every item inserted into an NPC sets CHECK_WIELD (item insert
0x466640). The next pass's gate then wields the best item for every slot
(0x4654F0), or with only CHECK_WEAPON the best weapon (0x465170), passing the
pass's target, the danger source, so a weapon in range of it is preferred
([core loop gate step 14](npc-ai-core-loop.md#3-one-pass); the choice rules are
[WB1–WB8](original-verification-ledger.md#starting-kit-arc-world-034)).

## 7. Grenades

**Combat perform (0x4AC6E0)**, for an NPC in COMBAT_FOCUS with no chosen action
and no back-off: when parameter 15 > 1, the target is closer than parameter 15
and `random(1, 20) = 1`, it starts backing off (BACKING_OFF and CHECK_GRENADE)
([morale section 4](npc-ai-morale.md#4-back-off)); otherwise the grenade, and when
no grenade is thrown the attack goal (0x434B00, stage A6).

**Grenade (0x4AC910)**, given the distance d to the target:

| # | Test | Effect |
| ---: | --- | --- |
| 1 | no CHECK_GRENADE | no throw |
| 2 | BACKING_OFF | no throw; CHECK_GRENADE stays |
| 3 | — | CHECK_GRENADE cleared |
| 4 | no throwing skill points (basic skill 3) | no throw |
| 5 | no generic item with the grenade flag (the first one, 0x462760) | no throw |
| 6 | d ≤ the grenade's area radius R (0x466230) | no throw |
| 7 | the [line of fire](npc-ai-awareness.md#5-line-of-fire-s8-combat-o4) to the target's tile reports any blocker (the cost is ignored) | no throw |
| 8 | a living NPC in the square of half-size R around the target that the NPC [protects](npc-ai-hostility.md#42-protect) (itself, its leader, its faction, guard origin) | no throw; PCs are not scanned (SP15) |
| 9 | — | throw goal (0x434F80) at the target's tile |

CHECK_GRENADE is set by the danger setter when a fight starts and by each
back-off start ([core loop rule 7](npc-ai-core-loop.md#5-danger-state-machine)), so
an NPC considers one grenade per fight start and one after each back-off, and a
failed consideration (no skill, no grenade, too close) is not repeated. The
friendly-fire test is the only one in the AI; spells have none (plan phase 2,
M3).

## 8. Anomalies and decisions

Verdicts and rows:
[ledger](original-verification-ledger.md#npc-ai-support-and-magic-arc-res-059).
Decisions follow D2 and D8: a bug of the original gets a Steamwoven fix and a
proposed Original switch; where only the reconstruction differs, both settings
follow the original. The owner accepted every proposed Original switch of this
table on 2026-10-20 (ARC-RES-063).

| ID | Original 1.0.7.4 | Verdict | Proposed decision |
| --- | --- | --- | --- |
| P1 | The item search keeps the nearest candidate (strictly smaller distance; the first stays on a tie). | Reconstruction slip (confirms ARC-RES-048; the earlier reading kept the farthest) | Both settings follow the original; no switch. |
| P4 | The heal helper tests the target's critter flag MECHANICAL first, so AI never heals or resurrects a mechanical critter; the heal skill check refuses mechanical and undead targets too. | Original (confirms ARC-RES-048; the earlier reading's "no-attack" name is the same bit of another flag set, so the behaviour is the same) | Kept in both settings; no switch. |
| P7 | The defensive list is tried when parameter 13 < d100, a `100 − parameter 13` % chance; parameter 13 is a skip threshold. | Original (confirms ARC-RES-048) | Kept in both settings; the dictionary's d100-threshold reading of slot 13 stands (it is a threshold to skip the defensive list). Whether the original `ai_params.mes` header labels it as a chance is a data question; the installed 0 in 12 rows (always defensive first) reads naturally only as a threshold **(inference)**. No switch. |
| SP8 | An item spell of the healing college skips the fail-chance test when the target is at or below 30 % HP **or** the user's fatigue is at least 80 %; a cast spell has only the HP exemption. | Original (intent unclear) | Kept in both settings: a rested user may simply not care about failure **(inference)**; nothing marks it as a slip. No switch. |
| SP11 | The list cache stores only the list requested when a critter's slot was created and keeps it, frozen, until an inventory change, an exhausted listed item, a reset or round-robin reuse; every other list is rebuilt on each request and never freed. | Original bug | **(inference)** A cache meant to hold all ten lists, whose hit path writes the new list into the caller's copy instead of the slot; the stale list (items versus spells chosen by an old fatigue, spells known then) is a side effect, and the leak has no game effect. Steamwoven builds each list fresh on every request; Original keeps the 40-slot cache with its first-list rule (`AiListCacheKeepsFirstList`, owner decision 2026-10-20; transient state, never saved, so no schema change). The owner decided knowing that the fix changes which spells or items an NPC uses after its fatigue or spells change. Lands with slice I8. |
| SP15 | The grenade's friendly-fire scan asks for NPCs only (type mask NPC), so a PC in the blast never stops a throw: a PC's follower may throw at a target next to its PC leader. | Original bug | **(inference)** The protect rule it calls handles PC leaders (GROUP) and PC reputation (FACTION), which only matter if PCs are scanned. Steamwoven scans PCs and NPCs; Original keeps NPCs only (`GrenadeIgnoresPcsInBlast`, owner decision 2026-10-20). The owner decided knowing the visible change: followers stop throwing near their PC leader, and an NPC whose faction the PC's reputation grants also refuses to throw at that PC. Lands with slice I8. |
| SP16 | In the danger update's enemy-list path the setter receives the pass record's target after the spell choice, which the chooser overwrites with the spell's target: a summon or self-aimed defensive spell makes the NPC its own combat focus for that pass. The other three paths pass a saved copy of the found target. | Original bug | The paths differ only in this, and a critter is never a valid focus of itself ([hostility validity](npc-ai-hostility.md#2-target-validity)); the next pass finds the focus invalid and searches again. Steamwoven passes the enemy-list pick; Original keeps the overwritten target (`EnemyListFocusTakesSpellTarget`, owner decision 2026-10-20). Lands with slices I4/I8. |
| SP17 | The fatigue-recovery rung (in danger) is tried when the **healer's** fatigue is below 20 % but aimed at the heal target, also when that target is the leader or a follower; every other rung tests the target. | Original bug | The verdict rests on inference about intent only (as HS9's does). **(inference)** The rung reads as "restore your own fatigue", reached for other targets only because the helper runs for each of them; the self call comes first, so the leader or a follower gets it only when the self attempt was refused (already under the spell, a fail chance, the line of fire). Steamwoven tries the rung only when the target is the healer; Original keeps it for every target (`HealFatigueRungForOthers`, owner decision 2026-10-20). The owner chose this fix; the other fix, testing the target's fatigue, would also make healers restore a tired leader, a new behaviour. Lands with slice I8. |
| SP18 | In mode 4 the chooser reads the second defensive value (0x4CC2A0) for every visited entry before its spell −1 test; for an item AI-action entry that read indexes the spell table with −1 and takes the 4 bytes before it, which decide whether the item is used on the pass target or on the NPC. Only an item with AI action 2 (defensive) reaches it; the installed data has none. | Original bug (latent) | Both fixed (owner rule: Original never reproduces an out-of-bounds read): a spell −1 entry in mode 4 is used on the NPC itself, as a defensive spell with second value 0 would be (a defensive item is a self-help item **(inference)**, like the healing and fatigue items). No switch. Lands with slice I8. |

The other rows (SP1–SP7, SP9, SP10, SP12–SP14) are the original's behaviour and
are kept in both settings. SP9's pair default (the second defensive value copies
the first when `@` is missing) changes nothing with the installed data, which
writes both values.

## 9. Consequences for Steamwoven (project policy proposals)

1. **Slice I8** implements the heal ladder, the spell choice, the list build, the
   chooser and the eligibility test as pure functions over the NPC, its party,
   its inventory, its known spells, the target and the parameters, returning the
   reason codes of section 5 (and `HEAL_SKILL`, `HEAL_LIST_<n>`, `NO_HEAL`), with
   the magic predicates (fail chance, cast cost, maintenance, under influence,
   item power) supplied by the magic slices
   ([ARC-RES-043](magic-core-evidence.md#what-ai-slice-i8-needs-from-this-domain)).
2. **Rolls.** Heal combat argument (1..100), spell chance (1..100), defensive
   threshold (1..100), chooser start (1..100, then 0..n − 1) and back-off start
   (1..20) are labelled draws of the session random source, drawn exactly when
   the original draws (the heal roll only in combat mode, the chooser's start
   once for every non-empty list it is given).
3. **The global sort.** The order of equal `AI_*` values must match the original
   sort's result on the installed records; slice I8 either reproduces that sort
   or records the resulting order from a run (U1).
4. **Item search and grenades** need the map owner's square query in the
   original's order (as the hostility scan does), the pickup and throw goals of
   slice I7 (stage A6), and the item flags of the inventory owner.
5. **Data.** The list contents come from the spell records and item prototypes
   the census already reads; nothing new is stored in the session (the cache of
   SP11, in the Original setting, is transient).

## Unknowns

- **U1** The order of spells with equal values inside a global list is decided
  by the runtime's sort routine on the record order; it was not traced.
- **U2** What the immediate item use (0x462CC0) of an AI-action entry does in a
  round (action points, animation) and how perform runs a cast, an item or a skill
  (0x4AC250, 0x4AC320, 0x4AC350) are stage A6's questions.
- **U3** Who sets LOOK_FOR_ARMOR besides an armour drop (prototype data or
  scripts) was not searched beyond direct writes in the executable.
- **U4** How often each path decides in play (heal skill versus lists, grenades)
  is a runtime question for slice I11.

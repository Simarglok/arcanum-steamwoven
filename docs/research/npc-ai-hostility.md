# NPC AI hostility and targeting

Status: **Research**, work item ARC-RES-057 (2026-10-20), stage A3 of the
[NPC combat AI plan](npc-combat-ai-plan.md#research-stages-source-reading-one-document-each)
(source units S10–S13). It fixes, for one NPC, when another critter counts as an
enemy (kill-on-sight), whom the NPC protects and whom it refuses to attack, how it
scans for a target and picks between two, when a target stays valid, how the enemy
list and the target lock live and die, and what one attack sets off around the
attacker and the victim. It changes no code. The rules are inputs for
implementation slice I4 (hostility and targets) and, through the enemy list and the
lock, slice I3 (danger state); stage A5 reuses the [protect rule](#42-protect)
and stage A7 the follower fan-out.

Earlier facts reused here: the [core loop](npc-ai-core-loop.md) (ARC-RES-054: the
pass record, the danger state read and setter, the parameter dictionary, whose
slots 9, 10, 11 and 12 are read here), the [reaction runtime](../world/npc-reaction-runtime.md)
and its ledger rows (ARC-WORLD-026: the effective reaction and its AI variant, the
enemy list reading 0), the [follow consent](original-verification-ledger.md#ai-parameters-and-follow-consent-arc-data-033)
rows (ARC-DATA-033: the follow check's result codes) and the
[session random contract](../world/session-random-runtime.md) (the range draw).

## Method

Behaviour comes from the retail executable (`Arcanum.exe` 1.0.7.4, SHA-256
`9bd80511…78d52`, re-hashed by both readers), read statically with `llvm-objdump`
by a first reader and, independently, by a second reader that saw only the
questions ([method step 6](original-executable-check.md)). Object field numbers
were named from the executable's own field table (0x59BEA8); flag bits, object
types, stats, skills, social classes, script points and struct offsets come from
the project's records of the executable's constants. The rows and verdicts are in
the [ledger](original-verification-ledger.md#npc-ai-hostility-and-targeting-arc-res-057).
Everything below is read in the executable unless marked **(inference)**; the
functions of other stages that these call (sight, hearing, should-flee, spell
choice, the noise fan-out) are named, not re-read.

Conventions: "valid(N, X)" is the target validity test of [section 2](#2-target-validity);
"perceived" means the hearing test (0x4AF470) or, failing it, the sight test
(0x4AF260) returns 0 (both return 0 for "perceived", an excess distance or 1000
otherwise; stage A2); the hearing test's last argument is 1 for a candidate that is
not concealed and 0 for a concealed one. A handle's "leader" is the NPC-leader field
(0x45DE00), null for a PC or a leaderless NPC; its "PC leader" follows the leader
chain to a PC (0x45DDA0). Distances are in tiles (0x441AE0).

## Answer

1. **The danger update decides the target once per pass (S11).** A peaceful NPC
   takes the target of a fresh scan; a fighting NPC keeps its focus while it is
   valid, else unlocks and scans, else takes the last critter that hit it if
   valid, else a random valid enemy-list entry, else calms down. A target found
   by the scan is fought without a flee check ([section 1](#1-the-danger-update)).
2. **Validity is a 20-tile leash plus party rules (S11).** The target must be
   within 20 tiles of the NPC and of its leader, not its own leader, follower or
   fellow follower, not stoned; a target under the invisibility spell stays valid
   when the NPC detects invisibility, or when it lacks the object-invisible flag
   and is either not a PC or a PC attempting an attack or a spell or in combat
   mode; not a decoy for a smart
   NPC, and not a master prowler the NPC cannot see; an unconscious target stays
   valid only while no other target exists ([section 2](#2-target-validity)).
3. **The scan looks at a square, nearest first (S11).** The square has half-size
   `clamp(Perception, 1, 10)` around the NPC, holds PCs and NPCs (at most the
   first 50), sorted by distance with decoys first and unconscious critters last.
   Each perceived candidate is a target when it is kill-on-sight; a fighting NPC
   candidate whose enemy the scanning NPC protects against hands over that enemy;
   a dead NPC the scanning NPC protects hands over its last focus (avenge)
   ([section 3](#3-the-scan)).
4. **Three decision tables (S10).** Kill-on-sight returns FACTION, REACTION
   (parameter 10), ALIGNMENT (parameter 11), DECOY or GUARD in that order after a
   script veto and a surrender exception; protect returns SELF, GROUP (its
   leader), FACTION or ORIGIN (guards only); upset-attacking returns PARTY, GOOD
   (parameter 12), ORIGIN or FACTION ([section 4](#4-decision-tables)).
5. **Two candidates: a coin toss between two scores (S11).** After validity-like
   filters a d100 of 1..50 compares `level − distance`, 51..100 compares nearness;
   a tie keeps the current target ([section 5](#5-choosing-between-two-targets)).
6. **The enemy list holds teams; the lock pins an order (S12).** An attack adds
   the attacker's team (the PC and all its followers, or the single NPC) to the
   victim's list and, for an NPC attacker, the victim's team to the attacker's;
   peace removes them. The pick starts at a random entry and takes the first
   valid one. A PC's attack order locks the follower on its target until the
   target becomes invalid, the follower is told to stop, or it leaves combat mode
   ([section 6](#6-enemy-list-and-target-lock)).
7. **One attack fans out (S13).** The attacker's and the victim's followers join
   or complain, the victim records who hit it, turns, fills its enemy list,
   changes its reaction toward a PC attacker (parameter 9 for a follower, which
   may leave jilted; lowered to parameter 10 otherwise; −10 for an accidental
   hit) and decides to fight, flee or keep its target; nearby witnesses are told
   (stage A2). A redirect spell makes every NPC near its target treat the target
   as its attacker. Peace clears focus, last attacker and enemy-list entries and
   raises a PC's reaction to 50 ([section 7](#7-attack-event-fan-out)).
8. **Anomalies.** P3 is confirmed with two readings: the redirect tests the
   spell target's intelligence, not the candidates'; the earlier reading was
   wrong about what it reads, and the original's own test is recorded as a bug
   of the original **(inference)**: one fixed critter is tested inside a
   per-candidate loop. A new bug of the original: upset-attacking treats any
   leaderless target as the NPC's party when the NPC itself has no leader, so for
   a leaderless NPC the scan's ally help never targets a PC or a leaderless NPC,
   GUARD never targets a leaderless NPC aggressor, and the guard-versus-monster
   clauses are dead (HS9). Three quirks are recorded and kept
   ([section 8](#8-anomalies-and-decisions)).

## 1. The danger update

The danger update (0x4A92D0) runs in the pipeline when neither heal nor item search
chose an action ([core loop](npc-ai-core-loop.md#3-one-pass)). It works on the pass
record (NPC at offset 0, target at 8, danger state at 16, sound request at 48) and
stores the setter's returned state back into the record. A sleeping NPC skips it.
Whenever a target is found, the spell and item choice (0x4ABC20, stage A5) runs
before the setter is asked for COMBAT_FOCUS on that target with the record's sound
request.

| State | Steps, in order |
| --- | --- |
| NONE | target := find target (0x4AB460, [section 3](#3-the-scan)); null → nothing (no setter call); else spell choice, fight it. |
| COMBAT_FOCUS | (1) current focus (field 0x11B) if valid; else unlock the target lock and take find target's result (0x4ABBC0); non-null → fight it. (2) Else the who-hit-me-last field (0x11C) if valid → fight it. (3) Else the enemy-list pick (0x4AFA90, [section 6](#6-enemy-list-and-target-lock)); non-null → fight it. (4) Else the setter is asked for NONE with no target. |
| FLEE | the fleeing action helper (0x4AAF50, stage A4); the state is not changed here. |
| SURRENDER | when the NPC's HP ratio (0x4AB400) is at least 80 and `random(1, 500) = 1`, the setter is asked for NONE. Otherwise nothing. |

So a target from the scan or the fallbacks never passes the should-flee test; the
flee test runs only from the reaction to an attack ([section 7.4](#74-reaction-to-an-attack)).
The surrender roll is drawn only at 80 % HP or more.

## 2. Target validity

Target validity (0x4AB990, arguments: NPC, candidate) returns valid or invalid.
The tests run in this order; the first that fails makes the candidate invalid.

| # | Test | Applies to |
| ---: | --- | --- |
| 1 | the candidate is null | all |
| 2 | object flags destroyed, off, don't-draw or invulnerable (mask 0x400103) | all |
| 3 | the candidate is the NPC itself | all |
| 4 | dead | PC, NPC |
| 5 | unconscious: invalid when the NPC fights non-lethally (critter flag 0x10000000); otherwise invalid when find target returns a different non-null critter | PC, NPC |
| 6 | the candidate is the NPC's leader | PC, NPC |
| 7 | the candidate's leader is not null and equals the NPC's leader | PC, NPC |
| 8 | the candidate's leader is the NPC (its follower) | PC, NPC |
| 9 | spell flag "stoned" (0x10000) | PC, NPC |
| 10 | invisibility (0x45A580): the candidate has the invisible spell flag, the NPC does not detect invisibility (spell flag 0x200), and either the candidate's object flag "invisible" (0x200) is set or the candidate is a PC that is neither in an attack attempt (goal 19) or a spell attempt (goal 27) nor in combat mode | PC, NPC |
| 11 | the NPC's intelligence is 5 or more and the candidate is the clockwork decoy (name 6719) | PC, NPC |
| 12 | a wall (type 0); or a non-critter that is busted (0x43F630) | other objects |
| 13 | farther than 20 tiles from the NPC | all |
| 14 | the NPC has a leader and the candidate is farther than 20 tiles from that leader | all |
| 15 | concealed, prowling training master (skill 6 at 3 or more), and not seen by the NPC | PC, NPC |

Test 5 calls find target, which refuses to run inside itself (a re-entrancy flag,
0x5F8498): inside a scan, an unconscious candidate is always valid; outside, it is
valid only while the scan finds nothing else (HS21). A non-critter target (a door,
a container) stays valid until it is busted; the 20-tile leash still applies.

## 3. The scan

**Find target (0x4AB460, argument: NPC).**

1. Nothing (null) when a scan is already running, or the NPC is invulnerable
   (object flag), nigh-invulnerable (critter flag 2 bit 0x200) or the decoy.
2. **Area.** The tiles from `(x − r, y − r)` to `(x + r, y + r)` around the NPC's
   location, both edges included, `r = clamp(Perception, 1, 10)` (0x4AF240,
   0x4AE4E0 → rectangle query 0x440B40); PCs and NPCs only (type mask bits 15
   and 16), dead ones and the NPC itself included. A box of 3×3 to 21×21 tiles.
3. **Cap.** At most the first 50 critters of the rectangle query's order (sector by
   sector, the most recently placed first: [round order row](original-verification-ledger.md#npc-ai-reaction-and-companions-arc-res-039-arc-res-028-arc-res-024))
   are kept, before sorting (HS20).
4. **Order.** With two or more, each gets a key: 0 when it is a decoy for this NPC
   (alive, NPC intelligence below 5, name 6719), else its distance; plus 1000 when
   it is unconscious. A stable insertion sort by ascending key: equal keys keep
   the query's order.
5. **Per candidate, in order:**

| # | Step | Result |
| ---: | --- | --- |
| a | not perceived | go to step f |
| b | a concealed PC | remembered (the last one perceived) |
| c | kill-on-sight ([4.1](#41-kill-on-sight)) not NO | **target: the candidate** |
| d | not an NPC | go to step f |
| e | an NPC in COMBAT_FOCUS, FLEE or SURRENDER whose danger source S (focus, or the critter it flees) is valid for the scanning NPC, and either protect(scanner, candidate) is not NO, or the scanner is a guard (social class 5) and S is monstrous and leaderless; then upset(scanner, S, scanner's leader) must be NONE and S perceived | **target: S** (ally help); any failed condition goes to step f |
| f | avenge (0x4AE450): the candidate is a dead NPC, its focus field F is not null and valid for the scanner, and protect(scanner, candidate) is not NO; F perceived | **target: F**; otherwise the next candidate |

6. **None found.** When a concealed PC was perceived, the NPC turns to face it
   (rotation goal toward the last remembered one). Result: null.

Step f runs also for a candidate the NPC does not perceive (a dead ally out of
sight still points at its killer, who must then be perceived).

## 4. Decision tables

Each table is a pure function of the named critters; the first matching row wins.
The result names are the project's reason codes (plan design rule 4).

### 4.1 Kill-on-sight

Kill-on-sight (0x4AE120, arguments: NPC, candidate), reasons 0 NO, 1 FACTION,
2 REACTION, 3 ALIGNMENT, 4 GUARD, 5 DECOY.

| # | Condition | Result |
| ---: | --- | --- |
| 1 | the candidate is not valid for the NPC ([section 2](#2-target-validity)) | NO |
| 2 | the NPC's will-kos script (attachment point 22, triggerer the candidate, attachee the NPC) returns 0 | NO (veto) |
| 3 | the NPC is in SURRENDER and its danger source is the candidate | NO |
| 4 | the NPC has a PC leader | skip to row 10 |
| 5 | NPC flag KOS (0x100), and the candidate is enshrouded (spell flag 0x20000000) while the NPC is undead (critter flag 0x4), or animal-enshrouded (critter flag 0x40000000) while the NPC is an animal (0x8) | NO |
| 6 | NPC flag KOS, no KOS override (NPC flag 0x800), and faction-same(NPC, candidate) false | FACTION |
| 7 | the candidate is a PC and the AI reaction variant (0x4C0CE0) of the NPC toward it is at most parameter 10 | REACTION |
| 8 | `|alignment(NPC) − alignment(candidate)|` at least parameter 11 (any candidate, PC or NPC) | ALIGNMENT |
| 9 | the candidate is a decoy for the NPC (alive, NPC intelligence below 5, name 6719) | DECOY |
| 10 | the candidate is an NPC in COMBAT_FOCUS on a non-null target V, and either protect(NPC, V) is not NO, or the NPC is a guard and the candidate is monstrous and leaderless; and upset(NPC, candidate, NPC's leader) is NONE | GUARD |
| 11 | otherwise | NO |

Notes. The AI reaction variant floors the race and reaction-modifier part at −49
before adding reputation, and a PC on the NPC's enemy list reads 0
([reaction R1](npc-reaction-reputation-evidence.md)); parameter 11 is 2001 in every
installed row, so ALIGNMENT never fires on shipped data
([census](combat-data-census.md#2-ai-parameter-rows)). Being attacked clears the
victim's KOS override ([section 7.1](#71-attack-event)). A PC follower kill-on-sights
only through GUARD. Callers: find target and two dialog functions (0x412FD0,
0x413130; stage A8).

### 4.2 Protect

Protect (0x4AE3A0, arguments: protector P, protected Q), reasons 0 NO, 1 FACTION,
2 ORIGIN, 3 GROUP, 4 SELF. This is the rule stage A5 reuses.

| # | Condition | Result |
| ---: | --- | --- |
| 1 | P is Q | SELF |
| 2 | P's leader is not null and is Q | GROUP |
| 3 | P has the mind-controlled spell flag (0x8000000) | NO |
| 4 | faction-same(P, Q) | FACTION |
| 5 | P is a guard (social class 5, field 0x128) and origin-same(P, Q) | ORIGIN |
| 6 | otherwise | NO |

It does not test whether Q is P's follower. Callers: find target (ally help),
avenge, kill-on-sight's GUARD row, the witness rule (0x4A9F10, stage A2) and the
grenade's friendly-fire test (0x4AC910, stage A5).

**Faction-same (0x45D110, arguments: A, B).** Faction is the NPC field 0x124 (0
for a non-NPC); a faction of 0 matches nothing.

| A, B | Same when |
| --- | --- |
| both PCs | never |
| A PC, B not | B's PC leader is A; else B's faction is not 0 and A's reputation grants it (0x4C21E0) |
| A not PC, B PC | the mirror: A's PC leader is B; else A's faction is not 0 and B's reputation grants it |
| neither a PC | the same non-null PC leader; or A's leader is B; or B's leader is A; or the same non-null leader; else the same non-zero faction |

**Origin-same (0x45D320).** Both origins (field 0x123, 0 for a non-NPC) equal and
not 0. **Monstrous (0x45F730).** A PC or NPC with any of the critter flags undead,
animal, monster or mechanical (mask 0x2000800C). **Guard.** Social class 5.

### 4.3 Upset attacking

Upset attacking (0x4ADCC0, arguments: NPC, target T, leader L): would the NPC
refuse to attack T on behalf of L. Reasons 0 NONE, 1 GOOD, 2 ORIGIN, 3 FACTION,
4 PARTY.

| # | Condition | Result |
| ---: | --- | --- |
| 1 | the NPC is mind-controlled and the controller found by its spell (0x459040) is L; or no controller is found and the NPC has a PC leader | NONE (a controlled NPC obeys) |
| 2 | T is not a PC or NPC | NONE |
| 3 | T's leader equals L, **including both null** | PARTY (HS9) |
| 4 | L is not null, the NPC's alignment is above 0 and T's alignment is at least parameter 12 | GOOD |
| 5 | origin-same(NPC, T) | ORIGIN |
| 6 | faction-same(NPC, T) | FACTION |
| 7 | otherwise | NONE |

Callers and their L: the follower fan-out passes the attacker or the victim
([section 7.2](#72-followers-join-or-complain)), never null; find target's ally
help and kill-on-sight's GUARD row pass the scanning NPC's own leader, null for
every leaderless NPC. With L null, row 3 matches every leaderless T: any PC and any
leaderless NPC.

## 5. Choosing between two targets

Choose target (0x4AB0B0, arguments: NPC, current A, newcomer B), used by the
reaction to an attack. First match wins.

| # | Condition | Result |
| ---: | --- | --- |
| 1 | A null | B |
| 2 | B null | A |
| 3 | B not a PC or NPC | A |
| 4 | A not a PC or NPC | B |
| 5 | B unconscious | A |
| 6 | A unconscious | B |
| 7 | B farther than 20 tiles from the NPC | A |
| 8 | A farther than 20 tiles from the NPC | B |
| 9 | A is a decoy for the NPC | A |
| 10 | B is a decoy for the NPC | B |
| 11 | `roll = random(1, 100)`; score := `level − distance` when roll ≤ 50, else `−distance` | B when score(B) > score(A), else A |

The roll is drawn only when rows 1–10 do not decide. With the range draw's modulo
rule, a roll of 1..50 comes up with 16,400 chances in 32,768 (50.05 %).

## 6. Enemy list and target lock

**Storage.** The enemy list is the NPC object array field 0x130 (persistent, saved
with the object); who-hit-me-last (0x11C) and the combat focus (0x11B) are
persistent handle fields too.

| Operation | Function | Rule |
| --- | --- | --- |
| Add a team | 0x4AF8C0 (owner, offender) | head := the offender's PC leader, else the offender; team (0x4413E0) := for a PC head the party's PCs and all their followers, otherwise the head alone; add each member |
| Add | 0x4AF930 (owner, X) | skipped when X (a PC) or X's PC leader (otherwise) is not null and is the owner's PC leader, or X is already listed; else appended at the end and the owner's combat reaction recalculated (0x4B80E0) |
| Remove | 0x4AF9D0 (owner, X) | every entry equal to X **and every null entry** is removed: the last entry fills the hole and the same index is checked again (order not kept) |
| Pick | 0x4AFA90 (owner) | null entries removed; `start := random(0, n − 1)`; entries `start, start + 1, …` modulo n; the first valid one ([section 2](#2-target-validity)); none → null |
| Has | 0x4AFB30 (owner, X) | false for a null or non-NPC owner; else a linear search |

The pick draws even for an empty list (bounds swapped to −1..0) and draws nothing
for a one-entry list (equal bounds), as the [range draw](../world/session-random-runtime.md)
does. Membership makes the owner's reaction toward a PC read 0 (reaction R1).

**Lifecycle.**

| Event | Effect on the list |
| --- | --- |
| An attack on an NPC victim ([7.1](#71-attack-event)) | the attacker's team is added to the victim's list, also for an accidental hit (HS19) |
| An NPC attacker | the victim's team is added to the attacker's list |
| Peace (0x4AA300, [7.5](#75-peace-stop-and-allow-attacking)) | the other side's head and each of its followers removed from the peaceful NPC's list; the peaceful NPC removed from each follower's list |
| The pick | null entries purged |

**Target lock.** Lock (0x4AFBD0, arguments: NPC, target; nothing for a null NPC,
a null target or an NPC owner that is not of type NPC): unlock, ask the setter for
COMBAT_FOCUS on the target, and only when the state read then reports
COMBAT_FOCUS on that target set critter flag 2 bit 0x200000. While it is set, the
setter ignores every further COMBAT_FOCUS request
([core loop rule 6](npc-ai-core-loop.md#5-danger-state-machine)), so the danger
update keeps the focus. Unlock (0x4AFC70) clears the bit. Lock is called only by
the follower fan-out for an ordered attack ([7.2](#72-followers-join-or-complain));
unlock by the danger update when the focus is no longer valid (0x4ABBC0), stop
attacking (0x4AA4A0), the lock itself and leaving combat mode (0x4B411C). The lock
is entered while the NPC is fighting, but the setter's lock test blocks only
COMBAT_FOCUS: a FLEE, SURRENDER or NONE request passes, clears FIGHTING and does
not unlock (a surrender unlocks only through its combat-mode exit, when the
toggle acts). So a locked follower that is hit and flees
([morale](npc-ai-morale.md#2-flight-and-surrender), stage A4) can stop fleeing
into NONE while still locked, and then ignores every fight request until it
leaves combat mode or is told to stop: the core loop's inferred state "locked but
not FIGHTING" is reachable.

**Dialog distance (0x4AFBB0).** 5 tiles for the player's PC (0x40DA20), 10 for anyone else (called by
the animation code, 0x42AE9F; stage A6).

## 7. Attack event fan-out

### 7.1 Attack event

The attack event (0x4A9650, arguments: attacker, victim, loudness index, flags).
Callers include the combat hit and miss paths, the damage step, scripts, the dialog
action interpreter, dynamite, guards, skills, fire damage, follower orders and the
redirect. Flags: bit 0 **accidental** (the combat path sets it from its context flag
0x800); bit 1 **no fan-out** (the script action passes 2); bit 2 **ordered** (the
PC's attack order passes 4 after clearing the follower's no-attack flag).

1. Stop when the attacker or the victim is null; when the victim is a PC or NPC
   that is nigh-invulnerable or dead (a sleeping one is woken, 0x4AD0B0); when the
   victim is off, not drawn or invulnerable (object flags 0x400102), the decoy,
   or a wall; when attacker and victim are the same; on a network client.
2. A non-critter attacker: an NPC victim is asked into COMBAT_FOCUS on it; stop.
3. Ordered (bit 2): the attacker's leader must exist; that leader's follower (the
   attacker) gets the follower rule with "ordered" ([7.2](#72-followers-join-or-complain));
   stop.
4. Unless bit 1: the attacker's direct followers are told (attacker side).
5. A non-critter victim: stop.
6. The victim's PC leader, or the victim itself, loses concealment.
7. An NPC victim loses its KOS override (NPC flag 0x800).
8. Unless bit 1: a PC victim (0x4A9AD0) gets the UI callback, enters combat mode
   (in a running round the turn goes to the attacker) and, when idle with
   auto-attack on and not already attempting an attack, attacks back; then the
   victim's direct followers are told (victim side); unless bit 0, the noise
   fan-out runs (0x4A9E10, stage A2).
9. A victim that is not an NPC: stop. The NPC victim turns toward the attacker.
10. Who-hit-me-last := the attacker, unless the hit is accidental and the two are
    faction-same.
11. The attacker's team joins the victim's enemy list; an NPC attacker also gets
    the victim's team.
12. A PC attacker changes the victim's reaction:

| Victim | Hit | Effect |
| --- | --- | --- |
| the PC's direct follower | deliberate | reaction += parameter 9 ([Reputation A2](original-verification-ledger.md#npc-ai-reaction-and-companions-arc-res-039-arc-res-028-arc-res-024): Steamwoven lowers by it, Original switch keeps the rise) |
| the PC's direct follower | any | follow check (0x4AD950, ignoring the follower limit); a refusal disbands it (0x45DFC0) and, when that succeeds, sets JILTED (NPC flag 0x20) and floats the "won't follow" line for the refusal code; otherwise one time in three (`random(1, 3) = 1`) the "accidental attack" line, deliberate or not |
| any other NPC | accidental | reaction −10 |
| any other NPC | deliberate | the AI reaction variant above parameter 10 is lowered to parameter 10 |

13. Unless the hit is accidental and the two are faction-same, the victim's
    reaction to the attack runs ([7.4](#74-reaction-to-an-attack)).

### 7.2 Followers join or complain

Tell followers (0x4A9B80, arguments: leader L, other T, side, accidental): when L
is not T, each direct follower F of L (0x4411C0) gets the follower rule (0x4A9C00)
with (F, L, T, side, accidental, not ordered). Side is "attacker" for the
attacker's followers (T is the victim) and "victim" for the victim's (T is the
attacker).

| Case | Effect |
| --- | --- |
| L is T | nothing |
| T a PC or NPC, F is T or dead | nothing |
| T a PC or NPC, u := upset(F, T, L) not NONE, attacker side, T not in combat mode, not accidental | F floats the "upset attacking" line for reason u toward L (when F is active) and its reaction to L drops by 5 |
| T a PC or NPC, u not NONE, otherwise | nothing |
| T a PC or NPC, u NONE, accidental | nothing |
| T a PC or NPC, u NONE, ordered | target lock on T |
| T a PC or NPC, u NONE, otherwise | F's reaction to an attack by T ([7.4](#74-reaction-to-an-attack)) |
| T not a critter, accidental | nothing |
| T not a critter, ordered | target lock on T |
| T not a critter, otherwise | F asked into COMBAT_FOCUS on T |

So an attacker's follower joins its leader against T unless T is its leader's
party, too good for it (GOOD), or shares its origin or faction, in which case it
complains instead (only when T is not yet fighting and the hit is deliberate); a
victim's follower turns on the attacker unless upset returns anything (the
attacker is in its leader's party, is too good for it, or shares its origin or
faction), in which case it does nothing.

### 7.3 Redirect

A spell component of type "AI redirect" (0x451700) fills a redirect record
(0x4A9530: two handles, intelligence threshold −1, critter-flag mask 0) with the
spell's target in **both** handles, the threshold from the component's second
field (named `MinIQ:` in the spell data) and the mask from its first (critter
flags). The redirect (0x4A9560) walks the PCs and NPCs in the vicinity of the
target (an iso-window-sized rectangle, 0x440FC0) and, for each living candidate C
other than the target, skips C when the threshold is above 0 and **the target's**
intelligence is above it, or when the mask is not 0 and C has none of its critter
flags; otherwise, for an NPC C (0x4A94C0), it runs the attack event with the target
as attacker and C as victim (loudness 0, no flags) and then writes the target into
C's focus and who-hit-me-last fields. The intelligence test reads the same critter
for every candidate, so it lets all of them through or none (P3).

### 7.4 Reaction to an attack

Reaction to an attack (0x4AA620, arguments: NPC, attacker), from the attack event
and the follower rule (and the witness rule, stage A2). A dead NPC does nothing.
"Weigh" is the fight-or-flee step (0x4AB2A0): FLEE from the attacker when
should-flee (0x4AB2F0, stage A4) says so, else COMBAT_FOCUS on it.

| NPC's state | Effect |
| --- | --- |
| NONE | weigh |
| COMBAT_FOCUS | weigh when the attacker is its focus or choose target ([section 5](#5-choosing-between-two-targets)) prefers the attacker; else keep the focus |
| FLEE | nothing when it flees the attacker; else weigh when it is farther than parameter 4 from what it flees, or mind-controlled (0x4AB030) |
| SURRENDER | weigh when it surrendered to the attacker or choose target prefers the attacker; else the "fleeing" float line (when active) and a flee goal away from the attacker |

In every case the NPC is then added to the turn-based round (0x4B7E00).

### 7.5 Peace, stop and allow attacking

| Function | Effect |
| --- | --- |
| Peace (0x4AA300; NPC N, other T) | head := T's PC leader, else T. N's focus and who-hit-me-last cleared; head removed from N's enemy list; a PC head: N's reaction to it raised to 50 when below; setter NONE; N's attack goals at the head or its party interrupted (0x44E050); when the head is a critter, for each of its followers F (all, 0x441260): F's focus and who-hit-me-last cleared when they name N, N removed from F's list, F removed from N's list, F's attack goals at N handled (0x44E0E0) |
| Stop attacking (0x4AA4A0; X) | an NPC: when it has a focus, peace with that focus, an animation interrupt (0x424070, priority 3) and leaving combat mode; with a PC leader, NPC flag NO_ATTACK (0x20000000) set; the target lock cleared. A PC: the same for all its followers |
| Allow attacking (0x4AA580; X) | an NPC with a PC leader: NO_ATTACK cleared. A PC: the same for all its followers |

Callers: peace from stop attacking and from a spell's flag component on an animal
NPC (0x454C40, stage A5 or the magic slices); stop attacking from scripts, the
"stop" order (0x4C3884) and the world map (0x5616DA); allow attacking from entering
combat mode (0x4B42B2), the "attack" order (0x4C37C0) and the world map. Peace is
one-sided for a non-PC head: the head's own focus and enemy list keep N.

### 7.6 Reaction changes in one place

| Trigger | Who | Toward | Change |
| --- | --- | --- | --- |
| Deliberate hit by the PC on its follower | follower | PC | + parameter 9 (Original), − parameter 9 (Steamwoven, Reputation A2) |
| Accidental hit by the PC on another NPC | victim | PC | −10 |
| Deliberate hit by the PC on another NPC | victim | PC | lowered to parameter 10 (AI variant) |
| A follower upset by its leader's target | follower | its leader | −5 |
| A fight started against a PC (setter rule 8) | NPC | PC | lowered to parameter 10 ([core loop](npc-ai-core-loop.md#5-danger-state-machine)) |
| Peace with a PC's side | NPC | PC | raised to 50 |
| Any attack adding the PC's team to the enemy list | victim | PC | reads 0 while listed |

## 8. Anomalies and decisions

Verdicts and rows: [ledger](original-verification-ledger.md#npc-ai-hostility-and-targeting-arc-res-057).
Decisions follow D2 and D8. The owner accepted every proposed Original switch of this table on 2026-10-20 (ARC-RES-063).

| ID | Original 1.0.7.4 | Verdict | Proposed decision |
| --- | --- | --- | --- |
| P3 | The redirect's intelligence test reads the spell target (the record's second handle, equal to the first), so with a threshold above 0 a target of higher intelligence redirects nobody and one at or below it redirects everyone the mask lets through; no candidate's intelligence is read. | Original bug. (The earlier reading, the threshold number's intelligence, was a reconstruction slip: confirmed, the ARC-RES-048 verdict unchanged.) **(inference)** A per-candidate loop that tests one fixed critter looks unintended; the field name fits neither reading, since the test skips when the intelligence is above the threshold. | Steamwoven tests each candidate's intelligence against the threshold (a candidate above it is not redirected); Original setting keeps the target's (`RedirectTestsTargetIntelligence`, owner decision 2026-10-20). The bug verdict rests on the inference; the owner confirmed it on 2026-10-20; which installed spells set `MinIQ:` is a data question for the magic census. Lands with slice I8. |
| HS9 | Upset attacking returns PARTY when the target's leader equals the leader argument also when both are null. Find target and kill-on-sight pass the NPC's own leader, so for a leaderless NPC every leaderless target (every PC, every loner NPC) is "party": the scan's ally help never targets a PC or a leaderless NPC, GUARD (which only targets an NPC) never targets a leaderless NPC aggressor, and the guard-versus-monster clauses (which demand a leaderless monster) are dead for leaderless guards. Validity, protect and faction-same all test for a non-null leader before comparing, and upset's own GOOD row tests the leader argument for null. | Bug of the original (also in the reconstruction) | Steamwoven: PARTY needs a non-null leader argument, so leaderless NPCs help allies against the PC and loners and guard against leaderless NPC aggressors by the scan. Original setting keeps the null match (`UpsetPartyMatchesMissingLeader`, owner decision 2026-10-20). Owner decision 2026-10-20, knowing that this makes townspeople and guards join a fight by their own scan, not only through the witness rule, a visible change of play. Lands with slice I4. |
| HS19 | Every attack on an NPC, accidental ones too, adds the attacker's team to the victim's enemy list, which makes the victim's reaction to the PC read 0 until peace, while the explicit accidental penalty is only −10. | Original (intent unclear) | Kept in both settings: a design question, not a confirmed slip; recorded so a later patch (plan phase 2) can revisit it. No switch. |
| HS20 | The scan keeps only the first 50 critters of the rectangle query before sorting by distance, so in a crowd a nearer critter may be dropped. | Original (limit) | Kept in both settings (50 entries in query order); more than 50 critters in a 21×21 box is not expected. No switch. |
| HS21 | Validity's unconscious rule calls find target, which does nothing inside a running scan, so during a scan an unconscious candidate is valid and outside one it is valid only while no other target exists. | Original | Kept in both settings: the scan orders unconscious critters last, so the rule rarely decides. No switch. |
| HS22 | The reaction to an attack calls the float-line callback in its surrender branch without testing it for null, unlike every other caller. | Bug of the original (latent: the game always sets the callback) | Not applicable: Steamwoven has no unset callback; never reproduced (owner rule on crashes). |

## 9. Consequences for Steamwoven (project policy proposals)

1. **Slice I4** implements kill-on-sight, protect, upset and validity as pure
   functions over explicit critter records (type, flags, leader, PC leader,
   faction, origin, social class, alignment, intelligence, prowling, the AI
   reaction variant, the NPC's parameters, distances), each returning the reason
   codes above; choose target and the enemy-list pick take an injected roll with
   a purpose label (`ChooseTargetScore`, `EnemyListStart`) and draw exactly when
   the original draws, including the empty-list draw.
2. **The scan** takes the rectangle query's order as an input list (the round-order
   rule of the map owner) and keeps the 50-entry cap, the keys and the stable sort.
3. **Slice I3** stores the enemy list, who-hit-me-last, the combat focus and the
   target-lock bit per NPC in the session snapshot (one schema for both settings,
   design rule 7), with peace and the team add as session operations.
4. **The attack event** becomes one session operation with the three flag bits;
   the reaction changes go through the reaction owner (ARC-WORLD-026), the
   follower rule and the noise fan-out are its steps, and the PC victim's combat
   entry goes to the combat-mode owner (ARC-COMBAT-004).
5. **Protect** is exported for stage A5 (heal targets, grenade friendly fire)
   exactly as [4.2](#42-protect) states it.

## Unknowns

- **U1** How often each path decides in play (for example how often the ally-help
  step fires for a follower) is a runtime question for slice I11.
- **U2** Which installed spells use the AI redirect and with what threshold and
  mask is a data question (P3).
- **U3** The animation helpers called by peace and stop attacking (0x44E050,
  0x44E0E0, 0x424070) are named by what both readers saw them do; their full rules
  belong to stage A6.

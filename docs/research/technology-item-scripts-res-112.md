# Technology item scripts and the generic item flags: what flag 0x20, the grenade bit and the crafted-gear scripts do

Status: **Research**, work item ARC-RES-112 (2026-11-11), read T-r1 of the
[technology audit](technology-audit-res-104.md#8-the-reads-this-audit-needs-mac-docs-only-each-ends-in-ledger-rows) (wave 0). Docs-only: no code
changes and no checks run (the data census and the admission check below ran locally as throwaway programs outside the repository, not as repository
checks). It answers the three questions of T-r1: what the generic item flag 0x20 means (two learned and three found products), which numeric bit the
grenade flag is, and what the item scripts at attach points 24, 25, 7 and 8 on crafted gear do (scripts 28012 to 28015, 29016, 29017 and 1022) and whether
Steamwoven's script host runs them. It ends in the verdict the audit asked for: whether the conditional slice T-m is needed (it is, as two slices) and
what the read says about T-k (nothing; T-r3, ARC-RES-114, has since dropped it).

Requirements (reference only; three coverage rows get one added sentence each):
[FR-ITEM-018](../requirements/items-economy.md#fr-item-018) (assembly: what the products do),
[FR-ITEM-011](../requirements/items-economy.md#fr-item-011) (item hit points) and
[FR-MAGIC-011](../requirements/combat-magic-tech.md#fr-magic-011) (item events). Also touched, not changed:
FR-ITEM-016, FR-ITEM-017, FR-MAGIC-007, FR-MAGIC-010, FR-MAGIC-012, FR-CHAR-017, FR-CHAR-019 and FR-CHAR-020 (the other rows of the audit).

Earlier results reused, not re-read: the item window's lines ([OH10, OH17](ledger/ui-audio.md)), the AI's grenade rule ([SP14](ledger/npc-ai.md)), the throw
([TW1 to TW7](ledger/combat.md)), item use ([AG15](ledger/npc-ai.md)), the script dispatcher's event catalogue
([scr-event-dispatcher.md](scr-event-dispatcher.md#2-event-catalogue-s)) and the follow and attack script actions
([npc-ai-script-surface.md](npc-ai-script-surface.md)). The new rows are [TI1 to TI10](ledger/technology.md#technology-item-scripts-and-generic-item-flags-arc-res-112);
this note tells the story and hands it off.

## Short answers

| Question | Answer |
| --- | --- |
| What is the generic item flag 0x20? | It means **"show this item's hit points"**. The only code in the executable that tests it is the item window's fourth line (0x5556E5), which draws `current/maximum` hit points for a weapon, an armour piece or a generic item with the bit (already recorded as OH17, and built). Nothing else reads it: no use, wear, throw, damage, price or AI rule. Six installed prototypes carry it (all six are the creature items below). Steamwoven needs nothing more for the flag itself. |
| Why do those six items have it? | Their item script turns the item into a creature, and that transformation **carries the item's hit-point percentage over to the new creature** (TI5). The item's condition is therefore real information for the player, and the flag lets the window show it. |
| Which bit is the grenade flag? | **0x10** of the generic flags (field 0xD3). The AI's grenade rule asks the inventory finder (0x462760) for the first generic item whose flags hold 0x10 (call at 0x4AC98A inside 0x4AC910). All 11 grenade prototypes carry exactly 0x10. The other bits are 0x1 (worn in the shield location: torches), 0x2 lockpick, 0x4 trap device, 0x8 healing kit and 0x20 hit points shown (TI2). |
| What are the scripts at points 7 and 8 (29017, 1022)? | **HIT and MISS scripts of a thrown creature item.** The attack resolver runs point 7 on a hit and point 8 on a miss with the attack's weapon as the attachee, and a thrown item is that weapon whatever its type (TW3). The script turns the item into a creature of a basic prototype (action 40, TI5). Four of the scripts then make the creature **join the thrower's party by force** (action 21: 1022, 1343, 30008, 30300); 29017 makes the creature **attack everyone in its vicinity** (action 36) instead (TI6). |
| What are the scripts at points 24 and 25 (28012 to 28015)? | Ordinary **wield-on and wield-off effect scripts** shared with magical armour: 28012/28013 add and remove an effect from the item's magick or tech adjustment, 28014/28015 the same with a fixed effect (137). Results are ignored by the equip code. Steamwoven's item-event set **admits all four** statically; whether each run is accepted at run time (effect 137, the adjustment entries, the aptitude check, the call-ex write-back) is unverified until T-a. |
| What is script 29016 (point 24 only, the Electric degree-3 shield)? | A **15-second timer**. Wield-on schedules a call of itself, and each call, while the wearer still wears the item (name number 2866), **uses one unit of ammunition type 2 (a power cell) from the wearer** and schedules the next call (TI8). Steamwoven's item set refuses its actions (125, 54, 135), so **the shield cannot be worn at all today**: the wear move is refused as `ItemScriptUnsupported`. |
| Does the SCR host run them? | 28012 to 28015: **they pass the static item-event admission** (the first real-data run, which also decides run-time acceptance, is the smoke stage T-a). 29016: **no, and it refuses the wear**. 29017, 1022, 1343, 30008, 30300: **no**; the actions 40, 21 and 36 are not admitted in any set, and Steamwoven's hero throw never fires points 7 and 8 (an existing "not ready" of the attack docs). A thrown creature item today just lands as an item. |
| Are the conditional slices needed? | **T-m: yes, two slices** (T-m1 creature items, M to L; T-m2 the powered shield, S to M), section 5. **T-k: this read does not touch it** (nothing here reads aptitude or expertise); T-r3 (ARC-RES-114) has dropped it. |

## Method and inputs

- The owner's unchanged GOG install (`/Applications/Arcanum`): `Version.txt` reads `VERSION:1.0.7.4`; `Arcanum.exe` SHA-256
  `9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52`, re-hashed for this reading.
- Static reading with Xcode's `llvm-objdump` (read-only, into the session scratchpad; nothing kept), following the
  [original executable check](original-executable-check.md), by one reader (F1). Every direct call and jump of the read functions
  (about 300) stays inside `.text`. No bytes, listings or decompiled code are in git. The search covered every direct reference to field 0xD3 (indirect or table-driven access is not excluded, F1): every direct
  reference to the number in `.text` was listed (19 reads, 22 writes of the prototype builders, and six unrelated user-interface message numbers
  that merely equal it).
- Data: a throwaway console program in the ignored `local/` folder (not committed), built on the repository's own readers, read the 798 item prototypes of the project's
  prototype cache (built from the retail executable), the schematic catalogue and the scripts through the content stack of the installed game, and ran
  Steamwoven's own item-event admission (`SessionItemScripts.Admission`) over each script from line 0. Counts and numbers only: no names, no art, no
  script text. Prototype numbers here are **file numbers** (a basic prototype number plus 20); a script's own basic prototype operand is the number
  before the 20 is added.
- A fresh agent re-reads the exact diff and the addresses it names before the PR (method step 6 for the claims it can re-check); its findings and what they changed are recorded
  in section 6.

## 1. The generic item flags (field 0xD3)

Every read of the generic flags in the executable (TI2), by the bit it tests:

| Bit | Meaning | Where the executable tests it | Installed prototypes |
| --- | --- | --- | --- |
| 0x1 | Worn in the shield location (the torch) | item location (the read at 0x465664 gives location 1005) | 2 |
| 0x2 | Lockpick | item use (0x462E87), the pick-locks skill check and tool search (0x4AE5D7, 0x4C8DA4), the inventory finder (callers 0x4C9212, 0x571D91, 0x571DD8), the item window (0x555246, 0x571D3D) | 3 |
| 0x4 | Trap device | the trap-device test (0x4BCA60) and the trap code before a use script (0x4BBEA1) | 4 |
| 0x8 | Healing kit | the heal skill check and tool search (0x4AE67F, 0x4C7B65, 0x4C8D36, finder callers 0x4C9242, 0x4C9268), the item window (0x55554F) | 2 |
| 0x10 | **Grenade** | the AI's grenade rule (0x4AC910 calls the finder 0x462760 with the mask 0x10 at 0x4AC98A) | 11 |
| 0x20 | **Hit points shown** | the item window's fourth line (0x5556E5) only | 6 |

There are 148 generic item prototypes; 120 carry no flag, and the 28 that do carry exactly one bit each. The inventory finder (0x462760) is called with the masks 0x2, 0x8 and 0x10
and never with 0x20, so no search can look for it. The three prototype builders that write 0x20 do it with a plain bit-or (0x492A52, 0x494EB3, 0x4978EA). The six unrelated compares with the number 0xD3 (0x547739, 0x5477A8, 0x54784E, 0x5561E6, 0x556C5E, 0x562682) are user-interface message and
control numbers.

**The flag 0x20 (TI1).** In the item window the fourth line (0x5C70F8) is drawn for every item: the weight, then, for a weapon, armour piece or a generic item
with 0x20 whose art is not a destroyed one, the hit points as `current/maximum` (format 0x5C7848). The two numbers come from the object helpers 0x43D600
(maximum less damage) and 0x43D5A0 (field 0x1B hit points plus field 0x1C adjustment), as the ledger row OH17 already says. The new result is the
negative one: **no direct reader of the bit other than this one was found** (F1), so the flag has no effect on use, wearing, throwing, wear, price, repair or the AI. It is a display
flag. (Not to be mixed up with the **item** flag 0x20 of field 0x57, no-drop, which `SessionEquipmentFacts` reads.)

**The grenade bit (TI3).** SP14 recorded "the first generic item with the grenade flag (0x462760)" without the number. The finder takes a mask and tests
`mask & flags`; the grenade rule pushes 0x10. The census agrees: the 11 prototypes with the bit have exactly 0x10, and their item flags are 0x10080. No
installed prototype combines 0x10 with another generic bit, so a future reader may treat the generic flags as a single-bit enumeration.

## 2. The six items with flag 0x20 are creature items

The data (census of the project's prototype cache and schematic catalogue):

| Prototype | Hit points | Made by | Item scripts (points 7 and 8) | Created creature (basic prototype, file number) |
| --- | --- | --- | --- | --- |
| 15136 | 300 | **learned**, Mechanical degree 6 (record 3050), 2 per assembly | 29017 | 27391 (27411) |
| 15086 | 80 | **learned**, Mechanical degree 7 (record 3060) | 1022 | 28460 (28480) |
| 15112 | 80 | found record 5000 | 1343 | 28461 (28481) |
| 15153 | 300 | found record 5010 | 30008 | 28462 (28482) |
| 15192 | 350 | found record 5070 | 30008 | 28462 (28482) |
| 15232 | 80 | no schematic | 30300 | 28467 (28487) |

All six have item flags 0x80000 (the bit the AI's ground-item search tests together with 0x10, SP12's "no NPC pick-up"), complexity -70 or -100 (technological) and discipline 5.
The audit's counts are confirmed (two learned and three found products with the flag; no schematic makes the sixth; its placement is T-r4). Each created creature is an NPC
(type 16) with its own dialog script at point 9; two of them carry one more script (a CRITTER_HITS script at point 26 for the 28461 prototype, a HEARTBEAT
script at point 19 for 28467).

## 3. What the HIT and MISS scripts do

**The dispatch (TI4).** Points 7 and 8 have three call-site groups ([event catalogue](scr-event-dispatcher.md#2-event-catalogue-s)): the tile crossing (0x4B2870, whose range end runs the miss script, PF9), the hit (0x4B2F60, after a strike, PF8) and the resolver 0x4B3770, which this read re-read directly. 0x4B3770 (one function for a hit and a miss) runs point 7 on a hit and point 8 on a miss with the attack record's
weapon (the item at record offset 0x10) as the attachee, the attacker as the triggerer and the target as the extra object, whenever the weapon handle is not
null; the critical points follow (34 and 35). It never tests the weapon's type. A throw builds the attack record with the thrown item as the weapon
([TW3](ledger/combat.md)), the item lies at the landing tile before the hit ([TW6](ledger/combat.md), PF8), so a **thrown generic item runs its point 7 or point 8
script at its landing** (a flight that strikes reaches the hit through PF8, one that ends at range the miss through PF9; the target-less landing, the scattered throw and the boomerang return are separate "not ready" items of [hero-attack-runtime.md](../world/hero-attack-runtime.md), lines 268, 299 and 328). The throw goal has five starters: the attack start of a throwing weapon (0x434BD2), the AI's grenade (0x4ACA9D) and three user-interface
paths (0x54E04B, 0x54F3E9, 0x572023); the last is the inventory's use-on-target handler, which as read reaches the throw for an item that is not a trap
device, has no spell charges and lacks the magical flag 0x4 (a trap device is set instead, AG15, TP1). Which items the viewer's drag to a tile may throw
is part of the aiming read T-r2; the throw itself exists in Steamwoven.

**Action 40, transform the attachee into a basic prototype (TI5).** The action reads the attachee's location (its holder's location when it is carried), its
hit-point percentage (the current points times 100 over the maximum, 0x4AB400), for a critter its fatigue percentage, and its current art; creates an object of the operand's basic
prototype there (0x4ED8B0; when it cannot, the action stops); destroys the old object (0x43CCA0, so a destroy script would run); makes the new object the run's
attachee; sets the new object's damage so that it has the **same hit-point percentage** (`(100 - percent) * maximum / 100`, kept below the maximum so a creature
never starts dead), gives it, as read (0x502880, 0x502930), the old art's rotation, and, when both are critters, carries the fatigue percentage over (an item becomes a critter with full fatigue); and, when the new object is itself an item and the
old one had a holder, inserts it into that holder. Ledger row OH17's hit-point line is what lets the player see the share that will be carried.

**Action 21, forced follow, and action 36, attack (TI6).** The four creature scripts 1022, 1343, 30008 and 30300 have the same three lines: transform (action 40) into the
creature, **make the creature follow the triggerer** (action 21, operand 0 the attachee, operand 1 the triggerer, with the follow check skipped:
[npc-ai-script-surface.md](npc-ai-script-surface.md), 0x45DE90 with force 1, so even the follower limit does not stop it), then return *run default*. Script 29017 transforms
(basic prototype 27391) and then runs action 36 with operand 0 the set `everyone in the vicinity` and operand 1 the attachee: **the new creature attacks every object
of the set** (the attack event with the creature as attacker, loudness 1 and flags 2, no fan-out; a PC in the set merely loses concealment). So five of the six
items (scripts 1022, 1343, 30008 on two prototypes, 30300) produce a pet that joins the thrower (a PC, since only NPCs join), and the one 29017 item (15136) an aggressor. The scripts do not check who threw the item.

## 4. The equipment scripts and the SCR host

| Script | Points | Items (crafted) | What it does (opcode level) | Steamwoven today |
| --- | --- | --- | --- | --- |
| 28012 / 28013 | 24 / 25 | 13 prototypes in all; crafted: the Electric ring (8077, learned degree 2) and the Mechanical helmet (8203, learned degree 4) | 28012 reads the item's magick or tech adjustment (action 91), adds an effect with the cause item (action 77), loops over its entries; lines 6 to 10 remove it (action 78). 28013 re-enters 28012's line 6 through call-ex (action 81). Results ignored (equip 0x465530, 0x4677B0, 0x467CB0) | **admitted** statically (ARC-SCRIPT-042 path); run-time acceptance unverified until T-a |
| 28014 / 28015 | 24 / 25 | 6 prototypes; crafted: the Smithy gauntlets (8195, learned degree 4), found 8297 | The same shape with a fixed effect number (137) | **admitted** |
| 29016 | 24 only | the Electric shield (8214, learned degree 3) only | A 15-second timer, see TI8 | **refused**: actions 125, 54 and 135 are outside the item set, and a refused item script refuses the wear |
| 29017, 1022, 1343, 30008, 30300 | 7 and 8 | the six creature items | Section 3 | **not run**: no set admits actions 40, 21 or 36; points 7 and 8 are not fired for thrown items |

**29016, the shield (TI8).** Line 0 removes any pending call of script 29016 for the attachee, line 1 schedules the script again at line 3 in 15 seconds
with the wearer as triggerer (action 54, the game-delay arithmetic of TE1), line 2 returns. A due call starts at line 3: when the triggerer no longer wears an
item of name number 2866 (the shield's own name number) the script returns; otherwise line 4 uses **one unit of ammunition type 2** from the triggerer
(action 135, the unit-use helper 0x4658E0 with count 1 and type 2: the cells of the charge-cell rules), line 5 schedules the next call, line 6 returns. The
script never tests whether a cell was there, so with no cell nothing happens and the timer goes on. There is no wield-off script; the wearer's removal ends
the loop at the next tick. The shield has no spell and no other effect, so its whole technological content is this drain.

**What Steamwoven does with them.** `SessionItemScripts.Admission` (the item-event set: S6 without dialog, story state and deferred calls, plus effects, call-ex, the
magick-tech adjustment, eye candy and sound) was run over each script from line 0: 28001, 28002 and 28012 to 28015 are admitted; 29016 is refused at line 0 for
action 125; the five creature scripts at line 0 for action 40. A refused script refuses the move (`ItemScriptEffects`), and effects that hold a deferred call are refused even when
the set admitted the action. `SessionScriptPoint` has no hit or miss point, and the hero throw does not refuse points 7 and 8 and runs nothing for them. Two
consequences: **the learned Electric degree-3 product cannot be put on**, and **a thrown creature item behaves like any thrown item** (it lands and stays an item).
No other learned product is refused by the static admission; run-time refusals after admission (an unknown effect rule, an unstorable change, an aptitude change) are possible and are for T-a to show.

## 5. Result for the plan: T-m is needed (two slices)

Slice codes are the coordinator's; the labels below are planning labels.

| Slice | Content | Size | Needs |
| --- | --- | --- | --- |
| **T-m1** creature items | (a) the HIT and MISS item events fire for a thrown item's landing, whatever its type (attacker, item, target or none), scoped by PF8 and PF9 (strike and range end) and the three "not ready" cases above; (b) the script actions 40, 21 and 36 are admitted for that run with hosts: the transformation (item to a creature at the landing tile with the hit-point and fatigue percentages and the rotation carried, the old item destroyed), the forced follow (follow check skipped) and the attack on a set; (c) the new NPC enters the map's directory and the follower rules through E1-c; (d) a viewer smoke step: throw one 28460-type item and see a follower, throw a 27391-type item and see the attack. Not in this slice: the created creatures' own scripts at points 9, 19 and 26 (the NPC script host) | M to L | **E1-c created critters** (planned, not built: E1-a, ARC-DEV-347, creates items, scenery and eye-candy objects only, so it is not enough); no aiming (the throw exists), so independent of T-g |
| **T-m2** powered shield | the item event set admits actions 54, 125 and 135; a deferred call from an item event is kept and run by the game clock (15 s), checks the wear (condition 21), and uses one unit of ammunition type 2; saved with the game | S to M | the game event queue's script call kind (check at slice start; the session refuses item-event calls today); script catalogue entries for actions 135 (and 40, 21, 36 for T-m1), which the named catalogue lacks; charge cells (built) |

Acceptance for both: every installed script of the group runs to the result recorded in TI5 to TI8 (an opt-in real-data test over the scripts of this note), plus the smoke step of T-m1. Original behaviour in both
modes: nothing here is a bug of the original under the bug rule (TI9), so no switch.

**T-k** (expertise effects, aptitude readers): nothing in this read touches aptitude or expertise; T-r3 (ARC-RES-114) dropped it. The tech books' insert and remove scripts (points 21 and 28) that ARC-RES-114 adds are a third slice, T-m3, in the audit plan; they were not read here. **Other audit rows** are
unchanged except the states in the matrix: the Electric ring, the Mechanical helmet and the Smithy gauntlets move from "scripts T-r1" to "EQUIP, scripts run",
the Electric shield to "BLOCKED (29016, T-m2)" and the two flag-0x20 learned products to "INERT (creature on throw, T-m1)". The AI's grenade pick (T-h)
is unaffected (a creature item has no grenade flag, so the AI never throws one: the only AI throw is the 0x10 rule).

## 6. Not done and open

- Not read: what a created creature's own scripts do (points 9, 19 and 26 of the created prototypes); how the viewer's drag of an item to a tile reaches the throw
  (T-r2); the effect numbers 137 and the adjustment entries of 28012 in the effect tables (the effects run through the existing path, ARC-SCRIPT-042).
- Not run: no scenario in the running game (F4); Steamwoven's claims are by admission run and code reading, not by a play-through (the first real-data run of the
  equipment scripts is the smoke step of T-a).
- Prototype numbers 28460, 28461, 28462, 28467 and 27391 name creature prototypes; what they are in the game's own words is not recorded here (no text).
- TI9 and TI10 in the ledger say what the read leaves as original behaviour and what it leaves out.
- Reviews (method step 6). A reader who saw only the executable questions and a second reader of the diff re-checked this note. The diff review found a wrong count of
  pet items (five, not four), a wrong section reference, "admitted" read as "runs" (now "statically admitted, run-time acceptance for T-a"), too few hit and miss call sites
  (now three groups with PF8 and PF9), negative claims stated too strongly (now "no direct reader found, F1"), and a wrong dependency of T-m1 (E1-c created critters,
  not E1-a). All are fixed here. The blind exe reader (questions only, no answers or repository documents) agreed on every point it read: the only direct test of generic bit 0x20 is the item
  window's hit-point line, the grenade rule's finder mask is 0x10, points 7 and 8 are dispatched by 0x4B3770 on the record's flag bit 0x2 with only a non-null weapon test, action
  40's steps as TI5 gives them (the table is indexed by the action number less 1), and action 135 is a unit-use call with count N and type 2 on the operand's object. Its limits:
  it did not rule out a register-held field index, and it read actions 21 and 36 only to medium confidence (their roles here rest on the earlier script-surface rows). It read
  the hit and miss dispatches of 0x4B2F60 as the same shape (guarded by a non-null weapon only).

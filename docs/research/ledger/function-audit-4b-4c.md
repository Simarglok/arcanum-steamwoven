# Original verification ledger: function audit of 0x4B0000–0x4CFFFF (ARC-RES-109)

Part of the [original verification ledger](../original-verification-ledger.md) (ARC-RES-048): its [inputs and method](../original-verification-ledger.md#inputs-and-method) and [verdicts](../original-verification-ledger.md#verdicts-and-what-they-mean-for-the-two-settings) apply. This section is slice E of V1 (triage) of the [function audit plan](../function-audit-plan.md#5-recommendation) and gives **every function start of the band 0x4B0000–0x4CFFFF that the registry did not yet hold one state**.

## Scope and method

- **Band:** 685 function starts of the [census](../../verification/census.csv) lie in 0x4B0000–0x4CFFFF (combat, items, skills, reputation, reactions, quests, magic items, AI action lists, eye candy, sectors and the modules around them). 211 already had a [registry](../../verification/functions.csv) row; the other 474 are listed below, 373 of them cited in some document. The 211 registered rows were not touched.
- **Read** on 2026-11-11 on the owner's Mac with `llvm-objdump` over the retail 1.0.7.4 file (SHA-256 `9bd80511…3678d52`); no Ghidra. The disassembly stayed in the session scratchpad: no bytes, listings or names go into git, and the notes below are in the project's own words.
- **Basis column.** `R` (205 functions) means the whole function was read in this audit: every function of at most 128 bytes in 0x4B0340–0x4B8000, at most 96 bytes up to 0x4BA000, at most 64 bytes up to 0x4BC000, at most 48 bytes from there on, and the larger marked functions of the rows added below. An F0 row is always `S`: it carries no verdict, even where its text was looked at. `S` (269) means the state comes from the function's structure (its callees, the globals it touches, its size) and from the documents and code comments that cite it, **not** from a reading of the whole function; a larger `S` function with no code mention carries an F0 row instead of a verdict.
- **One reading, no second reader.** A row added here is at F1 at best. Nothing in this slice was run in the oracle, the emulator or the game.

## States

| State | Meaning | Registry |
| --- | --- | --- |
| row added and marked | read in the executable and implemented by one method of ours that carried no mark: the row and the `OriginalFunction` mark are added together | `partial` row at F1 with its method |
| implemented, unmarked | the function is implemented, in several methods, inside a host, or in an assembly the registry test does not load (`Arcanum.World` and the Godot layer); a mark needs a one-to-one method or a change of the test, so no row is added yet | none; the implementing code is named in the note |
| implemented per documents, no address in code | as above, but no source file of ours names the function's address: the implementation is inferred from the documents that describe it (a ledger row, a contract, a research study) and from code that does the same job | none; V2 or V4 confirms it |
| not implemented (read) | read, and no method of ours is tied to it | `not implemented` row at F1 |
| not yet read (F0 row) | not read as a whole; whether our code covers it is not established | `not implemented` row at F0 whose reason says so |
| not relevant | the function holds no game rule of its own; the class says why (below) | none |

Classes of **not relevant**: *module lifecycle* (init, exit, reset and empty module callbacks, and settings callbacks: our modules and settings are our own); *table or constant accessor* (returns a global, a table entry or a constant: the data is covered with its module); *wrapper of another function* (forwards constant arguments to a function that has, or is covered by, its own row); *interface and viewport* (screen geometry, drawing, windows and indicators: the viewer draws on its own); *multiplayer only* (party slots, per-player lists and transfers of the network group, which a single-player game never reaches); *memory or list helper* (pool, table clear and free).

## Counts

| State | All | Cited in a document | Cited and reached by direct calls | Address taken |
| --- | ---: | ---: | ---: | ---: |
| row added and marked | 12 | 12 | 12 | 0 |
| implemented, unmarked | 77 | 77 | 64 | 8 |
| implemented per documents, no address in code | 56 | 52 | 39 | 12 |
| not implemented (read) | 2 | 2 | 2 | 0 |
| not yet read (F0 row) | 78 | 57 | 40 | 13 |
| not relevant: module lifecycle | 73 | 45 | 4 | 66 |
| not relevant: table or constant accessor | 29 | 23 | 18 | 1 |
| not relevant: wrapper of another function | 29 | 29 | 25 | 0 |
| not relevant: interface and viewport | 81 | 47 | 20 | 32 |
| not relevant: multiplayer only | 20 | 19 | 14 | 3 |
| not relevant: memory or list helper | 17 | 10 | 6 | 6 |
| **Total** | 474 | 373 | 244 | 141 |

`reached` is the census `reach` column (a direct-call chain from a registry function or a game-loop root); `address taken` rows are the ones a table or callback can reach (settings callbacks, save and load slots, module init and exit). The registry gains 92 rows: 12 marked, 2 not implemented at F1 and 78 at F0.

## Rows added to the registry

Twelve functions were read and found implemented by methods of ours that carried no mark. Each row is `partial` at F1: the method does the pure rule and takes as arguments what the original reads from the world, and the note says what the caller keeps. The original was compared with the method's code, not run.

| Address | Original (read once) | Method | What stays with the caller |
| --- | --- | --- | --- |
| 0x4B0FC0 | The minimum of a stat: a table lookup by the stat number (the object is not used); the 28 entries were compared with ours for the 19 stats we model. | `CharacterStatRules.Range` | The minimum of a stat from a table indexed by the stat (0x5B5194; the entries match ours for the 19 stats we model); the original covers 28 stats and Range refuses the nine it does not model (level, experience, alignment, fate points, unspent points, poison level, age, gender, race). |
| 0x4B0FD0 | The maximum of a stat: Strength, Dexterity, Constitution, Beauty and Willpower depend on the race (20 to 26), every other stat comes from a table (0x5B5204); a null object reads as race 0 (human). | `CharacterStatRules.Range` | The maximum of a stat: by race for Strength, Dexterity, Constitution, Beauty and Willpower, from a table (0x5B5204) for the rest, which matches ours for the 19 stats we model; the nine stats Range refuses are not modelled; a null object reads as race 0 in the original and Range takes the race as an argument. |
| 0x4B65A0 | One draw of 1 to 100: up to 70 gives 0, up to 85 gives 3, up to 95 gives 2, above gives 1 (torso, leg, arm, head). | `AttackRules.HitLocationFromRoll` | Takes the d100 as an argument; the original draws it itself (random 1 to 100): up to 70 torso, up to 85 leg, up to 95 arm, else head. |
| 0x4B6B90 | The end of one attack: with the taunt setting on, a player attacker, an object struck outside the attacker's party and a hit, one of the attacker's followers taunts (the combat taunt 0x4AE9E0 is called with the attacker): kind 0 for a critical hit on the aimed object, kind 1 for another struck object under record flag 8. | `FollowerRules.TauntKindOf` | Selects the kind only; the setting read and the call of the combat taunt 0x4AE9E0 are the caller's. |
| 0x4B7300 | Half the local player's Perception plus 5 (rounded toward zero), at least 10, stored as the round's scan half-width. | `TurnBasedRules.ScanRadius` | Takes the effective perception as an argument; the original reads the local player's Perception and stores the radius in a global. |
| 0x4B7790 | Outside a round and for any critter but the actor the check passes; for the actor it stores the cost and passes when the action points cover it, or for the local player with action points left when current fatigue is above 1. | `TurnBasedRules.CanAfford` | The pure check; the gates for a round and for the acting critter, the store of the pending cost and the action point display callback are the host's (ExecutorRules). |
| 0x4B78D0 | The attack cost, plus the move to an adjacent cell when the attacker is unarmed or has a weapon of range 1 or less and the target is farther than one cell; then the check 0x4B7790. | `TurnBasedRules.AttackActionCost` | Takes the attack cost, the reach, the distance and the path steps as arguments; the original reads the weapon range and measures them itself. |
| 0x4B79A0 | 2 for using an object, plus the move when the actor is not on the object's cell; then the check. | `TurnBasedRules.UseObjectActionCost` | Takes the cell test and the path steps as arguments; the original measures them itself. |
| 0x4B7A20 | 1 for picking up, plus the move when not on the item's cell; then the check. | `TurnBasedRules.PickUpActionCost` | Takes the cell test and the path steps as arguments; the original measures them itself. |
| 0x4B7BA0 | The path length to the target times 2, or times 1 for the local player who always runs. | `TurnBasedRules.MoveCost` | Takes the path length as an argument; the original measures the path itself and reads the always-running setting of the local player. |
| 0x4B7C30 | From the weapon speed: above 24 costs 1, above 20 costs 2, else 8 less a third of the speed, at least 1. | `TurnBasedRules.AttackCost` | Takes the weapon speed as an argument; the original reads the wielded weapon and its speed itself (0x465CF0). |
| 0x4B7CD0 | Only for the critter whose turn it is: paid when the points cover the cost; else the local player with points left and fatigue above 1 pays with 2 fatigue damage and ends at 0 points; else the action is refused and the next subturn starts. | `TurnBasedRules.Charge` | The pure part (paid, overexerted or refused); the gates, the fatigue damage call, the call that ends the subturn and the action point display callback are the host's. |

The other 80 rows are `not implemented`. Two were read as a whole: 0x4B0450 writes the 28 default base values of an object's stats from a fixed table (no method of ours is tied to it; the only mention is a comment in `GameSessionFate`), and 0x4B2810 returns the loudness of a weapon's attack event (the first reading called it what a projectile does with an object it meets): 1 for no object or a non-weapon, 2 when a type 5 object has flag 1 of field 0x70 set, 0 when it has flag 2 set, otherwise 1 (ARC-COMBAT-1930 ties `AwarenessRules.WeaponLoudness` to it). The 78 F0 rows are the functions not read as a whole; each reason says what the structure shows.

## Findings

1. **The pure rules of the turn-based round were implemented without marks.** `TurnBasedRules` carried no `OriginalFunction` mark although it holds the scan radius, the attack, move, use and pick-up costs, the affordability check and the charge of the round (eight methods, rows above). Four more (the stat limits, the hit location roll and the taunt kind) were in the same state. The registry's mark gate could not find them: it only checks marks against rows, which is the V4 gate of the plan.
2. **A mark is often blocked, not missing.** 133 functions are implemented but unmarked: 77 have their address named in our source and 56 only in documents. Eight of the first are named only in `Arcanum.World` or the viewer, which the root registry test does not load; the rest are spread over several methods or sit inside a host (the attack resolution 0x4B3170 and its callees, the round loop, persistence, the item trigger events). A row needs a one-to-one method, or the test loading `Arcanum.World`; this slice changes neither.
3. **Over half of the band is not game rules.** 249 of the 474 are not relevant: module lifecycle, accessors, wrappers, interface drawing, the multiplayer party and memory helpers. The two big groups are the interface (the viewport, indicators, fades, markers, the dialog answer window and the facade drawing) and module lifecycle.
4. **Where a reading was wanted and not done.** 78 functions carry an F0 row. The largest are the turn-based round helpers (0x4B6C90, 0x4B7EB0), the monster generator placement and counters (0x4BA460–0x4BA910), the trap lists' save and load (0x4BD550, 0x4BD710), the teleport ban lists (0x4BD9E0, 0x4BDB30, 0x4BDC60), the reaction memory slots (0x4C1290–0x4C1490), the spoken orders (0x4C2C10–0x4C3B90) and the sector load around the view (0x4CF390). The slots of the reaction list and the monster generator are the ones V2 should read first: both are saved state. The monster generator band (0x4BA460–0x4BABF0) was read on 2026-11-12 (ARC-RES-1103, [PO1–PO12](npc-ai.md#generators-encounter-creatures-and-body-decay-arc-res-1103)); its registry rows are at F1 and the F0 rows of the table below for it are superseded.
5. **Comments that name the wrong function.** `ExecutorEyeCandyGoals` calls 0x4CDC50 the first begin of an eye candy, but the function at that address is a 32-byte accessor of a ten-entry table, not a begin step; the same address is named in a comment of `AreaEyeCandyHost` (the eye candy place port). Which function the comments mean is open (a follow-up for the eye candy owner). `BackgroundTable` names the background item removal 0x4C2830, which lies inside 0x4C26F0 and is not a function start.

## What this section does not say

- A `not relevant` row for an interface or lifecycle function is a classification by structure for the larger ones (basis `S`), not a proof that nothing in it matters to a rule; a rule found there is a V2 task.
- `implemented, unmarked` and `implemented per documents` say where the behaviour lives, not that it matches the original: only the twelve marked functions were compared with the code, once.
- An F0 row is not a claim that the function is missing; its reason says the opposite is not established.

## The band, module by module

Registered rows are not repeated. `Size` is in bytes (the census), `B` the basis above.

### Stats (0x4B0340–0x4B1350)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4B0340 | 256 | S | implemented per documents, no address in code | stat text tables loaded at module start; the viewer reads stat.mes itself (Arcanum.World, outside the registry test) |
| 0x4B0440 | 16 | R | not relevant: module lifecycle | stat module exit, frees the loaded stat texts |
| 0x4B0450 | 64 | R | not implemented (read) | writes the 28 default base stat values of an object from a fixed table; no method of ours is tied to it (the only mention is a comment in GameSessionFate) |
| 0x4B0980 | 1376 | S | implemented, unmarked | stat base set (1376 bytes); its guards and clamps are spread over CharacterPointRules, SkillRules and GameSessionEncumbrance |
| 0x4B0EE0 | 112 | R | implemented, unmarked | "stat is extraordinary" (critter or player, stat 0..7, value of 20 or more); the host fills AwarenessRules.ExtraordinaryPerception |
| 0x4B0F80 | 16 | R | not relevant: table or constant accessor | derived stat name text by index |
| 0x4B0F90 | 16 | R | not relevant: table or constant accessor | stat short label text by index |
| 0x4B0FA0 | 16 | R | not relevant: table or constant accessor | gender name text by index |
| 0x4B0FB0 | 16 | R | not relevant: table or constant accessor | race name text by index |
| 0x4B0FC0 | 16 | R | row added and marked | stat minimum: CharacterStatRules.Range |
| 0x4B0FD0 | 208 | R | row added and marked | stat maximum: CharacterStatRules.Range |
| 0x4B10A0 | 208 | S | not yet read (F0 row) | stat change helper of the skill key stat guard; not yet read |
| 0x4B1170 | 416 | S | implemented, unmarked | poison event handler (TE10), implemented in the game event rules |
| 0x4B1310 | 64 | R | not relevant: table or constant accessor | poison event match test for the event queue; our queue keys events by owner and kind |
| 0x4B1350 | 240 | S | implemented per documents, no address in code | poison event schedule, implemented in the game event rules |

### Spell table (0x4B1440–0x4B1D4F)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4B1520 | 16 | R | not relevant: module lifecycle | empty module callback |
| 0x4B1530 | 64 | R | implemented, unmarked | empty spell book (every rank 0), SpellBook.Empty |
| 0x4B15A0 | 96 | R | not relevant: interface and viewport | longest spell name length, for window layout |
| 0x4B1740 | 16 | R | not relevant: table or constant accessor | constant 100 returned to the spell cost functions |
| 0x4B1750 | 16 | R | not relevant: wrapper of another function | minimum intelligence of a rank: forwards to 0x4502E0 |
| 0x4B1760 | 16 | R | not relevant: wrapper of another function | minimum willpower of a rank: forwards to 0x4502F0, which has its own row |
| 0x4B1AB0 | 80 | R | implemented, unmarked | critter's level in a college, a host query (SessionDialogCollegePort and the spell book) |
| 0x4B1B30 | 96 | R | implemented per documents, no address in code | college level set, applied by the spell book rules of learn and unlearn |
| 0x4B1CB0 | 64 | R | implemented per documents, no address in code | mastered college of a critter (SpellBook.MasteredCollege) |

### Combat (0x4B1D50–0x4B83E0)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4B1D50 | 256 | R | not relevant: module lifecycle | combat module init: loads the combat texts and registers four settings |
| 0x4B1E50 | 48 | R | not relevant: module lifecycle | combat module exit |
| 0x4B1E80 | 32 | R | not relevant: module lifecycle | combat module reset: ends the round |
| 0x4B1EA0 | 384 | S | implemented per documents, no address in code | combat save: the round is saved in the session save schema (original-persistence) |
| 0x4B2020 | 496 | S | implemented per documents, no address in code | combat load: the round is restored from the session save |
| 0x4B2210 | 416 | S | implemented, unmarked | attack context of an attacker (wielded weapon, unarmed values, sight, hearing), spread over GameSessionAttack and SpellLifeComponents |
| 0x4B23B0 | 32 | R | not relevant: wrapper of another function | wielded weapon of a critter (item in slot 1004) |
| 0x4B23D0 | 64 | R | not relevant: module lifecycle | settings callback of the turn-based setting; our settings are our own |
| 0x4B2410 | 224 | R | not relevant: module lifecycle | settings callback of the fast turn-based setting; our settings are our own |
| 0x4B24F0 | 352 | S | implemented, unmarked | projectile creation for a ranged attack (GameSessionProjectileObjects, GameSessionProjectiles) |
| 0x4B2650 | 64 | R | not relevant: wrapper of another function | projectile end without a target, calls 0x4B2690 |
| 0x4B2810 | 96 | R | full (read; ARC-COMBAT-1930) | the loudness of a weapon's attack event (1 for no object or a non-weapon, 2 for flag 0x1, 0 for flag 0x2 of weapon field 0x70); `AwarenessRules.WeaponLoudness` |
| 0x4B2F60 | 528 | S | implemented, unmarked | projectile hit resolution, miss and damage (AttackResolver, GameSessionAttack) |
| 0x4B3170 | 1536 | S | implemented, unmarked | attack resolution (1536 bytes), spread over GameSessionAttack, GameSessionNpcAttack and AttackResolver |
| 0x4B3770 | 576 | S | implemented per documents, no address in code | melee attack, spread over GameSessionAttack and AttackResolver |
| 0x4B3BB0 | 80 | R | not relevant: wrapper of another function | hit step wrapper: builds the attack context then runs 0x4B3170 |
| 0x4B3D50 | 64 | R | not relevant: table or constant accessor | combat mode flag of a critter |
| 0x4B3F20 | 48 | R | implemented, unmarked | leave combat mode (calls the toggle when set), GameSessionCombatMode |
| 0x4B3F50 | 48 | R | implemented, unmarked | enter combat mode (calls the toggle when clear), AreaAiHost |
| 0x4B3F80 | 928 | S | implemented, unmarked | combat mode toggle (928 bytes), GameSessionCombatMode and WeaponArtRules |
| 0x4B5520 | 96 | S | not yet read (F0 row) | walks a two-entry callback table of the critical effects; not yet read |
| 0x4B5E90 | 160 | S | not yet read (F0 row) | destroys a combat object (160 bytes); not yet read |
| 0x4B5F30 | 16 | R | not relevant: table or constant accessor | callback by index from a two-entry table |
| 0x4B65A0 | 48 | R | row added and marked | hit location roll: AttackRules.HitLocationFromRoll |
| 0x4B65D0 | 176 | R | implemented, unmarked | ranged attack ammunition gate, spread over AttackTypes, CriticalEffects and GameSessionAttack |
| 0x4B6680 | 480 | S | implemented per documents, no address in code | attack damage roll (480 bytes), AttackResolver |
| 0x4B6860 | 208 | S | implemented per documents, no address in code | armour resistance step of the damage, oracle model and AttackResolver |
| 0x4B6930 | 208 | S | implemented, unmarked | entangled branch of the hit effect, SessionSpellCasterEffects |
| 0x4B6B90 | 176 | R | row added and marked | taunt kind: FollowerRules.TauntKindOf |
| 0x4B6C40 | 64 | R | not relevant: module lifecycle | stores the five interface callbacks of the round into globals |
| 0x4B6C80 | 16 | R | not relevant: table or constant accessor | turn-based setting value |
| 0x4B6C90 | 144 | S | not yet read (F0 row) | toggles turn-based mode (144 bytes); not yet read as a whole |
| 0x4B6D20 | 80 | S | not yet read (F0 row) | turn-based setting toggle by the hero's combat state; not yet read as a whole |
| 0x4B6D70 | 16 | R | not relevant: table or constant accessor | a round runs |
| 0x4B6D80 | 32 | R | not relevant: table or constant accessor | current participant of the round |
| 0x4B6DA0 | 208 | S | not yet read (F0 row) | round trace line writer (208 bytes); not yet read |
| 0x4B6E70 | 416 | S | implemented, unmarked | round set-up, the participants' scan (GameSessionCombat and the AI host) |
| 0x4B71E0 | 288 | S | implemented, unmarked | round start, the participants' runs (AreaAiHost) |
| 0x4B7300 | 48 | R | row added and marked | round scan radius: TurnBasedRules.ScanRadius |
| 0x4B7330 | 112 | R | implemented per documents, no address in code | round end clear, GameSessionCombat |
| 0x4B73A0 | 480 | S | implemented per documents, no address in code | round advance to the next participant, GameSessionCombat |
| 0x4B7580 | 80 | R | implemented, unmarked | participant skip when it cannot act, GameSessionCombat and GameSessionCritterStates |
| 0x4B75D0 | 96 | R | implemented, unmarked | subturn start, the AI host |
| 0x4B7630 | 96 | R | implemented, unmarked | subturn end of an NPC, the AI host |
| 0x4B7690 | 176 | S | implemented per documents, no address in code | subturn step of the round, GameSessionCombat |
| 0x4B7740 | 64 | R | implemented per documents, no address in code | round end, GameSessionCombat |
| 0x4B7780 | 16 | R | not relevant: table or constant accessor | action points left of the actor |
| 0x4B7790 | 160 | R | row added and marked | action point check: TurnBasedRules.CanAfford |
| 0x4B7830 | 160 | S | not yet read (F0 row) | action cost check for taking an item (160 bytes); not yet read as a whole |
| 0x4B78D0 | 208 | R | row added and marked | attack cost preview: TurnBasedRules.AttackActionCost |
| 0x4B79A0 | 128 | R | row added and marked | use object cost preview: TurnBasedRules.UseObjectActionCost |
| 0x4B7A20 | 128 | R | row added and marked | pick up cost preview: TurnBasedRules.PickUpActionCost |
| 0x4B7AA0 | 64 | R | not relevant: wrapper of another function | fixed cost of four action points and the check 0x4B7790 (cast, skill or item use); TurnBasedRules.CastSpellCost, UseSkillCost, UseItemCost |
| 0x4B7AE0 | 192 | S | not yet read (F0 row) | action cost check for use of an item or object (192 bytes); not yet read as a whole |
| 0x4B7BA0 | 128 | R | row added and marked | move cost estimate: TurnBasedRules.MoveCost |
| 0x4B7C20 | 16 | R | not relevant: table or constant accessor | pending action cost |
| 0x4B7C30 | 96 | R | row added and marked | attack cost: TurnBasedRules.AttackCost |
| 0x4B7C90 | 64 | R | implemented, unmarked | a planner failure ends an NPC's subturn, ExecutorRules |
| 0x4B7CD0 | 240 | R | row added and marked | action point charge: TurnBasedRules.Charge |
| 0x4B7DC0 | 64 | R | implemented per documents, no address in code | skip rule of the round: a hidden critter that is not asleep is skipped (TurnBasedRules.IsSkipped) |
| 0x4B7E00 | 176 | S | implemented, unmarked | an NPC joins the turn-based round, HostilityTypes and the AI host |
| 0x4B7EB0 | 400 | S | not yet read (F0 row) | round participants list rebuild (400 bytes); not yet read |
| 0x4B8040 | 144 | S | implemented, unmarked | critter turns toward a target by the short way, HeroInputRules.RotateStep |
| 0x4B80D0 | 16 | R | not relevant: table or constant accessor | id of the round in progress |
| 0x4B80E0 | 208 | S | implemented, unmarked | combat category refresh of the owner, GameSessionDanger |
| 0x4B81B0 | 128 | S | not yet read (F0 row) | combat floating text (128 bytes); not yet read |
| 0x4B8230 | 80 | R | not relevant: table or constant accessor | auto-attack setting of the hero |
| 0x4B8280 | 96 | R | not relevant: wrapper of another function | auto-attack setting writer |
| 0x4B82E0 | 80 | R | not relevant: table or constant accessor | taunt setting |
| 0x4B8330 | 80 | R | not relevant: table or constant accessor | fast turn-based setting of the hero |
| 0x4B8380 | 96 | R | not relevant: wrapper of another function | fast turn-based setting writer |
| 0x4B83E0 | 96 | S | not yet read (F0 row) | tests whether a critter's weapon can fire (96 bytes); not yet read |

### Coordinates (0x4B8440–0x4B99C0)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4B8440 | 240 | S | not relevant: interface and viewport | screen rectangle set-up of the view at a resolution change |
| 0x4B8530 | 16 | R | not relevant: module lifecycle | empty module callback |
| 0x4B8540 | 96 | R | not relevant: interface and viewport | copies the view rectangle into globals |
| 0x4B85A0 | 224 | S | not relevant: interface and viewport | view rectangle to tile corners |
| 0x4B8940 | 400 | S | not relevant: interface and viewport | screen rectangle to tile rectangle over the view |
| 0x4B8AD0 | 96 | R | not relevant: interface and viewport | reads or writes the tile rectangle of the view |
| 0x4B8B30 | 432 | S | not relevant: interface and viewport | tile rectangle to screen rectangle over the view |
| 0x4B8CE0 | 96 | R | not relevant: interface and viewport | redraws the tile rectangle of the view (World viewer draws its own) |
| 0x4B8D40 | 16 | R | not relevant: interface and viewport | sets the redraw callback |
| 0x4B8FF0 | 224 | S | implemented, unmarked | step to the neighbouring tile in a direction, the grid's neighbour step used by the line of fire, knockback, drops and the path request |
| 0x4B9130 | 704 | S | implemented, unmarked | tile reach of the view rectangle, ExperienceRules.WaitingStepReach |
| 0x4B93F0 | 48 | R | not relevant: interface and viewport | screen offset to tile offset pair for the scene plan |
| 0x4B9420 | 720 | S | implemented, unmarked | location normalisation to the tile an anchor point lies in (Arcanum.World grid) |
| 0x4B9760 | 128 | S | not relevant: interface and viewport | screen geometry helper of the view |
| 0x4B97E0 | 48 | R | not relevant: interface and viewport | reads two stored view positions |
| 0x4B9810 | 160 | S | not relevant: interface and viewport | isometric projection helper (floating point) |
| 0x4B98B0 | 272 | S | not relevant: interface and viewport | isometric projection helper (floating point) |
| 0x4B99C0 | 464 | S | not yet read (F0 row) | area test of a spell target over a tile rectangle with a path request (464 bytes); not yet read |

### Game parameters (0x4B9B90–0x4B9DB0)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4B9B90 | 80 | R | not relevant: module lifecycle | game parameter reset at a new game |
| 0x4B9BE0 | 64 | R | implemented, unmarked | main menu music start (MainMenuSound, Arcanum.World) |
| 0x4B9C20 | 16 | R | not relevant: module lifecycle | game parameter flag clear |
| 0x4B9C30 | 384 | S | implemented, unmarked | game parameter module init: start date and time and the main menu music numbers (MainMenuSound, Arcanum.World) |
| 0x4B9DB0 | 96 | R | not relevant: module lifecycle | game parameter module exit |

### Party of the network group (0x4B9E10–0x4BA320)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4B9E10 | 16 | R | not relevant: module lifecycle | party module reset |
| 0x4B9E20 | 16 | R | not relevant: module lifecycle | party module reset |
| 0x4B9E30 | 32 | R | not relevant: multiplayer only | party slot table cleared to none (eight slots) |
| 0x4B9E50 | 112 | S | not relevant: multiplayer only | party slot table save |
| 0x4B9EC0 | 128 | S | not relevant: multiplayer only | party slot table load |
| 0x4B9F40 | 128 | S | not relevant: multiplayer only | party slot of a player |
| 0x4B9FC0 | 96 | R | not relevant: multiplayer only | next occupied party slot |
| 0x4BA020 | 96 | S | not relevant: multiplayer only | party slot of a handle |
| 0x4BA080 | 352 | S | not relevant: multiplayer only | a player joins a party |
| 0x4BA1E0 | 128 | S | not relevant: multiplayer only | a player leaves a party |
| 0x4BA260 | 48 | R | not relevant: multiplayer only | party slot compaction |
| 0x4BA290 | 80 | S | not relevant: multiplayer only | party slot test |
| 0x4BA2E0 | 64 | R | not relevant: multiplayer only | party slot table from the network |
| 0x4BA320 | 96 | S | not relevant: multiplayer only | party slot update helper |

### Monster generators (0x4BA380–0x4BABF0)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4BA380 | 128 | S | not relevant: interface and viewport | monster generator view rectangle init |
| 0x4BA400 | 32 | R | not relevant: memory or list helper | clears the 256 generator counters |
| 0x4BA420 | 16 | R | not relevant: memory or list helper | frees the generator counter table |
| 0x4BA430 | 48 | R | not relevant: interface and viewport | copies the view rectangle for the generators |
| 0x4BA460 | 48 | S | not yet read (F0 row) | generator counters save blob; not yet matched to our save schema |
| 0x4BA490 | 112 | S | not yet read (F0 row) | generator counters load blob; not yet matched to our save schema |
| 0x4BA500 | 144 | S | not yet read (F0 row) | monster generator count query helper; not yet read as a whole |
| 0x4BA590 | 144 | S | not yet read (F0 row) | monster generator spawn helper; not yet read as a whole |
| 0x4BA620 | 176 | S | not yet read (F0 row) | monster generator spawn helper; not yet read as a whole |
| 0x4BA6D0 | 80 | S | not yet read (F0 row) | monster generator spawn helper; not yet read as a whole |
| 0x4BA720 | 112 | S | not yet read (F0 row) | monster generator placement helper; not yet read as a whole |
| 0x4BA790 | 384 | S | implemented, unmarked | monster generator spawner tick, a host port (SchedulerRules.Spawn, AreaAiHost in Arcanum.World) |
| 0x4BA910 | 544 | S | not yet read (F0 row) | monster generator placement (544 bytes); not yet read as a whole |
| 0x4BAB30 | 112 | S | not yet read (F0 row) | generator slot release after a death; not yet read as a whole |
| 0x4BABA0 | 48 | S | not yet read (F0 row) | generator blocked flag test (bit 0x80 of the counter); script condition 46 reads it, not yet matched |
| 0x4BABD0 | 32 | S | not yet read (F0 row) | generator blocked flag clear; not yet matched |
| 0x4BABF0 | 32 | S | not yet read (F0 row) | generator blocked flag set; not yet matched |

### Run list nodes (0x4BAC10–0x4BAF50)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4BAC10 | 16 | R | not relevant: module lifecycle | node pool init |
| 0x4BAC20 | 16 | R | not relevant: module lifecycle | node pool exit |
| 0x4BAC30 | 80 | S | not relevant: memory or list helper | node from the pool (run list nodes) |
| 0x4BAC80 | 32 | R | not relevant: memory or list helper | node back to the pool |
| 0x4BACA0 | 96 | S | not relevant: memory or list helper | node pool block allocation |
| 0x4BAD00 | 112 | S | implemented per documents, no address in code | saves a run list (the session save keeps the runs, not the node pool) |
| 0x4BAD70 | 96 | S | implemented per documents, no address in code | saves a list of run entries |
| 0x4BADD0 | 128 | S | implemented per documents, no address in code | loads a run list |
| 0x4BAE50 | 144 | S | implemented per documents, no address in code | loads a list of run entries |
| 0x4BAEE0 | 112 | S | implemented per documents, no address in code | saves a second run list |
| 0x4BAF50 | 160 | S | implemented per documents, no address in code | loads a second run list |

### Manual tile block tool (0x4BAFF0–0x4BB590)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4BAFF0 | 96 | S | not relevant: interface and viewport | manual tile block tool init |
| 0x4BB050 | 16 | R | not relevant: module lifecycle | empty module callback |
| 0x4BB060 | 32 | R | not relevant: interface and viewport | manual tile block tool position |
| 0x4BB080 | 64 | R | not relevant: interface and viewport | manual tile block tool state |
| 0x4BB0C0 | 848 | S | not relevant: interface and viewport | manual tile block draw |
| 0x4BB410 | 384 | S | implemented per documents, no address in code | tile blocked test on the sector's block mask (sector passability reads the same mask) |
| 0x4BB590 | 400 | S | not relevant: interface and viewport | manual tile block draw helper |

### Indicators over the view (0x4BB720–0x4BBCE0)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4BB720 | 416 | S | not relevant: interface and viewport | combat indicator init |
| 0x4BB8C0 | 32 | R | not relevant: interface and viewport | combat indicator free |
| 0x4BB8E0 | 16 | R | not relevant: interface and viewport | combat indicator counter increment |
| 0x4BB8F0 | 16 | R | not relevant: interface and viewport | combat indicator counter decrement |
| 0x4BB900 | 16 | R | not relevant: interface and viewport | combat indicator counter read |
| 0x4BB910 | 16 | R | not relevant: module lifecycle | empty module callback |
| 0x4BB920 | 96 | S | not relevant: interface and viewport | combat indicator redraw |
| 0x4BB980 | 32 | R | not relevant: interface and viewport | combat indicator art helper |
| 0x4BB9A0 | 352 | S | not relevant: interface and viewport | load indicator init |
| 0x4BBB00 | 32 | R | not relevant: interface and viewport | load indicator free |
| 0x4BBB20 | 224 | S | not relevant: interface and viewport | load indicator size and corner (AreaTransitionPanel) |
| 0x4BBC00 | 16 | R | not relevant: interface and viewport | load indicator movie counter increment |
| 0x4BBC10 | 32 | R | not relevant: interface and viewport | load indicator movie counter decrement and read |
| 0x4BBC30 | 80 | S | not relevant: interface and viewport | load indicator tick, called by the sector loader |
| 0x4BBC80 | 96 | S | not relevant: interface and viewport | load indicator redraw |
| 0x4BBCE0 | 32 | R | not relevant: interface and viewport | load indicator art helper |

### Traps (0x4BBD00–0x4BD950)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4BBD00 | 144 | S | not relevant: module lifecycle | trap module init |
| 0x4BBD90 | 64 | R | not relevant: module lifecycle | trap module exit |
| 0x4BCAB0 | 80 | S | implemented, unmarked | first trap on a tile and whether the owner avoids it (AreaDropMap, Arcanum.World) |
| 0x4BD1E0 | 352 | S | not relevant: multiplayer only | trap list of a player (multiplayer) |
| 0x4BD340 | 240 | S | not relevant: multiplayer only | trap list of a player (multiplayer) |
| 0x4BD430 | 80 | S | not relevant: multiplayer only | trap list of a player (multiplayer) |
| 0x4BD480 | 208 | S | not relevant: multiplayer only | trap list of a player helper (multiplayer) |
| 0x4BD550 | 448 | S | not yet read (F0 row) | trap lists save (448 bytes); not yet read as a whole |
| 0x4BD710 | 320 | S | not yet read (F0 row) | trap lists load (320 bytes); not yet read as a whole |
| 0x4BD850 | 256 | S | not yet read (F0 row) | trap list clear and trap object flags (256 bytes); not yet read as a whole |

### Teleport bans (0x4BD980–0x4BDD30)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4BD980 | 64 | S | not relevant: module lifecycle | teleport ban module reset |
| 0x4BD9C0 | 32 | R | not relevant: module lifecycle | teleport ban module reset |
| 0x4BD9E0 | 208 | S | not yet read (F0 row) | teleport ban lists load from the message files (208 bytes); not yet read |
| 0x4BDAB0 | 16 | R | not relevant: module lifecycle | teleport ban module reset |
| 0x4BDAC0 | 80 | S | not relevant: memory or list helper | teleport ban lists free |
| 0x4BDB10 | 32 | R | not relevant: memory or list helper | clears one teleport ban list head |
| 0x4BDB30 | 272 | S | not yet read (F0 row) | teleport ban list parse (272 bytes); not yet read |
| 0x4BDC40 | 32 | R | not relevant: memory or list helper | clears one teleport ban list head |
| 0x4BDC60 | 208 | S | not yet read (F0 row) | teleport ban list parse (208 bytes); not yet read |
| 0x4BDD30 | 320 | S | implemented, unmarked | whether the parent may be teleported where it stands (SpellInvocationRules) |

### Screen fade and gamma (0x4BDE70–0x4BE0F0)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4BDE70 | 240 | S | not relevant: interface and viewport | screen fade module init |
| 0x4BDF60 | 32 | R | not relevant: interface and viewport | screen fade tick |
| 0x4BDF80 | 32 | R | not relevant: interface and viewport | screen fade colour set |
| 0x4BDFA0 | 176 | S | not relevant: interface and viewport | screen fade start (AreaChangeOfPlace starts its own fade) |
| 0x4BE050 | 96 | S | not relevant: interface and viewport | screen fade end |
| 0x4BE0B0 | 48 | R | not relevant: interface and viewport | gamma module init |
| 0x4BE0E0 | 16 | R | not relevant: module lifecycle | empty module callback |
| 0x4BE0F0 | 192 | S | not relevant: interface and viewport | gamma setting callback |

### Town maps (0x4BE1B0–0x4BEFB0)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4BE1B0 | 16 | R | not relevant: module lifecycle | town map module reset |
| 0x4BE1C0 | 336 | S | not yet read (F0 row) | town map module init: loads the town map lists (336 bytes); not yet read |
| 0x4BE310 | 96 | S | not relevant: module lifecycle | town map module exit |
| 0x4BE370 | 16 | R | not relevant: module lifecycle | town map module reset |
| 0x4BE380 | 128 | S | implemented, unmarked | town map of the area at a location through the sector lock (WorldTravelRules) |
| 0x4BE400 | 32 | R | not relevant: table or constant accessor | town map list entry by index |
| 0x4BE420 | 192 | S | implemented per documents, no address in code | town map text of a number (the viewer's town map texts) |
| 0x4BE4E0 | 400 | S | implemented per documents, no address in code | town map state of a cell (GameSessionTownMaps) |
| 0x4BE670 | 272 | S | not relevant: interface and viewport | town map tile to picture position |
| 0x4BE780 | 368 | S | implemented per documents, no address in code | tile of a clicked town map pixel (AreaTownMapArt, Arcanum.World) |
| 0x4BE8F0 | 448 | S | not yet read (F0 row) | town map reveal around the PC (448 bytes); not yet read as a whole |
| 0x4BEAF0 | 464 | S | implemented per documents, no address in code | town map neighbour table (AreaTownMapArt, Arcanum.World) |
| 0x4BECC0 | 64 | S | not yet read (F0 row) | town map data for waiting (the waiting interface is not modelled); not yet read |
| 0x4BED00 | 48 | R | not relevant: memory or list helper | town map state free |
| 0x4BED30 | 96 | S | implemented, unmarked | reveal every cell of a town map (ScrInterpreter, script operand) |
| 0x4BEDD0 | 144 | S | implemented, unmarked | town map cell bit read (GameSessionTownMaps) |
| 0x4BEE60 | 224 | S | implemented per documents, no address in code | town map state load and save (the session save keeps the town map bits) |
| 0x4BEF40 | 112 | S | implemented per documents, no address in code | town map state clear |
| 0x4BEFB0 | 176 | S | implemented per documents, no address in code | town map state save |

### Newspapers (0x4BF060–0x4BF330)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4BF060 | 32 | R | not relevant: module lifecycle | newspaper module init |
| 0x4BF080 | 48 | R | implemented, unmarked | newspaper state reset (GameSessionNewspaper) |
| 0x4BF0B0 | 16 | R | not relevant: module lifecycle | newspaper module exit |
| 0x4BF0C0 | 64 | S | implemented, unmarked | newspaper state load (GameSessionNewspaper) |
| 0x4BF100 | 64 | S | implemented, unmarked | newspaper state save (GameSessionNewspaper) |
| 0x4BF1D0 | 48 | S | not yet read (F0 row) | newspaper issue known test; not yet read |
| 0x4BF200 | 16 | R | not relevant: table or constant accessor | newspaper queue entry by index |
| 0x4BF210 | 96 | S | not yet read (F0 row) | written item kind test (96 bytes); not yet read |
| 0x4BF270 | 80 | S | implemented, unmarked | newspaper update on game time (GameSessionNewspaper) |
| 0x4BF2C0 | 112 | S | implemented, unmarked | newspaper issue pick (GameSessionNewspaper) |
| 0x4BF330 | 96 | S | not yet read (F0 row) | newspaper random choice (96 bytes); not yet read |

### Inventory sources (0x4BF390–0x4BFCA0)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4BF390 | 384 | S | implemented per documents, no address in code | inventory source file load (the data reader of InvenSource.mes) |
| 0x4BF510 | 192 | S | not relevant: memory or list helper | inventory source tables free |
| 0x4BF5D0 | 112 | S | implemented, unmarked | inventory source set as the generation reads it (InventorySourceRules, RestockRules) |
| 0x4BFCA0 | 64 | S | not yet read (F0 row) | inventory source line helper called from 17 places; not yet read as a whole |

### Waypoint markers and sector script (0x4BFCE0–0x4C0490)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4BFCE0 | 160 | S | not relevant: interface and viewport | waypoint marker init |
| 0x4BFD80 | 32 | R | not relevant: module lifecycle | waypoint marker reset |
| 0x4BFDA0 | 16 | R | not relevant: interface and viewport | waypoint marker state |
| 0x4BFDB0 | 80 | S | not relevant: interface and viewport | waypoint marker state |
| 0x4BFE00 | 656 | S | not relevant: interface and viewport | waypoint marker draw |
| 0x4C0090 | 64 | S | not relevant: interface and viewport | waypoint marker helper |
| 0x4C00D0 | 112 | S | not relevant: interface and viewport | waypoint marker helper |
| 0x4C0140 | 320 | S | not relevant: interface and viewport | waypoint marker helper |
| 0x4C0280 | 240 | S | not relevant: interface and viewport | waypoint marker helper |
| 0x4C0370 | 160 | S | not relevant: interface and viewport | waypoint marker helper |
| 0x4C0410 | 16 | R | not relevant: module lifecycle | empty module callback |
| 0x4C0420 | 16 | R | not relevant: module lifecycle | empty module callback |
| 0x4C0430 | 16 | R | not relevant: module lifecycle | empty module callback |
| 0x4C0440 | 80 | S | not yet read (F0 row) | sector script read through the sector lock (80 bytes); not yet read |
| 0x4C0490 | 160 | S | not yet read (F0 row) | sector script write through the sector lock (160 bytes); not yet read |

### Tile scripts (0x4C0530–0x4C0A40)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4C0530 | 128 | S | not relevant: interface and viewport | tile script marker init |
| 0x4C05B0 | 16 | R | not relevant: module lifecycle | empty module callback |
| 0x4C05C0 | 16 | R | not relevant: module lifecycle | tile script module reset |
| 0x4C05D0 | 16 | R | not relevant: interface and viewport | tile script marker state |
| 0x4C05E0 | 80 | S | not relevant: interface and viewport | tile script marker state |
| 0x4C0630 | 384 | S | not relevant: interface and viewport | tile script marker draw |
| 0x4C07B0 | 128 | S | implemented per documents, no address in code | tile script read through the sector lock (the tile script host) |
| 0x4C0830 | 256 | S | implemented per documents, no address in code | tile script run (the tile script host, ScrRuleProfile) |
| 0x4C0930 | 80 | S | implemented per documents, no address in code | tile script run helper (the tile script host) |
| 0x4C0980 | 192 | S | implemented, unmarked | tile script set and run, with the X3 original bug (MapTransitions, ScrRuleProfile) |
| 0x4C0A40 | 400 | S | not relevant: interface and viewport | tile script marker draw |

### Reaction (0x4C0BD0–0x4C15A0)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4C0BD0 | 96 | S | implemented per documents, no address in code | reaction category names from reaction.mes (AreaGameBar, Arcanum.World) |
| 0x4C0C30 | 16 | R | not relevant: module lifecycle | reaction module exit |
| 0x4C0CC0 | 32 | R | not relevant: wrapper of another function | standard effective reaction, calls 0x4C0D00 with mode 0 |
| 0x4C0CE0 | 32 | R | not relevant: wrapper of another function | effective reaction for the hostility test, calls 0x4C0D00 with mode 1 |
| 0x4C0F50 | 112 | S | implemented, unmarked | the NPC forgets having met the PC (AreaShopMerchant, Arcanum.World) |
| 0x4C1010 | 16 | R | not relevant: table or constant accessor | reaction category name by index |
| 0x4C1110 | 64 | S | implemented, unmarked | conversation partner of an NPC, a scheduler port (SchedulerTypes) |
| 0x4C1290 | 64 | S | not yet read (F0 row) | reaction memory slot read (ledger R3); not matched to one method |
| 0x4C12D0 | 32 | R | not relevant: wrapper of another function | reaction slot field read (field 0x127 of the object) |
| 0x4C12F0 | 112 | S | not yet read (F0 row) | reaction memory slot search (ledger R3); not matched to one method |
| 0x4C1360 | 304 | S | not yet read (F0 row) | reaction memory slot write (ledger R3); not matched to one method |
| 0x4C1490 | 112 | S | not yet read (F0 row) | reaction memory slot clear (ledger R3); not matched to one method |
| 0x4C15A0 | 48 | S | not yet read (F0 row) | reaction scale step (48 bytes); not yet read |

### Reputation (0x4C15D0–0x4C21E0)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4C15D0 | 48 | R | not relevant: module lifecycle | reputation module init (allocations) |
| 0x4C1600 | 32 | R | not relevant: module lifecycle | reputation module exit |
| 0x4C16E0 | 80 | S | not relevant: module lifecycle | reputation module free |

### Backgrounds (0x4C2270–0x4C2B50)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4C22D0 | 48 | R | not relevant: module lifecycle | background module exit |
| 0x4C26F0 | 608 | S | implemented, unmarked | background items and money given at creation (OpeningKitRules, BackgroundTable) |
| 0x4C2950 | 288 | S | implemented per documents, no address in code | Educator background update after a training change (SkillRules, GameSessionSkills; the oracle model) |
| 0x4C2AA0 | 112 | S | not relevant: multiplayer only | multiplayer money scaling of the background |
| 0x4C2B10 | 64 | S | implemented, unmarked | background item list read (BackgroundTable block slots) |

### Spoken orders (0x4C2C10–0x4C3BE0)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4C2C10 | 528 | S | not yet read (F0 row) | spoken orders table load (528 bytes); not yet read |
| 0x4C2E20 | 128 | S | not yet read (F0 row) | spoken orders table free |
| 0x4C2EA0 | 16 | R | not relevant: table or constant accessor | stores the order mode |
| 0x4C2EB0 | 80 | S | not yet read (F0 row) | spoken order text load; not yet read |
| 0x4C2F00 | 160 | R | not relevant: wrapper of another function | broadcast order: outside a network client calls the party order handler 0x4C31A0; the client branch is multiplayer |
| 0x4C2FA0 | 160 | S | not yet read (F0 row) | spoken order matcher helper; not yet read |
| 0x4C3040 | 352 | S | not yet read (F0 row) | spoken order text matching (352 bytes); not yet read |
| 0x4C3B40 | 80 | S | not yet read (F0 row) | spoken order helper; not yet read |
| 0x4C3B90 | 80 | S | not yet read (F0 row) | spoken order text; not yet read |

### Curses and blessings (0x4C3C60–0x4C4450)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4C3C60 | 32 | R | not relevant: module lifecycle | curse module init |
| 0x4C3C80 | 32 | R | not relevant: module lifecycle | curse module exit |
| 0x4C3CA0 | 32 | R | not relevant: wrapper of another function | curse name text, calls 0x4C3CC0 with kind 0 |
| 0x4C3CC0 | 112 | R | implemented per documents, no address in code | curse text by number and kind (the logbook texts, LogbookTexts) |
| 0x4C3D30 | 32 | R | not relevant: wrapper of another function | curse description text, calls 0x4C3CC0 with kind 2 |
| 0x4C3D50 | 128 | S | implemented, unmarked | first curse number that has a name (LogbookTexts) |
| 0x4C3DD0 | 112 | S | implemented per documents, no address in code | curse list helper of the logbook texts |
| 0x4C4110 | 32 | R | not relevant: module lifecycle | blessing module init |
| 0x4C4130 | 32 | R | not relevant: module lifecycle | blessing module exit |
| 0x4C4150 | 32 | R | not relevant: wrapper of another function | blessing name text, calls 0x4C4170 with kind 0 |
| 0x4C4170 | 112 | S | implemented per documents, no address in code | blessing text by number and kind (the logbook texts, LogbookTexts) |
| 0x4C41E0 | 32 | R | not relevant: wrapper of another function | blessing description text, calls 0x4C4170 with kind 2 |
| 0x4C4200 | 128 | S | implemented, unmarked | first blessing number that has a name (LogbookTexts) |
| 0x4C4280 | 112 | S | implemented per documents, no address in code | blessing list helper of the logbook texts |

### Quests (0x4C45C0–0x4C5400)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4C4620 | 32 | R | implemented per documents, no address in code | sets all 1000 global quest states to 2, Accepted (ledger QL2); the quest state table of the session |
| 0x4C4640 | 48 | R | not relevant: module lifecycle | quest module exit |
| 0x4C47C0 | 64 | S | not relevant: module lifecycle | quest module free |
| 0x4C4800 | 48 | R | implemented per documents, no address in code | quest state save (original-persistence) |
| 0x4C4830 | 48 | R | implemented per documents, no address in code | quest state load (original-persistence) |
| 0x4C4860 | 928 | S | implemented per documents, no address in code | quest list load from the message file (the data reader) |
| 0x4C4C00 | 176 | S | read, ARC-SCRIPT-1532 | quest reply chooser: the PC's quest state, Intelligence below 5, reaction below 20, picks a table field ([DQR2](social.md#quest-state-reply-arc-script-1532)) |
| 0x4C4E60 | 528 | S | implemented, unmarked | quest state change with experience (ReactionRules mention, the quest rules) |
| 0x4C5070 | 304 | S | not yet read (F0 row) | quest logbook helper (304 bytes); not yet read as a whole |
| 0x4C51A0 | 32 | R | implemented, unmarked | quest state by index (DialogConversationGrammar) |
| 0x4C51C0 | 144 | S | not relevant: multiplayer only | quest transfer to a player (multiplayer) |
| 0x4C53A0 | 32 | S | not yet read (F0 row) | quest list compare helper; not yet read |
| 0x4C5400 | 256 | S | not relevant: multiplayer only | quest transfer between players (multiplayer) |

### Rumors (0x4C5500–0x4C5B10)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4C5500 | 48 | R | not relevant: module lifecycle | rumor module init |
| 0x4C5530 | 32 | R | not relevant: memory or list helper | rumor table cleared |
| 0x4C5550 | 32 | R | not relevant: module lifecycle | rumor module exit |
| 0x4C5570 | 80 | S | not relevant: module lifecycle | rumor module load |
| 0x4C55C0 | 48 | R | not relevant: memory or list helper | rumor texts free |
| 0x4C55F0 | 48 | R | implemented per documents, no address in code | rumor state save (original-persistence) |
| 0x4C5620 | 48 | R | implemented per documents, no address in code | rumor state load (original-persistence) |
| 0x4C5650 | 64 | S | implemented, unmarked | rumor quelled mark test (DialogConversation) |
| 0x4C5700 | 224 | S | not yet read (F0 row) | rumor logbook entry text (224 bytes); not yet read as a whole |
| 0x4C57E0 | 192 | S | implemented per documents, no address in code | rumor known test with a number (DialogConversation) |
| 0x4C58A0 | 48 | R | not relevant: wrapper of another function | rumor field read (field 0x109 of an object) |
| 0x4C58D0 | 80 | S | implemented, unmarked | rumor known test by rumor number (DialogConversation) |
| 0x4C5960 | 112 | S | not yet read (F0 row) | rumor text helper; not yet read as a whole |
| 0x4C59D0 | 112 | S | not yet read (F0 row) | rumor text helper; not yet read as a whole |
| 0x4C5AF0 | 32 | S | not yet read (F0 row) | rumor list compare helper; not yet read |
| 0x4C5B10 | 192 | S | not relevant: multiplayer only | rumor transfer to a player (multiplayer) |

### Skills (0x4C5BD0–0x4C91F0)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4C5BD0 | 384 | S | implemented, unmarked | skill module init: skill texts from skill.mes (SkillTexts) |
| 0x4C5D50 | 96 | S | not relevant: module lifecycle | skill module exit |
| 0x4C5DB0 | 16 | R | not relevant: module lifecycle | skill module free |
| 0x4C5DC0 | 32 | S | not yet read (F0 row) | saves the 4-byte training schedule global (0x6876C4) into the save; persisted state, not yet matched to our save schema |
| 0x4C5DE0 | 32 | S | not yet read (F0 row) | loads the 4-byte training schedule global (0x6876C4) from the save; persisted state, not yet matched to our save schema |
| 0x4C5E00 | 80 | S | not yet read (F0 row) | zeroes the twelve basic skill points and four tech skill points of an object (80 bytes); not yet matched to character creation |
| 0x4C62D0 | 16 | S | not yet read (F0 row) | formula of two arguments (a + 2) times (b + 1); its caller is not yet matched to code |
| 0x4C6510 | 16 | R | not relevant: table or constant accessor | constant 100 for the training chance |
| 0x4C6520 | 64 | S | not yet read (F0 row) | training chance of basic skill 8: table value of its level, doubled with training of one or more (64 bytes); not yet matched |
| 0x4C6560 | 32 | S | not yet read (F0 row) | training roll with the stored last value (32 bytes); not yet read |
| 0x4C69E0 | 16 | S | not yet read (F0 row) | formula of two arguments (a + 2) times (b + 1); its caller is not yet matched to code |
| 0x4C6F90 | 64 | R | not relevant: wrapper of another function | skill attempt wrapper: forwards to the skill starter 0x4350F0 with a fixed skill |
| 0x4C6FD0 | 64 | S | not relevant: wrapper of another function | skill attempt wrapper: forwards to the skill starter 0x4350F0 with a fixed skill |
| 0x4C7010 | 64 | S | not relevant: wrapper of another function | skill attempt wrapper: forwards to the skill starter 0x4350F0 with a fixed skill |
| 0x4C7050 | 64 | S | not relevant: wrapper of another function | skill attempt wrapper: forwards to the skill starter 0x4350F0 with a fixed skill |
| 0x4C7090 | 144 | S | implemented, unmarked | skill attempt invocation after the four action points (ExecutorHeroSkillGoals) |
| 0x4C7120 | 64 | S | not yet read (F0 row) | tests that three stored handles of a record are not empty (64 bytes); not yet matched |
| 0x4C83E0 | 80 | S | not yet read (F0 row) | critter is the local PC or is led by it (80 bytes); not yet matched |
| 0x4C8FA0 | 80 | S | not yet read (F0 row) | network client gate of the skill window (80 bytes); not yet read as a whole |
| 0x4C8FF0 | 96 | S | not yet read (F0 row) | network client gate of the skill window (96 bytes); not yet read as a whole |
| 0x4C9050 | 416 | S | not yet read (F0 row) | skill use result text (416 bytes); not yet read as a whole |
| 0x4C91F0 | 144 | S | implemented, unmarked | first healing item of an NPC, the heal skill's item (SupportTypes) |

### Dialog answer window and facades (0x4C9280–0x4CA7C0)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4C9280 | 704 | S | not relevant: interface and viewport | dialog answer window draw |
| 0x4C9540 | 96 | S | not relevant: interface and viewport | dialog answer window helper |
| 0x4C95A0 | 80 | S | not relevant: interface and viewport | dialog answer window helper |
| 0x4C95F0 | 48 | R | not relevant: interface and viewport | dialog answer window helper |
| 0x4C9620 | 160 | S | not relevant: interface and viewport | dialog answer window helper |
| 0x4C96C0 | 48 | R | not relevant: interface and viewport | dialog answer window helper |
| 0x4C96F0 | 288 | S | not relevant: interface and viewport | dialog answer window helper |
| 0x4C9810 | 512 | S | not relevant: interface and viewport | dialog answer window helper |
| 0x4C9A10 | 384 | S | not relevant: interface and viewport | dialog answer window helper |
| 0x4C9B90 | 80 | S | not relevant: interface and viewport | dialog answer window helper |
| 0x4C9BE0 | 448 | S | not relevant: interface and viewport | dialog answer window draw |
| 0x4C9DA0 | 64 | S | not relevant: interface and viewport | facade module state |
| 0x4C9DE0 | 16 | R | not relevant: interface and viewport | facade module reset |
| 0x4C9DF0 | 16 | R | not relevant: interface and viewport | facade module state |
| 0x4C9E00 | 112 | S | not relevant: interface and viewport | facade module init |
| 0x4C9E70 | 640 | S | not relevant: interface and viewport | facade draw |
| 0x4CA0F0 | 336 | S | not relevant: interface and viewport | facade draw helper |
| 0x4CA240 | 128 | S | not relevant: interface and viewport | facade free |
| 0x4CA2C0 | 1008 | S | not relevant: interface and viewport | facade draw |
| 0x4CA6B0 | 272 | S | not relevant: interface and viewport | facade helper |
| 0x4CA7C0 | 384 | S | not relevant: interface and viewport | facade helper |

### World map areas (0x4CA940–0x4CB6A0)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4CA940 | 16 | R | not relevant: module lifecycle | world map area module init |
| 0x4CA950 | 80 | S | not relevant: module lifecycle | world map area module state reset |
| 0x4CA9A0 | 16 | R | not relevant: module lifecycle | empty module callback |
| 0x4CACB0 | 160 | S | not relevant: memory or list helper | world map area tables free |
| 0x4CAD50 | 160 | S | implemented per documents, no address in code | world map area known bytes save (original-persistence) |
| 0x4CADF0 | 144 | S | implemented per documents, no address in code | world map area known bytes load (original-persistence) |
| 0x4CAE80 | 16 | R | not relevant: table or constant accessor | world map area count |
| 0x4CAE90 | 32 | R | not relevant: table or constant accessor | world map area id by index |
| 0x4CAEB0 | 32 | R | not relevant: table or constant accessor | world map area art by index |
| 0x4CAED0 | 48 | R | not relevant: table or constant accessor | world map area location pair by index |
| 0x4CAF00 | 80 | S | not yet read (F0 row) | world map area positions by index (80 bytes); not yet matched |
| 0x4CB100 | 96 | S | not yet read (F0 row) | last known area of a player; not yet matched |
| 0x4CB6A0 | 128 | S | implemented, unmarked | area of an object: the current map's list area at its location (DialogConversation, MapTransitions) |

### Item spell triggers (0x4CB720–0x4CC160)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4CB720 | 16 | R | not relevant: module lifecycle | item trigger module flag set |
| 0x4CB730 | 48 | S | not yet read (F0 row) | item trigger one-shot flag (48 bytes); not yet read as a whole |
| 0x4CB760 | 48 | R | implemented, unmarked | record trigger mask of a spell slot value (SessionSpellCasterItems) |
| 0x4CB790 | 16 | R | not relevant: table or constant accessor | low byte of a slot's spell number (ItemSpellTriggerRules.SpellMask) |
| 0x4CB7D0 | 48 | R | not relevant: wrapper of another function | item trigger on wear: runs the executor with the wear mask |
| 0x4CB800 | 48 | R | not relevant: wrapper of another function | item trigger executor wrapper, calls 0x4CB830 with fixed arguments |
| 0x4CBAA0 | 48 | R | not relevant: wrapper of another function | item trigger on pick up: runs the executor with the pick up mask |
| 0x4CBB80 | 112 | S | implemented, unmarked | item trigger on the parent being stunned (ItemSpellTriggerEvents) |
| 0x4CBBF0 | 112 | S | implemented, unmarked | item trigger on the parent going unconscious (ItemSpellTriggerEvents) |
| 0x4CBC60 | 224 | S | implemented, unmarked | item trigger on the parent dying (ItemSpellTriggerEvents) |
| 0x4CBD40 | 112 | S | implemented, unmarked | item trigger on the parent attacking an opponent (ItemSpellTriggerEvents, GameSessionItemTriggers) |
| 0x4CBE00 | 112 | S | implemented, unmarked | item trigger on the target going unconscious (ItemSpellTriggerEvents) |
| 0x4CBF70 | 80 | S | implemented, unmarked | item trigger on item used (ItemSpellTriggerEvents, SessionSpellCasterItems) |
| 0x4CBFC0 | 48 | R | not relevant: wrapper of another function | item trigger on unwear: runs the withdraw executor with the unwear mask |
| 0x4CC130 | 48 | R | not relevant: wrapper of another function | item trigger on drop: runs the withdraw executor with the drop mask |

### AI action lists (0x4CC1F0–0x4CCC40)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4CC1F0 | 32 | R | not relevant: module lifecycle | AI list module init |
| 0x4CC210 | 16 | R | not relevant: module lifecycle | AI list module reset |
| 0x4CC220 | 32 | R | not relevant: module lifecycle | AI list module exit |
| 0x4CC270 | 48 | R | not relevant: memory or list helper | AI list tables free |
| 0x4CC350 | 48 | R | implemented, unmarked | the ten global AI action lists built at init (ActionLists) |
| 0x4CC470 | 80 | S | not relevant: module lifecycle | AI list module state init |
| 0x4CC4C0 | 48 | R | implemented, unmarked | AI list cache clear, all forty slots (ActionLists) |
| 0x4CC4F0 | 48 | R | not relevant: memory or list helper | AI list header init |
| 0x4CC520 | 96 | S | implemented per documents, no address in code | AI list cache slot clear (ActionLists) |
| 0x4CC6F0 | 80 | S | implemented, unmarked | AI list cache of the Original setting (ActionLists) |
| 0x4CC740 | 48 | R | not relevant: memory or list helper | AI list copy of ten pairs |
| 0x4CC770 | 160 | S | implemented, unmarked | AI list cache slot of a critter (ActionLists) |
| 0x4CCAD0 | 64 | S | implemented, unmarked | AI list size cap, first list (ActionLists) |
| 0x4CCB10 | 64 | S | implemented, unmarked | AI list size cap, second list (ActionLists) |
| 0x4CCB50 | 64 | S | implemented, unmarked | AI list size cap, third list (ActionLists) |
| 0x4CCB90 | 96 | S | implemented, unmarked | AI list cache slot release (ActionLists) |
| 0x4CCBF0 | 16 | R | not relevant: module lifecycle | empty callback |

### Eye candy (0x4CCCF0–0x4CE300)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4CCCF0 | 16 | R | not relevant: module lifecycle | eye candy module flag set |
| 0x4CCD00 | 32 | R | not relevant: module lifecycle | eye candy module flag clear |
| 0x4CCD20 | 96 | R | implemented per documents, no address in code | eye candy run record initialisation (the eye candy runs of the executor) |
| 0x4CCD80 | 80 | R | implemented per documents, no address in code | eye candy art of an entry (the eye candy runs) |
| 0x4CCDD0 | 416 | S | not yet read (F0 row) | eye candy begin helper (416 bytes); not yet read as a whole |
| 0x4CCF70 | 2096 | S | implemented, unmarked | eye candy add (2096 bytes): the goal run of ExecutorEyeCandyGoals |
| 0x4CD7A0 | 416 | S | implemented, unmarked | eye candy place test of the first begin (ExecutorEyeCandyGoals) |
| 0x4CD940 | 128 | S | implemented, unmarked | eye candy remove (SessionSpellCaster) |
| 0x4CD9C0 | 368 | S | not yet read (F0 row) | eye candy remove helper (368 bytes); not yet read as a whole |
| 0x4CDB30 | 64 | R | not relevant: memory or list helper | animation effect record initialisation (zeroes a record with its defaults) |
| 0x4CDC30 | 32 | R | not relevant: table or constant accessor | index of an eye candy id in the ten-entry table |
| 0x4CDC50 | 32 | R | not relevant: table or constant accessor | eye candy id by index (a comment of ExecutorEyeCandyGoals names this address as a begin step; recheck) |
| 0x4CDC70 | 96 | S | not relevant: module lifecycle | eye candy module free |
| 0x4CDCD0 | 96 | S | implemented, unmarked | eye candy line exists test (SpellEyeCandy) |
| 0x4CDD30 | 192 | S | not yet read (F0 row) | eye candy list entry parse (192 bytes); not yet read as a whole |
| 0x4CE2A0 | 96 | S | implemented, unmarked | eye candy entry without art test (SpellEyeCandy) |
| 0x4CE300 | 80 | S | not yet read (F0 row) | eye candy index lookup (80 bytes); not yet read |

### Portraits (0x4CE350–0x4CEB80)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4CE390 | 48 | R | not relevant: module lifecycle | portrait module free |
| 0x4CE470 | 48 | R | not relevant: wrapper of another function | portrait draw at its own size, calls 0x4CE4A0 |
| 0x4CE640 | 64 | S | not relevant: wrapper of another function | portrait draw large image, calls 0x4CE4A0 |
| 0x4CE680 | 48 | R | not relevant: wrapper of another function | portrait draw at 32 by 32, calls 0x4CE4A0 |

### Script names (0x4CEC10–0x4CEE60)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4CECC0 | 32 | R | not relevant: module lifecycle | script name module reset |
| 0x4CEDA0 | 32 | R | not relevant: module lifecycle | script name module reset |
| 0x4CEDC0 | 160 | S | implemented per documents, no address in code | script file name of a number (the script name tables of Arcanum.Data) |
| 0x4CEE60 | 80 | S | implemented per documents, no address in code | script file name of a script id (the script name tables of Arcanum.Data) |

### Random generator (0x4CEEB0–0x4CEF50)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4CEEB0 | 16 | R | not relevant: module lifecycle | random module init: seeds from the clock |
| 0x4CEEC0 | 16 | R | not relevant: module lifecycle | empty module callback |
| 0x4CEED0 | 16 | R | implemented, unmarked | starts the random generator at a seed (OriginalRandom constructor, a constructor cannot carry a mark) |
| 0x4CEEE0 | 32 | R | implemented, unmarked | seeds the random generator from the clock (AreaRandomSeed, Arcanum.World) |

### Sectors (0x4CEF70–0x4CFFA0)

| Address | Size | B | State | Note |
| --- | ---: | --- | --- | --- |
| 0x4CEF70 | 352 | S | not relevant: module lifecycle | sector module init (352 bytes) |
| 0x4CF0D0 | 16 | R | not relevant: module lifecycle | sector module reset |
| 0x4CF0E0 | 480 | S | not relevant: module lifecycle | sector module exit: evicts all sectors and frees the tables |
| 0x4CF2C0 | 96 | S | not relevant: module lifecycle | sector module state init |
| 0x4CF340 | 80 | S | not relevant: module lifecycle | sector module state init |
| 0x4CF390 | 1024 | S | not yet read (F0 row) | sector load around the view rectangle (1024 bytes); not yet read as a whole |
| 0x4CF790 | 80 | S | not relevant: interface and viewport | reads two stored view positions |
| 0x4CF7E0 | 48 | R | not relevant: interface and viewport | reads two stored view positions |
| 0x4CF810 | 1088 | S | implemented per documents, no address in code | sector cache init and save (1088 bytes): the residency model (ARC-DEV-101..) |
| 0x4CFC50 | 64 | R | implemented per documents, no address in code | sector id from a location: coordinates shifted by six and packed (SectorCoord math, widely used) |
| 0x4CFC90 | 64 | R | implemented per documents, no address in code | location from a sector id: the inverse of 0x4CFC50 |
| 0x4CFFA0 | 240 | S | implemented per documents, no address in code | neighbour sector with the blocked-sector and file tests (the residency model) |

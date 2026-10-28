# Original verification ledger: Scripts and events

Part of the [original verification ledger](../original-verification-ledger.md) (ARC-RES-048): its [inputs and method](../original-verification-ledger.md#inputs-and-method), [verdicts](../original-verification-ledger.md#verdicts-and-what-they-mean-for-the-two-settings), [decisions to revisit](../original-verification-ledger.md#decisions-to-revisit) and [second reading](../original-verification-ledger.md#second-reading) cover every section below. A new section of this area goes at the end of this file and adds one line to the ledger's [area index](../original-verification-ledger.md#areas).

## Scripts, event queue and randomness (ARC-RES-042, ARC-RES-045, ARC-SCRIPT-007, ARC-RES-046, ARC-WORLD-019)

Source: [dispatcher](../scr-event-dispatcher.md#11-anomalies-recorded-not-adopted-s),
[census](../scr-census.md#anomalies-for-s1-s), [admission](../../scripting/scr-program-admission.md),
[event queue](../game-event-queue.md#7-anomalies-recorded-not-adopted),
[session randomness](../../world/session-random-runtime.md#what-the-original-does).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| X1 | `script_execute_action`, dialog case (0x447282) | Flags go to flags, counters to counters. | Reconstruction slip |
| X2 | `script_execute` (0x4449B0) | With no attachee the type comes from an uninitialised slot. | Original bug |
| X3, sector | `objlist_notify_sector_changed` (0x4F2330) | Number, flags and counters are compared and the changed copy is stored: a sector script's self-removal persists. | Reconstruction slip |
| X3, tile | `tile_script_exec` (0x4C0980) | The **unmodified** record is stored back: nothing a tile script changes about itself persists, including "remove this script". | Original bug |
| X4 | `sub_44B030` | Call-script runs one local copy and writes nothing back. | Original |
| X5 | `script_execute`, `script_flags` | The two missing-file exits are as the dispatcher study describes. | Original |
| X6 | `script_execute`, `sub_44C1B0` | −1 next, −2 skip default, −3 and −4 run default; −2 and −3 clear an auto-removing script, −4 does not. Other negative values fail the bounds check (skip default). So the admission refusal `NegativeGoto` is the project's own strictness, not the original's: in the original a literal goto of −1..−4 is a control code. | Original (reconstruction slip in the −4 removal detail) |
| X7 | `script_execute_action`, divide (0x4474CE) | Stores the signed quotient; nothing for divisor 0. | Reconstruction slip |
| X8 | `script_set_value` (0x44BB50) | Counter writes are masked to 8 bits. | Reconstruction slip |
| X9 | `script_execute_action`, action 86 (0x4494DF) | Reads slot 1. (The headline punctuation test reads the last character; the earlier reading read the terminator and always appended a period.) | Original |
| X10 | `script_global_flag_set` (0x444D20) | Global-flag writes are not masked: 2 sets the next flag, a negative value every higher flag of the word. | Original bug |
| TE1 | call-in cases, `sub_454790`, `datetime_add_milliseconds` (0x45AA10) | The 32-bit `seconds × 1000`, `× 8` delay wraps above 536,870 s (see [second reading](../original-verification-ledger.md#second-reading)). | Original bug (edges only) |
| TE2 | call-at case, `sub_45AD70` | Counts from the start hour. | Original |
| TE3 (OQ1) | `timeevent_add_base_at_func` (0x45B8C0), `sub_45B750` | Ties newest first; events merged after a ping go in front of older equals. | Original |
| TE4–TE7, TE9 | `timeevent_clear_one_ex`, `timeevent_ping` (0x45B370), `timeevent_recover_handles_internal` | Two removals possible, shared loop guard, merge drops stale handles without recovery, handler results ignored, null objects passed on. | Original |
| TE10 (ARC-WORLD-024) | `stat_poison_timeevent_process` (0x4B1170), `stat_base_set` (0x4B0980), `poison_timeevent_schedule` (0x4B1350) | The recovery case writes the lowered level through `stat_base_set`, which for a positive level calls `poison_timeevent_schedule` with recovery on; that adds a recovery event when `timeevent_any` finds none (the firing one is already off the list). The handler then adds another recovery event itself (120,000 ms) while the level stays positive. From the second tick on two recovery events run per period. Read twice (the second reader, for the ARC-WORLD-024 review, decoded the post-write switch tables of `stat_base_set` and the list unlink in `timeevent_ping` 0x45B370 before the handler call). | Original bug |
| TE11 (ARC-WORLD-024 finding, recorded by ARC-DEV-031) | `stat_poison_timeevent_process` (0x4B1170) | Not given a verdict. ARC-WORLD-024 reported, as a side finding without a verdict, that in a single-player game the handler seems to apply the poison damage and recovery ticks to a critter whose object is switched off (OFF flag) as well, the off-object skip looking tied to the network game; that branch has not been read to a verdict. Steamwoven does the same today: the session's poison timers carry no off flag, and the AREA viewer's event pass (ARC-WORLD-039) runs an NPC's timers whenever a covered map holds the NPC's row (its current map, or its home map for a follower the session moved). To be read at 0x4B1170 by the task that next changes the poison timers or gives event targets an off rule (owner decision 2026-10-20, ARC-DEV-030). | Not checked (F0) |
| OQ2 | type table 0x5B2188, `timeevent_save`, `ai_timeevent_process` (0x4AD200) | The AI tick kind is not saved; a load schedules a new tick per NPC with 0..5000 ms jitter. | Original |
| OQ3 | `timeevent_save_nodes_to_map` and neighbours | Per-map `TimeEvent.dat` files; events follow their objects across a teleport. | Original |
| Random (R1–R6) | `random_rand` (0x4CEF50), `random_between` (0x4CEF00), `random_seed_generate` | LCG 1,664,525 / 1,013,904,223, output bits 8..22; modulo ranges, swapped bounds; time seed; no save or load; spawn-inventory and dialog reseeds. All as the contract says. | Original |
| SN1 (ARC-SCRIPT-011) | `script_name_init` (0x4CEC10), `script_name_mod_load` (0x4CECE0), `script_name_build_scr_name` (0x4CEDC0), `script_name_build_dlg_name` (0x4CEE60) | The `scr\*.scr` listing maps `atoi(name)` ≥ 1000 at game start and 1..999 at module load, a later name replaces an earlier one with an error message; lookup takes ≥ 1000 from the first table and 1..999 from the second; the DLG name swaps the first and last three characters for `dlg`. | Original |
| DL1 (ARC-SCRIPT-012) | `dialog_load` (0x412E10), `dialog_parse_field` (0x417C20), `dialog_parse_entry` (0x417860) | As the DLG original read mode: a field is found by skipping to the next `{` (`//` comments skipped), capped at 999 characters; id, IQ and response read with `atoi`; a non-empty IQ that reads 0 and a response starting with `#` end the read; a file with no record is not opened. | Original |
| DF1 (ARC-DATA-022) | `dialog_load` (0x412E10) over four stone-examination DLGs | Data defect: the only record lacks its effects field, no record is read, the file is not opened and the print line shows nothing. | Original bug (data) |
| DF2 (ARC-DATA-022) | `dialog_parse_entry` (0x417860), NPC line 315 of `01306benjamin_gershwin.dlg` | Data defect: the line's effects are in its response field, which an NPC line never uses; they are never applied. | Original bug (data) |
| DF3 (ARC-DATA-022) | `dialog_parse_entry` (0x417860), reply 361 of `01009mayor_blackroot.dlg` | Data defect: the final-line action is in the response field; `atoi` reads 0 and the reply ends the talk. | Original bug (data) |

## Guard conditions (ARC-SCRIPT-014)

Source: the [dispatcher's teleports and guards](../../scripting/scr-dispatcher-runtime.md#teleports-and-guards-arc-script-014-set-s4)
and the guard kinds of [map transitions](../world-transitions.md#conditions). Question: what the condition function does for
the five guards that stand most often between a placed object or tile and a teleport. Read on 2026-10-20 by a first reader
and, independently, a second reader that saw only the questions and the addresses (method step 6); they agree on every row.
The second reader added the object-type branches inside the helpers (SG1, SG2, SG5). The teleport actions themselves are
rows TR1, TR2 and TR4. Every call target stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| SG1 | script condition (0x4451B0, jump table 0x44714C, entry 6), quest state (0x4C4CB0) | Operand 0 is a focus list, operands 1 and 2 values: an object counts when the quest state of operand 1 for it equals operand 2. The state is the PC's own entry for a PC (6 when the entry's 0x100 bit marks it botched) and the quest's global state (0x4C51A0) for any other object. | Original |
| SG2 | the same, entry 17, item search by name (0x462480) | An object counts when it holds an item whose name field equals the value: the direct inventory of a container or a critter, the first match; any other object holds nothing. | Original |
| SG3 | the same, entry 20 | An object counts when its spell flags (field 20) lack the mind-controlled bit (0x08000000) and its name field (22) equals the value. A mind-controlled object is not counted but stays in the list. | Original |
| SG4 | the same, entry 21, wielded item (0x4649C0) | An object counts when an item it wields in inventory location 1000..1008 has a name field equal to the value; the first such location ends the walk. | Original |
| SG5 | the same, entry 33, reputation (0x4C1CB0) | An object counts when it is a PC with the reputation in its reputation array; any other object has none. | Original |
| SG6 | the same, after each of the five | The focus list is released, then the true branch runs when at least one object counted and either all did or the focus kind is one of 3, 5, 7, 9, 14, 16, 18, 20 or 23 (one is enough); otherwise, and always for an empty list, the false branch. | Original. The dispatcher admits single-object focus only, where "all" is the one object; an absent object stops the run there (project rule of the single-object conditions) instead of taking the false branch. |

## Tile and sector scripts (ARC-SCRIPT-017)

Source: the [tile and sector scripts of map mode](../../rendering/area-map-mode.md#tile-and-sector-scripts-arc-script-017) and
rows TR3 and TR5. Question: which node a tile's script is, when a tile script and a sector script run on a move, and in
which order. Read on 2026-10-21 by a first reader and, independently, a second reader that saw only the questions and the
addresses (method step 6); they agree on every row. The second reader added the callers' modes (TS2) and the sector
lookup (TS3).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| TS1 | tile script (0x4C0980), tile lookup (0x4C07B0), list search (0x4F6570), list insert (0x4F6470) | A tile's script is the node of the sector's tile-script list whose key is the tile index in the sector: the list is kept sorted with unique keys (an insert with an equal key overwrites), so the first match is the only one, and no node flag is tested. The node's header flags, counters and script number are run from line 0 as a use, with the moving object as the triggerer and no attachee. | Original |
| TS2 | object location change (0x444270), move (0x43E770) | After a move: for a PC (type 15) the jump point of the new tile (0x4101D0); then for a PC or NPC (type 15 or 16) the tile script; then, only when the move enters another sector (the move passes mode 2 for a new sector, 1 for the same one) and only for the local player, the sector notification (0x4F2330). Placing an object on the map (0x4416E5, 0x4423A5) passes mode 2 as well; which placement a teleport arrival uses is not read here. | Original |
| TS3 | sector notification (0x4F2330), sector script copy (0x4C0440), store (0x4C0490) | The sector's music scheme is switched first; then the sector script runs when its number is not 0, from line 0 as a use with the player as the triggerer and no attachee, and a changed copy is stored back (X3 sector). | Original |

## Object variables and calls (ARC-SCRIPT-018)

Source: the [dispatcher's object variables and calls](../../scripting/scr-dispatcher-runtime.md#object-variables-and-calls-arc-script-018-set-s12).
Question: what call script (7), assign object (15) and get object type (34) do, and what an object variable (focus kind
11) names. Read on 2026-10-21 by a first reader and, independently, a second reader that saw only the questions and the
addresses (method step 6); they agree on every row. The second reader added the group behaviour of call script, the
writable number kinds and the undefined results for an empty group or a null object.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| OV1 | script action (0x447220, jump table 0x44ADBC, entry 6), call script (0x44B030), script run (0x4449B0) | Operand 0 is the script number, operand 1 the start line, operands 2 and 3 the triggerer and attachee groups; the script runs once for every triggerer and attachee pair, with no extra object, the caller's attachment point, and the callee's own header flags and counters (read from its file) in one copy shared by the pairs; the callee has fresh variables, nothing it changes about itself is written back, its return code is ignored and the caller goes on. No depth bound but the stack; each run keeps the 1,000-line budget. | Original. The project bounds the nesting at 16 and a dispatch at 16 × 1,000 lines in both modes (owner decision 2026-10-21, [rule modes](../../world/rule-modes.md)). |
| OV2 | the same, entry 14 (0x44750A), object read (0x44B960), object write (0x44B9B0) | The first object operand 1 names is written into operand 0: the run's triggerer, attachee or extra object, an object variable (ten, zeroed at the start of each run), or the current looped object; any other focus kind is not written. For an empty group the value read is undefined. | Original. The project reads an absent single object as none. |
| OV3 | the same, entry 33 (0x447C71), field read (0x406CA0), number write (0x44BB50) | The type field of the first object operand 0 names is written into the number store of operand 1 (a counter, global or local variable, global flag, PC variable or PC flag; a literal is not written). A null object reads undefined data. | Original. The project stops the run on an absent object, and admission refuses a literal store (project strictness). |
| OV4 | object list (0x44B4E0, entry 11) | An object variable is a one-object list of whatever it holds, a null object when it was never assigned. | Original. The project reads an unassigned variable as no object. |

## Story state and story actions (ARC-SCRIPT-019)

Source: the [dispatcher's story actions](../../scripting/scr-dispatcher-runtime.md#story-state-and-story-actions-arc-script-019-set-s13).
Question: where the story-state number lives and how actions 42 and 43 use it, and what set PC quest state (16), mark map
location (49), set rumor (50) and add or remove reputation (72, 73) pass to the original's functions. Read on 2026-10-21 by a
first reader (the action cases) and, independently, a second reader that saw only the questions and the addresses (method
step 6); they agree on every row. The second reader added the callees, read before by their own tasks (XP8, WT2, RP4, RM1).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| SS1 | script action (0x447220, jump table 0x44ADBC, entries 41, 42), story state get (0x445090), set (0x4450A0), script state init, reset and save (0x444740, 0x4447D0, 0x4448D0, 0x444930) | One game-wide number: 0 at a new game, saved with the script state; get story state (42) writes it into a number store, set story state (43) stores its value only when it is larger (a signed compare). In a network game a client changes nothing and a host sends a message first. | Original |
| SS2 | the same, entry 15 (0x447543), quest state set (0x4C4D20) | For every object operand 0 names: the quest (operand 1) and the state (operand 2), with the run's attachee as the quest's giver. The function changes a PC's quest only, by its own rules (XP8: it only rises, an ended quest stays, the shared state); for any other object nothing is stored. The giver's reaction to the PC rises to 41 at state 2 and by 10 at state 4. | Original |
| SS3 | the same, entry 48 (0x448456), area known (0x4CAFD0) | The area (operand 0) for the first object operand 1 names: known for the local PC only (WT2); no range check. | Original |
| SS4 | the same, entry 49 (0x448497), rumor learn (0x4C57E0) | The rumor (operand 0) for the first object operand 1 names: a PC that does not know it stores the current game time, a known one keeps its first time; quelled rumors are a separate set it never reads; no range check. | Original |
| SS5 | the same, entries 71, 72 (0x448E85, 0x448EF6), reputation add and remove (0x4C1D20, 0x4C1E10) | For every object operand 0 names, the reputation (operand 1) is added or removed (RP4, RM1: a PC only). | Original |

## Item insert script (ARC-RES-055)

Source: [item insert script](../item-insert-script.md). Question: what the `print_line` action (type 24) of a script does,
for script 1341 on the Crash Site amulet's insert-item and examine points. Read on 2026-10-18 by a first reader and,
independently, a second reader that saw only the question and the addresses (method step 6). Both agree on IS1 and on
the path of IS2. The second reader added that the NPC-line step runs the printed line's action field, the generated-text
prefixes and IS3; the first reader re-read them (the release skip of IS3 directly, the action field through the step's
dialog records) and agrees. IS4 comes from the reviewing reader; the first reader re-counted the six call sites. Every
call target stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| IS1 | `script_execute_action` (0x447220), jump table 0x44ADBC | The table is indexed by action type − 1 (entry 43 is the teleport case, map transitions TR1); entry 23, type 24 `print_line`, is the case at 0x447822, which only calls 0x44B390 with the action and the run state. | Original |
| IS2 | print line (0x44B390), NPC-line step 0x413A30 → 0x416840 | Loads the DLG named after the running attachment's script through the dialog cache (0x4CEE60, `dialog_load` 0x412E10) and does nothing without a script number, on a failed load or with a null attachee; operand 0 (`script_get_value` 0x44BA70) selects the line (a missing line prints a single space); the NPC-line step runs the line's action field (DLG action interpreter 0x415BA0, triggerer and attachee) and builds the text (`g:`, `m:`, `r:` prefixes or a whole `i:` generated; else text or alternate text by the triggerer's gender); operand 1 travels with the text to the interface's message callback (0x460630, slot 0x5E86D8 of the table installed at 0x45FD40), or on a network game (0x62AC6C bit 0) by the triggerer's player slot (0x4A2B10: local → the callback, another → a packet 0x4EDA60, none → nothing); the cache reference is released (0x412F40). The function writes no global or object itself. | Original |
| IS3 | print line (0x44B390) | The null-attachee test comes after the DLG load and returns without the release, leaving one cache reference behind. | Original bug (latent: never reached with an item as attachee) |
| IS4 | global-flag write (0x444D20) | Six callers: DLG action interpreter 0x415EF6, script set and clear 0x449A4A and 0x449A72, script value store 0x44BC2F, network relay 0x4EFD19, pre-made hero menu 0x545C1A (Pre-made flags row). None writes a fixed flag of its own. | Original |

## Item events (ARC-SCRIPT-041)

Source: [item events](../../world/inventory-runtime.md#item-events-arc-script-041) and the event catalogue of the
[dispatcher research](../scr-event-dispatcher.md#2-event-catalogue-s). Question: which item events the original's transfers,
removal, insertion and wield commands run, in what order, with which roles, and which results they use? Read on 2026-10-28 by
this task and, independently, by a fresh agent that saw only the questions and the addresses (method step 6). They agree on
every row. The second reader corrected the first reading of IE6: the wield command's arguments and the order and rollback of the
occupied slot. The first reader re-read those rows and agrees. The rows are F2. Roles are given as triggerer / attachee / extra.
The calls go to the object script entry (0x441980).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| IE1 | ground transfer (0x461810) | An item with a holder goes to the parented transfer (IE2). Otherwise the first step runs point 4 (get): critter / item / none. Skip default returns failure before any check. Next comes the insert check (0x466510); its refusal returns failure, and the get script's effects stay. Then the item leaves the map (0x4417A0), the insertion (IE4) follows and the decay event is cancelled (0x468110). There is no no-drop check on this path. | Original; Steamwoven drops the get script's writes when the insert check then refuses (session policy, no installed script) |
| IE2 | parented transfer (0x461CA0) | The first step runs point 32 (transfer) on the recipient: holder / recipient / item. Skip default returns failure before any check. Then, in order: the no-drop check (0x466DA0; a refusal messages a PC at either end), the insert check (0x466510), the removal (IE3) and the insertion (IE4); then the decay event is cancelled and scheduled again for 48 hours when the item is eligible (0x468110, 0x468090). | Original; the decay re-scheduling is outside this task |
| IE3 | removal (0x466E00 → 0x467860) | Every removal from an inventory takes this path, not only a forced one. Without a holder it logs an error and does nothing. Otherwise it unlinks the item from the holder's list and clears its location. An item worn in slots 1000–1008 is unwielded (IE5), and hot-key slots 2000–2009 are released. Only then does it run point 28 (remove item): holder / item / none. The result is not used. | Original |
| IE4 | insertion (0x466640) | The item is appended to the list with its location and holder. A worn slot 1000–1008 is wielded (IE5). Then point 21 (insert item) runs: holder / item / none, and its result is not used. A stackable item that merges into a held stack adds its quantity, runs point 21 the same way and is then destroyed (0x43CCA0, which runs its point 2); a merge wields nothing. | Original |
| IE5 | wield (0x4677B0), unwield (0x467CB0) | After the slot's own work and the light recalculation (0x467E80; the unwield first removes the item's spells, 0x4CBFC0), point 24 (wield on) or point 25 (wield off) runs: wielder / item / none. No caller uses the result. | Original |
| IE6 | wield command (0x464AE0), take off (0x464C50 → 0x464C80) | The arguments are the item and the slot; the wielder is the item's holder. If the occupant is the item, it returns success. Otherwise come the wear check (0x464D20) and the item's no-drop check (0x466DA0). With an empty slot it removes the item (IE3) and inserts it at the slot (IE4), with no insert check. With an occupant: the occupant's no-drop check, then removal of the item and then of the occupant, then the occupant's insert check. When that check accepts, the occupant is inserted where the check placed it and then the item at the slot. When it refuses, both are inserted back where they were (the item at its old location, the occupant at the slot) and failure is returned; the scripts of both moves have run. Take off: a worn item, its no-drop check, its insert check (a refusal leaves it worn), then removal and insertion. | Original; Steamwoven refuses before any move (ARC-WORLD-028) and runs no wield event yet |
| IE7 | rewield (0x465530, one caller 0x4B0D4F) | For slots 1000–1008 in order, each worn item: the global 0x5E87E8 is set to 1, then the light recalculation, point 25, then point 24 (wielder / item / none, results unused), then the global is cleared. Its getter (0x4655C0) has one caller, case 50 of the condition switch (0x4451B0), the condition "items are being rewielded". The drop (0x461AB0) runs point 5: holder / item / none. | Original (F2 for the rewield; the drop roles F1, first reader and second reader in passing) |

## Item use script (ARC-SCRIPT-020)

Source: the [reading window runtime](../../world/reading-window-runtime.md) and WR13. Question: what runs between a click on a written
item and its reading, and what decides whether the reading goes on? Read on 2026-10-21 by this task and, independently, by a fresh
agent that saw only the questions (method step 6). Both agree on every row; the rows are F2 unless marked.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| IU1 | item use (0x462CC0), before the script | A null item returns; a multiplayer client forwards the use to its host. A scroll whose user lacks the required stat stops with a message (a PC only); an item with item flag 0x100 and no target starts target picking instead; a generic device with generic flag 0x4 takes its own handler. None of these applies to a written item: no installed written prototype has flag 0x100 (`WrittenRealDataTests`). | Original (the target-picking flag's name is inferred) |
| IU2 | item use (0x462CC0) → script call (0x441980) | The use calls the script runner with the user as the triggerer, the item as the attachee, the target as the extra object, attachment point 1 and start line 0. | Original |
| IU3 | script call (0x441980) | In the editor it returns 1 without running. At attachment point 1 a trap the user does not yet know and spots now (Spot Trap roll or a player flag) is revealed and the call returns 0. Script 0 returns 1; otherwise the run's answer: return-run-default 1, return-skip-default, falling off the last line, an unreadable line or the line cap 0. A run-once script clears its own number after the run. | Original (the trap check not modelled: no written item has a trap script) |
| IU4 | item use (0x462CC0), after the script | An answer of 0 charges 4 action points in a turn-based round (0x4B7CD0) and ends the use. Otherwise an item with a spell mana store or the magical item flag casts its spells (food and scrolls are then destroyed), and any other charges 4 points with the result ignored and goes on by type; a written item (type 13) starts the reading (0x4606F0 → WR2). | Original |
| IU5 | item use (0x462CC0) | Nothing between the script and the reading checks the item again: the reading uses the same handle, and a written item is never destroyed on this path. | Original |

The runtime runs the use script in the viewer's use script host (the PC as the user, no target, the item's inventory row; the
host's admission set) and reads only on a run-default answer; a run the host refuses is not read (PROJECT: the original has no
refusal). Not modelled: the action points (the viewer reads outside rounds), the trap check, the item-spell path (no written item
takes it), a teleport from an item's use script.

## Template sector scripts (ARC-RES-073)

Source: the [plant generator study](../template-sector-plants.md). Question: what the sector script of the terrain
templates (30285, the plant generator) does when it runs, which objects it creates and where, and whether a template
sector's script runs like an own sector's. Read on 2026-10-27 by this task and, independently, by a fresh agent that
saw only the questions and the addresses (method step 6); the rows are F2 unless marked. The readers agree on every
row both read (PG9 was read by the first reader only); the second reader corrected the critter case of PG2 and added the network paths of PG1 and the span limit of PG5.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| PG1 | script action (0x447220, jump table 0x44ADBC, entry 51: create object, 0x448537), object create (0x4ED8B0) | Operand 0 is the prototype number, operand 1 the object to create it next to (the first one it names). Up to ten times the nearby-tile search (PG2) runs with a random distance 1..3; after ten failures the object's own location is used. The single-player path turns the number into a prototype (PG8) and creates an object there (0x43CBA0); a network host also sends the creation to the clients, and a client creates nothing. The action ignores the create result; a created NPC (type 16) gets bit 0x20000 in field 0xDA, and nothing else follows. | Original |
| PG2 | nearby tile (0x4F4E40), direction step (0x4B8FF0), path (0x41F3C0) | A random direction 0..8 and the distance in steps from the object's location; direction 8 is no direction (the step keeps the tile), and a step off the map fails the search. A path search from the object to that tile (at most 200 nodes) then decides: no path, or a path longer than the distance + 1, gives the object's own tile; otherwise the tile reached by walking the path. When the object itself is a critter, the walk returns the first tile on the way that holds no PC or NPC (the end tile is not checked). | Original |
| PG3 | object list (0x44B4E0, focus kind 15, every scenery in vicinity), vicinity (0x440FC0, 0x440FF0) | The centre is the run's attachee; with none the list is empty. The area is the game window's rectangle moved so that its centre is on the attachee's location, turned into a location rectangle; the type mask holds scenery only. At most the first 100 objects of the list are copied. | Original (the cap drops objects silently) |
| PG4 | script condition (0x4451B0, jump table 0x44714C, entry 3: local flag, 0x445307) | Tests bit (value & 31) of the running attachment's flags word. | Original |
| PG5 | script action, entry 36 (random, 0x447DCA), random between (0x4CEF00), generator (0x4CEF50) | Operand 0 and operand 1 are the bounds (either order, both included), operand 2 receives the number, the low bound plus a draw modulo the span; the generator is the game's shared linear congruential one (15 bits a draw), so a span above 32,768 is not covered. | Original |
| PG6 | value read (0x44BA70), kind 0 (counter) | The counter word is shifted right by eight times the index and masked to a byte; the shift count keeps five bits, so an index above 3 reads counter (index mod 4): the plant generator's index 2,012,780,696 reads counter 0. | Original (no range check) |
| PG7 | script action, entries 17 and 18 (loop for 0x44762B, loop end 0x4476C2) | Loop for fills the run's list (PG3, at most 100) once, remembers the line after it and makes the first object the current looped object; an empty list skips to after its loop end. Loop end steps to the next object and jumps back, or clears the loop after the last. A loop for inside a running loop logs an error and does nothing. | Original |
| PG8 | prototype from number (0x4685A0, 0x468860) | A script's prototype number gets 20 added before the lookup (the basic number, as the prototype file numbering). | Original |
| PG9 | sector notification (0x4F2330) | Locks the entered sector and runs its script record copied from the sector (0x4C0440) without asking where the sector came from: a template sector runs the script section read from its template (SS5) like an own sector file. | Original (F1: one reader) |
| PG10 | script condition, entries 4 and 5 (equal 0x445355, less or equal 0x4453A7) | Both read their operands with PG6's reader and compare them as signed 32-bit numbers. | Original |

## Wield events (ARC-SCRIPT-042)

Source: [wield events](../../world/equipment-runtime.md#wield-events-arc-script-042), the
[dispatcher's set S14](../../scripting/scr-dispatcher-runtime.md#item-effects-arc-script-042-set-s14) and the census of the installed wield
scripts ([item points](../scr-census.md)). Question: what do the commands the installed wield-on and wield-off scripts use do, which
of their results go where, and how does the wield command order its item events? Read on 2026-10-28 by this task and, independently,
by a fresh agent that saw only the questions and the addresses (method step 6). They agree on every row; the rows are F2. Roles are
given as triggerer / attachee / extra.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| WE1 | script action (0x447220) cases 77 and 78 | Add effect: the object list of operand 0, then the effect number (operand 1) and the cause (operand 2); for each object in list order the effect add (0x4E9F70) with the effect and the cause. Remove effect: the object list, then the number; for each object the typed removal (0x4EA100, the first entry with the number whatever its cause). No result is used; the run goes on. The cause numbers: race 0 and gender 9 (the race and gender cases of the stat setter 0x4B0980 pass them with effects 64 + race and 330), bless 3 and curse 4 (0x4C42F0, 0x4C3E40); every installed item script passes 5. | Original (the item cause 5 by the order of the four read causes; the session names it Item) |
| WE2 | script action case 91 (0x4496F8), power ratio (0x461540) | The value of operand 0, the single objects of operands 1 (the item) and 2 (its owner), the ratio 0x461540(item, owner) (EQ9: 100 for a neutral item or an owner that is not a PC or an NPC, else `100 × power / complexity`; the same for magickal and technological items, the adjustment 0x461590 is not called), then `(ratio × value + 50) / 100` into operand 3 by the value store (0x44BB50; a counter keeps the low byte). The product and the sum keep 32 bits and wrap; the division truncates toward zero, so a negative product rounds unevenly (−150 gives −1, −149 gives 0). | Original |
| WE3 | script action case 81 → 0x44B170 → 0x441980 | Call-ex runs an object's attachment as an event: the object of operand 0 is the attachee, the value of operand 1 the attachment point, the value of operand 2 the start line, the object of operand 3 the triggerer, no extra object. The result is not used and the run goes on. The object script entry copies the attachee's slot of the point (flags, counters, script), runs from the copy and writes the copy back only when one of the three words changed (0x441A92–0x441AC0); a trap object whose script became 0 is destroyed instead. It returns early, running nothing, in the editor, for a point-1 trap the triggerer spots and for script 0. | Original |
| WE4 | script action case 120 (0x44A410), 0x44C7A0 | Play script eye candy: the number of operand 0 and the single object of operand 1; 0x44C7A0(object, number) plays entry *number* of the script eye-candy list (`Rules\ScriptEyeCandy.mes`, loaded at 0x4447A0) on the object. Presentation only; the run goes on. | Original |
| WE5 | script condition (0x4451B0) case 47 | The object list of operand 0; for each object the identified test (0x464370: a null handle or a non-item is not identified, an item with complexity 0 or less is, else item flag 0x1). None identified (an empty list included) is false; all of them true; some only for the "any" focus kinds 3, 5, 7, 9, 14, 16, 18, 20 and 23, as the other list conditions (SG6). | Original |
| WE6 | script condition case 50, 0x4655C0, rewield (0x465530), stat setter (0x4B0980) | True while the global 0x5E87E8 is set. Only the item module's start (0x460E70, to 0) and the rewield write it: for each worn location 1000–1008 that holds an item, the global is set, the light is recalculated (0x467E80), point 25 and then point 24 run (critter / item / none, line 0) and the global is cleared. The rewield's only caller is the base stat setter, after the new value is stored, for stats 22 and 23 (magick points and tech points). | Original; the session has no rewield yet (the condition answers false) |
| WE7 | wield command (0x464AE0), take off (0x464C80) | As IE6, read again for this task: the item at its own location returns success at once; the wear check (0x464D20) and the item's no-drop check (0x466DA0) refuse before anything moves. An empty location: the item's removal (0x466E00) and its insertion at the location (0x466640), no insert check; at the medallion location (1003) 0x436FA0 (EQ5). An occupant: its no-drop check, the item's old location, the removals of the item and then of the occupant, then the occupant's insert check (0x466510; 0 accepts with the location it found, non-zero refuses: 1 too heavy, 2 no room, 15 Technophobia). Accepted: the occupant is inserted where the check placed it, then the item at the location. Refused: the item is inserted back at its old location first, then the occupant at the location, and the command fails. Take off: a location outside 1000–1008 succeeds with nothing done; the no-drop check, then the insert check with the item still worn (a refusal leaves it worn), then the removal and the insertion. | Original; the session runs the removals' and insertions' item events in this order (IE3, IE4) |

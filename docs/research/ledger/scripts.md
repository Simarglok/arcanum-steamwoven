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
| X11 (ARC-SCRIPT-046) | `script_execute_action`, dialog case (0x447282); call script (0x44B030) | The dialog action reads its node, and only with a dialog callback set and a PC triggerer (0x40DA20): reads the attachee's script attachment at the run's point (0x407840, field 0x20), stores the running script's local flags and counters into it when either differs (0x4078A0; the attachment's number kept), calls the dialog callback with the triggerer, the attachee, the running script's number and the node, and returns −2 either way. Call script builds a new attachment from the callee's file header with the caller's attachment point copied and runs it (0x4449B0) for each triggerer and attachee pair, ignoring its result: a callee's dialog starts its conversation with the callee's DLG and its attachee, and the caller goes on. One reader (ARC-SCRIPT-046), not in the second reading. | Original |
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
| PG11 | loop end search (0x44BC60), called by loop for (0x44762B) with an empty list | From the line after the loop for, reads each line until one whose condition is "true" (0) and whose true action is loop end (19), and returns the line after it; when a line cannot be read (past the last) it returns the line after that one, so the run then stops on a line outside the script. A loop end in a false action or behind any other condition is passed over. Read by ARC-WORLD-081 and, independently, a fresh reader (F2); the current looped object is left as it was. | Original |
| PG12 | script action, entry 51 (create object, 0x448537..0x4485D3) | Each of the up to ten tries draws a fresh distance (random between 1 and 3, 0x4CEF00) before it calls the placement 0x4F4E40, which draws the direction; the loop ends at the first try the placement accepts, and only after ten failures the object's own location is read (0x406DA0, field 2). The new object's handle is kept nowhere. Read by ARC-WORLD-081 and, independently, a fresh reader (F2). | Original |
| PG13 | vicinity (0x440FF0) | The rectangle is the game window's (0x51CEB0 on the handle 0x5E2F24, which the view callback 0x43A330/0x43A570 stores from its argument); the window is the 800 × 400 play view (OL29), so the box reaches 10 tiles on both axes and never depends on a resolution or zoom. ARC-WORLD-081, F1 (it repeats OL29's reading of the same rectangle). | Original |

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

## Unchosen pre-made heroes (ARC-WORLD-114)

Source: [character creation evidence](../character-creation-evidence.md#unchosen-pre-made-heroes-arc-world-114) and the
[heartbeat scripts in the AI host](../../world/ai-host.md#heartbeat-scripts-arc-world-114). Question: who reads the pre-made hero
flags 2350..2361 that the main menu writes (ledger row Pre-made flags), and what do the two actions their readers use do? Read on
2026-10-29 by this task with `llvm-objdump`; PM2 and PM3 agree with SU6 ([NPC AI script surface](npc-ai.md), ARC-RES-070), read
independently earlier, so those two rows have two readers (F2); PM1 is F1.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| PM1 | whole `.text` (immediates 0x92E..0x939), main menu pre-made step (0x545B60) | No instruction names a number 2350..2361 as an immediate (the one match, 0x40C08E, is a structure offset 0x938). The flags reach the executable only as `MainMenu.mes` 551 + i, which the pre-made step turns into flag numbers and writes; nothing in the executable reads them. Their readers are the installed scripts (character creation evidence, CC-11). | Original |
| PM2 | script action (0x447220) case 56 (0x4487CE) | Toggle state: the object list of operand 0 (0x44B4E0); for each object in list order its object flags are read (0x406CA0, field id 0x13) and bit 0x2 (off) is cleared through 0x4EFEE0 when set, else set through 0x4EFF50: the raw field bit writes, not the object flag setter (0x43D0E0), so the object's lights keep their OFF bit (LT13 does not apply). No result is used; the run goes on. | Original |
| PM3 | script action case 58 (0x4488E8), kill (0x45D900) | Kill: the same object list; for each object the kill with the object alone, no killer (the damage step with a null attacker, then the pinned death damage; LifeRules.Kill). The run goes on. | Original |

## Script triggers (ARC-SCRIPT-047)

Source: the [script trigger audit](../script-trigger-audit.md) and the [heartbeat scripts in the AI host](../../world/ai-host.md#heartbeat-scripts-arc-world-114).
Question: where the original runs an object's script attachment points, and why Virgil's talk opens by itself at the start of a new
game. Read on 2026-10-29 by this task with `llvm-objdump` and, independently, by a fresh agent that saw only the questions and the
addresses (method step 6); the readers agree on every row both read, so those rows are F2. TG6 is a reading of the installed scripts
(F1) and TG8 was read by this task only (F1). The second reader added the first-tick condition of TG3 and the inventory skip of TG4.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| TG1 | script point runner (0x441980), its 72 direct call sites | Arguments: triggerer, attachee and extra object (64-bit handles), the point, the start line. Every call site pushes the point and the line as immediates except call-ex (0x44B1D6, point and line from the action's operands) and the armour hit (0x4B47F8, point 34 or 35 in a register). Every point 0..35 has a call site except 31; the audit's attachment table lists them per point. | Original |
| TG2 | tile script (0x4C0980), object moved (0x444270), path walk (0x455350) | The tile script runs from two places only: the object-moved step 0x444270 and the path walk 0x455350 (called from 0x452630, each tile of a walked list). 0x444270 reaches it only for a PC or NPC (type 15 or 16) and only in modes 1 and 2; mode 0 sets the move flags and returns. Its callers: 0x43E770 (modes 2 and 1), 0x43EA20 (mode 2), 0x4415C0 (mode 2), 0x442260 (mode 2) and 0x4423E0 (mode 0, no tile script). So any move of a critter through those steps, the player's or an NPC's, runs the tile script of the tile it lands on. | Original |
| TG3 | AI tick (0x4AD200), near gate (0x4AD420) | The first heartbeat (point 10) runs on the first tick of a critter that is not dead, with triggerer = attachee = the critter at line 0. Unless it returned run default the heartbeat is not run. The near gate passes when the critter is alive, no turn-based round runs (0x4B6D70) and the local PC is within 30 tiles (0x441AE0); then the heartbeat (point 19) runs, again triggerer = attachee = the critter at line 0. Run default goes on to the AI pass (0x4A84F0, or the generator path for critter flag 0x80000); any other return reschedules the tick without the AI pass. | Original |
| TG4 | object list (0x44B4E0) focus kinds 8 and 9, vicinity (0x440FC0, 0x440FF0, 0x440B40) | Everyone and anyone in the vicinity share one handler: the list is built around the invocation's attachee with type mask 0x18000 (PCs and NPCs) in the play-view rectangle (10 tiles on both axes, PG13), at most 100 objects. No test leaves the centre out, so a critter is in its own list. The builder skips an object in an inventory (flag 0x1000) and one whose flags hit the editor mask (3 in play: destroyed or off); dead critters stay. | Original |
| TG5 | point dispatch gates (0x4449B0), call-ex (0x44B170) | For any point, an NPC attachee with critter flag 0x80000 runs nothing (run default). At the dialog point (9) of an NPC attachee: dead and the script's file flag 0x8 clear, or surrendered (0x4AF210) and file flag 0x10 clear, run default; mind-controlled (flag 0x8000000) runs script 2188 in place of the slot's script. Call-ex goes through the same runner, so a call-ex to point 9 meets the same gates and runs the callee's dialog-point script with the call-ex's triggerer; its dialog action starts the talk with that triggerer. | Original |
| TG6 | installed scripts 1100, 1324 (Virgil at the Crash Site) | 1100 is Virgil's heartbeat: for each object in the vicinity of everyone (focus 8) it tests the object's type; for the PC it calls Virgil's own dialog point at the dialog script's line 4 with the PC as triggerer, removes itself and returns skipping default. 1324's line 4 is the dialog action at node 1. So the talk opens on the first heartbeat after the PC comes within the vicinity, once. Eight other installed heartbeats call their NPC's dialog point the same way (the audit's census). | Original |
| TG7 | condition evaluator (0x4451B0, table 0x44714C), action executor (0x447220, table 0x44ADBC) | Conditions 0..52 are indexed directly; actions 1..141 by type − 1. Actions 54 and 55 share one handler (0x44867B); no other entries share one, and there is no default entry inside the range. These are the counts the audit's admission matrix uses. | Original |
| TG8 | dialog resume (0x568480, call at 0x5684AA) | A conversation's "return to script" runs the speaker's dialog point (9) again, triggerer the PC, at the line the reply names or the conversation's held line + 1. So a talk opened from a dialog-point script resumes that same slot; a conversation opened by another point (use, heartbeat call-ex to a non-dialog point) is resumed at the dialog point too. | Original |

## Heartbeat world questions (ARC-SCRIPT-049)

Source: the [script trigger audit](../script-trigger-audit.md) (slice 1) and the [dispatcher contract](../../scripting/scr-dispatcher-runtime.md#heartbeat-world-questions-arc-script-049-set-s18).
Question: what the conditions and actions the installed heartbeats reach first do. Read on 2026-10-29 by this task with `llvm-objdump`
in the retail 1.0.7.4 `Arcanum.exe` (SHA-256 as the method records), at the entries of the condition table 0x44714C (22, 27) and the
action table 0x44ADBC (71, 134), and their callees; read by this task only (F1).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| HW1 | script condition (0x4451B0) case 22 (0x445E90), critter is dead (0x45D8D0) | Obj is dead: the object list of operand 0 (0x44B4E0); each object counts when 0x45D8D0 holds: a null handle is dead, else its current hit points (0x43D600) are 0 or less. The list is released (0x44B8F0), then the SG6 rule: none counted (an empty list included) false, all counted true, some only for the "any" kinds 3, 5, 7, 9, 14, 16, 18, 20, 23. | Original |
| HW2 | script condition case 27 (0x4461B3) | Obj is switched off: the same list; each object counts when its object flags (0x406CA0, field 0x13) have bit 0x2 (off); then the SG6 rule. | Original |
| HW3 | script action (0x447220) case 71 (0x448E25), object distance (0x441AE0), tile distance (0x4B96F0) | Distance between: the first object of operand 0 and of operand 1 (0x44B960), each one's location field (0x406DA0, field 2) without a null test, the tile distance of the two locations (the larger of the absolute X and Y differences, 64-bit), stored into operand 2 (0x44BB50) as its low 32 bits. The run goes on. | Original |
| HW4 | script action case 134 (0x44A973), set faction (0x45D0C0) | Set faction: the first object of operand 0 and the value of operand 1; 0x45D0C0 returns at a null handle and for an object whose type (field 0x14F) is not an NPC (16), else writes the NPC's faction field 0x124. The run goes on. | Original |

## Talk commands (ARC-SCRIPT-050)

Source: slice 2 of the [script trigger audit](../script-trigger-audit.md#6-fix-slices-prioritised) and the
[dispatcher runtime](../../scripting/scr-dispatcher-runtime.md#talk-set-arc-script-050-set-s19). Question: what float line (23) and get area (84)
do, the two actions that kept most installed dialog scripts from running. Read on 2026-10-29 by this task only with `llvm-objdump` (F1).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| FL1 | action executor (0x447220), case 23 (0x44780A) | Calls 0x44B1F0 with the action and the run state and goes on with the next line. | Original |
| FL2 | float line (0x44B1F0), float callback slot (0x5E2FE8, set by 0x444990 from the dialog interface at 0x567368 to 0x568430) | Does nothing when the slot is empty, when the running attachment has no script number, or when the DLG named after it (0x4CEE60) does not load (0x412E10). In a game the dialog interface fills the slot. | Original |
| FL3 | float line (0x44B1F0), object list (0x440FC0) | The dialog's PC is the first object of type mask 0x8000 (a PC) in the game-window rectangle around the run's **triggerer**; with none the list is released and nothing more happens (no operand is read). | Original |
| FL4 | float line (0x44B1F0), NPC-line step (0x413A30 with 1 → 0x416840), callback 0x568430 | Operand 0 (0x44BA70) selects the DLG line. For each object of operand 1's list (0x44B4E0) in list order, that object is the dialog's NPC and the step builds the line as for print line (IS2: a missing line is one space, the line's action field runs between the PC and that object, generated text or the alternate text by the PC's gender), then the callback floats it over the object. The attachee is not needed. Afterwards the list and the DLG are released (0x44B8F0, 0x412F40). | Original |
| FL5 | float callback (0x568430) | Calls the dialog interface's speech display 0x568540 for the object with one style for a PC (type 15) and another for anything else; the display itself is not read (Steamwoven floats the text in the floater field). | Original (display F0) |
| GA1 | action executor (0x447220), case 84 (0x449468) | The first object of operand 0's list (0x44B960), its area (0x4CB6A0) stored into operand 1 (0x44BB50). | Original |
| GA2 | area of an object (0x4CB6A0), map area (0x40FFE0) | A null object has area 0. Otherwise the **current map's** area from its map-list row (0x40FFE0: runtime number 1..count, else 0 and the area is 0), whatever the object's map. When that area is 0 and the current map is the start map (0x40FF50 with 1), the area at the object's location (0x4CB630); on any other map 0. | Original |
| GA3 | area at a location (0x4CB630), nearest area (0x4CB220), town map of a sector (0x4BE380) | The nearest area by distance (0x4CB220: area 0's location first, then areas 1.. that are strictly nearer) counts only when the town map of the location's sector (sector field 0x4640, read through the sector lock 0x4D0540) is not 0 and equals that of the area's own location (0x4CAED0); else 0. Not modelled: a talk on the start map with map-list area 0 stops at get area. | Original (not modelled) |
| ML1 | map list load (0x4125C0 at 0x41267D..0x41271C), keyword readers (0x532480, 0x532320, 0x531990) | After the start the optional values are read through one cursor in a fixed order: `Type:` (0x5A05DC), `WorldMap:` (0x5A05D0, default −1), `Area:` (0x5A05C8, default 0). Each reader skips white space and needs its case-sensitive keyword at the cursor (strstr at the cursor); a mismatch leaves the cursor for the next reader, a match consumes the value up to and past its comma. The number is the C runtime's atol of the value. The row is 0x118 bytes; the area is at +0x114. | Original |

## Tile-script actors (ARC-SCRIPT-052)

Source: slice 8 of the [script trigger audit](../script-trigger-audit.md#6-fix-slices-prioritised) and the
[dispatcher runtime](../../scripting/scr-dispatcher-runtime.md#tile-script-actors-arc-script-052-set-s20). Question: what destroy (68) and
stop attacking (109) do, which the refused tile scripts reach most after call-ex (81, WE3). Read on 2026-10-29 by this task only with
`llvm-objdump`, at the entries of the action table 0x44ADBC (index type − 1) and their callees (F1).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| TD1 | script action (0x447220), case 68 (0x448D19) | Destroy: the first object of operand 0 (0x44B960) goes to the object destroy 0x43CCA0 without a null test; the case then returns as the other cases do and the run goes on. | Original |
| TD2 | object destroy (0x43CCA0) | In a network game (0x5E2F58 = 1) the request goes to 0x43CEA0 (not read). Otherwise an object whose flags (0x406CA0, field 0x13) hold 0x1 is left as it is; else the object's destroy point (2) runs with the object as triggerer and attachee (0x441980 at 0x43CD0E) and a skip default keeps the object. Then clean-up calls by type (field 0x14F; a PC or NPC: 0x45E180, 0x4AD7D0, for an NPC 0x4C1110 → 0x460A20; a critter or container: 0x4639E0 with 1; an item: its parent's 0x4612A0 path) and common ones (0x423FF0, 0x459740, 0x4601D0, 0x443770), each object of a list built at the start (0x43DFD0 with 7) is destroyed in turn (0x43CCA0), and last the object flags take 0x1 (0x406D40). The helpers' bodies are not read. | Original (helpers F0) |
| TD3 | script action case 109 (0x44A038), stop attacking (0x4AA4A0) | The first object of operand 0 (0x44B960) goes to 0x4AA4A0 without a null test (it reads the type, field 0x14F, at once: an NPC (16) takes the NPC branch, else the followers' one, as [OL](social.md#companion-orders-and-loyalty-arc-res-064) and the session's `StopAttacking` model it); the run goes on. | Original |

## Script stores (ARC-DEV-100)

Source: MIG8 slice 4 ([object directory](../../architecture/object-directory.md#mig8-slice-4-what-is-built-arc-dev-100), gap GR29) and the
[dispatcher runtime](../../scripting/scr-dispatcher-runtime.md#script-stores-arc-dev-100-set-s21). Question: how the script operand kinds 1, 5 and 6
(global variable, PC variable, PC flag) are read and written, which SA13 and SA14 only named. Read on 2026-11-05 by this task only with `llvm-objdump`
(the value reader 0x44BA70 and writer 0x44BB50 with their jump tables 0x44BB34 and 0x44BC3C, and the accessors 0x444C80 to 0x444F30), F1.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| GV1 | value reader (0x44BA70) kind 1, writer (0x44BB50) kind 1, 0x444C80 / 0x444C90 | The reader returns the 32-bit word at the operand's index of the 2000-word array (0x5E2FA0) with no bounds test. The writer stores the whole 32-bit value there, unmasked (a counter write is masked, X8; a variable is not), after the multiplayer gate 0x4A2BA0 (the one the global flag write 0x444D20 takes; a single-player game is on its yes side and stores; the other side sends packet 0x7C and stores only when the packet flag 0x62AC6C has bit 2). Kind 3 (a number) has no write: the writer does nothing for it. | Original. The project refuses an index outside 0..1999 at admission (like the global flags, both fixed: out of bounds). |
| GV2 | jump tables 0x44BB34 (reader) and 0x44BC3C (writer) | Seven kinds, 0 counter, 1 global variable, 2 local variable, 3 number, 4 global flag, 5 PC variable, 6 PC flag; a kind above 6 reads as the value itself and writes nothing. | Original (admission refuses a kind above 6 already) |
| GV3 | reader kind 5 (0x44BAA4), writer kind 5 (0x44BBA0), 0x444DB0 / 0x444DF0 | The operand's high half (an arithmetic shift by 16, so it keeps its sign) is a focus kind and its low 16 bits the index. The object is the first of that focus kind (0x44B960 with value 0 and the run), a 64-bit handle. The accessor reads the object's type (field 0x14F) and goes on only for 15, a PC: the reader returns the element of field 0x111 at the index (a sparse array of 4-byte elements, 0 where none), the writer stores the value there through 0x4F0270. For any other type the reader returns 0 and the writer does nothing. A null object reads undefined data (the project stops the run, as for OV3). | Original |
| GV4 | reader kind 6 (0x44BAE4), writer kind 6 (0x44BBE3), 0x444E30 / 0x444E90 | The operand is packed as in GV3. Flag *n* is bit *n* mod 32 of element *n* / 32 of field 0x110, and a read shifts that element right and masks 1. The write reads the element, clears bit *n* mod 32, ORs the whole value shifted left by *n* mod 32 (a 32-bit shift) and stores it back through 0x4F0270, so it spills exactly as the global flag write does: 2 at flag 33 sets flag 34, a negative value every higher flag of its word; again only for a PC. | Original bug (as X10): the Original setting spills (`GlobalFlagWriteSpills` covers both), Steamwoven writes the value's low bit into the flag only |

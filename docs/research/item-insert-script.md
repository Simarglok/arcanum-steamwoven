# The insert-item script of the Crash Site amulet (item 2527)

## Status, scope and evidence discipline

Status: **Research**, work item ARC-RES-055 (2026-10-18). It answers the open question that
[ARC-RES-023](crash-site-item-inventory.md#2-how-the-player-gets-each-item-s) left as an Unknown and that makes the
[session inventory](../world/inventory-runtime.md) (ARC-WORLD-014) refuse one of the six dead-NPC items:

> What does script 1341 do when item 2527 enters the PC's inventory? Which actions does it run, can it change or
> remove the item, and under which conditions does it fire?

It adds a real-data test and this note. It changes no production code, session rule, viewer or launcher file.
Requirement: [FR-ITEM-001](../requirements/items-economy.md#fr-item-001).

Categories: **Data (D)** is the authenticated installation (its `Version.txt` says 1.0.7.4) read through the project's
readers ([script content](../scripting/script-content.md), [SCR program](../scripting/scr-program-admission.md),
[Area T0 inventory](../world/area-inventory-view.md)); **Executable (E)** is a static reading of the retail
`Arcanum.exe` (SHA-256 `9bd80511…3678d52`) under the [original executable check](original-executable-check.md),
recorded in the [verification ledger](original-verification-ledger.md#item-insert-script-arc-res-055); **Proposal (P)**
and **Unknown (U)** as usual. No original text is published: numbers, sizes and digests only.

## Answer

- **What it does (D, E).** Script 1341 has four lines. If global flag 1085 is set, it returns and lets the default
  run; otherwise it prints line 1 of its own DLG (handed to the interface's message callback; that this is the message
  window is inferred, U1) and returns with "skip the default". That is
  all: the script writes no flag, variable, quest, rumor or object. Printing a DLG line also runs that line's action
  field (E, IS2), but this line's condition, response and action fields are empty and its text has no generated-text
  prefix, so here it only shows text.
- **Can it change or remove the item? (E)** No. Printing this line reads the script's DLG and hands text to the user
  interface; with an empty action field it touches no object. The insert-item point cannot cancel: the insertion is already done when the script
  runs and its result is ignored (ledger [IF10](original-verification-ledger.md#implemented-functions-arc-dev-025),
  [dispatcher table](scr-event-dispatcher.md), point 21).
- **When it fires (D, E).** On every insertion through the item, gold and ammunition insert functions that fire point 21
  (any holder, PC or not), every time: there is no local flag or counter. Only global flag 1085 silences it. Whether a
  load or generation path places items without those functions was not read. The same script also sits on the item's
  examine point, where "skip the default" suppresses the examine text and the same line goes to the same callback, so
  the line most likely stands in for the description (inferred).
- **At the Crash Site.** A new game clears every global flag (0x4446E0, 0x4447D0), and nothing reachable at the Crash
  Site sets 1085 (D4, IS4), so looting the amulet prints the line once per insertion, and examining it prints that line
  instead of the examine text. After a conversation that sets flag 1085 (below), both stay silent and examining shows
  the normal text.

So the item can move like the other five: the script has no game-state effect, only a message line. Admitting it needs
the line delivered to the host, not a new inventory rule (proposal below).

## Data (D)

Read by [ItemInsertScriptRealDataTests](../../tests/Arcanum.Data.Tests/Scripts/ItemInsertScriptRealDataTests.cs)
(`DAT_ROOT` = the game folder) and the Area inventory test
([AreaInventoryOriginalDataTests](../../tests/Arcanum.Data.Tests/Inventory/AreaInventoryOriginalDataTests.cs),
`ARC_AREA_*` inputs).

| ID | Fact |
| --- | --- |
| D1 | Item 2527 (armor type, name class 2807, description 8159), pack slot 0 of dead NPC 1201, has script 1341 on attachment point 0 (examine) **and** point 21 (insert item); no other attachment. The context item 3488 (same prototype, held by context NPC 1382) has the same pair. NPC 1201 has no script. |
| D2 | Script 1341 (`scr/01341…`) has 4 lines, no initial local flags or counters, file flags 0. Line 0: `eq(GF 1085, 1)` → `goto 3`, else `do_nothing`. Line 1: always → `print_line(1, 6)` (both numbers literal). Line 2: always → `return_and_skip_default`. Line 3: always → `return_and_run_default`. False branches of lines 1–3 do nothing. Type names are the [census](scr-census.md) names. |
| D3 | Its DLG (the script's name with the dialog extension) reads and holds exactly one record: NPC line 1 with 75 bytes of text and 75 of alternate text, neither starting with `g:`, `i:`, `m:` or `r:`, and empty condition, response and action fields. |
| D4 | Global flag 1085 across all 2,399 scripts and the 697 dialogs whose records the game keeps (4 more open with none, the rest have no DLG). A flag write is not masked (ledger X10): a value of 2 or a negative value also sets higher flags of the same 32-flag word, so the whole word 1056–1087 is checked. Scripts: no literal operand writes 1085 and only line 0 of 1341 reads it; the only writes into the word assign a literal 1 (to 1062, 1068 and 1070); three operands compute a flag index from a local variable (1957 line 9 reads 1201–1250; 2231 lines 2 and 6 read and clear 2495–2750), none of which can reach the word. Dialogs (operations read as code, index and value, leading zeros allowed): four set 1085 to 1 in actions, 1047 (records 1100, 1130, 1160, 1195, 1225, 1235), 1074 (379), 1096 (19) and 3011 (4); dialogs 1074 (376, 377) and 3011 (4) test it in player conditions; none clears it; every dialog write into the word is 0 or 1. None of these dialogs belongs to a Crash Site speaker (the only Crash Site dialog is Virgil's, 1324). |

## Executable (E)

| ID | Function (address) | Reading |
| --- | --- | --- |
| IS1 | `script_execute_action` (0x447220), jump table at 0x44ADBC | The table is indexed by action type − 1; entry 23 (type 24, `print_line`) is the case at 0x447822, which only calls 0x44B390 with the action and the run state and returns. Entry 43 is the teleport case 0x4481A1 (ledger, map transitions, TR1), which checks the indexing. |
| IS2 | print line (0x44B390), NPC-line step 0x413A30 → 0x416840 | Builds the DLG name of the running attachment's script (0x4CEE60) and loads it through the dialog cache (`dialog_load` 0x412E10); does nothing when the attachment has no script, the load fails or the attachee is null. Operand 0 through `script_get_value` (0x44BA70) selects the DLG line. The NPC-line step finds it (a missing line prints a single space), **runs its action field** through the DLG action interpreter (0x415BA0) between triggerer and attachee, and builds the text: a `g:`, `m:` or `r:` prefix or a whole text of `i:` selects generated text, otherwise the text or alternate text by the triggerer's gender when it is a critter. Operand 1, read the same way, travels with the text: in a single-player game to the user interface's message callback (0x460630, slot 0x5E86D8 of the table installed at 0x45FD40), in a network game (flags 0x62AC6C bit 0) by the triggerer's player slot (0x4A2B10): the local one gets the callback, another one a packet (0x4EDA60), none nothing. Then the cache reference is released (0x412F40). The function itself writes no global or object; what the line's action field does is the only game effect. |
| IS3 | print line (0x44B390) | A null attachee is tested after the DLG load and returns without the release, so the cache entry keeps one extra reference. Inserts always have the item as attachee, so this path is not reached here. |
| IS4 | global-flag write (0x444D20) and its six callers | Callers: the DLG action interpreter (0x415EF6), the script set and clear actions (0x449A4A, 0x449A72), the script value store (0x44BC2F), the network relay of a host's write (0x4EFD19) and the pre-made hero menu (0x545C1A, flags named by `MainMenu.mes`, ledger "Pre-made flags"). The first four write what the data says (D4); the menu writes the hero flags, not 1085. No engine code sets a fixed flag. |

Already in the ledger and used here: the insert-item script runs after the item entered the list and cannot cancel
(IF10); its roles are holder (triggerer) and item (attachee), result ignored, and examine is gated by its script
([dispatcher table](scr-event-dispatcher.md), points 0 and 21).

## Unknowns (U)

- **U1.** What operand 1 (here 6) selects in the message window (a colour or a message kind). It is passed to the
  interface callback, whose side was not read. It matters only for presentation.
- **U2.** Which object the line's name tokens refer to, if the text has any; it is not read here because the text is
  not published. Presentation only.
- **U3.** Whether the generated-text helpers behind the `g:`, `m:` and `r:` prefixes change state. Line 1 of 1341 uses
  none of them; a script that prints such a line needs them read first.

## Proposal (P)

1. **Admit 2527 through the script, not by exception.** A follow-up implementation task runs the item's insert-item
   script through the [SCR dispatcher](../scripting/scr-dispatcher-runtime.md) when the session moves an item (holder
   as triggerer, item as attachee, result ignored), with `print_line` added to an admission set as a host-facing
   message effect (DLG line and the second number), admitted only for a line whose action field is empty (or itself
   admitted) and whose text has no generated-text prefix. The session keeps refusing items whose insert-item script
   falls outside the admitted set. 2527 then loots, and the viewer shows the line while flag 1085 is clear.
2. The same path covers the examine point later (an inspect window would show the line instead of the description
   while 1085 is clear).
3. Size: the [census](scr-census.md) counts 41 insert-item scripts on 74 object slots plus 12 prototype defaults, 7 of
   them admitted by the S1 set on their live lines; how many the extended set admits is for that task to count.

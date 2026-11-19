# Original verification ledger: Function triage of the code bands 0x46, 0x49 and 0x4A

Part of the [original verification ledger](../original-verification-ledger.md) (ARC-RES-048): its [inputs and method](../original-verification-ledger.md#inputs-and-method), [verdicts](../original-verification-ledger.md#verdicts-and-what-they-mean-for-the-two-settings), [decisions to revisit](../original-verification-ledger.md#decisions-to-revisit) and [second reading](../original-verification-ledger.md#second-reading) cover every section below. A new section of this area goes at the end of this file and adds one line to the ledger's [area index](../original-verification-ledger.md#areas).

## Function triage of the exe bands 0x46, 0x49 and 0x4A (ARC-RES-110)

Source: slice D (V1) of the [function audit plan](../function-audit-plan.md#5-recommendation) over the [census](../../verification/census.csv) of ARC-RES-106. Read on 2026-11-11 on the owner's Mac (`Arcanum.exe` 1.0.7.4, SHA-256 `9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52`, checked before the reading) with `llvm-objdump` (no Ghidra), by one reader per sub-range with the repository's earlier readings at hand, and checked by the first reader against the executable and the code (below). No second reading was made, so every row of this section is **F1**, except the rows that point to an earlier two-reader ledger row (F2, their own evidence). The addresses of the bands 0x46 (items, inventory, prototypes), 0x49 (prototype files, network start, packet dispatcher) and 0x4A (network layer, level and light, NPC AI) that the census lists and [`functions.csv`](../../verification/functions.csv) did not name are now each a row there: 381 of them (551 census functions in the three bands; the other 170 had a row already).

The states of the audit plan map onto the registry's statuses: **not relevant** is `out of scope` with the reason class in front of the reason; **read and implemented** is `full` or `partial` with the `[OriginalFunction]` mark; **read and not implemented** is `not implemented` (also when our host does the work with no method of its own, as the earlier registry rows do); **not yet read** would be `not implemented` at F0 (none is left in these bands).

| Result | Rows |
| --- | --- |
| `out of scope` (not relevant) | 191 |
| `not implemented` | 168 (88 of them the deferred network game, FR-NET-001..008) |
| `partial` (marked) | 21 |
| `full` (marked) | 1 |
| Not read (F0) | 0 |

By band: 0x46 has 181 rows (117 out of scope, 49 not implemented, 14 partial, 1 full), 0x49 has 30 (15 and 15), 0x4A has 170 (59 out of scope, 104 not implemented, 7 partial). The two giants are one row each: 0x468930 (the prototype group fill, run by `Arcanum.Executable`, F2 with PX4) and 0x49D690 (the network packet dispatcher, NT5/NT6).

### Rules of this triage

- **Marks are strict.** A function is `partial`/`full` only when a method of ours carries its address in its own documentation and does what that function does for one part of its callers; the first pass of the reading proposed 81 such rows; 53 of them were turned into `not implemented` rows that name the nearest method and six into `out of scope` rows (five interpreter rows and the stub below), because the method is a host entry (`GameSessionOwner`, `AreaAiHost`, the viewer) that spreads the behaviour, is a property or constant (an attribute cannot go there), lives in an assembly the registry test does not load (`Arcanum.World`, the viewer, `Arcanum.Executable`), or the match was a guess. One first-pass row was wrong outright: 0x465010 is a stub that answers 0 (it is not the remove check, which is 0x466DA0, already marked).
- **`partial` says what is left out.** All but one marked row are `partial`; the reason is the reading's sentence plus "Read once (F1) and not compared branch by branch" where no second reading exists.
- **The interpreter is not a re-implementation.** The prototype build (0x4681B0, 0x468330, 0x468660, 0x468720, 0x468890, 0x468930, the critter file readers 0x49B010..0x49BF10 and their thunks) runs the original's own code in `Arcanum.Executable`: class `interpreted`, nothing to verify against the executable.
- **A callback slot is not the callback.** A slot is a function that calls a global function pointer when it is set. The slot is `out of scope`; the function installed behind it is a separate row elsewhere (the interface hooks are filled by the installer 0x45FD40 from the table that 0x57C6E0 builds).

### Reason classes of the `out of scope` rows

| Class | Rows | Meaning and addresses |
| --- | --- | --- |
| `callback slot` | 87 | Dispatchers that read one global function pointer and call it when set (interface notification hooks and the like). The row names the global; the installed callbacks are other functions. 4601C0, 4601D0, 4601F0, 460240, 460260, 460280, 4602A0, 4602D0, 460300, 460330, 460360, 460380, 4603E0, 460410, 460440, 460460, 4604A0, 4604C0, 4604E0, 4604F0, 460520, 460530, 460540, 460560, 460590, 4605C0, 4605D0, 4605E0, 460610, 460630, 4606C0, 4606E0, 4606F0, 460720, 460740, 460770, 460780, 460790, 4607B0, 4607D0, 460800, 460820, 460840, 460860, 460870, 460890, 4608B0, 4608D0, 460900, 460930, 460960, 460980, 4609E0, 460A20, 460A40, 460A80, 460AB0, 460AD0, 460AF0, 460B00, 460B20, 460B30, 460B50, 460B80, 460BA0, 460BB0, 460BC0, 460BE0, 460C00, 460C20, 460C30, 460C50, 460C80, 460CB0, 460CE0, 460D10, 460D30, 460D50, 460D70, 460D90, 460DB0, 460DD0, 460DE0, 460E10, 460E30, 460E50, 4A2A30 |
| `accessor` | 39 | Getters, setters and number lookups that our typed records or tables replace. 4612A0, 461310, 461340, 461520, 462410, 4631A0, 4642C0, 4649C0, 465690, 4673B0, 468570, 4685A0, 4685D0, 468600, 468860, 49B290, 4A2B00, 4A38A0, 4A39D0, 4A40D0, 4A4180, 4A41B0, 4A41E0, 4A4230, 4A4240, 4A4270, 4A55D0, 4A55F0, 4A5600, 4A6D40, 4A7DA0, 4A7DC0, 4A7EA0, 4A7FA0, 4A7FB0, 4A9530, 4AFE10, 4AFE20, 4AFE30 |
| `thunk` | 27 | Forwarders to one other function (including the matchmaker wrappers of the network layer); the row names the target. 49AFF0, 49B220, 49CC50, 4A4EB0, 4A4EC0, 4A4ED0, 4A4EE0, 4A4EF0, 4A4F10, 4A4F30, 4A4F50, 4A4F60, 4A4F80, 4A4F90, 4A4FA0, 4A4FB0, 4A4FD0, 4A4FE0, 4A5000, 4A5010, 4A5020, 4A5040, 4A5050, 4A5070, 4A5090, 4A50A0, 4A50C0 |
| `interpreted` | 14 | Code the original runs and Steamwoven runs the same way in `Arcanum.Executable`: the prototype build and the critter file readers. 4681B0, 468330, 468660, 468720, 468890, 468930, 49B010, 49B240, 49B2E0, 49B340, 49B5A0, 49BB40, 49BB70, 49BF10 |
| `start-up` | 12 | Module init, shutdown and free-all functions with no rule. 460E70, 460F90, 460FC0, 468310, 468800, 4A6850, 4A7C60, 4A7C80, 4A7CC0, 4A7CF0, 4AFDD0, 4A84D0 |
| `other` | 7 | Empty module hooks, a predicate that only serves the decay-event removal, and name-length helpers nothing calls. 4657E0, 465AA0, 468150, 49CB50, 4A3890, 4A83F0, 4A84C0 |
| `debug` | 3 | The developer critter text export and the NPC-fighting toggle behind a cheat key that nothing presses. 49C060, 49C610, 4AF860 |
| `runtime library` | 1 | A C-runtime-style helper. 462920 |
| `stub` | 1 | A function that answers a constant (the hexed-item check). 465010 |

### Read and implemented (marked)

| Address | Status | Method | What is left out |
| --- | --- | --- | --- |
| 461AB0 | partial | `GameSessionOwner.DropHeroItem` | Drop of an item by its owner: drop script, no-drop remove check, tile and placement; the hero's drop and the critical, arm-hit and spell weapon drop of a critter go through it (the drop script through `DropScript`, ARC-SCRIPT-2853; the NPC look-for bits are written, ARC-WORLD-1288). Read once (F1) and not compared branch by branch. |
| 462480 | partial | `SessionDialogItemHoldPort.NpcHolds` | Finds the first item whose name field equals a value in the inventory list of a container or critter (any other object holds nothing); our port answers yes or no for the dialog conditions in and ni, not the handle. Read once (F1) and not compared branch by bra |
| 462540 | partial | `Technology.SchematicAssemblyRules.Find` | Searches a holder list for an item of a prototype under flags (skip worn items, match description, quick slot fallback); only the schematic assembly flag set 7 is reproduced. Read once (F1) and not compared branch by branch. |
| 462C30 | partial | `Hotkeys.HotkeyRules.PlanItem` | First part answers 0 for an item with the use-box flag, else 6; the second part calls the hotkey placement hook dispatcher 0x460560 for an item with a held flag; the first part is the can-use test of the hotkey item plan. Read once (F1) and not compared branch |
| 463730 | partial | `GameSessionOwner.ObjectWrite` | Drops every item of a container or critter list, last first, through the drop placement or a plain tile move after regenerating a flagged container; only the broken container spill is reproduced. Read once (F1) and not compared branch by branch. |
| 4643D0 | partial | `GameSessionOwner.OpenBarter` | Sets the identified flag on every item of a holder list; reproduced inside the barter open for the NPC and its container when no PC leads it. Read once (F1) and not compared branch by branch. |
| 464970 | partial | `GameSessionOwner.DropHeroGold` | Creates a gold object of the base prototype with a quantity at a place; our drop of coins creates a made pile of a kind the host names. Read once (F1) and not compared branch by branch. |
| 466310 | partial | `Items.InventoryGridRules.Occupied` | Writes a value into every cell of an item's rectangle in a holder's 10-column grid map (worn 1000..1008 and quick 2000..2009 slots take no cell); we rebuild the occupancy map from the items instead of setting and clearing cells, and refuse a rectangle that lea |
| 4664C0 | partial | `GameSessionOwner.HeroGridRoom` | Builds a holder's occupancy grid (0x466260) and finds the first free place for the item's size (0x4675A0), returning the slot or -1; ours does it for the hero's grid only (FindSlot and Occupied compose in the session), the loot window uses its own arrange. |
| 466E00 | partial | `GameSessionOwner.DropHeroItem` | Asks the item's holder (0x4612A0), logs an error and returns when there is none, else calls the inventory removal 0x467860; ours covers the hero's drop, throw and take paths and NPC paths in the AI host, not one shared entry. Read once (F1) and not compared br |
| 4677B0 | partial | `GameSessionOwner.Equip` | Wield-on hook of the insert: for weapon or shield slots sets the critter's weapon art and refreshes the interface, for the body armour slot sets the armour art, then runs the item's spells (0x4CB7D0), recalculates the light and fires the wield-on script; ours  |
| 467860 | partial | `GameSessionOwner.DropHeroItem` | Takes an item out of its holder's list: ends its running spells, nulls the holder's stack field of that kind, unwields a worn item, flags an NPC giver for re-wielding, fires the remove-item script, dissolves an emptied junk pile, recalculates encumbrance and r |
| 467CB0 | partial | `GameSessionOwner.Unequip` | Wield-off hook of the removal: for weapon or shield slots restores the weapon art, for body armour sets the bare art, then runs the item's unwear spells (0x4CBFC0), recalculates the light and fires the wield-off script; ours runs the spells after the move and  |
| 468110 | partial | `GameSessionOwner.CancelDecay` | When the item no longer decays (0x468010) removes its pending Item Decay events with a predicate on the item and returns 1, else 0; ours cancels unconditionally on a pickup or deletion and tests decay in the schedule. |
| 468610 | full | `Characters.InventorySourceBuyRules.InBasicRange` | nothing |
| 4ABBC0 | partial | `Ai.HostilityRules.DangerUpdate` | Keeps the NPC focus when it is a valid target, else clears the target lock and runs the target scan; ours is the rescan branch inside the danger update. Read once (F1) and not compared branch by branch. |
| 4ABC70 | partial | `Ai.AiSupportRules.ChooseSpell` | Picks a spell or item for the danger update target: nothing for invulnerable NPCs or with NPC fighting off, then a chance roll, the summon list, the defensive list or the offensive list. Read once (F1) and not compared branch by branch. |
| 4AD060 | partial | `Ai.NpcRouteRules.ForcedStandpoint` | Answers nothing for an NPC with a leader, else the day or night standpoint location by the day test; ours picks the standpoint inside the standpoint step. Read once (F1) and not compared branch by branch. |
| 4AD7D0 | partial | `GameSessionOwner.ScheduleTick` | Stores the object handle in a global and removes every pending AI tick of that object through the predicate; ours removes other-map ticks when scheduling and on a change of place, the destroy path is separate. Read once (F1) and not compared branch by branch. |
| 4ADFF0 | partial | `GameSessionOwner.SetAiSupportBits` | Sets the look-for-ammunition, look-for-weapon and check-weapon bits in the NPC flags after a ranged attack with nothing to shoot. Read once (F1) and not compared branch by branch. |
| 4AE0A0 | partial | `Ai.MoraleRules.Party` | Counts a critter and its recursive follower list and adds their levels for the flee test; ours takes the follower list from the world. Read once (F1) and not compared branch by branch. |
| 4AE450 | partial | `Ai.HostilityRules.Avenge` | For a dead NPC the scanner protects: returns its focus when that focus is a valid target for the scanner, else nothing. Read once (F1) and not compared branch by branch. |

### Read and not implemented

The rows are in the registry (their `reason` is the reading). By domain:

| Domain | Rows | Addresses |
| --- | --- | --- |
| multiplayer | 88 | 466C40, 466CF0, 467520, 467D60, 467E00, 49C670, 49C780, 49C820, 49C930, 49CA20, 49CB60, 49CB80, 49CBD0, 49CC20, 49CC70, 49D100, 49D320, 49D570, 49D590, 49D690, 4A1F30, 4A1F60, 4A1FC0, 4A2020, 4A2040, 4A2070, 4A2A40, 4A2A90, 4A2AE0, 4A2B10, 4A2B60, 4A2BA0, 4A2BC0, 4A2BD0, 4A2BE0, 4A2C60, 4A2CD0, 4A2D00, 4A2E90, 4A2EC0, 4A3030, 4A3080, 4A30D0, 4A3170, 4A3230, 4A33F0, 4A3660, 4A3780, 4A38B0, 4A39F0, 4A3D00, 4A3D70, 4A3F40, 4A40F0, 4A4280, 4A4320, 4A43B0, 4A44C0, 4A45B0, 4A47D0, 4A49E0, 4A4C40, 4A4D60, 4A5290, 4A52C0, 4A5320, 4A5380, 4A53B0, 4A5490, 4A54A0, 4A54E0, 4A5510, 4A5570, 4A5610, 4A5670, 4A5710, 4A57F0, 4A5840, 4A5920, 4A59F0, 4A5CA0, 4A5D80, 4A5E10, 4A5EE0, 4A6010, 4A6190, 4A6470, 4A6560 |
| items | 35 | 461540, 461810, 461F60, 4626B0, 462760, 462820, 462880, 462A30, 463860, 464150, 464780, 4647D0, 464C50, 4650D0, 465530, 4655C0, 465760, 4657D0, 465BA0, 465C30, 465CA0, 465E30, 466390, 466AA0, 466BD0, 466D60, 4673F0, 467440, 467E70, 468180, 468190, 4681A0, 4A50D0, 4A51C0, 4A5460 |
| ai | 20 | 466230, 4A88D0, 4A9AD0, 4AA420, 4AAAA0, 4AB400, 4AB430, 4AC320, 4AC350, 4AD0B0, 4AD140, 4AD1B0, 4AD6B0, 4AD700, 4AE4E0, 4AEB10, 4AEE50, 4AF130, 4AF210, 4AFB30 |
| lighting | 6 | 4A7C90, 4A7D00, 4A7D40, 4A7DD0, 4A7EB0, 4A8060 |
| interface | 4 | 460160, 460650, 464630, 464700 |
| locks | 3 | 463110, 463240, 4AECA0 |
| objects | 3 | 463630, 4639E0, 464470 |
| advancement | 2 | 4A6DE0, 4A7B90 |
| technology | 2 | 4AFDE0, 4AFF90 |
| characters | 1 | 465C90 |
| combat | 1 | 465F70 |
| party | 1 | 4AA8C0 |
| skills | 1 | 4AE570 |
| dialogue | 1 | 4AFBB0 |

### What the reading found that the earlier documents did not say

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| FT1 | interface hook slots 0x4601C0..0x460FC0 (87 dispatchers), installer 0x45FD40 | The block is a table of dispatchers over global function pointers 0x5E8658..0x5E87C4; the installer fills them from the structure that 0x57C6E0 builds on its stack. That builder never writes three members (0x110, 0x148 and 0x154), so their slots take whatever the stack held: 0x460B50 (already TC2, the crash class), 0x460D30 and 0x460D90, which nothing calls directly. | Original; 0x460B50 original bug (TC2) |
| FT2 | 0x460BC0 | Tests one global (0x5E8774) for non-null and then jumps through the next one (0x5E8778). The table fills both (members 0x11C and 0x120), so it is not a slip in practice. | Original |
| FT3 | 0x465010 | The hexed-item check is a stub that always answers 0: nothing is ever refused for being hexed. | Original (confirms the note on 0x466DA0) |
| FT4 | 0x461F60, 0x4626B0, 0x462820 (AI ground search), 0x4639E0 (inventory on destroy), 0x463860 (corpse inventory), 0x464470 (item list repair after a copy), 0x463630 (summon marking) | These item helpers: since ARC-AI-1309 the AI host answers the worthless flag (from the item's worth field) and the first-in-pile answer (`AreaAiHost.FirstInPile`) for the world; 0x462820, 0x4639E0, 0x463860, 0x464470 and 0x463630 are not touched by that task. | Original; open (not implemented) |
| FT5 | 0x4A50D0, 0x4A51C0 | In single player they set (0x4A50D0, read in full) and clear (0x4A51C0, by its reading) item flag 0x10 of the item they are called for; the item flag list names 0x10 as the no-display flag only by the reading's note, and no code of ours writes that flag. | Original; open |
| FT6 | 0x4AEB10 (portal permission), 0x4AEE50 (portal and container guards), 0x4AF130 (start fleeing), 0x4AD0B0 (bed re-seat), 0x4AD140 (tile object finder) | The AI parameter slot 16 is stored but no one asks it; the guards' witness reactions raise flags the host never acts on; only the stop-fleeing side of flee exists; a sleeping NPC is not re-seated and no route step searches for a bed (built since by ARC-WORLD-1162 slice 2, see the [contract](../../world/sleep-window-runtime.md#npc-beds-arc-world-1162-slice-2-ledger-swb16swb21)). | Original; the bed part built |
| FT7 | 0x4A7D00, 0x4A7D40 | Light scheme save and load; ours recomputes the ambient light from the clock and the sector, so nothing is saved. | Original; Steamwoven recomputes |
| FT8 | 0x4A6DE0, 0x4A6D40 | 0x4A6D40 is the field-0xF5 getter (the scheme read of AL2). 0x4A6DE0 looks the scheme's level-name text up and returns its pointer (0 for none; corrected by ARC-UI-1742, [ALP2](character.md#character-sheet-scheme-page-arc-ui-1742), the earlier reading took it for the number); it reads the name files, not the rule files (0x4A6E40, AL2). The [combat-domain load map](../combat-domain-load-map.md) calls 0x4A6DE0 the scheme read. | Original; that wording to be rechecked |
| FT9 | multiplayer band 0x49C..0x4A5 | 88 rows now state what each network function does, including the single-player branches of 0x4A2BA0, 0x4A2BE0, 0x4A2C60, 0x4A3230 and 0x4A2B10 (always local, hero lookup, callback at once) that our code takes as given. | Original; deferred (FR-NET) |

### Not checked

- The F1 readings were not repeated by a second reader. The method column of the marked rows and the "nearest method" notes of the demoted rows were matched by grep and by the addresses cited in the code, not by running the original function.
- The subagent readings of the 0x4A2..0x4A5 network functions describe some functions by their callers and call lists only (0x4A2D00, 0x4A45B0, 0x4A47D0, 0x4A49E0, 0x4A5840, 0x4A59F0, 0x4A5CA0); the notes say so.
- Two census ranges hold a second function: 0x4A4280 also covers 0x4A4290 (no callers) and 0x4A7FB0 also covers 0x4A7FC0 and 0x4A8010; only the first is read.
- Census sizes include trailing padding and, for a few functions, the first instruction was missing from the stored listing; the readings inferred the head from the surroundings.

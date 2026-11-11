# Original verification ledger: Function triage of the code band 0x55 to 0x59

Part of the [original verification ledger](../original-verification-ledger.md) (ARC-RES-048): its [inputs and method](../original-verification-ledger.md#inputs-and-method), [verdicts](../original-verification-ledger.md#verdicts-and-what-they-mean-for-the-two-settings), [decisions to revisit](../original-verification-ledger.md#decisions-to-revisit) and [second reading](../original-verification-ledger.md#second-reading) cover every section below. A new section of this area goes at the end of this file and adds one line to the ledger's [area index](../original-verification-ledger.md#areas).

## Function triage of the exe band 0x55-0x59 (ARC-RES-120)

Source: slice H2 (V1, wave 2) of the [function audit plan](../function-audit-plan.md#5-recommendation) over the [census](../../verification/census.csv) of ARC-RES-106 (slice H1, ARC-RES-119, takes 0x50-0x54). Read on 2026-11-11 on the owner's Mac (`Arcanum.exe` 1.0.7.4, SHA-256 `9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52`, checked before the reading) with `llvm-objdump` (no Ghidra), by 17 readers that each took one address chunk of 12 to 18 KB of code with the repository's earlier readings at hand. No second reading was made, so every row of this section is **F1**, except 9 rows that point to an earlier two-reader ledger row (F2, their own evidence). The census lists 1236 function starts in 0x550000-0x59FFFF; 93 of them had a registry row already and are unchanged, and the other **1143** are this slice: each now has exactly one state. The registry [`functions.csv`](../../verification/functions.csv) grew by 193 rows (the read functions); the 950 not relevant functions are listed by class below and have no registry row, as in [slice C](census-triage-0x44-0x45.md).

The states of the audit plan map onto the registry's statuses: **not relevant** has no registry row and a reason class here; **read and implemented** is `full` or `partial` with the `[OriginalFunction]` mark; **read and not implemented** is `not implemented` (also when our host does the work with no method of its own); **not yet read** would be `not implemented` at F0 (none is left in this band).

| Result | Functions |
| --- | --- |
| Not relevant (reason class, no row) | 950 |
| `not implemented` rows | 152 |
| `partial` rows (marked) | 41 |
| `full` rows (marked) | 0 |
| Not read (F0) | 0 |

By block of 0x10000: 0x550000 has 200 (150 not relevant, 39 not implemented, 11 implemented); 0x560000 has 251 (193 not relevant, 48 not implemented, 10 implemented); 0x570000 has 220 (139 not relevant, 61 not implemented, 20 implemented); 0x580000 has 342 (338 not relevant, 4 not implemented, 0 implemented); 0x590000 has 130 (130 not relevant, 0 not implemented, 0 implemented). The code of 0x550000-0x57FFFF is the game's interface windows (message window, character sheet, world map, dialog, inventory, barter, skill bar, hotkey bar, rest window, multiplayer lobby); the part from about 0x580000 is the options window and the C runtime library.

### Rules of this triage

- **Marks are strict.** A function is `partial` or `full` only when a method of an assembly the registry test loads (`Arcanum.Data`, `Arcanum.Simulation`, `Arcanum.Scripting`, `Arcanum.Session`, `Arcanum.World`) does what that function does for one part of its callers. A method of the viewer or of `Arcanum.Godot`, a property, a constant or inline code cannot carry a mark; such a function is a `not implemented` row that names the nearest code. Interface functions mix drawing with rules: the drawing, layout and icon work is `draw` or `ui` (not relevant), the rule inside (a text line's content, a cost, an enabled state, what a click does) is read and judged against our code.
- **`partial` says what is left out.** Every marked row is `partial`; the reason says what the function does and what ours does not.
- **The multiplayer lobby is the deferred network game.** Server list, account logon and creation menus, player list, chat strip, the dialog and trade packet code and the lobby windows are class `network` (FR-NET-001..008, deferred by the owner); they are not read for rules.
- **The C runtime is class `crt`.** The heap, stdio, scanf, time-zone, file, float conversion, string and multibyte helpers of the statically linked library, with their thunks. Names there are best guesses from the callers and nothing in the game's rules depends on them.

### Reason classes of the not relevant rows

| Class | Rows | Meaning and addresses |
| --- | --- | --- |
| `crt` | 268 | C runtime or library code with its thunks and helpers. 589BD0, 589C22, 589CAD, 589CB8, 589D80, 589E08, 589F33, 58A029, 58A04E, 58A072, 58A320, 58A347, 58A3B0, 58A4F7, 58A6EA, 58A716, 58A728, 58A754, 58A7D0, 58A8A0, 58A99E, 58AA4F, 58AA7C, 58AA8D, 58AABC, 58AB55, 58AB70, 58AB90, 58AC40, 58AC70, 58ACC0, 58AD80, 58ADB0, 58AE40, 58AE70, 58AEE5, 58AF74, 58AFA0, 58B060, 58B395, 58B3EB, 58B442, 58B462, 58B475, 58B4A7, 58B4D4, 58B530, 58B55A, 58B575, 58B5A6, 58B62C, 58B647, 58B67B, 58B70C, 58B7A8, 58B846, 58B922, 58B939, 58BB41, 58BB58, 58BB90, 58BBD0, 58BC08, 58BC59, 58BD07, 58BD2F, 58BE8F, 58BEDF, 58BF1A, 58BF76, 58BF7F, 58BFEC, 58C02F, 58C060, 58C16A, 58C1B7, 58C20B, 58C240, 58C328, 58C42E, 58C48F, 58C4DC, 58C568, 58C59C, 58C6F4, 58C773, 58C875, 58CA06, 58CA34, 58CAD5, 58CB06, 58CB2B, 58CB99, 58CBC7, 58CC7A, 58CE3E, 58D0F7, 58D2ED, 58D3B7, 58D47C, 58D49B, 58D509, 58D527, 58D63C, 58D6F5, 58D798, 58D7D0, 58D8F3, 58E034, 58E069, 58E09A, 58E0FD, 58E211, 58E2C5, 58E321, 58E34C, 58E35E, 58E39C, 58E3C5, 58E41F, 58E46D, 58E485, 58E4C3, 58E5C7, 58E6A5, 58E78A, 58E7DB, 58E800, 58E941, 58E984, 58F42C, 58F465, 58F6BB, 58F703, 58F72E, 58FA57, 58FD60, 58FE11, 58FF0C, 5902D3, 590624, 590768, 5907BE, 590880, 5908D7, 59091C, 590B24, 590C48, 590CF1, 590E65, 590E80, 5911B5, 59134E, 591398, 5913CB, 5913F4, 591589, 5915A5, 591630, 5916B0, 591708, 591801, 591805, 591851, 59187C, 591955, 591AC5, 591B3D, 591BCA, 591C10, 591C80, 591CF5, 59271A, 592751, 59276B, 592782, 5927A6, 5927F0, 592800, 5928E0, 5929A2, 5929B7, 592C15, 592DC1, 592F01, 592FFA, 593051, 5930E2, 59317C, 5931E3, 593227, 593252, 593258, 5932B8, 5932F4, 593355, 593399, 59342E, 5934A5, 59351F, 59355C, 5935F9, 59373F, 5937BD, 5937D0, 5938A1, 5938D8, 593940, 593953, 593967, 59399C, 5939DC, 593A6E, 593AF7, 593B2C, 593B75, 593BCB, 593C57, 593C72, 593C7E, 593C99, 593D26, 593E92, 593EA8, 593EBE, 593F38, 593F65, 593F93, 593FC0, 594037, 59409B, 594151, 5941EE, 594237, 594268, 5942F1, 5943C0, 594444, 5944C1, 594992, 5949CE, 594A44, 594A65, 594AC3, 594AF1, 594B1E, 594BE5, 594E78, 594EB7, 594F25, 595145, 5951C1, 595378, 59537C, 5953E7, 5953EB, 59543E, 595469, 5955F0, 595648, 5956AF, 595898, 595A50, 595A60, 595AA0, 595B4D, 595B77, 595BF7 |
| `network` | 155 | Multiplayer lobby, server list, account menus and packet code (deferred network game). 55F360, 55F450, 567AB0, 567C30, 567D60, 568220, 568280, 569AE0, 569B50, 569B60, 569C30, 569C50, 569C60, 569C70, 569D60, 569DA0, 569DC0, 569DD0, 569F20, 569F30, 569F40, 56A000, 56FF80, 570050, 5700B0, 5700C0, 570130, 5701A0, 570260, 5703B0, 5703D0, 5704E0, 5706D0, 570890, 5708C0, 570940, 5709E0, 570A10, 570A40, 570BC0, 570E10, 570E40, 570EF0, 571100, 5713F0, 571570, 5715A0, 571610, 571630, 571660, 571730, 57A6C0, 57AC90, 57CBC0, 57CEE0, 57CFB0, 57D080, 57D150, 581700, 5818C0, 581910, 581950, 5819D0, 581A60, 581A80, 581A90, 581B10, 581E60, 581F10, 581F30, 581F90, 581FB0, 581FC0, 582060, 582440, 5824C0, 582510, 5825B0, 582650, 582670, 582690, 5826B0, 5826C0, 5826D0, 582790, 582860, 5829D0, 582AD0, 582D10, 582D20, 582D40, 582D50, 582D60, 582E50, 582E80, 5830F0, 583140, 583200, 583510, 583540, 583560, 5836A0, 5837A0, 583830, 583A00, 583A90, 583B20, 583C80, 583D90, 584150, 5845E0, 5847D0, 584A40, 584AE0, 584C00, 584C30, 584CB0, 5850C0, 585270, 585630, 585970, 585A20, 585BC0, 585CB0, 585D50, 585E20, 585E40, 585F00, 585FF0, 5860D0, 586150, 5862D0, 586AD0, 586D20, 586ED0, 587570, 587960, 587D60, 588180, 5883E0, 588650, 5887F0, 588990, 588B10, 588B70, 588C80, 588CC0, 588D00, 588D70, 588D90, 588E10, 588EC0, 588EF0, 588F10, 588FF0 |
| `ui` | 147 | Interface plumbing with no game rule: window open, close and refresh, hooks and callback slots (functions that call one global function pointer when it is set). 550150, 5504F0, 5506C0, 550720, 550770, 5507E0, 550860, 5508C0, 550CD0, 550D60, 551160, 551740, 5517A0, 5518C0, 551F20, 551F40, 552080, 5520D0, 552130, 5526F0, 5528E0, 552930, 553320, 553350, 556A90, 556B70, 556E60, 556F80, 5570A0, 5571C0, 557230, 5572B0, 5572D0, 557330, 557790, 557810, 5578E0, 557B60, 557DF0, 557EA0, 557ED0, 55A230, 55B0E0, 55B150, 55B280, 55EFB0, 55EFE0, 55F0E0, 560010, 563270, 563590, 563610, 563790, 563B10, 563C00, 563C60, 564030, 564070, 564320, 566E50, 566ED0, 5672A0, 5672D0, 567320, 567A20, 567A60, 5689B0, 568A40, 568C90, 568CF0, 568D20, 568F20, 56A4A0, 56A880, 56A8D0, 56A940, 56A990, 56AFD0, 56B0F0, 56B4D0, 56B620, 56B850, 56B880, 56BC90, 56BF60, 56BFC0, 56D0D0, 56D2D0, 56D310, 56D4D0, 56EC00, 56ECE0, 56ED00, 56ED20, 56EF40, 56FBF0, 56FC40, 56FC70, 56FE40, 56FF40, 5718A0, 571910, 576100, 579C40, 579D70, 579DA0, 579DD0, 579E00, 57A1A0, 57B6E0, 57CAB0, 57CAE0, 57CAF0, 57CB80, 57CE10, 57CF70, 57CFA0, 57D370, 57D3B0, 57D3E0, 57D840, 57DAB0, 57DC20, 57E8B0, 57F260, 57F6D0, 57F720, 57F750, 57F8A0, 57F9D0, 57FA70, 57FE60, 57FF80, 580390, 5803D0, 5804E0, 580690, 580720, 580780, 580B10, 5810D0, 5811C0, 5811F0, 581280, 5812E0, 586AB0, 5894C0 |
| `accessor` | 101 | Getters, setters and small leaf functions that read or write one field or global; the rule lives in the callers. 5507D0, 551A00, 551F70, 552070, 5533B0, 553A60, 5577D0, 5577E0, 5577F0, 557800, 5578C0, 5578D0, 557AA0, 557AB0, 557AC0, 557B00, 557B10, 557B20, 557B30, 557B50, 558130, 5589D0, 55A220, 55B410, 55B4D0, 560740, 560750, 560F90, 5614C0, 562F90, 563D90, 564000, 5643C0, 5648E0, 566EF0, 567400, 567420, 567A10, 5695F0, 569A90, 569AA0, 569AC0, 569AD0, 571890, 5735F0, 573600, 573620, 573730, 573840, 579760, 579F50, 57A700, 57B4E0, 57C520, 57D640, 57D650, 57DE00, 57E5A0, 57F240, 57FA40, 5811A0, 5811B0, 5816A0, 5816D0, 5816E0, 5816F0, 584A80, 584A90, 589540, 589560, 589580, 5895A0, 5895D0, 589710, 589740, 589750, 589780, 589790, 5897C0, 5897D0, 589800, 589810, 589840, 589860, 589880, 5898A0, 5898C0, 5898E0, 589900, 589920, 589940, 589960, 589980, 5899A0, 5899C0, 5899E0, 589A00, 58E0D2, 58E0DF, 58E0EF, 595BEF |
| `draw` | 81 | Pure drawing, layout, icons, fonts and button plumbing with no decision. 5503F0, 550930, 5509C0, 550A10, 550DA0, 551000, 551080, 551A10, 5521B0, 5522F0, 552960, 5533C0, 553620, 553960, 554560, 554B00, 5561D0, 556B90, 556C20, 557F30, 557FD0, 55B880, 55BAB0, 55C110, 55C2E0, 55C890, 55CA70, 55F110, 561430, 5632A0, 563300, 563D50, 5642F0, 565230, 5656B0, 5657A0, 565D00, 565E40, 565F00, 566A80, 566D10, 568880, 568E70, 568F40, 569770, 5699F0, 569A60, 56A300, 56A6E0, 56C590, 56C630, 56C750, 56F2F0, 56F660, 56F840, 571990, 5782D0, 579E30, 57B4F0, 57D700, 57D810, 57E500, 57F160, 57FAD0, 57FB20, 57FB50, 57FC20, 57FC90, 57FD80, 57FFB0, 580000, 580190, 5807F0, 581360, 5813E0, 581460, 5814E0, 581550, 5815A0, 5815D0, 581660 |
| `startup` | 67 | Module init, shutdown, free-all and registration functions with no rule. 5501C0, 550BD0, 550D20, 558700, 558AF0, 558B50, 558CE0, 559690, 559770, 55E110, 55EBA0, 55F8D0, 55FF80, 560150, 560200, 567330, 5673B0, 5689D0, 568A00, 5695B0, 5695D0, 56A020, 56A2B0, 56A820, 56BB20, 56D040, 56D0B0, 56EA90, 56EBE0, 56FAE0, 56FBB0, 56FBC0, 5717F0, 571880, 572060, 572190, 5721D0, 579E80, 579F30, 579F40, 57B080, 57B140, 57C6E0, 57D240, 57D2D0, 57DA50, 57F4D0, 57F680, 580410, 580480, 580490, 589ED3, 589EEB, 58A01E, 58E9DC, 58EA95, 58EB2E, 58ECE2, 58EE14, 58EFE2, 58F00F, 58F157, 58F25C, 58F27C, 58F29E, 58F332, 58F608 |
| `forwarder` | 67 | Thunks and default-argument wrappers to one other function. 550750, 5517F0, 551830, 552050, 552160, 552740, 553370, 553380, 5533A0, 553910, 554BE0, 557EC0, 558110, 5581A0, 559550, 5597B0, 55B720, 55F0D0, 55F160, 55F180, 55F1A0, 55F1E0, 55F320, 55F340, 560760, 560790, 562FA0, 562FC0, 562FE0, 563750, 563770, 563DE0, 564140, 5673A0, 567E00, 569060, 569550, 56B280, 56EBF0, 56F990, 5708B0, 5754C0, 578B80, 57A6B0, 57AC50, 57B170, 57BB40, 57CAA0, 57CBE0, 57CDF0, 57CE00, 57D350, 57EDA0, 57EF90, 5806F0, 583A80, 583B10, 584AA0, 584AC0, 585BA0, 585BB0, 589530, 58C761, 58CE27, 58D6E1, 58E740, 58E767 |
| `stub` | 38 | Functions that answer a constant or do nothing. 5572C0, 5581B0, 5581C0, 5581D0, 560EE0, 5627B0, 5627F0, 563200, 563D80, 5642E0, 564830, 5649C0, 5649D0, 565130, 565CF0, 566E20, 566E30, 566E40, 5681B0, 568A30, 569040, 569050, 569570, 569580, 569590, 5695A0, 5719D0, 5719E0, 57BB30, 57BB50, 57CA90, 57D2C0, 57D300, 57D800, 588F00, 589EEA, 58E20D, 58E2C1 |
| `own-format` | 9 | Save and load of the map notes, replaced by our snapshot (see below the table). 560040, 560280, 560350, 560440, 5604B0, 5605C0, 5606B0, 564FD0, 5650C0 |
| `other` | 8 | Small functions that fit no other class; the reason of each is in the table below the classes. 563000, 5630F0, 563210, 5640C0, 564210, 564360, 5650E0, 589B90 |
| `folded` | 7 | A piece of a function listed elsewhere (inlined tail or shared fragment). 558A40, 558AC0, 558AD0, 558C90, 558D00, 558D20, 575580 |
| `dead` | 2 | Nothing calls it and no table holds it. 563F00, 563F90 |

The `own-format` rows are the save and load of the town and world-map notes (.tmn files and the data2.sav module): our snapshot keeps the notes whole (`GameSessionOwner.RecordMapNotes`), so there is no method per original function and none is needed. The hotkey bar and maintain bar save and load rows are `partial` instead because a method of ours maps onto each of them (the entries, their recent actions, their slots). The rows of class `other`:

| Address | Function | Why not relevant |
| --- | --- | --- |
| 563000 | map tile bitmap load | resource plumbing: builds the tile path (town tiles fall back to tile 0 when the file is missing), loads the bitmap into a video buffer, marks it loaded; ours loads town tiles in the viewer |
| 5630F0 | map bitmap to video buffer | resource plumbing: loads a world-map or town-map bitmap into a video buffer and reports its rectangle; failure is logged |
| 563210 | map tile unload | resource plumbing: frees a loaded tile's video buffer and clears its loaded flag |
| 5640C0 | note text accept | accept callback of the note text entry: non-blank text keeps or adds the note, blank text deletes it, then redraws; reached only through the quill state no retail control enters (WM126) |
| 564210 | note delete by id | removes a note by id and compacts the list; reached only through the blank-text accept of the quill state that no retail control enters |
| 564360 | note open for edit | finds a note by id and, unless its flag bit 2 is set, starts text entry; reached only in the quill state no retail control enters |
| 5650E0 | party heading weight dead value | computes a quarter-step weight of a global from two points and hands it to the empty stub 0x565130; the value is never used |
| 589B90 | editor degree change dispatch | dispatch on +1 to editor degree buy 0x589A20 or -1 to sell 0x589AF0; rules live in those |

### Read and implemented (marked)

| Address | Status | Method | What is left out |
| --- | --- | --- | --- |
| 551A80 | partial | `Arcanum.World.InterfaceModeStack.Change` | The mode stack: 0 pops, another value pushes; lock, leave then enter cases, a leave of mode 1 or 2 pops again. Ours has the stack and its steps; the cases' effects (cursor refresh, held item put back, window opens) belong to the observer host. |
| 5570D0 | partial | `Arcanum.Session.GameSessionOwner.SettleHotkeys` | After an item leaves or enters the hero's inventory, hotkey entries whose item moved are cleared and item entries' counts refreshed. Left out: closing bar mode 8 when its item is removed, clearing the two quick slots naming the item, hiding the weapon button, and the cursor refresh. |
| 55A150 | partial | `Arcanum.Session.GameSessionOwner.CloseHeroEditor` | Closes the open sheet or editor: in creation mode (0) clears the PC's hit-point and fatigue damage, in modes 1 and 2 makes the interface close call, frees the window handles, clears the shown critter and snapshot globals and resumes auto-attack when its saved flag is set (any mode). Ours drops the editor floor only; no damage clear in creation mode and no interface or auto-attack handling. |
| 55A240 | partial | `Arcanum.World.AreaCharacterSheet.Build` | Prints the examined name (gold, shortened until it fits), unspent points, level and experience to next (a last-level text at the cap), then race, gender and age for a PC; an NPC gets race and gender only when it is not monstrous. Ours builds the view values (name, level, experience to next, points, race and gender, hidden for monsters) but not age or the layout. |
| 55B1B0 | partial | `Arcanum.World.AreaCharacterSheet.Build` | Draws the effective levels of stats 8..15 (carry weight, damage bonus, AC adjustment, speed, heal rate, poison recovery, reaction modifier, max followers) and the five display resistances (without the magic adjustment of unidentified items), each as a number. Ours reads the same stats and resistances into the view, with effect changes of resistances not modelled. |
| 55B2A0 | partial | `Arcanum.World.AreaCharacterSheet.Mark` | The first three entries and the base columns draw nothing; maximum hit points and maximum fatigue draw plainly; a primary stat draws its effective value green when the non-innate effects raise its base value and red when they lower it. Ours carries the colour rule; positions, fonts and the hit-point and fatigue draw are the viewer's. |
| 55BD10 | partial | `Arcanum.World.AreaSkillsPage.BuildFor` | Shows one of four groups of four skills (three basic groups, then tech): keeps the current group, hides all +/- buttons and shows the group's, except in the NPC mode; prints each row's training name in parentheses when trained and draws the bars. Ours builds the sixteen rows with name, training text, points and level; button visibility and group memory are the viewer's. |
| 55BF20 | partial | `Arcanum.World.AreaCharacterSheetPagesLayout.GaugeVisible` | For each of the four rows draws a bar of the bar art's width times effective level over 20 (none at level 0) and computes the point cost of the next level, which is never used. Ours carries the gauge width rule as six pixels a level, clamped; the unused cost has no effect. |
| 55C3A0 | partial | `Arcanum.World.AreaCharacterSheetPagesLayout.DegreeColour` | Redraws the tech page: discipline buttons, the header (discipline and degree names), the effective expertise, seven degree rows (name and components) coloured white up to the stored degree, yellow for the next and grey after it, plus the + and - buttons (by degree, mode and snapshot). Ours carries the colour rule, the minus test and the expertise; layout is the viewer's. |
| 55CBC0 | partial | `Arcanum.World.AreaSpellsPage.BuildFor` | Redraws the spells page of the shown college: for each of five ranks the icon, the name (white for known ranks, gold for the next, grey above), the minimum-level text, and the + (below five ranks) and - (down to the floor; at the main-menu modes down to rank 0) buttons. Ours builds the five rows with known, next and locked states and minimum levels; colours, buttons and menu modes are the viewer's. |
| 55F200 | partial | `Arcanum.World.AreaSheetRefusals.KeyStatMessage` | When the editor is open posts the 'raise <stat> first' message for the skill's key stat: strength 32, dexterity 33, constitution 34, beauty 35, intelligence 26, perception 37, willpower 36, charisma 38, 22 for any other. Ours reproduces the stat-to-message table; the editor-open gate and the text-bar post are the host's. |
| 5615D0 | partial | `Arcanum.Simulation.Travel.WorldJourney.Cancel` | Sets the tool state: entering travel stops attacking, hides the party and starts the map music; leaving it shows the party, removes WorldMap events (kind 14) and lands the party (snap, cache save). Also quill cursor, drag release snap and compass. Ours: the leaving-travel landing only; party hiding, music, attack stop and cursor not modelled. |
| 561860 | partial | `Arcanum.Simulation.ChangeOfPlace.ChangeOfPlaceRules.EmptiesCacheBeforeRequest` | Saves and evicts every unlocked sector, then submits a change-of-place request to the location on the start map (flag 0x20). Ours: the cache-emptying rule for the landing caller is a rule method; building and submitting the request is the host's. |
| 5618B0 | partial | `Arcanum.World.AreaWorldMap.RemoveWaypoint` | Mouse/key handler: left press adds or selects a waypoint (30 cap, known-area snap), drag and release, right release or Delete removes a waypoint and leaves travel, compass scroll, kind switches, travel button, hover readouts; a scripted-open click picks the nearest known area within 320 tiles, charges the caster, teleports (message 640 if refused). Ours: waypoint removal; the scripted teleport and compass are not. |
| 564A70 | partial | `Arcanum.World.AreaWorldMap.Step` | Finds the area covering a location (radius test, no arrival flag); if the PC does not know it, the area becomes known, the last-discovered record is cleared when it is that area and a map marker is added; a known area only clears the last record when equal. Ours discovers in the travel step but clears the last record whenever one is set and the marker is the host's. Also called from the sector entry hook 0x564F60, which ours lacks. |
| 567460 | partial | `Arcanum.Session.GameSessionOwner.EndConcealmentForDialog` | Refuses a dead or unconscious PC, an NPC that cannot speak, a second dialog of the local PC and a PC that cannot leave combat mode; otherwise ends concealment of both, leaves combat mode, turns the NPC to the PC, fills the slot, begins the reaction conversation, pushes dialog mode 3 and shows the first node or a line. Ours: only the concealment end and the host's start. |
| 5678D0 | partial | `Arcanum.World.AreaConversationHost.EndReaction` | For the PC's dialog slot: clears the active word, pops dialog mode 3 for the local PC, frees the dialog state, expires the NPC bubble and calls the conversation end that clears the NPC's partner slot. Ours: the conversation-end half only; window pop and bubble are the viewer's. |
| 5679C0 | partial | `Arcanum.World.AreaConversationHost.EndReaction` | Silent close of one active slot (the reset path): clears the active word, pops the modal state for the local PC, frees the dialog state and calls the conversation end. Ours: only the conversation-end half (teardown, map change); no window or bubble. |
| 5681C0 | partial | `Arcanum.World.AreaConversationHost.GenericGreeting` | Builds the generic greeting text of a talk request whose dialog point ran default (0x4132A0, refusing an NPC that cannot speak) and shows it through the speech display as a floating line. Ours: the text floated over the NPC; the log record, speech sound, duration and the can-speak refusal are not made. |
| 568480 | partial | `Arcanum.World.AreaConversationHost.Resume` | Runs the object's dialog script point (9) for the NPC and PC at the given line; a zero line means the slot's saved line plus one. Ours: the run (Resume with the line from the conversation's return-to-script rule); the saved-line default is the conversation's. |
| 56EA10 | partial | `Arcanum.World.AreaCharacterSheetTexts.SchematicName` | Schematic name of a degree: the learned schematic number (discipline, degree) then the record's name text. Ours matches for degrees 1..7; a missing record yields null where the original shows the missing-line text, and no degree range check is made in the original. |
| 5736E0 | partial | `Arcanum.World.AreaHeldCursor.Compose` | If an item is held, redraws the cursor as the item's inventory art with the prior cursor art over it and the hot point at (2,2). Left out: nothing of the held state itself, which the viewer owns; the cursor-stack calls are Godot's. |
| 573850 | partial | `Arcanum.World.AreaInventoryDrag.Release` | The inventory window's mouse and button handler: press and release on grid and wear slots, right release use or move, drop and use icons, round view close, arrange, doll toggles, other party's grid and scroll, barter, steal, hover text. Ours has the own window's hit, press and release (grid move, wear, take-off, put back, icons) and the loot grid release; the rest is the viewer's. |
| 575200 | partial | `Arcanum.World.AreaBarter.Buy` | Release of a partner item on the player's grid: in barter mode (1) for a non-follower it needs the PC's coins to reach the stored unit quote, else prints the not-enough-money line, puts the item back; otherwise it moves the item to the hero. A quote of 0 cancels. Other modes just move the item. Left out: the loot take path and the grid location choice. |
| 575CB0 | partial | `Arcanum.World.AreaInventoryDrag.HitTest` | Finds the location under a point of the window for the mode: the nine wear-slot rectangles (1000..1008) with y lowered by 41, the player's grid of 10 by 12 cells, the quick slots (2000..2009), the other party's grid with scroll in modes 2-4, and the second doll table. Ours: own-window slots, grid and icons; loot grid in AreaLootArtLayout.HitTest; no quick slots or second table. |
| 575FA0 | partial | `Arcanum.World.AreaInventoryDrag.Press` | Uses the hit test to find the item at a window point (worn, quick slot, grid of PC or other party) and its parent; an item with object flag 0x2 or pick flag 0x10 is skipped (none returned). Ours: Press looks up worn slot or grid item for the own window; the flag skips and the other party's grid with scroll are not in it. |
| 579FA0 | partial | `Arcanum.Session.GameSessionOwner.ToggleHeroConcealment` | Does nothing when the hero, or the target (if another critter), is dead or unconscious; otherwise stores the skill-use source, target and button index, closes a pending skill cursor, and applies locally through 0x57A0A0 (in multiplayer sends a request first). Ours carries the dead or unconscious refusal and the prowling toggle; left out: other buttons' skill cursor entry, the network apply test and the pending-use globals. |
| 57A0A0 | partial | `Arcanum.Session.GameSessionOwner.ToggleHeroConcealment` | By button index: 0 toggles concealment (opposite of the hero's state, 0x45EE30); 1, 2 and 3 open the skill cursor (inventory repair window for 3) with the table's target mask; an index above 3 logs an error. Ours implements only the prowling toggle (ToggleHeroConcealment); the skill cursor entries, repair window open and spell picker follow-up are not in the world assemblies. |
| 57A1F0 | partial | `Arcanum.Session.GameSessionOwner.RepairWithSkill` | After a skill-bar skill got its target, refuses when the local PC or the stored performer is unconscious, else starts the skill on the target through the skill-bar start (0x57A320); in a network game a client sends a packet instead. Ours: only the repair start refuses an unconscious or dead hero; the other skills' starts and the packet are not made. |
| 57BB60 | partial | `Arcanum.Session.GameSessionOwner.CaptureSpells` | Writes the five maintain bar slots (a spell id and an active flag each) to the save file. Ours saves the bar's slots with their runs in the spells section (schema 73) rather than spell id and flag, and only for runs a save keeps. |
| 57BBB0 | partial | `Arcanum.Session.GameSessionOwner.ImportSpells` | Reads the five slots from the save and, for each slot flagged active, re-adds the spell to the bar with its icon. Ours restores the bar from the saved slots only while their runs exist (a different save form); the icons are the viewer's. |
| 57CC70 | partial | `Arcanum.World.AreaConversationHost.GenericGreeting` | The talk callback: runs the dialog script event on the target; when it runs default, a surrendered NPC whose danger source is in the talker's party is made to flee, else the generic greeting floats. Ours runs the script and floats the greeting but never makes a surrendered NPC flee. |
| 57CE30 | partial | `Arcanum.Session.SessionItemTriggers.CastSlotZero` | Item use callback: nothing when the slot-0 spell has item triggers; else the activation (0x57BC70), the cast at the target when it answered 0 (0x57C110), then the cancel (0x57BFF0). Ours does the activation and the cast; the cancel is the viewer's. |
| 57DB40 | partial | `Arcanum.Session.GameSessionSnapshotCodec.WriteHotkeys` | Writes the two recent actions and the ten entries to the save, each entry through 0x57DE10. Ours writes the entries and recent actions in the hotkeys section (schema 53) with kind, item and index; the item count is recomputed on load. |
| 57DBA0 | partial | `Arcanum.Session.GameSessionOwner.ImportHotkeys` | Reads the two recent actions through 0x57EFA0 (which swaps their order, an original slip) and the ten entries through 0x57DEF0. Ours restores the bar as saved, checks that its items are held and refuses the whole load otherwise, and keeps the recent order. |
| 57DE10 | partial | `Arcanum.Session.GameSessionSnapshotCodec.WriteHotkeys` | Writes one entry: its type and slot and by type the item id and count, the skill, the spell, or the item and spell. Ours writes kind, item and index only. |
| 57DEF0 | partial | `Arcanum.Session.GameSessionOwner.ImportHotkeys` | Reads one entry and by type rebuilds its object, icon and count; an item that no longer resolves empties it, an item-and-spell entry's failure fails the load. Ours validates the held item and the entry kind as a whole and does not rebuild icons. |
| 57ED60 | partial | `Arcanum.Session.GameSessionOwner.AssignHotkey` | Sets the target entry from a dragged spell or skill-bar button (type and index), clears its drag flag and redraws it. Ours assigns the entry in the session; the redraw is the viewer's. |
| 57EE30 | partial | `Arcanum.Session.GameSessionOwner.AssignHotkey` | Puts an item on a hotkey location: an item already on the entry goes back to its holder (dropped on the owner's tile when it cannot go), then the new item moves to the entry's location and the entry is set. Ours does this in the drop, putting the old item on the first free grid place. |
| 57EED0 | partial | `Arcanum.Session.GameSessionOwner.AssignHotkey` | Returns any old item of the entry to its holder, then makes the entry a type-1 item entry with its icon and the held count of the prototype. Ours sets the entry in the drop; the count is queried on demand. |
| 57F210 | partial | `Arcanum.Session.GameSessionOwner.ClearHotkey` | Empties one hotkey entry and redraws it. Ours empties an entry in the session; the redraw is the viewer's. |

A method that carries more than one mark does one part of each function: `AreaCharacterSheet.Build` (basic info and secondary stats), `ToggleHeroConcealment` (the skill bar press and its preprocess), `AssignHotkey` (the drop, the item place and the entry set), `WriteHotkeys` and `ImportHotkeys` (the bar and one entry), `EndReaction` (the dialog end callback and the silent slot close) and `GenericGreeting` (the greeting shown and the talk request default).

### Read and not implemented

One row each in the registry (the full reading is its reason column); the first sentence follows here. The addresses are grouped by domain.

**travel** (32)

| Address | Function | Reading |
| --- | --- | --- |
| 5581E0 | random encounter table load | Module load of the random encounter tables from Rules\WMap_Rnd.mes: three tables (lines 10000+, 20000+, 30000+) and a row set; |
| 5589E0 | encounter terrain pair test | A terrain pair (two terrain words of a world map cell) qualifies for an encounter only when both are known terrains below 19 with a nonzero entry in the table at 0x5C79C4; |
| 558B80 | random encounter chart load | Loads the world-map random encounter tables from the rules message file: counts the entries, warns when the chart is empty, parses each row (three position/radius numbers, a value read as a named list or a plain integer, then up to five |
| 558D40 | encounter monster slots parse | Parses the five monster slots of an encounter table row: for each slot name it reads the prototype number and a min-max group size range, storing the prototype in the row's slot array and the range as two shorts; |
| 558DE0 | random encounter roll | The world-map roll for a location: skipped under the no-random switch or without loaded tables; |
| 558F30 | encounter area table lookup | Walks a table of circular regions (centre, radius, value) and returns the value of the first whose distance from the given location is within the radius, else reports no match. |
| 558FB0 | encounter terrain value | Reads the terrain word of a location and maps its terrain-base field (0..18) through a 19-entry table to the encounter terrain value; |
| 559010 | encounter table pick | Chooses the encounter table (override value, else six times the terrain value plus twice the density plus the night flag, less 8), rejects an out-of-range table, sums the weights of the rows the PC is eligible for, rolls 1..sum and walks |
| 559150 | encounter row eligibility | A table row is eligible when the PC's level (stat 17) lies within the row's min and max level, its required global flag (if any) is set, and its use count is still below its cap (when the cap is not -1). |
| 5591B0 | encounter table weight sum | Adds up the weights of the rows of one table that pass the eligibility test; zero means no encounter can fire from that table. No counterpart in Steamwoven. |
| 559200 | encounter group size roll | For each of the five monster slots with a prototype, rolls a group size between the slot's min and max (a fixed size when equal), stores it and returns the total; |
| 559260 | encounter creature spawn | Creates the rolled monsters around the roll location: a 1..100 roll under 51 picks one of two ring orientations; |
| 5594E0 | encounter spawn offset step | Gives the tile offset of the n-th creature of an encounter group: the first goes one step along one axis, later ones step outward in rings of three (n minus 1 divided by 3 plus 1 on one axis, n modulo 3 on the other). |
| 5595B0 | random encounter hook | The handler of the Random Encounters event (kind 34): re-arms the next event first (0x559640), then acts only on the outdoor map slot 1 when the PC is in no named area; |
| 559640 | random encounter scheduler | Queues the next Random Encounters event (kind 34) with a delay of random(300, 700) minutes of game time (times 60,000 ms), via the clock's add-event call; |
| 560720 | random encounter map lockout start | Sets the encounter flag that makes the world-map open refuse with message 602 and posts a real-time UI event (action 13, 4000 ms) that later clears it. |
| 5607E0 | world map open gate | Toggles the window shut when open. Silent refusal without a living, conscious PC. |
| 560AA0 | world map module data load | Loads WorldMap.mes of the module once per open (cached name): longitude/latitude offsets from entry 20, tile columns/rows/base name/ZoomedName/MapKeyedTo from entry 50 plus the keyed map; |
| 560EF0 | world map walkable state | Non-scripted open: stores the town map of the PC's sector and sets walkable = current map is the keyed outdoor map and that sector has no town map. Ours: viewer decides (PcTownMapNumber); nearest none in the assemblies. |
| 560F40 | world map close | Closing: a PC hidden by travel (field 0x13 bit 2) is shown again and may attack again, mode 0 is restored and the destroy at 0x561520 runs (notes saved, travel state 0, art freed, module cache cleared). |
| 560FA0 | world map window build | Finds where the party is (PC location on the start map, else the area's location; |
| 5614F0 | world map off-start-map test and close | Predicate: map view (kind 0) while the current map is not the start map, used for the readouts' 2000 width. |
| 562800 | known area marker add | Adds an area marker (kind 5) at the area's world-map pixel with the area's name (up to 250 characters) to the view's list; called for each known area at build. Ours: viewer draws its own markers; nearest none in the assemblies. |
| 562880 | world map coordinate readouts | On pointer move draws two readouts: W = map width in sectors (2000 off the start map) minus (pixel x + longitude offset), and S = pixel y + latitude offset, as '%d W' and '%d S' texts into the boxes. |
| 562A20 | world map compass scroll | With the compass pressed, a pointer outside the dead zone scrolls the view by steps toward the pointer on each axis; sets the scroll flag. Ours: viewer does its own scrolling; nearest none. |
| 562AF0 | world map edge scroll on drag | While dragging, a pointer within 23 px of a view edge scrolls the view 50 px that way (speed 10) along each axis. Ours: viewer scroll only; nearest none. |
| 562B70 | world map view kind switch | Leaves tool states then switches among map (0), continent (1) and town map (2): town only when the PC's sector has one (else stays), continent only when a zoomed picture exists (else message 605); |
| 562D80 | town map info load | Zeroes the town waypoint count, reads the town info of the PC's sector (failure clears it and the caller falls back to the map view), sets the party pixel, name, tile columns/rows and allocates the tile table, loads tile 0, sets the scroll |
| 564780 | waypoint click hit test | Walks the current list from the last waypoint down and returns the first whose stored picture pixel lies within 9 px (half the marker art plus 5) of the clicked pixel; |
| 564E30 | travel position snap probe | Turns the map position pixel into its sector-centre location (64 x (W - x) + 32, 64 x y + 32), then looks up the nearest known area within 320 tiles and moves to it only if the PC does not know it; |
| 565140 | travel start check | Before a travel segment starts it asks the party to interrupt its running actions at priority 3 (0x424070); |
| 566CC0 | party map position location | Gives the location for the world: during world-map travel the map position's location, otherwise the PC's own location; |

**items** (26)

| Address | Function | Reading |
| --- | --- | --- |
| 550000 | hotkey item wear swap | Hotkey wear of an item into a slot: reads the item worn there, runs the wear check, takes the old item off first, wears the new one, puts the old one back if the wear is refused, and plays the item sound. |
| 556220 | weapon stat lines | Writes a weapon's lines in the inventory window: displayed damage rating, the weapon skill's effectiveness as a percent (basic or tech skill), and five damage-type ranges as min-max or 0 (0x465F70), for the item in the hero's wielded slot |
| 5566B0 | armour stat lines | Writes the armour lines of the inventory window: displayed armour rating, armour class (0x43D880) and five resistance percents (0x43D6D0) or 0. No counterpart in the allowed assemblies; nearest the viewer's item window lines. |
| 56BC00 | reading window open | Open of a written document: when the window is already open it closes instead (toggle); |
| 56C800 | reading window text fit | Word-wraps a string into a rectangle by pixel width and reports how many characters fit, which sets where a book page or newspaper column breaks. |
| 56CAA0 | reading window page fill | Fills one book page rectangle through the column fill and stores the message and offset where the page starts. Ours paginates by a character count in AreaReading.Pages, not by the rectangle. |
| 56CB60 | reading window column fill | Fills a text column with the messages from the current line on, wrapped by pixel width, stopping at ten messages or a full column. Ours has no column fill; the newspaper filler columns are cut differently. |
| 56CCA0 | newspaper filler text fetch | Reads the message of the given number from the newspaper file, runs the markup parse, copies the text into the caller's buffer and turns every newline into a space; |
| 5719F0 | item cursor target mask | Chooses the 64-bit target mask of the item cursor: an item with spell charges (not 0) or the magical flag takes its slot-0 spell's BEGIN mask; |
| 571AA0 | item cursor open | Begins the item target cursor: refuses for a null item, an OFF, dead or unconscious user, cancels an open item cursor, takes the mask; |
| 571C80 | item cursor cancel | Returns the interface to the mode below, clears the stored item and sets the cursor art global back to 21 (spell cursor). Ours: none. |
| 571CB0 | item cursor apply | Applies the picked target: dead or unconscious PC does nothing; |
| 572240 | inventory window open | The open entry: an open window closes instead (0x572340); a network client sends a request; |
| 572340 | inventory window toggle close | If the window is open, puts a held item back and closes it (the I key closes the window); answers 1 when it closed. Ours: the I key is ignored while another window owns input; no toggle. |
| 572510 | inventory window mode select | Returns the interface to mode 0, then selects the interface mode of the window mode (0 own: 8, 1 barter: 4, 2 and 4 loot: 10, 3: 11, 5: 0x13, 6: 0x14); refuses with 0 when holder and partner are the same object (except mode 0). Ours: none. |
| 5727B0 | inventory window setup | Builds the window: stores holder, partner and mode, creates the grids and buttons, in barter mode swaps the partner for the merchant's substitute container, opens the container, arranges and identifies, closes the window if the partner |
| 573590 | inventory window close for object | Closes the inventory window when the given object is its holder, shown object or partner (called from the interface's object-removed hook 0x57CAF0). Ours: none. |
| 575770 | held item put back | Puts the held item back when the drag ends without a move: for a quick slot or an item no longer in its holder it plays the sound, inserts it where it was; |
| 575930 | held item restore | Puts the held item back where it came from when it is no longer in its list: into its worn slot or quick slot, or a grid cell that fits (checked with the fit test), else the first place that passes the insert check, else drops it on the |
| 575BE0 | held item drop on tile | When the held item fits nowhere, places it as a drop at the holder's tile with scatter 2 (0x466E50) and plays the item sound. Ours: nothing; the viewer never loses the item. |
| 575C50 | held item release if match | If the given item is the held one, clears its pick flag and the held state, refreshes the cursor and redraws the window; else nothing. Used when the item is destroyed or moved elsewhere. Ours: none. |
| 5761D0 | bad inventory repair | Repairs a holder's item list while redrawing: removes duplicate handles, then for each item whose inventory art is missing removes it, re-arranges the list and re-inserts it (insert check, pickup transfer), with warning lines. |
| 5764B0 | bad item removal check | Called for an item with bad art: if its holder is missing or differs from the expected one it logs a warning and removes it from the list (0x467860), returning 1; else 0. Ours: none. |
| 578330 | barter hover quote text | Hover quote in a barter window: none in a follower's window; else the item price (0 writes a refusal phrase) compared with the partner's coins, the item script's check phrase, the price phrase; |
| 5786C0 | identify hover quote text | In the identify mode, for the hovered item: an already identified item writes the 'already identified' phrase; |
| 579770 | take all button handler | Records the shown object as the move source, copies its whole inventory list and loops it: skips an item with object flag 0x2 or item flag 0x10 (held), skips one the hero's insert check refuses, and moves each other with flag 0x21 to the |

**ui** (20)

| Address | Function | Reading |
| --- | --- | --- |
| 551F80 | pointer pick filter set | Sets which objects the pointer can pick, only when the top interface mode is 0: a hero not in combat mode gives flags 4 and 0x8200; |
| 552770 | message log post | Posts a message: types 6-12 are shown only after 3 s since the last stored one and never stored; others go to a ring of ten, with the equal-text check for types 4 and 5. Ours: the viewer's message log, outside the allowed assemblies. |
| 5529C0 | message window entry draw | Composes the text of each message type 0-13: level-up lines, skill and spell hints (cost line '%s: %d (%d / %s)' with fatigue and maintenance), college, discipline, degree and schematic texts. |
| 553990 | cursor art refresh | Does nothing while an item is held for a drag. Otherwise picks the cursor art from the interface mode on top of the mode stack: targeting mode the spell cursor, skill mode the skill cursor, game mode none, any other mode the gauntlet; |
| 553A70 | world hover pick | Each mouse move over the play view: asks if the pointer is over the view, picks the object under the pointer (0x4F2CB0), and when it differs from the hovered object makes it the hovered object and prints its description (0x57CCF0); |
| 557670 | locked refusal message | Prints interface message 2000 in the message window; called for a hovered type 15 or 16 object whose field 0xED is 0 (an object that cannot be used). No counterpart; the viewer shows no such line. |
| 557730 | alt click refusal message | Callback of the message entry hook: prints entry 3000 + n of the interface message file when it exists. |
| 563E00 | map marker hit test | Walks the view's markers from the newest, skips those whose type is hidden, and picks the first whose picture pixel lies within a 19 x 19 box (9 px each way; |
| 564160 | town note add | Adds a note record to the current view's list: refused when the array is missing or 199 notes are held; |
| 564AF0 | scenery marks town map | Called from the scenery description for a scenery flagged as a town map marker: when the object's sector and the PC's sector share the same town map and no note lies within 9 px, adds a question note (type 3, flags 2) named after the |
| 566130 | town map redraw | Redraws the town map: tiles show plainly when their cell is in the town's reveal bitmap, an unrevealed cell next to a revealed one fades from black, other cells stay black, then notes, route, party and each follower marker. |
| 5671E0 | text edit character filter | Tests a typed character against the active text field's descriptor flags: bit 1 refuses the file-name characters (double quote, apostrophe, asterisk, slash, colon, less, greater, question mark, backslash, bar), bit 2 refuses letters; |
| 569600 | slide show | Runs the slide show (type 1 death, 2 credits, other end game): clears the queue, runs the slide script, for a death with an empty queue queues slide 0, then plays the queued slides in order over music, stopping when one returns 0. |
| 569930 | slide picture and voice lookup | Reads a slide message from slide.mes, splits it at space, tab and line feed: the first word is the picture, the second the voice path; |
| 576520 | inventory window redraw | Redraws the inventory, barter or loot window by mode: total weight line, encumbrance level name with its bracketed carry limit, displayed damage and armour ratings, gold and four ammunition counts, item icons per grid cell, a red tint on |
| 57EDF0 | hotkey item place hook | Hook slot 0x5E86C0: for the local PC places an item on a hotkey entry through 0x57EE30, for another holder only moves it to the entry's location. Ours has no such use-time placement (nearest: GameSessionOwner.AssignHotkey). |
| 589280 | options window page build | Builds one options page: eight slots from the control table, label from message 100 x (page+1) + slot, value and enabled flag from each getter, cyclic control created and set. |
| 589430 | options module apply | Done on page 0 loads the chosen module and resets the interface; on failure logs a can not load module line and the window stays. Ours has no module choice (hidden by owner), nearest method none. |
| 589610 | default combat mode getter | Gives the options value of Default Combat Mode: 0 real time, 1 turn-based, 2 fast turn-based from the two settings; |
| 5896A0 | default combat mode setter | Writes the two settings "turn-based" and "fast turn-based" from the chosen mode (0 both off, 1 turn-based only, 2 both on); |

**skills** (13)

| Address | Function | Reading |
| --- | --- | --- |
| 579840 | gambling check | Gamble precondition for a barter item: the partner needs gambling points (skill 8), the item price must not exceed the partner's limit table value (0x4C6520) nor the hero's gold, a worn item needs partner gambling training 2 and a no-sell |
| 579B60 | gambling run | After the gambling check passes, runs a skill check of skill 8 (gambling, with the stored roll) between the hero, the partner and the item; |
| 57A620 | follower skill hand-over | Unless a Ctrl key is held, asks the follower picker for the best actor of the skill on this target; |
| 57A710 | pick pocket window open | Opens the inventory window on a target for the skill bar: nothing when the target is the performer; |
| 57A770 | skill result line float | Floats skill_ui line 500 (success) or 501 (failure) over the skill user, the argument choosing the line and a failure flag for the floater. Ours floats no result line for repair or pick pocket (PickPocketRules holds only the line numbers). |
| 57A7F0 | skill result line with icon | Like the plain result float (500 success, 501 failure) but the text goes to the message window for the local PC or gets the object's slot icon for another user; |
| 57A8C0 | skill result line with icon twin | A twin of 0x57A7F0: lines 500 and 501, message window or slot icon, floater, and the client send when the server bit is set. Ours floats no such line. |
| 57A990 | skill result line float copy | A second copy of the plain 500 (success) or 501 (failure) result float over the skill user. Ours floats no result line. |
| 57AA10 | skill result line 560 float | Floats skill_ui line 560 on success or 561 on failure over the skill user with a failure flag for the floater. Ours has no counterpart. |
| 57AA90 | skill result line float third copy | A third copy of the plain 500 (success) or 501 (failure) result float over the skill user. Ours floats no result line. |
| 57AB10 | nothing to repair line float | Floats skill_ui line 505 (nothing to repair) over the user as a failure floater. Ours reports the status NothingToRepair (GameSessionOwner.RepairWithSkill) but floats no text. |
| 57AB70 | skill result line 550 float | Floats skill_ui line 550 on success or 551 on failure over the skill user. Ours has no counterpart. |
| 57ABF0 | skill result line 512 float | Floats skill_ui line 512 over the skill user as a failure floater, the same text every time. Ours has no counterpart. |

**interface** (12)

| Address | Function | Reading |
| --- | --- | --- |
| 55EC90 | alignment and aptitude draw | Prints alignment (stat 19) divided by ten, truncated, and picks the alignment bar frame (value/10 + 10); |
| 573630 | held item cursor set | Takes an item handle, stores it as the held item of the inventory drag with its holder and inventory location, then draws the cursor as the item's inventory art with the previous cursor over it and a hot point at (2,2). |
| 573740 | item lift to cursor | Lifts the given item onto the cursor: puts it in the held slot, and outside network play moves it within its holder (flag 4, or 0x24 when the caller asks) before the cursor art is set to the item's inventory art with the old cursor over it. |
| 574FD0 | barter follower step | The barter window's previous or next follower button: walks the leader's follower list from the shown follower until one that passes the can-be-shown test (0x575080) is found, then closes the window and reopens it on that follower with the |
| 575080 | follower window eligibility test | Decides whether a follower may be shown in the barter window: yes if it is already the shown one in the open window; |
| 5755A0 | item pick callback | Runs the pick: stores the held item, sets the item's no-display pick flag (0x10), computes the item art's offset from the grabbed grid cell (cell column and row times 32 in the window's layout, per mode), sets the cursor art and plays the |
| 57BFF0 | item spell use cancel | After an item's spell activation: calls 0x500570 when the cursor global 0x68350C is 0, puts the interface in mode 0 and resets the targeting globals when 0x683508 is 0, so an NPC's item use closes a player's open targeting cursor too. |
| 57CB10 | steal window hook | Pick-pocket window hook: nothing for an object whose flag 0x10000 is set; |
| 57CC10 | fatigue change refresh | Fatigue bar hook: for the local PC queues a 50 ms interface event and, when the PC is no longer active, puts the held cursor item back; then redraws the hover text and the bar. Ours leaves the held item alone (nearest: none). |
| 57CCF0 | hover description show | Hover hook: asks the name builder (0x57CD60) for an object's hover name and, when it answers, builds the item description (0x553BE0) and shows it in the hover window. Ours has no counterpart (nearest: none). |
| 57CD60 | hover name by script | Builds an object's hover name: nothing unless the object passes the PC test; runs the object's script with an all-zero event and, when that does not stop, builds the display name. Ours has no hover name through scripts (nearest: none). |
| 57D410 | interface timed event handler | Interface time event dispatcher: death (10) resets the game to the main menu when the PC is still dead, after closing the inventory and re-queuing while the menu is open; |

**magic** (8)

| Address | Function | Reading |
| --- | --- | --- |
| 553670 | maintain slot enable or disable | Enables or disables one maintained-spell slot button on the top strip and in its 32 by 32 copy. |
| 556EA0 | weapon spell button press | A press of the bar's weapon button: stores the hero's wielded weapon (slot 1004), or the given item, as the pane's item and requests bar mode 8; |
| 557C00 | item pane button hit test | In bar mode 8 with a pane item returns the index of the item spell button under the pointer; |
| 557CF0 | spell pane button hit test | In bar mode 1 returns the spell button index under the pointer on the current page only when the spell is known (0x4B1950), else 5; 6 when hidden. Drag source test of the spell pane; no counterpart in src. |
| 55DC60 | spell college page handler | Spell page button handler: hover ids (college tab 3000 + college, spell 2000 + spell); |
| 57F2C0 | hotkey recent action item removal | Part of the inventory removal callback: each of the two recent actions whose object is the removed item is replaced by the default skill button of its position. |
| 57F340 | hotkey spell removal clear | Called when a spell is removed from the book (unlearn): a recent action naming that spell is replaced by the default skill button, and every spell entry of the ten hotkeys with that spell is emptied and redrawn. |
| 57F3C0 | hotkey bar load recovery | After a load, re-resolves every item and item-and-spell entry and the recent actions by object id: a recent action whose item is gone becomes the default skill button, an entry whose item is gone is logged and emptied; |

**time** (6)

| Address | Function | Reading |
| --- | --- | --- |
| 57B180 | rest window open decision | The rest hook (bed or wait): refuses for a dead PC or in a network game, closes an open window, refuses an unconscious PC (line 11), waiting where the town map does not allow it (20), combat that cannot be left (9), a bed script that |
| 57B450 | rest window close | Closes an open rest window: removes the pending rest events (kind 0xF), hides the window, resumes ambient lighting, takes the sleeper out of the bed when there was one and clears the rest globals. Ours has no rest. |
| 57B710 | rest option click | A click on one of the 7 or 8 rest options: removes the pending rest events, takes the option's length from a table (hours, or the special until-morning, until-evening, until-healed values), hides the buttons, runs the followers' sleeping |
| 57B7F0 | rest step | Every 200 ms step jumps game time by one hour; when resting (not waiting) heals the sleeper and followers each hour; |
| 57B9E0 | rest overlay start | On rest start (once): begins a black screen overlay effect and switches the ambient hourly light apply off while hours pass. Ours has no rest. |
| 57BA70 | rest overlay end | On rest end: switches the ambient light apply back on (it applies the current hour at once), ends the overlay effect and closes the rest window. Ours has no rest. |

**advancement** (5)

| Address | Function | Reading |
| --- | --- | --- |
| 55D060 | auto-level scheme page build | Builds the character sheet's fourth page: scans scheme numbers 0..199 and keeps those whose level-name text exists (up to 200), creates the 15 row hover regions and the page art. |
| 55D210 | auto-level scheme page refresh | Redraws the scheme page: the shown critter's current scheme name (read from its stored scheme number, or for a flagged player character from its character-file extra text) and up to 15 scheme names from the scroll offset. |
| 55D6F0 | tech skill buttons handler | Tech skill page button handler: hover sets the hint id; + buys a point (or sends a network packet); |
| 55D940 | tech degree page handler | Tech page button handler: hover sets hint ids (degree rows 5000 + 8 d + k, discipline tabs 4000 + d); |
| 55DF90 | auto-level scheme page handler | Scheme page button handler: hover over a row shows its name; a click on row k writes scheme list[offset + k] through the scheme setter (0x4A6D90), saves it to the player file in multiplayer and redraws; |

**dialogue** (5)

| Address | Function | Reading |
| --- | --- | --- |
| 568430 | float line speech callback | Callback of the float line action: gives the speech display (0x568540) style 2 for a PC (type 15) and style 0 for any other object. Ours floats the text with one floater style; no PC/NPC style split, nearest none. |
| 5684C0 | dialog reply show | Shows the NPC reply text of a slot through the speech display with style -2, and for the local PC clears the dialog text and adds each option row (count at +0x464, 1000 bytes apart). |
| 568540 | speech display | Shows a spoken line: in a network host sends a packet; if a listener is given and the speaker is alive and runs no animation it interrupts the speaker's runs (level 3) and turns it to face the listener; |
| 5686C0 | speech display without voice | Variant of the speech display: same network packet, same turn of the speaker toward the listener when alive and idle, but no voice start, and the bubble is not set when the speaker is the local PC. Ours: none (text floats; no turn). |
| 568830 | dialog forced close | Called when an object goes away: ends (0x5678D0, argument 0) every one of the eight dialog slots in which that object is the PC or the NPC. Ours: the viewer closes a talk itself when its partner goes; no slot walk. Nearest EndReaction. |

**party** (5)

| Address | Function | Reading |
| --- | --- | --- |
| 56B110 | follower order target pick start | Starts the target pick of the walk and attack orders: pushes pick mode 18 and sets the pick filter flags, one set for walk (argument 0) and different sets for attack, switched by either Alt key being held. |
| 56B180 | follower order target text | After a target is picked for a portrait order (walk or attack), returns at once when the order's speaker is dead or unconscious; |
| 56B6F0 | follower portrait list refresh | Rebuilds the portrait list from the direct followers array (field 241, array order, so a waiting NPC has none), grows the slot buffer, clamps the scroll offset to count minus 6 (0 when 6 or fewer), enables one portrait button per follower |
| 56B970 | follower menu row states | Draws the eight rows of the follower order menu: Wait is greyed when the follower is mind-controlled (flag 0x8000000 of its field 0x14), Inventory is greyed when the can-open test 0x575080 refuses, the highlighted row has its own colour. |
| 571950 | typed order submit | On Enter in the order text entry, a non-empty text is sent as a broadcast order (0x4C2F00, the followers' text order) with the tile under the pointer, then the entry closes. |

**combat** (4)

| Address | Function | Reading |
| --- | --- | --- |
| 557370 | action point cost preview | In turn-based mode over a hovered object: picks which cost to show by cursor and object type (use object, attack, pick up, take from container, use an item) with tests on activity, flags, being dead and the leader; |
| 56EFA0 | combat action point bar | On the local PC's subturn draws the points left, the action's cost and the shortfall as green, orange and red shares on a 25-point scale (4.8 px a point) on the hidden-interface bar. |
| 56F430 | combat action point lamps | Given the action points A and the last check's cost P, lights up to 20 lamps in four groups of five: A minus P lamps of the left art, then min(A, P) of the cost art, then the shortfall of the third art; |
| 56F9B0 | combat end turn event | The end-turn button and the E key released (no text focus): when the current participant is the local PC and it runs no animation, or only its goal 2 animation, starts the next subturn at once, without zeroing its action points; |

**technology** (4)

| Address | Function | Reading |
| --- | --- | --- |
| 56D530 | schematic window event handler | Window clicks: a discipline tab sets the discipline and page 0; |
| 56E950 | schematic completion callback | Completion callback of an assembly: acts only when the viewer is the local PC: shows schematic_text message 2, redraws the page and plays sound 3018 plus the current discipline. |
| 56E9D0 | schematic text by record | Reads the schematic record of a number and fetches the text from schematic_text.mes for it, returning the string. Ours: AreaCharacterSheetTexts.SchematicName and SchematicCatalog.Text read the same texts; no method carries this address. |
| 56EA30 | schematic components names | Gets the learned schematic number and its record, then the display names of its two component prototypes for the character sheet's component line "[a]+[b]". Ours: no assembly method builds the line. |

**lighting** (3)

| Address | Function | Reading |
| --- | --- | --- |
| 57D310 | ambient light event rearm | Removes the pending ambient light events (kind 13) and queues a new one 3,600,000 ms ahead, i.e. every game hour. Ours has no hourly ambient event (nearest: none). |
| 57D620 | ambient light enable | Sets the ambient light apply flag when it was off and at once applies the current game hour to the lighting (0x57D6C0). Ours has no hourly apply (nearest: none). |
| 57D660 | ambient hour event | Time event every game hour: applies the current game hour to the light when the ambient flag is on, then queues the next event 3,600,000 ms ahead. Ours has no hourly ambient application (nearest: none). |

**scripts** (3)

| Address | Function | Reading |
| --- | --- | --- |
| 569720 | slide script run | Runs the slide script for the show type (997 end game, 998 death, 999 credits) through the script entry at the use point; the script may queue up to 100 slides. Ours runs no slide script (the installed data has none for 998), nearest none. |
| 57BAC0 | leader sleeping event | Fires script event 29 (leader sleeping) at every direct follower of the sleeper when a rest starts. Ours has no rest and no such event source. |
| 57CDE0 | end of game trigger | Slot 60 callback, called by script action 0x447220 and 0x4EFCD0: queues the interface time event 11 after 50 ms, whose handler ends the game and resets to the main menu. Ours has no end-game screen (nearest: none). |

**fate** (2)

| Address | Function | Reading |
| --- | --- | --- |
| 56FE70 | fate window event handler | Press of a fate button arms that fate kind (0x4F50C0) and pops the button back when refused; |
| 57CDC0 | player death delay | Player death hook: queues the interface time event 10 for 5000 ms of real time, whose handler resets the game after the death. Ours starts its own wait in the viewer when the hero dies (nearest: none). |

**persistence** (2)

| Address | Function | Reading |
| --- | --- | --- |
| 558730 | random encounter save write | Writes, into data2.sav, a count and pairs (row line id, trigger count) of every encounter table row; nothing when encounters are disabled. No random encounters in src, so no counterpart (ledger PS16). |
| 558880 | random encounter save read | Reads the count and pairs of (row line id, trigger count) back and matches them to the rows of the loaded tables by line id; error line on a short read. No counterpart (ledger PS16). |

**navigation** (1)

| Address | Function | Reading |
| --- | --- | --- |
| 57CE70 | sector enter town map | Called when the local PC enters a sector (a server also for a remote one): looks up the sector's town map data and updates the town map view and its notes. Ours has no town map update (nearest: none). |

**residency** (1)

| Address | Function | Reading |
| --- | --- | --- |
| 564F60 | sector entry area and town notes | On a local PC entering a sector or after a game load: reads the sector's town map, runs the per-step area discovery (0x564A70) at the sector's location, loads that town's notes, sends the notes notice and recomputes the walkable flag; |

### What the reading found that the earlier documents did not say

The triage changes no behaviour. Each item is a place where the original does something our code does not reproduce at all (or does differently), read from the executable and not seen in game; the steps to compare them are in the [owner re-check list](../../project/owner-recheck-list.md#arc-res-120--function-triage-of-the-exe-band-0x55-0x59-2026-11-11). None of them is a task yet.

- **H2-1 Random encounters are missing entirely.** The original queues a Random Encounters event 300 to 700 game minutes apart (0x559640, hook 0x5595B0); the hook acts only on the outdoor map when the party is in no named area, rolls a chance from a region table (0x558DE0: default 5 percent, times 5 while the rest window is open, night from 18:00 to 06:00, refused in a town-map sector), picks a creature table from terrain, density and night (0x559010, 0x559150, 0x5591B0, 0x559200; rows gated by the party's level range, a global flag and a use count, each monster slot rolling its own group size), creates the creatures on grid rings around the roll location (0x559260, 0x5594E0), and closes the world map or wakes a resting party. The chart is loaded from the rules message files (0x558B80, 0x558D40, 0x558F30, 0x558FB0); the position used is the world-map position while travelling, else the party's (0x566CC0); the map refuses to open for a few real seconds after an encounter (0x5607E0 family). Ours has no counterpart; the missing-systems plan lists it as E2.
- **H2-2 World-map travel.** While travelling the original hides the whole party, stops attacking and plays the map music, and shows the party again on leaving (0x5615D0); before every travel segment it asks the hero to interrupt running actions and drops maintained spells, and a refusal drops the waypoints (0x565140); the map opens from any map and, in combat, leaves combat mode first when it can (0x5607E0); the scripted open (teleportation spell) picks the nearest known area within 320 tiles, charges the caster and teleports (0x5618B0). Ours has the journey step and the landing only.
- **H2-3 Area discovery on walking.** The sector-entry hook runs the discovery whenever the hero enters a new sector or a game is loaded (0x564F60 with 0x564A70): the area at the sector's location becomes known, gets its map marker and is cleared as the last discovered. Ours discovers only on world-map travel steps, dialog and scripts, and clears any last-discovered area at every step where the original clears it only when it equals the area handled.
- **H2-4 Dialog start and speech.** The dialog start refuses a dead or unconscious hero, an NPC that cannot speak, a second dialog and a hero that cannot leave combat mode, and turns the NPC toward the hero (0x567460); the speech display makes a speaking idle object stop and face the listener (0x568540); a talk request to a surrendered NPC whose danger source is in the talker's party makes it flee instead of greeting (0x57CC70, the same-party test is always false in single player, so this is dormant); when an object goes away every dialog slot where it is hero or NPC ends (0x568830).
- **H2-5 Hotkey bar.** Unlearning a spell empties every hotkey entry that holds it (0x57F340); after a load an entry whose item is gone is emptied alone, where ours refuses the whole load (0x57F3C0, 0x57DBA0); the hotkey for armour or a weapon swaps the item worn in its slot, putting the old one back when the new one is refused (0x550000); removing an item also closes its spell pane and clears the two quick slots that name it (0x5570D0); a spell recovered from a load by the maintain bar re-adds only the slots flagged active (0x57BBB0).
- **H2-6 Inventory and barter window.** The original shows, beside the doll, the damage and armour numbers, a weight line and an encumbrance line "Name (limit)", gold and four ammunition counts, and tints the primary weapon red while strength is below its minimum (0x576520); a pointer resting on an item in barter writes a quote with the price, refusal phrases, and a haggle percentage (0x578330); in identify mode the quote is a fixed 100 (0x5786C0, the price getter is a constant); the gamble button needs the merchant's gambling points and a limit table (0x579840, 0x579B60); Take all moves every item of a body or container, worn items included (0x579770); a held item whose source is gone is put back or dropped on the holder's tile (0x575770, 0x575930, 0x575BE0); picking an item sets a no-display flag and plays a sound (0x5755A0); an object removed from under an open window closes it at once (0x573590); pressing I while the window is open closes it (0x572340); previous and next follower buttons step through the followers (0x574FD0, 0x575080).
- **H2-7 Item target cursor and typed orders.** An item that needs a target opens interface mode 0x10 with a mask from its spell, walks next to the target for charged or out-of-range items, takes the lockpick path unless Ctrl is held, and applies to self with Shift (0x571AA0, 0x571CB0, 0x5719F0, 0x571C80); Enter opens a text line that sends a broadcast order (followers whose name starts the text, or all with "#") with the tile under the pointer (0x571950).
- **H2-8 Skill bar.** Disarm from the skill bar goes to the follower with the best effectiveness minus difficulty unless Ctrl is held (0x57A620); the result lines (skill messages 500/501, 505, 512, 550/551, 560/561) float over the user (0x57A770 and eight copies); the pick filter flags depend on combat mode and Alt (0x551F80); an Alt-click pull of a far object prints a refusal (0x557730).
- **H2-9 Rest and wait window.** The original opens it from a bed or the wait command, refuses for an unconscious hero, combat that cannot be left, a refusing bed script or an occupied bed, steps one game hour per 200 ms, heals the sleeper and followers each hour, drops maintained spells, fires script event 29 at the followers and ends at the chosen length, 07:00, 20:00 or full life (0x57B180, 0x57B710, 0x57B7F0, 0x57BAC0). Ours has no rest, wait or hourly healing.
- **H2-10 Character sheet and editor.** The fourth sheet tab lists every named auto-level scheme (1..199, 15 rows) and a click sets the scheme of the shown character (0x55D060, 0x55D210, 0x55DF90); closing the sheet clears the hero's damage in creation mode and resumes auto-attack whenever its saved flag is set (0x55A150); the tech-skill minus press compares with the editor snapshot for equality (the known deviation CP2, 0x55D6F0).
- **H2-11 Time-driven interface events.** A dead hero resets the game to the main menu 5 s later (0x57D410); a script's end-of-game action queues the end game after 50 ms (0x57CDE0); the ambient light event re-applies the current hour every 3,600,000 ms of game time and is off during rest (0x57D620, 0x57D660, 0x57D310); entering a sector updates the town map and its notes (0x57CE70); the maintain bar ends spells when intelligence over four falls below the number maintained (0x553670).
- **H2-12 Options window.** Three pages of eight slots (game: module, difficulty, violence filter, combat mode, auto attack, auto switch weapons, always run, follower skills; video: brightness, text duration, floats, float speed, combat taunts; sound: three volumes) and a three-valued default combat mode with a fast turn-based value (0x589280, 0x589610, 0x5896A0); Done loads a module chosen on page 0 (0x589430). Ours shows part of them (ledger ui-audio OP2).
- **H2-13 Smaller differences.** The message log never stores hover hints (types 6-12) and merges repeats (0x552770); the book reading layout wraps by pixels where ours cuts at about 700 characters (0x56C800, 0x56CAA0, 0x56CB60); the follower menu greys Wait for a mind-controlled follower and Inventory when the can-open test refuses (0x56B970); turn-based hover shows the action-point cost under the pointer (0x557370); the slide show runs scripts 997 to 999 (0x569600, 0x569720, 0x569930); text fields filter file-name characters (0x5671E0); a float line over the hero uses style 2 (0x568430); the hover text of the spell hint line carries the maintenance cost (0x5529C0).

### Not checked

- **No second reading.** Every `not implemented` and `partial` row is one reader's reading (F1); a reader may have read a large function from its callers and the existing ledger rows rather than line by line (the inventory window mouse handler 0x573850, the multiplayer packet code). The not relevant rows of the C runtime were judged from the first instructions and the callers.
- **Names of not relevant rows are not stored.** The reason class and the address are the record; the names the readers chose for C runtime and lobby functions are guesses and were not kept.
- **Callees outside the band.** A row names a callee of another band (for example the dialog start 0x4132A0, the can-open test 0x575080 for the follower menu) without re-reading it; those have their own rows in other slices.
- **Marks not run against the game.** A mark ties the code to the row; it is not an equality test, and the oracle covers none of these functions (all of them need windows, the object table or the interface mode stack).

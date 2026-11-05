# Reading window runtime

Status: **Implemented bounded component (Data, Session, viewer).** Work item ARC-UI-049, 2026-10-21. Written items (type 13:
books, notes, newspapers, telegrams, pictures, schematic documents, plaques) are read through the original's use path: the item
use (0x462CC0) → the reading start (0x56BB60) → the reading window (0x56BC00 / 0x56BCC0). A schematic document registers through
the [schematic knowledge](schematic-knowledge-runtime.md) (TECH-3); every other kind opens the reading window. Script actions 64,
65 and 114 open the same window. Universal: any written prototype or instance on any map. No saved state is added (the snapshot
schema is unchanged).

Requirements: [FR-ITEM-010](../requirements/items-economy.md#fr-item-010) (written objects),
[FR-ITEM-017](../requirements/items-economy.md#fr-item-017) (a found document registers).

Every rule was read in the retail 1.0.7.4 `Arcanum.exe` ([ledger "Reading window"](../research/ledger/ui-audio.md#reading-window-arc-ui-049),
WR1–WR16, anomalies WRA1–WRA4 and data oddities WRD1–WRD3). Code:
[`WrittenTexts`](../../src/Arcanum.Data/Written/WrittenTexts.cs) (the texts and the document of each kind),
[`GameSessionReading`](../../src/Arcanum.Session/GameSessionReading.cs) (the use entry and the document query),
[`SessionWrittenFacts`](../../src/Arcanum.Session/GameSessionSchematics.cs) (the item's written fields, prototype, use script and
item-spell path), the SCR dispatcher's [`ScrReadingRequest`](../../src/Arcanum.Scripting/Scr/ScrDispatchTypes.cs) (admission set
S10), [`AreaReading`](../../tools/terrain-viewer/Core/AreaReading.cs) (the Godot-free view and paging),
[`AreaInventoryHost.WrittenFacts`](../../tools/terrain-viewer/Core/AreaInventoryHost.cs),
[`AreaReadingPanel`](../../tools/terrain-viewer/Godot/AreaReadingPanel.cs) (the diagnostic panel) and
[`TerrainViewerAreaReading`](../../tools/terrain-viewer/Godot/TerrainViewerAreaReading.cs) (opening, keys, sounds, script requests).
Tests: [`WrittenTextsTests`](../../tests/Arcanum.Data.Tests/Written/WrittenTextsTests.cs),
[`WrittenRealDataTests`](../../tests/Arcanum.Data.Tests/Written/WrittenRealDataTests.cs) (opt-in, `DAT_ROOT`),
[`WrittenUseStateTests`](../../tests/Arcanum.Session.Tests/WrittenUseStateTests.cs),
[`ScrReadingTests`](../../tests/Arcanum.Scripting.Tests/Scr/ScrReadingTests.cs),
[`AreaReadingTests`](../../tests/Arcanum.TerrainViewer.Tests/AreaReadingTests.cs), the PC-viewer filter in
[`AreaScriptHostTests`](../../tests/Arcanum.TerrainViewer.Tests/AreaScriptHostTests.cs) and
[`AreaUseScriptHostTests`](../../tests/Arcanum.TerrainViewer.Tests/AreaUseScriptHostTests.cs), the Godot self-test `ARC-UI-049 self-test PASS`
and the native smoke `ARC-UI-049 native-smoke reading PASS`.

## Texts (Data, WR1, WR3–WR11)

`WrittenTexts.Open` loads `mes\gamebook.mes`, `gamenote.mes`, `gamenewspaper.mes`, `gametelegram.mes` and `gameplaque.mes` through
the content stack in the module phase (each file refused separately, with a typed status), and counts the newspaper fillers as
the original does (0 without message 10, else the messages from 10 on, up to 999). `Read(subtype, startLine, alternateNewspaper)`
gives a `WrittenDocument`:

| Kind | Background art | Body | Header |
| --- | --- | --- | --- |
| book (0) | 477 | start line + up to nine more to the first gap, with the markup | — |
| note (1) | 489 | the same lines, each followed by a newline, in the 20,000-byte buffer; no markup | — |
| newspaper (2) | 487 | the headline (start line) and the next nine, with the markup | masthead 0, 1, 0, 2, 3, 2 (fonts 485, 484, 485, 482, 482, 482) |
| telegram (3) | 491 | each line followed by message 1012, in the same buffer; no markup | form texts 1000–1011 |
| picture (4) | the start line | — (the art is the start line) | — |
| schematic (5) | — | status `Schematic` (no window) | — |
| plaque (6) | 633 | the lines with the markup | — |

The frame art is 494. Prototype 14092 (the executable's 14072) sets the alternate art 817 for every kind but a picture, and for a
newspaper the masthead 0, 4, 0, 2, 5, 2 with the second font 815. `NewspaperFiller(start, count)` gives the filler messages after the
body (from 10 + start / 10, wrapping to 10 at 10 + the filler count, a gap stopping it). `Markup` reads a line's font (`atoi`), skips
the digits, drops the next character (centred when it is `c` or `C`) and one whitespace character more. The item's last text line and
its written flags are never read (WR4).

**Rule setting (WRD3, ARC-TECHNOLOGY-009).** `Read`, `NewspaperFiller` and `Markup` take the session's `WrittenRuleProfile`
(`GameRules.Written`; `QueryWrittenDocument` and the viewer pass the session's). Original (and no profile, a tool's view) drops the
first character of a message without a font prefix as above. Steamwoven reads a message whose first character is not an ASCII digit
as having no markup: its whole text, font 0, not centred (owner decision 2026-10-21; switch `UnprefixedMessageLosesFirstCharacter`).
A message that starts with an ASCII digit reads the same in both; one whose `atoi` prefix follows white space or a sign gets that font in Original and font 0 (whole text) in Steamwoven.

Refused as the original cannot show them, in both rule settings (WRA1–WRA4): a subtype outside 0..6 (`SubtypeOutOfRange`), a picture
art outside 0..4095 (`ArtOutOfRange`), a missing text file (`TextsUnavailable`, as the original refuses silently); the note and
telegram buffer stops at 19,999 bytes; a markup font outside the range is 0; the viewer's book pages stop at 100.

## Use and query (Session, WR2, WR13, WR14)

`GameSessionOwner.UseWrittenItem(expected, entity, facts, texts, useScriptRan)` is the hero's use of a written item it holds. Refused
before any effect: no hero or a dead one (`InvalidLifeTarget`, as the original's start returns for a dead reader), an invalid entity,
the version, an item the hero does not hold (`ItemNotAvailable`) or that is not written (`InventoryMismatch`), an item with a use
script whose caller did not run it (`ItemScriptUnsupported`: the original runs attachment point 1 first and it can cancel the use;
ARC-SCRIPT-020: a host runs it and passes `useScriptRan` only when the script let the use go on), an item on the
item-spell path (`ItemTypeUnsupported`: a spell mana store or the magical item flag), a subtype or picture art out of range
(`InvalidItem`). Then a schematic document registers through `ReadWrittenItem` (TC8; new → the document is destroyed, known → kept)
and any other kind gives the document the window shows (`NoChange`). Reading changes nothing else: the original keeps no read flag.
`QueryWrittenDocument(facts, texts)` reads the same document without a hero, for a script opener. The 4 action points the original
charges in a turn-based round are not charged (the viewer reads outside rounds).

`SessionWrittenFacts` carries the item's subtype and first text line, its prototype, whether it has a use script (attachment point 1)
and whether it takes the item-spell path; the host reads them with the combat field reader from an area row (instance over prototype)
or, for a kit or made item, from its prototype.

## Script openers (Scripting, WR15)

The SCR actions 64 (show a book: number, viewer), 65 (a picture: art, viewer) and 114 (a plaque: number, viewer) are recorded as
`ScrReadingRequest`s in the run's effects; admission set S10 (S9 + 64, 65, 114). The viewer's dialog script host and its use, tile and
sector script host (`S4-area-use-reading-objects`) admit them and keep the requests whose viewer is the PC, as the original opens the window
only for a player viewer (no dead check). Action 94 (set a written item's first text line) is not implemented.

## Viewer (AREA, `crash-site-area`: exterior, cave and `--area-map`)

- The written texts and the facts of every written prototype load once per module in the background (`AreaReading.LoadData`).
- The inventory window shows a `Read:` button for each written item the hero holds (kit, taken or made). An item with a use script
  (ARC-SCRIPT-020, WR13, IU rows) first runs it through the use script host (`AreaUseScriptHost.RunItemUse`, attachment point 1, the
  PC as the user and no target, on the inventory row of the map the item came from): its effects apply (story, quests, rumors,
  areas, flags, awards); skip default stops the reading with a status line naming any floating line the script printed (a line
  printed before run default is only logged); a run the host refuses (an action outside its set, an unreadable script, a teleport,
  which an item's use does not model; effects applied before the refused step stay, IU rows), a kit or made item with a use script
  (no map row) or an item whose row is not loaded is not read. As in the original (IU4, WR2), the script runs before the session's
  dead-reader and item-spell checks, so its effects stay when those then refuse the reading. A schematic document
  registers and the window shows `mes\tech.mes` 89 with the schematic's name (88 when it was known; the Steamwoven refusal of a
  document numbered 0 has its own text); the document leaves the list when new. Any other kind replaces the inventory window with
  the reading window, as the original's mode change pops the inventory's mode.
- A script's reading request opens the window once no dialog, window or palette owns input (PROJECT: the original changes the mode
  at once; a later request replaces a waiting one). A quick load drops a waiting request and closes an open window.
- The window is modal like the schematic window (the hero's input gate closed; the clock runs, ARC-DEV-126) and owns every key. Escape, a controller cancel or the
  Close button close it (PROJECT keys: the original's window takes no key and closes by its frame hotspot or a mode change); a book's
  Previous and Next, Left/Right or Page Up/Down turn two pages. Sounds: open 3008, a book's page 3010, close 3009 by the player
  (a teardown is silent).
- **Diagnostic layout (PROJECT, NOT_VERIFIED for parity):** a plain panel instead of the original's background, frame and page art
  and its fonts; the header line names the kind and the arts; a newspaper's masthead is one line and three filler messages follow the
  body (the original fills its columns); a picture names its art instead of drawing it; a book pages by 700 characters instead of
  the original's pixel height, keeping two pages a spread and the turn of two.

## Installed data (opt-in census, `WrittenRealDataTests`)

68 written prototypes: 3 books, 2 notes, 2 newspapers, 2 telegrams, 1 picture and 58 schematic documents; every text file loads;
16 newspaper fillers. Every one is read or a schematic document (no crash-class record). Four template prototypes start at line 0
where no message exists and read empty (WRD1; one gets its line from its insert-item script's action 94), the two newspapers at
line 0 show the masthead messages as their body, the last text line is 0 everywhere (WRD2), two documents have a message without a
font prefix (WRD3: whole in Steamwoven, first character dropped in Original). Two readable prototypes have a use script and one more only an insert-item script; none is magical. Both use scripts run in a new game's state and let the reading go on
(ARC-SCRIPT-020, `AreaUseScriptsOriginalTests`): the note 14086 (script 1146) raises the story state to 15, completes quest 1016 and
learns rumor 2107; the book 14090 (script 1891) marks area 44 known and accepts quest 1103 (the PC does not hold item 2021). The native
smoke reads a newspaper template (the first readable prototype with text and no use script) and registers schematic document 14093
(schematic 4000) on a detached session.

## Not implemented

- The item-spell path, the 4 action points in a round (also charged when the use script stops the use), action 94, the trap check
  the original's script call makes at attachment point 1, a teleport from an item's use script.
- The original's art, fonts, pixel layout, newspaper columns and picture drawing; a scenery click that reads in place (the
  original has none: plaques on maps come through scripts, WR16).

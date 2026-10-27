# Original verification ledger: Interface windows and audio

Part of the [original verification ledger](../original-verification-ledger.md) (ARC-RES-048): its [inputs and method](../original-verification-ledger.md#inputs-and-method), [verdicts](../original-verification-ledger.md#verdicts-and-what-they-mean-for-the-two-settings), [decisions to revisit](../original-verification-ledger.md#decisions-to-revisit) and [second reading](../original-verification-ledger.md#second-reading) cover every section below. A new section of this area goes at the end of this file and adds one line to the ledger's [area index](../original-verification-ledger.md#areas).

## Audio (ARC-RES-052)

Source: the [audio study](../audio-evidence.md#what-the-original-does), rows SN1–SN34, which keeps the
full table (SN34: one reading, ARC-WORLD-041). Three domains (resources and positional sound, schemes and combat music, voice and
event sounds) were each read by a first reader and, independently, a second reader that saw only
the questions (method step 6); the three differences are settled in the study. Every direct
branch of the functions read stays inside `.text`.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| SN3 | `gsound_init` (0x41AA30) | soundparams key 12 is the small size's volume (default 40), key 22 the medium's (70); no overwrite. | Reconstruction slip |
| SN4 | `gsound_calc_positional_params` (0x41B420) | The attenuated volume is multiplied by the size's volume % (small 40, medium 70, large 100). | Reconstruction slip |
| SN12 | `gsound_scheme_load` (0x41BE20), `gsound_scheme_parse_entry` (0x41BF70) | A 26th entry in one scheme dereferences a null pointer (crash). | Original bug (no installed scheme reaches it; both settings refuse) |
| SN13 | `gsound_scheme_parse_entry` (0x41BF70) | `/over` wins over `/loop`; a balance-left above 127 clamps to 127. | Reconstruction slip |
| SN14 | `gsound_update` (0x41BAF0) | At most one ambient per slot per tick; hour windows never wrap, so 20 installed night entries never play. | Reconstruction slip (one per tick); original bug (windows), rule switch (ARC-UI-041) |
| SN17 | `gsound_start_combat_music` (0x41C4D0) | An NPC entering combat starts stinger and loop together; the local PC leaving combat restores the latest request. | Original |
| SN18 | `gsound_save` (0x41B1D0), `gsound_load` (0x41B0F0) | A save made during combat loses the pre-combat schemes. | Original bug, rule switch (ARC-WORLD-040) |
| SN22 | `dialog_ui_speech_start` (0x5688D0) | Voice gender is the PC's; closing the dialog does not stop the voice. | Original |
| SN24 | `sfx_critter_sound` (0x4F0ED0) | The off flag is tested on the local PC, not the sounding critter. | Reconstruction slip |
| SN26 | `AGbeginAnimLoopAnim` (0x42DA50), eye candy (0x431550, 0x431960) | Looping scenery and eye-candy sounds loop; flag bit 0x80; handle test and goal fields as written in the study. | Reconstruction slip |
| SN31 | hot-key equip (0x550000) | Equipping into an empty slot plays the slot number as a sound id. | Original bug, switch candidate |

## Logbook (ARC-UI-008)

Source: the [viewer logbook](../../rendering/terrain-viewer.md#logbook-arc-ui-008), the [story domain](../../world/game-session-runtime.md#story-domain)
and [FR-SOCIAL-008](../../requirements/story-social-ai.md). Question: what the original logbook shows on its rumors and quests
pages, which text it reads, how it opens and how a rumor is quelled. Read on 2026-10-18 by a first reader and,
independently, a second reader that saw only the questions (method step 6); both agree on every row. Every direct branch
stays inside `.text`; the indirect ones (the key handler's character table, the tab switch of the list builder, the dialog
action table) stay inside their functions. LB8 is a slip of the reconstruction (the first verdict of the table above), so
no rule switch; Steamwoven reads both files as the original does.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| LB1 | in-game message filter (0x54B5D0) | The character messages `l` and `L` (one case of the filter's character table, next to `c`/`C` of CS1) call the logbook toggle with the local PC, under the guards of CS1 (not while another critter acts in turn-based combat, a text edit owns the keys or the main menu is active); an interface sound follows every handled letter. The interface's logbook button calls the same toggle. A keyboard key for the logbook was not found in the earlier reading of the key handlers; the executable has one. | Original |
| LB2 | logbook toggle (0x53F020), logbook close (0x53F090) | Already open: closes it (a toggle). Otherwise a null object returns; a dead **local PC** (not the argument) refuses; the interface mode is set to main, then to the logbook, either refusal refuses; then the object is stored and the window created. Close sets the main mode, then destroys the window and forgets the object. | Original |
| LB3 | logbook create (0x53F0E0), destroy (0x53F2F0), panel draw (0x53F490), tab switch (0x53F5F0), reset (0x53EFD0) | The tab and the page spread are kept across close and open: opening rebuilds the lists and redraws the kept spread. Clicking a tab resets the page to the first spread; a game reset (load, new game) sets the rumors tab and the first page. Holding left Ctrl and left Alt while opening shows a hidden page of developer quotes in place of the rumors. Neither opening nor creating pauses the game. | Original |
| LB4 | entry timestamp (0x5403C0), rumor entry (0x540310), quest entry (0x540470) | An entry starts with a date line: the month name (message month + 6 of `mes\logbk_ui.mes`), the day, a comma, the year, two spaces, the hour on a 12-hour clock (hour 0 is 12 am, hour 12 is 12 pm) with two minute digits and "am"/"pm", then four spaces. A quest entry appends its state label (message state + 19) right after it. A newline follows, then the rumor's text or the quest's description. | Original |
| LB5 | logbook list builder (0x53FBB0), rumors and quests tabs | Rumors: struck through in black when quelled, plain black otherwise. Quests: accepted and achieved in blue; completed struck through in green; completed by someone else and botched struck through in red; mentioned in plain black. | Original |
| LB6 | quest logbook list (0x4C52E0), its comparator (0x4C53A0) | Only a PC has quests. Every quest record whose state without the botched bit is not Unknown is listed (a quest botched while still Unknown is not), reported Botched when the botched bit is set and with its stored state otherwise, then sorted by the date-time of its last change, oldest first, with the run-time library's sort, which is not stable: the order of equal times is the sort's. | Original |
| LB7 | quest description (0x4C5250) | The output starts empty; the low-intelligence description is used when one is loaded and the object's intelligence is 4 or less, otherwise the normal one; with neither the description stays empty. | Original |
| LB8 | quest module load (0x4C4670) | `rules\gamequest.mes` is parsed for quests 1000..1999; only when that file loads are `mes\gamequestlog.mes` (normal descriptions) and `mes\gamequestlogdumb.mes` (low-intelligence descriptions) loaded, each searched for 1000..1999 in its own file; the second is tried even when the first is missing. The earlier reading searched the normal file for both, which made the low-intelligence descriptions the normal ones. | Reconstruction slip |
| LB9 | rumor logbook list (0x4C5A40), its comparator (0x4C5AF0) | Every rumor 1000..2999 with a non-zero learning time is listed with its quelled mark, sorted by that time, oldest first, with the same unstable sort. | Original |
| LB10 | rumor logbook text (0x4C5920, 0x4C5960, 0x4C59D0), rumor module load (0x4C5570) | Module load reads `mes\game_rd_npc_m2m.mes`, `…_m2f`, `…_f2f`, `…_f2m` and `…_m2m_dumb`. A rumor's logbook text is message 20 × number of the first file, or of the last one when the object's intelligence is 4 or less; a missing message gives an empty text, with no fallback to the other file. | Original |
| LB11 | rumor quell (0x4C5690), multiplayer lock test (0x4A2BA0) | In single player the rumor's bit (number − 1000) of a game-wide bitmap is set; there is no range check. A multiplayer game sends a message as well. No function clears a bit except the module load. | Original |
| LB12 | dialog action executor (0x415BA0) | `RQ n` quells rumor *n* with no participant and no PC test; `RU n` marks rumor *n* known to the dialog's PC. | Original |
| LB13 | date-time hour (0x45A870), minute (0x45A8B0), day (0x45A8E0), month (0x45A900), year (0x45A920) | From the (days, milliseconds) pair, unsigned: hour = ms / 3,600,000 mod 24, minute = ms / 60,000 mod 60, day = days mod 30 + 1, month = days / 30 mod 12 + 1, year = start year + days / 360. | Original |

Not modelled by Steamwoven: the order of equal times (Steamwoven orders them by number), a rumor learned at game time 0
(the original's zero time means not known; no profile starts there), the developer quotes page, the
other five tabs, the book page spreads, the month and state labels of `logbk_ui.mes` (the viewer uses its own), the
logbook button's highlight and the sounds.

## Reading window (ARC-UI-049)

Source: the [reading window runtime](../../world/reading-window-runtime.md). Question: how does the original show a written item
(books, notes, newspapers, telegrams, pictures, plaques), which texts and art it reads, how it pages, opens and closes, which
sounds it plays, what state it changes, and who else opens it? Read on 2026-10-21 by this task and, independently, by a fresh agent
that saw only the questions (method step 6); where they differed (the markup, the buffer size, the filler wrap, the alternate art,
the masthead) a third reading settled the row. The rows are F2 unless marked. Every direct branch stays inside `.text`. Prototype
numbers below are the project's (the executable's basic number + 20: its 14072 is our 14092).

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| WR1 | module init (0x56BAA0) | Loads `mes\gamebook.mes`, `gamenote.mes`, `gamenewspaper.mes`, `gametelegram.mes` and `gameplaque.mes`, and counts the newspaper fillers with 0x4D4550(10, 999): 0 when message 10 is missing, else the messages present from 10 on, up to 999. | Original |
| WR2 | reading start (0x56BB60) → 0x4EE290 → open (0x56BC00) | A dead or null reader returns; subtype 5 registers (SK2); otherwise a flag is set for prototype 14092 and 0x4EE290 continues only for the local PC (an NPC reader gets nothing). The open toggles: a second open closes. A missing text file of subtypes 0–3 and 6 refuses silently. The interface mode change pops the current mode (an open inventory closes) and pushes the reading mode 15; leaving mode 15 closes the window (0x551D3D → 0x56BC90). | Original |
| WR3 | create (0x56BCC0), kinds and art | Book 0 → background art 477, note 1 → 489, newspaper 2 → 487, telegram 3 → 491, picture 4 → the start line is the art, plaque 6 → 633; frame art 494. The prototype-14092 flag selects art 817 for every subtype but a picture. Neither the subtype (an index into the art table) nor a picture's art is range-checked. | Original (WRA1, WRA2) |
| WR4 | create (0x56BCC0), body | The messages from the item's first text line (field 203) on, up to ten, stopping at the first missing number. The last text line (field 204) and the written flags (201) are never read. | Original (WRD2) |
| WR5 | book (0x56BCC0, 0x56C050) | Two pages of 200 × 290 at (227, 57) and (476, 57) with the fonts of the markup; buttons with arts 495 and 496 turn two pages back or on; the page tables hold 100 entries and are filled without a bound; the book always opens at its first spread. | Original (WRA4) |
| WR6 | note | The messages joined, each followed by a newline, in a 20,000-byte buffer appended without a bound; font 497; no markup and no paging. | Original (WRA3) |
| WR7 | telegram | The form texts 1000–1010 and 1011 as headers; each body message followed by message 1012, in the same buffer; font 480; no markup. | Original (WRA3) |
| WR8 | newspaper masthead (0x56C050) | Six header entries: messages 0, 1, 0, 2, 3, 2 with fonts 485, 484, 485, 482, 482, 482; with the prototype-14092 flag messages 0, 4, 0, 2, 5, 2 and the second font 815. The headline is the start line; the next nine fill four columns. | Original |
| WR9 | newspaper filler (0x56C050) | After the body the columns are filled from message 10 + start / 10 (10 when that is at least 10 + the filler count), each next message one higher, wrapping to 10 at 10 + the count; a missing message stops the fill. | Original |
| WR10 | plaque | The markup applies; one text rectangle; white text. | Original |
| WR11 | markup (0x56C9F0) | The font is `atoi` of the message (leading whitespace and a sign skipped); a loop then skips the digits only; the next character is tested for `c`/`C` (centred) and is dropped whatever it is; then one whitespace character more is dropped. A font outside the art range is not checked. | Original; Steamwoven switch (WRD3, ARC-TECHNOLOGY-009) |
| WR12 | sounds and close (0x56BC00, 0x56BC90, the window handler) | Open plays 3008, a book's page turn 3010, close 3009. The window closes by an 89 × 89 hotspot at (25, 24) of the frame, by any interface mode change or by a second open; its handler takes no key. | Original |
| WR13 | item use (0x462CC0, variant 0x462FC0) | The item's use script (attachment point 1) runs first and can cancel the use; an item with a spell mana store or the magical item flag (0x4) takes the item-spell path instead; in a turn-based round the use costs 4 action points, whose result is not tested; then the reading start (WR2). | Original |
| WR14 | state | Reading sets no read flag and changes nothing besides the schematic registration (SK2). | Original |
| WR15 | script actions (0x447220) | Action 64 shows a book (number, viewer), 65 a picture (art, viewer), 114 a plaque (number, viewer): only a player viewer gets the window, with no dead check. Action 94 sets a written item's first text line. | Original (94 not implemented) |
| WR16 | the world | No path reads scenery in place; a plaque on a map is shown by a script that calls action 114 (use, tile or sector scripts). | Original (F1: one reader, by the absence of another caller) |

Anomalies. Crash class (the no-crash rule; both settings fixed):

- WRA1 (WR3): a subtype outside 0..6 reads past the art table. Both settings refuse the item (`SubtypeOutOfRange`; the use is refused as an invalid item).
- WRA2 (WR3): a picture art outside 0..4095 draws a handle out of range. Both settings refuse it (`ArtOutOfRange`); a markup font outside the range draws font 0.
- WRA3 (WR6, WR7): the note and telegram buffer is appended without a bound. Both settings stop at 19,999 bytes. No installed document comes near it.
- WRA4 (WR5): a book of more than 100 pages writes past the page tables. Both settings stop at 100 pages.

Data oddities (not crash class; both settings follow the original until the owner decides; a proposal each; WRD3 decided below):

- WRD1 (WR4): four template prototypes (14082, 14083, 14085, 14089) start at line 0, where no message exists: their window is empty. 14089
  gets its line from its insert-item script (action 94, not implemented); the newspapers 14084 and 14092 at line 0 show the masthead
  messages as their body. Proposal: keep (the items as placed on maps or given by scripts carry their own lines).
- WRD2 (WR4): the last text line is 0 in every written prototype (spans −1050..0) and the original never reads it. Proposal: keep.
- WRD3 (WR11): a message without a font prefix loses its first character (two installed documents have a font-0 message). **Decided**
  (owner, 2026-10-21): a switch, `WrittenRuleProfile.UnprefixedMessageLosesFirstCharacter` (ARC-TECHNOLOGY-009): Steamwoven reads a
  message whose first character is not a digit as having no markup (its whole text, font 0, not centred); Original keeps the drop.

Not modelled (named in the runtime): the item-spell path, the 4 action points in a round, action 94, the original's pixel paging
and fonts. The use script before a reading runs since ARC-SCRIPT-020 (IU rows below); the two readable prototypes with one (14086,
14090) are read.

## Window key modifiers (ARC-INPUT-004)

Source: the [WORLD keyboard route](../../input/diagnostic-palette.md) and
[KM-07](../../requirements/original-keyboard-mouse.md#km-07--переносимость-key-identity-и-модификаторов). Question: do the
original's window letters (L, C, I, T, W, R and the other letters of the character table) act while Ctrl, Shift or Alt is
held? Read on 2026-10-27 by this task and, independently, by a fresh agent that saw only the questions (method step 6);
both agree on every row. Every direct branch stays inside `.text`; the indirect ones (the window procedure's message
switch, the filter's character and scancode tables) stay inside their functions. The rows are F2.

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| WK1 | message pump (0x52B7B0), window procedure (0x52B9D0), enqueue (0x52B890) | Every message goes through `TranslateMessage` (the game sets no pre-translate hook). WM_CHAR is the only keyboard message the window procedure turns into a game message: a character message (type 4) carrying the low byte of `wParam`. WM_KEYDOWN, WM_KEYUP, WM_SYSKEYDOWN, WM_SYSKEYUP and WM_SYSCHAR go to `DefWindowProcA`. Of the enqueue's 19 callers only the WM_CHAR case writes type 4. | Original |
| WK2 | in-game filter (0x54B5D0, a copy at 0x54C8E0), 0x57DC60 | The character branch acts on the character alone, with no modifier read: space, C, F, I, L, R, S, T and W in both cases through one table (character − 0x20, at most 0x57), A and a in 0x57DC60. A code below 0x20 (Ctrl+letter's control code) fails the range test and does nothing, except 0x0D, handled first as Enter; codes from 0x78 and bytes from 0x80 are ignored too. | Original |
| WK3 | in-game filter scancode tables (0x54C3E8, 0x54C57D), keyboard poll (0x52B380) | No scancode branch acts on A, C, F, I, L, R, S, T or W (key down: Backspace, Delete, `,` `.` `/`, the numpad block; key up: K, M, `,` `.` `/`). The poll queues no scancode message of a non-modifier key while either Ctrl or either Alt is held (Shift is not tested). | Original (the poll's Ctrl/Alt drop is not modelled: the viewer keeps its other keys' modifier rules) |
| WK4 | logbook open (0x53F0E0) | With left Ctrl and left Alt held the logbook opens in an alternate state (sound 0xBBA), cleared on close; this path is reached from the logbook button, since Ctrl+Alt+L makes no character on a standard layout. | Original (not modelled) |

What Windows does, not the executable: Ctrl+letter is a control code (Ctrl+M is 0x0D, Enter), Alt+letter is WM_SYSCHAR,
AltGr and the active layout decide the character (a Cyrillic layout gives bytes from 0x80, so the letters act only on a
Latin layout). So in the original, L and Shift+L (or Caps Lock) open the logbook, and Ctrl+L, Ctrl+Shift+L, Alt+L and
Ctrl+Alt+L do nothing; the same holds for A, C, F, I, R, S, T and W. Steamwoven (`OriginalWorldKeyboard.CharacterKeySilenced`)
silences these letters under Ctrl, Alt or Command (Command is the Mac system's), lets Shift through, and an open window's
own letter does not close it under those modifiers either. Not located in this pass: where O (options), V (version) and E
(the turn-based sub-turn) are read; they stay reserved keys at F0 with their earlier addresses.

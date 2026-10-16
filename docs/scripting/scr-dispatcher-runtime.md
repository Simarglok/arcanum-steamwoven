# SCR dispatcher core

## Status and boundary

Work item ARC-SCRIPT-008, slice **S2** of the
[SCR event dispatcher plan](../research/scr-event-dispatcher.md#10-proposed-implementation-slices).
`Arcanum.Scripting.Scr.ScrDispatcher` decides, runs and reports one script event:
an object's slot for an attachment point, a tile script, or a sector script. It
runs only programs that the [static admission](scr-program-admission.md) admits
for `ScrAdmissionSet.S2`. It reads the game through a host port and changes
nothing itself: the result carries every change, for the caller to apply as one
step. Godot-free, net8.0, synchronous. No session binding, no engine event fires
it yet (see [Not ready](#not-ready)).

Sources: rows E1–E13 of the dispatcher research (CE at the pinned commit), the
[verification ledger](../research/original-verification-ledger.md) for the
original 1.0.7.4 executable, and the rule modes of
[Steamwoven and Original](../world/rule-modes.md). Original-executable parity
beyond the rows named here is **NOT_VERIFIED**.

## API

| Call | Event | Writeback |
| --- | --- | --- |
| `RunObject(ScrObjectEvent, IScrHost, ScrRunOptions?)` | point 0..35 on an attachee (triggerer and extra optional), start line ≥ 0 | the attachee's slot for the point when its number, local flags or counters changed; a trap object whose script became 0 is destroyed instead |
| `RunTile(ScrTileEvent, IScrHost, ScrRunOptions?)` | the tile's slot, the entering critter; point use, line 0, no attachee | the tile's slot, unless `TileScriptChangesDiscarded` (Original, X3 tile) |
| `RunSector(ScrSectorEvent, IScrHost, ScrRunOptions?)` | the sector's slot, the player; point use, line 0, no attachee | the sector's slot in both modes (X3 sector is a CE slip) |

`IScrHost` answers reads only: the attachee's slot (`ScrSlot`: number, local
flags, counters), a script by number (`ScrScriptLookup`: found program, missing,
unreadable), global flag 0..3199, the story state, and the object questions of
the gates below. A host answers from one consistent state for the whole call.

`ScrRunOptions`: `Rules` (`ScrRuleProfile`, from `GameRules.Scripts`, read at
each call), `Admission` (default `S2`; a set the interpreter cannot run is an
`ArgumentException`) and `MaxExecutedLines` (1..1,000, default the original's
1,000). Invalid options throw; an event never does.

`ScrRunResult`: `Outcome` (`RunDefault`, `SkipDefault`, or `Refused` when the
script is outside the admitted set and nothing ran), `Stop` (why it ended),
`Script` (the number that ran, 2188 for a mind-controlled NPC), `StopLine`,
`Refusal`, `Trace` (one `ScrTraceStep` per executed line: line, condition, held,
action, next line or null) and `Effects`.

`ScrEffects`: the slot `Writeback` (source, attachee or null, point, new slot)
or `DestroyTrapObject`, the changed `GlobalFlags` (final values, ascending, only
those that differ from the host's), and the new `StoryState`. A run reads its
own writes back from an overlay. Effects made before a stop are kept, as the
original applies them as it goes; a refused script has none.

## Decision order of an object event

Each step returns at once when it applies (CE `object.c` 3866–3908 and
`script.c` 278–401; original `script_execute` 0x4449B0):

1. Point outside 0..35, the dialog-override slot (31, it names a DLG), a
   negative line or a default (empty) object reference: `SkipDefault`,
   `InvalidEvent`.
2. No slot, or script 0: `RunDefault`, `NoScript` (E2).
3. A use event with a triggerer: the host's trap spot; `Spotted` gives
   `SkipDefault`, `TrapSpotted` (E2).
4. Missing or unreadable script: `SkipDefault` (E4).
5. A trap script, number 30000..30007 with a trap file flag (0x1 or 0x2), as
   the original's `trap_script_execute` 0x4BCB70 tests: `SkipDefault`,
   `TrapScript`. The trap domain runs it and must reproduce what the original does
   afterwards: the seven installed trap scripts carry the auto-removing flag, so
   the slot clears and a trap object is destroyed through E3 (E4). A trap flag on
   any other number is an ordinary script.
6. A monster generator attachee: `RunDefault` (E5).
7. Dialog point only (E6). The host reports any combination of dead,
   surrendered and mind-controlled, and the gates test them in the original's
   order (0x444A95–0x444B17): dead without the death-speech flag, then
   surrendered without the surrender-speech flag: `RunDefault`; then
   mind-controlled: script 2188 runs on the NPC's slot instead, its own number is
   kept afterwards, and a missing 2188 gives `RunDefault` (X5).
8. Admission for the set; a refusal is `Refused` with the `ScrRefusal`.
9. The run, then the writeback (E3).

## The run

A run starts at the event's line with ten local variables at 0 and a private
copy of the slot's local flags and counters (E7, E11). Each line evaluates its
condition (the always-true condition runs only its true action) and runs one
action. Read in the original at 0x444B8E–0x444C4D:

| End | Stop | Default | Auto-removal |
| --- | --- | --- | --- |
| return and skip default (1) | `ReturnSkipDefault` | skip | yes |
| return and run default (2) | `ReturnRunDefault` | run | yes |
| the last line continues | `FellOffEnd` | skip | yes |
| a line outside the script: a goto to exactly the line count, a start line outside it, an empty script | `LineOutOfRange` | skip | no |
| 1,000 executed lines (or the option's lower budget) | `Budget` | skip | no |
| a computed global flag number outside 0..3199 | `IndexOutOfRange` | skip | no |

Auto-removal (file flag 0x4 of the attachment's own script) sets the number to 0.
Remove this script (5) and change this script (6) take effect after the run and
win over auto-removal; a computed change to −1 is the original's "no change",
and any other computed value is stored as the original does (E10). Admission
refuses a literal change outside 0..99999, −1 included (project strictness, no
installed script has one). A computed local flag number uses its low five bits,
as the original's `shl` does (0x4473C2, 0x4473F5): 32 is flag 0 and −1 flag 31.
The `IndexOutOfRange` stop is a fix in both modes: the original would read or
write outside its global flag store.

Values: a counter reads and writes its byte; a local variable its slot; a number
is itself; a global flag reads 0 or 1. Add, subtract and multiply wrap as 32-bit
integers. Get story state stores the current value; set story state only ever
raises it (original 0x4450A0).

## Rule-mode differences and fixes

| ID | Original 1.0.7.4 | Steamwoven | Original setting |
| --- | --- | --- | --- |
| X7 divide | signed quotient, nothing for divisor 0 (CE adds: CE slip) | as the original | as the original |
| X7 edge | `int.MinValue / −1` faults the original's `idiv` | stores `int.MinValue` (both fixed: crash) | stores `int.MinValue` |
| X8 counter write | masked to the counter's byte (CE spills: CE slip) | as the original | as the original |
| X10 global-flag write | clears the flag, ORs the whole value shifted to its bit into the 32-flag word (2 sets the next flag, a negative value every higher one) | the value's low bit into the flag only (D2 fix) | `GlobalFlagWriteSpills` |
| X3 tile writeback | the unmodified tile record is stored back | changes persist, as for objects and sectors (D2 fix) | `TileScriptChangesDiscarded` |
| X3 sector writeback | number, flags and counters persist (CE drops the number: CE slip) | as the original | as the original |
| X6 negative goto | −1..−4 are control codes | refused by admission (project strictness; no installed script uses one) | refused |
| global flag number outside 0..3199 | reads or writes outside the flag store | `IndexOutOfRange` stop (both fixed: out of bounds) | the same stop |

Set and clear global flag (100, 101) write 1 and 0 and never spill. The D2 fixes
above are recorded by this slice; `ScrRuleProfile` is part of
`GameRules` (`Scripts`).

## Dialogue (ARC-SCRIPT-009, slice S3)

- **Region admission.** The dispatcher admits, for each event, only the lines a
  run from the event's line can reach (`ScrAdmission.AdmitFrom`): it follows a
  literal goto, the next line, and ends a path at a return or a dialog action;
  both actions of a conditional line are followed, the dead false action of an
  always-true line is not. A run of an admitted region can only reach lines
  inside it, so it still never stops half-way on an unsupported command, and
  lines outside it are never read. The whole-script `Admit` stays for the census
  and tools. This lets Virgil talk: his entry lines 0..4 and the dialog lines
  they reach are inside the set, while later lines (float line, calls, loops) are
  not reached by the talk.
- **`ScrAdmissionSet.S3`** (the default of `ScrRunOptions`) is S2 plus condition
  18 (the first object's leader is the second object) and 39 (a jilted NPC),
  action 4 (dialog) and the single-object focus kinds triggerer, attachee,
  extra object and player. The host answers `Player`, `Leader`, `IsJilted` and
  `IsPc`. An object operand naming a role the event does not have (no
  triggerer, attachee, extra or player) stops the run with `MissingObject`
  (skip default); the original would pass a null handle (both fixed).
- **Dialog action (4).** It ends the run with `Stop = Dialog` and skip default;
  auto-removal applies as for a return. When the triggerer is a PC, `Effects.Dialog`
  (`ScrDialogStart`: PC, NPC, script, line, node) asks the caller to start the
  DLG of the run's script (2188 for a mind-controlled NPC) at the node after it
  applied the writeback: the original stores the slot before its start callback
  (X1 in the original: flags to flags).
- **Resumption.** A DLG end of 0 resumes the dialog script at the action's line
  + 1, −N at line N: the caller dispatches a dialog event with that line, and the
  region is admitted from it.

The AREA viewer's conversation host (`AreaConversationHost`) now runs the talk
request and the resumption through the dispatcher with `AreaScriptHost`
(`tools/terrain-viewer/Core`): the speaker's dialog slot is its effective area
attachment overridden by the session's entity map changes `dialog.lf`,
`dialog.counters` and `dialog.script` (no schema change; a value equal to the
loaded one clears its key); global flags are the session's; the PC leads a
following speaker. It runs `S3` without the story-state actions (the session has
no story-state number yet, so such a script is refused instead of run on a
made-up value). Run default is the original's generic greeting (shown as a
status, not modelled); a skip default without a dialog action ends the talk; a
refusal is `EntryNotAdmitted` with the line, code and type. The session applies
global flags and the slot as separate steps (it has no transaction across its
flag owner and its map changes); a refused step stops the later ones and is
reported as `ScriptEffectsRejected`.

## Checks

- `tests/Arcanum.Scripting.Tests/Scr/ScrDispatcherTests.cs`: every step of the
  decision order, every end of a run with and without auto-removal, remove and
  change, writeback only on change, fresh locals, trap object destruction, X7,
  X8 and X10 in both modes, the own-write overlay, story state, computed-index
  stops, tile and sector writeback per mode, refusal, invalid events and
  options, the trace, the profile.
- `tests/Arcanum.Session.Tests/RuleModeTests.cs`: `GameRules.Scripts` per mode.
- Opt-in, `DAT_ROOT` = the game folder, `ScrDispatcherRealDataTests`: every
  installed script dispatched on a synthetic NPC slot from line 0 in both
  modes, with the file's own number. S2 admits 148 of 2,399 (the 144 of S1 and
  four that remove or change themselves); 116 end on a return, 25 empty scripts
  on an unreadable line 0, the seven trap scripts (30000..30006) go to trap code
  without running, 2,251 are refused before running; both modes end every run
  the same way.

## Not ready

- The session has no story-state number and no global variables (a snapshot
  schema change, coordinated with other schema work); the viewer binding refuses
  the story-state actions and no admitted set reads global variables.
- Tile and sector writebacks name no location; the caller applies them to the
  tile or sector it dispatched.
- Only the AREA viewer's talk and resumption call the dispatcher (S3); use, tile
  and sector events are S4, deferred calls S5, heartbeat S6, combat S7, items S8.
- Not in the admitted set yet: focus groups and local objects, loops, calls and
  call-ex, and every world action (float line, teleport, quests, reactions...).
- The viewer's script host models only the dialog slot of the map's speakers;
  generators, traps, death, surrender, mind control and the jilted flag answer
  "no", and the generic greeting is a status line.
- X2 (an uninitialised attachee type without attachee) is reachable only through
  call-script and is left to the slice that admits calls.

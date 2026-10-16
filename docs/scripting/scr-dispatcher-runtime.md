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

1. Point outside 0..35, the dialog-override slot (31, it names a DLG) or a
   negative line: `SkipDefault`, `InvalidEvent`.
2. No slot, or script 0: `RunDefault`, `NoScript` (E2).
3. A use event with a triggerer: the host's trap spot; `Spotted` gives
   `SkipDefault`, `TrapSpotted` (E2).
4. Missing or unreadable script: `SkipDefault` (E4).
5. A trap-flagged script (file flags 0x1 or 0x2): `SkipDefault`, `TrapScript`;
   the trap domain runs it (E4).
6. A monster generator attachee: `RunDefault` (E5).
7. Dialog point only (E6): dead without the death-speech flag, or surrendered
   without the surrender-speech flag: `RunDefault`; mind-controlled: script 2188
   runs on the NPC's slot instead, its own number is kept afterwards, and a
   missing 2188 gives `RunDefault` (X5).
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
| a computed flag number outside 0..31 (local) or 0..3199 (global), or a computed script number outside 0..99999 (−1 excepted) | `IndexOutOfRange` | skip | no |

Auto-removal (file flag 0x4 of the attachment's own script) sets the number to 0.
Remove this script (5) and change this script (6) take effect after the run and
win over auto-removal; a change to −1 is the original's "no change" (E10). The
`IndexOutOfRange` stop is a project bound in both modes: the original would read
or write outside its flag store or store a nonsense number.

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

Set and clear global flag (100, 101) write 1 and 0 and never spill. The D2 fixes
above are recorded by this slice; `ScrRuleProfile` is part of
`GameRules` (`Scripts`).

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
  modes. S2 admits 148 of 2,399 (the 144 of S1 and four that remove or change
  themselves); 116 end on a return, 25 empty scripts on an unreadable line 0,
  seven trap scripts go to trap code, 2,251 are refused before running.

## Not ready

- No session binding: the session has no script slot store, story-state number
  or global variables, so nothing applies `ScrEffects` yet; that needs a snapshot
  schema change (a later slice, coordinated with other schema work).
- No engine event calls the dispatcher yet; dialogue through it is slice S3, use,
  tile and sector events S4, deferred calls S5, heartbeat S6, combat S7, items S8.
- Not in the admitted set yet: object operands and focus groups, loops, calls
  and call-ex, the dialog action and resumption, and every world action.
- X2 (an uninitialised attachee type without attachee) is reachable only through
  call-script and is left to the slice that admits calls.

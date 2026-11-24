# SCR program reader and static admission

## Status and boundary

Work item ARC-SCRIPT-007, slice **S1** of the
[SCR event dispatcher plan](../research/scr-event-dispatcher.md#10-proposed-implementation-slices).
It reads an SCR script into an immutable program and decides, before anything
runs, whether a whole script can run under a given interpreter version; a
script that cannot is refused with the line, the clause, the type number and,
for an operand, the slot, kind and value. It runs nothing: there is no
interpreter, event, dispatcher, writeback or world state here (slice S2 and
later). The first admitted set and its rules come from the
[ARC-RES-045 census](../research/scr-census.md#proposed-s1-admission-rules-p).
Godot-free, net8.0. Original-executable parity is **NOT_VERIFIED**.

## Reader (`Arcanum.Data.Scripts`)

`ScrProgram.Read(ReadOnlySpan<byte>, ScrReaderOptions?)` returns
`ScrReadResult(Status, Program)`; only `Read` carries a program. Framing is
strict and checked in this order:

| Status | When |
| --- | --- |
| `TooLarge` | the payload exceeds `MaxBytes` (default 1 MiB) |
| `Truncated` | shorter than the 64-byte header |
| `NegativeCount` | the line count (offset 52) is negative |
| `CapacityBelowCount` | the stored capacity (offset 56) is below the count |
| `LengthMismatch` | the payload is not exactly `64 + count × 132` bytes |
| `TooManyLines` | more than `MaxLines` lines (default 4,096; the largest installed script has 221) |

The header supplies the initial local flags (offset 0) and counters (4) of an
attachment that has none of its own, and the file flags (48); the 40-byte
description and the stored pointer word are not kept. Each line is a condition
and two actions (`OnTrue`, `OnFalse`), each a `ScrClause` of a type number and
eight operands (kind byte, 32-bit value). An empty script (count 0) is a valid
program. Programs, lines and clauses are immutable copies; nothing aliases the
payload. Invalid options throw `ArgumentOutOfRangeException`; a payload never
throws.

`ScrCatalogue` holds the type catalogue as format facts: 53 condition types
(0..52) and 142 action types (0..141) with each type's operand signature
(`ScrOperandRole.Number`, `Object` or `Unused` for each of the eight slots), the
7 value kinds (`ScrValueKind`) and the 24 focus kinds (`ScrFocusKind`), the file
flags and the store sizes (32 local flags, 4 counters, 10 local variables and
local objects, 3,200 global flags, 2,000 global variables). Only the types the
project reads by name have named constants. The numbers and signatures are read
from a reading of the original's interpreter, cross-checked against the
ScriptEd 1.50 editor data and against every installed script (ARC-RES-045).

## Admission (`Arcanum.Scripting.Scr`)

`ScrAdmission.Admit(ScrProgram, ScrAdmissionSet)` returns `ScrAdmissionResult`:
either `Admitted` (an `ScrAdmittedProgram` of the program and the set) or the
first `ScrRefusal`. It checks **every line** (rule P7), in line order, the
condition first, then the true action, then the false action; within a clause
its type, then its operands left to right (slot 0's index, write and goto rules
right after slot 0's kind), and stops at the first refusal. Only `Admit` creates
an `ScrAdmittedProgram`, so holding one is the proof of admission; a result
carries exactly one of the program and the refusal.

| Code | Rule | Refused when |
| --- | --- | --- |
| `UnknownCondition`, `UnknownAction` | P2 | the type is outside the catalogue |
| `ConditionNotAdmitted`, `ActionNotAdmitted` | P2 | a known type is outside the set |
| `UnknownValueKind`, `ValueKindNotAdmitted` | P3 | a number slot's kind byte is above 6, or outside the set |
| `UnknownFocusKind`, `FocusKindNotAdmitted` | P3 | an object slot's kind byte is above 23, or outside the set; since ARC-DEV-100 also a PC variable or PC flag operand (value kind 5 or 6) whose high 16 bits (the focus kind of its object, an arithmetic shift) are no focus kind or one the set does not admit (its low 16 bits are the index, any of 0..65535) |
| `IndexOutOfRange` | P4 | a literal index outside its store: a counter, local variable, global flag, global variable or local object operand, or the literal flag number of a local-flag (0..31) or global-flag (0..3199) condition or action |
| `WriteToNumber` | P5 | an assign, add, subtract, multiply, divide or get-story-state action stores into a number literal (operand 0), or a get-skill action (set S11, ARC-SKILLS-009) into one (its operand 2) |
| `ComputedGoto` | P6 | a goto whose target is not a number literal |
| `NegativeGoto` | P6 | a literal goto below 0 (the original reads −1..−4 as control codes, ledger X6) |
| `ScriptNumberOutOfRange` | P4 | a literal change-this-script number outside 0..99999 (ARC-SCRIPT-008) |

Not refused, by design:

- **P1** Slots a type's signature does not read are never checked: the installed
  data leaves editor bytes in nearly every clause.
- The false action of an always-true condition (type 0) is never run, so it is
  not checked either.
- A literal goto to the line count or beyond it is the end of the script: *skip
  default* without auto-removal (the original clears an auto-removing script
  when a run falls past the last line, not on this goto, F0; census rule P6).
  Since ARC-SCRIPT-3005 a target past the line count is admitted too: the
  original's line read compares unsigned and a target outside 0..count−1 ends
  the run with skip default (ledger E9, [`scr-event-dispatcher.md`](../research/scr-event-dispatcher.md)),
  which the interpreter already does as `LineOutOfRange`; the refusal
  `GotoBeyondEnd` was the project's own strictness and is gone (installed
  dialog scripts 1537 and 1539 hold such a goto).
- A computed flag index (a counter, variable or flag holding the number) is a
  run-time bound for the interpreter (S2), not a static refusal.

`ScrAdmissionSet` is data: an id and the admitted conditions, actions, value
kinds and focus kinds, each checked against the catalogue and frozen. A later
slice widens admission by publishing a larger set, not by changing the rules.
`ScrAdmissionSet.S1`:

| Part | Admitted |
| --- | --- |
| Conditions | 0 always true, 3 local flag, 4 `==`, 5 `<=`, 43 global flag |
| Actions | 0 do nothing, 1 return and skip default, 2 return and run default, 3 goto, 8/9 set/clear local flag, 10 assign, 11 add, 12 subtract, 13 multiply, 14 divide, 42 get story state, 43 set story state, 100/101 set/clear global flag |
| Value kinds | counter, local variable, number, global flag |
| Focus kinds | none |

`ScrAdmission.AdmitFrom(program, set, line)` (ARC-SCRIPT-009) is the region
admission the dispatcher uses: only the lines a run from `line` can reach, with
the same rules; the refusal reported is the lowest-numbered refused line among
those reached through admitted lines (a refused line's successors are not
explored); `ScrAdmittedProgram.EntryLine`
and `Region` record what was admitted. `ScrAdmissionSet.S3` adds the follower and
jilted conditions, the dialog action and the single-object focus kinds
([dispatcher dialogue](scr-dispatcher-runtime.md#dialogue-arc-script-009-slice-s3)).

`ScrAdmissionSet.S2` (ARC-SCRIPT-008) is S1 plus 5 remove this script and 6
change this script; it is the set the [dispatcher core](scr-dispatcher-runtime.md)
runs and admits 148 installed scripts. `IsSubsetOf` compares two sets.

On the installed module S1 admits 144 of the 2,399 scripts whole (the census
count; the opt-in `RealData` test checks it). It is a correctness core, not a
coverage target.

## NPC dialog entry

The bounded [NPC dialog entry resolver](npc-dialog-entry-runtime.md) now frames
its script with `ScrProgram.Read` instead of its own private parser and keeps its
own rules on top (at least one line, capacity within its entry limit, no file
flags, its query-only walk and frontier codes). Its results are unchanged.

## Checks

- `tests/Arcanum.Data.Tests/Scripts/ScrProgramTests.cs`: header and clause
  values, empty script, every framing status and its order, no aliasing,
  catalogue sizes and signatures.
- `tests/Arcanum.Scripting.Tests/Scr/ScrAdmissionTests.cs`: a script with every
  S1 type and value kind is admitted; unread slots and the dead false action
  are ignored; every refusal code with its line, clause, slot, kind and value;
  first-refusal order; set validation and immutability.
- Opt-in, `TestCategory=RealData`, `DAT_ROOT` = the game folder:
  `ScrProgramRealDataTests` (all 2,399 scripts frame, census counts, every read
  kind inside the catalogue) and `ScrAdmissionRealDataTests` (S1 admits 144, no
  unknown type, index or write refusal).

Divide (14), counter writes and arithmetic writes into a global flag are
admitted on shape only; their meaning (X7 quotient and X8 masked counters as in
the original, X10 as a rule-mode switch) is fixed by the
[dispatcher core](scr-dispatcher-runtime.md#rule-mode-differences-and-fixes).

## Not ready

No interpreter, no run result, no dispatcher, no attachment writeback, no
object operand resolution, no loops, calls, dialog action or resumption through
this path; those are slices S2..S8.

# Bounded DLG reader

`Arcanum.Data.DlgReader` is an engine-independent, byte-oriented reader for a
bounded physical DLG subset. It reads caller-provided bytes or a readable
`Stream`; it neither discovers installations/archives nor selects resource
precedence.

## API and ownership

```csharp
var document = DlgReader.Read(bytes);
var validation = DlgReferenceValidator.Validate(document);
```

`Read(ReadOnlySpan<byte>, DlgReaderOptions?)` and
`Read(Stream, DlgReaderOptions?, bool leaveOpen = false)` return immutable
`DlgDocument` values. The document preserves serialized order, supports
`TryGetRecord(int, out DlgRecord?)`, and owns copied field bytes: mutating the
caller buffer or disposing the input stream cannot change a returned document.
`DlgField.Bytes`, records, and reference rows are read-only collections.

The stream overload starts at the current position and reads to EOF, including
short-read/non-seekable streams. It does not restore position. It disposes the
supplied stream on success and failure unless `leaveOpen` is true. Unreadable
streams cause `ArgumentException`; native I/O/disposal errors are not wrapped.

Safety options default to `MaxInputBytes = 512 KiB`, `MaxRecordCount = 10,000`,
and `MaxFieldBytes = 4 KiB`. All must be positive. They are safety policy, not
claims about every DLG file.

## Physical grammar and data model

The accepted grammar is exactly:

```text
trivia* (record trivia*)+
record = field field field field field field field
field = "{" payload "}"
```

Outside a record, trivia is ASCII space, LF, CRLF, or a `//` comment ending at
LF, CRLF, or EOF. Fields are adjacent: no whitespace/comments occur between a
record's seven fields. Tabs, lone CR, high-bit/control bytes outside payloads,
NUL, unmatched braces, nested/escaped fields, empty input, and other outside
variants reject. Nested/escaped fields and unsupported outside forms are
`DlgUnsupportedFormatException`; malformed/truncated/NUL/numeric/duplicate
forms are `DlgFormatException`; configured caps are `DlgLimitExceededException`.
These `DlgException` values provide zero-based `Offset` and nullable
`RecordIndex`/`FieldKind`. Diagnostic messages describe only structural facts,
never a field payload.

The seven `DlgFieldKind` values are `Id`, `Text`, `AlternateText`, `Iq`,
`Condition`, `Response`, and `Action`. `PayloadOffset` is a zero-based offset
into the supplied input and `Length`/`Bytes` preserve payload bytes exactly.
Payloads in text, alternate text, condition, and action fields are opaque and
may include high-bit, control, and newline bytes (but never `{`, `}`, or NUL).
No codepage decoding, operation parsing, normalizing, or execution occurs.

ID, IQ, and response fields use strict signed ASCII `Int32` decimal (optional
`-`, then one or more digits). IDs are nonempty and unique by parsed value.
Empty IQ means `Npc`; nonempty nonzero IQ means `Player`; `0` and `-0` reject.
Responses preserve `null` for empty and `0` for explicit zero. For player rows
those are both `Exit`; positive values are `Direct`, negative values are
`Special`. NPC response values remain preserved but are not references.

## Structural reference validation

Parsing deliberately does not reject graph defects. Instead,
`DlgReferenceValidator.Validate` produces one `DlgReference` per player in
serialized order. Direct positive responses resolve through the parse-time ID
index as `Valid`, `MissingTarget`, or `WrongTargetRole`; exit and special rows
are valid with no target. Cycles are valid and no runtime traversal, grouping,
entry selection, or operation interpretation is performed.

## Synthetic example

The following invented bytes describe one NPC and one player exit, without
original data:

```csharp
var bytes = System.Text.Encoding.ASCII.GetBytes(
    "{100}{synthetic-npc}{}{}{}{200}{}\n{200}{synthetic-player}{}{5}{}{0}{}");
var document = DlgReader.Read(bytes);
```

## Opt-in local evidence check

The test category `DlgRealData` is read-only and requires an explicit local
`DAT_ROOT`. It uses existing bounded `DatArchive` APIs to verify the archived
hash/size before and after reads, exact raw members, payload hashes, aggregate
record/reference results, and source-span field preservation:

```text
DAT_ROOT=/path/to/Arcanum dotnet test \
  tests/Arcanum.Data.Tests/Arcanum.Data.Tests.csproj --no-build \
  --filter "TestCategory=DlgRealData"
```

It reads only the selected members and never extracts or writes original
resources. Original dialogue bytes/text/expressions are kept local: logs and
review evidence use only approved identifiers, hashes, sizes, counts, and
status aggregates. Physical grammar evidence is in the sections below and the [dialogue evidence
record](../research/dialog-evidence.md); this implementation is independently
authored and does not execute the research probe. The conversation
runtime lives in the [conversation runtime](../scripting/dialog-conversation-runtime.md).

## Research subset: observed grammar and bounded policy

Carried from the former seven-field research record of
`tools/research/Arcanum.DialogProbe` (one explicit archive path and one exact
raw DLG identifier, `dlg\basename.dlg`: printable ASCII, no drive, separator
or traversal forms). The initial inspection candidate was `dlg\01324Virgil.dlg` in
`arcanum3.dat` (ordinal 160; declared/stored bytes 56,782/12,335; archive SHA-256
`cbadd76bb3df798fa25dcc05871af1ea751c1164fb1426dccc520cd7d9edc499`); a file
name is not evidence of character identity, initial encounter, archive winner or
runtime entry node.

The original's dialog entry and field parse (0x417860, 0x417C20) is a
brace-delimited entry of seven fields. The roles and the evidence behind them:

| Field | Role | Boundary |
| --- | --- | --- |
| 0 | unique signed decimal record ID | full `Int32`; duplicate, malformed or empty IDs reject |
| 1 | primary text bytes | opaque; encoding not established |
| 2 | alternate/gender bytes | opaque; encoding not established |
| 3 | empty = NPC; nonzero = player response | a nonempty zero rejects; structural role, not runtime selection |
| 4 | condition opcodes | printable ASCII |
| 5 | signed decimal response value; empty parses as zero in the source | positive direct, zero/empty exit, negative special |
| 6 | action opcodes | printable ASCII |

The probe's safety limits (stored/decoded DAT entry 1 MiB, payload 512 KiB,
10,000 records, exactly seven fields, 4 KiB per field) are policy, not DLG
facts; the reader's own defaults are above. Synthetic forms:
`{100}{synthetic-npc}{}{ }{}{200}{}` and `{200}{synthetic-player}{}{5}{}{0}{}`.

References: the original's dialog check (0x418030) treats a positive player
response as a target record and reports a missing target or a target with the
wrong structural role; its response classification (0x417590) separates
positive, zero and negative values (source reading, F0, not checked in the
executable). The research probe classified references as `direct`, `exit`,
`special`, `dangling`, `ambiguous` or `unresolved` and never turned a missing
edge into a direct branch. Its bounded traversal started at the lowest numeric
NPC record (a deterministic convenience, not the runtime entry; first-record
order is never entry evidence), counted nodes/references before appending, and
stopped at the first exit, special/generated transition, callback,
uninterpreted condition/action, dangling target, cycle or cap (256 nodes, 1,024
references); with no operation evaluator every nonempty condition/action was an
unsupported boundary (`SC` callback-shaped). Without such a stop it chose the
smallest positive target ID, then the smallest response ID.

ARC-RES-006 selected semantic slice (identifier-only, from `dlg\01478Erland.dlg`,
ordinal 220 in `arcanum3.dat`; fingerprints in
[dialog-evidence.md](../research/dialog-evidence.md); no claim about identity,
invocation, entry, precedence or a runtime branch): NPC `N30` has action
`QU(questId, questState)`; its immediate player row `P31` has condition
`LF(localFlagIndex, expectedValue)`, action `LF(localFlagIndex, newValue)` and a
positive response whose checked target is NPC `N50`, whose action is
`RU(rumorId)`. Bounded admission for the proposal: local-flag indices `0..31`,
LF values `0..1`, QU state `0..6`, RU ids `>=1000` (source-backed domain). A
positive `P31` response is `Direct(N50)` only after target existence and NPC
role validation; zero stays `exit`, negative stays `special` (neither is in this
slice). The probe's `--semantic-slice NPC_ID PLAYER_ID TARGET_NPC_ID` form
validated such a slice fail-closed (unknown/malformed/out-of-domain operations,
missing or wrong-role nodes, invalid targets), without state mutation.

Readiness of that research subset: the physical reader is ready for the bounded
byte-preserving subset (synthetic data plus one opt-in hashed sample, no
encoding or localization verdict); the LF/QU/RU/direct slice is project design
plus synthetic evidence, not original-runtime execution. Unknown: runtime
entry and invocation, dialogue identity, archive precedence, generated dialogue,
the SCR bridge, text encoding beyond the sampled ASCII observation, localization
and text substitution.

## Original read mode (ARC-DATA-017)

`DlgReaderOptions.Mode = DlgReadMode.Original` reads a file the way the original's
internal dialog load (0x417720) → entry parse (0x417860) → field parse (0x417C20) does; the
default `Strict` mode above is unchanged. Since ARC-SCRIPT-012 the universal script
and dialog loader ([script content](../scripting/script-content.md)) reads game data
in this mode, as the game does, and the conversation runtime receives those
documents; the strict mode stays a data-quality check, the reader for project
fixtures and what the research probes and the superseded NPC entry resolver use.

- Fields flow: each field is found by skipping to the next `{`, whatever lies
  between (white space, line breaks, stray text); a `//` pair outside a field
  starts a comment to the end of its line. As in the original, line breaks and a
  stray `}` do not reset the pair detection, so `/`, line break, `/` is a comment
  too. Non-white-space bytes skipped this way, other than comments, are counted
  in `DlgDocument.SkippedBytes`.
- A field runs to the next `}`; an inner `{` is part of its text. Field bytes are
  kept as stored: the original reads in text mode and sees CR LF as LF, so a
  consumer that shows text must do the same (2 installed fields contain CR LF).
- Seven fields make a record regardless of line breaks. Id, IQ and response
  are read like C `atoi` (leading digits, else 0; out-of-range values saturate).
- Reading stops, keeping the records before, where the original stops:
  input ending inside a record (`IncompleteRecord`, that record dropped), a
  non-empty IQ that reads as 0 (`ZeroIq`), or a response starting with `#`
  (`HashResponse`). `DlgDocument.StopReason` reports it; a clean end is
  `EndOfInput`. An empty file reads as zero records.
- Repeated ids are kept and counted in `DuplicateIds`; `TryGetRecord` returns
  the first (the original's sorted lookup may find either).
- IQ is checked as soon as its field is read and the response as soon as its
  field is read, so a stop is reported even if the input ends right after.
- NUL bytes and the input, field and record limits still refuse. Two known
  divergences: the original cuts a field at 999 characters while this reader
  keeps up to `MaxFieldBytes` (4 KiB) and refuses longer ones (the longest
  installed field is 448 bytes), and the original drops the rest of a line after
  a NUL while this reader refuses it.

Installed original (opt-in `DlgOriginalModeTests`, `DAT_ROOT`): all 709 DLG
files read. The 671 the strict mode accepts read identically; of the 38 it
refuses, 4 hold a single record that lacks its last field, so the original
loads nothing from them either (they read as `IncompleteRecord` with zero
records); the test asserts these counts, one file with skipped bytes and no
repeated ids.

### The 38 strict refusals (ARC-SCRIPT-012)

Measured on the installed original with a local probe (structure only; the
counts are not asserted beyond the totals above):

| Cause | Files | What the game does |
| --- | ---: | --- |
| one space between two fields of a record | 28 | skips it |
| tabs between records | 3 | skips them |
| `//` comments and 53 bytes of stray text between records | 1 | skips them |
| a number field that is not a plain number | 2 | reads it like `atoi` |
| one record that lacks its 7th field | 4 | keeps no record and does not open the file |

Checked in the 1.0.7.4 executable: the dialog load (0x412E10)
fails when no record was read; the field parse (0x417C20) skips to the next
`{`, honours `//` and caps a field at 999 characters; the entry parse
(0x417860) reads seven fields, the id, IQ and response with `atoi`, and ends the
read (keeping the records before) at a non-empty IQ that reads 0 or a response
starting with `#`.

All 38 are the DLGs of their scripts (named after the script). 31 are not
conversations but text for the script actions float line (action type 23) and print line
(action type 24): altars, graves, stones, levers, object examination. 7 open a conversation
(dialog action 4). 31 of the 38 scripts are attached on the module's maps, most
on the world map.

In three cases (six files) the game itself loses content (data bugs). Since ARC-DATA-022 the
Steamwoven rule mode fixes them ([data fixes](data-fixes.md), DF1..DF3); the Original mode reads
them as the game does:

- `03042`..`03045` (`*_stone_exa`, four stones on one Tulla map): the only record
  lacks its effects field, so the game opens no file and examining a stone prints
  nothing.
- `01306benjamin_gershwin` record 315: the effects are written in the response
  field of an NPC line and the effects field is empty, so the game never applies
  them.
- `01009mayor_blackroot` record 361: a player reply's response field holds text,
  which `atoi` reads as 0, so the reply ends the conversation.

`01957game_of_witt` has 154 player replies whose IQ field reads `N, -N`; the game
takes the first number as the IQ. The second number's meaning is unknown.

## Explicit exclusions

This reader does not implement a dialogue interpreter/runtime, writer, text
codepage decoding, SCR/generated dialogue support, archive discovery or
precedence, conversation invocation/entry selection, game services, or UI.

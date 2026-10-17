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
status aggregates. Physical grammar evidence is attributed to [bounded DLG
format research](dlg.md), the [dialogue evidence
record](../research/dialog-evidence.md), and CE revision
`a7ff41b300ef712f0e7d088183a3d08957110cdd`; this implementation is
independently authored and does not copy or execute that source or the research
probe. The [minimal dialog contract](../scripting/dialog-minimal-contract.md)
is a separate proposal, not part of this physical reader.

## Original read mode (ARC-DATA-017)

`DlgReaderOptions.Mode = DlgReadMode.Original` reads a file the way the pinned CE
`dialog_load_internal` → `dialog_parse_entry` → `dialog_parse_field` does; the
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

Checked in the 1.0.7.4 executable, not only in CE: `dialog_load` (0x412E10)
fails when no record was read; `dialog_parse_field` (0x417C20) skips to the next
`{`, honours `//` and caps a field at 999 characters; `dialog_parse_entry`
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

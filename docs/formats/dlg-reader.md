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
status aggregates. Physical grammar evidence is attributed to repository
research and CE revision `a7ff41b300ef712f0e7d088183a3d08957110cdd`; this
implementation is independently authored and does not copy or execute that
source or the research probe.

## Explicit exclusions

This reader does not implement a dialogue interpreter/runtime, writer, text
codepage decoding, SCR/generated dialogue support, archive discovery or
precedence, conversation invocation/entry selection, game services, or UI.

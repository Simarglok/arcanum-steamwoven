# Schematics and tech texts

Status: **Implemented, bounded**, work item ARC-TECHNOLOGY-001 (2026-10-20), slice TECH-1 of the
[technology research](../research/technology-evidence.md#proposed-implementation-slices-project-policy-not-accepted).
A Godot-free reader in `Arcanum.Data` of the data the original's tech module and schematic window load at their init:
the texts of `mes\tech.mes` and the schematic records of `rules\schematic.mes` with the texts of
`mes\schematic_text.mes`. It applies no technology rule and changes no session, snapshot, viewer or rule mode.

Requirements: [FR-ITEM-017](../requirements/items-economy.md#fr-item-017) (found and learned schematics) and
[FR-ITEM-018](../requirements/items-economy.md#fr-item-018) (assembly), their data part.

Every behaviour below was read in the retail 1.0.7.4 `Arcanum.exe` by two independent readers: the first reading of
ARC-RES-062 ([technology study](../research/technology-evidence.md#schematic-records-fr-item-017-fr-item-018)) and a
second reading for this slice ([ledger "Schematic data"](../research/original-verification-ledger.md#schematic-data-arc-technology-001),
rows SD1–SD6 and TC11). The registry rows ([functions](../verification/functions.csv)) are at F2.

Code: [`src/Arcanum.Data/Technology`](../../src/Arcanum.Data/Technology); tests:
[`tests/Arcanum.Data.Tests/Technology`](../../tests/Arcanum.Data.Tests/Technology).

## API

- `TechTexts.Open(stack, options)` reads `mes\tech.mes`; `TechTexts.Read(file)` reads a parsed table.
  `DisciplineNames`, `DegreeNames`, `DisciplineDescriptions` (eight each), `DegreeDescription(discipline, degree)`,
  `AlreadyKnown` (88), `Learned` (89), `Missing` (the numbers of 0..89 the file lacks).
- `SchematicCatalog.Open(stack, options)` reads `rules\schematic.mes`, then `mes\schematic_text.mes`;
  `SchematicCatalog.Create(rules, texts)` builds the same from parsed tables (synthetic data or another loader).
- `SchematicCatalog`: `Records` (every record holding an entry), `Read(number)` (any number, as the original reads it),
  `Text(number)` and `TextCount` (`schematic_text.mes`), `LearnedCount`, `LearnedStarts` (the full list), `Learned(d)`,
  `Discipline(number, prototypeDiscipline)`, `Select(list, shown, discipline, page, prototypeDiscipline)`,
  `SelectLearned(degrees, discipline, page)`, `RecordToShow(selected, discipline)`, `Unread`; static `KindOf(number)`,
  `PlaceholderNumber(d)`, `LearnedNumber(d, degree)`, `ParseAlternatives(line)`.
- `SchematicRecord` (immutable): `Number`, `Kind` (`Learned`, `Found`, `Placeholder`, `Other`), `NameText`,
  `DescriptionText`, `Art`, `Component1`, `Component2`, `Product` (`SchematicAlternatives`), `Quantity`,
  `MissingLines` (per `SchematicLine`), `IsComplete`. `SchematicAlternatives`: `Bases` (three or none), `Tokens`
  (0..3), `Prototypes` (base + 20), `DistinctBases`, `IsEmpty`.
- Results are typed: `TechTextsLoad`, `SchematicCatalogLoad`, `SchematicRecordRead`, `SchematicAlternativesParse` with a
  `TechnologyDataStatus` and a `TechnologyDataRefusal` naming the file, the message number and a token. No exception at
  the boundary except for null arguments and out-of-range disciplines, degrees or pages, which are caller errors.
- Universal: nothing names a map, a location or a module. No game text is decoded beyond Latin-1 characters of the
  stored bytes.

## Files

All three are init files: each comes from the **first base source** holding it (`ContentLoadPhase.GameStart`,
[load phases](../research/original-verification-ledger.md#load-phases-arc-data-027)); a module copy is never read.

| File | Read | Required |
| --- | --- | --- |
| `mes\tech.mes` | 0..7 discipline names, 8..15 degree names, 16..23 discipline descriptions, `24 + discipline + 8 × degree` the 64 degree descriptions, 88 and 89 the registration messages | the file; a missing message is `null` (the original keeps its shared missing-line text, which the host formats) |
| `rules\schematic.mes` | the records below | the file |
| `mes\schematic_text.mes` | the record names and descriptions by number, and the window texts 0..5 | the file: without it the original's init unloads `rules\schematic.mes` and fails |

A missing file is `FileMissing`, an unreadable one `FileUnreadable`, a file without messages `FileEmpty` (the
original's loader fails on it), one the [MES grammar](mes-reader.md) refuses `FileRefused`. The init reads only 0..87
of `tech.mes`; 88 and 89 are read by the schematic registration and are kept here with the other texts.

## Records

A record is the seven entries **n..n+6** of `rules\schematic.mes`:

| Offset | Field | Read |
| --- | --- | --- |
| +0 | `NameText`: message number in `schematic_text.mes` | C `atoi` |
| +1 | `DescriptionText` | `atoi` |
| +2 | `Art` | `atoi` |
| +3 | `Component1` | list parse |
| +4 | `Component2` | list parse |
| +5 | `Product` | list parse |
| +6 | `Quantity`: how many products assembly creates | `atoi` |

**List parse.** The line is split at single spaces as C `strtok` does (runs of spaces and leading or trailing spaces
make no token; a tab is part of a token) and each token goes through `atoi`. One or two tokens repeat the last value to
fill three; three are kept; no token leaves the list empty (`IsEmpty`; the original leaves the field unwritten). Each
value is a **base** prototype number; the prototype is the base + 20. More than three tokens are `TooManyTokens`, a line
of 2,000 bytes or more `LineTooLong` (TC6, below); a name, description, art or quantity line is a plain number, so
extra words there are read by `atoi` and ignored.

**Missing entry.** A missing entry reads as the original's missing-line text, whose number is 0: a number field is 0
and a list three zeros (`Tokens` 3), and `MissingLines` flags it. `Read(number)` of a number without any entry gives
all zeros with every line flagged, as the original reads it.

**Ranges** (`KindOf`):

| Range | Kind |
| --- | --- |
| below 2000 | `Other` |
| 2000..3999 | `Learned` |
| `6000 + 10 × d`, d 0..7 | `Placeholder`: the record shown for an empty tab of discipline d |
| 4000 and up, other than a placeholder | `Found` |

The catalogue parses every record whose number n (n mod 10 = 0) holds at least one entry when it is created, so a bad
list line refuses the whole catalogue; entries at offsets 7..9 belong to no record and are listed in `Unread`.

## Learned list, discipline and selection

- **Learned count**: the entries in 2000..3999 divided by 7 (C division), counted only when entry 2000 itself exists
  (otherwise 0).
- **Full list** (`LearnedStarts`): for a count above 0, each of 2000, 2010, ... below 3999 whose entry exists, in order;
  empty for a count of 0. Its length must equal the count and no start may be at 3600 or above (TC11, below).
- **Discipline** (`Discipline`): 4000 and up, the discipline field of the first product alternative's prototype (base +
  20), which the caller's `prototypeDiscipline` gives; 2000..3999, `(n − 2000) / 200`; otherwise, negative numbers
  included, 0. Null when the lookup does not know the prototype or the product line is empty.
- **Selection** (`Select`): the tab's count for the discipline (`shown`: the crafter's stored degree on the learned
  tab, the found count on the found tab) of 0 gives −1; otherwise the page-th number (from 0) of the list whose
  discipline matches, else −1. The window keeps the page below the count; the selection does not compare them.
  `SelectLearned(degrees, d, page)` is the learned tab: the full list with the stored degree as the count.
- **Shown record** (`RecordToShow`): the selection, or for −1 the placeholder `6000 + 10 × discipline`.
- **Learned number** (`LearnedNumber`): `1990 + 200 × discipline + 10 × degree` for degree 1..7 (the editor's tooltips);
  on the installed data it equals the degree-th learned record of each discipline.

## Differences from the original

Owner rule of 2026-10-17: the Original mode never reproduces an out-of-bounds access, so these are fixed in both rule
modes ([rule modes](../world/rule-modes.md#registry), Technology row) and need no switch.

| Original 1.0.7.4 | Steamwoven (both modes) |
| --- | --- |
| TC6: the list parse copies the line into a 2,000-byte buffer without a length check and writes every token, more than three past the field and the record. | `TooManyTokens` / `LineTooLong` refuse the catalogue. The MES grammar already refuses a text of 2,000 bytes or more (`FileRefused`). |
| A missing list entry is the six-token missing-line text, so the parse writes three values past the field (the components' are rewritten by the next line, the product's reach two integers past the record). | The three zeros are kept; the rest is dropped. In the one corner where the next list line has no token, the original keeps (N, 0, 0) there and Steamwoven an empty list. |
| TC11: the full list has "count" slots but is filled with every existing start without a bound and walked to the count; a start at 3600 or above counts past the eight-entry discipline table. | `LearnedListMismatch` / `LearnedDisciplineOutOfRange` refuse the catalogue. |
| An empty list line leaves the field unwritten (earlier stack contents). | An empty `SchematicAlternatives`; `Discipline` gives null for an empty product line. |
| A missing text is the shared missing-line text. | `null`; the host formats it. |

## Installed original

`SchematicRealDataTests` (`DAT_ROOT`, category `RealData`) checks the [data census](../research/technology-evidence.md#data-census):
`tech.mes` holds all 90 entries; `rules\schematic.mes` (from `Arcanum4.dat`) has 882 entries in 126 complete records,
none unread: 56 learned (7 per discipline, the k-th at `1990 + 200d + 10k`, `LearnedCount` 56), 62 found (4000..5440)
and 8 placeholders; every list has 1 or 3 alternatives; every name and description exists in the 258-entry
`schematic_text.mes`; every quantity is at least 1. No refusal is reached by the installed data.

## Not ready

- No item discipline reader: the caller supplies the prototype → discipline lookup (field 0x61) for found records.
- No degrees or expertise (TECH-2), no found list, no registration from a schematic document and no reading of the
  document's schematic number (TECH-3, which also implements TC8), no component gate or assembly (TECH-4, TC3–TC5), no
  window, tabs, pages, button texts or sounds (TECH-5, which settles the button codes O7).
- The window's per-discipline found counts (0x56D130) are the caller's `shown`; the open function is not implemented.

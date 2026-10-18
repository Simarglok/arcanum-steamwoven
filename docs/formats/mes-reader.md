# MES messages and object display names

ARC-DATA-013. `Arcanum.Data.Messages` reads original MES message tables without
decoding them, loads the three description tables through the
[content stack](../research/content-precedence.md), and selects the name the
original shows for an object (FR-ITEM-008, FR-CONTENT-006). It is
Godot-independent and read-only. Independently authored from the original's
behaviour cited below (by executable address).

**Status:** the reader, catalog and resolver are merged with synthetic tests and
one opt-in real-data check. The AREA viewer's hover tooltip
([ARC-UI-005](../rendering/terrain-viewer.md#hover-tooltip-and-action-cursor-arc-ui-005)) is the
first consumer. **NOT READY:** the conversation and session do not call it, there is no
examine panel, no "met before" recognition (the hover shows every NPC unmet), no
identification state change, no item effect lines, no quantity text and no
localization other than the declared byte profiles below.

## API

```csharp
var load = ObjectDescriptionCatalog.Load(stack);              // ContentStack from ARC-DATA-011
var name = ObjectDisplayName.Resolve(areaObject, load.Catalog!, CritterRecognition.Recognized);
if (name.Status == ObjectNameStatus.Resolved)
{
    var text = MesText.Decode(name.Message!, MesTextProfile.Windows1252);
}
```

- `MesFile.Parse(bytes, MesReaderOptions?)` → `MesParseResult(Status, File, Line, Number)`.
  `MesFile` is immutable, owns a copy of the payload, orders messages by number
  and offers `Find(number)`, `Count` and `LastNumber`. `MesMessage.Bytes` is the
  text field as the original reads it: the stored bytes with each CR LF read as LF,
  nothing else changed (no decoding or trimming). `MesMessage` equality compares
  number, line and bytes.
- `MesText.Decode(bytes | message, MesTextProfile)` → `MesDecodeResult`. Profiles are
  `Ascii`, `Windows1252` and `Windows1251`; the caller always declares one. A byte the
  profile does not define fails with its offset (`UndefinedByte`); nothing is
  replaced.
- `ObjectDescriptionCatalog.Load(ContentStack, MesReaderOptions?)` reads
  `mes/description.mes` (required, first copy in a base source, see below) and the
  content-stack winners of `mes/gamedesc.mes` and `mes/gamekey.mes` (both optional),
  and records the `ContentCandidate` each came from. A file without messages counts
  as not loaded. Read and parse refusals are typed (`ReadFailed` keeps the
  `ContentReadStatus`). `Create(...)` builds one from parsed tables. `Find(number)` and
  `FindKey(keyId)` return `DescriptionLookup(Status, Number, File, Message)` with
  `Found`, `OutOfRange`, `ModuleFileAbsent` or `NotFound`.
- `ObjectDisplayName.Resolve(AreaObjectRecord | type + ObjectFieldReader, catalog,
  CritterRecognition)` → `ObjectNameResolution(Status, Source, Field, Number, Lookup)`
  with `Resolved`, `NoText` (the original shows an empty name), `FieldUnresolved`,
  `PlayerNameNotModelled` or `UnsupportedType`.

Defaults: 4 MiB payload, 65,536 messages, 1,999 text bytes. The text limit cannot
be raised above 1,999.

## MES grammar as the original reads it

ARC-DATA-027: this is the one MES grammar of the data readers. Besides the description
tables, `MesTable` (number → text, Latin-1 characters of the stored bytes) feeds the lighting
rules and scheme files, the terrain tile and facade name tables (`TerrainLoader`,
`TerrainObstructionInspector`), the T0 step rules (tile names, effects, game effects) and the area
owner's name tables; each keeps its own limits and failure type.

The original loader (load (0x4D4920),
entry and field (0x4D4A60, 0x4D4B50),
character reader (0x4D4CA0),
search (0x4D45E0))
skips everything up to a `{`, takes the bytes up to the next `}` as the number,
does the same for the text, and repeats until end of file. Comments are therefore
simply text outside braces, a stray `}` there is ignored, and the two fields may be
on different lines. Messages are sorted by number and found by binary search.
Archive members are opened in text mode, which reads CR LF as LF
(archive text mode);
the original Windows executable reads loose files in text mode the same way.

Accepted as the original does: any text outside braces, any order, empty text,
multi-line text (LF kept, CR LF read as LF), leading zeros in the number. Refused
(typed status), because the original's result would be a silent truncation, depend
on platform or file origin, keep a shape it warns about, or be arbitrary:

| Status | Shape | Original behaviour |
| --- | --- | --- |
| `UnterminatedField` / `MissingText` | a field without `}`, a number without text | parsing stops; earlier messages are kept |
| `UnsupportedNumber` | anything but 1..10 ASCII digits within int32 | `atoi`; a warning only for a leading non-digit, trailing garbage is dropped silently |
| `BraceInField` | `{` inside a field | warning; the brace is kept |
| `LoneCarriageReturn` | a CR not followed by LF | archive text mode drops it and repeats the next character |
| `TextTooLong` | text over 1,999 bytes | copied into a 2,000-byte buffer; longer text is cut and the terminator overflows |
| `NulByte` | a NUL byte | the line reader treats it as end of line or file |
| `EndOfFileByte` | a 0xFF byte, or 0x1A | the character reader returns a plain `char`; where `char` is signed, 0xFF equals end of file (0xFF is `я` in Windows-1251, `ÿ` in Windows-1252). 0x1A ends a loose file in Windows text mode |
| `DuplicateNumber` | one number twice | warning; the binary search may return either |

Installed original: 253 of the 254 MES copies in the archives parse; the one
refusal is the module's `semes/globalflags.mes` (`DuplicateNumber`), which no
caller here reads.

## Encoding

The original stores bytes and draws them with its fonts; nothing in the reader
sets an encoding. Steamwoven therefore never guesses: text is kept as bytes and
decoded only under a profile the caller names. The NPC conversation already
declares Windows-1252 for the English data. The installed English description
tables are pure ASCII (checked below), so all three profiles give the same text
for them. Windows-1252 leaves 0x81, 0x8D, 0x8F, 0x90 and 0x9D undefined and
Windows-1251 leaves 0x98 undefined; both are strict tables in
[`MesText`](../../src/Arcanum.Data/Messages/MesText.cs). Which profile a localized
release needs is the caller's decision and is NOT_VERIFIED here.

**NOT READY for Cyrillic files containing `я`:** that letter is byte 0xFF in
Windows-1251, so `MesFile.Parse` refuses such a file as `EndOfFileByte`. The
end-of-file reading is inferred from the source (signed `char`);
whether a localized original executable reads 0xFF differently is NOT_VERIFIED.

## Description tables

The description functions (0x4D40D0, 0x4D4150, 0x4D4160, 0x4D41D0, 0x4D4210, 0x4D4260):
`mes\description.mes` is required at start-up; the module's `mes\gamedesc.mes` and
`mes\gamekey.mes` are optional, and a file without messages counts as not loaded
(the MES load (0x4D4290)).
The base file is loaded while only the base sources are mounted (`arcanum*.dat`
and `data/` over the game folder and `tig.dat`; the module's `.dat`, patches and
folder come later with the module,
start-up (0x404A20))
and stays loaded, so the catalog takes the first copy in a base source and never
a module copy. The two module files are the content-stack winners. A description number below 30000 is looked up in
the base file, from 30000 up in the module file. Any number below 0 or above the
**accepted maximum** returns nothing. The maximum is the last (highest) number of
the module file when that file is loaded and has messages, otherwise of the base
file; it is not the larger of the two. Key names have no range check and come only
from `gamekey.mes`; with no key file loaded the original would search an invalid
handle, which the catalog reports as `ModuleFileAbsent`.

## Which name is shown

The examine function (0x441B60)
picks the source by type, in this order:

| Object | Field (effective slot) | Table |
| --- | --- | --- |
| key (type 11) | `OBJ_F_KEY_KEY_ID` (59) | `gamekey.mes` |
| item (types 5..14) not identified: `OBJ_F_ITEM_MAGIC_TECH_COMPLEXITY` (45) > 0 and no `OIF_IDENTIFIED` (0x1) in `OBJ_F_ITEM_FLAGS` (36) (identified (0x464370)) | `OBJ_F_ITEM_DESCRIPTION_UNKNOWN` (47) | description |
| player character (15) | `OBJ_F_PC_PLAYER_NAME` string | none: `PlayerNameNotModelled` |
| NPC (16) the observer does not recognize (recognition (0x45F970)) | `OBJ_F_CRITTER_DESCRIPTION_UNKNOWN` (58) | description |
| everything else, including identified items and recognized NPCs | `OBJ_F_DESCRIPTION` (22) | description |

An NPC is recognized when the observer is the NPC itself or has met it; that is
reaction state, so the caller supplies `CritterRecognition`. The editor-only branch
is not modelled. Only the fields of the selected branch are read, as in the
original; a field without an int32 value is `FieldUnresolved` with its slot. Types
above trap (17) are `UnsupportedType`.

`OBJ_F_NAME` (21) is **not** looked up. It is a numeric name class that prototypes
set from a per-type base or a specific number and that name comparisons such as
dialog `IN` use ([RES-023 §3](../research/crash-site-item-inventory.md#3-display-names-s--d)).
Its internal-name table (`oemes\oname.mes`) has no established caller chain and is
out of scope.

## Original-data check

Opt-in test `ObjectDisplayNameRealDataTests` (`TestCategory("RealData")`) needs
`DAT_ROOT` (the game folder) and the Crash Site inputs `ARC_AREA_MODULE`,
`ARC_AREA_RESOURCE1`, `ARC_AREA_RESOURCE2`, `ARC_AREA_MOBILE`, `ARC_AREA_PROTOTYPES`;
a missing input fails. It compares counts, numbers and digests only.

| File | Winner | Bytes | Messages | Numbers |
| --- | --- | ---: | ---: | --- |
| `mes/description.mes` | `arcanum3.dat` | 39,124 | 1,448 | 1000..29723 |
| `mes/gamedesc.mes` | `modules/Arcanum.dat` | 34,179 | 1,152 | 30000..31213 |
| `mes/gamekey.mes` | `modules/Arcanum.dat` | 3,258 | 69 | 0..68 |

All three parse whole with none of the refusals above, all text decodes as ASCII,
and the accepted maximum is 31213. All 52 Crash Site core items (37 on the ground,
15 in core NPC inventories) resolve through `OBJ_F_DESCRIPTION`: 47 in the base
file and 5 in the module file, including the ring (name class 2804). Every core NPC
resolves both recognized and not recognized. This matches the RES-023 probe.

```sh
G=/path/to/Arcanum
DAT_ROOT=$G ARC_AREA_MODULE=$G/modules/Arcanum.dat ARC_AREA_RESOURCE1=$G/arcanum1.dat \
ARC_AREA_RESOURCE2=$G/arcanum2.dat ARC_AREA_MOBILE=$G/modules/Arcanum/maps/<exterior aggregate> \
ARC_AREA_PROTOTYPES=$G/data/proto \
dotnet test tests/Arcanum.Data.Tests --filter "FullyQualifiedName~ObjectDisplayNameRealData"
```

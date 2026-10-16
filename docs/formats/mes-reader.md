# MES messages and object display names

ARC-DATA-013. `Arcanum.Data.Messages` reads original MES message tables without
decoding them, loads the three description tables through the
[content stack](../research/content-precedence.md), and selects the name the
original shows for an object (FR-ITEM-008, FR-CONTENT-006). It is
Godot-independent and read-only. Independently authored from the pinned CE
behaviour cited below; no CE code or structure is reused.

**Status:** the reader, catalog and resolver are merged with synthetic tests and
one opt-in real-data check. **NOT READY:** no consumer is wired yet (the AREA
viewer, conversation and session do not call it), there is no examine/hover UI, no
identification state change, no item effect lines, no quantity text and no
localization other than the declared byte profiles below.

## API

```csharp
var load = ObjectDescriptionCatalog.Load(stack);              // ContentStack from ARC-DATA-011
var name = ObjectDisplayName.Resolve(areaObject, load.Catalog!, CritterRecognition.Recognized);
if (name.Status == ObjectNameStatus.Resolved)
    var text = MesText.Decode(name.Message!, MesTextProfile.Windows1252);
```

- `MesFile.Parse(bytes, MesReaderOptions?)` → `MesParseResult(Status, File, Line, Number)`.
  `MesFile` is immutable, owns a copy of the payload, orders messages by number
  and offers `Find(number)`, `Count` and `LastNumber`. `MesMessage.Bytes` is the
  stored text field exactly as it is: no decoding, trimming or line-ending change.
- `MesText.Decode(bytes | message, MesTextProfile)` → `MesDecodeResult`. Profiles are
  `Ascii`, `Windows1252` and `Windows1251`; the caller always declares one. A byte the
  profile does not define fails with its offset (`UndefinedByte`); nothing is
  replaced.
- `ObjectDescriptionCatalog.Load(ContentStack, MesReaderOptions?)` reads the winning
  copies of `mes/description.mes` (required), `mes/gamedesc.mes` and
  `mes/gamekey.mes` (both optional) and records the `ContentCandidate` each came
  from. `Create(...)` builds one from parsed tables. `Find(number)` and
  `FindKey(keyId)` return `DescriptionLookup(Status, Number, File, Message)` with
  `Found`, `OutOfRange`, `ModuleFileAbsent` or `NotFound`.
- `ObjectDisplayName.Resolve(AreaObjectRecord | type + ObjectFieldReader, catalog,
  CritterRecognition)` → `ObjectNameResolution(Status, Source, Field, Number, Lookup)`
  with `Resolved`, `NoText` (the original shows an empty name), `FieldUnresolved`,
  `PlayerNameNotModelled` or `UnsupportedType`.

Defaults: 4 MiB payload, 65,536 messages, 1,999 text bytes. The text limit cannot
be raised above 1,999.

## MES grammar as the original reads it

The original loader ([load](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mes.c#L455-L509),
[entry and field](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mes.c#L516-L618),
[character reader](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mes.c#L647-L665),
[search](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mes.c#L287-L305))
skips everything up to a `{`, takes the bytes up to the next `}` as the number,
does the same for the text, and repeats until end of file. Comments are therefore
simply text outside braces, a stray `}` there is ignored, and the two fields may be
on different lines. Messages are sorted by number and found by binary search.

Accepted as the original does: any text outside braces, any order, empty text,
leading zeros in the number. Refused (typed status), because the original's
result would be a silent truncation, a warning-only guess or platform-dependent:

| Status | Shape | Original behaviour |
| --- | --- | --- |
| `UnterminatedField` / `MissingText` | a field without `}`, a number without text | parsing stops; earlier messages are kept |
| `UnsupportedNumber` | anything but 1..10 ASCII digits within int32 | warning, then `atoi` |
| `BraceInField` | `{` inside a field | warning; the brace is kept |
| `LineBreakInText` | CR or LF inside text | CR survival depends on how the file is opened (archive or loose, platform) |
| `TextTooLong` | text over 1,999 bytes | copied into a 2,000-byte buffer; longer text is cut and the terminator overflows |
| `NulByte` | a NUL byte | the line reader treats it as end of line or file |
| `EndOfFileByte` | a 0xFF byte | the character reader returns a plain `char`; where `char` is signed, 0xFF equals end of file. Note: 0xFF is `я` in Windows-1251 and `ÿ` in Windows-1252 |
| `DuplicateNumber` | one number twice | warning; the binary search may return either |

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

## Description tables

[`description.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/description.c#L38-L165):
`mes\description.mes` is required at start-up; the module's `mes\gamedesc.mes` and
`mes\gamekey.mes` are optional. A description number below 30000 is looked up in
the base file, from 30000 up in the module file. Any number below 0 or above the
**accepted maximum** returns nothing. The maximum is the last (highest) number of
the module file when that file is loaded and has messages, otherwise of the base
file; it is not the larger of the two. Key names have no range check and come only
from `gamekey.mes`; with no key file loaded the original would search an invalid
handle, which the catalog reports as `ModuleFileAbsent`.

## Which name is shown

[`object_examine`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L3934-L3992)
picks the source by type, in this order:

| Object | Field (effective slot) | Table |
| --- | --- | --- |
| key (type 11) | `OBJ_F_KEY_KEY_ID` (59) | `gamekey.mes` |
| item (types 5..14) not identified: `OBJ_F_ITEM_MAGIC_TECH_COMPLEXITY` (45) > 0 and no `OIF_IDENTIFIED` (0x1) in `OBJ_F_ITEM_FLAGS` (36) ([identified](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L2285-L2294)) | `OBJ_F_ITEM_DESCRIPTION_UNKNOWN` (47) | description |
| player character (15) | `OBJ_F_PC_PLAYER_NAME` string | none: `PlayerNameNotModelled` |
| NPC (16) the observer does not recognize ([recognition](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L2267-L2274)) | `OBJ_F_CRITTER_DESCRIPTION_UNKNOWN` (58) | description |
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

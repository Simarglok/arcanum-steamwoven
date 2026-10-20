# Spell records

Status: **Implemented, bounded**, work item ARC-MAGIC-001 (2026-10-20), slice MT1 of the
[magic implementation plan](../research/magic-implementation-plan.md). A Godot-free reader in `Arcanum.Data` of the
spell table the original's magic-tech init loads: lines +0..+4 of the 223 records of `rules\spelllist.mes`, the texts of
`mes\spell.mes` and the 25 component type names of `rules\magictech.mes`. Requirement:
[FR-MAGIC-001](../requirements/combat-magic-tech.md#fr-magic-001) (definitions; the component lines are MT2's). It
applies no magic rule and changes no session, snapshot or viewer.

Every behaviour below was read in the retail 1.0.7.4 `Arcanum.exe` by two independent readers
([ledger "Spell records"](../research/original-verification-ledger.md#spell-records-arc-magic-001), rows SR1–SR16);
where it differs from the earlier reading in the [load map](../research/combat-domain-load-map.md#42-spell-records) or the
[magic core definitions](../research/magic-core-evidence.md#definitions-fr-magic-001), the ledger is the newer evidence.

Code: [`src/Arcanum.Data/Magic`](../../src/Arcanum.Data/Magic); tests:
[`tests/Arcanum.Data.Tests/Magic`](../../tests/Arcanum.Data.Tests/Magic).

## API

- `SpellData.Open(stack, mode)` reads the three files from the content stack in the original's init order (component type
  file, text file, component type names, record file). They are init files, so each comes from the **first base source**
  that holds it ([load phases](../research/original-verification-ledger.md#load-phases-arc-data-027),
  `ContentLoadPhase.GameStart`); a module copy is never read. Nothing names a map or a location.
- `SpellData.Create(componentTypes, spellTexts, spellList, mode)` builds the same from parsed MES tables (synthetic data).
- `SpellRecordTable.Read(list, mode)` reads the records alone; `SpellRecordTable.FirstMessage(n)` is record n's first
  message. `SpellTexts.Read(file, mode)` reads the texts alone.
- `SpellNameTables` holds the name tables values are matched against: 32 spell flags (`OSF_*`), 32 critter flags
  (`OCF_*`), 26 item triggers with their values, 23 stat keys, four `Info:` names, the ten `AI_*` keys.
- Results are typed: `SpellDataLoad`, `SpellRecordRead`, `SpellTextsRead` with a `SpellDataStatus` and a
  `SpellDataRefusal` naming the file, the message number and the token. No exception at the boundary except for null
  arguments and an undefined mode.

## Files

| File | Read | Required |
| --- | --- | --- |
| `rules\magictech.mes` | messages 10..34: the `Type:` names of the 25 component types, in type order | the file and each of the 25 messages (the original's init fails otherwise) |
| `mes\spell.mes` | message n: name of record n (0..222); 500 + c: college name (16); 700 + n: description of college spell n (80); 8000 + c: college description; 9000 + c: mastery description | the file; a missing text is the original's shared missing-line text (`null` here, the host formats it); strict mode refuses it |
| `rules\spelllist.mes` | the 223 records below | the file |

A missing file is `FileMissing`, an unreadable one `FileUnreadable`, a file without messages `FileEmpty` (the original's
loader fails on it), one the [MES grammar](mes-reader.md) refuses `FileRefused` (project policy).

## Records

Record n (0..222) starts at message **1000 + 50·n** for n < 140 and **3000 + 50·n** for n 140..222 (2000 more from the
technology records). Records 0..79 are the college spells: college `n / 5`, rank `n % 5 + 1`. Records are read in
number order, each:

1. every field gets its default (below);
2. line +0 (the record's target area) is kept as text; its typed reading is MT2's;
3. line +1 is always read, lines +2 and +3 only when present;
4. the ten AI values are set to −1 and the defensive second value to 0;
5. line +4 is read when present;
6. forced values: the willpower minimum is 0 for every record from 80 (whatever `IQ:` said) and the technology flag is on
   for every record from 140.

A second pass repeats step 6 and collects the component lines: from +5 while the next message exists, at most 44
(`ComponentLines`, as text for MT2). A missing line +0 or +1 is the original's missing-line text, which holds no key, so
the record keeps its defaults.

| Line | Key (in order) | Value | Field | Default |
| --- | --- | --- | --- | --- |
| +1 | `IQ:` | number | `MinimumWillpower` (LM6: the willpower minimum) | {6, 9, 12, 15, 18}[n mod 5]; forced 0 from 80 |
| | `Cost:` | number | `Cost` | 0 |
| | `Maintain:` | pair | `MaintainCost`, `MaintainPeriod` | 0, 0 |
| | `Duration:` | pair | `DurationSeconds`, `DurationStat` | 0, −1 |
| | `DurationStatInfo:` | pair, tried only after a `Duration:` whose second value is not −1 | `DurationStatLevel`, `DurationStatModifier` | 0, 1 |
| | `DurationTriggerCount:` | number, kept only above 0 | `DurationTriggerCount` | 0 |
| | `Resist:` | `(stat@value)`, stat by name | `ResistStat` (index in the stat keys), `ResistModifier` | −1, 0 |
| | `Range:` | number | `Range` | 99 |
| | `ChargesFromCells:` | number, non-zero sets | `ChargesFromCells` | off |
| | `ChargeBeginCost:` | number, **zero** sets (other values change nothing) | `SkipsBeginCost` | off |
| | `Info:` | `neutral`, `friendly`, `aggressive`, `may_be_aggressive` | `Info` (values 0, 1, 2, 3, OR-ed into the flags: friendly 0x1, aggressive 0x2) | neutral |
| | `Disabled:` | number: absent or 0 → enabled | `Enabled` | enabled |
| +2 | `CastingAnim:`, `Missile:` | number | `CastingAnimation`, `Missile` | −1 |
| | `[Begin]Caster:`, `[Begin]Target:`, … `[EndCallback]Target:` (five actions, caster then target) | number | `CasterEyeCandy`, `TargetEyeCandy` by `SpellAction` | −1 |
| | `Is_Tech:` | number, non-zero sets | `IsTechnology` | off; forced from 140 |
| +3 | `No_Stack:` | number, kept as written | `NoStack` | 0 |
| | `Cancels_SF:`, `Cancels_EnvSF:`, `Disallowed_SF:`, `Disallowed_TSF:` | spell-flag list | `CancelsSpellFlags`, `CancelsEnvironmentSpellFlags`, `DisallowedCasterSpellFlags`, `DisallowedTargetSpellFlags` (bit = index) | 0 |
| | `Disallowed_TCF:` | critter-flag list | `DisallowedTargetCritterFlags` (bit = index) | 0 |
| | `ItemTriggers:` | item-trigger list | `ItemTriggers` (OR of the table values; `Item_Used` is 0x3000, which includes `Target_Attacker`'s 0x2000) | 0 |
| +4 | `AI_Flee:`, `AI_Summon:` | number | `AiPriorities[0..1]` | −1 |
| | `AI_Defensive:` | pair | `AiPriorities[2]`, `AiDefensiveSecond` | −1, 0 |
| | `AI_Offensive:` … `AI_Resurrect:` (seven) | number | `AiPriorities[3..9]` | −1 |
| | `No_Resist:`, `No_Reflect:` | number, **by presence** (the value is ignored) | `NoResist`, `NoReflect` | off |

`KeysRead` lists the keys the cursor read on lines +1..+4, `LinesPresent` which of lines +0..+4 exist.

Which stat a `Resist:` index means at run time is the resolver's question: indices 21 and 22 carry the names
`stat_poison_level` and `stat_age` (LM17); no installed record uses them.

## Value grammar

The original's named-key parser, with the comma as value delimiter:

- **Keys.** White space (space, tab, line feed, vertical tab, form feed, carriage return) before a key is skipped. A key
  matches only when it starts exactly at the cursor, case-sensitively, colon included. A key that is not there leaves the
  cursor where it is: a key out of order, a repeated key, an unknown key and everything after it on the line are not read.
- **Value end.** A value runs to the next comma (the cursor then follows it) or to the end of the line (no later key is
  read). Parentheses do not protect a comma.
- **Number.** C `atoi` (`atol` in the executable, the same on Win32): leading white space, one sign, decimal digits up to the first other character, 0 when there are
  none, 32-bit wrap on overflow.
- **Pair** `(a@b)`. After white space the value must start with `(`, otherwise the original exits. a is `atoi` of the text
  after `(`, b of the text after `@`, or a again without `@` (so `Duration:(30)` has stat 30). `)` is never checked.
- **Stat pair** `(name@b)`. The name runs from right after `(` (no white space skipped) to its first space, or to `@`; it is
  compared ASCII case-insensitively with the stat keys in order. Without `@` the closing `)` belongs to the name. A name
  that matches none crashes the original (its 28-entry table has null entries after the 23 names).
- **Name** (`Info:`). The whole value up to the comma, trailing spaces included, compared ASCII case-insensitively; no
  match ends the process.
- **Flag list.** Names separated by a space or `|`, compared ASCII case-insensitively, each OR-ed in. A name ends at
  whichever comes first; when a space comes first and a `|` follows later, the walk resumes after that `|`, so the names
  between are skipped (`A B|C` reads A and C; `A|B C` reads all three). White space after a separator is skipped
  (`A|  B` reads A and B). An unknown or empty name (an empty value, a doubled `|`, only spaces after `|`) ends the
  process; a separator that is the last character of the value (`A ,`, `A|`) crashes it.

## Reader modes

Load-map policy 2. Both modes refuse what ends the original's process: `MissingParenthesis`, `UnknownName`, `UnknownFlag`,
`TrailingSeparator`, and `NonAsciiByte` for a byte above 0x7F on lines +1..+4 (the original's white-space test reads
memory before its character table for one, so its result depends on run-time data; the refusal covers the whole line,
deliberately broader than the places the original's skip meets the byte). A missing component type name is
`MissingComponentName`.

- **Like original** (`SpellRecordMode.LikeOriginal`) reads everything else as the original does: skipped and unknown keys
  are not read, missing lines and texts keep the defaults, numbers are `atoi`.
- **Strict** (`SpellRecordMode.Strict`) also refuses:

| Status | When |
| --- | --- |
| `MissingRequiredLine` | line +0 or +1 of a record is missing |
| `MissingText` | a text of `mes\spell.mes` is missing |
| `UnknownKey` | text is left after the cursor's last key and its first non-empty segment's key is not one of the line's |
| `SkippedKey` | the left text starts with one of the line's keys (out of order, repeated, or a `DurationStatInfo:` after a stat of −1) |
| `InvalidNumber` | a number, alone or in a pair, that is not an optional sign and digits within 32 bits |
| `MalformedPair` | a pair without `@`, with text after `)`, or a stat name followed by more than spaces |
| `AmbiguousFlagList` | a flag list whose names the original reads only in part |
| `IgnoredValue` | a value read and then dropped: a negative `DurationTriggerCount:`, a non-zero `ChargeBeginCost:`, a zero `No_Resist:` or `No_Reflect:`, a non-zero `IQ:` on a record from 80 (forced to 0), `Is_Tech: 0` on a record from 140 (forced technology) |

The first refusal in record order (file, message number, token) is returned; within one line, a fault that ends the
original's process takes priority over a strict refusal of an earlier key. Records are read before the texts of
`mes\spell.mes` (the spell module reads those in a later init), so in strict mode a record refusal comes before a
`MissingText`; the order changes no result. On the installed original both modes read
all 223 records and give the same records.

## Installed original

Opt-in test `SpellRecordRealDataTests` (`DAT_ROOT`), matching the [data census](../research/combat-data-census.md#3-spell-records):
lines +0..+4 present in 223/223/10/160/105 records; no `IQ:`; `Resist:` constitution 22, willpower 12, dexterity 2,
strength 1; `Info:` aggressive 82, friendly 75; `No_Resist:` 2 (technology), `No_Reflect:` 2 (college); per record range
0..79 / 80..139 / 140..222 `Cost:` 80/36/41, `Maintain:` 53/7/6, `Duration:` 2/12/37, `DurationStatInfo:` 0/0/9,
`DurationTriggerCount:` 2/3/6, `Resist:` 26/6/5, `Range:` 0/8/42, `ChargesFromCells:` 0/0/3, `ChargeBeginCost:` 1/0/1,
`Info:` 37/43/77; the ten AI lists 7/16/25/32/7/3/1/3/1/2; component lines 0 / 1..4 / 5..9 / 10..13 in 5/123/86/9 records;
strict mode reads it, so no unknown or skipped key. `AutoLevelTables.ReadSpellWillpower` (unchanged) gives the same
willpower minima as the records.

## Differences from the original

Owner decision 2026-10-20: the original's bugs in this parse are fixed as proposed, and every difference is documented
here ([anomaly decisions](../research/npc-combat-ai-plan.md#anomaly-decisions) SR12–SR14). None is a rule-mode switch:
the reader modes cover them, and no installed record reaches any of them.

| Original 1.0.7.4 | Steamwoven | Mode |
| --- | --- | --- |
| A pair without `(`, an unknown `Info:` name or an unknown or empty flag name logs and exits the process (SR11–SR13). | The spell data is refused with a typed reason naming file, message and token. | both |
| A `Resist:` stat name that matches no key crashes on the key table's null entries (SR12). | Refused (`UnknownName`). | both |
| A flag-list separator as the last character of the value crashes (SR13). | Refused (`TrailingSeparator`). | both |
| A byte above 0x7F met by the white-space skip is classed by memory before the character table (SR14). | Refused (`NonAsciiByte`) on lines +1..+4. | both |
| A missing file, component type name, or a file the original's loader rejects fails the magic-tech init (SR1). | Refused (`FileMissing`, `MissingComponentName`, `FileEmpty`, `FileUnreadable`); a file the [MES grammar](mes-reader.md) refuses is `FileRefused` (project policy). | both |
| A missing text of `mes\spell.mes` is the shared "Error! Missing line N" text (SR15). | `null`; the host formats the missing text. Strict mode refuses it (`MissingText`). | both / strict |
| A missing line +0 or +1, a key out of order or repeated, an unknown key, a non-numeric or overflowing number, a pair without `@`, a dropped value and a flag list read in part are read silently. | Read the same way in like-original mode; refused in strict mode (statuses under [Reader modes](#reader-modes)). | strict |
| The flag-list parse overwrites separators in the loaded message text and can leave a cut in it. | The text is never changed; each line is parsed once in both programs, so no reading differs. | both |
| Line +0 (target area), the component lines and `Rules\SpellEyeCandy.mes` are parsed by the same init. | Line +0 and the component lines are kept as text for slice MT2; the eye-candy file is not read. | both |

## Not ready

- Line +0 and the component lines are text only: their target areas, filters and the 25 component types are MT2.
- No rule reads the records yet (spell book MT3, cost MT5, resistance MT6, invocation MT7); the auto-level scheme keeps its
  own willpower read.
- The eye-candy file `Rules\SpellEyeCandy.mes` and the 512 run slots of the same init are not read here.

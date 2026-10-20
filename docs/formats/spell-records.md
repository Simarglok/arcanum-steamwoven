# Spell records

Status: **Implemented, bounded**, work items ARC-MAGIC-001 and ARC-MAGIC-002 (2026-10-20), slices MT1 and MT2 of the
[magic implementation plan](../research/magic-implementation-plan.md). A Godot-free reader in `Arcanum.Data` of the
spell table the original's magic-tech init loads: the 223 records of `rules\spelllist.mes` (lines +0..+4 and the effect
components of lines +5 onward), the texts of `mes\spell.mes` and the 25 component type names of `rules\magictech.mes`.
It applies no magic rule and changes no session, snapshot or viewer; the one rule-mode switch it brings (MG18) is applied
where an area is used.

Requirements: [FR-MAGIC-001](../requirements/combat-magic-tech.md#fr-magic-001) (definitions),
[FR-MAGIC-003](../requirements/combat-magic-tech.md#fr-magic-003) and
[FR-MAGIC-007](../requirements/combat-magic-tech.md#fr-magic-007) (their data part: target areas and effect components).

Every behaviour below was read in the retail 1.0.7.4 `Arcanum.exe` by two independent readers
([ledger "Spell records"](../research/original-verification-ledger.md#spell-records-arc-magic-001), rows SR1–SR16, and
["Spell components"](../research/original-verification-ledger.md#spell-components-arc-magic-002), rows SC1–SC12);
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
- `SpellRecordTable.Read(list, componentTypes, mode)` reads the records alone (`componentTypes`: the 25 `Type:` names);
  `SpellRecordTable.FirstMessage(n)` is record n's first message; `Unread` lists, in like-original mode, the text the
  original never reads (below). `SpellTexts.Read(file, mode)` reads the texts alone.
- `SpellComponentTables` holds the target names and their 64-bit values and the name tables of the component data.
- `Arcanum.Simulation.Magic.SpellTargetRules.Targets(mask, rules)` gives the mask an area tests under a rule setting
  (MG18, [below](#mg18-the-not-dead-critter-target)); `MagicAnomalyProfile` is `GameRules.Magic`.
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
2. line +0, the target areas of the five actions ([below](#target-areas-line-0));
3. line +1 is always read, lines +2 and +3 only when present;
4. the ten AI values are set to −1 and the defensive second value to 0;
5. line +4 is read when present;
6. forced values: the willpower minimum is 0 for every record from 80 (whatever `IQ:` said) and the technology flag is on
   for every record from 140.

A second pass, after every record's first pass, reads the component lines of each record in number order — from +5 while
the next message exists, at most 44 ([below](#components-lines-5-onward)) — and repeats step 6. A missing line +0 or +1
is the original's missing-line text, which holds no key, so the record keeps its defaults (line +0: see `AoE:`).

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

## Target areas (line +0)

Each action (`SpellAction`: Begin, Maintain, End, Callback, EndCallback) has a `SpellArea`: the 64-bit target mask (OR of
the values of the target names), the spell flags a target must have and must not have, the radius and the count.
Defaults: mask 0, flags 0, radius 0, count −1. Keys in order:

| Key | Value | Effect |
| --- | --- | --- |
| `AoE:` | target-name list (65 names, 64-bit values) | every action's mask; then the not-invulnerable bit (`NotInvulnerableBit`, the high word's 0x2000) is added for every record except 1, 2 and 9. Missing: the original logs an error and the masks hold that bit only (strict mode: `MissingKey`). |
| `AoE_SF:`, `AoE_NO_SF:` | spell-flag lists | every action's required and excluded flags (0 when absent) |
| `Radius:`, `Count:` | number | every action's radius and count when present |
| `[Begin]AoE:` … `[EndCallback]Count:` | the same five keys per action, actions in order | the mask and the flag sets are OR-ed into that action's area; radius and count replace |

The 65 target names (`SpellComponentTables.Targets`, values `TargetValues`) run from `Tgt_None` (0) through the object,
critter, portal, container, wall, tile, party and list targets to `Tgt_List` (bit 63); a name's value combines a kind bit
(object 0x4, critter 0x40 with it, tile) with its own test bit.

## Components (lines +5 onward)

Each component line becomes a `SpellComponent` in file order: its action, area, `Apply_AoE:` mask, item triggers, type and
typed data (`SpellComponentData`, null for the types without data). The line starts with the action name **without a key**
(`[Begin]`, `[Maintain]`, `[End]`, `[Callback]`, `[EndCallback]`, the whole value to the first comma); then:

| Key | Value | Default |
| --- | --- | --- |
| `AoE:` | target-name list | 0 |
| `AoE_SF:`, `AoE_NO_SF:` | spell-flag lists | 0 |
| `Radius:` | number | the action's radius from line +0 |
| `Count:` | number | −1 |
| `Apply_AoE:` | target-name list | 0 |
| `ItemTriggers:` | item-trigger list | 0 |
| `Type:` | one of the 25 `rules\magictech.mes` names, ASCII case-insensitive | NoOp (the original reads on; strict mode: `MissingKey`) |

Then the type's data. "Positional" values have no key and are read at the cursor in the order shown:

| Type | Data | Keys and positional values (default) |
| --- | --- | --- |
| NoOp, Destroy, Dispel, Identify, Terminate, Use | none | — |
| AGoal | `AnimationGoalData` | positional goal name (`anim_goal_animate`, `_knockback`, `_follow`, `_floating`, `_destroy_obj`, `_knock_down`, stored as the goal values 0, 43, 24, 44, 32, 62); `SubGoal:` (0) |
| AGoalTerminate | `AnimationGoalTerminateData` | positional goal name |
| AIRedirect | `AiRedirectData` | positional critter-flag name (one bit); `MinIQ:` (−1) |
| Cast, Interrupt | `SpellReferenceData` | `Spell:` (Cast) or `MagicTech:` (Interrupt); 10000 when absent, which the original logs (strict: `MissingKey`) |
| ChargeNBranch | `ChargeBranchData` | `Cost:` (0), `Branch:` (−1) |
| Damage | `DamageData` | `DmgType:` (`Dmg_Normal`, `_Poison`, `_Electrical`, `_Fire`, `_Magic`, `_Acid`; 0), `Dmg:` range `a-b` or `a` for both (0, 0), `Dmg_Flags:` list (`None`, `Full`, `Resurrect`, `Reanimate`, `Death`, `Stunned`, `Scars`, `Scaled`, `Weap_Dropped`, `Crippling` with the original's values; 0). Marks the record `HasDamage`. |
| Heal | `DamageData` | as Damage, but `DmgType:` accepts the first five names only |
| Effect | `EffectData` | positional effect number; positional `Remove` or `Add`; `Count:` (1); `Cause:` (`Race` … `Gender`, 10 names; 6, spell); `Scaled:` (0) |
| EnvFlag | `EnvironmentFlagData` | positional spell-flag name (one bit); positional `FLAG_OFF` or `FLAG_ON` |
| EyeCandy | `EyeCandyData` | positional eye-candy number; positional `Remove` or `Add`; `Play:` list (`reverse`, `stack`, `destroy`, `callback`, `end_callback`, `random_start`, `fire_dmg`, `check_already`, `no_id`, `ice_dmg`; 0) |
| Movement | `MovementData` | `Move_Location:` (`Target_Tile`, `Random_Tile_In_Radius_Self`, `Teleport_Tile`, `Teleport_Area_Portal`; 0); `Tile_Radius:` (0), read only after `Random_Tile_In_Radius_Self` |
| ObjFlag | `ObjectFlagData` | positional field name (21 object flag fields, stored as the field number); positional flag name of that field's table (one bit); positional `FLAG_OFF` or `FLAG_ON`. Turning a flag of `obj_f_spell_flags` on adds it to the record's `SpellFlagsSet`. |
| Recharge | `RechargeData` | positional amount; `Max:` (9999) |
| Summon | `SummonData` | `Proto:` (0, which the original logs; strict: `MissingKey`), `Clear_Faction:` (0), `Palette:` (0), `List:` (−1) |
| TestNBranch | `TestBranchData` | positional field (`art_num`, `Teleport_Tile`, `obj_f_critter_fleeing_from`, `HP_Damage`, stored as the field values 1, 242, 230, 29); positional comparison (`Equal`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`); `TestVal:` (0); `Branch:` (−1) |
| Trait | `TraitData` | positional field (as TestNBranch); positional value; `Palette:` (0) |
| TraitIdx | `TraitIndexData` | positional indexed field (`obj_f_critter_stat_base_idx` 220, `obj_f_resistance_idx` 31); positional index name (the stat keys, or `resist_damage` … `resist_magic`); positional value |
| Trait64 | `Trait64Data` | positional field (as TestNBranch); positional source (`Target_Tile`, `Caster_Obj`, `Caster_Owner_Obj`) |

The parse fixes further fields that the data has no key for (the multiplier and divisor 1 and the addend 0 of Trait and
TraitIdx, a component's last three working fields 0 and −1); they are the run time's, not data. `Proto:` is the **basic**
prototype number: every installed value has a prototype file at the number + 20 (the file numbering `ContentPrototypes`
uses); the original resolves it to a prototype at load, which the summon slice (MT17) reads.

### MG18: the not-dead-critter target

The target name `Tgt_Obj_No_ST_Critter_Dead` has the value 0x80000004: it lacks the critter bit 0x40 that every other
`Tgt_Obj_No_ST_Critter_*` name carries, so an area naming it does not require critters (ledger MG18, an original bug). The
data keeps the original's masks; `SpellTargetRules.Targets` adds the critter bit to a mask naming that target under
the Steamwoven setting and keeps it under Original (`MagicAnomalyProfile.NotDeadCritterTargetLacksCritterBit`, the
[rule-mode registry](../world/rule-modes.md#registry)). 105 installed records name it (100 line +0 areas, 15 components),
so the owner's decision makes it a fix (2026-10-20). Three further target values look inconsistent with their siblings —
`Tgt_Non_Party_Critters` without the critter bit (20 records), `Tgt_Obj_Radius` without the object bit (24 records),
`Tgt_Tile_Self` without the tile bit (unused) — and are recorded as MG21–MG23 (ledger SC10) for the target slice (MT8),
which reads how the bits are tested; the data keeps their values.

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
  process; a separator that is the last character of the value (`A ,`, `A|`) crashes it. Target-name lists work the same
  way with 64-bit values.
- **Range** `a-b` (`Dmg:`). a is the `atoi` of the text before the first `-`, b of the text after it, or a again without
  `-`.
- **Positional value** (component data, no key): a number or a name at the cursor, as above. When the line has already
  ended (the last value had no comma after it), the original reads through a null cursor and crashes.

## Reader modes

Load-map policy 2. Both modes refuse what ends the original's process: `MissingParenthesis`, `UnknownName` (also an
unknown action name, component type, positional name, or a name reaching a hole of a field's table), `UnknownFlag`,
`TrailingSeparator`, `MissingValue` (a positional value after the line has ended), and `NonAsciiByte` for a byte above 0x7F on lines +1..+4 (the original's white-space test reads
memory before its character table for one, so its result depends on run-time data; the refusal covers the whole line,
deliberately broader than the places the original's skip meets the byte). A missing component type name is
`MissingComponentName`.

- **Like original** (`SpellRecordMode.LikeOriginal`) reads everything else as the original does: skipped and unknown keys
  are not read (they are listed in `SpellRecordTable.Unread`), missing lines and texts keep the defaults, numbers are
  `atoi`.
- **Strict** (`SpellRecordMode.Strict`) also refuses:

| Status | When |
| --- | --- |
| `MissingRequiredLine` | line +0 or +1 of a record is missing |
| `MissingKey` | a key whose absence the original logs and reads on with a default: `AoE:` of line +0, a component's `Type:`, a Cast's `Spell:`, an Interrupt's `MagicTech:`, a Summon's `Proto:` |
| `MissingText` | a text of `mes\spell.mes` is missing |
| `UnknownKey` | text is left after the cursor's last key and its first non-empty segment's key is not one of the line's |
| `SkippedKey` | the left text starts with one of the line's keys (out of order, repeated, or a `DurationStatInfo:` after a stat of −1) |
| `InvalidNumber` | a number, alone, in a pair, in a range or positional, that is not an optional sign and digits within 32 bits (a range: one or two unsigned numbers) |
| `MalformedPair` | a pair without `@`, with text after `)`, or a stat name followed by more than spaces |
| `AmbiguousFlagList` | a flag list whose names the original reads only in part |
| `IgnoredValue` | a value read and then dropped: a negative `DurationTriggerCount:`, a non-zero `ChargeBeginCost:`, a zero `No_Resist:` or `No_Reflect:`, a non-zero `IQ:` on a record from 80 (forced to 0), `Is_Tech: 0` on a record from 140 (forced technology) |

The first refusal is returned: in record order for lines +0..+4, then in record order for the component lines (the
original's two passes); within one line, a fault that ends the
original's process takes priority over a strict refusal of an earlier key. Records are read before the texts of
`mes\spell.mes` (the spell module reads those in a later init), so in strict mode a record refusal comes before a
`MissingText`; the order changes no result. On the installed original like-original mode reads all 223 records; strict
mode refuses the file at its first unread component text ([Installed original](#installed-original)).

## Installed original

**Strict mode refuses the installed file** since ARC-MAGIC-002: 44 component lines hold text the original never reads
(`Unread`, like-original mode): 40 Destroy components and 3 Identify components carry a positional word after their type
(those types take no data), and one Damage component of record 221 (line +8) repeats `Dmg_Flags:`, so its second value,
`Scaled`, is never read (a candidate data fix, not taken here). Strict mode stops at the first (message 1305); like-original
mode reads all 223 records. This is the [DLG precedent](dlg-reader.md#the-38-strict-refusals-arc-script-012): strict
refusals of installed data are analysed, not loosened.

Opt-in test `SpellRecordRealDataTests` (`DAT_ROOT`), matching the [data census](../research/combat-data-census.md#3-spell-records):
lines +0..+4 present in 223/223/10/160/105 records; no `IQ:`; `Resist:` constitution 22, willpower 12, dexterity 2,
strength 1; `Info:` aggressive 82, friendly 75; `No_Resist:` 2 (technology), `No_Reflect:` 2 (college); per record range
0..79 / 80..139 / 140..222 `Cost:` 80/36/41, `Maintain:` 53/7/6, `Duration:` 2/12/37, `DurationStatInfo:` 0/0/9,
`DurationTriggerCount:` 2/3/6, `Resist:` 26/6/5, `Range:` 0/8/42, `ChargesFromCells:` 0/0/3, `ChargeBeginCost:` 1/0/1,
`Info:` 37/43/77; the ten AI lists 7/16/25/32/7/3/1/3/1/2; component lines 0 / 1..4 / 5..9 / 10..13 in 5/123/86/9 records;
no unknown or skipped key on lines +1..+4. `AutoLevelTables.ReadSpellWillpower` (unchanged) gives the same willpower
minima as the records.

Component census (ARC-MAGIC-002; the questions census U3 left open, counts only):

- **1,009 component lines**, all parsed; actions Begin 627, Maintain 19, End 286, Callback 50, EndCallback 27. Per type
  (Begin/Maintain/End/Callback/EndCallback): EyeCandy 261/3/163/0/2, ObjFlag 103/0/59/27/0, Effect 75/0/50/0/1, Damage
  51/7/7/3/0, Destroy 17/0/1/15/24, Heal 34/6/0/5/0, Summon 42/0/0/0/0, AGoal 17/0/0/0/0, Trait 9/0/5/0/0, NoOp 4/0/0/0/0,
  Identify 3/0/0/0/0, Movement 3/0/0/0/0, ChargeNBranch 2/1/0/0/0, Dispel 2/0/0/0/0, AIRedirect 1/1/0/0/0, Cast, Recharge
  and Terminate 1 Begin each, TestNBranch 1 Maintain, Use 1 End. **No record uses Interrupt** (MG7 stays record only),
  AGoalTerminate, EnvFlag, TraitIdx or Trait64.
- 52 records are damaging (`HasDamage`); 54 turn spell flags on; 3 records (1, 2, 9) have no not-invulnerable bit.
- `Dmg_Flags:` on 24 of 68 Damage and 12 of 45 Heal components: Full 16, Scaled 16, Resurrect 7 (2 of them Reanimate),
  Death 4, Stunned 3, Scars 2, Crippling 2, Weapon dropped 1. Damage types: Damage normal 32, poison 10, electrical 9,
  fire 6, magic 10, acid 1; Heal normal 32, poison 8, magic 5.
- Summons: 42 components, 38 without `List:`, lists 0, 3, 4 and 5 once each; **no `Clear_Faction:`** and no `Palette:`
  (MG8 and the summon palette change nothing in the installed data); 4 without `Proto:` (all with a list); 20 distinct
  prototypes, each with a file at the number + 20.
- `No_Stack:` 1 on 109 records, 0 on 34; `Cancels_SF:` 12, `Cancels_EnvSF:` 0, `Disallowed_SF:` 3, `Disallowed_TSF:` 15,
  `Disallowed_TCF:` 6 records.
- `No_Resist:` (records 218 and 221) never comes with `Maintain:`, `Duration:` or `DurationTriggerCount:`, so MG19 (an
  unresistable run ends right after BEGIN) changes no installed record; it still reaches the script action that casts
  unresistably ([plan](../research/magic-implementation-plan.md#anomaly-decisions-accepted-2026-10-20)).
- Records 80..139: 57 have components (EyeCandy 69, ObjFlag 38, Effect 28, Heal 16, Damage 15, AGoal 5, Destroy 5,
  Summon 5, Trait 3, NoOp 2, Recharge 1), 16 have AI priorities and 23 item triggers.
- MG18: `Tgt_Obj_No_ST_Critter_Dead` in 105 records (100 line +0 areas, 15 components).

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
| A positional component value read after its line has ended crashes (null cursor); a name of an ObjFlag field or a TraitIdx stat that matches nothing reaches a hole of its table and crashes (SC5). | Refused (`MissingValue`, `UnknownName`). | both |
| An unknown action name or component type exits (SC5). | Refused (`UnknownName`). | both |
| A missing `AoE:`, `Type:`, `Spell:`, `MagicTech:` or `Proto:` is logged and read on with a default (SC1, SC5, SC7, SC8). | The same default; strict mode refuses it (`MissingKey`). | strict |
| `Tgt_Obj_No_ST_Critter_Dead` lacks the critter bit (MG18). | The data keeps the original's mask; the Steamwoven setting adds the bit where the area is used, the Original setting keeps it (switch `NotDeadCritterTargetLacksCritterBit`). | rule mode |
| `Proto:` is resolved to a prototype at load. | The basic prototype number is kept (resolution is the summon slice's). | both |
| `Rules\SpellEyeCandy.mes` is parsed by the same init. | Not read. | both |

## Not ready

- No rule reads the records yet (spell book MT3, cost MT5, resistance MT6, invocation MT7, target lists MT8, components
  MT12–MT18); the auto-level scheme keeps its own willpower read. What the target names and the component data do at run
  time is those slices'.
- The record 221 `Dmg_Flags:` data defect is recorded, not fixed (a data-fix row would need the owner's decision).
- The eye-candy file `Rules\SpellEyeCandy.mes` and the 512 run slots of the same init are not read here.

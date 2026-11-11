using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Arcanum.Data.Fixes;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Magic;

/// <summary>How the spell data readers treat shapes the original reads silently (load-map policy 2).</summary>
public enum SpellRecordMode
{
    /// <summary>Refuses an unknown key, a key the original's cursor skips, a missing required line or text, and a value the
    /// original reads in a way its text does not say (a malformed number or pair, a dropped value, a flag list it reads in part).</summary>
    Strict,
    /// <summary>Reads as the original: skipped and unknown keys and missing lines change nothing, numbers are C <c>atoi</c>.</summary>
    LikeOriginal,
}

public enum SpellDataStatus
{
    Read,
    /// <summary>A file the original's init loads is not in the base sources (its init fails).</summary>
    FileMissing,
    FileUnreadable,
    /// <summary>The file has no message (the original's loader fails on an empty file).</summary>
    FileEmpty,
    /// <summary>The file is not one the MES grammar admits (project policy).</summary>
    FileRefused,
    /// <summary>A component type name 10..34 of <c>rules\magictech.mes</c> is missing: the original's init fails.</summary>
    MissingComponentName,
    /// <summary>A byte above 0x7F on any parsed line (ledger SR14). This and the next five statuses, through
    /// <see cref="MissingValue"/>, are refused in both modes: the original ends its process on them.</summary>
    NonAsciiByte,
    MissingParenthesis,
    UnknownName,
    UnknownFlag,
    TrailingSeparator,
    /// <summary>A positional value of a component read after its line has ended: the original reads through a null cursor.</summary>
    MissingValue,
    /// <summary>Strict mode only.</summary>
    MissingText,
    MissingRequiredLine,
    UnknownKey,
    /// <summary>A known key the original's cursor does not reach (out of order, repeated, or after a <c>Duration:</c> whose
    /// stat is −1), so it is not read.</summary>
    SkippedKey,
    InvalidNumber,
    MalformedPair,
    /// <summary>A key the original reads on without, with a default: logged as an error for <c>AoE:</c> of line +0, a Cast's
    /// <c>Spell:</c>, an Interrupt's <c>MagicTech:</c> and a Summon's <c>Proto:</c>; silent for a component's <c>Type:</c>
    /// (NoOp).</summary>
    MissingKey,
    /// <summary>A flag list the original reads only in part (a space-separated name before a later <c>|</c>).</summary>
    AmbiguousFlagList,
    /// <summary>A value the original reads and drops: a negative <c>DurationTriggerCount:</c>, a non-zero
    /// <c>ChargeBeginCost:</c>, a zero <c>No_Resist:</c> or <c>No_Reflect:</c>.</summary>
    IgnoredValue,
}

/// <summary>Why a spell data file was refused: the file, the message number and the token involved.</summary>
public sealed record SpellDataRefusal(SpellDataStatus Status, string File, int? Entry = null, string? Token = null);

public sealed record SpellRecordRead(SpellDataStatus Status, SpellRecordTable? Table, SpellDataRefusal? Refusal = null);

/// <summary>
/// ARC-MAGIC-001: the 223 spell records of <c>rules\spelllist.mes</c> (contract docs/formats/spell-records.md, checked in the
/// 1.0.7.4 executable, ledger "Spell records"). Record n starts at message 1000 + 50·n for n &lt; 140 and 3000 + 50·n from 140.
/// Lines +0 and +1 are always read (a missing one reads as the original's missing-line text, which holds no key), lines +2..+4
/// only when present; each is a run of named keys in a fixed order (<see cref="NamedKeyCursor"/>). Records are read in
/// number order and the first refusal stops the read.
/// </summary>
public sealed partial class SpellRecordTable
{
    public const string Path = "rules/spelllist.mes";
    public const int FirstBase = 1000, TechnologyBase = 3000, Stride = 50, FirstComponentLine = 5, MaxComponentLines = 44;
    private static readonly int[] RankWillpower = [6, 9, 12, 15, 18];
    private static readonly string[] Line1Keys =
        ["IQ:", "Cost:", "Maintain:", "Duration:", "DurationStatInfo:", "DurationTriggerCount:", "Resist:", "Range:", "ChargesFromCells:",
         "ChargeBeginCost:", "Info:", "Disabled:"];
    private static readonly string[] Line2Keys =
        ["CastingAnim:", "Missile:", "[Begin]Caster:", "[Begin]Target:", "[Maintain]Caster:", "[Maintain]Target:", "[End]Caster:",
         "[End]Target:", "[Callback]Caster:", "[Callback]Target:", "[EndCallback]Caster:", "[EndCallback]Target:", "Is_Tech:"];
    private static readonly string[] Line3Keys =
        ["No_Stack:", "Cancels_SF:", "Cancels_EnvSF:", "Disallowed_SF:", "Disallowed_TSF:", "Disallowed_TCF:", "ItemTriggers:"];
    private static readonly string[] Line4Keys = [.. SpellNameTables.AiKeys, "No_Resist:", "No_Reflect:"];

    private readonly SpellRecord[] _records;
    private SpellRecordTable(SpellRecord[] records, SpellDataRefusal[] unread)
    {
        _records = records;
        Unread = Array.AsReadOnly(unread);
    }

    public IReadOnlyList<SpellRecord> Records => Array.AsReadOnly(_records);
    /// <summary>Like-original mode: text the original's cursor leaves unread after a line's last key, which strict mode refuses
    /// (<see cref="SpellDataStatus.UnknownKey"/> or <see cref="SpellDataStatus.SkippedKey"/> with the first unread key or word);
    /// empty in strict mode.</summary>
    public IReadOnlyList<SpellDataRefusal> Unread { get; }
    public SpellRecord this[int spell] => _records[spell];

    /// <summary>The first message of record <paramref name="spell"/> (0..222).</summary>
    public static int FirstMessage(int spell)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(spell);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(spell, SpellRecord.Count);
        return (spell < SpellRecord.FirstTechnology ? FirstBase : TechnologyBase) + Stride * spell;
    }

    /// <summary>Reads all 223 records of a parsed <c>rules\spelllist.mes</c>; <paramref name="componentTypes"/> are the 25
    /// <c>Type:</c> names of <c>rules\magictech.mes</c> (<see cref="SpellData.ComponentTypeNames"/>).</summary>
    public static SpellRecordRead Read(MesFile list, IReadOnlyList<string> componentTypes, SpellRecordMode mode, DataFixProfile? fixes = null)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(componentTypes);
        if (componentTypes.Count != SpellData.ComponentTypes || componentTypes.Any(t => t is null))
            throw new ArgumentException("The 25 component type names are required.", nameof(componentTypes));
        if (mode is not (SpellRecordMode.Strict or SpellRecordMode.LikeOriginal)) throw new ArgumentOutOfRangeException(nameof(mode));
        var drafts = new Draft[SpellRecord.Count];
        var refusal = ReadRecords(list, mode, drafts) ?? ReadComponentPass(list, componentTypes, mode, drafts, fixes?.Applies("DF4") == true);
        if (refusal is not null) return new(refusal.Status, null, refusal);
        return new(SpellDataStatus.Read, new(drafts.Select(d => d.ToRecord()).ToArray(), drafts.SelectMany(d => d.Unread).ToArray()));
    }

    /// <summary>The first pass of the original's load: lines +0..+4 of each record in number order, then the forced values
    /// (willpower 0 from record 80, technology from 140). The file itself is loaded by <see cref="SpellData.Open"/>.</summary>
    [OriginalFunction(0x44FE30, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x44FE47, OriginalFunctionScope.Partial)]
    private static SpellDataRefusal? ReadRecords(MesFile list, SpellRecordMode mode, Draft[] drafts)
    {
        for (var spell = 0; spell < SpellRecord.Count; spell++)
        {
            drafts[spell] = Defaults(spell);
            if (ReadRecord(list, mode, drafts[spell]) is { } refusal) return refusal;
            Force(drafts[spell]);
        }
        return null;
    }

    /// <summary>The second pass: the component lines of each record in number order, then the forced values again.</summary>
    [OriginalFunction(0x44FFA0, OriginalFunctionScope.Full)]
    private static SpellDataRefusal? ReadComponentPass(MesFile list, IReadOnlyList<string> types, SpellRecordMode mode, Draft[] drafts,
        bool repeatedDamageFlags)
    {
        foreach (var draft in drafts)
        {
            draft.RepeatedDamageFlags = repeatedDamageFlags;
            if (ComponentLines(list, draft, types, mode) is { } refusal) return refusal;
            Force(draft);
        }
        return null;
    }

    private static void Force(Draft draft)
    {
        if (draft.Number >= SpellRecord.CollegeSpells) draft.MinimumWillpower = 0;
        if (draft.Number >= SpellRecord.FirstTechnology) draft.IsTechnology = true;
    }

    /// <summary>One record: line +0 (the target areas), line +1, line +2 and +3 when present, the AI values reset to −1 and 0, line
    /// +4 when present. The record's name (message n of <c>mes\spell.mes</c>) is <see cref="SpellTexts.Names"/>.</summary>
    [OriginalFunction(0x450090, OriginalFunctionScope.Partial)]
    private static SpellDataRefusal? ReadRecord(MesFile list, SpellRecordMode mode, Draft draft)
    {
        var first = FirstMessage(draft.Number);
        for (var offset = 0; offset <= 4; offset++) draft.LinesPresent[offset] = list.Find(first + offset) is not null;
        if (mode == SpellRecordMode.Strict)
            for (var offset = 0; offset <= 1; offset++)
                if (!draft.LinesPresent[offset]) return new(SpellDataStatus.MissingRequiredLine, Path, first + offset);
        draft.AreaLine = Text(list, first);
        return Line(list, mode, draft, first, Line0Keys, cursor => ParseLine0(cursor, draft, mode), always: true)
            ?? Line(list, mode, draft, first + 1, Line1Keys, cursor => ParseLine1(cursor, draft, mode), always: true)
            ?? Line(list, mode, draft, first + 2, Line2Keys, cursor => ParseLine2(cursor, draft, mode), always: false)
            ?? Line(list, mode, draft, first + 3, Line3Keys, cursor => ParseLine3(cursor, draft, mode), always: false)
            ?? ResetAi(draft)
            ?? Line(list, mode, draft, first + 4, Line4Keys, cursor => ParseLine4(cursor, draft, mode), always: false);
    }

    private static SpellDataRefusal? ResetAi(Draft draft)
    {
        Array.Fill(draft.AiPriorities, -1);
        draft.AiDefensiveSecond = 0;
        return null;
    }

    /// <summary>The component lines: from +5 while the next message exists, at most 44, each parsed in order.</summary>
    [OriginalFunction(0x4501D0, OriginalFunctionScope.Full)]
    private static SpellDataRefusal? ComponentLines(MesFile list, Draft draft, IReadOnlyList<string> types, SpellRecordMode mode)
    {
        var lines = new List<string>();
        var number = FirstMessage(draft.Number) + FirstComponentLine;
        for (; lines.Count < MaxComponentLines && Text(list, number) is { } text; number++)
        {
            lines.Add(text);
            if (Line(list, mode, draft, number, ComponentKeys, cursor => ParseComponent(cursor, draft, types, mode), always: true) is { } refusal) return refusal;
        }
        draft.ComponentLines = lines.ToArray();
        return null;
    }

    /// <summary>Every field's starting value: willpower by rank, range 99, no resistance or duration stat, duration modifier 1,
    /// eye candy and AI −1, action areas empty with count −1, everything else 0 or off, enabled off until line +1 sets it.</summary>
    [OriginalFunction(0x457580, OriginalFunctionScope.Full)]
    private static Draft Defaults(int spell) => new(spell) { MinimumWillpower = RankWillpower[spell % SpellRecord.Ranks] };

    /// <summary>Line +1, in this order: IQ, Cost, Maintain, Duration (then DurationStatInfo only when its stat is not −1),
    /// DurationTriggerCount (kept when above 0), Resist, Range, ChargesFromCells, ChargeBeginCost, Info, Disabled.</summary>
    [OriginalFunction(0x4578F0, OriginalFunctionScope.Full)]
    private static SpellDataRefusal? ParseLine1(NamedKeyCursor cursor, Draft d, SpellRecordMode mode)
    {
        var check = new ValueCheck(mode, d.KeysRead);
        if (check.Int(cursor, "IQ:") is { } iq)
        {
            d.MinimumWillpower = iq;
            if (d.Number >= SpellRecord.CollegeSpells && iq != 0) check.Ignored("IQ:");
        }
        if (check.Int(cursor, "Cost:") is { } cost) d.Cost = cost;
        if (check.Pair(cursor, "Maintain:") is { } maintain) (d.MaintainCost, d.MaintainPeriod) = maintain;
        if (check.Pair(cursor, "Duration:") is { } duration)
        {
            (d.DurationSeconds, d.DurationStat) = duration;
            if (duration.Second != -1 && check.Pair(cursor, "DurationStatInfo:") is { } info) (d.DurationStatLevel, d.DurationStatModifier) = info;
        }
        if (check.Int(cursor, "DurationTriggerCount:") is { } count)
        {
            if (count > 0) d.DurationTriggerCount = count;
            else if (count < 0) check.Ignored("DurationTriggerCount:");
        }
        if (check.NamePair(cursor, "Resist:", SpellNameTables.StatKeys) is { } resist) (d.ResistStat, d.ResistModifier) = resist;
        if (check.Int(cursor, "Range:") is { } range) d.Range = range;
        if (check.Int(cursor, "ChargesFromCells:") is { } cells && cells != 0) d.ChargesFromCells = true;
        if (check.Int(cursor, "ChargeBeginCost:") is { } begin)
        {
            if (begin == 0) d.SkipsBeginCost = true;
            else check.Ignored("ChargeBeginCost:");
        }
        if (check.Name(cursor, "Info:", SpellNameTables.Info) is { } kind) d.Info = (SpellInfo)kind;
        var disabled = check.Int(cursor, "Disabled:");
        if (disabled is null or 0) d.Enabled = true;
        return check.Refusal;
    }

    /// <summary>Line +2: CastingAnim, Missile, caster and target eye candy of the five actions, Is_Tech (non-zero: technology).</summary>
    [OriginalFunction(0x457B20, OriginalFunctionScope.Full)]
    private static SpellDataRefusal? ParseLine2(NamedKeyCursor cursor, Draft d, SpellRecordMode mode)
    {
        var check = new ValueCheck(mode, d.KeysRead);
        if (check.Int(cursor, "CastingAnim:") is { } anim) d.CastingAnimation = anim;
        if (check.Int(cursor, "Missile:") is { } missile) d.Missile = missile;
        for (var action = 0; action < SpellRecord.Actions; action++)
        {
            var name = ((SpellAction)action).ToString();
            if (check.Int(cursor, $"[{name}]Caster:") is { } caster) d.CasterEyeCandy[action] = caster;
            if (check.Int(cursor, $"[{name}]Target:") is { } target) d.TargetEyeCandy[action] = target;
        }
        if (check.Int(cursor, "Is_Tech:") is { } tech)
        {
            if (tech != 0) d.IsTechnology = true;
            else if (d.Number >= SpellRecord.FirstTechnology) check.Ignored("Is_Tech:");
        }
        return check.Refusal;
    }

    /// <summary>Line +3: No_Stack, Cancels_SF, Cancels_EnvSF, Disallowed_SF, Disallowed_TSF (spell flags), Disallowed_TCF
    /// (critter flags), ItemTriggers (trigger values).</summary>
    [OriginalFunction(0x457D00, OriginalFunctionScope.Full)]
    private static SpellDataRefusal? ParseLine3(NamedKeyCursor cursor, Draft d, SpellRecordMode mode)
    {
        var check = new ValueCheck(mode, d.KeysRead);
        if (check.Int(cursor, "No_Stack:") is { } stack) d.NoStack = stack;
        if (check.Bits(cursor, "Cancels_SF:", SpellNameTables.SpellFlags) is { } cancels) d.CancelsSpellFlags = cancels;
        if (check.Bits(cursor, "Cancels_EnvSF:", SpellNameTables.SpellFlags) is { } env) d.CancelsEnvironmentSpellFlags = env;
        if (check.Bits(cursor, "Disallowed_SF:", SpellNameTables.SpellFlags) is { } caster) d.DisallowedCasterSpellFlags = caster;
        if (check.Bits(cursor, "Disallowed_TSF:", SpellNameTables.SpellFlags) is { } target) d.DisallowedTargetSpellFlags = target;
        if (check.Bits(cursor, "Disallowed_TCF:", SpellNameTables.CritterFlags) is { } critter) d.DisallowedTargetCritterFlags = critter;
        if (check.Triggers(cursor, "ItemTriggers:") is { } triggers) d.ItemTriggers = triggers;
        return check.Refusal;
    }

    /// <summary>Line +4: the ten AI values (AI_Defensive a pair), then No_Resist and No_Reflect by presence.</summary>
    [OriginalFunction(0x457E70, OriginalFunctionScope.Full)]
    private static SpellDataRefusal? ParseLine4(NamedKeyCursor cursor, Draft d, SpellRecordMode mode)
    {
        var check = new ValueCheck(mode, d.KeysRead);
        for (var i = 0; i < SpellNameTables.AiKeys.Count; i++)
        {
            if (i == 2)
            {
                if (check.Pair(cursor, SpellNameTables.AiKeys[i]) is { } defensive) (d.AiPriorities[i], d.AiDefensiveSecond) = defensive;
            }
            else if (check.Int(cursor, SpellNameTables.AiKeys[i]) is { } value) d.AiPriorities[i] = value;
        }
        if (check.Int(cursor, "No_Resist:") is { } noResist)
        {
            d.NoResist = true;
            if (noResist == 0) check.Ignored("No_Resist:");
        }
        if (check.Int(cursor, "No_Reflect:") is { } noReflect)
        {
            d.NoReflect = true;
            if (noReflect == 0) check.Ignored("No_Reflect:");
        }
        return check.Refusal;
    }

    /// <summary>Runs one line's parse. A fault that ends the original's process takes priority over a strict refusal of an
    /// earlier key on the same line. A missing line +1 (or +0) is the original's missing-line text, which holds no key; a
    /// missing optional line is not parsed. A line with a byte above 0x7F is refused in both modes; strict mode refuses what is
    /// left after the cursor's last key.</summary>
    private static SpellDataRefusal? Line(MesFile list, SpellRecordMode mode, Draft draft, int number, string[] keys,
        Func<NamedKeyCursor, SpellDataRefusal?> parse, bool always)
    {
        var text = Text(list, number);
        if (text is null && !always) return null;
        text ??= "";
        // Broader than the original on purpose: a byte above 0x7F anywhere on the line, not only where the white-space skip
        // meets it (ledger SR14).
        foreach (var c in text)
            if (c > '\x7f') return new(SpellDataStatus.NonAsciiByte, Path, number, c.ToString());
        var cursor = new NamedKeyCursor(text);
        var refusal = parse(cursor);
        if (cursor.Fault != NamedKeyFault.None)
            return new(cursor.Fault switch
            {
                NamedKeyFault.MissingParenthesis => SpellDataStatus.MissingParenthesis,
                NamedKeyFault.UnknownName => SpellDataStatus.UnknownName,
                NamedKeyFault.UnknownFlag => SpellDataStatus.UnknownFlag,
                NamedKeyFault.MissingValue => SpellDataStatus.MissingValue,
                _ => SpellDataStatus.TrailingSeparator,
            }, Path, number, cursor.FaultToken);
        if (refusal is not null) return refusal with { Entry = number };
        var rest = cursor.Rest();
        if (rest.Length == 0) return null;
        var segment = rest.Split(',').FirstOrDefault(part => part.Trim().Length > 0)?.Trim() ?? rest;
        var colon = segment.IndexOf(':');
        var key = colon < 0 ? segment.Trim() : segment[..(colon + 1)];
        var unread = new SpellDataRefusal(keys.Contains(key, StringComparer.Ordinal) ? SpellDataStatus.SkippedKey : SpellDataStatus.UnknownKey,
            Path, number, key);
        if (mode == SpellRecordMode.Strict) return unread;
        draft.Unread.Add(unread);
        return null;
    }

    private static string? Text(MesFile list, int number) =>
        list.Find(number) is { } message ? Encoding.Latin1.GetString(message.Bytes.Span) : null;

    /// <summary>Strict mode's checks of a value the cursor read; the first failure is kept.</summary>
    private sealed partial class ValueCheck(SpellRecordMode mode, List<string> keys)
    {
        public SpellDataRefusal? Refusal { get; private set; }

        public int? Int(NamedKeyCursor cursor, string key)
        {
            var value = cursor.NamedInt(key, out var token);
            if (value is not null) keys.Add(key);
            if (value is not null && Strict && !(IntText().IsMatch(token) && int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
                Refuse(SpellDataStatus.InvalidNumber, $"{key}{token}");
            return value;
        }

        public (int First, int Second)? Pair(NamedKeyCursor cursor, string key)
        {
            var value = cursor.NamedPair(key, out var token);
            if (value is not null) keys.Add(key);
            if (value is not null && Strict) CheckPair(key, token, PairText().Match(token), first: true);
            return value;
        }

        public (int First, int Second)? NamePair(NamedKeyCursor cursor, string key, IReadOnlyList<string> names)
        {
            var value = cursor.NamedNamePair(key, names, out var token);
            if (value is not null) keys.Add(key);
            if (value is not null && Strict) CheckPair(key, token, NamePairText().Match(token), first: false);
            return value;
        }

        public uint? Bits(NamedKeyCursor cursor, string key, IReadOnlyList<string> names)
        {
            var value = cursor.NamedFlagBits(key, names, out var tokens, out var text);
            if (value is not null) CheckList(key, tokens, text);
            return value;
        }

        public uint? Triggers(NamedKeyCursor cursor, string key) =>
            Triggers(cursor, key, SpellNameTables.ItemTriggers, SpellNameTables.ItemTriggerValues);

        public void Ignored(string key)
        {
            if (Strict) Refuse(SpellDataStatus.IgnoredValue, key);
        }

        public void Missing(string key)
        {
            if (Strict) Refuse(SpellDataStatus.MissingKey, key);
        }

        public ulong? Targets(NamedKeyCursor cursor, string key)
        {
            var value = cursor.NamedFlags64(key, SpellComponentTables.Targets, SpellComponentTables.TargetValues, out var tokens, out var text);
            if (value is not null) CheckList(key, tokens, text);
            return value;
        }

        public uint? Triggers(NamedKeyCursor cursor, string key, IReadOnlyList<string> names, IReadOnlyList<uint> values)
        {
            var value = cursor.NamedFlags(key, names, values, out var tokens, out var text);
            if (value is not null) CheckList(key, tokens, text);
            return value;
        }

        public int? Name(NamedKeyCursor cursor, string key, IReadOnlyList<string?> names)
        {
            var value = cursor.NamedName(key, names, out _);
            if (value is not null) keys.Add(key);
            return value;
        }

        /// <summary>A <c>Dmg:</c> range <c>a-b</c>, or one number for both.</summary>
        public (int First, int Second)? Range(NamedKeyCursor cursor, string key)
        {
            var value = cursor.NamedRange(key, out var token);
            if (value is null) return null;
            keys.Add(key);
            if (Strict && !(RangeText().Match(token) is { Success: true } m && Fits(m.Groups["a"].Value)
                && (!m.Groups["b"].Success || Fits(m.Groups["b"].Value))))
                Refuse(SpellDataStatus.InvalidNumber, $"{key}{token}");
            return value;
        }

        /// <summary>A positional number (no key) of a component.</summary>
        public int? Positional(NamedKeyCursor cursor)
        {
            var value = cursor.IntValue(out var token);
            if (value is not null && Strict && !(IntText().IsMatch(token) && int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
                Refuse(SpellDataStatus.InvalidNumber, token);
            return value;
        }

        private bool Strict => mode == SpellRecordMode.Strict;

        private void CheckList(string key, IReadOnlyList<string> tokens, string text)
        {
            keys.Add(key);
            if (Strict && !text.Split([' ', '|'], StringSplitOptions.RemoveEmptyEntries).SequenceEqual(tokens, StringComparer.Ordinal))
                Refuse(SpellDataStatus.AmbiguousFlagList, $"{key}{text}");
        }

        private void CheckPair(string key, string token, Match match, bool first)
        {
            if (!match.Success) Refuse(SpellDataStatus.MalformedPair, $"{key}{token}");
            else if (first && !Fits(match.Groups["a"].Value) || !Fits(match.Groups["b"].Value)) Refuse(SpellDataStatus.InvalidNumber, $"{key}{token}");
        }

        private static bool Fits(string number) => int.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);

        private void Refuse(SpellDataStatus status, string token) => Refusal ??= new(status, Path, null, token);

        [GeneratedRegex(@"^\s*(?<a>[0-9]+)\s*(-\s*(?<b>[0-9]+)\s*)?$")]
        private static partial Regex RangeText();

        [GeneratedRegex(@"^\s*[+-]?[0-9]+\s*$")]
        private static partial Regex IntText();

        [GeneratedRegex(@"^\(\s*(?<a>[+-]?[0-9]+)\s*@\s*(?<b>[+-]?[0-9]+)\s*\)\s*$")]
        private static partial Regex PairText();

        [GeneratedRegex(@"^\([^\s@]+\s*@\s*(?<b>[+-]?[0-9]+)\s*\)\s*$")]
        private static partial Regex NamePairText();
    }

    private sealed class Draft(int number)
    {
        public int Number { get; } = number;
        public int MinimumWillpower, Cost, MaintainCost, MaintainPeriod, DurationSeconds, DurationStat = -1, DurationStatLevel,
            DurationStatModifier = 1, DurationTriggerCount, ResistStat = -1, ResistModifier, Range = 99, CastingAnimation = -1,
            Missile = -1, NoStack, AiDefensiveSecond;
        public bool IsTechnology, ChargesFromCells, SkipsBeginCost, Enabled, NoResist, NoReflect;
        public SpellInfo Info;
        public uint CancelsSpellFlags, CancelsEnvironmentSpellFlags, DisallowedCasterSpellFlags, DisallowedTargetSpellFlags,
            DisallowedTargetCritterFlags, ItemTriggers;
        public int[] CasterEyeCandy = [-1, -1, -1, -1, -1], TargetEyeCandy = [-1, -1, -1, -1, -1];
        public int[] AiPriorities = Enumerable.Repeat(-1, 10).ToArray();
        public bool[] LinesPresent = new bool[5];
        public string? AreaLine;
        public string[] ComponentLines = [];
        public List<string> KeysRead = [];
        public SpellArea[] Areas = Enumerable.Repeat(new SpellArea(0, 0, 0, 0, -1), SpellRecord.Actions).ToArray();
        public List<SpellComponent> Components = [];
        public List<SpellDataRefusal> Unread = [];
        // DF4 (the Steamwoven setting): a repeated Dmg_Flags: is read too.
        public bool RepeatedDamageFlags;
        public bool HasDamage;
        public uint SpellFlagsSet;

        public SpellRecord ToRecord() => new()
        {
            Number = Number, MinimumWillpower = MinimumWillpower, Cost = Cost, MaintainCost = MaintainCost, MaintainPeriod = MaintainPeriod,
            DurationSeconds = DurationSeconds, DurationStat = DurationStat, DurationStatLevel = DurationStatLevel,
            DurationStatModifier = DurationStatModifier, DurationTriggerCount = DurationTriggerCount, ResistStat = ResistStat,
            ResistModifier = ResistModifier, Range = Range, IsTechnology = IsTechnology, ChargesFromCells = ChargesFromCells,
            SkipsBeginCost = SkipsBeginCost, Info = Info, Enabled = Enabled, NoResist = NoResist, NoReflect = NoReflect,
            CastingAnimation = CastingAnimation, Missile = Missile, CasterEyeCandy = Array.AsReadOnly(CasterEyeCandy),
            TargetEyeCandy = Array.AsReadOnly(TargetEyeCandy), NoStack = NoStack, CancelsSpellFlags = CancelsSpellFlags,
            CancelsEnvironmentSpellFlags = CancelsEnvironmentSpellFlags, DisallowedCasterSpellFlags = DisallowedCasterSpellFlags,
            DisallowedTargetSpellFlags = DisallowedTargetSpellFlags, DisallowedTargetCritterFlags = DisallowedTargetCritterFlags,
            ItemTriggers = ItemTriggers, AiPriorities = Array.AsReadOnly(AiPriorities), AiDefensiveSecond = AiDefensiveSecond,
            LinesPresent = Array.AsReadOnly(LinesPresent), KeysRead = KeysRead.AsReadOnly(), Areas = Array.AsReadOnly(Areas),
            Components = Components.AsReadOnly(), HasDamage = HasDamage, SpellFlagsSet = SpellFlagsSet, AreaLine = AreaLine, ComponentLines = Array.AsReadOnly(ComponentLines),
        };
    }
}

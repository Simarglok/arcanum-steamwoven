namespace Arcanum.Simulation.Travel;

/// <summary>One circle of a region chart (ledger PO13): its centre and radius in tiles and the value (the chance in per cent, the power 0..3, or the table override).</summary>
public readonly record struct EncounterRegion(long X, long Y, long Radius, int Value);

/// <summary>One monster slot of a table row: the prototype file number and the group size range (shorts in the original).</summary>
public readonly record struct EncounterGroup(int Prototype, int Min, int Max);

/// <summary>
/// One row of an encounter table (ledger PO14, 0x558D40, 0x5581E0): the weight, up to five groups, the level range (0..32000 by default), the global flag that must be set (-1: none) and the use cap (-1: none).
/// <see cref="Line"/> is the message number the row came from; the saved use count is keyed by it.
/// </summary>
public sealed record EncounterRow(int Line, int Weight, IReadOnlyList<EncounterGroup> Groups, int MinLevel, int MaxLevel, int GlobalFlag, int TriggerCap);

public enum EncounterTablesStatus { Loaded, Disabled }

/// <summary>
/// ARC-WORLD-1159 (E2-c, ledger PO13–PO19): the world-map random encounter tables of <c>rules/wmap_rnd.mes</c> as the original's module reads them (0x5581E0, 0x558B80, 0x558D40): the frequency chart (lines 10000..),
/// the power chart (20000..), the table-override chart (30000..) and the tables from line 50000 in blocks of 100 lines. Any failure of the original's loader disables the encounters for the session
/// (<see cref="Status"/> Disabled, with the reason in <see cref="Detail"/>); there is no partial load.
/// </summary>
public sealed class EncounterTables
{
    public const int FrequencyBase = 10000, PowerBase = 20000, OverrideBase = 30000, ChartSpan = 10000, TableCountLine = 49999, TableBase = 50000, TableSpan = 100, ExtraTables = 54, MaxGroups = 5;
    public const int DefaultMaxLevel = 32000, MaxGlobalFlag = 0xC80;

    private EncounterTables(EncounterTablesStatus status, string detail, IReadOnlyList<EncounterRegion> frequency, IReadOnlyList<EncounterRegion> power, IReadOnlyList<EncounterRegion> overrides,
        IReadOnlyList<IReadOnlyList<EncounterRow>> tables)
    {
        Status = status; Detail = detail; Frequency = frequency; Power = power; Override = overrides; Tables = tables;
    }

    public EncounterTablesStatus Status { get; }
    public string Detail { get; }
    public IReadOnlyList<EncounterRegion> Frequency { get; }
    public IReadOnlyList<EncounterRegion> Power { get; }
    public IReadOnlyList<EncounterRegion> Override { get; }
    public IReadOnlyList<IReadOnlyList<EncounterRow>> Tables { get; }

    /// <summary>The encounters are off: nothing was loaded, so no roll happens (the original's loader disables them on any error).</summary>
    public static EncounterTables Disabled(string detail) => new(EncounterTablesStatus.Disabled, detail, [], [], [], []);

    private static readonly string[] SlotKeys = ["First:", "Second:", "Third:", "Fourth:", "Fifth:"];
    private static readonly string[] PowerNames = ["none", "easy", "average", "powerful"];

    /// <summary>The tables from the file's messages (number and text, in any order).</summary>
    [OriginalFunction(0x5581E0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x558B80, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x558D40, OriginalFunctionScope.Full)]
    public static EncounterTables Parse(IReadOnlyList<(int Number, string Text)> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var sorted = messages.OrderBy(m => m.Number).ToArray();
        var byNumber = new Dictionary<int, string>();
        foreach (var (number, text) in sorted) byNumber[number] = text;

        IReadOnlyList<(int Number, string Text)> Range(int from, int span) => sorted.Where(m => m.Number >= from && m.Number < (long)from + span).ToArray();

        // 0x558B80: the chart rows are the first <count> messages from the base on, x, y and radius then the value (a number, or a power name).
        EncounterRegion[]? Chart(int baseLine, bool named, out string detail)
        {
            detail = "";
            var lines = Range(baseLine, ChartSpan);
            if (lines.Count == 0) { detail = $"empty chart {baseLine}"; return null; }
            var regions = new EncounterRegion[lines.Count];
            for (var i = 0; i < lines.Count; i++)
            {
                var cursor = new Cursor(lines[i].Text);
                var x = cursor.ReadLong(); var y = cursor.ReadLong(); var radius = cursor.ReadLong();
                int value;
                if (named)
                {
                    var name = cursor.ReadToken();
                    value = name is null ? -1 : Array.FindIndex(PowerNames, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                    if (value < 0) { detail = $"line {lines[i].Number}: unknown power name"; return null; }
                }
                else value = cursor.ReadInt();
                regions[i] = new(x, y, radius, value);
            }
            return regions;
        }

        var frequency = Chart(FrequencyBase, false, out var detail1);
        if (frequency is null) return Disabled(detail1);
        var power = Chart(PowerBase, true, out var detail2);
        if (power is null) return Disabled(detail2);
        var overrides = Chart(OverrideBase, false, out var detail3);
        if (overrides is null) return Disabled(detail3);

        // The count line 49999 (the first message from it on when absent) names how many tables follow the 54 terrain ones.
        var countText = sorted.FirstOrDefault(m => m.Number >= TableCountLine).Text;
        var tableCount = (countText is null ? 0 : Cursor.Atoi(countText)) + ExtraTables;
        if (tableCount < 0 || tableCount > 10_000) return Disabled("table count out of range");
        var tables = new IReadOnlyList<EncounterRow>[tableCount];
        for (var t = 0; t < tableCount; t++)
        {
            var baseLine = TableBase + t * TableSpan;
            var count = Range(baseLine, TableSpan).Count;
            var rows = new EncounterRow[count];
            for (var i = 0; i < count; i++)
            {
                if (!byNumber.TryGetValue(baseLine + i, out var text)) return Disabled($"table {t} is missing line {baseLine + i}");
                if (!ParseRow(baseLine + i, text, out var row, out var rowDetail)) return Disabled(rowDetail);
                rows[i] = row!;
            }
            tables[t] = rows;
        }
        return new(EncounterTablesStatus.Loaded, $"tables {tableCount}", frequency, power, overrides, tables);
    }

    // 0x558D40 and the loader's key reads: the weight, the groups in the order First..Fifth until one is missing, then MinLevel, MaxLevel, GlobalFlag and TriggerCount, each only when it stands next
    // (a key is read only at the cursor, so a misspelt slot name such as "Thrid:" ends the groups and leaves every later key unread).
    private static bool ParseRow(int line, string text, out EncounterRow? row, out string detail)
    {
        row = null; detail = "";
        var cursor = new Cursor(text);
        var weight = cursor.ReadInt();
        var groups = new List<EncounterGroup>();
        for (var slot = 0; slot < MaxGroups; slot++)
        {
            if (!cursor.Key(SlotKeys[slot], out var prototype)) break;
            if (!cursor.ReadRange(out var min, out var max)) { detail = $"line {line}: group size missing"; return false; }
            groups.Add(new(prototype, (short)min, (short)max));
        }
        if (groups.Count == 0) { detail = $"line {line}: no monsters"; return false; }
        int minLevel = 0, maxLevel = DefaultMaxLevel, flag = -1, cap = -1;
        if (cursor.Key("MinLevel:", out var v)) { if (v is < 0 or > DefaultMaxLevel) { detail = $"line {line}: MinLevel"; return false; } minLevel = v; }
        if (cursor.Key("MaxLevel:", out v)) { if (v is < 0 or > DefaultMaxLevel) { detail = $"line {line}: MaxLevel"; return false; } maxLevel = v; }
        if (cursor.Key("GlobalFlag:", out v)) { if (v > MaxGlobalFlag) { detail = $"line {line}: GlobalFlag"; return false; } flag = v; }
        if (cursor.Key("TriggerCount:", out v)) cap = v;
        row = new(line, weight, groups.ToArray(), minLevel, maxLevel, flag, cap);
        return true;
    }

    /// <summary>The tig string parser over one message text (0x531990, 0x531A20, 0x531AB0, 0x532320, 0x531EE0): comma-separated values read with <c>atoi</c>, white space skipped, a key found only at the cursor.</summary>
    private sealed class Cursor(string text)
    {
        private int _at; private bool _end;

        private static bool Space(char c) => c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';

        public static int Atoi(string s)
        {
            var i = 0; while (i < s.Length && Space(s[i])) i++;
            var neg = false; if (i < s.Length && s[i] is '+' or '-') neg = s[i++] == '-';
            var v = 0; while (i < s.Length && s[i] is >= '0' and <= '9') v = unchecked(v * 10 + (s[i++] - '0'));
            return neg ? unchecked(-v) : v;
        }

        // The text up to the next comma (the cursor then stands after it) or the rest (the cursor is then at the end).
        private string Token()
        {
            while (_at < text.Length && Space(text[_at])) _at++;
            var comma = text.IndexOf(',', _at);
            string token;
            if (comma < 0) { token = text[_at..]; _at = text.Length; _end = true; }
            else { token = text[_at..comma]; _at = comma + 1; }
            return token;
        }

        public int ReadInt() => _end ? 0 : Atoi(Token());

        public long ReadLong()
        {
            if (_end) return 0;
            var token = Token().TrimStart(' ', '\t', '\n', '\v', '\f', '\r');
            var i = 0; var neg = false; if (i < token.Length && token[i] is '+' or '-') neg = token[i++] == '-';
            long v = 0; for (; i < token.Length && char.IsAsciiDigit(token[i]); i++) v = unchecked(v * 10 + (token[i] - '0'));
            return neg ? -v : v;
        }

        public string? ReadToken() => _end ? null : Token().Trim();

        public bool ReadRange(out int min, out int max)
        {
            min = max = 0;
            if (_end) return false;
            var token = Token();
            var dash = token.IndexOf('-');
            min = dash < 0 ? Atoi(token) : Atoi(token[..dash]);
            max = dash < 0 ? min : Atoi(token[(dash + 1)..]);
            return true;
        }

        public bool Key(string key, out int value)
        {
            value = 0;
            if (_end) return false;
            while (_at < text.Length && Space(text[_at])) _at++;
            if (string.CompareOrdinal(text, _at, key, 0, key.Length) != 0) return false;
            _at += key.Length;
            value = Atoi(Token());
            return true;
        }
    }
}

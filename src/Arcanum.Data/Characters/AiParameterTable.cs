using Arcanum.Data.Audio;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Characters;

public enum AiParameterTableStatus
{
    Read,
    /// <summary><see cref="AiParameterTable.BasePath"/> is not in the first base source: the original's AI init fails.</summary>
    BaseMissing,
    BaseUnreadable,
    /// <summary>The base table is not one the MES grammar admits (project policy: refused instead of read partially).</summary>
    BaseRefused,
    ModuleUnreadable,
    /// <summary>The module's table is present but not one the MES grammar admits (project policy).</summary>
    ModuleRefused,
}

/// <summary>
/// One AI parameter row: the 17 integers the original keeps per row. Fields 5..8 are the follow check's (dislike at
/// reaction ≤ field 5, alignment gap limits above and below in fields 6 and 7, level gate in field 8; ledger AP3).
/// A row no entry filled is all zero, as the original's zero-initialised table.
/// </summary>
public sealed record AiParameterRow(int Index, IReadOnlyList<int> Values)
{
    public int DislikeReaction => Values[5];
    public int MaxAlignmentAbove => Values[6];
    public int MaxAlignmentBelow => Values[7];
    public int LevelGate => Values[8];
}

public sealed record AiParameterTableLoad(AiParameterTableStatus Status, AiParameterTable? Table, string Detail);

/// <summary>
/// ARC-DATA-033: the original's AI parameter table (ledger AP1, AP2). Rows 0..99 come from <see cref="BasePath"/>, read by
/// the AI init before the module is mounted (the first base source), rows 100..149 from the module's
/// <see cref="ModulePath"/>, read at module load (the content stack's winner). Each part starts at the entry numbered
/// exactly 0 (100); that entry and every later one in number order fill consecutive rows, whatever their numbers, up to the
/// part's last row. A row's text is split on space, tab and line feed and its first 17 tokens are read like C
/// <c>atoi</c>; missing tokens leave the field 0. A part whose first entry is missing fills nothing, and a missing module
/// table is no failure, as in the original. A module table that cannot be read or is refused fails the whole load (project
/// policy; the original keeps rows 0..99 then).
/// </summary>
public sealed class AiParameterTable
{
    public const string BasePath = "rules/ai_params.mes", ModulePath = "rules/gameai.mes";
    public const int Fields = 17, BaseRows = 100, Rows = 150, FirstModuleRow = 100;

    private readonly int[] _values;
    private AiParameterTable(int[] values, int baseFilled, int moduleFilled)
    {
        _values = values; BaseFilled = baseFilled; ModuleFilled = moduleFilled;
    }

    /// <summary>How many rows each part filled (rows 0.. and 100..).</summary>
    public int BaseFilled { get; }
    public int ModuleFilled { get; }

    /// <summary>The row an NPC's AI data field names (field 282, ledger AP2), or null outside 0..149: the original reads
    /// past its table there (project policy: refused).</summary>
    [OriginalFunction(0x4AAA60, OriginalFunctionScope.Partial)]
    public AiParameterRow? Row(int index) =>
        index is < 0 or >= Rows ? null : new(index, Array.AsReadOnly(_values.AsSpan(index * Fields, Fields).ToArray()));

    /// <summary>The table from the module's content stack, each part from the sources of its load phase (ARC-DATA-027).</summary>
    public static AiParameterTableLoad Open(ContentStack stack, MesReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        options ??= new(); options.Validate();
        if (ContentPhase.Candidate(stack, BasePath, ContentLoadPhase.GameStart) is not { } basePart) return new(AiParameterTableStatus.BaseMissing, null, BasePath);
        var read = stack.Read(basePart);
        if (read.Status != ContentReadStatus.Read) return new(AiParameterTableStatus.BaseUnreadable, null, $"{BasePath} {read.Status}");
        var parsed = MesFile.Parse(read.Bytes, options);
        if (parsed.Status != MesParseStatus.Parsed) return new(AiParameterTableStatus.BaseRefused, null, $"{BasePath} {parsed.Status} line {parsed.Line}");
        MesFile? module = null;
        if (ContentPhase.Candidate(stack, ModulePath, ContentLoadPhase.ModuleLoaded) is { } modulePart)
        {
            var moduleRead = stack.Read(modulePart);
            if (moduleRead.Status != ContentReadStatus.Read) return new(AiParameterTableStatus.ModuleUnreadable, null, $"{ModulePath} {moduleRead.Status}");
            var moduleParsed = MesFile.Parse(moduleRead.Bytes, options);
            if (moduleParsed.Status != MesParseStatus.Parsed) return new(AiParameterTableStatus.ModuleRefused, null, $"{ModulePath} {moduleParsed.Status} line {moduleParsed.Line}");
            module = moduleParsed.File;
        }
        var table = Create(parsed.File!, module);
        return new(AiParameterTableStatus.Read, table, $"rows {table.BaseFilled} + {table.ModuleFilled}");
    }

    /// <summary>The table of a parsed base table and, when the module has one, its table.</summary>
    public static AiParameterTable Create(MesFile baseTable, MesFile? moduleTable)
    {
        ArgumentNullException.ThrowIfNull(baseTable);
        var values = new int[Rows * Fields];
        var baseFilled = FillBase(baseTable, values);
        var moduleFilled = moduleTable is null ? 0 : FillModule(moduleTable, values);
        return new(values, baseFilled, moduleFilled);
    }

    [OriginalFunction(0x4A8320, OriginalFunctionScope.Partial)]
    private static int FillBase(MesFile table, int[] values) => Fill(table, 0, BaseRows, values);

    [OriginalFunction(0x4A8400, OriginalFunctionScope.Full)]
    private static int FillModule(MesFile table, int[] values) => Fill(table, FirstModuleRow, Rows, values);

    private static int Fill(MesFile table, int first, int end, int[] values)
    {
        var messages = table.Messages;
        var start = -1;
        for (var i = 0; i < messages.Count && start < 0; i++) if (messages[i].Number == first) start = i;
        if (start < 0) return 0;
        var row = first;
        for (var i = start; i < messages.Count && row < end; i++, row++)
        {
            var text = messages[i].Bytes.Span;
            var field = 0; var at = 0;
            while (field < Fields)
            {
                while (at < text.Length && text[at] is (byte)' ' or (byte)'\t' or (byte)'\n') at++;
                if (at == text.Length) break;
                var tokenStart = at;
                while (at < text.Length && text[at] is not ((byte)' ' or (byte)'\t' or (byte)'\n')) at++;
                var token = new char[at - tokenStart];
                for (var c = 0; c < token.Length; c++) token[c] = (char)text[tokenStart + c];
                values[row * Fields + field++] = SoundSchemeParser.Atoi(token);
            }
        }
        return row - first;
    }
}

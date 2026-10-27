using Arcanum.Data.Audio;
using Arcanum.Data.Combat;
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
/// One AI parameter row (ARC-DATA-033, typed by ARC-WORLD-077, plan slice I1): the 17 integers the original keeps per row,
/// each named after its slot in the A1 parameter dictionary (NPC AI core loop §4) with the unit its readers compare it in.
/// The values are the table's as read; a slot's rule lands with the slice of its stage (A3..A7, S22), not here. A row no
/// entry filled is all zero, as the original's zero-initialised table.
/// </summary>
public sealed record AiParameterRow
{
    /// <summary>The row <paramref name="index"/> (0..149) of the 17 <paramref name="values"/> in slot order.</summary>
    public AiParameterRow(int index, ReadOnlySpan<int> values)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, AiParameterTable.Rows);
        if (values.Length != AiParameterTable.Fields) throw new ArgumentException($"an AI parameter row has {AiParameterTable.Fields} values", nameof(values));
        Index = index;
        FleeHpPercent = values[0]; FleeOutnumbered = values[1]; FleeOutlevelled = values[2]; StandWhileTargetWeakPercent = values[3];
        FleeDistance = values[4]; FollowReaction = values[5]; FollowAlignmentAbove = values[6]; FollowAlignmentBelow = values[7];
        JoinLevelGap = values[8]; FollowerHitReaction = values[9]; HostileReaction = values[10]; HostileAlignmentGap = values[11];
        GoodFollowerStop = values[12]; DefensiveSpellRoll = values[13]; CombatHealChance = values[14]; CombatDistance = values[15];
        OpensPortals = values[16];
    }

    public int Index { get; }

    /// <summary>Slot 0, flee HP: percent of the NPC's own hit points (should-flee 0x4AB2F0, stage A4).</summary>
    public int FleeHpPercent { get; }

    /// <summary>Slot 1, flee outnumbered: a count of critters by which the other party is larger (should-flee, A4).</summary>
    public int FleeOutnumbered { get; }

    /// <summary>Slot 2, flee outlevelled: levels by which the other party's level sum is higher (should-flee, A4).</summary>
    public int FleeOutlevelled { get; }

    /// <summary>Slot 3, stand while target weak: percent of the target's hit points (should-flee, A4).</summary>
    public int StandWhileTargetWeakPercent { get; }

    /// <summary>Slot 4, flee distance: tiles from what the NPC flees (0x4AB030, A4).</summary>
    public int FleeDistance { get; }

    /// <summary>Slot 5, follow reaction: a reaction value (check follow 0x4AD950 and check leader 0x4ADB50, A7; ledger AP3).</summary>
    public int FollowReaction { get; }

    /// <summary>Slot 6, follow alignment above: alignment points by which the PC's alignment exceeds the NPC's (A7; AP3).</summary>
    public int FollowAlignmentAbove { get; }

    /// <summary>Slot 7, follow alignment below: alignment points by which the NPC's alignment exceeds the PC's (A7; AP3).</summary>
    public int FollowAlignmentBelow { get; }

    /// <summary>Slot 8, join level gap: levels added to the PC's level (check follow 0x4AD950, A7; AP3).</summary>
    public int JoinLevelGap { get; }

    /// <summary>Slot 9, follower hit reaction: a reaction change (attack event 0x4A9650, A3; Reputation A2).</summary>
    public int FollowerHitReaction { get; }

    /// <summary>Slot 10, hostile reaction: a reaction value (danger setter 0x4AABE0, attack event, kill-on-sight 0x4AE120,
    /// portal guards 0x4AEE50; A1, A3).</summary>
    public int HostileReaction { get; }

    /// <summary>Slot 11, hostile alignment gap: alignment points between the NPC and its target (kill-on-sight, A3).</summary>
    public int HostileAlignmentGap { get; }

    /// <summary>Slot 12, good follower stop: an alignment value of the target (upset attacking 0x4ADCC0, A3, A7).</summary>
    public int GoodFollowerStop { get; }

    /// <summary>Slot 13, defensive spell roll: a d100 threshold (spell choice 0x4ABC70, A5; P7).</summary>
    public int DefensiveSpellRoll { get; }

    /// <summary>Slot 14, combat heal chance: percent, compared with a d100 (heal 0x4A8940, A5).</summary>
    public int CombatHealChance { get; }

    /// <summary>Slot 15, combat distance: tiles (spell chance 0x4ABE20, perform in combat 0x4AC6E0, back off 0x4AC7B0; A4, A5).</summary>
    public int CombatDistance { get; }

    /// <summary>Slot 16, opens portals: a flag kept as stored, nonzero set (can open portals 0x4AEB10, S22).</summary>
    public int OpensPortals { get; }

    /// <summary>The 17 values in slot order.</summary>
    public IReadOnlyList<int> Values =>
    [
        FleeHpPercent, FleeOutnumbered, FleeOutlevelled, StandWhileTargetWeakPercent, FleeDistance, FollowReaction,
        FollowAlignmentAbove, FollowAlignmentBelow, JoinLevelGap, FollowerHitReaction, HostileReaction, HostileAlignmentGap,
        GoodFollowerStop, DefensiveSpellRoll, CombatHealChance, CombatDistance, OpensPortals,
    ];
}

/// <summary>The outcome of <see cref="AiParameterTable.Lookup(int)"/>; the default is a refusal, never a row.</summary>
public enum AiParameterLookupStatus
{
    /// <summary>The index is outside rows 0..149: the original reads past its table there (ledger LM3; project policy in
    /// both settings: refused).</summary>
    OutsideTable,
    Found,
}

/// <summary>The row an NPC's AI data field names, or a typed refusal with the index asked for.</summary>
public readonly record struct AiParameterLookup(AiParameterLookupStatus Status, int Index, AiParameterRow? Row);

public sealed record AiParameterTableLoad(AiParameterTableStatus Status, AiParameterTable? Table, string Detail);

/// <summary>
/// ARC-DATA-033: the original's AI parameter table (ledger AP1, AP2). Rows 0..99 come from <see cref="BasePath"/>, read by
/// the AI init before the module is mounted (the first base source), rows 100..149 from the module's
/// <see cref="ModulePath"/>, read at module load (the content stack's winner). Each part starts at the entry numbered
/// exactly 0 (100); that entry and every later one in number order fill consecutive rows, whatever their numbers, up to the
/// part's last row. A row's text is split on space, tab and line feed and its first 17 tokens are read like C
/// <c>atoi</c>; missing tokens leave the field as it was, which is 0 here: each table is built fresh, as the original's
/// first module load (ledger AP6; the original never clears its table, so another module loaded later in the same process
/// keeps the earlier module's values where its own table is shorter; project policy: not carried over). A part whose first
/// entry is missing fills nothing, and a missing module table is no failure, as in the original. A module table that cannot
/// be read or is refused fails the whole load (project policy; the original keeps rows 0..99 then).
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

    /// <summary>The row an NPC's AI data field names (field 282, ledger AP2), or <see cref="AiParameterLookupStatus.OutsideTable"/>
    /// outside 0..149: the original reads past its table there (LM3; project policy: refused).</summary>
    [OriginalFunction(0x4AAA60, OriginalFunctionScope.Partial)]
    public AiParameterLookup Lookup(int index) => index is < 0 or >= Rows
        ? new(AiParameterLookupStatus.OutsideTable, index, null)
        : new(AiParameterLookupStatus.Found, index, new(index, _values.AsSpan(index * Fields, Fields)));

    /// <summary>The row of an NPC's AI data field (<see cref="CombatNpcFields.AiData"/>).</summary>
    public AiParameterLookup Lookup(CombatNpcFields npc)
    {
        ArgumentNullException.ThrowIfNull(npc);
        return Lookup(npc.AiData);
    }

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

using Arcanum.Data.Content;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Reputations;

/// <summary>One effect of a reputation: an NPC whose origin and faction match (0 = any) adds <see cref="Adjustment"/> to its
/// reaction to a PC holding the reputation.</summary>
public readonly record struct ReputationEffect(int Adjustment, int Origin, int Faction);

/// <summary>
/// One reputation of <c>rules\gamerep.mes</c>: the three factions whose members treat the holder as one of them (0 = none) and
/// up to five effects. <see cref="Complete"/> is false for the entry that stopped the original's parse: it keeps the faction
/// numbers read before the stop and no effect, as the original leaves it in its table.
/// </summary>
public sealed record ReputationRecord(int Number, IReadOnlyList<int> Factions, IReadOnlyList<ReputationEffect> Effects, bool Complete = true);

public enum ReputationTableStatus { Ready, ReadFailed, ParseFailed, OutOfRange }

/// <summary>The reputation data of a module, or the file that could not be read or parsed (<paramref name="Path"/>).</summary>
public sealed record ReputationTableLoad(ReputationTableStatus Status, ReputationTable? Table, string? Path = null, ContentReadStatus? Read = null,
    MesParseResult? Parse = null, int? Number = null);

/// <summary>
/// ARC-WORLD-044: the reputation data the original loads at module load (checked in the 1.0.7.4 executable by two readers,
/// docs/research/original-verification-ledger.md "Reputations"): the reputations of <c>rules\gamerep.mes</c> (entries
/// 1000..1999), their logbook names (<c>mes\gamereplog.mes</c>) and the four greeting files
/// <c>mes\game_rp_npc_{m2m,m2f,f2f,f2m}.mes</c>. Universal: whatever module the stack mounts.
/// <list type="bullet">
/// <item>No <c>gamerep.mes</c>, or an entry missing one of its three faction numbers, ends the table there (earlier entries
/// stay) and then the names and greetings are not loaded (<see cref="Aborted"/>).</item>
/// <item>No <c>gamereplog.mes</c>: the greetings are not loaded either. A missing greeting file is skipped.</item>
/// <item>Every effect keeps the origin and faction it is written with, signed or not (owner decision D1, ledger Reputation P1).</item>
/// </list>
/// A file that cannot be read or parsed as MES refuses the whole load, and so does a number beyond the 32-bit range, which
/// the original would wrap (project policy).
/// </summary>
public sealed class ReputationTable
{
    public const string RulesPath = "rules/gamerep.mes", NamesPath = "mes/gamereplog.mes";
    /// <summary>The greeting files in the original's handle order: NPC gender to PC gender.</summary>
    public static readonly IReadOnlyList<string> GreetingPaths =
        ["mes/game_rp_npc_m2m.mes", "mes/game_rp_npc_m2f.mes", "mes/game_rp_npc_f2f.mes", "mes/game_rp_npc_f2m.mes"];
    public const int First = 1000, Last = 1999, FactionCount = 3, MaxEffects = 5;

    private readonly Dictionary<int, ReputationRecord> _records;

    private ReputationTable(IReadOnlyList<ReputationRecord> records, bool rulesLoaded, int? aborted, MesFile? names, IReadOnlyList<MesFile?> greetings)
    {
        Records = records;
        _records = records.ToDictionary(record => record.Number);
        RulesLoaded = rulesLoaded; Aborted = aborted; Names = names; Greetings = greetings;
    }

    /// <summary>The reputations in number order.</summary>
    public IReadOnlyList<ReputationRecord> Records { get; }
    /// <summary>Whether the module has <c>rules\gamerep.mes</c>.</summary>
    public bool RulesLoaded { get; }
    /// <summary>The entry that stopped the parse (missing faction numbers), or null.</summary>
    public int? Aborted { get; }
    public MesFile? Names { get; }
    /// <summary>The four greeting files (m2m, m2f, f2f, f2m); an entry is null when the file is absent or not loaded.</summary>
    public IReadOnlyList<MesFile?> Greetings { get; }

    public ReputationRecord? Find(int number) => _records.GetValueOrDefault(number);

    /// <summary>The logbook name of a reputation (0x4C1BD0): the names file's entry of that number, null below 1000, without the
    /// file or without the entry.</summary>
    [OriginalFunction(0x4C1BD0, OriginalFunctionScope.Partial)]
    public MesMessage? Name(int number) => number < First ? null : Names?.Find(number);

    /// <summary>Builds a table from already-parsed files (synthetic data or another loader), with the original's skip rules.</summary>
    public static ReputationTableLoad Create(MesFile? rules, MesFile? names = null, IReadOnlyList<MesFile?>? greetings = null)
    {
        if (greetings is not null && greetings.Count != GreetingPaths.Count) throw new ArgumentException("Four greeting files.", nameof(greetings));
        if (rules is null) return new(ReputationTableStatus.Ready, new([], false, null, null, Absent()));
        var parsed = Parse(rules, out var aborted);
        if (parsed is null) return new(ReputationTableStatus.OutOfRange, null, RulesPath, Number: aborted);
        if (aborted is not null) return new(ReputationTableStatus.Ready, new(parsed, true, aborted, null, Absent()));
        return new(ReputationTableStatus.Ready, new(parsed, true, null, names, names is null ? Absent() : greetings ?? Absent()));
    }

    /// <summary>
    /// The module load (0x4C1620): the rules, then the names, then the greetings, each from the stack's winner at module load.
    /// </summary>
    [OriginalFunction(0x4C1620, OriginalFunctionScope.Partial)]
    public static ReputationTableLoad Load(ContentStack stack, MesReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        options ??= new();
        options.Validate();
        var failure = Read(stack, RulesPath, options, out var rules);
        if (failure is not null) return failure;
        if (rules is null) return Create(null);
        var parsed = Parse(rules, out var aborted);
        if (parsed is null) return new(ReputationTableStatus.OutOfRange, null, RulesPath, Number: aborted);
        if (aborted is not null) return new(ReputationTableStatus.Ready, new(parsed, true, aborted, null, Absent()));
        failure = Read(stack, NamesPath, options, out var names);
        if (failure is not null) return failure;
        if (names is null) return new(ReputationTableStatus.Ready, new(parsed, true, null, null, Absent()));
        var greetings = new MesFile?[GreetingPaths.Count];
        for (var i = 0; i < greetings.Length; i++)
        {
            failure = Read(stack, GreetingPaths[i], options, out greetings[i]);
            if (failure is not null) return failure;
        }
        return new(ReputationTableStatus.Ready, new(parsed, true, null, names, Array.AsReadOnly(greetings)));
    }

    /// <summary>
    /// The entry parse (0x4C1730): entries 1000..1999 by number (an absent one is skipped); the text is split on commas, empty
    /// pieces skipped; the first piece holds the three faction numbers, each further piece one effect (adjustment, origin,
    /// faction) up to five, more ignored; an effect with fewer than three numbers keeps 0 for the missing ones and counts. An
    /// entry whose first piece ends before its third faction number stops the parse: it is kept with the numbers read before
    /// the stop (<see cref="ReputationRecord.Complete"/> false) and <paramref name="aborted"/> names it. Null (with
    /// <paramref name="aborted"/> the entry) when a number does not fit 32 bits.
    /// </summary>
    [OriginalFunction(0x4C1730, OriginalFunctionScope.Partial)]
    internal static IReadOnlyList<ReputationRecord>? Parse(MesFile rules, out int? aborted)
    {
        aborted = null;
        var records = new List<ReputationRecord>();
        for (var number = First; number <= Last; number++)
        {
            if (rules.Find(number) is not { } message) continue;
            var pieces = Split(message.Bytes.Span);
            var factions = new int[FactionCount];
            var read = pieces.Count == 0 ? 0 : Numbers(pieces[0], factions);
            if (read < 0) { aborted = number; return null; }
            if (read < FactionCount)
            {
                records.Add(new(number, Array.AsReadOnly(factions), [], Complete: false));
                aborted = number;
                return records.AsReadOnly();
            }
            var effects = new List<ReputationEffect>();
            foreach (var piece in pieces.Skip(1).Take(MaxEffects))
            {
                var values = new int[3];
                if (Numbers(piece, values) < 0) { aborted = number; return null; }
                effects.Add(new(values[0], values[1], values[2]));
            }
            records.Add(new(number, Array.AsReadOnly(factions), effects.AsReadOnly()));
        }
        return records.AsReadOnly();
    }

    // strtok on ",": empty pieces are skipped.
    private static List<byte[]> Split(ReadOnlySpan<byte> text)
    {
        var pieces = new List<byte[]>();
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] != (byte)',') continue;
            if (i > start) pieces.Add(text[start..i].ToArray());
            start = i + 1;
        }
        return pieces;
    }

    /// <summary>
    /// The original's number scan of one piece into <paramref name="into"/>: a number by <c>atoi</c>, then before each further
    /// number skip spaces (the space character only) and a run of digits, '+' and '-'; the end of the piece before a needed
    /// number stops the scan. Returns how many numbers were read, or −1 for a number beyond 32 bits.
    /// </summary>
    private static int Numbers(byte[] piece, int[] into)
    {
        var at = 0;
        for (var n = 0; n < into.Length; n++)
        {
            if (n > 0)
            {
                while (at < piece.Length && piece[at] == (byte)' ') at++;
                if (at == piece.Length) return n;
                while (at < piece.Length && piece[at] is (>= (byte)'0' and <= (byte)'9') or (byte)'+' or (byte)'-') at++;
                if (at == piece.Length) return n;
            }
            if (Atoi(piece, at) is not { } value) return -1;
            into[n] = value;
        }
        return into.Length;
    }

    // C atoi: leading white space, an optional sign, decimal digits; anything else ends it (no digits is 0).
    private static int? Atoi(byte[] text, int at)
    {
        while (at < text.Length && text[at] is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\v' or (byte)'\f' or (byte)'\r') at++;
        var negative = false;
        if (at < text.Length && text[at] is (byte)'+' or (byte)'-') negative = text[at++] == (byte)'-';
        long value = 0;
        while (at < text.Length && text[at] is >= (byte)'0' and <= (byte)'9')
        {
            value = value * 10 + (text[at++] - (byte)'0');
            if (value > 2_147_483_648L) return null;
        }
        value = negative ? -value : value;
        return value is < int.MinValue or > int.MaxValue ? null : (int)value;
    }

    private static IReadOnlyList<MesFile?> Absent() => Array.AsReadOnly(new MesFile?[GreetingPaths.Count]);

    private static ReputationTableLoad? Read(ContentStack stack, string path, MesReaderOptions options, out MesFile? file)
    {
        file = null;
        if (ContentPhase.Candidate(stack, path, ContentLoadPhase.ModuleLoaded) is not { } source) return null;
        if (source.Length > options.MaxBytes) return new(ReputationTableStatus.ParseFailed, null, path, Parse: new(MesParseStatus.TooLarge, null));
        var read = stack.Read(source);
        if (read.Status != ContentReadStatus.Read) return new(ReputationTableStatus.ReadFailed, null, path, read.Status);
        var parsed = MesFile.Parse(read.Bytes!, options);
        if (parsed.Status != MesParseStatus.Parsed) return new(ReputationTableStatus.ParseFailed, null, path, Parse: parsed);
        file = parsed.File;
        return null;
    }
}

namespace Arcanum.Session;

/// <summary>ARC-DEV-098 (MIG8, GR29, TM9; SA13, SA14): the bounds of the script stores the session keeps beside the global flags and the story number.</summary>
public static class SessionScriptLimits
{
    /// <summary>The global variables (SA13: <c>calloc(2000, 4)</c>, operand kind 1).</summary>
    public const int GlobalVariables = 2000;

    /// <summary>The indexes of the PC's flags and variables: the operand packs the index in its low 16 bits (SA14).</summary>
    public const int PcIndexes = 1 << 16;

    /// <summary>The 32-bit elements of the PC's flag array: flag n is bit n mod 32 of element n / 32 (SA14).</summary>
    public const int PcFlagElements = PcIndexes / 32;

    public static bool IsGlobalVariable(int index) => index is >= 0 and < GlobalVariables;

    public static bool IsPcIndex(int index) => index is >= 0 and < PcIndexes;
}

/// <summary>One non-zero element of a sparse script store: its index and its 32-bit value.</summary>
public readonly record struct SessionScriptCell(int Index, int Value);

/// <summary>
/// ARC-DEV-098: the script stores of a session, as saved (schema 67, the <c>scripts</c> section): the 2000 global variables and the PC's
/// flag and variable arrays (fields 272 and 273 of the hero). Each list holds only the non-zero elements, ascending by index; a store
/// the game never wrote is empty, and so is a new game (SA13: init and reset zero them). The PC's arrays belong to a hero; a session without
/// one holds none (the accessors act only on a PC, SA14).
/// </summary>
/// <param name="GlobalVariables">The non-zero global variables, index 0..1999.</param>
/// <param name="PcFlags">The non-zero 32-bit elements of the PC's flags: element n holds flags 32n..32n+31 (bit 0 first), n 0..2047.</param>
/// <param name="PcVariables">The non-zero PC variables, index 0..65535.</param>
public sealed record SessionScriptState(
    IReadOnlyList<SessionScriptCell> GlobalVariables, IReadOnlyList<SessionScriptCell> PcFlags, IReadOnlyList<SessionScriptCell> PcVariables)
{
    public static SessionScriptState Empty { get; } = new([], [], []);

    public bool IsEmpty => GlobalVariables.Count == 0 && PcFlags.Count == 0 && PcVariables.Count == 0;
}

/// <summary>The mutable script stores of one owner. A zero element is not stored, so the capture is canonical whatever order the writes came in.</summary>
internal sealed class ScriptStores
{
    internal Dictionary<int, int> GlobalVariables { get; } = [];
    internal Dictionary<int, int> PcFlags { get; } = [];
    internal Dictionary<int, int> PcVariables { get; } = [];

    internal static int Read(Dictionary<int, int> store, int index) => store.GetValueOrDefault(index);

    internal static void Write(Dictionary<int, int> store, int index, int value)
    {
        if (value == 0) store.Remove(index);
        else store[index] = value;
    }

    internal SessionScriptState Capture() => new(Cells(GlobalVariables), Cells(PcFlags), Cells(PcVariables));

    private static SessionScriptCell[] Cells(Dictionary<int, int> store) =>
        [.. store.OrderBy(pair => pair.Key).Select(pair => new SessionScriptCell(pair.Key, pair.Value))];
}

public sealed partial class GameSessionOwner
{
    private ScriptStores _scripts = new();

    /// <summary>ARC-DEV-098: the global variables and the PC's flags and variables, as the snapshot saves them.</summary>
    public SessionScriptState QueryScriptState() { ThrowIfDisposed(); return _scripts.Capture(); }

    /// <summary>ARC-DEV-098: global variable <paramref name="index"/> (SA13, 0x444C80), 0 until written; the original does not check the index, the
    /// session refuses one outside 0..1999 with an argument error.</summary>
    public int QueryGlobalVariable(int index)
    {
        ThrowIfDisposed();
        if (!SessionScriptLimits.IsGlobalVariable(index)) throw new ArgumentOutOfRangeException(nameof(index));
        return ScriptStores.Read(_scripts.GlobalVariables, index);
    }

    /// <summary>ARC-DEV-098: stores a global variable (SA13, 0x444C90). An index outside 0..1999 is <see cref="GameSessionFailure.InvalidScriptIndex"/>
    /// (the original reads and writes past the array, which the project does not reproduce); the same value is NoChange.</summary>
    public GameSessionMutationResult SetGlobalVariable(GameSessionVersion expected, int index, int value) =>
        SetScript(expected, SessionScriptLimits.IsGlobalVariable(index), false, _scripts.GlobalVariables, index, value);

    /// <summary>ARC-DEV-098: PC variable <paramref name="index"/> of the hero (SA14, 0x444DB0), 0 until written and for a session without a hero (the
    /// accessor reads 0 on any object that is not a PC).</summary>
    public int QueryPcVariable(int index)
    {
        ThrowIfDisposed();
        if (!SessionScriptLimits.IsPcIndex(index)) throw new ArgumentOutOfRangeException(nameof(index));
        return ScriptStores.Read(_scripts.PcVariables, index);
    }

    /// <summary>ARC-DEV-098: stores a PC variable (SA14, 0x444DF0). A session without a hero is <see cref="GameSessionFailure.PcScriptsUnavailable"/>
    /// (the original ignores a write to an object that is not a PC); an index outside 0..65535 is <see cref="GameSessionFailure.InvalidScriptIndex"/>.</summary>
    public GameSessionMutationResult SetPcVariable(GameSessionVersion expected, int index, int value) =>
        SetScript(expected, SessionScriptLimits.IsPcIndex(index), true, _scripts.PcVariables, index, value);

    /// <summary>ARC-DEV-098: PC flag <paramref name="flag"/> of the hero (SA14, 0x444E30): bit flag mod 32 of element flag / 32; false for a session
    /// without a hero.</summary>
    public bool QueryPcFlag(int flag)
    {
        ThrowIfDisposed();
        if (!SessionScriptLimits.IsPcIndex(flag)) throw new ArgumentOutOfRangeException(nameof(flag));
        return (ScriptStores.Read(_scripts.PcFlags, flag >> 5) >> (flag & 31) & 1) != 0;
    }

    /// <summary>ARC-DEV-098: sets or clears a PC flag (SA14, 0x444E90), the other 31 flags of its element untouched. Failures as
    /// <see cref="SetPcVariable"/>.</summary>
    public GameSessionMutationResult SetPcFlag(GameSessionVersion expected, int flag, bool value)
    {
        ThrowIfDisposed();
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (!SessionScriptLimits.IsPcIndex(flag)) return Reject(GameSessionFailure.InvalidScriptIndex);
        if (_hero is null) return Reject(GameSessionFailure.PcScriptsUnavailable);
        var element = flag >> 5;
        var word = ScriptStores.Read(_scripts.PcFlags, element);
        var next = value ? word | 1 << (flag & 31) : word & ~(1 << (flag & 31));
        if (next == word) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        ScriptStores.Write(_scripts.PcFlags, element, next);
        return Changed();
    }

    private GameSessionMutationResult SetScript(GameSessionVersion expected, bool inRange, bool pc, Dictionary<int, int> store, int index, int value)
    {
        ThrowIfDisposed();
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (!inRange) return Reject(GameSessionFailure.InvalidScriptIndex);
        if (pc && _hero is null) return Reject(GameSessionFailure.PcScriptsUnavailable);
        if (ScriptStores.Read(store, index) == value) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        ScriptStores.Write(store, index, value);
        return Changed();
    }

    /// <summary>The saved script stores as the session holds them; a PC store needs a hero to own it.</summary>
    private static GameSessionFailure ImportScripts(SessionScriptState saved, bool hasHero, out ScriptStores stores)
    {
        stores = new();
        if (!hasHero && (saved.PcFlags.Count > 0 || saved.PcVariables.Count > 0)) return GameSessionFailure.InvalidSnapshot;
        return Fill(saved.GlobalVariables, SessionScriptLimits.GlobalVariables, stores.GlobalVariables) is var global and not GameSessionFailure.None ? global
            : Fill(saved.PcFlags, SessionScriptLimits.PcFlagElements, stores.PcFlags) is var flags and not GameSessionFailure.None ? flags
            : Fill(saved.PcVariables, SessionScriptLimits.PcIndexes, stores.PcVariables);
    }

    // Ascending, unique, in range, non-zero: the one spelling Capture writes.
    private static GameSessionFailure Fill(IReadOnlyList<SessionScriptCell> cells, int count, Dictionary<int, int> into)
    {
        var last = -1;
        foreach (var cell in cells)
        {
            if (cell.Index <= last || cell.Index >= count || cell.Value == 0) return GameSessionFailure.InvalidSnapshot;
            into.Add(cell.Index, cell.Value);
            last = cell.Index;
        }
        return GameSessionFailure.None;
    }
}

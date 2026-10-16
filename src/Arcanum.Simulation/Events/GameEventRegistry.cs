using System.Collections.Immutable;

namespace Arcanum.Simulation.Events;

/// <summary>
/// The closed, versioned set of event kinds a queue accepts. Construction checks each definition and throws
/// <see cref="ArgumentException"/> on a programming error (bad name, duplicate name, undefined enum value, slot counts,
/// key slots beyond the counts, a merge function without <see cref="GameEventUniqueness.Merge"/> or the reverse, a
/// scoped kind without entities). Immutable.
/// </summary>
public sealed class GameEventRegistry
{
    public const int MaxNameCharacters = 64;
    private readonly Dictionary<string, GameEventKindDefinition> _kinds;

    public GameEventRegistry(int version, IEnumerable<GameEventKindDefinition> kinds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        ArgumentNullException.ThrowIfNull(kinds);
        Version = version;
        _kinds = new(StringComparer.Ordinal);
        foreach (var kind in kinds)
        {
            Check(kind);
            if (!_kinds.TryAdd(kind.Name, kind)) throw new ArgumentException($"Duplicate event kind '{kind.Name}'.", nameof(kinds));
        }
        if (_kinds.Count == 0) throw new ArgumentException("A registry needs at least one kind.", nameof(kinds));
        Kinds = _kinds.Values.OrderBy(k => k.Name, StringComparer.Ordinal).ToImmutableArray();
    }

    /// <summary>A snapshot restores only into a registry of the same version.</summary>
    public int Version { get; }

    /// <summary>The kinds in ordinal name order.</summary>
    public ImmutableArray<GameEventKindDefinition> Kinds { get; }

    public bool TryGet(string? name, out GameEventKindDefinition kind)
    {
        if (name is not null && _kinds.TryGetValue(name, out var found)) { kind = found; return true; }
        kind = null!;
        return false;
    }

    /// <summary>Event and entity codes: 1..max ASCII letters, digits and <c>. _ : -</c>.</summary>
    internal static bool IsCode(string? value, int max)
    {
        if (value is null || value.Length < 1 || value.Length > max) return false;
        foreach (var c in value)
            if (c is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '.' or '_' or ':' or '-'))
                return false;
        return true;
    }

    private static void Check(GameEventKindDefinition kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        if (!IsCode(kind.Name, MaxNameCharacters)) throw Bad(kind, "has an invalid name");
        if (!Enum.IsDefined(kind.Domain) || !Enum.IsDefined(kind.Persistence) || !Enum.IsDefined(kind.Uniqueness) ||
            !Enum.IsDefined(kind.Scope)) throw Bad(kind, "has an undefined domain, persistence, uniqueness or scope");
        if (kind.EntityCount < 0 || kind.ValueCount < 0) throw Bad(kind, "has a negative slot count");
        if (kind.KeyEntities < 0 || kind.KeyEntities > kind.EntityCount || kind.KeyValues < 0 || kind.KeyValues > kind.ValueCount)
            throw Bad(kind, "has key slots outside its slot counts");
        if (kind.Uniqueness == GameEventUniqueness.Many && (kind.KeyEntities != 0 || kind.KeyValues != 0))
            throw Bad(kind, "declares key slots without a uniqueness policy");
        if ((kind.Uniqueness == GameEventUniqueness.Merge) != (kind.Merge is not null))
            throw Bad(kind, "needs a merge function exactly when its uniqueness is Merge");
        if (kind.Scope != GameEventScopeRule.Global && kind.EntityCount == 0) throw Bad(kind, "is scoped but has no entity");
        if (kind.Handler is null) throw Bad(kind, "has no handler");
    }

    private static ArgumentException Bad(GameEventKindDefinition kind, string what) =>
        new($"Event kind '{kind.Name}' {what}.", "kinds");
}

/// <summary>
/// Delay arithmetic for producers. The source builds a real-seconds delay as <c>seconds × 1000</c> and then multiplies
/// only the millisecond word by 8, which overflows or wraps for large or negative operands (anomaly TE1); this is the
/// project default instead: one checked signed 64-bit product, refused when negative or overflowing.
/// </summary>
public static class GameEventDelays
{
    /// <summary>Game milliseconds per real second at the source's 8× rate (ARC-RES-046 K2).</summary>
    public const long GameMillisecondsPerRealSecond = 8_000;

    public static bool TryFromRealSeconds(long seconds, out long gameMilliseconds)
    {
        gameMilliseconds = 0;
        if (seconds < 0 || seconds > long.MaxValue / GameMillisecondsPerRealSecond) return false;
        gameMilliseconds = seconds * GameMillisecondsPerRealSecond;
        return true;
    }
}

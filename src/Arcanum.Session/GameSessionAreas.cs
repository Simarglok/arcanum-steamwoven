using Arcanum.Simulation;

namespace Arcanum.Session;

/// <summary>
/// ARC-WORLD-056: the PC's world-map areas (ledger WT2, WT3), part of the story domain: the areas the PC knows and its last
/// discovered area. Travel (Arcanum.Simulation.Travel) discovers areas; scripts and the interface may mark one known too.
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>Whether the PC knows an area; area 0 and numbers outside 1..<see cref="SessionStoryLimits.LastArea"/> are never known.</summary>
    public bool IsAreaKnown(int area)
    {
        ThrowIfDisposed();
        return SessionStoryLimits.IsArea(area) && _story.KnownAreas.Contains(area);
    }

    /// <summary>The areas the PC knows, ascending.</summary>
    public IReadOnlyList<int> QueryKnownAreas()
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_story.KnownAreas.Order().ToArray());
    }

    /// <summary>The PC's last discovered area, 0 for none.</summary>
    public int QueryLastDiscoveredArea()
    {
        ThrowIfDisposed();
        return _story.LastArea;
    }

    /// <summary>Marks an area known (0x4CAFD0): an unknown area becomes known and the last discovered one; a known area is
    /// NoChange. One revision.</summary>
    [OriginalFunction(0x4CAFD0, OriginalFunctionScope.Partial)]
    public GameSessionMutationResult DiscoverArea(GameSessionVersion expected, int area)
    {
        ThrowIfDisposed();
        if (!SessionStoryLimits.IsArea(area)) return Reject(GameSessionFailure.InvalidArea);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_story.KnownAreas.Contains(area)) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _story.KnownAreas.Add(area);
        _story.LastArea = area;
        return Changed();
    }

    /// <summary>Clears the last discovered area (0x4CB160), as each travel step does after its discovery (ledger WT3). One
    /// revision; NoChange when there is none.</summary>
    [OriginalFunction(0x4CB160, OriginalFunctionScope.Full)]
    public GameSessionMutationResult ClearLastDiscoveredArea(GameSessionVersion expected)
    {
        ThrowIfDisposed();
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_story.LastArea == 0) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _story.LastArea = 0;
        return Changed();
    }
}

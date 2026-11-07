using Arcanum.Simulation;

namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-205 (bug B22): the town maps revealed whole, part of the story domain. The original keeps a bit per cell of every town map in a file
/// of the save folder (<c>&lt;town map&gt;.tmf</c>, PS25) and unfog town map (script action 113) sets every bit of one (ledger UF1, UF2); the
/// session keeps only the whole-map reveal. NOT READY: the cells the PC reveals by walking and the town-map window that shows them.
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>Whether a town map is revealed whole; 0 and numbers outside 1..<see cref="SessionStoryLimits.LastTownMap"/> never are.</summary>
    public bool IsTownMapRevealed(int townMap)
    {
        ThrowIfDisposed();
        return SessionStoryLimits.IsTownMap(townMap) && _story.RevealedTownMaps.Contains(townMap);
    }

    /// <summary>The town maps revealed whole, ascending.</summary>
    public IReadOnlyList<int> QueryRevealedTownMaps()
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_story.RevealedTownMaps.Order().ToArray());
    }

    /// <summary>
    /// Reveals every cell of a town map (0x4BEAB0 with the reveal flag, ledger UF1): a town map not yet revealed whole becomes so; a revealed
    /// one is NoChange (the original fills the same bits again). One revision. A number outside 1..<see cref="SessionStoryLimits.LastTownMap"/>
    /// is refused (InvalidTownMap). The original also does nothing for a number its town-map list (<c>rules\townmap.mes</c>) or info file
    /// does not hold; the session reads neither and keeps such a number (nothing shows it).
    /// </summary>
    [OriginalFunction(0x4BEAB0, OriginalFunctionScope.Partial)]
    public GameSessionMutationResult RevealTownMap(GameSessionVersion expected, int townMap)
    {
        ThrowIfDisposed();
        if (!SessionStoryLimits.IsTownMap(townMap)) return Reject(GameSessionFailure.InvalidTownMap);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_story.RevealedTownMaps.Contains(townMap)) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _story.RevealedTownMaps.Add(townMap);
        return Changed();
    }
}

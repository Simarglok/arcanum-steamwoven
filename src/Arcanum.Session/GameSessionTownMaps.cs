using Arcanum.Simulation;

namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-205 (bug B22): the town maps revealed whole, part of the story domain. The original keeps a bit per cell of every town map in a file
/// of the save folder (<c>&lt;town map&gt;.tmf</c>, PS25) and unfog town map (script action 113) sets every bit of one (ledger UF1, UF2); the
/// session keeps the whole-map reveal and, since ARC-WORLD-122 (schema 75), the cells the PC reveals by walking (WM78: one tile of the town's picture
/// for the PC's location, set whenever the PC changes location in a sector with a town map).
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>Whether a town map is revealed whole; 0 and numbers outside 1..<see cref="SessionStoryLimits.LastTownMap"/> never are.</summary>
    public bool IsTownMapRevealed(int townMap)
    {
        ThrowIfDisposed();
        return SessionStoryLimits.IsTownMap(townMap) && _story.RevealedTownMaps.Contains(townMap);
    }

    /// <summary>ARC-WORLD-122: whether a cell (a tile of the town's picture, index = row × columns + column) is revealed: the town map is revealed whole or the
    /// cell's bit is set (0x4BEDD0 reads the bit; the whole reveal fills every bit, UF1). Numbers outside the ranges are never revealed.</summary>
    public bool IsTownMapCellRevealed(int townMap, int cell)
    {
        ThrowIfDisposed();
        if (!SessionStoryLimits.IsTownMap(townMap) || !SessionStoryLimits.IsTownMapCell(cell)) return false;
        if (_story.RevealedTownMaps.Contains(townMap)) return true;
        return _story.TownMapCells.TryGetValue(townMap, out var bits) && cell / 8 < bits.Length && (bits[cell / 8] & (1 << (cell % 8))) != 0;
    }

    /// <summary>ARC-WORLD-122: the number of cells walked into on a town map (0 when none; a whole reveal does not add to it).</summary>
    public int QueryTownMapCellCount(int townMap)
    {
        ThrowIfDisposed();
        return _story.TownMapCells.TryGetValue(townMap, out var bits) ? bits.Sum(b => System.Numerics.BitOperations.PopCount(b)) : 0;
    }

    /// <summary>The town maps revealed whole, ascending.</summary>
    public IReadOnlyList<int> QueryRevealedTownMaps()
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_story.RevealedTownMaps.Order().ToArray());
    }

    /// <summary>
    /// ARC-WORLD-122 (0x4BED90, ledger WM78): sets the bit of one cell of a town map, as the original does whenever the PC changes location in a sector with a town
    /// map (the caller names the tile under the new location). A cell already set, or any cell of a town map revealed whole, is NoChange. One revision. A town map
    /// outside 1..<see cref="SessionStoryLimits.LastTownMap"/> or a cell outside 0..<see cref="SessionStoryLimits.LastTownMapCell"/> is refused (InvalidTownMap), as is a
    /// town map beyond <see cref="SessionStoryLimits.MaxTownMapsWithCells"/> holding cells.
    /// </summary>
    [OriginalFunction(0x4BED90, OriginalFunctionScope.Partial)]
    public GameSessionMutationResult RevealTownMapCell(GameSessionVersion expected, int townMap, int cell)
    {
        ThrowIfDisposed();
        if (!SessionStoryLimits.IsTownMap(townMap) || !SessionStoryLimits.IsTownMapCell(cell)) return Reject(GameSessionFailure.InvalidTownMap);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (IsTownMapCellRevealed(townMap, cell)) return NoChange();
        var held = _story.TownMapCells.TryGetValue(townMap, out var bits);
        if (!held && _story.TownMapCells.Count >= SessionStoryLimits.MaxTownMapsWithCells) return Reject(GameSessionFailure.InvalidTownMap);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        if (!held || cell / 8 >= bits!.Length)
        {
            var grown = new byte[cell / 8 + 1];
            bits?.CopyTo(grown, 0);
            bits = grown;
            _story.TownMapCells[townMap] = bits;
        }
        bits![cell / 8] |= (byte)(1 << (cell % 8));
        return Changed();
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

    /// <summary>ARC-UI-104 (ledger WM91): the notes a town map holds, null for none (a town map outside 1..<see cref="SessionStoryLimits.LastTownMap"/> never has any).</summary>
    public SessionTownNotes? QueryTownNotes(int townMap)
    {
        ThrowIfDisposed();
        return SessionStoryLimits.IsTownMap(townMap) && _story.TownNotes.TryGetValue(townMap, out var notes) ? notes : null;
    }

    /// <summary>
    /// ARC-UI-104: keeps the notes of a town map (the viewer adds them as the PC hovers marked scenery, WM127, and hands the whole list over): the same list is NoChange; an
    /// empty list removes the town's entry; a list over the limits or not canonical (ids not ascending from 200 below the next id, text over 249 characters) is
    /// refused (InvalidTownMap). One revision. A town map beyond <see cref="SessionStoryLimits.MaxTownMapsWithCells"/> holding notes is refused too.
    /// </summary>
    public GameSessionMutationResult RecordTownNotes(GameSessionVersion expected, int townMap, int nextId, IReadOnlyList<SessionTownNote> notes)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(notes);
        if (!SessionStoryLimits.IsTownMap(townMap)) return Reject(GameSessionFailure.InvalidTownMap);
        var candidate = new SessionTownNotes(townMap, nextId, notes);
        if (notes.Count > 0 && !candidate.IsValid()) return Reject(GameSessionFailure.InvalidTownMap);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var held = _story.TownNotes.TryGetValue(townMap, out var current);
        if (notes.Count == 0)
        {
            if (!held) return NoChange();
            if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
            _story.TownNotes.Remove(townMap);
            return Changed();
        }
        if (held && current!.NextId == nextId && current.Notes.SequenceEqual(candidate.Notes)) return NoChange();
        if (!held && _story.TownNotes.Count >= SessionStoryLimits.MaxTownMapsWithCells) return Reject(GameSessionFailure.InvalidTownMap);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _story.TownNotes[townMap] = candidate;
        return Changed();
    }
}

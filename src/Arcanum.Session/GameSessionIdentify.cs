namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-MAGIC-032 (MA-7; ledger XR4): Divine Magick's ObjFlag line (<c>obj_f_item_flags</c>, <c>OIF_IDENTIFIED</c> 0x1, set through 0x454A10, a plain write
    /// with no side effect and no reference count) as the session keeps it: the item joins the identified marks a barter window sets (<see cref="QueryItemIdentified"/>;
    /// saved in the inventory section, so no schema step). The item's own stored flag is the host's, as for the barter marks. Refused for an invalid
    /// entity (<see cref="GameSessionFailure.InvalidItem"/>), a stale version, an item the session does not track or deleted
    /// (<see cref="GameSessionFailure.ItemNotAvailable"/>) and a full mark budget (<see cref="GameSessionFailure.BudgetExceeded"/>). An item already marked is
    /// <see cref="GameSessionDisposition.NoChange"/>. One revision.
    /// </summary>
    public GameSessionMutationResult MarkItemIdentified(GameSessionVersion expected, string item)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(item)) return new(GameSessionDisposition.Rejected, GameSessionFailure.InvalidItem, Version);
        if (!IsCurrent(expected)) return new(GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion, Version);
        if (!TracksItem(item)) return new(GameSessionDisposition.Rejected, GameSessionFailure.ItemNotAvailable, Version);
        if (_inventory.Identified.Contains(item)) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);
        if (_inventory.Identified.Count >= SessionInventoryState.MaxIdentified) return new(GameSessionDisposition.Rejected, GameSessionFailure.BudgetExceeded, Version);
        if (_revision == long.MaxValue) return new(GameSessionDisposition.Rejected, GameSessionFailure.RevisionExhausted, Version);
        _inventory.Identified.Add(item);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }
}

using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-042 (T-m1, ledger TI5): the outcome of <see cref="GameSessionOwner.TransformPlacedItem"/>: the critter the item became (<see cref="Critter"/>, a created critter on the item's place) and the item that was destroyed
/// (<see cref="Destroyed"/>), both null when the call changed nothing.
/// </summary>
public sealed record SessionTransformResult(GameSessionDisposition Disposition, GameSessionFailure Failure, SessionCreatedObject? Critter, string? Destroyed, GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-MAGIC-042 (T-m1, TI5, the script action transform into a basic prototype, 0x447E89): the hero's item lying on the ground (a thrown item that landed) becomes a critter of <paramref name="prototype"/> (the prototype file's number: the
    /// script's basic number + 20, as <see cref="CreateCritterNear"/> takes it) on the item's own cell and map, and the item is destroyed (0x43CCA0: the one destroy path, <see cref="DestroyObject"/>), in one revision. The critter is a plain
    /// created critter (<see cref="SessionCreatedKind.Critter"/>, no encounter flag: the action makes the object with the ordinary creator 0x4ED8B0, not the create-object action's). The session reads no game data, so the prototype being a critter is
    /// the host's to have checked. Not carried (limits of the first slice): the old art's rotation and the hit-point share (TI5: the item has full points in every installed case, a creature item is never damaged). Refused before any effect: an item
    /// that is not lying on the ground (<see cref="GameSessionFailure.ItemNotAvailable"/>), an invalid prototype, the created-object bound, the version, the revision bound.
    /// </summary>
    public SessionTransformResult TransformPlacedItem(GameSessionVersion expected, string item, int prototype)
    {
        ThrowIfDisposed();
        SessionTransformResult Reject(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, null, new(_identity, _revision));
        if (!SessionValidation.Entity(item)) return Reject(GameSessionFailure.InvalidItem);
        if (_inventory.PlacedItem(item) is not { } placed) return Reject(GameSessionFailure.ItemNotAvailable);
        if (CreateObjectRefusal(SessionCreatedKind.Critter, prototype, placed.Map, placed.Cell, 0, 0) is { } refused) return Reject(refused);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        var made = CreateCreatedObject(SessionCreatedKind.Critter, prototype, placed.Map, placed.Cell, 0, 0, _clock.QueryClockSnapshot().GameTimestampMs);
        DestroyObject(item);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, made, item, new(_identity, _revision));
    }
}

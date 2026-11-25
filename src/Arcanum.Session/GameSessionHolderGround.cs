using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    private Func<string, CellCoord?>? _holderCells;

    /// <summary>
    /// ARC-SCRIPT-2911 (ledger NT1, NT2): attaches the host's answer to where a holder stands on the actor's map: for an NPC's or a container's entity its world cell, or null when
    /// the host does not know it. A new gold pile or ammunition stack of a move into an NPC or a container is made by the original on the holder's tile and moved in; when the move's get script (point 4) skips the default or the insert check refuses,
    /// the insert returns 0 and the object stays there (the call still reports success). With a cell the session leaves the pile or stack on the ground at it (<see cref="QueryPlacedItems"/>, source made);
    /// without one (no source attached, or the host does not know the holder) the move is refused as before (<see cref="GameSessionFailure.InsertRefused"/>, nothing created). The source must not change the session. Not state: it is never saved and is attached again after a restore; null detaches it.
    /// </summary>
    // The original's gold move, 0x464830, makes the pile on the receiver's tile (0x464970) and moves it in through 0x4617F0 → 0x461810; the ammunition move is 0x4658E0 (0x465A40).
    public void AttachHolderCells(Func<string, CellCoord?>? cells)
    {
        ThrowIfDisposed();
        _holderCells = cells;
    }

    // The cell a refused new pile or stack of a move into the holder lies on, or null when it cannot lie there: no source, no hero (the placed items are the hero's store), a holder the host does not place, a tile the ground cannot hold or the hero's
    // item budget (a ground made item counts against it, as a drop's does, PlanGoldGround).
    private CellCoord? GroundTarget(string holder)
    {
        if (_holderCells is null || _hero is null || _inventory.Held + 1 > _limits.MaxPcItems) return null;
        var cell = _holderCells(holder);
        ThrowIfDisposed();
        return cell is { } found && DropCellFits(found) ? found : null;
    }
}

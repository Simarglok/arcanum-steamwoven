using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-WORLD-081: how many more items a script may create next to an object on <paramref name="cell"/> of <paramref name="map"/>
    /// (<see cref="CreatedItemRules.Room"/> over the created items lying there), or null under the original's rule
    /// (<c>ScrRuleProfile.CreatedItemsUnbounded</c>), where only the session's budget bounds them.
    /// </summary>
    public int? QueryCreatedItemRoom(SessionMapId map, CellCoord cell)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Map(map)) throw new ArgumentException("Invalid map.", nameof(map));
        return CreatedRoom(map, cell);
    }

    private int? CreatedRoom(SessionMapId map, CellCoord near) => CreatedItemRules.Room(near,
        _inventory.Placed.Where(item => item.Source == SessionPlacedSource.Created && item.Map == map).Select(item => item.Cell),
        _rules.Scripts.CreatedItemsUnbounded);

    /// <summary>
    /// ARC-WORLD-081: a script's create object (action 52, ledger PG1) adds an item of <paramref name="kind"/> to the ground of
    /// <paramref name="map"/> next to the object on <paramref name="near"/>: the tile is the create action's (up to ten tries of a distance
    /// 1..3 and a direction over the host's <paramref name="dropMap"/>, <see cref="DropPlacementRules.PlaceCreated"/>, drawn from the
    /// session's generator), the item a new <c>made:n</c> entity numbered with the made items, a placed item of source
    /// <see cref="SessionPlacedSource.Created"/> with its prototype's weight facts and complexity (<see cref="AttachItemWeights"/>). It
    /// schedules no decay (the creation never reaches the decay's scheduler, ledger ID9), and it needs no hero. Under Steamwoven it creates
    /// nothing and draws nothing when <see cref="QueryCreatedItemRoom"/> is 0 (<see cref="GameSessionDisposition.NoChange"/>, no item).
    /// One revision. Refused before any effect: an invalid input, ammunition, gold or a key (<see cref="GameSessionFailure.ItemTypeUnsupported"/>:
    /// no unit count, no gold pile, no key ring), the version, the created items' budget (<see cref="GameSessionLimits.MaxCreatedItems"/>),
    /// a generator at its end (<see cref="GameSessionFailure.RandomExhausted"/>) and a tile outside the ground store's range.
    /// </summary>
    public InventoryMutationResult CreateGroundItem(GameSessionVersion expected, SessionItemKind kind, SessionMapId map, CellCoord near, IDropMap dropMap)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(dropMap);
        if (!Enum.IsDefined(kind.Type) || !SessionValidation.Map(map) || !DropCellFits(near)) return RejectItem(GameSessionFailure.InvalidItem);
        if (!InventoryDomain.KitAdmitted(kind.Type)) return RejectItem(GameSessionFailure.ItemTypeUnsupported);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (CreatedRoom(map, near) == 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, Version);
        if (_inventory.Created >= _limits.MaxCreatedItems) return RejectItem(GameSessionFailure.BudgetExceeded);
        var number = NextMadeNumber();
        if (number > int.MaxValue) return RejectItem(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        var revision = _revision;
        CellCoord cell;
        try { cell = DropPlacementRules.PlaceCreated(near, dropMap, new SessionDropRolls(_random)); }
        catch (SessionDropRolls.ExhaustedException) { return RejectItem(GameSessionFailure.RandomExhausted); }
        ThrowIfDisposed();
        if (_revision != revision) return RejectItem(GameSessionFailure.StaleVersion);
        if (!DropCellFits(cell)) return RejectItem(GameSessionFailure.InvalidItem);
        var entity = SessionMadeItem.EntityOf((int)number);
        // ARC-WORLD-085: the new object has its prototype's weight; ARC-WORLD-083: and its complexity (field 0x60, MC1).
        var weight = CreatedWeight(kind.Prototype);
        _inventory.Placed.Add(new(entity, kind, SessionPlacedSource.Created, null, map, cell, weight.Complexity));
        _inventory.SetWeight(entity, weight);
        _inventory.Taken.Add(entity);
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, entity, null, Version);
    }
}

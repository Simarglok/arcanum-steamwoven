using Arcanum.Simulation;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>
/// ARC-WORLD-2493 (ledger DD1–DD8): the original's inventory-on-destroy (0x4639E0, the one caller object destroy 0x43CCA0 with mode 1) for a map NPC the script destroy removes.
/// </summary>
public sealed partial class GameSessionOwner
{
    // The walk of 0x4639E0 over the NPC's list (the added items, then the seed's, so reversed it runs last item first), the items with object flag 0x400000 only, and the budget refusal of laying them down.
    private (List<BodyItem> Walk, GameSessionFailure Refusal) PlanNpcDestroyDrop(SessionMapId map, string entity, CellCoord cell)
    {
        var walk = new List<BodyItem>();
        if (_inventory.Seeds.TryGetValue(map, out var seed) && seed.Holders.TryGetValue(entity, out var holder) && holder.Kind == SessionHolderKind.Npc)
        {
            foreach (var item in _inventory.Current(seed, holder, row => DroppedCell(map, seed, row), added: false))
                if (seed.Items[item.Entity].Facts is { Invulnerable: true }) walk.Add(new(item.Entity, null, true, false));
            foreach (var made in _inventory.AddedTo(entity))
                if (AddedItemFacts(made) is { Invulnerable: true } && AddedBodyItem(made, map, cell) is { Destroy: false } placed) walk.Add(placed);
        }
        walk.Reverse();
        var droppedCreated = walk.Count(item => item.Placed is { Source: SessionPlacedSource.Created });
        var droppedHeld = walk.Count(item => item.Placed is { } placed && placed.Source != SessionPlacedSource.Created);
        var refusal = walk.Count > 0 && !DropCellFits(cell) ? GameSessionFailure.DropTileUnfit
            : (long)_inventory.Created + droppedCreated > _limits.MaxCreatedItems || (long)_inventory.Held + droppedHeld > _limits.MaxPcItems ? GameSessionFailure.BudgetExceeded
            : GameSessionFailure.None;
        return (walk, refusal);
    }

    /// <summary>ARC-WORLD-2493: why <see cref="DestroyMapNpc"/> would refuse the NPC (<see cref="GameSessionFailure.DropTileUnfit"/>, <see cref="GameSessionFailure.BudgetExceeded"/>), or null when it would destroy it (also for a destroyed NPC, which is left as it is); changes nothing. A script run checks it before it applies any effect.</summary>
    public GameSessionFailure? DestroyMapNpcRefusal(SessionMapId map, string entity, CellCoord cell)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) return GameSessionFailure.InvalidObject;
        if (_revision >= long.MaxValue - 2) return GameSessionFailure.RevisionExhausted;
        if (_maps.Get(map, MapChangeKey.ForEntity(entity, ObjectDestroyedProperty)) == 1) return null;
        return PlanNpcDestroyDrop(map, entity, cell).Refusal is var refusal && refusal != GameSessionFailure.None ? refusal : null;
    }

    /// <summary>
    /// ARC-WORLD-2493 (0x43CCA0 → 0x4639E0 with mode 1, DD1–DD8): the script destroy of a map NPC, alive or dead (the destroy does not test death). The NPC is destroyed (the map change <see cref="ObjectDestroyedProperty"/>) and
    /// every item of its list whose object flag 0x400000 is set (the data's, <see cref="SessionItemFacts.Invulnerable"/>; the script toggle writes none on an item) is taken out and dropped on the NPC's cell by the drop rule
    /// (<see cref="SettleDrop"/>: a junk pile with room, else loose) with its decay event two game days on (0x466E50, 0x468090), the list walked from its last item to its first. No other item is touched: an item without the
    /// flag stays in the destroyed NPC's list, destroyed neither flagged (DD6), and a worn invulnerable item is unequipped as every take-out does (0x467860). The take-out runs no remove check (a no-drop item goes too)
    /// and no drop script. An NPC with no inventory record, or no invulnerable item, only gets the destroy. The budgets come first (<see cref="DestroyMapNpcRefusal"/>), so the destroy does all of it or is refused whole.
    /// A destroyed NPC is left as it is (TD2: <see cref="GameSessionDisposition.NoChange"/>). One revision for the destroy and one for the drops. Limits: the item's own remove-item script (point 28) is not run, a made item's
    /// flags read as clear (its prototype is the host's), a given kit item's too, an added item whose ground record only a hero's drop makes stays out of the walk in a session with no hero, and an added item the body walk
    /// would destroy (no-display or summoned flag of its origin seed) is not dropped either; the NPC's leave of its leader (with <paramref name="leaveLeaders"/> the session unlinks it, ARC-SCRIPT-2506), its destroy script and the host's runs of it are the caller's (the script host refuses such a destroy).
    /// </summary>
    [OriginalFunction(0x4639E0, OriginalFunctionScope.Partial)]
    public GameSessionMutationResult DestroyMapNpc(GameSessionVersion expected, SessionMapId map, string entity, CellCoord cell, bool leaveLeaders = false)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) return new(GameSessionDisposition.Rejected, GameSessionFailure.InvalidObject, Version);
        if (!IsCurrent(expected)) return new(GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion, Version);
        if (_revision >= long.MaxValue - 2) return new(GameSessionDisposition.Rejected, GameSessionFailure.RevisionExhausted, Version);
        if (_maps.Get(map, MapChangeKey.ForEntity(entity, ObjectDestroyedProperty)) == 1) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);

        var (walk, refusal) = PlanNpcDestroyDrop(map, entity, cell);
        if (refusal != GameSessionFailure.None) return new(GameSessionDisposition.Rejected, refusal, Version);

        // The seed's items lie where they dropped (the item.dropped map change), written with the destroy: one change, all refusals before the commit.
        var edits = new List<MapChangeEdit> { new(map, MapChangeKey.ForEntity(entity, ObjectDestroyedProperty), 1) };
        foreach (var item in walk.Where(item => item.Seed)) edits.Add(new(map, MapChangeKey.ForEntity(item.Entity, ItemDroppedProperty), DroppedValue(cell)));
        var written = ApplyChanges(Version, null, edits);
        if (written.Disposition != GameSessionDisposition.Changed) return written;
        // ARC-SCRIPT-2506 (0x45E180, 0x45E040): a destroy that goes through the object's own destroy (a transform's) takes the NPC out of its NPC leader's array and frees the followers it led.
        var unlinked = leaveLeaders && (_party.NpcLed.ContainsKey(entity) || _party.NpcArrays.ContainsKey(entity));
        if (unlinked) DisbandNpcLinks(entity);
        if (walk.Count == 0)
        {
            if (!unlinked) return written;
            Revise();
            return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
        }

        // Each item leaves its holder in the walk's order; each placement sees the items still to come as not yet dropped (as a container's spill does), and a drop schedules the item's decay (0x468090).
        var later = walk.Select(item => item.Entity).ToHashSet(StringComparer.Ordinal);
        foreach (var (item, placed, _, _) in walk)
        {
            later.Remove(item);
            if (placed is not null)
            {
                _inventory.RemoveFromNpc(item);
                _inventory.Hero.TakeGiven(item);
                _inventory.Hero.AddPlaced(placed);
            }
            else _inventory.Unwear(item);
            SettleDrop(item, map, cell, later);
            ScheduleDecay(item, map);
        }
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }
}

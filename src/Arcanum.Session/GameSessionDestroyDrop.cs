using Arcanum.Simulation;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>
/// ARC-WORLD-2493 (ledger DD1–DD8): the original's inventory-on-destroy (0x4639E0, the one caller object destroy 0x43CCA0 with mode 1) for a map NPC the script destroy removes.
/// </summary>
public sealed partial class GameSessionOwner
{
    // The walk of 0x4639E0 over the holder's list (an NPC's or a container's: the added items, then the seed's, so reversed it runs last item first), the items with object flag 0x400000 only, and the budget refusal of laying them down.
    // ARC-WORLD-2502: <paramref name="survivors"/> are the items a regeneration of the container keeps (0x463B30, RK5): the walk runs after it, over those only.
    private (List<BodyItem> Walk, GameSessionFailure Refusal) PlanHolderDestroyDrop(SessionMapId map, string entity, CellCoord cell, SessionHolderKind kind, IReadOnlySet<string>? survivors = null)
    {
        var walk = new List<BodyItem>();
        if (_inventory.Seeds.TryGetValue(map, out var seed) && seed.Holders.TryGetValue(entity, out var holder) && holder.Kind == kind)
        {
            foreach (var item in _inventory.Current(seed, holder, row => DroppedCell(map, seed, row), added: false))
                if (survivors?.Contains(item.Entity) != false && seed.Items[item.Entity].Facts is { Invulnerable: true }) walk.Add(new(item.Entity, null, true, false));
            foreach (var made in _inventory.AddedTo(entity))
                if (survivors?.Contains(made.Entity) != false && AddedItemFacts(made) is { Invulnerable: true } && AddedBodyItem(made, map, cell) is { Destroy: false } placed) walk.Add(placed);
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
        return PlanHolderDestroyDrop(map, entity, cell, SessionHolderKind.Npc).Refusal is var refusal && refusal != GameSessionFailure.None ? refusal : null;
    }

    /// <summary>
    /// ARC-WORLD-2493 (0x43CCA0 → 0x4639E0 with mode 1, DD1–DD8): the script destroy of a map NPC, alive or dead (the destroy does not test death). The NPC is destroyed (the map change <see cref="ObjectDestroyedProperty"/>) and
    /// every item of its list whose object flag 0x400000 is set (the data's, <see cref="SessionItemFacts.Invulnerable"/>; the script toggle writes none on an item) is taken out and dropped on the NPC's cell by the drop rule
    /// (<see cref="SettleDrop"/>: a junk pile with room, else loose) with its decay event two game days on (0x466E50, 0x468090), the list walked from its last item to its first. No other item is touched: an item without the
    /// flag stays in the destroyed NPC's list, destroyed neither flagged (DD6), and a worn invulnerable item is unequipped as every take-out does (0x467860). The take-out runs no remove check (a no-drop item goes too)
    /// and no drop script. An NPC with no inventory record, or no invulnerable item, only gets the destroy. The budgets come first (<see cref="DestroyMapNpcRefusal"/>), so the destroy does all of it or is refused whole.
    /// A destroyed NPC is left as it is (TD2: <see cref="GameSessionDisposition.NoChange"/>). One revision for the destroy and one for the drops. Limits: the item's own remove-item script (point 28) is not run, a made item's
    /// flags read as clear (its prototype is the host's), a given kit item's too, an added item whose ground record only a hero's drop makes stays out of the walk in a session with no hero, and an added item the body walk
    /// would destroy (no-display or summoned flag of its origin seed) is not dropped either; the NPC's leave of its leader, its destroy script and the host's runs of it are the caller's (the script host refuses such a destroy).
    /// </summary>
    [OriginalFunction(0x4639E0, OriginalFunctionScope.Partial)]
    public GameSessionMutationResult DestroyMapNpc(GameSessionVersion expected, SessionMapId map, string entity, CellCoord cell)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) return new(GameSessionDisposition.Rejected, GameSessionFailure.InvalidObject, Version);
        if (!IsCurrent(expected)) return new(GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion, Version);
        if (_revision >= long.MaxValue - 2) return new(GameSessionDisposition.Rejected, GameSessionFailure.RevisionExhausted, Version);
        if (_maps.Get(map, MapChangeKey.ForEntity(entity, ObjectDestroyedProperty)) == 1) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);

        var (walk, refusal) = PlanHolderDestroyDrop(map, entity, cell, SessionHolderKind.Npc);
        if (refusal != GameSessionFailure.None) return new(GameSessionDisposition.Rejected, refusal, Version);

        // The seed's items lie where they dropped (the item.dropped map change), written with the destroy: one change, all refusals before the commit.
        var edits = new List<MapChangeEdit> { new(map, MapChangeKey.ForEntity(entity, ObjectDestroyedProperty), 1) };
        foreach (var item in walk.Where(item => item.Seed)) edits.Add(new(map, MapChangeKey.ForEntity(item.Entity, ItemDroppedProperty), DroppedValue(cell)));
        var written = ApplyChanges(Version, null, edits);
        if (written.Disposition != GameSessionDisposition.Changed || walk.Count == 0) return written;

        DropWalk(walk, map, cell, worn: true);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }

    // Each item leaves its holder in the walk's order; each placement sees the items still to come as not yet dropped (as a container's spill does), and a drop schedules the item's decay (0x468090). A worn item (an NPC's) is unequipped first.
    private void DropWalk(List<BodyItem> walk, SessionMapId map, CellCoord cell, bool worn)
    {
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
            else if (worn) _inventory.Unwear(item);
            SettleDrop(item, map, cell, later);
            ScheduleDecay(item, map);
        }
    }

    /// <summary>
    /// ARC-WORLD-2502: why <see cref="DestroyMapContainer"/> would refuse the container (<see cref="GameSessionFailure.DropTileUnfit"/>, <see cref="GameSessionFailure.BudgetExceeded"/>, <see cref="GameSessionFailure.RevisionExhausted"/>), or null when it would destroy it
    /// (also for a destroyed container, which is left as it is). A junk pile has no walk, so only the revision can refuse it.
    /// </summary>
    public GameSessionFailure? DestroyMapContainerRefusal(SessionMapId map, string entity, CellCoord cell, bool junkPile)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) return GameSessionFailure.InvalidObject;
        if (_revision >= long.MaxValue - 3) return GameSessionFailure.RevisionExhausted;
        if (ContainerDestroyed(map, entity) || junkPile) return null;
        return PlanContainerDestroy(map, entity, cell).Refusal is var refusal && refusal != GameSessionFailure.None ? refusal : null;
    }

    // The container's destroy as planned before anything changes: the regeneration a never generated 0x200 container runs first (null when none) and the walk after it.
    private (BreakGeneration? Generation, List<BodyItem> Walk, GameSessionFailure Refusal) PlanContainerDestroy(SessionMapId map, string entity, CellCoord cell)
    {
        var generation = PlanBreakGeneration(map, entity);
        var survivors = generation?.Plan.Kept.Select(item => item.Entity).ToHashSet(StringComparer.Ordinal);
        var (walk, refusal) = PlanHolderDestroyDrop(map, entity, cell, SessionHolderKind.Container, survivors);
        return (generation, walk, refusal);
    }

    private bool ContainerDestroyed(SessionMapId map, string entity) =>
        RecordedObject(_maps, map, entity) is { Condition: SessionObjectCondition.Destroyed };

    /// <summary>
    /// ARC-WORLD-2502 (ledger DD1–DD9): the script destroy of a container of the shown map; the container path of <see cref="DestroyMapNpc"/>'s rule. A junk pile (<paramref name="junkPile"/>: prototype 3043, description 3023) is only destroyed (DD2). Any other container
    /// with flag 0x200 that was never generated is regenerated first (the break's own planning, <c>GenerateOnceWorld</c>), as the break of such a container does (ARC-WORLD-1301; a revision of its own: the clear destroys every item but the
    /// persistent ones, RK5, and the walk is planned over those, so a refusal comes before the generation); then the container is destroyed (<see cref="SessionObjectCondition.Destroyed"/> with no spill cell: the items it still holds stay in its list, DD6) and every item of its list with object flag 0x400000 (the data's,
    /// <see cref="SessionItemFacts.Invulnerable"/>) is taken out and dropped on the container's cell by the drop rule with its decay event, the list walked from its last item to its first, an item the hero sold into the container as the ground record it
    /// becomes. A container already destroyed (a break, an earlier destroy) is left as it is (<see cref="GameSessionDisposition.NoChange"/>). Limits as <see cref="DestroyMapNpc"/>; the goods a regeneration makes are made items, whose flags read as
    /// clear, so none of them is dropped. The host's destroy point (2) and its runs of the container are the caller's.
    /// </summary>
    public GameSessionMutationResult DestroyMapContainer(GameSessionVersion expected, SessionMapId map, string entity, CellCoord cell, bool junkPile)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) return new(GameSessionDisposition.Rejected, GameSessionFailure.InvalidObject, Version);
        if (!IsCurrent(expected)) return new(GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion, Version);
        if (_revision >= long.MaxValue - 3) return new(GameSessionDisposition.Rejected, GameSessionFailure.RevisionExhausted, Version);
        if (ContainerDestroyed(map, entity)) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);

        var destroyed = new MapChangeEdit(map, MapChangeKey.ForEntity(entity, ObjectConditionProperty), (long)SessionObjectCondition.Destroyed);
        if (junkPile) return ApplyChanges(Version, null, [destroyed]);

        // The walk is planned over what the regeneration keeps, so a refused destroy has changed nothing.
        var (generation, walk, refusal) = PlanContainerDestroy(map, entity, cell);
        if (refusal != GameSessionFailure.None) return new(GameSessionDisposition.Rejected, refusal, Version);

        // 0x4639E0 regenerates a container with flag 0x200 first (0x463E20): the generation is one revision of its own, as the break's is.
        if (generation is not null)
        {
            Regenerate(generation.World, map, generation.Plan);
            Revise();
        }

        // The seed's items lie where they dropped (the item.dropped map change), written with the destroy: one change, all refusals before the commit.
        var edits = new List<MapChangeEdit> { destroyed };
        foreach (var item in walk.Where(item => item.Seed)) edits.Add(new(map, MapChangeKey.ForEntity(item.Entity, ItemDroppedProperty), DroppedValue(cell)));
        var written = ApplyChanges(Version, null, edits);
        if (written.Disposition != GameSessionDisposition.Changed || walk.Count == 0) return written;
        DropWalk(walk, map, cell, worn: false);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }
}

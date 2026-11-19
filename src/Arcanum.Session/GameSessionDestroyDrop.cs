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

    // ARC-SCRIPT-2880 (IE3; 0x467860 through 0x466E00): every item the walk takes out of the dying holder's list goes through the removal, which runs the item's remove-item script (point 28) with the dying object as the triggerer. The runs are made and prepared before anything
    // applies (a run the session cannot apply refuses the destroy whole); without a script host an item with the point refuses it, never dropped with the script skipped. The script points are the seed row's and the slots the session kept or the made item inherits.
    private RemovalScripted RunDestroyRemovals(IReadOnlyList<BodyItem> walk, SessionMapId map, string holder, ISessionItemScriptHost? scripts)
    {
        var added = walk.Any(item => !item.Seed) ? _inventory.AddedTo(holder).ToDictionary(made => made.Entity, StringComparer.Ordinal) : [];
        var items = walk.Select(item => (item.Entity, Points: item.Seed
            ? KnownScriptPoints(item.Entity, _inventory.Seeds.TryGetValue(map, out var seed) && seed.Items.TryGetValue(item.Entity, out var row) ? row.Facts : null)
            : KnownScriptPoints(item.Entity, added.TryGetValue(item.Entity, out var made) ? AddedItemFacts(made) : null))).ToArray();
        if (scripts is not null) return RunRemovalScripts(items.Select(item => (item.Entity, item.Points)).ToArray(), holder, scripts);
        return items.Any(item => item.Points.Contains(SessionScriptPoint.RemoveItem)) ? new(RejectItem(GameSessionFailure.ItemScriptUnsupported), null) : new(null, null);
    }

    // The destroy's own changes and the runs' planned ones as the commit writes them together: false when they would not fit the budget (checked before the runs commit, so a refusal leaves everything as it was).
    private bool RemovalChangesFit(RemovalScripted removal, IReadOnlyList<MapChangeEdit> edits) =>
        removal.Runs is not { } runs || ChangesFit(EffectiveChanges([.. edits, .. runs.PlannedArtEdits]));

    /// <summary>ARC-WORLD-2493: why <see cref="DestroyMapNpc"/> would refuse the NPC (<see cref="GameSessionFailure.DropTileUnfit"/>, <see cref="GameSessionFailure.BudgetExceeded"/>), or null when it would destroy it (also for a destroyed NPC, which is left as it is); changes nothing. A script run checks it before it applies any effect; with <paramref name="scripts"/> also the dry run of the remove-item scripts of the items taken out (ARC-SCRIPT-2880), without it an item with one refuses (<see cref="GameSessionFailure.ItemScriptUnsupported"/>).</summary>
    public GameSessionFailure? DestroyMapNpcRefusal(SessionMapId map, string entity, CellCoord cell, ISessionItemScriptHost? scripts = null)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) return GameSessionFailure.InvalidObject;
        if (_revision >= long.MaxValue - 2) return GameSessionFailure.RevisionExhausted;
        if (_maps.Get(map, MapChangeKey.ForEntity(entity, ObjectDestroyedProperty)) == 1) return null;
        var (walk, refusal) = PlanHolderDestroyDrop(map, entity, cell, SessionHolderKind.Npc);
        if (refusal != GameSessionFailure.None) return refusal;
        // ARC-SCRIPT-2880: the dry run of the taken-out items' remove-item scripts finds a run the session cannot apply before the calling script applies anything.
        return RunDestroyRemovals(walk, map, entity, scripts).Done is { } dry ? dry.Failure : null;
    }

    /// <summary>
    /// ARC-WORLD-2493 (0x43CCA0 → 0x4639E0 with mode 1, DD1–DD8): the script destroy of a map NPC, alive or dead (the destroy does not test death). The NPC is destroyed (the map change <see cref="ObjectDestroyedProperty"/>) and
    /// every item of its list whose object flag 0x400000 is set (the data's, <see cref="SessionItemFacts.Invulnerable"/>; the script toggle writes none on an item) is taken out and dropped on the NPC's cell by the drop rule
    /// (<see cref="SettleDrop"/>: a junk pile with room, else loose) with its decay event two game days on (0x466E50, 0x468090), the list walked from its last item to its first. No other item is touched: an item without the
    /// flag stays in the destroyed NPC's list, destroyed neither flagged (DD6), and a worn invulnerable item is unequipped as every take-out does (0x467860). The take-out runs no remove check (a no-drop item goes too)
    /// and no drop script. An NPC with no inventory record, or no invulnerable item, only gets the destroy. The budgets come first (<see cref="DestroyMapNpcRefusal"/>), so the destroy does all of it or is refused whole.
    /// A destroyed NPC is left as it is (TD2: <see cref="GameSessionDisposition.NoChange"/>). One revision for the destroy and one for the drops. ARC-SCRIPT-2880 (IE3): each item taken out goes through the removal (0x467860), so its remove-item script (point 28) runs with the NPC as the triggerer and the item as the attachee, the result ignored, the runs made before anything applies and committed with the destroy (the lines they print are the result's <see cref="GameSessionMutationResult.Messages"/>); without a script host an item with the point refuses the destroy (<see cref="GameSessionFailure.ItemScriptUnsupported"/>), a run the session cannot apply refuses it too. Limits: a made item's
    /// flags read as clear (its prototype is the host's), a given kit item's too, an added item whose ground record only a hero's drop makes stays out of the walk in a session with no hero, and an added item the body walk
    /// would destroy (no-display or summoned flag of its origin seed) is not dropped either; the NPC's leave of its leader (with <paramref name="leaveLeaders"/> the session unlinks it, ARC-SCRIPT-2506), its destroy script and the host's runs of it are the caller's (the script host refuses such a destroy).
    /// </summary>
    [OriginalFunction(0x4639E0, OriginalFunctionScope.Partial)]
    public GameSessionMutationResult DestroyMapNpc(GameSessionVersion expected, SessionMapId map, string entity, CellCoord cell, bool leaveLeaders = false, ISessionItemScriptHost? scripts = null)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) return new(GameSessionDisposition.Rejected, GameSessionFailure.InvalidObject, Version);
        if (!IsCurrent(expected)) return new(GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion, Version);
        if (_revision >= long.MaxValue - 2) return new(GameSessionDisposition.Rejected, GameSessionFailure.RevisionExhausted, Version);
        if (_maps.Get(map, MapChangeKey.ForEntity(entity, ObjectDestroyedProperty)) == 1) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);

        var (walk, refusal) = PlanHolderDestroyDrop(map, entity, cell, SessionHolderKind.Npc);
        if (refusal != GameSessionFailure.None) return new(GameSessionDisposition.Rejected, refusal, Version);
        var removal = RunDestroyRemovals(walk, map, entity, scripts);
        if (removal.Done is { } refused) return new(GameSessionDisposition.Rejected, refused.Failure, Version);

        // The seed's items lie where they dropped (the item.dropped map change), written with the destroy: one change, all refusals before the commit.
        var edits = new List<MapChangeEdit> { new(map, MapChangeKey.ForEntity(entity, ObjectDestroyedProperty), 1) };
        foreach (var item in walk.Where(item => item.Seed)) edits.Add(new(map, MapChangeKey.ForEntity(item.Entity, ItemDroppedProperty), DroppedValue(cell)));
        if (!RemovalChangesFit(removal, edits)) return new(GameSessionDisposition.Rejected, GameSessionFailure.BudgetExceeded, Version);
        removal.Runs?.Commit();
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

        DropWalk(walk, map, cell, worn: true);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version) { Messages = removal.Runs?.Messages ?? [] };
    }

    /// <summary>
    /// ARC-WORLD-2503 (the inventory-on-destroy, DD1–DD8): the destroy of created critters (a summon's expiry or dispel, a script's undo) as planned before anything changes: for each critter of <paramref name="critters"/> the walk of
    /// <see cref="PlanHolderDestroyDrop"/> over its list on its map at <paramref name="cell"/> (the live cell the caller knows, used when there is one critter; else the cell it was created on), and the first refusal of the drops
    /// (<see cref="GameSessionFailure.DropTileUnfit"/>, <see cref="GameSessionFailure.BudgetExceeded"/>), the budgets counted over all the critters' drops together.
    /// </summary>
    private (List<(SessionMapId Map, CellCoord Cell, List<BodyItem> Walk)> Drops, GameSessionFailure Refusal) PlanCreatedCritterDrops(IReadOnlyList<SessionCreatedObject> critters, CellCoord? cell)
    {
        var drops = new List<(SessionMapId, CellCoord, List<BodyItem>)>();
        foreach (var critter in critters)
        {
            if (critter.Kind != SessionCreatedKind.Critter) continue;
            var at = critters.Count == 1 && cell is { } live ? live : critter.Cell;
            var (walk, refusal) = PlanHolderDestroyDrop(critter.Map, critter.Entity, at, SessionHolderKind.Npc);
            if (refusal != GameSessionFailure.None) return (drops, refusal);
            if (walk.Count > 0) drops.Add((critter.Map, at, walk));
        }
        var created = drops.Sum(drop => drop.Item3.Count(item => item.Placed is { Source: SessionPlacedSource.Created }));
        var held = drops.Sum(drop => drop.Item3.Count(item => item.Placed is { } placed && placed.Source != SessionPlacedSource.Created));
        return (drops, (long)_inventory.Created + created > _limits.MaxCreatedItems || (long)_inventory.Held + held > _limits.MaxPcItems ? GameSessionFailure.BudgetExceeded : GameSessionFailure.None);
    }

    // The planned drops laid down, each critter's walk in its order (the critter's destroy follows and discards what is left of its list).
    private void DropCreatedCritterItems((List<(SessionMapId Map, CellCoord Cell, List<BodyItem> Walk)> Drops, GameSessionFailure Refusal) plan)
    {
        foreach (var (map, cell, walk) in plan.Drops) DropWalk(walk, map, cell, worn: true);
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
    /// (also for a destroyed container, which is left as it is). A junk pile has no walk, so only the revision can refuse it. ARC-SCRIPT-2880: <paramref name="scripts"/> as for <see cref="DestroyMapNpcRefusal"/>.
    /// </summary>
    public GameSessionFailure? DestroyMapContainerRefusal(SessionMapId map, string entity, CellCoord cell, bool junkPile, ISessionItemScriptHost? scripts = null)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) return GameSessionFailure.InvalidObject;
        if (_revision >= long.MaxValue - 3) return GameSessionFailure.RevisionExhausted;
        if (ContainerDestroyed(map, entity) || junkPile) return null;
        var (_, walk, refusal) = PlanContainerDestroy(map, entity, cell);
        if (refusal != GameSessionFailure.None) return refusal;
        // ARC-SCRIPT-2880: the dry run of the taken-out items' remove-item scripts, as for an NPC.
        return RunDestroyRemovals(walk, map, entity, scripts).Done is { } dry ? dry.Failure : null;
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
    /// clear, so none of them is dropped. The host's destroy point (2) and its runs of the container are the caller's. ARC-SCRIPT-2880 (IE3): each item taken out runs its remove-item script (28) with the container as the triggerer, as for <see cref="DestroyMapNpc"/> (the walk is planned over what the regeneration keeps; the runs are made before it and committed with the destroy).
    /// </summary>
    public GameSessionMutationResult DestroyMapContainer(GameSessionVersion expected, SessionMapId map, string entity, CellCoord cell, bool junkPile, ISessionItemScriptHost? scripts = null)
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
        var removal = RunDestroyRemovals(walk, map, entity, scripts);
        if (removal.Done is { } refused) return new(GameSessionDisposition.Rejected, refused.Failure, Version);

        // The seed's items lie where they dropped (the item.dropped map change), written with the destroy: one change, all refusals before the commit.
        var edits = new List<MapChangeEdit> { destroyed };
        foreach (var item in walk.Where(item => item.Seed)) edits.Add(new(map, MapChangeKey.ForEntity(item.Entity, ItemDroppedProperty), DroppedValue(cell)));
        if (!RemovalChangesFit(removal, edits)) return new(GameSessionDisposition.Rejected, GameSessionFailure.BudgetExceeded, Version);

        // 0x4639E0 regenerates a container with flag 0x200 first (0x463E20): the generation is one revision of its own, as the break's is.
        if (generation is not null)
        {
            Regenerate(generation.World, map, generation.Plan);
            Revise();
        }

        removal.Runs?.Commit();
        var written = ApplyChanges(Version, null, edits);
        if (written.Disposition != GameSessionDisposition.Changed || walk.Count == 0) return written;
        DropWalk(walk, map, cell, worn: false);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version) { Messages = removal.Runs?.Messages ?? [] };
    }
}

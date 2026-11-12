using Arcanum.Simulation.Items;

namespace Arcanum.Session;

/// <summary>
/// ARC-WORLD-1301 (ledger VP19, VP23, VPG6): the break of a container with flag 0x200 that was never generated. The break's drop of the whole
/// inventory (0x463730, RK13, OD9) first runs the regeneration (0x463E20, RK10) on such a container, so the goods it would have held at its first
/// open lie on the ground; the session's spill used to lay only the stored items.
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-WORLD-1301: the host's world for the break's generation of a container with flag 0x200 (the same <see cref="ISessionRestockWorld"/> the
    /// first loot open passes to <see cref="GenerateContainerOnce"/>), asked when a container breaks outside an event pass (inside one, the pass's
    /// world is used). It may answer null. None set, a null answer or a host that is not ready (<see cref="ISessionRestockWorld.RestockReady"/>): the
    /// break spills only the stored items, as before (a recorded limit); so does a break whose seed facts the host cannot give
    /// (<see cref="ISessionRestockWorld.GeneratedOnceFacts"/>), a set the session does not admit, a transfer script on the container and a full
    /// session budget. The host must not call back into the session from its callbacks.
    /// </summary>
    public Func<ISessionRestockWorld?>? GenerateOnceWorld { get; set; }

    private sealed record BreakGeneration(ISessionRestockWorld World, RestockPlan Plan);

    // The generation a breaking container with flag 0x200 will run, planned before the break is written (the plan changes nothing): null for
    // a container that needs none or whose generation the session cannot run, which then spills as before.
    private BreakGeneration? PlanBreakGeneration(SessionObjectTarget target)
    {
        // The pass's world when it can answer, else the host's own.
        var world = PassWorldAs<ISessionRestockWorld>() is { RestockReady: true } passed ? passed : GenerateOnceWorld?.Invoke();
        if (world is not { RestockReady: true } || !_inventory.Seeds.TryGetValue(target.Map, out var seed) ||
            !seed.Holders.TryGetValue(target.Entity, out var row) || row.Kind != SessionHolderKind.Container ||
            world.RestockFacts(target.Map, target.Entity) is not { Kind: RestockHolderKind.Container } found) return null;
        var facts = GeneratedFacts(target.Map, target.Entity, found);
        if ((facts.ContainerFlags & RestockRules.GeneratedOnceFlag) == 0) return null;
        var plan = PlanRestock(world, target.Map, seed, target.Entity, facts);
        return plan.Outcome == SessionRestockOutcome.Unsupported || !RestockFits([plan]) ? null : new(world, plan);
    }

    // The break's walk (0x463730, mode 1) after the regeneration: the container's list is the persistent stored items the clear kept and the
    // goods the lines made, and it drops last first, each through the placement with its decay (ARC-WORLD-063). A made item the ground admits
    // lies there as a placed item (as a dead body's made items do); gold, ammunition, a pile or an item the hero gave is destroyed instead (a
    // recorded limit, the ground's own), and beyond the bound of created ground items the item is destroyed too.
    private void SpillGeneratedGoods(SessionObjectTarget target, InventoryDomain.Seed seed, SessionSeedHolder holder, BreakGeneration generation)
    {
        // The generation runs after the break is written, so the container already counts as spilled: an ammunition line then makes its own stack
        // instead of merging into one (the spill destroys every ammunition stack anyway).
        Regenerate(generation.World, target.Map, generation.Plan);
        var list = _inventory.Current(seed, holder, entity => DroppedCell(target.Map, seed, entity), added: false)
            .Select(item => (item.Entity, Made: false, Destroy: false))
            .Concat(_inventory.AddedTo(target.Entity).Select(made => (made.Entity, Made: true, Destroy: SessionMadeItem.Number(made.Entity) is null ||
                !InventoryDomain.KitAdmitted(made.Kind.Type) || _inventory.Piles.ContainsKey(made.Entity) || _inventory.Ammo.ContainsKey(made.Entity) ||
                _inventory.Hero.IsGiven(made.Entity)))).Reverse().ToArray();
        // Each placement sees the items still to come as not yet dropped (not the ones that will be destroyed).
        var later = list.Where(item => !item.Destroy).Select(item => item.Entity).ToHashSet(StringComparer.Ordinal);
        foreach (var (entity, isMade, destroy) in list)
        {
            later.Remove(entity);
            if (isMade)
            {
                var made = _inventory.AddedTo(target.Entity).First(listed => listed.Entity == entity);
                if (destroy || _inventory.Created >= _limits.MaxCreatedItems)
                {
                    Discard(entity);
                    continue;
                }
                _inventory.RemoveFromNpc(entity);
                _inventory.Hero.AddPlaced(new(entity, made.Kind, SessionPlacedSource.Created, null, target.Map, target.Baseline.Cell, made.Complexity));
            }
            SettleDrop(entity, target.Map, target.Baseline.Cell, later);
            ScheduleDecay(entity, target.Map);
        }
    }
}

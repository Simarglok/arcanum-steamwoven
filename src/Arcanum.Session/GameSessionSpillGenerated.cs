using Arcanum.Simulation.Items;

namespace Arcanum.Session;

/// <summary>
/// ARC-WORLD-1301 (ledger VP19, VP23, VPG6): the break of a container with flag 0x200 that was never generated. The break's drop of the whole
/// inventory (0x463730, RK13, OD9) first runs the regeneration (0x463E20, RK10) on such a container, so the goods it would have held at its first
/// open lie on the ground; <c>ObjectWrite</c> runs that generation before the spill's walk (<c>SpillWalk</c>, which then drops the kept items
/// and the goods, last first, ARC-WORLD-1306).
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-WORLD-1301: the host's world for the break's generation of a container with flag 0x200 (the same <see cref="ISessionRestockWorld"/> the
    /// first loot open passes to <see cref="GenerateContainerOnce"/>), asked when a container breaks outside an event pass (inside one, the pass's
    /// world is used when it is ready). It may answer null. None set, a null answer or a host that is not ready (<see cref="ISessionRestockWorld.RestockReady"/>): the
    /// break spills only the stored items, as before (a recorded limit); so does a break whose seed facts the host cannot give
    /// (<see cref="ISessionRestockWorld.GeneratedOnceFacts"/>), a set the session does not admit, a transfer script on the container and a full
    /// session budget. The host must not call back into the session from its callbacks.
    /// </summary>
    public Func<ISessionRestockWorld?>? GenerateOnceWorld { get; set; }

    private sealed record BreakGeneration(ISessionRestockWorld World, RestockPlan Plan);

    // The generation a breaking container with flag 0x200 will run, planned before the break is written (the plan changes nothing): null for
    // a container that needs none or whose generation the session cannot run, which then spills as before.
    private BreakGeneration? PlanBreakGeneration(SessionObjectTarget target) => PlanBreakGeneration(target.Map, target.Entity);

    // ARC-WORLD-2502: the same planning for a container the script destroy removes (0x4639E0 runs the regeneration before its walk too).
    private BreakGeneration? PlanBreakGeneration(SessionMapId map, string entity)
    {
        // The pass's world when it can answer, else the host's own.
        var world = PassWorldAs<ISessionRestockWorld>() is { RestockReady: true } passed ? passed : GenerateOnceWorld?.Invoke();
        // The generation and the break are two revisions.
        if (_revision >= long.MaxValue - 1 || world is not { RestockReady: true } || !_inventory.Seeds.TryGetValue(map, out var seed) ||
            !seed.Holders.TryGetValue(entity, out var row) || row.Kind != SessionHolderKind.Container ||
            world.RestockFacts(map, entity) is not { Kind: RestockHolderKind.Container } found) return null;
        var facts = GeneratedFacts(map, entity, found);
        if ((facts.ContainerFlags & RestockRules.GeneratedOnceFlag) == 0) return null;
        var plan = PlanRestock(world, map, seed, entity, facts);
        return plan.Outcome == SessionRestockOutcome.Unsupported || !RestockFits([plan]) ? null : new(world, plan);
    }
}

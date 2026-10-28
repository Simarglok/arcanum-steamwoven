using Arcanum.Simulation.Items;

namespace Arcanum.Session;

/// <summary>ARC-WORLD-100: what the first open of a container with flag 0x200 did: the regeneration (null when the container has no flag
/// 0x200 or the session generated it already).</summary>
public sealed record SessionGenerateOnceResult(GameSessionDisposition Disposition, GameSessionFailure Failure, SessionRestockRegeneration? Regeneration,
    GameSessionVersion Version);

/// <summary>
/// ARC-WORLD-100: the generated-once containers in the session (flag 0x200; 0x572640 → 0x463E20, ledger RK10–RK12). A container with
/// flag 0x200 is generated the first time an inventory window opens on it in loot mode, before the window's restock schedule, or at a
/// restock event: its draws come from the original generator seeded from the hero and the container's place, and after them its source
/// and flag are cleared, so it never restocks. The session keeps the generated containers (snapshot schema 55); the host's data still
/// shows their source and flag, which the session overrides. Contract: docs/world/barter-runtime.md.
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>
    /// The first open's generation (the partner setup 0x572640 in loot mode 2 or 4, call site 0x572779, RK12): the host calls it for a
    /// <paramref name="container"/> of the actor's map before <see cref="ScheduleRestock"/>. A container with flag 0x200 (the host's
    /// <see cref="ISessionRestockWorld.RestockFacts"/>, unless the session generated it already) is cleared but for its persistent items
    /// (RK5) and generated from its set with the draws of <see cref="RestockRules.GenerateOnce"/> at the seed of
    /// <see cref="RestockRules.GeneratedOnceSeed"/> (the hero's race, gender and background, the host's
    /// <see cref="ISessionRestockWorld.GeneratedOnceFacts"/>); the session's own generator draws nothing. With a source it is then recorded
    /// as generated (its source and flag 0x200 cleared, RK10), so the schedule after it finds no source; a container with flag 0x200 and
    /// source 0 is only cleared and keeps its flag (none installed). Without flag 0x200 nothing changes; a container the session cannot
    /// regenerate (a set it does not admit, a transfer script, no seed facts from the host) keeps its list
    /// (<see cref="SessionRestockOutcome.Unsupported"/>, no change). Refused before any effect: an invalid entity
    /// (<see cref="GameSessionFailure.InvalidItem"/>), the version, no seed for the actor's map
    /// (<see cref="GameSessionFailure.InventoryUnavailable"/>), a container the seed or the host does not list as one, or a spilled one
    /// (<see cref="GameSessionFailure.UnknownHolder"/>), a full record, list or generated budget (<see cref="GameSessionFailure.BudgetExceeded"/>)
    /// and the revision. One revision.
    /// </summary>
    public SessionGenerateOnceResult GenerateContainerOnce(GameSessionVersion expected, string container, ISessionRestockWorld world)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(world);
        SessionGenerateOnceResult Refuse(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, Version);
        if (!SessionEventKinds.RestockHolder(container)) return Refuse(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return Refuse(GameSessionFailure.StaleVersion);
        var map = _actor.Map;
        if (!_inventory.Seeds.TryGetValue(map, out var seed)) return Refuse(GameSessionFailure.InventoryUnavailable);
        if (!seed.Holders.TryGetValue(container, out var row) || row.Kind != SessionHolderKind.Container || SpillCell(map, seed, container) is not null ||
            world.RestockFacts(map, container) is not { Kind: RestockHolderKind.Container } found) return Refuse(GameSessionFailure.UnknownHolder);
        var facts = GeneratedFacts(map, container, found);
        if ((facts.ContainerFlags & RestockRules.GeneratedOnceFlag) == 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, Version);
        var plan = PlanRestock(world, map, seed, container, facts);
        // A call back into the session from the host may have moved it on.
        if (!IsCurrent(expected)) return Refuse(GameSessionFailure.StaleVersion);
        if (plan.Outcome == SessionRestockOutcome.Unsupported)
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, new(container, plan.Outcome, 0, 0, null, [], 0, 0), Version);
        if (!RestockFits([plan])) return Refuse(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return Refuse(GameSessionFailure.RevisionExhausted);
        var done = Regenerate(world, map, plan);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, done, Version);
    }

    /// <summary>ARC-WORLD-100: whether the session generated the container with flag 0x200 on the map once (its source and flag are then
    /// cleared, whatever the host's data says).</summary>
    public bool QueryContainerGenerated(SessionMapId map, string container)
    {
        ThrowIfDisposed();
        return container is not null && _inventory.Generated.Contains(new(map, container));
    }
}

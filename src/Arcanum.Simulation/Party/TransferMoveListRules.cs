namespace Arcanum.Simulation.Party;

/// <summary>
/// The followers of a PC's change of place as the original's teleport request lists them (ARC-DEV-073, migration step MIG10; research
/// FM1, FM2, RL22, RL23): the direct followers it orders to wait first (<see cref="Waiting"/>) and the followers that travel with the
/// PC (<see cref="Travelling"/>), in the move list's order. A pure result; the session applies it.
/// </summary>
public sealed record TransferMoveList(IReadOnlyList<string> Waiting, IReadOnlyList<string> Travelling)
{
    public static TransferMoveList Empty { get; } = new([], []);
}

/// <summary>
/// The move list of a PC's change of place (the request processor 0x4D3460 and the list walk 0x4D3760, research FM1 and FM2, read again
/// in <c>original-object-model.md</c> RL22 and RL23), as pure rules over the PC's follower array and the facts the host answers.
/// <para>
/// Before the walk, unless the request carries "no wait orders" (flag 0x100, set by teleport spells), every direct follower for which
/// 0x45D800 answers yes is ordered to wait: the test reads fatigue and is true for a critter that is not undead and has no fatigue
/// left, so the followers left behind are the unconscious, not the dead. A waiting follower leaves the follower array and is not walked.
/// The walk then takes each remaining follower and, before the follower itself, the followers of that follower, recursively: followers of
/// followers travel too, a node is listed after its own followers, and an object already listed is not listed twice.
/// </para>
/// </summary>
public static class TransferMoveListRules
{
    /// <summary>The deepest chain of leaders the walk follows; a deeper chain is cut (the original's list is bounded by its object pool).</summary>
    public const int MaxDepth = 32;

    /// <param name="followers">The PC's follower array, in order.</param>
    /// <param name="noFatigue">0x45D800 for a direct follower: not undead and no fatigue left.</param>
    /// <param name="followersOf">The follower array of an NPC (a follower's own followers, none waiting among them); null answers none.</param>
    /// <param name="noWaitOrders">The request's flag 0x100.</param>
    public static TransferMoveList Build(IReadOnlyList<string> followers, Func<string, bool> noFatigue,
        Func<string, IReadOnlyList<string>>? followersOf, bool noWaitOrders)
    {
        ArgumentNullException.ThrowIfNull(followers);
        ArgumentNullException.ThrowIfNull(noFatigue);
        var waiting = new List<string>();
        var travelling = new List<string>();
        // A follower ordered to wait is never walked, even below another leader.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!noWaitOrders)
            foreach (var follower in followers)
                if (noFatigue(follower) && seen.Add(follower)) waiting.Add(follower);
        foreach (var follower in followers) Walk(follower, followersOf, seen, travelling, 0);
        return new(waiting.AsReadOnly(), travelling.AsReadOnly());
    }

    // A node is listed after the followers walked below it; the visited set keeps a cycle or a repeated follower out.
    private static void Walk(string npc, Func<string, IReadOnlyList<string>>? followersOf, HashSet<string> seen, List<string> list, int depth)
    {
        if (depth > MaxDepth || !seen.Add(npc)) return;
        if (followersOf?.Invoke(npc) is { } below)
            foreach (var follower in below) Walk(follower, followersOf, seen, list, depth + 1);
        list.Add(npc);
    }
}

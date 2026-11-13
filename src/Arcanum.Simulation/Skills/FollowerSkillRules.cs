using Arcanum.Simulation.Party;

namespace Arcanum.Simulation.Skills;

/// <summary>
/// ARC-COMBAT-1777 (Follower Skills slice 2, ledger PL5, OH4): the best-follower chooser of the PC's lockpick item use and of the skill bar's Disarm Traps (0x4C9050, with the setting getter 0x4C8FA0), as pure rules over the
/// scores the host computes. For a PC user with the setting "follower skills" on, the chooser scores the PC first and then every follower of the PC's all-followers list (0x441260) as the skill's effectiveness minus its
/// difficulty for the same request (<see cref="SkillCheckRules.Score"/>) and replaces the user by a follower only with a strictly higher score: a tie keeps the PC or the earlier follower. There is no test of life, distance or sight in
/// the function; this model leaves out a follower it cannot score (no sheet, no life record, dead), which the host decides.
/// </summary>
public static class FollowerSkillRules
{
    /// <summary>The "follower skills" setting's default (the skill module's init, 0x4C5BD0).</summary>
    public const bool DefaultOn = true;

    /// <summary>The hand-over line when the user changed (0x4139A0): a random line of this range of the follower's own dialog override or of the gender pair's gd_npc file.</summary>
    public static readonly LoyaltyLines HandOverLines = new(4700, 4799);

    /// <summary>The deepest chain of leaders the follower list follows; a deeper chain is cut (the original's list is bounded by its object pool).</summary>
    public const int MaxDepth = 32;

    /// <summary>
    /// The PC's all-followers list (0x441260): its direct followers in order, then, for each of them in order, that follower's own list (recursively). A follower reached twice is listed once (the original has no such guard; a cycle
    /// would not end there), and a chain deeper than <see cref="MaxDepth"/> is cut.
    /// </summary>
    /// <param name="followersOf">The follower array of an object (the PC's or an NPC's); null answers none.</param>
    public static IReadOnlyList<string> AllFollowers(string leader, Func<string, IReadOnlyList<string>>? followersOf)
    {
        ArgumentNullException.ThrowIfNull(leader);
        var list = new List<string>();
        Walk(leader, followersOf, new HashSet<string>(StringComparer.Ordinal) { leader }, list, 0);
        return list.AsReadOnly();
    }

    private static void Walk(string leader, Func<string, IReadOnlyList<string>>? followersOf, HashSet<string> seen, List<string> list, int depth)
    {
        if (depth >= MaxDepth || followersOf?.Invoke(leader) is not { } direct) return;
        var added = new List<string>();
        foreach (var follower in direct)
            if (seen.Add(follower)) { list.Add(follower); added.Add(follower); }
        foreach (var follower in added) Walk(follower, followersOf, seen, list, depth + 1);
    }

    /// <summary>
    /// The chooser's pick: the index of the strictly best score in <paramref name="scores"/>, the PC's first and the followers' after it in list order; a later entry replaces the best only with a strictly higher score, so ties keep the
    /// earlier one. Index 0 (the PC) when the list holds one entry or none better.
    /// </summary>
    public static int Choose(IReadOnlyList<int> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);
        if (scores.Count == 0) throw new ArgumentException("The PC's score is the first entry.", nameof(scores));
        var best = 0;
        for (var i = 1; i < scores.Count; i++)
            if (scores[best] < scores[i]) best = i;
        return best;
    }
}

using System.Collections.Immutable;

namespace Arcanum.Simulation.Magic;

/// <summary>
/// ARC-MAGIC-058 (magic plan slice MT9b, ledger MX1-MX6): which spell runs go with a player character's change to another map and which end.
/// The original's teleport notification (0x45C720, only when the destination differs and the teleported object is a player character) calls 0x44F7A0,
/// which walks the 512 run slots in order and, for every run in use, tests the run's object handles against the travel list (0x4D3420: the mover, its
/// followers recursively and every item of their inventories): the source (+0x10), the parent (+0x58; its own handle when it is the source, or none),
/// the target (+0xA0) and the two object lists (+0x130 and +0x134, the summoned list), each passing when it is none or on the list. A run that passes
/// goes into the destination map's <c>MT.dat</c> and its slot is freed; every other run is ended at once by 0x457270 (<see cref="SpellRunSystem.ForceEnd"/>).
/// The other object (+0xE8) is not tested. At the map close that follows, whatever run is still in the table is parked in the left map's <c>MT.dat</c>
/// (0x44FA70, no test); after the forced ends there is none (a forced end frees the run unless its END callback is pending, which no
/// teleport waits for), and a map entry restores the file's runs with their ticks (0x44FC30).
/// <para>
/// Steamwoven keeps one run table with global <c>magic.run</c> events, so a run that travels simply stays in it (its ticks go on: what the original's
/// write and re-read does), and the runs of a map that is not shown never exist: the forced ends are the whole of the original's step.
/// </para>
/// </summary>
public static class SpellRunTravelRules
{
    /// <summary>
    /// The runs the player's change of map ends, in slot order (0x44F7A0). <paramref name="travels"/> answers whether an object is on the travel list;
    /// an item (a run object of an item type) is on it when its holder is, which the run knows as its parent. <paramref name="summoned"/> lists the objects
    /// a run holds as summoned (the cast host's), the second list the original tests.
    /// </summary>
    [OriginalFunction(0x44F7A0, OriginalFunctionScope.Partial)]
    public static ImmutableArray<int> RunsLeft(IEnumerable<SpellRun> runs, Func<string, bool> travels, Func<SpellRun, IEnumerable<string>> summoned)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(travels);
        ArgumentNullException.ThrowIfNull(summoned);
        var left = ImmutableArray.CreateBuilder<int>();
        foreach (var run in runs)
            if (!Goes(run, travels, summoned)) left.Add(run.Id);
        return left.ToImmutable();
    }

    /// <summary>Whether one run in use goes with the travellers: every object it names is none or on the list (0x44F7A0's test).</summary>
    public static bool Goes(SpellRun run, Func<string, bool> travels, Func<SpellRun, IEnumerable<string>> summoned)
    {
        ArgumentNullException.ThrowIfNull(run);
        if ((run.Flags & SpellRunFlags.Active) == 0) return true;
        bool OnList(SpellRunObject? who) =>
            who is null || (IsItem(who) ? run.Parent is { } holder && !IsItem(holder) && travels(holder.Id) : travels(who.Id));
        return OnList(run.Source) && (run.Parent is null || run.Source is not null && run.Parent.Id == run.Source.Id || OnList(run.Parent)) && OnList(run.Target)
            && summoned(run).All(travels);
    }

    private static bool IsItem(SpellRunObject who) => who.Type is >= SpellObjectFacts.FirstItemType and <= SpellObjectFacts.LastItemType;
}

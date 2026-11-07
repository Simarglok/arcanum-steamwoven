using System.Collections.Immutable;

namespace Arcanum.Simulation.Goals;

public sealed partial class GoalRunTable
{
    /// <summary>
    /// ARC-DEV-208 (MIG14 S2, docs/architecture/saved-runs.md §3.1): a table holding exactly <paramref name="runs"/> in their own slots with the next run id
    /// <paramref name="nextRunId"/>, for a load. The stepping marker and the exhaustion flag are clear (a save is never taken inside a step, and the flag is a
    /// property of the last allocation, not of a save). Null when the runs cannot be a table: a slot outside 0..<see cref="SlotCount"/>, two runs in one slot,
    /// a run id below 1 or a next id below 1.
    /// </summary>
    public static GoalRunTable? Restore(int nextRunId, IEnumerable<GoalRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        if (nextRunId < 1) return null;
        var slots = new GoalRun?[SlotCount];
        var count = 0;
        foreach (var run in runs)
        {
            var slot = run.Handle.Slot;
            if (slot is < 0 or >= SlotCount || run.Handle.RunId < 1 || slots[slot] is not null) return null;
            slots[slot] = run;
            count++;
        }
        return new(ImmutableArray.Create(slots), nextRunId, count, false, null);
    }
}

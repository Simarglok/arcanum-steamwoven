using System.Collections.Immutable;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;

namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-208 (MIG14 S2, docs/architecture/saved-runs.md §3.1; schema 72, the <c>goals</c> section): the goal runs of the active map as a save writes them
/// (the original's animation module saves its run table with every goal stack, PS15): the next run id and the runs in slot order, each a plain value (the
/// owner's entity string, the frames with entity strings, cells and integers). The steps are not saved: a restore re-creates one <c>goal.step</c> per
/// run at its due time. The hero's runs, the flights of spell missiles and projectiles and the runs that name a spell run are not written until the slices
/// that save those (S4, S5, S6); a game of an older schema has none.
/// </summary>
public sealed record SessionGoalState(int NextRunId, IReadOnlyList<GoalRun> Runs)
{
    public static SessionGoalState Empty { get; } = new(1, []);

    public bool IsEmpty => Runs.Count == 0;
}

/// <summary>The limits of a saved run, shared by the codec and the restore.</summary>
public static class SessionGoalLimits
{
    /// <summary>The longest planned path a frame may hold (the executor plans segments, never a whole map walk).</summary>
    public const int MaxPathCells = 4096;
}

public sealed partial class GameSessionOwner
{
    // The runs a save writes (saved-runs.md §3.1, §3.4, §3.5): not done, not the one being stepped, and of a kind this build saves.
    private bool GoalRunIsSaved(GoalRun run) => !run.Done && _goals.SteppingSlot != run.Handle.Slot && GoalRunIsSavedKind(run);

    // The kinds a save holds: the hero's runs, the flights of missiles and projectiles and the runs that name a spell run wait for S4-S6. The restore applies the
    // same test, so an edited file never brings a frame that dangles.
    private static bool GoalRunIsSavedKind(GoalRun run) =>
        !string.Equals(run.Owner, AiSchedulerRules.Pc, StringComparison.Ordinal)
        && !run.Owner.StartsWith(SessionMissile.EntityPrefix, StringComparison.Ordinal)
        && !run.Owner.StartsWith(SessionProjectile.EntityPrefix, StringComparison.Ordinal)
        && run.Frames.All(f => f.SpellRun is null && f.EyeCandy is not { Tag: >= 0 });

    private SessionGoalState CaptureGoals() =>
        _goals.InUseCount == 0 ? new(_goals.NextRunId, []) : new(_goals.NextRunId, [.. _goals.Runs.Where(GoalRunIsSaved)]);

    // Shapes the codec cannot know: the owners and entities are session entities, every goal has a descriptor (the run type guarantees it), the slots are
    // distinct and below the table's size, the due time is not negative and the path index is inside the path.
    private static GameSessionFailure ImportGoals(SessionGoalState saved, out GoalRunTable table)
    {
        table = GoalRunTable.Empty;
        if (saved.NextRunId < 1) return GameSessionFailure.InvalidSnapshot;
        if (saved.Runs.Count > GoalRunTable.SlotCount) return GameSessionFailure.BudgetExceeded;
        var seen = new HashSet<int>();
        foreach (var run in saved.Runs)
        {
            if (!seen.Add(run.Handle.Slot) || run.Handle.RunId < 1 || run.Done || run.DueMs < 0 || run.PaceMs < 0) return GameSessionFailure.InvalidSnapshot;
            if (!SessionValidation.Entity(run.Owner) || !GoalRunIsSavedKind(run)) return GameSessionFailure.InvalidSnapshot;
            foreach (var frame in run.Frames)
            {
                if (frame.Target is not null && !SessionValidation.Entity(frame.Target)) return GameSessionFailure.InvalidSnapshot;
                if (frame.Item is not null && !SessionValidation.Entity(frame.Item)) return GameSessionFailure.InvalidSnapshot;
                if (frame.EyeCandy?.Source is { } source && !SessionValidation.Entity(source)) return GameSessionFailure.InvalidSnapshot;
                if (frame.PathIndex < 0 || frame.PathIndex > frame.Path.Length || frame.Path.Length > SessionGoalLimits.MaxPathCells) return GameSessionFailure.InvalidSnapshot;
            }
        }
        var restored = GoalRunTable.Restore(saved.NextRunId, saved.Runs);
        if (restored is null) return GameSessionFailure.InvalidSnapshot;
        table = restored;
        return GameSessionFailure.None;
    }

    // The restore: the runs are the table, and each gets the one step it is waiting for, in slot order, at its saved due time (a due time already past fires at the
    // frame step's next pass: the delay is at least 1 ms). A run whose step the queue refuses is dropped, so no run waits for a step that will not come.
    private void RestoreGoalSteps(GoalRunTable table)
    {
        _goals = table;
        var now = _frame.AnimationMs;
        foreach (var run in table.Runs.ToArray())
        {
            var delay = Math.Max(1L, run.DueMs - now);
            if (ScheduleGoalStep(run.Handle, run.Owner, delay) is not null) continue;
            var freed = _goals.Free(run.Handle);
            _goals = freed.Table;
        }
    }
}

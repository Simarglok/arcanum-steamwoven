using System.Collections.Immutable;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;

namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-208 (MIG14 S2, docs/architecture/saved-runs.md §3.1; schema 72, the <c>goals</c> section): the goal runs of the active map as a save writes them
/// (the original's animation module saves its run table with every goal stack, PS15): the next run id and the runs in slot order, each a plain value (the
/// owner's entity string, the frames with entity strings, cells and integers). The steps are not saved: a restore re-creates one <c>goal.step</c> per
/// run at its due time. ARC-DEV-290: the hero's walk, turn and states are written (its other runs wait for their slices); ARC-DEV-288: a projectile's flight is, with its projectile (the <c>flights</c> section);
/// ARC-DEV-289: a spell missile's flight is, with its missile, and an NPC's casting goals are, with the spell runs they name (<see cref="GameSessionOwner"/>'s saved casts). A game of an older schema has none.
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
    // ARC-DEV-288: a projectile's flight run goes with its projectile (saved only when the projectile is); ARC-DEV-289: a missile's with its missile.
    // ARC-DEV-290: a hero's run only with a hero (a session without one, a synthetic fixture's, keeps none: the restore refuses it).
    private bool GoalRunIsSavedAlone(GoalRun run) => !run.Done && _goals.SteppingSlot != run.Handle.Slot && GoalRunIsSavedKind(run)
        && (_hero is not null || !string.Equals(run.Owner, AiSchedulerRules.Pc, StringComparison.Ordinal))
        && (!run.Owner.StartsWith(SessionProjectile.EntityPrefix, StringComparison.Ordinal) || ProjectileRunIsSaved(run))
        && (!run.Owner.StartsWith(SessionMissile.EntityPrefix, StringComparison.Ordinal) || MissileRunIsSaved(run));

    // The kinds a save holds: the hero's runs wait for their slice. The restore applies the same test, so an edited file never brings a frame that dangles.
    private static bool GoalRunIsSavedKind(GoalRun run) =>
        // The overlays (eye candy, burning, hover) and the knockback slide have a viewer host that a load does not rebuild (it is built by its first Play or push), so a saved
        // run would be given up at its first step: they stay out until their slices. ARC-DEV-288: a projectile's flight (goal 36 on its own projectile) is saved, the projectile host
        // rebuilds it from the saved projectile; a goal 36 run of another owner, or another goal on a projectile, is not. ARC-DEV-289: a missile's flight (goal 28 on its own missile)
        // the same way, and a frame may name a spell run (the casting goals 26, 82, 27, 83 and the missile's 28); an eye candy tied to a spell run is an overlay and stays out.
        !AiExecutorRules.IsOverlayRunGoal(run.Bottom.Goal) && !AiExecutorRules.IsKnockbackRunGoal(run.Bottom.Goal)
        && AiExecutorRules.IsProjectileRunGoal(run.Bottom.Goal) == run.Owner.StartsWith(SessionProjectile.EntityPrefix, StringComparison.Ordinal)
        && AiExecutorRules.IsMissileRunGoal(run.Bottom.Goal) == run.Owner.StartsWith(SessionMissile.EntityPrefix, StringComparison.Ordinal)
        && (!string.Equals(run.Owner, AiSchedulerRules.Pc, StringComparison.Ordinal) || HeroRunIsSavedKind(run))
        && run.Frames.All(f => f.EyeCandy is not { Tag: >= 0 });

    // ARC-DEV-290 (MIG14 S9a): the hero's runs this build saves: its walk (goals 3 and 4 at the bottom; the step 5, the turn 53 and the pause the walk pushed above), its turn (53) and its states (the stun 57,
    // the knock-down 62, the get-up 63, dying 31), which the hero's host steps over the session and the viewer's body alone; ARC-DEV-317 (S9b): its attack and throw, whose viewer holders a load rebuilds from the run.
    // The hero's talk, use, pickup, cast and skill runs wait for their slices (S9c, S9d).
    internal static bool HeroRunIsSavedKind(GoalRun run) => run.Bottom.Goal switch
    {
        GoalType.MoveToTile or GoalType.RunToTile => run.Frames.All(f => f.Goal is GoalType.MoveToTile or GoalType.RunToTile or GoalType.AttemptMove or GoalType.Rotate or GoalType.MoveToPause
            && f.Target is null && f.SpellRun is null && f.Item is null),
        GoalType.Rotate or GoalType.AnimateStunned or GoalType.KnockDown or GoalType.GetUp or GoalType.Dying => run.Frames.Length == 1,
        // ARC-DEV-317 (S9b): the hero's attack (goal 18, or 19 once it replaced it) with the walk its approach pushed, every frame naming the attack's target; and its throw (37: one frame with the item and the tile).
        GoalType.Attack or GoalType.AttemptAttack => run.Bottom.Target is { } target && run.Frames.All(f => f.Target == target && f.Item is null && f.SpellRun is null
            && f.Goal is GoalType.Attack or GoalType.AttemptAttack or GoalType.MoveToTile or GoalType.RunToTile or GoalType.AttemptMove or GoalType.Rotate or GoalType.MoveToPause),
        GoalType.ThrowItem => run.Frames.Length == 1 && run.Bottom is { Item: not null, Tile: not null, Target: null, SpellRun: null },
        _ => false,
    };

    private static int HeroRunKind(GoalType bottom) => bottom switch
    {
        GoalType.MoveToTile or GoalType.RunToTile => 0,
        GoalType.Rotate => 1,
        GoalType.AnimateStunned => 2,
        GoalType.Attack or GoalType.AttemptAttack => 4,
        GoalType.ThrowItem => 5,
        _ => 3, // the knock-down, the get-up and dying
    };

    // ARC-DEV-290: the restored table holds the hero's stun run (its end clears the stunned bit, which the load then keeps).
    private static bool HoldsHeroStun(GoalRunTable table) =>
        table.Runs.Any(r => string.Equals(r.Owner, AiSchedulerRules.Pc, StringComparison.Ordinal) && r.Bottom.Goal == GoalType.AnimateStunned);

    // ARC-DEV-290: the hero's walk (ARC-DEV-317: and an attack's approach walk) is written with its replan flag (the retarget's, GoalRunTable.Retarget): the step under way at the save is not saved (the hero's place is the committed cell), so the
    // loaded walk plans again from where the hero stands, as a retarget's walk does; a second save of the loaded walk is the same text. A far goal's mark that the viewer's route follows it (RouteFollow,
    // Steamwoven's) is not written either: the loaded route follows nothing, and the plan hands the goal to it again.
    private static GoalRun SavedHeroWalk(GoalRun run) =>
        string.Equals(run.Owner, AiSchedulerRules.Pc, StringComparison.Ordinal) && run.Frames.Any(f => f.Goal is GoalType.MoveToTile or GoalType.RunToTile)
            ? run.WithFrames([.. run.Frames.Select(f => f with { Flags = f.Flags & ~GoalFrameFlags.RouteFollow })]) with { PathFlags = run.PathFlags | GoalPathFlags.Replan }
            : run;

    private SessionGoalState CaptureGoals()
    {
        if (_goals.InUseCount == 0) return new(_goals.NextRunId, []);
        var saved = SavedCasts().Goals;
        return new(_goals.NextRunId, [.. _goals.Runs.Where(run => saved.Contains(run.Handle.Slot)).Select(SavedHeroWalk)]);
    }

    // Shapes the codec cannot know: the owners and entities are session entities, every goal has a descriptor (the run type guarantees it), the slots are
    // distinct and below the table's size, the due time is not negative and the path index is inside the path.
    private static GameSessionFailure ImportGoals(SessionGoalState saved, long animationMs, bool hero, out GoalRunTable table)
    {
        table = GoalRunTable.Empty;
        if (saved.NextRunId < 1) return GameSessionFailure.InvalidSnapshot;
        if (saved.Runs.Count > GoalRunTable.SlotCount) return GameSessionFailure.BudgetExceeded;
        var seen = new HashSet<int>();
        foreach (var run in saved.Runs)
        {
            if (!seen.Add(run.Handle.Slot) || run.Handle.RunId < 1 || run.Done || run.DueMs < 0 || run.PaceMs < 0 || run.DueMs - animationMs > MaxGoalStepDelayMs * GoalStepClockRate) return GameSessionFailure.InvalidSnapshot;
            if (!SessionValidation.Entity(run.Owner) || !GoalRunIsSavedKind(run)) return GameSessionFailure.InvalidSnapshot;
            // ARC-DEV-290: a run of the hero needs a hero.
            if (!hero && string.Equals(run.Owner, AiSchedulerRules.Pc, StringComparison.Ordinal)) return GameSessionFailure.InvalidSnapshot;
            foreach (var frame in run.Frames)
            {
                if (frame.Target is not null && !SessionValidation.Entity(frame.Target)) return GameSessionFailure.InvalidSnapshot;
                if (frame.Item is not null && !SessionValidation.Entity(frame.Item)) return GameSessionFailure.InvalidSnapshot;
                if (frame.EyeCandy?.Source is { } source && !SessionValidation.Entity(source)) return GameSessionFailure.InvalidSnapshot;
                if (frame.PathIndex < 0 || frame.PathIndex > frame.Path.Length || frame.Path.Length > SessionGoalLimits.MaxPathCells) return GameSessionFailure.InvalidSnapshot;
            }
        }
        // ARC-DEV-290: the hero has at most one run of each kind the build saves (the walk starter retargets the one walk; a turn, a stun or a state interrupts the hero's runs before it starts).
        if (saved.Runs.Where(r => string.Equals(r.Owner, AiSchedulerRules.Pc, StringComparison.Ordinal)).GroupBy(r => HeroRunKind(r.Bottom.Goal)).Any(g => g.Count() > 1)) return GameSessionFailure.InvalidSnapshot;
        var restored = GoalRunTable.Restore(saved.NextRunId, [.. saved.Runs.Select(SavedHeroWalk)]);
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
            Unclaimed([Finish(run, GoalEndKind.Killed, endNodes: false)]);
        }
    }
}

using System.Collections.Immutable;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Magic;

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

    // The kinds a save holds. The restore applies the same test, so an edited file never brings a frame that dangles.
    // The knockback slide has a viewer host that a load does not rebuild (it is built by its first push), so its run stays out until its slice. ARC-DEV-288: a projectile's flight (goal 36 on its own projectile) is saved, the
    // projectile host rebuilds it from the saved projectile; a goal 36 run of another owner, or another goal on a projectile, is not. ARC-DEV-289: a missile's flight (goal 28 on its own missile) the same way, and a frame may
    // name a spell run (the casting goals 26, 82, 27, 83 and the missile's 28); an eye candy tied to a spell run is an overlay (below), not part of a casting goal's stack.
    // ARC-MAGIC-1280: the overlay runs (eye candy 45–48, 58, 59, 75, 76 and the hover 44) are saved (OverlayRunIsSavedKind): the overlay host is built again by the first step of a restored run.
    private static bool GoalRunIsSavedKind(GoalRun run) =>
        AiExecutorRules.IsOverlayRunGoal(run.Bottom.Goal) ? OverlayRunIsSavedKind(run)
        : !AiExecutorRules.IsKnockbackRunGoal(run.Bottom.Goal)
        && AiExecutorRules.IsProjectileRunGoal(run.Bottom.Goal) == run.Owner.StartsWith(SessionProjectile.EntityPrefix, StringComparison.Ordinal)
        && AiExecutorRules.IsMissileRunGoal(run.Bottom.Goal) == run.Owner.StartsWith(SessionMissile.EntityPrefix, StringComparison.Ordinal)
        && (!string.Equals(run.Owner, AiSchedulerRules.Pc, StringComparison.Ordinal) || HeroRunIsSavedKind(run))
        && run.Frames.All(f => f.EyeCandy is not { Tag: >= 0 });

    // ARC-MAGIC-1280 (saved-runs.md, the overlay runs): an overlay run is a stack of eye candy goals (a stack-flag add queues one under another) or the hover, on any owner but a missile or a projectile (their
    // runs are the flight's own); the hero's overlay runs are written with a hero only (GoalRunIsSavedAlone). Every frame is an overlay goal of its own kind: an eye candy goal holds its eye candy and a hover frame
    // none, and none names a target, an item, a tile, a skill, a spell run or a path (the add builds none). The tag an eye candy holds is the spell run it belongs to: it is written only as a slot of the spell
    // run table or as none (a negative), a tag of a run that is gone is written as none (OverlayRunTagged); the restore checks the slot is held (CheckCastRuns).
    private static bool OverlayRunIsSavedKind(GoalRun run) =>
        !run.Owner.StartsWith(SessionProjectile.EntityPrefix, StringComparison.Ordinal) && !run.Owner.StartsWith(SessionMissile.EntityPrefix, StringComparison.Ordinal)
        && run.Frames.All(f => AiExecutorRules.IsOverlayGoal(f.Goal) && (f.EyeCandy is not null) == AiExecutorRules.IsEyeCandyGoal(f.Goal)
            && f.Target is null && f.Item is null && f.Tile is null && f.Skill is null && f.SpellRun is null && f.Path.IsEmpty && f.PlannedTargetTile is null
            && f.EyeCandy is not { Tag: >= SpellRunTable.Slots });

    // ARC-MAGIC-1280: the overlay run as a save writes it: an eye candy whose spell run is gone (its slot is empty now, or reused) calls nobody back any more, so it is written with no tag (the host's stale mark of
    // the live game, ARC-DEV-352, which a load does not keep); the other frames and runs are written as they are.
    private GoalRun SavedOverlayRun(GoalRun run) =>
        AiExecutorRules.IsOverlayRunGoal(run.Bottom.Goal) && run.Frames.Any(f => f.EyeCandy is { Tag: >= 0 } eye && _spellRuns[eye.Tag] is null)
            ? run.WithFrames([.. run.Frames.Select(f => f.EyeCandy is { Tag: >= 0 } eye && _spellRuns[eye.Tag] is null ? f with { EyeCandy = eye with { Tag = -1 } } : f)])
            : run;

    // ARC-DEV-290 (MIG14 S9a): the hero's runs this build saves: its walk (goals 3 and 4 at the bottom; the step 5, the turn 53 and the pause the walk pushed above), its turn (53) and its states (the stun 57,
    // the knock-down 62, the get-up 63, dying 31), which the hero's host steps over the session and the viewer's body alone; ARC-DEV-317 (S9b): its attack and throw, whose viewer holders a load rebuilds from the run.
    // ARC-DEV-318 (S9c): its cast (goal 26, or 27 once it replaced it, with the casts queued beneath it as goals 26 that never started, 0x44D9B0), every frame naming its own spell run.
    // ARC-DEV-319 (S9d): its talk (21), use (38, 81, or the door goal 11, 12, 49 or 50 a use of a door became), pickup (15 with its take 16) and lock pick (33 with skill 14, or 67 once it replaced it), each with
    // the approach 8 it pushed (naming the same target) and the walk the approach pushed (naming none). ARC-DEV-321 (S9e): the pick pocket (33 with skill 5, or 86 once it replaced it) the same way, with its item and its plant flag.
    internal static bool HeroRunIsSavedKind(GoalRun run) => run.Bottom.Goal switch
    {
        GoalType.MoveToTile or GoalType.RunToTile => run.Frames.All(f => f.Goal is GoalType.MoveToTile or GoalType.RunToTile or GoalType.AttemptMove or GoalType.Rotate or GoalType.MoveToPause
            && f.Target is null && f.SpellRun is null && f.Item is null),
        GoalType.Rotate or GoalType.AnimateStunned or GoalType.KnockDown or GoalType.GetUp or GoalType.Dying => run.Frames.Length == 1,
        // ARC-DEV-317 (S9b): the hero's attack (goal 18, or 19 once it replaced it) with the walk its approach pushed, every frame naming the attack's target; and its throw (37: one frame with the item and the tile).
        GoalType.Attack or GoalType.AttemptAttack => run.Bottom.Target is { } target && run.Frames.All(f => f.Target == target && f.Item is null && f.SpellRun is null
            && f.Goal is GoalType.Attack or GoalType.AttemptAttack or GoalType.MoveToTile or GoalType.RunToTile or GoalType.AttemptMove or GoalType.Rotate or GoalType.MoveToPause),
        GoalType.ThrowItem => run.Frames.Length == 1 && run.Bottom is { Item: not null, Tile: not null, Target: null, SpellRun: null },
        GoalType.Talk => ApproachRun(run, static g => g is GoalType.Talk, items: false),
        GoalType.UseObject or GoalType.UseContainer or GoalType.OpenDoor or GoalType.AttemptOpenDoor or GoalType.CloseDoor or GoalType.AttemptCloseDoor =>
            ApproachRun(run, static g => g is GoalType.UseObject or GoalType.UseContainer or GoalType.OpenDoor or GoalType.AttemptOpenDoor or GoalType.CloseDoor or GoalType.AttemptCloseDoor, items: false),
        GoalType.PickupItem => ApproachRun(run, static g => g is GoalType.PickupItem or GoalType.AttemptPickup, items: false),
        GoalType.UseSkill or GoalType.AttemptPickLocks or GoalType.AttemptPickpocket => run.Bottom.Item is not null && run.Bottom.Goal switch
            {
                GoalType.AttemptPickLocks => run.Bottom.Skill == AiExecutorRules.PickLocksSkill,
                GoalType.AttemptPickpocket => run.Bottom.Skill == AiExecutorRules.PickpocketSkill,
                _ => run.Bottom.Skill is AiExecutorRules.PickLocksSkill or AiExecutorRules.PickpocketSkill,
            }
            // The attempt goal that replaces 33 is the skill's own: 67 for pick locks, 86 for pick pocket; a frame of the other skill's attempt refuses the save.
            && ApproachRun(run, run.Bottom.Skill == AiExecutorRules.PickLocksSkill ? static g => g is GoalType.UseSkill or GoalType.AttemptPickLocks : static g => g is GoalType.UseSkill or GoalType.AttemptPickpocket, items: true)
            && run.Frames.All(f => f.Goal == GoalType.MoveNear || f.Target is null || f.Item == run.Bottom.Item && f.Skill == run.Bottom.Skill && f.Flags == run.Bottom.Flags),
        GoalType.CastSpell or GoalType.AttemptSpell => run.Frames.Select((f, i) => f is { SpellRun: not null, Item: null, Tile: null }
            && (i == run.Frames.Length - 1 ? f.Goal is GoalType.CastSpell or GoalType.AttemptSpell : f is { Goal: GoalType.CastSpell, Node: 1 })).All(ok => ok)
            && run.Frames.Select(f => f.SpellRun).Distinct().Count() == run.Frames.Length,
        _ => false,
    };

    // ARC-DEV-319: a run of an action on a target: the bottom names the target, every frame that names it is the action's own goal or the approach 8, and every other frame is the approach's walk (3, 4, the step 5, the turn 53,
    // the pause 6, naming nothing); no frame names a spell run, and only a skill's frames an item.
    private static bool ApproachRun(GoalRun run, Func<GoalType, bool> own, bool items) => run.Bottom.Target is { } target && run.Frames.All(f => f.SpellRun is null && (items || f.Item is null)
        && (f.Target == target ? own(f.Goal) || f.Goal == GoalType.MoveNear && f.Item is null
            : f.Target is null && f.Item is null && f.Goal is GoalType.MoveToTile or GoalType.RunToTile or GoalType.AttemptMove or GoalType.Rotate or GoalType.MoveToPause));

    private static int HeroRunKind(GoalType bottom) => bottom switch
    {
        GoalType.MoveToTile or GoalType.RunToTile => 0,
        GoalType.Rotate => 1,
        GoalType.AnimateStunned => 2,
        GoalType.Attack or GoalType.AttemptAttack => 4,
        GoalType.ThrowItem => 5,
        GoalType.CastSpell or GoalType.AttemptSpell => 6,
        GoalType.Talk => 7,
        GoalType.UseObject or GoalType.UseContainer or GoalType.OpenDoor or GoalType.AttemptOpenDoor or GoalType.CloseDoor or GoalType.AttemptCloseDoor => 8,
        GoalType.PickupItem => 9,
        GoalType.UseSkill or GoalType.AttemptPickLocks or GoalType.AttemptPickpocket => 10,
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
        return new(_goals.NextRunId, [.. _goals.Runs.Where(run => saved.Contains(run.Handle.Slot)).Select(run => SavedOverlayRun(SavedHeroWalk(run)))]);
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
        // ARC-MAGIC-1280: its overlay runs (eye candy, hover) are not of these kinds: it may hold several.
        if (saved.Runs.Where(r => string.Equals(r.Owner, AiSchedulerRules.Pc, StringComparison.Ordinal) && !AiExecutorRules.IsOverlayRunGoal(r.Bottom.Goal)).GroupBy(r => HeroRunKind(r.Bottom.Goal)).Any(g => g.Count() > 1)) return GameSessionFailure.InvalidSnapshot;
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

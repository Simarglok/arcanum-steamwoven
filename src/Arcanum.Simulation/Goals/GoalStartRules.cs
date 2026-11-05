using System.Collections.Immutable;
using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Goals;

/// <summary>The starter families of the original that differ in how they interrupt (ledger GS12, GS13), for the gate port to tell apart.</summary>
public enum GoalStarterKind
{
    /// <summary>The walk starter 0x433640 (goal 3): level 3, the movement gate.</summary>
    Walk,
    /// <summary>The run starter 0x433C80 (goal 4): level 3, the movement gate.</summary>
    Run,
    /// <summary>The follow starter 0x4346A0 (goal 24): level 3, refuses a duplicate.</summary>
    Follow,
    /// <summary>The attack starter 0x434B00 (goal 18): level 3, refuses a duplicate.</summary>
    Attack,
    /// <summary>The throw starter 0x434F80 (goal 37): level 3, refuses a duplicate.</summary>
    Throw,
    /// <summary>The spread-out starter 0x436960 (goal 69): level 3, refuses a duplicate.</summary>
    SpreadOut,
    /// <summary>The stun starter 0x4357B0 (goal 57): level 4, then the goal's own priority 1.</summary>
    Stun,
    /// <summary>The projectile starter 0x435870 (goal 36): level 4, then the goal's own priority 5.</summary>
    Projectile,
    /// <summary>The knock-down starter 0x435A90 (goal 62): level 5, then the goal's own priority 3.</summary>
    KnockDown,
    /// <summary>Any other start through 0x44D4E0: it interrupts at the goal's own priority (0 for a no-interrupt goal) and refuses when that fails.</summary>
    Generic,
    /// <summary>A start through 0x44D500: it interrupts nothing (the friendly cast 73, the secondary cast 84, the destroy goal 32, the spell goal component).</summary>
    NoInterrupt,
}

/// <summary>
/// How a starter interrupts before it starts (ledger GS12, GS13). The interrupts run in order, each over every run of the owner (<see cref="GoalRunTable.InterruptOwner"/>), and stop at
/// the first refusal. <see cref="FixedLevels"/> come first; <see cref="InterruptsAtOwnPriority"/> adds the goal's own priority as the last level (0 for a no-interrupt goal).
/// </summary>
/// <param name="Kind">The starter family.</param>
/// <param name="FixedLevels">The fixed levels the starter interrupts at itself: [3] for the walks, runs, follow, attack, throw and spread out; [4] for the stun and projectile; [5] for the knock-down.</param>
/// <param name="InterruptsAtOwnPriority">The start goes through 0x44D4E0, which interrupts again at the new goal's priority (stun 1, projectile 5, knock-down 3, any generic start its own).</param>
/// <param name="RefusesDuplicate">An equal goal already queued refuses the start before anything is interrupted (attack, throw, follow, spread out).</param>
/// <param name="StartsOnRefusal">A refused interrupt does not refuse the start: the new run coexists with the one that refused (the variable-goal node 0x424710, the fidget event 0x4300D0, the fire loop 0x433580).</param>
public sealed record GoalStartPlan(GoalStarterKind Kind, ImmutableArray<int> FixedLevels, bool InterruptsAtOwnPriority, bool RefusesDuplicate, bool StartsOnRefusal)
{
    /// <summary>The levels this plan interrupts at, in order, for a goal with <paramref name="descriptor"/>.</summary>
    public ImmutableArray<int> LevelsFor(GoalDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var fixedLevels = FixedLevels.IsDefault ? [] : FixedLevels;
        return InterruptsAtOwnPriority ? fixedLevels.Add(descriptor.NoInterrupt ? 0 : descriptor.Priority) : fixedLevels;
    }
}

/// <summary>The starters' interrupt plans (ledger GS12, GS13, GS30, GS31, read again in the S2 slice), by the starters' addresses in the retail 1.0.7.4 executable.</summary>
public static class GoalStarters
{
    private const int Ordinary = GoalInterruptRules.OrdinaryActions;

    public static GoalStartPlan Walk { get; } = new(GoalStarterKind.Walk, [Ordinary], false, false, false);
    public static GoalStartPlan Run { get; } = new(GoalStarterKind.Run, [Ordinary], false, false, false);
    public static GoalStartPlan Follow { get; } = new(GoalStarterKind.Follow, [Ordinary], false, true, false);
    public static GoalStartPlan Attack { get; } = new(GoalStarterKind.Attack, [Ordinary], false, true, false);
    public static GoalStartPlan Throw { get; } = new(GoalStarterKind.Throw, [Ordinary], false, true, false);
    public static GoalStartPlan SpreadOut { get; } = new(GoalStarterKind.SpreadOut, [Ordinary], false, true, false);
    public static GoalStartPlan Stun { get; } = new(GoalStarterKind.Stun, [4], true, false, false);
    public static GoalStartPlan Projectile { get; } = new(GoalStarterKind.Projectile, [4], true, false, false);
    public static GoalStartPlan KnockDown { get; } = new(GoalStarterKind.KnockDown, [5], true, false, false);
    public static GoalStartPlan Generic { get; } = new(GoalStarterKind.Generic, [], true, false, false);
    /// <summary>A generic start whose refused interrupt does not refuse it (three sites of the original).</summary>
    public static GoalStartPlan GenericStartsAnyway { get; } = new(GoalStarterKind.Generic, [], true, false, true);
    public static GoalStartPlan NoInterrupt { get; } = new(GoalStarterKind.NoInterrupt, [], false, false, false);
}

/// <summary>A request to start a run of <paramref name="Owner"/> whose bottom goal is <paramref name="Frame"/>.</summary>
/// <param name="Owner">The object's session entity string.</param>
/// <param name="Plan">The starter's interrupt plan.</param>
/// <param name="Frame">The bottom goal with its typed parameters.</param>
/// <param name="Flags">The run flags the starter sets (running, flight, near-occupied...).</param>
/// <param name="DueMs">The animation-clock time of the run's first step (5 ms after the start, or 1 ms in a round for an owner that is not the current participant: S3).</param>
/// <param name="PaceMs">The run's pace.</param>
public sealed record GoalStartRequest(string Owner, GoalStartPlan Plan, GoalFrame Frame, GoalRunFlags Flags = GoalRunFlags.None, long DueMs = 0, int PaceMs = GoalRun.DefaultPaceMs);

/// <summary>
/// What a start needs from the world (ledger GS11, GS15): the gates and the two states that refuse a start. The rules cannot decide them: the start gate (action
/// points and fatigue of an NPC in a round, 0x4348E0) and the movement gate (the object able, the current participant, not an NPC in a reaction, 0x4339A0) read
/// the combat and critter state of the host.
/// </summary>
public interface IGoalStartPorts
{
    /// <summary>Whether the starter's gate passes for this request (a starter with no gate answers true).</summary>
    bool GatePasses(GoalStartRequest request);
    /// <summary>The animation module is loading (the original's flag 0x5E34FC): nothing starts.</summary>
    bool Loading { get; }
    /// <summary>The owner is destroyed: nothing starts for it.</summary>
    bool IsDestroyed(string owner);
}

/// <summary>Why a start did what it did.</summary>
public enum GoalStartOutcome
{
    /// <summary>The run was allocated; the handle is in the result.</summary>
    Started,
    /// <summary>The gate refused (nothing was interrupted).</summary>
    Gate,
    /// <summary>An equal goal is already queued (nothing was interrupted).</summary>
    Duplicate,
    /// <summary>An interrupt was refused; the runs before the refusal in slot order may already have ended (GS17).</summary>
    Interrupt,
    /// <summary>The animation module is loading; the interrupts have already run (GS11).</summary>
    Loading,
    /// <summary>The owner is destroyed; the interrupts have already run (GS11).</summary>
    Destroyed,
    /// <summary>No free slot: the table's exhaustion flag is set (GS2).</summary>
    TableFull,
    /// <summary>The goal has no descriptor (13, 22): the original has no way to start one (it reads the descriptor first).</summary>
    NoDescriptor,
}

/// <summary>
/// The table after a start, its outcome, the new run's handle and the runs the start's interrupts ended (their end nodes and events are for the caller to settle; a refused start can
/// still have ended runs).
/// </summary>
public sealed record GoalStartResult(GoalRunTable Table, GoalStartOutcome Outcome, GoalHandle? Handle, ImmutableArray<GoalRunEnd> Ended);

/// <summary>
/// The start of a goal run as pure rules (ARC-DEV-131, ledger GS11..GS15, goal-table.md §3.3): the order is gate, duplicate, interrupts, then the start's own refusals,
/// because that is the order of the original's starters (the gate first, the duplicate test before any interrupt, the interrupts in the starter, the loading and
/// destroyed tests inside the run start 0x44D730). The start itself compares no priority; replacement is decided by the interrupts alone.
/// </summary>
public static class GoalStartRules
{
    /// <summary>
    /// Starts a run (0x44D730 behind the starters' gates, duplicate tests and interrupts, 0x44D4E0 and 0x44D500). Nothing is changed by a refusal before the interrupts; a
    /// refusal after them keeps the runs they ended. The first frame's time and the events are S3's.
    /// </summary>
    [OriginalFunction(0x44D730, OriginalFunctionScope.Partial)]
    public static GoalStartResult Start(GoalRunTable table, GoalStartRequest request, IGoalStartPorts ports)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ports);
        var descriptor = GoalDescriptors.Of(request.Frame.Goal);
        if (descriptor is null) return new(table, GoalStartOutcome.NoDescriptor, null, []);
        if (!ports.GatePasses(request)) return new(table, GoalStartOutcome.Gate, null, []);
        if (request.Plan.RefusesDuplicate && table.EqualGoalQueued(request.Owner, request.Frame) is not null)
            return new(table, GoalStartOutcome.Duplicate, null, []);

        var ended = ImmutableArray.CreateBuilder<GoalRunEnd>();
        foreach (var level in request.Plan.LevelsFor(descriptor))
        {
            var interrupted = table.InterruptOwner(request.Owner, level);
            table = interrupted.Table;
            ended.AddRange(interrupted.Ended);
            if (interrupted.Succeeded) continue;
            if (!request.Plan.StartsOnRefusal) return new(table, GoalStartOutcome.Interrupt, null, ended.ToImmutable());
            break;
        }
        if (ports.Loading) return new(table, GoalStartOutcome.Loading, null, ended.ToImmutable());
        if (ports.IsDestroyed(request.Owner)) return new(table, GoalStartOutcome.Destroyed, null, ended.ToImmutable());
        var allocation = table.Allocate(request.Owner, request.Frame, request.Flags, request.DueMs, request.PaceMs);
        return allocation.Handle is { } handle
            ? new(allocation.Table, GoalStartOutcome.Started, handle, ended.ToImmutable())
            : new(allocation.Table, GoalStartOutcome.TableFull, null, ended.ToImmutable());
    }

    /// <summary>
    /// The first step of the main walk and run starters (0x433640, ledger GS14): when the owner's first run's bottom goal is <paramref name="walkGoal"/> or one of its aliases, the
    /// same destination is accepted with no change and a new one is written over the destination in place (the run re-plans); with no such run the caller starts one
    /// (<see cref="GoalRetargetOutcome.NoRun"/>); a run whose bottom goal is no tile walk is refused (<see cref="GoalRetargetOutcome.NotAWalk"/>). The move-near starter 0x4341C0
    /// does the same (F1 in GS14, not read again here). The starter's other branches (the gate, the always-run conversion, the network) are the caller's, and so is the order:
    /// the original runs the movement gate (0x4339A0) <b>before</b> this retarget, so a caller passes the gate first and calls this only when it passed; unlike <see cref="Start"/>
    /// this function takes no gate port (S10 builds the walk request on it).
    /// </summary>
    [OriginalFunction(0x433640, OriginalFunctionScope.Partial)]
    public static GoalRetargetResult RetargetWalk(GoalRunTable table, string owner, GoalType walkGoal, CellCoord destination)
    {
        ArgumentNullException.ThrowIfNull(table);
        return table.RunRunningGoal(owner, walkGoal) is { } run ? table.RetargetDestination(run.Handle, destination) : new(table, GoalRetargetOutcome.NoRun, null);
    }
}

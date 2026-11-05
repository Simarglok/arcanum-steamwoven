using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Goals;

/// <summary>The table after an allocation: the new run's handle, or none when all 216 slots are in use (the table then holds the exhaustion flag, GS2).</summary>
public sealed record GoalAllocation(GoalRunTable Table, GoalHandle? Handle);

/// <summary>The table after freeing a slot: the run that was freed (null when the handle matched none) and the significant-goal count after it; with a count of zero the round's "all animations done" callback is due (GS23).</summary>
public sealed record GoalFreeResult(GoalRunTable Table, GoalRun? Run, int SignificantGoalsLeft)
{
    public bool AnimationsDone => Run is not null && SignificantGoalsLeft == 0;
}

/// <summary>What <see cref="GoalRunTable.InsertAtBottom"/> did.</summary>
public enum GoalInsertOutcome
{
    Inserted,
    /// <summary>The handle matches no run.</summary>
    NoRun,
    /// <summary>The run already holds <see cref="GoalRun.MaxFrames"/> goals. The original writes a ninth frame past the stack there (GS14, Q8); Steamwoven refuses.</summary>
    StackFull,
    /// <summary>The goal has no descriptor (13, 22).</summary>
    NoDescriptor,
}

/// <summary>The table after an insert at the bottom of a run's stack.</summary>
public sealed record GoalInsertResult(GoalRunTable Table, GoalInsertOutcome Outcome);

/// <summary>What <see cref="GoalRunTable.RetargetDestination"/> did.</summary>
public enum GoalRetargetOutcome
{
    /// <summary>The handle matches no run.</summary>
    NoRun,
    /// <summary>The run's bottom goal is no tile walk (3, 4, 7 or 55), so it has no destination to overwrite: refused with no change.</summary>
    NotAWalk,
    /// <summary>The run already walks to that destination: accepted with no change.</summary>
    Unchanged,
    /// <summary>The bottom goal's destination was overwritten and the path flag "replan" set.</summary>
    Retargeted,
}

/// <summary>The table after a retarget in place.</summary>
public sealed record GoalRetargetResult(GoalRunTable Table, GoalRetargetOutcome Outcome, GoalHandle? Handle);

/// <summary>
/// The run table of the retail 1.0.7.4 executable as a pure value (ARC-DEV-131, MIG13 S2, docs/architecture/goal-table.md §3.1, ledger GS1..GS4, GS14,
/// GS16..GS21): 216 slots, each free or holding one <see cref="GoalRun"/>; handles of a slot and a run id; the lowest free slot is allocated; a freed
/// slot is empty, so an old handle never matches; the queries read the set of runs the original's helpers read; the interrupt rule, the per-owner
/// interrupt, the sweeps and the kill are in <c>GoalRunTable.Interrupts.cs</c>. Every change returns a new table with its result, in the style of
/// the other rule sets. The session owns the one instance in slice S3; nothing calls this type yet.
/// <para>
/// Differences from the original (goal-table.md §4): run ids start at 1 (the original seeds its counter randomly), the table holds no empty runs
/// (a run always has a goal), and the significant-goal count is derived from the runs instead of kept as a counter.
/// </para>
/// </summary>
public sealed partial class GoalRunTable
{
    /// <summary>The number of run slots (GS1).</summary>
    public const int SlotCount = 216;
    /// <summary>The goals a run's stack holds.</summary>
    public const int MaxFrames = GoalRun.MaxFrames;

    private readonly ImmutableArray<GoalRun?> slots;

    private GoalRunTable(ImmutableArray<GoalRun?> slots, int nextRunId, int inUseCount, bool exhaustionPending, int? steppingSlot)
    {
        this.slots = slots; NextRunId = nextRunId; InUseCount = inUseCount; ExhaustionPending = exhaustionPending; SteppingSlot = steppingSlot;
    }

    /// <summary>A table with every slot free.</summary>
    public static GoalRunTable Empty { get; } = new(ImmutableArray.Create(new GoalRun?[SlotCount]), 1, 0, false, null);

    /// <summary>The run id the next allocation gets (+1 per allocation; the original seeds its counter randomly, Steamwoven at 1 for determinism).</summary>
    public int NextRunId { get; }
    /// <summary>The slots in use (the original's in-use count 0x5E3500).</summary>
    public int InUseCount { get; }
    /// <summary>A start found no free slot (the original's flag 0x5E34F4, ledger GS2): the next step of any run interrupts every run at level 3 (<see cref="SweepIfExhausted"/>) and clears it.</summary>
    public bool ExhaustionPending { get; }
    /// <summary>The slot of the run the stepper is stepping, if any (the original's marker 0x5A5978): an interrupt of that run only marks it done, the stepper ends it (GS16).</summary>
    public int? SteppingSlot { get; }

    /// <summary>The run in a slot (null when it is free); a slot outside 0..<see cref="SlotCount"/> is an argument error.</summary>
    public GoalRun? this[int slot] => slot is >= 0 and < SlotCount ? slots[slot] : throw new ArgumentOutOfRangeException(nameof(slot));

    /// <summary>Whether <paramref name="handle"/> names a run that is in the table: its slot holds a run with the same id (the original's lookup compares the id and, in single player, the slot).</summary>
    public bool TryGet(GoalHandle handle, [NotNullWhen(true)] out GoalRun? run)
    {
        run = null;
        if (handle.Slot is < 0 or >= SlotCount) return false;
        var candidate = slots[handle.Slot];
        if (candidate is null || candidate.Handle.RunId != handle.RunId) return false;
        run = candidate;
        return true;
    }

    /// <summary>The table with the stepper's marker set to <paramref name="slot"/> (or cleared). The marker is a plain slot number, as in the original: a free slot is accepted, an interrupt only matches it against the run it ends.</summary>
    public GoalRunTable WithStepping(int? slot)
    {
        if (slot is < 0 or >= SlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
        return new(slots, NextRunId, InUseCount, ExhaustionPending, slot);
    }

    // ---- allocation, free, replace ----

    /// <summary>
    /// Allocates the lowest free slot for a run of <paramref name="owner"/> whose bottom goal is <paramref name="bottom"/> (0x44CCB0): the handle is the slot and
    /// the next run id; the run is in use with the frame as goal 0. With no free slot nothing is queued and the table's exhaustion flag is set (GS2).
    /// The bottom goal must have a descriptor (<see cref="GoalStartRules.Start"/> refuses one that has none before it gets here).
    /// </summary>
    [OriginalFunction(0x44CCB0, OriginalFunctionScope.Partial)]
    public GoalAllocation Allocate(string owner, GoalFrame bottom, GoalRunFlags flags = GoalRunFlags.None, long dueMs = 0, int paceMs = GoalRun.DefaultPaceMs)
    {
        ArgumentException.ThrowIfNullOrEmpty(owner);
        ArgumentNullException.ThrowIfNull(bottom);
        if (!GoalDescriptors.Has(bottom.Goal)) throw new ArgumentException($"goal {(int)bottom.Goal} has no descriptor", nameof(bottom));
        var free = FirstFreeSlot();
        if (free < 0) return new(new(slots, NextRunId, InUseCount, true, SteppingSlot), null);
        var handle = new GoalHandle(free, NextRunId);
        var run = new GoalRun(owner, handle, bottom, flags, dueMs, paceMs);
        return new(new(slots.SetItem(free, run), NextRunId == int.MaxValue ? 1 : NextRunId + 1, InUseCount + 1, ExhaustionPending, SteppingSlot), handle);
    }

    /// <summary>
    /// Frees a run's slot (0x44CF20): the slot is empty again and the result carries the run and the significant-goal count after it (zero means the round's
    /// "all animations done" callback is called, GS23). The run's events, the hero's attack target in the interface (bottom goal 18 or 19) and the end nodes are the caller's.
    /// </summary>
    [OriginalFunction(0x44CF20, OriginalFunctionScope.Partial)]
    public GoalFreeResult Free(GoalHandle handle)
    {
        if (!TryGet(handle, out var run)) return new(this, null, SignificantGoalCount);
        var table = new GoalRunTable(slots.SetItem(handle.Slot, null), NextRunId, InUseCount - 1, ExhaustionPending, SteppingSlot == handle.Slot ? null : SteppingSlot);
        return new(table, run, table.SignificantGoalCount);
    }

    /// <summary>Stores <paramref name="run"/> in its slot (the stepper's result of a step): the handle and the owner must be those of the run in the table.</summary>
    public bool TryReplace(GoalRun run, [NotNullWhen(true)] out GoalRunTable? table)
    {
        ArgumentNullException.ThrowIfNull(run);
        table = null;
        if (!TryGet(run.Handle, out var current) || !string.Equals(current.Owner, run.Owner, StringComparison.Ordinal)) return false;
        table = WithRun(run);
        return true;
    }

    // ---- changes of a run's stack (GS14) ----

    /// <summary>
    /// Inserts <paramref name="frame"/> at the bottom of a run's stack (0x44D9B0, ledger GS14): every goal moves up one place and the new one becomes goal 0, so it runs
    /// after the current goals have popped. It serves the hero's repeated attack and the casting run's queued cast. No event is scheduled. A run that already holds
    /// eight goals refuses (<see cref="GoalInsertOutcome.StackFull"/>).
    /// </summary>
    public GoalInsertResult InsertAtBottom(GoalHandle handle, GoalFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!TryGet(handle, out var run)) return new(this, GoalInsertOutcome.NoRun);
        if (!GoalDescriptors.Has(frame.Goal)) return new(this, GoalInsertOutcome.NoDescriptor);
        if (run.Depth >= MaxFrames) return new(this, GoalInsertOutcome.StackFull);
        return new(WithRun(run.WithFrames(run.Frames.Insert(0, frame))), GoalInsertOutcome.Inserted);
    }

    /// <summary>
    /// Changes a running walk's destination in place (0x433640, ledger GS14): the same destination is accepted with no change; a new one overwrites the bottom goal's
    /// destination and sets the run's path flag "replan", so the walk re-plans without a new run. Only a run whose bottom goal walks to a tile (move to tile 3, run to tile 4,
    /// move near tile 7, run near tile 55) has a destination to change; any other run is refused (<see cref="GoalRetargetOutcome.NotAWalk"/>). The movement gate that precedes
    /// the retarget in the original is the caller's (<see cref="GoalStartRules.RetargetWalk"/>).
    /// </summary>
    public GoalRetargetResult RetargetDestination(GoalHandle handle, CellCoord destination)
    {
        if (!TryGet(handle, out var run)) return new(this, GoalRetargetOutcome.NoRun, null);
        if (run.Bottom.Goal is not (GoalType.MoveToTile or GoalType.RunToTile or GoalType.MoveNearTile or GoalType.RunNearTile))
            return new(this, GoalRetargetOutcome.NotAWalk, handle);
        if (run.Bottom.Tile == destination) return new(this, GoalRetargetOutcome.Unchanged, handle);
        var changed = run.WithFrames(run.Frames.SetItem(0, run.Bottom with { Tile = destination })) with { PathFlags = run.PathFlags | GoalPathFlags.Replan };
        return new(WithRun(changed), GoalRetargetOutcome.Retargeted, handle);
    }

    // ---- queries (GS4) ----

    /// <summary>
    /// The runs of <paramref name="owner"/> in slot order (0x44D2F0 and its next-run partner 0x44D340): a run that is free, done or empty is skipped. This is the set
    /// every query below reads, except that "runs goal N" and the idle test look at the first run only.
    /// </summary>
    [OriginalFunction(0x44D2F0, OriginalFunctionScope.Partial)]
    public ImmutableArray<GoalRun> RunsOf(string owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var runs = ImmutableArray.CreateBuilder<GoalRun>();
        foreach (var run in slots)
            if (run is { Done: false } && string.Equals(run.Owner, owner, StringComparison.Ordinal)) runs.Add(run);
        return runs.ToImmutable();
    }

    /// <summary>The owner's first (lowest-slot) run, or null.</summary>
    public GoalRun? FirstRunOf(string owner) => RunsOf(owner) is { Length: > 0 } runs ? runs[0] : null;

    /// <summary>
    /// The owner's main run (0x423300): the first run whose bottom goal lacks the no-interrupt flag. The runs beside it are overlays.
    /// </summary>
    [OriginalFunction(0x423300, OriginalFunctionScope.Full)]
    public GoalRun? MainRunOf(string owner)
    {
        foreach (var run in RunsOf(owner))
            if (GoalDescriptors.Of(run.Bottom.Goal) is not { NoInterrupt: true }) return run;
        return null;
    }

    /// <summary>The priority of the main run's bottom goal, 0 when the owner has no main run (0x4233D0; the AI pre-check decides for an NPC only while it is 2 or less, GS20).</summary>
    [OriginalFunction(0x4233D0, OriginalFunctionScope.Full)]
    public int MainRunPriority(string owner) =>
        MainRunOf(owner) is { } run ? GoalDescriptors.Of(run.Bottom.Goal)?.Priority ?? 0 : 0;

    /// <summary>
    /// "Runs goal N" (0x44E830): the owner's <b>first</b> run when its bottom goal is <paramref name="goal"/> or one of its aliases, else null. Because an overlay takes any
    /// free slot, an eye-candy run in a lower slot than a critter's walk hides the walk from this query (the first-run shadowing of GS4).
    /// </summary>
    [OriginalFunction(0x44E830, OriginalFunctionScope.Full)]
    public GoalRun? RunRunningGoal(string owner, GoalType goal) =>
        FirstRunOf(owner) is { } first && GoalDescriptors.IsSameActivity(goal, first.Bottom.Goal) ? first : null;

    /// <summary>
    /// "An equal goal is queued" (0x44E710, the duplicate test of the attack, throw, follow and spread-out starters): the first of the owner's runs, in slot order,
    /// whose bottom goal is the frame's goal or one of its aliases and whose object parameters are the same. The original compares the five object parameters; the
    /// typed frame holds the target and the item (the owner is the run's own). The starters ask through the forwarder 0x44E6F0, which calls this function with no
    /// out-handle and so only learns whether such a run exists; this method serves both forms (it returns the run).
    /// </summary>
    [OriginalFunction(0x44E710, OriginalFunctionScope.Partial)]
    public GoalRun? EqualGoalQueued(string owner, GoalFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        foreach (var run in RunsOf(owner))
            if (GoalDescriptors.IsSameActivity(frame.Goal, run.Bottom.Goal)
                && string.Equals(run.Bottom.Target, frame.Target, StringComparison.Ordinal)
                && string.Equals(run.Bottom.Item, frame.Item, StringComparison.Ordinal)) return run;
        return null;
    }

    /// <summary>"A run whose bottom goal is N" (0x44E650): the first run, in slot order, whose bottom goal is exactly <paramref name="goal"/> (no aliases).</summary>
    [OriginalFunction(0x44E650, OriginalFunctionScope.Partial)]
    public GoalRun? RunWithBottomGoal(string owner, GoalType goal)
    {
        foreach (var run in RunsOf(owner))
            if (run.Bottom.Goal == goal) return run;
        return null;
    }

    /// <summary>"A busy run" (0x44E8C0): the first run whose bottom goal lacks the carry flag (the please-move and step-apart starters ask a critter that has none to move).</summary>
    [OriginalFunction(0x44E8C0, OriginalFunctionScope.Full)]
    public GoalRun? BusyRun(string owner)
    {
        foreach (var run in RunsOf(owner))
            if (GoalDescriptors.Of(run.Bottom.Goal) is not { Carried: true }) return run;
        return null;
    }

    /// <summary>
    /// "The first run attacks" (0x44E940): the owner's first run has the attack goal (18 or its alias 19) as its bottom goal and attacks <paramref name="target"/>. A null
    /// target matches any attack. The two targets are compared through their leader (<paramref name="leaderOf"/>, the original's 0x45DDA0: an object's leader, else itself).
    /// The original's party-wide branch (a target that is a player character matches when the run's target is one of its group) is not modelled.
    /// </summary>
    [OriginalFunction(0x44E940, OriginalFunctionScope.Partial)]
    public GoalRun? FirstRunAttacks(string owner, string? target, Func<string, string?>? leaderOf = null)
    {
        if (FirstRunOf(owner) is not { } first || !GoalDescriptors.IsSameActivity(GoalType.Attack, first.Bottom.Goal)) return null;
        if (target is null) return first;
        var lead = leaderOf ?? (_ => null);
        var runTarget = first.Bottom.Target;
        if (runTarget is null) return null;
        return string.Equals(lead(target) ?? target, lead(runTarget) ?? runTarget, StringComparison.Ordinal) ? first : null;
    }

    /// <summary>
    /// The idle test (0x423470) on the owner's <b>first</b> run: a stack of one goal whose goal is animate (0), animate loop (1) or fidget (2), and the object's art
    /// is in animation 0 (<paramref name="artAnimationIsIdle"/>, the original's 0x503E20 of the art id, the caller's). An owner with no run is not idle (the original reads
    /// memory before the table there; not shown to be reachable).
    /// </summary>
    [OriginalFunction(0x423470, OriginalFunctionScope.Partial)]
    public bool IsIdle(string owner, bool artAnimationIsIdle) =>
        FirstRunOf(owner) is { Depth: 1 } run && run.Top.Goal is GoalType.Animate or GoalType.AnimateLoop or GoalType.Fidget && artAnimationIsIdle;

    /// <summary>
    /// The significant goals of the round (TL39): the goals on the stacks of runs whose current goal is not "not counted", of priority 2 or more and no no-interrupt
    /// flag. The round's "all animations done" callback is due when a slot is freed with this at zero. Derived from the runs; the original keeps a counter (<see cref="GoalDescriptors.IsSignificant"/>).
    /// </summary>
    public int SignificantGoalCount
    {
        get
        {
            var count = 0;
            foreach (var run in slots)
            {
                if (run is null || run.CurrentGoalNotCounted()) continue;
                foreach (var frame in run.Frames)
                    if (GoalDescriptors.Of(frame.Goal) is { } descriptor && GoalDescriptors.IsSignificant(descriptor)) count++;
            }
            return count;
        }
    }

    /// <summary>Every run in the table, in slot order (done runs included).</summary>
    public IEnumerable<GoalRun> Runs => slots.Where(r => r is not null).Select(r => r!);

    private int FirstFreeSlot()
    {
        for (var i = 0; i < slots.Length; i++)
            if (slots[i] is null) return i;
        return -1;
    }

    private GoalRunTable WithRun(GoalRun run) => new(slots.SetItem(run.Handle.Slot, run), NextRunId, InUseCount, ExhaustionPending, SteppingSlot);

    private GoalRunTable WithExhaustionCleared() => new(slots, NextRunId, InUseCount, false, SteppingSlot);
}

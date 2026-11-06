using System.Collections.Immutable;
using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Goals;

/// <summary>Flags of a goal frame (the original's frame flags, the integer parameter at +0x70, ledger GS5).</summary>
[Flags]
public enum GoalFrameFlags
{
    None = 0,
    /// <summary>0x80: the goal is "not counted" (TL39): the run's current goal being so keeps the run out of the significant-goal count and the round-start sweep.</summary>
    NotCounted = 0x80,
    /// <summary>0x1000: set by the follow starter.</summary>
    Follow = 0x1000,
    /// <summary>0x10 of the original's run flags, kept on the frame (ARC-DEV-137): the goal's animation is playing, so its first node steps a frame instead of starting it.</summary>
    Playing = 0x10,
}

/// <summary>
/// One goal of a run's stack, typed (ARC-DEV-131, docs/architecture/goal-table.md §3.1): the original's 21 parameter slots become the fields the
/// goals read. Every field is a plain value (the object parameters are session entity strings, the locations cells) so a save can hold a stack
/// unchanged (MIG14). It widened the NPC executor's former <c>AiGoalFrame</c> with <see cref="Flags"/>, and S4 (ARC-DEV-134) replaced that record by this one.
/// </summary>
public sealed record GoalFrame
{
    private readonly GoalType goal;
    private readonly int range;
    private readonly ImmutableArray<CellCoord> path = [];

    public GoalFrame(GoalType goal, string? target = null, CellCoord? tile = null, int range = 0, int? sound = null)
    {
        if (!GoalTypes.IsDefined((int)goal)) throw new ArgumentOutOfRangeException(nameof(goal));
        if (range < 0) throw new ArgumentOutOfRangeException(nameof(range));
        Goal = goal; Target = target; Tile = tile; Range = range; Sound = sound;
    }

    public GoalType Goal
    {
        get => goal;
        init => goal = GoalTypes.IsDefined((int)value) ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }
    /// <summary>The node the goal continues at; a goal starts and restarts at 1.</summary>
    public int Node { get; init; } = 1;
    /// <summary>The goal's object (object parameter 1): the attack's target, the flight's source, the follower's leader.</summary>
    public string? Target { get; init; }
    /// <summary>The goal's item (an object parameter): the item of an item use, a skill's tool, a throw or a pickup.</summary>
    public string? Item { get; init; }
    /// <summary>The goal's destination location (parameter 5): the walk's and the run's destination tile.</summary>
    public CellCoord? Tile { get; init; }
    /// <summary>The goal's range in tiles (the attack's approach 1, the flight's 9, the follower's 2 or 5).</summary>
    public int Range
    {
        get => range;
        init => range = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }
    /// <summary>The attack's alerted sound, played once when the approach starts moving and then cleared.</summary>
    public int? Sound { get; init; }
    /// <summary>The skill of a skill goal (the original's skill numbers: 5 pickpocket, 10 heal, 14 pick locks).</summary>
    public int? Skill { get; init; }
    /// <summary>The magic run a casting goal plays (the cast host's run slot).</summary>
    public int? SpellRun { get; init; }
    /// <summary>The walk goals' planned segment, else empty.</summary>
    public ImmutableArray<CellCoord> Path
    {
        get => path;
        init => path = value.IsDefault ? [] : value;
    }
    /// <summary>The next step of <see cref="Path"/>.</summary>
    public int PathIndex { get; init; }
    /// <summary>The target's tile when the segment was planned (the approach walks stop when it changes).</summary>
    public CellCoord? PlannedTargetTile { get; init; }
    /// <summary>The wait from the action frame to the animation's end, in game milliseconds (also the action goals' rest after their action frame).</summary>
    public int RestMs { get; init; }
    /// <summary>The frame flags (<see cref="GoalFrameFlags"/>).</summary>
    public GoalFrameFlags Flags { get; init; }
    /// <summary>ARC-DEV-137 (MIG13 S6): the animation a critter-state goal plays (parameter slot 8 or 14: the stun's 23, the fall's 7, the get-up's 8, the kill's dying animation 7, 17..19), 0 when none.</summary>
    public int Animation { get; init; }
    /// <summary>ARC-DEV-137: the frame of <see cref="Animation"/> the critter shows now (the animation step's own counter).</summary>
    public int FrameIndex { get; init; }
    /// <summary>ARC-DEV-137: the stun's countdown (parameter slot 18): how many plays or skipped turns the stun still lasts.</summary>
    public int Count { get; init; }
    /// <summary>ARC-DEV-139 (MIG13 S8): the straight-flight goals' path flag (the run's +0xC00 bit 0x1, GT58): set, the goal's next node 2 builds its line (the original sets it at the table's init, on every free and
    /// when a retarget writes a new destination); the line builder clears it. A goal 28 frame is built with it set.</summary>
    public bool PathPending { get; init; }
    /// <summary>ARC-DEV-142 (MIG13 S9a): the scenery loop's last distance answer (the run's slot 17, GT75): the player character was within 30 tiles when the loop last measured.</summary>
    public bool Near { get; init; }
}

/// <summary>The run flags the table models (the original's run flags at +0xC, ledger GS6); the numbers are the original's. In use is a slot being filled and done is <see cref="GoalRun.Done"/>.</summary>
[Flags]
public enum GoalRunFlags
{
    None = 0,
    /// <summary>0x4: the action frame was reached.</summary>
    ActionFrameReached = 0x4,
    /// <summary>0x8: the action frame was handled.</summary>
    ActionFrameHandled = 0x8,
    /// <summary>0x40: the run walks running.</summary>
    Running = 0x40,
    /// <summary>0x100: set after a click with Shift; the walk step then answers 0 at once, so the run does not walk ("act from where you stand", inference).</summary>
    NoWalk = 0x100,
    /// <summary>0x400: a flight: path requests relax their mask and the move-away nodes test it.</summary>
    Flight = 0x400,
    /// <summary>0x4000: the approach may end beside an occupied tile.</summary>
    NearOccupied = 0x4000,
    /// <summary>0x8000: the run was killed (GS21): its end nodes do nothing.</summary>
    Killed = 0x8000,
}

/// <summary>The run's path-block flags (+0xC00).</summary>
[Flags]
public enum GoalPathFlags
{
    None = 0,
    /// <summary>0x4: the walk re-plans its path (set by a retarget in place, GS14).</summary>
    Replan = 0x4,
}

/// <summary>A run's handle: its slot and its run id (the original's third word, the change counter, is 0 in single player and not kept). A freed slot's id is cleared, so an old handle never matches (GS1).</summary>
public readonly record struct GoalHandle(int Slot, int RunId);

/// <summary>
/// One goal run: the original's run slot (ledger GS1) as an immutable value (ARC-DEV-131). It belongs to an <see cref="Owner"/> (the object's session
/// entity string; a run never holds a reference) and holds a stack of at most <see cref="MaxFrames"/> goal frames, bottom first. A run
/// always has a goal (the original's empty run is never kept: popping the bottom goal ends the run). The table stores it; the stepper returns the next one.
/// </summary>
public sealed record GoalRun
{
    /// <summary>The goal stack's depth limit (GS1, GS5).</summary>
    public const int MaxFrames = 8;
    /// <summary>The pace of a run whose art has no frame rate: 100 ms (the original's default, GS1).</summary>
    public const int DefaultPaceMs = 100;

    public GoalRun(string owner, GoalHandle handle, GoalFrame bottom, GoalRunFlags flags = GoalRunFlags.None, long dueMs = 0, int paceMs = DefaultPaceMs)
    {
        ArgumentException.ThrowIfNullOrEmpty(owner);
        ArgumentNullException.ThrowIfNull(bottom);
        if (!GoalDescriptors.Has(bottom.Goal)) throw new ArgumentException($"goal {(int)bottom.Goal} has no descriptor", nameof(bottom));
        ArgumentOutOfRangeException.ThrowIfNegative(paceMs);
        Owner = owner; Handle = handle; Frames = [bottom]; Flags = flags; DueMs = dueMs; PaceMs = paceMs;
    }

    public string Owner { get; }
    public GoalHandle Handle { get; }
    /// <summary>The goal stack, bottom first: 1..<see cref="MaxFrames"/> goals.</summary>
    public ImmutableArray<GoalFrame> Frames { get; private init; }
    public GoalRunFlags Flags { get; init; }
    public GoalPathFlags PathFlags { get; init; }
    /// <summary>The run is done (run flag 0x2): an interrupt of the stepped run marks it and the stepper ends it; every query skips a done run.</summary>
    public bool Done { get; init; }
    /// <summary>The animation-clock time of the run's next step, in milliseconds.</summary>
    public long DueMs { get; init; }
    /// <summary>The run's pace in milliseconds (1000 / the art's frame rate, 100 without one).</summary>
    public int PaceMs { get; init; }

    /// <summary>The bottom goal: the one the run was started with (the main-run test, the aliases and the carry read it).</summary>
    public GoalFrame Bottom => Frames[0];
    /// <summary>The current goal: the top of the stack (the interrupt rule and the idle test read it).</summary>
    public GoalFrame Top => Frames[^1];
    public int Depth => Frames.Length;

    /// <summary>
    /// The run with another goal stack (the stepper's push and pop): 1..8 goals, each with a descriptor. The original reads the descriptor of the current goal in every
    /// rule and has no way to start goal 13 or 22 (Q5 asks whether the door goals push 13), so the model keeps no such goal on a stack: the interrupt rule always has a descriptor to read.
    /// </summary>
    public GoalRun WithFrames(ImmutableArray<GoalFrame> frames)
    {
        if (frames.IsDefaultOrEmpty || frames.Length > MaxFrames) throw new ArgumentOutOfRangeException(nameof(frames), "a run holds 1..8 goals");
        foreach (var frame in frames)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (!GoalDescriptors.Has(frame.Goal)) throw new ArgumentException($"goal {(int)frame.Goal} has no descriptor", nameof(frames));
        }
        return this with { Frames = frames };
    }

    /// <summary>
    /// Whether the run's current goal is "not counted" (0x44C9A0): its descriptor's flag or its frame's flag 0x80. The round-start sweep skips such a run
    /// and the significant-goal count leaves it out.
    /// </summary>
    [OriginalFunction(0x44C9A0, OriginalFunctionScope.Full)]
    public bool CurrentGoalNotCounted() =>
        GoalDescriptors.Of(Top.Goal)?.NotCounted == true || (Top.Flags & GoalFrameFlags.NotCounted) != 0;
}

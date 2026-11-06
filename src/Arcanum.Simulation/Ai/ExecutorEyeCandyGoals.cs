using Arcanum.Simulation.Goals;

namespace Arcanum.Simulation.Ai;

/// <summary>
/// ARC-DEV-143 (MIG13 S9b, docs/architecture/goal-table.md §3.5, the S9b read GT83–GT93): what the ART of an eye candy says (the header read 0x5030B0) for the goal's nodes: the frames of the timing part (the underlay, else the foreground), its action frame
/// (0 or past the last frame never fires), the frames of the background that animates beside a foreground (0 none), the pace (1000 / the frame rate) and whether the timing part sets the action flag (a foreground or underlay) and burns (a foreground with no underlay).
/// </summary>
public readonly record struct EyeCandyFacts(int Frames, int ActionFrame, int PaceMs, int BackgroundFrames, bool TimesAction, bool Burns);

/// <summary>
/// ARC-DEV-143: what the overlay goals need of the world. An eye candy (goals 45–48, 58, 59, 75, 76) is an overlay run that any object owns beside its main run (priority 5, no interrupt, carried, GT67); the parts it shows are drawn from the run (the frame
/// is on the goal, <see cref="GoalEyeCandy"/>), so placing and clearing the original's overlay fields is the run's presence. The hover (goal 44) raises and bobs the object's own pixel offset. The sound, the light, the tinting and the draw are the host's.
/// </summary>
public interface IAiOverlayWorld : IAiFireWorld
{
    /// <summary>The ART facts of the goal's entry, or null when the art does not read or its frame rate is 0 (the original divides by zero there; Steamwoven ends the run).</summary>
    EyeCandyFacts? EyeCandyFactsOf(string owner, GoalFrame goal);
    /// <summary>Node 3 of the first begin (0x4CDC50 → 0x4CD7A0): the owner has a free place in every list the entry's parts go to (7 foreground, 7 background, 4 underlay). False ends the run.</summary>
    bool EyeCandyPlace(string owner, GoalFrame goal);
    /// <summary>A random number in <paramref name="low"/>..<paramref name="high"/>, both inclusive (0x4CEF00): the random start frame.</summary>
    int EyeCandyRandom(int low, int high);
    /// <summary>The callbacks of goals 47/48 (phase 3, Callback) and 58/59 (phase 4, EndCallback): 0x457000 resumes the magic-tech run named by <paramref name="tag"/>; nothing for tag −1 or a run that is gone.</summary>
    void EyeCandyCallback(string owner, int tag, int phase);
    /// <summary>The hover's step on the owner's pixel offset y (0x43EAF0): added to the object's own offset, so the offset stays when the run is ended from outside.</summary>
    void HoverShift(string owner, int deltaY);
}

public static partial class AiExecutorRules
{
    /// <summary>The play flags of an EyeCandy component / the add's block (+0x24, GT70).</summary>
    public const uint PlayReverse = 0x1, PlayStack = 0x2, PlayDestroy = 0x4, PlayCallback = 0x8, PlayEndCallback = 0x10, PlayRandomStart = 0x20, PlayFireDamage = 0x40, PlayCheckAlready = 0x80, PlayNoId = 0x100, PlayIceDamage = 0x240;

    /// <summary>The hover's raise and the bob's step in pixels (0x4311F0, 0x431150) and its delay.</summary>
    public const int HoverRaise = 15, HoverStep = 2, HoverPeak = 5, HoverDelayMs = 100;

    /// <summary>The eight eye-candy goals.</summary>
    public static bool IsEyeCandyGoal(GoalType goal) => goal is GoalType.EyeCandy or GoalType.EyeCandyReverse or GoalType.EyeCandyCallback or GoalType.EyeCandyReverseCallback
        or GoalType.EyeCandyEndCallback or GoalType.EyeCandyReverseEndCallback or GoalType.EyeCandyFireDamage or GoalType.EyeCandyReverseFireDamage;

    /// <summary>The goals the overlay stepper interprets: the eye candy's and the hover (44).</summary>
    public static bool IsOverlayGoal(GoalType goal) => IsEyeCandyGoal(goal) || goal == GoalType.Floating;

    /// <summary>The goals an overlay run starts with (they are the same: an eye candy queued under another eye candy's run becomes the bottom goal).</summary>
    public static bool IsOverlayRunGoal(GoalType goal) => IsOverlayGoal(goal);

    /// <summary>Whether the eye-candy goal plays its parts backward (46, 48, 59, 76).</summary>
    public static bool IsReverseEyeCandy(GoalType goal) => goal is GoalType.EyeCandyReverse or GoalType.EyeCandyReverseCallback or GoalType.EyeCandyReverseEndCallback or GoalType.EyeCandyReverseFireDamage;

    /// <summary>The goal the add 0x4CD4BF chooses by the block flags: no callback flag → 45, 75 with fire damage (0x40), the reverse flag (0x1) turning each into its twin; else the callback flag (0x8) gives 47/48 and wins over 0x10 and 0x40; else 0x10 gives 58/59 (GT70, S9b read).</summary>
    public static GoalType EyeCandyGoalOf(uint play)
    {
        var reverse = (play & PlayReverse) != 0;
        if ((play & (PlayCallback | PlayEndCallback)) == 0)
            return (play & PlayFireDamage) != 0 ? reverse ? GoalType.EyeCandyReverseFireDamage : GoalType.EyeCandyFireDamage : reverse ? GoalType.EyeCandyReverse : GoalType.EyeCandy;
        if ((play & PlayCallback) != 0) return reverse ? GoalType.EyeCandyReverseCallback : GoalType.EyeCandyCallback;
        return reverse ? GoalType.EyeCandyReverseEndCallback : GoalType.EyeCandyEndCallback;
    }

    /// <summary>The frame of an eye-candy run (the add 0x4CCF70, GT70): the goal by <paramref name="play"/>, the entry's art, index and tag, the frame flags (a looping entry is also "not counted", 0x80; the random start 0x800; the normal-damage fire 0x4000).</summary>
    public static GoalFrame EyeCandyFrame(uint play, uint art, int entry, int tag, bool looping, string? source = null)
    {
        var flags = GoalFrameFlags.None;
        if (looping) flags |= GoalFrameFlags.NotCounted;
        if ((play & PlayRandomStart) != 0) flags |= GoalFrameFlags.RandomStart;
        if ((play & PlayIceDamage) != 0) flags |= GoalFrameFlags.NormalDamage;
        return new(EyeCandyGoalOf(play)) { Flags = flags, EyeCandy = new(art, entry, tag, source) };
    }

    /// <summary>The frame of a hover run (the AGoal component, 0x4514E0 → 0x44D500).</summary>
    public static GoalFrame HoverFrame() => new(GoalType.Floating);

    /// <summary>The pixel offset the hover has applied after <paramref name="steps"/> bob steps (0 steps: the begin's raise): −15, then the bob (GT69).</summary>
    public static int HoverOffsetAfter(int steps)
    {
        var offset = -HoverRaise; var counter = 1;
        for (var i = 0; i < steps; i++) (counter, offset) = HoverBob(counter, offset);
        return offset;
    }

    // One bob step (0x431150): a positive counter counts up and moves the offset by +2 (past 5 it becomes −1, the move stays +2); a non-positive one counts down and moves by −2 (past −5 it becomes 1).
    private static (int Counter, int Offset) HoverBob(int counter, int offset)
    {
        if (counter > 0) { counter++; offset += HoverStep; if (counter > HoverPeak) counter = -1; }
        else { counter--; offset -= HoverStep; if (counter < -HoverPeak) counter = 1; }
        return (counter, offset);
    }

    /// <summary>
    /// One step of an overlay run (the stepper 0x423560 over the nodes of goals 44–48, 58, 59, 75, 76). Eye candy: 1 begun (2) else begin (3); 2 the step (a part at its last frame, or at frame 0 backward, is false → finish; true waits one pace, goals 47/48 go to the gate 5);
    /// 3 the begin places the parts (a failure ends the run) and waits one pace (goals 58/59 restart at once, 75/76 burn first); 4 the finish (a looping eye candy begins again, else the goal pops; 58/59 call back first and finish in 5); 5/6 the callback gate and call of 47/48, the fire damage of 75/76.
    /// Hover: 1 raised (2) else the raise (3); 2 the bob, 100 ms apart. No overlay has an end node to run: the parts are drawn from the run, so an ended run takes them off, and the hover leaves the object where it is (GT69).
    /// </summary>
    public static AiGoalStepResult StepOverlay(GoalRun run, IAiOverlayWorld world)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(world);
        return StepObjectRun(run, (owner, top) => OverlayNode(owner, top, world), _ => { });
    }

    private static Transition OverlayNode(string owner, GoalFrame goal, IAiOverlayWorld world) =>
        goal.Goal == GoalType.Floating ? HoverNode(owner, goal, world) : IsEyeCandyGoal(goal.Goal) && goal.EyeCandy is not null ? EyeCandyNode(owner, goal, world) : new(Move.Kill);

    private static Transition HoverNode(string owner, GoalFrame goal, IAiOverlayWorld world)
    {
        switch (goal.Node)
        {
            case 1: return (goal.Flags & GoalFrameFlags.Raised) != 0 ? Next(2) : Next(3);
            case 3:
                world.HoverShift(owner, -HoverRaise);
                return new(Move.Restart, Delay: 0, Top: goal with { Flags = goal.Flags | GoalFrameFlags.Raised, Count = 1, RestMs = HoverDelayMs });
            case 2:
            {
                var counter = goal.Count; int delta;
                if (counter > 0) { counter++; delta = HoverStep; if (counter > HoverPeak) counter = -1; }
                else { counter--; delta = -HoverStep; if (counter < -HoverPeak) counter = 1; }
                world.HoverShift(owner, delta);
                return new(Move.Restart, Delay: HoverDelayMs, Top: goal with { Count = counter });
            }
            default: return new(Move.End);
        }
    }

    private static Transition EyeCandyNode(string owner, GoalFrame goal, IAiOverlayWorld world)
    {
        var eye = goal.EyeCandy!;
        var kind = goal.Goal;
        var reverse = IsReverseEyeCandy(kind);
        var fire = kind is GoalType.EyeCandyFireDamage or GoalType.EyeCandyReverseFireDamage;
        var callback = kind is GoalType.EyeCandyCallback or GoalType.EyeCandyReverseCallback;
        var endCallback = kind is GoalType.EyeCandyEndCallback or GoalType.EyeCandyReverseEndCallback;
        var pace = Math.Max(goal.RestMs, 0);
        var looping = (goal.Flags & GoalFrameFlags.NotCounted) != 0;
        switch (goal.Node)
        {
            // 1 (0x431320): true when the begun flag is clear → the begin.
            case 1: return (goal.Flags & GoalFrameFlags.Begun) == 0 ? Next(3) : Next(2);
            // 2 (0x431340 and its kin): one frame on; false at the end of the part → node 4.
            case 2:
            {
                if (world.EyeCandyFactsOf(owner, goal) is not { } facts) return Next(4);
                if (fire && facts.Burns && eye.Frame % 3 == 1) goal = FireDamage(owner, goal, world);
                var (moved, more) = EyeCandyFrameStep(goal, facts, reverse);
                if (moved is null) return new(Move.Next, 4, Top: goal);
                if (!more) return new(Move.Next, 4, Top: moved);
                return callback ? new(Move.Next, 5, Top: moved) : new(Move.Restart, Delay: pace, Top: moved);
            }
            // 3 (0x4315B0 and its reverse 0x431E50): the first begin finds the overlay places; the parts start at frame 0 (backward: the last frame, or a random one), a looping eye candy's second begin at 0 or a random frame.
            case 3:
            {
                var first = (goal.Flags & GoalFrameFlags.OverlaysFound) == 0;
                if (first && !world.EyeCandyPlace(owner, goal)) return new(Move.End);
                if (world.EyeCandyFactsOf(owner, goal) is not { } facts || facts.Frames < 1) return new(Move.End);
                var random = (goal.Flags & GoalFrameFlags.RandomStart) != 0;
                int frame, back;
                if (!reverse)
                {
                    // The forward begin applies the start only when the overlay shows a frame other than 0 (0x4317AA): never at the first begin of an art at frame 0, always at a looping one's second.
                    frame = eye.Frame != 0 ? random ? world.EyeCandyRandom(0, facts.Frames - 1) : 0 : eye.Frame;
                    back = 0;
                }
                else
                {
                    frame = random && facts.Frames > 1 ? world.EyeCandyRandom(1, facts.Frames - 1) : facts.Frames - 1;
                    back = Math.Max(facts.BackgroundFrames - 1, 0);
                }
                var top = goal with { Flags = goal.Flags | GoalFrameFlags.Begun | GoalFrameFlags.OverlaysFound, RestMs = facts.PaceMs, EyeCandy = eye with { Frame = frame, BackgroundFrame = back } };
                if (fire) return new(Move.Next, 5, Top: top);
                return new(Move.Restart, Delay: endCallback ? 0 : facts.PaceMs, Top: top);
            }
            // 4: the finish (0x431A40) of goals 45/47/75: a looping eye candy begins again, else the goal pops. Goals 58/59 call back here (0x42A280, phase 4) and finish in 5.
            case 4:
                if (endCallback)
                {
                    world.EyeCandyCallback(owner, eye.Tag, 4);
                    return Next(5);
                }
                return Finish(goal, looping);
            case 5:
                if (fire) return new(Move.Restart, Delay: pace, Top: FireDamage(owner, goal, world));
                // 47/48: the gate (0x42CAA0) answers true once, when the action flag is set and the handled flag clear.
                if (callback)
                {
                    if ((goal.Flags & GoalFrameFlags.ActionReached) != 0 && (goal.Flags & GoalFrameFlags.ActionHandled) == 0)
                        return new(Move.Next, 6, Top: goal with { Flags = goal.Flags | GoalFrameFlags.ActionHandled });
                    return new(Move.Restart, Delay: pace);
                }
                // 58/59: the finish pops either way (the looping flag keeps the parts on in the original: a pop of a looping 58 clears nothing).
                return new(Move.Pop, Top: goal with { Flags = goal.Flags & ~GoalFrameFlags.Begun });
            case 6:
                world.EyeCandyCallback(owner, eye.Tag, 3);
                return new(Move.Restart, Delay: pace);
            default: return new(Move.End);
        }
    }

    /// <summary>
    /// The frame step of an eye candy (the update 0x431340, its reverse 0x431C40 and the fire steps 0x4322A0, 0x4324D0, S9b read): the timing part moves one frame and the background, if it animates beside a foreground, moves after it; a timing part already at its last frame (frame 0 backward)
    /// ends the goal without moving (<c>Moved</c> null), a background already at its last frame ends it after the timing part moved (<c>More</c> false). The timing part's new frame equal to the ART action frame sets the action flag (a foreground or underlay only; the reverse step tests the frame before the
    /// step against action − 1, an original slip), and nothing clears it.
    /// </summary>
    [OriginalFunction(0x431340, OriginalFunctionScope.Partial)]
    private static (GoalFrame? Moved, bool More) EyeCandyFrameStep(GoalFrame goal, EyeCandyFacts facts, bool reverse)
    {
        var eye = goal.EyeCandy!;
        int frame = eye.Frame, back = eye.BackgroundFrame;
        var flags = goal.Flags;
        bool more;
        if (!reverse)
        {
            if (frame >= facts.Frames - 1) return (null, false);
            frame++;
            if (facts.TimesAction && frame == facts.ActionFrame) flags |= GoalFrameFlags.ActionReached;
            more = facts.BackgroundFrames <= 0 || back < facts.BackgroundFrames - 1;
            if (more && facts.BackgroundFrames > 0) back++;
        }
        else
        {
            if (frame <= 0) return (null, false);
            if (facts.TimesAction && frame == facts.ActionFrame - 1) flags |= GoalFrameFlags.ActionReached;
            frame--;
            more = facts.BackgroundFrames <= 0 || back > 0;
            if (more && facts.BackgroundFrames > 0) back--;
        }
        return (goal with { Flags = flags, EyeCandy = eye with { Frame = frame, BackgroundFrame = back } }, more);
    }

    private static Transition Finish(GoalFrame goal, bool looping) =>
        looping ? new(Move.Next, 3, Top: goal with { Flags = goal.Flags & ~GoalFrameFlags.Begun }) : new(Move.Pop, Top: goal with { Flags = goal.Flags & ~GoalFrameFlags.Begun });
}

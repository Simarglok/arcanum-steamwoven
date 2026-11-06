using System.Collections.Immutable;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;

namespace Arcanum.Simulation.Ai;

/// <summary>The three sounds a door makes (the portal sound caller 0x4F1010 has exactly three sites, GT51): its open and close at the animation's begin, its locked sound at goal 12's last test.</summary>
public enum DoorSound { Open, Close, Locked }

/// <summary>
/// ARC-DEV-138 (MIG13 S7, docs/architecture/goal-table.md §3.5, the S7 read GT38–GT51): what the door goals need of the world. A door is a portal object; its state is its ART
/// frame (0 closed, any other open) and the flags of its lock, so the host answers from the session. Everything is by the door's session entity string. The opener goals 11, 12, 49
/// and 50 live on the opener's run with the door as their target; the door's own goals 70, 71 and 72 live on a run the door owns.
/// </summary>
public interface IAiDoorWorld
{
    /// <summary>The door exists, is a portal and is not destroyed or switched off (the first node of every door goal).</summary>
    bool DoorValid(string door);
    /// <summary>The door is open: its frame is not 0 (<c>portal_is_open</c>).</summary>
    bool DoorOpen(string door);
    /// <summary>The door owns a main run (0x423300): an opening, a pending close or a closing.</summary>
    bool DoorBusy(string door);
    /// <summary>Whether the open attempt (0x4AEB70: busted, locked, jammed, held, the unlock script) refuses <paramref name="opener"/>.</summary>
    bool DoorRefusesOpen(string opener, string door);
    /// <summary>Whether the opener may handle a portal at all (0x45F550).</summary>
    bool DoorOpenerCapable(string opener) => true;
    /// <summary>The use node 0x428A10 of goal 12: 2 action points of the current participant, the turn toward the door and the door's use script (point 1). False ends the opener's run.</summary>
    bool UseDoor(string opener, string door) => true;
    /// <summary>The portal is magically held (lock flag 0x4): a held door cannot be closed.</summary>
    bool DoorHeld(string door);
    /// <summary>Portal flag 0x100 (<see cref="Arcanum.Simulation.Locks.LockRules.StaysOpenBit"/>): the door stays open once opened.</summary>
    bool DoorStaysOpen(string door);
    /// <summary>A turn-based round is active (0x4B6D70): no door closes in one, whoever opened it.</summary>
    bool DoorRound { get; }
    /// <summary>A living player character or NPC (hit points above 0) stands on the door's own tile.</summary>
    bool DoorwayOccupied(string door);
    /// <summary>The door starter 0x4246E0: starts <paramref name="goal"/> (70 or 71) as the door's own run, after the level-3 interrupt of its runs. False: the start was refused.</summary>
    bool StartDoorRun(string door, GoalType goal, string opener);
    /// <summary>One open (<paramref name="opening"/>) or close step of the door's ART (0x4F0950, 0x4F0A90), written to the session; false when the portal cannot step further.</summary>
    bool DoorStep(string door, bool opening);
    /// <summary>The ART's frame delay in milliseconds (1000 / the frame rate, 100 ms when the header does not read).</summary>
    int DoorStepMs(string door);
    void PlayDoorSound(string door, DoorSound sound);
    /// <summary>The end node of a still-open closing door (0x42B6F0): the door is closed at once, with no sound.</summary>
    void SnapDoorClosed(string door);
}

public static partial class AiExecutorRules
{
    /// <summary>The wait of the pending-close goal 72 before every test and before the closing starts, and of goal 70 before it turns into 72: 1.5 s (GT43, GT44).</summary>
    public const int DoorCloseWaitMs = 1500;

    /// <summary>The seven door goals the door stepper interprets: the opener's 11, 12, 49 and 50, the door's own 70, 71 and 72.</summary>
    public static ImmutableHashSet<GoalType> DoorGoals { get; } =
    [
        GoalType.OpenDoor, GoalType.AttemptOpenDoor, GoalType.CloseDoor, GoalType.AttemptCloseDoor,
        GoalType.AnimateDoorOpen, GoalType.AnimateDoorClosed, GoalType.PendClosingDoor,
    ];

    public static bool IsDoorGoal(GoalType goal) => DoorGoals.Contains(goal);

    /// <summary>The three goals a portal owns as its own run (70, 71, 72); the opener's 11, 12, 49 and 50 belong to the opener's run, which the door host does not step.</summary>
    public static bool IsDoorRunGoal(GoalType goal) => goal is GoalType.AnimateDoorOpen or GoalType.AnimateDoorClosed or GoalType.PendClosingDoor;

    /// <summary>The opener's use goal for a portal (goals 38 and 81 turn into 11 for a closed door, 49 otherwise, GS29).</summary>
    public static GoalFrame OpenDoorFrame(string door) => new(GoalType.OpenDoor, door);

    /// <summary>The opener's close goal (49).</summary>
    public static GoalFrame CloseDoorFrame(string door) => new(GoalType.CloseDoor, door);

    /// <summary>The frame of a door's own run (70, 71): the opener is its target (the starter writes it to slot 4).</summary>
    public static GoalFrame DoorRunFrame(GoalType goal, string opener) => new(goal, opener);

    /// <summary>
    /// One step of a door goal's run (the stepper 0x423560 over the door nodes): the nodes run until one waits or the run ends. Door runs push nothing, so the machine is the
    /// executor's reduced to the next node, the pop, the replacement, the restart, the wait and the end step. The run's owner is the opener for 11, 12, 49 and 50 and the door for 70, 71 and 72.
    /// </summary>
    public static AiGoalStepResult StepDoor(GoalRun run, IAiDoorWorld world)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(world);
        var frames = run.Frames.ToBuilder();
        for (var nodes = 1; ; nodes++)
        {
            if (nodes > MaxNodes) return new(null, 0, AiRunEnd.NodeLimit, nodes - 1, AiExecutorReason.None);
            var top = frames[^1];
            var t = DoorNode(run.Owner, top, world);
            if (t.Top is { } changed) frames[^1] = top = changed;
            switch (t.Move)
            {
                case Move.Next:
                    frames[^1] = top with { Node = t.Node };
                    break;
                case Move.Pop:
                    DoorEndNode(run.Owner, frames[^1], world);
                    frames.RemoveAt(frames.Count - 1);
                    if (frames.Count == 0) return new(null, 0, AiRunEnd.Finished, nodes, AiExecutorReason.None);
                    frames[^1] = frames[^1] with { Node = 1 };
                    // The opener's goal popped onto the goal that pushed it (a walk, GT49: "when 12 pops, the walk resumes at node 1 at once"): this machine knows no such node, so the run goes back to its
                    // owner's executor, which steps it next frame.
                    if (!IsDoorGoal(frames[^1].Goal)) return new(run.WithFrames(frames.ToImmutable()), 0, AiRunEnd.Running, nodes, AiExecutorReason.None);
                    break;
                case Move.Replace:
                    frames[^1] = t.Frame!;
                    break;
                case Move.Restart:
                case Move.Wait:
                    frames[^1] = top with { Node = t.Move == Move.Restart ? 1 : t.Node };
                    return new(run.WithFrames(frames.ToImmutable()), Math.Max(t.Delay, 0), AiRunEnd.Running, nodes, AiExecutorReason.None);
                case Move.End:
                    for (var i = frames.Count - 1; i >= 0; i--) DoorEndNode(run.Owner, frames[i], world);
                    return new(null, 0, AiRunEnd.EndStep, nodes, AiExecutorReason.None);
                default:
                    return new(null, 0, AiRunEnd.Killed, nodes, AiExecutorReason.None);
            }
            if (t.Delay > 0) return new(run.WithFrames(frames.ToImmutable()), t.Delay, AiRunEnd.Running, nodes, AiExecutorReason.None);
        }
    }

    /// <summary>The end nodes of a door run ended from outside the stepper (an interrupt, a map's close), top down; a killed run skips them (GS21, GT43).</summary>
    public static void EndDoorRun(GoalRun run, IAiDoorWorld world)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(world);
        if ((run.Flags & GoalRunFlags.Killed) != 0) return;
        for (var i = run.Frames.Length - 1; i >= 0; i--) DoorEndNode(run.Owner, run.Frames[i], world);
    }

    // The door goals' end nodes. 71's (0x42B6F0): a door still open and not held is closed at once, with no sound; 70's (0x42B640) is a net no-op in the original (it toggles an already
    // closed door open and closed again, GT43), so an interrupted opening leaves the door where it stands with no close pending.
    private static void DoorEndNode(string owner, GoalFrame frame, IAiDoorWorld world)
    {
        if (frame.Goal == GoalType.AnimateDoorClosed && world.DoorOpen(owner) && !world.DoorHeld(owner)) world.SnapDoorClosed(owner);
    }

    // The door is closed and no run of its own is on it (nodes 2 of goals 11, 12, 49 and 50: "closed and idle").
    private static bool ClosedAndIdle(IAiDoorWorld world, string door) => world.DoorValid(door) && !world.DoorOpen(door) && !world.DoorBusy(door);

    private static Transition DoorNode(string owner, GoalFrame goal, IAiDoorWorld world)
    {
        var door = goal.Target ?? "";
        switch (goal.Goal)
        {
            case GoalType.OpenDoor:
                // 11: node 1 the door is a live portal; node 2 it is closed with no main run; node 3 the open attempt: a refusal (locked, jammed, held, a refusing script)
                // ends the opener's whole run at once (GT39: the original's nodes 4 and 5 are dead, no sound, no message); otherwise the goal becomes 12.
                return goal.Node switch
                {
                    1 => world.DoorValid(door) ? Next(2) : new(Move.Pop),
                    2 => ClosedAndIdle(world, door) ? Next(3) : new(Move.Pop),
                    _ => world.DoorRefusesOpen(owner, door) ? new(Move.End) : new(Move.Replace, Frame: new GoalFrame(GoalType.AttemptOpenDoor, door)),
                };
            case GoalType.AttemptOpenDoor:
                switch (goal.Node)
                {
                    case 1: return world.DoorValid(door) ? Next(2) : new(Move.Pop);
                    case 2: return ClosedAndIdle(world, door) ? Next(3) : new(Move.End);
                    case 3: return world.DoorOpenerCapable(owner) ? Next(4) : new(Move.End);
                    case 4: return world.DoorRefusesOpen(owner, door) ? new(Move.End) : Next(6);
                    case 6: return world.UseDoor(owner, door) ? Next(7) : new(Move.End);
                    case 7:
                        // The only place the locked sound plays (GT40, GT51); unreachable in play, since nodes 3 and 4 end on a refusal first.
                        if (!world.DoorRefusesOpen(owner, door)) return Next(8);
                        world.PlayDoorSound(door, DoorSound.Locked);
                        return new(Move.End);
                    default:
                        // 8: the starter 0x4246E0 starts 70 on the door; the opener's goal pops at once, it does not wait for the animation. A refused start ends the opener's whole run.
                        return world.StartDoorRun(door, GoalType.AnimateDoorOpen, owner) ? new(Move.Pop) : new(Move.End);
                }
            case GoalType.CloseDoor:
                // 49: a closed door with nothing running ends the run; an open (or opening, or pending) door turns the goal into 50.
                if (goal.Node == 1) return world.DoorValid(door) ? Next(2) : new(Move.Pop);
                return ClosedAndIdle(world, door) ? new(Move.End) : new(Move.Replace, Frame: new GoalFrame(GoalType.AttemptCloseDoor, door));
            case GoalType.AttemptCloseDoor:
                // 50: only a magically held door refuses; no action points, script, lock or capability test (GT41). A door that is opening or pending counts as not idle: the close interrupts it.
                switch (goal.Node)
                {
                    case 1: return world.DoorValid(door) ? Next(2) : new(Move.Pop);
                    case 2: return ClosedAndIdle(world, door) ? new(Move.End) : Next(3);
                    case 3: return world.DoorHeld(door) ? new(Move.End) : Next(4);
                    default: return world.StartDoorRun(door, GoalType.AnimateDoorClosed, owner) ? new(Move.Pop) : new(Move.End);
                }
            case GoalType.AnimateDoorOpen:
                return OpenAnimationNode(owner, goal, world);
            case GoalType.AnimateDoorClosed:
                return CloseAnimationNode(owner, goal, world);
            case GoalType.PendClosingDoor:
                // 72 (0x42B7F0): in a round or with a living critter on the door's tile the close fails and is tried again in 1.5 s; otherwise the closing starts after another 1.5 s, with no
                // second look at the doorway (GT44, anomaly b).
                return world.DoorRound || world.DoorwayOccupied(owner)
                    ? new(Move.Restart, Delay: DoorCloseWaitMs)
                    : new(Move.Replace, Frame: new GoalFrame(GoalType.AnimateDoorClosed, goal.Target), Delay: DoorCloseWaitMs);
            default:
                return new(Move.Kill);
        }
    }

    // Goal 70 (nodes 1 playing-test, 2 step, 3 begin, 4 sticky-test): the begin makes the first open step at once (the door is passable from there on), plays the open sound, stores the ART's frame
    // delay and sets the playing flag; each later step comes one delay after. At the last frame a door with portal flag 0x100 ends the goal and stays open; any other becomes 72 after 1.5 s.
    private static Transition OpenAnimationNode(string door, GoalFrame goal, IAiDoorWorld world)
    {
        switch (goal.Node)
        {
            case 1:
                return PlayingNow(goal) ? Next(2) : Next(3);
            case 2:
                return world.DoorStep(door, opening: true) ? new(Move.Restart, Delay: Math.Max(goal.RestMs, 0)) : Next(4);
            case 3:
                if (!world.DoorStep(door, opening: true)) return new(Move.Pop);
                world.PlayDoorSound(door, DoorSound.Open);
                var rest = world.DoorStepMs(door);
                return new(Move.Restart, Delay: rest, Top: WithPlaying(goal with { RestMs = rest }, true));
            default:
                return world.DoorStaysOpen(door) ? new(Move.Pop) : new(Move.Replace, Frame: new GoalFrame(GoalType.PendClosingDoor, goal.Target), Delay: DoorCloseWaitMs);
        }
    }

    // Goal 71 (nodes 1, 2 step, 3 begin, 5 the last wait): as 70 downward; the door blocks again only when the step reaches frame 0, and the run ends one delay after the step that finds the door closed.
    private static Transition CloseAnimationNode(string door, GoalFrame goal, IAiDoorWorld world)
    {
        switch (goal.Node)
        {
            case 1:
                return PlayingNow(goal) ? Next(2) : Next(3);
            case 2:
                return world.DoorStep(door, opening: false) ? new(Move.Restart, Delay: Math.Max(goal.RestMs, 0)) : new(Move.Wait, Node: 5, Delay: Math.Max(goal.RestMs, 0));
            case 3:
                if (!world.DoorStep(door, opening: false)) return new(Move.Pop);
                world.PlayDoorSound(door, DoorSound.Close);
                var rest = world.DoorStepMs(door);
                return new(Move.Restart, Delay: rest, Top: WithPlaying(goal with { RestMs = rest }, true));
            default:
                return new(Move.Pop);
        }
    }
}

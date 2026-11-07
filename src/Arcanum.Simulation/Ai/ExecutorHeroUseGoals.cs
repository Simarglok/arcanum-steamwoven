using Arcanum.Simulation.Goals;

namespace Arcanum.Simulation.Ai;

public static partial class AiExecutorRules
{
    // Goals 38 `ag_use_object` and 81 `ag_use_container` (ARC-DEV-174, the S11b read GT124 to GT129): priority 3, 6 nodes, no end node, one shared node table. Node 1 (0x428930) writes the range into the frame
    // (1 for a container or a creature, 2 for anything else); node 2 (0x425130, the same test as goal 8's node 1) passes when the distance (Chebyshev) is at most the range and the access is clear, else pushes goal 8,
    // which inherits the range; node 3 (0x4284A0) turns the goal into the door goals 11 and 49 for a portal (S11d, ARC-DEV-176: nodes 3, 5 and 6 here, the door goals over the door ports); node 4 (0x428A10) acts and ends the run.
    // There are no get-up or unconceal nodes. The act charges 2 action points in a round (the viewer charges them for a loot window, not for a trigger object's use), then runs the use script or the loot window; the script's answer is ignored.
    private static Transition HeroUseNode(GoalFrame goal, IAiHeroWalkWorld world)
    {
        var target = goal.Target;
        switch (goal.Node)
        {
            case 1:
                if (target is null || world.ObjectTarget(target) is not { } known) return End(AiExecutorReason.ObjectGone);
                return new(Move.Next, 2, Top: goal with { Range = known.UseRange });
            case 2:
                if (target is null || world.ObjectTarget(target) is not { } there || world.Tile() is not { } at) return End(AiExecutorReason.ObjectGone);
                return Distance(at, there.Tile) <= goal.Range ? Next(3) : new(Move.Push, Frame: new GoalFrame(GoalType.MoveNear, target) { Range = goal.Range });
            case 3:
                // 0x4284A0: a portal goes on to 5 (the target is copied into the door slot) and 6; anything else acts at node 4.
                if (target is null || world.ObjectTarget(target) is not { } kind) return End(AiExecutorReason.ObjectGone);
                return Next(kind.Portal ? 5 : 4);
            case 5:
                return Next(6);
            case 6:
                // 0x4284F0 (GT124): a portal that is not open and has no main run becomes goal 11 (open door), any other goal 49 (close door). Steamwoven waits here until the hero stands settled, so a door never
                // closes under the hero's step (the old rule of the door click).
                if (target is null || world.Doors is not { } doors) return End(AiExecutorReason.ObjectGone);
                if (!world.HeroSettled) return new(Move.Wait, Node: 6, Delay: HeroRoutePollMs);
                return new(Move.Replace, Frame: doors.DoorValid(target) && !doors.DoorOpen(target) && !doors.DoorBusy(target) ? OpenDoorFrame(target) : CloseDoorFrame(target));
            default:
                if (target is null || world.ObjectTarget(target) is null) return End(AiExecutorReason.ObjectGone);
                world.UseObject(target);
                return new(Move.End);
        }
    }
}

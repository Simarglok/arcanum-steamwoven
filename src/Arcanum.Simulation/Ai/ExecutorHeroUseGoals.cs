using Arcanum.Simulation.Goals;

namespace Arcanum.Simulation.Ai;

public static partial class AiExecutorRules
{
    // Goals 38 `ag_use_object` and 81 `ag_use_container` (ARC-DEV-174, the S11b read GT124 to GT129): priority 3, 6 nodes, no end node, one shared node table. Node 1 (0x428930) writes the range into the frame
    // (1 for a container or a creature, 2 for anything else); node 2 (0x425130, the same test as goal 8's node 1) passes when the distance (Chebyshev) is at most the range and the access is clear, else pushes goal 8,
    // which inherits the range; node 3 (0x4284A0) turns the goal into the door goals 11 and 49 for a portal (S11d, not modelled here: the hero's door click is its own intent); node 4 (0x428A10) acts and ends the run.
    // There are no get-up or unconceal nodes. The act charges 2 action points in a round (not modelled: the viewer's use charges none), then runs the use script or the loot window; the script's answer is ignored.
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
                return Distance(at, there.Tile) <= goal.Range ? Next(4) : new(Move.Push, Frame: new GoalFrame(GoalType.MoveNear, target) { Range = goal.Range });
            default:
                if (target is null || world.ObjectTarget(target) is null) return End(AiExecutorReason.ObjectGone);
                world.UseObject(target);
                return new(Move.End);
        }
    }
}

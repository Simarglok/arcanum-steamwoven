using Arcanum.Simulation.Goals;

namespace Arcanum.Simulation.Ai;

public static partial class AiExecutorRules
{
    // Goals 15 `ag_pickup_item` and 16 `ag_attempt_pickup` for the hero (ARC-DEV-175, the S11c read GT131 to GT137): priority 3, no end node. Goal 15 has ten nodes, of which 1, 2, 4 and 5 are constants and 6 to 9 are
    // compiled-out ownership checks nothing reaches: node 3 (0x424D90) compares the picker's location with the item's and pushes goal 16 when they are the same; otherwise node 10 (0x42BEA0) writes range 0 into the
    // frame and pushes goal 8, so the hero walks ONTO the item's tile (as the NPC does); goal 15 runs again from node 1 when goal 8 pops. It has no head check of its own (an item taken meanwhile resolves to the holder's
    // tile in the original; Steamwoven ends the run, the item being gone). Goal 16 has two nodes: node 1 (0x42A9B0) picks the item up (0x42AA70), node 2 sets the stand art, and both end the whole run.
    private static Transition HeroPickupNode(GoalFrame goal, IAiHeroWalkWorld world)
    {
        var target = goal.Target;
        switch (goal.Node)
        {
            case 1:
            case 2:
                return Next(goal.Node + 1);
            case 3:
                if (target is null || world.ObjectTarget(target) is not { } there || world.Tile() is not { } at) return End(AiExecutorReason.ObjectGone);
                return at == there.Tile ? new(Move.Push, Frame: new GoalFrame(GoalType.AttemptPickup, target)) : Next(4);
            case 4:
                return Next(5);
            case 5:
                return Next(10);
            default:
                return new(Move.Push, Frame: new GoalFrame(GoalType.MoveNear, target) { Range = 0 }, Top: goal with { Range = 0 }, Reason: AiExecutorReason.ActionApproach);
        }
    }

    private static Transition HeroAttemptPickupNode(GoalFrame goal, IAiHeroWalkWorld world)
    {
        if (goal.Target is not { } target || world.ObjectTarget(target) is null) return End(AiExecutorReason.ObjectGone);
        world.PickUpObject(target);
        return End(AiExecutorReason.PickedUp);
    }
}

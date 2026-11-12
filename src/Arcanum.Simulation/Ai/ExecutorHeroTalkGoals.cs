using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Ai;

/// <summary>What the hero's talk and use goals know of their target (<see cref="IAiHeroWalkWorld.ObjectTarget"/>).</summary>
/// <param name="Tile">The cell the target stands on.</param>
/// <param name="Refused">Node 1 of the talk (0x42ACD0) refuses the target: it is stoned, stunned or paralysed, or not a creature.</param>
/// <param name="UseRange">The range the use goals 38 and 81 write into their frame at node 1 (0x428930): 1 for a container or a creature, 2 for anything else.</param>
/// <param name="Portal">ARC-DEV-176: the target is a door or a window (node 3 of the use goals, 0x4284A0, turns the goal into the door goals 11 and 49).</param>
public readonly record struct AiHeroTarget(CellCoord Tile, bool Refused = false, int UseRange = AiExecutorRules.HeroUseRange, bool Portal = false);

public static partial class AiExecutorRules
{
    /// <summary>The range the hero talks from (0x4AFBB0 answers 5 for a player character, 10 for anyone else): the distance (0x4B96F0) between the two must be strictly below it (node 0x42AE10), so the hero talks from at most 4 cells.</summary>
    public const int HeroTalkRange = 5;

    /// <summary>The range of the use goals 38 and 81 for scenery, portals, walls, items and traps (node 1, 0x428930, writes 2 into the frame; 1 for a container or a creature).</summary>
    public const int HeroUseRange = 2;

    /// <summary>The range of the use goals 38 and 81 for a container or a creature (a body to loot, a chest): node 1 (0x428930) writes 1 into the frame.</summary>
    public const int HeroContainerUseRange = 1;

    /// <summary>The range node 6 of goal 21 (0x42BEA0) writes into the frame it pushes goal 8 over: the approach ends adjacent to the target.</summary>
    public const int HeroTalkApproachRange = 1;

    // Goal 21 `ag_talk`, six nodes, no end node (ARC-DEV-169, the S11 read GT115 to GT118): 1 the checks (0x42ACD0: a target that is gone or refused ends the run, silently), 2 the get-up (the hero's own host gets it
    // up; a hero that is down ends the run), 3 the unconceal (never for the hero), 4 the range test (0x42AE10), 5 the act (0x42AF00: the dialog opens, the run ends by its end step), 6 the approach: goal 8 with range 1,
    // after which the goal runs again from node 1. ARC-AI-1137 (GT122, the second read in the GT122 row): a talk started with Shift carries the no-walk flag 0x100, the one reader of which is the walk step (node 9 of goals 5, 61 and 80:
    // it answers 0 at once), so the approach of node 6 never takes a step: goals 8 and 61 hand the run to each other inside one step until the 101st node kills it. The hero stays where it is, with no message and no sound
    // (a talk is never started in a round, so the turn the guard would end is not at stake); node 6 of a no-walk run therefore ends the run at once. A Shift talk from within 4 cells reaches node 5 and talks as always.
    private static Transition HeroTalkNode(GoalFrame goal, HeroWalkState state, IAiHeroWalkWorld world)
    {
        var target = goal.Target;
        switch (goal.Node)
        {
            case 1:
                return target is not null && world.ObjectTarget(target) is { Refused: false } ? Next(2) : new(Move.Pop, Reason: AiExecutorReason.ObjectGone);
            case 2:
                return world.Down ? End(AiExecutorReason.ObjectGone) : Next(3);
            case 3:
                return Next(4);
            case 4:
                if (target is null || world.ObjectTarget(target) is not { } there || world.Tile() is not { } at) return End(AiExecutorReason.ObjectGone);
                return Next(Distance(at, there.Tile) < HeroTalkRange ? 5 : 6);
            case 5:
                // The act (0x42AF00) re-tests only that the target is there; no distance test follows, and the call always answers "done" (the end step), so the run ends here.
                if (target is null || world.ObjectTarget(target) is null) return End(AiExecutorReason.ObjectGone);
                world.OpenDialog(target);
                return new(Move.End);
            default:
                if ((state.Flags & GoalRunFlags.NoWalk) != 0) return End(AiExecutorReason.NoWalkOutOfRange);
                return new(Move.Push, Frame: new GoalFrame(GoalType.MoveNear, target) { Range = HeroTalkApproachRange });
        }
    }

    // Goal 8 `ag_move_near_obj` for the hero: 1 pops when the target is within the frame's range (the original also asks that the step toward it is clear), 2 and 3 the get-up and unconceal pushes (not modelled), 4 a kept
    // path (goal 61; not modelled), 5 the plan, 6 the common no-path node. The original's plan pushes goal 61 along a path to the target dropping the last step; Steamwoven pushes the hero's walk goal 3 to the
    // host's approach cell next to the target (a deliberate difference: the walk, its far goals and its steps are the one walk of the hero), and the goal tests the range again when the walk pops, so a target that moved
    // is approached again.
    private static Transition HeroMoveNearNode(GoalFrame goal, IAiHeroWalkWorld world)
    {
        var target = goal.Target;
        switch (goal.Node)
        {
            case 1:
                if (target is null || world.ObjectTarget(target) is not { } there || world.Tile() is not { } at) return End(AiExecutorReason.ObjectGone);
                return Distance(at, there.Tile) <= goal.Range ? new(Move.Pop, Reason: AiExecutorReason.FollowInRange) : Next(5);
            case 5:
                if (target is null || world.Tile() is not { } here) return End(AiExecutorReason.ObjectGone);
                // A cell the hero stands on already is no way: the walk would pop at once and the goal would plan again for ever.
                if (world.ApproachTile(target, goal.Range) is not { } approach || approach == here) return Next(6);
                return new(Move.Push, Frame: new GoalFrame(GoalType.MoveToTile, null, approach), Reason: AiExecutorReason.FollowWalk);
            default:
                return world.InRound ? End(AiExecutorReason.NoPath) : new(Move.Push, Frame: new GoalFrame(GoalType.MoveToPause), Reason: AiExecutorReason.NoPath);
        }
    }
}

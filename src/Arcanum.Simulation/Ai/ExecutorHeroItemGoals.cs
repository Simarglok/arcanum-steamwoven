using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Ai;

public static partial class AiExecutorRules
{
    /// <summary>The range goals 39 and 41 write into their frame at node 1 (ledger IA11 and AG5: the user acts from next to the target or the tile).</summary>
    public const int HeroItemUseRange = 1;

    // Goal 39 `ag_use_item_on_object` for the hero (ARC-DEV-365, T-g; ledger IA8, AG5): node 1 writes range 1, node 2 passes to the use when the target is within the range, else pushes the approach goal 8 (the same
    // approach as the talk's); node 3 (0x428CD0) ends the run through the use (0x462CC0) when the item is still the user's. A run with the no-walk flag (the click had Shift, IA8: inference) acts from where the hero
    // stands and walks no approach. The goal has no end node of its own beyond the end step.
    private static Transition HeroUseItemNode(GoalFrame goal, HeroWalkState state, IAiHeroWalkWorld world)
    {
        var target = goal.Target;
        switch (goal.Node)
        {
            case 1:
                return new(Move.Next, 2, Top: goal with { Range = HeroItemUseRange });
            case 2:
                if (target is null || world.ObjectTarget(target) is not { } there || world.Tile() is not { } at) return End(AiExecutorReason.ObjectGone);
                if ((state.Flags & GoalRunFlags.NoWalk) != 0 || Distance(at, there.Tile) <= goal.Range) return Next(3);
                return new(Move.Push, Frame: new GoalFrame(GoalType.MoveNear, target) { Range = goal.Range }, Reason: AiExecutorReason.ActionApproach);
            default:
                if (goal.Item is not { } item || target is null) return End(AiExecutorReason.ObjectGone);
                world.HeroUseItem(item, target);
                return End(AiExecutorReason.ItemUsed);
        }
    }

    // Goal 41 `ag_use_item_on_tile` for the hero (ARC-DEV-365, T-g; ledger IA11): node 1 writes range 1 (AG5), node 2 (0x425270) passes when the tile is within the range of the user, else the goal walks next to the tile (the
    // original's push code reads as goal 7, a move near the tile; the hero's walk is its walk goal 3 to the host's cell next to the tile, as the object approaches are), node 3 (0x429040) needs the item to be the
    // user's and uses it on the tile (0x462FC0); a refusal ends the run (the answer 0).
    private static Transition HeroUseItemOnTileNode(GoalFrame goal, IAiHeroWalkWorld world)
    {
        if (goal.Tile is not { } tile || world.Tile() is not { } at) return End(AiExecutorReason.ObjectGone);
        switch (goal.Node)
        {
            case 1:
                return new(Move.Next, 2, Top: goal with { Range = HeroItemUseRange });
            case 2:
                if (Distance(at, tile) <= goal.Range) return Next(3);
                if (world.ItemTileApproach(tile, goal.Range) is not { } approach || approach == at) return End(AiExecutorReason.NoPath);
                return new(Move.Push, Frame: new GoalFrame(GoalType.MoveToTile, null, approach), Reason: AiExecutorReason.ActionApproach);
            default:
                if (goal.Item is not { } item) return End(AiExecutorReason.ObjectGone);
                world.HeroUseItemOnTile(item, tile);
                return End(AiExecutorReason.ItemUsed);
        }
    }
}

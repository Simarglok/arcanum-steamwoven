using Arcanum.Simulation.Goals;

namespace Arcanum.Simulation.Ai;

public static partial class AiExecutorRules
{
    // Goal 37 `ag_throw_item` for the hero (ARC-DEV-182, MIG13 S12b): the same three steps as an NPC's (ThrowItemNode, TW2): 1 the turn to the tile and the throw's start (the art 14, 0x42BD40, 0x42C390, 0x42CB10), at the action frame 2 the
    // throw (0x42AB90 → 0x4B3C00), after the rest of the animation 3 the end step. The goal has no approach: the hero throws from the cell it stands on (TT2, TT6), so the first step waits only for the hero to stand. The 2 action
    // points of the end (0x433270) and the pending cost of the end step (EX6) exempt the local PC (TT4); the hero's action points are charged at the start by the viewer's port (the Original setting charges none).
    private static Transition HeroThrowNode(GoalFrame goal, IAiHeroWalkWorld world)
    {
        if (goal.Item is not { } item || goal.Tile is not { } tile) return End(AiExecutorReason.ObjectGone);
        switch (goal.Node)
        {
            case 1:
                if (!world.HeroSettled) return new(Move.Wait, Node: 1, Delay: HeroRoutePollMs);
                if (world.HeroBeginThrow(item, tile) is not { } swing) { world.HeroThrowEnds(item, AiExecutorReason.ActionRefused); return End(AiExecutorReason.ActionRefused); }
                return Next(2, Math.Max(swing.ActionFrameMs, 1)) with { Top = goal with { RestMs = Math.Max(swing.RestMs, 0) } };
            case 2:
                if (!world.HeroThrowItem(item, tile)) { world.HeroThrowEnds(item, AiExecutorReason.ActionRefused); return End(AiExecutorReason.ActionRefused); }
                return new(Move.Next, 3, Delay: Math.Max(goal.RestMs, 1), Reason: AiExecutorReason.Thrown);
            default:
                world.HeroThrowEnds(item, AiExecutorReason.Thrown);
                return End(AiExecutorReason.Thrown);
        }
    }
}

using Arcanum.Simulation.Goals;

namespace Arcanum.Simulation.Ai;

public static partial class AiExecutorRules
{
    /// <summary>The range of goal 33 (node 1, 0x42BEA0): the skill is used on a target next to the user.</summary>
    public const int HeroSkillRange = 1;

    // The hero's skill goals (ARC-DEV-184, MIG13 S12d). Goal 33 `ag_use_skill_on` (GT142): node 1 writes range 1, node 2 tests the distance and the access, nodes 3-4 walk (the original plans itself and walks with goal 5, never
    // pushing goal 8; the hero's walk is the approach goal 8, as the talk's, S11a), node 7 (0x42A440) replaces the goal by goal 86 `ag_attempt_use_pickpocket_skill_on` for skill 5. Skill 14 (pick locks) replaces it by goal 67 (ARC-DEV-185); the generic skill 34 is not modelled. There is no sight test in goals 33 and 86 (GT142).
    private static Transition HeroUseSkillNode(GoalFrame goal, IAiHeroWalkWorld world)
    {
        var target = goal.Target;
        if (goal.Skill is not (PickpocketSkill or PickLocksSkill)) return End(AiExecutorReason.ActionRefused);
        switch (goal.Node)
        {
            case 1:
                return new(Move.Next, 2, Top: goal with { Range = HeroSkillRange });
            default:
                if (target is null || world.ObjectTarget(target) is not { } there || world.Tile() is not { } at) return End(AiExecutorReason.ObjectGone);
                return Distance(at, there.Tile) <= goal.Range
                    // Node 5 (0x42A490) takes skill 14 to goal 67, node 7 (0x42A440) skill 5 to goal 86 (GT142).
                    ? new(Move.Replace, Frame: Child(goal.Skill == PickLocksSkill ? GoalType.AttemptPickLocks : GoalType.AttemptPickpocket, goal) with { Flags = goal.Flags }, Reason: AiExecutorReason.ActionApproach)
                    : new(Move.Push, Frame: new GoalFrame(GoalType.MoveNear, target) { Range = goal.Range }, Reason: AiExecutorReason.ActionApproach);
        }
    }

    // Goal 67 (the NPC's AttemptPickLocksNode): the turn, then the attempt (0x42A4E0, no action points of its own); the pick-locks loops of the animation are not modelled for the hero (it plays none), so the attempt comes at once and the goal pops.
    private static Transition HeroAttemptPickLocksNode(GoalFrame goal, IAiHeroWalkWorld world)
    {
        if (goal.Target is not { } target || goal.Item is not { } item) return End(AiExecutorReason.ObjectGone);
        if (goal.Node != 1) return new(Move.Pop);
        if (!world.HeroFaceSkill(target)) return End(AiExecutorReason.ActionRefused);
        world.HeroPickLock(target, item);
        return new(Move.Pop, Reason: AiExecutorReason.SkillAttempted);
    }

    // Goal 86 (GT143): node 1 turns the thief toward the victim (0x42C0F0), node 4 (0x42A2A0) pays the 4 action points and makes the check (0x4C7090, the plant's flag 0x2), and a success pops the run with no animation (node 8).
    // Steamwoven's hero asks the viewer for the attempt at its next safe point (the victim's script and the attack event may change runs) and pops; the failure's animation 12 and the 2 extra points of a non-PC (node 9) are not modelled.
    private static Transition HeroAttemptPickpocketNode(GoalFrame goal, IAiHeroWalkWorld world)
    {
        if (goal.Target is not { } target || goal.Item is not { } item) return End(AiExecutorReason.ObjectGone);
        if (goal.Node != 1) return new(Move.Pop);
        if (!world.HeroFaceSkill(target)) return End(AiExecutorReason.ActionRefused);
        world.HeroPickPocket(target, item, (goal.Flags & GoalFrameFlags.Plant) != 0);
        return new(Move.Pop, Reason: AiExecutorReason.SkillAttempted);
    }
}

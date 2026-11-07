using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Ai;

/// <summary>What the hero's attack goals know of their target (<see cref="IAiHeroWalkWorld.AttackTarget"/>).</summary>
/// <param name="Tile">The cell the target stands on.</param>
/// <param name="Dead">The target is dead, destroyed or broken: node 1 of goal 18 (0x429450) fails.</param>
public readonly record struct AiHeroAttackTarget(CellCoord Tile, bool Dead = false);

public static partial class AiExecutorRules
{
    // Goal 18 `ag_attack` for the hero (ARC-DEV-181, MIG13 S12a): the same nodes as an NPC's (AttackNode), the hero's ports. 1 the target is there and alive (0x429450; else the goal pops and the run finishes), 2 the attack is possible
    // (0x4294A0: a weapon the hero cannot use yet, a boomerang in flight; else the run ends), 3 the get-up (the hero's own host gets it up; a hero that is down ends the run), 4 the unconceal (never for the hero), 5 the reach test (0x429960:
    // the target within the weapon's range with a clear shot, melee weapons too) replaces the goal by goal 19, else 6 the range (an NPC writes 1 here; the hero's approach cell is the planner's), 7 the approach: the walk to the cell next to
    // the target (the planner 0x4280D0 of the old viewer walk, over the hero's walk goal 3; the original pushes goal 60 over the combat walks 61 and 80), after which the goal runs again from node 1, 8 no way (the run ends).
    private static Transition HeroAttackNode(GoalFrame goal, IAiHeroWalkWorld world)
    {
        var target = goal.Target;
        switch (goal.Node)
        {
            case 1:
                if (target is not null && world.AttackTarget(target) is { Dead: false }) return Next(2);
                if (target is not null) world.AttackEnds(target, AiExecutorReason.AttackTargetGone);
                return new(Move.Pop, Reason: AiExecutorReason.AttackTargetGone);
            case 2:
                if (world.AttackPossible(target!)) return Next(3);
                world.AttackEnds(target!, AiExecutorReason.AttackImpossible);
                return End(AiExecutorReason.AttackImpossible);
            case 3:
                return world.Down ? End(AiExecutorReason.ObjectGone) : Next(4);
            case 4:
                return Next(5);
            case 5:
                return world.InAttackReach(target!)
                    ? new(Move.Replace, Frame: new GoalFrame(GoalType.AttemptAttack, target, range: goal.Range, sound: goal.Sound), Reason: AiExecutorReason.AttackInReach)
                    : Next(6);
            case 6:
                return Next(7);
            case 7:
                // A cell the hero stands on already is no way: the walk would pop at once and the goal would plan again for ever.
                // (A walk that arrived and still left the target out of reach is not tried again and again: the host's approach cell is null after three walks of one click, Steamwoven's cap.)
                if (world.Tile() is not { } here || world.AttackApproachTile(target!) is not { } approach || approach == here) return Next(8);
                return new(Move.Push, Frame: new GoalFrame(GoalType.MoveToTile, target, approach), Reason: AiExecutorReason.AttackApproach);
            default:
                world.AttackEnds(target!, AiExecutorReason.AttackBlocked);
                return End(AiExecutorReason.AttackBlocked);
        }
    }

    // Goal 19 `ag_attempt_attack` for the hero: 1 the reach test again and the attack's start (0x432700: the weapon's cost charged, the animation begun; an unpaid attack kills the run), the action frame later 2 the hit step (the
    // viewer's resolution tests the reach once more and hits, or lets the swing go by), the rest of the animation later 3 the repeat test (0x429760): a hero in combat mode with the auto-attack option outside a round, whose target is
    // still there, replaces the goal with a new attack 5 ms later; any other run ends. (A hero that has not come to a halt waits for its step to end.)
    private static Transition HeroAttemptAttackNode(GoalFrame goal, IAiHeroWalkWorld world)
    {
        var target = goal.Target!;
        switch (goal.Node)
        {
            case 1:
                if (world.AttackTarget(target) is null || !world.InAttackReach(target)) { world.AttackEnds(target, AiExecutorReason.AttackBlocked); return End(AiExecutorReason.AttackBlocked); }
                if (!world.HeroSettled) return new(Move.Wait, Node: 1, Delay: HeroRoutePollMs);
                if (world.HeroBeginAttack(target) is not { } swing) { world.AttackEnds(target, AiExecutorReason.Unpaid); return new(Move.Kill, Reason: AiExecutorReason.Unpaid); }
                return Next(2, Math.Max(swing.ActionFrameMs, 1)) with { Top = goal with { RestMs = Math.Max(swing.RestMs, 0) } };
            case 2:
                world.HeroAttackHit(target);
                return new(Move.Next, 3, Delay: Math.Max(goal.RestMs, 1), Reason: AiExecutorReason.AttackHit);
            default:
                if (world.HeroAttackRepeats(target))
                    return new(Move.Replace, Frame: new GoalFrame(GoalType.Attack, target, range: goal.Range, sound: goal.Sound), Delay: RealTimeCombatRules.RepeatDelayMs);
                return new(Move.Pop);
        }
    }
}

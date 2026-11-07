using Arcanum.Simulation.Goals;

namespace Arcanum.Simulation.Ai;

/// <summary>How the hero's casting goal ended (the viewer's cast host frees the run and decides on the casts queued behind it).</summary>
public enum HeroCastEnd
{
    /// <summary>The start was refused (the points, the pre-animation check, the target, the turn): the run is freed unpaid.</summary>
    Refused,
    /// <summary>The goal ended through the end step (no cast eye candy, a target that failed before it): the casts queued behind it are dropped.</summary>
    EndStep,
    /// <summary>The goal popped when its play ended (0x431A40): the next queued cast starts.</summary>
    Popped,
}

public static partial class AiExecutorRules
{
    // The hero's casting goals 26 and 27 (ARC-DEV-183, MIG13 S12c): the NPC's nodes (CastNode, AttemptSpellNode) over the hero ports. Goal 26: the start (0x429B50 with 0x429BC0: the action points of a round, the pre-animation check)
    // and the turn (0x42C850), then goal 27 replaces it (the approach node is not reachable, EX13; the hero has no get-up or unconceal node: a hero that cannot act has no cast). Goal 27: the play's start (0x429F00: the target
    // re-check and the cast eye candy, whose release frame and last frame time the goal); without a play the release at once and the end step; else the action frame (0x42CAA0 → 0x429C40: the missile or the release 0x429C80), the pop after
    // the rest. The goal's end node is the magic run's end (0x429CD0), which the viewer's port runs when the goal ends on its own and the host runs when an interrupt ends it.
    private static Transition HeroCastNode(GoalFrame goal, IAiHeroWalkWorld world)
    {
        if (goal.SpellRun is not { } run) return End(AiExecutorReason.ObjectGone);
        if (world.HeroStartCast(run) != AiCastGoalStart.Started || !world.HeroFaceCast(run))
        {
            world.HeroCastEnds(run, HeroCastEnd.Refused);
            return End(AiExecutorReason.CastRefused);
        }
        return new(Move.Replace, Frame: Child(GoalType.AttemptSpell, goal), Reason: AiExecutorReason.CastPlays);
    }

    private static Transition HeroAttemptSpellNode(GoalFrame goal, IAiHeroWalkWorld world)
    {
        if (goal.SpellRun is not { } run) return End(AiExecutorReason.ObjectGone);
        switch (goal.Node)
        {
            case 1:
                var swing = world.HeroBeginCast(run);
                if (!swing.Plays)
                {
                    var released = world.HeroReleaseCast(run);
                    world.HeroCastEnds(run, HeroCastEnd.EndStep);
                    return End(released ? AiExecutorReason.CastReleased : AiExecutorReason.CastNotReleased);
                }
                return Next(2, Math.Max(swing.ActionFrameMs, 1)) with { Top = goal with { RestMs = Math.Max(swing.RestMs, 0) } };
            case 2:
                if (!world.HeroCastActionFrame(run))
                {
                    world.HeroCastEnds(run, HeroCastEnd.Popped);
                    return new(Move.Pop, Reason: AiExecutorReason.CastNotReleased);
                }
                return new(Move.Next, 3, Delay: Math.Max(goal.RestMs, 1), Reason: AiExecutorReason.CastReleased);
            default:
                world.HeroCastEnds(run, HeroCastEnd.Popped);
                return new(Move.Pop);
        }
    }
}

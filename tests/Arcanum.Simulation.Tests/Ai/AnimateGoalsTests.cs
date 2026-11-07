using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>
/// ARC-DEV-233 (MIG13 S14b, goal-table.md GT155–GT182): the script action 62's goals as the stepper interprets them: goal 0 plays an animation once and its end node stands the critter, goal 60 kneels
/// (9), plays the hands (10) once at the kneel's rate and is replaced by goal 51, which plays the kneel back and stands the critter. Over the executor tests' fake world.
/// </summary>
[TestClass]
public sealed class AnimateGoalsTests
{
    private const string Npc = "oid:bandit";
    private static readonly CombatAnomalyProfile Steamwoven = CombatAnomalyProfile.Steamwoven;

    private static ExecutorRulesTests.World Arena()
    {
        var world = new ExecutorRulesTests.World();
        world.At[Npc] = new(10, 10);
        world.Movers[Npc] = new(Fatigue: 20, MaxFatigue: 20);
        return world;
    }

    private static GoalRun Run(GoalFrame frame) => new(Npc, default, frame);

    [TestMethod]
    public void The_frame_is_goal_60_for_animation_10_of_a_critter_and_goal_0_otherwise_and_nothing_outside_0_to_25()
    {
        Assert.AreEqual(GoalType.AnimateKneelMagicHands, AiExecutorRules.PerformAnimationFrame(10, critter: true)!.Goal);
        Assert.AreEqual(GoalType.Animate, AiExecutorRules.PerformAnimationFrame(10, critter: false)!.Goal);
        Assert.AreEqual((GoalType.Animate, 13), (AiExecutorRules.PerformAnimationFrame(13, critter: true)!.Goal, AiExecutorRules.PerformAnimationFrame(13, critter: true)!.Animation));
        Assert.IsNull(AiExecutorRules.PerformAnimationFrame(26, critter: true));
        Assert.IsNull(AiExecutorRules.PerformAnimationFrame(-1, critter: true));
    }

    [TestMethod]
    public void Goal_0_plays_the_animation_frame_by_frame_once_and_its_end_node_stands_the_critter()
    {
        var world = Arena();
        world.Clips[13] = new(4, 10);
        var steps = ExecutorRulesTests.Drive(Run(AiRunFrames.Of(13)), world, Steamwoven);
        CollectionAssert.AreEqual(new[] { (13, 0), (13, 1), (13, 2), (13, 3), (0, 0) }, world.Writes, "GT168–GT172: frame 0, then each frame, then the end node's standing art");
        Assert.AreEqual((0, 0), world.Writes[^1], "the end node stood the critter");
        Assert.AreEqual(AiRunEnd.Finished, steps[^1].End);
        Assert.AreEqual(3 * 100, steps.Where(s => !s.Ended).Sum(s => s.DelayMs), "GT171: (frames - 1) delays of 1000 / the rate");
    }

    [TestMethod]
    public void A_stoned_critter_refuses_the_start_and_a_paralysed_one_unless_the_animation_is_a_death()
    {
        var stoned = Arena();
        stoned.Clips[13] = new(4, 10);
        stoned.StonedSet.Add(Npc);
        ExecutorRulesTests.Drive(Run(AiRunFrames.Of(13)), stoned, Steamwoven);
        Assert.IsFalse(stoned.Writes.Any(w => w.Item1 == 13), "GT169: a stoned object plays nothing");

        var held = Arena();
        held.Clips[13] = new(4, 10);
        held.ParalysedSet.Add(Npc);
        ExecutorRulesTests.Drive(Run(AiRunFrames.Of(13)), held, Steamwoven);
        Assert.IsFalse(held.Writes.Any(w => w.Item1 == 13), "GT169: flag 0x60 refuses unless the animation is 17..19");

        var dying = Arena();
        dying.Clips[18] = new(3, 10);
        dying.ParalysedSet.Add(Npc);
        ExecutorRulesTests.Drive(Run(AiRunFrames.Of(18)), dying, Steamwoven);
        Assert.IsTrue(dying.Writes.Contains((18, 0)) && dying.Writes.Contains((18, 2)), "animation 18 plays under the paralysis");
    }

    [TestMethod]
    public void Goal_60_kneels_plays_the_hands_once_at_the_kneels_rate_and_kneels_back_then_stands()
    {
        var world = Arena();
        world.Clips[9] = new(3, 10);
        world.Clips[10] = new(2, 20);
        var steps = ExecutorRulesTests.Drive(Run(AiExecutorRules.PerformAnimationFrame(10, critter: true)!), world, Steamwoven);
        CollectionAssert.AreEqual(new[] { (9, 0), (9, 1), (9, 2), (10, 0), (10, 1), (9, 2), (9, 1), (9, 0), (0, 0) }, world.Writes,
            "GT175–GT178: kneel forward, hands once, kneel backward, stand");
        Assert.AreEqual(AiRunEnd.Finished, steps[^1].End);
    }

    private static class AiRunFrames
    {
        public static GoalFrame Of(int animation) => AiExecutorRules.PerformAnimationFrame(animation, critter: true)!;
    }
}

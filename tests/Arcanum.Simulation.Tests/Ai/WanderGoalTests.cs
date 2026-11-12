using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>
/// ARC-AI-1313 (ledger VA19, VAG2): the wander goals 65 and 66 (nodes 0x425930 and 0x425D60, starters 0x435E60 and 0x436040) over the open test grid whose planner walks straight lines: the node draws a tile within the
/// radius of its tether, accepts it when a path of at most the radius steps reaches it and then becomes the walk to it (goal 7) after 300 ms; the dark wander also wants a tile that is not lighter than the NPC's own.
/// </summary>
[TestClass]
public sealed class WanderGoalTests
{
    private const string Npc = "oid:wanderer";
    private static readonly CellCoord Tether = new(50, 60);

    /// <summary>Hands out the given offsets in order and records the purposes asked.</summary>
    private sealed class OffsetRolls(params int[] offsets) : IAiRollSource
    {
        public readonly List<(AiRollPurpose Purpose, int Min, int Max)> Asked = [];
        public int Roll(AiRollPurpose purpose, int min, int max)
        {
            Asked.Add((purpose, min, max));
            return Math.Clamp(offsets[Asked.Count - 1], min, max);
        }
    }

    private static ExecutorRulesTests.World WorldAt(CellCoord at)
    {
        var world = new ExecutorRulesTests.World();
        world.At[Npc] = at;
        return world;
    }

    private static GoalRun Run(GoalType goal = GoalType.Wander) => new(Npc, default, new GoalFrame(goal, tile: Tether, range: NpcRouteRules.WanderRadius));

    [TestMethod]
    public void Both_wander_goals_are_interpreted_and_are_the_ordinary_priority_with_the_walk_as_their_alias()
    {
        Assert.IsTrue(AiExecutorRules.Interprets(GoalType.Wander));
        Assert.IsTrue(AiExecutorRules.Interprets(GoalType.WanderSeekDarkness));
        Assert.AreEqual(3, GoalDescriptors.Of(GoalType.Wander)!.Priority);
        Assert.AreEqual(3, GoalDescriptors.Of(GoalType.WanderSeekDarkness)!.Priority);
        Assert.IsTrue(GoalDescriptors.IsSameActivity(GoalType.Wander, GoalType.MoveNearTile), "the wanders list goal 7 (GS9): a walk found for the NPC is the same activity");
    }

    [TestMethod]
    public void The_node_draws_x_then_y_within_the_radius_and_becomes_the_walk_to_a_reachable_tile()
    {
        var world = WorldAt(Tether);
        var rolls = new OffsetRolls(3, -2);
        var step = AiExecutorRules.Step(Run(), world, rolls, CombatAnomalyProfile.Steamwoven);
        Assert.AreEqual(2, rolls.Asked.Count);
        Assert.IsTrue(rolls.Asked.All(a => a is (AiRollPurpose.WanderOffset, -4, 4)), "two draws of -radius..radius");
        Assert.IsFalse(step.Ended);
        Assert.AreEqual(AiExecutorReason.WanderWalk, step.Reason);
        Assert.AreEqual(AiExecutorRules.WanderRetryMs, step.DelayMs, "both exits wait 300 ms");
        var top = step.Run!.Top;
        Assert.AreEqual(GoalType.MoveNearTile, top.Goal, "the wander is replaced by the walk (goal 7)");
        Assert.AreEqual(new CellCoord(53, 58), top.Tile, "x is the first draw, y the second");
        Assert.AreEqual(1, step.Run.Depth, "a replace, not a push");
    }

    [TestMethod]
    public void A_tile_with_no_path_or_a_path_longer_than_the_radius_restarts_the_goal_after_300_ms()
    {
        // The test grid walks straight lines (diagonals count one), so reach the NPC from far away: its tile is 10 steps from the drawn one.
        var far = WorldAt(new CellCoord(60, 60));
        var longPath = AiExecutorRules.Step(Run(), far, new OffsetRolls(0, 0), CombatAnomalyProfile.Steamwoven);
        Assert.AreEqual(AiExecutorReason.WanderRetry, longPath.Reason);
        Assert.AreEqual(GoalType.Wander, longPath.Run!.Top.Goal, "the goal itself restarts");
        Assert.AreEqual(AiExecutorRules.WanderRetryMs, longPath.DelayMs);
        var none = WorldAt(Tether);
        none.NoPaths = true;
        var refused = AiExecutorRules.Step(Run(), none, new OffsetRolls(1, 1), CombatAnomalyProfile.Steamwoven);
        Assert.AreEqual(AiExecutorReason.WanderRetry, refused.Reason);
        Assert.AreEqual(GoalType.Wander, refused.Run!.Top.Goal);
        // The drawn tile that is the NPC's own has an empty path, which the original counts as none (0x41F3C0 answers 0).
        var here = AiExecutorRules.Step(Run(), WorldAt(Tether), new OffsetRolls(0, 0), CombatAnomalyProfile.Steamwoven);
        Assert.AreEqual(AiExecutorReason.WanderRetry, here.Reason);
    }

    [TestMethod]
    public void A_failed_wander_ends_a_non_pcs_subturn_in_a_round_and_never_a_pcs()
    {
        var world = WorldAt(Tether);
        world.NoPaths = true;
        world.Round = true;
        world.Actor = Npc;
        AiExecutorRules.Step(Run(), world, new OffsetRolls(1, 1), CombatAnomalyProfile.Steamwoven);
        CollectionAssert.Contains(world.Calls, "end subturn");
        var other = WorldAt(Tether);
        other.NoPaths = true;
        other.Round = true;
        other.Actor = "oid:someone";
        AiExecutorRules.Step(Run(), other, new OffsetRolls(1, 1), CombatAnomalyProfile.Steamwoven);
        CollectionAssert.DoesNotContain(other.Calls, "end subturn", "only the participant's subturn ends");
    }

    [TestMethod]
    public void The_dark_wander_takes_a_tile_that_is_not_lighter_than_its_own_and_restarts_without_a_plan_otherwise()
    {
        var world = WorldAt(Tether);
        world.Light[Tether] = 120;
        world.Light[new CellCoord(52, 61)] = 200;
        var lighter = AiExecutorRules.Step(Run(GoalType.WanderSeekDarkness), world, new OffsetRolls(2, 1), CombatAnomalyProfile.Steamwoven);
        Assert.AreEqual(AiExecutorReason.WanderRetry, lighter.Reason);
        Assert.AreEqual(GoalType.WanderSeekDarkness, lighter.Run!.Top.Goal);
        CollectionAssert.DoesNotContain(world.Calls, "plan ToTile", "a lighter tile is refused before the path is asked");
        world.Light[new CellCoord(52, 61)] = 120;
        var equal = AiExecutorRules.Step(Run(GoalType.WanderSeekDarkness), world, new OffsetRolls(2, 1), CombatAnomalyProfile.Steamwoven);
        Assert.AreEqual(AiExecutorReason.WanderWalk, equal.Reason, "as light as its own tile is accepted");
        Assert.AreEqual(new CellCoord(52, 61), equal.Run!.Top.Tile);
        // The plain wander never reads the light.
        var plain = WorldAt(Tether);
        plain.Light[new CellCoord(52, 61)] = 255;
        plain.Light[Tether] = 0;
        Assert.AreEqual(AiExecutorReason.WanderWalk, AiExecutorRules.Step(Run(), plain, new OffsetRolls(2, 1), CombatAnomalyProfile.Steamwoven).Reason);
    }

    [TestMethod]
    public void The_replacing_walk_takes_the_npc_to_the_drawn_tile_and_the_run_finishes()
    {
        var world = WorldAt(Tether);
        var rolls = new OffsetRolls(3, -2);
        var steps = new List<AiGoalStepResult>();
        for (var current = Run(); current is not null && steps.Count < 50;)
        {
            var step = AiExecutorRules.Step(current, world, rolls, CombatAnomalyProfile.Steamwoven);
            steps.Add(step);
            current = step.Run;
        }
        Assert.AreEqual(new CellCoord(53, 58), world.At[Npc], "the NPC stands on the drawn tile");
        Assert.AreEqual(AiRunEnd.Finished, steps[^1].End);
        Assert.AreEqual(2, rolls.Asked.Count, "the walk does not draw again");
    }

    [TestMethod]
    public void A_wander_run_without_a_tether_or_a_tile_ends_as_the_object_gone()
    {
        var noTether = AiExecutorRules.Step(new GoalRun(Npc, default, new GoalFrame(GoalType.Wander, range: 4)), WorldAt(Tether), new OffsetRolls(), CombatAnomalyProfile.Steamwoven);
        Assert.AreEqual(AiRunEnd.EndStep, noTether.End);
        Assert.AreEqual(AiExecutorReason.ObjectGone, noTether.Reason);
        var noTile = AiExecutorRules.Step(Run(), new ExecutorRulesTests.World(), new OffsetRolls(), CombatAnomalyProfile.Steamwoven);
        Assert.AreEqual(AiExecutorReason.ObjectGone, noTile.Reason);
    }
}

using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Goals;

/// <summary>ARC-DEV-302 (GT146, GT147): the runs a hold's start interrupts: a walk (goal 3 at the bottom) and a run whose current goal is 61; not a run or a run near.</summary>
[TestClass]
public sealed class HoldInterruptTests
{
    private static GoalRun Run(string owner, int slot, params GoalType[] goals)
    {
        var run = new GoalRun(owner, new GoalHandle(slot, slot + 1), new GoalFrame(goals[0], null, new CellCoord(4, 4)));
        return goals.Length == 1 ? run : run.WithFrames([.. goals.Select(goal => new GoalFrame(goal, null, new CellCoord(4, 4)))]);
    }

    [TestMethod]
    public void A_walk_and_a_run_on_the_melee_approach_are_interrupted_and_nothing_else()
    {
        var table = GoalRunTable.Restore(20, [
            Run("pc", 0, GoalType.MoveToTile, GoalType.AttemptMove),
            Run("pc", 1, GoalType.RunToTile, GoalType.AttemptMove),
            Run("pc", 2, GoalType.Attack, GoalType.MoveNear, GoalType.MeleeApproach),
            Run("pc", 3, GoalType.Attack, GoalType.AttemptAttack),
            Run("pc", 4, GoalType.RunNearTile, GoalType.AttemptMove),
            Run("oid:1", 5, GoalType.MoveToTile),
            Run("pc", 6, GoalType.MoveToTile) with { Done = true },
        ])!;
        CollectionAssert.AreEqual(new[] { 0, 2 }, HoldInterrupt.Runs(table, "pc").Select(r => r.Handle.Slot).ToArray());
        CollectionAssert.AreEqual(new[] { 5 }, HoldInterrupt.Runs(table, "oid:1").Select(r => r.Handle.Slot).ToArray());
        Assert.AreEqual(0, HoldInterrupt.Runs(GoalRunTable.Empty, "pc").Count);
    }
}

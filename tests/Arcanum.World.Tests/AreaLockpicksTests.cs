using Arcanum.Session;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>ARC-SCRIPT-2752: the lockpicks handed to goal 40 lose the use script point that asked for the goal, and nothing else.</summary>
[TestClass]
public sealed class AreaLockpicksTests
{
    private static SessionLockpick Picks(params int[] scripts) => new("oid:picks", SessionLockpick.LockpickFlag, 5, default, scripts);

    [TestMethod]
    public void The_use_script_point_is_dropped_and_the_other_points_are_kept()
    {
        var stripped = AreaLockpicks.WithoutUseScript(Picks(SessionLockpick.UseScript, 5, 7));
        CollectionAssert.AreEqual(new[] { 5, 7 }, stripped.ScriptPoints.ToArray());
        Assert.AreEqual((5, SessionLockpick.LockpickFlag, "oid:picks"), (stripped.UsageBonus, stripped.Flags, stripped.Entity));
    }

    [TestMethod]
    public void Lockpicks_without_a_use_script_are_returned_as_they_are()
    {
        var picks = Picks(5);
        Assert.AreSame(picks, AreaLockpicks.WithoutUseScript(picks));
    }
}

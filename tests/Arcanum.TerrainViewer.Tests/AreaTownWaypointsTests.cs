using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-UI-092: the town map's waypoint list and the PC's walk through it (ledger WM85-WM88, WM105, WM107, WM108, WM110-WM125). Invented tiles only.
[TestClass]
public sealed class AreaTownWaypointsTests
{
    private static readonly (long X, long Y) Pc = (100, 100);

    private static AreaTownWaypointRefusal Add(AreaTownWaypoints list, long x, Func<(long X, long Y), (long X, long Y), bool>? path = null) =>
        list.Add((x * 10, 50), (x, 100), Pc, path ?? ((_, _) => true));

    [TestMethod]
    public void A_waypoint_is_added_after_a_path_test_from_the_pc_then_from_the_previous_waypoint_and_is_selected()
    {
        var list = new AreaTownWaypoints();
        var starts = new List<((long, long), (long, long))>();
        bool Path((long X, long Y) from, (long X, long Y) to) { starts.Add((from, to)); return true; }
        Assert.AreEqual(AreaTownWaypointRefusal.None, Add(list, 103, Path));
        Assert.AreEqual(AreaTownWaypointRefusal.None, Add(list, 106, Path));
        CollectionAssert.AreEqual(new[] { (Pc, (103L, 100L)), ((103L, 100L), (106L, 100L)) }, starts);
        Assert.AreEqual((2, 1), (list.Count, list.Selected));
        Assert.AreEqual(((103L * 10, 50L), (103L, 100L)), (list.Items[0].Pixel, list.Items[0].Location));
    }

    [TestMethod]
    public void A_blocked_path_a_full_town_and_the_shared_cap_refuse_a_waypoint()
    {
        var list = new AreaTownWaypoints();
        Assert.AreEqual(AreaTownWaypointRefusal.Blocked, Add(list, 103, (_, _) => false));
        Assert.AreEqual((0, -1), (list.Count, list.Selected));
        for (var i = 0; i < AreaTownWaypoints.TownCap; i++) Assert.AreEqual(AreaTownWaypointRefusal.None, Add(list, 101 + i));
        // The seventh asks no path at all: message 611 comes first.
        Assert.AreEqual(AreaTownWaypointRefusal.TownCap, Add(list, 120, (_, _) => throw new InvalidOperationException("no path test after the cap")));
        Assert.AreEqual(AreaTownWaypoints.TownCap, list.Count);
    }

    [TestMethod]
    public void A_removal_takes_the_selected_waypoint_or_the_last_and_keeps_the_selection_in_range()
    {
        var list = new AreaTownWaypoints();
        Assert.IsFalse(list.Remove());
        for (var i = 0; i < 4; i++) Add(list, 101 + i);
        Assert.IsTrue(list.Select(1));
        Assert.IsFalse(list.Select(4) || list.Select(-1));
        Assert.IsTrue(list.Remove());
        CollectionAssert.AreEqual(new long[] { 101, 103, 104 }, list.Items.Select(w => w.Location.X).ToArray());
        Assert.AreEqual(1, list.Selected, "the selection stays on the next waypoint");
        list.Select(2);
        Assert.IsTrue(list.Remove());
        Assert.AreEqual((2, 1), (list.Count, list.Selected), "clamped to the new last");
        list.Clear();
        Assert.AreEqual((0, -1), (list.Count, list.Selected));
    }

    private sealed class Walker
    {
        public (int, int)? Goal; public (int, int) At = (0, 0); public readonly List<(int, int)> Asked = [];
        public bool Accept = true;
        public bool Start((int, int) cell) { Asked.Add(cell); if (Accept) Goal = cell; return Accept; }
    }

    [TestMethod]
    public void The_chain_starts_each_walk_when_the_one_before_ended_on_its_tile()
    {
        var chain = new AreaTownWalkChain<(int, int)>();
        var w = new Walker { Goal = (1, 1) };
        chain.Begin((1, 1), true, [(2, 2), (3, 3)]);
        Assert.AreEqual((true, 2), (chain.Active, chain.Pending));
        Assert.AreEqual(AreaTownWalkTick.Waiting, chain.Tick(w.Goal, w.At, w.Start), "walking to the first");
        w.Goal = null; w.At = (1, 1);
        Assert.AreEqual(AreaTownWalkTick.Started, chain.Tick(w.Goal, w.At, w.Start));
        CollectionAssert.AreEqual(new[] { (2, 2) }, w.Asked);
        Assert.AreEqual(AreaTownWalkTick.Waiting, chain.Tick(w.Goal, w.At, w.Start));
        w.Goal = null; w.At = (2, 2);
        Assert.AreEqual(AreaTownWalkTick.Started, chain.Tick(w.Goal, w.At, w.Start));
        w.Goal = null; w.At = (3, 3);
        Assert.AreEqual(AreaTownWalkTick.Finished, chain.Tick(w.Goal, w.At, w.Start));
        Assert.IsFalse(chain.Active);
        Assert.AreEqual(AreaTownWalkTick.Idle, chain.Tick(null, w.At, w.Start));
    }

    [TestMethod]
    public void A_replaced_or_short_walk_drops_the_rest_and_a_refused_walk_is_skipped()
    {
        var w = new Walker();
        var chain = new AreaTownWalkChain<(int, int)>();
        chain.Begin((1, 1), true, [(2, 2)]);
        Assert.AreEqual(AreaTownWalkTick.Replaced, chain.Tick((9, 9), w.At, w.Start), "a click gave the PC another goal");
        Assert.IsFalse(chain.Active);
        chain.Begin((1, 1), true, [(2, 2)]);
        Assert.AreEqual(AreaTownWalkTick.EndedShort, chain.Tick(null, (0, 5), w.Start), "the walk stopped elsewhere");
        Assert.AreEqual(0, chain.Pending);
        // The first walk was refused: the next waypoint starts a fresh walk (WM108); a refused one is skipped too.
        chain.Begin((1, 1), false, [(2, 2), (3, 3)]);
        w.Accept = false;
        Assert.AreEqual(AreaTownWalkTick.Finished, chain.Tick(null, w.At, w.Start));
        CollectionAssert.AreEqual(new[] { (2, 2), (3, 3) }, w.Asked);
        chain.Begin((1, 1), false, [(2, 2), (3, 3)]);
        w.Accept = true; w.Asked.Clear();
        Assert.AreEqual(AreaTownWalkTick.Started, chain.Tick(null, w.At, w.Start));
        CollectionAssert.AreEqual(new[] { (2, 2) }, w.Asked);
    }
}

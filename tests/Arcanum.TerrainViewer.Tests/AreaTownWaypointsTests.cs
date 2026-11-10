using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-UI-092: the town map's waypoint list (ledger WM85-WM88, WM105, WM107, WM108, WM110-WM125). Invented tiles only.
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
}

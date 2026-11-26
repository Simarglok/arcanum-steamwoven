using Arcanum.Data.Area;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-RENDER-3362: the rows' part of the line of fire is made once per read-only row list; what a map is given of its own (the moved records, the live actors, the gone test)
/// holds for that map only. Invented rows, no original data.
/// </summary>
[TestClass]
public sealed class AreaLineOfFireRowsTests
{
    [TestMethod]
    public void Each_map_over_one_row_list_has_its_own_moved_records_live_actors_and_gone_test()
    {
        using var f = AreaAiHostRebindTests.Open();
        var steps = AreaAiHostRebindTests.Sector(0, 0);
        var rows = Array.AsReadOnly(new[] { f.RowA, f.RowB });
        var at = new CellCoord(checked((int)f.RowA.WorldX!.Value), checked((int)f.RowA.WorldY!.Value));
        var elsewhere = new CellCoord(at.X + 1, at.Y + 1);
        AreaLineOfFire Map(AreaObjectKey[] moved, Dictionary<CellCoord, AreaObjectRecord> live, Func<AreaObjectRecord, bool> gone) =>
            new(steps, rows, moved.ToHashSet(), live, gone, _ => false, _ => null);
        string[] On(AreaLineOfFire map, CellCoord cell) => map.Objects(cell).Select(o => AreaUseScriptHost.EntityId(o.Object)).ToArray();
        var a = AreaUseScriptHost.EntityId(f.RowA);

        var plain = Map([], [], _ => false);
        CollectionAssert.Contains(On(plain, at), a);
        // A walked actor: its T0 record is moved, and its row stands on the live cell, moved or not.
        var walked = Map([f.RowA.Key], new() { [elsewhere] = f.RowA }, _ => false);
        CollectionAssert.DoesNotContain(On(walked, at), a);
        CollectionAssert.Contains(On(walked, elsewhere), a);
        var gone = Map([], [], row => row.Key == f.RowA.Key);
        CollectionAssert.DoesNotContain(On(gone, at), a);
        // The earlier maps and a new one read the rows as they did: nothing of one map stays in the shared part.
        CollectionAssert.Contains(On(plain, at), a);
        CollectionAssert.DoesNotContain(On(plain, elsewhere), a);
        CollectionAssert.AreEqual(On(plain, at), On(Map([], [], _ => false), at));
    }
}

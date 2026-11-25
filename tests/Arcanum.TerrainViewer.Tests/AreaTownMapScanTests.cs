using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-RENDER-3107: the town-map scan's tables (which map each town map is drawn from). Synthetic data only; the scan of a real module is run on the owner's Mac.
[TestClass]
public sealed class AreaTownMapScanTests
{
    [TestMethod]
    public void The_tables_are_tab_separated_with_a_header_and_one_row_per_map_and_per_map_and_town()
    {
        var (maps, sectors) = AreaTownMapScan.ToTsv(
            [new("inner", 9, 3, 3, 477, 178), new("shopmap", 1, 1, 1, null, null)],
            [new("inner", 7, 2, 1, 1, 2, 3), new("inner", 8, 1, 5, 5, 5, 5)]);
        CollectionAssert.AreEqual(new[] { "map\tsector_files\tterrain_width\tterrain_height\tstart_x\tstart_y", "inner\t9\t3\t3\t477\t178", "shopmap\t1\t1\t1\t\t" },
            maps.TrimEnd('\n').Split('\n'));
        CollectionAssert.AreEqual(new[] { "map\ttownmap\tsectors\tmin_x\tmin_y\tmax_x\tmax_y", "inner\t7\t2\t1\t1\t2\t3", "inner\t8\t1\t5\t5\t5\t5" },
            sectors.TrimEnd('\n').Split('\n'));
    }

    [TestMethod]
    public void A_module_path_outside_a_game_folder_is_not_scanned()
    {
        Assert.IsNull(AreaTownMapScan.Scan(Path.Combine(Path.GetTempPath(), "no-such-folder", "Arcanum.dat"), out var detail));
        StringAssert.Contains(detail, "not read");
    }
}

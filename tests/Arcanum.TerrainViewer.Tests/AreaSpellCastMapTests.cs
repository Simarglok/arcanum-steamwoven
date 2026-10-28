using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-MAGIC-014: the cast map's direction of a tile offset, as the original numbers it (0x4B8D50).
[TestClass]
public sealed class AreaSpellCastMapTests
{
    [TestMethod]
    public void Directions_follow_the_original_table_with_a_two_to_one_slope_rule()
    {
        var cases = new (long Dx, long Dy, int Direction)[]
        {
            (-1, -1, 0), (-1, 0, 1), (-1, 1, 2), (0, 1, 3), (1, 1, 4), (1, 0, 5), (1, -1, 6), (0, -1, 7), (0, 0, 5),
            (5, 2, 5), (3, 2, 4), (5, -1, 5), (-1, -5, 7), (-4, 2, 2), (-5, 2, 1),
        };
        foreach (var (dx, dy, direction) in cases) Assert.AreEqual(direction, AreaSpellCastMap.Direction(dx, dy), $"({dx},{dy})");
    }
}

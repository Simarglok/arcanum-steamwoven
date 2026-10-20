using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-042: the listener's tile under a point of the AREA world space (the diamond of chart cell (u, v) is centred
/// on (40·(v − u) + 40, 20·(u + v) + 20)).</summary>
[TestClass]
public sealed class AreaSoundListenerTests
{
    [TestMethod]
    public void The_point_falls_in_the_diamond_of_the_nearest_cell_centre()
    {
        Assert.AreEqual(new CellCoord(0, 0), AreaSoundListener.ChartCell(40, 20));
        Assert.AreEqual(new CellCoord(3, 7), AreaSoundListener.ChartCell(40 * (7 - 3) + 40, 20 * 10 + 20));
        Assert.AreEqual(new CellCoord(3, 7), AreaSoundListener.ChartCell(40 * (7 - 3) + 40 + 30, 20 * 10 + 20 + 4), "inside the diamond, off centre");
        Assert.AreEqual(new CellCoord(3, 8), AreaSoundListener.ChartCell(40 * (7 - 3) + 40 + 30, 20 * 10 + 20 + 12), "past its lower right edge");
        Assert.AreEqual(new CellCoord(-2, -1), AreaSoundListener.ChartCell(40 * 1 + 40, 20 * -3 + 20));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaSoundListener.ChartCell(double.NaN, 0));
    }
}

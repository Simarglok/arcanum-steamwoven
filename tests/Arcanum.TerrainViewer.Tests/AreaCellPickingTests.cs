using Arcanum.Data.Terrain;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class AreaCellPickingTests
{
    [TestMethod]
    public void DiamondCentreIsTheAreaTileCentreAndPicksItsCell()
    {
        foreach (var cell in new CellCoord[] { new(6, 1), new(0, 0), new(-64, -64), new(127, 127), new(-64, 127), new(127, -64) })
        {
            // Independent of the picker: AreaScenePlan draws the tile at Project(x,y)+(1,0) with a 78x40 footprint.
            var tile = TerrainGeometry.Project(cell.X, cell.Y);
            Assert.AreEqual(new Vec2(tile.X + 1 + 39, tile.Y + 20), AreaCellPicking.Center(cell));
            Assert.AreEqual(cell, AreaCellPicking.Pick(AreaCellPicking.Center(cell)));
        }
    }

    [TestMethod]
    public void EveryPointInsideTheDiamondPicksItsCell()
    {
        var cell = new CellCoord(6, 1);
        var c = AreaCellPicking.Center(cell);
        // Just inside each vertex and each edge midpoint; the diamond spans +/-40 x and +/-20 y.
        foreach (var (dx, dy) in new[] { (-39.9, 0.0), (39.9, 0.0), (0.0, -19.9), (0.0, 19.9), (-19.9, -9.9), (19.9, -9.9), (-19.9, 9.9), (19.9, 9.9), (10.0, 4.0) })
            Assert.AreEqual(cell, AreaCellPicking.Pick(new(c.X + dx, c.Y + dy)), $"offset {dx},{dy}");
        // Just outside: the four edge neighbours (x-1 is up-right, x+1 down-left, y-1 up-left, y+1 down-right).
        Assert.AreEqual(new CellCoord(5, 1), AreaCellPicking.Pick(new(c.X + 20.1, c.Y - 10.1)));
        Assert.AreEqual(new CellCoord(7, 1), AreaCellPicking.Pick(new(c.X - 20.1, c.Y + 10.1)));
        Assert.AreEqual(new CellCoord(6, 0), AreaCellPicking.Pick(new(c.X - 20.1, c.Y - 10.1)));
        Assert.AreEqual(new CellCoord(6, 2), AreaCellPicking.Pick(new(c.X + 20.1, c.Y + 10.1)));
    }

    [TestMethod]
    public void TheActorFootAnchorIsTheDiamondsLeftVertexNotItsCentre()
    {
        // DiagnosticActorMotion.Project + (0,20) for a settled actor: 40 px left of the drawn diamond centre.
        var foot = DiagnosticActorMotion.Project(new(6, 1)) + new Vec2(0, 20);
        Assert.AreEqual(AreaCellPicking.Center(new(6, 1)) - new Vec2(40, 0), foot);
        Assert.AreNotEqual(new CellCoord(6, 1), AreaCellPicking.Pick(foot), "A shared vertex goes to the larger map coordinate.");
    }

    [TestMethod]
    public void NonFiniteAndFarPointsAreNotPicked()
    {
        Assert.IsNull(AreaCellPicking.Pick(new(double.NaN, 0)));
        Assert.IsNull(AreaCellPicking.Pick(new(0, double.PositiveInfinity)));
        Assert.IsNull(AreaCellPicking.Pick(new(1e12, 0)));
        Assert.IsNotNull(AreaCellPicking.Pick(AreaCellPicking.Center(new(AreaCellPicking.Reach, 0))));
    }
}

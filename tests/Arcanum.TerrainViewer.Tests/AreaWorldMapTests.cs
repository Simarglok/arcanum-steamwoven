using System.Text;
using Arcanum.Data.Maps;
using Arcanum.Data.Messages;
using Arcanum.Simulation.Travel;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-048 world-map journey over an invented 40 × 40 start map (no original data): waypoints by pixel and area,
/// the route, the steps with game time and discovery in the session, the landing, a refused route and the picture.</summary>
[TestClass]
public sealed class AreaWorldMapTests
{
    private const long Width = 40;

    // Grass everywhere; a water wall at sector x = 20 for y 0..30, so the far side is reached only around its end.
    private static AreaWorldMapData Data()
    {
        var types = TerrainTypeTable.Create(MesFile.Parse(Encoding.ASCII.GetBytes("{0}{grass}\n{1}{water /b}")).File!, _ => true).Table!;
        var words = new ushort[Width * Width];
        for (var y = 0; y <= 30; y++) words[20 + y * Width] = 1 << 11 | 1 << 6;
        var grid = new WorldTerrainGrid(new(1.2f, false, Width, Width, 0), words);
        var map = new WorldMap("start", new(0, 0, Width * 64, Width * 64), grid.Header, null, null, [], [], [], null, []);
        var sectors = new WorldTravelSectors(map, grid, types);
        // Area 1 near (10, 10); area 2 at (30, 35), past the wall's end.
        var areas = WorldAreaTable.Create(["0, 0, 0, 0 /Unknown/none", $"{10 * 64 + 32}, {10 * 64 + 32}, 0, 0 /West/w", $"{30 * 64 + 32}, {35 * 64 + 32}, 0, 0 /East/e"]);
        var size = (int)(Width / AreaWorldMap.PictureScale);
        return new("start", 1, areas, sectors, Width, size, AreaWorldMap.Draw(Width, size, grid, types, sectors));
    }

    private static TravelLocation Sector(long x, long y) => WorldTravelGeometry.Centre(new(x, y));

    [TestMethod]
    public void A_journey_takes_the_hours_discovers_the_area_and_lands_on_it()
    {
        using var session = ConversationSessionFixture.Create();
        var data = Data();
        var map = new AreaWorldMap(data, session, Sector(5, 10));
        Assert.AreEqual(AreaWorldMapRefusal.NoWaypoints, map.Start());
        Assert.AreEqual(AreaWorldMapRefusal.None, map.AddArea(1));
        Assert.AreEqual((5, 5L), map.Estimate());
        Assert.AreEqual(6, map.RoutePixels().Count); // the position, four sector centres and the area
        Assert.AreEqual(AreaWorldMapRefusal.None, map.Start());
        Assert.AreEqual(AreaWorldMapRefusal.Travelling, map.AddArea(2));
        long advanced = 0; var discovered = new List<int>();
        void Step() { var step = map.Step(ms => advanced += ms); if (step.Discovered > 0) discovered.Add(step.Discovered); }
        // Mid-journey the route drawn starts where the journey stands: after two steps, two centres and the area remain.
        Step(); Step();
        var ahead = map.RoutePixels();
        Assert.AreEqual(map.Position, ahead[0]);
        CollectionAssert.AreEqual(new[] { 8L, 9L, 10L }, ahead.Skip(1).Take(3).Select(p => WorldTravelGeometry.Sector(WorldTravelGeometry.Location(p, Width)).X).ToArray());
        Assert.AreEqual(4, ahead.Count);
        while (map.Travelling) Step();
        Assert.AreEqual(5 * WorldJourney.MsPerSector, advanced);
        Assert.AreEqual(5, map.Steps);
        CollectionAssert.AreEqual(new[] { 1 }, discovered);
        Assert.IsTrue(session.IsAreaKnown(1));
        // The travel step clears the last discovered record again (ledger WT3).
        Assert.AreEqual(0, session.QueryLastDiscoveredArea());
        Assert.AreEqual(data.TravelAreas[1].Location, map.Landing);
        Assert.AreEqual(WorldTravelGeometry.Pixel(data.TravelAreas[1].Location, Width), map.Position);
    }

    [TestMethod]
    public void A_route_through_the_wall_is_refused_and_one_around_it_plans()
    {
        using var session = ConversationSessionFixture.Create();
        var map = new AreaWorldMap(Data(), session, Sector(10, 10));
        // Straight east the wall is 31 sectors long: no detour of under 8 sectors joins it (message 601).
        Assert.AreEqual(AreaWorldMapRefusal.NoRoute, map.AddWaypoint(WorldTravelGeometry.Pixel(Sector(30, 10), Width)));
        Assert.AreEqual(0, map.Waypoints.Count);
        // Down the west side, then around the wall's end.
        Assert.AreEqual(AreaWorldMapRefusal.None, map.AddWaypoint(WorldTravelGeometry.Pixel(Sector(18, 34), Width)));
        Assert.AreEqual(AreaWorldMapRefusal.None, map.AddArea(2));
        Assert.AreEqual(2, map.Waypoints.Count);
        var data = Data();
        Assert.IsTrue(map.RoutePixels().All(p => data.Passes(WorldTravelGeometry.Sector(WorldTravelGeometry.Location(p, Width)))));
        Assert.AreEqual(AreaWorldMapRefusal.OffMap, map.AddWaypoint(new(0, 5)));
        Assert.IsTrue(map.Clear());
        Assert.AreEqual(0, map.Waypoints.Count);
    }

    [TestMethod]
    public void A_cancel_stops_where_the_journey_stands_and_the_picture_marks_blocked_sectors()
    {
        using var session = ConversationSessionFixture.Create();
        var data = Data();
        var map = new AreaWorldMap(data, session, Sector(5, 30));
        Assert.AreEqual(AreaWorldMapRefusal.None, map.AddWaypoint(WorldTravelGeometry.Pixel(Sector(15, 30), Width)));
        map.Start(); map.Step(_ => { }); map.Step(_ => { });
        var landing = map.Cancel();
        Assert.IsFalse(map.Travelling);
        Assert.AreEqual(Sector(7, 30), landing);
        // Picture pixel (x, y) shows map pixel (4x + 2, 4y + 2), that is sector (W − px, py): (4, 2) is sector (22, 10), grass.
        Assert.AreEqual(data.PictureSize * data.PictureSize * 4, data.Picture.Length);
        Assert.AreEqual(new WorldMapPixel(18, 10), AreaWorldMap.PixelOf(4, 2));
        var at = (2 * data.PictureSize + 4) * 4;
        CollectionAssert.AreEqual(new byte[] { 110, 150, 80, 255 }, data.Picture[at..(at + 4)]);
    }
}

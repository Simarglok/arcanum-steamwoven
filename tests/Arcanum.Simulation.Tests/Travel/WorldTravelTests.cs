using Arcanum.Simulation.Travel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Travel.WorldTravelGeometry;

namespace Arcanum.Simulation.Tests.Travel;

// ARC-WORLD-056: world-map travel rules (ledger WT3–WT12). Maps, areas and blocks are invented; one pixel check uses the
// location of an installed area as a number.
[TestClass]
public sealed class WorldTravelTests
{
    private const long Width = 2000;
    private static readonly TravelRuleProfile Steamwoven = TravelRuleProfile.Steamwoven, Original = TravelRuleProfile.Original;
    private static TravelLocation Tile(long sx, long sy) => Centre(new(sx, sy));
    private static bool Open(TravelSector s) => s.X is >= 0 and < Width && s.Y is >= 0 and < Width;

    [TestMethod]
    public void A_pixel_is_a_sector_centre_with_x_mirrored()
    {
        Assert.AreEqual(new TravelLocation(64 * 1995 + 32, 64 * 7 + 32), Location(new(5, 7), Width));
        Assert.AreEqual(new WorldMapPixel(5, 7), Pixel(Location(new(5, 7), Width), Width));
        Assert.AreEqual(new WorldMapPixel(Width - 1605, 1826), Pixel(new(102768, 116894), Width));
        Assert.AreEqual(5L, Distance(new(0, 0), new(-5, 3)));
    }

    [TestMethod]
    public void The_direction_keeps_the_minor_axis_only_from_half_the_major()
    {
        TravelSector O = new(10, 10);
        Assert.AreEqual(5, Direction(O, new(20, 10)));
        Assert.AreEqual(5, Direction(O, new(20, 14))); // 2 × 4 < 10: the minor axis drops
        Assert.AreEqual(4, Direction(O, new(20, 15))); // 2 × 5 ≥ 10
        Assert.AreEqual(4, Direction(O, new(15, 15)));
        Assert.AreEqual(0, Direction(O, new(5, 5)));
        Assert.AreEqual(7, Direction(O, new(10, 0)));
        Assert.AreEqual(1, Direction(O, new(0, 10)));
        Assert.AreEqual(2, Direction(O, new(0, 20)));
        Assert.AreEqual(6, Direction(O, new(20, 0)));
        Assert.AreEqual(3, Direction(O, new(11, 20)));
        // Odd majors: 2 × 1 < 3 drops the minor axis, 2 × 2 ≥ 3 keeps it (the original doubles the deltas first).
        Assert.AreEqual(3, Direction(O, new(11, 13)));
        Assert.AreEqual(4, Direction(O, new(12, 13)));
        Assert.AreEqual(3, Direction(O, new(12, 15))); // 2 × 2 < 5
        Assert.AreEqual(4, Direction(O, new(13, 15))); // 2 × 3 ≥ 5
        Assert.AreEqual(5, Direction(O, O));
    }

    [TestMethod]
    public void Discovery_differs_where_a_nearer_area_lies_outside_its_own_radius()
    {
        // Area 1 covers the point (distance 200 < 320); area 2 is nearer (100) but its radius is 64.
        TravelArea[] areas = [new(new(0, 0), 0), new(new(1200, 1000), 320), new(new(1100, 1000), 64), new(new(90000, 90000), 320)];
        var point = new TravelLocation(1000, 1000);
        Assert.AreEqual(1, AreaInRadius(areas, point, arrival: false, Steamwoven));
        // The original then checks area 2 against its own radius in the last comparison: nothing is discovered.
        Assert.AreEqual(0, AreaInRadius(areas, point, arrival: false, Original));
        // When the replacing area is the table's last, only the earlier best's radius counts: it is found outside its own.
        Assert.AreEqual(2, AreaInRadius(areas[..3], point, arrival: false, Original));
        // A negative radius is never a match, unless at arrival (64 tiles).
        TravelArea[] hidden = [new(new(0, 0), 0), new(new(1000, 1030), -64)];
        Assert.AreEqual(0, AreaInRadius(hidden, point, arrival: false, Steamwoven));
        Assert.AreEqual(1, AreaInRadius(hidden, point, arrival: true, Original));
        Assert.AreEqual(2, NearestKnownArea(areas, a => true, point, 320));
        Assert.AreEqual(1, NearestKnownArea(areas, a => a == 1, point, 320));
        Assert.AreEqual(0, NearestKnownArea(areas, a => a == 1, point, 200));
    }

    [TestMethod]
    public void A_route_goes_straight_and_detours_around_a_blocked_sector()
    {
        var route = new WorldRoute();
        var plan = route.Plan(new(10, 10), new(15, 10), 0, Open, Steamwoven);
        Assert.AreEqual(new TravelSegmentPlan(true, 0, 5), plan);
        for (var i = 0; i < 5; i++) Assert.AreEqual(5, route.Step(i));
        // A wall at x = 12, y 8..12 forces a detour within the 16 × 16 window.
        bool Wall(TravelSector s) => Open(s) && !(s.X == 12 && s.Y is >= 8 and <= 12);
        var detour = route.Plan(new(10, 10), new(15, 10), 0, Wall, Steamwoven);
        Assert.IsTrue(detour.Planned);
        var at = new TravelSector(10, 10);
        for (var i = 0; i < detour.Steps; i++) { at = Neighbor(at, route.Step(i)); Assert.IsTrue(Wall(at), $"step {i} at {at}"); }
        Assert.AreEqual(new TravelSector(15, 10), at);
        // The original counts one step more after the detour; the extra byte is whatever the buffer held.
        var original = new WorldRoute().Plan(new(10, 10), new(15, 10), 0, Wall, Original);
        Assert.AreEqual(detour.Steps + 1, original.Steps);
        // A blocked destination, the same sector and a detour farther than 7 sectors refuse.
        Assert.IsFalse(route.Plan(new(10, 10), new(12, 10), 0, Wall, Steamwoven).Planned);
        Assert.IsFalse(route.Plan(new(10, 10), new(10, 10), 0, Open, Steamwoven).Planned);
        bool LongWall(TravelSector s) => Open(s) && !(s.X is >= 12 and <= 20 && s.Y is >= 0 and <= 30);
        Assert.IsFalse(route.Plan(new(10, 10), new(25, 10), 0, LongWall, Steamwoven).Planned);
        // At the buffer's end the original's extra step would pass it: the segment is refused, not written past the end.
        var tight = WorldRoute.BufferSteps - detour.Steps - 2;
        Assert.IsTrue(new WorldRoute().Plan(new(10, 10), new(15, 10), tight, Wall, Steamwoven).Planned);
        var end = WorldRoute.BufferSteps - detour.Steps;
        Assert.IsTrue(new WorldRoute().Plan(new(10, 10), new(15, 10), end, Wall, Steamwoven).Planned);
        Assert.IsFalse(new WorldRoute().Plan(new(10, 10), new(15, 10), end, Wall, Original).Planned);
    }

    [TestMethod]
    public void A_journey_takes_an_hour_per_sector_and_lands_on_an_area()
    {
        TravelArea[] areas = [new(new(0, 0), 0), new(Tile(20, 10), 320)];
        var journey = new WorldJourney(new WorldRoute(), areas, Width, Pixel(Tile(10, 10), Width));
        Assert.AreEqual(TravelWaypointRefusal.None, journey.AddWaypoint(Pixel(Tile(20, 10), Width), a => false, Open, Steamwoven));
        var known = new HashSet<int>();
        var results = new List<TravelStepResult>();
        while (journey.Waypoints.Count > 0)
        {
            var step = journey.Step(known.Contains, Steamwoven);
            if (step.Discovered > 0) known.Add(step.Discovered);
            results.Add(step);
        }
        Assert.AreEqual(10, results.Count);
        Assert.IsTrue(results.All(r => r.GameMs == WorldJourney.MsPerSector));
        // Discovered when the sector centre comes within 320 tiles: 4 sectors out, on the sixth step.
        Assert.AreEqual(5, results.FindIndex(r => r.Discovered == 1));
        Assert.AreEqual(areas[1].Location, results[^1].Landing);
        // The map position takes the landing, so a next waypoint starts there.
        Assert.AreEqual(Pixel(areas[1].Location, Width), journey.Pixel);
        // The original adds no time on the segment's last step.
        var original = new WorldJourney(new WorldRoute(), areas, Width, Pixel(Tile(10, 10), Width));
        original.AddWaypoint(Pixel(Tile(20, 10), Width), a => false, Open, Original);
        long total = 0;
        while (original.Waypoints.Count > 0) total += original.Step(a => false, Original).GameMs;
        Assert.AreEqual(9 * WorldJourney.MsPerSector, total);
    }

    [TestMethod]
    public void Waypoints_snap_to_a_known_area_and_stop_at_thirty()
    {
        TravelArea[] areas = [new(new(0, 0), 0), new(Tile(30, 30) with { X = Tile(30, 30).X + 100 }, 320)];
        var journey = new WorldJourney(new WorldRoute(), areas, Width, Pixel(Tile(10, 10), Width));
        journey.AddWaypoint(Pixel(Tile(30, 30), Width), a => a == 1, Open, Steamwoven);
        Assert.AreEqual(areas[1].Location, journey.Waypoints[0].End);
        var free = new WorldJourney(new WorldRoute(), areas, Width, Pixel(Tile(10, 10), Width));
        for (var i = 0; i < WorldRoute.MaxWaypoints; i++)
            Assert.AreEqual(TravelWaypointRefusal.None, free.AddWaypoint(Pixel(Tile(11 + i, 10 + (i % 2)), Width), a => false, Open, Steamwoven));
        Assert.AreEqual(TravelWaypointRefusal.TooMany, free.AddWaypoint(Pixel(Tile(100, 100), Width), a => false, Open, Steamwoven));
    }

    [TestMethod]
    public void A_cancel_snaps_only_in_steamwoven()
    {
        TravelArea[] areas = [new(new(0, 0), 0), new(Tile(12, 10) with { X = Tile(12, 10).X + 20 }, 320)];
        var journey = new WorldJourney(new WorldRoute(), areas, Width, Pixel(Tile(10, 10), Width));
        journey.AddWaypoint(Pixel(Tile(20, 10), Width), a => false, Open, Steamwoven);
        journey.Step(a => false, Steamwoven); journey.Step(a => false, Steamwoven);
        var here = Location(journey.Pixel, Width);
        Assert.AreEqual(here, new WorldJourney(new WorldRoute(), areas, Width, journey.Pixel).Cancel(Original));
        Assert.AreEqual(areas[1].Location, journey.Cancel(Steamwoven));
        Assert.AreEqual(Pixel(areas[1].Location, Width), journey.Pixel);
        Assert.AreEqual(0, journey.Waypoints.Count);
    }
}

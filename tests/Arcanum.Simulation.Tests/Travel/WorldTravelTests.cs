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

    private static WorldJourney ThreeStops(TravelRuleProfile rules, TravelSectorPasses passes, params (long X, long Y)[] stops)
    {
        var journey = new WorldJourney(new WorldRoute(), [new(new(0, 0), 0)], Width, Pixel(Tile(10, 10), Width));
        foreach (var (x, y) in stops) Assert.AreEqual(TravelWaypointRefusal.None, journey.AddWaypoint(Pixel(Tile(x, y), Width), a => false, passes, rules));
        return journey;
    }

    private static int StepsToWalk(WorldJourney journey, TravelRuleProfile rules)
    {
        var steps = 0;
        while (journey.Waypoints.Count > 0) { journey.Step(a => false, rules); steps++; }
        return steps;
    }

    [TestMethod]
    public void The_original_keeps_the_steps_of_a_deleted_middle_waypoint_and_steamwoven_plans_again()
    {
        // WM97 (A5): (10,10) → (20,10) → (20,20); deleting the middle stop. The original's later segment still holds the steps planned
        // from the deleted stop, so the walk goes through it (20 steps); Steamwoven plans the diagonal (10 steps).
        var original = ThreeStops(Original, Open, (20, 10), (20, 20));
        Assert.AreEqual(TravelWaypointRefusal.None, original.RemoveWaypoint(0, Open, Original));
        Assert.AreEqual(1, original.Waypoints.Count);
        Assert.AreEqual(20, StepsToWalk(original, Original));
        var fixedOne = ThreeStops(Steamwoven, Open, (20, 10), (20, 20));
        Assert.AreEqual(TravelWaypointRefusal.None, fixedOne.RemoveWaypoint(0, Open, Steamwoven));
        Assert.AreEqual(1, fixedOne.Waypoints.Count);
        Assert.AreEqual(0, fixedOne.Waypoints[0].Start);
        Assert.AreEqual(10, StepsToWalk(fixedOne, Steamwoven));
        Assert.AreEqual(Pixel(Tile(20, 20), Width), Pixel(Location(fixedOne.Pixel, Width), Width));
    }

    [TestMethod]
    public void Deleting_the_last_waypoint_frees_its_steps_in_both_settings()
    {
        foreach (var rules in new[] { Original, Steamwoven })
        {
            var journey = ThreeStops(rules, Open, (20, 10), (20, 20));
            var first = journey.Waypoints[0];
            Assert.AreEqual(TravelWaypointRefusal.None, journey.RemoveWaypoint(1, Open, rules));
            Assert.AreEqual(1, journey.Waypoints.Count);
            Assert.AreEqual(TravelWaypointRefusal.None, journey.AddWaypoint(Pixel(Tile(20, 15), Width), a => false, Open, rules));
            Assert.AreEqual(first.Start + first.Steps, journey.Waypoints[1].Start, rules.Name);
            // An index outside the list, and the last waypoint of a list of one, are handled quietly.
            Assert.AreEqual(TravelWaypointRefusal.None, journey.RemoveWaypoint(7, Open, rules));
            Assert.AreEqual(2, journey.Waypoints.Count);
            // The step position is reset only when the list becomes empty (WM97).
            journey.Step(a => false, rules);
            Assert.AreEqual(1, journey.CurrentStep);
            journey.RemoveWaypoint(1, Open, rules);
            Assert.AreEqual(1, journey.CurrentStep, rules.Name);
            journey.RemoveWaypoint(0, Open, rules);
            Assert.AreEqual(0, journey.Waypoints.Count);
            Assert.AreEqual(0, journey.CurrentStep, rules.Name);
        }
    }

    [TestMethod]
    public void Steamwoven_refuses_a_deletion_whose_following_segments_have_no_route()
    {
        // A wall at x = 15 with a one-sector gap at y = 40: (10,10) → (10,40) → (20,40) goes through the gap, but (10,10) → (20,40) runs into the wall.
        bool Wall(TravelSector s) => Open(s) && (s.X != 15 || s.Y == 40);
        var kept = ThreeStops(Steamwoven, Wall, (10, 40), (20, 40));
        var before = kept.Waypoints.ToArray();
        Assert.AreEqual(TravelWaypointRefusal.NoRoute, kept.RemoveWaypoint(0, Wall, Steamwoven));
        CollectionAssert.AreEqual(before, kept.Waypoints.ToArray());
        // The original only shifts its list and walks on through the deleted stop.
        var original = ThreeStops(Original, Wall, (10, 40), (20, 40));
        Assert.AreEqual(TravelWaypointRefusal.None, original.RemoveWaypoint(0, Wall, Original));
        Assert.AreEqual(1, original.Waypoints.Count);
        Assert.AreEqual(30 + 10, StepsToWalk(original, Original));
    }

    [TestMethod]
    public void Steamwoven_drops_a_later_waypoint_that_lies_where_the_walk_would_start_and_keeps_a_journey_under_way()
    {
        // (10,10) → (20,10) → (10,10)'s sector again: after the middle stop goes, the last has nothing left to walk to and goes with it.
        var journey = ThreeStops(Steamwoven, Open, (20, 10), (20, 20), (10, 10));
        Assert.AreEqual(TravelWaypointRefusal.None, journey.RemoveWaypoint(0, Open, Steamwoven));
        Assert.AreEqual(2, journey.Waypoints.Count);
        var same = ThreeStops(Steamwoven, Open, (20, 10), (10, 10));
        Assert.AreEqual(TravelWaypointRefusal.None, same.RemoveWaypoint(0, Open, Steamwoven));
        Assert.AreEqual(0, same.Waypoints.Count, "the map position's own sector needs no waypoint");
        // A journey under way refuses a re-plan from the middle (the host only removes the last one then).
        var walking = ThreeStops(Steamwoven, Open, (20, 10), (20, 20));
        walking.Step(a => false, Steamwoven);
        Assert.AreEqual(TravelWaypointRefusal.NoRoute, walking.RemoveWaypoint(0, Open, Steamwoven));
        Assert.AreEqual(TravelWaypointRefusal.None, walking.RemoveWaypoint(1, Open, Steamwoven));
    }

    [TestMethod]
    public void A_dragged_waypoint_snaps_to_a_known_area_only_on_release()
    {
        // WM100: the click pixel stays until the snap; the nearest known area within 320 tiles then takes the location and the icon.
        TravelArea[] areas = [new(new(0, 0), 0), new(Tile(30, 30) with { X = Tile(30, 30).X + 100 }, 320)];
        var journey = new WorldJourney(new WorldRoute(), areas, Width, Pixel(Tile(10, 10), Width));
        var clicked = Pixel(Tile(30, 30), Width);
        journey.AddWaypoint(clicked, a => false, Open, Steamwoven);
        Assert.AreEqual(clicked, journey.Waypoints[0].Pixel);
        Assert.AreEqual(Tile(30, 30), journey.Waypoints[0].End);
        Assert.IsFalse(journey.SnapWaypoint(0, a => false), "no known area near");
        Assert.IsFalse(journey.SnapWaypoint(5, a => true), "outside the list");
        Assert.IsTrue(journey.SnapWaypoint(0, a => a == 1));
        Assert.AreEqual(areas[1].Location, journey.Waypoints[0].End);
        Assert.AreEqual(Pixel(areas[1].Location, Width), journey.Waypoints[0].Pixel);
        Assert.IsFalse(journey.SnapWaypoint(0, a => a == 1), "already on it");
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

    // ARC-DEV-178 (ledger GA3): the nearest area (0x4CB220) and the area at a location (0x4CB630). Areas, sectors and town maps are invented.
    private static TravelArea At(long x, long y) => new(new(x, y), 100);

    [TestMethod]
    public void The_nearest_area_starts_at_area_zero_and_a_later_area_must_be_strictly_nearer()
    {
        // Area 0 at the origin, areas 1 and 2 equally far from (150, 150), area 3 far away.
        var areas = new[] { At(0, 0), At(200, 100), At(100, 200), At(500, 500) };
        Assert.AreEqual(0, NearestArea(areas, new(0, 10)), "area 0 is the first candidate");
        Assert.AreEqual(1, NearestArea(areas, new(150, 150)), "areas 1 and 2 tie at 50: the earlier stays");
        Assert.AreEqual(2, NearestArea(areas, new(120, 180)));
        Assert.AreEqual(0, NearestArea(areas, new(100, 100)), "areas 0 and 1 tie at 100: area 0 stays");
        Assert.AreEqual(0, NearestArea(areas, new(50, 50)));
        Assert.AreEqual(3, NearestArea(areas, new(490, 510)));
        Assert.AreEqual(0, NearestArea([], new(5, 5)), "no table: area 0 at the origin");
        Assert.AreEqual(5L, Distance(new(0, 0), new(-5, 3)), "the Chebyshev distance of the original");
    }

    [TestMethod]
    public void The_area_at_a_location_needs_the_sectors_town_map_to_equal_the_areas_own()
    {
        // Area 1 stands in sector (1, 1) (town map 7), area 2 in sector (5, 5) (town map 7), area 3 in sector (9, 9) (town map 0).
        var areas = new[] { At(0, 0), At(70, 70), At(330, 330), At(590, 590) };
        var townMaps = new Dictionary<TravelSector, int> { [new(0, 0)] = 0, [new(1, 1)] = 7, [new(2, 2)] = 7, [new(5, 5)] = 7, [new(9, 9)] = 0, [new(3, 3)] = 8 };
        int? Read(TravelSector sector) => townMaps.TryGetValue(sector, out var value) ? value : null;

        Assert.AreEqual(1, AreaAt(areas, new(130, 130), Read), "sector (2, 2) has town map 7, the nearest area 1's sector too");
        Assert.AreEqual(0, AreaAt(areas, new(200, 200), Read), "sector (3, 3) has town map 8: not area 1's");
        Assert.AreEqual(2, AreaAt(areas, new(320, 330), Read), "sector (5, 5) has town map 7: area 2 is nearest and shares it");
        Assert.AreEqual(0, AreaAt(areas, new(10, 10), Read), "sector (0, 0) has no town map");
        Assert.AreEqual(0, AreaAt(areas, new(590, 590), Read), "sector (9, 9) has no town map");
        // Area 0 as the nearest never counts, whatever the town maps are.
        Assert.AreEqual(0, AreaAt([At(130, 130), At(900, 900)], new(130, 130), Read));
        // An area whose own sector has no town map gives 0.
        Assert.AreEqual(0, AreaAt([At(9000, 9000), At(590, 590)], new(130, 130), Read));
    }

    [TestMethod]
    public void The_area_at_a_location_asks_for_a_sector_the_caller_cannot_read_and_stays_unanswered()
    {
        var areas = new[] { At(0, 0), At(70, 70) };
        Assert.IsNull(AreaAt(areas, new(130, 130), _ => null), "the location's own sector is unknown");
        Assert.IsNull(AreaAt(areas, new(130, 130), s => s == new TravelSector(2, 2) ? 7 : null), "the area's sector is unknown");
        Assert.AreEqual(0, AreaAt(areas, new(130, 130), s => s == new TravelSector(2, 2) ? 0 : null), "no town map here: nothing else is asked");
    }
}

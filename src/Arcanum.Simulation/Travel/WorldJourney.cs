namespace Arcanum.Simulation.Travel;

/// <summary>One waypoint of a journey: its segment in the route buffer and where the segment ends (snapped to a known area).</summary>
public readonly record struct TravelWaypoint(int Start, int Steps, TravelLocation End, WorldMapPixel Pixel);

public enum TravelWaypointRefusal { None, TooMany, NoRoute }

/// <summary>What one travel step did: the map position after it, the game time it adds, the area it discovered (0 for none),
/// whether it ended a segment, and the landing location when the journey ends with it.</summary>
public readonly record struct TravelStepResult(WorldMapPixel Pixel, TravelLocation Location, long GameMs, int Discovered, bool SegmentEnded,
    TravelLocation? Landing);

/// <summary>
/// ARC-WORLD-056: a world-map journey of the retail executable (ledger WT5, WT10–WT12): waypoints whose segments share one
/// <see cref="WorldRoute"/> buffer, and the travel step the original runs every 62 ms of real time (<see cref="StepRealMs"/>).
/// The host supplies the areas, whether the PC knows an area, and whether a sector passes, and applies what a step returns
/// (the clock, the discovery, the landing teleport).
/// </summary>
public sealed class WorldJourney
{
    /// <summary>The real time between travel steps (the WorldMap event's delay).</summary>
    public const long StepRealMs = 62;
    /// <summary>A waypoint within this many tiles of a known area ends on it (0x66D850).</summary>
    public const long SnapDistance = 320;
    /// <summary>Game time per sector of distance: one hour.</summary>
    public const long MsPerSector = 3_600_000;

    private readonly List<TravelWaypoint> _waypoints = new();
    private readonly IReadOnlyList<TravelArea> _areas;
    private readonly long _width;
    private int _step;

    public WorldJourney(WorldRoute route, IReadOnlyList<TravelArea> areas, long width, WorldMapPixel pixel)
    {
        Route = route ?? throw new ArgumentNullException(nameof(route));
        _areas = areas ?? throw new ArgumentNullException(nameof(areas));
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        _width = width; Pixel = pixel;
    }

    public WorldRoute Route { get; }
    /// <summary>The map position (the original's icon pixel).</summary>
    public WorldMapPixel Pixel { get; private set; }
    public IReadOnlyList<TravelWaypoint> Waypoints => _waypoints.AsReadOnly();
    public long Width => _width;
    /// <summary>The route position the next travel step reads (0 before a journey).</summary>
    public int CurrentStep => _step;

    /// <summary>
    /// Adds a waypoint at a clicked pixel (0x5643E0, world-map mode): at most <see cref="WorldRoute.MaxWaypoints"/>; the segment
    /// starts where the last one ends (the map position for the first) and ends at the pixel's sector centre, or at a known area
    /// within <see cref="SnapDistance"/> of it (0x564970).
    /// </summary>
    [OriginalFunction(0x5643E0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x564970, OriginalFunctionScope.Full)]
    public TravelWaypointRefusal AddWaypoint(WorldMapPixel clicked, Func<int, bool> known, TravelSectorPasses passes, TravelRuleProfile rules)
    {
        ArgumentNullException.ThrowIfNull(known); ArgumentNullException.ThrowIfNull(passes); ArgumentNullException.ThrowIfNull(rules);
        if (_waypoints.Count >= WorldRoute.MaxWaypoints) return TravelWaypointRefusal.TooMany;
        var (from, offset) = _waypoints.Count == 0 ? (WorldTravelGeometry.Location(Pixel, _width), 0)
            : (_waypoints[^1].End, _waypoints[^1].Start + _waypoints[^1].Steps);
        var end = WorldTravelGeometry.Location(clicked, _width);
        if (WorldTravelGeometry.NearestKnownArea(_areas, known, end, SnapDistance) is var area and > 0) end = _areas[area].Location;
        var plan = Route.Plan(WorldTravelGeometry.Sector(from), WorldTravelGeometry.Sector(end), offset, passes, rules);
        if (!plan.Planned) return TravelWaypointRefusal.NoRoute;
        _waypoints.Add(new(plan.Start, plan.Steps, end, clicked));
        return TravelWaypointRefusal.None;
    }

    /// <summary>
    /// One travel step (0x564C20): the map position moves one route direction to the next sector centre, the segment's last
    /// step to its end; the new sector centre discovers an area (WT3); a finished segment drops its waypoint and, with none
    /// left, lands (WT12) and the map position takes the landing; unless the original's segment end (A3) applies, the step adds ⌊distance / 64⌋ hours (1 ms for no
    /// sector). <paramref name="continueJourney"/> is the original's check before the next segment (0x565140): when it
    /// refuses, every waypoint is dropped and the PC lands.
    /// </summary>
    [OriginalFunction(0x564C20, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x565170, OriginalFunctionScope.Full)]
    [OriginalFunction(0x564EE0, OriginalFunctionScope.Full)]
    public TravelStepResult Step(Func<int, bool> known, TravelRuleProfile rules, Func<bool>? continueJourney = null)
    {
        ArgumentNullException.ThrowIfNull(known); ArgumentNullException.ThrowIfNull(rules);
        if (_waypoints.Count == 0) throw new InvalidOperationException("TRAVEL_NO_WAYPOINT");
        var old = Pixel;
        var segment = _waypoints[0];
        var next = WorldTravelGeometry.Neighbor(WorldTravelGeometry.Sector(WorldTravelGeometry.Location(Pixel, _width)), Route.Step(_step));
        _step++;
        var ended = _step >= segment.Start + segment.Steps;
        Pixel = ended ? WorldTravelGeometry.Pixel(segment.End, _width) : WorldTravelGeometry.Pixel(WorldTravelGeometry.Centre(next), _width);
        var location = WorldTravelGeometry.Location(Pixel, _width);
        var area = WorldTravelGeometry.AreaInRadius(_areas, location, arrival: false, rules);
        var discovered = area > 0 && !known(area) ? area : 0;
        TravelLocation? landing = null;
        if (ended)
        {
            _waypoints.RemoveAt(0);
            if (_waypoints.Count > 0 && continueJourney?.Invoke() == false) _waypoints.Clear();
            if (_waypoints.Count == 0) { _step = 0; landing = Landing(location, rules, cancel: false); Pixel = WorldTravelGeometry.Pixel(landing.Value, _width); }
        }
        var sectors = WorldTravelGeometry.Distance(WorldTravelGeometry.Location(old, _width), location) / 64;
        var ms = ended && rules.SegmentEndAddsNoTime ? 0 : Math.Max(1, sectors * MsPerSector);
        return new(Pixel, location, ms, discovered, ended, landing);
    }

    /// <summary>Ends the journey where it stands (leaving travel, 0x5615D0): the landing of the map position, without the area
    /// snap under the original's cancel (A4). The map position takes the landing (WT12).</summary>
    public TravelLocation Cancel(TravelRuleProfile rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _waypoints.Clear(); _step = 0;
        var landing = Landing(WorldTravelGeometry.Location(Pixel, _width), rules, cancel: true);
        Pixel = WorldTravelGeometry.Pixel(landing, _width);
        return landing;
    }

    /// <summary>The landing location (0x5649F0): an area whose radius (64 tiles for 0 or below) covers the location, else the
    /// location itself.</summary>
    [OriginalFunction(0x5649F0, OriginalFunctionScope.Partial)]
    public TravelLocation Landing(TravelLocation location, TravelRuleProfile rules, bool cancel)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (cancel && rules.CancelSkipsAreaSnap) return location;
        return WorldTravelGeometry.AreaInRadius(_areas, location, arrival: true, rules) is var area and > 0 ? _areas[area].Location : location;
    }
}

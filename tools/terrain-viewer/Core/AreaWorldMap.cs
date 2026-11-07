using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Session;
using Arcanum.Simulation.Travel;

namespace Arcanum.TerrainViewer.Core;

public enum AreaWorldMapDataStatus { Ready, ModuleOutsideGameRoot, MountFailed, StackFailed, NoStartMap, TerrainFailed, TypesFailed, AreasFailed }

/// <summary>
/// The world-map data of the module the area was read from (ARC-UI-048): the area table (ARC-WORLD-056), the start map (the
/// map list's runtime map 1, the map the world map is keyed to) with its travel sectors, and a picture of the map drawn from
/// its terrain grid, one picture pixel per <see cref="AreaWorldMap.PictureScale"/> × <see cref="AreaWorldMap.PictureScale"/>
/// world-map pixels. The picture is the project's: the original's world-map art is not drawn.
/// </summary>
public sealed class AreaWorldMapData
{
    public AreaWorldMapData(string startMap, int startRuntime, WorldAreaTable areas, WorldTravelSectors sectors, long width, int pictureSize, byte[] picture,
        IReadOnlyDictionary<TravelSector, int>? areaTownMaps = null)
    {
        ArgumentNullException.ThrowIfNull(startMap);
        ArgumentNullException.ThrowIfNull(areas);
        ArgumentNullException.ThrowIfNull(sectors);
        ArgumentNullException.ThrowIfNull(picture);
        if (width <= 0 || pictureSize <= 0 || picture.Length != checked(pictureSize * pictureSize * 4)) throw new ArgumentOutOfRangeException(nameof(picture));
        StartMap = startMap; StartRuntime = startRuntime; Areas = areas; Sectors = sectors; Width = width; PictureSize = pictureSize; Picture = picture;
        TravelAreas = Array.AsReadOnly(areas.Areas.Select(a => new TravelArea(new(a.Location.X, a.Location.Y), a.Radius)).ToArray());
        AreaTownMaps = areaTownMaps ?? new Dictionary<TravelSector, int>();
    }

    /// <summary>ARC-DEV-178 (ledger GA3): the town map of the sector each area's location lies in (the sector's metadata field 0x4640; 0 for a
    /// sector without its own file), for the area at a location (<see cref="WorldTravelGeometry.AreaAt"/>). Keyed by sector.</summary>
    public IReadOnlyDictionary<TravelSector, int> AreaTownMaps { get; }

    /// <summary>The start map's folder name and runtime number.</summary>
    public string StartMap { get; }
    public int StartRuntime { get; }
    public WorldAreaTable Areas { get; }
    public IReadOnlyList<TravelArea> TravelAreas { get; }
    public WorldTravelSectors Sectors { get; }
    /// <summary>The start map's width in sectors (the original's W).</summary>
    public long Width { get; }
    /// <summary>The picture's side in pixels and its RGBA bytes, row by row.</summary>
    public int PictureSize { get; }
    public byte[] Picture { get; }

    public bool Passes(TravelSector sector) => Sectors.Passes(sector.X, sector.Y);
}

public sealed record AreaWorldMapDataLoad(AreaWorldMapDataStatus Status, AreaWorldMapData? Data, string Detail);

/// <summary>Why a waypoint, a journey or a debug teleport was refused: none, the journey is running, no waypoints, too many, no route
/// (the original's message 601), a pixel off the map, the PC not on the start map (ARC-DEV-050: a debug-mode view opened elsewhere),
/// or an area without a location on the map.</summary>
public enum AreaWorldMapRefusal { None, Travelling, NoWaypoints, TooMany, NoRoute, OffMap, NotOnStartMap, NoLocation }

/// <summary>What one travel step did to the session: the step, the game time applied, and the area it made known (0 for none).</summary>
public sealed record AreaWorldMapStep(TravelStepResult Step, long GameMs, int Discovered);

/// <summary>
/// ARC-UI-048: the viewer's world-map journey over the rules of ARC-WORLD-056, Godot-free. It holds one
/// <see cref="WorldJourney"/> from the PC's location on the start map, adds waypoints at a picture pixel or an area, and runs
/// the travel step the host calls every <see cref="WorldJourney.StepRealMs"/>: the step's game time goes to the host's clock,
/// a discovered area becomes known in the session and the session's last discovered area is cleared again (ledger WT3). The
/// rules setting is the session's (<see cref="GameRules.Travel"/>). The journey itself is not saved (as the original's lives
/// in its interface).
/// </summary>
public sealed class AreaWorldMap
{
    /// <summary>World-map pixels per picture pixel on each axis: a 2000-pixel map draws as 500 × 500.</summary>
    public const int PictureScale = 4;
    /// <summary>The longest map side the picture admits (project policy; the installed start map is 2000).</summary>
    public const long MaxWidth = 4096;

    private readonly AreaWorldMapData _data;
    private readonly GameSessionOwner _session;
    private WorldJourney _journey;

    /// <summary>A journey from <paramref name="start"/>, the PC's location on the start map. ARC-DEV-050: without a
    /// <paramref name="start"/> (debug mode on another map) the view has no position, plans no journey and only teleports.</summary>
    public AreaWorldMap(AreaWorldMapData data, GameSessionOwner session, TravelLocation? start)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        OnStartMap = start is not null;
        _journey = new(new WorldRoute(), data.TravelAreas, data.Width, WorldTravelGeometry.Pixel(start ?? default, data.Width));
    }

    /// <summary>The PC stands on the start map, so the view has a position and plans journeys.</summary>
    public bool OnStartMap { get; }

    public AreaWorldMapData Data => _data;
    /// <summary>The session the journey writes to.</summary>
    public GameSessionOwner Session => _session;
    public WorldMapPixel Position => _journey.Pixel;
    public IReadOnlyList<TravelWaypoint> Waypoints => _journey.Waypoints;
    public bool Travelling { get; private set; }
    /// <summary>The landing location of a finished journey, or null.</summary>
    public TravelLocation? Landing { get; private set; }
    /// <summary>Steps taken and game hours applied by the running or last journey.</summary>
    public int Steps { get; private set; }
    public long GameMs { get; private set; }
    private TravelRuleProfile Rules => _session.Rules.Travel;

    /// <summary>The world-map pixel at the centre of a picture pixel.</summary>
    public static WorldMapPixel PixelOf(int pictureX, int pictureY) => new(pictureX * (long)PictureScale + PictureScale / 2, pictureY * (long)PictureScale + PictureScale / 2);

    /// <summary>Adds a waypoint at a world-map pixel (the original's click, WT5): a known area within 320 tiles takes its place.</summary>
    public AreaWorldMapRefusal AddWaypoint(WorldMapPixel pixel)
    {
        if (!OnStartMap) return AreaWorldMapRefusal.NotOnStartMap;
        if (Travelling) return AreaWorldMapRefusal.Travelling;
        if (pixel.X < 1 || pixel.X > _data.Width || pixel.Y < 0 || pixel.Y >= _data.Width) return AreaWorldMapRefusal.OffMap;
        Landing = null;
        return _journey.AddWaypoint(pixel, _session.IsAreaKnown, _data.Passes, Rules) switch
        {
            TravelWaypointRefusal.None => AreaWorldMapRefusal.None,
            TravelWaypointRefusal.TooMany => AreaWorldMapRefusal.TooMany,
            _ => AreaWorldMapRefusal.NoRoute,
        };
    }

    /// <summary>Adds a waypoint at an area's pixel (a diagnostic shortcut for the click on it).</summary>
    public AreaWorldMapRefusal AddArea(int area)
    {
        if (area < 1 || area >= _data.TravelAreas.Count) throw new ArgumentOutOfRangeException(nameof(area));
        return AddWaypoint(WorldTravelGeometry.Pixel(_data.TravelAreas[area].Location, _data.Width));
    }

    /// <summary>
    /// ARC-DEV-050, debug mode only (the host never offers it otherwise): the start-map location to teleport the PC to for an area,
    /// known or not, at once. The view and the session are not changed: the host lands there as a journey's landing does, with no
    /// game time and no discovered area. Refused during a journey and for an area that has no location on the map.
    /// </summary>
    public AreaWorldMapRefusal Teleport(int area, out TravelLocation landing)
    {
        if (area < 1 || area >= _data.TravelAreas.Count) throw new ArgumentOutOfRangeException(nameof(area));
        landing = _data.TravelAreas[area].Location;
        if (Travelling) return AreaWorldMapRefusal.Travelling;
        if (landing == default || landing.X >= _data.Width * 64 || landing.Y >= _data.Width * 64) return AreaWorldMapRefusal.NoLocation;
        Landing = null;
        return AreaWorldMapRefusal.None;
    }

    /// <summary>Drops every waypoint (only before the journey starts).</summary>
    public bool Clear()
    {
        if (Travelling || _journey.Waypoints.Count == 0) return false;
        _journey = new(new WorldRoute(), _data.TravelAreas, _data.Width, _journey.Pixel);
        return true;
    }

    public AreaWorldMapRefusal Start()
    {
        if (!OnStartMap) return AreaWorldMapRefusal.NotOnStartMap;
        if (Travelling) return AreaWorldMapRefusal.Travelling;
        if (_journey.Waypoints.Count == 0) return AreaWorldMapRefusal.NoWaypoints;
        Travelling = true; Landing = null; Steps = 0; GameMs = 0;
        return AreaWorldMapRefusal.None;
    }

    /// <summary>
    /// One travel step: the journey moves, <paramref name="advance"/> applies the game time (the host's clock and event passes),
    /// a newly discovered area becomes known and the last discovered record is cleared, and a finished journey sets
    /// <see cref="Landing"/>.
    /// </summary>
    public AreaWorldMapStep Step(Action<long> advance)
    {
        ArgumentNullException.ThrowIfNull(advance);
        if (!Travelling) throw new InvalidOperationException("AREA_WORLD_MAP_NOT_TRAVELLING");
        var step = _journey.Step(_session.IsAreaKnown, Rules);
        Steps++;
        if (step.GameMs > 0) { advance(step.GameMs); GameMs = checked(GameMs + step.GameMs); }
        var discovered = 0;
        if (step.Discovered > 0 && _session.DiscoverArea(_session.Version, step.Discovered).Disposition == GameSessionDisposition.Changed) discovered = step.Discovered;
        if (_session.QueryLastDiscoveredArea() != 0) _session.ClearLastDiscoveredArea(_session.Version);
        if (step.Landing is { } landing) { Travelling = false; Landing = landing; }
        return new(step, step.GameMs, discovered);
    }

    /// <summary>Stops a running journey where it stands (WT12, the setting's cancel landing) and returns the landing.</summary>
    public TravelLocation Cancel()
    {
        var landing = _journey.Cancel(Rules);
        Travelling = false; Landing = landing;
        return landing;
    }

    /// <summary>The route still ahead as world-map pixels, from the map position through every waypoint's remaining steps (for
    /// drawing); during a journey the first segment starts at the step the journey has reached.</summary>
    public IReadOnlyList<WorldMapPixel> RoutePixels()
    {
        var pixels = new List<WorldMapPixel> { _journey.Pixel };
        var sector = WorldTravelGeometry.Sector(WorldTravelGeometry.Location(_journey.Pixel, _data.Width));
        var first = true;
        foreach (var waypoint in _journey.Waypoints)
        {
            var from = first ? Math.Max(waypoint.Start, _journey.CurrentStep) : waypoint.Start;
            first = false;
            for (var i = from; i < waypoint.Start + waypoint.Steps - 1; i++)
            {
                var direction = _journey.Route.Step(i);
                if (direction is < 0 or > 7) break;
                sector = WorldTravelGeometry.Neighbor(sector, direction);
                pixels.Add(WorldTravelGeometry.Pixel(WorldTravelGeometry.Centre(sector), _data.Width));
            }
            pixels.Add(WorldTravelGeometry.Pixel(waypoint.End, _data.Width));
            sector = WorldTravelGeometry.Sector(waypoint.End);
        }
        return pixels;
    }

    /// <summary>The steps the waypoints hold, and about the game hours they take under the setting (the original's segment end
    /// takes none, A3; a last step that ends short of a sector, at a snapped area, takes 1 ms instead of an hour).</summary>
    public (int Steps, long Hours) Estimate()
    {
        var steps = _journey.Waypoints.Sum(w => w.Steps);
        return (steps, Rules.SegmentEndAddsNoTime ? steps - _journey.Waypoints.Count : steps);
    }

    /// <summary>Loads the world-map data through the game folder's content stack (<see cref="AreaContentRoot.Discover"/>).</summary>
    public static AreaWorldMapDataLoad LoadData(string modulePath, CancellationToken token = default)
    {
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready)
            return new(found.Status == AreaContentRootStatus.MountFailed ? AreaWorldMapDataStatus.MountFailed : AreaWorldMapDataStatus.ModuleOutsideGameRoot, null, found.Detail);
        var opened = ContentStack.Open(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return new(AreaWorldMapDataStatus.StackFailed, null, opened.Status.ToString());
        using var stack = opened.Stack!;
        var catalog = WorldCatalog.Open(stack);
        if (MapList.Read(stack).ByRuntime(1) is not { } entry || catalog.Find(entry.Name) is not { Properties: { } properties } map)
            return new(AreaWorldMapDataStatus.NoStartMap, null, "no runtime map 1 with properties");
        var width = properties.WidthTiles / 64;
        if (width <= 0 || width > MaxWidth || properties.HeightTiles != properties.WidthTiles)
            return new(AreaWorldMapDataStatus.NoStartMap, null, $"start map {width} sectors wide, not square or beyond {MaxWidth}");
        token.ThrowIfCancellationRequested();
        var terrain = catalog.ReadTerrain(map);
        if (terrain.Grid is not { } grid) return new(AreaWorldMapDataStatus.TerrainFailed, null, terrain.Issue?.Code.ToString() ?? "no grid");
        var types = TerrainTypeTable.Open(stack);
        if (types.Table is not { } table) return new(AreaWorldMapDataStatus.TypesFailed, null, types.Status.ToString());
        var areas = WorldAreaTable.Open(stack);
        if (areas.Table is not { } area) return new(AreaWorldMapDataStatus.AreasFailed, null, areas.Detail);
        token.ThrowIfCancellationRequested();
        var sectors = new WorldTravelSectors(map, grid, table);
        var size = checked((int)((width + PictureScale - 1) / PictureScale));
        var picture = Draw(width, size, grid, table, sectors);
        var townMaps = ReadAreaTownMaps(catalog, map, area, token);
        return new(AreaWorldMapDataStatus.Ready, new(map.Name, entry.Runtime, area, sectors, width, size, picture, townMaps),
            $"map {map.Name}, {width} sectors, areas {area.Areas.Count}");
    }

    /// <summary>ARC-DEV-178 (ledger GA3): the town map word of the sector each area's location lies in. The metadata and sound section's first
    /// word of a sector with its own file; 0 for a sector without one or whose file is refused (the original would generate it from the terrain
    /// grid; a generated wilderness sector holds no town map).</summary>
    internal static IReadOnlyDictionary<TravelSector, int> ReadAreaTownMaps(WorldCatalog catalog, WorldMap map, WorldAreaTable areas, CancellationToken token)
    {
        var townMaps = new Dictionary<TravelSector, int>();
        foreach (var area in areas.Areas)
        {
            token.ThrowIfCancellationRequested();
            var sector = WorldTravelGeometry.Sector(new(area.Location.X, area.Location.Y));
            if (townMaps.ContainsKey(sector)) continue;
            var townMap = 0;
            if (sector.X is >= 0 and <= int.MaxValue && sector.Y is >= 0 and <= int.MaxValue)
            {
                var read = catalog.ReadSector(map, new((int)sector.X, (int)sector.Y));
                if (read.Sector?.MetadataAndSound is { Count: > 0 } metadata) townMap = metadata[0];
            }
            townMaps.Add(sector, townMap);
        }
        return townMaps;
    }

    /// <summary>The picture: each pixel shows the terrain type of the sector at its centre, darker where travel is blocked, with
    /// the sectors that have their own file brightened.</summary>
    internal static byte[] Draw(long width, int size, WorldTerrainGrid grid, TerrainTypeTable types, WorldTravelSectors sectors)
    {
        var rgba = new byte[checked(size * size * 4)];
        var colours = types.Types.Select(t => Colour(t.Name)).ToArray();
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var pixel = PixelOf(x, y);
                var sector = WorldTravelGeometry.Sector(WorldTravelGeometry.Location(pixel, width));
                var (r, g, b) = (8, 8, 16);
                if (sector.X >= 0 && sector.Y >= 0 && sector.X < grid.Header.WidthSectors && sector.Y < grid.Header.HeightSectors)
                {
                    var word = new TerrainWord(grid[(int)sector.X, (int)sector.Y]);
                    if (word.Value != TerrainWord.OutsideGrid && word.Base < colours.Length) (r, g, b) = colours[word.Base];
                    if (!sectors.Passes(sector.X, sector.Y)) (r, g, b) = (r * 3 / 5, g * 3 / 5, b * 3 / 5);
                    else if (sectors.TerrainBlocked(sector.X, sector.Y)) (r, g) = (Math.Min(255, r + 70), Math.Min(255, g + 60));
                }
                var at = (y * size + x) * 4;
                rgba[at] = (byte)r; rgba[at + 1] = (byte)g; rgba[at + 2] = (byte)b; rgba[at + 3] = 255;
            }
        return rgba;
    }

    // A diagnostic colour by the type's folder name (the project's choice, not the original's map art).
    private static (int R, int G, int B) Colour(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("water")) return (40, 80, 160);
        if (n.Contains("mountain")) return n.Contains("snow") ? (200, 200, 210) : n.Contains("desert") ? (160, 130, 90) : (120, 110, 100);
        if (n.Contains("snow")) return (225, 230, 235);
        if (n.Contains("desert") || n.Contains("scorched")) return (205, 180, 120);
        if (n.Contains("swamp")) return (95, 110, 70);
        if (n.Contains("void")) return (110, 90, 120);
        if (n.Contains("jungle") || n.Contains("forest")) return (50, 110, 55);
        if (n.Contains("deforested")) return (130, 120, 80);
        return (110, 150, 80);
    }
}

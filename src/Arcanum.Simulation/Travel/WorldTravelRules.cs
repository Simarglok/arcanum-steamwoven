namespace Arcanum.Simulation.Travel;

/// <summary>A tile location of the start map (the original's location: x in the low, y in the high 32 bits).</summary>
public readonly record struct TravelLocation(long X, long Y);

/// <summary>A sector of the start map (64 × 64 tiles): the location's x and y shifted right by 6.</summary>
public readonly record struct TravelSector(long X, long Y);

/// <summary>A world-map pixel: one pixel is one sector, x mirrored (ledger WT6).</summary>
public readonly record struct WorldMapPixel(long X, long Y);

/// <summary>
/// The travel rule settings of owner decision D8 (contract docs/world/rule-modes.md): <see cref="Steamwoven"/> (the default)
/// and <see cref="Original"/> (the retail 1.0.7.4 behaviour). Each switch is a behaviour of the original confirmed in its
/// executable ([ledger WT3, WT7, WT11, WT12](../../../docs/research/original-verification-ledger.md)); the profile holds no state.
/// </summary>
public sealed record TravelRuleProfile
{
    public required string Name { get; init; }

    /// <summary>Ledger WT3 (A1): discovery compares a later area with the current best's radius, and the result with the radius
    /// loaded last, so a nearer area outside its own radius makes the step discover nothing. Steamwoven: the nearest area
    /// within its own radius.</summary>
    public bool DiscoveryUsesBestRadius { get; init; }

    /// <summary>Ledger WT7 (A2): after a detour the route counts one direction more than it wrote, so the walker takes the
    /// buffer's stale byte. Steamwoven: the route holds the steps it wrote.</summary>
    public bool DetourKeepsStaleStep { get; init; }

    /// <summary>Ledger WT11 (A3): the step that ends a segment adds no game time. Steamwoven: every step adds its hours.</summary>
    public bool SegmentEndAddsNoTime { get; init; }

    /// <summary>Ledger WT12 (A4): cancelling travel lands on the sector centre without the area snap. Steamwoven: a cancel
    /// lands as an arrival does.</summary>
    public bool CancelSkipsAreaSnap { get; init; }

    public static readonly TravelRuleProfile Steamwoven = new() { Name = "steamwoven" };

    public static readonly TravelRuleProfile Original = new()
    {
        Name = "original", DiscoveryUsesBestRadius = true, DetourKeepsStaleStep = true, SegmentEndAddsNoTime = true, CancelSkipsAreaSnap = true,
    };

    public static IReadOnlyList<TravelRuleProfile> All { get; } = new[] { Steamwoven, Original };

    public static TravelRuleProfile? FromName(string? name) => All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}

/// <summary>One area as the travel rules see it: its location and its discovery radius in tiles (ledger WT1).</summary>
public readonly record struct TravelArea(TravelLocation Location, long Radius);

/// <summary>
/// ARC-WORLD-056: world-map geometry and area searches of the retail executable (ledger WT3, WT4, WT6): pixels and sector
/// centres, the Chebyshev tile distance, the 8 sector directions and the area found in a radius or the nearest known area.
/// </summary>
public static class WorldTravelGeometry
{
    /// <summary>The width in sectors the original uses when it does not read the map's (ledger WT6).</summary>
    public const long DefaultWidth = 2000;

    /// <summary>Sector deltas of the 8 direction codes (ledger WT7): 0 (−1,−1), 1 (−1,0), 2 (−1,+1), 3 (0,+1), 4 (+1,+1),
    /// 5 (+1,0), 6 (+1,−1), 7 (0,−1).</summary>
    public static ReadOnlySpan<int> DeltaX => [-1, -1, -1, 0, 1, 1, 1, 0];
    public static ReadOnlySpan<int> DeltaY => [-1, 0, 1, 1, 1, 0, -1, -1];

    /// <summary>The sector centre a pixel stands for (0x561800): x = 64 (W − px) + 32, y = 64 py + 32.</summary>
    [OriginalFunction(0x561800, OriginalFunctionScope.Full)]
    public static TravelLocation Location(WorldMapPixel pixel, long width) =>
        new(checked(64 * (width - pixel.X) + 32), checked(64 * pixel.Y + 32));

    /// <summary>The pixel of a location (0x561490): (W − x/64, y/64).</summary>
    [OriginalFunction(0x561490, OriginalFunctionScope.Full)]
    public static WorldMapPixel Pixel(TravelLocation location, long width) => new(width - (location.X >> 6), location.Y >> 6);

    public static TravelSector Sector(TravelLocation location) => new(location.X >> 6, location.Y >> 6);

    public static TravelLocation Centre(TravelSector sector) => new(checked(64 * sector.X + 32), checked(64 * sector.Y + 32));

    /// <summary>The Chebyshev distance of two tile locations (0x4B96F0).</summary>
    [OriginalFunction(0x4B96F0, OriginalFunctionScope.Full)]
    public static long Distance(TravelLocation a, TravelLocation b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    /// <summary>
    /// The direction from one sector toward another (0x4CFCD0): the signs of the deltas, keeping the minor axis only when twice
    /// its size reaches the major's (an equal pair keeps both); the same sector gives 5.
    /// </summary>
    [OriginalFunction(0x4CFCD0, OriginalFunctionScope.Full)]
    public static int Direction(TravelSector from, TravelSector to)
    {
        long dx = to.X - from.X, dy = to.Y - from.Y;
        long sx = Math.Sign(dx), sy = Math.Sign(dy);
        if (Math.Abs(dx) > Math.Abs(dy)) { if (2 * Math.Abs(dy) - Math.Abs(dx) < 0) sy = 0; }
        else if (2 * Math.Abs(dx) - Math.Abs(dy) < 0) sx = 0;
        for (var code = 0; code < 8; code++)
            if (DeltaX[code] == sx && DeltaY[code] == sy) return code;
        return 5;
    }

    public static TravelSector Neighbor(TravelSector sector, int direction) =>
        direction is >= 0 and < 8 ? new(sector.X + DeltaX[direction], sector.Y + DeltaY[direction]) : throw new ArgumentOutOfRangeException(nameof(direction));

    /// <summary>
    /// The area whose radius covers a location (0x4CB4D0, ledger WT3), from area 1; with <paramref name="arrival"/> a radius
    /// of 0 or below counts as 64. 0 when none. Under <see cref="TravelRuleProfile.DiscoveryUsesBestRadius"/> the original's
    /// comparison: a later area against the current best's radius, the result against the radius loaded last.
    /// </summary>
    [OriginalFunction(0x4CB4D0, OriginalFunctionScope.Full)]
    public static int AreaInRadius(IReadOnlyList<TravelArea> areas, TravelLocation location, bool arrival, TravelRuleProfile rules)
    {
        ArgumentNullException.ThrowIfNull(areas); ArgumentNullException.ThrowIfNull(rules);
        long Radius(int area) => arrival && areas[area].Radius <= 0 ? 64 : areas[area].Radius;
        if (!rules.DiscoveryUsesBestRadius)
        {
            var nearest = 0; var nearestDistance = long.MaxValue;
            for (var area = 1; area < areas.Count; area++)
                if (Distance(areas[area].Location, location) is var d && d < Radius(area) && d < nearestDistance) (nearest, nearestDistance) = (area, d);
            return nearest;
        }
        var best = 1; long bestDistance = 0, loaded = 0;
        for (; best < areas.Count; best++)
        {
            loaded = Radius(best); bestDistance = Distance(areas[best].Location, location);
            if (bestDistance < loaded) break;
        }
        for (var other = best + 1; other < areas.Count; other++)
        {
            loaded = Radius(best);
            if (Distance(areas[other].Location, location) is var d && d < loaded && d < bestDistance) (best, bestDistance) = (other, d);
        }
        return best < areas.Count && bestDistance < loaded ? best : 0;
    }

    /// <summary>The nearest area the PC knows within a distance (0x4CB2A0, ledger WT4), from area 1; 0 when none.</summary>
    [OriginalFunction(0x4CB2A0, OriginalFunctionScope.Partial)]
    public static int NearestKnownArea(IReadOnlyList<TravelArea> areas, Func<int, bool> known, TravelLocation location, long distance)
    {
        ArgumentNullException.ThrowIfNull(areas); ArgumentNullException.ThrowIfNull(known);
        var nearest = 0; var nearestDistance = distance;
        for (var area = 1; area < areas.Count; area++)
            if (known(area) && Distance(areas[area].Location, location) is var d && d < nearestDistance) (nearest, nearestDistance) = (area, d);
        return nearest;
    }

    /// <summary>
    /// The area nearest to a location (0x4CB220, ledger GA3): the Chebyshev distance (0x4B96F0) to area 0's location first, then to each later
    /// area, which replaces the best only when strictly nearer. Area 0 when none is nearer; the location of an area outside the table is (0, 0).
    /// </summary>
    [OriginalFunction(0x4CB220, OriginalFunctionScope.Full)]
    public static int NearestArea(IReadOnlyList<TravelArea> areas, TravelLocation location)
    {
        ArgumentNullException.ThrowIfNull(areas);
        var best = 0; var bestDistance = Distance(areas.Count > 0 ? areas[0].Location : default, location);
        for (var area = 1; area < areas.Count; area++)
            if (Distance(areas[area].Location, location) is var d && d < bestDistance) (best, bestDistance) = (area, d);
        return best;
    }

    /// <summary>
    /// The area at a location (0x4CB630, ledger GA3): the nearest area (<see cref="NearestArea"/>) when the town map of the location's sector
    /// (sector field 0x4640 through the sector lock, 0x4BE380) is not 0 and equals the town map of the area's own location's sector; else 0.
    /// <paramref name="townMap"/> reads a sector's town map (0 for a sector without one) and answers null for a sector this caller cannot
    /// read; the result is then null too, and nothing is decided.
    /// </summary>
    [OriginalFunction(0x4CB630, OriginalFunctionScope.Full)]
    public static int? AreaAt(IReadOnlyList<TravelArea> areas, TravelLocation location, Func<TravelSector, int?> townMap)
    {
        ArgumentNullException.ThrowIfNull(areas); ArgumentNullException.ThrowIfNull(townMap);
        if (townMap(Sector(location)) is not { } here) return null;
        if (here == 0) return 0;
        var nearest = NearestArea(areas, location);
        if (nearest == 0) return 0;
        if (townMap(Sector(areas[nearest].Location)) is not { } there) return null;
        return there == 0 ? 0 : here == there ? nearest : 0;
    }
}

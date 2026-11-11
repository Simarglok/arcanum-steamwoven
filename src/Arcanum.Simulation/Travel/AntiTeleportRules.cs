namespace Arcanum.Simulation.Travel;

/// <summary>One anti-teleport region (ledger TP1): a centre tile and a radius in tiles.</summary>
public readonly record struct AntiTeleportZone(TravelLocation Centre, long Radius);

/// <summary>The loaded anti-teleport module (0x4BD9E0): the regions and the runtime maps on which no teleport works. Null in place of it means the module is off (no file).</summary>
public sealed record AntiTeleportZones(IReadOnlyList<AntiTeleportZone> Regions, IReadOnlyList<int> DisabledMaps);

/// <summary>
/// What the anti-teleport test (0x4BDD30) reads about the object that would teleport: whether there is one at all (a zero handle refuses at once), the map it is on, the start map (map list entry 1),
/// and the place the test measures from: on the start map the object's own tile; on any other map the location of the map's **area** on the start map (0x40FFE0 → 0x4CAED0), or null when the map
/// has no list row (the test refuses).
/// </summary>
public readonly record struct AntiTeleportSubject(bool HasObject, int CurrentMap, int StartMap, TravelLocation? Source);

/// <summary>
/// ARC-MAGIC-038 (MA-5 second half; ledger TP1, the anti-teleport test 0x4BDD30, read in the 1.0.7.4 executable): whether a teleport of an object is allowed. In the original's order: no object refuses;
/// with the module off (no <c>Rules\AntiTeleport.mes</c>) everything is allowed; the current map in the disabled list refuses; then the measuring place (<see cref="AntiTeleportSubject.Source"/>) refuses
/// when its Chebyshev tile distance (0x4B96F0) to **any** region's centre is below the region's radius; and when a target location other than (0, 0) is given it is measured the same way against every region.
/// Both compares are signed 64-bit.
/// </summary>
public static class AntiTeleportRules
{
    [OriginalFunction(0x4BDD30, OriginalFunctionScope.Full)]
    public static bool Allowed(AntiTeleportZones? zones, AntiTeleportSubject subject, TravelLocation? target = null)
    {
        if (!subject.HasObject) return false;
        if (zones is null) return true;
        if (zones.DisabledMaps.Contains(subject.CurrentMap)) return false;
        if (subject.Source is not { } source) return false;
        if (Inside(zones, source)) return false;
        if (target is { } to && (to.X != 0 || to.Y != 0) && Inside(zones, to)) return false;
        return true;
    }

    // Distance below the radius of any region.
    private static bool Inside(AntiTeleportZones zones, TravelLocation place)
    {
        foreach (var region in zones.Regions)
            if (WorldTravelGeometry.Distance(place, region.Centre) < region.Radius) return true;
        return false;
    }
}

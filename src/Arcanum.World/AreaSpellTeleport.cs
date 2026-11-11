using Arcanum.Session;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Travel;

namespace Arcanum.World;

/// <summary>
/// ARC-MAGIC-038 (MA-5 second half; ledger TP1–TP3): the cast host's teleport host over the shown map and the world-map data (<see cref="ISpellTeleportHost"/>). The anti-teleport test (0x4BDD30,
/// <see cref="AntiTeleportRules"/>) measures an object on the start map from its own tile and an object on any other map from the location of the map's area (map list, 0x40FFE0; the area table, 0x4CAED0; a map
/// without a row is refused); the area helper (0x452650) finds the area the same two ways (on the start map the area whose radius covers the object's tile, 0x4CB4D0 with the arrival flag clear). The world-map data is
/// loaded in the background at the start: until it is there the module counts as off and the answers are "allowed" with no area. Single-player only (the host branch of the original is not built).
/// </summary>
public sealed class AreaSpellTeleport(Func<AreaWorldMapData?> data, Func<int> currentMap, Func<string, CellCoord?> where, TravelRuleProfile? rules = null) : ISpellTeleportHost
{
    private readonly TravelRuleProfile _rules = rules ?? TravelRuleProfile.Steamwoven;

    /// <summary>The anti-teleport test for the object, with the optional second measure of a target location.</summary>
    public bool Allowed(string subject, SpellLocation? target)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (data() is not { } world) return true;
        var here = currentMap();
        TravelLocation? source;
        if (here == world.StartRuntime) source = where(subject) is { } cell ? new(cell.X, cell.Y) : new(0, 0);
        // 0x40FFE0 answers 0 for no list row, which 0x4CAED0 turns into the location of area 0; the test refuses only when the map index is not a list row at all.
        else source = world.MapAreas.TryGetValue(here, out var area) ? AreaLocation(world, area) : null;
        return AntiTeleportRules.Allowed(world.AntiTeleport, new(true, here, world.StartRuntime, source), target is { } to ? new(to.X, to.Y) : null);
    }

    /// <summary>The area helper's plan for the object (0x452650).</summary>
    public SpellPortalPlan Portal(string subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (data() is not { } world) return new(0, true, default);
        var here = currentMap();
        var area = here == world.StartRuntime
            ? where(subject) is { } cell ? WorldTravelGeometry.AreaInRadius(world.TravelAreas, new(cell.X, cell.Y), false, _rules) : 0
            : world.MapAreas.GetValueOrDefault(here);
        var location = AreaLocation(world, area) ?? default;
        return new(area, Allowed(subject, null), new(unchecked((uint)location.X), unchecked((uint)location.Y)));
    }

    /// <summary>The location of the map's own area on the start map (the map list's area, 0x40FFE0, then the area table, 0x4CAED0); null for a map with no list row.</summary>
    public static TravelLocation? MapAreaLocation(AreaWorldMapData world, int runtimeMap) => world.MapAreas.TryGetValue(runtimeMap, out var area) ? AreaLocation(world, area) : null;

    // 0x4CAED0: the location of an area, (0, 0) outside the table.
    private static TravelLocation? AreaLocation(AreaWorldMapData world, int area) =>
        area >= 0 && area < world.TravelAreas.Count ? world.TravelAreas[area].Location : new(0, 0);
}

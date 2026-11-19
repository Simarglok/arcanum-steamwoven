using Arcanum.Data.Area;

namespace Arcanum.World;

/// <summary>
/// ARC-WORLD-2792 (ARC-RENDER-022, ledger RF10): the bodies a roof covers are not drawn, and the original tests the tile a body stands on now. The map's T0 cover set is by the objects' stored
/// cells; a body that stands elsewhere (a companion, an NPC the AI host walked, a critter made at run time) is taken out of that set and put back only where its own tile is covered.
/// </summary>
public static class AreaRoofCover
{
    /// <summary>Replaces each placed body's entry of <paramref name="covered"/> (the T0 set) by the cover test of the world cell it stands on: in the set when a roof covers that tile (a faded roof too), out of it otherwise.</summary>
    public static void Place(ISet<AreaObjectKey> covered, IEnumerable<(AreaObjectKey Key, long X, long Y)> placed, AreaRoofGrid grid, Func<long, long, bool> indoor)
    {
        ArgumentNullException.ThrowIfNull(covered); ArgumentNullException.ThrowIfNull(placed); ArgumentNullException.ThrowIfNull(grid); ArgumentNullException.ThrowIfNull(indoor);
        foreach (var (key, x, y) in placed)
        {
            covered.Remove(key);
            if (grid.IsCovered(x, y, true, indoor)) covered.Add(key);
        }
    }
}

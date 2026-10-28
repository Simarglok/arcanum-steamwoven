using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Items;

/// <summary>
/// ARC-WORLD-081: the items a script's create object (action 52) adds to the ground, as the plant generator of template sectors
/// (script 30285, docs/research/template-sector-plants.md) does on every sector entry. The original adds one every time and none of
/// them decays (ledger PG1, ID9), so crossing a border again and again piles them up without bound; owner decision 2026-10-28 caps them
/// under Steamwoven (switch <c>ScrRuleProfile.CreatedItemsUnbounded</c>, Original: no cap).
/// </summary>
public static class CreatedItemRules
{
    /// <summary>The tiles a created object can land on around the object it is created next to: the create action's distance is at most
    /// 3 steps (PG1), and the walk to the target never ends further out (PG2), so every created object lies within 3 tiles on both axes.</summary>
    public const int Reach = 3;

    /// <summary>
    /// The created items that may lie within <see cref="Reach"/> of one object before Steamwoven creates no more next to it: the most one
    /// run of the installed plant generator can create next to one marker, two draws in each of R rounds with R at most 3 (the plains
    /// variants 1 and 67108865; ARC-RES-073 recount), so a patch never holds more than one lucky crossing makes.
    /// </summary>
    public const int Limit = 6;

    /// <summary>Whether a cell lies within <see cref="Reach"/> tiles of <paramref name="anchor"/> on both axes.</summary>
    public static bool Near(CellCoord anchor, CellCoord cell) =>
        Math.Abs((long)cell.X - anchor.X) <= Reach && Math.Abs((long)cell.Y - anchor.Y) <= Reach;

    /// <summary>How many more items may be created next to <paramref name="anchor"/>: <see cref="Limit"/> less the created items lying
    /// within <see cref="Reach"/> of it (0 at or past the limit), or null when <paramref name="unbounded"/> (the original's rule).</summary>
    public static int? Room(CellCoord anchor, IEnumerable<CellCoord> created, bool unbounded)
    {
        ArgumentNullException.ThrowIfNull(created);
        if (unbounded) return null;
        return Math.Max(0, Limit - created.Count(cell => Near(anchor, cell)));
    }
}

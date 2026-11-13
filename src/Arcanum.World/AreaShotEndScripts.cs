using Arcanum.Scripting.Scr;

namespace Arcanum.World;

/// <summary>
/// ARC-COMBAT-1719 (ledger PF9, PS5, CP4; 0x4B2870): which of the weapon's scripts a projectile's flight runs where it ends without a strike. The original ends the projectile first (0x4B2690), then dispatches (0x441980) the parent
/// item's hit script (point 7) at the destination of a shot with no target (a scattered throw, a boomerang that left its destination) or its miss script (point 8) at the range end, each followed, for a critical shot whose parent item is a
/// weapon (object type 5), by the critical-hit (34) or critical-miss (35) point; the shooter is the triggerer, the shot's target the extra object, and the answers are ignored. Nothing runs for a shot with no parent item. The hero's thrown
/// item runs its own hit and miss scripts in the thrown-item host (ARC-MAGIC-042: the six creature items, which transform into critters), so only its critical point is asked here.
/// </summary>
public static class AreaShotEndScripts
{
    /// <summary>The attachment points to run, in the original's order, for an end at the destination (<paramref name="destination"/>) or the range end of a shot that is critical or not, whose parent item is a weapon or not.</summary>
    public static IReadOnlyList<int> Points(bool destination, bool critical, bool weapon, bool thrownByHero)
    {
        var points = new List<int>(2);
        if (!thrownByHero) points.Add(destination ? ScrPoint.Hit : ScrPoint.Miss);
        if (critical && weapon) points.Add(destination ? ScrPoint.CriticalHit : ScrPoint.CriticalMiss);
        return points;
    }
}

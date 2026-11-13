using Arcanum.Session;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Traps;

namespace Arcanum.World;

/// <summary>
/// ARC-TECHNOLOGY-1619 (ledger TT12, TT15): the shooter candidates of a 30002 trap (0x4BD150 over the list 0x440FC0): the walls, portals, scenery and trap objects in the rectangle of the game window around the
/// <b>triggerer</b>, each with its use script and counters, for <see cref="SessionTrapFiring.Shooters"/> (the first whose script is 30007 and whose counters' third byte is the trap's tag shoots,
/// <see cref="TrapEffectRules.Shooter"/>). PROJECT: the original's rectangle is the game window's size around the triggerer's screen point; this one is the isometric window of the original's 800 by 400 game view at its
/// 80 by 40 tile footprint, <see cref="HalfTiles"/> cells on each isometric axis (|dx - dy| and |dx + dy| up to 10), as the AI host's near-the-hero scan (<c>AreaAiHost.CombatThreats</c>) takes it; the order is the square query's (the
/// list's own order is not read further).
/// </summary>
public static class AreaTrapShooters
{
    /// <summary>The isometric half extent of the window in cells.</summary>
    public const int HalfTiles = 10;

    /// <summary>
    /// The candidates around <paramref name="centre"/> (the triggerer's location; none without a triggerer: null). <paramref name="square"/> is the map's square query
    /// (<see cref="ISpellTargetWorld.ObjectsInSquare"/>), <paramref name="useScript"/> answers an object's use script and counters (null: the object has no use slot, so it cannot be a shooter).
    /// </summary>
    public static IReadOnlyList<TrapShooterCandidate> Candidates(SpellLocation? centre, Func<SpellLocation, int, int, IReadOnlyList<SpellTargetObject>> square,
        Func<string, (int Script, int Counters)?> useScript)
    {
        ArgumentNullException.ThrowIfNull(square);
        ArgumentNullException.ThrowIfNull(useScript);
        if (centre is not { } at) return [];
        var found = new List<TrapShooterCandidate>();
        foreach (var candidate in square(at, HalfTiles, TrapEffectRules.ShooterTypeMask))
        {
            long dx = (long)candidate.Location.X - at.X, dy = (long)candidate.Location.Y - at.Y;
            if (Math.Abs(dx - dy) > HalfTiles || Math.Abs(dx + dy) > HalfTiles || useScript(candidate.Id) is not { } use) continue;
            found.Add(new(candidate.Id, use.Script, use.Counters));
        }
        return found;
    }
}

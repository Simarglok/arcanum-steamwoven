using Arcanum.Session;
using Arcanum.Simulation;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>
/// ARC-INPUT-1946 (0x4445A0, ledger VC62): the Alt click's immediate action out of combat mode. Original: the target is a player character or an NPC, or an item of the types 5 to 14 that no inventory holds (base flag 0x1000 clear); the
/// Chebyshev distance of the two locations (0x441AE0, <c>location_dist</c> 0x4B96F0) is read: more than one tile and the UI hook 0x4606C0 is told (it does nothing here, so nothing moves); otherwise, with no network (the word 0x62AC6C is
/// clear), the object is moved onto the hero's location with zero offsets by the ordinary move 0x43E770, which sets no flag, schedules no decay and joins no junk pile. The click rules reach it for a loose item and a dead critter only.
/// </summary>
public static class AreaHeroPull
{
    /// <summary>The tile distance the action reaches (inclusive).</summary>
    public const int Reach = 1;

    /// <summary>
    /// Pulls a loose item the hero's map holds onto <paramref name="hero"/>: <see cref="HeroImmediateResult.None"/> for an item that does not lie on the ground (held, in a pile, in flight, destroyed), <see cref="HeroImmediateResult.TooFar"/> beyond
    /// <see cref="Reach"/>, otherwise the item's cell in the session becomes the hero's (<see cref="GameSessionOwner.MoveGroundItem"/>; the viewer draws a map-data item from the new cell as it draws a pushed one).
    /// </summary>
    [OriginalFunction(0x4445A0, OriginalFunctionScope.Partial)]
    public static HeroImmediateResult PullItem(GameSessionOwner session, string item, CellCoord hero)
    {
        ArgumentNullException.ThrowIfNull(session);
        SessionLyingItem? lying;
        try { lying = session.QueryGroundItem(item); }
        catch (ArgumentException) { return HeroImmediateResult.None; }
        if (lying is not { } at) return HeroImmediateResult.None;
        if (Distance(at.Cell, hero) > Reach) return HeroImmediateResult.TooFar;
        return session.MoveGroundItem(session.Version, item, hero).Disposition == GameSessionDisposition.Rejected ? HeroImmediateResult.None : HeroImmediateResult.Moved;
    }

    /// <summary>
    /// Pulls the body of the dead NPC <paramref name="npc"/>, which lies on <paramref name="body"/>, onto <paramref name="hero"/> through the AI host (<see cref="AreaAiHost.PlaceBody"/>): <see cref="HeroImmediateResult.TooFar"/> beyond
    /// <see cref="Reach"/>, <see cref="HeroImmediateResult.None"/> when the host refuses the body (not held, gone, not dead, a cell the map does not hold).
    /// </summary>
    public static HeroImmediateResult PullBody(AreaAiHost? host, string npc, CellCoord body, CellCoord hero)
    {
        if (host is null) return HeroImmediateResult.None;
        if (Distance(body, hero) > Reach) return HeroImmediateResult.TooFar;
        return host.PlaceBody(npc, hero) ? HeroImmediateResult.Moved : HeroImmediateResult.None;
    }

    /// <summary>The Chebyshev tile distance of 0x4B96F0.</summary>
    public static int Distance(CellCoord a, CellCoord b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}

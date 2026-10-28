using Arcanum.Simulation.Combat;

namespace Arcanum.Session;

/// <summary>ARC-COMBAT-016: a shot of the hero in flight (a projectile, ledger PF rows), named by the session for its strike or its end.</summary>
public readonly record struct SessionShot(long Id);

/// <summary>
/// ARC-COMBAT-016: the hero's shots in flight. A shot that flies (<see cref="SessionAttack.Flight"/>,
/// <see cref="SessionObjectAttack.Flight"/>) is decided at once, as the original's dispatcher decides it (the check, the dodge, the
/// fate, the ammunition and a critical miss's turn on the hero), and its projectile carries the outcome until it strikes
/// (<see cref="LandShot"/>, <see cref="LandShotOnObject"/>) or its flight ends (<see cref="EndShot"/>): only then is a hit's damage
/// rolled and applied. Shots in flight are not saved: a snapshot taken while one flies holds what the shot already spent and no
/// projectile (Steamwoven; the original's projectile is an object of the map), and a restored session has none.
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>The most shots in flight at once (one hero shoots one projectile per shot; a bow at expert two, ARC-COMBAT-018).</summary>
    public const int MaxShotsInFlight = 16;

    /// <summary>A shot in flight: the hero's weapon and skill at the shot (the projectile's parent weapon, field 0x50), the decided
    /// check, whether it hit, and its target: an NPC's entity, or a non-critter object's key (<see cref="ShotObjectKey"/>).</summary>
    private sealed record PendingShot(SessionHeroAttack Hero, AttackResolution Check, bool Hit, string? Entity, string? Object);

    private readonly Dictionary<SessionShot, PendingShot> _shots = [];
    private long _nextShot;

    /// <summary>The shots in flight, oldest first.</summary>
    public IReadOnlyList<SessionShot> QueryShotsInFlight()
    {
        ThrowIfDisposed();
        return _shots.Keys.OrderBy(shot => shot.Id).ToArray();
    }

    /// <summary>
    /// ARC-COMBAT-016: the flight of <paramref name="shot"/> ends without a strike (it flew beyond its range, left the map, or its host
    /// gave it up: the map changed); a hit it carried is lost, as the original's projectile that never meets its target. False when the
    /// shot is not in flight. Nothing of the session's saved state changes.
    /// </summary>
    public bool EndShot(SessionShot shot)
    {
        ThrowIfDisposed();
        return _shots.Remove(shot);
    }

    private SessionShot Launch(PendingShot pending)
    {
        var shot = new SessionShot(++_nextShot);
        _shots.Add(shot, pending);
        return shot;
    }

    /// <summary>The identity of an object attack's struck object, to match a strike against the shot's own target.</summary>
    private static string ShotObjectKey(SessionObjectAttack attack) =>
        attack.Wall ? "wall" : attack.Target is { } target ? $"object:{target.Map.RuntimeMap}:{target.Entity}" : $"item:{attack.Item?.Entity}";
}

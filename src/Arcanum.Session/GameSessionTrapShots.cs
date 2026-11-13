namespace Arcanum.Session;

/// <summary>
/// ARC-TECHNOLOGY-1635 (ledger TT15): a native trap's shot (script 30002) in flight, as the projectile carries it to the strike: the shooter object's entity (the cast map's id, the attack event's attacker and
/// the damage source), the victim it was shot at (the projectile's goal target) and the amount drawn at the trap (the original keeps it in the projectile's field 0x4F, in place of the hit location).
/// </summary>
public readonly record struct SessionTrapShotFacts(string Shooter, string Victim, int Amount);

public sealed partial class GameSessionOwner
{
    private readonly Dictionary<SessionShot, SessionTrapShotFacts> _trapShots = [];

    /// <summary>
    /// ARC-TECHNOLOGY-1635 (TT15, the projectile launch of the ranged path): a trap's shot leaves its shooter object: a shot of its own is made for the projectile the host flies (the amount is carried to the strike,
    /// <see cref="QueryTrapShot"/>; the strike is <see cref="SessionSpellCaster.StrikeTrapShot"/>, the end without one <see cref="EndShot"/>). Null, with nothing changed, when the shot cannot be kept: the shots in
    /// flight are at <see cref="MaxShotsInFlight"/>, the revision is used up, or an entity is empty or an amount negative; the trap then strikes at once.
    /// </summary>
    public SessionShot? BeginTrapShot(string shooter, string victim, int amount)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(shooter) || !SessionValidation.Entity(victim) || amount < 0 || ShotsInFlight > MaxShotsInFlight - 1 || _revision == long.MaxValue || _nextShot == long.MaxValue)
            return null;
        var shot = new SessionShot(++_nextShot);
        _trapShots[shot] = new(shooter, victim, amount);
        Revise();
        return shot;
    }

    /// <summary>ARC-TECHNOLOGY-1635: a trap's shot in flight as the projectile and its strike need it; null for a shot not in flight or one that is no trap's.</summary>
    public SessionTrapShotFacts? QueryTrapShot(SessionShot shot)
    {
        ThrowIfDisposed();
        return _trapShots.TryGetValue(shot, out var facts) ? facts : null;
    }
}

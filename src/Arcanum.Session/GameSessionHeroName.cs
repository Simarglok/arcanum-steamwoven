namespace Arcanum.Session;

/// <summary>
/// ARC-UI-033: the hero's player name (the original's PC string field 0x10E). The new-character window's Accept (0x545780, ledger NW6)
/// writes it once the name passes its check; the session stores the bytes as given. The portrait (field 0xE7) comes with the hero's
/// definition (<see cref="SessionHeroDefinition.Portrait"/>), as the window chooses it before the game starts.
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>
    /// The store of the Accept (0x545780, whose check is <see cref="Arcanum.Simulation.Character.CharacterNameRules.Accept"/>): stores <paramref name="name"/> (its bytes without a terminating NUL) as the hero's player name, in one revision. Refused, in order:
    /// no hero (<see cref="GameSessionFailure.InvalidHero"/>); a name longer than <see cref="SessionHeroLimits.MaxPlayerNameBytes"/> or with a
    /// NUL byte (<see cref="GameSessionFailure.InvalidPlayerName"/>); the version; the revision. The same name is NoChange.
    /// </summary>
    public GameSessionMutationResult SetHeroPlayerName(GameSessionVersion expected, IReadOnlyList<byte> name)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(name);
        if (_hero is not { } hero) return Reject(GameSessionFailure.InvalidHero);
        if (!SessionHeroLimits.ValidPlayerName(name)) return Reject(GameSessionFailure.InvalidPlayerName);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (hero.State.PlayerName.SequenceEqual(name)) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        hero.Apply(hero.State.WithPlayerName([.. name]));
        return Changed();
    }
}

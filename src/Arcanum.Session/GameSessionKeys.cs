using Arcanum.Simulation;

namespace Arcanum.Session;

/// <summary>
/// ARC-UI-081: the PC's key numbers, part of the story domain. The original's PC carries a key ring, an inventory item whose list holds the numbers
/// of the keys it was given (the logbook's keys tab lists that array in stored order, ledger LB20; the add function 0x466A50 appends a number). The
/// session keeps the list itself, not the ring item. NOT READY: key and key-ring items, the feed of this list to the lock check (the match rule itself is `LockRules.KeyFits`, 0x463340) and any host that gives a key.
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>The PC's key numbers in the order they were added, or null for a session without a hero (no PC, no key ring).</summary>
    public IReadOnlyList<int>? QueryHeroKeys()
    {
        ThrowIfDisposed();
        return _hero is null ? null : Array.AsReadOnly(_story.HeroKeys.ToArray());
    }

    /// <summary>
    /// Adds a key to the PC's ring (0x466A50): the number goes to the end of the list, one revision. A number held already is added again (the
    /// original's add does not look; the list shows it twice). <see cref="GameSessionFailure.KeysUnavailable"/> without a hero,
    /// <see cref="GameSessionFailure.InvalidKey"/> for a negative number, <see cref="GameSessionFailure.KeyRingFull"/> at
    /// <see cref="SessionStoryLimits.MaxKeys"/> keys (project policy).
    /// </summary>
    [OriginalFunction(0x466A50, OriginalFunctionScope.Partial)]
    public GameSessionMutationResult AddHeroKey(GameSessionVersion expected, int key)
    {
        ThrowIfDisposed();
        if (_hero is null) return Reject(GameSessionFailure.KeysUnavailable);
        if (!SessionStoryLimits.IsKey(key)) return Reject(GameSessionFailure.InvalidKey);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_story.HeroKeys.Count >= SessionStoryLimits.MaxKeys) return Reject(GameSessionFailure.KeyRingFull);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _story.HeroKeys.Add(key);
        return Changed();
    }
}

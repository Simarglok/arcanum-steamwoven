using Arcanum.Simulation;

namespace Arcanum.Session;

/// <summary>
/// ARC-UI-081: the PC's key numbers, part of the story domain. The original's PC carries a key ring, an inventory item whose list holds the numbers
/// of the keys it was given (the logbook's keys tab lists that array in stored order, ledger LB20; the add function 0x466A50 appends a number). The
/// session keeps the list itself, not the ring item. ARC-WORLD-123: the list feeds the open attempt (<c>AttemptOpen</c>, the match rule is
/// `LockRules.KeyFits`, 0x463340) and a key item the hero takes (pickup, loot, a dialog's item) is folded into it. NOT READY: key-ring items and keys
/// that stay loose (the session's hero always holds the ring, <see cref="HeroHoldsKeyRing"/>), keys of followers and keys from other sources.
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>
    /// Whether the hero holds a key ring. The original's PC starts with none (no pre-made kit lists one) and a key item taken without a ring
    /// stays an ordinary item; the session has no ring item yet, so a hero always holds the one ring whose list is
    /// <see cref="QueryHeroKeys"/> (ARC-UI-081) and a session without a hero holds none. One place to change when the ring becomes an item.
    /// </summary>
    internal bool HeroHoldsKeyRing => _hero is not null;

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

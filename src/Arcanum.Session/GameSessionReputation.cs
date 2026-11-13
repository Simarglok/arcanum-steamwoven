using Arcanum.Simulation;
using Arcanum.Simulation.Reaction;
using Arcanum.Simulation.Reputation;

namespace Arcanum.Session;

/// <summary>
/// ARC-WORLD-044: the PC's reputations (slice 4 of ARC-RES-028), read in the retail 1.0.7.4 executable by two readers (ledger
/// "Reputations"). The session holds the list in acquisition order with the game time of each, as the original's two PC
/// fields do; the module's table is data the host passes (<see cref="GameSessionDescriptor.Reputations"/>), never saved.
/// Single player: the original's network message is not modelled; the logbook button's light is the host's (ARC-UI-1364, QueryHeroInterfaceStamp).
/// </summary>
public sealed partial class GameSessionOwner
{
    private ReputationCatalog _reputationCatalog = ReputationCatalog.Empty;

    /// <summary>The module's reputation table the session reads (empty without one).</summary>
    public ReputationCatalog Reputations { get { ThrowIfDisposed(); return _reputationCatalog; } }

    /// <summary>The PC's reputations in the order it gained them, each with the game time it was first gained (0x4C1C30).</summary>
    [OriginalFunction(0x4C1C30, OriginalFunctionScope.Full)]
    public IReadOnlyList<SessionReputation> QueryReputations() { ThrowIfDisposed(); return Array.AsReadOnly(_story.Reputations.ToArray()); }

    /// <summary>Whether the PC holds the reputation (0x4C1CB0; dialog <c>RP</c>, script condition 33). A number outside
    /// 1000..1999 is never held: the session admits no other (A5).</summary>
    [OriginalFunction(0x4C1CB0, OriginalFunctionScope.Partial)]
    public bool HeroHasReputation(int reputation)
    {
        ThrowIfDisposed();
        return _story.Reputations.Exists(held => held.Reputation == reputation);
    }

    /// <summary>
    /// The PC gains a reputation (0x4C1D20) at the current game time; one already held is NoChange and keeps its first time.
    /// A number outside 1000..1999 is <see cref="GameSessionFailure.InvalidReputation"/> (proposal for A5: the original stores
    /// any number, which then reads outside its table).
    /// </summary>
    [OriginalFunction(0x4C1D20, OriginalFunctionScope.Partial)]
    public GameSessionMutationResult AddReputation(GameSessionVersion expected, int reputation)
    {
        ThrowIfDisposed();
        if (!SessionStoryLimits.IsReputation(reputation)) return Reject(GameSessionFailure.InvalidReputation);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_story.Reputations.Exists(held => held.Reputation == reputation)) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _story.Reputations.Add(new(reputation, _clock.QueryClockSnapshot().GameTimestampMs));
        return Changed();
    }

    /// <summary>
    /// The PC loses a reputation (0x4C1E10): the entry goes and the later ones move up, times with them; one not held is
    /// NoChange. The original's shift copies the wrong entries (ledger RM1, an original bug); both settings shift correctly
    /// (owner decision 2026-10-21). A number outside 1000..1999 is <see cref="GameSessionFailure.InvalidReputation"/>.
    /// </summary>
    [OriginalFunction(0x4C1E10, OriginalFunctionScope.Partial)]
    public GameSessionMutationResult RemoveReputation(GameSessionVersion expected, int reputation)
    {
        ThrowIfDisposed();
        if (!SessionStoryLimits.IsReputation(reputation)) return Reject(GameSessionFailure.InvalidReputation);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var index = _story.Reputations.FindIndex(held => held.Reputation == reputation);
        if (index < 0) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _story.Reputations.RemoveAt(index);
        return Changed();
    }

    /// <summary>The reputation term of the PC's reaction to an NPC of this origin and faction (<see cref="ReputationRules.ReactionSum"/>).</summary>
    public long QueryReputationReaction(int npcOrigin, int npcFaction)
    {
        ThrowIfDisposed();
        return ReputationRules.ReactionSum(HeldReputations(), _reputationCatalog, npcOrigin, npcFaction);
    }

    /// <summary>Whether the PC's reputations make it a member of the faction (<see cref="ReputationRules.GrantsFaction"/>).</summary>
    public bool HeroGrantedFaction(int faction)
    {
        ThrowIfDisposed();
        return ReputationRules.GrantsFaction(HeldReputations(), _reputationCatalog, faction);
    }

    /// <summary>The held numbers in list order.</summary>
    public IReadOnlyList<int> HeldReputations() { ThrowIfDisposed(); return Array.AsReadOnly(_story.Reputations.Select(held => held.Reputation).ToArray()); }

    /// <summary>
    /// The hero's reaction facts toward an NPC of this origin and faction: as <see cref="QueryHeroReactionFacts()"/> with the
    /// reputation sum for that NPC (R10). A sum beyond ±1,000,000 is passed on as such, and every reaction query and change
    /// then refuses it as invalid facts. Null without a hero.
    /// </summary>
    public ReactionPcFacts? QueryHeroReactionFacts(int npcOrigin, int npcFaction)
    {
        ThrowIfDisposed();
        if (_hero is null) return null;
        var sum = QueryReputationReaction(npcOrigin, npcFaction);
        return ReactionDomain.HeroFacts(_hero, _effects, HeroInnateRules()) with { ReputationAdjustment = (int)Math.Clamp(sum, int.MinValue, int.MaxValue) };
    }

    // Beyond its range: no repeated number and no time after the saved clock or before an earlier entry's (the list is in the
    // order gained, and game time does not run back).
    private static GameSessionFailure ImportReputations(IReadOnlyList<SessionReputation> saved, long clockMs, List<SessionReputation> into)
    {
        long previous = 0;
        var seen = new HashSet<int>();
        foreach (var held in saved)
        {
            if (!SessionStoryLimits.IsReputation(held.Reputation)) return GameSessionFailure.InvalidReputation;
            if (held.TimestampMs < previous || held.TimestampMs > clockMs || !seen.Add(held.Reputation)) return GameSessionFailure.InvalidSnapshot;
            previous = held.TimestampMs;
            into.Add(held);
        }
        return GameSessionFailure.None;
    }
}

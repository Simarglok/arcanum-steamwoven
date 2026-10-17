using Arcanum.Simulation.Character;
using Arcanum.Simulation.Reaction;

namespace Arcanum.Session;

/// <summary>
/// One NPC's saved reaction toward the session PC (ARC-WORLD-026; the source's per-PC reaction memory, ARC-RES-028 R3, R5,
/// R6). <see cref="Remembered"/> is the value of the PC's remembered slot: present exactly when the NPC has met the PC
/// before. <see cref="Conversation"/> is the value of the conversation slot: present exactly while the PC talks to the NPC.
/// An NPC without a record has neither and reads its reaction base.
/// </summary>
public readonly record struct SessionReaction(string Entity, int? Remembered, int? Conversation)
{
    /// <summary>The source's stored value: the conversation slot, else the remembered slot, else the base.</summary>
    public int Stored(int baseReaction) => Conversation ?? Remembered ?? baseReaction;

    public bool MetBefore => Remembered.HasValue;
    public bool InConversation => Conversation.HasValue;
    internal bool IsBaseline => Remembered is null && Conversation is null;
}

/// <summary>Saved reaction domain: the NPCs whose reaction memory the session changed, in ordinal entity order.</summary>
public sealed class SessionReactionState
{
    internal SessionReactionState(SessionReaction[] records) => Records = Array.AsReadOnly(records);

    public IReadOnlyList<SessionReaction> Records { get; }
}

/// <summary>
/// A reaction query: the effective value under the facts with its category, the stored value it was computed from, whether
/// the NPC has met the PC before (the source's "met before" excludes the conversation slot, so it is still false during the
/// first conversation) and whether a conversation is open, and whether the session holds a record.
/// </summary>
public sealed record SessionReactionView(int Effective, ReactionCategory Category, int Stored, bool MetBefore, bool InConversation,
    bool Recorded);

public sealed record SessionReactionQueryResult(GameSessionFailure Failure, SessionReactionView? View);

/// <summary>
/// What a reaction change did. <see cref="Adjustment"/> is set whenever a change was decided, also for NoChange (a skipped
/// change, or an unchanged record); <see cref="FollowCheck"/> asks the caller to re-run a hit follower's follow check
/// (<see cref="GameSessionOwner.ReactToPcHit"/>). Both are null/false for a refusal and for the conversation and forget steps.
/// </summary>
public sealed record SessionReactionResult(GameSessionDisposition Disposition, GameSessionFailure Failure,
    ReactionAdjustment? Adjustment, bool FollowCheck, GameSessionVersion Version);

/// <summary>The reaction domain of one owner.</summary>
internal sealed class ReactionDomain
{
    internal Dictionary<string, SessionReaction> Records { get; } = new(StringComparer.Ordinal);

    internal SessionReaction Get(string entity) => Records.TryGetValue(entity, out var record) ? record : new(entity, null, null);

    internal SessionReactionState Capture() =>
        new(Records.Values.OrderBy(record => record.Entity, StringComparer.Ordinal).ToArray());

    internal static bool ValidRecord(SessionReaction record) =>
        !record.IsBaseline && (record.Remembered is not { } r || ReactionLimits.InRange(r)) &&
        (record.Conversation is not { } c || ReactionLimits.InRange(c));

    /// <summary>The hero's side of a reaction (see <see cref="GameSessionOwner.QueryHeroReactionFacts"/>).</summary>
    internal static ReactionPcFacts HeroFacts(HeroDomain hero)
    {
        var stats = hero.Stats.Query(new(0)).Snapshot!;
        return new(hero.State.Race, stats.GetEffective(CharacterStat.ReactionModifier), 0, [], [], [],
            stats.GetEffective(CharacterStat.Charisma) >= CharacterStatRules.ExtraordinaryThreshold);
    }
}

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// The hero's reaction facts: race, effective reaction modifier and whether its effective charisma is extraordinary.
    /// The reputation sum is 0 and the reaction, good and bad reaction effects are empty: there is no reputation owner yet,
    /// and the hero's innate effects keep only the stats the character owner models. Null for a session without a hero, whose host
    /// supplies PC facts itself.
    /// </summary>
    public ReactionPcFacts? QueryHeroReactionFacts() { ThrowIfDisposed(); return _hero is null ? null : ReactionDomain.HeroFacts(_hero); }

    /// <summary>The NPC's saved reaction record, or null while the session never changed it. An invalid entity id throws.</summary>
    public SessionReaction? QueryReactionRecord(string entity)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) throw new ArgumentException("Invalid entity id.", nameof(entity));
        return _reactions.Records.TryGetValue(entity, out var record) ? record : null;
    }

    /// <summary>Detached copy of the reaction domain.</summary>
    public SessionReactionState QueryReactions() { ThrowIfDisposed(); return _reactions.Capture(); }

    /// <summary>
    /// The NPC's effective reaction to the PC (<see cref="ReactionRules.Effective"/>) from its record, or its base while it has
    /// none. An invalid entity id is <see cref="GameSessionFailure.InvalidReactionTarget"/>, facts out of range
    /// <see cref="GameSessionFailure.InvalidReactionFacts"/>.
    /// </summary>
    public SessionReactionQueryResult QueryReaction(string entity, ReactionNpcFacts npc, ReactionPcFacts pc,
        ReactionQuery query = ReactionQuery.Standard)
    {
        ThrowIfDisposed();
        var failure = ResolveReaction(entity, npc, pc, out var record);
        if (failure == GameSessionFailure.None && !Enum.IsDefined(query)) failure = GameSessionFailure.InvalidReactionFacts;
        if (failure != GameSessionFailure.None) return new(failure, null);
        var stored = record.Stored(npc.BaseReaction);
        var effective = ReactionRules.Effective(stored, npc, pc, query);
        return new(GameSessionFailure.None, new(effective, ReactionRules.Category(effective), stored, record.MetBefore,
            record.InConversation, _reactions.Records.ContainsKey(entity)));
    }

    /// <summary>
    /// Changes the NPC's reaction by <paramref name="delta"/> (<see cref="ReactionRules.Adjust"/>; dialog <c>RE ±d</c>, quest
    /// completion, scripts). Whether the NPC follows or waits for the PC is the session party's. The new value goes to the
    /// conversation slot while one is open, else to the remembered slot, which makes the NPC remember the PC. A delta or a
    /// new stored value beyond ±1,000,000 is <see cref="GameSessionFailure.InvalidReactionAmount"/>.
    /// </summary>
    public SessionReactionResult AdjustReaction(GameSessionVersion expected, string entity, ReactionNpcFacts npc, ReactionPcFacts pc,
        int delta)
    {
        ThrowIfDisposed();
        return ChangeReaction(expected, entity, npc, pc, _ => delta);
    }

    /// <summary>
    /// "Set the reaction to <paramref name="target"/>" (dialog <c>RE v</c>, a quest's "at least 41", scripts): the delta
    /// <c>target − effective</c> through <see cref="AdjustReaction"/>, so with PC reaction effects the result can differ from
    /// the target, as in the source.
    /// </summary>
    public SessionReactionResult SetReaction(GameSessionVersion expected, string entity, ReactionNpcFacts npc, ReactionPcFacts pc,
        int target)
    {
        ThrowIfDisposed();
        if (!ReactionLimits.InRange(target)) return RejectReaction(GameSessionFailure.InvalidReactionAmount);
        return ChangeReaction(expected, entity, npc, pc,
            stored => ReactionRules.DeltaTo(target, ReactionRules.Effective(stored, npc, pc)));
    }

    /// <summary>
    /// The reaction effect of a PC's hit on the NPC (<see cref="ReactionRules.PcHit"/> under the active rules' reaction
    /// profile, read at this call): a follower of the PC gains (Original) or loses (Steamwoven) AI parameter 9 on a deliberate
    /// hit and must re-run its follow check (<see cref="SessionReactionResult.FollowCheck"/>; leaving the party is the
    /// caller's next step); any other NPC loses 10 on an accidental hit or is lowered to parameter 10 on a deliberate one.
    /// </summary>
    public SessionReactionResult ReactToPcHit(GameSessionVersion expected, string entity, ReactionNpcFacts npc, ReactionPcFacts pc,
        ReactionHitFacts ai, bool deliberate)
    {
        ThrowIfDisposed();
        if (!ReactionLimits.InRange(ai.FollowerHitAdjustment) || !ReactionLimits.InRange(ai.HostileReaction))
            return RejectReaction(GameSessionFailure.InvalidReactionFacts);
        var profile = _rules.Reaction;
        var follow = false;
        return ChangeReaction(expected, entity, npc, pc, stored =>
        {
            var hit = ReactionRules.PcHit(stored, npc, pc, ai, LedByPc(entity), deliberate, profile);
            follow = hit.FollowCheck;
            return hit.Delta;
        }, () => follow);
    }

    /// <summary>
    /// A conversation with the NPC starts (R5): the stored value moves into the conversation slot, raised to 100 when it is
    /// below and the PC spends its "force good reaction" fate point (<paramref name="forceGoodReaction"/>; fate points are not
    /// session state yet). Changes made until <see cref="EndReactionConversation"/> go to that slot; "met before" stays as it was.
    /// </summary>
    public SessionReactionResult BeginReactionConversation(GameSessionVersion expected, string entity, ReactionNpcFacts npc,
        bool forceGoodReaction = false)
    {
        ThrowIfDisposed();
        return StepReaction(expected, entity, npc, record =>
            record with { Conversation = ReactionRules.ConversationStart(record.Stored(npc.BaseReaction), forceGoodReaction) });
    }

    /// <summary>The conversation ends (R5): the stored value goes to the remembered slot and the conversation slot is cleared,
    /// so the NPC has met the PC. As in the source this also remembers the PC when no conversation was open.</summary>
    public SessionReactionResult EndReactionConversation(GameSessionVersion expected, string entity, ReactionNpcFacts npc)
    {
        ThrowIfDisposed();
        return StepReaction(expected, entity, npc, record => new(entity, record.Stored(npc.BaseReaction), null));
    }

    /// <summary>The NPC forgets the PC (R6): the remembered slot is cleared; an open conversation keeps its slot.</summary>
    public SessionReactionResult ForgetReaction(GameSessionVersion expected, string entity)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) return RejectReaction(GameSessionFailure.InvalidReactionTarget);
        return Commit(expected, _reactions.Get(entity) with { Remembered = null }, null, false);
    }

    private bool LedByPc(string entity) => _party.Get(entity).LedByPc;

    // Every change: target, facts and version checks, then the delta (none: NoChange), its range, the rules, the new stored
    // value's range, then the record budget and revision capacity.
    private SessionReactionResult ChangeReaction(GameSessionVersion expected, string entity, ReactionNpcFacts npc, ReactionPcFacts pc,
        Func<int, long?> delta, Func<bool>? followCheck = null)
    {
        var failure = ResolveReaction(entity, npc, pc, out var record);
        if (failure != GameSessionFailure.None) return RejectReaction(failure);
        if (!IsCurrent(expected)) return RejectReaction(GameSessionFailure.StaleVersion);
        var stored = record.Stored(npc.BaseReaction);
        var requested = delta(stored);
        var follow = followCheck?.Invoke() ?? false;
        if (requested is not { } value) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, follow, Version);
        if (!ReactionLimits.InRange(value)) return RejectReaction(GameSessionFailure.InvalidReactionAmount);
        var adjustment = ReactionRules.Adjust((int)value, npc, pc, LedByPc(entity));
        if (!adjustment.Applies) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, adjustment, follow, Version);
        var next = (long)stored + adjustment.Delta;
        if (!ReactionLimits.InRange(next)) return RejectReaction(GameSessionFailure.InvalidReactionAmount);
        var updated = record.InConversation ? record with { Conversation = (int)next } : record with { Remembered = (int)next };
        return Commit(expected, updated, adjustment, follow);
    }

    private SessionReactionResult StepReaction(GameSessionVersion expected, string entity, ReactionNpcFacts npc,
        Func<SessionReaction, SessionReaction> step)
    {
        if (!SessionValidation.Entity(entity)) return RejectReaction(GameSessionFailure.InvalidReactionTarget);
        if (!ReactionRules.IsValid(npc)) return RejectReaction(GameSessionFailure.InvalidReactionFacts);
        return Commit(expected, step(_reactions.Get(entity)), null, false);
    }

    private SessionReactionResult Commit(GameSessionVersion expected, SessionReaction next, ReactionAdjustment? adjustment, bool follow)
    {
        if (!IsCurrent(expected)) return RejectReaction(GameSessionFailure.StaleVersion);
        var recorded = _reactions.Records.TryGetValue(next.Entity, out var current);
        if (recorded ? current == next : next.IsBaseline)
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, adjustment, follow, Version);
        if (!recorded && _reactions.Records.Count >= _limits.MaxReactionRecords) return RejectReaction(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectReaction(GameSessionFailure.RevisionExhausted);
        if (next.IsBaseline) _reactions.Records.Remove(next.Entity);
        else _reactions.Records[next.Entity] = next;
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, adjustment, follow, Version);
    }

    private GameSessionFailure ResolveReaction(string entity, ReactionNpcFacts npc, ReactionPcFacts pc, out SessionReaction record)
    {
        record = default;
        if (!SessionValidation.Entity(entity)) return GameSessionFailure.InvalidReactionTarget;
        if (!ReactionRules.IsValid(npc) || !ReactionRules.IsValid(pc)) return GameSessionFailure.InvalidReactionFacts;
        record = _reactions.Get(entity);
        return GameSessionFailure.None;
    }

    private SessionReactionResult RejectReaction(GameSessionFailure failure) =>
        new(GameSessionDisposition.Rejected, failure, null, false, new(_identity, _revision));

    // Records are unique, in ordinal order, not at the baseline, in range and within the caller's budget.
    private static GameSessionFailure ImportReactions(SessionReactionState saved, GameSessionLimits limits, out ReactionDomain reactions)
    {
        reactions = new();
        if (saved.Records.Count > limits.MaxReactionRecords) return GameSessionFailure.BudgetExceeded;
        string? previous = null;
        foreach (var record in saved.Records)
        {
            if (!SessionValidation.Entity(record.Entity)) return GameSessionFailure.InvalidReactionTarget;
            if (!ReactionDomain.ValidRecord(record)) return GameSessionFailure.InvalidReactionAmount;
            if (previous is not null && string.CompareOrdinal(previous, record.Entity) >= 0) return GameSessionFailure.InvalidSnapshot;
            previous = record.Entity;
            reactions.Records.Add(record.Entity, record);
        }
        return GameSessionFailure.None;
    }
}

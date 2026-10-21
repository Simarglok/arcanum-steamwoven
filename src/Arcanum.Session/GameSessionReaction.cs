using Arcanum.Simulation;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Party;
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

    /// <summary>The source's "met before" (0x4C0C40): the remembered slot only, so not during the first conversation.</summary>
    public bool MetBefore { [OriginalFunction(0x4C0C40, OriginalFunctionScope.Partial)] get => Remembered.HasValue; }
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
/// (<see cref="GameSessionOwner.ReactToPcHit"/>), also when the reaction write is refused after the hit was decided (a new
/// value beyond the bounds). Otherwise both are null/false for a refusal and for the conversation and forget steps.
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

    /// <summary>The hero's side of a reaction (see <see cref="GameSessionOwner.QueryHeroReactionFacts"/>); the reaction, good and
    /// bad reaction effects are the hero's durable effects' (ARC-WORLD-029).</summary>
    internal static ReactionPcFacts HeroFacts(HeroDomain hero, EffectDomain effects)
    {
        var stats = hero.Stats.Query(new(0)).Snapshot!;
        return new(hero.State.Race, stats.GetEffective(CharacterStat.ReactionModifier), 0, effects.Special(EffectSpecial.Reaction),
            effects.Special(EffectSpecial.GoodReaction), effects.Special(EffectSpecial.BadReaction),
            stats.GetEffective(CharacterStat.Charisma) >= CharacterStatRules.ExtraordinaryThreshold);
    }
}

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// The hero's reaction facts: race, effective reaction modifier, whether its effective charisma is extraordinary, its
    /// durable effects' reaction, good and bad reaction effects, and since ARC-WORLD-044 the reputation sum for an NPC of origin
    /// 0 and faction 0 (<see cref="QueryHeroReactionFacts(int, int)"/> for another NPC). Null for a session without a hero, whose
    /// host supplies PC facts itself.
    /// </summary>
    public ReactionPcFacts? QueryHeroReactionFacts() => QueryHeroReactionFacts(0, 0);

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
        return ChangeReaction(expected, entity, npc, pc,
            stored => ReactionRules.DeltaTo(target, ReactionRules.Effective(stored, npc, pc)), validAmount: ReactionLimits.InRange(target));
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
        var validAi = ReactionLimits.InRange(ai.FollowerHitAdjustment) && ReactionLimits.InRange(ai.HostileReaction);
        var profile = _rules.Reaction;
        var follow = false;
        return ChangeReaction(expected, entity, npc, pc, stored =>
        {
            var hit = ReactionRules.PcHit(stored, npc, pc, ai, LedByPc(entity), deliberate, profile);
            follow = hit.FollowCheck;
            return hit.Delta;
        }, () => follow, validAi);
    }

    /// <summary>
    /// A conversation with the NPC starts (R5): the stored value moves into the conversation slot, raised to 100 when it is
    /// below and the hero's "force good reaction" fate is armed; that fate is then used in the same revision (DR6, ARC-WORLD-054),
    /// while at 100 or above it stays armed for a later talk. Changes made until <see cref="EndReactionConversation"/> go to that
    /// slot; "met before" stays as it was.
    /// </summary>
    public SessionReactionResult BeginReactionConversation(GameSessionVersion expected, string entity, ReactionNpcFacts npc)
    {
        ThrowIfDisposed();
        var force = _hero is { } hero && FateRules.IsArmed(hero.State.Fate, FateKind.ForceGoodReaction);
        var used = false;
        var result = StepReaction(expected, entity, npc, record =>
        {
            var stored = record.Stored(npc.BaseReaction);
            used = force && stored < ReactionRules.ForcedGoodReaction;
            return record with { Conversation = ReactionRules.ConversationStart(stored, force) };
        });
        // The raised start always changes the slot, so a used fate commits with it.
        if (used && result.Disposition == GameSessionDisposition.Changed)
            _hero!.Apply(_hero.State.WithFate(FateRules.Use(_hero.State.Fate, FateKind.ForceGoodReaction).After));
        return result;
    }

    /// <summary>The conversation ends (R5): the stored value goes to the remembered slot and the conversation slot is cleared,
    /// so the NPC has met the PC. As in the source this also remembers the PC when no conversation was open.</summary>
    [OriginalFunction(0x4C10A0, OriginalFunctionScope.Partial)]
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

    // Every change: target, facts and version checks, then the requested amount, the delta (none: NoChange), its range, the
    // rules, the new stored value's range, then the record budget and revision capacity. Once a hit is decided its follow
    // check is reported even when the reaction write is refused, as the source runs the check whatever the write does.
    private SessionReactionResult ChangeReaction(GameSessionVersion expected, string entity, ReactionNpcFacts npc, ReactionPcFacts pc,
        Func<int, long?> delta, Func<bool>? followCheck = null, bool validFacts = true, bool validAmount = true)
    {
        var failure = ResolveReaction(entity, npc, pc, out var record);
        if (failure == GameSessionFailure.None && !validFacts) failure = GameSessionFailure.InvalidReactionFacts;
        if (failure != GameSessionFailure.None) return RejectReaction(failure);
        if (!IsCurrent(expected)) return RejectReaction(GameSessionFailure.StaleVersion);
        if (!validAmount) return RejectReaction(GameSessionFailure.InvalidReactionAmount);
        var stored = record.Stored(npc.BaseReaction);
        var requested = delta(stored);
        var follow = followCheck?.Invoke() ?? false;
        if (requested is not { } value) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, follow, Version);
        if (!ReactionLimits.InRange(value)) return RejectReaction(GameSessionFailure.InvalidReactionAmount, follow);
        var planned = PlanReaction(record, stored, (int)value, npc, pc, LedByPc(entity), out var adjustment, out var updated);
        if (!adjustment.Applies) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, adjustment, follow, Version);
        if (planned != GameSessionFailure.None) return RejectReaction(planned, follow);
        return Commit(expected, updated, adjustment, follow);
    }

    // The change's decision and the record it writes; a new stored value beyond the bounds is InvalidReactionAmount.
    private static GameSessionFailure PlanReaction(SessionReaction record, int stored, int delta, ReactionNpcFacts npc, ReactionPcFacts pc,
        bool ledByPc, out ReactionAdjustment adjustment, out SessionReaction updated)
    {
        adjustment = ReactionRules.Adjust(delta, npc, pc, ledByPc);
        updated = record;
        if (!adjustment.Applies) return GameSessionFailure.None;
        var next = (long)stored + adjustment.Delta;
        if (!ReactionLimits.InRange(next)) return GameSessionFailure.InvalidReactionAmount;
        updated = record.InConversation ? record with { Conversation = (int)next } : record with { Remembered = (int)next };
        return GameSessionFailure.None;
    }

    private SessionReactionResult StepReaction(GameSessionVersion expected, string entity, ReactionNpcFacts npc,
        Func<SessionReaction, SessionReaction> step)
    {
        if (!SessionValidation.Entity(entity)) return RejectReaction(GameSessionFailure.InvalidReactionTarget);
        if (!ReactionRules.IsValid(npc)) return RejectReaction(GameSessionFailure.InvalidReactionFacts);
        return Commit(expected, step(_reactions.Get(entity)), null, false);
    }

    // ARC-WORLD-045 (OL17): a change that asks for the leader check arms the follower's check-leader and reaction marks in the
    // same revision, also when its delta after the PC's effects leaves the stored value as it was.
    private SessionReactionResult Commit(GameSessionVersion expected, SessionReaction next, ReactionAdjustment? adjustment, bool follow)
    {
        if (!IsCurrent(expected)) return RejectReaction(GameSessionFailure.StaleVersion);
        var recorded = _reactions.Records.TryGetValue(next.Entity, out var current);
        var unchanged = recorded ? current == next : next.IsBaseline;
        var companion = _party.Get(next.Entity);
        var armed = adjustment is { LeaderCheck: true } ? companion with { Loyalty = CompanionLoyaltyRules.ArmReaction(companion.Loyalty) } : companion;
        if (unchanged && armed == companion)
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, adjustment, follow, Version);
        if (!unchanged && !recorded && _reactions.Records.Count >= _limits.MaxReactionRecords) return RejectReaction(GameSessionFailure.BudgetExceeded, follow);
        if (_revision == long.MaxValue) return RejectReaction(GameSessionFailure.RevisionExhausted, follow);
        if (!unchanged)
        {
            if (next.IsBaseline) _reactions.Records.Remove(next.Entity);
            else _reactions.Records[next.Entity] = next;
        }
        // The leader check needs a PC leader, so the NPC already has a party record and keeps its place in the array.
        if (armed != companion) _party.Put(armed, false);
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

    private SessionReactionResult RejectReaction(GameSessionFailure failure, bool follow = false) =>
        new(GameSessionDisposition.Rejected, failure, null, follow, new(_identity, _revision));

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

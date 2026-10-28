using Arcanum.Simulation;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Party;
using Arcanum.Simulation.Reaction;

namespace Arcanum.Session;

/// <summary>
/// One NPC's saved danger record (ARC-WORLD-079, NPC AI slice I3): its danger bits, combat focus (field 0x11B), fleeing-from
/// (0xE6), who-hit-me-last (0x11C) and enemy list (0x130), each handle the hero or an object by its stable entity id. The
/// enemy order is the original's (appended at the end; a removal fills the hole with the last entry), which the enemy-list
/// pick of slice I4 draws over. An NPC without a record has none of them.
/// </summary>
public sealed record SessionDanger
{
    public SessionDanger(string entity, DangerFlags flags, SessionCombatant? focus, SessionCombatant? fleeingFrom, SessionCombatant? hitBy,
        IReadOnlyList<SessionCombatant> enemies)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(enemies);
        Entity = entity; Flags = flags; Focus = focus; FleeingFrom = fleeingFrom; HitBy = hitBy; Enemies = enemies;
    }

    public string Entity { get; }
    public DangerFlags Flags { get; init; }
    public SessionCombatant? Focus { get; init; }
    public SessionCombatant? FleeingFrom { get; init; }
    public SessionCombatant? HitBy { get; init; }
    /// <summary>A read-only copy of the list it is given.</summary>
    public IReadOnlyList<SessionCombatant> Enemies
    {
        get => _enemies;
        init => _enemies = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray());
    }

    private readonly IReadOnlyList<SessionCombatant> _enemies = [];

    /// <summary>The state read (<see cref="DangerRules.Read"/>).</summary>
    public DangerState State => DangerRules.Read(Flags);

    /// <summary>The state read's source: fleeing-from for FLEE and SURRENDER, the focus for COMBAT_FOCUS, none for NONE.</summary>
    public SessionCombatant? Source => State switch
    {
        DangerState.Flee or DangerState.Surrender => FleeingFrom,
        DangerState.CombatFocus => Focus,
        _ => null,
    };

    internal static SessionDanger Baseline(string entity) => new(entity, DangerFlags.None, null, null, null, []);

    internal bool IsBaseline => Flags == DangerFlags.None && Focus is null && FleeingFrom is null && HitBy is null && Enemies.Count == 0;

    internal bool SameAs(SessionDanger other) => Entity == other.Entity && Flags == other.Flags && Focus == other.Focus &&
        FleeingFrom == other.FleeingFrom && HitBy == other.HitBy && Enemies.SequenceEqual(other.Enemies);
}

/// <summary>Saved danger domain: the NPCs whose danger record the session holds, in ordinal entity order.</summary>
public sealed class SessionDangerState
{
    internal SessionDangerState(SessionDanger[] records) => Records = Array.AsReadOnly(records);

    public static SessionDangerState Empty { get; } = new([]);

    public IReadOnlyList<SessionDanger> Records { get; }
}

/// <summary>
/// The reaction facts setter rule 8 reads when a fight targets the hero: the NPC's and the PC's reaction facts and the NPC's
/// AI parameter 10 (hostile reaction). <see cref="ReactionNpcFacts.PcOnEnemyList"/> is replaced by the session's enemy list.
/// </summary>
public sealed record SessionDangerReaction(ReactionNpcFacts Npc, ReactionPcFacts Pc, int HostileReaction);

/// <summary>
/// A setter call (0x4AABE0): the NPC, the requested state and target (may be none), the setter's facts
/// (<see cref="DangerSetterFacts.TargetIsPc"/> is the session's: the target is the hero), whether the caller asks for the
/// alerted sound, the reaction facts that a fight on the hero needs (<see cref="Reaction"/>, rule 8) and the NPC's
/// combat-mode facts that a surrender of an NPC in combat mode needs (<see cref="CombatMode"/>, the toggle's refusals).
/// </summary>
public sealed record SessionDangerRequest
{
    public required string Entity { get; init; }
    public required DangerState State { get; init; }
    public SessionCombatant? Target { get; init; }
    public DangerSetterFacts Facts { get; init; } = new();
    public bool AlertSound { get; init; }
    public SessionDangerReaction? Reaction { get; init; }
    public SessionCombatModeContext? CombatMode { get; init; }
}

/// <summary>A script hook a danger call asks the host to run: the attachment point, the triggerer (may be none), the NPC as attachee.</summary>
public readonly record struct SessionDangerHook(DangerScriptPoint Point, SessionCombatant? Triggerer, string Attachee);

/// <summary>
/// What a danger call did. <see cref="Transition"/> is the setter's step when one ran (also for NoChange); <see cref="Hook"/>
/// the script to run after the call; <see cref="LeftCombatMode"/> that a surrender's combat-mode exit acted (the bit then
/// stays set in the Original setting, CL4); <see cref="Reaction"/> the rule-8 reaction change when one was decided;
/// <see cref="Locked"/> that a target lock took hold; <see cref="StartsFleeGoal"/> that perform fleeing starts the flee goal away
/// from the NPC's fleeing-from (ARC-WORLD-082).
/// </summary>
public sealed record SessionDangerResult(GameSessionDisposition Disposition, GameSessionFailure Failure, GameSessionVersion Version)
{
    public DangerTransition? Transition { get; init; }
    public SessionDangerHook? Hook { get; init; }
    public bool LeftCombatMode { get; init; }
    public ReactionAdjustment? Reaction { get; init; }
    public bool Locked { get; init; }
    public bool StartsFleeGoal { get; init; }
}

/// <summary>The danger domain of one owner.</summary>
internal sealed class DangerDomain
{
    internal Dictionary<string, SessionDanger> Records { get; } = new(StringComparer.Ordinal);
    internal int Enemies { get; private set; }

    internal SessionDanger Get(string entity) => Records.TryGetValue(entity, out var record) ? record : SessionDanger.Baseline(entity);

    internal void Put(SessionDanger record)
    {
        if (Records.TryGetValue(record.Entity, out var current)) Enemies -= current.Enemies.Count;
        if (record.IsBaseline) { Records.Remove(record.Entity); return; }
        Records[record.Entity] = record;
        Enemies += record.Enemies.Count;
    }

    internal SessionDangerState Capture() =>
        new(Records.Values.OrderBy(record => record.Entity, StringComparer.Ordinal).ToArray());
}

public sealed partial class GameSessionOwner
{
    /// <summary>The NPC's danger record, or a baseline record (state NONE, nothing listed) while the session holds none. An invalid
    /// entity id throws.</summary>
    public SessionDanger QueryDanger(string entity)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) throw new ArgumentException("Invalid entity id.", nameof(entity));
        return _danger.Get(entity);
    }

    /// <summary>Detached copy of the danger domain.</summary>
    public SessionDangerState QueryDangerRecords() { ThrowIfDisposed(); return _danger.Capture(); }

    /// <summary>
    /// The danger setter (<see cref="DangerRules.Set"/> under the active combat setting) for one NPC, applied in one revision:
    /// its bits, fleeing-from (rule 5) and focus (rule 9) := the target; for a fight on the hero, the hostility-variant
    /// reaction above parameter 10 lowered to it through the reaction owner (rule 8, as a deliberate hit does); for a surrender
    /// of an NPC in combat mode, the combat-mode exit when its toggle acts (which also unlocks the target; the Original setting
    /// keeps the combat-mode bit, CL4). The hook (enter or exit combat, the target as triggerer), the alerted sound and the
    /// fleeing line are the host's. The host runs the hook after the call, so a script sees the focus and reaction already
    /// written, where the original runs it before rules 8 and 9. A fight on the hero without <see cref="SessionDangerRequest.Reaction"/>,
    /// or a surrender of an NPC in combat mode without <see cref="SessionDangerRequest.CombatMode"/> facts, is
    /// <see cref="GameSessionFailure.InvalidDangerFacts"/>.
    /// </summary>
    public SessionDangerResult SetDangerState(GameSessionVersion expected, SessionDangerRequest request)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        var failure = CheckDangerRequest(request);
        if (failure != GameSessionFailure.None) return RejectDanger(failure);
        if (!IsCurrent(expected)) return RejectDanger(GameSessionFailure.StaleVersion);
        var plan = new DangerPlan(this);
        var result = plan.Set(request, request.AlertSound);
        return result.Failure != GameSessionFailure.None ? RejectDanger(result.Failure) : plan.Commit(result);
    }

    /// <summary>
    /// The target lock (0x4AFBD0, the follower fan-out of an ordered attack): unlock, the setter asked for COMBAT_FOCUS on the
    /// target (no sound), and the lock set only when the state read then reports COMBAT_FOCUS on that target. The request's
    /// state must be COMBAT_FOCUS and its target set. One revision for the whole step.
    /// </summary>
    [OriginalFunction(0x4AFBD0, OriginalFunctionScope.Full)]
    public SessionDangerResult LockDangerTarget(GameSessionVersion expected, SessionDangerRequest request)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        var failure = CheckDangerRequest(request);
        if (failure == GameSessionFailure.None && request.Target is null) failure = GameSessionFailure.InvalidDangerTarget;
        if (failure == GameSessionFailure.None && request.State != DangerState.CombatFocus) failure = GameSessionFailure.InvalidDangerFacts;
        if (failure != GameSessionFailure.None) return RejectDanger(failure);
        if (!IsCurrent(expected)) return RejectDanger(GameSessionFailure.StaleVersion);
        var plan = new DangerPlan(this);
        plan.Update(request.Entity, record => record with { Flags = record.Flags & ~DangerFlags.TargetLocked });
        var result = plan.Set(request, false);
        if (result.Failure != GameSessionFailure.None) return RejectDanger(result.Failure);
        var after = plan.Get(request.Entity);
        var locked = after.State == DangerState.CombatFocus && after.Focus == request.Target;
        if (locked) plan.Update(request.Entity, record => record with { Flags = record.Flags | DangerFlags.TargetLocked });
        return plan.Commit(result with { Locked = locked });
    }

    /// <summary>The target unlock (0x4AFC70): the lock bit cleared.</summary>
    [OriginalFunction(0x4AFC70, OriginalFunctionScope.Full)]
    public SessionDangerResult UnlockDangerTarget(GameSessionVersion expected, string entity) =>
        ChangeDanger(expected, entity, null, false, plan => plan.Update(entity, record => record with { Flags = record.Flags & ~DangerFlags.TargetLocked }));

    /// <summary>Who-hit-me-last := <paramref name="attacker"/> (none clears it; the attack event's step 10 and peace, slice I4).</summary>
    public SessionDangerResult SetDangerHitBy(GameSessionVersion expected, string entity, SessionCombatant? attacker) =>
        ChangeDanger(expected, entity, attacker, false, plan => plan.Update(entity, record => record with { HitBy = attacker }));

    /// <summary>
    /// The enemy-list add (<see cref="DangerRules.SkipsEnemy"/>): skipped when the owner follows or waits for the hero and the
    /// entry is the hero or another of its followers, or when the entry is listed; otherwise appended. The original's
    /// presentation refresh of the owner's combat category (0x4B80E0) is the host's.
    /// </summary>
    public SessionDangerResult AddDangerEnemy(GameSessionVersion expected, string owner, SessionCombatant entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ChangeDanger(expected, owner, entry, false, plan => plan.AddEnemy(owner, entry));
    }

    /// <summary>
    /// The team add (0x4AF8C0): head := the offender's PC leader (the hero for its followers), else the offender; for the hero
    /// the team is the hero and then its follower array in order, otherwise the head alone; each member goes through
    /// <see cref="AddDangerEnemy"/>.
    /// </summary>
    [OriginalFunction(0x4AF8C0, OriginalFunctionScope.Partial)]
    public SessionDangerResult AddDangerEnemyTeam(GameSessionVersion expected, string owner, SessionCombatant offender)
    {
        ArgumentNullException.ThrowIfNull(offender);
        return ChangeDanger(expected, owner, offender, false, plan => plan.AddTeam(owner, offender));
    }

    /// <summary>The enemy-list remove (<see cref="DangerRules.RemoveEnemy"/>): every entry equal to <paramref name="entry"/>, the
    /// last entry filling each hole.</summary>
    public SessionDangerResult RemoveDangerEnemy(GameSessionVersion expected, string owner, SessionCombatant entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ChangeDanger(expected, owner, entry, false, plan =>
            plan.Update(owner, record => record with { Enemies = DangerRules.RemoveEnemy(record.Enemies, entry) }));
    }

    /// <summary>
    /// Allow attacking (0x4AA580): an NPC that follows or waits for the hero loses NO_ATTACK; for the hero every NPC of its
    /// follower array does (the original's recursive follower list; the session has no followers of followers). Any other NPC
    /// is NoChange. The hero's combat-mode entry runs it for the hero.
    /// </summary>
    [OriginalFunction(0x4AA580, OriginalFunctionScope.Partial)]
    public SessionDangerResult AllowAttacking(GameSessionVersion expected, SessionCombatant combatant)
    {
        ArgumentNullException.ThrowIfNull(combatant);
        return ChangeDanger(expected, combatant.Entity, null, combatant.IsHero, plan => plan.AllowAttacking(combatant));
    }

    // ---- shared steps ----

    // The owner id (none only for the hero, which AllowAttacking alone passes) and the handle: valid ids, then the version.
    private SessionDangerResult ChangeDanger(GameSessionVersion expected, string? entity, SessionCombatant? handle, bool hero, Action<DangerPlan> step)
    {
        ThrowIfDisposed();
        if (!(hero ? entity is null : SessionValidation.Entity(entity)) || !ValidDangerHandle(handle)) return RejectDanger(GameSessionFailure.InvalidDangerTarget);
        if (!IsCurrent(expected)) return RejectDanger(GameSessionFailure.StaleVersion);
        var plan = new DangerPlan(this);
        step(plan);
        return plan.Commit(new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version));
    }

    private static GameSessionFailure CheckDangerRequest(SessionDangerRequest request)
    {
        if (!SessionValidation.Entity(request.Entity) || !ValidDangerHandle(request.Target)) return GameSessionFailure.InvalidDangerTarget;
        if (!Enum.IsDefined(request.State)) return GameSessionFailure.InvalidDangerFacts;
        if (request.Reaction is { } reaction && (!ReactionRules.IsValid(reaction.Npc) || !ReactionRules.IsValid(reaction.Pc) ||
                !ReactionLimits.InRange(reaction.HostileReaction)))
            return GameSessionFailure.InvalidDangerFacts;
        if (request.CombatMode is { Facts: { } facts } && !ValidFacts(facts)) return GameSessionFailure.InvalidDangerFacts;
        return GameSessionFailure.None;
    }

    private static bool ValidDangerHandle(SessionCombatant? handle) => handle is null || handle.IsHero || SessionValidation.Entity(handle.Entity);

    private SessionDangerResult RejectDanger(GameSessionFailure failure) =>
        new(GameSessionDisposition.Rejected, failure, new(_identity, _revision));

    // ARC-WORLD-079: leaving combat mode unlocks the NPC's target (the toggle's call 0x4B411C), for every exit of an NPC.
    private void UnlockOnCombatModeExit(string entity)
    {
        var record = _danger.Get(entity);
        if (record.Flags.HasFlag(DangerFlags.TargetLocked)) _danger.Put(record with { Flags = record.Flags & ~DangerFlags.TargetLocked });
    }

    // The hero entering combat mode allows its followers to attack (the toggle's call 0x4B42B2).
    private void AllowFollowersOnCombatModeEntry()
    {
        foreach (var follower in _party.Order)
        {
            var record = _danger.Get(follower);
            if (record.Flags.HasFlag(DangerFlags.NoAttack)) _danger.Put(record with { Flags = record.Flags & ~DangerFlags.NoAttack });
        }
    }

    // Records are unique, in ordinal order, not at the baseline, with defined bits, valid handles, unique enemies and within the
    // caller's budgets.
    private static GameSessionFailure ImportDanger(SessionDangerState? saved, GameSessionLimits limits, out DangerDomain danger)
    {
        danger = new();
        if (saved?.Records is not { } records) return GameSessionFailure.InvalidSnapshot;
        if (records.Count > limits.MaxDangerRecords) return GameSessionFailure.BudgetExceeded;
        string? previous = null;
        foreach (var record in records)
        {
            if (record is null || !SessionValidation.Entity(record.Entity)) return GameSessionFailure.InvalidDangerTarget;
            if (previous is not null && string.CompareOrdinal(previous, record.Entity) >= 0) return GameSessionFailure.InvalidSnapshot;
            previous = record.Entity;
            if ((record.Flags & ~DangerFlags.All) != 0 || record.IsBaseline || record.Enemies is null) return GameSessionFailure.InvalidSnapshot;
            if (!ValidDangerHandle(record.Focus) || !ValidDangerHandle(record.FleeingFrom) || !ValidDangerHandle(record.HitBy) ||
                record.Enemies.Any(entry => entry is null || !ValidDangerHandle(entry)))
                return GameSessionFailure.InvalidDangerTarget;
            if (record.Enemies.Distinct().Count() != record.Enemies.Count) return GameSessionFailure.InvalidSnapshot;
            if (danger.Enemies + record.Enemies.Count > limits.MaxDangerEnemies) return GameSessionFailure.BudgetExceeded;
            danger.Put(record);
        }
        return GameSessionFailure.None;
    }

    /// <summary>
    /// The staged changes of one danger call: records of the danger domain, NPCs' combat-mode exits and reaction writes. Nothing
    /// touches the owner until <see cref="Commit"/>, which checks the budgets and the revision and applies them in one revision.
    /// </summary>
    private sealed class DangerPlan(GameSessionOwner owner)
    {
        private readonly Dictionary<string, SessionDanger> _staged = new(StringComparer.Ordinal);
        private readonly HashSet<string> _leavesCombatMode = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (SessionReaction? Write, ReactionAdjustment Adjustment)> _reactions = new(StringComparer.Ordinal);

        internal SessionDanger Get(string entity) => _staged.TryGetValue(entity, out var record) ? record : owner._danger.Get(entity);

        internal void Update(string entity, Func<SessionDanger, SessionDanger> step) => _staged[entity] = step(Get(entity));

        internal void AddEnemy(string entity, SessionCombatant entry)
        {
            var record = Get(entity);
            var sameLeader = owner.LedByPc(entity) && (entry.IsHero || owner.LedByPc(entry.Entity!));
            if (DangerRules.SkipsEnemy(record.Enemies, entry, sameLeader)) return;
            _staged[entity] = record with { Enemies = [.. record.Enemies, entry] };
        }

        // The team add (0x4AF8C0): the offender's PC leader (the hero for its followers) or the offender, and for the hero its
        // follower array in order.
        internal void AddTeam(string entity, SessionCombatant offender)
        {
            var head = offender.IsHero || owner.LedByPc(offender.Entity!) ? SessionCombatant.Hero : offender;
            AddEnemy(entity, head);
            if (head.IsHero)
                foreach (var follower in owner._party.Order) AddEnemy(entity, SessionCombatant.Critter(follower));
        }

        internal void AllowAttacking(SessionCombatant combatant)
        {
            var npcs = combatant.IsHero ? owner._party.Order.ToArray()
                : owner.LedByPc(combatant.Entity!) ? new[] { combatant.Entity! } : System.Array.Empty<string>();
            foreach (var npc in npcs) Update(npc, record => record with { Flags = record.Flags & ~DangerFlags.NoAttack });
        }

        // The setter for one NPC; Failure is set when a needed fact is missing or a reaction write is out of range.
        internal SessionDangerResult Set(SessionDangerRequest request, bool alertSound)
        {
            var entity = request.Entity;
            var record = Get(entity);
            var target = request.Target;
            var profile = owner._rules.Combat;
            var step = DangerRules.Set(record.Flags, request.State, request.Facts with { TargetIsPc = target?.IsHero == true }, profile);
            var flags = step.Flags;
            var left = false;
            if (step.LeavesCombatMode && InCombatMode(entity))
            {
                if (request.CombatMode?.Facts is null) return Failed(GameSessionFailure.InvalidDangerFacts);
                // The toggle's refusals decide whether it acts; when it does it unlocks the target (0x4B411C) and the Original
                // setting then writes the combat-mode bit back (CL4).
                left = owner.NpcCombatModeExitActs(entity, request.CombatMode);
                if (left)
                {
                    flags &= ~DangerFlags.TargetLocked;
                    if (!profile.SurrenderKeepsCombatModeFlag) _leavesCombatMode.Add(entity);
                }
            }
            var next = record with { Flags = flags };
            if (step.WritesFleeingFrom) next = next with { FleeingFrom = target };
            if (step.WritesFocus) next = next with { Focus = target };
            ReactionAdjustment? adjustment = null;
            if (step.LowersReaction)
            {
                if (request.Reaction is not { } reaction) return Failed(GameSessionFailure.InvalidDangerFacts);
                var failure = PlanReactionTo(entity, reaction.Npc, reaction.Pc, record.Enemies.Contains(SessionCombatant.Hero), reaction.HostileReaction,
                    ReactionQuery.Hostility, lower: true, out adjustment);
                if (failure != GameSessionFailure.None) return Failed(failure);
            }
            _staged[entity] = next;
            SessionDangerHook? hook = step.Script is { } point
                ? new SessionDangerHook(point, target, entity) : null;
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, owner.Version)
            {
                Transition = step with { AlertSound = step.AlertSound && alertSound }, Hook = hook, LeftCombatMode = left, Reaction = adjustment,
            };
        }

        // An NPC in combat mode, after this plan's exits.
        internal bool InCombatMode(string entity) => owner._combat.Critters.Contains(entity) && !_leavesCombatMode.Contains(entity);

        // The combat-mode exit of an NPC (0x4B3F20) when its toggle acts; it unlocks the target. False when it is not in combat mode
        // or the toggle refuses; Failure when its facts are missing.
        internal GameSessionFailure LeaveCombatMode(string entity, SessionCombatModeContext? context, out bool left)
        {
            left = false;
            if (!InCombatMode(entity)) return GameSessionFailure.None;
            if (context?.Facts is null) return GameSessionFailure.InvalidDangerFacts;
            left = owner.NpcCombatModeExitActs(entity, context);
            if (!left) return GameSessionFailure.None;
            _leavesCombatMode.Add(entity);
            Update(entity, record => record with { Flags = record.Flags & ~DangerFlags.TargetLocked });
            return GameSessionFailure.None;
        }

        // The reaction of <entity> toward the hero moved to <bound> through the PC's effects (0x4C0DE0): lowered to it when above
        // (setter rule 8, the hostility variant) or raised to it when below (peace, the standard reaction). heroListed is whether
        // the hero is on the NPC's enemy list at that moment.
        internal GameSessionFailure PlanReactionTo(string entity, ReactionNpcFacts facts, ReactionPcFacts pc, bool heroListed, int bound,
            ReactionQuery query, bool lower, out ReactionAdjustment? planned)
        {
            planned = null;
            var npc = facts with { PcOnEnemyList = heroListed };
            var record = _reactions.TryGetValue(entity, out var staged) && staged.Write is { } write ? write : owner._reactions.Get(entity);
            var stored = record.Stored(npc.BaseReaction);
            var value = ReactionRules.Effective(stored, npc, pc, query);
            if (lower ? value <= bound : value >= bound) return GameSessionFailure.None;
            var delta = ReactionRules.DeltaTo(bound, value);
            if (!ReactionLimits.InRange(delta)) return GameSessionFailure.InvalidReactionAmount;
            var failure = PlanReaction(record, stored, (int)delta, npc, pc, owner.LedByPc(entity), out var adjustment, out var updated);
            planned = adjustment;
            if (failure != GameSessionFailure.None) return failure;
            _reactions[entity] = (adjustment.Applies ? updated : staged.Write, adjustment);
            return GameSessionFailure.None;
        }

        private SessionDangerResult Failed(GameSessionFailure failure) =>
            new(GameSessionDisposition.Rejected, failure, owner.Version);

        // Budgets, then the revision; an unchanged plan is NoChange with the caller's details.
        internal TResult Commit<TResult>(TResult result, Func<TResult, GameSessionDisposition, GameSessionVersion, TResult> with,
            Func<GameSessionFailure, TResult> reject)
        {
            var danger = owner._danger;
            var changed = _staged.Values.Where(record => !(danger.Records.TryGetValue(record.Entity, out var current) ? current.SameAs(record) : record.IsBaseline)).ToArray();
            var writes = _reactions.Values.Select(r => r.Write).OfType<SessionReaction>()
                .Where(next => !(owner._reactions.Records.TryGetValue(next.Entity, out var stored) ? stored == next : next.IsBaseline)).ToArray();
            var armed = _reactions.Where(r => r.Value.Adjustment.LeaderCheck).Select(r => owner._party.Get(r.Key))
                .Select(c => (Before: c, After: c with { Loyalty = CompanionLoyaltyRules.ArmReaction(c.Loyalty) }))
                .Where(c => c.After != c.Before).Select(c => c.After).ToArray();
            var leaving = _leavesCombatMode.Where(owner._combat.Critters.Contains).ToArray();
            if (changed.Length == 0 && leaving.Length == 0 && writes.Length == 0 && armed.Length == 0)
                return with(result, GameSessionDisposition.NoChange, owner.Version);
            var records = danger.Records.Count + changed.Count(record => !record.IsBaseline && !danger.Records.ContainsKey(record.Entity))
                - changed.Count(record => record.IsBaseline && danger.Records.ContainsKey(record.Entity));
            var enemies = danger.Enemies + changed.Sum(record => record.Enemies.Count -
                (danger.Records.TryGetValue(record.Entity, out var current) ? current.Enemies.Count : 0));
            if (records > owner._limits.MaxDangerRecords || enemies > owner._limits.MaxDangerEnemies)
                return reject(GameSessionFailure.BudgetExceeded);
            var added = writes.Count(r => !owner._reactions.Records.ContainsKey(r.Entity) && !r.IsBaseline);
            if (added > 0 && owner._reactions.Records.Count + added > owner._limits.MaxReactionRecords)
                return reject(GameSessionFailure.BudgetExceeded);
            if (owner._revision == long.MaxValue) return reject(GameSessionFailure.RevisionExhausted);
            foreach (var record in changed) danger.Put(record);
            foreach (var entity in leaving) owner._combat.Critters.Remove(entity);
            foreach (var write in writes)
            {
                if (write.IsBaseline) owner._reactions.Records.Remove(write.Entity);
                else owner._reactions.Records[write.Entity] = write;
            }
            // ARC-WORLD-045 (OL17): a reaction drop of a follower arms its leader check in the same revision.
            foreach (var arm in armed) owner._party.Put(arm, false);
            owner.Revise();
            return with(result, GameSessionDisposition.Changed, owner.Version);
        }

        internal SessionDangerResult Commit(SessionDangerResult result) =>
            Commit(result, (r, disposition, version) => r with { Disposition = disposition, Version = version }, owner.RejectDanger);
    }
}

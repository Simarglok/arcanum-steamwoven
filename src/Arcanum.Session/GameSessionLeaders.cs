using Arcanum.Simulation;
using Arcanum.Simulation.Party;

namespace Arcanum.Session;

/// <summary>
/// ARC-WORLD-1840 (#1322, owner decision 2026-11-13: an NPC leads followers as in the original). In the original every critter has a leader handle (field 0x119, read by 0x45DE00) and a follower
/// array (field 0xF1); the join (0x45DE90) and the leave (0x45DFC0, the removal 0x45E040) work under any leader, and the chain query 0x45DDA0 walks the leaders up to a player character. The session
/// keeps the hero's party as it was (<see cref="SessionCompanion"/>, the hero's array) and beside it the followers of leaders that are not the hero (<see cref="SessionNpcFollower"/>); a critter has
/// one leader, so a join under one leader first leaves the other, the hero included.
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>ARC-WORLD-1840: the bound of followers of NPC leaders the session holds (the summons of NPC casters and the scripts' NPC groups; the census counts a few dozen at most).</summary>
    public const int MaxNpcFollowers = 256;

    /// <summary>
    /// The leader of <paramref name="critter"/> (0x45DE00): the hero (<see cref="SessionEventKinds.PlayerEntity"/>) for an NPC the hero leads (following or waiting), the NPC leader of a follower
    /// of an NPC, else null; the hero has none. An invalid id throws.
    /// </summary>
    [OriginalFunction(0x45DE00, OriginalFunctionScope.Full)]
    public string? QueryLeader(string critter)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(critter)) throw new ArgumentException("Invalid entity id.", nameof(critter));
        return LeaderOf(critter);
    }

    /// <summary>
    /// The player character at the top of <paramref name="critter"/>'s chain of leaders (0x45DDA0): the hero when the chain reaches it, else null. Like the original it starts from the leader, so
    /// the hero itself answers null. The session never holds a cycle (a join that would close one is refused), so the walk ends. An invalid id throws.
    /// </summary>
    [OriginalFunction(0x45DDA0, OriginalFunctionScope.Full)]
    public string? QueryPcLeader(string critter)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(critter)) throw new ArgumentException("Invalid entity id.", nameof(critter));
        return PcLeaderOf(critter);
    }

    /// <summary>The follower array of <paramref name="leader"/> in order (field 0xF1): the hero's party array for the hero, the NPC followers of an NPC leader; empty for a critter that leads no one. An invalid id throws.</summary>
    public IReadOnlyList<string> QueryFollowers(string leader)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(leader)) throw new ArgumentException("Invalid entity id.", nameof(leader));
        return FollowersOf(leader);
    }

    /// <summary>
    /// The forced join (0x45DE90 with force 1, no follow check) of <paramref name="entity"/> under <paramref name="leader"/>, any critter: the hero's is <see cref="ForceJoinParty"/>. Under an NPC
    /// leader the joiner first leaves the leader it has (0x45DFC0 with force 1: a follower or waiting NPC of the hero leaves the hero's party with the leave's side effects; a follower of another NPC
    /// leaves that array), then it is appended at the end of the leader's array, forced, jilted cleared (the NPC flag 0x20 the join clears), the leader's concealment taken (0x45EE30) and the
    /// leader's Tempus Fugit flag copied (TF3). Already the last follower of that leader is NoChange. The hero follows no one, a critter does not lead itself, and a join that would close a cycle of
    /// leaders (the original's chain query would never end) is <see cref="GameSessionFailure.InvalidCompanion"/>; a full bound is <see cref="GameSessionFailure.BudgetExceeded"/> with nothing changed.
    /// </summary>
    public PartyMutationResult ForceJoinLeader(GameSessionVersion expected, string entity, string leader)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity) || !SessionValidation.Entity(leader) || entity == SessionEventKinds.PlayerEntity) return RejectParty(GameSessionFailure.InvalidCompanion);
        if (leader == SessionEventKinds.PlayerEntity) return ForceJoinParty(expected, entity);
        if (!IsCurrent(expected)) return RejectParty(GameSessionFailure.StaleVersion);
        if (entity == leader || LeadsUpTo(leader, entity)) return RejectParty(GameSessionFailure.InvalidCompanion);
        var linked = _party.NpcLed.TryGetValue(entity, out var current);
        if (linked && current.Leader == leader && current.Forced && FollowersOf(leader).LastOrDefault() == entity)
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, new(_identity, _revision));
        if (!linked && _party.NpcLed.Count >= MaxNpcFollowers) return RejectParty(GameSessionFailure.BudgetExceeded);
        var copyTempus = JoinCopiesTempusFugit(entity, leader);
        if (copyTempus && !_objectFlags.ContainsKey(entity) && _objectFlags.Count >= MaxObjectFlagCritters) return RejectParty(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectParty(GameSessionFailure.RevisionExhausted);
        var hero = _party.Get(entity);
        if (hero.Standing != CompanionStanding.Free) LeaveHero(hero);
        else if (linked) LeaveNpcLeader(entity);
        // The join clears the NPC flag 0x20 (jilted); the WAIT bit it does not touch (CF9), and like the hero's join the session drops the timer of a free NPC that joins with it.
        // ARC-WORLD-2116: the flag on a follower of an NPC means it waits outside the leader's array, so a free NPC that carries it (CF9 keeps it in the original, in the array) joins an NPC without it.
        var record = _party.Get(entity);
        if (record.Jilted || record.WaitFlag) _party.Put(record with { Jilted = false, WaitFlag = false }, false);
        if (hero.WaitBit) CancelWaitTimers(entity);
        _party.PutNpcLed(new(entity, leader, true));
        if (copyTempus) WriteTempusFugitOnJoin(entity);
        // 0x45EE30 gives the joiner its leader's concealment; the session's concealment is the hero's party's (its toggle, its end on an attack and its save cover the hero's followers),
        // so a follower of an NPC is never concealed here (named: a companion's summon does not sneak with it).
        _combat.Concealed.Remove(entity);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, null, new(_identity, _revision));
    }

    private string? LeaderOf(string critter) =>
        critter == SessionEventKinds.PlayerEntity ? null
        : _party.Get(critter).LedByPc ? SessionEventKinds.PlayerEntity
        : _party.NpcLed.TryGetValue(critter, out var led) ? led.Leader : null;

    private string? PcLeaderOf(string critter)
    {
        var at = critter;
        // Each step climbs one leader; the session holds no cycle, so the chain is at most every NPC-led follower long and then reaches the hero or a critter with no leader.
        for (var step = 0; step <= _party.NpcLed.Count + 1; step++)
        {
            if (LeaderOf(at) is not { } next) return null;
            if (next == SessionEventKinds.PlayerEntity) return next;
            at = next;
        }
        return null;
    }

    // ARC-WORLD-1864: whether the chain of leaders of `critter` reaches the hero (0x45DDA0 not null), the test of the original's "PC leader" sites; LedByPc is the direct test (0x45DE00 names the hero).
    private bool PcLeads(string critter) => PcLeaderOf(critter) is not null;

    // The follower tree of `leader` as 0x441260 builds it: its direct followers in array order, then each follower's own tree (recursively, in the same order) after them.
    private List<string> FollowerTreeOf(string leader)
    {
        var tree = FollowersOf(leader).ToList();
        foreach (var follower in tree.ToArray()) tree.AddRange(FollowerTreeOf(follower));
        return tree;
    }

    // ARC-WORLD-2116: a follower that waits under its NPC leader (the wait flag on its record) has left the leader's array (0x4AA7A0's forced leave) and kept the leader handle.
    private IReadOnlyList<string> FollowersOf(string leader) =>
        leader == SessionEventKinds.PlayerEntity ? _party.Order.ToArray()
        : _party.NpcArrays.TryGetValue(leader, out var array) ? array.Where(entity => !_party.Get(entity).WaitFlag).ToArray() : [];

    // Whether the critter waits under an NPC leader: the wait flag with an NPC leader kept (the hero's waiting NPC is a standing, a leaderless one has no leader).
    private bool WaitsUnderNpcLeader(string entity) => _party.NpcLed.ContainsKey(entity) && _party.Get(entity).WaitFlag;

    // The forced unwait of a follower waiting under an NPC leader (0x4AA8C0 with force 1, then the join 0x45DE90 with force 1): the flag and the timers go, the leader is joined again, appended at the end of
    // its array, forced, jilted cleared, the leader's Tempus Fugit flag copied. The caller has checked the wait and has the revision room.
    private void RejoinNpcLeader(string entity, bool forced = true)
    {
        var led = _party.NpcLed[entity];
        var record = _party.Get(entity);
        _party.Put(record with { WaitFlag = false, Jilted = false }, false);
        CancelWaitTimers(entity);
        _party.RemoveNpcLed(entity);
        _party.PutNpcLed(new(entity, led.Leader, forced));
        ClearTempusFugitOnLeave(entity);
        if (JoinCopiesTempusFugit(entity, led.Leader) && (_objectFlags.ContainsKey(entity) || _objectFlags.Count < MaxObjectFlagCritters)) WriteTempusFugitOnJoin(entity);
        _combat.Concealed.Remove(entity);
    }

    private PartyMutationResult UnwaitUnderNpcLeader(string entity)
    {
        // The join's Tempus Fugit copy needs room in the written-flags bound: a full bound refuses before anything changes, as every join does (the death's forced unwait alone leaves the copy out).
        if (JoinCopiesTempusFugit(entity, _party.NpcLed[entity].Leader) && !_objectFlags.ContainsKey(entity) && _objectFlags.Count >= MaxObjectFlagCritters) return RejectParty(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectParty(GameSessionFailure.RevisionExhausted);
        RejoinNpcLeader(entity);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, null, new(_identity, _revision));
    }

    /// <summary>
    /// ARC-WORLD-2127: the unforced unwait of a follower waiting under an NPC leader L (see the ledger rows NLD5 and AP3): the follow check with L as the player runs first, a refusal changes nothing (no line,
    /// no reaction change: it keeps waiting); an acceptance clears the WAIT bit, the handle and the timers and the unforced join runs the check again with the handle cleared: a refusal there
    /// leaves the follower with no leader, not waiting and not jilted (<see cref="PartyMutationResult.Consent"/> then carries that refusal, the disposition stays Changed), else it is appended at the end of
    /// L's array, not forced, jilted cleared, L's Tempus Fugit flag copied. L's side comes from the caller (<see cref="NpcLeaderUnwaitFacts"/>: the session holds no NPC's stats); the session owns the reaction
    /// (0 when L is on the follower's enemy list, else the fixed 50 of a leader that is no player character), the forced flag (cleared by the wait's leave), the charisma limit (never ignored) and the
    /// count of L's array without forced entries (the waiting follower is out of the array in both checks). Refused, in order: the entity, the training, the version, a follower not waiting under an NPC leader
    /// (<see cref="GameSessionFailure.NotWaiting"/>), the first check (<see cref="GameSessionFailure.ConsentRefused"/>), the written-flags bound for the Tempus Fugit copy, the revision.
    /// </summary>
    public PartyMutationResult UnwaitNpcLed(GameSessionVersion expected, string entity, NpcLeaderUnwaitFacts facts)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(facts);
        if (!SessionValidation.Entity(entity)) return RejectParty(GameSessionFailure.InvalidCompanion);
        if (!Enum.IsDefined(facts.LeaderPersuasion)) return RejectParty(GameSessionFailure.InvalidConsentFacts);
        if (!IsCurrent(expected)) return RejectParty(GameSessionFailure.StaleVersion);
        if (!WaitsUnderNpcLeader(entity)) return RejectParty(GameSessionFailure.NotWaiting);
        var leader = _party.NpcLed[entity].Leader;
        var first = NpcLeaderConsent(entity, leader, facts, facts.MindControlAccepted);
        if (first != FollowConsent.Accepted) return RejectParty(GameSessionFailure.ConsentRefused, first);
        var second = NpcLeaderConsent(entity, leader, facts, facts.MindControlAcceptedAfterUnlink);
        if (second == FollowConsent.Accepted && JoinCopiesTempusFugit(entity, leader) && !_objectFlags.ContainsKey(entity) && _objectFlags.Count >= MaxObjectFlagCritters)
            return RejectParty(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectParty(GameSessionFailure.RevisionExhausted);
        if (second == FollowConsent.Accepted) RejoinNpcLeader(entity, false);
        else
        {
            _party.Put(_party.Get(entity) with { WaitFlag = false }, false);
            CancelWaitTimers(entity);
            _party.RemoveNpcLed(entity);
        }
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, second, new(_identity, _revision));
    }

    // One follow check of the unwait or its join with an NPC leader as the player (the step "another leader" is skipped: the follower's leader is L itself, or none).
    private FollowConsent NpcLeaderConsent(string entity, string leader, NpcLeaderUnwaitFacts facts, bool mindControlAccepted)
    {
        var onEnemyList = _danger.Get(entity).Enemies.Contains(SessionCombatant.Critter(leader));
        var nonForced = _party.NpcArrays.TryGetValue(leader, out var array)
            ? array.Count(member => !_party.NpcLed[member].Forced && !_party.Get(member).WaitFlag) : 0;
        return FollowConsentRules.Evaluate(new(mindControlAccepted, facts.LeaderPersuasion, 0, facts.LeaderAlignment, facts.LeaderLevel, null, false,
            onEnemyList ? 0 : NpcLeaderFixedReaction, facts.Thresholds, facts.NpcAlignment, facts.NpcLevel, facts.MaxFollowers, nonForced, false));
    }

    /// <summary>ARC-WORLD-2127 (0x4C0D00): the reaction toward a leader that is no player character, when it is not on the follower's enemy list.</summary>
    private const int NpcLeaderFixedReaction = 50;

    // Whether `critter`'s chain of leaders reaches `ancestor` (a join of `ancestor` under `critter` would close a cycle).
    private bool LeadsUpTo(string critter, string ancestor)
    {
        var at = critter;
        for (var step = 0; step <= _party.NpcLed.Count + 1; step++)
        {
            if (LeaderOf(at) is not { } next) return false;
            if (next == ancestor) return true;
            at = next;
        }
        return false;
    }

    // The leave half of a join under another leader for a critter the hero leads (0x45DFC0 with force 1, the forced unwait 0x4AA8C0 first): the record goes free (forced and the WAIT bit cleared),
    // its concealment ends, the hero's no-attack order goes with it, the Tempus Fugit flag is cleared and a waiting NPC's wait timer is removed (OL25). The caller's revision covers the writes.
    private void LeaveHero(SessionCompanion current)
    {
        _party.Put(current with { Standing = CompanionStanding.Free, Forced = false, WaitFlag = false }, false);
        _combat.Concealed.Remove(current.Entity);
        ClearNoAttackOnLeave(current.Entity);
        ClearTempusFugitOnLeave(current.Entity);
        if (current.WaitBit) CancelWaitTimers(current.Entity);
    }

    // The leave of a follower of an NPC (0x45DFC0 → the forced unwait 0x4AA8C0 → 0x45E040): the WAIT bit cleared and its timer removed, out of the leader's array (the gap closed), the forced bit
    // with the record, the Tempus Fugit flag cleared (0x459EA0) and the concealment ended (0x45EE30 with 0). The caller's revision covers the writes.
    private void LeaveNpcLeader(string entity)
    {
        var record = _party.Get(entity);
        if (record.WaitFlag)
        {
            _party.Put(record with { WaitFlag = false }, false);
            CancelWaitTimers(entity);
        }
        _party.RemoveNpcLed(entity);
        ClearTempusFugitOnLeave(entity);
        _combat.Concealed.Remove(entity);
    }

    // The forced leave of a follower of an NPC (LeaveParty for a critter the hero does not lead): NoChange without an NPC leader; an unforced leave is refused under an extraordinary-charisma leader
    // or a mind-controlled follower, as the hero's (the caller supplies both for an NPC leader).
    private GameSessionMutationResult LeaveNpcLeader(string entity, PartyLeave leave)
    {
        if (!_party.NpcLed.ContainsKey(entity)) return NoChange();
        if (!leave.Force && !FollowConsentRules.MayLeaveUnforced(leave.LeaderCharisma, leave.MindControlled)) return Reject(GameSessionFailure.LeaveRefused);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        LeaveNpcLeader(entity);
        return Changed();
    }

    // ARC-WORLD-1840: a destroyed critter (DestroyObject) leaves its NPC leader (0x45E180 → 0x45E040), and the followers it led lose their leader without a leave of their own (the original
    // touches them not at all: their handle names an object that no longer resolves, so they answer no leader, and they keep their flags). The caller's revision covers the writes.
    private void DisbandNpcLinks(string entity)
    {
        if (_party.NpcLed.ContainsKey(entity)) LeaveNpcLeader(entity);
        if (!_party.NpcArrays.TryGetValue(entity, out var array)) return;
        foreach (var follower in array.ToArray()) _party.RemoveNpcLed(follower);
    }
}

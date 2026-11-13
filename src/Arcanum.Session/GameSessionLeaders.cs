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
        if (linked && current.Leader == leader && current.Forced && _party.NpcArrays[leader][^1] == entity)
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, new(_identity, _revision));
        if (!linked && _party.NpcLed.Count >= MaxNpcFollowers) return RejectParty(GameSessionFailure.BudgetExceeded);
        var copyTempus = JoinCopiesTempusFugit(entity, leader);
        if (copyTempus && !_objectFlags.ContainsKey(entity) && _objectFlags.Count >= MaxObjectFlagCritters) return RejectParty(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectParty(GameSessionFailure.RevisionExhausted);
        var hero = _party.Get(entity);
        if (hero.Standing != CompanionStanding.Free) LeaveHero(hero);
        else if (linked) LeaveNpcLeader(entity);
        // The join clears the NPC flag 0x20 (jilted); the WAIT bit it does not touch (CF9), and like the hero's join the session drops the timer of a free NPC that joins with it.
        var record = _party.Get(entity);
        if (record.Jilted) _party.Put(record with { Jilted = false }, false);
        if (hero.WaitBit) CancelWaitTimers(entity);
        _party.PutNpcLed(new(entity, leader, true));
        if (copyTempus) WriteTempusFugitOnJoin(entity);
        if (_combat.Concealed.Contains(leader)) _combat.Concealed.Add(entity); else _combat.Concealed.Remove(entity);
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

    private IReadOnlyList<string> FollowersOf(string leader) =>
        leader == SessionEventKinds.PlayerEntity ? _party.Order.ToArray()
        : _party.NpcArrays.TryGetValue(leader, out var array) ? array.ToArray() : [];

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

    // The leave of a follower of an NPC (0x45DFC0 → 0x45E040): out of the leader's array (the gap closed), the forced bit with the record, the Tempus Fugit flag cleared (0x459EA0) and the concealment
    // ended (0x45EE30 with 0). The caller's revision covers the writes.
    private void LeaveNpcLeader(string entity)
    {
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

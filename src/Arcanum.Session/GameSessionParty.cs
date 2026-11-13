using Arcanum.Simulation.Party;

namespace Arcanum.Session;

/// <summary>
/// An NPC's link to the session PC (ARC-RES-024 state model). <see cref="Waiting"/> keeps the PC as leader but is
/// outside the follower array; a dead NPC is not modelled.
/// </summary>
public enum CompanionStanding { Free = 0, Following = 1, Waiting = 2 }

/// <summary>
/// One NPC's party record. <see cref="Forced"/> only while following; <see cref="Jilted"/> only while free (a join
/// clears it); <see cref="SpreadOut"/> in any standing. <see cref="Loyalty"/> (ARC-WORLD-045) are the follower's loyalty marks
/// in any standing: a reaction change arms them while the PC leads it, the loyalty step clears them, and marks armed just
/// before the leader is cleared stay until the NPC follows again (OL22). An NPC without a record is free with no flags.
/// <see cref="WaitFlag"/> (ARC-SCRIPT-1136, ledger CF8 to CF10) is the WAIT bit (0x8) of the NPC's flag word while the standing does not imply it: the dialog <c>wa</c> of an NPC with no leader sets it
/// (a free NPC, the original does not test the leader), and a join keeps it (0x45DE90 never touches that bit), so a follower in the array can carry it. A waiting standing is that bit with the PC as leader and the NPC
/// outside the array, so <see cref="WaitFlag"/> is never set with it.
/// </summary>
public readonly record struct SessionCompanion(string Entity, CompanionStanding Standing, bool Forced, bool SpreadOut, bool Jilted,
    CompanionLoyalty Loyalty = CompanionLoyalty.None, bool WaitFlag = false)
{
    /// <summary>The source's "leader is this PC" (dialog FO, SCR FOLLOWING): true while waiting too.</summary>
    public bool LedByPc => Standing != CompanionStanding.Free;

    /// <summary>The source's WAIT bit (0x8): a waiting standing, or the flag on a free or following NPC (the dialog <c>wa</c> condition reads this bit alone, CF9).</summary>
    public bool WaitBit => Standing == CompanionStanding.Waiting || WaitFlag;

    /// <summary>A wait timer can be pending for it: a waiting standing, or a free NPC with the flag (CF8 queues the timer; at its expiry nothing happens without a leader, CF9).</summary>
    internal bool HoldsWaitTimer => Standing == CompanionStanding.Waiting || (Standing == CompanionStanding.Free && WaitFlag);

    internal bool IsBaseline => Standing == CompanionStanding.Free && !SpreadOut && !Jilted && Loyalty == CompanionLoyalty.None && !WaitFlag;
}

/// <summary>
/// ARC-WORLD-1840 (#1322): a follower of a leader that is not the hero (the original's leader handle, field 0x119, and the leader's follower array, field 0xF1, which any critter has).
/// <see cref="Forced"/> is the NPC flag 0x400 (<c>ONF_FORCED_FOLLOWER</c>) the forced join sets. Its record in the hero's party (<see cref="SessionCompanion"/>) stands free: a critter has one leader.
/// </summary>
public readonly record struct SessionNpcFollower(string Entity, string Leader, bool Forced);

/// <summary>
/// Saved party state: the PC's follower array in order, then the other recorded NPCs (waiting, or free with a flag)
/// in ordinal entity order, then (ARC-WORLD-1840) the followers of leaders that are not the hero: grouped by leader in ordinal leader order, each leader's array in its order.
/// </summary>
public sealed class SessionPartyState
{
    internal SessionPartyState(SessionCompanion[] followers, SessionCompanion[] others, SessionNpcFollower[]? npcLed = null)
    {
        Followers = Array.AsReadOnly(followers);
        Others = Array.AsReadOnly(others);
        NpcLed = Array.AsReadOnly(npcLed ?? []);
    }

    public IReadOnlyList<SessionCompanion> Followers { get; }
    public IReadOnlyList<SessionCompanion> Others { get; }

    /// <summary>ARC-WORLD-1840: the followers of NPC leaders (see the class).</summary>
    public IReadOnlyList<SessionNpcFollower> NpcLed { get; }

    /// <summary>Followers that count against the PC's max followers (forced ones do not).</summary>
    public int NonForcedFollowers => Followers.Count(companion => !companion.Forced);
}

/// <summary>
/// Caller-supplied facts of one follow check (see <see cref="FollowConsentInput"/>). The session adds what it owns:
/// the NPC's forced flag, the PC's non-forced follower count and whether the charisma limit is ignored, and with a hero
/// (ARC-SKILLS-002) the PC's persuasion training, effective charisma, level and max followers in place of
/// <see cref="PcPersuasion"/>, <see cref="PcCharisma"/>, <see cref="PcLevel"/> and <see cref="MaxFollowers"/>.
/// </summary>
public sealed record PartyConsentFacts(
    bool MindControlledByPc,
    PersuasionTraining PcPersuasion,
    int PcCharisma,
    int PcAlignment,
    int PcLevel,
    int? OtherLeaderCharisma,
    int Reaction,
    FollowThresholds Thresholds,
    int NpcAlignment,
    int NpcLevel,
    int MaxFollowers);

/// <summary>
/// An unforced leave is refused while <paramref name="LeaderCharisma"/> is extraordinary or the NPC is mind
/// controlled; a forced one (dialog LV, script disband) always applies. <paramref name="Jilt"/> marks the NPC jilted
/// (the loyalty and wait-timeout paths of the source).
/// </summary>
public sealed record PartyLeave(bool Force, int LeaderCharisma = 0, bool MindControlled = false, bool Jilt = false);

/// <summary>
/// Outcome of a checked party change. <see cref="Consent"/> is the follow check's result when one ran (also on a
/// <see cref="GameSessionFailure.ConsentRefused"/> refusal), else null.
/// </summary>
public readonly record struct PartyMutationResult(GameSessionDisposition Disposition, GameSessionFailure Failure,
    FollowConsent? Consent, GameSessionVersion Version);

/// <summary>Mutable party domain of one owner.</summary>
internal sealed class PartyDomain
{
    internal List<string> Order { get; } = [];
    internal Dictionary<string, SessionCompanion> Records { get; } = new(StringComparer.Ordinal);

    internal SessionCompanion Get(string entity) =>
        Records.TryGetValue(entity, out var record) ? record : new(entity, CompanionStanding.Free, false, false, false);

    internal int NonForcedFollowers => Order.Count(entity => !Records[entity].Forced);

    /// <summary>Stores a record, keeping the follower array: a follower is (re)appended at the end, others leave it.</summary>
    internal void Put(SessionCompanion record, bool append)
    {
        if (append || record.Standing != CompanionStanding.Following) Order.Remove(record.Entity);
        if (append) Order.Add(record.Entity);
        if (record.IsBaseline) Records.Remove(record.Entity);
        else Records[record.Entity] = record;
    }

    internal SessionPartyState Capture() => new(
        Order.Select(entity => Records[entity]).ToArray(),
        Records.Values.Where(record => record.Standing != CompanionStanding.Following)
            .OrderBy(record => record.Entity, StringComparer.Ordinal).ToArray(),
        NpcArrays.OrderBy(pair => pair.Key, StringComparer.Ordinal).SelectMany(pair => pair.Value.Select(entity => NpcLed[entity])).ToArray());

    /// <summary>ARC-WORLD-1840: the follower arrays of leaders that are not the hero (leader → followers in join order) and each such follower's record. A leader with no follower has no entry.</summary>
    internal Dictionary<string, List<string>> NpcArrays { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, SessionNpcFollower> NpcLed { get; } = new(StringComparer.Ordinal);

    /// <summary>Appends a follower at the end of its NPC leader's array (the caller has removed any earlier link).</summary>
    internal void PutNpcLed(SessionNpcFollower record)
    {
        if (!NpcArrays.TryGetValue(record.Leader, out var array)) NpcArrays[record.Leader] = array = [];
        array.Add(record.Entity);
        NpcLed[record.Entity] = record;
    }

    /// <summary>Takes a follower out of its NPC leader's array (the original's leave 0x45E040 closes the gap); false when it has no NPC leader.</summary>
    internal bool RemoveNpcLed(string entity)
    {
        if (!NpcLed.Remove(entity, out var record)) return false;
        var array = NpcArrays[record.Leader];
        array.Remove(entity);
        if (array.Count == 0) NpcArrays.Remove(record.Leader);
        return true;
    }
}

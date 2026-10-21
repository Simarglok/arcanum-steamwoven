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
/// </summary>
public readonly record struct SessionCompanion(string Entity, CompanionStanding Standing, bool Forced, bool SpreadOut, bool Jilted,
    CompanionLoyalty Loyalty = CompanionLoyalty.None)
{
    /// <summary>The source's "leader is this PC" (dialog FO, SCR FOLLOWING): true while waiting too.</summary>
    public bool LedByPc => Standing != CompanionStanding.Free;

    internal bool IsBaseline => Standing == CompanionStanding.Free && !SpreadOut && !Jilted && Loyalty == CompanionLoyalty.None;
}

/// <summary>
/// Saved party state: the PC's follower array in order, then the other recorded NPCs (waiting, or free with a flag)
/// in ordinal entity order.
/// </summary>
public sealed class SessionPartyState
{
    internal SessionPartyState(SessionCompanion[] followers, SessionCompanion[] others)
    {
        Followers = Array.AsReadOnly(followers);
        Others = Array.AsReadOnly(others);
    }

    public IReadOnlyList<SessionCompanion> Followers { get; }
    public IReadOnlyList<SessionCompanion> Others { get; }

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
            .OrderBy(record => record.Entity, StringComparer.Ordinal).ToArray());
}

using Arcanum.Simulation;

namespace Arcanum.Session;

/// <summary>
/// ARC-UI-082 (ledger LB21): the PC's kill statistics and injuries, part of the story domain. The original keeps them in the PC's logbook field 0x10C
/// (the kill writer 0x4F5330, called when a PC or a PC's follower kills an NPC; the injury writer 0x4F5690, called by the damage function when a hit
/// blinds, cripples or scars a PC). The session keeps the same numbers. NOT READY: nothing feeds them from combat yet (the attack steps do not
/// know the victim's description number, alignment and aptitude, or the attacker's description number); a host records through these two calls.
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>The PC's kill statistics and injuries, or null for a session without a hero.</summary>
    public SessionKillLog? QueryHeroKillLog()
    {
        ThrowIfDisposed();
        if (_hero is null) return null;
        var records = _story.KillRecords;
        return new(_story.KillTotal, records[0], records[1], records[2], records[3], records[4], records[5], Array.AsReadOnly(_story.Injuries.ToArray()));
    }

    /// <summary>
    /// A kill credited to the PC (0x4F5330): the total grows by one and each of the six slots is replaced as the original does: the highest and the
    /// lowest level (stat 0x11) by the victim's <paramref name="level"/>, the most positive and the most negative alignment by a positive or a
    /// negative <paramref name="alignment"/> (zero counts for neither), the highest and the most negative magick/tech aptitude likewise. A slot is
    /// replaced when it holds no victim yet (description number 0) or the new value is at least (for the lowest ones at most) the held one, so a tie
    /// goes to the newest kill. <paramref name="nameId"/> is the victim's description number as the PC knows it (0 shows as no name). One revision.
    /// <see cref="GameSessionFailure.KillLogUnavailable"/> without a hero, <see cref="GameSessionFailure.InvalidKillFacts"/> for a negative number.
    /// </summary>
    [OriginalFunction(0x4F5330, OriginalFunctionScope.Partial)]
    public GameSessionMutationResult RecordHeroKill(GameSessionVersion expected, int nameId, int level, int alignment, int aptitude)
    {
        ThrowIfDisposed();
        if (_hero is null) return Reject(GameSessionFailure.KillLogUnavailable);
        if (nameId < 0) return Reject(GameSessionFailure.InvalidKillFacts);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        var records = _story.KillRecords;
        if (_story.KillTotal < int.MaxValue) _story.KillTotal++;
        // Slots: 0 highest level, 1 lowest level, 2 most positive alignment, 3 most negative alignment, 4 highest aptitude, 5 most negative aptitude.
        if (records[0].NameId == 0 || level >= records[0].Value) records[0] = new(nameId, level);
        if (records[1].NameId == 0 || level <= records[1].Value) records[1] = new(nameId, level);
        if (alignment > 0 && (records[2].NameId == 0 || alignment >= records[2].Value)) records[2] = new(nameId, alignment);
        if (alignment < 0 && (records[3].NameId == 0 || alignment <= records[3].Value)) records[3] = new(nameId, alignment);
        if (aptitude > 0 && (records[4].NameId == 0 || aptitude >= records[4].Value)) records[4] = new(nameId, aptitude);
        if (aptitude < 0 && (records[5].NameId == 0 || aptitude <= records[5].Value)) records[5] = new(nameId, aptitude);
        return Changed();
    }

    /// <summary>
    /// An injury of the PC (0x4F5690): the description number of whoever caused it (<paramref name="nameId"/>, the original's 30286 for a self-inflicted
    /// one and 30287 for another PC, else the attacker's description as the PC knows it) and the kind 0 blinded, 1 crippled arm, 2 crippled legs,
    /// 3 scar, appended at the end. One revision. <see cref="GameSessionFailure.KillLogUnavailable"/> without a hero,
    /// <see cref="GameSessionFailure.InvalidKillFacts"/> for a number below 1 (the original's list ends at the first zero number: a zero would cut it) or a
    /// kind outside 0..3, <see cref="GameSessionFailure.InjuryLogFull"/> at <see cref="SessionStoryLimits.MaxInjuries"/> records (project policy).
    /// </summary>
    [OriginalFunction(0x4F5690, OriginalFunctionScope.Partial)]
    public GameSessionMutationResult RecordHeroInjury(GameSessionVersion expected, int nameId, int kind)
    {
        ThrowIfDisposed();
        if (_hero is null) return Reject(GameSessionFailure.KillLogUnavailable);
        if (nameId < 1 || kind is < 0 or >= SessionStoryLimits.InjuryKinds) return Reject(GameSessionFailure.InvalidKillFacts);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_story.Injuries.Count >= SessionStoryLimits.MaxInjuries) return Reject(GameSessionFailure.InjuryLogFull);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _story.Injuries.Add(new(nameId, kind));
        return Changed();
    }
}

using Arcanum.Simulation;
using Arcanum.Simulation.Combat;

namespace Arcanum.Session;

/// <summary>
/// ARC-UI-097 (ledger LB21): what the PC's kill and injury log reads of an NPC: its description number (<c>OBJ_F_DESCRIPTION</c>, the name the PC
/// knows once the NPC has met it), its not-yet-met description number (<c>OBJ_F_CRITTER_DESCRIPTION_UNKNOWN</c>), its alignment (stat 0x13, the
/// stored one clamped to the stat's range; its effects are not read) and its magick/tech aptitude (stat 0x10, derived from its stored points).
/// </summary>
public readonly record struct SessionKillFacts(int Description, int UnknownDescription, int Alignment, int Aptitude);

/// <summary>
/// ARC-UI-082 (ledger LB21): the PC's kill statistics and injuries, part of the story domain. The original keeps them in the PC's logbook field 0x10C
/// (the kill writer 0x4F5330, called when a PC or a PC's follower kills an NPC; the injury writer 0x4F5690, called by the damage function when a hit
/// blinds, cripples or scars a PC). The session keeps the same numbers. Since ARC-UI-097 the hero's and an NPC's attack steps feed them (the kill
/// when an NPC the hero or a critter led by the hero kills dies, the injury when a hit blinds, cripples or scars the hero); NOT READY: the kills
/// and injuries of spell damage (its damage step is not wired to the log), burning, traps and script damage (no attacker), a follower's own death
/// by a critical self-hit, and the dying script's veto of a death (script point 12, not run).
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

    /// <summary>The injury cause the original writes for a self-inflicted injury (0x4F5690) and for one dealt by another PC.</summary>
    private const int SelfInflictedCause = 30286, OtherPcCause = 30287;

    /// <summary>
    /// ARC-UI-097: the kill record of an NPC that died to the hero or to a critter led by the hero (the death function 0x45DA20 calls 0x4F5330 after
    /// the experience award). The victim's name is the description number the PC knows: the object's own once the NPC remembers the hero (the
    /// recognition test 0x45F970, "met before" 0x4C0C40), else its not-yet-met one; a number below 0 is stored as 0 (shown as no name). The
    /// level is the victim's stat 0x11 as the attack sees it (a levelled follower's sheet), the alignment and the aptitude its stats 0x13 and 0x10.
    /// Returns the failure of a refused record (none for a recorded one).
    /// </summary>
    private GameSessionFailure RecordKillLog(SessionCritterCombat victim)
    {
        var facts = victim.KillFacts;
        var name = _reactions.Get(victim.Entity).MetBefore ? facts.Description : facts.UnknownDescription;
        var recorded = RecordHeroKill(Version, Math.Max(0, name), victim.Level, facts.Alignment, facts.Aptitude);
        return recorded.Disposition == GameSessionDisposition.Rejected ? recorded.Failure : GameSessionFailure.None;
    }

    /// <summary>
    /// ARC-UI-097: the injury records a hit left on the hero (the damage function 0x4B4390 calls 0x4F5690 for each, in this order): a scar every
    /// time the hit scars (the session's own scar limit does not stop the record), a blinding, a crippled arm (the first, or the second one on the
    /// roll that crippled both), crippled legs. <paramref name="cause"/> is the original's causer: 30286 for a self-inflicted hit, else the
    /// attacker's description number as the hero knows it (<see cref="AttackerCause"/>). A cause below 1 records nothing (the original would write
    /// a zero number, which cuts its own list: not reproduced, see <see cref="RecordHeroInjury"/>); a full log drops the record (project policy).
    /// </summary>
    internal GameSessionFailure RecordHeroInjuries(LifeEvents events, int cause)
    {
        if (cause < 1) return GameSessionFailure.None;
        foreach (var (flags, kind) in new[] { (LifeEvents.Scarred | LifeEvents.ScarLimitReached, 3), (LifeEvents.Blinded, 0),
                     (LifeEvents.CrippledArm | LifeEvents.CrippledBothArms, 1), (LifeEvents.CrippledLegs, 2) })
        {
            if ((events & flags) == 0) continue;
            var recorded = RecordHeroInjury(Version, cause, kind);
            if (recorded.Disposition == GameSessionDisposition.Rejected && recorded.Failure != GameSessionFailure.InjuryLogFull) return recorded.Failure;
        }
        return GameSessionFailure.None;
    }

    /// <summary>ARC-UI-097: the cause of an injury an NPC dealt the hero: the NPC's description number as the hero knows it (0x45F970: its own
    /// once it remembers the hero, else its not-yet-met one).</summary>
    private int AttackerCause(SessionCritterCombat attacker) =>
        _reactions.Get(attacker.Entity).MetBefore ? attacker.KillFacts.Description : attacker.KillFacts.UnknownDescription;
}

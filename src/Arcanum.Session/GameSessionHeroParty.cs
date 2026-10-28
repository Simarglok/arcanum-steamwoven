using Arcanum.Simulation.Character;
using Arcanum.Simulation.Party;

namespace Arcanum.Session;

/// <summary>
/// The session hero as the PC side of the party rules (ARC-WORLD-067): its Persuasion training, its effective charisma (the stat
/// with its effects), its level and alignment, and the max followers they give (ledger AP4: charisma / 4, +1 from expert Persuasion,
/// then the hero's effects on that stat, clamped to 1..7). One value the follow check, the orders' follow check, the loyalty step,
/// the wait timer, its expiry and the leave guard all read while the session has a hero.
/// </summary>
public sealed record SessionPartySide(PersuasionTraining Persuasion, int Charisma, int Level, int Alignment, int MaxFollowers);

public sealed partial class GameSessionOwner
{
    /// <summary>The hero as the PC side of the party rules, or null without a hero (the caller's facts then stand in).</summary>
    public SessionPartySide? QueryHeroPartySide()
    {
        ThrowIfDisposed();
        return HeroPartySide();
    }

    private SessionPartySide? HeroPartySide()
    {
        if (_hero is not { } hero) return null;
        var persuasion = HeroPersuasion();
        var snapshot = hero.Stats.Query(new(0)).Snapshot!;
        var charisma = snapshot.GetEffective(CharacterStat.Charisma);
        var unclamped = charisma / 4 + (persuasion >= PersuasionTraining.Expert ? 1 : 0);
        var maxFollowers = Math.Clamp(CharacterStatRules.ApplyModifiers(CharacterStat.MaxFollowers, unclamped, snapshot.AppliedEffects),
            FollowConsentRules.MinMaxFollowers, FollowConsentRules.MaxMaxFollowers);
        return new(persuasion, charisma, hero.State.Level, hero.State.Alignment, maxFollowers);
    }

    /// <summary>
    /// A dialog's or script's alignment change of the PC when the session has a hero (ARC-WORLD-067): the hero's stored alignment
    /// becomes <paramref name="next"/> clamped to the stat's −1000..1000 (the stat setter), and the followers in the array are armed
    /// as <see cref="RecordPcAlignmentChange"/> arms them (OL17), in one revision. Refused, in order: no hero
    /// (<see cref="GameSessionFailure.InvalidHero"/>); the version; the revision. The same alignment is NoChange.
    /// </summary>
    public GameSessionMutationResult ChangeHeroAlignment(GameSessionVersion expected, long next)
    {
        ThrowIfDisposed();
        if (_hero is not { } hero) return Reject(GameSessionFailure.InvalidHero);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var previous = hero.State.Alignment;
        var alignment = (int)Math.Clamp(next, SessionHeroLimits.MinAlignment, SessionHeroLimits.MaxAlignment);
        if (alignment == previous) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        foreach (var entity in _party.Order.ToArray())
        {
            var record = _party.Records[entity];
            var armed = record with { Loyalty = CompanionLoyaltyRules.ArmAlignment(record.Loyalty, previous, alignment) };
            if (armed != record) _party.Put(armed, false);
        }
        hero.Apply(hero.State.WithAlignment(alignment));
        return Changed();
    }
}

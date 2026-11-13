using System.Collections.Immutable;
using Arcanum.Data.Characters;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Skills;

namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-1658 (the TraitIdx component's stat writes the hero's state held no field for): the hero's stored resistance array (field 31) and age (stat 25), and the race and gender cases of the stat setter
/// (ledger RM3b) for the hero. The bounds and the order of the setter are the caller's (<c>SessionSpellCaster.TraitIndexHero</c>).
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>The hero's stored resistance <paramref name="index"/> (field 31: 0 normal, 1 fire, 2 electrical, 3 poison, 4 magic) as the indexed read gives it, or null without a hero or for another index. Effects and worn armour are not read.</summary>
    public int? QueryHeroStoredResistance(int index)
    {
        ThrowIfDisposed();
        return _hero is { } hero && index is >= 0 and < SessionHeroLimits.ResistanceCount ? hero.State.Resistances[index] : null;
    }

    /// <summary>The hero's stored age (base stat 25), or null without a hero or when the session has not read it (a save of schema 98 or before).</summary>
    public int? QueryHeroStoredAge()
    {
        ThrowIfDisposed();
        return _hero?.State.Age;
    }

    /// <summary>Stores resistance <paramref name="index"/> of the hero's array as the indexed write does: the value is kept as it is, unclamped.</summary>
    /// <remarks>
    /// ARC-MAGIC-1658: the stored array is what the hero's resistance readers lay the worn armour and the effects over (<c>HeroStoredResistances</c>). Refused, in order: no hero (<see cref="GameSessionFailure.InvalidHero"/>), an index
    /// outside 0..4 (<see cref="GameSessionFailure.InvalidChangeKey"/>), the version, the revision. The same value is <see cref="GameSessionDisposition.NoChange"/>.
    /// </remarks>
    public GameSessionMutationResult WriteHeroResistance(GameSessionVersion expected, int index, int value)
    {
        ThrowIfDisposed();
        if (_hero is not { } hero) return Reject(GameSessionFailure.InvalidHero);
        if (index is < 0 or >= SessionHeroLimits.ResistanceCount) return Reject(GameSessionFailure.InvalidChangeKey);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (hero.State.Resistances[index] == value) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        hero.Apply(hero.State.WithResistances(hero.State.Resistances.SetItem(index, value)));
        return Changed();
    }

    /// <summary>Stores the hero's age (base stat 25, after the setter's clamp, which has no case for it).</summary>
    /// <remarks>
    /// ARC-MAGIC-1658: refused, in order: no hero (<see cref="GameSessionFailure.InvalidHero"/>), a value outside the stat's range (<see cref="GameSessionFailure.InvalidChangeKey"/>), the version, the revision.
    /// The same value is <see cref="GameSessionDisposition.NoChange"/>.
    /// </remarks>
    public GameSessionMutationResult WriteHeroAge(GameSessionVersion expected, int value)
    {
        ThrowIfDisposed();
        if (_hero is not { } hero) return Reject(GameSessionFailure.InvalidHero);
        if (CharacterStatRules.StoredRange(hero.State.Race, 25) is not { } range || !range.Contains(value)) return Reject(GameSessionFailure.InvalidChangeKey);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (hero.State.Age == value) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        hero.Apply(hero.State.WithAge(value));
        return Changed();
    }

    /// <summary>The hero's gender as the stat setter stores it (0 female, 1 male), or null without a hero.</summary>
    public int? QueryHeroStoredGender()
    {
        ThrowIfDisposed();
        return _hero is { } hero ? hero.State.Gender == CharacterGender.Female ? 0 : 1 : null;
    }

    /// <summary>The hero's race as the stat setter stores it (the source's index 0..10), or null without a hero.</summary>
    public int? QueryHeroStoredRace()
    {
        ThrowIfDisposed();
        return _hero is { } hero ? HeroDomain.SourceRace(hero.State.Race) : null;
    }

    /// <summary>Whether <see cref="WriteHeroInnate"/> would write the hero's gender or race <paramref name="value"/>: true when every fact it needs is there and admitted (see the method), without writing anything.</summary>
    public bool HeroInnateReady(bool gender, int value)
    {
        ThrowIfDisposed();
        return PrepareHeroInnate(gender, value, out _) == GameSessionFailure.None;
    }

    /// <summary>
    /// The gender (<paramref name="gender"/>: 0 female, 1 male) or race (the source's index 0..10) cases of the stat setter for the hero: the old innate effect of the cause leaves the hero's list, the value is stored, the new one
    /// is added.
    /// </summary>
    /// <remarks>
    /// ARC-MAGIC-1658 (cases 2 and 3 of the setter's table, 0x4B0E74): the removal of the first entry of the gender cause (9) or race cause (0) (0x4EA2E0), then the store, then the add of the race's effect <c>0x40 + race</c> (race cause) or, for
    /// gender 0, effect 0x14A (gender cause), through 0x4E9F70; each moves the hero's damage by the change of its maximums as every effect step does (<c>PrepareEffects</c>, current hit points and fatigue stay, never below no damage). The hero's
    /// race, gender, innate stat effects and innate skill changes are the state's, and its character owner is built from them, so the owner is built again from the new state and the durable entries are applied to it again, and the
    /// effects of the old race leave with it. The rules of the effects are the ones attached with <see cref="AttachInnateEffects"/> (their stat changes and, as at the hero's creation, their basic and tech skill changes; ARC-MAGIC-1700: their resistance changes are read from the rules at each question, <c>HeroEffectResistances</c>, so
    /// a swap moves the hero's resistances with the entry; ARC-MAGIC-1723: their special changes (maximum hit points and fatigue, reaction, good and bad reaction, experience gain) are read over the innate entries too, and each step of the swap moves the damage by them; the age change of an innate effect is not modelled for the hero). Refused, in order: no hero (<see cref="GameSessionFailure.InvalidHero"/>), a gender outside 0..1 or a race outside the character rules' ten (the table's 11 has none:
    /// <see cref="GameSessionFailure.InvalidChangeKey"/>), the rule of the effect to add not attached (<see cref="GameSessionFailure.EffectsUnavailable"/>), a stored stat the new race's ranges do not admit or more skill changes than a
    /// cause holds (<see cref="GameSessionFailure.InvalidHero"/>), a durable effect, blessing or curse whose number is the new innate effect's, the life rules or the event budget (<see cref="GameSessionFailure.InvalidEffect"/>,
    /// <see cref="GameSessionFailure.EventBudgetExceeded"/>), the version, the revision. Nothing is written for a refusal.
    /// </remarks>
    public GameSessionMutationResult WriteHeroInnate(GameSessionVersion expected, bool gender, int value)
    {
        ThrowIfDisposed();
        var failure = PrepareHeroInnate(gender, value, out var prepared);
        if (failure != GameSessionFailure.None) return Reject(failure);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        var hero = _hero!;
        var after = prepared!.Staged!.State;
        var before = hero.State;
        var same = after.Race == before.Race && after.Gender == before.Gender && SameEffect(after.RaceEffect, before.RaceEffect) && SameEffect(after.GenderEffect, before.GenderEffect) &&
            after.InnateSkills.SequenceEqual(before.InnateSkills) && !prepared.LifeChanged;
        ApplyEffects(prepared);
        return same ? NoChange() : Changed();
    }

    /// <summary>
    /// ARC-MAGIC-1700: the resistance changes the hero's resistance readers lay over the stored array, the worn armour and the poison bonus (the original's combat resistance getter reads the whole effect list, the innate entries first): the changes of the innate
    /// effects of the hero's race and gender (see <see cref="HeroInnateRules"/>) and then the durable entries' in list order.
    /// </summary>
    private EffectResistances HeroEffectResistances(IEnumerable<SessionEffect>? entries = null, IReadOnlyDictionary<int, SessionEffectDefinition>? definitions = null, IEnumerable<SessionEffectDefinition>? innate = null) =>
        EffectDomain.Resistances(entries ?? _effects.Entries, definitions ?? _effects.Definitions, innate ?? HeroInnateRules());

    /// <summary>
    /// ARC-MAGIC-1700, ARC-MAGIC-1723: the rules of the hero's innate effects in list order (the race effect <c>64 + race</c>, then 330 for a woman; the rules are the ones attached with <see cref="AttachInnateEffects"/>, a number without an attached
    /// rule has none). <paramref name="state"/> is the hero state the entries are taken from (the live one by default; a trial of a race or gender swap passes the state it ends with), <paramref name="without"/> a cause whose entry has left (the removal
    /// that opens the setter's case, before the add).
    /// </summary>
    internal ImmutableArray<SessionEffectDefinition> HeroInnateRules(SessionHeroState? state = null, EffectCause? without = null) =>
        (state ?? _hero?.State) is { } held
            ? [.. EffectDomain.Innate(held).Where(entry => entry.Cause != without).Select(entry => InnateRule(entry.Number)).OfType<SessionEffectDefinition>()]
            : [];

    private static bool SameEffect(StatEffectDefinition? a, StatEffectDefinition? b) =>
        a is null ? b is null : b is not null && a.EffectId == b.EffectId && a.Modifiers.SequenceEqual(b.Modifiers);

    // The trial of the setter's two steps for the hero: the removal state, then the final state; nothing is written.
    private GameSessionFailure PrepareHeroInnate(bool gender, int value, out PreparedEffects? prepared)
    {
        prepared = null;
        if (_hero is not { } hero) return GameSessionFailure.InvalidHero;
        if (gender ? value is not (0 or 1) : value is < 0 or > (int)CharacterRace.Orc || !Enum.IsDefined((CharacterRace)value)) return GameSessionFailure.InvalidChangeKey;
        var state = hero.State;
        var cause = gender ? EffectCause.Gender : EffectCause.Race;
        // The effect the add puts in: the race's, a woman's gender's; a man's gender adds none.
        SessionEffectDefinition? rule = null;
        if (!gender || value == 0)
        {
            rule = InnateRule(gender ? FemaleEffectNumber : RaceEffectBase + value);
            if (rule is null) return GameSessionFailure.EffectsUnavailable;
        }
        // The removal: the innate stat effect and skill changes of the cause leave (a gender or race the state holds no effect for loses none).
        var stages = new List<HeroStage>();
        var kept = state.InnateSkills.Where(change => change.Cause != cause).ToImmutableArray();
        var removed = state.WithInnate(state.Race, state.Gender, gender ? state.RaceEffect : null, gender ? null : state.GenderEffect, kept);
        var heldEffect = gender ? state.GenderEffect is not null : state.RaceEffect is not null;
        // ARC-MAGIC-1723: an entry that carries only resistance or special changes leaves too (it moves the maxima).
        var left = HeroInnateRules(state, cause);
        if (heldEffect || kept.Length != state.InnateSkills.Length || left.Length != HeroInnateRules(state).Length) stages.Add(new(removed, left));
        // The store and the add.
        var race = gender ? state.Race : (CharacterRace)value;
        var sex = gender ? value == 0 ? CharacterGender.Female : CharacterGender.Male : state.Gender;
        var added = rule is null ? null : InnateEffect(rule, cause, out var skills);
        var raceEffect = gender ? state.RaceEffect : added;
        var genderEffect = gender ? added : state.GenderEffect;
        ImmutableArray<SessionInnateSkillChange> innateSkills = [.. gender
            ? state.InnateSkills.Where(change => change.Cause == EffectCause.Race).Concat(rule is null ? [] : Skills(rule, cause))
            : (rule is null ? [] : (IEnumerable<SessionInnateSkillChange>)Skills(rule, cause)).Concat(state.InnateSkills.Where(change => change.Cause == EffectCause.Gender))];
        var final = state.WithInnate(race, sex, raceEffect, genderEffect, innateSkills);
        var innate = HeroInnateRules(final);
        stages.Add(new(final, innate));
        // A durable effect, blessing or curse with the number of the innate effect now in the list would have been refused at its add (the list holds the innate entries first).
        if (_effects.Entries.Any(entry => EffectDomain.IsInnate(final, entry.Number)) || _effects.Blessings.Concat(_effects.Curses).Any(entry => EffectDomain.IsInnate(final, entry.Effect)))
            return GameSessionFailure.InvalidEffect;
        // ARC-MAGIC-1723: the durable entries with the new innate ones stay within the bounds of the reaction and maximum changes (the innate entries cannot be removed, so no later removal is refused for them).
        if (!EffectDomain.Bounded(_effects.Entries, _effects.Definitions, innate: innate)) return GameSessionFailure.InvalidEffect;
        return PrepareEffects([], new(_effects.Definitions), 0, stages, out prepared);
    }

    // The stat changes of an innate rule as the character owner models them.
    private static StatEffectDefinition? InnateEffect(SessionEffectDefinition rule, EffectCause cause, out ImmutableArray<SessionInnateSkillChange> skills)
    {
        skills = Skills(rule, cause);
        return EffectDomain.Stats(rule);
    }

    // The basic and tech skill changes of an innate rule, in the rule's order (the hero's creation keeps them the same way: AreaStartingHero.Definition).
    private static ImmutableArray<SessionInnateSkillChange> Skills(SessionEffectDefinition rule, EffectCause cause)
    {
        var skills = ImmutableArray.CreateBuilder<SessionInnateSkillChange>();
        foreach (var change in rule.Changes)
        {
            if (change.Kind is not (EffectAttributeKind.BasicSkill or EffectAttributeKind.TechSkill)) continue;
            var tech = change.Kind == EffectAttributeKind.TechSkill;
            if (change.Attribute >= 0 && change.Attribute < (tech ? SkillRules.TechCount : SkillRules.BasicCount))
                skills.Add(new(cause, tech ? SkillRules.Tech(change.Attribute) : SkillRules.Basic(change.Attribute), EffectDomain.Operator(change.Operator), change.Value));
        }
        return skills.ToImmutable();
    }
}

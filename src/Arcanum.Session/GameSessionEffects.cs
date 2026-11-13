using System.Collections.Immutable;
using Arcanum.Data.Characters;
using Arcanum.Simulation;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Reaction;

namespace Arcanum.Session;

/// <summary>Bounds of the hero's durable effects (ARC-WORLD-029; project policy, the source has none).</summary>
public static class SessionEffectLimits
{
    /// <summary>The source's effect tables hold numbers 0..399 (0..49 the module's <c>gameeffect.mes</c>, 50..399
    /// <c>effect.mes</c>); a larger number would read past them, so the session refuses it.</summary>
    public const int FirstNumber = 0, LastNumber = 399;
    /// <summary>
    /// Durable entries the hero holds after its innate ones (project policy: the original's list has no bound, ledger EF1). ARC-DEV-366: a
    /// tech book's insert script adds one entry per point of the holder's intelligence (ledger XA9), so eight books at the stat ceiling
    /// (<see cref="Arcanum.Simulation.Combat.LifeLimits.MaxStat"/>, 100) need 800 entries and the rest of the list keeps 224 more; the
    /// hero's character owner holds as many applied (stat-changing) effects as this list admits entries.
    /// </summary>
    public const int MaxHeroEffects = 1024;
    /// <summary>Pending timed effects (their END events) a session holds: 64, though an END can outlive the entries it would
    /// remove (the bound the durable list had before ARC-DEV-366, which kept the two equal).</summary>
    public const int MaxTimedEffects = 64;
    /// <summary>Entries of the hero's blessing list, and of its curse list (ARC-WORLD-035): each adds one effect entry; 64 as before
    /// ARC-DEV-366, which raised the durable list's bound alone.</summary>
    public const int MaxBlessings = 64;
    /// <summary>The longest duration: int.MaxValue real seconds at the game rate (8 game ms per real ms).</summary>
    public const long MaxDurationMs = int.MaxValue * 8000L;
    /// <summary>The largest magnitude of a change the hero's character owner, reaction and life rules admit.</summary>
    public const int MaxModelledMagnitude = 1000;
    /// <summary>The largest magnitude of any change: the nine digits the effect rules read.</summary>
    public const int MaxMagnitude = 999_999_999;
}

/// <summary>
/// One effect number and its changes, as the effect tables hold it (ARC-WORLD-029): every change of the rule, in its order,
/// also those the session does not model yet (skills, tech skills, resistances, tech disciplines and most special values),
/// so a save keeps the whole rule. Build it from the data with <see cref="FromRule"/>; a number with no table entry has no
/// changes, as in the source.
/// </summary>
public sealed record SessionEffectDefinition(int Number, ImmutableArray<EffectChange> Changes)
{
    public static SessionEffectDefinition FromRule(EffectRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return new(rule.Number, [.. rule.Changes]);
    }

    /// <summary>Same number and the same changes in the same order.</summary>
    public bool SameAs(SessionEffectDefinition? other) =>
        other is not null && other.Number == Number && !Changes.IsDefault && !other.Changes.IsDefault && Changes.SequenceEqual(other.Changes);
}

/// <summary>One entry of the hero's effect list: its number and cause (the source's two parallel critter arrays).</summary>
public readonly record struct SessionEffect(int Number, EffectCause Cause);

/// <summary>A pending timed effect: the END its event runs (remove <see cref="Count"/> entries of <see cref="Number"/>),
/// when it was set and when it is due.</summary>
public sealed record SessionTimedEffect(long Sequence, long ScheduledAtMs, long DeadlineMs, int Number, int Count);

/// <summary>
/// The hero's effect list (ARC-WORLD-029): <see cref="Innate"/> are the race and female effects the list starts with (their
/// stat changes are the hero's <see cref="SessionHeroState.RaceEffect"/> and <see cref="SessionHeroState.GenderEffect"/>),
/// <see cref="Effects"/> the durable entries after them in list order, <see cref="Definitions"/> the rule of each number
/// they use, and <see cref="Timed"/> the pending ENDs in queue order.
/// </summary>
public sealed record SessionHeroEffectsView(IReadOnlyList<SessionEffect> Innate, IReadOnlyList<SessionEffect> Effects,
    IReadOnlyList<SessionEffectDefinition> Definitions, IReadOnlyList<SessionTimedEffect> Timed)
{
    /// <summary>Entries of <paramref name="number"/> in the whole list, innate ones included (0x4EA4A0).</summary>
    [OriginalFunction(0x4EA4A0, OriginalFunctionScope.Full)]
    public int Count(int number) => Innate.Count(e => e.Number == number) + Effects.Count(e => e.Number == number);
}

/// <summary>Saved durable effects of the hero (snapshot schema 13; blessings and curses since schema 17): the entries in list
/// order, the rule of each number they use by ascending number, and the blessing and curse lists. All are empty for a session
/// without a hero.</summary>
public sealed class SessionEffectState
{
    internal SessionEffectState(SessionEffect[] hero, SessionEffectDefinition[] definitions, SessionBlessing[] blessings,
        SessionBlessing[] curses, SessionCritterEffectRecord[] critters)
    {
        Hero = Array.AsReadOnly(hero);
        Definitions = Array.AsReadOnly(definitions);
        Blessings = Array.AsReadOnly(blessings);
        Curses = Array.AsReadOnly(curses);
        Critters = Array.AsReadOnly(critters);
    }

    public static SessionEffectState Empty { get; } = new([], [], [], [], []);

    public IReadOnlyList<SessionEffect> Hero { get; }
    public IReadOnlyList<SessionEffectDefinition> Definitions { get; }
    /// <summary>The hero's blessings in list order (snapshot schema 17, ARC-WORLD-035).</summary>
    public IReadOnlyList<SessionBlessing> Blessings { get; }
    /// <summary>The hero's curses in list order (snapshot schema 17, ARC-WORLD-035).</summary>
    public IReadOnlyList<SessionBlessing> Curses { get; }
    /// <summary>The effect lists of the NPCs the session changed, ascending by entity (snapshot schema 87, ARC-DEV-348); an NPC without a record keeps the list its map data stores.</summary>
    public IReadOnlyList<SessionCritterEffectRecord> Critters { get; }
}

/// <summary>A blessing or a curse (ARC-WORLD-035): the source's two PC lists, each with its own effect cause.</summary>
public enum SessionBlessingKind { Blessing, Curse }

/// <summary>
/// One entry of the hero's blessing or curse list (ARC-WORLD-035): its number, the game time it was received at (the
/// logbook's date) and the effect number it added, which its removal removes again (the source reads the same table entry
/// both times, so the session keeps the number it was given).
/// </summary>
public readonly record struct SessionBlessing(int Number, int Effect, long TimeMs);

/// <summary>
/// An effect change: how many entries it added or removed, the hero's life record when the change of a maximum moved its
/// damage (null when the life record did not change), and the queue sequence of a timed effect's END (null otherwise).
/// </summary>
public sealed record SessionEffectResult(GameSessionDisposition Disposition, GameSessionFailure Failure, int Entries,
    SessionLifeRecord? Life, long? TimedEffect, GameSessionVersion Version);

/// <summary>A timed effect's END that ran in an event pass, and what it did.</summary>
public sealed record SessionEffectFiring(GameEvent Event, SessionEffectResult Result);

// The special changes the session models (indices of the effect rules' special collection).
internal enum EffectSpecial { MaxHitPoints = 0, MaxFatigue = 1, Reaction = 2, BadReaction = 7, GoodReaction = 8, XpGain = 9 }

/// <summary>The durable effects of one owner: the hero's entries after its innate ones, the character owner's id of each
/// entry with modelled stat changes, and the rule of each number in use.</summary>
internal sealed class EffectDomain
{
    internal List<SessionEffect> Entries { get; } = [];
    internal List<AppliedEffectId?> Applied { get; } = [];
    internal Dictionary<int, SessionEffectDefinition> Definitions { get; } = [];

    internal List<SessionBlessing> Blessings { get; } = [];
    internal List<SessionBlessing> Curses { get; } = [];

    /// <summary>ARC-DEV-348: the effect list of each NPC the session changed (the whole list, the loaded entries included), by entity.</summary>
    internal SortedDictionary<string, CritterEffectList> Critters { get; } = new(StringComparer.Ordinal);

    internal List<SessionBlessing> List(SessionBlessingKind kind) => kind == SessionBlessingKind.Blessing ? Blessings : Curses;

    internal SessionEffectState Capture() =>
        new([.. Entries], [.. Definitions.OrderBy(pair => pair.Key).Select(pair => pair.Value)], [.. Blessings], [.. Curses],
            [.. Critters.Select(pair => new SessionCritterEffectRecord(pair.Key, [.. pair.Value.Entries], [.. pair.Value.Definitions.OrderBy(d => d.Key).Select(d => d.Value)]))]);

    internal ImmutableArray<EffectModifier> Maximum(EffectSpecial special, IEnumerable<SessionEffectDefinition>? innate = null) => Modifiers(Entries, Definitions, special, innate);

    internal IReadOnlyList<ReactionModifier> Special(EffectSpecial special, IEnumerable<SessionEffectDefinition>? innate = null) =>
        Array.AsReadOnly(Modifiers(Entries, Definitions, special, innate).Select(m => new ReactionModifier(m.Operator, m.Value)).ToArray());

    /// <summary>The changes of one special value over the entries in list order; for the hero <paramref name="innate"/> are the rules of its innate entries, which stand first in its list.</summary>
    /// <remarks>ARC-MAGIC-1723: the original's special getters (0x4EAA20, 0x4EAA50, 0x4EAA80, 0x4EAAB0, 0x4EABC0) read the whole list: only the stat getter has an "ignore innate" form.</remarks>
    internal static ImmutableArray<EffectModifier> Modifiers(IEnumerable<SessionEffect> entries,
        IReadOnlyDictionary<int, SessionEffectDefinition> definitions, EffectSpecial special, IEnumerable<SessionEffectDefinition>? innate = null) =>
        [.. (innate ?? []).SelectMany(rule => rule.Changes).Concat(entries.SelectMany(entry => definitions[entry.Number].Changes))
            .Where(c => c.Kind == EffectAttributeKind.Special && c.Attribute == (int)special)
            .Select(c => new EffectModifier(Operator(c.Operator), c.Value))];

    /// <summary>
    /// ARC-MAGIC-027 (MA-2): the resistance changes of the entries (the five resistances in the source's order, list order within each), for the effective resistance function
    /// (<see cref="CombatantRules.Resistances(DamageResistances, Arcanum.Simulation.Equipment.EquipmentResistances, int, bool, EffectResistances)"/>). A number without a rule has no changes.
    /// </summary>
    internal static EffectResistances Resistances(IEnumerable<SessionEffect> entries, IReadOnlyDictionary<int, SessionEffectDefinition> definitions,
        IEnumerable<SessionEffectDefinition>? innate = null)
    {
        // ARC-MAGIC-1700: the hero's innate entries stand first in its list, so their changes come first within each resistance (the rules are the host's, <see cref="GameSessionOwner.AttachInnateEffects"/>).
        var changes = (innate ?? []).SelectMany(rule => rule.Changes).Concat(entries.SelectMany(entry => definitions.TryGetValue(entry.Number, out var rule) ? rule.Changes : []))
            .Where(c => c.Kind == EffectAttributeKind.Resistance).ToArray();
        if (changes.Length == 0) return EffectResistances.None;
        IReadOnlyList<EffectModifier> Of(int attribute) =>
            Array.AsReadOnly(changes.Where(c => c.Attribute == attribute).Select(c => new EffectModifier(Operator(c.Operator), c.Value)).ToArray());
        return new(Of(0), Of(1), Of(2), Of(3), Of(4));
    }

    /// <summary>
    /// Whether the entries keep the hero within the bounds of its consumers whatever subset of them stays, so no later removal
    /// can be refused: at most <see cref="ReactionLimits.MaxModifiers"/> changes of each reaction value, and maxima below the
    /// pinned death damage in either direction even when the bounds that hold them and the changes that pull the other way
    /// are removed (a hero's maximum before effects is between 0 and <see cref="HeroMaximumCeiling"/>).
    /// </summary>
    internal static bool Bounded(IReadOnlyList<SessionEffect> entries, IReadOnlyDictionary<int, SessionEffectDefinition> definitions, long ceiling = HeroMaximumCeiling,
        IEnumerable<SessionEffectDefinition>? innate = null)
    {
        foreach (var special in new[] { EffectSpecial.Reaction, EffectSpecial.GoodReaction, EffectSpecial.BadReaction })
            if (Modifiers(entries, definitions, special, innate).Length > ReactionLimits.MaxModifiers) return false;
        foreach (var special in new[] { EffectSpecial.MaxHitPoints, EffectSpecial.MaxFatigue })
        {
            var modifiers = Modifiers(entries, definitions, special, innate);
            long Sum(StatModifierOperator op, bool positive) =>
                modifiers.Where(m => m.Operator == op && m.Value > 0 == positive).Sum(m => (long)m.Value);
            var highest = ceiling * (100 + Sum(StatModifierOperator.Percent, true)) / 100 + Sum(StatModifierOperator.Add, true);
            var fall = 100 + Sum(StatModifierOperator.Percent, false);
            var lowest = (fall < 0 ? ceiling * fall / 100 : 0) + Sum(StatModifierOperator.Add, false);
            foreach (var m in modifiers)
            {
                if (m.Operator == StatModifierOperator.Minimum) highest = Math.Max(highest, m.Value);
                if (m.Operator == StatModifierOperator.Maximum) lowest = Math.Min(lowest, m.Value);
            }
            if (highest >= LifeLimits.DeathHitPointDamage || lowest <= -LifeLimits.DeathHitPointDamage) return false;
        }
        return true;
    }

    /// <summary>The largest maximum hit points or fatigue a hero has before effects: every stat, the level and the bought points
    /// (ARC-WORLD-036) at their limits (<c>4 × points + WP + 2 × (ST + level) + 4</c>, <c>4 × points + 2 × (level + CN) + WP + 4</c>).</summary>
    internal const long HeroMaximumCeiling = 3 * LifeLimits.MaxStat + 2 * LifeLimits.MaxLevel + 4 + 4L * SessionAdvancementLimits.MaxHeroPoolPoints;

    /// <summary>The stat changes of a rule the character owner models, or null when it has none.</summary>
    internal static StatEffectDefinition? Stats(SessionEffectDefinition definition)
    {
        var modifiers = definition.Changes.Where(c => c.Kind == EffectAttributeKind.Stat && Stat(c.Attribute) is not null)
            .Select(c => new StatModifier(Stat(c.Attribute)!.Value, Operator(c.Operator), c.Value)).ToImmutableArray();
        return modifiers.IsEmpty ? null : new(definition.Number, modifiers);
    }

    /// <summary>
    /// A rule the session admits: a number in the tables' range, at most seven changes per collection, each naming an
    /// attribute of its collection with an admitted operator and at most nine digits, and every change the session models
    /// (the stats, the two maxima and the three reaction values) at most <see cref="SessionEffectLimits.MaxModelledMagnitude"/>;
    /// no divisor of 0 and no multiply or divide change on a maximum (ARC-WORLD-035).
    /// </summary>
    internal static bool IsValid(SessionEffectDefinition? definition)
    {
        if (definition is null || definition.Changes.IsDefault ||
            definition.Number is < SessionEffectLimits.FirstNumber or > SessionEffectLimits.LastNumber) return false;
        var counts = new int[Sizes.Length];
        foreach (var change in definition.Changes)
        {
            if ((uint)change.Kind >= (uint)Sizes.Length || change.Attribute < 0 || change.Attribute >= Sizes[(int)change.Kind] ||
                !Enum.IsDefined(change.Operator) || Math.Abs((long)change.Value) > SessionEffectLimits.MaxMagnitude ||
                ++counts[(int)change.Kind] > EffectRules.MaxChangesPerCollection) return false;
            if (Modelled(change) && Math.Abs((long)change.Value) > SessionEffectLimits.MaxModelledMagnitude) return false;
            // A divisor of 0 would fault the original; a factor on a maximum is refused, so the bounds of Bounded hold (no
            // installed rule has one).
            if (change.Operator == EffectChangeOperator.Divide && change.Value == 0) return false;
            if (change.Kind == EffectAttributeKind.Special && change.Attribute is (int)EffectSpecial.MaxHitPoints or (int)EffectSpecial.MaxFatigue &&
                change.Operator is EffectChangeOperator.Multiply or EffectChangeOperator.Divide) return false;
        }
        return true;
    }

    // The attribute counts of the six collections (stats, basic skills, tech skills, resistances, tech disciplines, special).
    private static readonly int[] Sizes = [28, 12, 4, 5, 8, 10];

    private static bool Modelled(EffectChange change) =>
        change.Kind == EffectAttributeKind.Stat ? Stat(change.Attribute) is not null
        : change.Kind == EffectAttributeKind.Resistance ? change.Attribute is >= 0 and <= 4
        : change.Kind == EffectAttributeKind.Special && Enum.IsDefined((EffectSpecial)change.Attribute);

    // The source's stat index of the stats the character owner models: 0..7 primary, 8..16 derived, 22 and 23 the points.
    private static CharacterStat? Stat(int attribute) => attribute switch
    {
        >= 0 and <= 7 => (CharacterStat)attribute,
        8 => CharacterStat.CarryWeight, 9 => CharacterStat.DamageBonus, 10 => CharacterStat.AcAdjustment, 11 => CharacterStat.Speed,
        12 => CharacterStat.HealRate, 13 => CharacterStat.PoisonRecovery, 14 => CharacterStat.ReactionModifier,
        15 => CharacterStat.MaxFollowers, 16 => CharacterStat.MagickTechAptitude,
        22 => CharacterStat.MagickPoints, 23 => CharacterStat.TechPoints,
        _ => null,
    };

    internal static StatModifierOperator Operator(EffectChangeOperator op) => op switch
    {
        EffectChangeOperator.Add => StatModifierOperator.Add,
        EffectChangeOperator.Percent => StatModifierOperator.Percent,
        EffectChangeOperator.Minimum => StatModifierOperator.Minimum,
        EffectChangeOperator.Maximum => StatModifierOperator.Maximum,
        EffectChangeOperator.Multiply => StatModifierOperator.Multiply,
        EffectChangeOperator.Divide => StatModifierOperator.Divide,
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    /// <summary>The innate entries the hero's list starts with: the race effect <c>64 + race</c>, then the female effect 330.</summary>
    internal static SessionEffect[] Innate(SessionHeroState hero) => hero.Gender == CharacterGender.Female
        ? [new(EffectRules.RaceEffectBase + HeroDomain.SourceRace(hero.Race), EffectCause.Race), new(EffectRules.FemaleEffect, EffectCause.Gender)]
        : [new(EffectRules.RaceEffectBase + HeroDomain.SourceRace(hero.Race), EffectCause.Race)];

    internal static bool IsInnate(SessionHeroState hero, int number) => Innate(hero).Any(e => e.Number == number);

    /// <summary>Causes the effect domain owns: not the race and gender setters' (innate), not the life domain's scars (injury).</summary>
    internal static bool OwnCause(EffectCause cause) =>
        Enum.IsDefined(cause) && cause is not (EffectCause.Race or EffectCause.Gender or EffectCause.Injury);
}

/// <summary>ARC-MAGIC-1658, ARC-MAGIC-1723: a hero state a trial passes through before its steps and the rules of the innate entries the hero holds at that state (the removal stage lacks the removed cause's).</summary>
internal readonly record struct HeroStage(SessionHeroState State, ImmutableArray<SessionEffectDefinition> Innate);

/// <summary>One step of an effect change: append an entry, or remove the entry at an index of the durable list.</summary>
internal readonly record struct EffectStep(SessionEffect? Add, int RemoveAt);

public sealed partial class GameSessionOwner
{
    /// <summary>The hero's effect list (ARC-WORLD-029), or null for a session without a hero.</summary>
    public SessionHeroEffectsView? QueryHeroEffects()
    {
        ThrowIfDisposed();
        if (_hero is null) return null;
        return new(Array.AsReadOnly(EffectDomain.Innate(_hero.State)), Array.AsReadOnly(_effects.Entries.ToArray()),
            _effects.Capture().Definitions, TimedEffects());
    }

    /// <summary>
    /// Adds <paramref name="count"/> entries of an effect with <paramref name="cause"/> to the end of the hero's list, one at a
    /// time, as 0x4E9F70 does for each (a script's add-effect action, an Effect component that adds). Each entry that
    /// changes a maximum moves the damage by the same amount, so current hit points and fatigue stay, clamped at no damage.
    /// Refused, in order: no hero (<see cref="GameSessionFailure.EffectsUnavailable"/>); a rule the session does not admit, a cause
    /// of the race and gender setters or of the life domain's scars, or a count below 1 (<see cref="GameSessionFailure.InvalidEffect"/>);
    /// the number of one of the hero's innate effects (<see cref="GameSessionFailure.InnateEffect"/>: its typed removal would reach
    /// the innate entry); a number in use with other changes (<see cref="GameSessionFailure.EffectMismatch"/>); the version; more
    /// than <see cref="SessionEffectLimits.MaxHeroEffects"/> entries (<see cref="GameSessionFailure.EffectCapacityExceeded"/>);
    /// entries that could leave the hero's maxima or reaction effects beyond their consumers' bounds once some are removed
    /// (<see cref="GameSessionFailure.InvalidEffect"/>), so no removal or END is ever refused for them; the event budget and the revision. One revision.
    /// </summary>
    [OriginalFunction(0x4E9F70, OriginalFunctionScope.Partial)]
    public SessionEffectResult AddHeroEffect(GameSessionVersion expected, SessionEffectDefinition definition, EffectCause cause, int count = 1)
    {
        ThrowIfDisposed();
        return AddEffects(expected, definition, cause, count, null);
    }

    /// <summary>
    /// A timed effect: <see cref="AddHeroEffect"/>, and an END after <paramref name="durationMs"/> of game time (1 ms up to
    /// <see cref="SessionEffectLimits.MaxDurationMs"/>; a spell's <c>Duration:</c> seconds × 8000) that removes
    /// <paramref name="count"/> entries of the number, as the END of a timed magick or tech run whose components only add and
    /// remove that effect does. The END removes the first entries with that number, whatever added them, as the source does.
    /// Its sequence is <see cref="SessionEffectResult.TimedEffect"/>. One revision.
    /// </summary>
    public SessionEffectResult AddHeroTimedEffect(GameSessionVersion expected, SessionEffectDefinition definition, EffectCause cause,
        int count, long durationMs)
    {
        ThrowIfDisposed();
        if (durationMs is < 1 or > SessionEffectLimits.MaxDurationMs) return RejectEffect(GameSessionFailure.InvalidEffect);
        return AddEffects(expected, definition, cause, count, durationMs);
    }

    /// <summary>
    /// Removes the first entry of <paramref name="number"/>, <paramref name="count"/> times (0x4EA100: a
    /// script's remove-effect action, an Effect component that removes). Refused with <see cref="GameSessionFailure.InnateEffect"/>
    /// when the first entry would be an innate one (the race and gender setters own them); NoChange when there is none.
    /// </summary>
    [OriginalFunction(0x4EA100, OriginalFunctionScope.Partial)]
    public SessionEffectResult RemoveHeroEffect(GameSessionVersion expected, int number, int count = 1)
    {
        ThrowIfDisposed();
        if (count < 1 || number is < SessionEffectLimits.FirstNumber or > SessionEffectLimits.LastNumber) return RejectEffect(GameSessionFailure.InvalidEffect);
        return RemoveEffects(expected, number, count, null);
    }

    /// <summary>Removes every entry of <paramref name="number"/> (0x4EA200); innate numbers are refused.</summary>
    [OriginalFunction(0x4EA200, OriginalFunctionScope.Partial)]
    public SessionEffectResult RemoveAllHeroEffects(GameSessionVersion expected, int number)
    {
        ThrowIfDisposed();
        if (number is < SessionEffectLimits.FirstNumber or > SessionEffectLimits.LastNumber) return RejectEffect(GameSessionFailure.InvalidEffect);
        return RemoveEffects(expected, number, int.MaxValue, null);
    }

    /// <summary>
    /// Removes the first entry with <paramref name="cause"/>, or every one when <paramref name="all"/>
    /// (0x4EA2E0, 0x4EA3C0). The race, gender and injury causes are refused.
    /// </summary>
    [OriginalFunction(0x4EA2E0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x4EA3C0, OriginalFunctionScope.Partial)]
    public SessionEffectResult RemoveHeroEffectsCausedBy(GameSessionVersion expected, EffectCause cause, bool all = false)
    {
        ThrowIfDisposed();
        if (!EffectDomain.OwnCause(cause)) return RejectEffect(GameSessionFailure.InvalidEffect);
        return RemoveEffects(expected, null, all ? int.MaxValue : 1, cause);
    }

    /// <summary>
    /// Ends a pending timed effect now (the run interrupted: a dispel, an explicit stop, the death of its caster or target):
    /// its END runs at once and its event is cancelled. <see cref="GameSessionFailure.UnknownTimedEffect"/> when no END with
    /// that sequence is pending.
    /// </summary>
    public SessionEffectResult EndHeroTimedEffect(GameSessionVersion expected, long sequence)
    {
        ThrowIfDisposed();
        if (_hero is null) return RejectEffect(GameSessionFailure.EffectsUnavailable);
        var pending = _events.QueryPending().FirstOrDefault(e => e.Kind == SessionEventKinds.HeroEffectEnd && e.Sequence == sequence);
        if (pending is null) return RejectEffect(GameSessionFailure.UnknownTimedEffect);
        if (_passWorld is not null) return RejectEffect(GameSessionFailure.InvalidEventPass);
        var result = RemoveEffects(expected, (int)pending.Payload.Values[2], (int)pending.Payload.Values[3], null, cancel: pending);
        return result;
    }

    /// <summary>The hero's blessings or curses in list order (ARC-WORLD-035), or null for a session without a hero. An undefined
    /// <paramref name="kind"/> throws <see cref="ArgumentOutOfRangeException"/> (a query; the mutations refuse it as
    /// <see cref="GameSessionFailure.InvalidEffect"/>).</summary>
    public IReadOnlyList<SessionBlessing>? QueryHeroBlessings(SessionBlessingKind kind)
    {
        ThrowIfDisposed();
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        return _hero is null ? null : Array.AsReadOnly(_effects.List(kind).ToArray());
    }

    /// <summary>
    /// The blessing and curse add (0x4C42F0, 0x4C3E40; ARC-WORLD-035; a script's add-blessing and add-curse actions): after the hero and
    /// version checks, NoChange when the hero already holds <paramref name="number"/> (the effect is not looked at, as in the
    /// source), <see cref="GameSessionFailure.EffectCapacityExceeded"/> at <see cref="SessionEffectLimits.MaxBlessings"/>; otherwise the number and the current game time are appended to the list and
    /// <paramref name="effect"/> is added with the bless or curse cause, as <see cref="AddHeroEffect"/> adds it (its refusals
    /// apply; nothing is written then). <paramref name="effect"/> is the rule of the number's effect entry, which the host
    /// reads with <c>BlessingRules.EffectOf</c> (a number below 50, a missing entry or a text without a number give effect 0,
    /// the module's <c>gameeffect.mes</c> effect 0, as in the original). The host shows the "received" message (entry 1000)
    /// and marks the logbook on a change. One revision.
    /// </summary>
    [OriginalFunction(0x4C42F0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x4C3E40, OriginalFunctionScope.Partial)]
    public SessionEffectResult AddHeroBlessing(GameSessionVersion expected, SessionBlessingKind kind, int number, SessionEffectDefinition effect)
    {
        ThrowIfDisposed();
        if (!Enum.IsDefined(kind)) return RejectEffect(GameSessionFailure.InvalidEffect);
        if (_hero is null) return RejectEffect(GameSessionFailure.EffectsUnavailable);
        if (!IsCurrent(expected)) return RejectEffect(GameSessionFailure.StaleVersion);
        var list = _effects.List(kind);
        if (list.Any(b => b.Number == number)) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, 0, null, null, Version);
        if (list.Count >= SessionEffectLimits.MaxBlessings) return RejectEffect(GameSessionFailure.EffectCapacityExceeded);
        var cause = kind == SessionBlessingKind.Blessing ? EffectCause.Bless : EffectCause.Curse;
        return AddEffects(expected, effect, cause, 1, null, now => list.Add(new(number, effect.Number, now)));
    }

    /// <summary>
    /// The blessing and curse removal (0x4C4450, 0x4C3FA0): NoChange when the hero does not hold <paramref name="number"/>; otherwise its
    /// first entry leaves the list (the later ones move up) and the first effect entry of the number it added is removed, as
    /// <see cref="RemoveHeroEffect"/> removes it (whatever caused that entry, as in the source; none may be left). One revision.
    /// </summary>
    [OriginalFunction(0x4C4450, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x4C3FA0, OriginalFunctionScope.Partial)]
    public SessionEffectResult RemoveHeroBlessing(GameSessionVersion expected, SessionBlessingKind kind, int number)
    {
        ThrowIfDisposed();
        if (!Enum.IsDefined(kind)) return RejectEffect(GameSessionFailure.InvalidEffect);
        if (_hero is null) return RejectEffect(GameSessionFailure.EffectsUnavailable);
        if (!IsCurrent(expected)) return RejectEffect(GameSessionFailure.StaleVersion);
        var list = _effects.List(kind);
        var index = list.FindIndex(b => b.Number == number);
        if (index < 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, 0, null, null, Version);
        return RemoveEffects(expected, list[index].Effect, 1, null, apply: _ => list.RemoveAt(index));
    }

    // An END that came due: its removals, without a version check, as any timer's step.
    internal GameEventHandlerOutcome FireEffectEnd(GameEventFiring firing)
    {
        if (_hero is null) return GameEventHandlerOutcome.Refused;
        var values = firing.Event.Payload.Values;
        var result = RemoveEffects(Version, (int)values[2], (int)values[3], null);
        _passEffects?.Add(new(firing.Event, result));
        return result.Disposition == GameSessionDisposition.Rejected ? GameEventHandlerOutcome.Refused : GameEventHandlerOutcome.Completed;
    }

    private IReadOnlyList<SessionTimedEffect> TimedEffects() => Array.AsReadOnly(_events.QueryPending()
        .Where(e => e.Kind == SessionEventKinds.HeroEffectEnd)
        .Select(e => new SessionTimedEffect(e.Sequence, e.Payload.Values[0], e.DeadlineMs, (int)e.Payload.Values[2], (int)e.Payload.Values[3]))
        .ToArray());

    private SessionEffectResult AddEffects(GameSessionVersion expected, SessionEffectDefinition definition, EffectCause cause, int count,
        long? durationMs, Action<long>? apply = null)
    {
        if (_hero is null) return RejectEffect(GameSessionFailure.EffectsUnavailable);
        if (!EffectDomain.IsValid(definition) || !EffectDomain.OwnCause(cause) || count < 1) return RejectEffect(GameSessionFailure.InvalidEffect);
        if (EffectDomain.IsInnate(_hero.State, definition.Number)) return RejectEffect(GameSessionFailure.InnateEffect);
        if (_effects.Definitions.TryGetValue(definition.Number, out var known) && !known.SameAs(definition))
            return RejectEffect(GameSessionFailure.EffectMismatch);
        if (!IsCurrent(expected)) return RejectEffect(GameSessionFailure.StaleVersion);
        if (count > SessionEffectLimits.MaxHeroEffects - _effects.Entries.Count) return RejectEffect(GameSessionFailure.EffectCapacityExceeded);
        var definitions = new Dictionary<int, SessionEffectDefinition>(_effects.Definitions) { [definition.Number] = definition };
        if (!EffectDomain.Bounded([.. _effects.Entries, .. Enumerable.Repeat(new SessionEffect(definition.Number, cause), count)], definitions, innate: HeroInnateRules()))
            return RejectEffect(GameSessionFailure.InvalidEffect);
        var steps = Enumerable.Repeat(new EffectStep(new SessionEffect(definition.Number, cause), -1), count).ToArray();
        return Commit(steps, definition, durationMs is { } d ? (definition.Number, count, d) : null, null, apply);
    }

    // The first matches by number or cause, up to count of them, as the source's loops find them: each removal shifts the
    // later entries up. An innate entry ahead of the durable ones would be the first match of its number, so a typed removal
    // of an innate number is refused (no durable entry or timed END can have one).
    private SessionEffectResult RemoveEffects(GameSessionVersion expected, int? number, int count, EffectCause? cause, GameEvent? cancel = null,
        Action<long>? apply = null)
    {
        if (_hero is null) return RejectEffect(GameSessionFailure.EffectsUnavailable);
        if (number is { } n && EffectDomain.IsInnate(_hero.State, n)) return RejectEffect(GameSessionFailure.InnateEffect);
        if (!IsCurrent(expected)) return RejectEffect(GameSessionFailure.StaleVersion);
        var indices = new List<int>();
        var shifted = 0;
        for (var i = 0; i < _effects.Entries.Count && indices.Count < count; i++)
        {
            var entry = _effects.Entries[i];
            if (number is { } k ? entry.Number != k : entry.Cause != cause) continue;
            indices.Add(i - shifted);
            shifted++;
        }
        var steps = indices.Select(index => new EffectStep(null, index)).ToArray();
        if (steps.Length == 0 && cancel is null && apply is null) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, 0, null, null, Version);
        return Commit(steps, null, null, cancel, apply);
    }

    // Every change: the steps on a trial character owner, each moving the damage by the change of a maximum, then the
    // admission of the life facts, the timers and the revision; nothing is written before every check passed.
    private SessionEffectResult Commit(EffectStep[] steps, SessionEffectDefinition? added, (int Number, int Count, long Duration)? timed,
        GameEvent? cancel, Action<long>? apply = null)
    {
        var definitions = new Dictionary<int, SessionEffectDefinition>(_effects.Definitions);
        if (added is not null) definitions[added.Number] = added;
        var failure = PrepareEffects(steps, definitions, timed is null ? 0 : 1, out var prepared);
        if (failure != GameSessionFailure.None) return RejectEffect(failure);
        if (timed is not null && _events.QueryPending().Count(e => e.Kind == SessionEventKinds.HeroEffectEnd) >= SessionEffectLimits.MaxTimedEffects)
            return RejectEffect(GameSessionFailure.EventBudgetExceeded);
        if (_revision == long.MaxValue) return RejectEffect(GameSessionFailure.RevisionExhausted);

        ApplyEffects(prepared!);
        var now = prepared!.Now;
        long? sequence = null;
        if (timed is { } t)
        {
            var scheduled = _events.ScheduleAfter(SessionEventKinds.HeroEffectEnd, GameEventPayload.Of([], now, t.Duration, t.Number, t.Count),
                t.Duration, EventClocks(now));
            if (scheduled.Disposition != GameEventScheduleDisposition.Added) throw new InvalidOperationException("END scheduling failed after admission.");
            sequence = scheduled.Event!.Sequence;
        }
        if (cancel is not null) _events.RemoveFirst(cancel.Kind, e => e.Sequence == cancel.Sequence);
        apply?.Invoke(now);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, steps.Length, prepared.LifeChanged ? _life.Hero : null, sequence,
            new(_identity, _revision));
    }

    /// <summary>A checked change of the hero's effect list (ARC-SCRIPT-042 split it from <see cref="Commit"/>, so an item move can apply it
    /// with its other changes in one revision): the steps, the rules of every number they and the list use, the hero's life state after
    /// them and its timers.</summary>
    private sealed record PreparedEffects(EffectStep[] Steps, Dictionary<int, SessionEffectDefinition> Definitions, LifeState State, bool LifeChanged,
        (string Kind, GameEventPayload Payload)[] Timers, long Now)
    {
        /// <summary>ARC-MAGIC-1658: when the trial went through changes of the hero's race or gender (<c>stages</c> of <see cref="PrepareEffects"/>), the character owner it ended with, with the durable entries applied, and their ids.</summary>
        internal HeroDomain? Staged { get; init; }
        internal List<AppliedEffectId?>? StagedIds { get; init; }
    }

    // The trial: the steps on a character owner built from the hero, each moving the damage by the change of a maximum (the effect add
    // 0x4E9F70 and the internal removal 0x4EA520; damage never goes below 0), then the life facts and the event budget for the timers and
    // extraEvents more. Nothing is written.
    private GameSessionFailure PrepareEffects(EffectStep[] steps, Dictionary<int, SessionEffectDefinition> definitions, int extraEvents, out PreparedEffects? prepared) =>
        PrepareEffects(steps, definitions, extraEvents, [], out prepared);

    // ARC-MAGIC-1658: stages are hero states the trial passes through before the steps (the removal and the add of the race and gender setters, each moving the damage as an effect's does): the character owner is built again from each
    // with the durable entries applied to it again. A state the character rules refuse, or an entry its owner refuses, is InvalidHero or InvalidEffect.
    private GameSessionFailure PrepareEffects(EffectStep[] steps, Dictionary<int, SessionEffectDefinition> definitions, int extraEvents, IReadOnlyList<HeroStage> stages, out PreparedEffects? prepared)
    {
        prepared = null;
        // The staged owner and the steps would both be applied to it again by ApplyEffects: the stages stand alone.
        if (stages.Count > 0 && steps.Length > 0) return GameSessionFailure.InvalidEffect;
        var trial = TrialHero();
        var entries = new List<SessionEffect>(_effects.Entries);
        var ids = new List<AppliedEffectId?>();
        foreach (var entry in entries) ids.Add(ApplyStats(trial.Stats, entry, definitions));
        var before = _life.Hero!.Value;
        var state = before.State;
        var innate = HeroInnateRules();
        var facts = TrialFacts(trial, entries, definitions, state, innate);
        if (facts is null) return GameSessionFailure.InvalidEffect;
        foreach (var stage in stages)
        {
            if (HeroDomain.BuildFrom(stage.State) is not { } staged) return GameSessionFailure.InvalidHero;
            innate = stage.Innate;
            trial = staged;
            ids = [];
            try { foreach (var entry in entries) ids.Add(ApplyStats(trial.Stats, entry, definitions)); }
            catch (InvalidOperationException) { return GameSessionFailure.InvalidEffect; }
            var after = TrialFacts(trial, entries, definitions, state, innate);
            if (after is null) return GameSessionFailure.InvalidEffect;
            state = state with
            {
                HitPointDamage = Shift(state.HitPointDamage, LifeRules.MaxHitPoints(after, state.PoisonLevel) - (long)LifeRules.MaxHitPoints(facts, state.PoisonLevel)),
                FatigueDamage = Shift(state.FatigueDamage, LifeRules.MaxFatigue(after) - (long)LifeRules.MaxFatigue(facts)),
            };
            facts = after;
        }
        foreach (var step in steps)
        {
            if (step.Add is { } entry)
            {
                entries.Add(entry);
                ids.Add(ApplyStats(trial.Stats, entry, definitions));
            }
            else
            {
                entries.RemoveAt(step.RemoveAt);
                if (ids[step.RemoveAt] is { } id && trial.Stats.RemoveEffect(id).Disposition == CharacterStatsDisposition.Rejected)
                    throw new InvalidOperationException("Trial effect removal failed.");
                ids.RemoveAt(step.RemoveAt);
            }
            var next = TrialFacts(trial, entries, definitions, state, innate);
            if (next is null) return GameSessionFailure.InvalidEffect;
            state = state with
            {
                HitPointDamage = Shift(state.HitPointDamage, LifeRules.MaxHitPoints(next, state.PoisonLevel) - (long)LifeRules.MaxHitPoints(facts, state.PoisonLevel)),
                FatigueDamage = Shift(state.FatigueDamage, LifeRules.MaxFatigue(next) - (long)LifeRules.MaxFatigue(facts)),
            };
            facts = next;
        }
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        var timers = Timers(true, before.State, state, null).Select(timer => TimerEvent(timer, SessionLifeTarget.Hero, 0, now, 1))
            .Where(pending => !IsPending(pending.Kind, null)).ToArray();
        var events = timers.Length + extraEvents;
        if (events > 0 && (_events.Count > _events.Limits.MaxPending - events || _events.NextSequence > long.MaxValue - events ||
            now > long.MaxValue - Math.Max(SessionEffectLimits.MaxDurationMs, SessionEventKinds.RestingDelayMs)))
            return GameSessionFailure.EventBudgetExceeded;
        prepared = new(steps, definitions, state, state != before.State, timers, now) { Staged = stages.Count > 0 ? trial : null, StagedIds = stages.Count > 0 ? ids : null };
        return GameSessionFailure.None;

        static int Shift(int damage, long change) => (int)Math.Clamp(damage + change, 0, LifeLimits.MaxDamage);
    }

    // The live character owner takes the prepared steps (the trial already showed each of them is admitted); the rules kept are those of
    // the numbers still in the list; the life state and its timers follow. No revision.
    private void ApplyEffects(PreparedEffects prepared)
    {
        var hero = _hero!;
        var before = _life.Hero!.Value;
        // ARC-MAGIC-1658: the character owner the trial ended with (the new race and gender with the durable entries applied) replaces the live one; the entries' ids are its own.
        if (prepared.Staged is { } staged)
        {
            hero.Replace(staged);
            _effects.Applied.Clear();
            _effects.Applied.AddRange(prepared.StagedIds!);
        }
        foreach (var step in prepared.Steps)
        {
            if (step.Add is { } entry)
            {
                _effects.Entries.Add(entry);
                _effects.Applied.Add(ApplyStats(hero.Stats, entry, prepared.Definitions));
            }
            else
            {
                if (_effects.Applied[step.RemoveAt] is { } id && hero.Stats.RemoveEffect(id).Disposition == CharacterStatsDisposition.Rejected)
                    throw new InvalidOperationException("Effect removal failed after admission.");
                _effects.Entries.RemoveAt(step.RemoveAt);
                _effects.Applied.RemoveAt(step.RemoveAt);
            }
        }
        _effects.Definitions.Clear();
        foreach (var number in _effects.Entries.Select(e => e.Number).Distinct()) _effects.Definitions[number] = prepared.Definitions[number];
        if (prepared.LifeChanged) _life.Hero = before with { State = prepared.State };
        foreach (var (kind, payload) in prepared.Timers)
            if (_events.ScheduleAfter(kind, payload, SessionEventKinds.Delay(TimerOf(kind)), EventClocks(prepared.Now)).Disposition != GameEventScheduleDisposition.Added)
                throw new InvalidOperationException("Timer scheduling failed after admission.");
    }

    // A character owner built from the hero's saved fields, for trials.
    private HeroDomain TrialHero()
    {
        var hero = _hero!;
        return HeroDomain.Build(hero.State.Prototype, hero.State.Race, hero.State.Gender, hero.State.Stats, hero.State.Level,
            hero.State.UnspentPoints, hero.State.Alignment, hero.State.RaceEffect, hero.State.GenderEffect, hero.State.Experience,
            hero.State.Advancement, hero.State.InnateSkills)!;
    }

    // The hero's life facts under a trial list, or null when the life rules refuse the maxima it gives.
    private LifeFacts? TrialFacts(HeroDomain trial, IReadOnlyList<SessionEffect> entries, IReadOnlyDictionary<int, SessionEffectDefinition> definitions,
        LifeState state, ImmutableArray<SessionEffectDefinition> innate)
    {
        var facts = LifeDomain.HeroFacts(trial.Stats, trial.State, state, HeroNormalResistance(entries, definitions, innate),
            EffectDomain.Modifiers(entries, definitions, EffectSpecial.MaxHitPoints, innate), EffectDomain.Modifiers(entries, definitions, EffectSpecial.MaxFatigue, innate));
        return LifeRules.IsValid(facts) ? facts : null;
    }

    // The entry's modelled stat changes into a character owner; its id, or null for a rule without any.
    private static AppliedEffectId? ApplyStats(CharacterStatsOwner stats, SessionEffect entry, IReadOnlyDictionary<int, SessionEffectDefinition> definitions)
    {
        if (EffectDomain.Stats(definitions[entry.Number]) is not { } modifiers) return null;
        var result = stats.AddEffect(modifiers, entry.Cause);
        return result.AddedEffect ?? throw new InvalidOperationException("Effect refused by the character owner after admission.");
    }

    /// <summary>The hero's life facts at a state, with its worn armour and durable effects.</summary>
    private LifeFacts HeroLifeFacts(LifeState state) => WithWrittenHolds(SessionEventKinds.PlayerEntity, LifeDomain.HeroFacts(_hero!.Stats, _hero.State, state, HeroNormalResistance(),
        _effects.Maximum(EffectSpecial.MaxHitPoints, HeroInnateRules()), _effects.Maximum(EffectSpecial.MaxFatigue, HeroInnateRules())));

    private SessionEffectResult RejectEffect(GameSessionFailure failure) =>
        new(GameSessionDisposition.Rejected, failure, 0, null, null, new(_identity, _revision));

    // Saved effects: only with a hero; every entry of an own cause with a rule of its number, every rule admitted and in use,
    // by ascending number; the entries applied in order to the restored hero, whose life facts must stay valid.
    private static GameSessionFailure ImportEffects(SessionEffectState saved, HeroDomain? hero, LifeDomain life, long clockMs, out EffectDomain effects)
    {
        effects = new();
        if (hero is null)
            return saved.Hero.Count == 0 && saved.Definitions.Count == 0 && saved.Blessings.Count == 0 && saved.Curses.Count == 0
                ? GameSessionFailure.None : GameSessionFailure.InvalidSnapshot;
        // ARC-WORLD-035: each list holds a number once, received no later than the saved clock, with an effect number of the
        // tables that no innate entry has (its add would have been refused).
        foreach (var (list, into) in new[] { (saved.Blessings, effects.Blessings), (saved.Curses, effects.Curses) })
        {
            if (list.Count > SessionEffectLimits.MaxBlessings) return GameSessionFailure.EffectCapacityExceeded;
            foreach (var entry in list)
            {
                if (entry.TimeMs < 0 || entry.TimeMs > clockMs || into.Any(b => b.Number == entry.Number) ||
                    entry.Effect is < SessionEffectLimits.FirstNumber or > SessionEffectLimits.LastNumber ||
                    EffectDomain.IsInnate(hero.State, entry.Effect)) return GameSessionFailure.InvalidEffect;
                into.Add(entry);
            }
        }
        if (saved.Hero.Count > SessionEffectLimits.MaxHeroEffects) return GameSessionFailure.EffectCapacityExceeded;
        var previous = int.MinValue;
        foreach (var definition in saved.Definitions)
        {
            if (!EffectDomain.IsValid(definition) || definition.Number <= previous) return GameSessionFailure.InvalidEffect;
            previous = definition.Number;
            effects.Definitions.Add(definition.Number, definition);
        }
        foreach (var entry in saved.Hero)
        {
            if (!EffectDomain.OwnCause(entry.Cause) || !effects.Definitions.ContainsKey(entry.Number) ||
                EffectDomain.IsInnate(hero.State, entry.Number)) return GameSessionFailure.InvalidEffect;
            effects.Entries.Add(entry);
            try { effects.Applied.Add(ApplyStats(hero.Stats, entry, effects.Definitions)); }
            catch (InvalidOperationException) { return GameSessionFailure.InvalidEffect; }
        }
        if (!EffectDomain.Bounded(effects.Entries, effects.Definitions)) return GameSessionFailure.InvalidEffect;
        var used = effects.Entries.Select(e => e.Number).ToHashSet();
        if (effects.Definitions.Keys.Any(number => !used.Contains(number))) return GameSessionFailure.InvalidEffect;
        // The stats and maxima under every saved entry: the life rules must admit the hero's facts at its saved state.
        var facts = LifeDomain.HeroFacts(hero.Stats, hero.State, life.Hero!.Value.State, 0,
            effects.Maximum(EffectSpecial.MaxHitPoints), effects.Maximum(EffectSpecial.MaxFatigue));
        return LifeRules.IsValid(facts) ? GameSessionFailure.None : GameSessionFailure.InvalidEffect;
    }
}

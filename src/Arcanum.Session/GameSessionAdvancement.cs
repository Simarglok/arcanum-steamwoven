using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Arcanum.Data.Characters;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Skills;

namespace Arcanum.Session;

/// <summary>Bounds of the advancement domain (ARC-WORLD-036; project policy, the source has none).</summary>
public static class SessionAdvancementLimits
{
    /// <summary>The largest hit or fatigue points the hero holds: above what a pre-made hero's points can buy (5 at the start and
    /// 59 for levels 2..50), low enough that the effect bounds of the hero's maxima keep holding. A scheme run that would take the
    /// hero past it buys nothing (a project guard; no installed hero comes near it).</summary>
    public const int MaxHeroPoolPoints = 100;
    /// <summary>The scheme numbers the source looks up (1..49 the user file, 50..199 the game file); 0 is none.</summary>
    public const int MaxScheme = 199;
}

/// <summary>
/// The hero's purchasable fields besides its stats (ARC-WORLD-036): the stored basic and tech skill fields (points in bits
/// 0..5, training in bits 6..7), the sixteen college levels, the eight tech degrees, the bought hit and fatigue points, and
/// the auto-level scheme (0: none), and the mastered college (−1 none, ARC-MAGIC-004, snapshot schema 25). A pre-made hero starts
/// with none of them bought, no mastery and its prototype's scheme.
/// </summary>
public sealed record SessionHeroAdvancement(ImmutableArray<int> BasicSkills, ImmutableArray<int> TechSkills, ImmutableArray<int> Colleges,
    ImmutableArray<int> TechDegrees, int HitPointPoints, int FatiguePoints, int AutoLevelScheme, int MasteredCollege = SpellBookRules.NoMastery)
{
    /// <summary>Equality compares the arrays' values.</summary>
    public bool Equals(SessionHeroAdvancement? other) => other is not null && Same(BasicSkills, other.BasicSkills) &&
        Same(TechSkills, other.TechSkills) && Same(Colleges, other.Colleges) && Same(TechDegrees, other.TechDegrees) &&
        HitPointPoints == other.HitPointPoints && FatiguePoints == other.FatiguePoints && AutoLevelScheme == other.AutoLevelScheme &&
        MasteredCollege == other.MasteredCollege;

    public override int GetHashCode() => HashCode.Combine(HitPointPoints, FatiguePoints, AutoLevelScheme, Hash(BasicSkills), Hash(Colleges));

    private static bool Same(ImmutableArray<int> a, ImmutableArray<int> b) => a.IsDefault ? b.IsDefault : !b.IsDefault && a.SequenceEqual(b);

    private static int Hash(ImmutableArray<int> values)
    {
        var hash = new HashCode();
        if (!values.IsDefault) foreach (var value in values) hash.Add(value);
        return hash.ToHashCode();
    }

    public static SessionHeroAdvancement None { get; } = new([.. new int[AutoLevelRules.BasicSkills]], [.. new int[AutoLevelRules.TechSkills]],
        [.. new int[AutoLevelRules.Colleges]], [.. new int[AutoLevelRules.TechDisciplines]], 0, 0, 0);

    /// <summary>The same fields with only the scheme set (a new hero).</summary>
    public static SessionHeroAdvancement WithScheme(int scheme) => None with { AutoLevelScheme = scheme };

    internal bool IsValid() =>
        Length(BasicSkills, AutoLevelRules.BasicSkills, 255) && Length(TechSkills, AutoLevelRules.TechSkills, 255) &&
        Length(Colleges, AutoLevelRules.Colleges, AutoLevelRules.SpellsPerCollege) &&
        Length(TechDegrees, AutoLevelRules.TechDisciplines, AutoLevelRules.MaxDegree) &&
        HitPointPoints is >= 0 and <= SessionAdvancementLimits.MaxHeroPoolPoints &&
        FatiguePoints is >= 0 and <= SessionAdvancementLimits.MaxHeroPoolPoints && AutoLevelScheme is >= 0 and <= SessionAdvancementLimits.MaxScheme &&
        MasteredCollege is >= SpellBookRules.NoMastery and < SpellBookRules.Colleges;

    private static bool Length(ImmutableArray<int> values, int length, int max) =>
        !values.IsDefault && values.Length == length && values.All(v => v >= 0 && v <= max);
}

/// <summary>
/// The scheme data a session buys with (ARC-WORLD-036), built by the host from the data (<see cref="AutoLevelTables"/> and
/// <see cref="AutoLevelRules.Parse"/>): the parsed rule of each scheme number that has one, and the willpower each of the 80
/// spells needs. Data, not state: never saved.
/// </summary>
public sealed class SessionAutoLevelRules
{
    private readonly ReadOnlyDictionary<int, AutoLevelScheme> _schemes;

    private SessionAutoLevelRules(Dictionary<int, AutoLevelScheme> schemes, ImmutableArray<int> spellWillpower)
    {
        _schemes = new(schemes);
        SpellWillpower = spellWillpower;
    }

    public ImmutableArray<int> SpellWillpower { get; }

    /// <summary>The rules, or null when a scheme number is outside 1..199, a rule is missing, or the willpower minima are not 80.</summary>
    public static SessionAutoLevelRules? Create(IReadOnlyDictionary<int, AutoLevelScheme>? schemes, IReadOnlyList<int>? spellWillpower)
    {
        if (schemes is null || spellWillpower is null || spellWillpower.Count != AutoLevelRules.Spells) return null;
        var copy = new Dictionary<int, AutoLevelScheme>();
        foreach (var (number, scheme) in schemes)
        {
            if (number is < 1 or > SessionAdvancementLimits.MaxScheme || scheme is null || scheme.Steps.IsDefault) return null;
            copy.Add(number, scheme);
        }
        return new(copy, [.. spellWillpower]);
    }

    /// <summary>The rule of a scheme number, or null for 0, another number or one without a rule (the source then buys nothing).</summary>
    public AutoLevelScheme? Scheme(int number) => _schemes.GetValueOrDefault(number);
}

/// <summary>
/// One NPC's advancement (ARC-WORLD-036): its sheet (level, points, stats, skills, colleges, degrees, pools) and its auto-level
/// scheme, as its map data loads them (the host's input) or as the session saved them after a level update.
/// </summary>
public sealed record SessionNpcAdvancement(string Entity, AdvancementSheet Sheet, int AutoLevelScheme);

/// <summary>
/// Whose auto-level scheme a script sets (SCR action 139, ARC-SCRIPT-016): the hero, or an NPC with its map data (sheet and scheme,
/// read only while the session has no record of it).
/// </summary>
public sealed class SessionSchemeTarget
{
    private SessionSchemeTarget(SessionNpcAdvancement? npc) => Npc = npc;

    public static SessionSchemeTarget Hero { get; } = new(null);

    public static SessionSchemeTarget ForNpc(SessionNpcAdvancement loaded) => new(loaded ?? throw new ArgumentNullException(nameof(loaded)));

    /// <summary>The NPC's loaded record, or null for the hero.</summary>
    public SessionNpcAdvancement? Npc { get; }
}

/// <summary>A scheme change: the scheme the target holds after the call (null for a refusal).</summary>
public sealed record SessionSchemeResult(GameSessionDisposition Disposition, GameSessionFailure Failure, int? Scheme, GameSessionVersion Version);

/// <summary>Saved advancement records of the NPCs the session levelled, in ordinal entity order (snapshot schema 20).</summary>
public sealed class SessionAdvancementState
{
    internal SessionAdvancementState(SessionNpcAdvancement[] npcs) => Npcs = Array.AsReadOnly(npcs);

    public static SessionAdvancementState Empty { get; } = new([]);

    public IReadOnlyList<SessionNpcAdvancement> Npcs { get; }
}

/// <summary>
/// A follower's level update: its level after the step and the points it gained, the scheme run (null when the level stayed:
/// the source runs the scheme only after a rise), the record after the call (null for a refusal).
/// </summary>
public sealed record SessionFollowerLevelResult(GameSessionDisposition Disposition, GameSessionFailure Failure, FollowerLevelUp? Level,
    AutoLevelOutcome? Scheme, SessionNpcAdvancement? Record, GameSessionVersion Version);

/// <summary>The advancement domain of one owner: its scheme data and the NPC records it changed.</summary>
internal sealed class AdvancementDomain(SessionAutoLevelRules? rules)
{
    internal SessionAutoLevelRules? Rules { get; } = rules;
    internal Dictionary<string, SessionNpcAdvancement> Npcs { get; } = new(StringComparer.Ordinal);

    internal SessionAdvancementState Capture() =>
        new(Npcs.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value).ToArray());

    /// <summary>A scheme run's sheet the hero may hold: its bought pools within <see cref="SessionAdvancementLimits.MaxHeroPoolPoints"/>.</summary>
    internal static bool HeroBounded(AdvancementSheet sheet) =>
        sheet.HitPointPoints <= SessionAdvancementLimits.MaxHeroPoolPoints && sheet.FatiguePoints <= SessionAdvancementLimits.MaxHeroPoolPoints;

    internal static bool Valid(SessionNpcAdvancement? npc) => npc is not null && SessionValidation.Entity(npc.Entity) &&
        AutoLevelRules.IsValid(npc.Sheet) && npc.AutoLevelScheme is >= 0 and <= SessionAdvancementLimits.MaxScheme;

    /// <summary>The hero's sheet: its stored stats, magick and tech points, level and points, and its advancement fields
    /// (a PC has no hit-point or fatigue adjustment and is never monstrous).</summary>
    internal static AdvancementSheet HeroSheet(SessionHeroState hero) => new(hero.Race, false, [.. hero.Stats.Take(AutoLevelRules.PrimaryStats)],
        hero.Stats[8], hero.Stats[9], hero.Level, hero.UnspentPoints, hero.Advancement.BasicSkills, hero.Advancement.TechSkills,
        hero.Advancement.Colleges, hero.Advancement.TechDegrees, hero.Advancement.HitPointPoints, 0, hero.Advancement.FatiguePoints, 0,
        hero.Advancement.MasteredCollege);

    /// <summary>The hero after a sheet: its stats, points, level and advancement fields from the sheet, everything else kept.</summary>
    internal static SessionHeroState WithSheet(SessionHeroState hero, AdvancementSheet sheet, int experience) => hero.With(
        [.. sheet.Stats, sheet.MagickPoints, sheet.TechPoints], sheet.Level, experience, sheet.UnspentPoints,
        hero.Advancement with
        {
            BasicSkills = sheet.BasicSkills, TechSkills = sheet.TechSkills, Colleges = sheet.Colleges, TechDegrees = sheet.TechDegrees,
            HitPointPoints = sheet.HitPointPoints, FatiguePoints = sheet.FatiguePoints, MasteredCollege = sheet.MasteredCollege,
        });

    /// <summary>
    /// The changes the hero's purchases and skill levels read: the stat changes of every effect its character owner holds
    /// (innate ones included), the skill changes of its race and gender effects (ARC-SKILLS-002) and of its durable effects, the
    /// maxima changes of its durable effects, and its poison level.
    /// </summary>
    internal static AdvancementModifiers HeroModifiers(HeroDomain hero, EffectDomain effects, int poisonLevel)
    {
        var applied = hero.Stats.Query(new(0)).Snapshot!.AppliedEffects;
        var changes = effects.Entries.SelectMany(entry => effects.Definitions[entry.Number].Changes).ToArray();
        SkillModifier[] Skills(EffectAttributeKind kind, bool tech) =>
        [
            .. hero.State.InnateSkills.Where(c => SkillRules.IsTech(c.Skill) == tech)
                .Select(c => new SkillModifier(SkillRules.FamilyIndex(c.Skill), c.Operator, c.Value)),
            .. changes.Where(c => c.Kind == kind).Select(c => new SkillModifier(c.Attribute, EffectDomain.Operator(c.Operator), c.Value)),
        ];
        return new([.. applied.SelectMany(effect => effect.Definition.Modifiers)], [.. Skills(EffectAttributeKind.BasicSkill, false)],
            [.. Skills(EffectAttributeKind.TechSkill, true)], effects.Maximum(EffectSpecial.MaxHitPoints), effects.Maximum(EffectSpecial.MaxFatigue),
            poisonLevel);
    }
}

public sealed partial class GameSessionOwner
{
    /// <summary>The session's scheme data, or null when no scheme runs (ARC-WORLD-036).</summary>
    public SessionAutoLevelRules? AutoLevel { get { ThrowIfDisposed(); return _advancement.Rules; } }

    /// <summary>An NPC's saved advancement record, or null while the session never levelled it. An invalid entity id throws.</summary>
    public SessionNpcAdvancement? QueryNpcAdvancement(string entity)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) throw new ArgumentException("Invalid entity id.", nameof(entity));
        return _advancement.Npcs.TryGetValue(entity, out var record) ? record : null;
    }

    /// <summary>
    /// A follower's level update after the hero's level step (0x4A6CB0; the step is an award's
    /// <see cref="SessionExperienceAward.Followers"/>): the follower's new level and points
    /// (<see cref="ExperienceRules.FollowerLevel"/>), then its scheme (0x4A7030, rule 70 after it completes). The host calls it
    /// for every follower and every waiting follower near the hero, as the source does. <paramref name="loaded"/> is the NPC's map
    /// data (sheet and scheme), read only while the session has no record of it; <paramref name="modifiers"/> are its effects'
    /// changes and poison now. A level that would not rise changes nothing. Refused, in order: no scheme data
    /// (<see cref="GameSessionFailure.AdvancementUnavailable"/>); an invalid entity, sheet, scheme, modifiers or step
    /// (<see cref="GameSessionFailure.InvalidAdvancement"/>); the version; a new record beyond the budget; the revision.
    /// </summary>
    public SessionFollowerLevelResult LevelFollower(GameSessionVersion expected, SessionNpcAdvancement loaded, AdvancementModifiers modifiers,
        FollowerLevelStep step)
    {
        ThrowIfDisposed();
        if (_advancement.Rules is not { } rules) return RejectFollower(GameSessionFailure.AdvancementUnavailable);
        if (!AdvancementDomain.Valid(loaded) || !AutoLevelRules.IsValid(modifiers) || step.OldPcLevel is < 0 or > ExperienceRules.MaxLevel ||
            step.NewPcLevel is < 0 or > ExperienceRules.MaxLevel) return RejectFollower(GameSessionFailure.InvalidAdvancement);
        if (!IsCurrent(expected)) return RejectFollower(GameSessionFailure.StaleVersion);
        var record = _advancement.Npcs.TryGetValue(loaded.Entity, out var saved) ? saved : loaded;
        var up = ExperienceRules.FollowerLevel(record.Sheet.Level, step);
        if (up.Level == record.Sheet.Level) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, up, null, record, Version);
        var sheet = record.Sheet with
        {
            Level = up.Level, UnspentPoints = Math.Min(ExperienceRules.MaxUnspentPoints, record.Sheet.UnspentPoints + up.PointsGained),
        };
        var outcome = AutoLevelRules.Apply(sheet, modifiers, rules.Scheme(record.AutoLevelScheme), rules.Scheme(AutoLevelRules.StandardNpcScheme),
            npc: true, rules.SpellWillpower);
        var next = record with { Sheet = outcome.Sheet };
        if (!_advancement.Npcs.ContainsKey(next.Entity) && _advancement.Npcs.Count >= _limits.MaxAdvancementRecords)
            return RejectFollower(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectFollower(GameSessionFailure.RevisionExhausted);
        _advancement.Npcs[next.Entity] = next;
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, up, outcome, next, Version);
    }

    /// <summary>
    /// SCR action 139 (ARC-SCRIPT-016, ledger OL30): the target's auto-level scheme becomes <paramref name="scheme"/> (0x4A6D90 writes
    /// a PC's or an NPC's scheme field). Nothing is bought now: the hero's scheme runs at its next level step, an NPC's when its level
    /// rises (AL12), and a number without a rule then buys nothing, so the hero's points stay unspent. An NPC without a record gets one
    /// from its loaded sheet. The original stores any number; the session keeps schemes within 0..199 (its snapshot bound, the numbers
    /// the source looks up), so a number outside is refused here, a documented deviation that no installed script reaches. Refused, in
    /// order: no hero for a hero target (<see cref="GameSessionFailure.AdvancementUnavailable"/>); a number outside 0..199 or an
    /// invalid loaded record (<see cref="GameSessionFailure.InvalidAdvancement"/>); the version; a new record beyond the budget; the
    /// revision. The same scheme is <see cref="GameSessionDisposition.NoChange"/>.
    /// </summary>
    [Simulation.OriginalFunction(0x4A6D90, Simulation.OriginalFunctionScope.Partial)]
    public SessionSchemeResult SetAutoLevelScheme(GameSessionVersion expected, SessionSchemeTarget target, int scheme)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(target);
        if (target.Npc is null && _hero is null) return RejectScheme(GameSessionFailure.AdvancementUnavailable);
        if (scheme is < 0 or > SessionAdvancementLimits.MaxScheme || (target.Npc is { } loaded && !AdvancementDomain.Valid(loaded)))
            return RejectScheme(GameSessionFailure.InvalidAdvancement);
        if (!IsCurrent(expected)) return RejectScheme(GameSessionFailure.StaleVersion);
        if (target.Npc is not { } npc)
        {
            var hero = _hero!.State;
            if (hero.Advancement.AutoLevelScheme == scheme) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, scheme, Version);
            if (_revision == long.MaxValue) return RejectScheme(GameSessionFailure.RevisionExhausted);
            _hero.Apply(hero.With([.. hero.Stats], hero.Level, hero.Experience, hero.UnspentPoints, hero.Advancement with { AutoLevelScheme = scheme }));
        }
        else
        {
            var saved = _advancement.Npcs.TryGetValue(npc.Entity, out var existing) ? existing : null;
            var record = saved ?? npc;
            if (record.AutoLevelScheme == scheme) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, scheme, Version);
            if (saved is null && _advancement.Npcs.Count >= _limits.MaxAdvancementRecords) return RejectScheme(GameSessionFailure.BudgetExceeded);
            if (_revision == long.MaxValue) return RejectScheme(GameSessionFailure.RevisionExhausted);
            _advancement.Npcs[npc.Entity] = record with { AutoLevelScheme = scheme };
        }
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, scheme, Version);
    }

    private SessionSchemeResult RejectScheme(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, new(_identity, _revision));

    private SessionFollowerLevelResult RejectFollower(GameSessionFailure failure) =>
        new(GameSessionDisposition.Rejected, failure, null, null, null, new(_identity, _revision));

    /// <summary>
    /// The hero's scheme at creation (0x4A6F00 runs it after the level's points): its purchases from the starting unspent
    /// points, with only its innate effects. Null when there is no scheme data, no rule for the hero's scheme, or the run would
    /// take the hero's bought pools past their bound.
    /// </summary>
    [Simulation.OriginalFunction(0x4A6F00, Simulation.OriginalFunctionScope.Partial)]
    internal static AutoLevelOutcome? CreationScheme(HeroDomain hero, SessionAutoLevelRules? rules)
    {
        if (rules?.Scheme(hero.State.Advancement.AutoLevelScheme) is not { } scheme) return null;
        var modifiers = AdvancementDomain.HeroModifiers(hero, new EffectDomain(), 0);
        var outcome = AutoLevelRules.Apply(AdvancementDomain.HeroSheet(hero.State), modifiers, scheme, null, npc: false, rules.SpellWillpower);
        return AdvancementDomain.HeroBounded(outcome.Sheet) ? outcome : null;
    }

    // NPC records are valid, unique, in ordinal order and within the caller's budget.
    private static GameSessionFailure ImportAdvancement(SessionAdvancementState saved, SessionAutoLevelRules? rules, GameSessionLimits limits,
        out AdvancementDomain advancement)
    {
        advancement = new(rules);
        if (saved.Npcs.Count > limits.MaxAdvancementRecords) return GameSessionFailure.BudgetExceeded;
        string? previous = null;
        foreach (var npc in saved.Npcs)
        {
            if (!AdvancementDomain.Valid(npc)) return GameSessionFailure.InvalidAdvancement;
            if (previous is not null && string.CompareOrdinal(previous, npc.Entity) >= 0) return GameSessionFailure.InvalidSnapshot;
            previous = npc.Entity;
            advancement.Npcs.Add(npc.Entity, npc);
        }
        return GameSessionFailure.None;
    }
}

using System.Collections.Immutable;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Party;

namespace Arcanum.World;

/// <summary>
/// ARC-WORLD-036: the AREA viewer's auto-level data and its followers' level updates. The scheme data comes from
/// <c>rules\gamelevel.mes</c>, <c>rules\userlevel.mes</c> and <c>rules\spelllist.mes</c> (read with the hero's other start tables);
/// a follower's loaded sheet comes from its stored fields (<see cref="CombatFieldReader"/>) and its effects from the effect rules.
/// Godot-free; typed results, never an exception for data.
/// </summary>
public static class AreaAdvancement
{
    public const string GameSchemesPath = "rules/gamelevel.mes", UserSchemesPath = "rules/userlevel.mes", SpellsPath = "rules/spelllist.mes";
    private const uint MonsterFlag = 0x8000;
    private const int LevelStat = 17, UnspentStat = 21, MagickStat = 22, TechStat = 23, PoisonStat = 24, RaceStat = 27, MasteryIndex = 16;

    /// <summary>The session's scheme data from the three tables, or null with the reason (a rule the grammar refuses, an
    /// <c>IQ:</c> that is not a number).</summary>
    public static SessionAutoLevelRules? Rules(MesFile game, MesFile user, MesFile spells, out string? detail)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(spells);
        var schemes = new Dictionary<int, AutoLevelScheme>();
        for (var number = AutoLevelTables.FirstUserScheme; number <= AutoLevelTables.LastScheme; number++)
        {
            if (AutoLevelTables.Rule(game, user, number) is not { } text) continue;
            var parsed = AutoLevelRules.Parse(text.Span);
            if (parsed.Scheme is null)
            {
                detail = $"{(number < AutoLevelTables.FirstGameScheme ? UserSchemesPath : GameSchemesPath)} scheme {number} {parsed.Status}";
                return null;
            }
            schemes[number] = parsed.Scheme;
        }
        var willpower = AutoLevelTables.ReadSpellWillpower(spells);
        if (willpower.Minima is null)
        {
            detail = $"{SpellsPath} {willpower.Status} {willpower.Number}";
            return null;
        }
        detail = null;
        return SessionAutoLevelRules.Create(schemes, willpower.Minima);
    }

    /// <summary>
    /// A follower's loaded advancement and its modifiers from its stored fields: race, stats, level, points, magick and tech points
    /// and poison from the 28 base stats, skills, colleges, the mastered college (ARC-MAGIC-004; a book outside its ranges gives no
    /// admissible sheet) and degrees, hit and fatigue points and adjustments, the scheme and the
    /// monster flag; its effects' stat, skill and maxima changes through <paramref name="effect"/> (null for a number without a rule,
    /// which changes nothing). Null when the fields are not a critter's or give no admissible sheet.
    /// </summary>
    public static (SessionNpcAdvancement Loaded, AdvancementModifiers Modifiers)? Npc(string entity, CombatFields fields, Func<int, EffectRule?> effect) =>
        Npc(null, entity, fields, effect);

    /// <summary>
    /// ARC-MAGIC-1475: <see cref="Npc(string, CombatFields, Func{int, EffectRule?})"/> over the base stats a spell wrote into the NPC (<see cref="SessionCritterIndexedWrites.Apply"/>): the
    /// sheet a script reads and the one a recruit's advancement record is seeded from start from the written stats. The stored fields without a session.
    /// </summary>
    public static (SessionNpcAdvancement Loaded, AdvancementModifiers Modifiers)? Npc(GameSessionOwner? session, string entity, CombatFields fields, Func<int, EffectRule?> effect)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (session is not null) fields = session.QueryCritterIndexedWrites(entity).Apply(fields);
        ArgumentNullException.ThrowIfNull(effect);
        if (fields.Critter is not { } critter || critter.BaseStats.Count <= RaceStat || critter.SpellTech.Count < MasteryIndex + 1 + AutoLevelRules.TechDisciplines ||
            critter.BaseStats[RaceStat] is < 0 or > 10) return null;
        var stats = critter.BaseStats;
        var sheet = new AdvancementSheet((CharacterRace)stats[RaceStat], (critter.Flags & MonsterFlag) != 0, [.. stats.Take(AutoLevelRules.PrimaryStats)],
            stats[MagickStat], stats[TechStat], stats[LevelStat], stats[UnspentStat], [.. critter.BasicSkills], [.. critter.TechSkills],
            [.. critter.SpellTech.Take(AutoLevelRules.Colleges)], [.. critter.SpellTech.Skip(MasteryIndex + 1).Take(AutoLevelRules.TechDisciplines)],
            fields.Object.HitPoints, fields.Object.HitPointAdjustment, critter.FatiguePoints, critter.FatigueAdjustment,
            SpellBookRules.FromFields(critter.SpellTech)?.MasteredCollege ?? int.MinValue);
        var loaded = new SessionNpcAdvancement(entity, sheet, critter.AutoLevelScheme);
        var statChanges = ImmutableArray.CreateBuilder<StatModifier>();
        var basic = ImmutableArray.CreateBuilder<SkillModifier>();
        var tech = ImmutableArray.CreateBuilder<SkillModifier>();
        var maxHp = ImmutableArray.CreateBuilder<EffectModifier>();
        var maxFatigue = ImmutableArray.CreateBuilder<EffectModifier>();
        foreach (var entry in critter.Effects ?? [])
        {
            if (effect(entry.Number) is not { } rule) continue;
            foreach (var change in rule.Changes)
            {
                var op = Operator(change.Operator);
                switch (change.Kind)
                {
                    case EffectAttributeKind.Stat when Stat(change.Attribute) is { } stat: statChanges.Add(new(stat, op, change.Value)); break;
                    case EffectAttributeKind.BasicSkill: basic.Add(new(change.Attribute, op, change.Value)); break;
                    case EffectAttributeKind.TechSkill: tech.Add(new(change.Attribute, op, change.Value)); break;
                    case EffectAttributeKind.Special when change.Attribute == 0: maxHp.Add(new(op, change.Value)); break;
                    case EffectAttributeKind.Special when change.Attribute == 1: maxFatigue.Add(new(op, change.Value)); break;
                }
            }
        }
        var modifiers = new AdvancementModifiers(statChanges.ToImmutable(), basic.ToImmutable(), tech.ToImmutable(), maxHp.ToImmutable(),
            maxFatigue.ToImmutable(), Math.Clamp(stats[PoisonStat], 0, 1000));
        return AdvancementState(loaded, modifiers) ? (loaded, modifiers) : null;
    }

    /// <summary>
    /// Passes an award's follower step on as the source does after the hero's level step: to every follower in the array, then to the
    /// waiting followers <see cref="ExperienceRules.WaitingFollowerGetsStep"/> admits under the session's rules (ARC-WORLD-047, ledger
    /// OL29, owner decision D7): the Original setting only one on the loaded map within the window's rectangle around the PC, Steamwoven
    /// every one wherever it waits. <paramref name="npc"/> gives an NPC's loaded sheet and effects (null when its map data cannot be
    /// read now); <paramref name="place"/> where a waiting one stands. A waiting follower whose sheet can be read gets its missed steps
    /// first; one whose sheet cannot be read keeps the step as missed (Steamwoven; the Original setting never reaches it). An NPC
    /// without an admissible sheet is skipped. Returns the followers whose level rose and the waiting ones whose step was deferred.
    /// </summary>
    public static AreaFollowerLevelReport LevelFollowers(GameSessionOwner session, SessionExperienceAward? award,
        Func<string, (SessionNpcAdvancement Loaded, AdvancementModifiers Modifiers)?> npc, Func<string, AreaWaitingPlace> place)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(place);
        var raised = new List<SessionFollowerLevelResult>();
        var deferred = new List<string>();
        var applied = new List<SessionFollowerLevelResult>();
        if (award?.Followers is not { } step || session.AutoLevel is null) return new(raised.AsReadOnly(), deferred.AsReadOnly(), applied.AsReadOnly());
        var party = session.QueryParty();
        var rules = session.Rules.Experience;
        // An NPC's missed steps come before this one, as they came before it; when they cannot be applied the new step is not applied
        // out of order: a waiting follower keeps it with them (Steamwoven), a follower in the array skips it.
        bool Missed((SessionNpcAdvancement Loaded, AdvancementModifiers Modifiers) input)
        {
            var before = Level(session, input.Loaded.Entity, input.Loaded.Sheet.Level);
            var result = session.ApplyMissedFollowerLevels(session.Version, input.Loaded, input.Modifiers);
            if (result.Disposition == GameSessionDisposition.Changed && result.Record!.Sheet.Level != before) applied.Add(result);
            return result.Disposition != GameSessionDisposition.Rejected;
        }
        void Step((SessionNpcAdvancement Loaded, AdvancementModifiers Modifiers) input)
        {
            var result = session.LevelFollower(session.Version, input.Loaded, input.Modifiers, step);
            if (result.Disposition == GameSessionDisposition.Changed) raised.Add(result);
        }
        // The original's array loop has no death test (A5): a follower gets the step; a dead one never gets its missed steps.
        foreach (var entity in party.Followers.Select(f => f.Entity))
            if (npc(entity) is { } input && (session.QueryNpcDead(entity) || Missed(input))) Step(input);
        foreach (var entity in party.Others.Where(o => o.Standing == CompanionStanding.Waiting).Select(o => o.Entity))
        {
            var where = place(entity);
            if (!ExperienceRules.WaitingFollowerGetsStep(where.Offset, where.Dead, rules)) continue;
            if (npc(entity) is { } input && Missed(input)) Step(input);
            else if (!rules.WaitingFollowersStepOnlyNearPc
                && session.DeferFollowerLevel(session.Version, entity, step).Disposition == GameSessionDisposition.Changed) deferred.Add(entity);
        }
        return new(raised.AsReadOnly(), deferred.AsReadOnly(), applied.AsReadOnly());
    }

    /// <summary>
    /// ARC-WORLD-047: applies the missed level steps of every NPC the session holds them for whose sheet can be read now (its map
    /// loaded) and that is not dead, in ordinal entity order. Returns the results that changed the session.
    /// </summary>
    public static IReadOnlyList<SessionFollowerLevelResult> ApplyMissed(GameSessionOwner session,
        Func<string, (SessionNpcAdvancement Loaded, AdvancementModifiers Modifiers)?> npc)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(npc);
        var applied = new List<SessionFollowerLevelResult>();
        if (session.AutoLevel is null) return applied.AsReadOnly();
        foreach (var entity in session.QueryMissedLevelEntities())
        {
            if (session.QueryNpcDead(entity) || npc(entity) is not { } input) continue;
            var result = session.ApplyMissedFollowerLevels(session.Version, input.Loaded, input.Modifiers);
            if (result.Disposition == GameSessionDisposition.Changed) applied.Add(result);
        }
        return applied.AsReadOnly();
    }

    /// <summary>The follower's level now: the session's record when it levelled it, else its stored level.</summary>
    public static int Level(GameSessionOwner session, string entity, int stored)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.QueryNpcAdvancement(entity)?.Sheet.Level ?? stored;
    }

    private static bool AdvancementState(SessionNpcAdvancement loaded, AdvancementModifiers modifiers) =>
        AutoLevelRules.IsValid(loaded.Sheet) && AutoLevelRules.IsValid(modifiers) && loaded.AutoLevelScheme is >= 0 and <= SessionAdvancementLimits.MaxScheme;

    private static StatModifierOperator Operator(EffectChangeOperator op) => op switch
    {
        EffectChangeOperator.Add => StatModifierOperator.Add,
        EffectChangeOperator.Percent => StatModifierOperator.Percent,
        EffectChangeOperator.Minimum => StatModifierOperator.Minimum,
        EffectChangeOperator.Maximum => StatModifierOperator.Maximum,
        EffectChangeOperator.Multiply => StatModifierOperator.Multiply,
        EffectChangeOperator.Divide => StatModifierOperator.Divide,
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    private static CharacterStat? Stat(int attribute) => attribute switch
    {
        >= 0 and <= 7 => (CharacterStat)attribute,
        22 => CharacterStat.MagickPoints,
        23 => CharacterStat.TechPoints,
        _ => null,
    };
}

/// <summary>Where a waiting follower stands for the level step (ARC-WORLD-047): whether it is dead, and its tile offset from the PC when it
/// stands among the loaded objects (drawn on the current map), else null.</summary>
public readonly record struct AreaWaitingPlace(bool Dead, (int X, int Y)? Offset);

/// <summary>An award's follower step: the followers whose level rose by it, the waiting followers whose step was kept as missed, and
/// the missed steps applied first that raised a level.</summary>
public sealed record AreaFollowerLevelReport(IReadOnlyList<SessionFollowerLevelResult> Raised, IReadOnlyList<string> Deferred,
    IReadOnlyList<SessionFollowerLevelResult> MissedApplied);

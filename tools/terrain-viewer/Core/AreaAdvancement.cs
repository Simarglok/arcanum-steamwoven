using System.Collections.Immutable;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Party;

namespace Arcanum.TerrainViewer.Core;

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
    /// and poison from the 28 base stats, skills, colleges and degrees, hit and fatigue points and adjustments, the scheme and the
    /// monster flag; its effects' stat, skill and maxima changes through <paramref name="effect"/> (null for a number without a rule,
    /// which changes nothing). Null when the fields are not a critter's or give no admissible sheet.
    /// </summary>
    public static (SessionNpcAdvancement Loaded, AdvancementModifiers Modifiers)? Npc(string entity, CombatFields fields, Func<int, EffectRule?> effect)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(effect);
        if (fields.Critter is not { } critter || critter.BaseStats.Count <= RaceStat || critter.SpellTech.Count < MasteryIndex + 1 + AutoLevelRules.TechDisciplines ||
            critter.BaseStats[RaceStat] is < 0 or > 10) return null;
        var stats = critter.BaseStats;
        var sheet = new AdvancementSheet((CharacterRace)stats[RaceStat], (critter.Flags & MonsterFlag) != 0, [.. stats.Take(AutoLevelRules.PrimaryStats)],
            stats[MagickStat], stats[TechStat], stats[LevelStat], stats[UnspentStat], [.. critter.BasicSkills], [.. critter.TechSkills],
            [.. critter.SpellTech.Take(AutoLevelRules.Colleges)], [.. critter.SpellTech.Skip(MasteryIndex + 1).Take(AutoLevelRules.TechDisciplines)],
            fields.Object.HitPoints, fields.Object.HitPointAdjustment, critter.FatiguePoints, critter.FatigueAdjustment);
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
    /// Passes an award's follower step to every follower of the hero and every NPC waiting for it, as the source does after the
    /// hero's level step (the waiting ones near the hero: here the ones on the current map, whose fields <paramref name="npc"/> gives).
    /// Returns the followers whose level rose; an NPC without fields or an admissible sheet is skipped.
    /// </summary>
    public static IReadOnlyList<SessionFollowerLevelResult> LevelFollowers(GameSessionOwner session, SessionExperienceAward? award,
        Func<string, (SessionNpcAdvancement Loaded, AdvancementModifiers Modifiers)?> npc)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(npc);
        var raised = new List<SessionFollowerLevelResult>();
        if (award?.Followers is not { } step || session.AutoLevel is null) return raised;
        var party = session.QueryParty();
        var entities = party.Followers.Select(f => f.Entity).Concat(party.Others.Where(o => o.Standing == CompanionStanding.Waiting).Select(o => o.Entity));
        foreach (var entity in entities)
        {
            if (npc(entity) is not { } input) continue;
            var result = session.LevelFollower(session.Version, input.Loaded, input.Modifiers, step);
            if (result.Disposition == GameSessionDisposition.Changed) raised.Add(result);
        }
        return raised.AsReadOnly();
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
        _ => StatModifierOperator.Maximum,
    };

    private static CharacterStat? Stat(int attribute) => attribute switch
    {
        >= 0 and <= 7 => (CharacterStat)attribute,
        22 => CharacterStat.MagickPoints,
        23 => CharacterStat.TechPoints,
        _ => null,
    };
}

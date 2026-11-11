using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation;

namespace Arcanum.World;

/// <summary>Why the sheet does not open: no hero in the session (the diagnostic PC profile), or a dead hero (the original
/// refuses the sheet for a dead PC, 0x5597C0).</summary>
public enum AreaSheetRefusal { None, NoHero, Dead }

/// <summary>How a primary stat is drawn in the original sheet: raised (green) or lowered (red) when its base value adjusted by
/// the effects that are not innate (race, background, class, gender) differs from the base value; otherwise plain.</summary>
public enum AreaSheetMark { None, Raised, Lowered }

/// <summary>One named value of the sheet. <see cref="Mark"/> is set only for the primary stats.</summary>
public sealed record AreaSheetValue(string Label, int Value, AreaSheetMark Mark = AreaSheetMark.None);

/// <summary>
/// ARC-UI-007: what the original character sheet of the PC (<c>charedit</c>, active mode) shows, read from the game session.
/// <see cref="ToNextLevel"/> is null when the session has no experience data. <see cref="Alignment"/> is the stored alignment
/// divided by ten (truncated), as the sheet prints it. <see cref="MagickAptitude"/> and <see cref="TechAptitude"/> split the
/// magick/tech aptitude: a positive value is magick, a negative one tech, the other side 0. <see cref="Stats"/> are the eight
/// primary stats in the sheet's order (strength, constitution, dexterity, beauty; intelligence, willpower, perception,
/// charisma) at their effective value; <see cref="Secondary"/> the eight derived stats and <see cref="Resistances"/> the five
/// resistances in the sheet's order.
/// </summary>
public sealed record AreaCharacterSheetView(string Name, CharacterRace Race, CharacterGender Gender, int Level, int Experience,
    int? ToNextLevel, int UnspentPoints, int Alignment, int MagickAptitude, int TechAptitude, int HitPoints, int MaxHitPoints,
    int Fatigue, int MaxFatigue, IReadOnlyList<AreaSheetValue> Stats, IReadOnlyList<AreaSheetValue> Secondary,
    IReadOnlyList<AreaSheetValue> Resistances)
{
    /// <summary>ARC-UI-088: false for a monstrous NPC's sheet, which shows neither race nor gender.</summary>
    public bool ShowRaceGender { get; init; } = true;
}

/// <summary>A sheet or the reason it does not open.</summary>
public sealed record AreaCharacterSheetBuild(AreaSheetRefusal Refusal, AreaCharacterSheetView? View);

/// <summary>
/// ARC-UI-007: the character sheet of the session's hero, Godot-free. Every value is a session query; the sheet changes
/// nothing. The rules of what the sheet shows were checked in the original executable (ledger section "Character sheet"):
/// the effective value of each primary stat with its effect mark, the derived stats at their effective value, each
/// resistance as 0x43D6D0 computes it for display, hit points and fatigue against their maxima, level,
/// experience to the next level (<see cref="ExperienceRules.ToNextLevel"/>), the unspent points, alignment and aptitude.
/// Resistances are partial: the session models only worn armour and the constitution bonus to poison (no base resistance
/// field, effect changes of resistances are inert in the session, every worn item counts as identified).
/// </summary>
public static class AreaCharacterSheet
{
    /// <summary>The resistance clamp of 0x43D6D0 for a PC.</summary>
    public const int MaxResistance = 95;

    private static readonly CharacterStat[] PrimaryOrder =
    [
        CharacterStat.Strength, CharacterStat.Constitution, CharacterStat.Dexterity, CharacterStat.Beauty,
        CharacterStat.Intelligence, CharacterStat.Willpower, CharacterStat.Perception, CharacterStat.Charisma,
    ];

    private static readonly CharacterStat[] SecondaryOrder =
    [
        CharacterStat.CarryWeight, CharacterStat.DamageBonus, CharacterStat.AcAdjustment, CharacterStat.Speed,
        CharacterStat.HealRate, CharacterStat.PoisonRecovery, CharacterStat.ReactionModifier, CharacterStat.MaxFollowers,
    ];

    /// <summary>Whether the sheet opens for this session, without building it.</summary>
    public static AreaSheetRefusal Refusal(GameSessionOwner session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.QueryHero() is null) return AreaSheetRefusal.NoHero;
        return session.QueryHeroLife()!.Status.IsDead ? AreaSheetRefusal.Dead : AreaSheetRefusal.None;
    }

    /// <summary>The hero's sheet, or the refusal.</summary>
    [OriginalFunction(0x55A240, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x55B1B0, OriginalFunctionScope.Partial)]
    public static AreaCharacterSheetBuild Build(GameSessionOwner session)
    {
        var refusal = Refusal(session);
        if (refusal != AreaSheetRefusal.None) return new(refusal, null);
        var hero = session.QueryHero()!;
        var life = session.QueryHeroLife()!;
        var state = life.Record.State;
        // The effective read of the source: poison lowers strength and dexterity, both crippled legs lower speed.
        var query = session.QueryHeroStats(new(0, state.PoisonLevel, state.Conditions.HasFlag(LifeConditions.CrippledLegsBoth)))!.Value;
        var stats = query.Snapshot ?? throw new InvalidOperationException("AREA_SHEET_STATS_" + query.Failure);
        var experience = session.QueryHeroExperience()!;
        var aptitude = stats.GetEffective(CharacterStat.MagickTechAptitude);
        var primary = PrimaryOrder.Select(stat => new AreaSheetValue(Label(stat), stats.GetEffective(stat), Mark(stats, stat))).ToArray();
        var secondary = SecondaryOrder.Select(stat => new AreaSheetValue(Label(stat), stats.GetEffective(stat))).ToArray();
        var worn = session.QueryHeroEquipmentCombat()!.Resistances;
        // object_get_resistance (0x43D6D0, ledger EQ8): the constitution bonus to poison counts only when positive.
        var poisonBonus = Math.Max(0, 5 * (stats.GetEffective(CharacterStat.Constitution) - 4));
        AreaSheetValue Resistance(string label, int value) => new(label, Math.Clamp(value, 0, MaxResistance));
        var resistances = new[]
        {
            Resistance("Normal", worn.Normal), Resistance("Magick", worn.Magic), Resistance("Fire", worn.Fire),
            Resistance("Poison", worn.Poison + poisonBonus), Resistance("Electrical", worn.Electrical),
        };
        return new(AreaSheetRefusal.None, new(AreaStartingHero.Name(hero), hero.Race, hero.Gender, experience.Level, experience.Experience,
            experience.ToNextLevel, experience.UnspentPoints, hero.Alignment / 10, Math.Max(0, aptitude), Math.Max(0, -aptitude),
            life.Status.HitPoints, life.Status.MaxHitPoints, life.Status.Fatigue, life.Status.MaxFatigue, primary, secondary, resistances));
    }

    /// <summary>The original's colour rule (0x55B2A0): the base value adjusted by the effects that
    /// are not innate (0x4EA930, the aggregation of ledger EF6 without race, background, class
    /// and gender effects) compared with the base value.</summary>
    [OriginalFunction(0x55B2A0, OriginalFunctionScope.Partial)]
    internal static AreaSheetMark Mark(CharacterStatsSnapshot stats, CharacterStat stat)
    {
        var value = stats.GetBase(stat);
        var adjusted = EffectModifiers.Apply(value, stats.AppliedEffects
            .Where(effect => effect.Cause is not (EffectCause.Race or EffectCause.Background or EffectCause.Class or EffectCause.Gender))
            .SelectMany(effect => effect.Definition.Modifiers).Where(modifier => modifier.Stat == stat)
            .Select(modifier => new EffectModifier(modifier.Operator, modifier.Value)).ToArray());
        return adjusted > value ? AreaSheetMark.Raised : adjusted < value ? AreaSheetMark.Lowered : AreaSheetMark.None;
    }

    /// <summary>The window's text: project labels only (no original UI text), one line per row of the sheet.</summary>
    public static IReadOnlyList<string> Lines(AreaCharacterSheetView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        static string Value(AreaSheetValue value) => $"{value.Label} {value.Value}" + value.Mark switch
        {
            AreaSheetMark.Raised => " (raised by effects)", AreaSheetMark.Lowered => " (lowered by effects)", _ => string.Empty,
        };
        var next = view.ToNextLevel is { } toNext
            ? toNext == ExperienceRules.NoNextLevel ? "last level" : $"{toNext} to the next level"
            : "next level unknown (no experience data)";
        return
        [
            $"{view.Name} | {RaceName(view.Race)}, {(view.Gender == CharacterGender.Female ? "female" : "male")}",
            $"Level {view.Level} | experience {view.Experience}, {next} | unspent points {view.UnspentPoints}",
            $"Alignment {view.Alignment} | magick aptitude {view.MagickAptitude}, tech aptitude {view.TechAptitude}",
            $"Hit points {view.HitPoints}/{view.MaxHitPoints} | fatigue {view.Fatigue}/{view.MaxFatigue}",
            string.Join(" | ", view.Stats.Take(4).Select(Value)),
            string.Join(" | ", view.Stats.Skip(4).Select(Value)),
            string.Join(" | ", view.Secondary.Take(4).Select(Value)),
            string.Join(" | ", view.Secondary.Skip(4).Select(Value)),
            "Resistances " + string.Join(", ", view.Resistances.Select(r => $"{r.Label.ToLowerInvariant()} {r.Value}%"))
                + " (worn armour and constitution only; effect changes NOT modelled)",
        ];
    }

    internal static string Label(CharacterStat stat) => stat switch
    {
        CharacterStat.CarryWeight => "Carry weight", CharacterStat.DamageBonus => "Damage bonus", CharacterStat.AcAdjustment => "AC adjustment",
        CharacterStat.HealRate => "Heal rate", CharacterStat.PoisonRecovery => "Poison recovery", CharacterStat.ReactionModifier => "Reaction modifier",
        CharacterStat.MaxFollowers => "Max followers", _ => stat.ToString(),
    };

    /// <summary>The project's race name (no original text).</summary>
    public static string RaceName(CharacterRace race) => race switch
    {
        CharacterRace.HalfElf => "half-elf", CharacterRace.HalfOrc => "half-orc", CharacterRace.HalfOgre => "half-ogre",
        CharacterRace.DarkElf => "dark elf", _ => race.ToString().ToLowerInvariant(),
    };
}

using System.Collections.Immutable;

namespace Arcanum.Simulation.Character;

/// <summary>
/// ARC-TECHNOLOGY-002: a critter's tech degrees as the retail 1.0.7.4 executable applies them (contract
/// docs/world/tech-degrees-runtime.md, ledger "Tech degrees"): the stored degree of a discipline, raising and lowering one
/// degree, and the effective expertise a degree gives. The degree, cost and intelligence tables are the auto-level rules'
/// (<see cref="AutoLevelRules"/>); the expertise table is <see cref="ExpertisePercent"/>. Pure functions over a sheet; the
/// session holds the sheet and the editor's floor.
/// </summary>
public static class TechDegreeRules
{
    public const int Disciplines = AutoLevelRules.TechDisciplines, MaxDegree = AutoLevelRules.MaxDegree;

    private static readonly int[] Expertise = [0, 10, 20, 35, 50, 65, 80, 100];

    /// <summary>The expertise percentage of a degree (table 0x5B5144): 0, 10, 20, 35, 50, 65, 80, 100.</summary>
    public static int ExpertisePercent(int degree) => degree is >= 0 and <= MaxDegree ? Expertise[degree]
        : throw new ArgumentOutOfRangeException(nameof(degree));

    /// <summary>The stored degree of <paramref name="discipline"/>: a PC's or NPC's sheet value, 0 for any other object.</summary>
    [OriginalFunction(0x4AFE60, OriginalFunctionScope.Full)]
    public static int Degree(AdvancementSheet? critter, int discipline)
    {
        CheckDiscipline(discipline);
        return critter is null ? 0 : critter.TechDegrees[discipline];
    }

    /// <summary>
    /// One degree up (0x4AFEC0, the auto-level rules' purchase): refused at degree 7 (<see cref="PointChangeStatus.AtMaximum"/>) and
    /// when the <em>effective</em> intelligence is below the next degree's minimum (<see cref="PointChangeStatus.IntelligenceTooLow"/>);
    /// otherwise the degree rises by one and the base tech points by its cost. <see cref="PointLaneStep.Points"/> is the character
    /// point the editor spends for it (one per degree). The unspent points are the caller's.
    /// </summary>
    public static PointLaneStep Raise(AdvancementSheet sheet, AdvancementModifiers modifiers, int discipline)
    {
        if (!Admit(sheet, modifiers, discipline)) return new(PointChangeStatus.Invalid, sheet, 0);
        var degree = sheet.TechDegrees[discipline];
        if (degree >= MaxDegree) return new(PointChangeStatus.AtMaximum, sheet, 0);
        var work = new AutoLevelRules.Work(sheet, modifiers, NoWillpower);
        if (AutoLevelRules.DegreeMinimumIntelligence(degree + 1) > work.Effective(CharacterStat.Intelligence))
            return new(PointChangeStatus.IntelligenceTooLow, sheet, 0);
        work.RaiseDegree(discipline);
        return new(PointChangeStatus.Changed, work.Sheet() with { UnspentPoints = sheet.UnspentPoints }, AutoLevelRules.DegreeCost(degree + 1));
    }

    /// <summary>
    /// One degree down (0x4AFFF0): refused at degree 0 (<see cref="PointChangeStatus.AtMinimum"/>); otherwise the degree falls by one
    /// and the base tech points by its cost, with no intelligence check. <see cref="PointLaneStep.Points"/> is the character point
    /// the editor refunds. The editor's floor and the unspent points are the caller's.
    /// </summary>
    public static PointLaneStep Lower(AdvancementSheet sheet, AdvancementModifiers modifiers, int discipline)
    {
        if (!Admit(sheet, modifiers, discipline)) return new(PointChangeStatus.Invalid, sheet, 0);
        var degree = sheet.TechDegrees[discipline];
        if (degree == 0) return new(PointChangeStatus.AtMinimum, sheet, 0);
        var work = new AutoLevelRules.Work(sheet, modifiers, NoWillpower);
        work.LowerDegree(discipline);
        return new(PointChangeStatus.Changed, work.Sheet() with { UnspentPoints = sheet.UnspentPoints }, AutoLevelRules.DegreeCost(degree));
    }

    /// <summary>
    /// The effective expertise in a discipline (0x4B00B0): the stored degree lowered while the effective intelligence is below its
    /// minimum, then that degree's <see cref="ExpertisePercent"/> through the "expertise in discipline" effect changes
    /// (<paramref name="effects"/>: percent, multiply, divide, add, minimum, maximum of every cause, <see cref="EffectModifiers.Apply"/>),
    /// with no clamp: an effect can lift it above 100 or below 0.
    /// </summary>
    [OriginalFunction(0x4B00B0, OriginalFunctionScope.Full)]
    public static int EffectiveExpertise(int degree, int effectiveIntelligence, IReadOnlyList<EffectModifier> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        return EffectModifiers.Apply(ExpertisePercent(CountingDegree(degree, effectiveIntelligence)), effects);
    }

    /// <summary>The degree the intelligence lets count (<see cref="EffectiveExpertise"/>'s loop): the stored degree, lowered while
    /// the effective intelligence is below its minimum.</summary>
    public static int CountingDegree(int degree, int effectiveIntelligence)
    {
        if (degree is < 0 or > MaxDegree) throw new ArgumentOutOfRangeException(nameof(degree));
        while (degree > 0 && effectiveIntelligence < AutoLevelRules.DegreeMinimumIntelligence(degree)) degree--;
        return degree;
    }

    /// <summary>The technology lane of the shared point pool (<see cref="CharacterPointRules"/>): <see cref="Raise"/> and
    /// <see cref="Lower"/>; the pool checks the points and the editor's floor (the sell wrapper of the original has none, TC10).</summary>
    public static IPointLane Lane { get; } = new DegreeLane();

    private sealed class DegreeLane : IPointLane
    {
        // The editor's buy wrapper 0x589A20: the cost of the next degree against the unspent points first, then the raise; the pool
        // spends the point. At degree 7 the original reads the cost past its table (the next table's 0), passes and the raise
        // refuses, so a degree at the cap is "at maximum" whatever the points.
        [OriginalFunction(0x589A20, OriginalFunctionScope.Partial)]
        public PointLaneStep Raise(AdvancementSheet sheet, AdvancementModifiers modifiers, int index)
        {
            if (Admit(sheet, modifiers, index) && sheet.TechDegrees[index] < MaxDegree &&
                AutoLevelRules.DegreeCost(sheet.TechDegrees[index] + 1) > sheet.UnspentPoints) return new(PointChangeStatus.NotEnoughPoints, sheet, 0);
            return TechDegreeRules.Raise(sheet, modifiers, index);
        }

        // The editor's sell wrapper 0x589AF0 refunds the point; the pool adds the floor of owner decision TC10.
        [OriginalFunction(0x589AF0, OriginalFunctionScope.Partial)]
        public PointLaneStep Lower(AdvancementSheet sheet, AdvancementModifiers modifiers, int index) => TechDegreeRules.Lower(sheet, modifiers, index);
    }

    // The degree rules read no spell table.
    private static readonly ImmutableArray<int> NoWillpower = [.. new int[AutoLevelRules.Spells]];

    private static bool Admit(AdvancementSheet sheet, AdvancementModifiers modifiers, int discipline) =>
        sheet is not null && modifiers is not null && AutoLevelRules.IsValid(sheet) && AutoLevelRules.IsValid(modifiers) &&
        discipline is >= 0 and < Disciplines;

    private static void CheckDiscipline(int discipline)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(discipline);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(discipline, Disciplines);
    }
}

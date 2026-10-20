using System.Collections.Immutable;
using Arcanum.Simulation.Character;

namespace Arcanum.Simulation.Magic;

/// <summary>
/// ARC-MAGIC-004: the spell ranks' lane of the shared point pool (<see cref="CharacterPointRules"/>, track
/// <see cref="AdvancementTrack.College"/>), as the character editor learns and unlearns (0x57C540, 0x57C670): a step up learns the
/// college's next rank through <see cref="SpellBookRules.Learn"/> (unforced, with the sheet's level and its effective intelligence
/// and willpower) and costs <see cref="SpellCostRules.RankPointCost"/>; a step down unlearns the highest rank and refunds the
/// same. The magick points move by one, clamped to 0..210 as the stat setter writes them. The pool tests the points first and
/// keeps the editor's floor.
/// </summary>
public sealed class SpellRankLane : IPointLane
{
    private readonly ImmutableArray<int> _willpower;

    /// <param name="spellWillpower">The willpower each of the 80 ranks needs (the spell records', or the defaults).</param>
    public SpellRankLane(ImmutableArray<int> spellWillpower)
    {
        if (spellWillpower.IsDefault || spellWillpower.Length != SpellBookRules.Spells)
            throw new ArgumentException("80 willpower minima expected.", nameof(spellWillpower));
        _willpower = spellWillpower;
    }

    public int Cost(AdvancementSheet sheet, int index)
    {
        if (!AutoLevelRules.IsValid(sheet)) throw new ArgumentException("Invalid advancement sheet.", nameof(sheet));
        if (index is < 0 or >= SpellBookRules.Colleges) throw new ArgumentOutOfRangeException(nameof(index));
        return SpellCostRules.RankPointCost(Next(sheet, index));
    }

    [OriginalFunction(0x57C540, OriginalFunctionScope.Partial)]
    public PointLaneStep Raise(AdvancementSheet sheet, AdvancementModifiers modifiers, int index)
    {
        if (!Admit(sheet, modifiers, index)) return new(PointChangeStatus.Invalid, sheet, 0);
        if (sheet.Colleges[index] >= SpellBookRules.Ranks) return new(PointChangeStatus.AtMaximum, sheet, 0);
        var spell = Next(sheet, index);
        var work = new AutoLevelRules.Work(sheet, modifiers, _willpower);
        var change = SpellBookRules.Learn(Book(sheet), spell, force: false, sheet.Level, work.Effective(CharacterStat.Intelligence),
            work.Effective(CharacterStat.Willpower), _willpower[spell]);
        return change.Status switch
        {
            SpellBookStatus.Done => Step(sheet, change, SpellCostRules.RankPointCost(spell)),
            SpellBookStatus.LevelTooLow => new(PointChangeStatus.LevelTooLow, sheet, 0),
            SpellBookStatus.IntelligenceTooLow => new(PointChangeStatus.StatTooLow, sheet, 0, CharacterStat.Intelligence),
            SpellBookStatus.WillpowerTooLow => new(PointChangeStatus.StatTooLow, sheet, 0, CharacterStat.Willpower),
            _ => new(PointChangeStatus.Invalid, sheet, 0),
        };
    }

    [OriginalFunction(0x57C670, OriginalFunctionScope.Partial)]
    public PointLaneStep Lower(AdvancementSheet sheet, AdvancementModifiers modifiers, int index)
    {
        if (!Admit(sheet, modifiers, index)) return new(PointChangeStatus.Invalid, sheet, 0);
        var rank = sheet.Colleges[index];
        if (rank == 0) return new(PointChangeStatus.AtMinimum, sheet, 0);
        var spell = index * SpellBookRules.Ranks + rank - 1;
        var change = SpellBookRules.Unlearn(Book(sheet), spell);
        return change.Succeeded ? Step(sheet, change, SpellCostRules.RankPointCost(spell)) : new(PointChangeStatus.Invalid, sheet, 0);
    }

    /// <summary>The book a sheet holds: its college ranks and its mastered college.</summary>
    public static SpellBook Book(AdvancementSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return new(sheet.Colleges, sheet.MasteredCollege);
    }

    private static bool Admit(AdvancementSheet sheet, AdvancementModifiers modifiers, int index) =>
        AutoLevelRules.IsValid(sheet) && AutoLevelRules.IsValid(modifiers) && index is >= 0 and < SpellBookRules.Colleges;

    // The next rank's spell number; a college with all five ranks names its last (the step refuses it).
    private static int Next(AdvancementSheet sheet, int index) =>
        index * SpellBookRules.Ranks + Math.Min(sheet.Colleges[index], SpellBookRules.Ranks - 1);

    private static PointLaneStep Step(AdvancementSheet sheet, SpellBookChange change, int points)
    {
        var range = CharacterStatRules.Range(sheet.Race, CharacterStat.MagickPoints);
        return new(PointChangeStatus.Changed, sheet with
        {
            Colleges = change.Book.Ranks, MagickPoints = Math.Clamp(sheet.MagickPoints + change.MagickPoints, range.Minimum, range.Maximum),
        }, points);
    }
}

using System.Globalization;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Skills;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One + or − press on the tech page: whether it changed the hero and the window's message.</summary>
public sealed record AreaTechPress(bool Changed, string Message, SessionPointResult Result);

/// <summary>Which element of a page the pointer is over, and its message id (<see cref="Hover"/>).</summary>
public enum AreaPageHit { None, GroupTab, SkillRow, SkillPlus, SkillMinus, DisciplineTab, TechHeader, TechDegree, TechPlus, TechMinus }

/// <summary>ARC-UI-076: the tech page's model and its + and − presses, Godot-free. The rules are the technology lane's (ledger TD3, TD4, TD7,
/// TD8, CP2): the pool checks the points, the intelligence and, for the original's buy, nothing at all for a sell (the editor's snapshot
/// only hides the − button).</summary>
public static class AreaTechPage
{
    public static AreaTechPageView? Build(GameSessionOwner session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.QueryHeroTechnology() is not { } technology) return null;
        var floor = session.QueryHeroEditorFloor();
        var rows = technology.Select(discipline => new AreaTechRow(discipline.Discipline, discipline.Degree, discipline.CountingDegree, discipline.Expertise,
            floor?.TechDegrees[discipline.Discipline] ?? discipline.Degree)).ToArray();
        return new(rows, floor is not null);
    }

    public static AreaTechPress Spend(GameSessionOwner session, int discipline)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Press(session.SpendHeroPoint(session.Version, AdvancementTrack.TechDiscipline, discipline), true);
    }

    public static AreaTechPress Refund(GameSessionOwner session, int discipline)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Press(session.RefundHeroPoint(session.Version, AdvancementTrack.TechDiscipline, discipline, AreaSkillsPage.RefundClick), false);
    }

    private static AreaTechPress Press(SessionPointResult result, bool spend)
    {
        if (result.Disposition == GameSessionDisposition.Changed)
            return new(true, spend ? $"spent {result.Change!.Points} point(s)" : $"refunded {result.Change!.Points} point(s)", result);
        // Project texts for the refusals; the original shows charedit.mes 18 and 26 (TD7).
        var message = result.Failure == GameSessionFailure.PointRefused ? result.Change!.Status switch
        {
            PointChangeStatus.NotEnoughPoints => "not enough unspent points",
            PointChangeStatus.AtMaximum => "the discipline is at its highest degree",
            PointChangeStatus.IntelligenceTooLow => "intelligence too low for the next degree",
            PointChangeStatus.AtMinimum => "no degree to give back",
            var status => status.ToString(),
        } : result.Failure.ToString();
        return new(false, "refused: " + message, result);
    }

    /// <summary>The text bar's lines of a degree (type 11, 0x55B06F): the degree's name, the minimum intelligence line
    /// (<c>"%s: %d"</c> with intgame.mes 17), then its description.</summary>
    public static string DegreeHint(AreaCharacterSheetTexts texts, int discipline, int degree)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var minimum = string.Format(CultureInfo.InvariantCulture, "{0}: {1}", texts.Interface(17, "Minimum IN"), AutoLevelRules.DegreeMinimumIntelligence(degree));
        return string.Join("\n", new[] { texts.TechDegreeName(degree), minimum, texts.TechDegreeDescription(discipline, degree) }.Where(line => !string.IsNullOrEmpty(line)));
    }

    /// <summary>The text bar's lines of a discipline (type 10, 0x55B03B): its name and its description.</summary>
    public static string DisciplineHint(AreaCharacterSheetTexts texts, int discipline)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return string.Join("\n", new[] { texts.TechDisciplineName(discipline), texts.TechDisciplineDescription(discipline) }.Where(line => !string.IsNullOrEmpty(line)));
    }
}

/// <summary>ARC-UI-076: the skills page's hover, Godot-free (type 7, 0x55AF17..0x55AFB2): the skill's name and description, and unless 0 the
/// key stat's name (intgame.mes 28 + stat) with the minimum key stat of the next point on the right.</summary>
public static class AreaSkillsHover
{
    public const int KeyStatNameBase = 28;

    public static AreaMessageEntry Hint(AreaSkillRow row, AreaCharacterSheetTexts texts)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(texts);
        var value = row.NextPointKeyStat == 0 ? string.Empty
            : string.Format(CultureInfo.InvariantCulture, "{0}: {1}", texts.Interface(KeyStatNameBase + (int)row.KeyStat, row.KeyStat.ToString()), row.NextPointKeyStat);
        return new(AreaMessageKind.SkillHint, row.Name, Detail: row.Description ?? string.Empty) { Value = value };
    }
}

using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;

namespace Arcanum.TerrainViewer.Core;

public enum AreaSkillTextsStatus { Ready, ModuleOutsideGameRoot, MountFailed, StackFailed, TextsFailed }

/// <summary>The skill texts for the area's game folder, or why none were loaded.</summary>
public sealed record AreaSkillTextsLoad(AreaSkillTextsStatus Status, SkillTexts? Texts, string? Detail);

/// <summary>
/// One row of the skills page: the skill (0..11 basic, 12..15 tech, the original's page order), its shown name and training
/// name (the player's <c>mes\skill.mes</c>, else the project's label), its description (null without texts), the stored points,
/// the effective level (the original's bar, level / 20), the training, the key stat, the minimum key stat the next point needs
/// (0 when no stat value allows it, and the original then omits the line) and the points the editor's floor holds (the − button
/// refuses at it).
/// </summary>
public sealed record AreaSkillRow(int Index, Skill Skill, string Name, string? TrainingName, string? Description, int Points, int Level,
    SkillTraining Training, CharacterStat KeyStat, int NextPointKeyStat, int FloorPoints);

/// <summary>The skills page of the hero: the sixteen rows in the original's four groups of four, and the unspent points.</summary>
public sealed record AreaSkillsPageView(IReadOnlyList<AreaSkillRow> Rows, int UnspentPoints, bool EditorOpen);

/// <summary>One + or − press: whether it changed the hero and the window's message.</summary>
public sealed record AreaSkillPress(bool Changed, string Message, SessionPointResult Result);

/// <summary>
/// ARC-UI-009 (skills plan S6): the skills page of the AREA viewer's character sheet, Godot-free. What the original page shows
/// was read in the retail 1.0.7.4 executable (ledger "Skills page (ARC-UI-009)", SP1–SP8): four groups of four skills (three of
/// basic skills, the fourth the tech skills), each row the skill's name with its training name in brackets (none untrained), a
/// bar of the effective level out of 20, + and − buttons, and on hover the name, the description and the key stat the next
/// point needs (<see cref="SkillRules.MinimumKeyStat"/> of 4 × points + 4). + and − go through the session's point pool
/// (<see cref="GameSessionOwner.SpendHeroPoint"/>, <see cref="GameSessionOwner.RefundHeroPoint"/>, ARC-WORLD-052) with the
/// editor the sheet opens; − uses the original's click (<see cref="SkillRefundClick.UntilLevelChanges"/>) and stops at the floor.
/// </summary>
public static class AreaSkillsPage
{
    public const int Rows = SkillTexts.BasicSkills + SkillTexts.TechSkills, RowsPerGroup = 4;
    /// <summary>The page's refund click: the original's (the owner has not chosen the alternative, skills plan S5).</summary>
    public const SkillRefundClick RefundClick = SkillRefundClick.UntilLevelChanges;

    /// <summary>The project's names of the four groups (the original's tabs carry art, no text).</summary>
    public static readonly IReadOnlyList<string> GroupLabels = ["Combat", "Thieves", "Social", "Technological"];

    private static readonly string[] TrainingLabels = ["", "Apprentice", "Expert", "Master"];

    /// <summary>Loads <c>mes\skill.mes</c> through the game folder's content stack of the module (the rule of the description
    /// tables, <see cref="AreaContentRoot.Discover"/>); otherwise no texts are loaded and the page shows the project's labels.</summary>
    public static AreaSkillTextsLoad LoadTexts(string modulePath)
    {
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready)
            return new(found.Status == AreaContentRootStatus.MountFailed ? AreaSkillTextsStatus.MountFailed : AreaSkillTextsStatus.ModuleOutsideGameRoot, null, found.Detail);
        var opened = ContentStack.Open(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return new(AreaSkillTextsStatus.StackFailed, null, opened.Status.ToString());
        using var stack = opened.Stack!;
        var loaded = SkillTexts.Load(stack);
        return loaded.Status != SkillTextStatus.Ready
            ? new(AreaSkillTextsStatus.TextsFailed, null, $"{loaded.Status} {loaded.Read} {loaded.Parse?.Status}")
            : new(AreaSkillTextsStatus.Ready, loaded.Texts, "skill texts loaded");
    }

    /// <summary>The track and index of a page row: 0..11 a basic skill, 12..15 a tech skill.</summary>
    public static (AdvancementTrack Track, int Index) Track(int row) => row switch
    {
        >= 0 and < SkillTexts.BasicSkills => (AdvancementTrack.BasicSkill, row),
        >= SkillTexts.BasicSkills and < Rows => (AdvancementTrack.TechSkill, row - SkillTexts.BasicSkills),
        _ => throw new ArgumentOutOfRangeException(nameof(row)),
    };

    /// <summary>The page of the session's hero, or null without a hero.</summary>
    public static AreaSkillsPageView? Build(GameSessionOwner session, SkillTexts? texts, MesTextProfile profile)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.QueryHeroSkills() is not { } skills || session.QueryHeroExperience() is not { } experience) return null;
        var floor = session.QueryHeroEditorFloor();
        string? Text(MesMessage? message) =>
            message is not null && MesText.Decode(message, profile) is { Status: MesDecodeStatus.Decoded } decoded ? decoded.Text : null;
        var rows = new AreaSkillRow[Rows];
        for (var row = 0; row < Rows; row++)
        {
            var (track, index) = Track(row);
            var tech = track == AdvancementTrack.TechSkill;
            var skill = skills.All[row];
            var trainingName = skill.Training == SkillTraining.None ? null
                : Text(texts?.TrainingName((int)skill.Training)) ?? TrainingLabels[(int)skill.Training];
            var floorPoints = floor is null ? 0 : SkillRules.Points(tech ? floor.TechSkills[index] : floor.BasicSkills[index]);
            rows[row] = new(row, skill.Skill, Text(tech ? texts?.TechName(index) : texts?.BasicName(index)) ?? Label(skill.Skill), trainingName,
                Text(tech ? texts?.TechDescription(index) : texts?.BasicDescription(index)), skill.Points, skill.Level, skill.Training,
                SkillRules.KeyStat(skill.Skill), SkillRules.MinimumKeyStat(SkillRules.LevelPerPoint * skill.Points + SkillRules.LevelPerPoint), floorPoints);
        }
        return new(rows, experience.UnspentPoints, floor is not null);
    }

    /// <summary>+ on a row: one point through the session's pool.</summary>
    public static AreaSkillPress Spend(GameSessionOwner session, int row)
    {
        ArgumentNullException.ThrowIfNull(session);
        var (track, index) = Track(row);
        return Press(session.SpendHeroPoint(session.Version, track, index), true);
    }

    /// <summary>− on a row: the original's click through the session's pool, down to the editor's floor.</summary>
    public static AreaSkillPress Refund(GameSessionOwner session, int row)
    {
        ArgumentNullException.ThrowIfNull(session);
        var (track, index) = Track(row);
        return Press(session.RefundHeroPoint(session.Version, track, index, RefundClick), false);
    }

    private static AreaSkillPress Press(SessionPointResult result, bool spend)
    {
        if (result.Disposition == GameSessionDisposition.Changed)
            return new(true, spend ? $"spent {result.Change!.Points} point(s)" : $"refunded {result.Change!.Points} point(s)", result);
        // Project texts for the refusals; the original shows its charedit.mes messages (SP7).
        var message = result.Failure == GameSessionFailure.PointRefused ? result.Change!.Status switch
        {
            PointChangeStatus.NotEnoughPoints => "not enough unspent points",
            PointChangeStatus.AtMaximum => "the skill is at its maximum",
            PointChangeStatus.KeyStatTooLow => $"{result.Change.Stat} too low for another point",
            PointChangeStatus.AtFloor => "nothing bought since the sheet opened to refund",
            PointChangeStatus.AtMinimum => "no points to refund",
            var status => status.ToString(),
        } : result.Failure.ToString();
        return new(false, "refused: " + message, result);
    }

    /// <summary>The window's text of one row: project words around the shown name and values.</summary>
    public static string Line(AreaSkillRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var name = row.TrainingName is null ? row.Name : $"{row.Name} ({row.TrainingName})";
        var next = row.NextPointKeyStat == 0 ? "no further point" : $"next point needs {row.KeyStat} {row.NextPointKeyStat}";
        return $"{name}: level {row.Level}/20, {row.Points} point(s); {next}";
    }

    /// <summary>The project's label of a skill (no original text).</summary>
    public static string Label(Skill skill) => skill switch
    {
        Skill.PickPocket => "Pick pocket", Skill.SpotTrap => "Spot trap", Skill.PickLocks => "Pick locks", Skill.DisarmTraps => "Disarm traps",
        _ => skill.ToString(),
    };
}

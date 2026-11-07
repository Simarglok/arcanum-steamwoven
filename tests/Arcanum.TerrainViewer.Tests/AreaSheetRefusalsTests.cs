using System.Text;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Skills;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-078: the original's <c>charedit.mes</c> refusal of each page's + and − (ledger CP3–CP7, SP7, SG6, TD7, TD8, CHW23), on invented tables.</summary>
[TestClass]
public sealed class AreaSheetRefusalsTests
{
    private static SessionPointResult Refused(PointChangeStatus status, CharacterStat? stat = null, Skill? skill = null) =>
        new(GameSessionDisposition.NoChange, GameSessionFailure.PointRefused, new PointChange(status, null!, 0, 0, stat, skill), default);

    private static readonly SessionPointResult Done = new(GameSessionDisposition.Changed, GameSessionFailure.None, new PointChange(PointChangeStatus.Changed, null!, 1, 1), default);

    private static AreaCharacterSheetTexts Texts()
    {
        var text = new StringBuilder();
        for (var n = 17; n <= 39; n++) text.Append($"{{{n}}}{{message {n}}}\n");
        return AreaCharacterSheetTexts.Create(MesFile.Parse(Encoding.Latin1.GetBytes(text.ToString())).File!, null, null);
    }

    [TestMethod]
    public void A_skills_plus_names_the_points_the_maximum_or_the_key_stat()
    {
        Assert.AreEqual(18, AreaSheetRefusals.Number(AreaSheetPage.Skill, true, Refused(PointChangeStatus.NotEnoughPoints)));
        Assert.AreEqual(21, AreaSheetRefusals.Number(AreaSheetPage.Skill, true, Refused(PointChangeStatus.AtMaximum)));
        CollectionAssert.AreEqual(new[] { 32, 33, 34, 35, 26, 36, 37, 38 }, new[]
        {
            CharacterStat.Strength, CharacterStat.Dexterity, CharacterStat.Constitution, CharacterStat.Beauty, CharacterStat.Intelligence,
            CharacterStat.Willpower, CharacterStat.Perception, CharacterStat.Charisma,
        }.Select(stat => AreaSheetRefusals.Number(AreaSheetPage.Skill, true, Refused(PointChangeStatus.KeyStatTooLow, stat))!.Value).ToArray(), "one message per key stat");
        Assert.AreEqual(22, AreaSheetRefusals.KeyStatMessage(null), "'You must raise your stats first' for any other");
    }

    [TestMethod]
    public void A_skills_minus_names_no_points_the_floor_or_another_failure()
    {
        Assert.AreEqual(24, AreaSheetRefusals.Number(AreaSheetPage.Skill, false, Refused(PointChangeStatus.AtMinimum)));
        Assert.AreEqual(23, AreaSheetRefusals.Number(AreaSheetPage.Skill, false, Refused(PointChangeStatus.AtFloor)));
        Assert.AreEqual(25, AreaSheetRefusals.Number(AreaSheetPage.Skill, false, Refused(PointChangeStatus.KeyStatTooLow, CharacterStat.Strength)));
    }

    [TestMethod]
    public void A_spells_learn_and_unlearn_refusals_follow_the_editors_order()
    {
        Assert.AreEqual(18, AreaSheetRefusals.Number(AreaSheetPage.Spell, true, Refused(PointChangeStatus.NotEnoughPoints)));
        Assert.AreEqual(30, AreaSheetRefusals.Number(AreaSheetPage.Spell, true, Refused(PointChangeStatus.LevelTooLow)));
        Assert.AreEqual(26, AreaSheetRefusals.Number(AreaSheetPage.Spell, true, Refused(PointChangeStatus.IntelligenceTooLow)));
        Assert.AreEqual(36, AreaSheetRefusals.Number(AreaSheetPage.Spell, true, Refused(PointChangeStatus.StatTooLow, CharacterStat.Willpower)));
        Assert.AreEqual(27, AreaSheetRefusals.Number(AreaSheetPage.Spell, false, Refused(PointChangeStatus.AtFloor)), "'You cannot sell back an accepted spell.'");
    }

    [TestMethod]
    public void A_tech_degree_refuses_with_the_points_or_the_intelligence_and_its_minus_is_silent()
    {
        Assert.AreEqual(18, AreaSheetRefusals.Number(AreaSheetPage.Tech, true, Refused(PointChangeStatus.NotEnoughPoints)));
        Assert.AreEqual(26, AreaSheetRefusals.Number(AreaSheetPage.Tech, true, Refused(PointChangeStatus.IntelligenceTooLow)));
        Assert.AreEqual(26, AreaSheetRefusals.Number(AreaSheetPage.Tech, true, Refused(PointChangeStatus.AtMaximum)), "degree 7: the raise refuses, message 26");
        Assert.IsNull(AreaSheetRefusals.Number(AreaSheetPage.Tech, false, Refused(PointChangeStatus.AtMinimum)));
        Assert.IsNull(AreaSheetRefusals.Message(Texts(), AreaSheetPage.Tech, false, Refused(PointChangeStatus.AtMinimum), "project"), "a refused lower returns silently (TD8)");
    }

    [TestMethod]
    public void A_stats_plus_names_the_maximum_or_the_points_and_its_minus_the_snapshot_the_minimum_or_the_cause()
    {
        Assert.AreEqual(17, AreaSheetRefusals.Number(AreaSheetPage.Stat, true, Refused(PointChangeStatus.AtMaximum)));
        Assert.AreEqual(18, AreaSheetRefusals.Number(AreaSheetPage.Stat, true, Refused(PointChangeStatus.NotEnoughPoints)));
        Assert.IsNull(AreaSheetRefusals.Number(AreaSheetPage.Stat, true, Refused(PointChangeStatus.LaneNotReady)), "no other + refusal has a message");
        Assert.AreEqual(19, AreaSheetRefusals.Number(AreaSheetPage.Stat, false, Refused(PointChangeStatus.AtFloor)), "'below its last accepted level'");
        Assert.AreEqual(20, AreaSheetRefusals.Number(AreaSheetPage.Stat, false, Refused(PointChangeStatus.AtMinimum)));
        Assert.AreEqual(28, AreaSheetRefusals.Number(AreaSheetPage.Stat, false, Refused(PointChangeStatus.SkillNeedsStat, null, Skill.Bow)));
        Assert.AreEqual(29, AreaSheetRefusals.Number(AreaSheetPage.Stat, false, Refused(PointChangeStatus.SpellNeedsStat), CharacterStat.Intelligence));
        Assert.AreEqual(39, AreaSheetRefusals.Number(AreaSheetPage.Stat, false, Refused(PointChangeStatus.SpellNeedsStat), CharacterStat.Willpower));
        Assert.AreEqual(39, AreaSheetRefusals.Number(AreaSheetPage.Stat, false, Refused(PointChangeStatus.SpellNeedsStat, CharacterStat.Willpower)), "the change's own stat wins");
        Assert.AreEqual(31, AreaSheetRefusals.Number(AreaSheetPage.Stat, false, Refused(PointChangeStatus.DegreeNeedsStat)));
        Assert.IsNull(AreaSheetRefusals.Number(AreaSheetPage.Stat, false, Refused(PointChangeStatus.NotEnoughPoints)), "no other − refusal has a message");
    }

    [TestMethod]
    public void A_status_the_original_never_shows_keeps_the_projects_words()
    {
        Assert.IsNull(AreaSheetRefusals.Number(AreaSheetPage.Skill, false, Refused(PointChangeStatus.Invalid)));
        Assert.IsNull(AreaSheetRefusals.Number(AreaSheetPage.Spell, true, Refused(PointChangeStatus.AtMaximum)));
        Assert.IsNull(AreaSheetRefusals.Number(AreaSheetPage.Tech, true, Refused(PointChangeStatus.LaneNotReady)));
        Assert.AreEqual("project", AreaSheetRefusals.Message(Texts(), AreaSheetPage.Tech, true, Refused(PointChangeStatus.Invalid), "project"));
        Assert.AreEqual("message 39", AreaSheetRefusals.Message(Texts(), AreaSheetPage.Stat, false, Refused(PointChangeStatus.SpellNeedsStat), "project", CharacterStat.Willpower));
        Assert.AreEqual(36, AreaSheetRefusals.Number(AreaSheetPage.Spell, true, Refused(PointChangeStatus.StatTooLow)), "willpower when the change names no stat");
    }

    [TestMethod]
    public void A_success_shows_no_message_and_a_refusal_shows_the_game_folders_text_else_the_projects()
    {
        Assert.IsNull(AreaSheetRefusals.Message(Texts(), AreaSheetPage.Skill, true, Done, "spent 1"));
        Assert.AreEqual("message 18", AreaSheetRefusals.Message(Texts(), AreaSheetPage.Skill, true, Refused(PointChangeStatus.NotEnoughPoints), "project"));
        Assert.AreEqual("message 36", AreaSheetRefusals.Message(Texts(), AreaSheetPage.Spell, true, Refused(PointChangeStatus.StatTooLow, CharacterStat.Willpower), "project"));
        Assert.AreEqual("project", AreaSheetRefusals.Message(AreaCharacterSheetTexts.Project("none"), AreaSheetPage.Skill, true, Refused(PointChangeStatus.NotEnoughPoints), "project"),
            "without the game folder's text");
        Assert.AreEqual("project", AreaSheetRefusals.Message(Texts(), AreaSheetPage.Skill, true, new SessionPointResult(GameSessionDisposition.Rejected, GameSessionFailure.HeroEditorClosed, null, default), "project"),
            "a refusal the table does not list keeps the project's words");
    }
}

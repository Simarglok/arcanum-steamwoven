using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Skills;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-009: the skills page of the character sheet (ledger SP1–SP8) over a synthetic hero with five unspent points
/// and dexterity 8 (bow's key stat; invented values).</summary>
[TestClass]
public sealed class AreaSkillsPageTests
{
    private static GameSessionOwner Hero(int unspent = 5)
    {
        var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, unspent, 0,
            null, null, 385, []);
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic");
        return AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, load).Owner!;
    }

    private static MesFile Mes(string text) => MesFile.Parse(Encoding.ASCII.GetBytes(text)).File!;

    [TestMethod]
    public void The_rows_follow_the_original_page_order_and_name_from_the_texts_or_the_project()
    {
        Assert.AreEqual((AdvancementTrack.BasicSkill, 11), AreaSkillsPage.Track(11));
        Assert.AreEqual((AdvancementTrack.TechSkill, 0), AreaSkillsPage.Track(12));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaSkillsPage.Track(16));
        using var session = Hero();
        var plain = AreaSkillsPage.Build(session, null, MesTextProfile.Windows1252)!;
        Assert.AreEqual((16, 5, false), (plain.Rows.Count, plain.UnspentPoints, plain.EditorOpen));
        Assert.AreEqual(("Bow", Skill.Bow, (string?)null, (string?)null), (plain.Rows[0].Name, plain.Rows[0].Skill, plain.Rows[0].Description, plain.Rows[0].TrainingName));
        Assert.AreEqual(("Disarm traps", Skill.DisarmTraps), (plain.Rows[15].Name, plain.Rows[15].Skill));
        // 0 points: the next point needs the key stat of level 4, 6 (SR7, SP5).
        Assert.AreEqual((CharacterStat.Dexterity, 6, 0, 0), (plain.Rows[0].KeyStat, plain.Rows[0].NextPointKeyStat, plain.Rows[0].Points, plain.Rows[0].Level));
        Assert.AreEqual("Bow: level 0/20, 0 point(s); next point needs Dexterity 6", AreaSkillsPage.Line(plain.Rows[0]));
        var texts = SkillTexts.Create(Mes("{0}{Arc}\n{15}{Untrap}\n{17}{Learner}\n{20}{Shoot things}"));
        var named = AreaSkillsPage.Build(session, texts, MesTextProfile.Windows1252)!;
        Assert.AreEqual(("Arc", "Shoot things", "Untrap"), (named.Rows[0].Name, named.Rows[0].Description, named.Rows[15].Name));
        Assert.AreEqual("Melee", named.Rows[2].Name, "a number the file lacks keeps the project's label");
        using var heroless = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1).Owner!;
        Assert.IsNull(AreaSkillsPage.Build(heroless, texts, MesTextProfile.Windows1252));
    }

    [TestMethod]
    public void Plus_and_minus_go_through_the_point_pool_down_to_the_editor_floor()
    {
        using var session = Hero();
        // Closed editor: the pool refuses.
        Assert.AreEqual((false, GameSessionFailure.HeroEditorClosed), (AreaSkillsPage.Spend(session, 0).Changed, AreaSkillsPage.Spend(session, 0).Result.Failure));
        session.OpenHeroEditor();
        var spent = AreaSkillsPage.Spend(session, 0);
        Assert.AreEqual((true, "spent 1 point(s)"), (spent.Changed, spent.Message));
        var page = AreaSkillsPage.Build(session, null, MesTextProfile.Windows1252)!;
        Assert.AreEqual((1, 4, 4, true, 9), (page.Rows[0].Points, page.Rows[0].Level, page.UnspentPoints, page.EditorOpen, page.Rows[0].NextPointKeyStat));
        Assert.AreEqual(0, page.Rows[0].FloorPoints);
        // Dexterity 8 caps the level at 7: one point of bow (level 4), the second (level 8) is refused for the key stat.
        Assert.AreEqual("refused: Dexterity too low for another point", AreaSkillsPage.Spend(session, 0).Message);
        var refunded = AreaSkillsPage.Refund(session, 0);
        Assert.AreEqual((true, "refunded 1 point(s)"), (refunded.Changed, refunded.Message), "the original's click: one point changes the level");
        Assert.AreEqual("refused: nothing bought since the sheet opened to refund", AreaSkillsPage.Refund(session, 0).Message);
        // A tech row: one point, then a reopened editor takes it as the floor.
        Assert.IsTrue(AreaSkillsPage.Spend(session, 12).Changed);
        session.CloseHeroEditor(); session.OpenHeroEditor();
        Assert.AreEqual(1, AreaSkillsPage.Build(session, null, MesTextProfile.Windows1252)!.Rows[12].FloorPoints);
        Assert.AreEqual("refused: nothing bought since the sheet opened to refund", AreaSkillsPage.Refund(session, 12).Message);
        using var poor = Hero(unspent: 0);
        poor.OpenHeroEditor();
        Assert.AreEqual("refused: not enough unspent points", AreaSkillsPage.Spend(poor, 1).Message);
    }
}

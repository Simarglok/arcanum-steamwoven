using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-034: the stats page of the character editor (ledger CP3, CP4, CM5) over a synthetic half-elf with five unspent
/// points and every primary stat 8 (invented values).</summary>
[TestClass]
public sealed class AreaStatsPageTests
{
    private static GameSessionOwner Hero(int unspent = 5, int strength = 8)
    {
        var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [strength, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, unspent, 0,
            null, null, 385, []);
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic");
        return AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, load).Owner!;
    }

    [TestMethod]
    public void The_rows_are_the_pools_then_the_stats_in_the_sheet_s_order()
    {
        Assert.AreEqual((AdvancementTrack.HitPoints, 0), AreaStatsPage.Track(0));
        Assert.AreEqual((AdvancementTrack.Fatigue, 0), AreaStatsPage.Track(1));
        // The original's button table (0x5C8034): strength, constitution, dexterity, beauty, intelligence, willpower, perception, charisma.
        Assert.AreEqual((AdvancementTrack.Stat, (int)CharacterStat.Strength), AreaStatsPage.Track(2));
        Assert.AreEqual((AdvancementTrack.Stat, (int)CharacterStat.Constitution), AreaStatsPage.Track(3));
        Assert.AreEqual((AdvancementTrack.Stat, (int)CharacterStat.Willpower), AreaStatsPage.Track(7));
        Assert.AreEqual((AdvancementTrack.Stat, (int)CharacterStat.Charisma), AreaStatsPage.Track(9));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaStatsPage.Track(10));
        using var session = Hero(strength: 9);
        var page = AreaStatsPage.Build(session)!;
        Assert.AreEqual((10, 5, false), (page.Rows.Count, page.UnspentPoints, page.EditorOpen));
        Assert.AreEqual((AdvancementTrack.HitPoints, (CharacterStat?)null, 0, 1), (page.Rows[0].Track, page.Rows[0].Stat, page.Rows[0].Value, page.Rows[0].NextCost));
        Assert.AreEqual((CharacterStat.Strength, 9, 9, 1, 9), (page.Rows[2].Stat!.Value, page.Rows[2].Value, page.Rows[2].Effective, page.Rows[2].NextCost, page.Rows[2].FloorValue));
        Assert.AreEqual("Strength: 9; next point costs 1", AreaStatsPage.Line(page.Rows[2]));
        StringAssert.StartsWith(AreaStatsPage.Line(page.Rows[0]), "Hit points: ");
        using var heroless = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1).Owner!;
        Assert.IsNull(AreaStatsPage.Build(heroless));
    }

    [TestMethod]
    public void Plus_and_minus_go_through_the_point_pool_down_to_the_editor_floor()
    {
        using var session = Hero();
        Assert.AreEqual(GameSessionFailure.HeroEditorClosed, AreaStatsPage.Spend(session, 2).Result.Failure, "a closed editor refuses");
        session.OpenHeroEditor();
        Assert.AreEqual((true, "spent 1 point(s)"), (AreaStatsPage.Spend(session, 2).Changed, AreaStatsPage.Spend(session, 0).Message));
        var page = AreaStatsPage.Build(session)!;
        Assert.AreEqual((9, 8, 1, 3, true), (page.Rows[2].Value, page.Rows[2].FloorValue, page.Rows[0].Value, page.UnspentPoints, page.EditorOpen));
        Assert.AreEqual((true, "refunded 1 point(s)"), (AreaStatsPage.Refund(session, 2).Changed, AreaStatsPage.Refund(session, 0).Message));
        Assert.AreEqual("refused: nothing bought since the sheet opened to refund", AreaStatsPage.Refund(session, 2).Message);
        Assert.AreEqual("refused: nothing bought since the sheet opened to refund", AreaStatsPage.Refund(session, 1).Message);
        // Bought, then the sheet closed and opened again: the new floor holds it (owner rule 2026-10-20).
        AreaStatsPage.Spend(session, 4);
        session.CloseHeroEditor(); session.OpenHeroEditor();
        Assert.AreEqual(9, AreaStatsPage.Build(session)!.Rows[4].FloorValue);
        Assert.AreEqual("refused: nothing bought since the sheet opened to refund", AreaStatsPage.Refund(session, 4).Message);
        using var poor = Hero(unspent: 0);
        poor.OpenHeroEditor();
        Assert.AreEqual("refused: not enough unspent points", AreaStatsPage.Spend(poor, 3).Message);
    }
}

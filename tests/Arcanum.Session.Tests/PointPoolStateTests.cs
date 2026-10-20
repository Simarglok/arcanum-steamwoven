using Arcanum.Simulation.Character;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The hero's character points in the editor (ARC-WORLD-052). The half-elf fixture hero has 5 unspent points, every stat 8
// (dexterity 9 through its race effect) and nothing bought.
[TestClass]
public sealed class PointPoolStateTests
{
    private static SessionPointResult Spent(SessionPointResult result, GameSessionOwner owner, long revision)
    {
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        Assert.AreEqual(new GameSessionVersion(owner.Identity, revision), result.Version);
        Assert.AreEqual(result.Version, owner.Version);
        return result;
    }

    private static PointChange Refused(SessionPointResult result, GameSessionOwner owner, PointChangeStatus status, long revision)
    {
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.PointRefused, status),
            (result.Disposition, result.Failure, result.Change!.Status));
        Assert.AreEqual(new GameSessionVersion(owner.Identity, revision), owner.Version);
        return result.Change;
    }

    private static void Failed(SessionPointResult result, GameSessionOwner owner, GameSessionFailure failure, long revision)
    {
        Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
        Assert.IsNull(result.Change);
        Assert.AreEqual(new GameSessionVersion(owner.Identity, revision), owner.Version);
    }

    [TestMethod]
    public void TheEditorTakesTheFloorWhenItOpensAndDropsItWhenItCloses()
    {
        using var owner = CreateWith(HalfElf());
        Assert.IsFalse(owner.HeroEditorOpen);
        Assert.IsNull(owner.QueryHeroEditorFloor());
        Failed(owner.SpendHeroPoint(owner.Version, AdvancementTrack.Stat, 0), owner, GameSessionFailure.HeroEditorClosed, 0);

        var opened = owner.OpenHeroEditor();
        Assert.AreEqual((GameSessionDisposition.Changed, new GameSessionVersion(owner.Identity, 0)), (opened.Disposition, opened.Version),
            "editor state is not saved: the revision stays");
        var floor = owner.QueryHeroEditorFloor()!;
        Assert.AreEqual((5, 8, CharacterRace.HalfElf), (floor.UnspentPoints, floor.Stats[1], floor.Race));
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.OpenHeroEditor().Disposition);
        Assert.AreSame(floor, owner.QueryHeroEditorFloor(), "opening again keeps the floor");

        Assert.AreEqual(GameSessionDisposition.Changed, owner.CloseHeroEditor().Disposition);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.CloseHeroEditor().Disposition);
        Assert.IsNull(owner.QueryHeroEditorFloor());
        Assert.AreEqual(0, owner.Version.Revision);
    }

    [TestMethod]
    public void PointsSpentWhileOpenRefundDownToTheFloorAndNeverAfterClosing()
    {
        using var owner = CreateWith(HalfElf());
        owner.OpenHeroEditor();
        var raised = Spent(owner.SpendHeroPoint(owner.Version, AdvancementTrack.Stat, (int)CharacterStat.Strength), owner, 1);
        Assert.AreEqual((1, 9), (raised.Change!.Points, raised.Change.Value));
        Assert.AreEqual((9, 4), (owner.QueryHero()!.Stats[0], owner.QueryHero()!.UnspentPoints));
        Assert.AreEqual(9, owner.QueryHeroStats(new(0))!.Value.Snapshot!.GetEffective(CharacterStat.Strength), "the character owner follows");
        Spent(owner.SpendHeroPoint(owner.Version, AdvancementTrack.BasicSkill, (int)Skill.Dodge), owner, 2);
        Spent(owner.SpendHeroPoint(owner.Version, AdvancementTrack.HitPoints, 0), owner, 3);
        Assert.AreEqual((1, 1, 2), (owner.QueryHeroSkills()![Skill.Dodge].Points, owner.QueryHero()!.Advancement.HitPointPoints,
            owner.QueryHero()!.UnspentPoints));

        Spent(owner.RefundHeroPoint(owner.Version, AdvancementTrack.Stat, 0), owner, 4);
        Refused(owner.RefundHeroPoint(owner.Version, AdvancementTrack.Stat, 0), owner, PointChangeStatus.AtFloor, 4);
        Spent(owner.RefundHeroPoint(owner.Version, AdvancementTrack.BasicSkill, (int)Skill.Dodge, SkillRefundClick.OnePoint), owner, 5);
        Assert.AreEqual(4, owner.QueryHero()!.UnspentPoints);

        owner.CloseHeroEditor();
        Failed(owner.RefundHeroPoint(owner.Version, AdvancementTrack.HitPoints, 0), owner, GameSessionFailure.HeroEditorClosed, 5);
        owner.OpenHeroEditor();
        Refused(owner.RefundHeroPoint(owner.Version, AdvancementTrack.HitPoints, 0), owner, PointChangeStatus.AtFloor, 5);
        Assert.AreEqual(1, owner.QueryHero()!.Advancement.HitPointPoints, "bought before this opening: kept for good");
    }

    [TestMethod]
    public void ARestoredSessionStartsWithTheEditorClosed()
    {
        using var owner = CreateWith(HalfElf());
        owner.OpenHeroEditor();
        Spent(owner.SpendHeroPoint(owner.Version, AdvancementTrack.Fatigue, 0), owner, 1);
        using var restored = Restore(owner.Capture());
        Assert.IsFalse(restored.HeroEditorOpen);
        Assert.AreEqual((1, 4), (restored.QueryHero()!.Advancement.FatiguePoints, restored.QueryHero()!.UnspentPoints));
        restored.OpenHeroEditor();
        Refused(restored.RefundHeroPoint(restored.Version, AdvancementTrack.Fatigue, 0), restored, PointChangeStatus.AtFloor, 1);
    }

    [TestMethod]
    public void RefusalsComeInOrderAndTheSpellLaneIsNotReady()
    {
        using var none = Create();
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.HeroEditorUnavailable),
            (none.OpenHeroEditor().Disposition, none.OpenHeroEditor().Failure));
        Failed(none.SpendHeroPoint(none.Version, AdvancementTrack.Stat, 0), none, GameSessionFailure.HeroEditorUnavailable, 0);

        using var owner = CreateWith(HalfElf());
        owner.OpenHeroEditor();
        Failed(owner.SpendHeroPoint(owner.Version, AdvancementTrack.Stat, 8), owner, GameSessionFailure.InvalidPointTarget, 0);
        Failed(owner.SpendHeroPoint(owner.Version, (AdvancementTrack)9, 0), owner, GameSessionFailure.InvalidPointTarget, 0);
        Failed(owner.RefundHeroPoint(new GameSessionVersion(owner.Identity, 7), AdvancementTrack.Stat, 0, (SkillRefundClick)5), owner,
            GameSessionFailure.InvalidPointTarget, 0);
        Failed(owner.SpendHeroPoint(new GameSessionVersion(owner.Identity, 7), AdvancementTrack.Stat, 0), owner, GameSessionFailure.StaleVersion, 0);
        Refused(owner.SpendHeroPoint(owner.Version, AdvancementTrack.College, 3), owner, PointChangeStatus.LaneNotReady, 0);
        Spent(owner.SpendHeroPoint(owner.Version, AdvancementTrack.BasicSkill, (int)Skill.Persuasion), owner, 1);
        var key = Refused(owner.SpendHeroPoint(owner.Version, AdvancementTrack.BasicSkill, (int)Skill.Persuasion), owner,
            PointChangeStatus.KeyStatTooLow, 1);
        Assert.AreEqual(CharacterStat.Charisma, key.Stat, "charisma 8 caps persuasion at 7: one point, not two");
    }

    [TestMethod]
    public void TheSessionBoundsThePoolsAndADeadHeroCannotOpenTheEditor()
    {
        var bounded = HalfElf() with { Advancement = SessionHeroAdvancement.None with { HitPointPoints = SessionAdvancementLimits.MaxHeroPoolPoints } };
        using var owner = CreateWith(bounded);
        owner.OpenHeroEditor();
        Refused(owner.SpendHeroPoint(owner.Version, AdvancementTrack.HitPoints, 0), owner, PointChangeStatus.AtMaximum, 0);

        using var dead = CreateWith(HalfElf());
        dead.OpenHeroEditor();
        dead.Kill(dead.Version, SessionLifeTarget.Hero);
        var revision = dead.Version.Revision;
        Failed(dead.SpendHeroPoint(dead.Version, AdvancementTrack.Stat, 0), dead, GameSessionFailure.HeroEditorUnavailable, revision);
        dead.CloseHeroEditor();
        var refused = dead.OpenHeroEditor();
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.HeroEditorUnavailable), (refused.Disposition, refused.Failure));
    }
}

using Arcanum.Data.Characters;
using Arcanum.Simulation.Character;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The hero's tech degrees through the point pool (ARC-TECHNOLOGY-002). The half-elf fixture hero has 5 unspent points and every
// stat 8 unless the test sets them; effects are invented.
[TestClass]
public sealed class TechDegreeStateTests
{
    private static GameSessionOwner Editing(int intelligence = 8)
    {
        var stats = Defaults();
        stats[(int)CharacterStat.Intelligence] = intelligence;
        var owner = CreateWith(HalfElf(stats));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.OpenHeroEditor().Disposition);
        return owner;
    }

    private static SessionPointResult Ok(SessionPointResult result, GameSessionOwner owner, long revision)
    {
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure), $"{result.Change?.Status}");
        Assert.AreEqual(new GameSessionVersion(owner.Identity, revision), owner.Version);
        return result;
    }

    private static void Refused(SessionPointResult result, GameSessionOwner owner, PointChangeStatus status, long revision)
    {
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.PointRefused, status), (result.Disposition, result.Failure, result.Change!.Status));
        Assert.AreEqual(new GameSessionVersion(owner.Identity, revision), owner.Version);
    }

    [TestMethod]
    public void A_degree_is_bought_with_a_point_and_gives_expertise()
    {
        using var owner = Editing();
        Assert.IsTrue(owner.QueryHeroTechnology()!.Value.All(d => d is { Degree: 0, CountingDegree: 0, Expertise: 0 }));
        var bought = Ok(owner.SpendHeroPoint(owner.Version, AdvancementTrack.TechDiscipline, 3), owner, 1);
        Assert.AreEqual((1, 1), (bought.Change!.Points, bought.Change.Value));
        Ok(owner.SpendHeroPoint(owner.Version, AdvancementTrack.TechDiscipline, 3), owner, 2);
        var hero = owner.QueryHero()!;
        Assert.AreEqual((2, 3, 2), (hero.Advancement.TechDegrees[3], hero.UnspentPoints, hero.Stats[9]), "degree 2, 5 − 2 points, tech points 0 + 2");
        // The tech points move the aptitude toward technology.
        Assert.IsTrue(owner.QueryHeroStats(new(0))!.Value.Snapshot!.GetEffective(CharacterStat.MagickTechAptitude) < 0);
        Assert.AreEqual(new SessionTechDiscipline(3, 2, 2, 20), owner.QueryHeroTechnology()!.Value[3]);
    }

    [TestMethod]
    public void Raising_needs_the_effective_intelligence_and_stops_at_seven()
    {
        using var owner = Editing(intelligence: 7);
        Ok(owner.SpendHeroPoint(owner.Version, AdvancementTrack.TechDiscipline, 0), owner, 1);
        Refused(owner.SpendHeroPoint(owner.Version, AdvancementTrack.TechDiscipline, 0), owner, PointChangeStatus.IntelligenceTooLow, 1);
        Assert.AreEqual(CharacterStat.Intelligence, owner.SpendHeroPoint(owner.Version, AdvancementTrack.TechDiscipline, 0).Change!.Stat);
        // An intelligence effect lifts the effective value: degree 2 needs 8.
        var clever = new SessionEffectDefinition(120, [new(EffectAttributeKind.Stat, (int)CharacterStat.Intelligence, EffectChangeOperator.Add, 1)]);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroEffect(owner.Version, clever, EffectCause.Item).Disposition);
        Ok(owner.SpendHeroPoint(owner.Version, AdvancementTrack.TechDiscipline, 0), owner, 3);

        var seven = HalfElf() with { Stats = [8, 8, 8, 8, 20, 8, 8, 8, 0, 7], UnspentPoints = 5,
            Advancement = SessionHeroAdvancement.None with { TechDegrees = [7, 0, 0, 0, 0, 0, 0, 0] } };
        using var master = CreateWith(seven);
        master.OpenHeroEditor();
        Refused(master.SpendHeroPoint(master.Version, AdvancementTrack.TechDiscipline, 0), master, PointChangeStatus.AtMaximum, 0);
    }

    [TestMethod]
    public void A_degree_is_refunded_only_down_to_the_value_the_editor_opened_with_TC10()
    {
        using var owner = Editing();
        Ok(owner.SpendHeroPoint(owner.Version, AdvancementTrack.TechDiscipline, 5), owner, 1);
        Ok(owner.SpendHeroPoint(owner.Version, AdvancementTrack.TechDiscipline, 5), owner, 2);
        var refunded = Ok(owner.RefundHeroPoint(owner.Version, AdvancementTrack.TechDiscipline, 5), owner, 3);
        Assert.AreEqual((1, 1), (refunded.Change!.Points, refunded.Change.Value));
        Assert.AreEqual((4, 1), (owner.QueryHero()!.UnspentPoints, owner.QueryHero()!.Stats[9]));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.CloseHeroEditor().Disposition);
        owner.OpenHeroEditor();
        // Degree 1 was bought before the editor opened again: it is never refundable.
        Refused(owner.RefundHeroPoint(owner.Version, AdvancementTrack.TechDiscipline, 5), owner, PointChangeStatus.AtFloor, 3);
    }

    [TestMethod]
    public void Intelligence_bought_for_a_degree_cannot_be_refunded_below_its_minimum()
    {
        using var owner = Editing(intelligence: 7);
        Ok(owner.SpendHeroPoint(owner.Version, AdvancementTrack.Stat, (int)CharacterStat.Intelligence), owner, 1);
        Ok(owner.SpendHeroPoint(owner.Version, AdvancementTrack.TechDiscipline, 2), owner, 2);
        Ok(owner.SpendHeroPoint(owner.Version, AdvancementTrack.TechDiscipline, 2), owner, 3);
        Refused(owner.RefundHeroPoint(owner.Version, AdvancementTrack.Stat, (int)CharacterStat.Intelligence), owner, PointChangeStatus.DegreeNeedsStat, 3);
        Ok(owner.RefundHeroPoint(owner.Version, AdvancementTrack.TechDiscipline, 2), owner, 4);
        Ok(owner.RefundHeroPoint(owner.Version, AdvancementTrack.Stat, (int)CharacterStat.Intelligence), owner, 5);
    }

    [TestMethod]
    public void Expertise_counts_the_degree_intelligence_holds_and_the_discipline_effects_unclamped()
    {
        var stats = Defaults();
        stats[(int)CharacterStat.Intelligence] = 13;
        var hero = HalfElf(stats) with { Advancement = SessionHeroAdvancement.None with { TechDegrees = [4, 0, 0, 0, 0, 0, 0, 0] } };
        using var owner = CreateWith(hero);
        Assert.AreEqual(new SessionTechDiscipline(0, 4, 4, 50), owner.QueryHeroTechnology()!.Value[0]);

        // A temporary intelligence drop lowers the counting degree (degree 4 needs 13, 3 needs 11), not the stored one.
        var dull = new SessionEffectDefinition(121, [new(EffectAttributeKind.Stat, (int)CharacterStat.Intelligence, EffectChangeOperator.Add, -2)]);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroEffect(owner.Version, dull, EffectCause.Spell).Disposition);
        Assert.AreEqual(new SessionTechDiscipline(0, 4, 3, 35), owner.QueryHeroTechnology()!.Value[0]);

        // Expertise effects of the discipline, every cause: +80 lifts it past 100; another discipline is not touched.
        var expert = new SessionEffectDefinition(122, [new(EffectAttributeKind.TechDiscipline, 0, EffectChangeOperator.Add, 80),
            new(EffectAttributeKind.TechDiscipline, 6, EffectChangeOperator.Add, -15)]);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroEffect(owner.Version, expert, EffectCause.Tech).Disposition);
        var technology = owner.QueryHeroTechnology()!.Value;
        Assert.AreEqual((115, -15, 0), (technology[0].Expertise, technology[6].Expertise, technology[1].Expertise), "no clamp");
        using var heroless = Create();
        Assert.IsNull(heroless.QueryHeroTechnology());
    }
}

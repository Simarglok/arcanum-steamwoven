using System.Collections.Immutable;
using Arcanum.Simulation.Character;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Character.AutoLevelRulesTests;

namespace Arcanum.Simulation.Tests.Character;

// ARC-TECHNOLOGY-002: the tech degree rules (ledger TD1–TD9). Sheets are invented: a human with every primary stat 8 at level 1,
// nothing bought (AutoLevelRulesTests.Sheet).
[TestClass]
public sealed class TechDegreeRulesTests
{
    private static AdvancementSheet WithIntelligence(AdvancementSheet sheet, int value) =>
        sheet with { Stats = sheet.Stats.SetItem((int)CharacterStat.Intelligence, value) };

    private static AdvancementSheet WithDegree(AdvancementSheet sheet, int discipline, int degree) =>
        sheet with { TechDegrees = sheet.TechDegrees.SetItem(discipline, degree) };

    [TestMethod]
    public void The_tables_are_the_auto_level_rules_and_the_expertise_percentages()
    {
        CollectionAssert.AreEqual(new[] { 0, 1, 1, 1, 1, 1, 1, 1 }, Enumerable.Range(0, 8).Select(AutoLevelRules.DegreeCost).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 5, 8, 11, 13, 15, 17, 19 }, Enumerable.Range(0, 8).Select(AutoLevelRules.DegreeMinimumIntelligence).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 10, 20, 35, 50, 65, 80, 100 }, Enumerable.Range(0, 8).Select(TechDegreeRules.ExpertisePercent).ToArray());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TechDegreeRules.ExpertisePercent(8));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AutoLevelRules.DegreeCost(-1));
    }

    [TestMethod]
    public void A_critter_reads_its_stored_degree_and_any_other_object_zero()
    {
        Assert.AreEqual(3, TechDegreeRules.Degree(WithDegree(Sheet(), 2, 3), 2));
        Assert.AreEqual(0, TechDegreeRules.Degree(null, 2));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TechDegreeRules.Degree(null, 8));
    }

    [TestMethod]
    public void Raising_needs_the_effective_intelligence_of_the_new_degree_adds_a_tech_point_and_stops_at_seven()
    {
        var raised = TechDegreeRules.Raise(Sheet(), AdvancementModifiers.None, 4);
        Assert.AreEqual((PointChangeStatus.Changed, 1, 1, 1), (raised.Status, raised.Points, raised.Sheet.TechDegrees[4], raised.Sheet.TechPoints));
        Assert.AreEqual(10, raised.Sheet.UnspentPoints, "the pool moves the unspent points");
        // Degree 3 needs intelligence 11; base 8 with +3 effective is enough, base 10 alone is not.
        var two = WithDegree(Sheet(), 4, 2);
        Assert.AreEqual(PointChangeStatus.IntelligenceTooLow, TechDegreeRules.Raise(WithIntelligence(two, 10), AdvancementModifiers.None, 4).Status);
        var clever = AdvancementModifiers.None with { Stats = [new(CharacterStat.Intelligence, StatModifierOperator.Add, 3)] };
        Assert.AreEqual(PointChangeStatus.Changed, TechDegreeRules.Raise(two, clever, 4).Status);
        var seven = WithIntelligence(WithDegree(Sheet(), 4, 7), 20);
        Assert.AreEqual(PointChangeStatus.AtMaximum, TechDegreeRules.Raise(seven, AdvancementModifiers.None, 4).Status);
        Assert.AreEqual(PointChangeStatus.Invalid, TechDegreeRules.Raise(Sheet(), AdvancementModifiers.None, 8).Status);
    }

    [TestMethod]
    public void Lowering_takes_the_tech_point_back_without_an_intelligence_check()
    {
        var bought = WithDegree(Sheet(), 1, 3) with { TechPoints = 3 };
        // Intelligence 5 no longer holds degree 3; lowering does not look.
        var lowered = TechDegreeRules.Lower(WithIntelligence(bought, 5), AdvancementModifiers.None, 1);
        Assert.AreEqual((PointChangeStatus.Changed, 1, 2, 2), (lowered.Status, lowered.Points, lowered.Sheet.TechDegrees[1], lowered.Sheet.TechPoints));
        Assert.AreEqual(PointChangeStatus.AtMinimum, TechDegreeRules.Lower(Sheet(), AdvancementModifiers.None, 1).Status);
    }

    [TestMethod]
    public void Expertise_counts_the_degree_the_intelligence_holds_then_the_effects_without_a_clamp()
    {
        Assert.AreEqual(50, TechDegreeRules.EffectiveExpertise(4, 13, []));
        Assert.AreEqual((35, 3), (TechDegreeRules.EffectiveExpertise(4, 12, []), TechDegreeRules.CountingDegree(4, 12)), "12 holds degree 3, not 4");
        Assert.AreEqual((0, 0), (TechDegreeRules.EffectiveExpertise(2, 4, []), TechDegreeRules.CountingDegree(2, 4)));
        Assert.AreEqual(100, TechDegreeRules.EffectiveExpertise(7, 19, []));
        // The effect aggregation: percent, multiply, divide, add, then min and max; unclamped.
        Assert.AreEqual(150, TechDegreeRules.EffectiveExpertise(7, 19, [new(StatModifierOperator.Add, 50)]));
        Assert.AreEqual(-20, TechDegreeRules.EffectiveExpertise(1, 5, [new(StatModifierOperator.Add, -30)]));
        Assert.AreEqual(75, TechDegreeRules.EffectiveExpertise(4, 13, [new(StatModifierOperator.Percent, 50)]));
        Assert.AreEqual(30, TechDegreeRules.EffectiveExpertise(4, 13, [new(StatModifierOperator.Maximum, 30)]));
    }

    [TestMethod]
    public void The_lane_buys_and_refunds_through_the_pool_down_to_the_floor()
    {
        var floor = Sheet(unspent: 2);
        var willpower = AutoLevelRules.DefaultSpellWillpower;
        var spent = CharacterPointRules.Spend(floor, AdvancementModifiers.None, AdvancementTrack.TechDiscipline, 0, willpower, TechDegreeRules.Lane);
        Assert.AreEqual((PointChangeStatus.Changed, 1, 1, 1), (spent.Status, spent.Points, spent.Sheet.UnspentPoints, spent.Sheet.TechDegrees[0]));
        var refunded = CharacterPointRules.Refund(spent.Sheet, floor, AdvancementModifiers.None, AdvancementTrack.TechDiscipline, 0,
            SkillRefundClick.UntilLevelChanges, willpower, TechDegreeRules.Lane);
        Assert.AreEqual((PointChangeStatus.Changed, 2, 0, 0), (refunded.Status, refunded.Sheet.UnspentPoints, refunded.Sheet.TechDegrees[0], refunded.Sheet.TechPoints));
        Assert.AreEqual(PointChangeStatus.AtFloor, CharacterPointRules.Refund(refunded.Sheet, floor, AdvancementModifiers.None,
            AdvancementTrack.TechDiscipline, 0, SkillRefundClick.UntilLevelChanges, willpower, TechDegreeRules.Lane).Status);
        var broke = Sheet(unspent: 0);
        Assert.AreEqual(PointChangeStatus.NotEnoughPoints, CharacterPointRules.Spend(broke, AdvancementModifiers.None, AdvancementTrack.TechDiscipline, 0,
            willpower, TechDegreeRules.Lane).Status);
        Assert.AreEqual(PointChangeStatus.IntelligenceTooLow, CharacterPointRules.Spend(WithIntelligence(Sheet(), 4), AdvancementModifiers.None,
            AdvancementTrack.TechDiscipline, 0, willpower, TechDegreeRules.Lane).Status);
        // The buy wrapper checks the points before the raise: no points and too little intelligence is "not enough points";
        // at degree 7 it is "at maximum" whatever the points.
        Assert.AreEqual(PointChangeStatus.NotEnoughPoints, CharacterPointRules.Spend(WithIntelligence(broke, 4), AdvancementModifiers.None,
            AdvancementTrack.TechDiscipline, 0, willpower, TechDegreeRules.Lane).Status);
        Assert.AreEqual(PointChangeStatus.AtMaximum, CharacterPointRules.Spend(WithIntelligence(WithDegree(broke, 0, 7), 20), AdvancementModifiers.None,
            AdvancementTrack.TechDiscipline, 0, willpower, TechDegreeRules.Lane).Status);
    }
}

using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Technology;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-TECHNOLOGY-007: the session's charge-cell port. The hero pays from the host's cell stack; asking changes nothing (the
// session holds no ammunition yet); TC1 follows the session's rule mode. Numbers are invented.
[TestClass]
public sealed class CellSourceStateTests
{
    private static readonly SpellPaymentRecord Cells = new(ChargesFromCells: true, MaintainPeriod: 0, DurationSeconds: 0);

    [TestMethod]
    public void The_hero_pays_cells_from_the_hosts_stack_without_changing_the_session()
    {
        using var owner = CreateWith(HalfElf());
        var before = owner.Version;
        var payer = owner.QueryHeroCellPayer(new CellStack("cells:1", 10))!;
        var source = owner.CellSource(payer);
        var paid = SpellPaymentRules.Pay(new SpellPayer(SpellPayerKind.Critter, CurrentFatigue: 50), 7, true, 168, Cells, source);
        Assert.AreEqual((SpellPaymentStatus.Paid, 3, true), (paid.Status, source.LastPlan!.UnitsAfter, source.LastPlan.Writes));
        var check = SpellPaymentRules.Pay(new SpellPayer(SpellPayerKind.Critter, CurrentFatigue: 50), 7, false, 168, Cells, source);
        Assert.AreEqual((SpellPaymentStatus.Paid, false), (check.Status, source.LastPlan!.Writes), "an affordability check writes nothing");
        Assert.AreEqual(before, owner.Version, "the session holds no ammunition: nothing changes");
        using var heroless = Create();
        Assert.IsNull(heroless.QueryHeroCellPayer(null));
        Assert.IsNull(heroless.QueryHeroItemCellPayer(-50, null));
    }

    [TestMethod]
    public void An_item_the_hero_holds_pays_by_the_heros_aptitude_and_the_sessions_tc1_setting()
    {
        using var owner = CreateWith(HalfElf());
        var payer = owner.QueryHeroItemCellPayer(-50, new CellStack("cells:1", 100))!;
        Assert.AreEqual((SpellPayerKind.Item, CellOwnerKind.Critter, -50), (payer.Payer, payer.Owner, payer.ItemComplexity));
        foreach (var mode in new[] { RuleMode.Steamwoven, RuleMode.Original })
        {
            owner.SelectRules(mode);
            var source = owner.CellSource(payer);
            var paid = SpellPaymentRules.Pay(new SpellPayer(SpellPayerKind.Item), 7, true, 147, Cells, source);
            Assert.AreEqual(ChargeCellRules.Need(payer, 7, owner.Rules.Technology), paid.Cells!.Cells, mode.ToString());
        }
        owner.SelectRules(RuleMode.Original);
        var original = ChargeCellRules.Need(payer, 7, owner.Rules.Technology);
        owner.SelectRules(RuleMode.Steamwoven);
        Assert.AreEqual(21, original + ChargeCellRules.Need(payer, 7, owner.Rules.Technology), "TC1 mirrors the share: cost + s and 2 × cost − s");
    }
}

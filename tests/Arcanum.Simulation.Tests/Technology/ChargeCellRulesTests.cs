using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Technology;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Technology;

/// <summary>ARC-TECHNOLOGY-007: the charge-cell branch of the spell payment (ledger CC1–CC5, TC1, CCA1–CCA3) over invented
/// numbers: the need of a critter and of an item for each complexity sign and holder, the one stack, the refusals and the
/// unapplied request.</summary>
[TestClass]
public sealed class ChargeCellRulesTests
{
    private static readonly TechnologyRuleProfile Steamwoven = TechnologyRuleProfile.Steamwoven, Original = TechnologyRuleProfile.Original;
    private static CellPayer Item(int complexity, int aptitude, CellOwnerKind owner = CellOwnerKind.Critter, int units = 100) =>
        new(SpellPayerKind.Item, owner, new("cells", units), complexity, aptitude);

    [TestMethod]
    public void A_critter_or_another_object_needs_the_cost()
    {
        var critter = new CellPayer(SpellPayerKind.Critter, CellOwnerKind.Critter, new("cells", 9));
        Assert.AreEqual(7, ChargeCellRules.Need(critter, 7, Steamwoven));
        Assert.AreEqual(7, ChargeCellRules.Need(critter, 7, Original));
        Assert.AreEqual(0, ChargeCellRules.Need(critter, 0, Original), "a cost of 0 or less needs nothing");
        Assert.AreEqual(7, ChargeCellRules.Need(critter with { Payer = SpellPayerKind.Other }, 7, Original));
    }

    [TestMethod]
    public void An_item_needs_twice_the_cost_less_the_magic_adjustment_and_tc1_switches_the_technological_side()
    {
        Assert.AreEqual(7, ChargeCellRules.Need(Item(0, -100), 7, Original), "a neutral item: the cost");
        // A magickal item (complexity 50): a mage (power 50) pays the cost, a technologist (power 0) twice; both settings.
        Assert.AreEqual(7, ChargeCellRules.Need(Item(50, 100), 7, Original));
        Assert.AreEqual(14, ChargeCellRules.Need(Item(50, -100), 7, Steamwoven));
        // A technological item (complexity −50): Original makes a technologist (power −50) pay twice and a mage (power 0) the cost.
        Assert.AreEqual(14, ChargeCellRules.Need(Item(-50, -100), 7, Original));
        Assert.AreEqual(7, ChargeCellRules.Need(Item(-50, 100), 7, Original));
        // Steamwoven (TC1): the technologist pays the cost and the mage twice.
        Assert.AreEqual(7, ChargeCellRules.Need(Item(-50, -100), 7, Steamwoven));
        Assert.AreEqual(14, ChargeCellRules.Need(Item(-50, 100), 7, Steamwoven));
        // A neutral holder (aptitude 0, power trunc(−50 / 2) = −25): the share trunc(7 × −25 / −50) = 3.
        Assert.AreEqual((10, 11), (ChargeCellRules.Need(Item(-50, 0), 7, Original), ChargeCellRules.Need(Item(-50, 0), 7, Steamwoven)));
        // A parent that is not a critter gives the item's complexity as power.
        Assert.AreEqual((14, 7), (ChargeCellRules.Need(Item(-50, 0, CellOwnerKind.Container), 7, Original),
            ChargeCellRules.Need(Item(-50, 0, CellOwnerKind.Container), 7, Steamwoven)));
        Assert.AreEqual(7, ChargeCellRules.Need(Item(50, -100, CellOwnerKind.None), 7, Original));
    }

    [TestMethod]
    public void A_need_out_of_range_is_refused_in_both_settings()
    {
        foreach (var profile in TechnologyRuleProfile.All)
        {
            Assert.IsNull(ChargeCellRules.Need(Item(50, -100), int.MaxValue, profile), "2 × cost does not fit 32 bits");
            Assert.IsNull(ChargeCellRules.Need(Item(5000, 0), 7, profile), "a complexity out of range");
            Assert.IsNull(ChargeCellRules.Need(Item(-50, 500), 7, profile), "an aptitude out of range");
            Assert.IsNull(ChargeCellRules.Need(Item(1000, 100), 10_000_000, profile), "power × cost wraps in 32 bits although the need would fit");
            Assert.AreEqual(CellRefusal.OutOfRange, ChargeCellRules.Plan(Item(50, -100), int.MaxValue, true, profile).Refusal);
        }
        Assert.AreEqual(int.MaxValue, ChargeCellRules.Need(Item(0, 0), int.MaxValue, Original), "a neutral item's need is the cost");
    }

    [TestMethod]
    public void The_cells_come_from_one_stack_which_goes_when_emptied()
    {
        var critter = new CellPayer(SpellPayerKind.Critter, CellOwnerKind.Critter, new("cells", 10));
        Assert.AreEqual(new CellPlan(true, CellRefusal.None, 7, new("cells", 10), 3, false, true), ChargeCellRules.Plan(critter, 7, true, Original));
        Assert.AreEqual(new CellPlan(true, CellRefusal.None, 10, new("cells", 10), 0, true, true), ChargeCellRules.Plan(critter, 10, true, Original));
        var short_ = ChargeCellRules.Plan(critter, 11, true, Original);
        Assert.AreEqual((false, CellRefusal.TooFewCells, 11, 10, false), (short_.Paid, short_.Refusal, short_.Need, short_.UnitsAfter, short_.StackRemoved),
            "too few cells: nothing is taken");
        Assert.IsFalse(ChargeCellRules.Plan(critter with { Cells = null }, 1, true, Original).Paid, "no stack");
        Assert.IsFalse(ChargeCellRules.Plan(critter with { Cells = new("cells", -5) }, 1, true, Original).Paid, "a negative count");
        Assert.IsFalse(ChargeCellRules.Plan(Item(0, 0, CellOwnerKind.None), 1, true, Original).Paid, "an item without a parent counts 0");
        Assert.IsFalse(ChargeCellRules.Plan(Item(0, 0, CellOwnerKind.Other), 1, true, Original).Paid, "another parent counts 0");
        Assert.IsFalse(ChargeCellRules.Plan(Item(0, 0, CellOwnerKind.Container, units: 5), 5, true, Original).Paid, "a container's lookup never matches (AM5)");
        Assert.IsTrue(ChargeCellRules.Plan(critter with { Cells = null }, 0, true, Original).Paid, "a cost of 0 takes nothing");
    }

    [TestMethod]
    public void An_unapplied_request_answers_the_same_and_writes_nothing()
    {
        var critter = new CellPayer(SpellPayerKind.Critter, CellOwnerKind.Critter, new("cells", 10));
        var check = ChargeCellRules.Plan(critter, 7, false, Original);
        Assert.AreEqual((true, 3, false, false), (check.Paid, check.UnitsAfter, check.Applied, check.Writes));
        Assert.IsFalse(ChargeCellRules.Plan(critter, 11, false, Original).Paid, "the same refusal");
        Assert.IsTrue(ChargeCellRules.Plan(critter, 7, true, Original).Writes);
    }

    [TestMethod]
    public void The_payment_asks_the_source_with_the_whole_cost_before_any_other_branch()
    {
        var record = new SpellPaymentRecord(ChargesFromCells: true, MaintainPeriod: 0, DurationSeconds: 0);
        var source = new ChargeCellSource(new(SpellPayerKind.Critter, CellOwnerKind.Critter, new("cells", 10)), Original);
        var payer = new SpellPayer(SpellPayerKind.Critter, FatigueLimiting: true, CurrentFatigue: 100);
        var paid = SpellPaymentRules.Pay(payer, 7, apply: true, spell: 168, record, source);
        Assert.AreEqual((SpellPaymentStatus.Paid, 7, 0), (paid.Status, paid.Cells!.Cells, paid.FatigueDamageAdded), "never halved, no fatigue");
        Assert.AreEqual((3, true), (source.LastPlan!.UnitsAfter, source.LastPlan.Writes));
        var refused = SpellPaymentRules.Pay(payer, 11, apply: false, spell: 168, record, source);
        Assert.AreEqual(SpellPaymentStatus.CellsRefused, refused.Status);
        Assert.IsFalse(source.LastPlan!.Applied);
        // An item payer: the payment's payer kind reaches the source, which uses the item's facts.
        var device = new ChargeCellSource(Item(-50, -100), Steamwoven);
        Assert.AreEqual(7, SpellPaymentRules.Pay(new SpellPayer(SpellPayerKind.Item, ItemCharges: 0), 7, true, 147, record, device).Cells!.Cells);
        // A container paying from its own first cell item; an out-of-range plan and a payer the source was not built for refuse with 0 cells.
        var container = new ChargeCellSource(new(SpellPayerKind.Other, CellOwnerKind.Container, new("cells", 7)), Original);
        Assert.AreEqual(SpellPaymentStatus.CellsRefused, SpellPaymentRules.Pay(new SpellPayer(SpellPayerKind.Other), 7, true, 187, record, container).Status,
            "the original's container lookup never finds its cells (AM5)");
        var huge = SpellPaymentRules.Pay(new SpellPayer(SpellPayerKind.Item), int.MaxValue, true, 147, record, new ChargeCellSource(Item(50, -100), Original));
        Assert.AreEqual((SpellPaymentStatus.CellsRefused, 0), (huge.Status, huge.Cells!.Cells));
        var mismatch = new ChargeCellSource(new(SpellPayerKind.Critter, CellOwnerKind.Critter, new("cells", 10)), Original);
        Assert.AreEqual(SpellPaymentStatus.CellsRefused, SpellPaymentRules.Pay(new SpellPayer(SpellPayerKind.Item), 7, true, 147, record, mismatch).Status);
        Assert.AreEqual(CellRefusal.PayerMismatch, mismatch.LastPlan!.Refusal);
        Assert.IsTrue(TechnologyRuleProfile.Original.ItemCellsFavourMagick);
        Assert.IsFalse(TechnologyRuleProfile.Steamwoven.ItemCellsFavourMagick);
    }
}

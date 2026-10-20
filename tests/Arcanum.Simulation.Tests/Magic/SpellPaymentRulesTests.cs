using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Magic;

// ARC-MAGIC-003: the spell cost formulas and the payment plan, on synthetic payers.
[TestClass]
public sealed class SpellPaymentRulesTests
{
    private static readonly SpellCaster Human = new(true, 0, -1), Dwarf = new(true, SpellCostRules.DwarfRace, -1);
    private static readonly SpellPaymentRecord Timed = new(false, 0, 30), Maintained = new(false, 10, 0), Untimed = new(false, 0, 0);

    /// <summary>A cell source that records its requests and answers as told.</summary>
    private sealed class Cells(bool pays, int cells = 3) : ISpellCellSource
    {
        public List<SpellCellRequest> Requests { get; } = [];

        public SpellCellPayment Pay(SpellCellRequest request)
        {
            Requests.Add(request);
            return new(pays, pays ? cells : 0);
        }
    }

    private static SpellPayer Critter(int fatigue, int damage = 0, bool limiting = false, params int[] stores) =>
        new(SpellPayerKind.Critter, limiting, stores, fatigue, damage);

    [TestMethod]
    public void The_three_cost_formulas_disagree_as_the_original_does()
    {
        // Cost 6, mastered college (spell 7, college 1), dwarf: run time 3 → 3 (even) × 2 = 6; cost 7: 4 × 2 = 8; UI 14 / 2 = 7.
        var master = Dwarf with { MasteredCollege = 1 };
        Assert.AreEqual((6, 8, 6, 7), (SpellCostRules.RunCost(6, 7, master, false), SpellCostRules.RunCost(7, 7, master, false),
            SpellCostRules.CastCost(6, 7, master), SpellCostRules.CastCost(7, 7, master)));
        Assert.AreEqual((14, 14), (SpellCostRules.CheckCost(7, master, false), SpellCostRules.PreAnimationCost(true, 7, 3, master, false)));
        Assert.AreEqual((6, 1, 2), (SpellCostRules.PreAnimationCost(false, 7, 3, master, false), SpellCostRules.CheckCost(9, Human, true),
            SpellCostRules.RunCost(9, 7, master, true)));
        // Mastery only for spells 0..79 at run time (a negative number's college 0 matches only in the UI cost); a non-critter
        // dwarf pays single at run time but double in the UI.
        Assert.AreEqual((9, 4), (SpellCostRules.RunCost(9, -3, Human with { MasteredCollege = 0 }, false), SpellCostRules.CastCost(9, -3, Human with { MasteredCollege = 0 })));
        Assert.AreEqual(9, SpellCostRules.RunCost(9, 7, Human, false));
        Assert.AreEqual((9, 18), (SpellCostRules.RunCost(9, 7, new(false, SpellCostRules.DwarfRace, -1), false), SpellCostRules.CastCost(9, 7, new(false, SpellCostRules.DwarfRace, -1))));
        Assert.AreEqual(unchecked(int.MaxValue * 2), SpellCostRules.RunCost(int.MaxValue, 100, Dwarf, false), "32-bit wrap");
        Assert.AreEqual((0, -1), (SpellCostRules.RunCost(-3, 7, Human with { MasteredCollege = 1 }, false), SpellCostRules.CastCost(-3, 7, Human with { MasteredCollege = 1 })));
    }

    [TestMethod]
    public void Maintenance_rank_points_begin_and_recharge()
    {
        Assert.AreEqual((10, 20), SpellCostRules.MaintainCost(5, 10, 7, Dwarf with { MasteredCollege = 1 }));
        Assert.AreEqual((5, 10), SpellCostRules.MaintainCost(5, 10, 7, Human));
        Assert.AreEqual(1, SpellCostRules.RankPointCost(42));
        Assert.IsTrue(SpellCostRules.BeginPays(freeRun: false, critterParent: true, technology: false, skipsBeginCost: false));
        Assert.IsFalse(SpellCostRules.BeginPays(true, true, false, false));
        Assert.IsFalse(SpellCostRules.BeginPays(false, false, false, false));
        Assert.IsFalse(SpellCostRules.BeginPays(false, true, true, false));
        Assert.IsFalse(SpellCostRules.BeginPays(false, true, false, true));
        Assert.IsTrue(SpellCostRules.PreAnimationChecked(false, true));
        Assert.IsFalse(SpellCostRules.PreAnimationChecked(true, true) || SpellCostRules.PreAnimationChecked(false, false));
        // 2 × max(1, elapsed / 120), capped at what is left.
        Assert.AreEqual((2, 8), SpellCostRules.Recharge(0, 10));
        Assert.AreEqual((8, 2), SpellCostRules.Recharge(480, 10));
        Assert.AreEqual((10, 0), SpellCostRules.Recharge(100_000, 10));
        Assert.AreEqual((2, 8), SpellCostRules.Recharge(-500, 10));
    }

    [TestMethod]
    public void Fatigue_pays_the_rest_and_refuses_at_minus_15()
    {
        var cells = new Cells(true);
        var paid = SpellPaymentRules.Pay(Critter(20, 5), 10, apply: true, 3, Timed, cells);
        Assert.AreEqual((SpellPaymentStatus.Paid, (int?)10, 10, 0), (paid.Status, paid.LastCost, paid.FatigueDamageAdded, paid.Drains.Count));
        // 5 − 20 = −15 is refused; −14 is paid and, timed, lands although fatigue is negative.
        Assert.AreEqual(SpellPaymentStatus.FatigueRefused, SpellPaymentRules.Pay(Critter(5), 20, true, 3, Timed, cells).Status);
        Assert.AreEqual(SpellPaymentStatus.Paid, SpellPaymentRules.Pay(Critter(5), 19, true, 3, Timed, cells).Status);
        // Maintained or untimed: the cast fails after paying when fatigue is now below 0, not at 0.
        var failed = SpellPaymentRules.Pay(Critter(5), 6, true, 3, Maintained, cells);
        Assert.AreEqual((SpellPaymentStatus.FailedAfterPay, 6), (failed.Status, failed.FatigueDamageAdded));
        Assert.AreEqual(SpellPaymentStatus.FailedAfterPay, SpellPaymentRules.Pay(Critter(5), 6, true, 3, Untimed, cells).Status);
        Assert.AreEqual(SpellPaymentStatus.Paid, SpellPaymentRules.Pay(Critter(5), 5, true, 3, Untimed, cells).Status);
        // A check (not applied) never fails after paying and adds no damage.
        var check = SpellPaymentRules.Pay(Critter(5), 6, false, 3, Maintained, cells);
        Assert.AreEqual((SpellPaymentStatus.Paid, 0), (check.Status, check.FatigueDamageAdded));
        // No record (spell 10000): no fail-after-pay and no cells.
        Assert.AreEqual(SpellPaymentStatus.Paid, SpellPaymentRules.Pay(Critter(5), 6, true, SpellPaymentRules.NoSpell, new(true, 10, 0), cells).Status);
        Assert.AreEqual(0, cells.Requests.Count);
    }

    [TestMethod]
    public void A_fatigue_limiting_critter_pays_half_and_worn_stores_pay_first()
    {
        var cells = new Cells(true);
        Assert.AreEqual(5, SpellPaymentRules.Pay(Critter(20, limiting: true), 11, true, 3, Timed, cells).FatigueDamageAdded);
        Assert.AreEqual(1, SpellPaymentRules.Pay(Critter(20, limiting: true), 1, true, 3, Timed, cells).FatigueDamageAdded);
        // Slots in order; an emptied store is reported; a store after the cost is covered is "drained" by 0, as the original writes it.
        var paid = SpellPaymentRules.Pay(Critter(20, 0, false, 0, 3, 0, 10, 4), 8, true, 3, Timed, cells);
        CollectionAssert.AreEqual(new[] { new ManaStoreDrain(1, 3, 0), new ManaStoreDrain(3, 5, 5), new ManaStoreDrain(4, 0, 4) }, paid.Drains.ToArray());
        Assert.AreEqual((0, true, false), (paid.FatigueDamageAdded, paid.Drains[0].Emptied, paid.Drains[1].Emptied));
        // MG20: the stores are drained before the fatigue refusal.
        var refused = SpellPaymentRules.Pay(Critter(0, 0, false, 3), 20, true, 3, Timed, cells);
        Assert.AreEqual((SpellPaymentStatus.FatigueRefused, 1), (refused.Status, refused.Drains.Count));
        // A check drains nothing, but the stores still lower what fatigue must cover.
        var check = SpellPaymentRules.Pay(Critter(0, 0, false, 3), 17, false, 3, Timed, cells);
        Assert.AreEqual((SpellPaymentStatus.Paid, 0), (check.Status, check.Drains.Count));
        Assert.ThrowsExactly<ArgumentException>(() => SpellPaymentRules.Pay(Critter(5, 0, false, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1), 1, true, 3, Timed, cells));
        // A negative store adds to the rest and is emptied, also after the cost was covered.
        var negative = SpellPaymentRules.Pay(Critter(20, 0, false, 5, -3), 4, true, 3, Timed, cells);
        CollectionAssert.AreEqual(new[] { new ManaStoreDrain(0, 4, 1), new ManaStoreDrain(1, -3, 0) }, negative.Drains.ToArray());
        Assert.AreEqual((SpellPaymentStatus.Paid, 3, true), (negative.Status, negative.FatigueDamageAdded, negative.Drains[1].Emptied));
        // A check is refused at −15 too, writing nothing.
        var refusedCheck = SpellPaymentRules.Pay(Critter(0, 0, false, 3), 20, false, 3, Timed, cells);
        Assert.AreEqual((SpellPaymentStatus.FatigueRefused, 0), (refusedCheck.Status, refusedCheck.Drains.Count));
    }

    [TestMethod]
    public void Charge_cells_come_first_and_alone_even_in_a_check()
    {
        var paying = new Cells(true, 7);
        var record = new SpellPaymentRecord(true, 10, 0);
        var paid = SpellPaymentRules.Pay(Critter(0, 0, false, 30), 10, apply: false, 168, record, paying);
        Assert.AreEqual((SpellPaymentStatus.Paid, 0, 0, 7), (paid.Status, paid.Drains.Count, paid.FatigueDamageAdded, paid.Cells!.Cells));
        Assert.AreEqual(new SpellCellRequest(SpellPayerKind.Critter, 10, false), paying.Requests.Single());
        Assert.AreEqual(SpellPaymentStatus.CellsRefused, SpellPaymentRules.Pay(new(SpellPayerKind.Item, ItemCharges: 5), 10, true, 168, record, new Cells(false)).Status);
        // Nothing to pay, or no payer, asks no cells.
        var idle = new Cells(true);
        SpellPaymentRules.Pay(Critter(10), 0, true, 168, record, idle);
        Assert.AreEqual(SpellPaymentStatus.Paid, SpellPaymentRules.Pay(new(SpellPayerKind.None), 10, true, 168, record, idle).Status);
        Assert.AreEqual(0, idle.Requests.Count);
    }

    [TestMethod]
    public void Item_sources_other_objects_and_bounds()
    {
        var cells = new Cells(true);
        var item = SpellPaymentRules.Pay(new(SpellPayerKind.Item, ItemCharges: 2), 5, true, 3, Timed, cells);
        Assert.AreEqual((SpellPaymentStatus.Paid, true, true), (item.Status, item.ChargeSpent, item.MarksItemMagical));
        var check = SpellPaymentRules.Pay(new(SpellPayerKind.Item, ItemCharges: 2), 5, false, 3, Timed, cells);
        Assert.AreEqual((false, true), (check.ChargeSpent, check.MarksItemMagical), "the item is marked magical before the apply test");
        Assert.AreEqual(SpellPaymentStatus.NoCharges, SpellPaymentRules.Pay(new(SpellPayerKind.Item, ItemCharges: 0), 5, true, 3, Timed, cells).Status);
        var unlimited = SpellPaymentRules.Pay(new(SpellPayerKind.Item, ItemCharges: -1), 5, true, 3, Timed, cells);
        Assert.AreEqual((SpellPaymentStatus.Paid, false, false), (unlimited.Status, unlimited.ChargeSpent, unlimited.MarksItemMagical));
        Assert.AreEqual((SpellPaymentStatus.Paid, (int?)5), (SpellPaymentRules.Pay(new(SpellPayerKind.Other), 5, true, 3, Timed, cells).Status,
            SpellPaymentRules.Pay(new(SpellPayerKind.None), 5, true, 3, Timed, cells).LastCost));
        Assert.IsNull(SpellPaymentRules.Pay(Critter(10), -2, true, 3, Timed, cells).LastCost);
        Assert.AreEqual(SpellPaymentStatus.SpellOutOfRange, SpellPaymentRules.Pay(Critter(10), 5, true, 223, Timed, cells).Status);
        Assert.AreEqual(SpellPaymentStatus.SpellOutOfRange, SpellPaymentRules.Pay(Critter(10), 5, true, -1, Timed, cells).Status);
        var notSet = SpellPaymentRules.Pay(Critter(10, -9), 5, true, 3, Timed, cells);
        Assert.AreEqual((SpellPaymentStatus.FatigueNotSet, 9), (notSet.Status, notSet.FatigueDamageAdded), "the damage is written as 0");
        Assert.ThrowsExactly<ArgumentNullException>(() => SpellPaymentRules.Pay(null!, 5, true, 3, Timed, cells));
        Assert.ThrowsExactly<ArgumentNullException>(() => SpellPaymentRules.Pay(Critter(1), 5, true, 3, Timed, null!));
    }
}

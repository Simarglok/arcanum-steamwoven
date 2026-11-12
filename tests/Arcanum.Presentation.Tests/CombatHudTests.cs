using Arcanum.Presentation.Hud;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

/// <summary>ARC-UI-1114: the action-point lamps and the turn-order list rules against the behaviour recorded in
/// docs/research/ledger/ui-audio.md CH2–CH6; values invented, expected results worked by hand.</summary>
[TestClass]
public sealed class CombatHudTests
{
    [TestMethod]
    public void Covered_cost_lights_the_points_left_first_and_the_cost_after_them()
    {
        Assert.AreEqual(new ActionPointRules.Shares(7, 3, 0), ActionPointRules.LampShares(10, 3), "10 points, cost 3: 7 left, then 3 of cost");
        Assert.AreEqual(new ActionPointRules.Shares(0, 4, 0), ActionPointRules.LampShares(4, 4), "a cost equal to the points leaves none");
        Assert.AreEqual(new ActionPointRules.Shares(5, 0, 0), ActionPointRules.LampShares(5, 0), "no pending cost: the points only");
    }

    [TestMethod]
    public void More_than_twenty_lamps_show_what_fits_and_the_cost_fills_what_remains()
    {
        Assert.AreEqual(new ActionPointRules.Shares(20, 0, 0), ActionPointRules.LampShares(30, 3), "27 left lamps cap at 20, nothing remains for the cost");
        Assert.AreEqual(new ActionPointRules.Shares(17, 3, 0), ActionPointRules.LampShares(22, 5), "17 left, the cost fills the remaining 3");
        Assert.AreEqual(new ActionPointRules.Shares(0, 20, 0), ActionPointRules.LampShares(25, 30), "the cost's lamps cap at 20, no shortfall fits");
    }

    [TestMethod]
    public void A_cost_beyond_the_points_lights_the_cost_then_the_shortfall()
    {
        Assert.AreEqual(new ActionPointRules.Shares(0, 3, 2), ActionPointRules.LampShares(3, 5), "3 points cover 3 of 5: 3 cost lamps, 2 short");
        Assert.AreEqual(new ActionPointRules.Shares(0, 18, 2), ActionPointRules.LampShares(18, 40), "the shortfall fills only what remains of the 20");
    }

    [TestMethod]
    public void No_points_light_nothing_and_a_negative_cost_counts_as_none()
    {
        Assert.AreEqual(default, ActionPointRules.LampShares(0, 5));
        Assert.AreEqual(default, ActionPointRules.LampShares(-2, 5));
        Assert.AreEqual(new ActionPointRules.Shares(5, 0, 0), ActionPointRules.LampShares(5, -3));
        Assert.AreEqual(new ActionPointRules.Shares(20, 0, 0), ActionPointRules.LampShares(int.MaxValue, int.MinValue + 1), "no overflow");
    }

    [TestMethod]
    public void The_lamps_run_left_to_right_in_the_three_arts()
    {
        var lamps = ActionPointRules.Lamps(3, 5);
        Assert.AreEqual(ActionPointRules.LampCount, lamps.Length);
        CollectionAssert.AreEqual(new[] { ActionPointLamp.Cost, ActionPointLamp.Cost, ActionPointLamp.Cost, ActionPointLamp.Shortfall, ActionPointLamp.Shortfall, ActionPointLamp.Off },
            lamps.Take(6).ToArray());
        Assert.IsTrue(lamps.Skip(5).All(l => l == ActionPointLamp.Off));
        var covered = ActionPointRules.Lamps(10, 3);
        Assert.AreEqual(7, covered.Count(l => l == ActionPointLamp.Left));
        Assert.AreEqual(ActionPointLamp.Cost, covered[7]);
        Assert.AreEqual(ActionPointLamp.Cost, covered[9]);
        Assert.AreEqual(ActionPointLamp.Off, covered[10]);
    }

    [TestMethod]
    public void Three_lights_run_seven_lamps_apart_one_step_every_hundred_milliseconds()
    {
        CollectionAssert.AreEqual(new[] { 0, 13, 7 }, ActionPointRules.CueLamps(0), "i, i − 7 and i + 7 round the 20");
        CollectionAssert.AreEqual(new[] { 3, 16, 10 }, ActionPointRules.CueLamps(3));
        CollectionAssert.AreEqual(new[] { 5, 18, 12 }, ActionPointRules.CueLamps(25), "the index wraps round the 20");
        CollectionAssert.AreEqual(new[] { 19, 12, 6 }, ActionPointRules.CueLamps(-1));
        Assert.AreEqual(0, ActionPointRules.CueIndex(99));
        Assert.AreEqual(1, ActionPointRules.CueIndex(100));
        Assert.AreEqual(19, ActionPointRules.CueIndex(1999));
        Assert.AreEqual(0, ActionPointRules.CueIndex(2000), "a round of the 20 lamps takes 2 s");
        Assert.AreEqual(0, ActionPointRules.CueIndex(-5));
        Assert.AreEqual(ActionPointRules.CueLights, ActionPointRules.CueLampStates(4).Count(l => l == ActionPointLamp.Left));
    }

    [TestMethod]
    public void Lamps_stand_thirteen_pixels_apart_with_a_gap_between_groups_of_five()
    {
        Assert.AreEqual(0, ActionPointRules.LampX(0, 4));
        Assert.AreEqual(52, ActionPointRules.LampX(4, 4), "within the first group");
        Assert.AreEqual(69, ActionPointRules.LampX(5, 4), "5 × 13 plus one group gap");
        Assert.AreEqual(259, ActionPointRules.LampX(19, 4), "19 × 13 plus three gaps");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ActionPointRules.LampX(20, 4));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ActionPointRules.LampX(-1, 4));
    }

    [TestMethod]
    public void A_short_turn_order_lists_everyone_in_round_order_with_the_current_one_marked()
    {
        var (rows, hidden) = TurnOrderRules.Window(3, 1);
        Assert.AreEqual(0, hidden);
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, rows.Select(r => r.Index).ToArray());
        CollectionAssert.AreEqual(new[] { false, true, false }, rows.Select(r => r.Current).ToArray());
        Assert.AreEqual(0, TurnOrderRules.Window(0, 0).Rows.Count, "no participants, no rows");
    }

    [TestMethod]
    public void A_long_turn_order_shows_a_window_from_the_current_one_and_counts_the_rest()
    {
        var (rows, hidden) = TurnOrderRules.Window(20, 18);
        Assert.AreEqual(12, hidden);
        CollectionAssert.AreEqual(new[] { 18, 19, 0, 1, 2, 3, 4, 5 }, rows.Select(r => r.Index).ToArray(), "from the current one on, wrapping round");
        Assert.IsTrue(rows[0].Current);
        Assert.AreEqual(1, rows.Count(r => r.Current));
        Assert.AreEqual(0, TurnOrderRules.Window(8, 7).Hidden, "exactly the most rows fits whole");
        Assert.AreEqual(1, TurnOrderRules.Window(9, 0).Hidden);
    }

    [TestMethod]
    public void The_turn_order_refuses_a_current_participant_outside_the_round()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TurnOrderRules.Window(3, 3));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TurnOrderRules.Window(3, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TurnOrderRules.Window(-1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TurnOrderRules.Window(3, 0, 0));
    }
}

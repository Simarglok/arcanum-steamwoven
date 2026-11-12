using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>ARC-UI-1114: the cost the hover previews (ledger CH4, 0x557370) per action kind; values worked by hand from the original's
/// costs (a step 2, 1 for an always-running PC; use 2; pickup 1; cast 4; an attack plus the approach for a reach-1 weapon).</summary>
[TestClass]
public sealed class AreaActionPointPreviewTests
{
    [TestMethod]
    public void A_walk_costs_two_a_step_or_one_for_an_always_running_pc()
    {
        Assert.AreEqual(8, AreaActionPointPreview.Cost(AreaHoverAction.Walk, 4, 3, 1, false));
        Assert.AreEqual(4, AreaActionPointPreview.Cost(AreaHoverAction.Walk, 4, 3, 1, true));
        Assert.AreEqual(0, AreaActionPointPreview.Cost(AreaHoverAction.Walk, 0, 3, 1, false), "the hero's own cell costs nothing");
    }

    [TestMethod]
    public void Using_an_object_costs_two_plus_the_walk_to_a_cell_next_to_it()
    {
        Assert.AreEqual(2, AreaActionPointPreview.Cost(AreaHoverAction.Use, 0, 3, 1, false), "on its cell");
        Assert.AreEqual(2, AreaActionPointPreview.Cost(AreaHoverAction.Use, 1, 3, 1, false), "already adjacent");
        Assert.AreEqual(6, AreaActionPointPreview.Cost(AreaHoverAction.WalkToUse, 3, 3, 1, false), "2 steps to an adjacent cell");
        Assert.AreEqual(4, AreaActionPointPreview.Cost(AreaHoverAction.WalkToLoot, 3, 3, 1, true), "the run halves the steps' cost");
        Assert.AreEqual(2, AreaActionPointPreview.Cost(AreaHoverAction.Loot, 1, 3, 1, false));
    }

    [TestMethod]
    public void Picking_an_item_up_costs_one_plus_the_walk_to_its_cell()
    {
        Assert.AreEqual(1, AreaActionPointPreview.Cost(AreaHoverAction.PickUp, 0, 3, 1, false));
        Assert.AreEqual(5, AreaActionPointPreview.Cost(AreaHoverAction.WalkToPickUp, 2, 3, 1, false), "1 plus 2 steps of 2");
    }

    [TestMethod]
    public void An_attack_adds_the_approach_only_for_a_weapon_that_reaches_one_cell()
    {
        Assert.AreEqual(7, AreaActionPointPreview.Cost(AreaHoverAction.WalkToAttack, 3, 3, 1, false), "attack 3 plus 2 steps of 2 to an adjacent cell");
        Assert.AreEqual(3, AreaActionPointPreview.Cost(AreaHoverAction.Attack, 1, 3, 1, false), "adjacent: the attack alone");
        Assert.AreEqual(3, AreaActionPointPreview.Cost(AreaHoverAction.Attack, 5, 3, 8, false), "a ranged weapon does not walk first");
        Assert.AreEqual(5, AreaActionPointPreview.Cost(AreaHoverAction.WalkToAttack, 3, 3, 0, true), "unarmed (reach 0) walks: attack 3 plus 2 steps of 1");
    }

    [TestMethod]
    public void A_cast_costs_four_and_an_action_that_runs_no_check_previews_nothing()
    {
        Assert.AreEqual(4, AreaActionPointPreview.Cost(AreaHoverAction.Cast, 0, 3, 1, false));
        foreach (var none in new[] { AreaHoverAction.None, AreaHoverAction.NoRoute, AreaHoverAction.Talk, AreaHoverAction.WalkToTalk, AreaHoverAction.CastNoTarget })
            Assert.IsNull(AreaActionPointPreview.Cost(none, 2, 3, 1, false), none.ToString());
    }

    [TestMethod]
    public void A_far_target_counts_as_the_paths_bound_and_a_negative_input_is_refused()
    {
        Assert.AreEqual(20_000, AreaActionPointPreview.Cost(AreaHoverAction.Walk, int.MaxValue, 3, 1, false), "10 000 steps of 2");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaActionPointPreview.Cost(AreaHoverAction.Walk, -1, 3, 1, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaActionPointPreview.Cost(AreaHoverAction.Attack, 1, -1, 1, false));
    }
}

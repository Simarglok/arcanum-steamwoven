using Arcanum.Simulation.Goals;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-304 (GT115): the click on a dead critter resolves to the talk (goal 21) when the body carries the spoken-with-dead spell flag and to the loot (goal 81) otherwise; the
/// pick-pocket start's status and the hover cursor say what the click resolves to.
/// </summary>
[TestClass]
public sealed class AreaSpokenBodyClickTests
{
    [TestMethod]
    public void The_pick_pocket_loot_click_status_names_the_goal_the_click_resolved_to()
    {
        Assert.AreEqual("Rat: walking to loot, goal 81 started", AreaPickPocket.LootStatus("Rat", new(HeroRequestOutcome.Done, Click: HeroClickGoal.UseContainer)));
        Assert.AreEqual("Rat: walking to talk, goal 21 started", AreaPickPocket.LootStatus("Rat", new(HeroRequestOutcome.Done, Click: HeroClickGoal.Talk)));
        Assert.AreEqual("Rat: the loot order was refused", AreaPickPocket.LootStatus("Rat", new(HeroRequestOutcome.Refused, Click: HeroClickGoal.UseContainer)));
        Assert.AreEqual("Rat: the talk order was refused", AreaPickPocket.LootStatus("Rat", new(HeroRequestOutcome.Refused, Click: HeroClickGoal.Talk)));
    }

    [TestMethod]
    public void The_cursor_over_a_body_follows_the_click()
    {
        // Loot from the next cell (reach 1); talk from less than 5 cells (the hero's talk range).
        Assert.AreEqual(AreaHoverAction.Loot, AreaHover.Body(false, 1, 1, 5));
        Assert.AreEqual(AreaHoverAction.WalkToLoot, AreaHover.Body(false, 2, 1, 5));
        Assert.AreEqual(AreaHoverAction.Talk, AreaHover.Body(true, 4, 1, 5));
        Assert.AreEqual(AreaHoverAction.WalkToTalk, AreaHover.Body(true, 5, 1, 5));
    }

    [TestMethod]
    public void A_body_with_the_flag_resolves_to_the_talk_and_one_without_to_the_loot()
    {
        var plain = new HeroClickFacts(true, false, false, false, false, HeroClickTarget.Critter, TargetDead: true);
        Assert.AreEqual(HeroClickGoal.UseContainer, HeroClickRules.Decide(plain).Goal);
        Assert.AreEqual(HeroClickGoal.Talk, HeroClickRules.Decide(plain with { TargetTalksDead = true }).Goal);
    }
}

using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Skills;

// ARC-COMBAT-1777 (ledger PL5, OH4): the best-follower chooser (0x4C9050) and the all-followers list (0x441260), as pure rules. The names are invented.
[TestClass]
public sealed class FollowerSkillRulesTests
{
    [TestMethod]
    public void The_pc_keeps_the_job_unless_a_follower_scores_strictly_higher_and_ties_keep_the_earlier_one()
    {
        Assert.AreEqual(0, FollowerSkillRules.Choose([40]), "no followers");
        Assert.AreEqual(0, FollowerSkillRules.Choose([40, 40, 39]), "a tie keeps the PC");
        Assert.AreEqual(1, FollowerSkillRules.Choose([40, 41, 41]), "a tie among followers keeps the earlier");
        Assert.AreEqual(3, FollowerSkillRules.Choose([40, 41, 41, 60, 59]), "the strictly best wins");
        Assert.AreEqual(2, FollowerSkillRules.Choose([-5, -9, -1]), "negative scores compare the same way");
        Assert.ThrowsExactly<ArgumentException>(() => FollowerSkillRules.Choose([]));
    }

    [TestMethod]
    public void The_all_followers_list_is_the_direct_followers_then_each_ones_own_list()
    {
        var map = new Dictionary<string, string[]> { ["pc"] = ["a", "b"], ["a"] = ["c"], ["c"] = ["d"], ["b"] = ["e"] };
        CollectionAssert.AreEqual(new[] { "a", "b", "c", "d", "e" }, FollowerSkillRules.AllFollowers("pc", id => map.GetValueOrDefault(id) ?? []).ToArray());
        Assert.AreEqual(0, FollowerSkillRules.AllFollowers("pc", null).Count);
    }

    [TestMethod]
    public void A_cycle_or_a_repeated_follower_is_listed_once_and_a_deep_chain_is_cut()
    {
        var cycle = new Dictionary<string, string[]> { ["pc"] = ["a"], ["a"] = ["b", "pc"], ["b"] = ["a"] };
        CollectionAssert.AreEqual(new[] { "a", "b" }, FollowerSkillRules.AllFollowers("pc", id => cycle.GetValueOrDefault(id) ?? []).ToArray());
        Assert.AreEqual(FollowerSkillRules.MaxDepth, FollowerSkillRules.AllFollowers("n0", id => [$"n{int.Parse(id[1..]) + 1}"]).Count);
    }

    [TestMethod]
    public void The_hand_over_line_range_is_4700_to_4799()
    {
        Assert.AreEqual((4700, 4799), (FollowerSkillRules.HandOverLines.First, FollowerSkillRules.HandOverLines.Last));
    }

    [TestMethod]
    public void The_score_is_effectiveness_minus_difficulty_and_an_impossible_target_scores_very_low()
    {
        var request = new SkillCheckRequest { Skill = Skill.PickLocks, Level = 10, Difficulty = new() { TargetLockable = true, LockDifficulty = 20, LockpickBonus = 5 } };
        Assert.AreEqual(60 - (20 - 5), SkillCheckRules.Score(request));
        Assert.AreEqual(60 - 20, SkillCheckRules.Score(request with { Difficulty = request.Difficulty with { LockpickBonus = 0 } }));
        Assert.IsTrue(SkillCheckRules.Score(request with { Difficulty = request.Difficulty with { TargetLockable = false } }) < -900_000, "not lockable is impossible");
    }
}

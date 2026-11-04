using Arcanum.Simulation.Party;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Party;

// ARC-DEV-073 (MIG10): the move list of a PC's change of place (research FM1, FM2, RL22, RL23). Entities and leaders are invented.
[TestClass]
public sealed class TransferMoveListRulesTests
{
    private static IReadOnlyList<string> Below(Dictionary<string, string[]> tree, string npc) => tree.GetValueOrDefault(npc) ?? [];

    [TestMethod]
    public void Every_follower_travels_when_none_is_unconscious()
    {
        var list = TransferMoveListRules.Build(["a", "b", "c"], _ => false, null, noWaitOrders: false);
        Assert.AreEqual(0, list.Waiting.Count);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, list.Travelling.ToArray());
    }

    [TestMethod]
    public void An_unconscious_follower_is_ordered_to_wait_and_stays_behind()
    {
        var list = TransferMoveListRules.Build(["a", "b", "c"], npc => npc == "b", null, noWaitOrders: false);
        CollectionAssert.AreEqual(new[] { "b" }, list.Waiting.ToArray());
        CollectionAssert.AreEqual(new[] { "a", "c" }, list.Travelling.ToArray());
    }

    [TestMethod]
    public void The_no_wait_orders_flag_takes_the_unconscious_along()
    {
        var list = TransferMoveListRules.Build(["a", "b"], npc => npc == "b", null, noWaitOrders: true);
        Assert.AreEqual(0, list.Waiting.Count);
        CollectionAssert.AreEqual(new[] { "a", "b" }, list.Travelling.ToArray());
    }

    [TestMethod]
    public void Followers_of_followers_travel_and_a_node_is_listed_after_its_followers()
    {
        var tree = new Dictionary<string, string[]> { ["a"] = ["a1", "a2"], ["a1"] = ["a1x"], ["b"] = ["b1"] };
        var list = TransferMoveListRules.Build(["a", "b"], _ => false, npc => Below(tree, npc), noWaitOrders: false);
        CollectionAssert.AreEqual(new[] { "a1x", "a1", "a2", "a", "b1", "b" }, list.Travelling.ToArray());
    }

    [TestMethod]
    public void Only_a_direct_follower_is_ordered_to_wait_and_the_unconscious_one_below_a_leader_travels()
    {
        var tree = new Dictionary<string, string[]> { ["a"] = ["a1"] };
        var list = TransferMoveListRules.Build(["a", "b"], npc => npc is "b" or "a1", npc => Below(tree, npc), noWaitOrders: false);
        CollectionAssert.AreEqual(new[] { "b" }, list.Waiting.ToArray());
        CollectionAssert.AreEqual(new[] { "a1", "a" }, list.Travelling.ToArray(), "a1 is not a direct follower: no wait order");
    }

    [TestMethod]
    public void A_follower_ordered_to_wait_is_not_walked_below_another_leader_either()
    {
        var tree = new Dictionary<string, string[]> { ["a"] = ["b"] };
        var list = TransferMoveListRules.Build(["a", "b"], npc => npc == "b", npc => Below(tree, npc), noWaitOrders: false);
        CollectionAssert.AreEqual(new[] { "a" }, list.Travelling.ToArray());
    }

    [TestMethod]
    public void A_cycle_or_a_repeated_follower_is_listed_once_and_a_deep_chain_is_cut()
    {
        var cycle = new Dictionary<string, string[]> { ["a"] = ["b"], ["b"] = ["a", "b"] };
        var list = TransferMoveListRules.Build(["a", "b"], _ => false, npc => Below(cycle, npc), noWaitOrders: false);
        CollectionAssert.AreEqual(new[] { "b", "a" }, list.Travelling.ToArray());
        var chain = TransferMoveListRules.Build(["n0"], _ => false, npc => [$"n{int.Parse(npc[1..]) + 1}"], noWaitOrders: false);
        Assert.AreEqual(TransferMoveListRules.MaxDepth + 1, chain.Travelling.Count);
        Assert.AreEqual("n0", chain.Travelling[^1]);
    }

    [TestMethod]
    public void No_followers_give_an_empty_list()
    {
        var list = TransferMoveListRules.Build([], _ => throw new AssertFailedException("no follower to ask"), null, noWaitOrders: false);
        Assert.AreEqual(0, list.Waiting.Count + list.Travelling.Count);
    }
}

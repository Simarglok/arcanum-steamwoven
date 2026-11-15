using Arcanum.Simulation.Party;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Party;

// ARC-UI-2305 (ledger OL2, OL3): the typed order text's keyword lookup and receivers. Words and names are invented.
[TestClass]
public sealed class BroadcastKeywordsTests
{
    private static readonly string[] TypeNames = ["none", "leave", "wait", "follow", "move", "close", "spread", "curse", "join", "disband", "attack", "walk", "stop", "other"];

    private static BroadcastKeywords Words() => BroadcastKeywords.Build(["hold", "come", "stay", "scatter", "come"], TypeNames, ["wait", "follow", "close", "spread", "other"]);

    [TestMethod]
    public void A_keyword_takes_the_type_whose_name_its_rules_entry_gives()
    {
        var words = Words();
        Assert.AreEqual(5, words.Count);
        Assert.AreEqual(CompanionOrderType.Wait, words.Lookup("hold"));
        Assert.AreEqual(CompanionOrderType.Follow, words.Lookup("come"), "the first equal keyword wins");
        Assert.AreEqual(CompanionOrderType.StayClose, words.Lookup("stay"));
        Assert.AreEqual(CompanionOrderType.SpreadOut, words.Lookup("scatter"));
    }

    [TestMethod]
    public void The_lookup_skips_only_leading_spaces_and_compares_the_whole_rest_case_insensitively()
    {
        var words = Words();
        Assert.AreEqual(CompanionOrderType.Wait, words.Lookup("   HoLd"));
        foreach (var miss in new[] { "hold ", "hold on", "hold,", ": hold", "\thold", "", "hol" })
            Assert.AreEqual(CompanionOrderType.None, words.Lookup(miss), $"'{miss}'");
    }

    [TestMethod]
    public void A_type_name_that_matches_none_or_a_missing_rules_entry_is_type_none()
    {
        var words = BroadcastKeywords.Build(["a", "b", "c"], TypeNames, ["Leave", "nonsense"]);
        Assert.AreEqual(CompanionOrderType.Leave, words.Lookup("a"), "the name compare is case-insensitive");
        Assert.AreEqual(CompanionOrderType.None, words.Lookup("b"));
        Assert.AreEqual(CompanionOrderType.None, words.Lookup("c"));
    }

    private static string Show(IEnumerable<(string Entity, CompanionOrderType Type)> receivers) => string.Join(' ', receivers.Select(r => $"{r.Entity}:{r.Type}"));

    private static CompanionOrderCandidate Follower(string entity, string name, long distance = 3) => new(entity, name, distance);

    [TestMethod]
    public void A_text_with_a_name_reaches_the_first_follower_in_reach_whose_name_prefixes_it_and_only_that_one()
    {
        var words = Words();
        CompanionOrderCandidate[] followers = [Follower("a", "Wolf"), Follower("b", "Wolfgang"), Follower("c", "Bear", 11)];
        Assert.AreEqual("a:None", Show(CompanionOrderRules.TypedReceivers(followers, "Wolfgang hold", words)),
            "the first follower whose name prefixes the text: the rest 'gang hold' is no order word");
        Assert.AreEqual("a:Wait", Show(CompanionOrderRules.TypedReceivers(followers, "wolf hold", words)));
        Assert.AreEqual(0, CompanionOrderRules.TypedReceivers(followers, "Bear hold", words).Count, "farther than 10 tiles is out of reach");
        Assert.AreEqual(0, CompanionOrderRules.TypedReceivers(followers, " Wolf hold", words).Count, "leading spaces before the name do not match");
        Assert.AreEqual(0, CompanionOrderRules.TypedReceivers(followers, "Wol", words).Count);
    }

    [TestMethod]
    public void A_hash_reaches_every_follower_in_reach_in_array_order_with_the_word_after_it()
    {
        var words = Words();
        CompanionOrderCandidate[] followers = [Follower("a", "Wolf"), Follower("b", "Bear", 11), Follower("c", "Fox", 10)];
        Assert.AreEqual("a:Wait c:Wait", Show(CompanionOrderRules.TypedReceivers(followers, "#hold", words)));
        Assert.AreEqual("a:Wait c:Wait", Show(CompanionOrderRules.TypedReceivers(followers, "#  hold", words)));
        Assert.AreEqual("a:None c:None", Show(CompanionOrderRules.TypedReceivers(followers, "# hold on", words)));
    }
}

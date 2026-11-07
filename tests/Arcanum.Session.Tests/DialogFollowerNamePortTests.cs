using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The dialog pa condition over the session party (ARC-DEV-227, ledger PN1–PN3). Entities and numbers are invented.
[TestClass]
public sealed class DialogFollowerNamePortTests
{
    private static readonly DialogParticipantId Pc = new(800), Other = new(801);

    private static SessionDialogFollowerNamePort Port(GameSessionOwner owner, Dictionary<string, (int Name, bool MindControlled)?> facts, List<string>? read = null) =>
        new(owner, Pc, entity => { read?.Add(entity); return facts.TryGetValue(entity, out var found) ? found : null; });

    [TestMethod]
    public void Only_a_follower_that_is_not_mind_controlled_counts_and_a_non_follower_does_not()
    {
        using var owner = Create();
        var facts = new Dictionary<string, (int Name, bool MindControlled)?> { ["virgil"] = (6014, false), ["priest"] = (8, true) };
        var port = Port(owner, facts);
        Assert.IsFalse(port.HasFollowerNamed(new(Pc, 6014)), "virgil is not a follower yet");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "virgil").Disposition);
        Assert.IsTrue(port.HasFollowerNamed(new(Pc, 6014)));
        Assert.IsFalse(port.HasFollowerNamed(new(Pc, 8)), "the name of someone who is not a follower");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "priest").Disposition);
        Assert.IsFalse(port.HasFollowerNamed(new(Pc, 8)), "a mind-controlled follower is not counted");
        facts["priest"] = (8, false);
        Assert.IsTrue(port.HasFollowerNamed(new(Pc, 8)));
    }

    [TestMethod]
    public void A_waiting_npc_is_not_a_follower()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "virgil").Disposition);
        var port = Port(owner, new() { ["virgil"] = (6014, false) });
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, "virgil").Disposition);
        Assert.IsFalse(port.HasFollowerNamed(new(Pc, 6014)));
    }

    [TestMethod]
    public void The_search_stops_at_the_first_match_and_an_unreadable_follower_or_another_pc_throws()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "virgil").Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "priest").Disposition);
        var read = new List<string>();
        var port = Port(owner, new() { ["virgil"] = (1, false) }, read);
        Assert.IsTrue(port.HasFollowerNamed(new(Pc, 1)));
        Assert.AreEqual(1, read.Count, "the loop ends at the match, as the source's does");
        Assert.ThrowsExactly<InvalidOperationException>(() => port.HasFollowerNamed(new(Pc, 2)), "the second follower has no readable record");
        Assert.ThrowsExactly<ArgumentException>(() => port.HasFollowerNamed(new(Other, 1)));
    }
}

using Arcanum.Simulation.Reaction;
using Arcanum.Simulation.Reputation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Reputation;

/// <summary>ARC-WORLD-044: the reputation rules as read in the retail executable (ledger RP4–RP7): the reaction sum, the
/// faction grant, faction-same and origin-same, each input varied alone over an invented catalogue.</summary>
[TestClass]
public sealed class ReputationRulesTests
{
    private static readonly ReputationCatalog Catalog = ReputationCatalog.Create([
        new(1000, [12, 0, 30], [new(7, 0, 0)]),
        new(1001, [], [new(-3, 5, 0), new(-4, 0, 8), new(-6, 5, 8), new(100, 6, 0)]),
        new(1002, [0, 0, 0], [new(1, 0, 0), new(1, 0, 0)]),
    ])!;

    [TestMethod]
    public void The_sum_adds_every_effect_whose_origin_and_faction_are_any_or_the_npcs()
    {
        Assert.AreEqual(0, ReputationRules.ReactionSum([], Catalog, 5, 8));
        Assert.AreEqual(7, ReputationRules.ReactionSum([1000], Catalog, 5, 8), "any/any reaches every NPC");
        Assert.AreEqual(0, ReputationRules.ReactionSum([1001], Catalog, 0, 0), "an NPC of origin 0 and faction 0 matches no scoped effect");
        Assert.AreEqual(-3, ReputationRules.ReactionSum([1001], Catalog, 5, 0));
        Assert.AreEqual(-4, ReputationRules.ReactionSum([1001], Catalog, 0, 8));
        Assert.AreEqual(-3 - 4 - 6, ReputationRules.ReactionSum([1001], Catalog, 5, 8), "origin and faction both have to match");
        Assert.AreEqual(100 - 4, ReputationRules.ReactionSum([1001], Catalog, 6, 8));
        Assert.AreEqual(7 - 13 + 2, ReputationRules.ReactionSum([1002, 1001, 1000], Catalog, 5, 8), "all reputations stack, a repeated effect too");
        Assert.AreEqual(0, ReputationRules.ReactionSum([1500], Catalog, 5, 8), "a number without a record has an empty one");
        Assert.AreEqual(0, ReputationRules.ReactionSum([1000], ReputationCatalog.Empty, 0, 0));
    }

    [TestMethod]
    public void A_greeting_candidate_has_an_effect_in_scope_whose_sign_fits_the_category()
    {
        // 1000: +7 any/any; 1001: −3 (origin 5), −4 (faction 8), −6 (5, 8), +100 (origin 6); 1002: +1, +1; 1003: 0 any/any.
        var catalog = ReputationCatalog.Create([.. Enumerable.Range(0, 3).Select(i => Catalog.Find(1000 + i)!), new(1003, [], [new(0, 0, 0)])])!;
        CollectionAssert.AreEqual(new[] { 1000, 1002 }, Candidates([1000, 1001, 1002], catalog, 5, 8, ReactionCategory.Love).ToArray(),
            "positive effects for love; 1001's +100 is for origin 6 only");
        CollectionAssert.AreEqual(new[] { 1000, 1001, 1002 }, Candidates([1000, 1001, 1002], catalog, 6, 0, ReactionCategory.Courteous).ToArray());
        CollectionAssert.AreEqual(new[] { 1001 }, Candidates([1000, 1001, 1002], catalog, 0, 8, ReactionCategory.Suspicious).ToArray(),
            "negative effects for suspicious, dislike and hatred");
        CollectionAssert.AreEqual(Array.Empty<int>(), Candidates([1000, 1001, 1002], catalog, 0, 0, ReactionCategory.Hatred).ToArray(), "no scoped effect reaches origin 0, faction 0");
        CollectionAssert.AreEqual(Array.Empty<int>(), Candidates([1000, 1001, 1002, 1003], catalog, 5, 8, ReactionCategory.Neutral).ToArray(), "neutral never fits");
        CollectionAssert.AreEqual(Array.Empty<int>(), Candidates([1003], catalog, 0, 0, ReactionCategory.Love).ToArray(), "an adjustment of 0 never fits");
        CollectionAssert.AreEqual(new[] { 1002, 1000 }, Candidates([1002, 1500, 1001, 1000], catalog, 5, 8, ReactionCategory.Amiable).ToArray(),
            "held order; a number without a record is skipped; 1002's two fitting effects count once");
        CollectionAssert.AreEqual(new[] { 1002 }, Candidates([1002], catalog, 0, 0, ReactionCategory.Love).ToArray());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ReputationRules.GreetingCandidates([], catalog, 0, 0, (ReactionCategory)7));
    }

    private static IReadOnlyList<int> Candidates(int[] held, ReputationCatalog catalog, int origin, int faction, ReactionCategory category) =>
        ReputationRules.GreetingCandidates(held, catalog, origin, faction, category);

    [TestMethod]
    public void A_reputation_grants_its_three_factions_and_never_faction_0()
    {
        Assert.IsTrue(ReputationRules.GrantsFaction([1000], Catalog, 12));
        Assert.IsTrue(ReputationRules.GrantsFaction([1002, 1000], Catalog, 30));
        Assert.IsFalse(ReputationRules.GrantsFaction([1000], Catalog, 0), "faction 0 is none, though 1000 lists 0");
        Assert.IsFalse(ReputationRules.GrantsFaction([1001, 1002], Catalog, 12));
        Assert.IsFalse(ReputationRules.GrantsFaction([], Catalog, 12));
    }

    [TestMethod]
    public void Faction_same_follows_the_executable_table()
    {
        var pc = new FactionMember("pc", true, 0, null, null);
        var otherPc = new FactionMember("pc2", true, 0, null, null);
        FactionMember Npc(string id, int faction = 0, string? leader = null, string? pcLeader = null) => new(id, false, faction, leader, pcLeader);
        int[] held = [1000];
        bool Same(FactionMember a, FactionMember b) => ReputationRules.FactionSame(a, b, held, Catalog);

        Assert.IsFalse(Same(pc, otherPc), "two PCs never");
        Assert.IsFalse(Same(pc, pc));
        // A PC and an NPC: the NPC's PC leader is that PC, or its non-zero faction is granted; both argument orders.
        Assert.IsTrue(Same(pc, Npc("a", pcLeader: "pc")));
        Assert.IsTrue(Same(Npc("a", pcLeader: "pc"), pc));
        Assert.IsFalse(Same(pc, Npc("a", pcLeader: "pc2")));
        Assert.IsTrue(Same(pc, Npc("a", faction: 12)));
        Assert.IsTrue(Same(Npc("a", faction: 30), pc));
        Assert.IsFalse(Same(pc, Npc("a", faction: 13)));
        Assert.IsFalse(Same(pc, Npc("a")), "faction 0 is never granted");
        Assert.IsFalse(ReputationRules.FactionSame(pc, Npc("a", faction: 12), [], Catalog), "without the reputation");
        // Two NPCs.
        Assert.IsTrue(Same(Npc("a", pcLeader: "pc"), Npc("b", pcLeader: "pc")), "the same non-null PC leader");
        Assert.IsTrue(Same(Npc("a", leader: "b"), Npc("b")), "one leads the other");
        Assert.IsTrue(Same(Npc("a"), Npc("b", leader: "a")));
        Assert.IsTrue(Same(Npc("a", leader: "c"), Npc("b", leader: "c")), "the same non-null leader");
        Assert.IsTrue(Same(Npc("a", faction: 4), Npc("b", faction: 4)));
        Assert.IsFalse(Same(Npc("a"), Npc("b")), "no leader, faction 0 on both: never");
        Assert.IsFalse(Same(Npc("a", faction: 4), Npc("b", faction: 5)));
        Assert.IsFalse(Same(Npc("a", faction: 12), Npc("b", faction: 30)), "the PC's grants play no part between NPCs");
        Assert.ThrowsExactly<ArgumentNullException>(() => Same(Npc(null!), Npc("b")), "an id is required: a null one would match a null leader");
    }

    [TestMethod]
    public void Origin_same_needs_a_non_zero_origin()
    {
        Assert.IsTrue(ReputationRules.OriginSame(3, 3));
        Assert.IsFalse(ReputationRules.OriginSame(0, 0));
        Assert.IsFalse(ReputationRules.OriginSame(3, 4));
    }

    [TestMethod]
    public void The_catalogue_refuses_definitions_out_of_its_bounds()
    {
        Assert.IsNull(ReputationCatalog.Create([new(999, [], [])]));
        Assert.IsNull(ReputationCatalog.Create([new(2000, [], [])]));
        Assert.IsNull(ReputationCatalog.Create([new(1000, [], []), new(1000, [], [])]), "a repeated number");
        Assert.IsNull(ReputationCatalog.Create([new(1000, [1, 2, 3, 4], [])]));
        Assert.IsNull(ReputationCatalog.Create([new(1000, [], Enumerable.Repeat(new ReputationEffectDefinition(1, 0, 0), 6).ToArray())]));
        Assert.IsNull(ReputationCatalog.Create([new(1000, [], [new(1_000_001, 0, 0)])]));
        Assert.AreEqual(1, ReputationCatalog.Create([new(1000, [], [new(-1_000_000, 0, 0)])])!.Count);
        Assert.IsFalse(ReputationRules.IsNumber(999));
        Assert.IsTrue(ReputationRules.IsNumber(1999));
    }
}

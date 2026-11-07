using Arcanum.Scripting.Dialog;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Reputation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-WORLD-044: the PC's reputations in the session (ledger RP1–RP9): add, remove, has, the list with times, the
/// snapshot, and the reputation term of the hero's reaction facts.</summary>
[TestClass]
public sealed class ReputationStateTests
{
    private const long StartMs = 54_000_000;

    private static readonly ReputationCatalog Catalog = ReputationCatalog.Create([
        new(1001, [7, 0, 0], [new(5, 0, 0), new(-10, 3, 0)]),
        new(1002, [0, 0, 0], [new(-20, 0, 9), new(2, 3, 9)]),
        new(1003, [0, 4, 0], []),
    ])!;

    [TestMethod]
    public void Add_keeps_the_first_time_remove_shifts_and_has_reads_the_list()
    {
        using var owner = Create();
        Assert.AreEqual(0, owner.QueryReputations().Count);
        Changed(owner.AddReputation(owner.Version, 1002), owner, 1);
        Advance(owner, 60_000);
        Changed(owner.AddReputation(owner.Version, 1001), owner, 2);
        Advance(owner, 60_000);
        NoChange(owner.AddReputation(owner.Version, 1002), owner, 2);
        Changed(owner.AddReputation(owner.Version, 1999), owner, 3);
        CollectionAssert.AreEqual(new SessionReputation[] { new(1002, StartMs), new(1001, StartMs + 60_000), new(1999, StartMs + 120_000) },
            owner.QueryReputations().ToArray(), "acquisition order, each with its first time");
        Assert.IsTrue(owner.HeroHasReputation(1001));
        Assert.IsFalse(owner.HeroHasReputation(1003));
        Assert.IsFalse(owner.HeroHasReputation(1), "a number outside the range is never held");

        Changed(owner.RemoveReputation(owner.Version, 1002), owner, 4);
        CollectionAssert.AreEqual(new SessionReputation[] { new(1001, StartMs + 60_000), new(1999, StartMs + 120_000) },
            owner.QueryReputations().ToArray(), "the later entries move up with their times (RM1 not reproduced)");
        NoChange(owner.RemoveReputation(owner.Version, 1002), owner, 4);
        Changed(owner.AddReputation(owner.Version, 1002), owner, 5);
        Assert.AreEqual(new SessionReputation(1002, StartMs + 120_000), owner.QueryReputations()[^1], "gained again: a new time at the end");
    }

    [TestMethod]
    public void Out_of_range_numbers_and_stale_versions_are_typed_refusals()
    {
        using var owner = Create();
        foreach (var number in new[] { 0, 1, 999, 2000, -1001 })
        {
            Rejected(owner.AddReputation(owner.Version, number), owner, GameSessionFailure.InvalidReputation, 0);
            Rejected(owner.RemoveReputation(owner.Version, number), owner, GameSessionFailure.InvalidReputation, 0);
        }
        var stale = owner.Version;
        Changed(owner.AddReputation(owner.Version, 1000), owner, 1);
        Rejected(owner.AddReputation(stale, 1001), owner, GameSessionFailure.StaleVersion, 1);
        Rejected(owner.RemoveReputation(stale, 1000), owner, GameSessionFailure.StaleVersion, 1);
    }

    [TestMethod]
    public void The_hero_reaction_facts_carry_the_reputation_sum_for_the_npc()
    {
        using var owner = GameSessionOwner.Create(Descriptor() with { Hero = HeroStateTests.HalfElf(), Reputations = Catalog }).Owner!;
        Assert.AreEqual(0, owner.QueryHeroReactionFacts()!.ReputationAdjustment);
        Changed(owner.AddReputation(owner.Version, 1001), owner, 1);
        Changed(owner.AddReputation(owner.Version, 1002), owner, 2);
        Assert.AreEqual(5, owner.QueryHeroReactionFacts()!.ReputationAdjustment, "origin 0, faction 0: only any/any effects");
        Assert.AreEqual(5 - 10, owner.QueryHeroReactionFacts(3, 0)!.ReputationAdjustment);
        Assert.AreEqual(5 - 10 - 20 + 2, owner.QueryHeroReactionFacts(3, 9)!.ReputationAdjustment, "every matching effect of every reputation");
        Assert.AreEqual(5 - 20, owner.QueryReputationReaction(4, 9));
        Assert.IsTrue(owner.HeroGrantedFaction(7));
        Assert.IsFalse(owner.HeroGrantedFaction(4), "1003 is not held");
        Assert.IsFalse(owner.HeroGrantedFaction(0));
        // The effective reaction reads the sum (R1 term).
        var npc = new Arcanum.Simulation.Reaction.ReactionNpcFacts(50, Arcanum.Simulation.Character.CharacterRace.Human, false, false);
        var plain = owner.QueryReaction("oid:a", npc, owner.QueryHeroReactionFacts()!).View!.Effective;
        Assert.AreEqual(plain + (5 - 10 - 20 + 2) - 5, owner.QueryReaction("oid:a", npc, owner.QueryHeroReactionFacts(3, 9)!).View!.Effective);
        using var heroless = GameSessionOwner.Create(Descriptor() with { Reputations = Catalog }).Owner!;
        Changed(heroless.AddReputation(heroless.Version, 1001), heroless, 1);
        Assert.IsNull(heroless.QueryHeroReactionFacts(), "no hero: the host supplies PC facts (and reads the sum itself)");
        Assert.AreEqual(5, heroless.QueryReputationReaction(0, 0));
    }

    [TestMethod]
    public void Reputations_round_trip_in_acquisition_order_and_restore_keeps_the_catalog_from_the_options()
    {
        using var owner = GameSessionOwner.Create(Descriptor() with { Reputations = Catalog }).Owner!;
        Changed(owner.AddReputation(owner.Version, 1999), owner, 1);
        Advance(owner, 1000);
        Changed(owner.AddReputation(owner.Version, 1001), owner, 2);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"reputations\":[{\"reputation\":1999,\"timestampMs\":54000000},{\"reputation\":1001,\"timestampMs\":54001000}],\"storyState\":0,\"revealedTownMaps\":[],\"townMapCells\":[]}");
        using var back = Restore(Decode(text), RestoreOptions() with { Reputations = Catalog });
        CollectionAssert.AreEqual(owner.QueryReputations().ToArray(), back.QueryReputations().ToArray());
        Assert.AreSame(Catalog, back.Reputations);
        Assert.AreEqual(5, back.QueryReputationReaction(0, 0));
        using var bare = Restore(Decode(text));
        Assert.AreEqual(0, bare.QueryReputationReaction(0, 0), "the table is data, never saved");
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(back.Capture()));
    }

    [TestMethod]
    public void Decode_and_restore_refuse_reputation_lists_no_change_produces()
    {
        static string With(string reputations) => GoldenJson.Replace(NoReputations, ",\"reputations\":[" + reputations + "]");
        static void Fails(string reputations, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(With(reputations));
            Assert.AreEqual((failure, path), (result.Failure, result.Path), reputations);
        }
        Fails("{\"reputation\":999,\"timestampMs\":0}", SnapshotDecodeFailure.OutOfRange, "$.story.reputations[0].reputation");
        Fails("{\"reputation\":2000,\"timestampMs\":0}", SnapshotDecodeFailure.OutOfRange, "$.story.reputations[0].reputation");
        Fails("{\"reputation\":1001,\"timestampMs\":0},{\"reputation\":1001,\"timestampMs\":1}", SnapshotDecodeFailure.NotCanonical, "$.story.reputations[1].reputation");
        Fails("{\"reputation\":1001,\"timestampMs\":5},{\"reputation\":1000,\"timestampMs\":4}", SnapshotDecodeFailure.NotCanonical, "$.story.reputations[1].timestampMs");
        Fails("{\"reputation\":1001}", SnapshotDecodeFailure.MissingField, "$.story.reputations[0].timestampMs");
        Fails("{\"reputation\":1001,\"timestampMs\":-1}", SnapshotDecodeFailure.OutOfRange, "$.story.reputations[0].timestampMs");
        // Later and earlier numbers are both fine: the list is in the order gained, not sorted.
        var decoded = GameSessionSnapshotCodec.TryDecode(With("{\"reputation\":1500,\"timestampMs\":0},{\"reputation\":1001,\"timestampMs\":0}"));
        Assert.AreEqual(SnapshotDecodeFailure.None, decoded.Failure);
        // A time after the saved clock no change can produce.
        var future = GameSessionSnapshotCodec.TryDecode(With("{\"reputation\":1001,\"timestampMs\":54000001}")).Snapshot!;
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(future, RestoreOptions()).Failure);
    }

    [TestMethod]
    public void The_dialog_port_reads_and_changes_the_list_and_skips_or_refuses_other_numbers()
    {
        using var owner = Create();
        var pc = new DialogParticipantId(1);
        var port = new SessionDialogReputationPort(owner, pc);
        Assert.AreEqual(DialogReputationResult.Applied, port.Execute(new(pc, 1001, true)));
        Assert.AreEqual(DialogReputationResult.Applied, port.Execute(new(pc, 1001, true)), "already held: applied, nothing changes");
        Assert.AreEqual(1, owner.Version.Revision);
        Assert.IsTrue(port.HasReputation(new(pc, 1001)));
        Assert.AreEqual(DialogReputationResult.Skipped, port.Execute(new(pc, 2, true)), "A5 proposal (b): skipped");
        Assert.AreEqual(DialogReputationResult.Skipped, port.Execute(new(pc, 0, false)));
        Assert.AreEqual(1, owner.Version.Revision);
        Assert.AreEqual(DialogReputationResult.Rejected, new SessionDialogReputationPort(owner, pc, ReputationRangePolicy.Refuse).Execute(new(pc, 2, true)));
        Assert.AreEqual(DialogReputationResult.Applied, port.Execute(new(pc, 1001, false)));
        Assert.IsFalse(owner.HeroHasReputation(1001));
        Assert.AreEqual(DialogReputationResult.Rejected, port.Execute(new(new DialogParticipantId(2), 1001, true)), "another PC");
        Assert.ThrowsExactly<ArgumentException>(() => port.HasReputation(new(new DialogParticipantId(2), 1001)));
    }

    [TestMethod]
    public void Changes_stop_at_the_revision_limit()
    {
        using var owner = Create();
        Changed(owner.AddReputation(owner.Version, 1000), owner, 1);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture()).Replace("\"revision\":1,\"clock\"", $"\"revision\":{long.MaxValue},\"clock\"");
        using var full = Restore(Decode(text));
        Rejected(full.AddReputation(full.Version, 1001), full, GameSessionFailure.RevisionExhausted, long.MaxValue);
        Rejected(full.RemoveReputation(full.Version, 1000), full, GameSessionFailure.RevisionExhausted, long.MaxValue);
        NoChange(full.AddReputation(full.Version, 1000), full, long.MaxValue);
    }

    private static void Advance(GameSessionOwner owner, long ms) => Assert.AreEqual(ClockMutationDisposition.Changed,
        owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);

    private static void NoChange(GameSessionMutationResult result, GameSessionOwner owner, long revision)
    {
        Assert.AreEqual(GameSessionDisposition.NoChange, result.Disposition);
        Assert.AreEqual(new GameSessionVersion(owner.Identity, revision), owner.Version);
    }
}

using Arcanum.Simulation.Character;
using Arcanum.Simulation.Reaction;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

[TestClass]
public sealed class ReactionStateTests
{
    private const string Virgil = "oid:virgil";
    private static readonly ReactionNpcFacts Human60 = new(60, CharacterRace.Human, false, false);
    private static readonly ReactionPcFacts Pc = new(CharacterRace.Human, 0, 0, [], [], [], false);
    private static readonly ReactionHitFacts Row = new(FollowerHitAdjustment: 5, HostileReaction: -20);

    private static SessionReactionView View(GameSessionOwner owner, string entity = Virgil, ReactionPcFacts? pc = null)
    {
        var result = owner.QueryReaction(entity, Human60, pc ?? Pc);
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.View!;
    }

    private static SessionReactionResult Changed(SessionReactionResult result, GameSessionOwner owner, long revision)
    {
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        Assert.AreEqual(new GameSessionVersion(owner.Identity, revision), result.Version);
        Assert.AreEqual(result.Version, owner.Version);
        return result;
    }

    private static void Refused(SessionReactionResult result, GameSessionOwner owner, GameSessionFailure failure, long revision)
    {
        Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
        Assert.AreEqual(revision, owner.Version.Revision);
        Assert.IsNull(result.Adjustment);
    }

    [TestMethod]
    public void AnUntouchedNpcReadsItsBaseAndHasNoRecord()
    {
        using var owner = Create();
        Assert.AreEqual(new SessionReactionView(60, ReactionCategory.Neutral, 60, false, false, false), View(owner));
        Assert.AreEqual(65, View(owner, pc: Pc with { Race = CharacterRace.HalfElf }).Effective, "the race term of the facts");
        Assert.IsNull(owner.QueryReactionRecord(Virgil));
        Assert.AreEqual(0, owner.QueryReactions().Records.Count);
        Assert.AreEqual(0, owner.Version.Revision);
    }

    [TestMethod]
    public void AChangeOutsideAConversationIsRememberedAndMeetsThePc()
    {
        using var owner = Create();
        var result = Changed(owner.AdjustReaction(owner.Version, Virgil, Human60, Pc, -15), owner, 1);
        Assert.AreEqual(new ReactionAdjustment(ReactionSkip.None, -15, false), result.Adjustment);
        Assert.AreEqual(new SessionReaction(Virgil, 45, null), owner.QueryReactionRecord(Virgil));
        Assert.AreEqual(new SessionReactionView(45, ReactionCategory.Neutral, 45, true, false, true), View(owner));
        // A change that PC effects reduce to 0 still writes the slot: the NPC remembers the PC.
        using var other = Create();
        var shrug = Pc with { BadReactionEffects = [new(StatModifierOperator.Add, 15)] };
        var zero = Changed(other.AdjustReaction(other.Version, Virgil, Human60, shrug, -15), other, 1);
        Assert.AreEqual(0, zero.Adjustment!.Value.Delta);
        Assert.AreEqual(new SessionReaction(Virgil, 60, null), other.QueryReactionRecord(Virgil));
    }

    [TestMethod]
    public void SetToIsADeltaFromTheEffectiveValue()
    {
        using var owner = Create();
        // Effective 65 (half-elf PC), so "set to 41" is −24 and the stored value becomes 36.
        var halfElf = Pc with { Race = CharacterRace.HalfElf };
        Changed(owner.SetReaction(owner.Version, Virgil, Human60, halfElf, 41), owner, 1);
        Assert.AreEqual(new SessionReaction(Virgil, 36, null), owner.QueryReactionRecord(Virgil));
        Assert.AreEqual(41, View(owner, pc: halfElf).Effective);
        // With a bad-reaction effect on the PC the target is missed, as in the source.
        var gnomish = halfElf with { BadReactionEffects = [new(StatModifierOperator.Add, 10)] };
        Changed(owner.SetReaction(owner.Version, Virgil, Human60, gnomish, 21), owner, 2);
        Assert.AreEqual(31, View(owner, pc: gnomish).Effective);
        var same = owner.SetReaction(owner.Version, Virgil, Human60, gnomish, 31);
        Assert.AreEqual((GameSessionDisposition.NoChange, ReactionSkip.ZeroDelta), (same.Disposition, same.Adjustment!.Value.Skip));
        // Set-to reads the standard value, not the hostility variant: a dark elf's −30 and beauty −65 are not floored to −49.
        var ugly = Pc with { ReactionModifier = -65 };
        var darkElf = Human60 with { Race = CharacterRace.DarkElf };
        Changed(owner.SetReaction(owner.Version, "oid:drow", darkElf, ugly, 0), owner, 3);
        Assert.AreEqual(0, owner.QueryReaction("oid:drow", darkElf, ugly).View!.Effective);
        Assert.AreEqual(new SessionReaction("oid:drow", 95, null), owner.QueryReactionRecord("oid:drow"));
    }

    [TestMethod]
    public void AConversationKeepsItsOwnSlotUntilItEnds()
    {
        using var owner = Create();
        Changed(owner.BeginReactionConversation(owner.Version, Virgil, Human60), owner, 1);
        Assert.AreEqual(new SessionReactionView(60, ReactionCategory.Neutral, 60, false, true, true), View(owner),
            "met before stays false during the first conversation");
        Changed(owner.AdjustReaction(owner.Version, Virgil, Human60, Pc, 10), owner, 2);
        Assert.AreEqual(new SessionReaction(Virgil, null, 70), owner.QueryReactionRecord(Virgil));
        Changed(owner.EndReactionConversation(owner.Version, Virgil, Human60), owner, 3);
        Assert.AreEqual(new SessionReaction(Virgil, 70, null), owner.QueryReactionRecord(Virgil));
        Assert.IsTrue(View(owner).MetBefore);

        // The second conversation starts from the remembered value; changes stay in the conversation slot.
        Changed(owner.BeginReactionConversation(owner.Version, Virgil, Human60), owner, 4);
        Changed(owner.AdjustReaction(owner.Version, Virgil, Human60, Pc, -30), owner, 5);
        Assert.AreEqual(new SessionReaction(Virgil, 70, 40), owner.QueryReactionRecord(Virgil));
        Assert.AreEqual(new SessionReactionView(40, ReactionCategory.Suspicious, 40, true, true, true), View(owner));
        // Forgetting clears only the remembered slot; the end remembers the PC again.
        Changed(owner.ForgetReaction(owner.Version, Virgil), owner, 6);
        Assert.AreEqual(new SessionReaction(Virgil, null, 40), owner.QueryReactionRecord(Virgil));
        Changed(owner.EndReactionConversation(owner.Version, Virgil, Human60), owner, 7);
        Assert.AreEqual(new SessionReaction(Virgil, 40, null), owner.QueryReactionRecord(Virgil));
        // Forgetting without a conversation returns the NPC to its base and drops the record.
        Changed(owner.ForgetReaction(owner.Version, Virgil), owner, 8);
        Assert.IsNull(owner.QueryReactionRecord(Virgil));
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.ForgetReaction(owner.Version, Virgil).Disposition);
        Assert.AreEqual(8, owner.Version.Revision);
    }

    [TestMethod]
    public void TheFatePointForcesAHundredAndAnEndWithoutBeginStillRemembers()
    {
        // ARC-WORLD-054 (DR6): the hero's armed "force good reaction" fate raises a start below 100 and is used by it.
        using var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        owner.GrantHeroFatePoint(owner.Version);
        owner.ArmHeroFate(owner.Version, FateKind.ForceGoodReaction);
        var start = owner.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.BeginReactionConversation(owner.Version, Virgil, Human60).Disposition);
        Assert.AreEqual(start + 1, owner.Version.Revision, "the reaction and the used fate in one revision");
        Assert.AreEqual(100, View(owner).Stored);
        Assert.AreEqual(new FateState(0, 0), owner.QueryHeroFate());
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.BeginReactionConversation(owner.Version, Virgil, Human60).Disposition, "already 100");
        // At 100 or above an armed fate stays armed for a later talk.
        owner.GrantHeroFatePoint(owner.Version);
        owner.ArmHeroFate(owner.Version, FateKind.ForceGoodReaction);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.BeginReactionConversation(owner.Version, Virgil, Human60).Disposition);
        Assert.IsTrue(owner.HeroFateArmed(FateKind.ForceGoodReaction));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.UndoHeroFate(owner.Version, FateKind.ForceGoodReaction).Disposition);
        // A second begin keeps the open conversation's value, not the remembered one or the base.
        var before = owner.Version.Revision;
        Changed(owner.AdjustReaction(owner.Version, Virgil, Human60, Pc, -30), owner, before + 1);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.BeginReactionConversation(owner.Version, Virgil, Human60).Disposition);
        Assert.AreEqual(new SessionReaction(Virgil, null, 70), owner.QueryReactionRecord(Virgil));
        using var other = Create();
        Changed(other.EndReactionConversation(other.Version, Virgil, Human60), other, 1);
        Assert.AreEqual(new SessionReaction(Virgil, 60, null), other.QueryReactionRecord(Virgil), "as the source: met before");
    }

    [TestMethod]
    public void TheSessionPartyDecidesTheLeaderRules()
    {
        using var owner = Create();
        var charming = Pc with { ExtraordinaryCharisma = true };
        // Not led: a drop applies and asks for no leader check.
        Assert.AreEqual(new ReactionAdjustment(ReactionSkip.None, -5, false),
            Changed(owner.AdjustReaction(owner.Version, Virgil, Human60, charming, -5), owner, 1).Adjustment);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Virgil).Disposition);
        var skipped = owner.AdjustReaction(owner.Version, Virgil, Human60, charming, -5);
        Assert.AreEqual((GameSessionDisposition.NoChange, ReactionSkip.LeaderCharisma), (skipped.Disposition, skipped.Adjustment!.Value.Skip));
        Assert.AreEqual(55, View(owner).Effective);
        var check = owner.AdjustReaction(owner.Version, Virgil, Human60, Pc, -5);
        Assert.AreEqual(new ReactionAdjustment(ReactionSkip.None, -5, true), check.Adjustment, "a follower's drop checks its leader");
        // Waiting keeps the PC as leader.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, Virgil).Disposition);
        Assert.AreEqual(ReactionSkip.LeaderCharisma, owner.AdjustReaction(owner.Version, Virgil, Human60, charming, -5).Adjustment!.Value.Skip);
    }

    [TestMethod]
    public void AHitOnAFollowerReadsTheActiveRuleModeAtEachCall()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Virgil).Disposition);
        var revision = owner.Version.Revision;
        var steamwoven = Changed(owner.ReactToPcHit(owner.Version, Virgil, Human60, Pc, Row, deliberate: true), owner, revision + 1);
        Assert.IsTrue(steamwoven.FollowCheck);
        Assert.AreEqual(new ReactionAdjustment(ReactionSkip.None, -5, true), steamwoven.Adjustment, "the fix is a drop: it checks the leader");
        Assert.AreEqual(55, View(owner).Effective);
        // The mode switch moves no revision and converts nothing; the next hit follows the original's rise.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SelectRules(RuleMode.Original));
        Assert.AreEqual(55, View(owner).Effective);
        var original = Changed(owner.ReactToPcHit(owner.Version, Virgil, Human60, Pc, Row, deliberate: true), owner, revision + 2);
        Assert.AreEqual(5, original.Adjustment!.Value.Delta);
        Assert.AreEqual(60, View(owner).Effective);
        Assert.IsFalse(original.Adjustment!.Value.LeaderCheck, "a rise never checks the leader");
        // An accidental hit only asks for the follow check.
        var accidental = owner.ReactToPcHit(owner.Version, Virgil, Human60, Pc, Row, deliberate: false);
        Assert.AreEqual((GameSessionDisposition.NoChange, true), (accidental.Disposition, accidental.FollowCheck));
        Assert.IsNull(accidental.Adjustment);
    }

    [TestMethod]
    public void TheSteamwovenFollowerHitIsADropWithTheDropRules()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Virgil).Disposition);
        // An extraordinary-charisma leader is immune to drops, so the fix changes nothing for it; the follow check still runs.
        var charming = owner.ReactToPcHit(owner.Version, Virgil, Human60, Pc with { ExtraordinaryCharisma = true }, Row, true);
        Assert.AreEqual((GameSessionDisposition.NoChange, ReactionSkip.LeaderCharisma, true),
            (charming.Disposition, charming.Adjustment!.Value.Skip, charming.FollowCheck));
        Assert.AreEqual(60, View(owner).Effective);
        // A refused write past the bound still reports the follow check.
        Changed(owner.AdjustReaction(owner.Version, Virgil, Human60, Pc, -1_000_000), owner, 2);
        Assert.AreEqual(-999_940, View(owner).Stored);
        Changed(owner.AdjustReaction(owner.Version, Virgil, Human60, Pc, -60), owner, 3);
        var bound = owner.ReactToPcHit(owner.Version, Virgil, Human60, Pc, Row, true);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InvalidReactionAmount, true),
            (bound.Disposition, bound.Failure, bound.FollowCheck));
        Assert.AreEqual(3, owner.Version.Revision);
    }

    [TestMethod]
    public void AHitOnAnotherNpcLowersIt()
    {
        foreach (var mode in new[] { RuleMode.Steamwoven, RuleMode.Original })
        {
            using var owner = GameSessionOwner.Create(Descriptor() with { Rules = mode }).Owner!;
            var accidental = Changed(owner.ReactToPcHit(owner.Version, "oid:guard", Human60, Pc, Row, false), owner, 1);
            Assert.AreEqual((-10, false), (accidental.Adjustment!.Value.Delta, accidental.FollowCheck));
            Changed(owner.ReactToPcHit(owner.Version, "oid:guard", Human60, Pc, Row, true), owner, 2);
            Assert.AreEqual(-20, View(owner, "oid:guard").Effective);
            Assert.AreEqual(GameSessionDisposition.NoChange, owner.ReactToPcHit(owner.Version, "oid:guard", Human60, Pc, Row, true).Disposition);
        }
    }

    [TestMethod]
    public void TheHeroSuppliesItsOwnFacts()
    {
        using var none = Create();
        Assert.IsNull(none.QueryHeroReactionFacts());
        using var halfElf = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var facts = halfElf.QueryHeroReactionFacts()!;
        // Beauty 8 + 1 (half-elf) = 9: reaction modifier −3; charisma 8 is not extraordinary.
        Assert.AreEqual((CharacterRace.HalfElf, -3, 0, false), (facts.Race, facts.ReactionModifier, facts.ReputationAdjustment, facts.ExtraordinaryCharisma));
        Assert.AreEqual(0, facts.ReactionEffects.Count + facts.GoodReactionEffects.Count + facts.BadReactionEffects.Count);
        Assert.AreEqual(62, halfElf.QueryReaction(Virgil, Human60, facts).View!.Effective, "60 + 5 race − 3 beauty");
        var stats = HeroStateTests.Defaults();
        stats[7] = 20;
        using var leader = HeroStateTests.CreateWith(HeroStateTests.HalfElf(stats));
        Assert.IsTrue(leader.QueryHeroReactionFacts()!.ExtraordinaryCharisma);
    }

    [TestMethod]
    public void InvalidInputsAreTypedRefusals()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionFailure.InvalidReactionTarget, owner.QueryReaction("", Human60, Pc).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidReactionFacts, owner.QueryReaction(Virgil, Human60 with { BaseReaction = 2_000_000 }, Pc).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidReactionFacts, owner.QueryReaction(Virgil, Human60, Pc with { Race = CharacterRace.Ogre }).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidReactionFacts, owner.QueryReaction(Virgil, Human60, Pc, (ReactionQuery)5).Failure);
        Refused(owner.AdjustReaction(owner.Version, "bad id", Human60, Pc, 1), owner, GameSessionFailure.InvalidReactionTarget, 0);
        Refused(owner.AdjustReaction(owner.Version, Virgil, null!, Pc, 1), owner, GameSessionFailure.InvalidReactionFacts, 0);
        Refused(owner.AdjustReaction(owner.Version, Virgil, Human60, Pc, 1_000_001), owner, GameSessionFailure.InvalidReactionAmount, 0);
        Refused(owner.SetReaction(owner.Version, Virgil, Human60, Pc, -1_000_001), owner, GameSessionFailure.InvalidReactionAmount, 0);
        Refused(owner.ReactToPcHit(owner.Version, Virgil, Human60, Pc, new(2_000_000, 0), true), owner, GameSessionFailure.InvalidReactionFacts, 0);
        Refused(owner.BeginReactionConversation(owner.Version, Virgil, null!), owner, GameSessionFailure.InvalidReactionFacts, 0);
        Refused(owner.ForgetReaction(owner.Version, ""), owner, GameSessionFailure.InvalidReactionTarget, 0);
        Assert.ThrowsExactly<ArgumentException>(() => owner.QueryReactionRecord("bad id"));
        // The order is entity, facts, version, amount, whatever else is wrong.
        var old = owner.Version with { Revision = 7 };
        Refused(owner.SetReaction(old, "bad id", Human60, Pc, 2_000_000), owner, GameSessionFailure.InvalidReactionTarget, 0);
        Refused(owner.SetReaction(old, Virgil, Human60, Pc, 2_000_000), owner, GameSessionFailure.StaleVersion, 0);
        Refused(owner.ReactToPcHit(old, "bad id", Human60, Pc, new(2_000_000, 0), true), owner, GameSessionFailure.InvalidReactionTarget, 0);
        Refused(owner.ReactToPcHit(old, Virgil, Human60, Pc, new(2_000_000, 0), true), owner, GameSessionFailure.InvalidReactionFacts, 0);
        // A stored value beyond ±1,000,000 is refused, not wrapped.
        Changed(owner.AdjustReaction(owner.Version, Virgil, Human60, Pc, 999_940), owner, 1);
        Refused(owner.AdjustReaction(owner.Version, Virgil, Human60, Pc, 1), owner, GameSessionFailure.InvalidReactionAmount, 1);
        // A stale version changes nothing.
        var stale = owner.Version with { Revision = 0 };
        Refused(owner.AdjustReaction(stale, Virgil, Human60, Pc, -1), owner, GameSessionFailure.StaleVersion, 1);
        Refused(owner.EndReactionConversation(stale, Virgil, Human60), owner, GameSessionFailure.StaleVersion, 1);
        Refused(owner.ForgetReaction(stale, Virgil), owner, GameSessionFailure.StaleVersion, 1);
    }

    [TestMethod]
    public void TheRecordBudgetIsTheCallersLimit()
    {
        using var owner = Create(new GameSessionLimits(maxReactionRecords: 1));
        Changed(owner.AdjustReaction(owner.Version, "oid:a", Human60, Pc, 1), owner, 1);
        Changed(owner.AdjustReaction(owner.Version, "oid:a", Human60, Pc, 1), owner, 2);
        Refused(owner.AdjustReaction(owner.Version, "oid:b", Human60, Pc, 1), owner, GameSessionFailure.BudgetExceeded, 2);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new GameSessionLimits(maxReactionRecords: GameSessionLimits.PolicyMaxReactionRecords + 1));
    }

    [TestMethod]
    public void ReactionsSurviveCanonicalTextAndRestore()
    {
        using var owner = Create();
        Changed(owner.AdjustReaction(owner.Version, "oid:b", Human60, Pc, 5), owner, 1);
        Changed(owner.BeginReactionConversation(owner.Version, "oid:a", Human60), owner, 2);
        Changed(owner.AdjustReaction(owner.Version, "oid:a", Human60, Pc, -70), owner, 3);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"reactions\":{\"records\":[{\"entity\":\"oid:a\",\"remembered\":null,\"conversation\":-10}," +
            "{\"entity\":\"oid:b\",\"remembered\":65,\"conversation\":null}]},\"combat\":null,\"combatMode\":{\"hero\":false,\"critters\":[]},\"concealment\":{\"hero\":false,\"npcs\":[]},\"sound\":");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(new SessionReaction("oid:a", null, -10), restored.QueryReactionRecord("oid:a"));
        Assert.AreEqual(new SessionReaction("oid:b", 65, null), restored.QueryReactionRecord("oid:b"));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
    }

    [TestMethod]
    public void DecodeAndRestoreRefuseReactionRecordsNoTransitionProduces()
    {
        static string With(string records) => GoldenJson.Replace(NoReactions, ",\"reactions\":{\"records\":[" + records + "]}");
        void Fails(string text, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(text);
            Assert.AreEqual((failure, path), (result.Failure, result.Path));
        }
        const string A = "{\"entity\":\"oid:a\",\"remembered\":1,\"conversation\":null}";
        const string B = "{\"entity\":\"oid:b\",\"remembered\":null,\"conversation\":2}";
        Assert.AreEqual(2, Decode(With(A + "," + B)).Reactions.Records.Count);
        Fails(With(B + "," + A), SnapshotDecodeFailure.NotCanonical, "$.reactions.records[1].entity");
        Fails(With(A + "," + A), SnapshotDecodeFailure.NotCanonical, "$.reactions.records[1].entity");
        Fails(With("{\"entity\":\"oid:a\",\"remembered\":null,\"conversation\":null}"), SnapshotDecodeFailure.NotCanonical, "$.reactions.records[0]");
        Fails(With("{\"entity\":\"oid:a\",\"remembered\":1000001,\"conversation\":null}"), SnapshotDecodeFailure.OutOfRange, "$.reactions.records[0].remembered");
        Fails(With("{\"entity\":\"oid:a\",\"remembered\":1}"), SnapshotDecodeFailure.MissingField, "$.reactions.records[0].conversation");
        Fails(With("{\"entity\":\"\",\"remembered\":1,\"conversation\":null}"), SnapshotDecodeFailure.OutOfRange, "$.reactions.records[0].entity");
        // Within the codec's maxima but over the caller's budget: Restore refuses.
        var result = GameSessionOwner.Restore(Decode(With(A + "," + B)), RestoreOptions(new GameSessionLimits(maxReactionRecords: 1)));
        Assert.AreEqual((null, GameSessionFailure.BudgetExceeded), (result.Owner, result.Failure));
    }
}

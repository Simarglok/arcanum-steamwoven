using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-046: out-of-combat follower orders in the session (ARC-RES-064 OL1–OL16). Names, distances and facts are invented; the
// follow facts are those of PartyStateTests (PC charisma 8, max followers 2, reaction 50).
[TestClass]
public sealed class CompanionOrderStateTests
{
    private const long T0 = 54_000_000, Day = 86_400_000;
    private static readonly CellCoord Tile = new(40, 41);

    private static CompanionOrderFacts F(string entity, long distance = 2, string? name = null, bool mindControlled = false, int reaction = 50,
        int maxFollowers = 3) =>
        new(entity, name ?? entity, distance, mindControlled, PartyStateTests.Facts(maxFollowers, reaction), new(3, PersuasionTraining.None, 8, false));

    private static GameSessionOwner Party(params string[] followers)
    {
        var owner = Create();
        foreach (var entity in followers) Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, entity).Disposition);
        return owner;
    }

    private static SessionCompanionOrderResult All(GameSessionOwner owner, CompanionOrderType type, params CompanionOrderFacts[] followers) =>
        owner.IssueCompanionOrder(owner.Version, new(type, CompanionOrderAddressing.All, followers, Tile: Tile));

    [TestMethod]
    public void An_order_to_all_reaches_the_array_followers_within_ten_tiles_in_array_order()
    {
        using var owner = Party("oid:c", "oid:a", "oid:b", "oid:far", "oid:unknown");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "oid:w").Disposition);
        Changed(owner.WaitParty(owner.Version, "oid:w"), owner, 7);
        var result = All(owner, CompanionOrderType.StayClose, F("oid:a", 10), F("oid:b"), F("oid:c", 0), F("oid:far", 11), F("oid:w", 1));
        CollectionAssert.AreEqual(new[] { "oid:c", "oid:a", "oid:b" }, result.Outcomes.Select(o => o.Entity).ToArray(),
            "array order, within 10; one beyond reach, one without facts and a waiting one get nothing (OL16)");
        Assert.IsTrue(result.Outcomes.All(o => o.Effect == CompanionOrderEffect.SpreadCleared && o.Line == CompanionOrderLine.Acknowledge));
        Assert.AreEqual(GameSessionDisposition.NoChange, result.Disposition, "the spread flags were clear already");
    }

    [TestMethod]
    public void Leave_is_a_forced_leave_after_the_farewell_and_nothing_under_mind_control()
    {
        using var owner = Party("oid:a", "oid:b", "oid:mc");
        var result = All(owner, CompanionOrderType.Leave, F("oid:a", reaction: 30), F("oid:b", reaction: 70), F("oid:mc", mindControlled: true));
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        CollectionAssert.AreEqual(new[] { CompanionOrderEffect.Left, CompanionOrderEffect.Left, CompanionOrderEffect.MindControlled },
            result.Outcomes.Select(o => o.Effect).ToArray());
        // Steamwoven (D3): friendly above 40.
        Assert.AreEqual((CompanionOrderLine.HostileFarewell, CompanionOrderLine.FriendlyFarewell, (CompanionOrderLine?)null),
            (result.Outcomes[0].Line, result.Outcomes[1].Line, result.Outcomes[2].Line));
        Assert.AreEqual(new LoyaltyLines(2000, 2099), result.Outcomes[0].Lines);
        Assert.AreEqual(CompanionStanding.Free, owner.QueryCompanion("oid:a").Standing);
        Assert.IsFalse(owner.QueryCompanion("oid:a").Jilted, "an order leave does not jilt");
        Assert.AreEqual(CompanionStanding.Following, owner.QueryCompanion("oid:mc").Standing);

        // Original: the swapped farewell; the leave is forced, so an extraordinary leader does not stop it.
        using var original = Party("oid:a");
        Assert.AreEqual(GameSessionDisposition.Changed, original.SelectRules(RuleMode.Original));
        var charming = F("oid:a", reaction: 30) with { Consent = PartyStateTests.Facts(reaction: 30) with { PcCharisma = 25 } };
        var left = All(original, CompanionOrderType.Leave, charming).Outcomes.Single();
        Assert.AreEqual((CompanionOrderEffect.Left, (CompanionOrderLine?)CompanionOrderLine.FriendlyFarewell), (left.Effect, left.Line));
    }

    [TestMethod]
    public void Wait_is_the_ordinary_wait_with_its_timer_and_nothing_under_mind_control()
    {
        using var owner = Party("oid:a", "oid:mc");
        var result = All(owner, CompanionOrderType.Wait, F("oid:a"), F("oid:mc", mindControlled: true));
        Assert.AreEqual((CompanionOrderEffect.Waited, (CompanionOrderLine?)CompanionOrderLine.Acknowledge), (result.Outcomes[0].Effect, result.Outcomes[0].Line));
        Assert.AreEqual(CompanionOrderEffect.MindControlled, result.Outcomes[1].Effect);
        Assert.AreEqual(CompanionStanding.Waiting, owner.QueryCompanion("oid:a").Standing);
        Assert.AreEqual(T0 + Day, owner.QueryWaitTimer("oid:a"), "wait index 3: one day (ARC-WORLD-045)");
        Assert.AreEqual(CompanionStanding.Following, owner.QueryCompanion("oid:mc").Standing);
        // Without timer facts the wait still applies, with no timer.
        using var untimed = Party("oid:a");
        Assert.AreEqual(CompanionOrderEffect.Waited, All(untimed, CompanionOrderType.Wait, F("oid:a") with { Wait = null }).Outcomes.Single().Effect);
        Assert.IsNull(untimed.QueryWaitTimer("oid:a"));
    }

    [TestMethod]
    public void Follow_only_acknowledges_or_says_cannot_and_d1_brings_waiting_followers_back()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, "oid:a", PartyStateTests.Facts(), false).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "oid:forced").Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "oid:w").Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, "oid:w").Disposition);
        var revision = owner.Version.Revision;

        // Original (D1 not fixed): array members only, no state change. The limit counts the follower itself (max 1: refused) unless forced.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SelectRules(RuleMode.Original));
        var original = All(owner, CompanionOrderType.Follow, F("oid:a", maxFollowers: 1), F("oid:forced", maxFollowers: 1), F("oid:w"));
        Assert.AreEqual(GameSessionDisposition.NoChange, original.Disposition);
        Assert.AreEqual(revision, owner.Version.Revision);
        CollectionAssert.AreEqual(new[] { "oid:a", "oid:forced" }, original.Outcomes.Select(o => o.Entity).ToArray());
        Assert.AreEqual((CompanionOrderEffect.Refused, (CompanionOrderLine?)CompanionOrderLine.Cannot, (FollowConsent?)FollowConsent.LimitReached),
            (original.Outcomes[0].Effect, original.Outcomes[0].Line, original.Outcomes[0].Consent));
        Assert.AreEqual((CompanionOrderEffect.Acknowledged, (CompanionOrderLine?)CompanionOrderLine.Acknowledge), (original.Outcomes[1].Effect, original.Outcomes[1].Line));
        Assert.AreEqual(CompanionStanding.Waiting, owner.QueryCompanion("oid:w").Standing);

        // Steamwoven (D1): the waiting follower in reach comes back after the array members, if its follow check passes.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SelectRules(RuleMode.Steamwoven));
        var refused = All(owner, CompanionOrderType.Follow, F("oid:a"), F("oid:w", reaction: -50));
        Assert.AreEqual((CompanionOrderEffect.Refused, (FollowConsent?)FollowConsent.Dislike), (refused.Outcomes[1].Effect, refused.Outcomes[1].Consent));
        Assert.AreEqual(CompanionStanding.Waiting, owner.QueryCompanion("oid:w").Standing);
        Assert.AreEqual(0, All(owner, CompanionOrderType.Follow, F("oid:w", distance: 11)).Outcomes.Count, "beyond reach");
        var back = All(owner, CompanionOrderType.Follow, F("oid:a"), F("oid:forced"), F("oid:w"));
        CollectionAssert.AreEqual(new[] { "oid:a", "oid:forced", "oid:w" }, back.Outcomes.Select(o => o.Entity).ToArray());
        Assert.AreEqual((CompanionOrderEffect.Unwaited, (CompanionOrderLine?)CompanionOrderLine.Acknowledge), (back.Outcomes[2].Effect, back.Outcomes[2].Line));
        Assert.AreEqual(GameSessionDisposition.Changed, back.Disposition);
        Assert.AreEqual("oid:w", owner.QueryParty().Followers[^1].Entity);
    }

    [TestMethod]
    public void Spread_out_sets_the_flag_and_asks_for_the_spread_goal_only_when_it_was_clear()
    {
        using var owner = Party("oid:a");
        var first = All(owner, CompanionOrderType.SpreadOut, F("oid:a")).Outcomes.Single();
        Assert.AreEqual((CompanionOrderEffect.SpreadSet, true), (first.Effect, first.StartSpreadGoal));
        Assert.IsTrue(owner.QueryCompanion("oid:a").SpreadOut);
        var again = All(owner, CompanionOrderType.SpreadOut, F("oid:a"));
        Assert.AreEqual((GameSessionDisposition.NoChange, false), (again.Disposition, again.Outcomes.Single().StartSpreadGoal));
        var close = All(owner, CompanionOrderType.StayClose, F("oid:a"));
        Assert.AreEqual((GameSessionDisposition.Changed, CompanionOrderEffect.SpreadCleared), (close.Disposition, close.Outcomes.Single().Effect));
        Assert.IsFalse(owner.QueryCompanion("oid:a").SpreadOut);
    }

    [TestMethod]
    public void Walk_move_combat_and_no_order_are_returned_for_the_host_without_state_changes()
    {
        using var owner = Party("oid:a");
        var walk = All(owner, CompanionOrderType.WalkHere, F("oid:a")).Outcomes.Single();
        Assert.AreEqual((CompanionOrderEffect.WalkRequested, (CompanionOrderLine?)CompanionOrderLine.Acknowledge), (walk.Effect, walk.Line));
        var noTile = owner.IssueCompanionOrder(owner.Version, new(CompanionOrderType.WalkHere, CompanionOrderAddressing.All, [F("oid:a")])).Outcomes.Single();
        Assert.AreEqual((CompanionOrderEffect.NoTile, (CompanionOrderLine?)null), (noTile.Effect, noTile.Line));
        Assert.AreEqual(CompanionOrderEffect.MoveRequested, All(owner, CompanionOrderType.Move, F("oid:a")).Outcomes.Single().Effect);
        foreach (var combat in new[] { CompanionOrderType.Attack, CompanionOrderType.BackOff, CompanionOrderType.Curse })
            Assert.AreEqual((CompanionOrderEffect.CombatOrder, (CompanionOrderLine?)null), (All(owner, combat, F("oid:a")).Outcomes.Single() is var o ? (o.Effect, o.Line) : default),
                $"{combat}: stage A7's");
        foreach (var none in new[] { CompanionOrderType.None, CompanionOrderType.Join, CompanionOrderType.Disband, CompanionOrderType.FollowOther })
            Assert.AreEqual(CompanionOrderEffect.NoOrder, All(owner, none, F("oid:a")).Outcomes.Single().Effect);
        Assert.AreEqual(1, owner.Version.Revision, "nothing changed state");
    }

    [TestMethod]
    public void A_portrait_order_follows_the_d4_setting()
    {
        using var owner = Party("oid:first", "oid:second");
        CompanionOrderFacts[] twins = [F("oid:first", name: "Wolf"), F("oid:second", name: "wolf")];
        var steamwoven = owner.IssueCompanionOrder(owner.Version, new(CompanionOrderType.Wait, CompanionOrderAddressing.Portrait, twins, "oid:second", "wait"));
        Assert.AreEqual("oid:second", steamwoven.Outcomes.Single().Entity);
        Assert.AreEqual(CompanionStanding.Waiting, owner.QueryCompanion("oid:second").Standing);

        using var original = Party("oid:first", "oid:second");
        Assert.AreEqual(GameSessionDisposition.Changed, original.SelectRules(RuleMode.Original));
        var first = original.IssueCompanionOrder(original.Version, new(CompanionOrderType.Wait, CompanionOrderAddressing.Portrait, twins, "oid:second", "wait"));
        Assert.AreEqual("oid:first", first.Outcomes.Single().Entity, "OL15: the first follower whose name matches");
        Assert.AreEqual((CompanionStanding.Waiting, CompanionStanding.Following),
            (original.QueryCompanion("oid:first").Standing, original.QueryCompanion("oid:second").Standing));
        Assert.AreEqual(GameSessionFailure.NotInParty,
            original.IssueCompanionOrder(original.Version, new(CompanionOrderType.Wait, CompanionOrderAddressing.Portrait, twins, "oid:first", "wait")).Failure,
            "a waiting NPC has no portrait");
    }

    [TestMethod]
    public void Bad_requests_are_refused_whole()
    {
        using var owner = Party("oid:a");
        GameSessionFailure Failure(CompanionOrderRequest request) => owner.IssueCompanionOrder(owner.Version, request).Failure;
        Assert.AreEqual(GameSessionFailure.InvalidCompanion, Failure(new(CompanionOrderType.Wait, CompanionOrderAddressing.All, [F("oid:a"), F("oid:a")])));
        Assert.AreEqual(GameSessionFailure.InvalidCompanion, Failure(new(CompanionOrderType.Wait, CompanionOrderAddressing.All, [F("")])));
        Assert.AreEqual(GameSessionFailure.InvalidCompanion, Failure(new((CompanionOrderType)99, CompanionOrderAddressing.All, [F("oid:a")])));
        Assert.AreEqual(GameSessionFailure.InvalidCompanion, Failure(new(CompanionOrderType.Wait, CompanionOrderAddressing.All, [F("oid:a") with { KnownName = "" }])),
            "an empty name would match every portrait text");
        Assert.AreEqual(GameSessionFailure.InvalidCompanion, Failure(new(CompanionOrderType.Wait, CompanionOrderAddressing.All, [F("oid:a", distance: -1)])));
        Assert.AreEqual(GameSessionFailure.InvalidConsentFacts, Failure(new(CompanionOrderType.Wait, CompanionOrderAddressing.All,
            [F("oid:a") with { Consent = PartyStateTests.Facts() with { OtherLeaderCharisma = 4 } }])));
        Assert.AreEqual(GameSessionFailure.InvalidConsentFacts, Failure(new(CompanionOrderType.Wait, CompanionOrderAddressing.All,
            [F("oid:a") with { Wait = new(3, (PersuasionTraining)9, 8, false) }])));
        Assert.AreEqual(GameSessionFailure.NotInParty, Failure(new(CompanionOrderType.Wait, CompanionOrderAddressing.Portrait, [F("oid:a")], "oid:z", "wait")));
        Assert.AreEqual(GameSessionFailure.InvalidCompanion, Failure(new(CompanionOrderType.Wait, CompanionOrderAddressing.Portrait, [F("oid:a")], "", "wait")));
        Assert.AreEqual(GameSessionFailure.InvalidCompanion, Failure(new(CompanionOrderType.Wait, CompanionOrderAddressing.Portrait, [F("oid:a")])), "no clicked follower");
        Assert.AreEqual(GameSessionFailure.StaleVersion,
            owner.IssueCompanionOrder(new GameSessionVersion(owner.Identity, 0), new(CompanionOrderType.Wait, CompanionOrderAddressing.All, [F("oid:a")])).Failure);
        Assert.AreEqual(CompanionStanding.Following, owner.QueryCompanion("oid:a").Standing);
        Assert.AreEqual(0, All(owner, CompanionOrderType.Wait).Outcomes.Count, "no facts: nobody in reach");
    }
}

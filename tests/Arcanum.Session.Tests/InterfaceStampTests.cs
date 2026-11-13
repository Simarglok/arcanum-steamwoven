using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-UI-1364 (ledger GB15): the stamps the host lights the top strip's buttons from.
[TestClass]
public sealed class InterfaceStampTests
{
    private static readonly SessionEffectDefinition Nothing = new(4, []);

    private static GameSessionOwner Owner(bool hero = true)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = hero ? HeroStateTests.HalfElf() : null });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static void Advance(GameSessionOwner owner, long ms) => Assert.AreEqual(ClockMutationDisposition.Changed,
        owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);

    [TestMethod]
    public void A_session_without_a_hero_has_no_stamp()
    {
        using var owner = Owner(hero: false);
        Assert.IsNull(owner.QueryHeroInterfaceStamp());
    }

    [TestMethod]
    public void The_stamp_is_steady_while_nothing_changes()
    {
        using var owner = Owner();
        var first = owner.QueryHeroInterfaceStamp();
        Assert.IsNotNull(first);
        Assert.AreEqual(first, owner.QueryHeroInterfaceStamp());
        Advance(owner, 60_000);
        Assert.AreEqual(first, owner.QueryHeroInterfaceStamp(), "game time alone moves no stamp");
    }

    [TestMethod]
    public void A_quest_a_reputation_and_a_blessing_move_only_the_logbook_stamp()
    {
        using var owner = Owner();
        var start = owner.QueryHeroInterfaceStamp()!.Value;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetQuestState(owner.Version, 1001, SessionQuestState.Accepted).Disposition);
        var quest = owner.QueryHeroInterfaceStamp()!.Value;
        Assert.AreNotEqual(start.Logbook, quest.Logbook, "a quest moves the logbook stamp");
        Assert.AreEqual((start.Inventory, start.Areas), (quest.Inventory, quest.Areas));
        Advance(owner, 60_000);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetQuestState(owner.Version, 1001, SessionQuestState.Completed).Disposition);
        Assert.AreNotEqual(quest.Logbook, owner.QueryHeroInterfaceStamp()!.Value.Logbook, "a later state moves it again");

        var before = owner.QueryHeroInterfaceStamp()!.Value;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddReputation(owner.Version, 1002).Disposition);
        var reputation = owner.QueryHeroInterfaceStamp()!.Value;
        Assert.AreNotEqual(before.Logbook, reputation.Logbook, "a reputation moves it");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.AddReputation(owner.Version, 1002).Disposition);
        Assert.AreEqual(reputation, owner.QueryHeroInterfaceStamp());

        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroBlessing(owner.Version, SessionBlessingKind.Blessing, 60, Nothing).Disposition);
        var blessing = owner.QueryHeroInterfaceStamp()!.Value;
        Assert.AreNotEqual(reputation.Logbook, blessing.Logbook, "a blessing moves it");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RemoveHeroBlessing(owner.Version, SessionBlessingKind.Blessing, 60).Disposition);
        Assert.AreNotEqual(blessing.Logbook, owner.QueryHeroInterfaceStamp()!.Value.Logbook, "and its removal");
        var beforeCurse = owner.QueryHeroInterfaceStamp()!.Value;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroBlessing(owner.Version, SessionBlessingKind.Curse, 60, Nothing).Disposition);
        Assert.AreNotEqual(beforeCurse.Logbook, owner.QueryHeroInterfaceStamp()!.Value.Logbook, "a curse moves it");
        Assert.AreEqual(start.Inventory, owner.QueryHeroInterfaceStamp()!.Value.Inventory);
    }

    [TestMethod]
    public void A_newly_known_area_moves_only_the_areas_stamp()
    {
        using var owner = Owner();
        var start = owner.QueryHeroInterfaceStamp()!.Value;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DiscoverArea(owner.Version, 4).Disposition);
        var found = owner.QueryHeroInterfaceStamp()!.Value;
        Assert.AreNotEqual(start.Areas, found.Areas);
        Assert.AreEqual((start.Logbook, start.Inventory), (found.Logbook, found.Inventory));
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.DiscoverArea(owner.Version, 4).Disposition);
        Assert.AreEqual(found, owner.QueryHeroInterfaceStamp());
    }

    [TestMethod]
    public void An_object_taken_into_the_heros_list_moves_only_the_inventory_stamp()
    {
        using var owner = Owner();
        var seed = new SessionMapInventorySeed([], [new("feather", new(new(SessionItemType.Generic, 5002, 10, 20), 1, 0, 0, null, []), new CellCoord(104, 200))]);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, seed).Disposition);
        var start = owner.QueryHeroInterfaceStamp()!.Value;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "feather", _ => SessionInsertVerdict.Accepted).Disposition);
        var taken = owner.QueryHeroInterfaceStamp()!.Value;
        Assert.AreNotEqual(start.Inventory, taken.Inventory);
        Assert.AreEqual((start.Logbook, start.Areas), (taken.Logbook, taken.Areas));
    }

    // ARC-WORLD-1554 (ledger SQ16): the original's public quest setter passes a request to the internal setter, the one that lights the Logbook button, only while the effective state is below it and is not a final one, so a repeated or lower request writes and lights nothing.
    [TestMethod]
    public void A_repeated_or_lower_quest_request_changes_nothing_and_the_logbook_stamp_stays()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetQuestState(owner.Version, 1001, SessionQuestState.Achieved).Disposition);
        var stamp = owner.QueryHeroInterfaceStamp()!.Value;
        var revision = owner.Version.Revision;
        var time = owner.QueryQuest(1001)!.Value.TimestampMs;
        Advance(owner, 60_000);
        foreach (var request in new[] { SessionQuestState.Achieved, SessionQuestState.Accepted, SessionQuestState.Mentioned, SessionQuestState.Unknown })
        {
            Assert.AreEqual(GameSessionDisposition.NoChange, owner.SetQuestState(owner.Version, 1001, request).Disposition, request.ToString());
            Assert.AreEqual(stamp, owner.QueryHeroInterfaceStamp(), request + ": no write, no light");
        }
        Assert.AreEqual(revision, owner.Version.Revision);
        Assert.AreEqual(time, owner.QueryQuest(1001)!.Value.TimestampMs, "the stored time is not rewritten");
    }

    // A botched request also sets the quest's shared state to Botched, which is what the unbotch needs. The internal setter lights only when the stored state less the botched bit is not 0, and the unbotch function the same: a mark on a quest the PC never held changes nothing the logbook shows.
    [TestMethod]
    public void A_botched_mark_on_a_quest_with_no_stored_state_lights_nothing_and_on_a_held_quest_it_does()
    {
        using var owner = Owner();
        var start = owner.QueryHeroInterfaceStamp()!.Value;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetQuestState(owner.Version, 1003, SessionQuestState.Botched).Disposition);
        Assert.AreEqual(SessionQuestState.Botched, owner.QueryQuest(1003)!.Value.Effective);
        Assert.AreEqual(start, owner.QueryHeroInterfaceStamp(), "state 0 with the mark: the original's test (state less the mark is 0) does not light");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.UnbotchQuest(owner.Version, 1003).Disposition);
        Assert.AreEqual(start, owner.QueryHeroInterfaceStamp(), "and the unbotch of it lights nothing either");

        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetQuestState(owner.Version, 1004, SessionQuestState.Accepted).Disposition);
        var held = owner.QueryHeroInterfaceStamp()!.Value;
        Advance(owner, 1_000);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetQuestState(owner.Version, 1004, SessionQuestState.Botched).Disposition);
        var botched = owner.QueryHeroInterfaceStamp()!.Value;
        Assert.AreNotEqual(held.Logbook, botched.Logbook, "a mark on an accepted quest lights");
        Advance(owner, 1_000);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.UnbotchQuest(owner.Version, 1004).Disposition);
        Assert.AreNotEqual(botched.Logbook, owner.QueryHeroInterfaceStamp()!.Value.Logbook, "and the unbotch of it does");
    }
}

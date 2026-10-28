using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-094: the restock event in the session (ledger RK1–RK8): the schedule at a window open, the firing's decision through the
// host's facts, the clear (persistent items stay, made items leave no deleted-item record) and the generation with labelled rolls, and
// the saved event and discarded made number (schema 51). A synthetic seed: the smith (a pile of 10 coins, a worn hammer and a persistent
// ring), its chest (a pile of 30 coins, an axe and 20 bullets) and a box; the half-elf hero. Entities and numbers are invented.
[TestClass]
public sealed class RestockStateTests
{
    private static readonly SessionItemKind GoldPile = new(SessionItemType.Gold, 9056, 3200, 33200);
    private static readonly SessionItemKind Sword = new(SessionItemType.Weapon, 4021, 2003, 32003);
    private static readonly SessionItemKind Bullets = new(SessionItemType.Ammo, 7059, 3301, 33301);
    private static readonly SessionItemKind Key = new(SessionItemType.Key, 9001, 3400, 33400);
    private const int SmithSource = 5, ChestSource = 7;

    private class World : ISessionRestockWorld
    {
        public Dictionary<string, RestockHolder> Facts { get; } = new()
        {
            ["smith"] = new(RestockHolderKind.Npc, SmithSource, false, false, 0, false),
            ["chest"] = new(RestockHolderKind.Container, ChestSource, false, false, 0x1, false),
            ["box"] = new(RestockHolderKind.Container, ChestSource, false, false, RestockRules.RegeneratesAloneFlag, true),
        };
        public string? Merchant { get; set; } = "smith";
        public HashSet<string> Kept { get; } = ["ring"];
        public Dictionary<int, SessionRestockSet> Sets { get; } = new()
        {
            // Equal coin bounds draw nothing; a rate of 100 always creates, 0 never.
            [SmithSource] = new(SessionRestockSetStatus.Read, new(25, 25, [new(100, 1), new(0, 1), new(100, 2), new(100, 2)])),
            [ChestSource] = new(SessionRestockSetStatus.Read, new(40, 40, [new(100, 1), new(100, 3), new(100, 99)])),
        };
        public SessionInsertCheck Insert { get; set; } = _ => SessionInsertVerdict.Accepted;
        public List<(SessionMapId Map, string Holder)> Checks { get; } = [];

        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
        public RestockHolder? RestockFacts(SessionMapId map, string entity) => Facts.TryGetValue(entity, out var facts) ? facts : null;
        public string? RestockMerchant(SessionMapId map, string container) => Merchant;
        public bool Persistent(SessionMapId map, string item) => Kept.Contains(item);
        public SessionRestockSet RestockSet(int source) => Sets[source];
        public SessionRestockItem? RestockItem(int basicPrototype) => basicPrototype switch
        {
            1 => new(Sword),
            2 => new(Bullets, 1, 10),
            3 => new(Key),
            _ => null,
        };
        public SessionItemKind RestockPileKind => GoldPile;
        public SessionInsertCheck RestockInsertCheck(SessionMapId map, string holder) { Checks.Add((map, holder)); return Insert; }
    }

    private static SessionMapInventorySeed Seed() => new(
        [
            new("smith", SessionHolderKind.Npc, false, true, [], ["purse", "hammer", "ring"]),
            new("chest", SessionHolderKind.Container, false, false, [], ["gold-a", "axe", "bullets"]),
            new("box", SessionHolderKind.Container, false, false, [], []),
        ],
        [
            new("purse", Facts(SessionItemType.Gold, 3200, quantity: 10), null),
            new("hammer", Facts(SessionItemType.Weapon, 2001, location: 1004), null),
            new("ring", Facts(SessionItemType.Armor, 2805), null),
            new("gold-a", Facts(SessionItemType.Gold, 3200, quantity: 30), null),
            new("axe", Facts(SessionItemType.Weapon, 2002), null),
            new("bullets", Facts(SessionItemType.Ammo, 3301, quantity: 20) with { AmmoType = 1 }, null),
        ]);

    private static GameSessionOwner Owner(GameSessionLimits? limits = null, SessionMapInventorySeed? seed = null)
    {
        var owner = GameSessionOwner.Create(Descriptor(limits) with { Hero = HeroStateTests.HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed ?? Seed()).Failure);
        return owner;
    }

    private static void Schedule(GameSessionOwner owner, string holder) =>
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleRestock(owner.Version, holder, hasSource: true).Disposition);

    private static SessionEventPassResult Fire(GameSessionOwner owner, ISessionEventWorld world, long ms = RestockRules.MaxDelayMs)
    {
        Assert.AreEqual(ClockMutationDisposition.Changed,
            owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        return owner.ProcessEvents(world);
    }

    private static string[] List(GameSessionOwner owner, string holder) =>
        owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == holder).Items.Select(item => item.Entity).ToArray();

    [TestMethod]
    public void A_window_open_on_an_object_with_a_source_replaces_its_restock_event()
    {
        using var owner = Owner();
        var now = owner.QueryClockSnapshot().GameTimestampMs;
        var draws = owner.QueryRandom().Draws;
        Schedule(owner, "smith");
        Assert.AreEqual((1L, draws + 1), (owner.Version.Revision, owner.QueryRandom().Draws), "one revision, one restock.delay draw");
        var first = owner.QueryRestocks().Single();
        Assert.AreEqual(("smith", Exterior), (first.Holder, first.Map));
        Assert.IsTrue(first.DeadlineMs - now is >= RestockRules.MinDelayMs and <= RestockRules.MaxDelayMs, "12 to 24 game hours (RK2)");
        Schedule(owner, "smith");
        Assert.AreEqual(1, owner.QueryRestocks().Count, "the pending event is replaced (0x45BF10)");
        // An object without a source keeps its pending event and draws nothing.
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.ScheduleRestock(owner.Version, "smith", hasSource: false).Disposition);
        Assert.AreEqual((1, draws + 2), (owner.QueryRestocks().Count, owner.QueryRandom().Draws));
        // Refused before any effect.
        foreach (var holder in new[] { "pc", "-", "made:3", "bad id" })
            Assert.AreEqual(GameSessionFailure.InvalidEvents, owner.ScheduleRestock(owner.Version, holder, true).Failure, holder);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.ScheduleRestock(new(owner.Identity, 99), "chest", true).Failure);
        Assert.AreEqual((2L, 1), (owner.Version.Revision, owner.QueryRestocks().Count));
    }

    [TestMethod]
    public void A_living_npc_is_cleared_but_for_its_persistent_items_and_generated_again()
    {
        using var owner = Owner();
        var world = new World();
        // A made item the smith already holds (a sale) is cleared without a deleted-item record.
        Assert.AreEqual("made:1", owner.AddItemToNpc(owner.Version, "smith", Sword, _ => SessionInsertVerdict.Accepted).Item);
        Schedule(owner, "smith");
        var revision = owner.Version.Revision;
        var fired = Fire(owner, world).Restocks.Single();
        Assert.AreEqual((RestockStep.Regenerate, false), (fired.Decision.Step, fired.Postponed));
        var done = fired.Regenerations.Single();
        Assert.AreEqual(("smith", SessionRestockOutcome.Regenerated, 3, 25, "made:2", 0, 0), (done.Holder, done.Outcome, done.Removed, done.Coins, done.Pile,
            done.Refused, done.Skipped));
        CollectionAssert.AreEqual(new[] { "made:3", "made:4", "made:4" }, done.Created.ToArray(), "the second bullets join the first stack");
        Assert.AreEqual(revision + 1, owner.Version.Revision, "one revision");
        CollectionAssert.AreEqual(new[] { "ring", "made:2", "made:3", "made:4" }, List(owner, "smith"));
        Assert.IsTrue(owner.IsItemDestroyed("purse") && owner.IsItemDestroyed("hammer"), "worn and gold items go too (RK5)");
        Assert.IsFalse(owner.IsItemDestroyed("made:1") || owner.IsItemDestroyed("ring"));
        Assert.AreEqual((25, 20), (owner.QueryNpcGold("smith"), owner.QueryAmmo("made:4")!.Value.Units));
        Assert.AreEqual(1, owner.Capture().Inventory.LastDiscardedMade);
        Assert.AreEqual(0, owner.QueryRestocks().Count, "a regeneration schedules nothing");
        Assert.IsTrue(world.Checks.All(check => check == (Exterior, "smith")));
    }

    [TestMethod]
    public void The_line_and_coin_draws_are_labelled_session_rolls()
    {
        using var owner = Owner();
        var world = new World();
        world.Sets[SmithSource] = new(SessionRestockSetStatus.Read, new(30, 10, [new(50, 1), new(50, 1)]));
        Schedule(owner, "smith");
        var draws = owner.QueryRandom().Draws;
        Fire(owner, world);
        Assert.AreEqual(draws + 3, owner.QueryRandom().Draws, "the coins, then one percent per line");
        // The same seed and position give the same rolls.
        using var again = Owner();
        Schedule(again, "smith");
        Fire(again, world);
        CollectionAssert.AreEqual(List(owner, "smith"), List(again, "smith"));
        Assert.AreEqual(owner.QueryNpcGold("smith"), again.QueryNpcGold("smith"));
        Assert.IsTrue(owner.QueryNpcGold("smith") is >= 10 and <= 30, "reversed bounds are swapped");
    }

    [TestMethod]
    public void A_dead_or_led_npc_does_nothing_and_gets_no_new_event()
    {
        foreach (var facts in new RestockHolder[] { new(RestockHolderKind.Npc, SmithSource, true, false, 0, false), new(RestockHolderKind.Npc, SmithSource, false, true, 0, false),
            new(RestockHolderKind.Npc, 0, false, false, 0, false), new(RestockHolderKind.Other, SmithSource, false, false, 0, false) })
        {
            using var owner = Owner();
            var world = new World();
            world.Facts["smith"] = facts;
            Schedule(owner, "smith");
            var revision = owner.Version.Revision;
            var fired = Fire(owner, world).Restocks.Single();
            Assert.AreEqual((RestockStep.None, 0), (fired.Decision.Step, fired.Regenerations.Count), facts.ToString());
            Assert.AreEqual((revision, 0), (owner.Version.Revision, owner.QueryRestocks().Count));
        }
        // An object the host no longer has does nothing either.
        using var gone = Owner();
        var none = new World();
        none.Facts.Remove("smith");
        Schedule(gone, "smith");
        Assert.AreEqual(RestockStep.None, Fire(gone, none).Restocks.Single().Decision.Step);
    }

    [TestMethod]
    public void A_container_on_screen_is_scheduled_again_and_off_screen_regenerates_through_its_merchant()
    {
        using var owner = Owner();
        var world = new World();
        world.Facts["chest"] = world.Facts["chest"] with { OnScreen = true };
        Schedule(owner, "chest");
        var fired = Fire(owner, world).Restocks.Single();
        Assert.AreEqual(RestockStep.Reschedule, fired.Decision.Step);
        Assert.AreEqual("chest", owner.QueryRestocks().Single().Holder, "scheduled again (0x4640C0)");
        CollectionAssert.AreEqual(new[] { "gold-a", "axe", "bullets" }, List(owner, "chest"));

        world.Facts["chest"] = world.Facts["chest"] with { OnScreen = false };
        var revision = owner.Version.Revision;
        fired = Fire(owner, world).Restocks.Single();
        Assert.AreEqual(new RestockDecision(RestockStep.RegenerateMerchantAndContainer, true), fired.Decision);
        CollectionAssert.AreEqual(new[] { "smith", "chest" }, fired.Regenerations.Select(r => r.Holder).ToArray(), "the merchant first");
        Assert.AreEqual(revision + 1, owner.Version.Revision, "one revision for both");
        var chest = fired.Regenerations[1];
        Assert.AreEqual((3, 40, 1), (chest.Removed, chest.Coins, chest.Created.Count));
        Assert.AreEqual(2, chest.Skipped, "a key line and a prototype the host does not know");
        CollectionAssert.AreEqual(new[] { chest.Pile!, chest.Created[0] }, List(owner, "chest"));
        Assert.AreEqual(0, owner.QueryRestocks().Count);
    }

    [TestMethod]
    public void An_off_screen_container_without_a_living_merchant_does_nothing_and_a_merchant_without_a_source_keeps_its_list()
    {
        foreach (var (merchant, facts) in new (string?, RestockHolder?)[] { (null, null), ("smith", new(RestockHolderKind.Npc, SmithSource, true, false, 0, false)),
            ("smith", new(RestockHolderKind.Npc, SmithSource, false, true, 0, false)), ("box", null) })
        {
            using var owner = Owner();
            var world = new World { Merchant = merchant };
            if (facts is { } given) world.Facts["smith"] = given;
            Schedule(owner, "chest");
            var fired = Fire(owner, world).Restocks.Single();
            Assert.AreEqual(RestockStep.None, fired.Decision.Step, merchant);
            Assert.AreEqual(0, owner.QueryRestocks().Count, "not scheduled again (RK3)");
        }
        using var plain = Owner();
        var sourceless = new World();
        sourceless.Facts["smith"] = sourceless.Facts["smith"] with { InventorySource = 0 };
        Schedule(plain, "chest");
        var only = Fire(plain, sourceless).Restocks.Single();
        Assert.AreEqual(new RestockDecision(RestockStep.RegenerateMerchantAndContainer, false), only.Decision);
        Assert.AreEqual("chest", only.Regenerations.Single().Holder);
        CollectionAssert.AreEqual(new[] { "purse", "hammer", "ring" }, List(plain, "smith"));
    }

    [TestMethod]
    public void A_container_that_regenerates_alone_does_so_on_screen()
    {
        using var owner = Owner();
        Schedule(owner, "box");
        var fired = Fire(owner, new World()).Restocks.Single();
        Assert.AreEqual(RestockStep.Regenerate, fired.Decision.Step);
        Assert.AreEqual((0, 40), (fired.Regenerations.Single().Removed, fired.Regenerations.Single().Coins));
    }

    [TestMethod]
    public void Sets_that_do_not_read_clear_or_keep_the_list()
    {
        using var owner = Owner();
        var world = new World();
        world.Sets[SmithSource] = new(SessionRestockSetStatus.Unreachable, null);
        Schedule(owner, "smith");
        var revision = owner.Version.Revision;
        var done = Fire(owner, world).Restocks.Single().Regenerations.Single();
        Assert.AreEqual((SessionRestockOutcome.Cleared, 2, 0), (done.Outcome, done.Removed, done.Created.Count), "cleared, nothing generated (RK7)");
        Assert.AreEqual(revision + 1, owner.Version.Revision);
        CollectionAssert.AreEqual(new[] { "ring" }, List(owner, "smith"));

        using var kept = Owner();
        world.Sets[SmithSource] = new(SessionRestockSetStatus.Unsupported, null);
        Schedule(kept, "smith");
        revision = kept.Version.Revision;
        done = Fire(kept, world).Restocks.Single().Regenerations.Single();
        Assert.AreEqual((SessionRestockOutcome.Unsupported, 0), (done.Outcome, done.Removed));
        Assert.AreEqual(revision, kept.Version.Revision, "an unsupported set changes nothing");
        CollectionAssert.AreEqual(new[] { "purse", "hammer", "ring" }, List(kept, "smith"));
        // A set without lines is not one the rules read.
        world.Sets[SmithSource] = new(SessionRestockSetStatus.Read, null);
        Schedule(kept, "smith");
        Assert.AreEqual(SessionRestockOutcome.Unsupported, Fire(kept, world).Restocks.Single().Regenerations.Single().Outcome);
    }

    [TestMethod]
    public void Refused_items_are_destroyed_and_a_persistent_pile_takes_the_coins()
    {
        using var owner = Owner();
        var world = new World { Insert = item => item.Facts.Kind.Type == SessionItemType.Weapon ? SessionInsertVerdict.TooHeavy : SessionInsertVerdict.Accepted };
        world.Kept.Add("purse");
        Schedule(owner, "smith");
        var done = Fire(owner, world).Restocks.Single().Regenerations.Single();
        Assert.AreEqual((25, "purse", 1), (done.Coins, done.Pile, done.Refused), "the sword is refused and destroyed");
        Assert.AreEqual(35, owner.QueryNpcGold("smith"));
        CollectionAssert.AreEqual(new[] { "purse", "ring", "made:1" }, List(owner, "smith"));
    }

    [TestMethod]
    public void Without_a_restock_host_or_the_maps_seed_the_event_waits_a_minute()
    {
        using var owner = Owner();
        Schedule(owner, "smith");
        var fired = Fire(owner, new NoHost()).Restocks.Single();
        Assert.IsTrue(fired.Postponed);
        Assert.AreEqual(owner.QueryClockSnapshot().GameTimestampMs + SessionEventKinds.RestockRetryDelayMs, owner.QueryRestocks().Single().DeadlineMs);
        Assert.IsTrue(Fire(owner, new World(), SessionEventKinds.RestockRetryDelayMs).Restocks.Single() is { Postponed: false, Decision.Step: RestockStep.Regenerate });
    }

    [TestMethod]
    public void Holders_the_session_cannot_regenerate_keep_their_lists()
    {
        // A container generated once (flag 0x200) is the first-open slice's.
        using var once = Owner();
        var world = new World();
        world.Facts["box"] = world.Facts["box"] with { ContainerFlags = RestockRules.RegeneratesAloneFlag | RestockRules.GeneratedOnceFlag };
        Schedule(once, "box");
        var revision = once.Version.Revision;
        Assert.AreEqual(SessionRestockOutcome.Unsupported, Fire(once, world).Restocks.Single().Regenerations.Single().Outcome);
        Assert.AreEqual(revision, once.Version.Revision);
        // A holder with a transfer script would refuse every created item after the clear (the script is not run).
        var scripted = Seed() with { Holders = [.. Seed().Holders.Select(h => h.Entity == "smith" ? h with { ScriptPoints = [SessionScriptPoint.Transfer] } : h)] };
        using var owner = Owner(seed: scripted);
        Schedule(owner, "smith");
        Assert.AreEqual(SessionRestockOutcome.Unsupported, Fire(owner, new World()).Restocks.Single().Regenerations.Single().Outcome);
        CollectionAssert.AreEqual(new[] { "purse", "hammer", "ring" }, List(owner, "smith"));
    }

    [TestMethod]
    public void A_clear_the_deleted_item_budget_cannot_hold_waits_a_minute()
    {
        using var owner = Owner(new GameSessionLimits(maxDurabilityRecords: 1));
        Schedule(owner, "smith");
        var revision = owner.Version.Revision;
        var fired = Fire(owner, new World()).Restocks.Single();
        Assert.AreEqual((true, GameSessionFailure.None, 0), (fired.Postponed, fired.ScheduleFailure, fired.Regenerations.Count));
        Assert.AreEqual(revision, owner.Version.Revision);
        CollectionAssert.AreEqual(new[] { "purse", "hammer", "ring" }, List(owner, "smith"), "nothing is cleared");
        Assert.AreEqual(owner.QueryClockSnapshot().GameTimestampMs + SessionEventKinds.RestockRetryDelayMs, owner.QueryRestocks().Single().DeadlineMs);
    }

    private sealed class ScriptWorld(params string[] present) : World, ISessionScriptWorld
    {
        public bool ObjectExists(SessionMapId map, string entity) => present.Contains(entity);
        public bool RunScriptCall(SessionScriptCall call) => true;
    }

    [TestMethod]
    public void A_host_that_checks_objects_answers_for_restock_holders_too()
    {
        using var owner = Owner();
        Schedule(owner, "chest");
        var pass = Fire(owner, new ScriptWorld());
        Assert.AreEqual(0, pass.Restocks.Count, "a missing holder's event is dropped by the queue (RK3: nothing)");
        Assert.AreEqual(0, owner.QueryRestocks().Count);
        Schedule(owner, "box");
        Assert.AreEqual(RestockStep.Regenerate, Fire(owner, new ScriptWorld("box")).Restocks.Single().Decision.Step);
    }

    private sealed class NoHost : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    [TestMethod]
    public void The_event_and_the_discarded_made_number_are_saved()
    {
        using var owner = Owner();
        var world = new World();
        world.Sets[SmithSource] = new(SessionRestockSetStatus.Unreachable, null);
        Assert.AreEqual("made:1", owner.AddItemToNpc(owner.Version, "smith", Sword, _ => SessionInsertVerdict.Accepted).Item);
        Schedule(owner, "smith");
        Fire(owner, world);
        Schedule(owner, "chest");
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"lastDiscardedMade\":1,\"grid\":[");
        StringAssert.Contains(text, "\"kind\":\"inventory.restock\"");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual(owner.QueryRestocks().Single(), restored.QueryRestocks().Single());
        Assert.AreEqual(GameSessionFailure.None, restored.AttachMapInventory(Exterior, Seed()).Failure);
        // No made item is left, and a new one still never takes the discarded number.
        Assert.AreEqual("made:2", restored.AddItemToNpc(restored.Version, "smith", Sword, _ => SessionInsertVerdict.Accepted).Item);
        // A saved event naming the player or a made item, or with a delay no schedule makes, is refused.
        var deadline = owner.QueryRestocks().Single().DeadlineMs;
        foreach (var damaged in new[] { text.Replace("\"map\":79,\"entity\":\"chest\"", "\"map\":79,\"entity\":\"pc\""), text.Replace("\"map\":79,\"entity\":\"chest\"", "\"map\":79,\"entity\":\"made:7\""),
            text.Replace($"\"deadlineMs\":{deadline}", $"\"deadlineMs\":{deadline + 1}") })
        {
            Assert.AreNotEqual(text, damaged);
            Assert.AreEqual(GameSessionFailure.InvalidEvents, GameSessionOwner.Restore(Decode(damaged), RestoreOptions()).Failure);
        }
    }
}

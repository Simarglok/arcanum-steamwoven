using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-WORLD-123 (ledger KY1–KY4): a key item the hero takes is folded into the key list (0x466640 → 0x466A00 → 0x466A50) and the list
/// feeds the open attempt (0x4AEB70, 0x4AED80 → 0x463370). Entities, prototypes and key numbers are invented.
/// </summary>
[TestClass]
public sealed class HeroKeySourceTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionInsertCheck NeverAsked = _ => throw new AssertFailedException("a key going onto the ring asks no insert check");

    private static SessionItemFacts KeyFacts(int name, int? keyId, int complexity = 0, params int[] scripts) =>
        new(new(SessionItemType.Key, 12000 + name, name, 30000 + name), 0, complexity, 0, null, scripts) { KeyId = keyId };

    private static SessionMapInventorySeed Seed() => new(
        [
            new("corpse", SessionHolderKind.Npc, true, false, [], ["key-c", "coat"]),
            new("giver", SessionHolderKind.Npc, false, false, [], ["key-g"]),
        ],
        [
            new("key-a", KeyFacts(9101, 30), new CellCoord(101, 200)),
            new("key-b", KeyFacts(9102, 4), new CellCoord(102, 200)),
            new("key-open", KeyFacts(9103, 1), new CellCoord(103, 200)),
            new("key-none", KeyFacts(9104, null), new CellCoord(104, 200)),
            new("key-script", KeyFacts(9105, 6, scripts: SessionScriptPoint.Get), new CellCoord(105, 200)),
            new("key-neg", KeyFacts(9106, -2), new CellCoord(106, 200)),
            new("key-c", KeyFacts(9107, 7), null),
            new("coat", InventoryStateTests.Facts(SessionItemType.Armor, 2800), null),
            new("key-g", KeyFacts(9108, 8), null),
        ]);

    private static GameSessionOwner Hero(GameSessionLimits? limits = null)
    {
        var result = GameSessionOwner.Create(Descriptor(limits) with { Hero = HeroStateTests.HalfElf() });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        var owner = result.Owner!;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, Seed()).Disposition);
        return owner;
    }

    private static void Folded(InventoryMutationResult result, GameSessionOwner owner, string item, long revision)
    {
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, item, (SessionInsertVerdict?)SessionInsertVerdict.Accepted),
            (result.Disposition, result.Failure, result.Item, result.Verdict));
        Assert.AreEqual(new GameSessionVersion(owner.Identity, revision), owner.Version, "one revision");
        Assert.AreEqual(owner.Version, result.Version);
    }

    private static void Refused(InventoryMutationResult result, GameSessionOwner owner, GameSessionFailure failure, long revision)
    {
        Assert.AreEqual((GameSessionDisposition.Rejected, failure, (string?)null), (result.Disposition, result.Failure, result.Item));
        Assert.AreEqual(new GameSessionVersion(owner.Identity, revision), owner.Version);
    }

    [TestMethod]
    public void A_key_picked_up_goes_onto_the_ring_in_order_and_leaves_the_ground_and_the_list()
    {
        using var owner = Hero();
        Folded(owner.PickUpItem(owner.Version, "key-b", NeverAsked), owner, "key-b", 1);
        Folded(owner.PickUpItem(owner.Version, "key-a", NeverAsked), owner, "key-a", 2);
        CollectionAssert.AreEqual(new[] { 4, 30 }, owner.QueryHeroKeys()!.ToArray(), "appended in the order taken");
        Assert.AreEqual(0, owner.QueryPcInventory().Count, "the key item is destroyed, not held");
        var ground = owner.QueryMapInventory(Exterior)!.Ground.Select(g => g.Item.Entity).ToArray();
        CollectionAssert.DoesNotContain(ground, "key-a");
        CollectionAssert.DoesNotContain(ground, "key-b");
        Refused(owner.PickUpItem(owner.Version, "key-a", Accept), owner, GameSessionFailure.ItemNotAvailable, 2);
        // The list is the ring's: a number held already is added again by a second key of the same number.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroKey(owner.Version, 4).Disposition);
        CollectionAssert.AreEqual(new[] { 4, 30, 4 }, owner.QueryHeroKeys()!.ToArray());
    }

    [TestMethod]
    public void A_key_looted_from_a_body_or_given_by_a_negative_IN_goes_onto_the_ring_too()
    {
        using var owner = Hero();
        Folded(owner.LootItem(owner.Version, "corpse", "key-c", NeverAsked), owner, "key-c", 1);
        Folded(owner.GiveItemToPc(owner.Version, "giver", 9108, NeverAsked), owner, "key-g", 2);
        CollectionAssert.AreEqual(new[] { 7, 8 }, owner.QueryHeroKeys()!.ToArray());
        var holders = owner.QueryMapInventory(Exterior)!.Holders;
        CollectionAssert.AreEqual(new[] { "coat" }, holders.Single(h => h.Entity == "corpse").Items.Select(i => i.Entity).ToArray());
        Assert.AreEqual(0, holders.Single(h => h.Entity == "giver").Items.Count);
        // The other items of the body still go through the host's insert check into the list.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LootItem(owner.Version, "corpse", "coat", Accept).Disposition);
        CollectionAssert.AreEqual(new[] { "coat" }, owner.QueryPcInventory().Select(i => i.Entity).ToArray());
    }

    [TestMethod]
    public void A_key_item_with_no_number_or_a_negative_one_changes_nothing_and_a_script_needs_its_host()
    {
        using var owner = Hero();
        Refused(owner.PickUpItem(owner.Version, "key-none", Accept), owner, GameSessionFailure.InvalidItem, 0);
        Refused(owner.PickUpItem(owner.Version, "key-neg", Accept), owner, GameSessionFailure.InvalidItem, 0);
        Refused(owner.PickUpItem(owner.Version, "key-script", Accept), owner, GameSessionFailure.ItemScriptUnsupported, 0);
        Assert.AreEqual(0, owner.QueryHeroKeys()!.Count);
        Assert.AreEqual(6, owner.QueryMapInventory(Exterior)!.Ground.Count(g => g.Item.Entity.StartsWith("key-", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void A_full_ring_refuses_the_key_and_leaves_it_where_it_lies()
    {
        using var owner = Hero();
        for (var i = 0; i < SessionStoryLimits.MaxKeys; i++) Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroKey(owner.Version, i).Disposition);
        var revision = owner.Version.Revision;
        Refused(owner.PickUpItem(owner.Version, "key-a", Accept), owner, GameSessionFailure.KeyRingFull, revision);
        Assert.IsTrue(owner.QueryMapInventory(Exterior)!.Ground.Any(g => g.Item.Entity == "key-a"));
        Assert.AreEqual(SessionStoryLimits.MaxKeys, owner.QueryHeroKeys()!.Count);
    }

    [TestMethod]
    public void A_session_without_a_hero_holds_no_ring_and_refuses_the_key_as_before()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, Seed()).Disposition);
        Refused(owner.PickUpItem(owner.Version, "key-a", Accept), owner, GameSessionFailure.ItemTypeUnsupported, 0);
        Assert.IsNull(owner.QueryHeroKeys());
        Assert.AreEqual(OpenAttempt.Locked, owner.AttemptOpen(new(Exterior, "sector:1:7"), new(OpenableKind.Portal, LockRules.LockedBit, 12, 0), false));
    }

    [TestMethod]
    public void The_ring_and_the_destroyed_key_item_survive_the_snapshot()
    {
        using var owner = Hero();
        Folded(owner.PickUpItem(owner.Version, "key-a", Accept), owner, "key-a", 1);
        Folded(owner.LootItem(owner.Version, "corpse", "key-c", Accept), owner, "key-c", 2);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        using var restored = Restore(Decode(text));
        CollectionAssert.AreEqual(new[] { 30, 7 }, restored.QueryHeroKeys()!.ToArray());
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        // The seed is attached again: the folded keys are not offered again.
        Assert.AreEqual(GameSessionDisposition.Changed, restored.AttachMapInventory(Exterior, Seed()).Disposition);
        var view = restored.QueryMapInventory(Exterior)!;
        Assert.IsFalse(view.Ground.Any(g => g.Item.Entity == "key-a"));
        CollectionAssert.AreEqual(new[] { "coat" }, view.Holders.Single(h => h.Entity == "corpse").Items.Select(i => i.Entity).ToArray());
        Refused(restored.PickUpItem(restored.Version, "key-a", Accept), restored, GameSessionFailure.ItemNotAvailable, 2);
    }

    [TestMethod]
    public void A_seed_item_carries_a_key_number_only_when_it_is_a_key()
    {
        using var owner = Create();
        var seed = Seed();
        var odd = seed with { Items = [.. seed.Items, new("apple", InventoryStateTests.Facts(SessionItemType.Food, 3600) with { KeyId = 5 }, new CellCoord(110, 200))] };
        Assert.AreEqual(GameSessionDisposition.Rejected, owner.AttachMapInventory(Exterior, odd).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, seed).Disposition);
    }

    [TestMethod]
    public void The_open_attempt_looks_up_the_hero_keys_with_the_lock_match_rule()
    {
        var door = new SessionOpenable(Exterior, "sector:1:7");
        var locked = new SessionOpenableBaseline(OpenableKind.Portal, LockRules.LockedBit, 12, 0);
        var chest = new SessionOpenable(Exterior, "oid:chest");
        var box = new SessionOpenableBaseline(OpenableKind.Container, LockRules.LockedBit, 12, 0);
        using var owner = Hero();
        Assert.AreEqual(OpenAttempt.Locked, owner.AttemptOpen(door, locked, false), "no key");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroKey(owner.Version, 11).Disposition);
        Assert.AreEqual(OpenAttempt.Locked, owner.AttemptOpen(door, locked, false), "another key");
        Assert.AreEqual(OpenAttempt.Locked, owner.AttemptOpen(chest, box, false));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroKey(owner.Version, 12).Disposition);
        Assert.AreEqual(OpenAttempt.Opens, owner.AttemptOpen(door, locked, false), "the lock's own key");
        Assert.AreEqual(OpenAttempt.Opens, owner.AttemptOpen(chest, box, false), "a container takes keys as well");
        // A jammed or magically held object refuses before the keys are read (LK7).
        Assert.AreEqual(OpenAttempt.Jammed, owner.AttemptOpen(door, locked with { Flags = LockRules.LockedBit | LockRules.JammedBit }, false));
        // A lock of 0 takes no key; a lock of 1 takes any key.
        Assert.AreEqual(OpenAttempt.Locked, owner.AttemptOpen(door, locked with { LockKey = 0 }, false));
        Assert.AreEqual(OpenAttempt.Opens, owner.AttemptOpen(door, locked with { LockKey = 1 }, false));
        // A folded key of 1 opens any lock; a key of 0 opens none.
        using var master = Hero();
        Folded(master.PickUpItem(master.Version, "key-open", Accept), master, "key-open", 1);
        Assert.AreEqual(OpenAttempt.Opens, master.AttemptOpen(door, locked with { LockKey = 99 }, false));
        Assert.AreEqual(OpenAttempt.Locked, master.AttemptOpen(door, locked with { LockKey = 0 }, false));
        using var zero = Hero();
        Assert.AreEqual(GameSessionDisposition.Changed, zero.AddHeroKey(zero.Version, 0).Disposition);
        Assert.AreEqual(OpenAttempt.Locked, zero.AttemptOpen(door, locked with { LockKey = 0 }, false));
        Assert.AreEqual(OpenAttempt.Locked, zero.AttemptOpen(door, locked, false));
    }
}

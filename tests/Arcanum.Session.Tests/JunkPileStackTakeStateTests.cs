using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-UI-1328: a part of a gold pile or an ammunition stack taken out of a junk pile, after the quantity window (ledger QW1, QW5, NT1, AM4, JP5):
// the session moves the count and leaves the rest in the pile; a stack taken whole leaves it; the pile's dissolve rules hold. The half-elf hero
// holds 385 coins and 30 bullets (made:2). Entities and numbers are invented.
[TestClass]
public sealed class JunkPileStackTakeStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemKind GoldPile = new(SessionItemType.Gold, 9076, 3200, 33200);
    private static readonly SessionItemKind Bullets = new(SessionItemType.Ammo, 7059, 3301, 33301);

    private static GameSessionOwner Hero(GameSessionLimits? limits = null)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf(), limits);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        var npcStack = owner.AddItemToNpc(owner.Version, "virgil", Bullets, Accept, (1, 30)).Item!;
        Assert.AreEqual("made:2", owner.TakeAmmoFromNpc(owner.Version, "virgil", npcStack, 30, Bullets, Accept).Item);
        return owner;
    }

    // Two gold piles (made:3 with 50, made:4 with 70) and a bullet stack (made:5 with 10) dropped on the hero's tile: one junk pile of three items.
    private static (GameSessionOwner Owner, string Pile) Piled()
    {
        var owner = Hero();
        Assert.AreEqual("made:3", owner.DropHeroGold(owner.Version, 50, GoldPile).Item);
        Assert.AreEqual("made:4", owner.DropHeroGold(owner.Version, 70, GoldPile).Item);
        Assert.AreEqual("made:5", owner.DropHeroAmmo(owner.Version, "made:2", 10, Bullets).Item);
        var pile = owner.QueryJunkPiles().Single();
        Assert.AreEqual(3, pile.Items.Count);
        return (owner, pile.Entity);
    }

    private static int Coins(GameSessionOwner owner, string item) =>
        owner.QueryMapInventory(Exterior)!.JunkPiles.Single().Items.Single(entry => entry.Entity == item).Facts.Quantity ?? 0;

    [TestMethod]
    public void Part_of_a_gold_pile_moves_to_the_hero_and_the_rest_stays_in_the_junk_pile()
    {
        var (owner, pile) = Piled();
        using var _ = owner;
        var gold = owner.QueryGold();
        var revision = owner.Version.Revision;
        var taken = owner.TakeGoldFromJunkPile(owner.Version, pile, "made:4", 20);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "made:4", revision + 1),
            (taken.Disposition, taken.Failure, taken.Item, owner.Version.Revision), "one revision");
        Assert.AreEqual(gold + 20, owner.QueryGold());
        Assert.AreEqual(50, Coins(owner, "made:4"), "the rest stays in the pile");
        Assert.AreEqual(3, owner.QueryJunkPiles().Single().Items.Count, "the pile holds the same items");
        Assert.IsFalse(owner.IsItemDestroyed("made:4"));
        Assert.AreEqual(2, owner.QueryItemDecays().Count(decay => decay.Entity is "made:3" or "made:4"), "a part taken leaves the decay of the rest");
    }

    [TestMethod]
    public void A_whole_gold_pile_leaves_the_junk_pile_which_stands_until_the_window_closes_then_dissolves()
    {
        var (owner, pile) = Piled();
        using var _ = owner;
        var gold = owner.QueryGold();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.TakeGoldFromJunkPile(owner.Version, pile, "made:4", 70).Disposition);
        Assert.AreEqual(gold + 70, owner.QueryGold());
        Assert.IsTrue(owner.IsItemDestroyed("made:4"));
        CollectionAssert.AreEquivalent(new[] { "made:3", "made:5" }, owner.QueryJunkPiles().Single().Items.Select(item => item.Entity).ToArray());
        Assert.IsFalse(owner.QueryItemDecays().Any(decay => decay.Entity == "made:4"), "the decay of the emptied pile ends");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.TakeGoldFromJunkPile(owner.Version, pile, "made:3", 50).Disposition);
        Assert.AreEqual(1, owner.QueryJunkPiles().Single().Items.Count, "the window shows the pile, so it stands with one item");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.CloseJunkPile(owner.Version, pile).Disposition, "the close dissolves a pile of one item (JP5)");
        Assert.AreEqual(0, owner.QueryJunkPiles().Count);
        Assert.IsTrue(owner.QueryMapInventory(Exterior)!.Ground.Any(lying => lying.Item.Entity == "made:5" && lying.Cell == Start.Cell), "its item lies on the cell again");
    }

    [TestMethod]
    public void Part_of_an_ammunition_stack_merges_into_the_heros_stack_and_the_rest_stays()
    {
        var (owner, pile) = Piled();
        using var _ = owner;
        Assert.AreEqual(20, owner.QueryAmmo("made:2")!.Value.Units);
        var taken = owner.TakeAmmoFromJunkPile(owner.Version, pile, "made:5", 4, Bullets, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "made:2"), (taken.Disposition, taken.Failure, taken.Item));
        Assert.AreEqual((24, 6), (owner.QueryAmmo("made:2")!.Value.Units, owner.QueryAmmo("made:5")!.Value.Units));
        Assert.AreEqual(3, owner.QueryJunkPiles().Single().Items.Count);
        Assert.AreEqual(6, Coins(owner, "made:5"), "the pile's line shows the rest");
        // The rest, whole: the stack goes into the hero's.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.TakeAmmoFromJunkPile(owner.Version, pile, "made:5", 6, Bullets, Accept).Disposition);
        Assert.IsTrue(owner.IsItemDestroyed("made:5"));
        Assert.AreEqual(30, owner.QueryAmmo("made:2")!.Value.Units);
        Assert.AreEqual(2, owner.QueryJunkPiles().Single().Items.Count);
    }

    [TestMethod]
    public void Part_of_a_stack_makes_a_new_stack_for_a_hero_with_none()
    {
        var (owner, pile) = Piled();
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DropHeroAmmo(owner.Version, "made:2", 20, Bullets).Disposition);
        Assert.AreEqual(0, owner.QueryHeroAmmo().Count);
        var stack = owner.QueryJunkPiles().Single().Items.Single(item => item.Entity != "made:3" && item.Entity != "made:4" && item.Entity != "made:5").Entity;
        var taken = owner.TakeAmmoFromJunkPile(owner.Version, pile, stack, 7, Bullets, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (taken.Disposition, taken.Failure));
        Assert.AreEqual(7, owner.QueryAmmo(taken.Item!)!.Value.Units, "a new stack of the units taken");
        Assert.AreEqual(13, owner.QueryAmmo(stack)!.Value.Units, "the rest stays in the pile");
    }

    [TestMethod]
    public void Refusals_and_a_count_of_zero_change_nothing()
    {
        var (owner, pile) = Piled();
        using var _ = owner;
        var version = owner.Version;
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.TakeGoldFromJunkPile(owner.Version, pile, "made:4", 0).Disposition, "a count of 0 moves nothing (QW5)");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.TakeAmmoFromJunkPile(owner.Version, pile, "made:5", 0, Bullets, Accept).Disposition);
        Assert.AreEqual(GameSessionFailure.GoldShort, owner.TakeGoldFromJunkPile(owner.Version, pile, "made:4", 71).Failure);
        Assert.AreEqual(GameSessionFailure.AmmoShort, owner.TakeAmmoFromJunkPile(owner.Version, pile, "made:5", 11, Bullets, Accept).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.TakeGoldFromJunkPile(owner.Version, pile, "made:4", -1).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidAmmo, owner.TakeAmmoFromJunkPile(owner.Version, pile, "made:5", 1, GoldPile, Accept).Failure);
        Assert.AreEqual(GameSessionFailure.UnknownHolder, owner.TakeGoldFromJunkPile(owner.Version, "junk:99", "made:4", 1).Failure);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.TakeGoldFromJunkPile(owner.Version, pile, "made:5", 1).Failure, "an ammunition stack is no gold pile");
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.TakeAmmoFromJunkPile(owner.Version, pile, "made:4", 1, Bullets, Accept).Failure);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.TakeGoldFromJunkPile(owner.Version, pile, "made:99", 1).Failure, "not in the pile");
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.TakeGoldFromJunkPile(new(version.Owner, version.Revision + 5), pile, "made:4", 1).Failure);
        Assert.AreEqual(version, owner.Version, "no refusal changed the session");
        Assert.AreEqual(70, Coins(owner, "made:4"));
    }

    [TestMethod]
    public void The_rest_of_a_stack_survives_a_save()
    {
        var (owner, pile) = Piled();
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.TakeGoldFromJunkPile(owner.Version, pile, "made:4", 25).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.TakeAmmoFromJunkPile(owner.Version, pile, "made:5", 3, Bullets, Accept).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        using var restored = Restore(Decode(text), RestoreOptions());
        Assert.AreEqual(45, restored.Capture().Inventory.Piles.Single(pile => pile.Entity == "made:4").Quantity);
        Assert.AreEqual(7, restored.QueryAmmo("made:5")!.Value.Units);
        Assert.AreEqual(owner.QueryGold(), restored.QueryGold());
        Assert.AreEqual(3, restored.QueryJunkPiles().Single().Items.Count);
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()), "canonical");
    }
}

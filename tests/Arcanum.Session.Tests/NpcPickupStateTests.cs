using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-AI-1309 (0x42AA70 → 0x4617F0 with an NPC as the new parent): an NPC's ground pickup. Entities and numbers are invented; the half-elf fixture's kit (kit:1..kit:3) is the hero's placed items.
[TestClass]
public sealed class NpcPickupStateTests
{
    private const string Npc = "oid:scavenger";
    private static readonly CellCoord SwordCell = new(90, 200), ArrowsCell = new(91, 200), There = new(120, 210);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemFacts Arrows = new(new(SessionItemType.Ammo, 1500, 1500, 31500), 1, 0, 0, 5, []) { AmmoType = 0 };

    private static SessionMapInventorySeed Seed(bool script = false)
    {
        var quiver = new SessionItemFacts(new(SessionItemType.Ammo, 1500, 1500, 31500), 1, 0, 0, 3, []) { AmmoType = 0 };
        return new([new(Npc, SessionHolderKind.Npc, false, false, [], ["quiver"])],
        [
            new("quiver", quiver, null),
            new("sword", InventoryStateTests.Facts(SessionItemType.Weapon, 2901), SwordCell),
            new("arrows", Arrows, ArrowsCell),
            new("coins", InventoryStateTests.Facts(SessionItemType.Gold, 3000, quantity: 10), new CellCoord(92, 200)),
            new("trapped", InventoryStateTests.Facts(SessionItemType.Weapon, 2902, 0, 0, null, script ? [SessionScriptPoint.Get] : []), new CellCoord(93, 200)),
        ]);
    }

    private static GameSessionOwner Open(bool script = false)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed(script)).Failure);
        return owner;
    }

    private static string[] Listed(GameSessionOwner owner) =>
        owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == Npc).Items.Select(item => item.Entity).ToArray();

    private static string[] OnGround(GameSessionOwner owner) => owner.QueryMapInventory(Exterior)!.Ground.Select(g => g.Item.Entity).ToArray();

    [TestMethod]
    public void ALooseSeedItemGoesToTheEndOfTheNpcsListAndLeavesTheGround()
    {
        using var owner = Open();
        var before = owner.Version;
        var result = owner.PickUpItemByNpc(owner.Version, Npc, "sword", Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "sword"), (result.Disposition, result.Failure, result.Item));
        Assert.AreEqual(before.Revision + 1, owner.Version.Revision, "one revision");
        CollectionAssert.AreEqual(new[] { "quiver", "sword" }, Listed(owner));
        CollectionAssert.DoesNotContain(OnGround(owner), "sword");
        Assert.IsTrue(owner.QueryDanger(Npc).Flags.HasFlag(DangerFlags.CheckWield), "the insert asks the NPC to re-wield");
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.PickUpItem(owner.Version, "sword", Accept).Failure, "the hero finds nothing there");
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.PickUpItemByNpc(owner.Version, Npc, "sword", Accept).Failure, "nor does a second pickup");
    }

    [TestMethod]
    public void AmmunitionMergesIntoTheNpcsStackOfItsPrototypeAndTheItemIsDeleted()
    {
        using var owner = Open();
        var result = owner.PickUpItemByNpc(owner.Version, Npc, "arrows", Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, "quiver"), (result.Disposition, result.Item));
        Assert.AreEqual(8, owner.QueryAmmo("quiver")!.Value.Units, "3 and 5 units");
        CollectionAssert.AreEqual(new[] { "quiver" }, Listed(owner));
        Assert.IsTrue(owner.IsItemDestroyed("arrows"));
        Assert.IsFalse(owner.QueryDanger(Npc).Flags.HasFlag(DangerFlags.CheckWield), "a merge sets no re-wield bit");
    }

    [TestMethod]
    public void TheHerosPlacedItemGoesToTheNpcAndComesBackToItsKitNumberWhenTheHeroTakesItFromTheNpc()
    {
        using var owner = Open();
        owner.PlaceHeroItem("kit:2", There);
        var result = owner.PickUpItemByNpc(owner.Version, Npc, "kit:2", Accept);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition, result.Failure.ToString());
        CollectionAssert.Contains(Listed(owner), "kit:2");
        Assert.AreEqual(0, owner.QueryPlacedItems().Count);
        CollectionAssert.AreEqual(new[] { "kit:2" }, owner.QueryGivenItems().Select(item => item.Entity).ToArray(), "recorded as given, as a hand-over is");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.TakeItemFromNpc(owner.Version, Npc, "kit:2", Accept).Disposition);
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:2", "kit:3" }, owner.QueryHeroKit().Select(item => item.Entity).ToArray());
    }

    [TestMethod]
    public void AnItemOfAJunkPileLeavesThePileForTheNpc()
    {
        using var owner = Open();
        owner.PlaceHeroItem("kit:2", There);
        owner.PlaceHeroItem("kit:3", There);
        var pile = owner.QueryJunkPiles().Single();
        CollectionAssert.AreEquivalent(new[] { "kit:2", "kit:3" }, pile.Items.Select(item => item.Entity).ToArray());
        var result = owner.PickUpItemByNpc(owner.Version, Npc, "kit:2", Accept);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition, result.Failure.ToString());
        CollectionAssert.Contains(Listed(owner), "kit:2");
        CollectionAssert.AreEqual(new[] { "kit:3" }, owner.QueryJunkPiles().SelectMany(p => p.Items).Select(item => item.Entity).ToArray());
    }

    [TestMethod]
    public void RefusalsChangeNothing()
    {
        using var owner = Open(script: true);
        var before = owner.Version;
        Assert.AreEqual(GameSessionFailure.UnknownHolder, owner.PickUpItemByNpc(owner.Version, "oid:nobody", "sword", Accept).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.PickUpItemByNpc(owner.Version, Npc, "", Accept).Failure);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.PickUpItemByNpc(owner.Version, Npc, "no-such-item", Accept).Failure);
        Assert.AreEqual(GameSessionFailure.ItemTypeUnsupported, owner.PickUpItemByNpc(owner.Version, Npc, "coins", Accept).Failure, "gold is not an item of a list");
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.PickUpItemByNpc(owner.Version, Npc, "trapped", Accept).Failure, "the get script is not run for an NPC");
        var heavy = owner.PickUpItemByNpc(owner.Version, Npc, "sword", _ => SessionInsertVerdict.TooHeavy);
        Assert.AreEqual((GameSessionFailure.InsertRefused, SessionInsertVerdict.TooHeavy), (heavy.Failure, heavy.Verdict));
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.PickUpItemByNpc(before with { Revision = before.Revision + 5 }, Npc, "sword", Accept).Failure);
        Assert.AreEqual(before, owner.Version, "no revision moved");
        CollectionAssert.AreEqual(new[] { "quiver" }, Listed(owner));
        CollectionAssert.Contains(OnGround(owner), "sword");
    }

    [TestMethod]
    public void TheHerosTakenItemIsNotThereForTheNpc()
    {
        using var owner = Open();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "sword", Accept).Disposition);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.PickUpItemByNpc(owner.Version, Npc, "sword", Accept).Failure);
    }

    [TestMethod]
    public void ATakenItemStaysInTheNpcsListThroughASnapshotAndTheGroundStaysEmptyOfIt()
    {
        using var owner = Open();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItemByNpc(owner.Version, Npc, "sword", Accept).Disposition);
        owner.PlaceHeroItem("kit:2", There);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItemByNpc(owner.Version, Npc, "kit:2", Accept).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()), "canonical");
        Assert.AreEqual(GameSessionFailure.None, restored.AttachMapInventory(Exterior, Seed()).Failure);
        CollectionAssert.AreEqual(new[] { "quiver", "sword", "kit:2" }, Listed(restored));
        CollectionAssert.DoesNotContain(OnGround(restored), "sword");
    }

    [TestMethod]
    public void AnItemWithAComplexityThePcDroppedSurvivesASnapshotOfTheNpcsListWithNoComplexityOfItsOwn()
    {
        using var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        var gizmo = InventoryStateTests.Facts(SessionItemType.Weapon, 2905, 0, 7);
        var seed = new SessionMapInventorySeed([new(Npc, SessionHolderKind.Npc, false, false, [], [])], [new("gizmo", gizmo, SwordCell)]);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "gizmo", Accept).Disposition);
        owner.PlaceHeroItem("gizmo", There);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItemByNpc(owner.Version, Npc, "gizmo", Accept).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()), "canonical");
        Assert.AreEqual(GameSessionFailure.None, restored.AttachMapInventory(Exterior, seed).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, restored.TakeItemFromNpc(restored.Version, Npc, "gizmo", Accept).Disposition, "the hero can take it back");
    }
}

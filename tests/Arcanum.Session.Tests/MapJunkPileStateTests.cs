using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-DEV-310 (B04): a junk pile that lies in the map's data (a standing container of prototype 3043, such as the pile of the Crash Site
// cave) goes when the loot window closes on it with no item (or one item) left, as a pile the session made does (ledger JP4, JP5).
// Cells, entities and items are invented.
[TestClass]
public sealed class MapJunkPileStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly CellCoord PileCell = new(120, 210);

    private static SessionObjectTarget Pile(string entity = "pile", bool junk = true, SessionMapId? map = null) => new(map ?? Exterior, entity,
        new SessionObjectBaseline
        {
            Kind = DamageableObjectKind.Container, Points = 6, Material = ObjectMaterial.Cloth, JunkPile = junk, Cell = PileCell,
            Openable = new SessionOpenableBaseline(OpenableKind.Container, 0, 0, 0, junk),
        });

    private static SessionMapInventorySeed Seed(params string[] items) => Seed(false, items);

    // With a rag lying loose on the pile's cell when asked.
    private static SessionMapInventorySeed Seed(bool rag, string[] items) => new(
        [new("pile", SessionHolderKind.Container, false, false, [], items), new("chest", SessionHolderKind.Container, false, false, [], ["coin"])],
        [
            .. items.Select(item => new SessionSeedItem(item, InventoryStateTests.Facts(SessionItemType.Generic, 5650 + item.Length), null)),
            new SessionSeedItem("coin", InventoryStateTests.Facts(SessionItemType.Generic, 5700), null),
            .. rag ? [new SessionSeedItem("rag", InventoryStateTests.Facts(SessionItemType.Generic, 5651), PileCell)] : Array.Empty<SessionSeedItem>(),
        ]);

    private static GameSessionOwner Owner(params string[] items) => Owner(false, items);

    private static GameSessionOwner Owner(bool rag, string[] items)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed(rag, items)).Failure);
        return owner;
    }

    private static void Take(GameSessionOwner owner, string holder, string item) =>
        Assert.AreEqual(GameSessionFailure.None, owner.LootItem(owner.Version, holder, item, Accept).Failure, item);

    // ARC-AI-1486: an NPC picks an item out of a junk pile of the map's data (the pickup goal's end 0x42AA70 → 0x4617F0 → 0x461CA0 from a container).
    private static GameSessionOwner NpcOwner(params string[] items)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        var seed = Seed(false, items);
        seed = new([new("npc", SessionHolderKind.Npc, false, false, [], []), .. seed.Holders], seed.Items);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        return owner;
    }

    private static string[] NpcList(GameSessionOwner owner) => owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == "npc").Items.Select(item => item.Entity).ToArray();

    [TestMethod]
    public void AnItemOfAMapsJunkPileGoesToTheNpcAndAPileOfThreeStaysWithTwo()
    {
        using var owner = NpcOwner("cup", "knife", "rope");
        var revision = owner.Version.Revision;
        var result = owner.PickUpItemByNpc(owner.Version, "npc", "knife", Accept, Pile());
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "knife"), (result.Disposition, result.Failure, result.Item));
        Assert.AreEqual(revision + 1, owner.Version.Revision, "one revision");
        CollectionAssert.AreEqual(new[] { "knife" }, NpcList(owner));
        CollectionAssert.AreEqual(new[] { "cup", "rope" }, owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == "pile").Items.Select(item => item.Entity).ToArray());
        Assert.AreEqual(SessionObjectCondition.Whole, owner.QueryObject(Pile()).Condition, "two items keep it");
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.PickUpItemByNpc(owner.Version, "npc", "knife", Accept, Pile()).Failure, "it is gone from the pile");
    }

    [TestMethod]
    public void APileOfTwoDissolvesAtTheNpcsPickupAndDropsTheOtherItemOnItsCell()
    {
        using var owner = NpcOwner("cup", "knife");
        var result = owner.PickUpItemByNpc(owner.Version, "npc", "cup", Accept, Pile());
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition, result.Failure.ToString());
        Assert.AreEqual(SessionObjectCondition.Destroyed, owner.QueryObject(Pile()).Condition, "JP5: the removal checks the container");
        Assert.AreEqual(("knife", PileCell), owner.QueryMapInventory(Exterior)!.Ground.Select(g => (g.Item.Entity, g.Cell)).Single(g => g.Item1 == "knife"));
        Assert.IsTrue(owner.QueryItemDecays().Any(decay => decay.Entity == "knife"), "its decay starts over");
        CollectionAssert.AreEqual(new[] { "cup" }, NpcList(owner));
    }

    [TestMethod]
    public void TheLastItemTakenByTheNpcDissolvesTheEmptyMapPileAtOnce()
    {
        using var owner = NpcOwner("cup");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItemByNpc(owner.Version, "npc", "cup", Accept, Pile()).Disposition);
        Assert.AreEqual(SessionObjectCondition.Destroyed, owner.QueryObject(Pile()).Condition, "no item left: the removal's check destroys the pile");
        Assert.AreEqual(0, owner.QueryMapInventory(Exterior)!.Ground.Count, "and nothing is dropped");
    }

    [TestMethod]
    public void TheNpcsPickupFromAMapPileRefusesWhatIsNotThereAndChangesNothing()
    {
        using var owner = NpcOwner("cup", "knife");
        var before = owner.Version;
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.PickUpItemByNpc(owner.Version, "npc", "coin", Accept, Pile()).Failure, "the chest's item is not the pile's");
        Assert.AreEqual(GameSessionFailure.InvalidObject, owner.PickUpItemByNpc(owner.Version, "npc", "cup", Accept, Pile(junk: false)).Failure, "a chest is no junk pile");
        Assert.AreEqual(GameSessionFailure.UnknownHolder, owner.PickUpItemByNpc(owner.Version, "npc", "cup", Accept, Pile("no-such")).Failure);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.PickUpItemByNpc(owner.Version, "npc", "cup", Accept).Failure, "without the pile's target the item is no loose one");
        Assert.AreEqual(GameSessionFailure.InsertRefused, owner.PickUpItemByNpc(owner.Version, "npc", "cup", _ => SessionInsertVerdict.TooHeavy, Pile()).Failure);
        Assert.AreEqual(before, owner.Version, "no revision moved");
        Take(owner, "pile", "cup");
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.PickUpItemByNpc(owner.Version, "npc", "cup", Accept, Pile()).Failure, "the hero took it");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.CloseMapJunkPile(owner.Version, Pile()).Disposition);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.PickUpItemByNpc(owner.Version, "npc", "knife", Accept, Pile()).Failure, "the pile has gone");
    }

    [TestMethod]
    public void APileTakenEmptyStaysWhileTheWindowShowsItAndGoesOnClosing()
    {
        using var owner = Owner("cup", "knife");
        var pile = Pile();
        Take(owner, "pile", "knife");
        Take(owner, "pile", "cup");
        Assert.AreEqual(SessionObjectCondition.Whole, owner.QueryObject(pile).Condition, "the window shows the pile: it stays, empty");
        Assert.AreEqual(0, owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == "pile").Items.Count);

        var revision = owner.Version.Revision;
        var closed = owner.CloseMapJunkPile(owner.Version, pile);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (closed.Disposition, closed.Failure));
        Assert.AreEqual(revision + 1, owner.Version.Revision, "one revision");
        Assert.AreEqual(SessionObjectCondition.Destroyed, owner.QueryObject(pile).Condition);
        Assert.AreEqual(SessionObjectCondition.Destroyed, owner.QueryObjectCondition(Exterior, "pile", DamageableObjectKind.Container), "a host draws it no more");
        Assert.AreEqual(GameSessionFailure.HolderNotLootable, owner.LootItem(owner.Version, "pile", "cup", Accept).Failure, "and cannot loot it");
        Assert.AreEqual(0, owner.QueryJunkPiles().Count, "no pile of the session's is made");
        Assert.AreEqual(0, owner.QueryItemDecays().Count);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.CloseMapJunkPile(owner.Version, pile).Disposition, "a pile already gone");
    }

    [TestMethod]
    public void APileLeftWithOneItemDropsItOnItsCellWithAnewDecayEvent()
    {
        using var owner = Owner("cup", "knife");
        var pile = Pile();
        Take(owner, "pile", "cup");
        Assert.AreEqual(ClockMutationDisposition.Changed,
            owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 1000, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.CloseMapJunkPile(owner.Version, pile).Disposition);
        Assert.AreEqual(SessionObjectCondition.Destroyed, owner.QueryObject(pile).Condition);
        Assert.AreEqual(("knife", PileCell), owner.QueryMapInventory(Exterior)!.Ground.Select(g => (g.Item.Entity, g.Cell)).Single(g => g.Item1 == "knife"));
        Assert.AreEqual(owner.QueryClockSnapshot().GameTimestampMs + ItemDecayRules.DelayMs, owner.QueryItemDecays().Single(d => d.Entity == "knife").DeadlineMs);
        Assert.AreEqual(GameSessionFailure.None, owner.PickUpItem(owner.Version, "knife", Accept).Failure, "it lies loose on the ground");
    }

    [TestMethod]
    public void ALooseItemOnThePilesCellGathersWithTheReleasedOneIntoAPileOfTheSessions()
    {
        // The placement of the released item (JP1): the loose items on the tile are swept into a new pile with it.
        using var owner = Owner(true, ["cup", "knife"]);
        Take(owner, "pile", "cup");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.CloseMapJunkPile(owner.Version, Pile()).Disposition);
        var junk = owner.QueryJunkPiles().Single();
        Assert.AreEqual(PileCell, junk.Cell);
        CollectionAssert.AreEquivalent(new[] { "rag", "knife" }, junk.Items.Select(item => item.Entity).ToArray());
        Assert.AreEqual(SessionObjectCondition.Destroyed, owner.QueryObject(Pile()).Condition);
    }

    [TestMethod]
    public void APileWithTwoItemsOrMoreAnotherContainerAndAnOtherMapsPileChangeNothing()
    {
        using var owner = Owner("cup", "knife", "lamp");
        var pile = Pile();
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.CloseMapJunkPile(owner.Version, pile).Disposition, "three items stay");
        Take(owner, "pile", "lamp");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.CloseMapJunkPile(owner.Version, pile).Disposition, "two items stay");
        Assert.AreEqual(SessionObjectCondition.Whole, owner.QueryObject(pile).Condition);

        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionFailure.InvalidObject, owner.CloseMapJunkPile(owner.Version, Pile("chest", junk: false)).Failure, "a chest is no pile");
        Assert.AreEqual(GameSessionFailure.UnknownHolder, owner.CloseMapJunkPile(owner.Version, Pile("nobody")).Failure);
        Assert.AreEqual(GameSessionFailure.UnknownHolder, owner.CloseMapJunkPile(owner.Version, Pile(map: new SessionMapId(Exterior.RuntimeMap + 1))).Failure,
            "a pile of another map than the actor's");
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.CloseMapJunkPile(owner.Version with { Revision = 0 }, pile).Failure);
        Assert.AreEqual(revision, owner.Version.Revision, "nothing was written");
    }

    [TestMethod]
    public void AReleasedItemStaysOnThePilesCellWithItsDecayAcrossASave()
    {
        using var owner = Owner("cup", "knife");
        Take(owner, "pile", "cup");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.CloseMapJunkPile(owner.Version, Pile()).Disposition);
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(GameSessionFailure.None, restored.AttachMapInventory(Exterior, Seed("cup", "knife")).Failure);
        Assert.AreEqual(SessionObjectCondition.Destroyed, restored.QueryObject(Pile()).Condition);
        Assert.AreEqual(("knife", PileCell), restored.QueryMapInventory(Exterior)!.Ground.Select(g => (g.Item.Entity, g.Cell)).Single(g => g.Item1 == "knife"));
        Assert.IsTrue(restored.QueryItemDecays().Any(d => d.Entity == "knife"));
    }

    [TestMethod]
    public void ADissolvedPileStaysGoneAcrossASave()
    {
        using var owner = Owner("cup");
        Take(owner, "pile", "cup");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.CloseMapJunkPile(owner.Version, Pile()).Disposition);
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(GameSessionFailure.None, restored.AttachMapInventory(Exterior, Seed("cup")).Failure);
        Assert.AreEqual(SessionObjectCondition.Destroyed, restored.QueryObject(Pile()).Condition);
        Assert.AreEqual(GameSessionFailure.HolderNotLootable, restored.LootItem(restored.Version, "pile", "cup", Accept).Failure);
    }
}

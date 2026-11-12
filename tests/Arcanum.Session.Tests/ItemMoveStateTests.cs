using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-1230 (UO5, UO6): a map-data item a push moved lies on the cell of the push; a placed item's cell is its own record's. Invented map, no original data.
[TestClass]
public sealed class ItemMoveStateTests
{
    private const string Apple = "oid:apple", Stone = "oid:stone", Guard = "oid:guard", Sword = "oid:sword";
    private static readonly CellCoord AppleCell = new(90, 200), StoneCell = new(91, 200), There = new(120, 210), Beyond = new(121, 210), Further = new(122, 210);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private static GameSessionOwner Owner()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        var light = InventoryStateTests.Facts(SessionItemType.Food, 3600) with { Weight = 3 };
        var heavy = InventoryStateTests.Facts(SessionItemType.Generic, 3601) with { Weight = 40 };
        var weapon = InventoryStateTests.Facts(SessionItemType.Weapon, 3602);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new SessionMapInventorySeed(
            [new SessionSeedHolder(Guard, SessionHolderKind.Npc, false, false, [], [Sword])],
            [new SessionSeedItem(Apple, light, AppleCell), new SessionSeedItem(Stone, heavy, StoneCell), new SessionSeedItem(Sword, weapon, null)])).Failure);
        return owner;
    }

    private static string Text(GameSessionOwner owner) => GameSessionSnapshotCodec.Encode(owner.Capture());

    [TestMethod]
    public void AGroundItemAnswersItsCellAndWeightAndAnItemInNoPlaceIsNone()
    {
        using var owner = Owner();
        Assert.AreEqual(new SessionLyingItem(Apple, Exterior, AppleCell, SessionItemType.Food, 3), owner.QueryGroundItem(Apple));
        Assert.AreEqual(40, owner.QueryGroundItem(Stone)!.Weight);
        Assert.IsNull(owner.QueryGroundItem(Sword), "held by the guard");
        Assert.IsNull(owner.QueryGroundItem("kit:1"), "in the hero's pack");
        Assert.IsNull(owner.QueryGroundItem("oid:nothing"));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, Apple, Accept).Disposition);
        Assert.IsNull(owner.QueryGroundItem(Apple), "taken");
    }

    [TestMethod]
    public void AMapItemPushedKeepsTheCellOfThePushInOneRevision()
    {
        using var owner = Owner();
        var revision = owner.Version.Revision;
        var moved = owner.MoveGroundItem(owner.Version, Apple, There);
        Assert.AreEqual(GameSessionDisposition.Changed, moved.Disposition);
        Assert.AreEqual(revision + 1, owner.Version.Revision);
        Assert.AreEqual(There, owner.QueryGroundItem(Apple)!.Cell);
        CollectionAssert.AreEqual(new[] { (Apple, There), (Stone, StoneCell) }, owner.QueryMapInventory(Exterior)!.Ground.Select(g => (g.Item.Entity, g.Cell)).ToArray());
        CollectionAssert.AreEqual(new[] { (Apple, There) }, owner.QueryMovedItems().Select(p => (p.Key, p.Value)).ToArray());
        // The same cell changes nothing; a second push goes on from the first.
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.MoveGroundItem(owner.Version, Apple, There).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.MoveGroundItem(owner.Version, Apple, Beyond).Disposition);
        Assert.AreEqual(Beyond, owner.QueryGroundItem(Apple)!.Cell);
        // The cell is the item's loose cell: the hero takes it from there, and it leaves no ground behind.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, Apple, Accept).Disposition);
        CollectionAssert.AreEqual(new[] { Stone }, owner.QueryMapInventory(Exterior)!.Ground.Select(g => g.Item.Entity).ToArray());
    }

    [TestMethod]
    public void APushBackOntoTheDataCellRemovesTheRecordAndATakenItemKeepsNoGround()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.MoveGroundItem(owner.Version, Apple, There).Disposition);
        Assert.AreEqual(1, owner.QueryMovedItems().Count);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.MoveGroundItem(owner.Version, Apple, AppleCell).Disposition);
        Assert.AreEqual(0, owner.QueryMovedItems().Count, "no inert record");
        Assert.AreEqual(AppleCell, owner.QueryGroundItem(Apple)!.Cell);
        // Pushed, taken and dropped again: the placed record's cell rules, the old record is inert.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.MoveGroundItem(owner.Version, Apple, There).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, Apple, Accept).Disposition);
        owner.PlaceHeroItem(Apple, Further);
        Assert.AreEqual(Further, owner.QueryGroundItem(Apple)!.Cell);
        Assert.AreEqual(Further, owner.QueryMapInventory(Exterior)!.Ground.Single(g => g.Item.Entity == Apple).Cell);
    }

    [TestMethod]
    public void ThePushedCellIsSavedAndRestored()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.MoveGroundItem(owner.Version, Apple, There).Disposition);
        var text = Text(owner);
        StringAssert.Contains(text, SessionItemMove.Property);
        using var restored = Restore(Decode(text));
        Assert.AreEqual(GameSessionFailure.None, restored.AttachMapInventory(Exterior, new SessionMapInventorySeed([],
            [new SessionSeedItem(Apple, InventoryStateTests.Facts(SessionItemType.Food, 3600) with { Weight = 3 }, AppleCell)])).Failure);
        Assert.AreEqual(There, restored.QueryGroundItem(Apple)!.Cell);
        Assert.AreEqual(text, Text(restored), "canonical");
    }

    [TestMethod]
    public void APlacedItemMovesInItsOwnRecordAndKeepsItsTrackedWeight()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, Apple, Accept).Disposition);
        owner.PlaceHeroItem(Apple, There);
        Assert.AreEqual(There, owner.QueryGroundItem(Apple)!.Cell);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.MoveGroundItem(owner.Version, Apple, Beyond).Disposition);
        Assert.AreEqual(Beyond, owner.QueryPlacedItems().Single().Cell);
        Assert.AreEqual(Beyond, owner.QueryGroundItem(Apple)!.Cell);
        Assert.AreEqual(0, owner.QueryMovedItems().Count, "a placed item has no map-data record");
        Assert.AreEqual(3, owner.QueryGroundItem(Apple)!.Weight, "the weight the hero's pick-up kept");
    }

    [TestMethod]
    public void AnItemACriticalDroppedMovesOnFromItsDropCell()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForEntity(Sword, GameSessionOwner.ItemDroppedProperty),
            GameSessionOwner.ItemDroppedValue(There)).Disposition);
        Assert.AreEqual(There, owner.QueryGroundItem(Sword)!.Cell);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.MoveGroundItem(owner.Version, Sword, Further).Disposition);
        Assert.AreEqual(Further, owner.QueryGroundItem(Sword)!.Cell);
        Assert.AreEqual(Further, owner.QueryDroppedItem(Sword)!.Cell, "the lying-item lookup of the cast host follows");
    }

    [TestMethod]
    public void AMoveRefusesWhatLiesNowhereOnTheMapAndABadCellOrVersion()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.MoveGroundItem(owner.Version, Sword, There).Failure);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.MoveGroundItem(owner.Version, "kit:1", There).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.MoveGroundItem(owner.Version, Apple, new(-1, 4)).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.MoveGroundItem(owner.Version, Apple, new(1 << 24, 4)).Failure);
        var stale = owner.Version;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.MoveGroundItem(owner.Version, Apple, There).Disposition);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.MoveGroundItem(stale, Apple, Beyond).Failure);
        Assert.AreEqual(There, owner.QueryGroundItem(Apple)!.Cell);
    }

    [TestMethod]
    public void AResidentItemIsOnThePushedCell()
    {
        using var owner = Owner();
        Assert.AreEqual(new ResidencyOpenResult(ResidencyStatus.Open, 3, string.Empty), owner.OpenResidency(Exterior,
            [new(Guard, SessionResidentKind.Npc, new CellCoord(10, 20)), new(Apple, SessionResidentKind.Food, AppleCell), new(Sword, SessionResidentKind.Weapon, null, Guard)]));
        Assert.AreEqual(AppleCell, owner.QueryResidents(Exterior).Residents.Single(r => r.Entity == Apple).Cell);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.MoveGroundItem(owner.Version, Apple, There).Disposition);
        Assert.AreEqual(There, owner.QueryResidents(Exterior).Residents.Single(r => r.Entity == Apple).Cell);
    }
}

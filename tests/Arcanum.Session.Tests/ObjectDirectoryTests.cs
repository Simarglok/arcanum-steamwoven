using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-DEV-084 (MIG6, TM1): the object directory answers "where is X" over every store; junk and pool numbers are never given twice.
// The half-elf fixture's kit (kit:1..kit:3) and an apple (two cells wide) the PC takes from the exterior's seed, as JunkPileStateTests.
// A second seed item, "stone", lies on the ground and stays there; "bag" is a seed item in the seed holder "chest". Values are invented.
[TestClass]
public sealed class ObjectDirectoryTests
{
    private static readonly CellCoord AppleCell = new(90, 200), StoneCell = new(91, 200), There = new(120, 210), Beside = new(121, 210);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemFacts AppleFacts = InventoryStateTests.Facts(SessionItemType.Food, 3600) with { InventoryWidth = 2 };

    private static GameSessionOwner Owner()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        var stone = InventoryStateTests.Facts(SessionItemType.Generic, 3601);
        var seed = new SessionMapInventorySeed(
            [new SessionSeedHolder("chest", SessionHolderKind.Container, false, false, [], ["bag"]), new SessionSeedHolder("smith", SessionHolderKind.Npc, false, false, [], [])],
            [new SessionSeedItem("apple", AppleFacts, AppleCell), new SessionSeedItem("stone", stone, StoneCell), new SessionSeedItem("bag", stone, null)]);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "apple", Accept).Disposition);
        return owner;
    }

    private static void Advance(GameSessionOwner owner, long ms) => Assert.AreEqual(ClockMutationDisposition.Changed,
        owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);

    [TestMethod]
    public void AnEntityNamesItsOrigin()
    {
        Assert.AreEqual(SessionObjectOrigin.Kit, GameSessionOwner.ObjectOriginOf("kit:3"));
        Assert.AreEqual(SessionObjectOrigin.Made, GameSessionOwner.ObjectOriginOf("made:12"));
        Assert.AreEqual(SessionObjectOrigin.JunkPile, GameSessionOwner.ObjectOriginOf("junk:1"));
        Assert.AreEqual(SessionObjectOrigin.BloodPool, GameSessionOwner.ObjectOriginOf("pool:7"));
        Assert.AreEqual(SessionObjectOrigin.MapObject, GameSessionOwner.ObjectOriginOf("oid:abc"));
        Assert.AreEqual(SessionObjectOrigin.MapObject, GameSessionOwner.ObjectOriginOf("made:x"), "a prefix without a number is no counter's id");
    }

    [TestMethod]
    public void TheDirectoryFindsAnObjectInEveryPlaceTheHeroCanPutIt()
    {
        using var owner = Owner();
        Assert.AreEqual(new SessionObjectEntry("kit:1", SessionObjectOrigin.Kit, SessionObjectPlace.HeroPack), owner.QueryObject("kit:1"));
        Assert.AreEqual(new SessionObjectEntry("apple", SessionObjectOrigin.MapObject, SessionObjectPlace.HeroPack), owner.QueryObject("apple"), "taken from the seed");
        Assert.AreEqual(new SessionObjectEntry("stone", SessionObjectOrigin.MapObject, SessionObjectPlace.Ground, Exterior, StoneCell), owner.QueryObject("stone"));
        Assert.AreEqual(new SessionObjectEntry("bag", SessionObjectOrigin.MapObject, SessionObjectPlace.SeedHolder, Exterior, Holder: "chest"), owner.QueryObject("bag"));
        Assert.IsNull(owner.QueryObject("nothing"));
        Assert.IsNull(owner.QueryObject(""), "an invalid entity is unknown, not an error");

        owner.PlaceHeroItem("kit:2", There);
        Assert.AreEqual(new SessionObjectEntry("kit:2", SessionObjectOrigin.Kit, SessionObjectPlace.Ground, Exterior, There), owner.QueryObject("kit:2"));
        owner.PlaceHeroItem("apple", There);
        var piled = owner.QueryObject("kit:2")!;
        Assert.AreEqual((SessionObjectPlace.JunkPile, "junk:1", There), (piled.Place, piled.Pile, piled.Cell), "a second drop on the cell gathers both");
        Assert.AreEqual(new SessionObjectEntry("junk:1", SessionObjectOrigin.JunkPile, SessionObjectPlace.Ground, Exterior, There), owner.QueryObject("junk:1"));
        Assert.AreEqual(SessionObjectPlace.JunkPile, owner.QueryObject("apple")!.Place);
    }

    [TestMethod]
    public void AWornItemAndAGivenItemHaveTheirPlaces()
    {
        using var owner = Owner();
        var mail = new EquipmentItem { Kind = EquipmentItemKind.Armor, Armor = new EquipmentArmor { Coverage = ArmorCoverage.Torso, Sizes = ArmorSizes.Medium } };
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, "kit:2", EquipmentSlot.Armor, new(Kit[1].Kind, [], false, mail), (_, _) => true, Accept).Failure);
        Assert.AreEqual(new SessionObjectEntry("kit:2", SessionObjectOrigin.Kit, SessionObjectPlace.HeroWorn, Slot: EquipmentSlot.Armor), owner.QueryObject("kit:2"));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, "smith", "kit:1", Accept).Disposition);
        Assert.AreEqual(new SessionObjectEntry("kit:1", SessionObjectOrigin.Kit, SessionObjectPlace.NpcList, Holder: "smith"), owner.QueryObject("kit:1"),
            "a given kit item keeps its entity and lies in the NPC's list");
    }

    [TestMethod]
    public void ADeletedItemIsDestroyed()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyItem(owner.Version, "kit:3", new ItemCondition(10, 0, 0, false)).Disposition, "deleted");
        Assert.AreEqual(SessionObjectPlace.Destroyed, owner.QueryObject("kit:3")!.Place);
        CollectionAssert.AreEqual(new[] { SessionObjectPlace.HeroPack, SessionObjectPlace.HeroPack, SessionObjectPlace.Destroyed },
            owner.QueryObjects(SessionObjectOrigin.Kit).Select(entry => entry.Place).ToArray(), "the per-origin list agrees with the lookup");
    }

    [TestMethod]
    public void TheQueriesByOriginListTheStoresInEntityOrder()
    {
        using var owner = Owner();
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:2", "kit:3" }, owner.QueryObjects(SessionObjectOrigin.Kit).Select(entry => entry.Entity).ToArray());
        owner.PlaceHeroItem("kit:2", There);
        owner.PlaceHeroItem("kit:3", There);
        CollectionAssert.AreEqual(new[] { SessionObjectPlace.HeroPack, SessionObjectPlace.JunkPile, SessionObjectPlace.JunkPile },
            owner.QueryObjects(SessionObjectOrigin.Kit).Select(entry => entry.Place).ToArray());
        CollectionAssert.AreEqual(new[] { "junk:1" }, owner.QueryObjects(SessionObjectOrigin.JunkPile).Select(entry => entry.Entity).ToArray());
        CollectionAssert.AreEqual(new[] { "apple" }, owner.QueryObjects(SessionObjectOrigin.MapObject).Select(entry => entry.Entity).ToArray(), "the map objects the session moved");
        Assert.AreEqual(0, owner.QueryObjects(SessionObjectOrigin.Made).Count);
        Assert.AreEqual(0, owner.QueryObjects(SessionObjectOrigin.BloodPool).Count);
    }

    [TestMethod]
    public void ADissolvedPilesNumberIsNotGivenAgainAndTheMarkIsSaved()
    {
        using var owner = Owner();
        owner.PlaceHeroItem("kit:2", There);
        owner.PlaceHeroItem("apple", There);
        Assert.AreEqual(1, owner.Capture().Objects.LastJunkNumber);
        Assert.AreEqual(GameSessionFailure.None, owner.LootItem(owner.Version, "junk:1", "apple", Accept).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.CloseJunkPile(owner.Version, "junk:1").Disposition);
        Assert.AreEqual(0, owner.QueryJunkPiles().Count);
        Assert.IsNull(owner.QueryObject("junk:1"), "a dissolved pile is gone");

        owner.PlaceHeroItem("kit:3", Beside);
        owner.PlaceHeroItem("apple", Beside);
        Assert.AreEqual("junk:2", owner.QueryJunkPiles().Single().Entity, "the dissolved pile's number is not reused");
        Assert.AreEqual(2, owner.Capture().Objects.LastJunkNumber);

        // The mark survives a save and a load.
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"lastJunkNumber\":2,\"lastPoolNumber\":0,\"state\":[],\"links\":[],\"created\":[],\"lastCreatedNumber\":0}");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(2, restored.Capture().Objects.LastJunkNumber);
    }

    [TestMethod]
    public void ALoadedMarkNeverSitsBelowANumberASavedPileHolds()
    {
        using var owner = Owner();
        owner.PlaceHeroItem("kit:2", There);
        owner.PlaceHeroItem("apple", There);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture()).Replace(",\"lastJunkNumber\":1,", ",\"lastJunkNumber\":0,");
        StringAssert.Contains(text, ",\"lastJunkNumber\":0,");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(1, restored.Capture().Objects.LastJunkNumber, "the older load's rule: the highest number held now");
        // A negative mark is no save.
        var negative = GameSessionSnapshotCodec.TryDecode(text.Replace(",\"lastJunkNumber\":0,", ",\"lastJunkNumber\":-1,"));
        Assert.AreEqual(SnapshotDecodeFailure.OutOfRange, negative.Failure);
        Assert.AreEqual("$.objects.lastJunkNumber", negative.Path);
    }

    // ARC-DEV-088 (MIG6d): the lists of added items are directory entries; a holder's stock is one query.
    [TestMethod]
    public void AHoldersStockIsItsSeedItemsThenTheItemsAddedToIt()
    {
        using var owner = Owner();
        Assert.AreEqual(0, owner.QueryHeldBy("smith").Count);
        Assert.AreEqual(0, owner.QueryHeldBy("nobody").Count, "an unknown holder holds nothing");
        Assert.AreEqual(0, owner.QueryHeldBy("").Count, "an invalid entity is no holder, not an error");
        CollectionAssert.AreEqual(new[] { new SessionObjectEntry("bag", SessionObjectOrigin.MapObject, SessionObjectPlace.SeedHolder, Exterior, Holder: "chest") },
            owner.QueryHeldBy("chest").ToArray());

        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, "smith", "kit:1", Accept).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, "smith", "kit:2", Accept).Disposition);
        owner.CreateMadeItem(new("made:1", new(SessionItemType.Food, 3602, 0, 0), SessionCreatePlace.ContainerList, new(0, 0, 0), Holder: "chest"));
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:2" }, owner.QueryHeldBy("smith").Select(entry => entry.Entity).ToArray(), "in the order they entered");
        Assert.IsTrue(owner.QueryHeldBy("smith").All(entry => entry.Place == SessionObjectPlace.NpcList && entry.Holder == "smith"));
        CollectionAssert.AreEqual(new[] { SessionObjectPlace.SeedHolder, SessionObjectPlace.ContainerList },
            owner.QueryHeldBy("chest").Select(entry => entry.Place).ToArray(), "the seed's item first, then the one a container list holds");
        Assert.AreEqual("made:1", owner.QueryHeldBy("chest")[1].Entity);

        // The lists are saved as before, and a load builds the same entries.
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        CollectionAssert.AreEqual(owner.QueryHeldBy("smith").ToArray(), restored.QueryHeldBy("smith").ToArray());
        Assert.AreEqual(new SessionObjectEntry("made:1", SessionObjectOrigin.Made, SessionObjectPlace.ContainerList, Holder: "chest"), restored.QueryObject("made:1"));

        // An item that leaves a list is in no list any more.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyItem(owner.Version, "kit:1", new ItemCondition(10, 0, 0, false)).Disposition);
        CollectionAssert.AreEqual(new[] { "kit:2" }, owner.QueryHeldBy("smith").Select(entry => entry.Entity).ToArray());
        Assert.AreEqual(SessionObjectPlace.Destroyed, owner.QueryObject("kit:1")!.Place);
    }
}

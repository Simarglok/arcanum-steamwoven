using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-059: the hero's items the session placed on the ground, with the half-elf fixture's kit (kit:1..kit:3) and an apple the
// PC takes from the exterior's seed. Values are invented.
[TestClass]
public sealed class PlacedItemsStateTests
{
    private static readonly CellCoord AppleCell = new(90, 200), There = new(120, 210);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemFacts AppleFacts = InventoryStateTests.Facts(SessionItemType.Food, 3600);

    private static SessionMapInventorySeed Seed() => new([], [new SessionSeedItem("apple", AppleFacts, AppleCell)]);

    private static GameSessionOwner Owner()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "apple", Accept).Disposition);
        return owner;
    }

    private static string Text(GameSessionOwner owner) => GameSessionSnapshotCodec.Encode(owner.Capture());

    [TestMethod]
    public void PlacedItemsLieOnTheirMapUntilPickedUpAndGoBackToTheirList()
    {
        using var owner = Owner();
        var revision = owner.Version.Revision;
        owner.PlaceHeroItem("kit:2", There);
        owner.PlaceHeroItem("apple", There);
        Assert.AreEqual(revision + 2, owner.Version.Revision, "one revision each");
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:3" }, owner.QueryHeroKit().Select(item => item.Entity).ToArray());
        Assert.AreEqual(0, owner.QueryPcInventory().Count);
        var placed = owner.QueryPlacedItems();
        Assert.AreEqual((SessionPlacedSource.Kit, (SessionMapId?)null), (placed[0].Source, placed[0].Origin));
        Assert.AreEqual((SessionPlacedSource.Pc, (SessionMapId?)Exterior, Exterior, There), (placed[1].Source, placed[1].Origin, placed[1].Map, placed[1].Cell));
        // The apple lies where it was placed, not at its seed cell.
        CollectionAssert.AreEqual(new[] { ("kit:2", There), ("apple", There) },
            owner.QueryMapInventory(Exterior)!.Ground.Select(g => (g.Item.Entity, g.Cell)).ToArray());

        // On another map it cannot be picked up.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyChanges(owner.Version, Start with { Map = new(80) }, []).Disposition);
        Assert.AreEqual(GameSessionFailure.InventoryUnavailable, owner.PickUpItem(owner.Version, "kit:2", Accept).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyChanges(owner.Version, Start, []).Disposition);

        // A refused insert changes nothing; an accepted one returns the kit item to its number's place and the apple to the end.
        Assert.AreEqual(GameSessionFailure.InsertRefused, owner.PickUpItem(owner.Version, "kit:2", _ => SessionInsertVerdict.TooHeavy).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "kit:2", Accept).Disposition);
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:2", "kit:3" }, owner.QueryHeroKit().Select(item => item.Entity).ToArray());
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "apple", Accept).Disposition);
        Assert.AreEqual(new SessionPcItem("apple", Exterior, AppleFacts.Kind), owner.QueryPcInventory().Single());
        Assert.AreEqual(0, owner.QueryPlacedItems().Count);
        Assert.AreEqual(0, owner.QueryMapInventory(Exterior)!.Ground.Count, "the apple is held again, so its seed cell stays empty");
    }

    [TestMethod]
    public void PlacedItemsAreSavedAndADeletedOneLeavesTheStore()
    {
        using var owner = Owner();
        owner.PlaceHeroItem("kit:1", There);
        owner.PlaceHeroItem("apple", There);
        var text = Text(owner);
        StringAssert.Contains(text, ",\"placed\":[{\"entity\":\"kit:1\",\"source\":\"kit\",\"origin\":0,\"map\":79,\"x\":120,\"y\":210,");
        StringAssert.Contains(text, "{\"entity\":\"apple\",\"source\":\"pc\",\"origin\":79,\"map\":79,\"x\":120,\"y\":210,");
        using var restored = Restore(Decode(text));
        CollectionAssert.AreEqual(owner.QueryPlacedItems().ToArray(), restored.QueryPlacedItems().ToArray());
        Assert.AreEqual(text, Text(restored), "canonical");
        // A reattached seed must still list the placed apple, with its kind.
        Assert.AreEqual(GameSessionFailure.InventoryMismatch, restored.AttachMapInventory(Exterior, new([], [])).Failure);
        Assert.AreEqual(GameSessionFailure.None, restored.AttachMapInventory(Exterior, Seed()).Failure);
        Assert.AreEqual(GameSessionFailure.InventoryMismatch,
            restored.AttachMapInventory(new(80), new([], [new SessionSeedItem("kit:1", AppleFacts, AppleCell)])).Failure, "no seed lists a kit item");

        var baseline = new ItemCondition(10, 0, 0, false);
        Assert.AreEqual(GameSessionDisposition.Changed, restored.DestroyItem(restored.Version, "apple", baseline).Disposition);
        Assert.AreEqual("kit:1", restored.QueryPlacedItems().Single().Entity);
    }

    [TestMethod]
    public void DecodeAndRestoreRefusePlacedItemsNoDropProduces()
    {
        using var owner = Owner();
        owner.PlaceHeroItem("kit:3", There);
        owner.PlaceHeroItem("apple", There);
        var text = Text(owner);
        const string Kit = "{\"entity\":\"kit:3\",\"source\":\"kit\",\"origin\":0,\"map\":79,\"x\":120,\"y\":210,";
        const string Apple = "{\"entity\":\"apple\",\"source\":\"pc\",\"origin\":79,";
        Assert.IsTrue(text.Contains(Kit) && text.Contains(Apple));
        void Fails(string changed, SnapshotDecodeFailure failure, string path)
        {
            var decoded = GameSessionSnapshotCodec.TryDecode(changed);
            Assert.AreEqual((failure, path), (decoded.Failure, decoded.Path));
        }
        Fails(text.Replace(Kit, Kit.Replace("\"kit\",", "\"bag\",")), SnapshotDecodeFailure.OutOfRange, "$.inventory.placed[0].source");
        Fails(text.Replace(Kit, Kit.Replace("\"origin\":0", "\"origin\":79")), SnapshotDecodeFailure.NotCanonical, "$.inventory.placed[0].origin");
        Fails(text.Replace(Apple, Apple.Replace("\"origin\":79", "\"origin\":0")), SnapshotDecodeFailure.NotCanonical, "$.inventory.placed[1].origin");
        Fails(text.Replace(Kit, Kit.Replace("\"x\":120", "\"x\":16777216")), SnapshotDecodeFailure.OutOfRange, "$.inventory.placed[0].x");
        Fails(text.Replace(Kit, Kit.Replace("\"y\":210", "\"y\":-1")), SnapshotDecodeFailure.OutOfRange, "$.inventory.placed[0].y");
        Fails(text.Replace(Kit, Kit.Replace("\"map\":79", "\"map\":0")), SnapshotDecodeFailure.OutOfRange, "$.inventory.placed[0].map");
        Fails(text.Replace(Kit, Kit.Replace("kit:3", "kit:1")), SnapshotDecodeFailure.NotCanonical, "$.inventory.placed[0].entity");

        void Refused(string changed, GameSessionFailure failure) =>
            Assert.AreEqual(failure, GameSessionOwner.Restore(Decode(changed), RestoreOptions()).Failure);
        Assert.AreEqual(GameSessionFailure.None, GameSessionOwner.Restore(Decode(text), RestoreOptions()).Failure);
        // Each refused for its own reason: no kit number, a kit number beyond the kit's (kit:1, kit:2 held, one placed, none deleted),
        // a made item without a made number (the last kit number placed, so the kit's own numbering still holds), a world item with a
        // kit id, a deleted placed item.
        Refused(text.Replace(Kit, Kit.Replace("kit:3", "kit:9x")), GameSessionFailure.InvalidSnapshot);
        Refused(text.Replace(Kit, Kit.Replace("kit:3", "kit:4")), GameSessionFailure.InvalidSnapshot);
        Refused(text.Replace(Kit, Kit.Replace("\"source\":\"kit\"", "\"source\":\"made\"")), GameSessionFailure.InvalidSnapshot);
        Refused(text.Replace(Apple, Apple.Replace("\"apple\"", "\"kit:5\"")), GameSessionFailure.InvalidSnapshot);
        Refused(text.Replace("\"destroyed\":[]", "\"destroyed\":[\"apple\"]"), GameSessionFailure.InvalidSnapshot);

        // Without a hero nothing can have been placed.
        using var heroless = GameSessionOwner.Create(Descriptor()).Owner!;
        var none = Text(heroless);
        Assert.IsTrue(none.Contains(",\"placed\":[]}"));
        Refused(none.Replace(",\"placed\":[]}", ",\"placed\":[{\"entity\":\"made:1\",\"source\":\"made\",\"origin\":0,\"map\":79,\"x\":1,\"y\":2," +
            "\"type\":\"Generic\",\"prototype\":1,\"name\":2,\"description\":3}]}"), GameSessionFailure.InvalidSnapshot);
    }

    [TestMethod]
    public void APlacedKitItemComesBackInPlaceAgainAndAgainWithoutTheSeed()
    {
        using var owner = Owner();
        owner.PlaceHeroItem("kit:1", There);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "kit:1", Accept).Disposition);
        owner.PlaceHeroItem("kit:1", There);
        // Restored without its seed attached, the kit item can still be picked up: it needs no seed.
        using var restored = Restore(Decode(Text(owner)));
        Assert.AreEqual(GameSessionDisposition.Changed, restored.PickUpItem(restored.Version, "kit:1", Accept).Disposition);
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:2", "kit:3" }, restored.QueryHeroKit().Select(item => item.Entity).ToArray());

        // An insert check that changes the session refuses as stale.
        owner.PlaceHeroItem("apple", There);
        var stale = owner.PickUpItem(owner.Version, "apple", _ =>
        {
            owner.ApplyChanges(owner.Version, null, [new(Exterior, MapChangeKey.ForEntity("x", "y"), 1)]);
            return SessionInsertVerdict.Accepted;
        });
        Assert.AreEqual(GameSessionFailure.StaleVersion, stale.Failure);
        Assert.IsNotNull(owner.QueryPlacedItems().SingleOrDefault(item => item.Entity == "apple"));

        // A world item whose seed shows a get script is not picked up from the store either.
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior,
            new([], [new SessionSeedItem("apple", AppleFacts with { ScriptPoints = [SessionScriptPoint.Get] }, AppleCell)])).Failure);
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.PickUpItem(owner.Version, "apple", Accept).Failure);
    }

    [TestMethod]
    public void APlacedMadeItemKeepsItsNumber()
    {
        using var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior,
            new([new("smith", SessionHolderKind.Npc, false, false, [], [])], [])).Failure);
        var kind = new SessionItemKind(SessionItemType.Generic, 2320, 5, 6);
        Assert.AreEqual("made:1", owner.AddItemToNpc(owner.Version, "smith", kind, Accept).Item);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToPc(owner.Version, "smith", 5, Accept).Disposition);
        owner.PlaceHeroItem("made:1", There);
        Assert.AreEqual("made:2", owner.AddItemToNpc(owner.Version, "smith", kind, Accept).Item, "made:1 lies on the ground");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "made:1", Accept).Disposition);
        CollectionAssert.AreEqual(new[] { "made:1" }, owner.QueryMadeItems().Select(item => item.Entity).ToArray());
    }
}

using Arcanum.Data.Technology;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Technology;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-083: a made item keeps its magick/tech complexity through every move, as the original's object keeps field 0x60 (ledger MC1):
// a refused tech product on the ground stays refused for a Technophobia hero, a dropped or given made item comes back with it, and the
// snapshot keeps it. Two neutral parts (2120) make a technological product (2320, complexity −5); the smith holds two parts of its own.
// Everything is invented.
[TestClass]
public sealed class MadeItemComplexityStateTests
{
    private static readonly SessionItemKind Part = new(SessionItemType.Generic, 2120, 1, 2);
    private static readonly SessionItemKind Product = new(SessionItemType.Generic, 2320, 5, 6);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionInsertCheck Full = _ => SessionInsertVerdict.NoRoom;
    private static readonly SessionCraftLookup InBag = _ => new SessionCraftFacts(10);
    private static readonly CellCoord SmithCell = new(104, 201);

    private static ComponentFacts? TechFacts(int prototype) => prototype switch { 2120 => new(0, 3), 2320 => new(-5, 3), _ => null };
    private static ComponentFacts? NeutralFacts(int prototype) => prototype == 2120 ? new(0, 3) : null;
    private static SessionItemKind? Kinds(int prototype) => prototype == 2320 ? Product : null;

    private static SchematicRecord Schematic(int quantity) => new()
    {
        Number = 2000, Kind = SchematicKind.Learned, NameText = 1, DescriptionText = 2, Art = 3,
        Component1 = new(Array.AsReadOnly(new[] { 2100 }), 1), Component2 = new(Array.AsReadOnly(new[] { 2100 }), 1),
        Product = new(Array.AsReadOnly(new[] { 2300 }), 1), Quantity = quantity, MissingLines = Array.AsReadOnly(new bool[7]),
    };

    // The smith holds two parts; a dead stranger holds nothing; a technological cog (complexity −3) lies on the ground.
    private static SessionMapInventorySeed SmithSeed()
    {
        var facts = new SessionItemFacts(Part, 5, 0, 10, null, []);
        return new([new("smith", SessionHolderKind.Npc, false, false, [], ["s1", "s2"]), new("corpse", SessionHolderKind.Npc, true, false, [], [])],
            [new("s1", facts, null), new("s2", facts, null),
                new("cog", new(new(SessionItemType.Generic, 5600, 7, 8), 1, -3, 0, null, []), new CellCoord(101, 200))]);
    }

    private static GameSessionOwner Hero(int background)
    {
        var owner = CreateWith(HalfElf() with { Kit = [new(Part), new(Part), new(Part)], Background = background });
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, SmithSeed()).Disposition);
        return owner;
    }

    private static SessionNpcCrafter Smith() =>
        new(new("smith", new(CharacterRace.Human, false, [8, 8, 8, 8, 8, 8, 8, 8], 0, 0, 1, 0, [.. new int[12]], [.. new int[4]],
            [.. new int[16]], [0, 0, 0, 0, 0, 0, 0, 0], 1, 2, 0, 0), 0), AdvancementModifiers.None);

    // The owner saved and restored with another background (no character creation sets one), the smith's seed attached again.
    private static GameSessionOwner AsTechnophobe(GameSessionOwner owner)
    {
        var saved = GameSessionSnapshotCodec.Encode(owner.Capture());
        Assert.AreEqual(1, saved.Split(",\"background\":0,\"backgroundDescription\":0,\"playerName\":\"\",\"portrait\":0}").Length - 1, "one hero background");
        var text = saved.Replace(",\"background\":0,\"backgroundDescription\":0,\"playerName\":\"\",\"portrait\":0}", ",\"background\":72,\"backgroundDescription\":0,\"playerName\":\"\",\"portrait\":0}", StringComparison.Ordinal);
        var restored = Restore(Decode(text));
        Assert.AreEqual(GameSessionDisposition.Changed, restored.AttachMapInventory(Exterior, SmithSeed()).Disposition);
        return restored;
    }

    [TestMethod]
    public void A_technophobe_cannot_pick_up_its_refused_tech_product_before_or_after_a_restore()
    {
        using var owner = Hero(SessionBackgrounds.Technophobia);
        var result = owner.AssembleSchematic(owner.Version, Schematic(1), InBag, TechFacts, Kinds, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, (SessionInsertVerdict?)SessionInsertVerdict.TechnologyRefused),
            (result.Disposition, result.Verdict), "the product is refused (code 15) and lies on the hero's tile");
        Assert.AreEqual(-5, owner.QueryPlacedItems().Single().Complexity, "the product's complexity on the ground");
        Assert.AreEqual(-5, owner.QueryMapInventory(Exterior)!.Ground.Single(g => g.Item.Entity == "made:1").Item.Facts.Complexity,
            "the host's insert check sees it too");
        var revision = owner.Version.Revision;
        var pickup = owner.PickUpItem(owner.Version, "made:1", Accept);
        Assert.AreEqual((GameSessionFailure.InsertRefused, (SessionInsertVerdict?)SessionInsertVerdict.TechnologyRefused, revision),
            (pickup.Failure, pickup.Verdict, owner.Version.Revision), "still refused when picked up");
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        CollectionAssert.AreEqual(owner.QueryPlacedItems().ToArray(), restored.QueryPlacedItems().ToArray(), "saved with its complexity");
        Assert.AreEqual(SessionInsertVerdict.TechnologyRefused, restored.PickUpItem(restored.Version, "made:1", Accept).Verdict);

        // A neutral product the inventory refused for another reason is carried once there is room.
        using var neutral = Hero(SessionBackgrounds.Technophobia);
        Assert.AreEqual(GameSessionDisposition.Changed, neutral.AssembleSchematic(neutral.Version, Schematic(1), InBag, NeutralFacts, Kinds, Full).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, neutral.PickUpItem(neutral.Version, "made:1", Accept).Disposition, "complexity 0 is carried");
    }

    [TestMethod]
    public void A_made_tech_item_keeps_its_complexity_when_dropped_picked_up_and_saved()
    {
        using var owner = Hero(SessionBackgrounds.None);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AssembleSchematic(owner.Version, Schematic(1), InBag, TechFacts, Kinds, Accept).Disposition);
        CollectionAssert.AreEqual(new[] { new SessionMadeItem("made:1", Product, -5) }, owner.QueryMadeItems().ToArray(), "in the list");
        owner.PlaceHeroItem("made:1", new CellCoord(120, 210));
        Assert.AreEqual(-5, owner.QueryPlacedItems().Single().Complexity, "on the ground");
        using var technophobe = AsTechnophobe(owner);
        Assert.AreEqual(SessionInsertVerdict.TechnologyRefused, technophobe.PickUpItem(technophobe.Version, "made:1", Accept).Verdict,
            "the dropped item is the same object");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "made:1", Accept).Disposition);
        CollectionAssert.AreEqual(new[] { new SessionMadeItem("made:1", Product, -5) }, owner.QueryMadeItems().ToArray(), "back in the list");
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        CollectionAssert.AreEqual(owner.QueryMadeItems().ToArray(), restored.QueryMadeItems().ToArray());
    }

    [TestMethod]
    public void A_made_tech_item_given_to_an_npc_keeps_its_complexity_and_a_technophobe_cannot_take_it_back()
    {
        using var owner = Hero(SessionBackgrounds.None);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AssembleSchematic(owner.Version, Schematic(1), InBag, TechFacts, Kinds, Accept).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, "smith", "made:1", Accept).Disposition);
        CollectionAssert.AreEqual(new[] { new SessionMadeItem("made:1", Product, -5) }, owner.QueryNpcInventory("smith").ToArray());
        Assert.AreEqual(-5, owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == "smith").Items.Single(i => i.Entity == "made:1").Facts.Complexity);
        using var technophobe = AsTechnophobe(owner);
        Assert.AreEqual(SessionInsertVerdict.TechnologyRefused, technophobe.TakeItemFromNpc(technophobe.Version, "smith", "made:1", Accept).Verdict);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.TakeItemFromNpc(owner.Version, "smith", "made:1", Accept).Disposition);
        CollectionAssert.AreEqual(new[] { new SessionMadeItem("made:1", Product, -5) }, owner.QueryMadeItems().ToArray(), "back with it");
    }

    [TestMethod]
    public void A_taken_tech_item_given_to_an_npc_is_answered_by_its_seed_and_refused_when_taken_back()
    {
        using var owner = Hero(SessionBackgrounds.None);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "cog", Accept).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, "smith", "cog", Accept).Disposition);
        StringAssert.Contains(GameSessionSnapshotCodec.Encode(owner.Capture()), "\"entity\":\"cog\",\"type\":\"Generic\",\"prototype\":5600,\"name\":7,\"description\":8,\"complexity\":0}",
            "the seed answers for a given taken item");
        Assert.AreEqual(-3, owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == "smith").Items.Single(i => i.Entity == "cog").Facts.Complexity);
        using var technophobe = AsTechnophobe(owner);
        Assert.AreEqual(SessionInsertVerdict.TechnologyRefused, technophobe.TakeItemFromNpc(technophobe.Version, "smith", "cog", Accept).Verdict);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.TakeItemFromNpc(owner.Version, "smith", "cog", Accept).Disposition);
        Assert.AreEqual("cog", owner.QueryPcInventory().Single().Entity, "back among the taken items");
    }

    [TestMethod]
    public void A_technophobe_cannot_loot_or_be_given_a_made_tech_item_a_dead_npc_holds()
    {
        using var owner = Hero(SessionBackgrounds.None);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AssembleSchematic(owner.Version, Schematic(1), InBag, TechFacts, Kinds, Accept).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, "corpse", "made:1", Accept).Disposition);
        using var technophobe = AsTechnophobe(owner);
        Assert.AreEqual(SessionInsertVerdict.TechnologyRefused, technophobe.LootItem(technophobe.Version, "corpse", "made:1", Accept).Verdict, "loot");
        Assert.AreEqual(SessionInsertVerdict.TechnologyRefused, technophobe.GiveItemToPc(technophobe.Version, "corpse", 5, Accept).Verdict, "negative IN");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LootItem(owner.Version, "corpse", "made:1", Accept).Disposition);
    }

    [TestMethod]
    public void An_npc_crafters_tech_products_keep_their_complexity_on_its_tile_and_in_its_list()
    {
        using var owner = Hero(SessionBackgrounds.Technophobia);
        var ground = owner.AssembleSchematicForNpc(owner.Version, Smith(), Schematic(1), InBag, TechFacts, Kinds, Full, crafterCell: SmithCell);
        Assert.AreEqual(GameSessionDisposition.Changed, ground.Disposition);
        Assert.AreEqual(SessionInsertVerdict.TechnologyRefused, owner.PickUpItem(owner.Version, "made:1", Accept).Verdict, "the smith's refused product");

        using var held = Hero(SessionBackgrounds.Technophobia);
        Assert.AreEqual(GameSessionDisposition.Changed, held.AssembleSchematicForNpc(held.Version, Smith(), Schematic(1), InBag, TechFacts, Kinds, Accept).Disposition,
            "an NPC has no background");
        CollectionAssert.AreEqual(new[] { new SessionMadeItem("made:1", Product, -5) }, held.QueryNpcInventory("smith").ToArray());
        Assert.AreEqual(SessionInsertVerdict.TechnologyRefused, held.TakeItemFromNpc(held.Version, "smith", "made:1", Accept).Verdict);
    }

    [TestMethod]
    public void The_snapshot_needs_a_made_items_complexity_and_keeps_none_on_another_placed_item()
    {
        using var owner = Hero(SessionBackgrounds.None);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AssembleSchematic(owner.Version, Schematic(1), InBag, TechFacts, Kinds, Accept).Disposition);
        owner.PlaceHeroItem("kit:3", new CellCoord(120, 210));
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"description\":6,\"complexity\":-5}");
        void Fails(string from, string to, SnapshotDecodeFailure failure, string path)
        {
            Assert.AreEqual(1, text.Split(from).Length - 1, from);
            var decoded = GameSessionSnapshotCodec.TryDecode(text.Replace(from, to, StringComparison.Ordinal));
            Assert.AreEqual((failure, path), (decoded.Failure, decoded.Path));
        }
        Fails("\"description\":6,\"complexity\":-5}", "\"description\":6}", SnapshotDecodeFailure.MissingField, "$.inventory.made[0].complexity");
        Fails("\"description\":2,\"complexity\":0}", "\"description\":2,\"complexity\":-1}", SnapshotDecodeFailure.NotCanonical, "$.inventory.placed[0].complexity");

        // In an NPC's list: required, and only a made item keeps its own (a given item's seed answers).
        using var giver = Hero(SessionBackgrounds.None);
        Assert.AreEqual(GameSessionDisposition.Changed, giver.AssembleSchematic(giver.Version, Schematic(1), InBag, TechFacts, Kinds, Accept).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, giver.GiveItemToNpc(giver.Version, "smith", "made:1", Accept).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, giver.GiveItemToNpc(giver.Version, "smith", "kit:3", Accept).Disposition);
        text = GameSessionSnapshotCodec.Encode(giver.Capture());
        Fails("\"description\":6,\"complexity\":-5}", "\"description\":6}", SnapshotDecodeFailure.MissingField, "$.inventory.npcs[0].items[0].complexity");
        Assert.AreEqual(GameSessionFailure.None, GameSessionOwner.Restore(Decode(text), RestoreOptions()).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(Decode(text.Replace("\"description\":2,\"complexity\":0}",
            "\"description\":2,\"complexity\":-1}", StringComparison.Ordinal)), RestoreOptions()).Failure, "a given kit item");
    }
}

using Arcanum.Data.Technology;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Technology;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-070: a product unit the inventory refuses lies on the crafter's tile (the original's creation on the tile, 0x4ED8B0, and
// unchecked transfer, 0x4617F0; ledger SA3–SA5), for the hero and an NPC crafter, ammunition included. Two neutral parts (2120) make
// a product (2320); the smith holds two parts of its own. Everything is invented.
[TestClass]
public sealed class AssemblyGroundStateTests
{
    private static readonly SessionItemKind Part = new(SessionItemType.Generic, 2120, 1, 2);
    private static readonly SessionItemKind Product = new(SessionItemType.Generic, 2320, 5, 6);
    private static readonly SessionItemKind Cells = new(SessionItemType.Ammo, 2320, 7, 8);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionInsertCheck Full = _ => SessionInsertVerdict.NoRoom;
    private static readonly SessionCraftLookup InBag = _ => new SessionCraftFacts(10);
    private static readonly CellCoord SmithCell = new(104, 201);

    private static ComponentFacts? Facts(int prototype) => prototype == 2120 ? new(0, 3) : null;
    private static SessionItemKind? Kinds(int prototype) => prototype == 2320 ? Product : null;
    private static SessionItemKind? AmmoKinds(int prototype) => prototype == 2320 ? Cells : null;
    private static (int AmmoType, int Units)? CellUnits(int prototype) => prototype == 2320 ? (2, 5) : null;

    private static SchematicRecord Schematic(int quantity) => new()
    {
        Number = 2000, Kind = SchematicKind.Learned, NameText = 1, DescriptionText = 2, Art = 3,
        Component1 = new(Array.AsReadOnly(new[] { 2100 }), 1), Component2 = new(Array.AsReadOnly(new[] { 2100 }), 1),
        Product = new(Array.AsReadOnly(new[] { 2300 }), 1), Quantity = quantity, MissingLines = Array.AsReadOnly(new bool[7]),
    };

    private static GameSessionOwner Hero(GameSessionLimits? limits = null)
    {
        var owner = CreateWith(HalfElf() with { Kit = [new(Part), new(Part)] }, limits);
        var facts = new SessionItemFacts(Part, 5, 0, 10, null, []);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, new(
            [new("smith", SessionHolderKind.Npc, false, false, [], ["s1", "s2"])], [new("s1", facts, null), new("s2", facts, null)])).Disposition);
        return owner;
    }

    private static SessionNpcCrafter Smith() =>
        new(new("smith", new(CharacterRace.Human, false, [8, 8, 8, 8, 8, 8, 8, 8], 0, 0, 1, 0, [.. new int[12]], [.. new int[4]],
            [.. new int[16]], [0, 0, 0, 0, 0, 0, 0, 0], 1, 2, 0, 0), 0), AdvancementModifiers.None);

    private static string Ground(GameSessionOwner owner) =>
        string.Join(" ", owner.QueryMapInventory(Exterior)!.Ground.Select(g => $"{g.Item.Entity}@{g.Cell.X},{g.Cell.Y}"));

    [TestMethod]
    public void A_full_inventory_leaves_the_heros_products_on_its_tile_and_the_assembly_succeeds()
    {
        using var owner = Hero();
        var result = owner.AssembleSchematic(owner.Version, Schematic(3), InBag, Facts, Kinds, Full);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, (SessionInsertVerdict?)SessionInsertVerdict.NoRoom),
            (result.Disposition, result.Failure, result.Verdict), "the refusal's message is the host's to show");
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:2" }, result.Consumed.ToArray(), "the components are spent");
        Assert.IsTrue(owner.IsItemDestroyed("kit:1") && owner.IsItemDestroyed("kit:2"));
        Assert.AreEqual(0, result.Made.Count);
        CollectionAssert.AreEqual(new[] { "made:1", "made:2", "made:3" }, result.OnGround.ToArray());
        Assert.AreEqual(1L, owner.Version.Revision, "one revision");
        Assert.AreEqual("made:1@100,200 made:2@100,200 made:3@100,200", Ground(owner), "on the hero's tile");
        Assert.IsTrue(owner.QueryPlacedItems().All(p => p.Source == SessionPlacedSource.Made && p.Kind == Product && p.Map == Exterior));
        // ARC-WORLD-062: a product created on the tile is not dropped, so it gets no decay event (ledger ID9).
        Assert.AreEqual(0, owner.QueryItemDecays().Count, "refused products do not decay");
        // The hero picks one up into its made items; saved and restored, the rest stay on the ground.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "made:2", Accept).Disposition);
        Assert.AreEqual(0, owner.QueryItemDecays().Count);
        CollectionAssert.AreEqual(new[] { new SessionMadeItem("made:2", Product) }, owner.QueryMadeItems().ToArray());
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        CollectionAssert.AreEqual(owner.QueryPlacedItems().ToArray(), restored.QueryPlacedItems().ToArray());

        // Mixed: the third unit is too heavy, the first two enter the inventory.
        using var mixed = Hero();
        var calls = 0;
        var some = mixed.AssembleSchematic(mixed.Version, Schematic(3), InBag, Facts, Kinds, _ => ++calls < 3 ? SessionInsertVerdict.Accepted : SessionInsertVerdict.TooHeavy);
        Assert.AreEqual((GameSessionDisposition.Changed, (SessionInsertVerdict?)SessionInsertVerdict.TooHeavy), (some.Disposition, some.Verdict));
        CollectionAssert.AreEqual(new[] { "made:1", "made:2" }, some.Made.ToArray());
        CollectionAssert.AreEqual(new[] { "made:3" }, some.OnGround.ToArray());
        Assert.AreEqual(2, mixed.QueryMadeItems().Count);
    }

    [TestMethod]
    public void A_ground_budget_refusal_still_refuses_before_anything_is_spent()
    {
        // 2 − 2 + 3 = 3 items over a bound of 2: the products on the ground count with the hero's items.
        using var owner = Hero(new GameSessionLimits(maxPcItems: 2));
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.AssembleSchematic(owner.Version, Schematic(3), InBag, Facts, Kinds, Full).Failure);
        Assert.AreEqual((0L, 2, 0), (owner.Version.Revision, owner.QueryHeroKit().Count, owner.QueryPlacedItems().Count));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AssembleSchematic(owner.Version, Schematic(2), InBag, Facts, Kinds, Full).Disposition, "2 fit");
    }

    [TestMethod]
    public void An_ammunition_product_merges_when_it_fits_and_lies_on_the_ground_as_its_own_stack_otherwise()
    {
        using var owner = Hero();
        var calls = 0;
        // Unit 1 starts a stack, unit 2 is refused, unit 3 merges into the stack unit 1 started.
        var result = owner.AssembleSchematic(owner.Version, Schematic(3), InBag, Facts, AmmoKinds, _ => ++calls == 2 ? SessionInsertVerdict.NoRoom : SessionInsertVerdict.Accepted,
            CellUnits);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        CollectionAssert.AreEqual(new[] { "made:1" }, result.Made.ToArray());
        CollectionAssert.AreEqual(new[] { "made:2" }, result.OnGround.ToArray());
        CollectionAssert.AreEqual(new[] { new SessionAmmoRecord("made:1", 2, 10) }, owner.QueryHeroAmmo().ToArray());
        Assert.AreEqual(new SessionAmmoRecord("made:2", 2, 5), owner.QueryAmmo("made:2"), "the ground stack keeps its units");
        // Picked up, it merges into the held stack (ARC-WORLD-066).
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "made:2", Accept).Disposition);
        CollectionAssert.AreEqual(new[] { new SessionAmmoRecord("made:1", 2, 15) }, owner.QueryHeroAmmo().ToArray());

        // Every unit refused: three separate stacks on the ground, none in the inventory.
        using var full = Hero();
        var all = full.AssembleSchematic(full.Version, Schematic(3), InBag, Facts, AmmoKinds, Full, CellUnits);
        CollectionAssert.AreEqual(new[] { "made:1", "made:2", "made:3" }, all.OnGround.ToArray());
        Assert.AreEqual(0, full.QueryHeroAmmo().Count);
        Assert.IsTrue(all.OnGround.All(entity => full.QueryAmmo(entity) == new SessionAmmoRecord(entity, 2, 5)));
        Assert.AreEqual(0, full.QueryItemDecays().Count, "refused stacks do not decay either (ARC-WORLD-062, ledger ID9)");
    }

    [TestMethod]
    public void An_npc_crafters_refused_products_lie_on_its_tile()
    {
        using var owner = Hero();
        var result = owner.AssembleSchematicForNpc(owner.Version, Smith(), Schematic(2), InBag, Facts, Kinds, Full, crafterCell: SmithCell);
        Assert.AreEqual((GameSessionDisposition.Changed, (SessionInsertVerdict?)SessionInsertVerdict.NoRoom), (result.Disposition, result.Verdict));
        CollectionAssert.AreEqual(new[] { "s1", "s2" }, result.Consumed.ToArray());
        CollectionAssert.AreEqual(new[] { "made:1", "made:2" }, result.OnGround.ToArray());
        Assert.AreEqual(0, owner.QueryNpcInventory("smith").Count, "the NPC's list gets nothing");
        Assert.AreEqual("made:1@104,201 made:2@104,201", Ground(owner), "on the smith's tile");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "made:1", Accept).Disposition, "the hero can pick it up");

        // Without the crafter's tile, or without a hero (the ground store is the hero's), a refused unit can go nowhere: nothing is spent.
        using var untold = Hero();
        var nowhere = untold.AssembleSchematicForNpc(untold.Version, Smith(), Schematic(1), InBag, Facts, Kinds, Full);
        Assert.AreEqual((GameSessionFailure.InsertRefused, (SessionInsertVerdict?)SessionInsertVerdict.NoRoom, 0L), (nowhere.Failure, nowhere.Verdict, untold.Version.Revision));
        using var heroless = Create();
        var facts = new SessionItemFacts(Part, 5, 0, 10, null, []);
        Assert.AreEqual(GameSessionDisposition.Changed, heroless.AttachMapInventory(Exterior, new(
            [new("smith", SessionHolderKind.Npc, false, false, [], ["s1", "s2"])], [new("s1", facts, null), new("s2", facts, null)])).Disposition);
        Assert.AreEqual(GameSessionFailure.InsertRefused, heroless.AssembleSchematicForNpc(heroless.Version, Smith(), Schematic(1), InBag, Facts, Kinds, Full,
            crafterCell: SmithCell).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, heroless.AssembleSchematicForNpc(heroless.Version, Smith(), Schematic(1), InBag, Facts, Kinds, Accept).Disposition,
            "an accepted product needs no tile");

        // Ammunition for the NPC: an accepted unit starts its stack, a refused one lies on the tile.
        using var ammo = Hero();
        var calls = 0;
        var mixed = ammo.AssembleSchematicForNpc(ammo.Version, Smith(), Schematic(2), InBag, Facts, AmmoKinds,
            _ => ++calls == 1 ? SessionInsertVerdict.Accepted : SessionInsertVerdict.NoRoom, CellUnits, SmithCell);
        CollectionAssert.AreEqual(new[] { "made:1" }, mixed.Made.ToArray());
        CollectionAssert.AreEqual(new[] { "made:2" }, mixed.OnGround.ToArray());
        Assert.AreEqual((new SessionAmmoRecord("made:1", 2, 5), new SessionAmmoRecord("made:2", 2, 5)), (ammo.QueryAmmo("made:1"), ammo.QueryAmmo("made:2")));
    }
}

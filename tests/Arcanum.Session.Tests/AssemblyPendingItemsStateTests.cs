using Arcanum.Data.Technology;
using Arcanum.Simulation.Technology;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-1133: an assembly's room test counts the cells its components free as free, but an item of the hero's list that waits outside the grid (no
// cell: its art grew, a save from before) takes the first free cell at the next placement, in list order, before the products (ARC-DEV-333). So the
// products' room test lets those items take their cells first. Two parts (2120) make a product (2320); sizes and entities are invented.
[TestClass]
public sealed class AssemblyPendingItemsStateTests
{
    private static readonly SessionItemKind Part = new(SessionItemType.Generic, 2120, 1, 2);
    private static readonly SessionItemKind Other = new(SessionItemType.Generic, 2130, 3, 4);
    private static readonly SessionItemKind Product = new(SessionItemType.Generic, 2320, 5, 6);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionCraftLookup InBag = _ => new SessionCraftFacts(10);

    private static ComponentFacts? Facts(int prototype) => prototype == 2120 ? new(0, 3) : null;
    private static SessionItemKind? Kinds(int prototype) => prototype == 2320 ? Product : null;

    private static SchematicRecord Schematic(int quantity) => new()
    {
        Number = 2000, Kind = SchematicKind.Learned, NameText = 1, DescriptionText = 2, Art = 3,
        Component1 = new(Array.AsReadOnly(new[] { 2100 }), 1), Component2 = new(Array.AsReadOnly(new[] { 2100 }), 1),
        Product = new(Array.AsReadOnly(new[] { 2300 }), 1), Quantity = quantity, MissingLines = Array.AsReadOnly(new bool[7]),
    };

    // kit:1 and kit:2 (the parts) take 10 x 6 cells each and fill the 120-cell grid, so kit:3 (2 x 1 cells, so its size changed too) waits outside it. The product's size is the test's.
    private static GameSessionOwner Hero(uint productHeight)
    {
        var owner = CreateWith(HalfElf() with { Gold = 0, Kit = [new(Part), new(Part), new(Other)] });
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, new([], [])).Disposition);
        owner.AttachItemArtSizes((entity, kind) => entity == "kit:3" ? (64u, 32u) : kind.Prototype == 2320 ? (320u, productHeight) : (320u, 192u));
        Assert.AreEqual("kit:1 kit:2", string.Join(" ", owner.QueryHeroGrid().Items.Select(item => item.Entity)));
        CollectionAssert.AreEqual(new[] { "kit:3" }, owner.QueryHeroGrid().Unplaced.ToArray());
        return owner;
    }

    [TestMethod]
    public void A_waiting_item_takes_a_freed_cell_before_the_product_so_a_whole_grid_product_lies_on_the_ground()
    {
        using var owner = Hero(productHeight: 384);   // 10 x 12 cells: the whole grid
        var result = owner.AssembleSchematic(owner.Version, Schematic(1), InBag, Facts, Kinds, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, (SessionInsertVerdict?)SessionInsertVerdict.NoRoom), (result.Disposition, result.Failure, result.Verdict));
        CollectionAssert.AreEqual(new[] { "made:1" }, result.OnGround.ToArray(), "kit:3 takes the first cells first, so the grid has no place for the whole-grid product");
        Assert.AreEqual(0, result.Made.Count);
        Assert.AreEqual("kit:3", owner.QueryHeroGrid().Items.Single().Entity, "the waiting item got its cell");
    }

    [TestMethod]
    public void A_product_that_fits_beside_the_waiting_item_still_enters_the_grid()
    {
        using var owner = Hero(productHeight: 192);   // 10 x 6 cells: kit:3 takes cells 0 and 1, the product the next free rows
        var result = owner.AssembleSchematic(owner.Version, Schematic(1), InBag, Facts, Kinds, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        CollectionAssert.AreEqual(new[] { "made:1" }, result.Made.ToArray());
        Assert.AreEqual(0, result.OnGround.Count);
        Assert.AreEqual("kit:3 made:1", string.Join(" ", owner.QueryHeroGrid().Items.Select(item => item.Entity)));
    }
}

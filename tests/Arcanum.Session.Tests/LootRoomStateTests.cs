using Arcanum.Data.Technology;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Technology;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-DEV-333 (B48): every way an item enters the hero's list from outside runs the insert check's room test (0x466510, code 2, ledger WE4, BG4,
// IE1, IE2): without a free grid place for the item's size the take is refused and the item stays where it was; Take all moves the items that fit
// and leaves the rest; a stack that joins a held one needs no place. The half-elf fixture's kit is three 1 × 1 items; sizes and entities are invented.
[TestClass]
public sealed class LootRoomStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private const int GemCount = 10;
    private static readonly SessionItemKind Cells = new(SessionItemType.Ammo, 7060, 100, 200);
    private static readonly SessionItemKind Bullets = new(SessionItemType.Ammo, 7059, 100, 200);

    private static SessionItemFacts Item(int prototype, int width = 1, int height = 1) =>
        new(new(SessionItemType.Generic, prototype, 10, 20), 0, 0, 0, null, []) { InventoryWidth = width, InventoryHeight = height };

    private static SessionItemFacts Ammo(SessionItemKind kind, int type, int units) => new(kind, 0, 0, 0, units, []) { AmmoType = type };

    private static string Gem(int number) => "gem" + number;

    private static SessionMapInventorySeed Seed()
    {
        var items = new List<SessionSeedItem>
        {
            new("slab", Item(5005, 10, 11), new CellCoord(101, 200)),
            new("cells-a", Ammo(Cells, 2, 10), new CellCoord(102, 200)),
            new("corpse-cells", Ammo(Cells, 2, 5), null),
            new("corpse-bullets", Ammo(Bullets, 1, 7), null),
            new("chest-bullets", Ammo(Bullets, 1, 7), null),
            new("smith-pebble", Item(5050), null),
        };
        for (var number = 1; number <= GemCount; number++) items.Add(new(Gem(number), Item(5100 + number), null));
        return new(
        [
            new("corpse", SessionHolderKind.Npc, true, false, [], [.. Enumerable.Range(1, GemCount).Select(Gem), "corpse-cells", "corpse-bullets"]),
            new("smith", SessionHolderKind.Npc, false, false, [], ["smith-pebble"]),
            new("chest", SessionHolderKind.Container, false, false, [], ["chest-bullets"]),
        ], items);
    }

    // The hero holds the slab (10 × 11 cells below the kit's row), so row 0 keeps seven free cells (slots 3 to 9).
    private static GameSessionOwner Hero()
    {
        var owner = CreateWith(HalfElf() with { Gold = 0 });
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, Seed()).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "slab", Accept).Disposition);
        Assert.AreEqual("kit:1@0 kit:2@1 kit:3@2 slab@10", Layout(owner));
        return owner;
    }

    private static string Layout(GameSessionOwner owner) => string.Join(" ", owner.QueryHeroGrid().Items.Select(item => $"{item.Entity}@{item.Slot}"));

    private static string[] Left(GameSessionOwner owner, string holder) =>
        owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == holder).Items.Select(item => item.Entity).ToArray();

    [TestMethod]
    public void Take_all_moves_the_items_that_fit_and_leaves_the_rest_in_the_body()
    {
        using var owner = Hero();
        var results = Enumerable.Range(1, GemCount).Select(number => owner.LootItem(owner.Version, "corpse", Gem(number), Accept)).ToArray();
        CollectionAssert.AreEqual(Enumerable.Repeat(GameSessionDisposition.Changed, 7).Concat(Enumerable.Repeat(GameSessionDisposition.Rejected, 3)).ToArray(),
            results.Select(result => result.Disposition).ToArray(), "seven free cells take seven gems");
        Assert.IsTrue(results.Skip(7).All(result => result.Failure == GameSessionFailure.InsertRefused && result.Verdict == SessionInsertVerdict.NoRoom), "the refusal is the insert check's room");
        CollectionAssert.AreEqual(new[] { "gem8", "gem9", "gem10", "corpse-cells", "corpse-bullets" }, Left(owner, "corpse"), "what did not fit stays in the body, in order");
        Assert.AreEqual(0, owner.QueryHeroGrid().Unplaced.Count, "nothing is held without a place");
        Assert.AreEqual(11, owner.QueryHeroGrid().Items.Count, "three kit items, the slab and seven gems");
        Assert.AreEqual(7, owner.QueryPcInventory().Count(item => item.Entity.StartsWith("gem", StringComparison.Ordinal)));
        Assert.AreEqual(owner.Version, results[7].Version, "a refusal changes nothing");

        // A place that frees takes the next item at once: the hero lays one down and the refused item goes in.
        owner.PlaceHeroItem("gem1", new CellCoord(103, 200));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LootItem(owner.Version, "corpse", "gem8", Accept).Disposition);
        Assert.AreEqual(SessionInsertVerdict.NoRoom, owner.LootItem(owner.Version, "corpse", "gem9", Accept).Verdict);
    }

    [TestMethod]
    public void A_ground_pickup_a_theft_and_a_container_take_need_room_too()
    {
        using var owner = Hero();
        foreach (var number in Enumerable.Range(1, 7)) Assert.AreEqual(GameSessionDisposition.Changed, owner.LootItem(owner.Version, "corpse", Gem(number), Accept).Disposition);
        // The grid is full: the pickup of an ammunition stack of a new prototype, the steal of an item and a container's take are all refused.
        var pickup = owner.PickUpItem(owner.Version, "cells-a", Accept);
        Assert.AreEqual((GameSessionFailure.InsertRefused, (SessionInsertVerdict?)SessionInsertVerdict.NoRoom), (pickup.Failure, pickup.Verdict), "a ground pickup");
        Assert.AreEqual(SessionInsertVerdict.NoRoom, owner.TakeItemFromNpc(owner.Version, "smith", "smith-pebble", Accept).Verdict, "a steal or a purchase from an NPC");
        Assert.AreEqual(SessionInsertVerdict.NoRoom, owner.LootItem(owner.Version, "chest", "chest-bullets", Accept).Verdict, "a container's item");
        CollectionAssert.AreEqual(new[] { "smith-pebble" }, Left(owner, "smith"));
        Assert.IsTrue(owner.QueryMapInventory(Exterior)!.Ground.Any(ground => ground.Item.Entity == "cells-a"), "the pickup leaves the stack on the ground");
        Assert.AreEqual(0, owner.QueryHeroGrid().Unplaced.Count);
    }

    [TestMethod]
    public void A_dialog_give_with_no_room_lays_the_item_on_the_hero_tile()
    {
        // ARC-WORLD-1131: the dialog's IN runs the room test; an item the grid has no place for is not refused (that would fail the conversation) but laid on the
        // PC's tile (0x415BA0), so nothing waits outside the grid and the NPC no longer holds it.
        using var owner = Hero();
        foreach (var number in Enumerable.Range(1, 7)) Assert.AreEqual(GameSessionDisposition.Changed, owner.LootItem(owner.Version, "corpse", Gem(number), Accept).Disposition);
        var before = owner.Version.Revision;
        var given = owner.GiveItemToPc(owner.Version, "corpse", 10, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, (SessionInsertVerdict?)SessionInsertVerdict.NoRoom), (given.Disposition, given.Verdict));
        Assert.AreEqual(before + 1, owner.Version.Revision);
        Assert.AreEqual(0, owner.QueryHeroGrid().Unplaced.Count);
        Assert.AreEqual(given.Item, owner.QueryPlacedItems().Single().Entity);
        CollectionAssert.DoesNotContain(Left(owner, "corpse"), given.Item);
    }

    [TestMethod]
    public void A_dialog_give_with_room_still_goes_into_the_grid()
    {
        using var owner = Hero();
        var given = owner.GiveItemToPc(owner.Version, "corpse", 10, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, (SessionInsertVerdict?)SessionInsertVerdict.Accepted), (given.Disposition, given.Verdict));
        Assert.AreEqual(0, owner.QueryPlacedItems().Count);
        Assert.AreEqual(0, owner.QueryHeroGrid().Unplaced.Count);
        CollectionAssert.Contains(owner.QueryPcInventory().Select(item => item.Entity).ToArray(), given.Item);
    }

    [TestMethod]
    public void A_stack_that_joins_a_held_one_needs_no_room_and_a_new_stack_does()
    {
        using var owner = Hero();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "cells-a", Accept).Disposition);
        foreach (var number in Enumerable.Range(1, 6)) Assert.AreEqual(GameSessionDisposition.Changed, owner.LootItem(owner.Version, "corpse", Gem(number), Accept).Disposition);
        Assert.AreEqual(SessionInsertVerdict.NoRoom, owner.LootItem(owner.Version, "corpse", Gem(7), Accept).Verdict, "the grid is full");

        var merged = owner.LootItem(owner.Version, "corpse", "corpse-cells", Accept);
        Assert.AreEqual(GameSessionDisposition.Changed, merged.Disposition, "the same prototype joins the held stack");
        Assert.AreEqual(15, owner.QueryAmmo("cells-a")!.Value.Units);
        Assert.AreEqual(SessionInsertVerdict.NoRoom, owner.LootItem(owner.Version, "corpse", "corpse-bullets", Accept).Verdict, "another prototype needs a place");
        CollectionAssert.AreEqual(new[] { Gem(7), "gem8", "gem9", "gem10", "corpse-bullets" }, Left(owner, "corpse"));

        // Units of a stack the hero does not hold become a new stack, which needs a place as well.
        var split = owner.TakeAmmoFromNpc(owner.Version, "corpse", "corpse-bullets", 3, Bullets, Accept);
        Assert.AreEqual((GameSessionFailure.InsertRefused, (SessionInsertVerdict?)SessionInsertVerdict.NoRoom), (split.Failure, split.Verdict));
        Assert.AreEqual(7, owner.QueryAmmo("corpse-bullets")!.Value.Units, "the stack keeps its units");
    }

    [TestMethod]
    public void Assembly_products_without_a_place_lie_on_the_tile()
    {
        // Two parts make three products of 10 × 11 cells (art frame 320 × 352): the components' cells are free by then, the first product takes
        // rows 0 to 10 and the other two find no place and lie on the tile (SA3, SA5).
        var part = new SessionItemKind(SessionItemType.Generic, 2120, 1, 2);
        var product = new SessionItemKind(SessionItemType.Generic, 2320, 5, 6);
        using var owner = CreateWith(HalfElf() with { Kit = [new(part), new(part)], Gold = 0 });
        owner.AttachItemArtSizes((_, kind) => kind == product ? (320u, 352u) : null);
        var schematic = new SchematicRecord
        {
            Number = 2000, Kind = SchematicKind.Learned, NameText = 1, DescriptionText = 2, Art = 3,
            Component1 = new(Array.AsReadOnly(new[] { 2100 }), 1), Component2 = new(Array.AsReadOnly(new[] { 2100 }), 1),
            Product = new(Array.AsReadOnly(new[] { 2300 }), 1), Quantity = 3, MissingLines = Array.AsReadOnly(new bool[7]),
        };
        var result = owner.AssembleSchematic(owner.Version, schematic, _ => new SessionCraftFacts(10), prototype => prototype == 2120 ? new ComponentFacts(0, 3) : null,
            prototype => prototype == 2320 ? product : null, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, (SessionInsertVerdict?)SessionInsertVerdict.NoRoom), (result.Disposition, result.Failure, result.Verdict));
        CollectionAssert.AreEqual(new[] { "made:1" }, result.Made.ToArray(), "the first product found its place");
        CollectionAssert.AreEqual(new[] { "made:2", "made:3" }, result.OnGround.ToArray(), "the others lie on the hero's tile");
        Assert.AreEqual("made:1@0", Layout(owner), "one product of 10 × 11 cells");
        Assert.AreEqual(0, owner.QueryHeroGrid().Unplaced.Count);
    }
}

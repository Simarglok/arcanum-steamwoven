using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.EquipmentStateTests;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-UI-019: the hero's inventory grid (10 columns, 12 rows, ledger JP3 and IW4). The half-elf fixture's kit is kit:1 a weapon, kit:2 and
// kit:3 armour; seed entities, prototypes and sizes are invented.
[TestClass]
public sealed class HeroGridStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private static SessionItemFacts Item(int prototype, int width = 1, int height = 1) =>
        new(new(SessionItemType.Generic, prototype, 10, 20), 0, 0, 0, null, []) { InventoryWidth = width, InventoryHeight = height };

    private static SessionMapInventorySeed Seed() => new([],
    [
        new("crate", Item(5001, 2, 3), new CellCoord(101, 200)),
        new("apple", Item(5002), new CellCoord(102, 200)),
        new("plank", Item(5003, 10, 1), new CellCoord(103, 200)),
        new("tower", Item(5004, 10, 12), new CellCoord(104, 200)),
        new("slab", Item(5005, 10, 11), new CellCoord(105, 200)),
        new("bar", Item(5006, 7, 1), new CellCoord(106, 200)),
        new("anvil", Item(5007) with { Weight = 3_990 }, new CellCoord(107, 200)),
        new("pebble", Item(5009), new CellCoord(109, 200)),
        new("torch", Item(5010), new CellCoord(110, 200)),
        new("torch2", Item(5011), new CellCoord(111, 200)),
        new("bar6", Item(5012, 6, 1), new CellCoord(112, 200)),
    ]);

    // ARC-UI-090: the grid tests are about items, so the hero holds no coin (a coin takes a cell of its own, the gold tests below).
    private static GameSessionOwner Hero(RuleMode rules = RuleMode.Steamwoven, int gold = 0)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() with { Gold = gold }, Rules = rules });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        var owner = result.Owner!;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, Seed()).Disposition);
        return owner;
    }

    private static SessionGridMoveItem Facts(GameSessionOwner owner, string entity) =>
        new(owner.QueryHeroKit().Select(item => (item.Entity, item.Kind)).Concat(owner.QueryPcInventory().Select(item => (item.Entity, item.Kind)))
            .First(pair => pair.Entity == entity).Kind, false, []);

    private static string Layout(GameSessionOwner owner) =>
        string.Join(" ", owner.QueryHeroGrid().Items.Select(item => $"{item.Entity}@{item.Slot}:{item.Width}x{item.Height}"));

    [TestMethod]
    public void Items_take_the_first_free_slot_for_their_size_in_list_order_and_leave_it_when_they_go()
    {
        using var owner = Hero();
        Assert.AreEqual("kit:1@0:1x1 kit:2@1:1x1 kit:3@2:1x1", Layout(owner), "the kit in kit order");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "crate", Accept).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "apple", Accept).Disposition);
        Assert.AreEqual("kit:1@0:1x1 kit:2@1:1x1 kit:3@2:1x1 crate@3:2x3 apple@5:1x1", Layout(owner),
            "the crate's 2 × 3 rectangle from slot 3; the apple takes the next free cell, row by row");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "plank", Accept).Disposition);
        Assert.AreEqual("plank@30:10x1", Layout(owner).Split(' ')[^1], "a full row is free only below the crate");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "tower", Accept).Disposition);
        CollectionAssert.AreEqual(new[] { "tower" }, owner.QueryHeroGrid().Unplaced.ToArray(), "no room: it waits outside the grid");

        // A worn item leaves the grid; the hole stays until the next item fills it.
        Ok(owner.Equip(owner.Version, "kit:2", EquipmentSlot.Armor, Wearable(2, new EquipmentItem
        {
            Kind = EquipmentItemKind.Armor, Armor = new EquipmentArmor { Coverage = ArmorCoverage.Torso, Sizes = ArmorSizes.Medium },
        }), Drawn, Accept), owner);
        Assert.IsFalse(owner.QueryHeroGrid().Items.Any(item => item.Entity == "kit:2"));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Unequip(owner.Version, EquipmentSlot.Armor, Accept).Disposition);
        Assert.AreEqual(1, owner.QueryHeroGrid().Items.Single(item => item.Entity == "kit:2").Slot, "taken off into the first free slot");
    }

    [TestMethod]
    public void A_grid_move_refuses_no_drop_items_and_taken_cells_and_keeps_the_size()
    {
        using var owner = Hero();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "crate", Accept).Disposition);
        var revision = owner.Version.Revision;
        var crate = Facts(owner, "crate");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.MoveHeroItem(owner.Version, "crate", 3, crate, Accept).Disposition, "its own slot");
        Assert.AreEqual((GameSessionFailure.WearRefused, EquipmentRefusal.NotDroppable),
            Refusal(owner.MoveHeroItem(owner.Version, "crate", 40, crate with { NoDrop = true }, Accept)), "no-drop refuses every move");
        Assert.AreEqual(GameSessionFailure.GridCellsTaken, owner.MoveHeroItem(owner.Version, "crate", 1, crate, Accept).Failure, "kit:2 and kit:3 lie there");
        Assert.AreEqual(GameSessionFailure.GridCellsTaken, owner.MoveHeroItem(owner.Version, "crate", 9, crate, Accept).Failure, "two columns from column 9 leave the grid");
        Assert.AreEqual(GameSessionFailure.GridCellsTaken, owner.MoveHeroItem(owner.Version, "crate", 100, crate, Accept).Failure, "three rows from row 10 leave the grid");
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.MoveHeroItem(owner.Version, "crate", 120, crate, Accept).Failure);
        Assert.AreEqual(GameSessionFailure.InventoryMismatch, owner.MoveHeroItem(owner.Version, "crate", 40, Facts(owner, "kit:1"), Accept).Failure);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.MoveHeroItem(owner.Version, "apple", 40, crate, Accept).Failure, "not held");
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.MoveHeroItem(new(owner.Identity, 99), "crate", 40, crate, Accept).Failure);
        Assert.AreEqual(revision, owner.Version.Revision, "no refusal moves the revision");

        // Its own cells count as free: one column to the right overlaps where it lay.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.MoveHeroItem(owner.Version, "crate", 4, crate, Accept).Disposition);
        Assert.AreEqual("kit:1@0:1x1 kit:2@1:1x1 kit:3@2:1x1 crate@4:2x3", Layout(owner));
        Assert.AreEqual(revision + 1, owner.Version.Revision);

        // A worn item comes off through Unequip into a chosen slot.
        Ok(owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, Wearable(1, Blade), Drawn, Accept), owner);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.MoveHeroItem(owner.Version, "kit:1", 40, Facts(owner, "kit:1"), Accept).Failure, "worn");
        Assert.AreEqual(GameSessionFailure.GridCellsTaken, owner.Unequip(owner.Version, EquipmentSlot.Weapon, Accept, gridSlot: 14).Failure, "the crate's cell");
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.Unequip(owner.Version, EquipmentSlot.Weapon, Accept, gridSlot: -1).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Unequip(owner.Version, EquipmentSlot.Weapon, Accept, gridSlot: 119).Disposition);
        Assert.AreEqual(119, owner.QueryHeroGrid().Items.Single(item => item.Entity == "kit:1").Slot);
    }

    [TestMethod]
    public void An_occupant_put_back_by_a_wear_move_counts_the_room_the_worn_item_left()
    {
        using var owner = Hero();
        var ring = new EquipmentItem { Kind = EquipmentItemKind.Armor, Armor = new EquipmentArmor { Coverage = ArmorCoverage.Torso, Sizes = ArmorSizes.Medium } };
        Ok(owner.Equip(owner.Version, "kit:2", EquipmentSlot.Armor, Wearable(2, ring), Drawn, Accept), owner);
        Assert.AreEqual("kit:1@0:1x1 kit:3@2:1x1", Layout(owner));
        var swap = owner.Equip(owner.Version, "kit:3", EquipmentSlot.Armor, Wearable(3, ring), Drawn, Accept);
        Assert.AreEqual("kit:2", swap.Displaced);
        Assert.AreEqual("kit:1@0:1x1 kit:2@1:1x1", Layout(owner), "the first free cell with kit:3 already out");
    }

    [TestMethod]
    public void The_grid_round_trips_and_a_restore_refuses_a_grid_that_disagrees_with_the_lists()
    {
        using var owner = Hero();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "crate", Accept).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "tower", Accept).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.MoveHeroItem(owner.Version, "crate", 57, Facts(owner, "crate"), Accept).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"grid\":[{\"entity\":\"kit:1\",\"slot\":0,\"width\":1,\"height\":1},{\"entity\":\"kit:2\",\"slot\":1,\"width\":1,\"height\":1}," +
            "{\"entity\":\"kit:3\",\"slot\":2,\"width\":1,\"height\":1},{\"entity\":\"crate\",\"slot\":57,\"width\":2,\"height\":3}],\"generated\":[],\"npcWorn\":[]},\"objects\":{\"junkPiles\":[],\"bloodPools\":[],\"lastJunkNumber\":0,\"lastPoolNumber\":0,\"state\":[],\"links\":[],\"created\":[],\"lastCreatedNumber\":0}");
        using (var restored = Restore(Decode(text)))
        {
            Assert.AreEqual(Layout(owner), Layout(restored));
            CollectionAssert.AreEqual(new[] { "tower" }, restored.QueryHeroGrid().Unplaced.ToArray());
            Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        }

        GameSessionFailure Restored(string changed) => GameSessionOwner.Restore(Decode(changed), RestoreOptions()).Failure;
        string Crate(string replacement) => text.Replace("{\"entity\":\"crate\",\"slot\":57,\"width\":2,\"height\":3}", replacement, StringComparison.Ordinal);
        Assert.AreEqual(GameSessionFailure.InvalidGrid, Restored(Crate("{\"entity\":\"crate\",\"slot\":58,\"width\":9,\"height\":3}")), "leaves the grid");
        Assert.AreEqual(GameSessionFailure.InvalidGrid, Restored(Crate("{\"entity\":\"apple\",\"slot\":57,\"width\":2,\"height\":3}")), "not held");
        Assert.AreEqual(GameSessionFailure.InvalidGrid, Restored(text.Replace("{\"entity\":\"kit:2\",\"slot\":1,\"width\":1,", "{\"entity\":\"kit:2\",\"slot\":1,\"width\":2,",
            StringComparison.Ordinal)), "kit:2 two cells wide meets kit:3");
        Assert.AreEqual(SnapshotDecodeFailure.NotCanonical, GameSessionSnapshotCodec.TryDecode(Crate("{\"entity\":\"crate\",\"slot\":1,\"width\":2,\"height\":3}")).Failure,
            "slots strictly ascending");
        Assert.AreEqual(SnapshotDecodeFailure.OutOfRange, GameSessionSnapshotCodec.TryDecode(Crate("{\"entity\":\"crate\",\"slot\":120,\"width\":2,\"height\":3}")).Failure);
    }

    [TestMethod]
    public void Wear_moves_need_room_and_a_bearable_load_and_a_ring_moves_between_its_slots()
    {
        var ring = new EquipmentItem { Kind = EquipmentItemKind.Armor, Armor = new EquipmentArmor { Coverage = ArmorCoverage.Ring } };
        foreach (var rules in new[] { RuleMode.Steamwoven, RuleMode.Original })
        {
            using var owner = Hero(rules);
            Ok(owner.Equip(owner.Version, "kit:2", EquipmentSlot.Ring1, Wearable(2, ring), Drawn, Accept), owner);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "slab", Accept).Disposition);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "bar", Accept).Disposition);
            Assert.AreEqual("kit:1@0:1x1 kit:3@2:1x1 bar@3:7x1 slab@10:10x11", Layout(owner), "the worn ring left slot 1");
            Ok(owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, Wearable(1, Blade), Drawn, Accept), owner);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "apple", Accept).Disposition);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "pebble", Accept).Disposition);
            Assert.AreEqual("apple@0:1x1 pebble@1:1x1 kit:3@2:1x1 bar@3:7x1 slab@10:10x11", Layout(owner), "the grid is full now");

            var off = owner.Unequip(owner.Version, EquipmentSlot.Weapon, Accept);
            Assert.AreEqual((GameSessionFailure.InsertRefused, (SessionInsertVerdict?)SessionInsertVerdict.NoRoom), (off.Failure, off.Verdict), "no free slot");
            var moved = owner.Equip(owner.Version, "kit:2", EquipmentSlot.Ring2, Wearable(2, ring), Drawn, Accept);
            if (rules == RuleMode.Original)
                Assert.AreEqual((GameSessionFailure.InsertRefused, (SessionInsertVerdict?)SessionInsertVerdict.NoRoom), (moved.Failure, moved.Verdict), "IWA1");
            else
            {
                Ok(moved, owner);
                Assert.AreEqual(EquipmentSlot.Ring2, owner.QueryEquipment().Worn.Single(item => item.Entity == "kit:2").Slot);
            }
        }

        // The insert check of a grid move: the host's weight answer refuses it and the item stays (the session's own load test, above the
        // carry weight, needs a load the pickups cannot reach).
        using var heavy = Hero();
        Assert.AreEqual(GameSessionDisposition.Changed, heavy.PickUpItem(heavy.Version, "anvil", Accept).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, heavy.PickUpItem(heavy.Version, "crate", Accept).Disposition);
        Assert.IsTrue(heavy.QueryHeroCarry()!.Weight <= 4_000);
        Assert.AreEqual(GameSessionDisposition.Changed, heavy.MoveHeroItem(heavy.Version, "crate", 50, Facts(heavy, "crate"), Accept).Disposition, "a load that fits");
        var refused = heavy.MoveHeroItem(heavy.Version, "crate", 60, Facts(heavy, "crate"), _ => SessionInsertVerdict.TooHeavy);
        Assert.AreEqual((GameSessionFailure.InsertRefused, (SessionInsertVerdict?)SessionInsertVerdict.TooHeavy), (refused.Failure, refused.Verdict), "the host's weight answer");
        Assert.AreEqual(50, heavy.QueryHeroGrid().Items.Single(item => item.Entity == "crate").Slot);

        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, heavy.MoveHeroItem(heavy.Version, "crate", 70,
            Facts(heavy, "crate") with { ScriptPoints = [SessionScriptPoint.InsertItem] }, Accept).Failure, "its insert-item event needs a script host");
    }

    [TestMethod]
    public void An_occupant_takes_the_place_its_check_found_before_an_item_waiting_outside_the_grid()
    {
        using var owner = Hero();
        foreach (var item in new[] { "torch2", "bar6", "slab", "apple", "torch" })
            Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, item, Accept).Disposition, item);
        CollectionAssert.AreEqual(new[] { "apple", "torch" }, owner.QueryHeroGrid().Unplaced.ToArray(), "the grid is full");
        var torch = new EquipmentItem { Kind = EquipmentItemKind.Generic, UsesShieldLocation = true };
        SessionWearableItem Torch(string entity) => new(Facts(owner, entity).Kind, [], false, torch);
        Ok(owner.Equip(owner.Version, "torch", EquipmentSlot.Shield, Torch("torch"), Drawn, Accept), owner);
        var swap = owner.Equip(owner.Version, "torch2", EquipmentSlot.Shield, Torch("torch2"), Drawn, Accept);
        Assert.AreEqual("torch", swap.Displaced);
        Assert.AreEqual(3, owner.QueryHeroGrid().Items.Single(item => item.Entity == "torch").Slot, "the cell torch2 left, not the waiting apple");
        CollectionAssert.AreEqual(new[] { "apple" }, owner.QueryHeroGrid().Unplaced.ToArray());
        var off = owner.Unequip(owner.Version, EquipmentSlot.Shield, Accept);
        Assert.AreEqual((GameSessionFailure.InsertRefused, (SessionInsertVerdict?)SessionInsertVerdict.NoRoom), (off.Failure, off.Verdict), "the grid is full again");
    }

    [TestMethod]
    public void Attached_art_sizes_lay_out_the_grid_again_and_size_new_items()
    {
        // ARC-UI-055: invented frames in pixels; 0x461780 makes each side (pixels − 1) / 32 + 1 cells.
        var frames = new Dictionary<string, (uint, uint)?>
        {
            ["kit:1"] = (32, 128), ["kit:2"] = (64, 96), ["kit:3"] = null, ["apple"] = (33, 32), ["pebble"] = (0, 32),
        };
        var asked = new List<(string, SessionItemKind)>();
        (uint, uint)? Frame(string entity, SessionItemKind kind) { asked.Add((entity, kind)); return frames.GetValueOrDefault(entity); }
        using var owner = Hero();
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        var revision = owner.Version.Revision;
        owner.AttachItemArtSizes(Frame);
        Assert.AreEqual(revision + 1, owner.Version.Revision, "one revision for the new layout");
        Assert.AreEqual("kit:1@0:1x4 kit:3@2:1x1 kit:2@3:2x3", Layout(owner),
            "kit:3 (no frame) keeps its place; kit:1 and kit:2 take the first free place for their size in list order");
        CollectionAssert.Contains(asked, ("kit:2", owner.QueryHeroKit().Single(item => item.Entity == "kit:2").Kind), "the session asks with the item's kind");
        owner.AttachItemArtSizes(Frame);
        Assert.AreEqual(revision + 1, owner.Version.Revision, "nothing to lay out again: no revision");

        Assert.AreEqual((2, 1), owner.QueryItemCells("apple"), "a seed item on the ground: the art's size");
        Assert.AreEqual((2, 3), owner.QueryItemCells("crate"), "no frame: the seed's size");
        Assert.AreEqual((1, 1), owner.QueryItemCells("nothing"), "an unknown item");
        Assert.ThrowsExactly<ArgumentException>(() => owner.QueryItemCells(""));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "apple", Accept).Disposition);
        Assert.AreEqual("apple@5:2x1", Layout(owner).Split(' ')[^1], "the art's 2 × 1 wins over the seed's 1 × 1");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "pebble", Accept).Disposition);
        CollectionAssert.AreEqual(new[] { "pebble" }, owner.QueryHeroGrid().Unplaced.ToArray(), "a 0-pixel side is 2^27 cells and never fits");

        // Detached, the kept sizes stay; a saved 1 × 1 grid (a save from before the art was read) is laid out again on attach.
        owner.AttachItemArtSizes(null);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "crate", Accept).Disposition);
        StringAssert.Contains(Layout(owner), "kit:1@0:1x4", "detaching moves nothing");
        StringAssert.Contains(Layout(owner), "crate@", "the seed's size without an art source");
        using var restored = Restore(Decode(text));
        Assert.AreEqual("kit:1@0:1x1 kit:2@1:1x1 kit:3@2:1x1", Layout(restored));
        restored.AttachItemArtSizes(Frame);
        Assert.AreEqual("kit:1@0:1x4 kit:3@2:1x1 kit:2@3:2x3", Layout(restored));
    }

    private static (GameSessionFailure, EquipmentRefusal) Refusal(EquipmentMutationResult result) => (result.Failure, result.Refusal);

    private static void Ok(EquipmentMutationResult result, GameSessionOwner owner)
    {
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        Assert.AreEqual(owner.Version, result.Version);
    }
}

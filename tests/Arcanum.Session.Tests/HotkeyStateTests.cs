using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Hotkeys;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.EquipmentStateTests;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-UI-022: the hotkey bar's entries and recent actions (ledger HK rows, IP6–IP9). The half-elf fixture's kit is kit:1 a weapon, kit:2 and
// kit:3 armour; seed entities and prototypes are invented: two potions share a prototype.
[TestClass]
public sealed class HotkeyStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private static SessionItemFacts Item(SessionItemType type, int prototype) => new(new(type, prototype, 10, 20), 0, 0, 0, null, []);

    private static GameSessionOwner Hero()
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        var owner = result.Owner!;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, new([],
        [
            new("potion", Item(SessionItemType.Food, 6001), new CellCoord(101, 200)),
            new("potion2", Item(SessionItemType.Food, 6001), new CellCoord(102, 200)),
            new("rock", Item(SessionItemType.Generic, 6002), new CellCoord(103, 200)),
            new("arrows", Item(SessionItemType.Ammo, 6003) with { AmmoType = 0, Quantity = 20 }, new CellCoord(104, 200)),
        ])).Disposition);
        foreach (var item in new[] { "potion", "potion2", "rock", "arrows" })
            Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, item, Accept).Disposition);
        return owner;
    }

    private static void Changed(GameSessionMutationResult result, GameSessionOwner owner)
    {
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        Assert.AreEqual(owner.Version, result.Version);
    }

    private static bool InGrid(GameSessionOwner owner, string entity) => owner.QueryHeroGrid().Items.Any(item => item.Entity == entity);

    [TestMethod]
    public void An_item_entry_takes_its_item_off_the_grid_until_the_entry_lets_it_go()
    {
        using var owner = Hero();
        Assert.IsTrue(InGrid(owner, "rock"));
        var revision = owner.Version.Revision;
        Changed(owner.AssignHotkey(owner.Version, 0, HotkeyEntry.ForItem("rock")), owner);
        Assert.AreEqual(revision + 1, owner.Version.Revision, "one revision");
        Assert.AreEqual(HotkeyEntry.ForItem("rock"), owner.QueryHotkeys().Entries[0]);
        Assert.IsFalse(InGrid(owner, "rock"), "location 2000 + entry, not a grid cell");
        Assert.IsFalse(owner.QueryHeroGrid().Unplaced.Contains("rock"), "not waiting for room either");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.AssignHotkey(owner.Version, 0, HotkeyEntry.ForItem("rock")).Disposition);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.AssignHotkey(owner.Version, 1, HotkeyEntry.ForItem("rock")).Failure, "one entry per item");

        // A drag to another entry swaps; the item stays off the grid.
        Changed(owner.AssignHotkey(owner.Version, 4, HotkeyEntry.ForSpell(62)), owner);
        Changed(owner.MoveHotkey(owner.Version, 0, 4), owner);
        Assert.AreEqual(HotkeyEntry.ForSpell(62), owner.QueryHotkeys().Entries[0]);
        Assert.AreEqual(HotkeyEntry.ForItem("rock"), owner.QueryHotkeys().Entries[4]);
        Assert.IsFalse(InGrid(owner, "rock"));

        // Overwritten by another drop, the item goes back to the first free slot.
        Changed(owner.AssignHotkey(owner.Version, 4, HotkeyEntry.ForSkill(SkillBarButton.Prowling)), owner);
        Assert.IsTrue(InGrid(owner, "rock"));
        Changed(owner.AssignHotkey(owner.Version, 4, HotkeyEntry.ForItem("rock")), owner);
        Changed(owner.ClearHotkey(owner.Version, 4), owner);
        Assert.IsTrue(InGrid(owner, "rock"), "dragged off the bar: back in the grid");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.ClearHotkey(owner.Version, 4).Disposition);
    }

    [TestMethod]
    public void Entries_naming_an_item_that_left_the_hero_or_moved_empty()
    {
        using var owner = Hero();
        Changed(owner.AssignHotkey(owner.Version, 0, HotkeyEntry.ForItem("potion")), owner);
        Changed(owner.AssignHotkey(owner.Version, 1, HotkeyEntry.ForItemSpell("potion", 30)), owner);
        Changed(owner.AssignHotkey(owner.Version, 2, HotkeyEntry.ForItem("kit:1")), owner);
        Changed(owner.AssignHotkey(owner.Version, 3, HotkeyEntry.ForItemSpell("kit:1", 62)), owner);
        Changed(owner.AssignHotkey(owner.Version, 4, HotkeyEntry.ForItemSpell("rock", 63)), owner);
        Assert.AreEqual("potion2", owner.QueryHotkeySameKind("potion"), "the first other item of the prototype (0x462930)");
        Assert.IsNull(owner.QueryHotkeySameKind("rock"));
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.AssignHotkey(owner.Version, 5, HotkeyEntry.ForItem("potion2")).Failure,
            "another item entry holds the prototype (message 4000)");

        Changed(owner.DestroyUsedItem(owner.Version, "potion"), owner);
        Assert.IsTrue(owner.QueryHotkeys().Entries[0].IsEmpty && owner.QueryHotkeys().Entries[1].IsEmpty, "the removal clears both (IP6)");

        // A wear move runs the removal callback: both entries naming the weapon empty (HK6); one made for the worn weapon stays until it moves.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, Wearable(1, Blade), Drawn, Accept).Disposition);
        Assert.IsTrue(owner.QueryHotkeys().Entries[2].IsEmpty && owner.QueryHotkeys().Entries[3].IsEmpty, "the weapon moved");
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.AssignHotkey(owner.Version, 2, HotkeyEntry.ForItem("kit:1")).Failure, "a worn item");
        Changed(owner.AssignHotkey(owner.Version, 3, HotkeyEntry.ForItemSpell("kit:1", 62)), owner);
        Changed(owner.RecordHotkeyAction(owner.Version, HotkeyEntry.ForSpell(1)), owner);
        Assert.AreEqual(HotkeyEntry.ForItemSpell("kit:1", 62), owner.QueryHotkeys().Entries[3], "an unrelated change keeps it");
        Assert.AreEqual(HotkeyEntry.ForItemSpell("rock", 63), owner.QueryHotkeys().Entries[4], "a grid item that did not move");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Unequip(owner.Version, EquipmentSlot.Weapon, Accept).Disposition);
        Assert.IsTrue(owner.QueryHotkeys().Entries[3].IsEmpty, "taken off: moved");
        var rock = owner.QueryHeroGrid().Items.Single(item => item.Entity == "rock");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.MoveHeroItem(owner.Version, "rock", 119, new(new(SessionItemType.Generic, 6002, 10, 20), false, []), Accept).Disposition);
        Assert.IsTrue(owner.QueryHotkeys().Entries[4].IsEmpty, $"a grid move from {rock.Slot} is a move too");
    }

    [TestMethod]
    public void An_item_moving_onto_along_or_off_the_bar_empties_the_other_entries_naming_it()
    {
        using var owner = Hero();
        Changed(owner.AssignHotkey(owner.Version, 3, HotkeyEntry.ForItemSpell("rock", 63)), owner);
        Changed(owner.AssignHotkey(owner.Version, 0, HotkeyEntry.ForItem("rock")), owner);
        Assert.AreEqual(HotkeyEntry.ForItem("rock"), owner.QueryHotkeys().Entries[0]);
        Assert.IsTrue(owner.QueryHotkeys().Entries[3].IsEmpty, "onto the bar: a move (HK6)");

        Changed(owner.AssignHotkey(owner.Version, 3, HotkeyEntry.ForItemSpell("rock", 63)), owner);
        Changed(owner.AssignHotkey(owner.Version, 5, HotkeyEntry.ForSpell(7)), owner);
        Changed(owner.MoveHotkey(owner.Version, 0, 5), owner);
        Assert.AreEqual(HotkeyEntry.ForItem("rock"), owner.QueryHotkeys().Entries[5], "the item entry keeps it");
        Assert.IsTrue(owner.QueryHotkeys().Entries[3].IsEmpty, "along the bar: a move");

        Changed(owner.AssignHotkey(owner.Version, 3, HotkeyEntry.ForItemSpell("rock", 63)), owner);
        Changed(owner.MoveHotkey(owner.Version, 3, 4), owner);
        Assert.AreEqual(HotkeyEntry.ForItemSpell("rock", 63), owner.QueryHotkeys().Entries[4], "an item-and-spell entry moved alone moves no item");
        Changed(owner.ClearHotkey(owner.Version, 5), owner);
        Assert.IsTrue(owner.QueryHotkeys().Entries[4].IsEmpty, "off the bar into the grid: a move");
        Assert.IsTrue(InGrid(owner, "rock"));
    }

    [TestMethod]
    public void The_recent_actions_keep_the_latest_two_presses()
    {
        using var owner = Hero();
        Changed(owner.RecordHotkeyAction(owner.Version, HotkeyEntry.ForSpell(62)), owner);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RecordHotkeyAction(owner.Version, HotkeyEntry.ForSpell(62)).Disposition, "the same as the latest");
        Changed(owner.RecordHotkeyAction(owner.Version, HotkeyEntry.ForSkill(SkillBarButton.Prowling)), owner);
        CollectionAssert.AreEqual(new[] { HotkeyEntry.ForSkill(SkillBarButton.Prowling), HotkeyEntry.ForSpell(62) }, owner.QueryHotkeys().Recent.ToArray());
        Changed(owner.RecordHotkeyAction(owner.Version, HotkeyEntry.ForSpell(62)), owner);
        CollectionAssert.AreEqual(new[] { HotkeyEntry.ForSpell(62), HotkeyEntry.ForSkill(SkillBarButton.Prowling) }, owner.QueryHotkeys().Recent.ToArray(),
            "the earlier action again becomes the latest");
        Assert.AreEqual(GameSessionFailure.InvalidHotkey, owner.RecordHotkeyAction(owner.Version, HotkeyEntry.ForItem("rock")).Failure, "the bars write spells and skills");
    }

    [TestMethod]
    public void Refusals_change_nothing()
    {
        using var owner = Hero();
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionFailure.InvalidHotkey, owner.AssignHotkey(owner.Version, 10, HotkeyEntry.ForSpell(1)).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidHotkey, owner.AssignHotkey(owner.Version, -1, HotkeyEntry.ForSpell(1)).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidHotkey, owner.AssignHotkey(owner.Version, 0, HotkeyEntry.Empty).Failure, "a clear is ClearHotkey");
        Assert.AreEqual(GameSessionFailure.InvalidHotkey, owner.AssignHotkey(owner.Version, 0, new(HotkeyKind.Skill, null, 4)).Failure, "four skill-bar buttons");
        Assert.AreEqual(GameSessionFailure.InvalidHotkey, owner.AssignHotkey(owner.Version, 0, HotkeyEntry.ForSpell(10000)).Failure, "the empty spell field");
        Assert.AreEqual(GameSessionFailure.InvalidHotkey, owner.AssignHotkey(owner.Version, 0, new(HotkeyKind.Spell, "rock", 1)).Failure, "a spell names no item");
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.AssignHotkey(owner.Version, 0, HotkeyEntry.ForItem("ghost")).Failure, "not held");
        Assert.AreEqual(GameSessionFailure.InvalidHotkey, owner.AssignHotkey(owner.Version, 0, HotkeyEntry.ForItem("arrows")).Failure, "ammunition");
        Assert.AreEqual(GameSessionFailure.InvalidHotkey, owner.MoveHotkey(owner.Version, 0, 1).Failure, "an empty source");
        var stale = owner.Version;
        Changed(owner.AssignHotkey(owner.Version, 0, HotkeyEntry.ForSpell(1)), owner);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.AssignHotkey(stale, 1, HotkeyEntry.ForSpell(2)).Failure);
        Assert.AreEqual(revision + 1, owner.Version.Revision);

        var none = GameSessionOwner.Create(Descriptor()).Owner!;
        using (none)
        {
            Assert.AreEqual(GameSessionFailure.HotkeysUnavailable, none.AssignHotkey(none.Version, 0, HotkeyEntry.ForSpell(1)).Failure);
            Assert.AreEqual(GameSessionFailure.HotkeysUnavailable, none.RecordHotkeyAction(none.Version, HotkeyEntry.ForSpell(1)).Failure);
        }
    }

    [TestMethod]
    public void The_bar_round_trips_as_saved_and_a_restore_refuses_a_bar_that_disagrees_with_the_lists()
    {
        using var owner = Hero();
        Changed(owner.AssignHotkey(owner.Version, 0, HotkeyEntry.ForItem("rock")), owner);
        Changed(owner.AssignHotkey(owner.Version, 9, HotkeyEntry.ForItemSpell("kit:1", 62)), owner);
        Changed(owner.AssignHotkey(owner.Version, 5, HotkeyEntry.ForSkill(SkillBarButton.Repair)), owner);
        Changed(owner.RecordHotkeyAction(owner.Version, HotkeyEntry.ForSpell(62)), owner);
        Changed(owner.RecordHotkeyAction(owner.Version, HotkeyEntry.ForSkill(SkillBarButton.Prowling)), owner);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"hotkeys\":{\"entries\":[{\"kind\":1,\"item\":\"rock\",\"index\":-1}," + EmptyHotkey);
        StringAssert.Contains(text, "{\"kind\":4,\"item\":\"kit:1\",\"index\":62}],\"recent\":[{\"kind\":2,\"item\":null,\"index\":0},{\"kind\":3,\"item\":null,\"index\":62}]}");
        using (var restored = Restore(Decode(text)))
        {
            CollectionAssert.AreEqual(owner.QueryHotkeys().Entries.ToArray(), restored.QueryHotkeys().Entries.ToArray());
            CollectionAssert.AreEqual(owner.QueryHotkeys().Recent.ToArray(), restored.QueryHotkeys().Recent.ToArray(), "in saved order (HK7 fixed)");
            Assert.IsFalse(InGrid(restored, "rock"));
            Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        }

        GameSessionFailure Restored(string changed) => GameSessionOwner.Restore(Decode(changed), RestoreOptions()).Failure;
        Assert.AreEqual(GameSessionFailure.InvalidHotkey, Restored(text.Replace("\"item\":\"rock\"", "\"item\":\"ghost\"")), "not held");
        Assert.AreEqual(GameSessionFailure.InvalidHotkey, Restored(text.Replace("{\"kind\":2,\"item\":null,\"index\":3}", "{\"kind\":1,\"item\":\"rock\",\"index\":-1}")),
            "one item on two item entries");
        Assert.AreEqual(GameSessionFailure.InvalidHotkey, Restored(text.Replace("],\"recent\":[{\"kind\":2,", "],\"recent\":[{\"kind\":1,\"item\":\"rock\",\"index\":-1},{\"kind\":2,")
            .Replace(",{\"kind\":3,\"item\":null,\"index\":62}]}", "]}")), "a recent action is a spell or a skill");
        Assert.AreEqual(GameSessionFailure.InvalidHotkey, Restored(text.Replace("\"item\":\"kit:1\"", "\"item\":\"ghost\"")), "an item-and-spell entry's item not held");
        // A saved grid that still places an item entry's item disagrees with the bar.
        Assert.AreEqual(GameSessionFailure.InvalidGrid, Restored(text.Replace("],\"generated\":[]},\"equipment\"", ",{\"entity\":\"rock\",\"slot\":119,\"width\":1,\"height\":1}],\"generated\":[]},\"equipment\"")));
    }
}

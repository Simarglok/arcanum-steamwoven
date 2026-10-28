using Arcanum.Data.Scripts;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.EquipmentStateTests;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-UI-025: the hero's drop of one of its items on its own tile (the inventory window's drop icon, ledger IW6, PI1). The half-elf
// fixture's kit is kit:1 a weapon, kit:2 and kit:3 armour; the apple and the charm lie in the exterior's seed. Values are invented.
[TestClass]
public sealed class HeroDropStateTests
{
    private const int DropScript = 610, RemoveScript = 611;
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private static SessionItemFacts Item(int prototype) => new(new(SessionItemType.Generic, prototype, 10, 20), 0, 0, 0, null, []);

    private static GameSessionOwner Hero()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        var seed = new SessionMapInventorySeed([],
        [
            new("apple", Item(5002), new CellCoord(90, 200)),
            new("charm", Item(5003) with { ScriptPoints = [SessionScriptPoint.Drop, SessionScriptPoint.RemoveItem] }, new CellCoord(91, 200)),
        ]);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, seed).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "apple", Accept).Disposition);
        return owner;
    }

    private static SessionGridMoveItem Facts(GameSessionOwner owner, string entity, bool noDrop = false, int[]? points = null) =>
        new(owner.QueryHeroKit().Select(item => (item.Entity, item.Kind)).Concat(owner.QueryPcInventory().Select(item => (item.Entity, item.Kind)))
            .First(pair => pair.Entity == entity).Kind, noDrop, points ?? []);

    private static string[] Held(GameSessionOwner owner) =>
        owner.QueryHeroKit().Select(item => item.Entity).Concat(owner.QueryPcInventory().Select(item => item.Entity)).ToArray();

    [TestMethod]
    public void A_dropped_item_lies_on_the_heros_tile_leaves_list_and_grid_and_starts_its_decay()
    {
        using var owner = Hero();
        var revision = owner.Version.Revision;
        var dropped = owner.DropHeroItem(owner.Version, "apple", Facts(owner, "apple"));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "apple", revision + 1),
            (dropped.Disposition, dropped.Failure, dropped.Item, owner.Version.Revision), "one revision");
        Assert.AreEqual(owner.Version, dropped.Version);
        var placed = owner.QueryPlacedItems().Single();
        Assert.AreEqual(("apple", SessionPlacedSource.Pc, Exterior, Start.Cell), (placed.Entity, placed.Source, placed.Map, placed.Cell));
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:2", "kit:3" }, Held(owner));
        Assert.IsFalse(owner.QueryHeroGrid().Items.Any(item => item.Entity == "apple"));
        Assert.AreEqual("apple", owner.QueryItemDecays().Single().Entity, "the drop's placement schedules the decay (ID4)");

        // A second item on the same tile makes a junk pile of both (JP1).
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DropHeroItem(owner.Version, "kit:3", Facts(owner, "kit:3")).Disposition);
        CollectionAssert.AreEquivalent(new[] { "apple", "kit:3" }, owner.QueryJunkPiles().Single().Items.Select(item => item.Entity).ToArray());

        // Picking it up again returns the kit item to its place.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LootItem(owner.Version, owner.QueryJunkPiles().Single().Entity, "kit:3", Accept).Disposition);
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:2", "kit:3" }, Held(owner));
    }

    [TestMethod]
    public void A_worn_item_leaves_its_slot()
    {
        using var owner = Hero();
        var armour = new EquipmentItem { Kind = EquipmentItemKind.Armor, Armor = new EquipmentArmor { Coverage = ArmorCoverage.Torso, Sizes = ArmorSizes.Medium } };
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, "kit:2", EquipmentSlot.Armor, Wearable(2, armour), Drawn, Accept).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DropHeroItem(owner.Version, "kit:2", Facts(owner, "kit:2")).Disposition);
        Assert.AreEqual(0, owner.QueryEquipment().Worn.Count);
        Assert.AreEqual("kit:2", owner.QueryPlacedItems().Single().Entity);

    }

    [TestMethod]
    public void Refusals_change_nothing()
    {
        using var owner = Hero();
        var revision = owner.Version.Revision;
        var apple = Facts(owner, "apple");
        Assert.AreEqual(GameSessionFailure.ItemNotDroppable, owner.DropHeroItem(owner.Version, "apple", apple with { NoDrop = true }).Failure, "item flag 0x20");
        Assert.AreEqual(GameSessionFailure.InventoryMismatch, owner.DropHeroItem(owner.Version, "apple", Facts(owner, "kit:1")).Failure);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.DropHeroItem(owner.Version, "charm", apple).Failure, "not held");
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.DropHeroItem(owner.Version, "", apple).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.DropHeroItem(owner.Version, "apple", apple with { ScriptPoints = [99] }).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.DropHeroItem(new(owner.Identity, 99), "apple", apple).Failure);
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported,
            owner.DropHeroItem(owner.Version, "apple", apple with { ScriptPoints = [SessionScriptPoint.Drop] }).Failure, "no host runs the drop script");
        Assert.AreEqual(revision, owner.Version.Revision);
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:2", "kit:3", "apple" }, Held(owner));

        Assert.AreEqual(GameSessionDisposition.Changed, owner.DropHeroItem(owner.Version, "apple", apple).Disposition);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.DropHeroItem(owner.Version, "apple", apple).Failure, "placed already");
    }

    [TestMethod]
    public void The_drop_script_runs_first_and_can_keep_the_item_then_remove_item_runs()
    {
        using var owner = Hero();
        var host = new Host();
        host.Slots[("charm", SessionScriptPoint.Drop)] = DropScript;
        host.Slots[("charm", SessionScriptPoint.RemoveItem)] = RemoveScript;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "charm", Accept, host).Disposition);
        var charm = Facts(owner, "charm", points: [SessionScriptPoint.Drop, SessionScriptPoint.RemoveItem]);

        // Skip default keeps the item; the run's flag write still happens, in one revision.
        host.Programs[DropScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnSkipDefault));
        var revision = owner.Version.Revision;
        var kept = owner.DropHeroItem(owner.Version, "charm", charm, host);
        Assert.AreEqual((GameSessionDisposition.Changed, (string?)null, (int?)SessionScriptPoint.Drop, revision + 1),
            (kept.Disposition, kept.Item, kept.DeclinedBy, owner.Version.Revision));
        Assert.AreEqual(true, ReadFlag(owner, 3));
        CollectionAssert.Contains(Held(owner), "charm");

        // Run default: the drop script, then the removal's remove-item script, which sees the drop script's write.
        Assert.AreEqual(Arcanum.Scripting.GlobalFlags.GlobalFlagWriteDisposition.Applied, WriteFlag(owner, 3, false).Disposition);
        Assert.AreEqual(Arcanum.Scripting.GlobalFlags.GlobalFlagWriteDisposition.Applied, WriteFlag(owner, 7, false).Disposition);
        host.Programs[DropScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 7)), C(ScrCatalogue.Action.ReturnRunDefault));
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.Assign, (Flag, 3), (Flag, 7)), C(ScrCatalogue.Action.ReturnRunDefault));
        var dropped = owner.DropHeroItem(owner.Version, "charm", charm, host);
        Assert.AreEqual((GameSessionDisposition.Changed, "charm"), (dropped.Disposition, dropped.Item), dropped.Failure.ToString());
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.Drop, "charm"), (SessionScriptPoint.RemoveItem, "charm") },
            dropped.Scripts.Select(run => (run.Point, run.Attachee)).ToArray());
        Assert.AreEqual((true, true), (ReadFlag(owner, 7), ReadFlag(owner, 3)));
        Assert.AreEqual("charm", owner.QueryPlacedItems().Single().Entity);

        // A refusal after the drop script (the no-drop flag) drops its writes (session policy).
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "charm", Accept, host).Disposition);
        Assert.AreEqual(Arcanum.Scripting.GlobalFlags.GlobalFlagWriteDisposition.Applied, WriteFlag(owner, 3, false).Disposition);
        host.Programs[DropScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        Assert.AreEqual(GameSessionFailure.ItemNotDroppable, owner.DropHeroItem(owner.Version, "charm", charm with { NoDrop = true }, host).Failure);
        Assert.AreEqual(false, ReadFlag(owner, 3));
    }

    [TestMethod]
    public void A_worn_items_removal_runs_wield_off_then_remove_item_in_one_revision()
    {
        using var owner = Hero();
        var host = new Host();
        host.Slots[("kit:2", SessionScriptPoint.WieldOff)] = DropScript;
        host.Slots[("kit:2", SessionScriptPoint.RemoveItem)] = RemoveScript;
        host.Programs[DropScript] = Program(C(ScrCatalogue.Action.ReturnRunDefault));
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.ReturnRunDefault));
        var armour = new EquipmentItem { Kind = EquipmentItemKind.Armor, Armor = new EquipmentArmor { Coverage = ArmorCoverage.Torso, Sizes = ArmorSizes.Medium } };
        int[] points = [SessionScriptPoint.WieldOff, SessionScriptPoint.RemoveItem];
        Assert.AreEqual(GameSessionDisposition.Changed,
            owner.Equip(owner.Version, "kit:2", EquipmentSlot.Armor, Wearable(2, armour, points), Drawn, Accept, host).Disposition);
        // Without a host the worn record's scripts refuse the drop.
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.DropHeroItem(owner.Version, "kit:2", Facts(owner, "kit:2")).Failure);
        var revision = owner.Version.Revision;
        var dropped = owner.DropHeroItem(owner.Version, "kit:2", Facts(owner, "kit:2"), host);
        Assert.AreEqual((GameSessionDisposition.Changed, revision + 1), (dropped.Disposition, owner.Version.Revision), dropped.Failure.ToString());
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.WieldOff, "kit:2"), (SessionScriptPoint.RemoveItem, "kit:2") },
            dropped.Scripts.Select(run => (run.Point, run.Attachee)).ToArray());
        Assert.AreEqual(0, owner.QueryEquipment().Worn.Count);
    }

    /// <summary>A host with slots per (entity, point); the session reads its own flags.</summary>
    private sealed class Host : ISessionItemScriptHost
    {
        public Dictionary<(string, int), int> Slots { get; } = [];
        public Dictionary<int, ScrProgram> Programs { get; } = [];
        public ScrSlot? Slot(ScrObjectRef attachee, int point) => Slots.TryGetValue((attachee.Code, point), out var script) ? new ScrSlot(script, 0, 0) : null;
        public ScrScriptLookup Script(int number) => Programs.TryGetValue(number, out var program) ? ScrScriptLookup.Of(program) : ScrScriptLookup.Missing;
        public bool GlobalFlag(int index) => throw new InvalidOperationException("the session reads flags itself");
        public int StoryState() => throw new InvalidOperationException("not admitted");
        public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
        public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
        public bool IsTrapObject(ScrObjectRef attachee) => false;
        public ScrObjectRef? Player() => new ScrObjectRef(Exterior.RuntimeMap, SessionEventKinds.PlayerEntity);
        public ScrObjectRef? Leader(ScrObjectRef critter) => null;
        public bool IsJilted(ScrObjectRef critter) => false;
        public bool IsPc(ScrObjectRef critter) => critter.Code == SessionEventKinds.PlayerEntity;
        public SessionPrintedLine PrintedLine(int script, int line) => new(SessionPrintedLineStatus.Shown, $"line {line} of {script}", "test");
    }
}

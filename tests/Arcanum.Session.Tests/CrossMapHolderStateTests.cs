using Arcanum.Data.Scripts;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-DEV-087 (MIG6e): an item moves between an NPC of another attached map (a companion carried here, whose seed is its home map's) and
// the hero's list: the negative IN and the steal take it from the map whose seed lists the NPC, and its origin is that map. Entities,
// numbers and cells are invented.
[TestClass]
public sealed class CrossMapHolderStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private static SessionItemFacts Facts(SessionItemType type, int name, int location = 0) => InventoryStateTests.Facts(type, name, location);

    // The home map of the companion "pal": it holds a locket, a bow (worn) and a second locket.
    private static SessionMapInventorySeed HomeSeed() => new(
        [
            new("pal", SessionHolderKind.Npc, false, false, [], ["locket", "bow", "locket-2"]),
            new("pal-pet", SessionHolderKind.Npc, false, false, [], ["bone"]),
        ],
        [
            new("locket", Facts(SessionItemType.Generic, 5700), null),
            new("bow", Facts(SessionItemType.Armor, 2100, location: 1006), null),
            new("locket-2", Facts(SessionItemType.Generic, 5700), null),
            new("bone", Facts(SessionItemType.Generic, 5701), null),
            new("stone", Facts(SessionItemType.Generic, 5702), new CellCoord(300, 300)),
        ]);

    // The hero stands on the Exterior map, whose seed lists nobody called "pal".
    private static GameSessionOwner Both()
    {
        var owner = InventoryStateTests.Attached();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Cave, HomeSeed()).Disposition);
        return owner;
    }

    private static string[] Pc(GameSessionOwner owner) => owner.QueryPcInventory().Select(item => item.Entity).ToArray();

    private static string[] Listed(GameSessionOwner owner, SessionMapId map, string holder) =>
        owner.QueryMapInventory(map)!.Holders.Single(h => h.Entity == holder).Items.Select(i => i.Entity).ToArray();

    [TestMethod]
    public void NegativeInTakesTheItemFromACompanionOfAnotherMapAndKeepsItsOrigin()
    {
        using var owner = Both();
        var result = owner.GiveItemToPc(owner.Version, "pal", 5700, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "locket"), (result.Disposition, result.Failure, result.Item));
        Assert.AreEqual(1L, owner.Version.Revision);
        // The item is the hero's, taken from the map whose seed lists it, not from the one the hero stands on.
        var taken = owner.QueryPcInventory().Single();
        Assert.AreEqual(("locket", Cave), (taken.Entity, taken.Origin));
        CollectionAssert.AreEqual(new[] { "bow", "locket-2" }, Listed(owner, Cave, "pal"));
        Assert.AreEqual(new SessionObjectEntry("locket", SessionObjectOrigin.MapObject, SessionObjectPlace.HeroPack), owner.QueryObject("locket"));
        // The list's next match goes next; another companion of the map answers for its own list.
        Assert.AreEqual("locket-2", owner.GiveItemToPc(owner.Version, "pal", 5700, Accept).Item);
        Assert.AreEqual("bone", owner.GiveItemToPc(owner.Version, "pal-pet", 5701, Accept).Item);
        CollectionAssert.AreEqual(new[] { "locket", "locket-2", "bone" }, Pc(owner));
        // No such item: nothing changes, as for a companion of the hero's own map.
        var none = owner.GiveItemToPc(owner.Version, "pal", 5700, Accept);
        Assert.AreEqual((GameSessionDisposition.NoChange, GameSessionFailure.None), (none.Disposition, none.Failure));
        Assert.AreEqual(3L, owner.Version.Revision);
    }

    [TestMethod]
    public void AnInsertRefusalLaysTheCompanionsItemOnTheHeroTileOfTheShownMap()
    {
        // ARC-WORLD-1131: the item comes from the home map's list, lies on the map the hero stands on, and keeps its origin.
        using var owner = Both();
        var heavy = owner.GiveItemToPc(owner.Version, "pal", 5700, _ => SessionInsertVerdict.TooHeavy);
        Assert.AreEqual((GameSessionDisposition.Changed, "locket", (SessionInsertVerdict?)SessionInsertVerdict.TooHeavy), (heavy.Disposition, heavy.Item, heavy.Verdict));
        Assert.AreEqual(0, Pc(owner).Length);
        var lies = owner.QueryPlacedItems().Single();
        Assert.AreEqual(("locket", Exterior, Cave), (lies.Entity, lies.Map, lies.Origin));
        CollectionAssert.AreEqual(new[] { "bow", "locket-2" }, Listed(owner, Cave, "pal"));
    }

    [TestMethod]
    public void AWornItemOfTheCompanionGoesToTheHeroAndTheOtherRulesStayTheSame()
    {
        using var owner = Both();
        // ARC-SCRIPT-1987: the transfer tests no worn state, so the companion's worn item is taken off it.
        var worn = owner.GiveItemToPc(owner.Version, "pal", 2100, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, "bow"), (worn.Disposition, worn.Item));
        Assert.AreEqual(1L, owner.Version.Revision);
        CollectionAssert.AreEqual(new[] { "bow" }, Pc(owner));
        CollectionAssert.AreEqual(new[] { "locket", "locket-2" }, Listed(owner, Cave, "pal"));
        // A name no list holds, and an unknown NPC, are what they were.
        Assert.AreEqual(GameSessionFailure.UnknownHolder, owner.GiveItemToPc(owner.Version, "ghost", 5700, Accept).Failure);
    }

    [TestMethod]
    public void WithoutTheHomeMapsSeedTheHolderIsUnknownAsBefore()
    {
        using var owner = InventoryStateTests.Attached();
        var unknown = owner.GiveItemToPc(owner.Version, "pal", 5700, Accept);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.UnknownHolder), (unknown.Disposition, unknown.Failure));
        var steal = owner.TakeItemFromNpc(owner.Version, "pal", "locket", Accept);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.UnknownHolder), (steal.Disposition, steal.Failure));
        // No seed on the hero's map and none that lists the holder: the inventory is unavailable.
        using var bare = Create();
        Assert.AreEqual(GameSessionFailure.InventoryUnavailable, bare.GiveItemToPc(bare.Version, "pal", 5700, Accept).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, bare.AttachMapInventory(Cave, HomeSeed()).Disposition);
        Assert.AreEqual("locket", bare.GiveItemToPc(bare.Version, "pal", 5700, Accept).Item, "a seed of another map alone is enough");
    }

    [TestMethod]
    public void TheStealTakesAnItemByEntityFromTheOtherMapsNpc()
    {
        using var owner = Both();
        var stolen = owner.TakeItemFromNpc(owner.Version, "pal", "locket-2", Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, "locket-2"), (stolen.Disposition, stolen.Item));
        Assert.AreEqual(Cave, owner.QueryPcInventory().Single().Origin);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.TakeItemFromNpc(owner.Version, "pal", "bone", Accept).Failure, "another NPC's item");
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.TakeItemFromNpc(owner.Version, "pal", "locket-2", Accept).Failure, "already taken");
    }

    [TestMethod]
    public void TheItemGoesBackToTheCompanionAndComesAgainUnderTheSameEntity()
    {
        using var owner = Both();
        Assert.AreEqual("locket", owner.GiveItemToPc(owner.Version, "pal", 5700, Accept).Item);
        var given = owner.GiveItemToNpc(owner.Version, "pal", "locket", Accept);
        Assert.AreEqual(GameSessionDisposition.Changed, given.Disposition);
        Assert.AreEqual(0, Pc(owner).Length);
        Assert.AreEqual(new SessionObjectEntry("locket", SessionObjectOrigin.MapObject, SessionObjectPlace.NpcList, Holder: "pal"), owner.QueryObject("locket"));
        Assert.AreEqual("locket", owner.QueryGivenItems().Single().Entity);
        Assert.AreEqual(Cave, owner.QueryGivenItems().Single().Origin);
        // The returned item is in the NPC's list again, so the next negative IN finds the first match in list order.
        Assert.AreEqual("locket-2", owner.GiveItemToPc(owner.Version, "pal", 5700, Accept).Item);
    }

    [TestMethod]
    public void ASeedReplacedDuringTheInsertCheckMakesTheMoveStale()
    {
        using var owner = Both();
        // The home map's seed is the one the item came from: a new one during the host's check is stale, with no effect.
        var stale = owner.GiveItemToPc(owner.Version, "pal", 5700, _ => { owner.AttachMapInventory(Cave, HomeSeed()); return SessionInsertVerdict.Accepted; });
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion), (stale.Disposition, stale.Failure));
        Assert.AreEqual(0, Pc(owner).Length);
        Assert.AreEqual("locket", owner.GiveItemToPc(owner.Version, "pal", 5700, Accept).Item);
    }

    [TestMethod]
    public void ASaveKeepsTheOriginAndTheHomeMapsSeedMustAgreeWithIt()
    {
        using var owner = Both();
        Assert.AreEqual("locket", owner.GiveItemToPc(owner.Version, "pal", 5700, Accept).Item);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        using var restored = Restore(Decode(text));
        Assert.AreEqual(Cave, restored.QueryPcInventory().Single().Origin);
        // The hero's map seed lists no such item, and the home map's seed lists it with its kind: both attach.
        Assert.AreEqual(GameSessionDisposition.Changed, restored.AttachMapInventory(Exterior, InventoryStateTests.Seed()).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, restored.AttachMapInventory(Cave, HomeSeed()).Disposition);
        Assert.AreEqual(new SessionObjectEntry("locket", SessionObjectOrigin.MapObject, SessionObjectPlace.HeroPack), restored.QueryObject("locket"));
        CollectionAssert.AreEqual(new[] { "bow", "locket-2" }, Listed(restored, Cave, "pal"));
        // A home seed that lacks the item disagrees with the save.
        using var other = Restore(Decode(text));
        var seed = HomeSeed();
        var without = seed with { Holders = [seed.Holders[0] with { Items = ["bow", "locket-2"] }, seed.Holders[1]], Items = seed.Items.Where(i => i.Entity != "locket").ToArray() };
        Assert.AreEqual(GameSessionFailure.InventoryMismatch, other.AttachMapInventory(Cave, without).Failure);
    }

    // A host that answers no slot: the shown map's rows are all it knows.
    private sealed class NoSlotHost : ISessionItemScriptHost
    {
        public ScrSlot? Slot(ScrObjectRef attachee, int point) => null;
        public ScrScriptLookup Script(int number) => ScrScriptLookup.Missing;
        public bool GlobalFlag(int index) => throw new InvalidOperationException("not read");
        public int StoryState() => throw new InvalidOperationException("not admitted");
        public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
        public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
        public bool IsTrapObject(ScrObjectRef attachee) => false;
        public ScrObjectRef? Player() => new ScrObjectRef(Exterior.RuntimeMap, SessionEventKinds.PlayerEntity);
        public ScrObjectRef? Leader(ScrObjectRef critter) => null;
        public bool IsJilted(ScrObjectRef critter) => false;
        public bool IsPc(ScrObjectRef critter) => critter.Code == SessionEventKinds.PlayerEntity;
        public SessionPrintedLine PrintedLine(int script, int line) => new(SessionPrintedLineStatus.Shown, string.Empty, "test");
    }

    [TestMethod]
    public void ScriptsOfAnotherMapsItemAreNotSkippedTheMoveIsRefusedAndAHoldersOwnScriptsDoNotMatter()
    {
        var seed = HomeSeed();
        var scripted = seed with
        {
            Holders = [.. seed.Holders, new("keeper", SessionHolderKind.Npc, false, false, [SessionScriptPoint.Transfer], ["band"])],
            Items = [.. seed.Items.Select(i => i.Entity == "locket" ? i with { Facts = i.Facts with { ScriptPoints = [SessionScriptPoint.InsertItem] } } : i),
                new("band", Facts(SessionItemType.Generic, 5703), null)],
        };
        using var owner = InventoryStateTests.Attached();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Cave, scripted).Disposition);
        var host = new NoSlotHost();
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.GiveItemToPc(owner.Version, "pal", 5700, Accept, host).Failure, "the item's insert-item script");
        Assert.AreEqual(0L, owner.Version.Revision);
        // The gate of a move out of a holder is the PC's transfer script (the host answers none here), so a holder's own transfer script does not refuse it.
        Assert.AreEqual("band", owner.GiveItemToPc(owner.Version, "keeper", 5703, Accept, host).Item, "the holder's own transfer script is not run on a take");
        // An item with no script of a holder with none moves through the host as without it.
        Assert.AreEqual("bone", owner.GiveItemToPc(owner.Version, "pal-pet", 5701, Accept, host).Item);
    }
}

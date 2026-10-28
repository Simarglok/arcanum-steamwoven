using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-004 viewer inventory host over an invented map (no original data): targets, taken items, transfers,
/// window lines, re-attach after a load and the dialog port.</summary>
[TestClass]
public sealed class AreaInventoryHostTests
{
    // Invented description numbers and text.
    private static readonly ObjectDescriptionCatalog Catalog = ObjectDescriptionCatalog.Create(
        MesFile.Parse(Encoding.Latin1.GetBytes("{100}{Synthetic Apple}{101}{Synthetic Knife}{102}{Synthetic Body}{103}{Synthetic Charm}")).File!);

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;

    private static AreaObjectRecord Row(int index, int type, int description, long x, long y) =>
        new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, x, y, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally,
            [], [Field(ObjectDisplayName.DescriptionSlot, description), .. type == 16 ? [Field(ObjectDisplayName.CritterUnknownDescriptionSlot, description)]
                : type is >= 5 and <= 14 ? [Field(ObjectDisplayName.ItemFlagsSlot, 0), Field(ObjectDisplayName.ItemComplexitySlot, 0)] : Array.Empty<AreaFieldValue>()],
            true, true, true);

    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);

    private static SessionItemFacts Facts(SessionItemType type, int name, int description, params int[] scripts) =>
        new(new(type, 4000 + name, name, description), 5, 0, 0, null, scripts);

    // 0 apple on the ground, 1 knife and 2 charm (insert-item script) on dead body 3, 4 living NPC with 5, 6 chest with 7.
    private static AreaInventoryMap Map(int runtimeMap = 1, bool withoutCharm = false) => new(runtimeMap,
        new(
            [
                new(Entity(3), SessionHolderKind.Npc, true, false, [], withoutCharm ? [Entity(1)] : [Entity(1), Entity(2)]),
                new(Entity(4), SessionHolderKind.Npc, false, false, [], [Entity(5)]),
                new(Entity(6), SessionHolderKind.Container, false, false, [], [Entity(7)]),
            ],
            [
                new(Entity(0), Facts(SessionItemType.Food, 10, 100), new CellCoord(10, 20)),
                new(Entity(1), Facts(SessionItemType.Weapon, 11, 101), null),
                .. withoutCharm ? Array.Empty<SessionSeedItem>() : [new SessionSeedItem(Entity(2), Facts(SessionItemType.Armor, 12, 103, SessionScriptPoint.InsertItem), null)],
                new(Entity(5), Facts(SessionItemType.Generic, 13, 103), null),
                new(Entity(7), Facts(SessionItemType.Generic, 14, 103), null),
            ]),
        new[] { Row(0, 9, 100, 10, 20), Row(1, 5, 101, 12, 22), Row(2, 7, 103, 12, 22), Row(3, 16, 102, 12, 22), Row(4, 16, 102, 14, 20),
            Row(5, 14, 103, 14, 20), Row(6, 2, 102, 16, 20), Row(7, 14, 103, 16, 20) }
            .Where(row => !withoutCharm || row.Key.SourceIndex != 2).ToDictionary(row => Entity(row.Key.SourceIndex)));

    [TestMethod]
    public void AGateScriptsDeclineIsNotAMove()
    {
        var version = new GameSessionVersion(null, 1);
        var moved = new InventoryMutationResult(GameSessionDisposition.Changed, GameSessionFailure.None, "item", SessionInsertVerdict.Accepted, version);
        // ARC-SCRIPT-041: a get script that wrote a flag and declined changes the session but moves nothing.
        var declined = new InventoryMutationResult(GameSessionDisposition.Changed, GameSessionFailure.None, null, null, version)
            { DeclinedBy = SessionScriptPoint.Get, Messages = [new SessionScriptMessage(600, 2, 0, "Not yet.")] };
        var transfer = new InventoryMutationResult(GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, version) { DeclinedBy = SessionScriptPoint.Transfer };
        Assert.IsTrue(AreaInventoryHost.Moved(moved));
        Assert.IsFalse(AreaInventoryHost.Moved(declined));
        Assert.AreEqual("the item's get script declined it — Not yet.", AreaInventoryHost.NotMoved(declined));
        Assert.AreEqual("the PC's transfer script declined it", AreaInventoryHost.NotMoved(transfer));
        Assert.AreEqual("the PC cannot take it", AreaInventoryHost.NotMoved(new(GameSessionDisposition.Rejected, GameSessionFailure.InsertRefused, null, null, version)));
    }

    [TestMethod]
    public void TargetsAreGroundItemsAndDeadNpcsOnly()
    {
        using var session = ConversationSessionFixture.Create();
        var host = new AreaInventoryHost([Map()]);
        Assert.AreEqual(GameSessionFailure.None, host.Attach(session));
        Assert.AreEqual((AreaInventoryTargetKind.GroundItem, Entity(0)), Pair(host.Target(session, 1, Key(0))));
        Assert.AreEqual((AreaInventoryTargetKind.DeadNpc, Entity(3)), Pair(host.Target(session, 1, Key(3))));
        Assert.IsNull(host.Target(session, 1, Key(4)), "a living NPC is talked to or walked past, never looted");
        Assert.IsNull(host.Target(session, 1, Key(6)), "chest contents are generated on first open and not modelled");
        Assert.IsNull(host.Target(session, 1, Key(1)), "a listed item is not drawn in the world");
        Assert.IsNull(host.Target(session, 1, Key(8)), "an object outside the seed");
        Assert.IsNull(host.Target(session, 79, Key(0)), "a map without a seed");
        Assert.AreEqual(0, host.Taken(session, 1).Count);
    }

    private static (AreaInventoryTargetKind, string)? Pair(AreaInventoryTarget? target) => target is null ? null : (target.Kind, target.Entity);

    [TestMethod]
    public void PickupAndLootMoveItemsToThePcAndTheWindowNamesThem()
    {
        using var session = ConversationSessionFixture.Create();
        var host = new AreaInventoryHost([Map()]);
        host.Attach(session);
        Assert.AreEqual(GameSessionDisposition.Changed, host.PickUp(session, Entity(0)).Disposition);
        CollectionAssert.AreEquivalent(new[] { Key(0) }, host.Taken(session, 1).ToArray(), "the taken apple is no longer drawn");
        Assert.IsNull(host.Target(session, 1, Key(0)));
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, host.PickUp(session, Entity(0)).Failure);

        var view = host.View(session, Catalog, 1, Entity(3));
        Assert.AreEqual("Synthetic Body", view.HolderTitle);
        CollectionAssert.AreEqual(new[] { "Synthetic Knife (weapon, weight 5)", "Synthetic Charm (armor, weight 5)" }, view.Loot.Select(l => l.Text).ToArray());
        Assert.AreEqual(GameSessionDisposition.Changed, host.Loot(session, Entity(3), Entity(1)).Disposition);
        var refused = host.Loot(session, Entity(3), Entity(2));
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, refused.Failure);
        StringAssert.Contains(AreaInventoryHost.Refusal(refused.Failure), "script");
        Assert.AreEqual(GameSessionFailure.HolderNotLootable, host.Loot(session, Entity(4), Entity(5)).Failure);

        view = host.View(session, Catalog, 1, Entity(3));
        CollectionAssert.AreEqual(new[] { "Synthetic Apple (food, weight 5)", "Synthetic Knife (weapon, weight 5)" }, view.Pc.Select(l => l.Text).ToArray(),
            "the PC's list in the order the items entered it");
        CollectionAssert.AreEqual(new[] { Entity(2) }, view.Loot.Select(l => l.Entity).ToArray());
        var plain = host.View(session, null, 1);
        Assert.IsNull(plain.Holder);
        Assert.AreEqual("[food: name unavailable] (food, weight 5)", plain.Pc[0].Text, "without the description tables only the type is named");
    }

    [TestMethod]
    public void ALoadedSessionGetsTheSeedsAgainAndAMismatchedSaveIsRefused()
    {
        using var session = ConversationSessionFixture.Create();
        var host = new AreaInventoryHost([Map()]);
        host.Attach(session);
        host.PickUp(session, Entity(0)); host.Loot(session, Entity(3), Entity(1));
        using var loaded = ConversationSessionFixture.SaveAndLoad(session);
        Assert.IsNull(loaded.QueryMapInventory(new(1)), "seeds are not saved");
        Assert.AreEqual(GameSessionFailure.None, host.Attach(loaded));
        CollectionAssert.AreEquivalent(new[] { Key(0), Key(1) }, host.Taken(loaded, 1).ToArray());
        Assert.IsNull(host.Target(loaded, 1, Key(0)), "the taken apple stays gone after a load");

        using var other = ConversationSessionFixture.SaveAndLoad(session);
        var changed = new AreaInventoryHost([Map(withoutCharm: true)]);
        Assert.AreEqual(GameSessionFailure.None, changed.Attach(other), "the save's items are all still in this seed");
        using var foreign = ConversationSessionFixture.SaveAndLoad(session);
        Assert.AreEqual(GameSessionFailure.InventoryMismatch, new AreaInventoryHost([Map(runtimeMap: 79)]).Attach(foreign),
            "a save whose items came from this map cannot take a seed that lists them on another map");
    }

    [TestMethod]
    public void NegativeInGoesThroughTheDialogPort()
    {
        using var session = ConversationSessionFixture.Create();
        var host = new AreaInventoryHost([Map()]);
        host.Attach(session);
        var port = host.DialogPort(session, npc => npc == AreaConversationState.Npc(0) ? Entity(4) : null);
        var command = new GiveItemCommand(AreaConversationState.Npc(0), AreaConversationState.PcParticipant, 13);
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(command));
        Assert.AreEqual(Entity(5), session.QueryPcInventory().Single().Entity);
    }

    [TestMethod]
    public void MapsNeedARowForEveryEntityAndDistinctRuntimeMaps()
    {
        var map = Map();
        Assert.ThrowsExactly<ArgumentException>(() => new AreaInventoryMap(1, map.Seed, new Dictionary<string, AreaObjectRecord> { [Entity(0)] = Row(0, 9, 100, 10, 20) }));
        var twice = Enumerable.Range(0, 8).ToDictionary(Entity, index => Row(index == 1 ? 0 : index, 9, 100, 10, 20));
        Assert.ThrowsExactly<ArgumentException>(() => new AreaInventoryMap(1, map.Seed, twice), "one row cannot be two entities");
        Assert.ThrowsExactly<ArgumentException>(() => new AreaInventoryHost([map, Map()]));
        Assert.AreEqual("[unknown object]", new AreaInventoryHost([map]).Name("oid:none", Catalog));
    }
}

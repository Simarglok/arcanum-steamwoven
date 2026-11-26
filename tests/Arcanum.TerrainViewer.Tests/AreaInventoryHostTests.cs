using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Arcanum.Session;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Arcanum.World;

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
    private static AreaInventoryMap Map(int runtimeMap = 1, bool withoutCharm = false, int knifeLocation = 0, bool appleOffInData = false) => new(runtimeMap,
        new(
            [
                new(Entity(3), SessionHolderKind.Npc, true, false, [], withoutCharm ? [Entity(1)] : [Entity(1), Entity(2)]),
                new(Entity(4), SessionHolderKind.Npc, false, false, [], [Entity(5)]),
                new(Entity(6), SessionHolderKind.Container, false, false, [], [Entity(7)]),
            ],
            [
                new(Entity(0), Facts(SessionItemType.Food, 10, 100) with { OffInData = appleOffInData }, new CellCoord(10, 20)),
                new(Entity(1), Facts(SessionItemType.Weapon, 11, 101) with { InventoryLocation = knifeLocation }, null),
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
        Assert.AreEqual("the transfer script declined it", AreaInventoryHost.NotMoved(transfer));
        Assert.AreEqual("the PC cannot take it", AreaInventoryHost.NotMoved(new(GameSessionDisposition.Rejected, GameSessionFailure.InsertRefused, null, null, version)));
    }

    [TestMethod]
    public void TheDeadNpcQuestionAgreesWithTheTarget()
    {
        // ARC-RENDER-3362: a frame asks it of every speaker, so it reads the one holder; it answers as Target does.
        using var session = ConversationSessionFixture.Create();
        var host = new AreaInventoryHost([Map()]);
        Assert.IsFalse(host.IsDeadNpc(session, 1, Key(3)), "no seed attached");
        Assert.AreEqual(GameSessionFailure.None, host.Attach(session));
        for (var index = 0; index <= 8; index++)
            Assert.AreEqual(host.Target(session, 1, Key(index))?.Kind == AreaInventoryTargetKind.DeadNpc, host.IsDeadNpc(session, 1, Key(index)), $"{index}");
        Assert.IsTrue(host.IsDeadNpc(session, 1, Key(3)));
        Assert.IsFalse(host.IsDeadNpc(session, 79, Key(3)), "a map without a seed");
        // The sets made from a view the caller made are the sets made without one.
        var current = session.QueryMapInventory(new(1));
        CollectionAssert.AreEquivalent(host.Taken(session, 1).ToArray(), host.Taken(session, 1, current).ToArray());
        CollectionAssert.AreEquivalent(host.Piled(session, 1).ToArray(), host.Piled(1, current).ToArray());
    }

    [TestMethod]
    public void TargetsAreGroundItemsDeadNpcsAndContainers()
    {
        using var session = ConversationSessionFixture.Create();
        var host = new AreaInventoryHost([Map()]);
        Assert.AreEqual(GameSessionFailure.None, host.Attach(session));
        Assert.AreEqual((AreaInventoryTargetKind.GroundItem, Entity(0)), Pair(host.Target(session, 1, Key(0))));
        Assert.AreEqual((AreaInventoryTargetKind.DeadNpc, Entity(3)), Pair(host.Target(session, 1, Key(3))));
        Assert.IsNull(host.Target(session, 1, Key(4)), "a living NPC is talked to or walked past, never looted");
        // ARC-WORLD-101: a standing container opens its loot window (its admission and generation are AreaContainerLoot's).
        Assert.AreEqual((AreaInventoryTargetKind.Container, Entity(6)), Pair(host.Target(session, 1, Key(6))));
        Assert.IsNull(host.Target(session, 1, Key(1)), "a listed item is not drawn in the world");
        Assert.IsNull(host.Target(session, 1, Key(8)), "an object outside the seed");
        Assert.IsNull(host.Target(session, 79, Key(0)), "a map without a seed");
        Assert.AreEqual(0, host.Taken(session, 1).Count);
    }

    private static (AreaInventoryTargetKind, string)? Pair(AreaInventoryTarget? target) => target is null ? null : (target.Kind, target.Entity);

    // ARC-SCRIPT-2386 (IV15, hide mask 3 of the screen pick 0x43D9F0 and the draw): a switched-off item lying on the ground is not picked and its T0 body is hidden.
    [TestMethod]
    public void ASwitchedOffItemOnTheGroundIsNotPickedAndItsBodyIsHidden()
    {
        using var session = ConversationSessionFixture.Create();
        var host = new AreaInventoryHost([Map(knifeLocation: 1004)]);
        host.Attach(session);
        Assert.AreEqual((AreaInventoryTargetKind.GroundItem, Entity(0)), Pair(host.Target(session, 1, Key(0))));
        Assert.IsFalse(host.Taken(session, 1).Contains(Key(0)));
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null, [new(new SessionMapId(1), SessionItemOff.Key(Entity(0)), 1)]).Disposition);
        Assert.IsNull(host.Target(session, 1, Key(0)), "not picked");
        Assert.IsTrue(host.Taken(session, 1).Contains(Key(0)), "its T0 body is not drawn");
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null, [new(new SessionMapId(1), SessionItemOff.Key(Entity(0)), null)]).Disposition);
        Assert.AreEqual((AreaInventoryTargetKind.GroundItem, Entity(0)), Pair(host.Target(session, 1, Key(0))));
    }

    // ARC-SCRIPT-2397 (IV18, hide mask 3): the map data's own off bit of an item lying on the ground hides and unpicks it like a script's toggle; a toggle (record 0) switches it on, as the original's flag flip does.
    [TestMethod]
    public void AnItemTheMapDataStoresOffIsNotPickedOnTheGroundAndItsBodyIsHidden()
    {
        using var session = ConversationSessionFixture.Create();
        var host = new AreaInventoryHost([Map(appleOffInData: true)]);
        host.Attach(session);
        Assert.IsNull(host.Target(session, 1, Key(0)), "not picked");
        Assert.IsTrue(host.Taken(session, 1).Contains(Key(0)), "its T0 body is not drawn");
        Assert.IsTrue(session.QueryItemOff(new SessionMapId(1), Entity(0), dataOff: true));
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null, [new(new SessionMapId(1), SessionItemOff.Key(Entity(0)), 0)]).Disposition);
        Assert.AreEqual((AreaInventoryTargetKind.GroundItem, Entity(0)), Pair(host.Target(session, 1, Key(0))), "a toggle switched it on");
        Assert.IsFalse(host.Taken(session, 1).Contains(Key(0)));
    }

    // ARC-SCRIPT-2386 (IV13, 0x576520): a switched-off item worn by a body is not on its doll either (the slot shows empty); the flag going brings it back.
    [TestMethod]
    public void ASwitchedOffWornItemOfABodyIsNotOnItsDoll()
    {
        using var session = ConversationSessionFixture.Create();
        var host = new AreaInventoryHost([Map(knifeLocation: 1004)]);
        host.Attach(session);
        Assert.AreEqual(1, host.View(session, Catalog, 1, Entity(3)).BodyDoll.Count(slot => slot.Entity is not null));
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null, [new(new SessionMapId(1), SessionItemOff.Key(Entity(1)), 1)]).Disposition);
        Assert.IsTrue(host.View(session, Catalog, 1, Entity(3)).BodyDoll.All(slot => slot.Entity is null));
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null, [new(new SessionMapId(1), SessionItemOff.Key(Entity(1)), null)]).Disposition);
        Assert.AreEqual(1, host.View(session, Catalog, 1, Entity(3)).BodyDoll.Count(slot => slot.Entity is not null));
    }

    [TestMethod]
    public void ABodysWornItemsAreItsDollAndItsGridLeavesThemOutAndTheLootTakesThem()
    {
        // ARC-UI-072 (B30): the knife is worn in the weapon slot (1004) of the dead body; the charm stays in the pack.
        using var session = ConversationSessionFixture.Create();
        var host = new AreaInventoryHost([Map(knifeLocation: 1004)]);
        host.Attach(session);
        var view = host.View(session, Catalog, 1, Entity(3));
        Assert.AreEqual(9, view.BodyDoll.Count);
        var weapon = view.BodyDoll.Single(slot => slot.Slot == Arcanum.Simulation.Equipment.EquipmentSlot.Weapon);
        Assert.AreEqual((Entity(1), "Synthetic Knife"), (weapon.Entity, weapon.Name));
        Assert.AreEqual(SessionItemType.Weapon, weapon.Kind!.Value.Type);
        Assert.AreEqual(1, view.BodyDoll.Count(slot => slot.Entity is not null), "the other eight are empty");
        CollectionAssert.AreEqual(new[] { Entity(2) }, view.LootGrid.Select(item => item.Entity).ToArray(), "the grid shows the pack only (IW28)");
        Assert.AreEqual(2, view.Loot.Count, "the list still holds both, so Take all takes the worn one too");
        // A container and a pile have no doll.
        Assert.AreEqual(0, host.View(session, Catalog, 1, Entity(6)).BodyDoll.Count);
        var taken = host.Loot(session, Entity(3), Entity(1));
        Assert.AreEqual(GameSessionDisposition.Changed, taken.Disposition);
        Assert.IsTrue(host.View(session, Catalog, 1, Entity(3)).BodyDoll.All(slot => slot.Entity is null), "the knife left the slot");
    }

    // ARC-WORLD-3122: an item the hero took and gave to an NPC is in no list of the hero's, yet it stays off the ground: not drawn, not a target, also in the host a later map change builds.
    [TestMethod]
    public void AGroundItemTheHeroGaveToAnNpcStaysHiddenAndUnpickedAcrossAMapReturn()
    {
        using var session = ConversationSessionFixture.Create();
        var host = new AreaInventoryHost([Map()]);
        host.Attach(session);
        Assert.AreEqual(GameSessionDisposition.Changed, host.PickUp(session, Entity(0)).Disposition);
        var given = session.GiveItemToNpc(session.Version, Entity(4), Entity(0), host.InsertCheck);
        Assert.AreEqual(GameSessionDisposition.Changed, given.Disposition, given.Failure.ToString());
        Assert.IsFalse(session.QueryPcInventory().Any(item => item.Entity == Entity(0)), "the hero no longer holds it");
        Assert.IsTrue(host.Taken(session, 1).Contains(Key(0)), "its T0 body is not drawn");
        Assert.IsNull(host.Target(session, 1, Key(0)), "not a pickup target");
        // The map is left and entered again: a new host is built over the same session.
        var again = new AreaInventoryHost([Map()]);
        Assert.AreEqual(GameSessionFailure.None, again.Attach(session));
        Assert.IsTrue(again.Taken(session, 1).Contains(Key(0)), "still hidden after the return");
        Assert.IsNull(again.Target(session, 1, Key(0)), "still not a pickup target after the return");
    }

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
    public void AJunkPileOpensAsAContainerAndItsItemsAreLootedFromIt()
    {
        // ARC-WORLD-063: a save whose junk pile holds the apple lying on its cell (the pile a drop there would have made).
        using var made = ConversationSessionFixture.Create();
        var text = GameSessionSnapshotCodec.Encode(made.Capture()).Replace("\"junkPiles\":[]",
            "\"junkPiles\":[{\"entity\":\"junk:1\",\"map\":1,\"x\":10,\"y\":20,\"items\":[{\"entity\":\"" + Entity(0) + "\",\"slot\":0,\"width\":1,\"height\":1}]}]");
        var snapshot = GameSessionSnapshotCodec.TryDecode(text).Snapshot!;
        using var session = GameSessionOwner.Restore(snapshot, new(snapshot.Identity, snapshot.Clock.Profile, new OwnerEpoch(Guid.NewGuid()))).Owner!;
        var host = new AreaInventoryHost([Map()]);
        Assert.AreEqual(GameSessionFailure.None, host.Attach(session));
        Assert.IsNull(host.Target(session, 1, Key(0)), "the apple lies in the pile, not on the ground");
        var view = host.View(session, Catalog, 1, "junk:1");
        Assert.AreEqual(AreaInventoryHost.JunkPileTitle, view.HolderTitle);
        CollectionAssert.AreEqual(new[] { "Synthetic Apple (food, weight 5)" }, view.Loot.Select(l => l.Text).ToArray());
        var world = new AreaEventWorld(() => null, () => null, lootShowsContainer: () => true);
        Assert.IsTrue(world.LootWindowShowsContainer, "an item's decay waits while the window shows the pile");
        Assert.IsFalse(new AreaEventWorld(() => null, () => null).LootWindowShowsContainer);
        Assert.AreEqual(GameSessionDisposition.Changed, host.Loot(session, "junk:1", Entity(0)).Disposition);
        Assert.AreEqual(0, host.View(session, Catalog, 1, "junk:1").Loot.Count);
        Assert.AreEqual(GameSessionDisposition.Changed, session.CloseJunkPile(session.Version, "junk:1").Disposition);
        Assert.AreEqual(0, session.QueryJunkPiles().Count);
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
    public void AnNpcIsNamedByTheRecognitionTheWindowAsksFor()
    {
        // ARC-DEV-297 (NR2): the window's name is the examine rule for the PC: the NPC's own description once it is remembered, else the not-yet-met one.
        var map = Map();
        var npc = Row(4, 16, 102, 14, 20) with { EffectiveFields = [Field(ObjectDisplayName.DescriptionSlot, 102), Field(ObjectDisplayName.CritterUnknownDescriptionSlot, 100)] };
        var rows = Enumerable.Range(0, 8).ToDictionary(Entity, index => index == 4 ? npc : Row(index, index switch { 0 => 9, 1 => 5, 2 => 7, 3 => 16, 6 => 2, _ => 14 }, 100, 10, 20));
        var host = new AreaInventoryHost([new AreaInventoryMap(1, map.Seed, rows)]);
        Assert.AreEqual("Synthetic Apple", host.Name(Entity(4), Catalog), "no recognition test: never recognized");
        Assert.AreEqual("Synthetic Apple", host.Name(Entity(4), Catalog, _ => CritterRecognition.NotRecognized));
        Assert.AreEqual("Synthetic Body", host.Name(Entity(4), Catalog, row => row.Key == npc.Key ? CritterRecognition.Recognized : CritterRecognition.NotRecognized));
        Assert.AreEqual("Synthetic Apple", host.Name(Entity(0), Catalog, _ => CritterRecognition.Recognized), "recognition reads NPCs only");
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

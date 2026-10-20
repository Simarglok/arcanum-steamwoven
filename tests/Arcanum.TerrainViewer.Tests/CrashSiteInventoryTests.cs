using System.Security.Cryptography;
using System.Text;
using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Data.Inventory;
using Arcanum.Data.Traversal;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-WORLD-014 and ARC-UI-004: the Crash Site T0 inventory (ARC-DATA-012 view, life from the step rules) as the AREA
/// viewer's session seed (<see cref="AreaInventoryMap.Build"/>), Virgil's ring through his original DLG and the viewer's
/// conversation host with its inventory port, loot of the four dead NPCs and every ground pickup through the viewer's
/// inventory host, then save and load. Opt-in: ARC_AREA_CONVERSATION_GAME_ROOT names the user-owned installation
/// (read-only). Structural identifiers only; no original text is asserted or printed.
/// </summary>
[TestClass]
public sealed class CrashSiteInventoryTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    [TestMethod, TestCategory("RealData")]
    public void Original_crash_site_items_reach_the_pc_through_dialog_loot_and_pickup()
    {
        var root = Environment.GetEnvironmentVariable("ARC_AREA_CONVERSATION_GAME_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("ARC_AREA_CONVERSATION_GAME_ROOT is required.");
        string P(string relative) => Path.Combine(root, relative);
        const string Mobile = "modules/Arcanum/maps/qrmvs-420-1zjcnpgn";
        using var data = AreaOriginalValidator.OpenFiles(P("modules/Arcanum.dat"), P("arcanum1.dat"), P("arcanum2.dat"), P(Mobile), P("data/proto"));
        var snapshot = data.LoadStrict();
        var read = AreaInventoryReader.Read(snapshot);
        Assert.AreEqual(AreaInventoryFailure.None, read.Failure, $"refused at {read.SourceIndex}");
        var inventory = read.Inventory!;
        var steps = AreaStepInspector.ValidateOriginalData(snapshot, P("modules/Arcanum.dat"), P("arcanum2.dat"), P("arcanum3.dat"),
            P("modules/Arcanum.PATCH0"), P(Mobile), 1);
        var build = AreaInventoryMap.Build(snapshot, steps);
        Assert.AreEqual(AreaInventoryMapStatus.Ready, build.Status, build.Detail);
        var inventoryMap = build.Map!;
        string Entity(int sourceIndex) => "oid:" + (inventory.TryGetItem(sourceIndex, out var item) ? item.Key : Holder(inventory, sourceIndex).Key).ObjectIdSha256;
        var dead = inventory.Holders.Where(holder => inventoryMap.Seed.Holders.Single(h => h.Entity == Entity(holder.SourceIndex)).Dead)
            .Select(holder => holder.SourceIndex).ToHashSet();
        // docs/research/crash-site-item-inventory.md section 1: the four NPCs carrying 32,000 damage.
        CollectionAssert.AreEquivalent(new[] { 5699, 4457, 4200, 1201 }, dead.Where(index => inventory.ListOf(index).Count > 0).ToArray());
        Assert.AreEqual(dead.Count, inventoryMap.DeadNpcs);
        Assert.AreEqual(0, inventoryMap.NpcsWithoutEvidence, "every NPC on an admitted cell has step evidence for its life");

        using var session = ConversationSessionFixture.Create(snapshot.RuntimeMap);
        var map = new SessionMapId(snapshot.RuntimeMap);
        var viewer = new AreaInventoryHost([inventoryMap]);
        Assert.AreEqual(GameSessionFailure.None, viewer.Attach(session));
        AreaObjectKey Key(int sourceIndex) => inventory.TryGetItem(sourceIndex, out var item) ? item.Key : Holder(inventory, sourceIndex).Key;
        // ARC-UI-004 click targets: every core ground item, every dead NPC; never Virgil (alive) or a chest.
        var ground = inventory.Items.Where(item => item.Scope == AreaScope.Core && !item.InInventory).ToArray();
        Assert.AreEqual(37, ground.Length);
        Assert.IsTrue(ground.All(item => viewer.Target(session, map.RuntimeMap, item.Key)?.Kind == AreaInventoryTargetKind.GroundItem));
        Assert.IsTrue(dead.All(index => viewer.Target(session, map.RuntimeMap, Key(index))?.Kind == AreaInventoryTargetKind.DeadNpc));
        Assert.IsNull(viewer.Target(session, map.RuntimeMap, Key(2907)));
        Assert.IsNull(viewer.Target(session, map.RuntimeMap, Key(4453)));

        // Virgil's first talk through the viewer's conversation host (ARC-UI-004 wiring): the DLG node 1 entry action
        // IN -2804 hands over the ring through the session inventory port.
        VirgilDialog(P("arcanum3.dat"));
        using var scripts = ConversationSessionFixture.Scripts(root);
        var host = new AreaConversationHost(snapshot, scripts, session, inventory: viewer);
        Assert.IsTrue(host.InventoryWired);
        var speaker = host.Speakers.Single();
        var virgil = AreaConversationState.EntityId(speaker.Row);
        Assert.AreEqual(Entity(2907), virgil);
        var start = host.Begin(host.Nearest(speaker.WorldX, speaker.WorldY));
        Assert.AreEqual(AreaConversationStartStatus.Started, start.Status, start.Detail);
        var talk = start.Conversation!;
        Assert.AreEqual((1, DialogConversationPhase.AwaitingSelection), (talk.CurrentNode.Value, talk.Phase));
        var give = talk.Effects.Single(effect => effect.Command is GiveItemCommand);
        Assert.AreEqual((2804, DialogCommandAttemptOutcome.Applied), (((GiveItemCommand)give.Command).ItemName, give.Outcome));
        Assert.AreEqual(0, host.State.Items.Count, "IN went to the session, not to the in-memory record");
        Assert.AreEqual(new SessionPcItem(Entity(1714), map, new(SessionItemType.Armor, Item(inventory, 1714).Prototype, 2804, 30082)),
            session.QueryPcInventory().Single());
        CollectionAssert.AreEqual(new[] { Entity(2421), Entity(2908) }, Current(session, map, virgil));
        Assert.IsTrue(talk.Close());

        // Loot without script content: five of the six dead-NPC items; 2527 has its own insert-item script, which needs the
        // script host (ARC-SCRIPT-013, below).
        foreach (var (holder, item) in new[] { (5699, 3312), (5699, 7282), (4457, 4698), (4457, 6177), (4200, 4201) })
            Assert.AreEqual(GameSessionDisposition.Changed, viewer.Loot(session, Entity(holder), Entity(item)).Disposition, item.ToString());
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, viewer.Loot(session, Entity(1201), Entity(2527)).Failure);
        Assert.AreEqual(1, viewer.View(session, null, map.RuntimeMap, Entity(1201)).Loot.Count, "the refused item stays on the body");
        Assert.AreEqual(GameSessionFailure.HolderNotLootable, session.LootItem(session.Version, virgil, Entity(2421), Accept).Failure);
        Assert.AreEqual(GameSessionFailure.HolderNotLootable, session.LootItem(session.Version, Entity(4453), Entity(2421), Accept).Failure, "chests are generated, not looted");

        // Pickup: every one of the 37 unscripted core ground items; each is then no longer drawn.
        foreach (var item in ground)
            Assert.AreEqual(GameSessionDisposition.Changed, viewer.PickUp(session, Entity(item.SourceIndex)).Disposition, item.SourceIndex.ToString());
        Assert.IsTrue(ground.All(item => viewer.Taken(session, map.RuntimeMap).Contains(item.Key) && viewer.Target(session, map.RuntimeMap, item.Key) is null));
        var pc = session.QueryPcInventory();
        Assert.AreEqual(1 + 5 + 37, pc.Count);
        Assert.IsFalse(session.QueryMapInventory(map)!.Ground.Any(g => ground.Any(item => Entity(item.SourceIndex) == g.Item.Entity)));

        // Save and load keep the list; the seed is attached again and agrees with it.
        using var loaded = ConversationSessionFixture.SaveAndLoad(session);
        CollectionAssert.AreEqual(pc.ToArray(), loaded.QueryPcInventory().ToArray());
        Assert.AreEqual(GameSessionFailure.None, viewer.Attach(loaded));
        CollectionAssert.AreEqual(new[] { Entity(2527) }, Current(loaded, map, Entity(1201)));
        Assert.AreEqual(GameSessionDisposition.NoChange, loaded.GiveItemToPc(loaded.Version, virgil, 2804, Accept).Disposition, "the ring is gone");

        // ARC-SCRIPT-013 (docs/research/item-insert-script.md): with the game's scripts the amulet loots; its insert-item
        // script 1341 prints line 1 of its DLG while global flag 1085 is clear and writes nothing else.
        var scripted = new AreaInventoryHost([inventoryMap], scripts: scripts);
        using var flagSet = ConversationSessionFixture.SaveAndLoad(loaded);
        Assert.AreEqual(GameSessionFailure.None, scripted.Attach(flagSet));
        var amulet = scripted.Loot(loaded, Entity(1201), Entity(2527));
        Assert.AreEqual((GameSessionDisposition.Changed, 1341, ScrStop.ReturnSkipDefault), (amulet.Disposition, amulet.InsertScript!.Script, amulet.InsertScript.Stop));
        Assert.AreEqual((1341, 1, 6), (amulet.Messages.Single().Script, amulet.Messages.Single().Line, amulet.Messages.Single().Kind));
        Assert.IsTrue(amulet.Messages.Single().Text.Trim().Length > 0, "a line of text (not asserted)");
        Assert.AreEqual(0, amulet.InsertScript.Effects.GlobalFlags.Count);
        Assert.IsFalse(Flag(loaded, 1085));
        Assert.AreEqual(0, Current(loaded, map, Entity(1201)).Length);
        Assert.AreEqual(1 + 6 + 37, loaded.QueryPcInventory().Count);
        // Once a conversation set flag 1085, the script returns at once and prints nothing.
        var write = flagSet.Flags.Write(new GlobalFlagWriteRequest(new GlobalFlagRequestId(Guid.NewGuid()), flagSet.FlagVersion, new FlagId(1085), true));
        Assert.AreEqual(GlobalFlagWriteDisposition.Applied, write.Disposition);
        var silent = scripted.Loot(flagSet, Entity(1201), Entity(2527));
        Assert.AreEqual((GameSessionDisposition.Changed, ScrStop.ReturnRunDefault, 0), (silent.Disposition, silent.InsertScript!.Stop, silent.Messages.Count));
    }

    private static bool Flag(GameSessionOwner session, int flag) =>
        session.Flags.Read(new GlobalFlagReadRequest(new GlobalFlagRequestId(Guid.NewGuid()), session.FlagVersion, new FlagId(flag))).Value == true;

    private static string[] Current(GameSessionOwner session, SessionMapId map, string holder) =>
        session.QueryMapInventory(map)!.Holders.Single(h => h.Entity == holder).Items.Select(i => i.Entity).ToArray();

    private static AreaInventoryItem Item(AreaInventory inventory, int sourceIndex) =>
        inventory.TryGetItem(sourceIndex, out var item) ? item : throw new AssertFailedException(sourceIndex.ToString());

    private static AreaInventoryHolder Holder(AreaInventory inventory, int sourceIndex) =>
        inventory.TryGetHolder(sourceIndex, out var holder) ? holder : throw new AssertFailedException(sourceIndex.ToString());

    // Virgil's DLG (script 1324) from the pinned dialog archive; ARC-RES-026 records its payload hash 34a49a2c…5e53a.
    private static DlgDocument VirgilDialog(string archivePath)
    {
        using var archive = DatArchive.Open(archivePath, new DatReaderOptions { MaxArchiveBytes = 536870912, MaxStoredEntryBytes = 1048576, MaxDecodedEntryBytes = 1048576 });
        var member = archive.Entries.Single(entry =>
            Encoding.Latin1.GetString(entry.RawNameBytes.ToArray()).Contains("01324", StringComparison.Ordinal));
        var bytes = archive.ReadEntry(member);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.IsTrue(hash.StartsWith("34a49a2c", StringComparison.Ordinal) && hash.EndsWith("5e53a", StringComparison.Ordinal), "pinned Virgil DLG");
        return DlgReader.Read(bytes);
    }
}

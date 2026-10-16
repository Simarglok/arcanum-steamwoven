using System.Security.Cryptography;
using System.Text;
using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Data.Inventory;
using Arcanum.Data.Traversal;
using Arcanum.Scripting.Dialog;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-WORLD-014: the Crash Site T0 inventory (ARC-DATA-012 view, life from the step rules) as a session seed, Virgil's
/// ring through his original DLG and the session inventory port, loot of the four dead NPCs and every ground pickup,
/// then save and load. The viewer is not wired to the session inventory yet; this checks the Session/Scripting seam on
/// real data. Opt-in: ARC_AREA_CONVERSATION_GAME_ROOT names the user-owned installation (read-only). Structural
/// identifiers only; no original text is asserted or printed.
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
        var dead = inventory.Holders.Where(holder => holder.Type == AreaInventoryHolderType.Npc && Dead(steps, holder))
            .Select(holder => holder.SourceIndex).ToHashSet();
        // docs/research/crash-site-item-inventory.md section 1: the four NPCs carrying 32,000 damage.
        CollectionAssert.AreEquivalent(new[] { 5699, 4457, 4200, 1201 }, dead.Where(index => inventory.ListOf(index).Count > 0).ToArray());

        using var session = ConversationSessionFixture.Create(snapshot.RuntimeMap);
        var map = new SessionMapId(snapshot.RuntimeMap);
        var seed = Seed(inventory, dead);
        Assert.AreEqual(GameSessionDisposition.Changed, session.AttachMapInventory(map, seed).Disposition);
        string Entity(int sourceIndex) => "oid:" + (inventory.TryGetItem(sourceIndex, out var item) ? item.Key : Holder(inventory, sourceIndex).Key).ObjectIdSha256;

        // Virgil's first talk: the DLG node 1 entry action IN -2804 hands over the ring through the session port.
        var document = VirgilDialog(P("arcanum3.dat"));
        var host = new AreaConversationHost(snapshot, P("arcanum2.dat"), P("arcanum3.dat"), session);
        var speaker = host.Speakers.Single();
        var virgil = AreaConversationState.EntityId(speaker.Row);
        Assert.AreEqual(Entity(2907), virgil);
        Assert.IsTrue(host.Begin(host.Nearest(speaker.WorldX, speaker.WorldY)).Conversation!.Close());
        var port = new SessionDialogInventoryPort(session, AreaConversationState.PcParticipant,
            npc => npc == AreaConversationState.Npc(0) ? virgil : null, Accept);
        var talk = DialogConversation.Start(document,
            new DialogConversationRequest(new DialogNodeId(1),
                new DialogParticipants(new DialogId(1324), AreaConversationState.PcParticipant, AreaConversationState.Npc(0)),
                new DialogScriptLine(4), host.Profile.Pc, host.Profile.Names),
            new DialogConversationPorts(host.State, host.State, session.Flags, host.State, Inventory: port));
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, talk.Phase);
        var give = talk.Effects.Single(effect => effect.Command is GiveItemCommand);
        Assert.AreEqual((2804, DialogCommandAttemptOutcome.Applied), (((GiveItemCommand)give.Command).ItemName, give.Outcome));
        Assert.AreEqual(Entity(1714), port.LastResult!.Value.Item);
        Assert.AreEqual(new SessionPcItem(Entity(1714), map, new(SessionItemType.Armor, Item(inventory, 1714).Prototype, 2804, 30082)),
            session.QueryPcInventory().Single());
        CollectionAssert.AreEqual(new[] { Entity(2421), Entity(2908) }, Current(session, map, virgil));
        Assert.IsTrue(talk.Close());

        // Loot: five of the six dead-NPC items; 2527 has its own insert-item script (ARC-RES-023 U frontier).
        foreach (var (holder, item) in new[] { (5699, 3312), (5699, 7282), (4457, 4698), (4457, 6177), (4200, 4201) })
            Assert.AreEqual(GameSessionDisposition.Changed, session.LootItem(session.Version, Entity(holder), Entity(item), Accept).Disposition, item.ToString());
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, session.LootItem(session.Version, Entity(1201), Entity(2527), Accept).Failure);
        Assert.AreEqual(GameSessionFailure.HolderNotLootable, session.LootItem(session.Version, virgil, Entity(2421), Accept).Failure);
        Assert.AreEqual(GameSessionFailure.HolderNotLootable, session.LootItem(session.Version, Entity(4453), Entity(2421), Accept).Failure, "chests are generated, not looted");

        // Pickup: every one of the 37 unscripted core ground items.
        var ground = inventory.Items.Where(item => item.Scope == AreaScope.Core && !item.InInventory).ToArray();
        Assert.AreEqual(37, ground.Length);
        foreach (var item in ground)
            Assert.AreEqual(GameSessionDisposition.Changed, session.PickUpItem(session.Version, Entity(item.SourceIndex), Accept).Disposition, item.SourceIndex.ToString());
        var pc = session.QueryPcInventory();
        Assert.AreEqual(1 + 5 + 37, pc.Count);
        Assert.IsFalse(session.QueryMapInventory(map)!.Ground.Any(g => ground.Any(item => Entity(item.SourceIndex) == g.Item.Entity)));

        // Save and load keep the list; the seed is attached again and agrees with it.
        using var loaded = ConversationSessionFixture.SaveAndLoad(session);
        CollectionAssert.AreEqual(pc.ToArray(), loaded.QueryPcInventory().ToArray());
        Assert.AreEqual(GameSessionDisposition.Changed, loaded.AttachMapInventory(map, Seed(inventory, dead)).Disposition);
        CollectionAssert.AreEqual(new[] { Entity(2527) }, Current(loaded, map, Entity(1201)));
        Assert.AreEqual(GameSessionDisposition.NoChange, loaded.GiveItemToPc(loaded.Version, virgil, 2804, Accept).Disposition, "the ring is gone");
    }

    // The host's mapping (to be shared with the viewer wiring): every Area holder and item, entity ids as the conversation host's.
    private static SessionMapInventorySeed Seed(AreaInventory inventory, IReadOnlySet<int> dead)
    {
        Assert.IsFalse(inventory.Holders.Any(holder => holder.Type == AreaInventoryHolderType.Pc), "no PC record at T0");
        string Id(AreaObjectKey key) => "oid:" + key.ObjectIdSha256;
        return new(
            inventory.Holders.Select(holder => new SessionSeedHolder(Id(holder.Key), (SessionHolderKind)holder.Type, dead.Contains(holder.SourceIndex),
                holder.SubstituteInventory is not null, holder.Scripts.Select(s => s.Sap).ToArray(),
                inventory.ListOf(holder.SourceIndex).Select(item => Id(item.Key)).ToArray())).ToArray(),
            inventory.Items.Select(item => new SessionSeedItem(Id(item.Key),
                new(new((SessionItemType)item.Type, item.Prototype, item.Name, item.Description), item.Weight, item.Complexity,
                    item.InventoryLocation, item.Quantity, item.Scripts.Select(s => s.Sap).ToArray()),
                item.InInventory ? null : new CellCoord(checked((int)item.WorldX!.Value), checked((int)item.WorldY!.Value)))).ToArray());
    }

    // Life from the step rules: the NPC's own evidence as the destination of a step into its cell.
    private static bool Dead(AreaStepSnapshot steps, AreaInventoryHolder holder)
    {
        if (holder.WorldX is not { } x || holder.WorldY is not { } y) return false;
        var cell = new AreaStepCell(x, y);
        if (!steps.IsAdmitted(cell)) return false;
        foreach (var direction in Enum.GetValues<CardinalTraversalDirection>())
        {
            var from = direction switch
            {
                CardinalTraversalDirection.XMinusYMinus => new AreaStepCell(x + 1, y + 1), CardinalTraversalDirection.XMinus => new(x + 1, y),
                CardinalTraversalDirection.XMinusYPlus => new(x + 1, y - 1), CardinalTraversalDirection.YPlus => new(x, y - 1),
                CardinalTraversalDirection.XPlusYPlus => new(x - 1, y - 1), CardinalTraversalDirection.XPlus => new(x - 1, y),
                CardinalTraversalDirection.XPlusYMinus => new(x - 1, y + 1), _ => new(x, y + 1),
            };
            if (!steps.IsAdmitted(from)) continue;
            var evidence = steps.Assess(steps.Request(from, cell, direction)).Objects
                .SingleOrDefault(o => o.Role == AreaStepObjectRole.Destination && o.Key == holder.Key);
            if (evidence is not null) return evidence.ReasonCode == "CRITTER_DEAD";
        }
        Assert.Fail($"no step evidence for {holder.SourceIndex}");
        return false;
    }

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

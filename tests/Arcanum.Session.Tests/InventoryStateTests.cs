using Arcanum.Scripting.Dialog;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// Pickup, loot and negative IN over an attached map seed (ARC-WORLD-014). Entities, numbers and cells are invented.
[TestClass]
public sealed class InventoryStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    internal static SessionItemFacts Facts(SessionItemType type, int name, int location = 0, int complexity = 0, int? quantity = null,
        params int[] scripts) => new(new(type, 1000 + name, name, 30000 + name), 5, complexity, location, quantity, scripts);

    private static SessionSeedItem Ground(string entity, SessionItemFacts facts, int x) => new(entity, facts, new CellCoord(x, 200));
    private static SessionSeedItem Held(string entity, SessionItemFacts facts) => new(entity, facts, null);

    // Virgil-like living NPC with the ring, a corpse with one item per rule, a corpse with a use script, an empty chest and a
    // living NPC with a substitute inventory.
    internal static SessionMapInventorySeed Seed() => new(
        [
            new("virgil", SessionHolderKind.Npc, false, false, [SessionScriptPoint.Examine], ["ring", "sword", "vest", "ring-copy"]),
            new("corpse", SessionHolderKind.Npc, true, false, [], ["note", "boots", "torn", "helmet"]),
            new("priest", SessionHolderKind.Npc, true, false, [SessionScriptPoint.Use], ["cup"]),
            new("chest", SessionHolderKind.Container, false, false, [], []),
            new("merchant", SessionHolderKind.Npc, false, true, [], ["coins"]),
        ],
        [
            Ground("apple", Facts(SessionItemType.Food, 3600), 101),
            Ground("cog", Facts(SessionItemType.Generic, 5600, complexity: -5), 102),
            Ground("charm", Facts(SessionItemType.Generic, 5601, scripts: SessionScriptPoint.Get), 103),
            Ground("glove", Facts(SessionItemType.Armor, 2810, scripts: SessionScriptPoint.InsertItem), 104),
            Held("ring", Facts(SessionItemType.Armor, 2804, scripts: SessionScriptPoint.Examine)),
            Held("sword", Facts(SessionItemType.Weapon, 2000, location: 1)),
            Held("vest", Facts(SessionItemType.Armor, 2800, location: 1006)),
            Held("ring-copy", Facts(SessionItemType.Armor, 2804, location: 2)),
            Held("note", Facts(SessionItemType.Written, 5242, scripts: SessionScriptPoint.Use)),
            Held("boots", Facts(SessionItemType.Armor, 2807, scripts: [SessionScriptPoint.Examine, SessionScriptPoint.InsertItem])),
            Held("torn", Facts(SessionItemType.Generic, 5607, location: 2, scripts: SessionScriptPoint.RemoveItem)),
            Held("helmet", Facts(SessionItemType.Armor, 2811, location: 1000)),
            Held("cup", Facts(SessionItemType.Generic, 5650)),
            Held("coins", Facts(SessionItemType.Gold, 3200, quantity: 5)),
        ]);

    internal static GameSessionOwner Attached(GameSessionLimits? limits = null)
    {
        var owner = Create(limits);
        var attached = owner.AttachMapInventory(Exterior, Seed());
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (attached.Disposition, attached.Failure));
        Assert.AreEqual(new GameSessionVersion(owner.Identity, 0), owner.Version, "a seed is not session state");
        return owner;
    }

    private static void Took(InventoryMutationResult result, GameSessionOwner owner, string item, long revision)
    {
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, item, (SessionInsertVerdict?)SessionInsertVerdict.Accepted),
            (result.Disposition, result.Failure, result.Item, result.Verdict));
        Assert.AreEqual(new GameSessionVersion(owner.Identity, revision), result.Version);
        Assert.AreEqual(result.Version, owner.Version);
    }

    private static void Refused(InventoryMutationResult result, GameSessionOwner owner, GameSessionFailure failure, long revision,
        SessionInsertVerdict? verdict = null)
    {
        Assert.AreEqual((GameSessionDisposition.Rejected, failure, (string?)null, verdict), (result.Disposition, result.Failure, result.Item, result.Verdict));
        Assert.AreEqual(new GameSessionVersion(owner.Identity, revision), owner.Version);
    }

    private static string[] Pc(GameSessionOwner owner) => owner.QueryPcInventory().Select(item => item.Entity).ToArray();

    [TestMethod]
    public void AttachRefusesMalformedSeedsAndCopiesTheAcceptedOne()
    {
        using var owner = Create();
        Assert.IsNull(owner.QueryMapInventory(Exterior));
        var seed = Seed();
        var item = seed.Items[0];
        var holder = seed.Holders[0];
        var bad = new SessionMapInventorySeed?[]
        {
            null,
            seed with { Items = [.. seed.Items, item] },
            seed with { Items = [.. seed.Items, item with { Entity = "bad id" }] },
            seed with { Items = [.. seed.Items, item with { Entity = "far", Cell = new CellCoord(-1, 0) }] },
            seed with { Items = [.. seed.Items, Held("loose", Facts(SessionItemType.Food, 1))] },
            seed with { Items = [.. seed.Items, Ground("gold", Facts(SessionItemType.Gold, 3200), 1)] },
            seed with { Items = [.. seed.Items, Ground("bread", Facts(SessionItemType.Food, 1, quantity: 2), 1)] },
            seed with { Items = [.. seed.Items, Ground("bread", Facts(SessionItemType.Food, 1) with { Kind = new((SessionItemType)4, 1, 1, 1) }, 1)] },
            seed with { Items = [.. seed.Items, Ground("bread", Facts(SessionItemType.Food, 1, scripts: [21, 21]), 1)] },
            seed with { Items = [.. seed.Items, Ground("bread", Facts(SessionItemType.Food, 1, scripts: [36]), 1)] },
            seed with { Holders = [.. seed.Holders, holder with { Entity = "twin" }] },
            seed with { Holders = [.. seed.Holders, holder] },
            seed with { Holders = [.. seed.Holders, holder with { Entity = "apple", Items = [] }] },
            seed with { Holders = [.. seed.Holders, holder with { Entity = "thief", Items = ["apple"] }] },
            seed with { Holders = [.. seed.Holders, holder with { Entity = "box", Kind = SessionHolderKind.Container, Dead = true, Items = [] }] },
            seed with { Holders = [.. seed.Holders, holder with { Entity = "pc", Kind = (SessionHolderKind)15, Items = [] }] },
        };
        foreach (var candidate in bad)
        {
            var result = owner.AttachMapInventory(Exterior, candidate!);
            Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InvalidInventorySeed), (result.Disposition, result.Failure));
        }
        Assert.AreEqual(GameSessionFailure.InvalidMap, owner.AttachMapInventory(new(0), seed).Failure);
        Assert.IsNull(owner.QueryMapInventory(Exterior));

        // The session keeps its own copy: changing the caller's lists afterwards changes nothing.
        var items = seed.Items.ToArray();
        var list = new[] { "ring", "sword", "vest", "ring-copy" };
        var mine = seed with { Items = items, Holders = [seed.Holders[0] with { Items = list }, .. seed.Holders.Skip(1)] };
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, mine).Disposition);
        list[0] = "sword"; items[0] = items[1];
        var view = owner.QueryMapInventory(Exterior)!;
        CollectionAssert.AreEqual(new[] { "apple", "cog", "charm", "glove" }, view.Ground.Select(g => g.Item.Entity).ToArray());
        CollectionAssert.AreEqual(new[] { "ring", "sword", "vest", "ring-copy" }, view.Holders[0].Items.Select(i => i.Entity).ToArray());
        Assert.AreEqual(new CellCoord(101, 200), view.Ground[0].Cell);
        Assert.AreEqual(0L, owner.Version.Revision);
    }

    [TestMethod]
    public void PickupAppendsAGroundItemOfTheActorsMapAndHidesItFromTheGround()
    {
        using var owner = Attached();
        Took(owner.PickUpItem(owner.Version, "cog", Accept), owner, "cog", 1);
        Took(owner.PickUpItem(owner.Version, "apple", Accept), owner, "apple", 2);
        CollectionAssert.AreEqual(new[] { "cog", "apple" }, Pc(owner));
        Assert.AreEqual(new SessionPcItem("cog", Exterior, new(SessionItemType.Generic, 6600, 5600, 35600)), owner.QueryPcInventory()[0]);
        CollectionAssert.AreEqual(new[] { "charm", "glove" }, owner.QueryMapInventory(Exterior)!.Ground.Select(g => g.Item.Entity).ToArray());

        Refused(owner.PickUpItem(owner.Version, "cog", Accept), owner, GameSessionFailure.ItemNotAvailable, 2);
        Refused(owner.PickUpItem(owner.Version, "sword", Accept), owner, GameSessionFailure.ItemNotAvailable, 2);
        Refused(owner.PickUpItem(owner.Version, "nothing", Accept), owner, GameSessionFailure.ItemNotAvailable, 2);
        Refused(owner.PickUpItem(owner.Version, "", Accept), owner, GameSessionFailure.InvalidItem, 2);
        Refused(owner.PickUpItem(new(owner.Identity, 1), "charm", Accept), owner, GameSessionFailure.StaleVersion, 2);
        Assert.ThrowsExactly<ArgumentNullException>(() => owner.PickUpItem(owner.Version, "charm", null!));

        // SAP_GET and SAP_INSERT_ITEM item scripts are not run, so their items are refused.
        Refused(owner.PickUpItem(owner.Version, "charm", Accept), owner, GameSessionFailure.ItemScriptUnsupported, 2);
        Refused(owner.PickUpItem(owner.Version, "glove", Accept), owner, GameSessionFailure.ItemScriptUnsupported, 2);

        // Items are taken only from the map the actor stands on.
        Changed(owner.PlaceActor(owner.Version, Start with { Map = Cave }), owner, 3);
        Refused(owner.PickUpItem(owner.Version, "charm", Accept), owner, GameSessionFailure.InventoryUnavailable, 3);
    }

    [TestMethod]
    public void TheInsertCheckRunsLastAndItsRefusalChangesNothing()
    {
        using var owner = Attached();
        var asked = new List<SessionInventoryItem>();
        SessionInsertCheck Answer(SessionInsertVerdict verdict) => item => { asked.Add(item); return verdict; };

        Refused(owner.PickUpItem(owner.Version, "charm", Answer(SessionInsertVerdict.Accepted)), owner, GameSessionFailure.ItemScriptUnsupported, 0);
        Refused(owner.LootItem(owner.Version, "virgil", "ring", Answer(SessionInsertVerdict.Accepted)), owner, GameSessionFailure.HolderNotLootable, 0);
        Assert.AreEqual(0, asked.Count, "no other refusal asks the host");

        foreach (var verdict in new[] { SessionInsertVerdict.TooHeavy, SessionInsertVerdict.TechnologyRefused, SessionInsertVerdict.NoRoom, (SessionInsertVerdict)9 })
            Refused(owner.PickUpItem(owner.Version, "cog", Answer(verdict)), owner, GameSessionFailure.InsertRefused, 0, verdict);
        Assert.AreEqual(4, asked.Count);
        Assert.AreEqual(new SessionInventoryItem("cog", Seed().Items[1].Facts).Entity, asked[0].Entity);
        Assert.AreEqual(-5, asked[0].Facts.Complexity, "the host sees the facts its check needs");
        Assert.AreEqual(0, Pc(owner).Length);
        Assert.AreEqual(4, owner.QueryMapInventory(Exterior)!.Ground.Count);
    }

    [TestMethod]
    public void ACallBackIntoTheOwnerFromTheInsertCheckMakesTheTransferStale()
    {
        using var owner = Attached();
        // The host takes the same item from inside its check: only the inner transfer commits.
        var inner = default(InventoryMutationResult);
        Refused(owner.PickUpItem(owner.Version, "apple", _ => { inner = owner.PickUpItem(owner.Version, "apple", Accept); return SessionInsertVerdict.Accepted; }),
            owner, GameSessionFailure.StaleVersion, 1, SessionInsertVerdict.Accepted);
        Took(inner, owner, "apple", 1);
        CollectionAssert.AreEqual(new[] { "apple" }, Pc(owner));
        Assert.AreEqual(SnapshotDecodeFailure.None, GameSessionSnapshotCodec.TryDecode(GameSessionSnapshotCodec.Encode(owner.Capture())).Failure);

        // A move to another map or a new seed during the check is stale as well, with no effect.
        Refused(owner.PickUpItem(owner.Version, "cog", _ => { owner.PlaceActor(owner.Version, Start with { Map = Cave }); return SessionInsertVerdict.Accepted; }),
            owner, GameSessionFailure.StaleVersion, 2, SessionInsertVerdict.Accepted);
        Changed(owner.PlaceActor(owner.Version, Start), owner, 3);
        Refused(owner.PickUpItem(owner.Version, "cog", _ => { owner.AttachMapInventory(Exterior, Seed()); return SessionInsertVerdict.Accepted; }),
            owner, GameSessionFailure.StaleVersion, 3, SessionInsertVerdict.Accepted);
        CollectionAssert.AreEqual(new[] { "apple" }, Pc(owner));
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.PickUpItem(owner.Version, "cog", _ => { owner.Dispose(); return SessionInsertVerdict.Accepted; }));
    }

    [TestMethod]
    public void OneItemCannotLieOnTwoAttachedMaps()
    {
        using var owner = Attached();
        var cave = new SessionMapInventorySeed([], [Ground("apple", Facts(SessionItemType.Food, 3600), 5)]);
        Assert.AreEqual(GameSessionFailure.InventoryMismatch, owner.AttachMapInventory(Cave, cave).Failure);
        Assert.IsNull(owner.QueryMapInventory(Cave));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Cave, cave with { Items = [Ground("pear", Facts(SessionItemType.Food, 3601), 5)] }).Disposition);
        // Replacing a map's own seed is not a conflict with itself.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, Seed()).Disposition);
    }

    [TestMethod]
    public void LootTakesOnlyFromADeadNpcAndRefusesUnmodelledItems()
    {
        using var owner = Attached();
        Refused(owner.LootItem(owner.Version, "virgil", "sword", Accept), owner, GameSessionFailure.HolderNotLootable, 0);
        // ARC-WORLD-101: a container is lootable, but only its own items.
        Refused(owner.LootItem(owner.Version, "chest", "sword", Accept), owner, GameSessionFailure.ItemNotAvailable, 0);
        Refused(owner.LootItem(owner.Version, "priest", "cup", Accept), owner, GameSessionFailure.HolderScriptUnsupported, 0);
        Refused(owner.LootItem(owner.Version, "ghost", "cup", Accept), owner, GameSessionFailure.UnknownHolder, 0);
        Refused(owner.LootItem(owner.Version, "apple", "apple", Accept), owner, GameSessionFailure.UnknownHolder, 0);
        Refused(owner.LootItem(owner.Version, "corpse", "sword", Accept), owner, GameSessionFailure.ItemNotAvailable, 0);
        Refused(owner.LootItem(owner.Version, "corpse", "apple", Accept), owner, GameSessionFailure.ItemNotAvailable, 0);
        Refused(owner.LootItem(owner.Version, "corpse", "boots", Accept), owner, GameSessionFailure.ItemScriptUnsupported, 0);
        Refused(owner.LootItem(owner.Version, "corpse", "torn", Accept), owner, GameSessionFailure.ItemScriptUnsupported, 0);
        Refused(owner.LootItem(owner.Version, "corpse", "x y", Accept), owner, GameSessionFailure.InvalidItem, 0);

        // A use script on the item itself does not run on a transfer.
        Took(owner.LootItem(owner.Version, "corpse", "note", Accept), owner, "note", 1);
        Refused(owner.LootItem(owner.Version, "corpse", "note", Accept), owner, GameSessionFailure.ItemNotAvailable, 1);
        var corpse = owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == "corpse");
        Assert.IsTrue(corpse.Dead);
        CollectionAssert.AreEqual(new[] { "boots", "torn", "helmet" }, corpse.Items.Select(i => i.Entity).ToArray());
        CollectionAssert.AreEqual(new[] { "note" }, Pc(owner));
    }

    // ARC-DEV-202 (B03): the loot takes a worn item off the body (the removal 0x466E00 unwields; the transfer 0x461CA0 and Take all
    // 0x579770 test no worn state); the other parented moves (a negative IN, a steal, a purchase) still refuse it.
    [TestMethod]
    public void LootTakesAWornItemOffTheBodyAndTheOtherParentedMovesKeepRefusingIt()
    {
        using var owner = Attached();
        // The insert check sees the item unwielded (no location), as after the removal.
        SessionInventoryItem? seen = null;
        Took(owner.LootItem(owner.Version, "corpse", "helmet", item => { seen = item; return SessionInsertVerdict.Accepted; }), owner, "helmet", 1);
        Assert.AreEqual(-1, seen!.Facts.InventoryLocation);
        CollectionAssert.AreEqual(new[] { "helmet" }, Pc(owner));
        var corpse = owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == "corpse");
        CollectionAssert.AreEqual(new[] { "note", "boots", "torn" }, corpse.Items.Select(i => i.Entity).ToArray());
        Refused(owner.LootItem(owner.Version, "corpse", "helmet", Accept), owner, GameSessionFailure.ItemNotAvailable, 1);
        // A refused insert check leaves the worn item on the body.
        using var heavy = Attached();
        Refused(heavy.LootItem(heavy.Version, "corpse", "helmet", _ => SessionInsertVerdict.TooHeavy), heavy, GameSessionFailure.InsertRefused, 0, SessionInsertVerdict.TooHeavy);
        Assert.IsTrue(heavy.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == "corpse").Items.Any(i => i.Entity == "helmet" && i.Facts.Worn));
        // The living NPC's worn vest: a purchase or a steal keeps its refusal (not read for those paths).
        using var other = Attached();
        Assert.AreEqual(GameSessionFailure.WornItemUnsupported, other.TakeItemFromNpc(other.Version, "virgil", "vest", Accept).Failure);
    }

    // ARC-SCRIPT-1987 (WL3): the dialog's negative IN is the transfer 0x461CA0, which tests no worn state of its source; its removal takes the worn item off the NPC.
    [TestMethod]
    public void NegativeInTakesAWornItemOffTheNpcAndMarksItToReWield()
    {
        using var owner = Attached();
        SessionInventoryItem? seen = null;
        Took(owner.GiveItemToPc(owner.Version, "virgil", 2800, item => { seen = item; return SessionInsertVerdict.Accepted; }), owner, "vest", 1);
        Assert.AreEqual(-1, seen!.Facts.InventoryLocation, "the insert check sees the item unwielded");
        CollectionAssert.AreEqual(new[] { "vest" }, Pc(owner));
        CollectionAssert.AreEqual(new[] { "ring", "sword", "ring-copy" }, owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == "virgil").Items.Select(i => i.Entity).ToArray());
        // Through the dialog port: the command applies and the item reaches the hero.
        using var viaPort = Attached();
        var port = new SessionDialogInventoryPort(viaPort, new(1), id => id == new DialogParticipantId(2) ? "virgil" : null, Accept);
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new GiveItemCommand(new(2), new(1), 2800)));
        CollectionAssert.AreEqual(new[] { "vest" }, Pc(viaPort));
        // A refused insert check puts the item on the hero's tile, worn no more; the NPC's list lost it too.
        using var heavy = Attached();
        var result = heavy.GiveItemToPc(heavy.Version, "virgil", 2800, _ => SessionInsertVerdict.TooHeavy);
        Assert.AreEqual((GameSessionDisposition.Changed, "vest"), (result.Disposition, result.Item));
        Assert.AreEqual(0, Pc(heavy).Length);
        Assert.IsTrue(heavy.QueryPlacedItems().Any(placed => placed.Entity == "vest"));
        CollectionAssert.AreEqual(new[] { "ring", "sword", "ring-copy" }, heavy.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == "virgil").Items.Select(i => i.Entity).ToArray());
    }

    [TestMethod]
    public void NegativeInGivesTheFirstItemWithTheNameAndAMissingNameChangesNothing()
    {
        using var owner = Attached();
        Took(owner.GiveItemToPc(owner.Version, "virgil", 2804, Accept), owner, "ring", 1);
        // The list's next match is taken next, in list order.
        Took(owner.GiveItemToPc(owner.Version, "virgil", 2804, Accept), owner, "ring-copy", 2);
        var none = owner.GiveItemToPc(owner.Version, "virgil", 2804, Accept);
        Assert.AreEqual((GameSessionDisposition.NoChange, GameSessionFailure.None, (string?)null, (SessionInsertVerdict?)null),
            (none.Disposition, none.Failure, none.Item, none.Verdict));
        Assert.AreEqual(2L, owner.Version.Revision);
        CollectionAssert.AreEqual(new[] { "ring", "ring-copy" }, Pc(owner));
        CollectionAssert.AreEqual(new[] { "sword", "vest" }, owner.QueryMapInventory(Exterior)!.Holders[0].Items.Select(i => i.Entity).ToArray());

        Refused(owner.GiveItemToPc(owner.Version, "merchant", 3200, Accept), owner, GameSessionFailure.ItemTypeUnsupported, 2);
        Refused(owner.GiveItemToPc(owner.Version, "merchant", 2804, Accept), owner, GameSessionFailure.SubstituteInventoryUnsupported, 2);
        Refused(owner.GiveItemToPc(owner.Version, "chest", 2804, Accept), owner, GameSessionFailure.UnknownHolder, 2);
        Refused(owner.GiveItemToPc(owner.Version, "virgil", 0, Accept), owner, GameSessionFailure.InvalidItem, 2);
        // ARC-WORLD-1131: a refused insert does not refuse the IN: the item leaves the NPC and lies on the PC's tile (0x415BA0), not in the PC's list.
        var heavy = owner.GiveItemToPc(owner.Version, "virgil", 2000, _ => SessionInsertVerdict.TooHeavy);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "sword", (SessionInsertVerdict?)SessionInsertVerdict.TooHeavy),
            (heavy.Disposition, heavy.Failure, heavy.Item, heavy.Verdict));
        Assert.AreEqual(3L, owner.Version.Revision, "one revision for the move and the placement");
        CollectionAssert.AreEqual(new[] { "ring", "ring-copy" }, Pc(owner));
        var lies = owner.QueryPlacedItems().Single();
        Assert.AreEqual(("sword", SessionPlacedSource.Pc), (lies.Entity, lies.Source));
        CollectionAssert.AreEqual(new[] { "vest" }, owner.QueryMapInventory(Exterior)!.Holders[0].Items.Select(i => i.Entity).ToArray());
    }

    [TestMethod]
    public void TheCallersPcItemLimitIsABudget()
    {
        using var owner = Attached(new GameSessionLimits(maxPcItems: 1));
        Took(owner.PickUpItem(owner.Version, "apple", Accept), owner, "apple", 1);
        Refused(owner.PickUpItem(owner.Version, "cog", Accept), owner, GameSessionFailure.BudgetExceeded, 1);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new GameSessionLimits(maxPcItems: GameSessionLimits.PolicyMaxPcItems + 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new GameSessionLimits(maxPcItems: 0));
    }

    [TestMethod]
    public void SaveKeepsThePcListAndAReattachedSeedMustAgreeWithIt()
    {
        using var owner = Attached();
        Took(owner.GiveItemToPc(owner.Version, "virgil", 2804, Accept), owner, "ring", 1);
        Took(owner.PickUpItem(owner.Version, "apple", Accept), owner, "apple", 2);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, NoHero + ",\"inventory\":{\"gold\":0,\"kit\":[],\"pc\":[" +
            "{\"entity\":\"ring\",\"map\":79,\"type\":\"Armor\",\"prototype\":3804,\"name\":2804,\"description\":32804}," +
            "{\"entity\":\"apple\",\"map\":79,\"type\":\"Food\",\"prototype\":4600,\"name\":3600,\"description\":33600}],\"made\":[],\"npcs\":[],\"placed\":[],\"ammo\":[],\"given\":[],\"piles\":[],\"scriptSlots\":[]," +
            // ARC-WORLD-085: each taken item keeps its seed's weight facts.
            "\"weights\":[{\"entity\":\"apple\",\"weight\":5,\"magicWeight\":0,\"complexity\":0},{\"entity\":\"ring\",\"weight\":5,\"magicWeight\":0,\"complexity\":0}],\"identified\":[],\"lastDiscardedMade\":0," +
            // ARC-UI-019: each taken item takes the next free grid slot.
            "\"grid\":[{\"entity\":\"ring\",\"slot\":0,\"width\":1,\"height\":1},{\"entity\":\"apple\",\"slot\":1,\"width\":1,\"height\":1}],\"generated\":[],\"npcWorn\":[],\"itemFields\":[],\"holderSlots\":[]},\"objects\":{\"junkPiles\":[],\"bloodPools\":[],\"lastJunkNumber\":0,\"lastPoolNumber\":0,\"state\":[],\"links\":[],\"created\":[],\"lastCreatedNumber\":0}" +
            NoEquipment + NoDurability + NoSchematics + NoEffects + NoExperience + NoAdvancement + FreshRandom + NoLife + NoReactions + NoCombat + NoCombatMode + NoConcealment +
            // ARC-WORLD-1284: the ring left virgil's list (0x467860), so its re-wield bit (NPC flag 0x40) is saved with it.
            ",\"danger\":{\"records\":[{\"entity\":\"virgil\",\"flags\":[\"CheckWield\"]}]}" + NoSound + NoEvents + NoHotkeys + NoScripts + NoResidency + NoGoals + NoSpells + NoNewspaper + NoGenerators + NoFlights + NoEncounters + NoBarLights + NoGamblingRoll + ",\"lighting\":null}");

        using var restored = Restore(Decode(text));
        CollectionAssert.AreEqual(owner.QueryPcInventory().ToArray(), restored.QueryPcInventory().ToArray());
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        // The seed is not saved: nothing is offered until the host attaches the map again.
        Assert.IsNull(restored.QueryMapInventory(Exterior));
        Refused(restored.PickUpItem(restored.Version, "cog", Accept), restored, GameSessionFailure.InventoryUnavailable, 2);

        var seed = Seed();
        void Mismatch(SessionMapId map, SessionMapInventorySeed other) =>
            Assert.AreEqual(GameSessionFailure.InventoryMismatch, restored.AttachMapInventory(map, other).Failure);
        Mismatch(Exterior, seed with { Items = seed.Items.Where(i => i.Entity != "apple").ToArray() });
        Mismatch(Exterior, seed with { Items = seed.Items.Select(i => i.Entity == "ring" ? i with { Facts = i.Facts with { Kind = i.Facts.Kind with { Description = 1 } } } : i).ToArray() });
        Mismatch(Cave, seed);
        Assert.AreEqual(GameSessionDisposition.Changed, restored.AttachMapInventory(Exterior, seed).Disposition);
        CollectionAssert.AreEqual(new[] { "cog", "charm", "glove" }, restored.QueryMapInventory(Exterior)!.Ground.Select(g => g.Item.Entity).ToArray());
        CollectionAssert.AreEqual(new[] { "sword", "vest", "ring-copy" }, restored.QueryMapInventory(Exterior)!.Holders[0].Items.Select(i => i.Entity).ToArray());
        Took(restored.PickUpItem(restored.Version, "cog", Accept), restored, "cog", 3);
        CollectionAssert.AreEqual(new[] { "ring", "apple", "cog" }, Pc(restored));

        var small = GameSessionOwner.Restore(owner.Capture(), RestoreOptions(new GameSessionLimits(maxPcItems: 1)));
        Assert.AreEqual((null, GameSessionFailure.BudgetExceeded), (small.Owner, small.Failure));
    }

    [TestMethod]
    public void DecodeAndRestoreRefusePcItemsTheTransfersCannotProduce()
    {
        const string Item = "{\"entity\":\"ring\",\"map\":79,\"type\":\"Armor\",\"prototype\":1,\"name\":2804,\"description\":2}";
        string With(string pc) => GoldenJson.Replace(EmptyInventory, ",\"inventory\":{\"gold\":0,\"kit\":[],\"pc\":[" + pc + "],\"made\":[],\"npcs\":[],\"placed\":[],\"ammo\":[],\"given\":[],\"piles\":[],\"scriptSlots\":[],\"weights\":[],\"identified\":[],\"lastDiscardedMade\":0,\"grid\":[],\"generated\":[],\"npcWorn\":[],\"itemFields\":[],\"holderSlots\":[]},\"objects\":{\"junkPiles\":[],\"bloodPools\":[],\"lastJunkNumber\":0,\"lastPoolNumber\":0,\"state\":[],\"links\":[],\"created\":[],\"lastCreatedNumber\":0}");
        void Fails(string pc, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(With(pc));
            Assert.AreEqual((null, failure, path), (result.Snapshot, result.Failure, result.Path), pc);
        }
        Assert.AreEqual(1, Decode(With(Item)).Inventory.Pc.Count);
        Fails(Item + "," + Item, SnapshotDecodeFailure.NotCanonical, "$.inventory.pc[1].entity");
        Fails(Item.Replace("Armor", "Bogus"), SnapshotDecodeFailure.OutOfRange, "$.inventory.pc[0].type");
        Fails(Item.Replace("\"map\":79", "\"map\":0"), SnapshotDecodeFailure.OutOfRange, "$.inventory.pc[0].map");
        Fails(Item.Replace("\"ring\"", "\"r i\""), SnapshotDecodeFailure.OutOfRange, "$.inventory.pc[0].entity");
        Fails(Item.Replace("}", ",\"slot\":0}"), SnapshotDecodeFailure.UnknownField, "$.inventory.pc[0].slot");
        Fails(Item.Replace(",\"description\":2", ""), SnapshotDecodeFailure.MissingField, "$.inventory.pc[0].description");
        var notList = GameSessionSnapshotCodec.TryDecode(GoldenJson.Replace(EmptyInventory, ",\"inventory\":{\"gold\":0,\"kit\":[],\"pc\":{},\"made\":[],\"npcs\":[],\"placed\":[],\"ammo\":[],\"given\":[],\"piles\":[],\"scriptSlots\":[],\"weights\":[],\"identified\":[],\"lastDiscardedMade\":0,\"grid\":[],\"generated\":[],\"npcWorn\":[],\"itemFields\":[],\"holderSlots\":[]},\"objects\":{\"junkPiles\":[],\"bloodPools\":[],\"lastJunkNumber\":0,\"lastPoolNumber\":0,\"state\":[],\"links\":[],\"created\":[],\"lastCreatedNumber\":0}"));
        Assert.AreEqual((SnapshotDecodeFailure.WrongType, "$.inventory.pc"), (notList.Failure, notList.Path));

        // Gold and keys never enter the list, so a save holding one is refused on restore; ammunition (ARC-WORLD-066) needs its stack.
        foreach (var type in new[] { "Gold", "Key", "KeyRing" })
        {
            var result = GameSessionOwner.Restore(Decode(With(Item.Replace("Armor", type))), RestoreOptions());
            Assert.AreEqual((null, GameSessionFailure.InvalidItem), (result.Owner, result.Failure), type);
        }
        var noStack = GameSessionOwner.Restore(Decode(With(Item.Replace("Armor", "Ammo"))), RestoreOptions());
        Assert.AreEqual((null, GameSessionFailure.InvalidSnapshot), (noStack.Owner, noStack.Failure), "ammunition without its stack");
    }

    [TestMethod]
    public void TheDialogPortRunsNegativeInAgainstTheSession()
    {
        using var owner = Attached();
        var pc = new DialogParticipantId(1);
        var npc = new DialogParticipantId(2);
        var port = new SessionDialogInventoryPort(owner, pc, id => id == npc ? "virgil" : null, Accept);
        Assert.IsNull(port.LastResult);
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new GiveItemCommand(npc, pc, 2804)));
        Assert.AreEqual("ring", port.LastResult!.Value.Item);
        CollectionAssert.AreEqual(new[] { "ring" }, Pc(owner));
        // The port reads the current version, so a second IN after other session changes still applies.
        Changed(owner.PlaceActor(owner.Version, Start with { Facing = NeighborDirection.XMinus }), owner, 2);
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new GiveItemCommand(npc, pc, 2804)));
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new GiveItemCommand(npc, pc, 2804)), "no such item: the source does nothing");
        Assert.AreEqual(GameSessionDisposition.NoChange, port.LastResult!.Value.Disposition);

        Assert.AreEqual(DialogCommandResult.Rejected, port.Execute(new GiveItemCommand(new(3), pc, 2000)));
        Assert.IsNull(port.LastResult);
        Assert.AreEqual(DialogCommandResult.Rejected, port.Execute(new GiveItemCommand(npc, new(4), 2000)));
        CollectionAssert.AreEqual(new[] { "ring", "ring-copy" }, Pc(owner));

        var refusing = new SessionDialogInventoryPort(owner, pc, _ => "virgil", _ => SessionInsertVerdict.NoRoom);
        // ARC-WORLD-1131: a refused insert is not a failed command: the item lies on the PC's tile and the conversation goes on.
        Assert.AreEqual(DialogCommandResult.Applied, refusing.Execute(new GiveItemCommand(npc, pc, 2000)));
        Assert.AreEqual((GameSessionDisposition.Changed, SessionInsertVerdict.NoRoom), (refusing.LastResult!.Value.Disposition, refusing.LastResult.Value.Verdict!.Value));
        Assert.AreEqual("sword", owner.QueryPlacedItems().Single().Entity);
        Assert.ThrowsExactly<ArgumentNullException>(() => new SessionDialogInventoryPort(owner, pc, _ => null, null!));
    }
}

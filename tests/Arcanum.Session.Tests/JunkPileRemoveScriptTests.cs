using Arcanum.Data.Scripts;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-2899 (ledger IE3, JP4, TQ20; exe read 0x463540 → 0x466E00 → 0x467860, 0x43CCA0, 0x466E50): the remove-item script (point 28) of the one item a dissolving junk pile releases runs with the pile as the triggerer, before the pile is destroyed and the item
// dropped again on its cell, on every path that dissolves a pile: the loot window's close (a pile the session made, a pile of the map's data), an item that decays or is destroyed in a pile (an event pass), and an NPC's pickup. Synthetic seeds, invented script numbers.
[TestClass]
public sealed class JunkPileRemoveScriptTests
{
    private const int RemoveScript = 702;
    private static readonly CellCoord There = new(120, 210), PileCell = new(120, 210);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private sealed class World(RemoveItemScriptTests.Host? host) : ISessionLootWindowWorld
    {
        public bool LootWindowShowsContainer => false;
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
        public ISessionItemScriptHost? ItemScriptHost => host;
    }

    // Two seed items the hero picks up and drops on one cell (a pile of two), with the apple's remove-item script; the NPC and a chest-less map.
    private static SessionMapInventorySeed Seed() => new(
        [new("npc", SessionHolderKind.Npc, false, false, [], []), new("pile", SessionHolderKind.Container, false, false, [], ["rag", "bone"])],
        [
            new SessionSeedItem("apple", Facts(SessionItemType.Food, 3600, scripts: SessionScriptPoint.RemoveItem), new CellCoord(90, 200)),
            new SessionSeedItem("cup", Facts(SessionItemType.Generic, 5650), new CellCoord(91, 200)),
            new SessionSeedItem("rag", Facts(SessionItemType.Generic, 5651, scripts: SessionScriptPoint.RemoveItem), null),
            new SessionSeedItem("bone", Facts(SessionItemType.Generic, 5652), null),
        ]);

    private static GameSessionOwner Owner()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HeroStateTests.HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        return owner;
    }

    // A pile of two made on the cell: the cup and the apple (scripted), the apple dropped last.
    private static GameSessionOwner PileOwner()
    {
        var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "apple", Accept).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "cup", Accept).Disposition);
        owner.PlaceHeroItem("cup", There);
        owner.PlaceHeroItem("apple", There);
        Assert.AreEqual(2, owner.QueryJunkPiles().Single().Items.Count);
        return owner;
    }

    private static RemoveItemScriptTests.Host Hosted(GameSessionOwner owner, params string[] items)
    {
        var host = new RemoveItemScriptTests.Host(owner);
        foreach (var item in items) host.Slots[(item, SessionScriptPoint.RemoveItem)] = RemoveScript;
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.PrintLine, (Number, 2), (Number, 0)), C(ScrCatalogue.Action.ReturnSkipDefault));
        return host;
    }

    private static List<string> Standing(GameSessionOwner owner, RemoveItemScriptTests.Host host)
    {
        var lines = new List<string>();
        owner.StandingItemScripts = () => host;
        owner.ItemRunOutput = (messages, _) => lines.AddRange(messages.Select(message => message.Text));
        return lines;
    }

    private static void Advance(GameSessionOwner owner, long ms) => Assert.AreEqual(ClockMutationDisposition.Changed,
        owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);

    [TestMethod]
    public void The_loot_windows_close_runs_the_released_items_script_with_the_pile_as_the_triggerer_before_the_pile_goes()
    {
        using var owner = PileOwner();
        var host = Hosted(owner, "apple");
        var lines = Standing(owner, host);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LootItem(owner.Version, "junk:1", "cup", Accept).Disposition);
        Assert.AreEqual(1, owner.QueryJunkPiles().Count, "the window shows the pile");
        var revision = owner.Version.Revision;
        var closed = owner.CloseJunkPile(owner.Version, "junk:1");
        Assert.AreEqual((GameSessionDisposition.Changed, revision + 1), (closed.Disposition, owner.Version.Revision), "one revision");
        CollectionAssert.AreEqual(new[] { $"line 2 of {RemoveScript}" }, lines, "a pile is no critter: the plain line variant");
        Assert.IsTrue(ReadFlag(owner, 3) == true, "the run's flag was applied");
        Assert.AreEqual(0, owner.QueryJunkPiles().Count);
        Assert.AreEqual(("apple", There), owner.QueryMapInventory(Exterior)!.Ground.Select(g => (g.Item.Entity, g.Cell)).Single(), "the item is dropped again after the script");
        Assert.AreEqual(1, owner.QueryItemDecays().Count(decay => decay.Entity == "apple"), "with a new decay");
    }

    [TestMethod]
    public void A_pile_that_keeps_two_items_or_releases_an_unscripted_one_runs_no_script()
    {
        using var owner = PileOwner();
        var host = Hosted(owner, "apple");
        var lines = Standing(owner, host);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.CloseJunkPile(owner.Version, "junk:1").Disposition, "two items stay");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LootItem(owner.Version, "junk:1", "apple", Accept).Disposition);
        var loot = lines.Count;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.CloseJunkPile(owner.Version, "junk:1").Disposition, "the cup goes back to the ground");
        Assert.AreEqual((loot, false), (lines.Count, ReadFlag(owner, 3) == true), "the cup has no point 28");
    }

    [TestMethod]
    public void A_close_that_cannot_run_the_released_script_is_refused_and_the_pile_stays()
    {
        using var owner = PileOwner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LootItem(owner.Version, "junk:1", "cup", Accept).Disposition);
        // The apple's point 28 is the seed row's: with no host at all, the close is refused whole.
        var before = owner.Version;
        var refused = owner.CloseJunkPile(owner.Version, "junk:1");
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, before), (refused.Disposition, refused.Failure, owner.Version));
        Assert.AreEqual(1, owner.QueryJunkPiles().Count);
        // A host that throws counts as none; the standing host once it answers closes it.
        owner.StandingItemScripts = () => throw new InvalidOperationException("no host");
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.CloseJunkPile(owner.Version, "junk:1").Failure);
        var host = Hosted(owner, "apple");
        Standing(owner, host);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.CloseJunkPile(owner.Version, "junk:1").Disposition);
        Assert.AreEqual(0, owner.QueryJunkPiles().Count);
    }

    [TestMethod]
    public void An_item_that_decays_in_a_pile_of_two_runs_the_other_ones_script_through_the_passs_host()
    {
        using var owner = PileOwner();
        var host = Hosted(owner, "apple");
        var lines = new List<string>();
        owner.ItemRunOutput = (messages, _) => lines.AddRange(messages.Select(message => message.Text));
        Advance(owner, ItemDecayRules.DelayMs);
        var fired = owner.ProcessEvents(new World(host)).Decays.First();
        Assert.AreEqual(("cup", ItemDecayStep.Destroy), (fired.Entity, fired.Step));
        Assert.AreEqual(0, owner.QueryJunkPiles().Count, "left with the apple, the pile dissolved");
        CollectionAssert.AreEqual(new[] { $"line 2 of {RemoveScript}" }, lines);
        Assert.IsTrue(ReadFlag(owner, 3) == true);
        Assert.AreEqual("apple", owner.QueryMapInventory(Exterior)!.Ground.Single().Item.Entity);
    }

    [TestMethod]
    public void A_pass_with_no_host_dissolves_the_pile_without_the_script()
    {
        using var owner = PileOwner();
        Advance(owner, ItemDecayRules.DelayMs);
        owner.ProcessEvents(new World(null));
        Assert.AreEqual(0, owner.QueryJunkPiles().Count, "an event pass cannot refuse the dissolve");
        Assert.AreEqual("apple", owner.QueryMapInventory(Exterior)!.Ground.Single().Item.Entity);
        Assert.IsFalse(ReadFlag(owner, 3) == true);
    }

    [TestMethod]
    public void An_npcs_pickup_that_leaves_one_item_runs_the_released_items_script_with_the_given_host()
    {
        using var owner = PileOwner();
        var host = Hosted(owner, "apple");
        var lines = new List<string>();
        owner.ItemRunOutput = (messages, _) => lines.AddRange(messages.Select(message => message.Text));
        var picked = owner.PickUpItemByNpc(owner.Version, "npc", "cup", Accept, null, host);
        Assert.AreEqual(GameSessionDisposition.Changed, picked.Disposition, picked.Failure.ToString());
        Assert.AreEqual(0, owner.QueryJunkPiles().Count);
        CollectionAssert.AreEqual(new[] { $"line 2 of {RemoveScript}" }, lines);
        Assert.IsTrue(ReadFlag(owner, 3) == true);
        Assert.AreEqual("apple", owner.QueryMapInventory(Exterior)!.Ground.Single().Item.Entity);
    }

    private static SessionObjectTarget MapPile() => new(Exterior, "pile", new SessionObjectBaseline
    {
        Kind = DamageableObjectKind.Container, Points = 6, Material = ObjectMaterial.Cloth, JunkPile = true, Cell = PileCell,
        Openable = new SessionOpenableBaseline(OpenableKind.Container, 0, 0, 0, true),
    });

    [TestMethod]
    public void A_map_pile_closed_with_one_item_left_runs_the_items_script_through_the_standing_host_or_is_refused_without_one()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionFailure.None, owner.LootItem(owner.Version, "pile", "bone", Accept).Failure);
        var before = owner.Version;
        var refused = owner.CloseMapJunkPile(owner.Version, MapPile());
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, before), (refused.Disposition, refused.Failure, owner.Version));
        Assert.AreEqual(SessionObjectCondition.Whole, owner.QueryObject(MapPile()).Condition, "the pile stays");
        var host = Hosted(owner, "rag");
        var lines = Standing(owner, host);
        var closed = owner.CloseMapJunkPile(owner.Version, MapPile());
        Assert.AreEqual(GameSessionDisposition.Changed, closed.Disposition, closed.Failure.ToString());
        CollectionAssert.AreEqual(new[] { $"line 2 of {RemoveScript}" }, lines);
        Assert.IsTrue(ReadFlag(owner, 3) == true);
        Assert.AreEqual(SessionObjectCondition.Destroyed, owner.QueryObject(MapPile()).Condition);
        Assert.AreEqual(("rag", PileCell), owner.QueryMapInventory(Exterior)!.Ground.Select(g => (g.Item.Entity, g.Cell)).Single(g => g.Item1 == "rag"));
    }

    [TestMethod]
    public void An_npcs_pickup_from_a_map_pile_of_two_runs_the_other_items_script_and_goes_on_without_a_host()
    {
        using var owner = Owner();
        var host = Hosted(owner, "rag");
        var picked = owner.PickUpItemByNpc(owner.Version, "npc", "bone", Accept, MapPile(), host);
        Assert.AreEqual(GameSessionDisposition.Changed, picked.Disposition, picked.Failure.ToString());
        Assert.AreEqual(SessionObjectCondition.Destroyed, owner.QueryObject(MapPile()).Condition);
        Assert.IsTrue(ReadFlag(owner, 3) == true, "the rag's removal ran with the pile as the triggerer");

        using var bare = Owner();
        var done = bare.PickUpItemByNpc(bare.Version, "npc", "bone", Accept, MapPile());
        Assert.AreEqual(GameSessionDisposition.Changed, done.Disposition, "a pickup cannot be taken back: the pile goes without the script");
        Assert.AreEqual(SessionObjectCondition.Destroyed, bare.QueryObject(MapPile()).Condition);
        Assert.IsFalse(ReadFlag(bare, 3) == true);
    }
}

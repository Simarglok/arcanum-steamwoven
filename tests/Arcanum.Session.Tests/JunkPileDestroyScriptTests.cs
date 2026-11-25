using Arcanum.Data.Scripts;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-3266 (ledger IE3, JP4, TQ20; exe read 0x43CCA0 → 0x466E00 → 0x467860, 0x463540): an item destroyed inside a junk pile (the Item Decay event or any other destroyer) is unlinked from the pile first, so its remove-item script (28) runs with the
// pile as the triggerer, after the pile's dissolve check (the other item's own script ran there) and with the pile possibly destroyed. Synthetic seeds, invented script numbers.
[TestClass]
public sealed class JunkPileDestroyScriptTests
{
    private const int AppleScript = 702, CupScript = 703;
    private static readonly CellCoord There = new(120, 210);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private sealed class World(RemoveItemScriptTests.Host? host) : ISessionLootWindowWorld
    {
        public bool LootWindowShowsContainer => false;
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
        public ISessionItemScriptHost? ItemScriptHost => host;
    }

    private static SessionMapInventorySeed Seed() => new(
        [new("npc", SessionHolderKind.Npc, false, false, [], [])],
        [
            new SessionSeedItem("apple", Facts(SessionItemType.Food, 3600, scripts: SessionScriptPoint.RemoveItem), new CellCoord(90, 200)),
            new SessionSeedItem("cup", Facts(SessionItemType.Generic, 5650, scripts: SessionScriptPoint.RemoveItem), new CellCoord(91, 200)),
            new SessionSeedItem("rag", Facts(SessionItemType.Generic, 5651), new CellCoord(92, 200)),
        ]);

    // A pile on the cell made of the apple, then the cup (and the rag when asked), dropped in that order; the apple decays first.
    private static GameSessionOwner PileOwner(bool rag = false)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HeroStateTests.HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        foreach (var item in rag ? new[] { "apple", "cup", "rag" } : ["apple", "cup"])
        {
            Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, item, Accept).Disposition);
            owner.PlaceHeroItem(item, There);
        }
        Assert.AreEqual(rag ? 3 : 2, owner.QueryJunkPiles().Single().Items.Count);
        return owner;
    }

    private static RemoveItemScriptTests.Host Hosted(GameSessionOwner owner)
    {
        var host = new RemoveItemScriptTests.Host(owner);
        host.Slots[("apple", SessionScriptPoint.RemoveItem)] = AppleScript;
        host.Slots[("cup", SessionScriptPoint.RemoveItem)] = CupScript;
        host.Programs[AppleScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.PrintLine, (Number, 2), (Number, 0)), C(ScrCatalogue.Action.ReturnSkipDefault));
        host.Programs[CupScript] = Program(C(ScrCatalogue.Action.PrintLine, (Number, 3), (Number, 0)), C(ScrCatalogue.Action.ReturnSkipDefault));
        return host;
    }

    private static List<string> Lines(GameSessionOwner owner)
    {
        var lines = new List<string>();
        owner.ItemRunOutput = (messages, _) => lines.AddRange(messages.Select(message => message.Text));
        return lines;
    }

    private static void Advance(GameSessionOwner owner, long ms) => Assert.AreEqual(ClockMutationDisposition.Changed,
        owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);

    [TestMethod]
    public void A_decaying_scripted_item_in_a_pile_of_two_runs_its_script_after_the_dissolve_released_the_other_one()
    {
        using var owner = PileOwner();
        var host = Hosted(owner);
        var lines = Lines(owner);
        Advance(owner, ItemDecayRules.DelayMs);
        var fired = owner.ProcessEvents(new World(host)).Decays.First();
        Assert.AreEqual(("apple", ItemDecayStep.Destroy), (fired.Entity, fired.Step));
        Assert.AreEqual(0, owner.QueryJunkPiles().Count, "left with the cup, the pile dissolved");
        // The dissolve ran the cup's script first (0x463540), the destroyed apple's runs after it (0x467860), with the pile as the triggerer: a pile is no critter, so the plain line variant.
        CollectionAssert.AreEqual(new[] { $"line 3 of {CupScript}", $"line 2 of {AppleScript}" }, lines);
        Assert.IsTrue(ReadFlag(owner, 3) == true, "the apple's run was applied");
        Assert.IsTrue(owner.QueryMapInventory(Exterior)!.Ground.Any(g => g.Item.Entity == "cup" && g.Cell == There), "the cup is dropped again on the pile's cell");
    }

    [TestMethod]
    public void Every_scripted_item_of_a_pile_that_decays_in_one_pass_runs_its_script_as_it_is_destroyed()
    {
        using var owner = PileOwner(rag: true);
        var host = Hosted(owner);
        var lines = Lines(owner);
        Advance(owner, ItemDecayRules.DelayMs);
        var fired = owner.ProcessEvents(new World(host)).Decays.Select(decay => decay.Entity).ToArray();
        CollectionAssert.AreEqual(new[] { "apple", "cup" }, fired, "the rag the cup's destroy left alone was released and dropped again, with a decay of its own");
        CollectionAssert.AreEqual(new[] { $"line 2 of {AppleScript}", $"line 3 of {CupScript}" }, lines, "each destroyed item runs its own 28 as it leaves the pile");
        Assert.AreEqual(0, owner.QueryJunkPiles().Count);
        Assert.IsTrue(ReadFlag(owner, 3) == true);
    }

    [TestMethod]
    public void A_pass_with_no_host_destroys_the_item_without_the_script()
    {
        using var owner = PileOwner();
        Advance(owner, ItemDecayRules.DelayMs);
        owner.ProcessEvents(new World(null));
        Assert.AreEqual(0, owner.QueryJunkPiles().Count, "an event pass cannot refuse the destroy");
        Assert.IsFalse(ReadFlag(owner, 3) == true);
    }

    [TestMethod]
    public void A_destroyer_outside_a_pass_runs_the_script_through_the_standing_host_and_skips_it_without_one()
    {
        using var owner = PileOwner(rag: true);
        var host = Hosted(owner);
        var lines = Lines(owner);
        owner.StandingItemScripts = () => host;
        var revision = owner.Version.Revision;
        owner.DestroyObject("cup");
        CollectionAssert.AreEqual(new[] { $"line 3 of {CupScript}" }, lines);
        Assert.AreEqual(2, owner.QueryJunkPiles().Single().Items.Count);

        owner.StandingItemScripts = null;
        owner.DestroyObject("apple");
        Assert.AreEqual(1, lines.Count, "no host: the destroy goes on without the script");
        Assert.IsFalse(ReadFlag(owner, 3) == true);
        Assert.AreEqual(revision, owner.Version.Revision, "the destroy takes no revision of its own");
    }

    [TestMethod]
    public void An_item_without_a_remove_item_script_destroyed_in_a_pile_runs_nothing()
    {
        using var owner = PileOwner(rag: true);
        var host = Hosted(owner);
        var lines = Lines(owner);
        owner.StandingItemScripts = () => host;
        owner.DestroyObject("rag");
        Assert.AreEqual(0, lines.Count);
    }
}

using Arcanum.Simulation.Equipment;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-2885 (ledger PI2, VP1, TQ20; exe read 0x467860 → 0x467CB0, 0x43CCA0 → 0x4639E0, 0x463860, 0x463B30): the removals of an NPC's WORN item that the session made without its wield-off script (25): the script destroy's walk (invulnerable
// items), destroy item named, a dead body's inventory walk and the restock's clear now run 25 then 28 per item, the NPC as the triggerer and the wearer (the host's context, NpcWearerScripts or the pass world's NpcWieldScripts); an item the NPC
// does not wear runs no 25. Without a context a worn item with point 25 refuses a destroy as a run the session cannot apply, and a pass counts it as skipped. Scripts, numbers and items are invented.
[TestClass]
public sealed class WornRemovalWieldOffTests
{
    private const int WieldOffScript = 700, RemoveScript = 701, SmithSource = 5;
    private static readonly SessionItemKind GoldPile = new(SessionItemType.Gold, 9056, 3200, 33200);
    private const int HelmName = 2805;

    private static SessionNpcWieldScripts Context(NpcThrowScriptTests.Host host, string npc)
    {
        var fields = AttackStateTests.Npc(material: 0, hpAdjustment: 5000, critterFlags: 0);
        var combat = SessionCritterCombat.From(npc, fields with { Critter = fields.Critter! with { CriticalHitChart = 0 } }).Combat!;
        return new(host, combat.Target, new int[SessionCritterIndexedWrites.StatCount]);
    }

    private static SessionItemFacts Both(SessionItemType type, int name, int location = 0) =>
        Facts(type, name, location, 0, null, SessionScriptPoint.WieldOff, SessionScriptPoint.RemoveItem);

    // smith: the helm (worn, slot 1001, both points, invulnerable), the sack (not worn, both points, invulnerable), cap (worn, no point, invulnerable). npc-1: the same helm as a corpse's item and a plain bag. chest: a container.
    private static GameSessionOwner Owner()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new(
            [
                new("smith", SessionHolderKind.Npc, false, true, [], ["helm", "sack", "cap"]),
                new("npc-1", SessionHolderKind.Npc, false, false, [], ["hat", "bag"]),
                new("npc-2", SessionHolderKind.Npc, false, false, [], ["scarf"]),
            ],
            [
                new("helm", Both(SessionItemType.Armor, HelmName, 1001) with { Invulnerable = true }, null),
                new("sack", Both(SessionItemType.Generic, 5600) with { Invulnerable = true }, null),
                new("cap", Facts(SessionItemType.Armor, 2806, 1002) with { Invulnerable = true }, null),
                new("hat", Both(SessionItemType.Armor, 2807, 1001), null),
                new("bag", Facts(SessionItemType.Generic, 5601), null),
                new("scarf", Both(SessionItemType.Armor, 2808), null),
            ])).Failure);
        return owner;
    }

    private static NpcThrowScriptTests.Host Hosted(GameSessionOwner owner)
    {
        var host = new NpcThrowScriptTests.Host(owner);
        host.Programs[WieldOffScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 7)), C(ScrCatalogue.Action.ReturnRunDefault));
        return host;
    }

    private static string[] Slots(NpcThrowScriptTests.Host host) => host.Asked.Where(line => line.StartsWith("slot ", StringComparison.Ordinal)).ToArray();

    // The wearer is the NPC whose entity the context names; the hook gives it only for that NPC.
    private static void Wear(GameSessionOwner owner, NpcThrowScriptTests.Host host, string npc) => owner.NpcWearerScripts = who => who == npc ? Context(host, npc) : null;

    [TestMethod]
    public void A_script_destroys_walk_runs_25_then_28_for_each_worn_item_it_takes_out_and_only_28_for_an_item_not_worn()
    {
        using var owner = Owner();
        var host = Hosted(owner);
        Wear(owner, host, "smith");
        var cell = new CellCoord(101, 202);
        Assert.IsNull(owner.DestroyMapNpcRefusal(Exterior, "smith", cell, host));
        host.Asked.Clear();
        var result = owner.DestroyMapNpc(owner.Version, Exterior, "smith", cell, scripts: host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        CollectionAssert.AreEqual(new[] { $"slot sack {SessionScriptPoint.RemoveItem}", $"slot helm {SessionScriptPoint.WieldOff}", $"slot helm {SessionScriptPoint.RemoveItem}" }, Slots(host),
            "the walk goes last item first; the sack is not worn (no 25), the cap has no point, the helm runs 25 then 28");
        Assert.AreEqual((true, true), (ReadFlag(owner, 3), ReadFlag(owner, 7)), "the runs' writes are applied with the destroy");
        Assert.IsNotNull(owner.QueryMapChange(Exterior, MapChangeKey.ForEntity("helm", GameSessionOwner.ItemDroppedProperty)));
    }

    [TestMethod]
    public void A_worn_item_with_point_25_and_no_context_for_the_npc_refuses_the_destroy_whole()
    {
        foreach (var give in new Func<NpcThrowScriptTests.Host, Func<string, SessionNpcWieldScripts?>?>[] { _ => null, _ => _ => null, _ => _ => throw new InvalidOperationException("a host refusal") })
        {
            using var owner = Owner();
            var host = Hosted(owner);
            owner.NpcWearerScripts = give(host);
            var cell = new CellCoord(101, 202);
            Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.DestroyMapNpcRefusal(Exterior, "smith", cell, host));
            var refused = owner.DestroyMapNpc(owner.Version, Exterior, "smith", cell, scripts: host);
            Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, 0L), (refused.Disposition, refused.Failure, owner.Version.Revision));
            Assert.IsNull(owner.QueryMapChange(Exterior, MapChangeKey.ForEntity("smith", GameSessionOwner.ObjectDestroyedProperty)), "nothing is destroyed on a guess");
        }
    }

    [TestMethod]
    public void An_item_not_worn_asks_no_context_for_its_wield_off_point()
    {
        using var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new(
            [new("smith", SessionHolderKind.Npc, false, true, [], ["sack"])], [new("sack", Both(SessionItemType.Generic, 5600) with { Invulnerable = true }, null)])).Failure);
        var host = Hosted(owner);
        owner.NpcWearerScripts = _ => throw new AssertFailedException("nothing is worn: no context needed");
        var result = owner.DestroyMapNpc(owner.Version, Exterior, "smith", new CellCoord(101, 202), scripts: host);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        CollectionAssert.AreEqual(new[] { $"slot sack {SessionScriptPoint.RemoveItem}" }, Slots(host));
    }

    [TestMethod]
    public void Destroy_item_named_of_an_npcs_worn_item_runs_25_then_28_and_without_a_context_is_refused()
    {
        using var owner = Owner();
        var host = Hosted(owner);
        // Without a host the worn item with point 25 stays refused, as before.
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.DestroyNamedItem(owner.Version, "smith", HelmName).Failure);
        // With a host but no context for the NPC the run cannot be applied.
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.DestroyNamedItem(owner.Version, "smith", HelmName, host).Failure);
        Assert.IsFalse(owner.IsItemDestroyed("helm"));
        Wear(owner, host, "smith");
        host.Asked.Clear();
        var done = owner.DestroyNamedItem(owner.Version, "smith", HelmName, host);
        Assert.AreEqual((GameSessionDisposition.Changed, "helm"), (done.Disposition, done.Item));
        // The plan's dry run asks first, then the destroy's own run: 25 then 28 each time.
        var wield = $"slot helm {SessionScriptPoint.WieldOff}";
        var remove = $"slot helm {SessionScriptPoint.RemoveItem}";
        CollectionAssert.AreEqual(new[] { wield, remove, wield, remove }, Slots(host));
        Assert.AreEqual((true, true, true), (ReadFlag(owner, 3), ReadFlag(owner, 7), owner.IsItemDestroyed("helm")));
    }

    // The pass world: a restock host for the smith and a body host with its own wearer context.
    private sealed class World(NpcThrowScriptTests.Host? host, bool context) : ISessionRestockWorld, ISessionBodyWorld
    {
        public ISessionItemScriptHost? ItemScriptHost => host;
        public SessionNpcWieldScripts? NpcWieldScripts(SessionMapId map, string npc) => context && host is not null ? Context(host, npc) : null;
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => new() { HitPointPoints = 10, FatiguePoints = 10 };
        public CellCoord? BodyCell(SessionMapId map, string entity) => new CellCoord(101, 202);
        public RestockHolder? RestockFacts(SessionMapId map, string entity) => entity == "smith" ? new(RestockHolderKind.Npc, SmithSource, false, false, 0, false) : null;
        public string? RestockMerchant(SessionMapId map, string container) => null;
        public bool Persistent(SessionMapId map, string item) => false;
        public SessionRestockSet RestockSet(int source) => new(SessionRestockSetStatus.Read, new(0, 0, []));
        public SessionRestockItem? RestockItem(int basicPrototype) => null;
        public SessionItemKind RestockPileKind => GoldPile;
        public SessionInsertCheck RestockInsertCheck(SessionMapId map, string holder) => _ => SessionInsertVerdict.Accepted;
    }

    private static void Advance(GameSessionOwner owner, long ms) => Assert.AreEqual(ClockMutationDisposition.Changed,
        owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);

    private static SessionRestockRegeneration Restock(GameSessionOwner owner, ISessionRestockWorld world)
    {
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleRestock(owner.Version, "smith", hasSource: true).Disposition);
        Advance(owner, RestockRules.MaxDelayMs);
        return owner.ProcessEvents(world).Restocks.Single().Regenerations.Single();
    }

    [TestMethod]
    public void The_restock_clear_runs_25_then_28_for_each_worn_item_with_the_pass_worlds_context_for_the_npc()
    {
        using var owner = Owner();
        var host = Hosted(owner);
        var done = Restock(owner, new World(host, context: true));
        Assert.AreEqual(3, done.Removed);
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.WieldOff, "helm"), (SessionScriptPoint.RemoveItem, "helm"), (SessionScriptPoint.RemoveItem, "sack") },
            done.Removals.Scripts.Select(run => (run.Point, run.Attachee)).ToArray(), "the clear walks first to last, 25 then 28 per item, the sack not worn");
        Assert.AreEqual(0, done.Removals.Skipped);
        Assert.AreEqual((true, true, true), (ReadFlag(owner, 3), ReadFlag(owner, 7), owner.IsItemDestroyed("helm")));
    }

    [TestMethod]
    public void The_restock_clear_without_a_context_for_a_worn_wield_off_counts_it_skipped_runs_the_remove_item_script_alone_and_deletes_the_item()
    {
        using var owner = Owner();
        var host = Hosted(owner);
        var done = Restock(owner, new World(host, context: false));
        Assert.AreEqual(3, done.Removed);
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.RemoveItem, "helm"), (SessionScriptPoint.RemoveItem, "sack") }, done.Removals.Scripts.Select(run => (run.Point, run.Attachee)).ToArray(), "the helm's 28 still runs alone");
        Assert.AreEqual(1, done.Removals.Skipped, "the helm's 25 needs the wearer");
        Assert.IsTrue(owner.IsItemDestroyed("helm"));
    }

    [TestMethod]
    public void The_restock_clear_falls_back_to_the_session_hook_for_the_context()
    {
        using var owner = Owner();
        var host = Hosted(owner);
        Wear(owner, host, "smith");
        var done = Restock(owner, new World(host, context: false));
        Assert.AreEqual(0, done.Removals.Skipped);
        CollectionAssert.AreEqual(new[] { SessionScriptPoint.WieldOff, SessionScriptPoint.RemoveItem, SessionScriptPoint.RemoveItem }, done.Removals.Scripts.Select(run => run.Point).ToArray());
    }

    private static void Kill(GameSessionOwner owner, string npc)
    {
        owner.Kill(owner.Version, SessionLifeTarget.Critter(npc, new() { HitPointPoints = 10, FatiguePoints = 10 }, LifeState.Unharmed));
        Advance(owner, BloodPoolRules.CritterDecayDelayMs);
    }

    [TestMethod]
    public void A_bodys_walk_runs_25_then_28_for_the_item_the_corpse_wore_and_28_only_for_the_one_it_did_not()
    {
        using var owner = Owner();
        var host = Hosted(owner);
        Kill(owner, "npc-1");
        var fired = owner.ProcessEvents(new World(host, context: true)).BodyDecays.Single();
        Assert.AreEqual(SessionBodyDecayOutcome.Removed, fired.Outcome);
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.WieldOff, "hat"), (SessionScriptPoint.RemoveItem, "hat") }, fired.Removals.Scripts.Select(run => (run.Point, run.Attachee)).ToArray(), "the bag has no point; the hat is worn");
        Assert.AreEqual(0, fired.Removals.Skipped);
        Assert.AreEqual((true, true), (ReadFlag(owner, 3), ReadFlag(owner, 7)));
    }

    [TestMethod]
    public void A_bodys_item_with_point_25_that_the_corpse_did_not_wear_runs_no_25_and_asks_no_context()
    {
        using var owner = Owner();
        var host = Hosted(owner);
        Kill(owner, "npc-2");
        var fired = owner.ProcessEvents(new World(host, context: false)).BodyDecays.Single();
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.RemoveItem, "scarf") }, fired.Removals.Scripts.Select(run => (run.Point, run.Attachee)).ToArray());
        Assert.AreEqual(0, fired.Removals.Skipped);
    }

    [TestMethod]
    public void A_bodys_walk_without_a_context_skips_the_worn_items_25_runs_its_28_alone_and_still_drops_the_item()
    {
        using var owner = Owner();
        var host = Hosted(owner);
        Kill(owner, "npc-1");
        var fired = owner.ProcessEvents(new World(host, context: false)).BodyDecays.Single();
        Assert.AreEqual((SessionBodyDecayOutcome.Removed, 1, 1), (fired.Outcome, fired.Removals.Scripts.Count, fired.Removals.Skipped), "the hat's 25 is skipped, its 28 runs alone");
        Assert.AreEqual((false, true), (ReadFlag(owner, 3), ReadFlag(owner, 7)));
    }

    [TestMethod]
    public void A_pass_through_a_cast_host_gives_the_bodys_walk_the_wrapped_worlds_context()
    {
        using var owner = Owner();
        var host = Hosted(owner);
        Kill(owner, "npc-1");
        var caster = new SessionSpellCaster(owner, _ => null, new RestockStateTests.NoMap(), new World(host, context: true));
        var fired = caster.ProcessEvents().BodyDecays.Single();
        Assert.AreEqual((SessionBodyDecayOutcome.Removed, 2, 0), (fired.Outcome, fired.Removals.Scripts.Count, fired.Removals.Skipped), "the wrapper forwards the wearer context");
    }

    // The break's own write (ItemBreakStep.Broken) is the durability record this marks; a session test has no other door to it from outside.
    private static void MarkBroken(GameSessionOwner owner, string item)
    {
        var durability = typeof(GameSessionOwner).GetField("_durability", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(owner)!;
        var records = (SortedDictionary<string, ItemCondition>)durability.GetType().GetProperty("Records", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(durability)!;
        records[item] = new ItemCondition(15, 0, 0, false) with { Broken = true };
    }

    [TestMethod]
    public void A_broken_body_armour_the_break_took_off_runs_no_25_again()
    {
        using var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new(
            [new("smith", SessionHolderKind.Npc, false, true, [], ["plate"])], [new("plate", Both(SessionItemType.Armor, 2809, 1006) with { Invulnerable = true }, null)])).Failure);
        MarkBroken(owner, "plate");
        var host = Hosted(owner);
        owner.NpcWearerScripts = _ => throw new AssertFailedException("the broken armour is no longer worn: no 25, no context");
        var result = owner.DestroyMapNpc(owner.Version, Exterior, "smith", new CellCoord(101, 202), scripts: host);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        CollectionAssert.AreEqual(new[] { $"slot plate {SessionScriptPoint.RemoveItem}" }, Slots(host));
    }

    // The session's own record of what a dressed NPC wears (ARC-WORLD-103), set directly: a session test has no other door to it from outside.
    private static SortedDictionary<EquipmentSlot, string> Dress(GameSessionOwner owner, string npc)
    {
        var inventory = typeof(GameSessionOwner).GetField("_inventory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(owner)!;
        var worn = (SortedDictionary<string, SortedDictionary<EquipmentSlot, string>>)inventory.GetType().GetProperty("NpcWorn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(inventory)!;
        return worn[npc] = [];
    }

    [TestMethod]
    public void A_dressed_npcs_record_decides_what_it_wears_whatever_its_map_data_says()
    {
        using var owner = Owner();
        var host = Hosted(owner);
        // The record says the NPC wears nothing: the map data's helm is in the pack, so no 25 and no context asked; a made-up record that names the sack as worn makes the sack run 25.
        var record = Dress(owner, "smith");
        owner.NpcWearerScripts = _ => throw new AssertFailedException("nothing the NPC wears has a wield-off point");
        var dry = owner.DestroyMapNpcRefusal(Exterior, "smith", new CellCoord(101, 202), host);
        Assert.IsNull(dry);
        record[EquipmentSlot.Armor] = "sack";
        Wear(owner, host, "smith");
        host.Asked.Clear();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyMapNpc(owner.Version, Exterior, "smith", new CellCoord(101, 202), scripts: host).Disposition);
        CollectionAssert.AreEqual(new[] { $"slot sack {SessionScriptPoint.WieldOff}", $"slot sack {SessionScriptPoint.RemoveItem}", $"slot helm {SessionScriptPoint.RemoveItem}" }, Slots(host),
            "the walk goes last item first: the sack (now worn: 25 then 28), then the helm (in the pack: 28 only)");
    }

    [TestMethod]
    public void A_28_only_item_shares_the_run_set_with_a_worn_25_item()
    {
        using var owner = Owner();
        var host = Hosted(owner);
        Wear(owner, host, "smith");
        host.Asked.Clear();
        var result = owner.DestroyMapNpc(owner.Version, Exterior, "smith", new CellCoord(101, 202), scripts: host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        CollectionAssert.AreEqual(new[] { $"slot sack {SessionScriptPoint.RemoveItem}", $"slot helm {SessionScriptPoint.WieldOff}", $"slot helm {SessionScriptPoint.RemoveItem}" }, Slots(host));
        Assert.AreEqual((true, true), (ReadFlag(owner, 3), ReadFlag(owner, 7)), "the sack's 28 and the helm's 25 and 28 all applied in one run set");
    }
}

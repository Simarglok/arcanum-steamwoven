using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.DestroyRemoveScriptTests;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-2903 (ledger IE3, DD4, DD10; exe read 0x43CCA0 → 0x4639E0 → 0x467860): the invulnerable items a created critter's destroy (a summon's expiry or dispel, the Destroy component, a script's undo) takes out and drops run
// their remove-item script (point 28) with the dying critter as the triggerer, result ignored, as for a map NPC (ARC-SCRIPT-2880). Policy: a run the session cannot apply, or an item with the point and no host, refuses the destroy whole.
// Invented prototype numbers, script numbers, maps and cells.
[TestClass]
public sealed class CreatedCritterDestroyRemoveScriptTests
{
    private const int Prototype = 28359, RemoveScript = 705;
    private static readonly CellCoord Spot = new(101, 200);

    private sealed class OpenMap : IDropMap
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public CellCoord? Step(CellCoord from, int rotation) => rotation == 8 ? from : new(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y);
        public DropPath Path(CellCoord from, CellCoord to, DropOwner who)
        {
            var steps = new List<int>();
            for (var at = from; at != to;)
            {
                var rotation = Array.IndexOf(Offsets, (Math.Sign(to.X - at.X), Math.Sign(to.Y - at.Y)));
                steps.Add(rotation);
                at = Step(at, rotation)!.Value;
            }
            return new(steps.Count, 0, steps);
        }
        public bool HasCritter(CellCoord cell) => false;
    }

    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    // Map items the hero picks up and gives to the created critters: each pair is the critter's holder entity and its items. "relic" and "amulet" carry the flag and a point 28, "glass" the flag only, "bread" the point only.
    private static (GameSessionOwner Owner, SessionCreatedObject[] Critters, Host Host) With(int critters = 1)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HeroStateTests.HalfElf() }).Owner!;
        var plain = InventoryStateTests.Facts(SessionItemType.Generic, 5600);
        var scripted = plain with { ScriptPoints = [SessionScriptPoint.RemoveItem] };
        string[] names = ["bread", "relic", "amulet", "glass"];
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new([], [
            new SessionSeedItem("bread", scripted, Spot), new SessionSeedItem("relic", scripted with { Invulnerable = true }, Spot), new SessionSeedItem("amulet", scripted with { Invulnerable = true }, Spot),
            new SessionSeedItem("glass", plain with { Invulnerable = true }, Spot)])).Failure);
        var made = new List<SessionCreatedObject>();
        for (var i = 0; i < critters; i++) made.Add(owner.CreateCritterNear(owner.Version, Prototype, Exterior, new(100 + 3 * i, 201), new OpenMap()).Object!);
        // The scripts are quiet while the hero gives the items away (the transfer runs the removal from the hero's list too); the test sets the script before the destroy.
        var host = new Host(owner, "bread", "relic", "amulet");
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.ReturnSkipDefault));
        foreach (var entity in names) Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, entity, Accept).Disposition, entity);
        for (var i = 0; i < critters; i++)
        {
            var mine = critters == 1 ? names : i == 0 ? ["bread", "relic"] : ["amulet", "glass"];
            foreach (var entity in mine) Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, made[i].Entity, entity, Accept, scripts: host).Disposition, entity);
        }
        host.Asked.Clear();
        return (owner, [.. made], host);
    }

    private static void Script(Host host, params (int Type, (byte Kind, int Value)[] Operands)[] actions) => host.Programs[RemoveScript] = Program(actions);

    private static (int Type, (byte Kind, int Value)[] Operands)[] FlagAndPrint() =>
        [C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.PrintLine, (Number, 2), (Number, 0)), C(ScrCatalogue.Action.ReturnSkipDefault)];

    [TestMethod]
    public void A_destroyed_created_critters_dropped_items_run_their_remove_item_script_in_the_walk_order_with_the_critter_as_the_triggerer()
    {
        var (owner, critters, host) = With();
        using var _ = owner;
        Script(host, FlagAndPrint());
        var before = owner.Version.Revision;
        var result = owner.DestroyCreatedObject(owner.Version, critters[0].Entity, scripts: host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, before + 1), (result.Disposition, result.Failure, owner.Version.Revision), "one revision for the runs, the drops and the destroy");
        CollectionAssert.AreEqual(new[] { "amulet", "relic" }, host.RemoveAsks, "the list is walked from its last item: the glass has no point, the bread no flag");
        CollectionAssert.AreEqual(new[] { new SessionScriptMessage(RemoveScript, 2, 0, $"npc line 2 of {RemoveScript}"), new SessionScriptMessage(RemoveScript, 2, 0, $"npc line 2 of {RemoveScript}") }, result.Messages.ToArray(),
            "a critter triggerer reads its own line variant");
        Assert.AreEqual(true, ReadFlag(owner, 3), "the runs' flag write applied with the destroy");
        Assert.IsNull(owner.QueryCreatedObject(critters[0].Entity), "the critter is destroyed");
        CollectionAssert.AreEquivalent(new[] { "relic", "amulet", "glass" }, owner.QueryPlacedItems().Select(placed => placed.Entity).ToArray(), "the invulnerable items lie on the ground");
        Assert.IsFalse(owner.IsItemDestroyed("relic"), "the script's answer (skip default) is ignored: the item is dropped, not deleted");
    }

    [TestMethod]
    public void A_list_of_destroyed_created_critters_runs_the_scripts_critter_by_critter_in_one_step()
    {
        var (owner, critters, host) = With(critters: 2);
        using var _ = owner;
        Script(host, FlagAndPrint());
        var before = owner.Version.Revision;
        var result = owner.DestroyCreatedObjects(owner.Version, [critters[0].Entity, critters[1].Entity], host);
        Assert.AreEqual((GameSessionDisposition.Changed, before + 1, 2, 2), (result.Disposition, owner.Version.Revision, result.Objects.Count, result.Messages.Count), "one revision; the relic and the amulet (flagged and scripted) print one line each, the bread is not flagged and the glass has no point");
        CollectionAssert.AreEqual(new[] { "relic", "amulet" }, host.RemoveAsks, "each critter's walk in the order of the list: the first has the bread and the relic, the second the amulet and the glass");
        Assert.AreEqual(true, ReadFlag(owner, 3));
        Assert.IsNull(owner.QueryCreatedObject(critters[0].Entity));
        Assert.IsNull(owner.QueryCreatedObject(critters[1].Entity));
    }

    [TestMethod]
    public void Without_a_script_host_an_invulnerable_item_with_a_remove_item_script_refuses_the_destroy_whole()
    {
        var (owner, critters, host) = With();
        using var _ = owner;
        Script(host, FlagAndPrint());
        var before = owner.Version.Revision;
        var one = owner.DestroyCreatedObject(owner.Version, critters[0].Entity);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, before), (one.Disposition, one.Failure, owner.Version.Revision));
        var many = owner.DestroyCreatedObjects(owner.Version, [critters[0].Entity]);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, before), (many.Disposition, many.Failure, owner.Version.Revision));
        Assert.IsNotNull(owner.QueryCreatedObject(critters[0].Entity), "the summon stands until the refusal clears");
        Assert.AreEqual(0, owner.QueryPlacedItems().Count, "nothing is dropped with the script skipped");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyCreatedObject(owner.Version, critters[0].Entity, scripts: host).Disposition, "with the host the same destroy goes through");
    }

    [TestMethod]
    public void A_remove_item_run_the_session_cannot_apply_refuses_the_destroy_before_any_effect()
    {
        var (owner, critters, host) = With();
        using var _ = owner;
        Script(host, C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.AddEffect, ((byte)0, 0), (Number, 50), (Number, 5)), C(ScrCatalogue.Action.ReturnRunDefault));
        var before = owner.Version.Revision;
        var refused = owner.DestroyCreatedObject(owner.Version, critters[0].Entity, scripts: host);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, before, (bool?)false), (refused.Disposition, refused.Failure, owner.Version.Revision, ReadFlag(owner, 3)), "no flag written");
        Assert.IsNotNull(owner.QueryCreatedObject(critters[0].Entity));
        Assert.AreEqual(0, owner.QueryPlacedItems().Count);
        var list = owner.DestroyCreatedObjects(owner.Version, [critters[0].Entity], host);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, before), (list.Disposition, list.Failure, owner.Version.Revision));
    }

    [TestMethod]
    public void Items_without_the_flag_or_the_point_need_no_script_host_and_run_nothing()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HeroStateTests.HalfElf() }).Owner!;
        using var _ = owner;
        var plain = InventoryStateTests.Facts(SessionItemType.Generic, 5600);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new([], [
            new SessionSeedItem("bread", plain with { ScriptPoints = [SessionScriptPoint.RemoveItem] }, Spot), new SessionSeedItem("glass", plain with { Invulnerable = true }, Spot)])).Failure);
        var critter = owner.CreateCritterNear(owner.Version, Prototype, Exterior, new(100, 201), new OpenMap()).Object!;
        var host = new Host(owner, "bread");
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.ReturnSkipDefault));
        foreach (var entity in new[] { "bread", "glass" })
        {
            Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, entity, Accept).Disposition, entity);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, critter.Entity, entity, Accept, scripts: host).Disposition, entity);
        }
        host.Asked.Clear();
        var result = owner.DestroyCreatedObject(owner.Version, critter.Entity);
        Assert.AreEqual((GameSessionDisposition.Changed, 0, 0), (result.Disposition, result.Messages.Count, host.RemoveAsks.Length));
        Assert.AreEqual("glass", owner.QueryPlacedItems().Single().Entity);
    }

    [TestMethod]
    public void A_critter_with_no_dropped_items_and_the_destruction_of_a_destroyed_critter_run_nothing()
    {
        var (owner, critters, host) = With();
        using var _ = owner;
        Script(host, FlagAndPrint());
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyCreatedObject(owner.Version, critters[0].Entity, scripts: host).Disposition);
        host.Asked.Clear();
        var again = owner.DestroyCreatedObject(owner.Version, critters[0].Entity, scripts: host);
        Assert.AreEqual((GameSessionDisposition.NoChange, 0, 0), (again.Disposition, again.Messages.Count, host.RemoveAsks.Length));
    }

    [TestMethod]
    public void A_list_whose_runs_cannot_be_applied_destroys_none_of_its_critters_and_commits_nothing()
    {
        var (owner, critters, host) = With(critters: 2);
        using var _ = owner;
        Script(host, C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.AddEffect, ((byte)0, 0), (Number, 50), (Number, 5)), C(ScrCatalogue.Action.ReturnRunDefault));
        var before = owner.Version.Revision;
        var refused = owner.DestroyCreatedObjects(owner.Version, [critters[0].Entity, critters[1].Entity], host);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, before, (bool?)false), (refused.Disposition, refused.Failure, owner.Version.Revision, ReadFlag(owner, 3)));
        Assert.IsTrue(critters.All(critter => owner.QueryCreatedObject(critter.Entity) is not null), "both critters stand");
        Assert.AreEqual(0, owner.QueryPlacedItems().Count, "nothing is dropped");
    }

    [TestMethod]
    public void A_critter_of_a_map_other_than_the_shown_one_with_a_scripted_item_refuses_and_one_without_is_destroyed()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HeroStateTests.HalfElf() }).Owner!;
        using var _ = owner;
        var other = new SessionMapId(Exterior.RuntimeMap + 1);
        var plain = InventoryStateTests.Facts(SessionItemType.Generic, 5600);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new([], [
            new SessionSeedItem("relic", plain with { ScriptPoints = [SessionScriptPoint.RemoveItem], Invulnerable = true }, Spot), new SessionSeedItem("glass", plain with { Invulnerable = true }, Spot)])).Failure);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(other, new([], [])).Failure);
        var host = new Host(owner, "relic");
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.ReturnSkipDefault));
        var scripted = owner.CreateCritterNear(owner.Version, Prototype, other, new(100, 201), new OpenMap()).Object!;
        var quiet = owner.CreateCritterNear(owner.Version, Prototype, other, new(110, 201), new OpenMap()).Object!;
        foreach (var (critter, item) in new[] { (scripted, "relic"), (quiet, "glass") })
        {
            Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, item, Accept).Disposition, item);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, critter.Entity, item, Accept, scripts: host).Disposition, item);
        }
        host.Asked.Clear();
        var refused = owner.DestroyCreatedObject(owner.Version, scripted.Entity, scripts: host);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, 0), (refused.Disposition, refused.Failure, host.RemoveAsks.Length), "the host answers for the shown map alone: no run, no destroy");
        Assert.IsNotNull(owner.QueryCreatedObject(scripted.Entity));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyCreatedObject(owner.Version, quiet.Entity, scripts: host).Disposition, "an item with no point needs no run");
    }
}

using Arcanum.Data.Scripts;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-2854 (ledger IE3, audit TQ20): the removal's remove-item script (point 28, 0x467860) runs for an item the session deletes out of a holder's list: holder as the triggerer, item as the attachee, after the unlink,
// result ignored. Over the synthetic seed of InventoryStateTests (corpse holds torn 5607 with a remove-item point; virgil: ring, sword, vest, ring-copy) and the half-elf hero; script numbers and programs are invented.
[TestClass]
public sealed class RemoveItemScriptTests
{
    private const int RemoveScript = 702;
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private sealed class Host(GameSessionOwner session) : ISessionItemScriptHost
    {
        public Dictionary<(string, int), int> Slots { get; } = [];
        public Dictionary<int, ScrProgram> Programs { get; } = [];
        public List<string> Asked { get; } = [];

        public ScrSlot? Slot(ScrObjectRef attachee, int point)
        {
            Asked.Add($"slot {attachee.Code} {point}");
            return Slots.TryGetValue((attachee.Code, point), out var script) ? new ScrSlot(script, 0, 0) : null;
        }

        public ScrScriptLookup Script(int number) => Programs.TryGetValue(number, out var program) ? ScrScriptLookup.Of(program) : ScrScriptLookup.Missing;
        public bool GlobalFlag(int index) => session.Flags.Read(new GlobalFlagReadRequest(new GlobalFlagRequestId(Guid.NewGuid()), session.FlagVersion, new FlagId(index))).Value ?? false;
        public int StoryState() => throw new InvalidOperationException("not admitted");
        public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
        public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
        public bool IsTrapObject(ScrObjectRef attachee) => false;
        public ScrObjectRef? Player() => new ScrObjectRef(Exterior.RuntimeMap, SessionEventKinds.PlayerEntity);
        public ScrObjectRef? Leader(ScrObjectRef critter) => null;
        public bool IsJilted(ScrObjectRef critter) => false;
        public bool IsPc(ScrObjectRef critter) => critter.Code == SessionEventKinds.PlayerEntity;
        public SessionPrintedLine PrintedLine(int script, int line) => new(SessionPrintedLineStatus.Shown, $"line {line} of {script}", "test");
        public SessionPrintedLine PrintedLineForNpc(int script, int line) => new(SessionPrintedLineStatus.Shown, $"npc line {line} of {script}", "test");
    }

    private static GameSessionOwner Hero()
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        return owner;
    }

    private static Host Hosted(GameSessionOwner owner, params (int Type, (byte Kind, int Value)[] Operands)[] actions)
    {
        var host = new Host(owner);
        host.Slots[("torn", SessionScriptPoint.RemoveItem)] = RemoveScript;
        host.Programs[RemoveScript] = Program(actions);
        return host;
    }

    private static string[] List(GameSessionOwner owner, string holder) => owner.QueryHolderItems(holder)!.Select(item => item.Entity).ToArray();

    [TestMethod]
    public void Destroy_item_named_runs_the_remove_item_script_and_applies_it_with_the_deletion_in_one_revision()
    {
        using var owner = Hero();
        var host = Hosted(owner, C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.PrintLine, (Number, 2), (Number, 0)), C(ScrCatalogue.Action.ReturnSkipDefault));
        var destroyed = owner.DestroyNamedItem(owner.Version, "corpse", 5607, host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "torn", 1L), (destroyed.Disposition, destroyed.Failure, destroyed.Item, owner.Version.Revision));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.RemoveItem, "torn") }, destroyed.Scripts.Select(run => (run.Point, run.Attachee)).ToArray());
        CollectionAssert.AreEqual(new[] { new SessionScriptMessage(RemoveScript, 2, 0, $"npc line 2 of {RemoveScript}") }, destroyed.Messages.ToArray(), "the holder is an NPC: its own line variant");
        Assert.AreEqual(true, ReadFlag(owner, 3), "the run's flag write applied with the deletion");
        Assert.IsTrue(owner.IsItemDestroyed("torn"), "the result of the script (skip default) is ignored: the item is deleted");
        CollectionAssert.AreEqual(new[] { "note", "boots", "helmet" }, List(owner, "corpse"));
    }

    [TestMethod]
    public void Without_a_script_host_destroy_item_named_still_refuses_an_item_with_a_remove_item_script()
    {
        using var owner = Hero();
        var refused = owner.DestroyNamedItem(owner.Version, "corpse", 5607);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, 0L), (refused.Disposition, refused.Failure, owner.Version.Revision));
        Assert.IsFalse(owner.IsItemDestroyed("torn"));
    }

    [TestMethod]
    public void The_plan_makes_a_dry_run_and_a_remove_item_run_the_session_cannot_apply_refuses_the_destroy_with_no_effect()
    {
        using var owner = Hero();
        var good = Hosted(owner, C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        var plan = owner.QueryNamedItemDestroy("corpse", 5607, good);
        Assert.IsNull(plan.Refusal);
        Assert.AreEqual((0L, (bool?)false, false), (owner.Version.Revision, ReadFlag(owner, 3), owner.IsItemDestroyed("torn")), "the plan applies nothing");

        var bad = Hosted(owner, C(ScrCatalogue.Action.AddEffect, ((byte)0, 0), (Number, 50), (Number, 5)), C(ScrCatalogue.Action.ReturnRunDefault));
        Assert.IsNotNull(owner.QueryNamedItemDestroy("corpse", 5607, bad).Refusal);
        var refused = owner.DestroyNamedItem(owner.Version, "corpse", 5607, bad);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, 0L), (refused.Disposition, refused.Failure, owner.Version.Revision));
        Assert.IsFalse(owner.IsItemDestroyed("torn"));
        CollectionAssert.AreEqual(new[] { "note", "boots", "torn", "helmet" }, List(owner, "corpse"));
    }

    [TestMethod]
    public void An_item_without_a_remove_item_script_runs_nothing_under_a_host()
    {
        using var owner = Hero();
        var host = Hosted(owner, C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        var destroyed = owner.DestroyNamedItem(owner.Version, "virgil", 2804, host);
        Assert.AreEqual((GameSessionDisposition.Changed, "ring", 0, false), (destroyed.Disposition, destroyed.Item, destroyed.Scripts.Count, ReadFlag(owner, 3) == true));
    }

    [TestMethod]
    public void An_item_an_npc_loses_by_a_script_runs_its_remove_item_script_with_the_npc_as_the_triggerer()
    {
        using var owner = Hero();
        var host = Hosted(owner, C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.PrintLine, (Number, 4), (Number, 0)), C(ScrCatalogue.Action.ReturnRunDefault));
        Assert.IsNull(owner.NpcItemRemovalRefusal("corpse", "torn", host));
        Assert.AreEqual((0L, (bool?)false), (owner.Version.Revision, ReadFlag(owner, 3)), "the dry run applies nothing");
        var removed = owner.RemoveItemFromNpc(owner.Version, "corpse", "torn", host);
        Assert.AreEqual((GameSessionDisposition.Changed, 1L, true), (removed.Disposition, owner.Version.Revision, ReadFlag(owner, 3)));
        CollectionAssert.AreEqual(new[] { new SessionScriptMessage(RemoveScript, 4, 0, $"npc line 4 of {RemoveScript}") }, removed.Messages.ToArray(), "an NPC triggerer reads its own line variant");
        Assert.IsTrue(owner.IsItemDestroyed("torn"));
    }

    [TestMethod]
    public void The_remove_item_script_of_an_item_the_hero_holds_runs_with_the_hero_as_the_triggerer()
    {
        using var owner = Hero();
        // The hero takes torn (its own remove-item script runs with the take); then destroys it by name.
        var host = Hosted(owner, C(ScrCatalogue.Action.PrintLine, (Number, 5), (Number, 0)), C(ScrCatalogue.Action.ReturnRunDefault));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LootItem(owner.Version, "corpse", "torn", Accept, host).Disposition);
        var revision = owner.Version.Revision;
        var destroyed = owner.DestroyNamedItem(owner.Version, null, 5607, host);
        Assert.AreEqual((GameSessionDisposition.Changed, "torn", revision + 1), (destroyed.Disposition, destroyed.Item, owner.Version.Revision));
        CollectionAssert.AreEqual(new[] { new SessionScriptMessage(RemoveScript, 5, 0, $"line 5 of {RemoveScript}") }, destroyed.Messages.ToArray(), "the PC reads the plain line variant");
        Assert.IsTrue(owner.IsItemDestroyed("torn"));
    }
}

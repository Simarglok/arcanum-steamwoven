using Arcanum.Data.Scripts;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-041: the get, transfer, remove-item and insert-item events of a session transfer, in the original's order (ledger
// IE1–IE4). Seed of InventoryStateTests ("charm" lies on the ground with a get script, the corpse holds "torn" with a remove-item
// script); script numbers, programs and texts are invented.
[TestClass]
public sealed class ItemEventScriptTests
{
    private const int GetScript = 600, TransferScript = 601, RemoveScript = 602, InsertScript = 603;

    /// <summary>A host with slots per (entity, point) and the session's flags; it records every question in order.</summary>
    private sealed class Host : ISessionItemScriptHost
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
        public bool GlobalFlag(int index) => throw new InvalidOperationException("the session reads flags itself");
        public int StoryState() => throw new InvalidOperationException("not admitted");
        public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
        public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
        public bool IsTrapObject(ScrObjectRef attachee) => false;
        public ScrObjectRef? Player() => new ScrObjectRef(Exterior.RuntimeMap, SessionEventKinds.PlayerEntity);
        public ScrObjectRef? Leader(ScrObjectRef critter) => null;
        public bool IsJilted(ScrObjectRef critter) => false;
        public bool IsPc(ScrObjectRef critter) => critter.Code == SessionEventKinds.PlayerEntity;

        public SessionPrintedLine PrintedLine(int script, int line)
        {
            Asked.Add($"line {script} {line}");
            return new(SessionPrintedLineStatus.Shown, $"line {line} of {script}", "test");
        }
    }

    private static string[] Pc(GameSessionOwner owner) => owner.QueryPcInventory().Select(item => item.Entity).ToArray();
    private static (int, string)[] Runs(InventoryMutationResult result) => result.Scripts.Select(run => (run.Point, run.Attachee)).ToArray();

    // "torn" also carries an insert-item script here, so one loot runs the transfer, remove-item and insert-item events.
    private static GameSessionOwner Seeded()
    {
        var owner = Create();
        var seed = InventoryStateTests.Seed();
        var items = seed.Items.Select(item => item.Entity == "torn"
            ? item with { Facts = item.Facts with { ScriptPoints = [SessionScriptPoint.RemoveItem, SessionScriptPoint.InsertItem] } } : item).ToArray();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, seed with { Items = items }).Disposition);
        return owner;
    }

    [TestMethod]
    public void The_get_script_runs_before_the_insert_check_and_its_flags_apply_with_the_pickup()
    {
        using var owner = Seeded();
        // Without a host the item is still refused.
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.PickUpItem(owner.Version, "charm", _ => SessionInsertVerdict.Accepted).Failure);
        var host = new Host();
        host.Slots[("charm", SessionScriptPoint.Get)] = GetScript;
        host.Programs[GetScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        var checks = new List<string>();
        var picked = owner.PickUpItem(owner.Version, "charm", item => { checks.Add(string.Join(",", host.Asked)); return SessionInsertVerdict.Accepted; }, host);
        Assert.AreEqual((GameSessionDisposition.Changed, "charm", (int?)null, 1L), (picked.Disposition, picked.Item, picked.DeclinedBy, owner.Version.Revision));
        CollectionAssert.AreEqual(new[] { $"slot charm {SessionScriptPoint.Get}" }, checks, "the get script ran before the insert check");
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.Get, "charm") }, Runs(picked));
        Assert.AreEqual(true, ReadFlag(owner, 3));
        CollectionAssert.AreEqual(new[] { "charm" }, Pc(owner));
    }

    [TestMethod]
    public void A_get_script_that_skips_the_default_leaves_the_item_but_its_effects_apply()
    {
        using var owner = Seeded();
        var host = new Host();
        host.Slots[("charm", SessionScriptPoint.Get)] = GetScript;
        var checks = 0;
        SessionInsertVerdict Counted(SessionInventoryItem item) { checks++; return SessionInsertVerdict.Accepted; }

        // No effect: nothing changes, not even the revision.
        host.Programs[GetScript] = Program(C(ScrCatalogue.Action.ReturnSkipDefault));
        var quiet = owner.PickUpItem(owner.Version, "charm", Counted, host);
        Assert.AreEqual((GameSessionDisposition.NoChange, GameSessionFailure.None, (string?)null, (int?)SessionScriptPoint.Get, 0L),
            (quiet.Disposition, quiet.Failure, quiet.Item, quiet.DeclinedBy, owner.Version.Revision));

        // A flag write and a printed line still happen, in one revision; the insert check is never asked.
        host.Programs[GetScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.PrintLine, (Number, 2), (Number, 0)),
            C(ScrCatalogue.Action.ReturnSkipDefault));
        var declined = owner.PickUpItem(owner.Version, "charm", Counted, host);
        Assert.AreEqual((GameSessionDisposition.Changed, (string?)null, (int?)SessionScriptPoint.Get, 1L), (declined.Disposition, declined.Item, declined.DeclinedBy, owner.Version.Revision));
        CollectionAssert.AreEqual(new[] { new SessionScriptMessage(GetScript, 2, 0, "line 2 of 600") }, declined.Messages.ToArray());
        Assert.AreEqual((true, 0, 0), (ReadFlag(owner, 3), checks, Pc(owner).Length));
        Assert.IsTrue(owner.QueryMapInventory(Exterior)!.Ground.Any(ground => ground.Item.Entity == "charm"), "the item stays on the ground");

        // A declining run whose effects the session cannot apply refuses with no effect.
        host.Programs[GetScript] = Program(C(ScrCatalogue.Action.SetLocalFlag, (Number, 1)), C(ScrCatalogue.Action.ReturnSkipDefault));
        var refused = owner.PickUpItem(owner.Version, "charm", Counted, host);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, (int?)null, 1L),
            (refused.Disposition, refused.Failure, refused.DeclinedBy, owner.Version.Revision));
    }

    [TestMethod]
    public void Loot_runs_the_pc_transfer_then_remove_item_then_insert_item_and_each_run_sees_the_earlier_writes()
    {
        using var owner = Seeded();
        Assert.AreEqual(Arcanum.Scripting.GlobalFlags.GlobalFlagWriteDisposition.Applied, WriteFlag(owner, 7, false).Disposition);
        // Without a host the remove-item script refuses the item, as before.
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.LootItem(owner.Version, "corpse", "torn", _ => SessionInsertVerdict.Accepted).Failure);
        var host = new Host();
        host.Slots[("torn", SessionScriptPoint.RemoveItem)] = RemoveScript;
        host.Slots[("torn", SessionScriptPoint.InsertItem)] = InsertScript;
        // The remove-item script sets flag 3; the insert-item script copies flag 3 into flag 7, so 7 is set only if it saw the write.
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnSkipDefault));
        host.Programs[InsertScript] = Program(C(ScrCatalogue.Action.Assign, (Flag, 7), (Flag, 3)), C(ScrCatalogue.Action.ReturnSkipDefault));
        var looted = owner.LootItem(owner.Version, "corpse", "torn", _ => SessionInsertVerdict.Accepted, host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "torn"), (looted.Disposition, looted.Failure, looted.Item));
        Assert.AreEqual(1L, owner.Version.Revision, "a flag write outside the session moves no session revision");
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.Transfer, SessionEventKinds.PlayerEntity), (SessionScriptPoint.RemoveItem, "torn"),
            (SessionScriptPoint.InsertItem, "torn") }, Runs(looted), "skip default of the remove-item script is ignored");
        Assert.AreEqual(ScrStop.NoScript, looted.Scripts[0].Run.Stop, "the PC has no transfer script here");
        Assert.AreEqual(looted.Scripts[2].Run, looted.InsertScript);
        Assert.AreEqual((true, true), (ReadFlag(owner, 3), ReadFlag(owner, 7)));
        CollectionAssert.AreEqual(new[] { "torn" }, Pc(owner));
    }

    [TestMethod]
    public void The_pc_transfer_script_gets_the_holder_and_the_item_and_can_decline()
    {
        using var owner = Seeded();
        var host = new Host();
        host.Slots[(SessionEventKinds.PlayerEntity, SessionScriptPoint.Transfer)] = TransferScript;
        host.Programs[TransferScript] = Program(C(ScrCatalogue.Action.ReturnSkipDefault));
        var declined = owner.LootItem(owner.Version, "corpse", "boots", _ => throw new AssertFailedException("no insert check after a decline"), host);
        Assert.AreEqual((GameSessionDisposition.NoChange, (int?)SessionScriptPoint.Transfer, 0L), (declined.Disposition, declined.DeclinedBy, owner.Version.Revision));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.Transfer, SessionEventKinds.PlayerEntity) }, Runs(declined));
        Assert.IsFalse(host.Asked.Any(question => question.StartsWith("slot boots", StringComparison.Ordinal)), "the item's scripts are not asked");

        // The negative IN path runs the same gate; the dialog port reports the decline as applied (no conversation failure).
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.GiveItemToPc(owner.Version, "virgil", 2804, _ => SessionInsertVerdict.Accepted, host).Disposition);
        var pc = new DialogParticipantId(1); var npc = new DialogParticipantId(2);
        var port = new SessionDialogInventoryPort(owner, pc, id => id == npc ? "virgil" : null, _ => SessionInsertVerdict.Accepted, host);
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new GiveItemCommand(npc, pc, 2804)));
        Assert.AreEqual(SessionScriptPoint.Transfer, port.LastResult!.Value.DeclinedBy);
        Assert.AreEqual(0, Pc(owner).Length);
    }

    [TestMethod]
    public void A_refused_later_run_or_insert_check_drops_every_write_of_the_transfer()
    {
        using var owner = Seeded();
        var host = new Host();
        host.Slots[(SessionEventKinds.PlayerEntity, SessionScriptPoint.Transfer)] = TransferScript;
        host.Slots[("torn", SessionScriptPoint.RemoveItem)] = RemoveScript;
        host.Programs[TransferScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        // A changed attachment of the moved item has no home: refused, and the transfer script's write goes with it.
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.SetLocalFlag, (Number, 1)));
        var refused = owner.LootItem(owner.Version, "corpse", "torn", _ => SessionInsertVerdict.Accepted, host);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, 0L), (refused.Disposition, refused.Failure, owner.Version.Revision));
        Assert.AreEqual(2, refused.Scripts.Count, "the insert-item script is not run after a refused run");
        Assert.AreEqual(false, ReadFlag(owner, 3));

        // Session policy: an insert check refusal after the gate drops the gate's write too.
        var heavy = owner.LootItem(owner.Version, "corpse", "boots", _ => SessionInsertVerdict.TooHeavy, host);
        Assert.AreEqual((GameSessionFailure.InsertRefused, 1, false), (heavy.Failure, heavy.Scripts.Count, ReadFlag(owner, 3)));
        Assert.AreEqual(0L, owner.Version.Revision);
    }
}

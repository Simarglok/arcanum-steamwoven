using Arcanum.Data.Scripts;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-2827 (ledger IE2, audit TQ19): the transfer script (point 32) of the recipient of a hero's give (an NPC's or a container's) runs first, with the hero as the triggerer, the recipient as the
// attachee and the item as the extra; skip default refuses the move before any other check. Over the synthetic seed of InventoryStateTests (virgil alive, chest a container) and the half-elf hero
// (kit:1..3); script numbers and programs are invented.
[TestClass]
public sealed class HolderTransferScriptTests
{
    private const int TransferScript = 701;
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
    }

    // The seed of InventoryStateTests with the transfer point on virgil and on the chest.
    private static GameSessionOwner Hero()
    {
        var seed = InventoryStateTests.Seed();
        var custom = seed with { Holders = [.. seed.Holders.Select(h => h.Entity is "virgil" or "chest" ? h with { ScriptPoints = [.. h.ScriptPoints, SessionScriptPoint.Transfer] } : h)] };
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, custom).Failure);
        return owner;
    }

    private static string[] List(GameSessionOwner owner, string holder) =>
        owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == holder).Items.Select(item => item.Entity).ToArray();

    private static Host Hosted(GameSessionOwner owner, string holder, params (int Type, (byte Kind, int Value)[] Operands)[] actions)
    {
        var host = new Host(owner);
        host.Slots[(holder, SessionScriptPoint.Transfer)] = TransferScript;
        host.Programs[TransferScript] = Program(actions);
        return host;
    }

    [TestMethod]
    public void Without_a_script_host_a_holder_with_a_transfer_script_still_refuses()
    {
        using var owner = Hero();
        var refused = owner.GiveItemToNpc(owner.Version, "virgil", "kit:1", Accept);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.HolderScriptUnsupported, 0L), (refused.Disposition, refused.Failure, owner.Version.Revision));
        Assert.AreEqual(GameSessionFailure.HolderScriptUnsupported, owner.GiveItemToContainer(owner.Version, "chest", "kit:1", Accept).Failure);
    }

    [TestMethod]
    public void The_transfer_script_of_an_npc_runs_before_the_insert_check_and_its_effects_apply_with_the_give()
    {
        using var owner = Hero();
        var host = Hosted(owner, "virgil", C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        var asked = new List<string>();
        var given = owner.GiveItemToNpc(owner.Version, "virgil", "kit:1", item => { asked.Add(string.Join(",", host.Asked)); return SessionInsertVerdict.Accepted; }, scripts: host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "kit:1", (int?)null, 1L), (given.Disposition, given.Failure, given.Item, given.DeclinedBy, owner.Version.Revision));
        CollectionAssert.AreEqual(new[] { $"slot virgil {SessionScriptPoint.Transfer}" }, asked, "the transfer script ran before the insert check");
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.Transfer, "virgil") }, given.Scripts.Select(run => (run.Point, run.Attachee)).ToArray());
        Assert.AreEqual(true, ReadFlag(owner, 3), "the run's flag write applied with the give");
        CollectionAssert.AreEqual(new[] { "ring", "sword", "vest", "ring-copy", "kit:1" }, List(owner, "virgil"));
    }

    [TestMethod]
    public void A_transfer_script_that_skips_the_default_refuses_the_give_before_the_insert_check_but_its_effects_apply()
    {
        using var owner = Hero();
        var checks = 0;
        SessionInsertVerdict Counted(SessionInventoryItem item) { checks++; return SessionInsertVerdict.Accepted; }

        // No effect: nothing changes, not even the revision.
        var quiet = Hosted(owner, "virgil", C(ScrCatalogue.Action.ReturnSkipDefault));
        var declined = owner.GiveItemToNpc(owner.Version, "virgil", "kit:1", Counted, scripts: quiet);
        Assert.AreEqual((GameSessionDisposition.NoChange, GameSessionFailure.None, (int?)SessionScriptPoint.Transfer, 0L), (declined.Disposition, declined.Failure, declined.DeclinedBy, owner.Version.Revision));

        // A flag write and a printed line still happen, in one revision; the item stays with the hero.
        var loud = Hosted(owner, "virgil", C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.PrintLine, (Number, 2), (Number, 0)), C(ScrCatalogue.Action.ReturnSkipDefault));
        var written = owner.GiveItemToNpc(owner.Version, "virgil", "kit:1", Counted, scripts: loud);
        Assert.AreEqual((GameSessionDisposition.Changed, (int?)SessionScriptPoint.Transfer, 1L), (written.Disposition, written.DeclinedBy, owner.Version.Revision));
        CollectionAssert.AreEqual(new[] { new SessionScriptMessage(TransferScript, 2, 0, $"line 2 of {TransferScript}") }, written.Messages.ToArray());
        Assert.AreEqual((true, 0), (ReadFlag(owner, 3), checks), "the insert check is never asked");
        CollectionAssert.AreEqual(new[] { "ring", "sword", "vest", "ring-copy" }, List(owner, "virgil"));
        Assert.IsTrue(owner.QueryHeroKit().Any(item => item.Entity == "kit:1"), "the hero keeps the item");
    }

    [TestMethod]
    public void A_transfer_script_run_the_session_cannot_apply_refuses_the_give_with_no_effect()
    {
        using var owner = Hero();
        var host = Hosted(owner, "virgil", C(ScrCatalogue.Action.AddEffect, ((byte)0, 0), (Number, 50), (Number, 5)), C(ScrCatalogue.Action.ReturnRunDefault));
        var refused = owner.GiveItemToNpc(owner.Version, "virgil", "kit:1", Accept, scripts: host);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, 0L), (refused.Disposition, refused.Failure, owner.Version.Revision));
        CollectionAssert.AreEqual(new[] { "ring", "sword", "vest", "ring-copy" }, List(owner, "virgil"));
    }

    [TestMethod]
    public void A_container_runs_its_transfer_script_on_a_give_the_same_way()
    {
        using var owner = Hero();
        var host = Hosted(owner, "chest", C(ScrCatalogue.Action.ReturnSkipDefault));
        var declined = owner.GiveItemToContainer(owner.Version, "chest", "kit:1", Accept, scripts: host);
        Assert.AreEqual((GameSessionDisposition.NoChange, (int?)SessionScriptPoint.Transfer), (declined.Disposition, declined.DeclinedBy));
        CollectionAssert.AreEqual(Array.Empty<string>(), List(owner, "chest"));
        host.Programs[TransferScript] = Program(C(ScrCatalogue.Action.ReturnRunDefault));
        var given = owner.GiveItemToContainer(owner.Version, "chest", "kit:1", Accept, scripts: host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (given.Disposition, given.Failure));
        CollectionAssert.AreEqual(new[] { "kit:1" }, List(owner, "chest"));
    }

    [TestMethod]
    public void The_script_transfer_item_plan_runs_the_holder_script_through_its_dry_run()
    {
        using var owner = Hero();
        var name = owner.QueryHeroKit().Single(item => item.Entity == "kit:1").Kind.Name;
        // Without a host the plan still refuses; with one it answers for the run.
        Assert.AreEqual(GameSessionFailure.HolderScriptUnsupported, owner.QueryNamedItemTransfer(null, "virgil", name).Failure);
        var host = Hosted(owner, "virgil", C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        Assert.IsNull(owner.QueryNamedItemTransfer(null, "virgil", name, host, Accept).Refusal);
        Assert.AreEqual((0L, (bool?)false), (owner.Version.Revision, ReadFlag(owner, 3)), "the plan applies nothing");
        var moved = owner.TransferNamedItem(owner.Version, null, "virgil", name, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (moved.Disposition, moved.Failure));
        Assert.AreEqual(true, ReadFlag(owner, 3));
    }
}

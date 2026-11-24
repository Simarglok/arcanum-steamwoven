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
        var custom = seed with { Holders = [.. seed.Holders.Select(h => h.Entity is "virgil" or "chest" or "merchant" ? h with { ScriptPoints = [.. h.ScriptPoints, SessionScriptPoint.Transfer] } : h)] };
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

    [TestMethod]
    public void A_declined_barter_sale_pays_nothing_and_a_declined_dialog_handover_takes_the_settle_fallback()
    {
        using var owner = Hero();
        var gold = owner.QueryGold();
        var bullets = new SessionItemKind(SessionItemType.Ammo, 7059, 3301, 33301);
        // The merchant's script writes a flag and skips the default: the sale is declined (a Changed result with DeclinedBy), so the merchant pays nothing and the hero keeps the item.
        var host = Hosted(owner, "merchant", C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnSkipDefault));
        var sold = owner.BarterSell(owner.Version, "merchant", "kit:1", 1, 5, bullets, Accept, scripts: host);
        Assert.AreEqual((GameSessionDisposition.Changed, (int?)SessionScriptPoint.Transfer, (string?)null), (sold.Disposition, sold.DeclinedBy, sold.Item));
        Assert.AreEqual((gold, true), (owner.QueryGold(), ReadFlag(owner, 3)), "the declined sale paid nothing, its flag write stands");
        Assert.IsTrue(owner.QueryHeroKit().Any(item => item.Entity == "kit:1"));

        // The dialog's IN: the declined hand-over is the original's failed transfer (0x461CA0 returned 0), which the port settles as it settles a refused insert.
        DialogParticipantId pc = new(1), npc = new(2);
        var name = owner.QueryItemName("kit:2", owner.QueryHeroKit().Single(item => item.Entity == "kit:2").Kind.Name);
        var virgil = Hosted(owner, "virgil", C(ScrCatalogue.Action.ReturnSkipDefault));
        var port = new SessionDialogItemTakePort(owner, pc, who => who == npc ? "virgil" : null, Accept, scripts: virgil);
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new TakeItemCommand(npc, pc, name)));
        CollectionAssert.DoesNotContain(List(owner, "virgil"), "kit:2");
    }

    [TestMethod]
    public void The_script_transfer_item_plan_and_move_of_a_declining_holder_script_change_nothing_but_its_effects()
    {
        using var owner = Hero();
        var name = owner.QueryHeroKit().Single(item => item.Entity == "kit:1").Kind.Name;
        var host = Hosted(owner, "virgil", C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnSkipDefault));
        Assert.IsNull(owner.QueryNamedItemTransfer(null, "virgil", name, host, Accept).Refusal);
        Assert.AreEqual((0L, (bool?)false), (owner.Version.Revision, ReadFlag(owner, 3)), "the plan applies nothing, a declined gate's flags included");
        var moved = owner.TransferNamedItem(owner.Version, null, "virgil", name, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, (int?)SessionScriptPoint.Transfer), (moved.Disposition, moved.DeclinedBy));
        CollectionAssert.DoesNotContain(List(owner, "virgil"), "kit:1");
    }

    // ARC-SCRIPT-2857 (ledger NT1, NT2, IE2; exe 0x464830 → 0x4617F0 → 0x461810): a new gold pile or ammunition stack is a created object with no parent, so 0x461810 does not forward it to the parented transfer 0x461CA0 and the recipient's
    // transfer script (point 32) never runs for it: the move goes through the insert check alone, whatever the holder's scripts and wherever the coins or units come from.
    private static readonly SessionItemKind GoldPile = new(SessionItemType.Gold, 9056, 3200, 33200);
    private static readonly SessionItemKind Bullets = new(SessionItemType.Ammo, 7059, 3301, 33301);

    [TestMethod]
    public void A_new_gold_pile_is_not_refused_by_a_holders_transfer_script()
    {
        using var owner = Hero();
        // The insert check still refuses, as for any holder, with nothing changed.
        Assert.AreEqual((GameSessionFailure.InsertRefused, 0L), (owner.GiveGoldToNpc(owner.Version, "virgil", 40, GoldPile, _ => SessionInsertVerdict.NoRoom).Failure, owner.Version.Revision));
        var checks = new List<string>();
        var paid = owner.GiveGoldToNpc(owner.Version, "virgil", 40, GoldPile, item => { checks.Add(item.Entity); return SessionInsertVerdict.Accepted; });
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, (int?)null, "made:1"), (paid.Disposition, paid.Failure, paid.DeclinedBy, paid.Item));
        CollectionAssert.AreEqual(new[] { "made:1" }, checks, "the insert check still runs");
        Assert.AreEqual((40, 345), (owner.QueryNpcGold("virgil"), owner.QueryGold()));
        CollectionAssert.AreEqual(new[] { "ring", "sword", "vest", "ring-copy", "made:1" }, List(owner, "virgil"));
    }

    [TestMethod]
    public void A_new_ammunition_stack_and_a_barter_payment_pile_are_not_refused_by_a_transfer_script()
    {
        using var owner = Hero();
        // The hero gets a stack from virgil's list, then gives part of it to the scripted merchant, which holds none: a new stack.
        var source = owner.AddItemToNpc(owner.Version, "virgil", Bullets, Accept, (1, 30)).Item!;
        var stack = owner.TakeAmmoFromNpc(owner.Version, "virgil", source, 30, Bullets, Accept).Item!;
        var given = owner.GiveAmmoToNpc(owner.Version, "merchant", stack, 10, Bullets, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, (int?)null), (given.Disposition, given.Failure, given.DeclinedBy));
        Assert.AreEqual((10, 20), (owner.QueryAmmo(given.Item!)!.Value.Units, owner.QueryAmmo(stack)!.Value.Units));
        // A purchase pays into virgil, which has no pile: a new one.
        var bought = owner.BarterBuy(owner.Version, "virgil", "ring", 1, 3, Bullets, GoldPile, Accept, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (bought.Disposition, bought.Failure));
        Assert.AreEqual(3, owner.QueryNpcGold("virgil"), "the payment made a new pile in the scripted holder");
    }
}

using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

// The NPC sheet (spelled c-e), ii and ri reply actions through the window port (ARC-DEV-229, ledger WN1–WN4). Records and phrases are invented synthetic fixtures.
[TestClass]
public sealed class DialogWindowTests
{
    private const string Editor = "c" + "e";

    internal sealed class Windows(params DialogWindowKind[] offered) : IDialogWindowPort
    {
        public List<OpenWindowCommand> Opened { get; } = [];
        public DialogCommandResult Result { get; init; } = DialogCommandResult.Applied;
        public bool Throw { get; init; }
        public bool Offers(DialogWindowKind kind) => offered.Contains(kind);
        public DialogCommandResult Execute(OpenWindowCommand command)
        {
            if (Throw) throw new InvalidOperationException("windows");
            Opened.Add(command);
            return Result;
        }
    }

    private static readonly DialogWindowKind[] All = [DialogWindowKind.NpcSheet, DialogWindowKind.ItemIdentify, DialogWindowKind.ItemRepair];

    private static string Document(string action) => Record("10", "?") + Record("11", "Go.", iq: "1", response: "20", action: action) + Record("20", "Never spoken.");

    [TestMethod]
    public void Each_window_action_runs_the_whole_field_skips_the_target_and_ends_for_its_window()
    {
        foreach (var (action, transition, end, kind) in new[]
        {
            (Editor, DialogOptionTransition.NpcSheet, DialogEndKind.NpcSheetWindow, DialogWindowKind.NpcSheet),
            ("ii", DialogOptionTransition.ItemIdentify, DialogEndKind.ItemIdentifyWindow, DialogWindowKind.ItemIdentify),
            ("RI", DialogOptionTransition.ItemRepair, DialogEndKind.ItemRepairWindow, DialogWindowKind.ItemRepair),
        })
        {
            var windows = new Windows(All);
            var world = new World();
            var conversation = Start(Read(Document($"MM 1, {action}, MM 2")), new Ports(), Owner(), world: world, windows: windows);
            Assert.AreEqual(transition, conversation.Options[0].Transition, action);
            Assert.IsNull(conversation.Options[0].Target, "no look-ahead into the target node");
            Assert.AreEqual(DialogConversationSelectStatus.Ended, conversation.Select(conversation.Options[0].Token).Status, action);
            Assert.AreEqual(new DialogConversationEnd(end, null), conversation.End, action);
            CollectionAssert.AreEqual(new[] { new OpenWindowCommand(new(900), new(800), kind) }, windows.Opened, "the NPC and the PC");
            CollectionAssert.AreEqual(new[] { "MM:1", "MM:2" }, world.Commands, "the actions around it still run, in order");
        }
    }

    [TestMethod]
    public void The_last_window_action_of_a_field_decides_the_window_and_every_one_is_asked()
    {
        var windows = new Windows(All);
        var conversation = Start(Read(Document($"{Editor}, ri, su")), new Ports(), Owner(), windows: windows, schematics: new DialogSchematicsTests.Schematics());
        Assert.AreEqual(DialogOptionTransition.Schematics, conversation.Options[0].Transition, "su is the last");
        conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual(DialogEndKind.SchematicWindow, conversation.End!.Kind);
        CollectionAssert.AreEqual(new[] { DialogWindowKind.NpcSheet, DialogWindowKind.ItemRepair }, windows.Opened.Select(c => c.Window).ToArray());

        var other = new Windows(All);
        var last = Start(Read(Document($"su, {Editor}, ii")), new Ports(), Owner(), windows: other, schematics: new DialogSchematicsTests.Schematics());
        Assert.AreEqual(DialogOptionTransition.ItemIdentify, last.Options[0].Transition, "ii is the last");
    }

    [TestMethod]
    public void The_hostile_check_is_skipped_as_the_string_returns_zero()
    {
        // The three actions clear the string's result as su does (0x415BA0: the cases at 0x4164AF, 0x416561, 0x41659B), so a hostile NPC does not turn the reply into a refusal.
        var hostile = new DialogHostileTests.Hostility();
        var conversation = Start(Read(Document(Editor)), new Ports(), Owner(), windows: new Windows(All), hostile: hostile);
        conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual(0, hostile.Asked.Count);
        Assert.AreEqual(DialogEndKind.NpcSheetWindow, conversation.End!.Kind);
    }

    [TestMethod]
    public void A_number_after_the_code_is_ignored_and_a_missing_target_does_not_fail_the_step()
    {
        foreach (var action in new[] { Editor + " 3", Editor + "3", "ii -1", "RI 99999999999" })
        {
            var numbered = Start(Read(Document(action)), new Ports(), Owner(), windows: new Windows(All));
            Assert.IsNull(numbered.Options[0].Block, action);
            Assert.IsNull(numbered.Failure, action);
        }
        var missing = Start(Read(Record("10", "?") + Record("11", "Go.", iq: "1", response: "77", action: "ii")), new Ports(), Owner(), windows: new Windows(All));
        Assert.IsNull(missing.Failure, "the target node is never entered");
    }

    [TestMethod]
    public void A_window_the_host_does_not_offer_and_the_other_places_stay_outside_the_subset()
    {
        // The port offers only some windows: the others are offered blocked.
        var onlyEditor = Start(Read(Record("10", "?") + Record("11", "Go.", iq: "1", response: "20", action: "ii") + Record("12", "Edit.", iq: "1", response: "20", action: Editor) + Record("20", "Never spoken.")), new Ports(), Owner(), windows: new Windows(DialogWindowKind.NpcSheet));
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedOperation, onlyEditor.Options.Single(o => o.Text == "Go.").Block?.Kind);
        Assert.IsNull(onlyEditor.Options.Single(o => o.Text == "Edit.").Block);
        // No port at all.
        foreach (var action in new[] { Editor, "ii", "ri" })
            Assert.AreEqual(DialogOptionBlockKind.UnsupportedOperation, Start(Read(Document(action)), new Ports(), Owner()).Options[0].Block?.Kind, action);
        // Not an NPC line's action and not a condition.
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Start(Read(Record("10", "?", action: "ii")), new Ports(), Owner(), windows: new Windows(All)).Frontier!.Kind);
        var condition = Start(Read(Record("10", "?") + Record("11", "Go.", iq: "1", condition: "ri", response: "0")), new Ports(), Owner(), windows: new Windows(All));
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, condition.Frontier?.Kind);
        // A dialog transfer in the same field.
        var transfer = Start(Read(Document("ri, fl 20")), new Ports(), Owner(), windows: new Windows(All));
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedInteraction, transfer.Options[0].Block?.Kind);
    }

    [TestMethod]
    public void A_refusing_or_throwing_port_fails_the_conversation()
    {
        var rejected = Start(Read(Document(Editor)), new Ports(), Owner(), windows: new Windows(All) { Result = DialogCommandResult.Rejected });
        rejected.Select(rejected.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, rejected.Failure!.Kind);
        Assert.AreEqual(DialogCommandAttemptOutcome.Rejected, rejected.Effects.Single(e => e.Command is OpenWindowCommand).Outcome);
        var thrown = Start(Read(Document("ri")), new Ports(), Owner(), windows: new Windows(All) { Throw = true });
        thrown.Select(thrown.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.CommandIndeterminate, thrown.Failure!.Kind);
    }
}

using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

// The SU action through the schematic port (ARC-TECHNOLOGY-006, ledger NC1–NC2). Records and phrases are invented synthetic fixtures.
[TestClass]
public sealed class DialogSchematicsTests
{
    internal sealed class Schematics : IDialogSchematicPort
    {
        public List<(long Npc, long Pc)> Opened { get; } = [];
        public DialogCommandResult Result { get; set; } = DialogCommandResult.Applied;

        public DialogCommandResult Execute(OpenSchematicsCommand command)
        {
            Opened.Add((command.Npc.Value, command.PlayerCharacter.Value));
            return Result;
        }
    }

    [TestMethod]
    public void Su_runs_the_whole_field_skips_the_target_node_and_ends_for_the_window()
    {
        var document = Read(Record("10", "Shall I build it?") + Record("11", "Build it.", iq: "1", response: "20", action: "MM 1, su, MM 2")
            + Record("20", "Never spoken."));
        var schematics = new Schematics();
        var world = new World();
        var conversation = Start(document, new Ports(), Owner(), world: world, schematics: schematics);
        Assert.AreEqual(DialogOptionTransition.Schematics, conversation.Options[0].Transition);
        Assert.IsNull(conversation.Options[0].Target, "no look-ahead into the target node");
        Assert.AreEqual(DialogConversationSelectStatus.Ended, conversation.Select(conversation.Options[0].Token).Status);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.SchematicWindow, null), conversation.End);
        CollectionAssert.AreEqual(new[] { (900L, 800L) }, schematics.Opened, "the NPC as crafter, the PC as viewer");
        CollectionAssert.AreEqual(new[] { "MM:1", "MM:2" }, world.Commands, "the actions around SU still run, in order");
        Assert.AreNotEqual("Never spoken.", conversation.NpcLine?.Text);

        var rejected = Start(document, new Ports(), Owner(), world: new World(), schematics: new Schematics { Result = DialogCommandResult.Rejected });
        rejected.Select(rejected.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, rejected.Failure!.Kind);
        Assert.AreEqual(DialogCommandAttemptOutcome.Rejected, rejected.Effects.Single(e => e.Command is OpenSchematicsCommand).Outcome);
    }

    [TestMethod]
    public void Su_needs_the_port_ignores_a_number_and_is_a_reply_action_only()
    {
        var reply = Read(Record("10", "?") + Record("11", "Build.", iq: "1", response: "0", action: "su"));
        var plain = Start(reply, new Ports(), Owner());
        Assert.IsNotNull(plain.Options[0].Block, "unadmitted without a schematic port");
        foreach (var action in new[] { "su 3", "su3", "SU -1", "su+", "su-", "su 99999999999" })
        {
            var numbered = Start(Read(Record("10", "?") + Record("11", "Build.", iq: "1", response: "0", action: action)), new Ports(), Owner(), schematics: new Schematics());
            Assert.AreEqual((DialogOptionTransition.Schematics, (DialogOptionBlock?)null), (numbered.Options[0].Transition, numbered.Options[0].Block), action);
        }
        // The reply's target node is never entered, so a missing one does not fail the step (the original skips it).
        var missing = Start(Read(Record("10", "?") + Record("11", "Build.", iq: "1", response: "77", action: "su")), new Ports(), Owner(), schematics: new Schematics());
        Assert.AreEqual((DialogOptionTransition.Schematics, (DialogConversationFailure?)null), (missing.Options[0].Transition, missing.Failure));
        var missingPlain = Start(Read(Record("10", "?") + Record("11", "Go.", iq: "1", response: "77")), new Ports(), Owner(), schematics: new Schematics());
        Assert.AreEqual(DialogConversationFailureKind.MissingTarget, missingPlain.Failure!.Kind, "without SU a missing target still fails");
        var npcLine = Start(Read(Record("10", "?", action: "su")), new Ports(), Owner(), schematics: new Schematics());
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, npcLine.Frontier!.Kind, "not an NPC line's action");
        var withFinal = Start(Read(Record("10", "?") + Record("11", "Build.", iq: "1", response: "0", action: "su, fl 20") + Record("20", "Bye.")),
            new Ports(), Owner(), schematics: new Schematics());
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedInteraction, withFinal.Options[0].Block!.Kind, "a dialog transfer in the same field");
    }
}

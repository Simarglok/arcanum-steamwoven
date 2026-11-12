using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

// The l: reply through the world map port (ARC-SCRIPT-1358, ledger VA5). Records and phrases are invented synthetic fixtures.
[TestClass]
public sealed class DialogWorldMapTests
{
    internal sealed class WorldMap : IDialogWorldMapPort
    {
        public List<(long Npc, long Pc)> Opened { get; } = [];
        public DialogCommandResult Result { get; set; } = DialogCommandResult.Applied;
        public bool Throws { get; set; }

        public DialogCommandResult Execute(OpenWorldMapCommand command)
        {
            if (Throws) throw new InvalidOperationException("synthetic");
            Opened.Add((command.Npc.Value, command.PlayerCharacter.Value));
            return Result;
        }
    }

    [TestMethod]
    public void An_l_reply_runs_its_action_then_ends_for_the_world_map_without_following_its_value()
    {
        var document = Read(Record("10", "Anything else?") + Record("11", "L:", iq: "1", response: "20", action: "MM 1") + Record("20", "Never spoken."));
        var map = new WorldMap();
        var world = new World();
        var conversation = Start(document, new Ports(), Owner(), world: world, worldMap: map);
        Assert.AreEqual((DialogOptionTransition.WorldMap, DialogOptionTextKind.Generated, (DialogOptionBlock?)null),
            (conversation.Options[0].Transition, conversation.Options[0].TextKind, conversation.Options[0].Block));
        Assert.IsNull(conversation.Options[0].Target);
        Assert.AreEqual(DialogConversationSelectStatus.Ended, conversation.Select(conversation.Options[0].Token).Status);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.WorldMapWindow, null), conversation.End, "no script continuation follows");
        CollectionAssert.AreEqual(new[] { (900L, 800L) }, map.Opened);
        CollectionAssert.AreEqual(new[] { "MM:1" }, world.Commands, "the reply's action runs first");
        Assert.AreEqual(DialogCommandAttemptOutcome.Applied, conversation.Effects.Single(e => e.Command is OpenWorldMapCommand).Outcome);

        var rejected = Start(document, new Ports(), Owner(), world: new World(), worldMap: new WorldMap { Result = DialogCommandResult.Rejected });
        rejected.Select(rejected.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, rejected.Failure!.Kind);
    }

    [TestMethod]
    public void An_l_reply_needs_the_port_an_exact_marker_and_no_dialog_transfer()
    {
        var reply = Read(Record("10", "?") + Record("11", "l:", iq: "1", response: "77"));
        var plain = Start(reply, new Ports(), Owner());
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedInteraction, plain.Options[0].Block!.Kind, "unadmitted without a world map port");
        var missing = Start(reply, new Ports(), Owner(), worldMap: new WorldMap());
        Assert.AreEqual((DialogOptionTransition.WorldMap, (DialogConversationFailure?)null), (missing.Options[0].Transition, missing.Failure),
            "a missing target does not fail: the value is not followed");
        var withFinal = Start(Read(Record("10", "?") + Record("11", "l:", iq: "1", response: "0", action: "fl 20") + Record("20", "Bye.")),
            new Ports(), Owner(), worldMap: new WorldMap());
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedInteraction, withFinal.Options[0].Block!.Kind, "a dialog transfer in the same reply");
    }

    [TestMethod]
    public void A_throwing_port_is_recorded_as_indeterminate_and_a_longer_marker_is_an_ordinary_reply()
    {
        var document = Read(Record("10", "?") + Record("11", "l:", iq: "1", response: "0"));
        var conversation = Start(document, new Ports(), Owner(), worldMap: new WorldMap { Throws = true });
        conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.CommandIndeterminate, conversation.Failure!.Kind);
        Assert.AreEqual(DialogCommandAttemptOutcome.Indeterminate, conversation.Effects.Single(e => e.Command is OpenWorldMapCommand).Outcome);

        var longer = Start(Read(Record("10", "?") + Record("11", "l:x", iq: "1", response: "0")), new Ports(), Owner(), worldMap: new WorldMap());
        Assert.AreEqual((DialogOptionTextKind.Record, DialogOptionTransition.Exit), (longer.Options[0].TextKind, longer.Options[0].Transition), "only the exact marker is the world map reply");
    }
}

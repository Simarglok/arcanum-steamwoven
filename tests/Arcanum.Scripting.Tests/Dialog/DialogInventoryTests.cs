using Arcanum.Data;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

// Negative IN through the inventory port (ARC-WORLD-014). Records and phrases are invented synthetic fixtures.
[TestClass]
public sealed class DialogInventoryTests
{
    private static readonly DlgDocument Give = Read(Record("10", "Take this.", action: "LF 1 1, IN -2804, MM 3") + Record("11", "Thanks.", iq: "1", response: "0"));

    [TestMethod]
    public void An_inventory_port_takes_in_from_the_world_port()
    {
        var inventory = new Inventory();
        var world = new World();
        var ports = new Ports();
        var conversation = Start(Give, ports, Owner(), world: world, inventory: inventory);
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, conversation.Phase);
        CollectionAssert.AreEqual(new[] { new GiveItemCommand(new(900), new(800), 2804) }, inventory.Commands);
        CollectionAssert.AreEqual(new[] { "MM:3" }, world.Commands);
        Assert.AreEqual(new GiveItemCommand(new(900), new(800), 2804), conversation.Effects[1].Command);
        Assert.AreEqual(DialogCommandAttemptOutcome.Applied, conversation.Effects[1].Outcome);

        // IN needs only one of the two ports; without either it is a frontier before any effect.
        var inventoryOnly = Start(Read(Record("10", "Take this.", action: "IN -2804") + Record("11", "Thanks.", iq: "1", response: "0")),
            new Ports(), Owner(), inventory: new Inventory());
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, inventoryOnly.Phase);
        var neither = Start(Give, new Ports(), Owner());
        Assert.AreEqual(DialogFrontierKind.CapabilityUnavailable, neither.Frontier!.Kind);
        Assert.AreEqual(0, neither.Effects.Count);
    }

    [TestMethod]
    public void A_refusing_or_throwing_inventory_port_fails_the_conversation()
    {
        var rejected = Start(Give, new Ports(), Owner(), world: new World(), inventory: new Inventory { Result = DialogCommandResult.Rejected });
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, rejected.Failure!.Kind);
        Assert.AreEqual(DialogCommandAttemptOutcome.Rejected, rejected.Effects.Last().Outcome);

        var thrown = Start(Give, new Ports(), Owner(), world: new World(), inventory: new Inventory { Throw = true });
        Assert.AreEqual(DialogConversationFailureKind.CommandIndeterminate, thrown.Failure!.Kind);
        Assert.AreEqual(DialogCommandAttemptOutcome.Indeterminate, thrown.Effects.Last().Outcome);
    }

    private sealed class Inventory : IDialogInventoryPort
    {
        public DialogCommandResult Result { get; init; } = DialogCommandResult.Applied;
        public bool Throw { get; init; }
        public List<GiveItemCommand> Commands { get; } = [];

        public DialogCommandResult Execute(GiveItemCommand command)
        {
            if (Throw) throw new InvalidOperationException("inventory");
            Commands.Add(command);
            return Result;
        }
    }
}

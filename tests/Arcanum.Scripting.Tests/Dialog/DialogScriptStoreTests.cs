using Arcanum.Data;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>ARC-DEV-152: dialog GV, PV and PF through the script store port (ledger DG1–DG4): as a condition the cell equals the second
/// operand; as an action of either side the cell takes it; PF admits only 0 and 1, an index outside the stores is invalid.</summary>
[TestClass]
public sealed class DialogScriptStoreTests
{
    [TestMethod]
    public void Conditions_compare_each_store_cell_with_the_second_operand()
    {
        var document = Read(Record("10", "?")
            + Record("11", "gv equal", iq: "1", condition: "GV 5 7", response: "0")
            + Record("12", "gv other", iq: "1", condition: "gv 5 8", response: "0")
            + Record("13", "pv negative", iq: "1", condition: "PV 65535 -3", response: "0")
            + Record("14", "pf set", iq: "1", condition: "PF 40 1", response: "0")
            + Record("15", "pf clear", iq: "1", condition: "pf 41 0", response: "0")
            + Record("16", "pf not clear", iq: "1", condition: "PF 40 0", response: "0")
            + Record("17", "all", iq: "1", condition: "GV 5 7, PV 65535 -3, PF 40 1", response: "0"));
        var stores = new Stores();
        stores.Cells[(DialogScriptStore.GlobalVariable, 5)] = 7;
        stores.Cells[(DialogScriptStore.PcVariable, 65535)] = -3;
        stores.Cells[(DialogScriptStore.PcFlag, 40)] = 1;
        CollectionAssert.AreEqual(new[] { "gv equal", "pv negative", "pf set", "pf clear", "all" },
            StartWith(document, new Ports(), stores).Options.Select(o => o.Text).ToArray());
        Assert.IsTrue(stores.Queries.All(query => query.PlayerCharacter == new DialogParticipantId(800)));
        CollectionAssert.AreEqual(new[] { "pf clear", "pf not clear" },
            StartWith(document, new Ports(), new Stores()).Options.Select(o => o.Text).ToArray(), "every cell reads 0 until written");
    }

    [TestMethod]
    public void Actions_write_the_cells_in_action_order_from_npc_and_player_fields()
    {
        var document = Read(Record("10", "?", action: "GV 1999 -2147483648, LF 1 1, PV 0 12, PF 65535 1, pf 3 0")
            + Record("11", "x", iq: "1", response: "0", action: "gv 4 9"));
        var ports = new Ports();
        var stores = new Stores();
        var conversation = StartWith(document, ports, stores);
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, conversation.Phase);
        CollectionAssert.AreEqual(new[] { "GlobalVariable 1999=-2147483648", "PcVariable 0=12", "PcFlag 65535=1", "PcFlag 3=0" }, stores.Commands.ToArray());
        Assert.AreEqual(new ScriptStoreCommand(new DialogParticipantId(800), DialogScriptStore.PcVariable, 0, 12), conversation.Effects[2].Command);
        Assert.IsTrue(conversation.Effects.All(effect => effect.Outcome == DialogCommandAttemptOutcome.Applied));
        conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual("GlobalVariable 4=9", stores.Commands[^1]);
    }

    [TestMethod]
    public void Out_of_subset_operands_and_a_missing_port_stop_the_conversation()
    {
        static DialogConversation Action(string action, IDialogScriptStorePort? stores) =>
            StartWith(Read(Record("10", "?", action: action) + Record("11", "x", iq: "1", response: "0")), new Ports(), stores);
        static DialogConversation Condition(string condition, IDialogScriptStorePort? stores) =>
            StartWith(Read(Record("10", "?") + Record("11", "x", iq: "1", condition: condition, response: "0")), new Ports(), stores);

        // A PF value other than 0 or 1 is valid data the subset leaves out (the source spreads it over the flag word, GV4).
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Action("PF 3 2", new Stores()).Frontier!.Kind);
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Condition("PF 3 -1", new Stores()).Frontier!.Kind);
        // A PC index beyond the session's 16-bit stores is outside the subset too.
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Action("PV 65536 1", new Stores()).Frontier!.Kind);
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Condition("PF 65536 0", new Stores()).Frontier!.Kind);
        // Without the port GV, PV and PF stay unadmitted on both sides.
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Action("GV 1 1", null).Frontier!.Kind);
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Condition("PV 1 1", null).Frontier!.Kind);
        // A global index outside 0..1999, a negative PC index, a missing second operand, a '+' sign: invalid here.
        foreach (var invalid in new[] { "GV 2000 1", "GV -1 1", "PV -1 1", "PF -2 0", "PV 3", "GV 3 +4" })
            Assert.AreEqual(DialogConversationFailureKind.InvalidOperation, Action(invalid, new Stores()).Failure!.Kind, invalid);
    }

    [TestMethod]
    public void A_refused_throwing_or_invalid_store_fails_the_conversation()
    {
        var action = Read(Record("10", "?", action: "PV 1 1") + Record("11", "x", iq: "1", response: "0"));
        var refused = StartWith(action, new Ports(), new Stores { Reject = true });
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, refused.Failure!.Kind);
        Assert.AreEqual(DialogCommandAttemptOutcome.Rejected, refused.Effects.Single().Outcome);
        Assert.AreEqual(DialogConversationFailureKind.CommandIndeterminate, StartWith(action, new Ports(), new Stores { Throw = true }).Failure!.Kind);

        var condition = Read(Record("10", "?") + Record("11", "x", iq: "1", condition: "PF 7 1", response: "0"));
        Assert.AreEqual(DialogConversationFailureKind.QueryException, StartWith(condition, new Ports(), new Stores { Throw = true }).Failure!.Kind);
        var stores = new Stores();
        stores.Cells[(DialogScriptStore.PcFlag, 7)] = 2;
        Assert.AreEqual(DialogConversationFailureKind.InvalidQueryResult, StartWith(condition, new Ports(), stores).Failure!.Kind,
            "a PC flag reads 0 or 1");
    }

    private static DialogConversation StartWith(DlgDocument document, Ports ports, IDialogScriptStorePort? stores) =>
        DialogConversation.Start(document,
            new DialogConversationRequest(new DialogNodeId(10),
                new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
                new DialogScriptLine(0), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
            new DialogConversationPorts(ports, ports, Owner(), Stores: stores));

    private sealed class Stores : IDialogScriptStorePort
    {
        public Dictionary<(DialogScriptStore Store, int Index), int> Cells { get; } = [];
        public List<DialogScriptStoreQuery> Queries { get; } = [];
        public List<string> Commands { get; } = [];
        public bool Reject { get; init; }
        public bool Throw { get; init; }

        public int Read(DialogScriptStoreQuery query)
        {
            if (Throw) throw new InvalidOperationException("Synthetic query failure.");
            Queries.Add(query);
            return Cells.GetValueOrDefault((query.Store, query.Index));
        }

        public DialogCommandResult Execute(ScriptStoreCommand command)
        {
            if (Throw) throw new InvalidOperationException("Synthetic command failure.");
            if (Reject) return DialogCommandResult.Rejected;
            Assert.AreEqual(new DialogParticipantId(800), command.PlayerCharacter);
            Commands.Add($"{command.Store} {command.Index}={command.Value}");
            Cells[(command.Store, command.Index)] = command.Value;
            return DialogCommandResult.Applied;
        }
    }
}

using System.Text;
using Arcanum.Data;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Dialog;

[TestClass]
public sealed class DialogTransitionRuntimeTests
{
    [TestMethod]
    public void Start_and_select_execute_one_checked_direct_edge_in_serialized_order()
    {
        var document = DlgReader.Read(Bytes(
            Record("100", action: "QU 10 1; QU(11,2)")
            + Record("200", iq: "1", condition: "lF 3 1", response: "300", action: "LF 4 1 LF 5 0")
            + Record("300", action: "RU 1000; RU 1001")));
        var ports = new Ports { Flags = { [3] = LocalFlagReadResult.Found(1) } };

        var start = DialogTransitionRuntime.Start(document, Request(100, 200), ports, ports);

        Assert.AreEqual(DialogStartStatus.AwaitingSelection, start.Status);
        Assert.AreEqual(1, start.Session.Offers.Count);
        Assert.AreEqual(DialogTransitionKind.Direct, start.Session.Offers[0].TransitionKind);
        Assert.AreEqual(new DialogNodeId(300), start.Session.Offers[0].DirectTarget);
        var selected = start.Session.Select(start.Session.Offers[0].Token);

        Assert.AreEqual(DialogSelectionStatus.Completed, selected.Status);
        Assert.AreEqual(DialogSessionPhase.CompletedAtFrontier, start.Session.Phase);
        Assert.AreEqual(new DialogNodeId(300), start.Session.CurrentNodeId);
        CollectionAssert.AreEqual(
            new[] { "QU:10:1:900", "QU:11:2:900", "LF:4:1:900", "LF:5:0:900", "RU:1000", "RU:1001" },
            ports.Commands);
        Assert.AreEqual(2, ports.QueryCount);
        Assert.IsTrue(start.Session.CommandAttempts.All(attempt => attempt.Outcome == DialogCommandAttemptOutcome.Applied));
        Assert.AreEqual(6, start.Session.CommandAttempts.Count);
        Assert.AreEqual(DlgFieldKind.Action, start.Session.CommandAttempts[0].Source.FieldKind);
        Assert.AreEqual(0, start.Session.CommandAttempts[0].Source.OperationOrdinal);
    }

    [TestMethod]
    public void Refresh_and_stale_selection_never_replay_source_entry_actions()
    {
        var document = DlgReader.Read(Bytes(
            Record("100", action: "QU 10 1")
            + Record("200", iq: "1", condition: "LF 3 1", response: "300")
            + Record("300")));
        var ports = new Ports { Flags = { [3] = LocalFlagReadResult.Found(1) } };
        var start = DialogTransitionRuntime.Start(document, Request(100, 200), ports, ports);
        var old = start.Session.Offers.Single().Token;

        var refreshed = start.Session.RefreshOffers();
        Assert.AreEqual(DialogOfferRefreshStatus.Updated, refreshed.Status);
        Assert.AreEqual(DialogSelectionStatus.NotOffered, start.Session.Select(old).Status);
        ports.Flags[3] = LocalFlagReadResult.Found(0);
        var stale = start.Session.Select(start.Session.Offers.Single().Token);

        Assert.AreEqual(DialogSelectionStatus.StaleSelection, stale.Status);
        Assert.AreEqual(DialogSessionPhase.AwaitingSelection, start.Session.Phase);
        CollectionAssert.AreEqual(new[] { "QU:10:1:900" }, ports.Commands);
        Assert.AreEqual(3, ports.QueryCount);
    }

    [TestMethod]
    public void Static_invalid_operation_fails_without_port_calls()
    {
        var document = DlgReader.Read(Bytes(Record("100", action: "SC 1") + Record("200", iq: "1", response: "0")));
        var ports = new Ports();

        var start = DialogTransitionRuntime.Start(document, Request(100, 200), ports, ports);

        Assert.AreEqual(DialogStartStatus.Failed, start.Status);
        Assert.AreEqual(DialogFailureKind.UnsupportedOperation, start.Failure!.Kind);
        Assert.AreEqual(DialogSessionPhase.Failed, start.Session.Phase);
        Assert.AreEqual(0, ports.QueryCount);
        Assert.AreEqual(0, ports.Commands.Count);
    }

    [TestMethod]
    public void Rejected_command_keeps_prior_receipts_and_enters_terminal_failed_state()
    {
        var document = DlgReader.Read(Bytes(Record("100", action: "QU 10 1; QU 11 2") + Record("200", iq: "1", response: "0")));
        var ports = new Ports { RejectAt = 2 };

        var start = DialogTransitionRuntime.Start(document, Request(100, 200), ports, ports);

        Assert.AreEqual(DialogStartStatus.Failed, start.Status);
        Assert.AreEqual(DialogFailureKind.CommandRejected, start.Failure!.Kind);
        Assert.AreEqual(2, start.Session.CommandAttempts.Count);
        Assert.AreEqual(DialogCommandAttemptOutcome.Applied, start.Session.CommandAttempts[0].Outcome);
        Assert.AreEqual(DialogCommandAttemptOutcome.Rejected, start.Session.CommandAttempts[1].Outcome);
        Assert.AreEqual(DialogSelectionStatus.WrongPhase, start.Session.Select(new DialogOfferToken(new DialogResponseId(200), 0)).Status);
        CollectionAssert.AreEqual(new[] { "QU:10:1:900" }, ports.Commands);
    }

    [TestMethod]
    public void Exit_and_special_are_visible_but_never_execute_commands()
    {
        var document = DlgReader.Read(Bytes(
            Record("100") + Record("200", iq: "1", response: "0", action: "LF 1 1") + Record("201", iq: "1", response: "-9", action: "LF 2 1")));
        var ports = new Ports();
        var start = DialogTransitionRuntime.Start(document, Request(100, 200, 201), ports, ports);

        Assert.AreEqual(2, start.Session.Offers.Count);
        Assert.AreEqual(DialogSelectionStatus.UnsupportedTransition, start.Session.Select(start.Session.Offers[0].Token).Status);
        Assert.AreEqual(DialogSelectionStatus.UnsupportedTransition, start.Session.Select(start.Session.Offers[1].Token).Status);
        Assert.AreEqual(0, ports.Commands.Count);
    }

    [TestMethod]
    public void Invalid_start_inputs_and_response_cap_are_static_and_port_free()
    {
        var sixResponses = string.Concat(Enumerable.Range(200, 6).Select(id => Record(id.ToString(), iq: "1", response: "0")));
        var document = DlgReader.Read(Bytes(Record("100") + sixResponses));
        var ports = new Ports();

        var capped = DialogTransitionRuntime.Start(document, Request(100, 200, 201, 202, 203, 204, 205), ports, ports);
        Assert.AreEqual(DialogFailureKind.ResponseLimitExceeded, capped.Failure!.Kind);
        Assert.AreEqual(0, ports.QueryCount);
        Assert.AreEqual(0, ports.Commands.Count);

        var one = DlgReader.Read(Bytes(Record("100") + Record("200", iq: "1", response: "0")));
        Throws<ArgumentOutOfRangeException>(() => DialogTransitionRuntime.Start(
            one, Request(100, 200), ports, ports, new DialogRuntimeOptions { MaxResponses = 6 }));
        var invalidEligibility = DialogTransitionRuntime.Start(one, Request(100), ports, ports);
        Assert.AreEqual(DialogFailureKind.InvalidEligibility, invalidEligibility.Failure!.Kind);
    }

    [TestMethod]
    public void Invalid_targets_and_grammar_are_static_failures_before_source_actions()
    {
        var ports = new Ports();
        var missingTarget = DlgReader.Read(Bytes(Record("100", action: "QU 1 1") + Record("200", iq: "1", response: "300")));
        var missing = DialogTransitionRuntime.Start(missingTarget, Request(100, 200), ports, ports);
        Assert.AreEqual(DialogFailureKind.MissingTarget, missing.Failure!.Kind);

        var wrongTarget = DlgReader.Read(Bytes(
            Record("100") + Record("200", iq: "1", response: "300") + Record("101") + Record("300", iq: "1", response: "0")));
        var wrong = DialogTransitionRuntime.Start(wrongTarget, Request(100, 200), ports, ports);
        Assert.AreEqual(DialogFailureKind.WrongTargetRole, wrong.Failure!.Kind);

        foreach (var action in new[] { "LF 32 1", "QU 1 7", "RU 999", "LF +1 0", "LF 1 0 SC 2" })
        {
            var document = DlgReader.Read(Bytes(Record("100", action: action) + Record("200", iq: "1", response: "0")));
            var result = DialogTransitionRuntime.Start(document, Request(100, 200), ports, ports);
            Assert.AreEqual(DialogStartStatus.Failed, result.Status, action);
        }
        Assert.AreEqual(0, ports.Commands.Count);
    }

    [TestMethod]
    public void Eligibility_gates_and_invalid_query_result_do_not_execute_response_actions()
    {
        var document = DlgReader.Read(Bytes(
            Record("100", action: "QU 1 1")
            + Record("200", iq: "1", condition: "LF 3 1", response: "300", action: "LF 1 1")
            + Record("300")));
        var gatedRequest = new DialogStartRequest(
            new DialogNodeId(100),
            new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
            new[] { new DialogResponseEligibility(new DialogResponseId(200), false, true) });
        var gatedPorts = new Ports();
        var gated = DialogTransitionRuntime.Start(document, gatedRequest, gatedPorts, gatedPorts);
        Assert.AreEqual(DialogStartStatus.AwaitingSelection, gated.Status);
        Assert.AreEqual(0, gated.Session.Offers.Count);
        Assert.AreEqual(0, gatedPorts.QueryCount);
        CollectionAssert.AreEqual(new[] { "QU:1:1:900" }, gatedPorts.Commands);

        var invalidPorts = new Ports { InvalidRead = true };
        var invalid = DialogTransitionRuntime.Start(document, Request(100, 200), invalidPorts, invalidPorts);
        Assert.AreEqual(DialogFailureKind.InvalidQueryResult, invalid.Failure!.Kind);
        CollectionAssert.AreEqual(new[] { "QU:1:1:900" }, invalidPorts.Commands);
    }

    [TestMethod]
    public void Command_and_query_exceptions_are_indeterminate_or_terminal_without_retry()
    {
        var commandDocument = DlgReader.Read(Bytes(Record("100", action: "QU 1 1; QU 2 2") + Record("200", iq: "1", response: "0")));
        var commandPorts = new Ports { ThrowAt = 2 };
        var commandFailure = DialogTransitionRuntime.Start(commandDocument, Request(100, 200), commandPorts, commandPorts);
        Assert.AreEqual(DialogFailureKind.CommandException, commandFailure.Failure!.Kind);
        Assert.AreEqual(DialogCommandAttemptOutcome.Indeterminate, commandFailure.Session.CommandAttempts[1].Outcome);
        Assert.AreEqual(1, commandPorts.Commands.Count);

        var queryDocument = DlgReader.Read(Bytes(Record("100", action: "QU 1 1") + Record("200", iq: "1", condition: "LF 3 1", response: "0")));
        var queryPorts = new Ports { ThrowOnRead = true };
        var queryFailure = DialogTransitionRuntime.Start(queryDocument, Request(100, 200), queryPorts, queryPorts);
        Assert.AreEqual(DialogFailureKind.QueryException, queryFailure.Failure!.Kind);
        Assert.AreEqual(DialogOfferRefreshStatus.WrongPhase, queryFailure.Session.RefreshOffers().Status);
        CollectionAssert.AreEqual(new[] { "QU:1:1:900" }, queryPorts.Commands);
    }

    private static DialogStartRequest Request(int entry, params int[] responses) => new(
        new DialogNodeId(entry),
        new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
        responses.Select(response => new DialogResponseEligibility(new DialogResponseId(response), true, true)).ToArray());

    private static string Record(string id, string text = "", string alternateText = "", string iq = "", string condition = "", string response = "", string action = "") =>
        $"{{{id}}}{{{text}}}{{{alternateText}}}{{{iq}}}{{{condition}}}{{{response}}}{{{action}}}";

    private static byte[] Bytes(string text) => Encoding.Latin1.GetBytes(text);

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new AssertFailedException($"Expected {typeof(TException).Name}.");
    }

    private sealed class Ports : IDialogStateQueryPort, IDialogCommandPort
    {
        public Dictionary<int, LocalFlagReadResult> Flags { get; } = [];
        public List<string> Commands { get; } = [];
        public int QueryCount { get; private set; }
        public int? RejectAt { get; init; }
        public int? ThrowAt { get; init; }
        public bool ThrowOnRead { get; init; }
        public bool InvalidRead { get; init; }

        public LocalFlagReadResult Read(LocalFlagQuery query)
        {
            QueryCount++;
            if (ThrowOnRead)
            {
                throw new InvalidOperationException("Synthetic query failure.");
            }
            if (InvalidRead)
            {
                return default;
            }
            return Flags.TryGetValue(query.Index, out var result) ? result : LocalFlagReadResult.Missing();
        }

        public DialogCommandResult Execute(LocalFlagWriteCommand command) => Execute($"LF:{command.Index}:{command.Value}:{command.Npc.Value}");
        public DialogCommandResult Execute(QuestStateCommand command) => Execute($"QU:{command.QuestId}:{command.State}:{command.NpcContext.Value}");
        public DialogCommandResult Execute(RumorLogCommand command) => Execute($"RU:{command.RumorId}");

        private DialogCommandResult Execute(string value)
        {
            var ordinal = Commands.Count + 1;
            if (RejectAt == ordinal)
            {
                return DialogCommandResult.Rejected;
            }
            if (ThrowAt == ordinal)
            {
                throw new InvalidOperationException("Synthetic command failure.");
            }
            Commands.Add(value);
            return DialogCommandResult.Applied;
        }
    }
}

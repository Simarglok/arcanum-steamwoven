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

    [TestMethod]
    public void Conditioned_exit_and_special_selection_are_zero_port_and_non_mutating()
    {
        var document = DlgReader.Read(Bytes(
            Record("100")
            + Record("200", iq: "1", condition: "LF 1 1", response: "0", action: "LF 2 1")
            + Record("201", iq: "1", condition: "LF 3 1", response: "-4", action: "LF 4 1")));
        var ports = new Ports { Flags = { [1] = LocalFlagReadResult.Found(1), [3] = LocalFlagReadResult.Found(1) } };
        var start = DialogTransitionRuntime.Start(document, Request(100, 200, 201), ports, ports);
        var beforeOffers = start.Session.Offers.ToArray();
        var beforeQueries = ports.QueryCount;

        foreach (var offer in beforeOffers)
        {
            Assert.AreEqual(DialogSelectionStatus.UnsupportedTransition, start.Session.Select(offer.Token).Status);
        }

        Assert.AreEqual(beforeQueries, ports.QueryCount);
        Assert.AreEqual(DialogSessionPhase.AwaitingSelection, start.Session.Phase);
        CollectionAssert.AreEqual(beforeOffers, start.Session.Offers.ToArray());
        Assert.AreEqual(new DialogNodeId(100), start.Session.CurrentNodeId);
        Assert.AreEqual(0, start.Session.CommandAttempts.Count);
        Assert.AreEqual(0, ports.Commands.Count);
    }

    [TestMethod]
    public void Selected_gate_query_failure_stops_immediately_and_preserves_exception()
    {
        foreach (var invalid in new[] { false, true })
        {
            var document = DlgReader.Read(Bytes(
                Record("100", action: "QU 1 1")
                + Record("200", iq: "1", condition: "LF 1 1", response: "300")
                + Record("201", iq: "1", condition: "LF 2 1", response: "301")
                + Record("300") + Record("301")));
            var sentinel = new InvalidOperationException("selected-query");
            var ports = new Ports
            {
                Flags = { [1] = LocalFlagReadResult.Found(1), [2] = LocalFlagReadResult.Found(1) },
                InvalidReadAt = invalid ? 3 : null,
                ThrowReadAt = invalid ? null : 3,
                QueryException = sentinel,
            };
            var start = DialogTransitionRuntime.Start(document, Request(100, 200, 201), ports, ports);
            Assert.AreEqual(2, ports.QueryCount);

            var selected = start.Session.Select(start.Session.Offers.Single(offer => offer.ResponseId.Value == 201).Token);

            Assert.AreEqual(DialogSelectionStatus.Failed, selected.Status);
            Assert.AreEqual(invalid ? DialogFailureKind.InvalidQueryResult : DialogFailureKind.QueryException, selected.Failure!.Kind);
            if (!invalid)
            {
                Assert.AreSame(sentinel, selected.Failure.Exception);
            }
            Assert.AreEqual(3, ports.QueryCount, "No rebuild query may follow the failing selected read.");
            Assert.AreEqual(DialogSessionPhase.Failed, start.Session.Phase);
            Assert.AreEqual(new DialogNodeId(100), start.Session.CurrentNodeId);
            CollectionAssert.AreEqual(new[] { "QU:1:1:900" }, ports.Commands);
            Assert.AreEqual(DialogOfferRefreshStatus.WrongPhase, start.Session.RefreshOffers().Status);
            Assert.AreEqual(3, ports.QueryCount, "Terminal methods must make no port calls.");
        }
    }

    [TestMethod]
    public void Unsupported_callback_and_combinator_forms_are_static_unsupported()
    {
        foreach (var action in new[] { "SC", "SCR", "SCR callback", "ZZ", "&&", "LF 1 0 AND LF 2 1" })
        {
            var document = DlgReader.Read(Bytes(Record("100", action: action) + Record("200", iq: "1", response: "0")));
            var ports = new Ports();

            var start = DialogTransitionRuntime.Start(document, Request(100, 200), ports, ports);

            Assert.AreEqual(DialogFailureKind.UnsupportedOperation, start.Failure!.Kind, action);
            Assert.AreEqual(0, ports.QueryCount, action);
            Assert.AreEqual(0, ports.Commands.Count, action);
        }
    }

    [TestMethod]
    public void Whole_step_limits_are_ordered_attributed_and_deduplicate_shared_targets()
    {
        var crossing = DlgReader.Read(Bytes(
            Record("100", action: "QU 1 1")
            + Record("200", iq: "1", response: "300", action: "RU 1000")
            + Record("300", action: "SC")));
        var crossingPorts = new Ports();
        var crossingResult = DialogTransitionRuntime.Start(
            crossing,
            Request(100, 200),
            crossingPorts,
            crossingPorts,
            new DialogRuntimeOptions { MaxSemanticInputBytes = 12 });
        Assert.AreEqual(DialogFailureKind.SemanticInputLimitExceeded, crossingResult.Failure!.Kind);
        Assert.AreEqual(new DialogNodeId(200), crossingResult.Failure.Source!.Value.RecordId);
        Assert.AreEqual(DlgFieldKind.Action, crossingResult.Failure.Source.Value.FieldKind);
        Assert.AreEqual(0, crossingPorts.Commands.Count);

        var sharedTarget = DlgReader.Read(Bytes(
            Record("100")
            + Record("200", iq: "1", response: "300")
            + Record("201", iq: "1", response: "300")
            + Record("300", action: "RU 1000")));
        var sharedPorts = new Ports();
        var sharedResult = DialogTransitionRuntime.Start(
            sharedTarget,
            Request(100, 200, 201),
            sharedPorts,
            sharedPorts,
            new DialogRuntimeOptions { MaxSemanticInputBytes = 7 });
        Assert.AreEqual(DialogStartStatus.AwaitingSelection, sharedResult.Status);

        var operationCrossing = DlgReader.Read(Bytes(
            Record("100", action: "QU 1 1")
            + Record("200", iq: "1", response: "300", action: "LF 1 1")
            + Record("300")));
        var operationPorts = new Ports();
        var operationResult = DialogTransitionRuntime.Start(
            operationCrossing,
            Request(100, 200),
            operationPorts,
            operationPorts,
            new DialogRuntimeOptions { MaxOperationsPerTransition = 1 });
        Assert.AreEqual(DialogFailureKind.OperationLimitExceeded, operationResult.Failure!.Kind);
        Assert.AreEqual(new DialogNodeId(200), operationResult.Failure.Source!.Value.RecordId);
        Assert.AreEqual(0, operationPorts.Commands.Count);
    }

    [TestMethod]
    public void Field_and_transition_limit_boundaries_are_exact()
    {
        var document = DlgReader.Read(Bytes(Record("100", action: "QU 1 1") + Record("200", iq: "1", response: "0")));
        var ports = new Ports();
        Assert.AreEqual(
            DialogStartStatus.AwaitingSelection,
            DialogTransitionRuntime.Start(document, Request(100, 200), ports, ports, new DialogRuntimeOptions
            {
                MaxOperationFieldBytes = 6,
                MaxOperationsPerField = 1,
                MaxSemanticInputBytes = 6,
                MaxOperationsPerTransition = 1,
            }).Status);
        foreach (var options in new[]
        {
            new DialogRuntimeOptions { MaxOperationFieldBytes = 5 },
            new DialogRuntimeOptions { MaxOperationsPerField = 0 },
            new DialogRuntimeOptions { MaxSemanticInputBytes = 5 },
            new DialogRuntimeOptions { MaxOperationsPerTransition = 0 },
        })
        {
            if (options.MaxOperationsPerField == 0 || options.MaxOperationsPerTransition == 0)
            {
                Throws<ArgumentOutOfRangeException>(() => DialogTransitionRuntime.Start(document, Request(100, 200), ports, ports, options));
            }
            else
            {
                var result = DialogTransitionRuntime.Start(document, Request(100, 200), ports, ports, options);
                Assert.AreEqual(DialogFailureKind.SemanticInputLimitExceeded, result.Failure!.Kind);
            }
        }
    }

    [TestMethod]
    public void Command_failures_at_every_position_preserve_receipts_and_never_retry()
    {
        var document = DlgReader.Read(Bytes(
            Record("100", action: "QU 1 1; QU 2 2; QU 3 3")
            + Record("200", iq: "1", response: "300", action: "LF 1 1; LF 2 0")
            + Record("300", action: "RU 1000; RU 1001; RU 1002")));
        for (var ordinal = 1; ordinal <= 8; ordinal++)
        {
            var ports = new Ports { RejectAt = ordinal };
            var start = DialogTransitionRuntime.Start(document, Request(100, 200), ports, ports);
            var result = start.Status == DialogStartStatus.Failed
                ? new DialogSelectionResult(DialogSelectionStatus.Failed, start.Failure)
                : start.Session.Select(start.Session.Offers.Single().Token);

            Assert.AreEqual(DialogSelectionStatus.Failed, result.Status, $"ordinal {ordinal}");
            Assert.AreEqual(DialogFailureKind.CommandRejected, result.Failure!.Kind, $"ordinal {ordinal}");
            Assert.AreEqual(ordinal, start.Session.CommandAttempts.Count, $"ordinal {ordinal}");
            Assert.AreEqual(DialogCommandAttemptOutcome.Rejected, start.Session.CommandAttempts[^1].Outcome, $"ordinal {ordinal}");
            Assert.AreEqual(ordinal - 1, ports.Commands.Count, $"ordinal {ordinal}");
            Assert.AreEqual(new DialogNodeId(100), start.Session.CurrentNodeId, $"ordinal {ordinal}");
            Assert.AreEqual(DialogSelectionStatus.WrongPhase, start.Session.Select(new DialogOfferToken(new DialogResponseId(200), 0)).Status);
            Assert.AreEqual(ordinal - 1, ports.Commands.Count, $"ordinal {ordinal}");
        }
    }

    [TestMethod]
    public void Grammar_domains_separators_and_opaque_fields_are_bounded_without_text_decoding()
    {
        foreach (var action in new[]
        {
            "LF\t0,1",
            "QU(-2147483648:6)",
            "RU;2147483647",
            "lF 31 0; qU 2147483647 0; rU 1000",
        })
        {
            var ports = new Ports();
            var document = DlgReader.Read(Bytes(Record("100", text: "\x80\x01", alternateText: "\x7f", action: action) + Record("200", iq: "1", response: "0")));
            var start = DialogTransitionRuntime.Start(document, Request(100, 200), ports, ports);
            Assert.AreEqual(DialogStartStatus.AwaitingSelection, start.Status, action);
            CollectionAssert.AreEqual(new byte[] { 0x80, 0x01 }, document.Records[0].Text.Bytes.ToArray(), action);
            CollectionAssert.AreEqual(new byte[] { 0x7f }, document.Records[0].AlternateText.Bytes.ToArray(), action);
        }

        foreach (var action in new[]
        {
            "LF -1 0", "LF 32 0", "LF 1 2", "QU 1 -1", "QU 1 7", "RU 999",
            "QU 2147483648 0", "RU 2147483648", "LF 1", "RU 1000 0", "LF 1 0\x01",
        })
        {
            var ports = new Ports();
            var document = DlgReader.Read(Bytes(Record("100", action: action) + Record("200", iq: "1", response: "0")));
            var start = DialogTransitionRuntime.Start(document, Request(100, 200), ports, ports);
            Assert.AreEqual(DialogFailureKind.InvalidOperation, start.Failure!.Kind, action);
            Assert.AreEqual(0, ports.Commands.Count, action);
        }
    }

    [TestMethod]
    public void Serialized_grouping_copied_eligibility_iq_gate_and_single_edge_cycles_are_preserved()
    {
        var document = DlgReader.Read(Bytes(
            Record("100", action: "QU 1 1")
            + Record("900", iq: "1", condition: "LF 1 1", response: "300")
            + Record("200", iq: "1", response: "300")
            + Record("300", action: "RU 1000")
            + Record("400", iq: "1", response: "100")));
        var eligibility = new[]
        {
            new DialogResponseEligibility(new DialogResponseId(900), true, true),
            new DialogResponseEligibility(new DialogResponseId(200), true, false),
        };
        var ports = new Ports { Flags = { [1] = LocalFlagReadResult.Found(1) } };
        var start = DialogTransitionRuntime.Start(
            document,
            new DialogStartRequest(new DialogNodeId(100), new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)), eligibility),
            ports,
            ports);
        eligibility[0] = new DialogResponseEligibility(new DialogResponseId(900), false, false);

        CollectionAssert.AreEqual(new[] { 900 }, start.Session.Offers.Select(offer => offer.ResponseId.Value).ToArray());
        Assert.AreEqual(1, ports.QueryCount, "The IQ-gated response does not query LF.");
        Assert.AreEqual(DialogOfferRefreshStatus.Updated, start.Session.RefreshOffers().Status);
        Assert.AreEqual(2, ports.QueryCount, "Copied eligibility continues to permit only the first LF query.");
        var token = start.Session.Offers.Single().Token;
        var forged = new DialogOfferToken(new DialogResponseId(200), token.Generation);
        Assert.AreEqual(DialogSelectionStatus.NotOffered, start.Session.Select(forged).Status);
        Assert.AreEqual(DialogSelectionStatus.Completed, start.Session.Select(token).Status);
        Assert.AreEqual(new DialogNodeId(300), start.Session.CurrentNodeId);
        CollectionAssert.AreEqual(new[] { "QU:1:1:900", "RU:1000" }, ports.Commands);
        Assert.AreEqual(DialogSelectionStatus.WrongPhase, start.Session.Select(token).Status);

        Assert.AreEqual(DialogSelectionStatus.WrongPhase, start.Session.Select(forged).Status);
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
        public int? ThrowReadAt { get; init; }
        public int? InvalidReadAt { get; init; }
        public Exception? QueryException { get; init; }

        public LocalFlagReadResult Read(LocalFlagQuery query)
        {
            QueryCount++;
            if (ThrowOnRead)
            {
                throw new InvalidOperationException("Synthetic query failure.");
            }
            if (ThrowReadAt == QueryCount)
            {
                throw QueryException ?? new InvalidOperationException("Synthetic query failure.");
            }
            if (InvalidRead)
            {
                return default;
            }
            if (InvalidReadAt == QueryCount)
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

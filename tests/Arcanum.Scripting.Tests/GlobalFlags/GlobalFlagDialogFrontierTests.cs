using System.Text;
using Arcanum.Data;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.GlobalFlags;

[TestClass]
public sealed class GlobalFlagDialogFrontierTests
{
    [TestMethod]
    public void GF_remains_unsupported_in_entry_hidden_condition_hidden_action_and_target_before_ports()
    {
        var rows = new[]
        {
            Record(100, action: "GF 3 1") + Record(200, iq: "1", response: "300") + Record(300),
            Record(100, action: "QU 7 2") + Record(200, iq: "1", condition: "GF 3 1", response: "300") + Record(300),
            Record(100, action: "QU 7 2") + Record(200, iq: "1", action: "GF 3 1", response: "300") + Record(300),
            Record(100, action: "QU 7 2") + Record(200, iq: "1", response: "300") + Record(300, action: "GF 3 1"),
        };
        foreach (var row in rows)
        {
            var ports = new Ports();
            var result = DialogTransitionRuntime.Start(DlgReader.Read(Encoding.ASCII.GetBytes(row)), Request(200), ports, ports);
            Assert.AreEqual(DialogStartStatus.Failed, result.Status);
            Assert.AreEqual(DialogFailureKind.UnsupportedOperation, result.Failure!.Kind);
            Assert.AreEqual(0, ports.Calls);
            Assert.AreEqual(0, result.Session.CommandAttempts.Count);
        }
    }

    [TestMethod]
    public void Existing_cap5_limits_and_serialized_grouping_remain_unchanged()
    {
        var options = new DialogRuntimeOptions();
        Assert.AreEqual(5, options.MaxResponses);
        Assert.AreEqual(4096, options.MaxOperationFieldBytes);
        Assert.AreEqual(32, options.MaxOperationsPerField);
        Assert.AreEqual(128, options.MaxOperationsPerTransition);
        Assert.AreEqual(65536, options.MaxSemanticInputBytes);
        var ports = new Ports();
        var six = Record(100, action: "QU 7 2") + string.Concat(Enumerable.Range(200, 6).Select(id => Record(id, iq: "1", response: "0")));
        var result = DialogTransitionRuntime.Start(DlgReader.Read(Encoding.ASCII.GetBytes(six)), Request(200, 201, 202, 203, 204, 205), ports, ports);
        Assert.AreEqual(DialogFailureKind.ResponseLimitExceeded, result.Failure!.Kind);
        Assert.AreEqual(0, ports.Calls);
        var unsorted = Record(100) + Record(220, iq: "1", response: "0") + Record(210, iq: "1", response: "0") + Record(300) + Record(230, iq: "1", response: "0");
        var grouped = DialogTransitionRuntime.Start(DlgReader.Read(Encoding.ASCII.GetBytes(unsorted)),
            new(new(100), new(new(7), new(8), new(9)), new[] { new DialogResponseEligibility(new(220), true, true), new(new(210), true, true) }), ports, ports);
        Assert.AreEqual(DialogStartStatus.AwaitingSelection, grouped.Status);
        CollectionAssert.AreEqual(new[] { 220, 210 }, grouped.Session.Offers.Select(offer => offer.ResponseId.Value).ToArray());
        Assert.AreEqual(0, ports.Calls);
    }

    private static DialogStartRequest Request(params int[] ids) => new(new(100), new(new(7), new(8), new(9)),
        ids.Select(id => new DialogResponseEligibility(new(id), false, false)).ToArray());
    private static string Record(int id, string iq = "", string condition = "", string response = "", string action = "") =>
        $"{{{id}}}{{synthetic}}{{}}{{{iq}}}{{{condition}}}{{{response}}}{{{action}}}\n";

    private sealed class Ports : IDialogStateQueryPort, IDialogCommandPort
    {
        internal int Calls { get; private set; }
        public LocalFlagReadResult Read(LocalFlagQuery query) { Calls++; return LocalFlagReadResult.Found(0); }
        public DialogCommandResult Execute(LocalFlagWriteCommand command) { Calls++; return DialogCommandResult.Applied; }
        public DialogCommandResult Execute(QuestStateCommand command) { Calls++; return DialogCommandResult.Applied; }
        public DialogCommandResult Execute(RumorLogCommand command) { Calls++; return DialogCommandResult.Applied; }
    }
}

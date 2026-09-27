using System.Security.Cryptography;
using System.Text;
using Arcanum.Data;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Dialog;

[TestClass]
public sealed class DialogRuntimeRealDataTests
{
    private const string ArchiveHash = "cbadd76bb3df798fa25dcc05871af1ea751c1164fb1426dccc520cd7d9edc499";
    private const string MemberName = "dlg\\01478Erland.dlg";

    [TestMethod]
    [TestCategory("RealData")]
    [TestCategory("DlgRuntimeRealData")]
    public void Selected_document_uses_the_public_runtime_without_exposing_payload()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            Assert.Fail("DAT_ROOT is required for this opt-in real-data check; it is not a synthetic pass.");
        }
        var archivePath = Path.Combine(root, "arcanum3.dat");
        Assert.AreEqual(ArchiveHash, Hash(archivePath), "Archive SHA-256 mismatch before reads.");

        using (var archive = DatArchive.Open(archivePath, new DatReaderOptions { MaxDecodedEntryBytes = 512 * 1024 }))
        {
            var rawName = Encoding.ASCII.GetBytes(MemberName + "\0");
            var entries = archive.Entries.Where(entry => entry.RawNameBytes.SequenceEqual(rawName)).ToArray();
            Assert.AreEqual(1, entries.Length, "Selected member must be unique.");
            var document = DlgReader.Read(archive.ReadEntry(entries[0]));
            Assert.IsTrue(document.TryGetRecord(30, out var entry));
            Assert.IsTrue(document.TryGetRecord(31, out var response));
            Assert.IsTrue(document.TryGetRecord(50, out var target));
            Assert.AreEqual(DlgRecordRole.Npc, entry!.Role);
            Assert.AreEqual(DlgRecordRole.Player, response!.Role);
            Assert.AreEqual(DlgRecordRole.Npc, target!.Role);
            Assert.AreEqual(DlgResponseKind.Direct, response.ResponseKind);
            Assert.AreEqual(50, response.ResponseValue);

            var eligibility = document.Records
                .Skip(entry.SerializedIndex + 1)
                .TakeWhile(record => record.Role == DlgRecordRole.Player)
                .Select(record => new DialogResponseEligibility(new DialogResponseId(record.Id), true, true))
                .ToArray();
            CollectionAssert.AreEqual(new[] { 31 }, eligibility.Select(item => item.Response.Value).ToArray());

            var sessions = new List<(DialogStartResult Result, RealDataPorts Ports)>();
            foreach (var localFlag in new[] { 0, 1 })
            {
                var ports = new RealDataPorts(localFlag);
                var result = Start(document, eligibility, ports);

                Assert.AreEqual(DialogStartStatus.AwaitingSelection, result.Status);
                Assert.AreEqual(DialogSessionPhase.AwaitingSelection, result.Session.Phase);
                Assert.IsNull(result.Failure);
                Assert.AreEqual(1, ports.QueryCount);
                Assert.AreEqual(1, result.Session.CommandAttempts.Count);
                Assert.IsInstanceOfType<QuestStateCommand>(result.Session.CommandAttempts[0].Command);
                Assert.AreEqual(new DialogNodeId(30), result.Session.CommandAttempts[0].Source.RecordId);
                Assert.AreEqual(DlgFieldKind.Action, result.Session.CommandAttempts[0].Source.FieldKind);
                Assert.AreEqual(0, result.Session.CommandAttempts[0].Source.OperationOrdinal);
                var quest = (QuestStateCommand)result.Session.CommandAttempts[0].Command;
                Assert.AreEqual(new DialogParticipantId(2), quest.PlayerCharacter);
                Assert.AreEqual(new DialogParticipantId(3), quest.NpcContext);
                sessions.Add((result, ports));
            }

            CollectionAssert.AreEquivalent(new[] { 0, 1 }, sessions.Select(session => session.Result.Session.Offers.Count).ToArray());
            var hidden = sessions.Single(session => session.Result.Session.Offers.Count == 0);
            Assert.AreEqual(new DialogNodeId(30), hidden.Result.Session.CurrentNodeId);
            Assert.AreEqual(1, hidden.Result.Session.CommandAttempts.Count);

            var visible = sessions.Single(session => session.Result.Session.Offers.Count == 1);
            var offer = visible.Result.Session.Offers.Single();
            Assert.AreEqual(new DialogResponseId(31), offer.ResponseId);
            Assert.AreEqual(DialogTransitionKind.Direct, offer.TransitionKind);
            Assert.AreEqual(new DialogNodeId(50), offer.DirectTarget);
            Assert.AreEqual(DialogSelectionStatus.Completed, visible.Result.Session.Select(offer.Token).Status);
            Assert.AreEqual(DialogSessionPhase.CompletedAtFrontier, visible.Result.Session.Phase);
            Assert.AreEqual(new DialogNodeId(50), visible.Result.Session.CurrentNodeId);
            Assert.AreEqual(2, visible.Ports.QueryCount);
            CollectionAssert.AreEqual(
                new[] { typeof(QuestStateCommand), typeof(LocalFlagWriteCommand), typeof(RumorLogCommand) },
                visible.Result.Session.CommandAttempts.Select(attempt => attempt.Command.GetType()).ToArray());
            CollectionAssert.AreEqual(
                new[] { 30, 31, 50 },
                visible.Result.Session.CommandAttempts.Select(attempt => attempt.Source.RecordId.Value).ToArray());
            Assert.IsTrue(visible.Result.Session.CommandAttempts.All(attempt =>
                attempt.Source.FieldKind == DlgFieldKind.Action && attempt.Source.OperationOrdinal == 0));
            Assert.AreEqual(new DialogParticipantId(2), ((QuestStateCommand)visible.Result.Session.CommandAttempts[0].Command).PlayerCharacter);
            Assert.AreEqual(new DialogParticipantId(3), ((QuestStateCommand)visible.Result.Session.CommandAttempts[0].Command).NpcContext);
            Assert.AreEqual(new DialogId(1), ((LocalFlagWriteCommand)visible.Result.Session.CommandAttempts[1].Command).Dialog);
            Assert.AreEqual(new DialogParticipantId(3), ((LocalFlagWriteCommand)visible.Result.Session.CommandAttempts[1].Command).Npc);
            Assert.AreEqual(new DialogParticipantId(2), ((RumorLogCommand)visible.Result.Session.CommandAttempts[2].Command).PlayerCharacter);
        }

        Assert.AreEqual(ArchiveHash, Hash(archivePath), "Archive SHA-256 mismatch after reads.");
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static DialogStartResult Start(
        DlgDocument document,
        IReadOnlyList<DialogResponseEligibility> eligibility,
        RealDataPorts ports) =>
        DialogTransitionRuntime.Start(
            document,
            new DialogStartRequest(
                new DialogNodeId(30),
                new DialogParticipants(new DialogId(1), new DialogParticipantId(2), new DialogParticipantId(3)),
                eligibility),
            ports,
            ports);

    private sealed class RealDataPorts(int localFlag) : IDialogStateQueryPort, IDialogCommandPort
    {
        public int QueryCount { get; private set; }
        public LocalFlagReadResult Read(LocalFlagQuery query)
        {
            QueryCount++;
            return LocalFlagReadResult.Found(localFlag);
        }
        public DialogCommandResult Execute(LocalFlagWriteCommand command) => Apply(command);
        public DialogCommandResult Execute(QuestStateCommand command) => Apply(command);
        public DialogCommandResult Execute(RumorLogCommand command) => Apply(command);

        private static DialogCommandResult Apply(DialogCommand command) => DialogCommandResult.Applied;
    }
}

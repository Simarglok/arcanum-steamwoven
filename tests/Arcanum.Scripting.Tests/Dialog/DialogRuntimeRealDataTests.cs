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

            foreach (var localFlag in new[] { 0, 1 })
            {
                var ports = new RealDataPorts(localFlag);
                var result = Start(document, eligibility, ports);

                Assert.AreEqual(DialogStartStatus.Failed, result.Status);
                Assert.AreEqual(DialogSessionPhase.Failed, result.Session.Phase);
                Assert.AreEqual(DialogFailureKind.InvalidOperation, result.Failure!.Kind);
                Assert.AreEqual(new DialogNodeId(30), result.Failure.Source!.Value.RecordId);
                Assert.AreEqual(DlgFieldKind.Action, result.Failure.Source.Value.FieldKind);
                Assert.AreEqual(0, result.Session.Offers.Count);
                Assert.AreEqual(0, result.Session.CommandAttempts.Count);
                Assert.AreEqual(0, ports.QueryCount);
            }
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
        public DialogCommandResult Execute(LocalFlagWriteCommand command) => DialogCommandResult.Applied;
        public DialogCommandResult Execute(QuestStateCommand command) => DialogCommandResult.Applied;
        public DialogCommandResult Execute(RumorLogCommand command) => DialogCommandResult.Applied;
    }
}

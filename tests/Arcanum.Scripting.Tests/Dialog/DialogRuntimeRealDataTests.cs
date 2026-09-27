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

            var eligibility = document.Records
                .Skip(entry.SerializedIndex + 1)
                .TakeWhile(record => record.Role == DlgRecordRole.Player)
                .Select(record => new DialogResponseEligibility(new DialogResponseId(record.Id), true, true))
                .ToArray();
            foreach (var localFlag in new[] { 0, 1 })
            {
                var ports = new RealDataPorts(localFlag);
                var result = DialogTransitionRuntime.Start(
                    document,
                    new DialogStartRequest(
                        new DialogNodeId(30),
                        new DialogParticipants(new DialogId(1), new DialogParticipantId(2), new DialogParticipantId(3)),
                        eligibility),
                    ports,
                    ports);
                Assert.IsTrue(result.Status is DialogStartStatus.AwaitingSelection or DialogStartStatus.Failed);
                Assert.IsTrue(result.Session.CommandAttempts.All(attempt => attempt.Source.RecordId.Value is 30 or 31 or 50));
            }
        }

        Assert.AreEqual(ArchiveHash, Hash(archivePath), "Archive SHA-256 mismatch after reads.");
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed class RealDataPorts(int localFlag) : IDialogStateQueryPort, IDialogCommandPort
    {
        public LocalFlagReadResult Read(LocalFlagQuery query) => LocalFlagReadResult.Found(localFlag);
        public DialogCommandResult Execute(LocalFlagWriteCommand command) => DialogCommandResult.Applied;
        public DialogCommandResult Execute(QuestStateCommand command) => DialogCommandResult.Applied;
        public DialogCommandResult Execute(RumorLogCommand command) => DialogCommandResult.Applied;
    }
}

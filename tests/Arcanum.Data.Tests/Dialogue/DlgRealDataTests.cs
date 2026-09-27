using System.Security.Cryptography;
using System.Text;
using Arcanum.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests;

[TestClass]
public sealed class DlgRealDataTests
{
    private const string ArchiveHash = "cbadd76bb3df798fa25dcc05871af1ea751c1164fb1426dccc520cd7d9edc499";

    [TestMethod]
    [TestCategory("RealData")]
    [TestCategory("DlgRealData")]
    public void Selected_hashed_members_are_byte_preserving_and_structurally_validated()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            Assert.Fail("DAT_ROOT is required for the opt-in DLG real-data check; this is not a synthetic pass.");
        }
        var archivePath = Path.Combine(root, "arcanum3.dat");
        Assert.AreEqual(9_605_011L, new FileInfo(archivePath).Length, "Archive size mismatch.");
        Assert.AreEqual(ArchiveHash, Hash(archivePath), "Archive SHA-256 mismatch before reads.");

        using (var archive = DatArchive.Open(archivePath, new DatReaderOptions { MaxDecodedEntryBytes = 512 * 1024 }))
        {
            Verify(archive, "dlg\\01324Virgil.dlg", 160, 0x002u, 56_782u, 12_335u,
                "34a49a2c160cf8a97856cc0a9e48baa04fe0611c5d6bf05b5f52a7310f45e53a", 409, 103, 306);
            Verify(archive, "dlg\\01478Erland.dlg", 220, 0x002u, 4_346u, 1_290u,
                "0bdfb29d04cdc5702d8d6f0e0208077c3cec8e708d923162a1bb33dfc1d022cc", 30, null, null);
        }

        Assert.AreEqual(ArchiveHash, Hash(archivePath), "Archive SHA-256 mismatch after reads.");
    }

    private static void Verify(DatArchive archive, string name, int ordinal, uint flags, uint declared, uint stored, string payloadHash,
        int recordCount, int? npcCount, int? playerCount)
    {
        var rawName = Encoding.ASCII.GetBytes(name + "\0");
        var matches = archive.Entries.Where(entry => entry.RawNameBytes.SequenceEqual(rawName)).ToArray();
        Assert.AreEqual(1, matches.Length, "Selected raw member must be unique.");
        var entry = matches[0];
        Assert.AreEqual(ordinal, entry.Ordinal);
        Assert.AreEqual(flags, entry.Flags);
        Assert.AreEqual(declared, entry.DeclaredOutputSize);
        Assert.AreEqual(stored, entry.StoredSize);
        var payload = archive.ReadEntry(entry);
        var inputBefore = payload.ToArray();
        Assert.AreEqual(payloadHash, Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant());

        var document = DlgReader.Read(payload);
        Assert.AreEqual(recordCount, document.Records.Count);
        Assert.AreEqual(recordCount, document.Records.Select(record => record.Id).Distinct().Count());
        if (npcCount.HasValue)
        {
            Assert.AreEqual(npcCount.Value, document.Records.Count(record => record.Role == DlgRecordRole.Npc));
        }
        if (playerCount.HasValue)
        {
            Assert.AreEqual(playerCount.Value, document.Records.Count(record => record.Role == DlgRecordRole.Player));
        }
        foreach (var field in document.Records.SelectMany(record => record.Fields))
        {
            CollectionAssert.AreEqual(payload.AsSpan(field.PayloadOffset, field.Length).ToArray(), field.Bytes.ToArray());
        }
        CollectionAssert.AreEqual(inputBefore, payload);

        var validation = DlgReferenceValidator.Validate(document);
        var players = document.Records.Count(record => record.Role == DlgRecordRole.Player);
        Assert.AreEqual(players, validation.References.Count);
        Assert.AreEqual(players, validation.References.Count(reference => reference.Status == DlgReferenceStatus.Valid && reference.ResponseKind == DlgResponseKind.Direct)
            + validation.References.Count(reference => reference.ResponseKind == DlgResponseKind.Exit)
            + validation.References.Count(reference => reference.ResponseKind == DlgResponseKind.Special)
            + validation.References.Count(reference => reference.Status == DlgReferenceStatus.MissingTarget)
            + validation.References.Count(reference => reference.Status == DlgReferenceStatus.WrongTargetRole));
        foreach (var reference in validation.References.Where(reference => reference.Status == DlgReferenceStatus.Valid && reference.ResponseKind == DlgResponseKind.Direct))
        {
            Assert.IsTrue(document.TryGetRecord(reference.TargetId!.Value, out var target));
            Assert.AreEqual(DlgRecordRole.Npc, target!.Role);
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

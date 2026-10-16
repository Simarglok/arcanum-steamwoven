using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

[TestClass]
public sealed class SaveSlotTests
{
    private string _directory = null!;

    [TestInitialize]
    public void CreateDirectory() => _directory = Directory.CreateTempSubdirectory("arc-save-slot-").FullName;

    [TestCleanup]
    public void DeleteDirectory()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private GameSessionSaveSlot Slot(string? directory = null) => GameSessionSaveSlot.QuickSave(directory ?? _directory);

    [TestMethod]
    public void AWrittenSaveIsTheCodecTextAndRestoresTheSameSession()
    {
        using var owner = GoldenSession();
        var snapshot = owner.Capture();
        var slot = Slot(Path.Combine(_directory, "not", "yet", "created"));
        Assert.IsFalse(slot.Exists);

        var written = slot.Write(snapshot);
        Assert.AreEqual((SaveSlotFailure.None, true), (written.Failure, written.Saved), written.Detail);
        var text = GameSessionSnapshotCodec.Encode(snapshot);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes(text), File.ReadAllBytes(slot.Path), "The file is exactly the canonical codec text.");
        Assert.AreEqual(text.Length, written.Bytes);
        Assert.IsFalse(File.Exists(slot.TemporaryPath), "No temporary file is left behind.");

        var read = slot.Read();
        Assert.AreEqual(SaveSlotFailure.None, read.Failure, read.Detail);
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(read.Snapshot!));
        using var restored = Restore(read.Snapshot!);
        Assert.AreEqual(owner.QueryActor(), restored.QueryActor());
        Assert.AreEqual(owner.Version.Revision, restored.Version.Revision);
    }

    [TestMethod]
    public void AMissingSaveOrDirectoryIsNoSave()
    {
        var missing = Slot().Read();
        Assert.AreEqual((SaveSlotFailure.NoSave, (GameSessionSnapshot?)null), (missing.Failure, missing.Snapshot));
        Assert.AreEqual(SaveSlotFailure.NoSave, Slot(Path.Combine(_directory, "absent")).Read().Failure);
    }

    [TestMethod]
    public void ASecondSaveReplacesTheFirst()
    {
        using var owner = GoldenSession();
        var slot = Slot();
        Assert.IsTrue(slot.Write(owner.Capture()).Saved);
        var moved = owner.PlaceActor(owner.Version, Start with { Cell = new(101, 201) });
        Assert.AreEqual(GameSessionDisposition.Changed, moved.Disposition);
        Assert.IsTrue(slot.Write(owner.Capture()).Saved);
        Assert.AreEqual(new Arcanum.Simulation.Movement.CellCoord(101, 201), slot.Read().Snapshot!.Actor.Cell);
    }

    [TestMethod]
    public void AFailedReplaceKeepsThePreviousSaveAndRemovesTheTemporaryFile()
    {
        using var owner = GoldenSession();
        var slot = Slot();
        Assert.IsTrue(slot.Write(owner.Capture()).Saved);
        var before = File.ReadAllBytes(slot.Path);
        owner.PlaceActor(owner.Version, Start with { Cell = new(5, 5) });

        string? observed = null;
        slot.BeforeReplaceForTest = temporary =>
        {
            observed = temporary;
            Assert.IsTrue(File.Exists(temporary), "The verified temporary file exists before the replace.");
            throw new IOException("simulated crash before the replace");
        };
        var failed = slot.Write(owner.Capture());
        Assert.AreEqual((SaveSlotFailure.WriteFailed, "IOException"), (failed.Failure, failed.Detail));
        Assert.IsFalse(failed.Saved);
        Assert.AreEqual(slot.TemporaryPath, observed);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(slot.Path), "The previous save is untouched.");
        Assert.IsFalse(File.Exists(slot.TemporaryPath));
        Assert.AreEqual(Start, slot.Read().Snapshot!.Actor);
    }

    [TestMethod]
    public void AnUnwritableSlotFailsWithoutThrowing()
    {
        using var owner = GoldenSession();
        // The slot's directory is an existing file, so neither the directory nor the temporary file can be made.
        var blocker = Path.Combine(_directory, "blocker");
        File.WriteAllText(blocker, "x");
        var slot = Slot(blocker);
        var failed = slot.Write(owner.Capture());
        Assert.AreEqual(SaveSlotFailure.WriteFailed, failed.Failure);
        Assert.AreEqual("x", File.ReadAllText(blocker));
    }

    [TestMethod]
    public void AStaleTemporaryFileFromAnInterruptedSaveIsReplaced()
    {
        using var owner = GoldenSession();
        var slot = Slot();
        File.WriteAllText(slot.TemporaryPath, "{\"half\":");
        Assert.IsTrue(slot.Write(owner.Capture()).Saved);
        Assert.IsFalse(File.Exists(slot.TemporaryPath));
        Assert.AreEqual(SaveSlotFailure.None, slot.Read().Failure);
    }

    [TestMethod]
    public void DamagedOrForeignFilesAreTypedFailures()
    {
        using var owner = GoldenSession();
        var slot = Slot();
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());

        SaveSlotReadResult ReadBytes(byte[] bytes)
        {
            File.WriteAllBytes(slot.Path, bytes);
            var read = slot.Read();
            Assert.IsNull(read.Snapshot);
            return read;
        }
        SaveSlotReadResult ReadText(string value) => ReadBytes(Encoding.UTF8.GetBytes(value));

        var truncated = ReadText(text[..(text.Length / 2)]);
        Assert.AreEqual((SaveSlotFailure.Corrupt, SnapshotDecodeFailure.MalformedJson, "$"), (truncated.Failure, truncated.DecodeFailure, truncated.DecodePath));
        Assert.AreEqual((SaveSlotFailure.Corrupt, SnapshotDecodeFailure.MalformedJson), (ReadText("").Failure, ReadText("").DecodeFailure));
        Assert.AreEqual(SaveSlotFailure.NotUtf8, ReadBytes([0x7B, 0xFF, 0xFE, 0x7D]).Failure);
        var bom = ReadBytes([0xEF, 0xBB, 0xBF, .. Encoding.ASCII.GetBytes(text)]);
        Assert.AreEqual(SaveSlotFailure.Corrupt, bom.Failure, "Only the codec's own BOM-free text is a save.");

        var newer = new Regex("\"schemaVersion\":\\d+").Replace(text, "\"schemaVersion\":999", 1);
        var unsupported = ReadText(newer);
        Assert.AreEqual((SaveSlotFailure.Corrupt, SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion"),
            (unsupported.Failure, unsupported.DecodeFailure, unsupported.DecodePath));
        var foreign = ReadText(text.Replace(GameSessionSnapshotCodec.Format, "some-other-format"));
        Assert.AreEqual((SaveSlotFailure.Corrupt, SnapshotDecodeFailure.WrongFormat), (foreign.Failure, foreign.DecodeFailure));
        var negative = ReadText(text.Replace("\"revision\":2,", "\"revision\":-2,"));
        Assert.AreEqual((SaveSlotFailure.Corrupt, SnapshotDecodeFailure.OutOfRange, "$.revision"), (negative.Failure, negative.DecodeFailure, negative.DecodePath));
    }

    [TestMethod]
    public void AFileOverTheByteLimitIsNotRead()
    {
        var slot = Slot();
        using (var stream = File.Create(slot.Path)) stream.SetLength(GameSessionSaveSlot.MaxBytes + 1L);
        var read = slot.Read();
        Assert.AreEqual((SaveSlotFailure.TooLarge, (GameSessionSnapshot?)null), (read.Failure, read.Snapshot));
    }

    [TestMethod]
    public void ASlotNeedsAnAbsoluteFilePath()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new GameSessionSaveSlot("saves/quicksave.json"));
        Assert.ThrowsExactly<ArgumentException>(() => new GameSessionSaveSlot(_directory + Path.DirectorySeparatorChar));
        Assert.ThrowsExactly<ArgumentException>(() => new GameSessionSaveSlot(" "));
        Assert.AreEqual(Path.Combine(_directory, "quicksave.json"), Slot().Path);
    }
}

using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arcanum.Data;
using ICSharpCode.SharpZipLib.Zip.Compression;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests;

[TestClass]
public sealed class DatArchiveTests
{
    [TestMethod]
    public void Open_preserves_order_duplicates_and_raw_metadata()
    {
        var archiveBytes = Fixture.Create(
            (new byte[] { (byte)'A', 0 }, 1u, 1u, new byte[] { 7 }, new byte[] { 1, 2, 3, 4 }),
            (new byte[] { (byte)'a', 0 }, 1u, 1u, new byte[] { 8 }, new byte[] { 5, 6, 7, 8 }),
            (new byte[] { 0xc1, 0 }, 1u, 1u, new byte[] { 9 }, new byte[] { 9, 8, 7, 6 }),
            (new byte[] { (byte)'A', 0 }, 1u, 1u, new byte[] { 10 }, new byte[] { 4, 3, 2, 1 }));
        using var archive = DatArchive.Open(new MemoryStream(archiveBytes), leaveOpen: true);

        Assert.AreEqual(4, archive.Entries.Count);
        CollectionAssert.AreEqual(new byte[] { (byte)'A', 0 }, archive.Entries[0].RawNameBytes.ToArray());
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, archive.Entries[0].OpaqueBytes.ToArray());
        CollectionAssert.AreEqual(new byte[] { 5, 6, 7, 8 }, archive.Entries[1].OpaqueBytes.ToArray());
        CollectionAssert.AreEqual(new byte[] { (byte)'a', 0 }, archive.Entries[1].RawNameBytes.ToArray());
        CollectionAssert.AreEqual(new byte[] { 0xc1, 0 }, archive.Entries[2].RawNameBytes.ToArray());
        CollectionAssert.AreEqual(new byte[] { (byte)'A', 0 }, archive.Entries[3].RawNameBytes.ToArray());
        Assert.AreEqual(0, archive.Entries[0].Ordinal);
        Assert.AreEqual(3, archive.Entries[3].Ordinal);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("1TAD"), archive.Footer.MarkerBytes.ToArray());
        CollectionAssert.AreEqual(new byte[16], archive.Footer.OpaqueBytes.ToArray());
    }

    [TestMethod]
    public void ReadEntry_reads_exact_plain_and_zlib_and_rejects_trailing_or_length_mismatch()
    {
        var compressed = Fixture.Zlib("zlib payload");
        var bytes = Fixture.Create(
            (Fixture.Name("plain"), 1u, 3u, Encoding.ASCII.GetBytes("raw"), new byte[4]),
            (Fixture.Name("packed"), 2u, 12u, compressed, new byte[4]),
            (Fixture.Name("trail"), 2u, 1u, Fixture.Zlib("x").Concat(new byte[] { 1 }).ToArray(), new byte[4]),
            (Fixture.Name("short"), 2u, 4u, Fixture.Zlib("x"), new byte[4]),
            (Fixture.Name("excess"), 2u, 1u, Fixture.Zlib("xx"), new byte[4]));
        using var archive = DatArchive.Open(new MemoryStream(bytes), leaveOpen: true);

        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("raw"), archive.ReadEntry(archive.Entries[0]));
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("zlib payload"), archive.ReadEntry(archive.Entries[1]));
        Throws<DatFormatException>(() => archive.ReadEntry(archive.Entries[2]));
        Throws<DatFormatException>(() => archive.ReadEntry(archive.Entries[3]));
        Throws<DatFormatException>(() => archive.ReadEntry(archive.Entries[4]));
    }

    [TestMethod]
    public void Zlib_valid_empty_and_nonempty_entries_finish_with_exact_output()
    {
        var bytes = Fixture.Create(
            (Fixture.Name("plain-empty"), 1u, 0u, Array.Empty<byte>(), new byte[4]),
            (Fixture.Name("empty"), 2u, 0u, Fixture.Zlib(string.Empty), new byte[4]),
            (Fixture.Name("value"), 2u, 5u, Fixture.Zlib("value"), new byte[4]));
        using var archive = DatArchive.Open(new MemoryStream(bytes), leaveOpen: true);

        CollectionAssert.AreEqual(Array.Empty<byte>(), archive.ReadEntry(archive.Entries[0]));
        CollectionAssert.AreEqual(Array.Empty<byte>(), archive.ReadEntry(archive.Entries[1]));
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("value"), archive.ReadEntry(archive.Entries[2]));
    }

    [TestMethod]
    public void Zlib_positive_probe_observes_finished_before_needing_input()
    {
        var stored = Fixture.Zlib("x");
        var inflater = new Inflater(noHeader: false);
        inflater.SetInput(stored);
        var output = new byte[1];
        var scratch = new byte[1];

        Assert.AreEqual(1, inflater.Inflate(output, 0, output.Length));
        Assert.AreEqual(0, inflater.Inflate(scratch, 0, scratch.Length));
        Assert.IsTrue(inflater.IsFinished);
        Assert.IsTrue(inflater.IsNeedingInput);
        Assert.AreEqual(1L, inflater.TotalOut);
        Assert.AreEqual(0, inflater.RemainingInput);
    }

    [TestMethod]
    public void Zlib_rejects_malformed_truncated_trailing_and_length_errors()
    {
        var valid = Fixture.Zlib("xy");
        var badHeader = valid.ToArray();
        badHeader[0] = 0;
        var badBody = valid.ToArray();
        badBody[2] = 0xff;
        var badTrailer = valid.ToArray();
        badTrailer[^1] ^= 1;

        AssertZlibFormatFailure(badHeader, 2);
        AssertZlibFormatFailure(badBody, 2);
        AssertZlibFormatFailure(badTrailer, 2);
        for (var length = 0; length < valid.Length; length++)
        {
            AssertZlibFormatFailure(valid[..length], 2);
        }
        AssertZlibFormatFailure(valid.Concat(new byte[] { 1 }).ToArray(), 2);
        AssertZlibFormatFailure(valid.Concat(new byte[] { 1, 2 }).ToArray(), 2);
        AssertZlibFormatFailure(valid.Concat(Fixture.Zlib("z")).ToArray(), 2);
        AssertZlibFormatFailure(valid, 1);
        AssertZlibFormatFailure(valid, 3);
        AssertZlibFormatFailure(Fixture.Zlib("x"), 0);
    }

    [TestMethod]
    public void ReadEntry_rejects_unsupported_combinations_and_plain_mismatch_before_payload_read()
    {
        var bytes = Fixture.Create(
            (Fixture.Name("combo"), 3u, 1u, new byte[] { 1 }, new byte[4]),
            (Fixture.Name("plain"), 1u, 2u, new byte[] { 1 }, new byte[4]));
        using var stream = new InstrumentedStream(bytes);
        using var archive = DatArchive.Open(stream, leaveOpen: true);
        stream.SetObservedRanges((0, archive.Footer.IndexOffset));
        stream.ResetObservedReads();

        Throws<DatUnsupportedEntryException>(() => archive.ReadEntry(archive.Entries[0]));
        Throws<DatFormatException>(() => archive.ReadEntry(archive.Entries[1]));
        Assert.AreEqual(0L, stream.ObservedReadBytes);
    }

    [TestMethod]
    public void Open_rejects_structural_errors_and_limits()
    {
        var valid = Fixture.Create((Fixture.Name("one"), 1u, 1u, new byte[] { 1 }, new byte[4]));
        var badMarker = valid.ToArray();
        badMarker[^12] = (byte)'X';
        Throws<DatUnsupportedFormatException>(() => DatArchive.Open(new MemoryStream(badMarker)));
        Throws<DatFormatException>(() => DatArchive.Open(new MemoryStream(valid[..27])));

        var noNul = valid.ToArray();
        noNul[5] = 1;
        Throws<DatFormatException>(() => DatArchive.Open(new MemoryStream(noNul)));

        var options = new DatReaderOptions { MaxEntryCount = 1 };
        var two = Fixture.Create((Fixture.Name("a"), 1u, 1u, new byte[] { 1 }, new byte[4]), (Fixture.Name("b"), 1u, 1u, new byte[] { 2 }, new byte[4]));
        Throws<DatLimitExceededException>(() => DatArchive.Open(new MemoryStream(two), options));
        Throws<DatLimitExceededException>(() => DatArchive.Open(new MemoryStream(valid), new DatReaderOptions { MaxIndexBytes = 1 }));
    }

    [TestMethod]
    public void Entry_payload_limits_are_checked_before_reading()
    {
        var bytes = Fixture.Create((Fixture.Name("one"), 1u, 2u, new byte[] { 1, 2 }, new byte[4]));
        using var archive = DatArchive.Open(new MemoryStream(bytes), new DatReaderOptions { MaxStoredEntryBytes = 1 }, leaveOpen: true);
        Throws<DatLimitExceededException>(() => archive.ReadEntry(archive.Entries[0]));
    }

    [TestMethod]
    public void Ownership_disposal_and_foreign_entry_behave_as_documented()
    {
        var bytes = Fixture.Create((Fixture.Name("one"), 1u, 1u, new byte[] { 1 }, new byte[4]));
        var retained = new MemoryStream(bytes);
        using (var archive = DatArchive.Open(retained, leaveOpen: true)) { }
        Assert.IsTrue(retained.CanRead);
        retained.Dispose();

        var owned = new MemoryStream(bytes);
        using (var archive = DatArchive.Open(owned)) { }
        Assert.IsFalse(owned.CanRead);

        using var first = DatArchive.Open(new MemoryStream(bytes), leaveOpen: true);
        using var second = DatArchive.Open(new MemoryStream(bytes), leaveOpen: true);
        Throws<ArgumentException>(() => first.ReadEntry(second.Entries[0]));
        first.Dispose();
        Throws<ObjectDisposedException>(() => first.ReadEntry(first.Entries[0]));
    }

    [TestMethod]
    public void Open_disposes_owned_stream_after_validation_failure()
    {
        var owned = new TrackingStream(canRead: false, canSeek: true);
        Throws<ArgumentException>(() => DatArchive.Open(owned));
        Assert.IsTrue(owned.Disposed);

        var retained = new TrackingStream(canRead: true, canSeek: false);
        Throws<ArgumentException>(() => DatArchive.Open(retained, leaveOpen: true));
        Assert.IsFalse(retained.Disposed);
    }

    [TestMethod]
    public void ReadEntry_classifies_directory_and_every_supported_unsupported_flag_before_bounds_or_payload_reads()
    {
        var flags = new uint[] { 0u, 3u, 0x100u, 0x200u, 0x400u, 0x401u, 0x402u, 0x800u };
        var bytes = Fixture.Create(flags.Select((flag, ordinal) =>
            (Fixture.Name($"f{ordinal}"), flag, 1u, new byte[] { (byte)ordinal }, new byte[] { 1, 2, 3, (byte)ordinal })).ToArray());
        Fixture.SetEntryOffset(bytes, 4, uint.MaxValue);
        Fixture.SetEntryOffset(bytes, 6, uint.MaxValue);
        using var stream = new InstrumentedStream(bytes);
        using var archive = DatArchive.Open(stream, leaveOpen: true);
        stream.SetObservedRanges((0, archive.Footer.IndexOffset));
        stream.ResetObservedReads();

        for (var ordinal = 0; ordinal < archive.Entries.Count; ordinal++)
        {
            Assert.AreEqual(flags[ordinal], archive.Entries[ordinal].Flags);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, (byte)ordinal }, archive.Entries[ordinal].OpaqueBytes.ToArray());
            Throws<DatUnsupportedEntryException>(() => archive.ReadEntry(archive.Entries[ordinal]));
        }

        Assert.AreEqual(0L, stream.ObservedReadBytes, "Unsupported metadata must not read payload bytes.");
    }

    [TestMethod]
    public void ReadEntry_reads_only_the_selected_payload_range()
    {
        var bytes = Fixture.Create(
            (Fixture.Name("first"), 1u, 3u, new byte[] { 1, 2, 3 }, new byte[4]),
            (Fixture.Name("second"), 1u, 4u, new byte[] { 4, 5, 6, 7 }, new byte[4]));
        using var stream = new InstrumentedStream(bytes);
        using var archive = DatArchive.Open(stream, leaveOpen: true);
        stream.SetObservedRanges(archive.Entries.Select(entry => ((long)entry.Offset, (long)entry.Offset + entry.StoredSize)));
        stream.ResetObservedReads();

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, archive.ReadEntry(archive.Entries[0]));

        Assert.AreEqual(3L, stream.ObservedReadsFor((long)archive.Entries[0].Offset, (long)archive.Entries[0].Offset + 3));
        Assert.AreEqual(0L, stream.ObservedReadsFor((long)archive.Entries[1].Offset, (long)archive.Entries[1].Offset + 4));
    }

    [TestMethod]
    public void Open_rejects_footer_count_index_name_fixed_tail_and_index_garbage_truncation()
    {
        var valid = Fixture.Create((Fixture.Name("one"), 1u, 1u, new byte[] { 1 }, new byte[4]));
        Throws<DatFormatException>(() => DatArchive.Open(new MemoryStream(valid[..27])));
        Throws<DatFormatException>(() => DatArchive.Open(new MemoryStream(Fixture.CreateRaw(Array.Empty<byte>(), new byte[] { 1, 0, 0 }))));
        Throws<DatFormatException>(() => DatArchive.Open(new MemoryStream(Fixture.CreateRaw(Array.Empty<byte>(), Fixture.U32(1).Concat(Fixture.U32(4)).Concat(new byte[] { (byte)'a', 0 }).ToArray()))));
        Throws<DatFormatException>(() => DatArchive.Open(new MemoryStream(Fixture.CreateRaw(Array.Empty<byte>(), Fixture.U32(1).Concat(Fixture.U32(2)).Concat(Fixture.Name("a")).Concat(new byte[19]).ToArray()))));

        var badName = valid.ToArray();
        Fixture.SetNameTerminator(badName, 0, 1);
        Throws<DatFormatException>(() => DatArchive.Open(new MemoryStream(badName)));

        var garbage = Fixture.InsertBeforeFooter(valid, 0xff);
        Throws<DatFormatException>(() => DatArchive.Open(new MemoryStream(garbage)));
        Throws<DatFormatException>(() => DatArchive.Open(new MemoryStream(Fixture.WithTail(valid, 27))));
        Throws<DatFormatException>(() => DatArchive.Open(new MemoryStream(Fixture.WithTail(valid, checked((uint)valid.Length + 1)))));
    }

    [TestMethod]
    public void Open_rejects_unreachable_counts_payload_ranges_and_bad_markers()
    {
        var valid = Fixture.Create((Fixture.Name("one"), 1u, 1u, new byte[] { 1 }, new byte[4]));
        var unreachable = valid.ToArray();
        Fixture.SetEntryCount(unreachable, 2);
        Throws<DatFormatException>(() => DatArchive.Open(new MemoryStream(unreachable)));

        var outOfPayload = valid.ToArray();
        Fixture.SetEntryOffset(outOfPayload, 0, uint.MaxValue);
        Throws<DatFormatException>(() => DatArchive.Open(new MemoryStream(outOfPayload)));

        var badMarker = valid.ToArray();
        badMarker[^12] = (byte)'X';
        Throws<DatUnsupportedFormatException>(() => DatArchive.Open(new MemoryStream(badMarker)));
    }

    [TestMethod]
    public void Open_and_ReadEntry_enforce_all_configured_limits_before_payload_reads()
    {
        var one = Fixture.Create((Fixture.Name("one"), 1u, 2u, new byte[] { 1, 2 }, new byte[4]));
        Throws<DatLimitExceededException>(() => DatArchive.Open(new MemoryStream(one), new DatReaderOptions { MaxArchiveBytes = checked((uint)one.Length - 1) }));
        Throws<DatLimitExceededException>(() => DatArchive.Open(new MemoryStream(one), new DatReaderOptions { MaxIndexBytes = 1 }));
        Throws<DatLimitExceededException>(() => DatArchive.Open(new MemoryStream(one), new DatReaderOptions { MaxNameBytes = 1 }));
        var two = Fixture.Create((Fixture.Name("a"), 1u, 1u, new byte[] { 1 }, new byte[4]), (Fixture.Name("b"), 1u, 1u, new byte[] { 2 }, new byte[4]));
        Throws<DatLimitExceededException>(() => DatArchive.Open(new MemoryStream(two), new DatReaderOptions { MaxEntryCount = 1 }));
        Throws<DatLimitExceededException>(() => DatArchive.Open(new MemoryStream(two), new DatReaderOptions { MaxTotalNameBytes = 3 }));

        foreach (var options in new[]
        {
            new DatReaderOptions { MaxArchiveBytes = 0 }, new DatReaderOptions { MaxIndexBytes = 0 },
            new DatReaderOptions { MaxEntryCount = 0 }, new DatReaderOptions { MaxNameBytes = 0 },
            new DatReaderOptions { MaxTotalNameBytes = 0 }, new DatReaderOptions { MaxStoredEntryBytes = 0 },
            new DatReaderOptions { MaxDecodedEntryBytes = 0 },
        })
        {
            Throws<ArgumentOutOfRangeException>(() => DatArchive.Open(new MemoryStream(one), options));
        }

        using var stream = new InstrumentedStream(one);
        using var archive = DatArchive.Open(stream, new DatReaderOptions { MaxStoredEntryBytes = 1 }, leaveOpen: true);
        stream.SetObservedRanges((0, archive.Footer.IndexOffset));
        stream.ResetObservedReads();
        Throws<DatLimitExceededException>(() => archive.ReadEntry(archive.Entries[0]));
        Assert.AreEqual(0L, stream.ObservedReadBytes);
        using var decodedArchive = DatArchive.Open(new MemoryStream(one), new DatReaderOptions { MaxDecodedEntryBytes = 1 }, leaveOpen: true);
        Throws<DatLimitExceededException>(() => decodedArchive.ReadEntry(decodedArchive.Entries[0]));
    }

    [TestMethod]
    public void Open_preserves_io_errors_and_ReadEntry_reports_truncated_payload()
    {
        var bytes = Fixture.Create((Fixture.Name("one"), 1u, 1u, new byte[] { 1 }, new byte[4]));
        Throws<IOException>(() => DatArchive.Open(new ThrowingReadStream(bytes), leaveOpen: true));

        using var stream = new PayloadEofStream(bytes);
        using var archive = DatArchive.Open(stream, leaveOpen: true);
        stream.PayloadStart = archive.Entries[0].Offset;
        stream.ReturnEofForPayload = true;
        Throws<DatFormatException>(() => archive.ReadEntry(archive.Entries[0]));
    }

    [TestMethod]
    public void File_and_stream_ownership_leave_open_dispose_and_foreign_entry_are_exact()
    {
        var bytes = Fixture.Create((Fixture.Name("one"), 1u, 1u, new byte[] { 1 }, new byte[4]));
        var path = Path.Combine(Path.GetTempPath(), $"arcanum-data-{Guid.NewGuid():N}.dat");
        try
        {
            File.WriteAllBytes(path, bytes);
            var fileArchive = DatArchive.Open(path);
            fileArchive.Dispose();
            fileArchive.Dispose();
            Throws<ObjectDisposedException>(() => fileArchive.ReadEntry(fileArchive.Entries[0]));
        }
        finally
        {
            File.Delete(path);
        }

        var retained = new TrackingStream(bytes, canRead: true, canSeek: true);
        using (var archive = DatArchive.Open(retained, leaveOpen: true)) { }
        Assert.IsFalse(retained.Disposed);
        var owned = new TrackingStream(bytes, canRead: true, canSeek: true);
        using (var archive = DatArchive.Open(owned)) { }
        Assert.IsTrue(owned.Disposed);
        using var first = DatArchive.Open(new MemoryStream(bytes), leaveOpen: true);
        using var second = DatArchive.Open(new MemoryStream(bytes), leaveOpen: true);
        Throws<ArgumentException>(() => first.ReadEntry(second.Entries[0]));
    }

    [TestMethod]
    [TestCategory("RealData")]
    public void R1_local_inventory_matches_documented_integrity_and_probe_metadata()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            Assert.Fail("DAT_ROOT is required for the opt-in R1 real-data check; this is not a synthetic pass.");
        }
        var expectations = new[]
        {
            new R1Archive("arcanum1.dat", 447470816, "6dd9d10ef0932e02fc88f0eb5367b1bc37275db51d60bd68ac9bd7ef2d478576"),
            new R1Archive("arcanum2.dat", 147991122, "4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d"),
            new R1Archive("arcanum3.dat", 9605011, "cbadd76bb3df798fa25dcc05871af1ea751c1164fb1426dccc520cd7d9edc499"),
            new R1Archive("Arcanum4.dat", 447614, "975d43854fca8b7dd74e2b12ef2ff3fe6d3c0f99dc541151d33001469f89710c"),
            new R1Archive("tig.dat", 57149, "c9b48eb6396c514eba3805fb4847df67db3c67ef9f13a79ea21bbf7dd329a09b"),
            new R1Archive("modules/Arcanum.dat", 355938305, "8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f"),
            new R1Archive("modules/Vormantown.dat", 16060074, "163a9560ad7d684f486167bf91ad6da0fdef8bd7fd1082fc054084611da04345"),
            new R1Archive("modules/Arcanum.PATCH0", 18804, "d9a3313f71d78115dcac489296bf3d8a5801dd55b20267bd6017b7a898364bce"),
        };
        var before = expectations.Select(expectation => InputFingerprint.Create(Path.Combine(root, expectation.RelativePath))).ToArray();
        var totalEntries = 0;
        foreach (var expectation in expectations)
        {
            var path = Path.Combine(root, expectation.RelativePath);
            Assert.AreEqual(expectation.Bytes, new FileInfo(path).Length, "R1 size mismatch.");
            Assert.AreEqual(expectation.Sha256, InputFingerprint.Hash(path), "R1 SHA-256 mismatch.");
            using var archive = DatArchive.Open(path);
            totalEntries += archive.Entries.Count;
            CompareWithProbe(path, archive);
        }
        Assert.AreEqual(107_889, totalEntries);

        using var arcanum1 = DatArchive.Open(Path.Combine(root, expectations[0].RelativePath));
        using var arcanum2 = DatArchive.Open(Path.Combine(root, expectations[1].RelativePath));
        using var module = DatArchive.Open(Path.Combine(root, expectations[5].RelativePath));
        using var patch = DatArchive.Open(Path.Combine(root, expectations[7].RelativePath));
        AssertProbePayload(Path.Combine(root, expectations[0].RelativePath), arcanum1, 0, 1_186);
        AssertProbePayload(Path.Combine(root, expectations[1].RelativePath), arcanum2, 4_247, 68);
        AssertProbePayload(Path.Combine(root, expectations[5].RelativePath), module, 8_022, 4);
        AssertProbePayload(Path.Combine(root, expectations[5].RelativePath), module, 3, 20_156);
        AssertProbePayload(Path.Combine(root, expectations[7].RelativePath), patch, 1, 16_270);
        CollectionAssert.AreEqual(before, expectations.Select(expectation => InputFingerprint.Create(Path.Combine(root, expectation.RelativePath))).ToArray());
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception.GetType() == typeof(TException))
        {
            return;
        }
        catch (Exception exception)
        {
            Assert.Fail($"Expected {typeof(TException).Name}, got {exception.GetType().Name}.");
        }
        Assert.Fail($"Expected {typeof(TException).Name}.");
    }

    private static void AssertZlibFormatFailure(byte[] stored, uint declaredSize)
    {
        var bytes = Fixture.Create((Fixture.Name("zlib"), 2u, declaredSize, stored, new byte[4]));
        using var archive = DatArchive.Open(new MemoryStream(bytes), leaveOpen: true);
        Throws<DatFormatException>(() => archive.ReadEntry(archive.Entries[0]));
    }

    private static void CompareWithProbe(string archivePath, DatArchive archive)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("python3")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        process.StartInfo.ArgumentList.Add("-B");
        process.StartInfo.ArgumentList.Add(Path.Combine(FindRepositoryRoot(), "tests", "Arcanum.Data.Tests", "dat_probe_adapter.py"));
        process.StartInfo.ArgumentList.Add(archivePath);
        Assert.IsTrue(process.Start(), "Could not start the local Python probe adapter.");
        using var reader = new BinaryReader(process.StandardOutput.BaseStream, Encoding.UTF8, leaveOpen: true);
        using var footer = ReadProbeRecord(reader);
        Assert.IsNotNull(footer);
        Assert.AreEqual("f", footer.RootElement.GetProperty("k").GetString());
        CollectionAssert.AreEqual(archive.Footer.OpaqueBytes.ToArray(), Convert.FromBase64String(footer.RootElement.GetProperty("opaque").GetString()!));
        CollectionAssert.AreEqual(archive.Footer.MarkerBytes.ToArray(), Convert.FromBase64String(footer.RootElement.GetProperty("marker").GetString()!));
        Assert.AreEqual(archive.Footer.NameBytesHint, footer.RootElement.GetProperty("hint").GetUInt32());
        Assert.AreEqual(archive.Footer.TailRegionBytes, footer.RootElement.GetProperty("tail").GetUInt32());
        Assert.AreEqual(archive.Footer.IndexOffset, footer.RootElement.GetProperty("index").GetInt64());
        foreach (var entry in archive.Entries)
        {
            using var record = ReadProbeRecord(reader);
            Assert.IsNotNull(record, "The Python probe ended before the C# index.");
            var data = record.RootElement;
            Assert.AreEqual("e", data.GetProperty("k").GetString());
            Assert.AreEqual(entry.Ordinal, data.GetProperty("ord").GetInt32());
            CollectionAssert.AreEqual(entry.RawNameBytes.ToArray(), Convert.FromBase64String(data.GetProperty("name").GetString()!));
            CollectionAssert.AreEqual(entry.OpaqueBytes.ToArray(), Convert.FromBase64String(data.GetProperty("opaque").GetString()!));
            Assert.AreEqual(entry.Flags, data.GetProperty("flags").GetUInt32());
            Assert.AreEqual(entry.DeclaredOutputSize, data.GetProperty("declared").GetUInt32());
            Assert.AreEqual(entry.StoredSize, data.GetProperty("stored").GetUInt32());
            Assert.AreEqual(entry.Offset, data.GetProperty("offset").GetUInt32());
        }
        Assert.IsNull(ReadProbeRecord(reader), "The Python probe emitted excess index records.");
        var error = process.StandardError.ReadToEnd();
        Assert.IsTrue(process.WaitForExit(30_000), "The Python probe adapter timed out.");
        Assert.AreEqual(0, process.ExitCode, $"The Python probe adapter failed: {error}");
    }

    private static JsonDocument? ReadProbeRecord(BinaryReader reader)
    {
        var lengthBytes = reader.ReadBytes(sizeof(uint));
        if (lengthBytes.Length == 0)
        {
            return null;
        }
        Assert.AreEqual(sizeof(uint), lengthBytes.Length, "The Python probe record length is truncated.");
        var length = BitConverter.ToUInt32(lengthBytes);
        Assert.IsTrue(length > 0 && length <= 1024 * 1024, "The Python probe record size is invalid.");
        var bytes = reader.ReadBytes(checked((int)length));
        Assert.AreEqual((int)length, bytes.Length, "The Python probe record is truncated.");
        return JsonDocument.Parse(bytes);
    }

    private static void AssertProbePayload(string archivePath, DatArchive archive, int ordinal, int expectedLength)
    {
        var payload = archive.ReadEntry(archive.Entries[ordinal]);
        Assert.AreEqual(expectedLength, payload.Length);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("python3")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        process.StartInfo.ArgumentList.Add("-B");
        process.StartInfo.ArgumentList.Add(Path.Combine(FindRepositoryRoot(), "tests", "Arcanum.Data.Tests", "dat_probe_adapter.py"));
        process.StartInfo.ArgumentList.Add(archivePath);
        process.StartInfo.ArgumentList.Add("--payload");
        process.StartInfo.ArgumentList.Add(ordinal.ToString());
        Assert.IsTrue(process.Start(), "Could not start the local Python payload adapter.");
        using var record = ReadProbeRecord(new BinaryReader(process.StandardOutput.BaseStream, Encoding.UTF8, leaveOpen: true));
        Assert.IsNotNull(record);
        Assert.AreEqual("p", record.RootElement.GetProperty("k").GetString());
        CollectionAssert.AreEqual(payload, Convert.FromBase64String(record.RootElement.GetProperty("payload").GetString()!));
        var error = process.StandardError.ReadToEnd();
        Assert.IsTrue(process.WaitForExit(30_000), "The Python payload adapter timed out.");
        Assert.AreEqual(0, process.ExitCode, $"The Python payload adapter failed: {error}");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "tools", "research", "dat_probe.py")))
            {
                return directory.FullName;
            }
        }
        Assert.Fail("Could not locate the repository's research probe.");
        return string.Empty;
    }

    private sealed record R1Archive(string RelativePath, long Bytes, string Sha256);

    private sealed record InputFingerprint(long Bytes, string Sha256)
    {
        public static InputFingerprint Create(string path) => new(new FileInfo(path).Length, Hash(path));

        public static string Hash(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha256 = SHA256.Create();
            return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
        }
    }

    private static class Fixture
    {
        public static byte[] Name(string value) => Encoding.ASCII.GetBytes(value + "\0");

        public static byte[] Zlib(string value)
        {
            using var output = new MemoryStream();
            using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                zlib.Write(Encoding.ASCII.GetBytes(value));
            }
            return output.ToArray();
        }

        public static byte[] Create(params (byte[] Name, uint Flags, uint DeclaredSize, byte[] Stored, byte[] Opaque)[] entries)
        {
            using var output = new MemoryStream();
            var offsets = new List<uint>();
            foreach (var entry in entries)
            {
                offsets.Add(checked((uint)output.Position));
                output.Write(entry.Stored);
            }
            var index = new MemoryStream();
            WriteU32(index, checked((uint)entries.Length));
            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                WriteU32(index, checked((uint)entry.Name.Length));
                index.Write(entry.Name);
                index.Write(entry.Opaque);
                WriteU32(index, entry.Flags);
                WriteU32(index, entry.DeclaredSize);
                WriteU32(index, checked((uint)entry.Stored.Length));
                WriteU32(index, offsets[i]);
            }
            index.Position = 0;
            index.CopyTo(output);
            output.Write(new byte[16]);
            output.Write(Encoding.ASCII.GetBytes("1TAD"));
            WriteU32(output, checked((uint)entries.Sum(entry => entry.Name.Length)));
            WriteU32(output, checked((uint)(index.Length + 28)));
            return output.ToArray();
        }

        public static byte[] U32(uint value)
        {
            var bytes = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            return bytes;
        }

        public static byte[] CreateRaw(byte[] payload, byte[] index)
        {
            using var output = new MemoryStream();
            output.Write(payload);
            output.Write(index);
            output.Write(new byte[16]);
            output.Write(Encoding.ASCII.GetBytes("1TAD"));
            WriteU32(output, 0);
            WriteU32(output, checked((uint)(index.Length + 28)));
            return output.ToArray();
        }

        public static byte[] WithTail(byte[] archive, uint tail)
        {
            var result = archive.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(result.Length - sizeof(uint)), tail);
            return result;
        }

        public static byte[] InsertBeforeFooter(byte[] archive, byte value)
        {
            var result = new byte[archive.Length + 1];
            Array.Copy(archive, 0, result, 0, archive.Length - 28);
            result[archive.Length - 28] = value;
            Array.Copy(archive, archive.Length - 28, result, archive.Length - 27, 28);
            var oldTail = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(archive.Length - sizeof(uint)));
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(result.Length - sizeof(uint)), checked(oldTail + 1));
            return result;
        }

        public static void SetEntryCount(byte[] archive, uint count)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(IndexStart(archive), sizeof(uint)), count);
        }

        public static void SetNameTerminator(byte[] archive, int ordinal, byte value)
        {
            var (_, nameStart, nameLength) = EntryPosition(archive, ordinal);
            archive[nameStart + nameLength - 1] = value;
        }

        public static void SetEntryOffset(byte[] archive, int ordinal, uint offset)
        {
            var (fieldsStart, _, _) = EntryPosition(archive, ordinal);
            BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(fieldsStart + 16, sizeof(uint)), offset);
        }

        private static int IndexStart(byte[] archive)
        {
            var tail = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(archive.Length - sizeof(uint)));
            return checked(archive.Length - (int)tail);
        }

        private static (int FieldsStart, int NameStart, int NameLength) EntryPosition(byte[] archive, int ordinal)
        {
            var cursor = IndexStart(archive) + sizeof(uint);
            for (var i = 0; i <= ordinal; i++)
            {
                var nameLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(cursor, sizeof(uint))));
                var nameStart = cursor + sizeof(uint);
                var fieldsStart = nameStart + nameLength;
                if (i == ordinal)
                {
                    return (fieldsStart, nameStart, nameLength);
                }
                cursor = fieldsStart + 20;
            }
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        private static void WriteU32(Stream stream, uint value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 24));
        }
    }

    private class InstrumentedStream : Stream
    {
        private readonly MemoryStream _inner;
        private readonly Dictionary<(long Start, long End), long> _observed = new();

        public InstrumentedStream(byte[] bytes)
        {
            _inner = new MemoryStream(bytes, writable: false);
        }

        public long ObservedReadBytes => _observed.Values.Sum();
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public void SetObservedRanges(params (long Start, long End)[] ranges) => SetObservedRanges((IEnumerable<(long Start, long End)>)ranges);
        public void SetObservedRanges(IEnumerable<(long Start, long End)> ranges)
        {
            _observed.Clear();
            foreach (var range in ranges)
            {
                _observed.Add(range, 0);
            }
        }

        public void ResetObservedReads()
        {
            foreach (var range in _observed.Keys.ToArray())
            {
                _observed[range] = 0;
            }
        }

        public long ObservedReadsFor(long start, long end) => _observed[(start, end)];
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var start = _inner.Position;
            var read = _inner.Read(buffer);
            Track(start, read);
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected void Track(long start, long length)
        {
            var end = start + length;
            foreach (var range in _observed.Keys.ToArray())
            {
                var overlap = Math.Max(0, Math.Min(end, range.End) - Math.Max(start, range.Start));
                _observed[range] += overlap;
            }
        }
    }

    private sealed class PayloadEofStream : InstrumentedStream
    {
        public PayloadEofStream(byte[] bytes) : base(bytes) { }
        public long PayloadStart { get; set; }
        public bool ReturnEofForPayload { get; set; }

        public override int Read(Span<byte> buffer)
        {
            return ReturnEofForPayload && Position >= PayloadStart ? 0 : base.Read(buffer);
        }
    }

    private sealed class ThrowingReadStream : Stream
    {
        private readonly MemoryStream _inner;

        public ThrowingReadStream(byte[] bytes) => _inner = new MemoryStream(bytes, writable: false);
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Injected read failure.");
        public override int Read(Span<byte> buffer) => throw new IOException("Injected read failure.");
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class TrackingStream : Stream
    {
        private readonly bool _canRead;
        private readonly bool _canSeek;
        private readonly MemoryStream? _inner;

        public TrackingStream(bool canRead, bool canSeek) : this(Array.Empty<byte>(), canRead, canSeek) { }

        public TrackingStream(byte[] bytes, bool canRead, bool canSeek)
        {
            _canRead = canRead;
            _canSeek = canSeek;
            _inner = canRead && canSeek ? new MemoryStream(bytes, writable: false) : null;
        }

        public bool Disposed { get; private set; }
        public override bool CanRead => _canRead;
        public override bool CanSeek => _canSeek;
        public override bool CanWrite => false;
        public override long Length => _inner?.Length ?? 0;
        public override long Position { get => _inner?.Position ?? 0; set { if (_inner is null) throw new NotSupportedException(); _inner.Position = value; } }
        public override void Flush() => _inner?.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner?.Read(buffer, offset, count) ?? throw new NotSupportedException();
        public override int Read(Span<byte> buffer) => _inner?.Read(buffer) ?? throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => _inner?.Seek(offset, origin) ?? throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            if (disposing)
            {
                _inner?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

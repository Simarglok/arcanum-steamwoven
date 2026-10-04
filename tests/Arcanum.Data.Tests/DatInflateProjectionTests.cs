using System.Buffers.Binary;
using System.IO.Compression;
using Arcanum.Data.Tests.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests;

[TestClass]
public sealed class DatInflateProjectionTests
{
    private sealed class Admission(long limit = long.MaxValue) : IDatEntryAdmission
    {
        internal long Total;
        internal int Calls;
        public void Admit(long bytes)
        {
            Calls++;
            if (bytes < 0 || bytes > limit - Total) throw new InvalidOperationException("Admission denied.");
            Total += bytes;
        }
    }

    [TestMethod]
    public void Literal_dynamic_repeat_and_empty_blocks_preserve_stock_bytes()
    {
        // Independent RFC1951 emitter, verified separately by Python zlib; no
        // production table/allocator constants supply the expected payloads.
        Compare(Convert.FromHexString("780105e0010500000000206cebffa91200420042"), [65]);
        Compare(Convert.FromHexString("780104e0010500000000206cebffa992003ca00000000000846dfd3f5509c0030a0000000040d8d6ff5365017840010000000008dbfa7faa0200c50083"), [65, 65]);
        Compare([0x78,0x01,0x03,0x00,0x00,0x00,0x00,0x01], []); // fixed empty
        Compare([0x78,0x01,0x01,0x00,0x00,0xff,0xff,0x00,0x00,0x00,0x01], []); // stored empty
        Compare([0x78,0x01,0x01,0x01,0x00,0xfe,0xff,0x41,0x00,0x42,0x00,0x42], [65]);
    }

    [TestMethod]
    public void Deep_canonical_trees_cover_secondary_table_allocations()
    {
        // Complete binary comb trees emitted independently; maximum lengths
        // 9 and 15 straddle the stock root/secondary-table boundary.
        Compare(Convert.FromHexString("780105e00190244992244922b1a8792400000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000024eadf7f000d000a"), [0,1,8]);
        Compare(Convert.FromHexString("780105e00190244992244922b1a87964f5ec3d0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000003ceafff7ff0700130010"), [0,1,14]);
    }

    [TestMethod]
    public void Repeated_flushed_dynamic_blocks_are_covered_cumulatively()
    {
        var plain = new byte[524288];
        var random = new Random(713);
        using var packed = new MemoryStream();
        using (var zlib = new ZLibStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            for (var block = 0; block < 128; block++)
            {
                for (var i = 0; i < 4096; i++) plain[block * 4096 + i] = (byte)(random.Next(16) + block % 8 * 16);
                zlib.Write(plain.AsSpan(block * 4096, 4096)); zlib.Flush();
            }
        }
        var stored = packed.ToArray();
        Assert.IsTrue(stored.Length < 1048576);
        var reservation = Compare(stored, plain);
        // Measurement checks the source-derived envelope, never defines it.
        // Fixtures/archive/wrapper are allocated before this measured region.
        using var archive = DatArchive.Open(new MemoryStream(Dat(stored, plain.Length)));
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var admission = new Admission();
            var before = GC.GetAllocatedBytesForCurrentThread();
            var decoded = archive.ReadEntry(archive.Entries[0], admission);
            var observed = GC.GetAllocatedBytesForCurrentThread() - before;
            CollectionAssert.AreEqual(plain, decoded);
            Assert.AreEqual(reservation, admission.Total);
            Console.WriteLine($"allocation attempt={attempt} stored={stored.Length} decoded={plain.Length} observed={observed} admitted={admission.Total}");
            Assert.IsTrue(observed <= admission.Total, $"Managed allocation {observed} exceeds admission {admission.Total}.");
        }
    }

    [TestMethod]
    public void Malformed_headers_truncation_trailer_output_and_trailing_data_fail_closed()
    {
        byte[] valid = [0x78,0x01,0x01,0x01,0x00,0xfe,0xff,0x41,0x00,0x42,0x00,0x42];
        var cases = Enumerable.Range(0, valid.Length).Select(n => valid[..n]).ToList();
        var badHeader = valid.ToArray(); badHeader[0] = 0; cases.Add(badHeader);
        var reserved = valid.ToArray(); reserved[2] = 7; cases.Add(reserved);
        var badCount = valid.ToArray(); badCount[5] ^= 1; cases.Add(badCount);
        var badChecksum = valid.ToArray(); badChecksum[^1] ^= 1; cases.Add(badChecksum);
        cases.Add([.. valid, 1]); cases.Add([.. valid, .. valid]);
        cases.Add([0x78,0x20,0,0,0,0]); // dictionary header
        foreach (var bytes in cases)
        {
            using var archive = DatArchive.Open(new MemoryStream(Dat(bytes, 1)));
            AreaIdentityTests.Throws<DatFormatException>(() => archive.ReadEntry(archive.Entries[0]));
            AreaIdentityTests.Throws<DatFormatException>(() => archive.ReadEntry(archive.Entries[0], new Admission()));
        }
        foreach (var declared in new[] { 0, 2 })
        {
            using var archive = DatArchive.Open(new MemoryStream(Dat(valid, declared)));
            AreaIdentityTests.Throws<DatFormatException>(() => archive.ReadEntry(archive.Entries[0]));
            AreaIdentityTests.Throws<DatFormatException>(() => archive.ReadEntry(archive.Entries[0], new Admission()));
        }
    }

    [TestMethod]
    public void Admission_rejects_before_stored_read_and_before_inflater_output_work()
    {
        byte[] stored = [0x78,0x01,0x01,0x01,0x00,0xfe,0xff,0x41,0x00,0x42,0x00,0x42];
        using var stream = new ReadTrace(Dat(stored, 1));
        using var archive = DatArchive.Open(stream, leaveOpen: true);
        stream.Observe = true;
        var first = new Admission(0);
        AreaIdentityTests.Throws<InvalidOperationException>(() => archive.ReadEntry(archive.Entries[0], first));
        Assert.AreEqual(0, stream.PayloadReads); Assert.AreEqual(0L, first.Total);
        var second = new Admission(stored.Length + 32L);
        AreaIdentityTests.Throws<InvalidOperationException>(() => archive.ReadEntry(archive.Entries[0], second));
        Assert.AreEqual(1, stream.PayloadReads); Assert.AreEqual(stored.Length, stream.PayloadBytes);
        Assert.AreEqual(2, second.Calls); Assert.AreEqual(stored.Length + 32L, second.Total);
        var full = new Admission(); archive.ReadEntry(archive.Entries[0], full);
        var equal = new Admission(full.Total); CollectionAssert.AreEqual(new byte[] {65}, archive.ReadEntry(archive.Entries[0], equal));
        var lower = new Admission(full.Total - 1);
        AreaIdentityTests.Throws<InvalidOperationException>(() => archive.ReadEntry(archive.Entries[0], lower));
        Assert.AreEqual(3, lower.Calls);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var before = stream.PayloadReads;
        AreaIdentityTests.Throws<OperationCanceledException>(() => archive.ReadEntry(archive.Entries[0], new Admission(), cancelled.Token));
        Assert.AreEqual(before, stream.PayloadReads);
    }

    private static long Compare(byte[] stored, byte[] expected)
    {
        using var archive = DatArchive.Open(new MemoryStream(Dat(stored, expected.Length)));
        CollectionAssert.AreEqual(expected, archive.ReadEntry(archive.Entries[0]));
        var admission = new Admission();
        CollectionAssert.AreEqual(expected, archive.ReadEntry(archive.Entries[0], admission));
        return admission.Total;
    }
    private static byte[] Dat(byte[] stored, int length)
    {
        using var stream = new MemoryStream(); stream.Write(stored);
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write(1); writer.Write(2); writer.Write(new byte[] {120,0}); writer.Write(0);
        writer.Write(2); writer.Write(length); writer.Write(stored.Length); writer.Write(0);
        var footer = new byte[28]; "1TAD"u8.CopyTo(footer.AsSpan(16));
        BinaryPrimitives.WriteUInt32LittleEndian(footer.AsSpan(24), 58); writer.Write(footer);
        return stream.ToArray();
    }
    private sealed class ReadTrace(byte[] bytes) : MemoryStream(bytes)
    {
        internal bool Observe;
        internal int PayloadReads, PayloadBytes;
        public override int Read(Span<byte> buffer)
        {
            if (Observe) PayloadReads++;
            var result = base.Read(buffer);
            if (Observe) PayloadBytes += result;
            return result;
        }
    }
}

using System.IO.Compression;
using System.Text;
using Arcanum.Data;
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
            (new byte[] { (byte)'A', 0 }, 0x400u, 0u, Array.Empty<byte>(), new byte[] { 5, 6, 7, 8 }));
        using var archive = DatArchive.Open(new MemoryStream(archiveBytes), leaveOpen: true);

        Assert.AreEqual(2, archive.Entries.Count);
        CollectionAssert.AreEqual(new byte[] { (byte)'A', 0 }, archive.Entries[0].RawNameBytes.ToArray());
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, archive.Entries[0].OpaqueBytes.ToArray());
        CollectionAssert.AreEqual(new byte[] { 5, 6, 7, 8 }, archive.Entries[1].OpaqueBytes.ToArray());
        Assert.AreEqual(0, archive.Entries[0].Ordinal);
        Assert.AreEqual(1, archive.Entries[1].Ordinal);
        Assert.AreEqual(0x400u, archive.Entries[1].Flags);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("1TAD"), archive.Footer.MarkerBytes.ToArray());
        CollectionAssert.AreEqual(new byte[16], archive.Footer.OpaqueBytes.ToArray());
        Throws<DatUnsupportedEntryException>(() => archive.ReadEntry(archive.Entries[1]));
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
    public void ReadEntry_rejects_unsupported_combinations_and_plain_mismatch_before_payload_read()
    {
        var bytes = Fixture.Create(
            (Fixture.Name("combo"), 3u, 1u, new byte[] { 1 }, new byte[4]),
            (Fixture.Name("plain"), 1u, 2u, new byte[] { 1 }, new byte[4]));
        using var archive = DatArchive.Open(new MemoryStream(bytes), leaveOpen: true);

        Throws<DatUnsupportedEntryException>(() => archive.ReadEntry(archive.Entries[0]));
        Throws<DatFormatException>(() => archive.ReadEntry(archive.Entries[1]));
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
    [TestCategory("RealData")]
    public void R1_local_inventory_matches_the_documented_aggregate_and_selected_reads()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            Assert.Fail("DAT_ROOT is required for the opt-in R1 real-data check; this is not a synthetic pass.");
        }
        var paths = new[]
        {
            "arcanum1.dat", "arcanum2.dat", "arcanum3.dat", "Arcanum4.dat", "tig.dat",
            "modules/Arcanum.dat", "modules/Vormantown.dat", "modules/Arcanum.PATCH0",
        };
        var totalEntries = 0;
        foreach (var relativePath in paths)
        {
            using var archive = DatArchive.Open(Path.Combine(root, relativePath));
            totalEntries += archive.Entries.Count;
        }
        Assert.AreEqual(107_889, totalEntries);

        using var arcanum1 = DatArchive.Open(Path.Combine(root, paths[0]));
        using var arcanum2 = DatArchive.Open(Path.Combine(root, paths[1]));
        using var module = DatArchive.Open(Path.Combine(root, paths[5]));
        using var patch = DatArchive.Open(Path.Combine(root, paths[7]));
        Assert.AreEqual(1_186, arcanum1.ReadEntry(arcanum1.Entries[0]).Length);
        Assert.AreEqual(68, arcanum2.ReadEntry(arcanum2.Entries[4_247]).Length);
        Assert.AreEqual(4, module.ReadEntry(module.Entries[8_022]).Length);
        Assert.AreEqual(20_156, module.ReadEntry(module.Entries[3]).Length);
        Assert.AreEqual(16_270, patch.ReadEntry(patch.Entries[1]).Length);
    }

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
        catch (Exception exception)
        {
            Assert.Fail($"Expected {typeof(TException).Name}, got {exception.GetType().Name}.");
        }
        Assert.Fail($"Expected {typeof(TException).Name}.");
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

        private static void WriteU32(Stream stream, uint value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 24));
        }
    }
}

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Arcanum.Data.Terrain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests;

[TestClass]
public sealed class TerrainLoaderTests
{
    [TestMethod]
    public void Inspect_and_load_resolve_fixed_4096_tile_and_facade_records()
    {
        var sector = Sector(Enumerable.Range(0, 4096).Select(index => index % 2 == 0 ? 0x000001C0u : 0xB0000000u).ToArray());
        var art = Art();
        using var module = DatArchive.Open(new MemoryStream(Dat(("maps\\fixture\\1.sec", sector))), leaveOpen: true);
        using var resource = DatArchive.Open(new MemoryStream(Dat(
            ("art\\tile\\tilename.mes", Ascii("{0}{aaa}{100}{bbb}{200}{ccc}{300}{ddd}{400}{000000}")),
            ("art\\facade\\facadename.mes", Ascii("{0}{zero}")),
            ("ART/TILE/AAABSE0A.ART", art), ("ART/FACADE/ZERO.ART", art))), leaveOpen: true);
        var selector = new TerrainSectorSelector("maps\\fixture\\1.sec", "fixture", 0, 0, 1, 100, 200, 64, 64);
        var source = new TerrainResourceSource(resource, "fixture-source", "art\\tile\\tilename.mes", "art\\facade\\facadename.mes", TerrainPathProfile.CeNonWindowsMacOs);

        var report = TerrainLoader.Inspect(module, selector, source);
        var complete = TerrainLoader.Load(module, selector, source);

        Assert.IsTrue(report.IsComplete);
        Assert.AreEqual(4096, report.Outcomes.Count);
        Assert.AreEqual(4096, complete.Records.Count);
        Assert.AreEqual(TerrainKind.Tile, complete.Records[0].Kind);
        Assert.AreEqual(TerrainKind.Facade, complete.Records[1].Kind);
        Assert.AreEqual(0x000001C0u, complete.Records[0].LookupId);
        Assert.AreEqual(0xB0000000u, complete.Records[1].LookupId);
        Assert.AreEqual(100, complete.Records[0].WorldX);
        Assert.AreEqual(200, complete.Records[0].WorldY);
        Assert.AreEqual(101, complete.Records[1].WorldX);
        Assert.AreEqual(200, complete.Records[1].WorldY);
        Assert.AreEqual(TerrainKind.Tile, complete.DiagnosticOrder[0].Kind);
    }

    [TestMethod]
    public void Inspect_reports_per_cell_missing_resource_and_load_carries_report()
    {
        var sector = Sector(Enumerable.Repeat(0x000001C0u, 4096).ToArray());
        using var module = DatArchive.Open(new MemoryStream(Dat(("maps\\fixture\\1.sec", sector))), leaveOpen: true);
        using var resource = DatArchive.Open(new MemoryStream(Dat(
            ("art\\tile\\tilename.mes", Ascii("{0}{aaa}{100}{bbb}{200}{ccc}{300}{ddd}{400}{000000}")),
            ("art\\facade\\facadename.mes", Ascii("{0}{zero}")))), leaveOpen: true);
        var selector = new TerrainSectorSelector("maps\\fixture\\1.sec", "fixture", 0, 0, 1, 0, 0, 64, 64);
        var source = new TerrainResourceSource(resource, "fixture-source", "art\\tile\\tilename.mes", "art\\facade\\facadename.mes", TerrainPathProfile.CeNonWindowsMacOs);
        var report = TerrainLoader.Inspect(module, selector, source);
        Assert.IsFalse(report.IsComplete); Assert.AreEqual(4096, report.Issues.Count); Assert.AreEqual(TerrainIssueCode.MissingResource, report.Issues[0].Code);
        TerrainIncompleteException? exception = null;
        try { _ = TerrainLoader.Load(module, selector, source); }
        catch (TerrainIncompleteException caught) { exception = caught; }
        Assert.IsNotNull(exception);
        Assert.AreEqual(4096, exception.Report.Issues.Count);
    }

    [TestMethod]
    public void Geometry_applies_crop_anchor_and_raw_tile_flip_without_facade_flip()
    {
        var tile = TerrainGeometry.Place(TerrainKind.Tile, 0, 0, 0xC1, 40, 30, new TerrainRect(-10, -10, 100, 100));
        Assert.IsNotNull(tile); Assert.AreEqual(new TerrainRect(1, 0, 78, 40), tile.Value.Footprint); Assert.AreEqual(new TerrainRect(0, 0, 40, 30), tile.Value.Source); Assert.IsTrue(tile.Value.FlipX);
        var facade = TerrainGeometry.Place(TerrainKind.Facade, 1, 0, 0xB4000000, 78, 40, new TerrainRect(-100, -100, 300, 300));
        Assert.IsNotNull(facade); Assert.AreEqual(new TerrainRect(-39, 20, 80, 40), facade.Value.Footprint); Assert.IsFalse(facade.Value.FlipX);
        Assert.IsNull(TerrainGeometry.Place(TerrainKind.Tile, 0, 0, 0, 40, 30, new TerrainRect(80, 50, 1, 1)));
    }

    [TestMethod]
    [TestCategory("RealData")]
    public void RealData_crash_site_is_immutable_and_complete_when_explicit_inputs_are_supplied()
    {
        var modulePath = Environment.GetEnvironmentVariable("ARCANUM_TERRAIN_MODULE");
        var resourcePath = Environment.GetEnvironmentVariable("ARCANUM_TERRAIN_RESOURCE");
        if (string.IsNullOrWhiteSpace(modulePath) || string.IsNullOrWhiteSpace(resourcePath)) Assert.Fail("ARCANUM_TERRAIN_MODULE and ARCANUM_TERRAIN_RESOURCE are required for this opt-in test.");
        var moduleBefore = Hash(modulePath!); var resourceBefore = Hash(resourcePath!);
        Assert.AreEqual("8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f", moduleBefore);
        Assert.AreEqual("4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d", resourceBefore);
        using var module = DatArchive.Open(modulePath!); using var resource = DatArchive.Open(resourcePath!);
        var report = TerrainLoader.Inspect(module,
            new TerrainSectorSelector("maps\\Arcanum1-024-fixed\\86570436012.sec", "Arcanum1-024-fixed", 1452, 1290, 86570436012, 92928, 82560, 64, 64, new TerrainEntryExpectation(463, 2, 47291)),
            new TerrainResourceSource(resource, "real-data", "art\\tile\\tilename.mes", "art\\facade\\facadename.mes", TerrainPathProfile.CeNonWindowsMacOs));
        Assert.IsTrue(report.IsComplete); Assert.AreEqual(4096, report.ResolvedRecords.Count); Assert.AreEqual(3033, report.ResolvedRecords.Count(record => record.Kind == TerrainKind.Tile)); Assert.AreEqual(1063, report.ResolvedRecords.Count(record => record.Kind == TerrainKind.Facade));
        Assert.AreEqual(moduleBefore, Hash(modulePath!)); Assert.AreEqual(resourceBefore, Hash(resourcePath!));
    }

    private static byte[] Sector(uint[] words)
    {
        var bytes = new byte[4 + (words.Length * 4) + 4 + 4 + 4];
        for (var i = 0; i < words.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4 + (i * 4)), words[i]);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4 + (words.Length * 4)), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8 + (words.Length * 4)), 0xAA0000);
        return bytes;
    }
    private static byte[] Art()
    {
        var bytes = new byte[132 + 1024 + 28 + 1];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 8); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32), 1); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 1);
        var frame = 132 + 1024; BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame), 1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 4), 1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 8), 1); bytes[^1] = 1;
        return bytes;
    }
    private static byte[] Dat(params (string Name, byte[] Bytes)[] entries)
    {
        using var output = new MemoryStream(); var offsets = new List<uint>();
        foreach (var entry in entries) { offsets.Add(checked((uint)output.Position)); output.Write(entry.Bytes); }
        using var index = new MemoryStream(); WriteU32(index, checked((uint)entries.Length));
        for (var i = 0; i < entries.Length; i++) { var name = Ascii(entries[i].Name + "\0"); WriteU32(index, checked((uint)name.Length)); index.Write(name); index.Write(new byte[4]); WriteU32(index, 1); WriteU32(index, checked((uint)entries[i].Bytes.Length)); WriteU32(index, checked((uint)entries[i].Bytes.Length)); WriteU32(index, offsets[i]); }
        index.Position = 0; index.CopyTo(output); output.Write(new byte[16]); output.Write("1TAD"u8); WriteU32(output, checked((uint)entries.Sum(e => e.Name.Length + 1))); WriteU32(output, checked((uint)(index.Length + 28))); return output.ToArray();
    }
    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant(); }
    private static void WriteU32(Stream stream, uint value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); stream.Write(bytes); }
}

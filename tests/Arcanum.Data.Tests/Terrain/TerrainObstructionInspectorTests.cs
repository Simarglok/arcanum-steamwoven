using System.Buffers.Binary;
using System.Text;
using Arcanum.Data;
using Arcanum.Data.Terrain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests;

[TestClass]
public sealed class TerrainObstructionInspectorTests
{
    [TestMethod]
    public void Exact_little_endian_mask_reports_independent_component_boundaries()
    {
        var words = new uint[4096];
        var mask = new uint[128];
        foreach (var ordinal in new[] { 0, 31, 32, 63, 64, 4095 }) mask[ordinal / 32] |= 1u << (ordinal % 32);
        using var module = Open(("maps\\fixture\\mask.sec", Sector(words, 0xAA0004, mask)));
        var report = TerrainObstructionInspector.InspectSector(module, "fixture-module", Selector("maps\\fixture\\mask.sec"), null);

        foreach (var (x, y) in new[] { (0, 0), (31, 0), (32, 0), (63, 0), (0, 1), (63, 63) })
            Assert.AreEqual(TerrainObstructionDisposition.Blocked, report.GetCell(x, y).SerializedBlockMask.Disposition);
        Assert.AreEqual(TerrainObstructionDisposition.NoBlock, report.GetCell(30, 0).SerializedBlockMask.Disposition);
        Assert.AreEqual(TerrainObstructionSupportedness.Unknown, report.GetCell(0, 0).TileNameRule.Supportedness);
        AssertThrows<ArgumentOutOfRangeException>(() => report.GetCell(-1, 0));
        AssertThrows<ArgumentOutOfRangeException>(() => report.GetCell(64, 0));

        using var oldModule = Open(("maps\\fixture\\old.sec", Sector(words, 0xAA0003, null)));
        var oldReport = TerrainObstructionInspector.InspectSector(oldModule, "fixture-module", Selector("maps\\fixture\\old.sec"), null);
        Assert.AreEqual(TerrainObstructionSupportedness.Unknown, oldReport.GetCell(0, 0).SerializedBlockMask.Supportedness);
        Assert.IsNull(oldReport.GetCell(0, 0).SerializedBlockMask.Disposition);
    }

    [TestMethod]
    public void Facade_tile_name_modes_and_category_mapping_remain_independent()
    {
        var words = new uint[4096];
        words[0] = 0xB0000001; // facade bit 0 set
        words[1] = 0xB0000002; // facade bit 0 clear, unrelated bits included
        words[2] = Tile(0, outdoor: true, flippable: true);
        words[3] = Tile(1, outdoor: true, flippable: false);
        words[4] = Tile(2, outdoor: false, flippable: true);
        words[5] = Tile(3, outdoor: false, flippable: false);
        words[6] = 0xA0000000;
        using var module = Open(("maps\\fixture\\rules.sec", Sector(words, 0xAA0004, new uint[128])));
        using var source = Open(("art\\tile\\tilename.mes", TileMes(new Dictionary<int, string>
        {
            [0] = "abc/b", [101] = "def/f", [202] = "ghi/s 12", [303] = "jkl"
        })));
        var tileSource = new TerrainTileNameSource(source, "fixture-resource", "art\\tile\\tilename.mes", TerrainPathProfile.CeNonWindowsMacOs);
        var report = TerrainObstructionInspector.InspectSector(module, "fixture-module", Selector("maps\\fixture\\rules.sec"), tileSource);

        Assert.AreEqual(TerrainObstructionDisposition.NoBlock, report.GetCell(0, 0).FacadeWalkabilityBit.Disposition);
        Assert.AreEqual(TerrainObstructionDisposition.Blocked, report.GetCell(1, 0).FacadeWalkabilityBit.Disposition);
        Assert.AreEqual(TerrainObstructionApplicability.NotApplicable, report.GetCell(0, 0).TileNameRule.Applicability);
        foreach (var ordinal in new[] { 2, 3 }) Assert.AreEqual(TerrainObstructionDisposition.Blocked, report.GetCell(ordinal, 0).TileNameRule.Disposition);
        foreach (var ordinal in new[] { 4, 5 }) Assert.AreEqual(TerrainObstructionDisposition.NoBlock, report.GetCell(ordinal, 0).TileNameRule.Disposition);
        var suppressed = report.GetCell(2, 0, TileNameRuleMode.SuppressedByCaller).TileNameRule;
        Assert.AreEqual(TerrainObstructionApplicability.NotApplicable, suppressed.Applicability);
        Assert.IsNull(suppressed.Disposition);
        Assert.AreEqual(TerrainObstructionSupportedness.Unsupported, report.GetCell(6, 0).FacadeWalkabilityBit.Supportedness);
        Assert.IsNull(report.GetCell(6, 0).FacadeWalkabilityBit.Disposition);
    }

    [TestMethod]
    public void Table_failure_is_local_to_tile_name_and_report_owns_source_snapshot()
    {
        var words = new uint[4096];
        words[0] = Tile(0, outdoor: true, flippable: true);
        var bytes = Sector(words, 0xAA0004, new uint[128]);
        TerrainObstructionSectorReport report;
        using (var module = Open(("maps\\fixture\\life.sec", bytes)))
        using (var resource = Open(("art\\tile\\tilename.mes", TileMes(new Dictionary<int, string> { [0] = "abc/b" }))))
        {
            report = TerrainObstructionInspector.InspectSector(module, "fixture-module", Selector("maps\\fixture\\life.sec"),
                new TerrainTileNameSource(resource, "fixture-resource", "art\\tile\\tilename.mes", TerrainPathProfile.CeNonWindowsMacOs));
            words[0] = 0; // caller fixture mutation cannot change the copied report.
        }
        Assert.AreEqual(TerrainObstructionDisposition.Blocked, report.GetCell(0, 0).TileNameRule.Disposition);
        Assert.AreEqual(TerrainObstructionDisposition.NoBlock, report.GetCell(0, 0).SerializedBlockMask.Disposition);

        using var missingModule = Open(("maps\\fixture\\missing.sec", Sector(new uint[4096], 0xAA0004, new uint[128])));
        using var empty = Open(("other.mes", Ascii("{0}{abc}")));
        var missing = TerrainObstructionInspector.InspectSector(missingModule, "fixture-module", Selector("maps\\fixture\\missing.sec"),
            new TerrainTileNameSource(empty, "fixture-resource", "art\\tile\\tilename.mes", TerrainPathProfile.CeNonWindowsMacOs));
        Assert.AreEqual("TILE_NAME_ENTRY_MISSING", missing.GetCell(0, 0).TileNameRule.ReasonCode);
        Assert.AreEqual(TerrainObstructionDisposition.NoBlock, missing.GetCell(0, 0).SerializedBlockMask.Disposition);
    }

    [TestMethod]
    public void Malformed_admitted_tile_value_is_unknown_never_no_block()
    {
        var words = new uint[4096];
        words[0] = Tile(0, outdoor: true, flippable: true);
        using var module = Open(("maps\\fixture\\bad.sec", Sector(words, 0xAA0004, new uint[128])));
        using var resource = Open(("art\\tile\\tilename.mes", TileMes(new Dictionary<int, string> { [0] = "abc/q" })));
        var report = TerrainObstructionInspector.InspectSector(module, "fixture-module", Selector("maps\\fixture\\bad.sec"),
            new TerrainTileNameSource(resource, "fixture-resource", "art\\tile\\tilename.mes", TerrainPathProfile.CeNonWindowsMacOs));
        var tile = report.GetCell(0, 0).TileNameRule;
        Assert.AreEqual(TerrainObstructionSupportedness.Unknown, tile.Supportedness);
        Assert.IsNull(tile.Disposition);
        Assert.AreEqual("TILE_NAME_VALUE_UNSUPPORTED", tile.ReasonCode);
    }

    [TestMethod]
    public void Public_serialized_mask_requires_exact_owned_little_endian_snapshot()
    {
        var bytes = new byte[TerrainSerializedBlockMask.ByteLength];
        foreach (var ordinal in new[] { 0, 31, 32, 63, 64, 4095 })
        {
            var offset = (ordinal / 32) * sizeof(uint);
            var word = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)), word | (1u << (ordinal % 32)));
        }
        var mask = TerrainSerializedBlockMask.Parse(bytes);
        bytes.AsSpan().Clear();
        foreach (var (x, y) in new[] { (0, 0), (31, 0), (32, 0), (63, 0), (0, 1), (63, 63) })
            Assert.AreEqual(TerrainObstructionDisposition.Blocked, mask.GetDisposition(x, y));
        Assert.AreEqual(TerrainObstructionDisposition.NoBlock, mask.GetDisposition(30, 0));
        Assert.AreEqual(TerrainObstructionDisposition.Blocked, mask.GetDispositionByOrdinal(4095));
        AssertThrows<ArgumentException>(() => TerrainSerializedBlockMask.Parse(new byte[511]));
        AssertThrows<ArgumentException>(() => TerrainSerializedBlockMask.Parse(new byte[513]));
        AssertThrows<ArgumentOutOfRangeException>(() => mask.GetDisposition(-1, 0));
        AssertThrows<ArgumentOutOfRangeException>(() => mask.GetDisposition(0, 64));
        AssertThrows<ArgumentOutOfRangeException>(() => mask.GetDispositionByOrdinal(-1));
        AssertThrows<ArgumentOutOfRangeException>(() => mask.GetDispositionByOrdinal(4096));
    }

    [TestMethod]
    public void Report_has_safe_selected_entry_provenance_without_selector_text()
    {
        var words = new uint[4096];
        words[0] = 0xB0000001;
        words[1] = Tile(0, outdoor: true, flippable: true);
        using var module = Open(("private\\sector.sec", Sector(words, 0xAA0004, new uint[128])));
        using var source = Open(("private\\names.mes", TileMes(new Dictionary<int, string> { [0] = "abc/b" })));
        var report = TerrainObstructionInspector.InspectSector(module, "module-source", Selector("private\\sector.sec"),
            new TerrainTileNameSource(source, "name-source", "private\\names.mes", TerrainPathProfile.CeNonWindowsMacOs));

        Assert.IsNull(typeof(TerrainObstructionSectorReport).GetProperty("Selector"));
        Assert.AreEqual(0, report.Sector.EntryOrdinal);
        Assert.AreEqual(10, report.Sector.OriginX);
        Assert.AreEqual(0, report.GetCell(0, 0).FacadeWalkabilityBit.Provenance.EntryOrdinal);
        Assert.AreEqual(0, report.GetCell(1, 0).TileNameRule.Provenance.EntryOrdinal);
    }

    [TestMethod]
    public void Tile_name_strict_flags_and_selection_fail_closed_without_affecting_other_components()
    {
        foreach (var (value, expected) in new[]
                 {
                     ("abc/b", TerrainObstructionDisposition.Blocked), ("abc/f", TerrainObstructionDisposition.Blocked),
                     ("abc/bf", TerrainObstructionDisposition.Blocked), ("abc/sinp 12", TerrainObstructionDisposition.NoBlock),
                     ("abc", TerrainObstructionDisposition.NoBlock),
                 })
        {
            var words = new uint[4096]; words[0] = Tile(0, outdoor: true, flippable: true);
            using var module = Open(("maps\\fixture\\flags.sec", Sector(words, 0xAA0004, new uint[128])));
            using var source = Open(("art\\tile\\tilename.mes", TileMes(new Dictionary<int, string> { [0] = value })));
            var tile = TerrainObstructionInspector.InspectSector(module, "fixture-module", Selector("maps\\fixture\\flags.sec"),
                new TerrainTileNameSource(source, "fixture-resource", "art\\tile\\tilename.mes", TerrainPathProfile.CeNonWindowsMacOs)).GetCell(0, 0).TileNameRule;
            Assert.AreEqual(expected, tile.Disposition, value);
        }
        foreach (var malformed in new[] { "abc/q", "abc/b//", "abc/b 1x", "abc/  +" })
        {
            var words = new uint[4096]; words[0] = Tile(0, outdoor: true, flippable: true);
            using var module = Open(("maps\\fixture\\strict.sec", Sector(words, 0xAA0004, new uint[128])));
            using var source = Open(("art\\tile\\tilename.mes", TileMes(new Dictionary<int, string> { [0] = malformed })));
            var report = TerrainObstructionInspector.InspectSector(module, "fixture-module", Selector("maps\\fixture\\strict.sec"),
                new TerrainTileNameSource(source, "fixture-resource", "art\\tile\\tilename.mes", TerrainPathProfile.CeNonWindowsMacOs));
            Assert.AreEqual(TerrainObstructionSupportedness.Unknown, report.GetCell(0, 0).TileNameRule.Supportedness, malformed);
            Assert.AreEqual(TerrainObstructionDisposition.NoBlock, report.GetCell(0, 0).SerializedBlockMask.Disposition);
        }
    }

    [TestMethod]
    public void Missing_and_ambiguous_tile_entries_preserve_mask_and_do_not_claim_entry_identity()
    {
        var words = new uint[4096]; words[0] = Tile(0, outdoor: true, flippable: true);
        using var module = Open(("maps\\fixture\\source.sec", Sector(words, 0xAA0004, new uint[128])));
        using var missingSource = Open(("elsewhere.mes", Ascii("{0}{abc}")));
        var missing = TerrainObstructionInspector.InspectSector(module, "fixture-module", Selector("maps\\fixture\\source.sec"),
            new TerrainTileNameSource(missingSource, "fixture-resource", "art\\tile\\tilename.mes", TerrainPathProfile.CeNonWindowsMacOs)).GetCell(0, 0);
        Assert.AreEqual("TILE_NAME_ENTRY_MISSING", missing.TileNameRule.ReasonCode);
        Assert.IsNull(missing.TileNameRule.Provenance.EntryOrdinal);
        Assert.AreEqual(TerrainObstructionDisposition.NoBlock, missing.SerializedBlockMask.Disposition);

        using var ambiguousSource = Open(("art\\tile\\tilename.mes", TileMes(new Dictionary<int, string>())), ("ART/TILE/TILENAME.MES", TileMes(new Dictionary<int, string>())));
        var ambiguous = TerrainObstructionInspector.InspectSector(module, "fixture-module", Selector("maps\\fixture\\source.sec"),
            new TerrainTileNameSource(ambiguousSource, "fixture-resource", "art\\tile\\tilename.mes", TerrainPathProfile.CeNonWindowsMacOs)).GetCell(0, 0);
        Assert.AreEqual("TILE_NAME_ENTRY_AMBIGUOUS", ambiguous.TileNameRule.ReasonCode);
        Assert.IsNull(ambiguous.TileNameRule.Provenance.EntryOrdinal);
    }

    private static TerrainSectorSelector Selector(string path) => new(path, "fixture", 0, 0, 1, 10, 20, 64, 64);
    private static uint Tile(int material, bool outdoor, bool flippable) => ((uint)material << 22) | (outdoor ? 1u << 8 : 0) | (flippable ? 1u << 7 : 0);
    private static DatArchive Open(params (string Name, byte[] Bytes)[] entries) => DatArchive.Open(new MemoryStream(Dat(entries)), leaveOpen: true);
    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    private static byte[] TileMes(IReadOnlyDictionary<int, string> overrides)
    {
        var result = new StringBuilder();
        foreach (var key in Enumerable.Range(0, 400))
            result.Append('{').Append(key).Append("}{").Append(overrides.TryGetValue(key, out var value) ? value : "abc").Append('}');
        return Ascii(result.ToString());
    }

    private static byte[] Sector(uint[] words, int placeholder, uint[]? mask)
    {
        var length = checked(4 + (4096 * 4) + 4 + 4 + 4 + (placeholder >= 0xAA0002 ? 12 : 0) + (placeholder >= 0xAA0003 ? 24 : 0) + (placeholder >= 0xAA0004 ? 512 : 0) + 4);
        var bytes = new byte[length];
        for (var i = 0; i < words.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4 + (i * 4)), words[i]);
        var cursor = 4 + (4096 * 4);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(cursor), 1); cursor += 4;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(cursor), placeholder); cursor += 4;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(cursor), 0); cursor += 4;
        if (placeholder >= 0xAA0002) cursor += 12;
        if (placeholder >= 0xAA0003) cursor += 24;
        if (placeholder >= 0xAA0004)
            for (var i = 0; i < 128; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor + (i * 4)), mask![i]);
        return bytes;
    }

    private static byte[] Dat((string Name, byte[] Bytes)[] entries)
    {
        using var output = new MemoryStream(); var offsets = new List<uint>();
        foreach (var entry in entries) { offsets.Add(checked((uint)output.Position)); output.Write(entry.Bytes); }
        using var index = new MemoryStream(); WriteU32(index, checked((uint)entries.Length));
        for (var i = 0; i < entries.Length; i++)
        {
            var name = Ascii(entries[i].Name + "\0"); WriteU32(index, checked((uint)name.Length)); index.Write(name); index.Write(new byte[4]); WriteU32(index, 1);
            WriteU32(index, checked((uint)entries[i].Bytes.Length)); WriteU32(index, checked((uint)entries[i].Bytes.Length)); WriteU32(index, offsets[i]);
        }
        index.Position = 0; index.CopyTo(output); output.Write(new byte[16]); output.Write("1TAD"u8); WriteU32(output, checked((uint)entries.Sum(e => e.Name.Length + 1))); WriteU32(output, checked((uint)(index.Length + 28))); return output.ToArray();
    }
    private static void WriteU32(Stream stream, uint value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); stream.Write(bytes); }
    private static void AssertThrows<T>(Action action) where T : Exception
    {
        try { action(); Assert.Fail($"Expected {typeof(T).Name}."); }
        catch (T) { }
    }
}

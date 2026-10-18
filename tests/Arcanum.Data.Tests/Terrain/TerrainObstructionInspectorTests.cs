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
        var tileSource = new TerrainTileNameSource(source, "fixture-resource", "art\\tile\\tilename.mes", TerrainPathProfile.NonWindowsMacOs);
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
                new TerrainTileNameSource(resource, "fixture-resource", "art\\tile\\tilename.mes", TerrainPathProfile.NonWindowsMacOs));
            words[0] = 0; // caller fixture mutation cannot change the copied report.
        }
        Assert.AreEqual(TerrainObstructionDisposition.Blocked, report.GetCell(0, 0).TileNameRule.Disposition);
        Assert.AreEqual(TerrainObstructionDisposition.NoBlock, report.GetCell(0, 0).SerializedBlockMask.Disposition);

        using var missingModule = Open(("maps\\fixture\\missing.sec", Sector(new uint[4096], 0xAA0004, new uint[128])));
        using var empty = Open(("other.mes", Ascii("{0}{abc}")));
        var missing = TerrainObstructionInspector.InspectSector(missingModule, "fixture-module", Selector("maps\\fixture\\missing.sec"),
            new TerrainTileNameSource(empty, "fixture-resource", "art\\tile\\tilename.mes", TerrainPathProfile.NonWindowsMacOs));
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
            new TerrainTileNameSource(resource, "fixture-resource", "art\\tile\\tilename.mes", TerrainPathProfile.NonWindowsMacOs));
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
        using var source = Open(
            ("harmless-before.mes", Ascii("{0}{abc}")),
            ("private\\names.mes", TileMes(new Dictionary<int, string> { [0] = "abc/b" })));
        var report = TerrainObstructionInspector.InspectSector(module, "module-source", Selector("private\\sector.sec"),
            new TerrainTileNameSource(source, "name-source", "private\\names.mes", TerrainPathProfile.NonWindowsMacOs));

        Assert.IsNull(typeof(TerrainObstructionSectorReport).GetProperty("Selector"));
        Assert.AreEqual(0, report.Sector.EntryOrdinal);
        Assert.AreEqual(10, report.Sector.OriginX);
        Assert.AreEqual(0, report.GetCell(0, 0).FacadeWalkabilityBit.Provenance.EntryOrdinal);
        Assert.AreEqual(1, report.GetCell(1, 0).TileNameRule.Provenance.EntryOrdinal);
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
                new TerrainTileNameSource(source, "fixture-resource", "art\\tile\\tilename.mes", TerrainPathProfile.NonWindowsMacOs)).GetCell(0, 0).TileNameRule;
            Assert.AreEqual(expected, tile.Disposition, value);
        }
        foreach (var malformed in new[] { "abc/q", "abc/b//", "abc/b 1x", "abc/  +" })
        {
            var words = new uint[4096]; words[0] = Tile(0, outdoor: true, flippable: true);
            using var module = Open(("maps\\fixture\\strict.sec", Sector(words, 0xAA0004, new uint[128])));
            using var source = Open(("art\\tile\\tilename.mes", TileMes(new Dictionary<int, string> { [0] = malformed })));
            var report = TerrainObstructionInspector.InspectSector(module, "fixture-module", Selector("maps\\fixture\\strict.sec"),
                new TerrainTileNameSource(source, "fixture-resource", "art\\tile\\tilename.mes", TerrainPathProfile.NonWindowsMacOs));
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
            new TerrainTileNameSource(missingSource, "fixture-resource", "art\\tile\\tilename.mes", TerrainPathProfile.NonWindowsMacOs)).GetCell(0, 0);
        Assert.AreEqual("TILE_NAME_ENTRY_MISSING", missing.TileNameRule.ReasonCode);
        Assert.IsNull(missing.TileNameRule.Provenance.EntryOrdinal);
        Assert.AreEqual(TerrainObstructionDisposition.NoBlock, missing.SerializedBlockMask.Disposition);

        using var ambiguousSource = Open(("art\\tile\\tilename.mes", TileMes(new Dictionary<int, string>())), ("ART/TILE/TILENAME.MES", TileMes(new Dictionary<int, string>())));
        var ambiguous = TerrainObstructionInspector.InspectSector(module, "fixture-module", Selector("maps\\fixture\\source.sec"),
            new TerrainTileNameSource(ambiguousSource, "fixture-resource", "art\\tile\\tilename.mes", TerrainPathProfile.NonWindowsMacOs)).GetCell(0, 0);
        Assert.AreEqual("TILE_NAME_ENTRY_AMBIGUOUS", ambiguous.TileNameRule.ReasonCode);
        Assert.IsNull(ambiguous.TileNameRule.Provenance.EntryOrdinal);
    }

    [TestMethod]
    public void Every_mask_boundary_is_set_and_clear_in_public_snapshot_and_sector_report()
    {
        var boundaries = new[] { (0, 0), (31, 0), (32, 0), (63, 0), (0, 1), (63, 63) };
        foreach (var (x, y) in boundaries)
        {
            var ordinal = x + (64 * y);
            var bytes = new byte[TerrainSerializedBlockMask.ByteLength];
            SetMaskBit(bytes, ordinal, true);
            var mask = TerrainSerializedBlockMask.Parse(bytes);
            Assert.AreEqual(TerrainObstructionDisposition.Blocked, mask.GetDisposition(x, y));
            Assert.AreEqual(TerrainObstructionDisposition.Blocked, mask.GetDispositionByOrdinal(ordinal));
            SetMaskBit(bytes, ordinal, false);
            mask = TerrainSerializedBlockMask.Parse(bytes);
            Assert.AreEqual(TerrainObstructionDisposition.NoBlock, mask.GetDisposition(x, y));
            Assert.AreEqual(TerrainObstructionDisposition.NoBlock, mask.GetDispositionByOrdinal(ordinal));

            var words = new uint[4096];
            var reportBytes = Sector(words, 0xAA0004, MaskWords(ordinal));
            using var module = Open(("maps\\fixture\\boundary.sec", reportBytes));
            var report = TerrainObstructionInspector.InspectSector(module, "fixture-module", Selector("maps\\fixture\\boundary.sec"), null);
            Assert.AreEqual(TerrainObstructionDisposition.Blocked, report.GetCell(x, y).SerializedBlockMask.Disposition);
            using var clearModule = Open(("maps\\fixture\\clear.sec", Sector(words, 0xAA0004, new uint[128])));
            var clear = TerrainObstructionInspector.InspectSector(clearModule, "fixture-module", Selector("maps\\fixture\\clear.sec"), null);
            Assert.AreEqual(TerrainObstructionDisposition.NoBlock, clear.GetCell(x, y).SerializedBlockMask.Disposition);
        }
    }

    [TestMethod]
    public void Report_validates_each_coordinate_and_sanitizes_world_coordinate_overflow()
    {
        using var module = Open(("maps\\fixture\\ranges.sec", Sector(new uint[4096], 0xAA0004, new uint[128])));
        var report = TerrainObstructionInspector.InspectSector(module, "fixture-module", Selector("maps\\fixture\\ranges.sec"), null);
        foreach (var coordinates in new[] { (-1, 0), (64, 0), (0, -1), (0, 64) })
            AssertThrows<ArgumentOutOfRangeException>(() => report.GetCell(coordinates.Item1, coordinates.Item2));
        foreach (var ordinal in new[] { -1, 4096 })
            AssertThrows<ArgumentOutOfRangeException>(() => TerrainSerializedBlockMask.Parse(new byte[512]).GetDispositionByOrdinal(ordinal));
        foreach (var coordinates in new[] { (-1, 0), (64, 0), (0, -1), (0, 64) })
            AssertThrows<ArgumentOutOfRangeException>(() => TerrainSerializedBlockMask.Parse(new byte[512]).GetDisposition(coordinates.Item1, coordinates.Item2));

        using var overflowModule = Open(("maps\\fixture\\overflow.sec", Sector(new uint[4096], 0xAA0004, new uint[128])));
        var xOverflow = TerrainObstructionInspector.InspectSector(overflowModule, "fixture-module",
            new TerrainSectorSelector("maps\\fixture\\overflow.sec", "fixture", 0, 0, 1, int.MaxValue, 0, 64, 64), null);
        AssertTerrainCode(TerrainErrorCode.InvalidFraming, () => xOverflow.GetCell(1, 0));
        var yOverflow = TerrainObstructionInspector.InspectSector(overflowModule, "fixture-module",
            new TerrainSectorSelector("maps\\fixture\\overflow.sec", "fixture", 0, 0, 1, 0, int.MaxValue, 64, 64), null);
        AssertTerrainCode(TerrainErrorCode.InvalidFraming, () => yOverflow.GetCell(0, 1));
    }

    [TestMethod]
    public void Facade_component_uses_only_class_eleven_bit_zero_and_preserves_component_independence()
    {
        var words = new uint[4096];
        words[0] = 0xB0000005; words[1] = 0xB0000006; words[2] = 0; words[3] = 0xA0000001;
        using var module = Open(("maps\\fixture\\facades.sec", Sector(words, 0xAA0004, MaskWords(0))));
        var report = TerrainObstructionInspector.InspectSector(module, "fixture-module", Selector("maps\\fixture\\facades.sec"), null);
        Assert.AreEqual(TerrainObstructionDisposition.Blocked, report.GetCell(0, 0).SerializedBlockMask.Disposition);
        Assert.AreEqual(TerrainObstructionDisposition.NoBlock, report.GetCell(0, 0).FacadeWalkabilityBit.Disposition);
        Assert.AreEqual(TerrainObstructionDisposition.Blocked, report.GetCell(1, 0).FacadeWalkabilityBit.Disposition);
        var tileClass = report.GetCell(2, 0);
        Assert.AreEqual(TerrainObstructionApplicability.NotApplicable, tileClass.FacadeWalkabilityBit.Applicability);
        Assert.IsNull(tileClass.FacadeWalkabilityBit.Disposition);
        var unknownClass = report.GetCell(3, 0).FacadeWalkabilityBit;
        Assert.AreEqual(TerrainObstructionSupportedness.Unsupported, unknownClass.Supportedness);
        Assert.AreEqual(TerrainObstructionApplicability.Unknown, unknownClass.Applicability);
        Assert.IsNull(unknownClass.Disposition);
    }

    [TestMethod]
    public void Tile_name_mapping_uses_sorted_category_ordinals_at_every_range_boundary()
    {
        var records = new Dictionary<int, string> { [0] = "aaa/b", [99] = "aaa", [100] = "aaa", [199] = "aaa/f", [200] = "aaa/b", [299] = "aaa", [300] = "aaa", [399] = "aaa/f" };
        var words = new uint[4096];
        var cases = new[] { (0, 0, true, true, TerrainObstructionDisposition.Blocked), (1, 1, true, true, TerrainObstructionDisposition.NoBlock),
            (2, 0, true, false, TerrainObstructionDisposition.NoBlock), (3, 1, true, false, TerrainObstructionDisposition.Blocked),
            (4, 0, false, true, TerrainObstructionDisposition.Blocked), (5, 1, false, true, TerrainObstructionDisposition.NoBlock),
            (6, 0, false, false, TerrainObstructionDisposition.NoBlock), (7, 1, false, false, TerrainObstructionDisposition.Blocked) };
        foreach (var item in cases) words[item.Item1] = Tile(item.Item2, item.Item3, item.Item4);
        using var module = Open(("maps\\fixture\\mapping.sec", Sector(words, 0xAA0004, new uint[128])));
        using var source = Open(("art\\tile\\tilename.mes", SparseMes(records)));
        var report = TerrainObstructionInspector.InspectSector(module, "fixture-module", Selector("maps\\fixture\\mapping.sec"),
            new TerrainTileNameSource(source, "fixture-resource", "art\\tile\\tilename.mes", TerrainPathProfile.NonWindowsMacOs));
        foreach (var item in cases) Assert.AreEqual(item.Item5, report.GetCell(item.Item1, 0).TileNameRule.Disposition);

        using var limitedSource = Open(("art\\tile\\tilename.mes", SparseMes(new Dictionary<int, string> { [0] = "aaa" })));
        foreach (var missing in new[] { Tile(1, true, true), Tile(0, true, false), Tile(63, false, false) })
        {
            words[10] = missing;
            using var unresolvedModule = Open(("maps\\fixture\\unresolved.sec", Sector(words, 0xAA0004, new uint[128])));
            var unresolved = TerrainObstructionInspector.InspectSector(unresolvedModule, "fixture-module", Selector("maps\\fixture\\unresolved.sec"),
                new TerrainTileNameSource(limitedSource, "fixture-resource", "art\\tile\\tilename.mes", TerrainPathProfile.NonWindowsMacOs)).GetCell(10, 0).TileNameRule;
            Assert.AreEqual("TILE_NAME_MAPPING_UNRESOLVED", unresolved.ReasonCode);
            Assert.IsNull(unresolved.Disposition);
        }
    }

    [TestMethod]
    public void Tile_name_admitted_and_unsupported_value_matrix_is_component_local()
    {
        foreach (var (value, disposition) in new[] { ("abc/b", TerrainObstructionDisposition.Blocked), ("abc/f", TerrainObstructionDisposition.Blocked),
            ("abc/bfs", TerrainObstructionDisposition.Blocked), ("abc/bb", TerrainObstructionDisposition.Blocked), ("abc/ff", TerrainObstructionDisposition.Blocked),
            ("abc/s", TerrainObstructionDisposition.NoBlock), ("abc/i", TerrainObstructionDisposition.NoBlock), ("abc/n", TerrainObstructionDisposition.NoBlock),
            ("abc/p", TerrainObstructionDisposition.NoBlock), ("abc/ss", TerrainObstructionDisposition.NoBlock), ("abc/", TerrainObstructionDisposition.NoBlock),
            ("abc/ 0", TerrainObstructionDisposition.NoBlock), ("abc/s +12", TerrainObstructionDisposition.NoBlock), ("abc/n -19", TerrainObstructionDisposition.NoBlock),
            ("abc/p 123456789012345678901234567890", TerrainObstructionDisposition.NoBlock), ("abc", TerrainObstructionDisposition.NoBlock) })
            Assert.AreEqual(disposition, TileResult(value).Disposition, value);
        // A brace or NUL inside a value refuses the whole table (Structural_mes_..., ARC-DATA-027), so it is not a component case.
        foreach (var value in new[] { "", "a", "ab", "abcd", "abc/q", "abc/b//", "abc/b 1x", "abc/ +", "abc/ -", "abc/0x", "abc/\u0001", "abc/é" })
        {
            var result = TileResult(value);
            Assert.AreEqual(TerrainObstructionSupportedness.Unknown, result.Supportedness, value);
            Assert.IsNull(result.Disposition, value);
        }
    }

    [TestMethod]
    public void Structural_mes_and_limit_failures_are_typed_and_do_not_leak_private_input()
    {
        // ARC-DATA-027: the original MES grammar refuses these shapes; text outside braces (a stray brace, a control
        // character) and a non-ASCII byte in a text are read as the original reads them.
        foreach (var mes in new[] { "{bogus}{abc}", "{0}", "{0}{abc", "{0}{a{b}", "{0}{abc}{0}{def}", "{0}{abc}\r", "{0}{ab\0c}", "{-1}{abc}", "" })
            AssertSanitizedTerrainFailure(TerrainErrorCode.InvalidFraming, () => InspectRawMes(mes));
        AssertSanitizedTerrainFailure(TerrainErrorCode.InvalidFraming, () => InspectRawMesBytes([(byte)'{', (byte)'0', (byte)'}', (byte)'{', 0xFF, (byte)'}']));
        foreach (var mes in new[] { "{0}{abc}}", "{0}{abc}\u0001", "// note\n{0}{abc} trailing" }) InspectRawMes(mes);
        InspectRawMesBytes([(byte)'{', (byte)'0', (byte)'}', (byte)'{', 0x80, (byte)'}']);
        AssertSanitizedTerrainFailure(TerrainErrorCode.LimitExceeded, () => InspectRawMes("{0}{abc}", new TerrainLoaderOptions { MaxMesDecodedEntryBytes = 1 }));
        AssertSanitizedTerrainFailure(TerrainErrorCode.LimitExceeded, () => InspectRawMes("{0}{abcdef}", new TerrainLoaderOptions { MaxMesValueBytes = 3 }));
        AssertSanitizedTerrainFailure(TerrainErrorCode.LimitExceeded, () => InspectRawMes("{0}{abc}{1}{abc}", new TerrainLoaderOptions { MaxMesEntries = 1 }));
    }

    [TestMethod]
    public void Missing_name_source_composition_and_snapshot_ownership_remain_safe()
    {
        var words = new uint[4096]; words[0] = Tile(0, true, true); words[1] = 0xB0000000;
        var moduleBytes = Dat(new[] { ("maps\\fixture\\owned.sec", Sector(words, 0xAA0004, MaskWords(0))) });
        var mesBytes = Dat(new[] { ("art\\tile\\tilename.mes", TileMes(new Dictionary<int, string> { [0] = "abc" })) });
        using var moduleStream = new MemoryStream(moduleBytes, 0, moduleBytes.Length, writable: true, publiclyVisible: true);
        using var mesStream = new MemoryStream(mesBytes, 0, mesBytes.Length, writable: true, publiclyVisible: true);
        using var module = DatArchive.Open(moduleStream, leaveOpen: true);
        using var source = DatArchive.Open(mesStream, leaveOpen: true);
        var report = TerrainObstructionInspector.InspectSector(module, "fixture-module", Selector("maps\\fixture\\owned.sec"),
            new TerrainTileNameSource(source, "fixture-resource", "art\\tile\\tilename.mes", TerrainPathProfile.NonWindowsMacOs));
        var selectedModuleEntry = module.Entries[0];
        var serializedMaskOffset = checked((int)selectedModuleEntry.Offset
            + module.ReadEntry(selectedModuleEntry).Length - TerrainSerializedBlockMask.ByteLength - sizeof(int));
        _ = source.ReadEntry(source.Entries[0]);
        moduleStream.GetBuffer()[(int)module.Entries[0].Offset + 4] ^= 0xff;
        moduleStream.GetBuffer()[serializedMaskOffset] ^= 0x01;
        mesStream.GetBuffer()[(int)source.Entries[0].Offset + 3] ^= 0xff;
        module.Dispose(); source.Dispose();
        Assert.AreEqual(TerrainObstructionDisposition.Blocked, report.GetCell(0, 0).SerializedBlockMask.Disposition);
        Assert.AreEqual(TerrainObstructionDisposition.NoBlock, report.GetCell(0, 0).TileNameRule.Disposition);
        Assert.AreEqual(TerrainObstructionDisposition.Blocked, report.GetCell(1, 0).FacadeWalkabilityBit.Disposition);

        using var missingModule = Open(("maps\\fixture\\missing-source.sec", Sector(words, 0xAA0004, MaskWords(0))));
        var missing = TerrainObstructionInspector.InspectSector(missingModule, "fixture-module", Selector("maps\\fixture\\missing-source.sec"), null).GetCell(0, 0);
        Assert.AreEqual(TerrainObstructionDisposition.Blocked, missing.SerializedBlockMask.Disposition);
        Assert.AreEqual(TerrainObstructionSupportedness.Unknown, missing.TileNameRule.Supportedness);
        Assert.IsNull(missing.TileNameRule.Disposition);
    }

    [TestMethod]
    public void Component_composition_preserves_unknowns_and_only_allows_applicable_no_block_claims()
    {
        var tileWords = new uint[4096]; tileWords[0] = Tile(0, true, true);
        using var tileModule = Open(("maps\\fixture\\tile-composition.sec", Sector(tileWords, 0xAA0004, new uint[128])));
        using var tileSource = Open(("art\\tile\\tilename.mes", SparseMes(new Dictionary<int, string> { [0] = "abc" })));
        var tileCell = TerrainObstructionInspector.InspectSector(tileModule, "fixture-module", Selector("maps\\fixture\\tile-composition.sec"),
            new TerrainTileNameSource(tileSource, "fixture-resource", "art\\tile\\tilename.mes", TerrainPathProfile.NonWindowsMacOs)).GetCell(0, 0);
        Assert.AreEqual(TerrainObstructionDisposition.NoBlock, tileCell.SerializedBlockMask.Disposition);
        Assert.AreEqual(TerrainObstructionDisposition.NoBlock, tileCell.TileNameRule.Disposition);
        Assert.AreEqual(TerrainObstructionApplicability.NotApplicable, tileCell.FacadeWalkabilityBit.Applicability);
        AssertNullUnlessSupportedAndApplicable(tileCell.SerializedBlockMask, tileCell.FacadeWalkabilityBit, tileCell.TileNameRule);

        var facadeWords = new uint[4096]; facadeWords[0] = 0xB0000001;
        using var facadeModule = Open(("maps\\fixture\\facade-composition.sec", Sector(facadeWords, 0xAA0004, new uint[128])));
        var facadeCell = TerrainObstructionInspector.InspectSector(facadeModule, "fixture-module", Selector("maps\\fixture\\facade-composition.sec"), null).GetCell(0, 0);
        Assert.AreEqual(TerrainObstructionDisposition.NoBlock, facadeCell.SerializedBlockMask.Disposition);
        Assert.AreEqual(TerrainObstructionDisposition.NoBlock, facadeCell.FacadeWalkabilityBit.Disposition);
        Assert.AreEqual(TerrainObstructionApplicability.NotApplicable, facadeCell.TileNameRule.Applicability);
        AssertNullUnlessSupportedAndApplicable(facadeCell.SerializedBlockMask, facadeCell.FacadeWalkabilityBit, facadeCell.TileNameRule);

        using var oldModule = Open(("maps\\fixture\\old-composition.sec", Sector(facadeWords, 0xAA0003, null)));
        var oldCell = TerrainObstructionInspector.InspectSector(oldModule, "fixture-module", Selector("maps\\fixture\\old-composition.sec"), null).GetCell(0, 0);
        Assert.AreEqual(TerrainObstructionSupportedness.Unknown, oldCell.SerializedBlockMask.Supportedness);
        Assert.AreEqual(TerrainObstructionDisposition.NoBlock, oldCell.FacadeWalkabilityBit.Disposition);
        Assert.AreEqual(TerrainObstructionApplicability.NotApplicable, oldCell.TileNameRule.Applicability);
        AssertNullUnlessSupportedAndApplicable(oldCell.SerializedBlockMask, oldCell.FacadeWalkabilityBit, oldCell.TileNameRule);

        var missingSourceCell = TerrainObstructionInspector.InspectSector(tileModule, "fixture-module", Selector("maps\\fixture\\tile-composition.sec"), null).GetCell(0, 0);
        Assert.AreEqual(TerrainObstructionSupportedness.Unknown, missingSourceCell.TileNameRule.Supportedness);
        AssertNullUnlessSupportedAndApplicable(missingSourceCell.SerializedBlockMask, missingSourceCell.FacadeWalkabilityBit, missingSourceCell.TileNameRule);

        var unsupportedWords = new uint[4096]; unsupportedWords[0] = 0xA0000000;
        using var unsupportedModule = Open(("maps\\fixture\\unsupported-composition.sec", Sector(unsupportedWords, 0xAA0004, new uint[128])));
        var unsupportedCell = TerrainObstructionInspector.InspectSector(unsupportedModule, "fixture-module", Selector("maps\\fixture\\unsupported-composition.sec"), null).GetCell(0, 0);
        Assert.AreEqual(TerrainObstructionSupportedness.Unsupported, unsupportedCell.FacadeWalkabilityBit.Supportedness);
        Assert.AreEqual(TerrainObstructionSupportedness.Unsupported, unsupportedCell.TileNameRule.Supportedness);
        AssertNullUnlessSupportedAndApplicable(unsupportedCell.SerializedBlockMask, unsupportedCell.FacadeWalkabilityBit, unsupportedCell.TileNameRule);
    }

    [TestMethod]
    public void Obstruction_source_ids_and_public_results_do_not_expose_private_paths_or_text()
    {
        using var archive = Open(("private\\names.mes", Ascii("{0}{abc}")));
        foreach (var sourceId in new[] { "bad/path", "bad\\path", "bad\nname", "bad\0name", "é" })
        {
            AssertThrows<ArgumentException>(() => _ = new TerrainTileNameSource(archive, sourceId, "private\\names.mes", TerrainPathProfile.NonWindowsMacOs));
            using var module = Open(("private\\sector.sec", Sector(new uint[4096], 0xAA0004, new uint[128])));
            AssertThrows<ArgumentException>(() => TerrainObstructionInspector.InspectSector(module, sourceId, Selector("private\\sector.sec"), null));
        }
        Assert.IsNull(typeof(TerrainObstructionSectorReport).GetProperty("Selector"));
        Assert.IsNull(typeof(TerrainObstructionSectorReport).GetProperty("TileNameSource"));
    }

    private static TerrainObstructionComponentResult TileResult(string value)
    {
        var words = new uint[4096]; words[0] = Tile(0, true, true);
        using var module = Open(("maps\\fixture\\value.sec", Sector(words, 0xAA0004, new uint[128])));
        using var source = Open(("art\\tile\\tilename.mes", SparseMes(new Dictionary<int, string> { [0] = value })));
        return TerrainObstructionInspector.InspectSector(module, "fixture-module", Selector("maps\\fixture\\value.sec"),
            new TerrainTileNameSource(source, "fixture-resource", "art\\tile\\tilename.mes", TerrainPathProfile.NonWindowsMacOs)).GetCell(0, 0).TileNameRule;
    }

    private static void InspectRawMes(string text, TerrainLoaderOptions? options = null)
        => InspectRawMesBytes(Ascii(text), options);

    private static void InspectRawMesBytes(byte[] bytes, TerrainLoaderOptions? options = null)
    {
        var words = new uint[4096]; words[0] = Tile(0, true, true);
        using var module = Open(("maps\\fixture\\malformed.sec", Sector(words, 0xAA0004, new uint[128])));
        using var source = Open(("art\\tile\\tilename.mes", bytes));
        _ = TerrainObstructionInspector.InspectSector(module, "fixture-module", Selector("maps\\fixture\\malformed.sec"),
            new TerrainTileNameSource(source, "fixture-resource", "art\\tile\\tilename.mes", TerrainPathProfile.NonWindowsMacOs), options);
    }

    private static uint[] MaskWords(int ordinal)
    {
        var result = new uint[128]; result[ordinal / 32] = 1u << (ordinal % 32); return result;
    }

    private static void SetMaskBit(byte[] bytes, int ordinal, bool value)
    {
        var offset = (ordinal / 32) * sizeof(uint);
        var word = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)));
        word = value ? word | (1u << (ordinal % 32)) : word & ~(1u << (ordinal % 32));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)), word);
    }

    private static byte[] SparseMes(IReadOnlyDictionary<int, string> records)
    {
        var result = new StringBuilder();
        foreach (var record in records.OrderBy(pair => pair.Key)) result.Append('{').Append(record.Key).Append("}{").Append(record.Value).Append('}');
        return Ascii(result.ToString());
    }

    private static void AssertTerrainCode(TerrainErrorCode expected, Action action)
    {
        try { action(); Assert.Fail($"Expected terrain error {expected}."); }
        catch (TerrainException exception) { Assert.AreEqual(expected, exception.Code); }
    }

    private static void AssertNullUnlessSupportedAndApplicable(params TerrainObstructionComponentResult[] components)
    {
        foreach (var component in components)
            if (component.Supportedness != TerrainObstructionSupportedness.Supported || component.Applicability != TerrainObstructionApplicability.Applicable)
                Assert.IsNull(component.Disposition, component.ReasonCode);
    }

    private static void AssertSanitizedTerrainFailure(TerrainErrorCode expectedCode, Action action)
    {
        try { action(); Assert.Fail("Expected a typed terrain failure."); }
        catch (TerrainException exception)
        {
            Assert.AreEqual(expectedCode, exception.Code);
            Assert.IsFalse(exception.Message.Contains("private", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(exception.Message.Contains("bogus", StringComparison.OrdinalIgnoreCase));
        }
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

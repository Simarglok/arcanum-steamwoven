using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Arcanum.Data.Terrain;
using Arcanum.Tools.TerrainInspect;
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
    public void Fixed_roof_branch_uses_256_words_even_when_configured_limit_is_larger_and_framing_is_read_only()
    {
        var sector = Sector(Enumerable.Repeat(0x000001C0u, 4096).ToArray(), roofDiscriminator: 0);
        using var module = DatArchive.Open(new MemoryStream(Dat(("maps\\fixture\\roof.sec", sector))), leaveOpen: true);
        using var resource = Resource(("ART/TILE/AAABSE0A.ART", Art()));
        var report = TerrainLoader.Inspect(module, Selector("maps\\fixture\\roof.sec"), Source(resource), new TerrainLoaderOptions { MaxRoofWords = 257 });

        Assert.IsTrue(report.IsComplete);
        Assert.AreEqual(1028, report.Framing[TerrainSection.Roof].Length);
        Assert.AreEqual(4 + (4096 * 4) + 4 + (256 * 4), report.Framing[TerrainSection.Placeholder].Offset);
        Assert.IsFalse(report.Framing.Ranges is Dictionary<TerrainSection, TerrainByteRange>);
        var exposed = report.Framing.Ranges as IDictionary<TerrainSection, TerrainByteRange>;
        Assert.IsNotNull(exposed);
        AssertThrows<NotSupportedException>(() => exposed.Clear());
        Assert.AreEqual(1028, report.Framing[TerrainSection.Roof].Length);
    }

    [TestMethod]
    public void Normalization_recovery_requires_missing_raw_path_and_counts_only_actual_recoveries()
    {
        var sector = Sector(Enumerable.Repeat(0x00003001u, 4096).ToArray());
        using var module = DatArchive.Open(new MemoryStream(Dat(("maps\\fixture\\normalized.sec", sector))), leaveOpen: true);
        using var resource = Resource(("ART/TILE/DDDBSE4A.ART", Art()));
        var report = TerrainLoader.Inspect(module, Selector("maps\\fixture\\normalized.sec"), Source(resource));

        Assert.IsTrue(report.IsComplete, report.Issues.Count == 0 ? string.Empty : report.Issues[0].LogicalPath);
        Assert.AreEqual(0x00003001u, report.ResolvedRecords[0].RawId);
        Assert.AreEqual(0x00003000u, report.ResolvedRecords[0].LookupId);
        Assert.IsTrue(report.ResolvedRecords[0].WasNormalizationRecovered);
        Assert.AreEqual(4096, report.ResolvedRecords.Count(record => record.WasNormalizationRecovered));
        Assert.AreEqual(1, report.ResolvedRecords.Where(record => record.WasNormalizationRecovered).Select(record => record.LookupId).Distinct().Count());
    }

    [TestMethod]
    public void Non_ascii_mes_is_rejected_without_lossy_replacement()
    {
        var malformedMes = Ascii("{0}{aaa}{100}{bbb}{200}{ccc}{300}{ddd}{400}{000000}");
        Array.Resize(ref malformedMes, malformedMes.Length + 1);
        malformedMes[^1] = 0xff;
        var sector = Sector(Enumerable.Repeat(0x000001C0u, 4096).ToArray());
        using var module = DatArchive.Open(new MemoryStream(Dat(("maps\\fixture\\ascii.sec", sector))), leaveOpen: true);
        using var resource = DatArchive.Open(new MemoryStream(Dat(
            ("art\\tile\\tilename.mes", malformedMes),
            ("art\\facade\\facadename.mes", Ascii("{0}{zero}")),
            ("ART/TILE/AAABSE0A.ART", Art()))), leaveOpen: true);

        AssertThrows<TerrainFormatException>(() => TerrainLoader.Inspect(module, Selector("maps\\fixture\\ascii.sec"), Source(resource)));
    }

    [TestMethod]
    public void Exact_module_and_normalized_art_selection_fail_closed_for_zero_and_many_candidates()
    {
        var words = Enumerable.Repeat(0x000001C0u, 4096).ToArray();
        using var missingModule = DatArchive.Open(new MemoryStream(Dat(("maps\\fixture\\other.sec", Sector(words)))), leaveOpen: true);
        using var normalResource = Resource(("ART/TILE/AAABSE0A.ART", Art()));
        AssertTerrainCode(TerrainErrorCode.MissingModuleEntry, () => TerrainLoader.Inspect(missingModule, Selector("maps\\fixture\\wanted.sec"), Source(normalResource)));

        using var duplicateModule = DatArchive.Open(new MemoryStream(Dat(("maps\\fixture\\wanted.sec", Sector(words)), ("maps\\fixture\\wanted.sec", Sector(words)))), leaveOpen: true);
        AssertTerrainCode(TerrainErrorCode.AmbiguousModuleEntry, () => TerrainLoader.Inspect(duplicateModule, Selector("maps\\fixture\\wanted.sec"), Source(normalResource)));

        using var module = DatArchive.Open(new MemoryStream(Dat(("maps\\fixture\\art.sec", Sector(words)))), leaveOpen: true);
        using var duplicateArt = Resource(("ART/TILE/AAABSE0A.ART", Art()), ("art\\tile\\aaabse0a.art", Art()));
        var partial = TerrainLoader.Inspect(module, Selector("maps\\fixture\\art.sec"), Source(duplicateArt));
        Assert.IsFalse(partial.IsComplete);
        Assert.AreEqual(4096, partial.Issues.Count);
        Assert.AreEqual(TerrainIssueCode.AmbiguousResource, partial.Issues[0].Code);
    }

    [TestMethod]
    public void Facade_uses_zero_based_sorted_ordinal_not_the_gapped_mes_key()
    {
        var words = Enumerable.Repeat(0xB0060000u, 4096).ToArray();
        using var module = DatArchive.Open(new MemoryStream(Dat(("maps\\fixture\\facade.sec", Sector(words)))), leaveOpen: true);
        using var resource = DatArchive.Open(new MemoryStream(Dat(
            ("art\\tile\\tilename.mes", Ascii("{0}{aaa}{100}{bbb}{200}{ccc}{300}{ddd}{400}{000000}")),
            ("art\\facade\\facadename.mes", Ascii("{0}{zero}{25}{one}{100}{two}{333}{three}")),
            ("ART/FACADE/THREE.ART", Art()))), leaveOpen: true);

        var report = TerrainLoader.Inspect(module, Selector("maps\\fixture\\facade.sec"), Source(resource));
        Assert.IsTrue(report.IsComplete);
        Assert.AreEqual(TerrainKind.Facade, report.ResolvedRecords[0].Kind);
        Assert.AreEqual("art/facade/three.art", report.ResolvedRecords[0].LogicalPath);
    }

    [TestMethod]
    public void Per_cell_unsupported_class_invalid_frame_and_unavailable_palette_are_fail_closed()
    {
        using var unsupportedModule = DatArchive.Open(new MemoryStream(Dat(("maps\\fixture\\unsupported.sec", Sector(Enumerable.Repeat(0xA0000000u, 4096).ToArray())))), leaveOpen: true);
        using var noArtResource = Resource();
        var unsupported = TerrainLoader.Inspect(unsupportedModule, Selector("maps\\fixture\\unsupported.sec"), Source(noArtResource));
        Assert.AreEqual(4096, unsupported.Issues.Count);
        Assert.AreEqual(TerrainIssueCode.UnsupportedClass, unsupported.Issues[0].Code);

        using var invalidFrameModule = DatArchive.Open(new MemoryStream(Dat(("maps\\fixture\\frame.sec", Sector(Enumerable.Repeat(0xB0000002u, 4096).ToArray())))), leaveOpen: true);
        using var facadeResource = Resource(("ART/FACADE/ZERO.ART", Art()));
        var invalidFrame = TerrainLoader.Inspect(invalidFrameModule, Selector("maps\\fixture\\frame.sec"), Source(facadeResource));
        Assert.AreEqual(4096, invalidFrame.Issues.Count);
        Assert.AreEqual(TerrainIssueCode.InvalidFrame, invalidFrame.Issues[0].Code);

        using var paletteModule = DatArchive.Open(new MemoryStream(Dat(("maps\\fixture\\palette.sec", Sector(Enumerable.Repeat(0x000001C0u, 4096).ToArray())))), leaveOpen: true);
        using var paletteResource = Resource(("ART/TILE/AAABSE0A.ART", Art(hasPaletteZero: false)));
        var palette = TerrainLoader.Inspect(paletteModule, Selector("maps\\fixture\\palette.sec"), Source(paletteResource));
        Assert.AreEqual(4096, palette.Issues.Count);
        Assert.AreEqual(TerrainIssueCode.UnavailablePalette, palette.Issues[0].Code);
    }

    [TestMethod]
    public void Selected_frame_decodes_on_demand_without_transferring_archive_ownership()
    {
        var sector = Sector(Enumerable.Repeat(0x000001C0u, 4096).ToArray());
        using var module = DatArchive.Open(new MemoryStream(Dat(("maps\\fixture\\decode.sec", sector))), leaveOpen: true);
        using var resource = Resource(("ART/TILE/AAABSE0A.ART", Art()));
        var record = TerrainLoader.Load(module, Selector("maps\\fixture\\decode.sec"), Source(resource)).Records[0];

        var decoded = TerrainLoader.DecodeSelectedFrame(resource, record);
        Assert.AreEqual(1, decoded.Width);
        Assert.AreEqual(1, decoded.Height);
        Assert.AreEqual(4096, TerrainLoader.Inspect(module, Selector("maps\\fixture\\decode.sec"), Source(resource)).ResolvedRecords.Count);
    }

    [TestMethod]
    public void Mes_tokens_preserve_complete_no_slash_values_and_exact_prefixes_for_tile_and_facade()
    {
        var words = Enumerable.Range(0, 4096).Select(index => index % 2 == 0 ? 0x000001C0u : 0xB0000000u).ToArray();
        using var module = DatArchive.Open(new MemoryStream(Dat(("maps\\fixture\\tokens.sec", Sector(words)))), leaveOpen: true);
        using var resource = DatArchive.Open(new MemoryStream(Dat(
            ("art\\tile\\tilename.mes", Ascii("{0}{longtile}{100}{bbb/suffix}{200}{ccc}{300}{ddd}{400}{000000}")),
            ("art\\facade\\facadename.mes", Ascii("{0}{longfacade/suffix}")),
            ("ART/TILE/LONGTILEBSE0A.ART", Art()),
            ("ART/FACADE/LONGFACADE.ART", Art()))), leaveOpen: true);

        var report = TerrainLoader.Inspect(module, Selector("maps\\fixture\\tokens.sec"), Source(resource));

        Assert.IsTrue(report.IsComplete);
        Assert.AreEqual("art/tile/longtilebse0a.art", report.Outcomes[0].Record!.LogicalPath);
        Assert.AreEqual("art/facade/longfacade.art", report.Outcomes[1].Record!.LogicalPath);
    }

    [TestMethod]
    public void Terrain_inspect_cli_seam_emits_deterministic_private_complete_and_partial_aggregates()
    {
        var complete = CliReport(includeArt: true);
        var partial = CliReport(includeArt: false);
        var args = new[] { "--module", "/private/module.dat", "--resource", "/private/resource.dat", "--source-id", "safe-source" };
        var completeFirst = new StringWriter(); var completeSecond = new StringWriter(); var completeErrors = new StringWriter();
        Assert.AreEqual(0, Program.Run(args, completeFirst, completeErrors, new FixtureRuntime(complete.Report, complete.Selector)));
        Assert.AreEqual(0, Program.Run(args, completeSecond, new StringWriter(), new FixtureRuntime(complete.Report, complete.Selector)));
        Assert.AreEqual(completeFirst.ToString(), completeSecond.ToString());
        Assert.AreEqual(string.Empty, completeErrors.ToString());
        StringAssert.Contains(completeFirst.ToString(), "\"complete\":true");
        Assert.IsFalse(completeFirst.ToString().Contains("/private/", StringComparison.Ordinal));
        Assert.IsFalse(completeFirst.ToString().Contains("aaabse0a", StringComparison.Ordinal));

        var partialOutput = new StringWriter(); var partialErrors = new StringWriter();
        Assert.AreEqual(3, Program.Run(args, partialOutput, partialErrors, new FixtureRuntime(partial.Report, partial.Selector)));
        Assert.AreEqual(string.Empty, partialErrors.ToString());
        StringAssert.Contains(partialOutput.ToString(), "\"complete\":false");
        Assert.IsFalse(partialOutput.ToString().Contains("/private/", StringComparison.Ordinal));
        Assert.IsFalse(partialOutput.ToString().Contains("No selected resource entry", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Terrain_inspect_production_runtime_fails_fixed_hash_without_echoing_input_path()
    {
        var scratch = Path.Combine(Path.GetTempPath(), $"terrain-inspect-{Guid.NewGuid():N}.dat");
        try
        {
            File.WriteAllBytes(scratch, new byte[] { 1, 2, 3 });
            var output = new StringWriter(); var errors = new StringWriter();
            Assert.AreEqual(1, Program.Run(new[] { "--module", scratch, "--resource", scratch, "--source-id", "safe-source" }, output, errors, new ProductionRuntime()));
            Assert.AreEqual(string.Empty, output.ToString());
            StringAssert.Contains(errors.ToString(), "TerrainFormatException: EntryIdentityMismatch");
            Assert.IsFalse(errors.ToString().Contains(scratch, StringComparison.Ordinal));
        }
        finally { if (File.Exists(scratch)) File.Delete(scratch); }
    }

    [TestMethod]
    public void Mes_selection_and_sector_framing_reject_missing_many_and_invalid_fixed_values()
    {
        var words = Enumerable.Repeat(0x000001C0u, 4096).ToArray();
        using var module = DatArchive.Open(new MemoryStream(Dat(("maps\\fixture\\validation.sec", Sector(words)))), leaveOpen: true);
        using var missingTile = DatArchive.Open(new MemoryStream(Dat(("art\\facade\\facadename.mes", Ascii("{0}{zero}")))), leaveOpen: true);
        AssertTerrainCode(TerrainErrorCode.MissingMesEntry, () => TerrainLoader.Inspect(module, Selector("maps\\fixture\\validation.sec"), Source(missingTile)));

        using var manyTile = DatArchive.Open(new MemoryStream(Dat(
            ("art\\tile\\tilename.mes", Ascii("{0}{aaa}{100}{bbb}{200}{ccc}{300}{ddd}{400}{000000}")),
            ("ART/TILE/TILENAME.MES", Ascii("{0}{aaa}{100}{bbb}{200}{ccc}{300}{ddd}{400}{000000}")),
            ("art\\facade\\facadename.mes", Ascii("{0}{zero}")))), leaveOpen: true);
        AssertTerrainCode(TerrainErrorCode.AmbiguousMesEntry, () => TerrainLoader.Inspect(module, Selector("maps\\fixture\\validation.sec"), Source(manyTile)));

        var invalidRoof = Sector(words); BinaryPrimitives.WriteInt32LittleEndian(invalidRoof.AsSpan(4 + (4096 * 4)), 2);
        using var badRoof = DatArchive.Open(new MemoryStream(Dat(("maps\\fixture\\roof-invalid.sec", invalidRoof))), leaveOpen: true);
        using var resource = Resource(("ART/TILE/AAABSE0A.ART", Art()));
        AssertTerrainCode(TerrainErrorCode.InvalidFraming, () => TerrainLoader.Inspect(badRoof, Selector("maps\\fixture\\roof-invalid.sec"), Source(resource)));

        var invalidPlaceholder = Sector(words); BinaryPrimitives.WriteInt32LittleEndian(invalidPlaceholder.AsSpan(4 + (4096 * 4) + 4), 0xAA0005);
        using var badPlaceholder = DatArchive.Open(new MemoryStream(Dat(("maps\\fixture\\placeholder-invalid.sec", invalidPlaceholder))), leaveOpen: true);
        AssertTerrainCode(TerrainErrorCode.InvalidFraming, () => TerrainLoader.Inspect(badPlaceholder, Selector("maps\\fixture\\placeholder-invalid.sec"), Source(resource)));
    }

    [TestMethod]
    public void Decode_selected_frame_rejects_same_ordinal_with_mismatched_stored_identity()
    {
        var sector = Sector(Enumerable.Repeat(0x000001C0u, 4096).ToArray());
        using var module = DatArchive.Open(new MemoryStream(Dat(("maps\\fixture\\identity.sec", sector))), leaveOpen: true);
        using var selected = Resource(("ART/TILE/AAABSE0A.ART", Art()));
        var record = TerrainLoader.Load(module, Selector("maps\\fixture\\identity.sec"), Source(selected)).Records[0];
        using var other = Resource(("ART/TILE/DIFFERENT.ART", Art()));
        AssertTerrainCode(TerrainErrorCode.EntryIdentityMismatch, () => TerrainLoader.DecodeSelectedFrame(other, record));
    }

    [TestMethod]
    public void Framing_truncations_and_limit_counts_are_rejected_through_public_inspect()
    {
        var valid = Sector(Enumerable.Repeat(0x000001C0u, 4096).ToArray(), roofDiscriminator: 0);
        using var resource = Resource(("ART/TILE/AAABSE0A.ART", Art()));
        foreach (var cut in new[] { 0, 3, 4, 4 + (4096 * 4) - 1, 4 + (4096 * 4) + 3, valid.Length - 5 })
        {
            using var module = DatArchive.Open(new MemoryStream(Dat(("maps\\fixture\\truncated.sec", valid[..cut]))), leaveOpen: true);
            AssertThrows<TerrainException>(() => TerrainLoader.Inspect(module, Selector("maps\\fixture\\truncated.sec"), Source(resource)));
        }

        var overLights = Sector(Enumerable.Repeat(0x000001C0u, 4096).ToArray());
        BinaryPrimitives.WriteInt32LittleEndian(overLights, 1025);
        using var limitsModule = DatArchive.Open(new MemoryStream(Dat(("maps\\fixture\\limits.sec", overLights))), leaveOpen: true);
        AssertTerrainCode(TerrainErrorCode.LimitExceeded, () => TerrainLoader.Inspect(limitsModule, Selector("maps\\fixture\\limits.sec"), Source(resource)));
    }

    [TestMethod]
    public void Art_failures_are_per_cell_and_metadata_is_reused_on_repeat_loads()
    {
        var sector = Sector(Enumerable.Repeat(0x000001C0u, 4096).ToArray());
        using var module = DatArchive.Open(new MemoryStream(Dat(("maps\\fixture\\art-invalid.sec", sector))), leaveOpen: true);
        using var malformed = Resource(("ART/TILE/AAABSE0A.ART", new byte[] { 1 }));
        var malformedReport = TerrainLoader.Inspect(module, Selector("maps\\fixture\\art-invalid.sec"), Source(malformed));
        Assert.AreEqual(4096, malformedReport.Issues.Count); Assert.AreEqual(TerrainIssueCode.UnsupportedArt, malformedReport.Issues[0].Code);

        using var valid = Resource(("ART/TILE/AAABSE0A.ART", Art()));
        var capped = TerrainLoader.Inspect(module, Selector("maps\\fixture\\art-invalid.sec"), Source(valid), new TerrainLoaderOptions { MaxArtDecodedEntryBytes = 1 });
        Assert.AreEqual(TerrainIssueCode.UnsupportedArt, capped.Issues[0].Code);
        Assert.AreEqual(4096, TerrainLoader.Inspect(module, Selector("maps\\fixture\\art-invalid.sec"), Source(valid)).ResolvedRecords.Count);
        Assert.AreEqual(4096, TerrainLoader.Load(module, Selector("maps\\fixture\\art-invalid.sec"), Source(valid)).Records.Count);
    }

    [TestMethod]
    public void Outcomes_preserve_source_ordinals_world_coordinates_and_editor_layer_order()
    {
        var words = Enumerable.Repeat(0x000001C0u, 4096).ToArray(); words[1] = 0xB0000000u; words[64] = 0x000001C0u;
        using var module = DatArchive.Open(new MemoryStream(Dat(("maps\\fixture\\order.sec", Sector(words)))), leaveOpen: true);
        using var resource = Resource(("ART/TILE/AAABSE0A.ART", Art()), ("ART/FACADE/ZERO.ART", Art()));
        var sector = TerrainLoader.Load(module, new TerrainSectorSelector("maps\\fixture\\order.sec", "fixture", 0, 0, 1, 100, 200, 64, 64), Source(resource));
        Assert.AreEqual((0, 0, 100, 200), (sector.Records[0].LocalX, sector.Records[0].LocalY, sector.Records[0].WorldX, sector.Records[0].WorldY));
        Assert.AreEqual((1, 0, 101, 200), (sector.Records[1].LocalX, sector.Records[1].LocalY, sector.Records[1].WorldX, sector.Records[1].WorldY));
        Assert.AreEqual((0, 1, 100, 201), (sector.Records[64].LocalX, sector.Records[64].LocalY, sector.Records[64].WorldX, sector.Records[64].WorldY));
        Assert.AreEqual(TerrainKind.Tile, sector.DiagnosticOrder[0].Kind);
        Assert.AreEqual(TerrainKind.Facade, sector.DiagnosticOrder[^1].Kind);
    }

    [TestMethod]
    [TestCategory("RealData")]
    public void RealData_crash_site_is_immutable_and_complete_when_explicit_inputs_are_supplied()
    {
        var modulePath = Environment.GetEnvironmentVariable("ARCANUM_TERRAIN_MODULE");
        var resourcePath = Environment.GetEnvironmentVariable("ARCANUM_TERRAIN_RESOURCE");
        if (string.IsNullOrWhiteSpace(modulePath) || string.IsNullOrWhiteSpace(resourcePath)) Assert.Fail("ARCANUM_TERRAIN_MODULE and ARCANUM_TERRAIN_RESOURCE are required for this opt-in test.");
        var moduleBefore = Hash(modulePath!); var resourceBefore = Hash(resourcePath!);
        var moduleSizeBefore = new FileInfo(modulePath!).Length; var resourceSizeBefore = new FileInfo(resourcePath!).Length;
        Assert.AreEqual(355938305L, moduleSizeBefore);
        Assert.AreEqual(147991122L, resourceSizeBefore);
        Assert.AreEqual("8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f", moduleBefore);
        Assert.AreEqual("4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d", resourceBefore);
        using var module = DatArchive.Open(modulePath!); using var resource = DatArchive.Open(resourcePath!);
        var report = TerrainLoader.Inspect(module,
            new TerrainSectorSelector("maps\\Arcanum1-024-fixed\\86570436012.sec", "Arcanum1-024-fixed", 1452, 1290, 86570436012, 92928, 82560, 64, 64, new TerrainEntryExpectation(463, 2, 47291)),
            new TerrainResourceSource(resource, "real-data", "art\\tile\\tilename.mes", "art\\facade\\facadename.mes", TerrainPathProfile.CeNonWindowsMacOs));
        Assert.IsTrue(report.IsComplete); Assert.AreEqual(4096, report.Outcomes.Count); Assert.AreEqual(4096, report.ResolvedRecords.Count); Assert.AreEqual(0, report.Issues.Count);
        Assert.AreEqual(3033, report.ResolvedRecords.Count(record => record.Kind == TerrainKind.Tile)); Assert.AreEqual(1063, report.ResolvedRecords.Count(record => record.Kind == TerrainKind.Facade));
        Assert.AreEqual(1, report.ResolvedRecords.Select(record => record.ResourceIdentity.SourceId).Distinct().Count());
        Assert.AreEqual(4096, report.ResolvedRecords.Count(record => record.FrameMetadata.Width > 0 && record.FrameMetadata.Height > 0));
        Assert.AreEqual(457, report.ResolvedRecords.Count(record => record.WasNormalizationRecovered));
        Assert.AreEqual(86, report.ResolvedRecords.Where(record => record.WasNormalizationRecovered).Select(record => record.LookupId).Distinct().Count());
        var normalized = report.Outcomes[821].Record!;
        Assert.AreEqual(0x000131C1u, normalized.RawId); Assert.AreEqual(0x000131C0u, normalized.LookupId); Assert.IsTrue(normalized.WasNormalizationRecovered);
        Assert.AreEqual(new TerrainPoint(-1640, 1300), normalized.ProjectedPoint); Assert.AreEqual(new TerrainPoint(-1639, 1300), normalized.DiagnosticAnchor); Assert.AreEqual(4577, normalized.ResourceIdentity.DatOrdinal);
        var facade = report.Outcomes[2011].Record!;
        Assert.AreEqual(TerrainKind.Facade, facade.Kind); Assert.AreEqual(1, facade.Selection.FrameIndex); Assert.AreEqual(90, facade.ResourceIdentity.DatOrdinal);
        var viewport = new TerrainRect(-3000, 0, 5000, 2000);
        var representatives = new[]
        {
            (392, TerrainKind.Tile, 4587, 0, new TerrainRect(-79, 280, 78, 40), true, false),
            (573, TerrainKind.Tile, 4587, 0, new TerrainRect(-2119, 1380, 78, 40), true, true),
            (821, TerrainKind.Tile, 4577, 0, new TerrainRect(-1639, 1300, 78, 40), true, true),
            (18, TerrainKind.Tile, 4490, 0, new TerrainRect(-719, 360, 78, 40), true, false),
            (2075, TerrainKind.Facade, 90, 0, new TerrainRect(201, 1180, 80, 40), false, false),
            (2011, TerrainKind.Facade, 90, 1, new TerrainRect(161, 1160, 80, 40), false, false),
            (1947, TerrainKind.Facade, 90, 2, new TerrainRect(121, 1140, 80, 40), false, false)
        };
        foreach (var expected in representatives)
        {
            var record = report.Outcomes[expected.Item1].Record!;
            Assert.AreEqual(expected.Item2, record.Kind); Assert.AreEqual(expected.Item3, record.ResourceIdentity.DatOrdinal); Assert.AreEqual(expected.Item4, record.Selection.FrameIndex);
            var placement = TerrainGeometry.Place(record.Kind, record.LocalX, record.LocalY, record.RawId, record.FrameMetadata.Width, record.FrameMetadata.Height, viewport);
            Assert.IsNotNull(placement); Assert.AreEqual(expected.Item5, placement.Value.Footprint); Assert.AreEqual(new TerrainRect(0, 0, 78, 40), placement.Value.Source);
            Assert.AreEqual(new TerrainRect(expected.Item5.X, expected.Item5.Y, 78, 40), placement.Value.Destination); Assert.AreEqual(expected.Item6, placement.Value.FlipX); Assert.IsFalse(placement.Value.FlipY);
        }
        Assert.AreEqual(moduleBefore, Hash(modulePath!)); Assert.AreEqual(resourceBefore, Hash(resourcePath!));
        Assert.AreEqual(moduleSizeBefore, new FileInfo(modulePath!).Length); Assert.AreEqual(resourceSizeBefore, new FileInfo(resourcePath!).Length);
    }

    private static TerrainSectorSelector Selector(string moduleEntryPath) => new(moduleEntryPath, "fixture", 0, 0, 1, 0, 0, 64, 64);

    private static (TerrainLoadReport Report, TerrainSectorSelector Selector) CliReport(bool includeArt)
    {
        var selector = Selector("maps\\fixture\\cli.sec");
        using var module = DatArchive.Open(new MemoryStream(Dat(("maps\\fixture\\cli.sec", Sector(Enumerable.Repeat(0x000001C0u, 4096).ToArray())))), leaveOpen: true);
        using var resource = includeArt ? Resource(("ART/TILE/AAABSE0A.ART", Art())) : Resource();
        return (TerrainLoader.Inspect(module, selector, Source(resource)), selector);
    }

    private sealed class FixtureRuntime(TerrainLoadReport report, TerrainSectorSelector selector) : ITerrainInspectRuntime
    {
        public TerrainInspectExecution Inspect(Arguments arguments) => new(report, selector, arguments.SourceId!, new InputFingerprint(1, "module-fingerprint"), new InputFingerprint(1, "resource-fingerprint"));
    }

    private static TerrainResourceSource Source(DatArchive archive) => new(archive, "fixture-source", "art\\tile\\tilename.mes", "art\\facade\\facadename.mes", TerrainPathProfile.CeNonWindowsMacOs);
    private static DatArchive Resource(params (string Name, byte[] Bytes)[] artEntries)
    {
        var entries = new List<(string Name, byte[] Bytes)>
        {
            ("art\\tile\\tilename.mes", Ascii("{0}{aaa}{100}{bbb}{200}{ccc}{300}{ddd}{400}{000000}")),
            ("art\\facade\\facadename.mes", Ascii("{0}{zero}"))
        };
        entries.AddRange(artEntries);
        return DatArchive.Open(new MemoryStream(Dat(entries.ToArray())), leaveOpen: true);
    }
    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try { action(); Assert.Fail($"Expected {typeof(TException).Name}."); }
        catch (TException) { }
    }
    private static void AssertTerrainCode(TerrainErrorCode code, Action action)
    {
        try { action(); Assert.Fail($"Expected terrain code {code}."); }
        catch (TerrainException exception) { Assert.AreEqual(code, exception.Code); }
    }
    private static byte[] Sector(uint[] words, int roofDiscriminator = 1)
    {
        var roofBytes = roofDiscriminator == 0 ? 256 * 4 : 0;
        var bytes = new byte[4 + (words.Length * 4) + 4 + roofBytes + 4 + 4];
        for (var i = 0; i < words.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4 + (i * 4)), words[i]);
        var cursor = 4 + (words.Length * 4);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(cursor), roofDiscriminator);
        cursor += 4 + roofBytes;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(cursor), 0xAA0000);
        return bytes;
    }
    private static byte[] Art(bool hasPaletteZero = true)
    {
        var paletteBytes = hasPaletteZero ? 1024 : 0;
        var bytes = new byte[132 + paletteBytes + 28 + 1];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 8); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32), 1); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), hasPaletteZero ? 1u : 0u);
        var frame = 132 + paletteBytes; BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame), 1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 4), 1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 8), 1); bytes[^1] = 1;
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

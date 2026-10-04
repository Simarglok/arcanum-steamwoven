using System.Buffers.Binary;
using System.Numerics;
using Arcanum.Data;
using Arcanum.Data.StaticObjects;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.StaticObjects;

[TestClass]
public sealed class StaticObjectLoaderTests
{
    [TestMethod]
    public void Inspect_preserves_source_order_absent_values_and_slot_18_not_slot_19()
    {
        using var fixture = new Fixture(Record(7001, 0x40000000, 0x00000000, 0x00002000, location: 7, offsetX: -3, offsetY: 11),
            Record(7002, 0x40000000, 0x00002000, 0, location: null, offsetX: null, offsetY: null));

        var report = fixture.Inspect();
        Assert.AreEqual(2, report.TerminalRecordCount);
        Assert.AreEqual(2, report.FieldResolvedCount);
        Assert.AreEqual(0, report.ResourceResolvedCount);
        Assert.AreEqual(2, report.ResourceNotEvaluatedCount);
        Assert.AreEqual(0, report.Records[0].SourceIndex);
        Assert.AreEqual(1, report.Records[1].SourceIndex);
        Assert.AreEqual(7L, report.Records[0].Location);
        Assert.AreEqual(-3, report.Records[0].DirectOffsetX);
        Assert.AreEqual(11, report.Records[0].DirectOffsetY);
        Assert.IsTrue(report.Records[0].IsStatic, "slot 18 is clear while the adjacent spell slot is dynamic");
        Assert.IsFalse(report.Records[1].IsStatic, "slot 18 is dynamic while the adjacent spell slot is clear");
        Assert.IsNull(report.Records[1].Location);
        Assert.IsNull(report.Records[1].DirectOffsetX);
        Assert.IsNull(report.Records[1].DirectOffsetY);
    }

    [TestMethod]
    public void Inspect_resolves_only_current_art_and_flags_from_a_matching_prototype()
    {
        using var fixture = new Fixture(Record(7001, null, null, 0, location: null, offsetX: null, offsetY: null));
        File.WriteAllBytes(Path.Combine(fixture.PrototypeRoot, "007001 - fixture.pro"), Prototype(7001, 0x40000000, 0));

        var record = fixture.Inspect().Records[0];
        Assert.AreEqual(0x40000000, record.CurrentArt);
        Assert.IsTrue(record.CurrentArtInherited);
        Assert.AreEqual(0u, record.EffectiveFlags);
        Assert.IsTrue(record.FlagsInherited);
        Assert.IsTrue(record.IsStatic);
        Assert.IsNull(record.Location, "location is never inherited by this bounded API");
    }

    [TestMethod]
    public void Inspect_rejects_truncation_versions_types_masks_counts_optionals_and_trailing_bytes()
    {
        AssertFailure(Sector(Record(7001, 1, 0, 0)[..^1]), StaticObjectErrorCode.InvalidRecord);
        AssertFailure(Sector(Record(7001, 1, 0, 0, version: 118)), StaticObjectErrorCode.UnsupportedVersion);
        using (var unsupported = new Fixture(Sector(Record(7001, 1, 0, 0, type: 2)), isSector: true))
            Assert.AreEqual(StaticObjectFieldStatus.UnsupportedType, unsupported.Inspect().Records[0].FieldStatus);
        AssertFailure(Sector(Record(7001, 1, 0, 0, fieldCountDelta: 1)), StaticObjectErrorCode.InvalidRecord);
        AssertFailure(Sector(Record(7001, 1, 0, 0, badLocationPresence: true)), StaticObjectErrorCode.InvalidRecord);
        var trailing = Record(7001, 1, 0, 0).ToList(); trailing.Add(1);
        AssertFailure(Sector(trailing.ToArray()), StaticObjectErrorCode.InvalidFraming);
    }

    [TestMethod]
    public void Inspect_rejects_missing_ambiguous_and_identity_mismatched_prototypes()
    {
        using var fixture = new Fixture(Record(7001, null, null, 0));
        AssertStaticFailure(() => fixture.Inspect(), StaticObjectErrorCode.UnresolvedReference, StaticObjectFailureStage.Prototype, 0);
        File.WriteAllBytes(Path.Combine(fixture.PrototypeRoot, "007001 - one.pro"), Prototype(7002, 0x40000000, 0));
        AssertStaticFailure(() => fixture.Inspect(), StaticObjectErrorCode.UnresolvedReference, StaticObjectFailureStage.Prototype, 0);
        File.WriteAllBytes(Path.Combine(fixture.PrototypeRoot, "007001 - two.pro"), Prototype(7001, 0x40000000, 0));
        AssertStaticFailure(() => fixture.Inspect(), StaticObjectErrorCode.UnresolvedReference, StaticObjectFailureStage.Prototype, 0);
    }

    [TestMethod]
    public void Inspect_rejects_a_type_mismatched_prototype_with_a_sanitized_prototype_diagnostic()
    {
        using var fixture = new Fixture(Record(7001, null, null, 0));
        var privateName = Path.Combine(fixture.PrototypeRoot, "007001 - private-type.pro");
        var bytes = Prototype(7001, 0x40000000, 0);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), 2);
        File.WriteAllBytes(privateName, bytes);

        var failure = AssertThrows(() => fixture.Inspect());
        Assert.AreEqual(StaticObjectErrorCode.UnresolvedReference, failure.Code);
        Assert.AreEqual(StaticObjectFailureStage.Prototype, failure.Stage);
        Assert.AreEqual(0, failure.SourceIndex);
        Assert.IsFalse(failure.ToString().Contains("private-type", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Inspect_reports_sanitized_record_diagnostics_and_enforces_caller_archive_limits()
    {
        using var fixture = new Fixture(Record(7001, 1, 0, 0, version: 118));
        var failure = AssertThrows(() => fixture.Inspect());
        Assert.AreEqual(StaticObjectErrorCode.UnsupportedVersion, failure.Code);
        Assert.AreEqual(StaticObjectFailureStage.Record, failure.Stage);
        Assert.AreEqual(0, failure.SourceIndex);
        Assert.IsNotNull(failure.Offset);
        Assert.IsFalse(failure.ToString().Contains(fixture.PrototypeRoot, StringComparison.Ordinal));

        var capped = AssertThrows(() => StaticObjectLoader.Inspect(fixture.Source, fixture.Prototypes,
            new StaticObjectLoaderOptions { MaxArchiveBytes = 1 }));
        Assert.AreEqual(StaticObjectErrorCode.LimitExceeded, capped.Code);
        Assert.AreEqual(StaticObjectFailureStage.Archive, capped.Stage);
    }

    [TestMethod]
    public void Inspect_rejects_reserved_masks_unknown_types_terminal_mismatches_and_explicit_caps()
    {
        var unknownType = Record(7001, 1, 0, 0); BinaryPrimitives.WriteInt32LittleEndian(unknownType.AsSpan(52), 18);
        AssertFailure(Sector(unknownType), StaticObjectErrorCode.UnsupportedType);

        var reservedMask = Record(7001, 1, 0, 0); BinaryPrimitives.WriteUInt32LittleEndian(reservedMask.AsSpan(62), 1u << 10);
        AssertFailure(Sector(reservedMask), StaticObjectErrorCode.InvalidRecord);

        var terminalMismatch = Sector(Record(7001, 1, 0, 0)); BinaryPrimitives.WriteInt32LittleEndian(terminalMismatch.AsSpan(terminalMismatch.Length - 4), 2);
        AssertFailure(terminalMismatch, StaticObjectErrorCode.InvalidRecord);

        using var fixture = new Fixture(Record(7001, 1, 0, 0), Record(7002, 1, 0, 0));
        Assert.AreEqual(StaticObjectErrorCode.LimitExceeded, AssertThrows(() => fixture.Inspect(new StaticObjectLoaderOptions { MaxRecords = 1 })).Code);
        Assert.AreEqual(StaticObjectErrorCode.LimitExceeded, AssertThrows(() => fixture.Inspect(new StaticObjectLoaderOptions { MaxFields = 1 })).Code);
        Assert.AreEqual(StaticObjectErrorCode.LimitExceeded, AssertThrows(() => fixture.Inspect(new StaticObjectLoaderOptions { MaxSectorBytes = 1 })).Code);
    }

    [TestMethod]
    public void Inspect_validates_array_envelopes_bitsets_populations_and_checked_bounds()
    {
        using (var valid = ArrayFixture(ArrayRecord(4, 1, 2, [1, 0], includeElements: true)))
            Assert.AreEqual(StaticObjectCensusStatus.Complete, valid.Inspect().CensusStatus);

        AssertArrayFailure(ArrayRecord(8, 1, 2, [1, 0], includeElements: true), StaticObjectErrorCode.InvalidRecord);
        AssertArrayFailure(ArrayRecord(4, 1, 2, [0, 0], includeElements: true), StaticObjectErrorCode.InvalidRecord);
        AssertArrayFailure(ArrayRecord(4, 2, 2, [3, 0], includeElements: true), StaticObjectErrorCode.LimitExceeded,
            new StaticObjectLoaderOptions { MaxArrayElements = 1 });
        AssertArrayFailure(ArrayRecord(4, int.MaxValue, 2, [], includeElements: false), StaticObjectErrorCode.InvalidRecord,
            new StaticObjectLoaderOptions { MaxArrayElements = int.MaxValue });
        AssertArrayFailure(ArrayRecord(4, 1, 2, [1], includeElements: true), StaticObjectErrorCode.InvalidRecord);
    }

    [TestMethod]
    public void File_array_capacity_is_positive_not_the_runtime_minimum_and_never_eats_the_next_record()
    {
        // These bit words independently encode sparse key31 and key33. A one-word
        // FILE capacity is valid; a runtime resize policy is not a wire constraint.
        foreach (var bitset in new uint[][] { [0x80000000], [0, 2] })
        {
            var first = ArrayRecord(4, 1, bitset.Length, bitset, includeElements: true);
            var next = Record(7002, 0x40000000, 0, 0, location: 63, offsetX: -17, offsetY: 29);
            using var fixture = new Fixture(first, next);
            File.WriteAllBytes(Path.Combine(fixture.PrototypeRoot, "007001 - array.pro"), Prototype(7001, 0x40000000, 0));
            var report = fixture.Inspect();
            Assert.AreEqual(2, report.TerminalRecordCount);
            Assert.AreEqual(1, report.Records[1].SourceIndex);
            Assert.AreEqual(7002, report.Records[1].PrototypeAid);
            Assert.AreEqual(63L, report.Records[1].Location);
            Assert.AreEqual(-17, report.Records[1].DirectOffsetX);
            Assert.AreEqual(29, report.Records[1].DirectOffsetY);
        }
        using (var empty = ArrayFixture(ArrayRecord(4, 0, 1, [0], includeElements: false)))
            Assert.AreEqual(StaticObjectCensusStatus.Complete, empty.Inspect().CensusStatus);
        AssertArrayFailure(ArrayRecord(4, 0, 0, [], includeElements: false), StaticObjectErrorCode.InvalidRecord);
        AssertArrayFailure(ArrayRecord(4, 1, 1, [3], includeElements: true), StaticObjectErrorCode.InvalidRecord);
        AssertArrayFailure(ArrayRecord(8, 1, 1, [1], includeElements: true), StaticObjectErrorCode.InvalidRecord);
        AssertArrayFailure(ArrayRecord(4, -1, 1, [0], includeElements: false), StaticObjectErrorCode.InvalidRecord);
        AssertArrayFailure(ArrayRecord(4, 1, 2, [0, 2], includeElements: true), StaticObjectErrorCode.LimitExceeded,
            new StaticObjectLoaderOptions { MaxArrayElements = 1 });
        AssertArrayFailure(ArrayRecord(4, 1, 1, [1], includeElements: true)[..^1], StaticObjectErrorCode.InvalidRecord);
    }

    [TestMethod]
    public void Inspect_validates_type_15_string_envelopes_nuls_truncation_and_caps()
    {
        using (var valid = new Fixture(StringRecord(3, "abc\0"u8.ToArray())))
            Assert.AreEqual(StaticObjectFieldStatus.UnsupportedType, valid.Inspect().Records[0].FieldStatus);

        AssertStringFailure(StringRecord(-1, []), StaticObjectErrorCode.LimitExceeded);
        AssertStringFailure(StringRecord(4, []), StaticObjectErrorCode.LimitExceeded, new StaticObjectLoaderOptions { MaxStringBytes = 3 });
        AssertStringFailure(StringRecord(3, "abcx"u8.ToArray()), StaticObjectErrorCode.InvalidRecord);
        AssertStringFailure(StringRecord(3, "a"u8.ToArray()), StaticObjectErrorCode.InvalidRecord);
    }

    [TestMethod]
    public void Selected_resource_stage_requires_five_structurally_eligible_records()
    {
        using var fixture = new Fixture(Record(7001, 1, 0, 0));
        var failure = AssertThrows(() => StaticObjectLoader.InspectSelectedResources(fixture.Source, fixture.Prototypes));
        Assert.AreEqual(StaticObjectErrorCode.InvalidSelector, failure.Code);
        Assert.AreEqual(StaticObjectFailureStage.Selector, failure.Stage);
    }

    [TestMethod]
    public void Selected_resource_stage_resolves_five_synthetic_records_without_claiming_original_identity()
    {
        var records = SyntheticResourceRecords();
        var resources = new[]
        {
            ("art/scenery/scenery.mes", System.Text.Encoding.ASCII.GetBytes("{1001}{one.art}\n{2002}{two.art}\0")),
            ("art/scenery/one.art", Art()), ("art/scenery/two.art", Art())
        };
        using var fixture = new Fixture(Sector(records), true, resources, "maps\\fixture\\object.sec");
        var report = StaticObjectLoader.InspectSelectedResources(fixture.Source, fixture.Prototypes);
        Assert.AreEqual(5, report.Records.Count);
        Assert.AreEqual(5, report.ResourceResolvedCount);
        Assert.AreEqual(0, report.ResourceNotEvaluatedCount);
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, report.Records.Where(record => record.ResourceStatus == StaticObjectResourceStatus.Resolved).Select(record => record.SourceIndex).ToArray());
        var decoded = StaticObjectLoader.DecodeSelectedFrame(fixture.Source.ResourceArchive, report.Records[0]);
        Assert.AreEqual(1, decoded.Width);
        Assert.AreEqual(1, decoded.Height);
        AssertSyntheticChain(report.Records[0], 7101, 1000, 0x40080040, 1, -1, 1, 1);
        AssertSyntheticChain(report.Records[1], 7102, 1001, 0x40100080, 2, -2, 2, 2);
        AssertSyntheticChain(report.Records[2], 7103, 1002, 0x40080040, 3, -3, 1, 1);
        AssertSyntheticChain(report.Records[3], 7104, 1003, 0x40100080, 4, -4, 2, 2);
        AssertSyntheticChain(report.Records[4], 7105, 1004, 0x40080040, 5, -5, 1, 1);

        var nonStatic = records.ToArray();
        nonStatic[3] = Record(7104, unchecked((int)0x40100080), 0x00002000, 0, 1003, 4, -4);
        using var rejected = new Fixture(Sector(nonStatic), true, resources, "maps\\fixture\\object.sec");
        var failure = AssertThrows(() => StaticObjectLoader.InspectSelectedResources(rejected.Source, rejected.Prototypes));
        Assert.AreEqual(StaticObjectErrorCode.InvalidSelector, failure.Code);
        Assert.AreEqual(StaticObjectFailureStage.Selector, failure.Stage);
        Assert.AreEqual(3, failure.SourceIndex);

        var mismatchedResources = new[]
        {
            ("art/scenery/scenery.mes", System.Text.Encoding.ASCII.GetBytes("{1001}{one.art}\n{2002}{two.art}\0")),
            ("art/scenery/different.art", Art()), ("art/scenery/two.art", Art())
        };
        using var mismatched = new Fixture(Sector(records), true, mismatchedResources, "maps\\fixture\\object.sec");
        AssertStaticFailure(() => StaticObjectLoader.DecodeSelectedFrame(mismatched.Source.ResourceArchive, report.Records[0]),
            StaticObjectErrorCode.UnresolvedResource, StaticObjectFailureStage.Art, 0);
    }

    [TestMethod]
    public void Selected_resource_decode_rejects_each_immutable_identity_component_and_selected_art_dimension()
    {
        var records = SyntheticResourceRecords();
        var mapping = System.Text.Encoding.ASCII.GetBytes("{1001}{one.art}\n{2002}{two.art}\0");
        var art = Art();
        using var fixture = new Fixture(Sector(records), true,
            [("art/scenery/scenery.mes", mapping), ("art/scenery/one.art", art), ("art/scenery/two.art", art)], "maps\\fixture\\object.sec");
        var record = StaticObjectLoader.InspectSelectedResources(fixture.Source, fixture.Prototypes).Records[0];
        var baseline = record.ArtMetadata!.ResourceIdentity;
        Assert.AreEqual(1, baseline.DatOrdinal);
        Assert.AreEqual(1u, baseline.Flags);
        Assert.AreEqual((uint)art.Length, baseline.DeclaredDecodedSize);

        var identityMutations = new[]
        {
            ("ordinal", Dat(("padding", [1]), ("art/scenery/scenery.mes", mapping), ("art/scenery/one.art", art), ("art/scenery/two.art", art))),
            ("flags", DatWithMetadata(("art/scenery/scenery.mes", mapping, 1u, null), ("art/scenery/one.art", art, 2u, null), ("art/scenery/two.art", art, 1u, null))),
            ("size", DatWithMetadata(("art/scenery/scenery.mes", mapping, 1u, null), ("art/scenery/one.art", art, 1u, (uint)(art.Length + 1)), ("art/scenery/two.art", art, 1u, null))),
            ("name", Dat(("art/scenery/scenery.mes", mapping), ("art/scenery/renamed.art", art), ("art/scenery/two.art", art)))
        };
        foreach (var mutation in identityMutations)
        {
            using var stream = new MemoryStream(mutation.Item2);
            using var archive = DatArchive.Open(stream, leaveOpen: true);
            var failure = AssertStaticFailure(() => StaticObjectLoader.DecodeSelectedFrame(archive, record),
                StaticObjectErrorCode.UnresolvedResource, StaticObjectFailureStage.Art, 0);
            Assert.IsFalse(failure.Message.Contains(mutation.Item1, StringComparison.Ordinal));
        }

        foreach (var offset in new[] { 0, 8, 12 }) // group count, frame count, palette presence
        {
            var incompatible = art.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(incompatible.AsSpan(offset), 0);
            using var stream = new MemoryStream(Dat(("art/scenery/scenery.mes", mapping), ("art/scenery/one.art", incompatible), ("art/scenery/two.art", art)));
            using var archive = DatArchive.Open(stream, leaveOpen: true);
            AssertStaticFailure(() => StaticObjectLoader.DecodeSelectedFrame(archive, record),
                StaticObjectErrorCode.InvalidArtSelector, StaticObjectFailureStage.Art, 0);
        }
    }

    [TestMethod]
    public void Selected_resource_stage_admits_exactly_indexes_zero_through_four_and_marks_every_later_record_outside_scope()
    {
        var records = SyntheticResourceRecords().Concat(
        [
            Record(7106, unchecked((int)0x40080040), 0, 0, 1005, 6, -6),
            Record(7107, unchecked((int)0x40100080), 0, 0, 1006, 7, -7)
        ]).ToArray();
        var resources = new[]
        {
            ("art/scenery/scenery.mes", System.Text.Encoding.ASCII.GetBytes("{1001}{one.art}\n{2002}{two.art}\0")),
            ("art/scenery/one.art", Art()), ("art/scenery/two.art", Art())
        };
        using var fixture = new Fixture(Sector(records), true, resources, "maps\\fixture\\object.sec");
        var report = StaticObjectLoader.InspectSelectedResources(fixture.Source, fixture.Prototypes);
        Assert.AreEqual(7, report.TerminalRecordCount);
        Assert.AreEqual(5, report.ResourceResolvedCount);
        Assert.AreEqual(2, report.ResourceOutsideScopeCount);
        Assert.AreEqual(0, report.ResourceNotEvaluatedCount);
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, report.Records.Where(record => record.ResourceStatus == StaticObjectResourceStatus.Resolved).Select(record => record.SourceIndex).ToArray());
        CollectionAssert.AreEqual(new[] { 5, 6 }, report.Records.Where(record => record.ResourceStatus == StaticObjectResourceStatus.OutsideResourceScope).Select(record => record.SourceIndex).ToArray());
    }

    [TestMethod]
    public void Inspection_is_immutable_after_caller_owned_buffer_mutation()
    {
        using var fixture = new Fixture(Record(7001, 0x40000000, 0, 0));
        var moduleBefore = fixture.ModuleBuffer.ToArray();
        var resourceBefore = fixture.ResourceBuffer.ToArray();
        var inspection = fixture.Inspect();
        CollectionAssert.AreEqual(moduleBefore, fixture.ModuleBuffer, "inspection must not mutate the caller-owned module buffer");
        CollectionAssert.AreEqual(resourceBefore, fixture.ResourceBuffer, "inspection must not mutate the caller-owned resource buffer");
        fixture.ModuleBuffer[0] ^= 0xff;
        fixture.ResourceBuffer[0] ^= 0xff;
        Assert.AreEqual(0x40000000, inspection.Records[0].CurrentArt);
        Assert.IsTrue(inspection.Records[0].IsStatic);
    }

    [TestMethod]
    public void Selected_resource_stage_fails_closed_for_missing_unsafe_and_duplicate_resources()
    {
        var records = SyntheticResourceRecords();
        AssertStrictResourceFailure(records, new[] { ("art/scenery/scenery.mes", System.Text.Encoding.ASCII.GetBytes("{2002}{two.art}\0")) }, StaticObjectFailureStage.Mapping);
        AssertStrictResourceFailure(records, new[] { ("art/scenery/scenery.mes", System.Text.Encoding.ASCII.GetBytes("{1001}{../escape.art}\0")) }, StaticObjectFailureStage.Mapping);
        AssertStrictResourceFailure(records, new[]
        {
            ("art/scenery/scenery.mes", System.Text.Encoding.ASCII.GetBytes("{1001}{one.art}\n{2002}{two.art}\0")),
            ("art/scenery/one.art", Art()), ("art/scenery/one.art", Art()), ("art/scenery/two.art", Art())
        }, StaticObjectFailureStage.Art);
        AssertStrictResourceFailure(records, new[]
        {
            ("art/scenery/scenery.mes", System.Text.Encoding.ASCII.GetBytes("{1001}{one.art}{1001}{again.art}\n{2002}{two.art}\0")),
            ("art/scenery/one.art", Art()), ("art/scenery/two.art", Art())
        }, StaticObjectFailureStage.Mapping);
        AssertStrictResourceFailure(records, new[]
        {
            ("art/scenery/scenery.mes", System.Text.Encoding.ASCII.GetBytes("{1001}{one.art}\n{2002}{two.art}\0")),
            ("art/scenery/two.art", Art())
        }, StaticObjectFailureStage.Art);


    }

    [TestMethod]
    public void Inspect_enforces_archive_and_prototype_caps_with_typed_sanitized_diagnostics()
    {
        using var archiveFixture = new Fixture(Record(7001, 0x40000000, 0, 0), isSector: false,
            resourceEntries: [("first", [1]), ("second", [2])]);
        AssertStaticFailure(() => archiveFixture.Inspect(new StaticObjectLoaderOptions { MaxArchiveIndexBytes = 1 }),
            StaticObjectErrorCode.LimitExceeded, StaticObjectFailureStage.Archive);
        AssertStaticFailure(() => archiveFixture.Inspect(new StaticObjectLoaderOptions { MaxArchiveEntries = 1 }),
            StaticObjectErrorCode.LimitExceeded, StaticObjectFailureStage.Archive);

        using var prototypeFixture = new Fixture(Record(7001, null, null, 0));
        File.WriteAllBytes(Path.Combine(prototypeFixture.PrototypeRoot, "007001 - oversized-private.pro"), Prototype(7001, 0x40000000, 0));
        var prototypeFailure = AssertStaticFailure(() => prototypeFixture.Inspect(new StaticObjectLoaderOptions { MaxPrototypeBytes = 1 }),
            StaticObjectErrorCode.LimitExceeded, StaticObjectFailureStage.Prototype, 0);
        Assert.IsFalse(prototypeFailure.ToString().Contains("oversized-private", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Selected_resource_stage_enforces_mapping_resource_and_art_caps()
    {
        var records = SyntheticResourceRecords();
        var safeResources = new[]
        {
            ("art/scenery/scenery.mes", System.Text.Encoding.ASCII.GetBytes("{1001}{one.art}\n{2002}{two.art}\0")),
            ("art/scenery/one.art", Art()), ("art/scenery/two.art", Art())
        };

        AssertStrictResourceFailure(records, safeResources, StaticObjectFailureStage.Mapping,
            StaticObjectErrorCode.LimitExceeded, new StaticObjectLoaderOptions { MaxResourceBytes = 1 });
        AssertStrictResourceFailure(records, safeResources, StaticObjectFailureStage.Mapping,
            StaticObjectErrorCode.LimitExceeded, new StaticObjectLoaderOptions { MaxMetadataEntries = 1 });
        AssertStrictResourceFailure(records, safeResources, StaticObjectFailureStage.Art,
            StaticObjectErrorCode.InvalidArtSelector, new StaticObjectLoaderOptions
            {
                ArtOptions = new ArtDecoderOptions { MaxInputBytes = 1 }
            });

        var noPalette = Art();
        BinaryPrimitives.WriteUInt32LittleEndian(noPalette.AsSpan(12), 0);
        AssertStrictResourceFailure(records, new[]
        {
            ("art/scenery/scenery.mes", System.Text.Encoding.ASCII.GetBytes("{1001}{one.art}\n{2002}{two.art}\0")),
            ("art/scenery/one.art", noPalette), ("art/scenery/two.art", Art())
        }, StaticObjectFailureStage.Art, StaticObjectErrorCode.InvalidArtSelector);

        var malformed = Art();
        BinaryPrimitives.WriteInt32LittleEndian(malformed.AsSpan(8), 4);
        AssertStrictResourceFailure(records, new[]
        {
            ("art/scenery/scenery.mes", System.Text.Encoding.ASCII.GetBytes("{1001}{one.art}\n{2002}{two.art}\0")),
            ("art/scenery/one.art", malformed), ("art/scenery/two.art", Art())
        }, StaticObjectFailureStage.Art, StaticObjectErrorCode.InvalidArtSelector);

        AssertStrictResourceFailure(records, safeResources, StaticObjectFailureStage.Mapping,
            StaticObjectErrorCode.LimitExceeded, new StaticObjectLoaderOptions { MaxStringBytes = 3 });
        AssertStrictResourceFailure(records, safeResources, StaticObjectFailureStage.Art,
            StaticObjectErrorCode.LimitExceeded, new StaticObjectLoaderOptions { MaxResourceBytes = 64 });

        var nonScenery = SyntheticResourceRecords(); nonScenery[0] = Record(7101, unchecked((int)0x30080040), 0, 0, 1000, 1, -1);
        AssertStrictResourceFailure(nonScenery, safeResources, StaticObjectFailureStage.Art, StaticObjectErrorCode.InvalidArtSelector);
        var invalidGroup = SyntheticResourceRecords(); invalidGroup[0] = Record(7101, unchecked((int)0x40080840), 0, 0, 1000, 1, -1);
        AssertStrictResourceFailure(invalidGroup, safeResources, StaticObjectFailureStage.Art, StaticObjectErrorCode.InvalidArtSelector);
        var invalidFrame = SyntheticResourceRecords(); invalidFrame[0] = Record(7101, unchecked((int)0x40084040), 0, 0, 1000, 1, -1);
        AssertStrictResourceFailure(invalidFrame, safeResources, StaticObjectFailureStage.Art, StaticObjectErrorCode.InvalidArtSelector);
    }


    [TestMethod]
    public async Task Inspect_serializes_caller_owned_seek_based_archives_and_preserves_caller_ownership()
    {
        var moduleBytes = Dat(("maps\\fixture\\object.sec", Sector(Record(7001, 0x40000000, 0, 0))));
        using var moduleStream = new OverlapDetectingStream(moduleBytes);
        moduleStream.ArmBarrier(2);
        await Task.WhenAll(Task.Run(() => moduleStream.Read(new byte[1], 0, 1)), Task.Run(() => moduleStream.Read(new byte[1], 0, 1)));
        Assert.AreEqual(2, moduleStream.MaxConcurrentReads, "the barrier-backed stream must prove the unsynchronized negative control overlaps");

        moduleStream.Position = 0;
        moduleStream.DisarmAndReset();
        using var module = DatArchive.Open(moduleStream, leaveOpen: true);
        using var resourceStream = new MemoryStream(Dat());
        using var resource = DatArchive.Open(resourceStream, leaveOpen: true);
        var root = Path.Combine(Path.GetTempPath(), $"static-object-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var source = new StaticObjectSource(module, "maps\\fixture\\object.sec", resource);
            var prototypes = new StaticObjectPrototypeSource(root);
            moduleStream.Arm();
            using var start = new Barrier(2);
            await Task.WhenAll(
                Task.Run(() => { start.SignalAndWait(); return StaticObjectLoader.Inspect(source, prototypes); }),
                Task.Run(() => { start.SignalAndWait(); return StaticObjectLoader.Inspect(source, prototypes); }));
            Assert.AreEqual(1, moduleStream.MaxConcurrentReads, "DatArchive must serialize loader-level reads against a shared seek stream");
            Assert.AreEqual(1, module.Entries.Count, "the caller still owns and can use the archive after inspection");
            Assert.IsTrue(moduleStream.CanRead, "the loader must not dispose a caller-owned stream");
            Assert.IsTrue(module.ReadEntry(module.Entries[0]).Length > 0, "caller archive remains reusable after inspection");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Inspect_rejects_an_invalid_prototype_with_a_prototype_stage_and_sanitized_message()
    {
        using var fixture = new Fixture(Record(7001, null, null, 0));
        File.WriteAllBytes(Path.Combine(fixture.PrototypeRoot, "007001 - injected-private-name.pro"), [1]);

        var failure = AssertThrows(() => fixture.Inspect());
        Assert.AreEqual(StaticObjectErrorCode.InvalidRecord, failure.Code);
        Assert.AreEqual(StaticObjectFailureStage.Prototype, failure.Stage);
        Assert.IsFalse(failure.ToString().Contains("injected-private-name", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Validate_original_data_rejects_untrusted_synthetic_paths_without_retaining_private_names()
    {
        var root = Path.Combine(Path.GetTempPath(), $"static-object-private-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var modulePath = Path.Combine(root, "private-module-input.dat");
        var resourcePath = Path.Combine(root, "private-resource-input.dat");
        File.WriteAllBytes(modulePath, Dat());
        File.WriteAllBytes(resourcePath, Dat());
        try
        {
            var failure = AssertThrows(() => StaticObjectLoader.ValidateOriginalData(modulePath, resourcePath, root));
            Assert.AreEqual(StaticObjectErrorCode.InvalidSelector, failure.Code);
            Assert.AreEqual(StaticObjectFailureStage.FingerprintInput, failure.Stage);
            Assert.IsFalse(failure.ToString().Contains("private-module-input", StringComparison.Ordinal));
            Assert.IsFalse(failure.ToString().Contains("private-resource-input", StringComparison.Ordinal));
            File.Delete(modulePath);
            File.Delete(resourcePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    [TestCategory("RealData")]
    public void RealData_opt_in_validation_reconciles_exact_selected_chains_and_identifier_only_prototype_evidence()
    {
        var modulePath = Environment.GetEnvironmentVariable("ARCANUM_STATIC_MODULE");
        var resourcePath = Environment.GetEnvironmentVariable("ARCANUM_STATIC_RESOURCE");
        var prototypeRoot = Environment.GetEnvironmentVariable("ARCANUM_STATIC_PROTOTYPES");
        if (string.IsNullOrWhiteSpace(modulePath) || string.IsNullOrWhiteSpace(resourcePath) || string.IsNullOrWhiteSpace(prototypeRoot))
            Assert.Fail("ARCANUM_STATIC_MODULE, ARCANUM_STATIC_RESOURCE, and ARCANUM_STATIC_PROTOTYPES are required for this opt-in test.");
        var validation = StaticObjectLoader.ValidateOriginalData(modulePath!, resourcePath!, prototypeRoot!);
        Assert.AreEqual(355938305L, validation.Module.Length);
        Assert.AreEqual("8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f", validation.Module.Sha256);
        Assert.AreEqual(147991122L, validation.Resource.Length);
        Assert.AreEqual("4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d", validation.Resource.Sha256);
        Assert.AreEqual(6, validation.PrototypeFingerprints.Count);
        Assert.IsTrue(validation.PrototypeFingerprints.All(item => item.Fingerprint.Length > 0));
        CollectionAssert.AreEqual(new[]
        {
            "4026:414:1378ddb05e981098eca7816b22a366e6733c9df7c580ccc3f19345f782319874",
            "4028:414:c2442deca958715bc49f5f0504066f951a7d549e5787be41dcded2257e23d6e3",
            "4030:406:f15e7b0ed999ac9eeabcafa1bcddbd55b96dcbba9b2d71a7d485024c06f9c310",
            "4031:414:3c5ad2f204a010b3908d0a02e693128e9f8ff421b4a7dafb7ee3e78b66420c53",
            "4038:414:2531e1a1c6ddca2f483a2d557bebcfd6c1b0b89bd2d996a3f559adc28924214b",
            "4048:378:ec807b77ea283d8d84387993a439953b7acf1a1ef7bdfb30b6a86a334dcfe8d1"
        }, validation.PrototypeFingerprints.Select(item => $"{item.Aid}:{item.Fingerprint.Length}:{item.Fingerprint.Sha256}").ToArray());
        Assert.AreEqual("00d5610fc295b84a8dd355d843b0307d0dff5f73e6d353ed25c81667882d65d6", validation.PrototypeAggregateSha256);
        var report = validation.Inspection;
        Assert.AreEqual(317, report.TerminalRecordCount);
        Assert.AreEqual(317, report.FieldResolvedCount);
        Assert.AreEqual(288, report.Records.Count(record => !record.CurrentArtInherited));
        Assert.AreEqual(29, report.Records.Count(record => record.CurrentArtInherited));
        Assert.AreEqual(31, report.Records.Count(record => !record.FlagsInherited));
        Assert.AreEqual(286, report.Records.Count(record => record.FlagsInherited));
        Assert.AreEqual(317, report.Records.Count(record => record.IsStatic == true));
        Assert.AreEqual(5, report.ResourceResolvedCount);
        Assert.AreEqual(0, report.ResourceNotEvaluatedCount);
        Assert.AreEqual(312, report.ResourceOutsideScopeCount);
        AssertChain(report.Records[0], 0, 4030, 354592500050738, 0x40280140, 2, -15, 0x00400c30u, false, 5, 5, 62, 57, 34, 39);
        AssertChain(report.Records[1], 1, 4027, 354596795017989, 0x40700080, -3, 14, 0x00400800u, true, 2, 14, 204, 267, 113, 262);
        AssertChain(report.Records[2], 2, 4030, 354596795017990, 0x40280140, -21, 8, 0x00400c30u, false, 5, 5, 62, 57, 34, 39);
        AssertChain(report.Records[3], 3, 4030, 354596795017990, 0x40280140, 19, 10, 0x00400c30u, false, 5, 5, 62, 57, 34, 39);
        AssertChain(report.Records[4], 4, 4027, 354596795018028, 0x40700080, -4, -10, 0x00400800u, true, 2, 14, 204, 267, 113, 262);
    }

    private static byte[][] SyntheticResourceRecords()
    {
        return
        [
            Record(7101, unchecked((int)0x40080040), 0, 0, 1000, 1, -1),
            Record(7102, unchecked((int)0x40100080), 0, 0, 1001, 2, -2),
            Record(7103, unchecked((int)0x40080040), 0, 0, 1002, 3, -3),
            Record(7104, unchecked((int)0x40100080), 0, 0, 1003, 4, -4),
            Record(7105, unchecked((int)0x40080040), 0, 0, 1004, 5, -5)
        ];
    }

    private static void AssertSyntheticChain(StaticObjectRecord record, int prototype, long location, int art, int x, int y, int category, int number)
    {
        Assert.AreEqual(3, record.ObjectType); Assert.AreEqual(prototype, record.PrototypeAid); Assert.AreEqual(location, record.Location);
        Assert.AreEqual((int)(location & 0x3f), record.LocalX); Assert.AreEqual((int)((location >> 32) & 0x3f), record.LocalY);
        Assert.AreEqual(art, record.DirectCurrentArt); Assert.AreEqual(art, record.CurrentArt); Assert.IsFalse(record.CurrentArtInherited);
        Assert.AreEqual(x, record.DirectOffsetX); Assert.AreEqual(y, record.DirectOffsetY); Assert.AreEqual(StaticObjectFieldStatus.Resolved, record.FieldStatus);
        Assert.AreEqual(0u, record.DirectFlags); Assert.AreEqual(0u, record.EffectiveFlags); Assert.IsFalse(record.FlagsInherited);
        Assert.IsTrue(record.IsStatic); Assert.AreEqual(StaticObjectResourceStatus.Resolved, record.ResourceStatus);
        Assert.IsNotNull(record.ArtMetadata); Assert.AreEqual(4, record.ArtMetadata.ArtType); Assert.AreEqual(category, record.ArtMetadata.SceneryCategory);
        Assert.AreEqual(number, record.ArtMetadata.ArtNumber); Assert.AreEqual(0, record.ArtMetadata.RotationGroup); Assert.AreEqual(0, record.ArtMetadata.Frame);
        Assert.AreEqual(0, record.ArtMetadata.Palette); Assert.AreEqual(0, record.ArtMetadata.EffectivePalette); Assert.AreEqual(1, record.ArtMetadata.Width);
        Assert.AreEqual(1, record.ArtMetadata.Height); Assert.AreEqual(0, record.ArtMetadata.HotX); Assert.AreEqual(0, record.ArtMetadata.HotY);
        Assert.AreEqual(0, record.ArtMetadata.ArtOffsetX); Assert.AreEqual(0, record.ArtMetadata.ArtOffsetY);
    }

    private static void AssertChain(StaticObjectRecord record, int sourceIndex, int prototype, long location, int art, int x, int y, uint flags, bool flagsDirect, int category, int number, int width, int height, int hotX, int hotY)
    {
        Assert.AreEqual(sourceIndex, record.SourceIndex); Assert.AreEqual(3, record.ObjectType); Assert.AreEqual(prototype, record.PrototypeAid); Assert.AreEqual(location, record.Location); Assert.AreEqual((int)(location & 0x3f), record.LocalX); Assert.AreEqual((int)((location >> 32) & 0x3f), record.LocalY); Assert.AreEqual(art, record.DirectCurrentArt); Assert.AreEqual(art, record.CurrentArt); Assert.IsFalse(record.CurrentArtInherited); if (flagsDirect) Assert.AreEqual(flags, record.DirectFlags); else Assert.IsNull(record.DirectFlags); Assert.AreEqual(flags, record.EffectiveFlags); Assert.AreEqual(!flagsDirect, record.FlagsInherited); Assert.AreEqual(StaticObjectFieldStatus.Resolved, record.FieldStatus); Assert.IsTrue(record.IsStatic); Assert.AreEqual(StaticObjectResourceStatus.Resolved, record.ResourceStatus);
        Assert.AreEqual(x, record.DirectOffsetX); Assert.AreEqual(y, record.DirectOffsetY);
        Assert.AreEqual(category, record.ArtMetadata!.SceneryCategory); Assert.AreEqual(number, record.ArtMetadata.ArtNumber);
        Assert.AreEqual(width, record.ArtMetadata.Width); Assert.AreEqual(height, record.ArtMetadata.Height);
        Assert.AreEqual(hotX, record.ArtMetadata.HotX); Assert.AreEqual(hotY, record.ArtMetadata.HotY);
        Assert.AreEqual(4, record.ArtMetadata.ArtType); Assert.AreEqual(0, record.ArtMetadata.RotationGroup); Assert.AreEqual(0, record.ArtMetadata.Frame); Assert.AreEqual(0, record.ArtMetadata.Palette); Assert.AreEqual(0, record.ArtMetadata.EffectivePalette); Assert.AreEqual(0, record.ArtMetadata.ArtOffsetX); Assert.AreEqual(0, record.ArtMetadata.ArtOffsetY);
    }

    private static void AssertFailure(byte[] sector, StaticObjectErrorCode code)
    {
        using var fixture = new Fixture(sector, isSector: true);
        var exception = AssertThrows(() => fixture.Inspect());
        Assert.AreEqual(code, exception.Code);
    }

    private static void AssertStrictResourceFailure(byte[][] records, (string Name, byte[] Payload)[] resources, StaticObjectFailureStage stage,
        StaticObjectErrorCode code = StaticObjectErrorCode.UnresolvedResource, StaticObjectLoaderOptions? options = null, int? sourceIndex = 0)
    {
        using var fixture = new Fixture(Sector(records), true, resources, "maps\\fixture\\object.sec");
        var failure = AssertThrows(() => StaticObjectLoader.InspectSelectedResources(fixture.Source, fixture.Prototypes, options));
        Assert.AreEqual(code, failure.Code);
        Assert.AreEqual(stage, failure.Stage);
        Assert.AreEqual(sourceIndex, failure.SourceIndex);
    }

    private static StaticObjectException AssertStaticFailure(Action action, StaticObjectErrorCode code, StaticObjectFailureStage stage, int? sourceIndex = null)
    {
        var failure = AssertThrows(action);
        Assert.AreEqual(code, failure.Code);
        Assert.AreEqual(stage, failure.Stage);
        if (sourceIndex.HasValue) Assert.AreEqual(sourceIndex.Value, failure.SourceIndex);
        return failure;
    }

    private static StaticObjectException AssertThrows(Action action)
    {
        try { action(); }
        catch (StaticObjectException exception) { return exception; }
        Assert.Fail("Expected StaticObjectException.");
        throw new InvalidOperationException("Unreachable after assertion failure.");
    }

    private static Fixture ArrayFixture(byte[] record)
    {
        var fixture = new Fixture(record);
        File.WriteAllBytes(Path.Combine(fixture.PrototypeRoot, "007001 - array.pro"), Prototype(7001, 0x40000000, 0));
        return fixture;
    }

    private static void AssertArrayFailure(byte[] record, StaticObjectErrorCode code, StaticObjectLoaderOptions? options = null)
    {
        using var fixture = ArrayFixture(record);
        AssertStaticFailure(() => fixture.Inspect(options ?? new StaticObjectLoaderOptions()), code, StaticObjectFailureStage.Record, 0);
    }

    private static void AssertStringFailure(byte[] record, StaticObjectErrorCode code, StaticObjectLoaderOptions? options = null)
    {
        using var fixture = new Fixture(record);
        AssertStaticFailure(() => fixture.Inspect(options ?? new StaticObjectLoaderOptions()), code, StaticObjectFailureStage.Record, 0);
    }

    internal static byte[] Record(int prototypeAid, int? art, uint? flags, uint spellFlags, long? location = 0, int? offsetX = 0, int? offsetY = 0, int version = 119, int type = 3, int fieldCountDelta = 0, bool badLocationPresence = false)
    {
        var bytes = new List<byte>();
        I32(bytes, version); ObjectId(bytes, 1, prototypeAid); ObjectId(bytes, 1, 1); I32(bytes, type);
        uint mask = 0; if (art.HasValue) mask |= 1u; if (location.HasValue || badLocationPresence) mask |= 1u << 1; if (offsetX.HasValue) mask |= 1u << 2; if (offsetY.HasValue) mask |= 1u << 3; if (flags.HasValue) mask |= 1u << 18; mask |= 1u << 19;
        I16(bytes, checked((short)(BitOperations.PopCount(mask) + fieldCountDelta))); U32(bytes, mask); U32(bytes, 0); U32(bytes, 0);
        if (art.HasValue) I32(bytes, art.Value); if (location.HasValue || badLocationPresence) { bytes.Add(badLocationPresence ? (byte)2 : (byte)1); if (!badLocationPresence) I64(bytes, location!.Value); }
        if (offsetX.HasValue) I32(bytes, offsetX.Value); if (offsetY.HasValue) I32(bytes, offsetY.Value); if (flags.HasValue) U32(bytes, flags.Value); U32(bytes, spellFlags);
        return bytes.ToArray();
    }

    internal static byte[] ArrayRecord(int size, int count, int words, uint[] bitset, bool includeElements)
    {
        var bytes = new List<byte>();
        I32(bytes, 119); ObjectId(bytes, 1, 7001); ObjectId(bytes, 1, 1); I32(bytes, 3);
        I16(bytes, 1); U32(bytes, 1u << 5); U32(bytes, 0); U32(bytes, 0);
        bytes.Add(1); I32(bytes, size); I32(bytes, count); I32(bytes, 0);
        if (includeElements && count > 0 && size > 0)
            bytes.AddRange(new byte[checked(size * count)]);
        I32(bytes, words);
        foreach (var word in bitset) U32(bytes, word);
        return bytes.ToArray();
    }

    internal static byte[] StringRecord(int length, byte[] value)
    {
        var bytes = new List<byte>();
        I32(bytes, 119); ObjectId(bytes, 1, 7001); ObjectId(bytes, 1, 1); I32(bytes, 15);
        I16(bytes, 1); U32(bytes, 0); U32(bytes, 0); U32(bytes, 0); U32(bytes, 0); U32(bytes, 1u << 17);
        bytes.Add(1); I32(bytes, length); bytes.AddRange(value);
        return bytes.ToArray();
    }

    internal static byte[] Prototype(int aid, int art, uint flags)
    {
        var bytes = new List<byte>(); I32(bytes, 119); ObjectId(bytes, -1, 0); ObjectId(bytes, 1, aid); I32(bytes, 3);
        U32(bytes, 0); U32(bytes, 0); U32(bytes, 0); I32(bytes, art);
        var optionalFields = new HashSet<int> { 1, 5, 6, 7, 15, 16, 17, 30, 31, 34, 35, 37, 40, 41 };
        for (var field = 1; field < 42; field++)
        {
            if (field == 18) U32(bytes, flags);
            else if (optionalFields.Contains(field)) bytes.Add(0);
            else I32(bytes, 0);
        }
        return bytes.ToArray();
    }

    internal static byte[] Sector(params byte[][] records)
    {
        var objectBytes = records.SelectMany(record => record).ToArray(); var bytes = new byte[4 + (4096 * 4) + 4 + 4 + objectBytes.Length + 4]; var offset = 4 + (4096 * 4);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), 1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 4), 0xAA0000); objectBytes.CopyTo(bytes, offset + 8);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(bytes.Length - 4), records.Length); return bytes;
    }

    private static byte[] Art()
    {
        var bytes = new byte[132 + 1024 + 28 + 1]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32), 1); var frame = 132 + 1024;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame), 1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 4), 1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 8), 1); bytes[^1] = 1; return bytes;
    }

    private static byte[] Dat(params (string Name, byte[] Payload)[] entries)
        => DatWithMetadata(entries.Select(entry => (entry.Name, entry.Payload, 1u, (uint?)null)).ToArray());

    private static byte[] DatWithMetadata(params (string Name, byte[] Payload, uint Flags, uint? DeclaredSize)[] entries)
    {
        using var output = new MemoryStream(); var offsets = new List<uint>(); foreach (var entry in entries) { offsets.Add(checked((uint)output.Position)); output.Write(entry.Payload); }
        using var index = new MemoryStream(); U32(index, checked((uint)entries.Length)); for (var i = 0; i < entries.Length; i++) { var name = System.Text.Encoding.ASCII.GetBytes(entries[i].Name + "\0"); U32(index, checked((uint)name.Length)); index.Write(name); U32(index, 0); U32(index, entries[i].Flags); U32(index, checked((uint)entries[i].Payload.Length)); U32(index, entries[i].DeclaredSize ?? checked((uint)entries[i].Payload.Length)); U32(index, offsets[i]); }
        index.Position = 0; index.CopyTo(output); output.Write(new byte[16]); output.Write("1TAD"u8); U32(output, 0); U32(output, checked((uint)(index.Length + 28))); return output.ToArray();
    }

    private static void ObjectId(List<byte> bytes, short kind, int aid) { I16(bytes, kind); bytes.AddRange(new byte[6]); var payload = new byte[16]; BinaryPrimitives.WriteInt32LittleEndian(payload, aid); bytes.AddRange(payload); }
    private static void I16(List<byte> bytes, short value) { var raw = new byte[2]; BinaryPrimitives.WriteInt16LittleEndian(raw, value); bytes.AddRange(raw); }
    private static void I32(List<byte> bytes, int value) { var raw = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(raw, value); bytes.AddRange(raw); }
    private static void I64(List<byte> bytes, long value) { var raw = new byte[8]; BinaryPrimitives.WriteInt64LittleEndian(raw, value); bytes.AddRange(raw); }
    private static void U32(List<byte> bytes, uint value) { var raw = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(raw, value); bytes.AddRange(raw); }
    private static void U32(Stream stream, uint value) { Span<byte> raw = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(raw, value); stream.Write(raw); }

    internal sealed class Fixture : IDisposable
    {
        private readonly MemoryStream _moduleBytes; private readonly MemoryStream _resourceBytes;
        public Fixture(params byte[][] records) : this(Sector(records), isSector: true) { }
        public Fixture(byte[] sector, bool isSector, (string Name, byte[] Payload)[]? resourceEntries = null,
            string moduleEntryPath = "maps\\fixture\\object.sec")
        {
            PrototypeRoot = Path.Combine(Path.GetTempPath(), $"static-object-test-{Guid.NewGuid():N}"); Directory.CreateDirectory(PrototypeRoot);
            ModuleBuffer = Dat((moduleEntryPath, sector)); ResourceBuffer = Dat(resourceEntries ?? []);
            _moduleBytes = new MemoryStream(ModuleBuffer); _resourceBytes = new MemoryStream(ResourceBuffer);
            Module = DatArchive.Open(_moduleBytes, leaveOpen: true); Resource = DatArchive.Open(_resourceBytes, leaveOpen: true);
            Source = new StaticObjectSource(Module, moduleEntryPath, Resource); Prototypes = new StaticObjectPrototypeSource(PrototypeRoot);
        }
        public string PrototypeRoot { get; }
        public byte[] ModuleBuffer { get; }
        public byte[] ResourceBuffer { get; }
        public DatArchive Module { get; }
        public DatArchive Resource { get; }
        public StaticObjectSource Source { get; }
        public StaticObjectPrototypeSource Prototypes { get; }
        public StaticObjectInspection Inspect() => StaticObjectLoader.Inspect(Source, Prototypes);
        public StaticObjectInspection Inspect(StaticObjectLoaderOptions options) => StaticObjectLoader.Inspect(Source, Prototypes, options);
        public void Dispose() { Module.Dispose(); Resource.Dispose(); _moduleBytes.Dispose(); _resourceBytes.Dispose(); Directory.Delete(PrototypeRoot, recursive: true); }
    }

    private sealed class OverlapDetectingStream : Stream
    {
        private readonly MemoryStream _inner;
        private int _active;
        private int _maxConcurrentReads;
        private int _armed;
        private Barrier? _barrier;

        public OverlapDetectingStream(byte[] bytes) => _inner = new MemoryStream(bytes, writable: false);
        public int MaxConcurrentReads => Volatile.Read(ref _maxConcurrentReads);
        public void Arm() => Volatile.Write(ref _armed, 1);
        public void ArmBarrier(int participantCount) { _barrier = new Barrier(participantCount); Volatile.Write(ref _armed, 1); }
        public void DisarmAndReset()
        {
            Volatile.Write(ref _armed, 0); _barrier?.Dispose(); _barrier = null;
            Volatile.Write(ref _active, 0); Volatile.Write(ref _maxConcurrentReads, 0);
        }
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count)
        {
            BeginRead();
            try { return _inner.Read(buffer, offset, count); }
            finally { EndRead(); }
        }
        public override int Read(Span<byte> buffer)
        {
            BeginRead();
            try { return _inner.Read(buffer); }
            finally { EndRead(); }
        }
        private void BeginRead()
        {
            if (Volatile.Read(ref _armed) == 0) return;
            var active = Interlocked.Increment(ref _active);
            while (true)
            {
                var maximum = Volatile.Read(ref _maxConcurrentReads);
                if (active <= maximum || Interlocked.CompareExchange(ref _maxConcurrentReads, active, maximum) == maximum) break;
            }
            _barrier?.SignalAndWait();
        }
        private void EndRead() { if (Volatile.Read(ref _armed) != 0) Interlocked.Decrement(ref _active); }
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}

using System.Buffers.Binary;
using Arcanum.Data.StaticObjects;
using Arcanum.Data.Terrain;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.StaticObjects.StaticObjectLoaderTests;

namespace Arcanum.Data.Tests.StaticObjects;

[TestClass]
public sealed class StaticObstructionInspectorTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(0u, "Blocker")]
    [DataRow(1024u, "NoBlock")]
    [DataRow(48u, "Blocker")]
    [DataRow(1072u, "NoBlock")]
    [DataRow(4096u, "INVENTORY_EXCLUDED")]
    [DataRow(5123u, "INVENTORY_EXCLUDED")]
    [DataRow(1u, "DESTROYED_EXCLUDED")]
    [DataRow(3u, "DESTROYED_EXCLUDED")]
    [DataRow(1025u, "DESTROYED_EXCLUDED")]
    [DataRow(2u, "OFF_EXCLUDED")]
    [DataRow(1026u, "OFF_EXCLUDED")]
    [DataRow(8192u, "DYNAMIC_EXCLUDED")]
    [DataRow(13315u, "DYNAMIC_EXCLUDED")]
    public void Literal_flags_matrix_and_ordering_through_production_parser(uint flags, string expected)
    {
        // Unresolved ART, extreme visual offsets and opposite slot-19 sentinel are irrelevant.
        using var fixture = new Fixture(Record(7001, null, flags, 0xffffffff, World(92933, 82561), int.MinValue, int.MaxValue),
            Record(7001, -1, null, 0xffffffff, World(92933, 82561), int.MaxValue, int.MinValue));
        PutPrototype(fixture, Prototype(7001, 123, flags));
        var report = Inspect(fixture);
        var record = report.Records[0];
        var inherited = report.Records[1];
        Assert.AreEqual(StaticObstructionFlagsSource.Prototype, inherited.FlagsSource);
        Assert.AreEqual(new StaticObstructionLocalCell(5, 1), record.Destination);
        Assert.AreEqual(StaticObstructionFlagsSource.Direct, record.FlagsSource);
        Assert.AreEqual(flags, record.EffectiveFlags);
        if (expected is "Blocker" or "NoBlock")
        {
            AssertResult(record.Result, "Supported", "Applicable", expected);
            AssertResult(inherited.Result, "Supported", "Applicable", expected);
            Assert.AreEqual(expected, report.GetCell(5, 1).Result.Disposition.ToString());
        }
        else
        {
            AssertResult(record.Result, expected == "DYNAMIC_EXCLUDED" ? "Unsupported" : "Supported",
                expected == "DYNAMIC_EXCLUDED" ? "Unknown" : "NotApplicable", null);
            AssertResult(inherited.Result, expected == "DYNAMIC_EXCLUDED" ? "Unsupported" : "Supported",
                expected == "DYNAMIC_EXCLUDED" ? "Unknown" : "NotApplicable", null);
            Assert.AreEqual(expected, record.Result.ReasonCode);
            Assert.AreEqual(0, report.GetCell(5, 1).Contributors.Count);
        }
        Assert.IsNull(report.InputIdentity, "synthetic structural inspection must never claim original identity");
    }

    [TestMethod]
    [DataRow(0u, 1024u, "Blocker")]
    [DataRow(1024u, 0u, "NoBlock")]
    public void Direct_slot18_beats_opposite_matching_default_and_slot19(uint direct, uint inherited, string expected)
    {
        // ART absence forces the old loader to read this opposite-value default;
        // obstruction flags remain direct, and a second record exercises actual inheritance.
        using var fixture = new Fixture(Record(7001, null, direct, inherited, World(92930, 82563)),
            Record(7001, 123, null, direct, World(92931, 82563)));
        PutPrototype(fixture, Prototype(7001, -1, inherited));
        Assert.AreEqual(direct, fixture.Inspect().Records[0].EffectiveFlags);
        var report = Inspect(fixture);
        AssertResult(report.Records[0].Result, "Supported", "Applicable", expected);
        Assert.AreEqual(StaticObstructionFlagsSource.Direct, report.Records[0].FlagsSource);
        AssertResult(report.Records[1].Result, "Supported", "Applicable", expected == "Blocker" ? "NoBlock" : "Blocker");
        Assert.AreEqual(StaticObstructionFlagsSource.Prototype, report.Records[1].FlagsSource);
        var inheritedProvenance = report.Records[1].Result.Provenance.Single(p => p.Kind == StaticObstructionProvenanceKind.PrototypeDefault);
        Assert.AreEqual(7001, inheritedProvenance.PrototypeAid);
        Assert.AreEqual("SYNTHETIC_PROTOTYPES", inheritedProvenance.SourceId);
        var instance = report.Records[0].Result.Provenance.Single(p => p.Kind == StaticObstructionProvenanceKind.SerializedInstance);
        Assert.AreEqual(0, instance.SourceIndex); Assert.AreEqual(0, instance.EntryOrdinal);
        Assert.AreEqual("SYNTHETIC_MODULE", instance.SourceId);
    }

    [TestMethod]
    public void Flags_resolution_is_independent_of_art_and_required_prototype_failures_preserve_unknown()
    {
        using var fixture = new Fixture(Record(7001, null, 0, 0, World(92930, 82563)),
            Record(7002, 1, null, 0, World(92931, 82563)), Record(7003, 1, null, 0, World(92932, 82563)));
        var mismatchedId = Prototype(7004, 0, 1024);
        PutPrototype(fixture, mismatchedId, "007002 - id-private.pro");
        var mismatchedType = Prototype(7003, 0, 1024);
        BinaryPrimitives.WriteInt32LittleEndian(mismatchedType.AsSpan(52), 2);
        PutPrototype(fixture, mismatchedType, "007003 - type-private.pro");
        var report = Inspect(fixture);
        AssertResult(report.Records[0].Result, "Supported", "Applicable", "Blocker");
        foreach (var record in report.Records.Skip(1))
        {
            AssertResult(record.Result, "Unknown", "Unknown", null);
            Assert.AreEqual("FLAGS_UNRESOLVED", record.Result.ReasonCode);
            Assert.AreEqual(StaticObstructionFlagsSource.Unresolved, record.FlagsSource);
            Assert.AreEqual(StaticObstructionLocationStatus.InRange, record.LocationStatus);
        }
        AssertResult(report.GetCell(3, 3).Result, "Unknown", "Unknown", null);
        Assert.AreEqual(StaticObjectErrorCode.UnresolvedReference, Throws<StaticObjectException>(() => fixture.Inspect()).Code,
            "strict loader contract must not be weakened");
    }

    [TestMethod]
    public void Missing_duplicate_non_typeA_and_out_of_identifier_range_defaults_never_invent_flags()
    {
        using var fixture = new Fixture(Record(7001, 1, null, 0, World(92930, 82563)));
        AssertResult(Inspect(fixture).Records[0].Result, "Unknown", "Unknown", null);
        PutPrototype(fixture, Prototype(7001, 1, 0));
        PutPrototype(fixture, Prototype(7001, 1, 1024), "007001 - duplicate.pro");
        AssertResult(Inspect(fixture).Records[0].Result, "Unknown", "Unknown", null);
        Assert.AreEqual(StaticObjectErrorCode.UnresolvedReference, Throws<StaticObjectException>(() => fixture.Inspect()).Code);
        foreach (var aid in new[] { -1, 1000000 })
        {
            using var invalidId = new Fixture(Record(aid, 1, null, 0, World(92930, 82563)));
            AssertResult(Inspect(invalidId).Records[0].Result, "Unknown", "Unknown", null);
        }
        var nonTypeA = Record(7001, 1, null, 0, World(92930, 82563));
        BinaryPrimitives.WriteInt16LittleEndian(nonTypeA.AsSpan(4), 2);
        using var nonA = new Fixture(nonTypeA);
        Assert.IsNull(Inspect(nonA).Records[0].PrototypeAid);
        AssertResult(Inspect(nonA).Records[0].Result, "Unknown", "Unknown", null);
        var missingDirectory = new StaticObjectPrototypeSource(Path.Combine(fixture.PrototypeRoot, "absent"));
        AssertResult(StaticObstructionInspector.InspectSector(fixture.Source, missingDirectory, "MODULE", "PROTOTYPES", Selector(fixture)).Records[0].Result,
            "Unknown", "Unknown", null);
    }

    [TestMethod]
    public void Raw_location_range_overflow_and_absence_are_not_masked_or_inherited()
    {
        foreach (var (raw, expectedStatus) in new (long?, string)[]
        {
            (null, "Absent"), (0, "OutOfRange"), (World(92992, 82560), "OutOfRange"),
            (World(92927, 82560), "OutOfRange"), (World(92928, 82624), "OutOfRange"),
            (World(92928, 82559), "OutOfRange"), (World(int.MinValue, int.MaxValue), "Overflow"),
            (World(int.MaxValue, int.MinValue), "Overflow")
        })
        {
            using var fixture = new Fixture(Record(7001, 1, 0x1003, 0, raw));
            PutPrototype(fixture, Prototype(7001, 1, 1024));
            var report = Inspect(fixture); var record = report.Records.Single();
            Assert.AreEqual(expectedStatus, record.LocationStatus.ToString());
            Assert.IsNull(record.Destination);
            AssertResult(record.Result, "Unknown", "Unknown", null, "location validity precedes inventory/destroyed/off");
            Assert.IsFalse(report.LocationCoverageComplete);
            Assert.AreEqual(1, report.UnknownLocationRecords.Count);
            Assert.AreEqual(0, report.GetCell(0, 0).Contributors.Count, "unknown location is not (0,0)");
            AssertResult(report.GetCell(0, 0).Result, "Unknown", "Unknown", null);
        }
        using var edges = new Fixture(Record(7001, 1, 0, 0, World(92928, 82560)), Record(7001, 1, 1024, 0, World(92991, 82623)));
        Assert.AreEqual(new StaticObstructionLocalCell(0, 0), Inspect(edges).Records[0].Destination);
        Assert.AreEqual(new StaticObstructionLocalCell(63, 63), Inspect(edges).Records[1].Destination);
        // Prototype contains a location, but absent instance location still remains absent.
        using var absent = new Fixture(Record(7001, null, null, 0, null));
        var prototype = Prototype(7001, 1, 0).ToList();
        prototype[72] = 1; // 68-byte prototype header, then four-byte current ART.
        var packed = new byte[8]; BinaryPrimitives.WriteInt64LittleEndian(packed, World(92930, 82563));
        prototype.InsertRange(73, packed); PutPrototype(absent, prototype.ToArray());
        AssertResult(Inspect(absent).Records[0].Result, "Unknown", "Unknown", null);
        Assert.AreEqual(StaticObstructionLocationStatus.Absent, Inspect(absent).Records[0].LocationStatus);
    }

    [TestMethod]
    public void Aggregation_blocker_dominance_unknown_known_cell_and_separate_unknown_location_coverage()
    {
        using var fixture = new Fixture(
            Record(7001, 1, 1024, 0, World(92930, 82563)),
            Record(7002, 1, null, 0, World(92930, 82563)),
            Record(7001, 1, 0, 0, World(92931, 82563)),
            Record(7002, 1, null, 0, World(92931, 82563)),
            Record(7001, 1, 1024, 0, World(92932, 82563)),
            Record(7001, 1, 1024, 0, World(92932, 82563)));
        var report = Inspect(fixture);
        AssertResult(report.GetCell(2, 3).Result, "Unknown", "Unknown", null);
        AssertResult(report.GetCell(3, 3).Result, "Supported", "Applicable", "Blocker");
        AssertResult(report.GetCell(4, 3).Result, "Supported", "Applicable", "NoBlock");
        AssertResult(report.GetCell(5, 3).Result, "Supported", "Applicable", "NoBlock");
        CollectionAssert.AreEqual(new[] { 2, 3 }, report.GetCell(3, 3).Contributors.Select(r => r.SourceIndex).ToArray());
        Assert.IsTrue(report.LocationCoverageComplete);
        using var uncertain = new Fixture(Record(7001, 1, 0, 0, World(92931, 82563)), Record(7001, 1, 0, 0, null));
        var coverage = Inspect(uncertain);
        AssertResult(coverage.GetCell(3, 3).Result, "Supported", "Applicable", "Blocker");
        Assert.IsFalse(coverage.GetCell(3, 3).LocationCoverageComplete);
        AssertResult(coverage.GetCell(4, 3).Result, "Unknown", "Unknown", null);
        using var reversed = new Fixture(Record(7002, 1, null, 0, World(92931, 82563)),
            Record(7001, 1, 0, 0, World(92931, 82563)));
        AssertResult(Inspect(reversed).GetCell(3, 3).Result, "Supported", "Applicable", "Blocker");
        using var dynamicMissingLocation = new Fixture(Record(7001, 1, 0x3003, 0, null));
        var excluded = Inspect(dynamicMissingLocation);
        AssertResult(excluded.Records[0].Result, "Unsupported", "Unknown", null, "dynamic subset exclusion precedes location/filter shortcuts");
        Assert.AreEqual(0, excluded.UnknownLocationRecords.Count, "known excluded dynamic records are not unresolved subset candidates");
        using var empty = new Fixture(Array.Empty<byte[]>());
        AssertResult(Inspect(empty).GetCell(0, 0).Result, "Supported", "Applicable", "NoBlock");
    }

    [TestMethod]
    public void All_other_types_and_contexts_are_explicit_and_step_query_is_destination_only()
    {
        foreach (var type in Enumerable.Range(0, 18).Where(type => type != 3))
        {
            // No common fields: the existing type-specific delimiter controls mask widths.
            var bytes = new List<byte>(Record(7001, null, null, 0, null, null, null, type: type));
            // Remove slot19 and its payload; add empty type-specific mask words where required.
            bytes.RemoveRange(bytes.Count - 4, 4);
            BinaryPrimitives.WriteInt16LittleEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bytes).Slice(56), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bytes).Slice(58), 0);
            if (type is >= 5 and <= 14) bytes.AddRange(new byte[4]);
            if (type is 15 or 16) bytes.AddRange(new byte[8]);
            using var fixture = new Fixture(bytes.ToArray());
            AssertResult(Inspect(fixture).Records.Single().Result, "Unsupported", "Unknown", null);
        }
        using var source = new Fixture(Record(7001, 1, 0, 0, World(92930, 82563)));
        var report = Inspect(source);
        foreach (var context in Enum.GetValues<StaticObstructionContext>().Where(c => c != StaticObstructionContext.NormalCardinalZeroFlags).Append((StaticObstructionContext)999))
        {
            AssertResult(report.GetCell(2, 3, context).Result, "Unsupported", "Unknown", null);
            AssertResult(report.GetDestinationForStep(1, 3, 2, 3, context).Result, "Unsupported", "Unknown", null);
        }
        AssertResult(report.GetDestinationForStep(1, 3, 2, 3).Result, "Supported", "Applicable", "Blocker");
        AssertResult(report.GetDestinationForStep(2, 3, 3, 3).Result, "Supported", "Applicable", "NoBlock", "source blocker must not be applied to destination");
        foreach (var (fromX, fromY, toX, toY, reason) in new[]
        {
            (2,3,2,3,"SAME_CELL_UNSUPPORTED"), (1,2,2,3,"DIAGONAL_UNSUPPORTED"),
            (0,3,2,3,"NONADJACENT_UNSUPPORTED"), (63,3,64,3,"CROSS_SECTOR_UNSUPPORTED"),
            (int.MinValue,3,2,3,"CROSS_SECTOR_UNSUPPORTED"), (2,3,int.MaxValue,3,"CROSS_SECTOR_UNSUPPORTED")
        })
        {
            var result = report.GetDestinationForStep(fromX, fromY, toX, toY).Result;
            AssertResult(result, "Unsupported", "Unknown", null); Assert.AreEqual(reason, result.ReasonCode);
        }
        AssertResult(report.GetCell(-1, 0).Result, "Unsupported", "Unknown", null);
        AssertResult(report.GetCell(0, 64).Result, "Unsupported", "Unknown", null);
    }

    [TestMethod]
    public void Snapshot_and_all_collections_survive_input_mutation_and_disposal()
    {
        StaticObstructionSectorReport report;
        using (var fixture = new Fixture(Record(7001, null, null, 0, World(92930, 82563))))
        {
            PutPrototype(fixture, Prototype(7001, 1, 0));
            var beforeModule = fixture.ModuleBuffer.ToArray(); var beforeResource = fixture.ResourceBuffer.ToArray();
            report = Inspect(fixture);
            CollectionAssert.AreEqual(beforeModule, fixture.ModuleBuffer); CollectionAssert.AreEqual(beforeResource, fixture.ResourceBuffer);
            Array.Fill(fixture.ModuleBuffer, (byte)0); Array.Fill(fixture.ResourceBuffer, (byte)0);
            PutPrototype(fixture, Prototype(7001, 1, 1024));
            Assert.AreEqual(1, fixture.Module.Entries.Count, "inspector does not dispose archives");
        }
        AssertResult(report.GetCell(2, 3).Result, "Supported", "Applicable", "Blocker");
        Assert.AreEqual(0u, report.Records[0].EffectiveFlags);
        Throws<NotSupportedException>(() => ((IList<StaticObstructionRecordAssessment>)report.Records)[0] = report.Records[0]);
        Throws<NotSupportedException>(() => ((IList<StaticObstructionRecordAssessment>)report.GetCell(2, 3).Contributors).Clear());
        Throws<NotSupportedException>(() => ((IList<StaticObstructionProvenance>)report.Records[0].Result.Provenance).Clear());
        Throws<NotSupportedException>(() => ((IList<StaticObstructionRecordAssessment>)report.UnknownLocationRecords).Clear());
    }

    [TestMethod]
    public void Malformed_parser_inputs_and_caps_are_exceptions_not_unknown_assessments()
    {
        foreach (var (bytes, code) in new[]
        {
            (Record(7001,1,0,0)[..^1], StaticObjectErrorCode.InvalidRecord),
            (Record(7001,1,0,0, badLocationPresence:true), StaticObjectErrorCode.InvalidRecord),
            (Record(7001,1,0,0, version:118), StaticObjectErrorCode.UnsupportedVersion),
            (Record(7001,1,0,0, fieldCountDelta:1), StaticObjectErrorCode.InvalidRecord),
            (Record(7001,1,0,0).Concat(new byte[]{1}).ToArray(), StaticObjectErrorCode.InvalidFraming),
            (ArrayRecord(4,int.MaxValue,2,[],false), StaticObjectErrorCode.InvalidRecord),
            (StringRecord(3,"abcx"u8.ToArray()), StaticObjectErrorCode.InvalidRecord)
        })
        {
            using var fixture = new Fixture(bytes);
            var failure = Throws<StaticObjectException>(() => Inspect(fixture,
                new StaticObjectLoaderOptions { MaxArrayElements = int.MaxValue }));
            Assert.AreEqual(code, failure.Code); Assert.AreEqual(StaticObjectFailureStage.Record, failure.Stage);
            Assert.IsFalse(failure.ToString().Contains(fixture.PrototypeRoot, StringComparison.Ordinal));
        }
        using var capped = new Fixture(Record(7001,1,0,0),Record(7001,1,0,0));
        foreach (var limit in new[]
        {
            new StaticObjectLoaderOptions { MaxRecords = 1 }, new StaticObjectLoaderOptions { MaxSectorBytes = 1 },
            new StaticObjectLoaderOptions { MaxFields = 1 }, new StaticObjectLoaderOptions { MaxArchiveBytes = 1 },
            new StaticObjectLoaderOptions { MaxArchiveIndexBytes = 1 }
        }) Assert.AreEqual(StaticObjectErrorCode.LimitExceeded, Throws<StaticObjectException>(() => Inspect(capped,limit)).Code);
        using var proto = new Fixture(Record(7001,1,null,0,World(92930,82563)));
        PutPrototype(proto, [1]);
        Assert.AreEqual(StaticObjectErrorCode.InvalidRecord, Throws<StaticObjectException>(() => Inspect(proto)).Code);
        PutPrototype(proto, Prototype(7001,1,0));
        Assert.AreEqual(StaticObjectErrorCode.LimitExceeded, Throws<StaticObjectException>(() => Inspect(proto,new StaticObjectLoaderOptions { MaxPrototypeBytes = 1 })).Code);
        Throws<ArgumentOutOfRangeException>(() => Inspect(capped,new StaticObjectLoaderOptions { MaxRecords = 0 }));
        using var many = new Fixture(Enumerable.Range(0,317).Select(_ => Record(7001,1,1024,0,World(92930,82563))).ToArray());
        Assert.AreEqual(317, Inspect(many).GetCell(2,3).Contributors.Count);
    }

    [TestMethod]
    public void Selector_and_source_labels_are_bounded_path_free_and_fail_closed()
    {
        using var fixture = new Fixture(Record(7001,1,0,0));
        foreach (var label in new[] { "private/path", "private\\path", "C:private", "bad\nlabel", "nonascii-Ж", new string('a',129) })
            Throws<ArgumentException>(() => StaticObstructionInspector.InspectSector(fixture.Source, fixture.Prototypes,label,"PROTOTYPES",Selector(fixture)));
        var other = new TerrainSectorSelector(fixture.Source.ModuleEntryPath,"OTHER",1,1,1,0,0,64,64);
        Assert.AreEqual(StaticObjectErrorCode.InvalidSelector, Throws<StaticObjectException>(() =>
            StaticObstructionInspector.InspectSector(fixture.Source,fixture.Prototypes,"MODULE","PROTOTYPES",other)).Code);
        var wrongEntryIdentity = new TerrainSectorSelector(fixture.Source.ModuleEntryPath,"SYNTHETIC",1452,1290,86570436012,92928,82560,64,64,
            new TerrainEntryExpectation(1,1,1));
        var failure = Throws<StaticObjectException>(() => StaticObstructionInspector.InspectSector(fixture.Source,fixture.Prototypes,
            "MODULE","PROTOTYPES",wrongEntryIdentity));
        Assert.AreEqual(StaticObjectErrorCode.InvalidSelector, failure.Code);
        Assert.AreEqual(StaticObjectFailureStage.Selector, failure.Stage);
    }

    [TestMethod]
    [TestCategory("RealData")]
    public void RealData_production_component_matches_literal_census_rows_and_authenticated_inputs()
    {
        var module = Environment.GetEnvironmentVariable("ARCANUM_STATIC_MODULE");
        var resource = Environment.GetEnvironmentVariable("ARCANUM_STATIC_RESOURCE");
        var prototypes = Environment.GetEnvironmentVariable("ARCANUM_STATIC_PROTOTYPES");
        if (string.IsNullOrWhiteSpace(module) || string.IsNullOrWhiteSpace(resource) || string.IsNullOrWhiteSpace(prototypes))
            Assert.Fail("ARCANUM_STATIC_MODULE, ARCANUM_STATIC_RESOURCE and ARCANUM_STATIC_PROTOTYPES are required.");
        var report = StaticObstructionInspector.ValidateOriginalData(module!,resource!,prototypes!);
        Assert.IsNotNull(report.InputIdentity);
        Assert.AreEqual(355938305L, report.InputIdentity.Module.Length);
        Assert.AreEqual("8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f", report.InputIdentity.Module.Sha256);
        Assert.AreEqual(147991122L, report.InputIdentity.Resource.Length);
        Assert.AreEqual("4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d", report.InputIdentity.Resource.Sha256);
        Assert.AreEqual(6,report.InputIdentity.Prototypes.Count);
        Assert.AreEqual("00d5610fc295b84a8dd355d843b0307d0dff5f73e6d353ed25c81667882d65d6",report.InputIdentity.PrototypeAggregateSha256);
        Assert.AreEqual(317,report.Records.Count);
        Assert.AreEqual(31,report.Records.Count(r => r.FlagsSource == StaticObstructionFlagsSource.Direct));
        Assert.AreEqual(286,report.Records.Count(r => r.FlagsSource == StaticObstructionFlagsSource.Prototype));
        Assert.AreEqual(317,report.Records.Count(r => r.LocationStatus == StaticObstructionLocationStatus.InRange));
        Assert.AreEqual(58,report.Records.Count(r => r.Result.Disposition == StaticObstructionDisposition.Blocker));
        Assert.AreEqual(259,report.Records.Count(r => r.Result.Disposition == StaticObstructionDisposition.NoBlock));
        Assert.AreEqual(0,report.Records.Count(r => r.Result.Supportedness != StaticObstructionSupportedness.Supported));
        Assert.AreEqual(0,report.UnknownLocationRecords.Count);
        var cells = Enumerable.Range(0,4096).Select(i => report.GetCell(i%64,i/64)).ToArray();
        Assert.AreEqual(293,cells.Count(c => c.Contributors.Count > 0));
        Assert.AreEqual(22,cells.Count(c => c.Contributors.Count > 1));
        Assert.AreEqual(4,cells.Max(c => c.Contributors.Count));
        Assert.AreEqual(317,cells.Sum(c => c.Contributors.Count));
        TestContext.WriteLine($"identity module_sha256={report.InputIdentity.Module.Sha256} resource_sha256={report.InputIdentity.Resource.Sha256} prototypes_sha256={report.InputIdentity.PrototypeAggregateSha256} before_after=verified_by_loader");
        TestContext.WriteLine($"census records={report.Records.Count} direct={report.Records.Count(r => r.FlagsSource == StaticObstructionFlagsSource.Direct)} prototype={report.Records.Count(r => r.FlagsSource == StaticObstructionFlagsSource.Prototype)} blocker={report.Records.Count(r => r.Result.Disposition == StaticObstructionDisposition.Blocker)} no_block={report.Records.Count(r => r.Result.Disposition == StaticObstructionDisposition.NoBlock)} occupied={cells.Count(c=>c.Contributors.Count>0)} multi={cells.Count(c=>c.Contributors.Count>1)} max={cells.Max(c=>c.Contributors.Count)}");
        foreach (var (index,x,y,source,disposition,count) in new[]
        {
            (1,5,1,"Direct","Blocker",1), (7,38,2,"Prototype","Blocker",1), (166,19,34,"Direct","NoBlock",1),
            (0,50,0,"Prototype","NoBlock",1), (15,59,4,"Mixed","Blocker",2), (2,6,1,"Prototype","NoBlock",2)
        })
        {
            var record = report.Records[index]; var cell = report.GetCell(x,y);
            Assert.AreEqual(new StaticObstructionLocalCell(x,y),record.Destination);
            AssertResult(record.Result,"Supported","Applicable",disposition);
            AssertResult(cell.Result,"Supported","Applicable",disposition);
            Assert.AreEqual(count,cell.Contributors.Count);
            var sources = cell.Contributors.Select(r=>r.FlagsSource.ToString()).Distinct().ToArray();
            var actualSource = sources.Length == 1 ? sources[0] : "Mixed";
            Assert.AreEqual(source,actualSource);
            TestContext.WriteLine($"row index={index} local={record.Destination!.Value.X},{record.Destination!.Value.Y} flags_source={actualSource} disposition={cell.Result.Disposition} contributors={cell.Contributors.Count}");
        }
    }

    private static StaticObstructionSectorReport Inspect(Fixture fixture, StaticObjectLoaderOptions? options = null) =>
        StaticObstructionInspector.InspectSector(fixture.Source, fixture.Prototypes,"SYNTHETIC_MODULE","SYNTHETIC_PROTOTYPES",Selector(fixture),options);
    private static TerrainSectorSelector Selector(Fixture fixture) => new(fixture.Source.ModuleEntryPath,"SYNTHETIC",1452,1290,86570436012,92928,82560,64,64);
    private static long World(int x,int y) => unchecked((long)((ulong)(uint)x | ((ulong)(uint)y << 32)));
    private static void PutPrototype(Fixture fixture,byte[] bytes,string name = "007001 - fixture.pro") => File.WriteAllBytes(Path.Combine(fixture.PrototypeRoot,name),bytes);
    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T failure) { return failure; }
        Assert.Fail($"Expected {typeof(T).Name}.");
        throw new InvalidOperationException("Unreachable after assertion failure.");
    }
    private static void AssertResult(StaticObstructionResult actual,string supportedness,string applicability,string? disposition,string? message = null)
    {
        Assert.AreEqual(supportedness,actual.Supportedness.ToString(),message);
        Assert.AreEqual(applicability,actual.Applicability.ToString(),message);
        Assert.AreEqual(disposition,actual.Disposition?.ToString(),message);
        Assert.IsTrue(actual.Provenance.Count > 0);
        Assert.IsTrue(actual.ReasonCode.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_'));
    }
}

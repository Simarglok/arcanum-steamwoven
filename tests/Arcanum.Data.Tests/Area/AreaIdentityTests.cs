using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Arcanum.Data.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Area;

// Test-local literal FILE/DAT/MES/ART layouts, no production encoders/schema/equality helpers.
[TestClass]
public sealed class AreaIdentityTests
{
    [TestMethod]
    public void Null_occurrences_include_identical_records_same_cell_and_neighbor_sectors()
    {
        var record = Record(Id(0));
        using var fixture = new Fixture([(67108865, [record, record]), (67108866, [record])]);
        var report = fixture.Owner.Inspect();
        Assert.IsTrue(report.IsComplete, string.Join(',', report.Issues));
        var snapshot = fixture.Owner.LoadStrict();
        Assert.AreEqual(3, snapshot.Objects.Count);
        Assert.AreEqual(3, snapshot.Objects.Select(o => o.Key).Distinct().Count());
        Assert.AreEqual(4, snapshot.Visuals.Count); // one shared terrain selection plus three bodies
        CollectionAssert.AreEqual(new long?[] { 64, 64, 128 }, snapshot.Objects.Select(o => o.WorldX).ToArray());
        CollectionAssert.AreEqual(new long[] { 16436, 16515, 16436 }, snapshot.Objects.Select(o => o.Key.Offset).ToArray());
        CollectionAssert.AreEqual(new[] { 79, 79, 79 }, snapshot.Objects.Select(o => o.Key.Length).ToArray());
        Assert.IsTrue(snapshot.Objects.All(o => o.RawSha256 == Hash(record)));
        Assert.IsTrue(snapshot.Objects.All(o => o.Key.ObjectIdSha256 == Hash(Id(0))));
        var keys = snapshot.Objects.Select(o => o.Key).ToArray();
        CollectionAssert.AreEqual(keys, fixture.Owner.LoadStrict().Objects.Select(o => o.Key).ToArray());
        foreach (var visual in fixture.Owner.PublishedSnapshot!.Visuals.Where(v => v.Key.Kind == AreaSourceKind.Static))
        {
            Assert.AreEqual(1, snapshot.Objects.Count(o => o.Key == visual.Key.Object));
            using var lease = fixture.Owner.AcquireSelectedFrame(fixture.Owner.PublishedSnapshot, visual.Key);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 255, 4, 5, 6, 255 }, lease.CopyRgba());
        }
    }

    [TestMethod]
    public void Null_union_and_padding_are_authenticated_without_becoming_entity_identity()
    {
        var id = Id(0); id[2] = 19; id[4] = 27; id[8] = 33;
        using var fixture = new Fixture([(67108865, [Record(Id(0)), Record(id)])]);
        var snapshot = fixture.Owner.LoadStrict();
        Assert.AreEqual(Hash(Id(0)), snapshot.Objects[0].Key.ObjectIdSha256);
        Assert.AreEqual(Hash(id), snapshot.Objects[1].Key.ObjectIdSha256);
        Assert.AreNotEqual(snapshot.Objects[0].RawSha256, snapshot.Objects[1].RawSha256);
    }

    [TestMethod]
    public void Non_null_guid_a_and_positional_semantic_duplicates_ignore_padding()
    {
        foreach (var id in new[] { GuidId(0), Id(1, 41), PositionId(-7, 41, 1) })
        {
            var padded = id.ToArray(); padded[2] = 99; padded[7] = 14;
            foreach (var second in new[] { id, padded })
            {
                using var fixture = new Fixture([(67108865, [Record(id), Record(second)])]);
                Reject(fixture, AreaErrorCode.DuplicateIdentity, AreaStage.Sector, 1);
            }
        }
    }

    [TestMethod]
    public void Different_guid_a_signed_a_positional_components_and_type_tags_are_distinct()
    {
        byte[][] ids = [GuidId(0), GuidId(16), Id(1, 41), Id(1, 42), Id(1, -7),
            PositionId(-7, 41, 1), PositionId(-7, 42, 1), PositionId(-8, 41, 1), PositionId(-7, 41, 2)];
        using var fixture = new Fixture([(67108865, ids.Select(id => Record(id)).ToArray())]);
        Assert.AreEqual(9, fixture.Owner.LoadStrict().Objects.Count);
    }

    [TestMethod]
    public void Non_null_uniqueness_spans_sectors_and_complete_outside_aggregate()
    {
        using (var fixture = new Fixture([(67108865, [Record(GuidId(0))]), (67108866, [Record(GuidId(0))])]))
            Reject(fixture, AreaErrorCode.DuplicateIdentity, AreaStage.Sector, 0);
        using (var fixture = new Fixture([(67108865, [Record(GuidId(0))])], [Record(GuidId(0), 0)]))
            Reject(fixture, AreaErrorCode.DuplicateIdentity, AreaStage.Aggregate, 0);
        using (var fixture = new Fixture([(67108865, [])], [Record(GuidId(0), 0), Record(GuidId(0), 0)]))
            Reject(fixture, AreaErrorCode.DuplicateIdentity, AreaStage.Aggregate, 1);
        using (var fixture = new Fixture([(67108865, [])], [Record(GuidId(0), 274877907008), Record(GuidId(0), 0)]))
            Reject(fixture, AreaErrorCode.DuplicateIdentity, AreaStage.Aggregate, 1);
    }

    [TestMethod]
    public void Null_aggregate_and_editor_are_schema_failures_not_positionally_joined()
    {
        using (var fixture = new Fixture([(67108865, [])], [Record(Id(0))]))
            Reject(fixture, AreaErrorCode.UnsupportedVersionOrSchema, AreaStage.Aggregate, 0);
        using (var fixture = new Fixture([(67108865, [])], [Record(GuidId(0))], [Record(Id(0))]))
            Reject(fixture, AreaErrorCode.UnsupportedVersionOrSchema, AreaStage.Correspondence);
    }

    [TestMethod]
    public void Exact_editor_bijection_never_double_counts_and_full_id_header_mutation_fails()
    {
        byte[][] records = [Record(GuidId(0), 274877907008), Record(GuidId(16), 274877907009)];
        using (var fixture = new Fixture([(67108865, [])], records))
            Assert.AreEqual(2, fixture.Owner.LoadStrict().Objects.Count);
        var mutated = records[0].ToArray(); mutated[30] = 5;
        foreach (var editors in new byte[][][] { [records[0], records[0]], [mutated, records[1]], [records[0]], [.. records, Record(GuidId(32))] })
        {
            using var fixture = new Fixture([(67108865, [])], records, editors);
            Reject(fixture, AreaErrorCode.InputIdentityMismatch, AreaStage.Correspondence);
        }
    }

    [TestMethod]
    public void Prototype_semantic_namespace_is_separate_and_parent_raw_fingerprint_is_retained()
    {
        var parent = Id(1, 7001); parent[3] = 31; parent[16] = 90;
        using (var fixture = new Fixture([(67108865, [Record(Id(1, 7001), parent: parent)])]))
        {
            var obj = fixture.Owner.LoadStrict().Objects.Single();
            Assert.AreEqual(Hash(parent), obj.PrototypeReferenceSha256);
            Assert.AreEqual(Hash(Id(1, 7001)), obj.PrototypeOwnSha256);
        }
        using (var fixture = new Fixture([(67108865, [Record(Id(0), parent: Id(1, 7002))])]))
            Reject(fixture, AreaErrorCode.MissingSource, AreaStage.Prototype, 0);
        using (var fixture = new Fixture([(67108865, [Record(Id(0), parent: GuidId(0))])]))
            Reject(fixture, AreaErrorCode.MissingSource, AreaStage.Prototype, 0);
        using (var fixture = new Fixture([(67108865, [])], extraPrototype: Prototype()))
            Reject(fixture, AreaErrorCode.AmbiguousSource, AreaStage.Prototype);
    }

    [TestMethod]
    public void Duplicate_sector_locator_truncation_trailing_and_invalid_id_are_rejected()
    {
        using (var fixture = new Fixture([(67108865, []), (67108865, [])]))
            Reject(fixture, AreaErrorCode.DuplicateIdentity, AreaStage.Selection);
        foreach (var malformed in new[] { Record(Id(0))[..40], Record(Id(-1)), Record(Id(4)) })
        {
            using var fixture = new Fixture([(67108865, [malformed])]);
            Reject(fixture, AreaErrorCode.MalformedRecord, AreaStage.Sector, 0);
        }
        using (var fixture = new Fixture([(67108865, [[.. Record(Id(0)), 0]])]))
            Reject(fixture, AreaErrorCode.MalformedRecord, AreaStage.Sector, 1);
    }

    [TestMethod]
    public void Source_map_and_order_are_bound_and_cross_owner_snapshot_is_rejected()
    {
        using var first = new Fixture([(67108865, [Record(Id(0)), Record(Id(0), 1)])]);
        using var otherMap = new Fixture([(67108865, [Record(Id(0)), Record(Id(0), 1)])], map: 2);
        using var reordered = new Fixture([(67108865, [Record(Id(0), 1), Record(Id(0))])]);
        var snapshot = first.Owner.LoadStrict();
        Assert.AreNotEqual(snapshot.Objects[0].Key, otherMap.Owner.LoadStrict().Objects[0].Key);
        Assert.AreNotEqual(snapshot.Objects[0].Key.SourceSha256, reordered.Owner.LoadStrict().Objects[0].Key.SourceSha256);
        var error = Throws<AreaException>(() => otherMap.Owner.AcquireSelectedFrame(snapshot, snapshot.Visuals[0].Key));
        Assert.AreEqual(AreaErrorCode.SnapshotMismatch, error.Issue.Code);
    }

    [TestMethod]
    public void Unknown_negative_or_absent_mobile_location_cannot_be_a_successful_empty_core()
    {
        using var empty = new MemoryStream(); I32(empty,119); empty.Write(Id(1,7001)); empty.Write(Id(1,42)); I32(empty,3);
        empty.Write(new byte[] { 0,0 }); I32(empty,0); I32(empty,0); I32(empty,0);
        foreach (var record in new[] { Record(Id(1,42),-1), empty.ToArray() })
        {
            using var fixture = new Fixture([(67108865,[])],[record]);
            Reject(fixture,AreaErrorCode.UnknownLocation,AreaStage.Aggregate,0);
        }
    }

    private static void Reject(Fixture fixture, AreaErrorCode code, AreaStage stage, int? index = null)
    {
        var report = fixture.Owner.Inspect();
        Assert.IsFalse(report.IsComplete);
        Assert.IsNull(fixture.Owner.PublishedSnapshot);
        Assert.IsTrue(report.Issues.Any(i => i.Code == code && i.Stage == stage && (!index.HasValue || i.SourceIndex == index)), string.Join(',', report.Issues));
        Throws<AreaIncompleteException>(() => fixture.Owner.LoadStrict());
    }

    internal static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        Assert.Fail($"Expected {typeof(T).Name}.");
        throw new InvalidOperationException("Unreachable after assertion failure.");
    }

    internal static byte[] Id(short kind, int number = 0)
    {
        var bytes = new byte[24]; BinaryPrimitives.WriteInt16LittleEndian(bytes, kind);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), number); return bytes;
    }
    private static byte[] GuidId(byte first)
    {
        var id = Id(2); for (var i = 0; i < 16; i++) id[8 + i] = (byte)(first + i); return id;
    }
    private static byte[] PositionId(long location, int temporary, int map)
    {
        var id = Id(3); BinaryPrimitives.WriteInt64LittleEndian(id.AsSpan(8), location);
        BinaryPrimitives.WriteInt32LittleEndian(id.AsSpan(16), temporary); BinaryPrimitives.WriteInt32LittleEndian(id.AsSpan(20), map); return id;
    }
    internal static byte[] Record(byte[] id, long location = 0, byte[]? parent = null)
    {
        using var s = new MemoryStream(); I32(s, 119); s.Write(parent ?? Id(1, 7001)); s.Write(id); I32(s, 3);
        s.Write(new byte[] { 1, 0 }); I32(s, 2); I32(s, 0); I32(s, 0); s.WriteByte(1); I64(s, location); return s.ToArray();
    }
    internal static byte[] Prototype()
    {
        using var s = new MemoryStream(); I32(s, 119); s.Write(Id(-1)); s.Write(Id(1, 7001)); I32(s, 3);
        I32(s, 0); I32(s, 0); I32(s, 0);
        int[] optional = [1, 5, 6, 7, 15, 16, 17, 30, 31, 34, 35, 37, 40, 41];
        for (var field = 0; field < 42; field++)
        {
            if (optional.Contains(field)) s.WriteByte(0);
            else I32(s, field == 0 ? 0x40000000 : field == 11 ? 100 : 0);
        }
        return s.ToArray();
    }
    internal static byte[] Sector(params byte[][] records)
    {
        using var s = new MemoryStream(); I32(s, 0); s.Write(new byte[16384]); I32(s, 1); I32(s, 0xAA0003);
        I32(s, 0); s.Write(new byte[12]); I32(s, 2); s.Write(new byte[20]);
        foreach (var record in records) s.Write(record); I32(s, records.Length); return s.ToArray();
    }
    internal static byte[] Art()
    {
        var bytes = new byte[1186]; BinaryPrimitives.WriteInt32LittleEndian(bytes, 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 8); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32), 1);
        bytes[136] = 3; bytes[137] = 2; bytes[138] = 1; bytes[140] = 6; bytes[141] = 5; bytes[142] = 4;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(1156), 2); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(1160), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(1164), 2); bytes[1184] = 1; bytes[1185] = 2; return bytes;
    }
    internal static byte[] Dat(params (string Name, byte[] Payload)[] entries)
    {
        using var s = new MemoryStream(); var offsets = new List<int>();
        foreach (var e in entries) { offsets.Add((int)s.Position); s.Write(e.Payload); }
        var start = s.Position; I32(s, entries.Length);
        for (var i = 0; i < entries.Length; i++)
        {
            var name = Encoding.ASCII.GetBytes(entries[i].Name + "\0"); I32(s, name.Length); s.Write(name);
            I32(s, 0); I32(s, 1); I32(s, entries[i].Payload.Length); I32(s, entries[i].Payload.Length); I32(s, offsets[i]);
        }
        var length = s.Position - start; s.Write(new byte[16]); s.Write("1TAD"u8); I32(s, 0); I32(s, checked((int)length + 28)); return s.ToArray();
    }
    internal static void I32(Stream s, int value) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(b, value); s.Write(b); }
    internal static void I64(Stream s, long value) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteInt64LittleEndian(b, value); s.Write(b); }
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal sealed class Fixture : IDisposable
    {
        private readonly List<Stream> _streams = [];
        internal Fixture((long Sector, byte[][] Records)[] sectors, byte[][]? aggregate = null, byte[][]? editors = null, int map = 1, byte[]? extraPrototype = null, AreaLoaderOptions? limits = null,
            Func<AreaInputKey, byte[], Stream>? streamFactory = null, byte[]? prototype = null, byte[]? moduleBytes = null,
            (string Name, byte[] Payload)[]? resourceEntries = null, byte[]? secondResource = null, AreaMappingRole[]? mappingRoles = null,
            AreaSectorSelection selection = AreaSectorSelection.TownmapComponent, bool allowArtTrailingBytes = false,
            AreaWindow? window = null, (AreaInputKey Key, byte[] Bytes)[]? extraInputs = null)
        {
            var moduleEntries = sectors.Select(s => ($"maps/test/{s.Sector}.sec", Sector(s.Records))).ToList();
            foreach (var (record, index) in (editors ?? aggregate ?? []).Select((r, i) => (r, i))) moduleEntries.Add(($"maps/test/{index}.mob", record));
            byte[] tile = Encoding.ASCII.GetBytes("{0}{stone}{100}{stone}{200}{stone}{300}{stone}{400}{end}");
            byte[] facade = Encoding.ASCII.GetBytes("{0}{fixture}"); byte[] scenery = Encoding.ASCII.GetBytes("{0}{fixture.art}");
            var entries = resourceEntries ?? [("tile.mes", tile), ("facade.mes", facade), ("scenery.mes", scenery), ("art/tile/stonebse0a.art", Art()), ("art/scenery/fixture.art", Art())];
            tile = entries[0].Payload; facade = entries[1].Payload; scenery = entries[2].Payload;
            var resource = Dat(entries);
            var files = new List<(AreaInputKey Key, byte[] Bytes)> { (new(AreaSourceRole.Module), moduleBytes ?? Dat(moduleEntries.ToArray())), (new(AreaSourceRole.Resource1), resource), (new(AreaSourceRole.Prototype, 7001), prototype ?? Prototype()) };
            if (aggregate is not null) files.Add((new(AreaSourceRole.NormalGameMobile), [.. new byte[16], .. aggregate.SelectMany(r => r)]));
            if (extraPrototype is not null) files.Add((new(AreaSourceRole.Prototype, 7002), extraPrototype));
            if (secondResource is not null) files.Add((new(AreaSourceRole.Resource2), secondResource));
            files.AddRange(extraInputs ?? []);
            var inputs = files.Select(f => { var stream = streamFactory?.Invoke(f.Key, f.Bytes) ?? new MemoryStream(f.Bytes); _streams.Add(stream); return new AreaInput(f.Key, stream, leaveOpen: true); }).ToArray();
            using var archive = DatArchive.Open(new MemoryStream(resource));
            var mappings = (mappingRoles ?? [AreaMappingRole.Tile, AreaMappingRole.Facade, AreaMappingRole.Scenery]).Select((role, i) =>
            {
                var e = archive.Entries[i]; var payload = entries[i].Payload;
                return new AreaMappingBinding(role, new(new(AreaSourceRole.Resource1), i, 1, e.StoredSize, e.DeclaredOutputSize, e.Offset, Hash(e.RawNameBytes.ToArray())), Hash(payload));
            });
            Plan = new(1, map, new(1, 1), 2, "maps/test", files.Select(f => new AreaInputIdentity(f.Key, f.Bytes.Length, Hash(f.Bytes))), mappings)
            { Selection = selection, AllowArtTrailingBytes = allowArtTrailingBytes, Window = window };
            Owner = AreaDataOwner.Open(new(inputs), Plan, limits ?? new());
        }
        internal AreaSourcePlan Plan { get; }
        internal AreaDataOwner Owner { get; }
        public void Dispose() { Owner.Dispose(); foreach (var s in _streams) s.Dispose(); }
    }
}

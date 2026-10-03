using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Arcanum.Data;

namespace Arcanum.CrashSiteProbe;

// Independently authored research only. This does not widen a production loader.
internal static partial class Program
{
    private const int AreaMobileBytes = 1_823_280;
    private const string AreaMobileSha = "a548f611ff5de9842bba1f700f6614c0b9651cf3ab640a23d19efc7626dde6a5";
    private sealed record AreaRecord(int Index, int Offset, int Length, string Sha256, InstanceInfo Instance);
    private static string AreaHash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string AreaName(DatEntry entry) => System.Text.Encoding.ASCII.GetString(entry.RawNameBytes.ToArray()[..^1]).Replace('\\', '/');

    private static List<AreaRecord> FrameAggregate(byte[] bytes, int byteCap)
    {
        if (bytes.Length < 16 || bytes.Length > byteCap) throw new InvalidDataException("AGGREGATE_BYTE_LIMIT");
        var records = new List<AreaRecord>();
        var offset = 16;
        var attemptCap = (bytes.Length - 16) / 70;
        while (offset < bytes.Length)
        {
            if (records.Count >= attemptCap) throw new InvalidDataException($"AGGREGATE_SUFFIX offset={offset} prefix={records.Count}");
            try
            {
                var instance = ObjectWire.ParseNext(bytes.AsSpan(offset), out var length) with { SourceIndex = records.Count };
                records.Add(new AreaRecord(records.Count, offset, length, AreaHash(bytes.AsSpan(offset, length)), instance));
                offset = checked(offset + length);
            }
            catch (InvalidDataException error)
            {
                throw new InvalidDataException($"AGGREGATE_SUFFIX offset={offset} prefix={records.Count} reason={error.Message}");
            }
        }
        return records;
    }

    private static string AreaBin(long? location)
    {
        if (location is null) return "unknown";
        var x = unchecked((int)location.Value); var y = unchecked((int)(location.Value >> 32));
        if (x < 0 || y < 0) return "unknown";
        if (x >= 92864 && x < 93056 && y >= 82496 && y < 82688) return "core";
        if (x >= 92800 && x < 93120 && y >= 82432 && y < 82752) return "context";
        return "outside";
    }

    private static int ProbeArea(string[] args)
    {
        if (args.Length != 8 || args[0] != "--module" || args[2] != "--prototype-root" || args[4] != "--resource" || args[6] != "--mobile")
            throw new ArgumentException("usage: --area-inventory --module PATH --prototype-root PATH --resource PATH --mobile PATH");
        var moduleBefore = InputFingerprint.Create(args[1]);
        var resourceBefore = InputFingerprint.Create(args[5]);
        var mobileBefore = InputFingerprint.Create(args[7]);
        if (moduleBefore != new InputFingerprint(ExpectedModuleBytes, ExpectedModuleSha256) || resourceBefore != new InputFingerprint(ExpectedResourceBytes, ExpectedResourceSha256) || mobileBefore != new InputFingerprint(AreaMobileBytes, AreaMobileSha))
            throw new InvalidDataException("AREA_INPUT_IDENTITY");
        using var module = DatArchive.Open(args[1], BoundedOptions());
        using var resource = DatArchive.Open(args[5], BoundedOptions());
        var mobile = FrameAggregate(File.ReadAllBytes(args[7]), AreaMobileBytes);
        var loose = new List<object>(); var expected = new Dictionary<string, int>(); var semanticExpected = new Dictionary<string, int>();
        foreach (var entry in module.Entries.Where(e => AreaName(e).StartsWith("maps/Arcanum1-024-fixed/", StringComparison.Ordinal) && AreaName(e).EndsWith(".mob", StringComparison.Ordinal)).OrderBy(e => e.Ordinal))
        {
            var bytes = module.ReadEntry(entry);
            var instance = ObjectWire.ParseNext(bytes, out var consumed);
            if (consumed != bytes.Length) throw new InvalidDataException("LOOSE_TRAILING_BYTES");
            var key = $"{bytes.Length}:{AreaHash(bytes)}";
            expected[key] = expected.GetValueOrDefault(key) + 1;
            var semantic = SemanticHash(bytes, instance); semanticExpected[semantic] = semanticExpected.GetValueOrDefault(semantic) + 1;
            loose.Add(new { ordinal = entry.Ordinal, length = bytes.Length, sha256 = AreaHash(bytes), semantic_sha256 = semantic, arrays = instance.Arrays });
        }
        var actual = mobile.GroupBy(r => $"{r.Length}:{r.Sha256}").ToDictionary(g => g.Key, g => g.Count());
        var mismatch = expected.Keys.Union(actual.Keys).Where(k => expected.GetValueOrDefault(k) != actual.GetValueOrDefault(k)).Order().Select(k => new { key = k, loose = expected.GetValueOrDefault(k), aggregate = actual.GetValueOrDefault(k) }).ToArray();
        var mobileBytes = File.ReadAllBytes(args[7]);
        var semanticActual = mobile.Select(r => SemanticHash(mobileBytes.AsSpan(r.Offset, r.Length).ToArray(), r.Instance)).GroupBy(h => h).ToDictionary(g => g.Key, g => g.Count());
        var semanticMismatch = semanticExpected.Keys.Union(semanticActual.Keys).Count(k => semanticExpected.GetValueOrDefault(k) != semanticActual.GetValueOrDefault(k));
        if (semanticMismatch != 0) throw new InvalidDataException($"SEMANTIC_BIJECTION_FAILED keys={semanticMismatch}");
        var selected = mobile.Where(r => AreaBin(r.Instance.Location) is "core" or "context").ToArray();
        var prototypes = ObjectWire.ReadReferencedPrototypesFromDirectory(args[3], selected.Select(r => r.Instance.PrototypeId).ToArray());
        var effective = ObjectWire.ResolveEffectiveInstances(selected.Select(r => r.Instance).ToArray(), prototypes.Prototypes);
        var visuals = effective.Select(v =>
        {
            var prototype = prototypes.Prototypes.Single(p => p.Id.Matches(v.Instance.PrototypeId));
            var record = selected.Single(r => r.Index == v.SourceIndex);
            return new { index = v.SourceIndex, current_aid = v.CurrentAid, v.CurrentAidInherited, v.EffectiveFlags, effective_numeric = EffectiveNumeric(v.Instance, prototype), scripts_inherited = !v.Instance.ScriptsOverridden, scripts = EffectiveScripts(mobileBytes.AsSpan(record.Offset, record.Length).ToArray(), v.Instance, prototype), visual = AreaBin(v.Instance.Location) == "core" ? VisualChain(resource, v, prototype, unchecked((int)v.Instance.Location!.Value), unchecked((int)(v.Instance.Location.Value >> 32))) : (object)new { status = "context_not_resolved" } };
        }).ToArray();
        var sectors = AreaSectors(module, resource, args[3]);
        var report = new
        {
            schema_version = 1, module = moduleBefore, resource = resourceBefore, mobile = mobileBefore,
            exact_eof_records = mobile.Count, loose_count = loose.Count, mismatch, semantic_mismatch = semanticMismatch, visuals, sectors, cave = AreaCave(module, resource, args[3]),
            prototypes = prototypes.Inputs.Select(p => p.Fingerprint).ToArray(),
            records = mobile.Select(r => new { r.Index, r.Offset, r.Length, r.Sha256, type = r.Instance.Type, prototype = r.Instance.PrototypeId.AidValue(), location = r.Instance.Location, bin = AreaBin(r.Instance.Location), aid = r.Instance.DirectCurrentAid, flags = r.Instance.DirectFlags, numeric_fields = r.Instance.NumericFields, arrays = r.Instance.Arrays, scripts = ScriptAttachments(mobileBytes.AsSpan(r.Offset, r.Length).ToArray(), r.Instance), semantic_sha256 = SemanticHash(mobileBytes.AsSpan(r.Offset, r.Length).ToArray(), r.Instance), own_id_fingerprint = AreaHash(r.Instance.OwnId.Payload) }).ToArray(), loose
        };
        if (moduleBefore != InputFingerprint.Create(args[1]) || resourceBefore != InputFingerprint.Create(args[5]) || mobileBefore != InputFingerprint.Create(args[7])) throw new InvalidDataException("AREA_INPUT_CHANGED");
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static void AreaSelfTest()
    {
        AreaContentOracles();
        // Literal raw layout, not ObjectWire's encoder/schema: two distinct scenery envelopes.
        static byte[] Record(int id, int x, int y)
        {
            var bytes = new byte[79];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, 119);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(4), 1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 7001);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28), 1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(36), id);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), 3);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(56), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(58), 2);
            bytes[70] = 1;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(71), x);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(75), y);
            return bytes;
        }
        var first = Record(41, 92864, 82496); var second = Record(42, 93056, 82688);
        byte[] aggregate = [..new byte[16], ..first, ..second];
        var framed = FrameAggregate(aggregate, 174);
        if (framed.Count != 2 || framed[0].Length != 79 || framed[1].Offset != 95 || framed[0].Instance.OwnId.AidValue() != 41 || framed[1].Instance.OwnId.AidValue() != 42 || AreaBin(framed[0].Instance.Location) != "core" || AreaBin(framed[1].Instance.Location) != "context") throw new InvalidOperationException("AREA_LITERAL_ORACLE");
        if (FrameAggregate(new byte[16], 16).Count != 0 || AreaBin(null) != "unknown" || AreaBin(-1) != "unknown") throw new InvalidOperationException("AREA_EMPTY_UNKNOWN");
        for (var length = 0; length < first.Length; length++)
        {
            var truncated = aggregate[..(16 + length)];
            if (length != 0) AssertRejected(() => FrameAggregate(truncated, 174), "aggregate truncation");
        }
        AssertRejected(() => FrameAggregate(aggregate, 173), "stream cap+1");
        AssertRejected(() => FrameAggregate([..aggregate, 1], 175), "trailing partial record");
        var bad = aggregate.ToArray(); bad[16] = 118; AssertRejected(() => FrameAggregate(bad, 174), "unsupported version");
        Console.WriteLine("area-self-test: PASS (literal EOF, identity, location and truncation oracles)");
    }
}

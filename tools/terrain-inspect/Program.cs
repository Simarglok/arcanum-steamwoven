using System.Security.Cryptography;
using System.Text.Json;
using Arcanum.Data;
using Arcanum.Data.Terrain;

namespace Arcanum.Tools.TerrainInspect;

internal static class Program
{
    private const string ModuleEntry = "maps\\Arcanum1-024-fixed\\86570436012.sec";
    private const string TileMes = "art\\tile\\tilename.mes";
    private const string FacadeMes = "art\\facade\\facadename.mes";

    private static int Main(string[] args)
    {
        if (args.SequenceEqual(["--help"])) { Console.WriteLine("usage: terrain-inspect --module PATH --resource PATH --source-id LABEL [--expect-module-sha256 SHA256] [--expect-resource-sha256 SHA256]"); return 0; }
        try
        {
            var values = Parse(args);
            var moduleFingerprint = Fingerprint(values.Module!); var resourceFingerprint = Fingerprint(values.Resource!);
            VerifyExpected(moduleFingerprint.Sha256, values.ModuleHash, "module"); VerifyExpected(resourceFingerprint.Sha256, values.ResourceHash, "resource");
            using var module = DatArchive.Open(values.Module!, BoundedDatOptions());
            using var resource = DatArchive.Open(values.Resource!, BoundedDatOptions());
            var selector = new TerrainSectorSelector(ModuleEntry, "Arcanum1-024-fixed", 1452, 1290, 86570436012, 92928, 82560, 64, 64, new TerrainEntryExpectation(463, 2, 47291));
            var source = new TerrainResourceSource(resource, values.SourceId!, TileMes, FacadeMes, TerrainPathProfile.CeNonWindowsMacOs);
            var report = TerrainLoader.Inspect(module, selector, source);
            var moduleAfter = Fingerprint(values.Module!); var resourceAfter = Fingerprint(values.Resource!);
            if (moduleFingerprint != moduleAfter || resourceFingerprint != resourceAfter) throw new IOException("An input changed while it was inspected.");
            var aggregate = new
            {
                schemaVersion = 1,
                complete = report.IsComplete,
                selector = new { map = selector.MapLabel, selector.SectorX, selector.SectorY, selector.PackedSectorId, selector.OriginX, selector.OriginY, selector.Width, selector.Height },
                framing = report.Framing.Ranges.ToDictionary(pair => pair.Key.ToString(), pair => new { pair.Value.Offset, pair.Value.Length }),
                sourceId = source.SourceId,
                input = new { moduleSha256 = moduleFingerprint.Sha256, resourceSha256 = resourceFingerprint.Sha256 },
                counts = new { cells = report.Outcomes.Count, resolved = report.ResolvedRecords.Count, issues = report.Issues.Count, tile = report.ResolvedRecords.Count(r => r.Kind == TerrainKind.Tile), facade = report.ResolvedRecords.Count(r => r.Kind == TerrainKind.Facade) },
                issues = report.Issues.GroupBy(issue => issue.Code).OrderBy(group => group.Key).ToDictionary(group => group.Key.ToString(), group => group.Count()),
                frameValidation = new { selected = report.ResolvedRecords.Count },
                normalization = new { tileLookupIds = report.ResolvedRecords.Where(r => r.Kind == TerrainKind.Tile).Select(r => r.LookupId).Distinct().Count(), recoveredCells = report.ResolvedRecords.Count(r => r.Kind == TerrainKind.Tile && r.RawId != r.LookupId) }
            };
            Console.WriteLine(JsonSerializer.Serialize(aggregate)); return report.IsComplete ? 0 : 3;
        }
        catch (ArgumentException exception) { Console.Error.WriteLine($"terrain-inspect: usage: {exception.Message}"); return 2; }
        catch (Exception exception) { Console.Error.WriteLine(exception is TerrainException terrain ? $"terrain-inspect: {exception.GetType().Name}: {terrain.Code}" : $"terrain-inspect: {exception.GetType().Name}"); return 1; }
    }

    private static Arguments Parse(string[] args)
    {
        var result = new Arguments();
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length) throw new ArgumentException("each option requires a value");
            switch (args[index])
            {
                case "--module": result.Module = args[index + 1]; break;
                case "--resource": result.Resource = args[index + 1]; break;
                case "--source-id": result.SourceId = args[index + 1]; break;
                case "--expect-module-sha256": result.ModuleHash = args[index + 1]; break;
                case "--expect-resource-sha256": result.ResourceHash = args[index + 1]; break;
                default: throw new ArgumentException("unknown option");
            }
        }
        if (string.IsNullOrWhiteSpace(result.Module) || string.IsNullOrWhiteSpace(result.Resource) || string.IsNullOrWhiteSpace(result.SourceId)) throw new ArgumentException("--module, --resource and --source-id are required");
        return result;
    }
    private static void VerifyExpected(string actual, string? expected, string label) { if (expected is not null && !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) throw new TerrainFormatException(TerrainErrorCode.EntryIdentityMismatch, $"Expected {label} fingerprint did not match."); }
    private static DatReaderOptions BoundedDatOptions() => new() { MaxArchiveBytes = uint.MaxValue, MaxStoredEntryBytes = 1024 * 1024, MaxDecodedEntryBytes = 1024 * 1024 };
    private static InputFingerprint Fingerprint(string path) { using var stream = File.OpenRead(path); return new InputFingerprint(stream.Length, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()); }
    private sealed record InputFingerprint(long Length, string Sha256);
    private sealed class Arguments { public string? Module { get; set; } public string? Resource { get; set; } public string? SourceId { get; set; } public string? ModuleHash { get; set; } public string? ResourceHash { get; set; } }
}

using System.Security.Cryptography;
using System.Text.Json;
using Arcanum.Data;
using Arcanum.Data.Terrain;

namespace Arcanum.Tools.TerrainInspect;

internal static class Program
{
    internal const string ModuleEntry = "maps\\Arcanum1-024-fixed\\86570436012.sec";
    internal const string TileMes = "art\\tile\\tilename.mes";
    internal const string FacadeMes = "art\\facade\\facadename.mes";
    internal const string CrashSiteModuleSha256 = "8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f";
    internal const string CrashSiteResourceSha256 = "4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d";

    private static int Main(string[] args) => Run(args, Console.Out, Console.Error, new ProductionRuntime());

    internal static int Run(string[] args, TextWriter stdout, TextWriter stderr, ITerrainInspectRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        ArgumentNullException.ThrowIfNull(runtime);
        if (args.SequenceEqual(["--help"]))
        {
            stdout.WriteLine("usage: terrain-inspect --module PATH --resource PATH --source-id LABEL [--expect-module-sha256 SHA256] [--expect-resource-sha256 SHA256]");
            return 0;
        }
        try
        {
            var values = Parse(args);
            var execution = runtime.Inspect(values);
            stdout.WriteLine(JsonSerializer.Serialize(Aggregate(execution)));
            return execution.Report.IsComplete ? 0 : 3;
        }
        catch (ArgumentException exception)
        {
            stderr.WriteLine($"terrain-inspect: usage: {exception.Message}");
            return 2;
        }
        catch (Exception exception)
        {
            stderr.WriteLine(exception is TerrainException terrain
                ? $"terrain-inspect: {exception.GetType().Name}: {terrain.Code}"
                : $"terrain-inspect: {exception.GetType().Name}");
            return 1;
        }
    }

    private static object Aggregate(TerrainInspectExecution execution)
    {
        var report = execution.Report;
        var selector = execution.Selector;
        return new
        {
            schemaVersion = 1,
            complete = report.IsComplete,
            selector = new { map = selector.MapLabel, selector.SectorX, selector.SectorY, selector.PackedSectorId, selector.OriginX, selector.OriginY, selector.Width, selector.Height },
            framing = report.Framing.Ranges.OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key.ToString(), pair => new { pair.Value.Offset, pair.Value.Length }),
            sourceId = execution.SourceId,
            input = new { moduleSha256 = execution.Module.Sha256, resourceSha256 = execution.Resource.Sha256 },
            counts = new { cells = report.Outcomes.Count, resolved = report.ResolvedRecords.Count, issues = report.Issues.Count, tile = report.ResolvedRecords.Count(r => r.Kind == TerrainKind.Tile), facade = report.ResolvedRecords.Count(r => r.Kind == TerrainKind.Facade) },
            issues = report.Issues.GroupBy(issue => issue.Code).OrderBy(group => group.Key).ToDictionary(group => group.Key.ToString(), group => group.Count()),
            frameValidation = new { selected = report.ResolvedRecords.Count },
            normalization = new
            {
                recoveredCells = report.ResolvedRecords.Count(record => record.WasNormalizationRecovered),
                recoveredLookupIds = report.ResolvedRecords.Where(record => record.WasNormalizationRecovered).Select(record => record.LookupId).Distinct().Count()
            }
        };
    }

    internal static Arguments Parse(string[] args)
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
        if (string.IsNullOrWhiteSpace(result.Module) || string.IsNullOrWhiteSpace(result.Resource) || string.IsNullOrWhiteSpace(result.SourceId))
            throw new ArgumentException("--module, --resource and --source-id are required");
        if (result.SourceId.Contains('/') || result.SourceId.Contains('\\'))
            throw new ArgumentException("--source-id must be a non-path label");
        return result;
    }

    internal static void VerifyExpected(string actual, string? expected, string label)
    {
        if (expected is not null && !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new TerrainFormatException(TerrainErrorCode.EntryIdentityMismatch, $"Expected {label} fingerprint did not match.");
    }

    internal static DatReaderOptions BoundedDatOptions() => new() { MaxArchiveBytes = uint.MaxValue, MaxStoredEntryBytes = 1024 * 1024, MaxDecodedEntryBytes = 1024 * 1024 };
    internal static InputFingerprint Fingerprint(string path) { using var stream = File.OpenRead(path); return new InputFingerprint(stream.Length, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()); }
}

internal interface ITerrainInspectRuntime
{
    TerrainInspectExecution Inspect(Arguments arguments);
}

internal sealed class ProductionRuntime : ITerrainInspectRuntime
{
    public TerrainInspectExecution Inspect(Arguments arguments)
    {
        var moduleFingerprint = Program.Fingerprint(arguments.Module!);
        var resourceFingerprint = Program.Fingerprint(arguments.Resource!);
        Program.VerifyExpected(moduleFingerprint.Sha256, Program.CrashSiteModuleSha256, "module");
        Program.VerifyExpected(resourceFingerprint.Sha256, Program.CrashSiteResourceSha256, "resource");
        Program.VerifyExpected(moduleFingerprint.Sha256, arguments.ModuleHash, "module");
        Program.VerifyExpected(resourceFingerprint.Sha256, arguments.ResourceHash, "resource");
        using var module = DatArchive.Open(arguments.Module!, Program.BoundedDatOptions());
        using var resource = DatArchive.Open(arguments.Resource!, Program.BoundedDatOptions());
        var selector = new TerrainSectorSelector(Program.ModuleEntry, "Arcanum1-024-fixed", 1452, 1290, 86570436012, 92928, 82560, 64, 64, new TerrainEntryExpectation(463, 2, 47291));
        var source = new TerrainResourceSource(resource, arguments.SourceId!, Program.TileMes, Program.FacadeMes, TerrainPathProfile.NonWindowsMacOs);
        var report = TerrainLoader.Inspect(module, selector, source);
        var moduleAfter = Program.Fingerprint(arguments.Module!);
        var resourceAfter = Program.Fingerprint(arguments.Resource!);
        if (moduleFingerprint != moduleAfter || resourceFingerprint != resourceAfter) throw new IOException("An input changed while it was inspected.");
        return new TerrainInspectExecution(report, selector, source.SourceId, moduleFingerprint, resourceFingerprint);
    }
}

internal sealed record InputFingerprint(long Length, string Sha256);
internal sealed record TerrainInspectExecution(TerrainLoadReport Report, TerrainSectorSelector Selector, string SourceId, InputFingerprint Module, InputFingerprint Resource);
internal sealed class Arguments
{
    public string? Module { get; set; }
    public string? Resource { get; set; }
    public string? SourceId { get; set; }
    public string? ModuleHash { get; set; }
    public string? ResourceHash { get; set; }
}

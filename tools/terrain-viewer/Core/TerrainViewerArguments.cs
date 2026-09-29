namespace Arcanum.TerrainViewer.Core;

public enum TerrainViewerMode { TerrainOnly, CombinedStaticDiagnostic }

public sealed record TerrainViewerArguments(TerrainViewerMode Mode, string ModulePath, string ResourcePath, string SourceId,
    string? PrototypePath, string? ExpectedModuleSha256, string? ExpectedResourceSha256)
{
    public static TerrainViewerArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? module = null, resource = null, sourceId = null, prototypes = null, moduleHash = null, resourceHash = null;
        var mode = TerrainViewerMode.TerrainOnly;
        var modeSpecified = false;
        for (var index = 0; index < args.Count; index += 2)
        {
            if (index + 1 >= args.Count) throw new ArgumentException("Usage");
            var value = args[index + 1];
            switch (args[index])
            {
                case "--mode":
                    if (modeSpecified) throw new ArgumentException("Usage");
                    modeSpecified = true;
                    mode = value switch { "terrain-only" => TerrainViewerMode.TerrainOnly, "combined" => TerrainViewerMode.CombinedStaticDiagnostic, _ => throw new ArgumentException("Usage") };
                    break;
                case "--module": module = Unique(module, value); break;
                case "--resource": resource = Unique(resource, value); break;
                case "--source-id": sourceId = Unique(sourceId, value); break;
                case "--prototypes": prototypes = Unique(prototypes, value); break;
                case "--expect-module-sha256": moduleHash = Unique(moduleHash, ValidateHash(value)); break;
                case "--expect-resource-sha256": resourceHash = Unique(resourceHash, ValidateHash(value)); break;
                default: throw new ArgumentException("Usage");
            }
        }
        if (string.IsNullOrWhiteSpace(module) || string.IsNullOrWhiteSpace(resource) || string.IsNullOrWhiteSpace(sourceId)
            || sourceId.Contains('/') || sourceId.Contains('\\') || sourceId.Any(char.IsControl)) throw new ArgumentException("Usage");
        if ((mode == TerrainViewerMode.TerrainOnly && prototypes is not null) || (mode == TerrainViewerMode.CombinedStaticDiagnostic && string.IsNullOrWhiteSpace(prototypes)))
            throw new ArgumentException("Usage");
        return new TerrainViewerArguments(mode, module, resource, sourceId, prototypes, moduleHash, resourceHash);
    }

    private static string ValidateHash(string value) => value.Length == 64 && value.All(Uri.IsHexDigit)
        ? value.ToLowerInvariant() : throw new ArgumentException("Usage");
    private static string Unique(string? existing, string value) => existing is null ? value : throw new ArgumentException("Usage");
}

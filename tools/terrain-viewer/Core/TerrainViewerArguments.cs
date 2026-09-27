namespace Arcanum.TerrainViewer.Core;

public sealed record TerrainViewerArguments(string ModulePath, string ResourcePath, string SourceId, string? ExpectedModuleSha256, string? ExpectedResourceSha256)
{
    public static TerrainViewerArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? module = null, resource = null, sourceId = null, moduleHash = null, resourceHash = null;
        for (var index = 0; index < args.Count; index += 2)
        {
            if (index + 1 >= args.Count) throw new ArgumentException("Usage");
            var value = args[index + 1];
            switch (args[index])
            {
                case "--module": module = value; break;
                case "--resource": resource = value; break;
                case "--source-id": sourceId = value; break;
                case "--expect-module-sha256": moduleHash = ValidateHash(value); break;
                case "--expect-resource-sha256": resourceHash = ValidateHash(value); break;
                default: throw new ArgumentException("Usage");
            }
        }
        if (string.IsNullOrWhiteSpace(module) || string.IsNullOrWhiteSpace(resource) || string.IsNullOrWhiteSpace(sourceId)
            || sourceId.Contains('/') || sourceId.Contains('\\') || sourceId.Any(char.IsControl)) throw new ArgumentException("Usage");
        return new TerrainViewerArguments(module, resource, sourceId, moduleHash, resourceHash);
    }

    private static string ValidateHash(string value) => value.Length == 64 && value.All(Uri.IsHexDigit)
        ? value.ToLowerInvariant() : throw new ArgumentException("Usage");
}

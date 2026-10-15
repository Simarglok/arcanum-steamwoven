namespace Arcanum.TerrainViewer.Core;

public enum TerrainViewerMode { TerrainOnly, CombinedStaticDiagnostic, ActorDiagnostic, WorldDiagnostic, CrashSiteArea }

public sealed record TerrainViewerArguments(TerrainViewerMode Mode, string ModulePath, string ResourcePath, string SourceId,
    string? PrototypePath, string? ExpectedModuleSha256, string? ExpectedResourceSha256, string? ActorResourcePath,
    string? ExpectedActorResourceSha256, ActorDiagnosticPlacementInput? ActorInput)
{
    public string? AreaMobilePath { get; init; }
    public static TerrainViewerArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? module = null, resource = null, sourceId = null, prototypes = null, moduleHash = null, resourceHash = null,
            actorResource = null, actorResourceHash = null, areaMobile = null;
        long? actorMapX = null, actorMapY = null, actorOriginX = null, actorOriginY = null;
        int? actorOffsetX = null, actorOffsetY = null, actorScale = null, actorFacing = null, actorFrame = null;
        ActorDiagnosticRenderBranch? actorBranch = null;
        ActorDiagnosticAction? actorAction = null;
        bool? actorShrunk = null;
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
                    mode = value switch { "terrain-only" => TerrainViewerMode.TerrainOnly, "combined" => TerrainViewerMode.CombinedStaticDiagnostic, "actor-diagnostic" => TerrainViewerMode.ActorDiagnostic, "world-diagnostic" => TerrainViewerMode.WorldDiagnostic, "crash-site-area" => TerrainViewerMode.CrashSiteArea, _ => throw new ArgumentException("Usage") };
                    break;
                case "--module": module = Unique(module, value); break;
                case "--resource": resource = Unique(resource, value); break;
                case "--source-id": sourceId = Unique(sourceId, value); break;
                case "--prototypes": prototypes = Unique(prototypes, value); break;
                case "--expect-module-sha256": moduleHash = Unique(moduleHash, ValidateHash(value)); break;
                case "--expect-resource-sha256": resourceHash = Unique(resourceHash, ValidateHash(value)); break;
                case "--actor-resource": actorResource = Unique(actorResource, value); break;
                case "--area-mobile": areaMobile = Unique(areaMobile, value); break;
                case "--expect-actor-resource-sha256": actorResourceHash = Unique(actorResourceHash, ValidateHash(value)); break;
                case "--actor-map-x": actorMapX = Unique(actorMapX, ParseInt64(value)); break;
                case "--actor-map-y": actorMapY = Unique(actorMapY, ParseInt64(value)); break;
                case "--actor-camera-origin-x": actorOriginX = Unique(actorOriginX, ParseInt64(value)); break;
                case "--actor-camera-origin-y": actorOriginY = Unique(actorOriginY, ParseInt64(value)); break;
                case "--actor-offset-x": actorOffsetX = Unique(actorOffsetX, ParseInt32(value)); break;
                case "--actor-offset-y": actorOffsetY = Unique(actorOffsetY, ParseInt32(value)); break;
                case "--actor-render-branch": actorBranch = Unique(actorBranch, value == "normal-uncached" ? ActorDiagnosticRenderBranch.NormalUncached : throw new ArgumentException("Usage")); break;
                case "--actor-scale-percent": actorScale = Unique(actorScale, ParseInt32(value)); break;
                case "--actor-shrunk": actorShrunk = Unique(actorShrunk, value == "false" ? false : value == "true" ? true : throw new ArgumentException("Usage")); break;
                case "--actor-action": actorAction = Unique(actorAction, value == "idle" ? ActorDiagnosticAction.Idle : value == "walk" ? ActorDiagnosticAction.Walk : throw new ArgumentException("Usage")); break;
                case "--actor-facing": actorFacing = Unique(actorFacing, ParseInt32(value)); break;
                case "--actor-frame": actorFrame = Unique(actorFrame, ParseInt32(value)); break;
                default: throw new ArgumentException("Usage");
            }
        }
        if (string.IsNullOrWhiteSpace(module) || string.IsNullOrWhiteSpace(resource) || string.IsNullOrWhiteSpace(sourceId)
            || sourceId.Contains('/') || sourceId.Contains('\\') || sourceId.Any(char.IsControl)) throw new ArgumentException("Usage");
        var actorSpecified = actorMapX is not null || actorMapY is not null || actorOriginX is not null || actorOriginY is not null || actorOffsetX is not null || actorOffsetY is not null || actorBranch is not null || actorScale is not null || actorShrunk is not null || actorAction is not null || actorFacing is not null || actorFrame is not null;
        var actorArchiveSpecified = actorResource is not null || actorResourceHash is not null;
        if ((mode == TerrainViewerMode.TerrainOnly && (prototypes is not null || actorSpecified || actorArchiveSpecified))
            || (mode == TerrainViewerMode.CombinedStaticDiagnostic && (string.IsNullOrWhiteSpace(prototypes) || actorSpecified || actorArchiveSpecified))
            || (mode == TerrainViewerMode.ActorDiagnostic && (string.IsNullOrWhiteSpace(prototypes) || string.IsNullOrWhiteSpace(actorResource) || !actorSpecified))
            || (mode == TerrainViewerMode.WorldDiagnostic && (string.IsNullOrWhiteSpace(prototypes) || string.IsNullOrWhiteSpace(actorResource) || actorSpecified))
            || (mode == TerrainViewerMode.CrashSiteArea && (string.IsNullOrWhiteSpace(prototypes) || string.IsNullOrWhiteSpace(actorResource) || string.IsNullOrWhiteSpace(areaMobile) || actorSpecified))
            || (mode != TerrainViewerMode.CrashSiteArea && areaMobile is not null))
            throw new ArgumentException("Usage");
        ActorDiagnosticPlacementInput? actor = null;
        if (mode == TerrainViewerMode.ActorDiagnostic)
        {
            if (actorMapX is null || actorMapY is null || actorOriginX is null || actorOriginY is null || actorOffsetX is null || actorOffsetY is null || actorBranch is null || actorScale is null || actorShrunk is null || actorAction is null || actorFacing is null || actorFrame is null || actorFacing is < 0 or > 7 || actorFrame < 0)
                throw new ArgumentException("Usage");
            actor = new ActorDiagnosticPlacementInput(actorMapX, actorMapY, actorOriginX, actorOriginY, actorOffsetX, actorOffsetY,
                actorBranch.Value, actorScale.Value, actorShrunk, actorAction.Value, actorFacing.Value, actorFrame.Value, 0, 0, 0, 0, 0, 0);
        }
        if (mode is TerrainViewerMode.WorldDiagnostic or TerrainViewerMode.CrashSiteArea)
            actor = new(6,1,0,0,0,0,ActorDiagnosticRenderBranch.NormalUncached,100,false,
                ActorDiagnosticAction.Idle,5,0,0,0,0,0,0,0);
        return new TerrainViewerArguments(mode, module, resource, sourceId, prototypes, moduleHash, resourceHash, actorResource, actorResourceHash, actor) { AreaMobilePath = areaMobile };
    }

    private static string ValidateHash(string value) => value.Length == 64 && value.All(Uri.IsHexDigit)
        ? value.ToLowerInvariant() : throw new ArgumentException("Usage");
    private static string Unique(string? existing, string value) => existing is null ? value : throw new ArgumentException("Usage");
    private static int? Unique(int? existing, int value) => existing is null ? value : throw new ArgumentException("Usage");
    private static long? Unique(long? existing, long value) => existing is null ? value : throw new ArgumentException("Usage");
    private static bool? Unique(bool? existing, bool value) => existing is null ? value : throw new ArgumentException("Usage");
    private static ActorDiagnosticRenderBranch? Unique(ActorDiagnosticRenderBranch? existing, ActorDiagnosticRenderBranch value) => existing is null ? value : throw new ArgumentException("Usage");
    private static ActorDiagnosticAction? Unique(ActorDiagnosticAction? existing, ActorDiagnosticAction value) => existing is null ? value : throw new ArgumentException("Usage");
    private static int ParseInt32(string value) => int.TryParse(value, out var result) ? result : throw new ArgumentException("Usage");
    private static long ParseInt64(string value) => long.TryParse(value, out var result) ? result : throw new ArgumentException("Usage");
}

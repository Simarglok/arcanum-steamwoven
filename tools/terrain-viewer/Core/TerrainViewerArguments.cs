namespace Arcanum.TerrainViewer.Core;

public enum TerrainViewerMode { TerrainOnly, CombinedStaticDiagnostic, ActorDiagnostic, WorldDiagnostic, CrashSiteArea }

public sealed record TerrainViewerArguments(TerrainViewerMode Mode, string ModulePath, string ResourcePath, string SourceId,
    string? PrototypePath, string? ExpectedModuleSha256, string? ExpectedResourceSha256, string? ActorResourcePath,
    string? ExpectedActorResourceSha256, ActorDiagnosticPlacementInput? ActorInput)
{
    public string? AreaMobilePath { get; init; }
    /// <summary>Archive holding rules/effect.mes for whole-core critter life (original: resource3).</summary>
    public string? AreaEffectsPath { get; init; }
    /// <summary>Module patch archive holding rules/map.patch (original: modules/Arcanum.PATCH0).</summary>
    public string? AreaPatchPath { get; init; }
    /// <summary>Optional: the Crash Site cave's game-generated object file (ARC-WORLD-009). Absent, the cave and
    /// its transition are unavailable and the area runs as before.</summary>
    public string? AreaCaveMobilePath { get; init; }
    /// <summary>ARC-RENDER-017: the game folder whose module content stack holds <see cref="AreaMapName"/>.</summary>
    public string? GameRoot { get; init; }
    /// <summary>ARC-RENDER-017: load this interior (a <c>maps/</c> folder name) under a whole-map plan
    /// instead of the pinned Crash Site exterior; prototypes come from the content stack, and the area mobile,
    /// effects, patch, cave and prototype folder arguments are refused.</summary>
    public string? AreaMapName { get; init; }
    /// <summary>ARC-WORLD-031: the tile a map transfer arrives on (never a command-line option). The actor starts there when
    /// the map's step evidence admits it, otherwise where a map-list arrival would.</summary>
    public Arcanum.Data.Maps.WorldLocation? AreaMapArrival { get; init; }
    /// <summary>ARC-RENDER-024: <c>--area-map-sector X,Y</c>, the sector the map opens at. A map too large to load whole (the
    /// main world map) opens as a window around it, the actor near its centre; a whole map stands the actor near it. Absent,
    /// a large map opens around the transfer arrival or the map-list start tile.</summary>
    public Arcanum.Data.Area.AreaSectorCoordinate? AreaMapSector { get; init; }
    /// <summary>ARC-DATA-037: the data-fix rules a map opens under (the session's; the map patch's MP7 switch); null is the
    /// default mode, Steamwoven. Not a command-line argument.</summary>
    public Arcanum.Data.Fixes.DataFixProfile? DataRules { get; init; }
    /// <summary>ARC-WORLD-027: start the AREA session as this original pre-made hero (<see cref="AreaStartingHero.Premade"/>);
    /// absent, the session has no hero and dialogs use the diagnostic PC profile.</summary>
    public AreaPremadeHero? Hero { get; init; }
    /// <summary>ARC-UI-011: <c>--main-menu true</c> opens the main menu over the AREA scene once it is ready (new game, last save,
    /// load); refused with <see cref="Hero"/>, as the menu picks the hero.</summary>
    public bool MainMenu { get; init; }
    /// <summary>Longest <c>--area-map</c> folder name (the launcher's <c>MAX_MAP_NAME</c>).</summary>
    public const int MaxAreaMapName = 128;
    public static TerrainViewerArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? module = null, resource = null, sourceId = null, prototypes = null, moduleHash = null, resourceHash = null,
            actorResource = null, actorResourceHash = null, areaMobile = null, areaEffects = null, areaPatch = null, areaCaveMobile = null,
            gameRoot = null, areaMap = null, hero = null, areaMapSector = null, mainMenu = null;
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
                case "--area-effects": areaEffects = Unique(areaEffects, value); break;
                case "--area-patch": areaPatch = Unique(areaPatch, value); break;
                case "--area-cave-mobile": areaCaveMobile = Unique(areaCaveMobile, value); break;
                case "--game-root": gameRoot = Unique(gameRoot, value); break;
                case "--area-map": areaMap = Unique(areaMap, value); break;
                case "--area-map-sector": areaMapSector = Unique(areaMapSector, value); break;
                case "--hero": hero = Unique(hero, value); break;
                case "--main-menu": mainMenu = Unique(mainMenu, value is "true" or "false" ? value : throw new ArgumentException("Usage")); break;
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
            || (mode == TerrainViewerMode.CrashSiteArea && areaMap is null && (string.IsNullOrWhiteSpace(prototypes) || string.IsNullOrWhiteSpace(actorResource) || string.IsNullOrWhiteSpace(areaMobile)
                || string.IsNullOrWhiteSpace(areaEffects) || string.IsNullOrWhiteSpace(areaPatch) || actorSpecified || gameRoot is not null))
            // A whole interior map: its inputs come from the game folder's content stack.
            || (areaMap is not null && (mode != TerrainViewerMode.CrashSiteArea || string.IsNullOrWhiteSpace(areaMap) || areaMap.Length > MaxAreaMapName || areaMap.Any(char.IsControl) ||
                areaMap.Contains('/') || areaMap.Contains('\\') || string.IsNullOrWhiteSpace(gameRoot) || prototypes is not null ||
                string.IsNullOrWhiteSpace(actorResource) || areaMobile is not null || areaEffects is not null || areaPatch is not null || areaCaveMobile is not null || actorSpecified))
            || (areaMap is null && (gameRoot is not null || areaMapSector is not null))
            || (mode == TerrainViewerMode.CrashSiteArea && areaCaveMobile is not null && string.IsNullOrWhiteSpace(areaCaveMobile))
            || (hero is not null && (mode != TerrainViewerMode.CrashSiteArea || AreaStartingHero.Find(hero) is null))
            || (mainMenu is "true" && (mode != TerrainViewerMode.CrashSiteArea || hero is not null))
            || (mode != TerrainViewerMode.CrashSiteArea && (areaMobile is not null || areaEffects is not null || areaPatch is not null || areaCaveMobile is not null)))
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
        return new TerrainViewerArguments(mode, module, resource, sourceId, prototypes, moduleHash, resourceHash, actorResource, actorResourceHash, actor) { AreaMobilePath = areaMobile, AreaEffectsPath = areaEffects, AreaPatchPath = areaPatch, AreaCaveMobilePath = areaCaveMobile,
            GameRoot = gameRoot, AreaMapName = areaMap, Hero = hero is null ? null : AreaStartingHero.Find(hero), MainMenu = mainMenu is "true",
            AreaMapSector = areaMapSector is null ? null : ParseSector(areaMapSector) };
    }

    /// <summary>ARC-DEV-046: the map the game opens when it starts from the stored game folder, as <c>./arcanum area</c> does (its
    /// <c>AREA_MAP</c>, the main world map).</summary>
    public const string GameFolderStartMap = "Arcanum1-024-fixed";
    /// <summary>The source id of a start from the stored game folder (<c>./arcanum setup</c>'s default).</summary>
    public const string GameFolderSourceId = "local-original";

    /// <summary>ARC-DEV-046: the arguments of a start without command-line inputs: map mode on <see cref="GameFolderStartMap"/> with
    /// the main menu, every input from the ready game folder's <paramref name="inputs"/> (what <c>./arcanum area</c> passes). The same
    /// checks as <see cref="Parse"/> apply.</summary>
    public static TerrainViewerArguments ForGameFolder(Arcanum.Data.Content.GameFolderInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        return Parse(["--mode", "crash-site-area", "--module", inputs.ModuleArchive, "--resource", inputs.TerrainArchive,
            "--source-id", GameFolderSourceId, "--actor-resource", inputs.ActorArchive, "--game-root", inputs.GameRoot,
            "--area-map", GameFolderStartMap, "--main-menu", "true"]);
    }

    /// <summary>A sector as <c>X,Y</c>: two decimal numbers below 2^26 (the packed sector id's field width).</summary>
    private static Arcanum.Data.Area.AreaSectorCoordinate ParseSector(string value)
    {
        var parts = value.Split(',');
        if (parts.Length != 2 || !parts.All(p => p.Length is > 0 and <= 8 && p.All(char.IsAsciiDigit))) throw new ArgumentException("Usage");
        var (x, y) = (long.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), long.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
        return x < 67108864 && y < 67108864 ? new(x, y) : throw new ArgumentException("Usage");
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

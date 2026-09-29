using Arcanum.Data.Terrain;

namespace Arcanum.TerrainViewer.Core;

public static class CrashSiteProfile
{
    public const string ModuleEntry = "maps\\Arcanum1-024-fixed\\86570436012.sec";
    public const string TileMesEntry = "art\\tile\\tilename.mes";
    public const string FacadeMesEntry = "art\\facade\\facadename.mes";
    public const long ModuleSize = 355_938_305;
    public const long ResourceSize = 147_991_122;
    public const string ModuleSha256 = "8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f";
    public const string ResourceSha256 = "4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d";
    public const int CellCount = 4096;
    public const int TileCount = 3033;
    public const int FacadeCount = 1063;
    public const int NormalizationRecoveredCount = 457;
    public const int NormalizationLookupCount = 86;

    public static TerrainSectorSelector CreateSelector() => new(ModuleEntry, "Arcanum1-024-fixed", 1452, 1290,
        86570436012, 92928, 82560, 64, 64, new TerrainEntryExpectation(463, 2, 47291));
}

public enum TerrainViewerErrorCode
{
    Usage, InputUnavailable, FingerprintMismatch, InvalidSector, IncompleteSector, ResourceLimit, DecodeFailure, RenderFailure,
    StaticFingerprint, StaticCensus, StaticResource, StaticDecode, StaticPlan, Cancelled
}

/// <summary>Safe, explicit load boundary. Never infer this from an error code.</summary>
public enum TerrainViewerFailureStage
{
    Arguments, Fingerprint, TerrainCensus, TerrainDecode, StaticFingerprint,
    StaticCensus, StaticResource, StaticDecode, Plan, Upload, Readback
}

/// <summary>Immutable aggregate values that are safe to show after a completed boundary.</summary>
public sealed class TerrainViewerProgress
{
    public TerrainViewerProgress(int terrainResolved = 0, int terrainTiles = 0, int terrainFacades = 0,
        int staticCensus = 0, int staticAdmitted = 0, int staticOutsideScope = 0)
    {
        if (terrainResolved is < 0 or > CrashSiteProfile.CellCount || terrainTiles is < 0 or > CrashSiteProfile.TileCount
            || terrainFacades is < 0 or > CrashSiteProfile.FacadeCount || terrainTiles + terrainFacades > terrainResolved
            || staticCensus is < 0 or > 317 || staticAdmitted is < 0 or > 5 || staticOutsideScope is < 0 or > 312
            || staticAdmitted + staticOutsideScope > staticCensus)
            throw new ArgumentOutOfRangeException(nameof(terrainResolved), "Diagnostic progress is outside the safe aggregate profile.");
        TerrainResolved = terrainResolved;
        TerrainTiles = terrainTiles;
        TerrainFacades = terrainFacades;
        StaticCensus = staticCensus;
        StaticAdmitted = staticAdmitted;
        StaticOutsideScope = staticOutsideScope;
    }

    public int TerrainResolved { get; }
    public int TerrainTiles { get; }
    public int TerrainFacades { get; }
    public int StaticCensus { get; }
    public int StaticAdmitted { get; }
    public int StaticOutsideScope { get; }

    /// <summary>Returns a separately validated post-static-census snapshot.</summary>
    public TerrainViewerProgress WithStaticCensus(int staticCensus, int staticAdmitted, int staticOutsideScope) =>
        new(TerrainResolved, TerrainTiles, TerrainFacades, staticCensus, staticAdmitted, staticOutsideScope);

    public static TerrainViewerProgress Empty { get; } = new();
}

public sealed record TerrainViewerError(TerrainViewerErrorCode Code, int ResolvedCount = 0, int IssueCount = 0,
    TerrainViewerFailureStage Stage = TerrainViewerFailureStage.Arguments, TerrainViewerProgress? Progress = null)
{
    public TerrainViewerProgress SafeProgress => Progress ?? TerrainViewerProgress.Empty;
    public override string ToString() => $"{Code} stage={Stage} (resolved={ResolvedCount}, issues={IssueCount})";
}

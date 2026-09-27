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
    Usage, InputUnavailable, FingerprintMismatch, InvalidSector, IncompleteSector, ResourceLimit, DecodeFailure, RenderFailure
}

public sealed record TerrainViewerError(TerrainViewerErrorCode Code, int ResolvedCount = 0, int IssueCount = 0)
{
    public override string ToString() => $"{Code} (resolved={ResolvedCount}, issues={IssueCount})";
}

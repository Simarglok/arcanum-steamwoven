namespace Arcanum.Data.Terrain;

public enum TerrainKind { Tile, Facade }
public enum TerrainPathProfile { CeNonWindowsMacOs }
public enum TerrainDiagnosticMode { EditorDiagnostic }
public enum TerrainErrorCode
{
    InvalidSelector, UnsupportedProfile, MissingModuleEntry, AmbiguousModuleEntry,
    MissingMesEntry, AmbiguousMesEntry, InvalidFraming, LimitExceeded, UnsupportedFormat,
    EntryIdentityMismatch, Incomplete
}
public enum TerrainIssueCode
{
    UnsupportedClass, InvalidTileSelector, InvalidFacadeSelector, MissingResource,
    AmbiguousResource, UnsupportedArt, InvalidFrame, UnavailablePalette
}
public enum TerrainSection
{
    LightCount, Lights, TileWords, Roof, Placeholder, TileScripts, SectorScript,
    MetadataAndSound, BlockMask, ObjectWindow, TerminalObjectCount
}

public class TerrainException : Exception
{
    public TerrainException(TerrainErrorCode code, string message, Exception? innerException = null) : base(message, innerException) => Code = code;
    public TerrainErrorCode Code { get; }
}
public sealed class TerrainFormatException : TerrainException
{
    public TerrainFormatException(TerrainErrorCode code, string message, Exception? innerException = null) : base(code, message, innerException) { }
}
public sealed class TerrainLimitExceededException : TerrainException
{
    public TerrainLimitExceededException(string message) : base(TerrainErrorCode.LimitExceeded, message) { }
}
public sealed class TerrainUnsupportedException : TerrainException
{
    public TerrainUnsupportedException(TerrainErrorCode code, string message) : base(code, message) { }
}
public sealed class TerrainSelectionException : TerrainException
{
    public TerrainSelectionException(TerrainErrorCode code, string message) : base(code, message) { }
}
public sealed class TerrainIncompleteException : TerrainException
{
    public TerrainIncompleteException(TerrainLoadReport report) : base(TerrainErrorCode.Incomplete, "The terrain sector has unresolved cells.") => Report = report;
    public TerrainLoadReport Report { get; }
}

public sealed record TerrainEntryExpectation(int Ordinal, uint Flags, uint DeclaredDecodedSize);
public sealed record TerrainSectorSelector
{
    public TerrainSectorSelector(string moduleEntryPath, string mapLabel, int sectorX, int sectorY, long packedSectorId,
        int originX, int originY, int width, int height, TerrainEntryExpectation? expectation = null)
    {
        ValidateAscii(moduleEntryPath, nameof(moduleEntryPath));
        ValidateAscii(mapLabel, nameof(mapLabel));
        if (width != 64 || height != 64) throw new TerrainUnsupportedException(TerrainErrorCode.InvalidSelector, "Only 64 by 64 terrain sectors are supported.");
        ModuleEntryPath = moduleEntryPath; MapLabel = mapLabel; SectorX = sectorX; SectorY = sectorY;
        PackedSectorId = packedSectorId; OriginX = originX; OriginY = originY; Width = width; Height = height; Expectation = expectation;
    }
    public string ModuleEntryPath { get; }
    public string MapLabel { get; }
    public int SectorX { get; }
    public int SectorY { get; }
    public long PackedSectorId { get; }
    public int OriginX { get; }
    public int OriginY { get; }
    public int Width { get; }
    public int Height { get; }
    public TerrainEntryExpectation? Expectation { get; }
    internal static void ValidateAscii(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrEmpty(value, parameter);
        if (value.Any(c => c is > '\x7f' or '\0')) throw new ArgumentException("Value must be non-empty ASCII without NUL.", parameter);
    }
}

public sealed record TerrainResourceSource
{
    public TerrainResourceSource(DatArchive archive, string sourceId, string tileMesEntryPath, string facadeMesEntryPath,
        TerrainPathProfile pathProfile)
    {
        Archive = archive ?? throw new ArgumentNullException(nameof(archive));
        TerrainSectorSelector.ValidateAscii(sourceId, nameof(sourceId));
        TerrainSectorSelector.ValidateAscii(tileMesEntryPath, nameof(tileMesEntryPath));
        TerrainSectorSelector.ValidateAscii(facadeMesEntryPath, nameof(facadeMesEntryPath));
        if (pathProfile != TerrainPathProfile.CeNonWindowsMacOs) throw new TerrainUnsupportedException(TerrainErrorCode.UnsupportedProfile, "Only the CE non-Windows/macOS path profile is supported.");
        SourceId = sourceId; TileMesEntryPath = tileMesEntryPath; FacadeMesEntryPath = facadeMesEntryPath; PathProfile = pathProfile;
    }
    public DatArchive Archive { get; }
    public string SourceId { get; }
    public string TileMesEntryPath { get; }
    public string FacadeMesEntryPath { get; }
    public TerrainPathProfile PathProfile { get; }
}

public sealed class TerrainLoaderOptions
{
    public int MaxSectorDecodedEntryBytes { get; init; } = 1024 * 1024;
    public int MaxMesDecodedEntryBytes { get; init; } = 1024 * 1024;
    public int MaxArtDecodedEntryBytes { get; init; } = 1024 * 1024;
    public int MaxLights { get; init; } = 1024;
    public int ExactCellCount { get; init; } = 4096;
    public int MaxRoofWords { get; init; } = 256;
    public int MaxTileScripts { get; init; } = 4096;
    public int MaxObjectTerminalCount { get; init; } = 4096;
    public int MaxDistinctArtMetadataEntries { get; init; } = 4096;
    public int MaxMesEntries { get; init; } = 4096;
    public int MaxMesValueBytes { get; init; } = 4096;
    public ArtDecoderOptions ArtOptions { get; init; } = new()
    {
        MaxInputBytes = 1024 * 1024,
        MaxEncodedFrameBytes = 1024 * 1024,
        MaxFrameCount = 4096,
        MaxDimension = 4096,
        MaxFrameTableBytes = 256 * 1024,
        MaxFramePixels = 4 * 1024 * 1024,
        MaxRgbaBytes = 16 * 1024 * 1024
    };
    internal void Validate()
    {
        if (MaxSectorDecodedEntryBytes <= 0 || MaxMesDecodedEntryBytes <= 0 || MaxArtDecodedEntryBytes <= 0 ||
            MaxLights <= 0 || ExactCellCount != 4096 || MaxRoofWords < 256 || MaxTileScripts <= 0 ||
            MaxObjectTerminalCount <= 0 || MaxDistinctArtMetadataEntries <= 0 || MaxMesEntries <= 0 || MaxMesValueBytes <= 0 || ArtOptions is null)
            throw new ArgumentOutOfRangeException(nameof(TerrainLoaderOptions), "Terrain limits must be positive and the cell count must be exactly 4096.");
        ArtOptions.Validate();
    }
}

public readonly record struct TerrainPoint(int X, int Y);
public readonly record struct TerrainRect
{
    public TerrainRect(int x, int y, int width, int height)
    {
        if (width < 0 || height < 0) throw new ArgumentOutOfRangeException(nameof(width), "Rectangle dimensions must be non-negative.");
        X = x; Y = y; Width = width; Height = height;
    }
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
}
public readonly record struct TerrainDiagnosticPlacement(TerrainRect Footprint, TerrainRect Source, TerrainRect Destination, bool FlipX, bool FlipY);
public readonly record struct TerrainDiagnosticOrderKey(TerrainDiagnosticMode Mode, int Layer, int LocalY, int LocalX);
public readonly record struct TerrainByteRange(int Offset, int Length)
{
    public int End => checked(Offset + Length);
}
public sealed class TerrainOpaqueWindow
{
    internal TerrainOpaqueWindow(TerrainByteRange range, int terminalObjectCount, byte[] bytes)
    { Range = range; TerminalObjectCount = terminalObjectCount; Bytes = Array.AsReadOnly(bytes); }
    public TerrainByteRange Range { get; }
    public int TerminalObjectCount { get; }
    public IReadOnlyList<byte> Bytes { get; }
}
public sealed class TerrainSectorFraming
{
    internal TerrainSectorFraming(IReadOnlyDictionary<TerrainSection, TerrainByteRange> ranges, int lightCount, int roofDiscriminator,
        int placeholder, int tileScriptCount, TerrainOpaqueWindow objectWindow)
    { Ranges = ranges; LightCount = lightCount; RoofDiscriminator = roofDiscriminator; Placeholder = placeholder; TileScriptCount = tileScriptCount; ObjectWindow = objectWindow; }
    public IReadOnlyDictionary<TerrainSection, TerrainByteRange> Ranges { get; }
    public int LightCount { get; }
    public int RoofDiscriminator { get; }
    public int Placeholder { get; }
    public int TileScriptCount { get; }
    public TerrainOpaqueWindow ObjectWindow { get; }
    public TerrainByteRange this[TerrainSection section] => Ranges[section];
}

public sealed record TerrainResourceEntryIdentity(string SourceId, int DatOrdinal, uint Flags, uint DeclaredDecodedSize, string LogicalEntryName);
public sealed record TerrainIssue(TerrainIssueCode Code, int Ordinal, string Message, string? LogicalPath = null);
public sealed class TerrainRecord
{
    internal TerrainRecord(int ordinal, int localX, int localY, int worldX, int worldY, uint rawId, uint lookupId, TerrainKind kind,
        string lookupBranch, string logicalPath, TerrainResourceEntryIdentity identity, ArtFrameSelection selection, int effectivePaletteSlot,
        ArtFrameMetadata frameMetadata, TerrainPoint projectedPoint, TerrainPoint diagnosticAnchor, TerrainRect diagnosticFootprint,
        bool flipX, TerrainDiagnosticOrderKey orderKey)
    { Ordinal = ordinal; LocalX = localX; LocalY = localY; WorldX = worldX; WorldY = worldY; RawId = rawId; LookupId = lookupId; Kind = kind; LookupBranch = lookupBranch; LogicalPath = logicalPath; ResourceIdentity = identity; Selection = selection; EffectivePaletteSlot = effectivePaletteSlot; FrameMetadata = frameMetadata; ProjectedPoint = projectedPoint; DiagnosticAnchor = diagnosticAnchor; DiagnosticFootprint = diagnosticFootprint; FlipX = flipX; DiagnosticOrderKey = orderKey; }
    public int Ordinal { get; }
    public int LocalX { get; }
    public int LocalY { get; }
    public int WorldX { get; }
    public int WorldY { get; }
    public uint RawId { get; }
    public uint LookupId { get; }
    public TerrainKind Kind { get; }
    public string LookupBranch { get; }
    public string LogicalPath { get; }
    public TerrainResourceEntryIdentity ResourceIdentity { get; }
    public ArtFrameSelection Selection { get; }
    public int EffectivePaletteSlot { get; }
    public ArtFrameMetadata FrameMetadata { get; }
    public TerrainPoint ProjectedPoint { get; }
    public TerrainPoint DiagnosticAnchor { get; }
    public TerrainRect DiagnosticFootprint { get; }
    public bool FlipX { get; }
    public TerrainDiagnosticOrderKey DiagnosticOrderKey { get; }
}
public sealed class TerrainCellOutcome
{
    internal TerrainCellOutcome(int ordinal, TerrainRecord? record, TerrainIssue? issue) { Ordinal = ordinal; Record = record; Issue = issue; }
    public int Ordinal { get; }
    public TerrainRecord? Record { get; }
    public TerrainIssue? Issue { get; }
}
public sealed class TerrainLoadReport
{
    internal TerrainLoadReport(TerrainSectorSelector selector, TerrainSectorFraming framing, TerrainCellOutcome[] outcomes)
    { Selector = selector; Framing = framing; Outcomes = Array.AsReadOnly(outcomes); ResolvedRecords = Array.AsReadOnly(outcomes.Where(o => o.Record is not null).Select(o => o.Record!).ToArray()); Issues = Array.AsReadOnly(outcomes.Where(o => o.Issue is not null).Select(o => o.Issue!).ToArray()); }
    public TerrainSectorSelector Selector { get; }
    public TerrainSectorFraming Framing { get; }
    public IReadOnlyList<TerrainCellOutcome> Outcomes { get; }
    public IReadOnlyList<TerrainRecord> ResolvedRecords { get; }
    public IReadOnlyList<TerrainIssue> Issues { get; }
    public bool IsComplete => Issues.Count == 0;
}
public sealed class TerrainSector
{
    internal TerrainSector(TerrainLoadReport report)
    { Selector = report.Selector; Framing = report.Framing; Records = Array.AsReadOnly(report.ResolvedRecords.ToArray()); DiagnosticOrder = Array.AsReadOnly(Records.OrderBy(r => r.DiagnosticOrderKey.Layer).ThenBy(r => r.DiagnosticOrderKey.LocalY).ThenBy(r => r.DiagnosticOrderKey.LocalX).ToArray()); }
    public TerrainSectorSelector Selector { get; }
    public TerrainSectorFraming Framing { get; }
    public IReadOnlyList<TerrainRecord> Records { get; }
    public IReadOnlyList<TerrainRecord> DiagnosticOrder { get; }
}

using System.Collections.ObjectModel;

namespace Arcanum.Data.StaticObjects;

/// <summary>Bounded, caller-selected input for the v119 static-object inspection.</summary>
public sealed class StaticObjectSource
{
    public StaticObjectSource(DatArchive moduleArchive, string moduleEntryPath, DatArchive resourceArchive)
    {
        ModuleArchive = moduleArchive ?? throw new ArgumentNullException(nameof(moduleArchive));
        ResourceArchive = resourceArchive ?? throw new ArgumentNullException(nameof(resourceArchive));
        ValidateLabel(moduleEntryPath, nameof(moduleEntryPath), allowPath: true);
        ModuleEntryPath = moduleEntryPath;
    }

    public DatArchive ModuleArchive { get; }
    public string ModuleEntryPath { get; }
    public DatArchive ResourceArchive { get; }

    internal static void ValidateLabel(string value, string parameter, bool allowPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(value, parameter);
        if (value.Any(c => c is > '\x7f' or '\0') || (!allowPath && (value.Contains('/') || value.Contains('\\'))))
            throw new ArgumentException("The value must be a non-path ASCII label without NUL.", parameter);
    }
}

public sealed class StaticObjectPrototypeSource
{
    public StaticObjectPrototypeSource(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(directoryPath, nameof(directoryPath));
        DirectoryPath = directoryPath;
    }

    public string DirectoryPath { get; }
}

public sealed class StaticObjectLoaderOptions
{
    public uint MaxArchiveBytes { get; init; } = 512 * 1024 * 1024;
    public uint MaxArchiveIndexBytes { get; init; } = 64 * 1024 * 1024;
    public int MaxArchiveEntries { get; init; } = 128 * 1024;
    public int MaxSectorBytes { get; init; } = 1024 * 1024;
    public int MaxRecords { get; init; } = 4096;
    public int MaxFields { get; init; } = 128;
    public int MaxArrayElements { get; init; } = 262144;
    public int MaxStringBytes { get; init; } = 1024 * 1024;
    public int MaxPrototypeBytes { get; init; } = 1024 * 1024;
    public int MaxResourceBytes { get; init; } = 1024 * 1024;
    public int MaxMetadataEntries { get; init; } = 8192;
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
        if (MaxArchiveBytes == 0 || MaxArchiveIndexBytes == 0 || MaxArchiveEntries <= 0 || MaxSectorBytes <= 0 || MaxRecords <= 0 || MaxFields <= 0 || MaxArrayElements <= 0 || MaxStringBytes <= 0 ||
            MaxPrototypeBytes <= 0 || MaxResourceBytes <= 0 || MaxMetadataEntries <= 0 || ArtOptions is null)
            throw new ArgumentOutOfRangeException(nameof(StaticObjectLoaderOptions), "All static-object limits must be positive.");
        ArtOptions.Validate();
    }
}

public enum StaticObjectCensusStatus { Complete, InvalidFraming }
public enum StaticObjectFieldStatus { Resolved, UnresolvedReference, UnsupportedType }
public enum StaticObjectResourceStatus { Resolved, NotEvaluated, OutsideResourceScope, UnresolvedResource, InvalidArtSelector }
public enum StaticObjectErrorCode { InvalidSelector, MissingModule, AmbiguousModule, InvalidFraming, LimitExceeded, UnsupportedVersion, UnsupportedType, InvalidRecord, UnresolvedReference, UnresolvedResource, InvalidArtSelector }
/// <summary>Sanitized stage of a bounded loader failure.</summary>
public enum StaticObjectFailureStage { Selector, Archive, SectorFraming, Record, Prototype, Mapping, Art, FingerprintInput }

public sealed class StaticObjectException : Exception
{
    internal StaticObjectException(StaticObjectErrorCode code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code; Stage = StaticObjectFailureStage.Record;
    }

    public StaticObjectException(StaticObjectErrorCode code, StaticObjectFailureStage stage, string message,
        int? sourceIndex = null, int? offset = null) : base(message)
    {
        Code = code; Stage = stage; SourceIndex = sourceIndex; Offset = offset;
    }
    public StaticObjectErrorCode Code { get; }
    public StaticObjectFailureStage Stage { get; }
    public int? SourceIndex { get; }
    public int? Offset { get; }
}

public sealed class StaticObjectRecord
{
    internal StaticObjectRecord(int sourceIndex, int objectType, int? prototypeAid, int? directCurrentArt, uint? directFlags,
        long? location, int? directOffsetX, int? directOffsetY, StaticObjectFieldStatus fieldStatus,
        int? currentArt, bool currentArtInherited, uint? effectiveFlags, bool flagsInherited, bool? isStatic,
        StaticObjectResourceStatus resourceStatus, StaticObjectArtMetadata? artMetadata)
    {
        SourceIndex = sourceIndex; ObjectType = objectType; PrototypeAid = prototypeAid; DirectCurrentArt = directCurrentArt;
        DirectFlags = directFlags; Location = location; DirectOffsetX = directOffsetX; DirectOffsetY = directOffsetY;
        FieldStatus = fieldStatus; CurrentArt = currentArt; CurrentArtInherited = currentArtInherited; EffectiveFlags = effectiveFlags;
        FlagsInherited = flagsInherited; IsStatic = isStatic; ResourceStatus = resourceStatus; ArtMetadata = artMetadata;
        LocalX = location is long locationValue ? (int)(locationValue & 0x3f) : null;
        LocalY = location is long locationYValue ? (int)((locationYValue >> 32) & 0x3f) : null;
    }

    public int SourceIndex { get; }
    public int ObjectType { get; }
    public int? PrototypeAid { get; }
    public int? DirectCurrentArt { get; }
    public uint? DirectFlags { get; }
    public long? Location { get; }
    /// <summary>Serialized sector-local coordinates, retained only when the location field is present.</summary>
    public int? LocalX { get; }
    public int? LocalY { get; }
    public int? DirectOffsetX { get; }
    public int? DirectOffsetY { get; }
    public StaticObjectFieldStatus FieldStatus { get; }
    public int? CurrentArt { get; }
    public bool CurrentArtInherited { get; }
    public uint? EffectiveFlags { get; }
    public bool FlagsInherited { get; }
    public bool? IsStatic { get; }
    public StaticObjectResourceStatus ResourceStatus { get; }
    public StaticObjectArtMetadata? ArtMetadata { get; }
}

/// <summary>Path-free identity for a selected resource archive entry.</summary>
public sealed record StaticObjectResourceIdentity(int DatOrdinal, uint Flags, uint DeclaredDecodedSize,
    string NormalizedLogicalNameSha256);

public sealed record StaticObjectArtMetadata(int ArtType, int SceneryCategory, int ArtNumber, int RotationGroup, int Frame,
    int Palette, int EffectivePalette, int Width, int Height, int HotX, int HotY, int ArtOffsetX, int ArtOffsetY,
    StaticObjectResourceIdentity ResourceIdentity);

public sealed class StaticObjectInspection
{
    internal StaticObjectInspection(StaticObjectCensusStatus censusStatus, int terminalRecordCount, IReadOnlyList<StaticObjectRecord> records)
    {
        CensusStatus = censusStatus; TerminalRecordCount = terminalRecordCount;
        Records = new ReadOnlyCollection<StaticObjectRecord>(records.ToArray());
        FieldResolvedCount = Records.Count(record => record.FieldStatus == StaticObjectFieldStatus.Resolved);
        ResourceResolvedCount = Records.Count(record => record.ResourceStatus == StaticObjectResourceStatus.Resolved);
        ResourceNotEvaluatedCount = Records.Count(record => record.ResourceStatus == StaticObjectResourceStatus.NotEvaluated);
        ResourceOutsideScopeCount = Records.Count(record => record.ResourceStatus == StaticObjectResourceStatus.OutsideResourceScope);
    }

    public StaticObjectCensusStatus CensusStatus { get; }
    public int TerminalRecordCount { get; }
    public IReadOnlyList<StaticObjectRecord> Records { get; }
    public int FieldResolvedCount { get; }
    public int ResourceResolvedCount { get; }
    public int ResourceNotEvaluatedCount { get; }
    public int ResourceOutsideScopeCount { get; }
}

/// <summary>Identifier-only immutable-input evidence; paths and payloads are intentionally not retained.</summary>
public sealed record StaticObjectInputFingerprint(long Length, string Sha256);

/// <summary>Result of the opt-in path-owning validation operation.</summary>
public sealed class StaticObjectValidation
{
    internal StaticObjectValidation(StaticObjectInspection inspection, StaticObjectInputFingerprint module,
        StaticObjectInputFingerprint resource, IReadOnlyList<StaticObjectPrototypeFingerprint> prototypes, string prototypeAggregate)
    {
        Inspection = inspection; Module = module; Resource = resource;
        PrototypeFingerprints = new ReadOnlyCollection<StaticObjectPrototypeFingerprint>(prototypes.ToArray());
        PrototypeAggregateSha256 = prototypeAggregate;
    }

    public StaticObjectInspection Inspection { get; }
    public StaticObjectInputFingerprint Module { get; }
    public StaticObjectInputFingerprint Resource { get; }
    public IReadOnlyList<StaticObjectPrototypeFingerprint> PrototypeFingerprints { get; }
    public string PrototypeAggregateSha256 { get; }
}

/// <summary>Identifier-only evidence for one canonical type-A prototype file.</summary>
public sealed record StaticObjectPrototypeFingerprint(int Aid, StaticObjectInputFingerprint Fingerprint);

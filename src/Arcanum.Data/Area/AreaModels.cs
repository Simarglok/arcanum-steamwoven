using System.Collections.ObjectModel;

namespace Arcanum.Data.Area;

public enum AreaSourceRole { Module, Resource1, Resource2, NormalGameMobile, Prototype }
public readonly record struct AreaInputKey(AreaSourceRole Role, int Number = 0);
public sealed record AreaInputIdentity(AreaInputKey Key, long Length, string Sha256);
public enum AreaScope { Core, Context, Outside, Unknown }
public enum AreaSourceKind { Terrain, Static, Mobile }
public enum AreaLayerKind { Body, Foreground, Background, Underlay }
public enum AreaPresence { Absent, Null, Value, Empty, Array }
public enum AreaValueOrigin { Instance, Prototype }
public enum AreaDisposition { BodyEligibleConditionally, ExteriorSectorInsertionRefusedByInventory, BodySuppressedBySerializedState, ContextOnly, MissingMandatoryFact }
public enum AreaErrorCode { MissingSource, AmbiguousSource, InputIdentityMismatch, InputChangedDuringRead, MalformedRecord, UnsupportedVersionOrSchema, UnsupportedArtClassOrState, UnsupportedPhysicalSelection, UnknownLocation, BudgetExceeded, DuplicateIdentity, InvalidSelection, LeaseLimit, SnapshotMismatch }
public enum AreaStage { Input, Selection, Sector, Aggregate, Prototype, Correspondence, Appearance, Mapping, Frame, Publication }
public enum AreaBudgetPhase { O, I0, I1, I2, I3, I4, I5, I6, I7, F, C }
public enum AreaBudgetDomain { Managed, Phase, Retained, Component, Read, Requests, Stored, Decoded, Rgba, Encoded, Copy }
public enum AreaBudgetCallsite { Owner, Authentication, ArchiveIndex, Entry, File, Survey, Wire, Metadata, ArtTable, Pixels, PixelHash, ConsumerCopy }
public readonly record struct AreaBudgetContext(AreaBudgetCallsite Callsite, AreaInputKey? Source = null, int? Ordinal = null, long? Offset = null, int? SourceIndex = null);
public readonly record struct AreaBudgetRejection(AreaBudgetDomain Domain, long Used, long Requested, long Limit, AreaBudgetPhase Phase, AreaBudgetContext Context, bool ComponentActive, long ComponentAllocated, AreaBudgetUsage Before, AreaBudgetUsage After);
public readonly record struct AreaBudgetPhaseUsage(AreaBudgetPhase Phase, long Visits, long Cumulative, long HighWater);
public sealed record AreaIssue(AreaErrorCode Code, AreaStage Stage, long? Offset = null, int? SourceIndex = null);
public sealed class AreaException : Exception
{
    public AreaException(AreaIssue issue) : base($"Area data failure: {issue.Code} at {issue.Stage}.") => Issue = issue;
    public AreaIssue Issue { get; }
}
public sealed class AreaIncompleteException : Exception
{
    public AreaIncompleteException(AreaInspection report) : base("The complete mandatory area data is not ready.") => Report = report;
    public AreaInspection Report { get; }
}

/// <summary>A legal, nonnegative 26-bit sector coordinate. Invalid values never wrap.</summary>
public readonly record struct AreaSectorCoordinate
{
    public AreaSectorCoordinate(long x, long y)
    {
        if (x is < 0 or >= 67108864 || y is < 0 or >= 67108864) throw new ArgumentOutOfRangeException(nameof(x));
        X = x; Y = y;
    }
    public long X { get; }
    public long Y { get; }
    public long Packed => checked(X + (Y << 26));
    public long OriginX => checked(X * 64);
    public long OriginY => checked(Y * 64);
    public static AreaSectorCoordinate Unpack(long packed)
    {
        if (packed is < 0 or >= 4503599627370496) throw new ArgumentOutOfRangeException(nameof(packed));
        return new(packed & 67108863, packed >> 26);
    }
}
public readonly record struct AreaSectorKey(string SourceSha256, int RuntimeMap, long Packed);
public readonly record struct AreaObjectKey(string SourceSha256, int RuntimeMap, AreaSourceKind Kind, long? Sector, int SourceIndex, long Offset, string ObjectIdSha256, int Length);
public readonly record struct AreaVisualKey(AreaSourceKind Kind, AreaSectorKey Sector, int SourceIndex, string ObjectIdSha256, AreaLayerKind Layer = AreaLayerKind.Body, int SparseIndex = 0, AreaObjectKey? Object = null);
public sealed record AreaEntryIdentity(AreaInputKey Source, int Ordinal, uint Flags, uint StoredBytes, uint DecodedBytes, uint Offset, string NameSha256);
public sealed record AreaSparseElement(int Key, IReadOnlyList<long> Words);
public sealed record AreaFieldValue(int Slot, AreaPresence Presence, AreaValueOrigin Origin, long? Number, IReadOnlyList<AreaSparseElement> Elements, string? Fingerprint);
public sealed record AreaByteRange(int Start, int Length);
public sealed record AreaSectorRecord(AreaSectorKey Key, AreaSectorCoordinate Coordinate, AreaScope Scope, bool Present, int? DatOrdinal, int? Townmap, int LightCount, int RoofDiscriminator, int Placeholder, int TerminalCount, IReadOnlyList<AreaByteRange> Sections, IReadOnlyList<IReadOnlyList<int>> Lights, AreaEntryIdentity? Entry, string? PayloadSha256);
public sealed record AreaTerrainRecord(AreaVisualKey Key, int LocalX, int LocalY, long WorldX, long WorldY, uint RawArt, uint LookupArt, AreaScope Scope, AreaVisualKey SelectedVisual);
public sealed record AreaObjectRecord(AreaObjectKey Key, int Version, int Type, int PrototypeNumber, string PrototypeReferenceSha256, string PrototypeOwnSha256, string RawSha256, string SemanticSha256, AreaScope Scope, long? SerializedLocation, long? WorldX, long? WorldY, AreaDisposition Disposition, IReadOnlyList<AreaFieldValue> DirectFields, IReadOnlyList<AreaFieldValue> EffectiveFields, bool SchemaReady, bool PlacementReady, bool RequiredLayersReady);
public sealed record AreaVisualRecord(AreaVisualKey Key, uint RawArt, uint LookupArt, AreaEntryIdentity Entry, string PayloadSha256, int ViewLength, int NominalFacing, int SourceGroup, int PhysicalGroup, int Frame, int RequestedPalette, int EffectivePalette, bool Mirrored, int Width, int Height, int HotX, int HotY, int ArtOffsetX, int ArtOffsetY, int ObjectOffsetX, int ObjectOffsetY, int Scale, int EncodedBytes, int RgbaBytes, string RgbaSha256, string Policy);
public sealed record AreaEndpoint(long Sector, int Ordinal, int Prototype, int Sap, int Script, int CatalogueDestination, int RuntimeDestination, long ArrivalX, long ArrivalY, string SourceObjectIdSha256);
public sealed record AreaReconciliation(int Survey, int CoreSectors, int ContextPresent, int ContextMissing, int CoreTerrain, int ContextTerrain, int Statics, int Aggregate, int CoreMobiles, int ContextMobiles, int OutsideMobiles, int UnknownMobiles, int EditorCorrespondence, int Prototypes, int Visuals);
public sealed record AreaBudgetUsage(long ReadReserved, long ReadActual, long ManagedReserved, long PhaseReserved, long RetainedReserved, long FrameRequests, long FrameStored, long FrameDecoded, long FrameRgba, long FrameEncoded, long ConsumerCopyBytes);

public sealed class AreaSnapshot
{
    internal AreaSnapshot(object owner, int generation, AreaSourcePlan plan, IEnumerable<AreaSectorRecord> sectors, IEnumerable<AreaTerrainRecord> terrain,
        IEnumerable<AreaObjectRecord> objects, IEnumerable<AreaVisualRecord> visuals, AreaReconciliation counts, IEnumerable<AreaEndpoint> endpoints)
    {
        Owner = owner; Generation = generation; CatalogueMap = plan.CatalogueMap; RuntimeMap = plan.RuntimeMap; Profile = plan.Profile;
        Inputs = Array.AsReadOnly(plan.Identities.ToArray());
        Sectors = Array.AsReadOnly(sectors.ToArray()); Terrain = Array.AsReadOnly(terrain.ToArray()); Objects = Array.AsReadOnly(objects.ToArray());
        Visuals = Array.AsReadOnly(visuals.ToArray()); Reconciliation = counts; Endpoints = Array.AsReadOnly(endpoints.ToArray());
    }
    internal object Owner { get; }
    internal int Generation { get; }
    public int CatalogueMap { get; }
    public int RuntimeMap { get; }
    public string Profile { get; }
    public IReadOnlyList<AreaInputIdentity> Inputs { get; }
    public IReadOnlyList<AreaSectorRecord> Sectors { get; }
    public IReadOnlyList<AreaTerrainRecord> Terrain { get; }
    public IReadOnlyList<AreaObjectRecord> Objects { get; }
    public IReadOnlyList<AreaVisualRecord> Visuals { get; }
    public IReadOnlyList<AreaEndpoint> Endpoints { get; }
    public AreaReconciliation Reconciliation { get; }
    public bool RendererExecutionVerified => false;
    public bool GameplayVerified => false;
}
public sealed class AreaInspection
{
    internal AreaInspection(AreaSnapshot? snapshot, IEnumerable<AreaIssue> issues, AreaBudgetUsage usage)
    { Snapshot = snapshot; Issues = Array.AsReadOnly(issues.ToArray()); Usage = usage; }
    public AreaSnapshot? Snapshot { get; }
    public IReadOnlyList<AreaIssue> Issues { get; }
    public AreaBudgetUsage Usage { get; }
    public bool IsComplete => Snapshot is not null && Issues.Count == 0;
}

/// <summary>Explicit owner transfer. All streams must be readable/seekable and distinct.
/// leaveOpen is honored on success AND failed Open. Streams may not be used externally while leased to an owner.</summary>
public sealed class AreaInput
{
    public AreaInput(AreaInputKey key, Stream stream, bool leaveOpen = false)
    { Key = key; Stream = stream ?? throw new ArgumentNullException(nameof(stream)); LeaveOpen = leaveOpen; }
    public static AreaInput FromFile(AreaInputKey key, string path) => new(key, new AreaPathStream(path));
    public AreaInputKey Key { get; }
    internal Stream Stream { get; }
    public bool LeaveOpen { get; }
}
public sealed class AreaInputSet
{
    public AreaInputSet(IEnumerable<AreaInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        Inputs = Array.AsReadOnly(inputs.ToArray());
        if (Inputs.Any(i => i is null) || Inputs.Select(i => i.Key).Distinct().Count() != Inputs.Count ||
            Inputs.Select(i => i.Stream).Distinct(ReferenceEqualityComparer.Instance).Count() != Inputs.Count)
            throw new ArgumentException("Area inputs must have distinct keys and streams.", nameof(inputs));
    }
    internal IReadOnlyList<AreaInput> Inputs { get; }
}
public enum AreaMappingRole { Tile, Facade, Scenery, Container, ItemGround, MonsterNames, UniqueNpcNames, MissingPolicy }
public sealed record AreaMappingBinding(AreaMappingRole Role, AreaEntryIdentity Entry, string PayloadSha256);
public sealed record AreaArtBinding(uint RawArt, AreaEntryIdentity Entry);

/// <summary>Version-one explicit FILE-effective source plan. Constructor plans are structural,
/// not proof that caller inputs are authentic original data. The original validator uses its own pinned profile.</summary>
public sealed class AreaSourcePlan
{
    public AreaSourcePlan(int catalogueMap, int runtimeMap, AreaSectorCoordinate start, int townmap,
        string sectorPrefix, IEnumerable<AreaInputIdentity> identities, IEnumerable<AreaMappingBinding> mappings, IEnumerable<AreaArtBinding>? artBindings = null)
    {
        if (catalogueMap < 0 || runtimeMap < 0 || townmap < 0) throw new ArgumentOutOfRangeException(nameof(catalogueMap));
        if (string.IsNullOrEmpty(sectorPrefix) || sectorPrefix.Any(c => c > 127 || c == '\0') || sectorPrefix.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("An explicit safe ASCII selector is required.", nameof(sectorPrefix));
        CatalogueMap = catalogueMap; RuntimeMap = runtimeMap; Start = start; Townmap = townmap; SectorPrefix = sectorPrefix.Replace('\\', '/').TrimEnd('/').ToLowerInvariant() + "/";
        Identities = Array.AsReadOnly(identities.ToArray()); Mappings = Array.AsReadOnly(mappings.ToArray());
        ArtBindings = Array.AsReadOnly((artBindings ?? []).ToArray());
        if (Identities.Select(i => i.Key).Distinct().Count() != Identities.Count || Mappings.Select(i => i.Role).Distinct().Count() != Mappings.Count ||
            Identities.Any(i => i.Length < 0 || !Digest(i.Sha256)) || Mappings.Any(m => !Digest(m.PayloadSha256) || !Digest(m.Entry.NameSha256)))
            throw new ArgumentException("Source identities must be unique, finite and SHA256-bound.");
    }
    public int Version => 1;
    public int CatalogueMap { get; }
    public int RuntimeMap { get; }
    public AreaSectorCoordinate Start { get; }
    public int Townmap { get; }
    internal string SectorPrefix { get; }
    public IReadOnlyList<AreaInputIdentity> Identities { get; }
    public IReadOnlyList<AreaMappingBinding> Mappings { get; }
    public IReadOnlyList<AreaArtBinding> ArtBindings { get; }
    public string Profile => Original ? "arc-data-009-complete-file-effective-source-v1" : "structural-file-effective-v1";
    internal bool Original { get; init; }
    internal static bool Digest(string value) => value is not null && value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public sealed class AreaLoaderOptions
{
    // Conservative reservations for loader-controlled data and collection
    // graphs. These are not measurements of all CLR allocations or process RSS.
    public long MaxManagedBytes { get; init; } = 4454350848;
    public long MaxRetainedBytes { get; init; } = 370147328;
    public long MaxBuildPhaseBytes { get; init; } = 1406140416;
    public long MaxFrameOperationBytes { get; init; } = 2097152;
    // Requested wrapped-stream bytes, including legal short reads and terminal
    // failed reads; not disk I/O or a completion guarantee for unlimited retries.
    public long MaxReadBytes { get; init; } = 193343722807071426;
    public long MaxFrameRequests { get; init; } = 4890;
    public long MaxFrameStoredBytes { get; init; } = 329489402;
    public long MaxFrameDecodedBytes { get; init; } = 449899684;
    public long MaxFrameRgbaBytes { get; init; } = 81853096;
    public long MaxFrameEncodedBytes { get; init; } = 9383648;
    public int MaxSurvey { get; init; } = 621;
    public int MaxPresentSectors { get; init; } = 20;
    public int MaxAggregateRecords { get; init; } = 7399;
    public int MaxAggregateBytes { get; init; } = 1823280;
    public int MaxPrototypes { get; init; } = 56;
    public int MaxObjects { get; init; } = 524;
    public int MaxVisuals { get; init; } = 2444;
    internal void Validate()
    {
        if (MaxManagedBytes <= 0 || MaxRetainedBytes <= 0 || MaxBuildPhaseBytes <= 0 || MaxFrameOperationBytes <= 0 || MaxReadBytes <= 0 ||
            MaxFrameRequests <= 0 || MaxFrameStoredBytes <= 0 || MaxFrameDecodedBytes <= 0 || MaxFrameRgbaBytes <= 0 || MaxFrameEncodedBytes <= 0 ||
            MaxSurvey <= 0 || MaxPresentSectors <= 0 || MaxAggregateRecords <= 0 || MaxAggregateBytes < 16 || MaxPrototypes <= 0 || MaxObjects <= 0 || MaxVisuals <= 0)
            throw new ArgumentOutOfRangeException(nameof(AreaLoaderOptions));
    }
}

using System.Collections.ObjectModel;

namespace Arcanum.Data.Area;

/// <summary>Input roles. <see cref="Archive"/> (any other resource archive of the content stack, numbered by
/// its search-order position) and <see cref="Loose"/> (one loose resource file) are read only by plans with a
/// <see cref="AreaSourcePlan.ResourceOrder"/> (ARC-DATA-018). <see cref="TerrainTemplate"/> (ARC-DATA-032) is one terrain
/// template file a window plan generates sectors from, numbered within the plan.</summary>
public enum AreaSourceRole { Module, Resource1, Resource2, NormalGameMobile, Prototype, Archive, Loose, MapPatch, TerrainTemplate }
public readonly record struct AreaInputKey(AreaSourceRole Role, int Number = 0);
public sealed record AreaInputIdentity(AreaInputKey Key, long Length, string Sha256);
public enum AreaScope { Core, Context, Outside, Unknown }
public enum AreaSourceKind { Terrain, Static, Mobile }
public enum AreaLayerKind { Body, Foreground, Background, Underlay, Roof }
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
/// <param name="Template">ARC-DATA-032: the <see cref="AreaSourceRole.TerrainTemplate"/> input a generated sector was read
/// from (its objects placed and its lights moved into this sector); null for a sector read from the module archive.</param>
public sealed record AreaSectorRecord(AreaSectorKey Key, AreaSectorCoordinate Coordinate, AreaScope Scope, bool Present, int? DatOrdinal, int? Townmap, int LightCount, int RoofDiscriminator, int Placeholder, int TerminalCount, IReadOnlyList<AreaByteRange> Sections, IReadOnlyList<IReadOnlyList<int>> Lights, AreaEntryIdentity? Entry, string? PayloadSha256,
    AreaInputKey? Template = null);
public sealed record AreaTerrainRecord(AreaVisualKey Key, int LocalX, int LocalY, long WorldX, long WorldY, uint RawArt, uint LookupArt, AreaScope Scope, AreaVisualKey SelectedVisual);
public sealed record AreaObjectRecord(AreaObjectKey Key, int Version, int Type, int PrototypeNumber, string PrototypeReferenceSha256, string PrototypeOwnSha256, string RawSha256, string SemanticSha256, AreaScope Scope, long? SerializedLocation, long? WorldX, long? WorldY, AreaDisposition Disposition, IReadOnlyList<AreaFieldValue> DirectFields, IReadOnlyList<AreaFieldValue> EffectiveFields, bool SchemaReady, bool PlacementReady, bool RequiredLayersReady,
    Maps.MapPatchApplication? MapPatch = null);
public sealed record AreaVisualRecord(AreaVisualKey Key, uint RawArt, uint LookupArt, AreaEntryIdentity Entry, string PayloadSha256, int ViewLength, int NominalFacing, int SourceGroup, int PhysicalGroup, int Frame, int RequestedPalette, int EffectivePalette, bool Mirrored, int Width, int Height, int HotX, int HotY, int ArtOffsetX, int ArtOffsetY, int ObjectOffsetX, int ObjectOffsetY, int Scale, int EncodedBytes, int RgbaBytes, string RgbaSha256, string Policy);
/// <summary>ARC-RENDER-021: one roof block of a whole map: the sector's slot (block <c>x/4 + 16·(y/4)</c> of its local
/// tiles), the block's first tile, the roof art as the sector stores it, its piece (0–12; 9–12 are pieces 0–3 drawn
/// mirrored with palette 1), the fill and fade bits at T0, and the visual of its frame.</summary>
public sealed record AreaRoofRecord(AreaSectorKey Sector, int Slot, long BlockX, long BlockY, uint RawArt, int Piece, bool Fill, bool Fade, AreaVisualKey Visual);
/// <summary>The three <c>art/roof/roofshade.mes</c> values a faded roof's corners take (entries 0, 1 and 2, read like C
/// <c>atoi</c> into a byte).</summary>
public sealed record AreaRoofShades(byte Opaque, byte Partial, byte Transparent);
public sealed record AreaEndpoint(long Sector, int Ordinal, int Prototype, int Sap, int Script, int CatalogueDestination, int RuntimeDestination, long ArrivalX, long ArrivalY, string SourceObjectIdSha256);
public sealed record AreaReconciliation(int Survey, int CoreSectors, int ContextPresent, int ContextMissing, int CoreTerrain, int ContextTerrain, int Statics, int Aggregate, int CoreMobiles, int ContextMobiles, int OutsideMobiles, int UnknownMobiles, int EditorCorrespondence, int Prototypes, int Visuals);
public sealed record AreaBudgetUsage(long ReadReserved, long ReadActual, long ManagedReserved, long PhaseReserved, long RetainedReserved, long FrameRequests, long FrameStored, long FrameDecoded, long FrameRgba, long FrameEncoded, long ConsumerCopyBytes);

public sealed class AreaSnapshot
{
    internal AreaSnapshot(object owner, int generation, AreaSourcePlan plan, IEnumerable<AreaSectorRecord> sectors, IEnumerable<AreaTerrainRecord> terrain,
        IEnumerable<AreaObjectRecord> objects, IEnumerable<AreaVisualRecord> visuals, AreaReconciliation counts, IEnumerable<AreaEndpoint> endpoints,
        IEnumerable<AreaRoofRecord>? roofs = null, AreaRoofShades? roofShades = null)
    {
        Roofs = Array.AsReadOnly((roofs ?? []).ToArray()); RoofShades = roofShades;
        Owner = owner; Generation = generation; CatalogueMap = plan.CatalogueMap; RuntimeMap = plan.RuntimeMap; Profile = plan.Profile; MapPatch = plan.MapPatch; Window = plan.Window;
        Inputs = Array.AsReadOnly(plan.Identities.ToArray());
        Sectors = Array.AsReadOnly(sectors.ToArray()); Terrain = Array.AsReadOnly(terrain.ToArray()); Objects = Array.AsReadOnly(objects.ToArray());
        Visuals = Array.AsReadOnly(visuals.ToArray()); Reconciliation = counts; Endpoints = Array.AsReadOnly(endpoints.ToArray());
    }
    internal object Owner { get; }
    internal int Generation { get; }
    /// <summary>Set once the owner publishes this snapshot as strict-complete; an inspected snapshot with issues never is.</summary>
    internal bool StrictlyPublished { get; private set; }
    internal void MarkStrictlyPublished() => StrictlyPublished = true;
    public int CatalogueMap { get; }
    public int RuntimeMap { get; }
    public string Profile { get; }
    /// <summary>ARC-DATA-028: the map patch the owner applied to the mobile objects, or null.</summary>
    public AreaMapPatchBinding? MapPatch { get; }
    /// <summary>ARC-DATA-034: the window of a window plan (its sectors and their template paths), else null.</summary>
    public AreaWindow? Window { get; }
    public IReadOnlyList<AreaInputIdentity> Inputs { get; }
    public IReadOnlyList<AreaSectorRecord> Sectors { get; }
    public IReadOnlyList<AreaTerrainRecord> Terrain { get; }
    public IReadOnlyList<AreaObjectRecord> Objects { get; }
    public IReadOnlyList<AreaVisualRecord> Visuals { get; }
    public IReadOnlyList<AreaEndpoint> Endpoints { get; }
    /// <summary>ARC-RENDER-021: the roof blocks of a whole map's core sectors (none for a pinned profile).</summary>
    public IReadOnlyList<AreaRoofRecord> Roofs { get; }
    /// <summary>The roof shade values, when the plan binds <c>art/roof/roofshade.mes</c> and the map has roofs.</summary>
    public AreaRoofShades? RoofShades { get; }
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
/// leaveOpen is honored on success AND failed Open. Streams may not be used externally while leased to an owner.
/// The owner may read a large input's stream from a worker thread during a parallel authentication pass
/// while it holds its own lock, so a custom stream must be safe to read from another thread and must not
/// call back into the owner while it is read.</summary>
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
public enum AreaMappingRole { Tile, Facade, Scenery, Container, ItemGround, MonsterNames, UniqueNpcNames, MissingPolicy, WallStructure, WallName, Portal, Roof, RoofShade }
public sealed record AreaMappingBinding(AreaMappingRole Role, AreaEntryIdentity Entry, string PayloadSha256);
public sealed record AreaArtBinding(uint RawArt, AreaEntryIdentity Entry);
/// <summary>One resource source in content-stack search order (ARC-DATA-018): an archive input
/// (<see cref="AreaSourceRole.Module"/>, <see cref="AreaSourceRole.Resource1"/>,
/// <see cref="AreaSourceRole.Resource2"/> or <see cref="AreaSourceRole.Archive"/>, no path) or one loose
/// file (<see cref="AreaSourceRole.Loose"/>) with the normalized virtual path it holds
/// (lower-case printable ASCII, <c>/</c> separators).</summary>
public sealed record AreaResourceSource(AreaInputKey Key, string? Path = null);

/// <summary>ARC-DATA-028: how a plan applies <c>Rules\map.patch</c>: the 8-byte header of each script a <c>script</c>
/// attribute names (null when the original cannot load it, so it sets nothing), resolved through the content stack when
/// the plan is made, and whether the file holds lines whose effect is not modelled.
/// The script headers are resolved from the stack when the plan is made and are not authenticated inputs of the owner.</summary>
public sealed record AreaMapPatchBinding
{
    public AreaMapPatchBinding(IReadOnlyDictionary<int, Maps.MapPatchScriptHeader?> scriptHeaders, bool hasUnmodelledLines)
    {
        ArgumentNullException.ThrowIfNull(scriptHeaders);
        ScriptHeaders = new System.Collections.ObjectModel.ReadOnlyDictionary<int, Maps.MapPatchScriptHeader?>(new Dictionary<int, Maps.MapPatchScriptHeader?>(scriptHeaders));
        HasUnmodelledLines = hasUnmodelledLines;
    }
    public IReadOnlyDictionary<int, Maps.MapPatchScriptHeader?> ScriptHeaders { get; }
    public bool HasUnmodelledLines { get; }
}

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
    public string Profile => OriginalMap switch
    {
        OriginalAreaMap.CrashSiteExterior => "arc-data-009-complete-file-effective-source-v1",
        OriginalAreaMap.CrashSiteCave => "arc-world-009-cave-file-effective-source-v1",
        _ when Selection == AreaSectorSelection.Window && AllowArtTrailingBytes => "arc-data-032-window-original-rules-v1",
        _ when Selection == AreaSectorSelection.Window => "arc-data-032-window-v1",
        _ when Selection == AreaSectorSelection.AllSectors && AllowArtTrailingBytes => "arc-data-019-whole-map-original-rules-v1",
        _ when Selection == AreaSectorSelection.AllSectors => "arc-data-019-whole-map-v1",
        _ => "structural-file-effective-v1",
    };
    /// <summary>How core sectors are chosen: the start sector's four-neighbour component with the
    /// plan's townmap (the exterior rule), every sector file under the prefix (a whole map,
    /// ARC-DATA-019), or the plan's <see cref="Window"/> (ARC-DATA-032).</summary>
    public AreaSectorSelection Selection { get; init; } = AreaSectorSelection.TownmapComponent;
    /// <summary>Read ART as the original does: accept bytes after the last frame
    /// (<see cref="ArtDecoderOptions.AllowTrailingBytes"/>) and draw a single-rotation ART from
    /// its only group whatever the requested rotation. Pinned profiles keep their exact
    /// per-object exceptions instead and ignore this flag.</summary>
    public bool AllowArtTrailingBytes { get; init; }
    /// <summary>ARC-DATA-018: the resource sources in content-stack search order, first wins. When set, an ART
    /// or name a plan resolves itself is read from the first source that holds it as a file (archive directory
    /// and ignored records hold nothing), as the original's file system opens it; empty keeps each pinned
    /// profile's own archive rule. Every <see cref="AreaSourceRole.Archive"/> and <see cref="AreaSourceRole.Loose"/>
    /// input must be listed; pinned profiles take none.</summary>
    public IReadOnlyList<AreaResourceSource> ResourceOrder
    {
        get => _resourceOrder;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            var order = Array.AsReadOnly(value.ToArray());
            if (order.Any(s => s is null) || order.Select(s => s.Key).Distinct().Count() != order.Count ||
                order.Any(s => !Identities.Any(i => i.Key == s.Key) || (s.Key.Role == AreaSourceRole.Loose ? !LoosePath(s.Path) :
                    s.Key.Role is not (AreaSourceRole.Module or AreaSourceRole.Resource1 or AreaSourceRole.Resource2 or AreaSourceRole.Archive) || s.Path is not null)) ||
                order.Where(s => s.Path is not null).Select(s => s.Path).Distinct(StringComparer.Ordinal).Count() != order.Count(s => s.Path is not null))
                throw new ArgumentException("Resource sources must be distinct plan inputs of a resource role; a loose source names one normalized path.", nameof(value));
            _resourceOrder = order;
        }
    }
    private readonly IReadOnlyList<AreaResourceSource> _resourceOrder = Array.Empty<AreaResourceSource>();
    /// <summary>ARC-DATA-028: the map patch the original applies when this map opens (the
    /// <see cref="AreaSourceRole.MapPatch"/> input), or null when the plan applies none. Pinned profiles take none.</summary>
    public AreaMapPatchBinding? MapPatch { get; init; }
    /// <summary>ARC-DATA-032: the sectors of a <see cref="AreaSectorSelection.Window"/> plan, required exactly for that selection.</summary>
    public AreaWindow? Window { get; init; }
    /// <summary>A plan made from the universal catalogue (a whole map or a window): its sectors, ART and objects follow the
    /// whole-map rules, not a pinned profile's exceptions.</summary>
    internal bool UniversalRules => !Original && Selection is AreaSectorSelection.AllSectors or AreaSectorSelection.Window;
    internal static bool LoosePath(string? path) =>
        !string.IsNullOrEmpty(path) && path.All(c => c is >= ' ' and <= '~' and not '\\' and not (>= 'A' and <= 'Z')) &&
        path.Split('/').All(segment => segment.Length > 0 && segment != "." && segment != "..");
    /// <summary>The pinned original profile that authored this plan; None for caller-authored structural plans.</summary>
    internal OriginalAreaMap OriginalMap { get; init; }
    internal bool Original => OriginalMap != OriginalAreaMap.None;
    internal static bool Digest(string value) => value is not null && value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

internal enum OriginalAreaMap { None, CrashSiteExterior, CrashSiteCave }
public enum AreaSectorSelection { TownmapComponent, AllSectors, Window }

/// <summary>ARC-DATA-032: one sector of a window: a core sector (loaded whole) or a context sector (its objects only as
/// context), read from the module archive (<see cref="Template"/> null) or generated from a terrain template input.
/// ARC-DATA-034: <see cref="TemplatePath"/> is that template's lower-case virtual path (null exactly when there is no
/// template), which step evidence reads the template again from.</summary>
public sealed record AreaWindowSector(AreaSectorCoordinate Coordinate, AreaScope Scope, AreaInputKey? Template, string? TemplatePath = null);

/// <summary>ARC-DATA-032: the sectors a window plan loads, around its <see cref="AreaSourcePlan.Start"/>.</summary>
public sealed record AreaWindow(IReadOnlyList<AreaWindowSector> Sectors);

public sealed class AreaLoaderOptions
{
    /// <summary>The pinned Crash Site exterior's own totals (its visual chains and their RGBA bytes): the defaults of
    /// <see cref="MaxVisuals"/> and <see cref="MaxFrameRgbaBytes"/>, and the pinned profiles' viewer repository caps
    /// (ARC-DATA-029). A whole-map plan sets its own visual count and scales its frame budgets.</summary>
    public const int PinnedVisuals = 2444;
    public const long PinnedFrameRgbaBytes = 81853096;
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
    public long MaxFrameRgbaBytes { get; init; } = PinnedFrameRgbaBytes;
    public long MaxFrameEncodedBytes { get; init; } = 9383648;
    public int MaxSurvey { get; init; } = 621;
    public int MaxPresentSectors { get; init; } = 20;
    public int MaxAggregateRecords { get; init; } = 7399;
    public int MaxAggregateBytes { get; init; } = 1823280;
    public int MaxPrototypes { get; init; } = 56;
    public int MaxObjects { get; init; } = 524;
    public int MaxVisuals { get; init; } = PinnedVisuals;
    /// <summary>Inputs at least this long are hashed in parallel by whole-set authentication passes
    /// when two or more of them take part (ARC-RENDER-009); 0 parallelizes every such pass.</summary>
    public long ParallelAuthenticationMinBytes { get; init; } = 1L << 20;
    /// <summary>Most <see cref="AreaSourceRole.Archive"/> and <see cref="AreaSourceRole.Loose"/> inputs (ARC-DATA-018);
    /// 0, the default, admits none.</summary>
    public int MaxResourceInputs { get; init; }
    /// <summary>ARC-DATA-032: most <see cref="AreaSourceRole.TerrainTemplate"/> inputs; 0, the default, admits none.</summary>
    public int MaxTerrainTemplates { get; init; }
    internal void Validate()
    {
        if (MaxManagedBytes <= 0 || MaxRetainedBytes <= 0 || MaxBuildPhaseBytes <= 0 || MaxFrameOperationBytes <= 0 || MaxReadBytes <= 0 ||
            MaxFrameRequests <= 0 || MaxFrameStoredBytes <= 0 || MaxFrameDecodedBytes <= 0 || MaxFrameRgbaBytes <= 0 || MaxFrameEncodedBytes <= 0 ||
            MaxSurvey <= 0 || MaxPresentSectors <= 0 || MaxAggregateRecords <= 0 || MaxAggregateBytes < 16 || MaxPrototypes <= 0 || MaxObjects <= 0 || MaxVisuals <= 0 ||
            ParallelAuthenticationMinBytes < 0 || MaxResourceInputs < 0 || MaxTerrainTemplates < 0)
            throw new ArgumentOutOfRangeException(nameof(AreaLoaderOptions));
    }
}

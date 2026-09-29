namespace Arcanum.Data.Terrain;

/// <summary>Evidence coverage for one terrain obstruction component. This is not a traversal result.</summary>
public enum TerrainObstructionSupportedness { Supported, Unknown, Unsupported }
public enum TerrainObstructionApplicability { Applicable, NotApplicable, Unknown }
public enum TerrainObstructionDisposition { Blocked, NoBlock }
public enum TileNameRuleMode { Apply, SuppressedByCaller }
public enum TerrainObstructionProvenanceKind { SerializedSector, PackedTerrainId, TileNameMes, CallerPolicy, ProjectPolicy }

/// <summary>Safe source identity; it intentionally contains no filesystem path or decoded payload text.</summary>
public sealed record TerrainObstructionProvenance(TerrainObstructionProvenanceKind Kind, string SourceId, int? EntryOrdinal = null);

/// <summary>
/// A result for exactly one source component. A null disposition is required unless coverage is
/// Supported and the component is Applicable; NoBlock never means that an actor can traverse.
/// </summary>
public sealed class TerrainObstructionComponentResult
{
    internal TerrainObstructionComponentResult(TerrainObstructionSupportedness supportedness, TerrainObstructionApplicability applicability,
        TerrainObstructionDisposition? disposition, string reasonCode, TerrainObstructionProvenance provenance)
    {
        if (string.IsNullOrWhiteSpace(reasonCode) || reasonCode.Any(c => c is < 'A' or > 'Z' && c != '_' && c is not >= '0' and <= '9'))
            throw new ArgumentException("Reason code must be a non-empty uppercase ASCII token.", nameof(reasonCode));
        ArgumentNullException.ThrowIfNull(provenance);
        if ((supportedness == TerrainObstructionSupportedness.Supported && applicability == TerrainObstructionApplicability.Applicable) != disposition.HasValue)
            throw new ArgumentException("Only supported, applicable components may carry a disposition.", nameof(disposition));
        Supportedness = supportedness;
        Applicability = applicability;
        Disposition = disposition;
        ReasonCode = reasonCode;
        Provenance = provenance;
    }

    public TerrainObstructionSupportedness Supportedness { get; }
    public TerrainObstructionApplicability Applicability { get; }
    public TerrainObstructionDisposition? Disposition { get; }
    public string ReasonCode { get; }
    public TerrainObstructionProvenance Provenance { get; }
}

/// <summary>Caller-owned, explicit source for the sole admitted tile-name MES lookup.</summary>
public sealed record TerrainTileNameSource
{
    public TerrainTileNameSource(DatArchive archive, string sourceId, string mesEntryPath, TerrainPathProfile pathProfile)
    {
        Archive = archive ?? throw new ArgumentNullException(nameof(archive));
        TerrainSectorSelector.ValidateAscii(sourceId, nameof(sourceId));
        if (sourceId.Contains('/') || sourceId.Contains('\\'))
            throw new ArgumentException("Source ID must be a non-path ASCII label.", nameof(sourceId));
        TerrainSectorSelector.ValidateAscii(mesEntryPath, nameof(mesEntryPath));
        if (pathProfile != TerrainPathProfile.CeNonWindowsMacOs)
            throw new TerrainUnsupportedException(TerrainErrorCode.UnsupportedProfile, "Only the CE non-Windows/macOS path profile is supported.");
        SourceId = sourceId;
        MesEntryPath = mesEntryPath;
        PathProfile = pathProfile;
    }

    public DatArchive Archive { get; }
    public string SourceId { get; }
    public string MesEntryPath { get; }
    public TerrainPathProfile PathProfile { get; }
}

public sealed class TerrainCellObstructionComponents
{
    internal TerrainCellObstructionComponents(int ordinal, int localX, int localY, int worldX, int worldY,
        TerrainObstructionComponentResult serializedBlockMask, TerrainObstructionComponentResult facadeWalkabilityBit,
        TerrainObstructionComponentResult tileNameRule)
    {
        Ordinal = ordinal; LocalX = localX; LocalY = localY; WorldX = worldX; WorldY = worldY;
        SerializedBlockMask = serializedBlockMask; FacadeWalkabilityBit = facadeWalkabilityBit; TileNameRule = tileNameRule;
    }

    public int Ordinal { get; }
    public int LocalX { get; }
    public int LocalY { get; }
    public int WorldX { get; }
    public int WorldY { get; }
    public TerrainObstructionComponentResult SerializedBlockMask { get; }
    public TerrainObstructionComponentResult FacadeWalkabilityBit { get; }
    public TerrainObstructionComponentResult TileNameRule { get; }
}

/// <summary>Immutable 64 by 64 component report. It deliberately has no combined or traversal verdict.</summary>
public sealed class TerrainObstructionSectorReport
{
    private readonly uint[] _tileWords;
    private readonly uint[]? _blockMaskWords;
    private readonly TerrainTileNameRuleTable? _tileNameRules;
    private readonly TerrainObstructionComponentResult _tileSourceFailure;
    private readonly TerrainObstructionProvenance _moduleProvenance;
    private readonly TerrainSectorSelector _selector;

    internal TerrainObstructionSectorReport(TerrainSectorSelector selector, uint[] tileWords, uint[]? blockMaskWords,
        TerrainTileNameRuleTable? tileNameRules, TerrainObstructionComponentResult tileSourceFailure,
        TerrainObstructionProvenance moduleProvenance)
    {
        _selector = selector;
        _tileWords = tileWords.ToArray();
        _blockMaskWords = blockMaskWords?.ToArray();
        _tileNameRules = tileNameRules;
        _tileSourceFailure = tileSourceFailure;
        _moduleProvenance = moduleProvenance;
    }

    public TerrainSectorSelector Selector => _selector;
    public TerrainCellObstructionComponents GetCell(int localX, int localY, TileNameRuleMode tileNameRuleMode = TileNameRuleMode.Apply)
    {
        if ((uint)localX >= 64 || (uint)localY >= 64)
            throw new ArgumentOutOfRangeException(nameof(localX), "Local terrain coordinates must be in the inclusive range 0 through 63.");
        var ordinal = checked(localX + (64 * localY));
        var mask = _blockMaskWords is null
            ? Unknown("BLOCK_MASK_ABSENT", _moduleProvenance)
            : Applicable(((_blockMaskWords[ordinal / 32] & (1u << (ordinal % 32))) != 0) ? TerrainObstructionDisposition.Blocked : TerrainObstructionDisposition.NoBlock,
                "SERIALIZED_BLOCK_MASK", _moduleProvenance);
        var rawId = _tileWords[ordinal];
        var packedClass = rawId >> 28;
        var facade = packedClass switch
        {
            11 => Applicable((rawId & 1) != 0 ? TerrainObstructionDisposition.NoBlock : TerrainObstructionDisposition.Blocked, "FACADE_WALKABILITY_BIT", new(TerrainObstructionProvenanceKind.PackedTerrainId, _moduleProvenance.SourceId)),
            0 => NotApplicable("FACADE_NOT_APPLICABLE", new(TerrainObstructionProvenanceKind.PackedTerrainId, _moduleProvenance.SourceId)),
            _ => Unsupported("FACADE_CLASS_UNSUPPORTED", new(TerrainObstructionProvenanceKind.PackedTerrainId, _moduleProvenance.SourceId)),
        };
        var tile = TileName(rawId, packedClass, tileNameRuleMode);
        return new TerrainCellObstructionComponents(ordinal, localX, localY, checked(_selector.OriginX + localX), checked(_selector.OriginY + localY), mask, facade, tile);
    }

    private TerrainObstructionComponentResult TileName(uint rawId, uint packedClass, TileNameRuleMode mode)
    {
        var provenance = new TerrainObstructionProvenance(TerrainObstructionProvenanceKind.TileNameMes, _tileSourceFailure.Provenance.SourceId, _tileSourceFailure.Provenance.EntryOrdinal);
        if (packedClass == 0 && mode == TileNameRuleMode.SuppressedByCaller)
            return NotApplicable("TILE_NAME_SUPPRESSED_BY_CALLER", new(TerrainObstructionProvenanceKind.CallerPolicy, provenance.SourceId, provenance.EntryOrdinal));
        if (packedClass == 11) return NotApplicable("TILE_NAME_NOT_APPLICABLE", provenance);
        if (packedClass != 0) return Unsupported("TILE_NAME_CLASS_UNSUPPORTED", provenance);
        if (_tileNameRules is null) return _tileSourceFailure;
        return _tileNameRules.Evaluate(rawId);
    }

    internal static TerrainObstructionComponentResult Applicable(TerrainObstructionDisposition value, string reason, TerrainObstructionProvenance provenance) =>
        new(TerrainObstructionSupportedness.Supported, TerrainObstructionApplicability.Applicable, value, reason, provenance);
    internal static TerrainObstructionComponentResult NotApplicable(string reason, TerrainObstructionProvenance provenance) =>
        new(TerrainObstructionSupportedness.Supported, TerrainObstructionApplicability.NotApplicable, null, reason, provenance);
    internal static TerrainObstructionComponentResult Unknown(string reason, TerrainObstructionProvenance provenance) =>
        new(TerrainObstructionSupportedness.Unknown, TerrainObstructionApplicability.Unknown, null, reason, provenance);
    internal static TerrainObstructionComponentResult Unsupported(string reason, TerrainObstructionProvenance provenance) =>
        new(TerrainObstructionSupportedness.Unsupported, TerrainObstructionApplicability.Unknown, null, reason, provenance);
}

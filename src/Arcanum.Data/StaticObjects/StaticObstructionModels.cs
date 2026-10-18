using Arcanum.Data.Terrain;

namespace Arcanum.Data.StaticObjects;

public enum StaticObstructionSupportedness { Supported, Unknown, Unsupported }
public enum StaticObstructionApplicability { Applicable, NotApplicable, Unknown }
public enum StaticObstructionDisposition { Blocker, NoBlock }
public enum StaticObstructionFlagsSource { Unresolved, Direct, Prototype }
public enum StaticObstructionLocationStatus { InRange, Absent, OutOfRange, Overflow, OutsideTypeScope }
public enum StaticObstructionProvenanceKind { SerializedInstance, PrototypeDefault, PinnedSpecification, ProjectPolicy, OriginalDataIdentity }

/// <summary>Only NormalCardinalZeroFlags admits non-editor ordinary-ground-critter traversal.</summary>
public enum StaticObstructionContext
{
    NormalCardinalZeroFlags, Editor, Projectile, Sound, SkipObjects, IgnoreCritters,
    Wall, Portal, Window, Critter, Trap, Spell, Combat, MutableState, NonGroundActor, OtherTraversalFlags
}

/// <summary>Identifier-only provenance, never a path, asset name or payload.</summary>
public sealed record StaticObstructionProvenance(StaticObstructionProvenanceKind Kind, string SourceId,
    int? EntryOrdinal = null, int? SourceIndex = null, int? PrototypeAid = null);
public readonly record struct StaticObstructionLocalCell(int X, int Y);

/// <summary>A component result, not a combined passability or step verdict.</summary>
public sealed class StaticObstructionResult
{
    internal StaticObstructionResult(StaticObstructionSupportedness supportedness, StaticObstructionApplicability applicability,
        StaticObstructionDisposition? disposition, string reasonCode, IEnumerable<StaticObstructionProvenance> provenance)
    {
        if ((supportedness == StaticObstructionSupportedness.Supported && applicability == StaticObstructionApplicability.Applicable) != disposition.HasValue)
            throw new ArgumentException("Only supported applicable results carry a disposition.", nameof(disposition));
        if (string.IsNullOrEmpty(reasonCode) || reasonCode.Any(c => c is not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') and not '_'))
            throw new ArgumentException("Reason must be an uppercase ASCII token.", nameof(reasonCode));
        Supportedness = supportedness; Applicability = applicability; Disposition = disposition; ReasonCode = reasonCode;
        Provenance = Array.AsReadOnly(provenance.ToArray());
    }
    public StaticObstructionSupportedness Supportedness { get; }
    public StaticObstructionApplicability Applicability { get; }
    public StaticObstructionDisposition? Disposition { get; }
    public string ReasonCode { get; }
    public IReadOnlyList<StaticObstructionProvenance> Provenance { get; }
}

public sealed class StaticObstructionRecordAssessment
{
    internal StaticObstructionRecordAssessment(int sourceIndex, int objectType, int? prototypeAid, uint? effectiveFlags,
        StaticObstructionFlagsSource flagsSource, StaticObstructionLocationStatus locationStatus,
        StaticObstructionLocalCell? destination, StaticObstructionResult result)
    {
        SourceIndex = sourceIndex; ObjectType = objectType; PrototypeAid = prototypeAid; EffectiveFlags = effectiveFlags;
        FlagsSource = flagsSource; LocationStatus = locationStatus; Destination = destination; Result = result;
    }
    public int SourceIndex { get; }
    public int ObjectType { get; }
    public int? PrototypeAid { get; }
    public uint? EffectiveFlags { get; }
    public StaticObstructionFlagsSource FlagsSource { get; }
    public StaticObstructionLocationStatus LocationStatus { get; }
    public StaticObstructionLocalCell? Destination { get; }
    public StaticObstructionResult Result { get; }
}

public sealed class StaticObstructionCellAssessment
{
    internal StaticObstructionCellAssessment(int localX, int localY, IReadOnlyList<StaticObstructionRecordAssessment> contributors,
        bool locationCoverageComplete, StaticObstructionResult result)
    {
        LocalX = localX; LocalY = localY; Contributors = contributors;
        LocationCoverageComplete = locationCoverageComplete; Result = result;
    }
    public int LocalX { get; }
    public int LocalY { get; }
    /// <summary>Applicable and unresolved subset candidates at this exact destination, in source order.</summary>
    public IReadOnlyList<StaticObstructionRecordAssessment> Contributors { get; }
    /// <summary>False when an unresolved candidate cannot be placed; even a proven Blocker retains this warning.</summary>
    public bool LocationCoverageComplete { get; }
    public StaticObstructionResult Result { get; }
}

/// <summary>Owned path-free identity evidence from the existing authenticated original-data loader.</summary>
public sealed class StaticObstructionInputIdentity
{
    internal StaticObstructionInputIdentity(StaticObjectValidation validation)
    {
        Module = validation.Module; Resource = validation.Resource;
        Prototypes = Array.AsReadOnly(validation.PrototypeFingerprints.ToArray());
        PrototypeAggregateSha256 = validation.PrototypeAggregateSha256;
    }
    public StaticObjectInputFingerprint Module { get; }
    public StaticObjectInputFingerprint Resource { get; }
    public IReadOnlyList<StaticObjectPrototypeFingerprint> Prototypes { get; }
    public string PrototypeAggregateSha256 { get; }
}

/// <summary>Owned immutable base-sector component snapshot; no archives, paths or mutable inputs are retained.</summary>
public sealed class StaticObstructionSectorReport
{
    private readonly IReadOnlyList<StaticObstructionRecordAssessment>[] _cells;
    private readonly IReadOnlyList<StaticObstructionProvenance> _provenance;

    internal StaticObstructionSectorReport(TerrainObstructionSectorIdentity sector, StaticObstructionRecordAssessment[] records,
        IReadOnlyList<StaticObstructionProvenance> provenance, StaticObstructionInputIdentity? inputIdentity)
    {
        Sector = sector; Records = Array.AsReadOnly(records.ToArray()); InputIdentity = inputIdentity;
        _provenance = Array.AsReadOnly(provenance.ToArray());
        UnknownLocationRecords = Array.AsReadOnly(Records.Where(record => record.Destination is null &&
            record.Result.Supportedness == StaticObstructionSupportedness.Unknown).ToArray());
        // Index once: bounded by the parser's terminal count plus exactly 4096 cell lists.
        var lists = new List<StaticObstructionRecordAssessment>[4096];
        foreach (var record in Records)
        {
            if (record.Destination is not StaticObstructionLocalCell cell ||
                record.Result.Supportedness == StaticObstructionSupportedness.Unsupported ||
                record.Result.Applicability == StaticObstructionApplicability.NotApplicable) continue;
            var ordinal = cell.X + 64 * cell.Y;
            (lists[ordinal] ??= []).Add(record);
        }
        _cells = lists.Select(list => (IReadOnlyList<StaticObstructionRecordAssessment>)Array.AsReadOnly(list?.ToArray() ?? [])).ToArray();
    }
    public TerrainObstructionSectorIdentity Sector { get; }
    public IReadOnlyList<StaticObstructionRecordAssessment> Records { get; }
    public IReadOnlyList<StaticObstructionRecordAssessment> UnknownLocationRecords { get; }
    public bool LocationCoverageComplete => UnknownLocationRecords.Count == 0;
    /// <summary>Null for caller-owned structural inspection; present only for authenticated original validation.</summary>
    public StaticObstructionInputIdentity? InputIdentity { get; }

    public StaticObstructionCellAssessment GetCell(int localX, int localY,
        StaticObstructionContext context = StaticObstructionContext.NormalCardinalZeroFlags)
    {
        if (!InBounds(localX, localY)) return ExcludedCell(localX, localY, "CROSS_SECTOR_UNSUPPORTED");
        if (context != StaticObstructionContext.NormalCardinalZeroFlags)
            return ExcludedCell(localX, localY, "CONTEXT_UNSUPPORTED");
        var contributors = _cells[localX + 64 * localY];
        var result = contributors.Any(record => record.Result.Disposition == StaticObstructionDisposition.Blocker)
            ? Applicable(StaticObstructionDisposition.Blocker, "DESTINATION_BLOCKER", _provenance)
            : contributors.Any(record => record.Result.Supportedness == StaticObstructionSupportedness.Unknown)
                ? Unknown("DESTINATION_UNRESOLVED", _provenance)
                : !LocationCoverageComplete ? Unknown("LOCATION_COVERAGE_UNCERTAIN", _provenance)
                : Applicable(StaticObstructionDisposition.NoBlock, "ADMITTED_SUBSET_NO_BLOCK", _provenance);
        return new(localX, localY, contributors, LocationCoverageComplete, result);
    }

    /// <summary>Checks the request boundary, then queries only its destination. It does not assess the step.</summary>
    public StaticObstructionCellAssessment GetDestinationForStep(int fromX, int fromY, int toX, int toY,
        StaticObstructionContext context = StaticObstructionContext.NormalCardinalZeroFlags)
    {
        if (!InBounds(fromX, fromY) || !InBounds(toX, toY)) return ExcludedCell(toX, toY, "CROSS_SECTOR_UNSUPPORTED");
        if (context != StaticObstructionContext.NormalCardinalZeroFlags) return ExcludedCell(toX, toY, "CONTEXT_UNSUPPORTED");
        var dx = Math.Abs(toX - fromX); var dy = Math.Abs(toY - fromY);
        if (dx == 0 && dy == 0) return ExcludedCell(toX, toY, "SAME_CELL_UNSUPPORTED");
        if (dx > 1 || dy > 1) return ExcludedCell(toX, toY, "NONADJACENT_UNSUPPORTED");
        if (dx == 1 && dy == 1) return ExcludedCell(toX, toY, "DIAGONAL_UNSUPPORTED");
        return GetCell(toX, toY, context);
    }

    private StaticObstructionCellAssessment ExcludedCell(int x, int y, string reason) =>
        new(x, y, Array.Empty<StaticObstructionRecordAssessment>(), LocationCoverageComplete, Unsupported(reason, _provenance));
    private static bool InBounds(int x, int y) => (uint)x < 64 && (uint)y < 64;
    internal static StaticObstructionResult Applicable(StaticObstructionDisposition value, string reason, IEnumerable<StaticObstructionProvenance> provenance) =>
        new(StaticObstructionSupportedness.Supported, StaticObstructionApplicability.Applicable, value, reason, provenance);
    internal static StaticObstructionResult NotApplicable(string reason, IEnumerable<StaticObstructionProvenance> provenance) =>
        new(StaticObstructionSupportedness.Supported, StaticObstructionApplicability.NotApplicable, null, reason, provenance);
    internal static StaticObstructionResult Unknown(string reason, IEnumerable<StaticObstructionProvenance> provenance) =>
        new(StaticObstructionSupportedness.Unknown, StaticObstructionApplicability.Unknown, null, reason, provenance);
    internal static StaticObstructionResult Unsupported(string reason, IEnumerable<StaticObstructionProvenance> provenance) =>
        new(StaticObstructionSupportedness.Unsupported, StaticObstructionApplicability.Unknown, null, reason, provenance);
}

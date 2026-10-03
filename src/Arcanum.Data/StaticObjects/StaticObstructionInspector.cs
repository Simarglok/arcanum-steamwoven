using Arcanum.Data.Terrain;

namespace Arcanum.Data.StaticObjects;

/// <summary>
/// Ordinary static scenery only, for the fixed 64x64 base-sector profile. Reuses the
/// existing loader wire/default-resolution seams; does not decode ART or decide passability.
/// </summary>
public static class StaticObstructionInspector
{
    private const int OriginX = 92928;
    private const int OriginY = 82560;
    private const string SpecificationId = "CE-a7ff41b300ef712f0e7d088183a3d08957110cdd";

    /// <summary>
    /// Caller-owned structural inspection. The caller explicitly selects sources and
    /// asserts the fixed sector profile; only ValidateOriginalData authenticates identities.
    /// </summary>
    public static StaticObstructionSectorReport InspectSector(StaticObjectSource source, StaticObjectPrototypeSource prototypes,
        string moduleSourceId, string prototypeSourceId, TerrainSectorSelector selector, StaticObjectLoaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(prototypes); ArgumentNullException.ThrowIfNull(selector);
        ValidateSourceId(moduleSourceId, nameof(moduleSourceId)); ValidateSourceId(prototypeSourceId, nameof(prototypeSourceId));
        if (selector.SectorX != 1452 || selector.SectorY != 1290 || selector.PackedSectorId != 86570436012 ||
            selector.OriginX != OriginX || selector.OriginY != OriginY || source.ModuleEntryPath != selector.ModuleEntryPath)
            throw new StaticObjectException(StaticObjectErrorCode.InvalidSelector, StaticObjectFailureStage.Selector,
                "Only the fixed base-sector numeric profile with one explicit matching selector is supported.");
        var limits = options ?? new StaticObjectLoaderOptions(); limits.Validate();
        // Numeric DAT entry identity uses the existing selected-source primitive.
        DatEntry entry;
        try { entry = TerrainLoader.SelectModule(source.ModuleArchive, selector); }
        catch (TerrainException)
        {
            throw new StaticObjectException(StaticObjectErrorCode.InvalidSelector, StaticObjectFailureStage.Selector,
                "The selected module entry does not match the declared sector identity.");
        }
        var inspection = StaticObjectLoader.InspectForObstruction(source, prototypes, limits);
        return Create(inspection, entry.Ordinal, moduleSourceId, prototypeSourceId, null);
    }

    /// <summary>
    /// Read-only opt-in validation of the original fixed profile, with the existing
    /// loader's pre-read/production-read/post-read module/resource/prototype identity gates.
    /// No census oracle values are used as obstruction rules.
    /// </summary>
    public static StaticObstructionSectorReport ValidateOriginalData(string modulePath, string resourcePath, string prototypeDirectory,
        StaticObjectLoaderOptions? options = null)
    {
        var validation = StaticObjectLoader.ValidateOriginalData(modulePath, resourcePath, prototypeDirectory, options);
        return Create(validation.Inspection, 463, "CRASH_SITE_MODULE", "CRASH_SITE_PROTOTYPES", new StaticObstructionInputIdentity(validation));
    }

    internal static StaticObstructionSectorReport Create(StaticObjectInspection inspection, int ordinal, string moduleId,
        string prototypeId, StaticObstructionInputIdentity? identity)
    {
        var provenance = new List<StaticObstructionProvenance>
        {
            new(StaticObstructionProvenanceKind.SerializedInstance, moduleId, ordinal),
            new(StaticObstructionProvenanceKind.PinnedCeSpecification, SpecificationId)
        };
        if (identity is not null) provenance.Add(new(StaticObstructionProvenanceKind.OriginalDataIdentity, identity.Module.Sha256, ordinal));
        var records = inspection.Records.Select(record => Assess(record, ordinal, moduleId, prototypeId, identity)).ToArray();
        return new(new TerrainObstructionSectorIdentity(ordinal, OriginX, OriginY, 64, 64), records, provenance, identity);
    }

    private static StaticObstructionRecordAssessment Assess(StaticObjectRecord record, int ordinal, string moduleId,
        string prototypeId, StaticObstructionInputIdentity? identity)
    {
        var flags = record.DirectFlags ?? record.EffectiveFlags;
        var flagsSource = record.DirectFlags.HasValue ? StaticObstructionFlagsSource.Direct
            : flags.HasValue && record.FlagsInherited ? StaticObstructionFlagsSource.Prototype : StaticObstructionFlagsSource.Unresolved;
        var provenance = new List<StaticObstructionProvenance>
        {
            new(StaticObstructionProvenanceKind.SerializedInstance, moduleId, ordinal, record.SourceIndex),
            new(StaticObstructionProvenanceKind.PinnedCeSpecification, SpecificationId)
        };
        if (flagsSource == StaticObstructionFlagsSource.Prototype)
            provenance.Add(new(StaticObstructionProvenanceKind.PrototypeDefault, prototypeId, PrototypeAid: record.PrototypeAid));
        if (identity is not null) provenance.Add(new(StaticObstructionProvenanceKind.OriginalDataIdentity, identity.Module.Sha256, ordinal));
        var (locationStatus, destination) = DecodeLocation(record);
        StaticObstructionResult result;
        if (record.ObjectType != 3) result = StaticObstructionSectorReport.Unsupported("TYPE_UNSUPPORTED", provenance);
        else if (flagsSource == StaticObstructionFlagsSource.Unresolved || flags is null)
            result = StaticObstructionSectorReport.Unknown("FLAGS_UNRESOLVED", provenance);
        else if ((flags.Value & 0x2000) != 0) result = StaticObstructionSectorReport.Unsupported("DYNAMIC_EXCLUDED", provenance);
        else if (locationStatus != StaticObstructionLocationStatus.InRange)
            result = StaticObstructionSectorReport.Unknown(locationStatus switch
            {
                StaticObstructionLocationStatus.Absent => "DIRECT_LOCATION_ABSENT",
                StaticObstructionLocationStatus.Overflow => "DIRECT_LOCATION_OVERFLOW",
                _ => "DIRECT_LOCATION_OUT_OF_RANGE"
            }, provenance);
        else if ((flags.Value & 0x1000) != 0) result = StaticObstructionSectorReport.NotApplicable("INVENTORY_EXCLUDED", provenance);
        else if ((flags.Value & 1) != 0) result = StaticObstructionSectorReport.NotApplicable("DESTROYED_EXCLUDED", provenance);
        else if ((flags.Value & 2) != 0) result = StaticObstructionSectorReport.NotApplicable("OFF_EXCLUDED", provenance);
        else result = StaticObstructionSectorReport.Applicable((flags.Value & 0x400) != 0 ? StaticObstructionDisposition.NoBlock : StaticObstructionDisposition.Blocker,
            (flags.Value & 0x400) != 0 ? "OBJECT_NO_BLOCK" : "OBJECT_BLOCKER", provenance);
        return new(record.SourceIndex, record.ObjectType, record.PrototypeAid, flags, flagsSource, locationStatus, destination, result);
    }

    private static (StaticObstructionLocationStatus, StaticObstructionLocalCell?) DecodeLocation(StaticObjectRecord record)
    {
        if (record.ObjectType != 3) return (StaticObstructionLocationStatus.OutsideTypeScope, null);
        if (record.Location is not long location) return (StaticObstructionLocationStatus.Absent, null);
        try
        {
            // Serialized signed 32-bit world components, NOT the loader's masked convenience coordinates.
            var x = checked(unchecked((int)(uint)location) - OriginX);
            var y = checked(unchecked((int)(location >> 32)) - OriginY);
            return (uint)x < 64 && (uint)y < 64
                ? (StaticObstructionLocationStatus.InRange, new StaticObstructionLocalCell(x, y))
                : (StaticObstructionLocationStatus.OutOfRange, null);
        }
        catch (OverflowException) { return (StaticObstructionLocationStatus.Overflow, null); }
    }

    private static void ValidateSourceId(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrEmpty(value, parameter);
        if (value.Length > 128 || value.Any(c => c is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '_' and not '-' and not '.'))
            throw new ArgumentException("Source ID must be a bounded non-path ASCII identifier.", parameter);
    }
}

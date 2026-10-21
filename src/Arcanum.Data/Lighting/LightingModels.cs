using System.Collections.ObjectModel;

namespace Arcanum.Data.Lighting;

/// <summary>One ambient multiplier colour. Channels are source bytes; 255 leaves a pixel unchanged.</summary>
public readonly record struct AmbientColor(byte R, byte G, byte B)
{
    public static AmbientColor Neutral { get; } = new(255, 255, 255);
}

/// <summary>The hourly pair. Source rows list outdoor RGB first, then indoor RGB.</summary>
public readonly record struct AmbientPair(AmbientColor Outdoor, AmbientColor Indoor)
{
    public static AmbientPair Neutral { get; } = new(AmbientColor.Neutral, AmbientColor.Neutral);
}

public enum LightingFailure
{
    None, MissingResource, AmbiguousResource, ReadFailed, MesGrammar, LimitExceeded,
    MissingHour, ChannelCount, ChannelRange, EdgeHourEmpty,
    MapNotCatalogued, MapDefaultMissing, MapDefaultMalformed, UnsafeResourceName,
    SectorIdentityMismatch, SectorMalformed, SectorMetadataAbsent, SectorSchemeNegative,
    SchemeNotCatalogued, StartupMalformed, InvalidHour, SectorNotAdmitted, SnapshotNotStrict, MapDirectoryMismatch,
    // ARC-RENDER-012 stationary sector lights.
    LightRecordMalformed, LightNotStationary, LightOutsideSector, LightFlagsUnsupported, LightArtUnsupported, LightArtNotCatalogued,
}

/// <summary>A complete normalized 24-hour scheme. Empty interior hours are filled from
/// their own bracketing explicit rows; hour 0 and hour 23 must be explicit.</summary>
public sealed class LightSchemeTable
{
    internal LightSchemeTable(int schemeId, string payloadSha256, AmbientPair[] hours, bool[] explicitHours)
    {
        SchemeId = schemeId; PayloadSha256 = payloadSha256;
        Hours = Array.AsReadOnly(hours); ExplicitHours = Array.AsReadOnly(explicitHours);
    }
    public int SchemeId { get; }
    public string PayloadSha256 { get; }
    public IReadOnlyList<AmbientPair> Hours { get; }
    public IReadOnlyList<bool> ExplicitHours { get; }
    public AmbientPair At(int hour) => hour is < 0 or > 23 ? throw new ArgumentOutOfRangeException(nameof(hour)) : Hours[hour];
}

/// <summary>Declared startup year/hour from the module's game-init rules, with its payload digest.
/// It is a clock bootstrap declaration, not a running clock.</summary>
public sealed record StartupClockDeclaration(int Year, int Hour, string PayloadSha256);

/// <summary>ARC-WORLD-057: the module's start clock, or why it cannot be read.</summary>
public sealed record StartupClockRead(LightingFailure Failure, StartupClockDeclaration? Startup);

/// <summary>Detached lighting inputs of one loaded area: map default, the raw serialized scheme
/// word of each admitted sector (0 is the map-default sentinel) and every referenced scheme.</summary>
public sealed class AreaLightingSource
{
    /// <summary>Validated detached construction (also used by synthetic fixtures). Every sector word must be
    /// nonnegative and resolve, directly or through the positive map default, to a supplied scheme table.</summary>
    public AreaLightingSource(string moduleSha256, int catalogueMap, int mapDefaultScheme, string mapInfoSha256,
        IDictionary<long, int> sectorSchemes, IDictionary<int, LightSchemeTable> schemes, StartupClockDeclaration startup)
    {
        ArgumentNullException.ThrowIfNull(sectorSchemes); ArgumentNullException.ThrowIfNull(schemes); ArgumentNullException.ThrowIfNull(startup);
        if (!Digest(moduleSha256) || !Digest(mapInfoSha256) || !Digest(startup.PayloadSha256) || catalogueMap < 0 || mapDefaultScheme <= 0 ||
            startup.Year < 0 || startup.Hour is < 0 or > 23 || sectorSchemes.Count == 0 ||
            schemes.Any(pair => pair.Value is null || pair.Key != pair.Value.SchemeId) ||
            sectorSchemes.Values.Any(raw => raw < 0 || !schemes.ContainsKey(raw == 0 ? mapDefaultScheme : raw)))
            throw new ArgumentException("A complete, consistent lighting source is required.");
        ModuleSha256 = moduleSha256; CatalogueMap = catalogueMap; MapDefaultScheme = mapDefaultScheme; MapInfoSha256 = mapInfoSha256;
        SectorSchemes = new ReadOnlyDictionary<long, int>(new SortedDictionary<long, int>(sectorSchemes));
        Schemes = new ReadOnlyDictionary<int, LightSchemeTable>(new SortedDictionary<int, LightSchemeTable>(schemes));
        Startup = startup;
    }
    public string ModuleSha256 { get; }
    public int CatalogueMap { get; }
    public int MapDefaultScheme { get; }
    public string MapInfoSha256 { get; }
    public IReadOnlyDictionary<long, int> SectorSchemes { get; }
    public IReadOnlyDictionary<int, LightSchemeTable> Schemes { get; }
    public StartupClockDeclaration Startup { get; }
    private static bool Digest(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>FR-LIGHT-007: sentinel 0 selects the map default; any other value is the sector's own
    /// scheme. A sector outside the admitted set is an error, never a guessed default.</summary>
    public LightingFailure TryResolveScheme(long packedSector, out int schemeId, out bool fromMapDefault)
    {
        schemeId = 0; fromMapDefault = false;
        if (!SectorSchemes.TryGetValue(packedSector, out var raw)) return LightingFailure.SectorNotAdmitted;
        fromMapDefault = raw == 0; schemeId = fromMapDefault ? MapDefaultScheme : raw;
        return LightingFailure.None;
    }
}

public sealed record AreaLightingReadResult(LightingFailure Failure, AreaLightingSource? Source);

/// <summary>The ambient pair applied for one active sector at one whole game hour.</summary>
public readonly record struct AmbientSelection(int SchemeId, bool FromMapDefault, long Sector, int Hour, AmbientPair Pair);

/// <summary>FR-LIGHT-005: discrete hourly application. The caller supplies the current whole game
/// hour from its clock and the active sector; the selection (and Revision) changes only when the
/// scheme or the hour changes. No scheduler, no minute blending, no time source of its own.</summary>
public sealed class AmbientLightingState
{
    private readonly AreaLightingSource _source;
    public AmbientLightingState(AreaLightingSource source) { _source = source ?? throw new ArgumentNullException(nameof(source)); }
    public AmbientSelection? Current { get; private set; }
    public long Revision { get; private set; }

    public LightingFailure Apply(int hour, long activeSector, out bool changed)
    {
        changed = false;
        if (hour is < 0 or > 23) return LightingFailure.InvalidHour;
        var failure = _source.TryResolveScheme(activeSector, out var scheme, out var fromDefault);
        if (failure != LightingFailure.None) return failure;
        if (!_source.Schemes.TryGetValue(scheme, out var table)) return LightingFailure.SchemeNotCatalogued;
        var next = new AmbientSelection(scheme, fromDefault, activeSector, hour, table.At(hour));
        changed = Current is not { } current || current.SchemeId != scheme || current.Hour != hour;
        if (changed) Revision = checked(Revision + 1);
        Current = next;
        return LightingFailure.None;
    }
}

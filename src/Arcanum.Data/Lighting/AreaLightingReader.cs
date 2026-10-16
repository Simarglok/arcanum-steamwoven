using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Terrain;

namespace Arcanum.Data.Lighting;

/// <summary>
/// Reads the lighting inputs of one strict area snapshot from its module archive: the map default
/// (maplist -> mapinfo <c>LightScheme</c>), the raw scheme word of every core sector (sector metadata
/// +8, re-read and bound to the snapshot's entry identity and payload digest), every referenced
/// scheme through <c>rules/lighting schemes.mes</c>, and the game-init startup year/hour.
/// The caller supplies the same module archive the snapshot was built from and re-verifies the
/// module file afterwards. Module only: no PATCH/loose override precedence is claimed.
/// </summary>
public static class AreaLightingReader
{
    public const int MaxSectors = 64;
    public const int MaxSchemes = 32;

    public static AreaLightingReadResult Read(DatArchive module, AreaSnapshot snapshot, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(module); ArgumentNullException.ThrowIfNull(snapshot);
        var failure = ReadCore(module, snapshot, token, out var source);
        return new(failure, failure == LightingFailure.None ? source : null);
    }

    private static LightingFailure ReadCore(DatArchive module, AreaSnapshot snapshot, CancellationToken token, out AreaLightingSource? source)
    {
        source = null;
        if (!snapshot.StrictlyPublished) return LightingFailure.SnapshotNotStrict;
        var modules = snapshot.Inputs.Where(input => input.Key.Role == AreaSourceRole.Module).Take(2).ToArray();
        if (modules.Length != 1) return LightingFailure.SectorIdentityMismatch;
        var moduleIdentity = modules[0];
        var index = new Dictionary<string, List<DatEntry>>(StringComparer.Ordinal);
        foreach (var entry in module.Entries)
        {
            var name = Name(entry); if (name is null) continue;
            if (!index.TryGetValue(name, out var list)) index.Add(name, list = new List<DatEntry>(1));
            list.Add(entry);
        }
        LightingFailure Resource(string path, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            if (!index.TryGetValue(path, out var found)) return LightingFailure.MissingResource;
            if (found.Count != 1) return LightingFailure.AmbiguousResource;
            return ReadEntry(module, found[0], out bytes);
        }

        token.ThrowIfCancellationRequested();
        var failure = Resource("rules/gameinit.mes", out var gameInit);
        if (failure != LightingFailure.None) return failure;
        failure = LightingMes.TryParse(gameInit, out var init);
        if (failure != LightingFailure.None) return failure;
        if (!init.TryGetValue(4, out var yearText) || !LightingMes.TryInteger(yearText, out var year) || year < 0 ||
            !init.TryGetValue(5, out var hourText) || !LightingMes.TryInteger(hourText, out var hour) || hour is < 0 or > 23)
            return LightingFailure.StartupMalformed;
        var startup = new StartupClockDeclaration(year, hour, LightingMes.Hash(gameInit));

        failure = Resource("rules/maplist.mes", out var mapListBytes);
        if (failure != LightingFailure.None) return failure;
        failure = LightingMes.TryParse(mapListBytes, out var mapList);
        if (failure != LightingFailure.None) return failure;
        if (!mapList.TryGetValue(snapshot.CatalogueMap, out var mapRow)) return LightingFailure.MapNotCatalogued;
        var directory = SafeName(mapRow.Split(',')[0]);
        if (directory is null) return LightingFailure.UnsafeResourceName;
        failure = Resource("maps/" + directory.ToLowerInvariant() + "/mapinfo.txt", out var mapInfo);
        if (failure != LightingFailure.None) return failure;
        failure = MapDefault(mapInfo, out var mapDefault);
        if (failure != LightingFailure.None) return failure;

        var sectors = new SortedDictionary<long, int>();
        foreach (var sector in snapshot.Sectors.Where(sector => sector.Scope == AreaScope.Core))
        {
            token.ThrowIfCancellationRequested();
            if (sectors.Count == MaxSectors) return LightingFailure.LimitExceeded;
            failure = SectorScheme(module, sector, "maps/" + directory.ToLowerInvariant() + "/", out var raw);
            if (failure != LightingFailure.None) return failure;
            if (!sectors.TryAdd(sector.Key.Packed, raw)) return LightingFailure.SectorIdentityMismatch;
        }
        if (sectors.Count == 0) return LightingFailure.SectorNotAdmitted;

        failure = Resource("rules/lighting schemes.mes", out var catalogueBytes);
        if (failure != LightingFailure.None) return failure;
        failure = LightingMes.TryParse(catalogueBytes, out var catalogue);
        if (failure != LightingFailure.None) return failure;
        var schemes = new SortedDictionary<int, LightSchemeTable>();
        foreach (var id in sectors.Values.Select(raw => raw == 0 ? mapDefault : raw).Distinct().Order())
        {
            token.ThrowIfCancellationRequested();
            if (schemes.Count == MaxSchemes) return LightingFailure.LimitExceeded;
            if (!catalogue.TryGetValue(id, out var schemeName)) return LightingFailure.SchemeNotCatalogued;
            var file = SafeName(schemeName);
            if (file is null) return LightingFailure.UnsafeResourceName;
            failure = Resource("rules/" + file.ToLowerInvariant() + ".mes", out var schemeBytes);
            if (failure != LightingFailure.None) return failure;
            failure = LightSchemeParser.TryParse(id, schemeBytes, out var table);
            if (failure != LightingFailure.None) return failure;
            schemes.Add(id, table!);
        }
        token.ThrowIfCancellationRequested();
        source = new AreaLightingSource(moduleIdentity.Sha256, snapshot.CatalogueMap, mapDefault, LightingMes.Hash(mapInfo), sectors, schemes, startup);
        return LightingFailure.None;
    }

    // mapinfo.txt is "Key:Value" lines; LightScheme must occur exactly once and be positive.
    // Absence is an error, not a guessed per-map fallback.
    internal static LightingFailure MapDefault(ReadOnlySpan<byte> payload, out int scheme)
    {
        scheme = 0;
        if (payload.Length > LightingMes.MaxPayloadBytes) return LightingFailure.LimitExceeded;
        int? found = null;
        foreach (var line in Encoding.Latin1.GetString(payload).Split('\n'))
        {
            var parts = line.Split(':', 2);
            if (parts.Length != 2 || !parts[0].Trim().Equals("LightScheme", StringComparison.OrdinalIgnoreCase)) continue;
            if (found.HasValue || !int.TryParse(parts[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
                return LightingFailure.MapDefaultMalformed;
            found = value;
        }
        if (found is not { } result) return LightingFailure.MapDefaultMissing;
        scheme = result; return LightingFailure.None;
    }

    private static LightingFailure SectorScheme(DatArchive module, AreaSectorRecord sector, string mapPrefix, out int scheme)
    {
        scheme = 0;
        if (!sector.Present || sector.Entry is not { } identity || sector.PayloadSha256 is null ||
            identity.Source.Role != AreaSourceRole.Module || identity.Ordinal < 0 || identity.Ordinal >= module.Entries.Count)
            return LightingFailure.SectorIdentityMismatch;
        var entry = module.Entries[identity.Ordinal];
        if (entry.Flags != identity.Flags || entry.StoredSize != identity.StoredBytes || entry.DeclaredOutputSize != identity.DecodedBytes ||
            entry.Offset != identity.Offset || LightingMes.Hash(entry.RawNameBytes.ToArray()) != identity.NameSha256)
            return LightingFailure.SectorIdentityMismatch;
        // The map default must come from the same map directory the sectors were loaded from.
        if (Name(entry) is not { } sectorName || !sectorName.StartsWith(mapPrefix, StringComparison.Ordinal))
            return LightingFailure.MapDirectoryMismatch;
        var failure = ReadEntry(module, entry, out var bytes);
        if (failure != LightingFailure.None) return failure;
        if (LightingMes.Hash(bytes) != sector.PayloadSha256) return LightingFailure.SectorIdentityMismatch;
        TerrainByteRange metadata;
        try { metadata = TerrainLoader.ParseSector(bytes, new TerrainLoaderOptions()).Framing[TerrainSection.MetadataAndSound]; }
        catch (TerrainException) { return LightingFailure.SectorMalformed; }
        // Absent metadata (old sector versions) differs from an explicit sentinel 0.
        if (metadata.Length != 24) return LightingFailure.SectorMetadataAbsent;
        scheme = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(metadata.Offset + 8));
        return scheme < 0 ? LightingFailure.SectorSchemeNegative : LightingFailure.None;
    }

    internal static LightingFailure ReadEntry(DatArchive module, DatEntry entry, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (entry.DeclaredOutputSize > LightingMes.MaxPayloadBytes || entry.StoredSize > LightingMes.MaxPayloadBytes) return LightingFailure.LimitExceeded;
        try { bytes = module.ReadEntry(entry); }
        catch (DatException) { return LightingFailure.ReadFailed; }
        return bytes.Length > LightingMes.MaxPayloadBytes ? LightingFailure.LimitExceeded : LightingFailure.None;
    }

    // Lower-case, '/'-separated ASCII name without its terminator; non-ASCII names are not addressable.
    internal static string? Name(DatEntry entry)
    {
        var raw = entry.RawNameBytes;
        if (raw.Count < 2 || raw[^1] != 0) return null;
        var chars = new char[raw.Count - 1];
        for (var i = 0; i < chars.Length; i++)
        {
            var b = raw[i]; if (b is 0 or > 127) return null;
            chars[i] = b == '\\' ? '/' : (char)(b is >= (byte)'A' and <= (byte)'Z' ? b + 32 : b);
        }
        return new string(chars);
    }

    // One relative name token from rule data: printable ASCII, no separators or traversal.
    internal static string? SafeName(string value)
    {
        var token = value.Trim(' ', '\t');
        if (token.Length is 0 or > 128 || token.Contains("..", StringComparison.Ordinal) ||
            token.Any(c => c < 32 || c >= 127 || c is '/' or '\\' or ':' or '<' or '>' or '*' or '?' or '"' or '|'))
            return null;
        return token;
    }
}

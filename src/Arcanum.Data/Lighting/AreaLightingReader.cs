using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Terrain;

namespace Arcanum.Data.Lighting;

/// <summary>
/// Reads the lighting inputs of one strict area snapshot: the map default (maplist -> mapinfo
/// <c>LightScheme</c>), the raw scheme word of every core sector (sector metadata +8, re-read and bound
/// to the snapshot's entry identity and payload digest), every referenced scheme through
/// <c>rules/lighting schemes.mes</c>, and the game-init startup year/hour.
/// <see cref="Read(ContentStack, AreaSnapshot, CancellationToken)"/> (ARC-DATA-027) reads every file as
/// the original does, after the module is mounted: <c>rules/gameinit.mes</c>, <c>rules/maplist.mes</c> and
/// <c>rules/lighting schemes.mes</c> at module load, <c>mapinfo.txt</c> when the map opens and each scheme
/// file when it is set, so each is the content stack's winner (exe-checked, ledger "Load phases"); each
/// sector is bound to the stack's module-archive candidate. <see cref="Read(DatArchive, AreaSnapshot, CancellationToken)"/>
/// is the pinned profiles' module-only read: the caller supplies the module archive the snapshot was
/// built from and re-verifies it afterwards. MES files are read with the original grammar (<see cref="Messages.MesFile"/>).
/// </summary>
public static class AreaLightingReader
{
    /// <summary>Core sectors read: a whole interior (ARC-RENDER-017) has up to <see cref="Area.AreaMapPlans.MaxSectorFiles"/>.</summary>
    public const int MaxSectors = 256;
    public const int MaxSchemes = 32;

    /// <summary>The pinned profiles' read: every file from <paramref name="module"/> alone.</summary>
    public static AreaLightingReadResult Read(DatArchive module, AreaSnapshot snapshot, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(module); ArgumentNullException.ThrowIfNull(snapshot);
        return Read(new ArchiveFiles(module), snapshot, token);
    }

    /// <summary>ARC-DATA-027: every file through <paramref name="stack"/> in the phase the original reads it. The stack's
    /// module archive must be the one the snapshot was built from: its length is checked against the snapshot's module
    /// identity (<see cref="LightingFailure.SectorIdentityMismatch"/> otherwise), and each sector is bound by member identity
    /// and payload digest; the rule files are the stack's.</summary>
    public static AreaLightingReadResult Read(ContentStack stack, AreaSnapshot snapshot, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(stack); ArgumentNullException.ThrowIfNull(snapshot);
        var module = stack.Plan.SearchOrder.SingleOrDefault(source => source.Role == ContentSourceRole.ModuleArchive);
        var identity = snapshot.Inputs.Where(input => input.Key.Role == AreaSourceRole.Module).Take(2).ToArray();
        long? length = null;
        try { if (module is not null) length = new FileInfo(module.Path).Length; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return new(LightingFailure.ReadFailed, null); }
        if (identity.Length == 1 && length != identity[0].Length) return new(LightingFailure.SectorIdentityMismatch, null);
        return Read(new StackFiles(stack), snapshot, token);
    }

    private static AreaLightingReadResult Read(ILightingFiles files, AreaSnapshot snapshot, CancellationToken token)
    {
        var failure = ReadCore(files, snapshot, token, out var source);
        return new(failure, failure == LightingFailure.None ? source : null);
    }

    private static LightingFailure ReadCore(ILightingFiles files, AreaSnapshot snapshot, CancellationToken token, out AreaLightingSource? source)
    {
        source = null;
        if (!snapshot.StrictlyPublished) return LightingFailure.SnapshotNotStrict;
        var modules = snapshot.Inputs.Where(input => input.Key.Role == AreaSourceRole.Module).Take(2).ToArray();
        if (modules.Length != 1) return LightingFailure.SectorIdentityMismatch;
        var moduleIdentity = modules[0];
        // Every lighting file is read at module load or later (ARC-DATA-027).
        LightingFailure Resource(string path, out byte[] bytes) => files.Read(path, ContentLoadPhase.ModuleLoaded, out bytes);

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
            // ARC-DATA-035: a generated window sector reads its scheme word from its template, which the original reads like a
            // sector file (ledger SS5); the window names the template's path.
            var template = sector.Template is null ? null : snapshot.Window?.Sectors.SingleOrDefault(s => s.Coordinate == sector.Coordinate)?.TemplatePath;
            int raw;
            if (sector.Template is null) failure = SectorScheme(files, sector, "maps/" + directory.ToLowerInvariant() + "/", out raw);
            else if (template is null) { failure = LightingFailure.SectorIdentityMismatch; raw = 0; }
            else failure = TemplateScheme(files, sector, template, out raw);
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

    private static LightingFailure SectorScheme(ILightingFiles files, AreaSectorRecord sector, string mapPrefix, out int scheme)
    {
        scheme = 0;
        if (!sector.Present || sector.Entry is not { } identity || sector.PayloadSha256 is null || identity.Source.Role != AreaSourceRole.Module)
            return LightingFailure.SectorIdentityMismatch;
        var failure = files.Sector(sector, identity, mapPrefix, out var bytes);
        return failure != LightingFailure.None ? failure : Scheme(sector, bytes, out scheme);
    }

    private static LightingFailure TemplateScheme(ILightingFiles files, AreaSectorRecord sector, string path, out int scheme)
    {
        scheme = 0;
        if (!sector.Present || sector.PayloadSha256 is null) return LightingFailure.SectorIdentityMismatch;
        var failure = files.Template(path, out var bytes);
        return failure != LightingFailure.None ? failure : Scheme(sector, bytes, out scheme);
    }

    // The sector's bytes must be the payload the snapshot read; the scheme word is metadata + 8.
    private static LightingFailure Scheme(AreaSectorRecord sector, byte[] bytes, out int scheme)
    {
        scheme = 0;
        if (LightingMes.Hash(bytes) != sector.PayloadSha256) return LightingFailure.SectorIdentityMismatch;
        TerrainByteRange metadata;
        try { metadata = TerrainLoader.ParseSector(bytes, new TerrainLoaderOptions()).Framing[TerrainSection.MetadataAndSound]; }
        catch (TerrainException) { return LightingFailure.SectorMalformed; }
        // Absent metadata (old sector versions) differs from an explicit sentinel 0.
        if (metadata.Length != 24) return LightingFailure.SectorMetadataAbsent;
        scheme = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(metadata.Offset + 8));
        return scheme < 0 ? LightingFailure.SectorSchemeNegative : LightingFailure.None;
    }

    /// <summary>True when <paramref name="entry"/> is the member <paramref name="identity"/> names.</summary>
    internal static bool Matches(DatEntry entry, AreaEntryIdentity identity) =>
        entry.Ordinal == identity.Ordinal && entry.Flags == identity.Flags && entry.StoredSize == identity.StoredBytes &&
        entry.DeclaredOutputSize == identity.DecodedBytes && entry.Offset == identity.Offset && LightingMes.Hash(entry.RawNameBytes.ToArray()) == identity.NameSha256;

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

    /// <summary>Where the lighting readers take their files from.</summary>
    internal interface ILightingFiles
    {
        /// <summary>One rule or resource file by virtual path, as read in <paramref name="phase"/>.</summary>
        LightingFailure Read(string path, ContentLoadPhase phase, out byte[] bytes);

        /// <summary>The bytes of the module-archive member <paramref name="identity"/> names, which must lie under
        /// <paramref name="mapPrefix"/> (the map directory of the default scheme).</summary>
        LightingFailure Sector(AreaSectorRecord sector, AreaEntryIdentity identity, string mapPrefix, out byte[] bytes);

        /// <summary>ARC-DATA-035: the bytes of a window's terrain template, as the original reads it when the sector loads.</summary>
        LightingFailure Template(string path, out byte[] bytes);
    }

    /// <summary>The pinned profiles' source: one archive, a name it holds twice is ambiguous; the phase is not modelled.</summary>
    internal sealed class ArchiveFiles : ILightingFiles
    {
        private readonly DatArchive _archive;
        private readonly Dictionary<string, List<DatEntry>> _index = new(StringComparer.Ordinal);

        internal ArchiveFiles(DatArchive archive, string? prefix = null)
        {
            _archive = archive;
            foreach (var entry in archive.Entries)
            {
                var name = Name(entry);
                if (name is null || (prefix is not null && !name.StartsWith(prefix, StringComparison.Ordinal))) continue;
                if (!_index.TryGetValue(name, out var list)) _index.Add(name, list = new List<DatEntry>(1));
                list.Add(entry);
            }
        }

        public LightingFailure Read(string path, ContentLoadPhase phase, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            if (!_index.TryGetValue(path, out var found)) return LightingFailure.MissingResource;
            if (found.Count != 1) return LightingFailure.AmbiguousResource;
            return ReadEntry(_archive, found[0], out bytes);
        }

        public LightingFailure Sector(AreaSectorRecord sector, AreaEntryIdentity identity, string mapPrefix, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            if (identity.Ordinal < 0 || identity.Ordinal >= _archive.Entries.Count) return LightingFailure.SectorIdentityMismatch;
            var entry = _archive.Entries[identity.Ordinal];
            if (!Matches(entry, identity)) return LightingFailure.SectorIdentityMismatch;
            // The map default must come from the same map directory the sectors were loaded from.
            if (Name(entry) is not { } sectorName || !sectorName.StartsWith(mapPrefix, StringComparison.Ordinal))
                return LightingFailure.MapDirectoryMismatch;
            return ReadEntry(_archive, entry, out bytes);
        }

        // The pinned profiles generate no sector.
        public LightingFailure Template(string path, out byte[] bytes) { bytes = Array.Empty<byte>(); return LightingFailure.SectorIdentityMismatch; }
    }

    /// <summary>ARC-DATA-027: the content stack. A file is the candidate of its load phase; a sector is the stack's
    /// winner of its path in the map directory, which must be the module-archive member the snapshot read.</summary>
    internal sealed class StackFiles(ContentStack stack) : ILightingFiles
    {
        public LightingFailure Read(string path, ContentLoadPhase phase, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            return ContentPhase.Candidate(stack, path, phase) is { } candidate ? ReadCandidate(candidate, out bytes) : LightingFailure.MissingResource;
        }

        public LightingFailure Sector(AreaSectorRecord sector, AreaEntryIdentity identity, string mapPrefix, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            // Whole maps name their sectors canonically (WorldCatalog), so the sector's path is the map directory and its id.
            var winner = stack.Resolve(mapPrefix + sector.Key.Packed.ToString(CultureInfo.InvariantCulture) + ".sec").Winner;
            if (winner?.Entry is { } entry && winner.Source.Role == ContentSourceRole.ModuleArchive && Matches(entry, identity))
                return ReadCandidate(winner, out bytes);
            // As the module-only read: a sector whose member is not under this map directory is a directory mismatch (the
            // map default would come from another map); one overridden by another source, or a changed member, is not
            // the sector the snapshot read.
            return winner is null || (winner.Entry is { } other && LightingMes.Hash(other.RawNameBytes.ToArray()) != identity.NameSha256)
                ? LightingFailure.MapDirectoryMismatch : LightingFailure.SectorIdentityMismatch;
        }

        // A template is read while the game runs, so from the stack's winner, an archive member or a loose file.
        public LightingFailure Template(string path, out byte[] bytes) => Read(path, ContentLoadPhase.ModuleLoaded, out bytes);

        private LightingFailure ReadCandidate(ContentCandidate candidate, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            if (candidate.Length > LightingMes.MaxPayloadBytes) return LightingFailure.LimitExceeded;
            var read = stack.Read(candidate);
            if (read.Status == ContentReadStatus.TooLarge) return LightingFailure.LimitExceeded;
            if (read.Status != ContentReadStatus.Read) return LightingFailure.ReadFailed;
            bytes = read.Bytes!;
            return bytes.Length > LightingMes.MaxPayloadBytes ? LightingFailure.LimitExceeded : LightingFailure.None;
        }
    }
}

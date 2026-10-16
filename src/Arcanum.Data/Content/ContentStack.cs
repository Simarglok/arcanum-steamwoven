using System.Text;

namespace Arcanum.Data.Content;

/// <summary>An opened, read-only snapshot of a <see cref="ContentMountPlan"/>. A virtual path
/// resolves to the first source in search order that holds it, which is what the original
/// runtime opens on a static installation (derivation and dynamic limits in
/// docs/research/content-precedence.md). Archive indexes and loose files are captured at
/// <see cref="Open"/>; later changes on disk are not observed.</summary>
public sealed class ContentStack : IDisposable
{
    private const uint DirectoryFlag = 0x400;
    private const uint IgnoredFlag = 0x800;
    private const string CacheFolder = "tigcache";
    private readonly Dictionary<string, List<ContentCandidate>> _index;
    private readonly Dictionary<ContentSource, DatArchive> _archives;
    private readonly ContentStackOptions _options;
    private bool _disposed;

    private ContentStack(ContentMountPlan plan, ContentStackOptions options, Dictionary<string, List<ContentCandidate>> index,
        Dictionary<ContentSource, DatArchive> archives, int cacheFiles, int skippedLoose)
    {
        Plan = plan; _options = options; _index = index; _archives = archives;
        ExcludedCacheFiles = cacheFiles; SkippedLooseFiles = skippedLoose;
    }

    public ContentMountPlan Plan { get; }
    /// <summary>Files under a directory source's top-level <c>TIGCache</c> folder; the original
    /// deletes that folder when it mounts the directory, so they are never candidates.</summary>
    public int ExcludedCacheFiles { get; }
    /// <summary>Loose files skipped because their relative path is not printable ASCII; such
    /// names are outside the supported virtual-path space. Links are never followed.</summary>
    public int SkippedLooseFiles { get; }
    public int KeyCount => _index.Count;

    public static ContentStackOpenResult Open(ContentMountPlan plan, ContentStackOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        options ??= new();
        options.Validate();
        var index = new Dictionary<string, List<ContentCandidate>>(StringComparer.Ordinal);
        var archives = new Dictionary<ContentSource, DatArchive>(ReferenceEqualityComparer.Instance);
        int cacheFiles = 0, skipped = 0, looseFiles = 0;
        try
        {
            foreach (var source in plan.SearchOrder)
            {
                var failure = source.Kind == ContentSourceKind.Archive
                    ? IndexArchive(source, options, index, archives)
                    : IndexDirectory(source, options, index, ref cacheFiles, ref skipped, ref looseFiles);
                if (failure is not null)
                {
                    foreach (var archive in archives.Values) archive.Dispose();
                    return failure;
                }
            }
        }
        catch
        {
            foreach (var archive in archives.Values) archive.Dispose();
            throw;
        }
        return new(ContentStackStatus.Ready, new ContentStack(plan, options, index, archives, cacheFiles, skipped));
    }

    /// <summary>Resolves a virtual path such as <c>scr\01324Virgil.scr</c>. Separators may be
    /// either slash; letters compare ASCII case-insensitively. Rooted, drive-qualified,
    /// dot-leading (the original opens those directly, outside the stack), empty-segment,
    /// <c>..</c> and non-printable-ASCII paths are <see cref="ContentResolutionStatus.InvalidPath"/>.</summary>
    public ContentResolution Resolve(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        ThrowIfDisposed();
        var key = NormalizeQuery(path);
        if (key is null) return new(ContentResolutionStatus.InvalidPath, path, Array.Empty<ContentCandidate>());
        return _index.TryGetValue(key, out var candidates)
            ? new(ContentResolutionStatus.Resolved, key, candidates.AsReadOnly())
            : new(ContentResolutionStatus.NotFound, key, Array.Empty<ContentCandidate>());
    }

    /// <summary>Every virtual path held by two or more sources, ordered by key.</summary>
    public IReadOnlyList<ContentResolution> Overrides()
    {
        ThrowIfDisposed();
        return _index.Where(pair => pair.Value.Count > 1).OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ContentResolution(ContentResolutionStatus.Resolved, pair.Key, pair.Value.AsReadOnly())).ToArray();
    }

    /// <summary>Reads one candidate's bytes: an archive member through its <see cref="DatArchive"/>
    /// limits, a loose file up to <see cref="ContentStackOptions.MaxLooseFileBytes"/>.</summary>
    public ContentReadResult Read(ContentCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ThrowIfDisposed();
        try
        {
            if (!Plan.SearchOrder.Any(source => ReferenceEquals(source, candidate.Source)))
                throw new ArgumentException("The candidate belongs to a different stack.", nameof(candidate));
            if (candidate.Entry is { } entry)
            {
                if (!_archives.TryGetValue(candidate.Source, out var archive) || entry.Ordinal >= archive.Entries.Count || !ReferenceEquals(archive.Entries[entry.Ordinal], entry))
                    throw new ArgumentException("The candidate belongs to a different stack.", nameof(candidate));
                return new(ContentReadStatus.Read, archive.ReadEntry(entry));
            }
            if (candidate.FilePath is null || candidate.Source.Kind != ContentSourceKind.Directory)
                throw new ArgumentException("The candidate has no readable location.", nameof(candidate));
            using var stream = new FileStream(candidate.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > _options.MaxLooseFileBytes) return new(ContentReadStatus.TooLarge, null, candidate.FilePath);
            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) return new(ContentReadStatus.Unreadable, null, candidate.FilePath);
            return new(ContentReadStatus.Read, bytes);
        }
        catch (DatLimitExceededException) { return new(ContentReadStatus.TooLarge, null, candidate.Source.Path); }
        catch (Exception error) when (error is DatException or IOException or UnauthorizedAccessException)
        {
            return new(ContentReadStatus.Unreadable, null, candidate.Entry is null ? candidate.FilePath : candidate.Source.Path);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var archive in _archives.Values) archive.Dispose();
    }

    /// <summary>The original lowercases member names and looks them up by binary search, so a
    /// faithful model needs every name strictly ordered once lowercased, in both the
    /// backslash form and the slash form; anything else is refused, not guessed.</summary>
    private static ContentStackOpenResult? IndexArchive(ContentSource source, ContentStackOptions options,
        Dictionary<string, List<ContentCandidate>> index, Dictionary<ContentSource, DatArchive> archives)
    {
        if (HasIgnoreList(source.Path)) return new(ContentStackStatus.IgnoreListUnsupported, null, source.Path);
        DatArchive archive;
        try { archive = DatArchive.Open(source.Path, options.Dat); }
        catch (Exception error) when (error is DatException or IOException or UnauthorizedAccessException)
        {
            return new(ContentStackStatus.ArchiveUnreadable, null, source.Path);
        }
        archives.Add(source, archive);
        string? previousBackslash = null, previousSlash = null;
        foreach (var entry in archive.Entries)
        {
            var raw = entry.RawNameBytes;
            var name = new StringBuilder(raw.Count - 1);
            for (var i = 0; i < raw.Count - 1; i++)
            {
                if (raw[i] is < 0x20 or > 0x7e) return new(ContentStackStatus.UnsupportedEntryName, null, source.Path);
                name.Append(raw[i] is >= (byte)'A' and <= (byte)'Z' ? (char)(raw[i] + 32) : (char)raw[i]);
            }
            var backslash = name.ToString();
            var slash = backslash.Replace('\\', '/');
            if (previousBackslash is not null && (string.CompareOrdinal(previousBackslash, backslash) >= 0 || string.CompareOrdinal(previousSlash, slash) >= 0))
                return new(ContentStackStatus.UnsortedArchiveIndex, null, source.Path);
            previousBackslash = backslash; previousSlash = slash;
            if ((entry.Flags & (DirectoryFlag | IgnoredFlag)) != 0) continue;
            Add(index, slash, new(source, entry, null, entry.DeclaredOutputSize));
        }
        return null;
    }

    private static ContentStackOpenResult? IndexDirectory(ContentSource source, ContentStackOptions options,
        Dictionary<string, List<ContentCandidate>> index, ref int cacheFiles, ref int skipped, ref int looseFiles)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var walk = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint, ReturnSpecialDirectories = false };
        try
        {
            foreach (var info in new DirectoryInfo(source.Path).EnumerateFileSystemInfos("*", walk))
            {
                var relative = Path.GetRelativePath(source.Path, info.FullName).Replace(Path.DirectorySeparatorChar, '/');
                var key = ContentMountPlan.AsciiLower(relative);
                var top = key.Split('/')[0];
                if (top == CacheFolder)
                {
                    if (info is FileInfo) cacheFiles++;
                    continue;
                }
                if (info is DirectoryInfo) continue;
                if (relative.Any(c => c is < ' ' or > '\x7e'))
                {
                    skipped++;
                    continue;
                }
                if (++looseFiles > options.MaxLooseFiles) return new(ContentStackStatus.LooseLimitExceeded, null, source.Path);
                // Two loose names that differ only in case cannot both be what the
                // original's case-insensitive lookup returns; refuse instead of picking one.
                if (!seen.Add(key)) return new(ContentStackStatus.AmbiguousLooseName, null, info.FullName);
                Add(index, key, new(source, null, info.FullName, ((FileInfo)info).Length));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new(ContentStackStatus.LooseUnreadable, null, source.Path);
        }
        return null;
    }

    private static void Add(Dictionary<string, List<ContentCandidate>> index, string key, ContentCandidate candidate)
    {
        if (!index.TryGetValue(key, out var list)) index[key] = list = new List<ContentCandidate>(1);
        list.Add(candidate);
    }

    private static bool HasIgnoreList(string archivePath)
    {
        var directory = Path.GetDirectoryName(archivePath)!;
        var wanted = ContentMountPlan.AsciiLower(Path.GetFileName(archivePath) + ".ignore");
        return Directory.EnumerateFileSystemEntries(directory).Any(path => ContentMountPlan.AsciiLower(Path.GetFileName(path)) == wanted);
    }

    internal static string? NormalizeQuery(string path)
    {
        if (path.Length == 0 || path[0] is '.' or '/' or '\\' || (path.Length > 1 && path[1] == ':')) return null;
        if (path.Any(c => c is < ' ' or > '\x7e')) return null;
        var key = ContentMountPlan.AsciiLower(path.Replace('\\', '/'));
        foreach (var segment in key.Split('/'))
            if (segment.Length == 0 || segment == "..") return null;
        return key;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

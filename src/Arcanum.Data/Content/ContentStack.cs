using System.Text;

namespace Arcanum.Data.Content;

/// <summary>An opened, read-only snapshot of a <see cref="ContentMountPlan"/>. A virtual path
/// resolves to the first source in search order that holds it, which is what the original
/// runtime opens on a static installation (derivation and dynamic limits in
/// docs/research/content-precedence.md). Archive indexes and loose files are captured at
/// <see cref="Open"/>; later changes on disk are not observed. ARC-DEV-372: a viewer run may share one index between its readers
/// (<see cref="OpenShared"/>); each reader still holds and disposes its own handle.</summary>
public sealed class ContentStack : IDisposable
{
    private const uint DirectoryFlag = 0x400;
    private const uint IgnoredFlag = 0x800;
    private const string CacheFolder = "tigcache";
    private readonly Dictionary<string, List<ContentCandidate>> _index;
    private readonly Dictionary<ContentSource, DatArchive> _archives;
    private readonly HashSet<ContentCandidate> _issued;
    private readonly ContentStackOptions _options;
    private readonly Share _share;
    private bool _disposed;

    private ContentStack(ContentMountPlan plan, ContentStackOptions options, Dictionary<string, List<ContentCandidate>> index,
        Dictionary<ContentSource, DatArchive> archives, int cacheFiles, int skippedLoose)
    {
        Plan = plan; _options = options; _index = index; _archives = archives;
        _issued = new HashSet<ContentCandidate>(index.Values.SelectMany(list => list), ReferenceEqualityComparer.Instance);
        ExcludedCacheFiles = cacheFiles; SkippedLooseFiles = skippedLoose;
        _share = new(archives);
    }

    /// <summary>Another handle on the same index and archives (the caller has acquired the share).</summary>
    private ContentStack(ContentStack origin)
    {
        Plan = origin.Plan; _options = origin._options; _index = origin._index; _archives = origin._archives; _issued = origin._issued;
        ExcludedCacheFiles = origin.ExcludedCacheFiles; SkippedLooseFiles = origin.SkippedLooseFiles; _share = origin._share;
    }

    /// <summary>The archives of one index, closed when the last handle on it is disposed.</summary>
    private sealed class Share(Dictionary<ContentSource, DatArchive> archives)
    {
        private int _handles = 1;

        public bool TryAcquire()
        {
            for (var seen = Volatile.Read(ref _handles); seen > 0; seen = Volatile.Read(ref _handles))
                if (Interlocked.CompareExchange(ref _handles, seen + 1, seen) == seen) return true;
            return false;
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref _handles) != 0) return;
            foreach (var archive in archives.Values) archive.Dispose();
        }
    }

    public ContentMountPlan Plan { get; }
    /// <summary>Files under a directory source's top-level <c>TIGCache</c> folder; the original
    /// deletes that folder when it mounts the directory, so they are never candidates, also
    /// not through an enclosing source such as the game folder. Each file is counted once.</summary>
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
        var directoryRecords = new HashSet<string>(StringComparer.Ordinal);
        int cacheFiles = 0, skipped = 0, looseFiles = 0;
        try
        {
            var cacheRoots = CacheRoots(plan, out var failure);
            foreach (var source in plan.SearchOrder)
            {
                if (failure is not null) break;
                failure = source.Kind == ContentSourceKind.Archive
                    ? IndexArchive(source, options, index, archives, directoryRecords)
                    : IndexDirectory(source, options, index, cacheRoots!, ref cacheFiles, ref skipped, ref looseFiles);
            }
            // The original's member lookup does not skip directory records, so one above a file
            // of the same name would shadow it; such a layout is refused, not modelled.
            if (failure is null && directoryRecords.FirstOrDefault(index.ContainsKey) is { } collision)
                failure = new(ContentStackStatus.DirectoryRecordCollision, null, collision);
            if (failure is not null)
            {
                foreach (var archive in archives.Values) archive.Dispose();
                return failure;
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

    /// <summary>Every indexed virtual path that starts with <paramref name="prefix"/>, in ordinal
    /// order. The prefix is only lower-cased (ASCII) and its <c>\</c> turned into <c>/</c>; it is a
    /// plain string prefix, not a validated path. An empty prefix lists every path; a prefix with
    /// non-printable-ASCII characters matches nothing.</summary>
    public IReadOnlyList<string> Keys(string prefix = "")
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ThrowIfDisposed();
        if (prefix.Any(c => c is < ' ' or > '\x7e')) return Array.Empty<string>();
        var normalized = ContentMountPlan.AsciiLower(prefix.Replace('\\', '/'));
        return _index.Keys.Where(key => key.StartsWith(normalized, StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
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
            // Only candidates this stack issued are readable; a copied or edited record is not.
            if (!_issued.Contains(candidate))
                throw new ArgumentException("The candidate was not issued by this stack.", nameof(candidate));
            if (candidate.Entry is { } entry) return new(ContentReadStatus.Read, _archives[candidate.Source].ReadEntry(entry));
            using var stream = new FileStream(candidate.FilePath!, FileMode.Open, FileAccess.Read, FileShare.Read);
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
        _share.Release();
    }

    // ARC-DEV-372: the index of a game folder is the same for every reader of a run (the original reads its static installation once), yet
    // each of the viewer's ~25 readers opened its own (about 87 MiB allocated and 0.3-0.4 s each, in parallel for the background binders).
    // A viewer run enables sharing; OpenShared then keeps one index per mount plan (at most two plans) and hands out handles on it.
    private const int SharedPlans = 2;
    private static readonly object SharedGate = new();
    private static readonly List<SharedEntry> SharedEntries = [];
    private static bool _sharing;

    private sealed class SharedEntry(string key)
    {
        public string Key { get; } = key;
        public object Gate { get; } = new();
        public string? Signature;
        public ContentStack? Held;
    }

    /// <summary>True while <see cref="OpenShared"/> shares an index; false (the default) it opens a private one as <see cref="Open"/> does.</summary>
    public static bool SharingEnabled { get { lock (SharedGate) return _sharing; } }

    /// <summary>The viewer's start: turns sharing on, or off (the held indexes are released, handles in use stay valid).</summary>
    public static void EnableSharing(bool on = true)
    {
        lock (SharedGate) _sharing = on;
        if (!on) ResetShared();
    }

    /// <summary>Releases every held index, so the next <see cref="OpenShared"/> indexes the disk again: the host changed what is mounted
    /// (<see cref="PrototypeCacheMount.Use"/> calls it). Handles still open keep their index until they are disposed.</summary>
    public static void ResetShared()
    {
        SharedEntry[] gone;
        lock (SharedGate) { gone = SharedEntries.ToArray(); SharedEntries.Clear(); }
        foreach (var entry in gone) Drop(entry);
    }

    private static void Drop(SharedEntry entry)
    {
        ContentStack? held;
        lock (entry.Gate) { held = entry.Held; entry.Held = null; entry.Signature = null; }
        held?.Dispose();
    }

    /// <summary>Like <see cref="Open"/>, but while <see cref="SharingEnabled"/> and with the default options the caller gets a handle on the index
    /// an earlier call opened for the same plan, which is indexed once, even when several threads ask at the same moment. The index is reopened
    /// when an archive's size or time changed; loose files are indexed at that first open only (as in any stack), and the host calls
    /// <see cref="ResetShared"/> when it changes what is mounted. The caller disposes its handle as for <see cref="Open"/>.</summary>
    public static ContentStackOpenResult OpenShared(ContentMountPlan plan, ContentStackOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (options is not null || !SharingEnabled) return Open(plan, options);
        var key = plan.GameRoot + "|" + plan.ModuleName + "|" + string.Join('|', plan.SearchOrder.Select(source => $"{(int)source.Role}:{source.Number}:{source.Path}"));
        SharedEntry? entry = null;
        List<SharedEntry> evicted = [];
        lock (SharedGate)
        {
            // Sharing switched off between the first test and here: no entry is registered after the reset that came with it.
            if (_sharing)
            {
                entry = SharedEntries.Find(known => known.Key == key) ?? new SharedEntry(key);
                SharedEntries.Remove(entry); SharedEntries.Add(entry);
                while (SharedEntries.Count > SharedPlans) { evicted.Add(SharedEntries[0]); SharedEntries.RemoveAt(0); }
            }
        }
        if (entry is null) return Open(plan);
        foreach (var old in evicted) Drop(old);
        lock (entry.Gate)
        {
            var signature = ArchiveSignature(plan);
            if (entry.Held is { } held && entry.Signature == signature && held._share.TryAcquire()) return new(ContentStackStatus.Ready, new ContentStack(held));
            var stale = entry.Held; entry.Held = null;
            stale?.Dispose();
            var opened = Open(plan);
            if (opened.Status != ContentStackStatus.Ready) return opened;
            lock (SharedGate)
            {
                // A reset or an eviction that came while this thread indexed leaves the entry unregistered: nothing is kept for it.
                if (!SharedEntries.Contains(entry) || !opened.Stack!._share.TryAcquire()) return opened;
            }
            entry.Held = new ContentStack(opened.Stack!); entry.Signature = signature;
            return opened;
        }
    }

    private static string ArchiveSignature(ContentMountPlan plan)
    {
        var text = new StringBuilder();
        foreach (var source in plan.SearchOrder.Where(source => source.Kind == ContentSourceKind.Archive))
        {
            // An ignore list beside the archive decides what Open indexes (it refuses the archive), so its presence is part of the signature.
            try { var info = new FileInfo(source.Path); text.Append(info.Length).Append('/').Append(info.LastWriteTimeUtc.Ticks).Append(HasIgnoreList(source.Path) ? "!" : "").Append(';'); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { text.Append("?;"); }
        }
        return text.ToString();
    }

    /// <summary>The original lowercases member names and looks them up by binary search, so a
    /// faithful model needs every name strictly ordered once lowercased, in both the
    /// backslash form and the slash form; anything else is refused, not guessed.</summary>
    private static ContentStackOpenResult? IndexArchive(ContentSource source, ContentStackOptions options,
        Dictionary<string, List<ContentCandidate>> index, Dictionary<ContentSource, DatArchive> archives, HashSet<string> directoryRecords)
    {
        DatArchive archive;
        try
        {
            if (HasIgnoreList(source.Path)) return new(ContentStackStatus.IgnoreListUnsupported, null, source.Path);
            archive = DatArchive.Open(source.Path, options.Dat);
        }
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
            if ((entry.Flags & IgnoredFlag) != 0) continue;
            if ((entry.Flags & DirectoryFlag) != 0)
            {
                directoryRecords.Add(slash);
                continue;
            }
            Add(index, slash, new(source, entry, null, entry.DeclaredOutputSize));
        }
        return null;
    }

    private static ContentStackOpenResult? IndexDirectory(ContentSource source, ContentStackOptions options,
        Dictionary<string, List<ContentCandidate>> index, IReadOnlyList<(string Owner, string Path)> cacheRoots,
        ref int cacheFiles, ref int skipped, ref int looseFiles)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var walk = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint, ReturnSpecialDirectories = false };
        try
        {
            foreach (var info in new DirectoryInfo(source.Path).EnumerateFileSystemInfos("*", walk))
            {
                var cache = cacheRoots.FirstOrDefault(root => info.FullName.StartsWith(root.Path + Path.DirectorySeparatorChar, StringComparison.Ordinal) || info.FullName == root.Path);
                if (cache.Path is not null)
                {
                    if (info is FileInfo && cache.Owner == source.Path) cacheFiles++;
                    continue;
                }
                if (info is DirectoryInfo) continue;
                var relative = Path.GetRelativePath(source.Path, info.FullName).Replace(Path.DirectorySeparatorChar, '/');
                var key = ContentMountPlan.AsciiLower(relative);
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

    /// <summary>Each directory source's top-level <c>TIGCache</c> folders (any letter case).</summary>
    private static IReadOnlyList<(string Owner, string Path)>? CacheRoots(ContentMountPlan plan, out ContentStackOpenResult? failure)
    {
        failure = null;
        var roots = new List<(string, string)>();
        foreach (var source in plan.SearchOrder.Where(source => source.Kind == ContentSourceKind.Directory))
        {
            try
            {
                roots.AddRange(Directory.EnumerateDirectories(source.Path)
                    .Where(path => ContentMountPlan.AsciiLower(Path.GetFileName(path)) == CacheFolder)
                    .Select(path => (source.Path, Path.GetFullPath(path))));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                failure = new(ContentStackStatus.LooseUnreadable, null, source.Path);
                return null;
            }
        }
        return roots;
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

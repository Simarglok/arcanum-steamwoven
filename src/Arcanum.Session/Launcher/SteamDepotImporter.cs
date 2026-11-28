using Arcanum.Data;
using Arcanum.Data.Content;

namespace Arcanum.Session.Launcher;

/// <summary>Where the import is: the downloaded folder is checked, its archives' compressed entries are unpacked in memory, its files are
/// copied, the copy is identified.</summary>
public enum SteamImportStage { Checking, Verifying, Copying, Identifying }

/// <summary>One step of the import; the counts move while <see cref="SteamImportStage.Verifying"/> (bytes only) and
/// <see cref="SteamImportStage.Copying"/> run.</summary>
public sealed record SteamImportProgress(SteamImportStage Stage, int FilesDone, int FilesTotal, long BytesDone, long BytesTotal, string? File);

/// <summary>How an import ended. Only <see cref="Imported"/> leaves a game folder to use.</summary>
public enum SteamImportStatus
{
    Imported, InvalidPath, SourceMissing, Incomplete, UnsupportedSource, TargetNotEmpty, NotEnoughSpace, SourceChanged, CopyFailed,
    NotAGameFolder, Cancelled,
}

/// <param name="Detail">What was refused or failed (a name, a size or an error; never bytes of the game).</param>
/// <param name="Folder">The checked game folder of an <see cref="SteamImportStatus.Imported"/> result; null otherwise.</param>
/// <param name="Partial">The copy had begun, so the target may hold part of the files.</param>
public sealed record SteamImportResult(SteamImportStatus Status, string? Detail = null, GameFolderCheck? Folder = null, bool Partial = false)
{
    public bool Succeeded => Status == SteamImportStatus.Imported;
}

/// <summary>The import's limits and stand-ins. The defaults are the Steam depot's size and this machine's disk.</summary>
public sealed record SteamImportOptions
{
    /// <summary>A downloaded folder holding fewer bytes is not finished: the manifest's size with one percent of slack.</summary>
    public long MinimumBytes { get; init; } = SteamDepot.ManifestBytes / 100 * 99;
    /// <summary>Free space kept on top of the copied size.</summary>
    public long SpaceSlackBytes { get; init; } = 64L * 1024 * 1024;
    public int MaxFiles { get; init; } = 20_000;
    public int MaxDepth { get; init; } = 16;
    /// <summary>The free bytes on the volume holding an existing folder; null when unknown (the check is then skipped).</summary>
    public Func<string, long?> FreeSpace { get; init; } = GogInstallerUnpacker.DiskFreeSpace;
    /// <summary>Why the ready folder's archives are damaged (a file cut short, or holes an unfinished download left), or null when they
    /// are not; the callback gets the compressed bytes checked and to check.</summary>
    public Func<GameFolderCheck, Action<long, long>?, CancellationToken, string?> VerifyArchives { get; init; } = SteamDepotImporter.VerifyArchives;
    /// <summary>Why the game cannot open the whole folder's content (a layout it refuses), or null when it can.</summary>
    public Func<GameFolderCheck, string?> OpenContent { get; init; } = SteamDepotImporter.OpenContent;
}

/// <summary>
/// ARC-DEV-3515: copies the player's downloaded Steam depot (<see cref="SteamDepot"/>) into a game folder of their choosing, so the
/// game does not depend on a folder Steam owns and may clear. It refuses an unfinished download (not a game folder yet, too few
/// bytes, an archive that does not open or holds a compressed entry that does not unpack, or files that change while they are
/// copied; holes in uncompressed entries and in loose files are not detected), copies into a folder that is new or empty (the
/// downloaded folder is only read, never written), then checks the copy with <see cref="GameFolderLocator.Check"/>. Unlike the GOG
/// installer path it does not require the verified executable: the check's words say which release it is. Links are refused. It
/// builds no prototypes (Play does). A failure is a typed <see cref="SteamImportResult"/>, not an exception. Godot-independent.
/// </summary>
public sealed class SteamDepotImporter
{
    private const int BufferBytes = 1024 * 1024;
    /// <summary>A large file reports its progress at least this often.</summary>
    private const long ReportBytes = 8L * 1024 * 1024;
    /// <summary>The archive entry flag of a zlib-compressed payload.</summary>
    private const uint CompressedEntry = 0x002;
    private const int MaxLinkDepth = 32;
    private const string FinderFile = ".DS_Store";

    private readonly SteamImportOptions _options;

    public SteamDepotImporter(SteamImportOptions? options = null) => _options = options ?? new SteamImportOptions();

    private sealed record Entry(string Relative, long Bytes, DateTime Written);

    /// <summary>The default <see cref="SteamImportOptions.VerifyArchives"/>: every mounted archive is opened and each compressed entry is
    /// unpacked in memory (an entry over the reader's size limits is skipped).</summary>
    public static string? VerifyArchives(GameFolderCheck check, Action<long, long>? progress, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (check.Plan is not { } plan) return "the folder is not a game folder";
        var paths = plan.SearchOrder.Where(source => source.Kind == ContentSourceKind.Archive).Select(source => source.Path).ToArray();
        var name = "";
        try
        {
            long total = 0, done = 0, reported = 0;
            foreach (var path in paths)
            {
                name = Path.GetFileName(path);
                using var archive = DatArchive.Open(path);
                total += archive.Entries.Where(entry => entry.Flags == CompressedEntry).Sum(entry => (long)entry.StoredSize);
            }
            foreach (var path in paths)
            {
                name = Path.GetFileName(path);
                using var archive = DatArchive.Open(path);
                foreach (var entry in archive.Entries.Where(entry => entry.Flags == CompressedEntry))
                {
                    cancel.ThrowIfCancellationRequested();
                    try { archive.ReadEntry(entry); }
                    catch (DatLimitExceededException) { }
                    done += entry.StoredSize;
                    if (done - reported >= ReportBytes || done == total) { reported = done; progress?.Invoke(done, total); }
                }
            }
            return null;
        }
        // A file Steam still holds is as unfinished as a damaged one.
        catch (Exception error) when (error is DatException or IOException or UnauthorizedAccessException) { return name + ": " + error.Message; }
    }

    /// <summary>The default <see cref="SteamImportOptions.OpenContent"/>: the folder's content is opened as the game opens it.</summary>
    public static string? OpenContent(GameFolderCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (check.Plan is not { } plan) return "the folder is not a game folder";
        var opened = ContentStack.Open(plan);
        if (opened.Status != ContentStackStatus.Ready) return opened.Status + (opened.Detail is { Length: > 0 } detail ? " " + detail : "");
        opened.Stack!.Dispose();
        return null;
    }

    /// <summary>Copies <paramref name="sourceFolder"/> into <paramref name="targetFolder"/> (a new folder whose parent exists, or an
    /// empty one). Progress is reported from the thread that runs the copy. Cancelling gives <see cref="SteamImportStatus.Cancelled"/>.</summary>
    public async Task<SteamImportResult> ImportAsync(string sourceFolder, string targetFolder, Action<SteamImportProgress>? progress = null,
        CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(sourceFolder);
        ArgumentNullException.ThrowIfNull(targetFolder);
        if (!Admissible(sourceFolder) || !Admissible(targetFolder)) return new(SteamImportStatus.InvalidPath, "path");
        string source, target;
        try
        {
            source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceFolder));
            target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetFolder));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return new(SteamImportStatus.InvalidPath, "path");
        }
        if (!Directory.Exists(source)) return new(SteamImportStatus.SourceMissing, source);
        var copying = false; // a failure from here on is the copy's, not the download's
        var writing = false; // a file exists in the target
        try
        {
            // Compared with links resolved: a link to the download must not become a way to write into it.
            if (Within(Resolve(target, 0), Resolve(source, 0)))
                return new(SteamImportStatus.InvalidPath, "the target is the downloaded folder or inside it");
            if (CheckTarget(target) is { } refused) return refused;

            progress?.Invoke(new(SteamImportStage.Checking, 0, 0, 0, 0, null));
            // 1. Is the download finished, and a game?
            var downloaded = GameFolderLocator.Check(source);
            if (!downloaded.Ready)
            {
                // A file that is not there yet, or cannot be read yet, is an unfinished download; anything else would not change by waiting.
                var missing = downloaded.Status is GameFolderStatus.MissingTig or GameFolderStatus.MissingModule or GameFolderStatus.MissingModuleArchive
                    or GameFolderStatus.MissingActorArchive or GameFolderStatus.MissingTerrainArchive or GameFolderStatus.Unreadable;
                return new(missing ? SteamImportStatus.Incomplete : SteamImportStatus.UnsupportedSource,
                    downloaded.Status + (downloaded.Detail is { Length: > 0 } detail ? " " + detail : ""));
            }
            var (files, listRefusal) = List(source, cancel);
            if (listRefusal is not null) return listRefusal;
            var total = files.Sum(file => file.Bytes);
            if (total < _options.MinimumBytes)
                return new(SteamImportStatus.Incomplete, $"{Megabytes(total)} of about {Megabytes(_options.MinimumBytes)} MB are there");
            Action<long, long>? verified = progress is null ? null
                : (bytes, of) => progress(new(SteamImportStage.Verifying, 0, files.Count, bytes, of, null));
            progress?.Invoke(new(SteamImportStage.Verifying, 0, files.Count, 0, 0, null));
            if (_options.VerifyArchives(downloaded, verified, cancel) is { } damaged) return new(SteamImportStatus.Incomplete, damaged);
            if (_options.OpenContent(downloaded) is { } closed) return new(SteamImportStatus.UnsupportedSource, closed);

            // 2. Is there room?
            var existing = target;
            while (!Directory.Exists(existing) && Path.GetDirectoryName(existing) is { } parent) existing = parent;
            if (_options.FreeSpace(existing) is { } free && free < total + _options.SpaceSlackBytes)
                return new(SteamImportStatus.NotEnoughSpace, $"{Megabytes(total + _options.SpaceSlackBytes)} MB needed, {Megabytes(free)} MB free");

            // 3. Copy.
            cancel.ThrowIfCancellationRequested();
            copying = true;
            if (await Copy(source, target, files, total, () => writing = true, progress, cancel).ConfigureAwait(false) is { } failed)
                return failed with { Partial = writing };

            // 4. A download still running would have changed the folder meanwhile.
            var (after, againRefusal) = List(source, cancel);
            if (againRefusal is not null) return againRefusal with { Partial = true };
            if (!files.SequenceEqual(after)) return new(SteamImportStatus.SourceChanged, "the downloaded files changed while they were copied", Partial: true);

            progress?.Invoke(new(SteamImportStage.Identifying, files.Count, files.Count, total, total, null));
            var check = GameFolderLocator.Check(target);
            return check.Ready
                ? new(SteamImportStatus.Imported, null, check)
                : new(SteamImportStatus.NotAGameFolder, check.Status + (check.Detail is { Length: > 0 } text ? " " + text : ""), Partial: true);
        }
        catch (OperationCanceledException) { return new(SteamImportStatus.Cancelled, Partial: writing); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            return new(copying ? SteamImportStatus.CopyFailed : SteamImportStatus.UnsupportedSource, error.Message, Partial: writing);
        }
    }

    /// <summary><paramref name="path"/> with every linked folder on the way replaced by what it points to.</summary>
    private static string Resolve(string path, int depth)
    {
        if (depth > MaxLinkDepth) throw new IOException("too many links");
        var root = Path.GetPathRoot(path) ?? "";
        var current = root;
        foreach (var part in path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            var folder = new DirectoryInfo(current);
            if (folder.LinkTarget is not null && folder.ResolveLinkTarget(returnFinalTarget: true) is { } to)
                current = Resolve(Path.TrimEndingDirectorySeparator(to.FullName), depth + 1);
        }
        return current;
    }

    /// <summary>Every file below <paramref name="root"/>, ordered by its relative path; a link, too many files or too deep a tree is
    /// refused (the Finder's own <c>.DS_Store</c> files are left out).</summary>
    private (List<Entry> Files, SteamImportResult? Refused) List(string root, CancellationToken cancel)
    {
        var files = new List<Entry>();
        var folders = new Stack<(DirectoryInfo Folder, int Depth)>();
        folders.Push((new DirectoryInfo(root), 0));
        while (folders.Count > 0)
        {
            cancel.ThrowIfCancellationRequested();
            var (folder, depth) = folders.Pop();
            foreach (var item in folder.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false }))
            {
                var relative = Path.GetRelativePath(root, item.FullName);
                if (item.LinkTarget is not null) return (files, new(SteamImportStatus.UnsupportedSource, "a link: " + relative));
                if (item is DirectoryInfo child)
                {
                    if (depth + 1 > _options.MaxDepth) return (files, new(SteamImportStatus.UnsupportedSource, "too deep: " + relative));
                    folders.Push((child, depth + 1));
                }
                else if (item is FileInfo file && file.Name != FinderFile)
                {
                    if (files.Count >= _options.MaxFiles) return (files, new(SteamImportStatus.UnsupportedSource, "too many files"));
                    files.Add(new(relative, file.Length, file.LastWriteTimeUtc));
                }
            }
        }
        files.Sort((a, b) => string.CompareOrdinal(a.Relative, b.Relative));
        return (files, null);
    }

    /// <summary>Null when every listed file was written with its listed size; <paramref name="writing"/> is called once a file exists in
    /// the target.</summary>
    private static async Task<SteamImportResult?> Copy(string source, string target, List<Entry> files, long total, Action writing,
        Action<SteamImportProgress>? progress, CancellationToken cancel)
    {
        Directory.CreateDirectory(target);
        var buffer = new byte[BufferBytes];
        long done = 0, reported = 0;
        var count = 0;
        foreach (var file in files)
        {
            cancel.ThrowIfCancellationRequested();
            var to = Path.Combine(target, file.Relative);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            long written = 0;
            await using (var input = new FileStream(Path.Combine(source, file.Relative), FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferBytes, useAsync: true))
            await using (var output = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferBytes, useAsync: true))
            {
                writing();
                int read;
                while ((read = await input.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
                {
                    written += read;
                    if (written > file.Bytes) return new(SteamImportStatus.SourceChanged, "a file grew while it was copied: " + file.Relative);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
                    done += read;
                    if (done - reported >= ReportBytes) { reported = done; progress?.Invoke(new(SteamImportStage.Copying, count, files.Count, done, total, file.Relative)); }
                }
            }
            if (written != file.Bytes) return new(SteamImportStatus.SourceChanged, "a file shrank while it was copied: " + file.Relative);
            count++;
            progress?.Invoke(new(SteamImportStage.Copying, count, files.Count, done, total, file.Relative));
        }
        return null;
    }

    /// <summary>A target that is a file, is not empty, or is new without an existing parent is refused (null when it can be used).</summary>
    private static SteamImportResult? CheckTarget(string target)
    {
        try
        {
            if (File.Exists(target)) return new(SteamImportStatus.InvalidPath, "the target is a file");
            if (Directory.Exists(target))
                return Directory.EnumerateFileSystemEntries(target).Any(entry => Path.GetFileName(entry) != FinderFile)
                    ? new(SteamImportStatus.TargetNotEmpty, target) : null;
            var parent = Path.GetDirectoryName(target);
            return parent is not null && Directory.Exists(parent) ? null : new(SteamImportStatus.InvalidPath, "the parent of the target folder does not exist");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            return new(SteamImportStatus.InvalidPath, "the target folder cannot be read");
        }
    }

    private static bool Within(string path, string folder)
        => string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
           || path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static long Megabytes(long bytes) => (bytes + 1024 * 1024 - 1) / (1024 * 1024);

    private static bool Admissible(string path) => !string.IsNullOrWhiteSpace(path) && path.Length <= GameFolderLocator.MaxPathLength && !path.Any(char.IsControl);
}

using System.Security.Cryptography;

namespace Arcanum.Data.Hd;

/// <summary><see cref="InstalledButUnreadable"/>: the copy was renamed into place (and a
/// replaced pack deleted) but opening it there failed; the detail says why.</summary>
public enum HdImportStatus { Imported, SourceInvalid, IntegrityFailed, AlreadyExists, CopyFailed, Cancelled, InstalledButUnreadable }
/// <param name="Pack">The pack as opened from its new place (same identity as the source).</param>
public sealed record HdImportResult(HdImportStatus Status, HdPack? Pack, HdIntegrityReport? Report, string? Detail = null);

/// <summary>Adds a pack built on another device to this device's pack collection: the
/// source must open and pass <see cref="HdPack.VerifyIntegrity"/> completely (a damaged
/// copy is refused, never partly imported). Only the manifest, the recipe and the entry
/// files are copied, each re-hashed while copying, into a hidden staging directory of the
/// collection that is renamed to <c>&lt;collection&gt;/&lt;pack id&gt;</c> only when complete.
/// The source is never modified. The installation is not consulted here: frames are checked
/// against it when bound (<see cref="HdPack.ResolveSprite"/>).</summary>
public static class HdPackImport
{
    public static HdImportResult Import(string sourceDirectory, string collection, HdPackOptions? options = null, bool replace = false,
        CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceDirectory);
        ArgumentException.ThrowIfNullOrEmpty(collection);
        options ??= new(); options.Validate();
        var opened = HdPack.Open(sourceDirectory, options);
        if (opened.Status != HdPackOpenStatus.Opened) return new(HdImportStatus.SourceInvalid, null, null, $"{opened.Status}: {opened.Detail}");
        var source = opened.Pack!;
        var report = source.VerifyIntegrity(token);
        if (report.Cancelled) return new(HdImportStatus.Cancelled, null, report);
        if (!report.Intact) return new(HdImportStatus.IntegrityFailed, null, report);

        var root = Path.GetFullPath(collection);
        var target = Path.Combine(root, source.Id);
        if (PathsEqual(Path.TrimEndingDirectorySeparator(source.Directory), target))
            return new(HdImportStatus.AlreadyExists, null, report, "source is the target");
        if (Directory.Exists(target))
        {
            // The same pack (possibly the source itself reached through a link) is never
            // replaced by a copy of itself.
            var installed = HdPack.Open(target, options);
            if (installed.Pack?.Identity == source.Identity) return new(HdImportStatus.AlreadyExists, null, report, "identical pack installed");
            if (!replace) return new(HdImportStatus.AlreadyExists, null, report, source.Id);
        }

        SweepStaging(root);
        var staging = Path.Combine(root, StagingPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            var files = new List<(string File, string Sha256, long Bytes)>
            {
                (HdPack.ManifestFile, source.Identity.ManifestSha256, -1), (source.RecipeFile, source.RecipeSha256, -1),
            };
            files.AddRange(source.Entries.Select(e => (e.File, e.Sha256, e.Bytes)));
            foreach (var (file, sha, bytes) in files)
            {
                if (token.IsCancellationRequested) { Delete(staging); return new(HdImportStatus.Cancelled, null, report); }
                if (!Copy(source.Directory, staging, file, sha, bytes))
                { Delete(staging); return new(HdImportStatus.CopyFailed, null, report, file); }
            }
            var copied = HdPack.Open(staging, options);
            if (copied.Status != HdPackOpenStatus.Opened || copied.Pack!.Identity != source.Identity)
            { Delete(staging); return new(HdImportStatus.CopyFailed, null, report, HdPack.ManifestFile); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Delete(staging);
            return new(HdImportStatus.CopyFailed, null, report, error.GetType().Name);
        }

        string? previous = null;
        try
        {
            if (Directory.Exists(target))
            {
                previous = Path.Combine(root, ReplacedPrefix + Guid.NewGuid().ToString("N"));
                Directory.Move(target, previous);
            }
            Directory.Move(staging, target);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Delete(staging);
            if (previous is null) return new(HdImportStatus.CopyFailed, null, report, error.GetType().Name);
            try { Directory.Move(previous, target); } // put the replaced pack back
            catch (Exception restore) when (restore is IOException or UnauthorizedAccessException)
            {
                return new(HdImportStatus.CopyFailed, null, report,
                    $"{error.GetType().Name}; the replaced pack could not be restored and is kept as {Path.GetFileName(previous)} in the collection");
            }
            return new(HdImportStatus.CopyFailed, null, report, error.GetType().Name);
        }
        if (previous is not null) Delete(previous);
        var final = HdPack.Open(target, options);
        return final.Status == HdPackOpenStatus.Opened
            ? new(HdImportStatus.Imported, final.Pack, report)
            : new(HdImportStatus.InstalledButUnreadable, null, report, $"{final.Status}: {final.Detail}");
    }

    private const string StagingPrefix = ".import-";
    private const string ReplacedPrefix = ".replaced-";

    /// <summary>Removes staging directories an interrupted import left behind more than a
    /// day ago. A <c>.replaced-*</c> directory may hold the only copy of a pack after a
    /// failed restore and is never removed automatically.</summary>
    private static void SweepStaging(string root)
    {
        if (!Directory.Exists(root)) return;
        try
        {
            foreach (var stale in Directory.GetDirectories(root, StagingPrefix + "*"))
                if (Directory.GetLastWriteTimeUtc(stale) < DateTime.UtcNow.AddDays(-1)) Delete(stale);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static bool Copy(string sourceRoot, string targetRoot, string relative, string sha256, long expectedBytes)
    {
        if (HdPackPath.HasLink(sourceRoot, relative)) return false;
        var to = HdPackPath.Full(targetRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        using (var input = new FileStream(HdPackPath.Full(sourceRoot, relative), FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, read); output.Write(buffer, 0, read); total += read;
            }
            output.Flush(true);
        }
        return (expectedBytes < 0 || total == expectedBytes) && Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() == sha256;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(a, b, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void Delete(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}

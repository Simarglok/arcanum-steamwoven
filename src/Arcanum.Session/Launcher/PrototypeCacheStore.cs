using System.Security.Cryptography;
using System.Text.Json;
using Arcanum.Executable.Prototypes;

namespace Arcanum.Session.Launcher;

/// <summary>
/// ARC-DEV-069: the folder of prototype files built from one player's <c>Arcanum.exe</c>, in Steamwoven's own settings folder (never
/// in the game folder, which stays read-only). Layout: <c>prototypes-&lt;first 16 hex of the executable's SHA-256&gt;/manifest.json</c>
/// and <c>.../files/proto/NNNNNN - label.pro</c>; only <c>files</c> is mounted ([PrototypeCacheMount]). The manifest says which executable
/// built the files, the SHA-256 of every data file the executable read or looked for while it built them (<c>rules\npc.txt</c>, the
/// message files, the art headers; an absent file counts as absent) and the length and SHA-256 of every prototype file, so a changed
/// executable, changed data or a damaged or edited file is noticed at every start and the files are built again. The manifest is written
/// last, into a temporary folder that then replaces the cache by a rename: a start that died half-way leaves no cache.
/// </summary>
internal sealed class PrototypeCacheStore
{
    public const int Schema = 2;
    public const string FolderPrefix = "prototypes-";
    public const string ManifestName = "manifest.json";
    public const int MaxManifestBytes = 16 << 20;
    public const int MaxDataFiles = 200_000;
    public const int MaxFiles = 4096;
    /// <summary>A temporary or replaced folder older than this is a leftover of a start that died and is removed.</summary>
    public static readonly TimeSpan LeftoverAge = TimeSpan.FromHours(6);

    private static readonly JsonSerializerOptions Json = new() { MaxDepth = 8, WriteIndented = false };

    public PrototypeCacheStore(string root, string exeSha256)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentException.ThrowIfNullOrEmpty(exeSha256);
        Root = Path.GetFullPath(root);
        ExeSha256 = exeSha256.ToLowerInvariant();
        Folder = Path.Combine(Root, FolderPrefix + ExeSha256[..16]);
    }

    public string Root { get; }
    public string ExeSha256 { get; }
    /// <summary>The cache folder of this executable (the manifest and <c>files</c>).</summary>
    public string Folder { get; }
    /// <summary>The folder the content stack mounts: it holds <c>proto/*.pro</c>.</summary>
    public string MountFolder => Path.Combine(Folder, "files");

    private sealed record DataEntry(string Path, string? Sha256);
    /// <summary>A directory search the executable made (<c>Directory</c> is the part of its pattern before the last backslash) and the SHA-256 of the sorted names the source answered.</summary>
    private sealed record ListingEntry(string Directory, string Sha256);
    private sealed record FileEntry(string Name, long Bytes, string Sha256);
    private sealed record Manifest(int Schema, string Exe, DataEntry[] Data, ListingEntry[] Listings, FileEntry[] Files);

    /// <summary>The number of files when the folder holds a cache that matches this executable and the data <paramref name="source"/> holds now
    /// and every file is intact; otherwise null (nothing is repaired: the caller builds again).</summary>
    public int? Verify(IOriginalFileSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            var manifestFile = new FileInfo(Path.Combine(Folder, ManifestName));
            if (!manifestFile.Exists || manifestFile.Length > MaxManifestBytes) return null;
            var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllBytes(manifestFile.FullName), Json);
            if (manifest is not { Data: not null, Listings: not null, Files: not null, Exe: not null } || manifest.Schema != Schema
                || !string.Equals(manifest.Exe, ExeSha256, StringComparison.Ordinal)
                || manifest.Files.Length is 0 or > MaxFiles || manifest.Data.Length > MaxDataFiles) return null;
            foreach (var entry in manifest.Data)
                if (entry is null || entry.Path is null || Hash(source.Read(entry.Path)) != entry.Sha256) return null;
            // A file added to a folder the executable searched would have been found by it.
            foreach (var listing in manifest.Listings)
                if (listing is null || listing.Directory is null || ListingHash(source, listing.Directory) != listing.Sha256) return null;
            var proto = Path.Combine(MountFolder, "proto");
            // Exactly the listed files: an extra .pro file would be mounted too. A name the system adds (.DS_Store, ._x) is not a file of the cache.
            if (Visible(MountFolder).Count() != 1 || !Directory.Exists(proto)) return null;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in manifest.Files)
            {
                if (entry is null || !ValidName(entry.Name) || !names.Add(entry.Name)) return null;
                var info = new FileInfo(Path.Combine(proto, entry.Name));
                if (!info.Exists || info.Length != entry.Bytes || info.LinkTarget is not null) return null;
                if (Hash(File.ReadAllBytes(info.FullName)) != entry.Sha256) return null;
            }
            if (Visible(proto).Count() != manifest.Files.Length) return null;
            return manifest.Files.Length;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or System.Security.SecurityException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Stores a finished build (replacing a cache that did not verify); null on success, else the reason in words.</summary>
    public string? Store(PrototypeBuildResult build, IOriginalFileSource source)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(source);
        if (build.Failure is not null || build.Files.Count is 0 or > MaxFiles) return "the build did not give a complete set of files";
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in build.Files)
            if (!ValidName(file.Name) || !names.Add(file.Name)) return "the build named a file the cache cannot hold";
        var data = build.ReadFiles.Select(path => (Path: path, Present: true)).Concat(build.MissedFiles.Select(path => (Path: path, Present: false)))
            .DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new DataEntry(entry.Path, Hash(source.Read(entry.Path)))).ToArray();
        var listings = build.ListedPatterns.Select(pattern => pattern[..Math.Max(0, pattern.LastIndexOf('\\'))])
            .Where(directory => !directory.Equals("proto", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(directory => new ListingEntry(directory, ListingHash(source, directory))).ToArray();
        var manifest = new Manifest(Schema, ExeSha256, data, listings,
            build.Files.Select(file => new FileEntry(file.Name, file.Bytes.Length, Hash(file.Bytes)!)).ToArray());
        var temporary = Folder + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var proto = Path.Combine(temporary, "files", "proto");
            Directory.CreateDirectory(proto);
            foreach (var file in build.Files) File.WriteAllBytes(Path.Combine(proto, file.Name), file.Bytes);
            // The manifest is the last file: a folder without it is no cache.
            File.WriteAllBytes(Path.Combine(temporary, ManifestName), JsonSerializer.SerializeToUtf8Bytes(manifest, Json));
            if (Directory.Exists(Folder))
            {
                var replaced = Folder + ".old-" + Guid.NewGuid().ToString("N");
                Retry(() => Directory.Move(Folder, replaced));
                TryDelete(replaced);
            }
            Retry(() => Directory.Move(temporary, Folder));
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            TryDelete(temporary);
            return $"the prototype files could not be stored ({error.GetType().Name}: {error.Message})";
        }
    }

    /// <summary>Removes the temporary and replaced folders of earlier starts that died (older than <see cref="LeftoverAge"/>).</summary>
    public void RemoveLeftovers()
    {
        try
        {
            if (!Directory.Exists(Root)) return;
            foreach (var path in Directory.EnumerateDirectories(Root, FolderPrefix + "*"))
            {
                var name = Path.GetFileName(path);
                if ((name.Contains(".tmp-", StringComparison.Ordinal) || name.Contains(".old-", StringComparison.Ordinal))
                    && DateTime.UtcNow - Directory.GetLastWriteTimeUtc(path) > LeftoverAge) TryDelete(path);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }

    /// <summary><c>NNNNNN - label.pro</c> (six digits, a label of printable ASCII without a path or reserved character, at most 100 characters): a name every
    /// file system the game runs on can hold, and one the content stack reads as a prototype.</summary>
    internal static bool ValidName(string? name)
    {
        if (name is null || name.Length < 14 || name.Length > 6 + 3 + 100 + 4 || !name.EndsWith(".pro", StringComparison.Ordinal) || name[6..9] != " - ") return false;
        for (var i = 0; i < 6; i++) if (name[i] is < '0' or > '9') return false;
        foreach (var c in name.AsSpan(9, name.Length - 13))
            if (c is < ' ' or > '~' or '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*') return false;
        return true;
    }

    private static IEnumerable<string> Visible(string folder)
        => Directory.EnumerateFileSystemEntries(folder).Where(path => !Path.GetFileName(path).StartsWith('.'));

    private static string ListingHash(IOriginalFileSource source, string directory)
        => Hash(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', source.List(directory).Order(StringComparer.OrdinalIgnoreCase))))!;

    /// <summary>A folder in use (a virus scanner on Windows, a second running copy) refuses a rename for a moment: three tries.</summary>
    private static void Retry(Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { action(); return; }
            catch (Exception error) when (attempt < 3 && error is IOException or UnauthorizedAccessException) { Thread.Sleep(250 * attempt); }
        }
    }

    private static string? Hash(byte[]? bytes) => bytes is null ? null : Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}

using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Arcanum.Data.Area;

/// <summary>What a file looked like when it was last hashed in full (ARC-DEV-374): the platform's file identity, never its
/// bytes. On macOS and Linux it is the device, inode, size and the modification and change times to the nanosecond (a
/// program that rewrites a file and puts its modification time back still moves the change time); on Windows (where the
/// system has no such ABI) the size and the last write time. Two stamps of the same path are equal only when the file has not
/// been written, replaced or renamed over since.</summary>
internal readonly record struct FileStamp(long Device, long Inode, long Size, long Modified, long ModifiedNanos, long Changed, long ChangedNanos);

/// <summary>The one process-wide record of whole files the process has already hashed in full (ARC-DEV-374, O5a of the
/// performance diagnosis; owner decision 2026-11-11, option A). The first check of a large file in the process reads and hashes it
/// all; every later check of the same path, in the same load or a later one, reuses that hash while the file's
/// <see cref="FileStamp"/> is unchanged, and hashes it again, as the first time, as soon as the stamp differs.
/// <para>Scope: only files of at least <see cref="MinBytes"/> (the archives and the large loose inputs; a prototype, a map patch
/// or a small resource is hashed on every check as before). The memo holds a path, a stamp, a length and a hash, no bytes. It
/// is a verdict cache and not a trust root: a pinned plan still compares the memoized hash with its pinned one, so a file
/// that is not the pinned file is refused on the first check and on every later one.</para>
/// <para>A hash is recorded only when the file's stamp is the same before and after the pass that computed it, so a file
/// that changed while it was read is not remembered. A caller that holds a path-bound handle
/// (<see cref="AreaPathStream"/>) keeps checking that binding on every pass; only the read of the bytes is skipped.</para></summary>
public static class VerifiedFileMemo
{
    /// <summary>The smallest file the memo keeps: 1 MiB, the size from which whole-set authentication passes already treat an input as large.</summary>
    internal const long MinBytes = 1L << 20;
    // A load touches a handful of archives and loose resources, a session at most a few dozen files; the cap only bounds a long-running
    // process that is fed ever new paths. A full memo stops recording, it never evicts a hash that is still good.
    private const int MaxEntries = 4096;

    private sealed record Entry(FileStamp Stamp, long Length, string Sha256);
    private static readonly ConcurrentDictionary<string, Entry> Verified = new(StringComparer.Ordinal);
    private static long _fullPasses, _reused;

    /// <summary>Whole-file passes that hashed a file since the process started or <see cref="Reset"/> (tests, the probe).</summary>
    internal static long FullPasses => Interlocked.Read(ref _fullPasses);
    /// <summary>Checks answered from the memo without reading the file.</summary>
    internal static long Reused => Interlocked.Read(ref _reused);
    /// <summary>Forgets every file; the next check of each is a first check.</summary>
    internal static void Reset() { Verified.Clear(); Interlocked.Exchange(ref _fullPasses, 0); Interlocked.Exchange(ref _reused, 0); }

    /// <summary>The memoized hash of <paramref name="path"/> when its present stamp is the one recorded with it, else null.
    /// Reads no file bytes.</summary>
    internal static string? Lookup(string path, FileStamp stamp, long length)
    {
        if (length < MinBytes || stamp.Size != length) return null;
        if (!Verified.TryGetValue(Path.GetFullPath(path), out var entry) || entry.Stamp != stamp || entry.Length != length) return null;
        Interlocked.Increment(ref _reused);
        return entry.Sha256;
    }

    /// <summary>Remembers a hash computed over a whole file whose stamp was <paramref name="stamp"/> before and after the pass.</summary>
    internal static void Record(string path, FileStamp stamp, long length, string sha256)
    {
        if (length < MinBytes || stamp.Size != length) return;
        Interlocked.Increment(ref _fullPasses);
        var key = Path.GetFullPath(path);
        if (Verified.Count >= MaxEntries && !Verified.ContainsKey(key)) return;
        Verified[key] = new(stamp, length, sha256);
    }

    /// <summary>The length and SHA-256 of the file at <paramref name="path"/>, from the memo when the file is unchanged since a
    /// full hash of it (see the type), else from a full read that is then remembered. Opens the file read-only with other readers
    /// allowed, as the plain read it replaces did.</summary>
    public static (long Length, string Sha256) Identity(string path, CancellationToken token = default)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
        var length = stream.Length;
        if (length >= MinBytes && AreaPathStream.TryStamp(stream.SafeFileHandle, out var stamp))
        {
            if (Lookup(path, stamp, length) is { } known) return (length, known);
            var (read, sha) = Hash(stream, token);
            // A file that grew, shrank or was rewritten while it was read reports what was read (its length then
            // disagrees with the caller's) and is not remembered.
            if (read == length && AreaPathStream.TryStamp(stream.SafeFileHandle, out var after) && after == stamp) Record(path, stamp, length, sha);
            return (read, sha);
        }
        var (total, digest) = Hash(stream, token);
        return (total, digest);
    }

    // Hashes to the end of the file, as the plain whole-stream hash this replaces did.
    private static (long Read, string Sha256) Hash(Stream stream, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536]; stream.Position = 0; long read = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var size = stream.Read(buffer, 0, buffer.Length);
            if (size == 0) break;
            read += size; hash.AppendData(buffer, 0, size);
        }
        return (read, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }
}

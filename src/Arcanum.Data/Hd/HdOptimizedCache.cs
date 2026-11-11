using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Arcanum.Data.Hd;

/// <summary>
/// ARC-DEV-376: the device-local store of optimized interface textures, named by the SHA-256 of the pack PNG they were made from, so a PNG the
/// owner edited has no entry and is converted again, and an unchanged one is never converted twice. Godot-free: it stores and returns bytes; the
/// viewer builds them (<c>Image.GenerateMipmaps</c> and <c>Image.GetData</c>, which is why the layout below is Godot's) and uploads them.
/// <para>A file is <c>root/&lt;sha[0..2]&gt;/&lt;sha&gt;.mip</c>: a 52-byte header (magic <c>ARCM</c>, version, format 1 = RGBA8 with the whole mip chain, width, height,
/// level count, payload length, the first 24 bytes of the source sha), then the payload: every level from the full size down to 1 × 1, each
/// <c>max(1, w &gt;&gt; i) × max(1, h &gt;&gt; i) × 4</c> bytes, one after the other. Written to a temporary name and renamed, so a half file is never
/// seen as a cache entry. A file whose header or length is not exactly what it should be is not an entry (it is converted and replaced).
/// The cache is never part of a pack: it depends on the GPU's data layout and is rebuilt on any device, so the iCloud copy and <c>mirror</c> never carry it.</para>
/// <para>ARC-DEV-382: beside the entries the cache keeps the <b>alpha verdicts</b> (<see cref="VerdictFile"/>): the alpha rule of a frame (<see cref="HdAlphaCheck"/>,
/// a per-texel pass over the whole 4× frame, about 75 ms for an 800 × 600 backdrop) is a pure function of the PNG, the Legacy frame it is checked against and
/// the policy, so a pass is remembered by those three and the rule's version, and a later bind of the same frame skips the pass. Only passes are kept. The
/// file is a bare list of 24-byte records (the first 8 bytes of the PNG's sha, then 16 bytes of the key's digest), appended as they happen; a torn tail
/// is cut back when the list is read, and a lost record (two processes appending at the same moment, or a compaction under a writer) only costs one more pass.</para>
/// </summary>
public sealed class HdOptimizedCache
{
    public const string Extension = ".mip";
    public const byte Version = 1;
    public const byte FormatRgba8Mips = 1;
    public const int HeaderBytes = 52;
    private static readonly byte[] Magic = "ARCM"u8.ToArray();

    public HdOptimizedCache(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = root;
    }

    public string Root { get; }

    /// <summary>The alpha verdict list (one flat file under the root; never an entry name, which are two-letter folders).</summary>
    public const string VerdictFile = "alpha-ok.v1";
    private const int VerdictRecordBytes = 24;
    // One gate per cache folder for the whole process (Attach makes a new cache object for the same folder whenever the pack object changes).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> VerdictGates = new(StringComparer.Ordinal);
    private object _verdictGate => VerdictGates.GetOrAdd(Path.GetFullPath(Root), _ => new object());
    private HashSet<(ulong, ulong)>? _verdicts;

    /// <summary>Levels of an RGBA8 mip chain down to 1 × 1 (Godot's count).</summary>
    public static int MipLevels(int width, int height)
    {
        var levels = 1;
        while (width > 1 || height > 1) { width = Math.Max(1, width >> 1); height = Math.Max(1, height >> 1); levels++; }
        return levels;
    }

    /// <summary>Bytes of the whole chain.</summary>
    public static long MipBytes(int width, int height)
    {
        long total = 0;
        while (true)
        {
            total += (long)width * height * 4;
            if (width == 1 && height == 1) return total;
            width = Math.Max(1, width >> 1); height = Math.Max(1, height >> 1);
        }
    }

    private static bool IsSha(string sha) => sha.Length == 64 && sha.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public string PathFor(string pngSha256)
    {
        if (!IsSha(pngSha256)) throw new ArgumentException("A lowercase hex SHA-256 is required.", nameof(pngSha256));
        return Path.Combine(Root, pngSha256[..2], pngSha256 + Extension);
    }

    private bool ReadHeader(FileStream stream, string sha, int width, int height, out int levels, out long payload)
    {
        levels = 0; payload = 0;
        Span<byte> head = stackalloc byte[HeaderBytes];
        if (stream.Length < HeaderBytes || stream.Read(head) != HeaderBytes) return false;
        if (!head[..4].SequenceEqual(Magic) || head[4] != Version || head[5] != FormatRgba8Mips) return false;
        if (BinaryPrimitives.ReadInt32LittleEndian(head[8..]) != width || BinaryPrimitives.ReadInt32LittleEndian(head[12..]) != height) return false;
        levels = BinaryPrimitives.ReadInt32LittleEndian(head[16..]);
        payload = BinaryPrimitives.ReadInt64LittleEndian(head[20..]);
        if (levels != MipLevels(width, height) || payload != MipBytes(width, height) || stream.Length != HeaderBytes + payload) return false;
        return SourceMatches(head, sha);
    }

    // The header ends with the first 24 bytes of the source sha (enough to catch a file copied under another name).
    private static bool SourceMatches(ReadOnlySpan<byte> head, string sha)
        => Convert.ToHexString(head[28..HeaderBytes]).Equals(sha[..48], StringComparison.OrdinalIgnoreCase);

    /// <summary>True when a complete entry for this PNG and size exists (header and file length only; the payload is not read).</summary>
    public bool Has(string pngSha256, int width, int height)
    {
        try
        {
            var path = PathFor(pngSha256);
            if (!File.Exists(path)) return false;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return ReadHeader(stream, pngSha256, width, height, out _, out _);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>The whole chain (level 0 first), or null when there is no complete entry.</summary>
    public byte[]? TryRead(string pngSha256, int width, int height)
    {
        try
        {
            var path = PathFor(pngSha256);
            if (!File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!ReadHeader(stream, pngSha256, width, height, out _, out var payload)) return null;
            var bytes = new byte[payload];
            stream.ReadExactly(bytes);
            return bytes;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Stores one chain (the exact <see cref="MipBytes"/> bytes); false when the bytes are not that chain or the file system refuses.</summary>
    public bool Write(string pngSha256, int width, int height, ReadOnlySpan<byte> chain)
    {
        if (width <= 0 || height <= 0 || chain.Length != MipBytes(width, height)) return false;
        string path;
        try { path = PathFor(pngSha256); } catch (ArgumentException) { return false; }
        var temp = path + $".{Environment.ProcessId}-{Environment.CurrentManagedThreadId}.part";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Span<byte> head = stackalloc byte[HeaderBytes];
            head.Clear();
            Magic.CopyTo(head);
            head[4] = Version; head[5] = FormatRgba8Mips;
            BinaryPrimitives.WriteInt32LittleEndian(head[8..], width);
            BinaryPrimitives.WriteInt32LittleEndian(head[12..], height);
            BinaryPrimitives.WriteInt32LittleEndian(head[16..], MipLevels(width, height));
            BinaryPrimitives.WriteInt64LittleEndian(head[20..], chain.Length);
            Convert.FromHexString(pngSha256[..48]).CopyTo(head[28..]);
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(head);
                stream.Write(chain);
                stream.Flush(flushToDisk: true); // before the rename, so a power loss leaves no entry with a valid header and a hollow payload
            }
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); } catch (Exception) { /* best effort */ }
            return false;
        }
    }

    /// <summary>Deletes entries (and leftover partial files) whose source sha is not in <paramref name="keep"/>: what an edited or removed PNG left behind.
    /// Returns the count removed. Only <c>.mip</c> and <c>.part</c> files directly under a two-letter folder of the cache root are touched.</summary>
    public int RemoveUnreferenced(IReadOnlySet<string> keep)
    {
        var removed = 0;
        if (!Directory.Exists(Root)) return 0;
        RemoveStaleVerdicts(keep);
        try
        {
            foreach (var folder in Directory.GetDirectories(Root).Where(d => Path.GetFileName(d).Length == 2))
            {
                foreach (var file in Directory.GetFiles(folder))
                {
                    var name = Path.GetFileName(file);
                    var isPart = name.EndsWith(".part", StringComparison.Ordinal);
                    var isMip = name.EndsWith(Extension, StringComparison.Ordinal) && name.Length == 64 + Extension.Length;
                    if (!isPart && !isMip) continue;
                    if (isMip && keep.Contains(name[..64])) continue;
                    File.Delete(file);
                    removed++;
                }
                if (Directory.GetFileSystemEntries(folder).Length == 0) Directory.Delete(folder);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* the next pass tries again */ }
        return removed;
    }

    /// <summary>The bytes the cache holds (entries only), for the log.</summary>
    public long SizeBytes()
    {
        if (!Directory.Exists(Root)) return 0;
        try { return Directory.EnumerateFiles(Root, "*" + Extension, SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return 0; }
    }

    // ---- alpha verdicts (ARC-DEV-382) ----

    private static (ulong Png, ulong A, ulong B) VerdictKey(string pngSha256, string legacyRgbaSha256, string policy)
    {
        if (!IsSha(pngSha256)) throw new ArgumentException("A lowercase hex SHA-256 is required.", nameof(pngSha256));
        var digest = SHA256.HashData(Encoding.ASCII.GetBytes($"{pngSha256}|{legacyRgbaSha256}|{policy}|rule{HdAlphaCheck.Version}"));
        return (BinaryPrimitives.ReadUInt64LittleEndian(Convert.FromHexString(pngSha256[..16])), BinaryPrimitives.ReadUInt64LittleEndian(digest), BinaryPrimitives.ReadUInt64LittleEndian(digest.AsSpan(8)));
    }

    private HashSet<(ulong, ulong)> Verdicts()
    {
        if (_verdicts is not null) return _verdicts;
        var set = new HashSet<(ulong, ulong)>();
        try
        {
            var path = Path.Combine(Root, VerdictFile);
            if (File.Exists(path))
            {
                var bytes = File.ReadAllBytes(path);
                for (var at = 0; at + VerdictRecordBytes <= bytes.Length; at += VerdictRecordBytes)
                    set.Add((BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(at + 8)), BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(at + 16))));
                if (bytes.Length % VerdictRecordBytes != 0)
                {
                    // A tail torn by a crash would shift every record appended after it: cut it back to a record boundary now.
                    ReplaceVerdictFile(path, bytes.AsSpan(0, bytes.Length - bytes.Length % VerdictRecordBytes).ToArray());
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* an unreadable list is an empty one */ }
        return _verdicts = set;
    }

    /// <summary>True when this PNG was already found to pass the alpha rule <paramref name="policy"/> (a short text such as <c>LegacySmooth:900</c>) against the
    /// Legacy frame with this RGBA hash.</summary>
    public bool AlphaVerdictKnown(string pngSha256, string legacyRgbaSha256, string policy)
    {
        var key = VerdictKey(pngSha256, legacyRgbaSha256, policy);
        lock (_verdictGate) return Verdicts().Contains((key.A, key.B));
    }

    /// <summary>Remembers a pass; false when the list cannot be written (the pass is then still kept for this process).</summary>
    public bool RecordAlphaVerdict(string pngSha256, string legacyRgbaSha256, string policy)
    {
        var key = VerdictKey(pngSha256, legacyRgbaSha256, policy);
        lock (_verdictGate)
        {
            if (!Verdicts().Add((key.A, key.B))) return true;
            try
            {
                Directory.CreateDirectory(Root);
                Span<byte> record = stackalloc byte[VerdictRecordBytes];
                BinaryPrimitives.WriteUInt64LittleEndian(record, key.Png);
                BinaryPrimitives.WriteUInt64LittleEndian(record[8..], key.A);
                BinaryPrimitives.WriteUInt64LittleEndian(record[16..], key.B);
                using var stream = new FileStream(Path.Combine(Root, VerdictFile), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                stream.Write(record);
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
        }
    }

    private static void ReplaceVerdictFile(string path, byte[] content)
    {
        var temp = path + $".{Environment.ProcessId}-{Environment.CurrentManagedThreadId}.part";
        try
        {
            File.WriteAllBytes(temp, content);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); } catch (Exception) { /* best effort */ }
            throw;
        }
    }

    /// <summary>Drops the verdicts of PNGs not in <paramref name="keep"/> (full SHA-256 names); returns how many went.</summary>
    private int RemoveStaleVerdicts(IReadOnlySet<string> keep)
    {
        var prefixes = keep.Where(IsSha).Select(sha => BinaryPrimitives.ReadUInt64LittleEndian(Convert.FromHexString(sha[..16]))).ToHashSet();
        lock (_verdictGate)
        {
            var path = Path.Combine(Root, VerdictFile);
            try
            {
                if (!File.Exists(path)) return 0;
                var bytes = File.ReadAllBytes(path);
                var kept = new List<byte>(bytes.Length);
                var removed = 0;
                for (var at = 0; at + VerdictRecordBytes <= bytes.Length; at += VerdictRecordBytes)
                {
                    if (prefixes.Contains(BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(at)))) kept.AddRange(bytes.AsSpan(at, VerdictRecordBytes).ToArray()); else removed++;
                }
                if (removed == 0 && bytes.Length % VerdictRecordBytes == 0) return 0;
                ReplaceVerdictFile(path, kept.ToArray());
                _verdicts = null; // read again, so a record another process appended meanwhile is not lost from memory
                return removed;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return 0; }
        }
    }
}

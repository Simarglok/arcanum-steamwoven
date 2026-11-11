using System.Buffers.Binary;

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
}

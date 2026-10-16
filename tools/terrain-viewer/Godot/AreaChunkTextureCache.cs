using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>GPU textures of composed terrain chunks, keyed by chunk and decimation
/// level. Chunks of the current submission are never evicted; others are evicted least
/// recently used first, down to three quarters of the limits, when a new chunk would
/// exceed <see cref="MaxBytes"/> or <see cref="MaxChunks"/>.</summary>
internal sealed class AreaChunkTextureCache : IDisposable
{
    internal const long MaxBytes = 192L * 1024 * 1024;
    internal const int MaxChunks = 1024;
    private sealed class Entry(ImageTexture texture, long bytes)
    {
        public ImageTexture Texture { get; } = texture;
        public long Bytes { get; } = bytes;
        public long LastUse { get; set; }
    }
    private readonly Dictionary<((int X, int Y) Chunk, int Level), Entry> _entries = new();
    private long _submission;
    public long Bytes { get; private set; }
    public int Count => _entries.Count;
    public long CreatedCount { get; private set; }
    public long DisposedCount { get; private set; }

    public void BeginSubmission() => _submission++;

    /// <summary>Whether this many chunks of one level fit the limits at all.</summary>
    public static bool Fits(int chunks, int level)
        => chunks <= MaxChunks && (long)chunks * AreaTerrainChunks.Side(level) * AreaTerrainChunks.Side(level) * 4 <= MaxBytes;

    public ImageTexture Acquire(AreaTerrainChunks terrain, (int X, int Y) chunk, int level)
    {
        if (_entries.TryGetValue((chunk, level), out var found)) { found.LastUse = _submission; return found.Texture; }
        var side = AreaTerrainChunks.Side(level); var bytes = (long)side * side * 4;
        if (Bytes + bytes > MaxBytes || _entries.Count + 1 > MaxChunks)
            EvictUnused(MaxBytes * 3 / 4 - bytes, MaxChunks * 3 / 4 - 1);
        if (Bytes + bytes > MaxBytes || _entries.Count + 1 > MaxChunks)
            throw new InvalidOperationException("AREA_TERRAIN_CHUNK_BUDGET");
        using var image = Image.CreateFromData(side, side, false, Image.Format.Rgba8, terrain.Compose(chunk, level));
        var entry = new Entry(ImageTexture.CreateFromImage(image), bytes) { LastUse = _submission };
        _entries.Add((chunk, level), entry); Bytes += bytes; CreatedCount++;
        return entry.Texture;
    }

    private void EvictUnused(long targetBytes, int targetChunks)
    {
        foreach (var (key, entry) in _entries.Where(pair => pair.Value.LastUse < _submission).OrderBy(pair => pair.Value.LastUse).ToArray())
        {
            if (Bytes <= targetBytes && _entries.Count <= targetChunks) break;
            _entries.Remove(key); Bytes -= entry.Bytes; entry.Texture.Dispose(); DisposedCount++;
        }
    }

    public void Clear()
    {
        foreach (var entry in _entries.Values) { entry.Texture.Dispose(); DisposedCount++; }
        _entries.Clear(); Bytes = 0;
    }

    public void Dispose() => Clear();
}

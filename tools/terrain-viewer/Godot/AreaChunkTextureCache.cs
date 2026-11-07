using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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

    /// <summary>Makes every chunk of a submission resident. Resident ones are marked used first so
    /// adding cannot evict them; missing ones are composed in parallel (pure CPU over the immutable
    /// plan and frames, byte-identical to <see cref="AreaTerrainChunks.Compose"/> one at a time), in
    /// batches of <see cref="ComposeBatch"/> to bound transient memory, then uploaded in order.</summary>
    public void Prepare(AreaTerrainChunks terrain, IReadOnlyList<(int X, int Y)> chunks, int level)
    {
        var missing = new List<(int X, int Y)>();
        foreach (var chunk in chunks)
            if (_entries.TryGetValue((chunk, level), out var found)) found.LastUse = _submission;
            else if (!missing.Contains(chunk)) missing.Add(chunk);
        for (var start = 0; start < missing.Count; start += ComposeBatch)
        {
            var batch = missing.Skip(start).Take(ComposeBatch).ToArray(); var composed = new byte[batch.Length][];
            if (batch.Length == 1) composed[0] = terrain.Compose(batch[0], level);
            else
            {
                try
                {
                    Parallel.For(0, batch.Length, new ParallelOptions { MaxDegreeOfParallelism = System.Environment.ProcessorCount },
                        index => composed[index] = terrain.Compose(batch[index], level));
                }
                // Keep the failure category of a sequential compose for the caller's handlers.
                catch (AggregateException error) when (error.InnerExceptions.Count == 1)
                { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerExceptions[0]).Throw(); }
            }
            for (var index = 0; index < batch.Length; index++) Add(batch[index], level, composed[index]);
        }
    }
    internal const int ComposeBatch = 32;

    public ImageTexture Acquire(AreaTerrainChunks terrain, (int X, int Y) chunk, int level)
    {
        if (_entries.TryGetValue((chunk, level), out var found)) { found.LastUse = _submission; return found.Texture; }
        return Add(chunk, level, terrain.Compose(chunk, level));
    }

    private ImageTexture Add((int X, int Y) chunk, int level, byte[] rgba)
    {
        var side = AreaTerrainChunks.Side(level); var bytes = (long)side * side * 4;
        if (rgba.Length != bytes) throw new InvalidOperationException("AREA_TERRAIN_CHUNK_SIZE");
        if (Bytes + bytes > MaxBytes || _entries.Count + 1 > MaxChunks)
            EvictUnused(MaxBytes * 3 / 4 - bytes, MaxChunks * 3 / 4 - 1);
        if (Bytes + bytes > MaxBytes || _entries.Count + 1 > MaxChunks)
            throw new InvalidOperationException("AREA_TERRAIN_CHUNK_BUDGET");
        using var image = Image.CreateFromData(side, side, false, Image.Format.Rgba8, rgba);
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

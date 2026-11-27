using Arcanum.Data.Area;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>One drawn chunk texture: the whole chunk when all its terrain is on one light channel, otherwise
/// (ARC-RENDER-3438, <paramref name="Split"/>) the layer of <paramref name="Channel"/>.</summary>
internal readonly record struct AreaChunkPart((int X, int Y) Chunk, AreaAmbientChannel Channel, bool Split);

/// <summary>GPU textures of composed terrain chunks, keyed by chunk, decimation level and,
/// for a split chunk, light channel. Textures of the current submission are never evicted;
/// others are evicted least recently used first, down to three quarters of the limits, when
/// a new one would exceed <see cref="MaxBytes"/> or <see cref="MaxChunks"/>. Channel layers
/// are dropped when a submission brings another channel table.</summary>
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
    private readonly record struct Key((int X, int Y) Chunk, int Level, AreaAmbientChannel? Layer);
    private static Key KeyOf(AreaChunkPart part, int level) => new(part.Chunk, level, part.Split ? part.Channel : null);
    private readonly Dictionary<Key, Entry> _entries = new();
    // The channel table the resident layers were composed by.
    private IReadOnlyDictionary<AreaVisualKey, AreaAmbientChannel>? _layerChannels;
    private long _submission;
    public long Bytes { get; private set; }
    public int Count => _entries.Count;
    public long CreatedCount { get; private set; }
    public long DisposedCount { get; private set; }

    public void BeginSubmission() => _submission++;

    /// <summary>Whether this many textures of one level fit the limits at all.</summary>
    public static bool Fits(int chunks, int level)
        => chunks <= MaxChunks && (long)chunks * AreaTerrainChunks.Side(level) * AreaTerrainChunks.Side(level) * 4 <= MaxBytes;

    /// <summary>Makes every texture of a submission resident. Resident ones are marked used first so
    /// adding cannot evict them; missing ones are composed in parallel (pure CPU over the immutable
    /// plan, frames and channel table, byte-identical to <see cref="AreaTerrainChunks.Compose"/> one at a time), in
    /// batches of <see cref="ComposeBatch"/> to bound transient memory, then uploaded in order.
    /// <paramref name="channels"/> is read for split parts only.</summary>
    public void Prepare(AreaTerrainChunks terrain, IReadOnlyList<AreaChunkPart> parts, int level,
        IReadOnlyDictionary<AreaVisualKey, AreaAmbientChannel>? channels)
    {
        UseChannels(channels);
        var missing = new List<AreaChunkPart>();
        foreach (var part in parts)
            if (_entries.TryGetValue(KeyOf(part, level), out var found)) found.LastUse = _submission;
            else if (!missing.Contains(part)) missing.Add(part);
        for (var start = 0; start < missing.Count; start += ComposeBatch)
        {
            var batch = missing.Skip(start).Take(ComposeBatch).ToArray(); var composed = new byte[batch.Length][];
            if (batch.Length == 1) composed[0] = Compose(terrain, batch[0], level);
            else
            {
                try
                {
                    Parallel.For(0, batch.Length, new ParallelOptions { MaxDegreeOfParallelism = System.Environment.ProcessorCount },
                        index => composed[index] = Compose(terrain, batch[index], level));
                }
                // Keep the failure category of a sequential compose for the caller's handlers.
                catch (AggregateException error) when (error.InnerExceptions.Count == 1)
                { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerExceptions[0]).Throw(); }
            }
            for (var index = 0; index < batch.Length; index++) Add(KeyOf(batch[index], level), composed[index]);
        }
    }
    internal const int ComposeBatch = 32;

    public ImageTexture Acquire(AreaTerrainChunks terrain, AreaChunkPart part, int level)
    {
        var key = KeyOf(part, level);
        if (_entries.TryGetValue(key, out var found)) { found.LastUse = _submission; return found.Texture; }
        return Add(key, Compose(terrain, part, level));
    }

    private byte[] Compose(AreaTerrainChunks terrain, AreaChunkPart part, int level)
    {
        if (!part.Split) return terrain.Compose(part.Chunk, level);
        var channels = _layerChannels ?? throw new InvalidOperationException("AREA_TERRAIN_CHUNK_CHANNELS");
        return terrain.Compose(part.Chunk, level, command => channels[command.Visual] == part.Channel);
    }

    private void UseChannels(IReadOnlyDictionary<AreaVisualKey, AreaAmbientChannel>? channels)
    {
        // Without a table no part is split, so the layers kept for the last table stay valid for its return.
        if (channels is null || ReferenceEquals(channels, _layerChannels)) return;
        _layerChannels = channels;
        foreach (var (key, entry) in _entries.Where(pair => pair.Key.Layer is not null).ToArray()) Remove(key, entry);
    }

    private ImageTexture Add(Key key, byte[] rgba)
    {
        var side = AreaTerrainChunks.Side(key.Level); var bytes = (long)side * side * 4;
        if (rgba.Length != bytes) throw new InvalidOperationException("AREA_TERRAIN_CHUNK_SIZE");
        if (Bytes + bytes > MaxBytes || _entries.Count + 1 > MaxChunks)
            EvictUnused(MaxBytes * 3 / 4 - bytes, MaxChunks * 3 / 4 - 1);
        if (Bytes + bytes > MaxBytes || _entries.Count + 1 > MaxChunks)
            throw new InvalidOperationException("AREA_TERRAIN_CHUNK_BUDGET");
        using var image = Image.CreateFromData(side, side, false, Image.Format.Rgba8, rgba);
        var entry = new Entry(ImageTexture.CreateFromImage(image), bytes) { LastUse = _submission };
        _entries.Add(key, entry); Bytes += bytes; CreatedCount++;
        return entry.Texture;
    }

    private void Remove(Key key, Entry entry)
    {
        _entries.Remove(key); Bytes -= entry.Bytes; entry.Texture.Dispose(); DisposedCount++;
    }

    private void EvictUnused(long targetBytes, int targetChunks)
    {
        foreach (var (key, entry) in _entries.Where(pair => pair.Value.LastUse < _submission).OrderBy(pair => pair.Value.LastUse).ToArray())
        {
            if (Bytes <= targetBytes && _entries.Count <= targetChunks) break;
            Remove(key, entry);
        }
    }

    public void Clear()
    {
        foreach (var entry in _entries.Values) { entry.Texture.Dispose(); DisposedCount++; }
        _entries.Clear(); Bytes = 0; _layerChannels = null;
    }

    public void Dispose() => Clear();
}

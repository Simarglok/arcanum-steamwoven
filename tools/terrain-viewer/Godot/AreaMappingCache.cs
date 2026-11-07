using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>One exact two-row nearest lookup: X row, Y row and their envelope lengths.</summary>
internal readonly record struct AreaMappingKey(AreaNearestRowKey X, AreaNearestRowKey Y, int Width, int Height);

/// <summary>Index-row textures shared by every draw item with the same translation-invariant
/// key, so camera pans reuse rows instead of rebuilding one per item. Rows used by the
/// current submission are never evicted; unused rows are evicted least recently used
/// first. Live row payload stays within <see cref="AreaDrawItem.MaxMappingBytes"/> and the
/// row count within <see cref="MaxRows"/>; eviction then frees down to three quarters
/// of both so that a zoom change does not evict once per miss. ARC-RENDER-032: the row count
/// allows every command of a main-map window at once (at most about 42,000: 36,864 tiles, the
/// bodies, masks and roofs). At a zoom that is not a power of two the terrain has no reduced
/// chunks and is drawn one item per tile, so far out on a large display nearly every visible item
/// has its own row of 100 to 200 bytes (2560 × 1440 at 0.1875 needed more than 8,192 with 1.1 MB):
/// the row count binds there, not the payload, and each row is a texture.</summary>
internal sealed class AreaMappingCache : IDisposable
{
    internal const int MaxRowLength = 8192;
    internal const int MaxRows = 65536;
    private sealed class Entry(ImageTexture texture, long bytes)
    {
        public ImageTexture Texture { get; } = texture;
        public long Bytes { get; } = bytes;
        public long LastUse { get; set; }
    }
    private readonly Dictionary<AreaMappingKey, Entry> _entries = new();
    private long _submission;
    public long Bytes { get; private set; }
    public int Count => _entries.Count;
    public long CreatedCount { get; private set; }

    /// <summary>Starts a submission: rows acquired after this call are in use until the next one.</summary>
    public void BeginSubmission() => _submission++;

    public ImageTexture Acquire(AreaMappingKey key)
    {
        if (_entries.TryGetValue(key, out var found)) { found.LastUse = _submission; return found.Texture; }
        if (key.Width <= 0 || key.Height <= 0 || key.Width > MaxRowLength || key.Height > MaxRowLength)
            throw new InvalidOperationException("AREA_NEAREST_MAPPING_DOMAIN");
        var width = Math.Max(key.Width, key.Height);
        var bytes = checked(width * 8L);
        if (Bytes + bytes > AreaDrawItem.MaxMappingBytes || _entries.Count + 1 > MaxRows)
            EvictUnused(AreaDrawItem.MaxMappingBytes * 3 / 4 - bytes, MaxRows * 3 / 4 - 1);
        if (Bytes + bytes > AreaDrawItem.MaxMappingBytes || _entries.Count + 1 > MaxRows)
            throw new InvalidOperationException("AREA_NEAREST_MAPPING_BUDGET");
        var rgba = new byte[bytes];
        void Encode(int at, int value)
        {
            var code = value + 1; rgba[at] = (byte)(code & 255); rgba[at + 1] = (byte)(code >> 8); rgba[at + 3] = 255;
        }
        for (var i = 0; i < key.Width; i++) Encode(i * 4, key.X.SourceIndex(i));
        for (var i = 0; i < key.Height; i++) Encode((width + i) * 4, key.Y.SourceIndex(i));
        using var image = Image.CreateFromData(width, 2, false, Image.Format.Rgba8, rgba);
        var entry = new Entry(ImageTexture.CreateFromImage(image), bytes) { LastUse = _submission };
        _entries.Add(key, entry); Bytes += bytes; CreatedCount++;
        return entry.Texture;
    }

    /// <summary>Drops every row not acquired in the current submission.</summary>
    public void DropUnused() => EvictUnused(0, 0);

    private void EvictUnused(long targetBytes, int targetRows)
    {
        if (Bytes <= targetBytes && _entries.Count <= targetRows) return;
        foreach (var (key, entry) in _entries.Where(pair => pair.Value.LastUse < _submission).OrderBy(pair => pair.Value.LastUse).ToArray())
        {
            if (Bytes <= targetBytes && _entries.Count <= targetRows) break;
            _entries.Remove(key); Bytes -= entry.Bytes; entry.Texture.Dispose();
        }
    }

    public void Clear()
    {
        foreach (var entry in _entries.Values) entry.Texture.Dispose();
        _entries.Clear(); Bytes = 0;
    }

    public void Dispose() => Clear();
}

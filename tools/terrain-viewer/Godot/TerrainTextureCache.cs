using Godot;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer;

public sealed class TerrainTextureCache : IDisposable
{
    private readonly Dictionary<TerrainTextureKey, ImageTexture> _textures = new();
    private readonly Dictionary<StaticSceneryTextureKey, ImageTexture> _staticTextures = new();
    private readonly Dictionary<ActorDiagnosticTextureKey, ImageTexture> _actorTextures = new();
    private readonly Dictionary<AreaFrameKey, ImageTexture> _areaTextures = new();
    public int AreaCount => _areaTextures.Count;
    public long AreaRgbaBytes { get; private set; }
    private readonly Func<Image, ImageTexture> _createTexture;
    private readonly Action<ImageTexture> _disposeTexture;

    /// <summary>
    /// The two delegates are a deliberately narrow ownership seam. Production
    /// uses Godot's normal factory/disposal; the graphical self-test can make
    /// upload N fail deterministically and count every disposal.
    /// </summary>
    public TerrainTextureCache(Func<Image, ImageTexture>? createTexture = null, Action<ImageTexture>? disposeTexture = null)
    {
        _createTexture = createTexture ?? ImageTexture.CreateFromImage;
        _disposeTexture = disposeTexture ?? (texture => texture.Dispose());
    }

    public int Count => _textures.Count + _staticTextures.Count + _actorTextures.Count + _areaTextures.Count;
    public int CreatedCount { get; private set; }
    public int DisposedCount { get; private set; }

    public void Upload(IReadOnlyDictionary<TerrainTextureKey, DecodedTerrainTexture> decoded)
    {
        ArgumentNullException.ThrowIfNull(decoded);
        var uploaded = new List<TerrainTextureKey>();
        try
        {
            foreach (var item in decoded)
            {
                if (_textures.ContainsKey(item.Key)) continue;
                using var image = Image.CreateFromData(item.Value.Width, item.Value.Height, false, Image.Format.Rgba8, item.Value.Rgba);
                var texture = _createTexture(image);
                CreatedCount++;
                _textures.Add(item.Key, texture);
                uploaded.Add(item.Key);
            }
        }
        catch
        {
            foreach (var key in uploaded)
            {
                DisposeTexture(_textures[key]);
                _textures.Remove(key);
            }
            throw;
        }
    }

    public bool TryGet(TerrainTextureKey key, out ImageTexture texture) => _textures.TryGetValue(key, out texture!);
    public bool TryGet(StaticSceneryTextureKey key, out ImageTexture texture) => _staticTextures.TryGetValue(key, out texture!);
    public bool TryGet(ActorDiagnosticTextureKey key, out ImageTexture texture) => _actorTextures.TryGetValue(key, out texture!);
    public bool TryGet(AreaFrameKey key, out ImageTexture texture) => _areaTextures.TryGetValue(key, out texture!);

    /// <summary>All-or-fail visible residency, from the bounded CPU repository.
    /// No lease/decode runs on pan. Dispose offscreen GPU allocations first.</summary>
    public void UploadAreaVisible(IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> frames, IReadOnlySet<AreaFrameKey> visible)
    {
        var bytes = visible.Sum(key => checked((long)frames[key].Width * frames[key].Height * 4));
        if (visible.Count > AreaRepositoryPolicy.MaxFrames || bytes > AreaRepositoryPolicy.MaxCpuBytes) throw new InvalidOperationException("AREA_GPU_BUDGET");
        foreach (var key in _areaTextures.Keys.Where(key => !visible.Contains(key)).ToArray())
        { DisposeTexture(_areaTextures[key]); _areaTextures.Remove(key); }
        var uploaded = new List<AreaFrameKey>();
        try
        {
            foreach (var key in visible)
            {
                if (_areaTextures.ContainsKey(key)) continue;
                var frame = frames[key];
                if (frame.Rgba.Length != checked(frame.Width * frame.Height * 4)) throw new InvalidOperationException("AREA_RGBA_LENGTH");
                using var image = Image.CreateFromData(frame.Width, frame.Height, false, Image.Format.Rgba8, frame.Rgba);
                var texture = _createTexture(image); CreatedCount++;
                _areaTextures.Add(key, texture); uploaded.Add(key);
            }
            AreaRgbaBytes = bytes;
        }
        catch
        {
            foreach (var key in uploaded) { DisposeTexture(_areaTextures[key]); _areaTextures.Remove(key); }
            AreaRgbaBytes = _areaTextures.Keys.Sum(key => checked((long)frames[key].Width * frames[key].Height * 4));
            throw;
        }
    }

    public bool TryFindOpaquePixel(StaticSceneryTextureKey key, out TerrainTexturePixel pixel)
        => TryFindOpaquePixel(key, static _ => true, out pixel);

    public bool TryFindOpaquePixel(StaticSceneryTextureKey key, Func<TerrainTexturePixel, bool> accept, out TerrainTexturePixel pixel)
    {
        pixel = default;
        ArgumentNullException.ThrowIfNull(accept);
        if (!_staticTextures.TryGetValue(key, out var texture)) return false;
        using var image = texture.GetImage();
        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var color = image.GetPixel(x, y);
                // A full-scene landmark must be fully opaque: a merely
                // translucent ART pixel is correctly blended with terrain and
                // cannot be compared to its decoded straight-RGBA value.
                if (color.A < 1f) continue;
                var candidate = new TerrainTexturePixel(x, y, (byte)Math.Round(color.R * 255), (byte)Math.Round(color.G * 255),
                    (byte)Math.Round(color.B * 255), (byte)Math.Round(color.A * 255));
                if (!accept(candidate)) continue;
                pixel = candidate;
                return true;
            }
        }
        return false;
    }

    public void UploadStatic(IReadOnlyDictionary<StaticSceneryTextureKey, DecodedStaticTexture> decoded)
    {
        ArgumentNullException.ThrowIfNull(decoded);
        var uploaded = new List<StaticSceneryTextureKey>();
        try
        {
            foreach (var item in decoded)
            {
                if (_staticTextures.ContainsKey(item.Key)) continue;
                using var image = Image.CreateFromData(item.Value.Width, item.Value.Height, false, Image.Format.Rgba8, item.Value.Rgba);
                var texture = _createTexture(image);
                CreatedCount++;
                _staticTextures.Add(item.Key, texture);
                uploaded.Add(item.Key);
            }
        }
        catch
        {
            foreach (var key in uploaded) { DisposeTexture(_staticTextures[key]); _staticTextures.Remove(key); }
            throw;
        }
    }

    public void UploadActor(IReadOnlyDictionary<ActorDiagnosticTextureKey, DecodedActorTexture> decoded)
    {
        ArgumentNullException.ThrowIfNull(decoded);
        var uploaded = new List<ActorDiagnosticTextureKey>();
        try
        {
            foreach (var item in decoded)
            {
                if (_actorTextures.ContainsKey(item.Key)) continue;
                using var image = Image.CreateFromData(item.Value.Width, item.Value.Height, false, Image.Format.Rgba8, item.Value.Rgba);
                var texture = _createTexture(image);
                CreatedCount++;
                _actorTextures.Add(item.Key, texture);
                uploaded.Add(item.Key);
            }
        }
        catch
        {
            foreach (var key in uploaded) { DisposeTexture(_actorTextures[key]); _actorTextures.Remove(key); }
            throw;
        }
    }

    public bool TryFindOpaquePixel(TerrainTextureKey key, TerrainRect source, out TerrainTexturePixel pixel)
        => TryFindOpaquePixel(key, source, static _ => true, out pixel);

    public bool TryFindOpaquePixel(TerrainTextureKey key, TerrainRect source, Func<TerrainTexturePixel, bool> accept, out TerrainTexturePixel pixel)
    {
        pixel = default;
        ArgumentNullException.ThrowIfNull(accept);
        if (!TryGet(key, out var texture)) return false;
        using var image = texture.GetImage();
        for (var y = source.Y; y < source.Y + source.Height; y++)
        {
            for (var x = source.X; x < source.X + source.Width; x++)
            {
                var color = image.GetPixel(x, y);
                if (color.A < 1f) continue;
                var candidate = new TerrainTexturePixel(x, y, (byte)Math.Round(color.R * 255), (byte)Math.Round(color.G * 255),
                    (byte)Math.Round(color.B * 255), (byte)Math.Round(color.A * 255));
                if (!accept(candidate)) continue;
                pixel = candidate;
                return true;
            }
        }
        return false;
    }

    public void Clear()
    {
        foreach (var texture in _textures.Values) DisposeTexture(texture);
        foreach (var texture in _staticTextures.Values) DisposeTexture(texture);
        foreach (var texture in _actorTextures.Values) DisposeTexture(texture);
        foreach (var texture in _areaTextures.Values) DisposeTexture(texture);
        _textures.Clear();
        _staticTextures.Clear();
        _actorTextures.Clear();
        _areaTextures.Clear(); AreaRgbaBytes = 0;
    }

    public void Dispose() => Clear();

    private void DisposeTexture(ImageTexture texture)
    {
        _disposeTexture(texture);
        DisposedCount++;
    }
}

public readonly record struct TerrainTexturePixel(int X, int Y, byte R, byte G, byte B, byte A);

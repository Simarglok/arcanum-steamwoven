using Godot;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer;

public sealed class TerrainTextureCache : IDisposable
{
    private readonly Dictionary<TerrainTextureKey, ImageTexture> _textures = new();
    public int Count => _textures.Count;

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
                _textures.Add(item.Key, ImageTexture.CreateFromImage(image));
                uploaded.Add(item.Key);
            }
        }
        catch
        {
            foreach (var key in uploaded)
            {
                _textures[key].Dispose();
                _textures.Remove(key);
            }
            throw;
        }
    }

    public bool TryGet(TerrainTextureKey key, out ImageTexture texture) => _textures.TryGetValue(key, out texture!);

    public bool TryFindOpaquePixel(TerrainTextureKey key, TerrainRect source, out TerrainTexturePixel pixel)
    {
        pixel = default;
        if (!TryGet(key, out var texture)) return false;
        using var image = texture.GetImage();
        for (var y = source.Y; y < source.Y + source.Height; y++)
        for (var x = source.X; x < source.X + source.Width; x++)
        {
            var color = image.GetPixel(x, y);
            if (color.A <= 0) continue;
            pixel = new TerrainTexturePixel(x, y, (byte)Math.Round(color.R * 255), (byte)Math.Round(color.G * 255),
                (byte)Math.Round(color.B * 255), (byte)Math.Round(color.A * 255));
            return true;
        }
        return false;
    }

    public void Clear()
    {
        foreach (var texture in _textures.Values) texture.Dispose();
        _textures.Clear();
    }

    public void Dispose() => Clear();
}

public readonly record struct TerrainTexturePixel(int X, int Y, byte R, byte G, byte B, byte A);

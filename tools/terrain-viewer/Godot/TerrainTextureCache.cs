using Godot;
using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer;

public sealed class TerrainTextureCache : IDisposable
{
    private readonly Dictionary<TerrainTextureKey, ImageTexture> _textures = new();
    public int Count => _textures.Count;

    public void Upload(IReadOnlyDictionary<TerrainTextureKey, DecodedTerrainTexture> decoded)
    {
        ArgumentNullException.ThrowIfNull(decoded);
        foreach (var item in decoded)
        {
            if (_textures.ContainsKey(item.Key)) continue;
            using var image = Image.CreateFromData(item.Value.Width, item.Value.Height, false, Image.Format.Rgba8, item.Value.Rgba);
            _textures.Add(item.Key, ImageTexture.CreateFromImage(image));
        }
    }

    public bool TryGet(TerrainTextureKey key, out ImageTexture texture) => _textures.TryGetValue(key, out texture!);

    public void Clear()
    {
        foreach (var texture in _textures.Values) texture.Dispose();
        _textures.Clear();
    }

    public void Dispose() => Clear();
}

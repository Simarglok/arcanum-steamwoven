using Arcanum.Data.Characters;
using Arcanum.Data.Hd;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-RENDER-3106: the texture of a town-map tile the viewer read from the game (<c>townmap/&lt;town&gt;/&lt;town&gt;NNNNNN.bmp</c>, 64 × 32). The tile's pixels are decoded by
/// <see cref="PortraitBitmap"/> (the pixels the HD pack's <c>townmap</c> entries are bound to), the texture keeps the tile's own size, so the grid of the town map (<c>AreaTownMapPlan</c>,
/// the scroll, <c>ToPixel</c>, the fog cell, the clicks) stays in the original's coordinates whatever the pack holds, and with the HD setting on and a pack entry for that tile
/// (<see cref="HdTownMapKey"/>) the same texture draws the pack's redraw into the same 64 × 32 rectangle, from the area copy of the window's device scale
/// (<see cref="HdInterfaceTexture.AreaFirst"/>): the redraw is 256 × 128 and a 1:1 draw of it on a 1080p screen (0.45 device pixels per picture pixel) rings. A tile the pack has no usable redraw
/// for draws as it always did; a tile the reader does not take, or one that is not 64 × 32 (a mod's), is decoded by Godot as before and has no HD question.
/// </summary>
public static class TownMapHd
{
    /// <summary>The texture of a read tile (<paramref name="path"/> is its <c>townmap/&lt;town&gt;/&lt;town&gt;NNNNNN.bmp</c>, null when unknown), or null when it did not decode.</summary>
    public static Texture2D? Texture(string? path, byte[] bytes)
    {
        if (path is not null && HdTownMapKey.TryPath(path, out _) && PortraitBitmap.Decode(bytes) is { Width: HdTownMapKey.TileWidth, Height: HdTownMapKey.TileHeight } bitmap
            && InterfaceHd.Texture(new AreaInterfaceFrame(bitmap.Width, bitmap.Height, bitmap.Rgba, Hd: HdTownMapKey.Query(path, bitmap.Width, bitmap.Height, bitmap.Rgba))) is { } texture)
        {
            if (texture is HdInterfaceTexture hd) hd.AreaFirst = true;
            return texture;
        }
        var image = new Image();
        if (image.LoadBmpFromBuffer(bytes) != Error.Ok || image.IsEmpty()) { image.Dispose(); return null; }
        image.Convert(Image.Format.Rgba8);
        var legacy = ImageTexture.CreateFromImage(image);
        image.Dispose();
        return legacy;
    }

    /// <summary>The smoke's check of one drawn town-map tile against <paramref name="pack"/>: the texture keeps 64 × 32, and when the pack holds an entry for exactly this tile (same path key
    /// and Legacy pixels) the texture draws HD (from the pack's redraw or its area copy). A texture with no HD question, or a tile the pack has no entry for, is fine and says so.</summary>
    internal static (bool Ok, string Detail) Check(Texture2D? texture, HdPack pack)
    {
        if (texture is not HdInterfaceTexture hd || hd.FrameForBoard?.Hd is not { } query) return (true, "no HD question for this tile");
        if (hd.GetWidth() != HdTownMapKey.TileWidth || hd.GetHeight() != HdTownMapKey.TileHeight) return (false, $"size {hd.GetWidth()}x{hd.GetHeight()}, expected {HdTownMapKey.TileWidth}x{HdTownMapKey.TileHeight}");
        if (!pack.Entries.Any(e => e.Kind == HdEntryKind.TownMap && e.Key == query.Key && e.Legacy == query.Legacy)) return (true, "the pack has no entry for this tile: Legacy");
        return hd.IsHd && (hd.HdScale == pack.Scale || hd.IsAreaResampled) ? (true, "HD") : (false, "the pack holds an entry for this tile but the texture does not draw HD");
    }
}

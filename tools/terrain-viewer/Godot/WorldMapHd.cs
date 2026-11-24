using Arcanum.Data.Characters;
using Arcanum.Data.Hd;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-RENDER-3079: the texture of a world-map bitmap the viewer read from the game (one of the 64 tiles of <c>worldmap/</c>, 250 × 250, or the continent picture
/// <c>Map_Zoomed</c>, 365 × 365). The bitmap's pixels are decoded by <see cref="PortraitBitmap"/> (the pixels the HD pack's <c>worldmap</c> and <c>map-zoomed</c> entries are
/// bound to), the texture keeps the bitmap's own size, so every draw rectangle, dot, hover box and click of the window stays in the original's coordinates, and with the HD
/// setting on and a pack entry for that bitmap (<see cref="HdMapKey"/>) the same texture draws the pack's redraw (<see cref="InterfaceHd.Texture(AreaInterfaceFrame)"/>) into the
/// same rectangle (a tile at <c>Scale × 250</c>, <c>Map_Zoomed</c> at <c>Scale × 365</c>), per file: a bitmap the pack has no usable redraw for draws as it always did. A bitmap the
/// reader does not take (or one that is not 250 × 250 or 365 × 365) is decoded by Godot as before and has no HD question.
/// </summary>
public static class WorldMapHd
{
    /// <summary>The texture of a read bitmap (<paramref name="path"/> is its <c>worldmap/&lt;name&gt;.bmp</c>, null when unknown), or null when it did not decode.</summary>
    public static Texture2D? Texture(string? path, byte[] bytes)
    {
        if (path is not null && HdMapKey.TryPath(path, out var key) && PortraitBitmap.Decode(bytes) is { } bitmap && HdMapKey.KindOf(bitmap.Width, bitmap.Height) is not null)
            return InterfaceHd.Texture(new AreaInterfaceFrame(bitmap.Width, bitmap.Height, bitmap.Rgba, Hd: HdMapKey.Query(key, bitmap.Width, bitmap.Height, bitmap.Rgba)));
        var image = new Image();
        if (image.LoadBmpFromBuffer(bytes) != Error.Ok || image.IsEmpty()) return null;
        image.Convert(Image.Format.Rgba8);
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>ARC-UI-3154: true when <paramref name="tiles"/> is not empty and every tile is a world-map texture that <paramref name="pack"/> holds a redraw entry for (same path key and Legacy pixels).
    /// Only then does the map view draw the tiles at the composition's scale; a missing or partial set keeps the 1:1 stopgap of ARC-UI-2993.</summary>
    internal static bool AllTilesHaveEntries(IReadOnlyList<Texture2D> tiles, HdPack pack)
    {
        if (tiles.Count == 0) return false;
        var entries = new HashSet<(HdSpriteKey? Key, HdLegacyFrame Legacy)>(
            pack.Entries.Where(e => e.Kind == HdEntryKind.WorldMap && e.Key is not null).Select(e => (e.Key, e.Legacy)));
        return tiles.All(t => t is HdInterfaceTexture { FrameForBoard.Hd: { } q } && entries.Contains((q.Key, q.Legacy)));
    }

    /// <summary>The smoke's check of one drawn world-map texture against <paramref name="pack"/>: the texture keeps <paramref name="size"/> (250 or 365), and when the pack holds an
    /// entry for exactly this bitmap (same path key and Legacy pixels) the texture draws HD at the pack's scale. A texture with no HD question, or a bitmap the pack has no entry for, is fine and says so.</summary>
    internal static (bool Ok, string Detail) Check(Texture2D? texture, HdPack pack, int size)
    {
        if (texture is not HdInterfaceTexture hd || hd.FrameForBoard?.Hd is not { } query) return (true, "no HD question for this bitmap");
        if (hd.GetWidth() != size || hd.GetHeight() != size) return (false, $"size {hd.GetWidth()}x{hd.GetHeight()}, expected {size}x{size}");
        if (!pack.Entries.Any(e => e.Kind is HdEntryKind.WorldMap or HdEntryKind.MapZoomed && e.Key == query.Key && e.Legacy == query.Legacy)) return (true, "the pack has no entry for this bitmap: Legacy");
        return hd.IsHd && (hd.HdScale == pack.Scale || hd.IsAreaResampled) ? (true, "HD") : (false, "the pack holds an entry for this bitmap but the texture does not draw HD");
    }
}

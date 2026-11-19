using Arcanum.Data.Characters;
using Arcanum.Data.Hd;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-2865: the texture of a portrait the viewer read from the game (<see cref="AreaPortraitFile"/>). The bitmap's pixels are decoded by
/// <see cref="PortraitBitmap"/> (the pixels the HD pack's <c>portrait</c> entries are bound to), the texture keeps the bitmap's own size (64 or 128) so every draw
/// site keeps its size, and with the HD setting on and a pack entry for that bitmap (<see cref="HdPortraitKey"/>) the same texture draws the pack's
/// redraw (<see cref="InterfaceHd.Texture(AreaInterfaceFrame)"/>), per file: a bitmap the pack has no usable redraw for draws as it always did. A bitmap
/// the portrait reader does not take is decoded by Godot as before and has no HD question.
/// </summary>
public static class PortraitHd
{
    /// <summary>The texture of a read portrait, or null when the file did not read or decode.</summary>
    public static Texture2D? Texture(AreaPortraitFile? file)
    {
        if (file is not { Status: AreaPortraitStatus.Ready, Bytes: { } bytes }) return null;
        if (file.Path is { } path && HdPortraitKey.TryPath(path, out var key) && PortraitBitmap.Decode(bytes) is { } bitmap)
            return InterfaceHd.Texture(new AreaInterfaceFrame(bitmap.Width, bitmap.Height, bitmap.Rgba, Hd: HdPortraitKey.Query(key, bitmap.Width, bitmap.Height, bitmap.Rgba)));
        var image = new Image();
        return image.LoadBmpFromBuffer(bytes) == Error.Ok && !image.IsEmpty() ? ImageTexture.CreateFromImage(image) : null;
    }

    /// <summary>The smoke's check of one drawn portrait texture against <paramref name="pack"/>: the texture keeps <paramref name="size"/> (64 or 128), and when the pack holds an entry for
    /// exactly this bitmap (same path key and Legacy pixels) the texture draws HD at the pack's scale. A texture with no HD question (an icon, no portrait, a bitmap the portrait
    /// reader does not take) or a bitmap the pack has no entry for is fine and says so.</summary>
    internal static (bool Ok, string Detail) Check(Texture2D? texture, HdPack pack, int size)
    {
        if (texture is not HdInterfaceTexture hd || hd.FrameForBoard?.Hd is not { } query) return (true, "no HD question for this portrait");
        if (hd.GetWidth() != size || hd.GetHeight() != size) return (false, $"size {hd.GetWidth()}x{hd.GetHeight()}, expected {size}x{size}");
        if (!pack.Entries.Any(e => e.Kind == HdEntryKind.Portrait && e.Key == query.Key && e.Legacy == query.Legacy)) return (true, "the pack has no entry for this bitmap: Legacy");
        return hd.IsHd && hd.HdScale == pack.Scale ? (true, "HD") : (false, "the pack holds an entry for this bitmap but the portrait does not draw HD");
    }
}

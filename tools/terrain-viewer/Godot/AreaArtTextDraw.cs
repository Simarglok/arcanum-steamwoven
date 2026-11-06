using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-UI-070: an art font ready to draw: its layout (<see cref="AreaArtFont"/>, MW4) and its glyph frames, whose textures for
/// each blend (<see cref="AreaArtTextRules.LayerPixels"/>) are made on first use.</summary>
public sealed class AreaArtFontFace
{
    private readonly IReadOnlyList<AreaInterfaceFrame> _frames;
    private readonly Dictionary<(int Frame, AreaArtTextBlend Blend), Texture2D?> _textures = [];

    public AreaArtFontFace(IReadOnlyList<AreaInterfaceFrame> frames)
    {
        _frames = frames ?? throw new ArgumentNullException(nameof(frames));
        Font = new(frames);
    }

    public AreaArtFont Font { get; }
    public IReadOnlyList<AreaInterfaceFrame> Frames => _frames;

    /// <summary>A glyph frame's texture for <paramref name="blend"/>; null for an empty frame.</summary>
    public Texture2D? Texture(int frame, AreaArtTextBlend blend)
    {
        if (_textures.TryGetValue((frame, blend), out var known)) return known;
        var art = _frames[frame];
        var texture = art.Width > 0 && art.Height > 0
            ? ImageTexture.CreateFromImage(Image.CreateFromData(art.Width, art.Height, false, Image.Format.Rgba8, AreaArtTextRules.LayerPixels(art.Rgba, blend)))
            : null;
        _textures[(frame, blend)] = texture;
        return texture;
    }
}

/// <summary>
/// ARC-UI-070: the one art-font text draw of the viewer's windows, with the original's blend (ledger MW31): a canvas made ready by
/// <see cref="Prepare"/> draws in premultiplied alpha, so a glyph drawn with its blend's texture and the font's colour covers, adds to
/// or replaces what the canvas drew under it as the software art renderer 0x505EB0 does, in the order of the canvas's draws. Every other
/// draw of a prepared canvas must be opaque or fully transparent with black transparent pixels (as the art decoder makes them).
/// </summary>
public static class AreaArtTextDraw
{
    private static CanvasItemMaterial? _material;

    /// <summary>Gives <paramref name="canvas"/> the premultiplied-alpha material the text draw needs.</summary>
    public static void Prepare(CanvasItem canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        canvas.Material = _material ??= new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.PremultAlpha };
    }

    public static bool Prepared(CanvasItem canvas) => _material is not null && ReferenceEquals(canvas.Material, _material);

    /// <summary>Placed glyphs (their x from the text's left edge) from (x, y) in <paramref name="colour"/>; <paramref name="alpha"/> (ARC-DEV-156,
    /// the floaters' fade) scales the draw's effect on the canvas under it, as the original's art blit with an opacity does.</summary>
    public static void Glyphs(CanvasItem canvas, AreaArtFontFace face, IEnumerable<(int X, AreaArtGlyph Glyph)> placed, int x, int y,
        (byte R, byte G, byte B) colour, AreaArtTextBlend blend = AreaArtTextBlend.Coverage, byte alpha = byte.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(placed);
        if (!Prepared(canvas)) throw new InvalidOperationException("ARC-UI-070: the canvas is not prepared for art text");
        // Premultiplied: the colour and the alpha both carry the opacity.
        var opacity = alpha / 255f;
        var tint = new Color(colour.R / 255f * opacity, colour.G / 255f * opacity, colour.B / 255f * opacity, opacity);
        foreach (var (gx, glyph) in placed)
            if (face.Texture(glyph.Frame, blend) is { } texture)
                canvas.DrawTextureRect(texture, new Rect2(x + gx, y, glyph.Width, glyph.Height), false, tint);
    }

    /// <summary>One line of text from (x, y) in <paramref name="colour"/>.</summary>
    public static void Text(CanvasItem canvas, AreaArtFontFace face, string text, int x, int y, (byte R, byte G, byte B) colour,
        AreaArtTextBlend blend = AreaArtTextBlend.Coverage, byte alpha = byte.MaxValue) => Glyphs(canvas, face, face.Font.Layout(text), x, y, colour, blend, alpha);
}

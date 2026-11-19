using Arcanum.Data.Hd;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-UI-070: an art font ready to draw: its layout (<see cref="AreaArtFont"/>, MW4) and its glyph frames, whose textures for
/// each blend (<see cref="AreaArtTextRules.LayerPixels"/>) are made on first use.
/// <para>ARC-DEV-361: with the HD interface on and an active pack that holds a traced coverage frame for a glyph (a 4× entry of the font's ART,
/// see docs/formats/hd-pack.md), the <see cref="AreaArtTextBlend.Coverage"/> texture is that frame, drawn into the same Legacy-size
/// rectangle through its own <see cref="CanvasTexture"/> with bilinear sampling and no mipmaps (ARC-UI-145; interface frames keep trilinear, ARC-UI-112), so
/// the layout, the advances and the blend are the original's and only the glyph's edge is finer. A glyph without an entry, every other blend and
/// everything with the switch off or no pack draw the Legacy frame as before.</para></summary>
public sealed class AreaArtFontFace
{
    private readonly IReadOnlyList<AreaInterfaceFrame> _frames;
    private readonly Dictionary<(int Frame, AreaArtTextBlend Blend), Texture2D?> _textures = [];
    private readonly Dictionary<(int Frame, AreaArtTextBlend Blend), Texture2D?> _hdTextures = [];
    private HdPack? _hdFor;

    public AreaArtFontFace(IReadOnlyList<AreaInterfaceFrame> frames)
    {
        _frames = frames ?? throw new ArgumentNullException(nameof(frames));
        Font = new(frames);
    }

    public AreaArtFont Font { get; }

    /// <summary>ARC-UI-2940: a face of 225 synthetic glyph cells (5 × 12, advance 6) for self-tests that run without a game folder; draws white blocks.</summary>
    internal static AreaArtFontFace SyntheticForSelfTest() =>
        new(Enumerable.Repeat(new AreaInterfaceFrame(5, 12, Enumerable.Repeat((byte)255, 5 * 12 * 4).ToArray(), 6), 225).ToArray());
    public IReadOnlyList<AreaInterfaceFrame> Frames => _frames;

    /// <summary>A glyph frame's texture for <paramref name="blend"/>; null for an empty frame.</summary>
    public Texture2D? Texture(int frame, AreaArtTextBlend blend)
    {
        // ARC-UI-2795: the additive blend (the inventory windows' counts, font flag 0x10) draws the same HD coverage frame, as the glyph pixel added instead of covering.
        if (blend is AreaArtTextBlend.Coverage or AreaArtTextBlend.Additive && HdTexture(frame, blend) is { } hd) return hd;
        if (_textures.TryGetValue((frame, blend), out var known)) return known;
        var art = _frames[frame];
        var texture = art.Width > 0 && art.Height > 0
            ? ImageTexture.CreateFromImage(Image.CreateFromData(art.Width, art.Height, false, Image.Format.Rgba8, AreaArtTextRules.LayerPixels(art.Rgba, blend)))
            : null;
        _textures[(frame, blend)] = texture;
        return texture;
    }

    /// <summary>True when <paramref name="frame"/> draws its HD coverage frame now (the switch is on and the pack holds a valid entry).</summary>
    public bool IsHd(int frame) => HdTexture(frame, AreaArtTextBlend.Coverage) is not null;

    // The glyph's HD frame as a premultiplied coverage layer (the HD pixels are grey with alpha 255 on ink: LayerPixels gives (w, w, w, w)),
    // decoded once per pack and frame; null while Legacy draws. Dropped when the pack or the switch changes.
    private Texture2D? HdTexture(int frame, AreaArtTextBlend blend)
    {
        if (!InterfaceHd.Enabled || InterfaceHd.Pack is not { } pack)
        {
            if (_hdFor is not null) { _hdFor = null; _hdTextures.Clear(); }
            return null;
        }
        if (!ReferenceEquals(_hdFor, pack)) { _hdFor = pack; _hdTextures.Clear(); }
        if (_hdTextures.TryGetValue((frame, blend), out var known)) return known;
        Texture2D? texture = null;
        var art = _frames[frame];
        if (art.Hd is { } query && art.Width > 0 && art.Height > 0
            && pack.ResolveSprite(query, art.Rgba) is { Status: HdFrameStatus.Hd, Rgba: { } rgba } result)
        {
            using var image = Image.CreateFromData(result.Width, result.Height, false, Image.Format.Rgba8, AreaArtTextRules.LayerPixels(rgba, blend));
            // ARC-UI-145: bilinear, no mipmaps. The mip chain of a 4× glyph blurred the edge at the sheet's and the windows' scales (2.5-3.5 here),
            // where one level down is a quarter of the detail; the owner chose this over trilinear, nearest and a sharper trace by comparing them on screen.
            texture = new CanvasTexture { DiffuseTexture = ImageTexture.CreateFromImage(image), TextureFilter = CanvasItem.TextureFilterEnum.Linear };
        }
        _hdTextures[(frame, blend)] = texture;
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
    /// the floaters' fade) scales the draw's effect on the canvas under it, as the original's art blit with an opacity does.
    /// ARC-UI-2795: <paramref name="scale"/> (a window drawn larger than the original's 1:1) enlarges the glyphs and their advances alike.</summary>
    public static void Glyphs(CanvasItem canvas, AreaArtFontFace face, IEnumerable<(int X, AreaArtGlyph Glyph)> placed, int x, int y,
        (byte R, byte G, byte B) colour, AreaArtTextBlend blend = AreaArtTextBlend.Coverage, byte alpha = byte.MaxValue, float scale = 1f)
    {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(placed);
        if (!Prepared(canvas)) throw new InvalidOperationException("ARC-UI-070: the canvas is not prepared for art text");
        // Premultiplied: the colour and the alpha both carry the opacity.
        var opacity = alpha / 255f;
        var tint = new Color(colour.R / 255f * opacity, colour.G / 255f * opacity, colour.B / 255f * opacity, opacity);
        foreach (var (gx, glyph) in placed)
            if (face.Texture(glyph.Frame, blend) is { } texture)
                canvas.DrawTextureRect(texture, new Rect2(x + gx * scale, y, glyph.Width * scale, glyph.Height * scale), false, tint);
    }

    /// <summary>One line of text from (x, y) in <paramref name="colour"/>.</summary>
    public static void Text(CanvasItem canvas, AreaArtFontFace face, string text, int x, int y, (byte R, byte G, byte B) colour,
        AreaArtTextBlend blend = AreaArtTextBlend.Coverage, byte alpha = byte.MaxValue, float scale = 1f) => Glyphs(canvas, face, face.Font.Layout(text), x, y, colour, blend, alpha, scale);
}

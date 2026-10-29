namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-070: how a glyph of an art font meets the screen (ledger MW31). The text draw 0x535570 hands each glyph to the art blit
/// 0x502360 with the font's colour and blit flags 0x2000, plus 0x10 for font flag 0x100 and 0x200 when font flag 0x80 is clear; without
/// a hardware surface the software art renderer 0x505EB0 draws it, and a glyph pixel of palette index 0 is skipped.
/// </summary>
public enum AreaArtTextBlend
{
    /// <summary>Neither font flag 0x80 nor 0x100 (every font the menus and the dialog box make): the glyph pixel's red level is its cover
    /// a; each channel of the screen becomes screen + (colour − screen) × a / 256, rounded down.</summary>
    Coverage,
    /// <summary>Font flag 0x100 (the brown pass of the menu's Morph30 set, MW3): the glyph pixel times the colour, channel by channel
    /// (rounded down), is added to the screen and held at the channel's top.</summary>
    Additive,
    /// <summary>Font flag 0x80 without 0x100: the glyph pixel times the colour replaces the screen.</summary>
    Multiply,
}

/// <summary>
/// ARC-UI-070: the original's text blend (ledger MW31) as arithmetic on 8-bit channels, the precision of the original's 24- and 32-bit
/// screens (a 16-bit screen does the same on its 5- and 6-bit fields), and the glyph pixels that give the same result on a
/// premultiplied-alpha canvas: the drawn colour times the pixel plus the screen times one minus the pixel's alpha. Godot-free.
/// </summary>
public static class AreaArtTextRules
{
    public const int AdditiveFontFlag = 0x100, OpaqueFontFlag = 0x80;

    /// <summary>The blend of a font's flags (0x535570: flag 0x100 makes the blit additive whatever else is set, 0x505EB0).</summary>
    public static AreaArtTextBlend Blend(int fontFlags) => (fontFlags & AdditiveFontFlag) != 0 ? AreaArtTextBlend.Additive
        : (fontFlags & OpaqueFontFlag) != 0 ? AreaArtTextBlend.Multiply : AreaArtTextBlend.Coverage;

    /// <summary>The original's result for one screen pixel under one glyph pixel (r, g, b, a of the decoded frame: a 0 is palette index 0,
    /// which leaves the screen).</summary>
    public static (byte R, byte G, byte B) Apply((byte R, byte G, byte B) screen, (byte R, byte G, byte B, byte A) glyph,
        (byte R, byte G, byte B) colour, AreaArtTextBlend blend)
    {
        if (glyph.A == 0) return screen;
        return blend switch
        {
            // The cover table 0x739EA0 holds red × 255 / red's top (255 on an 8-bit field), rounded down.
            AreaArtTextBlend.Coverage => (Mix(screen.R, colour.R, glyph.R), Mix(screen.G, colour.G, glyph.R), Mix(screen.B, colour.B, glyph.R)),
            AreaArtTextBlend.Additive => (Add(screen.R, glyph.R, colour.R), Add(screen.G, glyph.G, colour.G), Add(screen.B, glyph.B, colour.B)),
            AreaArtTextBlend.Multiply => (Times(glyph.R, colour.R), Times(glyph.G, colour.G), Times(glyph.B, colour.B)),
            _ => throw new ArgumentOutOfRangeException(nameof(blend)),
        };
    }

    // The difference times the cover, shifted right by 8 as a signed value (rounded down), then added back.
    private static byte Mix(byte screen, byte colour, byte cover) => (byte)(screen + ((colour - screen) * cover >> 8));

    // The product table 0x739EA4: i × j / the channel's top, rounded down.
    private static byte Times(byte glyph, byte colour) => (byte)(glyph * colour / 255);

    private static byte Add(byte screen, byte glyph, byte colour) => (byte)Math.Min(255, screen + Times(glyph, colour));

    /// <summary>The pixels of a glyph frame (RGBA, palette index 0 transparent) for a premultiplied-alpha canvas drawn with the font's
    /// colour: the cover a as (w, w, w, w) for <see cref="AreaArtTextBlend.Coverage"/>, w = a × 255 / 256 rounded (the original mixes
    /// by a / 256, the canvas by w / 255), the glyph with alpha 0 for
    /// <see cref="AreaArtTextBlend.Additive"/> and the glyph itself for <see cref="AreaArtTextBlend.Multiply"/>; the result is
    /// <see cref="Apply"/>'s within one level.</summary>
    public static byte[] LayerPixels(ReadOnlySpan<byte> rgba, AreaArtTextBlend blend)
    {
        if (rgba.Length % 4 != 0) throw new ArgumentException("ARC-UI-070: RGBA pixels come in fours", nameof(rgba));
        var pixels = new byte[rgba.Length];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            if (rgba[i + 3] == 0) continue;
            switch (blend)
            {
                case AreaArtTextBlend.Coverage:
                    pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3] = (byte)((rgba[i] * 255 + 128) / 256);
                    break;
                case AreaArtTextBlend.Additive:
                    (pixels[i], pixels[i + 1], pixels[i + 2]) = (rgba[i], rgba[i + 1], rgba[i + 2]);
                    break;
                case AreaArtTextBlend.Multiply:
                    (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (rgba[i], rgba[i + 1], rgba[i + 2], byte.MaxValue);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(blend));
            }
        }
        return pixels;
    }

    /// <summary>Draws <paramref name="text"/> the original's way into an RGBA screen of <paramref name="width"/> pixels a row: each
    /// glyph's frame at (x + its place, y), clipped to the screen. The reference the viewer's draw is checked against.</summary>
    public static void Render(byte[] screen, int width, AreaArtFont font, IReadOnlyList<AreaInterfaceFrame> frames, string text, int x, int y,
        (byte R, byte G, byte B) colour, AreaArtTextBlend blend)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(frames);
        if (width <= 0 || screen.Length % (width * 4) != 0) throw new ArgumentOutOfRangeException(nameof(width));
        var height = screen.Length / (width * 4);
        foreach (var (gx, glyph) in font.Layout(text))
        {
            var frame = frames[glyph.Frame];
            for (var row = 0; row < frame.Height; row++)
                for (var column = 0; column < frame.Width; column++)
                {
                    var (sx, sy) = (x + gx + column, y + row);
                    if (sx < 0 || sy < 0 || sx >= width || sy >= height) continue;
                    var (s, g) = ((sy * width + sx) * 4, (row * frame.Width + column) * 4);
                    var pixel = frame.Rgba;
                    (screen[s], screen[s + 1], screen[s + 2]) = Apply((screen[s], screen[s + 1], screen[s + 2]),
                        (pixel[g], pixel[g + 1], pixel[g + 2], pixel[g + 3]), colour, blend);
                }
        }
    }
}

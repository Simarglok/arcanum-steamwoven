using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-066/068: the drawing the pre-made browser (window 13) and the new-character window (window 12) share on a canvas of the 800 ×
/// 600 screen: the strips and a window background (MW1), the character draw 0x5448E0 (the portrait, the gender, race and background
/// names, the description field and the stats, MW18–MW20) and the art buttons' frames. PROJECT: a glyph's grey levels scale the font's
/// colour (as MW5).
/// </summary>
internal static class AreaCharacterArtDraw
{
    public static Texture2D? Art(IReadOnlyDictionary<int, Texture2D[]> art, int number, int frame = 0) =>
        art.TryGetValue(number, out var frames) && frame < frames.Length ? frames[frame] : null;

    /// <summary>The frame a push button shows (GB3/GB4): down while held over it, idle while another is held, lit under the pointer or
    /// <paramref name="lit"/> (PROJECT: the controller's focus).</summary>
    public static int Frame(int frames, bool held, bool heldHere, bool hovered, bool lit = false)
    {
        var look = held ? heldHere && hovered ? AreaBarButtonLook.Down : AreaBarButtonLook.Idle
            : hovered || lit ? AreaBarButtonLook.Hovered : AreaBarButtonLook.Idle;
        return AreaGameBarLayout.Frame(look, Math.Max(1, frames));
    }

    public static void Fill(CanvasItem canvas, (int X, int Y, int Width, int Height) field) =>
        canvas.DrawRect(new Rect2(field.X, field.Y, field.Width, field.Height), Colors.Black);

    public static void Glyphs(CanvasItem canvas, IReadOnlyDictionary<int, Texture2D> glyphs, IEnumerable<(int X, AreaArtGlyph Glyph)> placed, int x, int y,
        (byte R, byte G, byte B) colour)
    {
        // PROJECT: a glyph's grey levels scale the font's colour (the text blit's blend is not read, F0; as the menu's labels).
        var tint = Color.Color8(colour.R, colour.G, colour.B);
        foreach (var (gx, glyph) in placed)
            if (glyphs.TryGetValue(glyph.Frame, out var texture)) canvas.DrawTextureRect(texture, new Rect2(x + gx, y, glyph.Width, glyph.Height), false, tint);
    }

    /// <summary>A one-line text centred in its field (0x542DF0), the field filled black first.</summary>
    public static void Centred(CanvasItem canvas, (int X, int Y, int Width, int Height) field, string text, AreaArtFont font,
        IReadOnlyDictionary<int, Texture2D> glyphs, (byte R, byte G, byte B) colour)
    {
        Fill(canvas, field);
        Glyphs(canvas, glyphs, font.Layout(text), AreaPremadeBrowserArtLayout.Centred(field, font.Measure(text).Width), field.Y, colour);
    }

    /// <summary>A text from its field's top-left, broken at its width; a line the field's remaining height cannot hold ends the draw
    /// (0x5356D8, MW26).</summary>
    public static void Lines(CanvasItem canvas, (int X, int Y, int Width, int Height) field, string text, AreaArtFont font,
        IReadOnlyDictionary<int, Texture2D> glyphs, (byte R, byte G, byte B) colour)
    {
        var height = font.Measure("A").Height;
        var y = field.Y;
        foreach (var line in AreaPremadeBrowserArtLayout.Wrap(font, text, field.Width))
        {
            if (height <= 0 || y + height > field.Y + field.Height) break;
            Glyphs(canvas, glyphs, line, field.X, y, colour);
            y += height;
        }
    }

    /// <summary>The screen's black, the strips and the window's background at (0, 41) (MW1).</summary>
    public static void Window(CanvasItem canvas, IReadOnlyDictionary<int, Texture2D[]> art, int background)
    {
        canvas.DrawRect(new Rect2(0, 0, AreaPremadeBrowserArtLayout.ScreenWidth, AreaPremadeBrowserArtLayout.ScreenHeight), Colors.Black);
        if (Art(art, AreaPremadeBrowserArtLayout.TopStripArt) is { } top) canvas.DrawTextureRect(top, new Rect2(0, 0, top.GetWidth(), top.GetHeight()), false);
        if (Art(art, AreaPremadeBrowserArtLayout.BottomStripArt) is { } bottom)
            foreach (var (x, y, width, height) in AreaSaveListArtLayout.BottomStripParts)
                canvas.DrawTextureRectRegion(bottom, new Rect2(x, y, width, height), new Rect2(x, 0, width, height));
        if (Art(art, background) is { } window)
            canvas.DrawTextureRect(window, new Rect2(0, AreaPremadeBrowserArtLayout.WindowTop, window.GetWidth(), window.GetHeight()), false);
    }

    /// <summary>The character draw 0x5448E0 of <paramref name="view"/> (all but the name, which each window's redraw adds), the
    /// description field showing <paramref name="description"/>: its title (or none) and its text in its field.</summary>
    public static void Character(CanvasItem canvas, AreaPremadeBrowserFonts fonts, AreaPremadeBrowserView view, Texture2D? portrait,
        AreaCharacterDescription description)
    {
        // The portrait's field, then the large portrait at its corner (0x4CE640).
        Fill(canvas, AreaPremadeBrowserArtLayout.PortraitFill);
        if (portrait is not null)
            canvas.DrawTextureRect(portrait, new Rect2(AreaPremadeBrowserArtLayout.Portrait.X, AreaPremadeBrowserArtLayout.Portrait.Y, portrait.GetWidth(), portrait.GetHeight()), false);
        var (white, red) = (AreaPremadeBrowserArtLayout.White, AreaPremadeBrowserArtLayout.Red);
        Centred(canvas, AreaPremadeBrowserArtLayout.Gender, view.Gender, fonts.Text, fonts.TextGlyphs, red);
        Centred(canvas, AreaPremadeBrowserArtLayout.Race, view.Race, fonts.Text, fonts.TextGlyphs, red);
        Centred(canvas, AreaPremadeBrowserArtLayout.Background, view.BackgroundName, fonts.Text, fonts.TextGlyphs, red);
        Fill(canvas, AreaPremadeBrowserArtLayout.Description);
        if (description.Title is { } title) Centred(canvas, AreaPremadeBrowserArtLayout.RaceTitle, title, fonts.Name, fonts.NameGlyphs, white);
        Lines(canvas, description.Field, description.Text, fonts.Text, fonts.TextGlyphs, white);
        Centred(canvas, AreaPremadeBrowserArtLayout.StatsTitle, view.StatsTitle, fonts.Name, fonts.NameGlyphs, white);
        for (var slot = 0; slot < view.Stats.Count && slot < AreaPremadeBrowserArtLayout.StatFields.Count; slot++)
        {
            var stat = AreaPremadeBrowserArtLayout.StatFields[slot];
            Fill(canvas, stat);
            // 0x5418A0 from the field's corner; a stat's line is taller than its 15-row field, so it is not clipped to it.
            Glyphs(canvas, fonts.TextGlyphs, fonts.Text.Layout(view.Stats[slot]), stat.X, stat.Y, white);
        }
    }
}

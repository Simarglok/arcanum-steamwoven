namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-3503 (ledger LS5): the line the original writes on the black screen while "Last Save" loads (window 9's init 0x544440):
/// <c>MainMenu.mes</c> message 5 in a fixed rectangle of the 800 × 600 screen, left aligned, in the text library's default font
/// (<see cref="FontPath"/> of <c>tig.dat</c>, not the interface art 27 of the same name), white with its shadow one pixel right and down.
/// Godot-free; the load screen draws it (<c>AreaSaveLoadingTextView</c>).
/// </summary>
public static class AreaSaveLoadingText
{
    /// <summary>The default font's ART in the content stack.</summary>
    public const string FontPath = "art/morph15font.art";

    /// <summary>The screen the rectangle's coordinates are in.</summary>
    public const int ScreenWidth = 800, ScreenHeight = 600;

    /// <summary>The shadow's offset right and down (the default font's flag).</summary>
    public const int ShadowOffset = 1;

    /// <summary>The text's rectangle on the screen.</summary>
    public static (int X, int Y, int Width, int Height) Rect => (340, 210, 200, 20);

    public static (byte R, byte G, byte B) Colour => (255, 255, 255);

    public static (byte R, byte G, byte B) ShadowColour => (10, 10, 10);

    /// <summary>The lines the rectangle shows: broken at its width, drawn from its top left, as many as its height takes (at least one).</summary>
    public static AreaArtTextPlacement Place(AreaArtFont font, string text)
    {
        ArgumentNullException.ThrowIfNull(font);
        return AreaTextFontLayout.Place(font, Math.Max(1, font.LineHeight), Rect, text);
    }

    /// <summary>The screen fitted into a window keeping its aspect, centred (as the death slides' screen): its scale and top left.</summary>
    public static (float Factor, float X, float Y) Fit(float width, float height)
    {
        var factor = Math.Max(0f, Math.Min(width / ScreenWidth, height / ScreenHeight));
        return (factor, MathF.Round((width - ScreenWidth * factor) / 2), MathF.Round((height - ScreenHeight * factor) / 2));
    }
}

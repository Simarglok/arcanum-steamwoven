using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-DEV-156 (B27, B28): the original's text fonts (<see cref="AreaTextFontLayout"/>) ready to draw, with their line heights.
/// ARC-UI-124: <paramref name="Numbers"/> is the combat panel's number font (null: the text font draws the number).</summary>
public sealed record AreaTextFonts(AreaArtFontFace Title, AreaArtFontFace Text, AreaArtFontFace? Numbers = null)
{
    /// <summary>ARC-UI-1884: the quantity pane's roller font (art 25), null when it did not read.</summary>
    public AreaArtFontFace? Roller { get; init; }

    public int TitleLineHeight { get; } = AreaTextFontLayout.LineHeight(Title.Frames);
    public int TextLineHeight { get; } = AreaTextFontLayout.LineHeight(Text.Frames);
    public int NumberLineHeight { get; } = Numbers is null ? AreaTextFontLayout.LineHeight(Text.Frames) : AreaTextFontLayout.LineHeight(Numbers.Frames);

    public AreaArtFontFace Face(AreaMessageFont font) => font switch { AreaMessageFont.Title => Title, AreaMessageFont.Number => Numbers ?? Text, _ => Text };
    public int LineHeight(AreaMessageFont font) => font switch { AreaMessageFont.Title => TitleLineHeight, AreaMessageFont.Number => NumberLineHeight, _ => TextLineHeight };
}

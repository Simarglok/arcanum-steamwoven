using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-DEV-156 (B27, B28): the original's two text fonts (<see cref="AreaTextFontLayout"/>) ready to draw, with their line heights.</summary>
public sealed record AreaTextFonts(AreaArtFontFace Title, AreaArtFontFace Text)
{
    public int TitleLineHeight { get; } = AreaTextFontLayout.LineHeight(Title.Frames);
    public int TextLineHeight { get; } = AreaTextFontLayout.LineHeight(Text.Frames);

    public AreaArtFontFace Face(AreaMessageFont font) => font == AreaMessageFont.Title ? Title : Text;
    public int LineHeight(AreaMessageFont font) => font == AreaMessageFont.Title ? TitleLineHeight : TextLineHeight;
}

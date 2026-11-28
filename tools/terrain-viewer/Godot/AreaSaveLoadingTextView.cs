using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-UI-3503: the original's 800 × 600 screen over the load screen's black with the saved game's loading line
/// (<see cref="AreaSaveLoadingText"/>), drawn in the default art font (with the HD interface on, its HD glyphs when the pack holds them). Without a
/// face (no game folder, or the font did not read) nothing is drawn.</summary>
public partial class AreaSaveLoadingTextView : Control
{
    private AreaArtFontFace? _face;
    private string _text = "";

    public AreaSaveLoadingTextView()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(AreaSaveLoadingText.ScreenWidth, AreaSaveLoadingText.ScreenHeight);
        AreaArtTextDraw.Prepare(this);
    }

    /// <summary>For the self-test: the lines drawn the last time (0 before a draw with a face).</summary>
    public int DrawnLines { get; private set; }

    public string Text => _text;

    public bool HasFont => _face is not null;

    public void SetContent(AreaArtFontFace? face, string text)
    {
        _face = face;
        _text = text ?? "";
        QueueRedraw();
    }

    /// <summary>Places the screen in a window of <paramref name="window"/>; a fractional scale is sampled linear, a whole one nearest.</summary>
    public void Fit(Vector2 window)
    {
        var (factor, x, y) = AreaSaveLoadingText.Fit(window.X, window.Y);
        Scale = new Vector2(factor, factor);
        Position = new Vector2(x, y);
        TextureFilter = Mathf.IsEqualApprox(factor, MathF.Round(factor)) ? TextureFilterEnum.Nearest : TextureFilterEnum.Linear;
    }

    public override void _Draw()
    {
        DrawnLines = 0;
        if (_face is not { } face || _text.Length == 0) return;
        var rect = AreaSaveLoadingText.Rect;
        foreach (var line in AreaSaveLoadingText.Place(face.Font, _text).Lines)
        {
            var (x, y) = (rect.X + line.X, rect.Y + line.Y);
            AreaArtTextDraw.Glyphs(this, face, line.Glyphs, x + AreaSaveLoadingText.ShadowOffset, y + AreaSaveLoadingText.ShadowOffset, AreaSaveLoadingText.ShadowColour);
            AreaArtTextDraw.Glyphs(this, face, line.Glyphs, x, y, AreaSaveLoadingText.Colour);
            DrawnLines++;
        }
    }
}

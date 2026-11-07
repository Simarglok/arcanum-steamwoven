using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-071 (B29): a text in the original's font art (<see cref="AreaTextFonts.Text"/>, font 229) in one colour, broken at
/// <see cref="WrapWidth"/> as the message window's text draw does (<see cref="AreaTextFontLayout.Place"/>, ML4) and drawn with the
/// original's blend (<see cref="AreaArtTextDraw"/>, MW31). Its minimum size is the lines' extent, so a container grows with it.
/// </summary>
public partial class AreaArtParagraph : AreaArtTextBlock
{
    private const int FarTooTall = 1 << 20;
    private AreaTextFonts? _fonts;
    private string _text = "";
    private (byte R, byte G, byte B) _colour = (255, 255, 255);
    private int _wrapWidth = 640;

    public AreaArtParagraph() => FitContent = true;

    public string Text => _text;
    /// <summary>The lines the text takes at its wrap width (0 without fonts or text).</summary>
    public int LineCount => Items.Count == 0 ? 0 : Items[0].Lines.Count;

    /// <summary>The width the lines are broken at, in pixels.</summary>
    public int WrapWidth
    {
        get => _wrapWidth;
        set
        {
            value = Math.Max(1, value);
            if (value == _wrapWidth) return;
            _wrapWidth = value;
            Place();
        }
    }

    public void SetFonts(AreaTextFonts? fonts)
    {
        if (ReferenceEquals(fonts, _fonts)) return;
        _fonts = fonts;
        Place();
    }

    public void SetText(string text, (byte R, byte G, byte B) colour)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text == _text && colour == _colour) return;
        (_text, _colour) = (text, colour);
        Place();
    }

    private void Place()
    {
        if (_fonts is not { } fonts || _text.Length == 0) { Set([]); return; }
        var placed = AreaTextFontLayout.Place(fonts.Text.Font, fonts.TextLineHeight, (0, 0, _wrapWidth, FarTooTall), _text);
        Set([new(fonts.Text, fonts.TextLineHeight, placed.Lines, 0, 0, _colour)]);
    }
}

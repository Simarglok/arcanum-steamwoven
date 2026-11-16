using Arcanum.Presentation.Hud;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-2368: one number slot of the bottom strip as the digit writer 0x54AD00 draws it (ledger BC5, BC9): the slot's rectangle (one row taller)
/// filled with opaque black unless it is the poison's, then the text in the bar font (art 171, <see cref="AreaArtFontFace"/>) in white over a
/// (10, 10, 10) shadow one pixel right and down, centred by <see cref="BarNumberRules.Place"/>. The control's rectangle is the slot; the fill's extra row
/// and the shadow draw just outside it. Without the font (no game folder, or the art did not read) the viewer's stand-in font writes the text.
/// </summary>
public partial class AreaBarNumber : Control
{
    private string _text = "";
    private AreaArtFontFace? _face;

    public AreaBarNumber(bool filled = true) => Filled = filled;

    /// <summary>Whether the black fill is drawn (every slot but the poison's).</summary>
    public bool Filled { get; }

    public string Text
    {
        get => _text;
        set { if (_text == value) return; _text = value ?? ""; QueueRedraw(); }
    }

    /// <summary>The bar font; null draws the stand-in.</summary>
    public AreaArtFontFace? Face
    {
        get => _face;
        set
        {
            if (ReferenceEquals(_face, value)) return;
            _face = value;
            // The art text draw needs the premultiplied canvas; the stand-in draws on the plain one.
            if (value is not null) AreaArtTextDraw.Prepare(this); else Material = null;
            QueueRedraw();
        }
    }

    /// <summary>The top left of the first glyph from the control's own top left (null: no text, the text is not drawn, or the stand-in draws it).</summary>
    internal (int X, int Y)? PlacementForSelfTest { get; private set; }

    public override void _Draw()
    {
        PlacementForSelfTest = null;
        var width = (int)Size.X;
        var height = (int)Size.Y;
        if (Filled)
        {
            var (r, g, b) = BarNumberRules.FillColour;
            DrawRect(new Rect2(0, 0, width, height + 1), new Color(r / 255f, g / 255f, b / 255f));
        }
        if (_text.Length == 0) return;
        if (_face is not { } face) { DrawStandIn(width, height); return; }
        var layout = face.Font.Layout(_text);
        if (layout.Count == 0) return;
        var (textWidth, textHeight) = face.Font.Measure(_text);
        if (BarNumberRules.Place(width, height, textWidth + BarNumberRules.ShadowOffset, textHeight + BarNumberRules.ShadowOffset) is not { } at) return;
        PlacementForSelfTest = at;
        AreaArtTextDraw.Glyphs(this, face, layout, at.X + BarNumberRules.ShadowOffset, at.Y + BarNumberRules.ShadowOffset, BarNumberRules.ShadowColour);
        AreaArtTextDraw.Glyphs(this, face, layout, at.X, at.Y, BarNumberRules.TextColour);
    }

    private void DrawStandIn(int width, int height)
    {
        var font = ThemeDB.FallbackFont;
        const int size = 10;
        var text = font.GetStringSize(_text, HorizontalAlignment.Left, -1, size);
        var x = Math.Max(0, (width - text.X) / 2);
        var y = (height - text.Y) / 2 + font.GetAscent(size);
        DrawString(font, new Vector2(x + 1, y + 1), _text, HorizontalAlignment.Left, -1, size, Colors.Black);
        DrawString(font, new Vector2(x, y), _text, HorizontalAlignment.Left, -1, size, Colors.White);
    }
}

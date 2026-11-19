using Arcanum.Presentation.Hud;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-2368: one number slot of the bottom strip as the digit writer 0x54AD00 draws it (ledger BC5, BC9): the slot's rectangle (one row taller)
/// filled with opaque black unless it is the poison's, then the text in the bar font (art 171, <see cref="AreaArtFontFace"/>) in white over a
/// (10, 10, 10) shadow one pixel right and down, centred by <see cref="BarNumberRules.Place"/>. The control's rectangle is the slot; the fill's extra row
/// and the shadow draw just outside it. Without the font (no game folder, or the art did not read) only the fill is drawn: no Godot font stands in (ARC-UI-2940).
/// </summary>
public partial class AreaBarNumber : Control
{
    private string _text = "";
    private AreaArtFontFace? _face;

    public AreaBarNumber() : this(true) { }

    public AreaBarNumber(bool filled) => Filled = filled;

    /// <summary>Whether the black fill is drawn (every slot but the poison's).</summary>
    public bool Filled { get; }

    public string Text
    {
        get => _text;
        set { if (_text == value) return; _text = value ?? ""; QueueRedraw(); }
    }

    /// <summary>The bar font; null draws no text.</summary>
    public AreaArtFontFace? Face
    {
        get => _face;
        set
        {
            if (ReferenceEquals(_face, value)) return;
            _face = value;
            // The art text draw needs the premultiplied canvas; the plain fill draws on the plain one.
            if (value is not null) AreaArtTextDraw.Prepare(this); else Material = null;
            QueueRedraw();
        }
    }

    /// <summary>The top left of the first glyph from the control's own top left (null: no text, or the text is not drawn).</summary>
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
        if (_face is not { } face) return;
        var layout = face.Font.Layout(_text);
        if (layout.Count == 0) return;
        var (textWidth, textHeight) = face.Font.Measure(_text);
        if (BarNumberRules.Place(width, height, textWidth + BarNumberRules.ShadowOffset, textHeight + BarNumberRules.ShadowOffset) is not { } at) return;
        PlacementForSelfTest = at;
        AreaArtTextDraw.Glyphs(this, face, layout, at.X + BarNumberRules.ShadowOffset, at.Y + BarNumberRules.ShadowOffset, BarNumberRules.ShadowColour);
        AreaArtTextDraw.Glyphs(this, face, layout, at.X, at.Y, BarNumberRules.TextColour);
    }
}

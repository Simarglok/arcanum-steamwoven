using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-2795: one line of window text in an original art font (<see cref="AreaArtFontFace"/>, drawn by <see cref="AreaArtTextDraw"/>, so with the
/// HD interface on it is the HD redraw of that same font and otherwise the original's own frames) in place of a Godot <see cref="Label"/>, which can only
/// draw Godot's default font. The control's rectangle is the text's rectangle: the line is placed left, centred or right in its width and at its top or
/// centred in its height. Without a face (no game folder, or the font art did not read) nothing is drawn and <see cref="Text"/> still holds the string.
/// </summary>
public partial class AreaArtLabel : Control
{
    public enum Alignment { Left, Centre, Right }

    private AreaArtFontFace? _face;
    private string _text = "";
    private (byte R, byte G, byte B) _colour = (255, 255, 255);
    private Alignment _align;
    private bool _middle;
    private float _scale = 1f;

    public AreaArtLabel()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AreaArtTextDraw.Prepare(this);
    }

    public AreaArtFontFace? Face
    {
        get => _face;
        set { if (ReferenceEquals(_face, value)) return; _face = value; QueueRedraw(); }
    }

    public string Text
    {
        get => _text;
        set { value ??= ""; if (_text == value) return; _text = value; QueueRedraw(); }
    }

    public (byte R, byte G, byte B) Colour
    {
        get => _colour;
        set { if (_colour == value) return; _colour = value; QueueRedraw(); }
    }

    public Alignment Align
    {
        get => _align;
        set { if (_align == value) return; _align = value; QueueRedraw(); }
    }

    /// <summary>True: the line's top is the rectangle's free height halved; false: the rectangle's top.</summary>
    public bool Middle
    {
        get => _middle;
        set { if (_middle == value) return; _middle = value; QueueRedraw(); }
    }

    /// <summary>The window's scale over the original's pixels (glyphs and advances are enlarged by it); 1 for a window at the original's size.</summary>
    public float TextScale
    {
        get => _scale;
        set { if (_scale == value) return; _scale = value; QueueRedraw(); }
    }

    /// <summary>The original's pixel width of the text in the face, or 0 without one.</summary>
    public int TextWidth => _face is { } face && _text.Length > 0 ? face.Font.Measure(_text).Width : 0;

    public override void _Draw()
    {
        if (_face is not { } face || _text.Length == 0) return;
        var width = face.Font.Measure(_text).Width * _scale;
        var height = AreaTextFontLayout.LineHeight(face.Frames) * _scale;
        var x = _align switch { Alignment.Right => Size.X - width, Alignment.Centre => (Size.X - width) / 2, _ => 0f };
        var y = _middle ? (Size.Y - height) / 2 : 0f;
        AreaArtTextDraw.Glyphs(this, face, face.Font.Layout(_text), (int)MathF.Round(Math.Max(0f, x)), (int)MathF.Round(y), _colour, scale: _scale);
    }
}

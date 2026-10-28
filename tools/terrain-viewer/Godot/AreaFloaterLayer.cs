using Arcanum.Presentation.Hud;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-021 (ledger CH10–CH11): the floating lines over the AREA view, drawn with the viewer's font (no original font art) under
/// every interface layer. Each object's lines stand in a 200 px box over the point its owner gives (<see cref="Anchor"/>: the screen
/// point 90 px above the object's tile centre, at the view's zoom), centred, at their offset from the box's top and with their fade;
/// the text and the box keep the screen's pixels at any zoom. Nothing is drawn at level 0 or for an object without a point (off the
/// shown map); its lines keep rising.
/// </summary>
public partial class AreaFloaterLayer : CanvasLayer
{
    public const int FontSize = 14;
    private static readonly Color Outline = new(0, 0, 0);
    private readonly Font _font = ThemeDB.FallbackFont;
    private Control _surface = null!;

    public AreaFloaterLayer()
    {
        LineHeight = FloaterRules.LineHeight((int)Math.Ceiling(_font.GetHeight(FontSize)));
        Field = new FloaterField<string>(LineHeight);
    }

    public int LineHeight { get; }
    public FloaterField<string> Field { get; }
    /// <summary>The screen point the box's bottom centre stands on for an owner, or null when it is not on the shown map.</summary>
    public Func<string, Vector2?> Anchor { get; set; } = _ => null;
    /// <summary>The "text floaters" level: 0 draws nothing.</summary>
    public int Level { get; set; } = FloaterRules.DefaultLevel;

    public override void _Ready()
    {
        Name = "AreaFloaters";
        Layer = 1;
        _surface = new Control { Name = "FloaterSurface", MouseFilter = Control.MouseFilterEnum.Ignore };
        _surface.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _surface.Draw += DrawLines;
        AddChild(_surface);
    }

    public void Redraw() { if (IsInsideTree()) _surface.QueueRedraw(); }

    /// <summary>What a draw shows now: per owner with a point, each line's text, colour, screen position and opacity.</summary>
    internal IReadOnlyList<(string Owner, string Text, FloaterColor Color, Vector2 Position, int Alpha)> DrawnForSelfTest()
    {
        var drawn = new List<(string, string, FloaterColor, Vector2, int)>();
        if (Level == FloaterRules.Never) return drawn;
        foreach (var owner in Field.Owners)
        {
            if (Anchor(owner) is not { } anchor) continue;
            var (left, top) = FloaterRules.Box((int)Math.Round(anchor.X), (int)Math.Round(anchor.Y) + FloaterRules.BoxRise, LineHeight);
            foreach (var line in Field.Lines(owner))
            {
                var alpha = FloaterRules.Alpha(line.Offset, LineHeight);
                if (alpha == 0) continue;
                var width = (int)Math.Ceiling(_font.GetStringSize(line.Text, HorizontalAlignment.Left, -1, FontSize).X);
                drawn.Add((owner, line.Text, line.Color, new Vector2(FloaterRules.LineLeft(left, width), top + line.Offset), alpha));
            }
        }
        return drawn;
    }

    private void DrawLines()
    {
        var ascent = _font.GetAscent(FontSize);
        foreach (var (_, text, color, position, alpha) in DrawnForSelfTest())
        {
            var (r, g, b) = FloaterRules.Rgb(color);
            var tint = Color.Color8(r, g, b, (byte)alpha);
            var baseline = position + new Vector2(0, ascent);
            _surface.DrawStringOutline(_font, baseline, text, HorizontalAlignment.Left, -1, FontSize, 3, Outline with { A = alpha / 255f });
            _surface.DrawString(_font, baseline, text, HorizontalAlignment.Left, -1, FontSize, tint);
        }
    }
}

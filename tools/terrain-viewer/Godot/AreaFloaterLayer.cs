using Arcanum.Presentation.Hud;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-021 (ledger CH10–CH11): the floating lines over the AREA view under every interface layer. ARC-DEV-156 (B28): drawn in the
/// original's font art 229 (the text font, in the floater's colour, with the fade as the blit's opacity, CH10–CH11) when the layer
/// has it (<see cref="AreaFloaterLayer(AreaArtFontFace?)"/>), else in the viewer's font. Each object's lines stand in a 200 px box over
/// the point its owner gives (<see cref="Anchor"/>: the screen point 90 px above the object's tile centre, at the view's zoom), centred, at their offset from the box's top and with their fade;
/// the text and the box keep the screen's pixels at any zoom. Nothing is drawn at level 0 or for an object without a point (off the
/// shown map); its lines keep rising.
/// </summary>
public partial class AreaFloaterLayer : CanvasLayer
{
    public const int FontSize = 14;
    private static readonly Color Outline = new(0, 0, 0);
    private readonly Font _font = ThemeDB.FallbackFont;
    private readonly AreaArtFontFace? _face;
    private Control _surface = null!;
    private Control _bubbleSurface = null!;

    public AreaFloaterLayer() : this(null) { }

    /// <summary>A layer drawing with <paramref name="face"/> (font art 229); null: the viewer's font. The line height is the font's
    /// height + 2 (CH11).</summary>
    public AreaFloaterLayer(AreaArtFontFace? face)
    {
        _face = face;
        LineHeight = FloaterRules.LineHeight(face is null ? (int)Math.Ceiling(_font.GetHeight(FontSize)) : AreaTextFontLayout.LineHeight(face.Frames));
        Field = new FloaterField<string>(LineHeight);
        // ARC-UI-1997 (TB1–TB22): a bubble's line height is the font's text height (the floater's without its 2 pixels).
        BubbleLineHeight = LineHeight - 2;
    }

    /// <summary>ARC-UI-1997: the text bubbles over objects (the original's TB table of eight), drawn under the floating lines.</summary>
    public TextBubbleField<string> Bubbles { get; } = new();
    public int BubbleLineHeight { get; }
    /// <summary>The screen point an owner's bubble anchors on (its tile centre, with the original's (40, 20), no draw offset), or null when it is not on the shown map.</summary>
    public Func<string, Vector2?> BubbleAnchor { get; set; } = _ => null;
    /// <summary>The view's rectangle the bubbles are placed in; the whole layer when null.</summary>
    public Func<Rect2>? BubbleView { get; set; }

    /// <summary>Whether the original's font art draws the lines.</summary>
    internal bool ArtFontForSelfTest => _face is not null;

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
        // Added first, so the bubbles draw under the floating lines (the original draws them first, 0x4D5F10 then 0x4D5310).
        _bubbleSurface = new Control { Name = "BubbleSurface", MouseFilter = Control.MouseFilterEnum.Ignore };
        _bubbleSurface.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _bubbleSurface.Draw += DrawBubbles;
        if (_face is not null) AreaArtTextDraw.Prepare(_bubbleSurface);
        AddChild(_bubbleSurface);
        _surface = new Control { Name = "FloaterSurface", MouseFilter = Control.MouseFilterEnum.Ignore };
        _surface.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _surface.Draw += DrawLines;
        if (_face is not null) AreaArtTextDraw.Prepare(_surface);
        AddChild(_surface);
    }

    public void Redraw() { if (IsInsideTree()) { _surface.QueueRedraw(); _bubbleSurface.QueueRedraw(); } }

    /// <summary>The width of a bubble's text in the layer's font.</summary>
    public int MeasureText(string text) => _face is { } face ? face.Font.Measure(text).Width
        : (int)Math.Ceiling(_font.GetStringSize(text, HorizontalAlignment.Left, -1, FontSize).X);

    /// <summary>Makes a bubble over <paramref name="owner"/>: the text wrapped and measured once, as the original renders it.</summary>
    public int AddBubble(string owner, string text, BubbleColor color, long nowMs)
    {
        var index = Bubbles.Create(owner, text, TextBubbleRules.Layout(text, BubbleLineHeight, MeasureText), color, nowMs);
        Redraw();
        return index;
    }

    /// <summary>What a bubble draw shows now: per bubble with an anchor, its lines (text, screen position), colour and placement slot. A bubble's slot is
    /// chosen the first time it is laid out with an anchor and kept. The draw and the smokes both read it.</summary>
    internal IReadOnlyList<(string Owner, BubbleColor Color, int Slot, Rect2 Rect, IReadOnlyList<(string Text, Vector2 Position)> Lines)> LayOutBubbles()
    {
        var drawn = new List<(string, BubbleColor, int, Rect2, IReadOnlyList<(string, Vector2)>)>();
        var view = BubbleView?.Invoke() ?? _bubbleSurface.GetRect();
        foreach (var bubble in Bubbles.Bubbles.ToArray())
        {
            if (BubbleAnchor(bubble.Owner) is not { } anchor) continue;
            var ax = (int)Math.Round(anchor.X); var ay = (int)Math.Round(anchor.Y);
            var slot = bubble.Slot ?? TextBubbleRules.ChooseSlot(ax, ay, bubble.Layout.Width, bubble.Layout.Height,
                (int)view.Position.X, (int)view.Position.Y, (int)view.Size.X, (int)view.Size.Y);
            if (bubble.Slot is null) Bubbles.SetSlot(bubble.Owner, slot);
            var (left, top) = TextBubbleRules.SlotPosition(slot, ax, ay, bubble.Layout.Width, bubble.Layout.Height);
            var lines = bubble.Layout.Lines.Select((l, i) => (l.Text, new Vector2(left + l.Left, top + i * BubbleLineHeight))).ToArray();
            drawn.Add((bubble.Owner, bubble.Color, slot, new Rect2(left, top, bubble.Layout.Width, bubble.Layout.Height), lines));
        }
        return drawn;
    }

    private void DrawBubbles()
    {
        var ascent = _font.GetAscent(FontSize);
        foreach (var (_, color, _, _, lines) in LayOutBubbles())
        {
            var (r, g, b) = TextBubbleRules.Rgb(color);
            var (sr, sg, sb) = TextBubbleRules.ShadowRgb;
            foreach (var (text, position) in lines)
            {
                if (_face is { } face)
                {
                    // The shadow first at +1,+1 (font flag 0x08), then the line.
                    AreaArtTextDraw.Text(_bubbleSurface, face, text, (int)position.X + TextBubbleRules.ShadowOffset, (int)position.Y + TextBubbleRules.ShadowOffset, (sr, sg, sb));
                    AreaArtTextDraw.Text(_bubbleSurface, face, text, (int)position.X, (int)position.Y, (r, g, b));
                    continue;
                }
                var baseline = position + new Vector2(0, ascent);
                _bubbleSurface.DrawStringOutline(_font, baseline, text, HorizontalAlignment.Left, -1, FontSize, 3, Outline);
                _bubbleSurface.DrawString(_font, baseline, text, HorizontalAlignment.Left, -1, FontSize, Color.Color8(r, g, b));
            }
        }
    }

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
                var width = _face is { } face ? face.Font.Measure(line.Text).Width
                    : (int)Math.Ceiling(_font.GetStringSize(line.Text, HorizontalAlignment.Left, -1, FontSize).X);
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
            if (_face is { } face)
            {
                AreaArtTextDraw.Text(_surface, face, text, (int)position.X, (int)position.Y, (r, g, b), alpha: (byte)alpha);
                continue;
            }
            var tint = Color.Color8(r, g, b, (byte)alpha);
            var baseline = position + new Vector2(0, ascent);
            _surface.DrawStringOutline(_font, baseline, text, HorizontalAlignment.Left, -1, FontSize, 3, Outline with { A = alpha / 255f });
            _surface.DrawString(_font, baseline, text, HorizontalAlignment.Left, -1, FontSize, tint);
        }
    }
}

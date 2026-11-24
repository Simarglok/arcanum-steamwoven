using Arcanum.Presentation.Hud;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-021 (ledger CH10–CH11): the floating lines over the AREA view under every interface layer. ARC-DEV-156 (B28): drawn in the
/// original's font art 229 (the text font, in the floater's colour, with the fade as the blit's opacity, CH10–CH11) when the layer
/// has it (<see cref="AreaFloaterLayer(AreaArtFontFace?)"/>); without it (no game folder) the lines are laid out at a nominal size and nothing is drawn: no Godot font stands in (ARC-UI-2940). Each object's lines stand in a 200 px box over
/// the point its owner gives (<see cref="Anchor"/>: the screen point 90 px above the object's tile centre, at the view's zoom), centred, at their offset from the box's top and with their fade;
/// the text and the box keep the original's pixels at any zoom, drawn at the screen's whole factor (ARC-UI-2289). Nothing is drawn at level 0 or for an object without a point (off the
/// shown map); its lines keep rising.
/// </summary>
public partial class AreaFloaterLayer : CanvasLayer
{
    /// <summary>ARC-UI-2940: the line height and the advance of a glyph used to lay the lines out when there is no font (art 229's line height and an average advance).</summary>
    private const int NominalLineHeight = 15, NominalAdvance = 6;
    private readonly AreaArtFontFace? _face;
    private Control _surface = null!;
    private Control _bubbleSurface = null!;

    public AreaFloaterLayer() : this(null) { }

    /// <summary>A layer drawing with <paramref name="face"/> (font art 229); null: nothing is drawn. The line height is the font's
    /// height + 2 (CH11).</summary>
    public AreaFloaterLayer(AreaArtFontFace? face)
    {
        _face = face;
        LineHeight = FloaterRules.LineHeight(face is null ? NominalLineHeight : AreaTextFontLayout.LineHeight(face.Frames));
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
    /// <summary>ARC-UI-2278, ARC-UI-2289: the whole factor the screen's pixels are drawn at, as the bars and the dialog band (<see cref="AreaGameBarPanel.ScreenFactor"/>);
    /// a test may fix it. A bubble (its 200 px box, the 8 slots, the (1, 1) shadow) and a floating line (the 200 px box, the 90 px rise, the font) are laid out in the
    /// original's pixels and drawn at this factor.</summary>
    public int? FactorOverride { get; set; }
    public int Factor => FactorOverride is { } fixedFactor ? Math.Max(1, fixedFactor)
        : IsInsideTree() ? AreaGameBarPanel.ScreenFactor(GetViewport().GetVisibleRect().Size) : 1;
    /// <summary>ARC-UI-2510: the scale the screen's pixels are drawn at: the whole <see cref="Factor"/>, or in the Steamwoven layout the strips' scale where they put the 800 × 600 screen
    /// (<see cref="AreaGameBarPanel.ScreenScale"/>), so a line's size and rise follow the strips; a test may fix the factor.</summary>
    public float DrawScale => FactorOverride is { } fixedFactor ? Math.Max(1, fixedFactor)
        : IsInsideTree() ? AreaGameBarPanel.ScreenScale(GetViewport().GetVisibleRect().Size) : 1;

    /// <summary>Whether the original's font art draws the lines.</summary>
    internal bool ArtFontForSelfTest => _face is not null;

    public int LineHeight { get; }
    public FloaterField<string> Field { get; }
    /// <summary>ARC-UI-2289: the owner's tile centre on the screen (with the original's (40, 20), no draw offset), or null when it is not on the shown map; the layer
    /// takes the 90 px rise itself, in the original's pixels.</summary>
    public Func<string, Vector2?> Anchor { get; set; } = _ => null;
    /// <summary>The "text floaters" level: 0 draws nothing.</summary>
    public int Level { get; set; } = FloaterRules.DefaultLevel;

    public override void _Ready()
    {
        Name = "AreaFloaters";
        Layer = 1;
        // Added first, so the bubbles draw under the floating lines (the original draws them first, 0x4D5F10 then 0x4D5310).
        // Nearest: a Legacy glyph is whole pixels at the factor; an HD glyph keeps its own bilinear texture (ARC-UI-145).
        _bubbleSurface = new Control { Name = "BubbleSurface", MouseFilter = Control.MouseFilterEnum.Ignore, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        _bubbleSurface.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _bubbleSurface.Draw += DrawBubbles;
        if (_face is not null) AreaArtTextDraw.Prepare(_bubbleSurface);
        AddChild(_bubbleSurface);
        _surface = new Control { Name = "FloaterSurface", MouseFilter = Control.MouseFilterEnum.Ignore, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        _surface.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _surface.Draw += DrawLines;
        if (_face is not null) AreaArtTextDraw.Prepare(_surface);
        AddChild(_surface);
    }

    // ARC-UI-2510: the strips' scale changed (the layout turned on or off, a new window size): the lines and bubbles are laid out again.
    public override void _EnterTree() => AreaGameBarPanel.TopScreenChanged += Redraw;

    public override void _ExitTree() => AreaGameBarPanel.TopScreenChanged -= Redraw;

    // ARC-UI-2510: a whole scale keeps the Legacy glyphs' whole pixels (Nearest); the strips' fractional scale samples Linear, as the strips do.
    private static void FilterFor(Control surface, float scale)
    {
        var filter = MathF.Abs(scale - MathF.Round(scale)) < 0.001f ? CanvasItem.TextureFilterEnum.Nearest : CanvasItem.TextureFilterEnum.Linear;
        if (surface.TextureFilter != filter) surface.TextureFilter = filter;
    }

    public void Redraw() { if (IsInsideTree()) { _surface.QueueRedraw(); _bubbleSurface.QueueRedraw(); } }

    /// <summary>The width of a bubble's text in the layer's font.</summary>
    public int MeasureText(string text) => _face is { } face ? face.Font.Measure(text).Width : text.Length * NominalAdvance;

    /// <summary>Makes a bubble over <paramref name="owner"/>: the text wrapped and measured once, as the original renders it.</summary>
    public int AddBubble(string owner, string text, BubbleColor color, long nowMs)
    {
        var index = Bubbles.Create(owner, text, TextBubbleRules.Layout(text, BubbleLineHeight, MeasureText), color, nowMs);
        Redraw();
        return index;
    }

    /// <summary>What a bubble draw shows now: per bubble with an anchor, its lines (text, screen position), colour and placement slot. A bubble's slot is
    /// chosen the first time it is laid out with an anchor and kept. The draw and the smokes both read it. ARC-UI-2278: the layout is counted in the original's
    /// pixels (the anchor and the view divided by <see cref="Factor"/>, as at 800 × 600) and the result is in screen pixels, each distance times the factor.</summary>
    internal IReadOnlyList<(string Owner, BubbleColor Color, int Slot, Rect2 Rect, IReadOnlyList<(string Text, Vector2 Position)> Lines)> LayOutBubbles()
    {
        var drawn = new List<(string, BubbleColor, int, Rect2, IReadOnlyList<(string, Vector2)>)>();
        var view = BubbleView?.Invoke() ?? _bubbleSurface.GetRect();
        var factor = DrawScale;
        foreach (var bubble in Bubbles.Bubbles.ToArray())
        {
            if (BubbleAnchor(bubble.Owner) is not { } anchor) continue;
            var ax = (int)Math.Round(anchor.X / factor); var ay = (int)Math.Round(anchor.Y / factor);
            var slot = bubble.Slot ?? TextBubbleRules.ChooseSlot(ax, ay, bubble.Layout.Width, bubble.Layout.Height,
                (int)(view.Position.X / factor), (int)(view.Position.Y / factor), (int)(view.Size.X / factor), (int)(view.Size.Y / factor));
            if (bubble.Slot is null) Bubbles.SetSlot(bubble.Owner, slot);
            var (left, top) = TextBubbleRules.SlotPosition(slot, ax, ay, bubble.Layout.Width, bubble.Layout.Height);
            var lines = bubble.Layout.Lines.Select((l, i) => (l.Text, new Vector2(left + l.Left, top + i * BubbleLineHeight) * factor)).ToArray();
            drawn.Add((bubble.Owner, bubble.Color, slot, new Rect2(left * factor, top * factor, bubble.Layout.Width * factor, bubble.Layout.Height * factor), lines));
        }
        return drawn;
    }

    private void DrawBubbles()
    {
        // ARC-UI-2278: drawn in the original's pixels at the screen's whole factor; the layout returned screen pixels, so each position goes back by the factor.
        var factor = DrawScale;
        FilterFor(_bubbleSurface, factor);
        _bubbleSurface.DrawSetTransform(Vector2.Zero, 0, new Vector2(factor, factor));
        foreach (var (_, color, _, _, scaled) in LayOutBubbles())
        {
            var lines = scaled.Select(l => (l.Text, l.Position / factor)).ToArray();
            var (r, g, b) = TextBubbleRules.Rgb(color);
            var (sr, sg, sb) = TextBubbleRules.ShadowRgb;
            foreach (var (text, position) in lines)
            {
                if (_face is not { } face) continue;
                // The shadow first at +1,+1 (font flag 0x08), then the line.
                AreaArtTextDraw.Text(_bubbleSurface, face, text, (int)MathF.Round(position.X) + TextBubbleRules.ShadowOffset, (int)MathF.Round(position.Y) + TextBubbleRules.ShadowOffset, (sr, sg, sb), drawSetScale: factor);
                AreaArtTextDraw.Text(_bubbleSurface, face, text, (int)MathF.Round(position.X), (int)MathF.Round(position.Y), (r, g, b), drawSetScale: factor);
            }
        }
    }

    /// <summary>What a draw shows now: per owner with a point, each line's text, colour, screen position and opacity. ARC-UI-2289: the box is counted in the original's
    /// pixels (the anchor divided by <see cref="Factor"/>, as at 800 × 600: 200 px wide, 90 px of rise) and the positions are in screen pixels, each distance times the factor.</summary>
    internal IReadOnlyList<(string Owner, string Text, FloaterColor Color, Vector2 Position, int Alpha)> DrawnForSelfTest()
    {
        var drawn = new List<(string, string, FloaterColor, Vector2, int)>();
        if (Level == FloaterRules.Never) return drawn;
        var factor = DrawScale;
        foreach (var owner in Field.Owners)
        {
            if (Anchor(owner) is not { } anchor) continue;
            var (left, top) = FloaterRules.Box((int)Math.Round(anchor.X / factor), (int)Math.Round(anchor.Y / factor), LineHeight);
            foreach (var line in Field.Lines(owner))
            {
                var alpha = FloaterRules.Alpha(line.Offset, LineHeight);
                if (alpha == 0) continue;
                drawn.Add((owner, line.Text, line.Color, new Vector2(FloaterRules.LineLeft(left, MeasureText(line.Text)), top + line.Offset) * factor, alpha));
            }
        }
        return drawn;
    }

    private void DrawLines()
    {
        // ARC-UI-2289: drawn in the original's pixels at the screen's whole factor; the layout returned screen pixels, so each position goes back by the factor.
        var factor = DrawScale;
        FilterFor(_surface, factor);
        _surface.DrawSetTransform(Vector2.Zero, 0, new Vector2(factor, factor));
        foreach (var (_, text, color, scaledPosition, alpha) in DrawnForSelfTest())
        {
            var position = scaledPosition / factor;
            var (r, g, b) = FloaterRules.Rgb(color);
            if (_face is not { } face) continue;
            AreaArtTextDraw.Text(_surface, face, text, (int)MathF.Round(position.X), (int)MathF.Round(position.Y), (r, g, b), alpha: (byte)alpha, drawSetScale: factor);
        }
    }
}

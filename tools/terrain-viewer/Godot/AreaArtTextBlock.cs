using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>One text of an <see cref="AreaArtTextBlock"/>: its font and its line height, lines (offsets from <see cref="X"/>, <see cref="Y"/> in the block) and colour.</summary>
public sealed record AreaArtTextItem(AreaArtFontFace Face, int LineHeight, IReadOnlyList<AreaArtTextLine> Lines, int X, int Y, (byte R, byte G, byte B) Colour);

/// <summary>
/// ARC-DEV-156 (B27): a control that draws placed art-font text (<see cref="AreaArtTextDraw"/>, the original's blend over what the
/// control's parents drew under it), the message window's texts and the floating tooltip's. It draws nothing else; with
/// <see cref="FitContent"/> its minimum size is the texts' extent.
/// </summary>
public partial class AreaArtTextBlock : Control
{
    private IReadOnlyList<AreaArtTextItem> _items = [];

    public bool FitContent { get; set; }
    public IReadOnlyList<AreaArtTextItem> Items => _items;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AreaArtTextDraw.Prepare(this);
    }

    public void Set(IReadOnlyList<AreaArtTextItem> items)
    {
        _items = items ?? throw new ArgumentNullException(nameof(items));
        UpdateMinimumSize();
        QueueRedraw();
    }

    public override Vector2 _GetMinimumSize()
    {
        if (!FitContent) return Vector2.Zero;
        var (width, height) = (0, 0);
        foreach (var item in _items)
            foreach (var line in item.Lines)
            {
                var span = line.Glyphs.Count == 0 ? 0 : line.Glyphs[^1].X + line.Glyphs[^1].Glyph.Width;
                width = Math.Max(width, item.X + line.X + span);
                height = Math.Max(height, item.Y + line.Y + item.LineHeight);
            }
        return new(width, height);
    }

    public override void _Draw()
    {
        foreach (var item in _items)
            foreach (var line in item.Lines)
                AreaArtTextDraw.Glyphs(this, item.Face, line.Glyphs, item.X + line.X, item.Y + line.Y, item.Colour);
    }
}

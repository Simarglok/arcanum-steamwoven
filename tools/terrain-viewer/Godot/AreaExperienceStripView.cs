using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-1365: the experience progress strip of the bottom interface strip, under the hot-key bar (ledger GB13, GB14): the pieces
/// <see cref="AreaExperienceStripLayout.Pieces"/> picks for the hero's progress toward the next level, each drawn from its own interface
/// art at the table's place in the strip's own coordinates (the panel scales the whole strip), the last row cut to its percent of the
/// art's width. The strip's art under it is the original's empty track, so nothing is repainted first. The view takes no pointer.
/// </summary>
public partial class AreaExperienceStripView : Control
{
    private IReadOnlyDictionary<int, Texture2D[]> _art = new Dictionary<int, Texture2D[]>();
    private int? _progress;

    /// <summary>The pieces the last draw put on the strip, with the width each one was cut to, for the self-test.</summary>
    internal IReadOnlyList<(AreaExperiencePiece Piece, int Width)> DrawnForSelfTest { get; private set; } = [];

    public override void _Ready()
    {
        Name = "ExperienceStrip";
        MouseFilter = MouseFilterEnum.Ignore;
        Position = Vector2.Zero;
        Size = CustomMinimumSize = new Vector2(AreaGameBarLayout.ScreenWidth, AreaGameBarLayout.BottomHeight);
    }

    /// <summary>Gives the view the hero's progress in thousandths (null: nothing drawn) and the arts' frames by number.</summary>
    public void Set(int? progress, IReadOnlyDictionary<int, Texture2D[]> art)
    {
        if (_progress == progress && ReferenceEquals(_art, art)) return;
        _progress = progress; _art = art;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var drawn = new List<(AreaExperiencePiece, int)>();
        foreach (var piece in AreaExperienceStripLayout.Pieces(_progress))
        {
            if (!_art.TryGetValue(piece.Art, out var frames) || frames.Length == 0) continue;
            var texture = frames[0];
            var width = piece.WidthPercent >= 100 ? texture.GetWidth() : AreaExperienceStripLayout.CutWidth(texture.GetWidth(), piece.WidthPercent);
            if (width <= 0) continue;
            DrawTextureRectRegion(texture, new Rect2(piece.X, piece.Y, width, texture.GetHeight()), new Rect2(0, 0, width, texture.GetHeight()));
            drawn.Add((piece, width));
        }
        DrawnForSelfTest = drawn;
    }
}

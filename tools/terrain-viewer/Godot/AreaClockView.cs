using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-1111: the clock window of the original's top strip (ledger GB8, GB9): a 128 × 30 window at (648, 5) filled black, the sky
/// slices of the last refresh (<see cref="AreaClockFace"/>: the day strip and the moon piece, each cut at its offset) and the needle at
/// (708, 6). The window is also the hit area of the clock button, so it takes the pointer. Frames are drawn at the art's own size from
/// the strip's own coordinates (the panel scales the whole strip).
/// </summary>
public partial class AreaClockView : Control
{
    private Texture2D? _strip, _moon, _needle;
    private AreaClockFace? _face;

    /// <summary>The slices the last draw cut (empty without a face or the art), for the self-test.</summary>
    internal IReadOnlyList<AreaClockSlice> SlicesForSelfTest { get; private set; } = [];
    /// <summary>Whether the needle was drawn at the last draw.</summary>
    internal bool NeedleForSelfTest { get; private set; }
    /// <summary>The moon art the last draw used (null without a face).</summary>
    internal int? MoonArtForSelfTest => _face?.MoonArt;

    public override void _Ready()
    {
        Name = "Clock";
        MouseFilter = MouseFilterEnum.Stop;
        Position = new Vector2(AreaClockLayout.WindowX, AreaClockLayout.WindowY);
        Size = CustomMinimumSize = new Vector2(AreaClockLayout.WindowWidth, AreaClockLayout.WindowHeight);
    }

    /// <summary>Gives the view what to draw: the face (null: the window stays black) and the three arts' textures.</summary>
    public void Set(AreaClockFace? face, Texture2D? strip, Texture2D? moon, Texture2D? needle)
    {
        if (_face == face && _strip == strip && _moon == moon && _needle == needle) return;
        _face = face; _strip = strip; _moon = moon; _needle = needle;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, AreaClockLayout.WindowWidth, AreaClockLayout.WindowHeight), Colors.Black);
        var slices = new List<AreaClockSlice>();
        if (_face is { } face && _strip is not null && _moon is not null)
        {
            foreach (var slice in face.Slices(_strip.GetWidth(), _moon.GetWidth()))
            {
                var art = slice.Piece == AreaClockPiece.Strip ? _strip : _moon;
                // The source rectangle is 30 rows high; the art has 29, so its last row stays black (the window is filled first).
                var rows = Math.Min(AreaClockLayout.WindowHeight, art.GetHeight());
                DrawTextureRectRegion(art, new Rect2(slice.DestX - AreaClockLayout.WindowX, 0, slice.Width, rows), new Rect2(slice.SourceX, 0, slice.Width, rows));
                slices.Add(slice);
            }
        }
        SlicesForSelfTest = slices;
        NeedleForSelfTest = _face is not null && _needle is not null;
        if (NeedleForSelfTest) DrawTexture(_needle!, new Vector2(AreaClockLayout.NeedleX - AreaClockLayout.WindowX, AreaClockLayout.NeedleY - AreaClockLayout.WindowY));
    }
}

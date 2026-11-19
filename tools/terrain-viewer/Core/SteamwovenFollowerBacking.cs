namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-2925 (AC3): the small brass backing of the follower group's show/hide button in the Steamwoven column. The group stands at the frame's left edge over the world, on the composition's 800 × 600 units;
/// the button (FP1, 67 × 13 at (0, 428)) gets a plate of its own so it reads against the world as the strips do. Everything is in the original's units, so the plate scales with the group. Godot-free; the viewer draws the rectangles.
/// </summary>
public static class SteamwovenFollowerBacking
{
    /// <summary>How far the plate reaches above and below the button, in units.</summary>
    public const int Margin = 2;

    /// <summary>How far the plate reaches right of the button, in units (its left edge is the screen's edge).</summary>
    public const int RightMargin = 3;

    /// <summary>The width of each of the three brass tones of the plate's edge (dark, bright, mid), in units, from the outside in.</summary>
    public const float ToneWidth = 0.5f;

    /// <summary>The plate behind a button rectangle (x, y, width, height in units): the button with the margins around it, flush with the left edge.</summary>
    public static (float X, float Y, float Width, float Height) Plate((int X, int Y, int Width, int Height) button) =>
        (button.X, button.Y - Margin, button.Width + RightMargin, button.Height + 2 * Margin);

    /// <summary>
    /// The nested layers of the plate from the outside in: the dark outline, the bright edge and the mid-brass body, each inset by <see cref="ToneWidth"/> more than the last on the top, right and bottom;
    /// the left side runs off the screen's edge, so no brass line stands at the window's edge.
    /// </summary>
    public static IReadOnlyList<(float X, float Y, float Width, float Height, int Tone)> Layers((int X, int Y, int Width, int Height) button)
    {
        var plate = Plate(button);
        var layers = new List<(float, float, float, float, int)>(3);
        for (var tone = 0; tone < 3; tone++)
        {
            var inset = tone * ToneWidth;
            var left = tone == 0 ? plate.X : plate.X - 2; // the outline is the plate itself; the inner tones start beyond the screen's edge, so the outline shows on three sides
            layers.Add((left, plate.Y + inset, plate.X + plate.Width - inset - left, plate.Height - 2 * inset, tone));
        }
        return layers;
    }

    /// <summary>Whether the plate is drawn: in the column mode with the interface bars drawn (the hidden-interface places stand on the world's bottom edge and have no strip to match).</summary>
    public static bool Shown(bool columnMode, bool barsDrawn) => columnMode && barsDrawn;
}

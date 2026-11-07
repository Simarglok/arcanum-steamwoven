using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-089: the fill of a strip's sides on a screen wider than 4:3 (ARC-RES-093 layout: the 800-unit strip centred, the rest filled by
/// tiling a piece of the strip; ARC-RES-094 U4 chose the pieces on the HD redraws). One <see cref="Tile"/> is a column run of the strip's own
/// art, in UI units; it repeats outwards from the strip's edge, the last repeat cut to the margin that is left. Godot-free arithmetic.
/// </summary>
public static class AreaStripSides
{
    /// <summary>The strip's width in UI units (both strips are 800 wide, ledger GB1).</summary>
    public const int StripWidth = 800;

    /// <summary>A run of columns of the strip's art, in UI units.</summary>
    public readonly record struct Tile(int X, int Width);

    /// <summary>One drawn piece: its left edge in the strip's own coordinates, the first source column and the width, all in UI units.</summary>
    public readonly record struct Run(float DestX, float SourceX, float Width);

    /// <summary>The tile each side repeats. Top strip: plain wood between the button groups (x 186, 32 wide), both sides. Bottom strip: the
    /// wood rail at its own end, 8 columns (left 0–7, right 792–799), so the brass strip and the vial housings never repeat.</summary>
    public static (Tile Left, Tile Right) Tiles(AreaBarStrip strip) => strip == AreaBarStrip.Top
        ? (new Tile(186, 32), new Tile(186, 32))
        : (new Tile(0, 8), new Tile(StripWidth - 8, 8));

    /// <summary>The pieces that fill <paramref name="margin"/> UI units to the left of the strip, nearest piece first; none for a margin
    /// of a quarter unit or less (a 4:3 screen).</summary>
    public static IReadOnlyList<Run> Left(AreaBarStrip strip, float margin) => Fill(Tiles(strip).Left, margin, left: true);

    /// <summary>The pieces that fill <paramref name="margin"/> UI units to the right of the strip, nearest piece first.</summary>
    public static IReadOnlyList<Run> Right(AreaBarStrip strip, float margin) => Fill(Tiles(strip).Right, margin, left: false);

    private static IReadOnlyList<Run> Fill(Tile tile, float margin, bool left)
    {
        if (tile.Width <= 0) throw new ArgumentOutOfRangeException(nameof(tile));
        if (!(margin > 0.25f)) return [];
        var runs = new List<Run>();
        for (float covered = 0; covered < margin - 0.001f; covered += tile.Width)
        {
            var width = Math.Min(tile.Width, margin - covered);
            // Left side: the nearest piece ends at x = 0 and the last one is cut on its far (left) side, so it keeps the tile's right columns.
            runs.Add(left ? new(-covered - width, tile.X + tile.Width - width, width) : new(StripWidth + covered, tile.X, width));
        }
        return runs;
    }
}

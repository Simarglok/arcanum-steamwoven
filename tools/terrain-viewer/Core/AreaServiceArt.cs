using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-1765: the parts of the item identify and repair window's original look that change from frame to frame (ledger IS2, IW20, IW26–IW27): the NPC's image art (null: none), the quote line's text
/// and the item the window remembered (the one last quoted; the original's global 0x681458), whose grid cells are filled.
/// </summary>
public sealed record AreaServiceFrame(int? Image, string Quote, string? Selected);

/// <summary>
/// ARC-UI-1765: the layout of the item identify (interface mode 5) and item repair (mode 6) windows of the original look, read in the open 0x5727B0 and the redraw 0x576520. Both are the player's own
/// window without its boxes, its weight lines and its map drop: background 223 `PDoll` (358 × 400) and 221 `Inventor` as the own window's (IW20), the hero's grid and doll as there, the NPC's image at
/// (141, 36) at half its size (the redraw's mode 5 and 6 branch asks the image function 0x553D10 about the other party and blits it with a halved size, 0x4CE680 for a portrait), and the NPC's quote line in
/// the rectangle (180, 29, 155 × 65) in the window's text font. The item the window remembers (0x681458) has its cells filled before its art is drawn, with the colour of the too heavy weapon's fill
/// (IW43). Pure arithmetic; the window's Godot drawing is <c>AreaInventoryGridArea</c>.
/// </summary>
public static class AreaServiceArtLayout
{
    /// <summary>The background art of both windows: the own window's `PDoll` (IW20).</summary>
    public const int BackgroundArt = AreaInventoryArtLayout.DollArt;

    /// <summary>The NPC's image corner in the frame (0x5CAF80 holds the mode 1 and 2 corners; modes 5 and 6 pass 0x8D, 0x24 to the blit).</summary>
    public static readonly (int X, int Y) Image = (141, 36);

    /// <summary>The quote line's rectangle (x, y, width, height; table 0x5CAFB0).</summary>
    public static readonly (int X, int Y, int Width, int Height) QuoteLine = (180, 29, 155, 65);

    /// <summary>ARC-UI-1820: the sunk name plate of the `PDoll` frame the NPC's image and quote line lie on, in frame pixels (x, y, width, height): the generated interface's recipe of that frame
    /// (<c>InterfaceChromeRecipes</c>, "pdoll.art") paints exactly this plate. The image (half size) lies inside it; the quote line's first lines do, and a line that wraps past its
    /// bottom (the rectangle is 10 pixels taller than the plate) falls on the frame's wood as in the original.</summary>
    public static readonly (int X, int Y, int Width, int Height) NamePlate = (124, 25, 223, 55);

    /// <summary>ARC-UI-1820: whether the image of an art of <paramref name="width"/> × <paramref name="height"/> pixels, at half size and at its corner, lies inside <see cref="NamePlate"/> with a
    /// two unit margin (the plate's bevel and ink line), so no picture cuts into the generated plate's edge.</summary>
    public static bool ImageFitsNamePlate(int width, int height)
    {
        var (x, y, w, h) = ImageRect(width, height);
        var (px, py, pw, ph) = NamePlate;
        return x >= px + 2 && y >= py + 2 && x + w <= px + pw - 2 && y + h <= py + ph - 2;
    }

    /// <summary>The colour (red, green, blue) the remembered item's cells are filled with: the red channel at table index 0x40 of 0xFF, the other two at their first entry, as the too heavy weapon's fill
    /// (<see cref="AreaLootArtLayout.TooHeavyFill"/>, IW43; the redraw builds both from the same expression).</summary>
    public static readonly (int Red, int Green, int Blue) SelectedFill = AreaLootArtLayout.TooHeavyFill;

    /// <summary>The image's rectangle for an art of <paramref name="width"/> × <paramref name="height"/> pixels: its corner and half its size (an integer halving, as the blit's).</summary>
    public static (float X, float Y, float Width, float Height) ImageRect(int width, int height) => (Image.X, Image.Y, width >> 1, height >> 1);

    /// <summary>The item under a frame point of the hero's window (a grid cell or a worn slot: the own window's item picker), or null: the drop and use boxes are not made in these modes, so a point on them names no item.</summary>
    public static string? ItemAt(AreaInventoryView view, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(view);
        var hit = AreaInventoryDrag.HitTest(x, y);
        return hit.Icon is not null ? null : AreaInventoryDrag.Press(view, hit)?.Entity;
    }

    /// <summary>The frame rectangle of the remembered item's cells (the grid's origin and the item's cell size), or null when the item is not in the hero's grid (a worn item has no fill).</summary>
    public static (float X, float Y, float Width, float Height)? SelectedRect(AreaInventoryView view, string? selected)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (selected is null || view.Grid.FirstOrDefault(item => item.Entity == selected) is not { } item) return null;
        const int cell = AreaInventoryDrag.CellPixels;
        return (AreaInventoryDrag.GridX + item.Slot % 10 * cell, AreaInventoryDrag.GridY + item.Slot / 10 * cell, item.Width * cell, item.Height * cell);
    }
}

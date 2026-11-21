using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-3037: what the original's background arts paint into themselves and a window still needs (a field a number or text stands on, a cell grid, a page the text is written on), drawn by the window under its content while the
/// kit's background shows, a stand-in until the kit's layers for them exist (the sheets' and backgrounds' batch, <a href="https://github.com/Simarglok/arcanum-steamwoven/issues/3098">#3098</a>). Only the elements the owner marked
/// as required are here (the TRD's table "Elements the old arts held"); the decorative ones (compass rose, gears, tab sockets, rivets, brass rims) are left to the kit. Coordinates are the window's units (800 × 400), from the recipes
/// of <c>InterfaceChrome</c>; <c>scale</c> is the device pixels of one unit where the node is not itself scaled by it (the inventory's frame), else 1. A stand-in is plain: black fields with a grey-blue edge, paper for the blueprint.
/// </summary>
internal static class AreaKitStandIns
{
    private static readonly Color Black = new(0f, 0f, 0f), Edge = new(.2f, .24f, .32f), Paper = new(.93f, .88f, .72f), Rule = new(.78f, .7f, .5f);
    private static readonly HashSet<SteamwovenKitWindow> Drawn = [];

    /// <summary>The windows whose stand-ins were drawn since <see cref="ResetForSelfTest"/>, for the smoke (a window that draws none of its own art over the kit has passed here).</summary>
    internal static IReadOnlyCollection<SteamwovenKitWindow> DrawnForSelfTest => [.. Drawn];

    internal static void ResetForSelfTest() => Drawn.Clear();

    private static void Field(CanvasItem canvas, float scale, float x, float y, float w, float h, Color? fill = null, bool edge = true)
    {
        var rect = new Rect2(x * scale, y * scale, w * scale, h * scale);
        canvas.DrawRect(rect, fill ?? Black);
        if (edge) canvas.DrawRect(rect, Edge, false);
    }

    private static void Lines(CanvasItem canvas, float scale, float x, float y, int columns, int rows, float cell, Color colour)
    {
        var width = Math.Max(1f, scale * .5f);
        for (var column = 0; column <= columns; column++) canvas.DrawLine(new Vector2((x + column * cell) * scale, y * scale), new Vector2((x + column * cell) * scale, (y + rows * cell) * scale), colour, width);
        for (var row = 0; row <= rows; row++) canvas.DrawLine(new Vector2(x * scale, (y + row * cell) * scale), new Vector2((x + columns * cell) * scale, (y + row * cell) * scale), colour, width);
    }

    /// <summary>The own inventory and the identify and repair windows (arts 223 and 221): the pack's pit and its 10 × 12 grid, the five count pits, the name plate's pit, the doll's pit with its 10 × 9 equipment grid, and the two number plates at the bottom.</summary>
    internal static void Inventory(CanvasItem canvas, float scale)
    {
        Drawn.Add(SteamwovenKitWindow.Inventory);
        Field(canvas, scale, 366, 6, 324, 388);
        Lines(canvas, scale, 368, 8, 10, 12, 32, Edge);
        for (var k = 0; k < 5; k++) Field(canvas, scale, 727, 71 + 23 * k, 61, 17);
        Field(canvas, scale, 124, 25, 223, 55);
        Field(canvas, scale, 18, 100, 330, 300);
        Lines(canvas, scale, 23, 107, 10, 9, 32, Edge);
        Field(canvas, scale, 10, 303, 112, 97);
        Field(canvas, scale, 244, 303, 114, 97);
    }

    /// <summary>The stat labels baked into art 22 (left and right column, top to bottom) and where each one's text starts: the sheet draws them with its own font over the stat ovals' stand-ins.</summary>
    internal static IEnumerable<(string Text, int X, int Y)> StatLabels()
    {
        string[] left = ["ST", "CN", "DX", "BE"], right = ["IN", "WP", "PE", "CH"];
        for (var r = 0; r < 4; r++) { yield return (left[r], 131, 115 + 40 * r); yield return (right[r], 263, 115 + 40 * r); }
    }

    /// <summary>The character sheet's main page (art 22): the name and info box, the eight stat ovals with their labels (the letters are baked into the art), the health and fatigue discs, the alignment disc, the aptitude ruler and the derived-stats box.</summary>
    internal static void CharacterSheet(CanvasItem canvas)
    {
        Drawn.Add(SteamwovenKitWindow.Character);
        Field(canvas, 1, 119, 9, 360, 87);
        Field(canvas, 1, 24, 278, 452, 113);
        Field(canvas, 1, 6, 131, 100, 100);
        Field(canvas, 1, 411, 127, 76, 56);
        Field(canvas, 1, 411, 197, 76, 56);
        Field(canvas, 1, 747, 60, 46, 330);
        for (var r = 0; r < 4; r++)
        {
            Field(canvas, 1, 172, 111 + 40 * r, 70, 31);
            Field(canvas, 1, 315, 111 + 40 * r, 70, 31);
        }
    }

    /// <summary>The world map (art 138): the two readout pits the pointer's notes and coordinates are written in.</summary>
    internal static void WorldMap(CanvasItem canvas)
    {
        Drawn.Add(SteamwovenKitWindow.WorldMap);
        Field(canvas, 1, 680, 157, 94, 22);
        Field(canvas, 1, 680, 189, 94, 22);
    }

    /// <summary>The schematic window (art 365): the blueprint page (paper ruled every 32 from 227, 22), the two component slot boxes and the assemble channel.</summary>
    internal static void Schematic(CanvasItem canvas)
    {
        Drawn.Add(SteamwovenKitWindow.Schematic);
        Field(canvas, 1, 201, 13, 506, 367, Paper, edge: false);
        for (var x = 227f; x < 707; x += 32) canvas.DrawLine(new Vector2(x, 13), new Vector2(x, 380), Rule, 1);
        for (var y = 22f; y < 380; y += 32) canvas.DrawLine(new Vector2(201, y), new Vector2(707, y), Rule, 1);
        Field(canvas, 1, 577, 179, 103, 71, Paper);
        Field(canvas, 1, 577, 275, 103, 71, Paper);
        Field(canvas, 1, 39, 334, 126, 30);
    }

    /// <summary>The logbook (arts 264 and 260): nothing the kit's two pieces do not carry (the pages are the book's own picture); only the mark for the smoke.</summary>
    internal static void Logbook() => Drawn.Add(SteamwovenKitWindow.Logbook);
}

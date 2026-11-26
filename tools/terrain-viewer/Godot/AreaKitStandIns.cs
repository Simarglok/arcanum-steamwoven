using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-3037: what the original's background arts paint into themselves and a window still needs (a field a number or text stands on, a cell grid, a page the text is written on), drawn by the window under its content while the
/// kit's background shows. <b>ARC-UI-3098:</b> each such element is a kit part (<see cref="AreaKitFixtures"/>, <c>fixture.*</c> in kit.json) drawn at its place when the set holds it at the table's size; the plain stand-in below is
/// drawn only for an element whose part the set lacks (one element at a time), so a half-made kit still reads. Only the elements the owner marked
/// as required are here (the TRD's table "Elements the old arts held"); the decorative ones (compass rose, gears, tab sockets, rivets, brass rims) are left to the kit. Coordinates are the window's units (800 × 400), from the recipes
/// of the retired generated chrome (ARC-UI-3143; the table is in the TRD); <c>scale</c> is the device pixels of one unit where the node is not itself scaled by it (the inventory's frame), else 1. A stand-in is plain: black fields with a grey-blue edge, paper for the blueprint.
/// </summary>
internal static class AreaKitStandIns
{
    private static readonly Color Black = new(0f, 0f, 0f), Edge = new(.2f, .24f, .32f), Paper = new(.93f, .88f, .72f), Rule = new(.78f, .7f, .5f);
    private static readonly HashSet<SteamwovenKitWindow> Drawn = [];

    /// <summary>The windows whose stand-ins were drawn since <see cref="ResetForSelfTest"/>, for the smoke (a window that draws none of its own art over the kit has passed here).</summary>
    internal static IReadOnlyCollection<SteamwovenKitWindow> DrawnForSelfTest => [.. Drawn];

    internal static void ResetForSelfTest() { Drawn.Clear(); DrawnScreens.Clear(); }

    private static readonly HashSet<SteamwovenKitScreen> DrawnScreens = [];

    /// <summary>ARC-UI-3038: the two-strip screens whose stand-ins were drawn since <see cref="ResetForSelfTest"/>.</summary>
    internal static IReadOnlyCollection<SteamwovenKitScreen> DrawnScreensForSelfTest => [.. DrawnScreens];

    private static void Field(CanvasItem canvas, float scale, float x, float y, float w, float h, Color? fill = null, bool edge = true)
    {
        var rect = new Rect2(x * scale, y * scale, w * scale, h * scale);
        canvas.DrawRect(rect, fill ?? Black);
        if (edge) canvas.DrawRect(rect, Edge, false);
    }

    /// <summary>The lines after the cells of a grid with its corner at (<paramref name="x"/>, <paramref name="y"/>), none before the first cell; without <paramref name="farEdges"/> none after the last column and row either.</summary>
    private static void Lines(CanvasItem canvas, float scale, float x, float y, int columns, int rows, float cell, Color colour, bool farEdges)
    {
        var width = Math.Max(1f, scale * .5f);
        var skip = farEdges ? 0 : 1;
        for (var column = 1; column <= columns - skip; column++) canvas.DrawLine(new Vector2((x + column * cell) * scale, y * scale), new Vector2((x + column * cell) * scale, (y + rows * cell) * scale), colour, width);
        for (var row = 1; row <= rows - skip; row++) canvas.DrawLine(new Vector2(x * scale, (y + row * cell) * scale), new Vector2((x + columns * cell) * scale, (y + row * cell) * scale), colour, width);
    }

    private static readonly Color Brass = new(.72f, .55f, .2f), Parchment = new(.8f, .74f, .58f), RedInk = new(.72f, .12f, .1f), Grey = new(.45f, .45f, .5f), DarkWood = new(.2f, .14f, .1f), Pale = new(.72f, .72f, .62f);

    private static Vector2 P(float scale, float x, float y) => new(x * scale, y * scale);

    private static void Disc(CanvasItem canvas, float scale, float x, float y, float r, Color fill) => canvas.DrawCircle(P(scale, x, y), r * scale, fill);

    private static void Ring(CanvasItem canvas, float scale, float x, float y, float r, Color colour, float width = 2) => canvas.DrawArc(P(scale, x, y), r * scale, 0, Mathf.Tau, 48, colour, width * scale);

    private static void Poly(CanvasItem canvas, float scale, Color colour, params (float X, float Y)[] points) => canvas.DrawColoredPolygon([.. points.Select(point => P(scale, point.X, point.Y))], colour);

    private static void Segment(CanvasItem canvas, float scale, float x1, float y1, float x2, float y2, Color colour, float width = 1) => canvas.DrawLine(P(scale, x1, y1), P(scale, x2, y2), colour, Math.Max(1f, width * scale));

    private static void Arrow(CanvasItem canvas, float scale, float x, float y, float length, float half, Color colour) =>
        Poly(canvas, scale, colour, (x, y - half * .4f), (x + length * .55f, y - half * .4f), (x + length * .55f, y - half), (x + length, y), (x + length * .55f, y + half), (x + length * .55f, y + half * .4f), (x, y + half * .4f));

    /// <summary>A toothed wheel: a disc with <paramref name="teeth"/> teeth of the radius <paramref name="r"/>.</summary>
    private static void Gear(CanvasItem canvas, float scale, float x, float y, float r, Color colour, int teeth = 12)
    {
        var points = new List<(float, float)>();
        for (var i = 0; i < teeth * 2; i++)
        {
            var (angle, radius) = (Mathf.Tau * i / (teeth * 2), i % 2 == 0 ? r : r * .8f);
            points.Add((x + Mathf.Cos(angle) * radius, y + Mathf.Sin(angle) * radius));
        }
        Poly(canvas, scale, colour, [.. points]);
        Disc(canvas, scale, x, y, r * .35f, DarkWood);
    }

    /// <summary>A filled quarter disc whose corner is (<paramref name="x"/>, <paramref name="y"/>) and which opens to the right (<paramref name="right"/> true) or to the left, with a brass arc: the frame round a weapon's or a shield's slot.</summary>
    private static void QuarterFrame(CanvasItem canvas, float scale, float x, float y, float r, bool right, Color fill)
    {
        var points = new List<(float, float)> { (x, y) };
        for (var i = 0; i <= 16; i++)
        {
            var angle = Mathf.Pi / 2 * i / 16;
            points.Add((x + (right ? 1 : -1) * Mathf.Sin(angle) * r, y - Mathf.Cos(angle) * r));
        }
        Poly(canvas, scale, fill, [.. points]);
        for (var i = 0; i < 16; i++)
        {
            var (a1, a2) = (Mathf.Pi / 2 * i / 16, Mathf.Pi / 2 * (i + 1) / 16);
            Segment(canvas, scale, x + (right ? 1 : -1) * Mathf.Sin(a1) * r, y - Mathf.Cos(a1) * r, x + (right ? 1 : -1) * Mathf.Sin(a2) * r, y - Mathf.Cos(a2) * r, Brass, 3);
        }
    }

    /// <summary>One element: the kit's fixture <paramref name="id"/> at (<paramref name="x"/>, <paramref name="y"/>) units, else <paramref name="standIn"/>; true when the kit's part was drawn.</summary>
    private static bool Part(CanvasItem canvas, float scale, string id, float x, float y, Action standIn)
    {
        if (AreaKitFixtures.Draw(canvas, scale, id, x, y)) return true;
        standIn();
        return false;
    }

    /// <summary>A grid of <paramref name="columns"/> x <paramref name="rows"/> cells of 32 units with its corner at (<paramref name="x"/>, <paramref name="y"/>): copies of the kit's cell well, else the stand-in's lines over the black pit. The lines lie where the original's do (ARC-UI-3342): on the last unit of a cell, none before the first; <paramref name="farEdges"/> false (the paper doll, whose frame takes that place) leaves none after the last column and row.</summary>
    internal static void Cells(CanvasItem canvas, float scale, float x, float y, int columns, int rows, bool farEdges = true)
    {
        if (AreaKitFixtures.Has("grid.inventory_cells"))
        {
            // The well carries its lines on its far edges: without them the last column and row are drawn a unit short, and the pit's trim shows there.
            var cut = farEdges ? 0 : 1;
            for (var row = 0; row < rows; row++) for (var column = 0; column < columns; column++)
                AreaKitFixtures.DrawRegion(canvas, scale, "grid.inventory_cells", x + column * 32, y + row * 32, 0, 0, column == columns - 1 ? 32 - cut : 32, row == rows - 1 ? 32 - cut : 32);
        }
        else Lines(canvas, scale, x - .5f, y - .5f, columns, rows, 32, Edge, farEdges);
    }

    /// <summary>The pack's pit with its 10 x 12 grid, the five count pits and their five signs (art 221, shared by the inventory, loot, steal and barter windows).</summary>
    internal static void Pack(CanvasItem canvas, float scale)
    {
        Part(canvas, scale, "fixture.pack_pit", 366, 6, () => Field(canvas, scale, 366, 6, 324, 388));
        Cells(canvas, scale, 368, 8, 10, 12);
        for (var k = 0; k < 5; k++)
        {
            var y = 71 + 23 * k;
            Part(canvas, scale, "fixture.count_pit", 727, y, () => Field(canvas, scale, 727, y, 61, 17));
        }
        AreaKitFixtures.Draw(canvas, scale, "fixture.count_icons", 690, 68); // a sign has no stand-in: a number without its sign is the old art's loss the kit's part makes good
    }

    /// <summary>The holder's pit and grid in the loot and steal windows (10 x 8 cells at 8, 136) or the barter window (10 x 7 at 8, 168), and the barter's quote field.</summary>
    internal static void Holder(CanvasItem canvas, float scale, bool barter)
    {
        if (barter)
        {
            Part(canvas, scale, "fixture.holder_pit_barter", 6, 166, () => Field(canvas, scale, 6, 166, 324, 228));
            Cells(canvas, scale, 8, 168, 10, 7);
            Part(canvas, scale, "fixture.quote_field", 82, 125, () => Field(canvas, scale, 82, 125, 267, 36));
        }
        else
        {
            Part(canvas, scale, "fixture.holder_pit_loot", 6, 134, () => Field(canvas, scale, 6, 134, 324, 260));
            Cells(canvas, scale, 8, 136, 10, 8);
        }
    }

    /// <summary>The own inventory and the identify and repair windows (arts 223 and 221): the pack's pit and its 10 x 12 grid, the five count pits with their signs, the name plate's pit, the doll's pit with its 10 x 9 equipment grid, the quarter-disc frames of the weapon's and the shield's slots at the bottom, and the picture row "dots, arrow, grid" (the arrange button's legend).</summary>
    internal static void Inventory(CanvasItem canvas, float scale)
    {
        Drawn.Add(SteamwovenKitWindow.Inventory);
        Pack(canvas, scale);
        Part(canvas, scale, "fixture.name_pit", 124, 25, () => Field(canvas, scale, 124, 25, 223, 55));
        Part(canvas, scale, "fixture.doll_pit", 18, 100, () => Field(canvas, scale, 18, 100, 330, 300));
        Cells(canvas, scale, 23, 107, 10, 9, farEdges: false);
        Part(canvas, scale, "fixture.weapon_frame", 10, 303, () => QuarterFrame(canvas, scale, 10, 400, 97, true, new(.2f, .05f, .05f)));
        Part(canvas, scale, "fixture.shield_frame", 244, 303, () => QuarterFrame(canvas, scale, 358, 400, 97, false, new(.04f, .05f, .2f)));
        // The arrange legend (690, 192, 78, 32): scattered dots, a red arrow, a small grid.
        Part(canvas, scale, "fixture.arrange_legend", 690, 192, () =>
        {
            foreach (var (dx, dy) in new (float, float)[] { (0, 4), (6, 0), (6, 9), (12, 5), (14, 12), (19, 1), (20, 9) }) Field(canvas, scale, 693 + dx, 197 + dy, 4, 4, Pale, edge: false);
            Arrow(canvas, scale, 721, 205, 17, 7, RedInk);
            for (var gx = 0; gx < 4; gx++) for (var gy = 0; gy < 4; gy++) Field(canvas, scale, 741 + gx * 5, 197 + gy * 5, 4, 4, Pale, edge: false);
        });
    }

    /// <summary>ARC-UI-3299: the kit parts that carry the sheet's baked-in lettering in the old HD version's look (light silvery letters with their embossed edge and shadow; the dial's E&amp;G with its curls), cut from the player's HD frame by <c>tools/steamwoven_ui/label_parts.py</c>: part id and the place of its corner in the sheet's units.</summary>
    internal static readonly (string Id, int X, int Y)[] SheetLabelPartPlaces = [("fixture.sheet_stat_names_l", 120, 108), ("fixture.sheet_stat_names_r", 255, 108), ("fixture.sheet_dial_letters", 25, 146)];

    /// <summary>ARC-UI-3299: draws the sheet's label parts that the kit holds (the same fixtures path as the other details: <see cref="AreaKitFixtures"/>) and says which it drew; the sheet writes the text of <see cref="SheetLabels"/> itself only for the ones that are missing (a text-free surface never shows without its lettering: all three parts or the font's text, per group).</summary>
    internal static IReadOnlySet<string> SheetLabelParts(CanvasItem canvas)
    {
        var drawn = new HashSet<string>();
        foreach (var (id, x, y) in SheetLabelPartPlaces)
            if (AreaKitFixtures.Draw(canvas, 1, id, x, y)) drawn.Add(id);
        return drawn;
    }

    /// <summary>The words and numbers baked into art 22 that the sheet writes itself with its own font while the kit's background shows: text, where it starts, and the colour (the gold of the stat names, dark ink on the parchment of the dial and the ruler). The stat names and the dial's letters are left out for a group whose kit part is in <paramref name="drawnParts"/> (<see cref="SheetLabelParts"/>); the ruler's divisions are always text.</summary>
    internal static IEnumerable<(string Text, int X, int Y, (byte R, byte G, byte B) Rgb)> SheetLabels(IReadOnlySet<string>? drawnParts = null)
    {
        (byte, byte, byte) gold = (255, 210, 0), ink = (60, 45, 25);
        string[] left = ["ST", "CN", "DX", "BE"], right = ["IN", "WP", "PE", "CH"];
        bool leftPart = drawnParts?.Contains("fixture.sheet_stat_names_l") == true, rightPart = drawnParts?.Contains("fixture.sheet_stat_names_r") == true, dialPart = drawnParts?.Contains("fixture.sheet_dial_letters") == true;
        for (var r = 0; r < 4; r++)
        {
            if (!leftPart) yield return (left[r], 131, 115 + 40 * r, gold);
            if (!rightPart) yield return (right[r], 263, 115 + 40 * r, gold);
        }
        // The alignment dial's two ends (evil, good) and the magic-technology ruler's divisions: 75, 50, 25, 0, 25, 50, 75 at 32 units a step round the centre line (221). ARC-UI-3321: the kit's ruler part carries the divisions (cut from the old HD frame with the ticks; digits carry no language), so the text is written only where the part is missing.
        if (!dialPart) { yield return ("E", 38, 146, ink); yield return ("G", 66, 146, ink); }
        if (drawnParts?.Contains(RulerPart) == true) yield break;
        string[] steps = ["75", "50", "25", "0", "25", "50", "75"];
        for (var i = 0; i < steps.Length; i++) yield return (steps[i], 766, 120 + 32 * i, ink);
    }

    /// <summary>ARC-UI-3321: the kit part that carries the ruler's divisions.</summary>
    internal const string RulerPart = "fixture.mt_ruler";
    /// <summary>ARC-UI-3321: the alignment needle's frames; it has no stand-in (the art's own needle is drawn where the kit has none).</summary>
    internal const string NeedlePart = "fixture.align_needle";

    /// <summary>The character sheet's main page (art 22): the name and info box, the eight stat ovals, the alignment dial, the health disc with its heart and the fatigue disc with its cross, the magic-technology ruler with its ticks and its two end signs, and the derived-stats box (the letters and the ruler's numbers are written by the sheet, <see cref="SheetLabels"/>).</summary>
    internal static IReadOnlySet<string> CharacterSheet(CanvasItem canvas)
    {
        Drawn.Add(SteamwovenKitWindow.Character);
        var drawn = new HashSet<string>();
        void Draw(string id, float x, float y, Action standIn) { if (Part(canvas, 1, id, x, y, standIn)) drawn.Add(id); }
        Draw("fixture.sheet_name_box", 106, 2, () => Field(canvas, 1, 119, 9, 360, 87));
        Draw("fixture.sheet_derived_box", 10, 270, () => Field(canvas, 1, 24, 278, 452, 113));
        // ARC-UI-3321: the two boxes, the ovals, the dial, the discs and the ruler are cut from the old HD frame and carry their cast shadow, so a part's box is a little larger than the object: the oval's box starts at 167, 105 (its sockets reach 170.6 and 243.1), the rows 40 apart (the boxes overlap by 4 units, the shadow only fades inside them).
        for (var r = 0; r < 4; r++)
        {
            var y = 105 + 40 * r;
            Draw("fixture.stat_oval", 167, y, () => Field(canvas, 1, 172, y + 6, 70, 31));
            Draw("fixture.stat_oval", 310, y, () => Field(canvas, 1, 315, y + 6, 70, 31));
        }
        Draw("fixture.align_dial", 3, 128, () => DialStandIn(canvas));
        Draw("fixture.health_disc", 405, 122, () => HeartStandIn(canvas));
        Draw("fixture.fatigue_disc", 405, 192, () => CrossStandIn(canvas));
        Draw(RulerPart, 740, 60, () => RulerStandIn(canvas));
        return drawn;
    }

    // The alignment dial: a brass ring, a parchment half face with its tick marks, the number pit under the hub.
    private static void DialStandIn(CanvasItem canvas)
    {
        Disc(canvas, 1, 56, 181, 50, Brass);
        Disc(canvas, 1, 56, 181, 45, Parchment);
        for (var i = 0; i <= 12; i++)
        {
            var angle = Mathf.Pi + Mathf.Pi * i / 12;
            Segment(canvas, 1, 56 + Mathf.Cos(angle) * 38, 181 + Mathf.Sin(angle) * 38, 56 + Mathf.Cos(angle) * 44, 181 + Mathf.Sin(angle) * 44, DarkWood);
        }
        Field(canvas, 1, 6, 181, 100, 50, Brass, edge: false);
        Disc(canvas, 1, 56, 181, 8, Black);
        Field(canvas, 1, 40, 198, 34, 17);
    }

    // The health disc (a heart): a black disc in a brass ring.
    private static void HeartStandIn(CanvasItem canvas)
    {
        Disc(canvas, 1, 449, 155, 24, Black); Ring(canvas, 1, 449, 155, 25, Brass, 3);
        Disc(canvas, 1, 442, 150, 7, RedInk); Disc(canvas, 1, 456, 150, 7, RedInk); Poly(canvas, 1, RedInk, (435, 153), (463, 153), (449, 170));
    }

    // The fatigue disc (a cross).
    private static void CrossStandIn(CanvasItem canvas)
    {
        Disc(canvas, 1, 449, 225, 24, Black); Ring(canvas, 1, 449, 225, 25, Brass, 3);
        var blue = new Color(.1f, .1f, .8f);
        Field(canvas, 1, 443, 209, 13, 29, blue, edge: false); Field(canvas, 1, 435, 217, 29, 13, blue, edge: false);
    }

    // The magic-technology ruler: a parchment strip, a tick every 6.4 units and a long one at each division, the magic sign (blue) on top, the technology gear (orange) at the bottom, the two number pits.
    private static void RulerStandIn(CanvasItem canvas)
    {
        Field(canvas, 1, 750, 62, 40, 324, Parchment, edge: false);
        for (var i = -17; i <= 17; i++)
        {
            var y = 223 + i * 6.4f;
            Segment(canvas, 1, 756, y, i % 5 == 0 ? 768 : 762, y, DarkWood);
        }
        Ring(canvas, 1, 771, 95, 12, new(.1f, .15f, .6f), 3); Disc(canvas, 1, 771, 95, 4, new(.1f, .15f, .6f));
        Gear(canvas, 1, 771, 352, 12, new(.8f, .5f, .05f), 8);
        Field(canvas, 1, 756, 66, 30, 16); Field(canvas, 1, 756, 366, 30, 17);
    }

    /// <summary>The schematic window (art 365): the blueprint page (paper ruled every 32 from 227, 22), the two component slot boxes, the assemble channel and the two list buttons' legends (a gear with a mortarboard, a gear with a scroll).</summary>
    internal static void Schematic(CanvasItem canvas)
    {
        Drawn.Add(SteamwovenKitWindow.Schematic);
        Part(canvas, 1, "fixture.schem_page", 201, 13, () => Field(canvas, 1, 201, 13, 506, 367, Paper, edge: false));
        Part(canvas, 1, "grid.schematic", 201, 13, () =>
        {
            for (var x = 227f; x < 707; x += 32) canvas.DrawLine(new Vector2(x, 13), new Vector2(x, 380), Rule, 1);
            for (var y = 22f; y < 380; y += 32) canvas.DrawLine(new Vector2(201, y), new Vector2(707, y), Rule, 1);
        });
        Part(canvas, 1, "fixture.schem_slot", 577, 179, () => Field(canvas, 1, 577, 179, 103, 71, Paper));
        Part(canvas, 1, "fixture.schem_slot", 577, 275, () => Field(canvas, 1, 577, 275, 103, 71, Paper));
        Part(canvas, 1, "fixture.schem_channel", 39, 334, () => Field(canvas, 1, 39, 334, 126, 30));
        // The legends: a dark oval, a gear and its sign (the mortarboard of the learned list, the scroll of the found list).
        Part(canvas, 1, "fixture.gear_hat", 72, 144, () =>
        {
            Disc(canvas, 1, 117, 179, 35, DarkWood); Gear(canvas, 1, 117, 179, 30, Grey);
            Poly(canvas, 1, new(.25f, .25f, .28f), (92, 173), (118, 162), (146, 173), (120, 184)); Poly(canvas, 1, new(.2f, .2f, .23f), (104, 178), (104, 192), (120, 198), (136, 192), (136, 178), (120, 184));
        });
        Part(canvas, 1, "fixture.gear_scroll", 71, 231, () =>
        {
            Disc(canvas, 1, 119, 267, 35, DarkWood); Gear(canvas, 1, 119, 267, 30, Grey);
            Field(canvas, 1, 92, 261, 56, 15, new(.85f, .78f, .6f), edge: false); Disc(canvas, 1, 93, 268, 8, new(.8f, .72f, .55f)); Disc(canvas, 1, 147, 268, 8, new(.8f, .72f, .55f));
        });
    }

    /// <summary>The logbook (arts 264 and 260): nothing the kit's two pieces do not carry (the pages are the book's own picture); only the mark for the smoke.</summary>
    internal static void Logbook() => Drawn.Add(SteamwovenKitWindow.Logbook);

    /// <summary>ARC-UI-3260: the black inside of the kit's message window frame, in the 800 × 600 screen's units (the part of <see cref="AreaSaveListArtLayout.MessageBox"/> the frame leaves free).</summary>
    internal static (float X, float Y, float Width, float Height) MessageField { get; } = (205f, 500f, 390f, 90f);

    /// <summary>
    /// ARC-UI-3038: the save and load screen (art 745 and the strip 335, census M05, M06): the preview's black field (the window's (281, 55) 468 × 300, on the 800 × 600 screen at y 96), which the original's background leaves as a hole over the
    /// screen's black and the info texts, the portrait and the picture stand on, and the message box in the middle of the bottom strip (the screen's black below the strip art's 47 rows: the save's details and the overwrite and delete
    /// questions are written on it). The list's black is drawn by the screen itself (MW13), the lens and the cover are their own arts, the scroll bar is not read (F0). In the view's units (the screen's 800 × 600).
    /// </summary>
    internal static void SaveLoad(CanvasItem canvas)
    {
        DrawnScreens.Add(SteamwovenKitScreen.SaveLoad);
        var (x, y, width, height) = AreaSaveListArtLayout.Preview;
        Field(canvas, 1, x, y, width, height);
        // ARC-UI-3260: the kit's frame of the message window (the bottom strip's `hardware.menu_window`) stands round the box, so the black is the frame's inside (master 736, 212 to 2142, 538 on the 3.6 px unit, from the 441 of the strip's top), not the whole box.
        Field(canvas, 1, MessageField.X, MessageField.Y, MessageField.Width, MessageField.Height, edge: false);
    }
}

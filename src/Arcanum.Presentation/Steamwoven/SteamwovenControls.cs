namespace Arcanum.Presentation.Steamwoven;

/// <summary>The band of the play screen a control belongs to.</summary>
public enum SteamwovenHudBand { Top, Bottom }

/// <summary>The controls and readouts of the play screen's centre block (the original's ten art buttons, the two vials, the clock, the message window and the experience strip).</summary>
public enum SteamwovenControlId
{
    Character, Logbook, Map, Inventory, FatePoints, Sleep, Clock, ExperienceStrip,
    Combat, Skills, Spells, Schematics, HealthVial, FatigueVial, MessageWindow, HoverImage,
}

/// <summary>
/// One control in master pixels (3840 × 2160 reference, multiples of 6): <see cref="X"/> from the centre block's left edge, <see cref="Y"/> from the top of its band.
/// </summary>
public sealed record SteamwovenControlSpec(SteamwovenControlId Id, SteamwovenHudBand Band, int X, int Y, int Width, int Height);

/// <summary>One control placed in a layout: its master rectangle and its rectangle in device pixels of the window (edges rounded from their own exact position, halves up, SWI-LAY-7).</summary>
public sealed record SteamwovenControl(SteamwovenControlSpec Spec, PixelRect Device)
{
    public SteamwovenControlId Id => Spec.Id;
}

/// <summary>The controls of one layout, compared by value so that two layouts of one size are equal.</summary>
public sealed class SteamwovenControlList(IReadOnlyList<SteamwovenControl> items) : IReadOnlyList<SteamwovenControl>, IEquatable<SteamwovenControlList>
{
    public int Count => items.Count;
    public SteamwovenControl this[int index] => items[index];
    public IEnumerator<SteamwovenControl> GetEnumerator() => items.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    public bool Equals(SteamwovenControlList? other) => other is not null && items.SequenceEqual(other);
    public override bool Equals(object? obj) => Equals(obj as SteamwovenControlList);
    public override int GetHashCode() => items.Aggregate(17, (hash, item) => hash * 31 + item.GetHashCode());
}

/// <summary>
/// ARC-UI-2526 (SWI-LAY-11): where each control of the centre block sits in the two panels, in master pixels, for any pair of panel heights. The centre block stays 2880 px wide
/// (the original's 800 units at 3.6 px, the width of the window zone), so every horizontal offset and every width is the original's × 3.6 snapped to the 6 px grid and the HUD text
/// has the scale of the windows' text. Only the vertical placement fits the band: the hardware is redrawn for it (the owner's choice between this and a centre scale k = 0.8,
/// 2026-11-17, #2526). Godot-free; the original unit positions are duplicated from the viewer's bar layout and checked against it by a test.
/// </summary>
/// <remarks>
/// Rule per band (G = 6 px grid, "centred" = <c>floor((band − height) / 2)</c> snapped down to G, a shift of at most 3 px):
/// top band: a button is a square of <c>min(120, T − 24)</c> (margin 12 px above and below), fate points and sleep <c>min(84, T − 48)</c>, the clock window 462 wide and <c>min(108, T − 36)</c> high, all centred;
/// bottom band: the three right-hand buttons are squares of <c>min(138, floor_G((B − 96) / 3))</c> at tops 18, 18 + side + 30 and 18 + 2 · (side + 30) (the original's stagger of x kept), the combat button the same square centred,
/// the vials 102 wide and `min(318, floor_G(B − 36))` high and the message window 1380 wide and `min(294, floor_G(B − 36))` high, centred, the hover image `min(228, message height − 48)` square at (24, 24) in the message window; the experience strip hangs 12 px above the top band's lower edge, 1380 × 48.
/// </remarks>
public static class SteamwovenControls
{
    private const int TopMargin = 12, BottomMargin = 18, BottomGap = 30;

    // The original's positions in UI units (AreaGameBarLayout, AreaGameClock, AreaMessageLog, AreaExperienceStrip); the viewer's tests compare these.
    public static readonly IReadOnlyList<(SteamwovenControlId Id, int X)> OriginalX =
    [
        (SteamwovenControlId.Character, 4), (SteamwovenControlId.Logbook, 41), (SteamwovenControlId.Map, 78), (SteamwovenControlId.Inventory, 115),
        (SteamwovenControlId.FatePoints, 157), (SteamwovenControlId.Sleep, 605), (SteamwovenControlId.Clock, 648), (SteamwovenControlId.ExperienceStrip, 211),
        (SteamwovenControlId.Combat, 86), (SteamwovenControlId.Skills, 693), (SteamwovenControlId.Spells, 649), (SteamwovenControlId.Schematics, 693),
        (SteamwovenControlId.HealthVial, 14), (SteamwovenControlId.FatigueVial, 754), (SteamwovenControlId.MessageWindow, 211),
    ];

    /// <summary>The controls of the two panels for the given heights (master px), in the order of <see cref="SteamwovenControlId"/>.</summary>
    public static IReadOnlyList<SteamwovenControlSpec> For(int topPanel, int bottomPanel)
    {
        var button = Fit(Units(34), topPanel - 2 * TopMargin);
        var small = Fit(Units(23), topPanel - 4 * TopMargin);
        var clockH = Fit(Units(30), topPanel - 3 * TopMargin);
        var side = Fit(Units(39), (bottomPanel - 2 * BottomMargin - 2 * BottomGap) / 3);
        var vialW = Units(28);
        var vialH = Fit(Units(88), bottomPanel - 2 * BottomMargin);
        var messageW = Units(383);
        var messageH = Fit(Units(82), bottomPanel - 2 * BottomMargin);
        var hover = Fit(Units(64), messageH - 2 * Units(7));

        SteamwovenControlSpec Top(SteamwovenControlId id, int w, int h) => new(id, SteamwovenHudBand.Top, X(id), Centred(topPanel, h), w, h);
        SteamwovenControlSpec Bottom(SteamwovenControlId id, int w, int h, int? y = null) => new(id, SteamwovenHudBand.Bottom, X(id), y ?? Centred(bottomPanel, h), w, h);

        return
        [
            Top(SteamwovenControlId.Character, button, button),
            Top(SteamwovenControlId.Logbook, button, button),
            Top(SteamwovenControlId.Map, button, button),
            Top(SteamwovenControlId.Inventory, button, button),
            Top(SteamwovenControlId.FatePoints, small, small),
            Top(SteamwovenControlId.Sleep, small, small),
            Top(SteamwovenControlId.Clock, Units(128), clockH),
            new(SteamwovenControlId.ExperienceStrip, SteamwovenHudBand.Top, X(SteamwovenControlId.ExperienceStrip), Math.Max(0, topPanel - Units(4)), Units(384), Units(14)),
            Bottom(SteamwovenControlId.Combat, side, side),
            Bottom(SteamwovenControlId.Skills, side, side, BottomMargin),
            Bottom(SteamwovenControlId.Spells, side, side, BottomMargin + side + BottomGap),
            Bottom(SteamwovenControlId.Schematics, side, side, BottomMargin + 2 * (side + BottomGap)),
            Bottom(SteamwovenControlId.HealthVial, vialW, vialH),
            Bottom(SteamwovenControlId.FatigueVial, vialW, vialH),
            Bottom(SteamwovenControlId.MessageWindow, messageW, messageH),
            new(SteamwovenControlId.HoverImage, SteamwovenHudBand.Bottom,
                X(SteamwovenControlId.MessageWindow) + Units(6), Centred(bottomPanel, messageH) + Units(7), hover, hover),
        ];
    }

    private static int X(SteamwovenControlId id) => Units(OriginalX.First(entry => entry.Id == id).X);

    private static int Units(int units) => SteamwovenGrid.UnitsToMaster(units);

    /// <summary>The original's length, or what the band leaves when that is smaller, snapped down to the grid and at least one cell.</summary>
    private static int Fit(int original, int room) => Math.Max(SteamwovenGrid.GridPx, Down(Math.Min(original, room)));

    private static int Down(int value) => value / SteamwovenGrid.GridPx * SteamwovenGrid.GridPx;

    private static int Centred(int band, int height) => Down(Math.Max(0, band - height) / 2);
}

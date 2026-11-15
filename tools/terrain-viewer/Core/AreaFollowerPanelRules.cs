using Arcanum.Simulation.Party;

namespace Arcanum.TerrainViewer.Core;

/// <summary>The state a menu row is drawn in (ledger FP8): normal white, greyed mid grey, or the bright colour under the pointer.</summary>
public enum FollowerMenuRowState { Normal, Greyed }

/// <summary>
/// ARC-UI-1736 (ledger FP1–FP13, ARC-RES-1107; the follower module 0x56A4A0..0x56B970): the follower panel's show/hide button and scroll
/// arrows, the offset's clamp and the order menu's geometry and row states as pure rules. Godot-free; <c>AreaFollowerPortraits</c> draws them.
/// </summary>
public static class AreaFollowerPanelRules
{
    /// <summary>The show/hide button's art while the panel is shown and hidden (window 6, FP1, FP5).</summary>
    public const int ShownArt = 501, HiddenArt = 502;
    /// <summary>The scroll-up art (506), its top end look (507), the scroll-down art (508) and its bottom end look (509), FP5.</summary>
    public const int UpArt = 506, UpEndArt = 507, DownArt = 508, DownEndArt = 509;
    /// <summary>
    /// ARC-UI-2069 (ledger DB1, DB2): whether the panel stays drawn but takes no input: the dialog is the top interface mode and nothing else covers the screen (a menu, the
    /// save list, the palette, a death or movie cover); ARC-UI-2268: in both settings. Otherwise a modal window hides it.
    /// </summary>
    public static bool HeldUnderDialog(bool dialogOnTop, bool otherCover) => dialogOnTop && !otherCover;

    /// <summary>The order menu's background art (FP8).</summary>
    public const int MenuArt = 826;

    /// <summary>The window rectangles of the three buttons on the 800 × 600 screen (FP1): show/hide 67 × 13 at (0, 428), up 33 × 13 at
    /// (0, 415), down 34 × 13 at (33, 415).</summary>
    public static readonly (int X, int Y, int Width, int Height) ToggleRect = (0, 428, 67, 13), UpRect = (0, 415, 33, 13), DownRect = (33, 415, 34, 13);

    /// <summary>ARC-UI-1785 (FP1, SZ11: the table 0x5CA420 is chosen while the interface is hidden, 0x5578C0): the same three rectangles in the hidden-interface layout, the
    /// y of show/hide 587 and of the arrows 574; x and sizes stay, and the portraits do not move.</summary>
    public static readonly (int X, int Y, int Width, int Height) HiddenToggleRect = (0, 587, 67, 13), HiddenUpRect = (0, 574, 33, 13), HiddenDownRect = (33, 574, 34, 13);

    /// <summary>The show/hide button's rectangle for the layout: the bars-drawn one (<see cref="ToggleRect"/>) or the hidden-interface one.</summary>
    public static (int X, int Y, int Width, int Height) ToggleRectFor(bool barsDrawn) => barsDrawn ? ToggleRect : HiddenToggleRect;

    /// <summary>ARC-UI-2284: the window y (in the original's pixels) of a button whose rectangle stands at <paramref name="rectY"/> on the 800 × 600 screen, for a window
    /// <paramref name="screenHeightUnits"/> original pixels high. The three buttons hang off the screen's bottom edge (FP1: their table is the bars-drawn one 13 pixels above the bottom
    /// strip, or the hidden-interface one at the very bottom), and the bottom strip is anchored to the window's bottom, so the y moves with the window's height; at 600 it is the table's own y.</summary>
    public static float AnchoredY(int rectY, float screenHeightUnits) => screenHeightUnits - (AreaGameBarLayout.ScreenHeight - rectY);

    /// <summary>The scroll-up arrow's rectangle for the layout.</summary>
    public static (int X, int Y, int Width, int Height) UpRectFor(bool barsDrawn) => barsDrawn ? UpRect : HiddenUpRect;

    /// <summary>The scroll-down arrow's rectangle for the layout.</summary>
    public static (int X, int Y, int Width, int Height) DownRectFor(bool barsDrawn) => barsDrawn ? DownRect : HiddenDownRect;

    /// <summary>The arts the panel draws beside the frames (the viewer loads them with <see cref="AreaFollowerFrameLayout.ArtNumbers"/>).</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } = [ShownArt, HiddenArt, UpArt, UpEndArt, DownArt, DownEndArt, MenuArt];

    /// <summary>The scroll offset of <paramref name="count"/> followers, clamped (FP3): 0 for six or fewer, else at most count − 6.</summary>
    public static int ClampOffset(int offset, int count)
    {
        var last = Math.Max(0, count - AreaFollowerFrameLayout.Slots);
        return Math.Clamp(offset, 0, last);
    }

    /// <summary>Whether the show/hide button exists: at least one follower (0x56B850).</summary>
    public static bool ToggleShown(int count) => count >= 1;

    /// <summary>The show/hide button's art for the panel's state (FP5: 501 shown, 502 hidden).</summary>
    public static int ToggleArt(bool panelShown) => panelShown ? ShownArt : HiddenArt;

    /// <summary>Whether the two scroll arrows exist: only with the panel shown and at least seven followers (FP5).</summary>
    public static bool ArrowsShown(bool panelShown, int count) => panelShown && count > AreaFollowerFrameLayout.Slots;

    /// <summary>The up arrow's art: 507 at offset 0, else 506 (FP5).</summary>
    public static int UpArtAt(int offset) => offset <= 0 ? UpEndArt : UpArt;

    /// <summary>The down arrow's art: 509 at the last offset (count − 6), else 508 (FP5).</summary>
    public static int DownArtAt(int offset, int count) => offset >= count - AreaFollowerFrameLayout.Slots ? DownEndArt : DownArt;

    /// <summary>A press-and-release on up: the offset lowers by one when above 0 (FP5).</summary>
    public static int ScrollUp(int offset, int count) => ClampOffset(offset > 0 ? offset - 1 : offset, count);

    /// <summary>A press-and-release on down: the offset rises by one when below count − 6 (FP5).</summary>
    public static int ScrollDown(int offset, int count) => ClampOffset(offset < count - AreaFollowerFrameLayout.Slots ? offset + 1 : offset, count);

    /// <summary>The indexes of the followers shown in the slots: none while the panel is hidden, else <c>offset .. offset + 5</c> up to the count (FP3).</summary>
    public static IReadOnlyList<int> VisibleIndexes(bool panelShown, int offset, int count)
    {
        if (!panelShown || count <= 0) return [];
        var first = ClampOffset(offset, count);
        return Enumerable.Range(first, Math.Min(AreaFollowerFrameLayout.Slots, count - first)).ToArray();
    }

    /// <summary>The menu's top left on the 800 × 600 screen (FP7): at the slot's right edge (slot x + 40) and the slot's y, no clamp to the screen.</summary>
    public static (int X, int Y) MenuOrigin(int slot)
    {
        var (x, y) = AreaFollowerFrameLayout.Slot(slot);
        return (x + AreaFollowerFrameLayout.Width, y);
    }

    /// <summary>A menu row's rectangle in the menu's own pixels (FP8): 118 × 15 at (10, 3 + 18 × row).</summary>
    public static (int X, int Y, int Width, int Height) MenuRow(FollowerMenuItem item) => (10, 3 + 18 * (int)item, 118, 15);

    /// <summary>
    /// A row's state (FP8, FP11): Wait is greyed when the follower is mind controlled (spell flag 0x8000000), Inventory when the follower cannot be
    /// opened (the test 0x575080); every other row, Character Sheet included, is always normal.
    /// </summary>
    public static FollowerMenuRowState RowState(FollowerMenuItem item, bool mindControlled, bool canOpenInventory) => item switch
    {
        FollowerMenuItem.Wait when mindControlled => FollowerMenuRowState.Greyed,
        FollowerMenuItem.Inventory when !canOpenInventory => FollowerMenuRowState.Greyed,
        _ => FollowerMenuRowState.Normal,
    };

    /// <summary>The spell flag that marks a mind-controlled follower (FP8).</summary>
    public const uint MindControlledSpellFlag = 0x8000000;

    /// <summary>The spell flag of a summoned critter, which the inventory test refuses (FP11, 0x4000).</summary>
    public const uint SummonedSpellFlag = 0x4000;

    /// <summary>The critter flags the inventory test refuses (FP11: 0x20000008).</summary>
    public const uint InventoryRefusedCritterFlags = 0x20000008;
}

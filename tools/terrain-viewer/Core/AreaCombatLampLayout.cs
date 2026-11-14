using Arcanum.Presentation.Hud;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-1888 (ledger CH1, CH2, CH5, CT5; the combat interface build 0x56ED20 and the action-point display 0x56F430): the original's lamp
/// panel of the combat interface, on the 800 × 600 screen. The panel is art 294 (<c>ap_bar</c>, 356 × 30) at (222, 412); on it the twenty
/// lamps are art 295 (the points left), 296 (the action's cost) or 315 (the cost the points do not cover), each 11 × 12, at the x of the
/// table 0x5CAA48 and y 10 of the panel, in four groups of five (a 13 pixel step inside a group, 19 between groups); the end-turn button
/// is art 297 (31 × 15) at (320, 9) of the panel. Positions, sizes and hit areas are the original's; the lamps' states are
/// <see cref="ActionPointRules"/>'s. The hidden-interface bar (124 × 14 at 617, 577, CH3) is not built (the viewer has no hidden interface,
/// ledger SZ11). Godot-free.
/// </summary>
public static class AreaCombatLampLayout
{
    /// <summary>The panel art (<c>ap_bar</c>) and its place on the screen (0x56ED20: window 0x5CAA18 at 222, 412).</summary>
    public const int PanelArt = 294, PanelX = 222, PanelY = 412, PanelWidth = 356, PanelHeight = 30;

    /// <summary>The lamp arts by share (table 0x5CAA98: 295 the points left and the running lights, 296 the cost, 315 the shortfall) and a lamp's size.</summary>
    public const int LeftArt = 295, CostArt = 296, ShortfallArt = 315, LampWidth = 11, LampHeight = 12;

    /// <summary>The lamps' top in the panel (0x56F430: the destination y 10 of every lamp).</summary>
    public const int LampY = 10;

    /// <summary>The end-turn button's art and its place in the panel (0x5CAA38: x 0x140 = 320, y 9; art 0x129 = 297, 31 × 15, CT5).</summary>
    public const int EndTurnArt = 297, EndTurnX = 320, EndTurnY = 9, EndTurnWidth = 31, EndTurnHeight = 15;

    /// <summary>The lamps' left edges in the panel (table 0x5CAA48, 20 entries: 22, 35, 48, 61, 74, then 93 .. 145, 164 .. 216, 235 .. 287).</summary>
    public static IReadOnlyList<int> LampX { get; } = [22, 35, 48, 61, 74, 93, 106, 119, 132, 145, 164, 177, 190, 203, 216, 235, 248, 261, 274, 287];

    /// <summary>Every interface art number the panel draws.</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } = [PanelArt, LeftArt, CostArt, ShortfallArt, EndTurnArt];

    /// <summary>The art a lit lamp draws (null for an unlit lamp, which shows the panel's own socket).</summary>
    public static int? LampArt(ActionPointLamp lamp) => lamp switch
    {
        ActionPointLamp.Left => LeftArt,
        ActionPointLamp.Cost => CostArt,
        ActionPointLamp.Shortfall => ShortfallArt,
        _ => null,
    };
}

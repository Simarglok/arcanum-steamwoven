namespace Arcanum.Presentation.Hud;

/// <summary>
/// ARC-UI-1113: the original's skill pane, bar mode 2 of the bottom strip's message window (docs/research/ledger/spell-runs.md "Skill pane",
/// SKP1–SKP6): the pane's background art and the skill bar's four buttons (prowling, pick pocket, disarm traps, repair) in one row.
/// Coordinates are the original's 800 × 600 screen. Engine-free; the host draws it and carries the presses out (a button is activated on
/// the release over it, the latest recent action first; a right press starts a drag onto the hotkey bar).
/// </summary>
public static class SkillPaneRules
{
    public const int Buttons = 4;
    /// <summary>The bar mode the pane is (0x5506C0 with 2; its name area is the mode's text area (208, 567, 387 × 18), HK21).</summary>
    public const int BarMode = 2;
    /// <summary>The mode's background (art 274, table 0x5C6548, drawn by 0x5503F0 at the same place as the spell pane's, SPN1).</summary>
    public const int BackgroundArt = 274, BackgroundX = 196, BackgroundY = 492;
    /// <summary>The four buttons' top edge (records 0x5C6C68, one row).</summary>
    public const int ButtonY = 514;

    /// <summary>The buttons' left edges by button (prowling first): 288, 351, 414, 477, 63 pixels apart (records 0x5C6C68, stride 16).</summary>
    public static IReadOnlyList<int> ButtonX { get; } = [288, 351, 414, 477];

    /// <summary>A button's art (0x579F50, table 0x5CB220): the hotkey bar's skill icons, <see cref="HotkeyBarRules.SkillIcon"/>; null outside
    /// the four buttons (the original returns the first for an index outside, and the pane builds only 0..3).</summary>
    [OriginalFunction(0x579F50, OriginalFunctionScope.Partial)]
    public static int? ButtonArt(int button) => button is >= 0 and < Buttons ? HotkeyBarRules.SkillArts[button] : null;

    /// <summary>Every interface art number the pane draws beyond the hotkey bar's own: its background (the button arts are the bar's).</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } = [BackgroundArt];
}

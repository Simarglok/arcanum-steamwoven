using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-2891 (TRD "Column mode"): what lies around the plate of a full-screen or two-strip menu in column mode: black (<see cref="SteamwovenColumnMenu.BlackRects"/>), over the whole window outside the plate.
/// The menu's plate is a sibling drawn over it. The view takes the pointer over the whole window (the menu is modal), so a press on the black does nothing.
/// </summary>
internal sealed partial class SteamwovenColumnSidesView : Control
{
    private IReadOnlyList<PixelRect> _black = [];
    private SteamwovenFullScreenPlan? _shown;

    public override void _Ready()
    {
        Name = "ColumnSides";
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
    }

    /// <summary>Shows the black around the plate of <paramref name="plan"/> (the window's size is its layout's). Idempotent for the same plan.</summary>
    public void Present(SteamwovenFullScreenPlan plan)
    {
        if (!plan.IsColumn) { Clear(); return; }
        Position = Vector2.Zero;
        Size = new Vector2(plan.Layout.WindowWidth, plan.Layout.WindowHeight);
        Visible = true;
        if (_shown == plan) return;
        _shown = plan;
        _black = SteamwovenColumnMenu.BlackRects(plan);
        QueueRedraw();
    }

    /// <summary>Hides the surroundings (the menu is not in column mode).</summary>
    public void Clear()
    {
        if (_shown is null && !Visible) return;
        _shown = null; _black = [];
        Visible = false;
    }

    public override void _Draw()
    {
        foreach (var rect in _black) DrawRect(new Rect2(rect.X, rect.Y, rect.Width, rect.Height), Colors.Black);
    }

    /// <summary>For the self-test: the black rectangles now drawn.</summary>
    internal IReadOnlyList<PixelRect> BlackForSelfTest => Visible ? _black : [];
}

using Arcanum.Presentation.Hud;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>What the combat HUD draws for one frame (ARC-UI-1114): the 20 lamps, the round's number, and the turn-order rows (null:
/// the list is not shown, as in the Original rule mode).</summary>
public sealed record AreaCombatHudView(ActionPointLamp[] Lamps, bool HeroTurn, int Round, IReadOnlyList<(string Name, bool Current)>? Rows, int Hidden);

/// <summary>
/// ARC-UI-1114 (slice 2 of docs/research/combat-hud.md; ledger CH1–CH6, plain Godot controls, no original interface art): the
/// action-point lamps just above the interface bar at the original's place (the panel at 222, 412 of the 800 × 600 screen, scaled
/// with the bars) and the turn-order list under the top strip. A lit lamp is green for the points left after the previewed action,
/// orange for its cost and red for the part the points do not cover (the colours of the original's hidden-interface bar, CH3); the
/// running lights of another participant's subturn are green. The list marks the participant whose subturn it is with <c>&gt;</c>.
/// </summary>
public partial class AreaCombatHud : CanvasLayer
{
    // The panel's window in the original's screen, and where the first lamp and the groups sit in it.
    private static readonly Rect2 PanelWindow = new(222, 412, 20 * ActionPointRules.LampPitch + 3 * GroupGap + 8, 28);
    private const int GroupGap = 4, LampInset = 5, LampTop = 9, LampSize = 10;
    private static readonly Color LeftColor = new(0f, 1f, 0f), CostColor = new(1f, .5f, 0f), ShortfallColor = new(1f, 0f, 0f), OffColor = new(.08f, .09f, .1f);

    private Control _root = null!;
    private ColorRect _panel = null!;
    private readonly ColorRect[] _lamps = new ColorRect[ActionPointRules.LampCount];
    private PanelContainer _list = null!;
    private VBoxContainer _rows = null!;
    private AreaGameBarPanel? _bars;
    private bool _inBars;
    private AreaCombatHudView? _view;

    internal ActionPointLamp[]? LampsForSelfTest => _view?.Lamps;
    internal bool ListShownForSelfTest => _list.Visible;
    internal IReadOnlyList<string> RowTextsForSelfTest => _rows.GetChildren().OfType<Label>().Select(l => l.Text).ToArray();
    internal bool ShownForSelfTest => Visible;
    /// <summary>The panel's and the list's rectangles in window pixels.</summary>
    internal (Rect2 Panel, Rect2 List) PlacementForSelfTest => (_panel.GetGlobalRect(), _list.GetGlobalRect());
    internal Rect2 LampRectForSelfTest(int lamp) => _lamps[lamp].GetGlobalRect();
    internal Color LampColorForSelfTest(int lamp) => _lamps[lamp].Color;

    public override void _Ready()
    {
        Name = "CombatHud";
        _root = new Control { Name = "ActionPoints", MouseFilter = Control.MouseFilterEnum.Ignore, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        AddChild(_root);
        _panel = new ColorRect { Name = "LampPanel", Color = new Color(0f, 0f, 0f, .62f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(_panel);
        for (var i = 0; i < _lamps.Length; i++)
        {
            _lamps[i] = new ColorRect { Name = "Lamp" + i, Color = OffColor, MouseFilter = Control.MouseFilterEnum.Ignore };
            _panel.AddChild(_lamps[i]);
        }
        _list = new PanelContainer { Name = "TurnOrder", MouseFilter = Control.MouseFilterEnum.Ignore };
        _rows = new VBoxContainer { Name = "Rows", MouseFilter = Control.MouseFilterEnum.Ignore };
        _list.AddChild(_rows);
        AddChild(_list);
        GetViewport().SizeChanged += Place;
        AreaGameBarPanel.InsetsChanged += Place;
        Hide();
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= Place;
        AreaGameBarPanel.InsetsChanged -= Place;
    }

    /// <summary>Draws <paramref name="view"/> (null hides the HUD), over the bars of <paramref name="bars"/> when given and shown.</summary>
    public void Show(AreaCombatHudView? view, AreaGameBarPanel? bars)
    {
        _view = view;
        _bars = bars;
        if (view is null) { Hide(); return; }
        for (var i = 0; i < _lamps.Length; i++)
            _lamps[i].Color = view.Lamps[i] switch
            {
                ActionPointLamp.Left => LeftColor,
                ActionPointLamp.Cost => CostColor,
                ActionPointLamp.Shortfall => ShortfallColor,
                _ => OffColor,
            };
        foreach (var child in _rows.GetChildren()) { _rows.RemoveChild(child); child.QueueFree(); }
        _list.Visible = view.Rows is not null;
        if (view.Rows is { } rows)
        {
            AddRow($"Round {view.Round}", false);
            foreach (var (name, current) in rows) AddRow((current ? "> " : "  ") + name, current);
            if (view.Hidden > 0) AddRow($"  +{view.Hidden} more", false);
        }
        Show();
        Place();
    }

    private void AddRow(string text, bool current)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", 13);
        if (current) label.AddThemeColorOverride("font_color", new Color(1f, .85f, .3f));
        _rows.AddChild(label);
    }

    private void Place()
    {
        if (_root is null) return;
        var size = GetViewport().GetVisibleRect().Size;
        _inBars = _bars is { Visible: true } && AreaGameBarPanel.BottomInset > 0;
        // As the PC's gauges: over the bars (layer 6) when they are shown, else under the hover tooltip and the windows.
        Layer = _inBars ? 6 : 2;
        var factor = _inBars ? _bars!.Factor : AreaGameBarPanel.ScreenFactor(size);
        _root.Position = new(AreaGameBarPanel.ScreenLeft(size, factor), size.Y - AreaGameBarLayout.ScreenHeight * factor);
        _root.Scale = new(factor, factor);
        _panel.Position = PanelWindow.Position; _panel.Size = PanelWindow.Size;
        for (var i = 0; i < _lamps.Length; i++)
        {
            _lamps[i].Position = new(LampInset + ActionPointRules.LampX(i, GroupGap), LampTop);
            _lamps[i].Size = new(LampSize, LampSize);
        }
        // The list under the top strip at the left, unscaled (its text is a normal label).
        _list.Position = new(8, AreaGameBarPanel.TopInset + 8);
        _list.ResetSize();
    }
}

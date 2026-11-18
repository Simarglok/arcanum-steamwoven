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
/// with the bars) and the turn-order list under the top strip at the right. A lit lamp is green for the points left after the previewed action,
/// orange for its cost and red for the part the points do not cover (the colours of the original's hidden-interface bar, CH3); the
/// running lights of another participant's subturn are green. The list marks the participant whose subturn it is with <c>&gt;</c>.
/// ARC-UI-1888: with the game's art (<see cref="SetArt"/>) the panel is the original's (<see cref="AreaCombatLampLayout"/>: the panel art 294, the lamps as
/// the arts 295, 296 and 315 at the original's table and the end-turn button 297, CH1, CH2, CT5), at the same place; without it the plain controls stay.
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
    // ARC-UI-1888: the original's art version of the panel: the panel picture, one picture a lamp (shown only when lit) and the end-turn button.
    private TextureRect _panelArt = null!;
    private readonly TextureRect[] _lampArt = new TextureRect[ActionPointRules.LampCount];
    private TextureRect _endTurn = null!;
    private Func<int, Texture2D[]?>? _art;
    private bool _artShown, _endTurnHeld;
    private PanelContainer _list = null!;
    private VBoxContainer _rows = null!;
    private AreaGameBarPanel? _bars;
    private bool _inBars;
    private AreaCombatHudView? _view;
    private string? _rowsKey;

    /// <summary>The end-turn button went down (for its sound).</summary>
    public event Action? EndTurnPressed;
    /// <summary>The end-turn button came up, and whether the pointer was still over it: a release over it ends the hero's subturn as E does (CT5).</summary>
    public event Action<bool>? EndTurnReleased;

    internal ActionPointLamp[]? LampsForSelfTest => _view?.Lamps;
    /// <summary>Whether the last <see cref="Show"/> drew the original's art panel.</summary>
    internal bool ArtShownForSelfTest => _artShown && _panelArt.Visible;
    /// <summary>The art number a lamp's picture shows (-1: the lamp is not drawn lit), for the art panel.</summary>
    internal int LampArtNumberForSelfTest(int lamp) => _lampArt[lamp].Visible && _lampArt[lamp].HasMeta("art") ? (int)_lampArt[lamp].GetMeta("art") : -1;
    internal Rect2 LampArtRectForSelfTest(int lamp) => _lampArt[lamp].GetGlobalRect();
    /// <summary>The first lamp's rectangle in window pixels, whichever look draws.</summary>
    internal Rect2 FirstLampRectForSelfTest => _artShown ? _lampArt[0].GetGlobalRect() : _lamps[0].GetGlobalRect();
    internal Rect2 PanelArtRectForSelfTest => _panelArt.GetGlobalRect();
    internal Rect2 EndTurnRectForSelfTest => _endTurn.GetGlobalRect();
    internal bool EndTurnShownForSelfTest => _artShown && _endTurn.Visible;
    /// <summary>A left press and release at the middle of the button (its own input path).</summary>
    internal void EndTurnClickForSelfTest()
    {
        var local = _endTurn.Size / 2;
        EndTurnInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = local });
        EndTurnInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = local });
    }
    internal int EndTurnFrameForSelfTest => _endTurn.HasMeta("frame") ? (int)_endTurn.GetMeta("frame") : -1;
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
        _panelArt = ArtRect("PanelArt", Control.MouseFilterEnum.Ignore);
        _panel.AddChild(_panelArt);
        for (var i = 0; i < _lamps.Length; i++)
        {
            _lamps[i] = new ColorRect { Name = "Lamp" + i, Color = OffColor, MouseFilter = Control.MouseFilterEnum.Ignore };
            _panel.AddChild(_lamps[i]);
            _lampArt[i] = ArtRect("LampArt" + i, Control.MouseFilterEnum.Ignore);
            _panel.AddChild(_lampArt[i]);
        }
        _endTurn = ArtRect("EndTurn", Control.MouseFilterEnum.Stop);
        _endTurn.GuiInput += EndTurnInput;
        _panel.AddChild(_endTurn);
        _list = new PanelContainer { Name = "TurnOrder", MouseFilter = Control.MouseFilterEnum.Ignore };
        _list.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, .68f), ContentMarginLeft = 6, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4 });
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

    private static TextureRect ArtRect(string name, Control.MouseFilterEnum filter) => new()
    {
        Name = name, MouseFilter = filter, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, Visible = false,
    };

    /// <summary>Gives the HUD the game's art (the frames of an interface art number, null when it did not read); without the five arts of
    /// <see cref="AreaCombatLampLayout.ArtNumbers"/> the plain controls draw. Call before <see cref="Show"/>.</summary>
    public void SetArt(Func<int, Texture2D[]?>? art) => _art = art;

    /// <summary>Whether the end-turn button takes the pointer (off while a window over the world is open: the HUD's layer is above the windows').</summary>
    public void SetEndTurnActive(bool active)
    {
        _endTurn.MouseFilter = active ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
        if (!active) _endTurnHeld = false;
    }

    private Texture2D? ArtFrame(int number, int frame = 0) => _art?.Invoke(number) is { Length: > 0 } frames ? frames[Math.Min(frame, frames.Length - 1)] : null;

    private bool ArtReady => AreaCombatLampLayout.ArtNumbers.All(number => ArtFrame(number) is not null);

    private void EndTurnInput(InputEvent input)
    {
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } click) return;
        _endTurn.AcceptEvent();
        if (click.Pressed) { _endTurnHeld = true; ShowEndTurnFrame(AreaBarButtonLook.Down); EndTurnPressed?.Invoke(); return; }
        if (!_endTurnHeld) return;
        _endTurnHeld = false;
        ShowEndTurnFrame(AreaBarButtonLook.Idle);
        EndTurnReleased?.Invoke(new Rect2(Vector2.Zero, _endTurn.Size).HasPoint(click.Position));
    }

    private void ShowEndTurnFrame(AreaBarButtonLook look)
    {
        var frames = _art?.Invoke(AreaCombatLampLayout.EndTurnArt);
        if (frames is not { Length: > 0 }) return;
        var frame = AreaGameBarLayout.Frame(look, frames.Length);
        _endTurn.Texture = frames[frame];
        _endTurn.SetMeta("frame", frame);
    }

    /// <summary>Draws <paramref name="view"/> (null hides the HUD), over the bars of <paramref name="bars"/> when given and shown.</summary>
    public void Show(AreaCombatHudView? view, AreaGameBarPanel? bars)
    {
        _view = view;
        _bars = bars;
        if (view is null) { _endTurnHeld = false; Hide(); return; }
        _artShown = ArtReady;
        _panel.Color = _artShown ? Colors.Transparent : new Color(0f, 0f, 0f, .62f);
        _panelArt.Visible = _artShown;
        if (_artShown)
        {
            _panelArt.Texture = ArtFrame(AreaCombatLampLayout.PanelArt);
            if (!_endTurnHeld) ShowEndTurnFrame(AreaBarButtonLook.Idle);
        }
        else _endTurnHeld = false;
        _endTurn.Visible = _artShown;
        for (var i = 0; i < _lamps.Length; i++)
        {
            var art = AreaCombatLampLayout.LampArt(view.Lamps[i]);
            _lampArt[i].Visible = _artShown && art is not null;
            if (_artShown && art is { } number) { _lampArt[i].Texture = ArtFrame(number); _lampArt[i].SetMeta("art", number); }
            _lamps[i].Visible = !_artShown;
        }
        for (var i = 0; i < _lamps.Length; i++)
            _lamps[i].Color = view.Lamps[i] switch
            {
                ActionPointLamp.Left => LeftColor,
                ActionPointLamp.Cost => CostColor,
                ActionPointLamp.Shortfall => ShortfallColor,
                _ => OffColor,
            };
        _list.Visible = view.Rows is not null;
        // The rows are rebuilt only when their text changes (the running lights redraw the lamps every 100 ms).
        var key = view.Rows is { } texts ? $"{view.Round}|{view.Hidden}|" + string.Join("\n", texts.Select(r => (r.Current ? ">" : " ") + r.Name)) : null;
        if (key != _rowsKey)
        {
            _rowsKey = key;
            foreach (var child in _rows.GetChildren()) { _rows.RemoveChild(child); child.QueueFree(); }
            if (view.Rows is { } rows)
            {
                AddRow($"Round {view.Round}", false);
                foreach (var (name, current) in rows) AddRow((current ? "> " : "  ") + name, current);
                if (view.Hidden > 0) AddRow($"  +{view.Hidden} more", false);
            }
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
        // ARC-UI-2452: in the Steamwoven layout the screen lies where the strips put it, at the strips' fractional scale.
        var (origin, scale) = _inBars ? _bars!.ScreenFrame(size) : (new Vector2(AreaGameBarPanel.ScreenLeft(size, factor), size.Y - AreaGameBarLayout.ScreenHeight * factor), (float)factor);
        _root.Position = origin;
        _root.Scale = new(scale, scale);
        if (_artShown)
        {
            // ARC-UI-1888: the original's places (table 0x5CAA48 and the window table 0x5CAA38).
            _panel.Position = new(AreaCombatLampLayout.PanelX, AreaCombatLampLayout.PanelY); _panel.Size = new(AreaCombatLampLayout.PanelWidth, AreaCombatLampLayout.PanelHeight);
            _panelArt.Position = Vector2.Zero; _panelArt.Size = _panel.Size;
            for (var i = 0; i < _lampArt.Length; i++)
            {
                _lampArt[i].Position = new(AreaCombatLampLayout.LampX[i], AreaCombatLampLayout.LampY);
                _lampArt[i].Size = new(AreaCombatLampLayout.LampWidth, AreaCombatLampLayout.LampHeight);
            }
            _endTurn.Position = new(AreaCombatLampLayout.EndTurnX, AreaCombatLampLayout.EndTurnY);
            _endTurn.Size = new(AreaCombatLampLayout.EndTurnWidth, AreaCombatLampLayout.EndTurnHeight);
        }
        else
        {
            _panel.Position = PanelWindow.Position; _panel.Size = PanelWindow.Size;
            for (var i = 0; i < _lamps.Length; i++)
            {
                _lamps[i].Position = new(LampInset + ActionPointRules.LampX(i, GroupGap), LampTop);
                _lamps[i].Size = new(LampSize, LampSize);
            }
        }
        // The list under the top strip at the right (the follower portraits stand at the left edge), unscaled (its text is a normal label).
        // ARC-UI-2604: at the frame's right edge, not under a pillarbox bar; without the layout FreeRect is the whole window, as before.
        _list.ResetSize();
        var free = AreaGameBarPanel.FreeRect(size);
        _list.Position = new(Math.Max(free.Position.X, free.End.X - _list.GetCombinedMinimumSize().X - 8), AreaGameBarPanel.TopInset + 8);
    }
}

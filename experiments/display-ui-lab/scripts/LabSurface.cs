using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Arcanum.Experiments.DisplayUiLab;

public partial class LabSurface : Node2D
{
    private Control _hud = null!;
    private PanelContainer _topHud = null!;
    private ScrollContainer _actionScroll = null!;
    private HBoxContainer _actions = null!;
    private Panel _activePanel = null!;
    private ScrollContainer _panelScroll = null!;
    private VBoxContainer _panelBody = null!;
    private PanelContainer _diagnostics = null!;
    private Label _diagnosticLabel = null!;
    private Camera2D _camera = null!;
    private ProceduralWorld _proceduralWorld = null!;
    private readonly Dictionary<string, Control> _importantControls = new();
    private DisplayState _state = new(UiScalePreset.Scale100, 1.0f, LabPanel.Dialogue, false);
    private ResponsiveClass _responsiveClass;
    private bool _panning;

    public DisplayState State => _state;

    public override void _Ready()
    {
        _hud = GetNode<Control>("UiLayer/Hud");
        _camera = GetNode<Camera2D>("WorldRoot/Camera2D");
        _proceduralWorld = GetNode<ProceduralWorld>("WorldRoot/ProceduralWorld");
        _camera.Zoom = Vector2.One;
        _camera.MakeCurrent();
        BuildUi();
        GetViewport().SizeChanged += Arrange;
        Arrange();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
            return;
        if (key.Keycode == Key.F11)
        {
            GetNode<DisplayUiLabApp>("/root/DisplayUiLabApp").ToggleFullscreen();
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode == Key.F1 && GetWindow().HasFocus())
        {
            _state = _state with { DiagnosticsVisible = !_state.DiagnosticsVisible };
            Arrange();
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode == Key.Escape)
        {
            var app = GetNode<DisplayUiLabApp>("/root/DisplayUiLabApp");
            if (app.IsFullscreen)
                app.LeaveFullscreen();
            else
                ClosePanel();
            GetViewport().SetInputAsHandled();
        }
    }

    public void ApplyUiScale(UiScalePreset preset)
    {
        _state = _state with { UiScale = preset };
        BuildUi();
        Arrange();
    }

    public void SetCameraZoom(float zoom)
    {
        var clamped = Mathf.Clamp(zoom, 0.50f, 2.00f);
        _state = _state with { CameraZoom = clamped };
        _camera.Zoom = new Vector2(clamped, clamped);
        RefreshDiagnostics();
    }

    public void ShowPanel(LabPanel panel)
    {
        _state = _state with { ActivePanel = panel, PanelVisible = true };
        BuildPanelBody();
        Arrange();
    }

    public void ResetLab()
    {
        _state = new DisplayState(UiScalePreset.Scale100, 1.0f, LabPanel.Dialogue, false, true);
        _camera.Zoom = Vector2.One;
        _camera.Position = new Vector2(640, 400);
        BuildUi();
        Arrange();
    }

    public LayoutSnapshot CaptureLayoutSnapshot()
    {
        var rects = _importantControls.ToDictionary(pair => pair.Key, pair => pair.Value.GetGlobalRect());
        rects["hud"] = _topHud.GetGlobalRect();
        rects["actions"] = _actionScroll.GetGlobalRect();
        rects["panel"] = _activePanel.GetGlobalRect();
        var worldMarker = GetViewport().GetCanvasTransform() * _proceduralWorld.GetGlobalTransform() * ProceduralWorld.MarkerWorldPosition;
        return new LayoutSnapshot(_responsiveClass, GetViewport().GetVisibleRect().Size, _state, rects, worldMarker);
    }

    internal Control? ImportantControl(string id) => _importantControls.GetValueOrDefault(id);

    internal void SetDiagnosticsVisible(bool visible)
    {
        _state = _state with { DiagnosticsVisible = visible };
        Arrange();
    }

    internal bool IsInsideIntentionalScroll(Control control)
    {
        for (Node? node = control; node != null; node = node.GetParent())
        {
            if (node == _actionScroll || node == _panelScroll)
                return true;
        }
        return false;
    }

    internal (bool wrapped, int lineCount, int visibleLineCount) TextMetrics(string id)
    {
        var label = _panelBody.GetNodeOrNull<Label>(id);
        return label == null
            ? (false, 0, 0)
            : (label.AutowrapMode != TextServer.AutowrapMode.Off, label.GetLineCount(), label.GetVisibleLineCount());
    }

    internal bool FallbackFontHasCyrillic()
    {
        var font = ThemeDB.FallbackFont;
        return font != null && font.HasChar('С') && font.HasChar('я');
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button && button.ButtonIndex == MouseButton.Middle)
        {
            _panning = button.Pressed;
            GetViewport().SetInputAsHandled();
        }
        else if (_panning && @event is InputEventMouseMotion motion)
        {
            _camera.Position -= motion.Relative / _camera.Zoom;
            GetViewport().SetInputAsHandled();
        }
    }

    internal void SimulateViewport(Vector2 size)
    {
        // SubViewport matrix callers set this node's viewport size before Ready. This helper
        // additionally supports editor/native smoke probing without duplicating formulas.
        _hud.Size = size;
        Arrange();
    }

    private void BuildUi()
    {
        foreach (var child in _hud.GetChildren())
            (child as Node)?.Free();
        _importantControls.Clear();

        _topHud = new PanelContainer { Name = "TopHud", MouseFilter = Control.MouseFilterEnum.Stop };
        _topHud.AddThemeStyleboxOverride("panel", UiScalePolicy.PanelStyle(new Color("122235dd"), _state.UiScale));
        _hud.AddChild(_topHud);
        var hudRow = new HBoxContainer { Name = "HudRow" };
        UiScalePolicy.ApplyContainer(hudRow, _state.UiScale);
        _topHud.AddChild(hudRow);
        var title = LabelFor("Display UI laboratory — synthetic data only", 18);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hudRow.AddChild(title);
        foreach (var panel in Enum.GetValues<LabPanel>())
        {
            var button = ButtonFor(panel.ToString(), $"Panel{panel}");
            button.Pressed += () => ShowPanel(panel);
            hudRow.AddChild(button);
        }
        var fullscreen = ButtonFor("Fullscreen", "Fullscreen");
        fullscreen.Pressed += () => GetNode<DisplayUiLabApp>("/root/DisplayUiLabApp").ToggleFullscreen();
        hudRow.AddChild(fullscreen);

        _actionScroll = new ScrollContainer { Name = "ActionScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Auto, VerticalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _hud.AddChild(_actionScroll);
        _actions = new HBoxContainer { Name = "ActionRow" };
        UiScalePolicy.ApplyContainer(_actions, _state.UiScale);
        _actionScroll.AddChild(_actions);
        var panelSelector = new OptionButton { Name = "PanelSelector", FocusMode = Control.FocusModeEnum.All, TooltipText = "Select active panel" };
        panelSelector.CustomMinimumSize = new Vector2(UiScalePolicy.Target(120, _state.UiScale), UiScalePolicy.Target(44, _state.UiScale));
        UiScalePolicy.Apply(panelSelector, _state.UiScale);
        foreach (var panel in Enum.GetValues<LabPanel>())
            panelSelector.AddItem(panel.ToString(), (int)panel);
        panelSelector.Selected = (int)_state.ActivePanel;
        panelSelector.ItemSelected += index => ShowPanel((LabPanel)panelSelector.GetItemId((int)index));
        _importantControls["PanelSelector"] = panelSelector;
        _actions.AddChild(panelSelector);
        AddAction("100%", UiScalePreset.Scale100);
        AddAction("125%", UiScalePreset.Scale125);
        AddAction("150%", UiScalePreset.Scale150);
        AddAction("175%", UiScalePreset.Scale175);
        AddAction("200%", UiScalePreset.Scale200);
        AddZoomAction("Zoom −", -0.25f);
        AddZoomAction("Zoom +", 0.25f);

        _activePanel = new Panel { Name = "ActivePanel", MouseFilter = Control.MouseFilterEnum.Stop, ClipContents = true };
        _activePanel.Visible = _state.PanelVisible;
        _activePanel.AddThemeStyleboxOverride("panel", UiScalePolicy.PanelStyle(new Color("172c42f5"), _state.UiScale));
        _hud.AddChild(_activePanel);
        var panelShell = new VBoxContainer { Name = "PanelShell" };
        UiScalePolicy.ApplyContainer(panelShell, _state.UiScale);
        panelShell.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _activePanel.AddChild(panelShell);
        var sticky = new HBoxContainer { Name = "StickyHeader" };
        UiScalePolicy.ApplyContainer(sticky, _state.UiScale);
        panelShell.AddChild(sticky);
        var panelTitle = LabelFor(_state.ActivePanel.ToString(), 20);
        panelTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        sticky.AddChild(panelTitle);
        var close = ButtonFor("Close", "Close");
        close.Pressed += ClosePanel;
        sticky.AddChild(close);
        var reset = ButtonFor("Reset", "Reset");
        reset.Pressed += () => GetNode<DisplayUiLabApp>("/root/DisplayUiLabApp").ResetWindow();
        sticky.AddChild(reset);
        _panelScroll = new ScrollContainer { Name = "PanelBodyScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Auto };
        _panelScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        panelShell.AddChild(_panelScroll);
        _panelBody = new VBoxContainer { Name = "PanelBody" };
        UiScalePolicy.ApplyContainer(_panelBody, _state.UiScale);
        _panelBody.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _panelScroll.AddChild(_panelBody);
        BuildPanelBody();

        _diagnostics = new PanelContainer { Name = "Diagnostics", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = _state.DiagnosticsVisible };
        _diagnostics.AddThemeStyleboxOverride("panel", UiScalePolicy.PanelStyle(new Color("0a111de8"), _state.UiScale));
        _hud.AddChild(_diagnostics);
        _diagnosticLabel = LabelFor(string.Empty, 12);
        _diagnosticLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _diagnostics.AddChild(_diagnosticLabel);
    }

    private void BuildPanelBody()
    {
        if (_panelBody == null)
            return;
        foreach (var child in _panelBody.GetChildren())
            (child as Node)?.Free();
        foreach (var key in _importantControls.Where(pair => !GodotObject.IsInstanceValid(pair.Value)).Select(pair => pair.Key).ToArray())
            _importantControls.Remove(key);
        switch (_state.ActivePanel)
        {
            case LabPanel.Dialogue:
                AddLongText("Dialogue", "Synthetic envoy: The market road remains open, but the bridge is crowded. This intentionally long English/Russian sample exercises wrapping without original game text.\n\nСинтетический посланник: Дорога к рынку открыта, однако мост переполнен. Этот текст создан для проверки переноса строк и доступной прокрутки.");
                AddResponse("ResponseOne", "Ask about the bridge / Спросить о мосте");
                AddResponse("ResponseTwo", "Mark the route and continue / Отметить маршрут");
                AddResponse("ResponseThree", "Leave this synthetic conversation / Завершить разговор");
                break;
            case LabPanel.Inventory:
                AddLongText("Inventory", "Synthetic inventory with deliberately long labels. No original item names or assets are used.");
                var grid = new GridContainer { Columns = 3, Name = "InventoryGrid" };
                UiScalePolicy.ApplyContainer(grid, _state.UiScale);
                for (var i = 1; i <= 12; i++)
                    grid.AddChild(ButtonFor($"Supply crate {i:00}", $"Item{i:00}"));
                _panelBody.AddChild(grid);
                AddLongText("Item detail", "Weathered brass instrument with a deliberately verbose synthetic description; it proves the panel body can scroll instead of hiding text.");
                break;
            case LabPanel.Settings:
                AddLongText("Settings", "UI scale uses deterministic theme metrics. Camera zoom changes only the procedural world. Window content scaling is disabled.");
                foreach (var scale in Enum.GetValues<UiScalePreset>())
                    AddResponse($"Scale{(int)scale}", $"UI scale {(int)scale}%", () => ApplyUiScale(scale));
                foreach (var zoom in new[] { 0.75f, 1.00f, 1.25f, 1.50f })
                    AddResponse($"Zoom{zoom:0.00}", $"Camera zoom {zoom:0.00}", () => SetCameraZoom(zoom));
                var diagnostic = ButtonFor(_state.DiagnosticsVisible ? "Hide diagnostics" : "Show diagnostics", "Diagnostics");
                diagnostic.Pressed += () => { _state = _state with { DiagnosticsVisible = !_state.DiagnosticsVisible }; Arrange(); };
                _panelBody.AddChild(diagnostic);
                break;
        }
    }

    private void Arrange()
    {
        if (!IsInstanceValid(_hud) || !IsInsideTree())
            return;
        var viewport = _hud.Size;
        if (viewport == Vector2.Zero)
            viewport = GetViewport().GetVisibleRect().Size;
        var scale = UiScalePolicy.Factor(_state.UiScale);
        _responsiveClass = UiScalePolicy.Classify(viewport, _state.UiScale);
        foreach (var panel in Enum.GetValues<LabPanel>())
            _importantControls[$"Panel{panel}"].Visible = _responsiveClass != ResponsiveClass.Compact;
        _importantControls["PanelSelector"].Visible = true;
        var margin = UiScalePolicy.Target(12, _state.UiScale);
        var topHeight = UiScalePolicy.Target(52, _state.UiScale);
        // The row contains a 44px base target plus deterministic container/theme space.
        // Reserving that full metric prevents a first-layout minimum-size correction.
        var actionHeight = UiScalePolicy.Target(52, _state.UiScale);
        ResponsiveLayout.SetRect(_topHud, new Rect2(margin, margin, Mathf.Max(0, viewport.X - margin * 2), topHeight));
        ResponsiveLayout.SetRect(_actionScroll, new Rect2(margin, viewport.Y - actionHeight - margin, Mathf.Max(0, viewport.X - margin * 2), actionHeight));
        var available = new Rect2(margin, topHeight + margin * 2, Mathf.Max(0, viewport.X - margin * 2), Mathf.Max(0, viewport.Y - topHeight - actionHeight - margin * 3));
        var panelRect = _responsiveClass switch
        {
            ResponsiveClass.Wide => new Rect2(Mathf.Max(available.Position.X, available.End.X - Mathf.Min(UiScalePolicy.Target(460, _state.UiScale), available.Size.X * 0.46f)), available.Position.Y, Mathf.Min(UiScalePolicy.Target(460, _state.UiScale), available.Size.X * 0.46f), available.Size.Y),
            ResponsiveClass.Medium => new Rect2(available.Position.X, available.End.Y - Mathf.Min(UiScalePolicy.Target(360, _state.UiScale), available.Size.Y), available.Size.X, Mathf.Min(UiScalePolicy.Target(360, _state.UiScale), available.Size.Y)),
            _ => available,
        };
        _activePanel.Visible = _state.PanelVisible;
        if (_activePanel.Visible)
            ResponsiveLayout.SetRect(_activePanel, ResponsiveLayout.ClampToViewport(panelRect, viewport));
        _diagnostics.Visible = _state.DiagnosticsVisible;
        if (_diagnostics.Visible)
        {
            var diagnosticSize = new Vector2(Mathf.Min(UiScalePolicy.Target(520, _state.UiScale), viewport.X - margin * 2), Mathf.Min(UiScalePolicy.Target(220, _state.UiScale), available.Size.Y));
            ResponsiveLayout.SetRect(_diagnostics, ResponsiveLayout.ClampToViewport(new Rect2(margin, topHeight + margin * 2, diagnosticSize.X, diagnosticSize.Y), viewport));
        }
        RefreshDiagnostics();
    }

    private void RefreshDiagnostics()
    {
        if (_diagnosticLabel != null && IsInstanceValid(_diagnosticLabel))
            _diagnosticLabel.Text = DiagnosticOverlay.Describe(GetViewport(), _state);
    }

    private Label LabelFor(string text, int baseFontSize)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.Off, ClipText = false };
        UiScalePolicy.Apply(label, _state.UiScale, baseFontSize);
        return label;
    }

    private Button ButtonFor(string text, string id)
    {
        var button = new Button { Text = text, Name = id, FocusMode = Control.FocusModeEnum.All, TooltipText = text };
        button.CustomMinimumSize = new Vector2(UiScalePolicy.Target(44, _state.UiScale), UiScalePolicy.Target(44, _state.UiScale));
        UiScalePolicy.Apply(button, _state.UiScale);
        _importantControls[id] = button;
        return button;
    }

    private void AddAction(string label, UiScalePreset preset)
    {
        var action = ButtonFor(label, $"Action{label}");
        action.Pressed += () => ApplyUiScale(preset);
        _actions.AddChild(action);
    }

    private void AddZoomAction(string label, float delta)
    {
        var action = ButtonFor(label, label.Replace(" ", string.Empty));
        action.Pressed += () => SetCameraZoom(_state.CameraZoom + delta);
        _actions.AddChild(action);
    }

    private void AddLongText(string id, string text)
    {
        var label = LabelFor(text, 16);
        label.Name = id;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _panelBody.AddChild(label);
    }

    private void AddResponse(string id, string text, Action? action = null)
    {
        var button = ButtonFor(text, id);
        if (action != null)
            button.Pressed += action;
        _panelBody.AddChild(button);
    }

    private void ClosePanel()
    {
        _state = _state with { PanelVisible = false };
        Arrange();
    }
}

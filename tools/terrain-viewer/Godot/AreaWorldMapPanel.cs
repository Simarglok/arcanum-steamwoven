using Arcanum.Simulation.Travel;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-048 diagnostic world-map panel (<see cref="AreaWorldMap"/>): the project's picture of the start map drawn from its
/// terrain (one pixel per 4 × 4 sectors, blocked sectors darker), the PC, the known areas, the waypoints and the planned route;
/// a click on the picture adds a waypoint there, a pick in the area list at that area; Travel, Clear and Close. The original's
/// world-map art, zoom, notes and town maps are not drawn. The app re-renders it after every change and travel step.
/// </summary>
public partial class AreaWorldMapPanel : CanvasLayer
{
    private PanelContainer _panel = null!;
    private TextureRect _picture = null!;
    private AreaWorldMapOverlay _overlay = null!;
    private ItemList _areas = null!;
    private Label _status = null!;
    private Label _message = null!;
    private Button _travel = null!, _clear = null!, _close = null!;
    private AreaWorldMap? _map;
    private Func<int, bool>? _known;
    internal AreaWorldMap? Map => _map;
    internal Func<int, bool>? Known => _known;

    public event Action<WorldMapPixel>? WaypointRequested;
    public event Action<int>? AreaRequested;
    public event Action? TravelRequested;
    public event Action? ClearRequested;
    public event Action? CloseRequested;
    public bool IsOpen => Visible;
    internal string StatusForSelfTest => _status.Text;
    internal string MessageForSelfTest => _message.Text;

    public override void _Ready()
    {
        Layer = 4;
        _panel = new PanelContainer { Name = "AreaWorldMap", MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(214 / 255f, 205 / 255f, 186 / 255f, .97f),
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 10, ContentMarginBottom = 10,
        });
        AddChild(_panel);
        var contents = new VBoxContainer(); _panel.AddChild(contents);
        var header = new Label { Text = "World map", Modulate = new Color(.15f, .1f, .05f) };
        var body = new HBoxContainer();
        _picture = new TextureRect
        {
            StretchMode = TextureRect.StretchModeEnum.Scale, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            CustomMinimumSize = new Vector2(500, 500), MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _picture.GuiInput += PictureInput;
        _overlay = new AreaWorldMapOverlay { Panel = this, MouseFilter = Control.MouseFilterEnum.Ignore };
        _overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _picture.AddChild(_overlay);
        _areas = new ItemList { CustomMinimumSize = new Vector2(260, 500), SelectMode = ItemList.SelectModeEnum.Single };
        _areas.AddThemeColorOverride("font_color", Colors.Black);
        _areas.ItemActivated += index => AreaRequested?.Invoke((int)_areas.GetItemMetadata((int)index));
        body.AddChild(_picture); body.AddChild(_areas);
        var buttons = new HBoxContainer();
        _travel = Plain("Travel", () => TravelRequested?.Invoke());
        _clear = Plain("Clear", () => ClearRequested?.Invoke());
        _close = Plain("Close", () => CloseRequested?.Invoke());
        foreach (var button in new[] { _travel, _clear, _close }) buttons.AddChild(button);
        _status = new Label { Modulate = new Color(.15f, .1f, .05f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _message = new Label { Modulate = new Color(.35f, .05f, .05f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        var hint = new Label
        {
            Modulate = new Color(.3f, .3f, .3f), AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "Click the map or double-click an area to add a waypoint; Enter or confirm travels; W, Escape or cancel closes (during travel: lands " +
                "where the journey stands) | PROJECT diagnostic panel: terrain picture, not the original's map art; area list for testing; parity NOT_VERIFIED",
        };
        foreach (var control in new Control[] { header, body, buttons, _status, _message, hint }) contents.AddChild(control);
        GetViewport().SizeChanged += ResizePanel;
        ResizePanel(); Hide();
    }

    private static Button Plain(string text, Action pressed)
    {
        var button = new Button { Name = text, Text = text, FocusMode = Control.FocusModeEnum.All };
        button.Pressed += pressed;
        return button;
    }

    private void ResizePanel()
    {
        var size = GetViewport().GetVisibleRect().Size;
        _panel.Position = new Vector2(Math.Max(0, (size.X - 800) / 2), Math.Max(0, (size.Y - 680) / 2));
    }

    public void Open(AreaWorldMap map, Func<int, bool> known, Func<int, string> names)
    {
        _map = map; _known = known;
        var data = map.Data;
        _picture.Texture = ImageTexture.CreateFromImage(Image.CreateFromData(data.PictureSize, data.PictureSize, false, Image.Format.Rgba8, data.Picture));
        _areas.Clear();
        for (var area = 1; area < data.TravelAreas.Count; area++)
        {
            var index = _areas.AddItem($"{area} {names(area)}");
            _areas.SetItemMetadata(index, area);
        }
        Show(); Render(null);
    }

    public void CloseView() { Hide(); _map = null; _known = null; }

    /// <summary>Re-renders the marks, the area list's known marks, the buttons and the texts.</summary>
    public void Render(string? message)
    {
        if (_map is not { } map) return;
        for (var i = 0; i < _areas.ItemCount; i++)
        {
            var area = (int)_areas.GetItemMetadata(i);
            _areas.SetItemCustomFgColor(i, _known?.Invoke(area) == true ? new Color(.05f, .35f, .05f) : Colors.Black);
        }
        var (steps, hours) = map.Estimate();
        _status.Text = map.Travelling
            ? $"Travelling: step {map.Steps}, {map.GameMs / WorldJourney.MsPerSector} h so far, {map.Waypoints.Count} waypoint(s) left"
            : $"{map.Waypoints.Count} waypoint(s), {steps} sector step(s), about {hours} h"
                + (map.Landing is { } landing ? $" | landed at ({landing.X},{landing.Y})" : "");
        _message.Text = message ?? "";
        _travel.Disabled = map.Travelling || map.Waypoints.Count == 0;
        _clear.Disabled = map.Travelling || map.Waypoints.Count == 0;
        _overlay.QueueRedraw();
    }

    public void Confirm() { if (!_travel.Disabled) TravelRequested?.Invoke(); }

    private void PictureInput(InputEvent input)
    {
        // A double-click's second press would add a waypoint in the same sector again.
        if (_map is null || input is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left, DoubleClick: false } click) return;
        var size = _picture.Size;
        if (size.X <= 0 || size.Y <= 0) return;
        var x = (int)(click.Position.X / size.X * _map.Data.PictureSize);
        var y = (int)(click.Position.Y / size.Y * _map.Data.PictureSize);
        if (x < 0 || y < 0 || x >= _map.Data.PictureSize || y >= _map.Data.PictureSize) return;
        WaypointRequested?.Invoke(AreaWorldMap.PixelOf(x, y));
        _picture.AcceptEvent();
    }

    internal Vector2 ToPicture(WorldMapPixel pixel)
    {
        var scale = _picture.Size.X / (_map!.Data.PictureSize * (float)AreaWorldMap.PictureScale);
        return new Vector2(pixel.X * scale, pixel.Y * scale);
    }
}

// Draws the marks over the world-map picture: known areas, the route, the waypoints and the PC.
internal partial class AreaWorldMapOverlay : Control
{
    internal AreaWorldMapPanel? Panel { get; init; }

    public override void _Draw()
    {
        if (Panel?.Map is not { } map) return;
        var data = map.Data;
        for (var area = 1; area < data.TravelAreas.Count; area++)
        {
            var at = Panel.ToPicture(WorldTravelGeometry.Pixel(data.TravelAreas[area].Location, data.Width));
            var known = Panel.Known?.Invoke(area) == true;
            DrawCircle(at, known ? 3.5f : 2f, known ? new Color(1, .85f, .1f) : new Color(.9f, .9f, .9f, .7f));
        }
        var route = map.RoutePixels();
        for (var i = 1; i < route.Count; i++) DrawLine(Panel.ToPicture(route[i - 1]), Panel.ToPicture(route[i]), new Color(.85f, .1f, .1f), 2);
        foreach (var waypoint in map.Waypoints)
            DrawRect(new Rect2(Panel.ToPicture(WorldTravelGeometry.Pixel(waypoint.End, data.Width)) - new Vector2(3, 3), new Vector2(6, 6)), new Color(.85f, .1f, .1f));
        var pc = Panel.ToPicture(map.Position);
        DrawCircle(pc, 5, Colors.White); DrawCircle(pc, 3.5f, new Color(.1f, .2f, .9f));
    }
}

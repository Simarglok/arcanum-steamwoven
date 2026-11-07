using Arcanum.Simulation.Travel;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-048 diagnostic world-map panel (<see cref="AreaWorldMap"/>): the project's picture of the start map drawn from its
/// terrain (one pixel per 4 × 4 sectors, blocked sectors darker), the PC, the known areas, the waypoints and the planned route;
/// a click on the picture adds a waypoint there; Travel, Clear and Close. ARC-DEV-050: only in debug mode it also shows the list of
/// every area (a double-click adds a waypoint at the area) and Teleport, which moves the PC to the selected area at once; it then
/// opens on any map, and off the start map it only teleports. The original's world-map art, zoom, notes and town maps are not
/// drawn. The app re-renders it after every change and travel step.
/// </summary>
public partial class AreaWorldMapPanel : CanvasLayer
{
    private PanelContainer _panel = null!;
    private TextureRect _picture = null!;
    private AreaWorldMapOverlay _overlay = null!;
    private ItemList _areas = null!;
    private Label _status = null!;
    private Label _message = null!;
    private Label _header = null!, _hint = null!;
    private Button _travel = null!, _clear = null!, _close = null!, _teleport = null!;
    private bool _debugMode;
    private AreaWorldMapArtView _artView = null!;
    private AreaWorldMapArtLook? _artLook;
    private AreaWorldMap? _map;
    private Func<int, bool>? _known;
    internal AreaWorldMap? Map => _map;
    internal Func<int, bool>? Known => _known;

    public event Action<WorldMapPixel>? WaypointRequested;
    public event Action<int>? AreaRequested;
    public event Action<int>? TeleportRequested;
    public event Action? TravelRequested;
    public event Action? ClearRequested;
    public event Action? CloseRequested;
    /// <summary>An interface sound of the art window (ARC-UI-073: 3000/3001 an art button's press and release).</summary>
    public event Action<int>? SoundRequested;
    /// <summary>ARC-UI-083: a hovered marker's description for the message window, null when the pointer left it.</summary>
    public event Action<string?>? DescriptionRequested;
    /// <summary>ARC-UI-083: a hovered button's hint for the message window, null when the pointer left it.</summary>
    public event Action<string?>? HintRequested;
    /// <summary>ARC-UI-073: false while another window owns the keys (the Esc menu over the map): the arrows then do not scroll the map.</summary>
    public Func<bool>? PollGate { get; set; }
    public bool IsOpen => Visible;
    /// <summary>ARC-UI-073: the window shows the original look (art) instead of the diagnostic panel.</summary>
    public bool ArtMode { get; private set; }
    internal AreaWorldMapArtView ArtView => _artView;
    internal string StatusForSelfTest => _status.Text;
    internal string MessageForSelfTest => _message.Text;
    /// <summary>ARC-DEV-050: the area list and Teleport are shown (debug mode only).</summary>
    internal bool DebugControlsShownForSelfTest => _areas.Visible && _teleport.Visible;
    /// <summary>Selects an area in the list and presses Teleport, as a player does.</summary>
    internal void TeleportForSelfTest(int area)
    {
        for (var i = 0; i < _areas.ItemCount; i++)
            if ((int)_areas.GetItemMetadata(i) == area) { _areas.Select(i); break; }
        _teleport.EmitSignal(BaseButton.SignalName.Pressed);
    }

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
        _header = new Label { Text = "World map", Modulate = new Color(.15f, .1f, .05f) };
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
        _teleport = Plain("Teleport", () =>
        {
            if (_areas.GetSelectedItems() is { Length: > 0 } selected) TeleportRequested?.Invoke((int)_areas.GetItemMetadata(selected[0]));
            else Render("select an area in the list to teleport to");
        });
        _teleport.Modulate = new Color(.6f, .1f, .05f);
        foreach (var button in new[] { _travel, _clear, _teleport, _close }) buttons.AddChild(button);
        _status = new Label { Modulate = new Color(.15f, .1f, .05f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _message = new Label { Modulate = new Color(.35f, .05f, .05f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _hint = new Label { Modulate = new Color(.3f, .3f, .3f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        foreach (var control in new Control[] { _header, body, buttons, _status, _message, _hint }) contents.AddChild(control);
        // ARC-UI-073: the original-look window; it forwards its waypoints and Travel to the same events as the diagnostic panel.
        _artView = new AreaWorldMapArtView();
        AddChild(_artView);
        _artView.WaypointRequested += pixel => { if (_map is { OnStartMap: true }) WaypointRequested?.Invoke(pixel); };
        _artView.TravelRequested += () => { if (!_travel.Disabled) TravelRequested?.Invoke(); };
        _artView.SoundRequested += id => SoundRequested?.Invoke(id);
        _artView.PollGate = () => PollGate?.Invoke() ?? true;
        _artView.DescriptionRequested += text => DescriptionRequested?.Invoke(text);
        _artView.HintRequested += text => HintRequested?.Invoke(text);
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

    /// <summary>Sets the art window's textures (ARC-UI-073); without them, and in debug mode, the diagnostic panel is shown.</summary>
    public void SetArtLook(AreaWorldMapArtLook? look)
    {
        _artLook = look;
        if (look is not null) _artView.SetLook(look);
    }

    /// <summary>Opens the view; ARC-DEV-050: <paramref name="debugMode"/> shows the area list and Teleport, otherwise hidden.</summary>
    public void Open(AreaWorldMap map, Func<int, bool> known, Func<int, string> names, bool debugMode)
    {
        _map = map; _known = known; _debugMode = debugMode;
        // ARC-UI-073: the original look when its art read; debug mode keeps the diagnostic panel (its area list and Teleport).
        ArtMode = _artLook is not null && !debugMode;
        _panel.Visible = !ArtMode;
        _areas.Visible = _teleport.Visible = debugMode;
        _header.Text = debugMode ? "World map | DEBUG MODE: every area open to Teleport (not gameplay)" : "World map";
        _header.Modulate = debugMode ? new Color(.6f, .1f, .05f) : new Color(.15f, .1f, .05f);
        _hint.Text = (map.OnStartMap ? "Click the map to add a waypoint" + (debugMode ? " (or double-click an area)" : "") + "; Enter or confirm travels; " : "Not on the start map: no journey from here; ")
            + "W or cancel closes (during travel: lands where the journey stands); Escape opens the menu over the map"
            + (debugMode ? "; Teleport moves to the selected area at once, with no game time and nothing discovered" : "")
            + " | PROJECT diagnostic panel: terrain picture, not the original's map art; parity NOT_VERIFIED";
        var data = map.Data;
        _picture.Texture = ImageTexture.CreateFromImage(Image.CreateFromData(data.PictureSize, data.PictureSize, false, Image.Format.Rgba8, data.Picture));
        _areas.Clear();
        for (var area = 1; area < data.TravelAreas.Count; area++)
        {
            var index = _areas.AddItem($"{area} {names(area)}");
            _areas.SetItemMetadata(index, area);
        }
        Show();
        if (ArtMode) _artView.Open(map, known);
        Render(null);
    }

    public void CloseView() { Hide(); _artView.CloseView(); _map = null; _known = null; _debugMode = false; ArtMode = false; }

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
        _status.Text = !map.OnStartMap ? "Not on the start map: teleport only"
            : map.Travelling
            ? $"Travelling: step {map.Steps}, {map.GameMs / WorldJourney.MsPerSector} h so far, {map.Waypoints.Count} waypoint(s) left"
            : $"{map.Waypoints.Count} waypoint(s), {steps} sector step(s), about {hours} h"
                + (map.Landing is { } landing ? $" | landed at ({landing.X},{landing.Y})" : "");
        _message.Text = message ?? "";
        _travel.Disabled = map.Travelling || map.Waypoints.Count == 0;
        _clear.Disabled = map.Travelling || map.Waypoints.Count == 0;
        _teleport.Disabled = !_debugMode || map.Travelling;
        _overlay.QueueRedraw();
        if (ArtMode) _artView.Render(message);
    }

    public void Confirm() { if (!_travel.Disabled) TravelRequested?.Invoke(); }

    /// <summary>Home (ARC-UI-073, WM22): the art window centres on the party marker.</summary>
    public void CentreOnParty() { if (ArtMode && _map is { Travelling: false }) _artView.Centre(); }

    /// <summary>Z (ARC-UI-083, WM22): the art window's other view kind, not while travelling.</summary>
    public void ToggleKind() { if (ArtMode) _artView.ToggleKind(); }

    /// <summary>Delete (ARC-UI-073, WM23): the art window drops its waypoints. The executable drops the selected one, or the last; the
    /// selection and the single removal follow in ARC-UI-085.</summary>
    public void DropWaypoints() { if (ArtMode) ClearRequested?.Invoke(); }

    private void PictureInput(InputEvent input)
    {
        // A double-click's second press would add a waypoint in the same sector again.
        if (_map is not { OnStartMap: true } || input is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left, DoubleClick: false } click) return;
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
        // ARC-DEV-050: a debug-mode view off the start map has no position or route.
        if (!map.OnStartMap) return;
        var route = map.RoutePixels();
        for (var i = 1; i < route.Count; i++) DrawLine(Panel.ToPicture(route[i - 1]), Panel.ToPicture(route[i]), new Color(.85f, .1f, .1f), 2);
        foreach (var waypoint in map.Waypoints)
            DrawRect(new Rect2(Panel.ToPicture(WorldTravelGeometry.Pixel(waypoint.End, data.Width)) - new Vector2(3, 3), new Vector2(6, 6)), new Color(.85f, .1f, .1f));
        var pc = Panel.ToPicture(map.Position);
        DrawCircle(pc, 5, Colors.White); DrawCircle(pc, 3.5f, new Color(.1f, .2f, .9f));
    }
}

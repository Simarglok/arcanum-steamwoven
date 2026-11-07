using Arcanum.Simulation.Travel;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>The world-map window's textures: the interface art by number and the 64 map tiles (column + 8 × row).</summary>
public sealed record AreaWorldMapArtLook(IReadOnlyDictionary<int, Texture2D[]> Art, IReadOnlyList<Texture2D> Tiles);

/// <summary>
/// ARC-UI-073: the world-map window in the original's look (<see cref="AreaWorldMapArtLayout"/>, ledger WM1–WM39): art 138 behind the
/// 501 × 365 map view of the 2000 × 2000 picture, scrolled by the arrow keys (20 px per step, at most every 10 ms, WM18; the
/// executable's two further paths, the main-loop hook at 40 px and the key records at 20 px each, would scroll at three rates, WM21, so
/// the viewer keeps the one poll), the compass button (40 px, WM31) and Home (centre on the party); a click in the view adds a waypoint at
/// that picture pixel; the zoom and whole-map buttons are drawn and do nothing yet. The window is 800 × 400 and sits between the bars at the
/// largest whole factor that fits. Markers: the party, the known areas, the waypoints and the route; their names, hover texts and the
/// continent view follow in ARC-UI-083.
/// </summary>
public partial class AreaWorldMapArtView : Control
{
    private AreaWorldMapArtLook? _look;
    private AreaWorldMap? _map;
    private Func<int, bool>? _known;
    private string _message = "";
    private (int X, int Y) _offset, _max = AreaWorldMapArtLayout.MaxOffset();
    private AreaWorldMapArtButton? _hovered, _pressed;
    private bool _compassDragged;
    private long _lastPoll, _nextCompass;
    private Vector2 _pointer;
    private Control _canvas = null!, _chrome = null!;

    /// <summary>A left press in the view, as the picture pixel of the executable (WM25), when the world is walkable.</summary>
    public event Action<WorldMapPixel>? WaypointRequested;
    public event Action? TravelRequested;
    /// <summary>An interface sound (3000/3001 an art button's press and release).</summary>
    public event Action<int>? SoundRequested;
    /// <summary>Asked before the arrow poll; false while another window owns the keys.</summary>
    public Func<bool>? PollGate { get; set; }

    public (int X, int Y) Offset => _offset;
    public (int X, int Y) MaxOffset => _max;
    internal string MessageForSelfTest => _message;

    public override void _Ready()
    {
        Name = "WorldMapArt";
        Size = new Vector2(AreaWorldMapArtLayout.WindowWidth, AreaWorldMapArtLayout.WindowHeight);
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        var (x, y, width, height) = AreaWorldMapArtLayout.View;
        _canvas = new ViewCanvas { Host = this, Position = new Vector2(x, y), Size = new Vector2(width, height), ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
        _chrome = new ChromeCanvas { Host = this, Size = Size, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_canvas);
        AddChild(_chrome);
        GetViewport().SizeChanged += Place;
        AreaGameBarPanel.InsetsChanged += Place;
        Visible = false;
    }

    public override void _ExitTree() => AreaGameBarPanel.InsetsChanged -= Place;

    public void SetLook(AreaWorldMapArtLook look) => _look = look ?? throw new ArgumentNullException(nameof(look));

    public bool HasLook => _look is not null;

    /// <summary>Shows the window over <paramref name="map"/>, centred on the party (WM12; off the start map, which only a debug teleport reaches, on
    /// the picture's middle).</summary>
    public void Open(AreaWorldMap map, Func<int, bool> known)
    {
        if (_look is null) throw new InvalidOperationException("ARC-UI-073: the world-map art is not set");
        _map = map; _known = known; _message = "";
        _max = AreaWorldMapArtLayout.MaxOffset(map.Data.Width, map.Data.Width);
        (_hovered, _pressed, _compassDragged) = (null, null, false);
        _lastPoll = (long)Time.GetTicksMsec();
        Centre();
        Visible = true;
        Place();
        Redraw();
    }

    public void CloseView()
    {
        Visible = false;
        _map = null; _known = null;
        (_hovered, _pressed) = (null, null);
    }

    /// <summary>Redraws after a change of the map or the message; a journey keeps the party in the view (PROJECT: the executable's view during
    /// travel is not read, F0, and it does not scroll then, WM18).</summary>
    public void Render(string? message)
    {
        if (_map is not { } map) return;
        _message = message ?? "";
        if (map.OnStartMap && map.Travelling && !InView(map.Position)) Centre();
        Redraw();
    }

    private bool InView(WorldMapPixel pixel)
    {
        var (x, y) = AreaWorldMapArtLayout.ToWindow(pixel.X, pixel.Y, _offset);
        var view = AreaWorldMapArtLayout.View;
        return x >= view.X && y >= view.Y && x < view.X + view.Width && y < view.Y + view.Height;
    }

    /// <summary>Centres the view on the party marker (open, Home and a compass release without a drag, WM12, WM22, WM31).</summary>
    public void Centre()
    {
        if (_map is not { } map) return;
        var at = map.OnStartMap ? map.Position : new WorldMapPixel(map.Data.Width / 2, map.Data.Width / 2);
        _offset = AreaWorldMapArtLayout.CentreOn(at.X, at.Y, _max);
        Redraw();
    }

    /// <summary>One scroll step in the executable's direction code (0 up, clockwise to 7 up-left); true when the view moved.</summary>
    public bool ScrollBy(int direction, int step)
    {
        var next = AreaWorldMapArtLayout.Scroll(_offset, direction, step, _max);
        if (next == _offset) return false;
        _offset = next;
        Redraw();
        return true;
    }

    private bool Travelling => _map?.Travelling == true;

    private void Redraw()
    {
        _canvas?.QueueRedraw();
        _chrome?.QueueRedraw();
        QueueRedraw();
    }

    /// <summary>The window between the bars at the largest whole factor that fits (at least a quarter when it cannot).</summary>
    public void Place()
    {
        var (top, free) = AreaGameBarPanel.FreeArea(GetViewportRect().Size);
        var fit = MathF.Min(free.X / AreaWorldMapArtLayout.WindowWidth, free.Y / AreaWorldMapArtLayout.WindowHeight);
        var factor = fit >= 1 ? MathF.Floor(fit) : MathF.Max(.25f, fit);
        Scale = new Vector2(factor, factor);
        Position = new Vector2(MathF.Round((free.X - AreaWorldMapArtLayout.WindowWidth * factor) / 2),
            top + MathF.Round((free.Y - AreaWorldMapArtLayout.WindowHeight * factor) / 2));
    }

    public override void _Process(double delta)
    {
        if (!Visible || _map is null || Travelling || PollGate?.Invoke() == false) return;
        var now = (long)Time.GetTicksMsec();
        // WM18: the poll runs at most once for 10 ms; left beats right and up beats down.
        if (now - _lastPoll >= AreaWorldMapArtLayout.PollMilliseconds)
        {
            var direction = AreaWorldMapArtLayout.Direction(Godot.Input.IsKeyPressed(Key.Left), Godot.Input.IsKeyPressed(Key.Right), Godot.Input.IsKeyPressed(Key.Up), Godot.Input.IsKeyPressed(Key.Down));
            _lastPoll = now;
            if (direction >= 0) ScrollBy(direction, AreaWorldMapArtLayout.KeyStep);
        }
        // WM31: a held compass repeats its step; a point inside the dead zone does not scroll.
        if (_pressed == AreaWorldMapArtButton.Compass && now >= _nextCompass)
        {
            _nextCompass = now + AreaWorldMapArtLayout.CompassRepeatMilliseconds;
            CompassStep(_pointer);
        }
    }

    private void CompassStep(Vector2 at)
    {
        var direction = AreaWorldMapArtLayout.CompassDirection(at.X, at.Y);
        if (direction < 0) return;
        _compassDragged = true;
        ScrollBy(direction, AreaWorldMapArtLayout.CompassStep);
    }

    private AreaWorldMapArtButton? ButtonAt(Vector2 at)
    {
        if (_look is null) return null;
        foreach (var button in Enum.GetValues<AreaWorldMapArtButton>())
        {
            if (button == AreaWorldMapArtButton.Travel && _map?.OnStartMap != true) continue;
            var (art, x, y) = AreaWorldMapArtLayout.Button(button);
            if (!_look.Art.TryGetValue(art, out var frames) || frames.Length == 0) continue;
            if (at.X >= x && at.Y >= y && at.X < x + frames[0].GetWidth() && at.Y < y + frames[0].GetHeight()) return button;
        }
        return null;
    }

    public override void _GuiInput(InputEvent input)
    {
        if (_map is null) return;
        switch (input)
        {
            case InputEventMouseMotion motion:
                _pointer = motion.Position;
                var over = ButtonAt(motion.Position);
                if (over != _hovered) { _hovered = over; Redraw(); }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                _pointer = button.Position;
                MouseLeft(button);
                AcceptEvent();
                break;
        }
    }

    private void MouseLeft(InputEventMouseButton button)
    {
        var at = ButtonAt(button.Position);
        _hovered = at;
        if (button.Pressed)
        {
            // A double-click's second press would add a waypoint in the same sector again.
            if (button.DoubleClick) return;
            _pressed = at;
            if (at is not null) SoundRequested?.Invoke(AreaGameBarLayout.PressSound);
            if (at == AreaWorldMapArtButton.Compass && !Travelling) { _compassDragged = false; _nextCompass = (long)Time.GetTicksMsec() + AreaWorldMapArtLayout.CompassRepeatMilliseconds; CompassStep(button.Position); }
            else if (at is null && _map!.OnStartMap && !Travelling
                && AreaWorldMapArtLayout.ToPicture(button.Position.X, button.Position.Y, _offset) is { } pixel)
                WaypointRequested?.Invoke(new WorldMapPixel(pixel.X, pixel.Y));
        }
        else if (_pressed is { } held)
        {
            _pressed = null;
            if (held == AreaWorldMapArtButton.Compass)
            {
                // WM31: a release without a drag centres on the party.
                if (!_compassDragged && !Travelling) Centre();
                _compassDragged = false;
            }
            else if (at == held)
            {
                SoundRequested?.Invoke(AreaGameBarLayout.ReleaseSound);
                if (held == AreaWorldMapArtButton.Travel) TravelRequested?.Invoke();
            }
        }
        Redraw();
    }

    public override void _Notification(int what)
    {
        if (what != NotificationMouseExit || _hovered is null) return;
        _hovered = null;
        Redraw();
    }

    public override void _Draw()
    {
        // The background; the view and the buttons are the children above it.
        if (_look?.Art.TryGetValue(AreaWorldMapArtLayout.BackgroundArt, out var back) == true && back.Length > 0) DrawTexture(back[0], Vector2.Zero);
    }

    // The view: black fill, the tiles, the markers and the route.
    private void DrawView(CanvasItem canvas)
    {
        var view = AreaWorldMapArtLayout.View;
        canvas.DrawRect(new Rect2(0, 0, view.Width, view.Height), Colors.Black);
        if (_look is null) return;
        var (first, last, firstRow, lastRow) = AreaWorldMapArtLayout.VisibleTiles(_offset);
        for (var row = firstRow; row <= lastRow; row++)
            for (var column = first; column <= last; column++)
            {
                var index = AreaWorldMapArtLayout.TileColumns * row + column;
                if (index >= _look.Tiles.Count) continue;
                canvas.DrawTexture(_look.Tiles[index], new Vector2(AreaWorldMapArtLayout.TileSize * column - _offset.X, AreaWorldMapArtLayout.TileSize * row - _offset.Y));
            }
        if (_map is { } map)
        {
            var data = map.Data;
            for (var area = 1; area < data.TravelAreas.Count; area++)
                if (_known?.Invoke(area) == true) Marker(canvas, AreaWorldMapArtLayout.AreaArt, WorldTravelGeometry.Pixel(data.TravelAreas[area].Location, data.Width));
            if (map.OnStartMap)
            {
                var route = map.RoutePixels();
                for (var i = 1; i < route.Count; i++) canvas.DrawLine(ViewPoint(route[i - 1]), ViewPoint(route[i]), new Color(.85f, .1f, .1f), 2);
                foreach (var waypoint in map.Waypoints) Marker(canvas, AreaWorldMapArtLayout.WaypointArt, WorldTravelGeometry.Pixel(waypoint.End, data.Width));
                if (map.Session.QueryLastDiscoveredArea() is > 0 and var last2 && last2 < data.TravelAreas.Count)
                    Marker(canvas, AreaWorldMapArtLayout.NewAreaArt, WorldTravelGeometry.Pixel(data.TravelAreas[last2].Location, data.Width));
                Marker(canvas, AreaWorldMapArtLayout.PartyArt, map.Position);
            }
        }
        if (_message.Length > 0)
        {
            var font = ThemeDB.FallbackFont;
            canvas.DrawRect(new Rect2(0, view.Height - 18, view.Width, 18), new Color(0, 0, 0, .7f));
            canvas.DrawString(font, new Vector2(4, view.Height - 5), _message, HorizontalAlignment.Left, view.Width - 8, 12, new Color(1f, .85f, .5f));
        }
    }

    private Vector2 ViewPoint(WorldMapPixel pixel) => new(pixel.X - _offset.X, pixel.Y - _offset.Y);

    private void Marker(CanvasItem canvas, int art, WorldMapPixel pixel)
    {
        if (_look is null || !_look.Art.TryGetValue(art, out var frames) || frames.Length == 0) return;
        var texture = frames[0];
        canvas.DrawTexture(texture, ViewPoint(pixel) - new Vector2(texture.GetWidth() / 2, texture.GetHeight() / 2));
    }

    // The buttons over the view (the compass overlaps its bottom edge).
    private void DrawChrome(CanvasItem canvas)
    {
        if (_look is null) return;
        foreach (var button in Enum.GetValues<AreaWorldMapArtButton>())
        {
            if (button == AreaWorldMapArtButton.Travel && _map?.OnStartMap != true) continue;
            var (art, x, y) = AreaWorldMapArtLayout.Button(button);
            if (!_look.Art.TryGetValue(art, out var frames) || frames.Length == 0) continue;
            var frame = AreaCharacterArtDraw.Frame(frames.Length, _pressed is not null, _pressed == button, _hovered == button);
            canvas.DrawTexture(frames[Math.Min(frame, frames.Length - 1)], new Vector2(x, y));
        }
    }

    private sealed partial class ViewCanvas : Control
    {
        internal AreaWorldMapArtView? Host { get; init; }
        public override void _Draw() => Host?.DrawView(this);
    }

    private sealed partial class ChromeCanvas : Control
    {
        internal AreaWorldMapArtView? Host { get; init; }
        public override void _Draw() => Host?.DrawChrome(this);
    }
}

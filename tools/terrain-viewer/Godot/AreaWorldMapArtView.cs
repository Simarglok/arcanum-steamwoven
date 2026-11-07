using Arcanum.Simulation.Travel;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>The world-map window's textures and texts: the interface art by number, the 64 map tiles (column + 8 × row), the continent picture, the
/// markers' font and the texts of <c>mes/worldmap.mes</c> (ARC-UI-083).</summary>
public sealed record AreaWorldMapArtLook(IReadOnlyDictionary<int, Texture2D[]> Art, IReadOnlyList<Texture2D> Tiles, Texture2D? Zoomed, AreaArtFontFace Font,
    AreaWorldMapArtContent Content);

/// <summary>
/// ARC-UI-073, ARC-UI-083: the world-map window in the original's look (<see cref="AreaWorldMapArtLayout"/>, ledger WM1–WM64): art 138 behind the
/// 501 × 365 view, which is either the map view (the 2000 × 2000 picture, scrolled by the arrow keys, the compass and Home, WM18, WM31) or, by the
/// zoom and whole-map buttons and Z, the continent view (<c>Map_Zoomed</c> with a red dot for every marker, WM53, WM54); a click in the map view
/// adds a waypoint, a click in the continent view zooms in on the clicked point (WM34). Over the picture are the markers with the areas' names in
/// the original's font (WM45–WM49), the readouts of the sector under the pointer in the two black boxes (WM51) and the cover over the view's bottom
/// edge (WM42); a marker under the pointer shows its description and a button its hint in the message window (WM58–WM61). The window is 800 × 400 and
/// sits between the bars at the largest whole factor that fits.
/// </summary>
public partial class AreaWorldMapArtView : Control
{
    private AreaWorldMapArtLook? _look;
    private AreaWorldMap? _map;
    private Func<int, bool>? _known;
    private string _message = "";
    private (int X, int Y) _offset, _max = AreaWorldMapArtLayout.MaxOffset();
    private AreaWorldMapKind _kind;
    private AreaWorldMapArtButton? _hovered, _pressed;
    private bool _compassDragged, _pressedInView;
    private long _lastPoll, _nextCompass;
    private Vector2 _pointer;
    private (long X, long Y)? _readoutPixel;
    private int _hoverArea;
    // The areas that carry a marker, in the original's order: the known ones from the last down to 1 at the open, later finds after them (WM11).
    private readonly List<int> _markers = [];
    private Control _canvas = null!, _chrome = null!, _note = null!;

    /// <summary>A left press in the view, as the picture pixel of the executable (WM25), when the world is walkable.</summary>
    public event Action<WorldMapPixel>? WaypointRequested;
    public event Action? TravelRequested;
    /// <summary>An interface sound (3000/3001 an art button's press and release).</summary>
    public event Action<int>? SoundRequested;
    /// <summary>A hovered marker's description for the message window, null when the pointer left it (WM59, WM60).</summary>
    public event Action<string?>? DescriptionRequested;
    /// <summary>A hovered button's hint for the message window ("Action", a new line and the button's text, WM61), null when the pointer left it.</summary>
    public event Action<string?>? HintRequested;
    /// <summary>Asked before the arrow poll; false while another window owns the keys.</summary>
    public Func<bool>? PollGate { get; set; }

    public (int X, int Y) Offset => _offset;
    public (int X, int Y) MaxOffset => _max;
    public AreaWorldMapKind Kind => _kind;
    internal string MessageForSelfTest => _message;
    internal (string West, string South)? ReadoutForSelfTest => _readoutPixel is { } pixel && _map is { } map
        ? AreaWorldMapArtLayout.Readouts(pixel.X, pixel.Y, map.Data.Width, _look!.Content.Offsets.Longitude, _look.Content.Offsets.Latitude) : null;
    internal IReadOnlyList<int> MarkersForSelfTest => _markers;
    /// <summary>The pointer at a window point of the 800 × 400 window, as a mouse move gives it (the readouts and the hover text follow).</summary>
    internal void PointerForSelfTest(Vector2 windowPoint) { _pointer = windowPoint; SetHovered(ButtonAt(windowPoint)); PointerOverView(windowPoint); }
    internal void SetOffsetForSelfTest((int X, int Y) offset) { _offset = AreaWorldMapArtLayout.Clamp(offset, _max); Redraw(); }

    public override void _Ready()
    {
        Name = "WorldMapArt";
        Size = new Vector2(AreaWorldMapArtLayout.WindowWidth, AreaWorldMapArtLayout.WindowHeight);
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        var (x, y, width, height) = AreaWorldMapArtLayout.View;
        _canvas = new ViewCanvas { Host = this, Position = new Vector2(x, y), Size = new Vector2(width, height), ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
        _chrome = new ChromeCanvas { Host = this, Size = Size, MouseFilter = MouseFilterEnum.Ignore };
        // The art text draw needs the premultiplied material; the art here has only opaque or clear pixels, which it draws the same.
        AreaArtTextDraw.Prepare(_canvas);
        AreaArtTextDraw.Prepare(_chrome);
        // The diagnostic message is drawn with the engine's font, which the premultiplied material would spoil: it has a canvas of its own.
        _note = new NoteCanvas { Host = this, Position = new Vector2(x, y), Size = new Vector2(width, height), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_canvas);
        AddChild(_note);
        AddChild(_chrome);
        GetViewport().SizeChanged += Place;
        AreaGameBarPanel.InsetsChanged += Place;
        Visible = false;
    }

    public override void _ExitTree() => AreaGameBarPanel.InsetsChanged -= Place;

    public void SetLook(AreaWorldMapArtLook look) => _look = look ?? throw new ArgumentNullException(nameof(look));

    public bool HasLook => _look is not null;

    /// <summary>Shows the window over <paramref name="map"/> in the map view, centred on the party (WM12; off the start map, which only a debug teleport
    /// reaches, on the picture's middle).</summary>
    public void Open(AreaWorldMap map, Func<int, bool> known)
    {
        if (_look is null) throw new InvalidOperationException("ARC-UI-073: the world-map art is not set");
        _map = map; _known = known; _message = "";
        _max = AreaWorldMapArtLayout.MaxOffset(map.Data.Width, map.Data.Width);
        (_hovered, _pressed, _compassDragged, _pressedInView) = (null, null, false, false);
        _kind = AreaWorldMapKind.Map; _readoutPixel = null; _hoverArea = -1;
        _lastPoll = (long)Time.GetTicksMsec();
        _markers.Clear();
        SyncMarkers();
        Centre();
        Visible = true;
        Place();
        Redraw();
    }

    public void CloseView()
    {
        // The message window shows nothing of the window's once it is gone.
        ClearDescription();
        if (_hovered is not null) SetHovered(null);
        Visible = false;
        _map = null; _known = null;
        (_hovered, _pressed) = (null, null);
        _markers.Clear();
        _hoverArea = -1;
    }

    // WM11: a marker for every known area, from the last down to 1 at the open; an area found on the journey is appended.
    private void SyncMarkers()
    {
        if (_map is not { } map || _known is null) return;
        if (_markers.Count == 0)
        {
            for (var area = map.Data.TravelAreas.Count - 1; area >= 1; area--) if (_known(area)) _markers.Add(area);
            return;
        }
        for (var area = 1; area < map.Data.TravelAreas.Count; area++) if (_known(area) && !_markers.Contains(area)) _markers.Add(area);
    }

    /// <summary>Redraws after a change of the map or the message; a journey keeps the party in the map view (PROJECT: the executable's view during
    /// travel is not read, F0, and it does not scroll then, WM18).</summary>
    public void Render(string? message)
    {
        if (_map is not { } map) return;
        _message = message ?? "";
        SyncMarkers();
        if (map.OnStartMap && map.Travelling && _kind == AreaWorldMapKind.Map && !InView(map.Position)) Centre();
        Redraw();
    }

    private bool InView(WorldMapPixel pixel)
    {
        var (x, y) = AreaWorldMapArtLayout.ToWindow(pixel.X, pixel.Y, _offset);
        var view = AreaWorldMapArtLayout.View;
        return x >= view.X && y >= view.Y && x < view.X + view.Width && y < view.Y + view.Height;
    }

    /// <summary>Centres the map view on the party marker (open, Home and a compass release without a drag, WM12, WM22, WM31).</summary>
    public void Centre()
    {
        if (_map is not { } map) return;
        var at = map.OnStartMap ? map.Position : new WorldMapPixel(map.Data.Width / 2, map.Data.Width / 2);
        _offset = AreaWorldMapArtLayout.CentreOn(at.X, at.Y, _max);
        Redraw();
    }

    /// <summary>Z (WM22): the other view kind, not while travelling. True when the kind changed.</summary>
    public bool ToggleKind() => SetKind(_kind == AreaWorldMapKind.Map ? AreaWorldMapKind.Continent : AreaWorldMapKind.Map);

    /// <summary>Switches the view kind (the buttons 811 and 812, Z, WM62); each kind keeps its own offset and nothing is re-centred. The executable's
    /// buttons also work during a journey and then end it (WM63, a possible gameplay flaw whose effect is F1); the viewer guards them as Z is guarded.</summary>
    public bool SetKind(AreaWorldMapKind kind)
    {
        // WM62: the continent view needs its picture (the executable refuses the switch without it).
        if (_map is null || Travelling || kind == _kind || (kind == AreaWorldMapKind.Continent && _look?.Zoomed is null)) return false;
        _kind = kind;
        ClearDescription();
        Redraw();
        return true;
    }

    /// <summary>One scroll step in the executable's direction code (0 up, clockwise to 7 up-left); true when the view moved. The continent view does not scroll.</summary>
    public bool ScrollBy(int direction, int step)
    {
        if (_kind != AreaWorldMapKind.Map) return false;
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
        _note?.QueueRedraw();
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

    // The buttons shown now: Travel only on the walkable map view, the compass not in the continent view (WM31, WM62).
    private bool Shown(AreaWorldMapArtButton button) => button switch
    {
        AreaWorldMapArtButton.Travel => _map?.OnStartMap == true && _kind == AreaWorldMapKind.Map,
        AreaWorldMapArtButton.Compass => _kind == AreaWorldMapKind.Map,
        _ => true,
    };

    private AreaWorldMapArtButton? ButtonAt(Vector2 at)
    {
        if (_look is null) return null;
        foreach (var button in Enum.GetValues<AreaWorldMapArtButton>())
        {
            if (!Shown(button)) continue;
            var (art, x, y) = AreaWorldMapArtLayout.Button(button);
            if (!_look.Art.TryGetValue(art, out var frames) || frames.Length == 0) continue;
            if (at.X >= x && at.Y >= y && at.X < x + frames[0].GetWidth() && at.Y < y + frames[0].GetHeight()) return button;
        }
        return null;
    }

    // The picture pixel under a window point in the current kind (WM25, WM41).
    private (long X, long Y)? PixelAt(Vector2 at) => _map is null ? null
        : _kind == AreaWorldMapKind.Map ? AreaWorldMapArtLayout.ToPicture(at.X, at.Y, _offset) : AreaWorldMapArtLayout.ZoomedToPicture(at.X, at.Y, _map.Data.Width);

    private IReadOnlyList<(long X, long Y)> MarkerPixels()
    {
        var data = _map!.Data;
        return [.. _markers.Select(area => { var pixel = WorldTravelGeometry.Pixel(data.TravelAreas[area].Location, data.Width); return (pixel.X, pixel.Y); })];
    }

    private string? ButtonHint(AreaWorldMapArtButton button)
    {
        if (_look is null) return null;
        var content = _look.Content;
        var text = button switch
        {
            AreaWorldMapArtButton.Zoom => content.Text(AreaWorldMapArtContent.MapViewText, "Map"),
            AreaWorldMapArtButton.Whole => content.Text(AreaWorldMapArtContent.ContinentViewText, "Continent"),
            AreaWorldMapArtButton.Compass => content.Text(AreaWorldMapArtContent.ScrollText, "Scroll"),
            _ => content.Text(AreaWorldMapArtContent.ToggleText, "Route"),
        };
        return content.Text(AreaWorldMapArtContent.ActionText, "Action") + "\n" + text;
    }

    private void SetHovered(AreaWorldMapArtButton? over)
    {
        if (over == _hovered) return;
        _hovered = over;
        // Leaving a button must not clear the description of a marker still under the pointer.
        if (over is not null || _hoverArea < 0) HintRequested?.Invoke(over is { } button ? ButtonHint(button) : null);
        Redraw();
    }

    // WM52, WM58, WM59: the readouts follow the pointer over the view; a marker under it (the newest wins) shows its description, in the map view only
    // while the world is not travelling.
    private void PointerOverView(Vector2 at)
    {
        var pixel = PixelAt(at);
        if (pixel is { } found)
        {
            _readoutPixel = found;
            _chrome.QueueRedraw();
        }
        var hit = -1;
        if (pixel is { } p && _map is not null && (_kind == AreaWorldMapKind.Continent || !Travelling))
        {
            var half = _kind == AreaWorldMapKind.Map ? AreaWorldMapArtLayout.HoverHalf : AreaWorldMapArtLayout.ZoomedHoverHalf;
            var index = AreaWorldMapArtLayout.HitMarker(MarkerPixels(), p.X, p.Y, half);
            hit = index < 0 ? -1 : _markers[index];
        }
        if (hit == _hoverArea) return;
        _hoverArea = hit;
        DescriptionRequested?.Invoke(hit < 0 ? null : _map!.Data.Areas.Areas[hit].Description);
    }

    private void ClearDescription()
    {
        if (_hoverArea < 0) return;
        _hoverArea = -1;
        DescriptionRequested?.Invoke(null);
    }

    public override void _GuiInput(InputEvent input)
    {
        if (_map is null) return;
        switch (input)
        {
            case InputEventMouseMotion motion:
                _pointer = motion.Position;
                PointerOverView(motion.Position);
                SetHovered(ButtonAt(motion.Position));
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
        if (at != _hovered) { _hovered = at; HintRequested?.Invoke(at is { } over ? ButtonHint(over) : null); }
        if (button.Pressed)
        {
            // A double-click's second press would add a waypoint in the same sector again.
            if (button.DoubleClick) return;
            _pressed = at;
            _pressedInView = false;
            if (at is not null) SoundRequested?.Invoke(AreaGameBarLayout.PressSound);
            if (at == AreaWorldMapArtButton.Compass && !Travelling) { _compassDragged = false; _nextCompass = (long)Time.GetTicksMsec() + AreaWorldMapArtLayout.CompassRepeatMilliseconds; CompassStep(button.Position); }
            else if (at is null && PixelAt(button.Position) is { } pixel)
            {
                _pressedInView = true;
                // WM27: a press in the map view adds a waypoint, on the walkable world.
                if (_kind == AreaWorldMapKind.Map && _map!.OnStartMap && !Travelling) WaypointRequested?.Invoke(new WorldMapPixel(pixel.X, pixel.Y));
            }
        }
        else
        {
            var held = _pressed;
            _pressed = null;
            if (held == AreaWorldMapArtButton.Compass)
            {
                // WM31: a release without a drag centres on the party.
                if (!_compassDragged && !Travelling) Centre();
                _compassDragged = false;
            }
            else if (held is { } pressed && at == pressed)
            {
                SoundRequested?.Invoke(AreaGameBarLayout.ReleaseSound);
                switch (pressed)
                {
                    case AreaWorldMapArtButton.Travel: TravelRequested?.Invoke(); break;
                    case AreaWorldMapArtButton.Zoom: SetKind(AreaWorldMapKind.Map); break;
                    case AreaWorldMapArtButton.Whole: SetKind(AreaWorldMapKind.Continent); break;
                }
            }
            else if (held is null && at is null && _pressedInView && _kind == AreaWorldMapKind.Continent && !Travelling && PixelAt(button.Position) is { } zoomTo)
            {
                // WM34: a click in the continent view zooms in on the clicked point.
                _kind = AreaWorldMapKind.Map;
                _offset = AreaWorldMapArtLayout.CentreOn(zoomTo.X, zoomTo.Y, _max);
                ClearDescription();
            }
            _pressedInView = false;
        }
        Redraw();
    }

    public override void _Notification(int what)
    {
        if (what != NotificationMouseExit) return;
        if (_hovered is not null) SetHovered(null);
        ClearDescription();
    }

    public override void _Draw()
    {
        // The background; the view and the buttons are the children above it.
        if (_look?.Art.TryGetValue(AreaWorldMapArtLayout.BackgroundArt, out var back) == true && back.Length > 0) DrawTexture(back[0], Vector2.Zero);
    }

    // The view, in its own coordinates (the window's minus the view's origin).
    private void DrawView(CanvasItem canvas)
    {
        var view = AreaWorldMapArtLayout.View;
        canvas.DrawRect(new Rect2(0, 0, view.Width, view.Height), Colors.Black);
        if (_look is null) return;
        if (_kind == AreaWorldMapKind.Map) DrawMapView(canvas); else DrawContinentView(canvas);
        // WM42: the cover over the view's bottom edge, drawn last in both views.
        if (_look.Art.TryGetValue(AreaWorldMapArtLayout.CoverArt, out var cover) && cover.Length > 0)
            canvas.DrawTexture(cover[0], new Vector2(AreaWorldMapArtLayout.CoverAt.X - view.X, AreaWorldMapArtLayout.CoverAt.Y - view.Y));
    }

    private void DrawNote(CanvasItem canvas)
    {
        if (_message.Length == 0) return;
        var view = AreaWorldMapArtLayout.View;
        canvas.DrawRect(new Rect2(0, view.Height - 18, view.Width, 18), new Color(0, 0, 0, .7f));
        canvas.DrawString(ThemeDB.FallbackFont, new Vector2(4, view.Height - 5), _message, HorizontalAlignment.Left, view.Width - 8, 12, new Color(1f, .85f, .5f));
    }

    private void DrawMapView(CanvasItem canvas)
    {
        var (first, last, firstRow, lastRow) = AreaWorldMapArtLayout.VisibleTiles(_offset);
        for (var row = firstRow; row <= lastRow; row++)
            for (var column = first; column <= last; column++)
            {
                var index = AreaWorldMapArtLayout.TileColumns * row + column;
                if (index >= _look!.Tiles.Count) continue;
                canvas.DrawTexture(_look.Tiles[index], new Vector2(AreaWorldMapArtLayout.TileSize * column - _offset.X, AreaWorldMapArtLayout.TileSize * row - _offset.Y));
            }
        if (_map is not { } map) return;
        var data = map.Data;
        var colour = _look!.Content.AreaColour;
        // WM15: each marker's icon, then its label.
        foreach (var area in _markers)
        {
            var pixel = WorldTravelGeometry.Pixel(data.TravelAreas[area].Location, data.Width);
            Marker(canvas, AreaWorldMapArtLayout.AreaArt, pixel);
            var worldArea = data.Areas.Areas[area];
            var (x, y) = AreaWorldMapArtLayout.LabelAt(pixel.X, pixel.Y, _offset, (int)worldArea.NameOffsetX, (int)worldArea.NameOffsetY);
            Label(canvas, worldArea.Name, x, y, colour);
        }
        if (!map.OnStartMap) return;
        var route = map.RoutePixels();
        for (var i = 1; i < route.Count; i++) canvas.DrawLine(ViewPoint(route[i - 1]), ViewPoint(route[i]), new Color(.85f, .1f, .1f), 2);
        foreach (var waypoint in map.Waypoints) Marker(canvas, AreaWorldMapArtLayout.WaypointArt, WorldTravelGeometry.Pixel(waypoint.End, data.Width));
        if (LastDiscovered(map) is { } newest) Marker(canvas, AreaWorldMapArtLayout.NewAreaArt, WorldTravelGeometry.Pixel(data.TravelAreas[newest].Location, data.Width));
        Marker(canvas, AreaWorldMapArtLayout.PartyArt, map.Position);
    }

    // WM16, WM54-WM56: the continent picture, a 2 x 2 red dot for each marker (WM17's flaw, only the first eight showing, is not kept), the last
    // discovered area as a 5 x 5 yellow square and the party as a 3 x 3 green one, each from its point at a top-left corner (-1 for the square).
    private void DrawContinentView(CanvasItem canvas)
    {
        if (_map is not { } map) return;
        var view = AreaWorldMapArtLayout.View;
        var data = map.Data;
        Point Dot(WorldMapPixel pixel) { var (x, y) = AreaWorldMapArtLayout.ToZoomed(pixel.X, pixel.Y, data.Width); return new(x - view.X, y - view.Y); }
        if (_look!.Zoomed is { } picture) canvas.DrawTexture(picture, new Vector2(AreaWorldMapArtLayout.Zoomed.X - view.X, AreaWorldMapArtLayout.Zoomed.Y - view.Y));
        foreach (var area in _markers)
        {
            var dot = Dot(WorldTravelGeometry.Pixel(data.TravelAreas[area].Location, data.Width));
            canvas.DrawRect(new Rect2(dot.X, dot.Y, 2, 2), Rgb(AreaWorldMapArtLayout.ZoomedMarkerColour));
        }
        if (!map.OnStartMap) return;
        if (LastDiscovered(map) is { } newest)
        {
            var dot = Dot(WorldTravelGeometry.Pixel(data.TravelAreas[newest].Location, data.Width));
            canvas.DrawRect(new Rect2(dot.X - 1, dot.Y - 1, 5, 5), Rgb(AreaWorldMapArtLayout.ZoomedNewAreaColour));
        }
        var party = Dot(map.Position);
        canvas.DrawRect(new Rect2(party.X, party.Y, 3, 3), Rgb(AreaWorldMapArtLayout.ZoomedPartyColour));
    }

    private readonly record struct Point(int X, int Y);

    private static Color Rgb((byte R, byte G, byte B) colour) => new(colour.R / 255f, colour.G / 255f, colour.B / 255f);

    private static int? LastDiscovered(AreaWorldMap map) =>
        map.Session.QueryLastDiscoveredArea() is > 0 and var area && area < map.Data.TravelAreas.Count ? area : null;

    private Vector2 ViewPoint(WorldMapPixel pixel) => new(pixel.X - _offset.X, pixel.Y - _offset.Y);

    private void Marker(CanvasItem canvas, int art, WorldMapPixel pixel)
    {
        if (_look is null || !_look.Art.TryGetValue(art, out var frames) || frames.Length == 0) return;
        var texture = frames[0];
        canvas.DrawTexture(texture, ViewPoint(pixel) - new Vector2(texture.GetWidth() / 2, texture.GetHeight() / 2));
    }

    // WM47-WM49: the name wrapped at 200 px, the lines that fit in 50 px, over a 1 px shadow in (10, 10, 10).
    private void Label(CanvasItem canvas, string text, int x, int y, (byte R, byte G, byte B) colour)
    {
        if (_look is null || text.Length == 0) return;
        AreaCharacterArtDraw.Lines(canvas, (x + 1, y + 1, AreaWorldMapArtLayout.LabelWidth, AreaWorldMapArtLayout.LabelHeight), text, _look.Font, AreaWorldMapArtLayout.ShadowColour);
        AreaCharacterArtDraw.Lines(canvas, (x, y, AreaWorldMapArtLayout.LabelWidth, AreaWorldMapArtLayout.LabelHeight), text, _look.Font, colour);
    }

    // The buttons over the view (the compass overlaps its bottom edge) and the two readouts in the frame art's black boxes.
    private void DrawChrome(CanvasItem canvas)
    {
        if (_look is null) return;
        foreach (var button in Enum.GetValues<AreaWorldMapArtButton>())
        {
            if (!Shown(button)) continue;
            var (art, x, y) = AreaWorldMapArtLayout.Button(button);
            if (!_look.Art.TryGetValue(art, out var frames) || frames.Length == 0) continue;
            var frame = AreaCharacterArtDraw.Frame(frames.Length, _pressed is not null, _pressed == button, _hovered == button);
            canvas.DrawTexture(frames[Math.Min(frame, frames.Length - 1)], new Vector2(x, y));
        }
        // WM51: each box is filled black and its text written at its corner, in the white of the type 0 font with the shadow.
        if (_readoutPixel is { } pixel && _map is { } map)
        {
            var (west, south) = AreaWorldMapArtLayout.Readouts(pixel.X, pixel.Y, map.Data.Width, _look.Content.Offsets.Longitude, _look.Content.Offsets.Latitude);
            Readout(canvas, AreaWorldMapArtLayout.ReadoutWest, west);
            Readout(canvas, AreaWorldMapArtLayout.ReadoutSouth, south);
        }
    }

    private void Readout(CanvasItem canvas, (int X, int Y, int Width, int Height) box, string text)
    {
        canvas.DrawRect(new Rect2(box.X, box.Y, box.Width, box.Height), Colors.Black);
        AreaArtTextDraw.Text(canvas, _look!.Font, text, box.X + 1, box.Y + 1, AreaWorldMapArtLayout.ShadowColour);
        AreaArtTextDraw.Text(canvas, _look.Font, text, box.X, box.Y, AreaWorldMapArtLayout.ReadoutColour);
    }

    private sealed partial class ViewCanvas : Control
    {
        internal AreaWorldMapArtView? Host { get; init; }
        public override void _Draw() => Host?.DrawView(this);
    }

    private sealed partial class NoteCanvas : Control
    {
        internal AreaWorldMapArtView? Host { get; init; }
        public override void _Draw() => Host?.DrawNote(this);
    }

    private sealed partial class ChromeCanvas : Control
    {
        internal AreaWorldMapArtView? Host { get; init; }
        public override void _Draw() => Host?.DrawChrome(this);
    }
}

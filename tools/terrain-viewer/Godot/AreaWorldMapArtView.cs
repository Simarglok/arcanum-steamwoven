using Arcanum.Simulation.Travel;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>The world-map window's textures and texts: the interface art by number, the 64 map tiles (column + 8 × row), the continent picture, the
/// markers' font and the texts of <c>mes/worldmap.mes</c> (ARC-UI-083).</summary>
public sealed record AreaWorldMapArtLook(IReadOnlyDictionary<int, Texture2D[]> Art, IReadOnlyList<Texture2D> Tiles, Texture2D? Zoomed, AreaArtFontFace Font,
    AreaWorldMapArtContent Content);

/// <summary>ARC-UI-084: the town map the window shows (kind 2): its files, its plan, the party's location and which tiles are revealed. A whole map
/// (<c>[w:1]</c> of the list, WM77) shows every tile plainly; otherwise <see cref="CellRevealed"/> says which tile bits are set (the session's
/// reveal, ARC-WORLD-122; a town revealed whole by a script has them all).</summary>
/// <summary>ARC-UI-092: <see cref="Waypoints"/> is the town's waypoint list, owned by the host (it outlives the window, WM89) and <see cref="PathExists"/>
/// the path test of a new waypoint from a start location to a target location (WM105, WM106).</summary>
/// <summary>ARC-UI-093: <see cref="Followers"/> gives the locations of the PC's followers at each draw (WM81: they are drawn where they stand now, the party marker
/// where the PC stood when the town map was loaded); null shows none. ARC-UI-103: <see cref="Notes"/> is the town's note list, owned by the host (WM79, WM127).</summary>
public sealed record AreaTownMapShow(AreaTownMapSource Source, AreaTownMapPlan Plan, (long X, long Y) PartyLocation, Func<int, bool> CellRevealed,
    AreaTownWaypoints Waypoints, Func<(long X, long Y), (long X, long Y), bool> PathExists, Func<IReadOnlyList<(long X, long Y)>>? Followers = null,
    AreaTownNotes? Notes = null);

/// <summary>
/// ARC-UI-073, ARC-UI-083: the world-map window in the original's look (<see cref="AreaWorldMapArtLayout"/>, ledger WM1–WM64): art 138 behind the
/// 501 × 365 view, which is either the map view (the 2000 × 2000 picture, scrolled by the arrow keys, the compass and Home, WM18, WM31) or, by the
/// zoom and whole-map buttons and Z, the continent view (<c>Map_Zoomed</c> with a red dot for every marker, WM53, WM54); a click in the map view
/// adds a waypoint, a click in the continent view zooms in on the clicked point (WM34). ARC-UI-084: a PC in a sector with a town map opens the town
/// map (kind 2, WM65–WM82): its tiles at the scroll offset, the unrevealed ones black or faded beside revealed ones, the party marker on top; the
/// zoom button and Z bring it back from the other views. Over the picture are the markers with the areas' names in
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
    // ARC-UI-084: the town map (kind 2) with its own scroll offset (each kind keeps its own, WM62), the tile textures read so far and which tile files exist.
    private AreaTownMapShow? _town;
    private (int X, int Y) _townOffset, _townMax;
    private (int X, int Y) _townParty;
    private readonly Dictionary<int, Texture2D?> _townTextures = [];
    private readonly Dictionary<int, bool> _townFiles = [];
    private const int MaxTownTextures = 6000;
    private AreaWorldMapKind _kind;
    // ARC-MAGIC-1147 (TP6, WM2): the window was opened by a Teleportation cast (0x560790): no journey, so no Travel button.
    private bool _teleportMode;
    private AreaWorldMapArtButton? _hovered, _pressed;
    private bool _compassDragged, _pressedInView;
    // ARC-UI-085, WM101/WM103: the armed flag (a press adds or selects a waypoint only while it is set; it is set at the open, on a kind switch and on every
    // left release, cleared by a press that adds or hits) and the drag state (state 1: the view scrolls near its edges until the left release).
    private bool _armed, _dragging, _townArmed;
    private long _lastPoll, _nextCompass, _nextDrag, _nextMotionScroll;
    private Vector2 _pointer;
    private (long X, long Y)? _readoutPixel;
    private int _hoverArea;
    // The areas that carry a marker, in the original's order: the known ones from the last down to 1 at the open, later finds after them (WM11).
    private readonly List<int> _markers = [];
    private Control _canvas = null!, _chrome = null!, _note = null!;
    // ARC-UI-106 (WM30, WM92; Steamwoven rules): the note quill, tool state 3 of the town map, and the text entry it opens (a new note at a picture pixel, or the note at
    // _editIndex). ARC-UI-2306 (BM4): the entry runs on the bottom strip (bar mode 6, the host's AreaMapNoteEntryView); this view holds what the note needs: the text the entry
    // opened with (for the Cancel button and for leaving), the text it has now (the host mirrors it), and the marker type a new note was made with.
    private bool _quill, _noteEntry;
    private int _editIndex = -1;
    private (int X, int Y) _editPixel;
    private string _editSaved = "", _editText = "";
    private int _editType = AreaTownNotes.QuestionType;
    // ARC-UI-110: the notes of the map view and of the continent view, lists owned by the host like the town's (null shows none and keeps the quill out of the view).
    private AreaTownNotes? _mapNotes, _continentNotes;
    // The note under the pointer in the map and continent views (an area marker, when one is under it, wins), -1 for none.
    private int _hoverNote = -1;

    /// <summary>A left press in the view, as the picture pixel of the executable (WM25), when the world is walkable.</summary>
    public event Action<WorldMapPixel>? WaypointRequested;
    /// <summary>A right release in the map view (WM98): the selected waypoint, or the last, is to go.</summary>
    public event Action? RemoveRequested;
    /// <summary>ARC-UI-092: a town waypoint was refused (message 610 or 611, WM105); the silent cap of 30 raises nothing.</summary>
    public event Action<AreaTownWaypointRefusal>? TownWaypointRefused;
    /// <summary>ARC-UI-092: the Travel button in the town map with waypoints (WM89): the PC walks along them and the window closes.</summary>
    public event Action? TownTravelRequested;
    public event Action? TravelRequested;
    /// <summary>An interface sound (3000/3001 an art button's press and release).</summary>
    public event Action<int>? SoundRequested;
    /// <summary>A hovered marker's description for the message window, null when the pointer left it (WM59, WM60).</summary>
    public event Action<string?>? DescriptionRequested;
    /// <summary>ARC-UI-106: the quill changed the notes of the town (its number and the list), to be kept by the session.</summary>
    public event Action<int, AreaTownNotes>? TownNotesChanged;
    /// <summary>ARC-UI-110: the quill changed the notes of the map view or of the continent view (which one and the list), to be kept by the session.</summary>
    public event Action<AreaWorldMapKind, AreaTownNotes>? WorldNotesChanged;
    /// <summary>A hovered button's hint for the message window ("Action", a new line and the button's text, WM61), null when the pointer left it.</summary>
    public event Action<string?>? HintRequested;
    /// <summary>ARC-UI-133 (WM36): a left release in the PC lens asks the app to close the window, as the key W does.</summary>
    public event Action? CloseRequested;
    /// <summary>Asked before the arrow poll; false while another window owns the keys.</summary>
    public Func<bool>? PollGate { get; set; }

    public (int X, int Y) Offset => _offset;
    public (int X, int Y) MaxOffset => _max;
    public AreaWorldMapKind Kind => _kind;
    public (int X, int Y) TownOffset => _townOffset;
    public (int X, int Y) TownMaxOffset => _townMax;
    public (int X, int Y) TownParty => _townParty;
    public bool HasTown => _town is not null;
    internal AreaTownMapPlan? TownPlanForSelfTest => _town?.Plan;
    internal int TownTexturesForSelfTest => _townTextures.Count(pair => pair.Value is not null);
    /// <summary>Swaps the town's reveal (a whole map or the set of revealed tile bits) for a check of the fog's drawing; the view redraws.</summary>
    internal void TownRevealForSelfTest(bool whole, Func<int, bool> revealed)
    {
        if (_town is not { } town) return;
        _town = town with { Plan = town.Plan with { Entry = town.Plan.Entry with { Whole = whole } }, CellRevealed = revealed };
        Redraw();
    }
    internal string MessageForSelfTest => _message;
    internal (string West, string South)? ReadoutForSelfTest => _readoutPixel is { } pixel && _map is { } map
        ? AreaWorldMapArtLayout.Readouts(pixel.X, pixel.Y, map.Data.Width, _look!.Content.Offsets.Longitude, _look.Content.Offsets.Latitude) : null;
    internal IReadOnlyList<int> MarkersForSelfTest => _markers;
    /// <summary>The pointer at a window point of the 800 × 400 window, as a mouse move gives it (the readouts and the hover text follow).</summary>
    internal void PointerForSelfTest(Vector2 windowPoint) { _pointer = windowPoint; SetHovered(ButtonAt(windowPoint)); PointerOverView(windowPoint); }
    internal AreaTownNotes? TownNotesForSelfTest => _town?.Notes;
    internal AreaTownNotes? NotesForSelfTest(AreaWorldMapKind kind) => kind == AreaWorldMapKind.Map ? _mapNotes : kind == AreaWorldMapKind.Continent ? _continentNotes : _town?.Notes;
    internal int HoverWorldNoteForSelfTest => _hoverNote;
    /// <summary>ARC-UI-106: the quill is the tool (town map only); its cursor is art 21 (WM30).</summary>
    public bool QuillActive => _quill;
    /// <summary>ARC-UI-106: the note text entry has the keyboard (typed keys go to it, not to the window's keys).</summary>
    public bool EditingNote => _noteEntry;
    /// <summary>ARC-UI-2306: the text the open entry opened with (the entry's Cancel restores it).</summary>
    public string NoteEntryOpenedText => _editSaved;
    /// <summary>ARC-UI-2306: the host mirrors the entry's text here, so that leaving by the map closing or the Quill ending keeps what was typed (0x564070 leaves live edits).</summary>
    public void SetNoteText(string text) { if (_noteEntry) _editText = text; }
    /// <summary>ARC-UI-2306 (0x66D89C): the radio group's marker type, taken by a note made from now on.</summary>
    public int NoteMarkerType { get; set; }
    /// <summary>ARC-UI-2306: whether the bottom strip can show the entry (the bars, art 200 and the font read). Without it a press starts no note, and says so.</summary>
    public bool NoteEntryAvailable { get; set; }
    internal string NoteEditTextForSelfTest { get => _editText; set => _editText = value; }
    internal int NoteEditIndexForSelfTest => _editIndex;
    internal int NoteEditTypeForSelfTest => _editType;
    internal int HoverNoteForSelfTest => _kind == AreaWorldMapKind.Town ? _hoverArea : -1;
    internal bool ArmedForSelfTest => _armed;
    internal bool DraggingForSelfTest => _dragging;
    /// <summary>A left press or release at a window point, as the mouse gives it.</summary>
    internal void LeftForSelfTest(Vector2 windowPoint, bool pressed) =>
        MouseLeft(new InputEventMouseButton { Position = windowPoint, GlobalPosition = windowPoint, Pressed = pressed, ButtonIndex = MouseButton.Left });
    internal void RightReleaseForSelfTest() => RightRelease();
    /// <summary>The screen point of a point of the 800 × 400 window, as the pointer gives it (the window is scaled and placed between the bars).</summary>
    internal Vector2 WindowPointForSelfTest(double x, double y) => GetGlobalTransform() * new Vector2((float)x, (float)y);
    /// <summary>A mouse move inside the view during a drag (the edge scroll of WM99).</summary>
    internal void DragForSelfTest(Vector2 windowPoint) { _pointer = windowPoint; if (_dragging) DragEdgeScroll(windowPoint); }
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
        // The art text draw needs the premultiplied material. The Legacy art has only opaque or clear pixels, which it draws the same; an HD redraw has soft edges
        // and colour under clear pixels, so every texture drawn on these two canvases is the premultiplied variant (ARC-UI-126; only the background, drawn by this node, is not).
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

    public override void _ExitTree()
    {
        AreaGameBarPanel.InsetsChanged -= Place;
        if (_onTextureChanged is { } handler) foreach (var texture in _watched) texture.Changed -= handler;
        _watched.Clear();
    }

    public void SetLook(AreaWorldMapArtLook look)
    {
        // ARC-UI-1197: the textures of a look this view no longer shows are let go.
        if (_onTextureChanged is { } handler) { foreach (var texture in _watched) texture.Changed -= handler; _watched.Clear(); }
        _look = look ?? throw new ArgumentNullException(nameof(look));
    }

    public bool HasLook => _look is not null;

    /// <summary>Shows the window over <paramref name="map"/> in the map view, centred on the party (WM12; off the start map, which only a debug teleport
    /// reaches, on the picture's middle).</summary>
    public void Open(AreaWorldMap map, Func<int, bool> known, AreaTownMapShow? town = null, AreaTownNotes? mapNotes = null, AreaTownNotes? continentNotes = null, bool teleportMode = false)
    {
        if (_look is null) throw new InvalidOperationException("ARC-UI-073: the world-map art is not set");
        _map = map; _known = known; _message = ""; _teleportMode = teleportMode;
        _max = AreaWorldMapArtLayout.MaxOffset(map.Data.Width, map.Data.Width);
        (_hovered, _pressed, _compassDragged, _pressedInView) = (null, null, false, false);
        (_armed, _dragging) = (map.OnStartMap, false);
        _kind = AreaWorldMapKind.Map; _readoutPixel = null; _hoverArea = _hoverNote = -1;
        (_mapNotes, _continentNotes) = (mapNotes, continentNotes);
        _lastPoll = (long)Time.GetTicksMsec();
        _markers.Clear();
        SyncMarkers();
        Centre();
        // WM6, WM72: a PC in a town-map sector opens the town map, centred on the party pixel computed now (it does not follow the party while open).
        SetTown(town);
        if (town is not null) { _kind = AreaWorldMapKind.Town; Centre(); }
        Visible = true;
        Place();
        Redraw();
    }

    // WM72, WM75: each load of a town starts afresh (the original leaks the earlier tile buffers; the viewer frees its textures).
    private void SetTown(AreaTownMapShow? town)
    {
        foreach (var texture in _townTextures.Values) texture?.Dispose();
        _townTextures.Clear(); _townFiles.Clear();
        EndQuill();
        _town = town;
        if (town is null) { _townOffset = _townMax = _townParty = default; return; }
        _townMax = AreaTownMapLayout.MaxOffset(town.Plan);
        _townParty = AreaTownMapLayout.ToPixel(town.Plan.Info, town.PartyLocation.X, town.PartyLocation.Y);
        _townOffset = default;
        // WM86: the original's town flag starts clear, so the first press of a session is dead (a technical flaw: fixed in both rule modes, the window opens armed).
        _townArmed = true;
    }

    public void CloseView()
    {
        // The message window shows nothing of the window's once it is gone.
        ClearDescription();
        EndQuill();
        if (_hovered is not null) SetHovered(null);
        Visible = false;
        _map = null; _known = null;
        (_hovered, _pressed) = (null, null);
        _markers.Clear();
        _hoverArea = -1;
        SetTown(null);
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
        if (_kind == AreaWorldMapKind.Town)
        {
            _townOffset = AreaWorldMapArtLayout.CentreOn(_townParty.X, _townParty.Y, _townMax);
            Redraw();
            return;
        }
        var at = map.OnStartMap ? map.Position : new WorldMapPixel(map.Data.Width / 2, map.Data.Width / 2);
        _offset = AreaWorldMapArtLayout.CentreOn(at.X, at.Y, _max);
        Redraw();
    }

    /// <summary>Z (WM22): from the map view or the town map to the continent view, from the continent view to the town map in a town-map sector and else
    /// to the map view; not while travelling. True when the kind changed.</summary>
    public bool ToggleKind() => SetKind(_kind != AreaWorldMapKind.Continent ? AreaWorldMapKind.Continent : _town is not null ? AreaWorldMapKind.Town : AreaWorldMapKind.Map);

    /// <summary>Button 811 (WM32): the town map in a town-map sector, else the map view.</summary>
    public bool ShowMapOrTown() => SetKind(_town is not null ? AreaWorldMapKind.Town : AreaWorldMapKind.Map);

    /// <summary>Switches the view kind (the buttons 811 and 812, Z, WM62); each kind keeps its own offset and nothing is re-centred. The executable's
    /// buttons also work during a journey and then end it (WM63, a possible gameplay flaw whose effect is F1); the viewer guards them as Z is guarded.</summary>
    public bool SetKind(AreaWorldMapKind kind)
    {
        // WM62: the continent view needs its picture (the executable refuses the switch without it).
        if (_map is null || Travelling || kind == _kind || (kind == AreaWorldMapKind.Continent && _look?.Zoomed is null)
            || (kind == AreaWorldMapKind.Town && _town is null)) return false;
        EndQuill();
        _kind = kind;
        // WM103: a kind switch enters the tool state 0, which re-arms.
        (_armed, _dragging, _townArmed) = (_map.OnStartMap, false, true);
        ClearDescription();
        Redraw();
        return true;
    }

    /// <summary>One scroll step in the executable's direction code (0 up, clockwise to 7 up-left); true when the view moved. The continent view does not scroll.</summary>
    public bool ScrollBy(int direction, int step)
    {
        if (_kind == AreaWorldMapKind.Town)
        {
            var nextTown = AreaWorldMapArtLayout.Scroll(_townOffset, direction, step, _townMax);
            if (nextTown == _townOffset) return false;
            _townOffset = nextTown;
            Redraw();
            return true;
        }
        if (_kind != AreaWorldMapKind.Map) return false;
        var next = AreaWorldMapArtLayout.Scroll(_offset, direction, step, _max);
        if (next == _offset) return false;
        _offset = next;
        Redraw();
        return true;
    }

    private bool Travelling => _map?.Travelling == true;

    // ARC-UI-134 (WM141): the lens state the chrome was last drawn with.
    private bool _lensFrozenDrawn;

    /// <summary>ARC-UI-134 (WM141): the lens shows no game view while a journey runs: the executable's travel state (0x5615D0(2)) turns the view-window mode to its
    /// second kind (0x550DA0(2)), which fills the lens's rectangle black and draws art 198 over it; the end of the journey turns the live view on again.</summary>
    internal bool LensFrozen => Travelling;

    /// <summary>ARC-UI-134: art 198 read and is drawn over the lens's square.</summary>
    internal bool HasLensArt => _look?.Art.TryGetValue(AreaWorldMapArtLayout.LensArt, out var lens) == true && lens.Length > 0;

    /// <summary>ARC-UI-134: the lens's rectangle on the screen (the window's rectangle scaled and moved), the round view's square.</summary>
    internal (Vector2 Origin, float Side) LensOnScreen
    {
        get
        {
            var (x, y, side) = AreaWorldMapArtLayout.Lens;
            return (GlobalPosition + new Vector2(x, y) * Scale.X, side * Scale.X);
        }
    }

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
        RedrawOnScaleChange();
    }

    // ARC-UI-1197: a change of the window's scale moves no pixel by itself (the canvas item is only stretched), so the frame is drawn again at the new device scale.
    private float _drawnScale;

    private void RedrawOnScaleChange()
    {
        var device = DeviceScale();
        if (Math.Abs(device - _drawnScale) < 0.0005f) return;
        _drawnScale = device;
        Redraw();
    }

    // ARC-UI-1197: the generated interface paints the window's frame at the window's own device scale (the frame scale times the canvas transform's and the viewport's stretch,
    // the bars' own rule). The view claims its look's textures at its draw; a texture swapped in by a worker redraws the window.
    private readonly HashSet<HdInterfaceTexture> _watched = [];
    private Action? _onTextureChanged;
    private Func<float>? _deviceScale;

    internal float DeviceScale() =>
        GodotObject.IsInstanceValid(this) && IsInsideTree() ? GetGlobalTransformWithCanvas().Scale.X * GetViewport().GetStretchTransform().Scale.X : 1f;

    private void ClaimScale()
    {
        if (_look is null) return;
        _deviceScale ??= DeviceScale;
        _onTextureChanged ??= () => { if (GodotObject.IsInstanceValid(this)) Redraw(); };
        foreach (var frames in _look.Art.Values)
            foreach (var texture in frames)
            {
                if (texture is not HdInterfaceTexture hd) continue;
                hd.ScaleSource = _deviceScale;
                if (_watched.Add(hd)) hd.Changed += _onTextureChanged;
            }
    }

    /// <summary>ARC-UI-1197: the textures of the given art numbers that the window holds, every frame, with the art number of each (the native smoke).</summary>
    internal IReadOnlyList<(int Art, HdInterfaceTexture Texture)> TexturesForSelfTest(IEnumerable<int> arts) =>
        _look is null ? [] : [.. arts.Where(_look.Art.ContainsKey).SelectMany(number => _look.Art[number].OfType<HdInterfaceTexture>().Select(texture => (number, texture)))];

    public override void _Process(double delta)
    {
        // ARC-UI-134 (WM141): the lens goes black when the journey starts and comes back when it ends, whoever changed the state.
        if (Visible && Travelling != _lensFrozenDrawn) { _lensFrozenDrawn = Travelling; _chrome?.QueueRedraw(); }
        if (!Visible || _map is null || Travelling || EditingNote || PollGate?.Invoke() == false) return;
        var now = (long)Time.GetTicksMsec();
        // WM18: the poll runs at most once for 10 ms; left beats right and up beats down.
        if (now - _lastPoll >= AreaWorldMapArtLayout.PollMilliseconds)
        {
            var direction = AreaWorldMapArtLayout.Direction(Godot.Input.IsKeyPressed(Key.Left), Godot.Input.IsKeyPressed(Key.Right), Godot.Input.IsKeyPressed(Key.Up), Godot.Input.IsKeyPressed(Key.Down));
            _lastPoll = now;
            if (direction >= 0) ScrollBy(direction, AreaWorldMapArtLayout.KeyStep);
        }
        // PROJECT: a release that never reached the view (focus lost, the menu opened) must not leave the drag state on.
        if (_dragging && !Godot.Input.IsMouseButtonPressed(MouseButton.Left)) EndDrag();
        // WM99: a held press in the drag state repeats every 250 ms like the compass.
        if (_dragging && now >= _nextDrag)
        {
            _nextDrag = now + AreaWorldMapArtLayout.CompassRepeatMilliseconds;
            DragEdgeScroll(_pointer);
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

    internal bool TravelButtonShownForSelfTest => Shown(AreaWorldMapArtButton.Travel);

    // The buttons shown now: Travel only on the walkable map view, the compass not in the continent view (WM31, WM62).
    private bool Shown(AreaWorldMapArtButton button) => button switch
    {
        AreaWorldMapArtButton.Travel => AreaWorldMapArtLayout.TravelShown(_map?.OnStartMap == true, _teleportMode, _kind),
        AreaWorldMapArtButton.Compass => _kind != AreaWorldMapKind.Continent,
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
        : _kind == AreaWorldMapKind.Map ? AreaWorldMapArtLayout.ToPicture(at.X, at.Y, _offset)
        : _kind == AreaWorldMapKind.Town ? AreaTownMapLayout.ToPicture(at.X, at.Y, _townOffset) : AreaWorldMapArtLayout.ZoomedToPicture(at.X, at.Y, _map.Data.Width);

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
        if (over is not null || (_hoverArea < 0 && _hoverNote < 0)) HintRequested?.Invoke(over is { } button ? ButtonHint(button) : null);
        Redraw();
    }

    // WM52, WM58, WM59: the readouts follow the pointer over the view; a marker under it (the newest wins) shows its description, in the map view only
    // while the world is not travelling.
    private void PointerOverView(Vector2 at)
    {
        var pixel = PixelAt(at);
        // WM52, WM59: the town map writes no readouts; a note under the pointer (the newest wins) shows its text in the message window.
        if (_kind == AreaWorldMapKind.Town)
        {
            var noteHit = -1;
            // WM59: the town map's marker hover works in tool state 0 only, not with the quill.
            if (!_quill && pixel is { } over && _town?.Notes is { } notes) noteHit = notes.Hit(over.X, over.Y);
            if (noteHit == _hoverArea) return;
            _hoverArea = noteHit;
            DescriptionRequested?.Invoke(noteHit < 0 ? null : _town!.Notes!.Items[noteHit].Text);
            return;
        }
        if (pixel is { } found)
        {
            _readoutPixel = found;
            _chrome.QueueRedraw();
        }
        var hit = -1;
        var worldNote = -1;
        // ARC-UI-110: as in the town map (WM59) the hover works in tool state 0 only, not with the quill.
        if (!_quill && pixel is { } p && _map is not null && (_kind == AreaWorldMapKind.Continent || !Travelling))
        {
            var half = _kind == AreaWorldMapKind.Map ? AreaWorldMapArtLayout.HoverHalf : AreaWorldMapArtLayout.ZoomedHoverHalf;
            var index = AreaWorldMapArtLayout.HitMarker(MarkerPixels(), p.X, p.Y, half);
            hit = index < 0 ? -1 : _markers[index];
            // A note shows its text where no area marker is under the pointer (the newest note wins an overlap, WM58).
            if (hit < 0 && ActiveNotes is { } notes) worldNote = notes.Hit(p.X, p.Y, NoteHitHalf);
        }
        if (hit == _hoverArea && worldNote == _hoverNote) return;
        (_hoverArea, _hoverNote) = (hit, worldNote);
        DescriptionRequested?.Invoke(hit >= 0 ? _map!.Data.Areas.Areas[hit].Description : worldNote >= 0 ? ActiveNotes!.Items[worldNote].Text : null);
    }

    private void ClearDescription()
    {
        if (_hoverArea < 0 && _hoverNote < 0) return;
        _hoverArea = _hoverNote = -1;
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
                // WM99: a mouse move inside the view during a drag scrolls it near the edges.
                // PROJECT: at most once per poll interval x5 (50 ms); the executable's move records come at its own input rate (not read, F0).
                if (_dragging && (long)Time.GetTicksMsec() is var tick && tick >= _nextMotionScroll) { _nextMotionScroll = tick + 50; DragEdgeScroll(motion.Position); }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false } right:
                _pointer = right.Position;
                RightRelease();
                AcceptEvent();
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
                if (_quill) QuillPress(pixel);
                else if (_kind == AreaWorldMapKind.Map && _map!.OnStartMap && !Travelling && _armed) PressWaypoint(pixel);
                else if (_kind == AreaWorldMapKind.Town && _townArmed) PressTownWaypoint(pixel);
            }
        }
        else
        {
            var held = _pressed;
            _pressed = null;
            // WM86: every left release re-arms the town flag (also one that ends a press made in the town map after a kind switch).
            if (_town is not null) _townArmed = true;
            // WM100: every left release in the map view re-arms; one that ends a drag snaps the selected waypoint to a known area near it.
            if (_kind == AreaWorldMapKind.Map)
            {
                _armed = _map!.OnStartMap;
                if (_dragging) EndDrag();
            }
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
                    // WM89: the town map's Travel walks along the town waypoints, and does nothing without one.
                    case AreaWorldMapArtButton.Travel:
                        EndQuill();
                        if (_kind != AreaWorldMapKind.Town) TravelRequested?.Invoke();
                        else if (_town is { Waypoints.Count: > 0 }) TownTravelRequested?.Invoke();
                        break;
                    case AreaWorldMapArtButton.Zoom: ShowMapOrTown(); break;
                    case AreaWorldMapArtButton.Whole: SetKind(AreaWorldMapKind.Continent); break;
                }
            }
            else if (held is null && at is null && _pressedInView && _kind == AreaWorldMapKind.Continent && !_quill && !Travelling && _town is null && PixelAt(button.Position) is { } zoomTo)
            {
                // WM34: a click in the continent view zooms in on the clicked point (not in a town-map sector).
                _kind = AreaWorldMapKind.Map;
                _offset = AreaWorldMapArtLayout.CentreOn(zoomTo.X, zoomTo.Y, _max);
                ClearDescription();
            }
            _pressedInView = false;
            // ARC-UI-133 (WM36, 0x561DF5): every left release ends in the lens test (0x551000) after the kind's own work, in every kind and tool state; inside the
            // lens the window closes (0x560F40, the close of the key W). The close frees this view's state, so nothing runs after it.
            if (AreaWorldMapArtLayout.LensContains(button.Position.X, button.Position.Y))
            {
                Redraw();
                CloseRequested?.Invoke();
                return;
            }
        }
        Redraw();
    }

    // WM100: the end of the drag: the selected waypoint takes the snap of a known area near it.
    private void EndDrag()
    {
        _dragging = false; _armed = _map?.OnStartMap == true;
        _map?.ReleaseSelected();
        Redraw();
    }

    // WM98, WM88: the right release deletes the selected waypoint (or the last), wherever it is, from the list of the kind.
    private void RightRelease()
    {
        // WM98: in the quill state the right release only leaves it (an open text entry is cancelled with it).
        if (_quill) { EndQuill(); Redraw(); return; }
        // ARC-UI-105: the right release acts in the continent view too (WM98: anywhere, the map view's list, no test of the view).
        if (_map is { OnStartMap: true } && _kind is AreaWorldMapKind.Map or AreaWorldMapKind.Continent) RemoveRequested?.Invoke();
        else if (_kind == AreaWorldMapKind.Town && _town is { } town)
        {
            _message = "";
            town.Waypoints.Remove();
            Redraw();
        }
    }

    /// <summary>Delete (WM23, WM88): the selected waypoint of the kind goes, or the last. In the quill state Delete leaves it.</summary>
    public void RemoveSelected() => RightRelease();

    /// <summary>The list of notes of the view being shown: the town's, or (ARC-UI-110) the map view's or the continent view's.</summary>
    private AreaTownNotes? ActiveNotes => _kind switch
    {
        AreaWorldMapKind.Town => _town?.Notes,
        AreaWorldMapKind.Map => _mapNotes,
        _ => _continentNotes,
    };

    /// <summary>
    /// ARC-UI-106, ARC-UI-110 (Steamwoven rules; WM30, WM92, WM126): Q enters or leaves the note quill in the town map, the map view and the continent view. The retail executable
    /// has the quill's press handler but nothing that enters its state (WM126), so the control is the project's. False without a list of notes for the view, while a journey
    /// runs in the map or continent view, or while a text entry is open.
    /// </summary>
    public bool ToggleQuill()
    {
        if (_map is null || ActiveNotes is null || EditingNote) return false;
        // Leaving is always allowed; entering is not while a journey runs in the map or continent view.
        if (!_quill && _kind != AreaWorldMapKind.Town && Travelling) return false;
        _quill = !_quill;
        // WM103: entering the quill state clears the waypoint selection; the lists keep their waypoints.
        if (_quill)
        {
            if (_kind == AreaWorldMapKind.Town) _town!.Waypoints.Deselect(); else _map.Deselect();
            ClearDescription();
        }
        Redraw();
        return true;
    }

    /// <summary>Leaves the quill (a journey starting by Enter or the confirm pulse does it as the Travel button does).</summary>
    public void LeaveQuill()
    {
        if (!_quill && !EditingNote) return;
        EndQuill();
        Redraw();
    }

    private void EndQuill()
    {
        CancelNoteEdit();
        _quill = false;
    }

    // WM30, WM92: a press on a note opens its text, elsewhere a new note is started at the pixel.
    private void QuillPress((long X, long Y) pixel)
    {
        if (ActiveNotes is not { } notes || EditingNote || (_kind != AreaWorldMapKind.Town && Travelling)) return;
        // The picture of the view: the town's own, or the world map's square.
        var (pictureWidth, pictureHeight) = _kind == AreaWorldMapKind.Town ? (_town!.Plan.PictureWidth, _town.Plan.PictureHeight) : (_map!.Data.Width, _map.Data.Width);
        if (pixel.X < 0 || pixel.Y < 0 || pixel.X >= pictureWidth || pixel.Y >= pictureHeight) return;
        var hit = notes.Hit(pixel.X, pixel.Y, NoteHitHalf);
        // 0x564360: a note with flag 2 (the ones a scenery marked) is not edited.
        if (hit >= 0 && !notes.IsEditable(hit)) { _message = "This note cannot be edited."; Redraw(); return; }
        if (hit < 0 && notes.IsFull) { _message = "No more notes fit on this map."; Redraw(); return; }
        if (!NoteEntryAvailable) { _message = "The note entry needs the bottom strip (art 200)."; Redraw(); return; }
        _message = "";
        _editIndex = hit;
        _editPixel = ((int)pixel.X, (int)pixel.Y);
        // 0x563F90: a new note takes the radio group's marker now; an edit keeps its own type (the accept never writes it).
        _editType = hit < 0 ? NoteMarkerType : notes.Items[hit].Type;
        _editSaved = _editText = hit < 0 ? "" : notes.Items[hit].Text;
        _noteEntry = true;
        Redraw();
    }

    // The hit box of a note (WM58's marker box): ±9 picture pixels in the town and map views, where the art is drawn 1:1. In the continent view the picture is shrunk
    // (365 px for the whole width) and art 143 is drawn at its full size, so the box is at least the marker's ±20 and at least the drawn art's half size in picture pixels.
    private int NoteHitHalf
    {
        get
        {
            if (_kind != AreaWorldMapKind.Continent) return AreaWorldMapArtLayout.HoverHalf;
            var artHalf = _look?.Art.TryGetValue(AreaWorldMapArtLayout.NoteArt, out var frames) == true && frames.Length > 0 ? Math.Max(frames[0].GetWidth(), frames[0].GetHeight()) / 2 : 0;
            return Math.Max(AreaWorldMapArtLayout.ZoomedHoverHalf, (int)(artHalf * (_map?.Data.Width ?? 0) / AreaWorldMapArtLayout.ZoomedSize));
        }
    }

    /// <summary>Enter or Done in the entry (0x5640C0): a text with something in it adds the note (with the marker the radio group gave when it was made) or keeps the edited
    /// one, an empty text deletes the edited note (0x564210) and adds none, and the entry ends (the original leaves the strip in mode 6, unbound; what closes it is not read, F0).</summary>
    public void AcceptNoteEdit()
    {
        if (!EditingNote || ActiveNotes is not { } notes) return;
        var text = _editText;
        var changed = _editIndex < 0 ? notes.AddByQuill(_editPixel.X, _editPixel.Y, text, _editType) : notes.EditByQuill(_editIndex, text);
        FinishNoteEdit(notes, changed);
    }

    /// <summary>Leaving the entry without accepting (Escape, the quill left, the window closing; 0x564070): a new note is dropped, and an edited note keeps what was typed, as the
    /// original's entry works on the note's own text. A text with nothing in it keeps the text the entry opened with instead (the session stores no empty note).</summary>
    public void CancelNoteEdit()
    {
        if (!EditingNote || ActiveNotes is not { } notes) { _noteEntry = false; return; }
        var changed = _editIndex >= 0 && AreaTownNotes.IsNoteText(_editText) && notes.EditByQuill(_editIndex, _editText);
        FinishNoteEdit(notes, changed);
    }

    private void FinishNoteEdit(AreaTownNotes notes, bool changed)
    {
        CloseNoteEdit();
        if (changed)
        {
            if (_kind == AreaWorldMapKind.Town) TownNotesChanged?.Invoke(_town!.Plan.Entry.Number, notes);
            else WorldNotesChanged?.Invoke(_kind, notes);
        }
        Redraw();
    }

    private void CloseNoteEdit()
    {
        _noteEntry = false;
        _editIndex = -1;
        _editSaved = _editText = "";
    }

    // WM85, WM87: an armed press on a town waypoint selects it, else adds one (message 611 at 6, 610 without a path); both disarm. There is no drag in the town map.
    private void PressTownWaypoint((long X, long Y) pixel)
    {
        if (_town is not { } town) return;
        _message = "";
        var hit = -1;
        if (_look?.Art.TryGetValue(AreaWorldMapArtLayout.AreaArt, out var frames) == true && frames.Length > 0)
            hit = AreaWorldMapArtLayout.HitWaypoint([.. town.Waypoints.Items.Select(w => w.Pixel)], pixel.X, pixel.Y, frames[0].GetWidth(), frames[0].GetHeight());
        if (hit >= 0) { town.Waypoints.Select(hit); _townArmed = false; return; }
        var refusal = town.Waypoints.Add(pixel, AreaTownMapLayout.ToLocation(town.Plan.Info, pixel.X, pixel.Y), town.PartyLocation, town.PathExists);
        if (refusal == AreaTownWaypointRefusal.None) _townArmed = false;
        else if (refusal != AreaTownWaypointRefusal.SharedCap)
        {
            // WM87, WM105: message 611 (the sixth waypoint is full) or 610 of WorldMap.mes; the text is the player's own file's, the fallback wording is the project's.
            _message = refusal == AreaTownWaypointRefusal.TownCap ? _look?.Content.Text(611, "No more waypoints can be set here.") ?? ""
                : _look?.Content.Text(610, "No path leads there. Try a point nearer the previous waypoint.") ?? "";
            TownWaypointRefused?.Invoke(refusal);
            Redraw();
        }
    }

    // WM27, WM101, WM102: an armed press on a waypoint selects it, else it adds one at the pixel (a refused add changes nothing); both start the drag and disarm.
    private void PressWaypoint((long X, long Y) pixel)
    {
        var map = _map!;
        var hit = -1;
        if (_look?.Art.TryGetValue(AreaWorldMapArtLayout.AreaArt, out var frames) == true && frames.Length > 0)
            hit = AreaWorldMapArtLayout.HitWaypoint([.. map.Waypoints.Select(w => (w.Pixel.X, w.Pixel.Y))], pixel.X, pixel.Y, frames[0].GetWidth(), frames[0].GetHeight());
        if (hit >= 0) map.Select(hit);
        else
        {
            var before = map.Waypoints.Count;
            WaypointRequested?.Invoke(new WorldMapPixel(pixel.X, pixel.Y));
            if (map.Waypoints.Count <= before) return;
        }
        (_armed, _dragging) = (false, true);
        _nextDrag = (long)Time.GetTicksMsec() + AreaWorldMapArtLayout.CompassRepeatMilliseconds;
    }

    // WM99: within 23 px of the view's top or bottom edge, then of its left or right edge, the view scrolls 50 px; the dragged waypoint itself never moves.
    private void DragEdgeScroll(Vector2 at)
    {
        var (vertical, horizontal) = AreaWorldMapArtLayout.DragScroll(at.X, at.Y);
        if (vertical >= 0) ScrollBy(vertical, AreaWorldMapArtLayout.DragStep);
        if (horizontal >= 0) ScrollBy(horizontal, AreaWorldMapArtLayout.DragStep);
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
        ClaimScale();
        if (_look?.Art.TryGetValue(AreaWorldMapArtLayout.BackgroundArt, out var back) == true && back.Length > 0) DrawTexture(back[0], Vector2.Zero);
    }

    // The view, in its own coordinates (the window's minus the view's origin).
    private void DrawView(CanvasItem canvas)
    {
        var view = AreaWorldMapArtLayout.View;
        canvas.DrawRect(new Rect2(0, 0, view.Width, view.Height), Colors.Black);
        if (_look is null) return;
        if (_kind == AreaWorldMapKind.Map) DrawMapView(canvas);
        else if (_kind == AreaWorldMapKind.Town) DrawTownView(canvas);
        else DrawContinentView(canvas);
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

    // WM76–WM82: the black fill (done by the caller), the tiles, the party marker; the notes, the route and the followers follow in later slices.
    private void DrawTownView(CanvasItem canvas)
    {
        if (_town is not { } town) return;
        var plan = town.Plan;
        var (first, endColumn, firstRow, endRow) = AreaTownMapLayout.VisibleTiles(plan, _townOffset);
        var columns = plan.Info.Columns;
        // The cache is trimmed before the draw (a texture freed inside it would blank the tiles already issued).
        if (_townTextures.Count >= MaxTownTextures - 512) { foreach (var old in _townTextures.Values) old?.Dispose(); _townTextures.Clear(); }
        bool Revealed(int index) => town.CellRevealed(index) && TownFile(index);
        for (var row = firstRow; row < endRow; row++)
            for (var column = first; column < endColumn; column++)
            {
                var index = columns * row + column;
                var at = new Vector2(column * plan.TileWidth - _townOffset.X, row * plan.TileHeight - _townOffset.Y);
                var state = AreaTownMapLayout.State(plan.Entry.Whole, columns, plan.Info.Rows, column, row, Revealed, out var corners);
                if (state == AreaTownMapTileState.Black || TownTexture(index) is not { } texture) continue;
                if (state == AreaTownMapTileState.Plain) { canvas.DrawTexture(texture, at); continue; }
                // WM77: the blend with white corners on the sides of revealed neighbours, a fade into black (the corner order is the executable's, clockwise from the top left).
                var (w, h) = (plan.TileWidth, plan.TileHeight);
                Color Corner(int bit) => (corners & bit) != 0 ? Colors.White : Colors.Black;
                canvas.DrawPolygon([at, at + new Vector2(w, 0), at + new Vector2(w, h), at + new Vector2(0, h)],
                    [Corner(8), Corner(4), Corner(2), Corner(1)], [new(0, 0), new(1, 0), new(1, 1), new(0, 1)], texture);
            }
        // WM79, WM82: the notes next, every one with the question art 143 centred on its pixel, whatever its type (no label: the text shows on hover, WM59).
        if (town.Notes is { } notes)
            foreach (var note in notes.Items) MarkerAt(canvas, AreaWorldMapArtLayout.NoteArt, new Vector2(note.PixelX - _townOffset.X, note.PixelY - _townOffset.Y));
        // WM80, WM104: the route from the party through the waypoints' icon pixels, in the colours of the map view.
        var from = new Vector2(_townParty.X - _townOffset.X, _townParty.Y - _townOffset.Y);
        for (var i = 0; i < town.Waypoints.Count; i++)
        {
            var to = new Vector2(town.Waypoints.Items[i].Pixel.X - _townOffset.X, town.Waypoints.Items[i].Pixel.Y - _townOffset.Y);
            canvas.DrawLine(from, to, Rgb(i == town.Waypoints.Selected ? AreaWorldMapArtLayout.SelectedRouteColour : AreaWorldMapArtLayout.RouteColour), 1);
            MarkerAt(canvas, AreaWorldMapArtLayout.WaypointArt, to);
            from = to;
        }
        // WM81: the party marker at the pixel computed at the load.
        MarkerAt(canvas, AreaWorldMapArtLayout.PartyArt, new Vector2(_townParty.X - _townOffset.X, _townParty.Y - _townOffset.Y));
        // WM81: then each follower of the PC at its current pixel with the same art (no labels); one outside the picture is left out, not drawn at the corner.
        if (town.Followers is { } followers)
            foreach (var (x, y) in AreaTownMapLayout.FollowerPixels(plan.Info, followers()))
                MarkerAt(canvas, AreaWorldMapArtLayout.PartyArt, new Vector2(x - _townOffset.X, y - _townOffset.Y));
    }

    private bool TownFile(int index)
    {
        if (_townFiles.TryGetValue(index, out var exists)) return exists;
        return _townFiles[index] = _town!.Source.TileExists(_town.Plan.Entry, index);
    }

    // WM74: a tile reads when first drawn, a missing file standing in as tile 000000; a tile that does not decode stays unread (drawn black).
    private Texture2D? TownTexture(int index)
    {
        if (_townTextures.TryGetValue(index, out var cached)) return cached;
        ImageTexture? texture = null;
        if (_town!.Source.ReadTile(_town.Plan.Entry, index) is { } bytes)
        {
            var image = new Image();
            if (image.LoadBmpFromBuffer(bytes) == Error.Ok) { image.Convert(Image.Format.Rgba8); texture = ImageTexture.CreateFromImage(image); }
            image.Dispose();
        }
        _townTextures[index] = texture;
        return texture;
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
        // ARC-UI-110: the map view's notes, every one with the question art 143 centred on its pixel (the text shows on hover).
        if (_mapNotes is { } mapNotes)
            foreach (var note in mapNotes.Items) MarkerAt(canvas, AreaWorldMapArtLayout.NoteArt, new Vector2(note.PixelX - _offset.X, note.PixelY - _offset.Y));
        if (!map.OnStartMap) return;
        // WM80, WM104: a straight line from the party through each waypoint's icon pixel in turn; the one that ends at the selected waypoint is (50, 50, 110), the
        // others (160, 50, 50); each icon is drawn after its line, so the next line overdraws it.
        var from = map.Position;
        for (var i = 0; i < map.Waypoints.Count; i++)
        {
            var to = map.Waypoints[i].Pixel;
            canvas.DrawLine(ViewPoint(from), ViewPoint(to), Rgb(i == map.Selected ? AreaWorldMapArtLayout.SelectedRouteColour : AreaWorldMapArtLayout.RouteColour), 1);
            Marker(canvas, AreaWorldMapArtLayout.WaypointArt, to);
            from = to;
        }
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
        // ARC-UI-110: the continent view's notes, art 143 centred on the note's point of the continent picture.
        if (_continentNotes is { } continentNotes)
            foreach (var note in continentNotes.Items) { var dot = Dot(new WorldMapPixel(note.PixelX, note.PixelY)); MarkerAt(canvas, AreaWorldMapArtLayout.NoteArt, new Vector2(dot.X, dot.Y)); }
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

    private void Marker(CanvasItem canvas, int art, WorldMapPixel pixel) => MarkerAt(canvas, art, ViewPoint(pixel));

    // A marker art centred on a point of the view.
    private void MarkerAt(CanvasItem canvas, int art, Vector2 point)
    {
        if (_look is null || !_look.Art.TryGetValue(art, out var frames) || frames.Length == 0) return;
        var texture = frames[0];
        canvas.DrawTexture(texture, point - new Vector2(texture.GetWidth() / 2, texture.GetHeight() / 2));
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
        // ARC-UI-134 (WM140, WM141): the lens. The game view's copy lies under the window (the round view); a running journey fills the square black (mode 2),
        // and art 198 is drawn over the square in both states.
        var (lensX, lensY, lensSide) = AreaWorldMapArtLayout.Lens;
        _lensFrozenDrawn = LensFrozen;
        if (_lensFrozenDrawn) canvas.DrawRect(new Rect2(lensX, lensY, lensSide, lensSide), Colors.Black);
        if (_look.Art.TryGetValue(AreaWorldMapArtLayout.LensArt, out var lens) && lens.Length > 0) canvas.DrawTexture(lens[0], new Vector2(lensX, lensY));
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

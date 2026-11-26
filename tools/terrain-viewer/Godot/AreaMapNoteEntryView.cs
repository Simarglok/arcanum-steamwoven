using Arcanum.Presentation.Hud;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>What the map note entry shows besides its text: the background art (null: it did not read), each push button's frames and each radio button's frames (a null entry:
/// that art did not read) and the text face (the marker font's, art 840).</summary>
public sealed record AreaMapNoteEntryLook(Texture2D? Background, IReadOnlyList<Texture2D[]?> Buttons, IReadOnlyList<Texture2D[]?> Markers, AreaArtFontFace Font);

/// <summary>
/// ARC-UI-2306: the original's map note entry (bar mode 6, ledger BM0, BM4) drawn in the bottom strip over the message window: art 200 at the place every strip pane uses,
/// the text rectangle (262, 508, 303 × 24) filled black with the typed text in white from its left edge and the caret mark "*" after its end (0x5521B0), the three push
/// buttons (Done, Clear, Cancel: the bar's buttons, down while held over them, lit under the pointer, acting on the release) and the radio group of the note marker (the
/// chosen one down; the radio art's own states are not read, F0). It raises what the pointer does; the app decides.
/// </summary>
public partial class AreaMapNoteEntryView : Control
{
    private TextureRect _background = null!;
    private TextLayer _text = null!;
    private readonly TextureRect[] _buttons = new TextureRect[MapNoteEntry.Buttons.Count];
    private readonly TextureRect[] _markers = new TextureRect[MapNoteEntry.Markers.Count];
    private AreaMapNoteEntryLook? _look;
    private int _marker;
    private (bool Marker, int Index)? _held, _hovered;

    /// <summary>A left press on a push button, and its release (over the button or not).</summary>
    public event Action<MapNoteAction>? ButtonPressed;
    public event Action<MapNoteAction, bool>? ButtonReleased;
    /// <summary>A left press and release over a radio button (the marker type); the release over it chooses.</summary>
    public event Action<int>? MarkerPressed;
    public event Action<int, bool>? MarkerReleased;

    internal Texture2D? BackgroundForSelfTest => _background.Visible ? _background.Texture : null;
    internal string TextForSelfTest => _text.Shown;
    internal Vector2? ButtonCentreForSelfTest(MapNoteAction action) => _buttons[(int)action].Visible ? _buttons[(int)action].GetGlobalRect().GetCenter() : null;
    internal Vector2? MarkerCentreForSelfTest(int type) => _markers[type].Visible ? _markers[type].GetGlobalRect().GetCenter() : null;
    internal Texture2D? ButtonArtForSelfTest(MapNoteAction action) => _buttons[(int)action].Visible ? _buttons[(int)action].Texture : null;
    internal Texture2D? MarkerArtForSelfTest(int type) => _markers[type].Visible ? _markers[type].Texture : null;

    // ARC-UI-3347: the note markers of the kit, through the round-part layer (their rects stay for the pointer).
    private readonly AreaKitRoundPartsLayer _rings = new();

    /// <summary>ARC-UI-3347: the buttons drawn from the kit now, for the self-test.</summary>
    internal IReadOnlyList<int> RingsShownForSelfTest => _rings.ShownForSelfTest;

    public override void _EnterTree()
    {
        InterfaceHd.Changed += KitChanged;
        GetViewport().SizeChanged += KitChanged;
    }

    public override void _ExitTree()
    {
        InterfaceHd.Changed -= KitChanged;
        GetViewport().SizeChanged -= KitChanged;
    }

    private void KitChanged()
    {
        if (IsNodeReady()) RenderButtons();
    }

    public override void _Ready()
    {
        Name = "MapNoteEntry";
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(AreaGameBarLayout.ScreenWidth, AreaGameBarLayout.BottomHeight);
        // The background takes the clicks over the pane, so the message window under it does not scroll its entries.
        _background = new TextureRect { Name = "Background", Position = Local(MapNoteEntry.BackgroundX, MapNoteEntry.BackgroundY), MouseFilter = MouseFilterEnum.Stop };
        AddChild(_background);
        _text = new TextLayer { Name = "Text", MouseFilter = MouseFilterEnum.Ignore, Position = Local(MapNoteEntry.TextX, MapNoteEntry.TextY), ClipContents = true,
            Size = new Vector2(MapNoteEntry.TextWidth, MapNoteEntry.TextHeight) };
        AreaArtTextDraw.Prepare(_text);
        AddChild(_text);
        for (var b = 0; b < _buttons.Length; b++)
        {
            var button = MapNoteEntry.Buttons[b];
            _buttons[b] = MakeButton("Note" + button.Action, button.X, button.Y, false, b);
        }
        for (var m = 0; m < _markers.Length; m++)
        {
            var marker = MapNoteEntry.Markers[m];
            _markers[m] = MakeButton("NoteMarker" + marker.Type, marker.X, marker.Y, true, m);
        }
        AddChild(_rings);
        // Hidden mid-press (the bars went away): no release reaches the view, so the press and the hover are forgotten.
        VisibilityChanged += () => { if (!Visible) { _held = null; _hovered = null; Render(); } };
        Render();
    }

    private TextureRect MakeButton(string name, int x, int y, bool marker, int index)
    {
        var rect = new TextureRect { Name = name, Position = Local(x, y), MouseFilter = MouseFilterEnum.Stop, Visible = false };
        rect.GuiInput += input => ButtonInput(rect, input, marker, index);
        rect.MouseEntered += () => { _hovered = (marker, index); Render(); };
        rect.MouseExited += () => { if (_hovered == (marker, index)) _hovered = null; Render(); };
        AddChild(rect);
        return rect;
    }

    /// <summary>The strip is the bottom one (origin 0, 441): screen coordinates become the strip's own.</summary>
    private static Vector2 Local(int x, int y) => new(x, y - AreaGameBarLayout.BottomTop);

    private void ButtonInput(Control rect, InputEvent input, bool marker, int index)
    {
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } click) return;
        rect.AcceptEvent();
        if (click.Pressed)
        {
            _held = (marker, index);
            Render();
            if (marker) MarkerPressed?.Invoke(MapNoteEntry.Markers[index].Type); else ButtonPressed?.Invoke(MapNoteEntry.Buttons[index].Action);
            return;
        }
        // Godot sends the release to the control that took the press; it is over the button when it lands inside it.
        var over = _held == (marker, index) && new Rect2(Vector2.Zero, rect.Size).HasPoint(click.Position);
        _held = null;
        Render();
        if (marker) MarkerReleased?.Invoke(MapNoteEntry.Markers[index].Type, over); else ButtonReleased?.Invoke(MapNoteEntry.Buttons[index].Action, over);
    }

    /// <summary>Draws <paramref name="look"/>.</summary>
    public void Bind(AreaMapNoteEntryLook look)
    {
        _look = look ?? throw new ArgumentNullException(nameof(look));
        if (IsNodeReady()) Render();
    }

    /// <summary>The text the entry shows (the entry's own text; empty draws nothing but the caret) and the radio group's chosen marker type.</summary>
    public void SetState(string text, int marker)
    {
        ArgumentNullException.ThrowIfNull(text);
        _marker = marker;
        _text.Set(_look?.Font, text);
        if (IsNodeReady()) RenderButtons();
    }

    private void Render()
    {
        var look = _look;
        _background.Texture = look?.Background;
        _background.Visible = look?.Background is not null;
        if (look?.Background is { } background) _background.Size = background.GetSize();
        _text.Set(look?.Font, _text.Shown);
        RenderButtons();
    }

    private void RenderButtons()
    {
        var rings = new List<AreaKitRoundPartsLayer.Item>();
        for (var b = 0; b < _buttons.Length; b++) DrawButton(_buttons[b], _look?.Buttons.ElementAtOrDefault(b), (false, b), false);
        for (var m = 0; m < _markers.Length; m++)
        {
            var frame = DrawButton(_markers[m], _look?.Markers.ElementAtOrDefault(m), (true, m), MapNoteEntry.Markers[m].Type == _marker);
            AreaKitRingButtonRect.Apply(_markers[m], MapNoteEntry.Markers[m].Art, frame, rings);
        }
        _rings.Show(rings);
    }

    // A button's frame (GB4): down while held with the pointer over it (a radio button also while chosen); lit (two steps) under the pointer; else idle. A button whose art
    // did not read is not shown (no invisible area takes the pointer).
    private int DrawButton(TextureRect rect, Texture2D[]? frames, (bool Marker, int Index) id, bool chosen)
    {
        rect.Visible = frames is { Length: > 0 };
        if (frames is not { Length: > 0 }) return 0;
        var held = _held == id;
        var hovered = _hovered == id;
        var look = chosen || (held && hovered) ? AreaBarButtonLook.Down : hovered && !held ? AreaBarButtonLook.Hovered : AreaBarButtonLook.Idle;
        var frame = AreaGameBarLayout.Frame(look, frames.Length);
        rect.Texture = frames[frame];
        rect.Size = frames[0].GetSize();
        return frame;
    }

    private sealed partial class TextLayer : Control
    {
        private AreaArtFontFace? _face;
        private string _shown = "";

        public string Shown => _shown;

        public void Set(AreaArtFontFace? face, string shown)
        {
            if (ReferenceEquals(face, _face) && shown == _shown) return;
            _face = face; _shown = shown;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_face is not { } face) return;
            DrawRect(new Rect2(Vector2.Zero, Size), Colors.Black);
            AreaArtTextDraw.Text(this, face, _shown, 0, 0, MapNoteEntry.TextColour);
            var width = _shown.Length == 0 ? 0 : face.Font.Measure(_shown).Width;
            AreaArtTextDraw.Text(this, face, MapNoteEntry.CaretMark, MapNoteEntry.CaretOffset + width, 0, MapNoteEntry.TextColour);
        }
    }
}

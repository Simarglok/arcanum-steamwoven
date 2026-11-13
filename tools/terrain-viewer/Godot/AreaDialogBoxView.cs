using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-WORLD-120: the dialog box's art: the background, the button frames by art number and the font.</summary>
public sealed record AreaDialogBoxArt(Texture2D Background, IReadOnlyDictionary<int, IReadOnlyList<Texture2D>> Buttons, AreaArtFontFace Font,
    int LineHeight);

/// <summary>
/// ARC-WORLD-120: the original's dialog box (<see cref="AreaDialogBoxLayout"/>, ledger DB1–DB4) as a control of the window's own size
/// (325 × 136), placed by its parent on the 800 × 600 screen: the background, the text in white art-font lines and the buttons, which
/// show their frames as GB4 (idle, down while held over them, lit under the pointer), act on the release over the pressed button (GB3)
/// and play no sound. The keys go to the app. ARC-UI-070: the text in the original's blend (the box's font has no flags: the cover, MW31).
/// </summary>
public partial class AreaDialogBoxView : Control
{
    private AreaDialogBoxArt? _art;
    private AreaDialogBoxKind _kind = AreaDialogBoxKind.YesNo;
    private string _text = "";
    private IReadOnlyList<IReadOnlyList<(int X, AreaArtGlyph Glyph)>> _lines = [];
    private IReadOnlyList<(int Art, bool Yes, int X, int Y)> _buttons = [];
    // _hovered is the button under the pointer, _pressed the one held (-1 none).
    private int _hovered = -1, _pressed = -1;

    /// <summary>A button released over itself: true for the yes (accept) button.</summary>
    public event Action<bool>? AnswerRequested;

    public bool HasArt => _art is not null;
    internal string TextForSelfTest => Visible ? _text : "";
    internal int LinesForSelfTest => Visible ? _lines.Count : 0;
    /// <summary>The window position of a button's centre (true: yes), or null.</summary>
    internal Vector2? ButtonCentreForSelfTest(bool yes) => Visible && _art is { } art && Index(yes) is var index and >= 0
        ? GetGlobalTransform() * new Vector2(_buttons[index].X + ButtonSize(art, index).X / 2f, _buttons[index].Y + ButtonSize(art, index).Y / 2f) : null;
    /// <summary>The frame a button shows now (-1 none).</summary>
    internal int ButtonFrameForSelfTest(bool yes) => Visible && _art is { } art && Index(yes) is var index and >= 0 ? Frame(index, art.Buttons[_buttons[index].Art].Count) : -1;

    public override void _Ready()
    {
        Name = "DialogBox";
        Size = new Vector2(AreaDialogBoxLayout.Width, AreaDialogBoxLayout.Height);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        AreaArtTextDraw.Prepare(this);
    }

    public override void _ExitTree() => Release();

    public void SetArt(AreaDialogBoxArt art)
    {
        ArgumentNullException.ThrowIfNull(art);
        // ARC-UI-1819: the textures of an art this view no longer shows are let go.
        Release();
        _art = art;
    }

    // ARC-UI-1819: the generated interface paints the box and its buttons at the window's own device scale (the shared AreaDeviceScale claim). The box on screen claims the art's textures at
    // its draw (the death panel and the menu share one art) and a frame a worker swaps in redraws it; a change of the window's scale moves no pixel by itself (the canvas item is only
    // stretched), so the box also draws again then.
    private AreaDeviceScale? _claim;
    private AreaDeviceScale Claim => _claim ??= new(this);
    private float _drawnScale;

    internal float DeviceScale() => Claim.Current();

    private void Release() => _claim?.Release();

    private IEnumerable<Texture2D> Textures() => _art is { } art ? art.Buttons.Values.SelectMany(frames => frames).Prepend(art.Background) : [];

    private void ClaimScale()
    {
        Claim.Claim(Textures());
        _drawnScale = DeviceScale();
    }

    public override void _Process(double delta)
    {
        if (IsVisibleInTree() && Math.Abs(DeviceScale() - _drawnScale) >= 0.0005f) QueueRedraw();
    }

    /// <summary>ARC-UI-1819: the textures of the box (the background 822, then the frames of the buttons it shows) with their art numbers, for the native smoke.</summary>
    internal IReadOnlyList<(int Art, HdInterfaceTexture Texture)> TexturesForSelfTest() => _art is not { } art ? [] :
    [
        .. new[] { (AreaDialogBoxLayout.BackgroundArt, art.Background) }.Concat(_buttons.Select(button => button.Art).Distinct()
            .SelectMany(number => art.Buttons[number].Select(frame => (number, frame)))).Where(pair => pair.Item2 is HdInterfaceTexture)
            .Select(pair => (pair.Item1, (HdInterfaceTexture)pair.Item2)),
    ];

    /// <summary>Shows the box with <paramref name="text"/> (a line feed starts a new line) at its window point of the screen.</summary>
    public void Show(string text, AreaDialogBoxKind kind, (int X, int Y) point)
    {
        ArgumentNullException.ThrowIfNull(text);
        var art = _art ?? throw new InvalidOperationException("ARC-WORLD-120: the dialog box art is not set");
        if (!Visible || kind != _kind) { _hovered = -1; _pressed = -1; }
        (_text, _kind) = (text, kind);
        _lines = AreaDialogBoxLayout.Lines(art.Font.Font, text, art.LineHeight);
        _buttons = AreaDialogBoxLayout.Buttons(kind);
        Position = new Vector2(point.X, point.Y);
        Visible = true;
        QueueRedraw();
    }

    public void Close() { Visible = false; _hovered = -1; _pressed = -1; }

    private int Index(bool yes)
    {
        for (var i = 0; i < _buttons.Count; i++) if (_buttons[i].Yes == yes) return i;
        return -1;
    }

    private Vector2 ButtonSize(AreaDialogBoxArt art, int index) => art.Buttons[_buttons[index].Art][0].GetSize();

    /// <summary>GB4: down while held with the pointer over it, lit under the pointer when none is held, idle otherwise.</summary>
    private int Frame(int index, int frames) => AreaGameBarLayout.Frame(
        _pressed == index && _hovered == index ? AreaBarButtonLook.Down
        : _pressed < 0 && _hovered == index ? AreaBarButtonLook.Hovered : AreaBarButtonLook.Idle, frames);

    private int HitTest(Vector2 at)
    {
        if (_art is not { } art) return -1;
        for (var i = 0; i < _buttons.Count; i++)
            if (new Rect2(_buttons[i].X, _buttons[i].Y, ButtonSize(art, i)).HasPoint(at)) return i;
        return -1;
    }

    public override void _Draw()
    {
        if (_art is not { } art) return;
        ClaimScale();
        DrawTextureRect(art.Background, new Rect2(0, 0, AreaDialogBoxLayout.Width, AreaDialogBoxLayout.Height), false);
        var (x, y, _, _) = AreaDialogBoxLayout.Text;
        for (var line = 0; line < _lines.Count; line++)
            AreaArtTextDraw.Glyphs(this, art.Font, _lines[line], x, y + line * art.LineHeight, AreaDialogBoxLayout.Face);
        for (var i = 0; i < _buttons.Count; i++)
        {
            var frames = art.Buttons[_buttons[i].Art];
            var texture = frames[Frame(i, frames.Count)];
            DrawTextureRect(texture, new Rect2(_buttons[i].X, _buttons[i].Y, texture.GetSize()), false);
        }
    }

    public override void _Notification(int what)
    {
        if (what != NotificationMouseExit || _hovered < 0) return;
        _hovered = -1;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseMotion motion:
                // While a button is held no other button takes the pointer (GB3).
                var over = HitTest(motion.Position);
                if (_pressed >= 0 && over != _pressed) over = -1;
                if (over != _hovered) { _hovered = over; QueueRedraw(); }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                var at = HitTest(button.Position);
                if (button.Pressed) { _pressed = at; _hovered = at; QueueRedraw(); }
                else
                {
                    var chosen = _pressed >= 0 && at == _pressed ? _pressed : -1;
                    _pressed = -1; _hovered = at;
                    QueueRedraw();
                    if (chosen >= 0) AnswerRequested?.Invoke(_buttons[chosen].Yes);
                }
                AcceptEvent();
                break;
        }
    }
}

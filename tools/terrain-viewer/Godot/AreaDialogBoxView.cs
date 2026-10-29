using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-WORLD-120: the dialog box's art: the background, the button frames by art number and the font's glyph frames by frame index.</summary>
public sealed record AreaDialogBoxArt(Texture2D Background, IReadOnlyDictionary<int, IReadOnlyList<Texture2D>> Buttons, AreaArtFont Font,
    IReadOnlyDictionary<int, Texture2D> Glyphs, int LineHeight);

/// <summary>
/// ARC-WORLD-120: the original's dialog box (<see cref="AreaDialogBoxLayout"/>, ledger DB1–DB4) as a control of the window's own size
/// (325 × 136), placed by its parent on the 800 × 600 screen: the background, the text in white art-font lines and the buttons, which
/// show their frames as GB4 (idle, down while held over them, lit under the pointer), act on the release over the pressed button (GB3)
/// and play no sound. The keys go to the app. PROJECT: a glyph's grey levels scale the white (the original's text blend is not read, MW5).
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
    }

    public void SetArt(AreaDialogBoxArt art) => _art = art ?? throw new ArgumentNullException(nameof(art));

    /// <summary>Shows the box with <paramref name="text"/> (a line feed starts a new line) at its window point of the screen.</summary>
    public void Show(string text, AreaDialogBoxKind kind, (int X, int Y) point)
    {
        ArgumentNullException.ThrowIfNull(text);
        var art = _art ?? throw new InvalidOperationException("ARC-WORLD-120: the dialog box art is not set");
        if (!Visible || kind != _kind) { _hovered = -1; _pressed = -1; }
        (_text, _kind) = (text, kind);
        _lines = AreaDialogBoxLayout.Lines(art.Font, text, art.LineHeight);
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
        DrawTextureRect(art.Background, new Rect2(0, 0, AreaDialogBoxLayout.Width, AreaDialogBoxLayout.Height), false);
        var (r, g, b) = AreaDialogBoxLayout.Face;
        var colour = Color.Color8(r, g, b);
        var (x, y, _, _) = AreaDialogBoxLayout.Text;
        for (var line = 0; line < _lines.Count; line++)
            foreach (var (gx, glyph) in _lines[line])
                if (art.Glyphs.TryGetValue(glyph.Frame, out var texture))
                    DrawTextureRect(texture, new Rect2(x + gx, y + line * art.LineHeight, glyph.Width, glyph.Height), false, colour);
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

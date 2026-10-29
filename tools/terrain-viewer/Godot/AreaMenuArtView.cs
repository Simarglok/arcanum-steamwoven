using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-058: a menu button window in the original's look (<see cref="AreaMenuArtLayout"/>, ledger MW1–MW8): the window's background
/// over the 800 × 600 screen at the whole factor the bars use, centred in the window, and its labels in the art font with their three
/// colour passes. The pointer over a label lights its face and plays 3026, a press plays 3027, and a release over the pressed label
/// chooses it (as the diagnostic window's buttons do). PROJECT: the shown buttons take the first slots in order (the original's
/// windows have no hidden buttons), and the focused label is lit as the pointer lights it at the window's opening and after a
/// controller's or key's focus move. ARC-UI-059: the options window's page buttons at their own points (MW7).
/// </summary>
public partial class AreaMenuArtView : Control
{
    private Texture2D? _background;
    private AreaArtFont? _font;
    private IReadOnlyDictionary<int, Texture2D> _glyphs = new Dictionary<int, Texture2D>();
    private IReadOnlyList<string> _labels = [];
    private IReadOnlyList<(int X, int Y, int Width, int Height)> _buttons = [];
    private AreaMenuWindow? _window;
    // _hovered is the lit label, _pointer the label under the pointer (its entry plays the sound).
    private int _focus = -1, _hovered = -1, _pointer = -1, _pressed = -1;

    /// <summary>A label chosen (its index among the shown buttons).</summary>
    public event Action<int>? ButtonPressed;
    /// <summary>A button's interface sound (3026 the pointer entering, 3027 a press).</summary>
    public event Action<int>? SoundRequested;

    internal int HoveredForSelfTest => _hovered;
    internal IReadOnlyList<string> LabelsForSelfTest => Visible ? _labels : [];
    internal IReadOnlyList<(int X, int Y, int Width, int Height)> ButtonsForSelfTest => Visible ? _buttons : [];
    /// <summary>The window position of a label's centre, or null.</summary>
    internal Vector2? ButtonCentreForSelfTest(int index) => Visible && index >= 0 && index < _buttons.Count
        ? GetGlobalTransform() * new Vector2(_buttons[index].X + _buttons[index].Width / 2f, _buttons[index].Y + _buttons[index].Height / 2f) : null;

    public override void _Ready()
    {
        Name = "MainMenuArt";
        Size = new Vector2(AreaMenuArtLayout.ScreenWidth, AreaMenuArtLayout.ScreenHeight);
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        Visible = false;
    }

    /// <summary>The background art and the glyph frames of the font (by frame index).</summary>
    public void SetArt(IReadOnlyDictionary<int, Texture2D> backgrounds, AreaArtFont font, IReadOnlyDictionary<int, Texture2D> glyphs)
    {
        _backgrounds = backgrounds ?? throw new ArgumentNullException(nameof(backgrounds));
        _font = font ?? throw new ArgumentNullException(nameof(font));
        _glyphs = glyphs ?? throw new ArgumentNullException(nameof(glyphs));
    }

    private IReadOnlyDictionary<int, Texture2D> _backgrounds = new Dictionary<int, Texture2D>();

    /// <summary>True when the art is set and <paramref name="window"/> is one this look draws.</summary>
    public bool Draws(AreaMenuWindow window) => _font is not null && AreaMenuArtLayout.Background(window) is { } art && _backgrounds.ContainsKey(art);

    /// <summary>Shows <paramref name="window"/> with its shown labels; <paramref name="focus"/> is the menu's focused button (-1 or
    /// below for none, as the options window's focus on a control).</summary>
    public void Render(AreaMenuWindow window, IReadOnlyList<string> labels, int focus)
    {
        ArgumentNullException.ThrowIfNull(labels);
        if (_font is null || AreaMenuArtLayout.Background(window) is not { } art || !_backgrounds.TryGetValue(art, out var background))
            throw new InvalidOperationException("ARC-UI-058: the menu art does not draw " + window);
        focus = Math.Max(focus, -1);
        var opened = _window != window || !Visible;
        // The focused label is lit when a window opens and after a focus move of the controller or a key (PROJECT); the pointer's next
        // move takes it over again.
        if (opened) { _hovered = focus; _pointer = -1; _pressed = -1; }
        else if (focus != _focus) _hovered = focus;
        (_window, _focus, _background, _labels) = (window, focus, background, labels);
        _buttons = [.. labels.Select((label, slot) => AreaMenuArtLayout.Button(window, slot, _font.Measure(label)))];
        if (_hovered >= _buttons.Count) _hovered = -1;
        Visible = true;
        Place();
        QueueRedraw();
    }

    public void CloseView()
    {
        Visible = false;
        (_window, _hovered, _pointer, _pressed) = (null, -1, -1, -1);
    }

    /// <summary>The screen at the bars' whole factor, centred in the viewport.</summary>
    public void Place()
    {
        var size = GetViewportRect().Size;
        var factor = AreaGameBarPanel.ScreenFactor(size);
        Scale = new Vector2(factor, factor);
        Position = new Vector2(AreaGameBarPanel.ScreenLeft(size, factor), MathF.Round((size.Y - AreaMenuArtLayout.ScreenHeight * factor) / 2));
    }

    public override void _Draw()
    {
        if (_background is null || _font is null) return;
        DrawTextureRect(_background, new Rect2(0, 0, AreaMenuArtLayout.ScreenWidth, AreaMenuArtLayout.ScreenHeight), false);
        for (var i = 0; i < _labels.Count; i++)
        {
            var (x, y, _, _) = _buttons[i];
            var glyphs = _font.Layout(_labels[i]);
            var passes = AreaMenuArtLayout.Passes;
            for (var pass = 0; pass < passes.Count; pass++)
            {
                var (dx, dy, (r, g, b)) = passes[pass];
                if (pass == passes.Count - 1 && i == _hovered) (r, g, b) = AreaMenuArtLayout.HoverFace;
                // PROJECT: a glyph's grey levels scale the pass's colour (the original's blend is not read, F0).
                var colour = Color.Color8(r, g, b);
                foreach (var (gx, glyph) in glyphs)
                    if (_glyphs.TryGetValue(glyph.Frame, out var texture))
                        DrawTextureRect(texture, new Rect2(x + gx + dx, y + dy, glyph.Width, glyph.Height), false, colour);
            }
        }
    }

    // The pointer leaving the screen's control (into the margin or out of the window) is the original's event 3: the label goes dark.
    public override void _Notification(int what)
    {
        if (what != NotificationMouseExit) return;
        _pointer = -1;
        if (_hovered < 0) return;
        _hovered = -1;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseMotion motion:
                var over = AreaMenuArtLayout.HitTest(_buttons, motion.Position.X, motion.Position.Y);
                if (over != _pointer)
                {
                    _pointer = over;
                    if (over >= 0) SoundRequested?.Invoke(AreaGameBarLayout.MenuHoverSound);
                }
                if (over == _hovered) break;
                _hovered = over;
                QueueRedraw();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                var at = AreaMenuArtLayout.HitTest(_buttons, button.Position.X, button.Position.Y);
                if (button.Pressed)
                {
                    _pressed = at;
                    if (at >= 0) SoundRequested?.Invoke(AreaGameBarLayout.MenuPressSound);
                }
                else
                {
                    var chosen = _pressed >= 0 && at == _pressed ? at : -1;
                    _pressed = -1;
                    if (chosen >= 0) ButtonPressed?.Invoke(chosen);
                }
                AcceptEvent();
                break;
        }
    }
}

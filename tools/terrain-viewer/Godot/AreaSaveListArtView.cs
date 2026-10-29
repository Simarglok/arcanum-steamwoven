using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-062: the save and load window in the original's look (<see cref="AreaSaveListArtLayout"/>, ledger MW9–MW13): the strips and
/// the background over the 800 × 600 screen at the bars' whole factor, centred in the window; the list's rows in the art font from the
/// scroll top, the selected row gold; the three art buttons with their idle, down and lit frames (GB4) and push behaviour (GB3:
/// down on the press, acting on a release over it), 3000 on the press and 3001 on the release (GB7). A press on a row selects it.
/// PROJECT: the selected save's stored preview is drawn into the window's preview rectangle and the wheel over the list scrolls it
/// (the scroll bar widget's art and the preview's own blit are not read, F0).
/// </summary>
public partial class AreaSaveListArtView : Control
{
    private static readonly AreaSaveListArtButton[] AllButtons = Enum.GetValues<AreaSaveListArtButton>();
    private IReadOnlyDictionary<int, Texture2D[]> _art = new Dictionary<int, Texture2D[]>();
    private AreaArtFont? _font;
    private IReadOnlyDictionary<int, Texture2D> _glyphs = new Dictionary<int, Texture2D>();
    private IReadOnlyList<string> _rows = [];
    private Texture2D? _preview;
    private AreaSaveListMode _mode;
    private int _selected = -1, _top;
    private AreaSaveListArtButton? _hovered, _pressed;

    /// <summary>A press on a row (its index in the list).</summary>
    public event Action<int>? RowPressed;
    /// <summary>A button released over itself after its press.</summary>
    public event Action<AreaSaveListArtButton>? ButtonPressed;
    /// <summary>A button's interface sound (3000 the press, 3001 the release).</summary>
    public event Action<int>? SoundRequested;

    public bool ArtSet => _font is not null;
    internal int TopForSelfTest => _top;
    /// <summary>The shown rows' texts as drawn (fitted), from the scroll top.</summary>
    internal IReadOnlyList<string> ShownRowsForSelfTest => Visible && _font is { } font
        ? [.. Enumerable.Range(_top, Math.Clamp(_rows.Count - _top, 0, AreaSaveListArtLayout.ShownRows))
            .Select(row => AreaSaveListArtLayout.Fit(font, _rows[row], AreaSaveListArtLayout.Row(0).Width))] : [];
    internal int FrameForSelfTest(AreaSaveListArtButton button) => Frame(button);
    /// <summary>The window position of a screen point of the 800 × 600 screen.</summary>
    internal Vector2 ScreenPointForSelfTest(double x, double y) => GetGlobalTransform() * new Vector2((float)x, (float)y);

    public override void _Ready()
    {
        Name = "SaveListArt";
        Size = new Vector2(AreaSaveListArtLayout.ScreenWidth, AreaSaveListArtLayout.ScreenHeight);
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        Visible = false;
    }

    /// <summary>The art's frames by number and the font's glyph frames (by frame index).</summary>
    public void SetArt(IReadOnlyDictionary<int, Texture2D[]> art, AreaArtFont font, IReadOnlyDictionary<int, Texture2D> glyphs)
    {
        _art = art ?? throw new ArgumentNullException(nameof(art));
        _font = font ?? throw new ArgumentNullException(nameof(font));
        _glyphs = glyphs ?? throw new ArgumentNullException(nameof(glyphs));
    }

    /// <summary>Shows the rows (the save window's new-save row first) with <paramref name="selected"/> lit; a new opening starts at the
    /// scroll top 0 (0x543380), later renders keep the selected row shown.</summary>
    public void Render(AreaSaveListMode mode, IReadOnlyList<string> rows, int selected, Texture2D? preview)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (_font is null) throw new InvalidOperationException("ARC-UI-062: the save window art is not set");
        if (!Visible) { _top = 0; _hovered = null; _pressed = null; }
        (_mode, _rows, _selected, _preview) = (mode, rows, selected, preview);
        _top = AreaSaveListArtLayout.ScrollTop(_top, selected, rows.Count);
        Visible = true;
        Place();
        QueueRedraw();
    }

    public void CloseView()
    {
        Visible = false;
        (_hovered, _pressed, _preview) = (null, null, null);
    }

    /// <summary>The screen at the bars' whole factor, centred in the viewport.</summary>
    public void Place()
    {
        var size = GetViewportRect().Size;
        var factor = AreaGameBarPanel.ScreenFactor(size);
        Scale = new Vector2(factor, factor);
        Position = new Vector2(AreaGameBarPanel.ScreenLeft(size, factor), MathF.Round((size.Y - AreaSaveListArtLayout.ScreenHeight * factor) / 2));
    }

    private Texture2D? Art(int number, int frame = 0) => _art.TryGetValue(number, out var frames) && frame < frames.Length ? frames[frame] : null;

    private int Frame(AreaSaveListArtButton button)
    {
        var frames = _art.TryGetValue(AreaSaveListArtLayout.Button(button).Art, out var art) ? art.Length : 1;
        // GB3/GB4: a held button shows down over it and idle away from it; another button is not lit while one is held.
        var look = _pressed is { } held ? held == button && _hovered == button ? AreaBarButtonLook.Down : AreaBarButtonLook.Idle
            : _hovered == button ? AreaBarButtonLook.Hovered : AreaBarButtonLook.Idle;
        return AreaGameBarLayout.Frame(look, Math.Max(1, frames));
    }

    public override void _Draw()
    {
        if (_font is null) return;
        DrawRect(new Rect2(0, 0, AreaSaveListArtLayout.ScreenWidth, AreaSaveListArtLayout.ScreenHeight), Colors.Black);
        if (Art(AreaSaveListArtLayout.TopStripArt) is { } top) DrawTextureRect(top, new Rect2(0, 0, top.GetWidth(), top.GetHeight()), false);
        if (Art(AreaSaveListArtLayout.BottomStripArt) is { } bottom)
            foreach (var (x, y, width, height) in AreaSaveListArtLayout.BottomStripParts)
                DrawTextureRectRegion(bottom, new Rect2(x, y, width, height), new Rect2(x, 0, width, height));
        if (_preview is not null)
        {
            var (px, py, pw, ph) = AreaSaveListArtLayout.Preview;
            DrawTextureRect(_preview, new Rect2(px, py, pw, ph), false);
        }
        // The background's preview hole is transparent: the preview shows through it.
        if (Art(AreaSaveListArtLayout.BackgroundArt) is { } background)
            DrawTextureRect(background, new Rect2(0, AreaSaveListArtLayout.WindowTop, background.GetWidth(), background.GetHeight()), false);
        for (var slot = 0; slot < AreaSaveListArtLayout.ShownRows && _top + slot < _rows.Count; slot++)
        {
            var row = _top + slot;
            var (x, y, width, _) = AreaSaveListArtLayout.Row(slot);
            var (r, g, b) = row == _selected ? AreaSaveListArtLayout.SelectedColour : AreaSaveListArtLayout.RowColour;
            // PROJECT: a glyph's grey levels scale the font's colour (the text blit's blend is not read, F0; as the menu's labels).
            var colour = Color.Color8(r, g, b);
            foreach (var (gx, glyph) in _font.Layout(AreaSaveListArtLayout.Fit(_font, _rows[row], width)))
                if (_glyphs.TryGetValue(glyph.Frame, out var texture))
                    DrawTextureRect(texture, new Rect2(x + gx, y, glyph.Width, glyph.Height), false, colour);
        }
        foreach (var button in AllButtons)
        {
            var (art, x, y) = AreaSaveListArtLayout.Button(button);
            if (Art(art, Frame(button)) is { } texture) DrawTextureRect(texture, new Rect2(x, y, texture.GetWidth(), texture.GetHeight()), false);
        }
    }

    private AreaSaveListArtButton? ButtonAt(Vector2 at)
    {
        foreach (var button in AllButtons)
        {
            var (art, x, y) = AreaSaveListArtLayout.Button(button);
            if (Art(art) is { } texture && AreaSaveListArtLayout.Contains((x, y, texture.GetWidth(), texture.GetHeight()), at.X, at.Y)) return button;
        }
        return null;
    }

    public override void _Notification(int what)
    {
        if (what != NotificationMouseExit || _hovered is null) return;
        _hovered = null;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseMotion motion:
                var over = ButtonAt(motion.Position);
                if (over == _hovered) break;
                _hovered = over;
                QueueRedraw();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel
                when AreaSaveListArtLayout.Contains(AreaSaveListArtLayout.WheelArea, wheel.Position.X, wheel.Position.Y):
                var top = Math.Clamp(_top + (wheel.ButtonIndex == MouseButton.WheelUp ? -1 : 1), 0, AreaSaveListArtLayout.MaxScrollTop(_rows.Count));
                if (top != _top) { _top = top; QueueRedraw(); }
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                var at = ButtonAt(button.Position);
                if (button.Pressed)
                {
                    _pressed = at;
                    if (at is not null) SoundRequested?.Invoke(AreaGameBarLayout.PressSound);
                    else if (AreaSaveListArtLayout.RowAt(button.Position.X, button.Position.Y, _top, _rows.Count, _mode) is { } row && row >= 0) RowPressed?.Invoke(row);
                }
                else if (_pressed is { } held)
                {
                    _pressed = null;
                    // GB3: a release elsewhere only lets the button go, silently.
                    if (at == held) { SoundRequested?.Invoke(AreaGameBarLayout.ReleaseSound); ButtonPressed?.Invoke(held); }
                }
                QueueRedraw();
                AcceptEvent();
                break;
        }
    }
}

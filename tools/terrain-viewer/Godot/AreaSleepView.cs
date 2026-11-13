using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-1363: the sleep and wait window in the original's look (<see cref="AreaSleepLayout"/>, ledger SZ13, SWB6, SWB7): the window art 565 with, for each
/// length, a push button of art 293 (idle, down, lit) and its line of <c>sleepUI.mes</c> in the white font art 229. A control of the window art's size placed at
/// (573, 42) of the original's screen (the bars' whole factor); the pointer over it never reaches the world. A left press goes down (sound 3000) and the
/// release over the same button presses it (sound 3001, <see cref="RowPressed"/>). PROJECT: a button takes the pointer on its art's rectangle, not its opaque
/// pixels; the controller's focus row is drawn lit; a line is drawn from its rectangle's top left, wrapped at its width. A press hides nothing (0x57B710): the buttons stay
/// and a second press restarts the rest.
/// </summary>
public partial class AreaSleepView : Control
{
    private IReadOnlyDictionary<int, Texture2D[]> _art = new Dictionary<int, Texture2D[]>();
    private AreaArtFontFace? _font;
    private IReadOnlyList<string> _labels = [];
    private readonly AreaSleepButtons _buttons = new();
    // ARC-UI-1200: the generated interface paints the window's frames and buttons at the window's own device scale (claimed at every draw).
    private AreaDeviceScale? _claim;
    private AreaDeviceScale Claim => _claim ??= new(this);

    /// <summary>A button was pressed (0-based row).</summary>
    public event Action<int>? RowPressed;
    /// <summary>An interface sound of the buttons (3000 press, 3001 release).</summary>
    public event Action<int>? SoundRequested;

    /// <summary>The window's art and font read and are set.</summary>
    public bool ArtSet => _font is not null && _art.ContainsKey(AreaSleepLayout.BackgroundArt) && _art.ContainsKey(AreaSleepLayout.ButtonArt);
    internal int RowCount => _labels.Count;
    internal string LabelForSelfTest(int row) => _labels[row];
    internal Rect2 WindowRectForSelfTest => new(GlobalPosition, Size * Scale);
    internal Vector2 ButtonCentreForSelfTest(int row)
    {
        var (x, y) = AreaSleepLayout.ButtonAt(row);
        var (width, height) = ButtonSize();
        return GetGlobalTransform() * new Vector2(x + width / 2f, y + height / 2f);
    }
    internal int FrameForSelfTest(int row) => AreaGameBarLayout.Frame(_buttons.Look(row), ButtonFrames());
    /// <summary>ARC-UI-1200: the window's device scale (what its generated frames are painted at) and its textures by art number, every frame (the native smoke).</summary>
    internal float DeviceScale() => Claim.Current();
    internal IReadOnlyList<(int Art, HdInterfaceTexture Texture)> TexturesForSelfTest(IEnumerable<int> arts) =>
        [.. arts.Where(_art.ContainsKey).SelectMany(number => _art[number].OfType<HdInterfaceTexture>().Select(texture => (number, texture)))];
    internal AreaSleepButtons Buttons => _buttons;
    /// <summary>The interface sounds the buttons asked for, newest last (self-test and smoke).</summary>
    internal List<int> SoundsForSelfTest { get; } = [];

    /// <summary>A left click on a row's button as the pointer does it: it moves onto the button, presses and releases there (self-test and smoke).</summary>
    internal void ClickForSelfTest(int row)
    {
        var (x, y) = AreaSleepLayout.ButtonAt(row);
        var (width, height) = ButtonSize();
        var at = new Vector2(x + width / 2f, y + height / 2f);
        _GuiInput(new InputEventMouseMotion { Position = at });
        _GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        _GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    /// <summary>The pointer moves onto a row's button (self-test and smoke).</summary>
    internal void HoverForSelfTest(int row)
    {
        var (x, y) = AreaSleepLayout.ButtonAt(row);
        var (width, height) = ButtonSize();
        _GuiInput(new InputEventMouseMotion { Position = new Vector2(x + width / 2f, y + height / 2f) });
    }

    internal int ButtonFramesForSelfTest => ButtonFrames();

    private void Sound(int id) { SoundsForSelfTest.Add(id); SoundRequested?.Invoke(id); }

    public override void _Ready()
    {
        Name = "SleepWindow";
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        Visible = false;
        AreaArtTextDraw.Prepare(this);
    }

    public void SetArt(IReadOnlyDictionary<int, Texture2D[]> art, AreaArtFontFace font)
    {
        Claim.Release();
        _art = art ?? throw new ArgumentNullException(nameof(art));
        _font = font ?? throw new ArgumentNullException(nameof(font));
        if (_art.TryGetValue(AreaSleepLayout.BackgroundArt, out var window) && window.Length > 0) Size = new Vector2(window[0].GetWidth(), window[0].GetHeight());
        QueueRedraw();
    }

    /// <summary>Forgets the art (the module changed); the host sets it again when the new one has read.</summary>
    public void ClearArt()
    {
        Claim.Release();
        _art = new Dictionary<int, Texture2D[]>(); _font = null;
        QueueRedraw();
    }

    /// <summary>Shows the window with the line of each button.</summary>
    public void Open(IReadOnlyList<string> labels)
    {
        _labels = labels ?? throw new ArgumentNullException(nameof(labels));
        _buttons.Reset();
        Visible = true;
        Place();
        QueueRedraw();
    }

    public void CloseView()
    {
        _buttons.Reset();
        Visible = false;
    }

    /// <summary>The window at (573, 42) of the original's screen at the bars' whole factor.</summary>
    public void Place()
    {
        var size = GetViewportRect().Size;
        var factor = AreaGameBarPanel.ScreenFactor(size);
        Scale = new Vector2(factor, factor);
        Position = new Vector2(AreaGameBarPanel.ScreenLeft(size, factor) + AreaSleepLayout.ScreenX * factor, AreaSleepLayout.ScreenY * factor);
    }

    /// <summary>The controller's focus row (PROJECT; drawn lit); null none.</summary>
    public int? Focus
    {
        get => _buttons.Focus;
        set { if (_buttons.Focus != value) { _buttons.Focus = value; QueueRedraw(); } }
    }

    /// <summary>The controller's confirm: the focused row (the first when none is) presses as a click on it (sounds 3000 then 3001).</summary>
    public bool Confirm()
    {
        if (_labels.Count == 0) return false;
        var row = Math.Min(_buttons.Focus ?? 0, _labels.Count - 1);
        foreach (var sound in new[] { AreaGameBarLayout.PressSound, AreaGameBarLayout.ReleaseSound }) Sound(sound);
        RowPressed?.Invoke(row);
        return true;
    }

    private int ButtonFrames() => _art.TryGetValue(AreaSleepLayout.ButtonArt, out var frames) ? Math.Max(1, frames.Length) : 1;

    private (int Width, int Height) ButtonSize() =>
        _art.TryGetValue(AreaSleepLayout.ButtonArt, out var frames) && frames.Length > 0 ? (frames[0].GetWidth(), frames[0].GetHeight()) : (0, 0);

    private int? RowAt(Vector2 at)
    {
        var (width, height) = ButtonSize();
        return AreaSleepLayout.RowAt(at.X, at.Y, width, height, _labels.Count);
    }

    public override void _Draw()
    {
        if (_font is not { } font) return;
        Claim.Claim(_art, AreaSleepLayout.BackgroundArt, AreaSleepLayout.ButtonArt);
        if (AreaCharacterArtDraw.Art(_art, AreaSleepLayout.BackgroundArt) is { } window)
            DrawTextureRect(window, new Rect2(0, 0, window.GetWidth(), window.GetHeight()), false);
        var frames = ButtonFrames();
        var width = (int)Size.X;
        for (var row = 0; row < _labels.Count; row++)
        {
            var (bx, by) = AreaSleepLayout.ButtonAt(row);
            if (AreaCharacterArtDraw.Art(_art, AreaSleepLayout.ButtonArt, AreaGameBarLayout.Frame(_buttons.Look(row), frames)) is { } button)
                DrawTextureRect(button, new Rect2(bx, by, button.GetWidth(), button.GetHeight()), false);
            AreaCharacterArtDraw.Lines(this, AreaSleepLayout.TextField(row, width), _labels[row], font, AreaSleepLayout.TextColour);
        }
    }

    public override void _ExitTree() => _claim?.Release();

    public override void _Notification(int what)
    {
        if (what != NotificationMouseExit || _buttons.Hovered is not { } hovered) return;
        _buttons.Exit(hovered);
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseMotion motion:
                var over = RowAt(motion.Position);
                if (over == _buttons.Hovered) break;
                if (_buttons.Hovered is { } left) _buttons.Exit(left);
                if (over is { } entered) _buttons.Enter(entered);
                QueueRedraw();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
                if (RowAt(press.Position) is { } row)
                {
                    foreach (var sound in _buttons.Press(row).Sounds) Sound(sound);
                    QueueRedraw();
                }
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } release:
                var effect = _buttons.Release(RowAt(release.Position));
                foreach (var sound in effect.Sounds) Sound(sound);
                QueueRedraw();
                if (effect.Act is { } act) RowPressed?.Invoke(act);
                AcceptEvent();
                break;
            case InputEventMouseButton:
                AcceptEvent();
                break;
        }
    }
}

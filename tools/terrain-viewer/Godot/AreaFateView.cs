using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-1112: the Fate Point window in the original's look (<see cref="AreaFateLayout"/>, ledger FW1–FW6): the window art 292 with the twelve lines of
/// <c>fate_ui.mes</c> in the white font art 229 and, for each fate kind, a toggle button of art 293 (down while the kind is armed, lit under the pointer).
/// A control of the window art's size placed at (0, 41) of the original's screen (the bars' whole factor); the pointer over it never reaches the world. A
/// press flips a button (sound 3000 turning on, 3001 turning off) and the host arms or undoes the kind; the host sets the buttons from the hero's fate
/// again after each answer, so a refused press pops back. PROJECT: a button takes the pointer on its art's rectangle, not its opaque pixels; the
/// controller has no focus row here; the lines are drawn from their rectangle's top left, wrapped at its width.
/// </summary>
public partial class AreaFateView : Control
{
    private IReadOnlyDictionary<int, Texture2D[]> _art = new Dictionary<int, Texture2D[]>();
    private AreaArtFontFace? _font;
    private AreaFateTexts _texts = AreaFateTexts.Project("not set");
    private readonly AreaFateButtons _buttons = new();
    // ARC-UI-1200: the generated interface paints the window's frames and buttons at the window's own device scale (claimed at every draw).
    private AreaDeviceScale? _claim;
    private AreaDeviceScale Claim => _claim ??= new(this);

    /// <summary>A button was pressed: the kind and whether it is now down (arm) or up (undo).</summary>
    public event Action<int, bool>? KindToggled;
    /// <summary>An interface sound of the buttons (3000 turning a button on, 3001 off).</summary>
    public event Action<int>? SoundRequested;

    /// <summary>The window's art and font read and are set.</summary>
    public bool ArtSet => _font is not null && _art.ContainsKey(AreaFateLayout.BackgroundArt) && _art.ContainsKey(AreaFateLayout.ButtonArt);
    public bool IsShown => Visible;
    internal AreaFateButtons Buttons => _buttons;
    internal int? HoveredForSelfTest => _buttons.Hovered;
    internal bool DownForSelfTest(int kind) => _buttons.IsOn(kind);
    internal Vector2 ButtonCentreForSelfTest(int kind)
    {
        var (x, y) = AreaFateLayout.ButtonAt(kind);
        var (width, height) = ButtonSize();
        return GetGlobalTransform() * new Vector2(x + width / 2f, y + height / 2f);
    }
    internal Rect2 WindowRectForSelfTest => new(GlobalPosition, Size * Scale);
    internal string LineForSelfTest(int kind) => _texts.Line(kind);
    internal int FrameForSelfTest(int kind) => AreaGameBarLayout.Frame(_buttons.Look(kind), ButtonFrames());
    /// <summary>ARC-UI-1200: the window's device scale (what its generated frames are painted at) and its textures by art number, every frame (the native smoke).</summary>
    internal float DeviceScale() => Claim.Current();
    internal IReadOnlyList<(int Art, HdInterfaceTexture Texture)> TexturesForSelfTest(IEnumerable<int> arts) =>
        [.. arts.Where(_art.ContainsKey).SelectMany(number => _art[number].OfType<HdInterfaceTexture>().Select(texture => (number, texture)))];

    public override void _Ready()
    {
        Name = "FateWindow";
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        Visible = false;
        AreaArtTextDraw.Prepare(this);
    }

    public void SetArt(IReadOnlyDictionary<int, Texture2D[]> art, AreaArtFontFace font, AreaFateTexts texts)
    {
        Claim.Release();
        _art = art ?? throw new ArgumentNullException(nameof(art));
        _font = font ?? throw new ArgumentNullException(nameof(font));
        _texts = texts ?? throw new ArgumentNullException(nameof(texts));
        if (_art.TryGetValue(AreaFateLayout.BackgroundArt, out var window) && window.Length > 0) Size = new Vector2(window[0].GetWidth(), window[0].GetHeight());
        QueueRedraw();
    }

    /// <summary>Forgets the art and the lines (the module changed); the host sets them again when the new ones have read.</summary>
    public void ClearArt()
    {
        Claim.Release();
        _art = new Dictionary<int, Texture2D[]>(); _font = null; _texts = AreaFateTexts.Project("not set");
        QueueRedraw();
    }

    /// <summary>Shows the window (the host sets its buttons from the hero's fate with <see cref="Follow"/>).</summary>
    public void Open()
    {
        Visible = true;
        Place();
        QueueRedraw();
    }

    public void CloseView()
    {
        _buttons.Reset();
        Visible = false;
    }

    /// <summary>The window at (0, 41) of the original's screen at the bars' whole factor.</summary>
    public void Place()
    {
        var size = GetViewportRect().Size;
        var factor = AreaGameBarPanel.ScreenFactor(size);
        Scale = new Vector2(factor, factor);
        Position = new Vector2(AreaGameBarPanel.ScreenLeft(size, factor) + AreaFateLayout.ScreenX * factor, AreaFateLayout.ScreenY * factor);
    }

    /// <summary>Sets every button from the hero's armed kinds and redraws when one changed.</summary>
    public void Follow(Arcanum.Simulation.Character.FateState? fate)
    {
        var before = Enumerable.Range(0, AreaFateLayout.Kinds).Select(_buttons.IsOn).ToArray();
        _buttons.Sync(fate);
        if (!before.SequenceEqual(Enumerable.Range(0, AreaFateLayout.Kinds).Select(_buttons.IsOn))) QueueRedraw();
    }

    private int ButtonFrames() => _art.TryGetValue(AreaFateLayout.ButtonArt, out var frames) ? Math.Max(1, frames.Length) : 1;

    private (int Width, int Height) ButtonSize() =>
        _art.TryGetValue(AreaFateLayout.ButtonArt, out var frames) && frames.Length > 0 ? (frames[0].GetWidth(), frames[0].GetHeight()) : (0, 0);

    private int? KindAt(Vector2 at)
    {
        var (width, height) = ButtonSize();
        return AreaFateLayout.KindAt(at.X, at.Y, width, height);
    }

    public override void _Draw()
    {
        if (_font is not { } font) return;
        Claim.Claim(_art, AreaFateLayout.BackgroundArt, AreaFateLayout.ButtonArt);
        if (AreaCharacterArtDraw.Art(_art, AreaFateLayout.BackgroundArt) is { } window)
            DrawTextureRect(window, new Rect2(0, 0, window.GetWidth(), window.GetHeight()), false);
        var frames = ButtonFrames();
        var width = (int)Size.X;
        for (var kind = 0; kind < AreaFateLayout.Kinds; kind++)
        {
            var (bx, by) = AreaFateLayout.ButtonAt(kind);
            if (AreaCharacterArtDraw.Art(_art, AreaFateLayout.ButtonArt, AreaGameBarLayout.Frame(_buttons.Look(kind), frames)) is { } button)
                DrawTextureRect(button, new Rect2(bx, by, button.GetWidth(), button.GetHeight()), false);
            var (tx, ty) = AreaFateLayout.TextAt(kind);
            AreaCharacterArtDraw.Lines(this, (tx, ty, width - tx, AreaFateLayout.RowStep), _texts.Line(kind), font, AreaFateLayout.TextColour);
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
                var over = KindAt(motion.Position);
                if (over == _buttons.Hovered) break;
                if (_buttons.Hovered is { } left) _buttons.Exit(left);
                if (over is { } entered) _buttons.Enter(entered);
                QueueRedraw();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
                if (KindAt(press.Position) is { } kind)
                {
                    _buttons.Enter(kind);
                    var effect = _buttons.Press(kind);
                    foreach (var sound in effect.Sounds) SoundRequested?.Invoke(sound);
                    if (effect.Kind is { } changed) KindToggled?.Invoke(changed, effect.On);
                    QueueRedraw();
                }
                AcceptEvent();
                break;
            case InputEventMouseButton:
                // The release of a toggle does nothing (GB3); the window still takes it.
                AcceptEvent();
                break;
        }
    }
}

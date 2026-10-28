using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-018: the original's in-game interface bars drawn with the game's own art: the top strip (Character, Logbook, Map,
/// Inventory, Fate Points, Sleep) at the top of the window and the bottom strip (Combat, Skills, Spells, Schematics) at its bottom,
/// each centred and scaled by the largest whole factor at which the original's 800×600 screen fits the window. A strip takes every
/// press on it (the original's world view never reaches under the bars); its buttons follow <see cref="AreaGameBar"/>. The bottom
/// strip holds the message window (<see cref="AreaMessageLogView"/>, ARC-UI-023), where the host writes the hover name, and the hotkey
/// bar (ARC-UI-027, a part the host adds with <see cref="AddPart"/>).
/// </summary>
public partial class AreaGameBarPanel : CanvasLayer
{
    private readonly Dictionary<AreaBarButton, TextureRect> _buttons = [];
    private readonly Dictionary<AreaBarStrip, Control> _strips = [];
    private IReadOnlyDictionary<int, ImageTexture[]> _art = new Dictionary<int, ImageTexture[]>();
    private AreaGameBar _bar = new();
    private AreaMessageLogView _messageLog = null!;

    /// <summary>A pointer event on the bars changed something: the host plays the sounds and runs the button's function.</summary>
    public event Action<AreaBarEffect>? Effect;
    /// <summary>The pointer moved while the bars are shown: its place on the original's 800×600 screen (ARC-UI-023: the hover name is
    /// written again on each move, and a move over the bottom strip gives the message window back to a pane, ML2/ML8).</summary>
    public event Action<Vector2>? PointerMoved;

    /// <summary>The message window of the bottom strip (ARC-UI-023).</summary>
    public AreaMessageLogView MessageLog => _messageLog;

    /// <summary>The whole factor the bars are drawn at.</summary>
    public int Factor { get; private set; } = 1;
    /// <summary>The height the bottom strip takes in the window while the bars are shown (0 otherwise): the diagnostic panels the
    /// viewer places at the window's bottom sit above it. One bar panel exists per app.</summary>
    public static float BottomInset { get; private set; }
    /// <summary>The height the top strip takes in the window while the bars are shown (0 otherwise).</summary>
    public static float TopInset { get; private set; }
    /// <summary>Raised after the insets changed (the bars shown, hidden or scaled): the panels placed by them place themselves again.</summary>
    public static event Action? InsetsChanged;

    /// <summary>The part of a window of <paramref name="size"/> the bars leave free: its top and its size.</summary>
    public static (float Top, Vector2 Size) FreeArea(Vector2 size) => (TopInset, new(size.X, Math.Max(1, size.Y - TopInset - BottomInset)));
    internal string HoverTextForSelfTest => _messageLog.HoverTextForSelfTest;
    internal int FrameForSelfTest(AreaBarButton button) => _buttons.TryGetValue(button, out var rect) && rect.HasMeta(FrameMeta) ? (int)rect.GetMeta(FrameMeta) : -1;
    internal Vector2 ButtonCentreForSelfTest(AreaBarButton button) => _buttons[button].GetGlobalRect().GetCenter();
    internal Rect2 StripRectForSelfTest(AreaBarStrip strip) => _strips[strip].GetGlobalRect();
    private const string FrameMeta = "bar_frame";

    public override void _Ready()
    {
        // Over the windows (layer 4), which the bar buttons open and close: a centred window must not cover a button. The host hides
        // the bars under the windows they cannot reach (conversation, barter, save list, reading, menu).
        Layer = 5;
        foreach (var strip in new[] { AreaBarStrip.Top, AreaBarStrip.Bottom })
        {
            var (_, _, width, height) = AreaGameBarLayout.StripRect(strip);
            var root = new Control { Name = strip + "Strip", MouseFilter = Control.MouseFilterEnum.Stop, Size = new Vector2(width, height), TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
            root.AddChild(new ColorRect { Color = Colors.Black, Size = new Vector2(width, height), MouseFilter = Control.MouseFilterEnum.Ignore });
            root.AddChild(new TextureRect { Name = "Art", MouseFilter = Control.MouseFilterEnum.Ignore });
            _strips[strip] = root;
            AddChild(root);
        }
        // Under the buttons: the message window's scroll areas lie between them (ARC-UI-023).
        _messageLog = new AreaMessageLogView();
        _strips[AreaBarStrip.Bottom].AddChild(_messageLog);
        foreach (var spec in AreaGameBarLayout.Buttons)
        {
            var (left, top, _, _) = AreaGameBarLayout.StripRect(spec.Strip);
            var rect = new TextureRect { Name = spec.Button.ToString(), Position = new Vector2(spec.X - left, spec.Y - top), MouseFilter = Control.MouseFilterEnum.Stop };
            var button = spec.Button;
            rect.MouseEntered += () => Raise(_bar.Enter(button));
            rect.MouseExited += () => Raise(_bar.Exit(button));
            rect.GuiInput += input => ButtonInput(button, rect, input);
            _buttons[button] = rect;
            _strips[spec.Strip].AddChild(rect);
        }
        GetViewport().SizeChanged += Place;
        VisibilityChanged += Place;
        Place();
        Render();
    }

    /// <summary>Gives the panel its textures (by interface art number, each art's frames), the bar state and the message log it draws.</summary>
    public void Bind(IReadOnlyDictionary<int, ImageTexture[]> art, AreaGameBar bar, AreaMessageLog log)
    {
        _art = art ?? throw new ArgumentNullException(nameof(art));
        _bar = bar ?? throw new ArgumentNullException(nameof(bar));
        _messageLog.Bind(log ?? throw new ArgumentNullException(nameof(log)), art);
        if (IsNodeReady()) Render();
    }

    /// <summary>ARC-UI-027: puts <paramref name="part"/> on <paramref name="strip"/>, in the strip's own frame (scaled and placed with it),
    /// above the strip's art and buttons.</summary>
    public void AddPart(AreaBarStrip strip, Control part) => _strips[strip].AddChild(part ?? throw new ArgumentNullException(nameof(part)));

    public override void _Input(InputEvent input)
    {
        if (input is not InputEventMouseMotion motion || !Visible) return;
        var strip = _strips[AreaBarStrip.Bottom];
        PointerMoved?.Invoke(strip.GetGlobalTransform().AffineInverse() * motion.Position + new Vector2(0, AreaGameBarLayout.BottomTop));
    }

    private void ButtonInput(AreaBarButton button, TextureRect rect, InputEvent input)
    {
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } click) return;
        rect.AcceptEvent();
        // Godot sends the release to the control that took the press; it is over the button when it lands inside it.
        if (click.Pressed) Raise(_bar.Press(button));
        else Raise(_bar.Release(new Rect2(Vector2.Zero, rect.Size).HasPoint(click.Position) ? button : null));
    }

    private void Raise(AreaBarEffect effect)
    {
        Render();
        if (effect.Sounds.Count > 0 || effect.Act is not null || effect.HoverChanged) Effect?.Invoke(effect);
    }

    /// <summary>Redraws the button frames from the bar state and the message window from the log.</summary>
    public void Render()
    {
        foreach (var strip in _strips)
        {
            var art = strip.Value.GetNode<TextureRect>("Art");
            art.Texture = _art.TryGetValue(AreaGameBarLayout.StripArt(strip.Key), out var frames) && frames.Length > 0 ? frames[0] : null;
        }
        foreach (var spec in AreaGameBarLayout.Buttons)
        {
            var rect = _buttons[spec.Button];
            if (!_art.TryGetValue(spec.Art, out var frames) || frames.Length == 0) { rect.Texture = null; rect.Size = new Vector2(32, 32); rect.RemoveMeta(FrameMeta); continue; }
            var frame = AreaGameBarLayout.Frame(_bar.Look(spec.Button), frames.Length);
            rect.Texture = frames[frame];
            rect.Size = frames[frame].GetSize();
            rect.SetMeta(FrameMeta, frame);
        }
        _messageLog.Render();
    }

    private void Place()
    {
        var size = GetViewport().GetVisibleRect().Size;
        Factor = Math.Max(1, (int)Math.Floor(Math.Min(size.X / AreaGameBarLayout.ScreenWidth, size.Y / AreaGameBarLayout.ScreenHeight)));
        var left = MathF.Round((size.X - AreaGameBarLayout.ScreenWidth * Factor) / 2);
        _strips[AreaBarStrip.Top].Position = new Vector2(left, 0);
        _strips[AreaBarStrip.Bottom].Position = new Vector2(left, size.Y - AreaGameBarLayout.BottomHeight * Factor);
        foreach (var strip in _strips.Values) strip.Scale = new Vector2(Factor, Factor);
        SetInsets(Visible ? AreaGameBarLayout.TopHeight * Factor : 0, Visible ? AreaGameBarLayout.BottomHeight * Factor : 0);
    }

    private static void SetInsets(float top, float bottom)
    {
        if (TopInset == top && BottomInset == bottom) return;
        TopInset = top; BottomInset = bottom;
        InsetsChanged?.Invoke();
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= Place;
        SetInsets(0, 0);
    }
}

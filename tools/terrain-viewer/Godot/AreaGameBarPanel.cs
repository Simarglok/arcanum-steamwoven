using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-018: the original's in-game interface bars drawn with the game's own art: the top strip (Character, Logbook, Map,
/// Inventory, Fate Points, Sleep) at the top of the window and the bottom strip (Combat, Skills, Spells, Schematics) at its bottom,
/// each centred and scaled by the largest whole factor at which the original's 800×600 screen fits the window. A strip takes every
/// press on it (the original's world view never reaches under the bars); its buttons follow <see cref="AreaGameBar"/>, and the hover
/// name is written where the original's message window draws it. The bottom strip's other parts (message log, hot keys, recent
/// actions, vials) are drawn as the art shows them and do nothing yet.
/// </summary>
public partial class AreaGameBarPanel : CanvasLayer
{
    private readonly Dictionary<AreaBarButton, TextureRect> _buttons = [];
    private readonly Dictionary<AreaBarStrip, Control> _strips = [];
    private IReadOnlyDictionary<int, ImageTexture[]> _art = new Dictionary<int, ImageTexture[]>();
    private AreaGameBar _bar = new();
    private AreaGameBarTexts _texts = AreaGameBarTexts.Project("not read");
    private Label _hover = null!;

    /// <summary>A pointer event on the bars changed something: the host plays the sounds and runs the button's function.</summary>
    public event Action<AreaBarEffect>? Effect;

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
    internal string HoverTextForSelfTest => _hover.Visible ? _hover.Text : "";
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
        var (x, y, w, h) = AreaGameBarLayout.HoverText;
        _hover = new Label
        {
            Name = "HoverName", Position = new Vector2(x, y - AreaGameBarLayout.BottomTop), Size = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center, Visible = false, Modulate = new Color(1f, .93f, .75f),
        };
        _strips[AreaBarStrip.Bottom].AddChild(_hover);
        GetViewport().SizeChanged += Place;
        VisibilityChanged += Place;
        Place();
        Render();
    }

    /// <summary>Gives the panel its textures (by interface art number, each art's frames), its texts and the bar state it draws.</summary>
    public void Bind(IReadOnlyDictionary<int, ImageTexture[]> art, AreaGameBarTexts texts, AreaGameBar bar)
    {
        _art = art ?? throw new ArgumentNullException(nameof(art));
        _texts = texts ?? throw new ArgumentNullException(nameof(texts));
        _bar = bar ?? throw new ArgumentNullException(nameof(bar));
        if (IsNodeReady()) Render();
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

    /// <summary>Redraws the button frames and the hover name from the bar state.</summary>
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
        _hover.Visible = _bar.HoverMessage is not null;
        _hover.Text = _bar.HoverMessage is { } message ? _texts.Text(message) : "";
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

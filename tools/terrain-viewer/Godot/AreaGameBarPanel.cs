using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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
    private IReadOnlyDictionary<int, Texture2D[]> _art = new Dictionary<int, Texture2D[]>();
    private AreaGameBar _bar = new();
    private AreaMessageLogView _messageLog = null!;
    // ARC-UI-024: the hovered critter's portrait and name in the message window (PA6).
    private TextureRect _hoverImage = null!;
    private float _sideLeft, _sideRight;
    // ARC-UI-1111: the clock window of the top strip (GB8) and the face the host last gave it.
    private AreaClockView _clock = null!;
    private AreaClockFace? _clockFace;
    // ARC-UI-1365: the experience progress strip of the bottom strip (GB13) and the progress the host last gave it.
    private AreaExperienceStripView _experience = null!;
    private int? _experienceProgress;

    /// <summary>A pointer event on the bars changed something: the host plays the sounds and runs the button's function.</summary>
    public event Action<AreaBarEffect>? Effect;
    /// <summary>The pointer moved while the bars are shown: its place on the original's 800×600 screen (ARC-UI-023: the hover name is
    /// written again on each move, and a move over the bottom strip gives the message window back to a pane, ML2/ML8).</summary>
    public event Action<Vector2>? PointerMoved;

    /// <summary>ARC-UI-1111: the clock window's last draw: its slices, whether the needle was drawn and the moon art in use.</summary>
    internal (IReadOnlyList<AreaClockSlice> Slices, bool Needle, int? MoonArt) ClockForSelfTest => (_clock.SlicesForSelfTest, _clock.NeedleForSelfTest, _clock.MoonArtForSelfTest);
    /// <summary>The clock window's centre on the screen (its hit area), for the self-test.</summary>
    internal Vector2 ClockCentreForSelfTest => _clock.GetGlobalRect().GetCenter();
    internal Rect2 ClockRectForSelfTest => _clock.GetGlobalRect();
    /// <summary>Hides the clock window to see the strip's art under it (the graphics report's "before"); a hidden window takes no pointer.</summary>
    internal bool ClockShownForSelfTest { get => _clock.Visible; set => _clock.Visible = value; }

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
    /// <summary>The message window's hovered portrait (its texture, null none).</summary>
    internal (Texture2D? Image, Rect2 Rect) HoverPortraitForSelfTest => (_hoverImage.Visible ? _hoverImage.Texture : null, _hoverImage.GetGlobalRect());

    /// <summary>The whole factor at which the original's 800 × 600 screen fits a window of <paramref name="size"/> (at least 1).</summary>
    public static int ScreenFactor(Vector2 size) => AreaUiScale.ScreenFactor(size.X, size.Y);

    /// <summary>
    /// ARC-UI-127: the window y of a window of <paramref name="heightUnits"/> original pixels that sits between the strips. In the original's
    /// 800 × 600 screen it ends exactly where the bottom strip begins (the sheet: 41 + 400 = 441); a window taller than 600 × factor leaves free
    /// height between the strips, and the window stays on the bottom strip instead of hanging under the top strip with a gap above the HUD.
    /// With the bars hidden the window keeps the top inset (0).
    /// </summary>
    public static float DockedWindowTop(Vector2 size, float factor, int heightUnits) =>
        BottomInset > 0 ? MathF.Max(TopInset, MathF.Round(size.Y - BottomInset - heightUnits * factor)) : TopInset;

    /// <summary>
    /// ARC-UI-132: the scale and position of a window of <paramref name="widthUnits"/> × <paramref name="heightUnits"/> original pixels that
    /// fills the whole height between the strips and keeps its aspect ratio (the free height at 16:9 is wider than the window, so only the side
    /// margins stay). The scale is the free height over the window's (at most the window width over its width), never below the bars' whole
    /// factor. A redrawn 4× art keeps its quality at a fractional scale (mipmapped linear sampling), so with <paramref name="fractional"/> the
    /// scale is exact; the original's bitmaps stay on a whole scale (the largest that fits, standing on the bottom strip as ARC-UI-127 set).
    /// With the bars hidden the bars' factor and the top inset apply, as before.
    /// </summary>
    public static (float Scale, Vector2 Position) FittedWindow(Vector2 size, int widthUnits, int heightUnits, bool fractional)
    {
        var factor = ScreenFactor(size);
        if (BottomInset <= 0) return (factor, new Vector2(ScreenLeft(size, factor), TopInset));
        var fit = MathF.Min((size.Y - TopInset - BottomInset) / heightUnits, size.X / widthUnits);
        var scale = MathF.Max(factor, fractional ? fit : MathF.Floor(fit));
        return (scale, new Vector2(MathF.Round((size.X - widthUnits * scale) / 2), DockedWindowTop(size, scale, heightUnits)));
    }

    /// <summary>The window x of the original screen's left edge, centred at <paramref name="factor"/>.</summary>
    public static float ScreenLeft(Vector2 size, int factor) => MathF.Round((size.X - AreaGameBarLayout.ScreenWidth * factor) / 2);

    /// <summary>ARC-UI-024: frame <paramref name="frame"/> of an interface art the bars read (the vials' art among them), or null.</summary>
    public Texture2D? ArtFrame(int number, int frame = 0) => _art.TryGetValue(number, out var frames) && frame >= 0 && frame < frames.Length ? frames[frame] : null;
    /// <summary>ARC-MAGIC-026: every frame of an interface art the bars read (the maintain bar's slot and plugged arts), or null.</summary>
    public Texture2D[]? ArtFrames(int number) => _art.GetValueOrDefault(number);
    /// <summary>ARC-UI-1364: the art number a button was last drawn with (its lit or map-face art included), -1 when it drew none.</summary>
    internal int ArtForSelfTest(AreaBarButton button) => _buttons.TryGetValue(button, out var rect) && rect.HasMeta(ArtMeta) ? (int)rect.GetMeta(ArtMeta) : -1;
    private const string ArtMeta = "bar_art";
    internal int FrameForSelfTest(AreaBarButton button) => _buttons.TryGetValue(button, out var rect) && rect.HasMeta(FrameMeta) ? (int)rect.GetMeta(FrameMeta) : -1;
    internal Vector2 ButtonCentreForSelfTest(AreaBarButton button) => _buttons[button].GetGlobalRect().GetCenter();
    internal Rect2 StripRectForSelfTest(AreaBarStrip strip) => _strips[strip].GetGlobalRect();
    private const string FrameMeta = "bar_frame";
    private const ulong DownMinimumMs = 80;
    private readonly Dictionary<AreaBarButton, DownFeedback> _downFeedback = new()
    {
        [AreaBarButton.Combat] = new(),
        [AreaBarButton.Character] = new(),
        [AreaBarButton.Inventory] = new(),
        [AreaBarButton.Logbook] = new(),
        [AreaBarButton.Schematics] = new(),
        [AreaBarButton.FatePoints] = new(),
        [AreaBarButton.Sleep] = new(),
    };

    private sealed class DownFeedback
    {
        public ulong? DrawnSince { get; set; }
        public bool Released { get; set; }
        public bool MinimumElapsed(ulong now) => DrawnSince is { } since && now - since >= DownMinimumMs;
    }

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
            root.AddChild(new AreaStripSidesView(strip) { Name = "Sides" });
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
        // ARC-UI-1111: the clock window, with the invisible button over it (GB9): it follows the same pointer rules, silently.
        _clock = new AreaClockView();
        _clock.MouseEntered += () => Raise(_bar.Enter(AreaBarButton.Clock));
        _clock.MouseExited += () => Raise(_bar.Exit(AreaBarButton.Clock));
        _clock.GuiInput += input => ButtonInput(AreaBarButton.Clock, _clock, input);
        _strips[AreaBarStrip.Top].AddChild(_clock);
        _experience = new AreaExperienceStripView();
        _strips[AreaBarStrip.Bottom].AddChild(_experience);
        var (ix, iy) = AreaGameBarLayout.HoverImage;
        _hoverImage = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Keep,
            Name = "HoverPortrait", Position = new Vector2(ix, iy - AreaGameBarLayout.BottomTop), MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = new Vector2(AreaGameBarLayout.HoverPortraitSize, AreaGameBarLayout.HoverPortraitSize), Visible = false,
        };
        _strips[AreaBarStrip.Bottom].AddChild(_hoverImage);
        GetViewport().SizeChanged += Place;
        VisibilityChanged += OnVisibilityChanged;
        RenderingServer.Singleton.FramePostDraw += RecordDownDraw;
        Place();
        Render();
    }

    /// <summary>Gives the panel its textures (by interface art number, each art's frames), the bar state and the message log it draws.</summary>
    public void Bind(IReadOnlyDictionary<int, Texture2D[]> art, AreaGameBar bar, AreaMessageLog log)
    {
        _art = art ?? throw new ArgumentNullException(nameof(art));
        _bar = bar ?? throw new ArgumentNullException(nameof(bar));
        _messageLog.Bind(log ?? throw new ArgumentNullException(nameof(log)), art);
        if (IsNodeReady()) Render();
    }

    /// <summary>ARC-UI-024: shows a hovered critter's portrait (drawn at its own size from the image's top left) in the message window, as
    /// the original's hovered-object panel does for the PC and its followers with a portrait (PA6); null hides it. ARC-UI-057: the panel's
    /// name and bars are the message window's object window (<see cref="AreaMessageKind.Object"/>), drawn under the portrait.</summary>
    public void ShowHoverPortrait(Texture2D? portrait)
    {
        _hoverImage.Texture = portrait;
        _hoverImage.Visible = portrait is not null;
    }

    /// <summary>ARC-UI-027: puts <paramref name="part"/> on <paramref name="strip"/>, in the strip's own frame (scaled and placed with it),
    /// above the strip's art and buttons.</summary>
    public void AddPart(AreaBarStrip strip, Control part) => _strips[strip].AddChild(part ?? throw new ArgumentNullException(nameof(part)));

    /// <summary>ARC-UI-053: puts <paramref name="part"/> on the bottom strip under the message window's drawing (a pane's name is written
    /// over it) and above the strip's art.</summary>
    public void AddPartUnderMessageLog(Control part)
    {
        var strip = _strips[AreaBarStrip.Bottom];
        strip.AddChild(part ?? throw new ArgumentNullException(nameof(part)));
        strip.MoveChild(part, _messageLog.GetIndex());
    }

    public override void _Input(InputEvent input)
    {
        if (input is not InputEventMouseMotion motion || !Visible) return;
        var strip = _strips[AreaBarStrip.Bottom];
        PointerMoved?.Invoke(strip.GetGlobalTransform().AffineInverse() * motion.Position + new Vector2(0, AreaGameBarLayout.BottomTop));
    }

    /// <summary>ARC-UI-1111: the sky the clock window shows (null: black), drawn from the clock arts the panel was given.</summary>
    public void SetClock(AreaClockFace? face)
    {
        _clockFace = face;
        RenderClock();
    }

    /// <summary>ARC-UI-1365: the hero's progress toward the next level in thousandths (null: no strip pieces), drawn from the strip's arts.</summary>
    public void SetExperience(int? progress)
    {
        _experienceProgress = progress;
        _experience.Set(progress, _art);
    }

    internal IReadOnlyList<(AreaExperiencePiece Piece, int Width)> ExperienceForSelfTest => _experience.DrawnForSelfTest;
    internal bool ExperienceShownForSelfTest { get => _experience.Visible; set => _experience.Visible = value; }

    private void RenderClock() =>
        _clock.Set(_clockFace, ArtFrame(AreaClockLayout.StripArt), _clockFace is { } face ? ArtFrame(face.MoonArt) : null, ArtFrame(AreaClockLayout.NeedleArt));

    private void ButtonInput(AreaBarButton button, Control rect, InputEvent input)
    {
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } click) return;
        rect.AcceptEvent();
        // Godot sends the release to the control that took the press; it is over the button when it lands inside it.
        if (click.Pressed)
        {
            if (_downFeedback.TryGetValue(button, out var feedback))
            {
                feedback.DrawnSince = null;
                feedback.Released = false;
            }
            Raise(_bar.Press(button));
            return;
        }

        var effect = _bar.Release(new Rect2(Vector2.Zero, rect.Size).HasPoint(click.Position) ? button : null);
        // A press and release can arrive before the same draw. Keep the visual feedback without delaying the action.
        if (effect.Act is { } acted && _downFeedback.TryGetValue(acted, out var released))
            released.Released = !released.MinimumElapsed(Time.GetTicksMsec());
        Raise(effect);
    }

    private void RecordDownDraw()
    {
        if (!Visible) return;
        var now = Time.GetTicksMsec();
        foreach (var (button, feedback) in _downFeedback)
            if (feedback.DrawnSince is null && (feedback.Released || _bar.Look(button) == AreaBarButtonLook.Down))
                feedback.DrawnSince = now;
    }

    public override void _Process(double delta)
    {
        var changed = false;
        var now = Time.GetTicksMsec();
        foreach (var feedback in _downFeedback.Values)
        {
            if (!feedback.Released || !feedback.MinimumElapsed(now)) continue;
            feedback.Released = false;
            changed = true;
        }
        if (changed) Render();
    }

    private void OnVisibilityChanged()
    {
        if (!Visible)
        {
            foreach (var feedback in _downFeedback.Values)
            {
                feedback.DrawnSince = null;
                feedback.Released = false;
            }
            Render();
        }
        Place();
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
            strip.Value.GetNode<AreaStripSidesView>("Sides").Set(art.Texture, _sideLeft, _sideRight);
        }
        foreach (var spec in AreaGameBarLayout.Buttons)
        {
            var rect = _buttons[spec.Button];
            // ARC-UI-1364: the art the game's light callbacks left on the button (lit, or the map's face); the layout's own art when that one did not read.
            var shown = _bar.Lights.ArtOf(spec.Button) is { } lightArt && _art.TryGetValue(lightArt, out var lightFrames) && lightFrames.Length > 0 ? lightArt : spec.Art;
            if (!_art.TryGetValue(shown, out var frames) || frames.Length == 0) { rect.Texture = null; rect.Size = new Vector2(32, 32); rect.RemoveMeta(FrameMeta); rect.RemoveMeta(ArtMeta); continue; }
            var look = _downFeedback.TryGetValue(spec.Button, out var feedback) && feedback.Released
                ? AreaBarButtonLook.Down : _bar.Look(spec.Button);
            var frame = AreaGameBarLayout.Frame(look, frames.Length);
            rect.Texture = frames[frame];
            rect.Size = frames[frame].GetSize();
            rect.SetMeta(FrameMeta, frame);
            rect.SetMeta(ArtMeta, shown);
        }
        RenderClock();
        _experience.Set(_experienceProgress, _art);
        _messageLog.Render();
    }

    private void Place()
    {
        var size = GetViewport().GetVisibleRect().Size;
        Factor = ScreenFactor(size);
        var left = ScreenLeft(size, Factor);
        _strips[AreaBarStrip.Top].Position = new Vector2(left, 0);
        _strips[AreaBarStrip.Bottom].Position = new Vector2(left, size.Y - AreaGameBarLayout.BottomHeight * Factor);
        foreach (var strip in _strips.Values) strip.Scale = new Vector2(Factor, Factor);
        // ARC-UI-141: the generated chrome is painted at the exact device scale the strips and buttons are drawn at (factor times the viewport's own scale).
        InterfaceHd.SetGeneratedScale(Factor * GetViewport().GetFinalTransform().Scale.X);
        // ARC-UI-089: the free width beside the centred strip, in UI units (filled with HD strip tiles while the HD profile is on).
        _sideLeft = left / Factor; _sideRight = (size.X - left - AreaGameBarLayout.ScreenWidth * Factor) / Factor;
        foreach (var strip in _strips)
            strip.Value.GetNode<AreaStripSidesView>("Sides").Set(strip.Value.GetNode<TextureRect>("Art").Texture, _sideLeft, _sideRight);
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
        RenderingServer.Singleton.FramePostDraw -= RecordDownDraw;
        GetViewport().SizeChanged -= Place;
        SetInsets(0, 0);
    }
}

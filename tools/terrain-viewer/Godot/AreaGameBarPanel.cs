using Arcanum.Presentation.Steamwoven;
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
    // ARC-UI-2452: the Steamwoven prototype layout (generated interface on): the plan in use (null: the whole-factor layout), the boards that continue the two strips to the frame's
    // sides, the black bars of a letterboxed or pillarboxed window and the backdrop an open menu gets over the middle band.
    private SteamwovenHudPlan? _plan;
    private Control _boards = null!, _bars = null!;
    private SteamwovenBoardView _topBoard = null!, _bottomBoard = null!;
    // ARC-UI-2576: the backdrop of the open menu window's family: one view per piece (two for a two-part window, SWI-BDR-5).
    private readonly SteamwovenBoardView[] _backdrops = new SteamwovenBoardView[2];
    private CanvasLayer _backdropLayer = null!;
    private readonly ColorRect[] _barRects = [new() { Name = "BarLeft" }, new() { Name = "BarRight" }, new() { Name = "BarTop" }, new() { Name = "BarBottom" }];
    private SteamwovenBackdropFamily? _backdropFamily;

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

    /// <summary>ARC-UI-2510: the left edge of the frame the Steamwoven layout lays the play screen out in, while the bars are shown (0 otherwise, and in a window with no pillarbox bar).</summary>
    public static float FreeLeft { get; private set; }
    private static float _freeWidth;

    /// <summary>The part of a window of <paramref name="size"/> the bars leave free: its top and its size. ARC-UI-2510: in the Steamwoven layout the free width is the frame's (a window wider than the
    /// 16:9 frame has a pillarbox bar on each side, which a window must not lie under); it starts at <see cref="FreeLeft"/>. Without the layout it is the whole window's width.</summary>
    public static (float Top, Vector2 Size) FreeArea(Vector2 size) => (TopInset, new(_freeWidth > 0 ? _freeWidth : size.X, Math.Max(1, size.Y - TopInset - BottomInset)));

    /// <summary>ARC-UI-2603: the rectangle in window pixels the strips and the pillarbox bars leave free, for what must stay clear of them (a text bubble's placement). In the Steamwoven layout it is
    /// the frame's width from <see cref="FreeLeft"/> between the strips; without the layout (the generated interface off) the strips are the original's and the whole window stands, as before.</summary>
    public static Rect2 FreeRect(Vector2 size)
    {
        if (_freeWidth <= 0) return new Rect2(Vector2.Zero, size);
        var (top, free) = FreeArea(size);
        return new Rect2(FreeLeft, top, free.X, free.Y);
    }

    /// <summary>ARC-UI-2510: where the original's 800 × 600 screen lies for what hangs from its top strip (the follower portraits and their buttons): the screen's top left, the strips' scale, and
    /// the window y of the bottom strip's bottom edge (the screen's bottom edge). Null while the layout is not the Steamwoven one or the bars are hidden: the whole-factor screen centred at the
    /// window's top stands. <see cref="ScreenFrame"/> is the same screen with its origin counted from the bottom strip (for what sits on that strip).</summary>
    public static (Vector2 Origin, float Scale, float BottomEdge)? TopScreen { get; private set; }

    /// <summary>ARC-UI-2510: raised after <see cref="TopScreen"/> changed (the layout turned on or off, a new window size): parts placed by it place themselves again.</summary>
    public static event Action? TopScreenChanged;

    private static void SetTopScreen((Vector2, float, float)? next)
    {
        if (TopScreen == next) return;
        TopScreen = next;
        TopScreenChanged?.Invoke();
    }

    /// <summary>
    /// ARC-UI-2510: where a window of the original's screen that keeps its own whole scale (the sleep and fate views) lies: its top left at (<paramref name="screenX"/>, <paramref name="screenY"/>)
    /// of the screen where the strips put it, and the whole factor as its scale (the strips' fractional scale is theirs: the window's textures are painted at the window's own).
    /// Without the Steamwoven layout: the whole-factor screen centred at the window's top.
    /// </summary>
    public static (Vector2 Position, float Scale) OwnScaleWindow(Vector2 size, int screenX, int screenY)
    {
        var factor = ScreenFactor(size);
        return TopScreen is { } screen
            ? (screen.Origin + new Vector2(screenX, screenY) * screen.Scale, factor)
            : (new Vector2(ScreenLeft(size, factor) + screenX * factor, screenY * factor), factor);
    }

    /// <summary>ARC-UI-2510: the scale the original's screen pixels (floating lines, text bubbles, the follower portraits) are drawn at: the strips' scale in the Steamwoven layout, else the whole factor.</summary>
    public static float ScreenScale(Vector2 size) => TopScreen?.Scale ?? ScreenFactor(size);
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
        // ARC-UI-2575: the Steamwoven layout puts every window in the window zone at the layout's unit scale, bars shown or not.
        if (ZoneWindow(size, widthUnits, heightUnits) is { } zone) return ((float)zone.Scale, new Vector2((float)zone.X, (float)zone.Y));
        var factor = ScreenFactor(size);
        if (BottomInset <= 0) return (factor, new Vector2(ScreenLeft(size, factor), TopInset));
        // ARC-UI-2510: the free width is the frame's, from its left edge.
        var (_, free) = FreeArea(size);
        var fit = MathF.Min((size.Y - TopInset - BottomInset) / heightUnits, free.X / widthUnits);
        var scale = MathF.Max(factor, fractional ? fit : MathF.Floor(fit));
        return (scale, new Vector2(FreeLeft + MathF.Round((free.X - widthUnits * scale) / 2), DockedWindowTop(size, scale, heightUnits)));
    }

    /// <summary>
    /// ARC-UI-2575: the place of a menu window of <paramref name="widthUnits"/> × <paramref name="heightUnits"/> original units in the window zone of the Steamwoven layout (at the layout's unit scale `f · 3.6`),
    /// or null while that layout is not in use (the HD option or the generated interface off: the window keeps its own whole or fitted scale). The one hook every menu window reads; it does not
    /// depend on whether the bars show, so the windows that hide them (loot, barter, reading, save and load) follow the same zone.
    /// </summary>
    public static SteamwovenWindowPlace? ZoneWindow(Vector2 size, int widthUnits, int heightUnits) =>
        SteamwovenOn ? SteamwovenWindowZone.Body((int)size.X, (int)size.Y, widthUnits, heightUnits) : null;

    /// <summary>ARC-UI-2575: the place of an 800 × 600 original screen whose 800 × 400 body (at <paramref name="bodyTopUnits"/> from its top) lies in the window zone, or null (see <see cref="ZoneWindow"/>).</summary>
    public static SteamwovenWindowPlace? ZoneScreen(Vector2 size, int bodyTopUnits) =>
        SteamwovenOn ? SteamwovenWindowZone.Screen((int)size.X, (int)size.Y, bodyTopUnits) : null;

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
        // ARC-UI-2452: under the strips, the boards of the Steamwoven layout (shown only while that layout is in use).
        _boards = new Control { Name = "Boards", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _topBoard = new SteamwovenBoardView { Name = "TopBoard" }; _bottomBoard = new SteamwovenBoardView { Name = "BottomBoard" };
        _boards.AddChild(_topBoard); _boards.AddChild(_bottomBoard);
        AddChild(_boards); MoveChild(_boards, 0);
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
        // ARC-UI-2452: over everything of the panel, the bars that leave the world out of the frame; a menu's backdrop is its own layer under the windows (layer 3).
        _bars = new Control { Name = "Bars", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        foreach (var bar in _barRects) { bar.Color = Colors.Black; bar.MouseFilter = Control.MouseFilterEnum.Stop; _bars.AddChild(bar); }
        AddChild(_bars);
        _backdropLayer = new CanvasLayer { Name = "MenuBackdrop", Layer = 3, Visible = false };
        for (var i = 0; i < _backdrops.Length; i++)
        {
            _backdrops[i] = new SteamwovenBoardView { Name = i == 0 ? "Backdrop" : "BackdropRight", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
            _backdropLayer.AddChild(_backdrops[i]);
            _backdrops[i].Painted += () => { if (_plan is { } painted) UpdateSteamwovenParts(painted); };
        }
        AddChild(_backdropLayer);
        GetViewport().SizeChanged += Place;
        InterfaceHd.Changed += Place;
        AreaRoundView.RectsChanged += ApplyBackdropHoles;
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
        if (_plan is { } plan) UpdateSteamwovenParts(plan);
    }

    /// <summary>ARC-UI-2452: true while the Steamwoven layout (the layout model of docs/ui/steamwoven-interface-trd.md) is the one in use: the HD option with the generated interface on.</summary>
    public static bool SteamwovenOn => InterfaceHd.Enabled && InterfaceHd.Generated && !SteamwovenHud.Suspended;

    /// <summary>
    /// ARC-UI-2452: where the original's 800 × 600 screen lies while the bars are shown, for the gauges and lamps drawn over the strips in their coordinates: its top left and its scale
    /// (the whole factor with the screen centred and standing on the window's bottom, or the Steamwoven strip scale with the screen where the strips put it).
    /// </summary>
    public (Vector2 Origin, float Scale) ScreenFrame(Vector2 size)
    {
        if (_plan is { } plan)
        {
            var scale = (float)plan.StripScale;
            return (new Vector2(plan.StripLeft, plan.BottomStripY - AreaGameBarLayout.BottomTop * scale), scale);
        }
        return (new Vector2(ScreenLeft(size, Factor), size.Y - AreaGameBarLayout.ScreenHeight * Factor), Factor);
    }

    /// <summary>ARC-UI-2452: the plan the panel is laid out by now (null: the original's whole-factor layout).</summary>
    internal SteamwovenHudPlan? PlanForSelfTest => _plan;
    /// <summary>The scale the strips are drawn at, the same in both strips (the whole <see cref="Factor"/> unless the Steamwoven layout is on).</summary>
    public float StripScale => _strips[AreaBarStrip.Top].Scale.X;
    /// <summary>True while a board of the Steamwoven layout is painted on a worker.</summary>
    internal bool BoardsPendingForSelfTest => _topBoard.Pending || _bottomBoard.Pending || _backdrops.Any(backdrop => backdrop.Pending);
    internal (SteamwovenBoardView Top, SteamwovenBoardView Bottom, bool BackdropShown) BoardsForSelfTest => (_topBoard, _bottomBoard, _backdropLayer.Visible);
    /// <summary>ARC-UI-2576: the family the backdrop is for (null: none asked) and the views of its pieces that show now, with the pixel size of each one's painted texture.</summary>
    internal (SteamwovenBackdropFamily? Family, IReadOnlyList<(Vector2 Position, Vector2I Pixels)> Shown) BackdropForSelfTest =>
        (_backdropFamily, [.. _backdrops.Where(backdrop => backdrop.Visible).Select(backdrop => (backdrop.Position, backdrop.PixelSizeForSelfTest))]);
    /// <summary>ARC-UI-2576: how many round-view squares the first backdrop piece leaves out.</summary>
    internal int BackdropHolesForSelfTest => _backdrops[0].HoleCount;
    internal IReadOnlyList<ColorRect> BarsForSelfTest => _barRects;
    internal bool BoardsShownForSelfTest => _boards.Visible;

    /// <summary>ARC-UI-2452, ARC-UI-2576: an open menu window gets the generated backdrop of its family over the whole middle band (layer 3, under the windows); null for no menu window. The host says it every frame.</summary>
    public void SetMenuBackdrop(SteamwovenBackdropFamily? family)
    {
        if (_backdropFamily == family) return;
        _backdropFamily = family;
        if (_plan is { } plan) UpdateSteamwovenParts(plan); else _backdropLayer.Visible = false;
    }

    /// <summary>ARC-UI-2452: places the bars again (the self-test turned the Steamwoven layout on or off, or gave it other panel heights).</summary>
    internal void Relayout() => Place();

    private void Place()
    {
        var size = GetViewport().GetVisibleRect().Size;
        // Only while the bars show: windows that hide them (the full-screen views) keep the whole-factor scale for their own textures.
        var plan = SteamwovenOn && Visible ? SteamwovenHud.Plan((int)size.X, (int)size.Y) : null;
        _plan = plan;
        if (plan is not null) { PlaceSteamwoven(size, plan); return; }
        // Not the Steamwoven layout (the HD option off, or the generated interface off): nothing of it shows and the original's whole-factor layout stands.
        InterfaceHd.SetFractionalScale(null);
        SetTopScreen(null);
        if (_boards.Visible) { _boards.Visible = false; _bars.Visible = false; _backdropLayer.Visible = false; }
        foreach (var strip in _strips.Values) strip.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
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
        SetInsets(Visible ? AreaGameBarLayout.TopHeight * Factor : 0, Visible ? AreaGameBarLayout.BottomHeight * Factor : 0, 0, 0);
    }

    /// <summary>
    /// ARC-UI-2452: the strips on the layout model: one fractional scale for both (<see cref="SteamwovenHudPlan.StripScale"/>), the top strip on the bottom edge of the top band and the bottom strip
    /// hanging from the top edge of the bottom band, both centred; the insets are the middle band's, so every window that docks between the strips docks in the middle band.
    /// </summary>
    private void PlaceSteamwoven(Vector2 size, SteamwovenHudPlan plan)
    {
        Factor = ScreenFactor(size);
        var scale = (float)plan.StripScale;
        InterfaceHd.SetFractionalScale(plan.StripScale * GetViewport().GetFinalTransform().Scale.X);
        _strips[AreaBarStrip.Top].Position = new Vector2(plan.StripLeft, plan.TopStripY);
        _strips[AreaBarStrip.Bottom].Position = new Vector2(plan.StripLeft, plan.BottomStripY);
        foreach (var strip in _strips.Values) { strip.Scale = new Vector2(scale, scale); strip.TextureFilter = CanvasItem.TextureFilterEnum.Linear; }
        // The sides beside a strip are the boards' (no strip tiles).
        _sideLeft = 0; _sideRight = 0;
        foreach (var strip in _strips)
            strip.Value.GetNode<AreaStripSidesView>("Sides").Set(strip.Value.GetNode<TextureRect>("Art").Texture, 0, 0);
        // ARC-UI-2510: the screen the portraits hang in (the top strip's top left, its bottom edge) and the frame the windows are centred in.
        SetTopScreen((new Vector2(plan.StripLeft, plan.TopStripY), scale, plan.BottomStripY + AreaGameBarLayout.BottomHeight * scale));
        SetInsets(Visible ? plan.TopInset : 0, Visible ? plan.BottomInset : 0, Visible ? plan.Layout.Frame.X : 0, Visible ? plan.Layout.Frame.Width : 0);
        UpdateSteamwovenParts(plan);
    }

    private (ChromeRecipe? Recipe, byte[]? Legacy, int Width, int Height) ToneOf(AreaBarStrip strip) => ToneOfArt(AreaGameBarLayout.StripArt(strip));

    private (ChromeRecipe? Recipe, byte[]? Legacy, int Width, int Height) ToneOfArt(int art)
    {
        var frame = (ArtFrame(art) as HdInterfaceTexture)?.FrameForBoard;
        var recipe = frame?.Hd is { } query ? InterfaceChromeRecipes.For(query) : null;
        return (recipe, frame?.Rgba, frame?.Width ?? 0, frame?.Height ?? 0);
    }

    private static Rect2I Device(PixelRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private void UpdateSteamwovenParts(SteamwovenHudPlan plan)
    {
        _boards.Visible = Visible;
        _bars.Visible = Visible;
        var layout = plan.Layout;
        var scale = plan.StripScale;
        // The grain continues the strips': a board's top left is where the strip's own top left lies, in units, from the strip (negative to its left and above).
        var originX = (layout.Frame.X - plan.StripLeft) / scale;
        var (topRecipe, topLegacy, topW, topH) = ToneOf(AreaBarStrip.Top);
        var (bottomRecipe, bottomLegacy, bottomW, bottomH) = ToneOf(AreaBarStrip.Bottom);
        var top = layout.Top.Band; var bottom = layout.Bottom.Band; var middle = layout.Middle.Band;
        if (top.Height > 0)
            _topBoard.Request(new(ChromeMaterial.Wood, ChromeSides.Bottom, Device(top), scale, originX, (top.Y - plan.TopStripY) / scale, topRecipe, topLegacy, topW, topH));
        if (bottom.Height > 0)
            _bottomBoard.Request(new(ChromeMaterial.Wood, ChromeSides.Top, Device(bottom), scale, originX, (bottom.Y - plan.BottomStripY) / scale, bottomRecipe, bottomLegacy, bottomW, bottomH));
        var bars = layout.Bars; var (width, height) = ((int)GetViewport().GetVisibleRect().Size.X, (int)GetViewport().GetVisibleRect().Size.Y);
        SetBar(_barRects[0], 0, 0, bars.Left, height);
        SetBar(_barRects[1], width - bars.Right, 0, bars.Right, height);
        SetBar(_barRects[2], bars.Left, 0, width - bars.Left - bars.Right, bars.Top);
        SetBar(_barRects[3], bars.Left, height - bars.Bottom, width - bars.Left - bars.Right, bars.Bottom);
        // ARC-UI-2576: the open menu's family backdrop: one piece, or the two parts of a two-part window that meet at the split (SWI-BDR-5). It is shown once every piece's first paint is in (an unpainted
        // piece would show the world through for the paint time), and a piece the family does not use stays hidden.
        var pieces = _backdropFamily is { } family && Visible && middle.Height > 0 ? SteamwovenBackdrops.Pieces(family, plan) : [];
        var unit = SteamwovenWindowZone.UnitScale(plan);
        for (var i = 0; i < _backdrops.Length; i++)
        {
            if (i >= pieces.Count) { _backdrops[i].Visible = false; continue; }
            var piece = pieces[i];
            // ARC-UI-2643: the wood takes the tone of the window art it continues (the bottom strip's where that art is not read).
            var tone = ToneOfArt(SteamwovenBackdrops.ToneArt(_backdropFamily!.Value, i));
            if (tone.Legacy is null || tone.Recipe is null) tone = (bottomRecipe, bottomLegacy, bottomW, bottomH);
            _backdrops[i].Request(new(piece.Material, ChromeSides.None, Device(piece.Rect), unit, piece.OriginX, piece.OriginY, tone.Recipe, tone.Legacy, tone.Width, tone.Height));
            _backdrops[i].Visible = _backdrops[i].Texture is not null && _backdrops[i].PixelSizeForSelfTest == new Vector2I(piece.Rect.Width, piece.Rect.Height);
        }
        _backdropLayer.Visible = pieces.Count > 0 && _backdrops.Take(pieces.Count).All(backdrop => backdrop.Visible);
        // Holes follow the lens squares now showing; a hidden layer clears them (no stale holes for the next menu).
        if (_backdropLayer.Visible) ApplyBackdropHoles(); else foreach (var backdrop in _backdrops) backdrop.SetHoles([]);
    }

    private static void SetBar(ColorRect bar, int x, int y, int width, int height)
    {
        bar.Visible = width > 0 && height > 0;
        bar.Position = new Vector2(x, y); bar.Size = new Vector2(Math.Max(0, width), Math.Max(0, height));
    }

    private static void SetInsets(float top, float bottom, float left, float width)
    {
        if (TopInset == top && BottomInset == bottom && FreeLeft == left && _freeWidth == width) return;
        TopInset = top; BottomInset = bottom; FreeLeft = left; _freeWidth = width;
        InsetsChanged?.Invoke();
    }

    // ARC-UI-2576: the open menu's backdrop leaves out the squares of the round views (the lens of the logbook, schematics, reading, inventory and barter windows), in the frame a lens is placed.
    private void ApplyBackdropHoles()
    {
        if (!_backdropLayer.Visible) return;
        var holes = AreaRoundView.VisibleRects();
        foreach (var backdrop in _backdrops) backdrop.SetHoles(holes);
    }

    public override void _ExitTree()
    {
        AreaRoundView.RectsChanged -= ApplyBackdropHoles;
        RenderingServer.Singleton.FramePostDraw -= RecordDownDraw;
        GetViewport().SizeChanged -= Place;
        InterfaceHd.Changed -= Place;
        if (_plan is not null) InterfaceHd.SetFractionalScale(null);
        SetTopScreen(null);
        SetInsets(0, 0, 0, 0);
    }
}

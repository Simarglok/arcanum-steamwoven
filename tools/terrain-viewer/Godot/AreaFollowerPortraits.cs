using Arcanum.Presentation.Hud;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-024: one bar of a follower frame: the bar art cropped from its left to the filled width (<see cref="LifeBarRules.PortraitFill"/>),
/// or a plain rectangle without the art.
/// </summary>
public partial class AreaArtBar : Control
{
    private Texture2D? _art;
    private Color _fallback;
    private Transform2D _drawnDevice;
    public int Filled { get; private set; }
    /// <summary>The art number drawn (null for the plain fallback).</summary>
    public int? Art { get; private set; }
    public Color FillColor => _fallback;
    /// <summary>ARC-UI-3060: the rectangles the last draw made by code (<see cref="AreaThinBarCode"/>, under the HD option) instead of the art's pixels; 0 when it drew the art or nothing.</summary>
    public int CodeRectsForSelfTest { get; private set; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = CustomMinimumSize = new Vector2(AreaFollowerFrameLayout.BarWidth, AreaFollowerFrameLayout.BarHeight);
        // ARC-UI-3060: the code-drawn bar follows the HD option and the device scale (a parent's scale change draws nothing by itself).
        SetNotifyTransform(true);
    }

    public override void _EnterTree()
    {
        InterfaceHd.Changed += QueueRedraw;
        GetViewport().SizeChanged += QueueRedraw; // the stretch (a window resize) moves the device pixel grid without moving the bar's own transform
    }

    public override void _ExitTree()
    {
        InterfaceHd.Changed -= QueueRedraw;
        if (GetViewport() is { } viewport) viewport.SizeChanged -= QueueRedraw;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationTransformChanged && AreaThinBarDraw.Active && IsInsideTree() && AreaThinBarDraw.Device(this) != _drawnDevice) QueueRedraw();
    }

    public void Set(int filled, int art, Texture2D? texture, Color fallback)
    {
        filled = Math.Clamp(filled, 0, AreaFollowerFrameLayout.BarWidth);
        int? drawn = texture is null ? null : art;
        if (filled == Filled && drawn == Art && texture == _art && fallback == _fallback) return;
        Filled = filled; Art = drawn; _art = texture; _fallback = fallback;
        QueueRedraw();
    }

    public override void _Draw()
    {
        CodeRectsForSelfTest = 0;
        if (Filled <= 0) return;
        var height = AreaFollowerFrameLayout.BarHeight;
        if (_art is null) { DrawRect(new Rect2(0, 0, Filled, height), _fallback); return; }
        // ARC-UI-3060: the pack has no frame for the bars, so under the HD option they are fills by value, not Legacy pixels.
        if (Art is { } art && AreaThinBarCode.Covers(art) && AreaThinBarDraw.Active)
        {
            _drawnDevice = AreaThinBarDraw.Device(this);
            CodeRectsForSelfTest = AreaThinBarDraw.Draw(this, art, 0, 0, Filled);
            return;
        }
        var width = Math.Min(Filled, _art.GetWidth());
        DrawTextureRectRegion(_art, new Rect2(0, 0, width, Math.Min(height, _art.GetHeight())), new Rect2(0, 0, width, Math.Min(height, _art.GetHeight())));
    }
}

/// <summary>
/// ARC-WORLD-046 follower portraits: one frame per follower in the PC's array (the original's portrait list; waiting NPCs have none,
/// OL16), each opening, on the right button's release, the order menu of the original's portrait (art 826 and eight rows: walk, attack, stay
/// close, spread out, back off, wait, inventory, character sheet).
/// ARC-UI-020 (ledger CH9): each frame's health bar (in its poisoned art while the follower is poisoned) and fatigue bar are cropped
/// to the follower's percent (<see cref="LifeBarRules.PortraitFill"/>); below 20 % health the portrait turns red
/// (<see cref="LifeBarRules.PortraitTinted"/>). A follower without combat values shows empty bars.
/// ARC-UI-024 (ledger CH9, PA6): the frames are the original's, drawn with its art at its places (<see cref="AreaFollowerFrameLayout"/>:
/// frame 503, bars 505/616 and 504, the follower's portrait at 32 × 32, the red added over it) on the 800 × 600 screen centred at the
/// interface bars' whole factor, its top at the window's top. Without the art (no game folder) the frame is a dark rectangle with the
/// name's first letters and plain bars. NOT READY: the critter art the original draws for a follower without a portrait.
/// ARC-UI-1736 (ledger FP1–FP13, <see cref="AreaFollowerPanelRules"/>): the show/hide button (art 501, 502 while hidden) and, from seven
/// followers, the two scroll arrows (arts 506–509) with the six frames showing <c>offset .. offset + 5</c>; the shown flag and the offset are
/// session-only (the original saves them, FP2; FR-LIFECYCLE-004 keeps saves for game state). The menu is the original's: art 826 at the
/// slot's right edge, eight 118 × 15 rows 18 pixels apart (Wait greyed for a mind-controlled follower, Inventory greyed when the follower
/// cannot be opened), and with the menu open every release is the menu's (FP9): a row runs after the menu closes, any other left or right
/// release closes it, and the portraits, the button and the arrows are not handled.
/// </summary>
public partial class AreaFollowerPortraits : CanvasLayer
{
    private sealed record Entry(int Slot, Button Button, ColorRect Back, TextureRect Frame, TextureRect Portrait, AreaArtLabel Initials, ColorRect Tint,
        AreaArtBar Health, AreaArtBar Fatigue);

    // ARC-UI-2795 (exe 0x56B970, fonts built at 0x56A4BD): the rows are art 229 (Flare12Font) in white, mid grey when greyed and (255, 210, 0) under the pointer (the font 0x67BB5C).
    private static readonly (byte R, byte G, byte B) MenuWhite = (255, 255, 255), MenuGrey = (128, 128, 128), MenuBright = (255, 210, 0);

    /// <summary>ARC-UI-2795: the menu rows' font (art 229); null draws no row text.</summary>
    public AreaArtFontFace? MenuFont
    {
        get => _menuFont;
        set
        {
            if (ReferenceEquals(_menuFont, value)) return;
            _menuFont = value;
            // ARC-UI-2940: the initials are drawn in this font too.
            foreach (var entry in _entries.Values) entry.Initials.Face = value;
            _chromeWords.RemoveAll(word => !IsInstanceValid(word));
            foreach (var word in _chromeWords) word.Face = value;
        }
    }

    private AreaArtFontFace? _menuFont;
    private readonly List<AreaArtLabel> _chromeWords = [];

    private Control _root = null!;
    private Control _menu = null!;
    private string? _menuEntity;
    private string _signature = "";
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly List<(FollowerMenuItem Item, Control Row, bool Greyed)> _menuRows = [];
    private IReadOnlyList<(string Entity, string Label, AreaLife? Life, Texture2D? Portrait)> _followers = [];
    private IReadOnlyDictionary<int, Texture2D> _art = new Dictionary<int, Texture2D>();
    private bool _panelShown = true;
    private int _offset;
    private Button? _toggle, _up, _down;
    private TextureRect? _toggleArt, _upArt, _downArt;
    private Vector2 _menuOrigin;
    private int _menuSlot;
    /// <summary>The width of a frame's bars, in the original's pixels (the bar art's).</summary>
    public const int BarWidth = AreaFollowerFrameLayout.BarWidth;
    private static readonly Color Back = new(14 / 255f, 16 / 255f, 22 / 255f, .85f);

    /// <summary>A menu item chosen for a follower: its entity and the item.</summary>
    public event Action<string, FollowerMenuItem>? ItemChosen;

    /// <summary>The left button was released over a follower's portrait (no menu open): a click on that follower (ARC-DEV-280, and the aim of a
    /// spell, a skill or an item in the target modes, FP6).</summary>
    public event Action<string>? PortraitClicked;

    /// <summary>ARC-UI-1281 (ledger HK26, callback 0x56A9D0, strip message type 1, code 2): the pointer entered a follower's portrait button while no
    /// order menu is open; the viewer's handler adds the dialog test (0x567400) and runs the examine.</summary>
    public event Action<string>? PortraitHovered;

    /// <summary>ARC-UI-1281 (HK26, 0x550720): the pointer left the portrait it entered (or the frames were rebuilt under it): the description and
    /// highlight go.</summary>
    public event Action? PortraitHoverLeft;

    /// <summary>ARC-UI-1736 (FP8, FP11): what the menu of a follower needs to draw its rows: whether it is mind controlled (Wait greyed) and whether
    /// its inventory can be opened (Inventory greyed otherwise). Without it the menu has no greyed row.</summary>
    public Func<string, (bool MindControlled, bool CanOpenInventory)>? MenuFacts { get; set; }

    /// <summary>ARC-UI-1786 (FP8): the rows' words from <c>mes\follower_ui.mes</c>; a row whose message is missing is not drawn. Null (nothing read yet) draws the project's English words.</summary>
    public AreaFollowerMenuTexts? MenuTexts { get; set; }

    private string? _hovered;

    internal IReadOnlyList<string> EntitiesForSelfTest { get; private set; } = [];
    private const string EntityMeta = "follower_entity";

    /// <summary>The screen centre of a follower's frame, or of an item of the open menu, for the native smoke's mouse presses.</summary>
    internal Vector2? PortraitCentreForSelfTest(string entity) =>
        _entries.TryGetValue(entity, out var entry) ? CentreOf(entry.Button) : null;
    /// <summary>A follower frame's bar fills (in the original's pixels), whether its health bar is the poisoned one, and its red tint.</summary>
    internal (int Health, int Fatigue, bool Poisoned, bool Tinted)? BarsForSelfTest(string entity) =>
        _entries.TryGetValue(entity, out var entry)
            ? (entry.Health.Filled, entry.Fatigue.Filled, entry.Health.FillColor == AreaLifeBar.PoisonedHealthColor, entry.Tint.Visible)
            : null;
    /// <summary>ARC-UI-3060: how many rectangles each of a follower frame's two bars made by code in its last draw (<see cref="AreaThinBarCode"/>, under the HD option); 0 when it drew its art or nothing.</summary>
    internal (int Health, int Fatigue)? BarCodeRectsForSelfTest(string entity) =>
        _entries.TryGetValue(entity, out var entry) ? (entry.Health.CodeRectsForSelfTest, entry.Fatigue.CodeRectsForSelfTest) : null;
    /// <summary>A follower frame's art: the frame art drawn, the portrait texture (null none), the bars' art numbers, and its and
    /// the portrait's window rectangles.</summary>
    internal (bool Frame, Texture2D? Portrait, int? HealthArt, int? FatigueArt, Rect2 Rect, Rect2 PortraitRect)? FrameForSelfTest(string entity) =>
        _entries.TryGetValue(entity, out var entry)
            ? (entry.Frame.Texture is not null, entry.Portrait.Visible ? entry.Portrait.Texture : null, entry.Health.Art, entry.Fatigue.Art,
                entry.Button.GetGlobalRect(), entry.Portrait.GetGlobalRect())
            : null;
    /// <summary>The follower whose portrait the pointer rests on now (null none), as the strip's hover tracks it.</summary>
    internal string? HoveredForSelfTest => _hovered;
    internal Vector2? MenuItemCentreForSelfTest(FollowerMenuItem item) =>
        _menuRows.Where(row => row.Item == item).Select(row => (Vector2?)CentreOf(row.Row)).FirstOrDefault();
    /// <summary>ARC-UI-1736: a row of the open menu is greyed (FP8), null when no menu is open.</summary>
    internal FollowerMenuRowState? MenuRowStateForSelfTest(FollowerMenuItem item) =>
        _menuRows.Where(row => row.Item == item).Select(row => (FollowerMenuRowState?)(row.Greyed ? FollowerMenuRowState.Greyed : FollowerMenuRowState.Normal)).FirstOrDefault();
    /// <summary>ARC-UI-1786: the word an open menu's row draws, null when the row is not drawn (its message is missing).</summary>
    internal string? MenuRowTextForSelfTest(FollowerMenuItem item) =>
        _menuRows.Where(row => row.Item == item).Select(row => row.Row.GetChildren().OfType<AreaArtLabel>().FirstOrDefault()?.Text).FirstOrDefault();
    /// <summary>ARC-UI-1736: a menu row's size in the menu's own pixels (FP8: 118 × 15).</summary>
    internal Vector2? MenuRowSizeForSelfTest(FollowerMenuItem item) => _menuRows.Where(row => row.Item == item).Select(row => (Vector2?)row.Row.Size).FirstOrDefault();
    /// <summary>ARC-UI-1736: the open menu's rectangle on the screen and its rows' count (FP7, FP8).</summary>
    internal (Rect2 Rect, int Rows, bool Art)? MenuForSelfTest => _menu.Visible ? (RectOf(_menu), _menuRows.Count, _art.ContainsKey(AreaFollowerPanelRules.MenuArt)) : null;
    /// <summary>ARC-UI-1736: whether the panel is shown (the show/hide flag, FP5), the scroll offset, the followers' count and the show/hide button's and the arrows' art.</summary>
    internal (bool Shown, int Offset, int Count, int? ToggleArt, int? UpArt, int? DownArt) PanelForSelfTest =>
        (_panelShown, _offset, _followers.Count, _toggle is null ? null : AreaFollowerPanelRules.ToggleArt(_panelShown),
            _up is null ? null : AreaFollowerPanelRules.UpArtAt(_offset), _down is null ? null : AreaFollowerPanelRules.DownArtAt(_offset, _followers.Count));
    /// <summary>ARC-UI-1736: the screen centre of the show/hide button, the up arrow or the down arrow (null when it is not there).</summary>
    internal Vector2? ButtonCentreForSelfTest(string which) =>
        (which switch { "toggle" => _toggle, "up" => _up, "down" => _down, _ => null }) is { } button ? CentreOf(button) : null;
    /// <summary>ARC-UI-1785: the window rectangle (the original's pixels) of the show/hide button, the up arrow or the down arrow (null when it is not there).</summary>
    internal Rect2? ButtonRectForSelfTest(string which) =>
        (which switch { "toggle" => _toggle, "up" => _up, "down" => _down, _ => null }) is { } button ? new Rect2(button.Position, button.Size) : null;
    /// <summary>ARC-UI-2284: the window rectangle (device pixels, under the panel's scale) of the show/hide button, the up arrow or the down arrow (null when it is not there).</summary>
    internal Rect2? ButtonScreenRectForSelfTest(string which) =>
        (which switch { "toggle" => _toggle, "up" => _up, "down" => _down, _ => null }) is { } button ? RectOf(button) : null;
    /// <summary>ARC-UI-2284: gives the panel no art, as a panel that was made after the art was read and missed it (for the smoke).</summary>
    internal void UnbindArtForSelfTest() => Bind(new Dictionary<int, Texture2D>());
    /// <summary>ARC-UI-1736: whether a button's art was drawn (the art read), by "toggle", "up", "down".</summary>
    internal bool? ButtonArtForSelfTest(string which) =>
        which switch { "toggle" => _toggle is null ? null : _toggleArt?.Texture is not null, "up" => _up is null ? null : _upArt?.Texture is not null,
            "down" => _down is null ? null : _downArt?.Texture is not null, _ => null };

    /// <summary>The whole factor the panel is drawn at (the interface bars').</summary>
    internal float ScaleForSelfTest => _root.Scale.X;
    /// <summary>ARC-UI-2510: the window position of the screen's top left the panel is drawn from (the self-test).</summary>
    internal Vector2 OriginForSelfTest => _root.Position;

    // The screen rectangle and centre of a control under the panel's scale (Control.GetGlobalRect ignores the scale).
    private static Rect2 RectOf(Control control) { var transform = control.GetGlobalTransform(); return new(transform.Origin, transform.BasisXform(control.Size)); }
    private static Vector2 CentreOf(Control control) => RectOf(control).GetCenter();

    /// <summary>The window x of the frames' right edge while any is shown, else 0: the diagnostic HUD starts right of it.</summary>
    public float RightEdge => Visible && _entries.Count > 0
        ? _root.Position.X + (AreaFollowerFrameLayout.X + AreaFollowerFrameLayout.Width) * _root.Scale.X : 0;
    /// <summary>Raised when <see cref="RightEdge"/> may have changed (frames shown, hidden or scaled).</summary>
    public event Action? EdgeChanged;

    /// <summary>Whether the order menu is shown.</summary>
    public bool MenuShown => _menu.Visible;

    private bool _inert;

    /// <summary>
    /// ARC-UI-2069 (ledger DB1, DB2): the panel stays drawn (frames, portraits, bars) but takes no input: the original's follower handler 0x56A9D0 returns at once while
    /// the PC's dialog flag is set (0x567400), and its draw routine 0x56B290 does not test the dialog. Turning it on closes the menu, forgets the hover without a hover-exit (the original fires none, and the dialog start's description in the description area must stay);
    /// the buttons still consume the pointer (the dialog's hook consumes every record, 0x5517A0).
    /// </summary>
    public bool Inert
    {
        get => _inert;
        set
        {
            if (_inert == value) return;
            _inert = value;
            if (value) { CloseMenu(); _hovered = null; }
        }
    }

    public override void _EnterTree() => AreaGameBarPanel.TopScreenChanged += ScreenMoved;

    public override void _Ready()
    {
        Layer = 3;
        // Nearest, as the bars' strips: the frames are scaled by the same whole factor.
        _root = new Control { Name = "FollowerPortraits", MouseFilter = Control.MouseFilterEnum.Ignore, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        AddChild(_root);
        // The menu (FP7, FP8): art 826 with the rows over it, a window of its own over the others; it takes every release it is under (FP9).
        _menu = new Control { Name = "FollowerOrders", MouseFilter = Control.MouseFilterEnum.Stop, Visible = false, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        AddChild(_menu);
        // ARC-DEV-280 (0x56AC47): a release that no menu row takes closes the menu, also over the menu's own margins (FP9).
        _menu.GuiInput += input => { if (input is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left or MouseButton.Right }) CloseMenu(); };
        GetViewport().SizeChanged += Resized;
        AreaGameBarPanel.InsetsChanged += PlaceChrome;
        Place();
        Hide();
    }

    private void Resized() { CloseMenu(); Place(); EdgeChanged?.Invoke(); }

    // ARC-UI-2510: the layout turned on or off, or the window was laid out again: the screen the portraits hang in moved (the bars place themselves after this panel's own resize handler may have run).
    private void ScreenMoved() { if (_root is null) return; Place(); EdgeChanged?.Invoke(); }

    /// <summary>ARC-UI-1817: device pixels per Legacy pixel of this panel and its menu (what the generated frames are painted at): the scale of the panel's root, the canvas transform's
    /// and the viewport's stretch.</summary>
    internal float DeviceScale() =>
        _root is null || !GodotObject.IsInstanceValid(_root) ? 1f
            : _root.IsInsideTree() ? _root.GetGlobalTransformWithCanvas().Scale.X * GetViewport().GetStretchTransform().Scale.X : _root.Scale.X;

    /// <summary>ARC-UI-1817: the interface texture of an art number the panel was bound with (null without it), for the native smoke.</summary>
    internal HdInterfaceTexture? TextureForSelfTest(int art) => _art.GetValueOrDefault(art) as HdInterfaceTexture;

    // The frames are painted again when the device scale changes (the textures ask for it at every draw, but a parent's scale change draws nothing by itself).
    private float _drawnScale;

    private void RedrawTextures()
    {
        _drawnScale = DeviceScale();
        foreach (var root in new Control?[] { _root, _menu })
            if (root is not null) foreach (var node in root.FindChildren("*", "TextureRect", true, false)) (node as CanvasItem)?.QueueRedraw();
    }

    /// <summary>Gives the frames their interface art (frame 0 of 503, 504, 505 and 616 by number, and the panel's); missing numbers draw the fallback.</summary>
    public void Bind(IReadOnlyDictionary<int, Texture2D> art)
    {
        _art = art ?? throw new ArgumentNullException(nameof(art));
        // ARC-UI-1817: the generated interface paints the panel's frames at the panel's own device scale.
        foreach (var texture in art.Values.OfType<HdInterfaceTexture>()) texture.ScaleSource = DeviceScale;
        // No list matches it: the next Show rebuilds the frames with the new art, an empty list included.
        _signature = "\0";
    }

    /// <summary>A modal window covers the panel: the frames and buttons go away and the menu closes, but the shown flag and the scroll offset are kept for when the window closes
    /// (the next <see cref="Show"/> brings the strip back as it was).</summary>
    public void Suspend()
    {
        CloseMenu();
        if (_hovered is not null) { _hovered = null; PortraitHoverLeft?.Invoke(); }
        Visible = false;
        EdgeChanged?.Invoke();
    }

    /// <summary>ARC-UI-1736 (FP1, the module's reset 0x56A880): the panel is shown again and the scroll offset is 0 (a new game or a load).</summary>
    public void ResetPanel()
    {
        CloseMenu();
        _panelShown = true; _offset = 0;
    }

    /// <summary>Shows the frames of the followers (entity, label, life, portrait) in array order: six at most, from the scroll offset, none while the
    /// panel is hidden; the show/hide button stands while there is a follower and the arrows from seven (FP3, FP5). The frames are rebuilt only when
    /// the followers, their labels, portraits, the offset or the shown flag change; the bars follow every call.</summary>
    public void Show(IReadOnlyList<(string Entity, string Label, AreaLife? Life, Texture2D? Portrait)> followers)
    {
        _followers = followers.ToArray();
        _offset = AreaFollowerPanelRules.ClampOffset(_offset, _followers.Count);
        var shown = AreaFollowerPanelRules.VisibleIndexes(_panelShown, _offset, _followers.Count).Select(i => _followers[i]).ToArray();
        // ARC-UI-1855: shown before the rebuild raises EdgeChanged, whose listener reads RightEdge (0 while hidden): the diagnostic HUD would not move right of the frames.
        var wasVisible = Visible;
        Visible = _followers.Count > 0;
        var signature = $"{_panelShown}|{_offset}|{_followers.Count}|" + string.Join("\n", shown.Select(f => f.Entity + "\t" + f.Label + "\t" + f.Portrait?.GetRid().Id));
        if (signature != _signature)
        {
            _signature = signature;
            // The button the pointer rested on is freed with the rebuild, and a freed button sends no exit.
            if (_hovered is not null) { _hovered = null; PortraitHoverLeft?.Invoke(); }
            foreach (var child in _root.GetChildren()) { _root.RemoveChild(child); child.QueueFree(); }
            _entries.Clear();
            _toggle = _up = _down = null; _toggleArt = _upArt = _downArt = null;
            EntitiesForSelfTest = shown.Select(f => f.Entity).ToArray();
            for (var slot = 0; slot < shown.Length; slot++) BuildFrame(slot, shown[slot]);
            // The menu stands at its follower's slot: a rebuild that moved or removed him closes it.
            if (_menuEntity is { } open && (!_entries.TryGetValue(open, out var kept) || kept.Slot != _menuSlot)) CloseMenu();
            BuildChrome();
            Place();
            EdgeChanged?.Invoke();
        }
        // ARC-UI-1994: Suspend() hid the panel and raised EdgeChanged (inset 0); the resume keeps the signature, so no rebuild raises it again here.
        else if (Visible != wasVisible) EdgeChanged?.Invoke();
        foreach (var (entity, _, life, _) in shown)
        {
            var entry = _entries[entity];
            var value = life ?? default;
            var healthArt = value.Poisoned ? AreaFollowerFrameLayout.PoisonedHealthArt : AreaFollowerFrameLayout.HealthArt;
            entry.Health.Set(LifeBarRules.PortraitFill(LifeBarRules.Percent(value.Health, value.MaxHealth), BarWidth), healthArt, _art.GetValueOrDefault(healthArt),
                value.Poisoned ? AreaLifeBar.PoisonedHealthColor : AreaLifeBar.HealthColor);
            entry.Fatigue.Set(LifeBarRules.PortraitFill(LifeBarRules.Percent(value.Fatigue, value.MaxFatigue), BarWidth), AreaFollowerFrameLayout.FatigueArt,
                _art.GetValueOrDefault(AreaFollowerFrameLayout.FatigueArt), AreaLifeBar.FatigueColor);
            entry.Tint.Visible = life is not null && LifeBarRules.PortraitTinted(value.Health, value.MaxHealth);
        }
    }

    private void BuildFrame(int slot, (string Entity, string Label, AreaLife? Life, Texture2D? Portrait) follower)
    {
        var (entity, label, _, portrait) = follower;
        var (x, y) = AreaFollowerFrameLayout.Slot(slot);
        var size = new Vector2(AreaFollowerFrameLayout.Width, AreaFollowerFrameLayout.Height);
        var button = new Button { Flat = true, FocusMode = Control.FocusModeEnum.None, Position = new Vector2(x, y), Size = size, CustomMinimumSize = size, ClipContents = true };
        // ARC-DEV-280 (B33, 0x56A9D0): the order menu opens on the RIGHT button's release over the portrait (an open menu is closed by it
        // instead), and the left button's release is a click on the follower (a talk, 0x54EA80 -> 0x54ED30, or in a target mode the aim of the
        // spell, skill or item, FP6), which only closes an open menu (FP9).
        button.Pressed += () => { if (_inert) return; if (_menu.Visible) CloseMenu(); else PortraitClicked?.Invoke(entity); };
        button.GuiInput += input =>
        {
            if (_inert) return;
            if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false } release) return;
            if (_menu.Visible) { CloseMenu(); return; }
            if (new Rect2(Vector2.Zero, button.Size).HasPoint(release.Position)) OpenMenu(entity);
        };
        // ARC-UI-1281 (HK26): entering a portrait with no menu open is the follower's hover; leaving it clears (0x550720).
        button.MouseEntered += () => { if (_inert) return; _hovered = entity; if (!_menu.Visible) PortraitHovered?.Invoke(entity); };
        button.MouseExited += () => { if (_hovered != entity) return; _hovered = null; PortraitHoverLeft?.Invoke(); };
        button.SetMeta(EntityMeta, entity);
        var frame = _art.GetValueOrDefault(AreaFollowerFrameLayout.FrameArt);
        var back = new ColorRect { Color = Back, Size = size, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = frame is null };
        var image = new Rect2(AreaFollowerFrameLayout.ImageX, AreaFollowerFrameLayout.ImageY, AreaFollowerFrameLayout.PortraitSize, AreaFollowerFrameLayout.PortraitSize);
        // The expand mode first: with the default one the 64 × 64 image would hold the rectangle at its own size.
        var picture = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            Texture = portrait, Visible = portrait is not null, Position = image.Position, Size = image.Size, MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        // ARC-UI-2940: the initials of a follower without a portrait in the original's text font (art 229; nothing without it), never Godot's default font.
        var initials = new AreaArtLabel
        {
            Text = new string(label.Where(char.IsLetter).Take(3).ToArray()), Visible = portrait is null, Position = image.Position, Size = image.Size,
            Align = AreaArtLabel.Alignment.Centre, Middle = true, Face = MenuFont, Colour = MenuWhite,
        };
        // The original adds pure red to the image's pixels (0x51E0A0).
        var tint = new ColorRect
        {
            Color = new Color(1, 0, 0), Position = image.Position, Size = image.Size, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false,
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
        };
        var frameRect = new TextureRect { Texture = frame, Size = size, MouseFilter = Control.MouseFilterEnum.Ignore };
        var health = new AreaArtBar { Name = "Health", Position = new Vector2(AreaFollowerFrameLayout.BarX, AreaFollowerFrameLayout.HealthBarY) };
        var fatigue = new AreaArtBar { Name = "Fatigue", Position = new Vector2(AreaFollowerFrameLayout.BarX, AreaFollowerFrameLayout.FatigueBarY) };
        // The frame (503) first, the image and bars over it, as the original draws them.
        foreach (Control child in new Control[] { back, frameRect, picture, initials, tint, health, fatigue }) button.AddChild(child);
        _root.AddChild(button);
        _entries[entity] = new(slot, button, back, frameRect, picture, initials, tint, health, fatigue);
    }

    /// <summary>The show/hide button (while there is a follower) and the scroll arrows (panel shown, seven or more followers), FP1, FP5.</summary>
    private void BuildChrome()
    {
        var count = _followers.Count;
        if (AreaFollowerPanelRules.ToggleShown(count))
            (_toggle, _toggleArt) = ChromeButton(AreaFollowerPanelRules.ToggleRectFor(BarsDrawn), AreaFollowerPanelRules.ToggleArt(_panelShown), _panelShown ? "hide" : "show", Toggle);
        if (!AreaFollowerPanelRules.ArrowsShown(_panelShown, count)) return;
        (_up, _upArt) = ChromeButton(AreaFollowerPanelRules.UpRectFor(BarsDrawn), AreaFollowerPanelRules.UpArtAt(_offset), "up",
            () => Scroll(AreaFollowerPanelRules.ScrollUp(_offset, count)));
        (_down, _downArt) = ChromeButton(AreaFollowerPanelRules.DownRectFor(BarsDrawn), AreaFollowerPanelRules.DownArtAt(_offset, count), "down",
            () => Scroll(AreaFollowerPanelRules.ScrollDown(_offset, count)));
    }

    /// <summary>ARC-UI-1785 (FP1, SZ11): whether the interface bars are drawn: the layout the three buttons stand in (the bars-drawn places or the hidden-interface ones).</summary>
    private static bool BarsDrawn => AreaGameBarPanel.BottomInset > 0;

    // The layout is chosen again at every resize in the original (SZ11): the bars shown or hidden move the three buttons, the portraits stay.
    private void PlaceChrome()
    {
        var barsDrawn = BarsDrawn;
        if (_root is null) return;
        // ARC-UI-2284: the strip's bottom is the window's bottom, so the buttons stand a table's distance above it, not at the table's y from the top.
        // ARC-UI-2510: in the Steamwoven layout the screen hangs from the top strip and ends at the bottom strip's bottom edge, not at the window's bottom.
        var bottom = AreaGameBarPanel.TopScreen?.BottomEdge ?? GetViewport().GetVisibleRect().Size.Y;
        var heightUnits = (bottom - _root.Position.Y) / _root.Scale.Y;
        foreach (var (button, rect) in new[] { (_toggle, AreaFollowerPanelRules.ToggleRectFor(barsDrawn)), (_up, AreaFollowerPanelRules.UpRectFor(barsDrawn)), (_down, AreaFollowerPanelRules.DownRectFor(barsDrawn)) })
            if (button is not null) button.Position = new Vector2(rect.X, AreaFollowerPanelRules.AnchoredY(rect.Y, heightUnits));
    }

    private (Button, TextureRect) ChromeButton((int X, int Y, int Width, int Height) rect, int art, string fallback, Action pressed)
    {
        var size = new Vector2(rect.Width, rect.Height);
        var button = new Button { Flat = true, FocusMode = Control.FocusModeEnum.None, Position = new Vector2(rect.X, rect.Y), Size = size, CustomMinimumSize = size, ClipContents = true };
        var texture = _art.GetValueOrDefault(art);
        var picture = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, Texture = texture, Size = size,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        button.AddChild(picture);
        if (texture is null)
        {
            // Without the art (no game folder): a dark plate with a word.
            button.AddChild(new ColorRect { Color = Back, Size = size, MouseFilter = Control.MouseFilterEnum.Ignore });
            var word = new AreaArtLabel { Text = fallback, Size = size, Align = AreaArtLabel.Alignment.Centre, Middle = true, Face = MenuFont, Colour = MenuWhite };
            _chromeWords.Add(word);
            button.AddChild(word);
        }
        // A release with a menu open is the menu's (FP9): it closes the menu and nothing else happens.
        button.Pressed += () => { if (_inert) return; if (_menu.Visible) CloseMenu(); else pressed(); };
        _root.AddChild(button);
        return (button, picture);
    }

    private void Toggle()
    {
        _panelShown = !_panelShown;
        Show(_followers);
    }

    private void Scroll(int offset)
    {
        if (offset == _offset) return;
        _offset = offset;
        Show(_followers);
    }

    /// <summary>Opens the order menu of a follower at its frame's right edge (FP7; opening the menu of the follower that is open closes it).</summary>
    public void OpenMenu(string entity)
    {
        if (_inert || !_entries.TryGetValue(entity, out var entry)) return;
        if (_menu.Visible && _menuEntity == entity) { CloseMenu(); return; }
        _menuEntity = entity;
        foreach (var child in _menu.GetChildren()) { _menu.RemoveChild(child); child.QueueFree(); }
        _menuRows.Clear();
        var (mind, canOpen) = MenuFacts?.Invoke(entity) ?? (false, true);
        var art = _art.GetValueOrDefault(AreaFollowerPanelRules.MenuArt);
        var size = art?.GetSize() ?? new Vector2(138, 3 + 18 * Enum.GetValues<FollowerMenuItem>().Length + 3);
        _menu.Size = size;
        _menu.AddChild(art is null
            ? new ColorRect { Color = new Color(22 / 255f, 25 / 255f, 34 / 255f, .95f), Size = size, MouseFilter = Control.MouseFilterEnum.Ignore }
            : new TextureRect { Texture = art, Size = size, MouseFilter = Control.MouseFilterEnum.Ignore });
        var texts = MenuTexts ?? AreaFollowerMenuTexts.Project("no texts read");
        foreach (var item in texts.Rows)
        {
            var (x, y, width, height) = AreaFollowerPanelRules.MenuRow(item);
            var greyed = AreaFollowerPanelRules.RowState(item, mind, canOpen) == FollowerMenuRowState.Greyed;
            // A row is a plain control with its word, not a button: a button's own minimum size (theme margins) would make it taller than the original's 15 pixels.
            var row = new Control
            {
                MouseFilter = Control.MouseFilterEnum.Stop, Position = new Vector2(x, y), Size = new Vector2(width, height), CustomMinimumSize = new Vector2(width, height), ClipContents = true,
            };
            // FP8: white, the bright colour under the pointer, mid grey when greyed (a greyed row has no pointer colour).
            var word = new AreaArtLabel
            {
                Text = texts.Text(item) ?? "", Size = new Vector2(width, height), Middle = true, Face = MenuFont, Colour = greyed ? MenuGrey : MenuWhite,
            };
            row.AddChild(word);
            if (!greyed)
            {
                row.MouseEntered += () => word.Colour = MenuBright;
                row.MouseExited += () => word.Colour = MenuWhite;
            }
            // The release on a row runs it after the menu closes (FP9, code 1); a right release over a row, or a left one over a greyed row, is taken by no menu row: it closes the menu.
            row.GuiInput += input =>
            {
                if (input is not InputEventMouseButton { Pressed: false } release) return;
                if (release.ButtonIndex == MouseButton.Left && !greyed) Choose(item);
                else if (release.ButtonIndex is MouseButton.Left or MouseButton.Right) CloseMenu();
            };
            _menu.AddChild(row);
            _menuRows.Add((item, row, greyed));
        }
        var (originX, originY) = AreaFollowerPanelRules.MenuOrigin(entry.Slot);
        _menuOrigin = new Vector2(originX, originY);
        _menuSlot = entry.Slot;
        PlaceMenu();
        _menu.Show();
    }

    private void Choose(FollowerMenuItem item)
    {
        var entity = _menuEntity;
        CloseMenu();
        if (entity is not null) ItemChosen?.Invoke(entity, item);
    }

    public void CloseMenu() { _menu.Hide(); _menuEntity = null; }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= Resized;
        AreaGameBarPanel.InsetsChanged -= PlaceChrome;
        AreaGameBarPanel.TopScreenChanged -= ScreenMoved;
    }

    private void Place()
    {
        if (_root is null) return;
        var size = GetViewport().GetVisibleRect().Size;
        // ARC-UI-2510: where the strips put the 800 × 600 screen (the Steamwoven layout: its top left under the top band's strip, at the strips' scale), else the whole factor centred at the window's top.
        // ARC-UI-2925 (owner decision 2026-11-20): the whole follower group (portraits, scroll arrows, show/hide button) stays in the column on the original's places, counted from the composition's left edge at its scale, so the button
        // stands at the top left corner of the bottom strip as in the original at every window size; nothing is carried to the window's edge.
        if (AreaGameBarPanel.TopScreen is { } screen)
        {
            _root.Position = screen.Origin;
            _root.Scale = new(screen.Scale, screen.Scale);
            _root.TextureFilter = CanvasItem.TextureFilterEnum.Linear;
        }
        else
        {
            var factor = AreaGameBarPanel.ScreenFactor(size);
            _root.Position = new(AreaGameBarPanel.ScreenLeft(size, factor), 0);
            _root.Scale = new(factor, factor);
            _root.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        }
        PlaceChrome();
        PlaceMenu();
    }

    // The menu is a window of its own at the screen's scale, at the open slot's right edge (FP7: no clamp to the screen).
    private void PlaceMenu()
    {
        if (_menu is null) return;
        _menu.Scale = _root.Scale;
        _menu.TextureFilter = _root.TextureFilter;
        _menu.Position = _root.Position + _menuOrigin * _root.Scale.X;
        if (IsInsideTree() && MathF.Abs(DeviceScale() - _drawnScale) > 0.001f) RedrawTextures();
    }
}

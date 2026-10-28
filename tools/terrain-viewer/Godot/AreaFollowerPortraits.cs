using Arcanum.Presentation.Hud;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
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
    public int Filled { get; private set; }
    /// <summary>The art number drawn (null for the plain fallback).</summary>
    public int? Art { get; private set; }
    public Color FillColor => _fallback;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = CustomMinimumSize = new Vector2(AreaFollowerFrameLayout.BarWidth, AreaFollowerFrameLayout.BarHeight);
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
        if (Filled <= 0) return;
        var height = AreaFollowerFrameLayout.BarHeight;
        if (_art is null) { DrawRect(new Rect2(0, 0, Filled, height), _fallback); return; }
        var width = Math.Min(Filled, _art.GetWidth());
        DrawTextureRectRegion(_art, new Rect2(0, 0, width, Math.Min(height, _art.GetHeight())), new Rect2(0, 0, width, Math.Min(height, _art.GetHeight())));
    }
}

/// <summary>
/// ARC-WORLD-046 follower portraits: one frame per follower in the PC's array (the original's portrait list; waiting NPCs have none,
/// OL16), each opening the order menu of the original's portrait (walk, attack, stay close, spread out, back off, wait, inventory,
/// character sheet; the rule leaves wait out for a mind-controlled follower, which the viewer does not model yet). Inventory and
/// character sheet are shown disabled (not modelled for followers). The menu is an inline panel of buttons that take no focus, not a
/// popup window: an embedded popup takes the world window's focus, which quarantines the world and refuses the choice.
/// ARC-UI-020 (ledger CH9): each frame's health bar (in its poisoned art while the follower is poisoned) and fatigue bar are cropped
/// to the follower's percent (<see cref="LifeBarRules.PortraitFill"/>); below 20 % health the portrait turns red
/// (<see cref="LifeBarRules.PortraitTinted"/>). A follower without combat values shows empty bars.
/// ARC-UI-024 (ledger CH9, PA6): the frames are the original's, drawn with its art at its places (<see cref="AreaFollowerFrameLayout"/>:
/// frame 503, bars 505/616 and 504, the follower's portrait at 32 × 32, the red added over it) on the 800 × 600 screen centred at the
/// interface bars' whole factor, its top at the window's top. Without the art (no game folder) the frame is a dark rectangle with the
/// name's first letters and plain bars. NOT READY: more than six followers (the original scrolls the list with its arrow buttons, the
/// viewer shows the first six) and the critter art the original draws for a follower without a portrait.
/// </summary>
public partial class AreaFollowerPortraits : CanvasLayer
{
    private sealed record Entry(Button Button, ColorRect Back, TextureRect Frame, TextureRect Portrait, Label Initials, ColorRect Tint,
        AreaArtBar Health, AreaArtBar Fatigue);

    private Control _root = null!;
    private PanelContainer _menu = null!;
    private VBoxContainer _menuItems = null!;
    private string? _menuEntity;
    private string _signature = "";
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<int, Texture2D> _art = new Dictionary<int, Texture2D>();
    /// <summary>The width of a frame's bars, in the original's pixels (the bar art's).</summary>
    public const int BarWidth = AreaFollowerFrameLayout.BarWidth;
    private static readonly Color Back = new(14 / 255f, 16 / 255f, 22 / 255f, .85f);

    /// <summary>A menu item chosen for a follower: its entity and the item.</summary>
    public event Action<string, FollowerMenuItem>? ItemChosen;

    internal IReadOnlyList<string> EntitiesForSelfTest { get; private set; } = [];
    private const string EntityMeta = "follower_entity";

    /// <summary>The screen centre of a follower's frame, or of an item of the open menu, for the native smoke's mouse presses.</summary>
    internal Vector2? PortraitCentreForSelfTest(string entity) =>
        _entries.TryGetValue(entity, out var entry) ? entry.Button.GetGlobalRect().GetCenter() : null;
    /// <summary>A follower frame's bar fills (in the original's pixels), whether its health bar is the poisoned one, and its red tint.</summary>
    internal (int Health, int Fatigue, bool Poisoned, bool Tinted)? BarsForSelfTest(string entity) =>
        _entries.TryGetValue(entity, out var entry)
            ? (entry.Health.Filled, entry.Fatigue.Filled, entry.Health.FillColor == AreaLifeBar.PoisonedHealthColor, entry.Tint.Visible)
            : null;
    /// <summary>A follower frame's art: the frame art drawn, the portrait texture (null none), the bars' art numbers, and its and
    /// the portrait's window rectangles.</summary>
    internal (bool Frame, Texture2D? Portrait, int? HealthArt, int? FatigueArt, Rect2 Rect, Rect2 PortraitRect)? FrameForSelfTest(string entity) =>
        _entries.TryGetValue(entity, out var entry)
            ? (entry.Frame.Texture is not null, entry.Portrait.Visible ? entry.Portrait.Texture : null, entry.Health.Art, entry.Fatigue.Art,
                entry.Button.GetGlobalRect(), entry.Portrait.GetGlobalRect())
            : null;
    internal Vector2? MenuItemCentreForSelfTest(FollowerMenuItem item) =>
        _menuItems.GetChildren().OfType<Button>().FirstOrDefault(b => b.HasMeta(ItemMeta) && (int)b.GetMeta(ItemMeta) == (int)item)?.GetGlobalRect().GetCenter();

    /// <summary>The window x of the frames' right edge while any is shown, else 0: the diagnostic HUD starts right of it.</summary>
    public float RightEdge => Visible && _entries.Count > 0
        ? _root.Position.X + (AreaFollowerFrameLayout.X + AreaFollowerFrameLayout.Width) * _root.Scale.X : 0;
    /// <summary>Raised when <see cref="RightEdge"/> may have changed (frames shown, hidden or scaled).</summary>
    public event Action? EdgeChanged;

    /// <summary>Whether the order menu is shown.</summary>
    public bool MenuShown => _menu.Visible;

    private const string ItemMeta = "follower_menu_item";

    public override void _Ready()
    {
        Layer = 3;
        // Nearest, as the bars' strips: the frames are scaled by the same whole factor.
        _root = new Control { Name = "FollowerPortraits", MouseFilter = Control.MouseFilterEnum.Ignore, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        AddChild(_root);
        _menu = new PanelContainer { Name = "FollowerOrders", MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        _menu.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(22 / 255f, 25 / 255f, 34 / 255f, .95f),
            ContentMarginLeft = 4, ContentMarginRight = 4, ContentMarginTop = 4, ContentMarginBottom = 4,
        });
        AddChild(_menu);
        _menuItems = new VBoxContainer(); _menu.AddChild(_menuItems);
        GetViewport().SizeChanged += Resized;
        Place();
        Hide();
    }

    private void Resized() { CloseMenu(); Place(); EdgeChanged?.Invoke(); }

    /// <summary>Gives the frames their interface art (frame 0 of 503, 504, 505 and 616 by number); missing numbers draw the fallback.</summary>
    public void Bind(IReadOnlyDictionary<int, Texture2D> art)
    {
        _art = art ?? throw new ArgumentNullException(nameof(art));
        // No list matches it: the next Show rebuilds the frames with the new art, an empty list included.
        _signature = "\0";
    }

    /// <summary>Shows one frame per follower (entity, label, life, portrait) in array order, at most six, with its bars; hidden when
    /// there is none. The frames are rebuilt only when the followers, their labels or portraits change; the bars follow every call.</summary>
    public void Show(IReadOnlyList<(string Entity, string Label, AreaLife? Life, Texture2D? Portrait)> followers)
    {
        var shown = followers.Take(AreaFollowerFrameLayout.Slots).ToArray();
        var signature = string.Join("\n", shown.Select(f => f.Entity + "\t" + f.Label + "\t" + f.Portrait?.GetRid().Id));
        if (signature != _signature)
        {
            _signature = signature;
            foreach (var child in _root.GetChildren()) { _root.RemoveChild(child); child.QueueFree(); }
            _entries.Clear();
            EntitiesForSelfTest = shown.Select(f => f.Entity).ToArray();
            if (_menuEntity is { } open && !EntitiesForSelfTest.Contains(open)) CloseMenu();
            for (var slot = 0; slot < shown.Length; slot++)
            {
                var (entity, label, _, portrait) = shown[slot];
                var (x, y) = AreaFollowerFrameLayout.Slot(slot);
                var size = new Vector2(AreaFollowerFrameLayout.Width, AreaFollowerFrameLayout.Height);
                var button = new Button { Flat = true, FocusMode = Control.FocusModeEnum.None, TooltipText = label, Position = new Vector2(x, y), Size = size, CustomMinimumSize = size, ClipContents = true };
                button.Pressed += () => OpenMenu(entity, button);
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
                var initials = new Label
                {
                    Text = new string(label.Where(char.IsLetter).Take(3).ToArray()), Visible = portrait is null, Position = image.Position, Size = image.Size,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                initials.AddThemeFontSizeOverride("font_size", 9);
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
                _entries[entity] = new(button, back, frameRect, picture, initials, tint, health, fatigue);
            }
            Visible = shown.Length > 0;
            Place();
            EdgeChanged?.Invoke();
        }
        foreach (var (entity, _, life, _) in shown)
        {
            var entry = _entries[entity];
            var value = life ?? default;
            var healthArt = value.Poisoned ? AreaFollowerFrameLayout.PoisonedHealthArt : AreaFollowerFrameLayout.HealthArt;
            entry.Health.Set(LifeBarRules.PortraitFill(value.HealthPercent, BarWidth), healthArt, _art.GetValueOrDefault(healthArt),
                value.Poisoned ? AreaLifeBar.PoisonedHealthColor : AreaLifeBar.HealthColor);
            entry.Fatigue.Set(LifeBarRules.PortraitFill(value.FatiguePercent, BarWidth), AreaFollowerFrameLayout.FatigueArt,
                _art.GetValueOrDefault(AreaFollowerFrameLayout.FatigueArt), AreaLifeBar.FatigueColor);
            entry.Tint.Visible = life is not null && LifeBarRules.PortraitTinted(value.Health, value.MaxHealth);
        }
    }

    /// <summary>Opens the order menu of a follower next to its frame (a second press of the same frame closes it).</summary>
    public void OpenMenu(string entity, Control? anchor = null, bool mindControlled = false)
    {
        if (_menu.Visible && _menuEntity == entity) { CloseMenu(); return; }
        _menuEntity = entity;
        foreach (var child in _menuItems.GetChildren()) { _menuItems.RemoveChild(child); child.QueueFree(); }
        foreach (var item in Enum.GetValues<FollowerMenuItem>())
        {
            if (item == FollowerMenuItem.Wait && CompanionOrderRules.PortraitOrder(item, mindControlled) is null) continue;
            var button = new Button
            {
                Text = AreaCompanionOrders.Label(item), Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None,
                Disabled = item is FollowerMenuItem.Inventory or FollowerMenuItem.CharacterSheet,
            };
            button.SetMeta(ItemMeta, (int)item);
            button.Pressed += () => Choose(item);
            _menuItems.AddChild(button);
        }
        _menu.ResetSize();
        var size = GetViewport().GetVisibleRect().Size;
        var anchorRect = anchor?.GetGlobalRect();
        var wanted = anchorRect is { } rect ? rect.Position + new Vector2(rect.Size.X + 4, 0) : new Vector2(24, 24);
        var menuSize = _menu.GetCombinedMinimumSize();
        _menu.Position = new(Math.Min(wanted.X, Math.Max(0, size.X - menuSize.X)), Math.Clamp(wanted.Y, 0, Math.Max(0, size.Y - menuSize.Y)));
        _menu.Show();
    }

    private void Choose(FollowerMenuItem item)
    {
        var entity = _menuEntity;
        CloseMenu();
        if (entity is not null) ItemChosen?.Invoke(entity, item);
    }

    public void CloseMenu() { _menu.Hide(); _menuEntity = null; }

    public override void _ExitTree() => GetViewport().SizeChanged -= Resized;

    private void Place()
    {
        if (_root is null) return;
        var size = GetViewport().GetVisibleRect().Size;
        var factor = AreaGameBarPanel.ScreenFactor(size);
        _root.Position = new(AreaGameBarPanel.ScreenLeft(size, factor), 0);
        _root.Scale = new(factor, factor);
    }
}

using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-050 diagnostic barter window: the hero's items with their sale quotes and the merchant's wares with their buy quotes, both
/// sides' gold, the hero's haggle and the merchant's reaction. A button per line sells or buys it (a stack trades whole); a quote of 0
/// is shown with its reason and its button is disabled outside a follower's window. It renders an <see cref="AreaBarterView"/>; the app
/// runs every trade through the session. ARC-UI-064: with the game's art and a shown object the window draws the original barter window
/// instead (<see cref="AreaInventoryGridArea"/> in mode 1, the round view, <see cref="AreaBarterArtLayout"/>): an item dragged from the
/// merchant's grid onto the hero's is bought, one dragged the other way sold; a switch below the frame shows the diagnostic window.
/// ARC-UI-069: the opening shop's docked window shows the original look too, at the menu screen's (0, 41) between the shop's strips, without
/// the round view or its release (the main menu is up, IW33, IW34) and without the switch.
/// </summary>
public partial class AreaBarterPanel : CanvasLayer
{
    private PanelContainer _panel = null!;
    private Label _header = null!;
    private Label _heroHeader = null!;
    private Label _waresHeader = null!;
    private VBoxContainer _hero = null!;
    private VBoxContainer _wares = null!;
    private Label _message = null!;
    private Label _hint = null!;
    private Button _close = null!;
    private ScrollContainer _scroll = null!;
    private HBoxContainer _columns = null!;
    private readonly List<Button> _sellButtons = new();
    private readonly List<Button> _buyButtons = new();
    public event Action? CloseRequested;
    public event Action<string>? SellRequested;
    public event Action<string>? BuyRequested;
    /// <summary>ARC-UI-064: an item held in the original look was released (a buy, a sale, or put back).</summary>
    public event Action<AreaInventoryDrop>? DropRequested;
    /// <summary>ARC-UI-1740: an item held over the bet box entered it (the host runs the bet gate and answers with <see cref="SetGambleGate"/>).</summary>
    public event Action<AreaInventoryHeld>? GambleEntered;
    /// <summary>ARC-UI-064: the pointer rested on another item of the original look (its entity) or on none (null).</summary>
    public event Action<string?>? PointerStill;
    /// <summary>ARC-DEV-339 (B54): the arrange button (art 340, IW30) of the original look was pressed and released.</summary>
    public event Action? ArrangeRequested;
    /// <summary>ARC-DEV-339: the arrange button's press (3000) and release (3001) sounds.</summary>
    public event Action<int>? SoundRequested;
    /// <summary>ARC-UI-1120 (BW6): a cycle button of the follower background was released: 0 previous follower, 1 next.</summary>
    public event Action<int>? CycleRequested;
    /// <summary>ARC-UI-064: the window's holder (the PC) on the screen for the round view: its foot point in canvas units and the game
    /// view's zoom; null when it is not drawn.</summary>
    public Func<(Vector2 Foot, float Zoom)?>? HolderView { get; set; }
    // ARC-UI-064: the original look: the art, the owner's choice of the diagnostic view, the switch between them, the frame, the round
    // view, the panel's two backgrounds and the frame's view (the hero's side and the shown object's grid) with the window's own parts.
    private AreaInventoryArtLook? _look;
    private bool _diagnosticChosen;
    private Button _lookToggle = null!;
    private AreaInventoryGridArea _area = null!;
    private AreaRoundView _roundView = null!;
    private StyleBoxFlat _diagnosticStyle = null!;
    private readonly StyleBoxEmpty _originalStyle = new();
    private (AreaInventoryView Inventory, AreaBarterFrame Frame)? _art;
    /// <summary>ARC-UI-1925: puts an item on the cursor (the occupant of a slot a wear move took out, as the own window's swap does).</summary>
    public void Hold(AreaInventoryHeld? held) => _area.Hold(held);
    public bool IsOpen => Visible;
    /// <summary>ARC-UI-038: the room the opening shop's window keeps below a docked window.</summary>
    public const float DockReserve = 230;
    private bool _docked;
    /// <summary>ARC-UI-038: the opening shop's window: at the top, above the shop's Accept and Back (<see cref="DockReserve"/>), without a
    /// Close of its own (the shop's Back closes it).</summary>
    public bool Docked
    {
        get => _docked;
        // Above the menu's backdrop (layer 4), whichever panel the tree made first.
        set { _docked = value; Layer = value ? 5 : 4; if (_close is not null) ApplyLook(); ResizePanel(); }
    }
    public AreaBarterView? View { get; private set; }
    internal IReadOnlyList<Button> SellButtonsForSelfTest => _sellButtons;
    internal IReadOnlyList<Button> BuyButtonsForSelfTest => _buyButtons;
    internal string MessageForSelfTest => _message.Text;
    internal string HeaderForSelfTest => _header.Text;
    internal AreaInventoryGridArea GridAreaForSelfTest => _area;
    internal Button LookToggleForSelfTest => _lookToggle;
    internal bool RoundViewShown => _roundView.Visible;

    /// <summary>ARC-UI-064: the window shows the original barter window: the barter arts read, a shown object and the diagnostic view not
    /// chosen (ARC-UI-069: docked in the opening shop too).</summary>
    public bool ArtMode => _look is { BarterReady: true } && _art is not null && !_diagnosticChosen;

    /// <summary>ARC-UI-064: the frame's view while the window is open (null without the original look's parts).</summary>
    public AreaInventoryView? ArtView => IsOpen ? _art?.Inventory : null;

    /// <summary>ARC-UI-064: sets the original look the window can show (null: the diagnostic window only).</summary>
    public void SetArtLook(AreaInventoryArtLook? look)
    {
        _look = look;
        if (IsOpen && View is { } view) Render(view, _message.Text, _art);
    }

    public override void _Ready()
    {
        Layer = 4;
        _panel = new PanelContainer { Name = "AreaBarter", MouseFilter = Control.MouseFilterEnum.Stop };
        _diagnosticStyle = new StyleBoxFlat
        {
            BgColor = new Color(14 / 255f, 16 / 255f, 22 / 255f, .96f),
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12,
        };
        _panel.AddThemeStyleboxOverride("panel", _diagnosticStyle);
        AddChild(_panel);
        var contents = new VBoxContainer(); _panel.AddChild(contents);
        _header = new Label { Modulate = new Color(.7f, .75f, .8f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _columns = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        _scroll.AddChild(_columns);
        var left = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _heroHeader = new Label { Modulate = new Color(1f, .85f, .55f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _waresHeader = new Label { Modulate = new Color(1f, .85f, .55f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _hero = new VBoxContainer(); _wares = new VBoxContainer();
        left.AddChild(_heroHeader); left.AddChild(_hero);
        right.AddChild(_waresHeader); right.AddChild(_wares);
        _columns.AddChild(left); _columns.AddChild(right);
        _message = new Label { Modulate = new Color(.6f, .8f, .65f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _close = new Button { Name = "Close", Text = "Close", Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.All };
        _close.Pressed += () => CloseRequested?.Invoke();
        _hint = new Label { Modulate = new Color(.55f, .6f, .65f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _area = new AreaInventoryGridArea { Name = "BarterFrame", Visible = false };
        _area.DropRequested += drop => DropRequested?.Invoke(drop);
        _area.PointerStill += item => PointerStill?.Invoke(item);
        _area.GambleEntered += held => GambleEntered?.Invoke(held);
        _area.ArrangeRequested += () => ArrangeRequested?.Invoke();
        _area.SoundRequested += id => SoundRequested?.Invoke(id);
        _area.CycleRequested += direction => CycleRequested?.Invoke(direction);
        _area.HoleReleased += () => CloseRequested?.Invoke();
        _lookToggle = new Button { Name = "LookToggle", Flat = true, FocusMode = Control.FocusModeEnum.All, Alignment = HorizontalAlignment.Left, Visible = false };
        _lookToggle.Pressed += () => Callable.From(() =>
        {
            _diagnosticChosen = !_diagnosticChosen;
            if (View is { } view) Render(view, _message.Text, _art);
        }).CallDeferred();
        foreach (var control in new Control[] { _header, _area, _scroll, _message, _close, _hint, _lookToggle }) contents.AddChild(control);
        _roundView = new AreaRoundView(); AddChild(_roundView);
        GetViewport().SizeChanged += ResizePanel;
        ResizePanel(); Hide();
    }

    /// <summary>Opens the window on <paramref name="view"/>; ARC-UI-064: <paramref name="art"/>, the original look's view and parts (null:
    /// the diagnostic window only).</summary>
    public void Open(AreaBarterView view, string? message = null, (AreaInventoryView Inventory, AreaBarterFrame Frame)? art = null)
    {
        _area.ResetStill(); _area.ResetLoot(); Show(); Render(view, message, art);
    }

    public void CloseView()
    {
        // ARC-UI-064: an item held on the cursor goes back first (0x575770, IW1): it never left its place.
        _area.Hold(null);
        View = null; _art = null; Hide(); _roundView.Visible = false;
        GetViewport().GuiReleaseFocus();
    }

    /// <summary>Shows the new lists; focus stays on the same row index when it still exists. ARC-UI-064: <paramref name="art"/>, the original
    /// look's view and parts (the hero's side, the shown object's grid, the background, the name and the quote line).</summary>
    public void Render(AreaBarterView view, string? message, (AreaInventoryView Inventory, AreaBarterFrame Frame)? art = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        _art = art;
        if (art is { } frame) { _area.SetBarter(frame.Frame); _area.Render(frame.Inventory); }
        var focus = Focusable().FindIndex(button => button.HasFocus());
        foreach (var button in _sellButtons) { _hero.RemoveChild(button); button.QueueFree(); }
        foreach (var button in _buyButtons) { _wares.RemoveChild(button); button.QueueFree(); }
        _sellButtons.Clear(); _buyButtons.Clear();
        View = view;
        var merchant = view.Merchant;
        _header.Text = $"Barter with {merchant.Name}{(merchant.Follower ? " (follower: no prices)" : "")} | hero gold {view.HeroGold} | merchant gold {view.MerchantGold}"
            + $" | haggle {view.Haggle} ({view.Training}) | reaction {view.Reaction} | price multiplier {merchant.Multiplier}"
            + $" | buys {merchant.BuyList.Status}{(merchant.ContainerBuyList is { } box ? $" (container {box.Status})" : "")}"
            + $"{(merchant.Fence ? " | fence" : "")}{(merchant.BuyScript ? " | buy script (not run)" : "")}";
        _heroHeader.Text = $"Hero: {(view.Hero.Count == 0 ? "nothing to sell" : $"{view.Hero.Count} items")}";
        _waresHeader.Text = $"{merchant.Name}{(merchant.Container is null ? "" : " (stock chest)")}: {(view.Wares.Count == 0 ? "nothing to sell" : $"{view.Wares.Count} wares")}";
        foreach (var line in view.Hero) _sellButtons.Add(Add(_hero, $"Sell{_sellButtons.Count + 1}", "Sell", line, view, SellRequested));
        foreach (var line in view.Wares) _buyButtons.Add(Add(_wares, $"Buy{_buyButtons.Count + 1}", "Buy", line, view, BuyRequested));
        _message.Text = message ?? string.Empty;
        _hint.Text = "Click a line to sell or buy it (a stack trades whole; controller: D-pad, confirm) | Escape or cancel closes | "
            + "PROJECT diagnostic window: no grid, paperdoll, count dialog or quote lines; original UI parity NOT_VERIFIED";
        ApplyLook();
        var enabled = Focusable();
        enabled[Math.Clamp(focus < 0 ? 0 : focus, 0, enabled.Count - 1)].GrabFocus();
        ResizePanel(); UpdateRoundView();
    }

    /// <summary>ARC-UI-064: the original look hides the diagnostic lists, buttons and texts (the frame's drags trade, its quote line shows
    /// the messages); the switch below the frame changes to the diagnostic window and back.</summary>
    private void ApplyLook()
    {
        var art = ArtMode;
        // ARC-UI-069: in the opening shop the main menu is up: no round view and no release in its square (IW33, IW34).
        _area.MenuUp = _docked;
        _area.SetLook(art ? _look : null);
        _area.Visible = art;
        foreach (var control in new Control[] { _header, _scroll, _message, _hint }) control.Visible = !art;
        _close.Visible = !art && !_docked;
        _lookToggle.Visible = _look is { BarterReady: true } && _art is not null && !_docked;
        _lookToggle.Text = art ? "Diagnostic view" : "Original look";
        _panel.AddThemeStyleboxOverride("panel", art ? _originalStyle : _diagnosticStyle);
    }

    /// <summary>ARC-UI-1740: the bet gate's answer for the item held over the bet box: the box shows its "on" art (passes) or "refused" art.</summary>
    public void SetGambleGate(bool passes) => _area.SetGambleGate(passes);

    /// <summary>ARC-UI-064: puts <paramref name="quote"/> on the original look's quote line (and the diagnostic message) without rebuilding
    /// the window.</summary>
    public void ShowQuote(string quote)
    {
        ArgumentNullException.ThrowIfNull(quote);
        _message.Text = quote;
        if (_art is not { } art) return;
        _art = (art.Inventory, art.Frame with { Quote = quote });
        _area.SetBarter(_art.Value.Frame);
    }

    private void UpdateRoundView() => _roundView.Update(_area, IsOpen && ArtMode && !_docked ? HolderView?.Invoke() : null);

    private static Button Add(VBoxContainer column, string name, string verb, AreaBarterLine line, AreaBarterView view, Action<string>? requested)
    {
        var free = view.Merchant.Follower;
        var price = free ? "free" : line.Quote.Price > 0
            ? line.Count > 1 ? $"{line.Quote.Price} each, {(long)line.Quote.Price * line.Count}" : $"{line.Quote.Price}"
            : $"no price ({line.Quote.Refusal})";
        var entity = line.Entity;
        var button = new Button
        {
            Name = name, Text = $"{verb}: {line.Text} — {price}", Alignment = HorizontalAlignment.Left,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, FocusMode = Control.FocusModeEnum.All, Disabled = !free && line.Quote.Price == 0,
        };
        // Rebuild after the emitting button's signal returns: Render frees the buttons.
        button.Pressed += () => Callable.From(() => requested?.Invoke(entity)).CallDeferred();
        column.AddChild(button);
        return button;
    }

    private List<Button> Focusable()
    {
        // ARC-UI-064: in the original look the switch to the diagnostic buttons is the one focusable control.
        if (ArtMode) return [_lookToggle];
        var list = _sellButtons.Concat(_buyButtons).Where(button => !button.Disabled).ToList();
        list.Add(_close);
        if (_lookToggle.Visible) list.Add(_lookToggle);
        return list;
    }

    public void Navigate(int direction)
    {
        var enabled = Focusable();
        var focus = enabled.FindIndex(button => button.HasFocus());
        enabled[Math.Clamp(focus < 0 ? 0 : focus + direction, 0, enabled.Count - 1)].GrabFocus();
    }

    public void Confirm()
    {
        var focused = Focusable().FirstOrDefault(button => button.HasFocus());
        if (focused is not null) focused.EmitSignal(BaseButton.SignalName.Pressed);
    }

    private void ResizePanel()
    {
        if (_panel is null) return;
        if (ArtMode && _docked)
        {
            // ARC-UI-069: the window's 800 × 400 at the screen's (0, 41) of the menu's 800 × 600 screen, at the bars' whole factor (as the
            // shop's strips, AreaShopArtView).
            var screen = GetViewport().GetVisibleRect().Size;
            var factor = AreaGameBarPanel.ScreenFactor(screen);
            _area.FitScale(factor);
            var (wx, wy, ww, _) = AreaShopArtLayout.Window;
            _panel.Size = new(ww * factor, AreaInventoryDrag.FrameHeight * factor);
            _panel.Position = new(AreaGameBarPanel.ScreenLeft(screen, factor) + wx * factor,
                MathF.Round((screen.Y - AreaPremadeBrowserArtLayout.ScreenHeight * factor) / 2) + wy * factor);
            return;
        }
        if (ArtMode)
        {
            // ARC-UI-064: the window's 800 × 400 at its place between the bars, scaled to the free part with room for the switch below it
            // (as the inventory window's original look).
            var (top, free) = AreaGameBarPanel.FreeArea(GetViewport().GetVisibleRect().Size);
            var frameWidth = 0f;
            for (var pass = 0; pass < 2; pass++)
            {
                var others = Math.Max(0, _panel.GetCombinedMinimumSize().Y - _area.CustomMinimumSize.Y);
                var scale = Math.Max(.25f, Math.Min(free.X / AreaInventoryDrag.FrameWidth, (free.Y - others) / AreaInventoryDrag.FrameHeight));
                _area.FitScale(scale);
                frameWidth = AreaInventoryDrag.FrameWidth * scale;
                _panel.Size = new(frameWidth, 0);
            }
            _panel.Size = new(frameWidth, Math.Min(_panel.GetCombinedMinimumSize().Y, free.Y));
            _panel.Position = new((free.X - frameWidth) / 2, top);
            return;
        }
        var size = GetViewport().GetVisibleRect().Size;
        var width = Math.Max(1, Math.Min(1040, size.X - 48));
        var reserve = _docked ? DockReserve : 0;
        _scroll.CustomMinimumSize = new(0, Math.Max(40, Math.Min(_columns.GetCombinedMinimumSize().Y, size.Y - 260 - reserve)));
        _panel.Size = new(width, 0);
        var height = Math.Min(_panel.GetCombinedMinimumSize().Y, Math.Max(1, size.Y - 48 - reserve));
        _panel.Size = new(width, height);
        _panel.Position = new((size.X - width) / 2, _docked ? 24 : Math.Max(24, (size.Y - height) / 2));
    }

    public override void _Process(double delta)
    {
        if (IsOpen) ResizePanel();
        UpdateRoundView();
    }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}

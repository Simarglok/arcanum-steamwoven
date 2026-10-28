using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-050 diagnostic barter window: the hero's items with their sale quotes and the merchant's wares with their buy quotes, both
/// sides' gold, the hero's haggle and the merchant's reaction. A button per line sells or buys it (a stack trades whole); a quote of 0
/// is shown with its reason and its button is disabled outside a follower's window. It renders an <see cref="AreaBarterView"/>; the app
/// runs every trade through the session.
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
        set { _docked = value; Layer = value ? 5 : 4; if (_close is not null) _close.Visible = !value; ResizePanel(); }
    }
    public AreaBarterView? View { get; private set; }
    internal IReadOnlyList<Button> SellButtonsForSelfTest => _sellButtons;
    internal IReadOnlyList<Button> BuyButtonsForSelfTest => _buyButtons;
    internal string MessageForSelfTest => _message.Text;
    internal string HeaderForSelfTest => _header.Text;

    public override void _Ready()
    {
        Layer = 4;
        _panel = new PanelContainer { Name = "AreaBarter", MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(14 / 255f, 16 / 255f, 22 / 255f, .96f),
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12,
        });
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
        foreach (var control in new Control[] { _header, _scroll, _message, _close, _hint }) contents.AddChild(control);
        GetViewport().SizeChanged += ResizePanel;
        ResizePanel(); Hide();
    }

    public void Open(AreaBarterView view, string? message = null)
    {
        Show(); Render(view, message);
    }

    public void CloseView()
    {
        View = null; Hide();
        GetViewport().GuiReleaseFocus();
    }

    /// <summary>Shows the new lists; focus stays on the same row index when it still exists.</summary>
    public void Render(AreaBarterView view, string? message)
    {
        ArgumentNullException.ThrowIfNull(view);
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
        var enabled = Focusable();
        enabled[Math.Clamp(focus < 0 ? 0 : focus, 0, enabled.Count - 1)].GrabFocus();
        ResizePanel();
    }

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
        var list = _sellButtons.Concat(_buyButtons).Where(button => !button.Disabled).ToList();
        list.Add(_close);
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
        var size = GetViewport().GetVisibleRect().Size;
        var width = Math.Max(1, Math.Min(1040, size.X - 48));
        var reserve = _docked ? DockReserve : 0;
        _scroll.CustomMinimumSize = new(0, Math.Max(40, Math.Min(_columns.GetCombinedMinimumSize().Y, size.Y - 260 - reserve)));
        _panel.Size = new(width, 0);
        var height = Math.Min(_panel.GetCombinedMinimumSize().Y, Math.Max(1, size.Y - 48 - reserve));
        _panel.Size = new(width, height);
        _panel.Position = new((size.X - width) / 2, _docked ? 24 : Math.Max(24, (size.Y - height) / 2));
    }

    public override void _Process(double delta) { if (IsOpen) ResizePanel(); }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}

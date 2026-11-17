using Arcanum.TerrainViewer.Core;
using Arcanum.Simulation.Items;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-1731 diagnostic item identify / item repair window (the dialog replies <c>ii</c> and <c>ri</c>; contract
/// docs/world/item-service-runtime.md): the hero's items, one button per item that carries its quote, the hero's gold, the NPC's name and the
/// last message. Clicking an item selects it and shows its quote; clicking the selected item again pays and does the service (the original's two
/// clicks, ledger IS4). It renders an <see cref="AreaItemServiceView"/>; the app runs every click through the model.
/// ARC-UI-1765: with the game's art the window draws the original look instead (the inventory window's modes 5 and 6: <see cref="AreaInventoryGridArea"/> in its service look,
/// <see cref="AreaServiceArtLayout"/>): the hero's grid and wear slots, the NPC's image and its quote line, the round view; a press on an item is the click. A switch below the frame shows
/// the diagnostic window and back.
/// </summary>
public partial class AreaItemServicePanel : CanvasLayer
{
    private PanelContainer _panel = null!;
    private Label _header = null!;
    private VBoxContainer _items = null!;
    private Label _message = null!;
    private Label _hint = null!;
    private Button _close = null!;
    private ScrollContainer _scroll = null!;
    private readonly List<Button> _buttons = new();
    public event Action? CloseRequested;
    /// <summary>An item button was pressed (or, in the original look, an item of the frame): its entity.</summary>
    public event Action<string>? ItemClicked;
    /// <summary>ARC-UI-1765: the arrange button of the original look was pressed and released (ledger IW37: every mode of the window makes it).</summary>
    public event Action? ArrangeRequested;
    /// <summary>ARC-UI-1924 (IW46): a stat hot spot of the original look was released over or entered (its stat lines).</summary>
    public event Action<AreaStatRequest>? StatRequested;
    /// <summary>ARC-UI-1765: the arrange button's press (3000) and release (3001) sounds.</summary>
    public event Action<int>? SoundRequested;
    /// <summary>ARC-UI-1765: the window's holder (the hero) on the screen for the round view: its foot point in canvas units and the game view's zoom; null when it is not drawn.</summary>
    public Func<(Vector2 Foot, float Zoom)?>? HolderView { get; set; }
    /// <summary>ARC-UI-1908: the stored portraits' small images the frame draws where the original's image function answers a portrait.</summary>
    public Func<int, Texture2D?>? PortraitTextures { get => _area.PortraitTextures; set => _area.PortraitTextures = value; }
    // ARC-UI-1765: the original look: the art, the owner's choice of the diagnostic view, the switch between them, the frame, the round view, the panel's two backgrounds and the frame's view.
    private AreaInventoryArtLook? _look;
    private bool _diagnosticChosen;
    private Button _lookToggle = null!;
    private AreaInventoryGridArea _area = null!;
    private AreaRoundView _roundView = null!;
    private StyleBoxFlat _diagnosticStyle = null!;
    private readonly StyleBoxEmpty _originalStyle = new();
    private (AreaInventoryView Inventory, AreaServiceFrame Frame)? _art;
    public bool IsOpen => Visible;
    public AreaItemServiceView? View { get; private set; }
    internal AreaInventoryGridArea GridAreaForSelfTest => _area;
    internal Button LookToggleForSelfTest => _lookToggle;
    internal bool RoundViewShown => _roundView.Visible;

    /// <summary>ARC-UI-1765: the window shows the original look: the inventory art read, the hero's frame view given and the diagnostic view not chosen.</summary>
    public bool ArtMode => _look is not null && _art is not null && !_diagnosticChosen;

    /// <summary>ARC-UI-1765: the frame's view while the window is open (null without the original look's parts).</summary>
    public AreaInventoryView? ArtView => IsOpen ? _art?.Inventory : null;

    /// <summary>ARC-UI-1765: sets the original look the window can show (null: the diagnostic window only).</summary>
    public void SetArtLook(AreaInventoryArtLook? look)
    {
        _look = look;
        if (IsOpen && View is { } view) Render(view, _art);
    }
    internal IReadOnlyList<Button> ButtonsForSelfTest => _buttons;
    internal string MessageForSelfTest => _message.Text;
    internal string HeaderForSelfTest => _header.Text;

    public override void _Ready()
    {
        Layer = 4;
        _panel = new PanelContainer { Name = "AreaItemService", MouseFilter = Control.MouseFilterEnum.Stop };
        _diagnosticStyle = new StyleBoxFlat
        {
            BgColor = new Color(14 / 255f, 16 / 255f, 22 / 255f, .96f),
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12,
        };
        _panel.AddThemeStyleboxOverride("panel", _diagnosticStyle);
        AddChild(_panel);
        var contents = new VBoxContainer(); _panel.AddChild(contents);
        _header = new Label { Modulate = new Color(1f, .85f, .55f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _items = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        _scroll.AddChild(_items);
        _message = new Label { Modulate = new Color(.6f, .8f, .65f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _close = new Button { Name = "Close", Text = "Close", Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.All };
        _close.Pressed += () => CloseRequested?.Invoke();
        _hint = new Label { Modulate = new Color(.55f, .6f, .65f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _area = new AreaInventoryGridArea { Name = "ServiceFrame", Visible = false };
        _area.ServiceItemPressed += entity => ItemClicked?.Invoke(entity);
        _area.ArrangeRequested += () => ArrangeRequested?.Invoke();
        _area.StatRequested += request => StatRequested?.Invoke(request);
        _area.SoundRequested += id => SoundRequested?.Invoke(id);
        _area.HoleReleased += () => CloseRequested?.Invoke();
        _lookToggle = new Button { Name = "LookToggle", Flat = true, FocusMode = Control.FocusModeEnum.All, Alignment = HorizontalAlignment.Left, Visible = false };
        _lookToggle.Pressed += () => Callable.From(() =>
        {
            _diagnosticChosen = !_diagnosticChosen;
            if (View is { } view) Render(view, _art);
        }).CallDeferred();
        foreach (var control in new Control[] { _header, _area, _scroll, _message, _close, _hint, _lookToggle }) contents.AddChild(control);
        _roundView = new AreaRoundView(); AddChild(_roundView);
        GetViewport().SizeChanged += ResizePanel;
        ResizePanel(); Hide();
    }

    /// <summary>Opens the window on <paramref name="view"/>; ARC-UI-1765: <paramref name="art"/>, the original look's view and parts (null: the diagnostic window only).</summary>
    public void Open(AreaItemServiceView view, (AreaInventoryView Inventory, AreaServiceFrame Frame)? art = null)
    {
        _area.ResetStill(); _area.ResetLoot(); Show(); Render(view, art);
    }

    public void CloseView()
    {
        View = null; _art = null; Hide(); _roundView.Visible = false;
        GetViewport().GuiReleaseFocus();
    }

    /// <summary>Shows the new lines; focus stays on the same row index when it still exists. ARC-UI-1765: <paramref name="art"/>, the original look's view and parts (the hero's grid and doll, the
    /// NPC's image, the quote line and the remembered item).</summary>
    public void Render(AreaItemServiceView view, (AreaInventoryView Inventory, AreaServiceFrame Frame)? art = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        _art = art;
        if (art is { } frame) { _area.SetService(frame.Frame); _area.Render(frame.Inventory); }
        var focus = Focusable().FindIndex(button => button.HasFocus());
        foreach (var button in _buttons) { _items.RemoveChild(button); button.QueueFree(); }
        _buttons.Clear();
        View = view;
        var verb = view.Kind == ItemServiceKind.Identify ? "Identify" : "Repair";
        _header.Text = $"{verb} by {view.Npc} | hero gold {view.HeroGold} | {(view.Items.Count == 0 ? "no items" : $"{view.Items.Count} items")}"
            + (view.KeptPrice > 0 ? $" | kept price {view.KeptPrice}" : "");
        foreach (var line in view.Items) _buttons.Add(Add(line, view));
        _message.Text = view.Message;
        _hint.Text = "Click an item to see the quote, click it again to pay (controller: D-pad, confirm) | Escape or cancel closes | "
            + "PROJECT diagnostic window: no grid, paperdoll or original-look quote line; original UI parity NOT_VERIFIED";
        ApplyLook();
        var enabled = Focusable();
        enabled[Math.Clamp(focus < 0 ? 0 : focus, 0, enabled.Count - 1)].GrabFocus();
        ResizePanel(); UpdateRoundView();
    }

    /// <summary>ARC-UI-1765: the original look hides the diagnostic list, buttons and texts (a press on an item of the frame is the click); the switch below the frame changes to the diagnostic window and back.</summary>
    private void ApplyLook()
    {
        var art = ArtMode;
        _area.SetLook(art ? _look : null);
        _area.Visible = art;
        foreach (var control in new Control[] { _header, _scroll, _message, _close, _hint }) control.Visible = !art;
        _lookToggle.Visible = _look is not null && _art is not null;
        _lookToggle.Text = art ? "Diagnostic view" : "Original look";
        _panel.AddThemeStyleboxOverride("panel", art ? _originalStyle : _diagnosticStyle);
    }

    /// <summary>ARC-UI-2071 (ledger DH10): the lone left release after the reply that opened the window (screen point); only the original look takes it.
    /// True when the window took it into account.</summary>
    public bool LoneRelease(Vector2 screen)
    {
        if (!IsOpen || !ArtMode) return false;
        _area.LoneRelease(screen);
        return true;
    }

    private void UpdateRoundView() => _roundView.Update(_area, IsOpen && ArtMode ? HolderView?.Invoke() : null);

    private Button Add(AreaItemServiceLine line, AreaItemServiceView view)
    {
        var price = line.Quote.Quoted ? $"{line.Quote.Price} gold" : line.Quote.Status switch
        {
            ItemServiceStatus.AlreadyIdentified => "identified",
            ItemServiceStatus.NothingToRepair => "nothing to repair",
            _ => "broken: needs a master",
        };
        var entity = line.Entity;
        var button = new Button
        {
            Name = $"Item{_buttons.Count + 1}", Text = $"{(line.Entity == view.Selected ? "▶ " : "")}{line.Text} — {price}", Alignment = HorizontalAlignment.Left,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, FocusMode = Control.FocusModeEnum.All,
        };
        // Rebuild after the emitting button's signal returns: Render frees the buttons.
        button.Pressed += () => Callable.From(() => ItemClicked?.Invoke(entity)).CallDeferred();
        _items.AddChild(button);
        return button;
    }

    private List<Button> Focusable()
    {
        // ARC-UI-1765: in the original look the switch to the diagnostic buttons is the one focusable control.
        if (ArtMode) return [_lookToggle];
        var list = _buttons.ToList();
        list.Add(_close);
        if (_lookToggle.Visible) list.Add(_lookToggle);
        return list;
    }

    // ARC-UI-1765: a pad or the keyboard in the original look: the D-pad steps over the hero's items and quotes the one it lands on (a click on another item only selects it, as the original's
    // hover quotes), confirm clicks the remembered item (the second click pays) or, with none remembered, the first. The diagnostic switch is the mouse's.
    private void PadMove(int direction)
    {
        if (View is not { Items.Count: > 0 } view) return;
        var at = -1;
        for (var i = 0; i < view.Items.Count; i++) if (view.Items[i].Entity == view.Selected) at = i;
        var next = Math.Clamp(at < 0 ? (direction >= 0 ? 0 : view.Items.Count - 1) : at + direction, 0, view.Items.Count - 1);
        if (view.Items[next].Entity != view.Selected) ItemClicked?.Invoke(view.Items[next].Entity);
    }

    public void Navigate(int direction)
    {
        if (ArtMode) { PadMove(direction); return; }
        var enabled = Focusable();
        var focus = enabled.FindIndex(button => button.HasFocus());
        enabled[Math.Clamp(focus < 0 ? 0 : focus + direction, 0, enabled.Count - 1)].GrabFocus();
    }

    public void Confirm()
    {
        if (ArtMode)
        {
            if (View is { Items.Count: > 0 } view) ItemClicked?.Invoke(view.Selected ?? view.Items[0].Entity);
            return;
        }
        var focused = Focusable().FirstOrDefault(button => button.HasFocus());
        if (focused is not null) focused.EmitSignal(BaseButton.SignalName.Pressed);
    }

    private void ResizePanel()
    {
        if (_panel is null) return;
        if (ArtMode)
        {
            // ARC-UI-1765: the window's 800 × 400 at its place between the bars, scaled to the free part with room for the switch below it (as the inventory and barter windows' original look).
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
            _panel.Position = new(AreaGameBarPanel.FreeLeft + (free.X - frameWidth) / 2, top);
            return;
        }
        var size = GetViewport().GetVisibleRect().Size;
        var width = Math.Max(1, Math.Min(900, size.X - 48));
        _scroll.CustomMinimumSize = new(0, Math.Max(40, Math.Min(_items.GetCombinedMinimumSize().Y, size.Y - 260)));
        _panel.Size = new(width, 0);
        var height = Math.Min(_panel.GetCombinedMinimumSize().Y, Math.Max(1, size.Y - 48));
        _panel.Size = new(width, height);
        _panel.Position = new((size.X - width) / 2, Math.Max(24, (size.Y - height) / 2));
    }

    public override void _Process(double delta)
    {
        if (IsOpen) ResizePanel();
        UpdateRoundView();
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}

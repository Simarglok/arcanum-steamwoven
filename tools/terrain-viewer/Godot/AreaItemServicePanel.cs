using Arcanum.Simulation.Items;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-1731 diagnostic item identify / item repair window (the dialog replies <c>ii</c> and <c>ri</c>; contract
/// docs/world/item-service-runtime.md): the hero's items, one button per item that carries its quote, the hero's gold, the NPC's name and the
/// last message. Clicking an item selects it and shows its quote; clicking the selected item again pays and does the service (the original's two
/// clicks, ledger IS4). It renders an <see cref="AreaItemServiceView"/>; the app runs every click through the model. The original look of the
/// window (the inventory window's grid and paperdoll in modes 5 and 6) is not built: PROJECT diagnostic window, original UI parity NOT_VERIFIED.
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
    /// <summary>An item button was pressed: its entity.</summary>
    public event Action<string>? ItemClicked;
    public bool IsOpen => Visible;
    public AreaItemServiceView? View { get; private set; }
    internal IReadOnlyList<Button> ButtonsForSelfTest => _buttons;
    internal string MessageForSelfTest => _message.Text;
    internal string HeaderForSelfTest => _header.Text;

    public override void _Ready()
    {
        Layer = 4;
        _panel = new PanelContainer { Name = "AreaItemService", MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(14 / 255f, 16 / 255f, 22 / 255f, .96f),
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12,
        });
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
        foreach (var control in new Control[] { _header, _scroll, _message, _close, _hint }) contents.AddChild(control);
        GetViewport().SizeChanged += ResizePanel;
        ResizePanel(); Hide();
    }

    public void Open(AreaItemServiceView view)
    {
        Show(); Render(view);
    }

    public void CloseView()
    {
        View = null; Hide();
        GetViewport().GuiReleaseFocus();
    }

    /// <summary>Shows the new lines; focus stays on the same row index when it still exists.</summary>
    public void Render(AreaItemServiceView view)
    {
        ArgumentNullException.ThrowIfNull(view);
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
            + "PROJECT diagnostic window: no grid, paperdoll or generated NPC lines; original UI parity NOT_VERIFIED";
        var enabled = Focusable();
        enabled[Math.Clamp(focus < 0 ? 0 : focus, 0, enabled.Count - 1)].GrabFocus();
        ResizePanel();
    }

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
        var list = _buttons.ToList();
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
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}

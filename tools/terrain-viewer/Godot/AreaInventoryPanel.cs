using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-004 diagnostic inventory window: the PC's list (items taken from the world, in order) and, when a dead NPC is
/// looted, that body's current list with a take button per item and "take all". It renders an
/// <see cref="AreaInventoryView"/>; the app runs every transfer through the session.
/// </summary>
public partial class AreaInventoryPanel : CanvasLayer
{
    private PanelContainer _panel = null!;
    private Label _header = null!;
    private Label _pc = null!;
    private VBoxContainer _use = null!;
    private readonly List<Button> _useButtons = new();
    private readonly List<Button> _pickButtons = new();
    private Label _lootHeader = null!;
    private VBoxContainer _loot = null!;
    private Label _message = null!;
    private Label _hint = null!;
    private Button _takeAll = null!;
    private Button _close = null!;
    private ScrollContainer _scroll = null!;
    private HBoxContainer _columns = null!;
    private readonly List<Button> _buttons = new();
    private AreaInventoryView? _view;
    public event Action? CloseRequested;
    public event Action<string>? TakeRequested;
    public event Action? TakeAllRequested;
    /// <summary>ARC-SKILLS-005: use a healing item of the PC's list on the PC.</summary>
    public event Action<string>? UseRequested;
    /// <summary>ARC-SKILLS-006: lockpicks chosen to pick a lock (the original's use of lockpicks on an object).</summary>
    public event Action<string>? PickRequested;
    public bool IsOpen => Visible;
    /// <summary>The looted holder's entity, or null in the plain inventory view.</summary>
    public string? Holder => IsOpen ? _view?.Holder : null;
    internal AreaInventoryView? ViewForSelfTest => _view;
    internal IReadOnlyList<Button> TakeButtonsForSelfTest => _buttons;
    internal IReadOnlyList<Button> UseButtonsForSelfTest => _useButtons;
    internal IReadOnlyList<Button> PickButtonsForSelfTest => _pickButtons;
    internal Button TakeAllForSelfTest => _takeAll;
    internal string MessageForSelfTest => _message.Text;
    internal string PcTextForSelfTest => _pc.Text;

    public override void _Ready()
    {
        Layer = 4;
        _panel = new PanelContainer { Name = "AreaInventory", MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(14 / 255f, 16 / 255f, 22 / 255f, .96f),
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12,
        });
        AddChild(_panel);
        var contents = new VBoxContainer(); _panel.AddChild(contents);
        _header = new Label { Modulate = new Color(.7f, .75f, .8f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        // 43 items fit the PC's list on real data; the columns scroll inside the window.
        var columns = _columns = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        _scroll.AddChild(columns);
        var left = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _pc = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        left.AddChild(new Label { Text = "PC (taken from the world)", Modulate = new Color(1f, .85f, .55f) }); left.AddChild(_pc);
        _use = new VBoxContainer(); left.AddChild(_use);
        _lootHeader = new Label { Modulate = new Color(1f, .85f, .55f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _loot = new VBoxContainer();
        _takeAll = new Button { Name = "TakeAll", Text = "Take all", Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.All };
        _takeAll.Pressed += () => Callable.From(() => TakeAllRequested?.Invoke()).CallDeferred();
        foreach (var control in new Control[] { _lootHeader, _loot, _takeAll }) right.AddChild(control);
        columns.AddChild(left); columns.AddChild(right);
        _message = new Label { Modulate = new Color(.6f, .8f, .65f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _close = new Button { Name = "Close", Text = "Close", Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.All };
        _close.Pressed += () => CloseRequested?.Invoke();
        _hint = new Label { Modulate = new Color(.55f, .6f, .65f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        foreach (var control in new Control[] { _header, _scroll, _message, _close, _hint }) contents.AddChild(control);
        GetViewport().SizeChanged += ResizePanel;
        ResizePanel(); Hide();
    }

    public void Open(AreaInventoryView view, string? message = null)
    {
        Show(); Render(view, message);
    }

    public void CloseView()
    {
        _view = null; Hide();
        GetViewport().GuiReleaseFocus();
    }

    /// <summary>Shows the new lists; focus stays on the same row index when it still exists.</summary>
    public void Render(AreaInventoryView view, string? message)
    {
        ArgumentNullException.ThrowIfNull(view);
        var focus = Focusable().FindIndex(button => button.HasFocus());
        foreach (var button in _buttons) { _loot.RemoveChild(button); button.QueueFree(); }
        _buttons.Clear();
        foreach (var button in _useButtons.Concat(_pickButtons)) { _use.RemoveChild(button); button.QueueFree(); }
        _useButtons.Clear(); _pickButtons.Clear();
        _view = view;
        _header.Text = $"Inventory | PC items {view.Pc.Count} | gold {view.Gold}" + (view.Holder is null ? string.Empty : " | looting a dead NPC");
        _pc.Text = view.Pc.Count == 0 ? "(nothing taken yet)" : string.Join("\n", view.Pc.Select((line, index) => $"{index + 1}. {line.Text}"));
        foreach (var line in view.Pc.Where(line => line.Healing))
        {
            var entity = line.Entity;
            var use = new Button
            {
                Name = $"Use{_useButtons.Count + 1}", Text = "Use on self: " + line.Text, Alignment = HorizontalAlignment.Left,
                AutowrapMode = TextServer.AutowrapMode.WordSmart, FocusMode = Control.FocusModeEnum.All,
            };
            use.Pressed += () => Callable.From(() => UseRequested?.Invoke(entity)).CallDeferred();
            _use.AddChild(use); _useButtons.Add(use);
        }
        foreach (var line in view.Pc.Where(line => line.Lockpick))
        {
            var entity = line.Entity;
            var pick = new Button
            {
                Name = $"Pick{_pickButtons.Count + 1}", Text = "Pick a lock with: " + line.Text, Alignment = HorizontalAlignment.Left,
                AutowrapMode = TextServer.AutowrapMode.WordSmart, FocusMode = Control.FocusModeEnum.All,
            };
            pick.Pressed += () => Callable.From(() => PickRequested?.Invoke(entity)).CallDeferred();
            _use.AddChild(pick); _pickButtons.Add(pick);
        }
        var looting = view.Holder is not null;
        _lootHeader.Visible = looting; _loot.Visible = looting; _takeAll.Visible = looting;
        if (looting)
        {
            _lootHeader.Text = $"{view.HolderTitle} (dead): {(view.Loot.Count == 0 ? "nothing left" : $"{view.Loot.Count} items")}";
            foreach (var line in view.Loot)
            {
                var entity = line.Entity;
                var button = new Button
                {
                    Name = $"Take{_buttons.Count + 1}", Text = "Take: " + line.Text, Alignment = HorizontalAlignment.Left,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart, FocusMode = Control.FocusModeEnum.All,
                };
                // Rebuild after the emitting button's signal returns: Render frees the buttons.
                button.Pressed += () => Callable.From(() => TakeRequested?.Invoke(entity)).CallDeferred();
                _loot.AddChild(button); _buttons.Add(button);
            }
            _takeAll.Disabled = view.Loot.Count == 0;
        }
        _message.Text = message ?? string.Empty;
        _hint.Text = (looting ? "Click an item to take it (controller: D-pad, confirm) | " : string.Empty)
            + (_useButtons.Count > 0 ? "Use on self heals the hero with a healing item | " : string.Empty)
            + (_pickButtons.Count > 0 ? "Pick a lock closes the window; then press a door or window | " : string.Empty)
            + "I, Escape or cancel closes | PROJECT diagnostic window: no grid, equipment, drop or weight limit; original UI parity NOT_VERIFIED";
        var enabled = Focusable();
        enabled[Math.Clamp(focus < 0 ? 0 : focus, 0, enabled.Count - 1)].GrabFocus();
        ResizePanel();
    }

    private List<Button> Focusable()
    {
        var list = _useButtons.Concat(_pickButtons).Concat(_buttons).ToList();
        if (_takeAll.Visible && !_takeAll.Disabled) list.Add(_takeAll);
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
        var width = Math.Max(1, Math.Min(960, size.X - 48));
        _scroll.CustomMinimumSize = new(0, Math.Max(40, Math.Min(_columns.GetCombinedMinimumSize().Y, size.Y - 260)));
        _panel.Size = new(width, 0);
        var height = Math.Min(_panel.GetCombinedMinimumSize().Y, Math.Max(1, size.Y - 48));
        _panel.Size = new(width, height);
        _panel.Position = new((size.X - width) / 2, Math.Max(24, (size.Y - height) / 2));
    }

    public override void _Process(double delta) { if (IsOpen) ResizePanel(); }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}

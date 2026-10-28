using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-004 diagnostic inventory window: the PC's list (items taken from the world, in order) and, when a dead NPC is
/// looted, that body's current list with a take button per item and "take all". ARC-UI-015: above the list the paper doll (the
/// nine wear slots, a take-off button per worn item), a wear button per slot an item can go to, and the weight line. It renders an
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
    private readonly List<Button> _repairButtons = new();
    private Label _doll = null!;
    private VBoxContainer _wear = null!;
    private readonly List<Button> _wearButtons = new();
    private readonly List<Button> _takeOffButtons = new();
    private readonly List<(string Entity, EquipmentSlot Slot)> _wearTargets = new();
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
    /// <summary>ARC-UI-049: use a written item of the PC's list (read it, or register a schematic document).</summary>
    public event Action<string>? ReadRequested;
    /// <summary>ARC-SKILLS-006: lockpicks chosen to pick a lock (the original's use of lockpicks on an object).</summary>
    public event Action<string>? PickRequested;
    /// <summary>ARC-SKILLS-007: an item chosen for the hero's Repair skill (the original's skill-bar repair on an inventory item).</summary>
    public event Action<string>? RepairRequested;
    /// <summary>ARC-UI-015: put a PC item into a wear slot (the original's drop of a held item on a doll slot).</summary>
    public event Action<string, EquipmentSlot>? WearRequested;
    /// <summary>ARC-UI-015: take the item of a wear slot off into the pack (the original's drop of a worn item on the grid).</summary>
    public event Action<EquipmentSlot>? TakeOffRequested;
    public bool IsOpen => Visible;
    /// <summary>The looted holder's entity, or null in the plain inventory view.</summary>
    public string? Holder => IsOpen ? _view?.Holder : null;
    internal AreaInventoryView? ViewForSelfTest => _view;
    internal IReadOnlyList<Button> TakeButtonsForSelfTest => _buttons;
    internal IReadOnlyList<Button> UseButtonsForSelfTest => _useButtons;
    internal IReadOnlyList<Button> PickButtonsForSelfTest => _pickButtons;
    internal IReadOnlyList<Button> RepairButtonsForSelfTest => _repairButtons;
    internal IReadOnlyList<Button> WearButtonsForSelfTest => _wearButtons;
    internal IReadOnlyList<Button> TakeOffButtonsForSelfTest => _takeOffButtons;
    internal IReadOnlyList<(string Entity, EquipmentSlot Slot)> WearTargetsForSelfTest => _wearTargets;
    internal string DollTextForSelfTest => _doll.Text;
    internal string HeaderForSelfTest => _header.Text;
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
        _doll = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _wear = new VBoxContainer();
        left.AddChild(new Label { Text = "Worn (paper doll)", Modulate = new Color(1f, .85f, .55f) }); left.AddChild(_doll); left.AddChild(_wear);
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
        foreach (var button in _useButtons.Concat(_pickButtons).Concat(_repairButtons)) { _use.RemoveChild(button); button.QueueFree(); }
        _useButtons.Clear(); _pickButtons.Clear(); _repairButtons.Clear();
        foreach (var button in _takeOffButtons.Concat(_wearButtons)) { _wear.RemoveChild(button); button.QueueFree(); }
        _takeOffButtons.Clear(); _wearButtons.Clear(); _wearTargets.Clear();
        _view = view;
        _header.Text = $"Inventory | PC items {view.Pc.Count} | gold {view.Gold}" + (view.Carry is { } carry ? " | " + CarryText(carry) : string.Empty)
            + (view.Holder is null ? string.Empty : " | looting a dead NPC");
        _doll.Text = view.Doll.Count == 0 ? "(no hero: nothing can be worn)"
            : string.Join("\n", view.Doll.Select(slot => $"{AreaInventoryHost.SlotName(slot.Slot)}: {slot.Name ?? "-"}"));
        foreach (var slot in view.Doll.Where(slot => slot.Entity is not null))
        {
            var wearSlot = slot.Slot;
            var off = new Button
            {
                Name = $"TakeOff{_takeOffButtons.Count + 1}", Text = $"Take off ({AreaInventoryHost.SlotName(wearSlot)}): {slot.Name}", Alignment = HorizontalAlignment.Left,
                AutowrapMode = TextServer.AutowrapMode.WordSmart, FocusMode = Control.FocusModeEnum.All,
            };
            off.Pressed += () => Callable.From(() => TakeOffRequested?.Invoke(wearSlot)).CallDeferred();
            _wear.AddChild(off); _takeOffButtons.Add(off);
        }
        foreach (var line in view.Pc.Where(line => line.WearSlots.Count > 0))
            foreach (var wearSlot in line.WearSlots)
            {
                var entity = line.Entity;
                var wear = new Button
                {
                    Name = $"Wear{_wearButtons.Count + 1}", Text = $"Wear ({AreaInventoryHost.SlotName(wearSlot)}): {line.Text}", Alignment = HorizontalAlignment.Left,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart, FocusMode = Control.FocusModeEnum.All,
                };
                wear.Pressed += () => Callable.From(() => WearRequested?.Invoke(entity, wearSlot)).CallDeferred();
                _wear.AddChild(wear); _wearButtons.Add(wear); _wearTargets.Add((entity, wearSlot));
            }
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
        foreach (var line in view.Pc.Where(line => line.Written))
        {
            var entity = line.Entity;
            var read = new Button
            {
                Name = $"Read{_useButtons.Count + 1}", Text = "Read: " + line.Text, Alignment = HorizontalAlignment.Left,
                AutowrapMode = TextServer.AutowrapMode.WordSmart, FocusMode = Control.FocusModeEnum.All,
            };
            read.Pressed += () => Callable.From(() => ReadRequested?.Invoke(entity)).CallDeferred();
            _use.AddChild(read); _useButtons.Add(read);
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
        foreach (var line in view.Pc.Where(line => line.Repairable))
        {
            var entity = line.Entity;
            var repair = new Button
            {
                Name = $"Repair{_repairButtons.Count + 1}", Text = "Repair: " + line.Text, Alignment = HorizontalAlignment.Left,
                AutowrapMode = TextServer.AutowrapMode.WordSmart, FocusMode = Control.FocusModeEnum.All,
            };
            repair.Pressed += () => Callable.From(() => RepairRequested?.Invoke(entity)).CallDeferred();
            _use.AddChild(repair); _repairButtons.Add(repair);
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
            + (view.Pc.Any(line => line.Healing) ? "Use on self heals the hero with a healing item | " : string.Empty)
            + (view.Pc.Any(line => line.Written) ? "Read opens a written item (a schematic document registers) | " : string.Empty)
            + (_pickButtons.Count > 0 ? "Pick a lock closes the window; then press a door or window | " : string.Empty)
            + (_repairButtons.Count > 0 ? "Repair uses the hero's Repair skill on a damaged item | " : string.Empty)
            + (_wearButtons.Count + _takeOffButtons.Count > 0 ? "Wear puts an item into a slot (an occupant goes back to the pack); Take off moves it to the pack | " : string.Empty)
            + "I, Escape or cancel closes | PROJECT diagnostic window: no grid, drag, drop or stacks; original UI parity NOT_VERIFIED";
        var enabled = Focusable();
        enabled[Math.Clamp(focus < 0 ? 0 : focus, 0, enabled.Count - 1)].GrabFocus();
        ResizePanel();
    }

    private List<Button> Focusable()
    {
        var list = _useButtons.Concat(_pickButtons).Concat(_repairButtons).Concat(_buttons).ToList();
        if (_takeAll.Visible && !_takeAll.Disabled) list.Add(_takeAll);
        // ARC-UI-015: the doll's buttons come after the earlier ones, so the first focus (a confirm right after opening) stays where it was.
        list.AddRange(_takeOffButtons.Concat(_wearButtons));
        list.Add(_close);
        return list;
    }

    /// <summary>ARC-UI-015: the original's weight lines (ledger IW9): the carried weight, and the encumbrance level with the weight
    /// up to which it holds (the carry weight times the level's threshold).</summary>
    internal static string CarryText(Arcanum.Session.SessionHeroCarry carry) =>
        $"weight {carry.Weight} | encumbrance level {carry.Level} (up to {CarryRules.Threshold(carry.Level, carry.CarryWeight)} of {carry.CarryWeight})";

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
        // ARC-UI-018: inside the part the interface bars leave free.
        var (top, size) = AreaGameBarPanel.FreeArea(GetViewport().GetVisibleRect().Size);
        var width = Math.Max(1, Math.Min(960, size.X - 48));
        _scroll.CustomMinimumSize = new(0, Math.Max(40, Math.Min(_columns.GetCombinedMinimumSize().Y, size.Y - 260)));
        _panel.Size = new(width, 0);
        var height = Math.Min(_panel.GetCombinedMinimumSize().Y, Math.Max(1, size.Y - 48));
        _panel.Size = new(width, height);
        _panel.Position = new((size.X - width) / 2, top + Math.Max(24, (size.Y - height) / 2));
    }

    public override void _Process(double delta) { if (IsOpen) ResizePanel(); }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}

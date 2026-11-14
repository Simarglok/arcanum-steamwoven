using Arcanum.Presentation.Hud;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-004 diagnostic inventory window: the PC's list (items taken from the world, in order) and, when a dead NPC is
/// looted, that body's current list with a take button per item and "take all". ARC-UI-015: above the list the paper doll (the
/// nine wear slots, a take-off button per worn item), a wear button per slot an item can go to, and the weight line. ARC-UI-019: above the
/// lists the doll and the hero's grid, where the mouse picks an item up and drops it (<see cref="AreaInventoryGridArea"/>); the buttons stay
/// for the keyboard and the controller. It renders an <see cref="AreaInventoryView"/>; the app runs every transfer through the session.
/// ARC-UI-029: the quantity window (<see cref="AskQuantity"/>, the original's interface mode 13) asks how many units of a stack move;
/// while it is up only its buttons and keys act.
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
    private readonly List<Button> _dropButtons = new();
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
    private AreaInventoryGridArea _area = null!;
    private readonly List<Button> _buttons = new();
    private AreaInventoryView? _view;
    // ARC-UI-029: the quantity window's row, its buttons (All, +1, −1, Accept, Cancel) and its state while it asks.
    private HBoxContainer _quantity = null!;
    private Label _quantityLabel = null!;
    private readonly List<Button> _quantityButtons = new();
    private QuantityBar? _bar;
    private string _quantityName = string.Empty;
    // ARC-UI-051: the original look of the player's own window, the owner's choice of the diagnostic view instead, the button that
    // switches between them and the panel's two backgrounds.
    private AreaInventoryArtLook? _look;
    private bool _diagnosticChosen;
    private Button _lookToggle = null!;
    private StyleBoxFlat _diagnosticStyle = null!;
    private readonly StyleBoxEmpty _originalStyle = new();
    // ARC-UI-054: the round view's copy of the game view (ARC-UI-064: the component the barter panel shares).
    private AreaRoundView _roundView = null!;
    public event Action? CloseRequested;
    /// <summary>ARC-UI-054: the window's holder on the screen for the round view: its foot point in canvas units and the game view's
    /// zoom (canvas units per game pixel); null when it is not drawn.</summary>
    public Func<(Vector2 Foot, float Zoom)?>? HolderView { get; set; }
    /// <summary>ARC-UI-029: the quantity window's Accept (or Enter) with the count shown, 0 too (QW4).</summary>
    public event Action<int>? QuantityAccepted;
    /// <summary>ARC-UI-029: the quantity window's Cancel: nothing moves.</summary>
    public event Action? QuantityCancelled;
    public event Action<string>? TakeRequested;
    public event Action? TakeAllRequested;
    /// <summary>ARC-DEV-339 (B54): the original look's arrange button was pressed and released (ledger IW37).</summary>
    public event Action? ArrangeRequested;
    /// <summary>ARC-DEV-339: the arrange button's press (3000) and release (3001) sounds.</summary>
    public event Action<int>? SoundRequested;
    /// <summary>ARC-SKILLS-010: in steal mode, plants the hero's item on the NPC (one pick-pocket attempt).</summary>
    public event Action<string>? PlantRequested;
    /// <summary>ARC-SKILLS-005: use a healing item of the PC's list on the PC; ARC-UI-025: use any item of the hero's as a right click on
    /// it does (the item use 0x462CC0 with the hero as user and target, IW5).</summary>
    public event Action<string>? UseRequested;
    /// <summary>ARC-MAGIC-1128: with <see cref="TargetPicking"/> a left press on an item of the own window names it as the spell's target (ledger IW44).</summary>
    public event Action<string>? ItemPicked;
    /// <summary>ARC-MAGIC-1128: a right release in the window while a spell's cursor stands over it cancels the cursor.</summary>
    public event Action? PickCancelled;
    /// <summary>ARC-MAGIC-1128: a spell's cursor (interface mode 1) stands over the window.</summary>
    public bool TargetPicking { get => _area.TargetPicking; set => _area.TargetPicking = value; }
    /// <summary>ARC-UI-035: the pointer rested on another item of the doll or the grid (its entity) or on none (null).</summary>
    public event Action<string?>? PointerStill;
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
    /// <summary>ARC-UI-019: the held item was released on the doll or the grid (or elsewhere: put back).</summary>
    public event Action<AreaInventoryDrop>? DropRequested;
    /// <summary>ARC-UI-063: the held item was released in the own window's round view: the map drop onto the game cursor (IW18).</summary>
    public event Action<AreaInventoryHeld>? MapDropRequested;
    public bool IsOpen => Visible;

    /// <summary>ARC-UI-029: the quantity window is up.</summary>
    public bool Asking => _bar is not null;
    internal QuantityBar? QuantityForSelfTest => _bar;
    /// <summary>ARC-UI-1884: the quantity window's count and maximum while it asks (the original's art pane draws them), null otherwise.</summary>
    public QuantityBar? Quantity => _bar;
    private bool _quantityRowHidden;

    /// <summary>
    /// ARC-UI-1884: the original's art pane in the bottom strip shows the question, so the diagnostic row is not drawn (hidden: <paramref name="hidden"/>); its
    /// buttons stay focusable for the keyboard and the pad but take no mouse.
    /// </summary>
    public void HideQuantityRow(bool hidden)
    {
        if (hidden == _quantityRowHidden) return;
        _quantityRowHidden = hidden;
        _quantity.Modulate = hidden ? new Color(1, 1, 1, 0) : Colors.White;
        foreach (var button in _quantityButtons) button.MouseFilter = hidden ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Stop;
    }
    internal IReadOnlyList<Button> QuantityButtonsForSelfTest => _quantityButtons;
    internal string QuantityTextForSelfTest => _quantityLabel.Text;
    /// <summary>The looted holder's entity, or null in the plain inventory view.</summary>
    public string? Holder => IsOpen ? _view?.Holder : null;

    /// <summary>ARC-SKILLS-010: a Plant press for the smoke (the button's own signal).</summary>
    internal void EmitPlantForSelfTest(string item) => PlantRequested?.Invoke(item);
    internal AreaInventoryView? ViewForSelfTest => _view;
    internal IReadOnlyList<Button> TakeButtonsForSelfTest => _buttons;
    internal IReadOnlyList<Button> UseButtonsForSelfTest => _useButtons;
    internal IReadOnlyList<Button> PickButtonsForSelfTest => _pickButtons;
    internal IReadOnlyList<Button> RepairButtonsForSelfTest => _repairButtons;
    internal IReadOnlyList<Button> DropButtonsForSelfTest => _dropButtons;
    internal IReadOnlyList<Button> WearButtonsForSelfTest => _wearButtons;
    internal IReadOnlyList<Button> TakeOffButtonsForSelfTest => _takeOffButtons;
    internal IReadOnlyList<(string Entity, EquipmentSlot Slot)> WearTargetsForSelfTest => _wearTargets;
    internal string DollTextForSelfTest => _doll.Text;
    internal string HeaderForSelfTest => _header.Text;
    internal Button TakeAllForSelfTest => _takeAll;
    internal string MessageForSelfTest => _message.Text;
    internal string PcTextForSelfTest => _pc.Text;
    internal AreaInventoryGridArea GridAreaForSelfTest => _area;
    internal Button LookToggleForSelfTest => _lookToggle;

    /// <summary>ARC-UI-051: the window shows the original look: its art read, the player's own window (mode 0) and the diagnostic view not
    /// chosen. ARC-UI-052: a loot window (mode 2) too when the loot arts read; ARC-UI-065: and the steal window (mode 3) on the same frame.</summary>
    public bool ArtMode => _look is not null && !_diagnosticChosen && OriginalAvailable;

    /// <summary>The open view has an original look: the player's own window, or a loot or steal window with the loot arts.</summary>
    private bool OriginalAvailable => _view is { Holder: null } || _view is not null && _look is { LootReady: true };

    /// <summary>ARC-UI-052: the open window's view (null when closed).</summary>
    public AreaInventoryView? CurrentView => IsOpen ? _view : null;

    /// <summary>ARC-UI-051: sets the original look the window can show (null: the diagnostic window only).</summary>
    public void SetArtLook(AreaInventoryArtLook? look)
    {
        _look = look;
        if (_view is { } view && IsOpen) Render(view, _message.Text);
    }

    public override void _Ready()
    {
        Layer = 4;
        _panel = new PanelContainer { Name = "AreaInventory", MouseFilter = Control.MouseFilterEnum.Stop };
        _diagnosticStyle = new StyleBoxFlat
        {
            BgColor = new Color(14 / 255f, 16 / 255f, 22 / 255f, .96f),
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12,
        };
        _panel.AddThemeStyleboxOverride("panel", _diagnosticStyle);
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
        _area = new AreaInventoryGridArea { Name = "DollAndGrid" };
        _area.DropRequested += drop => DropRequested?.Invoke(drop);
        _area.UseRequested += held => UseRequested?.Invoke(held.Entity);
        _area.ItemPicked += held => ItemPicked?.Invoke(held.Entity);
        _area.PickCancelled += () => PickCancelled?.Invoke();
        _area.PointerStill += item => PointerStill?.Invoke(item);
        _area.TakeAllRequested += () => TakeAllRequested?.Invoke();
        _area.ArrangeRequested += () => ArrangeRequested?.Invoke();
        _area.SoundRequested += id => SoundRequested?.Invoke(id);
        _area.HoleReleased += () => CloseRequested?.Invoke();
        _area.MapDropRequested += held => MapDropRequested?.Invoke(held);
        _quantity = new HBoxContainer { Name = "Quantity", Visible = false };
        _quantityLabel = new Label { Modulate = new Color(1f, .85f, .55f) };
        _quantity.AddChild(_quantityLabel);
        foreach (var (name, text, action) in new (string, string, Action)[]
        {
            ("QuantityAll", "All", () => PressQuantity(QuantityAction.All)), ("QuantityUp", "+1", () => PressQuantity(QuantityAction.Up)),
            ("QuantityDown", "-1", () => PressQuantity(QuantityAction.Down)), ("QuantityAccept", "Accept", AcceptQuantity), ("QuantityCancel", "Cancel", CancelQuantity),
        })
        {
            var button = new Button { Name = name, Text = text, FocusMode = Control.FocusModeEnum.All };
            button.Pressed += () => Callable.From(action).CallDeferred();
            _quantity.AddChild(button); _quantityButtons.Add(button);
        }
        _lookToggle = new Button { Name = "LookToggle", Flat = true, FocusMode = Control.FocusModeEnum.All, Alignment = HorizontalAlignment.Left, Visible = false };
        _lookToggle.Pressed += () => Callable.From(() =>
        {
            _diagnosticChosen = !_diagnosticChosen;
            if (_view is { } view) Render(view, _message.Text);
        }).CallDeferred();
        foreach (var control in new Control[] { _header, _quantity, _area, _scroll, _message, _close, _hint, _lookToggle }) contents.AddChild(control);
        _roundView = new AreaRoundView(); AddChild(_roundView);
        GetViewport().SizeChanged += ResizePanel;
        ResizePanel(); Hide();
    }

    /// <summary>ARC-UI-054: the round view shows the game view around the holder (IW33) while the original look is up.</summary>
    internal bool RoundViewShown => _roundView.Visible;

    private void UpdateRoundView() => _roundView.Update(_area, IsOpen && ArtMode ? HolderView?.Invoke() : null);

    public void Open(AreaInventoryView view, string? message = null)
    {
        _area.ResetStill(); _area.ResetLoot(); Show(); Render(view, message);
    }

    /// <summary>ARC-UI-019: puts an item on the cursor (a wear move's occupant).</summary>
    public void Hold(AreaInventoryHeld? held) => _area.Hold(held);

    /// <summary>ARC-UI-022: the item held on the cursor, or null (a key 1..0 drops it on a hotkey entry).</summary>
    public AreaInventoryHeld? Held => _area.Held;

    public void CloseView()
    {
        // ARC-UI-029: a question still up goes with the window (nothing moves).
        EndQuantity();
        // An item held on the cursor goes back first (0x575770, IW1): it never left its place.
        _area.Hold(null);
        _view = null; Hide(); _roundView.Visible = false;
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
        foreach (var button in _takeOffButtons.Concat(_wearButtons).Concat(_dropButtons)) { _wear.RemoveChild(button); button.QueueFree(); }
        _takeOffButtons.Clear(); _wearButtons.Clear(); _wearTargets.Clear(); _dropButtons.Clear();
        _view = view;
        _area.Render(view);
        ApplyLook();
        _header.Text = $"Inventory | PC items {view.Pc.Count} | gold {view.Gold} | {AmmoText(view.Ammo)}" + (view.Carry is { } carry ? " | " + CarryText(carry) : string.Empty)
            + (view.Holder is null ? string.Empty : view.Contents ? " | contents of a container (See Contents)" : view.Steal ? " | picking a pocket" : " | looting a dead NPC");
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
        // ARC-UI-025: the drop icon for the keyboard and the controller: one button per held item (a worn one from its slot).
        foreach (var line in view.Pc)
        {
            var held = new AreaInventoryHeld(line.Entity, view.Doll.FirstOrDefault(slot => slot.Entity == line.Entity)?.Slot, 0);
            var drop = new Button
            {
                Name = $"Drop{_dropButtons.Count + 1}", Text = "Drop on the ground: " + line.Text, Alignment = HorizontalAlignment.Left,
                AutowrapMode = TextServer.AutowrapMode.WordSmart, FocusMode = Control.FocusModeEnum.All,
            };
            drop.Pressed += () => Callable.From(() => DropRequested?.Invoke(new(AreaInventoryDropKind.DropToGround, held))).CallDeferred();
            _wear.AddChild(drop); _dropButtons.Add(drop);
        }
        _pc.Text = view.Pc.Count == 0 ? "(nothing taken yet)" : string.Join("\n", view.Pc.Select((line, index) => $"{index + 1}. {line.Text}"));
        foreach (var line in view.Pc.Where(line => line.Usable))
        {
            var entity = line.Entity;
            var use = new Button
            {
                Name = $"Use{_useButtons.Count + 1}", Text = "Use: " + line.Text, Alignment = HorizontalAlignment.Left,
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
        _lootHeader.Visible = looting; _loot.Visible = looting; _takeAll.Visible = looting && !view.Steal && !view.Contents;
        if (looting)
        {
            _lootHeader.Text = view.Contents ? $"{view.HolderTitle} (contents): {(view.Loot.Count == 0 ? "empty" : $"{view.Loot.Count} items")}"
                : view.Steal ? $"{view.HolderTitle} (picking its pocket): {view.Loot.Count} items"
                : $"{view.HolderTitle} (dead): {(view.Loot.Count == 0 ? "nothing left" : $"{view.Loot.Count} items")}";
            foreach (var line in view.Loot)
            {
                var entity = line.Entity;
                var button = new Button
                {
                    Name = $"Take{_buttons.Count + 1}", Text = (view.Contents ? "Contents: " : view.Steal ? "Steal: " : "Take: ") + line.Text, Alignment = HorizontalAlignment.Left,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart, FocusMode = Control.FocusModeEnum.All,
                };
                // Rebuild after the emitting button's signal returns: Render frees the buttons.
                button.Pressed += () => Callable.From(() => TakeRequested?.Invoke(entity)).CallDeferred();
                _loot.AddChild(button); _buttons.Add(button);
            }
            // ARC-SKILLS-010: steal mode plants the hero's items one by one (its coins are a count, not a pile, and stay).
            if (view.Steal)
                foreach (var line in view.Pc)
                {
                    var entity = line.Entity;
                    var plant = new Button
                    {
                        Name = $"Plant{_buttons.Count + 1}", Text = "Plant: " + line.Text, Alignment = HorizontalAlignment.Left,
                        AutowrapMode = TextServer.AutowrapMode.WordSmart, FocusMode = Control.FocusModeEnum.All,
                    };
                    plant.Pressed += () => Callable.From(() => PlantRequested?.Invoke(entity)).CallDeferred();
                    _loot.AddChild(plant); _buttons.Add(plant);
                }
            _takeAll.Disabled = view.Loot.Count == 0;
        }
        _message.Text = message ?? string.Empty;
        _hint.Text = (view.Contents ? "See Contents: the items are only shown, nothing in this window can be picked, moved or taken (ledger XR16) | "
                : view.Steal ? "Steal or Plant is one pick-pocket attempt (4 action points in a turn-based round); a caught thief's window closes | "
                : looting ? "Click an item to take it (controller: D-pad, confirm) | " : string.Empty)
            + (_useButtons.Count > 0 ? "Use (or a right click) uses an item on the hero; the Use icon uses a held item (Shift: on the hero) | " : string.Empty)
            + (view.Pc.Any(line => line.Written) ? "Read opens a written item (a schematic document registers) | " : string.Empty)
            + (_pickButtons.Count > 0 ? "Pick a lock closes the window; then press a door or window | " : string.Empty)
            + (_repairButtons.Count > 0 ? "Repair uses the hero's Repair skill on a damaged item | " : string.Empty)
            + (_wearButtons.Count + _takeOffButtons.Count > 0 ? "Wear puts an item into a slot (an occupant goes back to the pack); Take off moves it to the pack | " : string.Empty)
            + "Mouse: press an item of the doll or grid and release it on a grid cell or a slot (an occupant comes onto the cursor), on Drop to drop it on the hero's tile or on Use to use it"
            + (view.Unplaced.Count > 0 ? $" | no room in the grid: {string.Join(", ", view.Unplaced)}" : string.Empty)
            + " | a gold or ammunition stack asks how many units move (keypad digits, Backspace, Enter; Escape cancels)"
            + " | I, Escape or cancel closes | PROJECT diagnostic window: no throw from the map; original UI parity NOT_VERIFIED";
        // ARC-UI-029: a re-render while the quantity window is up keeps the rest of the window inert.
        if (_bar is not null) SetOthersDisabled(true);
        // ARC-RES-1327 (XR16): the See Contents window moves nothing: every action button is inert, only the close stays.
        if (view.Contents)
            foreach (var button in _useButtons.Concat(_pickButtons).Concat(_repairButtons).Concat(_buttons).Concat(_takeOffButtons).Concat(_wearButtons).Concat(_dropButtons)) button.Disabled = true;
        ShowMessageLine();
        // ARC-UI-051: in the original look only the switch takes focus, so a pad or the keyboard can reach the diagnostic buttons.
        var enabled = Focusable();
        enabled[Math.Clamp(focus < 0 ? 0 : focus, 0, enabled.Count - 1)].GrabFocus();
        ResizePanel(); UpdateRoundView();
    }

    /// <summary>ARC-UI-054: the message line is always laid out. In the original look it keeps one line, empty or not, cut with an ellipsis,
    /// so a message ("took N item(s)") never changes the frame's scale (PROJECT: the original prints none in the window); the frame is
    /// therefore always one line smaller than with no line at all. The diagnostic window wraps it.</summary>
    private void ShowMessageLine()
    {
        var art = ArtMode;
        _message.Visible = true;
        _message.AutowrapMode = art ? TextServer.AutowrapMode.Off : TextServer.AutowrapMode.WordSmart;
        _message.TextOverrunBehavior = art ? TextServer.OverrunBehavior.TrimEllipsis : TextServer.OverrunBehavior.NoTrimming;
    }

    /// <summary>ARC-UI-051: the original look hides the diagnostic lists, buttons and texts (their actions stay: the doll, the grid, the
    /// boxes and a right click); the switch below the frame changes to the diagnostic window and back.</summary>
    private void ApplyLook()
    {
        var art = ArtMode;
        _area.SetLook(art ? _look : null);
        foreach (var control in new Control[] { _header, _scroll, _close, _hint }) control.Visible = !art;
        _lookToggle.Visible = _look is not null && OriginalAvailable;
        _lookToggle.Text = art ? "Diagnostic view" : "Original look";
        _panel.AddThemeStyleboxOverride("panel", art ? _originalStyle : _diagnosticStyle);
    }

    private List<Button> Focusable()
    {
        // ARC-UI-029: while the quantity window asks only its buttons take focus.
        if (_bar is not null) return _quantityButtons.ToList();
        // ARC-UI-051: the original look's diagnostic buttons are hidden; the switch to them is the one focusable control.
        if (ArtMode) return [_lookToggle];
        var list = _useButtons.Concat(_pickButtons).Concat(_repairButtons).Concat(_buttons).ToList();
        if (_takeAll.Visible && !_takeAll.Disabled) list.Add(_takeAll);
        // ARC-UI-015: the doll's buttons come after the earlier ones, so the first focus (a confirm right after opening) stays where it was.
        list.AddRange(_takeOffButtons.Concat(_wearButtons).Concat(_dropButtons));
        list.Add(_close);
        return list;
    }

    /// <summary>
    /// ARC-UI-029: opens the quantity window over a stack of <paramref name="maximum"/> units of <paramref name="name"/> (bar mode 9,
    /// ledger QW2, QW3): the count starts at 0; the rest of the window, the doll and the grid do nothing until Accept or Cancel (QW6).
    /// </summary>
    public void AskQuantity(string name, int maximum, string? message = null)
    {
        // A held item goes back to its place first (0x575770, QW1).
        _area.Hold(null);
        _bar = QuantityBar.Open(maximum);
        _quantityName = name;
        _quantity.Show(); _area.Blocked = true;
        SetOthersDisabled(true);
        ShowQuantity();
        _message.Text = message ?? string.Empty;
        ShowMessageLine();
        _quantityButtons[0].GrabFocus();
        ResizePanel();
    }

    /// <summary>ARC-UI-029: one press on the quantity window (its buttons, the keypad digits, Backspace or Delete).</summary>
    public void PressQuantity(QuantityAction action, int digit = 0)
    {
        if (_bar is not { } bar) return;
        _bar = bar.Apply(action, digit);
        ShowQuantity();
    }

    /// <summary>ARC-UI-029: Accept (or Enter): the question ends and the count shown goes to the app.</summary>
    public void AcceptQuantity()
    {
        if (_bar is not { } bar) return;
        EndQuantity();
        QuantityAccepted?.Invoke(bar.Count);
    }

    /// <summary>ARC-UI-029: Cancel (or Escape): the question ends and nothing moves.</summary>
    public void CancelQuantity()
    {
        if (_bar is null) return;
        EndQuantity();
        QuantityCancelled?.Invoke();
    }

    private void EndQuantity()
    {
        if (_bar is null) return;
        _bar = null;
        _quantity.Hide(); _area.Blocked = false;
        SetOthersDisabled(false);
        // The app re-renders after every answer; this restores the buttons if it does not.
        if (_view is { } view) Render(view, _message.Text);
    }

    private void ShowQuantity() =>
        _quantityLabel.Text = _bar is { } bar ? $"How many of {_quantityName}? {bar.Count.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(QuantityBar.Digits)} of {bar.Maximum}  " : string.Empty;

    private void SetOthersDisabled(bool disabled)
    {
        foreach (var button in _useButtons.Concat(_pickButtons).Concat(_repairButtons).Concat(_buttons).Concat(_takeOffButtons).Concat(_wearButtons)
            .Concat(_dropButtons).Append(_takeAll).Append(_close)) button.Disabled = disabled || button == _takeAll && (_view?.Loot.Count ?? 0) == 0;
    }

    /// <summary>ARC-UI-029: the four ammunition counts after the gold (ledger IW9).</summary>
    internal static string AmmoText(IReadOnlyList<int> ammo)
    {
        int At(int type) => type < ammo.Count ? ammo[type] : 0;
        return $"arrows {At(0)} | bullets {At(1)} | charges {At(2)} | fuel {At(3)}";
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
        if (ArtMode)
        {
            // ARC-UI-051: the window's 800 × 400 at its place between the bars, scaled to the free part with room for what the panel
            // shows besides the frame (the switch, the message line, the quantity row; PROJECT: the original prints no message in the
            // window). ARC-UI-054: the message line is one line whether a message is shown or not, so a message keeps the scale.
            var frameWidth = 0f;
            for (var pass = 0; pass < 2; pass++)
            {
                var others = Math.Max(0, _panel.GetCombinedMinimumSize().Y - _area.CustomMinimumSize.Y);
                var scale = Math.Max(.25f, Math.Min(size.X / AreaInventoryDrag.FrameWidth, (size.Y - others) / AreaInventoryDrag.FrameHeight));
                _area.FitScale(scale);
                frameWidth = AreaInventoryDrag.FrameWidth * scale;
                _panel.Size = new(frameWidth, 0);
            }
            _panel.Size = new(frameWidth, 0);
            _panel.Size = new(frameWidth, Math.Min(_panel.GetCombinedMinimumSize().Y, size.Y));
            _panel.Position = new((size.X - frameWidth) / 2, top);
            return;
        }
        var width = Math.Max(1, Math.Min(960, size.X - 48));
        _area.Fit(Math.Min(width - 32, (size.Y - 300) * AreaInventoryDrag.FrameWidth / AreaInventoryDrag.FrameHeight));
        _scroll.CustomMinimumSize = new(0, Math.Max(40, Math.Min(_columns.GetCombinedMinimumSize().Y, size.Y - 260 - _area.CustomMinimumSize.Y)));
        _panel.Size = new(width, 0);
        var height = Math.Min(_panel.GetCombinedMinimumSize().Y, Math.Max(1, size.Y - 48));
        _panel.Size = new(width, height);
        _panel.Position = new((size.X - width) / 2, top + Math.Max(24, (size.Y - height) / 2));
    }

    public override void _Process(double delta)
    {
        if (IsOpen) ResizePanel();
        UpdateRoundView();
    }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}

using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-MAGIC-015: the AREA viewer's spell pane, the smallest bounded stand-in for the original's interface bar in its spell mode
/// (opened with M): one button per known spell, college by college, with its fatigue cost. A press activates the spell as the
/// original's pane does (0x57BC70). The pane is not a window: the world keeps its clicks, and its buttons take no focus. A
/// diagnostic panel: no original art.
/// ARC-UI-016: the bar's weapon button sits in the pane's header while the wielded weapon qualifies (0x556EF0); it opens the weapon's
/// spell pane (bar mode 8) in place of the spell list, and a second press returns to the list (0x5506C0's toggle). The weapon pane
/// shows a button per spell slot (0x551660) and the charges gauge; a slot whose spell has item triggers shows disabled (its press
/// does nothing in the original, 0x57C040).
/// ARC-UI-053: while the interface bars show the original's spell pane in the bottom strip (<see cref="ListInStrip"/>, AreaSpellBarView),
/// this pane lists no spells: it shows only while the weapon button or the weapon's pane has something to show; ARC-UI-2304: and while the
/// strip draws the magick weapon button and pane too (AreaWeaponBarView), the app gives this pane no weapon, so it shows nothing.
/// </summary>
public partial class AreaSpellPane : CanvasLayer
{
    private PanelContainer _panel = null!;
    private Label _title = null!;
    private Button _weapon = null!;
    private HFlowContainer _buttons = null!;
    private IReadOnlyList<AreaSpellChoice> _shown = [];
    private AreaWeaponSpellPane? _weaponShown;
    private bool _open, _listInStrip;

    /// <summary>A spell button was pressed (the spell's number).</summary>
    public event Action<int>? Activated;

    /// <summary>ARC-UI-016: a button of the weapon's spell pane was pressed (the item's slot).</summary>
    public event Action<int>? ItemActivated;

    /// <summary>ARC-UI-016: the weapon button was pressed.</summary>
    public event Action? WeaponToggled;

    /// <summary>ARC-UI-027: a right press on a spell button (the spell's number) or on a slot of the weapon's pane (the slot): the
    /// original starts a drag onto the hotkey bar there (0x57E5D0).</summary>
    public event Action<int>? SpellDragStarted;
    public event Action<int>? ItemDragStarted;

    /// <summary>Whether the pane is open (M or the Spells button), drawn here or, for the spell list, in the bottom strip.</summary>
    public bool Shown => _open;

    /// <summary>ARC-UI-053: the bottom strip draws the spell list (the bars show with the pane's art): this pane leaves it out.</summary>
    public bool ListInStrip
    {
        get => _listInStrip;
        set
        {
            if (_listInStrip == value) return;
            _listInStrip = value;
            if (_open) Rebuild();
        }
    }

    internal bool DrawnForSelfTest => _panel.Visible;

    internal IReadOnlyList<AreaSpellChoice> ShownForSelfTest => _shown;

    /// <summary>ARC-UI-2604 smoke: the pane's rectangle in window pixels.</summary>
    internal Rect2 PanelRectForSelfTest => _panel.GetGlobalRect();

    internal AreaWeaponSpellPane? WeaponShownForSelfTest => _weaponShown;

    public override void _Ready()
    {
        Layer = 2;
        _panel = new AreaFallbackBox { Name = "SpellPane", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(14 / 255f, 16 / 255f, 22 / 255f, .88f),
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        AddChild(_panel);
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; _panel.AddChild(column);
        var header = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; column.AddChild(header);
        _title = new Label { MouseFilter = Control.MouseFilterEnum.Ignore }; header.AddChild(_title);
        _weapon = new Button { Text = "Weapon spells", FocusMode = Control.FocusModeEnum.None, Visible = false, ToggleMode = true };
        _weapon.Pressed += () => WeaponToggled?.Invoke();
        header.AddChild(_weapon);
        _buttons = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new(520, 0) }; column.AddChild(_buttons);
        GetViewport().SizeChanged += Place;
        AreaGameBarPanel.InsetsChanged += Place;
    }

    /// <summary>Shows the pane with <paramref name="spells"/> or, while it is open, the weapon's spell pane
    /// (<paramref name="weapon"/>; rebuilt only when what is shown changed).</summary>
    public void Show(IReadOnlyList<AreaSpellChoice> spells, AreaWeaponSpellPane? weapon = null)
    {
        ArgumentNullException.ThrowIfNull(spells);
        if (!_open || !_shown.SequenceEqual(spells) || !Equals(_weaponShown, weapon))
        {
            _shown = spells; _weaponShown = weapon; _open = true;
            Rebuild();
        }
    }

    private void Rebuild()
    {
        foreach (var child in _buttons.GetChildren()) child.QueueFree();
        var weapon = _weaponShown;
        _weapon.Visible = weapon is { ButtonShown: true } || weapon is { Open: true };
        _weapon.SetPressedNoSignal(weapon is { Open: true });
        _title.Visible = true;
        if (weapon is { Open: true } open) ShowWeapon(open);
        else if (!_listInStrip) ShowSpells(_shown);
        else _title.Visible = false;
        _buttons.Visible = weapon is { Open: true } || !_listInStrip;
        _panel.Visible = _open && (!_listInStrip || _weapon.Visible);
        Place();
    }

    private void ShowSpells(IReadOnlyList<AreaSpellChoice> spells)
    {
        _title.Text = spells.Count == 0 ? "Spells (M): no spell known" : "Spells (M): press a spell, then left-click a target; right click cancels";
        foreach (var choice in spells)
        {
            var spell = choice.Spell;
            var button = new Button
            {
                Text = choice.Cost is { } cost ? $"{choice.Name} ({cost})" : choice.Name, FocusMode = Control.FocusModeEnum.None,
            };
            AreaArtTip.Set(button, choice.College);
            button.Pressed += () => Activated?.Invoke(spell);
            button.GuiInput += input => RightPress(button, input, () => SpellDragStarted?.Invoke(spell));
            _buttons.AddChild(button);
        }
    }

    private void ShowWeapon(AreaWeaponSpellPane weapon)
    {
        _title.Text = weapon.Buttons.Count == 0 ? $"Weapon spells: none shown, {weapon.Gauge}" : $"Weapon spells: {weapon.Gauge}";
        foreach (var choice in weapon.Buttons)
        {
            var slot = choice.Slot;
            var button = new Button
            {
                Text = choice.Name, FocusMode = Control.FocusModeEnum.None, Disabled = !choice.Activates,
            };
            AreaArtTip.Set(button, choice.Activates ? $"slot {slot + 1}" : $"slot {slot + 1}: cast by the item's own events, not from the pane");
            button.Pressed += () => ItemActivated?.Invoke(slot);
            button.GuiInput += input => RightPress(button, input, () => ItemDragStarted?.Invoke(slot));
            _buttons.AddChild(button);
        }
    }

    public void HidePane() { _open = false; _panel.Hide(); }

    private static void RightPress(Control button, InputEvent input, Action started)
    {
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }) return;
        button.AcceptEvent();
        started();
    }

    /// <summary>ARC-UI-027 smoke: the centre of the shown button of <paramref name="spell"/> (null when none shows).</summary>
    internal Vector2? SpellButtonCentreForSelfTest(int spell)
    {
        var index = _shown.Select((choice, i) => (choice, i)).FirstOrDefault(pair => pair.choice.Spell == spell, (null!, -1)).Item2;
        return _weaponShown is { Open: true } || _listInStrip || index < 0 || index >= _buttons.GetChildCount() ? null : ((Control)_buttons.GetChild(index)).GetGlobalRect().GetCenter();
    }

    internal void PressForSelfTest(int spell) => Activated?.Invoke(spell);

    internal void PressWeaponForSelfTest() => WeaponToggled?.Invoke();

    internal void PressItemForSelfTest(int slot) => ItemActivated?.Invoke(slot);

    // ARC-UI-018: the bar panel's inset event is static; a freed panel must leave it.
    public override void _ExitTree() => AreaGameBarPanel.InsetsChanged -= Place;

    private void Place()
    {
        if (_panel is null) return;
        var size = GetViewport().GetVisibleRect().Size;
        _panel.ResetSize();
        var panel = _panel.GetCombinedMinimumSize();
        // ARC-UI-2604: from the left edge of the frame's edge rectangle (EdgeRect); without the layout it is the whole window, as before.
        // ARC-UI-2890: in the column mode it stands against the frame's left edge over the world, down to the frame's bottom (the strips are in the column).
        _panel.Position = new(AreaGameBarPanel.EdgeRect(size).Position.X + 12, Math.Max(12, AreaGameBarPanel.EdgeBottom(size) - panel.Y - 12));
    }
}

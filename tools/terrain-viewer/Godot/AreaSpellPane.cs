using Arcanum.TerrainViewer.Core;
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
/// </summary>
public partial class AreaSpellPane : CanvasLayer
{
    private PanelContainer _panel = null!;
    private Label _title = null!;
    private Button _weapon = null!;
    private HFlowContainer _buttons = null!;
    private IReadOnlyList<AreaSpellChoice> _shown = [];
    private AreaWeaponSpellPane? _weaponShown;

    /// <summary>A spell button was pressed (the spell's number).</summary>
    public event Action<int>? Activated;

    /// <summary>ARC-UI-016: a button of the weapon's spell pane was pressed (the item's slot).</summary>
    public event Action<int>? ItemActivated;

    /// <summary>ARC-UI-016: the weapon button was pressed.</summary>
    public event Action? WeaponToggled;

    public bool Shown => _panel.Visible;

    internal IReadOnlyList<AreaSpellChoice> ShownForSelfTest => _shown;

    internal AreaWeaponSpellPane? WeaponShownForSelfTest => _weaponShown;

    public override void _Ready()
    {
        Layer = 2;
        _panel = new PanelContainer { Name = "SpellPane", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
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
    }

    /// <summary>Shows the pane with <paramref name="spells"/> or, while it is open, the weapon's spell pane
    /// (<paramref name="weapon"/>; rebuilt only when what is shown changed).</summary>
    public void Show(IReadOnlyList<AreaSpellChoice> spells, AreaWeaponSpellPane? weapon = null)
    {
        ArgumentNullException.ThrowIfNull(spells);
        if (!_panel.Visible || !_shown.SequenceEqual(spells) || !Equals(_weaponShown, weapon))
        {
            _shown = spells; _weaponShown = weapon;
            foreach (var child in _buttons.GetChildren()) child.QueueFree();
            _weapon.Visible = weapon is { ButtonShown: true } || weapon is { Open: true };
            _weapon.SetPressedNoSignal(weapon is { Open: true });
            if (weapon is { Open: true } open) ShowWeapon(open);
            else ShowSpells(spells);
        }
        _panel.Show();
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
                TooltipText = choice.College,
            };
            button.Pressed += () => Activated?.Invoke(spell);
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
                TooltipText = choice.Activates ? $"slot {slot + 1}" : $"slot {slot + 1}: cast by the item's own events, not from the pane",
            };
            button.Pressed += () => ItemActivated?.Invoke(slot);
            _buttons.AddChild(button);
        }
    }

    public void HidePane() => _panel.Hide();

    internal void PressForSelfTest(int spell) => Activated?.Invoke(spell);

    internal void PressWeaponForSelfTest() => WeaponToggled?.Invoke();

    internal void PressItemForSelfTest(int slot) => ItemActivated?.Invoke(slot);

    private void Place()
    {
        if (_panel is null) return;
        var size = GetViewport().GetVisibleRect().Size;
        _panel.ResetSize();
        var panel = _panel.GetCombinedMinimumSize();
        _panel.Position = new(12, Math.Max(12, size.Y - panel.Y - 12 - AreaGameBarPanel.BottomInset));
    }
}

using Arcanum.Presentation.Hud;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-WORLD-046 follower portraits: one button per follower in the PC's array (the original's portrait list; waiting NPCs have none,
/// OL16), each opening the order menu of the original's portrait (walk, attack, stay close, spread out, back off, wait, inventory,
/// character sheet; the rule leaves wait out for a mind-controlled follower, which the viewer does not model yet). Inventory and character sheet are shown disabled (not
/// modelled for followers). A diagnostic strip: no portrait art. The menu is an inline panel of buttons that take no focus, not a
/// popup window: an embedded popup takes the world window's focus, which quarantines the world and refuses the choice.
/// ARC-UI-020 (ledger CH9): under each button a health bar (in its poisoned colour while the follower is poisoned) and a fatigue
/// bar, each cropped to the follower's percent (<see cref="LifeBarRules.PortraitFill"/>); below 20 % health the button turns red
/// (<see cref="LifeBarRules.PortraitTinted"/>), as the original adds red over the face. A follower without combat values shows
/// empty bars.
/// </summary>
public partial class AreaFollowerPortraits : CanvasLayer
{
    private VBoxContainer _list = null!;
    private PanelContainer _menu = null!;
    private VBoxContainer _menuItems = null!;
    private string? _menuEntity;
    private string _signature = "";
    private readonly Dictionary<string, (Button Button, AreaLifeBar Health, AreaLifeBar Fatigue)> _entries = new(StringComparer.Ordinal);
    /// <summary>The width of a portrait's bars, in pixels.</summary>
    public const int BarWidth = 96;
    private static readonly Color Tint = new(1f, .35f, .35f);

    /// <summary>A menu item chosen for a follower: its entity and the item.</summary>
    public event Action<string, FollowerMenuItem>? ItemChosen;

    internal IReadOnlyList<string> EntitiesForSelfTest { get; private set; } = [];
    private const string EntityMeta = "follower_entity";

    /// <summary>The screen centre of a follower's portrait button, or of an item of the open menu, for the native smoke's mouse
    /// presses (the layer has no transform).</summary>
    internal Vector2? PortraitCentreForSelfTest(string entity) =>
        _entries.TryGetValue(entity, out var entry) ? entry.Button.GetGlobalRect().GetCenter() : null;
    /// <summary>A follower portrait's bar fills, whether its health bar has the poisoned colour, and its red tint.</summary>
    internal (int Health, int Fatigue, bool Poisoned, bool Tinted)? BarsForSelfTest(string entity) =>
        _entries.TryGetValue(entity, out var entry)
            ? (entry.Health.Filled, entry.Fatigue.Filled, entry.Health.FillColor == AreaLifeBar.PoisonedHealthColor, entry.Button.SelfModulate == Tint)
            : null;
    internal Vector2? MenuItemCentreForSelfTest(FollowerMenuItem item) =>
        _menuItems.GetChildren().OfType<Button>().FirstOrDefault(b => b.HasMeta(ItemMeta) && (int)b.GetMeta(ItemMeta) == (int)item)?.GetGlobalRect().GetCenter();

    /// <summary>Whether the order menu is shown.</summary>
    public bool MenuShown => _menu.Visible;

    private const string ItemMeta = "follower_menu_item";

    public override void _Ready()
    {
        Layer = 3;
        var panel = new PanelContainer { Name = "FollowerPortraits", MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(14 / 255f, 16 / 255f, 22 / 255f, .85f),
            ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        AddChild(panel);
        _list = new VBoxContainer(); panel.AddChild(_list);
        _menu = new PanelContainer { Name = "FollowerOrders", MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        _menu.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(22 / 255f, 25 / 255f, 34 / 255f, .95f),
            ContentMarginLeft = 4, ContentMarginRight = 4, ContentMarginTop = 4, ContentMarginBottom = 4,
        });
        AddChild(_menu);
        _menuItems = new VBoxContainer(); _menu.AddChild(_menuItems);
        GetViewport().SizeChanged += () => { CloseMenu(); Place(); };
        AreaGameBarPanel.InsetsChanged += Place;
        Hide();
    }

    /// <summary>Shows one button per follower (entity, label, life) in array order, with its bars; hidden when there is none. The
    /// buttons are rebuilt only when the followers or their labels change; the bars follow every call.</summary>
    public void Show(IReadOnlyList<(string Entity, string Label, AreaLife? Life)> followers)
    {
        var signature = string.Join("\n", followers.Select(f => f.Entity + "\t" + f.Label));
        if (signature != _signature)
        {
            _signature = signature;
            // Removed at once so the panel's minimum size no longer counts them when it is placed below.
            foreach (var child in _list.GetChildren()) { _list.RemoveChild(child); child.QueueFree(); }
            _entries.Clear();
            EntitiesForSelfTest = followers.Select(f => f.Entity).ToArray();
            if (_menuEntity is { } open && !EntitiesForSelfTest.Contains(open)) CloseMenu();
            foreach (var (entity, label, _) in followers)
            {
                var entry = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
                entry.AddThemeConstantOverride("separation", 2);
                var button = new Button { Text = label + " ▸", Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None };
                button.Pressed += () => OpenMenu(entity, button);
                button.SetMeta(EntityMeta, entity);
                var health = new AreaLifeBar { Name = "Health", Length = BarWidth, Thickness = 4 };
                var fatigue = new AreaLifeBar { Name = "Fatigue", Length = BarWidth, Thickness = 4 };
                entry.AddChild(button); entry.AddChild(health); entry.AddChild(fatigue);
                _list.AddChild(entry);
                _entries[entity] = (button, health, fatigue);
            }
            Visible = followers.Count > 0;
            Place();
        }
        foreach (var (entity, _, life) in followers)
        {
            var (button, health, fatigue) = _entries[entity];
            var value = life ?? default;
            health.SetFill(LifeBarRules.PortraitFill(value.HealthPercent, BarWidth), value.Poisoned ? AreaLifeBar.PoisonedHealthColor : AreaLifeBar.HealthColor);
            fatigue.SetFill(LifeBarRules.PortraitFill(value.FatiguePercent, BarWidth), AreaLifeBar.FatigueColor);
            button.SelfModulate = life is not null && LifeBarRules.PortraitTinted(value.Health, value.MaxHealth) ? Tint : Colors.White;
        }
    }

    /// <summary>Opens the order menu of a follower next to its portrait button (a second press of the same portrait closes it).</summary>
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
        var wanted = anchor is null ? new Vector2(24, 24) : anchor.GetGlobalRect().Position + new Vector2(anchor.Size.X + 4, 0);
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

    // ARC-UI-018: the bar panel's inset event is static; a freed panel must leave it.
    public override void _ExitTree() => AreaGameBarPanel.InsetsChanged -= Place;

    private void Place()
    {
        if (_list?.GetParent() is not PanelContainer panel) return;
        var size = GetViewport().GetVisibleRect().Size;
        panel.ResetSize();
        panel.Position = new(12, Math.Max(12, size.Y - panel.GetCombinedMinimumSize().Y - 12 - AreaGameBarPanel.BottomInset));
    }
}
